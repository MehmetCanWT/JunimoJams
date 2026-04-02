using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Collections.Generic;

namespace SpotifyValley.Services.Platform
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

        private readonly string[] _playerProcessNames = { "Spotify", "iTunes", "AppleMusic", "Amazon Music" };

        public bool IsSpotifyRunning()
        {
            return this.GetActivePlayerProcess() != null;
        }

        public TrackInfo GetCurrentTrack()
        {
            try
            {
                var process = this.GetActivePlayerProcess();
                if (process == null)
                    return new TrackInfo { Name = "Not Running", Artist = "Music Player", IsPlaying = false };

                string title = process.MainWindowTitle;
                string processName = process.ProcessName;

                // Basic "Idle" titles
                if (title == "Spotify" || title == "Spotify Premium" || title == "Spotify Free" || title == "iTunes" || title == "Amazon Music")
                {
                    return new TrackInfo { Name = "Paused / Idle", Artist = processName, IsPlaying = false };
                }

                // Apple Music Window Title usually: "Song Title — Artist" (Note the em-dash or en-dash)
                // Spotify: "Artist - Song Title"
                // Amazon: "Song Title - Artist"
                string[] separators = { " - ", " — ", " – " };
                
                foreach (var sep in separators)
                {
                    if (title.Contains(sep))
                    {
                        string[] parts = title.Split(new[] { sep }, 2, StringSplitOptions.None);
                        
                        // Heuristic for player-specific order
                        if (processName == "Spotify")
                        {
                            return new TrackInfo { Artist = parts[0].Trim(), Name = parts[1].Trim(), IsPlaying = true };
                        }
                        else // iTunes/Amazon often flip it or keep it as Title - Artist
                        {
                             return new TrackInfo { Name = parts[0].Trim(), Artist = parts[1].Trim(), IsPlaying = true };
                        }
                    }
                }

                return new TrackInfo { Name = title, Artist = processName, IsPlaying = true };
            }
            catch
            {
                return new TrackInfo { Name = "Unknown", Artist = "Music Player", IsPlaying = false };
            }
        }

        public void NextTrack() => this.PressKey(VK_MEDIA_NEXT_TRACK);
        public void PreviousTrack() => this.PressKey(VK_MEDIA_PREV_TRACK);
        public void TogglePlayPause() => this.PressKey(VK_MEDIA_PLAY_PAUSE);

        private Process GetActivePlayerProcess()
        {
            foreach (var name in this._playerProcessNames)
            {
                var p = Process.GetProcessesByName(name).FirstOrDefault(p => !string.IsNullOrWhiteSpace(p.MainWindowTitle));
                if (p != null) return p;
            }
            return null;
        }

        private void PressKey(byte key)
        {
            keybd_event(key, 0, KEYEVENTF_EXTENDEDKEY, 0);
            keybd_event(key, 0, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, 0);
        }
    }
}
