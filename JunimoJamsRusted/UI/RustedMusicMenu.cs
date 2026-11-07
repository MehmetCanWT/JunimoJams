using System;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using JunimoJamsRusted.Services;
using Rusted.Smapi.Native;
using Rusted.Smapi.UI;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;

namespace JunimoJamsRusted.UI
{
    public sealed class RustedMusicMenu : IClickableMenu
    {
        private readonly RustedView _view;
        private readonly MusicService _musicService;
        private readonly ModConfig _config;
        private readonly IModHelper _helper;
        private readonly Action _onThemeChanged;
        private TrackInfo _track;

        public RustedView View => this._view;

        public RustedMusicMenu(
            MusicService musicService,
            TrackInfo track,
            ModConfig config,
            IModHelper helper,
            Action onThemeChanged)
            : base(0, 0, Game1.uiViewport.Width, Game1.uiViewport.Height, showUpperRightCloseButton: false)
        {
            this._musicService = musicService;
            this._track = track;
            this._config = config;
            this._helper = helper;
            this._onThemeChanged = onThemeChanged;

            bool followGlobal = string.Equals(config.Theme, "FollowGlobal", StringComparison.OrdinalIgnoreCase);
            this._view = new RustedView(ownerModId: "MehmetCanWT.JunimoJamsRusted", useRustedUiTheme: followGlobal);
            this._view.SoundEnabled = config.EnableUiSounds;

            string menuPath = Path.Combine(helper.DirectoryPath, "assets", "ui", "music_menu.html");
            if (File.Exists(menuPath))
            {
                this._view.LoadMarkupFileWithHotReload(menuPath);
            }
            else
            {
                this.BuildFallbackFluent();
            }

            this.ApplyTheme(this.GetThemeIdFromName(config.Theme), config.Theme);
            this.BindActions();
            this.UpdateTrackState(track);
        }

        private void BindActions()
        {
            this._view.OnAction("close_menu", () =>
            {
                Game1.playSound("drumkit6");
                this.exitThisMenu(playSound: false);
            });

            this._view.OnAction("playpause", () =>
            {
                this._musicService.TogglePlayPause();
                this._track = this._musicService.GetCurrentTrack();
                this.UpdateTrackState(this._track);
            });

            this._view.OnAction("next", () =>
            {
                this._musicService.NextTrack();
                this._track = this._musicService.GetCurrentTrack();
                this.UpdateTrackState(this._track);
            });

            this._view.OnAction("prev", () =>
            {
                this._musicService.PreviousTrack();
                this._track = this._musicService.GetCurrentTrack();
                this.UpdateTrackState(this._track);
            });

            // Theme actions
            this._view.OnAction("theme_junimo", () => this.SetThemeAndSave(UiThemeId.JunimoForest, "JunimoForest"));
            this._view.OnAction("theme_pelican", () => this.SetThemeAndSave(UiThemeId.PelicanClassic, "PelicanClassic"));
            this._view.OnAction("theme_zinc", () => this.SetThemeAndSave(UiThemeId.ShadcnZincDark, "ShadcnZincDark"));
            this._view.OnAction("theme_arcane", () => this.SetThemeAndSave(UiThemeId.WizardArcane, "WizardArcane"));
            this._view.OnAction("theme_joja", () => this.SetThemeAndSave(UiThemeId.JojaCorp, "JojaCorp"));
            this._view.OnAction("theme_catppuccin", () => this.SetThemeAndSave(UiThemeId.CatppuccinMocha, "CatppuccinMocha"));
            this._view.OnAction("theme_tokyonight", () => this.SetThemeAndSave(UiThemeId.TokyoNight, "TokyoNight"));
            this._view.OnAction("theme_dracula", () => this.SetThemeAndSave(UiThemeId.Dracula, "Dracula"));
            this._view.OnAction("theme_nord", () => this.SetThemeAndSave(UiThemeId.Nord, "Nord"));
            this._view.OnAction("theme_cyberpunk", () => this.SetThemeAndSave(UiThemeId.Cyberpunk, "Cyberpunk"));
        }

        private void SetThemeAndSave(UiThemeId themeId, string themeName)
        {
            this._config.Theme = themeName;
            this._helper.WriteConfig(this._config);
            this.ApplyTheme(themeId, themeName);
            this._onThemeChanged();
        }

        private void ApplyTheme(UiThemeId themeId, string themeName)
        {
            this._view.SetTheme(themeId);
            this._view.SetText("menu_active_theme", $"Theme: {themeName}");
        }

        private UiThemeId GetThemeIdFromName(string name)
        {
            return name switch
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
        }

