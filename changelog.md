# 🌿 Junimo Jams — v5.2.0 Release Notes / Changelog

### 🎶 Universal YouTube Music Support
* **Browser & Desktop Support:** Out-of-the-box support for both browser-based YouTube Music (Chrome, Safari, Edge, Brave, Arc, Firefox, Helium, etc.) and the official Desktop App.
* **Real-Time Tracking:** Custom platform-specific integrations (Windows `EnumWindows`, macOS AppleScript, and Linux MPRIS2) ensure song changes are detected in real-time without getting stuck in browser title caches.

### 🎵 Massive Player Expansion
* **Expanded Player Integration:** Built-in support for all popular media players: **TIDAL, Deezer, Cider, Plexamp, foobar2000, MusicBee, AIMP, Winamp, MediaMonkey, VLC, Strawberry, Rhythmbox**.
* **Custom Players (ExtraPlayers):** Easily track any unlisted media player by adding its process name to the `ExtraPlayers` list in `config.json`.

### 🗺️ Smart Map Hiding
* **Map Compatibility:** The HUD now automatically hides itself whenever you open the map (`M` key) or any non-inventory menu. Fully compatible with *World Atlas* and other map-altering mods.

### ✨ Advanced Album Art Engine
* **Jaccard Token Similarity:** Generic or browser playing states (like raw YouTube Music titles lacking artist details) are parsed and queried against the iTunes API. Results are scored using Jaccard word token matching to resolve correct artist names and fetch 600x600 high-res cover art.
* **Smart Local Cache:** Downloaded covers and resolved artist names are cached locally in the `cache/` folder (as `.png` and `.txt` files) for instant loading on subsequent plays with zero network overhead.

### 🎨 Visual & Polish Upgrades
* **Smooth Transitions:** Added a cozy, smooth fade-in animation for album art and text whenever a new track starts playing.
* **Text Shadows:** Drop-shadow styling applied to text labels ensures perfect readability even on custom, bright theme backgrounds.
* **Interactive Button Glow:** Playback control buttons glow on mouse hover.

### 🔠 Font Normalization
* **Character Fixer:** Normalizes Turkish (`ı`, `ş`, `ğ`, `ç`, `ö`, `ü`), Cyrillic, and other special characters to prevent empty boxes (missing glyphs) on standard game fonts.

### 🐛 Bug Fixes & Performance
* Prevented duplicate playback status queries to reduce resource usage and optimize performance.
* Resolved feature disparities across platforms (Windows, macOS, Linux).
