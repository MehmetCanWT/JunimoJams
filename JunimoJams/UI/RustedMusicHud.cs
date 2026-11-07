using System;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using JunimoJams.Services;
using StardewModdingAPI;
using StardewValley;

namespace JunimoJams.UI
{
    public struct HudThemeColors
    {
        public Color Background;
        public Color Border;
        public Color BorderHighlight;
        public Color Title;
        public Color Artist;
        public Color Time;
        public Color ProgressFill;
        public Color ProgressTrack;
        public Color IconNormal;
        public Color IconHover;
        public bool HasTopHighlight;
    }

    public sealed class RustedMusicHud : IDisposable
    {
        private readonly ModConfig _config;
        private readonly IModHelper _helper;
        private readonly MusicService _musicService;
        private readonly Action _openMenuAction;
        private readonly Action _onStateChangedAction;

        private readonly SynchronizedHudMarquee _marquee = new();
        private readonly EqualizerEngine _equalizer = new();

        private static readonly string[] AsciiStrings = new string[128];
        static RustedMusicHud()
        {
            for (int i = 0; i < 128; i++)
            {
                AsciiStrings[i] = ((char)i).ToString();
            }
        }

        private Texture2D? _cardBg82;
        private Texture2D? _cardBorder82;
        private Texture2D? _cardBg56;
        private Texture2D? _cardBorder56;
        private Texture2D? _cardBg46;
        private Texture2D? _cardBorder46;
        private Texture2D? _boxBg76;
        private Texture2D? _boxBorder76;

        private Texture2D? _artBgTexture;
        private Texture2D? _mediaIconsTexture;
        private Texture2D? _roundedArtCache;
        private string _roundedArtCacheKey = string.Empty;

        private SpriteFont? _ruSmallFont;

        private bool _wasMouseDown;
        private TrackInfo _lastTrack = new();

        public const int CARD_WIDTH = 320;
        public const int CARD_HEIGHT = 82;
        public const int ART_SIZE = 64;
        public const int CORNER_RADIUS = 8;
        public const int BORDER_WIDTH = 2;

        public RustedMusicHud(
            ModConfig config,
            IModHelper helper,
            MusicService musicService,
            Action openMenuAction,
            Action onStateChangedAction)
        {
            this._config = config;
            this._helper = helper;
            this._musicService = musicService;
            this._openMenuAction = openMenuAction;
            this._onStateChangedAction = onStateChangedAction;
        }

        public void ApplyConfigTheme()
        {
            // Re-evaluates theme on config change
        }

        public void UpdateTrack(TrackInfo track)
        {
            this._lastTrack = track;
            this._marquee.Reset();
        }

        private void EnsureFontsLoaded()
        {
            if (this._ruSmallFont != null)
                return;

            try
            {
                this._ruSmallFont = Game1.content.Load<SpriteFont>("Fonts/SmallFont.ru-RU");
            }
            catch
            {
                this._ruSmallFont = Game1.smallFont;
            }
        }

        private (SpriteFont font, float titleScale, float artistScale, float timeScale) GetFontAndScales(float s)
        {
            this.EnsureFontsLoaded();
            SpriteFont font = this._ruSmallFont ?? Game1.smallFont;
            float titleScale = 0.75f * s;
            float artistScale = 0.65f * s;
            float timeScale = 0.65f * s;
            return (font, titleScale, artistScale, timeScale);
        }

        private static string SanitizeForFont(string text, SpriteFont font)
        {
            if (string.IsNullOrEmpty(text)) return text ?? "";
            var sb = new System.Text.StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (font.Characters.Contains(c) || font.DefaultCharacter != null)
                {
                    sb.Append(c);
                }
                else
                {
                    sb.Append(' ');
                }
            }
            return sb.ToString();
        }

        public void UpdatePerTickAnimations()
        {
            if (this._lastTrack.Name == "Not Running")
                return;

            if (this._config.EnableMarqueeText && !this._config.OnlyAlbumArt)
            {
                float s = Math.Clamp(this._config.HudScale, 0.5f, 2.5f);
                if (s <= 0.01f) s = 1.0f;

                var (font, titleScale, artistScale, timeScale) = this.GetFontAndScales(s);

                bool showArt = this._config.ShowAlbumArt;
                float maxTitleWidth = (showArt ? 228f : 288f) * s;

                // Title measurements
                string title = string.IsNullOrWhiteSpace(this._lastTrack.Name) ? "Not Running" : StringUtils.NormalizeForHud(this._lastTrack.Name);
                float titlePixelWidth = font.MeasureString(SanitizeForFont(title, font)).X * titleScale;

                // Time width in Row 2
                string timeStr = "0:00 / 0:00";
                if (this._lastTrack.Duration > TimeSpan.Zero)
                {
                    TimeSpan currentPos = this._lastTrack.GetExtrapolatedPosition();
                    timeStr = $"{FormatTime(currentPos)} / {FormatTime(this._lastTrack.Duration)}";
                }
                float timeWidth = font.MeasureString(SanitizeForFont(timeStr, font)).X * timeScale;

                // Artist measurements
                string artist = string.IsNullOrWhiteSpace(this._lastTrack.Artist) ? "Music Player" : StringUtils.NormalizeForHud(this._lastTrack.Artist);
                float artistPixelWidth = font.MeasureString(SanitizeForFont(artist, font)).X * artistScale;
                float eqWidth = this._lastTrack.IsPlaying ? (13f * s) : 0f;
                float maxArtistWidth = Math.Max(30f * s, maxTitleWidth - timeWidth - (8f * s) - eqWidth);

                this._marquee.Update(title, titlePixelWidth, maxTitleWidth, artist, artistPixelWidth, maxArtistWidth);
            }
        }

