using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using JunimoJams.Services;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;

namespace JunimoJams.UI
{
    public class MusicOverlay : IDisposable
    {
        private readonly ModConfig _config;
        private readonly IModHelper _helper;
        private Texture2D _backgroundTexture;
        private Texture2D _buttonsTexture;
        private Texture2D _buttonBgTexture;
        
        private Texture2D _btnPlayTexture;
        private Texture2D _btnPauseTexture;
        private Texture2D _btnNextTexture;
        private Texture2D _btnPrevTexture;
        
        private Rectangle _prevRect;
        private Rectangle _playPauseRect;
        private Rectangle _nextRect;
        private Rectangle _mainBoxRect;
        
        private string _lastTrackName = "";
        private string _lastArtistName = "";
        private bool _layoutDirty = true;

        // Fade animation — smooth transition when track changes
        private float _fadeAlpha = 1f;
        private bool _isFading = false;
        private int _fadeTimer = 0;
        private const int FADE_DURATION = 18; // ~300ms at 60fps

        // Cached Layout Values
        private float _cachedScale;
        private Vector2 _cachedPosition;
        private float _cachedArtSize;
        private float _cachedArtGap;
        private float _cachedContentW;
        private float _cachedBoxW;
        private float _cachedBoxH;
        private float _cachedFramePaddingX;
        private float _cachedExtraIndent;  // Extra indent on both left & right for custom backgrounds
        private float _cachedActualTextW;  // Real measured width of the widest text line (pre-truncation cap)
        private string _cachedDisplaySong;
        private string _cachedDisplayArtist;
        private float _cachedLineHeight;
        private float _cachedTextBlockHeight;

        private readonly Rectangle SRC_PREV = new Rectangle(0, 0, 16, 16);
        private readonly Rectangle SRC_PLAY = new Rectangle(16, 0, 16, 16);
        private readonly Rectangle SRC_PAUSE = new Rectangle(32, 0, 16, 16);
        private readonly Rectangle SRC_NEXT = new Rectangle(48, 0, 16, 16);

        // ─── Theme Asset Dimensions ────────────────────────────────────────────────
        // When generating theme PNGs with AI (e.g. Nano Banana), use these exact sizes:
        //
        //   background.png  → 400 × 150 px  (wide rectangle, landscape, NOT square)
        //   btn_play.png    → 128 × 128 px  (perfect square, icon centered)
        //   btn_pause.png   → 128 × 128 px  (perfect square, icon centered)
        //   btn_next.png    → 128 × 128 px  (perfect square, icon centered)
        //   btn_prev.png    → 128 × 128 px  (perfect square, icon centered)
        //
        // All files must have a pitch-black (#000000) exterior background.
        // The mod automatically removes it via flood-fill on load.
        // See assets/THEME_SPEC.md for full details and AI prompt templates.
        // ──────────────────────────────────────────────────────────────────────────

        public MusicOverlay(ModConfig config, IModHelper helper)
        {
            this._config = config;
            this._helper = helper;
            this.LoadTextures(this._config.Theme);
        }

        public void ReloadTextures()
        {
            this.LoadTextures(this._config.Theme);
            this._layoutDirty = true;
        }

        private void LoadTextures(string theme)
        {
            // Dispose old textures to prevent memory leaks when switching themes
            this._backgroundTexture?.Dispose();
            this._buttonsTexture?.Dispose();
            this._buttonBgTexture?.Dispose();
            
            this._btnPlayTexture?.Dispose();
            this._btnPauseTexture?.Dispose();
            this._btnNextTexture?.Dispose();
            this._btnPrevTexture?.Dispose();

            this._backgroundTexture = this.LoadExternalTexture(theme, "background.png");
            this._buttonsTexture = this.LoadExternalTexture(theme, "buttons.png");
            this._buttonBgTexture = this.LoadExternalTexture(theme, "button_bg.png");
            
            this._btnPlayTexture = this.LoadExternalTexture(theme, "btn_play.png");
            this._btnPauseTexture = this.LoadExternalTexture(theme, "btn_pause.png");
            this._btnNextTexture = this.LoadExternalTexture(theme, "btn_next.png");
            this._btnPrevTexture = this.LoadExternalTexture(theme, "btn_prev.png");
        }

