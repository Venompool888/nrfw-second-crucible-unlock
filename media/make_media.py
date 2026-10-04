"""Render original, text-only Nexus artwork for the mod page."""

from pathlib import Path
from PIL import Image, ImageDraw, ImageFont
import math


ROOT = Path(__file__).parent
FONT_DIR = Path("C:/Windows/Fonts")
FONT_BOLD = FONT_DIR / "segoeuib.ttf"
FONT_REGULAR = FONT_DIR / "segoeui.ttf"


def font(path: Path, size: int) -> ImageFont.FreeTypeFont:
    return ImageFont.truetype(str(path), size)


def render(width: int, height: int, output: str, compact: bool) -> None:
    im = Image.new("RGB", (width, height), (10, 15, 22))
    d = ImageDraw.Draw(im)
    for y in range(height):
        t = y / max(1, height - 1)
        d.line((0, y, width, y), fill=(int(10 + 7*t), int(15 + 4*t), int(22 + 4*t)))

    cx, cy = int(width * .77), int(height * .51)
    base = int(min(width, height) * (.34 if compact else .29))
    for i in range(9):
        r = base + i * int(min(width, height) * .039)
        alpha = 140 - i * 12
        color = (min(255, 82 + alpha), 30 + i * 2, 43 + i * 2)
        d.arc((cx-r, cy-r, cx+r, cy+r), 20+i*8, 330-i*5, fill=color, width=max(2, width//700))
    for arm in range(6):
        a = -math.pi / 2 + arm * math.pi / 3
        x0 = cx + int(base * .32 * math.cos(a))
        y0 = cy + int(base * .32 * math.sin(a))
        x1 = cx + int(base * 1.75 * math.cos(a + .11))
        y1 = cy + int(base * 1.75 * math.sin(a + .11))
        d.line((x0, y0, x1, y1), fill=(110, 37, 48), width=max(2, width//800))
        d.ellipse((x1-4, y1-4, x1+4, y1+4), fill=(192, 67, 74))
    d.ellipse((cx-base*.16, cy-base*.16, cx+base*.16, cy+base*.16), outline=(219, 80, 81), width=max(3,width//500))
    d.ellipse((cx-base*.055, cy-base*.055, cx+base*.055, cy+base*.055), fill=(219, 80, 81))

    x = int(width * .07)
    if compact:
        d.text((x, height*.18), "SECOND CRUCIBLE", font=font(FONT_BOLD, 70), fill=(239, 232, 222))
        d.text((x, height*.53), "UNLOCK + ENCOUNTER FIXES", font=font(FONT_REGULAR, 30), fill=(213, 120, 117))
        d.text((x, height*.76), "BUILD 22928553", font=font(FONT_REGULAR, 20), fill=(151, 161, 168))
    else:
        d.text((x, height*.20), "SECOND", font=font(FONT_BOLD, 134), fill=(239, 232, 222))
        d.text((x, height*.34), "CRUCIBLE", font=font(FONT_BOLD, 134), fill=(239, 232, 222))
        d.rectangle((x, int(height*.56), int(width*.43), int(height*.563)), fill=(185, 66, 70))
        d.text((x, height*.62), "UNLOCK + ENCOUNTER FIXES", font=font(FONT_REGULAR, 43), fill=(213, 120, 117))
        d.text((x, height*.78), "NO REST FOR THE WICKED  /  BUILD 22928553", font=font(FONT_REGULAR, 28), fill=(151, 161, 168))
    im.save(ROOT / output, optimize=True)


if __name__ == "__main__":
    render(1920, 1080, "gallery.png", False)
    render(1300, 372, "header.png", True)
