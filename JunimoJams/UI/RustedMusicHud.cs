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

        private readonly SmoothPixelMarquee _titleMarquee = new();
        private readonly SmoothPixelMarquee _artistMarquee = new();
        private readonly EqualizerEngine _equalizer = new();

        private static readonly string[] AsciiStrings = new string[128];
        static RustedMusicHud()
        {
            for (int i = 0; i < 128; i++)
            {
                AsciiStrings[i] = ((char)i).ToString();
            }
        }

        private Texture2D? _cardBgTexture;
        private Texture2D? _cardBorderTexture;
        private Texture2D? _artBgTexture;
        private Texture2D? _mediaIconsTexture;
        private Texture2D? _roundedArtCache;
        private string _roundedArtCacheKey = string.Empty;

        private SpriteFont? _ruDialogueFont;
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

            string normTitle = StringUtils.NormalizeForHud(track.Name);
            string normArtist = StringUtils.NormalizeForHud(track.Artist);

            this._titleMarquee.Text = normTitle;
            this._artistMarquee.Text = normArtist;
        }

        private void EnsureFontsLoaded()
        {
            if (this._ruDialogueFont != null && this._ruSmallFont != null)
                return;

            try
            {
                this._ruDialogueFont ??= Game1.content.Load<SpriteFont>("Fonts/SpriteFont1.ru-RU");
            }
            catch
            {
                this._ruDialogueFont = Game1.dialogueFont;
            }

            try
            {
                this._ruSmallFont ??= Game1.content.Load<SpriteFont>("Fonts/SmallFont.ru-RU");
            }
            catch
            {
                this._ruSmallFont = Game1.smallFont;
            }
        }

        private (SpriteFont font, float titleScale, float artistScale, float timeScale) GetFontAndScales(float s)
        {
            this.EnsureFontsLoaded();
            bool isClassic = this._config.Theme == "PelicanClassic";
            SpriteFont font = isClassic ? (this._ruSmallFont ?? Game1.smallFont) : (this._ruDialogueFont ?? Game1.dialogueFont);
            float titleScale = (isClassic ? 0.62f : 0.38f) * s;
            float artistScale = (isClassic ? 0.52f : 0.30f) * s;
            float timeScale = (isClassic ? 0.52f : 0.30f) * s;
            return (font, titleScale, artistScale, timeScale);
        }

        public void UpdatePerTickAnimations()
        {
            if (this._lastTrack.Name == "Not Running")
                return;

            if (this._config.EnableMarqueeText)
            {
                float s = Math.Clamp(this._config.HudScale, 0.5f, 2.5f);
                if (s <= 0.01f) s = 1.0f;

                var (font, titleScale, artistScale, timeScale) = this.GetFontAndScales(s);

                // Title measurements
                string title = string.IsNullOrWhiteSpace(this._lastTrack.Name) ? "Not Running" : this._lastTrack.Name;
                float titlePixelWidth = font.MeasureString(title).X * titleScale;
                float maxTitleWidth = 228f * s;
                this._titleMarquee.Update(titlePixelWidth, maxTitleWidth);

                // Time width in Row 2
                string timeStr = "0:00 / 0:00";
                if (this._lastTrack.Duration > TimeSpan.Zero)
                {
                    TimeSpan currentPos = this._lastTrack.GetExtrapolatedPosition();
                    timeStr = $"{FormatTime(currentPos)} / {FormatTime(this._lastTrack.Duration)}";
                }
                float timeWidth = font.MeasureString(timeStr).X * timeScale;

                // Artist measurements
                string artist = string.IsNullOrWhiteSpace(this._lastTrack.Artist) ? "Music Player" : this._lastTrack.Artist;
                float artistPixelWidth = font.MeasureString(artist).X * artistScale;
                float eqWidth = this._lastTrack.IsPlaying ? (13f * s) : 0f;
                float maxArtistWidth = Math.Max(30f * s, (228f * s) - timeWidth - (8f * s) - eqWidth);
                this._artistMarquee.Update(artistPixelWidth, maxArtistWidth);
            }
        }

        public bool UpdateInput(float mouseX, float mouseY, bool leftMouseDown)
        {
            if (!this._config.ShowHud || !this._config.ShowPlaybackButtons)
                return false;

            float s = Math.Clamp(this._config.HudScale, 0.5f, 2.5f);
            if (s <= 0.01f) s = 1.0f;

            int cardX = this._config.HudPositionX;
            int cardY = this._config.HudPositionY;
            int cardW = (int)(CARD_WIDTH * s);
            int cardH = (int)(CARD_HEIGHT * s);

            Rectangle cardRect = new Rectangle(cardX, cardY, cardW, cardH);
            bool isOverCard = cardRect.Contains((int)mouseX, (int)mouseY);

            float unscaledX = (mouseX - cardX) / s;
            float unscaledY = (mouseY - cardY) / s;

            int colX = 82;
            int colW = 228;
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
                    Game1.playSound("smallSelect");
                    this._musicService.PreviousTrack();
                    this._onStateChangedAction();
                    actionFired = true;
                }
                else if (playHit.Contains((int)unscaledX, (int)unscaledY))
                {
                    Game1.playSound("smallSelect");
                    this._musicService.TogglePlayPause();
                    this._onStateChangedAction();
                    actionFired = true;
                }
                else if (nextHit.Contains((int)unscaledX, (int)unscaledY))
                {
                    Game1.playSound("smallSelect");
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
            int cardW = (int)(CARD_WIDTH * s);
            int cardH = (int)(CARD_HEIGHT * s);

            HudThemeColors colors = GetThemeColors(this._config.Theme);
            GraphicsDevice device = Game1.graphics.GraphicsDevice;

            this.EnsureTexturesCreated(device);

            // 1. Drop Shadow (4px offset)
            spriteBatch.Draw(
                this._cardBgTexture!,
                new Rectangle((int)(cardX + 4 * s), (int)(cardY + 6 * s), cardW, cardH),
                Color.Black * 0.45f
            );

            // 2. Card Background
            spriteBatch.Draw(
                this._cardBgTexture!,
                new Rectangle(cardX, cardY, cardW, cardH),
                colors.Background
            );

            // Layout metrics
            int colX = (int)(cardX + 82 * s);
            int colW = (int)(228 * s);

            var (font, titleScale, artistScale, timeScale) = this.GetFontAndScales(s);

            // Row 1: Full-Width Title (Y = cardY + 8 * s)
            string displayTitle = StringUtils.NormalizeForHud(track.Name);
            if (string.IsNullOrWhiteSpace(displayTitle)) displayTitle = "Not Running";

            float titlePixelWidth = font.MeasureString(displayTitle).X * titleScale;
            float titleOffset = (this._config.EnableMarqueeText && titlePixelWidth > colW) ? this._titleMarquee.PixelOffset : 0f;

            Vector2 titlePos = new Vector2(colX - titleOffset, cardY + (8 * s));
            DrawBoundedString(spriteBatch, font, displayTitle, titlePos, titleScale, colors.Title, Color.Black * 0.85f, colX, colX + colW);

            // Row 2: Equalizer + Artist (Left), Time (Right) (Y = cardY + 28 * s)
            string timeStr = "0:00 / 0:00";
            if (track.Duration > TimeSpan.Zero)
            {
                TimeSpan currentPos = track.GetExtrapolatedPosition();
                timeStr = $"{FormatTime(currentPos)} / {FormatTime(track.Duration)}";
            }

            float timeWidth = font.MeasureString(timeStr).X * timeScale;
            Vector2 timePos = new Vector2(colX + colW - timeWidth, cardY + (28 * s));

            int artistX = colX + (track.IsPlaying ? (int)(13 * s) : 0);
            string displayArtist = StringUtils.NormalizeForHud(track.Artist);
            if (string.IsNullOrWhiteSpace(displayArtist)) displayArtist = "Music Player";

            float artistPixelWidth = font.MeasureString(displayArtist).X * artistScale;
            int maxArtistWidth = Math.Max(30, (int)(timePos.X - artistX - (8 * s)));
            float artistOffset = (this._config.EnableMarqueeText && artistPixelWidth > maxArtistWidth) ? this._artistMarquee.PixelOffset : 0f;

            Vector2 artistPos = new Vector2(artistX - artistOffset, cardY + (28 * s));
            DrawBoundedString(spriteBatch, font, displayArtist, artistPos, artistScale, colors.Artist, Color.Black * 0.7f, artistX, timePos.X - (8 * s));

            // Clean Painter's Masks (ensure text never crosses into padding or other elements)
            // Left margin mask (covers cardX to colX)
            Rectangle leftMask = new Rectangle(cardX + (int)(2 * s), cardY + (int)(4 * s), (int)(80 * s), (int)(44 * s));
            spriteBatch.Draw(Game1.staminaRect, leftMask, colors.Background);

            // Right margin mask (covers colX + colW to card edge)
            int rightPadX = colX + colW;
            Rectangle rightMask = new Rectangle(rightPadX, cardY + (int)(4 * s), (int)(cardW - (rightPadX - cardX)), (int)(44 * s));
            spriteBatch.Draw(Game1.staminaRect, rightMask, colors.Background);

            // Time background shield on Row 2
            int timeShieldX = (int)(timePos.X - 6 * s);
            Rectangle timeShield = new Rectangle(timeShieldX, cardY + (int)(24 * s), (int)(colX + colW - timeShieldX + 4), (int)(20 * s));
            spriteBatch.Draw(Game1.staminaRect, timeShield, colors.Background);

            // Draw Time text (Right side, always crisp & visible)
            spriteBatch.DrawString(font, timeStr, timePos + new Vector2(1, 1), Color.Black * 0.7f, 0f, Vector2.Zero, timeScale, SpriteEffects.None, 1f);
            spriteBatch.DrawString(font, timeStr, timePos, colors.Time, 0f, Vector2.Zero, timeScale, SpriteEffects.None, 1f);

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

            // 3. Album Art Slot (drawn after text/left mask to guarantee zero bleeding)
            int artX = (int)(cardX + 9 * s);
            int artY = (int)(cardY + 9 * s);
            int artSize = (int)(ART_SIZE * s);
            Rectangle artRect = new Rectangle(artX, artY, artSize, artSize);

            spriteBatch.Draw(this._artBgTexture!, artRect, new Color(11, 23, 17));

            if (this._config.ShowAlbumArt && track.CoverArt != null && !track.CoverArt.IsDisposed)
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

            // Row 3: Slim Progress Bar
            int barY = (int)(cardY + 47 * s);
            int barHeight = (int)Math.Max(2, 4 * s);
            Rectangle barTrackRect = new Rectangle(colX, barY, colW, barHeight);
            spriteBatch.Draw(Game1.staminaRect, barTrackRect, colors.ProgressTrack);

            // 1px bar border
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

            // Row 4: Centered Lucide Media Controls
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

                Rectangle prevHit = new Rectangle(82 + 114 - 44 - 4, 58 - 4, 24, 24);
                Rectangle playHit = new Rectangle(82 + 114 - 8 - 4, 58 - 4, 24, 24);
                Rectangle nextHit = new Rectangle(82 + 114 + 28 - 4, 58 - 4, 24, 24);

                Color prevCol = prevHit.Contains((int)unscaledX, (int)unscaledY) ? colors.IconHover : colors.IconNormal;
                Color playCol = playHit.Contains((int)unscaledX, (int)unscaledY) ? colors.IconHover : colors.IconNormal;
                Color nextCol = nextHit.Contains((int)unscaledX, (int)unscaledY) ? colors.IconHover : colors.IconNormal;

                // Prev (glyph 0: 0..15)
                spriteBatch.Draw(this._mediaIconsTexture!, prevRect, new Rectangle(0, 0, 16, 16), prevCol);

                // Play / Pause (glyph 1: 16..31 Play, glyph 2: 32..47 Pause)
                int playSrcX = track.IsPlaying ? 32 : 16;
                spriteBatch.Draw(this._mediaIconsTexture!, playRect, new Rectangle(playSrcX, 0, 16, 16), playCol);

                // Next (glyph 3: 48..63)
                spriteBatch.Draw(this._mediaIconsTexture!, nextRect, new Rectangle(48, 0, 16, 16), nextCol);
            }

            // 5. Card Border & Top Highlight (drawn on top of all content to frame perfectly)
            spriteBatch.Draw(
                this._cardBorderTexture!,
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

        private static void DrawBoundedString(SpriteBatch sb, SpriteFont font, string text, Vector2 pos, float scale, Color color, Color shadowColor, float minX, float maxX)
        {
            if (string.IsNullOrEmpty(text))
                return;

            float curX = pos.X;
            float y = pos.Y;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                string ch = (c < 128) ? AsciiStrings[c] : c.ToString();

                if (!font.Characters.Contains(c) && font.DefaultCharacter == null)
                {
                    ch = " ";
                }

                float w = font.MeasureString(ch).X * scale;

                // Character visible if its horizontal span overlaps [minX, maxX]
                if (curX + w > minX && curX < maxX)
                {
                    Vector2 p = new Vector2(curX, y);
                    sb.DrawString(font, ch, p + new Vector2(1, 1), shadowColor, 0f, Vector2.Zero, scale, SpriteEffects.None, 1f);
                    sb.DrawString(font, ch, p, color, 0f, Vector2.Zero, scale, SpriteEffects.None, 1f);
                }

                curX += w;
                if (curX >= maxX + 30f)
                    break;
            }
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
            if (this._cardBgTexture == null || this._cardBgTexture.IsDisposed)
            {
                this._cardBgTexture = CreateRoundedCardTexture(device, CARD_WIDTH, CARD_HEIGHT, CORNER_RADIUS);
            }

            if (this._cardBorderTexture == null || this._cardBorderTexture.IsDisposed)
            {
                this._cardBorderTexture = CreateRoundedBorderTexture(device, CARD_WIDTH, CARD_HEIGHT, CORNER_RADIUS, BORDER_WIDTH);
            }

            if (this._artBgTexture == null || this._artBgTexture.IsDisposed)
            {
                this._artBgTexture = CreateRoundedCardTexture(device, ART_SIZE, ART_SIZE, r: 4);
            }

            if (this._mediaIconsTexture == null || this._mediaIconsTexture.IsDisposed)
            {
                this._mediaIconsTexture = CreateMediaIconsAtlas(device);
            }
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
            this._cardBgTexture?.Dispose();
            this._cardBgTexture = null;

            this._cardBorderTexture?.Dispose();
            this._cardBorderTexture = null;

            this._artBgTexture?.Dispose();
            this._artBgTexture = null;

            this._mediaIconsTexture?.Dispose();
            this._mediaIconsTexture = null;

            this._roundedArtCache?.Dispose();
            this._roundedArtCache = null;
        }
    }
}
