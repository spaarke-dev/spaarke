using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Core.Auth;
using Spaarke.Core.Auth.Rules;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Models.Ai.SemanticSearch;
using Sprk.Bff.Api.Services.Ai.SemanticSearch;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Models;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Finance;

/// <summary>
/// <see cref="InvoiceRelatedWorkService"/> (ebilling-r1 task 107, FR-34): the candidate set is the line's own core record, the line's
/// timekeeper's linked contact and the settings window; every candidate is put to the caller's own Read before it is searched or returned;
/// the search runs in documentIds scope over the readable ids, narrative only; the empty states carry their reason.
/// </summary>
/// <remarks>
/// The Dataverse seam is <see cref="IGenericEntityService.RetrieveMultipleAsync(QueryExpression, CancellationToken)"/>. The fake EVALUATES the
/// query's conditions against fixture rows (equality, the date window, the active filter, ordering, TopCount), so the scoping tests prove what the
/// service asked for, not what a hand-written answer happens to contain: a document of another core record or another author is in the fixture and
/// is excluded only if the query excludes it.
/// </remarks>
public class InvoiceRelatedWorkServiceTests
{
    private static readonly Guid InvoiceId = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid LineId = Guid.Parse("00000000-0000-0000-0000-0000000001a1");
    private static readonly Guid TimekeeperId = Guid.Parse("00000000-0000-0000-0000-00000000aa01");
    private static readonly Guid ContactId = Guid.Parse("00000000-0000-0000-0000-00000000c001");
    private static readonly Guid OtherContactId = Guid.Parse("00000000-0000-0000-0000-00000000c002");
    private static readonly Guid MatterId = Guid.Parse("00000000-0000-0000-0000-0000000000b1");
    private static readonly Guid OtherMatterId = Guid.Parse("00000000-0000-0000-0000-0000000000b2");
    private static readonly Guid ProjectId = Guid.Parse("00000000-0000-0000-0000-0000000000c1");

    private const string Narrative = "Reviewed and revised the commercial lease amendment for the Springfield premises";
    private const string Tenant = "tenant-1";
    private static readonly DateTime LineDate = new(2026, 3, 10, 0, 0, 0, DateTimeKind.Utc);

    private static readonly InvoiceContextService.InvoiceCaller Caller = new("caller-oid", "caller-token");

    private static Guid Doc(int n) => Guid.Parse($"00000000-0000-0000-0000-0000000d{n:0000}");

    // =========================================================================================
    // Harness
    // =========================================================================================

    private sealed class Harness
    {
        public Entity? Line = FeeLine();
        public Entity? TimekeeperRow = Timekeeper(ContactId);
        public List<Entity> Documents = [];
        public int WindowDays = 7;

        public List<QueryExpression> Queries { get; } = [];
        public List<SemanticSearchRequest> SearchRequests { get; } = [];
        public List<string> SearchTenants { get; } = [];
        public Dictionary<Guid, double> Scores { get; } = new();
        public Func<SemanticSearchRequest, Task<SemanticSearchResponse>>? SearchOverride { get; set; }
        public Access Rights { get; } = new();
        public ListLogger Log { get; } = new();

        public Mock<IGenericEntityService> Entities { get; } = new(MockBehavior.Strict);
        public Mock<IEbillingSettingsReader> Settings { get; } = new(MockBehavior.Strict);
        public Mock<ISemanticSearchService> Search { get; } = new(MockBehavior.Strict);

