"""render_word_playbook.py - render a parsed legal playbook (playbook_parse.py JSON) as a Word copy.

Build-time (python-docx). The Word copy is what an attorney attaches to the Word legal plugin's
"Review this contract using a playbook" (Attach from Cloud), or saves there as a playbook.

Two layouts:
  render_table()  PRIMARY (design v0.6 §5.3, OD-39). Microsoft's contract-review playbook shape, as
                  written by Legal for Cowork `create-playbook` (whose output Word imported directly,
                  POC Act 1): landscape US Letter; a Title-style heading (Word names a saved playbook
                  after it); one short scope paragraph; ONE table with exactly four columns
                  Topic | Instructions | Example Text | Compromise Positions; repeating header row;
                  rows that do not split; page numbers. No fifth column, no narrative sections.
                  The Markdown master stays the superset: Priority and Rationale are not rendered.
  render()        Transitional heading layout (Heading 2 per topic, labelled lines). Kept until the
                  Word test set (runbook Act 2, R1-R7 and A/B) confirms the table (backlog X3).

  render_readable()  Table-free copy for an agent's knowledge (Title, version line, introduction, then per topic a
                  Heading 2 "<n>. <title> (<topic id>)" and labelled paragraphs). No tables.

Practice-neutral: nothing here names a contract type.
"""
import re
from datetime import datetime, timezone

import docx
from docx.enum.section import WD_ORIENT
from docx.enum.table import WD_TABLE_ALIGNMENT
from docx.oxml import OxmlElement
from docx.oxml.ns import qn
from docx.shared import Inches, Pt, RGBColor

COLUMNS = ("Topic", "Instructions", "Example Text", "Compromise Positions")
COLUMN_WIDTHS = (Inches(1.7), Inches(3.4), Inches(2.6), Inches(2.1))  # 9.8" = 11" - 2 x 0.6" margins
TOPIC_ID_PLACEMENTS = ("none", "instructions", "topic")  # decided by the Word A/B test; "none" = Cowork's shape
SILENT_INSTRUCTION = "No standard position in this playbook. Do not edit; flag the clause for attorney review."

FIELDS = [("preferred", "Preferred position"), ("fallback", "Acceptable fallback"),
          ("escalate", "Prohibited / escalate"), ("rationale", "Rationale"), ("model_language", "Model language")]


def guidelines(playbook, titles):
    """General guidelines: becomes the plugin's own "General guidelines" topic on import."""
    meta = playbook["meta"]
    out = [
        "Apply the positions in this playbook to the agreement under review. Each topic below is one "
        "issue; review the agreement topic by topic.",
        "Sources: you MUST base every finding and edit only on this playbook and the agreement under "
        "review. You MUST NOT use or cite other documents, emails, earlier reviews, redlines or web "
        "results as positions.",
        "Edits: you MUST make the smallest change that brings a clause to the preferred position, or to "
        "the acceptable fallback where one is given. Change words and phrases; you MUST NOT replace "
        "whole clauses or sections. Keep numbering, defined terms and formatting.",
        "Comments: one comment per change, naming the topic and giving the reason from its rationale.",
        "Prohibited / escalate: you MUST NOT draft around these silently. Flag them for attorney review "
        "with a comment.",
    ]
    if meta.get("side") == "mutual":
        out.append("This is a mutual agreement: apply every position to both parties equally.")
    if meta.get("jurisdiction_default"):
        out.append(f"Default governing law and venue: {meta['jurisdiction_default']}.")
    if playbook.get("silent_topics"):
        names = ", ".join(titles.get(i, {}).get("title", i) for i in playbook["silent_topics"])
        out.append(f"This playbook takes no position on: {names}. Report these as not covered; do not edit them.")
    if meta.get("status"):
        out.append(f"Status: {meta['status']}. Treat every result as a draft for attorney review.")
    return out


def labelled(d, label, text):
    p = d.add_paragraph()
    p.add_run(f"{label}: ").bold = True
    p.add_run(text)