        private Texture2D LoadExternalTexture(string theme, string fileName)
        {
            try
            {
                string path = System.IO.Path.Combine(this._helper.DirectoryPath, "assets", theme, fileName);
                if (System.IO.File.Exists(path))
                {
                    using (var stream = System.IO.File.OpenRead(path))
                    {
                        Texture2D tex = Texture2D.FromStream(Game1.graphics.GraphicsDevice, stream);
                        this.RemoveBackground(tex);
                        return tex;
                    }
                }
            }
            catch
            {
                // Ignore load errors (fallback to default will be used)
            }
            return null;
        }

        private void RemoveBackground(Texture2D texture)
        {
            Color[] data = new Color[texture.Width * texture.Height];
            texture.GetData(data);
            
            Queue<Point> queue = new Queue<Point>();
            queue.Enqueue(new Point(0, 0)); // Start from top-left corner
            queue.Enqueue(new Point(texture.Width - 1, 0)); // Top-right corner
            queue.Enqueue(new Point(0, texture.Height - 1)); // Bottom-left corner
            queue.Enqueue(new Point(texture.Width - 1, texture.Height - 1)); // Bottom-right corner
            
            Color targetColor = data[0];
            if (targetColor.A == 0) return; // Already transparent
            
            bool[] visited = new bool[data.Length];
            
            while (queue.Count > 0)
            {
                Point p = queue.Dequeue();
                int idx = p.Y * texture.Width + p.X;
                
                if (visited[idx]) continue;
                visited[idx] = true;

                // Color tolerance to account for AI compression/slight blending
                if (Math.Abs(data[idx].R - targetColor.R) < 15 && 
                    Math.Abs(data[idx].G - targetColor.G) < 15 && 
                    Math.Abs(data[idx].B - targetColor.B) < 15)
                {
                    data[idx] = Color.Transparent;
                    
                    if (p.X > 0) queue.Enqueue(new Point(p.X - 1, p.Y));
                    if (p.X < texture.Width - 1) queue.Enqueue(new Point(p.X + 1, p.Y));
                    if (p.Y > 0) queue.Enqueue(new Point(p.X, p.Y - 1));
                    if (p.Y < texture.Height - 1) queue.Enqueue(new Point(p.X, p.Y + 1));
                }
            }
            
            texture.SetData(data);
        }

        public void OnTrackChanged()
        {
            this._layoutDirty = true;
            this._fadeAlpha = 0f;
            this._isFading = true;
            this._fadeTimer = 0;
        }

