"""make_doctype_library.py - the document-type library kit (POC Act 7, 2026-11-07).

Builds, under kit-held/Act 7 - Document-type library/out/, the files for the Legal Knowledge library organised
by document type (one folder per type, the playbook file at the top of each folder):

  Legal Knowledge/Start here – Quick card.docx
  Legal Knowledge/NDA/Spaarke NDA – Mutual v1.2.docx                  (Word copy; X2b: fallbacks + If missing)
  Legal Knowledge/NDA/Playbook master/Spaarke NDA – Mutual v1.2.md   (master, read by Cowork)
  Legal Knowledge/Legal Hold Notice/Spaarke Legal Hold Notice – Employee Custodians v1.0.docx
  POC Workspace/Matters/Harbor Point Logistics/Harbor Point – Legal Hold Notice (draft A|B).docx
  POC Workspace/Test variants/Spaarke Legal Hold Notice – Employee Custodians v1.0 (requirements).docx

and answer-key/legal-hold-answer-key.md (never uploaded).

The legal hold notice is the non-contract test type. Its positions follow Fed. R. Civ. P. 37(e) (reasonable
steps to preserve electronically stored information) and The Sedona Conference, Commentary on Legal Holds,
Second Edition: The Trigger & The Process (2019). Synthetic POC content, not attorney-approved.

Run: C:\\Python313\\python.exe -B make_doctype_library.py
"""
import os
import sys
import tempfile

import docx
from docx.shared import Pt

HERE = os.path.dirname(os.path.abspath(__file__))
POC = os.path.dirname(HERE)
REPO = os.path.abspath(os.path.join(POC, "..", "..", "..", ".."))
ACC = os.path.join(REPO, "src", "accelerators")
KIT = os.path.join(POC, "kit-held", "Act 7 - Document-type library")
OUT = os.path.join(KIT, "out")
sys.path[:0] = [os.path.join(ACC, "build"), os.path.join(ACC, "core", "engines")]
import build_accelerator as b  # noqa: E402
import playbook_parse as pp  # noqa: E402
import render_word_playbook as rw  # noqa: E402

NDA_NAME = "Spaarke NDA – Mutual v1.2"
LH_NAME = "Spaarke Legal Hold Notice – Employee Custodians v1.0"
LK = os.path.join(OUT, "Legal Knowledge")
WS = os.path.join(OUT, "POC Workspace")

# ------------------------------------------------------------------ legal hold notice playbook (13 topics)
LH_INTRO = (
    "Scope. Document type: legal hold (litigation hold) notice from in-house counsel to employee custodians, sent "
    "when litigation or a government investigation is reasonably anticipated. Use it to review a draft notice before "
    "it is sent. Basis: Fed. R. Civ. P. 37(e) (reasonable steps to preserve electronically stored information) and "
    "The Sedona Conference, Commentary on Legal Holds, Second Edition (2019). Do not use for: preservation demand "
    "letters to an opposing party, subpoenas, or records-retention schedules.")
LH_META = {"version": "1.0", "owner": "Spaarke Legal Engineering", "effective": "2026-11-07",
           "status": "POC test positions, not attorney-approved"}

