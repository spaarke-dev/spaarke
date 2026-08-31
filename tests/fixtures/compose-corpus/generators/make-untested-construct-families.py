"""Authors the four corpus fixtures that close FR-A07's evidence gap (task 043).

Scanning the 19-document corpus for the construct families a capability gate would plausibly target
found six with ZERO coverage, so the gate's "zero hard-fails" said nothing about them:

    OLE object (w:object) · chart part · embedded OLE binary · embedded font · endnote · macro

Five of the six are covered here (macros are excluded — see the note at the bottom). Once these land,
ComposeCorpusFixtureLocator picks them up automatically (it globs *.docx), so every existing harness
measures them with no code change: the preservation oracle, the edited-block loss measurement, the
warning taxonomy, the merge-integrity sweep and the schema-validity check.

Each fixture puts the construct in a paragraph of its own, next to ordinary prose. That shape is what
makes the measurement mean something: the merge's contract is per-BLOCK, so a construct in a block the
user did not touch should clone byte-verbatim, and the same construct in the block they DID edit should
either survive or be reported. A fixture with only the construct and nothing else could not tell those
two cases apart.

Run from the repo root:  python tests/fixtures/compose-corpus/generators/make-untested-construct-families.py
"""
import os
import zipfile

W = 'http://schemas.openxmlformats.org/wordprocessingml/2006/main'
W14 = 'http://schemas.microsoft.com/office/word/2010/wordml'
MC = 'http://schemas.openxmlformats.org/markup-compatibility/2006'
R = 'http://schemas.openxmlformats.org/officeDocument/2006/relationships'
WP = 'http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing'
A = 'http://schemas.openxmlformats.org/drawingml/2006/main'
C = 'http://schemas.openxmlformats.org/drawingml/2006/chart'
V = 'urn:schemas-microsoft-com:vml'
O = 'urn:schemas-microsoft-com:office:office'

OUT_DIR = 'tests/fixtures/compose-corpus'

# NOTE (carried from make-comment-ranges-multiparagraph.py): the XML declaration is emitted WITHOUT a
# trailing newline. The OpenXML SDK re-serializes any part it opens in exactly that shape, so a newline
# makes the part differ by one byte after a round trip and trips the 'untouched parts are byte-identical'
# harness — an artifact of the generator, not content drift.
DECL = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'

SECT = ('<w:sectPr><w:pgSz w:w="12240" w:h="15840"/>'
        '<w:pgMar w:top="1440" w:right="1440" w:bottom="1440" w:left="1440"/></w:sectPr>')

ROOT_RELS = (DECL + '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">'
             '<Relationship Id="rId1" '
             'Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" '
             'Target="word/document.xml"/></Relationships>')

# A 1x1 transparent PNG — real bytes, so the package is genuinely well-formed rather than a stub.
PNG_1X1 = bytes.fromhex(
    '89504e470d0a1a0a0000000d49484452000000010000000108060000001f15c4'
    '890000000a49444154789c6300010000050001' '0d0a2db4' '0000000049454e44ae426082')


def para(para_id, children):
    return f'<w:p w14:paraId="{para_id}" w14:textId="{para_id}">{children}</w:p>'


def run(text):
    return f'<w:r><w:t xml:space="preserve">{text}</w:t></w:r>'


def content_types(overrides, defaults=()):
    parts = [DECL, '<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">',
             '<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>',
             '<Default Extension="xml" ContentType="application/xml"/>']
    parts += [f'<Default Extension="{e}" ContentType="{c}"/>' for e, c in defaults]
    parts.append('<Override PartName="/word/document.xml" '
                 'ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>')
    parts += [f'<Override PartName="{p}" ContentType="{c}"/>' for p, c in overrides]
    parts.append('</Types>')
    return ''.join(parts)


def doc_rels(rels):
    body = ''.join(f'<Relationship Id="{i}" Type="{t}" Target="{tg}"/>' for i, t, tg in rels)
    return (DECL + '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">'
            + body + '</Relationships>')


