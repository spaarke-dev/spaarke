"""make_kit.py - generate the fresh synthetic kit for the end-to-end POC (runbook.md).

All parties, people and deals are fictional. Clause wording is ours. Nothing here has been
reviewed or redlined before, so every Word and Cowork run in the POC sees new documents.

Outputs (relative to notes/poc-e2e/):
  kit/Legal Knowledge/...   upload to Acme Company > Shared Documents (drag and drop)
  kit-held/...              held back: Act 4 counterparty response, Act 5 playbook v1.1
  answer-key/...            NEVER upload: expected ratings and policy findings per document

Run from PowerShell:  C:\\Python313\\python.exe -B make_kit.py
PDFs are made afterwards by to_pdf.ps1 (Word desktop).
"""
import copy
import datetime
import difflib
import json
import os
import re
import shutil
import sys
import tempfile

import docx
from docx.enum.text import WD_ALIGN_PARAGRAPH
from docx.oxml import OxmlElement
from docx.oxml.ns import qn
from docx.shared import Inches, Pt

HERE = os.path.dirname(os.path.abspath(__file__))
POC = os.path.dirname(HERE)
REPO = os.path.abspath(os.path.join(POC, "..", "..", "..", ".."))
ACC = os.path.join(REPO, "src", "accelerators")
KIT = os.path.join(POC, "kit", "Legal Knowledge")
HELD = os.path.join(POC, "kit-held")
KEY = os.path.join(POC, "answer-key")

ACME = {"name": "Acme Company, Inc.", "short": "Acme", "state": "Delaware", "type": "corporation",
        "signer": "Jordan Ellis", "title": "Vice President, Operations",
        "address": "400 Market Street, Suite 1200, Wilmington, DE 19801"}

