using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Engine;
using Sprk.Bff.Api.Services.Communication.Engine.Rungs;
using Sprk.Bff.Api.Services.Communication.Models;
using Xunit;
using DataverseEntity = Microsoft.Xrm.Sdk.Entity;

namespace Sprk.Bff.Api.Tests.Seam.Communication;

/// <summary>
/// FR-D3 golden end-to-end regression (email-communication-intelligence-r2 task 032), ADR-038
/// <c>tests/integration/seam/Communication/**</c> KEEP path — <b>MAINTAIN-class</b>, survives /test-diet.
/// Drives the REAL association spine (real rungs → real <see cref="AssociationStatusMapper"/> → real
/// <see cref="AutoFileGate"/> reading the default core-writable set → <see cref="IncomingAssociationResolver"/>)
/// over the R1 UAT golden misfile identifiers (PAT-942665, PAT-942404, REAL-2026-123456.02, plus the
/// Invoice-10044725.pdf / #123456 collision trap) and asserts the round-1/2/2b/3 verdicts that R1's UAT fixes
/// established — so those fixes cannot silently regress. Only the Dataverse boundary is doubled (ADR-045: no
/// reaching into private rung internals). Expected outcomes are pinned in
/// <c>notes/fixtures/r1-golden-emails.md</c>.
///
/// <para>The FR-A3 external-reply self-association case is co-located in this KEEP path as
/// <c>ThreadSelfAssociationRegressionTests</c> (authored by task 015) — one golden suite, not two, so it is
/// not duplicated here.</para>
/// </summary>
public sealed class GoldenMisfileRegressionTests
{
    private static readonly Guid CommId = Guid.NewGuid();

    // The 7 core types + their catalog number field (task 001 / IdentifierReverseLookupRung roster).
    private static readonly (string Entity, string NumberField)[] CoreSeven =
    {
        ("sprk_matter", "sprk_matternumber"),
        ("sprk_project", "sprk_projectnumber"),
        ("sprk_invoice", "sprk_invoicenumber"),
        ("sprk_workassignment", "sprk_workassignmentnumber"),
        ("sprk_budget", "sprk_budgetnumber"),
        ("sprk_servicerequest", "sprk_servicerequestnumber"),
        ("sprk_reportcard", "sprk_reportcardnumber"),
    };

