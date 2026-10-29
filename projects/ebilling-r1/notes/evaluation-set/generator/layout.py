"""Invoice templates, pagination and PDF drawing (reportlab) for the synthetic evaluation set.

`layout_invoice` paginates an invoice dict and returns (pages, line_pages); the ground truth stores the page of each
line, so pagination is computed with the same code that draws the PDF. 24 templates cover the layout axes of
test-data-sources.md 5b. Evaluation tooling only.
"""
import datetime as dt

from reportlab.lib.utils import simpleSplit
from reportlab.pdfbase.pdfmetrics import stringWidth
from reportlab.pdfgen import canvas

import refdata as R

GENERATOR_ID = "gen_eval_set.py@1"

# ------------------------------------------------------------------ templates
DEFAULT = dict(orient="P", font="Helvetica", fs=9, group="none", cols=["date", "tk", "code", "desc", "hours", "rate", "amount"],
               hours_fmt="dec2", date_fmt="us", code_style="column", code_sep="/", exp_mode="separate",
               summary_first=False, page_totals=False, header="left", money="us", tk_fmt="initials", title_fmt="abbr",
               caps=False, inv_fmt="INV-{y}-{n:05d}", legend="bottom", neg="paren")


def _t(name, **kw):
    d = dict(DEFAULT)
    d.update(kw, name=name)
    return d


TEMPLATES = [
    _t("T01"),
    _t("T02", font="Times-Roman", fs=10, tk_fmt="full", cols=["date", "tk", "desc", "hours", "rate", "amount"], code_style="prefix", inv_fmt="{y}{n:06d}"),
    _t("T03", group="timekeeper", cols=["date", "code", "desc", "hours", "amount"], tk_fmt="full", legend="top", header="center"),
    _t("T04", group="task", cols=["date", "tk", "desc", "hours", "rate", "amount"], tk_fmt="first_last_initial", code_style="none"),
    _t("T05", group="date", cols=["tk", "code", "desc", "hours", "amount"], date_fmt="long", header="boxed"),
    _t("T06", orient="L", fs=8, cols=["date", "tk", "title", "code", "desc", "hours", "rate", "amount"], title_fmt="full", tk_fmt="last_first"),
    _t("T07", font="Courier", fs=8, cols=["date", "tk", "code", "desc", "hours", "amount"], hours_fmt="dec1", code_sep="-", inv_fmt="A-{n:05d}/{y}"),
    _t("T08", hours_fmt="hhmm", cols=["date", "tk", "desc", "hours", "rate", "amount"], code_style="prefix", date_fmt="iso"),
    _t("T09", exp_mode="interleaved", cols=["date", "tk", "code", "desc", "hours", "rate", "amount"], money="plain"),
    _t("T10", summary_first=True, group="timekeeper", cols=["date", "code", "desc", "hours", "amount"], tk_fmt="full", header="boxed"),
    _t("T11", page_totals=True, font="Times-Roman", cols=["date", "tk", "code", "desc", "hours", "rate", "amount"], money="suffix"),
    _t("T12", caps=True, cols=["date", "tk", "desc", "hours", "amount"], code_style="none", date_fmt="dmy", neg="minus"),
    _t("T13", orient="L", group="timekeeper", fs=9, cols=["date", "title", "code", "desc", "hours", "rate", "amount"], title_fmt="full", tk_fmt="full"),
    _t("T14", group="task", summary_first=True, cols=["date", "tk", "desc", "hours", "amount"], font="Times-Roman", tk_fmt="last_first"),
    _t("T15", hours_fmt="hhmm", group="date", cols=["tk", "code", "desc", "hours", "amount"], header="center", date_fmt="long"),
    _t("T16", page_totals=True, group="timekeeper", cols=["date", "code", "desc", "hours", "rate", "amount"], tk_fmt="first_last_initial"),
    _t("T17", font="Courier", fs=8, orient="L", cols=["date", "tk", "title", "code", "desc", "hours", "rate", "amount"], code_sep=" ", hours_fmt="dec1"),
    _t("T18", exp_mode="interleaved", group="none", cols=["date", "tk", "desc", "hours", "rate", "amount"], code_style="prefix", tk_fmt="full", date_fmt="dmy"),
    _t("T19", fs=10, cols=["date", "tk", "code", "desc", "amount"], legend="top", header="boxed", money="suffix", inv_fmt="LF{n:07d}"),
    _t("T20", caps=True, group="task", cols=["date", "tk", "desc", "hours", "rate", "amount"], font="Times-Roman", date_fmt="iso"),
    _t("T21", summary_first=True, page_totals=True, cols=["date", "tk", "code", "desc", "hours", "rate", "amount"], header="center", money="plain"),
    _t("T22", orient="L", fs=8, group="date", cols=["tk", "title", "code", "desc", "hours", "rate", "amount"], tk_fmt="initials", title_fmt="abbr"),
    _t("T23", cols=["date", "tk", "code", "desc", "hours", "rate", "amount"], hours_fmt="hhmm", font="Times-Roman", header="boxed", neg="minus"),
    _t("T24", group="timekeeper", exp_mode="separate", cols=["date", "desc", "hours", "amount"], tk_fmt="last_first", code_style="prefix", legend="top"),
]
SYMBOL = {"USD": "$", "GBP": "£", "EUR": "€", "CAD": "C$"}


