using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using System.Text.Json;
using Sprk.Bff.Api.Api;
using Sprk.Bff.Api.Api.Agent;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Handlers;
using Sprk.Bff.Api.Services.RecordCreation;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;
using Directory = Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

/// <summary>
/// field-mapping-server-write-path-r1 task 039, review R4 (FR-38, #2399): the wizard's REAL route
/// (<c>POST /api/v1/child-records/sprk_workassignment</c>, <see cref="ChildRecordEndpoints.CreateAsync"/> over the real
/// <c>RecordCreationService</c>) with a Web API body that names the search index. The service drops both index columns from
/// that body before it is mapped and before step 5 asks the caller anything, so a bind the caller cannot AppendTo, or one that is
/// malformed, is neither asked about nor refused, and no index is written (task 041, FR-40: the index follows the content type,
/// not the owning unit). Must fire for the Web API body
/// drop (<c>RecordCreationService</c> step 2): take it out and the malformed-bind tests and the both-spellings test fail. The
/// AppendTo theory is defence in depth: it still passes without that drop, because the late drop after step 3.5 removes the bind
/// before step 5 asks anything.
/// </summary>
public sealed partial class SecureChildOwnershipAiToolTests
{
    private static readonly Guid UnitIndexRow = Guid.Parse("a0390000-0000-4000-8000-0000000000b1");
    private static readonly Guid CallersIndexRow = Guid.Parse("a0390000-0000-4000-8000-0000000000b2");
    private const string UnitIndexName = "spaarke-child-unit-index";

    /// <summary>POST /api/v1/child-records/sprk_workassignment, the owning (child) business unit holding a search index.</summary>
    private Task<IResult> CreateChildUnderAUnitWithAnIndex(Dictionary<string, object?> payload, List<string> catalogReads)
    {
        var entities = RouteRecordCreation.Entities();
        entities
            .Setup(e => e.RetrieveAsync("businessunit", Directory.ChildBu, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                var unit = new Entity("businessunit", Directory.ChildBu);
                unit["sprk_searchindexname"] = UnitIndexName;
                unit["sprk_ai_search_index"] = new EntityReference("sprk_aisearchindex", UnitIndexRow);
                return unit;
            });
        _user.GetOverride = path =>
        {
            if (path.Contains("sprk_aisearchindexes(", StringComparison.Ordinal))
                catalogReads.Add(path);
            return null;
        };

        return ChildRecordEndpoints.CreateAsync(
            "sprk_workassignment", Payload(payload), _user,
            RouteRecordCreation.Over(
                _world.Resolver(), _appOnly.Object, Restamper(), Shares(), SecureRootFilingGateFixtures.NothingSecure(),
                null, _routeMembership.Object, entities.Object),
            HttpContextOfCaller(), NullLogger<Program>.Instance, CancellationToken.None);
    }

    [Theory]
    [InlineData("sprk_AI_Search_Index@odata.bind")]
    [InlineData("sprk_ai_search_index@odata.bind")]
    public async Task WorkAssignmentCreate_ABodyBindingASearchIndexTheCallerCannotAppendTo_IsCreated_AskingNoAppendToOnTheCatalog(string bindKey)
    {
        _user.NoAppendToIn.Add(("sprk_aisearchindexes", CallersIndexRow));
        var payload = WizardWorkAssignment(OrdinaryMatter);
        payload[bindKey] = $"/sprk_aisearchindexes({CallersIndexRow:D})";
        var catalogReads = new List<string>();

        var result = await CreateChildUnderAUnitWithAnIndex(payload, catalogReads);

        Status(result).Should().Be(StatusCodes.Status201Created, Detail(result));
        catalogReads.Should().BeEmpty("the bind is dropped before step 5, so AppendTo is never asked on a catalog row (#2399)");
        var fields = _appCreates.Should().ContainSingle().Subject.Fields;
        fields.Keys.Where(k => k.StartsWith("sprk_ai_search_index", StringComparison.OrdinalIgnoreCase)).Should().BeEmpty(
            "the wizard's bind is dropped and the server writes none (task 041)");
        fields.Should().NotContainKey("sprk_searchindexname", "the wizard's name is dropped and the server writes none (task 041)");
    }

