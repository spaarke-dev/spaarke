"""ooxml_redline.py - apply an edit plan to a .docx as surgical tracked changes.

Python 3 standard library + lxml only (plus safe_docx.py beside it). Never falls back to
xml.etree for writing (ElementTree rewrites namespace prefixes and breaks mc:Ignorable).

Usage:
  python ooxml_redline.py PLAN.json ORIGINAL.docx REDLINE.docx
         [--external EXTERNAL.docx] [--clean CLEAN.docx] [--dry-run]

Outputs:
  REDLINE    tracked changes + one margin comment per commented edit + bookmarks
             (summary_table.py later adds the page-1 summary -> internal redline)
  EXTERNAL   same tracked changes, none of our comments (the counterparty's stay)
  CLEAN      every tracked change accepted (ours AND any already in the document, in the body,
             headers, footers and notes), no comments, optional drawing strip

Plan (see edit-plan.schema.json; unknown keys are refused):
  {"author": "...", "initials": "..", "date": "2026-10-31T12:00:00Z",
   "strip_drawings_in_clean": false,
   "bookmarks": {"<para key>": "NDA_Sec10", ...},
   "edits": [
     {"para": "<key>", "find": "three", "replace": "five", "comment": "...", "summary": {...}},
     {"para": "<key>", "find": "(3)", "old": "3", "replace": "5"},
     {"para": "<key>", "after": "anchor text", "insert": "new text", "comment": "..."},
     {"para": "<key>", "find": "*", "replace": "Intentionally Omitted.", "comment": "..."},
     {"para": "<key>", "find": "text to flag", "comment": "comment-only"}]}

Rules:
  - "para" matches exactly one paragraph (visible + deleted text, whitespace collapsed).
  - "find"/"after" occur exactly once in the paragraph's visible text AND in its editable text
    (direct runs), and start and end on word boundaries; "old" occurs once inside "find".
  - "find": "*" = the paragraph text after a leading section number and heading.
  - A span may not cross an existing revision, hyperlink, content control, field or drawing.
  - List edits within one paragraph right to left (later text first).
Exit codes: 0 ok, 2 bad input/plan, 3 plan does not fit the document.
"""
import argparse
import copy
import json
import os
import re
import sys
import zipfile

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
try:
    from lxml import etree
    import safe_docx
except ImportError as _ex:  # stop and report; never fall back to xml.etree
    print(json.dumps({"error": f"cannot load lxml/safe_docx: {_ex}"}), file=sys.stderr)
    sys.exit(2)

W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main"
XML_NS = "http://www.w3.org/XML/1998/namespace"
MC = "http://schemas.openxmlformats.org/markup-compatibility/2006"
REL_COMMENTS = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/comments"
CT_COMMENTS = "application/vnd.openxmlformats-officedocument.wordprocessingml.comments+xml"
COMMENT_PARTS = {"word/comments.xml", "word/commentsExtended.xml", "word/commentsIds.xml",
                 "word/commentsExtensible.xml"}
STORY_PART = re.compile(r"^word/(header\d*|footer\d*|footnotes|endnotes)\.xml$")


def q(tag):
    return "{%s}%s" % (W, tag)


SPACE = "{%s}space" % XML_NS
# run children that carry visible characters, and the character each stands for
CHAR_CHILDREN = {q("t"): None, q("tab"): "\t", q("br"): "\n", q("cr"): "\n", q("noBreakHyphen"): "-"}
# markers that may sit between runs inside a span; they are kept outside our w:del
START_MARKERS = {q("bookmarkStart"), q("commentRangeStart"), q("permStart")}
END_MARKERS = {q("bookmarkEnd"), q("commentRangeEnd"), q("permEnd"), q("proofErr")}
UNSAFE_IN_RUN = {q("fldChar"), q("instrText"), q("drawing"), q("pict"), q("object"), "{%s}AlternateContent" % MC}
SETTINGS_BEFORE_TRACK = {
    "writeProtection", "view", "zoom", "removePersonalInformation", "removeDateAndTime",
    "doNotDisplayPageBoundaries", "displayBackgroundShape", "printPostScriptOverText",
    "printFractionalCharacterWidth", "printFormsData", "embedTrueTypeFonts", "embedSystemFonts",
    "saveSubsetFonts", "saveFormsData", "mirrorMargins", "alignBordersAndEdges",
    "bordersDoNotSurroundHeader", "bordersDoNotSurroundFooter", "gutterAtTop", "hideSpellingErrors",
    "hideGrammaticalErrors", "activeWritingStyle", "proofState", "formsDesign", "attachedTemplate",
    "linkStyles", "stylePaneFormatFilter", "stylePaneSortMethod", "documentType", "mailMerge",
    "revisionView"}
