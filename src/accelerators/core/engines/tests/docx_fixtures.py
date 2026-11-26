"""Build small .docx fixtures for the engine tests (python-docx + lxml; test-time only, never shipped).

A paragraph spec is a list of runs; each run is a str, or a dict
{"text": str, "bold": bool, "tab_before": bool}. Post-processing adds what python-docx cannot:
pre-existing tracked changes and comments, as another author would leave them.
"""
import copy
import zipfile

import docx
from lxml import etree

W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main"
REL_COMMENTS = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/comments"
CT_COMMENTS = "application/vnd.openxmlformats-officedocument.wordprocessingml.comments+xml"
XML_SPACE = "{http://www.w3.org/XML/1998/namespace}space"


def q(tag):
    return "{%s}%s" % (W, tag)


def build(path, paragraphs):
    d = docx.Document()
    for spec in paragraphs:
        p = d.add_paragraph()
        for r in spec:
            if isinstance(r, str):
                r = {"text": r}
            run = p.add_run()
            if r.get("tab_before"):
                run.add_tab()
            run.add_text(r["text"])  # tab + text in one run: several content children
            run.bold = r.get("bold", False)
    d.save(path)
    return path


def _rewrite(path, fn):
    with zipfile.ZipFile(path) as z:
        parts = {n: z.read(n) for n in z.namelist()}
    fn(parts)
    with zipfile.ZipFile(path, "w", zipfile.ZIP_DEFLATED) as zo:
        for n in sorted(parts, key=lambda n: n != "[Content_Types].xml"):
            zo.writestr(n, parts[n])


def _para(doc, key):
    hits = [p for p in doc.iter(q("p")) if key in "".join(t.text or "" for t in p.iter(q("t")))]
    assert len(hits) == 1, f"fixture key {key!r} matched {len(hits)}"
    return hits[0]


def add_existing_insertion(path, para_key, text, author="Opposing Counsel", rev_id="1"):
    """Append a pre-existing tracked insertion to the end of a paragraph."""
    def fn(parts):
        doc = etree.fromstring(parts["word/document.xml"])
        p = _para(doc, para_key)
        ins = etree.SubElement(p, q("ins"), {q("id"): rev_id, q("author"): author, q("date"): "2026-09-01T00:00:00Z"})
        r = etree.SubElement(ins, q("r"))
        t = etree.SubElement(r, q("t"))
        t.text = text
        t.set(XML_SPACE, "preserve")
        parts["word/document.xml"] = etree.tostring(doc, xml_declaration=True, encoding="UTF-8", standalone=True)
    _rewrite(path, fn)


def add_existing_deletion(path, para_key, text, author="Opposing Counsel", rev_id="2"):
    """Append a pre-existing tracked deletion (text that was struck) to the end of a paragraph."""
    def fn(parts):
        doc = etree.fromstring(parts["word/document.xml"])
        p = _para(doc, para_key)
        d = etree.SubElement(p, q("del"), {q("id"): rev_id, q("author"): author, q("date"): "2026-09-01T00:00:00Z"})
        r = etree.SubElement(d, q("r"))
        t = etree.SubElement(r, q("delText"))
        t.text = text
        t.set(XML_SPACE, "preserve")
        parts["word/document.xml"] = etree.tostring(doc, xml_declaration=True, encoding="UTF-8", standalone=True)
    _rewrite(path, fn)


def add_existing_comment(path, para_key, text, author="Opposing Counsel", cid="0"):
    """Anchor a pre-existing comment on a whole paragraph."""
    def fn(parts):
        doc = etree.fromstring(parts["word/document.xml"])
        p = _para(doc, para_key)
        runs = p.findall(q("r"))
        cs = etree.Element(q("commentRangeStart"), {q("id"): cid})
        runs[0].addprevious(cs)
        ce = etree.Element(q("commentRangeEnd"), {q("id"): cid})
        runs[-1].addnext(ce)
        rr = etree.Element(q("r"))
        etree.SubElement(rr, q("commentReference"), {q("id"): cid})
        ce.addnext(rr)
        parts["word/document.xml"] = etree.tostring(doc, xml_declaration=True, encoding="UTF-8", standalone=True)
        root = etree.Element(q("comments"), nsmap={"w": W})
        c = etree.SubElement(root, q("comment"), {q("id"): cid, q("author"): author, q("date"): "2026-09-01T00:00:00Z"})
        cp = etree.SubElement(c, q("p"))
        cr = etree.SubElement(cp, q("r"))
        ct = etree.SubElement(cr, q("t"))
        ct.text = text
        parts["word/comments.xml"] = etree.tostring(root, xml_declaration=True, encoding="UTF-8", standalone=True)
        rels = parts["word/_rels/document.xml.rels"].decode()
        parts["word/_rels/document.xml.rels"] = rels.replace(
            "</Relationships>", f'<Relationship Id="rIdFixCmt" Type="{REL_COMMENTS}" Target="comments.xml"/></Relationships>').encode()
        ctypes = parts["[Content_Types].xml"].decode()
        parts["[Content_Types].xml"] = ctypes.replace(
            "</Types>", f'<Override PartName="/word/comments.xml" ContentType="{CT_COMMENTS}"/></Types>').encode()
    _rewrite(path, fn)