    [Fact]
    public async Task WorkAssignmentCreate_ABodyWithBothSpellingsOfTheSearchIndexBind_IsCreated_WithNoIndex()
    {
        _user.NoAppendToIn.Add(("sprk_aisearchindexes", CallersIndexRow));
        var payload = WizardWorkAssignment(OrdinaryMatter);
        payload["sprk_AI_Search_Index@odata.bind"] = $"/sprk_aisearchindexes({CallersIndexRow:D})";
        payload["sprk_ai_search_index@odata.bind"] = $"/sprk_aisearchindexes({CallersIndexRow:D})";
        var catalogReads = new List<string>();

        var result = await CreateChildUnderAUnitWithAnIndex(payload, catalogReads);

        Status(result).Should().Be(StatusCodes.Status201Created, Detail(result));
        catalogReads.Should().BeEmpty();
        var fields = _appCreates.Should().ContainSingle().Subject.Fields;
        fields.Keys.Where(k => k.StartsWith("sprk_ai_search_index", StringComparison.OrdinalIgnoreCase)).Should().BeEmpty();
    }

    [Theory]
    [InlineData("sprk_AI_Search_Index@odata.bind")]
    [InlineData("sprk_ai_search_index@odata.bind")]
    public async Task WorkAssignmentCreate_ABodyWithAMalformedSearchIndexBind_IsCreated_NotRefusedAs400(string bindKey)
    {
        var payload = WizardWorkAssignment(OrdinaryMatter);
        payload[bindKey] = "this is not a bind";
        var catalogReads = new List<string>();

        var result = await CreateChildUnderAUnitWithAnIndex(payload, catalogReads);

        Status(result).Should().Be(StatusCodes.Status201Created, Detail(result));
        var fields = _appCreates.Should().ContainSingle().Subject.Fields;
        fields.Keys.Where(k => k.StartsWith("sprk_ai_search_index", StringComparison.OrdinalIgnoreCase)).Should().BeEmpty();
    }

    // ── Review R7: the chat create tool (dataverse.create_record) ────────────────────────────────────────────────────

    /// <summary>
    /// The chat create maps the model's item before the service drops anything, so a model-built index lookup that cannot be
    /// mapped used to fail the whole create (ValidationFailed) while the chat UPDATE dropped it. Both now drop the index first:
    /// a malformed lookup, or one the caller cannot AppendTo, never refuses the create and never reaches the row.
    /// </summary>
    [Theory]
    [InlineData("malformed")]
    [InlineData("noappendto")]
    [InlineData("name")]
    public async Task ChatCreate_AnItemNamingTheSearchIndex_IsCreated_NeverRefused_AndTheIndexIsNotTheModels(string shape)
    {
        _user.NoAppendToIn.Add(("sprk_aisearchindexes", CallersIndexRow));
        var index = shape switch
        {
            "malformed" => ("sprk_ai_search_index", System.Text.Json.JsonSerializer.SerializeToElement(
                new { relatedTable = "sprk_aisearchindexes", recordId = "x" })),
            "noappendto" => Lookup("sprk_ai_search_index", "sprk_aisearchindex", CallersIndexRow),
            _ => ("sprk_searchindexname", System.Text.Json.JsonSerializer.SerializeToElement("model-index")),
        };

        var result = await CreateRecord("sprk_event",
            ("sprk_eventname", System.Text.Json.JsonSerializer.SerializeToElement("Hearing")),
            Lookup("sprk_regardingmatter", "sprk_matter", OrdinaryMatter),
            index);

        result.Success.Should().BeTrue(result.ErrorMessage);
        var fields = _appCreates.Should().ContainSingle(c => c.Table == "sprk_event").Subject.Fields;
        fields.Keys.Where(k => k.StartsWith("sprk_ai_search_index", StringComparison.OrdinalIgnoreCase)).Should().BeEmpty(
            "the model's index lookup is dropped (the owning unit has no index in this world)");
        fields.Should().NotContainKey("sprk_searchindexname", "the model's name is dropped too");
    }
    // ── Fix 019 review: the chat create's drop log has the service's shape, and only when the values differ ───────────

