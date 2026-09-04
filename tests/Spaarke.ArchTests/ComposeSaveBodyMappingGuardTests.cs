using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// Structural fitness function (the eighth KEEP path — ADR-038 Amendment A1): every property on the
/// HTTP body DTO <c>SaveComposeDocumentBody</c> must actually be READ when the endpoint maps it onto
/// <c>SaveComposeDocumentRequest</c>, or be listed here as a deliberate omission with a reason.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this guard exists — a defect class, observed three times.</b> The Compose save endpoint maps
/// its body onto the service request FIELD BY FIELD. System.Text.Json silently ignores unknown JSON
/// properties, and an unmapped DTO property is simply never copied. Both failure modes are TOTALLY
/// SILENT: no exception, no log, no failing test. The feature is wired at every other layer and does
/// nothing, and the seam tests below the endpoint stay green because they construct the service request
/// directly and never traverse the mapping.
/// </para>
/// <para>Confirmed instances, in the order they were found:</para>
/// <list type="number">
///   <item><b>paraIdMap / importedRevisions / importedComments</b> (compose-r2 task 052 fast-follow) —
///   the server projected all three onto the Load response and <c>ComposeWorkspace</c> threaded none of
///   them to <c>ComposeEditor</c>. Two shipped features were dead props. Fixed then; the regression
///   guard is <c>ComposeWorkspace.imports.test.tsx</c>, which is client-side and cannot see this side.</item>
///   <item><b>summaryPage</b> (nda-r1 task 041) — <c>ComposeSummaryPageGenerator</c>, its corpus seam
///   test and the <c>SaveAsync</c> call site all exist and are green. <c>SaveComposeDocumentBody</c> has
///   no such property and no client sends one, so the NDA Summary Page appendix has never been produced
///   in the running app. STILL OPEN — see the allowlist entry below.</item>
///   <item><b>revisionReport</b> (r8 UAT item 8) — caught by this investigation BEFORE it shipped: the
///   client sent the field, the DTO did not declare it, so it was dropped at the transport boundary
///   while every unit and seam test stayed green. Fixed in the same change that added this guard.</item>
/// </list>
/// <para>
/// Three occurrences is a missing forcing function, not three coincidences. This test is that function.
/// </para>
/// <para>
/// <b>Maintenance.</b> If this fails, you added a property to <c>SaveComposeDocumentBody</c> and did not
/// read it in the endpoint. Either map it (<c>Prop = body.Prop</c>) or add it to
/// <see cref="DeliberatelyUnmapped"/> WITH a written reason. Do not delete the assertion.
/// </para>
/// </remarks>
public sealed class ComposeSaveBodyMappingGuardTests
{
    private const string EndpointsRelativePath = "src/server/api/Sprk.Bff.Api/Api/ComposeSaveEndpoints.cs";

    /// <summary>
    /// Body properties the endpoint deliberately does NOT forward. Every entry carries its reason —
    /// an unexplained exemption is indistinguishable from an oversight six months later.
    /// </summary>
    private static readonly Dictionary<string, string> DeliberatelyUnmapped = new(StringComparer.Ordinal)
    {
        ["EditedParagraphs"] =
            "LEGACY detection-only. A payload still carrying the retired paragraph-diff shape is REJECTED " +
            "with a ProblemDetails (client too old) rather than forwarded — the property exists so a stale " +
            "payload is a clean 400 instead of a 500. Reading it into the request would defeat that.",
    };

    private static string EndpointSource =>
        File.ReadAllText(Path.Combine(SourceScan.RepoRoot, EndpointsRelativePath));

