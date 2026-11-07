using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using StardewModdingAPI;

namespace JunimoJamsRusted.Services.Platform
{
    public enum MprisCommandType
    {
        PlayPause,
        Next,
        Previous
    }

    /// <summary>
    /// Fully non-blocking, zero-stall MPRIS2 client for Valve Proton and Native Linux.
    /// Runs all socket operations, D-Bus Wire Protocol exchanges, and reconnects on a
    /// dedicated background thread. Main thread calls are strictly lock-free memory reads.
    /// Plug-and-play: zero external packages, zero wine commands, zero tools to install.
    /// </summary>
    public sealed class AsyncProtonMprisClient : IMusicPlayer, IDisposable
    {
        private readonly string[] _priorityPlayers;
        private readonly Action<string, LogLevel>? _logger;
        private readonly Thread _workerThread;
        private readonly CancellationTokenSource _cts = new();
        private readonly ConcurrentQueue<MprisCommandType> _commandQueue = new();

        // Lock-free atomic reference read by Game1.Update / UpdateTicked
        private volatile TrackInfo _latestTrack = new() { Name = "Not Running", Artist = "Music Player", IsPlaying = false };

        // Internal background state (owned strictly by the background worker thread)
        private Socket? _socket;
        private uint _serial = 1;
        private string? _activePlayer;
        private readonly byte[] _readBuffer = new byte[65536];
        private DateTime _lastPollTime = DateTime.MinValue;

        public AsyncProtonMprisClient(string[]? extraPlayers = null, Action<string, LogLevel>? logger = null)
        {
            this._logger = logger;

            var players = new List<string>
            {
                "bitchord", "spotify", "tidal-hifi", "cider", "deezer",
                "plexamp", "nuclear", "clementine", "strawberry", "elisa",
                "lollypop", "vlc", "rhythmbox", "audacious", "deadbeef",
                "chromium", "firefox", "brave", "chrome"
            };

            if (extraPlayers != null)
            {
                foreach (string p in extraPlayers)
                {
                    string? lower = p?.Trim().ToLowerInvariant();
                    if (!string.IsNullOrEmpty(lower) && !players.Contains(lower))
                        players.Add(lower);
                }
            }

            this._priorityPlayers = players.ToArray();

            // Start dedicated background worker thread
            this._workerThread = new Thread(this.WorkerLoop)
            {
                Name = "JunimoJams_MPRIS_Worker",
                IsBackground = true,
                Priority = ThreadPriority.BelowNormal // Never steal CPU cycles from game render loop
            };
            this._workerThread.Start();
        }

        private void Log(string message, LogLevel level = LogLevel.Debug)
        {
            this._logger?.Invoke("[JunimoJams MPRIS] " + message, level);
        }

        #region IMusicPlayer Interface (Zero-Stall, Called by Main Game Thread)

        public TrackInfo GetCurrentTrack() => this._latestTrack;

        public bool IsPlayerRunning() => this._latestTrack.Name != "Not Running" && this._latestTrack.Name != "Error";

        public void TogglePlayPause() => this._commandQueue.Enqueue(MprisCommandType.PlayPause);
        public void NextTrack() => this._commandQueue.Enqueue(MprisCommandType.Next);
        public void PreviousTrack() => this._commandQueue.Enqueue(MprisCommandType.Previous);

        #endregion

        #region Background Worker Thread Loop

