# 📚 Junimo Jams (RustedUI Edition) — Comprehensive Engineering Research

Welcome to the technical research and architecture index for **Junimo Jams (RustedUI Edition)**. This repository houses in-depth specifications, platform benchmarks, IPC protocols, and graphics engineering studies conducted by the specialized agent research army.

---

## 📑 Research Divisions & Specifications Index

| Specification | Division & Topic | Focus Areas | Target File |
|---|---|---|---|
| **[01](./01-linux-mpris2-dbus-pipewire-media-control.md)** | **Linux Media IPC Architecture** | D-Bus MPRIS2 protocol, real-time `PropertiesChanged` event subscriptions, eliminating polling, PipeWire/PulseAudio sink metadata, and Flatpak/Snap sandbox traversal. | `01-linux-mpris2-dbus-pipewire-media-control.md` |
| **[02](./02-windows-gsmtc-winrt-and-win32-media-apis.md)** | **Windows Media Systems** | Windows 10/11 GSMTC (Global System Media Transport Controls) WinRT API, native thumbnail stream extraction, Win32 `EnumWindows` & `SendInput` heuristics, Chromium title cache mitigation. | `02-windows-gsmtc-winrt-and-win32-media-apis.md` |
| **[03](./03-macos-applescript-quartz-and-nowplaying-apis.md)** | **macOS CoreMedia & Interop** | AppleScript/JXA execution optimization without process spawns, private `MediaRemote.framework` reverse engineering, Quartz `CGEvent` media keys, safe multi-browser tab title querying. | `03-macos-applescript-quartz-and-nowplaying-apis.md` |
| **[04](./04-proton-wine-steam-sandbox-and-steam-deck.md)** | **Steam Deck, Proton & Container Sandbox** | Linux native vs Proton execution, Wine D-Bus bridging (`winedbus.sys`, Unix socket proxying), Steam Linux Runtime (`pressure-vessel` bubblewrap) permissions, Steam Deck 1280x800 HUD ergonomics, and suspend/resume resilience. | `04-proton-wine-steam-sandbox-and-steam-deck.md` |
| **[05](./05-music-metadata-similarity-and-cover-art-services.md)** | **Music Information Retrieval (MIR)** | iTunes Search API, MusicBrainz / Cover Art Archive, Spotify Web API, advanced regex title cleaning, string similarity algorithms (Token Jaccard, Levenshtein, Jaro-Winkler), and multi-tier disk/RAM LRU caching. | `05-music-metadata-similarity-and-cover-art-services.md` |
| **[06](./06-gpu-texture-streaming-and-rusted-ui-enhancements.md)** | **Graphics, MonoGame & RustedUI UX** | Thread-safe `Texture2D` upload without rendering deadlocks, high-resolution downsampling and mipmap anti-aliasing, rounded corner masking, marquee text auto-scrolling, volume/progress scrubbing, and simulated audio visualizers. | `06-gpu-texture-streaming-and-rusted-ui-enhancements.md` |

---

## 🎯 Architectural Goals

1. **True Zero-GC Runtime**: Ensure that during normal music playback and UI rendering, zero managed heap allocations occur per frame.
2. **Event-Driven Over Polling**: Replace 60-frame polling loops with operating system event listeners (D-Bus `PropertiesChanged` on Linux, GSMTC `MediaPropertiesChanged` on Windows) for near-instant (<1ms) state synchronization with 0% idle CPU footprint.
3. **Hardware Sandbox Resilience**: Operate seamlessly whether Stardew Valley is run natively on Linux, on Steam Deck (Gaming Mode), inside Flatpak Steam, or under Proton/Wine.
4. **Instant Hot-Reloadable Ergonomics**: Preserve RustedUI's live `<20ms` HTML/XML hot-reloading for UI designers while providing type-safe GPUI C# Fluent builders for modders.
