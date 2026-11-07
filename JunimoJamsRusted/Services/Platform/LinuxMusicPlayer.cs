using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace JunimoJamsRusted.Services.Platform
{
    public class LinuxMusicPlayer : IMusicPlayer
    {
        private readonly string[] _priorityPlayers;

        public LinuxMusicPlayer(string[]? extraPlayers = null)
        {
            var players = new List<string>
            {
                // Active User Player & Streaming
                "bitchord",
                "spotify",
                "tidal-hifi",
                "cider",
                "deezer",
                "plexamp",
                "nuclear",

                // Desktop Players
                "clementine",
                "strawberry",
                "elisa",
                "lollypop",
                "vlc",
                "rhythmbox",
                "audacious",
                "deadbeef",

                // Browsers with Media Keys
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

            string metadata = this.RunPlayerCommand(player, "org.freedesktop.DBus.Properties.Get string:'org.mpris.MediaPlayer2.Player' string:'Metadata'");
            string status = this.RunPlayerCommand(player, "org.freedesktop.DBus.Properties.Get string:'org.mpris.MediaPlayer2.Player' string:'PlaybackStatus'");
            string posOutput = this.RunPlayerCommand(player, "org.freedesktop.DBus.Properties.Get string:'org.mpris.MediaPlayer2.Player' string:'Position'");

            bool isPlaying = status.Contains("Playing");
            string artist = this.ParseDbusMetadata(metadata, "xesam:artist");
            string title = this.ParseDbusMetadata(metadata, "xesam:title");
            string album = this.ParseDbusMetadata(metadata, "xesam:album");
            string artUrl = this.ParseDbusMetadata(metadata, "mpris:artUrl");

            // Parse track length in microseconds (mpris:length int64)
            TimeSpan duration = TimeSpan.Zero;
            string lengthStr = this.ParseDbusInt64(metadata, "mpris:length");
            if (long.TryParse(lengthStr, out long lengthMicrosec) && lengthMicrosec > 0)
            {
                duration = TimeSpan.FromMilliseconds(lengthMicrosec / 1000.0);
            }

            // Parse position in microseconds
            TimeSpan position = TimeSpan.Zero;
            if (long.TryParse(this.ExtractVariantInt64(posOutput), out long posMicrosec) && posMicrosec >= 0)
            {
                position = TimeSpan.FromMilliseconds(posMicrosec / 1000.0);
            }

            var track = new TrackInfo
            {
                Name = string.IsNullOrEmpty(title) ? "Unknown" : title,
                Artist = string.IsNullOrEmpty(artist) ? "Unknown" : artist,
                Album = album,
                IsPlaying = isPlaying,
                Duration = duration,
                Position = position,
                PlaybackRate = isPlaying ? 1.0f : 0.0f,
                LastPositionUpdateTime = DateTime.UtcNow,
                DirectArtUri = !string.IsNullOrEmpty(artUrl) ? artUrl : null
            };

            return track;
        }

        public void NextTrack() => this.SendPlayerMethod("Next");

        public void PreviousTrack() => this.SendPlayerMethod("Previous");

        public void TogglePlayPause() => this.SendPlayerMethod("PlayPause");

        private string? GetActivePlayer()
        {
            string names = this.RunShellCommand("dbus-send --print-reply --dest=org.freedesktop.DBus /org/freedesktop/DBus org.freedesktop.DBus.ListNames");
            string[] lines = names.Split('\n');
            
            // Check priority list first (extract exact bus name from line)
            foreach (var p in this._priorityPlayers)
            {
                var matchingLine = lines.FirstOrDefault(l => l.Contains($"org.mpris.MediaPlayer2.{p}", StringComparison.OrdinalIgnoreCase));
                if (matchingLine != null)
                {
                    int q1 = matchingLine.IndexOf('"');
                    int q2 = matchingLine.LastIndexOf('"');
                    if (q1 >= 0 && q2 > q1)
                    {
                        return matchingLine.Substring(q1 + 1, q2 - q1 - 1);
                    }
                    return $"org.mpris.MediaPlayer2.{p}";
                }
            }

            // Fallback to first MPRIS2 player found (e.g. browser instances: org.mpris.MediaPlayer2.chromium.instance...)
            var fallback = lines.FirstOrDefault(l => l.Contains("org.mpris.MediaPlayer2.", StringComparison.OrdinalIgnoreCase));
            if (fallback != null)
            {
                int quoteStart = fallback.IndexOf('"') + 1;
                int quoteEnd = fallback.LastIndexOf('"');
                if (quoteStart > 0 && quoteEnd > quoteStart)
                {
                    return fallback.Substring(quoteStart, quoteEnd - quoteStart);
                }
            }

            return null;
        }

        private void SendPlayerMethod(string method)
        {
            string? player = this.GetActivePlayer();
            if (player != null)
            {
                this.RunShellCommand($"dbus-send --print-reply --dest={player} /org/mpris/MediaPlayer2 org.mpris.MediaPlayer2.Player.{method}");
            }
        }

        private string RunPlayerCommand(string dest, string command)
        {
            return this.RunShellCommand($"dbus-send --print-reply --dest={dest} /org/mpris/MediaPlayer2 {command}");
        }

        private string RunShellCommand(string command)
        {
            try
            {
                string fileName;
                string arguments;
                int firstSpace = command.IndexOf(' ');
                if (firstSpace > 0)
                {
                    fileName = command.Substring(0, firstSpace);
                    arguments = command.Substring(firstSpace + 1);
                }
                else
                {
                    fileName = command;
                    arguments = "";
                }

                if (WineDetector.IsRunningUnderWineOrProton())
                {
                    arguments = $"/c start /unix /usr/bin/{fileName} {arguments}";
                    fileName = "cmd.exe";
                }

                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = fileName,
                        Arguments = arguments,
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                process.Start();
                string result = process.StandardOutput.ReadToEnd();
                process.WaitForExit(1000);
                return result;
            }
            catch
            {
                return "";
            }
        }

        private string ParseDbusMetadata(string output, string key)
        {
            try
            {
                int keyIndex = output.IndexOf($"\"{key}\"");
                if (keyIndex == -1)
                    keyIndex = output.IndexOf(key);
                if (keyIndex == -1)
                    return "";

                int variantIndex = output.IndexOf("variant", keyIndex);
                if (variantIndex == -1)
                    return "";

                int nextEntry = output.IndexOf("dict entry(", variantIndex);
                string block = nextEntry != -1
                    ? output.Substring(variantIndex, nextEntry - variantIndex)
                    : output.Substring(variantIndex);

                // Collect all string "..." inside the variant block (handles single string and array of strings)
                var matches = System.Text.RegularExpressions.Regex.Matches(block, "string\\s+\"([^\"]*)\"");
                if (matches.Count > 0)
                {
                    var values = new List<string>();
                    foreach (System.Text.RegularExpressions.Match m in matches)
                    {
                        string val = m.Groups[1].Value.Trim();
                        if (!string.IsNullOrEmpty(val))
                            values.Add(val);
                    }
                    if (values.Count > 0)
                        return string.Join(", ", values);
                }

                // Fallback to first quoted string
                int q1 = block.IndexOf('"');
                if (q1 != -1)
                {
                    int q2 = block.IndexOf('"', q1 + 1);
                    if (q2 != -1)
                        return block.Substring(q1 + 1, q2 - q1 - 1);
                }
            }
            catch
            {
                // Suppress
            }
            return "";
        }

        private string ParseDbusInt64(string output, string key)
        {
            try
            {
                int keyIndex = output.IndexOf(key);
                if (keyIndex == -1)
                    return "";

                string afterKey = output.Substring(keyIndex);
                int int64Index = afterKey.IndexOf("int64");
                if (int64Index != -1)
                {
                    string rest = afterKey.Substring(int64Index + 5).Trim();
                    int endLine = rest.IndexOfAny(new[] { '\n', '\r', ' ' });
                    if (endLine > 0)
                        return rest.Substring(0, endLine).Trim();
                    return rest;
                }
            }
            catch
            {
                // Suppress
            }
            return "";
        }

        private string ExtractVariantInt64(string output)
        {
            try
            {
                int int64Idx = output.IndexOf("int64");
                if (int64Idx != -1)
                {
                    string rest = output.Substring(int64Idx + 5).Trim();
                    int endLine = rest.IndexOfAny(new[] { '\n', '\r', ' ' });
                    if (endLine > 0)
                        return rest.Substring(0, endLine).Trim();
                    return rest;
                }
            }
            catch {}
            return "";
        }
    }
}
