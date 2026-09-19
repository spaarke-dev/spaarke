using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace Sprk.Bff.Api.Tests.Shared.Office;

/// <summary>
/// Builds a REAL, minimal WordprocessingML package for Office save-path tests, and reads its body text back.
/// </summary>
/// <remarks>
/// <para><b>Why this exists</b> (spaarkeai-word-add-in-r1 task 014). Every Office save fixture used to be a
/// 5–6 byte array beginning <c>PK\x03\x04</c>. Those bytes carry a zip signature but are not a readable
/// archive, so once FR-02 stamping wired into the save path they classify CORRUPT and every such save is
/// refused with <c>OFFICE_021</c>.</para>
///
/// <para><b>The trap this helper exists to avoid.</b> The other way to make those fixtures pass is to drop
/// the <c>PK</c> prefix — then the stamper classifies them "not a zip" and passes them through, and the
/// link/graduate tests stay GREEN while production has no link at all. That is a false green: it would hide
/// exactly the dedup consequence the owner accepted on 2026-09-17 (spec SC-5 / NFR-08 as amended). Fixtures
/// must be real Word packages so the tests see what production sees.</para>
///
/// <para><b>Deterministic by construction</b> — fixed entry timestamps and fixed part content, so a fixture's
/// bytes are stable across runs and across machines. Two calls with the same <c>bodyText</c> are
/// byte-identical; two calls with different text are not, which is what lets a test distinguish drafts.</para>
/// </remarks>
public static class MinimalDocx
{
    private const string RelsNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private const string ContentTypesNs = "http://schemas.openxmlformats.org/package/2006/content-types";
    private const string WordMainNs = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string OfficeDocumentRelType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument";

    private static readonly DateTimeOffset FixedTimestamp = new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// A valid single-paragraph <c>.docx</c> whose body reads <paramref name="bodyText"/>.
    /// </summary>
    /// <param name="bodyText">Distinguishes one fixture from another; also what <see cref="ReadBodyText"/> returns.</param>
    /// <param name="withDocumentRels">
    /// When <c>true</c> (the default, and the shape almost every real document has) the package carries a
    /// <c>word/_rels/document.xml.rels</c> part, so stamping exercises the SURGICAL INSERTION path. When
    /// <c>false</c> the part is absent and stamping must CREATE it — the shape 7 of the 28 corpus documents
    /// had, and a distinct code path worth covering.
    /// </param>
    public static byte[] Create(string bodyText, bool withDocumentRels = true)
    {
        ArgumentNullException.ThrowIfNull(bodyText);

        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(zip, "[Content_Types].xml",
                $"""<?xml version="1.0" encoding="UTF-8" standalone="yes"?>{"\r\n"}<Types xmlns="{ContentTypesNs}"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/></Types>""");

            Add(zip, "_rels/.rels",
                $"""<?xml version="1.0" encoding="UTF-8" standalone="yes"?>{"\r\n"}<Relationships xmlns="{RelsNs}"><Relationship Id="rId1" Type="{OfficeDocumentRelType}" Target="word/document.xml"/></Relationships>""");

            Add(zip, "word/document.xml",
                $"""<?xml version="1.0" encoding="UTF-8" standalone="yes"?>{"\r\n"}<w:document xmlns:w="{WordMainNs}"><w:body><w:p><w:r><w:t>{Escape(bodyText)}</w:t></w:r></w:p></w:body></w:document>""");

            if (withDocumentRels)
            {
                // Deliberately EMPTY (but not self-closing): a relationship to a part that does not exist would
                // make the package invalid, and the only thing the stamper needs is a splice point.
                Add(zip, "word/_rels/document.xml.rels",
                    $"""<?xml version="1.0" encoding="UTF-8" standalone="yes"?>{"\r\n"}<Relationships xmlns="{RelsNs}"></Relationships>""");
            }
        }

        return buffer.ToArray();
    }

    /// <summary>
    /// The concatenated <c>w:t</c> text of the package's main document part, or <c>null</c> when the bytes are
    /// not a readable Word package.
    /// </summary>
    /// <remarks>
    /// Assertions compare BODY TEXT rather than raw bytes so they stay true regardless of the stamp's byte
    /// layout: "the stored version is this draft" is the claim the test actually wants to make, and it must
    /// not become a restatement of the stamper's own output format.
    /// </remarks>
    public static string? ReadBodyText(byte[]? bytes)
    {
        if (bytes is null || bytes.Length < 4 || bytes[0] != 0x50 || bytes[1] != 0x4B)
        {
            return null;
        }

        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
            var entry = zip.GetEntry("word/document.xml");
            if (entry is null)
            {
                return null;
            }

            using var part = entry.Open();
            var document = XDocument.Load(part);
            return string.Concat(document.Descendants(XName.Get("t", WordMainNs)).Select(node => node.Value));
        }
        catch (Exception ex) when (ex is InvalidDataException or System.Xml.XmlException)
        {
            return null;
        }
    }

    private static void Add(ZipArchive zip, string path, string content)
    {
        var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
        entry.LastWriteTime = FixedTimestamp;
        using var stream = entry.Open();
        var payload = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(content);
        stream.Write(payload, 0, payload.Length);
    }

    private static string Escape(string text) =>
        text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
