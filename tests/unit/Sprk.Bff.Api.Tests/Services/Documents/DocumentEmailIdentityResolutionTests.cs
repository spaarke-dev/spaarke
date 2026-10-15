using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Services.Documents;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Documents;

/// <summary>
/// spaarkeai-word-add-in-r1 task 120 (UAT round 12 O6): <see cref="DocumentUrlIdentityResolution.ParseEmailKeys"/> and
/// <see cref="DocumentUrlIdentityResolution.ResolveByEmailMessageIdAsync"/> — the email entry point behind
/// <c>POST /api/documents/resolve-email-identity</c>. The contracts pinned here:
/// <list type="bullet">
/// <item>input: the RFC message id is required, both keys are capped at 998 characters, and a refusal never echoes a
/// key back;</item>
/// <item>the query: saved <c>.eml</c> rows only (<c>sprk_isemailarchive = true</c>), <c>sprk_emailmessageid</c> equal to
/// EITHER key, the newest by <c>createdon</c> first, one row, the names and all four direct slots;</item>
/// <item>no row is <see langword="null"/>; a Dataverse failure is 503, never "not saved"; cancellation propagates.</item>
/// </list>
/// Dataverse is <see cref="IGenericEntityService"/>, STRICT, so any call a test does not expect fails it.
/// </summary>
public class DocumentEmailIdentityResolutionTests
{
    private const string MessageId = "<CAF0a1b2c3@mail.example.com>";
    private const string ItemId = "AAMkADAxZWI4YzM4LWVjNTgtNDcwOC1iY2EzLTI1MTc1MGU1MmFhMQBGAAAAAAD/abc+def=";

    private static readonly Guid DocumentId = Guid.Parse("6fb8c3fa-13c3-f111-a05c-0022482913fc");

    private readonly Mock<IGenericEntityService> _dataverse = new(MockBehavior.Strict);
    private QueryExpression? _captured;

    // ── Input ─────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ParseEmailKeys_WithoutAMessageId_Is400Required(string? messageId)
    {
        var act = () => DocumentUrlIdentityResolution.ParseEmailKeys(messageId, ItemId);

        var thrown = act.Should().Throw<SdapProblemException>().Which;
        thrown.StatusCode.Should().Be(400);
        thrown.Code.Should().Be("internet_message_id_required");
    }

    [Fact]
    public void ParseEmailKeys_WithAnOverLongMessageId_Is400_AndDoesNotEchoIt()
    {
        var tooLong = "<" + new string('m', DocumentUrlIdentityResolution.MaxEmailKeyLength) + "@x>";

        var act = () => DocumentUrlIdentityResolution.ParseEmailKeys(tooLong, null);

        var thrown = act.Should().Throw<SdapProblemException>().Which;
        thrown.StatusCode.Should().Be(400);
        thrown.Code.Should().Be("internet_message_id_too_long");
        thrown.Message.Should().NotContain("mmmmmmmmmm");
        thrown.Detail.Should().NotContain("mmmmmmmmmm");
    }

    [Fact]
    public void ParseEmailKeys_WithAnOverLongItemId_Is400_AndDoesNotEchoIt()
    {
        var tooLong = new string('A', DocumentUrlIdentityResolution.MaxEmailKeyLength + 1);

        var act = () => DocumentUrlIdentityResolution.ParseEmailKeys(MessageId, tooLong);

        var thrown = act.Should().Throw<SdapProblemException>().Which;
        thrown.StatusCode.Should().Be(400);
        thrown.Code.Should().Be("exchange_item_id_too_long");
        thrown.Detail.Should().NotContain("AAAAAAAAAA");
    }