        private void WorkerLoop()
        {
            this.Log("Background MPRIS worker thread started.", LogLevel.Trace);

            while (!this._cts.IsCancellationRequested)
            {
                try
                {
                    if (this._socket == null || !this._socket.Connected)
                    {
                        if (!this.TryConnect())
                        {
                            // If direct socket failed, attempt fallback host process bridge
                            if (this.TryFallbackHostBridge())
                            {
                                this.ProcessPendingCommandsFallback();
                                Thread.Sleep(1000);
                                continue;
                            }

                            Thread.Sleep(2000);
                            continue;
                        }
                    }

                    // Process pending commands from main thread (fire-and-forget NO_REPLY_EXPECTED)
                    this.ProcessPendingCommands();

                    // Check if socket has incoming data (signals or replies) with 250ms select timeout
                    if (this._socket != null && this._socket.Poll(250_000, SelectMode.SelectRead))
                    {
                        if (!this.ReceiveAndProcessMessages())
                        {
                            this.Log("Socket disconnect or error detected during polling. Closing.", LogLevel.Debug);
                            this.CloseSocket();
                            continue;
                        }
                    }

                    // Periodic heartbeat / polling (every 1.5 seconds) to keep track position & status refreshed
                    if ((DateTime.UtcNow - this._lastPollTime).TotalMilliseconds >= 1500)
                    {
                        this._lastPollTime = DateTime.UtcNow;
                        this.RefreshPlayerState();
                    }
                }
                catch (ThreadAbortException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    this.Log($"Unexpected exception in MPRIS worker loop: {ex.Message}", LogLevel.Warn);
                    this.CloseSocket();
                    Thread.Sleep(1500);
                }
            }

            this.CloseSocket();
            this.Log("Background MPRIS worker thread stopped.", LogLevel.Trace);
        }

        private bool TryConnect()
        {
            this.CloseSocket();

            List<string> candidates = GetCandidateSocketPaths();
            foreach (string path in candidates)
            {
                try
                {
                    this.Log($"Trying candidate D-Bus socket: '{path}'...", LogLevel.Trace);

                    var endpoint = new UnixDomainSocketEndPoint(path);
                    var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified)
                    {
                        ReceiveTimeout = 2000,
                        SendTimeout = 2000,
                        Blocking = true
                    };

                    socket.Connect(endpoint);
                    this.Log($"Connected to socket: '{path}'. Starting SASL authentication...", LogLevel.Info);

                    // SASL EXTERNAL Handshake:
                    // 1. Send \0AUTH EXTERNAL 31303030\r\n (hex 1000)
                    byte[] auth = Encoding.ASCII.GetBytes("\0AUTH EXTERNAL 31303030\r\n");
                    socket.Send(auth);

                    byte[] authBuf = new byte[256];
                    int read = socket.Receive(authBuf);
                    string authReply = Encoding.ASCII.GetString(authBuf, 0, read);
                    this.Log($"SASL reply 1: {authReply.Trim()}", LogLevel.Trace);

                    if (!authReply.StartsWith("OK"))
                    {
                        // Fallback: send \0AUTH EXTERNAL\r\n without credentials to let kernel verify SO_PEERCRED
                        socket.Send(Encoding.ASCII.GetBytes("AUTH EXTERNAL\r\n"));
                        read = socket.Receive(authBuf);
                        authReply = Encoding.ASCII.GetString(authBuf, 0, read);
                        this.Log($"SASL reply 2: {authReply.Trim()}", LogLevel.Trace);
                    }

                    if (!authReply.StartsWith("OK"))
                    {
                        this.Log($"SASL authentication failed on socket: '{path}'", LogLevel.Warn);
                        socket.Dispose();
                        continue;
                    }

                    // Complete SASL exchange
                    socket.Send(Encoding.ASCII.GetBytes("BEGIN\r\n"));

                    this._socket = socket;

                    // Register with bus via Hello
                    this.SendMethodCall("org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus", "Hello", null, Array.Empty<byte>());
                    byte[]? helloReply = this.ReceiveMethodReply();
                    if (helloReply != null)
                    {
                        string assignedName = ExtractStringFromBody(helloReply);
                        this.Log($"D-Bus Hello succeeded! Assigned bus name: {assignedName}", LogLevel.Info);
                    }
                    else
                    {
                        this.Log("D-Bus Hello received no reply; proceeding with caution.", LogLevel.Warn);
                    }

                    // Subscribe to MPRIS PropertiesChanged signals & NameOwnerChanged signals
                    this.AddMatchRule("type='signal',interface='org.freedesktop.DBus.Properties',member='PropertiesChanged',path='/org/mpris/MediaPlayer2'");
                    this.AddMatchRule("type='signal',sender='org.freedesktop.DBus',interface='org.freedesktop.DBus',member='NameOwnerChanged'");

                    // Initial player scan and immediate refresh
                    this.DetectActivePlayer();
                    if (!string.IsNullOrEmpty(this._activePlayer))
                    {
                        this.RefreshPlayerState();
                    }

                    return true;
                }
                catch (Exception ex)
                {
                    this.Log($"Failed to connect via socket '{path}': {ex.Message}", LogLevel.Trace);
                }
            }

