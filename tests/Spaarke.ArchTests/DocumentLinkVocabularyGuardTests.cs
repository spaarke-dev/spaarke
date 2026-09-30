using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// Structural fitness function (the eighth KEEP path — ADR-038 Amendment A1): the <c>sprk_document</c>
/// record-link vocabulary is declared in exactly ONE place.
/// </summary>
/// <remarks>
/// <para>
/// <b>The defect this prevents, which already happened.</b> The vocabulary was declared twice —
/// <c>AttachmentDocumentAssociationRung.DocumentLinkFields</c> and
/// <c>ComposeService.DocumentAssociationLookupAttributes</c> — in two different subsystems. Both drifted.
/// Measured against a live <c>describe('tables/sprk_document')</c> on 2026-09-04, the table carried
/// <b>17</b> link columns and the declarations knew <b>6</b>.
/// </para>
/// <para>
/// The consequence was silent. Compose create-on-save copies a source document's links onto the new Word
/// document so the two file together; it copied only the six it knew. A PDF filed under an Agreement
/// produced a Word document with no Agreement link — no exception, no log, nothing red. The user simply
/// finds the document is not where they filed the original.
/// </para>
/// <para>
/// A second copy of a vocabulary is not a style problem: it is two things that must agree, with nothing
/// making them agree. This guard makes adding one fail the build.
/// </para>
/// <para>
/// <b>If this fails</b>, you have hard-coded document link columns somewhere. Reference
/// <c>Spaarke.Dataverse.DocumentLinkFields</c> instead. If your consumer needs a SUBSET, express it as a
/// named projection with its exclusions written down — an exclusion expressed as silent omission is
/// indistinguishable from the oversight above.
/// <para>
/// <b>The canonical home MOVED on 2026-09-29.</b> It was this project's BFF-local
/// <c>Services/Documents/DocumentLinkFieldMap</c>; <c>unified-access-control-r2</c> had independently
/// hoisted the same closed set to <c>Spaarke.Dataverse</c> a day later, which is the better home (a
/// shared library, carrying the case-sensitive <c>SchemaName</c>). Both were right about the problem and
/// neither could see the other. The guard now protects the shared home, and still scans BFF sources —
/// which is where every copy so far has appeared.
/// </para>
/// </para>
/// </remarks>
public sealed class DocumentLinkVocabularyGuardTests
{
    /// <summary>
    /// The canonical declaration, in the SHARED library — deliberately outside the BFF tree this guard
    /// scans, so it is not an allowlist entry but a separate positive assertion (see
    /// <see cref="TheCanonicalDeclaration_StillCarriesTheVocabulary"/>).
    /// </summary>
    private static readonly string CanonicalPath =
        Path.Combine(SourceScan.RepoRoot, "src", "server", "shared", "Spaarke.Dataverse", "Models.cs");

    /// <summary>
    /// Files allowed to name several link columns without being the canonical declaration. Every entry
    /// carries its reason — an unexplained exemption is indistinguishable from an oversight later.
    /// </summary>
    private static readonly Dictionary<string, string> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        ["DocumentLinkFieldMap.cs"] =
            "Declares the legacy→successor MAPPING (4 entries), which necessarily names its 4 successor " +
            "columns. That is not a vocabulary copy and cannot drift into one: it holds no full list, " +
            "derives every column-shaped member from Spaarke.Dataverse.DocumentLinkFields, and names 4 of " +
            "12 Related* columns — below any plausible copy. The distinction matters: a mapping that could " +
            "not name its own targets would be useless, whereas the failure this guard prevents is a " +
            "SECOND ENUMERATION of the closed set.",
    };

    /// <summary>
    /// A document link column literal. Deliberately narrow: the `Related*` family plus the four legacy
    /// unprefixed forms. Matching bare `"sprk_matter"` alone would fire on every entity-logical-name
    /// reference in the codebase, so the legacy names are only counted alongside `sprk_related*` ones.
    /// </summary>
    private static readonly Regex RelatedLinkLiteral = new(
        @"""sprk_related(agreement|communication|contact|event|invoice|matter|organization|project|servicerequest|todo|vendororg|workassignment)""",
        RegexOptions.Compiled);

    private const int ClusterThreshold = 3;

    private static IEnumerable<string> BffSourceFiles() =>
        Directory.EnumerateFiles(
                Path.Combine(SourceScan.RepoRoot, "src", "server", "api", "Sprk.Bff.Api"),
                "*.cs",
                SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    /// <summary>Distinct document-link column literals in <paramref name="source"/>.</summary>
    private static int DistinctLinkLiterals(string source) =>
        RelatedLinkLiteral.Matches(source).Select(m => m.Value).Distinct(StringComparer.Ordinal).Count();

    [Fact]
    public void OnlyTheCanonicalMap_DeclaresTheDocumentLinkVocabulary()
    {
        var offenders = new List<string>();
        var scanned = 0;

        foreach (var file in BffSourceFiles())
        {
            scanned++;
            var name = Path.GetFileName(file);
            if (Allowed.ContainsKey(name))
            {
                continue;
            }

            var count = DistinctLinkLiterals(File.ReadAllText(file));
            if (count >= ClusterThreshold)
            {
                offenders.Add($"{name} ({count} link literals)");
            }
        }

        Assert.True(scanned > 0, "the scan must find BFF sources — an empty scan makes this vacuous, not clean");

        Assert.True(
            offenders.Count == 0,
            "the sprk_document link vocabulary must be declared ONCE, in Spaarke.Dataverse.DocumentLinkFields. " +
            "A second copy drifts silently: the last pair diverged to 6 of 17 columns and Compose quietly " +
            "dropped a document's filing for ten link types. Reference the map, or add a named projection to " +
            "it with your exclusions written down. Offending files: " + string.Join(", ", offenders));
    }

    [Fact]
    public void TheCanonicalDeclaration_StillCarriesTheVocabulary()
    {
        // Without this, emptying the canonical list would make the guard above pass vacuously — "no file
        // declares the vocabulary twice" is trivially true when no file declares it at all.
        Assert.True(File.Exists(CanonicalPath), $"the canonical declaration must exist at {CanonicalPath}");

        var count = DistinctLinkLiterals(File.ReadAllText(CanonicalPath));
        Assert.True(
            count >= 12,
            $"Spaarke.Dataverse.DocumentLinkFields must still carry the full Related* vocabulary (found " +
            $"{count} of 12) — if columns were removed from the schema, update DocumentLinkFieldsTests too");
    }

    [Fact]
    public void TheGuardActuallyFires_OnASecondHardCodedList()
    {
        // Negative control: a detector nobody has seen fail is a detector nobody knows works.
        const string seeded = """
            private static readonly string[] MyOwnCopy =
            {
                "sprk_relatedmatter",
                "sprk_relatedproject",
                "sprk_relatedagreement",
            };
            """;

        Assert.True(DistinctLinkLiterals(seeded) >= ClusterThreshold);
    }

    [Fact]
    public void TheGuardDoesNotFire_OnAnIncidentalMention()
    {
        // Positive control: a guard that flags ordinary code gets deleted rather than obeyed. Referencing
        // one or two columns by name — a targeted query, a comment, a test fixture — is not a vocabulary.
        const string sanctioned = """
            // Reads the matter link only; see DocumentLinkFieldMap for the full vocabulary.
            var matter = doc.GetAttributeValue<EntityReference>("sprk_relatedmatter");
            var project = doc.GetAttributeValue<EntityReference>("sprk_relatedproject");
            """;

        Assert.True(DistinctLinkLiterals(sanctioned) < ClusterThreshold);
    }
}
