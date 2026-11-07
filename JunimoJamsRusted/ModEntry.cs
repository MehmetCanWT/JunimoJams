using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework.Graphics;
using JunimoJamsRusted.Services;
using JunimoJamsRusted.UI;
using Rusted.Smapi.Native;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;

namespace JunimoJamsRusted
{
    public sealed class ModEntry : Mod
    {
        private ModConfig _config = new();
        private MusicService _musicService = null!;
        private ArtService _artService = null!;
        private RustedMusicHud _hud = null!;

        private TrackInfo _currentTrack = new();
        private string _lastArtKey = string.Empty;
        private int _checkInterval = 60;
        private int _checkTimer = 0;

        private byte[]? _pendingArtBytes;
        private string _pendingArtTrackKey = string.Empty;
        private string? _pendingRealArtist;
        private readonly object _artLock = new();
        private CancellationTokenSource? _artCts;

        public override void Entry(IModHelper helper)
        {
            this._config = helper.ReadConfig<ModConfig>();

            try
            {
                // Ensure native FFI resolver is initialized
                NativeResolver.Initialize(helper, this.Monitor);

                this._musicService = new MusicService(this._config.ExtraPlayers, (msg, lvl) => this.Monitor.Log(msg, lvl));
                this._artService = new ArtService(helper.DirectoryPath);
                this._hud = new RustedMusicHud(
                    this._config,
                    helper,
                    this._musicService,
                    openMenuAction: this.ToggleMusicMenu,
                    onStateChangedAction: this.UpdateMusicState
                );

                this.Monitor.Log("Junimo Jams (RustedUI Edition) initialized successfully!", LogLevel.Info);
            }
            catch (Exception ex)
            {
                this.Monitor.Log($"Error initializing Junimo Jams (RustedUI Edition): {ex.Message}", LogLevel.Error);
            }

            helper.Events.GameLoop.GameLaunched += this.OnGameLaunched;
            helper.Events.GameLoop.UpdateTicked += this.OnUpdateTicked;
            helper.Events.GameLoop.ReturnedToTitle += this.OnReturnedToTitle;
            helper.Events.Display.RenderingHud += this.OnRenderingHud;
            helper.Events.Display.RenderedActiveMenu += this.OnRenderedActiveMenu;
            helper.Events.Input.ButtonPressed += this.OnButtonPressed;
        }

        private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
        {
            this.RegisterGenericModConfigMenu();
        }

        private void RegisterGenericModConfigMenu()
        {
            var configMenu = this.Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
            if (configMenu == null)
                return;

            configMenu.Register(this.ModManifest, () =>
            {
                this._config = new ModConfig();
                this._hud.ApplyConfigTheme();
            }, () =>
            {
                this.Helper.WriteConfig(this._config);
                this._hud.ApplyConfigTheme();
            });

            configMenu.AddSectionTitle(this.ModManifest, () => "HUD Settings");
            configMenu.AddBoolOption(this.ModManifest, () => this._config.ShowHud, v => this._config.ShowHud = v, () => "Show HUD", () => "Toggle overlay visibility");
            configMenu.AddBoolOption(this.ModManifest, () => this._config.EnableUiSounds, v => { this._config.EnableUiSounds = v; this._hud.ApplyConfigTheme(); }, () => "UI Sound Effects", () => "Enable audio feedback on button clicks");
            configMenu.AddBoolOption(this.ModManifest, () => this._config.ShowAlbumArt, v => this._config.ShowAlbumArt = v, () => "Show Album Cover", () => "Toggle album art visibility");
            configMenu.AddBoolOption(this.ModManifest, () => this._config.ShowPlaybackButtons, v => this._config.ShowPlaybackButtons = v, () => "Show Buttons", () => "Toggle playback controls visibility");
            configMenu.AddBoolOption(this.ModManifest, () => this._config.ShowProgressBar, v => this._config.ShowProgressBar = v, () => "Show Timeline Bar", () => "Toggle track progress bar");
            configMenu.AddBoolOption(this.ModManifest, () => this._config.EnableMarqueeText, v => this._config.EnableMarqueeText = v, () => "Marquee Scrolling", () => "Smoothly scroll long song and artist titles");
            configMenu.AddBoolOption(this.ModManifest, () => this._config.OnlyShowInInventory, v => this._config.OnlyShowInInventory = v, () => "Only In Menu", () => "Show only when inventory open");
            configMenu.AddBoolOption(this.ModManifest, () => this._config.HideOnMap, v => this._config.HideOnMap = v, () => "Hide On Map", () => "Hide when map or overlay menus are open");

            string[] themeOptions = new[]
            {
                "JunimoForest",
                "PelicanClassic",
                "ShadcnZincDark",
                "WizardArcane",
                "JojaCorp",
                "CatppuccinMocha",
                "TokyoNight",
                "Dracula",
                "Nord",
                "Cyberpunk",
                "FollowGlobal"
            };

            configMenu.AddTextOption(
                this.ModManifest,
                () => this._config.Theme,
                v => { this._config.Theme = v; this._hud.ApplyConfigTheme(); },
                () => "Rusted Theme",
                () => "Select the active RustedUI theme or FollowGlobal",
                themeOptions
            );

            configMenu.AddKeybindList(
                this.ModManifest,
                () => this._config.OpenMenuKey,
                v => this._config.OpenMenuKey = v,
                () => "Open Music Hub Key",
                () => "Keybind to open the interactive Junimo Jams Music Hub"
            );

            configMenu.AddNumberOption(this.ModManifest, () => this._config.HudPositionX, v => this._config.HudPositionX = v, () => "X Position", () => "Horizontal position", 0, 2000);
            configMenu.AddNumberOption(this.ModManifest, () => this._config.HudPositionY, v => this._config.HudPositionY = v, () => "Y Position", () => "Vertical position", 0, 2000);
        }

