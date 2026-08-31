"""Authors `nested-merge-fields.docx` — the corpus fixture for a CONDITIONAL MERGE BLOCK (task 058).

Why this fixture had to exist: the corpus covered ordinary fields (`ref-cross-references.docx` — a
`w:fldSimple` REF and a `w:fldChar` PAGEREF, both carried since task 049) but contained NO nested field
at all. The shape the owner asked about on 2026-08-25 — "we will be introducing templates and
field-merge-codes, will these be supported?" — is not the bare `{ MERGEFIELD Party }` that already
round-trips. It is the CONDITIONAL merge block a real template is built from:

    { IF { MERGEFIELD State } = "California" "...CA text..." "...{ MERGEFIELD State } text..." }

an `IF` field whose condition AND whose false branch both contain `MERGEFIELD`s. Task 049 flattened that
by design (`notes/049-field-carry-decisions.md` sections 2 and 3), and until this fixture existed the only
nested field anywhere in the repo was a synthetic XML fragment inside two test files. A carry proven on a
fragment proves only that the fragment works; ADR-038 wants the real seam.

What this document deliberately contains, and why each part earns its place:

  * `w:noProof` on every field RESULT run. Word writes it on merge results, and it is the run property
    the task-049 scalar carry does NOT preserve (`COMPOSE-WRITE-RESIDUAL-LOSS.md` section 3, "what a
    carried field does not keep"). Its presence makes the difference between a re-authored look-alike
    and a verbatim carry MEASURABLE rather than argued.
  * `w:b` on one result run — the same point for a property the scalar carry does keep, so a test can
    tell "carried verbatim" apart from "re-authored from the model's three marks".
  * A DOUBLE nesting (`{ IF { MERGEFIELD } ... { MERGEFIELD } ... }` — two inner fields, one in the
    condition and one in the false branch). Depth is still 2, but the outer instruction is split across
    THREE `w:instrText` runs rather than two, which is what an instruction-reconstructing carry would
    have to reassemble and a verbatim carry never touches.
  * A PLAIN `{ MERGEFIELD ClientName }` in its own block. It is the already-carried control arm living
    in the SAME document, so the flat-scan non-regression is measurable on the same fixture rather than
    inferred from a different one.
  * A nested field alone in a block AND one mid-sentence, because the merge's contract is per-block and
    the two positions exercise different halves of it.

Layout mirrors the other construct fixtures: ordinary prose around the construct blocks, so the merge's
per-BLOCK contract can be measured in both positions (untouched -> cloned verbatim; edited -> the only
place loss can occur).

`ComposeCorpusFixtureLocator` globs `*.docx`, so this lands in every existing harness with no code change.

Run from the repo root:  python tests/fixtures/compose-corpus/generators/make-nested-merge-fields.py
"""
import os
import zipfile

W = 'http://schemas.openxmlformats.org/wordprocessingml/2006/main'
W14 = 'http://schemas.microsoft.com/office/word/2010/wordml'
MC = 'http://schemas.openxmlformats.org/markup-compatibility/2006'
R = 'http://schemas.openxmlformats.org/officeDocument/2006/relationships'

OUT_DIR = 'tests/fixtures/compose-corpus'

# NOTE (carried from the sibling generators): the XML declaration is emitted WITHOUT a trailing newline.
# The OpenXML SDK re-serializes any part it opens in exactly that shape, so a newline makes the part differ
# by one byte after a round trip and trips the 'untouched parts are byte-identical' harness.
DECL = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'

SECT = ('<w:sectPr><w:pgSz w:w="12240" w:h="15840"/>'
        '<w:pgMar w:top="1440" w:right="1440" w:bottom="1440" w:left="1440"/></w:sectPr>')

ROOT_RELS = (DECL + '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">'
             '<Relationship Id="rId1" '
             'Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" '
             'Target="word/document.xml"/></Relationships>')


def para(para_id, children):
    return '<w:p w14:paraId="' + para_id + '" w14:textId="' + para_id + '">' + children + '</w:p>'


def run(text):
    return '<w:r><w:t xml:space="preserve">' + text + '</w:t></w:r>'


def begin():
    return '<w:r><w:fldChar w:fldCharType="begin"/></w:r>'


def separate():
    return '<w:r><w:fldChar w:fldCharType="separate"/></w:r>'


def end():
    return '<w:r><w:fldChar w:fldCharType="end"/></w:r>'


def instr(text):
    return '<w:r><w:instrText xml:space="preserve">' + text + '</w:instrText></w:r>'


def result(text, bold=False):
    props = '<w:rPr>' + ('<w:b/>' if bold else '') + '<w:noProof/></w:rPr>'
    return '<w:r>' + props + '<w:t xml:space="preserve">' + text + '</w:t></w:r>'


def mergefield(name, value):
    """A plain MERGEFIELD complex field, in Word's own authoring shape."""
    return (begin()
            + instr(' MERGEFIELD  ' + name + '  \\* MERGEFORMAT ')
            + separate()
            + result(value)
            + end())


# -- The conditional merge block ---------------------------------------------------------------------
# { IF { MERGEFIELD State } = "California" "..." "...{ MERGEFIELD State }..." }
#
# Depth 2. The OUTER instruction is split across three w:instrText runs (' IF ', the comparison, and the
# tail after the second inner field), which is exactly the split that makes an instruction-reconstructing
# carry impossible: the scan can only recover the CONCATENATION of all five instruction runs.
CONDITIONAL_MERGE = (
    begin()
    + instr(' IF ')
    + mergefield('State', 'California')
    + instr(' = "California" "This Agreement is governed by the laws of the State of California." "This '
            'Agreement is governed by the laws of ')
    + mergefield('State', 'California')
    + instr('." ')
    + separate()
    + result('This Agreement is governed by the laws of the State of California.', bold=True)
    + end())

# A second, smaller conditional -- mid-sentence rather than alone in its block.
INLINE_CONDITIONAL = (
    begin()
    + instr(' IF ')
    + mergefield('Entity', 'Delaware corporation')
    + instr(' = "Delaware corporation" "a Delaware corporation" "an entity organised under applicable law" ')
    + separate()
    + result('a Delaware corporation')
    + end())

body = (
    para('7E000001', run('Schedule 2 - Governing Law.'))
    + para('7E000002', run('This Schedule is entered into by ')
           + mergefield('ClientName', 'Acme Holdings, Inc.') + run('.'))
    + para('7E000003', run('The Client, ') + INLINE_CONDITIONAL + run(', agrees as follows.'))
    + para('7E000004', CONDITIONAL_MERGE)
    + para('7E000005', run('Executed as of the date first written above.'))
    + SECT)

document = (DECL + '<w:document xmlns:w="' + W + '" xmlns:w14="' + W14 + '" xmlns:mc="' + MC
            + '" xmlns:r="' + R + '" mc:Ignorable="w14"><w:body>' + body + '</w:body></w:document>')

content_types = (DECL + '<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">'
                 '<Default Extension="rels" '
                 'ContentType="application/vnd.openxmlformats-package.relationships+xml"/>'
                 '<Default Extension="xml" ContentType="application/xml"/>'
                 '<Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-'
                 'officedocument.wordprocessingml.document.main+xml"/></Types>')

path = os.path.join(OUT_DIR, 'nested-merge-fields.docx')
with zipfile.ZipFile(path, 'w', zipfile.ZIP_DEFLATED) as z:
    z.writestr('[Content_Types].xml', content_types)
    z.writestr('_rels/.rels', ROOT_RELS)
    z.writestr('word/document.xml', document)
print('wrote', path, os.path.getsize(path), 'bytes')
