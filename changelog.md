# 🌿 Junimo Jams — v5.2.0 Changelog
> **Latest Release & Improvements**

---

### 🎶 Universal YouTube Music Support
* **Browser & Desktop Support**
  * Out-of-the-box support for browser-based YouTube Music (including Chrome, Safari, Edge, Brave, Arc, Firefox, Helium, and more).
  * Native desktop app integration.
* **Real-Time Tracking**
  * Built using custom native hooks: Windows `EnumWindows`, macOS AppleScript, and Linux MPRIS2.
  * Song transitions are detected instantly without becoming stuck in browser window title caches.

---

### 🎵 Massive Player Expansion
* **New Built-in Players**
  * Added plug-and-play support for **TIDAL**, **Deezer**, **Cider**, **Plexamp**, **foobar2000**, **MusicBee**, **AIMP**, **Winamp**, **MediaMonkey**, **VLC**, **Strawberry**, and **Rhythmbox**.
* **Custom Process Monitoring**
  * Easily track any unlisted media player by adding its process name directly to the `ExtraPlayers` setting in your `config.json`.

---

### 🗺️ Smart Map Hiding
* **HUD Auto-Hiding**
  * The HUD now automatically hides itself whenever you open the world map (`M` key) or any non-inventory menu.
  * Fully compatible with **World Atlas** and other UI-overlay mods.

---

### ✨ Advanced Album Art Engine
* **Jaccard Token Similarity**
  * Browser playing states (like raw YouTube Music window titles) are parsed and queried against the iTunes API.
  * Results are scored using a smart **Jaccard word-token matching algorithm** to resolve exact artist names and download high-quality **600x600px cover art**.
* **Smart Local Caching**
  * Downloaded covers and resolved titles are cached inside the `cache/` folder (as `.png` and `.txt` files) for instant loading during replay with zero network overhead.

---

### 🎨 Visual & Polish Upgrades
* **Transitions** — Added a cozy, smooth fade-in animation for album art and text when a new track starts.
* **Drop-Shadows** — Subtle drop shadows applied to text labels to guarantee readability on bright custom themes.
* **Button Hover Glow** — Playback controls respond with an interactive, glowing visual feedback.

---

### 🔠 Unicode normalisation (Character Fixer)
* **Font Normalization**
  * Converts special Turkish (`ı`, `ş`, `ğ`, `ç`, `ö`, `ü`), Cyrillic, and international characters.
  * Fixes empty square glyphs ("box character") by mapping them to legible font counterparts.

---

### 🐛 Bug Fixes & Under-the-Hood Improvements
* **Performance:** Prevented duplicate playback status queries to reduce resource usage and optimize performance.
* **Stability:** Resolved feature disparities across platforms (Windows, macOS, Linux).
