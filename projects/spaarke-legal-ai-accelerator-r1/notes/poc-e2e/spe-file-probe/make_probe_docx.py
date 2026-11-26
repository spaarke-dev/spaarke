"""Build the harmless .docx used by the Cowork file-delivery probe (#3257 gap 1).

The file holds no client content: a title, the probe code FDP-1126-A and filler text from a fixed seed, so
every build is byte-identical. The test reads the probe code and the SHA-256 back from inside Cowork, which
proves the real bytes reached the sandbox (a model can't guess the hash).

Run:  C:\\Python313\\python.exe -B make_probe_docx.py [out_dir]
Writes spaarke-file-probe-small.docx and -large.docx; prints each size and SHA-256 for the test script.
"""
import hashlib
import os
import random
import sys
import zipfile

CODE = "FDP-1126-A"
FIXED = (1980, 1, 1, 0, 0, 0)
WORDS = ("alpha bravo charlie delta echo foxtrot golf hotel india juliet kilo lima mike november oscar papa "
         "quebec romeo sierra tango uniform victor whiskey xray yankee zulu").split()

CT = ('<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
      '<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">'
      '<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>'
      '<Default Extension="xml" ContentType="application/xml"/>'
      '<Override PartName="/word/document.xml" '
      'ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>'
      '</Types>')
RELS = ('<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
        '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">'
        '<Relationship Id="rId1" '
        'Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" '
        'Target="word/document.xml"/></Relationships>')


def para(text):
    return f'<w:p><w:r><w:t xml:space="preserve">{text}</w:t></w:r></w:p>'


def document(n):
    rng = random.Random(1126)
    paras = [para("Spaarke file delivery probe"),
             para(f"Probe code: {CODE}. This file holds no client content; it exists only to test how a "
                  "file reaches the Cowork workspace.")]
    for i in range(1, n + 1):
        paras.append(para(f"{i}. " + " ".join(rng.choice(WORDS) for _ in range(rng.randint(12, 40))) + "."))
    return ('<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
            '<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body>'
            + "".join(paras) + "</w:body></w:document>")


def build(path, n):
    parts = [("[Content_Types].xml", CT), ("_rels/.rels", RELS), ("word/document.xml", document(n))]
    with zipfile.ZipFile(path, "w", zipfile.ZIP_DEFLATED) as z:
        for name, text in parts:
            info = zipfile.ZipInfo(name, FIXED)
            info.compress_type = zipfile.ZIP_DEFLATED
            z.writestr(info, text.encode("utf-8"))


SIZES = {"small": 120, "large": 6000}  # paragraphs: about 6 KB and about 150 KB (a real contract's size)

if __name__ == "__main__":
    out_dir = sys.argv[1] if len(sys.argv) > 1 else os.path.dirname(os.path.abspath(__file__))
    for size, n in SIZES.items():
        out = os.path.join(out_dir, f"spaarke-file-probe-{size}.docx")
        build(out, n)
        data = open(out, "rb").read()
        print(os.path.basename(out), len(data), "bytes", "sha256", hashlib.sha256(data).hexdigest())
