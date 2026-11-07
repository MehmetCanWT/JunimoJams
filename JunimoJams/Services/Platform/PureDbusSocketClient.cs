using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;

namespace JunimoJams.Services.Platform
{
    /// <summary>
    /// Pure C# Zero-Dependency D-Bus Wire Protocol Client.
    /// Communicates directly with the Linux session bus over Unix Domain Sockets (AF_UNIX).
    /// Fully plug-and-play for both Native Linux and Steam Proton (via Z:\run\user\&lt;uid&gt;\bus).
    /// Requires zero external binaries, zero wine commands, and zero tools to install.
    /// </summary>
    public sealed class PureDbusSocketClient : IMusicPlayer, IDisposable
    {
        private readonly string[] _priorityPlayers;
        private Socket? _socket;
        private uint _serial = 1;
        private readonly byte[] _recvBuffer = new byte[32768];
        private string? _cachedPlayer;
        private DateTime _lastPlayerCheck = DateTime.MinValue;

        public PureDbusSocketClient(string[]? extraPlayers = null)
        {
            var players = new List<string>
            {
                "bitchord",
                "spotify",
                "tidal-hifi",
                "cider",
                "deezer",
                "plexamp",
                "nuclear",
                "clementine",
                "strawberry",
                "elisa",
                "lollypop",
                "vlc",
                "rhythmbox",
                "audacious",
                "deadbeef",
                "chromium",
                "firefox",
                "brave",
                "chrome"
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
            this.EnsureConnected();
        }

        public bool IsPlayerRunning()
        {
            return this.GetActivePlayer() != null;
        }

        public TrackInfo GetCurrentTrack()
        {
            string? player = this.GetActivePlayer();
            if (player == null)
            {
                return new TrackInfo { Name = "Not Running", Artist = "Music Player", IsPlaying = false };
            }

            try
            {
                bool isPlaying = false;
                byte[]? statusReply = this.CallMethod(player, "/org/mpris/MediaPlayer2", "org.freedesktop.DBus.Properties", "Get", "ss",
                    EncodeStringPropertyArgs("org.mpris.MediaPlayer2.Player", "PlaybackStatus"));

                if (statusReply != null)
                {
                    string status = ExtractVariantString(statusReply);
                    isPlaying = string.Equals(status, "Playing", StringComparison.OrdinalIgnoreCase);
                }

                // Query Metadata
                byte[]? metaReply = this.CallMethod(player, "/org/mpris/MediaPlayer2", "org.freedesktop.DBus.Properties", "Get", "ss",
                    EncodeStringPropertyArgs("org.mpris.MediaPlayer2.Player", "Metadata"));

                string title = "Unknown";
                string artist = "Unknown";
                string album = string.Empty;
                string? artUrl = null;
                TimeSpan duration = TimeSpan.Zero;

                if (metaReply != null)
                {
                    var dict = ParseMetadataDict(metaReply);
                    if (dict.TryGetValue("xesam:title", out object? tObj) && tObj is string tStr && !string.IsNullOrWhiteSpace(tStr))
                        title = tStr;

                    if (dict.TryGetValue("xesam:artist", out object? aObj) && aObj is string aStr && !string.IsNullOrWhiteSpace(aStr))
                        artist = aStr;

                    if (dict.TryGetValue("xesam:album", out object? alObj) && alObj is string alStr)
                        album = alStr;

                    if (dict.TryGetValue("mpris:artUrl", out object? uObj) && uObj is string uStr && !string.IsNullOrWhiteSpace(uStr))
                        artUrl = uStr;

                    if (dict.TryGetValue("mpris:length", out object? lObj) && lObj is long lMicro && lMicro > 0)
                        duration = TimeSpan.FromMilliseconds(lMicro / 1000.0);
                }

                // Query Position
                TimeSpan position = TimeSpan.Zero;
                byte[]? posReply = this.CallMethod(player, "/org/mpris/MediaPlayer2", "org.freedesktop.DBus.Properties", "Get", "ss",
                    EncodeStringPropertyArgs("org.mpris.MediaPlayer2.Player", "Position"));

                if (posReply != null)
                {
                    long posMicro = ExtractVariantInt64(posReply);
                    if (posMicro >= 0)
                        position = TimeSpan.FromMilliseconds(posMicro / 1000.0);
                }

                return new TrackInfo
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
                return new TrackInfo { Name = "Not Running", Artist = "Music Player", IsPlaying = false };
            }
        }

        public void TogglePlayPause()
        {
            string? player = this.GetActivePlayer();
            if (player != null)
                this.CallMethod(player, "/org/mpris/MediaPlayer2", "org.mpris.MediaPlayer2.Player", "PlayPause", null, Array.Empty<byte>());
        }

        public void NextTrack()
        {
            string? player = this.GetActivePlayer();
            if (player != null)
                this.CallMethod(player, "/org/mpris/MediaPlayer2", "org.mpris.MediaPlayer2.Player", "Next", null, Array.Empty<byte>());
        }

        public void PreviousTrack()
        {
            string? player = this.GetActivePlayer();
            if (player != null)
                this.CallMethod(player, "/org/mpris/MediaPlayer2", "org.mpris.MediaPlayer2.Player", "Previous", null, Array.Empty<byte>());
        }

        private string? GetActivePlayer()
        {
            if (this._cachedPlayer != null && (DateTime.UtcNow - this._lastPlayerCheck).TotalSeconds < 3.0)
                return this._cachedPlayer;

            try
            {
                byte[]? reply = this.CallMethod("org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus", "ListNames", null, Array.Empty<byte>());
                if (reply == null)
                    return null;

                string raw = Encoding.UTF8.GetString(reply);
                var activeNames = new List<string>();

                int idx = 0;
                while ((idx = raw.IndexOf("org.mpris.MediaPlayer2.", idx, StringComparison.Ordinal)) != -1)
                {
                    int end = raw.IndexOf('\0', idx);
                    if (end == -1) end = raw.Length;
                    string found = raw.Substring(idx, end - idx);
                    if (!activeNames.Contains(found))
                        activeNames.Add(found);
                    idx = end + 1;
                }

                // Check priority players first
                foreach (string priority in this._priorityPlayers)
                {
                    string target = $"org.mpris.MediaPlayer2.{priority}";
                    foreach (string name in activeNames)
                    {
                        if (name.StartsWith(target, StringComparison.OrdinalIgnoreCase))
                        {
                            this._cachedPlayer = name;
                            this._lastPlayerCheck = DateTime.UtcNow;
                            return name;
                        }
                    }
                }

                if (activeNames.Count > 0)
                {
                    this._cachedPlayer = activeNames[0];
                    this._lastPlayerCheck = DateTime.UtcNow;
                    return activeNames[0];
                }
            }
            catch
            {
                this.CloseSocket();
            }

            this._cachedPlayer = null;
            return null;
        }

        private bool EnsureConnected()
        {
            if (this._socket != null && this._socket.Connected)
                return true;

            this.CloseSocket();

            string? socketPath = ResolveSocketPath();
            if (string.IsNullOrEmpty(socketPath) || !File.Exists(socketPath))
                return false;

            try
            {
                var endpoint = new UnixDomainSocketEndPoint(socketPath);
                this._socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                this._socket.ReceiveTimeout = 2000;
                this._socket.SendTimeout = 2000;
                this._socket.Connect(endpoint);

                // D-Bus SASL EXTERNAL Handshake with hex UID (1000 -> 31303030)
                byte[] auth = Encoding.ASCII.GetBytes("\0AUTH EXTERNAL 31303030\r\n");
                this._socket.Send(auth);

                int read = this._socket.Receive(this._recvBuffer);
                string authReply = Encoding.ASCII.GetString(this._recvBuffer, 0, read);
                if (!authReply.StartsWith("OK"))
                {
                    // Fallback to anonymous EXTERNAL
                    this._socket.Send(Encoding.ASCII.GetBytes("AUTH EXTERNAL\r\n"));
                    read = this._socket.Receive(this._recvBuffer);
                }

                // Begin binary protocol
                this._socket.Send(Encoding.ASCII.GetBytes("BEGIN\r\n"));

                // Call Hello to establish bus registration
                this.CallMethod("org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus", "Hello", null, Array.Empty<byte>());
                return true;
            }
            catch
            {
                this.CloseSocket();
                return false;
            }
        }

        private static string? ResolveSocketPath()
        {
            // Candidate paths covering Native Linux, Proton (Wine Z:\ drive), and Steam Containers
            string?[] candidates = new string?[]
            {
                Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") is string xdg ? Path.Combine(xdg, "bus") : null,
                "/run/user/1000/bus",
                @"Z:\run\user\1000\bus",
                Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") is string xdgWine ? Path.Combine(@"Z:", xdgWine.TrimStart('/'), "bus") : null,
                "/tmp/dbus-session",
                @"Z:\tmp\dbus-session"
            };

            foreach (string? p in candidates)
            {
                if (!string.IsNullOrEmpty(p) && File.Exists(p))
                    return p;
            }

            // Search /run/user/*/bus or Z:\run\user\*\bus dynamically
            string[] baseDirs = new[] { "/run/user", @"Z:\run\user" };
            foreach (string dir in baseDirs)
            {
                try
                {
                    if (Directory.Exists(dir))
                    {
                        foreach (string sub in Directory.GetDirectories(dir))
                        {
                            string candidate = Path.Combine(sub, "bus");
                            if (File.Exists(candidate))
                                return candidate;
                        }
                    }
                }
                catch { }
            }

            return null;
        }

        private byte[]? CallMethod(string dest, string path, string iface, string member, string? sig, byte[] body)
        {
            if (!this.EnsureConnected())
                return null;

            try
            {
                uint serial = this._serial++;
                byte[] message = BuildDbusMessage(dest, path, iface, member, sig, body, serial);

                this._socket!.Send(message);

                int totalRead = 0;
                int bytesRead = this._socket.Receive(this._recvBuffer, 0, this._recvBuffer.Length, SocketFlags.None);
                totalRead += bytesRead;

                if (totalRead < 16)
                    return null;

                // Little-endian header: body length at offset 4, fields length at offset 12
                uint bodyLen = BitConverter.ToUInt32(this._recvBuffer, 4);
                uint fieldsLen = BitConverter.ToUInt32(this._recvBuffer, 12);
                int headerLen = 16 + (int)fieldsLen;
                int rem = headerLen % 8;
                if (rem != 0) headerLen += (8 - rem);

                int expectedTotal = headerLen + (int)bodyLen;
                while (totalRead < expectedTotal && totalRead < this._recvBuffer.Length)
                {
                    int more = this._socket.Receive(this._recvBuffer, totalRead, this._recvBuffer.Length - totalRead, SocketFlags.None);
                    if (more <= 0) break;
                    totalRead += more;
                }

                byte[] reply = new byte[totalRead];
                Buffer.BlockCopy(this._recvBuffer, 0, reply, 0, totalRead);
                return reply;
            }
            catch
            {
                this.CloseSocket();
                return null;
            }
        }

        private static byte[] BuildDbusMessage(string dest, string path, string iface, string member, string? sig, byte[] body, uint serial)
        {
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);

            // Fields: 1=PATH (o), 6=DESTINATION (s), 2=INTERFACE (s), 3=MEMBER (s), 8=SIGNATURE (g)
            using var fMs = new MemoryStream();
            using var fW = new BinaryWriter(fMs);

            WriteField(fW, 1, "o", path);
            WriteField(fW, 6, "s", dest);
            WriteField(fW, 2, "s", iface);
            WriteField(fW, 3, "s", member);
            if (!string.IsNullOrEmpty(sig))
                WriteField(fW, 8, "g", sig);

            byte[] fieldsBytes = fMs.ToArray();

            // Header: 'l' (little-endian), 1 (METHOD_CALL), 0 (flags), 1 (version), bodyLen (uint32), serial (uint32)
            w.Write((byte)'l');
            w.Write((byte)1);
            w.Write((byte)0);
            w.Write((byte)1);
            w.Write((uint)body.Length);
            w.Write(serial);
            w.Write((uint)fieldsBytes.Length);
            w.Write(fieldsBytes);

            // Pad header to 8 bytes boundary
            PadTo(w, 8);

            // Append body
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
                for (int i = 0; i < count; i++)
                    w.Write((byte)0);
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

            // Variant: 1 byte sig len, sig, null, align 4, len, str
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
            pos += 1 + sigLen + 1; // skip variant sig ("a{sv}\0")

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
                if (pos >= endPos || pos + 4 > b.Length) break;

                uint keyLen = BitConverter.ToUInt32(b, pos);
                pos += 4;
                if (pos + keyLen >= b.Length) break;
                string key = Encoding.UTF8.GetString(b, pos, (int)keyLen);
                pos += (int)keyLen + 1; // null

                if (pos >= b.Length) break;
                int vSigLen = b[pos];
                pos += 1;
                if (pos + vSigLen >= b.Length) break;
                string varSig = Encoding.ASCII.GetString(b, pos, vSigLen);
                pos += vSigLen + 1; // null

                if (varSig == "s" || varSig == "o")
                {
                    rem = pos % 4;
                    if (rem != 0) pos += (4 - rem);
                    if (pos + 4 > b.Length) break;
                    uint valLen = BitConverter.ToUInt32(b, pos);
                    pos += 4;
                    if (pos + valLen > b.Length) break;
                    string val = Encoding.UTF8.GetString(b, pos, (int)valLen);
                    pos += (int)valLen + 1;
                    dict[key] = val;
                }
                else if (varSig == "as")
                {
                    rem = pos % 4;
                    if (rem != 0) pos += (4 - rem);
                    if (pos + 4 > b.Length) break;
                    uint asLen = BitConverter.ToUInt32(b, pos);
                    pos += 4;
                    int asEnd = pos + (int)asLen;
                    var items = new List<string>();
                    while (pos < asEnd && pos + 4 <= b.Length)
                    {
                        rem = pos % 4;
                        if (rem != 0) pos += (4 - rem);
                        if (pos + 4 > b.Length) break;
                        uint itemLen = BitConverter.ToUInt32(b, pos);
                        pos += 4;
                        if (pos + itemLen > b.Length) break;
                        items.Add(Encoding.UTF8.GetString(b, pos, (int)itemLen));
                        pos += (int)itemLen + 1;
                    }
                    dict[key] = string.Join(", ", items);
                }
                else if (varSig == "x")
                {
                    rem = pos % 8;
                    if (rem != 0) pos += (8 - rem);
                    if (pos + 8 > b.Length) break;
                    long val = BitConverter.ToInt64(b, pos);
                    pos += 8;
                    dict[key] = val;
                }
                else
                {
                    // Skip unsupported variant
                    break;
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

        private void CloseSocket()
        {
            try
            {
                this._socket?.Close();
                this._socket?.Dispose();
            }
            catch { }
            this._socket = null;
        }

        public void Dispose()
        {
            this.CloseSocket();
        }
    }
}
