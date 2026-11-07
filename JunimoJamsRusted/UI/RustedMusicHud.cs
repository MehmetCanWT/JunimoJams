using System;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using JunimoJamsRusted.Services;
using Rusted.Smapi.Native;
using Rusted.Smapi.UI;
using StardewModdingAPI;
using StardewValley;

namespace JunimoJamsRusted.UI
{
    public sealed class RustedMusicHud : IDisposable
    {
        private readonly ModConfig _config;
        private readonly IModHelper _helper;
        private readonly MusicService _musicService;
        private readonly Action _openMenuAction;
        private readonly Action _onStateChangedAction;

        private RustedView? _view;
        private readonly LruTextureCache _textureCache = new(capacity: 30);
        private readonly MarqueeController _titleMarquee = new() { MaxVisibleChars = 22 };
        private readonly MarqueeController _artistMarquee = new() { MaxVisibleChars = 28 };
        private readonly EqualizerEngine _equalizer = new();

        private Texture2D? _roundedArtCache;
        private string _roundedArtCacheKey = string.Empty;
        private Texture2D? _mediaIconsTexture;
        private bool _wasMouseDown;
        private TrackInfo _lastTrack = new();

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

            this.InitializeView();
        }

        public void InitializeView()
        {
            this._view?.Dispose();

            bool followGlobal = string.Equals(this._config.Theme, "FollowGlobal", StringComparison.OrdinalIgnoreCase);
            this._view = new RustedView(ownerModId: "MehmetCanWT.JunimoJamsRusted", useRustedUiTheme: followGlobal);

            string htmlPath = Path.Combine(this._helper.DirectoryPath, "assets", "ui", "music_hud.html");
            if (File.Exists(htmlPath))
            {
                this._view.LoadMarkupFileWithHotReload(htmlPath);
            }
            else
            {
                this.BuildFallbackFluentView();
            }

            this.ApplyConfigTheme();

            // Bind Actions
            this._view.OnAction("playpause", () =>
            {
                this._musicService.TogglePlayPause();
                this._onStateChangedAction();
            });

            this._view.OnAction("next", () =>
            {
                this._musicService.NextTrack();
                this._onStateChangedAction();
            });

            this._view.OnAction("prev", () =>
            {
                this._musicService.PreviousTrack();
                this._onStateChangedAction();
            });

            this._view.OnAction("open_menu", () =>
            {
                this._openMenuAction();
            });
        }

        private void BuildFallbackFluentView()
        {
            if (this._view == null) return;

            this._view.BuildWithFluent(ui =>
                ui.Div("w-full h-full flex flex-col justify-start items-start p-4").Children(
                    ui.Div("flex flex-row items-center w-320 h-80 sdv-box p-2 gap-2 overflow-hidden").Id("music_card").Children(
                        ui.Div("w-16 h-16 shrink-0 flex items-center justify-center sdv-panel overflow-hidden").Id("album_art_slot").Children(
                            ui.Icon("sparkles", "lucide-icon lucide-md")
                        ),
                        ui.Div("flex flex-col flex-1 h-full justify-between min-w-0").Children(
                            ui.Div("flex flex-row items-center w-full min-w-0").Children(
                                ui.Text("Not Running", "font-small text-sdv-brown shadow truncate w-full").Id("track_title")
                            ),
                            ui.Div("flex flex-row items-center justify-between w-full min-w-0").Children(
                                ui.Text("Music Player", "font-tiny text-sdv-dark truncate flex-1 mr-1").Id("track_artist"),
                                ui.Text("0:00 / 0:00", "font-tiny text-sdv-muted shrink-0").Id("hud_time")
                            ),
                            ui.Div("w-full").Id("progress_row").Children(
                                ui.ProgressBar("hud_progress_bar", 0f, 100f, "w-full h-1.5 sdv-bar")
                            ),
                            ui.Div("flex flex-row items-center justify-center gap-6 w-full h-5").Id("controls_row").Children(
                                ui.Div("w-6 h-5 flex items-center justify-center").Id("btn_prev").OnClick("prev"),
                                ui.Div("w-6 h-5 flex items-center justify-center").Id("btn_playpause").OnClick("playpause"),
                                ui.Div("w-6 h-5 flex items-center justify-center").Id("btn_next").OnClick("next")
                            )
                        )
                    )
                )
            );
        }

