"""Authors `inline-image.docx` — the corpus fixture for a REAL inline image (task 056).

Why this fixture had to exist: the corpus covered a chart (`w:drawing` -> `c:chart r:id`) and an OLE
embed (`w:object` -> VML `v:imagedata r:id`), but NOT the single most common embedded object in a legal
document — a picture inlined in a paragraph, `w:drawing` > `wp:inline` > `a:graphic` > `pic:pic` >
`a:blip r:embed`. That is the shape whose relationship the task-056 carry had to prove resolves in the
SAVED package, and proving it on a construct the corpus did not contain would have proved nothing.

The image is a real 1x1 PNG (same bytes as `ole-embedded-object.docx`), so the package is genuinely
well-formed rather than a stub, and `word/media/image1.png` is a part a reader can actually resolve.

Layout mirrors the other construct fixtures: the object sits in a paragraph of its own between ordinary
prose, so the merge's per-BLOCK contract can be measured in both positions (untouched -> cloned verbatim;
edited -> the only place loss can occur).

`ComposeCorpusFixtureLocator` globs `*.docx`, so this lands in every existing harness with no code change.

Run from the repo root:  python tests/fixtures/compose-corpus/generators/make-inline-image.py
"""
import os
import zipfile

W = 'http://schemas.openxmlformats.org/wordprocessingml/2006/main'
W14 = 'http://schemas.microsoft.com/office/word/2010/wordml'
MC = 'http://schemas.openxmlformats.org/markup-compatibility/2006'
R = 'http://schemas.openxmlformats.org/officeDocument/2006/relationships'
WP = 'http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing'
A = 'http://schemas.openxmlformats.org/drawingml/2006/main'
PIC = 'http://schemas.openxmlformats.org/drawingml/2006/picture'

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

PNG_1X1 = bytes.fromhex(
    '89504e470d0a1a0a0000000d49484452000000010000000108060000001f15c4'
    '890000000a49444154789c6300010000050001' '0d0a2db4' '0000000049454e44ae426082')


def para(para_id, children):
    return f'<w:p w14:paraId="{para_id}" w14:textId="{para_id}">{children}</w:p>'


def run(text):
    return f'<w:r><w:t xml:space="preserve">{text}</w:t></w:r>'


IMAGE_RUN = (
    '<w:r><w:drawing>'
    '<wp:inline distT="0" distB="0" distL="0" distR="0">'
    '<wp:extent cx="914400" cy="914400"/>'
    '<wp:effectExtent l="0" t="0" r="0" b="0"/>'
    '<wp:docPr id="1" name="Picture 1" descr="Executed signature block"/>'
    '<wp:cNvGraphicFramePr>'
    f'<a:graphicFrameLocks xmlns:a="{A}" noChangeAspect="1"/>'
    '</wp:cNvGraphicFramePr>'
    f'<a:graphic xmlns:a="{A}"><a:graphicData uri="{PIC}">'
    f'<pic:pic xmlns:pic="{PIC}">'
    '<pic:nvPicPr><pic:cNvPr id="1" name="signature.png"/><pic:cNvPicPr/></pic:nvPicPr>'
    f'<pic:blipFill><a:blip r:embed="rIdImg"/><a:stretch><a:fillRect/></a:stretch></pic:blipFill>'
    '<pic:spPr><a:xfrm><a:off x="0" y="0"/><a:ext cx="914400" cy="914400"/></a:xfrm>'
    '<a:prstGeom prst="rect"><a:avLst/></a:prstGeom></pic:spPr>'
    '</pic:pic>'
    '</a:graphicData></a:graphic>'
    '</wp:inline></w:drawing></w:r>')

body = (
    para('7D000001', run('Execution Page.'))
    + para('7D000002', run('The parties have executed this Agreement as of the date first written above.'))
    + para('7D000003', IMAGE_RUN)
    + para('7D000004', run('Signed for and on behalf of the Company.'))
    + SECT)

document = (DECL + f'<w:document xmlns:w="{W}" xmlns:w14="{W14}" xmlns:mc="{MC}" xmlns:r="{R}"'
            f' xmlns:wp="{WP}" mc:Ignorable="w14"><w:body>' + body + '</w:body></w:document>')

content_types = (DECL + '<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">'
                 '<Default Extension="rels" '
                 'ContentType="application/vnd.openxmlformats-package.relationships+xml"/>'
                 '<Default Extension="xml" ContentType="application/xml"/>'
                 '<Default Extension="png" ContentType="image/png"/>'
                 '<Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-'
                 'officedocument.wordprocessingml.document.main+xml"/></Types>')

doc_rels = (DECL + '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">'
            f'<Relationship Id="rIdImg" Type="{R}/image" Target="media/image1.png"/>'
            '</Relationships>')

path = os.path.join(OUT_DIR, 'inline-image.docx')
with zipfile.ZipFile(path, 'w', zipfile.ZIP_DEFLATED) as z:
    z.writestr('[Content_Types].xml', content_types)
    z.writestr('_rels/.rels', ROOT_RELS)
    z.writestr('word/_rels/document.xml.rels', doc_rels)
    z.writestr('word/document.xml', document)
    z.writestr('word/media/image1.png', PNG_1X1)
print('wrote', path, os.path.getsize(path), 'bytes')
