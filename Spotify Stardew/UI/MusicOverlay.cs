using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Spotify_Stardew.Services;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;

namespace Spotify_Stardew.UI
{
    public class MusicOverlay
    {
        private readonly ModConfig _config;
        private readonly IModHelper _helper;
        private Texture2D _backgroundTexture;
        private Texture2D _buttonsTexture;
        
        private Rectangle _prevRect;
        private Rectangle _playPauseRect;
        private Rectangle _nextRect;
        private Rectangle _boxRect;
        
        private string _lastTrackName = "";
        private string _lastArtistName = "";
        private bool _layoutDirty = true;

        // Cached Layout Values
        private float _cachedScale;
        private Vector2 _cachedPosition;
        private float _cachedArtSize;
        private float _cachedArtGap;
        private float _cachedContentW;
        private float _cachedBoxW;
        private float _cachedBoxH;
        private float _cachedFramePaddingX;
        private string _cachedDisplaySong;
        private string _cachedDisplayArtist;
        private float _cachedLineHeight;
        private float _cachedTextBlockHeight;

        private readonly Rectangle SRC_PREV = new Rectangle(0, 0, 16, 16);
        private readonly Rectangle SRC_PLAY = new Rectangle(16, 0, 16, 16);
        private readonly Rectangle SRC_PAUSE = new Rectangle(32, 0, 16, 16);
        private readonly Rectangle SRC_NEXT = new Rectangle(48, 0, 16, 16);

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
            try
            {
                this._backgroundTexture = this._helper.ModContent.Load<Texture2D>($"assets/{theme}/background.png");
            }
            catch
            {
                this._backgroundTexture = null;
            }

            try
            {
                this._buttonsTexture = this._helper.ModContent.Load<Texture2D>($"assets/{theme}/buttons.png");
            }
            catch
            {
                this._buttonsTexture = null;
            }
        }

        public void OnTrackChanged()
        {
            this._layoutDirty = true;
        }