# --------------------------------------------------------------------------------------------
# Clause library: topic -> variant code -> (text or None, expected rating)
# G = preferred (GREEN), Y = acceptable fallback (YELLOW), Y2 = other deviation (YELLOW),
# R = prohibited / escalate (RED), S = clause absent (rating depends on the topic), NC = non-compete
# Placeholders: {acme} {cp} {cp_state}
# --------------------------------------------------------------------------------------------
CI = '"Confidential Information" means all non-public information disclosed by or on behalf of a party (the "Disclosing Party") to the other party (the "Receiving Party") in connection with the Purpose, in any form, '
L = {
    "nda.definition-unmarked": {
        "G": (CI + "that is identified as confidential or that a reasonable person would understand to be confidential given the nature of the information and the circumstances of its disclosure.", "GREEN"),
        "Y": (CI + 'that is marked "Confidential" or with a similar legend or, if disclosed without a marking, that the Disclosing Party designates as confidential in a written notice delivered within thirty (30) days after disclosure.', "YELLOW"),
        "R": (CI + 'but only to the extent it is clearly marked "Confidential" or "Proprietary" at the time of disclosure. Information that is not so marked is not Confidential Information.', "RED"),
    },
    "nda.oral-disclosure": {
        "G": ("Information disclosed orally or visually is Confidential Information if it is identified as confidential when disclosed or would reasonably be understood to be confidential.", "GREEN"),
        "Y": ("Information disclosed orally or visually is Confidential Information only if the Disclosing Party summarizes it in a writing marked as confidential and delivers that summary within thirty (30) days after disclosure.", "YELLOW"),
        "R": ("Information disclosed orally or visually is not Confidential Information.", "RED"),
    },
    "nda.exclusion-prior-possession": {
        "G": ("(a) was known to the Receiving Party without a duty of confidentiality before its disclosure, as shown by the Receiving Party's written records;", "GREEN"),
        "Y": ("(a) was already known to the Receiving Party before its disclosure;", "YELLOW"),
    },
    "nda.exclusion-third-party": {
        "G": ("(c) is rightfully received by the Receiving Party from a third party without a duty of confidentiality; or", "GREEN"),
        "Y": ("(c) is received by the Receiving Party from a third party without a breach of any obligation known to the Receiving Party; or", "YELLOW"),
    },
    "nda.exclusion-independent-development": {
        "G": ("(d) is independently developed by the Receiving Party without use of or reference to the Disclosing Party's Confidential Information.", "GREEN"),
        "R": ("(d) is independently developed by or for the Receiving Party.", "RED"),
    },
    "nda.one-sided-carveouts": {
        "G": (None, "GREEN"),
        "Y": ("Neither party's aggregated industry benchmarking data that does not identify the other party is Confidential Information of the other party.", "YELLOW"),
        "R": ("Notwithstanding anything to the contrary, any business requirements, specifications, feedback or suggestions that {acme} provides about {cp}'s products or services are not Confidential Information.", "RED"),
    },
    "nda.purpose-limited-use": {
        "G": ("The Receiving Party will use Confidential Information solely for the Purpose and will protect it using at least the degree of care it uses to protect its own information of a similar nature, and in no event less than reasonable care.", "GREEN"),
        "R": ("The Receiving Party may use Confidential Information for any lawful purpose, provided that it does not disclose Confidential Information except as permitted by this Agreement.", "RED"),
    },
    "nda.representatives": {
        "G": ('The Receiving Party may disclose Confidential Information only to its directors, officers, employees, contractors and professional advisers who need to know it for the Purpose ("Representatives") and who are bound by confidentiality obligations at least as protective as those in this Agreement. Each party is responsible for any breach of this Agreement by its Representatives.', "GREEN"),
        "Y": ('The Receiving Party may disclose Confidential Information only to its directors, officers, employees, contractors and professional advisers who need to know it for the Purpose ("Representatives") and who are bound by written or professional obligations of confidentiality.', "YELLOW"),
        "R": ('The Receiving Party may disclose Confidential Information to its directors, officers, employees, contractors and professional advisers who need to know it for the Purpose ("Representatives"). The Receiving Party has no liability for any act or omission of its Representatives.', "RED"),
    },
    "nda.copying-and-breach-notice": {
        "G": ("The Receiving Party will make copies of Confidential Information only as needed for the Purpose, will keep all proprietary notices on those copies, and will promptly notify the Disclosing Party of any unauthorized use or disclosure of Confidential Information of which it becomes aware.", "GREEN"),
        "Y": ("The Receiving Party will promptly notify the Disclosing Party of any unauthorized disclosure of Confidential Information of which it becomes aware.", "YELLOW"),
        "S": (None, "YELLOW"),
    },
    "nda.reverse-engineering": {
        "G": ("The Receiving Party will not reverse engineer, decompile, disassemble or analyze the composition of any prototype, sample, software or other tangible material provided by the Disclosing Party.", "GREEN"),
        "S": (None, "RED"),
    },
    "nda.data-use-licence-back": {
        "G": ("Neither party will use the other party's Confidential Information, or any data derived from it, for any purpose other than the Purpose.", "GREEN"),
        "Y": ("Neither party will use the other party's Confidential Information, or any data derived from it, for any purpose other than the Purpose, except that each party may use aggregated data that does not identify the other party or reveal its Confidential Information solely to improve the services it provides to the other party.", "YELLOW"),
        "R": ("Each party may use de-identified, anonymized or aggregated data derived from the other party's Confidential Information to develop and improve its products and services and for its other business purposes.", "RED"),
    },
    "nda.compelled-disclosure": {
        "G": ("If the Receiving Party is required by law, regulation or court order to disclose Confidential Information, it will, to the extent legally permitted, give the Disclosing Party prompt written notice, reasonably cooperate, at the Disclosing Party's expense, with any effort to obtain a protective order, and disclose only the portion of the Confidential Information that it is legally required to disclose.", "GREEN"),
        "Y": ("If the Receiving Party is required by law, regulation or court order to disclose Confidential Information, it will give the Disclosing Party notice of the requirement where legally permitted.", "YELLOW"),
        "R": ("The Receiving Party may disclose Confidential Information where it determines, in its reasonable judgment, that disclosure is required by law, regulation or legal process.", "RED"),
    },
    "nda.no-licence-no-warranty": {
        "G": ('No licence under any patent, copyright, trade secret or other intellectual property right is granted by this Agreement, except the limited right to use Confidential Information for the Purpose. All Confidential Information is provided "as is", without any warranty of any kind.', "GREEN"),
        "R": ("No licence under any patent, copyright, trade secret or other intellectual property right is granted by this Agreement, except the limited right to use Confidential Information for the Purpose. The Disclosing Party warrants that its Confidential Information is accurate and complete.", "RED"),
    },
    "nda.return-destruction": {
        "G": ("Within thirty (30) days after the Disclosing Party's written request, or after this Agreement terminates, the Receiving Party will return or destroy the Disclosing Party's Confidential Information and, on request, certify in writing that it has done so. The Receiving Party may keep copies in routine backup systems or as required by law, and those copies remain subject to this Agreement.", "GREEN"),
        "Y": ("On the Disclosing Party's written request, the Receiving Party will return or destroy the Disclosing Party's Confidential Information.", "YELLOW"),
        "R": ("The Receiving Party is not required to return or destroy Confidential Information.", "RED"),
    },
    "nda.residuals": {
        "G": (None, "GREEN"),
        "R": ('Nothing in this Agreement restricts either party from using ideas, concepts, know-how or techniques of a general nature retained in the unaided memory of its personnel who had access to Confidential Information ("Residuals"). A person\'s memory is unaided if the person has not intentionally memorized the information for the purpose of retaining and later using it.', "RED"),
    },
    "nda.term": {
        "G": ("This Agreement covers disclosures made during the two (2) years following the Effective Date. The Receiving Party's obligations continue for three (3) years after each disclosure, provided that obligations with respect to trade secrets continue for as long as the information remains a trade secret under applicable law.", "GREEN"),
        "Y": ("This Agreement covers disclosures made during the two (2) years following the Effective Date. The Receiving Party's obligations continue for two (2) years after each disclosure, provided that obligations with respect to trade secrets continue for as long as the information remains a trade secret under applicable law.", "YELLOW"),
        "Y2": ("This Agreement covers disclosures made during the two (2) years following the Effective Date. The Receiving Party's obligations continue for five (5) years after each disclosure, provided that obligations with respect to trade secrets continue for as long as the information remains a trade secret under applicable law.", "YELLOW"),
        "R": ("This Agreement, and all of the Receiving Party's obligations under it, terminate one (1) year after the Effective Date.", "RED"),
    },
    "nda.non-solicit": {
        "G": (None, "GREEN"),
        "Y": ("For twelve (12) months after the Effective Date, neither party will solicit for employment any employee of the other party with whom it had contact in connection with the Purpose. General solicitations not targeted at those employees, and hiring anyone who responds to them, do not breach this section.", "YELLOW"),
        "R": ("For twenty-four (24) months after the Effective Date, neither party will directly or indirectly solicit, recruit or hire any employee of the other party.", "RED"),
        "NC": ("During the term of this Agreement and for one (1) year afterwards, {acme} will not develop, market or sell any product or service that competes with the {cp} products or services discussed under this Agreement.", "RED"),
    },
    "nda.remedies": {
        "G": ("Each party acknowledges that a breach of this Agreement may cause irreparable harm for which monetary damages would not be an adequate remedy, and that the non-breaching party may seek injunctive relief in addition to any other remedy available to it.", "GREEN"),
        "R": ("Each party's total liability arising out of or relating to this Agreement will not exceed one thousand dollars ($1,000), and each party waives any right to seek injunctive or other equitable relief.", "RED"),
    },
    "nda.whistleblower-dtsa": {
        "G": ("Nothing in this Agreement prevents either party or its personnel from reporting a possible violation of law to a government agency. Notice under 18 U.S.C. 1833(b): an individual will not be held criminally or civilly liable under any federal or state trade secret law for disclosing a trade secret in confidence to a government official or to an attorney solely for the purpose of reporting or investigating a suspected violation of law, or in a complaint or other document filed under seal in a lawsuit or other proceeding.", "GREEN"),
        "R": ("The Receiving Party will not disclose Confidential Information to any governmental authority without the Disclosing Party's prior written consent.", "RED"),
        "S": (None, "YELLOW"),
    },
    "nda.trade-secret-preservation": {
        "G": ("Nothing in this Agreement limits either party's rights or remedies under applicable trade secret law.", "GREEN"),
        "R": ("This Agreement states the parties' entire obligations regarding Confidential Information, and each party disclaims any obligation of confidentiality implied by law.", "RED"),
        "S": (None, "YELLOW"),
    },
    "nda.confidentiality-of-agreement": {
        "G": ("The existence and terms of this Agreement and of the parties' discussions are Confidential Information of both parties.", "GREEN"),
        "S": (None, "YELLOW"),
    },
    "nda.assignment": {
        "G": ("Neither party may assign this Agreement without the other party's prior written consent, except to a successor to all or substantially all of the business to which this Agreement relates that assumes this Agreement in writing.", "GREEN"),
        "Y": ("Neither party may assign this Agreement without the other party's prior written consent, which will not be unreasonably withheld.", "YELLOW"),
        "R": ("Either party may assign this Agreement without the other party's consent.", "RED"),
    },
    "nda.governing-law-venue": {
        "G": ("This Agreement is governed by the laws of the State of Delaware, without regard to its conflict-of-laws rules. The state and federal courts located in Delaware have non-exclusive jurisdiction over any dispute arising out of or relating to this Agreement.", "GREEN"),
        "Y": ("This Agreement is governed by the laws of the State of {cp_state}, without regard to its conflict-of-laws rules. The state and federal courts located in {cp_state} have non-exclusive jurisdiction over any dispute arising out of or relating to this Agreement.", "YELLOW"),
        "R": ("This Agreement is governed by the laws of England and Wales. The courts of London, England have exclusive jurisdiction over any dispute arising out of or relating to this Agreement.", "RED"),
    },
    # housekeeping is handled in the preamble and signature block, not as a clause
    "nda.housekeeping": {"G": (None, "GREEN"), "Y": (None, "YELLOW"), "R": (None, "RED")},
}

