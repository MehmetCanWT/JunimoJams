using System;
using System.Diagnostics;
using System.Linq;

namespace SpotifyValley.Services.Platform
{
    public class LinuxMusicPlayer : IMusicPlayer
    {
        public bool IsSpotifyRunning()
        {
            return this.GetActivePlayer() != null;
        }

        public TrackInfo GetCurrentTrack()
        {
            string player = this.GetActivePlayer();
            if (player == null)
            {
                return new TrackInfo { Name = "Not Running", Artist = "Music Player", IsPlaying = false };
            }

            string metadata = this.RunPlayerCommand(player, "org.freedesktop.DBus.Properties.Get string:'org.mpris.MediaPlayer2.Player' string:'Metadata'");
            string status = this.RunPlayerCommand(player, "org.freedesktop.DBus.Properties.Get string:'org.mpris.MediaPlayer2.Player' string:'PlaybackStatus'");
            
            bool isPlaying = status.Contains("Playing");
            string artist = this.ParseDbusMetadata(metadata, "xesam:artist");
            string title = this.ParseDbusMetadata(metadata, "xesam:title");

            return new TrackInfo
            {
                Name = string.IsNullOrEmpty(title) ? "Unknown" : title,
                Artist = string.IsNullOrEmpty(artist) ? "Unknown" : artist,
                IsPlaying = isPlaying
            };
        }

        public void NextTrack() => this.SendPlayerMethod("Next");

        public void PreviousTrack() => this.SendPlayerMethod("Previous");

        public void TogglePlayPause() => this.SendPlayerMethod("PlayPause");

        private string GetActivePlayer()
        {
            string names = this.RunShellCommand("dbus-send --print-reply --dest=org.freedesktop.DBus /org.freedesktop.DBus org.freedesktop.DBus.ListNames");
            string[] lines = names.Split('\n');
            
            // Priority list
            string[] players = { "spotify", "google-play-music", "clementine", "vlc", "rhythmbox" };
            
            foreach (var p in players)
            {
                if (lines.Any(l => l.Contains($"org.mpris.MediaPlayer2.{p}")))
                    return $"org.mpris.MediaPlayer2.{p}";
            }

            // Fallback to first MPRIS2 player found
            var fallback = lines.FirstOrDefault(l => l.Contains("org.mpris.MediaPlayer2."));
            if (fallback != null)
            {
                int quoteStart = fallback.IndexOf("\"") + 1;
                int quoteEnd = fallback.LastIndexOf("\"");
                return fallback.Substring(quoteStart, quoteEnd - quoteStart);
            }

            return null;
        }

        private void SendPlayerMethod(string method)
        {
            string player = this.GetActivePlayer();
            if (player != null)
            {
                this.RunShellCommand($"dbus-send --print-reply --dest={player} /org/mpris/MediaPlayer2 org.mpris.MediaPlayer2.Player.{method}");
            }
        }

        private string RunPlayerCommand(string dest, string command)
        {
            return this.RunShellCommand($"dbus-send --print-reply --dest={dest} /org/mpris/MediaPlayer2 {command}");
        }

        private string RunShellCommand(string args)
        {
            try
            {
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "/bin/bash",
                        Arguments = $"-c \"{args}\"",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                process.Start();
                string result = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
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
                int keyIndex = output.IndexOf(key);
                if (keyIndex == -1)
                    return "";

                string afterKey = output.Substring(keyIndex);
                int variantIndex = afterKey.IndexOf("variant");
                if (variantIndex != -1)
                {
                    int startQuote = afterKey.IndexOf("\"", variantIndex);
                    if (startQuote != -1)
                    {
                        int endQuote = afterKey.IndexOf("\"", startQuote + 1);
                        if (endQuote != -1)
                        {
                            return afterKey.Substring(startQuote + 1, endQuote - startQuote - 1);
                        }
                    }
                }
            }
            catch
            {
                // Ignore
            }
            return "";
        }
    }
}
