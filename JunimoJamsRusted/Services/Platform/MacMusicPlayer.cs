using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace JunimoJamsRusted.Services.Platform
{
    public class MacMusicPlayer : IMusicPlayer
    {
        private readonly string[] _appNames;

        public MacMusicPlayer(string[]? extraPlayers = null)
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
            string? activeBrowser = this.GetActiveBrowserWithYtMusic();
            if (activeBrowser != null)
            {
                string tabTitle = this.GetBrowserTabTitle(activeBrowser);
                var parsed = YouTubeMusicParser.ParseTitle(tabTitle);
                if (parsed != null)
                    return parsed;
            }

            string? app = this.GetActiveApp();
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
            if (this.GetActiveBrowserWithYtMusic() != null)
                this.SendMediaKey("next");
            else
                this.RunActiveAppCommand("next track");
        }

        public void PreviousTrack()
        {
            if (this.GetActiveBrowserWithYtMusic() != null)
                this.SendMediaKey("previous");
            else
                this.RunActiveAppCommand("previous track");
        }

        public void TogglePlayPause()
        {
            if (this.GetActiveBrowserWithYtMusic() != null)
                this.SendMediaKey("playpause");
            else
                this.RunActiveAppCommand("playpause");
        }

        private void SendMediaKey(string action)
        {
            int keyCode = action switch
            {
                "playpause" => 16,
                "next" => 17,
                "previous" => 18,
                _ => -1
            };

            if (keyCode < 0) return;

            string script = $@"
                import Quartz
                event = Quartz.NSEvent.otherEventWithType_location_modifierFlags_timestamp_windowNumber_context_subtype_data1_data2_(
                    14, (0, 0), 0xa00, 0, 0, 0, 8, ({keyCode} << 16) | (0xa << 8), -1)
                Quartz.CGEventPost(0, event.CGEvent())
                import time; time.sleep(0.05)
                event = Quartz.NSEvent.otherEventWithType_location_modifierFlags_timestamp_windowNumber_context_subtype_data1_data2_(
                    14, (0, 0), 0xb00, 0, 0, 0, 8, ({keyCode} << 16) | (0xb << 8), -1)
                Quartz.CGEventPost(0, event.CGEvent())
            ";
            try
            {
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "python3",
                        Arguments = $"-c \"{script.Replace("\"", "\\\"")}\"",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                process.Start();
                process.WaitForExit(2000);
            }
            catch
            {
                this.RunAppleScript($"tell application \"System Events\" to key code {keyCode}");
            }
        }

        private string? GetActiveBrowserWithYtMusic()
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

        private string? GetActiveApp()
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
            string? app = this.GetActiveApp();
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