# a leading "5." / "5.1" / "(a)" / "A." / "Section 5" plus an optional "Heading." - kept by find "*"
LEAD_RE = re.compile(r"^\s*(?:(?:section|article|clause)\s+)?(?:\d+(?:\.\d+)*\.?|\([a-z0-9]{1,4}\)|[A-Z]\.)\s+"
                     r"(?:[A-Z][^.:]{0,60}[.:]\s+)?", re.I)

PLAN_KEYS = {"author", "initials", "date", "strip_drawings_in_clean", "provenance", "agreement", "review_basis",
             "bookmarks", "unchanged", "edits"}
EDIT_KEYS = {"para", "find", "old", "replace", "after", "insert", "comment", "topic", "summary"}
SUMMARY_KEYS = {"n", "section", "bookmark", "issue", "revision", "priority"}
BOOKMARK_RE = re.compile(r"^[A-Za-z][A-Za-z0-9_]{0,39}$")


def norm(s):
    return re.sub(r"\s+", " ", s)


class PlanError(Exception):
    """The plan does not fit the document, or breaks the plan contract (exit 3)."""


def validate_plan(plan):
    """Closed-set check of the plan shape. A misspelt key must never turn an edit into a no-op."""
    if not isinstance(plan, dict):
        raise PlanError("plan must be a JSON object")
    errors = [f"unknown plan key {k!r}" for k in plan if k not in PLAN_KEYS]
    if not isinstance(plan.get("edits"), list):
        errors.append("plan needs an 'edits' list")
    for name in (plan.get("bookmarks") or {}).values():
        if not isinstance(name, str) or not BOOKMARK_RE.match(name):
            errors.append(f"bookmark name {name!r} must start with a letter, <=40 chars, letters/digits/_")
    names = list((plan.get("bookmarks") or {}).values())
    if len(names) != len(set(names)):
        errors.append("bookmark names repeat in the plan")
    for i, e in enumerate(plan.get("edits") or []):
        if not isinstance(e, dict):
            errors.append(f"edit {i}: not an object")
            continue
        bad = [k for k in e if k not in EDIT_KEYS]
        if bad:
            errors.append(f"edit {i}: unknown key(s) {bad}")
        for k in ("para", "find", "old", "replace", "after", "insert", "comment", "topic"):
            if k in e and not isinstance(e[k], str):
                errors.append(f"edit {i}: {k!r} must be a string")
        if not e.get("para"):
            errors.append(f"edit {i}: 'para' is required")
        has_find, has_after = "find" in e, "after" in e
        if has_find == has_after:
            errors.append(f"edit {i}: exactly one of 'find' or 'after' is required")
        if has_after and ("insert" not in e or not e.get("insert")):
            errors.append(f"edit {i}: 'after' needs a non-empty 'insert'")
        if has_after and ("replace" in e or "old" in e):
            errors.append(f"edit {i}: 'after' cannot be combined with 'replace' or 'old'")
        if has_find and "insert" in e:
            errors.append(f"edit {i}: 'insert' belongs with 'after'; use 'replace' with 'find'")
        if has_find and "replace" not in e and not e.get("comment"):
            errors.append(f"edit {i}: comment-only edit (no 'replace') needs a 'comment'")
        if "summary" in e:
            s = e["summary"]
            if not isinstance(s, dict) or set(s) != SUMMARY_KEYS:
                errors.append(f"edit {i}: summary needs exactly {sorted(SUMMARY_KEYS)}")
            if not e.get("comment"):
                errors.append(f"edit {i}: a summary row needs a comment on the same edit")
    if errors:
        raise PlanError("; ".join(errors))