    /// <summary>Build the REAL spine (deterministic rungs + real mapper + real gate), fake only Dataverse.</summary>
    private static IncomingAssociationResolver BuildSpine(
        Mock<IDataverseService> dv, out List<Dictionary<string, object>> writes)
    {
        var captured = new List<Dictionary<string, object>>();
        writes = captured;
        dv.Setup(d => d.UpdateAsync("sprk_communication", CommId,
                It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Callback<string, Guid, Dictionary<string, object>, CancellationToken>(
                (_, _, f, _) => captured.Add(new Dictionary<string, object>(f)))
            .Returns(Task.CompletedTask);

        // Default AutoFileOptions: Enabled, Threshold 0.85, CoreWritableEntities = matter/project/service
        // request (round-3 core-only gate active — non-core never auto-files/writes).
        var options = new AutoFileOptions();
        var monitor = Mock.Of<IOptionsMonitor<AutoFileOptions>>(m => m.CurrentValue == options);
        var mapper = new AssociationStatusMapper(new AutoFileGate(monitor), NullLogger<AssociationStatusMapper>.Instance);

        var rungs = new IAssociationRung[]
        {
            new ExplicitReferenceRung(dv.Object),
            new IdentifierReverseLookupRung(dv.Object, NullLogger<IdentifierReverseLookupRung>.Instance),
            new ThreadContinuityRung(dv.Object),
            new ParticipantCorrelationRung(dv.Object),
        };
        return new IncomingAssociationResolver(rungs, dv.Object, dv.Object, mapper, Sprk.Bff.Api.Tests.TestInfrastructure.CoreAncestorResolverFixtures.Inert(), NullLogger<IncomingAssociationResolver>.Instance);
    }

    private static void SetupRoster(Mock<IDataverseService> dv)
    {
        var rows = CoreSeven.Select(t =>
        {
            var e = new DataverseEntity("sprk_recordtype_ref") { Id = Guid.NewGuid() };
            e["sprk_recordlogicalname"] = t.Entity;
            e["sprk_regardingrecordnumberfield"] = t.NumberField;
            return e;
        }).ToArray();
        dv.Setup(d => d.QueryAllRecordTypeRefsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(rows);

        // Default: every batched reverse lookup returns empty; individual tests add matches.
        dv.Setup(d => d.QueryRecordsByNumberFieldValuesAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<DataverseEntity>());
    }

    /// <summary>Stub the batched reverse lookup for (entity, numberField): when the value-set contains
    /// <paramref name="value"/>, return one record per id, each carrying numberField == value.</summary>
    private static void SetupMatch(Mock<IDataverseService> dv, string entity, string numberField, string value, params Guid[] ids) =>
        dv.Setup(d => d.QueryRecordsByNumberFieldValuesAsync(
                entity, numberField,
                It.Is<IReadOnlyCollection<string>>(vs => vs.Contains(value, StringComparer.OrdinalIgnoreCase)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ids.Select(id =>
            {
                var e = new DataverseEntity(entity) { Id = id };
                e[numberField] = value;
                return e;
            }).ToArray());

    /// <summary>A matched record carrying its number-field attribute (the rung reads it to re-associate).</summary>
    private static DataverseEntity NumberedRecord(string entity, Guid id, string numberField, string value)
    {
        var e = new DataverseEntity(entity) { Id = id };
        e[numberField] = value;
        return e;
    }

    private static NormalizedMessage Email(string? subject = null, string? body = null, string from = "sender@external-firm.com") =>
        new() { Direction = CommunicationDirection.Incoming, From = from, Subject = subject, BodyText = body };

    private static int Status(Dictionary<string, object> fields) => ((OptionSetValue)fields["sprk_associationstatus"]).Value;

    // ── Round 3 headline: two conflicting matters → Ambiguous (never a guessed single auto-file) ──────

    [Fact]
    public async Task ConflictingMatters_PAT942665_And_PAT942404_ResolvesAmbiguous_NoSingleAutoFile()
    {
        var dv = new Mock<IDataverseService>();
        SetupRoster(dv);
        var matterA = Guid.NewGuid();
        var matterB = Guid.NewGuid();
        // Both patent identifiers arrive in ONE batched matter query (all distinct token values). It returns
        // BOTH matters, each carrying its own number so the rung re-associates them to their tokens — two
        // matters on sprk_regardingmatter ≥ threshold, which the mapper resolves to Ambiguous, not a guess.
        dv.Setup(d => d.QueryRecordsByNumberFieldValuesAsync("sprk_matter", "sprk_matternumber",
                It.Is<IReadOnlyCollection<string>>(vs => vs.Contains("PAT-942665") && vs.Contains("PAT-942404")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                NumberedRecord("sprk_matter", matterA, "sprk_matternumber", "PAT-942665"),
                NumberedRecord("sprk_matter", matterB, "sprk_matternumber", "PAT-942404"),
            });
        var resolver = BuildSpine(dv, out var writes);

        await resolver.ResolveAsync(
            CommId, Email(subject: "RE: PAT-942665 / PAT-942404 status update"), new AssociationContext(), CancellationToken.None);

        var fields = writes.Should().ContainSingle().Subject;
        Status(fields).Should().Be(AssociationStatusCodes.Ambiguous,
            "two matters conflicting at auto-file strength must yield 'Needs your decision', never a guessed single matter");
        // Neither matter is written as THE regarding — the conflict is surfaced, not silently crowned.
        fields.Should().NotContainKey("sprk_regardingmatter");
    }

    // ── Round 1/2: a single clear core matter auto-files; the embedded digit-run does NOT collide ─────

    [Fact]
    public async Task SingleClearMatter_REAL2026_AutoFilesResolved_EmbeddedDigitsNeverQueried()
    {
        var dv = new Mock<IDataverseService>();
        SetupRoster(dv);
        var matterId = Guid.NewGuid();
        SetupMatch(dv, "sprk_matter", "sprk_matternumber", "REAL-2026-123456.02", matterId);
        // Trap: an invoice #123456 exists. If the inner digit-run "123456" were extracted (the round-1 bug),
        // it would collide with this invoice and misfile. The P1 guard must prevent that query entirely.
        SetupMatch(dv, "sprk_invoice", "sprk_invoicenumber", "123456", Guid.NewGuid());
        var resolver = BuildSpine(dv, out var writes);

        await resolver.ResolveAsync(
            CommId, Email(subject: "RE: REAL-2026-123456.02 closing docs", body: "Regarding REAL-2026-123456.02, attached."),
            new AssociationContext(), CancellationToken.None);

        var fields = writes.Should().ContainSingle().Subject;
        Status(fields).Should().Be(AssociationStatusCodes.Resolved, "one well-formed core matter auto-files");
        ((EntityReference)fields["sprk_regardingmatter"]).Id.Should().Be(matterId);
        fields.Should().NotContainKey("sprk_regardinginvoice", "the attached/embedded invoice must never auto-file");
        // The inner digit-runs "123456"/"2026" must never have been queried against ANY number field.
        dv.Verify(d => d.QueryRecordsByNumberFieldValuesAsync(
            It.IsAny<string>(), It.IsAny<string>(),
            It.Is<IReadOnlyCollection<string>>(vs => vs.Contains("123456") || vs.Contains("2026")),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Round 3: a contact match (no core record) is suggest-only — NEVER auto-filed (core-only gate) ──

    [Fact]
    public async Task ContactMatchOnly_NoCoreRecord_NotAutoFiled_ContactNotWrittenAsRegarding()
    {
        var dv = new Mock<IDataverseService>();
        SetupRoster(dv); // no identifier tokens match → no core record
        var contact = new DataverseEntity("contact") { Id = Guid.NewGuid() };
        dv.Setup(d => d.QueryContactByEmailAsync("ralph.schroeder@client.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(contact);
        var resolver = BuildSpine(dv, out var writes);

        await resolver.ResolveAsync(
            CommId, Email(subject: "Following up", body: "Please advise.", from: "ralph.schroeder@client.com"),
            new AssociationContext(), CancellationToken.None);

        var fields = writes.Should().ContainSingle().Subject;
        Status(fields).Should().NotBe(AssociationStatusCodes.Resolved,
            "a contact is non-core (round-3): it surfaces as Suggested for confirmation, never 'Filed automatically'");
        // Core-only write gate: the contact regarding is surfaced in provenance but NOT written as a lookup.
        fields.Should().NotContainKey("sprk_regardingperson");
    }
}
