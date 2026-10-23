using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.RecordCreation;
using Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.RecordCreation;

/// <summary>
/// field-mapping-server-write-path-r1 task 013, fix round 2 (ebilling-r1 review): the pre-write runner's contract
/// (<c>RecordCreationService.RunPreWriteRulesAsync</c>, step 5½) pinned from the rule's side. A PARTIAL of the pipeline
/// seam fixture, so these tests drive the same REAL service and doubles (the smallest possible edit to that file: the class
/// is <c>partial</c>); the invoice-family rule itself is not on this branch, so a fixture rule stands in for it.
/// </summary>
/// <remarks>
/// What each test pins, and the mutant that fails it (task 013 notes, "Fix round 2"): the rule is handed the exact item and
/// the user principal; a throwing rule is a 503 with nothing created; a cancelled token propagates; a not-found is the
/// uniform 404 (never 400); a coded refusal is a 400 with its code; the rule's own refusal object is carried by reference.
/// </remarks>
public sealed partial class RecordCreationServicePipelineSeamTests
{
    // =====================================================================================
    // Harness additions (task 013 fix round 2, UAC-r2 F-1): the stamp resolver's Access Permission reads, journaled
    // =====================================================================================

    /// <summary>Whether the stamp resolver's Access Permission reads and writes (I-16) are journaled as "inherit".</summary>
    private bool _journalInherit;