def word_edge_ok(text, start, end):
    """True when [start, end) does not cut a word at either end."""
    def cut(a, b):
        return a.isalnum() and b.isalnum()
    if start > 0 and start < len(text) and cut(text[start - 1], text[start]):
        return False
    if 0 < end < len(text) and cut(text[end - 1], text[end]):
        return False
    return True


def strip_drawings(root):
    """Remove pictures: runs holding a drawing/VML picture/object/AlternateContent, and loose AlternateContent."""
    for r in list(root.iter(q("r"))):
        if r.getparent() is not None and any(d.tag in UNSAFE_IN_RUN - {q("fldChar"), q("instrText")} for d in r.iter()):
            r.getparent().remove(r)
    for ac in list(root.iter("{%s}AlternateContent" % MC)):
        if ac.getparent() is not None:
            ac.getparent().remove(ac)
    return root


class Redliner:
    def __init__(self, src, author, initials="", date="2026-01-01T00:00:00Z"):
        self.parts = safe_docx.read_parts(src)
        self.doc = safe_docx.parse(self.parts["word/document.xml"])
        self.body = self.doc.find(q("body"))
        self.author, self.initials, self.date = author, initials, date
        ids = [int(v) for n, data in self.parts.items() if n.endswith(".xml")
               for v in re.findall(rb'w:id="(-?\d+)"', data)]
        self._next_id = max(ids + [0]) + 1000  # revision and bookmark ids above anything in any part
        self.comments_root = None
        if "word/comments.xml" in self.parts:
            self.comments_root = safe_docx.parse(self.parts["word/comments.xml"])
            cids = [int(c.get(q("id"))) for c in self.comments_root.findall(q("comment"))]
            self._next_cid = max(cids + [-1]) + 1
        else:
            self._next_cid = 0
        self.bookmark_names = {b.get(q("name")) for b in self.doc.iter(q("bookmarkStart"))}
        self.new_comments = []  # (id, text)
        self.stats = {"ins": 0, "del": 0, "comments": 0, "bookmarks": 0}

    # ---------- ids ----------
    def _id(self):
        self._next_id += 1
        return str(self._next_id)

    def _rev_attrs(self):
        return {q("id"): self._id(), q("author"): self.author, q("date"): self.date}

    def _fresh_ids(self, el):
        """A copied run must not duplicate the id of a formatting revision it carries."""
        for rc in el.iter(q("rPrChange")):
            rc.set(q("id"), self._id())

    # ---------- paragraphs and runs ----------
    def paras(self):
        return list(self.body.iter(q("p")))

    @staticmethod
    def full_text(p):
        """Visible + deleted text with whitespace collapsed (tabs/breaks count as a space)."""
        return norm("".join((t.text or "") if t.tag in (q("t"), q("delText")) else " "
                            for t in p.iter(q("t"), q("delText"), q("tab"), q("br"))))

    @staticmethod
    def visible_text(p):
        """What the reader sees: every run's text not under a deletion, including hyperlinks,
        content controls and insertions."""
        out = []
        for el in p.iter(q("t"), q("tab"), q("br")):
            if any(a.tag in (q("del"), q("moveFrom")) for a in el.iterancestors()):
                continue
            out.append((el.text or "") if el.tag == q("t") else ("\t" if el.tag == q("tab") else "\n"))
        return "".join(out)

    def find_para(self, key):
        key = norm(key)
        hits = [p for p in self.paras() if key in self.full_text(p)]
        if len(hits) != 1:
            raise PlanError(f"paragraph key {key!r} matched {len(hits)} paragraphs")
        return hits[0]

    @staticmethod
    def _run_char(child):
        if child.tag == q("t"):
            return child.text or ""
        return CHAR_CHILDREN.get(child.tag, "") or ""

    def atomize(self, p):
        """Split direct-child runs so each holds one content child (t/tab/br/...).

        Makes every character offset addressable without changing what Word renders.
        """
        for r in list(p.findall(q("r"))):
            content = [c for c in r if c.tag != q("rPr")]
            if len(content) <= 1:
                continue
            rpr = r.find(q("rPr"))
            anchor = r
            for i, c in enumerate(content):
                nr = etree.Element(q("r"))
                if rpr is not None:
                    nr.append(copy.deepcopy(rpr))
                    if i:
                        self._fresh_ids(nr)
                nr.append(c)  # moves c out of r
                anchor.addnext(nr)
                anchor = nr
            p.remove(r)

    def runs(self, p):
        """Direct-child runs with their start offset and text (the editable text)."""
        out, pos = [], 0
        for r in p.findall(q("r")):
            txt = "".join(self._run_char(c) for c in r if c.tag != q("rPr"))
            out.append((r, pos, txt))
            pos += len(txt)
        return out

    def text(self, p):
        return "".join(t for _, _, t in self.runs(p))

    def _split(self, p, off):
        for r, s, t in self.runs(p):
            if s < off < s + len(t):
                tnode = r.find(q("t"))
                if tnode is None:  # tab/br are one character; cannot be split inside
                    raise PlanError("cannot split inside a non-text run")
                k = off - s
                r2 = copy.deepcopy(r)
                self._fresh_ids(r2)
                tnode.text = t[:k]
                tnode.set(SPACE, "preserve")
                t2 = r2.find(q("t"))
                t2.text = t[k:]
                t2.set(SPACE, "preserve")
                r.addnext(r2)
                return

    @staticmethod
    def _in_field(p):
        """Direct children of p that sit inside a complex field (instruction or result)."""
        inside, depth = set(), 0
        for el in p:
            chars = [c for c in el.iter(q("fldChar"))] if el.tag == q("r") else []
            for c in chars:
                kind = c.get(q("fldCharType"))
                if kind == "begin":
                    depth += 1
                elif kind == "end":
                    depth = max(0, depth - 1)
            if depth or chars:
                inside.add(el)
        return inside

    def _span(self, p, s, e):
        """The runs covering [s, e), plus the markers between them, in document order."""
        self._split(p, s)
        self._split(p, e)
        rs = [r for r, rs_, t in self.runs(p) if t and rs_ >= s and rs_ + len(t) <= e]
        if not rs:
            raise PlanError("empty span")
        span, el = [], rs[0]
        while el is not None:
            span.append(el)
            if el is rs[-1]:
                break
            el = el.getnext()
        in_field = self._in_field(p)
        for el in span:
            if el.tag == q("r"):
                if any(d.tag in UNSAFE_IN_RUN for d in el.iter()):
                    raise PlanError("span crosses a field or drawing; narrow the edit")
                if el in in_field:
                    raise PlanError("span is inside a field result (it would be lost when Word updates fields)")
            elif el.tag not in START_MARKERS | END_MARKERS:
                raise PlanError(f"span crosses existing {etree.QName(el).localname} "
                                "(revision, hyperlink, content control...); narrow the edit or anchor next to it")
        return span

    @staticmethod
    def coalesce(p):
        """Merge adjacent plain-text direct-child runs with identical formatting (undo atomize)."""
        def key(r):
            kids = [c for c in r if c.tag != q("rPr")]
            if len(kids) != 1 or kids[0].tag != q("t"):
                return None
            rpr = r.find(q("rPr"))
            if rpr is not None and rpr.find(q("rPrChange")) is not None:
                return None  # keep formatting revisions exactly as the author left them
            return b"" if rpr is None else etree.tostring(rpr)
        prev, prev_key = None, None
        for r in list(p):
            k = key(r) if r.tag == q("r") else None
            if k is not None and prev is not None and k == prev_key:
                pt = prev.find(q("t"))
                pt.text = (pt.text or "") + (r.find(q("t")).text or "")
                pt.set(SPACE, "preserve")
                p.remove(r)
                continue
            prev, prev_key = (r, k) if k is not None else (None, None)

    @staticmethod
    def _new_run(text, rpr):
        r = etree.Element(q("r"))
        if rpr is not None:
            rp = copy.deepcopy(rpr)
            for rc in rp.findall(q("rPrChange")):  # never inherit another author's formatting revision
                rp.remove(rc)
            r.append(rp)
        t = etree.SubElement(r, q("t"))
        t.text = text
        t.set(SPACE, "preserve")
        return r

    def _comment(self, first, last, text):
        cid = self._next_cid
        self._next_cid += 1
        self.new_comments.append((cid, text))
        cs = etree.Element(q("commentRangeStart"))
        cs.set(q("id"), str(cid))
        first.addprevious(cs)
        ce = etree.Element(q("commentRangeEnd"))
        ce.set(q("id"), str(cid))
        last.addnext(ce)
        rr = etree.Element(q("r"))
        rp = etree.SubElement(rr, q("rPr"))
        etree.SubElement(rp, q("rStyle")).set(q("val"), "CommentReference")
        etree.SubElement(rr, q("commentReference")).set(q("id"), str(cid))
        ce.addnext(rr)
        self.stats["comments"] += 1

    @staticmethod
    def _to_deleted(r):
        for c in r:
            if c.tag == q("t"):
                c.tag = q("delText")
                c.set(SPACE, "preserve")

    def _locate(self, p, needle, what):
        """Offset of needle in the editable text after checking it is unique where the reader sees it."""
        txt = self.text(p)
        vis = self.visible_text(p)
        if vis.count(needle) != 1:
            raise PlanError(f"{what} {needle[:50]!r} occurs {vis.count(needle)} times in the visible paragraph text")
        if txt.count(needle) != 1:
            raise PlanError(f"{what} {needle[:50]!r} is not in the paragraph's editable text "
                            "(inside an existing revision, hyperlink or content control)")
        s = txt.index(needle)
        if not word_edge_ok(txt, s, s + len(needle)):
            raise PlanError(f"{what} {needle[:50]!r} starts or ends inside a word; extend it to whole words")
        return txt, s

    # ---------- edit primitives ----------
    def apply(self, e):
        p = self.find_para(e["para"])
        self.atomize(p)
        try:
            self._apply(p, e)
        finally:
            self.coalesce(p)

    def _apply(self, p, e):
        if "after" in e:
            txt, s = self._locate(p, e["after"], "anchor")
            off = s + len(e["after"])
            self._split(p, off)
            prev = [r for r, rs_, t in self.runs(p) if t and rs_ + len(t) <= off][-1]
            ins = etree.Element(q("ins"), self._rev_attrs())
            ins.append(self._new_run(e["insert"], prev.find(q("rPr"))))
            prev.addnext(ins)
            self.stats["ins"] += 1
            if e.get("comment"):
                self._comment(ins, ins, e["comment"])
            return
        if e["find"] == "*":
            whole = self.text(p)
            lead = LEAD_RE.match(whole)
            find = whole[lead.end():] if lead else whole
            if not find.strip():
                raise PlanError("find '*' left nothing after the section number and heading")
        else:
            find = e["find"]
        txt, s = self._locate(p, find, "find")
        old = e.get("old", find)
        if find.count(old) != 1:
            raise PlanError(f"old {old!r} must occur exactly once inside find {find!r}")
        s += find.index(old)
        if not word_edge_ok(txt, s, s + len(old)):
            raise PlanError(f"old {old!r} starts or ends inside a word")
        span = self._span(p, s, s + len(old))
        runs = [r for r in span if r.tag == q("r")]
        if "replace" not in e:
            self._comment(span[0], span[-1], e["comment"])
            return
        text_runs = [r for r in runs if r.find(q("t")) is not None]
        rpr = (text_runs or runs)[-1].find(q("rPr"))
        d = etree.Element(q("del"), self._rev_attrs())
        runs[0].addprevious(d)
        before = [m for m in span if m.tag in START_MARKERS]
        after = [m for m in span if m.tag in END_MARKERS]
        for r in runs:
            self._to_deleted(r)
            d.append(r)
        for m in before:  # markers stay outside the deletion, starts before it, ends after it
            d.addprevious(m)
        anchor = d
        for m in after:
            anchor.addnext(m)
            anchor = m
        self.stats["del"] += 1
        last = d
        if e["replace"]:
            ins = etree.Element(q("ins"), self._rev_attrs())
            ins.append(self._new_run(e["replace"], rpr))
            d.addnext(ins)
            last = ins
            self.stats["ins"] += 1
        if e.get("comment"):
            self._comment(d, last, e["comment"])

    def bookmark(self, key, name):
        if not BOOKMARK_RE.match(name):
            raise PlanError(f"bookmark name {name!r} must start with a letter, <=40 chars, letters/digits/_")
        if name in self.bookmark_names:
            raise PlanError(f"bookmark {name!r} already exists in the document; choose another name")
        p = self.find_para(key)
        bid = self._id()
        bs = etree.Element(q("bookmarkStart"), {q("id"): bid, q("name"): name})
        ppr = p.find(q("pPr"))
        if ppr is not None:
            ppr.addnext(bs)
        else:
            p.insert(0, bs)
        p.append(etree.Element(q("bookmarkEnd"), {q("id"): bid}))
        self.bookmark_names.add(name)
        self.stats["bookmarks"] += 1

    # ---------- output ----------
    def _comments_xml(self, include_new=True):
        root = copy.deepcopy(self.comments_root) if self.comments_root is not None \
            else etree.Element(q("comments"), nsmap={"w": W})
        for cid, text in (self.new_comments if include_new else []):
            c = etree.SubElement(root, q("comment"), {q("id"): str(cid), q("author"): self.author,
                                                     q("date"): self.date, q("initials"): self.initials})
            p = etree.SubElement(c, q("p"))
            r1 = etree.SubElement(p, q("r"))
            etree.SubElement(r1, q("annotationRef"))
            r2 = etree.SubElement(p, q("r"))
            t = etree.SubElement(r2, q("t"))
            t.text = text
            t.set(SPACE, "preserve")
        return etree.tostring(root, xml_declaration=True, encoding="UTF-8", standalone=True)

    def _ensure_comment_parts(self, out):
        rels = out["word/_rels/document.xml.rels"].decode("utf-8")
        if "comments.xml" not in rels:
            rels = rels.replace("</Relationships>",
                                '<Relationship Id="rIdSprkCmt1" Type="%s" Target="comments.xml"/></Relationships>' % REL_COMMENTS)
            out["word/_rels/document.xml.rels"] = rels.encode("utf-8")
        ct = out["[Content_Types].xml"].decode("utf-8")
        if "/word/comments.xml" not in ct:
            ct = ct.replace("</Types>", '<Override PartName="/word/comments.xml" ContentType="%s"/></Types>' % CT_COMMENTS)
            out["[Content_Types].xml"] = ct.encode("utf-8")

    @staticmethod
    def _set_tracking(out, on):
        if "word/settings.xml" not in out:
            return
        st = safe_docx.parse(out["word/settings.xml"])
        for el in st.findall(q("trackRevisions")):
            st.remove(el)
        if on:
            # CT_Settings is a sequence: trackRevisions goes after these, before everything else
            before = [c for c in st if etree.QName(c).localname in SETTINGS_BEFORE_TRACK]
            el = etree.Element(q("trackRevisions"))
            if before:
                before[-1].addnext(el)
            else:
                st.insert(0, el)
        out["word/settings.xml"] = etree.tostring(st, xml_declaration=True, encoding="UTF-8", standalone=True)

    @staticmethod
    def _drop_comment_parts(out):
        """Remove every comments part and its relationship and content-type entries."""
        for n in [n for n in out if n in COMMENT_PARTS]:
            del out[n]
        rels = safe_docx.parse(out["word/_rels/document.xml.rels"])
        for r in list(rels):
            if "word/" + r.get("Target", "").lstrip("/").replace("word/", "", 1) in COMMENT_PARTS:
                rels.remove(r)
        out["word/_rels/document.xml.rels"] = etree.tostring(rels, xml_declaration=True, encoding="UTF-8", standalone=True)
        ct = safe_docx.parse(out["[Content_Types].xml"])
        for o in list(ct):
            if o.get("PartName", "").lstrip("/") in COMMENT_PARTS:
                ct.remove(o)
        out["[Content_Types].xml"] = etree.tostring(ct, xml_declaration=True, encoding="UTF-8", standalone=True)

    def _write(self, path, root, comments, tracking, extra_parts=None):
        """comments: "all" (original + ours), "original" (counterparty's only) or None (no comments)."""
        out = dict(self.parts)
        out["word/document.xml"] = etree.tostring(root, xml_declaration=True, encoding="UTF-8", standalone=True)
        out.update(extra_parts or {})
        if comments == "all" and (self.new_comments or self.comments_root is not None):
            out["word/comments.xml"] = self._comments_xml(include_new=True)
            self._ensure_comment_parts(out)
        elif comments == "original" and self.comments_root is not None:
            out["word/comments.xml"] = self._comments_xml(include_new=False)
        elif comments is None:
            self._drop_comment_parts(out)
        self._set_tracking(out, tracking)
        with zipfile.ZipFile(path, "w", zipfile.ZIP_DEFLATED) as zo:
            # [Content_Types].xml first, as Word expects
            for n in sorted(out, key=lambda n: n != "[Content_Types].xml"):
                zo.writestr(n, out[n])

    @staticmethod
    def _strip_comment_marks(root, ids=None):
        for tag in ("commentRangeStart", "commentRangeEnd"):
            for el in list(root.iter(q(tag))):
                if ids is None or el.get(q("id")) in ids:
                    el.getparent().remove(el)
        for el in list(root.iter(q("commentReference"))):
            if ids is None or el.get(q("id")) in ids:
                r = el.getparent()
                r.getparent().remove(r)

    def save_redline(self, path):
        self._write(path, self.doc, comments="all", tracking=True)

    def save_external(self, path):
        """Same tracked changes, without our review comments (the counterparty's stay)."""
        root = copy.deepcopy(self.doc)
        self._strip_comment_marks(root, {str(cid) for cid, _ in self.new_comments})
        self._write(path, root, comments="original", tracking=True)

    def save_clean(self, path, strip=False):
        root = accept_all(copy.deepcopy(self.doc))
        self._strip_comment_marks(root)
        if strip:
            strip_drawings(root)
        stories = {}
        for name, data in self.parts.items():  # headers, footers and notes carry revisions and comments too
            if STORY_PART.match(name):
                sroot = accept_all(safe_docx.parse(data))
                self._strip_comment_marks(sroot)
                if strip:
                    strip_drawings(sroot)
                stories[name] = etree.tostring(sroot, xml_declaration=True, encoding="UTF-8", standalone=True)
        self._write(path, root, comments=None, tracking=False, extra_parts=stories)