        private void UpdateLayout(TrackInfo track, SpriteFont font)
        {
            this._cachedScale = this._config.HudScale;
            this._cachedPosition = new Vector2(this._config.HudPositionX, this._config.HudPositionY);

            bool hasArt = track.CoverArt != null && this._config.ShowAlbumArt;
            float basePadding = 24f;

            if (this._backgroundTexture != null)
            {
                basePadding = Math.Min(64f, (float)this._backgroundTexture.Width / 3f);
            }

            this._cachedFramePaddingX = basePadding * this._cachedScale;
            float framePaddingY = basePadding * this._cachedScale;
            this._cachedLineHeight = font.LineSpacing * this._cachedScale;
            this._cachedTextBlockHeight = this._cachedLineHeight * 2f + 4f * this._cachedScale;
            this._cachedArtSize = hasArt ? 80f * this._cachedScale : 0f;
            this._cachedArtGap = hasArt ? 16f * this._cachedScale : 0f;

            float rawTextWidth = Math.Max(font.MeasureString(track.Name).X, font.MeasureString(track.Artist).X) * this._cachedScale;
            this._cachedContentW = Math.Min(rawTextWidth, 350f * this._cachedScale);

            this._cachedDisplaySong = this.TruncateText(font, track.Name, this._cachedContentW, this._cachedScale);
            this._cachedDisplayArtist = this.TruncateText(font, track.Artist, this._cachedContentW, this._cachedScale);

            float innerWidth = this._cachedArtSize + this._cachedArtGap + this._cachedContentW;
            float innerHeight = Math.Max(this._cachedArtSize, this._cachedTextBlockHeight);

            this._cachedBoxW = Math.Max(innerWidth + this._cachedFramePaddingX * 2f, 128f * this._cachedScale);
            this._cachedBoxH = Math.Max(innerHeight + framePaddingY * 2f, 64f * this._cachedScale);

            this._boxRect = new Rectangle((int)this._cachedPosition.X, (int)this._cachedPosition.Y, (int)this._cachedBoxW, (int)this._cachedBoxH);

            if (this._config.ShowPlaybackButtons)
            {
                float btnSize = 16f * this._cachedScale;
                float buttonsTotalWidth = btnSize * 3f + 60f * this._cachedScale;
                float boxCenterX = this._boxRect.X + this._boxRect.Width / 2f;
                float btnGroupStartX = boxCenterX - buttonsTotalWidth / 2f;
                float buttonY = this._boxRect.Bottom + 8f * this._cachedScale;

                this._prevRect = new Rectangle((int)btnGroupStartX, (int)buttonY, (int)btnSize, (int)btnSize);
                this._playPauseRect = new Rectangle((int)(btnGroupStartX + btnSize + 30f * this._cachedScale), (int)buttonY, (int)btnSize, (int)btnSize);
                this._nextRect = new Rectangle((int)(btnGroupStartX + btnSize * 2f + 60f * this._cachedScale), (int)buttonY, (int)btnSize, (int)btnSize);
            }

            this._lastTrackName = track.Name;
            this._lastArtistName = track.Artist;
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

            // Draw Background
            if (this._backgroundTexture != null)
            {
                IClickableMenu.drawTextureBox(spriteBatch, this._backgroundTexture, new Rectangle(0, 0, this._backgroundTexture.Width, this._backgroundTexture.Height), this._boxRect.X, this._boxRect.Y, this._boxRect.Width, this._boxRect.Height, Color.White, this._cachedScale, false);
            }
            else
            {
                IClickableMenu.drawTextureBox(spriteBatch, Game1.menuTexture, new Rectangle(0, 256, 60, 60), this._boxRect.X, this._boxRect.Y, this._boxRect.Width, this._boxRect.Height, Color.White, this._cachedScale, false);
            }

            float startX = this._cachedPosition.X + this._cachedFramePaddingX;
            float centerY = this._cachedPosition.Y + this._cachedBoxH / 2f;

            // Draw Album Art
            if (track.CoverArt != null && this._config.ShowAlbumArt)
            {
                float artY = centerY - this._cachedArtSize / 2f;
                spriteBatch.Draw(track.CoverArt, new Rectangle((int)startX, (int)artY, (int)this._cachedArtSize, (int)this._cachedArtSize), Color.White);
                startX += this._cachedArtSize + this._cachedArtGap;
            }

            // Draw Text
            float textStartY = centerY - this._cachedTextBlockHeight / 2f;
            float textX = (track.CoverArt != null && this._config.ShowAlbumArt) ? startX : this._cachedPosition.X + this._cachedBoxW / 2f - this._cachedContentW / 2f;

            spriteBatch.DrawString(font, this._cachedDisplaySong, new Vector2(textX, textStartY), Game1.textColor, 0f, Vector2.Zero, this._cachedScale, SpriteEffects.None, 1f);
            spriteBatch.DrawString(font, this._cachedDisplayArtist, new Vector2(textX, textStartY + this._cachedLineHeight), Game1.textColor * 0.75f, 0f, Vector2.Zero, this._cachedScale, SpriteEffects.None, 1f);

            // Draw Playback Controls
            if (this._config.ShowPlaybackButtons)
            {
                if (this._buttonsTexture != null)
                {
                    spriteBatch.Draw(this._buttonsTexture, this._prevRect, SRC_PREV, Color.White);
                    spriteBatch.Draw(this._buttonsTexture, this._playPauseRect, track.IsPlaying ? SRC_PAUSE : SRC_PLAY, Color.White);
                    spriteBatch.Draw(this._buttonsTexture, this._nextRect, SRC_NEXT, Color.White);
                }
                else
                {
                    this.DrawFallbackButton(spriteBatch, "[<<]", this._prevRect, this._cachedScale);
                    this.DrawFallbackButton(spriteBatch, track.IsPlaying ? "[||]" : "[>]", this._playPauseRect, this._cachedScale);
                    this.DrawFallbackButton(spriteBatch, "[>>]", this._nextRect, this._cachedScale);
                }
            }
        }

        private void DrawFallbackButton(SpriteBatch b, string text, Rectangle rect, float scale)
        {
            SpriteFont font = Game1.smallFont;
            Vector2 size = font.MeasureString(text) * scale;
            Vector2 pos = new Vector2(rect.X + (rect.Width - size.X) / 2f, rect.Y + (rect.Height - size.Y) / 2f);
            b.DrawString(font, text, pos, Game1.textColor, 0f, Vector2.Zero, scale, SpriteEffects.None, 1f);
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
    }
}