        private void UpdateLayout(TrackInfo track, SpriteFont font)
        {
            this._cachedScale = this._config.HudScale;
            this._cachedPosition = new Vector2(this._config.HudPositionX, this._config.HudPositionY);

            bool hasArt = track.CoverArt != null && this._config.ShowAlbumArt;

            this._cachedLineHeight = font.LineSpacing * this._cachedScale;
            this._cachedTextBlockHeight = this._cachedLineHeight * 2f + 4f * this._cachedScale;
            this._cachedArtSize = hasArt ? 80f * this._cachedScale : 0f;
            this._cachedArtGap = hasArt ? 16f * this._cachedScale : 0f;

            if (this._backgroundTexture != null)
            {
                // ── Fixed mode: box is always exactly the background image size ──────────
                // This keeps the HUD stable regardless of track name length.
                // Fixed size: match background image, then add vertical breathing room
                // so album art doesn't clip into the frame's vine/flower decorations
                this._cachedBoxW = this._backgroundTexture.Width * this._cachedScale;
                this._cachedBoxH = (this._backgroundTexture.Height + 20f) * this._cachedScale;

                this._cachedExtraIndent = 8f * this._cachedScale;

                // Padding: estimate frame border as 1/4 of image width, clamped 48–80px
                float basePadding = Math.Max(48f, Math.Min(80f, (float)this._backgroundTexture.Width / 4f));
                this._cachedFramePaddingX = basePadding * this._cachedScale;
                float framePaddingY = basePadding * this._cachedScale;

                // Content width fills whatever's left after padding, art, and extra indent
                this._cachedContentW = this._cachedBoxW
                    - this._cachedFramePaddingX * 2f
                    - this._cachedArtSize
                    - this._cachedArtGap
                    - this._cachedExtraIndent * 2f;
                this._cachedContentW = Math.Max(this._cachedContentW, 60f * this._cachedScale);
            }
            else
            {
                // ── Dynamic mode: box grows to fit content (no custom background) ────────
                this._cachedExtraIndent = 0f;
                float basePadding = 24f;
                this._cachedFramePaddingX = basePadding * this._cachedScale;
                float framePaddingY = basePadding * this._cachedScale;

                float rawTextWidth = Math.Max(font.MeasureString(track.Name).X, font.MeasureString(track.Artist).X) * this._cachedScale;
                this._cachedContentW = Math.Min(rawTextWidth, 220f * this._cachedScale);

                float innerWidth = this._cachedArtSize + this._cachedArtGap + this._cachedContentW;
                float innerHeight = Math.Max(this._cachedArtSize, this._cachedTextBlockHeight);

                this._cachedBoxW = Math.Max(innerWidth + this._cachedFramePaddingX * 2f, 128f * this._cachedScale);
                this._cachedBoxH = Math.Max(innerHeight + framePaddingY * 2f, 64f * this._cachedScale);
            }

            // Measure actual text width (used for centering short tracks)
            this._cachedActualTextW = Math.Max(
                font.MeasureString(track.Name).X,
                font.MeasureString(track.Artist).X) * this._cachedScale;

            this._mainBoxRect = new Rectangle((int)this._cachedPosition.X, (int)this._cachedPosition.Y, (int)this._cachedBoxW, (int)this._cachedBoxH);

            if (this._config.ShowPlaybackButtons)
            {
                // Unify custom texture sizing 
                bool hasCustomButtons = this._btnPlayTexture != null || this._buttonBgTexture != null;
                float btnSize = (hasCustomButtons ? 48f : 24f) * this._cachedScale;
                float spacing = 16f * this._cachedScale; 
                float buttonsRowW = btnSize * 3f + spacing * 2f;
                
                float btnY = this._mainBoxRect.Bottom + 16f * this._cachedScale;
                float btnRowStartX = this._mainBoxRect.X + (this._mainBoxRect.Width / 2f - buttonsRowW / 2f);

                this._prevRect = new Rectangle((int)btnRowStartX, (int)btnY, (int)btnSize, (int)btnSize);
                this._playPauseRect = new Rectangle((int)(btnRowStartX + btnSize + spacing), (int)btnY, (int)btnSize, (int)btnSize);
                this._nextRect = new Rectangle((int)(btnRowStartX + (btnSize + spacing) * 2f), (int)btnY, (int)btnSize, (int)btnSize);
            }

            this._lastTrackName = track.Name;
            this._lastArtistName = track.Artist;

            // Compute display strings (truncated to contentW) — done after contentW is finalized
            string normalizedTrack = StringUtils.NormalizeForHud(track.Name);
            string normalizedArtist = StringUtils.NormalizeForHud(track.Artist);
            this._cachedDisplaySong = this.TruncateText(font, normalizedTrack, this._cachedContentW, this._cachedScale);
            this._cachedDisplayArtist = this.TruncateText(font, normalizedArtist, this._cachedContentW, this._cachedScale);

            this._layoutDirty = false;
        }