def invoice_number(tpl, rng):
    return tpl["inv_fmt"].format(y=rng.choice([2025, 2026]), n=rng.randint(100, 99999))


def code_capability(tpl):
    """What the template prints about codes: "full" (column or prefix), "task_only" (group headers) or "none"."""
    if (tpl["code_style"] == "column" and "code" in tpl["cols"]) or tpl["code_style"] == "prefix":
        return "full"
    if tpl["group"] == "task":
        return "task_only"
    return "none"


def billed_name(name, tpl):
    first, last = name.split(" ", 1)
    f = tpl["tk_fmt"]
    if f == "full":
        return name
    if f == "initials":
        return f"{first[0]}{last[0]}"
    if f == "last_first":
        return f"{last}, {first[0]}."
    return f"{first[0]}. {last}"


def billed_title(title, tpl):
    if tpl["title_fmt"] == "full":
        return title
    return [t[1] for t in R.TITLES if t[0] == title][0]


# ------------------------------------------------------------------ formatting
def fmt_date(iso, tpl):
    if not iso:
        return ""
    d = dt.date.fromisoformat(iso)
    f = tpl["date_fmt"]
    if f == "us":
        return d.strftime("%m/%d/%Y")
    if f == "iso":
        return iso
    if f == "dmy":
        return d.strftime("%d-%b-%Y")
    return d.strftime("%B %d, %Y").replace(" 0", " ")


def fmt_hours(h, tpl):
    if h is None:
        return ""
    f = tpl["hours_fmt"]
    if f == "dec2":
        return f"{h:.2f}"
    if f == "dec1":
        return f"{h:.2f}" if abs(h * 10 - round(h * 10)) > 1e-9 else f"{h:.1f}"
    mins = int(round(h * 60))
    return f"{mins // 60}:{mins % 60:02d}"


def fmt_money(x, tpl, cur):
    if x is None:
        return ""
    neg = x < 0
    s = f"{abs(x):,.2f}"
    m = tpl["money"]
    if m == "us":
        s = SYMBOL[cur] + s
    elif m == "suffix":
        s = f"{s} {cur}"
    if neg:
        s = f"({s})" if tpl["neg"] == "paren" else f"-{s}"
    return s


def order_lines(lines, tpl):
    fee = [l for l in lines if l["line_type"] == "fee"]
    exp = [l for l in lines if l["line_type"] == "expense"]
    adj = [l for l in lines if l["line_type"] == "adjustment"]
    key_date = lambda l: (l["date"] or "")
    g = tpl["group"]
    if g == "timekeeper":
        fee.sort(key=lambda l: ((l["tk"] or {"name": "~"})["name"], key_date(l)))
    elif g == "task":
        fee.sort(key=lambda l: ((l["codes"][0] if l["codes"] else l["intended"][0]), key_date(l)))
    else:
        fee.sort(key=key_date)
    exp.sort(key=key_date)
    if tpl["exp_mode"] == "interleaved" and g == "none":
        return sorted(fee + exp, key=key_date) + adj
    return fee + exp + adj


