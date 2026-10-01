using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace Sprk.Bff.Api.Services.Office;

/// <summary>
/// FR-02 (spaarkeai-word-add-in-r1 task 014): writes the <c>sprk_document</c> GUID into a custom XML
/// PART inside a WordprocessingML package, so a document downloaded from Spaarke and later re-uploaded
/// self-identifies without a Graph round-trip.
/// </summary>
/// <remarks>
/// <para><b>Static, stateless, bytes-in/bytes-out.</b> No DI registration (ADR-010 — a method on the save
/// spine would have needed the same code and nothing here needs a lifetime), no Graph type (ADR-007), and
/// no reference to <c>Services/Compose/**</c> or <c>Spaarke.Compose.Components</c> (ADR-049: the two
/// <c>.docx</c> write spines stay independent — this one inserts package parts and never touches the body).</para>
///
/// <para><b>Why hand-rolled surgery and not the Open XML SDK.</b> The SDK re-serializes
/// <c>[Content_Types].xml</c> and any relationship part it touches, normalising namespaces, attribute order
/// and whitespace. That is precisely the "library that normalizes XML on save" the task's escalation trigger
/// names. Instead: entries that are not opened are never re-encoded by <see cref="ZipArchive"/> in
/// <see cref="ZipArchiveMode.Update"/>, and the two parts that MUST change are edited by a single
/// <b>byte splice</b> immediately before their root end tag — so the original bytes of both survive as a
/// common prefix plus a common suffix. Measured on 28 real corpus documents: every pre-existing part
/// byte-identical except those two, which change by insertion only.</para>
///
/// <para><b>Byte-determinism is a contract, not an accident.</b> <c>Stamp(bytes, id)</c> returns the same
/// bytes every time for the same input: the datastore item id is derived from <paramref name="documentId"/>
/// rather than freshly generated, and every NEW zip entry carries a fixed timestamp. The save spine's
/// task-047 duplicate check depends on this — it stamps the incoming request and compares it against the
/// stored bytes, which is only meaningful if stamping is reproducible. <c>OfficeDocumentStampTests</c> pins it.</para>
///
/// <para><b>A stamp is a hint, never an authorization.</b> Anyone can author a custom XML part carrying any
/// GUID. Every consumer must confirm the id through an existing authorized read — a version save already
/// requires Dataverse <c>write</c> on the target via <c>OfficeVersionSaveAuthorizationFilter</c> — so a forged
/// stamp gains nothing the user could not get by typing an id.</para>
///
/// <para><b>Contract for the CLIENT-side reader (task 051).</b> The part is related FROM THE MAIN DOCUMENT
/// PART and carries an <c>itemProps</c> sibling, which is the data-store shape the Office.js Common API
/// enumerates. The root element carries an explicit default <c>xmlns</c>, without which
/// <c>CustomXMLPart.namespaceUri</c> is not populated and <c>getByNamespaceAsync</c> silently finds nothing
/// (task 019 condition 2). Read it with
/// <c>Office.context.document.customXmlParts.getByNamespaceAsync("urn:spaarke:office:document-identity:1")</c>,
/// then <c>getXmlAsync</c>, and take the text of the single <c>documentId</c> child.</para>
/// </remarks>
public static class OfficeDocumentStamp
{
    /// <summary>
    /// The stamp's XML namespace — also its schema version. The client reads BY this namespace
    /// (task 019 condition 1/2), so it is a wire contract: changing it is a breaking change.
    /// </summary>
    public const string StampNamespace = "urn:spaarke:office:document-identity:1";

    /// <summary>The stamp root element's local name.</summary>
    public const string StampRootElement = "documentIdentity";

    /// <summary>The element carrying the canonical bare-lowercase GUID (ADR-044).</summary>
    public const string StampIdElement = "documentId";

    private const string ContentTypesPart = "[Content_Types].xml";
    private const string PackageRelsPart = "_rels/.rels";
    private const string OfficeDocumentRelType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument";
    private const string CustomXmlRelType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/customXml";
    private const string CustomXmlPropsRelType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/customXmlProps";
    private const string CustomXmlPropsContentType =
        "application/vnd.openxmlformats-officedocument.customXmlProperties+xml";

