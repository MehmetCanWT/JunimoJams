using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;

namespace Spotify_Stardew.Services.Platform
{
    public class WindowsMusicPlayer : IMusicPlayer
    {
        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, int dwExtraInfo);

        private const byte VK_MEDIA_NEXT_TRACK = 176;
        private const byte VK_MEDIA_PREV_TRACK = 177;
        private const byte VK_MEDIA_PLAY_PAUSE = 179;
        private const uint KEYEVENTF_EXTENDEDKEY = 1;
        private const uint KEYEVENTF_KEYUP = 2;

        public bool IsSpotifyRunning()
        {
            return Process.GetProcessesByName("Spotify")
                .Any(p => !string.IsNullOrEmpty(p.MainWindowTitle));
        }

        public TrackInfo GetCurrentTrack()
        {
            Process process = Process.GetProcessesByName("Spotify")
                .FirstOrDefault(p => !string.IsNullOrEmpty(p.MainWindowTitle));

            if (process == null)
            {
                return new TrackInfo
                {
                    Name = "Not Running",
                    Artist = "",
                    IsPlaying = false
                };
            }

            string title = process.MainWindowTitle;
            if (title == "Spotify" || title == "Spotify Premium" || title == "Spotify Free")
            {
                return new TrackInfo
                {
                    Name = "Paused / Idle",
                    Artist = "Spotify",
                    IsPlaying = false
                };
            }

            if (title.Contains(" - "))
            {
                string[] parts = title.Split(new[] { " - " }, 2, StringSplitOptions.None);
                if (parts.Length == 2)
                {
                    return new TrackInfo
                    {
                        Artist = parts[0].Trim(),
                        Name = parts[1].Trim(),
                        IsPlaying = true
                    };
                }
            }

            return new TrackInfo
            {
                Name = title,
                Artist = "Spotify",
                IsPlaying = true
            };
        }

        public void NextTrack() => this.PressKey(VK_MEDIA_NEXT_TRACK);

        public void PreviousTrack() => this.PressKey(VK_MEDIA_PREV_TRACK);

        public void TogglePlayPause() => this.PressKey(VK_MEDIA_PLAY_PAUSE);

        private void PressKey(byte key)
        {
            keybd_event(key, 0, KEYEVENTF_EXTENDEDKEY, 0);
            keybd_event(key, 0, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, 0);
        }
    }
}
