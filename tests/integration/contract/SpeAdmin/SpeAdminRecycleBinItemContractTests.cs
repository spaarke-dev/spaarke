using System.Text.Json;
using Azure.Core;
using Azure.Security.KeyVault.Secrets;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Graph;
using Xunit;

namespace Sprk.Bff.Api.Tests.Contract.SpeAdmin;

/// <summary>
/// Pins the per-container ITEM recycle bin contract (spec FR-E03, task 052): the request shapes of
/// the <c>restore</c> / <c>delete</c> beta collection actions, the mapping of a
/// <c>recycleBinItem</c>, and — the load-bearing part — that neither operation's result is ever
/// collapsed into a single pass/fail.
/// </summary>
/// <remarks>
/// <para>
/// <b>The finding these tests exist to protect.</b> Measured live on throwaway containers
/// (2026-08-27, notes/task-052-findings.md §2): restore and permanent delete fail in <b>opposite</b>
/// ways.
/// </para>
/// <list type="bullet">
/// <item><b>restore</b> → <c>207</c> whose body lists only the ids that SUCCEEDED. Partial failure
/// is expressed by <b>absence</b>; there is no per-item error object. One invalid id in the batch
/// makes the whole call <c>400</c> and restores nothing — it is atomic on rejection.</item>
/// <item><b>delete</b> → <c>204</c> with an empty body <b>regardless of what it did</b>. A batch
/// containing one unknown id still returned 204 and still purged the valid ids. It is non-atomic and
/// completely silent. For an <b>irreversible</b> operation that is the worst reporting shape found
/// anywhere in this API.</item>
/// </list>
/// <para>
/// So the two operations cannot share a verification strategy, and the delete path is obliged to
/// re-read the recycle bin and diff rather than trust its own success status. These tests are the
/// automated statement of both. They were authored from Graph's <b>CSDL</b> and from <b>measured
/// live responses</b> — never from the shape our own code happens to produce, which would agree with
/// our own bugs.
/// </para>
/// <para>
/// This is the ITEM bin, not the deleted-CONTAINERS bin that task 022 fixed. Spec decision D3 keeps
/// both; the request-shape tests below assert the path so the two cannot quietly converge.
/// </para>
/// <para>
/// Per <c>tests/CLAUDE.md</c> these live under <c>tests/integration/contract/**</c> — a KEEP path.
/// The task POML nominated <c>tests/unit/Sprk.Bff.Api.Tests/Api/SpeAdmin/</c>, which task 042
/// established is NOT a KEEP path; writing them there would schedule them for deletion at the
/// <c>/test-diet</c> gate. Same deviation task 050 recorded.
/// </para>
/// </remarks>
public class SpeAdminRecycleBinItemContractTests
{
    private const string ContainerId = "b!DcvTfUkibESq94RyGJFs-UhqWZU646tBrEagKKMKiOc";
    private static string ItemsPath => $"/storage/fileStorage/containers/{ContainerId}/recycleBin/items";
    private static string RestorePath => $"{ItemsPath}/restore";
    private static string DeletePath => $"{ItemsPath}/delete";

    private const string ItemA = "01ABCDEF000000000000000000000000000001";
    private const string ItemB = "01ABCDEF000000000000000000000000000002";

    /// <summary>
    /// A recycle-bin listing shaped exactly as Graph returned it live. <c>deletedBy</c> and
    /// <c>title</c> are OpenType extras absent from the CSDL — they are present here precisely
    /// because a projection written from the CSDL alone would not know to expect them.
    /// </summary>
    private static string BinWith(params string[] ids)
    {
        var rows = ids.Select(id => $$"""
            {
              "id": "{{id}}",
              "name": "contract-{{id[^1]}}.docx",
              "title": "contract-{{id[^1]}}.docx",
              "size": 24576,
              "deletedDateTime": "2026-08-27T14:03:11Z",
              "deletedFromLocation": "contentstorage/CSP_8a6ce34c/Document Library",
              "deletedBy": { "user": { "displayName": "SharePoint App", "email": "", "id": "1073741822" } }
            }
            """);

        return $$"""{"value":[{{string.Join(",", rows)}}]}""";
    }

    private static string RestoredIds(params string[] ids)
        => $$"""{"value":[{{string.Join(",", ids.Select(i => $$"""{"id":"{{i}}"}"""))}}]}""";

