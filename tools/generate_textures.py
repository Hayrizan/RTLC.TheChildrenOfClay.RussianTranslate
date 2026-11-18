import argparse
import os
import sys
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

sys.stdout.reconfigure(encoding="utf-8")

ROOT_DIR = Path(__file__).resolve().parent.parent
DEFAULT_SOURCE_DIR = ROOT_DIR / "tools" / "source_textures"
DEFAULT_OUTPUT_DIR = ROOT_DIR / "Assets" / "Textures"
ORIG_DIR = DEFAULT_SOURCE_DIR
OUT_DIR = DEFAULT_OUTPUT_DIR
FONT_SANS = None


def resolve_font(requested_font):
    candidates = [Path(requested_font)] if requested_font else []
    if not requested_font:
        windows_dir = os.environ.get("WINDIR")
        if windows_dir:
            candidates.append(Path(windows_dir) / "Fonts" / "segoeui.ttf")
        candidates.extend([
            Path("/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf"),
            Path("/System/Library/Fonts/Supplemental/Arial.ttf"),
        ])

    for candidate in candidates:
        try:
            ImageFont.truetype(str(candidate), 20)
            return str(candidate)
        except OSError:
            continue
    raise ValueError("No usable font found; pass --font with a TrueType font path.")


def draw_centered_text_in_box(size, lines, font_path, font_size, color, center_x, center_y, line_spacing=40):
    img = Image.new("RGBA", size, (0, 0, 0, 0))
    draw = ImageDraw.Draw(img)
    font = ImageFont.truetype(font_path, font_size)

    bboxes = [draw.textbbox((0, 0), line, font=font) for line in lines]
    heights = [bb[3] - bb[1] for bb in bboxes]
    total_h = sum(heights) + line_spacing * (len(lines) - 1)

    y = center_y - total_h / 2.0
    for line, bb, h in zip(lines, bboxes, heights):
        w = bb[2] - bb[0]
        x = center_x - w / 2.0 - bb[0]
        draw.text((x, y - bb[1]), line, font=font, fill=color)
        y += h + line_spacing
    return img


def make_ending_logo():
    # Original bbox=(509, 814, 844, 1204), center=(676.5, 1009.0), color=(189, 85, 101)
    im = draw_centered_text_in_box(
        (2048, 2048),
        ["ДЕТИ", "ГЛИНЫ"],
        FONT_SANS,
        98,
        (189, 85, 101, 255),
        center_x=677,
        center_y=1009,
        line_spacing=46,
    )
    im.save(os.path.join(OUT_DIR, "endingscreenlogo.png"))


def make_credit_line(filename, size, text, font_size, cx, cy):
    im = draw_centered_text_in_box(
        size,
        [text],
        FONT_SANS,
        font_size,
        (203, 70, 93, 255),
        center_x=cx,
        center_y=cy,
        line_spacing=0,
    )
    im.save(os.path.join(OUT_DIR, filename))


def make_rovas_chart():
    src = Image.open(os.path.join(ORIG_DIR, "rovasiras.png")).convert("RGBA")
    arr = np.array(src)

    # Erase only the 3 Latin letter columns by sampling clean paper pixels on the left + blank row in the same column
    rng = np.random.default_rng(101)
    for x0, x1 in [(306, 385), (506, 585), (706, 785)]:
        y0, y1 = 115, 820
        left_strip = arr[y0:y1, x0 - 10 : x0 - 2, :3]
        # Sample clean background inside the same x-column from a blank band (e.g. y=560..615 for col2, y=755..810 for col1/3)
        blank_y0 = 560 if x0 == 506 else 755
        blank_strip = arr[blank_y0 : blank_y0 + 45, x0:x1, :3]
        w = x1 - x0
        for row_i in range(y1 - y0):
            # Use local left-edge vertical brightness matched with blank-band horizontal texture
            local_mean = left_strip[row_i].mean(axis=0)
            b_row = blank_strip[row_i % blank_strip.shape[0]].astype(float)
            b_mean = b_row.mean(axis=0)
            adjusted = np.clip(b_row - b_mean + local_mean, 0, 255).astype(np.uint8)
            perm = rng.integers(0, w, size=w)
            arr[y0 + row_i, x0:x1, :3] = adjusted[perm]
            arr[y0 + row_i, x0:x1, 3] = 255

    base = Image.fromarray(arr, mode="RGBA")
    overlay = Image.new("RGBA", base.size, (0, 0, 0, 0))
    draw = ImageDraw.Draw(overlay)

    font = ImageFont.truetype(FONT_SANS, 40)

    columns = [
        (311, [
            (142.0, "А"),
            (214.0, "Б"),
            (277.5, "Ц"),
            (341.0, "Ч"),
            (404.5, "Д"),
            (468.0, "Е"),
            (534.5, "Ф"),
            (598.5, "Г"),
            (662.0, "ДЬ"),
            (725.5, "Х"),
        ]),
        (512, [
            (144.0, "Й"),
            (206.0, "К"),
            (270.5, "Л"),
            (333.5, "ЛЬ"),
            (397.0, "М"),
            (461.0, "Н"),
            (524.5, "НЬ"),
            (654.5, "П"),
            (729.0, "Р"),
            (793.5, "Ш"),
        ]),
        (712, [
            (141.5, "С"),
            (205.0, "Т"),
            (269.0, "ТЬ"),
            (333.5, "У"),
            (402.0, "В"),
            (466.0, "З"),
            (529.5, "Ж"),
        ]),
    ]

    for x_left, rows in columns:
        for cy, text in rows:
            bb = draw.textbbox((0, 0), text, font=font)
            th = bb[3] - bb[1]
            draw.text((x_left - bb[0], cy - th / 2.0 - bb[1]), text, font=font, fill=(24, 24, 24, 255))

    overlay = overlay.filter(ImageFilter.GaussianBlur(radius=0.45))
    result = Image.alpha_composite(base, overlay)
    result.save(os.path.join(OUT_DIR, "rovasiras.png"))


