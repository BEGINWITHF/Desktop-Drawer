from __future__ import annotations

import math
import os

from PIL import Image, ImageDraw, ImageFilter

SIZE = 2048
OUT_DIR = os.path.join(os.path.dirname(__file__), "..", "src", "DesktopDrawer", "Assets")


def rounded_mask(size: tuple[int, int], radius: int) -> Image.Image:
    mask = Image.new("L", size, 0)
    draw = ImageDraw.Draw(mask)
    draw.rounded_rectangle((0, 0, size[0] - 1, size[1] - 1), radius=radius, fill=255)
    return mask


def vertical_gradient(size: tuple[int, int], top: tuple[int, int, int], bottom: tuple[int, int, int]) -> Image.Image:
    image = Image.new("RGB", size)
    draw = ImageDraw.Draw(image)
    for y in range(size[1]):
        t = y / max(1, size[1] - 1)
        color = tuple(round(top[i] + (bottom[i] - top[i]) * t) for i in range(3))
        draw.line((0, y, size[0], y), fill=color)
    return image


def rounded_card(size: tuple[int, int], radius: int, color: tuple[int, int, int, int]) -> Image.Image:
    card = Image.new("RGBA", size, (0, 0, 0, 0))
    mask = rounded_mask(size, radius)
    fill = Image.new("RGBA", size, color)
    card.paste(fill, (0, 0), mask)
    return card


def paste_rotated(base: Image.Image, layer: Image.Image, center: tuple[int, int], angle: float) -> None:
    rotated = layer.rotate(angle, resample=Image.BICUBIC, expand=True)
    x = int(center[0] - rotated.width / 2)
    y = int(center[1] - rotated.height / 2)
    base.alpha_composite(rotated, (x, y))


def main() -> None:
    os.makedirs(OUT_DIR, exist_ok=True)

    canvas = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    background = vertical_gradient((SIZE, SIZE), (40, 49, 82), (10, 13, 23)).convert("RGBA")
    canvas.paste(background, (0, 0), rounded_mask((SIZE, SIZE), 440))

    # Soft glow behind the stack.
    glow = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    glow_draw = ImageDraw.Draw(glow)
    glow_draw.ellipse((420, 380, 1640, 1600), fill=(124, 156, 255, 70))
    glow = glow.filter(ImageFilter.GaussianBlur(180))
    canvas.alpha_composite(glow)

    card_size = (920, 1120)
    card_radius = 120

    shadow = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    shadow_draw = ImageDraw.Draw(shadow)
    shadow_draw.rounded_rectangle((560, 500, 1490, 1610), radius=card_radius, fill=(0, 0, 0, 150))
    shadow = shadow.filter(ImageFilter.GaussianBlur(55))
    canvas.alpha_composite(shadow)

    back = rounded_card(card_size, card_radius, (64, 83, 146, 235))
    middle = rounded_card(card_size, card_radius, (105, 133, 218, 245))

    front = rounded_card(card_size, card_radius, (246, 249, 255, 255))
    front_draw = ImageDraw.Draw(front)
    accent = (100, 132, 235, 255)
    soft = (214, 226, 255, 255)

    # Page dots.
    dot = 150
    gap = 78
    start_x = (card_size[0] - (dot * 2 + gap)) // 2
    start_y = 300
    for row in range(2):
        for column in range(2):
            x = start_x + column * (dot + gap)
            y = start_y + row * (dot + gap)
            front_draw.rounded_rectangle((x, y, x + dot, y + dot), radius=46, fill=accent)

    # Subtle text lines.
    front_draw.rounded_rectangle((300, 800, 620, 850), radius=25, fill=soft)
    front_draw.rounded_rectangle((300, 900, 540, 950), radius=25, fill=soft)

    # Drawer handle.
    front_draw.rounded_rectangle((390, 1030, 530, 1060), radius=15, fill=(150, 172, 238, 255))

    paste_rotated(canvas, back, (840, 980), 9)
    paste_rotated(canvas, middle, (950, 1000), -7)
    paste_rotated(canvas, front, (1080, 1030), 2)

    highlight = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    highlight_draw = ImageDraw.Draw(highlight)
    highlight_draw.arc((60, 40, SIZE - 60, SIZE - 60), 180, 330, fill=(255, 255, 255, 70), width=10)
    canvas.alpha_composite(highlight)

    canvas = canvas.resize((1024, 1024), Image.LANCZOS)

    png_path = os.path.join(OUT_DIR, "app.png")
    canvas.save(png_path, "PNG")

    ico_path = os.path.join(OUT_DIR, "app.ico")
    canvas.save(
        ico_path,
        format="ICO",
        sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)],
    )

    print(f"Wrote {png_path}")
    print(f"Wrote {ico_path}")


if __name__ == "__main__":
    main()