    [Fact]
    public void ParseEmailKeys_AtTheCap_IsAccepted_AndKeysAreTrimmed()
    {
        var atCap = new string('m', DocumentUrlIdentityResolution.MaxEmailKeyLength);

        var keys = DocumentUrlIdentityResolution.ParseEmailKeys("  " + atCap + " ", " " + ItemId + " ");

        keys.InternetMessageId.Should().Be(atCap);
        keys.ExchangeItemId.Should().Be(ItemId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(MessageId)]
    public void ParseEmailKeys_AbsentOrDuplicateItemId_LeavesOnlyTheMessageId(string? itemId)
    {
        var keys = DocumentUrlIdentityResolution.ParseEmailKeys(MessageId, itemId);

        keys.InternetMessageId.Should().Be(MessageId);
        keys.ExchangeItemId.Should().BeNull();
    }

    // ── The query ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Query_IsSavedEmlRowsMatchingEitherKey_NewestFirst_OneRow_WithNamesAndAllFourSlots()
    {
        Returns();

        await Resolve(new DocumentUrlIdentityResolution.EmailKeys(MessageId, ItemId));

        _captured.Should().NotBeNull();
        var q = _captured!;
        q.EntityName.Should().Be("sprk_document");
        q.TopCount.Should().Be(1);
        q.ColumnSet.Columns.Should().Contain(new[]
        {
            "sprk_documentid", "sprk_documentname", "sprk_filename",
            "sprk_matter", "sprk_project", "sprk_invoice", "sprk_workassignment",
        });

        q.Criteria.FilterOperator.Should().Be(LogicalOperator.And);
        q.Criteria.Conditions.Should().ContainSingle(c =>
            c.AttributeName == "sprk_isemailarchive" && c.Operator == ConditionOperator.Equal
            && Equals(c.Values.Single(), true));

        var anyKey = q.Criteria.Filters.Should().ContainSingle().Which;
        anyKey.FilterOperator.Should().Be(LogicalOperator.Or);
        anyKey.Conditions.Should().HaveCount(2);
        anyKey.Conditions.Should().OnlyContain(c =>
            c.AttributeName == "sprk_emailmessageid" && c.Operator == ConditionOperator.Equal);
        anyKey.Conditions.Select(c => c.Values.Single()).Should().BeEquivalentTo(new object[] { MessageId, ItemId });

        q.Orders.Should().HaveCount(2);
        q.Orders[0].AttributeName.Should().Be("createdon");
        q.Orders[0].OrderType.Should().Be(OrderType.Descending);
        q.Orders[1].AttributeName.Should().Be("sprk_documentid", "a deterministic tie-break");
    }

    [Fact]
    public async Task Query_WithOnlyTheMessageId_MatchesThatKeyAlone()
    {
        Returns();

        await Resolve(new DocumentUrlIdentityResolution.EmailKeys(MessageId, null));

        var anyKey = _captured!.Criteria.Filters.Single();
        anyKey.Conditions.Should().ContainSingle();
        anyKey.Conditions[0].Values.Single().Should().Be(MessageId);
    }

    // ── Answers ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task NoSavedCopy_IsNull()
    {
        Returns();

        (await Resolve(new DocumentUrlIdentityResolution.EmailKeys(MessageId, ItemId))).Should().BeNull();
    }

    [Fact]
    public async Task SavedCopyFiledToAMatter_ResolvesWithItsNamesAndRecord()
    {
        var matter = new EntityReference("sprk_matter", Guid.NewGuid()) { Name = "MAT-000042" };
        Returns(Row(DocumentId, ("sprk_matter", matter)));

        var identity = await Resolve(new DocumentUrlIdentityResolution.EmailKeys(MessageId, ItemId));

        identity.Should().NotBeNull();
        identity!.DocumentId.Should().Be(DocumentId);
        identity.DocumentName.Should().Be("RE: Discovery schedule");
        identity.FileName.Should().Be("RE_ Discovery schedule_a1b2.eml");
        identity.RelatedRecord.Should().BeSameAs(matter);
    }

    [Fact]
    public async Task UnfiledSavedCopy_ResolvesWithNoRecord()
    {
        Returns(Row(DocumentId));

        var identity = await Resolve(new DocumentUrlIdentityResolution.EmailKeys(MessageId, null));

        identity!.RelatedRecord.Should().BeNull();
    }

    public static TheoryData<Exception> Failures => new()
    {
        new TimeoutException("The request channel timed out while waiting for a reply."),
        new InvalidOperationException("RetrieveMultiple failed", new HttpRequestException("No such host is known.")),
    };

    [Theory]
    [MemberData(nameof(Failures))]
    public async Task DataverseFailure_Is503_NeverNotSaved(Exception failure)
    {
        _dataverse
            .Setup(d => d.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(failure);

        var act = () => Resolve(new DocumentUrlIdentityResolution.EmailKeys(MessageId, ItemId));

        var thrown = (await act.Should().ThrowAsync<SdapProblemException>()).Which;
        thrown.StatusCode.Should().Be(503);
        thrown.Code.Should().Be("identity_resolution_unavailable");
    }

    [Fact]
    public async Task CallerCancellation_PropagatesAsCancellation_NotAs503()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        _dataverse
            .Setup(d => d.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("RetrieveMultiple failed", new TaskCanceledException("canceled")));

        var act = () => Resolve(new DocumentUrlIdentityResolution.EmailKeys(MessageId, null), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ── helpers ───────────────────────────────────────────────────────────────────────────────

    private Task<DocumentUrlIdentityResolution.Resolution?> Resolve(
        DocumentUrlIdentityResolution.EmailKeys keys, CancellationToken ct = default)
        => DocumentUrlIdentityResolution.ResolveByEmailMessageIdAsync(keys, _dataverse.Object, NullLogger.Instance, ct);

    private void Returns(params Entity[] rows)
        => _dataverse
            .Setup(d => d.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .Callback<QueryExpression, CancellationToken>((q, _) => _captured = q)
            .ReturnsAsync(new EntityCollection(rows.ToList()));

    internal static Entity Row(Guid id, params (string Attribute, EntityReference Reference)[] lookups)
    {
        var row = new Entity("sprk_document", id)
        {
            ["sprk_documentname"] = "RE: Discovery schedule",
            ["sprk_filename"] = "RE_ Discovery schedule_a1b2.eml",
        };
        foreach (var (attribute, reference) in lookups)
            row[attribute] = reference;
        return row;
    }
}
