from playwright.sync_api import sync_playwright
from pathlib import Path

out = Path("src/StatusTracker/Assets")
out.mkdir(parents=True, exist_ok=True)

jobs = [
    ("picture/app-icon.svg", "app-icon.png", 256),
    ("picture/tray-normal.svg", "tray-normal.png", 64),
    ("picture/tray-error.svg", "tray-error.png", 64),
]

with sync_playwright() as p:
    browser = p.chromium.launch(headless=True)
    page = browser.new_page()
    for src, name, size in jobs:
        svg = Path(src).read_text(encoding="utf-8")
        html = f"<html><body style='margin:0'><div id='r'>{svg}</div></body></html>"
        page.set_content(html)
        page.set_viewport_size({"width": size, "height": size})
        page.locator("#r svg").first.evaluate(
            f"el => {{ el.setAttribute('width', '{size}'); el.setAttribute('height', '{size}'); }}"
        )
        page.locator("#r svg").first.screenshot(path=str(out / name))
        print("wrote", name)
    browser.close()

# MSIX tile placeholders from app icon
from PIL import Image

base = Image.open(out / "app-icon.png").convert("RGBA")
for w, h, name in [
    (44, 44, "Square44x44Logo.png"),
    (150, 150, "Square150x150Logo.png"),
    (310, 150, "Wide310x150Logo.png"),
    (620, 300, "SplashScreen.png"),
    (50, 50, "StoreLogo.png"),
]:
    img = base.resize((w, h), Image.Resampling.LANCZOS)
    img.save(out / name)
    print("wrote", name)

# ICO for window/exe
ico_sizes = [(16, 16), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)]
base.save(out / "app.ico", format="ICO", sizes=ico_sizes)
print("wrote app.ico")

# tray ico (normal/error) for notify icon
for src_name in ("tray-normal.png", "tray-error.png"):
    t = Image.open(out / src_name).convert("RGBA")
    t.save(out / src_name.replace(".png", ".ico"), format="ICO", sizes=[(16, 16), (32, 32), (48, 48)])
    print("wrote", src_name.replace(".png", ".ico"))

print("assets done")
