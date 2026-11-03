"""render_word_playbook.py - render a parsed legal playbook (playbook_parse.py JSON) as a Word playbook.

Build-time (python-docx). The output is what an attorney attaches to the Word legal plugin's
"Review this contract using a playbook" (Attach from Cloud). The plugin imports it as a playbook
object of topics, each with Name / Rules / Examples (T-12 desktop, results L24), so the layout
mirrors that shape: one Heading 2 per topic, the rule fields as labelled lines, model language last.
The importer strips bracketed ids from headings, so the topic id travels as a "Topic ID:" line
inside the rules, where it survives and lets a Word review be compared with a Cowork review.
Practice-neutral: nothing here names a contract type.
"""
import docx
from docx.shared import Pt

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
