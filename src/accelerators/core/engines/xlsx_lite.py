"""xlsx_lite.py - write a small .xlsx workbook with styled cells, links and native bar charts.

Python 3 standard library only (zipfile + string XML). No openpyxl: the Cowork sandbox may not
have it, and skills ship stdlib + lxml only. Charts carry cached values so previewers that do not
recalculate still draw them.

Library use:
  wb = Workbook()
  ws = wb.sheet("Summary")
  ws.append(["Rating", "Topics"], style="header")
  ws.append(["RED", 6]); ws.append(["YELLOW", 7])
  ws.widths = [18, 10]; ws.freeze = (1, 0)          # rows, cols frozen
  ws.add_chart(title="Topics by rating", cats=(2, 1, 3, 1), series=[("Topics", (2, 2, 3, 2), "d03b3b")])
  wb.save("out.xlsx")

A cell value is str, int, float, None, or a dict {"v": value, "s": style, "link": url}.
Styles: header, bold, title, wrap, link, GREEN, YELLOW, RED, grey.

CLI: `python xlsx_lite.py --selftest OUT.xlsx` writes a demo workbook. Exit codes: 0 ok, 2 bad input.
"""
import argparse
import re
import sys
import zipfile

STYLES = {None: 0, "header": 1, "GREEN": 2, "YELLOW": 3, "RED": 4, "wrap": 5, "grey": 6, "link": 7,
          "bold": 8, "title": 9}
_ILLEGAL = re.compile(r"[\x00-\x08\x0b\x0c\x0e-\x1f]")
_BAD_SHEET = re.compile(r"[\[\]:*?/\\]")

NS_MAIN = "http://schemas.openxmlformats.org/spreadsheetml/2006/main"
NS_R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships"
NS_C = "http://schemas.openxmlformats.org/drawingml/2006/chart"
NS_A = "http://schemas.openxmlformats.org/drawingml/2006/main"
NS_XDR = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing"
REL = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/"
PKG_REL = "http://schemas.openxmlformats.org/package/2006/relationships"
CT = "application/vnd.openxmlformats-officedocument."


def col_letter(c):  # 1-based
    s = ""
    while c:
        c, r = divmod(c - 1, 26)
        s = chr(65 + r) + s
    return s


def ref(row, col, absolute=False):
    d = "$" if absolute else ""
    return f"{d}{col_letter(col)}{d}{row}"


def _x(text):
    t = _ILLEGAL.sub("", str(text))
    return t.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;").replace('"', "&quot;")


class Sheet:
    def __init__(self, name):
        name = _BAD_SHEET.sub("-", name)[:31] or "Sheet"
        self.name = name
        self.rows = []          # list of lists of cell dicts
        self.widths = []
        self.freeze = None      # (rows, cols)
        self.autofilter = False
        self.charts = []

    def append(self, values, style=None):
        row = []
        for v in values:
            cell = dict(v) if isinstance(v, dict) else {"v": v}
            cell.setdefault("s", style)
            row.append(cell)
        self.rows.append(row)
        return len(self.rows)   # 1-based row number

    def add_chart(self, title, cats, series, anchor=None, size=(8, 16), bar_dir="col", stacked=False):
        """cats / series refs are (row1, col1, row2, col2) on THIS sheet; series = [(name, ref, rgb)] or
        [(name, ref, rgb, point_rgbs)], where point_rgbs colours each bar in category order (None keeps rgb)."""
        self.charts.append({"title": title, "cats": cats, "series": series, "anchor": anchor,
                            "size": size, "bar_dir": bar_dir, "stacked": stacked})

    def value(self, r, c):
        try:
            return self.rows[r - 1][c - 1].get("v")
        except IndexError:
            return None

    def quoted(self):
        return "'" + self.name.replace("'", "''") + "'"