def document(body, extra_ns=''):
    return (DECL + f'<w:document xmlns:w="{W}" xmlns:w14="{W14}" xmlns:mc="{MC}" xmlns:r="{R}"'
            + extra_ns + ' mc:Ignorable="w14"><w:body>' + body + '</w:body></w:document>')


def write(name, files):
    path = os.path.join(OUT_DIR, name)
    with zipfile.ZipFile(path, 'w', zipfile.ZIP_DEFLATED) as z:
        for part, data in files:
            z.writestr(part, data)
    print('wrote', path, os.path.getsize(path), 'bytes')


# ═════════════════════════════════════════════════════════════════════════════════════════════════
# 1. OLE embedded object — covers BOTH "OLE object (w:object)" and "embedded OLE binary".
#    A real-world shape: an Excel range embedded in an engagement letter's fee schedule.
# ═════════════════════════════════════════════════════════════════════════════════════════════════

ole_body = (
    para('7A000001', run('Schedule B — Fee Basis.'))
    + para('7A000002', run('The parties agree the fees are calculated on the basis embedded below.'))
    + para('7A000003',
           '<w:r><w:object w:dxaOrig="1531" w:dyaOrig="994">'
           '<v:shape id="_x0000_i1025" type="#_x0000_t75" style="width:76.5pt;height:49.5pt" o:ole="">'
           '<v:imagedata r:id="rIdImg" o:title=""/></v:shape>'
           '<o:OLEObject Type="Embed" ProgID="Excel.Sheet.12" ShapeID="_x0000_i1025" '
           'DrawAspect="Content" ObjectID="_1712345678" r:id="rIdOle"/>'
           '</w:object></w:r>')
    + para('7A000004', run('Fees are payable within thirty (30) days of invoice.'))
    + SECT)

write('ole-embedded-object.docx', [
    ('[Content_Types].xml', content_types(
        overrides=[],
        defaults=[('png', 'image/png'),
                  ('bin', 'application/vnd.openxmlformats-officedocument.oleObject')])),
    ('_rels/.rels', ROOT_RELS),
    ('word/_rels/document.xml.rels', doc_rels([
        ('rIdImg', R + '/image', 'media/image1.png'),
        ('rIdOle', R + '/oleObject', 'embeddings/oleObject1.bin'),
    ])),
    ('word/document.xml', document(ole_body, extra_ns=f' xmlns:v="{V}" xmlns:o="{O}"')),
    ('word/media/image1.png', PNG_1X1),
    # Not a real OLE compound file — the merge never parses it, which is exactly the property under
    # test: an untouched block clones the relationship and the part survives byte-for-byte.
    ('word/embeddings/oleObject1.bin', b'\xd0\xcf\x11\xe0\xa1\xb1\x1a\xe1' + b'\x00' * 56),
])

# ═════════════════════════════════════════════════════════════════════════════════════════════════
# 2. Embedded chart — covers "chart part". FR-A07's own example text names embedded charts.
# ═════════════════════════════════════════════════════════════════════════════════════════════════

chart_xml = (
    DECL + f'<c:chartSpace xmlns:c="{C}" xmlns:a="{A}" xmlns:r="{R}">'
    '<c:chart><c:plotArea><c:layout/>'
    '<c:barChart><c:barDir val="col"/><c:grouping val="clustered"/><c:varyColors val="0"/>'
    '<c:ser><c:idx val="0"/><c:order val="0"/>'
    '<c:val><c:numRef><c:f>Sheet1!$B$1:$B$3</c:f></c:numRef></c:val></c:ser>'
    '<c:axId val="111111111"/><c:axId val="222222222"/></c:barChart>'
    '<c:catAx><c:axId val="111111111"/><c:scaling><c:orientation val="minMax"/></c:scaling>'
    '<c:delete val="0"/><c:axPos val="b"/><c:crossAx val="222222222"/></c:catAx>'
    '<c:valAx><c:axId val="222222222"/><c:scaling><c:orientation val="minMax"/></c:scaling>'
    '<c:delete val="0"/><c:axPos val="l"/><c:crossAx val="111111111"/></c:valAx>'
    '</c:plotArea><c:plotVisOnly val="1"/></c:chart></c:chartSpace>')