    /// <summary>Extracts the <c>SaveComposeDocumentBody</c> record's property names in declaration order.</summary>
    private static IReadOnlyList<string> BodyPropertyNames(string source)
    {
        var start = source.IndexOf("public sealed record SaveComposeDocumentBody(", StringComparison.Ordinal);
        Assert.True(start > -1, "the DTO must still be declared in this file — if it moved, update this guard");

        // Bound the slice at the NEXT top-level declaration rather than at a punctuation guess: doc
        // comments in this DTO contain parentheses and quotes, so scanning for a literal close-paren
        // silently truncated the parse and made the guard miss the last property — which is exactly the
        // kind of quiet under-reporting this whole file exists to prevent.
        var next = source.IndexOf("public sealed record SaveComposeDocumentResponse", start + 1, StringComparison.Ordinal);
        var end = next > start ? next : source.Length;

        var body = source[start..end];
        return Regex.Matches(body, @"\[property:\s*JsonPropertyName\(""[^""]+""\)\]\s*[\w<>,\?\.\[\]\s]+?\s(\w+)\s*(?:=|,|$)")
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    [Fact]
    public void EverySaveBodyProperty_IsEitherMappedOntoTheServiceRequest_OrDeclaredDeliberatelyUnmapped()
    {
        var source = EndpointSource;
        var properties = BodyPropertyNames(source);

        Assert.True(properties.Count > 0, "the DTO parse must find properties — an empty parse would make this test vacuous");
        Assert.True(
            properties.Contains("RevisionReport", StringComparer.Ordinal),
            "the r8 instance of this defect class must stay covered — if this property is gone, so is the reason this guard exists");

        var unread = properties
            .Where(p => !DeliberatelyUnmapped.ContainsKey(p))
            .Where(p => !Regex.IsMatch(source, $@"body\.{Regex.Escape(p)}\b"))
            .ToList();

        Assert.True(
            unread.Count == 0,
            "an unmapped body property is dropped SILENTLY — System.Text.Json ignores unknown JSON and the " +
            "field-by-field mapping simply never copies it, so the feature is dead at every layer above with " +
            "no error anywhere. Map it, or add it to DeliberatelyUnmapped with a reason. Unmapped: " +
            string.Join(", ", unread));
    }

    [Fact]
    public void TheGuardActuallyFires_WhenAPropertyIsLeftUnread()
    {
        // Negative control (ArchTests convention): a detector nobody has seen fail is a detector nobody
        // knows works. Simulates the exact defect — a declared property with no `body.X` read anywhere —
        // against the same matcher the real assertion uses.
        const string seeded = """
            public sealed record SaveComposeDocumentBody(
                [property: JsonPropertyName("tenantId")] string TenantId,
                [property: JsonPropertyName("revisionReport")] ComposeRevisionReportInput? RevisionReport = null);

            var request = new SaveComposeDocumentRequest { TenantId = body.TenantId };
            """;

        var properties = BodyPropertyNames(seeded);
        Assert.Contains("RevisionReport", properties);

        var unread = properties
            .Where(p => !DeliberatelyUnmapped.ContainsKey(p))
            .Where(p => !Regex.IsMatch(seeded, $@"body\.{Regex.Escape(p)}\b"))
            .ToList();

        Assert.True(
            unread.Count == 1 && unread[0] == "RevisionReport",
            "the seeded omission MUST be detected — otherwise the real assertion above proves nothing");
    }

    [Fact]
    public void TheGuardDoesNotFire_OnTheSanctionedMappedShape()
    {
        // Positive control: the guard must NOT flag correctly-mapped code, or it gets deleted rather
        // than obeyed. This caught two real defects in the auth-v4 guards for the same reason.
        const string sanctioned = """
            public sealed record SaveComposeDocumentBody(
                [property: JsonPropertyName("tenantId")] string TenantId,
                [property: JsonPropertyName("revisionReport")] ComposeRevisionReportInput? RevisionReport = null);

            var request = new SaveComposeDocumentRequest
            {
                TenantId = body.TenantId,
                RevisionReport = body.RevisionReport,
            };
            """;

        var unread = BodyPropertyNames(sanctioned)
            .Where(p => !DeliberatelyUnmapped.ContainsKey(p))
            .Where(p => !Regex.IsMatch(sanctioned, $@"body\.{Regex.Escape(p)}\b"))
            .ToList();

        Assert.True(unread.Count == 0, "correctly-mapped code must not trip the guard");
    }

    [Fact]
    public void SummaryPage_IsStillTheOpenInstanceOfThisDefectClass()
    {
        // Not a failure — a RECORD, kept executable so it cannot rot into stale prose (FAILURE-MODES
        // AP-12: "a comment becomes the constraint"). `SaveComposeDocumentRequest.SummaryPage` is
        // consumed by SaveAsync and has a generator + corpus seam test, but no HTTP body property and
        // no client sender, so the NDA Summary Page appendix cannot be produced.
        //
        // When someone wires it, this test flips to red and SHOULD be deleted — its whole purpose is to
        // stop the gap being forgotten, not to keep it.
        var source = EndpointSource;

        Assert.False(
            source.Contains("body.SummaryPage", StringComparison.Ordinal),
            "if the endpoint now forwards a summary page, the gap is CLOSED — delete this test and the " +
            "corresponding entry in the class remarks");
    }
}
