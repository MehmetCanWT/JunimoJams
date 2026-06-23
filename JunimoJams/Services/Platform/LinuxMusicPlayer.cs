using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace JunimoJams.Services.Platform
{
    public class LinuxMusicPlayer : IMusicPlayer
    {
        private readonly string[] _priorityPlayers;

        public LinuxMusicPlayer(string[] extraPlayers = null)
        {
            var players = new List<string>
            {
                // Tier 1 — Streaming
                "spotify",
                "tidal-hifi",
                "cider",
                "deezer",
                "plexamp",
                "nuclear",

                // Tier 2 — Desktop Players
                "clementine",
                "strawberry",
                "elisa",
                "lollypop",
                "vlc",
                "rhythmbox",
                "audacious",
                "deadbeef"
            };

            if (extraPlayers != null)
            {
                foreach (string p in extraPlayers)
                {
                    string lower = p?.Trim().ToLowerInvariant();
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
            
            // Check priority list first
            foreach (var p in this._priorityPlayers)
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

        private string RunShellCommand(string command)
        {
            try
            {
                // Split the command into executable and arguments to avoid
                // spawning an unnecessary /bin/bash wrapper process per call.
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
