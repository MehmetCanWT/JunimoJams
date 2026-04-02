using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace SpotifyValley.Services
{
    public class ArtService
    {
        private readonly HttpClient _http;
        private readonly string _cachePath;
        private string _lastSearchQuery = "";
        private byte[] _lastResultBytes = null;

        public ArtService(string modPath)
        {
            this._http = new HttpClient();
            this._http.Timeout = TimeSpan.FromSeconds(5.0);
            this._http.DefaultRequestHeaders.Add("User-Agent", "SpotifyValley/1.0");
            
            this._cachePath = Path.Combine(modPath, "cache");
            if (!Directory.Exists(this._cachePath))
            {
                Directory.CreateDirectory(this._cachePath);
            }
        }

        public async Task<byte[]> FetchCoverArtBytes(string artist, string trackName)
        {
            if (string.IsNullOrWhiteSpace(artist) || string.IsNullOrWhiteSpace(trackName))
                return null;

            if (artist == "Unknown" || trackName == "Unknown")
                return null;

            string cleanTrackName = this.CleanTitle(trackName);
            string queryKey = $"{artist}-{cleanTrackName}";

            if (this._lastSearchQuery == queryKey && this._lastResultBytes != null)
                return this._lastResultBytes;

            // Check Cache
            byte[] cached = this.GetFromCache(queryKey);
            if (cached != null)
            {
                this._lastSearchQuery = queryKey;
                this._lastResultBytes = cached;
                return cached;
            }

            try
            {
                string term = $"{artist} {cleanTrackName}";
                string url = $"https://itunes.apple.com/search?term={Uri.EscapeDataString(term)}&entity=song&limit=10";
                string response = await this._http.GetStringAsync(url);
                
                JObject json = JObject.Parse(response);
                JArray results = json["results"] as JArray;
                
                if (results == null || results.Count == 0)
                    return null;

                string bestArtUrl = null;
                double bestScore = -1;

                foreach (JToken result in results)
                {
                    string foundArtist = result["artistName"]?.ToString() ?? "";
                    string foundTitle = result["trackName"]?.ToString() ?? "";
                    
                    double score = this.CalculateMatchScore(artist, cleanTrackName, foundArtist, foundTitle);
                    
                    if (score > bestScore && score > 0.5) // Minimum threshold
                    {
                        string art = result["artworkUrl100"]?.ToString();
                        if (!string.IsNullOrEmpty(art))
                        {
                            bestArtUrl = art.Replace("100x100", "600x600");
                            bestScore = score;
                        }
                    }
                }

                if (string.IsNullOrEmpty(bestArtUrl))
                    return null;

                byte[] imageBytes = await this._http.GetByteArrayAsync(bestArtUrl);
                this._lastSearchQuery = queryKey;
                this._lastResultBytes = imageBytes;
                
                // Save to cache
                this.SaveToCache(queryKey, imageBytes);
                
                return imageBytes;
            }
            catch
            {
                return null;
            }
        }

        private byte[] GetFromCache(string key)
        {
            try
            {
                string fileName = this.GetHashString(key) + ".png";
                string fullPath = Path.Combine(this._cachePath, fileName);
                if (File.Exists(fullPath))
                {
                    return File.ReadAllBytes(fullPath);
                }
            }
            catch { }
            return null;
        }

        private void SaveToCache(string key, byte[] bytes)
        {
            try
            {
                string fileName = this.GetHashString(key) + ".png";
                string fullPath = Path.Combine(this._cachePath, fileName);
                File.WriteAllBytes(fullPath, bytes);
            }
            catch { }
        }

        private string GetHashString(string inputString)
        {
            using (HashAlgorithm algorithm = MD5.Create())
            {
                byte[] bytes = algorithm.ComputeHash(Encoding.UTF8.GetBytes(inputString));
                StringBuilder sb = new StringBuilder();
                foreach (byte b in bytes)
                    sb.Append(b.ToString("X2"));
                return sb.ToString();
            }
        }

        private string CleanTitle(string title)
        {
            if (string.IsNullOrEmpty(title))
                return title;

            // Remove common music tags that break search
            string[] tags = { " - Remastered", " - Remaster", " (Remastered)", " (Remaster)", " - Live", " (Live)", " - Radio Edit", "(feat. ", "feat. " };
            foreach (string tag in tags)
            {
                int index = title.IndexOf(tag, StringComparison.OrdinalIgnoreCase);
                if (index > 0)
                {
                    title = title.Substring(0, index);
                }
            }

            int dashIndex = title.IndexOf(" - ");
            if (dashIndex > 0)
            {
                title = title.Substring(0, dashIndex);
            }
            
            title = Regex.Replace(title, @"\s*[\(\[].*?[\)\]]", "");
            return title.Trim();
        }

        private double CalculateMatchScore(string targetArtist, string targetTitle, string foundArtist, string foundTitle)
        {
            double artistScore = this.GetStringSimilarity(targetArtist, foundArtist);
            double titleScore = this.GetStringSimilarity(targetTitle, foundTitle);
            
            // Weight artist higher for general searches, or average them
            return (artistScore * 0.4) + (titleScore * 0.6);
        }

        private double GetStringSimilarity(string s, string t)
        {
            if (string.IsNullOrEmpty(s) || string.IsNullOrEmpty(t)) return 0;
            s = s.ToLowerInvariant().Trim();
            t = t.ToLowerInvariant().Trim();

            if (s == t) return 1.0;
            if (s.Contains(t) || t.Contains(s)) return 0.8;
            
            return 0; // Low similarity
        }

        private bool IsFuzzyMatch(string query, string result)
        {
            return this.GetStringSimilarity(query, result) > 0.7;
        }
    }
}
