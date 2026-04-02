using System;
using System.Diagnostics;

namespace Spotify_Stardew.Services.Platform
{
    public class MacMusicPlayer : IMusicPlayer
    {
        public bool IsSpotifyRunning()
        {
            string output = this.RunAppleScript("application \"Spotify\" is running");
            return output.Trim().ToLower() == "true";
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

            string state = this.RunAppleScript("tell application \"Spotify\" to player state as string");
            bool isPlaying = state.Trim().ToLower() == "playing";
            string artist = this.RunAppleScript("tell application \"Spotify\" to artist of current track as string");
            string name = this.RunAppleScript("tell application \"Spotify\" to name of current track as string");

            return new TrackInfo
            {
                Artist = artist.Trim(),
                Name = name.Trim(),
                IsPlaying = isPlaying
            };
        }

        public void NextTrack() => this.RunAppleScript("tell application \"Spotify\" to next track");

        public void PreviousTrack() => this.RunAppleScript("tell application \"Spotify\" to previous track");

        public void TogglePlayPause() => this.RunAppleScript("tell application \"Spotify\" to playpause");

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
