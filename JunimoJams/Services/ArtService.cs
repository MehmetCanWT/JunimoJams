using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace JunimoJams.Services
{
    public class CoverArtResult
    {
        public byte[] Bytes { get; set; }
        public string RealArtist { get; set; }
    }

    public class ArtService : IDisposable
    {
        private readonly HttpClient _http;
        private readonly string _cachePath;
        private string _lastSearchQuery = "";
        private CoverArtResult _lastResult;
        private readonly object _cacheLock = new object();

        public ArtService(string modPath)
        {
            this._http = new HttpClient();
            this._http.Timeout = TimeSpan.FromSeconds(5.0);
            this._http.DefaultRequestHeaders.Add("User-Agent", "JunimoJams/1.0");

            this._cachePath = Path.Combine(modPath, "cache");
            Directory.CreateDirectory(this._cachePath);
        }

        public async Task<CoverArtResult> FetchCoverArtBytes(string artist, string trackName, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(artist) || string.IsNullOrWhiteSpace(trackName))
                return null;

            if (artist == "Unknown" || trackName == "Unknown")
                return null;

            string cleanTrackName = this.CleanTitle(trackName);
            string queryKey = $"{artist}-{cleanTrackName}";

            lock (this._cacheLock)
            {
                if (this._lastSearchQuery == queryKey && this._lastResult != null)
                    return this._lastResult;
            }

            // Check local cache first
            byte[] cached = this.GetFromCache(queryKey, out string cachedArtist);
            if (cached != null)
            {
                var result = new CoverArtResult { Bytes = cached, RealArtist = cachedArtist };
                this._lastSearchQuery = queryKey;
                this._lastResult = result;
                return result;
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                bool isGenericArtist = artist.Equals("YouTube Music", StringComparison.OrdinalIgnoreCase);
                string term = isGenericArtist ? cleanTrackName : $"{artist} {cleanTrackName}";
                string url = $"https://itunes.apple.com/search?term={Uri.EscapeDataString(term)}&entity=song&limit=10";
                string response = await this._http.GetStringAsync(url, cancellationToken);

                JObject json = JObject.Parse(response);
                JArray results = json["results"] as JArray;

                if (results == null || results.Count == 0)
                    return null;

                string bestArtUrl = null;
                double bestScore = -1;
                string bestArtist = null;

                foreach (JToken result in results)
                {
                    string foundArtist = result["artistName"]?.ToString() ?? "";
                    string foundTitle = result["trackName"]?.ToString() ?? "";

                    double score = isGenericArtist 
                        ? this.GetStringSimilarity(cleanTrackName, foundTitle)
                        : this.CalculateMatchScore(artist, cleanTrackName, foundArtist, foundTitle);

                    double threshold = isGenericArtist ? 0.85 : 0.5;

                    if (score > bestScore && score > threshold)
                    {
                        string art = result["artworkUrl100"]?.ToString();
                        if (!string.IsNullOrEmpty(art))
                        {
                            bestArtUrl = art.Replace("100x100", "600x600");
                            bestScore = score;
                            if (isGenericArtist)
                            {
                                bestArtist = foundArtist;
                            }
                        }
                    }
                }

                if (string.IsNullOrEmpty(bestArtUrl))
                    return null;

                cancellationToken.ThrowIfCancellationRequested();

                byte[] imageBytes = await this._http.GetByteArrayAsync(bestArtUrl, cancellationToken);
                var finalResult = new CoverArtResult { Bytes = imageBytes, RealArtist = bestArtist };
                
                lock (this._cacheLock)
                {
                    this._lastSearchQuery = queryKey;
                    this._lastResult = finalResult;
                }
                this.SaveToCache(queryKey, imageBytes, bestArtist);

                return finalResult;
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch
            {
                return null;
            }
        }

        private byte[] GetFromCache(string key, out string cachedArtist)
        {
            cachedArtist = null;
            try
            {
                string baseName = Path.Combine(this._cachePath, this.GetCacheFileName(key));
                if (File.Exists(baseName + ".png"))
                {
                    if (File.Exists(baseName + ".txt"))
                    {
                        cachedArtist = File.ReadAllText(baseName + ".txt", Encoding.UTF8);
                    }
                    return File.ReadAllBytes(baseName + ".png");
                }
                return null;
            }
            catch { return null; }
        }

        private void SaveToCache(string key, byte[] bytes, string realArtist)
        {
            try
            {
                string baseName = Path.Combine(this._cachePath, this.GetCacheFileName(key));
                File.WriteAllBytes(baseName + ".png", bytes);
                if (!string.IsNullOrEmpty(realArtist))
                {
                    File.WriteAllText(baseName + ".txt", realArtist, Encoding.UTF8);
                }
            }
            catch { }
        }

        private string GetCacheFileName(string key)
        {
            // Use MD5 hash as a stable, short filename
            byte[] hash = System.Security.Cryptography.MD5.HashData(Encoding.UTF8.GetBytes(key));
            return Convert.ToHexString(hash);
        }

        public void Dispose()
        {
            this._http?.Dispose();
        }

        private string CleanTitle(string title)
        {
            if (string.IsNullOrEmpty(title))
                return title;

            string[] tags = { " - Remastered", " - Remaster", " (Remastered)", " (Remaster)", " - Live", " (Live)", " - Radio Edit", "(feat. ", "feat. " };
            foreach (string tag in tags)
            {
                int index = title.IndexOf(tag, StringComparison.OrdinalIgnoreCase);
                if (index > 0)
                    title = title.Substring(0, index);
            }

            int dashIndex = title.IndexOf(" - ");
            if (dashIndex > 0)
                title = title.Substring(0, dashIndex);

            return Regex.Replace(title, @"\s*[\(\[].*?[\)\]]", "").Trim();
        }

        private double CalculateMatchScore(string targetArtist, string targetTitle, string foundArtist, string foundTitle)
        {
            double artistScore = this.GetStringSimilarity(targetArtist, foundArtist);
            double titleScore = this.GetStringSimilarity(targetTitle, foundTitle);

            // Title is weighted slightly higher as artist names can be generic
            return (artistScore * 0.4) + (titleScore * 0.6);
        }

        private double GetStringSimilarity(string s, string t)
        {
            if (string.IsNullOrEmpty(s) || string.IsNullOrEmpty(t)) return 0;
            s = s.ToLowerInvariant().Trim();
            t = t.ToLowerInvariant().Trim();

            if (s == t) return 1.0;
            if (s.Contains(t) || t.Contains(s)) return 0.85;

            // Jaccard similarity on word tokens
            var wordsS = new HashSet<string>(s.Split(' ', StringSplitOptions.RemoveEmptyEntries));
            var wordsT = new HashSet<string>(t.Split(' ', StringSplitOptions.RemoveEmptyEntries));

            int intersection = 0;
            foreach (var w in wordsS)
            {
                if (wordsT.Contains(w))
                    intersection++;
            }

            int union = wordsS.Count + wordsT.Count - intersection;
            return union == 0 ? 0 : (double)intersection / union;
        }
    }
}