# ------------------------------------------------------------------ layout
LETTER = (612.0, 792.0)


class Pg:
    def __init__(self, size):
        self.size, self.ops = size, []


def _desc_text(l, tpl):
    t = l["narrative"]
    if tpl["caps"]:
        t = t.upper()
    if tpl["code_style"] == "prefix" and l["codes"]:
        t = f"[{tpl['code_sep'].join(l['codes'])}] {t}"
    return t


def _col_weights(tpl):
    w = {"date": 9, "tk": 11, "title": 9, "code": 9, "desc": 42, "hours": 6, "rate": 8, "amount": 10}
    cols = [c for c in tpl["cols"] if not (c == "code" and tpl["code_style"] != "column")]
    tot = sum(w[c] for c in cols)
    return cols, [w[c] / tot for c in cols]


def layout_invoice(inv, tpl, bundle=None):
    """Paginate. Returns (pages, line_page, invoice_first_page, invoice_pages, parts)."""
    size = (LETTER[1], LETTER[0]) if tpl["orient"] == "L" else LETTER
    W, H = size
    ML, MR, MT, MB = 40.0, 40.0, 48.0, 54.0
    fs, font = tpl["fs"], tpl["font"]
    lead = fs * 1.3
    pad = 2.5
    hdr = inv["header"]
    cur = hdr["currency"]
    pages = []
    parts = []
    line_page = {}

    # ---- optional cover letter pages (portrait)
    if bundle and bundle["cover"]:
        pg = Pg(LETTER)
        y = LETTER[1] - 70
        pg.ops.append(("text", 60, y, inv["firm"]["name"], "Helvetica-Bold", 14, "l"))
        for ad in inv["firm"]["address"][:2]:
            y -= 14
            pg.ops.append(("text", 60, y, ad, "Helvetica", 9, "l"))
        y -= 50
        pg.ops.append(("text", 60, y, fmt_date(hdr["invoice_date"], tpl), "Helvetica", 10, "l"))
        y -= 30
        for t in [f"Re: Invoice {hdr['firm_invoice_number']}", f"Our matter {hdr['firm_matter_reference']}"]:
            pg.ops.append(("text", 60, y, t, "Helvetica-Bold", 10, "l"))
            y -= 15
        y -= 15
        body = ("Enclosed please find our invoice for professional services rendered and expenses incurred during the "
                f"period {fmt_date(hdr['billing_period_start'], tpl)} through {fmt_date(hdr['billing_period_end'], tpl)}. "
                f"The amount due is {fmt_money(hdr['total_due'], tpl, cur)}. Please let us know if you have any "
                "questions about the enclosed statement. Payment instructions appear on the remittance advice. "
                "We appreciate the opportunity to be of service.")
        for ln in simpleSplit(body, "Helvetica", 10, LETTER[0] - 120):
            pg.ops.append(("text", 60, y, ln, "Helvetica", 10, "l"))
            y -= 14
        y -= 30
        pg.ops.append(("text", 60, y, "Sincerely,", "Helvetica", 10, "l"))
        pg.ops.append(("text", 60, y - 36, (sorted(inv["_tks"])[0] if inv["_tks"] else "Billing Department"), "Helvetica", 10, "l"))
        pg.footer = "cover"
        pages.append(pg)
        parts.append({"part": "cover_letter", "pages": 1})
    inv_first = len(pages)

    # ---- invoice pages
    cols, fr = _col_weights(tpl)
    TW = W - ML - MR
    xs, acc = [], ML
    for f in fr:
        xs.append(acc)
        acc += f * TW
    wd = [f * TW for f in fr]
    heads = {"date": "Date", "tk": "Timekeeper", "title": "Title", "code": "Code", "desc": "Description",
             "hours": "Hours", "rate": "Rate", "amount": "Amount"}
    right = {"hours", "rate", "amount"}

    cur_pg = [None]
    y = [0.0]
    state = {"in_table": False, "pt_h": 0.0, "page_h": 0.0, "page_amt": 0.0, "group": None}

    def new_page():
        pg = Pg(size)
        pg.footer = "invoice"
        pages.append(pg)
        cur_pg[0] = pg
        y[0] = H - MT
        return pg

    def txt(x, yy, s, f=None, sz=None, al="l"):
        cur_pg[0].ops.append(("text", x, yy, s, f or font, sz or fs, al))

    def rect(x, yy, w, h, fill):
        cur_pg[0].ops.append(("rect", x, yy, w, h, fill))

    def hline(yy):
        cur_pg[0].ops.append(("line", ML, yy, W - MR, yy))

    def draw_table_header(expenses=False):
        h = lead + 2 * pad
        rect(ML, y[0] - h, TW, h, 0.88)
        hd = dict(heads, hours="Qty", rate="Unit Cost") if expenses else heads
        for c, x, w in zip(cols, xs, wd):
            if c in right:
                txt(x + w - 3, y[0] - pad - fs, hd[c], font + ("-Bold" if font == "Helvetica" else ""), fs, "r")
            else:
                txt(x + 2, y[0] - pad - fs, hd[c], font + ("-Bold" if font == "Helvetica" else ""), fs)
        y[0] -= h

    def ensure(h, reserve_pt=True):
        extra = state["pt_h"] if (state["in_table"] and tpl["page_totals"] and reserve_pt) else 0.0
        if y[0] - h - extra < MB:
            if state["in_table"] and tpl["page_totals"] and state["page_h"] > 0:
                ph = lead + 2 * pad
                txt(xs[cols.index("desc")] + 2 if "desc" in cols else ML, y[0] - pad - fs, "Page total",
                    font, fs)
                if "hours" in cols:
                    txt(xs[cols.index("hours")] + wd[cols.index("hours")] - 3, y[0] - pad - fs,
                        fmt_hours(state["page_h"], tpl), font, fs, "r")
                txt(xs[-1] + wd[-1] - 3, y[0] - pad - fs, fmt_money(state["page_amt"], tpl, cur), font, fs, "r")
                y[0] -= ph
            new_page()
            state["page_h"] = state["page_amt"] = 0.0
            if state["in_table"]:
                draw_table_header()
                if state["group"]:
                    txt(ML + 2, y[0] - pad - fs, f"{state['group']} (continued)", font, fs)
                    y[0] -= lead + 2 * pad

    def row(cells, bold=False, shade=None, record=None, hrs=None, amt=None, is_line=True):
        """cells: dict col -> text. Wrap the description; place the row; record page for line n."""
        wrapped = {}
        nlines = 1
        for c in cols:
            s = cells.get(c, "")
            if c == "desc":
                ls = simpleSplit(s, font, fs, wd[cols.index(c)] - 6) or [""]
            elif c not in right and stringWidth(s, font, fs) > wd[cols.index(c)] - 4:
                ls = simpleSplit(s, font, fs, wd[cols.index(c)] - 4) or [""]
            else:
                ls = [s]
            wrapped[c] = ls
            nlines = max(nlines, len(ls))
        h = nlines * lead + 2 * pad
        ensure(h)
        if shade is not None:
            rect(ML, y[0] - h, TW, h, shade)
        for c, x, w in zip(cols, xs, wd):
            for i, s in enumerate(wrapped[c]):
                yy = y[0] - pad - fs - i * lead
                if c in right:
                    txt(x + w - 3, yy, s, font + ("-Bold" if bold and font == "Helvetica" else ""), fs, "r")
                else:
                    txt(x + 2, yy, s, font + ("-Bold" if bold and font == "Helvetica" else ""), fs)
        y[0] -= h
        if record is not None:
            line_page[record] = len(pages) - 1
        if hrs is not None:
            state["page_h"] += hrs
        if amt is not None:
            state["page_amt"] += amt

    def section_title(s):
        ensure(lead * 2 + 10, reserve_pt=False)
        y[0] -= 6
        txt(ML, y[0] - fs, s, "Helvetica-Bold", fs + 1)
        y[0] -= lead + 4

    # ---- page 1 header
    new_page()
    state["pt_h"] = lead + 2 * pad
    name_f = "Helvetica-Bold"
    if tpl["header"] == "center":
        txt(W / 2, y[0] - 16, inv["firm"]["name"], name_f, 15, "c")
        for i, ad in enumerate(inv["firm"]["address"]):
            txt(W / 2, y[0] - 30 - i * 11, ad, "Helvetica", 8, "c")
        y[0] -= 30 + 11 * len(inv["firm"]["address"]) + 10
    elif tpl["header"] == "boxed":
        rect(ML, y[0] - 44, TW, 44, 0.9)
        txt(ML + 8, y[0] - 20, inv["firm"]["name"], name_f, 15)
        txt(ML + 8, y[0] - 34, " | ".join(inv["firm"]["address"][:2]), "Helvetica", 8)
        y[0] -= 56
    else:
        txt(ML, y[0] - 16, inv["firm"]["name"], name_f, 15)
        for i, ad in enumerate(inv["firm"]["address"]):
            txt(ML, y[0] - 30 - i * 11, ad, "Helvetica", 8)
        y[0] -= 30 + 11 * len(inv["firm"]["address"]) + 10
    meta = [("Invoice No.", hdr["firm_invoice_number"]), ("Invoice Date", fmt_date(hdr["invoice_date"], tpl)),
            ("Billing Period", f"{fmt_date(hdr['billing_period_start'], tpl)} - {fmt_date(hdr['billing_period_end'], tpl)}"),
            ("Matter Ref.", hdr["firm_matter_reference"])]
    if hdr.get("po_number"):
        meta.append(("Purchase Order", hdr["po_number"]))
    meta.append(("Currency", cur))
    txt(ML, y[0] - 10, "INVOICE", "Helvetica-Bold", 13)
    txt(ML, y[0] - 26, "Bill To: Client Legal Department", "Helvetica", 9)
    txt(ML, y[0] - 38, "Re: Professional services", "Helvetica", 9)
    my = y[0] - 10
    for k, v in meta:
        txt(W - MR - 190, my, k + ":", "Helvetica-Bold", 9)
        txt(W - MR - 100, my, v, "Helvetica", 9)
        my -= 12
    y[0] = min(y[0] - 52, my - 6)

    def legend():
        used = sorted(inv["_tks"].values(), key=lambda t: t["name"])
        section_title("Timekeeper Summary")
        hrs_by = {}
        for l in inv["lines"]:
            if l["line_type"] == "fee" and l["hours"] is not None and l.get("timekeeper_name"):
                hrs_by[l["timekeeper_name"]] = hrs_by.get(l["timekeeper_name"], 0.0) + l["hours"]
        ensure(lead * 2 + 4, reserve_pt=False)
        for x, hdr_t, al in [(ML, "Timekeeper", "l"), (ML + 170, "Title", "l"), (ML + 280, "Rate", "r"),
                             (ML + 340, "Hours", "r"), (ML + 420, "Amount", "r")]:
            txt(x, y[0] - fs, hdr_t, "Helvetica-Bold", fs, al)
        y[0] -= lead + 2
        for t in used:
            ensure(lead + 2, reserve_pt=False)
            h = hrs_by.get(t["name"], 0.0)
            bn = billed_name(t["name"], tpl)
            txt(ML, y[0] - fs, t["name"] if bn == t["name"] else f"{t['name']} ({bn})", font, fs)
            txt(ML + 170, y[0] - fs, t["title"], font, fs)
            txt(ML + 280, y[0] - fs, fmt_money(t["rate"], dict(tpl, money="plain"), cur), font, fs, "r")
            txt(ML + 340, y[0] - fs, fmt_hours(h, tpl), font, fs, "r")
            txt(ML + 420, y[0] - fs, fmt_money(round(h * t["rate"], 2), tpl, cur), font, fs, "r")
            y[0] -= lead + 1
        y[0] -= 6

    def totals_box():
        ensure(lead * 6 + 20, reserve_pt=False)
        state["in_table"] = False
        y[0] -= 8
        bx = W - MR - 230
        for lab, val, b in [("Fees", hdr["fees_total"], False), ("Expenses", hdr["expenses_total"], False),
                            ("Adjustments", hdr["adjustments_total"], False), ("TOTAL DUE", hdr["total_due"], True)]:
            fnt = "Helvetica-Bold" if b else "Helvetica"
            txt(bx, y[0] - fs, lab, fnt, fs + (1 if b else 0))
            txt(W - MR, y[0] - fs, fmt_money(val, tpl, cur), fnt, fs + (1 if b else 0), "r")
            y[0] -= lead + 3
        y[0] -= 6
        txt(ML, y[0] - fs, f"Payment due within 30 days. Remit to: {inv['firm']['name']}, {inv['firm']['address'][0]}.",
            "Helvetica", 8)
        y[0] -= lead
        txt(ML, y[0] - fs, inv["firm"]["address"][2], "Helvetica", 8)
        y[0] -= lead

    lines = inv["lines"]
    fee = [l for l in lines if l["line_type"] == "fee"]
    exp = [l for l in lines if l["line_type"] == "expense"]
    adj = [l for l in lines if l["line_type"] == "adjustment"]

    if tpl["legend"] == "top" or tpl["summary_first"] or "rate" not in cols:
        if tpl["legend"] == "top" or tpl["summary_first"]:
            legend()
    if tpl["summary_first"]:
        new_page()
        state["pt_h"] = lead + 2 * pad

    def line_cells(l):
        cells = {"date": fmt_date(l["date"], tpl), "tk": l["timekeeper"] or "", "title": l["title"] or "",
                 "desc": _desc_text(l, tpl),
                 "hours": (f"{l['hours']:g}" if l["line_type"] == "expense" and l["hours"] is not None else fmt_hours(l["hours"], tpl)),
                 "rate": fmt_money(l["rate"], dict(tpl, money="plain"), cur) if l["rate"] is not None else "",
                 "amount": fmt_money(l["amount"], tpl, cur)}
        if tpl["code_style"] == "column":
            cells["code"] = tpl["code_sep"].join(l["codes"])
        return cells

    # fee section (plus interleaved expenses if the template interleaves)
    ensure(lead * 5, reserve_pt=False)
    state["in_table"] = True
    draw_table_header()
    g = tpl["group"]
    cur_key = None
    sub_h = sub_a = 0.0
    body = [l for l in lines if l["line_type"] == "fee" or (tpl["exp_mode"] == "interleaved" and g == "none" and l["line_type"] == "expense")]

    def flush_subtotal():
        nonlocal sub_h, sub_a
        if g != "none" and cur_key is not None:
            cells = {"desc": "Subtotal", "hours": fmt_hours(sub_h, tpl), "amount": fmt_money(round(sub_a, 2), tpl, cur)}
            row(cells, bold=True, is_line=False)
        sub_h = sub_a = 0.0

    for l in body:
        if g == "timekeeper":
            key = l.get("timekeeper_name") or "Flat fee items"
            label = f"Timekeeper: {l['timekeeper_name']} ({l['timekeeper_title']})" if l.get("timekeeper_name") else "Flat fee items"
        elif g == "task":
            key = l["codes"][0] if l["codes"] else l["intended"][0]
            label = f"{key} - {R.TASK.get(key, '')}"
        elif g == "date":
            key = l["date"]
            label = f"Date: {fmt_date(l['date'], tpl)}"
        else:
            key, label = None, None
        if g != "none" and key != cur_key:
            flush_subtotal()
            cur_key = key
            state["group"] = None
            row({"desc": label}, bold=True, shade=0.94, is_line=False)
            state["group"] = label
        cells = line_cells(l)
        if g == "date":
            cells["date"] = ""
        row(cells, record=l["n"], hrs=l["hours"] if l["line_type"] == "fee" else 0, amt=l["amount"])
        if l["hours"] is not None and l["line_type"] == "fee":
            sub_h += l["hours"]
        sub_a += l["amount"]
    flush_subtotal()
    state["group"] = None
    state["in_table"] = False

    sep_exp = [l for l in exp if not (tpl["exp_mode"] == "interleaved" and g == "none")]
    if sep_exp:
        section_title("Disbursements")
        ensure(lead * 4, reserve_pt=False)
        state["in_table"] = True
        draw_table_header(expenses=True)
        for l in sep_exp:
            row(line_cells(l), record=l["n"], hrs=0, amt=l["amount"])
        state["in_table"] = False
        cells = {"desc": "Total disbursements", "amount": fmt_money(hdr["expenses_total"], tpl, cur)}
        state["in_table"] = True
        row(cells, bold=True)
        state["in_table"] = False
    if adj:
        section_title("Adjustments")
        state["in_table"] = True
        for l in adj:
            row({"desc": l["narrative"], "amount": fmt_money(l["amount"], tpl, cur)}, record=l["n"], amt=l["amount"])
        state["in_table"] = False
    totals_box()
    if tpl["legend"] == "bottom" and not (tpl["summary_first"]):
        legend()
    inv_pages = len(pages) - inv_first
    parts.append({"part": "invoice", "pages": inv_pages})

    # ---- optional trailing pages
    if bundle and bundle["remit"]:
        pg = Pg(LETTER)
        pg.footer = "remit"
        yy = LETTER[1] - 80
        pg.ops.append(("text", 60, yy, "REMITTANCE ADVICE", "Helvetica-Bold", 14, "l"))
        for k, v in [("Invoice number", hdr["firm_invoice_number"]), ("Matter", hdr["firm_matter_reference"]),
                     ("Amount enclosed", ""), ("Remit to", inv["firm"]["name"]), ("", inv["firm"]["address"][0]),
                     ("", inv["firm"]["address"][1])]:
            yy -= 22
            pg.ops.append(("text", 60, yy, k, "Helvetica-Bold", 10, "l"))
            pg.ops.append(("text", 200, yy, v, "Helvetica", 10, "l"))
        pages.append(pg)
        parts.append({"part": "remittance_advice", "pages": 1})
    if bundle and bundle["statement"]:
        pg = Pg(LETTER)
        pg.footer = "statement"
        yy = LETTER[1] - 80
        pg.ops.append(("text", 60, yy, "STATEMENT OF ACCOUNT", "Helvetica-Bold", 14, "l"))
        yy -= 28
        for lab, amt in [("Balance forward", 18420.50), ("Payments received", -18420.50), ("Prior invoice outstanding", 6310.00)]:
            pg.ops.append(("text", 60, yy, lab, "Helvetica", 10, "l"))
            pg.ops.append(("text", 400, yy, fmt_money(amt, tpl, cur), "Helvetica", 10, "r"))
            yy -= 18
        pg.ops.append(("text", 60, yy - 10, "Current invoice: see attached", "Helvetica", 10, "l"))
        pages.append(pg)
        parts.append({"part": "statement_of_account", "pages": 1})
    return pages, line_page, inv_first, inv_pages, parts


