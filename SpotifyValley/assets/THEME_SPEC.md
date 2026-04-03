# Spotify Valley — Theme Asset Specification

This file defines the **exact required dimensions** for all PNG assets in a theme folder.
When generating new theme images with AI (e.g. Nano Banana), use these specs.

---

## Folder Structure

```
assets/
  my_theme_name/
    background.png
    btn_play.png
    btn_pause.png
    btn_next.png
    btn_prev.png
```

---

## Asset Dimensions

### `background.png` — Main HUD Frame
| Property      | Value              |
|---------------|--------------------|
| Width         | **400 px**         |
| Height        | **150 px**         |
| Aspect ratio  | Horizontal (wide)  |
| Shape         | Rectangle (NOT square) |
| Background    | Pitch black `#000000` outside the frame |

> The code stretches this image to fit the HUD box dynamically.
> Keep it wide and short. Decoration (vines, flowers, etc.) should be on the **edges only** — the center must be visually open/empty for text + album art.

---

### `btn_play.png`, `btn_pause.png`, `btn_next.png`, `btn_prev.png` — Playback Buttons
| Property      | Value              |
|---------------|--------------------|
| Width         | **128 px**         |
| Height        | **128 px**         |
| Shape         | **Perfect square** |
| Background    | Pitch black `#000000` outside the button |

> **All 4 buttons MUST be the same pixel dimensions.**
> The icon (play/pause/next/prev) must be placed **exactly centered** inside the button.
> The button style (wood, stone, leaves, etc.) must be **identical** across all 4 files — only the icon in the center changes.

---

## Color Notes for AI Generation
- Exterior background: **pure `#000000`** (the mod auto-removes it via flood-fill)
- Frame / button base color: should match the theme's aesthetic
- Icon color: dark, high contrast against the button background

---

## Example Prompt Template (for AI image generation)

### background.png
```
Stardew valley pixel art UI element. A [THEME DESCRIPTION] rectangular horizontal wooden menu box frame, 
decorated with [DECORATION]. The frame must be wide and short (landscape orientation). 
Pitch black #000000 background outside the frame. The center of the frame must be empty/open. 
400x150 pixels. 2d game asset, pixel art style.
```

### btn_*.png (use same base, change only the icon)
```
Stardew valley pixel art UI element. A perfectly square [THEME DESCRIPTION] wooden button, 
[DECORATION]. In the exact center, a dark [ICON NAME] icon ([ICON DESCRIPTION]). 
Pitch black #000000 background outside the button. The button must be perfectly centered 
and fill the majority of the 128x128 image. 2d pixel art style.
```

Icons:
- `btn_play.png`  → PLAY icon (right-pointing triangle ▶)
- `btn_pause.png` → PAUSE icon (two vertical bars ⏸)
- `btn_next.png`  → NEXT TRACK icon (two right-pointing triangles ⏭)
- `btn_prev.png`  → PREV TRACK icon (two left-pointing triangles ⏮)
