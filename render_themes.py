import os
import subprocess
import base64
from PIL import Image

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
THEMES_DIR = os.path.join(SCRIPT_DIR, "docs", "themes")
ARTIFACT_DIR = "/home/mehmetcan/.gemini/antigravity-cli/brain/cf252247-d775-43a9-a323-a805bff052cb"
LOGO_PATH = os.path.join(SCRIPT_DIR, "JunimoJams", "assets", "logo.png")

os.makedirs(THEMES_DIR, exist_ok=True)

with open(LOGO_PATH, "rb") as f:
    logo_b64 = "data:image/png;base64," + base64.b64encode(f.read()).decode("ascii")

THEMES = [
    {
        "id": "junimo_forest",
        "name": "Junimo Forest",
        "bg": "#14261C",
        "border": "#4A7C59",
        "border_highlight": "#86EFAC",
        "title": "#FDE047",
        "artist": "#D1D5DB",
        "time": "#86EFAC",
        "fill": "#86EFAC",
        "track": "rgba(0,0,0,0.45)",
        "icons": "#E4E4E7",
        "highlight": True
    },
    {
        "id": "pelican_classic",
        "name": "Pelican Classic",
        "bg": "#FEE7B6",
        "border": "#8B4513",
        "border_highlight": "#CD853F",
        "title": "#4A2810",
        "artist": "#3C1E0A",
        "time": "#8B4513",
        "fill": "#E68A00",
        "track": "rgba(0,0,0,0.18)",
        "icons": "#4A2810",
        "highlight": False
    },
    {
        "id": "shadcn_zinc_dark",
        "name": "Shadcn Zinc Dark",
        "bg": "#09090B",
        "border": "#27272A",
        "border_highlight": "#3F3F46",
        "title": "#FAFAFA",
        "artist": "#A1A1AA",
        "time": "#71717A",
        "fill": "#FAFAFA",
        "track": "rgba(255,255,255,0.12)",
        "icons": "#A1A1AA",
        "highlight": False
    },
    {
        "id": "cyberpunk",
        "name": "Cyberpunk Neon",
        "bg": "#0A0616",
        "border": "#00F0FF",
        "border_highlight": "#FCEE0A",
        "title": "#FCEE0A",
        "artist": "#E4E4E7",
        "time": "#00F0FF",
        "fill": "#00F0FF",
        "track": "rgba(0,0,0,0.5)",
        "icons": "#00F0FF",
        "highlight": True
    },
    {
        "id": "wizard_arcane",
        "name": "Wizard Arcane",
        "bg": "#0F081D",
        "border": "#A855F7",
        "border_highlight": "#F59E0B",
        "title": "#F59E0B",
        "artist": "#E9D5FF",
        "time": "#A855F7",
        "fill": "#A855F7",
        "track": "rgba(0,0,0,0.45)",
        "icons": "#E9D5FF",
        "highlight": False
    },
    {
        "id": "joja_corp",
        "name": "Joja Corp",
        "bg": "#0F172A",
        "border": "#38BDF8",
        "border_highlight": "#0284C7",
        "title": "#38BDF8",
        "artist": "#F1F5F9",
        "time": "#94A3B8",
        "fill": "#38BDF8",
        "track": "rgba(0,0,0,0.45)",
        "icons": "#F1F5F9",
        "highlight": False
    },
    {
        "id": "catppuccin_mocha",
        "name": "Catppuccin Mocha",
        "bg": "#1E1E2E",
        "border": "#CBA6F7",
        "border_highlight": "#F5C2E7",
        "title": "#CBA6F7",
        "artist": "#CDD6F4",
        "time": "#89DCEB",
        "fill": "#CBA6F7",
        "track": "rgba(0,0,0,0.45)",
        "icons": "#CDD6F4",
        "highlight": False
    },
    {
        "id": "tokyo_night",
        "name": "Tokyo Night",
        "bg": "#1A1B26",
        "border": "#7AA2F7",
        "border_highlight": "#BB9AF7",
        "title": "#7AA2F7",
        "artist": "#C0CAF5",
        "time": "#7DCFFF",
        "fill": "#7AA2F7",
        "track": "rgba(0,0,0,0.45)",
        "icons": "#C0CAF5",
        "highlight": False
    },
    {
        "id": "dracula",
        "name": "Dracula",
        "bg": "#282A36",
        "border": "#BD93F9",
        "border_highlight": "#FF79C6",
        "title": "#FF79C6",
        "artist": "#F8F8F2",
        "time": "#50FA7B",
        "fill": "#BD93F9",
        "track": "rgba(0,0,0,0.45)",
        "icons": "#F8F8F2",
        "highlight": False
    },
    {
        "id": "nord",
        "name": "Nord",
        "bg": "#2E3440",
        "border": "#88C0D0",
        "border_highlight": "#81A1C1",
        "title": "#88C0D0",
        "artist": "#ECEFF4",
        "time": "#EBCB8B",
        "fill": "#88C0D0",
        "track": "rgba(0,0,0,0.45)",
        "icons": "#ECEFF4",
        "highlight": False
    }
]

