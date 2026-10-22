using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Sprk.Bff.Api.Services.RecordCreation;
using Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.RecordCreation;

/// <summary>
/// field-mapping-server-write-path-r1 task 018b-1s (decisions.md, "018b-1 inventory: answers to G1-G7") — two members the
/// Office adapter reads instead of parsing text or reading the row back:
/// <list type="bullet">
/// <item><see cref="RecordCreationOutcome.WrittenColumns"/> (G1): the keys of the body the one app-only create sent, as sent.</item>
/// <item><see cref="RecordCreationRefusal.RefusedColumn"/> (G5): the lookup column a step-5 not-found refused.</item>
/// </list>
/// G2 (the WhoAmI failure on a <see cref="RecordCreationRefusalKind.PrincipalUnresolved"/> refusal) is in
/// <c>RecordCreationServicePipelineSeamTests.PrincipalFailure.cs</c>, where the caller's WhoAmI can be scripted.
/// </summary>
/// <remarks>
/// The REAL <see cref="RecordCreationService"/> over <see cref="CreateWorld"/> (the REAL owner resolver and caller checks
/// over the caller's scripted Dataverse). Nothing here changes a refusal's kind, code, detail or HTTP status: each refusal
/// test pins them beside the new member.
/// </remarks>
[Trait("status", "new")]
public sealed class RecordCreationServiceOutcomeSeamTests
{
    private static readonly Guid MatterTypeRow = Guid.Parse("a018b100-0000-4000-8000-000000000001");
    private static readonly Guid PracticeAreaRow = Guid.Parse("a018b100-0000-4000-8000-000000000002");
    private const string MatterTypeBind = "sprk_MatterType@odata.bind";
    private const string PracticeAreaBind = "sprk_PracticeArea@odata.bind";

    private readonly CreateWorld _world = new();

    private Task<RecordCreationRequest> MatterAsync(bool withType = true) => _world.MappedAsync("sprk_matter",
        withType
            ? new
            {
                sprk_mattername = "Acme v. Widget",
                sprk_mattertype = CreateWorld.Lookup("sprk_mattertype_ref", MatterTypeRow),
                sprk_practicearea = CreateWorld.Lookup("sprk_practicearea_ref", PracticeAreaRow),
            }
            : (object)new { sprk_mattername = "Acme v. Widget" });

    private Task<RecordCreationRequest> EventUnder(Guid matter) => _world.MappedAsync("sprk_event", new
    {
        sprk_eventname = "Hearing",
        sprk_regardingmatter = CreateWorld.Lookup("sprk_matter", matter),
    });

    private static string BodyOf(RecordCreationOutcome refused, string table) =>
        JsonSerializer.Serialize(RecordCreationProblemMap.ToProblemDetails(refused.Refusal!, table, refused.CorrelationId));

    // =====================================================================================
    // G1 - WrittenColumns
    // =====================================================================================

    [Fact]
    public async Task WrittenColumns_AreTheKeysOfTheBodyTheCreateSent_AMatterTypeBindAsItsBindKey_NotExpanded()
    {
        var outcome = await _world.CreateAsync(await MatterAsync());

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        var sent = _world.Creates.Should().ContainSingle().Subject.Fields;
        outcome.WrittenColumns.Should().BeEquivalentTo(sent.Keys, "the final row, key for key, as the create sent it");
        outcome.WrittenColumns.Should().Contain(["sprk_mattername", MatterTypeBind, PracticeAreaBind, "ownerid@odata.bind"]);
        outcome.WrittenColumns.Should().NotContain("sprk_mattertype", "a bind is reported as written, not expanded to its logical column");
    }

