using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Sprk.Bff.Api.Tests.Services.Ai.Insights.Fixtures;

/// <summary>
/// Wave D7 (task 036) synthetic legal-document + Dataverse-entity fixture manifest.
/// Provides typed access to fixtures committed at <c>tests/Insights/fixtures/r2/</c>
/// which are CopyToOutputDirectory'd next to the test assembly via
/// <c>Sprk.Bff.Api.Tests.csproj</c>'s Content glob.
/// </summary>
/// <remarks>
/// <para>
/// <b>What this is for</b>: Wave D's 3-area × 2-doc-type baseline for the eval harness
/// (spec.md NFR-09: ≥3 practice areas × 2 entity types covered). Per task 036 POML
/// acceptance criteria, ≥18 synthetic documents exist (3 areas × 2 entity types × 3 docs).
/// These fixtures exercise:
/// <list type="number">
///   <item>Per-area Layer 1 prompts (INS-L1C-CTRNS@v1 / INS-L1C-IPPAT@v1 / INS-L1C-BNKF@v1)
///         — at least one matter-shaped doc per (area × type) so routing can be tested.</item>
///   <item>Per-(area, type) Layer 2 schemas (5 D-G2 pairs landed in Wave D3) — extracted-field
///         validation targets.</item>
///   <item>CTRNS × NDA gate-fail (<see cref="GateFailCtrnsNda"/>) — Layer 1 classifies,
///         <c>sprk_layer2actioncode</c> is NULL on the matrix row, no Layer 2 dispatch.</item>
///   <item>Multi-entity subjects (matter / project / invoice resolvers from task 034) —
///         <see cref="SyntheticMatterId"/> / <see cref="SyntheticProjectId"/> /
///         <see cref="SyntheticInvoiceId"/> are real Dataverse rows in Spaarke Dev.</item>
///   <item>Index dual-write/read (task 035 hybrid scope) — matter observations should
///         dual-write <c>scope.matterId</c> AND <c>scope.entityType/entityId</c>;
///         project + invoice observations write only the multi-entity fields.</item>
///   <item>Routing fallback (task 033 D-G3 territory) — <see cref="UncoveredIppatTradeSecrets"/>
///         is an intentionally-uncovered (IPPAT, TRADE_SECRETS_POLICY) pair so the
///         fallback path (gate-fail with <c>signal=unknown_pair_for_practice_area</c>)
///         can be exercised.</item>
/// </list>
/// </para>
/// <para>
/// <b>How tests consume this</b>: tests pass the fixture path to AiAnalysisNodeExecutor
/// (or to a synthesis playbook input) as the sanitized-document body. Tests use
/// <see cref="ReadFixture(string)"/> to obtain the text content. xunit
/// <c>MemberData</c> can drive parameterized tests over <see cref="AllMatterFixtures"/>
/// / <see cref="AllProjectFixtures"/> / <see cref="AllFixtures"/>.
/// </para>
/// <para>
/// <b>Generation method</b>: all fixtures are LLM-assisted realistic synthetic legal
/// prose. Every party name, jurisdiction, monetary amount, GUID, USPTO application
/// number, and personal identifier is fabricated. NO REAL CLIENT DATA. The style
/// matches the existing r1 fixtures at <c>tests/Insights/fixtures/sample-*.txt</c>.
/// </para>
/// <para>
/// <b>Content style decision</b>: realistic prose (not lorem ipsum) so gpt-4o-mini
/// (Layer 1 classifier) + gpt-4o (Layer 2 extractor) can be reasonably expected to
/// classify + extract correctly during eval-harness runs. Length: 60–250 lines per
/// document — enough to exercise sanitization + classification + extraction without
/// running per-document inference past 2 minutes.
/// </para>
/// <para>
/// <b>Dataverse entity GUIDs</b>: the matter GUID is the existing test matter created
/// during Wave B5 smoke (Commercial Transactions practice area, code CMRCL-293209).
/// The project and invoice GUIDs are fresh rows created during task 036 execution.
/// Tests may safely use them for <c>matter:</c>/<c>project:</c>/<c>invoice:</c> subject
/// resolution against Dataverse Dev via the resolvers added in task 034.
/// </para>
/// </remarks>
public static class SyntheticDocuments
{
    /// <summary>
    /// Root directory inside the test output for r2 synthetic fixtures. The
    /// project's <c>Content</c> glob copies <c>tests/Insights/fixtures/r2/**/*.txt</c>
    /// into <c>{bin}/Insights/fixtures/r2/...</c>.
    /// </summary>
    public const string FixtureRoot = "Insights/fixtures/r2";