            return false;
        }

        private void CloseSocket()
        {
            try
            {
                this._socket?.Shutdown(SocketShutdown.Both);
            }
            catch { }
            try
            {
                this._socket?.Dispose();
            }
            catch { }

            this._socket = null;
            this._activePlayer = null;
        }

        private static List<string> GetCandidateSocketPaths()
        {
            var list = new List<string>();

            void AddIfValid(string? p)
            {
                if (!string.IsNullOrWhiteSpace(p) && !list.Contains(p))
                    list.Add(p);
            }

            // 1. DBUS_SESSION_BUS_ADDRESS environment variable
            string? dbusEnv = Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS");
            if (!string.IsNullOrEmpty(dbusEnv))
            {
                // Format: unix:path=/run/user/1000/bus,...
                int pIdx = dbusEnv.IndexOf("path=", StringComparison.OrdinalIgnoreCase);
                if (pIdx != -1)
                {
                    int end = dbusEnv.IndexOf(',', pIdx);
                    if (end == -1) end = dbusEnv.Length;
                    string rawPath = dbusEnv.Substring(pIdx + 5, end - (pIdx + 5)).Trim();

                    AddIfValid(rawPath);
                    if (rawPath.StartsWith("/"))
                        AddIfValid(@"Z:" + rawPath.Replace('/', '\\'));
                }
            }

            // 2. Standard user 1000 bus paths (Host systemd default)
            AddIfValid(@"Z:\run\user\1000\bus");
            AddIfValid("/run/user/1000/bus");

            // 3. XDG_RUNTIME_DIR variable
            string? xdg = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
            if (!string.IsNullOrEmpty(xdg))
            {
                AddIfValid(Path.Combine(xdg, "bus"));
                if (xdg.StartsWith("/"))
                    AddIfValid(@"Z:" + xdg.Replace('/', '\\').TrimEnd('\\') + @"\bus");
            }

            // 4. Dynamic UID scanning under Z:\run\user\
            string[] baseDirs = new[] { @"Z:\run\user", "/run/user" };
            foreach (string baseDir in baseDirs)
            {
                try
                {
                    if (Directory.Exists(baseDir))
                    {
                        foreach (string userDir in Directory.GetDirectories(baseDir))
                        {
                            AddIfValid(Path.Combine(userDir, "bus"));
                            if (userDir.StartsWith("Z:", StringComparison.OrdinalIgnoreCase))
                            {
                                string posix = userDir.Substring(2).Replace('\\', '/') + "/bus";
                                AddIfValid(posix);
                            }
                        }
                    }
                }
                catch { }
            }

            // 5. Fallback paths
            AddIfValid(@"Z:\tmp\dbus-session");
            AddIfValid("/tmp/dbus-session");

            return list;
        }

        #endregion

        #region Command Processing & Signal Dispatching

        private void ProcessPendingCommands()
        {
            if (this._socket == null || string.IsNullOrEmpty(this._activePlayer))
                return;

            while (this._commandQueue.TryDequeue(out MprisCommandType cmd))
            {
                string member = cmd switch
                {
                    MprisCommandType.PlayPause => "PlayPause",
                    MprisCommandType.Next => "Next",
                    MprisCommandType.Previous => "Previous",
                    _ => "PlayPause"
                };

                this.Log($"Sending MPRIS command '{member}' to {this._activePlayer}...", LogLevel.Debug);

                // Flags: 0x01 = NO_REPLY_EXPECTED (Fire and forget! No waiting for reply packet)
                byte[] msg = BuildDbusMessage(
                    dest: this._activePlayer,
                    path: "/org/mpris/MediaPlayer2",
                    iface: "org.mpris.MediaPlayer2.Player",
                    member: member,
                    sig: null,
                    body: Array.Empty<byte>(),
                    serial: this._serial++,
                    flags: 0x01 // NO_REPLY_EXPECTED
                );

                try
                {
                    this._socket.Send(msg);
                }
                catch (Exception ex)
                {
                    this.Log($"Failed to send command '{member}': {ex.Message}", LogLevel.Warn);
                    this.CloseSocket();
                    break;
                }
            }
        }