        public bool UpdateInput(float mouseX, float mouseY, bool leftMouseDown)
        {
            if (!this._config.ShowHud)
                return false;

            float s = Math.Clamp(this._config.HudScale, 0.5f, 2.5f);
            if (s <= 0.01f) s = 1.0f;

            int cardX = this._config.HudPositionX;
            int cardY = this._config.HudPositionY;

            if (this._config.OnlyAlbumArt)
            {
                int boxSize = (int)(76 * s);
                Rectangle hit = new Rectangle(cardX, cardY, boxSize, boxSize);
                bool isOver = hit.Contains((int)mouseX, (int)mouseY);
                if (leftMouseDown && !this._wasMouseDown && isOver)
                {
                    if (this._config.EnableUiSounds) Game1.playSound("smallSelect");
                    this._musicService.TogglePlayPause();
                    this._onStateChangedAction();
                    this._wasMouseDown = leftMouseDown;
                    return true;
                }
                this._wasMouseDown = leftMouseDown;
                return leftMouseDown && isOver;
            }

            if (!this._config.ShowPlaybackButtons)
                return false;

            bool showArt = this._config.ShowAlbumArt;
            int unscaledHeight = (!this._config.ShowPlaybackButtons && !this._config.ShowProgressBar) ? 46 :
                                 (!this._config.ShowPlaybackButtons) ? 56 : CARD_HEIGHT;
            int cardW = (int)(CARD_WIDTH * s);
            int cardH = (int)(unscaledHeight * s);

            Rectangle cardRect = new Rectangle(cardX, cardY, cardW, cardH);
            bool isOverCard = cardRect.Contains((int)mouseX, (int)mouseY);

            float unscaledX = (mouseX - cardX) / s;
            float unscaledY = (mouseY - cardY) / s;

            int colX = showArt ? 82 : 16;
            int colW = showArt ? 228 : 288;
            int ctrlY = 58;
            int centerX = colX + (colW / 2);

            Rectangle prevHit = new Rectangle(centerX - 44 - 4, ctrlY - 4, 24, 24);
            Rectangle playHit = new Rectangle(centerX - 8 - 4, ctrlY - 4, 24, 24);
            Rectangle nextHit = new Rectangle(centerX + 28 - 4, ctrlY - 4, 24, 24);

            bool actionFired = false;
            if (leftMouseDown && !this._wasMouseDown)
            {
                if (prevHit.Contains((int)unscaledX, (int)unscaledY))
                {
                    if (this._config.EnableUiSounds) Game1.playSound("smallSelect");
                    this._musicService.PreviousTrack();
                    this._onStateChangedAction();
                    actionFired = true;
                }
                else if (playHit.Contains((int)unscaledX, (int)unscaledY))
                {
                    if (this._config.EnableUiSounds) Game1.playSound("smallSelect");
                    this._musicService.TogglePlayPause();
                    this._onStateChangedAction();
                    actionFired = true;
                }
                else if (nextHit.Contains((int)unscaledX, (int)unscaledY))
                {
                    if (this._config.EnableUiSounds) Game1.playSound("smallSelect");
                    this._musicService.NextTrack();
                    this._onStateChangedAction();
                    actionFired = true;
                }
            }

            this._wasMouseDown = leftMouseDown;
            return actionFired || (leftMouseDown && isOverCard);
        }

