namespace SpotifyValley
{
    public class ModConfig
    {
        public bool ShowHud { get; set; } = true;
        public int HudPositionX { get; set; } = 20;
        public int HudPositionY { get; set; } = 20;
        public float HudScale { get; set; } = 1f;
        public bool OnlyShowInInventory { get; set; } = false;
        public string Theme { get; set; } = "default";
        public bool ShowAlbumArt { get; set; } = true;
        public bool ShowPlaybackButtons { get; set; } = true;

        /// <summary>
        /// Additional music player process names to detect.
        /// Built-in: Spotify, iTunes, AppleMusic, Amazon Music, Cider.
        /// Add your own custom player here (e.g. "Tidal", "Deezer", "foobar2000").
        /// </summary>
        public string[] ExtraPlayers { get; set; } = System.Array.Empty<string>();
    }
}