LH_TOPICS = [
    dict(id="lh.matter", title="Matter identification",
         preferred="Identifies the dispute or investigation (the other party and the general subject) clearly enough "
                   "that recipients can recognise relevant information.",
         fallback="A matter code name, if the notice also describes the subject matter it covers.",
         escalate="The notice does not say what matter it concerns.",
         model_language="Acme has received a claim from [other party] concerning [subject]. Acme expects litigation "
                        "and must preserve information relevant to it."),
    dict(id="lh.time-period", title="Relevant time period",
         preferred="A start date early enough to cover the underlying events (for a contract dispute, at least from "
                   "the negotiation of the agreement), and an ongoing hold that also covers information created or "
                   "received after the notice.",
         fallback="A start date at the signing of the agreement, if the hold is ongoing.",
         escalate="A period that ends at the notice date or another fixed date, or that starts only at the demand "
                  "letter or another recent event.",
         model_language="This hold covers information from [start date, no later than the start of the relationship "
                        "or events in dispute] to the present, and applies to information created or received after "
                        "the date of this notice."),
    dict(id="lh.subject-scope", title="Subject matter to preserve",
         preferred="Describes the categories of relevant information in plain language (the agreement, its "
                   "performance, communications with the other side, internal discussions, financial records).",
         escalate="Scope limited to what the recipient considers important or key, or described only in legal "
                  "terms such as \"all discoverable material\"."),
    dict(id="lh.data-types", title="Forms and locations of information",
         preferred="Covers every form and location: email; chat and collaboration messages (for example Teams); text "
                   "and instant messages, including on personal phones used for work; voicemail; documents on "
                   "network drives, OneDrive and SharePoint; business systems; paper files and notebooks.",
         escalate="Limited to email, paper or shared-drive documents; omits chat, text messages or personal devices "
                  "used for work.",
         if_missing="If the notice does not mention chat messages, text messages or personal devices used for work, "
                    "it is not acceptable: add them from the Example Text.",
         model_language="This includes email, Teams and other chat messages, text and instant messages (including on "
                        "personal phones used for work), voicemail, documents on network drives, OneDrive and "
                        "SharePoint, business systems, and paper files and notebooks."),
    dict(id="lh.preserve-instruction", title="Preservation instruction",
         preferred="Tells recipients plainly not to delete, discard, alter or overwrite relevant information, and to "
                   "keep it where it is: no moving, forwarding to personal accounts or tidying up.",
         escalate="Any invitation to delete, tidy, consolidate or clean up files, drafts or duplicates, or to use "
                  "personal judgment about what to keep.",
         model_language="Do not delete, discard, alter or overwrite any of this information, and do not move it or "
                        "forward it to personal accounts. Keep it as it is, including drafts and duplicates."),
    dict(id="lh.auto-deletion", title="Automatic deletion",
         preferred="Tells recipients to turn off automatic deletion they control (mailbox clean-up rules, auto-delete "
                   "of messages on phones) and says that Legal and IT are placing technical holds on company "
                   "systems.",
         fallback="Says that IT will suspend automatic deletion on company systems, without steps for recipients.",
         escalate="Silent on automatic deletion.",
         if_missing="If the notice says nothing about automatic deletion, it is not acceptable: add the instruction "
                    "from the Example Text.",
         model_language="Turn off any automatic deletion you control, such as mailbox clean-up rules or auto-delete "
                        "settings for messages on your phone. Legal and IT are placing technical holds on company "
                        "systems; do not rely on them alone."),
    dict(id="lh.overrides-retention", title="Retention schedules",
         preferred="States that the hold overrides the records-retention schedule and any routine destruction until "
                   "Legal releases it.",
         escalate="Says or implies that normal retention or destruction schedules continue to apply.",
         model_language="This hold overrides Acme's records-retention schedule and any routine destruction practice."),
    dict(id="lh.other-custodians", title="Other custodians and departures",
         preferred="Asks recipients to tell Legal about anyone else (including former employees, contractors and "
                   "vendors) who may have relevant information, and to tell Legal before they leave the company or "
                   "change role so that their devices and accounts are preserved.",
         fallback="Asks recipients to identify other people with relevant information, without the departure "
                  "instruction.",
         escalate="Neither: the notice treats the recipient list as complete.",
         model_language="If you know of anyone else, including former employees, contractors or vendors, who may have "
                        "relevant information, tell Legal. If you plan to leave Acme or change roles, tell Legal "
                        "first so that your devices and accounts can be preserved."),
    dict(id="lh.duration-release", title="Duration and release",
         preferred="The hold stays in effect until Legal releases it in writing.",
         escalate="A fixed expiry date, or an end on an event without a written release from Legal.",
         model_language="This hold remains in effect until Legal tells you in writing that it has been released."),
    dict(id="lh.acknowledgment", title="Acknowledgment",
         preferred="Each recipient confirms in writing, by a stated date (normally within 5 business days), that they "
                   "have read the notice and will comply.",
         fallback="Written acknowledgment requested without a deadline.",
         escalate="No acknowledgment requested.",
         model_language="Please confirm by [date, within 5 business days] that you have read this notice and will "
                        "comply, by replying to this email."),
    dict(id="lh.contact", title="Legal contact",
         preferred="Names a person in Legal, with contact details, for questions.",
         fallback="Names a Legal Department mailbox.",
         escalate="No contact for questions."),
    dict(id="lh.privilege-content", title="Privilege and content",
         preferred="Marked privileged and confidential, and limited to what recipients need in order to preserve: no "
                   "assessment of the merits, strategy or settlement, and no opinions about the other side, because "
                   "a hold notice may be disclosed in the litigation.",
         escalate="Comments on the strength of the claims, strategy or settlement, or statements that could be read "
                  "as an admission.",
         model_language="PRIVILEGED & CONFIDENTIAL — ATTORNEY-CLIENT COMMUNICATION"),
    dict(id="lh.consequences", title="Importance and consequences",
         preferred="Explains, in a neutral tone, that failing to preserve can lead to court sanctions against Acme "
                   "and to disciplinary action.",
         fallback="Explains why compliance matters without naming consequences.",
         model_language="If relevant information is lost, a court can impose sanctions on Acme, and failure to "
                        "comply with this notice may lead to disciplinary action."),
]
for _t in LH_TOPICS:
    _t.setdefault("fallback", None)
    _t.setdefault("escalate", None)
    _t.setdefault("if_missing", None)
    _t.setdefault("model_language", None)
    _t["priority"] = "High"
