using System;
using System.Diagnostics;

namespace JunimoJamsRusted.Services.Platform
{
    /// <summary>
    /// Shared helper for parsing YouTube Music browser tab titles into TrackInfo.
    /// Used by both WindowsMusicPlayer and MacMusicPlayer to avoid code duplication.
    /// </summary>
    public static class YouTubeMusicParser
    {
        private static readonly string[] BrowserSuffixes =
        {
            " - Google Chrome", " - Microsoft Edge", " - Mozilla Firefox",
            " - Brave", " - Opera", " - Vivaldi", " - Arc", " - Helium",
            " - Safari"
        };

        public static TrackInfo? ParseTitle(string? rawTitle)
        {
            if (string.IsNullOrWhiteSpace(rawTitle))
                return null;

            string title = rawTitle.Trim();
            bool isPlaying = true;

            // Clean up play/pause Unicode prefixes
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

            // Strip browser suffixes (e.g., " - Google Chrome")
            foreach (var suffix in BrowserSuffixes)
            {
                int idx = title.LastIndexOf(suffix, StringComparison.OrdinalIgnoreCase);
                if (idx > 0)
                    title = title.Substring(0, idx).Trim();
            }

            // If the title is just "YouTube Music" (idle/home page)
            if (title.Equals("YouTube Music", StringComparison.OrdinalIgnoreCase))
            {
                return new TrackInfo
                {
                    Name = isPlaying ? "Playing" : "Paused / Idle",
                    Artist = "YouTube Music",
                    IsPlaying = isPlaying
                };
            }

            // Find "YouTube Music" index to separate song/artist from the service name
            int ytIndex = title.IndexOf("YouTube Music", StringComparison.OrdinalIgnoreCase);
            if (ytIndex > 0)
            {
                string trackPart = title.Substring(0, ytIndex).Trim();

                // Strip trailing separators between track info and "YouTube Music"
                string[] trailingSeps = { " - ", " — ", " – ", " -", " —", " –", " | ", " |", "| ", "|" };
                foreach (var sep in trailingSeps)
                {
                    if (trackPart.EndsWith(sep))
                        trackPart = trackPart.Substring(0, trackPart.Length - sep.Length).Trim();
                }

                // Try to split "Song - Artist" within the track part
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

                // No separator found — use entire track part as name
                return new TrackInfo
                {
                    Name = trackPart,
                    Artist = "YouTube Music",
                    IsPlaying = isPlaying
                };
            }

            // Fallback: title contains no "YouTube Music" marker
            return new TrackInfo { Name = title, Artist = "YouTube Music", IsPlaying = isPlaying };
        }

        public static void SendMacMediaKey(int keyCode)
        {
            try
            {
                string script = $"tell application \"System Events\" to key code {keyCode} using {{}}";
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
                process.WaitForExit();
            }
            catch
            {
                // Ignore errors — media key simulation is best-effort
            }
        }
    }
}
