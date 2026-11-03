"""Scan-noise pass for synthetic invoices: rasterise a clean PDF and degrade it like a scan, fax or photocopy.

Needs pypdfium2, Pillow and numpy (generator-only dependencies; see gen_eval_set.py header). Deterministic per seed.
Parameters are chosen at data time (`scan_params`) and recorded in the ground truth so the degradation is reproducible
and documented; bounding boxes are not part of the ground truth.
"""
import random


def scan_params(rng: random.Random):
    style = rng.choices(["scan", "fax", "photocopy"], [0.55, 0.2, 0.25])[0]
    return {"style": style, "dpi": rng.choice([150, 150, 200, 200, 300]) if style != "fax" else 200,
            "skew_deg": round(rng.uniform(-0.9, 0.9), 2), "noise": rng.choice([8, 12, 18]),
            "blur": round(rng.uniform(0.3, 0.9), 2), "stamp": rng.random() < 0.25,
            "jpeg_quality": rng.choice([55, 65, 75])}


def scanify(src_pdf: str, dst_pdf: str, params: dict, seed: int):
    import numpy as np
    import pypdfium2 as pdfium
    from PIL import Image, ImageDraw, ImageFilter, ImageFont

    rs = np.random.RandomState(seed)
    rng = random.Random(seed)
    doc = pdfium.PdfDocument(src_pdf)
    imgs = []
    for i in range(len(doc)):
        page = doc[i]
        bmp = page.render(scale=params["dpi"] / 72.0)
        img = bmp.to_pil().convert("L")
        if params["stamp"] and i == 0:
            w, h = img.size
            layer = Image.new("L", (int(w * 0.34), int(h * 0.06)), 255)
            ld = ImageDraw.Draw(layer)
            ld.rectangle([2, 2, layer.size[0] - 3, layer.size[1] - 3], outline=90, width=3)
            try:
                f = ImageFont.load_default(size=max(14, int(layer.size[1] * 0.5)))
            except TypeError:
                f = ImageFont.load_default()
            ld.text((layer.size[0] * 0.12, layer.size[1] * 0.2), "RECEIVED", fill=90, font=f)
            layer = layer.rotate(rng.uniform(-12, 12), expand=True, fillcolor=255)
            mask = layer.point(lambda v: 255 if v < 200 else 0)
            img.paste(layer, (int(w * rng.uniform(0.05, 0.10)), int(h * rng.uniform(0.89, 0.92))), mask)
        img = img.rotate(params["skew_deg"], resample=Image.BICUBIC, expand=False, fillcolor=255)
        img = img.filter(ImageFilter.GaussianBlur(params["blur"]))
        a = np.asarray(img).astype("float32")
        a += rs.normal(0, params["noise"], a.shape)
        if params["style"] == "photocopy":
            a = (a - 128) * 1.35 + 128
            spk = rs.rand(*a.shape) < 0.0008
            a[spk] = 0
            a[:, :14] *= 0.55
        if params["style"] == "fax":
            a = np.where(a < 150 + rs.normal(0, 18, a.shape), 0, 255)
            band = rs.randint(0, a.shape[0] - 4, 3)
            for b in band:
                a[b:b + 2, :] = 255
        a = np.clip(a, 0, 255).astype("uint8")
        imgs.append(Image.fromarray(a, "L"))
    imgs[0].save(dst_pdf, "PDF", save_all=True, append_images=imgs[1:], resolution=float(params["dpi"]),
                 quality=params["jpeg_quality"])