def draw_pdf(path, inv, tpl, pages, inv_first, inv_pages):
    c = canvas.Canvas(path, pagesize=LETTER, invariant=1, pageCompression=1)
    c.setTitle(f"Synthetic invoice {inv['header']['firm_invoice_number']}")
    c.setAuthor("eBilling evaluation set generator")
    for i, pg in enumerate(pages):
        c.setPageSize(pg.size)
        for op in pg.ops:
            if op[0] == "text":
                _, x, y, s, f, sz, al = op
                c.setFont(f, sz)
                {"l": c.drawString, "r": c.drawRightString, "c": c.drawCentredString}[al](x, y, s)
            elif op[0] == "rect":
                _, x, y, w, h, fill = op
                c.setFillGray(fill)
                c.rect(x, y, w, h, stroke=0, fill=1)
                c.setFillGray(0)
            else:
                _, x1, y1, x2, y2 = op
                c.line(x1, y1, x2, y2)
        c.setFont("Helvetica", 7)
        c.setFillGray(0.45)
        c.drawString(40, 24, "Synthetic evaluation document - not a real invoice")
        if pg.footer == "invoice":
            c.drawRightString(pg.size[0] - 40, 24,
                              f"Invoice {inv['header']['firm_invoice_number']}  Page {i - inv_first + 1} of {inv_pages}")
        c.setFillGray(0)
        c.showPage()
    c.save()