def render(playbook, catalogue, out_path, fallback_label="Acceptable fallback"):
    titles = {t["id"]: t for t in catalogue["topics"]}
    labels = dict(FIELDS, fallback=fallback_label)
    d = docx.Document()
    st = d.styles["Normal"]
    st.font.name = "Arial"
    st.font.size = Pt(10)
    meta = playbook["meta"]
    props = d.core_properties
    props.title = playbook["title"]
    props.subject = "Contract review playbook"
    props.comments = ("Attach in Word: Copilot > Review this contract using a playbook > Attach from Cloud. "
                      "Generated from the Markdown playbook by the Spaarke accelerator build; edit the "
                      "Markdown source, not this file. Draft for attorney review; not legal advice.")
    d.add_heading(playbook["title"], level=1)
    d.add_paragraph(" | ".join(f"{k.replace('_', ' ').capitalize()}: {v}" for k, v in meta.items() if v))
    d.add_heading("General guidelines", level=2)
    for line in guidelines(playbook, titles):
        d.add_paragraph(line, style="List Bullet")
    for t in playbook["topics"]:
        cat = titles.get(t["id"], {})
        d.add_heading(cat.get("title", t["id"]), level=2)
        labelled(d, "Topic ID", t["id"])
        labelled(d, "Priority", t["priority"])
        if cat.get("question"):
            labelled(d, "Review question", cat["question"])
        for key, _ in FIELDS:
            if t.get(key):
                labelled(d, labels[key], f"“{t[key]}”" if key == "model_language" else t[key])
    d.save(out_path)
    return out_path


# ---------------------------------------------------------------- four-column table (primary)

def word_name(playbook):
    """The name Word shows for a saved playbook: the title, with the version if the title lacks it."""
    title, version = playbook["title"].strip(), playbook["meta"].get("version", "").strip()
    if version and not re.search(rf"\bv{re.escape(version)}\s*$", title):
        title = f"{title} v{version}"
    return title


def scope_paragraph(playbook):
    """One short paragraph above the table: the master's introduction, then the header facts."""
    meta = playbook["meta"]
    intro = (playbook.get("intro") or "").strip()
    facts = []  # header facts the introduction doesn't already state
    if meta.get("side") and "side:" not in intro.lower():
        side = meta["side"]
        facts.append("Side: mutual (apply every position to both parties)" if side == "mutual" else f"Side: {side}")
    if meta.get("jurisdiction_default") and meta["jurisdiction_default"] not in intro:
        facts.append(f"Default governing law and venue: {meta['jurisdiction_default']}")
    for key, label in (("version", "Version"), ("owner", "Owner"), ("effective", "Effective"), ("status", "Status")):
        if meta.get(key):
            facts.append(f"{label}: {meta[key]}")
    tail = ". ".join(facts) + "." if facts else ""
    return f"{intro} {tail}".strip()


def example_text(model_language):
    """Model language is often an insertion fragment for redlining; mark a fragment with an ellipsis."""
    text = (model_language or "").strip()
    if not text:
        return ""
    if text[0] in ",;:" or text[0].islower():
        text = "…" + text.lstrip(",;: ")
    return text


FALLBACK_LABEL = "Acceptable fallback (accept without redlining)"  # Word keeps only Rules + Examples (gap #30)


def instruction_paragraphs(topic, topic_ids):
    """Instructions cell. Word's saved playbook keeps only Name / Rules / Examples and drops the Compromise
    column (POC gap #30), so the fallback is repeated here; "If missing" makes absence topics imperative
    (gap #31). The Compromise column still carries the fallback for Cowork and review-contract."""
    out = [topic["preferred"].strip()]
    if topic.get("if_missing"):
        out.append(f"If missing: {topic['if_missing'].strip()}")
    if topic.get("fallback"):
        out.append(f"{FALLBACK_LABEL}: {topic['fallback'].strip()}")
    if topic.get("escalate"):
        out.append(f"Escalate if: {topic['escalate'].strip()}")
    if topic_ids == "instructions":
        out.append(f"Topic ID: {topic['id']}")
    return out