        public void Draw(SpriteBatch spriteBatch, TrackInfo track)
        {
            if (!this._config.ShowHud)
                return;

            float s = Math.Clamp(this._config.HudScale, 0.5f, 2.5f);
            if (s <= 0.01f) s = 1.0f;

            int cardX = this._config.HudPositionX;
            int cardY = this._config.HudPositionY;

            HudThemeColors colors = GetThemeColors(this._config.Theme);
            GraphicsDevice device = Game1.graphics.GraphicsDevice;

            this.EnsureTexturesCreated(device);

            // ==========================================
            // MODE A: Only Album Art Widget
            // ==========================================
            if (this._config.OnlyAlbumArt)
            {
                int boxSize = (int)(76 * s);

                // 1. Drop shadow
                spriteBatch.Draw(
                    this._boxBg76!,
                    new Rectangle((int)(cardX + 3 * s), (int)(cardY + 5 * s), boxSize, boxSize),
                    Color.Black * 0.45f
                );

                // 2. Box background
                spriteBatch.Draw(
                    this._boxBg76!,
                    new Rectangle(cardX, cardY, boxSize, boxSize),
                    colors.Background
                );

                // 3. Album Art
                int artX = (int)(cardX + 6 * s);
                int artY = (int)(cardY + 6 * s);
                int artSize = (int)(64 * s);
                Rectangle artRect = new Rectangle(artX, artY, artSize, artSize);

                if (track.CoverArt != null && !track.CoverArt.IsDisposed)
                {
                    this.DrawRoundedArt(spriteBatch, track.CoverArt, artRect, radius: (int)Math.Max(2, 4 * s));
                }
                else
                {
                    spriteBatch.Draw(this._artBgTexture!, artRect, new Color(11, 23, 17));
                    Vector2 center = new Vector2(artX + (22 * s), artY + (18 * s));
                    float starScale = 1.2f * s;
                    spriteBatch.DrawString(Game1.smallFont, "*", center + new Vector2(1, 1), Color.Black * 0.5f, 0f, Vector2.Zero, starScale, SpriteEffects.None, 1f);
                    spriteBatch.DrawString(Game1.smallFont, "*", center, colors.BorderHighlight, 0f, Vector2.Zero, starScale, SpriteEffects.None, 1f);
                }

                // 4. Equalizer indicator overlay (bottom right)
                if (track.IsPlaying)
                {
                    this._equalizer.Update(isPlaying: true);
                    float[] bars = this._equalizer.GetBars();
                    int eqBaseX = (int)(artX + artSize - (15 * s));
                    int eqBaseY = (int)(artY + artSize - (4 * s));
                    int barW = (int)Math.Max(1, 2 * s);
                    int barGap = (int)Math.Max(2, 3 * s);

                    Rectangle eqBg = new Rectangle(eqBaseX - (int)(2 * s), eqBaseY - (int)(12 * s), (int)(13 * s), (int)(14 * s));
                    spriteBatch.Draw(Game1.staminaRect, eqBg, colors.Background * 0.85f);

                    for (int i = 0; i < 3; i++)
                    {
                        int barH = Math.Max(2, (int)(bars[i] * 9f * s));
                        Rectangle barRect = new Rectangle(eqBaseX + (i * barGap), eqBaseY - barH, barW, barH);
                        spriteBatch.Draw(Game1.staminaRect, barRect, colors.ProgressFill);
                    }
                }

                // 5. Border
                spriteBatch.Draw(
                    this._boxBorder76!,
                    new Rectangle(cardX, cardY, boxSize, boxSize),
                    colors.Border
                );

                if (colors.HasTopHighlight)
                {
                    spriteBatch.Draw(
                        Game1.staminaRect,
                        new Rectangle((int)(cardX + CORNER_RADIUS * s), cardY, (int)((76 - (CORNER_RADIUS * 2)) * s), (int)Math.Max(1, 2 * s)),
                        colors.BorderHighlight
                    );
                }

                return;
            }

            // ==========================================
            // MODE B: Full HUD
            // ==========================================
            bool showArt = this._config.ShowAlbumArt;
            int unscaledHeight = CARD_HEIGHT; // 82
            if (!this._config.ShowPlaybackButtons && !this._config.ShowProgressBar)
            {
                unscaledHeight = 46;
            }
            else if (!this._config.ShowPlaybackButtons)
            {
                unscaledHeight = 56;
            }

            int cardW = (int)(CARD_WIDTH * s);
            int cardH = (int)(unscaledHeight * s);

            Texture2D bgTex = (unscaledHeight == 46) ? this._cardBg46! : (unscaledHeight == 56) ? this._cardBg56! : this._cardBg82!;
            Texture2D borderTex = (unscaledHeight == 46) ? this._cardBorder46! : (unscaledHeight == 56) ? this._cardBorder56! : this._cardBorder82!;

            // 1. Drop Shadow (4px offset)
            spriteBatch.Draw(
                bgTex,
                new Rectangle((int)(cardX + 4 * s), (int)(cardY + 6 * s), cardW, cardH),
                Color.Black * 0.45f
            );

            // 2. Card Background
            spriteBatch.Draw(
                bgTex,
                new Rectangle(cardX, cardY, cardW, cardH),
                colors.Background
            );

            // Layout metrics
            int colX = (int)(cardX + (showArt ? 82 : 16) * s);
            int colW = (int)((showArt ? 228 : 288) * s);

            var (font, titleScale, artistScale, timeScale) = this.GetFontAndScales(s);

            // Row 1: Full-Width Title (Y = cardY + 8 * s)
            string displayTitle = StringUtils.NormalizeForHud(track.Name);
            if (string.IsNullOrWhiteSpace(displayTitle)) displayTitle = "Not Running";

            float titlePixelWidth = font.MeasureString(SanitizeForFont(displayTitle, font)).X * titleScale;
            float titleOffset = (this._config.EnableMarqueeText && titlePixelWidth > colW) ? this._marquee.TitleOffset : 0f;

            Vector2 titlePos = new Vector2(colX - titleOffset, cardY + (8 * s));
            DrawBoundedString(spriteBatch, font, displayTitle, titlePos, titleScale, colors.Title, Color.Black * 0.85f);

            // Row 2: Equalizer + Artist (Left), Time (Right) (Y = cardY + 28 * s)
            string timeStr = "0:00 / 0:00";
            if (track.Duration > TimeSpan.Zero)
            {
                TimeSpan currentPos = track.GetExtrapolatedPosition();
                timeStr = $"{FormatTime(currentPos)} / {FormatTime(track.Duration)}";
            }

            float timeWidth = font.MeasureString(SanitizeForFont(timeStr, font)).X * timeScale;
            Vector2 timePos = new Vector2(colX + colW - timeWidth, cardY + (28 * s));

            int artistX = colX + (track.IsPlaying ? (int)(13 * s) : 0);
            string displayArtist = StringUtils.NormalizeForHud(track.Artist);
            if (string.IsNullOrWhiteSpace(displayArtist)) displayArtist = "Music Player";

            float artistPixelWidth = font.MeasureString(SanitizeForFont(displayArtist, font)).X * artistScale;
            int maxArtistWidth = Math.Max(30, (int)(timePos.X - artistX - (8 * s)));
            float artistOffset = (this._config.EnableMarqueeText && artistPixelWidth > maxArtistWidth) ? this._marquee.ArtistOffset : 0f;

            Vector2 artistPos = new Vector2(artistX - artistOffset, cardY + (28 * s));
            DrawBoundedString(spriteBatch, font, displayArtist, artistPos, artistScale, colors.Artist, Color.Black * 0.7f);

            // Clean Painter's Masks
            // Left margin mask
            int leftMaskW = (int)((showArt ? 80 : 14) * s);
            Rectangle leftMask = new Rectangle(cardX + (int)(2 * s), cardY + (int)(4 * s), leftMaskW, (int)(44 * s));
            spriteBatch.Draw(Game1.staminaRect, leftMask, colors.Background);

            // Right margin mask
            int rightPadX = colX + colW;
            Rectangle rightMask = new Rectangle(rightPadX, cardY + (int)(4 * s), (int)(cardW - (rightPadX - cardX)), (int)(44 * s));
            spriteBatch.Draw(Game1.staminaRect, rightMask, colors.Background);

            // Time background shield on Row 2
            int timeShieldX = (int)(timePos.X - 6 * s);
            Rectangle timeShield = new Rectangle(timeShieldX, cardY + (int)(24 * s), (int)(colX + colW - timeShieldX + 4), (int)(20 * s));
            spriteBatch.Draw(Game1.staminaRect, timeShield, colors.Background);

            // Draw Time text
            Vector2 snappedTimePos = new Vector2((float)Math.Round(timePos.X), (float)Math.Round(timePos.Y));
            spriteBatch.DrawString(font, timeStr, snappedTimePos + new Vector2(1, 1), Color.Black * 0.7f, 0f, Vector2.Zero, timeScale, SpriteEffects.None, 1f);
            spriteBatch.DrawString(font, timeStr, snappedTimePos, colors.Time, 0f, Vector2.Zero, timeScale, SpriteEffects.None, 1f);

            // Equalizer (Left side of Row 2)
            if (track.IsPlaying)
            {
                Rectangle eqShield = new Rectangle(colX, cardY + (int)(25 * s), (int)(13 * s), (int)(18 * s));
                spriteBatch.Draw(Game1.staminaRect, eqShield, colors.Background);

                this._equalizer.Update(isPlaying: true);
                float[] bars = this._equalizer.GetBars();
                int eqY = (int)(cardY + 38 * s);
                int barW = (int)Math.Max(1, 2 * s);
                int barGap = (int)Math.Max(2, 3 * s);
                for (int i = 0; i < 3; i++)
                {
                    int barH = Math.Max(2, (int)(bars[i] * 8f * s));
                    Rectangle barRect = new Rectangle(colX + (i * barGap), eqY - barH, barW, barH);
                    spriteBatch.Draw(Game1.staminaRect, barRect, colors.ProgressFill);
                }
            }

            // 3. Album Art Slot (ONLY IF showArt IS TRUE!)
            if (showArt)
            {
                int artX = (int)(cardX + 9 * s);
                int artY = (int)(cardY + 9 * s);
                int artSize = (int)(ART_SIZE * s);
                Rectangle artRect = new Rectangle(artX, artY, artSize, artSize);

                spriteBatch.Draw(this._artBgTexture!, artRect, new Color(11, 23, 17));

                if (track.CoverArt != null && !track.CoverArt.IsDisposed)
                {
                    this.DrawRoundedArt(spriteBatch, track.CoverArt, artRect, radius: (int)Math.Max(2, 4 * s));
                }
                else
                {
                    Vector2 center = new Vector2(artX + (22 * s), artY + (18 * s));
                    float starScale = 1.2f * s;
                    spriteBatch.DrawString(Game1.smallFont, "*", center + new Vector2(1, 1), Color.Black * 0.5f, 0f, Vector2.Zero, starScale, SpriteEffects.None, 1f);
                    spriteBatch.DrawString(Game1.smallFont, "*", center, colors.BorderHighlight, 0f, Vector2.Zero, starScale, SpriteEffects.None, 1f);
                }
            }

            // Row 3: Slim Progress Bar (ONLY IF ShowProgressBar IS TRUE!)
            if (this._config.ShowProgressBar)
            {
                int barY = (int)(cardY + 47 * s);
                int barHeight = (int)Math.Max(2, 4 * s);
                Rectangle barTrackRect = new Rectangle(colX, barY, colW, barHeight);
                spriteBatch.Draw(Game1.staminaRect, barTrackRect, colors.ProgressTrack);
                DrawHollowRect(spriteBatch, barTrackRect, Color.White * 0.12f);

                if (track.Duration > TimeSpan.Zero)
                {
                    TimeSpan curPos = track.GetExtrapolatedPosition();
                    float ratio = Math.Clamp((float)(curPos.TotalSeconds / track.Duration.TotalSeconds), 0f, 1f);
                    int fillWidth = (int)(colW * ratio);
                    if (fillWidth > 0)
                    {
                        spriteBatch.Draw(Game1.staminaRect, new Rectangle(colX, barY, fillWidth, barHeight), colors.ProgressFill);
                    }
                }
            }

            // Row 4: Centered Lucide Media Controls (ONLY IF ShowPlaybackButtons IS TRUE!)
            if (this._config.ShowPlaybackButtons)
            {
                float mouseX = Game1.getMouseX(ui_scale: true);
                float mouseY = Game1.getMouseY(ui_scale: true);

                float unscaledX = (mouseX - cardX) / s;
                float unscaledY = (mouseY - cardY) / s;

                int ctrlY = (int)(cardY + 58 * s);
                int centerX = colX + (colW / 2);
                int iconSize = (int)(16 * s);

                Rectangle prevRect = new Rectangle(centerX - (int)(44 * s), ctrlY, iconSize, iconSize);
                Rectangle playRect = new Rectangle(centerX - (int)(8 * s), ctrlY, iconSize, iconSize);
                Rectangle nextRect = new Rectangle(centerX + (int)(28 * s), ctrlY, iconSize, iconSize);

                int unscaledColX = showArt ? 82 : 16;
                int unscaledColW = showArt ? 228 : 288;
                int unscaledCenterX = unscaledColX + (unscaledColW / 2);

                Rectangle prevHit = new Rectangle(unscaledCenterX - 44 - 4, 58 - 4, 24, 24);
                Rectangle playHit = new Rectangle(unscaledCenterX - 8 - 4, 58 - 4, 24, 24);
                Rectangle nextHit = new Rectangle(unscaledCenterX + 28 - 4, 58 - 4, 24, 24);

                Color prevCol = prevHit.Contains((int)unscaledX, (int)unscaledY) ? colors.IconHover : colors.IconNormal;
                Color playCol = playHit.Contains((int)unscaledX, (int)unscaledY) ? colors.IconHover : colors.IconNormal;
                Color nextCol = nextHit.Contains((int)unscaledX, (int)unscaledY) ? colors.IconHover : colors.IconNormal;

                spriteBatch.Draw(this._mediaIconsTexture!, prevRect, new Rectangle(0, 0, 16, 16), prevCol);
                int playIconSrcX = track.IsPlaying ? 32 : 16;
                spriteBatch.Draw(this._mediaIconsTexture!, playRect, new Rectangle(playIconSrcX, 0, 16, 16), playCol);
                spriteBatch.Draw(this._mediaIconsTexture!, nextRect, new Rectangle(48, 0, 16, 16), nextCol);
            }

            // 5. Card Border & Top Highlight
            spriteBatch.Draw(
                borderTex,
                new Rectangle(cardX, cardY, cardW, cardH),
                colors.Border
            );

            if (colors.HasTopHighlight)
            {
                spriteBatch.Draw(
                    Game1.staminaRect,
                    new Rectangle((int)(cardX + CORNER_RADIUS * s), cardY, (int)((CARD_WIDTH - (CORNER_RADIUS * 2)) * s), (int)Math.Max(1, 2 * s)),
                    colors.BorderHighlight
                );
            }
        }

