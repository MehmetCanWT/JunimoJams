using System;
using Microsoft.Xna.Framework.Graphics;

namespace SpotifyValley.Services
{
    public class TrackInfo : IDisposable
    {
        public string Name { get; set; } = "Unknown";

        public string Artist { get; set; } = "Unknown";

        public bool IsPlaying { get; set; } = false;

        public Texture2D CoverArt { get; set; }

        public string DisplayString => $"{this.Artist} - {this.Name}";

        public void Dispose()
        {
            this.CoverArt?.Dispose();
            this.CoverArt = null;
        }
    }
}
