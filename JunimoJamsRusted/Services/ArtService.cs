using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace JunimoJamsRusted.Services
{
    public sealed class CoverArtResult
    {
        public byte[]? Bytes { get; set; }
        public string? RealArtist { get; set; }
        public bool IsDirectFromOs { get; set; }
    }

    public sealed class ArtService : IDisposable
    {
        private readonly HttpClient _http;
        private readonly string _cachePath;
        private string _lastSearchQuery = string.Empty;
        private CoverArtResult? _lastResult;
        private readonly object _cacheLock = new();

        // Circuit breaker for offline / disconnected environments
        private int _consecutiveFailures = 0;
        private DateTime _circuitBreakerTripUntil = DateTime.MinValue;
        private const int MAX_CONSECUTIVE_FAILURES = 3;
        private static readonly TimeSpan CIRCUIT_BREAKER_DURATION = TimeSpan.FromSeconds(60);

        public ArtService(string modPath)
        {
            this._http = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(4.0)
            };
            this._http.DefaultRequestHeaders.Add("User-Agent", "JunimoJamsRusted/2.0 (StardewValley-SMAPI; +https://github.com/MehmetCanWT)");

            this._cachePath = Path.Combine(modPath, "cache");
            Directory.CreateDirectory(this._cachePath);
        }

        public async Task<CoverArtResult?> FetchCoverArtBytes(
            TrackInfo track,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(track.Name) || string.IsNullOrWhiteSpace(track.Artist))
                return null;

            if (track.Name == "Unknown" || track.Name == "Not Running" || track.Name == "Error")
                return null;

            // ── Tier 0: Direct OS Artwork (MPRIS2 mpris:artUrl, Windows GSMTC, macOS MediaRemote) ──
            if (track.DirectArtBytes != null && track.DirectArtBytes.Length > 0)
            {
                return new CoverArtResult
                {
                    Bytes = track.DirectArtBytes,
                    RealArtist = track.Artist,
                    IsDirectFromOs = true
                };
            }

            if (!string.IsNullOrEmpty(track.DirectArtUri))
            {
                byte[]? directBytes = await this.TryLoadDirectUri(track.DirectArtUri, cancellationToken);
                if (directBytes != null && directBytes.Length > 0)
                {
                    return new CoverArtResult
                    {
                        Bytes = directBytes,
                        RealArtist = track.Artist,
                        IsDirectFromOs = true
                    };
                }
            }

            // Clean title and extract featured artists
            string cleanTrackName = TitleNormalizer.CleanTrackTitle(track.Name, out string? featArtist);
            string cleanArtist = TitleNormalizer.NormalizeUnicode(track.Artist);
            string queryKey = $"{cleanArtist}-{cleanTrackName}";

            lock (this._cacheLock)
            {
                if (this._lastSearchQuery == queryKey && this._lastResult != null)
                    return this._lastResult;
            }

            // ── Tier 1: Local SSD Disk Cache (SHA-256) ──
            byte[]? cached = this.GetFromDiskCache(queryKey, out string? cachedArtist);
            if (cached != null && cached.Length > 0)
            {
                var result = new CoverArtResult
                {
                    Bytes = cached,
                    RealArtist = cachedArtist ?? track.Artist,
                    IsDirectFromOs = false
                };

                lock (this._cacheLock)
                {
                    this._lastSearchQuery = queryKey;
                    this._lastResult = result;
                }
                return result;
            }

            // Check Circuit Breaker before making web requests
            if (DateTime.UtcNow < this._circuitBreakerTripUntil)
            {
                return null; // Circuit open: skip network requests while offline
            }

            // ── Tier 2: Apple iTunes Search API ──
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                bool isGenericArtist = cleanArtist.Equals("YouTube Music", StringComparison.OrdinalIgnoreCase);
                string term = isGenericArtist ? cleanTrackName : $"{cleanArtist} {cleanTrackName}";
                string url = $"https://itunes.apple.com/search?term={Uri.EscapeDataString(term)}&entity=song&limit=8";

                string response = await this._http.GetStringAsync(url, cancellationToken);
                JObject json = JObject.Parse(response);
                JArray? results = json["results"] as JArray;

                if (results == null || results.Count == 0)
                {
                    return null;
                }

                string? bestArtUrl = null;
                double bestScore = -1.0;
                string? bestArtist = null;

                foreach (JToken result in results)
                {
                    string foundArtist = result["artistName"]?.ToString() ?? string.Empty;
                    string foundTitle = result["trackName"]?.ToString() ?? string.Empty;

                    double score = StringSimilarity.CalculateCompositeScore(
                        cleanArtist, cleanTrackName,
                        foundArtist, foundTitle,
                        isGenericArtist
                    );

                    double threshold = isGenericArtist ? 0.80 : 0.45;

                    if (score > bestScore && score > threshold)
                    {
                        string? art = result["artworkUrl100"]?.ToString();
                        if (!string.IsNullOrEmpty(art))
                        {
                            // Dynamic URL upgrade: replace 100x100 with crisp 600x600bb.jpg
                            bestArtUrl = art.Replace("100x100bb.jpg", "600x600bb.jpg")
                                            .Replace("100x100", "600x600");
                            bestScore = score;
                            bestArtist = foundArtist;
                        }
                    }
                }

                if (string.IsNullOrEmpty(bestArtUrl))
                {
                    return null;
                }

                cancellationToken.ThrowIfCancellationRequested();
                byte[] rawImageBytes = await this._http.GetByteArrayAsync(bestArtUrl, cancellationToken);

                // Reset circuit breaker on success
                this._consecutiveFailures = 0;

                var finalResult = new CoverArtResult
                {
                    Bytes = rawImageBytes,
                    RealArtist = bestArtist ?? track.Artist,
                    IsDirectFromOs = false
                };

                lock (this._cacheLock)
                {
                    this._lastSearchQuery = queryKey;
                    this._lastResult = finalResult;
                }

                this.SaveToDiskCache(queryKey, rawImageBytes, bestArtist);
                return finalResult;
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch
            {
                this._consecutiveFailures++;
                if (this._consecutiveFailures >= MAX_CONSECUTIVE_FAILURES)
                {
                    this._circuitBreakerTripUntil = DateTime.UtcNow + CIRCUIT_BREAKER_DURATION;
                }
                return null;
            }
        }

        private async Task<byte[]?> TryLoadDirectUri(string uriString, CancellationToken ct)
        {
            try
            {
                if (uriString.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
                {
                    string localPath = Uri.UnescapeDataString(new Uri(uriString).LocalPath);
                    if (File.Exists(localPath))
                    {
                        return await File.ReadAllBytesAsync(localPath, ct);
                    }
                }
                else if (uriString.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                         uriString.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    return await this._http.GetByteArrayAsync(uriString, ct);
                }
            }
            catch
            {
                // Ignore transient direct loading errors
            }
            return null;
        }

        private byte[]? GetFromDiskCache(string key, out string? cachedArtist)
        {
            cachedArtist = null;
            try
            {
                string hash = this.ComputeSha256(key);
                string imgPath = Path.Combine(this._cachePath, $"{hash}.png");
                string metaPath = Path.Combine(this._cachePath, $"{hash}.json");

                if (File.Exists(imgPath))
                {
                    if (File.Exists(metaPath))
                    {
                        cachedArtist = File.ReadAllText(metaPath, Encoding.UTF8).Trim();
                    }
                    return File.ReadAllBytes(imgPath);
                }
            }
            catch
            {
                // Fallback to network
            }
            return null;
        }

        private void SaveToDiskCache(string key, byte[] bytes, string? realArtist)
        {
            try
            {
                string hash = this.ComputeSha256(key);
                string imgPath = Path.Combine(this._cachePath, $"{hash}.png");
                string tmpPath = Path.Combine(this._cachePath, $"{hash}.tmp");
                string metaPath = Path.Combine(this._cachePath, $"{hash}.json");

                // Atomic write with .tmp
                File.WriteAllBytes(tmpPath, bytes);
                File.Move(tmpPath, imgPath, overwrite: true);

                if (!string.IsNullOrEmpty(realArtist))
                {
                    File.WriteAllText(metaPath, realArtist, Encoding.UTF8);
                }
            }
            catch
            {
                // Non-critical write failure
            }
        }

        private string ComputeSha256(string input)
        {
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
            return Convert.ToHexString(hash);
        }

        public void Dispose()
        {
            this._http.Dispose();
        }
    }
}