TOPIC_ORDER = list(L)

# Section layout: (heading, [topic ids in order]); "exclusions" is special-cased
SECTIONS = [
    ("Confidential Information", ["nda.definition-unmarked", "nda.oral-disclosure"]),
    ("Exclusions", ["EXCLUSIONS", "nda.one-sided-carveouts"]),
    ("Use and Protection", ["nda.purpose-limited-use", "nda.representatives", "nda.copying-and-breach-notice",
                            "nda.reverse-engineering", "nda.data-use-licence-back"]),
    ("Compelled Disclosure", ["nda.compelled-disclosure"]),
    ("No Licence; No Warranty", ["nda.no-licence-no-warranty"]),
    ("Return or Destruction", ["nda.return-destruction"]),
    ("Residuals", ["nda.residuals"]),
    ("Term", ["nda.term"]),
    ("Non-Solicitation", ["nda.non-solicit"]),
    ("Remedies", ["nda.remedies"]),
    ("Protected Reporting", ["nda.whistleblower-dtsa"]),
    ("Trade Secrets", ["nda.trade-secret-preservation"]),
    ("General", ["nda.confidentiality-of-agreement", "nda.assignment", "nda.governing-law-venue", "BOILERPLATE"]),
]
PUBLIC = "(b) is or becomes generally available to the public through no fault of the Receiving Party;"
BOILER = ("This Agreement is the parties' entire agreement on its subject and supersedes all prior discussions on it. "
          "It may be amended only in a writing signed by both parties and may be signed in counterparts, including by electronic signature.")

# --------------------------------------------------------------------------------------------
# Policy (normative grounding). Mapping of (topic, code) -> requirement ids it conflicts with
# --------------------------------------------------------------------------------------------
POLICY_HITS = {
    ("nda.term", "Y2"): ["NDA-3.1"], ("nda.term", "R"): ["NDA-3.2"],
    ("nda.trade-secret-preservation", "R"): ["NDA-3.2"],
    ("nda.non-solicit", "Y"): ["NDA-3.3"], ("nda.non-solicit", "R"): ["NDA-3.3"], ("nda.non-solicit", "NC"): ["NDA-3.3"],
    ("nda.compelled-disclosure", "R"): ["NDA-3.4"],
    ("nda.residuals", "R"): ["NDA-3.5"],
    ("nda.governing-law-venue", "R"): ["NDA-3.6"],
    ("nda.remedies", "R"): ["NDA-3.7"],
    ("nda.whistleblower-dtsa", "R"): ["NDA-3.8"],
}
GC_ITEMS = {("nda.non-solicit", "NC"), ("nda.residuals", "R"), ("nda.governing-law-venue", "R"), ("nda.remedies", "R")}


def approval_level(variants):
    """NDA-4: which approval the policy requires before signature."""
    if any((t, c) in GC_ITEMS for t, c in variants.items()):
        return "NDA-4.3 General Counsel approval"
    if any(L[t][c][1] != "GREEN" for t, c in variants.items()):
        return "NDA-4.2 Legal approval, then signature by a Vice President or above"
    return "NDA-4.1 Director or above may sign"


# --------------------------------------------------------------------------------------------
# Document assembly
# --------------------------------------------------------------------------------------------
def full_variants(overrides):
    v = {t: "G" for t in TOPIC_ORDER}
    v.update(overrides)
    return v


def text_of(topic, code, ctx):
    t = L[topic][code][0]
    return t.format(**ctx) if t else None


def blocks(variants, ctx):
    """Return [(kind, key, text)] for the agreement body. Every topic yields a block, text None if absent."""
    out = []
    for heading, items in SECTIONS:
        sec = []
        for it in items:
            if it == "EXCLUSIONS":
                sec.append(("p", "excl-intro", "Confidential Information does not include information that:"))
                sec.append(("p", "nda.exclusion-prior-possession", text_of("nda.exclusion-prior-possession", variants["nda.exclusion-prior-possession"], ctx)))
                sec.append(("p", "excl-public", PUBLIC))
                sec.append(("p", "nda.exclusion-third-party", text_of("nda.exclusion-third-party", variants["nda.exclusion-third-party"], ctx)))
                sec.append(("p", "nda.exclusion-independent-development", text_of("nda.exclusion-independent-development", variants["nda.exclusion-independent-development"], ctx)))
            elif it == "BOILERPLATE":
                sec.append(("p", "boiler", BOILER))
            else:
                sec.append(("p", it, text_of(it, variants[it], ctx)))
        out.append(("h", heading, sec))
    return out


TOKEN = re.compile(r"\s+|[^\s]+")


def diff_segments(old, new):
    a, b = TOKEN.findall(old or ""), TOKEN.findall(new or "")
    segs = []
    for op, i1, i2, j1, j2 in difflib.SequenceMatcher(None, a, b, autojunk=False).get_opcodes():
        if op == "equal":
            segs.append(("t", "".join(a[i1:i2])))
        if op in ("replace", "delete"):
            segs.append(("del", "".join(a[i1:i2])))
        if op in ("replace", "insert"):
            segs.append(("ins", "".join(b[j1:j2])))
    return segs


class Tracker:
    def __init__(self, author, date):
        self.author, self.date, self.n = author, date, 1000

    def _el(self, tag):
        e = OxmlElement(tag)
        e.set(qn("w:id"), str(self.n))
        e.set(qn("w:author"), self.author)
        e.set(qn("w:date"), self.date)
        self.n += 1
        return e

    def write(self, par, segs, bold=False):
        for kind, text in segs:
            if not text:
                continue
            if kind == "t":
                par.add_run(text).bold = bold or None
                continue
            r = OxmlElement("w:r")
            if bold:
                rpr = OxmlElement("w:rPr")
                rpr.append(OxmlElement("w:b"))
                r.append(rpr)
            t = OxmlElement("w:delText" if kind == "del" else "w:t")
            t.set(qn("xml:space"), "preserve")
            t.text = text
            r.append(t)
            w = self._el("w:del" if kind == "del" else "w:ins")
            w.append(r)
            par._p.append(w)


