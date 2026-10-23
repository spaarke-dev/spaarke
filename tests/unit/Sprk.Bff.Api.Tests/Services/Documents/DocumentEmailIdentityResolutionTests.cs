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
/// <item>no row is <see langword="null"/>; a Dataverse failure is 503, never "not saved"; cancellation propagates;</item>
/// <item>task 132: an archive is ALSO found through its <c>sprk_relatedcommunication</c>'s <c>sprk_internetmessageid</c>
/// (mailbox capture and Spaarke send never stamp <c>sprk_emailmessageid</c>), newest wins across both branches, and the
/// record such an archive is filed to is derived from the communication only when the document has no slot of its own
/// (<see cref="DocumentUrlIdentityResolution.ResolveDerivedRelatedRecordAsync"/>).</item>
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

        q.ColumnSet.Columns.Should().Contain("sprk_relatedcommunication", "task 132: the derived record's source");

        q.Criteria.FilterOperator.Should().Be(LogicalOperator.And);
        q.Criteria.Conditions.Should().ContainSingle(c =>
            c.AttributeName == "sprk_isemailarchive" && c.Operator == ConditionOperator.Equal
            && Equals(c.Values.Single(), true));

        // Task 132: the communication is an OUTER link (a pane save has none), read for its message id only.
        var link = q.LinkEntities.Should().ContainSingle().Which;
        link.LinkToEntityName.Should().Be("sprk_communication");
        link.LinkFromAttributeName.Should().Be("sprk_relatedcommunication");
        link.LinkToAttributeName.Should().Be("sprk_communicationid");
        link.JoinOperator.Should().Be(JoinOperator.LeftOuter);
        link.Columns.Columns.Should().BeEmpty();

        var anyKey = q.Criteria.Filters.Should().ContainSingle().Which;
        anyKey.FilterOperator.Should().Be(LogicalOperator.Or);
        anyKey.Conditions.Should().HaveCount(3);
        var onDocument = anyKey.Conditions.Where(c => string.IsNullOrEmpty(c.EntityName)).ToList();
        onDocument.Should().OnlyContain(c => c.AttributeName == "sprk_emailmessageid" && c.Operator == ConditionOperator.Equal);
        onDocument.Select(c => c.Values.Single()).Should().BeEquivalentTo(new object[] { MessageId, ItemId });
        var onCommunication = anyKey.Conditions.Should().ContainSingle(c => c.EntityName == link.EntityAlias).Which;
        onCommunication.AttributeName.Should().Be("sprk_internetmessageid");
        onCommunication.Operator.Should().Be(ConditionOperator.Equal);
        onCommunication.Values.Single().Should().Be(MessageId, "only the RFC id — a communication never holds an item id");

        q.Orders.Should().HaveCount(2);
        q.Orders[0].AttributeName.Should().Be("createdon");
        q.Orders[0].OrderType.Should().Be(OrderType.Descending);
        q.Orders[1].AttributeName.Should().Be("sprk_documentid", "a deterministic tie-break");
    }

    [Fact]
    public async Task Query_WithOnlyTheMessageId_MatchesThatKeyAndTheCommunication()
    {
        Returns();

        await Resolve(new DocumentUrlIdentityResolution.EmailKeys(MessageId, null));

        var anyKey = _captured!.Criteria.Filters.Single();
        anyKey.Conditions.Should().HaveCount(2);
        anyKey.Conditions.Should().OnlyContain(c => Equals(c.Values.Single(), MessageId));
    }

    // ── Task 132: the union, evaluated (one query over both branches) ─────────────────────────
    // These run the CAPTURED query over in-memory rows (InMemoryRows), so they prove what the query selects — the
    // archive rule, the outer link, the OR, the order and the TopCount together — not merely its shape.

    [Fact]
    public async Task CapturedArchive_WithNoMessageIdStamp_ResolvesThroughItsCommunication()
    {
        var communication = Guid.NewGuid();
        var rows = new InMemoryRows();
        rows.Communication(communication, MessageId);
        rows.Document(DocumentId, At(1), communication: communication);

        var identity = await Resolve(rows, new DocumentUrlIdentityResolution.EmailKeys(MessageId, ItemId));

        identity.Should().NotBeNull();
        identity!.DocumentId.Should().Be(DocumentId);
        identity.RelatedRecord.Should().BeNull("a captured archive carries no direct slot");
        identity.RelatedCommunication!.Id.Should().Be(communication);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task KeyMatchAndCommunicationMatch_TheNewerWins(bool communicationMatchIsNewer)
    {
        var communication = Guid.NewGuid();
        var keyMatched = Guid.NewGuid();
        var communicationMatched = Guid.NewGuid();
        var rows = new InMemoryRows();
        rows.Communication(communication, MessageId);
        rows.Document(keyMatched, communicationMatchIsNewer ? At(1) : At(2), messageId: ItemId);
        rows.Document(communicationMatched, communicationMatchIsNewer ? At(2) : At(1), communication: communication);

        var identity = await Resolve(rows, new DocumentUrlIdentityResolution.EmailKeys(MessageId, ItemId));

        identity!.DocumentId.Should().Be(communicationMatchIsNewer ? communicationMatched : keyMatched);
    }

    [Fact]
    public async Task KeyMatchAndCommunicationMatch_AtTheSameCreatedOn_BreakOnTheDocumentId()
    {
        // Differ in the LAST byte only, so .NET's and SQL Server's uniqueidentifier orders agree.
        var lower = Guid.Parse("6fb8c3fa-13c3-f111-a05c-002248291301");
        var higher = Guid.Parse("6fb8c3fa-13c3-f111-a05c-002248291302");
        var communication = Guid.NewGuid();
        var rows = new InMemoryRows();
        rows.Communication(communication, MessageId);
        rows.Document(lower, At(1), communication: communication);
        rows.Document(higher, At(1), messageId: MessageId);

        (await Resolve(rows, new DocumentUrlIdentityResolution.EmailKeys(MessageId, null)))!.DocumentId.Should().Be(higher);

        var swapped = new InMemoryRows();
        swapped.Communication(communication, MessageId);
        swapped.Document(lower, At(1), messageId: MessageId);
        swapped.Document(higher, At(1), communication: communication);

        (await Resolve(swapped, new DocumentUrlIdentityResolution.EmailKeys(MessageId, null)))!.DocumentId.Should().Be(higher);
    }

    [Fact]
    public async Task CommunicationMatch_OnADocumentThatIsNotAnArchive_IsNeverReturned()
    {
        // An attachment child of the captured email: linked to the same communication, but not the .eml archive.
        var communication = Guid.NewGuid();
        var rows = new InMemoryRows();
        rows.Communication(communication, MessageId);
        rows.Document(DocumentId, At(2), communication: communication, isArchive: false);

        (await Resolve(rows, new DocumentUrlIdentityResolution.EmailKeys(MessageId, ItemId))).Should().BeNull();
    }

    [Fact]
    public async Task NeitherBranchMatches_IsNull()
    {
        var communication = Guid.NewGuid();
        var rows = new InMemoryRows();
        rows.Communication(communication, "<another-email@mail.example.com>");
        rows.Document(DocumentId, At(1), communication: communication);
        rows.Document(Guid.NewGuid(), At(2), messageId: "<yet-another@mail.example.com>");
        rows.Document(Guid.NewGuid(), At(3)); // an archive with neither key nor communication

        (await Resolve(rows, new DocumentUrlIdentityResolution.EmailKeys(MessageId, ItemId))).Should().BeNull();
    }

    [Fact]
    public async Task TheExchangeItemId_NeverMatchesThroughTheCommunication()
    {
        var communication = Guid.NewGuid();
        var rows = new InMemoryRows();
        rows.Communication(communication, ItemId);
        rows.Document(DocumentId, At(1), communication: communication);

        (await Resolve(rows, new DocumentUrlIdentityResolution.EmailKeys(MessageId, ItemId))).Should().BeNull();
    }

    // ── Task 132: the record derived through the communication ────────────────────────────────

    [Fact]
    public async Task Derived_WhenTheDocumentHasItsOwnSlot_IsNull_AndTheCommunicationIsNotRead()
    {
        // STRICT with no setup: any read fails the test.
        var identity = new DocumentUrlIdentityResolution.Resolution(
            DocumentId, "n", "f",
            new EntityReference("sprk_project", Guid.NewGuid()),
            new EntityReference("sprk_communication", Guid.NewGuid()));

        (await Derived(identity)).Should().BeNull();
    }

    [Fact]
    public async Task Derived_WithNoCommunication_IsNull_AndNothingIsRead()
    {
        var identity = new DocumentUrlIdentityResolution.Resolution(DocumentId, "n", "f", null);

        (await Derived(identity)).Should().BeNull();
    }

    [Fact]
    public async Task Derived_IsTheCommunicationsFirstRegarding_InTheDirectSlotPriority()
    {
        var communication = Guid.NewGuid();
        var project = new EntityReference("sprk_project", Guid.NewGuid()) { Name = "PRJ-000417" };
        string[]? columns = null;
        _dataverse
            .Setup(d => d.RetrieveAsync("sprk_communication", communication, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .Callback<string, Guid, string[], CancellationToken>((_, _, c, _) => columns = c)
            .ReturnsAsync(new Entity("sprk_communication", communication)
            {
                ["sprk_regardingworkassignment"] = new EntityReference("sprk_workassignment", Guid.NewGuid()),
                ["sprk_regardingproject"] = project,
            });

        var derived = await Derived(new DocumentUrlIdentityResolution.Resolution(
            DocumentId, "n", "f", null, new EntityReference("sprk_communication", communication)));

        derived.Should().BeSameAs(project, "matter > project > invoice > work assignment, as for the document's own slots");
        columns.Should().Equal(
            "sprk_regardingmatter", "sprk_regardingproject", "sprk_regardinginvoice", "sprk_regardingworkassignment");
    }

    [Fact]
    public async Task Derived_WhenTheCommunicationIsFiledToNothing_IsNull()
    {
        var communication = Guid.NewGuid();
        _dataverse
            .Setup(d => d.RetrieveAsync("sprk_communication", communication, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_communication", communication));

        (await Derived(new DocumentUrlIdentityResolution.Resolution(
            DocumentId, "n", "f", null, new EntityReference("sprk_communication", communication)))).Should().BeNull();
    }

    [Fact]
    public async Task Derived_WhenTheCommunicationCannotBeRead_IsNull_Never503()
    {
        var communication = Guid.NewGuid();
        _dataverse
            .Setup(d => d.RetrieveAsync("sprk_communication", communication, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("The request channel timed out while waiting for a reply."));

        (await Derived(new DocumentUrlIdentityResolution.Resolution(
            DocumentId, "n", "f", null, new EntityReference("sprk_communication", communication)))).Should().BeNull();
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

    private Task<DocumentUrlIdentityResolution.Resolution?> Resolve(InMemoryRows rows, DocumentUrlIdentityResolution.EmailKeys keys)
    {
        _dataverse
            .Setup(d => d.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((QueryExpression q, CancellationToken _) => rows.Run(q));
        return Resolve(keys);
    }

    private Task<EntityReference?> Derived(DocumentUrlIdentityResolution.Resolution identity)
        => DocumentUrlIdentityResolution.ResolveDerivedRelatedRecordAsync(identity, _dataverse.Object, NullLogger.Instance, default);

    private static DateTime At(int day) => new(2026, 10, day, 9, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// Task 132: <c>sprk_document</c> and <c>sprk_communication</c> rows, and a reading of a <see cref="QueryExpression"/>
    /// over them with Dataverse's semantics for what <c>ResolveByEmailMessageIdAsync</c> uses: Equal conditions, And/Or
    /// filters, a condition on a link entity's alias, an outer or inner link on a lookup, ordering and TopCount. Anything
    /// else fails the test rather than being guessed at.
    /// </summary>
    private sealed class InMemoryRows
    {
        private readonly List<Entity> _documents = [];
        private readonly Dictionary<Guid, Entity> _communications = [];

        public void Communication(Guid id, string internetMessageId)
            => _communications[id] = new Entity("sprk_communication", id) { ["sprk_internetmessageid"] = internetMessageId };

        public void Document(Guid id, DateTime createdOn, string? messageId = null, Guid? communication = null, bool isArchive = true)
        {
            var row = Row(id);
            row["createdon"] = createdOn;
            row["sprk_isemailarchive"] = isArchive;
            if (messageId is not null)
                row["sprk_emailmessageid"] = messageId;
            if (communication is { } c)
                row["sprk_relatedcommunication"] = new EntityReference("sprk_communication", c);
            _documents.Add(row);
        }

        public EntityCollection Run(QueryExpression query)
        {
            query.EntityName.Should().Be("sprk_document");
            var link = query.LinkEntities.SingleOrDefault();

            var matches = _documents
                .Select(d => (Document: d, Linked: Linked(d, link)))
                .Where(r => link is null || link.JoinOperator == JoinOperator.LeftOuter || r.Linked is not null)
                .Where(r => Holds(query.Criteria, r.Document, r.Linked, link))
                .Select(r => r.Document);

            IOrderedEnumerable<Entity>? ordered = null;
            foreach (var order in query.Orders)
            {
                Func<Entity, object?> key = e => order.AttributeName == "sprk_documentid" ? e.Id : e.GetAttributeValue<object>(order.AttributeName);
                ordered = (ordered, order.OrderType) switch
                {
                    (null, OrderType.Descending) => matches.OrderByDescending(key),
                    (null, _) => matches.OrderBy(key),
                    (_, OrderType.Descending) => ordered.ThenByDescending(key),
                    _ => ordered.ThenBy(key),
                };
            }

            var result = (ordered ?? matches).AsEnumerable();
            if (query.TopCount is { } top)
                result = result.Take(top);
            return new EntityCollection(result.ToList());
        }

        private Entity? Linked(Entity document, LinkEntity? link)
        {
            if (link is null)
                return null;
            link.LinkToEntityName.Should().Be("sprk_communication");
            link.LinkToAttributeName.Should().Be("sprk_communicationid");
            return document.GetAttributeValue<EntityReference>(link.LinkFromAttributeName) is { } reference
                   && _communications.TryGetValue(reference.Id, out var communication)
                ? communication
                : null;
        }

        private static bool Holds(FilterExpression filter, Entity document, Entity? linked, LinkEntity? link)
        {
            var results = filter.Conditions.Select(c => Holds(c, document, linked, link))
                .Concat(filter.Filters.Select(f => Holds(f, document, linked, link)))
                .ToList();
            return filter.FilterOperator == LogicalOperator.And ? results.All(r => r) : results.Any(r => r);
        }

        private static bool Holds(ConditionExpression condition, Entity document, Entity? linked, LinkEntity? link)
        {
            condition.Operator.Should().Be(ConditionOperator.Equal);
            Entity? source = document;
            if (!string.IsNullOrEmpty(condition.EntityName))
            {
                condition.EntityName.Should().Be(link?.EntityAlias, "a condition on a joined table names its alias");
                source = linked;
            }

            return source is not null
                && source.Contains(condition.AttributeName)
                && Equals(source[condition.AttributeName], condition.Values.Single());
        }
    }

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