def make_hud_html(theme):
    top_bar = f'<div style="position: absolute; top: 0; left: 8px; right: 8px; height: 2px; background: {theme["border_highlight"]}; z-index: 5;"></div>' if theme["highlight"] else ''
    
    return f"""<!DOCTYPE html>
<html>
<head>
<meta charset="utf-8">
<style>
  @import url('https://fonts.googleapis.com/css2?family=Press+Start+2P&display=swap');
  * {{ box-sizing: border-box; margin: 0; padding: 0; }}
  body {{
    background: transparent;
    padding: 12px;
    display: inline-block;
  }}
  .card {{
    width: 320px;
    height: 82px;
    background: {theme["bg"]};
    border: 2px solid {theme["border"]};
    border-radius: 8px;
    position: relative;
    box-shadow: 4px 6px 14px rgba(0, 0, 0, 0.55);
    display: flex;
    flex-direction: row;
    align-items: center;
    padding: 8px;
    gap: 9px;
    overflow: hidden;
    font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, "Helvetica Neue", sans-serif;
  }}
  .art {{
    width: 64px;
    height: 64px;
    border-radius: 4px;
    background: #0B1711;
    overflow: hidden;
    flex-shrink: 0;
    display: flex;
    align-items: center;
    justify-content: center;
  }}
  .art img {{
    width: 100%;
    height: 100%;
    object-fit: cover;
  }}
  .content {{
    flex: 1;
    height: 64px;
    display: flex;
    flex-direction: column;
    justify-content: space-between;
    min-width: 0;
  }}
  .title-row {{
    font-size: 13px;
    font-weight: 700;
    color: {theme["title"]};
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
    text-shadow: 1px 1px 0 rgba(0,0,0,0.85);
    letter-spacing: -0.2px;
    line-height: 16px;
  }}
  .info-row {{
    display: flex;
    justify-content: space-between;
    align-items: center;
    font-size: 11px;
    font-weight: 600;
    line-height: 14px;
    margin-top: 1px;
  }}
  .artist-eq {{
    display: flex;
    align-items: center;
    gap: 4px;
    color: {theme["artist"]};
    text-shadow: 1px 1px 0 rgba(0,0,0,0.7);
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
  }}
  .eq-bars {{
    display: flex;
    align-items: flex-end;
    gap: 2px;
    height: 10px;
  }}
  .eq-bar {{
    width: 2px;
    background: {theme["fill"]};
    border-radius: 1px;
  }}
  .time {{
    color: {theme["time"]};
    text-shadow: 1px 1px 0 rgba(0,0,0,0.7);
    flex-shrink: 0;
    font-variant-numeric: tabular-nums;
  }}
  .bar-row {{
    width: 100%;
    height: 4px;
    background: {theme["track"]};
    border: 1px solid rgba(255,255,255,0.12);
    border-radius: 2px;
    overflow: hidden;
    position: relative;
    margin: 2px 0;
  }}
  .bar-fill {{
    width: 52%;
    height: 100%;
    background: {theme["fill"]};
  }}
  .controls-row {{
    display: flex;
    justify-content: center;
    align-items: center;
    gap: 24px;
    height: 18px;
  }}
  .ctrl-icon {{
    width: 16px;
    height: 16px;
    display: flex;
    align-items: center;
    justify-content: center;
    fill: {theme["icons"]};
    color: {theme["icons"]};
  }}
</style>
</head>
<body>
  <div class="card">
    {top_bar}
    <div class="art">
      <img src="{logo_b64}" alt="Cover Art">
    </div>
    <div class="content">
      <div class="title-row">Dance of the Moonlight Jellies</div>
      <div class="info-row">
        <div class="artist-eq">
          <div class="eq-bars">
            <div class="eq-bar" style="height: 6px;"></div>
            <div class="eq-bar" style="height: 10px;"></div>
            <div class="eq-bar" style="height: 7px;"></div>
          </div>
          <span>ConcernedApe</span>
        </div>
        <div class="time">1:24 / 3:15</div>
      </div>
      <div class="bar-row">
        <div class="bar-fill"></div>
      </div>
      <div class="controls-row">
        <!-- Prev Icon -->
        <svg class="ctrl-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round">
          <polygon points="19 20 9 12 19 4 19 20" fill="currentColor"></polygon>
          <line x1="5" y1="19" x2="5" y2="5"></line>
        </svg>
        <!-- Pause Icon -->
        <svg class="ctrl-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round">
          <rect x="6" y="4" width="4" height="16" fill="currentColor"></rect>
          <rect x="14" y="4" width="4" height="16" fill="currentColor"></rect>
        </svg>
        <!-- Next Icon -->
        <svg class="ctrl-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round">
          <polygon points="5 4 15 12 5 20 5 4" fill="currentColor"></polygon>
          <line x1="19" y1="5" x2="19" y2="19"></line>
        </svg>
      </div>
    </div>
  </div>
</body>
</html>
"""