    // ----- Synthetic Dataverse entity GUIDs (Spaarke Dev) ----------------------

    /// <summary>
    /// Existing synthetic matter row in Spaarke Dev — Wave B5 smoke matter
    /// ("Test New Matter via Workspace", Commercial Transactions, CMRCL-293209).
    /// Practice area: CTRNS. Re-used in r2 D7 to anchor the matter-subject test
    /// surface across CTRNS fixtures.
    /// </summary>
    public static readonly Guid SyntheticMatterId =
        Guid.Parse("da116923-d65a-f111-a825-3833c5d9bcb1");

    /// <summary>
    /// Synthetic <c>sprk_project</c> row created in Spaarke Dev during Wave D7
    /// (task 036). Project name: "Synthetic Test Project — Insights r2 D7". Used
    /// to anchor the <see cref="ProjectLiveFactResolver"/> dispatch path
    /// (subject = <c>project:27845394-8e5f-f111-a825-70a8a59455f4</c>).
    /// </summary>
    public static readonly Guid SyntheticProjectId =
        Guid.Parse("27845394-8e5f-f111-a825-70a8a59455f4");

    /// <summary>
    /// Synthetic <c>sprk_invoice</c> row created in Spaarke Dev during Wave D7
    /// (task 036). Invoice number: "INV-SYNTH-D7-2026-001". Used to anchor the
    /// <see cref="InvoiceLiveFactResolver"/> dispatch path
    /// (subject = <c>invoice:05c8ef8d-8e5f-f111-a825-70a8a59455f4</c>).
    /// </summary>
    public static readonly Guid SyntheticInvoiceId =
        Guid.Parse("05c8ef8d-8e5f-f111-a825-70a8a59455f4");

    /// <summary>
    /// Returns the canonical subject string for the synthetic matter
    /// (<c>matter:{guid}</c>) for r2 D7 tests.
    /// </summary>
    public static string SyntheticMatterSubject =>
        $"matter:{SyntheticMatterId}";

    /// <summary>
    /// Returns the canonical subject string for the synthetic project
    /// (<c>project:{guid}</c>) for r2 D7 tests.
    /// </summary>
    public static string SyntheticProjectSubject =>
        $"project:{SyntheticProjectId}";

    /// <summary>
    /// Returns the canonical subject string for the synthetic invoice
    /// (<c>invoice:{guid}</c>) for r2 D7 tests.
    /// </summary>
    public static string SyntheticInvoiceSubject =>
        $"invoice:{SyntheticInvoiceId}";

    // ----- Fixture descriptors --------------------------------------------------

    /// <summary>
    /// Single fixture descriptor binding a synthetic document to its expected
    /// classification + entity context. <see cref="EntityType"/> values:
    /// <c>"matter"</c>, <c>"project"</c>, <c>"invoice"</c>. <see cref="ExpectedDocumentTypeCode"/>
    /// is the (area, type) pair the Layer 1 prompt should emit when classifying
    /// this fixture; <c>null</c> means the fixture is uncovered (the gate-fail or
    /// fallback path is expected).
    /// </summary>
    public sealed record FixtureDescriptor(
        string Path,
        string PracticeAreaCode,
        string EntityType,
        string? ExpectedDocumentTypeCode,
        string Description)
    {
        /// <summary>Absolute path resolved at runtime against
        /// <see cref="AppContext.BaseDirectory"/>.</summary>
        public string AbsolutePath => System.IO.Path.Combine(
            AppContext.BaseDirectory,
            Path.Replace('/', System.IO.Path.DirectorySeparatorChar));
    }

    // ----- CTRNS × matter (3) ---------------------------------------------------

    public static readonly FixtureDescriptor CtrnsMatterClosingStatement = new(
        Path: $"{FixtureRoot}/CTRNS/matter-closing-statement-1.txt",
        PracticeAreaCode: "CTRNS",
        EntityType: "matter",
        ExpectedDocumentTypeCode: "CTRNS_CLOSING_STATEMENT",
        Description: "Verdant Solar / Pelican Bay APA closing statement — exercises " +
                     "the CTRNS_CLOSING_STATEMENT Layer 2 extraction path " +
                     "(closing_date, parties, purchase_price, financing_terms).");

