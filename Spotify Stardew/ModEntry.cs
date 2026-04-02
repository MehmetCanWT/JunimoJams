using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Xna.Framework.Graphics;
using Spotify_Stardew.Services;
using Spotify_Stardew.UI;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;

namespace Spotify_Stardew
{
    public class ModEntry : Mod
    {
        private ModConfig Config;
        private MusicService _musicService;
        private MusicOverlay _overlay;
        private ArtService _artService;
        
        private TrackInfo _currentTrack = new TrackInfo();
        private string _lastArtKey = "";
        private bool _isFetchingArt = false;
        
        private int _checkInterval = 60;
        private int _checkTimer = 0;
        
        private byte[] _pendingArtBytes;
        private string _pendingArtTrackKey = "";
        private readonly object _artLock = new object();

        public override void Entry(IModHelper helper)
        {
            this.Config = helper.ReadConfig<ModConfig>();
            try
            {
                this._musicService = new MusicService();
                this._artService = new ArtService();
                this._overlay = new MusicOverlay(this.Config, base.Helper);
                base.Monitor.Log("Spotify Stardew Initialized (Native + Cover Art).", LogLevel.Info);
            }
            catch (Exception ex)
            {
                base.Monitor.Log("Error initializing: " + ex.Message, LogLevel.Error);
            }

            helper.Events.GameLoop.GameLaunched += this.OnGameLaunched;
            helper.Events.GameLoop.UpdateTicked += this.OnUpdateTicked;
            helper.Events.Display.RenderingHud += this.OnRenderingHud;
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
            if (this._artService == null || this._isFetchingArt)
                return;

            if (track.Name == "Unknown" || track.Name == "Not Running")
                return;

            string key = $"{track.Artist}-{track.Name}";
            if (key == this._lastArtKey)
                return;

            this._isFetchingArt = true;
            this._lastArtKey = key;

            Task.Run(async () =>
            {
                try
                {
                    byte[] imageBytes = await this._artService.FetchCoverArtBytes(track.Artist, track.Name);
                    if (imageBytes != null && imageBytes.Length != 0)
                    {
                        lock (this._artLock)
                        {
                            this._pendingArtBytes = imageBytes;
                            this._pendingArtTrackKey = key;
                        }
                    }
                }
                catch
                {
                    // Ignore errors
                }
                finally
                {
                    this._isFetchingArt = false;
                }
            });
        }

        private void OnRenderingHud(object sender, RenderingHudEventArgs e)
        {
            // Handle pending art bytes from background task
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

            if (!this.IsHudVisible())
                return;

            if (this._currentTrack.Name != "Unknown" && this._currentTrack.Name != "Not Running")
            {
                this._overlay?.Draw(e.SpriteBatch, this._currentTrack);
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
    }
}