        private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
        {
            if (!this._config.ShowHud)
                return;

            // Per-tick animation updates: marquee scrolling + linear timeline extrapolation
            this._hud.UpdatePerTickAnimations();

            // Update mouse input on HUD when in gameplay to support live tooltips and hover effects
            if (Game1.activeClickableMenu == null && this.IsHudVisible())
            {
                float uiX = Game1.getMouseX(ui_scale: true);
                float uiY = Game1.getMouseY(ui_scale: true);
                bool leftDown = Game1.input.GetMouseState().LeftButton == Microsoft.Xna.Framework.Input.ButtonState.Pressed;
                this._hud.UpdateInput(uiX, uiY, leftDown);
            }

            this._checkTimer++;
            if (this._checkTimer >= this._checkInterval)
            {
                this._checkTimer = 0;
                this.UpdateMusicState();
            }
        }

        private void UpdateMusicState()
        {
            if (this._musicService == null)
                return;

            TrackInfo newTrack = this._musicService.GetCurrentTrack();

            bool isSameTrack = newTrack.Name == this._currentTrack.Name &&
                               (newTrack.Artist == this._currentTrack.Artist ||
                                (newTrack.Artist == "YouTube Music" && this._currentTrack.Artist != "YouTube Music" && this._currentTrack.Artist != "Unknown"));

            if (isSameTrack)
            {
                newTrack.CoverArt = this._currentTrack.CoverArt;
                newTrack.Artist = this._currentTrack.Artist;
                this._currentTrack = newTrack;
            }
            else
            {
                this._currentTrack?.Dispose();
                this._currentTrack = newTrack;
                this.FetchAlbumArt(newTrack);
            }

            this._hud?.UpdateTrack(this._currentTrack);

            if (Game1.activeClickableMenu is RustedMusicMenu menu)
            {
                menu.UpdateTrackState(this._currentTrack);
            }
        }

