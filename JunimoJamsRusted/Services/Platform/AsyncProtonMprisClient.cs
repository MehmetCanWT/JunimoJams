using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;

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

        public AsyncProtonMprisClient(string[]? extraPlayers = null)
        {
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
            while (!this._cts.IsCancellationRequested)
            {
                try
                {
                    if (this._socket == null || !this._socket.Connected)
                    {
                        if (!this.TryConnect())
                        {
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
                catch
                {
                    this.CloseSocket();
                    Thread.Sleep(1500);
                }
            }

            this.CloseSocket();
        }

        private bool TryConnect()
        {
            this.CloseSocket();

            string? socketPath = ResolveSocketPath();
            if (string.IsNullOrEmpty(socketPath) || !File.Exists(socketPath))
                return false;

            try
            {
                var endpoint = new UnixDomainSocketEndPoint(socketPath);
                var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified)
                {
                    ReceiveTimeout = 1500,
                    SendTimeout = 1500,
                    Blocking = true
                };

                socket.Connect(endpoint);

                // SASL EXTERNAL Handshake: \0AUTH EXTERNAL 31303030\r\n (1000 in hex)
                byte[] auth = Encoding.ASCII.GetBytes("\0AUTH EXTERNAL 31303030\r\n");
                socket.Send(auth);

                int read = socket.Receive(this._readBuffer);
                string authReply = Encoding.ASCII.GetString(this._readBuffer, 0, read);
                if (!authReply.StartsWith("OK"))
                {
                    socket.Send(Encoding.ASCII.GetBytes("AUTH EXTERNAL\r\n"));
                    read = socket.Receive(this._readBuffer);
                }

                socket.Send(Encoding.ASCII.GetBytes("BEGIN\r\n"));

                this._socket = socket;

                // Register with bus via Hello
                this.SendMethodCall("org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus", "Hello", null, Array.Empty<byte>());
                this.ReceiveMessageSync();

                // Subscribe to MPRIS PropertiesChanged signals & NameOwnerChanged signals
                this.AddMatchRule("type='signal',interface='org.freedesktop.DBus.Properties',member='PropertiesChanged',path='/org/mpris/MediaPlayer2'");
                this.AddMatchRule("type='signal',sender='org.freedesktop.DBus',interface='org.freedesktop.DBus',member='NameOwnerChanged'");

                // Initial player scan
                this.DetectActivePlayer();
                return true;
            }
            catch
            {
                this.CloseSocket();
                return false;
            }
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

        private static string? ResolveSocketPath()
        {
            string?[] candidates = new string?[]
            {
                Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") is string xdg ? Path.Combine(xdg, "bus") : null,
                @"Z:\run\user\1000\bus",
                "/run/user/1000/bus",
                Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") is string xdgWine ? Path.Combine(@"Z:", xdgWine.TrimStart('/'), "bus") : null,
                @"Z:\tmp\dbus-session",
                "/tmp/dbus-session"
            };

            foreach (string? path in candidates)
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    return path;
            }

            string[] baseDirs = new[] { @"Z:\run\user", "/run/user" };
            foreach (string baseDir in baseDirs)
            {
                try
                {
                    if (Directory.Exists(baseDir))
                    {
                        foreach (string userDir in Directory.GetDirectories(baseDir))
                        {
                            string bus = Path.Combine(userDir, "bus");
                            if (File.Exists(bus)) return bus;
                        }
                    }
                }
                catch { }
            }

            return null;
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
                catch
                {
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
                byte[]? statusReply = this.ReceiveMessageSync();

                bool isPlaying = false;
                if (statusReply != null)
                {
                    string status = ExtractVariantString(statusReply);
                    isPlaying = string.Equals(status, "Playing", StringComparison.OrdinalIgnoreCase);
                }

                // Query Metadata
                this.SendMethodCall(this._activePlayer, "/org/mpris/MediaPlayer2", "org.freedesktop.DBus.Properties", "Get", "ss",
                    EncodeStringPropertyArgs("org.mpris.MediaPlayer2.Player", "Metadata"));
                byte[]? metaReply = this.ReceiveMessageSync();

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
                byte[]? posReply = this.ReceiveMessageSync();
                if (posReply != null)
                {
                    long posMicro = ExtractVariantInt64(posReply);
                    if (posMicro >= 0)
                        position = TimeSpan.FromMilliseconds(posMicro / 1000.0);
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
            catch
            {
                this.CloseSocket();
            }
        }

        private void DetectActivePlayer()
        {
            try
            {
                this.SendMethodCall("org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus", "ListNames", null, Array.Empty<byte>());
                byte[]? reply = this.ReceiveMessageSync();
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

                // Match against priority list (BitChord, Spotify, etc.)
                foreach (string priority in this._priorityPlayers)
                {
                    string target = $"org.mpris.MediaPlayer2.{priority}";
                    foreach (string name in activeNames)
                    {
                        if (name.StartsWith(target, StringComparison.OrdinalIgnoreCase))
                        {
                            this._activePlayer = name;
                            return;
                        }
                    }
                }

                this._activePlayer = activeNames.Count > 0 ? activeNames[0] : null;
            }
            catch
            {
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
            this.ReceiveMessageSync();
        }

        private bool ReceiveAndProcessMessages()
        {
            if (this._socket == null) return false;

            int bytesRead;
            try
            {
                bytesRead = this._socket.Receive(this._readBuffer, 0, 16, SocketFlags.None);
                if (bytesRead <= 0) return false;
            }
            catch
            {
                return false;
            }

            uint bodyLen = BitConverter.ToUInt32(this._readBuffer, 4);
            uint fieldsLen = BitConverter.ToUInt32(this._readBuffer, 12);
            int headerLen = 16 + (int)fieldsLen;
            int rem = headerLen % 8;
            if (rem != 0) headerLen += (8 - rem);

            int totalExpected = headerLen + (int)bodyLen;
            int totalRead = bytesRead;

            while (totalRead < totalExpected && totalRead < this._readBuffer.Length)
            {
                int more = this._socket.Receive(this._readBuffer, totalRead, Math.Min(this._readBuffer.Length - totalRead, totalExpected - totalRead), SocketFlags.None);
                if (more <= 0) return false;
                totalRead += more;
            }

            byte msgType = this._readBuffer[1];
            if (msgType == 4) // SIGNAL (PropertiesChanged or NameOwnerChanged)
            {
                this.RefreshPlayerState();
            }

            return true;
        }

        private byte[]? ReceiveMessageSync()
        {
            if (this._socket == null) return null;

            int totalRead = 0;
            int bytesRead = this._socket.Receive(this._readBuffer, 0, 16, SocketFlags.None);
            if (bytesRead <= 0) return null;
            totalRead += bytesRead;

            uint bodyLen = BitConverter.ToUInt32(this._readBuffer, 4);
            uint fieldsLen = BitConverter.ToUInt32(this._readBuffer, 12);
            int headerLen = 16 + (int)fieldsLen;
            int rem = headerLen % 8;
            if (rem != 0) headerLen += (8 - rem);

            int totalExpected = headerLen + (int)bodyLen;
            while (totalRead < totalExpected && totalRead < this._readBuffer.Length)
            {
                int more = this._socket.Receive(this._readBuffer, totalRead, Math.Min(this._readBuffer.Length - totalRead, totalExpected - totalRead), SocketFlags.None);
                if (more <= 0) break;
                totalRead += more;
            }

            byte[] reply = new byte[totalRead];
            Buffer.BlockCopy(this._readBuffer, 0, reply, 0, totalRead);
            return reply;
        }

        private void SendMethodCall(string dest, string path, string iface, string member, string? sig, byte[] body)
        {
            if (this._socket == null) return;
            uint serial = this._serial++;
            byte[] msg = BuildDbusMessage(dest, path, iface, member, sig, body, serial, flags: 0);
            this._socket.Send(msg);
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