def accept_all(root):
    """Accept every tracked change in a part's element tree (ours and pre-existing)."""
    # rows: a deleted row goes; an inserted row stays
    for tr in list(root.iter(q("tr"))):
        trpr = tr.find(q("trPr"))
        if trpr is None:
            continue
        if trpr.find(q("del")) is not None:
            tr.getparent().remove(tr)
            continue
        for m in trpr.findall(q("ins")):
            trpr.remove(m)
    # paragraph marks: a deleted mark joins the paragraph with the next one, which keeps its own
    # properties (Word stores paragraph formatting in the surviving mark); chains are followed
    for p in list(root.iter(q("p"))):
        if p.getparent() is None:
            continue
        while True:
            mark = p.find("%s/%s/%s" % (q("pPr"), q("rPr"), q("del")))
            if mark is None:
                break
            mark.getparent().remove(mark)
            nxt = p.getnext()
            if nxt is None or nxt.tag != q("p"):
                break
            own = p.find(q("pPr"))
            nxt_ppr = nxt.find(q("pPr"))
            if own is not None:
                p.remove(own)
            if nxt_ppr is not None:
                p.insert(0, nxt_ppr)
            for ch in list(nxt):
                p.append(ch)
            nxt.getparent().remove(nxt)
        mark = p.find("%s/%s/%s" % (q("pPr"), q("rPr"), q("ins")))
        if mark is not None:
            mark.getparent().remove(mark)
    for tag in ("del", "moveFrom"):
        for el in list(root.iter(q(tag))):
            if el.getparent() is not None:
                el.getparent().remove(el)
    for tag in ("ins", "moveTo"):
        for el in list(root.iter(q(tag))):
            parent = el.getparent()
            if parent is None:
                continue
            idx = parent.index(el)
            for ch in list(el):
                parent.insert(idx, ch)
                idx += 1
            parent.remove(el)
    for tag in ("rPrChange", "pPrChange", "sectPrChange", "tblPrChange", "trPrChange", "tcPrChange",
                "tblGridChange", "numberingChange", "cellIns", "cellDel", "cellMerge",
                "moveFromRangeStart", "moveFromRangeEnd", "moveToRangeStart", "moveToRangeEnd"):
        for el in list(root.iter(q(tag))):
            el.getparent().remove(el)
    return root