CREDIT_TRANS_COLOR = (200, 68, 90, 255)


def _crop_trimmed_x(src_arr, y0, y1, x0, x1):
    """Crops [y0:y1, x0:x1] and trims leading/trailing empty (alpha <= 8) columns."""
    sub = src_arr[y0:y1, x0:x1].copy()
    col_has = np.where((sub[:, :, 3] > 8).any(axis=0))[0]
    if len(col_has) == 0:
        return Image.fromarray(sub, mode="RGBA")
    first_x = int(col_has[0])
    last_x = int(col_has[-1]) + 1
    return Image.fromarray(sub[:, first_x:last_x], mode="RGBA")


def _draw_left_baseline_text(im, x, y_baseline, text, font_size, color=CREDIT_TRANS_COLOR):
    """Draws text anchored at left-baseline (anchor='ls') with subtle anti-alias softening and returns rendered (x0, y0, x1, y1)."""
    tmp = Image.new("RGBA", im.size, (0, 0, 0, 0))
    draw = ImageDraw.Draw(tmp)
    font = ImageFont.truetype(FONT_SANS, font_size)
    draw.text((x, y_baseline), text, font=font, fill=color, anchor="ls")
    tmp = tmp.filter(ImageFilter.GaussianBlur(radius=0.35))
    arr = np.array(tmp)
    ys, xs = np.where(arr[:, :, 3] > 10)
    im.alpha_composite(tmp)
    if len(xs) == 0:
        return (x, y_baseline, x, y_baseline)
    return (int(xs.min()), int(ys.min()), int(xs.max()), int(ys.max()))


def _make_simple_translation_credit(filename, ru_header, header_x, header_y_base=549, clear_y_end=580, font_size=35):
    src = Image.open(os.path.join(ORIG_DIR, filename)).convert("RGBA")
    arr = np.array(src)
    arr[:clear_y_end, :, :] = 0
    im = Image.fromarray(arr, mode="RGBA")
    _draw_left_baseline_text(im, header_x, header_y_base, ru_header, font_size)
    im.save(os.path.join(OUT_DIR, filename))


def make_translation_cat():
    src = Image.open(os.path.join(ORIG_DIR, "translation_cat.png")).convert("RGBA")
    src_arr = np.array(src)

    im = Image.new("RGBA", src.size, (0, 0, 0, 0))
    # Preserve Line 2 ("Mel negra")
    im_arr = np.array(im)
    im_arr[585:670, :] = src_arr[585:670, :]
    im = Image.fromarray(im_arr, mode="RGBA")

    # Line 1: header
    _draw_left_baseline_text(im, 335, 549, "перевод на каталанский", 35)

    # Line 3: "Перевод:" + original "Eloi Nada" (orig baseline 721, cap height ~29-30 -> font_size=41)
    eloi_block = _crop_trimmed_x(src_arr, 685, 730, 505, 668)
    _, _, l3_end, _ = _draw_left_baseline_text(im, 335, 722, "Перевод:", 41)
    im.alpha_composite(eloi_block, dest=(l3_end + 11, 685))

    # Line 4: "Редактура:" + original "Xavier Quero" (orig baseline 803, cap height 29 -> font_size=41)
    xavier_block = _crop_trimmed_x(src_arr, 768, 815, 459, 682)
    _, _, l4_end, _ = _draw_left_baseline_text(im, 335, 804, "Редактура:", 41)
    im.alpha_composite(xavier_block, dest=(l4_end + 11, 768))

    im.save(os.path.join(OUT_DIR, "translation_cat.png"))


