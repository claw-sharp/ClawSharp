from pathlib import Path
from collections import deque

from PIL import Image


REPO_ROOT = Path(__file__).resolve().parents[1]
SOURCE_PATH = REPO_ROOT / "assets" / "logos" / "app-logo.png"
PUBLIC_DIR = REPO_ROOT / "apps" / "desktop" / "public"
ICONS_DIR = REPO_ROOT / "apps" / "desktop" / "src-tauri" / "icons"


def extract_mark(image: Image.Image) -> Image.Image:
    def is_background(r: int, g: int, b: int) -> bool:
        channel_span = max(r, g, b) - min(r, g, b)
        return channel_span <= 32 and min(r, g, b) >= 160

    width, height = image.size
    source_pixels = image.load()
    background: set[tuple[int, int]] = set()
    queue: deque[tuple[int, int]] = deque()

    for x in range(width):
        queue.append((x, 0))
        queue.append((x, height - 1))
    for y in range(height):
        queue.append((0, y))
        queue.append((width - 1, y))

    while queue:
        x, y = queue.popleft()
        if (x, y) in background or not (0 <= x < width and 0 <= y < height):
            continue

        r, g, b, _ = source_pixels[x, y]
        if not is_background(r, g, b):
            continue

        background.add((x, y))
        queue.extend(((x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1)))

    masked = Image.new("RGBA", image.size)
    masked_pixels = []
    for y in range(height):
        for x in range(width):
            r, g, b, _ = source_pixels[x, y]
            masked_pixels.append((r, g, b, 0 if (x, y) in background else 255))
    masked.putdata(masked_pixels)
    bbox = masked.getbbox()
    if bbox is None:
        raise RuntimeError("Failed to isolate icon from app-logo.png")

    return masked.crop(bbox)


def square_icon(mark: Image.Image, size: int, inset: float = 0.90) -> Image.Image:
    canvas = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    scale = min(size / mark.width, size / mark.height) * inset
    target_size = (
        max(1, round(mark.width * scale)),
        max(1, round(mark.height * scale)),
    )
    resized = mark.resize(target_size, Image.Resampling.LANCZOS)
    x = (size - target_size[0]) // 2
    y = (size - target_size[1]) // 2
    canvas.alpha_composite(resized, (x, y))
    return canvas


def main() -> None:
    source = Image.open(SOURCE_PATH).convert("RGBA")
    mark = extract_mark(source)

    for name in ("clawsharp-mark-light.png", "clawsharp-mark-dark.png"):
        mark.save(PUBLIC_DIR / name)

    png_targets = {
        ICONS_DIR / "32x32.png": 32,
        ICONS_DIR / "64x64.png": 64,
        ICONS_DIR / "128x128.png": 128,
        ICONS_DIR / "128x128@2x.png": 256,
        ICONS_DIR / "icon.png": 512,
        ICONS_DIR / "Square30x30Logo.png": 30,
        ICONS_DIR / "Square44x44Logo.png": 44,
        ICONS_DIR / "Square71x71Logo.png": 71,
        ICONS_DIR / "Square89x89Logo.png": 89,
        ICONS_DIR / "Square107x107Logo.png": 107,
        ICONS_DIR / "Square142x142Logo.png": 142,
        ICONS_DIR / "Square150x150Logo.png": 150,
        ICONS_DIR / "Square284x284Logo.png": 284,
        ICONS_DIR / "Square310x310Logo.png": 310,
        ICONS_DIR / "StoreLogo.png": 50,
        ICONS_DIR / "android" / "mipmap-mdpi" / "ic_launcher.png": 48,
        ICONS_DIR / "android" / "mipmap-mdpi" / "ic_launcher_round.png": 48,
        ICONS_DIR / "android" / "mipmap-mdpi" / "ic_launcher_foreground.png": 108,
        ICONS_DIR / "android" / "mipmap-hdpi" / "ic_launcher.png": 72,
        ICONS_DIR / "android" / "mipmap-hdpi" / "ic_launcher_round.png": 72,
        ICONS_DIR / "android" / "mipmap-hdpi" / "ic_launcher_foreground.png": 162,
        ICONS_DIR / "android" / "mipmap-xhdpi" / "ic_launcher.png": 96,
        ICONS_DIR / "android" / "mipmap-xhdpi" / "ic_launcher_round.png": 96,
        ICONS_DIR / "android" / "mipmap-xhdpi" / "ic_launcher_foreground.png": 216,
        ICONS_DIR / "android" / "mipmap-xxhdpi" / "ic_launcher.png": 144,
        ICONS_DIR / "android" / "mipmap-xxhdpi" / "ic_launcher_round.png": 144,
        ICONS_DIR / "android" / "mipmap-xxhdpi" / "ic_launcher_foreground.png": 324,
        ICONS_DIR / "android" / "mipmap-xxxhdpi" / "ic_launcher.png": 192,
        ICONS_DIR / "android" / "mipmap-xxxhdpi" / "ic_launcher_round.png": 192,
        ICONS_DIR / "android" / "mipmap-xxxhdpi" / "ic_launcher_foreground.png": 432,
        ICONS_DIR / "ios" / "AppIcon-20x20@1x.png": 20,
        ICONS_DIR / "ios" / "AppIcon-20x20@2x.png": 40,
        ICONS_DIR / "ios" / "AppIcon-20x20@2x-1.png": 40,
        ICONS_DIR / "ios" / "AppIcon-20x20@3x.png": 60,
        ICONS_DIR / "ios" / "AppIcon-29x29@1x.png": 29,
        ICONS_DIR / "ios" / "AppIcon-29x29@2x.png": 58,
        ICONS_DIR / "ios" / "AppIcon-29x29@2x-1.png": 58,
        ICONS_DIR / "ios" / "AppIcon-29x29@3x.png": 87,
        ICONS_DIR / "ios" / "AppIcon-40x40@1x.png": 40,
        ICONS_DIR / "ios" / "AppIcon-40x40@2x.png": 80,
        ICONS_DIR / "ios" / "AppIcon-40x40@2x-1.png": 80,
        ICONS_DIR / "ios" / "AppIcon-40x40@3x.png": 120,
        ICONS_DIR / "ios" / "AppIcon-60x60@2x.png": 120,
        ICONS_DIR / "ios" / "AppIcon-60x60@3x.png": 180,
        ICONS_DIR / "ios" / "AppIcon-76x76@1x.png": 76,
        ICONS_DIR / "ios" / "AppIcon-76x76@2x.png": 152,
        ICONS_DIR / "ios" / "AppIcon-83.5x83.5@2x.png": 167,
        ICONS_DIR / "ios" / "AppIcon-512@2x.png": 1024,
    }

    cache: dict[int, Image.Image] = {}
    for path, size in png_targets.items():
        if size not in cache:
            cache[size] = square_icon(mark, size)
        path.parent.mkdir(parents=True, exist_ok=True)
        cache[size].save(path)

    favicon_sizes = [(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)]
    favicon_base = square_icon(mark, 256)
    favicon_base.save(PUBLIC_DIR / "favicon.ico", format="ICO", sizes=favicon_sizes)
    favicon_base.save(ICONS_DIR / "icon.ico", format="ICO", sizes=favicon_sizes)

    icns_base = square_icon(mark, 1024)
    icns_base.save(ICONS_DIR / "icon.icns", format="ICNS")

    print(f"Desktop icons regenerated from {SOURCE_PATH}")


if __name__ == "__main__":
    main()
