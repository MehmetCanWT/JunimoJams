# Spotify Stardew

A Stardew Valley mod that adds a HUD overlay showing your currently playing Spotify track, including album art and playback controls.

## Features
- **HUD Overlay**: Shows track name, artist, and album art.
- **Playback Controls**: Skip, previous, and play/pause directly from the game.
- **Album Art**: High-quality cover art fetched automatically from iTunes API.
- **Customizable**: Change HUD position, scale, theme, and more via Generic Mod Config Menu.
- **Cross-Platform**: Supports Windows, macOS, and Linux.

## Requirements
- **SMAPI**: Stardew Modding API.
- **Generic Mod Config Menu**: (Recommended) For easy in-game settings.
- **Spotify**: Must be running on the same machine.

## Installation
1. Install [SMAPI](https://smapi.io/).
2. Download the mod and extract it into your `Mods` folder.
3. Ensure the `assets/default/` folder contains:
   - `background.png` (The HUD box)
   - `buttons.png` (The playback icons)
4. Launch the game!

## Development & Building
This project is built using the modern `.NET SDK` style.
1. Open the solution in Visual Studio or VS Code.
2. Ensure you have the `Pathoschild.Stardew.ModBuildConfig` NuGet package installed.
3. If the build fails because of a missing game folder, add your game path to the `.csproj` file:
   ```xml
   <PropertyGroup>
     <GamePath>C:\Path\To\Stardew Valley</GamePath>
   </PropertyGroup>
   ```

## Optimization & Fixes
The code has been optimized for:
- **Memory Management**: Automatic disposal of album art textures to prevent RAM leaks.
- **CPU Performance**: Caching of UI layout and measurements to minimize per-frame overhead.
- **Async Logic**: Background fetching of album art to prevent game stutters.
