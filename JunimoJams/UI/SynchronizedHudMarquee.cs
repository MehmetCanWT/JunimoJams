using System;

namespace JunimoJams.UI
{
    /// <summary>
    /// Synchronized 60 FPS sub-pixel marquee controller.
    /// When both Title and Artist overflow their bounding boxes, both scroll in perfect lockstep:
    /// pausing together at start, gliding forward together, pausing together at end, and gliding back.
    /// </summary>
    public sealed class SynchronizedHudMarquee
    {
        private string _lastTitle = string.Empty;
        private string _lastArtist = string.Empty;

        private float _titleOffset = 0f;
        private float _artistOffset = 0f;

        private int _state = 0; // 0: PausedAtStart, 1: ScrollingToEnd, 2: PausedAtEnd, 3: ScrollingToStart
        private int _timer = 0;
        private int _scrollDurationTicks = 180;

        // ~1.8 seconds pause at endpoints (110 ticks at 60 FPS)
        private const int PAUSE_TICKS = 110;

        // Base readable speed in pixels per second (~20 px/sec)
        public float SpeedPixelsPerSecond { get; set; } = 20.0f;

        public float TitleOffset => this._titleOffset;
        public float ArtistOffset => this._artistOffset;

        public void Reset()
        {
            this._titleOffset = 0f;
            this._artistOffset = 0f;
            this._state = 0;
            this._timer = PAUSE_TICKS;
        }

        public void Update(
            string title, float titlePixelWidth, float maxTitleWidth,
            string artist, float artistPixelWidth, float maxArtistWidth)
        {
            if (title != this._lastTitle || artist != this._lastArtist)
            {
                this._lastTitle = title;
                this._lastArtist = artist;
                this.Reset();
            }

            float maxTitleScroll = Math.Max(0f, titlePixelWidth - maxTitleWidth);
            float maxArtistScroll = Math.Max(0f, artistPixelWidth - maxArtistWidth);

            bool titleNeedsScroll = maxTitleScroll > 2f;
            bool artistNeedsScroll = maxArtistScroll > 2f;

            if (!titleNeedsScroll && !artistNeedsScroll)
            {
                this._titleOffset = 0f;
                this._artistOffset = 0f;
                return;
            }

            // Duration based on the longer scroll distance to maintain smooth, calm speed
            float maxScroll = Math.Max(maxTitleScroll, maxArtistScroll);
            float durationSec = Math.Max(3.0f, maxScroll / this.SpeedPixelsPerSecond);
            this._scrollDurationTicks = Math.Max(60, (int)(durationSec * 60f));

            switch (this._state)
            {
                case 0: // Paused at start
                    this._titleOffset = 0f;
                    this._artistOffset = 0f;
                    this._timer--;
                    if (this._timer <= 0)
                    {
                        this._state = 1;
                        this._timer = 0;
                    }
                    break;

                case 1: // Scrolling forward together
                    this._timer++;
                    float progressForward = Math.Clamp((float)this._timer / this._scrollDurationTicks, 0f, 1f);
                    this._titleOffset = titleNeedsScroll ? (maxTitleScroll * progressForward) : 0f;
                    this._artistOffset = artistNeedsScroll ? (maxArtistScroll * progressForward) : 0f;

                    if (this._timer >= this._scrollDurationTicks)
                    {
                        this._state = 2;
                        this._timer = PAUSE_TICKS;
                    }
                    break;

                case 2: // Paused at end
                    this._titleOffset = titleNeedsScroll ? maxTitleScroll : 0f;
                    this._artistOffset = artistNeedsScroll ? maxArtistScroll : 0f;
                    this._timer--;
                    if (this._timer <= 0)
                    {
                        this._state = 3;
                        this._timer = 0;
                    }
                    break;

                case 3: // Scrolling backward together
                    this._timer++;
                    float progressBackward = 1f - Math.Clamp((float)this._timer / this._scrollDurationTicks, 0f, 1f);
                    this._titleOffset = titleNeedsScroll ? (maxTitleScroll * progressBackward) : 0f;
                    this._artistOffset = artistNeedsScroll ? (maxArtistScroll * progressBackward) : 0f;

                    if (this._timer >= this._scrollDurationTicks)
                    {
                        this._state = 0;
                        this._timer = PAUSE_TICKS;
                    }
                    break;
            }
        }
    }
}