        public void Draw(SpriteBatch spriteBatch, TrackInfo track)
        {
            if (track == null)
                return;

            SpriteFont font = Game1.smallFont;
            
            // Recalculate layout only if necessary
            if (this._layoutDirty || track.Name != this._lastTrackName || track.Artist != this._lastArtistName)
            {
                this.UpdateLayout(track, font);
            }

            // Update fade animation
            if (this._isFading)
            {
                this._fadeTimer++;
                this._fadeAlpha = Math.Min(1f, (float)this._fadeTimer / FADE_DURATION);
                if (this._fadeAlpha >= 1f)
                    this._isFading = false;
            }

            Color drawColor = Color.White * this._fadeAlpha;
            Color textColor = Game1.textColor * this._fadeAlpha;
            Color artistColor = Game1.textColor * (0.75f * this._fadeAlpha);

            // Draw Backgrounds
            if (this._backgroundTexture != null)
            {
                // Stretch the background to exactly fit the calculated box rect
                // (background image was generated at arbitrary size, so always stretch-draw it)
                spriteBatch.Draw(this._backgroundTexture, this._mainBoxRect, drawColor);

                if (this._config.ShowPlaybackButtons && this._btnPlayTexture == null)
                {
                    Texture2D btnBg = this._buttonBgTexture ?? this._backgroundTexture;
                    IClickableMenu.drawTextureBox(spriteBatch, btnBg, new Rectangle(0, 0, btnBg.Width, btnBg.Height), this._prevRect.X, this._prevRect.Y, this._prevRect.Width, this._prevRect.Height, drawColor, this._cachedScale, false);
                    IClickableMenu.drawTextureBox(spriteBatch, btnBg, new Rectangle(0, 0, btnBg.Width, btnBg.Height), this._playPauseRect.X, this._playPauseRect.Y, this._playPauseRect.Width, this._playPauseRect.Height, drawColor, this._cachedScale, false);
                    IClickableMenu.drawTextureBox(spriteBatch, btnBg, new Rectangle(0, 0, btnBg.Width, btnBg.Height), this._nextRect.X, this._nextRect.Y, this._nextRect.Width, this._nextRect.Height, drawColor, this._cachedScale, false);
                }
            }
            else
            {
                IClickableMenu.drawTextureBox(spriteBatch, Game1.menuTexture, new Rectangle(0, 256, 60, 60), this._mainBoxRect.X, this._mainBoxRect.Y, this._mainBoxRect.Width, this._mainBoxRect.Height, drawColor, this._cachedScale, false);
                if (this._config.ShowPlaybackButtons && this._btnPlayTexture == null)
                {
                    IClickableMenu.drawTextureBox(spriteBatch, Game1.menuTexture, new Rectangle(0, 256, 60, 60), this._prevRect.X, this._prevRect.Y, this._prevRect.Width, this._prevRect.Height, drawColor, this._cachedScale, false);
                    IClickableMenu.drawTextureBox(spriteBatch, Game1.menuTexture, new Rectangle(0, 256, 60, 60), this._playPauseRect.X, this._playPauseRect.Y, this._playPauseRect.Width, this._playPauseRect.Height, drawColor, this._cachedScale, false);
                    IClickableMenu.drawTextureBox(spriteBatch, Game1.menuTexture, new Rectangle(0, 256, 60, 60), this._nextRect.X, this._nextRect.Y, this._nextRect.Width, this._nextRect.Height, drawColor, this._cachedScale, false);
                }
            }

            float startX = this._cachedPosition.X + this._cachedFramePaddingX + this._cachedExtraIndent;
            float centerY = this._cachedPosition.Y + this._cachedBoxH / 2f;

            // Draw Album Art
            if (track.CoverArt != null && this._config.ShowAlbumArt)
            {
                float artY = centerY - this._cachedArtSize / 2f;
                this.DrawRoundedArt(spriteBatch, track.CoverArt,
                    new Rectangle((int)startX, (int)artY, (int)this._cachedArtSize, (int)this._cachedArtSize),
                    radius: (int)(10f * this._cachedScale), alpha: this._fadeAlpha);
                startX += this._cachedArtSize + this._cachedArtGap;
            }

            // Draw Text
            float textStartY = centerY - this._cachedTextBlockHeight / 2f;
            float textX;

            bool showingArt = track.CoverArt != null && this._config.ShowAlbumArt;
            if (showingArt)
            {
                // Album art is visible: text starts right after it
                textX = startX;
            }
            else
            {
                // No album art: center based on actual text width (not the max content area)
                // This keeps short names like "Supernova" centered, not left-aligned
                float actualW = Math.Min(this._cachedActualTextW, this._cachedContentW);
                textX = this._cachedPosition.X + this._cachedBoxW / 2f - actualW / 2f;
            }

            // Draw text shadow for better readability on custom backgrounds
            if (this._backgroundTexture != null)
            {
                Color shadowColor = Color.Black * (0.3f * this._fadeAlpha);
                Vector2 shadowOffset = new Vector2(1f * this._cachedScale, 1f * this._cachedScale);
                spriteBatch.DrawString(font, this._cachedDisplaySong, new Vector2(textX, textStartY) + shadowOffset, shadowColor, 0f, Vector2.Zero, this._cachedScale, SpriteEffects.None, 1f);
                spriteBatch.DrawString(font, this._cachedDisplayArtist, new Vector2(textX, textStartY + this._cachedLineHeight) + shadowOffset, shadowColor, 0f, Vector2.Zero, this._cachedScale, SpriteEffects.None, 1f);
            }

            spriteBatch.DrawString(font, this._cachedDisplaySong, new Vector2(textX, textStartY), textColor, 0f, Vector2.Zero, this._cachedScale, SpriteEffects.None, 1f);
            spriteBatch.DrawString(font, this._cachedDisplayArtist, new Vector2(textX, textStartY + this._cachedLineHeight), artistColor, 0f, Vector2.Zero, this._cachedScale, SpriteEffects.None, 1f);

            // Draw Playback Controls
            if (this._config.ShowPlaybackButtons)
            {
                if (this._btnPlayTexture != null)
                {
                    Color prevBtnColor = this.IsHovering(this._prevRect) ? drawColor : drawColor * 0.85f;
                    Color ppBtnColor = this.IsHovering(this._playPauseRect) ? drawColor : drawColor * 0.85f;
                    Color nextBtnColor = this.IsHovering(this._nextRect) ? drawColor : drawColor * 0.85f;

                    spriteBatch.Draw(this._btnPrevTexture, this._prevRect, prevBtnColor);
                    spriteBatch.Draw(track.IsPlaying ? this._btnPauseTexture : this._btnPlayTexture, this._playPauseRect, ppBtnColor);
                    spriteBatch.Draw(this._btnNextTexture, this._nextRect, nextBtnColor);
                }
                else if (this._buttonsTexture != null)
                {
                    spriteBatch.Draw(this._buttonsTexture, this._prevRect, SRC_PREV, drawColor);
                    spriteBatch.Draw(this._buttonsTexture, this._playPauseRect, track.IsPlaying ? SRC_PAUSE : SRC_PLAY, drawColor);
                    spriteBatch.Draw(this._buttonsTexture, this._nextRect, SRC_NEXT, drawColor);
                }
                else
                {
                    this.DrawFallbackButton(spriteBatch, "[<<]", this._prevRect, this._cachedScale, textColor);
                    this.DrawFallbackButton(spriteBatch, track.IsPlaying ? "[||]" : "[>]", this._playPauseRect, this._cachedScale, textColor);
                    this.DrawFallbackButton(spriteBatch, "[>>]", this._nextRect, this._cachedScale, textColor);
                }
            }
        }