        public void ApplyConfigTheme()
        {
            if (this._view == null) return;

            this._view.SoundEnabled = this._config.EnableUiSounds;

            if (string.Equals(this._config.Theme, "FollowGlobal", StringComparison.OrdinalIgnoreCase))
            {
                this._view.UseRustedUiTheme = true;
                return;
            }

            this._view.UseRustedUiTheme = false;
            UiThemeId themeId = this._config.Theme switch
            {
                "PelicanClassic" => UiThemeId.PelicanClassic,
                "ShadcnZincDark" => UiThemeId.ShadcnZincDark,
                "WizardArcane" => UiThemeId.WizardArcane,
                "JojaCorp" => UiThemeId.JojaCorp,
                "CatppuccinMocha" => UiThemeId.CatppuccinMocha,
                "TokyoNight" => UiThemeId.TokyoNight,
                "Dracula" => UiThemeId.Dracula,
                "Nord" => UiThemeId.Nord,
                "Cyberpunk" => UiThemeId.Cyberpunk,
                _ => UiThemeId.JunimoForest
            };

            this._view.SetTheme(themeId);
        }

        public void UpdateTrack(TrackInfo track)
        {
            this._lastTrack = track;
            if (this._view == null) return;

            string normTitle = StringUtils.NormalizeForHud(track.Name);
            string normArtist = StringUtils.NormalizeForHud(track.Artist);

            this._titleMarquee.Text = normTitle;
            this._artistMarquee.Text = normArtist;

            string displayTitle = this._config.EnableMarqueeText ? this._titleMarquee.GetVisibleText() : normTitle;
            string displayArtist = this._config.EnableMarqueeText ? this._artistMarquee.GetVisibleText() : normArtist;

            this._view.SetText("track_title", displayTitle);
            this._view.SetText("track_artist", displayArtist);

            this._view.SetVisible("controls_row", this._config.ShowPlaybackButtons);
            this._view.SetVisible("album_art_slot", this._config.ShowAlbumArt);
            this._view.SetVisible("progress_row", this._config.ShowProgressBar);

            this.UpdateTimelineDisplay(track);
        }

        public void UpdatePerTickAnimations()
        {
            if (this._view == null || this._lastTrack.Name == "Not Running")
                return;

            // Update Marquee scrolling
            if (this._config.EnableMarqueeText)
            {
                this._titleMarquee.Update();
                this._artistMarquee.Update();
                this._view.SetText("track_title", this._titleMarquee.GetVisibleText());
                this._view.SetText("track_artist", this._artistMarquee.GetVisibleText());
            }

            // Update Timeline linear interpolation
            this.UpdateTimelineDisplay(this._lastTrack);
        }

        private void UpdateTimelineDisplay(TrackInfo track)
        {
            if (this._view == null) return;

            if (track.Duration > TimeSpan.Zero)
            {
                TimeSpan currentPos = track.GetExtrapolatedPosition();
                float percent = (float)(currentPos.TotalSeconds / track.Duration.TotalSeconds) * 100f;
                this._view.SetProgress("hud_progress_bar", Math.Clamp(percent, 0f, 100f), 100f);

                string curStr = FormatTime(currentPos);
                string durStr = FormatTime(track.Duration);
                this._view.SetText("hud_time", $"{curStr} / {durStr}");
            }
            else
            {
                this._view.SetProgress("hud_progress_bar", 0f, 100f);
                this._view.SetText("hud_time", "0:00 / 0:00");
            }
        }

        private static string FormatTime(TimeSpan span)
        {
            if (span.TotalHours >= 1.0)
                return $"{(int)span.TotalHours}:{span.Minutes:D2}:{span.Seconds:D2}";
            return $"{span.Minutes}:{span.Seconds:D2}";
        }

        public bool UpdateInput(float mouseX, float mouseY, bool leftMouseDown)
        {
            if (this._view == null) return false;

            int cardX = this._config.HudPositionX;
            int cardY = this._config.HudPositionY;
            int ctrlY = cardY + 54;

            Rectangle prevHit = new Rectangle(cardX + 148, ctrlY - 4, 24, 24);
            Rectangle playHit = new Rectangle(cardX + 182, ctrlY - 4, 24, 24);
            Rectangle nextHit = new Rectangle(cardX + 216, ctrlY - 4, 24, 24);

            bool handled = false;
            if (leftMouseDown && !this._wasMouseDown)
            {
                if (prevHit.Contains((int)mouseX, (int)mouseY))
                {
                    this._musicService.PreviousTrack();
                    this._onStateChangedAction();
                    handled = true;
                }
                else if (playHit.Contains((int)mouseX, (int)mouseY))
                {
                    this._musicService.TogglePlayPause();
                    this._onStateChangedAction();
                    handled = true;
                }
                else if (nextHit.Contains((int)mouseX, (int)mouseY))
                {
                    this._musicService.NextTrack();
                    this._onStateChangedAction();
                    handled = true;
                }
            }

            this._wasMouseDown = leftMouseDown;

            bool viewHandled = this._view.UpdateInput(mouseX, mouseY, leftMouseDown, 0f);
            return handled || viewHandled;
        }