def make_translation_sp_ch():
    src = Image.open(os.path.join(ORIG_DIR, "translation_sp_ch.png")).convert("RGBA")
    src_arr = np.array(src)

    im = Image.new("RGBA", src.size, (0, 0, 0, 0))
    # Preserve Line 2 ("°Know Team")
    im_arr = np.array(im)
    im_arr[405:470, :] = src_arr[405:470, :]
    im = Image.fromarray(im_arr, mode="RGBA")

    # Line 1: header
    _draw_left_baseline_text(im, 293, 366, "перевод на упрощённый китайский", 34)

    # Line 3: "переводчик" + ": Cûedhel"
    cuedhel_block = _crop_trimmed_x(src_arr, 475, 518, 571, 725)
    _, _, l3_end, _ = _draw_left_baseline_text(im, 420, 508, "переводчик", 35)
    im.alpha_composite(cuedhel_block, dest=(l3_end + 10, 475))

    # Line 4: "корректор" + ": LeanderLin"
    leander_block = _crop_trimmed_x(src_arr, 526, 572, 603, 800)
    _, _, l4_end, _ = _draw_left_baseline_text(im, 420, 559, "корректор", 35)
    im.alpha_composite(leander_block, dest=(l4_end + 10, 526))

    # Line 5: "ассистент" + ": Amazing Orange Tree"
    orange_block = _crop_trimmed_x(src_arr, 577, 625, 569, 920)
    _, _, l5_end, _ = _draw_left_baseline_text(im, 420, 610, "ассистент", 35)
    im.alpha_composite(orange_block, dest=(l5_end + 10, 577))

    # Line 6: "дополнительное тестирование:" + "Gao Ladon"
    gao_block = _crop_trimmed_x(src_arr, 660, 708, 613, 785)
    _, _, l6_end, _ = _draw_left_baseline_text(im, 294, 692, "дополнительное тестирование:", 35)
    im.alpha_composite(gao_block, dest=(l6_end + 10, 660))

    im.save(os.path.join(OUT_DIR, "translation_sp_ch.png"))


def main():
    parser = argparse.ArgumentParser(description="Generate Russian localized game textures.")
    parser.add_argument(
        "--source-textures",
        default=os.environ.get("THE_CHILDREN_OF_CLAY_SOURCE_TEXTURES", str(DEFAULT_SOURCE_DIR)),
        help="Directory containing the original PNG textures (or set THE_CHILDREN_OF_CLAY_SOURCE_TEXTURES).",
    )
    parser.add_argument("--output-dir", default=str(DEFAULT_OUTPUT_DIR), help="Directory for generated PNG files.")
    parser.add_argument("--font", help="TrueType font path; defaults to Segoe UI on Windows or a common system sans font.")
    args = parser.parse_args()

    global ORIG_DIR, OUT_DIR, FONT_SANS
    ORIG_DIR = Path(args.source_textures).expanduser().resolve()
    OUT_DIR = Path(args.output_dir).expanduser().resolve()
    try:
        FONT_SANS = resolve_font(args.font)
    except ValueError as error:
        parser.error(str(error))

    required_sources = [
        "rovasiras.png",
        "translation_pr_br.png",
        "translation_ko.png",
        "translation_es.png",
        "translation_jp.png",
        "translation_cat.png",
        "translation_sp_ch.png",
    ]
    missing_sources = [name for name in required_sources if not (ORIG_DIR / name).is_file()]
    if missing_sources:
        parser.error(f"missing original texture(s) in {ORIG_DIR}: {', '.join(missing_sources)}")

    OUT_DIR.mkdir(parents=True, exist_ok=True)
    make_ending_logo()
    make_credit_line("createdby.png", (1024, 1024), "автор игры: balazs ronyai", 52, 600, 491)
    make_credit_line("musicby.png", (2048, 2048), "музыка: michael barker (juanjo_sound)", 52, 1111, 1002)
    make_credit_line("jam.png", (2048, 2048), "создано для cosmic horrors jam 3 в 2024 году", 52, 1044, 1002)
    make_rovas_chart()

    # Translation credit textures (names left untranslated)
    _make_simple_translation_credit("translation_pr_br.png", "перевод на бразильский португальский", header_x=336)
    _make_simple_translation_credit("translation_ko.png", "перевод на корейский", header_x=336)
    _make_simple_translation_credit("translation_es.png", "перевод на испанский", header_x=334)
    _make_simple_translation_credit("translation_jp.png", "перевод на японский", header_x=331)
    make_translation_cat()
    make_translation_sp_ch()

    for f in sorted(os.listdir(OUT_DIR)):
        p = OUT_DIR / f
        im = Image.open(p)
        print(f"Generated {f:22s} {im.size} {im.mode}")


if __name__ == "__main__":
    main()