    // ─────────────────────────────────────────────────────────────────────────
    // Listing — mapping
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public async Task ListRecycleBinItems_WhenGraphReturnsItems_MapsEveryReportedField()
    {
        using var graph = new GraphWireMockFixture();
        graph.StubGet(ItemsPath, BinWith(ItemA));

        var items = await CreateSut().ListRecycleBinItemsAsync(graph.CreateGraphClient(), ContainerId);

        var item = items.Should().ContainSingle().Subject;
        item.Id.Should().Be(ItemA);
        item.Name.Should().Be("contract-1.docx");
        item.Size.Should().Be(24576);
        item.DeletedDateTime.Should().Be(DateTimeOffset.Parse("2026-08-27T14:03:11Z"));
        item.DeletedFromLocation.Should().Be("contentstorage/CSP_8a6ce34c/Document Library");

        // The most operationally useful field on the record, and an OpenType extra — exactly the
        // kind that gets silently dropped by a reader written from the declared schema.
        item.DeletedByDisplayName.Should().Be("SharePoint App");
    }

    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public async Task ListRecycleBinItems_WhenBinIsEmpty_ReturnsEmptyListRatherThanFailing()
    {
        using var graph = new GraphWireMockFixture();
        graph.StubGet(ItemsPath, """{"value":[]}""");

        var items = await CreateSut().ListRecycleBinItemsAsync(graph.CreateGraphClient(), ContainerId);

        // An empty bin is a valid state. It must be representable as "nothing here" so the UI can
        // render an empty state distinguishable from a failure (acceptance criterion 6).
        items.Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public async Task ListRecycleBinItems_WhenGraphOmitsDeletedBy_ReportsNullRatherThanFabricatingAName()
    {
        using var graph = new GraphWireMockFixture();
        graph.StubGet(ItemsPath, """
            {"value":[{"id":"01X","name":"orphan.docx","size":10,"deletedDateTime":"2026-08-27T14:03:11Z"}]}
            """);

        var items = await CreateSut().ListRecycleBinItemsAsync(graph.CreateGraphClient(), ContainerId);

        // Negative control. Null means NOT REPORTED and must stay distinguishable from a real value —
        // the defect class this project exists to remove (task 050's fabricated "active" status).
        items.Should().ContainSingle().Which.DeletedByDisplayName.Should().BeNull();
        items.Single().DeletedFromLocation.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public async Task ListRecycleBinItems_TargetsTheContainerItemBin_NotTheDeletedContainersBin()
    {
        using var graph = new GraphWireMockFixture();
        graph.StubGet(ItemsPath, """{"value":[]}""");

        await CreateSut().ListRecycleBinItemsAsync(graph.CreateGraphClient(), ContainerId);

        // Spec D3 keeps the two recycle bins distinct. This is the mechanical guard against them
        // converging: the item bin hangs off the container, never off /deletedContainers.
        var request = graph.RequestsFor(ItemsPath).Should().ContainSingle().Subject;
        request.Method.Should().Be("GET");
        request.Path.Should().NotContain("deletedContainers");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Restore — 207 partial success
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public async Task RestoreRecycleBinItems_PostsIdsToTheBetaCollectionAction()
    {
        using var graph = new GraphWireMockFixture();
        graph.StubGet(ItemsPath, BinWith(ItemA));
        graph.StubPost(RestorePath, RestoredIds(ItemA), statusCode: 207);

        await CreateSut().RestoreRecycleBinItemsAsync(graph.CreateGraphClient(), ContainerId, [ItemA]);

        var post = graph.RequestsFor(RestorePath)
            .Should().ContainSingle(r => r.Method == "POST").Subject;

        // `restore` is bound to Collection(recycleBinItem) and takes an `ids` array in the body.
        // A wrong property name here is a hard failure against real Graph and invisible otherwise.
        var ids = post.BodyAsJson().GetProperty("ids").EnumerateArray()
            .Select(e => e.GetString()).ToList();
        ids.Should().ContainSingle().Which.Should().Be(ItemA);
    }

    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public async Task RestoreRecycleBinItems_WhenGraphConfirmsEveryId_ReportsAllRestored()
    {
        using var graph = new GraphWireMockFixture();
        graph.StubGet(ItemsPath, BinWith(ItemA, ItemB));
        graph.StubPost(RestorePath, RestoredIds(ItemA, ItemB), statusCode: 207);

        var result = await CreateSut()
            .RestoreRecycleBinItemsAsync(graph.CreateGraphClient(), ContainerId, [ItemA, ItemB]);

        result.RestoredCount.Should().Be(2);
        result.RequestedCount.Should().Be(2);
        result.IsPartialSuccess.Should().BeFalse();
        result.Outcomes.Should().OnlyContain(o => o.Succeeded);
    }

    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public async Task RestoreRecycleBinItems_WhenGraphOmitsAnId_ReportsThatItemAsNotRestored()
    {
        using var graph = new GraphWireMockFixture();
        graph.StubGet(ItemsPath, BinWith(ItemA, ItemB));

        // Two requested, Graph confirms one. This IS the 207 partial case, and it is the whole
        // acceptance criterion: the missing id is the failure, expressed as absence.
        graph.StubPost(RestorePath, RestoredIds(ItemA), statusCode: 207);

        var result = await CreateSut()
            .RestoreRecycleBinItemsAsync(graph.CreateGraphClient(), ContainerId, [ItemA, ItemB]);

        result.RequestedCount.Should().Be(2);
        result.RestoredCount.Should().Be(1);
        result.IsPartialSuccess.Should().BeTrue();

        result.Outcomes.Single(o => o.Id == ItemA).Succeeded.Should().BeTrue();

        var failed = result.Outcomes.Single(o => o.Id == ItemB);
        failed.Succeeded.Should().BeFalse();
        failed.Detail.Should().NotBeNullOrWhiteSpace();

        // Named, not just counted — "1 of 2 restored" without saying WHICH is barely better than a
        // collapsed boolean.
        failed.Name.Should().Be("contract-2.docx");
    }

    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public async Task RestoreRecycleBinItems_WhenGraphReturnsNoIds_ReportsEveryItemNotRestored()
    {
        using var graph = new GraphWireMockFixture();
        graph.StubGet(ItemsPath, BinWith(ItemA, ItemB));
        graph.StubPost(RestorePath, """{"value":[]}""", statusCode: 207);

        var result = await CreateSut()
            .RestoreRecycleBinItemsAsync(graph.CreateGraphClient(), ContainerId, [ItemA, ItemB]);

        // A 207 with an empty value array is still a 207. Treating the status as the answer would
        // report complete success for an operation that restored nothing.
        result.RestoredCount.Should().Be(0);
        result.Outcomes.Should().HaveCount(2).And.OnlyContain(o => !o.Succeeded);
    }

    /// <summary>
    /// Both error payloads Graph has actually been observed returning for a rejected restore.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>These are the SAME case, measured six days apart, with different error codes.</b>
    /// <list type="bullet">
    /// <item>2026-08-27 (discovery): <c>badArgument</c> — <i>"Invalid Recyle Bin Restore Ids"</i>
    /// (Microsoft's typo, reproduced verbatim rather than corrected).</item>
    /// <item>2026-08-27 (implementation verification, later the same day):
    /// <c>invalidRequest</c> — <i>"One of the provided arguments is not acceptable."</i></item>
    /// </list>
    /// Nothing about the request changed. Graph's <c>code</c> for this condition is simply not
    /// stable, which is why <c>RestoreRecycleBinItemsAsync</c> keys the diagnosis on the <b>400
    /// status</b> rather than on the code string. Had it matched on <c>badArgument</c> — the way
    /// <c>IsArchivalNotEnabled</c> is obliged to match on <c>notAllowed</c>, because there a 403
    /// alone is ambiguous — the detector would already have stopped detecting within a week of
    /// being written, and silently: rejections would have fallen through to the generic error path
    /// and been reported as ordinary failures rather than as "nothing was restored".
    /// </remarks>
    public static TheoryData<string, string> ObservedRejectionPayloads => new()
    {
        { "badArgument", "Invalid Recyle Bin Restore Ids" },
        { "invalidRequest", "One of the provided arguments is not acceptable." },
    };

    [Theory]
    [MemberData(nameof(ObservedRejectionPayloads))]
    [Trait("Category", "SpeAdminGraphContract")]
    public async Task RestoreRecycleBinItems_WhenGraphRejectsTheBatch_ThrowsRejectedRegardlessOfErrorCode(
        string graphCode, string graphMessage)
    {
        using var graph = new GraphWireMockFixture();
        graph.StubGet(ItemsPath, BinWith(ItemA));
        // Serialized rather than written as a raw string literal: the nested closing braces of an
        // OData error collide with raw-string terminator parsing, and the workaround (padding with
        // whitespace) silently changes the payload under test.
        var errorBody = JsonSerializer.Serialize(
            new { error = new { code = graphCode, message = graphMessage } });

        graph.StubPost(RestorePath, errorBody, statusCode: 400);

        var act = async () => await CreateSut()
            .RestoreRecycleBinItemsAsync(graph.CreateGraphClient(), ContainerId, [ItemA, ItemB]);

        // Restore is ATOMIC on rejection — nothing was restored and the bin is unchanged. Reporting
        // this as "0 of 2 restored" would be indistinguishable from a 207 that restored nothing, and
        // the two need different remediation: one says "refresh and retry", the other does not.
        var thrown = await act.Should()
            .ThrowAsync<SpeAdminGraphService.RecycleBinRestoreRejectedException>();

        thrown.Which.RequestedIds.Should().BeEquivalentTo([ItemA, ItemB]);

        // Graph's message is preserved verbatim for the ProblemDetails payload, whichever wording
        // this particular deployment happens to send.
        thrown.Which.GraphMessage.Should().Be(graphMessage);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Permanent delete — the 204 that means nothing
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public async Task PermanentDeleteRecycleBinItems_PostsIdsToTheBetaCollectionAction()
    {
        using var graph = new GraphWireMockFixture();
        graph.StubGetSequence(ItemsPath, (BinWith(ItemA), 200), ("""{"value":[]}""", 200));
        graph.StubPost(DeletePath, "", statusCode: 204);

        await CreateSut()
            .PermanentDeleteRecycleBinItemsAsync(graph.CreateGraphClient(), ContainerId, [ItemA]);

        var post = graph.RequestsFor(DeletePath)
            .Should().ContainSingle(r => r.Method == "POST").Subject;

        post.BodyAsJson().GetProperty("ids").EnumerateArray()
            .Select(e => e.GetString()).Should().ContainSingle().Which.Should().Be(ItemA);
    }

    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public async Task PermanentDeleteRecycleBinItems_WhenItemIsGoneAfterwards_ReportsItPurged()
    {
        using var graph = new GraphWireMockFixture();
        graph.StubGetSequence(ItemsPath, (BinWith(ItemA), 200), ("""{"value":[]}""", 200));
        graph.StubPost(DeletePath, "", statusCode: 204);

        var result = await CreateSut()
            .PermanentDeleteRecycleBinItemsAsync(graph.CreateGraphClient(), ContainerId, [ItemA]);

        result.Verified.Should().BeTrue();
        result.PurgedCount.Should().Be(1);
        result.Outcomes.Should().ContainSingle().Which.Succeeded.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public async Task PermanentDeleteRecycleBinItems_When204ButItemRemainsInBin_ReportsItNotPurged()
    {
        using var graph = new GraphWireMockFixture();

        // The bin is unchanged across the delete. Graph still said 204.
        graph.StubGetSequence(ItemsPath, (BinWith(ItemA), 200), (BinWith(ItemA), 200));
        graph.StubPost(DeletePath, "", statusCode: 204);

        var result = await CreateSut()
            .PermanentDeleteRecycleBinItemsAsync(graph.CreateGraphClient(), ContainerId, [ItemA]);

        // 🔴 THE REGRESSION GUARD. Graph returns 204 whether it purged everything, some, or nothing.
        // Any implementation that reads the status instead of re-reading the bin reports a
        // destruction that did not happen — and for an irreversible operation, a false "deleted"
        // sends an admin away believing data is gone when it is still there.
        result.Verified.Should().BeTrue();
        result.PurgedCount.Should().Be(0);

        var outcome = result.Outcomes.Should().ContainSingle().Subject;
        outcome.Succeeded.Should().BeFalse();
        outcome.Detail.Should().Contain("Still in the recycle bin");
    }

    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public async Task PermanentDeleteRecycleBinItems_WhenIdWasNeverInTheBin_ReportsNoPurgeRatherThanSuccess()
    {
        using var graph = new GraphWireMockFixture();

        // ItemB is requested but was never in the bin. It is absent from the AFTER list for a reason
        // that has nothing to do with this call.
        graph.StubGetSequence(ItemsPath, (BinWith(ItemA), 200), ("""{"value":[]}""", 200));
        graph.StubPost(DeletePath, "", statusCode: 204);

        var result = await CreateSut()
            .PermanentDeleteRecycleBinItemsAsync(graph.CreateGraphClient(), ContainerId, [ItemA, ItemB]);

        // Negative control for the before-list. Diffing only the AFTER state would credit this call
        // with purging ItemB — a fabricated success, and the exact defect shape this project exists
        // to remove, pointed at an irreversible operation.
        result.Outcomes.Single(o => o.Id == ItemA).Succeeded.Should().BeTrue();

        var neverPresent = result.Outcomes.Single(o => o.Id == ItemB);
        neverPresent.Succeeded.Should().BeFalse();
        neverPresent.Detail.Should().ContainEquivalentOf("was not in the recycle bin");
    }

    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public async Task PermanentDeleteRecycleBinItems_WhenTheBinCannotBeReRead_ReportsUnverifiedRatherThanAssumingSuccess()
    {
        using var graph = new GraphWireMockFixture();

        // Before-list succeeds; the delete is sent; the verification read then fails.
        graph.StubGetSequence(
            ItemsPath,
            (BinWith(ItemA), 200),
            ("""{"error":{"code":"serviceUnavailable","message":"Graph is unavailable."}}""", 503));
        graph.StubPost(DeletePath, "", statusCode: 204);

        var result = await CreateSut()
            .PermanentDeleteRecycleBinItemsAsync(graph.CreateGraphClient(), ContainerId, [ItemA]);

        // The delete WAS issued and the data may well be gone — but we did not observe it. Reporting
        // success here would assert something unestablished; reporting a hard failure would assert
        // the opposite thing, equally unestablished. Unverified is the only honest answer.
        result.Verified.Should().BeFalse();
        result.VerificationFailureReason.Should().NotBeNullOrWhiteSpace();
        result.Outcomes.Should().ContainSingle().Which.Succeeded.Should().BeFalse();
        result.Outcomes.Single().Detail.Should().Contain("Unverified");
    }

    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public async Task PermanentDeleteRecycleBinItems_WithNoIds_MakesNoGraphCall()
    {
        using var graph = new GraphWireMockFixture();

        var result = await CreateSut()
            .PermanentDeleteRecycleBinItemsAsync(graph.CreateGraphClient(), ContainerId, []);

        // An empty request must not reach an irreversible endpoint at all.
        result.Outcomes.Should().BeEmpty();
        result.Verified.Should().BeTrue();
        graph.AllRequests.Should().BeEmpty();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SUT
    // ─────────────────────────────────────────────────────────────────────────

    private static SpeAdminGraphService CreateSut()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Dataverse:ServiceUrl"] = "https://unused.invalid",
            })
            .Build();

        return new SpeAdminGraphService(
            httpClientFactory: new UnusedHttpClientFactory(),
            secretClient: new SecretClient(new Uri("https://unused.invalid/"), new UnusableCredential()),
            dataverseClient: new DataverseWebApiClient(
                configuration, NullLogger<DataverseWebApiClient>.Instance, new UnusableCredential()),
            configuration: configuration,
            logger: NullLogger<SpeAdminGraphService>.Instance,
            tokenProvider: null);
    }

    private sealed class UnusedHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new InvalidOperationException(
            $"A method under test requested the '{name}' HttpClient. These tests supply the Graph " +
            "client directly, so building one means the code took an unexpected path.");
    }

    private sealed class UnusableCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken ct)
            => throw new InvalidOperationException(
                "A method under test tried to acquire a token. These tests reach only the fake Graph " +
                "endpoint, so a token request means the code took an unexpected path.");

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken ct)
            => throw new InvalidOperationException(
                "A method under test tried to acquire a token. These tests reach only the fake Graph " +
                "endpoint, so a token request means the code took an unexpected path.");
    }
}