        private void RefreshPlayerState()
        {
            if (this._socket == null) return;

            if (string.IsNullOrEmpty(this._activePlayer))
            {
                this.DetectActivePlayer();
                if (string.IsNullOrEmpty(this._activePlayer))
                {
                    this._latestTrack = new TrackInfo { Name = "Not Running", Artist = "Music Player", IsPlaying = false };
                    return;
                }
            }

            try
            {
                // Query PlaybackStatus
                this.SendMethodCall(this._activePlayer, "/org/mpris/MediaPlayer2", "org.freedesktop.DBus.Properties", "Get", "ss",
                    EncodeStringPropertyArgs("org.mpris.MediaPlayer2.Player", "PlaybackStatus"));
                byte[]? statusReply = this.ReceiveMethodReply();

                bool isPlaying = false;
                if (statusReply != null)
                {
                    string status = ExtractVariantString(statusReply);
                    isPlaying = string.Equals(status, "Playing", StringComparison.OrdinalIgnoreCase);
                }

                // Query Metadata
                this.SendMethodCall(this._activePlayer, "/org/mpris/MediaPlayer2", "org.freedesktop.DBus.Properties", "Get", "ss",
                    EncodeStringPropertyArgs("org.mpris.MediaPlayer2.Player", "Metadata"));
                byte[]? metaReply = this.ReceiveMethodReply();

                string title = "Unknown";
                string artist = "Unknown";
                string album = string.Empty;
                string? artUrl = null;
                TimeSpan duration = TimeSpan.Zero;

                if (metaReply != null)
                {
                    var dict = ParseMetadataDict(metaReply);
                    if (dict.TryGetValue("xesam:title", out object? t) && t is string tStr && !string.IsNullOrWhiteSpace(tStr))
                        title = tStr;
                    if (dict.TryGetValue("xesam:artist", out object? a) && a is string aStr && !string.IsNullOrWhiteSpace(aStr))
                        artist = aStr;
                    if (dict.TryGetValue("xesam:album", out object? al) && al is string alStr)
                        album = alStr;
                    if (dict.TryGetValue("mpris:artUrl", out object? u) && u is string uStr)
                        artUrl = uStr;
                    if (dict.TryGetValue("mpris:length", out object? l) && l is long lMicro && lMicro > 0)
                        duration = TimeSpan.FromMilliseconds(lMicro / 1000.0);
                }

                // Query Position
                TimeSpan position = TimeSpan.Zero;
                this.SendMethodCall(this._activePlayer, "/org/mpris/MediaPlayer2", "org.freedesktop.DBus.Properties", "Get", "ss",
                    EncodeStringPropertyArgs("org.mpris.MediaPlayer2.Player", "Position"));
                byte[]? posReply = this.ReceiveMethodReply();
                if (posReply != null)
                {
                    long posMicro = ExtractVariantInt64(posReply);
                    if (posMicro >= 0)
                        position = TimeSpan.FromMilliseconds(posMicro / 1000.0);
                }

                // Log change if title or playback state changed
                if (this._latestTrack.Name != title || this._latestTrack.IsPlaying != isPlaying)
                {
                    this.Log($"Active Track: '{title}' by '{artist}' [{(isPlaying ? "Playing" : "Paused")}]", LogLevel.Info);
                }

                // Atomically update snapshot for GameLoop.UpdateTicked
                this._latestTrack = new TrackInfo
                {
                    Name = title,
                    Artist = artist,
                    Album = album,
                    IsPlaying = isPlaying,
                    Duration = duration,
                    Position = position,
                    PlaybackRate = isPlaying ? 1.0f : 0.0f,
                    LastPositionUpdateTime = DateTime.UtcNow,
                    DirectArtUri = artUrl
                };
            }
            catch (Exception ex)
            {
                this.Log($"Error refreshing player state: {ex.Message}", LogLevel.Debug);
                this.CloseSocket();
            }
        }

