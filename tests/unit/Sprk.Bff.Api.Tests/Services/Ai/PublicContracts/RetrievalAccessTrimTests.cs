using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.PublicContracts;

/// <summary>
/// The access trim's own contract (unified-access-control-r2 task 176, #1511): the rules the owner set for the batch
/// read: ids canonicalized (ADR-044), at most 20 ids per read, rows without an id dropped, an answer naming an id that
/// was not asked never widens the result, and a caller who is not the request principal gets nothing. The callers'
/// behaviour is in <c>tests/integration/regression/Ai/Issue1511_AiRetrievalAccessTrimTests.cs</c>.
/// </summary>
public sealed class RetrievalAccessTrimTests
{
    private const string CallerOid = "11111111-2222-3333-4444-555555555555";

    private sealed record Row(string Name, string? DocumentId);

    private static (RetrievalAccessTrim Trim, ReadableDocumentsUserClient Client) Create(
        string? requestOid = CallerOid, params Guid[] readable)
    {
        var accessor = new HttpContextAccessor
        {
            HttpContext = requestOid is null ? null : TestHttpContexts.Authenticated(requestOid),
        };
        var client = new ReadableDocumentsUserClient(readable);
        return (new RetrievalAccessTrim(accessor, client, NullLogger<RetrievalAccessTrim>.Instance), client);
    }

    [Fact]
    public async Task Ids_AreCanonicalized_SoBracedUppercaseRowsMatchTheLowercaseAnswer()
    {
        var doc = Guid.NewGuid();
        var (trim, client) = Create(readable: doc);
        var rows = new[] { new Row("a", "{" + doc.ToString().ToUpperInvariant() + "}") };

        var result = await trim.TrimAsync(rows, r => r.DocumentId, CallerOid);

        result.Rows.Should().ContainSingle().Which.Name.Should().Be("a");
        client.Reads.Single().Should().ContainSingle().Which.Should().Be(doc.ToString("D"),
            because: "ADR-044: the filter carries the bare lowercase form, whatever the index row spelled");
    }

    [Fact]
    public async Task MoreThanTwentyDistinctIds_AreReadInChunksOfTwenty_AndOrderIsKept()
    {
        var docs = Enumerable.Range(0, 45).Select(_ => Guid.NewGuid()).ToArray();
        var readable = docs.Where((_, i) => i % 2 == 0).ToArray();
        var (trim, client) = Create(readable: readable);
        var rows = docs.Select((d, i) => new Row($"r{i}", d.ToString())).ToList();

        var result = await trim.TrimAsync(rows, r => r.DocumentId, CallerOid);

        client.Reads.Select(r => r.Count).Should().Equal(20, 20, 5);
        result.Rows.Select(r => r.Name).Should().Equal(rows.Where((_, i) => i % 2 == 0).Select(r => r.Name));
        result.DroppedCount.Should().Be(22);
    }

    [Fact]
    public async Task RowsWithoutAUsableDocumentId_AreDropped()
    {
        var doc = Guid.NewGuid();
        var (trim, _) = Create(readable: doc);
        var rows = new[]
        {
            new Row("kept", doc.ToString()),
            new Row("null", null),
            new Row("not-a-guid", "chunk-17"),
            new Row("empty-guid", Guid.Empty.ToString()),
        };

        var result = await trim.TrimAsync(rows, r => r.DocumentId, CallerOid);

        result.Rows.Select(r => r.Name).Should().Equal("kept");
    }

    [Fact]
    public async Task AnAnswerNamingAnIdThatWasNotAsked_DoesNotWidenTheResult()
    {
        var asked = Guid.NewGuid();
        var other = Guid.NewGuid();
        var (trim, client) = Create();
        client.RawBody = $"{{\"value\":[{{\"sprk_documentid\":\"{other}\"}}]}}";

        var result = await trim.TrimAsync(new[] { new Row("asked", asked.ToString()) }, r => r.DocumentId, CallerOid);

        result.Rows.Should().BeEmpty();
        result.Outcome.Should().Be(RetrievalTrimOutcome.Evaluated);
    }

    [Fact]
    public async Task ADeclaredCallerWhoIsNotTheRequestPrincipal_GetsNothing_AndNoReadIsMade()
    {
        var doc = Guid.NewGuid();
        var (trim, client) = Create(requestOid: "99999999-9999-9999-9999-999999999999", readable: doc);

        var result = await trim.TrimAsync(new[] { new Row("a", doc.ToString()) }, r => r.DocumentId, CallerOid);

        result.Rows.Should().BeEmpty();
        result.Outcome.Should().Be(RetrievalTrimOutcome.NoVerifiedCaller);
        client.Reads.Should().BeEmpty();
        trim.CanEvaluate(CallerOid).Should().BeFalse();
    }

    [Fact]
    public async Task AMalformedAnswer_WithholdsEverything()
    {
        var doc = Guid.NewGuid();
        var (trim, client) = Create(readable: doc);
        client.RawBody = "{\"unexpected\":true}";

        var result = await trim.TrimAsync(new[] { new Row("a", doc.ToString()) }, r => r.DocumentId, CallerOid);

        result.Rows.Should().BeEmpty();
        result.Outcome.Should().Be(RetrievalTrimOutcome.CheckFailed);
    }
}