def table_rows(playbook, catalogue, topic_ids="none"):
    """The table as data: a list of 4-tuples of cell paragraph lists. Shared by the renderer and its checker."""
    if topic_ids not in TOPIC_ID_PLACEMENTS:
        raise ValueError(f"topic_ids must be one of {TOPIC_ID_PLACEMENTS}")
    titles = {t["id"]: t for t in catalogue["topics"]}

    def topic_cell(tid):
        name = titles.get(tid, {}).get("title", tid)
        return [f"{name} ({tid})" if topic_ids == "topic" else name]

    rows = []
    for t in playbook["topics"]:
        ex, comp = example_text(t.get("model_language")), (t.get("fallback") or "").strip()
        rows.append((topic_cell(t["id"]), instruction_paragraphs(t, topic_ids), [ex] if ex else [], [comp] if comp else []))
    for tid in playbook.get("silent_topics", []):
        instr = [SILENT_INSTRUCTION] + ([f"Topic ID: {tid}"] if topic_ids == "instructions" else [])
        rows.append((topic_cell(tid), instr, [], []))
    return rows


def _set_cell_width(cell, width):
    cell.width = width  # python-docx writes w:tcW in dxa


def _fixed_layout(table):
    """Grid column widths; table.autofit = False already writes <w:tblLayout w:type="fixed"/>."""
    for col, width in zip(table.columns, COLUMN_WIDTHS):
        col.width = width  # w:gridCol


def _row_flag(row, tag):
    trpr = row._tr.get_or_add_trPr()
    el = OxmlElement(tag)
    el.set(qn("w:val"), "true")
    trpr.append(el)


def _shade(cell, fill):
    shd = OxmlElement("w:shd")
    shd.set(qn("w:val"), "clear")
    shd.set(qn("w:color"), "auto")
    shd.set(qn("w:fill"), fill)
    cell._tc.get_or_add_tcPr().append(shd)


def _fill(cell, paragraphs, bold=False, muted_prefix=None):
    cell.text = ""
    first = cell.paragraphs[0]
    for i, text in enumerate(paragraphs):
        p = first if i == 0 else cell.add_paragraph()
        run = p.add_run(text)
        run.bold = bold
        if muted_prefix and text.startswith(muted_prefix):
            run.font.size, run.font.color.rgb = Pt(8), RGBColor(0x59, 0x59, 0x59)


def _field(paragraph, instr):
    """Append a simple field (PAGE / NUMPAGES) to a paragraph."""
    run = paragraph.add_run()
    for kind, text in (("begin", None), (None, instr), ("separate", None), (None, "1"), ("end", None)):
        if kind:
            el = OxmlElement("w:fldChar")
            el.set(qn("w:fldCharType"), kind)
        elif text == instr:
            el = OxmlElement("w:instrText")
            el.set(qn("xml:space"), "preserve")
            el.text = f" {instr} "
        else:
            el = OxmlElement("w:t")
            el.text = text
        run._r.append(el)


PREFACE_STYLES = {"bullet": "List Bullet", "bullet2": "List Bullet 2"}


def _add_preface(d, preface):
    """Paragraphs before the table, from (kind, text) pairs: "h" = bold lead-in line, "bullet"/"bullet2" = list
    items, "para" = plain. No Heading styles and no tables, so the file keeps the four-column shape's one table."""
    for kind, text in preface:
        if kind in PREFACE_STYLES:
            d.add_paragraph(text, style=PREFACE_STYLES[kind])
        else:
            p = d.add_paragraph()
            run = p.add_run(text)
            if kind == "h":
                run.bold = True
                p.paragraph_format.space_before = Pt(8)