def style_doc(d, font, size):
    st = d.styles["Normal"]
    st.font.name, st.font.size = font, Pt(size)
    st.element.rPr.rFonts.set(qn("w:eastAsia"), font)
    for s in d.sections:
        s.left_margin = s.right_margin = Inches(1)


def party_line(p):
    return f'{p["name"]}, a {p["state"]} {p["type"]} with offices at {p["address"]} ("{p["short"]}")'


def build_nda(path, *, first, second, title, purpose, base, new=None, tracker=None, date_text, font="Calibri",
              size=11, heading_style="numbered", executed=False, comments=None, wrong_signer=None):
    """Write one NDA. base/new are full variant dicts. With new + tracker, differences are tracked changes."""
    ctx = {"acme": ACME["short"], "cp": (second if first is ACME else first)["short"],
           "cp_state": (second if first is ACME else first)["state"]}
    d = docx.Document()
    style_doc(d, font, size)
    d.core_properties.title = title
    d.core_properties.author = first["short"]
    h = d.add_paragraph()
    h.alignment = WD_ALIGN_PARAGRAPH.CENTER
    r = h.add_run(title.upper())
    r.bold = True
    r.font.size = Pt(size + 3)
    pre = d.add_paragraph()
    pre.add_run(f'This {title} (the "Agreement") is entered into as of {date_text} (the "Effective Date") between '
                f'{party_line(first)}, and {party_line(second)}. Each of them is a "party".')
    num = 1
    p = d.add_paragraph()
    hd = p.add_run(f"{num}. Purpose. " if heading_style == "numbered" else f"Section {num} - Purpose. ")
    hd.bold = True
    p.add_run(f'The parties wish to exchange information in order to {purpose} (the "Purpose").')
    newb = blocks(new, ctx) if new else None
    for si, (_, heading, sec) in enumerate(blocks(base, ctx)):
        nsec = newb[si][2] if newb else sec
        if all(t is None for (_, _, t) in sec) and all(t is None for (_, _, t) in nsec):
            continue
        num += 1
        sub = 0
        hp = d.add_paragraph()
        label = f"{num}. {heading}." if heading_style == "numbered" else f"Section {num} - {heading}."
        sec_new_only = all(t is None for (_, _, t) in sec)
        if sec_new_only and tracker:
            tracker.write(hp, [("ins", label)], bold=True)
        else:
            hp.add_run(label).bold = True
        for (_, key, old), (_, _, nt) in zip(sec, nsec):
            if old is None and nt is None:
                continue
            para = d.add_paragraph()
            para.paragraph_format.left_indent = Inches(0.3)
            lead = ""
            if not key.startswith(("nda.exclusion", "excl-public")):
                sub += 1
                lead = f"{num}.{sub} "
            if newb and old != nt and tracker:
                if old is None:      # whole paragraph inserted: the number is part of the insertion
                    tracker.write(para, [("ins", lead + nt)])
                elif nt is None:     # whole paragraph deleted
                    tracker.write(para, [("del", lead + old)])
                else:
                    tracker.write(para, [("t", lead)] + diff_segments(old, nt))
                if comments and key in comments:
                    try:
                        anchor = para.runs[0] if para.runs else para.add_run("")
                        d.add_comment(anchor, text=comments[key], author=tracker.author, initials="AL")
                    except Exception as ex:  # comments are a nice-to-have
                        print("  (comment skipped:", ex, ")")
            else:
                para.add_run(lead + old)
    # signature block
    d.add_paragraph()
    d.add_paragraph("The parties have signed this Agreement as of the Effective Date.")
    tbl = d.add_table(rows=5, cols=2)
    for col, party in enumerate((first, second)):
        name = wrong_signer if (wrong_signer and party is not ACME and col == 1) else party["name"]
        cells = [name.upper(), "By: " + (f"/s/ {party['signer']}" if executed else "______________________"),
                 f"Name: {party['signer']}", f"Title: {party['title']}",
                 "Date: " + (date_text if executed else "______________")]
        for row, val in enumerate(cells):
            tbl.cell(row, col).text = val
    os.makedirs(os.path.dirname(path), exist_ok=True)
    d.save(path)


# --------------------------------------------------------------------------------------------
# Counterparties (all fictional)
# --------------------------------------------------------------------------------------------
def cp(name, short, state, signer, title, address, typ="corporation"):
    return {"name": name, "short": short, "state": state, "type": typ, "signer": signer, "title": title, "address": address}


BRIGHT = cp("Brightwater Systems, Inc.", "Brightwater", "California", "Priya Raman", "General Counsel",
            "1180 Harbor Boulevard, Oakland, CA 94607")