    /// <summary>
    /// The REAL <see cref="CoreAncestorResolver"/> (I-1 ancestors and I-16) over the stamp world, with the world's column probe
    /// (which has <c>sprk_accesspermission</c>, as the host's does). Its calls that concern the Access Permission column are
    /// journaled as "inherit" when <see cref="_journalInherit"/> is set, so I-16 is observable: before this, the resolver read
    /// the stamp world directly and a no-op I-16 changed nothing a test could see.
    /// </summary>
    private CoreAncestorResolver InheritResolver()
    {
        const string Column = "sprk_accesspermission";
        var entities = new Mock<IGenericEntityService>(MockBehavior.Strict);
        var world = _stamps.Service;
        entities
            .Setup(e => e.RetrieveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .Returns((string t, Guid id, string[] c, CancellationToken ct) =>
            {
                if (_journalInherit && c is not null && c.Contains(Column, StringComparer.OrdinalIgnoreCase))
                    _journal.Add("inherit");
                return world.RetrieveAsync(t, id, c!, ct);
            });
        entities
            .Setup(e => e.UpdateAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Returns((string t, Guid id, Dictionary<string, object> f, CancellationToken ct) =>
            {
                if (_journalInherit && f.ContainsKey(Column))
                    _journal.Add("inherit");
                return world.UpdateAsync(t, id, f, ct);
            });
        entities
            .Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .Returns((QueryExpression q, CancellationToken ct) =>
            {
                if (_journalInherit && q.ColumnSet.Columns.Contains(Column, StringComparer.OrdinalIgnoreCase))
                    _journal.Add("inherit");
                return world.RetrieveMultipleAsync(q, ct);
            });
        entities
            .Setup(e => e.RetrieveMultipleAsync(It.IsAny<FetchExpression>(), It.IsAny<CancellationToken>()))
            .Returns((FetchExpression q, CancellationToken ct) => world.RetrieveMultipleAsync(q, ct));
        return new CoreAncestorResolver(entities.Object, SecureChildShareWorld.ColumnProbe(), NullLogger<CoreAncestorResolver>.Instance);
    }

    // =====================================================================================
    // UAC-r2 F-1: I-16 runs, and runs between the I-1 re-stamp and the sharee mirror
    // =====================================================================================

    [Fact]
    public async Task CreateAsync_AnEventUnderARestrictedMatter_IsStoredRestricted_ByI16_BetweenTheRestampAndTheMirror()
    {
        _stamps.Set("sprk_matter", OrdinaryMatter, "sprk_accesspermission", new OptionSetValue(InheritedAccessPermission.Restricted));
        _journalInherit = true;

        var outcome = await Service().CreateAsync(EventRequest(OrdinaryMatter), AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        var (_, id, _) = _creates.Should().ContainSingle().Subject;
        _stamps.RowOf("sprk_event", id).GetAttributeValue<OptionSetValue>("sprk_accesspermission")?.Value
            .Should().Be(InheritedAccessPermission.Restricted, "I-16 sets the event to what the matter at the top of its filing holds");
        Journal().Should().ContainInOrder("create", "restamp", "inherit", "mirror");
    }

    private string EventUnder(Guid matter) => $"/sprk_matters({matter:D})";

    private RecordCreationRequest EventRequest(Guid matter) =>
        WebApiRequest("sprk_event", new { sprk_eventname = "Hearing", sprk_RegardingMatter_bind = EventUnder(matter) });

    /// <summary>Registers a fixture pre-write rule on the event table that sees the token; returns the rows it was asked about.</summary>
    private List<PreWriteRow> WithAsyncTestRule(Func<PreWriteRow, CancellationToken, Task<PreWriteRefusal?>> decide)
    {
        var asked = new List<PreWriteRow>();
        var rule = RecordTableRules.Get("sprk_event") with
        {
            PreWriteRules =
            [
                new PreWriteRule(new NamedRule("SeamTestRule", "A fixture rule standing in for the invoice-family guard."),
                    (row, ct) =>
                    {
                        _journal.Add("prewrite");
                        asked.Add(row);
                        return decide(row, ct);
                    }),
            ],
        };
        _tableRules = t => t == "sprk_event" ? rule : RecordTableRules.Find(t);
        return asked;
    }

    // =====================================================================================
    // 1. The rule gets the exact item and the caller
    // =====================================================================================

    [Fact]
    public async Task CreateAsync_APreWriteRule_IsAskedAboutTheExactItemWritten_AsTheCallersOwnClient()
    {
        var asked = WithAsyncTestRule((_, _) => Task.FromResult<PreWriteRefusal?>(null));

        var outcome = await Service().CreateAsync(EventRequest(OrdinaryMatter), AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        var row = asked.Should().ContainSingle().Subject;
        var (_, _, written) = _creates.Should().ContainSingle().Subject;

        row.Caller.Should().BeSameAs(_user, "the rule reads as the person creating the row, never as the application (ADR-028)");
        row.Table.Should().Be("sprk_event");

        // The item the rule saw IS the item the create wrote: its lookups (the filing the caller was AppendTo-checked on) ...
        row.Item.Lookups.Should().ContainSingle().Which.Should().Match<DataverseWriteItemMapper.MappedLookup>(
            l => l.RecordId == OrdinaryMatter && l.RelatedTable == "sprk_matter");
        written.Should().ContainKey("sprk_RegardingMatter@odata.bind");
        JsonSerializer.Serialize(written["sprk_RegardingMatter@odata.bind"]).Should().Contain(OrdinaryMatter.ToString("D"));

        // ... and its body: every property of the item's body reached the app-only create unchanged.
        row.Item.Columns.Should().Contain("sprk_eventname");
        using var body = JsonDocument.Parse(row.Item.JsonBody);
        var properties = body.RootElement.EnumerateObject().ToList();
        properties.Should().NotBeEmpty("an empty item would pass a rule nothing to decide on");
        foreach (var property in properties)
        {
            written.Should().ContainKey(property.Name);
            JsonSerializer.Serialize(written[property.Name]).Should().Be(property.Value.GetRawText(), property.Name);
        }

        JsonSerializer.Serialize(written["sprk_eventname"]).Should().Contain("Hearing");
    }

    // =====================================================================================
    // 2. A throwing rule
    // =====================================================================================

    [Fact]
    public async Task CreateAsync_AThrowingPreWriteRule_Is503CheckFailed_AndCreatesNothing()
    {
        WithAsyncTestRule((_, _) => throw new InvalidOperationException("rule-internal-secret: sprk_invoice index offline"));

        var outcome = await Service().CreateAsync(EventRequest(OrdinaryMatter), AsCaller(), CancellationToken.None);

        outcome.Refusal!.Kind.Should().Be(RecordCreationRefusalKind.Unavailable);
        outcome.Refusal.Code.Should().Be(RecordCreationRefusal.CheckFailedCode);
        var problem = Problem(outcome, "sprk_event");
        problem.Status.Should().Be(StatusCodes.Status503ServiceUnavailable);
        problem.Extensions["errorCode"].Should().Be(RecordCreationRefusal.CheckFailedCode);
        JsonSerializer.Serialize(problem).Should().NotContain("rule-internal-secret", "a fault's text stays in the log (ADR-019)");

        _createAttempts.Should().BeEmpty("nothing is created when a check faulted (NFR-01)");
        _creates.Should().BeEmpty();
        Journal().Should().NotContain(["owner", "create", "restamp", "mirror", "membership"]);
        outcome.Steps.Select(s => s.Step).Should().NotContain([RecordCreationStep.Create, RecordCreationStep.PostCreate]);
        _publisher.Published.Should().BeEmpty("there is no post-create step");
    }

    // =====================================================================================
    // 3. A cancelled token while the rule runs
    // =====================================================================================

    [Fact]
    public async Task CreateAsync_WhenTheTokenIsCancelledWhileAPreWriteRuleRuns_PropagatesTheCancellation_AndCreatesNothing()
    {
        using var cts = new CancellationTokenSource();
        WithAsyncTestRule((_, ct) =>
        {
            cts.Cancel();
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<PreWriteRefusal?>(null);
        });

        var create = () => Service().CreateAsync(EventRequest(OrdinaryMatter), AsCaller(), cts.Token);

        await create.Should().ThrowAsync<OperationCanceledException>(
            "a caller's cancel is the caller leaving, not a failed check to be answered 503");
        _createAttempts.Should().BeEmpty();
        _creates.Should().BeEmpty();
        Journal().Should().NotContain(["owner", "create"]);
    }

    // =====================================================================================
    // 4. Arm order: a not-found is the uniform 404, never the generic 400
    // =====================================================================================

    [Fact]
    public async Task CreateAsync_APreWriteRuleNotFound_RendersTheSameBodyAsAMissingParent_AndNot400()
    {
        // The reference answer: a missing parent, with the table's real rules (no fixture rule registered yet).
        _scripted.Missing.Add(MissingMatter);
        var missing = await Service().CreateAsync(EventRequest(MissingMatter), AsCaller(), CancellationToken.None);
        missing.Refusal!.Kind.Should().Be(RecordCreationRefusalKind.NotFoundOrDenied);
        var asked = WithAsyncTestRule((_, _) => Task.FromResult<PreWriteRefusal?>(PreWriteRefusal.NotFound()));

        var ruleSaid = await Service().CreateAsync(EventRequest(OrdinaryMatter), AsCaller(), CancellationToken.None);

        asked.Should().ContainSingle("the rule ran and answered not-found");
        ruleSaid.Refusal!.Kind.Should().Be(RecordCreationRefusalKind.NotFoundOrDenied);
        ruleSaid.Refusal.PreWrite!.ParentUnavailable.Should().BeTrue();
        var ruleProblem = Problem(ruleSaid, "sprk_event");
        ruleProblem.Status.Should().Be(StatusCodes.Status404NotFound).And.NotBe(StatusCodes.Status400BadRequest);
        ruleProblem.Extensions["errorCode"].Should().Be(RecordCreationRefusal.NotFoundCode);
        JsonSerializer.Serialize(ruleProblem).Should().Be(
            JsonSerializer.Serialize(Problem(missing, "sprk_event")), "a rule's not-found is indistinguishable from a missing parent (NFR-01)");
        _creates.Should().BeEmpty();
    }

    // =====================================================================================
    // 5. A Refused with a code is a 400 carrying that code
    // =====================================================================================

    [Fact]
    public async Task CreateAsync_APreWriteRuleRefusedWithACode_Is400_CarryingThatCodeAndDetail()
    {
        var refused = PreWriteRefusal.Refused("test.names_two_core_records", "The row names two core records; it must name exactly one.");
        WithAsyncTestRule((_, _) => Task.FromResult<PreWriteRefusal?>(refused));

        var outcome = await Service().CreateAsync(EventRequest(OrdinaryMatter), AsCaller(), CancellationToken.None);

        outcome.Refusal!.Kind.Should().Be(RecordCreationRefusalKind.Invalid);
        var problem = Problem(outcome, "sprk_event");
        problem.Status.Should().Be(StatusCodes.Status400BadRequest, "today's behaviour; the 404/500 split by code is reconciliation work");
        problem.Extensions["errorCode"].Should().Be("test.names_two_core_records");
        problem.Extensions["reasonCode"].Should().Be("test.names_two_core_records");
        problem.Detail.Should().Be("The row names two core records; it must name exactly one.");
        _creates.Should().BeEmpty();
    }

    // =====================================================================================
    // 6. The rule's own refusal object is carried by reference
    // =====================================================================================

    [Theory]
    [InlineData("refused")]
    [InlineData("not-found")]
    [InlineData("failed")]
    public async Task CreateAsync_APreWriteRuleRefusal_IsCarriedByReference_NotCopied(string flavour)
    {
        var refusal = flavour switch
        {
            "refused" => PreWriteRefusal.Refused("test.prewrite_refused", "The test rule refuses this row."),
            "not-found" => PreWriteRefusal.NotFound(),
            _ => PreWriteRefusal.Failed(DataverseUserResponse.Fail(503, DataverseUserClientErrorCodes.RateLimited, "upstream text")),
        };
        WithAsyncTestRule((_, _) => Task.FromResult<PreWriteRefusal?>(refusal));

        var outcome = await Service().CreateAsync(EventRequest(OrdinaryMatter), AsCaller(), CancellationToken.None);

        outcome.Refusal!.PreWrite.Should().BeSameAs(refusal, "ebilling-r1 correction (c): the rule's own object, not a copy of it");
        if (flavour == "refused")
        {
            outcome.Refusal.Code.Should().Be(refusal.Code);
            outcome.Refusal.Detail.Should().Be(refusal.Detail);
        }

        _creates.Should().BeEmpty();
    }
}