def render_table(playbook, catalogue, out_path, topic_ids="none", preface=None):
    """Render the Word copy in Microsoft's four-column contract-review playbook shape.

    preface = optional (kind, text) pairs placed after the title and before the scope paragraph: the review method
    for NON-contract playbooks (gap #36), so attaching one file brings both. Never for a contract playbook: Word's
    Review contract imports the text around the table as "General guidelines"."""
    rows = table_rows(playbook, catalogue, topic_ids)
    name = word_name(playbook)
    d = docx.Document()
    normal = d.styles["Normal"]
    normal.font.name, normal.font.size = "Calibri", Pt(10)
    s = d.sections[0]
    s.orientation = WD_ORIENT.LANDSCAPE
    s.page_width, s.page_height = Inches(11), Inches(8.5)
    for side in ("left_margin", "right_margin", "top_margin", "bottom_margin"):
        setattr(s, side, Inches(0.6))
    status = playbook["meta"].get("status")
    s.header.paragraphs[0].text = f"{name} | {status}" if status else name
    foot = s.footer.paragraphs[0]
    foot.add_run("Page ")
    _field(foot, "PAGE")
    foot.add_run(" of ")
    _field(foot, "NUMPAGES")

    props = d.core_properties
    props.title, props.subject, props.author = name, "Playbook", "Spaarke accelerator build"
    if preface:
        props.comments = ("Word copy of a legal playbook, with its review method first. In Word: attach it in Copilot "
                          "chat (agent mode, Chat only). Generated from the Markdown master; edit the master, not this "
                          "file. Draft for attorney review; not legal advice.")
    else:
        props.comments = ("Word copy of a legal playbook. In Word: Copilot > Review contract > New playbook > Attach "
                          "from Cloud. Generated from the Markdown master; edit the master, not this file. Draft for "
                          "attorney review; not legal advice.")

    d.add_paragraph(name, style="Title")
    _add_preface(d, preface or [])
    d.add_paragraph(scope_paragraph(playbook))

    t = d.add_table(rows=1, cols=4)
    t.style, t.alignment, t.autofit = "Table Grid", WD_TABLE_ALIGNMENT.CENTER, False
    _fixed_layout(t)
    header = t.rows[0]
    _row_flag(header, "w:tblHeader")
    _row_flag(header, "w:cantSplit")
    for cell, label, width in zip(header.cells, COLUMNS, COLUMN_WIDTHS):
        _fill(cell, [label], bold=True)
        _shade(cell, "D9D9D9")
        _set_cell_width(cell, width)
    for row in rows:
        r = t.add_row()
        _row_flag(r, "w:cantSplit")
        for cell, paragraphs, width in zip(r.cells, row, COLUMN_WIDTHS):
            _fill(cell, paragraphs, muted_prefix="Topic ID: ")
            _set_cell_width(cell, width)
    d.save(out_path)
    return out_path


# ---------------------------------------------------------------- readable copy (table-free, agent knowledge)

def readable_blocks(playbook, catalogue):
    """The readable copy as data: a list of (heading, [(label, text), ...]) per topic, in playbook order, silent
    topics last. Only the fields a topic has are listed. Shared by the renderer and its checker."""
    titles = {t["id"]: t for t in catalogue["topics"]}
    blocks = []
    for n, t in enumerate(playbook["topics"], 1):
        fields = [("Instructions", t["preferred"].strip())]
        for label, key in (("If missing", "if_missing"), (FALLBACK_LABEL, "fallback"), ("Escalate if", "escalate")):
            if t.get(key):
                fields.append((label, t[key].strip()))
        if example_text(t.get("model_language")):
            fields.append(("Example text", example_text(t["model_language"])))
        blocks.append((f"{n}. {titles.get(t['id'], {}).get('title', t['id'])} ({t['id']})", fields))
    for n, tid in enumerate(playbook.get("silent_topics", []), len(playbook["topics"]) + 1):
        blocks.append((f"{n}. {titles.get(tid, {}).get('title', tid)} ({tid})", [("Instructions", SILENT_INSTRUCTION)]))
    return blocks


def render_readable(playbook, catalogue, out_path, created=None):
    """Table-free Word copy for an agent's knowledge (Copilot agents read plain headings and paragraphs more
    reliably than a four-column table). Title, version/status line, introduction, then per topic a Heading 2
    "<n>. <title> (<topic id>)" followed by labelled paragraphs. No tables. created = the document's created date
    (default: now; python-docx's template would otherwise say 2013)."""
    name = word_name(playbook)
    meta = playbook["meta"]
    d = docx.Document()
    normal = d.styles["Normal"]
    normal.font.name, normal.font.size = "Calibri", Pt(11)
    when = created or datetime.now(timezone.utc)  # core properties are stored as UTC
    props = d.core_properties
    props.title, props.subject, props.author = name, "Playbook (readable copy)", "Spaarke accelerator build"
    props.last_modified_by, props.created, props.modified = "Spaarke accelerator build", when, when
    props.comments = ("Readable (table-free) copy of a legal playbook, for an agent's knowledge. Generated from the "
                      "Markdown master; edit the master, not this file. Draft for attorney review; not legal advice.")
    d.add_paragraph(name, style="Title")
    line = [f"Version {meta['version']}"] if meta.get("version") else []
    line += [f"{label}: {meta[key]}" for key, label in (("owner", "Owner"), ("effective", "Effective"),
                                                         ("status", "Status")) if meta.get(key)]
    if line:
        d.add_paragraph(" · ".join(line))
    if (playbook.get("intro") or "").strip():
        d.add_paragraph(playbook["intro"].strip())
    for heading, fields in readable_blocks(playbook, catalogue):
        d.add_heading(heading, level=2)
        for label, text in fields:
            labelled(d, label, text)
    d.save(out_path)
    return out_path


