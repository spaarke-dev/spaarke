using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// FR-02 (code-quality-and-assurance-r4 task 002) — the shared-package census. Every sanctioned shared
/// package under <c>src/client/shared/</c> is listed here with a one-line reason, and the list is asserted
/// against the filesystem. A 16th package cannot appear silently.
///
/// <para><b>The specific failure this exists to prevent.</b> ADR-012 required an ADR amendment to add a
/// shared sibling package while simultaneously describing the sanctioned set as three named packages
/// "plus the domain component libraries (<c>@spaarke/events-components</c>, <b>etc.</b>)". An open set
/// makes that amendment requirement unenforceable by construction: any new package can be claimed as
/// already covered by the "etc.", so the rule could never be violated and therefore never enforced. Task
/// 001 closed the set to exactly 15. This census is what keeps it closed — without it, ADR-012's
/// enumeration is correct only until the next package lands, and wrong silently from that moment.</para>
///
/// <para><b>Why the failure message is long.</b> The point of this census is not to report a mismatch —
/// a diff would do that. It is to put ADR-012's three promotion-evaluation questions in front of the
/// person who just added a package, at the moment they are deciding, because that is the only moment the
/// questions are cheap to answer. A message reading "expected 15, found 16" teaches nothing and gets
/// resolved by editing the 15 to a 16.</para>
///
/// <para><b>No DI resolution anywhere in this file</b> — ADR-038 ban <b>B3</b>. This is a filesystem
/// question; a container could not answer it, since shared packages are TypeScript and never enter a .NET
/// service collection at all.</para>
///
/// <para><b>Naming and setup-ratio conventions.</b> Per <c>tests/CLAUDE.md</c>, structural fitness
/// functions name the invariant they enforce rather than following
/// <c>{Method}_{Scenario}_{ExpectedResult}</c> (B13 does not apply), and a high setup-to-assertion ratio
/// is inherent to a scan-based arrange (B15 does not apply).</para>
/// </summary>
public class SharedPackageCensusTests
{
    // =============================================================================================
    // THE CENSUS — ADR-012's closed enumeration, mirrored.
    // ---------------------------------------------------------------------------------------------
    // MAINTENANCE PROCEDURE — read before adding an entry here.
    //
    //   1. A failure here is NOT a signal to add a row. It is a signal to answer ADR-012's three
    //      promotion-evaluation questions in writing:
    //
    //        (1) Is the API stable across the consumers, or would each need its own branching?
    //        (2) Is it testable in isolation, without standing up consumer fixtures?
    //        (3) Is the commonality semantic or coincidental?
    //
    //      NONE of the three is a gate. A package that answers badly on all three may still be
    //      promoted, provided the reason is stated. They are prompts for a written judgment, not
    //      criteria to clear. Do not turn them into a checklist here or anywhere else.
    //
    //   2. Adding a shared package requires an AMENDMENT TO ADR-012 (CLAUDE.md §6.5 path B), not just
    //      a row in this array. The ADR is the decision; this file is the forcing function that keeps
    //      the ADR honest. Editing only this file inverts that relationship and defeats the census.
    //
    //   3. Every entry carries a reason. An unexplained entry is indistinguishable from an oversight
    //      six months later, and an unexplained LIST is what ADR-012 had before task 001 — a set that
    //      trailed off into "etc." and could not be checked.
    //
    //   4. This census does NOT rank, deprecate, or flag packages for removal. Anticipatory promotion
    //      is legitimate per ADR-012: @spaarke/visuals, @spaarke/legal-workspace and @spaarke/ai-context
    //      each have a single declared consumer today and that is a normal state, not a defect. A future
    //      change that adds a "consumers" column and asserts a minimum on it would be a count-proxy for
    //      a judgment question — the retired God-class ratchet in a new costume. Do not add one.
    //
    // =============================================================================================
    private static readonly IReadOnlyList<PackageEntry> Census = new[]
    {
        new PackageEntry(
            Directory: "Spaarke.AI.Context",
            Package: "@spaarke/ai-context",
            Reason: "AI context providers, service clients and hooks — the shared entry to AI features for React 19 surfaces."),

        new PackageEntry(
            Directory: "Spaarke.AI.Outputs",
            Package: "@spaarke/ai-outputs",
            Reason: "Output-pane and source-pane widgets plus their component registries — the render half of the AI surface, kept separate from the context half."),

        new PackageEntry(
            Directory: "Spaarke.AI.Widgets",
            Package: "@spaarke/ai-widgets",
            Reason: "Workspace and context widgets for the three-pane shell."),

        new PackageEntry(
            Directory: "Spaarke.Auth",
            Package: "@spaarke/auth",
            Reason: "Token acquisition, authenticated fetch and the token bridge — the one auth path every surface shares (ADR-028)."),

        new PackageEntry(
            Directory: "Spaarke.Communication.Components",
            Package: "@spaarke/communication-components",
            Reason: "Communications — dual-use across the communications-list workspace widget and the standalone sprk_communicationspage."),

        new PackageEntry(
            Directory: "Spaarke.Compose.Components",
            Package: "@spaarke/compose-components",
            Reason: "The TipTap-based Compose drafting workspace, mounted by the LegalWorkspace section shim. React 19; not PCF-safe."),

        new PackageEntry(
            Directory: "Spaarke.DailyBriefing.Components",
            Package: "@spaarke/daily-briefing-components",
            Reason: "Daily Briefing — dual-use across the standalone code page and the SpaarkeAi workspace widget."),

        new PackageEntry(
            Directory: "Spaarke.DocumentOperations",
            Package: "@spaarke/document-operations",
            Reason: "Cross-surface document verbs (Open-in-Word, download, delete, email-link, send-to-index) shared by SemanticSearch and Compose so they behave identically in both."),

        new PackageEntry(
            Directory: "Spaarke.Events.Components",
            Package: "@spaarke/events-components",
            Reason: "Events/Tasks — dual-use across the standalone EventsPage code page and the SpaarkeAi Calendar workspace widget. The canonical dual-use precedent."),

        new PackageEntry(
            Directory: "Spaarke.LegalWorkspace",
            Package: "@spaarke/legal-workspace",
            Reason: "A package BOUNDARY rather than a component set — it re-exports a barrel over files that stay under src/solutions/LegalWorkspace/src/, eliminating the SpaarkeAi source-alias trap. Source-only; excluded from the build orchestrator by design."),

        new PackageEntry(
            Directory: "Spaarke.Notifications",
            Package: "@spaarke/notifications",
            Reason: "The host-agnostic notification-spine client: SignalR negotiate/connect, envelope routing by kind, poll fallback on disconnect."),

        new PackageEntry(
            Directory: "Spaarke.SdapClient",
            Package: "@spaarke/sdap-client",
            Reason: "The typed BFF/SDAP API client. Shared so request shapes and error handling cannot drift per surface."),

        new PackageEntry(
            Directory: "Spaarke.SmartTodo.Components",
            Package: "@spaarke/smart-todo-components",
            Reason: "Smart To Do — dual-use across the LegalWorkspace section shim and SpaarkeAi widget registration."),

        new PackageEntry(
            Directory: "Spaarke.UI.Components",
            Package: "@spaarke/ui-components",
            Reason: "The primary library — Fluent v9 UX components, hooks and abstracted-I/O services consumed across PCF, Code Pages and the SPA. The default destination; every other package needs a reason not to be this one."),

        new PackageEntry(
            Directory: "Spaarke.Visuals",
            Package: "@spaarke/visuals",
            Reason: "Presentational data-viz primitives, quarantining the heavyweight @fluentui/react-charting dependency from every ui-components consumer (ADR-012 amendment 2026-07-12)."),
    };

