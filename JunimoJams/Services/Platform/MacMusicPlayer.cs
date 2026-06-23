using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace JunimoJams.Services.Platform
{
    public class MacMusicPlayer : IMusicPlayer
    {
        private readonly string[] _appNames;

        public MacMusicPlayer(string[] extraPlayers = null)
        {
            var apps = new List<string>
            {
                "Spotify",
                "Music",       // Apple Music (macOS app name)
                "Cider",       // Open-source Apple Music client
                "TIDAL",
                "Deezer",
                "Plexamp"
            };

            if (extraPlayers != null)
            {
                foreach (string p in extraPlayers)
                {
                    if (!string.IsNullOrWhiteSpace(p) && !apps.Contains(p, StringComparer.OrdinalIgnoreCase))
                        apps.Add(p.Trim());
                }
            }

            this._appNames = apps.ToArray();
        }

        private static readonly string[] MacBrowsers = { "Google Chrome", "Safari", "Microsoft Edge", "Brave Browser" };

        public bool IsPlayerRunning()
        {
            return this.GetActiveApp() != null || this.GetActiveBrowserWithYtMusic() != null;
        }

        public TrackInfo GetCurrentTrack()
        {
            // Check browser-based YouTube Music first
            string activeBrowser = this.GetActiveBrowserWithYtMusic();
            if (activeBrowser != null)
            {
                string tabTitle = this.GetBrowserTabTitle(activeBrowser);
                return this.ParseYouTubeMusicTitle(tabTitle);
            }

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

        public void NextTrack()
        {
            if (this.GetActiveBrowserWithYtMusic() == null)
                this.RunActiveAppCommand("next track");
        }

        public void PreviousTrack()
        {
            if (this.GetActiveBrowserWithYtMusic() == null)
                this.RunActiveAppCommand("previous track");
        }

        public void TogglePlayPause()
        {
            if (this.GetActiveBrowserWithYtMusic() == null)
                this.RunActiveAppCommand("playpause");
        }

        private string GetActiveBrowserWithYtMusic()
        {
            foreach (var browser in MacBrowsers)
            {
                string tabTitle = this.GetBrowserTabTitle(browser);
                if (!string.IsNullOrEmpty(tabTitle) && tabTitle.Contains("YouTube Music", StringComparison.OrdinalIgnoreCase))
                {
                    return browser;
                }
            }
            return null;
        }

        private string GetBrowserTabTitle(string appName)
        {
            // Safari uses "current tab", Chrome/Edge/Brave use "active tab"
            string tabProperty = appName.Equals("Safari", StringComparison.OrdinalIgnoreCase) 
                ? "name of current tab" 
                : "title of active tab";

            string script = $"if application \"{appName}\" is running then\n" +
                            $"    tell application \"{appName}\" to get {tabProperty} of first window\n" +
                            $"else\n" +
                            $"    return \"\"\n" +
                            $"end if";

            return this.RunAppleScript(script).Trim();
        }

        private TrackInfo ParseYouTubeMusicTitle(string title)
        {
            title = title.Trim();
            bool isPlaying = true; // Default to true if active

            // Clean up play/pause prefixes if present
            if (title.StartsWith("▶"))
            {
                isPlaying = true;
                title = title.Substring(1).Trim();
            }
            else if (title.StartsWith("⏸"))
            {
                isPlaying = false;
                title = title.Substring(1).Trim();
            }

            // Strip browser suffixes
            string[] browserSuffixes = { " - Google Chrome", " - Safari", " - Microsoft Edge", " - Brave", " - Opera", " - Vivaldi", " - Arc", " - Helium" };
            foreach (var suffix in browserSuffixes)
            {
                int idx = title.LastIndexOf(suffix, StringComparison.OrdinalIgnoreCase);
                if (idx > 0)
                    title = title.Substring(0, idx).Trim();
            }

            // Check if it's just "YouTube Music"
            if (title.Equals("YouTube Music", StringComparison.OrdinalIgnoreCase))
            {
                if (isPlaying)
                {
                    return new TrackInfo
                    {
                        Name = "Playing",
                        Artist = "YouTube Music",
                        IsPlaying = true
                    };
                }
                return new TrackInfo
                {
                    Name = "Paused / Idle",
                    Artist = "YouTube Music",
                    IsPlaying = false
                };
            }

            // Find "YouTube Music" index to parse song/artist
            int ytIndex = title.IndexOf("YouTube Music", StringComparison.OrdinalIgnoreCase);
            if (ytIndex > 0)
            {
                string trackPart = title.Substring(0, ytIndex).Trim();
                string[] separators = { " - ", " — ", " – ", " -", " —", " –", " | ", " |", "| ", "|" };
                foreach (var sep in separators)
                {
                    if (trackPart.EndsWith(sep))
                        trackPart = trackPart.Substring(0, trackPart.Length - sep.Length).Trim();
                }

                string[] splitSeps = { " - ", " — ", " – " };
                foreach (var sep in splitSeps)
                {
                    if (trackPart.Contains(sep))
                    {
                        string[] parts = trackPart.Split(new[] { sep }, 2, StringSplitOptions.None);
                        return new TrackInfo
                        {
                            Name = parts[0].Trim(),
                            Artist = parts[1].Trim(),
                            IsPlaying = isPlaying
                        };
                    }
                }

                return new TrackInfo
                {
                    Name = trackPart,
                    Artist = "YouTube Music",
                    IsPlaying = isPlaying
                };
            }

            return new TrackInfo { Name = title, Artist = "YouTube Music", IsPlaying = isPlaying };
        }

        private string GetActiveApp()
        {
            foreach (string app in this._appNames)
            {
                if (this.IsAppRunning(app))
                    return app;
            }
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