LH_PLAYBOOK = {"title": LH_NAME, "meta": LH_META, "intro": LH_INTRO, "topics": LH_TOPICS, "silent_topics": []}
LH_CATALOGUE = {"topics": [{"id": t["id"], "title": t["title"]} for t in LH_TOPICS]}

# ------------------------------------------------------------------ the two draft notices
HEAD = [("PRIVILEGED & CONFIDENTIAL — ATTORNEY-CLIENT COMMUNICATION", True),
        ("LEGAL HOLD NOTICE", True),
        ("To: Supply Chain Operations, Procurement and Finance team members listed at the end of this notice", False),
        ("From: Acme Company, Inc., Legal Department", False),
        ("Date: November 9, 2026", False),
        ("Re: Legal hold — Harbor Point Logistics, LLC", False)]
FACTS = ("On October 20, 2026, Acme received a letter from counsel for Harbor Point Logistics, LLC (\"Harbor Point\") "
         "alleging that Acme breached the Warehousing and Distribution Services Agreement dated March 1, 2024 (the "
         "\"Agreement\") by terminating it early and by misusing Harbor Point's shipment-routing data. Harbor Point "
         "has threatened to file suit. Acme therefore must preserve information relevant to this dispute.")
SCOPE = ("Preserve all information relating to the Harbor Point relationship, including the Agreement and its "
         "negotiation, statements of work, performance and service-level reports, invoices and payments, the "
         "termination, the use of Harbor Point routing data, communications with Harbor Point, and internal "
         "discussions about any of these.")
RECIPIENTS = "Recipients: M. Alvarez, J. Chen, R. Okafor, S. Patel, T. Lindqvist, K. Brennan."

DRAFT_A = [  # planted: 6 RED, 2 YELLOW, 5 GREEN (answer key below)
    ("1. Why you are receiving this notice", None),
    (FACTS, "lh.matter G"),
    ("We believe Harbor Point's claims are without merit and largely a negotiating tactic, and we expect to resolve "
     "this quickly on favourable terms.", "lh.privilege-content R"),
    ("2. What to preserve", None),
    (SCOPE.replace("Preserve all information", "Please preserve documents"), "lh.subject-scope G"),
    ("This applies to information from September 1, 2026 through the date of this notice.", "lh.time-period R"),
    ("This includes email and documents saved on the Supply Chain shared drive.", "lh.data-types R"),
    ("3. What to do", None),
    ("Do not delete or alter any of the documents described above. To keep things manageable, feel free to clean up "
     "duplicate copies and outdated drafts before Legal collects your files.", "lh.preserve-instruction R"),
    ("This hold overrides Acme's Records Retention Schedule; do not destroy any covered documents under the normal "
     "schedule.", "lh.overrides-retention G"),
    ("4. Other people", None),
    ("If you know of other employees, former employees or vendors who may have relevant information, please let "
     "Legal know.", "lh.other-custodians Y"),
    ("5. How long the hold lasts", None),
    ("This hold will expire on March 31, 2027 unless Legal extends it.", "lh.duration-release R"),
    ("6. Why it matters", None),
    ("Failure to preserve relevant information can result in court sanctions against Acme, and failure to comply "
     "with this notice may lead to disciplinary action.", "lh.consequences G"),
    ("7. Questions and confirmation", None),
    ("If you have questions, contact Dana Whitfield, Senior Counsel, at dana.whitfield@acme.example or "
     "(555) 010-4471.", "lh.contact G"),
    ("Please confirm receipt of this notice when convenient.", "lh.acknowledgment Y"),
    (RECIPIENTS, None),
]
# auto-deletion: absent from draft A (silent) -> RED
DRAFT_A_ABSENT = {"lh.auto-deletion": "R"}