    /// <summary>
    /// <c>@spaarke/*</c> names that are APPLICATIONS, not shared-library members. They are listed so the
    /// distinction is written down rather than implicit, and
    /// <see cref="NoApplicationScopedPackageHasCreptIntoTheSharedTree"/> gives the list teeth: it asserts
    /// none of them has appeared under <c>src/client/shared/</c>.
    ///
    /// <para>Note that a directory-keyed census cannot be confused by these in the first place — none of
    /// them lives in the shared tree, so none can enter the scan. The list therefore documents an
    /// invariant rather than filtering a scan, which is why it is asserted rather than subtracted. Were
    /// the census ever re-keyed on package NAMES, this list would become load-bearing immediately.</para>
    /// </summary>
    private static readonly IReadOnlyList<(string Package, string Reason)> ApplicationScopedPackages = new[]
    {
        ("@spaarke/office-addins",
            "An application: the Outlook/Word add-in host at src/client/office-addins/. Ships as an add-in, not consumed as a library."),
        ("@spaarke/secure-project-workspace",
            "An application: the external-access SPA at src/client/external-spa/. A deployable site, not a library."),
        ("@spaarke/document-upload-wizard",
            "An application: the Code Page solution at src/solutions/DocumentUploadWizard/. Wizard CONTENT that is shared lives in @spaarke/ui-components; this is its host."),
        ("@spaarke/pcf-shared",
            "Not a published package at all — the name appears only in a usage example in a doc comment at src/client/pcf/shared/index.ts. Listed so that a future reader who greps the name finds this note instead of concluding a shared package went missing."),
    };