        public Harness()
        {
            Entities
                .Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
                .Returns((QueryExpression q, CancellationToken _) => Task.FromResult(Answer(q)));
            Settings
                .Setup(s => s.GetAsync(It.IsAny<CancellationToken>()))
                .Returns(() => Task.FromResult(new EbillingSettings { RelatedWorkWindowDays = WindowDays }));
            Search
                .Setup(s => s.SearchAsync(It.IsAny<SemanticSearchRequest>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((SemanticSearchRequest r, string tenant, CancellationToken _) =>
                {
                    SearchRequests.Add(r);
                    SearchTenants.Add(tenant);
                    return SearchOverride is not null ? SearchOverride(r) : Task.FromResult(ResponseFor(r));
                });
        }

        public InvoiceRelatedWorkService Service()
        {
            var authorization = new AuthorizationService(
                Rights, [new OperationAccessRule(NullLogger<OperationAccessRule>.Instance)], NullLogger<AuthorizationService>.Instance);
            return new InvoiceRelatedWorkService(Entities.Object, Settings.Object, authorization, Search.Object, Log);
        }

        public Task<RelatedWorkDto?> GetAsync(InvoiceContextService.InvoiceCaller? caller = null, string? tenant = Tenant) =>
            Service().GetAsync(InvoiceId, LineId, caller ?? Caller, tenant);

        /// <summary>What the real search does: only documents it was told about, scored.</summary>
        private SemanticSearchResponse ResponseFor(SemanticSearchRequest request)
        {
            var results = request.DocumentIds!
                .Select(Guid.Parse)
                .Where(Scores.ContainsKey)
                .Select(id => new SearchResult { DocumentId = id.ToString(), CombinedScore = Scores[id] })
                .ToList();
            return new SemanticSearchResponse { Results = results, Metadata = new SearchMetadata { ReturnedResults = results.Count } };
        }

        private EntityCollection Answer(QueryExpression query)
        {
            lock (Queries)
            {
                Queries.Add(query);
            }

            IEnumerable<Entity> source = query.EntityName switch
            {
                "sprk_billingevent" => Line is null ? [] : [Line],
                "sprk_timekeeper" => TimekeeperRow is null ? [] : [TimekeeperRow],
                "sprk_document" => Documents,
                _ => throw new InvalidOperationException($"Unexpected query on {query.EntityName}."),
            };

            var rows = source.Where(e => Matches(e, query.Criteria)).ToList();
            foreach (var order in query.Orders.Reverse())
            {
                rows = (order.OrderType == OrderType.Descending
                    ? rows.OrderByDescending(e => Sort(e, order.AttributeName))
                    : rows.OrderBy(e => Sort(e, order.AttributeName))).ToList();
            }

            if (query.TopCount is { } top)
            {
                rows = rows.Take(top).ToList();
            }

            return new EntityCollection(rows);
        }

        private static IComparable Sort(Entity e, string attribute) =>
            attribute == "sprk_documentid" ? e.Id : (IComparable)e.GetAttributeValue<DateTime>(attribute);

        private static bool Matches(Entity row, FilterExpression filter) =>
            filter.Conditions.All(c => Matches(row, c)) && filter.Filters.All(f => Matches(row, f));

        private static bool Matches(Entity row, ConditionExpression c)
        {
            var value = c.AttributeName switch
            {
                "sprk_billingeventid" or "sprk_timekeeperid" or "sprk_documentid" => (object?)row.Id,
                _ => row.Contains(c.AttributeName) ? row[c.AttributeName] : null,
            };
            var expected = c.Values[0];
            switch (c.Operator)
            {
                case ConditionOperator.Equal:
                    return value switch
                    {
                        EntityReference r => expected is Guid g && r.Id == g,
                        OptionSetValue o => expected is int i && o.Value == i,
                        null => false,
                        _ => value.Equals(expected),
                    };
                case ConditionOperator.GreaterEqual:
                    return value is DateTime d1 && d1 >= (DateTime)expected;
                case ConditionOperator.LessThan:
                    return value is DateTime d2 && d2 < (DateTime)expected;
                default:
                    throw new InvalidOperationException($"Operator {c.Operator} is not modelled.");
            }
        }

        public IEnumerable<ConditionExpression> DocumentConditions() =>
            Queries.Single(q => q.EntityName == "sprk_document").Criteria.Conditions;
    }

    private static Entity FeeLine(
        int costType = (int)InvoiceLineCostType.Fee, Guid? timekeeper = null, Guid? matter = null, Guid? project = null,
        DateTime? eventDate = null, string? narrative = Narrative, Guid? invoice = null)
    {
        var line = new Entity("sprk_billingevent", LineId);
        line["sprk_invoice"] = new EntityReference("sprk_invoice", invoice ?? InvoiceId);
        line["sprk_costtype"] = new OptionSetValue(costType);
        line["sprk_resolvedtimekeeper"] = new EntityReference("sprk_timekeeper", timekeeper ?? TimekeeperId);
        if (project is null)
        {
            line["sprk_matter"] = new EntityReference("sprk_matter", matter ?? MatterId);
        }

        if (project is { } p)
        {
            line["sprk_project"] = new EntityReference("sprk_project", p);
        }

        line["sprk_eventdate"] = eventDate ?? LineDate;
        line["sprk_description"] = narrative;
        return line;
    }

    private static Entity Timekeeper(Guid? contact)
    {
        var row = new Entity("sprk_timekeeper", TimekeeperId);
        if (contact is { } c)
        {
            row["sprk_contact"] = new EntityReference("contact", c);
        }

        return row;
    }

    private static Entity Document(
        Guid id, string name, DateTime createdOn, Guid? matter = null, Guid? contact = null, int stateCode = 0, Guid? project = null)
    {
        var d = new Entity("sprk_document", id);
        d["sprk_documentname"] = name;
        d["createdon"] = createdOn;
        d["statecode"] = new OptionSetValue(stateCode);
        if (project is { } p)
        {
            d["sprk_project"] = new EntityReference("sprk_project", p);
        }
        else
        {
            d["sprk_matter"] = new EntityReference("sprk_matter", matter ?? MatterId);
        }

        d["sprk_createdbycontact"] = new EntityReference("contact", contact ?? ContactId);
        return d;
    }

    private static DateTime At(int day, int hour = 12) => new(2026, 3, day, hour, 0, 0, DateTimeKind.Utc);

    private static Harness WithDocuments(params (int N, double? Score)[] docs)
    {
        var h = new Harness();
        foreach (var (n, score) in docs)
        {
            h.Documents.Add(Document(Doc(n), $"Doc {n}", At(10)));
            if (score is { } s)
            {
                h.Scores[Doc(n)] = s;
            }
        }

        return h;
    }

    // =========================================================================================
    // The happy path and ranking
    // =========================================================================================

    [Fact]
    public async Task ReturnsReadableCandidates_OrderedBySearchScore_WithTheirDetails()
    {
        var h = WithDocuments((1, 0.20), (2, 0.90), (3, 0.55));

        var result = (await h.GetAsync())!;

        result.Status.Should().Be(RelatedWorkStatus.Available);
        result.Reason.Should().BeNull();
        result.WindowDays.Should().Be(7);
        result.Items.Select(i => i.DocumentId).Should().Equal(new[] { Doc(2), Doc(3), Doc(1) }, "ordered by the search's score, not by date or name");
        result.Items[0].Should().Be(new RelatedWorkItem(Doc(2), "Doc 2", At(10), 0.90));
    }

    [Fact]
    public async Task OnlyTheTopResultsAreReturned()
    {
        var h = new Harness();
        for (var n = 1; n <= 15; n++)
        {
            h.Documents.Add(Document(Doc(n), $"Doc {n}", At(10)));
            h.Scores[Doc(n)] = n / 100.0;
        }

        var result = (await h.GetAsync())!;

        result.Items.Should().HaveCount(InvoiceRelatedWorkService.MaxResults);
        result.Items.Select(i => i.Score).Should().BeInDescendingOrder();
        result.Items[0].DocumentId.Should().Be(Doc(15));
    }

    [Fact]
    public async Task SearchGetsOnlyTheNarrative_InDocumentIdsScope_ForTheCallersTenant()
    {
        var h = WithDocuments((1, 0.5), (2, 0.4));

        await h.GetAsync();

        var request = h.SearchRequests.Should().ContainSingle().Subject;
        request.Query.Should().Be(Narrative);
        request.Scope.Should().Be(SearchScope.DocumentIds);
        request.DocumentIds.Should().BeEquivalentTo(new[] { Doc(1).ToString(), Doc(2).ToString() });
        request.EntityType.Should().BeNull();
        request.EntityId.Should().BeNull();
        request.Filters.Should().BeNull();
        request.Options!.IncludeHighlights.Should().BeFalse("no document text is needed or returned");
        h.SearchTenants.Should().Equal(Tenant);
    }

    [Fact]
    public async Task ANarrativeLongerThanTheSearchContractAllows_IsCutNotRefused()
    {
        var h = WithDocuments((1, 0.5));
        h.Line = FeeLine(narrative: new string('x', 2500));

        var result = (await h.GetAsync())!;

        result.Status.Should().Be(RelatedWorkStatus.Available);
        h.SearchRequests.Single().Query!.Length.Should().Be(1000);
    }

    [Fact]
    public async Task AChunkedDocument_AppearsOnceWithItsBestScore()
    {
        var h = WithDocuments((1, null), (2, 0.3));
        h.SearchOverride = r => Task.FromResult(new SemanticSearchResponse
        {
            Results =
            [
                new SearchResult { DocumentId = Doc(1).ToString(), CombinedScore = 0.2 },
                new SearchResult { DocumentId = Doc(2).ToString(), CombinedScore = 0.3 },
                new SearchResult { DocumentId = Doc(1).ToString(), CombinedScore = 0.8 },
            ],
            Metadata = new SearchMetadata(),
        });

        var result = (await h.GetAsync())!;

        result.Items.Select(i => (i.DocumentId, i.Score)).Should().Equal((Doc(1), 0.8), (Doc(2), 0.3));
    }

    // =========================================================================================
    // Scope: the line's own core record, its timekeeper's contact, the window
    // =========================================================================================

    [Fact]
    public async Task ADocumentBySameAuthorInTheWindowOnAnotherCoreRecord_IsNeverReturned_Searched_OrChecked()
    {
        var h = WithDocuments((1, 0.5));
        h.Documents.Add(Document(Doc(2), "Other matter", At(10), matter: OtherMatterId));
        h.Documents.Add(Document(Doc(3), "A project", At(10), project: ProjectId));
        h.Scores[Doc(2)] = 0.99;
        h.Scores[Doc(3)] = 0.98;

        var result = (await h.GetAsync())!;

        result.Items.Select(i => i.DocumentId).Should().Equal(Doc(1));
        h.SearchRequests.Single().DocumentIds.Should().Equal(Doc(1).ToString());
        h.Rights.Calls.Select(c => c.Id).Should().Equal(new[] { Doc(1) }, "documents of other core records are not even asked about");
    }

    [Fact]
    public async Task TheCoreRecordIsTheLinesOwn_AProjectLineLooksOnlyUnderItsProject()
    {
        var h = new Harness { Line = FeeLine(project: ProjectId) };
        h.Documents.Add(Document(Doc(1), "On the project", At(10), project: ProjectId));
        h.Documents.Add(Document(Doc(2), "On a matter", At(10)));
        h.Scores[Doc(1)] = 0.5;
        h.Scores[Doc(2)] = 0.9;

        var result = (await h.GetAsync())!;

        result.Items.Select(i => i.DocumentId).Should().Equal(Doc(1));
        h.DocumentConditions().Should().Contain(c => c.AttributeName == "sprk_project");
        h.DocumentConditions().Should().NotContain(c => c.AttributeName == "sprk_matter");
    }

    [Fact]
    public async Task ADocumentByAnotherAuthor_IsNotACandidate()
    {
        var h = WithDocuments((1, 0.5));
        h.Documents.Add(Document(Doc(2), "Someone else's", At(10), contact: OtherContactId));
        h.Scores[Doc(2)] = 0.9;

        var result = (await h.GetAsync())!;

        result.Items.Select(i => i.DocumentId).Should().Equal(Doc(1));
        h.Rights.Calls.Select(c => c.Id).Should().Equal(Doc(1));
    }

    [Fact]
    public async Task AnInactiveDocument_IsNotACandidate()
    {
        var h = WithDocuments((1, 0.5));
        h.Documents.Add(Document(Doc(2), "Deleted", At(10), stateCode: 1));
        h.Scores[Doc(2)] = 0.9;

        var result = (await h.GetAsync())!;

        result.Items.Select(i => i.DocumentId).Should().Equal(Doc(1));
    }

    [Fact]
    public async Task TheWindowIsPlusOrMinusTheSettingsDays_BothEdgesInclusive_WholeUtcDays()
    {
        var h = new Harness { WindowDays = 7 };
        var lastTick = At(18, 0).AddTicks(-1); // 2026-03-17 23:59:59.9999999 is still day +7
        var cases = new (int N, DateTime At, bool In)[]
        {
            (1, At(3, 0), true),                        // first instant of day -7
            (2, At(3, 0).AddTicks(-1), false),          // last instant of day -8
            (3, lastTick, true),                        // last instant of day +7
            (4, At(18, 0), false),                      // first instant of day +8
            (5, At(10), true),                          // the line date itself
        };
        foreach (var c in cases)
        {
            h.Documents.Add(Document(Doc(c.N), $"Doc {c.N}", c.At));
            h.Scores[Doc(c.N)] = 0.5 + c.N / 100.0;
        }

        var result = (await h.GetAsync())!;

        result.Items.Select(i => i.DocumentId).Should().BeEquivalentTo(new[] { Doc(1), Doc(3), Doc(5) });
        result.WindowDays.Should().Be(7);
    }

    [Theory]
    [InlineData(0, 10, true)]
    [InlineData(0, 11, false)]
    [InlineData(0, 9, false)]
    [InlineData(2, 12, true)]
    [InlineData(2, 13, false)]
    public async Task TheWindowComesFromTheSettings(int windowDays, int day, bool expectedIn)
    {
        var h = new Harness { WindowDays = windowDays };
        h.Documents.Add(Document(Doc(1), "Doc", At(day)));
        h.Scores[Doc(1)] = 0.5;

        var result = (await h.GetAsync())!;

        (result.Items.Count == 1).Should().Be(expectedIn);
        result.WindowDays.Should().Be(windowDays);
    }

    [Fact]
    public async Task AWindowBeyondTheSettingsMaximum_IsClamped()
    {
        var h = new Harness { WindowDays = 4000 };
        h.Documents.Add(Document(Doc(1), "Doc", LineDate.AddDays(91)));
        h.Scores[Doc(1)] = 0.5;

        var result = (await h.GetAsync())!;

        result.WindowDays.Should().Be(InvoiceRelatedWorkService.MaxWindowDays);
        result.Status.Should().Be(RelatedWorkStatus.Empty);
    }

    [Fact]
    public async Task MoreCandidatesThanTheCap_AreCutToTheMostRecent_AndSaid()
    {
        var h = new Harness { WindowDays = 30 };
        for (var n = 1; n <= InvoiceRelatedWorkService.MaxCandidates + 5; n++)
        {
            h.Documents.Add(Document(Doc(n), $"Doc {n}", At(10).AddMinutes(n)));
            h.Scores[Doc(n)] = n / 1000.0;
        }

        var result = (await h.GetAsync())!;

        result.Truncated.Should().BeTrue();
        h.SearchRequests.Single().DocumentIds!.Count.Should().Be(InvoiceRelatedWorkService.MaxCandidates);
        h.SearchRequests.Single().DocumentIds.Should().NotContain(Doc(1).ToString(), "the oldest readable candidates are the ones cut");
    }

    [Fact]
    public async Task ARunOfRestrictedNewDocuments_DoesNotHideTheReadableOlderOnes()
    {
        // 60 candidates, the 50 newest restricted: the old single capped read saw only the restricted ones and answered "nothing".
        var h = new Harness { WindowDays = 30 };
        for (var n = 1; n <= 60; n++)
        {
            h.Documents.Add(Document(Doc(n), $"Doc {n}", At(10).AddMinutes(n)));
            h.Scores[Doc(n)] = n / 1000.0;
        }

        h.Rights.OnlyReadable(Enumerable.Range(1, 10).Select(Doc).ToArray());

        var result = (await h.GetAsync())!;

        result.Status.Should().Be(RelatedWorkStatus.Available);
        result.Items.Select(i => i.DocumentId).Should().BeEquivalentTo(Enumerable.Range(1, 10).Select(Doc));
        result.Truncated.Should().BeFalse("only readable documents can say there are more");
        h.SearchRequests.Single().DocumentIds!.Count.Should().Be(10);
    }

    [Fact]
    public async Task ManyUnreadableDocumentsAndOneReadable_BeyondTheCap_IsNotTruncated()
    {
        // 51 candidates (one more than the cap) of which only the oldest is readable: truncated would reveal the 50 restricted ones.
        var h = new Harness { WindowDays = 30 };
        for (var n = 1; n <= InvoiceRelatedWorkService.MaxCandidates + 1; n++)
        {
            h.Documents.Add(Document(Doc(n), $"Doc {n}", At(10).AddMinutes(n)));
            h.Scores[Doc(n)] = n / 1000.0;
        }

        h.Rights.OnlyReadable(Doc(1));

        var result = (await h.GetAsync())!;

        result.Truncated.Should().BeFalse();
        result.Items.Select(i => i.DocumentId).Should().Equal(Doc(1));
    }

    [Fact]
    public async Task AnExhaustedCheckBudget_AnswersLikeAnEmptyWindow_NeverRevealingWhatWasNotReached()
    {
        // The 200 newest are restricted and 30 readable ones lie behind them: the budget runs out on the restricted ones.
        var h = new Harness { WindowDays = 30 };
        for (var n = 1; n <= InvoiceRelatedWorkService.MaxCandidateChecks + 30; n++)
        {
            h.Documents.Add(Document(Doc(n), $"Doc {n}", At(10).AddMinutes(n)));
            h.Scores[Doc(n)] = 0.5;
        }

        h.Rights.OnlyReadable(Enumerable.Range(1, 30).Select(Doc).ToArray());

        var result = (await h.GetAsync())!;
        var empty = (await new Harness { WindowDays = 30 }.GetAsync())!;

        result.Should().BeEquivalentTo(empty);
        result.Truncated.Should().BeFalse();
        h.Rights.Calls.Count.Should().BeLessThanOrEqualTo(InvoiceRelatedWorkService.MaxCandidateChecks);
        h.SearchRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task LongDocumentsTakingEveryChunk_DoNotCrowdOutTheRest_TheUnrankedAreSearchedAgain()
    {
        var h = new Harness { WindowDays = 30 };
        var longOnes = new HashSet<string> { Doc(1).ToString(), Doc(2).ToString(), Doc(3).ToString() };
        for (var n = 1; n <= 15; n++)
        {
            h.Documents.Add(Document(Doc(n), $"Doc {n}", At(10).AddMinutes(n)));
            h.Scores[Doc(n)] = n <= 3 ? 0.9 : 0.1 + n / 1000.0;
        }

        // Three briefs fill every chunk of the limit, so a search that includes them returns only them.
        h.SearchOverride = request =>
        {
            var ids = request.DocumentIds!;
            var dominant = ids.Where(longOnes.Contains).ToList();
            var results = (dominant.Count > 0 ? dominant : ids)
                .Select(id => new SearchResult { DocumentId = id, CombinedScore = h.Scores[Guid.Parse(id)] })
                .ToList();
            return Task.FromResult(new SemanticSearchResponse { Results = results, Metadata = new SearchMetadata { ReturnedResults = results.Count } });
        };

        var result = (await h.GetAsync())!;

        result.Items.Should().HaveCount(InvoiceRelatedWorkService.MaxResults);
        result.Items.Select(i => i.DocumentId).Take(3).Should().BeEquivalentTo(new[] { Doc(1), Doc(2), Doc(3) });
        h.SearchRequests.Should().HaveCount(2);
        h.SearchRequests[1].DocumentIds.Should().NotContain(longOnes, "the second search is over the readable documents not yet ranked");
    }

    [Fact]
    public async Task TheExtraSearches_AreCapped()
    {
        var h = new Harness { WindowDays = 30 };
        for (var n = 1; n <= 15; n++)
        {
            h.Documents.Add(Document(Doc(n), $"Doc {n}", At(10).AddMinutes(n)));
        }

        // Every search ranks exactly one new document.
        h.SearchOverride = request =>
        {
            var results = new List<SearchResult> { new() { DocumentId = request.DocumentIds![0], CombinedScore = 0.5 } };
            return Task.FromResult(new SemanticSearchResponse { Results = results, Metadata = new SearchMetadata { ReturnedResults = 1 } });
        };

        var result = (await h.GetAsync())!;

        h.SearchRequests.Should().HaveCount(1 + InvoiceRelatedWorkService.MaxExtraSearches);
        result.Items.Should().HaveCount(1 + InvoiceRelatedWorkService.MaxExtraSearches);
    }

    [Fact]
    public async Task ReadableDocumentsTheIndexDoesNotHold_AreNotSearchedForAgainAndAgain()
    {
        // Two readable, one indexed: the second search (for the unranked one) finds nothing new and ends the loop.
        var h = WithDocuments((1, 0.5), (2, null));

        var result = (await h.GetAsync())!;

        result.Items.Select(i => i.DocumentId).Should().Equal(Doc(1));
        h.SearchRequests.Should().HaveCount(2);
    }

    // =========================================================================================
    // The line must belong to the invoice
    // =========================================================================================

    [Fact]
    public async Task ALineOfAnotherInvoice_IsNull_AndNothingElseIsRead()
    {
        var h = WithDocuments((1, 0.5));
        h.Line = FeeLine(invoice: Guid.NewGuid());

        var result = await h.GetAsync();

        result.Should().BeNull();
        h.Queries.Should().ContainSingle().Which.EntityName.Should().Be("sprk_billingevent");
        h.Rights.Calls.Should().BeEmpty();
        h.SearchRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task AnUnknownLine_IsNull()
    {
        var h = new Harness { Line = null };

        (await h.GetAsync()).Should().BeNull();
    }

    // =========================================================================================
    // Documents the CALLER cannot read
    // =========================================================================================

    [Fact]
    public async Task ACandidateTheCallerCannotRead_IsOmitted_NeverSearchedNorReturned()
    {
        var h = WithDocuments((1, 0.5), (2, 0.9), (3, 0.7));
        h.Rights.OnlyReadable(Doc(1), Doc(3));

        var result = (await h.GetAsync())!;

        result.Items.Select(i => i.DocumentId).Should().Equal(Doc(3), Doc(1));
        h.SearchRequests.Single().DocumentIds.Should().BeEquivalentTo(new[] { Doc(1).ToString(), Doc(3).ToString() });
        JsonSerializer.Serialize(result).Should().NotContain(Doc(2).ToString()).And.NotContain("Doc 2");
    }

    [Fact]
    public async Task EveryCandidateIsPutToTheCallersOwnReadOnDocuments_WithTheCallersToken()
    {
        var h = WithDocuments((1, 0.5), (2, 0.6));

        await h.GetAsync();

        h.Rights.Calls.Should().BeEquivalentTo(new[]
        {
            ("caller-oid", "sprk_documents", Doc(1), (string?)"caller-token"),
            ("caller-oid", "sprk_documents", Doc(2), (string?)"caller-token"),
        });
    }

    [Fact]
    public async Task OnlyWriteOnADocument_IsNotRead()
    {
        var h = WithDocuments((1, 0.5));
        h.Rights.Rights = AccessRights.Write | AccessRights.Append;

        var result = (await h.GetAsync())!;

        result.Items.Should().BeEmpty();
        h.SearchRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task EveryCandidateUnreadable_AnswersExactlyLikeAnEmptyWindow_AndSearchesNothing()
    {
        var unreadable = WithDocuments((1, 0.5), (2, 0.9));
        unreadable.Rights.OnlyReadable();
        var empty = new Harness();

        var a = (await unreadable.GetAsync())!;
        var b = (await empty.GetAsync())!;

        a.Should().BeEquivalentTo(b);
        a.Status.Should().Be(RelatedWorkStatus.Empty);
        a.Reason.Should().Be(RelatedWorkReason.NoneInWindow);
        unreadable.SearchRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task UnreadableCandidatesBeyondTheCap_DoNotRevealThemselvesThroughTheTruncationFlag()
    {
        var h = new Harness { WindowDays = 30 };
        for (var n = 1; n <= InvoiceRelatedWorkService.MaxCandidates + 5; n++)
        {
            h.Documents.Add(Document(Doc(n), $"Doc {n}", At(10).AddMinutes(n)));
        }

        h.Rights.OnlyReadable();

        var result = (await h.GetAsync())!;

        result.Truncated.Should().BeFalse();
        result.Reason.Should().Be(RelatedWorkReason.NoneInWindow);
    }

    [Fact]
    public async Task ARightsLookupThatThrows_MakesTheAnswerUnavailable_NothingIsShownOrSearched()
    {
        var h = WithDocuments((1, 0.5), (2, 0.9));
        h.Rights.ThrowFor(Doc(2));

        var result = (await h.GetAsync())!;

        AssertUnavailable(result, InvoiceRelatedWorkService.CodeAccessCheckFailed);
        h.SearchRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task ARightsLookupThatFaultedWithoutGrantingRead_MakesTheAnswerUnavailable()
    {
        var h = WithDocuments((1, 0.5), (2, 0.9));
        h.Rights.FaultFor(Doc(1));

        var result = (await h.GetAsync())!;

        AssertUnavailable(result, InvoiceRelatedWorkService.CodeAccessCheckFailed);
        h.SearchRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task AFaultedLookupThatDidGrantRead_TheDegradedProbe_CountsAsReadable()
    {
        var h = WithDocuments((1, 0.5));
        h.Rights.FaultedButGranted.Add(Doc(1));

        var result = (await h.GetAsync())!;

        result.Items.Select(i => i.DocumentId).Should().Equal(Doc(1));
    }

    [Theory]
    [InlineData("caller-oid", null)]
    [InlineData("caller-oid", "  ")]
    [InlineData("", "caller-token")]
    public async Task NoCallerIdentityOrToken_ShowsNothing_AndAsksNobody(string userId, string? token)
    {
        var h = WithDocuments((1, 0.5));

        var result = (await h.GetAsync(new InvoiceContextService.InvoiceCaller(userId, token)))!;

        AssertUnavailable(result, InvoiceRelatedWorkService.CodeAccessCheckFailed);
        h.Rights.Calls.Should().BeEmpty();
        h.SearchRequests.Should().BeEmpty();
    }

    // =========================================================================================
    // Empty states, each with its reason
    // =========================================================================================

    [Fact]
    public async Task AnExpenseLine_IsEmpty_NotAFeeLine_AndLooksNowhere()
    {
        var h = WithDocuments((1, 0.5));
        h.Line = FeeLine(costType: (int)InvoiceLineCostType.Expense);

        var result = (await h.GetAsync())!;

        AssertEmpty(result, RelatedWorkReason.NotAFeeLine);
        h.Queries.Select(q => q.EntityName).Should().Equal("sprk_billingevent");
        h.SearchRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task ALineWithNoMatchedTimekeeper_IsEmpty_NoTimekeeper()
    {
        var h = WithDocuments((1, 0.5));
        h.Line!.Attributes.Remove("sprk_resolvedtimekeeper");

        var result = (await h.GetAsync())!;

        AssertEmpty(result, RelatedWorkReason.NoTimekeeper);
        h.Queries.Select(q => q.EntityName).Should().NotContain("sprk_document");
    }

    [Fact]
    public async Task ATimekeeperWithNoLinkedContact_IsEmpty_NoLinkedContact_AndNeverMatchesAuthorlessDocuments()
    {
        var h = WithDocuments((1, 0.5));
        h.TimekeeperRow = Timekeeper(null);
        h.Documents[0].Attributes.Remove("sprk_createdbycontact");

        var result = (await h.GetAsync())!;

        AssertEmpty(result, RelatedWorkReason.NoLinkedContact);
        h.Queries.Select(q => q.EntityName).Should().NotContain("sprk_document", "a null contact must never become a match-every-document filter");
    }

    [Fact]
    public async Task AVanishedTimekeeperRow_IsEmpty_NoLinkedContact()
    {
        var h = WithDocuments((1, 0.5));
        h.TimekeeperRow = null;

        AssertEmpty((await h.GetAsync())!, RelatedWorkReason.NoLinkedContact);
    }

    [Fact]
    public async Task NoCandidatesInTheWindow_IsEmpty_NoneInWindow()
    {
        var h = new Harness();
        h.Documents.Add(Document(Doc(1), "Far away", At(10).AddDays(60)));

        var result = (await h.GetAsync())!;

        AssertEmpty(result, RelatedWorkReason.NoneInWindow);
        result.WindowDays.Should().Be(7);
        h.Rights.Calls.Should().BeEmpty();
        h.SearchRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task ALineWithNoCoreRecord_OrTwo_IsEmpty_NoCoreRecord()
    {
        var none = WithDocuments((1, 0.5));
        none.Line!.Attributes.Remove("sprk_matter");
        var two = WithDocuments((1, 0.5));
        two.Line!["sprk_project"] = new EntityReference("sprk_project", ProjectId);

        AssertEmpty((await none.GetAsync())!, RelatedWorkReason.NoCoreRecord);
        AssertEmpty((await two.GetAsync())!, RelatedWorkReason.NoCoreRecord);
        none.Queries.Concat(two.Queries).Select(q => q.EntityName).Should().NotContain("sprk_document");
    }

    [Fact]
    public async Task ALineWithNoDate_IsEmpty_NoLineDate()
    {
        var h = WithDocuments((1, 0.5));
        h.Line!.Attributes.Remove("sprk_eventdate");

        AssertEmpty((await h.GetAsync())!, RelatedWorkReason.NoLineDate);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task ALineWithNoNarrative_IsEmpty_NoNarrative_NothingToRankBy(string? narrative)
    {
        var h = WithDocuments((1, 0.5));
        h.Line = FeeLine(narrative: narrative);

        AssertEmpty((await h.GetAsync())!, RelatedWorkReason.NoNarrative);
        h.SearchRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task ReadableCandidatesNoneOfWhichTheIndexReturns_IsEmpty_NoneIndexed()
    {
        var h = WithDocuments((1, null), (2, null));

        var result = (await h.GetAsync())!;

        AssertEmpty(result, RelatedWorkReason.NoneIndexed);
        h.SearchRequests.Should().ContainSingle();
    }

    [Fact]
    public async Task ASearchResultOutsideTheReadableSet_IsDropped()
    {
        var h = WithDocuments((1, null));
        var stranger = Guid.NewGuid();
        h.SearchOverride = _ => Task.FromResult(new SemanticSearchResponse
        {
            Results =
            [
                new SearchResult { DocumentId = stranger.ToString(), CombinedScore = 0.99 },
                new SearchResult { DocumentId = null, CombinedScore = 0.98 },
                new SearchResult { DocumentId = "not-a-guid", CombinedScore = 0.97 },
                new SearchResult { DocumentId = Doc(1).ToString(), CombinedScore = 0.1 },
            ],
            Metadata = new SearchMetadata(),
        });

        var result = (await h.GetAsync())!;

        result.Items.Select(i => i.DocumentId).Should().Equal(Doc(1));
    }

    // =========================================================================================
    // Failure of a source
    // =========================================================================================

    [Fact]
    public async Task ASearchThatFails_IsUnavailable_NeverAnUnrankedList()
    {
        var h = WithDocuments((1, 0.5));
        h.SearchOverride = _ => Task.FromException<SemanticSearchResponse>(new InvalidOperationException("index down"));

        AssertUnavailable((await h.GetAsync())!, InvoiceRelatedWorkService.CodeSearchUnavailable);
    }

    [Fact]
    public async Task SearchSwitchedOff_TheNullObject_IsUnavailable_NotEmpty()
    {
        var h = WithDocuments((1, 0.5));
        h.SearchOverride = _ => Task.FromException<SemanticSearchResponse>(new FeatureDisabledException("ai.semantic-search.disabled", "off"));

        var result = (await h.GetAsync())!;

        AssertUnavailable(result, InvoiceRelatedWorkService.CodeSearchUnavailable);
    }

    [Fact]
    public async Task ANullSearchObject_ThrowsFeatureDisabled_ForBothMembers()
    {
        var search = new NullSemanticSearchService(NullLogger<NullSemanticSearchService>.Instance);

        await FluentActions.Awaiting(() => search.SearchAsync(new SemanticSearchRequest(), Tenant))
            .Should().ThrowAsync<FeatureDisabledException>();
        await FluentActions.Awaiting(() => search.CountAsync(new SemanticSearchRequest(), Tenant))
            .Should().ThrowAsync<FeatureDisabledException>();
    }

    [Fact]
    public async Task NoTenantOnTheToken_IsUnavailable_AndNothingIsSearched()
    {
        var h = WithDocuments((1, 0.5));

        var result = (await h.GetAsync(tenant: null))!;

        AssertUnavailable(result, InvoiceRelatedWorkService.CodeSearchUnavailable);
        h.SearchRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task AFailedDataverseRead_IsUnavailable()
    {
        var h = WithDocuments((1, 0.5));
        h.Entities.Reset();
        h.Entities
            .Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Dataverse down"));

        AssertUnavailable((await h.GetAsync())!, InvoiceRelatedWorkService.CodeUnavailable);
    }

    [Fact]
    public async Task ASearchThatNeverAnswers_HitsTheDeadline_AndIsUnavailable()
    {
        var h = WithDocuments((1, 0.5));
        var service = h.Service();
        service.Deadline = TimeSpan.FromMilliseconds(100);

        h.Search.Reset();
        h.Search
            .Setup(s => s.SearchAsync(It.IsAny<SemanticSearchRequest>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(async (SemanticSearchRequest _, string _, CancellationToken ct) =>
            {
                await Task.Delay(Timeout.Infinite, ct);
                return new SemanticSearchResponse { Results = [], Metadata = new SearchMetadata() };
            });

        var result = (await service.GetAsync(InvoiceId, LineId, Caller, Tenant))!;

        AssertUnavailable(result, InvoiceRelatedWorkService.CodeTimeout);
    }

    [Fact]
    public async Task ACancelledRequest_Propagates()
    {
        var h = WithDocuments((1, 0.5));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        h.Entities.Reset();
        h.Entities
            .Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .Returns((QueryExpression _, CancellationToken ct) => Task.FromCanceled<EntityCollection>(ct));

        await FluentActions.Awaiting(() => h.Service().GetAsync(InvoiceId, LineId, Caller, Tenant, cts.Token))
            .Should().ThrowAsync<OperationCanceledException>();
    }

    // =========================================================================================
    // Minimum text (NFR-11, ADR-015)
    // =========================================================================================

    [Fact]
    public async Task LogsCarryIdsAndCounts_NeverTheNarrativeOrADocumentName()
    {
        var h = WithDocuments((1, 0.5), (2, 0.9));
        h.Rights.OnlyReadable(Doc(1));

        var ok = await h.GetAsync();
        h.SearchOverride = _ => Task.FromException<SemanticSearchResponse>(new InvalidOperationException("boom"));
        var failed = await h.GetAsync();
        h.Rights.ThrowFor(Doc(1));
        var denied = await h.GetAsync();

        ok!.Status.Should().Be(RelatedWorkStatus.Available);
        failed!.Status.Should().Be(RelatedWorkStatus.Unavailable);
        denied!.Status.Should().Be(RelatedWorkStatus.Unavailable);
        h.Log.Messages.Should().NotBeEmpty();
        h.Log.Messages.Should().OnlyContain(m => !m.Contains("lease amendment") && !m.Contains("Springfield") && !m.Contains("Doc 1") && !m.Contains("Doc 2"));
    }

    [Fact]
    public async Task TheResponseCarriesNoNarrativeAndNoSnippet()
    {
        var h = WithDocuments((1, 0.5));

        var json = JsonSerializer.Serialize(await h.GetAsync());

        json.Should().NotContain("lease amendment").And.NotContain("highlights").And.NotContain("summary");
    }

    // =========================================================================================
    // Helpers
    // =========================================================================================

    private static void AssertEmpty(RelatedWorkDto result, string reason)
    {
        result.Status.Should().Be(RelatedWorkStatus.Empty);
        result.Reason.Should().Be(reason);
        result.Code.Should().BeNull();
        result.Items.Should().BeEmpty();
    }

    private static void AssertUnavailable(RelatedWorkDto result, string code)
    {
        result.Status.Should().Be(RelatedWorkStatus.Unavailable);
        result.Code.Should().Be(code);
        result.Message.Should().NotBeNullOrWhiteSpace();
        result.Reason.Should().BeNull();
        result.Items.Should().BeEmpty("nothing is shown when the answer cannot be trusted");
    }

    /// <summary>The caller's rights on each document, answered the way Dataverse's RetrievePrincipalAccess would.</summary>
    private sealed class Access : IAccessDataSource
    {
        private HashSet<Guid>? _readable;
        private readonly HashSet<Guid> _faulted = [];
        private readonly HashSet<Guid> _throws = [];

        public AccessRights? Rights { get; set; }
        public HashSet<Guid> FaultedButGranted { get; } = [];
        public List<(string UserId, string EntitySet, Guid Id, string? Token)> Calls { get; } = [];

        public void OnlyReadable(params Guid[] ids) => _readable = [.. ids];

        public void FaultFor(Guid id) => _faulted.Add(id);

        public void ThrowFor(Guid id) => _throws.Add(id);

        public Task<AccessSnapshot> GetRecordAccessAsync(
            string userId, string entitySetName, Guid recordId, string? userAccessToken, CancellationToken ct = default)
        {
            lock (Calls)
            {
                Calls.Add((userId, entitySetName, recordId, userAccessToken));
            }

            if (_throws.Contains(recordId))
            {
                return Task.FromException<AccessSnapshot>(new InvalidOperationException("Dataverse unreachable"));
            }

            var granted = FaultedButGranted.Contains(recordId)
                || (!_faulted.Contains(recordId) && (_readable is null || _readable.Contains(recordId)));
            return Task.FromResult(new AccessSnapshot
            {
                UserId = userId,
                ResourceId = recordId.ToString(),
                AccessRights = Rights ?? (granted ? AccessRights.Read : AccessRights.None),
                Faulted = _faulted.Contains(recordId) || FaultedButGranted.Contains(recordId),
            });
        }

        public Task<AccessSnapshot> GetUserAccessAsync(string userId, string resourceId, string? userAccessToken = null, CancellationToken ct = default) =>
            throw new InvalidOperationException("Related work asks the entity-generic record path (sprk_documents), not the document-only one.");
    }

    private sealed class ListLogger : ILogger<InvoiceRelatedWorkService>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (Messages)
            {
                Messages.Add(formatter(state, exception) + (exception is null ? string.Empty : " | " + exception));
            }
        }
    }
}