    /// <summary>
    /// The chat create tool over a business unit that holds an index, with both loggers captured: the handler's (where the
    /// pre-fix drop logged, with no record id) and the service's (step 12, where the drop is logged with the record id and the
    /// applied index).
    /// </summary>
    private async Task<(ToolResult Result, List<string> DropLines)> ChatCreateEventOverAUnitWithAnIndex(
        params (string Column, JsonElement Value)[] index)
    {
        var entities = RouteRecordCreation.Entities();
        entities
            .Setup(e => e.RetrieveAsync("businessunit", Directory.ChildBu, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                var unit = new Entity("businessunit", Directory.ChildBu);
                unit["sprk_searchindexname"] = UnitIndexName;
                unit["sprk_ai_search_index"] = new EntityReference("sprk_aisearchindex", UnitIndexRow);
                return unit;
            });
        var handlerLog = new CapturingLog<DataverseCreateRecordHandler>();
        var serviceLog = new CapturingLog<RecordCreationService>();
        var handler = new DataverseCreateRecordHandler(
            _user, handlerLog, new HandoffUrlBuilder("https://spaarkedev1.crm.dynamics.com"),
            _world.Resolver(), IdentityNormalizationFixtures.WithContact(CallerContact).Object,
            RouteRecordCreation.Over(_world.Resolver(), _appOnly.Object, Restamper(), Shares(),
                SecureRootFilingGateFixtures.NothingSecure(), null, null, entities.Object, serviceLog));

        var result = await handler.ExecuteChatAsync(
            CreateContext("sprk_event",
                new[]
                {
                    ("sprk_eventname", System.Text.Json.JsonSerializer.SerializeToElement("Hearing")),
                    Lookup("sprk_regardingmatter", "sprk_matter", OrdinaryMatter),
                }.Concat(index).ToArray()),
            BuildAnalysisTool(nameof(DataverseCreateRecordHandler)), CancellationToken.None);

        return (result, handlerLog.Messages.Concat(serviceLog.Messages).Where(m => m.Contains("client_search_index_dropped")).ToList());
    }

    [Fact]
    public async Task ChatCreate_AClientIndexThatDiffersFromTheApplied_LogsOneDrop_WithTheRecordId_TheAppliedIndex_AndOriginCaller()
    {
        var (result, dropLines) = await ChatCreateEventOverAUnitWithAnIndex(
            Lookup("sprk_ai_search_index", "sprk_aisearchindex", CallersIndexRow));

        result.Success.Should().BeTrue(result.ErrorMessage);
        var created = _appCreates.Should().ContainSingle(c => c.Table == "sprk_event").Subject;
        created.Fields.Keys.Where(k => k.StartsWith("sprk_ai_search_index", StringComparison.OrdinalIgnoreCase)).Should().BeEmpty();
        var line = dropLines.Should().ContainSingle("the create logs the drop once, from the service's step 12").Subject;
        line.Should().Contain("table=sprk_event").And.Contain($"record={created.Id}").And.Contain("origin=caller")
            .And.Contain($"droppedIndexId={CallersIndexRow}").And.Contain("appliedIndexId=(null)");
        line.Should().NotContain("unchanged", "a create has an applied index; 'unchanged' is the update's");
    }

    /// <summary>
    /// Task 041: the owning unit's own index, sent by the model, is no longer "equal to the applied" (nothing is applied), so
    /// both dropped values differ and are logged. FR-38's "log only when it differs" rule itself is unchanged.
    /// </summary>
    [Fact]
    public async Task ChatCreate_AClientIndexThatEqualsTheOwningUnitsIndex_IsLogged_BecauseNothingIsApplied()
    {
        var (result, dropLines) = await ChatCreateEventOverAUnitWithAnIndex(
            Lookup("sprk_ai_search_index", "sprk_aisearchindex", UnitIndexRow),
            ("sprk_searchindexname", System.Text.Json.JsonSerializer.SerializeToElement(UnitIndexName)));

        result.Success.Should().BeTrue(result.ErrorMessage);
        var fields = _appCreates.Should().ContainSingle(c => c.Table == "sprk_event").Subject.Fields;
        fields.Keys.Where(k => k.StartsWith("sprk_ai_search_index", StringComparison.OrdinalIgnoreCase)).Should().BeEmpty();
        dropLines.Should().HaveCount(2, "one line per column: nothing is applied, so the unit's own index differs from it");
    }