        public void UpdateTrackState(TrackInfo track)
        {
            this._track = track;
            string normTitle = StringUtils.NormalizeForHud(track.Name);
            string normArtist = StringUtils.NormalizeForHud(track.Artist);

            this._view.SetText("menu_track_title", normTitle);
            this._view.SetText("menu_track_artist", normArtist);

            string status = track.IsPlaying ? "Playing" : (track.Name == "Not Running" ? "Idle" : "Paused");
            this._view.SetText("menu_status_badge", status);

            string btnLabel = track.IsPlaying ? "Pause" : "Play";
            this._view.SetText("btn_menu_playpause", btnLabel);

            if (track.Duration > TimeSpan.Zero)
            {
                TimeSpan currentPos = track.GetExtrapolatedPosition();
                float percent = (float)(currentPos.TotalSeconds / track.Duration.TotalSeconds) * 100f;
                this._view.SetProgress("menu_progress_bar", Math.Clamp(percent, 0f, 100f), 100f);

                string curStr = FormatTime(currentPos);
                string durStr = FormatTime(track.Duration);
                this._view.SetText("menu_time", $"{curStr} / {durStr}");
            }
            else
            {
                this._view.SetProgress("menu_progress_bar", 0f, 100f);
                this._view.SetText("menu_time", "0:00 / 0:00");
            }
        }

        private static string FormatTime(TimeSpan span)
        {
            if (span.TotalHours >= 1.0)
                return $"{(int)span.TotalHours}:{span.Minutes:D2}:{span.Seconds:D2}";
            return $"{span.Minutes}:{span.Seconds:D2}";
        }

        public override void receiveKeyPress(Keys key)
        {
            if (key == Keys.Escape || Game1.options.doesInputListContain(Game1.options.menuButton, key))
            {
                Game1.playSound("drumkit6");
                this.exitThisMenu(playSound: false);
                return;
            }

            base.receiveKeyPress(key);
        }

        public override void update(GameTime time)
        {
            base.update(time);

            MouseState mouse = Mouse.GetState();
            bool leftDown = mouse.LeftButton == ButtonState.Pressed;
            float uiX = Game1.getMouseX(ui_scale: true);
            float uiY = Game1.getMouseY(ui_scale: true);
            this._view.UpdateInput(uiX, uiY, leftDown, 0f);

            // Continuously update extrapolated progress in the menu
            this.UpdateTrackState(this._track);
        }

        public override void draw(SpriteBatch b)
        {
            // Dim background like vanilla menus
            if (!Game1.options.showClearBackgrounds)
            {
                b.Draw(Game1.fadeToBlackRect, Game1.graphics.GraphicsDevice.Viewport.Bounds, Color.Black * 0.55f);
            }

            this._view.Draw(b);
            drawMouse(b);
        }

        private void BuildFallbackFluent()
        {
            this._view.BuildWithFluent(ui =>
                ui.Div("w-full h-full flex flex-col justify-center items-center").Children(
                    ui.Div("flex flex-col w-640 h-auto sdv-box gap-3").Id("music_hub_window").Children(
                        ui.Div("flex flex-row justify-between items-center w-full gap-2").Children(
                            ui.Banner("Junimo Jams Music Hub", "font-dialogue text-sdv-brown shadow"),
                            ui.CloseButton("btn_close")
                        ),
                        ui.Panel("flex flex-col gap-2 sdv-panel w-full").Children(
                            ui.Title("Now Playing", "font-dialogue text-sdv-brown"),
                            ui.Text("No Track Playing", "font-dialogue text-sdv-brown shadow").Id("menu_track_title"),
                            ui.Text("Start Spotify or YouTube Music", "font-small text-sdv-dark").Id("menu_track_artist"),
                            ui.Div("w-full py-1").Children(
                                ui.ProgressBar("menu_progress_bar", 0f, 100f, "w-full h-4")
                            ),
                            ui.Div("flex flex-row items-center justify-center gap-4 w-full pt-2").Children(
                                ui.Button("btn_menu_prev", "|< Prev", "prev", "px-4"),
                                ui.Button("btn_menu_playpause", "Play", "playpause", "px-6"),
                                ui.Button("btn_menu_next", "Next >|", "next", "px-4")
                            )
                        ),
                        ui.Divider(),
                        ui.Div("flex flex-row items-center justify-between w-full px-2").Children(
                            ui.Text("Universal Music Hub", "font-tiny text-sdv-muted"),
                            ui.Button("btn_menu_close_bottom", "Done", "close_menu", "px-4")
                        )
                    )
                )
            );
        }
    }
}