DRAFT_B = [  # clean except the contact (Legal mailbox = fallback): 12 GREEN, 1 YELLOW
    ("1. Why you are receiving this notice", None),
    (FACTS, "lh.matter G"),
    ("This notice tells you what to keep and how. It does not ask you to collect or send anything yet.",
     "lh.privilege-content G"),
    ("2. What to preserve", None),
    (SCOPE, "lh.subject-scope G"),
    ("This hold covers information from January 1, 2024, when the parties began negotiating the Agreement, to the "
     "present, and applies to information created or received after the date of this notice.", "lh.time-period G"),
    ("This includes email, Teams and other chat messages, text and instant messages (including on personal phones "
     "used for work), voicemail, documents on network drives, OneDrive and SharePoint, business systems such as the "
     "warehouse management system, and paper files and notebooks.", "lh.data-types G"),
    ("3. What to do", None),
    ("Do not delete, discard, alter or overwrite any of this information, and do not move it or forward it to "
     "personal accounts. Keep it as it is, including drafts and duplicates.", "lh.preserve-instruction G"),
    ("Turn off any automatic deletion you control, such as mailbox clean-up rules or auto-delete settings for "
     "messages on your phone. Legal and IT are placing technical holds on company systems; do not rely on them "
     "alone.", "lh.auto-deletion G"),
    ("This hold overrides Acme's Records Retention Schedule and any routine destruction practice.",
     "lh.overrides-retention G"),
    ("4. Other people", None),
    ("If you know of anyone else, including former employees, contractors or vendors, who may have relevant "
     "information, tell Legal. If you plan to leave Acme or change roles, tell Legal first so that your devices and "
     "accounts can be preserved.", "lh.other-custodians G"),
    ("5. How long the hold lasts", None),
    ("This hold remains in effect until Legal tells you in writing that it has been released.",
     "lh.duration-release G"),
    ("6. Why it matters", None),
    ("If relevant information is lost, a court can impose sanctions on Acme, and failure to comply with this notice "
     "may lead to disciplinary action.", "lh.consequences G"),
    ("7. Questions and confirmation", None),
    ("If you have questions, write to the Legal Department at legalhold@acme.example.", "lh.contact Y"),
    ("Please confirm by November 16, 2026 that you have read this notice and will comply, by replying to this "
     "email.", "lh.acknowledgment G"),
    (RECIPIENTS, None),
]
RATING = {"G": "GREEN", "Y": "YELLOW", "R": "RED"}
WORD = {"G": "Acceptable", "Y": "Needs review", "R": "Not acceptable"}


def base_doc(title):
    d = docx.Document()
    d.styles["Normal"].font.name, d.styles["Normal"].font.size = "Calibri", Pt(11)
    d.core_properties.title, d.core_properties.author = title, "Acme Legal (synthetic POC document)"
    return d


def write_notice(path, title, body):
    d = base_doc(title)
    for text, bold in HEAD:
        p = d.add_paragraph()
        p.add_run(text).bold = bold
    for text, tag in body:
        if tag is None and text[:2].rstrip(".").isdigit():
            d.add_paragraph().add_run(text).bold = True
        else:
            d.add_paragraph(text)
    d.save(path)


def key_for(body, absent=None):
    key = {t: v for _, tag in body if tag for t, v in [tag.split()]}
    key.update(absent or {})
    missing = [t["id"] for t in LH_TOPICS if t["id"] not in key]
    assert not missing, f"answer key has no rating for {missing}"
    return key