        private static void DrawHollowRect(SpriteBatch sb, Rectangle r, Color color)
        {
            sb.Draw(Game1.staminaRect, new Rectangle(r.X, r.Y, r.Width, 1), color);
            sb.Draw(Game1.staminaRect, new Rectangle(r.X, r.Bottom - 1, r.Width, 1), color);
            sb.Draw(Game1.staminaRect, new Rectangle(r.X, r.Y, 1, r.Height), color);
            sb.Draw(Game1.staminaRect, new Rectangle(r.Right - 1, r.Y, 1, r.Height), color);
        }

        private static void DrawBoundedString(SpriteBatch sb, SpriteFont font, string text, Vector2 pos, float scale, Color color, Color shadowColor)
        {
            if (string.IsNullOrEmpty(text))
                return;

            string safeText = SanitizeForFont(text, font);
            Vector2 snappedPos = new Vector2((float)Math.Round(pos.X), (float)Math.Round(pos.Y));
            Vector2 shadowOffset = new Vector2(1, 1);

            sb.DrawString(font, safeText, snappedPos + shadowOffset, shadowColor, 0f, Vector2.Zero, scale, SpriteEffects.None, 1f);
            sb.DrawString(font, safeText, snappedPos, color, 0f, Vector2.Zero, scale, SpriteEffects.None, 1f);
        }