        private void DetectActivePlayer()
        {
            try
            {
                this.SendMethodCall("org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus", "ListNames", null, Array.Empty<byte>());
                byte[]? reply = this.ReceiveMethodReply();
                if (reply == null) return;

                string raw = Encoding.UTF8.GetString(reply);
                var activeNames = new List<string>();

                int idx = 0;
                while ((idx = raw.IndexOf("org.mpris.MediaPlayer2.", idx, StringComparison.Ordinal)) != -1)
                {
                    int end = raw.IndexOf('\0', idx);
                    if (end == -1) end = raw.Length;
                    string name = raw.Substring(idx, end - idx);
                    if (!activeNames.Contains(name))
                        activeNames.Add(name);
                    idx = end + 1;
                }

                if (activeNames.Count > 0)
                {
                    this.Log($"Found {activeNames.Count} MPRIS players: {string.Join(", ", activeNames)}", LogLevel.Info);
                }

                // Match against priority list (bitchord, spotify, etc.)
                foreach (string priority in this._priorityPlayers)
                {
                    string target = $"org.mpris.MediaPlayer2.{priority}";
                    foreach (string name in activeNames)
                    {
                        if (name.StartsWith(target, StringComparison.OrdinalIgnoreCase))
                        {
                            this._activePlayer = name;
                            this.Log($"Selected active player by priority: {name}", LogLevel.Info);
                            return;
                        }
                    }
                }

                this._activePlayer = activeNames.Count > 0 ? activeNames[0] : null;
                if (this._activePlayer != null)
                {
                    this.Log($"Selected default active player: {this._activePlayer}", LogLevel.Info);
                }
            }
            catch (Exception ex)
            {
                this.Log($"Error detecting active player: {ex.Message}", LogLevel.Debug);
                this.CloseSocket();
            }
        }

        private void AddMatchRule(string rule)
        {
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            byte[] ruleBytes = Encoding.UTF8.GetBytes(rule);
            w.Write((uint)ruleBytes.Length);
            w.Write(ruleBytes);
            w.Write((byte)0);

            this.SendMethodCall("org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus", "AddMatch", "s", ms.ToArray());
            this.ReceiveMethodReply();
        }

        private bool ReceiveAndProcessMessages()
        {
            if (this._socket == null) return false;

            byte[]? msg = this.ReceiveSingleMessage();
            if (msg == null) return false;

            byte msgType = msg[1];
            if (msgType == 4) // SIGNAL (PropertiesChanged or NameOwnerChanged)
            {
                this.RefreshPlayerState();
            }

            return true;
        }

        private byte[]? ReceiveMethodReply()
        {
            if (this._socket == null) return null;

            // Wait for METHOD_RETURN (type 2) or ERROR (type 3). If signals arrive while waiting, process them.
            for (int attempt = 0; attempt < 5; attempt++)
            {
                byte[]? msg = this.ReceiveSingleMessage();
                if (msg == null) return null;

                byte msgType = msg[1];
                if (msgType == 2) // METHOD_RETURN
                {
                    return msg;
                }
                else if (msgType == 3) // ERROR
                {
                    this.Log("D-Bus method returned error message.", LogLevel.Debug);
                    return null;
                }
                else if (msgType == 4) // SIGNAL
                {
                    // Signal arrived while waiting for reply; handle it and keep waiting
                    this.RefreshPlayerState();
                }
            }

            return null;
        }

        private byte[]? ReceiveSingleMessage()
        {
            if (this._socket == null) return null;

            // 1. Read fixed 16-byte header
            if (!ReadExact(this._socket, this._readBuffer, 0, 16))
                return null;

            uint bodyLen = BitConverter.ToUInt32(this._readBuffer, 4);
            uint fieldsLen = BitConverter.ToUInt32(this._readBuffer, 12);
            int headerLen = 16 + (int)fieldsLen;
            int rem = headerLen % 8;
            if (rem != 0) headerLen += (8 - rem);

            int totalExpected = headerLen + (int)bodyLen;
            if (totalExpected > this._readBuffer.Length)
            {
                this.Log($"D-Bus message too large ({totalExpected} bytes).", LogLevel.Warn);
                return null;
            }

            // 2. Read remaining message bytes
            int remaining = totalExpected - 16;
            if (remaining > 0)
            {
                if (!ReadExact(this._socket, this._readBuffer, 16, remaining))
                    return null;
            }

            byte[] fullMsg = new byte[totalExpected];
            Buffer.BlockCopy(this._readBuffer, 0, fullMsg, 0, totalExpected);
            return fullMsg;
        }