BATCH = [
    ("b01", cp("Kestrel Ridge Analytics, LLC", "Kestrel", "Colorado", "Owen Marsh", "Chief Operating Officer", "2200 Blake Street, Denver, CO 80205", "limited liability company"),
     "evaluate a potential data analytics engagement", {}, "Mutual Non-Disclosure Agreement"),
    ("b02", cp("Ninefold Robotics, Inc.", "Ninefold", "Massachusetts", "Lena Kowalski", "VP Business Development", "75 Fargo Street, Boston, MA 02210"),
     "evaluate a potential joint development of warehouse robotics", {"nda.definition-unmarked": "Y", "nda.oral-disclosure": "Y", "nda.return-destruction": "Y", "nda.governing-law-venue": "Y"}, "Mutual Confidentiality Agreement"),
    ("b03", cp("Larkspur Health Data, Inc.", "Larkspur", "Minnesota", "Marcus Webb", "Chief Executive Officer", "300 South 6th Street, Minneapolis, MN 55402"),
     "evaluate a potential data-sharing pilot", {"nda.data-use-licence-back": "R", "nda.whistleblower-dtsa": "R", "nda.confidentiality-of-agreement": "S"}, "Mutual Non-Disclosure Agreement"),
    ("b04", cp("Tidewell Logistics Corp.", "Tidewell", "Georgia", "Hannah Ortiz", "SVP Procurement", "1050 Peachtree Street, Atlanta, GA 30309"),
     "evaluate a potential logistics services relationship", {"nda.residuals": "R", "nda.term": "Y2"}, "Mutual Nondisclosure Agreement"),
    ("b05", cp("Quillon Software, Inc.", "Quillon", "Washington", "Ravi Desai", "VP Partnerships", "500 Union Street, Seattle, WA 98101"),
     "evaluate a potential software integration, including access to pre-release software builds", {"nda.exclusion-independent-development": "R", "nda.reverse-engineering": "S"}, "Mutual Non-Disclosure Agreement"),
    ("b06", cp("Marrow & Finch Consulting LLC", "Marrow & Finch", "New York", "Claire Dunham", "Managing Partner", "120 Broadway, New York, NY 10271", "limited liability company"),
     "evaluate a potential operations consulting engagement", {"nda.non-solicit": "R", "nda.representatives": "R"}, "Mutual Confidentiality Agreement"),
    ("b07", cp("Bluefield Materials, Inc.", "Bluefield", "Ohio", "Tom Albrecht", "President", "88 East Broad Street, Columbus, OH 43215"),
     "evaluate a potential supply of composite materials", {"nda.non-solicit": "NC", "nda.trade-secret-preservation": "R"}, "Mutual Non-Disclosure Agreement"),
    ("b08", cp("Saltmarsh Energy Partners, LP", "Saltmarsh", "Texas", "Diego Fuentes", "Chief Commercial Officer", "700 Louisiana Street, Houston, TX 77002", "limited partnership"),
     "evaluate a potential energy procurement arrangement", {"nda.governing-law-venue": "R", "nda.remedies": "R"}, "Mutual Non-Disclosure Agreement"),
    ("b09", cp("Cobaltline Semiconductor, Inc.", "Cobaltline", "Arizona", "Grace Liu", "Director, Strategic Sourcing", "1 North Central Avenue, Phoenix, AZ 85004"),
     "evaluate a potential component sourcing relationship", {"nda.definition-unmarked": "R", "nda.oral-disclosure": "R"}, "Mutual Nondisclosure Agreement"),
    ("b10", cp("Verdant Loop Foods, Inc.", "Verdant Loop", "Oregon", "Sam Okafor", "VP Operations", "925 NW Davis Street, Portland, OR 97209"),
     "evaluate a potential co-packing arrangement", {"nda.term": "R", "nda.return-destruction": "R"}, "Mutual Confidentiality Agreement"),
    ("b11", cp("Harborlight Media, LLC", "Harborlight", "Florida", "Nina Castellano", "Head of Partnerships", "200 South Biscayne Boulevard, Miami, FL 33131", "limited liability company"),
     "evaluate a potential co-marketing campaign", {"nda.one-sided-carveouts": "R", "nda.compelled-disclosure": "R"}, "Mutual Non-Disclosure Agreement"),
    ("b12", cp("Ironvale Medical Devices, Inc.", "Ironvale", "Utah", "Ethan Brooks", "Chief Technology Officer", "222 South Main Street, Salt Lake City, UT 84101"),
     "evaluate a potential device distribution relationship", {"nda.purpose-limited-use": "R", "nda.no-licence-no-warranty": "R"}, "Mutual Non-Disclosure Agreement"),
    ("b13", cp("Petrichor AI, Inc.", "Petrichor", "California", "Maya Lindqvist", "VP Legal", "600 California Street, San Francisco, CA 94108"),
     "evaluate a potential machine-learning services engagement", {"nda.data-use-licence-back": "Y", "nda.non-solicit": "Y", "nda.copying-and-breach-notice": "Y"}, "Mutual Non-Disclosure Agreement"),
    ("b14", cp("Glasswing Security, Inc.", "Glasswing", "Virginia", "Victor Hale", "Chief Revenue Officer", "1750 Tysons Boulevard, McLean, VA 22102"),
     "evaluate a potential managed security services relationship", {"nda.housekeeping": "R", "nda.assignment": "R"}, "Mutual Nondisclosure Agreement"),
    ("b15", cp("Northbeam Payments, Inc.", "Northbeam", "Illinois", "Aisha Grant", "Director, Partnerships", "233 South Wacker Drive, Chicago, IL 60606"),
     "evaluate a potential payments processing integration", {"nda.exclusion-prior-possession": "Y", "nda.exclusion-third-party": "Y", "nda.assignment": "Y", "nda.whistleblower-dtsa": "S"}, "Mutual Non-Disclosure Agreement"),
    ("b16", cp("Oakhollow Pharmaceuticals, Inc.", "Oakhollow", "New Jersey", "Robert Klein", "SVP Business Development", "100 Overlook Center, Princeton, NJ 08540"),
     "evaluate a potential clinical supply collaboration", {"nda.residuals": "R", "nda.data-use-licence-back": "R", "nda.term": "Y2", "nda.governing-law-venue": "Y",
                                                           "nda.non-solicit": "Y", "nda.compelled-disclosure": "Y", "nda.representatives": "Y"}, "Mutual Confidentiality Agreement"),
]
BATCH_PDF = {"b14", "b15", "b16"}
FONTS = [("Calibri", 11, "numbered"), ("Times New Roman", 12, "section"), ("Arial", 10.5, "numbered"), ("Georgia", 11, "section")]

MATTER = {"nda.definition-unmarked": "Y", "nda.exclusion-independent-development": "R", "nda.one-sided-carveouts": "R",
          "nda.representatives": "Y", "nda.compelled-disclosure": "R", "nda.reverse-engineering": "S",
          "nda.data-use-licence-back": "R", "nda.return-destruction": "Y", "nda.residuals": "R", "nda.term": "Y",
          "nda.non-solicit": "Y", "nda.governing-law-venue": "Y", "nda.housekeeping": "Y"}
MATTER_PURPOSE = ("evaluate a potential integration of Brightwater's industrial sensor platform, including prototype "
                  "sensor hardware and pre-release firmware, with Acme's asset-monitoring products")

