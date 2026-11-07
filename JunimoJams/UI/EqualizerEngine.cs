using System;

namespace JunimoJams.UI
{
    public sealed class EqualizerEngine
    {
        private const int BAR_COUNT = 6;
        private readonly float[] _bars = new float[BAR_COUNT];
        private float _phase = 0f;

        public float[] GetBars() => this._bars;

        public void Update(bool isPlaying)
        {
            if (!isPlaying)
            {
                // Smooth decay to rest
                for (int i = 0; i < BAR_COUNT; i++)
                {
                    this._bars[i] = Math.Max(0f, this._bars[i] - 0.08f);
                }
                return;
            }

            this._phase += 0.12f;
            if (this._phase > MathF.PI * 2f)
                this._phase -= MathF.PI * 2f;

            for (int i = 0; i < BAR_COUNT; i++)
            {
                // Phase-shifted harmonic sinusoidal motion
                float wave1 = MathF.Sin(this._phase * 1.5f + (i * 0.9f));
                float wave2 = MathF.Cos(this._phase * 2.3f + (i * 1.4f));
                float combined = (wave1 + wave2 + 2f) / 4f; // 0.0 .. 1.0

                // Minimum height so bar is always slightly visible
                this._bars[i] = Math.Clamp(0.15f + (combined * 0.85f), 0.15f, 1.0f);
            }
        }
    }
}