    // =============================================================================================

    [Fact(DisplayName = "FR-02: the shared-package set on disk is exactly ADR-012's enumeration")]
    public void SharedPackageSetMatchesTheAdr012Enumeration()
    {
        var actual = SourceScan.SharedClientPackageDirectories().ToList();
        var expected = Census.Select(e => e.Directory).OrderBy(d => d, StringComparer.Ordinal).ToList();

        var unlisted = actual.Except(expected, StringComparer.Ordinal).ToList();
        var missing = expected.Except(actual, StringComparer.Ordinal).ToList();

        Assert.True(
            unlisted.Count == 0 && missing.Count == 0,
            BuildFailureMessage(unlisted, missing));
    }

    [Fact(DisplayName = "FR-02: every census entry carries a package name and a substantive reason")]
    public void EveryCensusEntryIsExplained()
    {
        // Without this the census degrades into a list of directory names — which is materially what
        // ADR-012 had before task 001, and it trailed off into "etc." precisely because nothing forced
        // each member to justify itself.
        var unexplained = Census
            .Where(e => string.IsNullOrWhiteSpace(e.Package)
                        || string.IsNullOrWhiteSpace(e.Reason)
                        || e.Reason.Trim().Length < 40)
            .Select(e => e.Directory)
            .ToList();

        Assert.True(
            unexplained.Count == 0,
            "Every shared-package census entry must name its package and give a substantive reason it is "
            + "in the shared library. Entries missing one: " + string.Join(", ", unexplained));
    }

    [Fact(DisplayName = "FR-02: no application-scoped @spaarke/* package has crept into the shared tree")]
    public void NoApplicationScopedPackageHasCreptIntoTheSharedTree()
    {
        // The allow-list, asserted rather than merely declared. An application appearing under
        // src/client/shared/ would be a real architectural event — an app being mistaken for a library —
        // and it should fail here rather than be silently subtracted by a filter.
        var sharedPackageNames = Census.Select(e => e.Package).ToHashSet(StringComparer.Ordinal);

        var crept = ApplicationScopedPackages
            .Where(a => sharedPackageNames.Contains(a.Package))
            .Select(a => a.Package)
            .ToList();

        Assert.True(
            crept.Count == 0,
            "These are APPLICATIONS, not shared-library members, but they appear in the shared-package "
            + "census: " + string.Join(", ", crept) + ". An application does not become a library by "
            + "moving directory. Move it back, or — if it genuinely became a library — amend ADR-012 "
            + "first and answer the three promotion questions there.");
    }

    [Fact(DisplayName = "FR-02: every census entry names a directory that exists")]
    public void EveryCensusEntryNamesADirectoryThatExists()
    {
        // The mirror of the unlisted check. A census that over-reports is as misleading as one that
        // under-reports: it asserts a package is sanctioned and present when it has in fact been removed.
        var onDisk = SourceScan.SharedClientPackageDirectories().ToHashSet(StringComparer.Ordinal);
        var phantom = Census.Where(e => !onDisk.Contains(e.Directory)).Select(e => e.Directory).ToList();

        Assert.True(
            phantom.Count == 0,
            "CENSUSED BUT ABSENT: " + string.Join(", ", phantom) + ". If a package was removed or renamed, "
            + "amend ADR-012 and update this census together — a census that claims a package exists when "
            + "it does not manufactures exactly the false confidence it was built to remove.");
    }