    private static readonly XNamespace RelsNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace ContentTypesNs = "http://schemas.openxmlformats.org/package/2006/content-types";
    private static readonly XNamespace DataStoreNs = "http://schemas.openxmlformats.org/officeDocument/2006/customXml";

    /// <summary>The WordprocessingML main-part content types: .docx, .docm, .dotx, .dotm.</summary>
    private static readonly string[] WordMainPartContentTypes =
    {
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml",
        "application/vnd.ms-word.document.macroEnabled.main+xml",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.template.main+xml",
        "application/vnd.ms-word.template.macroEnabledTemplate.main+xml",
    };

    /// <summary>
    /// Fixed timestamp for every entry this class CREATES, so output is byte-deterministic. 1980-01-01 is
    /// the zip format's own minimum; <see cref="ZipArchiveEntry.LastWriteTime"/> rejects anything earlier.
    /// Entries this class merely edits keep the timestamp they already had.
    /// </summary>
    private static readonly DateTimeOffset NewEntryTimestamp = new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Throws rather than substituting U+FFFD, so a non-UTF-8 registration part is detected, not corrupted.</summary>
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>
    /// Largest DECOMPRESSED size this class will read for a single package part.
    /// </summary>
    /// <remarks>
    /// <b>Zip-bomb guard.</b> These bytes are an untrusted user upload, and this class is the first thing in
    /// the save path that DECOMPRESSES them — before FR-02 the bytes were streamed to storage without ever
    /// being expanded. A zip can expand at roughly 1000:1, so an upload well inside the request limit can
    /// declare a part of many gigabytes. Every part this class reads is small by nature (`[Content_Types].xml`,
    /// a relationship part, a custom XML item), so 8 MB is far above any legitimate value while bounding the
    /// allocation. Exceeding it is NOT an error: the package is passed through unstamped, per the rule that a
    /// missing stamp is always recoverable but a failed save is not.
    /// </remarks>
    private const long MaxPartBytes = 8L * 1024 * 1024;

    /// <summary>
    /// Upper bound on the `customXml/item{N}` probe, so a hostile package carrying a vast run of custom XML
    /// parts cannot make the search unbounded. Real documents use single digits.
    /// </summary>
    private const int MaxCustomXmlProbe = 5000;

    /// <summary>
    /// Reads one package part into memory, refusing anything larger than <see cref="MaxPartBytes"/>.
    /// </summary>
    /// <remarks>
    /// Checks the central directory's declared length first (cheap), then enforces the same bound WHILE
    /// reading — a zip header can lie about the uncompressed size, so the declared value alone is not a guard.
    /// </remarks>
    private static bool TryReadPart(ZipArchiveEntry entry, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();

        if (entry.Length > MaxPartBytes)
        {
            return false;
        }

        using var stream = entry.Open();
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        long total = 0;
        int read;
        while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
        {
            total += read;
            if (total > MaxPartBytes)
            {
                return false;
            }

            buffer.Write(chunk, 0, read);
        }

        bytes = buffer.ToArray();
        return true;
    }

    /// <summary>What kind of payload a set of bytes is, decided by CONTENT and never by file name.</summary>
    public enum PackageKind
    {
        /// <summary>No zip local-file signature — a PDF, an EML, an arbitrary binary. Passes through untouched.</summary>
        NotAZip,

        /// <summary>Has a zip signature but cannot be opened as an archive: truncated or damaged.</summary>
        Corrupt,

        /// <summary>A readable zip that is not WordprocessingML — an .xlsx sent as a Document, a plain .zip. Passes through untouched.</summary>
        NotWordProcessing,

        /// <summary>A readable WordprocessingML package: the only shape that is stamped.</summary>
        WordProcessing,
    }

    /// <summary>The outcome of a <see cref="Stamp"/> call.</summary>
    public enum StampOutcome
    {
        /// <summary>The stamp was added, or an existing Spaarke stamp was rewritten to this id.</summary>
        Stamped,

        /// <summary>Already carried exactly this id — the input bytes are returned unchanged.</summary>
        AlreadyStamped,

        /// <summary>Not an OOXML Word package. Returned byte-for-byte unmodified; never an error.</summary>
        PassedThrough,

