using System;
using System.Diagnostics;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace JunimoJamsRusted.Services.Platform
{
    public class WindowsMusicPlayer : IMusicPlayer
    {
        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, int dwExtraInfo);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        private const byte VK_MEDIA_NEXT_TRACK = 176;
        private const byte VK_MEDIA_PREV_TRACK = 177;
        private const byte VK_MEDIA_PLAY_PAUSE = 179;
        private const uint KEYEVENTF_EXTENDEDKEY = 1;
        private const uint KEYEVENTF_KEYUP = 2;

        private readonly string[] _playerProcessNames;

        private static readonly string[] BrowserProcessNames =
            { "chrome", "msedge", "firefox", "opera", "brave", "vivaldi", "arc", "helium" };

        private static readonly HashSet<string> IdleTitles = new(StringComparer.OrdinalIgnoreCase)
        {
            "Spotify", "Spotify Premium", "Spotify Free",
            "iTunes",
            "Amazon Music",
            "Apple Music", "AppleMusic",
            "Cider", "Cider (Preview)",
            "YouTube Music",
            "YouTube Music Desktop App",
            "TIDAL", "TIDAL - HiFi",
            "foobar2000",
            "MusicBee",
            "AIMP",
            "Winamp",
            "MediaMonkey"
        };

        public WindowsMusicPlayer(string[]? extraPlayers = null)
        {
            var players = new List<string>
            {
                // Tier 1 — Streaming
                "Spotify",
                "iTunes",
                "AppleMusic",
                "Amazon Music",
                "Cider",
                "YouTube Music",
                "youtube-music-desktop-app",
                "TIDAL",

                // Tier 2 — Desktop Players
                "foobar2000",
                "MusicBee",
                "AIMP",
                "Winamp",
                "MediaMonkey"
            };

            if (extraPlayers != null && extraPlayers.Length > 0)
            {
                foreach (string p in extraPlayers)
                {
                    if (!string.IsNullOrWhiteSpace(p) && !players.Contains(p, StringComparer.OrdinalIgnoreCase))
                    {
                        players.Add(p.Trim());
                        IdleTitles.Add(p.Trim());
                    }
                }
            }
            this._playerProcessNames = players.ToArray();
        }

        public bool IsPlayerRunning()
        {
            return this.GetYouTubeMusicTrack() != null || this.GetActivePlayerProcess() != null;
        }

        public TrackInfo GetCurrentTrack()
        {
            try
            {
                var ytTrack = this.GetYouTubeMusicTrack();
                if (ytTrack != null)
                    return ytTrack;

                var process = this.GetActivePlayerProcess();
                if (process == null)
                    return new TrackInfo { Name = "Not Running", Artist = "Music Player", IsPlaying = false };

                string title = process.MainWindowTitle;
                string processName = process.ProcessName;

                if (IdleTitles.Contains(title))
                {
                    return new TrackInfo { Name = "Paused / Idle", Artist = processName, IsPlaying = false };
                }

                string[] separators = { " - ", " — ", " – " };
                
                foreach (var sep in separators)
                {
                    if (title.Contains(sep))
                    {
                        string[] parts = title.Split(new[] { sep }, 2, StringSplitOptions.None);
                        
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

        private TrackInfo? GetYouTubeMusicTrack()
        {
            try
            {
                var titles = new List<string>();
                EnumWindows((hWnd, lParam) =>
                {
                    if (IsWindowVisible(hWnd))
                    {
                        StringBuilder sb = new StringBuilder(512);
                        GetWindowText(hWnd, sb, sb.Capacity);
                        string title = sb.ToString();
                        if (!string.IsNullOrWhiteSpace(title))
                        {
                            GetWindowThreadProcessId(hWnd, out uint pid);
                            try
                            {
                                using var proc = Process.GetProcessById((int)pid);
                                string procName = proc.ProcessName;
                                if (BrowserProcessNames.Contains(procName.ToLowerInvariant()))
                                {
                                    titles.Add(title);
                                }
                            }
                            catch {}
                        }
                    }
                    return true;
                }, IntPtr.Zero);

                foreach (string rawTitle in titles)
                {
                    if (!rawTitle.Contains("YouTube Music", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var track = YouTubeMusicParser.ParseTitle(rawTitle);
                    if (track != null)
                        return track;
                }
            }
            catch {}
            return null;
        }

        public void NextTrack() => this.PressKey(VK_MEDIA_NEXT_TRACK);
        public void PreviousTrack() => this.PressKey(VK_MEDIA_PREV_TRACK);
        public void TogglePlayPause() => this.PressKey(VK_MEDIA_PLAY_PAUSE);

        private Process? GetActivePlayerProcess()
        {
            foreach (var name in this._playerProcessNames)
            {
                var processes = Process.GetProcessesByName(name);
                Process? match = null;
                foreach (var p in processes)
                {
                    if (match == null && !string.IsNullOrWhiteSpace(p.MainWindowTitle))
                        match = p;
                    else
                        p.Dispose();
                }
                if (match != null) return match;
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
