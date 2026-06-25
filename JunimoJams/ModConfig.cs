namespace JunimoJams
{
    public class ModConfig
    {
        public bool ShowHud { get; set; } = true;
        public int HudPositionX { get; set; } = 20;
        public int HudPositionY { get; set; } = 20;
        public float HudScale { get; set; } = 1f;
        public bool OnlyShowInInventory { get; set; } = false;
        public string Theme { get; set; } = "overgrown_grass_flowery";
        public bool ShowAlbumArt { get; set; } = true;
        public bool ShowPlaybackButtons { get; set; } = true;

        /// <summary>
        /// Hide the HUD when a non-inventory menu (like World Atlas map) is open.
        /// Prevents the music player from blocking map and overlay content.
        /// </summary>
        public bool HideOnMap { get; set; } = true;

        /// <summary>
        /// Additional music player process names to detect.
        /// Built-in: Spotify, iTunes, AppleMusic, Amazon Music, Cider, TIDAL,
        /// Deezer, Plexamp, foobar2000, MusicBee, AIMP, Winamp, MediaMonkey.
        /// Add your own custom player here (e.g. "VLC", "Strawberry").
        /// </summary>
        public string[] ExtraPlayers { get; set; } = System.Array.Empty<string>();
    }
}