def check_readable(path, playbook, catalogue):
    """Deterministic checker for the readable copy. Returns a list of problems."""
    problems = []
    d = docx.Document(path)
    if d.tables:
        problems.append(f"expected no tables, found {len(d.tables)}")
    name = word_name(playbook)
    if d.core_properties.title != name:
        problems.append("document Title property differs from the playbook name")
    if d.core_properties.created is None or d.core_properties.created.year < 2020:
        problems.append(f"created date is {d.core_properties.created}, not a real build date")
    titles = [p for p in d.paragraphs if p.style.name == "Title"]
    if len(titles) != 1 or titles[0].text != name:
        problems.append("expected one Title paragraph equal to the playbook name")
    expected = readable_blocks(playbook, catalogue)
    found, current = [], None
    for p in d.paragraphs:
        if p.style.name == "Heading 2":
            current = (p.text, [])
            found.append(current)
        elif current is not None and p.text.strip():
            label, _, text = p.text.partition(": ")
            current[1].append((label, text))
    if [h for h, _ in found] != [h for h, _ in expected]:
        problems.append("topic headings differ from the playbook (count, order or text)")
    else:
        for (h, got), (_, want) in zip(found, expected):
            if got != want:
                problems.append(f"{h}: labelled paragraphs differ from the playbook")
    return problems


def check_table(path, playbook, catalogue, topic_ids="none", preface=None):
    """Deterministic checker (cf. Microsoft create-playbook's verify-playbook.py). Returns a list of problems.
    The paragraphs outside the table must be exactly: the title, the preface texts (if any), the scope paragraph."""
    problems, expected = [], table_rows(playbook, catalogue, topic_ids)
    d = docx.Document(path)
    want_paras = [word_name(playbook)] + [t for _, t in (preface or [])] + [scope_paragraph(playbook)]
    got_paras = [p.text for p in d.paragraphs]
    if got_paras != want_paras:
        problems.append(f"paragraphs outside the table differ: {len(got_paras)} found, {len(want_paras)} expected"
                        + ("" if preface else " (no preface expected)"))
    if len(d.tables) != 1:
        return [f"expected exactly 1 table, found {len(d.tables)}"]
    t = d.tables[0]
    head = [c.text for c in t.rows[0].cells]
    if tuple(head) != COLUMNS:
        problems.append(f"header row is {head}, expected {list(COLUMNS)}")
    if any(len(r.cells) != 4 for r in t.rows):
        problems.append("a row does not have exactly four cells")
    body = t.rows[1:]
    if len(body) != len(expected):
        problems.append(f"{len(body)} topic rows, expected {len(expected)}")
    for i, (r, want) in enumerate(zip(body, expected), 1):
        got = tuple("\n".join(p.text for p in c.paragraphs).strip() for c in r.cells)
        want = tuple("\n".join(cell).strip() for cell in want)
        if got != want:
            problems.append(f"row {i} ({want[0]}): cell text differs from the playbook")
    titles = [p for p in d.paragraphs if p.style.name == "Title"]
    if len(titles) != 1 or titles[0].text != word_name(playbook):
        problems.append("expected one Title paragraph equal to the playbook name")
    if d.core_properties.title != word_name(playbook):
        problems.append("document Title property differs from the playbook name")
    if d.sections[0].orientation != WD_ORIENT.LANDSCAPE:
        problems.append("page is not landscape")
    if any(p.style.name.startswith("Heading") for p in d.paragraphs):
        problems.append("narrative headings present (no sections outside the table)")
    if t.rows[0]._tr.trPr is None or t.rows[0]._tr.trPr.find(qn("w:tblHeader")) is None:
        problems.append("header row does not repeat across pages")
    return problems
