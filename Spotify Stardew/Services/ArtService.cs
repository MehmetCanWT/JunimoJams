using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Spotify_Stardew.Services
{
    public class ArtService
    {
        private readonly HttpClient _http;
        private string _lastSearchQuery = "";
        private byte[] _lastResultBytes = null;

        public ArtService()
        {
            this._http = new HttpClient();
            this._http.Timeout = TimeSpan.FromSeconds(5.0);
            this._http.DefaultRequestHeaders.Add("User-Agent", "SpotifyStardew/1.0");
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

            try
            {
                string term = $"{artist} {cleanTrackName}";
                string url = $"https://itunes.apple.com/search?term={Uri.EscapeDataString(term)}&entity=song&limit=3";
                string response = await this._http.GetStringAsync(url);
                
                JObject json = JObject.Parse(response);
                JArray results = json["results"] as JArray;
                
                if (results == null || results.Count == 0)
                    return null;

                string bestArtUrl = null;
                foreach (JToken result in results)
                {
                    string foundArtist = result["artistName"]?.ToString() ?? "";
                    if (this.IsFuzzyMatch(artist, foundArtist))
                    {
                        string art = result["artworkUrl100"]?.ToString();
                        if (!string.IsNullOrEmpty(art))
                        {
                            bestArtUrl = art.Replace("100x100", "600x600");
                            break;
                        }
                    }
                }

                if (string.IsNullOrEmpty(bestArtUrl))
                    return null;

                byte[] imageBytes = await this._http.GetByteArrayAsync(bestArtUrl);
                this._lastSearchQuery = queryKey;
                this._lastResultBytes = imageBytes;
                return imageBytes;
            }
            catch
            {
                return null;
            }
        }

        private string CleanTitle(string title)
        {
            if (string.IsNullOrEmpty(title))
                return title;

            int dashIndex = title.IndexOf(" - ");
            if (dashIndex > 0)
            {
                title = title.Substring(0, dashIndex);
            }
            title = Regex.Replace(title, @"\s*[\(\[].*?[\)\]]", "");
            return title.Trim();
        }

        private bool IsFuzzyMatch(string query, string result)
        {
            if (string.IsNullOrEmpty(query) || string.IsNullOrEmpty(result))
                return false;

            query = query.ToLowerInvariant();
            result = result.ToLowerInvariant();
            return query.Contains(result) || result.Contains(query);
        }
    }
}