        /// <summary>
        /// A Word package in a shape the surgical writer will not take (a prefixed or self-closing root on a
        /// registration part, a non-UTF-8 part). Returned unmodified and unstamped, with a reason — a missing
        /// stamp is a normal, recoverable state (task 019 condition 3), so the save MUST NOT fail.
        /// </summary>
        Unsupported,

        /// <summary>Zip signature but unreadable. The caller MUST refuse the save before any storage write.</summary>
        Corrupt,
    }

    /// <param name="Outcome">What happened.</param>
    /// <param name="Bytes">The bytes to store. Reference-identical to the input for every non-stamping outcome.</param>
    /// <param name="Reason">Why, for <see cref="StampOutcome.Unsupported"/> and <see cref="StampOutcome.Corrupt"/>; else null.</param>
    public sealed record StampResult(StampOutcome Outcome, byte[] Bytes, string? Reason);

    /// <summary>Classifies <paramref name="bytes"/> by content. Never throws.</summary>
    public static PackageKind Classify(byte[]? bytes)
    {
        if (!HasZipSignature(bytes))
        {
            return PackageKind.NotAZip;
        }

        try
        {
            using var stream = new MemoryStream(bytes!, writable: false);
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read);

            // Touching the entry list is what actually forces the central directory to be parsed, so a
            // truncated archive surfaces HERE rather than on first use deeper in the save.
            _ = zip.Entries.Count;

            return TryResolveMainPart(zip, out _) ? PackageKind.WordProcessing : PackageKind.NotWordProcessing;
        }
        catch (InvalidDataException)
        {
            return PackageKind.Corrupt;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or EndOfStreamException)
        {
            return PackageKind.Corrupt;
        }
    }

    /// <summary>
    /// Returns bytes carrying <paramref name="documentId"/> as this package's single Spaarke stamp.
    /// Deterministic: the same input and id always produce the same output.
    /// </summary>
    public static StampResult Stamp(byte[] bytes, Guid documentId)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        if (documentId == Guid.Empty)
        {
            throw new ArgumentException("A stamp must carry a real sprk_document id.", nameof(documentId));
        }

        var kind = Classify(bytes);
        switch (kind)
        {
            case PackageKind.NotAZip:
            case PackageKind.NotWordProcessing:
                return new StampResult(StampOutcome.PassedThrough, bytes, null);
            case PackageKind.Corrupt:
                return new StampResult(StampOutcome.Corrupt, bytes, "The document file could not be read as an Office package.");
        }

        // Read-only survey first: a re-stamp with the same id must be a TRUE no-op (same array back), and the
        // shapes the surgical writer refuses must be detected before anything is written.
        SurveyResult survey;
        try
        {
            using var probe = new MemoryStream(bytes, writable: false);
            using var zip = new ZipArchive(probe, ZipArchiveMode.Read);
            survey = Survey(zip, documentId);
        }
        catch (InvalidDataException)
        {
            return new StampResult(StampOutcome.Corrupt, bytes, "The document file could not be read as an Office package.");
        }

        if (survey.Refusal is not null)
        {
            return new StampResult(StampOutcome.Unsupported, bytes, survey.Refusal);
        }

        if (survey.AlreadyCarriesThisId)
        {
            return new StampResult(StampOutcome.AlreadyStamped, bytes, null);
        }

        var output = new MemoryStream(bytes.Length + 4096);
        output.Write(bytes, 0, bytes.Length);
        output.Position = 0;

        using (var zip = new ZipArchive(output, ZipArchiveMode.Update, leaveOpen: true))
        {
            if (survey.ExistingStampParts.Count > 0)
            {
                // A copy of another Spaarke document, or the "save as new document" override of an identified
                // one. Rewrite in place: no new part, no new registration, so exactly one Spaarke part remains.
                foreach (var partPath in survey.ExistingStampParts)
                {
                    var entry = FindEntry(zip, partPath);
                    if (entry is not null)
                    {
                        WriteEntry(entry, StampXml(documentId));
                    }
                }
            }
            else
            {
                AddStampPart(zip, survey, documentId);
            }
        }

        return new StampResult(StampOutcome.Stamped, output.ToArray(), null);
    }

    /// <summary>
    /// The <c>sprk_document</c> id this package self-identifies as, or <c>null</c> when it carries no Spaarke
    /// stamp, cannot be read, or carries stamps that DISAGREE. Never throws.
    /// </summary>
    /// <remarks>
    /// Disagreement answers <c>null</c> deliberately: two different ids mean the package cannot say which
    /// record it is, and a wrong answer on a save path is worse than no answer (the caller then falls back to
    /// task 012's Graph path). Absence is likewise normal — the Document Inspector's "Custom XML Data /
    /// Remove All" is a real, user-reachable un-stamping path (task 019 condition 3).
    /// </remarks>
    public static Guid? TryReadStamp(byte[]? bytes)
    {
        if (!HasZipSignature(bytes))
        {
            return null;
        }

        try
        {
            using var stream = new MemoryStream(bytes!, writable: false);
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read);

            if (!TryResolveMainPart(zip, out var mainPart))
            {
                return null;
            }

            Guid? found = null;
            foreach (var partPath in CustomXmlPartPaths(zip, mainPart!))
            {
                if (!TryReadStampId(zip, partPath, out var id))
                {
                    continue;
                }

                if (found is not null && found.Value != id)
                {
                    return null; // stamps disagree — no answer is better than the wrong one
                }

                found = id;
            }

            return found;
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException or NotSupportedException or EndOfStreamException)
        {
            return null;
        }
    }

    // ── survey ────────────────────────────────────────────────────────────────────────────────────

    private sealed class SurveyResult
    {
        public string MainPart { get; init; } = string.Empty;
        public string? Refusal { get; set; }
        public bool AlreadyCarriesThisId { get; set; }
        public List<string> ExistingStampParts { get; } = new();
        public int ItemNumber { get; set; }
        public bool NeedsItemContentTypeOverride { get; set; }
        public string RelationshipId { get; set; } = "rIdSpaarkeIdentity";
        public string MainPartRelsPath { get; set; } = string.Empty;
        public bool MainPartRelsExists { get; set; }
    }

    private static SurveyResult Survey(ZipArchive zip, Guid documentId)
    {
        TryResolveMainPart(zip, out var mainPart);
        var survey = new SurveyResult { MainPart = mainPart! };

        // Existing Spaarke stamps.
        var distinct = new HashSet<Guid>();
        foreach (var partPath in CustomXmlPartPaths(zip, mainPart!))
        {
            if (TryReadStampId(zip, partPath, out var id))
            {
                survey.ExistingStampParts.Add(partPath);
                distinct.Add(id);
            }
        }

        survey.AlreadyCarriesThisId = survey.ExistingStampParts.Count == 1
            && distinct.Count == 1
            && distinct.Contains(documentId);

        if (survey.ExistingStampParts.Count > 0)
        {
            return survey; // rewrite path — no new part, so no registration edits to validate
        }

        survey.ItemNumber = LowestFreeItemNumber(zip);
        if (survey.ItemNumber < 0)
        {
            survey.Refusal = "The package already carries an implausible number of custom XML parts.";
            return survey;
        }

        survey.MainPartRelsPath = RelsPathFor(mainPart!);
        survey.MainPartRelsExists = FindEntry(zip, survey.MainPartRelsPath) is not null;

        // [Content_Types].xml must accept a surgical Override insertion.
        var contentTypes = FindEntry(zip, ContentTypesPart);
        if (contentTypes is null)
        {
            survey.Refusal = "The package has no [Content_Types].xml.";
            return survey;
        }

        if (!TryReadUtf8(contentTypes, out var contentTypesBytes, out var contentTypesText))
        {
            survey.Refusal = "[Content_Types].xml is not UTF-8.";
            return survey;
        }

        if (LastIndexOf(contentTypesBytes!, "</Types>") < 0)
        {
            survey.Refusal = "[Content_Types].xml has a prefixed or self-closing root element.";
            return survey;
        }

        survey.NeedsItemContentTypeOverride = !HasXmlDefaultContentType(contentTypesText!);

        // The main part's rels: either editable, or absent (then it is created whole).
        if (survey.MainPartRelsExists)
        {
            var rels = FindEntry(zip, survey.MainPartRelsPath)!;
            if (!TryReadUtf8(rels, out var relsBytes, out var relsText))
            {
                survey.Refusal = $"{survey.MainPartRelsPath} is not UTF-8.";
                return survey;
            }

            if (LastIndexOf(relsBytes!, "</Relationships>") < 0)
            {
                survey.Refusal = $"{survey.MainPartRelsPath} has a prefixed or self-closing root element.";
                return survey;
            }

            survey.RelationshipId = FreeRelationshipId(relsText!);
        }

        return survey;
    }

    // ── writing ───────────────────────────────────────────────────────────────────────────────────

    private static void AddStampPart(ZipArchive zip, SurveyResult survey, Guid documentId)
    {
        var n = survey.ItemNumber;
        var itemPath = $"customXml/item{n}.xml";
        var propsPath = $"customXml/itemProps{n}.xml";

        CreateEntry(zip, itemPath, StampXml(documentId));
        CreateEntry(zip, propsPath, ItemPropsXml(documentId));
        CreateEntry(zip, $"customXml/_rels/item{n}.xml.rels", ItemRelsXml(n));

        // The main document part's relationship to the stamp, with a RELATIVE target — the shape of the
        // in-repo custom XML parts that survived four modern-Word save cycles (task 019 §1.5). A part related
        // only from the package root may not reach the Office.js data store the client reads.
        var relationship =
            $"<Relationship Id=\"{survey.RelationshipId}\" Type=\"{CustomXmlRelType}\" Target=\"../{itemPath}\"/>";

        if (survey.MainPartRelsExists)
        {
            SpliceBeforeRootEnd(zip, survey.MainPartRelsPath, "</Relationships>", relationship);
        }
        else
        {
            CreateEntry(zip, survey.MainPartRelsPath,
                $"""<?xml version="1.0" encoding="UTF-8" standalone="yes"?>{"\r\n"}<Relationships xmlns="{RelsNs}">{relationship}</Relationships>""");
        }

        var overrides = $"<Override PartName=\"/{propsPath}\" ContentType=\"{CustomXmlPropsContentType}\"/>";
        if (survey.NeedsItemContentTypeOverride)
        {
            // Only when the package has no `xml -> application/xml` Default to inherit from.
            overrides = $"<Override PartName=\"/{itemPath}\" ContentType=\"application/xml\"/>" + overrides;
        }

        SpliceBeforeRootEnd(zip, ContentTypesPart, "</Types>", overrides);
    }

    /// <summary>
    /// Inserts <paramref name="insertion"/> immediately before the LAST occurrence of
    /// <paramref name="rootEndTag"/>, operating on BYTES. Every original byte of the part survives, as a
    /// common prefix plus a common suffix — the property the byte-fidelity constraint is about.
    /// </summary>
    private static void SpliceBeforeRootEnd(ZipArchive zip, string partPath, string rootEndTag, string insertion)
    {
        var entry = FindEntry(zip, partPath)
            ?? throw new InvalidOperationException($"Expected package part '{partPath}' to exist.");

        // Unreachable in practice: Survey read this same part under the same bound before deciding to write.
        if (!TryReadPart(entry, out var original))
        {
            throw new InvalidOperationException($"Package part '{partPath}' exceeds the readable size bound.");
        }

        var at = LastIndexOf(original, rootEndTag);
        if (at < 0)
        {
            // Survey already proved the tag is present; reaching here would mean the package changed underneath us.
            throw new InvalidOperationException($"Package part '{partPath}' has no '{rootEndTag}' root end tag.");
        }

        var addition = StrictUtf8.GetBytes(insertion);
        var spliced = new byte[original.Length + addition.Length];
        Buffer.BlockCopy(original, 0, spliced, 0, at);
        Buffer.BlockCopy(addition, 0, spliced, at, addition.Length);
        Buffer.BlockCopy(original, at, spliced, at + addition.Length, original.Length - at);

        WriteEntry(entry, spliced);
    }

    private static void CreateEntry(ZipArchive zip, string path, string content)
    {
        var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
        entry.LastWriteTime = NewEntryTimestamp; // determinism
        using var stream = entry.Open();
        var payload = StrictUtf8.GetBytes(content);
        stream.Write(payload, 0, payload.Length);
    }

    private static void WriteEntry(ZipArchiveEntry entry, string content) => WriteEntry(entry, StrictUtf8.GetBytes(content));

    private static void WriteEntry(ZipArchiveEntry entry, byte[] content)
    {
        using var stream = entry.Open();
        stream.SetLength(0);
        stream.Write(content, 0, content.Length);
    }

    // ── part content ──────────────────────────────────────────────────────────────────────────────

    /// <remarks>
    /// The explicit default <c>xmlns</c> on the ROOT is load-bearing, not decoration: Microsoft documents
    /// that <c>CustomXMLPart.namespaceUri</c> is populated only when the top-level element carries it, and the
    /// client (task 051) looks the part up BY namespace. Omitting it is a silent total failure.
    /// ADR-044: <c>Guid.ToString("D")</c> is bare and lowercase by definition, so the stamped value is already
    /// canonical and the reader needs no normalisation.
    /// </remarks>
    private static string StampXml(Guid documentId) =>
        $"""<?xml version="1.0" encoding="UTF-8" standalone="yes"?>{"\r\n"}<{StampRootElement} xmlns="{StampNamespace}"><{StampIdElement}>{documentId:D}</{StampIdElement}></{StampRootElement}>""";

    /// <remarks>
    /// Matches Word's own data-store property parts. The <c>ds:itemID</c> is DERIVED from the document id
    /// rather than freshly generated, which is what makes <see cref="Stamp"/> byte-deterministic; Word's own
    /// format for this attribute is a braced, upper-case GUID.
    /// </remarks>
    private static string ItemPropsXml(Guid documentId)
    {
        // Built as a local first: a braced GUID inside an interpolated raw string would collide with the
        // interpolation delimiters themselves.
        var itemId = "{" + documentId.ToString("D").ToUpperInvariant() + "}";

        return $"""<?xml version="1.0" encoding="UTF-8" standalone="yes"?>{"\r\n"}<ds:datastoreItem ds:itemID="{itemId}" xmlns:ds="{DataStoreNs}"><ds:schemaRefs><ds:schemaRef ds:uri="{StampNamespace}"/></ds:schemaRefs></ds:datastoreItem>""";
    }

    private static string ItemRelsXml(int n) =>
        $"""<?xml version="1.0" encoding="UTF-8" standalone="yes"?>{"\r\n"}<Relationships xmlns="{RelsNs}"><Relationship Id="rId1" Type="{CustomXmlPropsRelType}" Target="itemProps{n}.xml"/></Relationships>""";

    // ── package navigation ────────────────────────────────────────────────────────────────────────

    private static bool HasZipSignature(byte[]? bytes) =>
        bytes is { Length: >= 4 } && bytes[0] == 0x50 && bytes[1] == 0x4B;

    /// <summary>
    /// Resolves the main document part by CONTENT — <c>_rels/.rels</c> officeDocument relationship, then the
    /// part's declared content type in <c>[Content_Types].xml</c> — never by file extension. A package whose
    /// main part is not WordprocessingML (or that declares no Override for it) answers <c>false</c>, and the
    /// caller passes those bytes through untouched rather than guessing.
    /// </summary>
    private static bool TryResolveMainPart(ZipArchive zip, out string? mainPart)
    {
        mainPart = null;

        var rels = ParseXml(zip, PackageRelsPart);
        var contentTypes = ParseXml(zip, ContentTypesPart);
        if (rels is null || contentTypes is null)
        {
            return false;
        }

        foreach (var relationship in rels.Root?.Elements(RelsNs + "Relationship") ?? Enumerable.Empty<XElement>())
        {
            if (!string.Equals((string?)relationship.Attribute("Type"), OfficeDocumentRelType, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var target = (string?)relationship.Attribute("Target");
            if (string.IsNullOrWhiteSpace(target))
            {
                continue;
            }

            var candidate = ResolvePartPath(string.Empty, target!);
            if (FindEntry(zip, candidate) is null)
            {
                continue;
            }

            var declared = DeclaredContentType(contentTypes, candidate);
            if (declared is not null
                && WordMainPartContentTypes.Contains(declared, StringComparer.OrdinalIgnoreCase))
            {
                mainPart = candidate;
                return true;
            }
        }

        return false;
    }

    private static string? DeclaredContentType(XDocument contentTypes, string partPath)
    {
        foreach (var element in contentTypes.Root?.Elements(ContentTypesNs + "Override") ?? Enumerable.Empty<XElement>())
        {
            var name = ((string?)element.Attribute("PartName"))?.TrimStart('/');
            if (string.Equals(name, partPath, StringComparison.OrdinalIgnoreCase))
            {
                return (string?)element.Attribute("ContentType");
            }
        }

        return null;
    }

    private static bool HasXmlDefaultContentType(string contentTypesText)
    {
        try
        {
            var document = XDocument.Parse(contentTypesText);
            return document.Root?.Elements(ContentTypesNs + "Default").Any(element =>
                string.Equals((string?)element.Attribute("Extension"), "xml", StringComparison.OrdinalIgnoreCase)) == true;
        }
        catch (System.Xml.XmlException)
        {
            return false;
        }
    }

    /// <summary>The paths of every custom XML part related FROM the main document part.</summary>
    private static IEnumerable<string> CustomXmlPartPaths(ZipArchive zip, string mainPart)
    {
        var rels = ParseXml(zip, RelsPathFor(mainPart));
        if (rels is null)
        {
            yield break;
        }

        foreach (var relationship in rels.Root?.Elements(RelsNs + "Relationship") ?? Enumerable.Empty<XElement>())
        {
            if (!string.Equals((string?)relationship.Attribute("Type"), CustomXmlRelType, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // An external target names something outside the package; there is nothing here to read.
            if (string.Equals((string?)relationship.Attribute("TargetMode"), "External", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var target = (string?)relationship.Attribute("Target");
            if (!string.IsNullOrWhiteSpace(target))
            {
                yield return ResolvePartPath(mainPart, target!);
            }
        }
    }

    /// <summary>A Spaarke stamp is identified by an EXACT namespace match on the root element — any other
    /// custom XML part (a bibliography, Google Docs metadata, SharePoint columns) is ignored.</summary>
    private static bool TryReadStampId(ZipArchive zip, string partPath, out Guid id)
    {
        id = Guid.Empty;

        var document = ParseXml(zip, partPath);
        var root = document?.Root;
        if (root is null
            || root.Name.NamespaceName != StampNamespace
            || root.Name.LocalName != StampRootElement)
        {
            return false;
        }

        var value = root.Element(XNamespace.Get(StampNamespace) + StampIdElement)?.Value;
        return Guid.TryParse(value, out id) && id != Guid.Empty;
    }

    /// <summary>
    /// The lowest <c>N</c> for which neither <c>customXml/itemN.xml</c> nor <c>itemPropsN.xml</c> exists, so the
    /// stamp never displaces an existing data-store part (e.g. a bibliography at item1). Returns <c>-1</c> when
    /// no free slot is found within <see cref="MaxCustomXmlProbe"/>.
    /// </summary>
    /// <remarks>
    /// The entry names are hashed ONCE. Probing with <c>FindEntry</c> per candidate would fall through to a
    /// linear scan on every miss, making the search quadratic in entry count — which a hostile package carrying
    /// a long run of <c>customXml/item*</c> parts could exploit on a save path.
    /// </remarks>
    private static int LowestFreeItemNumber(ZipArchive zip)
    {
        var names = new HashSet<string>(zip.Entries.Select(entry => entry.FullName), StringComparer.OrdinalIgnoreCase);

        for (var n = 1; n <= MaxCustomXmlProbe; n++)
        {
            if (!names.Contains($"customXml/item{n}.xml") && !names.Contains($"customXml/itemProps{n}.xml"))
            {
                return n;
            }
        }

        return -1;
    }

    private static string FreeRelationshipId(string relsText)
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var document = XDocument.Parse(relsText);
            foreach (var element in document.Root?.Elements(RelsNs + "Relationship") ?? Enumerable.Empty<XElement>())
            {
                var id = (string?)element.Attribute("Id");
                if (!string.IsNullOrEmpty(id))
                {
                    taken.Add(id!);
                }
            }
        }
        catch (System.Xml.XmlException)
        {
            // Survey validated well-formedness separately; an unparseable part simply yields no taken ids.
        }

        var candidate = "rIdSpaarkeIdentity";
        for (var suffix = 2; taken.Contains(candidate); suffix++)
        {
            candidate = $"rIdSpaarkeIdentity{suffix}";
        }

        return candidate;
    }

    private static string RelsPathFor(string partPath)
    {
        var slash = partPath.LastIndexOf('/');
        var directory = slash < 0 ? string.Empty : partPath[..slash];
        var name = slash < 0 ? partPath : partPath[(slash + 1)..];
        return directory.Length == 0 ? $"_rels/{name}.rels" : $"{directory}/_rels/{name}.rels";
    }

    private static string ResolvePartPath(string basePartPath, string target)
    {
        if (target.StartsWith('/'))
        {
            return target.TrimStart('/');
        }

        var slash = basePartPath.LastIndexOf('/');
        var segments = slash < 0
            ? new List<string>()
            : new List<string>(basePartPath[..slash].Split('/', StringSplitOptions.RemoveEmptyEntries));

        foreach (var segment in target.Replace('\\', '/').Split('/'))
        {
            if (segment.Length == 0 || segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                if (segments.Count > 0)
                {
                    segments.RemoveAt(segments.Count - 1);
                }

                continue;
            }

            segments.Add(segment);
        }

        return string.Join('/', segments);
    }

    /// <summary>OPC part names are compared case-insensitively; zip entry lookup is not.</summary>
    private static ZipArchiveEntry? FindEntry(ZipArchive zip, string path)
    {
        var direct = zip.GetEntry(path);
        if (direct is not null)
        {
            return direct;
        }

        foreach (var entry in zip.Entries)
        {
            if (string.Equals(entry.FullName, path, StringComparison.OrdinalIgnoreCase))
            {
                return entry;
            }
        }

        return null;
    }

    private static XDocument? ParseXml(ZipArchive zip, string path)
    {
        var entry = FindEntry(zip, path);
        if (entry is null)
        {
            return null;
        }

        try
        {
            if (!TryReadPart(entry, out var raw))
            {
                return null;
            }

            using var stream = new MemoryStream(raw, writable: false);
            // No DTD processing and no external resolution: package parts are untrusted input, so this closes
            // XXE and entity-expansion ("billion laughs") alongside the size bound above.
            var settings = new System.Xml.XmlReaderSettings
            {
                DtdProcessing = System.Xml.DtdProcessing.Prohibit,
                XmlResolver = null,
            };
            using var reader = System.Xml.XmlReader.Create(stream, settings);
            return XDocument.Load(reader);
        }
        catch (Exception ex) when (ex is System.Xml.XmlException or InvalidDataException or IOException)
        {
            return null;
        }
    }

    private static bool TryReadUtf8(ZipArchiveEntry entry, out byte[]? bytes, out string? text)
    {
        bytes = null;
        text = null;

        try
        {
            if (!TryReadPart(entry, out var raw))
            {
                return false;
            }

            bytes = raw;
            text = StrictUtf8.GetString(StripBom(raw));
            return true;
        }
        catch (Exception ex) when (ex is DecoderFallbackException or ArgumentException or InvalidDataException or IOException)
        {
            return false;
        }
    }

    private static byte[] StripBom(byte[] bytes) =>
        bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF
            ? bytes[3..]
            : bytes;

    /// <summary>Last index of an ASCII needle in <paramref name="haystack"/>, or -1.</summary>
    private static int LastIndexOf(byte[] haystack, string needle)
    {
        var pattern = Encoding.ASCII.GetBytes(needle);
        for (var start = haystack.Length - pattern.Length; start >= 0; start--)
        {
            var matched = true;
            for (var offset = 0; offset < pattern.Length; offset++)
            {
                if (haystack[start + offset] != pattern[offset])
                {
                    matched = false;
                    break;
                }
            }

            if (matched)
            {
                return start;
            }
        }

        return -1;
    }
}