def run(plan, src, dst, external=None, clean=None, dry_run=False):
    validate_plan(plan)
    rl = Redliner(src, plan.get("author", "Reviewer"), plan.get("initials", ""),
                  plan.get("date", "2026-01-01T00:00:00Z"))
    for key, name in (plan.get("bookmarks") or {}).items():  # before edits change the text
        rl.bookmark(key, name)
    for i, e in enumerate(plan.get("edits", [])):
        try:
            rl.apply(e)
        except PlanError as ex:
            raise PlanError(f"edit {i}: {ex}") from None
    if not dry_run:
        rl.save_redline(dst)
        if external:
            rl.save_external(external)
        if clean:
            rl.save_clean(clean, strip=plan.get("strip_drawings_in_clean", False))
    return {"edits": len(plan.get("edits", [])), **rl.stats, "dry_run": dry_run}


def main(argv=None):
    ap = argparse.ArgumentParser(description="Apply an edit plan to a .docx as tracked changes.")
    ap.add_argument("plan")
    ap.add_argument("original")
    ap.add_argument("redline")
    ap.add_argument("--external")
    ap.add_argument("--clean")
    ap.add_argument("--dry-run", action="store_true", help="validate the plan against the document; write nothing")
    a = ap.parse_args(argv)
    try:
        with open(a.plan, encoding="utf-8") as f:
            plan = json.load(f)
    except (OSError, ValueError) as ex:
        print(json.dumps({"error": f"cannot read plan: {ex}"}), file=sys.stderr)
        return 2
    try:
        result = run(plan, a.original, a.redline, a.external, a.clean, a.dry_run)
    except PlanError as ex:
        print(json.dumps({"error": str(ex)}), file=sys.stderr)
        return 3
    except (OSError, ValueError, zipfile.BadZipFile, etree.XMLSyntaxError) as ex:
        print(json.dumps({"error": str(ex)}), file=sys.stderr)
        return 2
    print(json.dumps(result))
    return 0


if __name__ == "__main__":
    sys.exit(main())