    public static readonly FixtureDescriptor CtrnsMatterAssetPurchaseAgreement = new(
        Path: $"{FixtureRoot}/CTRNS/matter-asset-purchase-agreement-1.txt",
        PracticeAreaCode: "CTRNS",
        EntityType: "matter",
        ExpectedDocumentTypeCode: "CTRNS_ASSET_PURCHASE_AGREEMENT",
        Description: "Sterling Arc / Crescent Delta APA — exercises the " +
                     "CTRNS_ASSET_PURCHASE_AGREEMENT Layer 2 extraction path " +
                     "(deal structure, purchase price, parties, indemnity caps).");

    public static readonly FixtureDescriptor GateFailCtrnsNda = new(
        Path: $"{FixtureRoot}/CTRNS/matter-nda-gate-fail-1.txt",
        PracticeAreaCode: "CTRNS",
        EntityType: "matter",
        ExpectedDocumentTypeCode: "CTRNS_NDA",
        Description: "Lumen Cascade / Brightwater Mutual NDA — exercises the " +
                     "CTRNS × CTRNS_NDA GATE-FAIL path (sprk_layer2actioncode = NULL " +
                     "on the matrix row; Layer 1 classifies as NDA, no Layer 2 runs).");

    // ----- IPPAT × matter (3) ---------------------------------------------------

    public static readonly FixtureDescriptor IppatMatterPatentApplication = new(
        Path: $"{FixtureRoot}/IPPAT/matter-patent-application-1.txt",
        PracticeAreaCode: "IPPAT",
        EntityType: "matter",
        ExpectedDocumentTypeCode: "IPPAT_PATENT_APPLICATION",
        Description: "Helios Quantum Compute thermal-management patent application — " +
                     "exercises the IPPAT_PATENT_APPLICATION Layer 2 extraction path " +
                     "(application_number, filing_date, inventors, claims, priority).");

    public static readonly FixtureDescriptor IppatMatterOfficeAction = new(
        Path: $"{FixtureRoot}/IPPAT/matter-office-action-1.txt",
        PracticeAreaCode: "IPPAT",
        EntityType: "matter",
        ExpectedDocumentTypeCode: "IPPAT_OFFICE_ACTION",
        Description: "Tessera Photonic Networks non-final Office Action — exercises " +
                     "the IPPAT_OFFICE_ACTION Layer 2 extraction path " +
                     "(application_number, mailing_date, response_due_date, examiner, " +
                     "rejection_types, cited_references).");

    public static readonly FixtureDescriptor IppatMatterResponseToOfficeAction = new(
        Path: $"{FixtureRoot}/IPPAT/matter-response-to-office-action-1.txt",
        PracticeAreaCode: "IPPAT",
        EntityType: "matter",
        ExpectedDocumentTypeCode: "IPPAT_RESPONSE_TO_OFFICE_ACTION",
        Description: "Response to the Tessera Office Action (medium-priority Layer 2 " +
                     "in seed taxonomy). Counter-amendment + § 103 / § 112 arguments.");

    // ----- BNKF × matter (3) ----------------------------------------------------

    public static readonly FixtureDescriptor BnkfMatterLoanAgreement = new(
        Path: $"{FixtureRoot}/BNKF/matter-loan-agreement-1.txt",
        PracticeAreaCode: "BNKF",
        EntityType: "matter",
        ExpectedDocumentTypeCode: "BNKF_LOAN_AGREEMENT",
        Description: "Mariner Logistics senior secured credit + security agreement — " +
                     "exercises the BNKF_LOAN_AGREEMENT Layer 2 extraction path " +
                     "(borrower, lender, principal_amount, interest_rate, maturity, " +
                     "security_description, covenants).");

    public static readonly FixtureDescriptor BnkfMatterSecurityAgreement = new(
        Path: $"{FixtureRoot}/BNKF/matter-security-agreement-1.txt",
        PracticeAreaCode: "BNKF",
        EntityType: "matter",
        ExpectedDocumentTypeCode: "BNKF_SECURITY_AGREEMENT",
        Description: "Mariner Logistics standalone security agreement (medium-priority " +
                     "Layer 2 in seed taxonomy). Grant of security interest, perfection, " +
                     "remedies upon default.");

    public static readonly FixtureDescriptor BnkfMatterPayoffLetter = new(
        Path: $"{FixtureRoot}/BNKF/matter-payoff-letter-1.txt",
        PracticeAreaCode: "BNKF",
        EntityType: "matter",
        ExpectedDocumentTypeCode: "BNKF_PAYOFF_LETTER",
        Description: "Cinder Peak Manufacturing payoff letter (medium-priority Layer 2 " +
                     "in seed taxonomy). Payoff amount, per-diem, wire instructions, " +
                     "release conditions.");

