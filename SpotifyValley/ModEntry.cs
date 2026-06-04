using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework.Graphics;
using SpotifyValley.Services;
using SpotifyValley.UI;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;

namespace SpotifyValley
{
    public class ModEntry : Mod
    {
        private ModConfig Config;
        private MusicService _musicService;
        private MusicOverlay _overlay;
        private ArtService _artService;
        
        private TrackInfo _currentTrack = new TrackInfo();
        private string _lastArtKey = "";
        
        private int _checkInterval = 60;
        private int _checkTimer = 0;
        
        private byte[] _pendingArtBytes;
        private string _pendingArtTrackKey = "";
        private readonly object _artLock = new object();

        // Cancellation support for album art fetching — cancels the previous
        // download when the user skips to the next track before it finishes.
        private CancellationTokenSource _artCts;

        public override void Entry(IModHelper helper)
        {
            this.Config = helper.ReadConfig<ModConfig>();
            try
            {
                this._musicService = new MusicService(this.Config.ExtraPlayers);
                this._artService = new ArtService(base.Helper.DirectoryPath);
                this._overlay = new MusicOverlay(this.Config, base.Helper);
                base.Monitor.Log("Spotify Valley Initialized.", LogLevel.Info);
            }
            catch (Exception ex)
            {
                base.Monitor.Log("Error initializing: " + ex.Message, LogLevel.Error);
            }

            helper.Events.GameLoop.GameLaunched += this.OnGameLaunched;
            helper.Events.GameLoop.UpdateTicked += this.OnUpdateTicked;
            helper.Events.GameLoop.ReturnedToTitle += this.OnReturnedToTitle;
            helper.Events.Display.RenderingHud += this.OnRenderingHud;
            helper.Events.Display.RenderedActiveMenu += this.OnRenderedActiveMenu;
            helper.Events.Input.ButtonPressed += this.OnButtonPressed;
        }

        private void OnGameLaunched(object sender, GameLaunchedEventArgs e)
        {
            var configMenu = base.Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
            if (configMenu == null)
                return;

            configMenu.Register(base.ModManifest, () =>
            {
                this.Config = new ModConfig();
                this._overlay?.ReloadTextures();
            }, () =>
            {
                base.Helper.WriteConfig(this.Config);
                this._overlay?.ReloadTextures();
            });

            configMenu.AddSectionTitle(base.ModManifest, () => "HUD Settings");
            configMenu.AddBoolOption(base.ModManifest, () => this.Config.ShowHud, v => this.Config.ShowHud = v, () => "Show HUD", () => "Toggle overlay visibility");
            configMenu.AddBoolOption(base.ModManifest, () => this.Config.OnlyShowInInventory, v => this.Config.OnlyShowInInventory = v, () => "Only In Menu", () => "Show only when inventory open");
            configMenu.AddBoolOption(base.ModManifest, () => this.Config.ShowAlbumArt, v => this.Config.ShowAlbumArt = v, () => "Show Album Cover", () => "Toggle album art visibility");
            configMenu.AddBoolOption(base.ModManifest, () => this.Config.ShowPlaybackButtons, v => this.Config.ShowPlaybackButtons = v, () => "Show Buttons", () => "Toggle playback controls visibility");

            string[] themeOptions = this.GetInstalledThemes();
            configMenu.AddTextOption(base.ModManifest, () => this.Config.Theme, v => this.Config.Theme = v, () => "Theme", () => "Select the visual style (Folder name in assets/)", themeOptions);

            configMenu.AddNumberOption(base.ModManifest, () => this.Config.HudPositionX, v => this.Config.HudPositionX = v, () => "X Position", () => "Horizontal position", 0, 2000);
            configMenu.AddNumberOption(base.ModManifest, () => this.Config.HudPositionY, v => this.Config.HudPositionY = v, () => "Y Position", () => "Vertical position", 0, 2000);
            configMenu.AddNumberOption(base.ModManifest, () => this.Config.HudScale, v => this.Config.HudScale = v, () => "Scale", () => "Size multiplier", 0.5f, 2f, 0.1f);
        }

        private string[] GetInstalledThemes()
        {
            List<string> themes = new List<string> { "default" };
            string assetsPath = Path.Combine(base.Helper.DirectoryPath, "assets");
            
            if (Directory.Exists(assetsPath))
            {
                string[] directories = Directory.GetDirectories(assetsPath)
                    .Select(d => new DirectoryInfo(d).Name)
                    .Where(name => name != "default")
                    .ToArray();
                themes.AddRange(directories);
            }
            return themes.ToArray();
        }

        private void OnUpdateTicked(object sender, UpdateTickedEventArgs e)
        {
            if (!this.IsHudVisible() && !this.Config.ShowHud)
                return;

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
            
            // Check if track changed
            if (newTrack.Name == this._currentTrack.Name && newTrack.Artist == this._currentTrack.Artist)
            {
                // Reuse existing art
                newTrack.CoverArt = this._currentTrack.CoverArt;
                this._currentTrack = newTrack;
            }
            else
            {
                // Track changed, cleanup old one
                this._currentTrack?.Dispose();
                this._currentTrack = newTrack;
                this.FetchAlbumArt(newTrack);
            }
        }