def make_album_art_only_html():
    return f"""<!DOCTYPE html>
<html>
<head>
<meta charset="utf-8">
<style>
  * {{ box-sizing: border-box; margin: 0; padding: 0; }}
  body {{
    background: transparent;
    padding: 12px;
    display: inline-block;
  }}
  .widget {{
    width: 76px;
    height: 76px;
    background: #14261C;
    border: 2px solid #4A7C59;
    border-radius: 8px;
    position: relative;
    box-shadow: 3px 5px 12px rgba(0, 0, 0, 0.55);
    display: flex;
    align-items: center;
    justify-content: center;
    overflow: hidden;
  }}
  .top-highlight {{
    position: absolute;
    top: 0;
    left: 8px;
    right: 8px;
    height: 2px;
    background: #86EFAC;
    z-index: 5;
  }}
  .art {{
    width: 64px;
    height: 64px;
    border-radius: 4px;
    overflow: hidden;
    position: relative;
  }}
  .art img {{
    width: 100%;
    height: 100%;
    object-fit: cover;
  }}
  .eq-badge {{
    position: absolute;
    bottom: 2px;
    right: 2px;
    background: rgba(20, 38, 28, 0.88);
    border-radius: 3px;
    padding: 2px 3px;
    display: flex;
    align-items: flex-end;
    gap: 2px;
    height: 12px;
  }}
  .eq-bar {{
    width: 2px;
    background: #86EFAC;
    border-radius: 1px;
  }}
</style>
</head>
<body>
  <div class="widget">
    <div class="top-highlight"></div>
    <div class="art">
      <img src="{logo_b64}" alt="Cover Art">
      <div class="eq-badge">
        <div class="eq-bar" style="height: 5px;"></div>
        <div class="eq-bar" style="height: 9px;"></div>
        <div class="eq-bar" style="height: 6px;"></div>
      </div>
    </div>
  </div>
</body>
</html>
"""

def render_html_to_png(html_content, output_png_path, is_mini=False):
    tmp_html = "/tmp/render_theme.html"
    tmp_png = "/tmp/render_theme.png"
    with open(tmp_html, "w", encoding="utf-8") as f:
        f.write(html_content)
    
    w = 120 if is_mini else 360
    h = 120 if is_mini else 120
    cmd = [
        "firefox",
        "--headless",
        "--screenshot",
        tmp_png,
        f"--window-size={w},{h}",
        f"file://{tmp_html}"
    ]
    subprocess.run(cmd, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, check=True)
    
    # Clean transparent crop
    im = Image.open(tmp_png)
    # The card starts around (12, 12) with shadow
    # Find bounding box of non-transparent / content
    bbox = im.getbbox()
    if bbox:
        im = im.crop(bbox)
    
    im.save(output_png_path, "PNG")
    if os.path.exists(ARTIFACT_DIR):
        artifact_path = os.path.join(ARTIFACT_DIR, os.path.basename(output_png_path))
        im.save(artifact_path, "PNG")

print("Rendering all 10 HUD themes + OnlyAlbumArt mode...")
for t in THEMES:
    out_file = os.path.join(THEMES_DIR, f"{t['id']}.png")
    html = make_hud_html(t)
    render_html_to_png(html, out_file)
    print(f"  ✓ {t['name']} -> {out_file}")

# Render OnlyAlbumArt
out_mini = os.path.join(THEMES_DIR, "only_album_art.png")
render_html_to_png(make_album_art_only_html(), out_mini, is_mini=True)
print(f"  ✓ Only Album Art Widget -> {out_mini}")

print("All theme HUD renders completed successfully!")
