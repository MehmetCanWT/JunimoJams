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

        private Texture2D? _roundedArtCache;
        private string _roundedArtCacheKey = string.Empty;
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
                    ui.Div("flex flex-row items-center w-400 h-auto sdv-box gap-3").Id("music_card").Children(
                        ui.Div("w-18 h-18 shrink-0 flex items-center justify-center sdv-panel overflow-hidden").Id("album_art_slot").Children(
                            ui.Icon("sparkles", "lucide-icon lucide-md")
                        ),
                        ui.Div("flex flex-col flex-1 gap-1 min-w-0").Children(
                            ui.Div("flex flex-row items-center justify-between w-full").Children(
                                ui.Text("Not Running", "font-dialogue text-sdv-brown shadow truncate flex-1 mr-2").Id("track_title"),
                                ui.Text("Idle", "font-tiny text-sdv-muted shrink-0").Id("hud_badge")
                            ),
                            ui.Div("flex flex-row items-center justify-between w-full").Children(
                                ui.Text("Music Player", "font-small text-sdv-dark truncate flex-1 mr-2").Id("track_artist"),
                                ui.Text("0:00 / 0:00", "font-tiny text-sdv-muted shrink-0").Id("hud_time")
                            ),
                            ui.Div("w-full py-1").Id("progress_row").Children(
                                ui.ProgressBar("hud_progress_bar", 0f, 100f, "w-full h-2 sdv-bar")
                            ),
                            ui.Div("flex flex-row items-center justify-between w-full pt-1").Id("controls_row").Children(
                                ui.Div("flex flex-row items-center gap-2").Children(
                                    ui.ArrowLeft("btn_prev", "prev"),
                                    ui.Button("btn_playpause", "Play", "playpause", "px-3"),
                                    ui.ArrowRight("btn_next", "next")
                                ),
                                ui.Button("btn_open_hub", "Hub [F6]", "open_menu", "px-2")
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

            string status = track.IsPlaying ? "Playing" : (track.Name == "Not Running" ? "Idle" : "Paused");
            this._view.SetText("hud_badge", status);

            string btnLabel = track.IsPlaying ? "Pause" : "Play";
            this._view.SetText("btn_playpause", btnLabel);

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
            return this._view.UpdateInput(mouseX, mouseY, leftMouseDown, 0f);
        }

        public void Draw(SpriteBatch spriteBatch, TrackInfo track)
        {
            if (this._view == null) return;

            // Render RustedUI layout (Taffy flexbox, theme 9-slice box, badges, fonts, buttons, tooltips)
            this._view.Draw(spriteBatch);

            // Overlay High-Resolution Rounded Album Art if available
            if (this._config.ShowAlbumArt && track.CoverArt != null && !track.CoverArt.IsDisposed)
            {
                int artX = this._config.HudPositionX + 24 + 4;
                int artY = this._config.HudPositionY + 24 + 4;
                Rectangle destRect = new Rectangle(artX, artY, 64, 64);

                this.DrawRoundedArt(spriteBatch, track.CoverArt, destRect, radius: 6);
            }
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

            this._textureCache.Dispose();
        }
    }
}