        public static HudThemeColors GetThemeColors(string themeName)
        {
            return themeName switch
            {
                "PelicanClassic" => new HudThemeColors
                {
                    Background = new Color(254, 231, 182),
                    Border = new Color(139, 69, 19),
                    BorderHighlight = new Color(205, 133, 63),
                    Title = new Color(74, 40, 16),
                    Artist = new Color(60, 30, 10),
                    Time = new Color(139, 69, 19),
                    ProgressFill = new Color(230, 138, 0),
                    ProgressTrack = new Color(0, 0, 0, 45),
                    IconNormal = new Color(74, 40, 16, 220),
                    IconHover = new Color(139, 69, 19),
                    HasTopHighlight = false
                },
                "ShadcnZincDark" => new HudThemeColors
                {
                    Background = new Color(9, 9, 11),
                    Border = new Color(39, 39, 42),
                    BorderHighlight = new Color(113, 113, 122),
                    Title = new Color(250, 250, 250),
                    Artist = new Color(212, 212, 216),
                    Time = new Color(161, 161, 170),
                    ProgressFill = new Color(250, 250, 250),
                    ProgressTrack = new Color(255, 255, 255, 30),
                    IconNormal = new Color(212, 212, 216, 200),
                    IconHover = Color.White,
                    HasTopHighlight = false
                },
                "TokyoNight" => new HudThemeColors
                {
                    Background = new Color(26, 27, 38),
                    Border = new Color(122, 162, 247),
                    BorderHighlight = new Color(187, 154, 247),
                    Title = new Color(122, 162, 247),
                    Artist = new Color(192, 202, 245),
                    Time = new Color(125, 207, 255),
                    ProgressFill = new Color(122, 162, 247),
                    ProgressTrack = new Color(0, 0, 0, 100),
                    IconNormal = new Color(192, 202, 245, 200),
                    IconHover = Color.White,
                    HasTopHighlight = false
                },
                "CatppuccinMocha" => new HudThemeColors
                {
                    Background = new Color(30, 30, 46),
                    Border = new Color(203, 166, 247),
                    BorderHighlight = new Color(245, 194, 231),
                    Title = new Color(203, 166, 247),
                    Artist = new Color(205, 214, 244),
                    Time = new Color(137, 220, 235),
                    ProgressFill = new Color(203, 166, 247),
                    ProgressTrack = new Color(0, 0, 0, 100),
                    IconNormal = new Color(205, 214, 244, 200),
                    IconHover = Color.White,
                    HasTopHighlight = false
                },
                "Dracula" => new HudThemeColors
                {
                    Background = new Color(40, 42, 54),
                    Border = new Color(189, 147, 249),
                    BorderHighlight = new Color(255, 121, 198),
                    Title = new Color(255, 121, 198),
                    Artist = new Color(248, 248, 242),
                    Time = new Color(80, 250, 123),
                    ProgressFill = new Color(189, 147, 249),
                    ProgressTrack = new Color(0, 0, 0, 100),
                    IconNormal = new Color(248, 248, 242, 200),
                    IconHover = Color.White,
                    HasTopHighlight = false
                },
                "Nord" => new HudThemeColors
                {
                    Background = new Color(46, 52, 64),
                    Border = new Color(136, 192, 208),
                    BorderHighlight = new Color(129, 161, 193),
                    Title = new Color(136, 192, 208),
                    Artist = new Color(236, 239, 244),
                    Time = new Color(235, 203, 139),
                    ProgressFill = new Color(136, 192, 208),
                    ProgressTrack = new Color(0, 0, 0, 100),
                    IconNormal = new Color(236, 239, 244, 200),
                    IconHover = Color.White,
                    HasTopHighlight = false
                },
                "Cyberpunk" => new HudThemeColors
                {
                    Background = new Color(10, 6, 22),
                    Border = new Color(0, 240, 255),
                    BorderHighlight = new Color(252, 238, 10),
                    Title = new Color(252, 238, 10),
                    Artist = new Color(228, 228, 231),
                    Time = new Color(0, 240, 255),
                    ProgressFill = new Color(0, 240, 255),
                    ProgressTrack = new Color(0, 0, 0, 120),
                    IconNormal = new Color(0, 240, 255, 200),
                    IconHover = Color.White,
                    HasTopHighlight = true
                },
                "WizardArcane" => new HudThemeColors
                {
                    Background = new Color(15, 8, 29),
                    Border = new Color(168, 85, 247),
                    BorderHighlight = new Color(245, 158, 11),
                    Title = new Color(245, 158, 11),
                    Artist = new Color(233, 213, 255),
                    Time = new Color(168, 85, 247),
                    ProgressFill = new Color(168, 85, 247),
                    ProgressTrack = new Color(0, 0, 0, 100),
                    IconNormal = new Color(233, 213, 255, 200),
                    IconHover = Color.White,
                    HasTopHighlight = false
                },
                "JojaCorp" => new HudThemeColors
                {
                    Background = new Color(15, 23, 42),
                    Border = new Color(56, 189, 248),
                    BorderHighlight = new Color(2, 132, 199),
                    Title = new Color(56, 189, 248),
                    Artist = new Color(241, 245, 249),
                    Time = new Color(148, 163, 184),
                    ProgressFill = new Color(56, 189, 248),
                    ProgressTrack = new Color(0, 0, 0, 100),
                    IconNormal = new Color(241, 245, 249, 200),
                    IconHover = Color.White,
                    HasTopHighlight = false
                },
                _ => new HudThemeColors // JunimoForest
                {
                    Background = new Color(20, 38, 28),
                    Border = new Color(74, 124, 89),
                    BorderHighlight = new Color(134, 239, 172),
                    Title = new Color(253, 224, 71), // Star Gold
                    Artist = new Color(209, 213, 219),
                    Time = new Color(134, 239, 172), // Mint Green
                    ProgressFill = new Color(134, 239, 172),
                    ProgressTrack = new Color(0, 0, 0, 110),
                    IconNormal = new Color(228, 228, 231, 210),
                    IconHover = Color.White,
                    HasTopHighlight = true
                }
            };
        }