def write_requirements(path):
    """The same playbook as a numbered requirement list (the shape Identify regulatory gaps handled in G1)."""
    name = f"{LH_NAME} (requirements)"
    d = base_doc(name)
    d.add_paragraph(name, style="Title")
    d.add_paragraph(LH_INTRO + " Each requirement below is one review topic. Version 1.0; owner: Spaarke Legal "
                    "Engineering; effective 2026-11-07; status: POC test positions, not attorney-approved.")
    for n, t in enumerate(LH_TOPICS, 1):
        p = d.add_paragraph()
        p.add_run(f"LH-{n} {t['title']}. ").bold = True
        p.add_run(f"The notice must meet this standard: {t['preferred']}")
        if t["fallback"]:
            d.add_paragraph(f"Acceptable alternative: {t['fallback']}", style="List Bullet")
        if t["escalate"]:
            d.add_paragraph(f"Not acceptable: {t['escalate']}", style="List Bullet")
        if t["if_missing"]:
            d.add_paragraph(f"If missing: {t['if_missing']}", style="List Bullet")
        if t["model_language"]:
            d.add_paragraph(f"Suggested wording: {t['model_language']}", style="List Bullet")
    d.save(path)


def write_quick_card(path):
    d = base_doc("Start here – Quick card")
    d.add_paragraph("Legal Knowledge — quick card (Word)", style="Title")
    d.add_paragraph("One habit for every document type. Version 1.0, 2026-11-07. Owner: Legal Knowledge "
                    "(POC: Spaarke). Results are drafts for attorney review.")
    d.add_heading("The habit: three steps", level=1)
    for s in ("Open your document in Word.",
              "In Copilot, attach the playbook for its document type: paperclip › Attach from Cloud › Acme Company › "
              "Documents › Legal Knowledge › [document type folder] › the playbook file at the top of the folder.",
              "Type: Review this against the attached playbook."):
        d.add_paragraph(s, style="List Number")
    d.add_paragraph("You don't need a slash command or to know which Copilot skill to use. Copilot reads the "
                    "playbook and picks the review.")
    d.add_heading("Document types", level=1)
    t = d.add_table(rows=1, cols=3)
    t.style = "Table Grid"
    for c, h in zip(t.rows[0].cells, ("Folder", "Use it for", "Playbook (top of the folder)")):
        c.text = h
        c.paragraphs[0].runs[0].bold = True
    for row in (("NDA", "Mutual NDAs. Not one-way NDAs, M&A confidentiality agreements or employee agreements.",
                 NDA_NAME),
                ("Legal Hold Notice", "A draft legal hold notice to employees, before it is sent.", LH_NAME)):
        cells = t.add_row().cells
        for c, v in zip(cells, row):
            c.text = v
    d.add_paragraph("No folder for your document type? Ask the Legal Knowledge owner. Don't use another type's "
                    "playbook.")
    d.add_heading("Optional next steps", level=1)
    for s in ("Check against a policy: type / and choose Identify regulatory gaps. When it offers laws to test "
              "against, choose Something else and name the policy, then attach it from the type's Policies folder. "
              "Attach the playbook as well if the policy refers to it.",
              "Contracts only, before signature: type / and choose Check execution readiness."):
        d.add_paragraph(s, style="List Bullet")
    d.add_heading("Good to know", level=1)
    for s in ("Attach the playbook each time rather than saving it in Copilot. A saved playbook is a copy that does "
              "not update when Legal Knowledge does. If you saved one, check that the version in its name matches "
              "the file in the folder.",
              "Edits arrive as suggestions. Nothing changes in your document until you click Apply; Apply edits with "
              "comments keeps the reasons.",
              "Ratings: Acceptable, Needs review, Not acceptable. Read every Needs review and Not acceptable item "
              "before you act on it."):
        d.add_paragraph(s, style="List Bullet")
    d.save(path)