class Workbook:
    def __init__(self):
        self.sheets = []

    def sheet(self, name):
        base, n = name, 2
        while any(s.name.lower() == _BAD_SHEET.sub("-", name)[:31].lower() for s in self.sheets):
            name = f"{base[:28]} {n}"
            n += 1
        s = Sheet(name)
        self.sheets.append(s)
        return s

    # ---------- parts ----------
    def _sheet_xml(self, s, idx, links, has_drawing):
        out = [f'<worksheet xmlns="{NS_MAIN}" xmlns:r="{NS_R}">']
        if s.freeze:
            fr, fc = s.freeze
            pane = "bottomRight" if fr and fc else ("bottomLeft" if fr else "topRight")
            attrs = (f' xSplit="{fc}"' if fc else "") + (f' ySplit="{fr}"' if fr else "")
            out.append(f'<sheetViews><sheetView workbookViewId="0"><pane{attrs} topLeftCell="{ref(fr + 1, fc + 1)}" '
                       f'activePane="{pane}" state="frozen"/></sheetView></sheetViews>')
        else:
            out.append('<sheetViews><sheetView workbookViewId="0"/></sheetViews>')
        out.append('<sheetFormatPr defaultRowHeight="15"/>')
        if s.widths:
            out.append("<cols>" + "".join(f'<col min="{i}" max="{i}" width="{w}" customWidth="1"/>'
                                          for i, w in enumerate(s.widths, 1) if w) + "</cols>")
        out.append("<sheetData>")
        for r, row in enumerate(s.rows, 1):
            cells = []
            for c, cell in enumerate(row, 1):
                v, st = cell.get("v"), STYLES.get(cell.get("s"), 0)
                if cell.get("link"):
                    links.append((ref(r, c), cell["link"]))
                    if st == 0:
                        st = STYLES["link"]
                sa = f' s="{st}"' if st else ""
                if v is None or v == "":
                    if st:
                        cells.append(f'<c r="{ref(r, c)}"{sa}/>')
                elif isinstance(v, bool) or not isinstance(v, (int, float)):
                    t = _x(v)[:32000]
                    cells.append(f'<c r="{ref(r, c)}"{sa} t="inlineStr"><is><t xml:space="preserve">{t}</t></is></c>')
                else:
                    cells.append(f'<c r="{ref(r, c)}"{sa}><v>{v}</v></c>')
            out.append(f'<row r="{r}">' + "".join(cells) + "</row>")
        out.append("</sheetData>")
        if s.autofilter and s.rows:
            out.append(f'<autoFilter ref="A1:{ref(len(s.rows), max(len(r) for r in s.rows))}"/>')
        if links:
            out.append("<hyperlinks>" + "".join(f'<hyperlink ref="{a}" r:id="rIdL{i}"/>'
                                                 for i, (a, _) in enumerate(links, 1)) + "</hyperlinks>")
        out.append('<pageMargins left="0.5" right="0.5" top="0.75" bottom="0.75" header="0.3" footer="0.3"/>')
        if has_drawing:
            out.append('<drawing r:id="rIdD1"/>')
        out.append("</worksheet>")
        return "".join(out)

    def _chart_xml(self, s, ch):
        def rng(t):
            r1, c1, r2, c2 = t
            return f"{s.quoted()}!{ref(r1, c1, True)}:{ref(r2, c2, True)}"

        def cells(t):
            r1, c1, r2, c2 = t
            return [s.value(r, c) for r in range(r1, r2 + 1) for c in range(c1, c2 + 1)]

        cats = cells(ch["cats"])
        cat_cache = f'<c:strCache><c:ptCount val="{len(cats)}"/>' + "".join(
            f'<c:pt idx="{i}"><c:v>{_x(v if v is not None else "")}</c:v></c:pt>' for i, v in enumerate(cats)) + "</c:strCache>"
        sers = []
        for i, (name, t, rgb, *rest) in enumerate(ch["series"]):
            vals = cells(t)
            num = f'<c:numCache><c:formatCode>General</c:formatCode><c:ptCount val="{len(vals)}"/>' + "".join(
                f'<c:pt idx="{j}"><c:v>{v}</c:v></c:pt>' for j, v in enumerate(vals) if isinstance(v, (int, float))) + "</c:numCache>"
            # CT_BarSer order: idx, order, tx, spPr, invertIfNegative, dPt*, cat, val
            dpts = "".join(
                f'<c:dPt><c:idx val="{j}"/><c:invertIfNegative val="0"/><c:bubble3D val="0"/>'
                f'<c:spPr><a:solidFill><a:srgbClr val="{p}"/></a:solidFill></c:spPr></c:dPt>'
                for j, p in enumerate(rest[0] if rest and rest[0] else []) if p)
            sers.append(
                f'<c:ser><c:idx val="{i}"/><c:order val="{i}"/>'
                f'<c:tx><c:v>{_x(name)}</c:v></c:tx>'
                f'<c:spPr><a:solidFill><a:srgbClr val="{rgb}"/></a:solidFill></c:spPr>'
                f'<c:invertIfNegative val="0"/>{dpts}'
                f'<c:cat><c:strRef><c:f>{rng(ch["cats"])}</c:f>{cat_cache}</c:strRef></c:cat>'
                f'<c:val><c:numRef><c:f>{rng(t)}</c:f>{num}</c:numRef></c:val></c:ser>')
        horiz = ch["bar_dir"] == "bar"
        grouping = "stacked" if ch["stacked"] else "clustered"
        overlap = '<c:overlap val="100"/>' if ch["stacked"] else ""
        legend = '<c:legend><c:legendPos val="b"/><c:overlay val="0"/></c:legend>' if len(sers) > 1 else ""
        return (
            f'<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
            f'<c:chartSpace xmlns:c="{NS_C}" xmlns:a="{NS_A}" xmlns:r="{NS_R}"><c:roundedCorners val="0"/><c:chart>'
            f'<c:title><c:tx><c:rich><a:bodyPr/><a:p><a:pPr><a:defRPr sz="1200" b="1"/></a:pPr><a:r><a:rPr lang="en-US" sz="1200" b="1"/>'
            f'<a:t>{_x(ch["title"])}</a:t></a:r></a:p></c:rich></c:tx><c:overlay val="0"/></c:title>'
            f'<c:autoTitleDeleted val="0"/><c:plotArea><c:layout/>'
            f'<c:barChart><c:barDir val="{"bar" if horiz else "col"}"/><c:grouping val="{grouping}"/><c:varyColors val="0"/>'
            + "".join(sers) +
            f'<c:gapWidth val="60"/>{overlap}<c:axId val="5001"/><c:axId val="5002"/></c:barChart>'
            f'<c:catAx><c:axId val="5001"/><c:scaling><c:orientation val="{"maxMin" if horiz else "minMax"}"/></c:scaling>'
            f'<c:delete val="0"/><c:axPos val="{"l" if horiz else "b"}"/><c:numFmt formatCode="General" sourceLinked="0"/>'
            f'<c:majorTickMark val="none"/><c:minorTickMark val="none"/><c:tickLblPos val="nextTo"/>'
            f'<c:crossAx val="5002"/><c:crosses val="autoZero"/><c:auto val="1"/><c:lblAlgn val="ctr"/><c:lblOffset val="100"/></c:catAx>'
            f'<c:valAx><c:axId val="5002"/><c:scaling><c:orientation val="minMax"/></c:scaling><c:delete val="0"/>'
            f'<c:axPos val="{"b" if horiz else "l"}"/><c:majorGridlines><c:spPr><a:ln w="6350"><a:solidFill><a:srgbClr val="D9D9D9"/></a:solidFill></a:ln></c:spPr></c:majorGridlines>'
            f'<c:numFmt formatCode="0" sourceLinked="0"/><c:majorTickMark val="none"/><c:minorTickMark val="none"/>'
            f'<c:tickLblPos val="nextTo"/><c:crossAx val="5001"/><c:crosses val="{"max" if horiz else "autoZero"}"/>'
            f'<c:crossBetween val="between"/><c:majorUnit val="1"/></c:valAx></c:plotArea>'
            f'{legend}<c:plotVisOnly val="1"/><c:dispBlanksAs val="gap"/></c:chart></c:chartSpace>')

    def _drawing_xml(self, s, first_chart_no):
        anchors = []
        width = max((len(r) for r in s.rows), default=0)
        for i, ch in enumerate(s.charts):
            c0, r0 = ch["anchor"] or (width + 1, 1 + i * (ch["size"][1] + 1))
            c1, r1 = c0 + ch["size"][0], r0 + ch["size"][1]
            anchors.append(
                f'<xdr:twoCellAnchor><xdr:from><xdr:col>{c0}</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>{r0}</xdr:row>'
                f'<xdr:rowOff>0</xdr:rowOff></xdr:from><xdr:to><xdr:col>{c1}</xdr:col><xdr:colOff>0</xdr:colOff>'
                f'<xdr:row>{r1}</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:to><xdr:graphicFrame macro="">'
                f'<xdr:nvGraphicFramePr><xdr:cNvPr id="{i + 2}" name="Chart {first_chart_no + i}"/><xdr:cNvGraphicFramePr/>'
                f'</xdr:nvGraphicFramePr><xdr:xfrm><a:off x="0" y="0"/><a:ext cx="0" cy="0"/></xdr:xfrm><a:graphic>'
                f'<a:graphicData uri="{NS_C}"><c:chart xmlns:c="{NS_C}" r:id="rIdC{i + 1}"/></a:graphicData></a:graphic>'
                f'</xdr:graphicFrame><xdr:clientData/></xdr:twoCellAnchor>')
        return (f'<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
                f'<xdr:wsDr xmlns:xdr="{NS_XDR}" xmlns:a="{NS_A}" xmlns:r="{NS_R}">' + "".join(anchors) + "</xdr:wsDr>")

    @staticmethod
    def _styles():
        fonts = ['<font><sz val="11"/><name val="Calibri"/><family val="2"/></font>',
                 '<font><b/><sz val="11"/><name val="Calibri"/><family val="2"/></font>',
                 '<font><u/><sz val="11"/><color rgb="FF0563C1"/><name val="Calibri"/><family val="2"/></font>',
                 '<font><b/><sz val="14"/><name val="Calibri"/><family val="2"/></font>',
                 '<font><b/><sz val="11"/><color rgb="FF0B5E0B"/><name val="Calibri"/><family val="2"/></font>',
                 '<font><b/><sz val="11"/><color rgb="FF0B0B0B"/><name val="Calibri"/><family val="2"/></font>',
                 '<font><b/><sz val="11"/><color rgb="FF8B1A1A"/><name val="Calibri"/><family val="2"/></font>']
        fill = lambda rgb: f'<fill><patternFill patternType="solid"><fgColor rgb="FF{rgb}"/><bgColor indexed="64"/></patternFill></fill>'  # noqa: E731
        fills = ['<fill><patternFill patternType="none"/></fill>', '<fill><patternFill patternType="gray125"/></fill>',
                 fill("E7E6E6"), fill("D6F0D6"), fill("FDE6B0"), fill("F6D3D3"), fill("F2F2F2")]
        top_wrap = '<alignment vertical="top" wrapText="1"/>'
        xfs = [(0, 0, ""), (1, 2, top_wrap), (4, 3, top_wrap), (5, 4, top_wrap), (6, 5, top_wrap), (0, 0, top_wrap),
               (0, 6, top_wrap), (2, 0, ""), (1, 0, ""), (3, 0, "")]
        apply_font, apply_fill = ' applyFont="1"', ' applyFill="1"'
        cellxfs = "".join(
            f'<xf numFmtId="0" fontId="{fo}" fillId="{fi}" borderId="0" xfId="0"'
            f'{apply_font if fo else ""}{apply_fill if fi else ""}'
            + (f' applyAlignment="1">{al}</xf>' if al else "/>") for fo, fi, al in xfs)
        return (f'<?xml version="1.0" encoding="UTF-8" standalone="yes"?><styleSheet xmlns="{NS_MAIN}">'
                f'<fonts count="{len(fonts)}">{"".join(fonts)}</fonts><fills count="{len(fills)}">{"".join(fills)}</fills>'
                f'<borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders>'
                f'<cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>'
                f'<cellXfs count="{len(xfs)}">{cellxfs}</cellXfs>'
                f'<cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles></styleSheet>')

    def save(self, path):
        if not self.sheets:
            self.sheet("Sheet1")
        ct = [f'<Override PartName="/xl/workbook.xml" ContentType="{CT}spreadsheetml.sheet.main+xml"/>',
              f'<Override PartName="/xl/styles.xml" ContentType="{CT}spreadsheetml.styles+xml"/>']
        wb_rels = [f'<Relationship Id="rIdS" Type="{REL}styles" Target="styles.xml"/>']
        sheets_xml = []
        chart_no = 1
        with zipfile.ZipFile(path, "w", zipfile.ZIP_DEFLATED) as z:
            for i, s in enumerate(self.sheets, 1):
                links = []
                xml = self._sheet_xml(s, i, links, bool(s.charts))
                z.writestr(f"xl/worksheets/sheet{i}.xml", '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>' + xml)
                ct.append(f'<Override PartName="/xl/worksheets/sheet{i}.xml" ContentType="{CT}spreadsheetml.worksheet+xml"/>')
                wb_rels.append(f'<Relationship Id="rId{i}" Type="{REL}worksheet" Target="worksheets/sheet{i}.xml"/>')
                sheets_xml.append(f'<sheet name="{_x(s.name)}" sheetId="{i}" r:id="rId{i}"/>')
                rels = [f'<Relationship Id="rIdL{j}" Type="{REL}hyperlink" Target="{_x(u)}" TargetMode="External"/>'
                        for j, (_, u) in enumerate(links, 1)]
                if s.charts:
                    rels.append(f'<Relationship Id="rIdD1" Type="{REL}drawing" Target="../drawings/drawing{i}.xml"/>')
                    z.writestr(f"xl/drawings/drawing{i}.xml", self._drawing_xml(s, chart_no))
                    ct.append(f'<Override PartName="/xl/drawings/drawing{i}.xml" ContentType="{CT}drawing+xml"/>')
                    drels = []
                    for k, ch in enumerate(s.charts, 1):
                        z.writestr(f"xl/charts/chart{chart_no}.xml", self._chart_xml(s, ch))
                        ct.append(f'<Override PartName="/xl/charts/chart{chart_no}.xml" ContentType="{CT}drawingml.chart+xml"/>')
                        drels.append(f'<Relationship Id="rIdC{k}" Type="{REL}chart" Target="../charts/chart{chart_no}.xml"/>')
                        chart_no += 1
                    z.writestr(f"xl/drawings/_rels/drawing{i}.xml.rels", _rels(drels))
                if rels:
                    z.writestr(f"xl/worksheets/_rels/sheet{i}.xml.rels", _rels(rels))
            z.writestr("xl/workbook.xml", f'<?xml version="1.0" encoding="UTF-8" standalone="yes"?><workbook xmlns="{NS_MAIN}" '
                       f'xmlns:r="{NS_R}"><bookViews><workbookView/></bookViews><sheets>{"".join(sheets_xml)}</sheets></workbook>')
            z.writestr("xl/_rels/workbook.xml.rels", _rels(wb_rels))
            z.writestr("xl/styles.xml", self._styles())
            z.writestr("_rels/.rels", _rels([f'<Relationship Id="rId1" Type="{REL}officeDocument" Target="xl/workbook.xml"/>']))
            z.writestr("[Content_Types].xml",
                       '<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">'
                       '<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>'
                       '<Default Extension="xml" ContentType="application/xml"/>' + "".join(ct) + "</Types>")
        return path


def _rels(items):
    return f'<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="{PKG_REL}">' + "".join(items) + "</Relationships>"


def main(argv=None):
    ap = argparse.ArgumentParser(description="Stdlib .xlsx writer (library). --selftest writes a demo workbook.")
    ap.add_argument("--selftest", metavar="OUT", required=True)
    a = ap.parse_args(argv)
    wb = Workbook()
    ws = wb.sheet("Summary")
    ws.append(["Rating", "Topics"], style="header")
    for k, n in (("RED", 6), ("YELLOW", 7), ("GREEN", 11)):
        ws.append([{"v": k, "s": k}, n])
    ws.append([{"v": "spaarke.com", "link": "https://spaarke.com"}])
    ws.widths, ws.freeze = [18, 10], (1, 0)
    ws.add_chart("Topics by rating", (2, 1, 4, 1), [("Topics", (2, 2, 4, 2), "D03B3B")], bar_dir="bar")
    wb.save(a.selftest)
    print(a.selftest)
    return 0


if __name__ == "__main__":
    sys.exit(main())
