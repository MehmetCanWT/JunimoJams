using System;
using StardewModdingAPI.Utilities;

namespace JunimoJamsRusted
{
    public sealed class ModConfig
    {
        public bool ShowHud { get; set; } = true;
        public bool EnableUiSounds { get; set; } = true;

        /// <summary>
        /// Active theme. Choose from RustedUI's 10 themes:
        /// "JunimoForest", "PelicanClassic", "ShadcnZincDark", "WizardArcane", "JojaCorp",
        /// "CatppuccinMocha", "TokyoNight", "Dracula", "Nord", "Cyberpunk", or "FollowGlobal".
        /// </summary>
        public string Theme { get; set; } = "JunimoForest";

        public int HudPositionX { get; set; } = 20;
        public int HudPositionY { get; set; } = 20;

        public bool ShowAlbumArt { get; set; } = true;
        public bool ShowPlaybackButtons { get; set; } = true;
        public bool ShowProgressBar { get; set; } = true;
        public bool EnableMarqueeText { get; set; } = true;

        public bool OnlyShowInInventory { get; set; } = false;
        public bool HideOnMap { get; set; } = true;

        /// <summary>
        /// Keybind to toggle the Junimo Jams Music Menu / Hub.
        /// </summary>
        public KeybindList OpenMenuKey { get; set; } = KeybindList.Parse("F6");

        /// <summary>
        /// Additional music player process names to detect.
        /// Built-in: Spotify, iTunes, AppleMusic, Amazon Music, Cider, TIDAL,
        /// Deezer, Plexamp, foobar2000, MusicBee, AIMP, Winamp, VLC, etc.
        /// </summary>
        public string[] ExtraPlayers { get; set; } = Array.Empty<string>();
    }
}