        private void FetchAlbumArt(TrackInfo track)
        {
            if (this._artService == null || track.Name == "Unknown" || track.Name == "Not Running")
                return;

            string key = $"{track.Artist}-{track.Name}";
            if (key == this._lastArtKey)
                return;

            this._artCts?.Cancel();
            this._artCts?.Dispose();
            this._artCts = new CancellationTokenSource();
            var token = this._artCts.Token;

            this._lastArtKey = key;

            Task.Run(async () =>
            {
                try
                {
                    var result = await this._artService.FetchCoverArtBytes(track, token);
                    if (result?.Bytes != null && result.Bytes.Length > 0)
                    {
                        lock (this._artLock)
                        {
                            this._pendingArtBytes = result.Bytes;
                            this._pendingArtTrackKey = key;
                            this._pendingRealArtist = result.RealArtist;
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    // Skipped quickly; do nothing
                }
                catch
                {
                    // Ignore transient network errors
                }
            }, token);
        }

        private void ProcessPendingArt()
        {
            byte[]? artToLoad = null;
            string? artKey = null;
            string? realArtist = null;

            lock (this._artLock)
            {
                if (this._pendingArtBytes != null)
                {
                    artToLoad = this._pendingArtBytes;
                    artKey = this._pendingArtTrackKey;
                    realArtist = this._pendingRealArtist;
                    this._pendingArtBytes = null;
                    this._pendingRealArtist = null;
                }
            }

            if (artToLoad != null)
            {
                try
                {
                    bool keyMatch = $"{this._currentTrack.Artist}-{this._currentTrack.Name}" == artKey ||
                                    (this._currentTrack.Artist == "YouTube Music" && $"YouTube Music-{this._currentTrack.Name}" == artKey);

                    if (keyMatch)
                    {
                        using MemoryStream stream = new(artToLoad);
                        this._currentTrack.CoverArt?.Dispose();
                        this._currentTrack.CoverArt = Texture2D.FromStream(Game1.graphics.GraphicsDevice, stream);

                        if (!string.IsNullOrEmpty(realArtist))
                        {
                            this._currentTrack.Artist = realArtist;
                        }

                        this._hud?.UpdateTrack(this._currentTrack);
                        if (Game1.activeClickableMenu is RustedMusicMenu menu)
                        {
                            menu.UpdateTrackState(this._currentTrack);
                        }
                    }
                }
                catch (Exception ex)
                {
                    this.Monitor.Log($"Cover art texture load error: {ex.Message}", LogLevel.Trace);
                }
            }
        }

        private void OnRenderingHud(object? sender, RenderingHudEventArgs e)
        {
            this.ProcessPendingArt();

            if (!this.IsHudVisible())
                return;

            if (Game1.activeClickableMenu != null)
                return;

            this._hud.Draw(e.SpriteBatch, this._currentTrack);
        }

        private void OnRenderedActiveMenu(object? sender, RenderedActiveMenuEventArgs e)
        {
            this.ProcessPendingArt();

            if (!this.IsHudVisible())
                return;

            if (Game1.activeClickableMenu == null || Game1.activeClickableMenu is RustedMusicMenu)
                return;

            this._hud.Draw(e.SpriteBatch, this._currentTrack);
        }

        private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
        {
            if (this._config.OpenMenuKey.JustPressed())
            {
                this.ToggleMusicMenu();
                this.Helper.Input.Suppress(e.Button);
                return;
            }

            if (e.Button == SButton.MouseLeft && this.IsHudVisible() && Game1.activeClickableMenu == null)
            {
                float uiX = Game1.getMouseX(ui_scale: true);
                float uiY = Game1.getMouseY(ui_scale: true);
                if (this._hud.UpdateInput(uiX, uiY, leftMouseDown: true))
                {
                    this.Helper.Input.Suppress(SButton.MouseLeft);
                }
            }
        }

        private void ToggleMusicMenu()
        {
            if (Game1.activeClickableMenu is RustedMusicMenu)
            {
                Game1.playSound("drumkit6");
                Game1.activeClickableMenu = null;
            }
            else
            {
                Game1.playSound("bigSelect");
                Game1.activeClickableMenu = new RustedMusicMenu(
                    this._musicService,
                    this._currentTrack,
                    this._config,
                    this.Helper,
                    onThemeChanged: () => this._hud.ApplyConfigTheme()
                );
            }
        }

        private bool IsHudVisible()
        {
            if (!this._config.ShowHud || !Context.IsWorldReady)
                return false;

            var menu = Game1.activeClickableMenu;

            if (this._config.HideOnMap && menu != null)
            {
                if (menu is not GameMenu)
                    return false;

                if (menu is GameMenu gameMenu && gameMenu.currentTab == GameMenu.mapTab)
                    return false;
            }

            if (this._config.OnlyShowInInventory)
                return menu is GameMenu;

            return true;
        }

        private void OnReturnedToTitle(object? sender, ReturnedToTitleEventArgs e)
        {
            this._artCts?.Cancel();
            this._artCts?.Dispose();
            this._artCts = null;

            this._currentTrack?.Dispose();
            this._currentTrack = new TrackInfo();
            this._lastArtKey = string.Empty;

            lock (this._artLock)
            {
                this._pendingArtBytes = null;
                this._pendingArtTrackKey = string.Empty;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                this._hud?.Dispose();
                this._currentTrack?.Dispose();
                this._artService?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