    [Fact]
    public async Task WrittenColumns_OfANameOnlyMatter_DoNotNameTheMatterType()
    {
        var outcome = await _world.CreateAsync(await MatterAsync(withType: false));

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        outcome.WrittenColumns.Should().Contain("sprk_mattername");
        outcome.WrittenColumns.Should().NotContain(c => c.StartsWith("sprk_mattertype", StringComparison.OrdinalIgnoreCase)
                                                        || c.StartsWith("sprk_MatterType", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WrittenColumns_LeaveOutAReferenceTheCreateDropped()
    {
        _world.Caller.Held.Remove("prvAppendTosprk_mattertype_ref");
        var request = await MatterAsync() with
        {
            ReferenceLookupColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "sprk_mattertype", "sprk_practicearea" },
        };

        var outcome = await _world.CreateAsync(request);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        outcome.WrittenColumns.Should().NotContain(MatterTypeBind, "the dropped reference is not in the row the create sent");
        outcome.WrittenColumns.Should().Contain(PracticeAreaBind);
    }

    [Fact]
    public async Task WrittenColumns_AreEmpty_OnARefusal_AndNothingWasCreated()
    {
        _world.Caller.NoAppendTo.Add(CreateWorld.OrdinaryMatter);

        var outcome = await _world.CreateAsync(await EventUnder(CreateWorld.OrdinaryMatter));

        outcome.Succeeded.Should().BeFalse();
        outcome.Refusal!.Kind.Should().Be(RecordCreationRefusalKind.NotFoundOrDenied);
        outcome.WrittenColumns.Should().BeEmpty();
        _world.Creates.Should().BeEmpty();
    }

    // =====================================================================================
    // G5 - RefusedColumn
    // =====================================================================================

    [Fact]
    public async Task RefusedColumn_OfATypedFilingLookupTheCallerCannotAppendTo_IsItsLogicalColumn_AndNothingElseChanged()
    {
        _world.Caller.NoAppendTo.Add(CreateWorld.OrdinaryMatter);

        var outcome = await _world.CreateAsync(await EventUnder(CreateWorld.OrdinaryMatter));

        var refusal = outcome.Refusal!;
        refusal.Kind.Should().Be(RecordCreationRefusalKind.NotFoundOrDenied);
        refusal.Code.Should().Be(RecordCreationRefusal.NotFoundCode);
        refusal.Detail.Should().Be("You do not have permission to file this record under the sprk_matter it names.");
        refusal.RefusedColumn.Should().Be("sprk_regardingmatter", "the logical column, without @odata.bind");
        RecordCreationProblemMap.StatusOf(refusal).Should().Be(StatusCodes.Status404NotFound);
        BodyOf(outcome, "sprk_event").Should().NotContain("sprk_regardingmatter", "the uniform not-found stays uniform");
        _world.Creates.Should().BeEmpty();
    }

    [Fact]
    public async Task RefusedColumn_OfANonFilingLookup_IsThatLookupsColumn()
    {
        _world.Caller.Held.Remove("prvAppendTosprk_mattertype_ref");

        var outcome = await _world.CreateAsync(await MatterAsync());

        outcome.Refusal!.Kind.Should().Be(RecordCreationRefusalKind.NotFoundOrDenied);
        outcome.Refusal.Code.Should().Be(RecordCreationRefusal.NotFoundCode);
        outcome.Refusal.RefusedColumn.Should().Be("sprk_mattertype");
    }

    [Fact]
    public async Task RefusedColumn_OfTheRegardingPairsRecord_IsSprkRegardingRecordId()
    {
        _world.Caller.NoAppendTo.Add(CreateWorld.OrdinaryMatter);

        var outcome = await _world.CreateAsync(_world.BrowserRouteProject(CreateWorld.OrdinaryMatter));

        var refusal = outcome.Refusal!;
        refusal.Kind.Should().Be(RecordCreationRefusalKind.NotFoundOrDenied);
        refusal.Code.Should().Be(RecordCreationRefusal.NotFoundCode);
        refusal.Detail.Should().Be("You do not have permission to file this record under the sprk_matter it names.");
        refusal.RefusedColumn.Should().Be("sprk_regardingrecordid");
        _world.Creates.Should().BeEmpty();
    }

    [Fact]
    public async Task RefusedColumn_OfARegardingPairWhoseRecordTypeTheCallerCannotRead_IsSprkRegardingRecordId()
    {
        _world.Caller.InvisibleRows.Add(CreateWorld.MatterType);

        var outcome = await _world.CreateAsync(_world.BrowserRouteProject(CreateWorld.OrdinaryMatter));

        var refusal = outcome.Refusal!;
        refusal.Kind.Should().Be(RecordCreationRefusalKind.NotFoundOrDenied);
        refusal.Code.Should().Be(RecordCreationRefusal.NotFoundCode);
        refusal.Detail.Should().Be("You do not have permission to file this record under the record type it names.");
        refusal.RefusedColumn.Should().Be("sprk_regardingrecordid");
        _world.Creates.Should().BeEmpty();
    }

    [Fact]
    public async Task RefusedColumn_IsNull_OnAForbiddenRefusal()
    {
        _world.Caller.Held.Remove("prvCreatesprk_event");

        var outcome = await _world.CreateAsync(await EventUnder(CreateWorld.OrdinaryMatter));

        outcome.Refusal!.Kind.Should().Be(RecordCreationRefusalKind.Forbidden);
        outcome.Refusal.RefusedColumn.Should().BeNull("a refusal of the caller's own rights names no lookup");
    }

    [Fact]
    public async Task RefusedColumn_IsNull_OnAFieldSecuredColumnRefusal()
    {
        _world.Caller.SecuredColumns.Add("sprk_eventname");

        var outcome = await _world.CreateAsync(await EventUnder(CreateWorld.OrdinaryMatter));

        outcome.Refusal!.Kind.Should().Be(RecordCreationRefusalKind.Forbidden);
        outcome.Refusal.RefusedColumn.Should().BeNull();
    }

    [Fact]
    public async Task RefusedColumn_IsNull_OnABlockedColumnRefusal()
    {
        var request = await _world.MappedAsync("sprk_matter", new { sprk_mattername = "Acme v. Widget", sprk_matternumber = "MAT-1" });

        var outcome = await _world.CreateAsync(request);

        outcome.Refusal!.Kind.Should().Be(RecordCreationRefusalKind.BlockedColumn);
        outcome.Refusal.RefusedColumn.Should().BeNull();
    }
}