        public void Draw(SpriteBatch spriteBatch, TrackInfo track)
        {
            if (this._view == null) return;

            // 1. Render RustedUI base layout (Taffy flexbox, theme window, typography, progress bar)
            this._view.Draw(spriteBatch);

            int cardX = this._config.HudPositionX;
            int cardY = this._config.HudPositionY;

            // 2. Overlay High-Resolution Album Art (flush 64x64 inside slot at cardX + 8, cardY + 8)
            if (this._config.ShowAlbumArt && track.CoverArt != null && !track.CoverArt.IsDisposed)
            {
                Rectangle artRect = new Rectangle(cardX + 8, cardY + 8, 64, 64);
                this.DrawRoundedArt(spriteBatch, track.CoverArt, artRect, radius: 4);
            }

            // 3. Draw Animated Equalizer Bars when track is playing (Image 1 aesthetic)
            if (track.IsPlaying)
            {
                this._equalizer.Update(isPlaying: true);
                float[] bars = this._equalizer.GetBars();
                int eqX = cardX + 78;
                int eqY = cardY + 36;
                for (int i = 0; i < 3; i++)
                {
                    int barHeight = Math.Max(2, (int)(bars[i] * 8f));
                    Rectangle barRect = new Rectangle(eqX + (i * 3), eqY - barHeight + 6, 2, barHeight);
                    spriteBatch.Draw(Game1.staminaRect, barRect, new Color(134, 239, 172, 220));
                }
            }

            // 4. Draw Lucide Media Control Icons (Prev, Play/Pause, Next)
            if (this._config.ShowPlaybackButtons)
            {
                Texture2D atlas = this.GetOrCreateMediaIconsTexture();
                float mouseX = Game1.getMouseX(ui_scale: true);
                float mouseY = Game1.getMouseY(ui_scale: true);

                int ctrlY = cardY + 54;
                Rectangle prevRect = new Rectangle(cardX + 152, ctrlY, 16, 16);
                Rectangle playRect = new Rectangle(cardX + 186, ctrlY, 16, 16);
                Rectangle nextRect = new Rectangle(cardX + 220, ctrlY, 16, 16);

                Rectangle prevHit = new Rectangle(prevRect.X - 4, prevRect.Y - 4, 24, 24);
                Rectangle playHit = new Rectangle(playRect.X - 4, playRect.Y - 4, 24, 24);
                Rectangle nextHit = new Rectangle(nextRect.X - 4, nextRect.Y - 4, 24, 24);

                Color normalColor = new Color(210, 220, 220, 220);
                Color hoverColor = Color.White;

                // Draw Prev (glyph 0)
                Color prevCol = prevHit.Contains((int)mouseX, (int)mouseY) ? hoverColor : normalColor;
                spriteBatch.Draw(atlas, prevRect, new Rectangle(0, 0, 16, 16), prevCol);

                // Draw Play / Pause (glyph 1 = Play, glyph 2 = Pause)
                int playSrcX = track.IsPlaying ? 32 : 16;
                Color playCol = playHit.Contains((int)mouseX, (int)mouseY) ? hoverColor : normalColor;
                spriteBatch.Draw(atlas, playRect, new Rectangle(playSrcX, 0, 16, 16), playCol);

                // Draw Next (glyph 3)
                Color nextCol = nextHit.Contains((int)mouseX, (int)mouseY) ? hoverColor : normalColor;
                spriteBatch.Draw(atlas, nextRect, new Rectangle(48, 0, 16, 16), nextCol);
            }
        }

        private Texture2D GetOrCreateMediaIconsTexture()
        {
            if (this._mediaIconsTexture != null && !this._mediaIconsTexture.IsDisposed)
                return this._mediaIconsTexture;

            const int width = 64;
            const int height = 16;
            Color[] pixels = new Color[width * height];

            string[][] glyphs = new[]
            {
                // 0: Prev (vertical bar + left-pointing triangle)
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
                // 1: Play (right-pointing triangle)
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
                // 2: Pause (two vertical bars)
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
                // 3: Next (right-pointing triangle + vertical bar)
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

            Texture2D tex = new Texture2D(Game1.graphics.GraphicsDevice, width, height);
            tex.SetData(pixels);
            this._mediaIconsTexture = tex;
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

            // Fast box downsampling
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
            this._view?.Dispose();
            this._view = null;

            this._roundedArtCache?.Dispose();
            this._roundedArtCache = null;

            this._mediaIconsTexture?.Dispose();
            this._mediaIconsTexture = null;

            this._textureCache.Dispose();
        }
    }
}