    // ----- CTRNS × project (3) --------------------------------------------------

    public static readonly FixtureDescriptor CtrnsProjectStatementOfWork = new(
        Path: $"{FixtureRoot}/CTRNS/project-statement-of-work-1.txt",
        PracticeAreaCode: "CTRNS",
        EntityType: "project",
        ExpectedDocumentTypeCode: null,
        Description: "Project SOW for the Verdant Solar APA matter — exercises the " +
                     "project:{guid} subject path. Currently uncovered by the seed " +
                     "taxonomy (no PROJECT_SOW document type); fallback path expected.");

    public static readonly FixtureDescriptor CtrnsProjectStatusReport = new(
        Path: $"{FixtureRoot}/CTRNS/project-status-report-1.txt",
        PracticeAreaCode: "CTRNS",
        EntityType: "project",
        ExpectedDocumentTypeCode: null,
        Description: "Weekly project status report for the Verdant Solar APA matter — " +
                     "exercises the project:{guid} subject path with operational/budget " +
                     "telemetry content. Fallback path expected (no PROJECT_STATUS doc type).");

    public static readonly FixtureDescriptor CtrnsProjectClosureMemo = new(
        Path: $"{FixtureRoot}/CTRNS/project-closure-memo-1.txt",
        PracticeAreaCode: "CTRNS",
        EntityType: "project",
        ExpectedDocumentTypeCode: null,
        Description: "Project closure memo for the Verdant Solar APA matter — outcomes " +
                     "vs plan + lessons learned + carry-forward. Fallback path expected.");

    // ----- IPPAT × project (3) --------------------------------------------------

    public static readonly FixtureDescriptor IppatProjectProsecutionPlan = new(
        Path: $"{FixtureRoot}/IPPAT/project-prosecution-plan-1.txt",
        PracticeAreaCode: "IPPAT",
        EntityType: "project",
        ExpectedDocumentTypeCode: null,
        Description: "Multi-year patent prosecution project plan for the Helios Quantum " +
                     "Compute family — exercises the project:{guid} subject path with " +
                     "portfolio strategy content. Fallback path expected.");

    public static readonly FixtureDescriptor IppatProjectDocketSummary = new(
        Path: $"{FixtureRoot}/IPPAT/project-docket-summary-1.txt",
        PracticeAreaCode: "IPPAT",
        EntityType: "project",
        ExpectedDocumentTypeCode: null,
        Description: "Quarterly patent docket summary for the Tessera Photonic Networks " +
                     "portfolio — exercises the project:{guid} subject path with " +
                     "portfolio-level reporting content. Fallback path expected.");

    public static readonly FixtureDescriptor IppatProjectStatusReport = new(
        Path: $"{FixtureRoot}/IPPAT/project-status-report-1.txt",
        PracticeAreaCode: "IPPAT",
        EntityType: "project",
        ExpectedDocumentTypeCode: null,
        Description: "Weekly status report for the Helios prosecution project. Status " +
                     "AMBER + budget telemetry. Fallback path expected.");

    // ----- BNKF × project (3) ---------------------------------------------------

    public static readonly FixtureDescriptor BnkfProjectFinancingEngagementLetter = new(
        Path: $"{FixtureRoot}/BNKF/project-financing-engagement-letter-1.txt",
        PracticeAreaCode: "BNKF",
        EntityType: "project",
        ExpectedDocumentTypeCode: null,
        Description: "Engagement letter for the Mariner Logistics refinancing project — " +
                     "exercises the project:{guid} subject path with engagement-letter " +
                     "content. Fallback path expected.");

    public static readonly FixtureDescriptor BnkfProjectClosingChecklist = new(
        Path: $"{FixtureRoot}/BNKF/project-closing-checklist-1.txt",
        PracticeAreaCode: "BNKF",
        EntityType: "project",
        ExpectedDocumentTypeCode: null,
        Description: "Closing checklist for the Mariner refinancing project — full " +
                     "deliverables index. Fallback path expected.");

    public static readonly FixtureDescriptor BnkfProjectMonthlyReport = new(
        Path: $"{FixtureRoot}/BNKF/project-monthly-report-1.txt",
        PracticeAreaCode: "BNKF",
        EntityType: "project",
        ExpectedDocumentTypeCode: null,
        Description: "March 2026 monthly post-closing perfection report — Status AMBER " +
                     "on two open DACA items. Fallback path expected.");

    // ----- Uncovered (routing fallback test) ------------------------------------