PRECEDENTS = [
    ("Acme - Halcyon Grid Technologies Mutual NDA (executed 2025-03-12)", cp("Halcyon Grid Technologies, Inc.", "Halcyon", "Nevada", "Iris Novak", "VP Engineering", "50 West Liberty Street, Reno, NV 89501"), "March 12, 2025", {}),
    ("Acme - Rookwood Instruments Mutual NDA (executed 2025-06-02)", cp("Rookwood Instruments, Inc.", "Rookwood", "Pennsylvania", "Daniel Price", "Chief Executive Officer", "1500 Market Street, Philadelphia, PA 19102"), "June 2, 2025", {}),
    ("Acme - Silverpine Data Mutual NDA (executed 2025-09-18)", cp("Silverpine Data Corp.", "Silverpine", "North Carolina", "Kara Jensen", "General Counsel", "150 Fayetteville Street, Raleigh, NC 27601"), "September 18, 2025", {"nda.term": "Y"}),
    ("Acme - Thornbury Labs Mutual NDA (executed 2026-01-27)", cp("Thornbury Labs, Inc.", "Thornbury", "Maryland", "Paul Whitaker", "President", "100 Light Street, Baltimore, MD 21202"), "January 27, 2026", {"nda.non-solicit": "Y"}),
]
FERNHILL = cp("Fernhill Instruments, Inc.", "Fernhill", "Michigan", "Laura Benton", "Director, Contracts", "150 West Jefferson Avenue, Detroit, MI 48226")
FERNHILL_BASE = {"nda.residuals": "R", "nda.data-use-licence-back": "R", "nda.term": "R", "nda.compelled-disclosure": "R", "nda.exclusion-independent-development": "R"}
FERNHILL_COMMENTS = {
    "nda.residuals": "Acme does not accept residuals clauses in NDAs. Deleted.",
    "nda.data-use-licence-back": "Derived-data use for general product development is not acceptable. Limited to the Purpose.",
    "nda.term": "Acme standard: 3 years after each disclosure, trade secrets for as long as they qualify.",
    "nda.compelled-disclosure": "Notice and cooperation are required so the owner can seek a protective order.",
    "nda.exclusion-independent-development": "Independent development must be without use of or reference to the other party's information.",
}
# Act 4: Brightwater's response to an Acme-revised draft (base = what Acme sent back; new = Brightwater's changes)
RESPONSE_BASE = {"nda.reverse-engineering": "G", "nda.governing-law-venue": "G"}
RESPONSE_NEW = {"nda.residuals": "R", "nda.term": "Y", "nda.data-use-licence-back": "Y", "nda.governing-law-venue": "Y"}


def expected_row(variants):
    rows = {t: {"variant": c, "rating": L[t][c][1]} for t, c in variants.items()}
    hits = sorted({h for t, c in variants.items() for h in POLICY_HITS.get((t, c), [])})
    return {"topics": rows, "policy_findings": hits, "approval": approval_level(variants)}


# --------------------------------------------------------------------------------------------
# Other kit documents
# --------------------------------------------------------------------------------------------
def write_policy(path):
    d = docx.Document()
    style_doc(d, "Calibri", 11)
    d.core_properties.title = "Acme NDA Approval and Signature Authority Policy v1.0"
    d.add_heading("Acme NDA Approval and Signature Authority Policy", level=1)
    d.add_paragraph("Version 1.0 | Effective: October 1, 2026 | Owner: Acme Legal Department | Jurisdiction: United States | Status: In force")
    rows = [
        ("1", "Scope", None),
        ("NDA-1.1", "This Policy applies to every non-disclosure or confidentiality agreement that Acme Company, Inc. or any of its subsidiaries enters into with a third party for commercial discussions.", None),
        ("NDA-1.2", "This Policy does not apply to confidentiality agreements for mergers or acquisitions, or to employee confidentiality agreements. Those are governed by separate policies.", None),
        ("2", "Definitions", None),
        ("NDA-2.1", "\"Deviation\" means any term that differs from the Acme NDA playbook currently published by the Legal Department.", None),
        ("3", "Required and prohibited terms", None),
        ("NDA-3.1", "The confidentiality period must not exceed three (3) years after each disclosure, except for trade secrets.", None),
        ("NDA-3.2", "Trade secrets must remain protected for as long as they qualify as trade secrets under applicable law. An NDA must not end trade secret protection at a fixed date or disclaim obligations implied by law.", None),
        ("NDA-3.3", "An NDA must not contain a non-solicitation or non-competition obligation unless the General Counsel approves it in writing.", None),
        ("NDA-3.4", "An NDA must require the receiving party to give notice of a legally compelled disclosure where notice is legally permitted.", None),
        ("NDA-3.5", "An NDA must not contain a residuals clause.", None),
        ("NDA-3.6", "The governing law must be the law of a U.S. state. Any other governing law requires General Counsel approval.", None),
        ("NDA-3.7", "An NDA must not cap liability for breach of confidentiality or waive the right to seek injunctive relief.", None),
        ("NDA-3.8", "An NDA must not restrict either party from reporting possible violations of law to a government agency.", None),
        ("4", "Signature authority", None),
        ("NDA-4.1", "An NDA with no Deviation may be signed by a Director or above.", None),
        ("NDA-4.2", "An NDA with any Deviation requires Legal Department approval before signature, and must be signed by a Vice President or above.", None),
        ("NDA-4.3", "An NDA that contains a non-competition obligation, a residuals clause, a non-U.S. governing law or a liability cap requires General Counsel approval before signature.", None),
        ("5", "Records", None),
        ("NDA-5.1", "Every executed NDA must be stored in the Acme contract repository within five (5) business days after signature.", None),
    ]
    for rid, text, _ in rows:
        if "." not in rid:
            d.add_heading(f"{rid}. {text}", level=2)
        else:
            p = d.add_paragraph()
            p.add_run(rid + "  ").bold = True
            p.add_run(text)
    d.save(path)


def write_memo_template(path):
    d = docx.Document()
    style_doc(d, "Calibri", 11)
    d.core_properties.title = "Acme Legal Memo Template v1.0"
    d.add_paragraph("PRIVILEGED AND CONFIDENTIAL | ATTORNEY-CLIENT COMMUNICATION").runs[0].bold = True
    d.add_heading("MEMORANDUM", level=1)
    for label in ("To:", "From:", "Date:", "Re:"):
        p = d.add_paragraph()
        p.add_run(label + " ").bold = True
        p.add_run("[ ]")
    for h, guide in [
        ("1. Bottom line", "Two or three sentences a business reader can act on. State the recommendation first."),
        ("2. Question", "The single question this memo answers, for the named business owner."),
        ("3. Background", "Only the facts needed for the analysis, each with its source document."),
        ("4. Analysis", "One short subsection per issue: what the document says (cited), what our standard is, the gap, the options."),
        ("5. Recommendation and approvals", "What to do, who must approve under the NDA policy, and by when."),
        ("6. Open items", "A table: item | owner | due date."),
    ]:
        d.add_heading(h, level=2)
        d.add_paragraph(guide).runs[0].italic = True
    d.add_paragraph("House style: plain English, no Latin, short paragraphs, defined terms as in the agreement, "
                    "every statement about a document cites its section. Mark every draft \"Draft for counsel review\".")
    d.save(path)


