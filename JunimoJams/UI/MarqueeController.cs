using System;

namespace JunimoJams.UI
{
    /// <summary>
    /// Smooth, relaxed back-and-forth marquee text controller.
    /// Provides comfortable readability with generous pauses at endpoints.
    /// </summary>
    public sealed class MarqueeController
    {
        private string _text = string.Empty;
        private int _maxVisibleChars = 24;
        private int _offset = 0;
        private int _direction = 1;
        private int _pauseTimer = 0;
        private int _stepTimer = 0;

        // ~1.6 seconds pause at start and end for relaxed reading
        private const int PAUSE_TICKS = 100;

        // Advance 1 character every 14 game ticks (~4.3 characters per second at 60 FPS)
        private int _stepDelay = 14;

        public string Text
        {
            get => this._text;
            set
            {
                if (this._text != value)
                {
                    this._text = value ?? string.Empty;
                    this._offset = 0;
                    this._direction = 1;
                    this._pauseTimer = PAUSE_TICKS;
                    this._stepTimer = 0;
                }
            }
        }

        public int MaxVisibleChars
        {
            get => this._maxVisibleChars;
            set => this._maxVisibleChars = Math.Max(6, value);
        }

        public int StepDelay
        {
            get => this._stepDelay;
            set => this._stepDelay = Math.Max(4, value);
        }

        public void Update()
        {
            if (this._text.Length <= this._maxVisibleChars)
            {
                this._offset = 0;
                return;
            }

            if (this._pauseTimer > 0)
            {
                this._pauseTimer--;
                return;
            }

            this._stepTimer++;
            if (this._stepTimer < this._stepDelay)
                return;

            this._stepTimer = 0;
            int maxOffset = this._text.Length - this._maxVisibleChars;

            this._offset += this._direction;

            if (this._offset >= maxOffset)
            {
                this._offset = maxOffset;
                this._direction = -1;
                this._pauseTimer = PAUSE_TICKS;
            }
            else if (this._offset <= 0)
            {
                this._offset = 0;
                this._direction = 1;
                this._pauseTimer = PAUSE_TICKS;
            }
        }

        public string GetVisibleText()
        {
            if (string.IsNullOrEmpty(this._text))
                return string.Empty;

            if (this._text.Length <= this._maxVisibleChars)
                return this._text;

            int len = Math.Min(this._maxVisibleChars, this._text.Length - this._offset);
            return this._text.Substring(this._offset, len);
        }
    }
}
