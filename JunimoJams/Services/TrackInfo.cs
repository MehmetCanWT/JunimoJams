using System;
using Microsoft.Xna.Framework.Graphics;

namespace JunimoJams.Services
{
    public class TrackInfo : IDisposable
    {
        public string Name { get; set; } = "Unknown";
        public string Artist { get; set; } = "Unknown";
        public string Album { get; set; } = "";
        public bool IsPlaying { get; set; } = false;

        /// <summary>
        /// Total track duration.
        /// </summary>
        public TimeSpan Duration { get; set; } = TimeSpan.Zero;

        /// <summary>
        /// Last reported track position.
        /// </summary>
        public TimeSpan Position { get; set; } = TimeSpan.Zero;

        /// <summary>
        /// Playback rate multiplier (1.0 = normal, 0.0 = paused).
        /// </summary>
        public float PlaybackRate { get; set; } = 1.0f;

        /// <summary>
        /// Timestamp when Position was recorded (used for zero-IPC client-side linear extrapolation).
        /// </summary>
        public DateTime LastPositionUpdateTime { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Direct artwork bytes extracted locally from OS (MPRIS2 mpris:artUrl, Windows GSMTC, or macOS MediaRemote).
        /// When present, external iTunes Web API queries are completely bypassed.
        /// </summary>
        public byte[]? DirectArtBytes { get; set; }

        /// <summary>
        /// Direct artwork URI (e.g. file:/// or https://i.scdn.co/image/...).
        /// </summary>
        public string? DirectArtUri { get; set; }

        public Texture2D? CoverArt { get; set; }

        public string DisplayString => $"{this.Artist} - {this.Name}";

        /// <summary>
        /// Extrapolates current position in real-time with 0 IPC overhead.
        /// </summary>
        public TimeSpan GetExtrapolatedPosition()
        {
            if (!this.IsPlaying || this.Duration <= TimeSpan.Zero || this.PlaybackRate <= 0f)
                return this.Position;

            double elapsedSec = (DateTime.UtcNow - this.LastPositionUpdateTime).TotalSeconds;
            double extrapolatedSec = this.Position.TotalSeconds + (elapsedSec * this.PlaybackRate);

            if (extrapolatedSec > this.Duration.TotalSeconds)
                extrapolatedSec = this.Duration.TotalSeconds;

            return TimeSpan.FromSeconds(Math.Max(0, extrapolatedSec));
        }

        public void Dispose()
        {
            this.CoverArt?.Dispose();
            this.CoverArt = null;
            this.DirectArtBytes = null;
        }
    }
}