    public static readonly FixtureDescriptor UncoveredIppatTradeSecrets = new(
        Path: $"{FixtureRoot}/_uncovered/ippat-trade-secrets-policy-1.txt",
        PracticeAreaCode: "IPPAT",
        EntityType: "matter",
        ExpectedDocumentTypeCode: null,
        Description: "IPPAT-adjacent trade-secrets policy document — intentionally uncovered " +
                     "by the seed taxonomy. Exercises Wave D4 / task 033 routing fallback: " +
                     "Layer 1 should emit unknown_pair_for_practice_area; no Layer 2.");

    // ----- Collections for MemberData -------------------------------------------

    /// <summary>All matter-subject fixtures across the three practice areas (9).</summary>
    public static IReadOnlyList<FixtureDescriptor> AllMatterFixtures { get; } = new[]
    {
        CtrnsMatterClosingStatement,
        CtrnsMatterAssetPurchaseAgreement,
        GateFailCtrnsNda,
        IppatMatterPatentApplication,
        IppatMatterOfficeAction,
        IppatMatterResponseToOfficeAction,
        BnkfMatterLoanAgreement,
        BnkfMatterSecurityAgreement,
        BnkfMatterPayoffLetter
    };

    /// <summary>All project-subject fixtures across the three practice areas (9).</summary>
    public static IReadOnlyList<FixtureDescriptor> AllProjectFixtures { get; } = new[]
    {
        CtrnsProjectStatementOfWork,
        CtrnsProjectStatusReport,
        CtrnsProjectClosureMemo,
        IppatProjectProsecutionPlan,
        IppatProjectDocketSummary,
        IppatProjectStatusReport,
        BnkfProjectFinancingEngagementLetter,
        BnkfProjectClosingChecklist,
        BnkfProjectMonthlyReport
    };

    /// <summary>Routing-fallback fixtures (Wave D4 / task 033 coverage).</summary>
    public static IReadOnlyList<FixtureDescriptor> UncoveredFixtures { get; } = new[]
    {
        UncoveredIppatTradeSecrets
    };

    /// <summary>All fixtures (matter + project + uncovered) = 19 total.</summary>
    public static IReadOnlyList<FixtureDescriptor> AllFixtures { get; } =
        AllMatterFixtures
            .Concat(AllProjectFixtures)
            .Concat(UncoveredFixtures)
            .ToArray();

    /// <summary>
    /// xunit MemberData adapter — yields one row per fixture descriptor for
    /// <c>[Theory] [MemberData(nameof(SyntheticDocuments.AllFixturesData))]</c>.
    /// </summary>
    public static IEnumerable<object[]> AllFixturesData =>
        AllFixtures.Select(f => new object[] { f });

    /// <summary>
    /// xunit MemberData adapter for the matter-only subset.
    /// </summary>
    public static IEnumerable<object[]> AllMatterFixturesData =>
        AllMatterFixtures.Select(f => new object[] { f });

    /// <summary>
    /// xunit MemberData adapter for the project-only subset.
    /// </summary>
    public static IEnumerable<object[]> AllProjectFixturesData =>
        AllProjectFixtures.Select(f => new object[] { f });

    // ----- Helpers --------------------------------------------------------------

    /// <summary>
    /// Reads a fixture's text content from disk. Throws
    /// <see cref="FileNotFoundException"/> when the fixture is missing — this is
    /// the signal that the csproj <c>Content</c> glob did not pick up the file
    /// (typical on a renamed fixture path).
    /// </summary>
    public static string ReadFixture(FixtureDescriptor descriptor)
    {
        if (descriptor is null)
        {
            throw new ArgumentNullException(nameof(descriptor));
        }
        return ReadFixture(descriptor.Path);
    }

    /// <summary>
    /// Reads a fixture's text content from disk by relative path
    /// (e.g., <c>"Insights/fixtures/r2/CTRNS/matter-closing-statement-1.txt"</c>).
    /// </summary>
    public static string ReadFixture(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new ArgumentException("relativePath required", nameof(relativePath));
        }

        var absolutePath = System.IO.Path.Combine(
            AppContext.BaseDirectory,
            relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));

        if (!File.Exists(absolutePath))
        {
            throw new FileNotFoundException(
                $"Synthetic fixture not found at {absolutePath}. Check the " +
                $"Sprk.Bff.Api.Tests.csproj <Content> glob includes " +
                $"tests/Insights/fixtures/r2/**/*.txt.",
                absolutePath);
        }

        return File.ReadAllText(absolutePath);
    }
}
