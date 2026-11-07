using System;

namespace JunimoJamsRusted.UI
{
    /// <summary>
    /// Butter-smooth, 60 FPS sub-pixel linear marquee controller.
    /// Provides linear pixel translation with generous pauses at endpoints,
    /// eliminating character-jumping stutter.
    /// </summary>
    public sealed class SmoothPixelMarquee
    {
        private string _text = string.Empty;
        private float _pixelOffset = 0f;
        private int _state = 0; // 0: PausedAtStart, 1: ScrollingToEnd, 2: PausedAtEnd, 3: ScrollingToStart
        private int _pauseTimer = 0;

        // ~1.8 seconds pause at start/end endpoints (110 ticks at 60 FPS)
        private const int PAUSE_TICKS = 110;

        // 24 pixels per second = ~0.40 pixels per tick at 60 FPS (calm, readable, smooth)
        public float SpeedPixelsPerSecond { get; set; } = 24.0f;

        public string Text
        {
            get => this._text;
            set
            {
                if (this._text != value)
                {
                    this._text = value ?? string.Empty;
                    this.Reset();
                }
            }
        }

        public float PixelOffset => this._pixelOffset;

        public void Reset()
        {
            this._pixelOffset = 0f;
            this._state = 0;
            this._pauseTimer = PAUSE_TICKS;
        }

        public void Update(float textPixelWidth, float maxVisiblePixelWidth)
        {
            if (textPixelWidth <= maxVisiblePixelWidth || string.IsNullOrEmpty(this._text))
            {
                this._pixelOffset = 0f;
                return;
            }

            float maxScroll = textPixelWidth - maxVisiblePixelWidth;
            float step = this.SpeedPixelsPerSecond / 60.0f;

            switch (this._state)
            {
                case 0: // Paused at start
                    this._pixelOffset = 0f;
                    this._pauseTimer--;
                    if (this._pauseTimer <= 0)
                    {
                        this._state = 1;
                    }
                    break;

                case 1: // Scrolling forward
                    this._pixelOffset += step;
                    if (this._pixelOffset >= maxScroll)
                    {
                        this._pixelOffset = maxScroll;
                        this._state = 2;
                        this._pauseTimer = PAUSE_TICKS;
                    }
                    break;

                case 2: // Paused at end
                    this._pixelOffset = maxScroll;
                    this._pauseTimer--;
                    if (this._pauseTimer <= 0)
                    {
                        this._state = 3;
                    }
                    break;

                case 3: // Scrolling backward
                    this._pixelOffset -= step;
                    if (this._pixelOffset <= 0f)
                    {
                        this._pixelOffset = 0f;
                        this._state = 0;
                        this._pauseTimer = PAUSE_TICKS;
                    }
                    break;
            }
        }
    }
}