        private static bool ReadExact(Socket socket, byte[] buffer, int offset, int count)
        {
            int total = 0;
            while (total < count)
            {
                try
                {
                    int read = socket.Receive(buffer, offset + total, count - total, SocketFlags.None);
                    if (read <= 0) return false;
                    total += read;
                }
                catch
                {
                    return false;
                }
            }
            return true;
        }

        private void SendMethodCall(string dest, string path, string iface, string member, string? sig, byte[] body)
        {
            if (this._socket == null) return;
            uint serial = this._serial++;
            byte[] msg = BuildDbusMessage(dest, path, iface, member, sig, body, serial, flags: 0);
            try
            {
                this._socket.Send(msg);
            }
            catch (Exception ex)
            {
                this.Log($"SendMethodCall '{member}' failed: {ex.Message}", LogLevel.Debug);
                this.CloseSocket();
            }
        }

        #endregion

        #region Fallback Host Process Bridge (When AF_UNIX Direct Socket Is Blocked)

        private bool TryFallbackHostBridge()
        {
            try
            {
                // Query active player via busctl or playerctl on host
                string busName = this.ExecuteHostCommand("busctl --user list | grep -i mpris");
                if (string.IsNullOrWhiteSpace(busName))
                    return false;

                string? active = null;
                foreach (string priority in this._priorityPlayers)
                {
                    if (busName.Contains($"org.mpris.MediaPlayer2.{priority}", StringComparison.OrdinalIgnoreCase))
                    {
                        active = $"org.mpris.MediaPlayer2.{priority}";
                        break;
                    }
                }

                if (active == null)
                {
                    int mIdx = busName.IndexOf("org.mpris.MediaPlayer2.", StringComparison.Ordinal);
                    if (mIdx != -1)
                    {
                        int end = busName.IndexOfAny(new[] { ' ', '\t', '\r', '\n' }, mIdx);
                        if (end == -1) end = busName.Length;
                        active = busName.Substring(mIdx, end - mIdx).Trim();
                    }
                }

                if (string.IsNullOrEmpty(active))
                    return false;

                this._activePlayer = active;

                // Query Status
                string statusOut = this.ExecuteHostCommand($"busctl --user get-property {active} /org/mpris/MediaPlayer2 org.mpris.MediaPlayer2.Player PlaybackStatus");
                bool isPlaying = statusOut.Contains("\"Playing\"", StringComparison.OrdinalIgnoreCase);

                // Query Metadata
                string metaOut = this.ExecuteHostCommand($"busctl --user get-property {active} /org/mpris/MediaPlayer2 org.mpris.MediaPlayer2.Player Metadata");

                string title = "Unknown";
                string artist = "Unknown";
                string? artUrl = null;
                TimeSpan duration = TimeSpan.Zero;

                if (!string.IsNullOrWhiteSpace(metaOut))
                {
                    title = ExtractRegexProperty(metaOut, "xesam:title") ?? "Unknown";
                    artist = ExtractRegexProperty(metaOut, "xesam:artist") ?? "Unknown";
                    artUrl = ExtractRegexProperty(metaOut, "mpris:artUrl");

                    string? lenStr = ExtractRegexProperty(metaOut, "mpris:length");
                    if (long.TryParse(lenStr, out long lenMicro) && lenMicro > 0)
                    {
                        duration = TimeSpan.FromMilliseconds(lenMicro / 1000.0);
                    }
                }

                this._latestTrack = new TrackInfo
                {
                    Name = title,
                    Artist = artist,
                    IsPlaying = isPlaying,
                    Duration = duration,
                    Position = TimeSpan.Zero,
                    PlaybackRate = isPlaying ? 1.0f : 0.0f,
                    LastPositionUpdateTime = DateTime.UtcNow,
                    DirectArtUri = artUrl
                };

                return true;
            }
            catch
            {
                return false;
            }
        }

        private void ProcessPendingCommandsFallback()
        {
            if (string.IsNullOrEmpty(this._activePlayer)) return;

            while (this._commandQueue.TryDequeue(out MprisCommandType cmd))
            {
                string member = cmd switch
                {
                    MprisCommandType.PlayPause => "PlayPause",
                    MprisCommandType.Next => "Next",
                    MprisCommandType.Previous => "Previous",
                    _ => "PlayPause"
                };

                this.ExecuteHostCommand($"busctl --user call {this._activePlayer} /org/mpris/MediaPlayer2 org.mpris.MediaPlayer2.Player {member}");
            }
        }