def tamper_text(path, old, new):
    """Change visible text WITHOUT a tracked change (simulates an untracked edit)."""
    def fn(parts):
        doc = etree.fromstring(parts["word/document.xml"])
        for t in doc.iter(q("t")):
            if old in (t.text or "") and not any(a.tag == q("ins") for a in t.iterancestors()):
                t.text = t.text.replace(old, new)
                break
        else:
            raise AssertionError(f"{old!r} not found to tamper")
        parts["word/document.xml"] = etree.tostring(doc, xml_declaration=True, encoding="UTF-8", standalone=True)
    _rewrite(path, fn)


def recolour(path, old_hex, new_hex):
    def fn(parts):
        parts["word/document.xml"] = parts["word/document.xml"].replace(
            f'w:val="{old_hex}"'.encode(), f'w:val="{new_hex}"'.encode())
    _rewrite(path, fn)


NSDECL = ('xmlns:w="%s" xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006" '
          'xmlns:v="urn:schemas-microsoft-com:vml" xmlns:wp="http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing" '
          'xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" '
          'xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"') % W
REV = 'w:author="Opposing Counsel" w:date="2026-09-01T00:00:00Z"'


def R(text, rpr=""):
    return '<w:r>%s<w:t xml:space="preserve">%s</w:t></w:r>' % (rpr, text)


def P(*content, ppr=""):
    return "<w:p>%s%s</w:p>" % (ppr, "".join(content))


def INS(text, rid):
    return '<w:ins w:id="%s" %s>%s</w:ins>' % (rid, REV, R(text))


def DEL(text, rid):
    return '<w:del w:id="%s" %s><w:r><w:delText xml:space="preserve">%s</w:delText></w:r></w:del>' % (rid, REV, text)


def build_raw(path, body, header=None):
    """A python-docx package whose body (and optionally first header) is the given raw WordprocessingML."""
    d = docx.Document()
    if header is not None:
        d.sections[0].header.paragraphs[0].text = "HEADERPLACEHOLDER"
    d.save(path)

    def fn(parts):
        sect = etree.fromstring(parts["word/document.xml"]).find("%s/%s" % (q("body"), q("sectPr")))
        parts["word/document.xml"] = ('<?xml version="1.0" encoding="UTF-8" standalone="yes"?><w:document %s><w:body>%s%s'
                                      '</w:body></w:document>' % (NSDECL, body, etree.tostring(sect).decode())).encode()
        if header is not None:
            hn = header_part(parts)
            parts[hn] = parts[hn].replace(b"<w:r><w:t>HEADERPLACEHOLDER</w:t></w:r>", header.encode())
            assert header.encode() in parts[hn]
    _rewrite(path, fn)
    return path


def header_part(parts):
    return sorted(n for n in parts if n.startswith("word/header"))[0]


def edit_part(path, name, fn):
    """Rewrite one part in place: fn(bytes) -> bytes. name may be a callable picking the part from the names."""
    def apply(parts):
        n = name(parts) if callable(name) else name
        new = fn(parts[n])
        assert new != parts[n], f"edit to {n} changed nothing"
        parts[n] = new
    _rewrite(path, apply)


def add_part(path, name, data):
    def fn(parts):
        parts[name] = data
    _rewrite(path, fn)


def drop_part(path, name):
    def fn(parts):
        del parts[name]
    _rewrite(path, fn)


def document_root(path):
    with zipfile.ZipFile(path) as z:
        return etree.fromstring(z.read("word/document.xml"))


def part(path, name):
    with zipfile.ZipFile(path) as z:
        return z.read(name) if name in z.namelist() else None


__all__ = ["build", "build_raw", "add_existing_insertion", "add_existing_deletion", "add_existing_comment", "tamper_text",
           "recolour", "edit_part", "add_part", "drop_part", "header_part", "document_root", "part", "q", "W", "R", "P", "INS", "DEL",
           "copy"]