        private void FetchAlbumArt(TrackInfo track)
        {
            if (this._artService == null)
                return;

            if (track.Name == "Unknown" || track.Name == "Not Running")
                return;

            string key = $"{track.Artist}-{track.Name}";
            if (key == this._lastArtKey)
                return;

            // Cancel any in-flight download from the previous track
            this._artCts?.Cancel();
            this._artCts?.Dispose();
            this._artCts = new CancellationTokenSource();
            var token = this._artCts.Token;

            this._lastArtKey = key;

            Task.Run(async () =>
            {
                try
                {
                    byte[] imageBytes = await this._artService.FetchCoverArtBytes(track.Artist, track.Name, token);
                    if (imageBytes != null && imageBytes.Length != 0)
                    {
                        lock (this._artLock)
                        {
                            this._pendingArtBytes = imageBytes;
                            this._pendingArtTrackKey = key;
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    // Expected when skipping tracks rapidly — do nothing
                }
                catch
                {
                    // Ignore other errors
                }
            }, token);
        }

        /// <summary>
        /// Processes pending album art bytes (downloaded on a background thread)
        /// and converts them into a Texture2D on the main graphics thread.
        /// This must be called from a rendering event to access the GraphicsDevice safely.
        /// </summary>
        private void ProcessPendingArt()
        {
            byte[] artToLoad = null;
            string artKey = null;

            lock (this._artLock)
            {
                if (this._pendingArtBytes != null)
                {
                    artToLoad = this._pendingArtBytes;
                    artKey = this._pendingArtTrackKey;
                    this._pendingArtBytes = null;
                }
            }

            if (artToLoad != null)
            {
                try
                {
                    if ($"{this._currentTrack.Artist}-{this._currentTrack.Name}" == artKey)
                    {
                        using (MemoryStream stream = new MemoryStream(artToLoad))
                        {
                            // Dispose old art before creating new one
                            this._currentTrack.CoverArt?.Dispose();
                            this._currentTrack.CoverArt = Texture2D.FromStream(Game1.graphics.GraphicsDevice, stream);
                            this._overlay?.OnTrackChanged(); // Trigger layout update
                        }
                    }
                }
                catch (Exception ex)
                {
                    base.Monitor.Log("Texture load error: " + ex.Message, LogLevel.Trace);
                }
            }
        }

        /// <summary>
        /// Draws the HUD when no active menu is open (normal gameplay).
        /// When a menu IS open, rendering is deferred to OnRenderedActiveMenu
        /// so the HUD appears above the menu's shadow overlay.
        /// </summary>
        private void OnRenderingHud(object sender, RenderingHudEventArgs e)
        {
            this.ProcessPendingArt();

            if (!this.IsHudVisible())
                return;

            // Only draw here when there is no active menu.
            // When a menu is open, OnRenderedActiveMenu handles drawing
            // so the HUD appears above the menu shadow.
            if (Game1.activeClickableMenu != null)
                return;

            this.DrawOverlay(e.SpriteBatch);
        }

        /// <summary>
        /// Draws the HUD after the active menu has been rendered, placing it
        /// on top of the menu and its shadow overlay. This fixes the user-reported
        /// issue where the HUD was darkened/covered when "Only In Menu" was enabled.
        /// </summary>
        private void OnRenderedActiveMenu(object sender, RenderedActiveMenuEventArgs e)
        {
            this.ProcessPendingArt();

            if (!this.IsHudVisible())
                return;

            // Only draw here when a menu is open
            if (Game1.activeClickableMenu == null)
                return;

            this.DrawOverlay(e.SpriteBatch);
        }

        /// <summary>
        /// Shared draw logic used by both OnRenderingHud and OnRenderedActiveMenu.
        /// </summary>
        private void DrawOverlay(SpriteBatch spriteBatch)
        {
            if (this._currentTrack.Name != "Unknown" && this._currentTrack.Name != "Not Running")
            {
                this._overlay?.Draw(spriteBatch, this._currentTrack);
            }
        }

        private void OnButtonPressed(object sender, ButtonPressedEventArgs e)
        {
            if (e.Button != SButton.MouseLeft || !this.IsHudVisible() || this._musicService == null || this._overlay == null)
                return;

            string action = this._overlay.HandleClick(Game1.getMouseX(true), Game1.getMouseY(true));
            if (action == "playpause")
            {
                this._musicService.TogglePlayPause();
            }
            else if (action == "next")
            {
                this._musicService.NextTrack();
                this.UpdateMusicState();
            }
            else if (action == "prev")
            {
                this._musicService.PreviousTrack();
                this.UpdateMusicState();
            }

            if (action != null)
            {
                this.UpdateMusicState();
            }
        }

        private bool IsHudVisible()
        {
            if (!this.Config.ShowHud || !Context.IsWorldReady)
                return false;

            return !this.Config.OnlyShowInInventory || Game1.activeClickableMenu is GameMenu;
        }

        /// <summary>
        /// Cleanup resources when returning to the title screen to prevent
        /// texture memory leaks across save file changes.
        /// </summary>
        private void OnReturnedToTitle(object sender, ReturnedToTitleEventArgs e)
        {
            this._artCts?.Cancel();
            this._artCts?.Dispose();
            this._artCts = null;

            this._currentTrack?.Dispose();
            this._currentTrack = new TrackInfo();
            this._lastArtKey = "";

            lock (this._artLock)
            {
                this._pendingArtBytes = null;
                this._pendingArtTrackKey = "";
            }
        }
    }
}