        private string ExecuteHostCommand(string linuxCommand)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c start /unix {linuxCommand}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var proc = Process.Start(psi);
                if (proc == null) return string.Empty;

                string result = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit(500);
                return result;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string? ExtractRegexProperty(string text, string propKey)
        {
            int idx = text.IndexOf(propKey, StringComparison.OrdinalIgnoreCase);
            if (idx == -1) return null;

            int quoteStart = text.IndexOf('"', idx + propKey.Length);
            if (quoteStart == -1) return null;

            int quoteEnd = text.IndexOf('"', quoteStart + 1);
            if (quoteEnd == -1) return null;

            return text.Substring(quoteStart + 1, quoteEnd - quoteStart - 1);
        }

        #endregion

        #region D-Bus Wire Protocol Serialization

        private static byte[] BuildDbusMessage(string dest, string path, string iface, string member, string? sig, byte[] body, uint serial, byte flags)
        {
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);

            using var fMs = new MemoryStream();
            using var fW = new BinaryWriter(fMs);

            WriteField(fW, 1, "o", path);
            WriteField(fW, 6, "s", dest);
            WriteField(fW, 2, "s", iface);
            WriteField(fW, 3, "s", member);
            if (!string.IsNullOrEmpty(sig))
                WriteField(fW, 8, "g", sig);

            byte[] fieldsBytes = fMs.ToArray();

            w.Write((byte)'l');
            w.Write((byte)1);
            w.Write(flags);
            w.Write((byte)1);
            w.Write((uint)body.Length);
            w.Write(serial);
            w.Write((uint)fieldsBytes.Length);
            w.Write(fieldsBytes);

            PadTo(w, 8);
            if (body.Length > 0)
                w.Write(body);

