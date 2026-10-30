"""Row reconstruction from a text-layer PDF using character rectangles (pypdfium2). Used by label_fee_applications.py.

`page_rows(page)` returns rows top to bottom; each row is a list of cells (x0, x1, text) left to right, built by grouping
text fragments whose vertical centres lie within `ytol` points and merging fragments closer than `gap` points.
Parsing real documents is untrusted-input work: run callers with `python -I`.
"""


def page_rows(page, ytol=2.5, gap=3.5):
    tp = page.get_textpage()
    frags = []
    for i in range(tp.count_rects()):
        x0, y0, x1, y1 = tp.get_rect(i)
        t = tp.get_text_bounded(x0, y0, x1, y1).replace("\r", " ").replace("\n", " ").strip()
        # A leading decimal point (".30") can fall just outside the rectangle: re-read short numeric cells with slack.
        if t and (t[0].isdigit() or t[0] == "$") and x1 - x0 < 70:
            t2 = tp.get_text_bounded(x0 - 1.5, y0, x1 + 1.5, y1).replace("\r", " ").replace("\n", " ").strip()
            if t2.replace(",", "").replace("$", "").replace(" ", "").replace(".", "").isdigit():
                t = t2
        if t:
            frags.append((x0, x1, (y0 + y1) / 2.0, t))
    frags.sort(key=lambda f: (-f[2], f[0]))
    rows = []
    for f in frags:
        for r in rows:
            if abs(r["y"] - f[2]) <= ytol:
                r["f"].append(f)
                break
        else:
            rows.append({"y": f[2], "f": [f]})
    out = []
    for r in sorted(rows, key=lambda r: -r["y"]):
        cells = []
        for x0, x1, _, t in sorted(r["f"], key=lambda f: f[0]):
            if cells and x0 - cells[-1][1] <= gap and not t[0:1].isupper() and False:
                cells[-1] = (cells[-1][0], x1, cells[-1][2] + t)
            elif cells and x0 - cells[-1][1] <= gap:
                cells[-1] = (cells[-1][0], x1, cells[-1][2] + (" " if cells[-1][2][-1:] != " " else "") + t)
            else:
                cells.append((x0, x1, t))
        out.append((r["y"], cells))
    return out


def dump_page(path, page_no, maxrows=70):
    import pypdfium2 as pdfium
    doc = pdfium.PdfDocument(path)
    for y, cells in page_rows(doc[page_no - 1])[:maxrows]:
        print(f"{y:6.1f} | " + " | ".join(f"{c[0]:.0f}-{c[1]:.0f}:{c[2][:48]}" for c in cells))
