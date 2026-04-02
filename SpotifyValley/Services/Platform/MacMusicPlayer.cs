using System;
using System.Diagnostics;

namespace SpotifyValley.Services.Platform
{
    public class MacMusicPlayer : IMusicPlayer
    {
        public bool IsSpotifyRunning()
        {
            return this.GetActiveApp() != null;
        }

        public TrackInfo GetCurrentTrack()
        {
            string app = this.GetActiveApp();
            if (app == null)
            {
                return new TrackInfo { Name = "Not Running", Artist = "Music Player", IsPlaying = false };
            }

            string state = this.RunAppleScript($"tell application \"{app}\" to player state as string");
            bool isPlaying = state.Trim().ToLower() == "playing";
            string artist = this.RunAppleScript($"tell application \"{app}\" to artist of current track as string");
            string name = this.RunAppleScript($"tell application \"{app}\" to name of current track as string");

            return new TrackInfo
            {
                Artist = artist.Trim(),
                Name = name.Trim(),
                IsPlaying = isPlaying
            };
        }

        public void NextTrack() => this.RunActiveAppCommand("next track");

        public void PreviousTrack() => this.RunActiveAppCommand("previous track");

        public void TogglePlayPause() => this.RunActiveAppCommand("playpause");

        private string GetActiveApp()
        {
            if (this.IsAppRunning("Spotify")) return "Spotify";
            if (this.IsAppRunning("Music")) return "Music";
            return null;
        }

        private bool IsAppRunning(string appName)
        {
            string output = this.RunAppleScript($"application \"{appName}\" is running");
            return output.Trim().ToLower() == "true";
        }

        private void RunActiveAppCommand(string command)
        {
            string app = this.GetActiveApp();
            if (app != null)
            {
                this.RunAppleScript($"tell application \"{app}\" to {command}");
            }
        }

        private string RunAppleScript(string script)
        {
            try
            {
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "osascript",
                        Arguments = $"-e '{script}'",
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
    }
}