def write_columns(path):
    d = docx.Document()
    style_doc(d, "Calibri", 11)
    d.core_properties.title = "NDA Key Terms Columns v1.0"
    d.add_heading("NDA Key Terms Columns v1.0", level=1)
    d.add_paragraph("Use one row per NDA and these columns, in this order. Quote the clause and give its section number for every value.")
    cols = [("Counterparty", "Legal name and state"), ("Effective date", "As written; flag blanks"),
            ("Mutual or one-way", "Mutual / we disclose / we receive"), ("Definition approach", "Reasonable-person / marking with window / marking only"),
            ("Oral disclosures", "Protected / written summary needed / excluded"), ("Protection period", "Years after disclosure"),
            ("Trade secret tail", "Yes / no"), ("Residuals", "Present / absent"), ("Non-solicit or non-compete", "None / non-solicit (months, exception?) / non-compete"),
            ("Derived or aggregated data use", "Purpose only / aggregated for services / general use"), ("Return or destruction", "Deadline, certification, backup exception"),
            ("Governing law and venue", "Law; exclusive or non-exclusive venue"), ("Remedies", "Injunctive relief; any liability cap"), ("Assignment", "Consent rule")]
    t = d.add_table(rows=1, cols=2)
    t.style = "Table Grid"
    t.rows[0].cells[0].text, t.rows[0].cells[1].text = "Column", "What to capture"
    for c, w in cols:
        row = t.add_row().cells
        row[0].text, row[1].text = c, w
    d.save(path)


EMAIL = """From: Dana Whitfield <dana.whitfield@acme-company.example>
To: Acme Legal <legal@acme-company.example>
Date: Wed, 4 Nov 2026 09:12:00 -0500
Subject: NDA with Brightwater Systems - need this week please

Hi Legal team,

Brightwater Systems (Oakland, CA) wants to show us their new industrial sensor platform, including
prototype sensors and pre-release firmware, so we can see whether it fits our asset-monitoring
products. They sent their own mutual NDA (attached: Brightwater Mutual NDA.docx).

We'd like to meet them on November 12, so we need the NDA signed before then. Their contact is
Priya Raman, General Counsel. On our side, Jordan Ellis (VP Operations) would sign.

Anything we should know before we share our roadmap with them?

Thanks,
Dana Whitfield
Director, Strategic Partnerships
Acme Company, Inc.
"""


def write_playbooks(version, term_change, outdir):
    """Our heading format (via the build renderer) and the four-column table, from the baseline positions."""
    sys.path[:0] = [os.path.join(ACC, "build"), os.path.join(ACC, "core", "engines")]
    import build_accelerator as b  # noqa: E402
    import playbook_parse as pp  # noqa: E402
    import render_word_playbook as rw  # noqa: E402
    cat = b.generate_topic_files(os.path.join(ACC, "practices", "nda"), tempfile.mkdtemp())
    sides = {t["id"]: set(t["sides"]) for t in cat["topics"] if t.get("status") != "retired"}
    src = open(os.path.join(ACC, "practices", "nda", "positions", "baseline-mutual.md"), encoding="utf-8").read()
    name = f"Spaarke NDA – Mutual v{version}"
    src = src.replace("# Playbook: NDA - Mutual (Spaarke baseline)", f"# Playbook: {name}")
    src = re.sub(r"Version: [0-9.]+", f"Version: {version}", src)
    src = src.replace("Status: MVP test positions, not attorney-approved", "Status: POC test positions, not attorney-approved")
    if term_change:
        src = src.replace("are protected for 3 years after disclosure", "are protected for 2 years after disclosure")
        src = src.replace("Fallback: 2 years after disclosure, plus trade secrets for as long as they qualify.",
                          "Fallback: 18 months after disclosure, plus trade secrets for as long as they qualify.")
    scope = ("\nScope. Agreement family: non-disclosure agreement where each party discloses and receives. Represented side: "
             "mutual (apply every position to both parties). Commercial context: standard commercial discussions, not M&A. "
             "Jurisdiction: United States; Delaware default. Negotiation stage: acceptable end state; fallbacks in order. "
             "Do not use for: one-way NDAs, M&A confidentiality agreements, employee confidentiality agreements.\n")
    lines = src.split("\n")
    first_topic = next(i for i, l in enumerate(lines) if l.startswith("## topic:"))
    # the customer copy carries the scope, not the baseline's own commentary
    src = "\n".join(lines[:2]) + "\n" + scope + "\n" + "\n".join(lines[first_topic:])
    os.makedirs(outdir, exist_ok=True)
    open(os.path.join(outdir, f"{name}.md"), "w", encoding="utf-8").write(src)
    parsed = pp.parse(src, sides)
    rw.render(parsed, cat, os.path.join(outdir, f"{name}.docx"))
    # the Word copy: Microsoft's four-column shape, from the build renderer (design §5.3, OD-39)
    for placement, suffix in (("none", " (table)"), ("instructions", " (table, ids in Instructions)")):
        path = rw.render_table(parsed, cat, os.path.join(outdir, f"{name}{suffix}.docx"), placement)
        problems = rw.check_table(path, parsed, cat, placement)
        if problems:
            raise SystemExit(f"{path}: {problems}")
    return name


