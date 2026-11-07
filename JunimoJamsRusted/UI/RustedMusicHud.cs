using System;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using JunimoJamsRusted.Services;
using StardewModdingAPI;
using StardewValley;

namespace JunimoJamsRusted.UI
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

        private readonly MarqueeController _titleMarquee = new() { MaxVisibleChars = 20 };
        private readonly MarqueeController _artistMarquee = new() { MaxVisibleChars = 26 };
        private readonly EqualizerEngine _equalizer = new();

        private Texture2D? _cardBgTexture;
        private Texture2D? _cardBorderTexture;
        private Texture2D? _artBgTexture;
        private Texture2D? _mediaIconsTexture;
        private Texture2D? _roundedArtCache;
        private string _roundedArtCacheKey = string.Empty;

        private bool _wasMouseDown;
        private TrackInfo _lastTrack = new();

        public const int CARD_WIDTH = 320;
        public const int CARD_HEIGHT = 76;
        public const int ART_SIZE = 60;
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

        public void UpdatePerTickAnimations()
        {
            if (this._lastTrack.Name == "Not Running")
                return;

            if (this._config.EnableMarqueeText)
            {
                this._titleMarquee.Update();
                this._artistMarquee.Update();
            }
        }

        public bool UpdateInput(float mouseX, float mouseY, bool leftMouseDown)
        {
            if (!this._config.ShowHud || !this._config.ShowPlaybackButtons)
                return false;

            int cardX = this._config.HudPositionX;
            int cardY = this._config.HudPositionY;

            Rectangle cardRect = new Rectangle(cardX, cardY, CARD_WIDTH, CARD_HEIGHT);
            bool isOverCard = cardRect.Contains((int)mouseX, (int)mouseY);

            int ctrlY = cardY + 54;
            int centerX = cardX + 78 + (234 / 2); // 195

            Rectangle prevHit = new Rectangle(centerX - 44 - 4, ctrlY - 4, 24, 24);
            Rectangle playHit = new Rectangle(centerX - 8 - 4, ctrlY - 4, 24, 24);
            Rectangle nextHit = new Rectangle(centerX + 28 - 4, ctrlY - 4, 24, 24);

            bool actionFired = false;
            if (leftMouseDown && !this._wasMouseDown)
            {
                if (prevHit.Contains((int)mouseX, (int)mouseY))
                {
                    Game1.playSound("smallSelect");
                    this._musicService.PreviousTrack();
                    this._onStateChangedAction();
                    actionFired = true;
                }
                else if (playHit.Contains((int)mouseX, (int)mouseY))
                {
                    Game1.playSound("smallSelect");
                    this._musicService.TogglePlayPause();
                    this._onStateChangedAction();
                    actionFired = true;
                }
                else if (nextHit.Contains((int)mouseX, (int)mouseY))
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

            int cardX = this._config.HudPositionX;
            int cardY = this._config.HudPositionY;

            HudThemeColors colors = GetThemeColors(this._config.Theme);
            GraphicsDevice device = Game1.graphics.GraphicsDevice;

            this.EnsureTexturesCreated(device);

            // 1. Drop Shadow (4px offset)
            spriteBatch.Draw(
                this._cardBgTexture!,
                new Rectangle(cardX + 4, cardY + 6, CARD_WIDTH, CARD_HEIGHT),
                Color.Black * 0.45f
            );

            // 2. Card Background & Border
            spriteBatch.Draw(
                this._cardBgTexture!,
                new Rectangle(cardX, cardY, CARD_WIDTH, CARD_HEIGHT),
                colors.Background
            );

            spriteBatch.Draw(
                this._cardBorderTexture!,
                new Rectangle(cardX, cardY, CARD_WIDTH, CARD_HEIGHT),
                colors.Border
            );

            if (colors.HasTopHighlight)
            {
                spriteBatch.Draw(
                    Game1.staminaRect,
                    new Rectangle(cardX + CORNER_RADIUS, cardY, CARD_WIDTH - (CORNER_RADIUS * 2), 2),
                    colors.BorderHighlight
                );
            }

            // 3. Album Art Slot (60x60, flush at cardX + 8, cardY + 8)
            int artX = cardX + 8;
            int artY = cardY + 8;
            Rectangle artRect = new Rectangle(artX, artY, ART_SIZE, ART_SIZE);

            spriteBatch.Draw(this._artBgTexture!, artRect, new Color(11, 23, 17));

            if (this._config.ShowAlbumArt && track.CoverArt != null && !track.CoverArt.IsDisposed)
            {
                this.DrawRoundedArt(spriteBatch, track.CoverArt, artRect, radius: 4);
            }
            else
            {
                // Draw elegant Sparkle fallback
                Vector2 center = new Vector2(artX + 22, artY + 18);
                spriteBatch.DrawString(Game1.smallFont, "*", center + new Vector2(1, 1), Color.Black * 0.5f, 0f, Vector2.Zero, 1.2f, SpriteEffects.None, 1f);
                spriteBatch.DrawString(Game1.smallFont, "*", center, colors.BorderHighlight, 0f, Vector2.Zero, 1.2f, SpriteEffects.None, 1f);
            }

            // 4. Content Column (cardX + 78 to cardX + 312, width = 234px)
            int colX = cardX + 78;
            int colW = 234;

            // Row 1: Title & Time
            string displayTitle = this._config.EnableMarqueeText ? this._titleMarquee.GetVisibleText() : StringUtils.NormalizeForHud(track.Name);
            if (string.IsNullOrWhiteSpace(displayTitle)) displayTitle = "Not Running";

            string timeStr = "0:00 / 0:00";
            if (track.Duration > TimeSpan.Zero)
            {
                TimeSpan currentPos = track.GetExtrapolatedPosition();
                timeStr = $"{FormatTime(currentPos)} / {FormatTime(track.Duration)}";
            }

            float timeWidth = Game1.tinyFont.MeasureString(timeStr).X;
            Vector2 timePos = new Vector2(colX + colW - timeWidth, cardY + 9);
            spriteBatch.DrawString(Game1.tinyFont, timeStr, timePos + new Vector2(1, 1), Color.Black * 0.7f);
            spriteBatch.DrawString(Game1.tinyFont, timeStr, timePos, colors.Time);

            // Title with shadow (clipped so it doesn't overlap time)
            int maxTitleWidth = (int)(colW - timeWidth - 8);
            Vector2 titlePos = new Vector2(colX, cardY + 8);
            spriteBatch.DrawString(Game1.smallFont, displayTitle, titlePos + new Vector2(1, 1), Color.Black * 0.85f, 0f, Vector2.Zero, 0.65f, SpriteEffects.None, 1f);
            spriteBatch.DrawString(Game1.smallFont, displayTitle, titlePos, colors.Title, 0f, Vector2.Zero, 0.65f, SpriteEffects.None, 1f);

            // Row 2: Equalizer & Artist
            int artistX = colX;
            if (track.IsPlaying)
            {
                this._equalizer.Update(isPlaying: true);
                float[] bars = this._equalizer.GetBars();
                int eqY = cardY + 36;
                for (int i = 0; i < 3; i++)
                {
                    int barH = Math.Max(2, (int)(bars[i] * 9f));
                    Rectangle barRect = new Rectangle(colX + (i * 3), eqY - barH, 2, barH);
                    spriteBatch.Draw(Game1.staminaRect, barRect, colors.ProgressFill);
                }
                artistX += 13;
            }

            string displayArtist = this._config.EnableMarqueeText ? this._artistMarquee.GetVisibleText() : StringUtils.NormalizeForHud(track.Artist);
            if (string.IsNullOrWhiteSpace(displayArtist)) displayArtist = "Music Player";

            Vector2 artistPos = new Vector2(artistX, cardY + 25);
            spriteBatch.DrawString(Game1.tinyFont, displayArtist, artistPos + new Vector2(1, 1), Color.Black * 0.7f, 0f, Vector2.Zero, 1.05f, SpriteEffects.None, 1f);
            spriteBatch.DrawString(Game1.tinyFont, displayArtist, artistPos, colors.Artist, 0f, Vector2.Zero, 1.05f, SpriteEffects.None, 1f);

            // Row 3: Slim Progress Bar (5px tall)
            int barY = cardY + 43;
            Rectangle barTrackRect = new Rectangle(colX, barY, colW, 5);
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
                    spriteBatch.Draw(Game1.staminaRect, new Rectangle(colX, barY, fillWidth, 5), colors.ProgressFill);
                }
            }

            // Row 4: Centered Lucide Media Controls
            if (this._config.ShowPlaybackButtons)
            {
                float mouseX = Game1.getMouseX(ui_scale: true);
                float mouseY = Game1.getMouseY(ui_scale: true);

                int ctrlY = cardY + 54;
                int centerX = colX + (colW / 2);

                Rectangle prevRect = new Rectangle(centerX - 44, ctrlY, 16, 16);
                Rectangle playRect = new Rectangle(centerX - 8, ctrlY, 16, 16);
                Rectangle nextRect = new Rectangle(centerX + 28, ctrlY, 16, 16);

                Rectangle prevHit = new Rectangle(prevRect.X - 4, prevRect.Y - 4, 24, 24);
                Rectangle playHit = new Rectangle(playRect.X - 4, playRect.Y - 4, 24, 24);
                Rectangle nextHit = new Rectangle(nextRect.X - 4, nextRect.Y - 4, 24, 24);

                Color prevCol = prevHit.Contains((int)mouseX, (int)mouseY) ? colors.IconHover : colors.IconNormal;
                Color playCol = playHit.Contains((int)mouseX, (int)mouseY) ? colors.IconHover : colors.IconNormal;
                Color nextCol = nextHit.Contains((int)mouseX, (int)mouseY) ? colors.IconHover : colors.IconNormal;

                // Prev (glyph 0: 0..15)
                spriteBatch.Draw(this._mediaIconsTexture!, prevRect, new Rectangle(0, 0, 16, 16), prevCol);

                // Play / Pause (glyph 1: 16..31 Play, glyph 2: 32..47 Pause)
                int playSrcX = track.IsPlaying ? 32 : 16;
                spriteBatch.Draw(this._mediaIconsTexture!, playRect, new Rectangle(playSrcX, 0, 16, 16), playCol);

                // Next (glyph 3: 48..63)
                spriteBatch.Draw(this._mediaIconsTexture!, nextRect, new Rectangle(48, 0, 16, 16), nextCol);
            }
        }

        private static void DrawHollowRect(SpriteBatch sb, Rectangle r, Color color)
        {
            sb.Draw(Game1.staminaRect, new Rectangle(r.X, r.Y, r.Width, 1), color);
            sb.Draw(Game1.staminaRect, new Rectangle(r.X, r.Bottom - 1, r.Width, 1), color);
            sb.Draw(Game1.staminaRect, new Rectangle(r.X, r.Y, 1, r.Height), color);
            sb.Draw(Game1.staminaRect, new Rectangle(r.Right - 1, r.Y, 1, r.Height), color);
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