        private static string FormatTime(TimeSpan span)
        {
            if (span.TotalHours >= 1.0)
                return $"{(int)span.TotalHours}:{span.Minutes:D2}:{span.Seconds:D2}";
            return $"{span.Minutes}:{span.Seconds:D2}";
        }

        private void EnsureTexturesCreated(GraphicsDevice device)
        {
            this._cardBg82 ??= CreateRoundedCardTexture(device, CARD_WIDTH, 82, CORNER_RADIUS);
            this._cardBorder82 ??= CreateRoundedBorderTexture(device, CARD_WIDTH, 82, CORNER_RADIUS, BORDER_WIDTH);

            this._cardBg56 ??= CreateRoundedCardTexture(device, CARD_WIDTH, 56, CORNER_RADIUS);
            this._cardBorder56 ??= CreateRoundedBorderTexture(device, CARD_WIDTH, 56, CORNER_RADIUS, BORDER_WIDTH);

            this._cardBg46 ??= CreateRoundedCardTexture(device, CARD_WIDTH, 46, CORNER_RADIUS);
            this._cardBorder46 ??= CreateRoundedBorderTexture(device, CARD_WIDTH, 46, CORNER_RADIUS, BORDER_WIDTH);

            this._boxBg76 ??= CreateRoundedCardTexture(device, 76, 76, CORNER_RADIUS);
            this._boxBorder76 ??= CreateRoundedBorderTexture(device, 76, 76, CORNER_RADIUS, BORDER_WIDTH);

            this._artBgTexture ??= CreateRoundedCardTexture(device, ART_SIZE, ART_SIZE, r: 4);
            this._mediaIconsTexture ??= CreateMediaIconsAtlas(device);
        }

