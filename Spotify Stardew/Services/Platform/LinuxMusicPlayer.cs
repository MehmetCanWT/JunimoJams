using System;
using System.Diagnostics;
using System.Linq;

namespace Spotify_Stardew.Services.Platform
{
    public class LinuxMusicPlayer : IMusicPlayer
    {
        public bool IsSpotifyRunning()
        {
            return Process.GetProcessesByName("spotify").Any();
        }

        public TrackInfo GetCurrentTrack()
        {
            if (!this.IsSpotifyRunning())
            {
                return new TrackInfo
                {
                    Name = "Not Running",
                    Artist = "",
                    IsPlaying = false
                };
            }

            string metadata = this.RunShellCommand("dbus-send --print-reply --dest=org.mpris.MediaPlayer2.spotify /org/mpris/MediaPlayer2 org.freedesktop.DBus.Properties.Get string:'org.mpris.MediaPlayer2.Player' string:'Metadata'");
            string status = this.RunShellCommand("dbus-send --print-reply --dest=org.mpris.MediaPlayer2.spotify /org/mpris/MediaPlayer2 org.freedesktop.DBus.Properties.Get string:'org.mpris.MediaPlayer2.Player' string:'PlaybackStatus'");
            
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

        public void NextTrack() => this.SendDbusMethod("Next");

        public void PreviousTrack() => this.SendDbusMethod("Previous");

        public void TogglePlayPause() => this.SendDbusMethod("PlayPause");

        private void SendDbusMethod(string method)
        {
            this.RunShellCommand($"dbus-send --print-reply --dest=org.mpris.MediaPlayer2.spotify /org/mpris/MediaPlayer2 org.mpris.MediaPlayer2.Player.{method}");
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