    [Fact]
    public async Task ChatCreate_AMalformedClientIndex_StillSucceeds_AndLogsNoDrop_BecauseBothAreNone()
    {
        var (result, dropLines) = await ChatCreateEventOverAUnitWithAnIndex(
            ("sprk_ai_search_index", System.Text.Json.JsonSerializer.SerializeToElement(
                new { relatedTable = "sprk_aisearchindexes", recordId = "x" })));

        result.Success.Should().BeTrue(result.ErrorMessage, "R7: a malformed index is dropped before it is mapped, never refused");
        var created = _appCreates.Should().ContainSingle(c => c.Table == "sprk_event").Subject;
        created.Fields.Keys.Where(k => k.StartsWith("sprk_ai_search_index", StringComparison.OrdinalIgnoreCase)).Should().BeEmpty();
        created.Fields.Should().NotContainKey("sprk_searchindexname", "the malformed value never reaches the row");
        dropLines.Should().BeEmpty(
            "FR-38: equal means both none; a client value that parses to no id equals the empty applied value");
    }

    // ── Pass 2 (task 019): the run-as-user branch logs its drop only after the create succeeds ────────────────────────

    /// <summary>
    /// A chat create of <c>sprk_event</c> down the RUN-AS-USER path (the table is declared organization-owned here, which is
    /// the only way a search-indexed table reaches it: <c>OwnedChildWrite.PathFor</c>), the model's index lookup in the item,
    /// filed under <paramref name="regarding"/>. Returns the result and the handler's drop lines.
    /// </summary>
    private async Task<(ToolResult Result, List<string> DropLines)> RunAsUserCreateEventNamingAnIndex(Guid regarding)
    {
        _user.AlsoOrganizationOwned.Add("sprk_event");
        var handlerLog = new CapturingLog<DataverseCreateRecordHandler>();
        var handler = new DataverseCreateRecordHandler(
            _user, handlerLog, new HandoffUrlBuilder("https://spaarkedev1.crm.dynamics.com"),
            _world.Resolver(), IdentityNormalizationFixtures.WithContact(CallerContact).Object,
            RouteRecordCreation.Over(_world.Resolver(), _appOnly.Object, Restamper(), Shares(), SecureRootFilingGateFixtures.NothingSecure()));

        var result = await handler.ExecuteChatAsync(
            CreateContext("sprk_event",
                ("sprk_eventname", System.Text.Json.JsonSerializer.SerializeToElement("Hearing")),
                Lookup("sprk_regardingmatter", "sprk_matter", regarding),
                Lookup("sprk_ai_search_index", "sprk_aisearchindex", CallersIndexRow)),
            BuildAnalysisTool(nameof(DataverseCreateRecordHandler)), CancellationToken.None);

        return (result, handlerLog.Messages.Where(m => m.Contains("client_search_index_dropped")).ToList());
    }

    /// <summary>The drop was logged BEFORE the secure-filing refusal and the POST, so a create that never happened still claimed one.</summary>
    [Fact]
    public async Task RunAsUserCreate_RefusedUnderASecureRecord_LogsNoSearchIndexDrop()
    {
        var (result, dropLines) = await RunAsUserCreateEventNamingAnIndex(SecureMatter);

        result.Success.Should().BeFalse("filed under a secure record, a run-as-user create is refused");
        _user.Posts.Should().BeEmpty("refused before the caller's POST");
        dropLines.Should().BeEmpty("nothing was created, so no index was dropped from a create");
    }

    /// <summary>The same branch, the create going through, still logs the drop once (the log moved; it was not removed).</summary>
    [Fact]
    public async Task RunAsUserCreate_ThatSucceeds_LogsTheSearchIndexDropOnce()
    {
        var (result, dropLines) = await RunAsUserCreateEventNamingAnIndex(OrdinaryMatter);

        result.Success.Should().BeTrue(result.ErrorMessage);
        var post = _user.Posts.Should().ContainSingle().Subject;
        post.Body.Should().NotContain("sprk_ai_search_index", "the model's index is not written on this path either");
        dropLines.Should().ContainSingle().Which.Should().Contain("table=sprk_event").And.Contain("origin=caller");
    }

    /// <summary>A POST the caller's Dataverse refuses creates nothing, so it logs no drop either.</summary>
    [Fact]
    public async Task RunAsUserCreate_WhoseCreateFails_LogsNoSearchIndexDrop()
    {
        _user.PostStatus = 403;

        var (result, dropLines) = await RunAsUserCreateEventNamingAnIndex(OrdinaryMatter);

        result.Success.Should().BeFalse();
        dropLines.Should().BeEmpty();
    }
}