        private void DrawFallbackButton(SpriteBatch b, string text, Rectangle rect, float scale, Color color)
        {
            SpriteFont font = Game1.smallFont;
            Vector2 size = font.MeasureString(text) * scale;
            Vector2 pos = new Vector2(rect.X + (rect.Width - size.X) / 2f, rect.Y + (rect.Height - size.Y) / 2f);
            b.DrawString(font, text, pos, color, 0f, Vector2.Zero, scale, SpriteEffects.None, 1f);
        }

        /// <summary>
        /// Draws album art with rounded corners by masking corner pixels via a cached rounded texture.
        /// The technique: copy art pixels to a temp texture, set corner pixels transparent, then draw it.
        /// Cached per-track to avoid per-frame pixel operations.
        /// </summary>
        private Texture2D _roundedArtCache;
        private string _roundedArtCacheKey = "";

        private void DrawRoundedArt(SpriteBatch spriteBatch, Texture2D source, Rectangle dest, int radius, float alpha = 1f)
        {
            // Cache key: texture reference + dest size (avoid rebuilding every frame)
            string cacheKey = $"{source.GetHashCode()}_{dest.Width}_{dest.Height}_{radius}";
            if (this._roundedArtCache == null || this._roundedArtCacheKey != cacheKey)
            {
                this._roundedArtCache?.Dispose();
                this._roundedArtCache = this.BuildRoundedTexture(source, dest.Width, dest.Height, radius);
                this._roundedArtCacheKey = cacheKey;
            }

            spriteBatch.Draw(this._roundedArtCache, dest, Color.White * alpha);
        }