            return ms.ToArray();
        }

        private static void WriteField(BinaryWriter w, byte code, string sig, string val)
        {
            PadTo(w, 8);
            w.Write(code);
            w.Write((byte)sig.Length);
            w.Write(Encoding.ASCII.GetBytes(sig));
            w.Write((byte)0);

            if (sig == "s" || sig == "o")
            {
                PadTo(w, 4);
                byte[] bytes = Encoding.UTF8.GetBytes(val);
                w.Write((uint)bytes.Length);
                w.Write(bytes);
                w.Write((byte)0);
            }
            else if (sig == "g")
            {
                byte[] bytes = Encoding.ASCII.GetBytes(val);
                w.Write((byte)bytes.Length);
                w.Write(bytes);
                w.Write((byte)0);
            }
        }

        private static void PadTo(BinaryWriter w, int align)
        {
            int rem = (int)(w.BaseStream.Position % align);
            if (rem != 0)
            {
                int count = align - rem;
                for (int i = 0; i < count; i++) w.Write((byte)0);
            }
        }

        private static byte[] EncodeStringPropertyArgs(string iface, string prop)
        {
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);

            byte[] b1 = Encoding.UTF8.GetBytes(iface);
            w.Write((uint)b1.Length);
            w.Write(b1);
            w.Write((byte)0);

            PadTo(w, 4);
            byte[] b2 = Encoding.UTF8.GetBytes(prop);
            w.Write((uint)b2.Length);
            w.Write(b2);
            w.Write((byte)0);

            return ms.ToArray();
        }

        private static string ExtractStringFromBody(byte[] b)
        {
            int bodyStart = GetBodyStart(b);
            if (bodyStart + 4 > b.Length) return string.Empty;

            uint len = BitConverter.ToUInt32(b, bodyStart);
            if (bodyStart + 4 + len > b.Length) return string.Empty;

            return Encoding.UTF8.GetString(b, bodyStart + 4, (int)len);
        }

        private static string ExtractVariantString(byte[] b)
        {
            int bodyStart = GetBodyStart(b);
            if (bodyStart >= b.Length) return string.Empty;

            int sigLen = b[bodyStart];
            int pos = bodyStart + 1 + sigLen + 1;
            int rem = pos % 4;
            if (rem != 0) pos += (4 - rem);

            if (pos + 4 > b.Length) return string.Empty;
            uint sLen = BitConverter.ToUInt32(b, pos);
            pos += 4;
            if (pos + sLen > b.Length) return string.Empty;

            return Encoding.UTF8.GetString(b, pos, (int)sLen);
        }

        private static long ExtractVariantInt64(byte[] b)
        {
            int bodyStart = GetBodyStart(b);
            if (bodyStart >= b.Length) return 0;

            int sigLen = b[bodyStart];
            int pos = bodyStart + 1 + sigLen + 1;
            int rem = pos % 8;
            if (rem != 0) pos += (8 - rem);

            if (pos + 8 > b.Length) return 0;
            return BitConverter.ToInt64(b, pos);
        }

        private static Dictionary<string, object> ParseMetadataDict(byte[] b)
        {
            var dict = new Dictionary<string, object>();
            int bodyStart = GetBodyStart(b);
            if (bodyStart >= b.Length) return dict;

            int pos = bodyStart;
            int sigLen = b[pos];
            pos += 1 + sigLen + 1;

            int rem = pos % 4;
            if (rem != 0) pos += (4 - rem);
            if (pos + 4 > b.Length) return dict;

            uint arrLen = BitConverter.ToUInt32(b, pos);
            pos += 4;
            int endPos = pos + (int)arrLen;

            while (pos < endPos && pos + 8 <= b.Length)
            {
                rem = pos % 8;
                if (rem != 0) pos += (8 - rem);
                if (pos >= endPos) break;

                uint kLen = BitConverter.ToUInt32(b, pos);
                pos += 4;
                if (pos + kLen > b.Length) break;
                string key = Encoding.UTF8.GetString(b, pos, (int)kLen);
                pos += (int)kLen + 1;

                if (pos >= b.Length) break;
                int vSigLen = b[pos++];
                if (pos + vSigLen > b.Length) break;
                string vSig = Encoding.ASCII.GetString(b, pos, vSigLen);
                pos += vSigLen + 1;

                if (vSig == "s")
                {
                    rem = pos % 4;
                    if (rem != 0) pos += (4 - rem);
                    if (pos + 4 > b.Length) break;
                    uint valLen = BitConverter.ToUInt32(b, pos);
                    pos += 4;
                    if (pos + valLen <= b.Length)
                    {
                        dict[key] = Encoding.UTF8.GetString(b, pos, (int)valLen);
                        pos += (int)valLen + 1;
                    }
                }
                else if (vSig == "x")
                {
                    rem = pos % 8;
                    if (rem != 0) pos += (8 - rem);
                    if (pos + 8 > b.Length) break;
                    dict[key] = BitConverter.ToInt64(b, pos);
                    pos += 8;
                }
                else if (vSig == "as")
                {
                    rem = pos % 4;
                    if (rem != 0) pos += (4 - rem);
                    if (pos + 4 > b.Length) break;
                    uint asLen = BitConverter.ToUInt32(b, pos);
                    pos += 4;
                    int asEnd = pos + (int)asLen;
                    var strList = new List<string>();
                    while (pos < asEnd && pos + 4 <= b.Length)
                    {
                        rem = pos % 4;
                        if (rem != 0) pos += (4 - rem);
                        if (pos + 4 > asEnd) break;
                        uint itemLen = BitConverter.ToUInt32(b, pos);
                        pos += 4;
                        if (pos + itemLen <= b.Length)
                        {
                            strList.Add(Encoding.UTF8.GetString(b, pos, (int)itemLen));
                            pos += (int)itemLen + 1;
                        }
                    }
                    dict[key] = string.Join(", ", strList);
                }
                else
                {
                    pos += 4;
                }
            }

            return dict;
        }

        private static int GetBodyStart(byte[] b)
        {
            if (b.Length < 16) return b.Length;
            uint fieldsLen = BitConverter.ToUInt32(b, 12);
            int headerLen = 16 + (int)fieldsLen;
            int rem = headerLen % 8;
            if (rem != 0) headerLen += (8 - rem);
            return headerLen;
        }

        #endregion

        public void Dispose()
        {
            this._cts.Cancel();
            try
            {
                if (this._workerThread.IsAlive)
                    this._workerThread.Join(500);
            }
            catch { }

            this.CloseSocket();
            this._cts.Dispose();
        }
    }
}