def main():
    for p in (os.path.join(POC, "kit"), HELD, KEY):
        if os.path.isdir(p):
            shutil.rmtree(p)
    key = {"generated": datetime.date.today().isoformat(), "policy": "Acme NDA Approval and Signature Authority Policy v1.0", "documents": {}}

    # Playbooks
    pb_name = write_playbooks("1.0", False, os.path.join(KIT, "Playbooks", "NDA"))
    write_playbooks("1.1", True, os.path.join(HELD, "Act 5 - playbook v1.1"))

    # Precedents: model form, executed precedents, negotiated example
    prec = os.path.join(KIT, "Precedents", "NDA")
    model_cp = cp("[COUNTERPARTY LEGAL NAME]", "[Counterparty]", "[State]", "[Name]", "[Title]", "[Address]", "[entity type]")
    build_nda(os.path.join(prec, "Acme Mutual NDA (model form).docx"), first=ACME, second=model_cp, title="Mutual Non-Disclosure Agreement",
              purpose="[describe the business purpose]", base=full_variants({}), date_text="[DATE]")
    for i, (fname, party, date_text, ov) in enumerate(PRECEDENTS):
        f, s, h = FONTS[i % 2]
        build_nda(os.path.join(prec, fname + ".docx"), first=ACME, second=party, title="Mutual Non-Disclosure Agreement",
                  purpose=f"evaluate a potential business relationship between {ACME['short']} and {party['short']}",
                  base=full_variants(ov), date_text=date_text, font=f, size=s, heading_style=h, executed=True)
        key["documents"][fname] = {"role": "precedent (executed, Acme paper)", **expected_row(full_variants(ov))}
    build_nda(os.path.join(prec, "Fernhill Instruments Mutual NDA - Acme markup (negotiated example).docx"),
              first=FERNHILL, second=ACME, title="Mutual Nondisclosure Agreement",
              purpose="evaluate a potential instrumentation supply relationship", base=full_variants(FERNHILL_BASE),
              new=full_variants({}), tracker=Tracker("Acme Legal (M. Ortega)", "2026-02-10T10:00:00Z"),
              date_text="February 3, 2026", font="Times New Roman", size=12, heading_style="section", comments=FERNHILL_COMMENTS)
    key["documents"]["Fernhill negotiated example"] = {"role": "precedent (Acme's requested positions as tracked changes)",
                                                       "base": expected_row(full_variants(FERNHILL_BASE)), "after_acme_changes": expected_row(full_variants({}))}

    # Matter: Brightwater (Acts 2-4)
    mat = os.path.join(KIT, "Matters", "Brightwater Systems")
    mv = full_variants(MATTER)
    build_nda(os.path.join(mat, "Brightwater Mutual NDA.docx"), first=BRIGHT, second=ACME, title="Mutual Nondisclosure Agreement",
              purpose=MATTER_PURPOSE, base=mv, date_text="____________, 2026", font="Arial", size=10.5, heading_style="numbered")
    key["documents"]["Brightwater Mutual NDA"] = {"role": "matter NDA (Acts 2-3)", **expected_row(mv)}
    # Act 4 response: Acme's revised draft (all G) with Brightwater's tracked pushback
    rb, rn = full_variants(RESPONSE_BASE), full_variants(RESPONSE_NEW)
    build_nda(os.path.join(HELD, "Act 4 - counterparty response", "Brightwater Mutual NDA - Brightwater response (tracked).docx"),
              first=BRIGHT, second=ACME, title="Mutual Nondisclosure Agreement", purpose=MATTER_PURPOSE, base=rb, new=rn,
              tracker=Tracker("Brightwater Legal (P. Raman)", "2026-11-09T16:30:00Z"), date_text="November 10, 2026",
              font="Arial", size=10.5, heading_style="numbered")
    key["documents"]["Brightwater response (Act 4)"] = {"role": "counterparty tracked changes on Acme's revised draft",
                                                        "changes": {t: f"{rb[t]} -> {rn[t]} ({L[t][rn[t]][1]})" for t in RESPONSE_NEW}}

    # Batch (Act 6)
    batch = os.path.join(KIT, "Inbox", "NDA batch")
    for i, (bid, party, purpose, ov, title) in enumerate(BATCH):
        f, s, h = FONTS[i % len(FONTS)]
        v = full_variants(ov)
        wrong = "Orchard Ridge Partners, LLC" if ov.get("nda.housekeeping") == "R" else None
        date_text = "____________, 2026" if ov.get("nda.housekeeping") == "Y" else f"November {i + 2}, 2026"
        fname = f"{bid} - {party['short']} {title}"
        build_nda(os.path.join(batch, fname + ".docx"), first=party, second=ACME, title=title, purpose=purpose, base=v,
                  date_text=date_text, font=f, size=s, heading_style=h, wrong_signer=wrong)
        key["documents"][fname] = {"role": "batch (Act 6)" + (" - convert to PDF, remove .docx" if bid in BATCH_PDF else ""), **expected_row(v)}

    # Grounding
    g = os.path.join(KIT, "Grounding")
    os.makedirs(os.path.join(g, "Normative", "NDA"), exist_ok=True)
    os.makedirs(os.path.join(g, "Format"), exist_ok=True)
    os.makedirs(os.path.join(g, "Samples", "NDA"), exist_ok=True)
    write_policy(os.path.join(g, "Normative", "NDA", "Acme NDA Approval and Signature Authority Policy v1.0.docx"))
    write_memo_template(os.path.join(g, "Format", "Acme Legal Memo Template v1.0.docx"))
    write_columns(os.path.join(g, "Format", "NDA Key Terms Columns v1.0.docx"))
    open(os.path.join(g, "Samples", "NDA", "Business request - Brightwater NDA (synthetic).eml"), "w", encoding="utf-8").write(EMAIL)
    ed = docx.Document()
    style_doc(ed, "Calibri", 11)
    for line in EMAIL.split("\n"):
        ed.add_paragraph(line)
    ed.save(os.path.join(g, "Samples", "NDA", "Business request - Brightwater NDA (synthetic).docx"))

    open(os.path.join(KIT, "README.txt"), "w", encoding="utf-8").write(
        "Legal Knowledge - POC kit (synthetic). All companies, people and deals are fictional. Generated "
        f"{key['generated']} by make_kit.py for the Spaarke accelerator end-to-end POC.\n")

    # Answer key (never upload)
    os.makedirs(KEY, exist_ok=True)
    json.dump(key, open(os.path.join(KEY, "answer-key.json"), "w", encoding="utf-8"), indent=2)
    md = ["# POC answer key (DO NOT upload to SharePoint or OneDrive)", "",
          f"Generated {key['generated']}. Playbook: {pb_name}. Policy: {key['policy']}.", "",
          "Only non-GREEN topics are listed. Rating = expected classification against the playbook.", ""]
    for name, rec in key["documents"].items():
        md.append(f"## {name}")
        md.append(f"*{rec['role']}*")
        if "topics" in rec:
            issues = [(t, r) for t, r in rec["topics"].items() if r["rating"] != "GREEN"]
            md += [""] + ([f"- `{t}`: **{r['rating']}** (variant {r['variant']})" for t, r in issues] or ["- All topics GREEN"])
            md += [f"- Policy findings: {', '.join(rec['policy_findings']) or 'none'}", f"- Approval: {rec['approval']}"]
        elif "changes" in rec:
            md += [""] + [f"- `{t}`: {c}" for t, c in rec["changes"].items()]
        else:
            md += ["", "- Base issues: " + ", ".join(f"`{t}` {r['rating']}" for t, r in rec["base"]["topics"].items() if r["rating"] != "GREEN"),
                   "- Acme's tracked changes bring every topic to GREEN"]
        md.append("")
    open(os.path.join(KEY, "answer-key.md"), "w", encoding="utf-8").write("\n".join(md))
    print("kit:", KIT)
    print("held:", HELD)
    print("answer key:", KEY)
    print("documents:", len(key["documents"]))


if __name__ == "__main__":
    main()
