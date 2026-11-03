"""safe_docx.py - read an untrusted .docx safely (shared by the engines; stdlib + lxml).

- Size caps checked from the zip directory BEFORE anything is decompressed (zip bombs).
- Hardened XML parser: no entity resolution, no DTD loading, no network (XXE, billion laughs),
  whatever the installed lxml's defaults are.
- Macro-enabled documents are refused: a .docm renamed .docx would carry vbaProject.bin into
  the outputs sent to a counterparty.
Nothing is extracted to disk.
"""
import zipfile

from lxml import etree

MAX_PARTS = 2000
MAX_PART_BYTES = 64 * 1024 * 1024
MAX_TOTAL_BYTES = 256 * 1024 * 1024
MAX_RATIO = 200  # uncompressed / compressed, per part above 1 MB

PARSER = etree.XMLParser(resolve_entities=False, no_network=True, load_dtd=False, dtd_validation=False,
                         huge_tree=False, remove_blank_text=False)


class UnsafeDocument(ValueError):
    pass


def read_parts(path):
    """Return {part name: bytes} for a .docx after size and type checks."""
    with zipfile.ZipFile(path) as z:
        infos = z.infolist()
        if len(infos) > MAX_PARTS:
            raise UnsafeDocument(f"{len(infos)} parts (limit {MAX_PARTS})")
        total = 0
        for i in infos:
            total += i.file_size
            if i.file_size > MAX_PART_BYTES:
                raise UnsafeDocument(f"part {i.filename} is {i.file_size} bytes uncompressed")
            if i.file_size > 1024 * 1024 and i.compress_size and i.file_size / i.compress_size > MAX_RATIO:
                raise UnsafeDocument(f"part {i.filename} has a suspicious compression ratio")
        if total > MAX_TOTAL_BYTES:
            raise UnsafeDocument(f"document is {total} bytes uncompressed (limit {MAX_TOTAL_BYTES})")
        names = {i.filename for i in infos}
        if "word/document.xml" not in names:
            raise ValueError("not a Word document: word/document.xml missing")
        if any(n.lower().endswith("vbaproject.bin") for n in names):
            raise UnsafeDocument("macro-enabled document (vbaProject.bin); save it as .docx without macros first")
        parts = {n: z.read(n) for n in names}
    ct = parts.get("[Content_Types].xml", b"")
    if b"macroEnabled" in ct:
        raise UnsafeDocument("macro-enabled content type; save it as .docx without macros first")
    return parts


def parse(data):
    return etree.fromstring(data, PARSER)
