using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// Structural fitness function (the eighth KEEP path — ADR-038 Amendment A1): across the WHOLE BFF
/// inbound surface, every property declared on a request-body DTO must actually be read by the endpoint
/// that receives it, or be listed here as a deliberate omission with a written reason.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists alongside <see cref="ComposeSaveBodyMappingGuardTests"/>.</b> That guard watches ONE
/// DTO — <c>SaveComposeDocumentBody</c> — because that is where the defect class was first characterised.
/// Watching one instance of a class is how the next instance gets through, and one duly did:
/// <c>SummarizeSessionRequest.Style</c> (found 2026-09-03) was declared, richly documented as "passed
/// through to the system prompt", and read by nothing at any layer. The narrow guard could not have seen
/// it. This file is that guard generalised to every inbound body DTO in <c>Api/**</c>.
/// </para>
/// <para>
/// <b>The defect class.</b> Minimal-API handlers bind a body DTO and then hand-map it onto a service
/// request field by field. A property that is declared but never read is dropped in total silence:
/// System.Text.Json does not complain, the compiler does not complain, and seam tests below the endpoint
/// stay green because they construct the service request directly and never traverse the mapping. The
/// feature is wired at every other layer and does nothing. Four confirmed instances to date — see the
/// remarks on <see cref="ComposeSaveBodyMappingGuardTests"/> for the first three.
/// </para>
/// <para>
/// <b>Deleting beats implementing, more often than it looks.</b> The <c>Style</c> instance was resolved by
/// REMOVING the property, not by honouring its doc comment: threading free caller text into a system
/// prompt is what ADR-039's closed structured-operand vocabulary forbids, so the field advertised a
/// capability the architecture rejects. When this guard fires, "make the documented behaviour real" is one
/// candidate repair and not automatically the right one.
/// </para>
/// <para>
/// <b>Scope and honesty about it.</b> This scans <c>Api/**</c> for records used as a handler parameter in
/// their own declaring file, and requires the read to appear in THAT file. File-scoped is deliberate: a
/// corpus-wide search for <c>x.Prop</c> is satisfied by the SERVICE layer reading its own request object,
/// which masks precisely the body-to-request copy that goes missing. An early version of this scan was
/// corpus-wide and was blind to the very defect it was written for.
/// </para>
/// </remarks>
public sealed class InboundBodyDtoMappingGuardTests
{
    /// <summary>
    /// Body properties an endpoint deliberately does NOT read, keyed <c>Record.Property</c>. Every entry
    /// carries its reason — an unexplained exemption is indistinguishable from an oversight six months on.
    /// </summary>
    private static readonly Dictionary<string, string> DeliberatelyUnread = new(StringComparer.Ordinal)
    {
        ["SaveComposeDocumentBody.EditedParagraphs"] =
            "LEGACY detection-only. A payload still carrying the retired paragraph-diff shape is REJECTED " +
            "with a ProblemDetails (client too old) rather than forwarded. Reading it into the request " +
            "would defeat the rejection. Also covered by ComposeSaveBodyMappingGuardTests.",
    };

    private static string ApiRoot => Path.Combine(
        SourceScan.RepoRoot, "src", "server", "api", "Sprk.Bff.Api", "Api");

