using System;
using System.Diagnostics;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.InteropServices;

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

        private readonly string[] _playerProcessNames;

        // Known idle window titles — when the player is open but not actively playing a track
        private static readonly HashSet<string> IdleTitles = new(StringComparer.OrdinalIgnoreCase)
        {
            "Spotify", "Spotify Premium", "Spotify Free",
            "iTunes",
            "Amazon Music",
            "Apple Music", "AppleMusic",
            "Cider", "Cider (Preview)"
        };

        public WindowsMusicPlayer(string[] extraPlayers = null)
        {
            // Built-in players + any user-configured extras from config.json
            var players = new List<string> { "Spotify", "iTunes", "AppleMusic", "Amazon Music", "Cider" };
            if (extraPlayers != null && extraPlayers.Length > 0)
            {
                foreach (string p in extraPlayers)
                {
                    if (!string.IsNullOrWhiteSpace(p) && !players.Contains(p, StringComparer.OrdinalIgnoreCase))
                    {
                        players.Add(p.Trim());
                        // Also register the extra player name as an idle title
                        IdleTitles.Add(p.Trim());
                    }
                }
            }
            this._playerProcessNames = players.ToArray();
        }

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

                // Check if the player is idle (open but not playing)
                if (IdleTitles.Contains(title))
                {
                    return new TrackInfo { Name = "Paused / Idle", Artist = processName, IsPlaying = false };
                }

                // Parse window title to extract artist and track name.
                // Different players use different separator formats:
                //   Spotify:       "Artist - Song"
                //   iTunes/Cider:  "Song - Artist"  (also em-dash / en-dash variants)
                //   Apple Music:   "Song — Artist"
                //   Amazon Music:  "Song - Artist"
                string[] separators = { " - ", " — ", " – " };
                
                foreach (var sep in separators)
                {
                    if (title.Contains(sep))
                    {
                        string[] parts = title.Split(new[] { sep }, 2, StringSplitOptions.None);
                        
                        // Spotify uses "Artist - Song", everyone else uses "Song - Artist"
                        if (processName == "Spotify")
                        {
                            return new TrackInfo { Artist = parts[0].Trim(), Name = parts[1].Trim(), IsPlaying = true };
                        }
                        else
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

