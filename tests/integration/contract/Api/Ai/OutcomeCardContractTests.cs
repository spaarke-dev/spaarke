using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Ai;

/// <summary>
/// Contract test for <c>OutcomeCard v1</c> (spaarke-ai-architecture-redesign-r2 task 011,
/// spec FR-A0-02, design D-F2). Locks the disposition-level shape the Completion Engine
/// (FR-A1-06/07) and Compose r2 (FR-05 create-on-save card, FR-28 push/save completion) build
/// against.
/// </summary>
/// <remarks>
/// <para>
/// SELF-CONTAINED by design: it constructs an OutcomeCard through the guard factory, serializes it
/// through the shared wire options, deserializes it back (proving the round-trip a producer→consumer
/// pair performs), and asserts (a) the wire shape, (b) the audience split, (c) next-step chips,
/// (d) job-aware AND single-shot completion hosting, (e) tolerant-reader forward-compat, and
/// (f) the two hard invariants — server-composed link + store-before-render — via their NEGATIVE
/// controls. No DI, no <c>WebApplicationFactory</c>, no HTTP: this is a pure contract-shape anchor
/// (ADR-038 — no <c>Mock&lt;HttpMessageHandler&gt;</c>, no DI-registration assertion).
/// </para>
/// <para>
/// The <c>ledgerOutputKey</c> stands in for a stored <c>SessionOutput.Key</c> (<c>{bindingId}@t{n}</c>);
/// the server-composed link stands in for a <c>HandoffUrlBuilder</c> output. The producer wiring that
/// supplies those two from real internal types lives outside the PublicContracts facade (described in
/// the task report), so this test needs neither.
/// </para>
/// </remarks>
public class OutcomeCardContractTests
{
    private const string StoredLedgerKey = "binding-abc@t3";
    private const string ServerUrl =
        "https://contoso.crm.dynamics.com/main.aspx?pagetype=entityrecord&etn=sprk_matter&id=491b1efe-e562-f111-ab0c-000d3a4d8152";

    private static OutcomeCard BuildRepresentativeCard() =>
        OutcomeCard.ForStoredOutcome(
            ledgerOutputKey: StoredLedgerKey,
            status: OutcomeStatus.Succeeded,
            summary: new OutcomeSummary(
                UserFacing: "Created the matter \"Smith v. Jones\".",
                Internal: "sprk_matter row upserted; tell the user the record is ready and offer analysis."),
            link: OutcomeCardLink.ServerComposed(ServerUrl, "Open the matter", kind: "record"),
            nextSteps: new[]
            {
                new NextStepChip("Analyze the document", "invoke_capability"),
                new NextStepChip("Open the workspace", "navigate", TargetUrl: ServerUrl),
            },
            completion: OutcomeCompletion.SingleShot(),
            traceRef: "trace-7f3a");

    // =====================================================================
    // Shape + version
    // =====================================================================

    [Fact]
    public void Serialize_RepresentativeCard_CarriesVersionedShapeWithAllContractFields()
    {
        var card = BuildRepresentativeCard();

        using var doc = JsonDocument.Parse(card.ToJson());
        var root = doc.RootElement;

        root.GetProperty("version").GetInt32().Should().Be(OutcomeCard.SchemaVersion,
            "every A0 contract carries an explicit version field for tolerant, additive evolution");
        root.GetProperty("ledgerOutputKey").GetString().Should().Be(StoredLedgerKey,
            "the card renders a STORED ledger output — the addressable {bindingId}@t{n} key must be on the wire");
        root.GetProperty("status").GetString().Should().Be("succeeded");
        root.GetProperty("traceRef").GetString().Should().Be("trace-7f3a",
            "the card links to its TraceEvent v1 record (task 013)");

        // Server-composed link (from HandoffUrlBuilder) — url + label + kind, no provenance leak.
        var link = root.GetProperty("link");
        link.GetProperty("url").GetString().Should().Be(ServerUrl);
        link.GetProperty("label").GetString().Should().Be("Open the matter");
        link.GetProperty("kind").GetString().Should().Be("record");
        link.TryGetProperty("isServerComposed", out _).Should().BeFalse(
            "link provenance is a construction-time guard, not part of the wire contract");
    }

    // =====================================================================
    // Audience split (formalizes ToolResult UserSummary vs Summary)
    // =====================================================================

    [Fact]
    public void Serialize_Summary_CarriesUserFacingAndInternalAsDistinctFields()
    {
        var card = BuildRepresentativeCard();

        using var doc = JsonDocument.Parse(card.ToJson());
        var summary = doc.RootElement.GetProperty("summary");

        summary.GetProperty("userFacing").GetString().Should().Be("Created the matter \"Smith v. Jones\".");
        summary.GetProperty("internal").GetString().Should().Contain("upserted",
            "the internal/model-facing detail is a distinct field — the audience split from ToolResult.cs:326");
    }

    [Fact]
    public void Render_Card_ExposesUserFacingSummaryButNeverTheInternalDetail()
    {
        var card = BuildRepresentativeCard();

        var view = card.Render();

        view.UserSummary.Should().Be("Created the matter \"Smith v. Jones\".");
        view.LinkLabel.Should().Be("Open the matter");
        view.NextStepLabels.Should().ContainInOrder("Analyze the document", "Open the workspace");

        // The audience split is preserved AT RENDER: the internal detail is not reachable from the
        // user-facing projection at all (OutcomeCardView carries no internal field).
        typeof(OutcomeCardView).GetProperties().Select(p => p.Name)
            .Should().NotContain("Internal",
                "the user-facing projection must never carry the internal-audience detail");
    }

    // =====================================================================
    // Next-step chips
    // =====================================================================