    private static IEnumerable<string> ApiSourceFiles() =>
        Directory.EnumerateFiles(ApiRoot, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    private static readonly Regex RecordDecl = new(@"public sealed record (\w+)\(", RegexOptions.Compiled);

    /// <summary>A declared body property and the record it belongs to.</summary>
    private sealed record BodyProp(string Record, string Property, string ParamName);

    /// <summary>
    /// Finds every record in <paramref name="source"/> that is used as a handler parameter in the same
    /// file, and returns its declared properties. Returns the parameter identifier too, because the read
    /// we require is <c>{param}.{Property}</c> — not any old member access.
    /// </summary>
    private static IReadOnlyList<BodyProp> InboundBodyProperties(string source)
    {
        var found = new List<BodyProp>();

        foreach (Match decl in RecordDecl.Matches(source))
        {
            var recordName = decl.Groups[1].Value;

            // Balanced-paren scan for the record header. A regex cannot do this: the headers contain
            // generics, attributes, default values and doc comments with their own parentheses.
            var open = source.IndexOf('(', decl.Index);
            if (open < 0) continue;
            var depth = 0;
            var close = -1;
            for (var i = open; i < source.Length; i++)
            {
                if (source[i] == '(') depth++;
                else if (source[i] == ')')
                {
                    depth--;
                    if (depth == 0) { close = i; break; }
                }
            }
            if (close < 0) continue;
            var header = source[open..(close + 1)];

            // INBOUND test: the record appears as a parameter (usually `[FromBody] X body`) in this file.
            var asParam = Regex.Match(
                source, @"[\(,]\s*(?:\[FromBody\]\s*)?" + Regex.Escape(recordName) + @"\??\s+(\w+)\s*[,\)]");
            if (!asParam.Success) continue;
            var paramName = asParam.Groups[1].Value;

            foreach (var prop in DeclaredProperties(header))
                found.Add(new BodyProp(recordName, prop, paramName));
        }

        return found;
    }

    /// <summary>Property names from a positional-record header, attributed or not.</summary>
    private static IEnumerable<string> DeclaredProperties(string header)
    {
        // Attributed form first: [property: JsonPropertyName("x")] string? Foo = null
        var attributed = Regex.Matches(
                header, @"\[property:\s*JsonPropertyName\(""[^""]+""\)\]\s*[\w<>,\?\.\[\]\s]+?\s(\w+)\s*(?:=|,|\))")
            .Select(m => m.Groups[1].Value)
            .ToList();
        if (attributed.Count > 0) return attributed.Distinct(StringComparer.Ordinal);

        // Convention-bound form: split the header on top-level commas and take the trailing identifier.
        var inner = header[1..^1];
        var parts = new List<string>();
        var depth = 0;
        var cur = new System.Text.StringBuilder();
        foreach (var ch in inner)
        {
            if (ch is '<' or '(' or '[') depth++;
            else if (ch is '>' or ')' or ']') depth--;

            if (ch == ',' && depth == 0) { parts.Add(cur.ToString()); cur.Clear(); }
            else cur.Append(ch);
        }
        parts.Add(cur.ToString());

        return parts
            .Select(p => Regex.Replace(p, @"//[^\n]*", string.Empty))
            .Select(p => Regex.Replace(p, @"=\s*[^,]*$", string.Empty))
            .Select(p => Regex.Replace(p, @"\[[^\]]*\]", string.Empty).Trim())
            .Select(p => Regex.Match(p, @"^[\w<>\?\.\[\],\s]+?\s(\w+)$"))
            .Where(m => m.Success)
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>The properties of <paramref name="source"/>'s inbound DTOs that nothing in it reads.</summary>
    private static IReadOnlyList<BodyProp> UnreadIn(string source) =>
        InboundBodyProperties(source)
            .Where(bp => !DeliberatelyUnread.ContainsKey($"{bp.Record}.{bp.Property}"))
            .Where(bp => !Regex.IsMatch(
                source, Regex.Escape(bp.ParamName) + @"\s*\??\s*\.\s*" + Regex.Escape(bp.Property) + @"\b"))
            .ToList();

    [Fact]
    public void EveryInboundBodyProperty_IsReadByItsEndpoint_OrDeclaredDeliberatelyUnread()
    {
        var offenders = new List<string>();
        var scanned = 0;

        foreach (var file in ApiSourceFiles())
        {
            var source = File.ReadAllText(file);
            var inbound = InboundBodyProperties(source);
            if (inbound.Count == 0) continue;
            scanned += inbound.Count;

            foreach (var bp in UnreadIn(source))
                offenders.Add($"{Path.GetFileName(file)}: {bp.Record}.{bp.Property}");
        }

        Assert.True(scanned > 0, "the scan must find inbound body properties — an empty scan is vacuous, not clean");

        Assert.True(
            offenders.Count == 0,
            "a declared-but-unread body property is dropped SILENTLY — no exception, no log, no failing " +
            "test — so the feature is dead at every layer above it while appearing wired. Either read it, " +
            "DELETE it (often the right answer — see this class's remarks on the Style instance), or add " +
            "it to DeliberatelyUnread with a reason. Unread: " + string.Join("; ", offenders));
    }

    [Fact]
    public void TheGuardActuallyFires_WhenABodyPropertyIsLeftUnread()
    {
        // Negative control. A detector nobody has watched fail is a detector nobody knows works — and this
        // one shipped a corpus-wide first draft that could not see its own motivating defect.
        const string seeded = """
            internal static void Map(IEndpointRouteBuilder app) =>
                app.MapPost("/x", async ([FromBody] SummarizeSessionRequest body) =>
                {
                    var args = new { fileIds = body.FileIds };
                });

            public sealed record SummarizeSessionRequest(
                IReadOnlyList<string>? FileIds = null,
                string? Style = null);
            """;

        var unread = UnreadIn(seeded);

        Assert.True(
            unread.Count == 1 && unread[0].Property == "Style",
            "the seeded omission MUST be detected, or the assertion above proves nothing. Got: " +
            string.Join(", ", unread.Select(u => u.Property)));
    }

    [Fact]
    public void TheGuardDoesNotFire_OnTheSanctionedMappedShape()
    {
        // Positive control: a guard that flags correct code gets deleted rather than obeyed.
        const string sanctioned = """
            internal static void Map(IEndpointRouteBuilder app) =>
                app.MapPost("/x", async ([FromBody] SummarizeSessionRequest body) =>
                {
                    var args = new { fileIds = body.FileIds, style = body.Style };
                });

            public sealed record SummarizeSessionRequest(
                IReadOnlyList<string>? FileIds = null,
                string? Style = null);
            """;

        Assert.Empty(UnreadIn(sanctioned));
    }

    [Fact]
    public void TheGuardSeesBothAttributedAndConventionBoundDtos()
    {
        // The two DTO dialects in this codebase are scanned by different code paths, and the attributed
        // path short-circuits the other. A regression in either half would silently halve the coverage
        // while the headline assertion stayed green — so pin both shapes.
        const string attributed = """
            internal static void Map() => app.MapPost("/x", ([FromBody] AttributedBody body) => body.Kept);

            public sealed record AttributedBody(
                [property: JsonPropertyName("kept")] string Kept,
                [property: JsonPropertyName("dropped")] string? Dropped = null);
            """;

        var attrUnread = UnreadIn(attributed);
        Assert.True(
            attrUnread.Count == 1 && attrUnread[0].Property == "Dropped",
            "the JsonPropertyName-attributed dialect must be scanned");

        const string convention = """
            internal static void Map() => app.MapPost("/x", ([FromBody] PlainBody body) => body.Kept);

            public sealed record PlainBody(
                string Kept,
                string? Dropped = null);
            """;

        var plainUnread = UnreadIn(convention);
        Assert.True(
            plainUnread.Count == 1 && plainUnread[0].Property == "Dropped",
            "the convention-bound dialect must be scanned — this is the dialect the Style defect lived in");
    }
}
