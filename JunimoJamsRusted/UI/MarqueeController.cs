using System;

namespace JunimoJamsRusted.UI
{
    public sealed class MarqueeController
    {
        private string _text = string.Empty;
        private int _maxVisibleChars = 24;
        private int _offset = 0;
        private int _direction = 1;
        private int _pauseTimer = 0;
        private const int PAUSE_TICKS = 45; // ~0.75 seconds pause at endpoints

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
                }
            }
        }

        public int MaxVisibleChars
        {
            get => this._maxVisibleChars;
            set => this._maxVisibleChars = Math.Max(6, value);
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