chart_body = (
    para('7B000001', run('Exhibit C — Quarterly Volume.'))
    + para('7B000002', run('Volumes for the trailing three quarters are charted below.'))
    + para('7B000003',
           '<w:r><w:drawing>'
           '<wp:inline distT="0" distB="0" distL="0" distR="0">'
           '<wp:extent cx="5486400" cy="3200400"/>'
           '<wp:docPr id="1" name="Chart 1"/>'
           f'<a:graphic><a:graphicData uri="{C}">'
           f'<c:chart xmlns:c="{C}" r:id="rIdChart"/>'
           '</a:graphicData></a:graphic>'
           '</wp:inline></w:drawing></w:r>')
    + para('7B000004', run('Volumes are measured at the end of each calendar quarter.'))
    + SECT)

write('chart-embedded.docx', [
    ('[Content_Types].xml', content_types(overrides=[
        ('/word/charts/chart1.xml',
         'application/vnd.openxmlformats-officedocument.drawingml.chart+xml')])),
    ('_rels/.rels', ROOT_RELS),
    ('word/_rels/document.xml.rels', doc_rels([
        ('rIdChart', R + '/chart', 'charts/chart1.xml'),
    ])),
    ('word/document.xml', document(chart_body, extra_ns=f' xmlns:wp="{WP}" xmlns:a="{A}"')),
    ('word/charts/chart1.xml', chart_xml),
])

# ═════════════════════════════════════════════════════════════════════════════════════════════════
# 3. Endnote references — the sibling of footnote-references.docx, which the corpus already has.
#    Worth its own fixture precisely BECAUSE they look interchangeable: endnotes live in a different
#    part, carry a different reference element, and were never once exercised.
# ═════════════════════════════════════════════════════════════════════════════════════════════════

endnotes_xml = (
    DECL + f'<w:endnotes xmlns:w="{W}" xmlns:w14="{W14}" xmlns:mc="{MC}" mc:Ignorable="w14">'
    '<w:endnote w:type="separator" w:id="-1"><w:p><w:pPr>'
    '<w:spacing w:after="0" w:line="240" w:lineRule="auto"/></w:pPr>'
    '<w:r><w:separator/></w:r></w:p></w:endnote>'
    '<w:endnote w:type="continuationSeparator" w:id="0"><w:p><w:pPr>'
    '<w:spacing w:after="0" w:line="240" w:lineRule="auto"/></w:pPr>'
    '<w:r><w:continuationSeparator/></w:r></w:p></w:endnote>'
    '<w:endnote w:id="1"><w:p w14:paraId="7C00E001" w14:textId="7C00E001"><w:r>'
    '<w:t xml:space="preserve">See the Master Services Agreement dated 1 March 2026.</w:t>'
    '</w:r></w:p></w:endnote>'
    '<w:endnote w:id="2"><w:p w14:paraId="7C00E002" w14:textId="7C00E002"><w:r>'
    '<w:t xml:space="preserve">Capitalised terms are defined in Section 1.</w:t>'
    '</w:r></w:p></w:endnote>'
    '</w:endnotes>')


def endnote_ref(eid):
    return ('<w:r><w:rPr><w:rStyle w:val="EndnoteReference"/></w:rPr>'
            f'<w:endnoteReference w:id="{eid}"/></w:r>')


endnote_body = (
    para('7C000001', run('Governing Agreement.'))
    + para('7C000002',
           run('This Statement of Work is issued under the Master Agreement')
           + endnote_ref('1')
           + run(' and is subject to its terms.'))
    + para('7C000003',
           run('Defined terms carry the meanings given to them')
           + endnote_ref('2')
           + run(' unless the context requires otherwise.'))
    + SECT)