    [Fact]
    public void Serialize_NextStepChips_PreserveOrderKindAndOptionalTarget()
    {
        var card = BuildRepresentativeCard();

        using var doc = JsonDocument.Parse(card.ToJson());
        var chips = doc.RootElement.GetProperty("nextSteps");

        chips.GetArrayLength().Should().Be(2);
        chips[0].GetProperty("label").GetString().Should().Be("Analyze the document");
        chips[0].GetProperty("actionKind").GetString().Should().Be("invoke_capability");
        chips[0].TryGetProperty("targetUrl", out _).Should().BeFalse(
            "a null optional field is omitted (WhenWritingNull) — additive tolerant wire shape");
        chips[1].GetProperty("actionKind").GetString().Should().Be("navigate");
        chips[1].GetProperty("targetUrl").GetString().Should().Be(ServerUrl);
    }

    // =====================================================================
    // Completion hosting — single-shot AND job-aware
    // =====================================================================

    [Fact]
    public void JobAwareCompletion_RoundTrips_WithConsumerDeclaredOrderedSteps()
    {
        // Compose r2's create-on-save declares: container → record → profile-analysis → indexing.
        var card = OutcomeCard.ForStoredOutcome(
            ledgerOutputKey: StoredLedgerKey,
            status: OutcomeStatus.Partial,
            summary: new OutcomeSummary("Saving the document…"),
            completion: OutcomeCompletion.JobAware(new[]
            {
                new OutcomeStep("container", "Create container", "succeeded"),
                new OutcomeStep("record", "Create record", "succeeded"),
                new OutcomeStep("profile-analysis", "Analyze profile", "running"),
                new OutcomeStep("indexing", "Index for search", "pending"),
            }));

        var roundTripped = OutcomeCard.FromJson(card.ToJson());

        roundTripped.Status.Should().Be(OutcomeStatus.Partial);
        roundTripped.Completion!.Mode.Should().Be(OutcomeCompletionMode.JobAware);
        roundTripped.Completion.Steps.Select(s => s.Key)
            .Should().Equal(new[] { "container", "record", "profile-analysis", "indexing" },
                "the completion contract is generic over CONSUMER-declared ordered steps");
        roundTripped.Render().CompletionMode.Should().Be(OutcomeCompletionMode.JobAware);
    }

    [Fact]
    public void SingleShotCompletion_RoundTrips_WithNoSteps()
    {
        var card = BuildRepresentativeCard();

        var roundTripped = OutcomeCard.FromJson(card.ToJson());

        roundTripped.Completion!.Mode.Should().Be(OutcomeCompletionMode.SingleShot);
        roundTripped.Completion.Steps.Should().BeEmpty();
    }

    // =====================================================================
    // Tolerant reader (unknown field ignored — additive forward-compat)
    // =====================================================================

    [Fact]
    public void Deserialize_PayloadWithUnknownFutureField_IgnoresItAndReadsKnownFields()
    {
        var card = BuildRepresentativeCard();

        // Simulate a producer on a FUTURE additive revision that added a field this consumer
        // doesn't know. A tolerant reader must ignore it, not throw.
        using var doc = JsonDocument.Parse(card.ToJson());
        var withUnknown = new Dictionary<string, object?>();
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            withUnknown[prop.Name] = JsonSerializer.Deserialize<object>(prop.Value.GetRawText());
        }
        withUnknown["someFutureV2Field"] = new { nested = "value", count = 42 };
        var forwardCompatJson = JsonSerializer.Serialize(withUnknown, OutcomeCard.JsonOptions);

        var read = OutcomeCard.FromJson(forwardCompatJson);

        read.LedgerOutputKey.Should().Be(StoredLedgerKey,
            "unknown future fields are ignored; known fields still read (tolerant reader)");
        read.Summary.UserFacing.Should().Be("Created the matter \"Smith v. Jones\".");
        read.Link!.Url.Should().Be(ServerUrl);
    }

    // =====================================================================
    // NEGATIVE invariants — server-composed link + store-before-render
    // =====================================================================

    [Fact]
    public void ForStoredOutcome_WithModelClaimedLink_Throws()
    {
        var act = () => OutcomeCard.ForStoredOutcome(
            ledgerOutputKey: StoredLedgerKey,
            status: OutcomeStatus.Succeeded,
            summary: new OutcomeSummary("Created the matter."),
            // A URL the MODEL invented, not composed by HandoffUrlBuilder — must be refused.
            link: OutcomeCardLink.ModelClaimed("https://evil.example/pretend", "Open", "record"));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*server-composed*",
                "the link a user clicks must be server-composed, never model-invented");
    }

    [Fact]
    public void ForStoredOutcome_WithNoStoredLedgerKey_Throws()
    {
        var act = () => OutcomeCard.ForStoredOutcome(
            ledgerOutputKey: "  ",
            status: OutcomeStatus.Succeeded,
            summary: new OutcomeSummary("Created the matter."),
            link: OutcomeCardLink.ServerComposed(ServerUrl, "Open the matter", "record"));

        act.Should().Throw<ArgumentException>()
            .WithMessage("*store-before-render*",
                "an OutcomeCard must reference a STORED ledger output (ADR-040)");
    }

    [Fact]
    public void ServerComposedLink_IsAcceptedAndRoundTripsWithoutProvenanceLeak()
    {
        var card = BuildRepresentativeCard();

        var roundTripped = OutcomeCard.FromJson(card.ToJson());

        roundTripped.Link!.Url.Should().Be(ServerUrl);
        // Provenance defaults to false after deserialize (not on the wire) — the guard is a
        // production-time concern, and a link only enters a card through the accepting factory.
        roundTripped.Link.IsServerComposed.Should().BeFalse(
            "isServerComposed is not serialized; it guards production, not the wire shape");
    }
}
