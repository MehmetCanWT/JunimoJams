# 🌿 Spotify Valley

A premium **Stardew Valley** mod that adds a beautiful, modern HUD overlay showing your currently playing Spotify track. Features high-quality album art fetching and integrated playback controls.

![Spotify Valley HUD](https://raw.githubusercontent.com/MehmetCanWT/SpotifyValley/main/assets/preview.png) *(Preview placeholder)*

## ✨ Features
- 🎵 **Real-time HUD**: Shows song title, artist, and progress.
- 🖼️ **Album Art**: Automatically fetches high-resolution covers via iTunes API.
- 🎮 **Integrated Controls**: Play/Pause, Next, and Previous track buttons directly in your game HUD.
- 🎨 **Theming System**: Support for custom themes in the `assets/` folder.
- ⚙️ **Fully Customizable**: Adjust position, scale, and visibility via **Generic Mod Config Menu**.
- 🐧 **Cross-Platform**: Full support for Windows, macOS, and Linux.

## 📥 Installation
1. Install the latest version of [SMAPI](https://smapi.io/).
2. Download **Spotify Valley** and extract it into your `Mods` folder.
3. (Optional but Recommended) Install [Generic Mod Config Menu](https://www.nexusmods.com/stardewvalley/mods/5098).
4. Launch the game and enjoy your music!

## 🛠️ Configuration
You can configure the mod in-game using Generic Mod Config Menu or by editing the `config.json` file:
- `ShowHud`: Toggle the entire overlay.
- `HudPositionX` / `HudPositionY`: Move the HUD anywhere on your screen.
- `HudScale`: Make it as small or large as you like.
- `ShowAlbumArt`: Toggle cover art visibility.
- `Theme`: Choose between included themes (default, sleet, etc.).

## ⌨️ Development
Built with **C#** and **SMAPI SDK**.

### Build Instructions
1. Clone the repository.
2. Open `SpotifyValley.sln` in Visual Studio or VS Code.
3. Ensure the project references your Stardew Valley game path (automatic with SMAPI SDK).
4. Build the solution to generate the `bin/` folder.

## 📜 Credits
- Created by **MehmetCanWT**
- Powered by **SMAPI**
- Icons and HUD design by **Antigravity AI**

---
*Stardew Valley is a trademark of ConcernedApe LLC. This mod is not affiliated with ConcernedApe or Spotify.*