write('endnote-references.docx', [
    ('[Content_Types].xml', content_types(overrides=[
        ('/word/endnotes.xml',
         'application/vnd.openxmlformats-officedocument.wordprocessingml.endnotes+xml')])),
    ('_rels/.rels', ROOT_RELS),
    ('word/_rels/document.xml.rels', doc_rels([
        ('rIdEn', R + '/endnotes', 'endnotes.xml'),
    ])),
    ('word/document.xml', document(endnote_body)),
    ('word/endnotes.xml', endnotes_xml),
])

# ═════════════════════════════════════════════════════════════════════════════════════════════════
# 4. Embedded font — a firm-branded template's obfuscated font. The font lives in a part the body
#    never references directly, so this fixture asks a different question from the others: does a
#    package-level part survive a save at all?
# ═════════════════════════════════════════════════════════════════════════════════════════════════

font_table = (
    DECL + f'<w:fonts xmlns:w="{W}" xmlns:r="{R}">'
    '<w:font w:name="Spaarke Serif">'
    '<w:panose1 w:val="02020603050405020304"/>'
    '<w:charset w:val="00"/>'
    '<w:family w:val="roman"/>'
    '<w:pitch w:val="variable"/>'
    '<w:embedRegular r:id="rIdFont" w:fontKey="{3E2B4C1A-9F55-4E7D-8A21-5C6D7E8F9A0B}"/>'
    '</w:font></w:fonts>')

font_body = (
    para('7D000001',
         '<w:pPr><w:rPr><w:rFonts w:ascii="Spaarke Serif" w:hAnsi="Spaarke Serif"/></w:rPr></w:pPr>'
         '<w:r><w:rPr><w:rFonts w:ascii="Spaarke Serif" w:hAnsi="Spaarke Serif"/></w:rPr>'
         '<w:t xml:space="preserve">Memorandum of Understanding</w:t></w:r>')
    + para('7D000002', run('The parties record their common understanding as set out below.'))
    + para('7D000003', run('This memorandum is not intended to create legally binding obligations.'))
    + SECT)

write('embedded-font.docx', [
    ('[Content_Types].xml', content_types(
        overrides=[('/word/fontTable.xml',
                    'application/vnd.openxmlformats-officedocument.wordprocessingml.fontTable+xml')],
        defaults=[('odttf', 'application/vnd.openxmlformats-officedocument.obfuscatedFont')])),
    ('_rels/.rels', ROOT_RELS),
    ('word/_rels/document.xml.rels', doc_rels([
        ('rIdFontTable', R + '/fontTable', 'fontTable.xml'),
    ])),
    ('word/document.xml', document(font_body)),
    ('word/fontTable.xml', font_table),
    ('word/fonts/font1.odttf', b'\x00' * 96),
])

# The font part is related FROM fontTable.xml, not from document.xml.
with zipfile.ZipFile(os.path.join(OUT_DIR, 'embedded-font.docx'), 'a', zipfile.ZIP_DEFLATED) as z:
    z.writestr('word/_rels/fontTable.xml.rels', doc_rels([
        ('rIdFont', R + '/font', 'fonts/font1.odttf'),
    ]))

# ═════════════════════════════════════════════════════════════════════════════════════════════════
# MACROS ARE DELIBERATELY NOT COVERED.
#
# A vbaProject.bin makes the package macro-enabled, which is a .docm — a different content type and a
# different file extension. Dropping one into a .docx would author a fixture that is invalid by
# construction, and the corpus locator globs *.docx so a real .docm would never be enumerated anyway.
# Compose's editable gate also routes on extension, so a .docm does not reach the merge today.
#
# Recorded as an open question in notes/capability-gate-triggers.md rather than faked here. A fixture
# that is wrong in the same way for every run is worse than a gap that is written down.
# ═════════════════════════════════════════════════════════════════════════════════════════════════
print('\nmacro (vbaProject): deliberately not covered — see the note at the end of this generator.')