def main():
    for p in (os.path.join(LK, "NDA", "Playbook master"), os.path.join(LK, "Legal Hold Notice"),
              os.path.join(WS, "Matters", "Harbor Point Logistics"), os.path.join(WS, "Test variants")):
        os.makedirs(p, exist_ok=True)

    # NDA v1.2: master + Word copy (X2b)
    cat = b.generate_topic_files(os.path.join(ACC, "practices", "nda"), tempfile.mkdtemp())
    sides = {t["id"]: set(t["sides"]) for t in cat["topics"] if t.get("status") != "retired"}
    src = os.path.join(KIT, "src", f"{NDA_NAME}.md")
    text = open(src, encoding="utf-8").read()
    pb = pp.parse(text, sides)
    with open(os.path.join(LK, "NDA", "Playbook master", f"{NDA_NAME}.md"), "w", encoding="utf-8") as f:
        f.write(text)
    nda_docx = os.path.join(LK, "NDA", f"{NDA_NAME}.docx")
    rw.render_table(pb, cat, nda_docx)
    problems = rw.check_table(nda_docx, pb, cat)
    assert not problems, problems

    # Legal hold notice playbook: table (primary) + requirement-list variant
    lh_docx = os.path.join(LK, "Legal Hold Notice", f"{LH_NAME}.docx")
    rw.render_table(LH_PLAYBOOK, LH_CATALOGUE, lh_docx)
    problems = rw.check_table(lh_docx, LH_PLAYBOOK, LH_CATALOGUE)
    assert not problems, problems
    write_requirements(os.path.join(WS, "Test variants", f"{LH_NAME} (requirements).docx"))

    # Drafts + answer key
    m = os.path.join(WS, "Matters", "Harbor Point Logistics")
    write_notice(os.path.join(m, "Harbor Point – Legal Hold Notice (draft A).docx"),
                 "Harbor Point – Legal Hold Notice (draft A)", DRAFT_A)
    write_notice(os.path.join(m, "Harbor Point – Legal Hold Notice (draft B).docx"),
                 "Harbor Point – Legal Hold Notice (draft B)", DRAFT_B)
    ka, kb = key_for(DRAFT_A, DRAFT_A_ABSENT), key_for(DRAFT_B)
    lines = ["# Answer key: legal hold notices (POC Act 7; never upload)", "",
             f"Playbook: {LH_NAME}. Word ratings: GREEN = Acceptable, YELLOW = Needs review, RED = Not acceptable.", "",
             "| Topic | Draft A | Draft B |", "|---|---|---|"]
    for t in LH_TOPICS:
        a, bb = ka[t["id"]], kb[t["id"]]
        lines.append(f"| {t['title']} (`{t['id']}`) | **{RATING[a]}** ({WORD[a]}) | {RATING[bb]} ({WORD[bb]}) |")
    count = lambda k, v: sum(1 for x in k.values() if x == v)  # noqa: E731
    lines += ["", f"Draft A: {count(ka, 'R')} RED, {count(ka, 'Y')} YELLOW, {count(ka, 'G')} GREEN. "
              "Automatic deletion is planted by absence (no sentence at all).",
              f"Draft B: {count(kb, 'R')} RED, {count(kb, 'Y')} YELLOW, {count(kb, 'G')} GREEN (false-positive check).",
              "", "W2 (NDA playbook on draft A): pass = Copilot says the playbook does not fit a legal hold notice, or "
              "rates the NDA topics Not relevant and makes no NDA edits. Fail = NDA positions applied to the notice.",
              "W3 (no playbook): record whether Copilot asks for a standard or improvises; improvised findings are "
              "scored against this key for interest only."]
    with open(os.path.join(POC, "answer-key", "legal-hold-answer-key.md"), "w", encoding="utf-8") as f:
        f.write("\n".join(lines) + "\n")

    write_quick_card(os.path.join(LK, "Start here – Quick card.docx"))
    with open(os.path.join(LK, "README.txt"), "w", encoding="utf-8") as f:
        f.write("Legal Knowledge - POC kit (synthetic). All companies, people and deals are fictional.\n"
                "Organised by document type (2026-11-07, make_doctype_library.py): one folder per type, with the "
                "playbook to attach at the top of the folder and supporting material in subfolders (Playbook "
                "master, Policies, Precedents, Samples, Templates, Archive). Start with 'Start here – Quick "
                "card.docx'. Test material (Inbox, Matters) is in Shared Documents/POC Workspace.\n")
    for root, _, files in os.walk(OUT):
        for fn in files:
            print(os.path.relpath(os.path.join(root, fn), OUT))


if __name__ == "__main__":
    main()