        private static Texture2D CreateRoundedCardTexture(GraphicsDevice device, int w, int h, int r)
        {
            Color[] pixels = new Color[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int cx = (x < w / 2) ? r : w - 1 - r;
                    int cy = (y < h / 2) ? r : h - 1 - r;
                    float dx = x - cx;
                    float dy = y - cy;
                    bool inCornerZone = (x < r || x >= w - r) && (y < r || y >= h - r);
                    if (inCornerZone && (dx * dx + dy * dy) > (float)(r * r))
                    {
                        pixels[y * w + x] = Color.Transparent;
                    }
                    else
                    {
                        pixels[y * w + x] = Color.White;
                    }
                }
            }
            Texture2D tex = new Texture2D(device, w, h);
            tex.SetData(pixels);
            return tex;
        }

        private static Texture2D CreateRoundedBorderTexture(GraphicsDevice device, int w, int h, int r, int bw)
        {
            Color[] pixels = new Color[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int cx = (x < w / 2) ? r : w - 1 - r;
                    int cy = (y < h / 2) ? r : h - 1 - r;
                    float dx = x - cx;
                    float dy = y - cy;
                    bool inCornerZone = (x < r || x >= w - r) && (y < r || y >= h - r);
                    bool insideOuter = !inCornerZone || (dx * dx + dy * dy) <= (float)(r * r);

                    if (!insideOuter)
                    {
                        pixels[y * w + x] = Color.Transparent;
                        continue;
                    }

                    bool inOuterBorder = x < bw || x >= w - bw || y < bw || y >= h - bw;
                    if (inOuterBorder)
                    {
                        pixels[y * w + x] = Color.White;
                    }
                    else
                    {
                        int ir = Math.Max(1, r - bw);
                        int icx = (x < w / 2) ? r : w - 1 - r;
                        int icy = (y < h / 2) ? r : h - 1 - r;
                        float idx = x - icx;
                        float idy = y - icy;
                        bool inInnerCorner = (x < r || x >= w - r) && (y < r || y >= h - r);
                        if (inInnerCorner && (idx * idx + idy * idy) > (float)(ir * ir))
                        {
                            pixels[y * w + x] = Color.White;
                        }
                        else
                        {
                            pixels[y * w + x] = Color.Transparent;
                        }
                    }
                }
            }
            Texture2D tex = new Texture2D(device, w, h);
            tex.SetData(pixels);
            return tex;
        }

        private static Texture2D CreateMediaIconsAtlas(GraphicsDevice device)
        {
            const int width = 64;
            const int height = 16;
            Color[] pixels = new Color[width * height];

            string[][] glyphs = new[]
            {
                // 0: Prev (vertical bar + left triangle)
                new[] {
                    "................",
                    "................",
                    ".##...######....",
                    ".##...#####.....",
                    ".##...####......",
                    ".##...###.......",
                    ".##...##........",
                    ".##...###.......",
                    ".##...####......",
                    ".##...#####.....",
                    ".##...######....",
                    "................",
                    "................",
                    "................",
                    "................",
                    "................"
                },
                // 1: Play (right triangle)
                new[] {
                    "................",
                    "................",
                    "..##............",
                    "..####..........",
                    "..######........",
                    "..########......",
                    "..##########....",
                    "..########......",
                    "..######........",
                    "..####..........",
                    "..##............",
                    "................",
                    "................",
                    "................",
                    "................",
                    "................"
                },
                // 2: Pause (two bars)
                new[] {
                    "................",
                    "................",
                    "..####....####..",
                    "..####....####..",
                    "..####....####..",
                    "..####....####..",
                    "..####....####..",
                    "..####....####..",
                    "..####....####..",
                    "..####....####..",
                    "..####....####..",
                    "................",
                    "................",
                    "................",
                    "................",
                    "................"
                },
                // 3: Next (right triangle + vertical bar)
                new[] {
                    "................",
                    "................",
                    "....######...##.",
                    ".....#####...##.",
                    "......####...##.",
                    ".......###...##.",
                    "........##...##.",
                    ".......###...##.",
                    "......####...##.",
                    ".....#####...##.",
                    "....######...##.",
                    "................",
                    "................",
                    "................",
                    "................",
                    "................"
                }
            };

            for (int iconIdx = 0; iconIdx < glyphs.Length; iconIdx++)
            {
                string[] rows = glyphs[iconIdx];
                int baseX = iconIdx * 16;
                for (int y = 0; y < 16 && y < rows.Length; y++)
                {
                    string row = rows[y];
                    for (int x = 0; x < 16 && x < row.Length; x++)
                    {
                        if (row[x] == '#')
                        {
                            pixels[y * width + baseX + x] = Color.White;
                        }
                    }
                }
            }

            Texture2D tex = new Texture2D(device, width, height);
            tex.SetData(pixels);
            return tex;
        }

        private void DrawRoundedArt(SpriteBatch spriteBatch, Texture2D source, Rectangle dest, int radius)
        {
            string cacheKey = $"{source.GetHashCode()}_{dest.Width}_{dest.Height}_{radius}";
            if (this._roundedArtCache == null || this._roundedArtCacheKey != cacheKey)
            {
                this._roundedArtCache?.Dispose();
                this._roundedArtCache = this.BuildRoundedTexture(source, dest.Width, dest.Height, radius);
                this._roundedArtCacheKey = cacheKey;
            }

            if (this._roundedArtCache != null && !this._roundedArtCache.IsDisposed)
            {
                spriteBatch.Draw(this._roundedArtCache, dest, Color.White);
            }
        }

        private Texture2D BuildRoundedTexture(Texture2D source, int w, int h, int radius)
        {
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

            int r = Math.Min(radius, Math.Min(w, h) / 2);
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
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

        public void Dispose()
        {
            this._cardBg82?.Dispose();
            this._cardBg82 = null;
            this._cardBorder82?.Dispose();
            this._cardBorder82 = null;

            this._cardBg56?.Dispose();
            this._cardBg56 = null;
            this._cardBorder56?.Dispose();
            this._cardBorder56 = null;

            this._cardBg46?.Dispose();
            this._cardBg46 = null;
            this._cardBorder46?.Dispose();
            this._cardBorder46 = null;

            this._boxBg76?.Dispose();
            this._boxBg76 = null;
            this._boxBorder76?.Dispose();
            this._boxBorder76 = null;

            this._artBgTexture?.Dispose();
            this._artBgTexture = null;

            this._mediaIconsTexture?.Dispose();
            this._mediaIconsTexture = null;

            this._roundedArtCache?.Dispose();
            this._roundedArtCache = null;
        }
    }
}