    // =============================================================================================
    // Negative controls
    // =============================================================================================

    [Fact(DisplayName = "FR-02: negative control — a 16th package fails, and the message teaches")]
    public void Census_NegativeControl_FiresOnASixteenthPackage()
    {
        // Proves the detector fires. Asserting only that it throws would leave the MESSAGE — which is
        // this census's entire reason for existing over a plain count — unverified. Every string checked
        // below is load-bearing: drop any one and the failure stops teaching what to do next.
        var expected = Census.Select(e => e.Directory).OrderBy(d => d, StringComparer.Ordinal).ToList();
        var withInterloper = expected.Concat(new[] { "Spaarke.Interloper" })
            .OrderBy(d => d, StringComparer.Ordinal).ToList();

        var unlisted = withInterloper.Except(expected, StringComparer.Ordinal).ToList();
        Assert.Single(unlisted);

        var message = BuildFailureMessage(unlisted, missing: new List<string>());

        Assert.Contains("Spaarke.Interloper", message, StringComparison.Ordinal);

        // ADR-012's three promotion-evaluation questions.
        Assert.Contains("Is the API stable across the consumers", message, StringComparison.Ordinal);
        Assert.Contains("testable in isolation", message, StringComparison.Ordinal);
        Assert.Contains("semantic or coincidental", message, StringComparison.Ordinal);

        // The amendment requirement — the action the reader must take.
        Assert.Contains("ADR-012", message, StringComparison.Ordinal);
        Assert.Contains("amendment", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("§6.5", message, StringComparison.Ordinal);

        // And that the questions are NOT presented as a bar to clear. A reader who takes them as a gate
        // will suppress legitimate anticipatory promotion, which ADR-012 explicitly sanctions.
        Assert.Contains("None of the three is a gate", message, StringComparison.Ordinal);

        // The message must not instruct the reader to simply edit this file — the failure mode that turns
        // a census into a rubber stamp.
        Assert.DoesNotContain("just add it to the census", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "FR-02: negative control — a removed package fails too, in the other direction")]
    public void Census_NegativeControl_FiresOnARemovedPackage()
    {
        var expected = Census.Select(e => e.Directory).OrderBy(d => d, StringComparer.Ordinal).ToList();
        var withoutOne = expected.Where(d => d != "Spaarke.Visuals").ToList();

        var missing = expected.Except(withoutOne, StringComparer.Ordinal).ToList();
        Assert.Single(missing);

        var message = BuildFailureMessage(unlisted: new List<string>(), missing: missing);
        Assert.Contains("Spaarke.Visuals", message, StringComparison.Ordinal);
        Assert.Contains("CENSUSED BUT ABSENT", message, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "FR-02: negative control — the scan is keyed on the directory, not on package.json")]
    public void Census_CountsADirectoryWithNoPackageJson()
    {
        // WHY THIS CONTROL EXISTS. Task 002 was authored believing Spaarke.LegalWorkspace had no
        // package.json, and that belief was the stated reason for keying on the directory. The belief was
        // WRONG — all 15 packages carry a manifest (measured 2026-09-04, task-001-deviations.md §1). The
        // DESIGN is still right, but its justification could no longer be "LegalWorkspace proves it", so
        // this control proves it directly instead of resting on a fact that is not true.
        //
        // A real temporary directory is used rather than an in-memory list because the claim under test is
        // about the enumerator's behaviour on disk. An in-memory stand-in would be asserting the design
        // rather than testing it.
        var sharedRoot = Path.Combine(SourceScan.RepoRoot, "src", "client", "shared");
        var probe = Path.Combine(sharedRoot, "Spaarke.CensusProbe.TmpDoNotCommit");

        Assert.False(
            Directory.Exists(probe),
            $"The probe directory {probe} already exists — a previous run did not clean up. Remove it.");

        try
        {
            Directory.CreateDirectory(probe);
            Assert.False(File.Exists(Path.Combine(probe, "package.json")));

            var scanned = SourceScan.SharedClientPackageDirectories().ToList();

            Assert.Contains("Spaarke.CensusProbe.TmpDoNotCommit", scanned);
        }
        finally
        {
            if (Directory.Exists(probe))
            {
                Directory.Delete(probe, recursive: true);
            }
        }

        // And confirm the probe is gone, so a crash here cannot leave the repository dirty for the next
        // test in the class — which would make the positive control fail for an unrelated reason.
        Assert.False(Directory.Exists(probe));
    }

    [Fact(DisplayName = "FR-02: negative control — the scan actually reaches the shared tree")]
    public void Census_ScanIsNotSilentlyEmpty()
    {
        // A scan that enumerated nothing would make every "no unlisted packages" assertion above pass
        // vacuously. This is the same blind-spot control CredentialCensusTests books for its own scan.
        var scanned = SourceScan.SharedClientPackageDirectories().ToList();

        Assert.NotEmpty(scanned);
        Assert.Contains("Spaarke.UI.Components", scanned);
    }

    // =============================================================================================
    // Machinery — filesystem enumeration only. No DI container is constructed or resolved in this file,
    // and no directory walker is hand-rolled: the enumerator lives on SourceScan (ADR-038, reuse rule).
    // =============================================================================================

    private sealed record PackageEntry(string Directory, string Package, string Reason);

    /// <summary>
    /// The teaching failure message. Built by a named method rather than inline so the negative controls
    /// can assert its CONTENT — a message only ever produced inside a failing assertion is a message
    /// nobody has read.
    /// </summary>
    private static string BuildFailureMessage(IReadOnlyList<string> unlisted, IReadOnlyList<string> missing)
    {
        var parts = new List<string>
        {
            "The shared-package census does not match src/client/shared/.",
            string.Empty,
            "A failure here is NOT a prompt to edit the list until it matches. ADR-012 enumerates the "
            + "sanctioned shared packages as a CLOSED set precisely so that adding one is a decision "
            + "somebody makes on the record, rather than a thing that happens.",
        };

        if (unlisted.Count > 0)
        {
            parts.Add(string.Empty);
            parts.Add("UNLISTED shared package(s): " + string.Join(", ", unlisted));
            parts.Add(string.Empty);
            parts.Add(
                "Before anything else, answer ADR-012's three promotion-evaluation questions in writing:");
            parts.Add(string.Empty);
            parts.Add("  (1) Is the API stable across the consumers, or would each need its own branching?");
            parts.Add("      Branch-per-consumer inside a shared component means the commonality is");
            parts.Add("      shallower than it looks.");
            parts.Add("  (2) Is it testable in isolation, without standing up consumer fixtures?");
            parts.Add("      If the only way to test it is through a host, it is probably still host code.");
            parts.Add("  (3) Is the commonality semantic or coincidental?");
            parts.Add("      Two surfaces rendering similar markup for unrelated reasons will diverge on");
            parts.Add("      the next requirement.");
            parts.Add(string.Empty);
            parts.Add(
                "None of the three is a gate. A package that answers badly on all three may still be "
                + "promoted, provided the reason is stated — anticipatory promotion is explicitly "
                + "legitimate. They are prompts for a written judgment, not criteria to clear.");
            parts.Add(string.Empty);
            parts.Add(
                "THEN: adding a sanctioned shared package requires an AMENDMENT to ADR-012 "
                + "(.claude/adr/ADR-012-shared-components.md) under CLAUDE.md §6.5 path B — record the "
                + "rule challenged, the conflict, the path and the rationale. Update this census in the "
                + "SAME change, so the ADR and the forcing function never disagree.");
            parts.Add(string.Empty);
            parts.Add(
                "If the directory is NOT meant to be a shared package — an application, a scratch folder, "
                + "a build artifact — it does not belong under src/client/shared/. Move it out.");
        }

        if (missing.Count > 0)
        {
            parts.Add(string.Empty);
            parts.Add("CENSUSED BUT ABSENT: " + string.Join(", ", missing));
            parts.Add(
                "These are in the census but not on disk. If a package was removed or renamed, amend "
                + "ADR-012 and update this census together. A census that over-reports manufactures the "
                + "same false confidence as one that under-reports.");
        }

        return string.Join("\n", parts);
    }
}
