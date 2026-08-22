"""Authors tests/fixtures/compose-corpus/comment-ranges-multiparagraph.docx (task 042).

The corpus had ZERO comment ranges in all 18 documents, so every comment-integrity assertion swept over an
empty set. FR-A11 is entirely about comment ranges, which made the sweep 18 green rows of nothing.

This fixture carries three shapes deliberately:
  1. A range SPANNING two paragraphs  — the clone/render boundary case.
  2. A point comment inside one paragraph — start/end adjacent.
  3. A second multi-paragraph range NESTED across a third and fourth paragraph, so two ranges are open at
     once and an implementation that tracks only "the current range" is caught.
"""
import os
import zipfile

W = 'http://schemas.openxmlformats.org/wordprocessingml/2006/main'
W14 = 'http://schemas.microsoft.com/office/word/2010/wordml'
MC = 'http://schemas.openxmlformats.org/markup-compatibility/2006'
R = 'http://schemas.openxmlformats.org/officeDocument/2006/relationships'

# NOTE (task 042): the XML declaration is emitted WITHOUT a trailing newline. The OpenXML SDK
# re-serializes any part it opens in exactly that shape, so a newline here makes the part differ by one
# byte after a round trip and trips the corpus 'untouched parts are byte-identical' harness on the
# op-log patch path -- an artifact of this generator, not content drift. The related finding (that the
# op-log path re-serializes comments.xml at all) is recorded in notes/merge-integrity-results.md.

OUT = 'tests/fixtures/compose-corpus/comment-ranges-multiparagraph.docx'

content_types = '''<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
<Default Extension="xml" ContentType="application/xml"/>
<Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
<Override PartName="/word/comments.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.comments+xml"/>
</Types>'''

root_rels = '''<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
</Relationships>'''

doc_rels = '''<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/comments" Target="comments.xml"/>
</Relationships>'''


def comment(cid, author, initials, text):
    return (f'<w:comment w:id="{cid}" w:author="{author}" w:initials="{initials}" '
            f'w:date="2026-08-22T09:00:00Z"><w:p w14:paraId="{cid}C000001" w14:textId="{cid}C000001">'
            f'<w:r><w:t xml:space="preserve">{text}</w:t></w:r></w:p></w:comment>')


comments = (f'<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
            f'<w:comments xmlns:w="{W}" xmlns:w14="{W14}" xmlns:mc="{MC}" mc:Ignorable="w14">'
            + comment('1', 'Reviewer One', 'R1', 'This obligation should survive termination.')
            + comment('2', 'Reviewer Two', 'R2', 'Check this defined term.')
            + comment('3', 'Reviewer One', 'R1', 'Confirm the notice period across both clauses.')
            + '</w:comments>')


def para(para_id, children):
    return f'<w:p w14:paraId="{para_id}" w14:textId="{para_id}">{children}</w:p>'


def run(text):
    return f'<w:r><w:t xml:space="preserve">{text}</w:t></w:r>'


def ref(cid):
    return f'<w:r><w:rPr><w:rStyle w:val="CommentReference"/></w:rPr><w:commentReference w:id="{cid}"/></w:r>'


body = (
    # 1. Range 1 SPANS paragraph 1 -> paragraph 2.
    para('5C000001',
         '<w:commentRangeStart w:id="1"/>'
         + run('Confidentiality. Each party shall protect the other party&#8217;s Confidential Information'))
    + para('5C000002',
           run(' using no less than reasonable care, and shall not disclose it to any third party.')
           + '<w:commentRangeEnd w:id="1"/>' + ref('1'))

    # 2. Point comment entirely inside paragraph 3.
    + para('5C000003',
           run('Defined Terms. ')
           + '<w:commentRangeStart w:id="2"/>' + run('Confidential Information')
           + '<w:commentRangeEnd w:id="2"/>' + ref('2')
           + run(' has the meaning given in Section 1.'))

    # 3. Range 3 spans paragraph 4 -> paragraph 5, so two shapes coexist in one document.
    + para('5C000004',
           '<w:commentRangeStart w:id="3"/>'
           + run('Notice. Any notice under this Agreement must be in writing.'))
    + para('5C000005',
           run('Notices are effective on receipt, or three business days after mailing.')
           + '<w:commentRangeEnd w:id="3"/>' + ref('3'))

    + '<w:sectPr><w:pgSz w:w="12240" w:h="15840"/>'
      '<w:pgMar w:top="1440" w:right="1440" w:bottom="1440" w:left="1440"/></w:sectPr>'
)

document = (f'<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
            f'<w:document xmlns:w="{W}" xmlns:w14="{W14}" xmlns:mc="{MC}" xmlns:r="{R}" mc:Ignorable="w14">'
            f'<w:body>{body}</w:body></w:document>')

os.makedirs(os.path.dirname(OUT), exist_ok=True)
with zipfile.ZipFile(OUT, 'w', zipfile.ZIP_DEFLATED) as z:
    z.writestr('[Content_Types].xml', content_types)
    z.writestr('_rels/.rels', root_rels)
    z.writestr('word/_rels/document.xml.rels', doc_rels)
    z.writestr('word/document.xml', document)
    z.writestr('word/comments.xml', comments)

print('wrote', OUT, os.path.getsize(OUT), 'bytes')