        private Texture2D BuildRoundedTexture(Texture2D source, int w, int h, int radius)
        {
            // Scale source pixels into a w×h buffer
            Color[] srcData = new Color[source.Width * source.Height];
            source.GetData(srcData);

            Color[] dst = new Color[w * h];
            float sx = (float)source.Width / w;
            float sy = (float)source.Height / h;

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int srcX = (int)(x * sx);
                    int srcY = (int)(y * sy);
                    dst[y * w + x] = srcData[srcY * source.Width + srcX];
                }
            }

            // Set corner pixels transparent using circle distance test
            int r = Math.Min(radius, Math.Min(w, h) / 2);
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    // Find closest corner
                    int cx = (x < w / 2) ? r : w - 1 - r;
                    int cy = (y < h / 2) ? r : h - 1 - r;
                    float dx = x - cx;
                    float dy = y - cy;
                    bool inCornerZone = (x < r || x >= w - r) && (y < r || y >= h - r);
                    if (inCornerZone && (dx * dx + dy * dy) > (float)(r * r))
                        dst[y * w + x] = Color.Transparent;
                }
            }

            Texture2D tex = new Texture2D(Game1.graphics.GraphicsDevice, w, h);
            tex.SetData(dst);
            return tex;
        }

        private bool IsHovering(Rectangle rect)
        {
            return rect.Contains(new Point(Game1.getMouseX(true), Game1.getMouseY(true)));
        }

        public string HandleClick(int x, int y)
        {
            if (!this._config.ShowPlaybackButtons)
                return null;

            Point p = new Point(x, y);
            if (this._playPauseRect.Contains(p))
                return "playpause";
            if (this._nextRect.Contains(p))
                return "next";
            if (this._prevRect.Contains(p))
                return "prev";

            return null;
        }

        private string TruncateText(SpriteFont font, string text, float maxWidth, float scale)
        {
            if (string.IsNullOrEmpty(text))
                return "";

            if (font.MeasureString(text).X * scale <= maxWidth)
                return text;

            string ellipsis = "...";
            float ellipsisWidth = font.MeasureString(ellipsis).X * scale;

            for (int i = text.Length - 1; i > 0; i--)
            {
                string t = text.Substring(0, i);
                if (font.MeasureString(t).X * scale + ellipsisWidth <= maxWidth)
                    return t + ellipsis;
            }
            return "...";
        }

        public void Dispose()
        {
            this._backgroundTexture?.Dispose();
            this._buttonsTexture?.Dispose();
            this._buttonBgTexture?.Dispose();
            this._btnPlayTexture?.Dispose();
            this._btnPauseTexture?.Dispose();
            this._btnNextTexture?.Dispose();
            this._btnPrevTexture?.Dispose();
            this._roundedArtCache?.Dispose();

            this._backgroundTexture = null;
            this._buttonsTexture = null;
            this._buttonBgTexture = null;
            this._btnPlayTexture = null;
            this._btnPauseTexture = null;
            this._btnNextTexture = null;
            this._btnPrevTexture = null;
            this._roundedArtCache = null;
        }
    }
}
