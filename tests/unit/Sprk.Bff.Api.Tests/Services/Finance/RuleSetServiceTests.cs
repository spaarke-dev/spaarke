using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Finance;

/// <summary>
/// <see cref="RuleSetService"/> (ebilling-r1 task 076, FR-28): create from a source document, the rules beside their clause quotes, edit /
/// accept / reject, publish (all or nothing, immutable versions), assignment, visibility, and the single clause-visibility decision.
/// </summary>
/// <remarks>
/// Maintain-class (ADR-038): the guarantees the rules engine and the firms rely on — nothing is in force before a publish, a published
/// version is never updated, one invalid rule publishes nothing, an Ontology policy is invisible to and unchanged by this service, and a
/// flag-off rule set never yields clause text. Dataverse and SPE are substituted at their narrow seams; validation, the describer and the
/// orchestration are real. The fake store evaluates the real query (nested filters, In, Null), so a lost domain filter is observable.
/// </remarks>
public sealed class RuleSetServiceTests
{
    private static readonly Guid TeamId = Guid.NewGuid();
    private static readonly Guid DocumentId = Guid.NewGuid();
    private static readonly Guid FirmId = Guid.NewGuid();
    private static readonly Guid CallerOid = Guid.NewGuid();
    private const string Drive = "b!procedurelibrary";
    private static readonly DateOnly Effective = new(2026, 11, 1);

    private const string CapBody = """{"type":"DailyHoursCap","maxHoursPerDay":10}""";
    private const string CapBody8 = """{"type":"DailyHoursCap","maxHoursPerDay":8}""";
    private const string IncrementBody = """{"type":"TimeIncrement","incrementHours":0.1}""";
    private const string Template = "A timekeeper billed over {{rule.maxHoursPerDay}} hours.";

    internal enum Write { Create, Update, BulkUpdate, Delete }

    private sealed class ListLogger(List<(LogLevel Level, string Message)> sink) : ILogger<RuleSetService>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            sink.Add((logLevel, formatter(state, exception)));
    }

    internal sealed class Harness
    {
        public readonly Mock<IGenericEntityService> Entities = new();
        public readonly Mock<IDocumentDataverseService> Documents = new();
        public readonly Mock<SpeFileStore> Spe;
        public readonly Mock<IRecordOwnershipResolver> Ownership = new();
        public readonly FinanceOptions Options = new() { ProcedureLibraryContainerId = Drive };

        public readonly Dictionary<string, List<Entity>> Rows = new();
        public readonly List<(Write Kind, string Entity, Guid Id, Dictionary<string, object> Fields)> Writes = new();
        public readonly List<QueryExpression> Queries = new();
        public readonly List<(string Drive, string Name, byte[] Bytes)> Uploads = new();
        public readonly List<string> FileDeletes = new();
        public readonly List<CreateDocumentRequest> DocumentCreates = new();
        public readonly List<string> Order = new();
        public readonly List<(string Entity, int Size)> BulkCalls = new();
        public Func<string, bool>? FailBulkFor;

        /// <summary>Runs at the start of every bulk update (after the cancellation check): a test uses it to disconnect the caller mid-write.</summary>
        public Action<string>? OnBulk;
        public readonly List<(LogLevel Level, string Message)> Logs = new();
        public Func<Entity, bool>? FailCreateWhen;
        public bool IgnoreQueryFilters;
        public RecordOwnerResolution OwnerAnswer = RecordOwnerResolution.Owned(TeamId);

        public Harness()
        {
            Seed(new Entity("sprk_organization", FirmId));

            var gcf = Mock.Of<IGraphClientFactory>();
            Spe = new Mock<SpeFileStore>(
                MockBehavior.Strict,
                new ContainerOperations(gcf, TestSpeOwnership.AllowAll(gcf), Mock.Of<ILogger<ContainerOperations>>()),
                new DriveItemOperations(gcf, TestSpeOwnership.AllowAll(gcf), Mock.Of<ILogger<DriveItemOperations>>()),
                new UploadSessionManager(gcf, TestSpeOwnership.AllowAll(gcf), Mock.Of<IHttpClientFactory>(), Mock.Of<ILogger<UploadSessionManager>>()),
                new UserOperations(gcf, Mock.Of<ILogger<UserOperations>>()),
                null!);
            Spe.Setup(s => s.UploadSmallAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<ConflictBehavior>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string drive, string name, Stream content, ConflictBehavior conflict, CancellationToken _) =>
                {
                    using var copy = new MemoryStream();
                    content.CopyTo(copy);
                    Uploads.Add((drive, name, copy.ToArray()));
                    return new FileHandleDto("item-1", name, null, copy.Length, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, false, "https://web/item-1", drive);
                });
            Spe.Setup(s => s.DeleteFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string _, string item, CancellationToken _) =>
                {
                    FileDeletes.Add(item);
                    return true;
                });

            Documents.Setup(d => d.CreateDocumentAsync(It.IsAny<CreateDocumentRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((CreateDocumentRequest r, CancellationToken _) =>
                {
                    DocumentCreates.Add(r);
                    Seed(new Entity("sprk_document", DocumentId));
                    return DocumentId.ToString();
                });
            Documents.Setup(d => d.UpdateDocumentAsync(It.IsAny<string>(), It.IsAny<UpdateDocumentRequest>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            Ownership.Setup(o => o.ResolveOwnerAsync(It.IsAny<RecordOwnershipContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => OwnerAnswer);

            Entities.Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((QueryExpression q, CancellationToken _) =>
                {
                    Queries.Add(q);
                    return new EntityCollection((Rows.GetValueOrDefault(q.EntityName) ?? new List<Entity>())
                        .Where(r => IgnoreQueryFilters && q.EntityName != "sprk_organization" ? true : Matches(r, q.Criteria))
                        .Take(q.TopCount ?? int.MaxValue).Select(Clone).ToList());
                });
            Entities.Setup(e => e.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Entity entity, CancellationToken _) =>
                {
                    if (FailCreateWhen?.Invoke(entity) == true)
                    {
                        throw new InvalidOperationException("simulated Dataverse failure");
                    }

                    entity.Id = Guid.NewGuid();
                    Writes.Add((Write.Create, entity.LogicalName, entity.Id, entity.Attributes.ToDictionary(a => a.Key, a => a.Value)));
                    Seed(entity);
                    return entity.Id;
                });
            Entities.Setup(e => e.UpdateAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
                .Returns((string entity, Guid id, Dictionary<string, object> fields, CancellationToken ct) =>
                {
                    ct.ThrowIfCancellationRequested(); // the real seam honours the caller's token
                    Order.Add("update:" + entity);
                    Writes.Add((Write.Update, entity, id, fields));
                    Apply(entity, id, fields);
                    return Task.CompletedTask;
                });
            Entities.Setup(e => e.BulkUpdateAsync(It.IsAny<string>(), It.IsAny<List<(Guid id, Dictionary<string, object> fields)>>(), It.IsAny<CancellationToken>()))
                .Returns((string entity, List<(Guid id, Dictionary<string, object> fields)> updates, CancellationToken ct) =>
                {
                    ct.ThrowIfCancellationRequested(); // the real seam honours the caller's token
                    OnBulk?.Invoke(entity);
                    if (FailBulkFor?.Invoke(entity) == true)
                    {
                        throw new InvalidOperationException("simulated transaction failure");
                    }

                    Order.Add("bulk:" + entity);
                    BulkCalls.Add((entity, updates.Count));
                    foreach (var (id, fields) in updates)
                    {
                        Writes.Add((Write.BulkUpdate, entity, id, fields));
                        Apply(entity, id, fields);
                    }

                    return Task.CompletedTask;
                });
            Entities.Setup(e => e.DeleteAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .Returns((string entity, Guid id, CancellationToken _) =>
                {
                    Writes.Add((Write.Delete, entity, id, new Dictionary<string, object>()));
                    Rows.GetValueOrDefault(entity)?.RemoveAll(r => r.Id == id);
                    return Task.CompletedTask;
                });
        }

        public RuleSetService Service()
        {
            var schema = new RuleBodySchemaValidator();
            var validator = new PolicyVersionValidator(schema, new PredicateCompiler(schema, TimeProvider.System), NullLogger<PolicyVersionValidator>.Instance);
            return new RuleSetService(
                Entities.Object, Documents.Object, Spe.Object, Ownership.Object, validator,
                new RuleBodyDescriber(validator, Entities.Object, NullLogger<RuleBodyDescriber>.Instance),
                Mock.Of<IOptionsMonitor<FinanceOptions>>(m => m.CurrentValue == Options),
                new ListLogger(Logs));
        }

        public IEnumerable<(Write Kind, string Entity, Guid Id, Dictionary<string, object> Fields)> WritesOf(params string[] entities) =>
            Writes.Where(w => entities.Contains(w.Entity));

        public void Seed(params Entity[] rows)
        {
            foreach (var row in rows)
            {
                if (!Rows.TryGetValue(row.LogicalName, out var list))
                {
                    Rows[row.LogicalName] = list = new List<Entity>();
                }

                list.Add(row);
            }
        }

        public Entity Row(string entity, Guid id) => Rows[entity].Single(r => r.Id == id);

        public Guid RuleSet(
            int status = RuleSetService.RuleSetDraft, bool visible = true, int? scope = (int)RuleSetScope.ClientDefault, Guid? firm = null,
            int? domain = RuleDomains.PolicyDomainBilling, DateTime? effectiveFrom = null, string? notConverted = null)
        {
            var e = new Entity("sprk_ruleset", Guid.NewGuid());
            e["sprk_name"] = "Acme guidelines";
            e["statuscode"] = new OptionSetValue(status);
            e["sprk_visibletofirms"] = visible;
            if (scope is { } s) e["sprk_scope"] = new OptionSetValue(s);
            if (firm is { } f) e["sprk_firm"] = new EntityReference("sprk_organization", f);
            if (domain is { } d) e["sprk_domain"] = new OptionSetValue(d);
            if (effectiveFrom is { } from) e["sprk_effectivefrom"] = from;
            if (notConverted is not null) e["sprk_notconverted"] = notConverted;
            Seed(e);
            return e.Id;
        }

        /// <summary>A policy (domain per <paramref name="domain"/>; null = empty = Ontology) and its first version.</summary>
        public (Guid Policy, Guid Version) Rule(
            Guid ruleSetId, string code = "BILL-A", ProposalState state = ProposalState.Proposed, bool current = false,
            string type = "DailyHoursCap", string? body = CapBody, int? consequence = (int)BillingConsequence.Flag,
            int? domain = RuleDomains.PolicyDomainBilling, DateTime? from = null, DateTime? to = null,
            string? quote = "No timekeeper may bill more than 10 hours a day.", string? location = "Section 4.2, p. 7")
        {
            var policy = new Entity("sprk_policy", Guid.NewGuid());
            policy["sprk_name"] = "Rule " + code;
            policy["sprk_policycode"] = code;
            policy["sprk_ruleset"] = new EntityReference("sprk_ruleset", ruleSetId);
            policy["sprk_enabled"] = false;
            if (domain is { } d) policy["sprk_domain"] = new OptionSetValue(d);
            Seed(policy);

            var version = AddVersion(policy.Id, 1, state, type, body, consequence, from, to, quote, location);
            if (current)
            {
                policy["sprk_currentversion"] = new EntityReference("sprk_policyversion", version);
                policy["sprk_enabled"] = true;
            }

            return (policy.Id, version);
        }

        public Guid AddVersion(
            Guid policyId, int number, ProposalState state, string type = "DailyHoursCap", string? body = CapBody,
            int? consequence = (int)BillingConsequence.Flag, DateTime? from = null, DateTime? to = null,
            string? quote = "No timekeeper may bill more than 10 hours a day.", string? location = "Section 4.2, p. 7")
        {
            var v = new Entity("sprk_policyversion", Guid.NewGuid());
            v["sprk_policy"] = new EntityReference("sprk_policy", policyId);
            v["sprk_versionnumber"] = number;
            v["sprk_ruletype"] = new OptionSetValue((int)Enum.Parse<RuleType>(type));
            if (body is not null) v["sprk_rulebody"] = body;
            v["sprk_messagetemplate"] = Template;
            v["sprk_shortheadline"] = "Daily hours cap";
            v["sprk_proposalstatus"] = new OptionSetValue((int)state);
            v["sprk_inforcefrom"] = from ?? new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            if (consequence is { } c) v["sprk_consequence"] = new OptionSetValue(c);
            if (to is { } t) v["sprk_inforceto"] = t;
            if (quote is not null) v["sprk_clausereference"] = quote;
            if (location is not null) v["sprk_clauselocation"] = location;
            Seed(v);
            return v.Id;
        }

        private void Apply(string entity, Guid id, Dictionary<string, object> fields)
        {
            var row = Row(entity, id);
            foreach (var (key, value) in fields)
            {
                if (value is DBNull) row.Attributes.Remove(key);
                else row[key] = value;
            }
        }

        private static Entity Clone(Entity e)
        {
            var c = new Entity(e.LogicalName, e.Id);
            foreach (var a in e.Attributes) c[a.Key] = a.Value;
            return c;
        }

        private static bool Matches(Entity row, FilterExpression filter)
        {
            var results = filter.Conditions.Select(c => Matches(row, c)).Concat(filter.Filters.Select(f => Matches(row, f))).ToList();
            return results.Count == 0 || (filter.FilterOperator == LogicalOperator.And ? results.All(x => x) : results.Any(x => x));
        }

        private static bool Matches(Entity row, ConditionExpression c)
        {
            object? actual = c.AttributeName == row.LogicalName + "id"
                ? row.Id
                : row.Contains(c.AttributeName) ? row[c.AttributeName] : c.AttributeName == "statecode" ? new OptionSetValue(0) : null;
            actual = actual switch { EntityReference r => r.Id, OptionSetValue o => o.Value, _ => actual };
            var wanted = c.Values.Select(v => v is OptionSetValue o ? o.Value : v).ToList();
            return c.Operator switch
            {
                ConditionOperator.Equal => actual is not null && Equals(actual, wanted[0]),
                ConditionOperator.In => actual is not null && wanted.Any(w => Equals(actual, w)),
                ConditionOperator.Null => actual is null,
                _ => throw new NotSupportedException(c.Operator.ToString()),
            };
        }
    }

    private static RuleSetCreateRequest CreateRequest(
        RuleSetSourceKind kind = RuleSetSourceKind.Guideline, RuleSetScope scope = RuleSetScope.ClientDefault, Guid? firm = null, string name = "Acme OCG") =>
        new(name, kind, scope, firm, "ocg.pdf", CallerOid);

    private static ProposalEdit Edit(
        string? type = null, string? body = null, string? template = null, string? headline = null, string? consequence = null,
        string? quote = null, string? location = null) =>
        new(type, body, template, headline, consequence, quote, location, CallerOid);

    private static DateTime Utc(int y, int m, int d) => new(y, m, d, 0, 0, 0, DateTimeKind.Utc);

    private static readonly string[] DomainIsolated = { "sprk_policy", "sprk_ruleset" };

    // ═══════════════════════════════════════════════════════════════════════════
    // Create
    // ═══════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(RuleSetSourceKind.Guideline, true)]
    [InlineData(RuleSetSourceKind.RateCard, true)]
    [InlineData(RuleSetSourceKind.InternalProcedure, false)]
    public async Task Create_StartsDraftBillingWithTheFlagFromTheSourceKind_AndStoresTheDocumentInTheProcedureLibrary(
        RuleSetSourceKind kind, bool expectedVisible)
    {
        var h = new Harness();

        var outcome = await h.Service().CreateAsync(CreateRequest(kind), new byte[] { 1, 2, 3 }, default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Done);
        outcome.Value!.VisibleToFirms.Should().Be(expectedVisible);
        outcome.Value.Status.Should().Be("Draft");
        outcome.Value.SourceKind.Should().Be(kind.ToString());
        h.Uploads.Should().ContainSingle().Which.Drive.Should().Be(Drive, "the source document goes to the Procedure Library, through SpeFileStore");
        h.Uploads[0].Bytes.Should().Equal(1, 2, 3);
        h.DocumentCreates.Should().ContainSingle().Which.ContainerId.Should().Be(Drive);
        var row = h.Rows["sprk_ruleset"].Single();
        row.GetAttributeValue<bool>("sprk_visibletofirms").Should().Be(expectedVisible);
        row.GetAttributeValue<OptionSetValue>("sprk_domain").Value.Should().Be(RuleDomains.PolicyDomainBilling);
        row.GetAttributeValue<OptionSetValue>("statuscode").Value.Should().Be(RuleSetService.RuleSetDraft);
        row.GetAttributeValue<EntityReference>("sprk_sourcedocument").Id.Should().Be(DocumentId);
        row.GetAttributeValue<EntityReference>("ownerid").Id.Should().Be(TeamId);
    }

    [Fact]
    public async Task Create_ForAFirm_SetsScopeAndFirm()
    {
        var h = new Harness();

        var outcome = await h.Service().CreateAsync(CreateRequest(scope: RuleSetScope.Firm, firm: FirmId), new byte[] { 1 }, default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Done);
        var row = h.Rows["sprk_ruleset"].Single();
        row.GetAttributeValue<OptionSetValue>("sprk_scope").Value.Should().Be((int)RuleSetScope.Firm);
        row.GetAttributeValue<EntityReference>("sprk_firm").Id.Should().Be(FirmId);
    }

    [Fact]
    public async Task Create_WithoutAConfiguredContainer_Is503_AndWritesNothing()
    {
        var h = new Harness { Options = { ProcedureLibraryContainerId = "" } };

        var outcome = await h.Service().CreateAsync(CreateRequest(), new byte[] { 1 }, default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Refused);
        outcome.ErrorCode.Should().Be("ruleset.container_not_configured");
        outcome.StatusCode.Should().Be(503);
        h.Uploads.Should().BeEmpty();
        h.Writes.Should().BeEmpty();
    }

    [Theory]
    [InlineData("firmScopeNoFirm")]
    [InlineData("defaultWithFirm")]
    [InlineData("unknownFirm")]
    [InlineData("blankName")]
    public async Task Create_InvalidInput_IsRefusedWithFieldErrors_AndWritesNothing(string scenario)
    {
        var h = new Harness();
        var request = scenario switch
        {
            "firmScopeNoFirm" => CreateRequest(scope: RuleSetScope.Firm),
            "defaultWithFirm" => CreateRequest(firm: FirmId),
            "unknownFirm" => CreateRequest(scope: RuleSetScope.Firm, firm: Guid.NewGuid()),
            _ => CreateRequest(name: "  "),
        };

        var outcome = await h.Service().CreateAsync(request, new byte[] { 1 }, default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Invalid);
        outcome.ErrorCode.Should().Be("ruleset.invalid");
        outcome.Errors.Should().NotBeEmpty();
        h.Uploads.Should().BeEmpty();
        h.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task Create_WhenTheRuleSetWriteFails_RemovesTheDocumentRowAndTheUploadedFile()
    {
        var h = new Harness { FailCreateWhen = e => e.LogicalName == "sprk_ruleset" };

        var outcome = await h.Service().CreateAsync(CreateRequest(), new byte[] { 1 }, default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Refused);
        outcome.ErrorCode.Should().Be("ruleset.write_failed");
        h.FileDeletes.Should().Equal("item-1");
        h.Writes.Should().Contain(w => w.Kind == Write.Delete && w.Entity == "sprk_document");
        h.Rows.GetValueOrDefault("sprk_ruleset").Should().BeNullOrEmpty();
    }

    [Fact]
    public async Task Create_WhenTheCallerCancelsMidWay_StillRemovesTheDocumentRowAndTheUploadedFile_AndRethrows()
    {
        using var cts = new CancellationTokenSource();
        var h = new Harness
        {
            FailCreateWhen = e =>
            {
                cts.Cancel();
                throw new OperationCanceledException(cts.Token);
            },
        };

        var act = () => h.Service().CreateAsync(CreateRequest(), new byte[] { 1 }, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        h.FileDeletes.Should().Equal("item-1");
        h.Writes.Should().Contain(w => w.Kind == Write.Delete && w.Entity == "sprk_document");
    }

    [Fact]
    public async Task Create_OwnerRefused_WritesNothing()
    {
        var h = new Harness { OwnerAnswer = RecordOwnerResolution.Refused(RecordOwnerRefusal.NoOwnerSource, "no team") };

        var outcome = await h.Service().CreateAsync(CreateRequest(), new byte[] { 1 }, default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.OwnerRefused);
        h.Uploads.Should().BeEmpty();
        h.Writes.Should().BeEmpty();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Reads and domain isolation
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task List_ReturnsOnlyBillingRuleSets_WithTheirRuleCounts_NeverAnOntologyOrEmptyDomainRow()
    {
        var h = new Harness();
        var billing = h.RuleSet();
        var ontology = h.RuleSet(domain: RuleDomains.PolicyDomainOntology);
        var empty = h.RuleSet(domain: null);
        h.Rule(billing, "BILL-A");
        h.Rule(billing, "BILL-B");
        h.Rule(billing, "ONT-X", domain: RuleDomains.PolicyDomainOntology);

        var outcome = await h.Service().ListAsync(default);

        outcome.Value!.Select(s => s.Id).Should().Equal(billing);
        outcome.Value!.Single().RuleCount.Should().Be(2, "an Ontology policy pointing at a billing rule set is not a rule of it");
        outcome.Value!.Select(s => s.Id).Should().NotContain(new[] { ontology, empty });
    }

    [Fact]
    public async Task Get_ShowsEachRuleBesideItsClause_WithDescriptionStateAndConsequence()
    {
        var h = new Harness();
        var set = h.RuleSet(notConverted: """[{"text":"Travel","reason":"unsupported"}]""");
        var (policy, version) = h.Rule(set, "BILL-A", ProposalState.Accepted);

        var outcome = await h.Service().GetAsync(set, default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Done);
        outcome.Value!.NotConverted!.AsArray().Should().HaveCount(1);
        var p = outcome.Value.Proposals.Single();
        p.PolicyId.Should().Be(policy);
        p.VersionId.Should().Be(version);
        p.State.Should().Be("Accepted");
        p.RuleType.Should().Be("DailyHoursCap");
        p.Consequence.Should().Be("Flag");
        p.ClauseQuote.Should().Be("No timekeeper may bill more than 10 hours a day.");
        p.ClauseLocation.Should().Be("Section 4.2, p. 7");
        p.DescriptionText.Should().Contain("at most 10 hours per day");
        p.InForce.Should().BeFalse();
        p.ValidationErrors.Should().BeEmpty();
    }

    [Fact]
    public async Task Get_ARuleSetOfAnotherDomain_IsNotFound()
    {
        var h = new Harness();
        var ontology = h.RuleSet(domain: RuleDomains.PolicyDomainOntology);
        var empty = h.RuleSet(domain: null);
        var docketing = h.RuleSet(domain: 100000002);

        foreach (var id in new[] { ontology, empty, docketing })
        {
            var outcome = await h.Service().GetAsync(id, default);
            outcome.Kind.Should().Be(RuleSetOutcomeKind.Refused);
            outcome.StatusCode.Should().Be(404);
        }
    }

    [Fact]
    public async Task Get_ListsOnlyBillingPolicies_AnOntologyPolicyLinkedToTheSetIsNeverShown()
    {
        var h = new Harness();
        var set = h.RuleSet();
        h.Rule(set, "BILL-A");
        h.Rule(set, "ONT-NULL", domain: null);
        h.Rule(set, "ONT-1", domain: RuleDomains.PolicyDomainOntology);
        h.Rule(set, "DOCK", domain: 100000002);

        var outcome = await h.Service().GetAsync(set, default);

        outcome.Value!.Proposals.Select(p => p.PolicyCode).Should().Equal("BILL-A");
    }

    [Fact]
    public async Task EveryPolicyAndRuleSetQuery_CarriesATopLevelBillingDomainCondition()
    {
        var h = new Harness();
        var set = h.RuleSet();
        var (policy, version) = h.Rule(set, state: ProposalState.Accepted, current: false);
        var service = h.Service();

        await service.ListAsync(default);
        await service.GetAsync(set, default);
        await service.EditAsync(set, policy, Edit(headline: "x"), default);
        await service.DecideAsync(set, policy, true, default);
        await service.PublishAsync(set, Effective, default);
        await service.AssignAsync(set, RuleSetScope.ClientDefault, null, default);
        await service.SetVisibilityAsync(set, false, default);
        await service.GetFirmClauseAsync(version, default);

        var reads = h.Queries.Where(q => DomainIsolated.Contains(q.EntityName)).ToList();
        reads.Should().NotBeEmpty();
        reads.Should().OnlyContain(q => q.Criteria.Conditions.Any(c =>
            c.AttributeName == "sprk_domain" && c.Operator == ConditionOperator.Equal && c.Values.Single().Equals(RuleDomains.PolicyDomainBilling)));
    }

    [Fact]
    public async Task EvenWhenAQueryLosesItsDomainFilter_AnOntologyPolicyIsNeverActedOn_ByAnyOperation()
    {
        var h = new Harness { IgnoreQueryFilters = true };
        var set = h.RuleSet();
        var (nullDomain, v1) = h.Rule(set, "ONT-NULL", ProposalState.Accepted, domain: null);
        var (ontology, v2) = h.Rule(set, "ONT-1", ProposalState.Accepted, domain: RuleDomains.PolicyDomainOntology);
        var service = h.Service();

        foreach (var policy in new[] { nullDomain, ontology })
        {
            (await service.EditAsync(set, policy, Edit(headline: "x"), default)).StatusCode.Should().Be(404);
            (await service.DecideAsync(set, policy, true, default)).StatusCode.Should().Be(404);
            (await service.DecideAsync(set, policy, false, default)).StatusCode.Should().Be(404);
        }

        (await service.PublishAsync(set, Effective, default)).ErrorCode.Should().Be("ruleset.nothing_to_publish");
        (await service.GetAsync(set, default)).Value!.Proposals.Should().BeEmpty();
        (await service.GetFirmClauseAsync(v1, default)).Should().Be(FirmClauseText.None);
        (await service.GetFirmClauseAsync(v2, default)).Should().Be(FirmClauseText.None);
        h.Writes.Should().BeEmpty("nothing about an Ontology policy is written");
    }

    [Fact]
    public async Task BillingOperations_NeverWriteTheDomainColumn_AndTheirRowsAreInvisibleToTheOntologyReaders()
    {
        var h = new Harness();
        var set = h.RuleSet();
        var (policy, _) = h.Rule(set, state: ProposalState.Accepted, current: true);
        var draft = h.AddVersion(policy, 2, ProposalState.Accepted, body: CapBody8);
        var service = h.Service();
        await service.EditAsync(set, policy, Edit(headline: "Edited"), default); // edits the draft in place
        await service.PublishAsync(set, Effective, default);
        await service.AssignAsync(set, RuleSetScope.ClientDefault, null, default);

        h.Writes.Should().NotBeEmpty();
        h.Writes.Should().OnlyContain(w => !w.Fields.ContainsKey("sprk_domain"));

        // The other direction: the Ontology allow-list refuses every row the billing path reads or writes.
        h.Row("sprk_policy", policy).Should().Match<Entity>(p => !RuleDomains.IsOntologyPolicy(p));
        var ontologyQuery = new QueryExpression("sprk_policy");
        RuleDomains.AddOntologyPolicyAllowlist(ontologyQuery);
        ontologyQuery.Criteria.Filters.Single().Conditions.Select(c => c.AttributeName).Should().OnlyContain(a => a == "sprk_domain");
        ontologyQuery.Criteria.Filters.Single().Conditions.Should().NotContain(c => c.Values.Contains(RuleDomains.PolicyDomainBilling));
        draft.Should().NotBeEmpty();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Edit
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Edit_OfADraft_UpdatesItInPlace_AndCreatesNothing()
    {
        var h = new Harness();
        var set = h.RuleSet();
        var (policy, version) = h.Rule(set);

        var outcome = await h.Service().EditAsync(set, policy, Edit(body: CapBody8, consequence: "reject", location: "Section 9"), default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Done);
        outcome.Value!.CreatedNewVersion.Should().BeFalse();
        outcome.Value.Proposal.VersionId.Should().Be(version);
        outcome.Value.Proposal.Consequence.Should().Be("Reject");
        h.Writes.Should().ContainSingle().Which.Should().Match<(Write Kind, string Entity, Guid Id, Dictionary<string, object> Fields)>(
            w => w.Kind == Write.Update && w.Entity == "sprk_policyversion" && w.Id == version);
        var row = h.Row("sprk_policyversion", version);
        row.GetAttributeValue<string>("sprk_rulebody").Should().Be(CapBody8);
        row.GetAttributeValue<string>("sprk_clauselocation").Should().Be("Section 9");
        row.GetAttributeValue<string>("sprk_proposedaction").Should().Be("billing-reject");
    }

    [Fact]
    public async Task Edit_OfAVersionInForce_CreatesANewDraftVersion_AndLeavesThePublishedRowUnchanged()
    {
        var h = new Harness();
        var set = h.RuleSet(RuleSetService.RuleSetPublished);
        var (policy, v1) = h.Rule(set, state: ProposalState.Accepted, current: true);
        var before = h.Row("sprk_policyversion", v1).Attributes.ToDictionary(a => a.Key, a => a.Value);

        var outcome = await h.Service().EditAsync(set, policy, Edit(body: CapBody8), default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Done);
        outcome.Value!.CreatedNewVersion.Should().BeTrue();
        outcome.Value.Proposal.VersionNumber.Should().Be(2);
        outcome.Value.Proposal.State.Should().Be("Proposed");
        outcome.Value.Proposal.InForce.Should().BeFalse();
        outcome.Value.Proposal.HasPublishedVersion.Should().BeTrue();
        h.Writes.Should().ContainSingle(w => w.Kind == Write.Create && w.Entity == "sprk_policyversion");
        h.Writes.Should().NotContain(w => w.Kind != Write.Create, "no update touches the published version, the policy pointer or the rule set");
        h.Row("sprk_policyversion", v1).Attributes.Should().BeEquivalentTo(before);
        h.Row("sprk_policy", policy).GetAttributeValue<EntityReference>("sprk_currentversion").Id.Should().Be(v1);
        var created = h.Rows["sprk_policyversion"].Single(v => v.Id != v1);
        created.GetAttributeValue<string>("sprk_rulebody").Should().Be(CapBody8);
        created.GetAttributeValue<OptionSetValue>("sprk_proposalstatus").Value.Should().Be((int)ProposalState.Proposed);
        created.GetAttributeValue<EntityReference>("sprk_policy").Id.Should().Be(policy);
        created.GetAttributeValue<EntityReference>("ownerid").Id.Should().Be(TeamId);
    }

    [Fact]
    public async Task Edit_OfAVersionThatWasInForceAndEnded_AlsoCreatesANewVersion()
    {
        var h = new Harness();
        var set = h.RuleSet(RuleSetService.RuleSetPublished);
        var (policy, v1) = h.Rule(set, state: ProposalState.Accepted, to: Utc(2026, 6, 1)); // ended, no longer current

        var outcome = await h.Service().EditAsync(set, policy, Edit(headline: "Changed"), default);

        outcome.Value!.CreatedNewVersion.Should().BeTrue();
        h.Writes.Should().NotContain(w => w.Id == v1);
    }

    [Fact]
    public async Task Edit_WithAnInvalidResult_WritesNothing_AndNamesTheField()
    {
        var h = new Harness();
        var set = h.RuleSet();
        var (policy, _) = h.Rule(set);

        var badBody = await h.Service().EditAsync(set, policy, Edit(body: """{"type":"DailyHoursCap","maxHoursPerDay":99}"""), default);
        var badTemplate = await h.Service().EditAsync(set, policy, Edit(template: "{{line.nope}}"), default);
        var badConsequence = await h.Service().EditAsync(set, policy, Edit(consequence: "Explode"), default);
        var nothing = await h.Service().EditAsync(set, policy, Edit(), default);

        badBody.Errors.Should().Contain(e => e.Field == "ruleBody");
        badTemplate.Errors.Should().Contain(e => e.Field == "messageTemplate");
        badConsequence.Errors.Should().Contain(e => e.Field == "consequence");
        nothing.Errors.Should().Contain(e => e.Field == "edit");
        new[] { badBody, badTemplate, badConsequence, nothing }.Should().OnlyContain(o => o.Kind == RuleSetOutcomeKind.Invalid);
        h.Writes.Should().BeEmpty();
    }

    [Theory]
    [InlineData("Existence")]
    [InlineData("Threshold")]
    [InlineData("100000002")]
    public async Task Edit_ToAnOntologyRuleType_IsRefusedAsWrongDomain_AndWritesNothing(string ruleType)
    {
        var h = new Harness();
        var set = h.RuleSet();
        var (policy, _) = h.Rule(set);

        var outcome = await h.Service().EditAsync(set, policy, Edit(type: ruleType), default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Invalid);
        outcome.Errors.Should().Contain(e => e.Field == "ruleType" && e.Reason == "rule_type_wrong_domain");
        h.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task Edit_ToAnotherBillingType_NeedsAMatchingBody()
    {
        var h = new Harness();
        var set = h.RuleSet();
        var (policy, version) = h.Rule(set);

        var mismatch = await h.Service().EditAsync(set, policy, Edit(type: "TimeIncrement"), default);
        var ok = await h.Service().EditAsync(set, policy, Edit(type: "TimeIncrement", body: IncrementBody, template: "Hours must be a multiple of {{rule.incrementHours}}."), default);

        mismatch.Kind.Should().Be(RuleSetOutcomeKind.Invalid);
        ok.Kind.Should().Be(RuleSetOutcomeKind.Done);
        h.Row("sprk_policyversion", version).GetAttributeValue<OptionSetValue>("sprk_ruletype").Value.Should().Be((int)RuleType.TimeIncrement);
    }

    [Fact]
    public async Task Edit_Decide_Publish_OfAMissingOrForeignRule_Is404_WithZeroWrites()
    {
        var h = new Harness();
        var set = h.RuleSet();
        var other = h.RuleSet();
        var (foreign, _) = h.Rule(other, "BILL-OTHER", ProposalState.Accepted);
        var service = h.Service();

        var missing = await service.EditAsync(set, Guid.NewGuid(), Edit(headline: "x"), default);
        var inAnotherSet = await service.EditAsync(set, foreign, Edit(headline: "x"), default);
        var unknownSet = await service.DecideAsync(Guid.NewGuid(), foreign, true, default);

        missing.ErrorCode.Should().Be("ruleset.proposal_not_found");
        inAnotherSet.ErrorCode.Should().Be("ruleset.proposal_not_found");
        unknownSet.ErrorCode.Should().Be("ruleset.not_found");
        h.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task Edit_OnARetiredRuleSet_Is409()
    {
        var h = new Harness();
        var set = h.RuleSet(RuleSetService.RuleSetRetired);
        var (policy, _) = h.Rule(set);

        var outcome = await h.Service().EditAsync(set, policy, Edit(headline: "x"), default);

        outcome.ErrorCode.Should().Be("ruleset.retired");
        outcome.StatusCode.Should().Be(409);
        h.Writes.Should().BeEmpty();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Accept / reject
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Accept_And_Reject_SetTheProposalState_OnTheDraftVersion_AndNothingElse()
    {
        var h = new Harness();
        var set = h.RuleSet();
        var (policy, version) = h.Rule(set);
        var service = h.Service();

        var accepted = await service.DecideAsync(set, policy, accept: true, default);
        var rejected = await service.DecideAsync(set, policy, accept: false, default);

        accepted.Value!.State.Should().Be("Accepted");
        rejected.Value!.State.Should().Be("Rejected");
        h.Writes.Should().OnlyContain(w => w.Kind == Write.Update && w.Entity == "sprk_policyversion" && w.Id == version);
        h.Writes.Should().OnlyContain(w => w.Fields.Keys.Single() == "sprk_proposalstatus");
        h.Row("sprk_policy", policy).Contains("sprk_currentversion").Should().BeFalse("deciding never puts anything in force");
    }

    [Fact]
    public async Task Accept_OfAnInvalidRule_IsRefusedWithItsErrors_AndWritesNothing()
    {
        var h = new Harness();
        var set = h.RuleSet();
        var (policy, _) = h.Rule(set, body: """{"type":"DailyHoursCap","maxHoursPerDay":0}""", consequence: null);

        var outcome = await h.Service().DecideAsync(set, policy, accept: true, default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Invalid);
        outcome.Errors.Select(e => e.Field).Should().Contain(new[] { "ruleBody", "consequence" });
        h.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task Decide_OnAVersionInForce_Is409_AndWritesNothing()
    {
        var h = new Harness();
        var set = h.RuleSet(RuleSetService.RuleSetPublished);
        var (policy, _) = h.Rule(set, state: ProposalState.Accepted, current: true);

        var accept = await h.Service().DecideAsync(set, policy, true, default);
        var reject = await h.Service().DecideAsync(set, policy, false, default);

        accept.ErrorCode.Should().Be("ruleset.proposal_in_force");
        reject.ErrorCode.Should().Be("ruleset.proposal_in_force");
        accept.StatusCode.Should().Be(409);
        h.Writes.Should().BeEmpty();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Publish
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Publish_PutsEveryAcceptedRuleInForce_ThroughVersionsThenPoliciesThenTheRuleSet()
    {
        var h = new Harness();
        var set = h.RuleSet(visible: true);
        var (p1, v1) = h.Rule(set, "BILL-A", ProposalState.Accepted);
        var (p2, v2) = h.Rule(set, "BILL-B", ProposalState.Accepted, type: "TimeIncrement", body: IncrementBody,
            quote: "Bill in tenths.");
        h.Row("sprk_policyversion", v2)["sprk_messagetemplate"] = "Hours are not a multiple of {{rule.incrementHours}}.";

        var outcome = await h.Service().PublishAsync(set, Effective, default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Done);
        outcome.Value!.Status.Should().Be("Published");
        outcome.Value.Published.Select(p => p.VersionId).Should().BeEquivalentTo(new[] { v1, v2 });
        h.Order.Should().Equal("bulk:sprk_policyversion", "bulk:sprk_policy", "update:sprk_ruleset");
        foreach (var (policy, version) in new[] { (p1, v1), (p2, v2) })
        {
            var row = h.Row("sprk_policy", policy);
            row.GetAttributeValue<EntityReference>("sprk_currentversion").Id.Should().Be(version);
            row.GetAttributeValue<bool>("sprk_enabled").Should().BeTrue();
            h.Row("sprk_policyversion", version).GetAttributeValue<DateTime>("sprk_inforcefrom").Should().Be(Utc(2026, 11, 1));
        }

        var set1 = h.Row("sprk_ruleset", set);
        set1.GetAttributeValue<OptionSetValue>("statuscode").Value.Should().Be(RuleSetService.RuleSetPublished);
        set1.GetAttributeValue<DateTime>("sprk_effectivefrom").Should().Be(Utc(2026, 11, 1));
    }

    [Theory]
    [InlineData(true, "Clause quotes will be visible to firms.")]
    [InlineData(false, "Clause quotes will not be visible to firms.")]
    public async Task Publish_StatesWhetherClauseQuotesWillBeVisibleToFirms(bool visible, string notice)
    {
        var h = new Harness();
        var set = h.RuleSet(visible: visible);
        h.Rule(set, state: ProposalState.Accepted);

        var outcome = await h.Service().PublishAsync(set, Effective, default);

        outcome.Value!.ClauseQuotesVisibleToFirms.Should().Be(visible);
        outcome.Value.ClauseVisibilityNotice.Should().Be(notice);
    }

    [Fact]
    public async Task Publish_WithOneInvalidAcceptedRule_RefusesTheWholePublishWithPerRuleErrors_AndWritesNothing()
    {
        var h = new Harness();
        var set = h.RuleSet();
        h.Rule(set, "BILL-GOOD", ProposalState.Accepted);
        var (bad, _) = h.Rule(set, "BILL-BAD", ProposalState.Accepted, body: """{"type":"DailyHoursCap","maxHoursPerDay":0}""");

        var outcome = await h.Service().PublishAsync(set, Effective, default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Invalid);
        outcome.ErrorCode.Should().Be("ruleset.publish_invalid");
        outcome.Errors.Should().OnlyContain(e => e.PolicyId == bad);
        outcome.Errors.Should().Contain(e => e.Field == "ruleBody" && e.Reason == "schema_invalid");
        h.Writes.Should().BeEmpty("nothing is published when one rule is invalid");
        h.Row("sprk_ruleset", set).GetAttributeValue<OptionSetValue>("statuscode").Value.Should().Be(RuleSetService.RuleSetDraft);
    }

    [Fact]
    public async Task Publish_RefusesAnAcceptedRuleOfAnotherDomainsType_BecauseSaveValidationIsBillingOnly()
    {
        var h = new Harness();
        var set = h.RuleSet();
        h.Rule(set, "BILL-ONT", ProposalState.Accepted, type: "Existence", body: """{"type":"Existence"}""");

        var outcome = await h.Service().PublishAsync(set, Effective, default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Invalid);
        outcome.Errors.Should().Contain(e => e.Field == "ruleType" && e.Reason == "rule_type_wrong_domain");
        h.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task Publish_ARuleWithNoConsequence_IsRefused()
    {
        var h = new Harness();
        var set = h.RuleSet();
        h.Rule(set, state: ProposalState.Accepted, consequence: null);

        var outcome = await h.Service().PublishAsync(set, Effective, default);

        outcome.Errors.Should().ContainSingle(e => e.Field == "consequence");
        h.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task Publish_NeverPutsProposedOrRejectedRulesInForce_AndCountsThem()
    {
        var h = new Harness();
        var set = h.RuleSet();
        var (accepted, vAccepted) = h.Rule(set, "BILL-A", ProposalState.Accepted);
        var (proposed, _) = h.Rule(set, "BILL-P", ProposalState.Proposed);
        var (rejected, _) = h.Rule(set, "BILL-R", ProposalState.Rejected);

        var outcome = await h.Service().PublishAsync(set, Effective, default);

        outcome.Value!.Published.Should().ContainSingle().Which.VersionId.Should().Be(vAccepted);
        outcome.Value.NotPublishedProposed.Should().Be(1);
        outcome.Value.NotPublishedRejected.Should().Be(1);
        foreach (var policy in new[] { proposed, rejected })
        {
            h.Row("sprk_policy", policy).Contains("sprk_currentversion").Should().BeFalse();
            h.Row("sprk_policy", policy).GetAttributeValue<bool>("sprk_enabled").Should().BeFalse();
        }

        h.Writes.Should().NotContain(w => w.Entity == "sprk_policy" && w.Id != accepted);
    }

    [Fact]
    public async Task Publish_WithNothingAccepted_IsRefused_AndWritesNothing()
    {
        var h = new Harness();
        var set = h.RuleSet();
        h.Rule(set, "BILL-P", ProposalState.Proposed);
        h.Rule(set, "BILL-R", ProposalState.Rejected);

        var outcome = await h.Service().PublishAsync(set, Effective, default);

        outcome.ErrorCode.Should().Be("ruleset.nothing_to_publish");
        outcome.StatusCode.Should().Be(400);
        h.Writes.Should().BeEmpty();
        h.Row("sprk_ruleset", set).GetAttributeValue<OptionSetValue>("statuscode").Value.Should().Be(RuleSetService.RuleSetDraft);
    }

    [Fact]
    public async Task EditThenPublish_StampsThePredecessorsInForceTo_MovesThePointer_AndLeavesThePredecessorOtherwiseUnchanged()
    {
        var h = new Harness();
        var set = h.RuleSet(RuleSetService.RuleSetPublished, effectiveFrom: Utc(2026, 1, 1));
        var (policy, v1) = h.Rule(set, state: ProposalState.Accepted, current: true, from: Utc(2026, 1, 1));
        var service = h.Service();
        var edited = await service.EditAsync(set, policy, Edit(body: CapBody8, template: "Over {{rule.maxHoursPerDay}} hours."), default);
        var v2 = edited.Value!.Proposal.VersionId;
        await service.DecideAsync(set, policy, accept: true, default);
        var v1Before = h.Row("sprk_policyversion", v1).Attributes.ToDictionary(a => a.Key, a => a.Value);

        var outcome = await service.PublishAsync(set, Effective, default);

        outcome.Value!.Published.Should().ContainSingle().Which.Should().Be(new PublishedRuleVersion(policy, v2, 2, v1));
        h.Row("sprk_policy", policy).GetAttributeValue<EntityReference>("sprk_currentversion").Id.Should().Be(v2);
        var predecessor = h.Row("sprk_policyversion", v1);
        predecessor.GetAttributeValue<DateTime>("sprk_inforceto").Should().Be(Utc(2026, 11, 1));
        predecessor.Attributes.Where(a => a.Key != "sprk_inforceto").Should().BeEquivalentTo(v1Before, "only the end stamp is written to a published version");
        h.Row("sprk_policyversion", v2).GetAttributeValue<DateTime>("sprk_inforcefrom").Should().Be(Utc(2026, 11, 1));
        h.Row("sprk_ruleset", set).GetAttributeValue<DateTime>("sprk_effectivefrom").Should().Be(Utc(2026, 1, 1), "an already effective set keeps its start");
    }

    [Fact]
    public async Task Publish_OfMoreThan1000Rules_WritesInTransactionsOfAtMost1000_VersionsThenPolicies_RuleSetLast()
    {
        var h = new Harness();
        var set = h.RuleSet();
        for (var i = 0; i < 1001; i++)
        {
            h.Rule(set, "BILL-" + i, ProposalState.Accepted);
        }

        var outcome = await h.Service().PublishAsync(set, Effective, default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Done);
        outcome.Value!.Published.Should().HaveCount(1001);
        h.BulkCalls.Should().Equal(
            ("sprk_policyversion", 1000), ("sprk_policyversion", 1), ("sprk_policy", 1000), ("sprk_policy", 1));
        h.Order.Last().Should().Be("update:sprk_ruleset");
    }

    [Fact]
    public async Task Get_ReadsTheVersionsOfManyRulesInBatchesOfAtMost500InValues()
    {
        var h = new Harness();
        var set = h.RuleSet();
        for (var i = 0; i < 501; i++)
        {
            h.Rule(set, "BILL-" + i);
        }

        var outcome = await h.Service().GetAsync(set, default);

        outcome.Value!.Proposals.Should().HaveCount(501);
        var inLists = h.Queries
            .SelectMany(q => q.Criteria.Conditions.Concat(q.Criteria.Filters.SelectMany(f => f.Conditions)))
            .Where(c => c.Operator == ConditionOperator.In)
            .ToList();
        inLists.Should().NotBeEmpty();
        inLists.Should().OnlyContain(c => c.Values.Count <= 500, "SQL Server refuses about 2,100 parameters in one statement");
        inLists.Max(c => c.Values.Count).Should().Be(500);
    }

    [Fact]
    public async Task Publish_DoesNotTouchRulesAlreadyInForce()
    {
        var h = new Harness();
        var set = h.RuleSet(RuleSetService.RuleSetPublished);
        var (inForce, vInForce) = h.Rule(set, "BILL-LIVE", ProposalState.Accepted, current: true);
        var (draft, vDraft) = h.Rule(set, "BILL-NEW", ProposalState.Accepted);

        var outcome = await h.Service().PublishAsync(set, Effective, default);

        outcome.Value!.Published.Should().ContainSingle().Which.VersionId.Should().Be(vDraft);
        h.Writes.Should().NotContain(w => w.Id == vInForce || w.Id == inForce);
    }

    [Fact]
    public async Task Publish_WithAnEffectiveDateBeforeThePredecessorStarted_IsRefused()
    {
        var h = new Harness();
        var set = h.RuleSet(RuleSetService.RuleSetPublished);
        var (policy, _) = h.Rule(set, state: ProposalState.Accepted, current: true, from: Utc(2026, 12, 1));
        h.AddVersion(policy, 2, ProposalState.Accepted, body: CapBody8);

        var outcome = await h.Service().PublishAsync(set, Effective, default);

        outcome.Errors.Should().ContainSingle(e => e.Field == "effectiveDate");
        h.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task Publish_WhenThePolicyTransactionFails_LeavesTheOldRulesInForce_NotPublished_AndARetryCompletes()
    {
        var h = new Harness();
        var set = h.RuleSet(RuleSetService.RuleSetPublished);
        var (policy, v1) = h.Rule(set, state: ProposalState.Accepted, current: true, from: Utc(2026, 1, 1));
        var v2 = h.AddVersion(policy, 2, ProposalState.Accepted, body: CapBody8);
        var service = h.Service();
        h.FailBulkFor = entity => entity == "sprk_policy";

        var failed = await service.PublishAsync(set, Effective, default);

        failed.Kind.Should().Be(RuleSetOutcomeKind.Refused);
        failed.ErrorCode.Should().Be("ruleset.write_failed");
        h.Row("sprk_policy", policy).GetAttributeValue<EntityReference>("sprk_currentversion").Id.Should().Be(v1, "nothing is in force before the pointer moves");
        h.Row("sprk_policyversion", v1).GetAttributeValue<DateTime?>("sprk_inforceto").Should().BeNull(
            "the end date and the pointer move together (D-14): the pointer did not move, so the end date the first write made is taken back");

        h.FailBulkFor = null;
        var retried = await service.PublishAsync(set, Effective, default);

        retried.Kind.Should().Be(RuleSetOutcomeKind.Done);
        retried.Value!.Published.Single().VersionId.Should().Be(v2);
        h.Row("sprk_policy", policy).GetAttributeValue<EntityReference>("sprk_currentversion").Id.Should().Be(v2);
        h.Row("sprk_policyversion", v1).GetAttributeValue<DateTime>("sprk_inforceto").Should().Be(Utc(2026, 11, 1));
        h.Row("sprk_policyversion", v2).GetAttributeValue<DateTime>("sprk_inforcefrom").Should().Be(Utc(2026, 11, 1));
    }

    /// <summary>A published rule with a drafted, accepted successor, and a pointer move that fails (review probe 2, 3).</summary>
    private static async Task<(Harness H, RuleSetService Service, Guid Set, Guid Policy, Guid V1, Guid V2)> FailedPointerMoveAsync(Func<string, bool>? failBulk = null)
    {
        var h = new Harness();
        var set = h.RuleSet(RuleSetService.RuleSetPublished);
        var (policy, v1) = h.Rule(set, state: ProposalState.Accepted, current: true, from: Utc(2026, 1, 1));
        var v2 = h.AddVersion(policy, 2, ProposalState.Accepted, body: CapBody8);
        var service = h.Service();
        h.FailBulkFor = failBulk ?? (entity => entity == "sprk_policy");

        var failed = await service.PublishAsync(set, Effective, default);

        failed.ErrorCode.Should().Be("ruleset.write_failed");
        return (h, service, set, policy, v1, v2);
    }

    [Fact]
    public async Task Publish_AfterAFailedPointerMove_ARetryWithAnotherDate_PutsTheNewDateOnBothVersions_LeavingNoGap()
    {
        var (h, service, set, policy, v1, v2) = await FailedPointerMoveAsync();
        h.FailBulkFor = null;

        var retried = await service.PublishAsync(set, new DateOnly(2026, 12, 1), default);

        retried.Kind.Should().Be(RuleSetOutcomeKind.Done);
        h.Row("sprk_policy", policy).GetAttributeValue<EntityReference>("sprk_currentversion").Id.Should().Be(v2);
        h.Row("sprk_policyversion", v1).GetAttributeValue<DateTime>("sprk_inforceto").Should().Be(Utc(2026, 12, 1));
        h.Row("sprk_policyversion", v2).GetAttributeValue<DateTime>("sprk_inforcefrom").Should().Be(Utc(2026, 12, 1),
            "the old version ends on the day the new one starts");
    }

    [Fact]
    public async Task Publish_WhenTheEndDateCannotBeTakenBack_TheFailureIsLoggedAsAnError_AndARetryWithAnotherDateRestampsIt()
    {
        var versionWrites = 0;
        // The first version write (the stamps) lands; the pointer move fails; the take-back (a second version write) fails too.
        var (h, service, set, policy, v1, v2) = await FailedPointerMoveAsync(e => e == "sprk_policy" || (e == "sprk_policyversion" && ++versionWrites > 1));

        h.Row("sprk_policyversion", v1).GetAttributeValue<DateTime>("sprk_inforceto").Should().Be(Utc(2026, 11, 1), "the take-back failed");
        h.Logs.Should().Contain(l => l.Level == LogLevel.Error && l.Message.Contains(v1.ToString()),
            "an end date that could not be taken back is named in an Error");
        h.FailBulkFor = null;

        var retried = await service.PublishAsync(set, new DateOnly(2026, 12, 1), default);

        retried.Kind.Should().Be(RuleSetOutcomeKind.Done);
        h.Row("sprk_policy", policy).GetAttributeValue<EntityReference>("sprk_currentversion").Id.Should().Be(v2);
        h.Row("sprk_policyversion", v1).GetAttributeValue<DateTime>("sprk_inforceto").Should().Be(Utc(2026, 12, 1),
            "the old version is re-stamped when its end date differs from the new effective date");
    }

    [Fact]
    public async Task Publish_AfterAFailedPointerMove_RejectingTheDraftLeavesNoEndDateOnTheVersionInForce()
    {
        var (h, service, set, policy, v1, _) = await FailedPointerMoveAsync();
        h.FailBulkFor = null;

        (await service.DecideAsync(set, policy, accept: false, default)).Kind.Should().Be(RuleSetOutcomeKind.Done);
        (await service.PublishAsync(set, Effective, default)).ErrorCode.Should().Be("ruleset.nothing_to_publish");

        h.Row("sprk_policy", policy).GetAttributeValue<EntityReference>("sprk_currentversion").Id.Should().Be(v1);
        h.Row("sprk_policyversion", v1).GetAttributeValue<DateTime?>("sprk_inforceto").Should().BeNull(
            "a rule that stays in force must not carry a permanent end date");
    }

    [Fact]
    public async Task Publish_WhenTheVersionWritesFailPartWay_TakesBackTheEndDatesTheEarlierChunkWrote()
    {
        var h = new Harness();
        var set = h.RuleSet(RuleSetService.RuleSetPublished);
        for (var i = 0; i < 501; i++) // 2 version updates each = 1,002: one chunk of 1,000 and one of 2
        {
            var (policy, _) = h.Rule(set, "BILL-" + i, ProposalState.Accepted, current: true, from: Utc(2026, 1, 1));
            h.AddVersion(policy, 2, ProposalState.Accepted, body: CapBody8);
        }

        var versionWrites = 0;
        h.FailBulkFor = e => e == "sprk_policyversion" && ++versionWrites == 2;

        var failed = await h.Service().PublishAsync(set, Effective, default);

        failed.ErrorCode.Should().Be("ruleset.write_failed");
        var numberOf = h.Rows["sprk_policyversion"].ToDictionary(v => v.Id, v => v.GetAttributeValue<int>("sprk_versionnumber"));
        h.Rows["sprk_policy"].Should().OnlyContain(p => numberOf[((EntityReference)p["sprk_currentversion"]).Id] == 1, "no pointer moved");
        h.Rows["sprk_policyversion"].Where(v => numberOf[v.Id] == 1).Should()
            .OnlyContain(v => !v.Contains("sprk_inforceto"), "no version in force keeps an end date after a failed publish");
    }

    [Fact]
    public async Task Publish_WhenOnlySomePolicyChunksMove_KeepsTheEndDatesOfTheMovedRules_AndTakesBackTheRest()
    {
        var h = new Harness();
        var set = h.RuleSet(RuleSetService.RuleSetPublished);
        for (var i = 0; i < 1001; i++) // 1,001 pointer moves: one chunk of 1,000 and one of 1
        {
            var (policy, _) = h.Rule(set, "BILL-" + i, ProposalState.Accepted, current: true, from: Utc(2026, 1, 1));
            h.AddVersion(policy, 2, ProposalState.Accepted, body: CapBody8);
        }

        var policyWrites = 0;
        h.FailBulkFor = e => e == "sprk_policy" && ++policyWrites == 2;
        var service = h.Service();

        (await service.PublishAsync(set, Effective, default)).ErrorCode.Should().Be("ruleset.write_failed");

        var numberOf = h.Rows["sprk_policyversion"].ToDictionary(v => v.Id, v => v.GetAttributeValue<int>("sprk_versionnumber"));
        var oldVersions = h.Rows["sprk_policyversion"].Where(v => numberOf[v.Id] == 1).ToList();
        var moved = h.Rows["sprk_policy"].Where(p => numberOf[((EntityReference)p["sprk_currentversion"]).Id] == 2).Select(p => p.Id).ToHashSet();
        moved.Should().HaveCount(1000);
        foreach (var old in oldVersions)
        {
            var policyMoved = moved.Contains(((EntityReference)old["sprk_policy"]).Id);
            old.GetAttributeValue<DateTime?>("sprk_inforceto").Should().Be(policyMoved ? Utc(2026, 11, 1) : null,
                "a version that was replaced keeps its end date; one that is still in force gets it taken back");
        }

        h.FailBulkFor = null;
        var retried = await service.PublishAsync(set, Effective, default);
        retried.Value!.Published.Should().HaveCount(1, "the 1,000 that moved are already in force; the retry finishes the last one");
    }

    [Fact]
    public async Task Publish_WhenTheCallerDisconnectsDuringTheWrites_StillFinishesAllThreeWrites()
    {
        using var cts = new CancellationTokenSource();
        var h = new Harness { OnBulk = _ => cts.Cancel() }; // the client goes away as the first write starts
        var set = h.RuleSet(RuleSetService.RuleSetPublished);
        var (policy, v1) = h.Rule(set, state: ProposalState.Accepted, current: true, from: Utc(2026, 1, 1));
        var v2 = h.AddVersion(policy, 2, ProposalState.Accepted, body: CapBody8);

        var outcome = await h.Service().PublishAsync(set, Effective, cts.Token);

        cts.IsCancellationRequested.Should().BeTrue("the test disconnected the caller during the writes");
        outcome.Kind.Should().Be(RuleSetOutcomeKind.Done);
        h.Row("sprk_policy", policy).GetAttributeValue<EntityReference>("sprk_currentversion").Id.Should().Be(v2);
        h.Row("sprk_policyversion", v1).GetAttributeValue<DateTime>("sprk_inforceto").Should().Be(Utc(2026, 11, 1));
        h.Row("sprk_ruleset", set).GetAttributeValue<OptionSetValue>("statuscode").Value.Should().Be(RuleSetService.RuleSetPublished);
    }

    [Fact]
    public async Task Publish_CancelledBeforeAnyWrite_WritesNothing_AndRethrows()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var h = new Harness();
        var set = h.RuleSet();
        h.Rule(set, state: ProposalState.Accepted);

        var act = () => h.Service().PublishAsync(set, Effective, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        h.Writes.Should().BeEmpty();
    }

    [Theory]
    [InlineData("firmNoFirm")]
    [InlineData("noScope")]
    [InlineData("defaultWithFirm")]
    public async Task Publish_OfARuleSetWhoseAssignmentIsInconsistent_IsRefused_AndWritesNothing(string scenario)
    {
        var h = new Harness();
        var set = scenario switch
        {
            "firmNoFirm" => h.RuleSet(scope: (int)RuleSetScope.Firm),
            "noScope" => h.RuleSet(scope: null),
            _ => h.RuleSet(scope: (int)RuleSetScope.ClientDefault, firm: FirmId),
        };
        h.Rule(set, state: ProposalState.Accepted);

        var outcome = await h.Service().PublishAsync(set, Effective, default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Invalid);
        outcome.Errors.Should().Contain(e => e.Field == "scope");
        h.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task Publish_OfARetiredRuleSet_Is409_AndAnUnknownOrForeignDomainOneIs404()
    {
        var h = new Harness();
        var retired = h.RuleSet(RuleSetService.RuleSetRetired);
        var ontology = h.RuleSet(domain: RuleDomains.PolicyDomainOntology);
        h.Rule(retired, state: ProposalState.Accepted);
        h.Rule(ontology, state: ProposalState.Accepted);
        var service = h.Service();

        (await service.PublishAsync(retired, Effective, default)).ErrorCode.Should().Be("ruleset.retired");
        (await service.PublishAsync(ontology, Effective, default)).ErrorCode.Should().Be("ruleset.not_found");
        (await service.PublishAsync(Guid.NewGuid(), Effective, default)).ErrorCode.Should().Be("ruleset.not_found");
        h.Writes.Should().BeEmpty();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Assignment and visibility
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Assign_MovesTheSetBetweenTheClientDefaultAndAFirm()
    {
        var h = new Harness();
        var set = h.RuleSet();
        var service = h.Service();

        var toFirm = await service.AssignAsync(set, RuleSetScope.Firm, FirmId, default);
        var back = await service.AssignAsync(set, RuleSetScope.ClientDefault, null, default);

        toFirm.Value!.Scope.Should().Be("Firm");
        toFirm.Value.FirmId.Should().Be(FirmId);
        back.Value!.Scope.Should().Be("ClientDefault");
        back.Value.FirmId.Should().BeNull();
        h.Row("sprk_ruleset", set).Contains("sprk_firm").Should().BeFalse("moving to the client default clears the firm");
    }

    [Theory]
    [InlineData("firmNoFirm")]
    [InlineData("defaultWithFirm")]
    [InlineData("unknownFirm")]
    public async Task Assign_InvalidCombinations_AreRefused_AndWriteNothing(string scenario)
    {
        var h = new Harness();
        var set = h.RuleSet();

        var outcome = scenario switch
        {
            "firmNoFirm" => await h.Service().AssignAsync(set, RuleSetScope.Firm, null, default),
            "defaultWithFirm" => await h.Service().AssignAsync(set, RuleSetScope.ClientDefault, FirmId, default),
            _ => await h.Service().AssignAsync(set, RuleSetScope.Firm, Guid.NewGuid(), default),
        };

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Invalid);
        h.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task SetVisibility_TogglesTheFlag_AndARetiredOrForeignRuleSetIsRefused()
    {
        var h = new Harness();
        var set = h.RuleSet(visible: true);
        var retired = h.RuleSet(RuleSetService.RuleSetRetired);
        var ontology = h.RuleSet(domain: RuleDomains.PolicyDomainOntology);
        var service = h.Service();

        var off = await service.SetVisibilityAsync(set, false, default);

        off.Value!.VisibleToFirms.Should().BeFalse();
        h.Row("sprk_ruleset", set).GetAttributeValue<bool>("sprk_visibletofirms").Should().BeFalse();
        (await service.SetVisibilityAsync(retired, true, default)).ErrorCode.Should().Be("ruleset.retired");
        (await service.SetVisibilityAsync(ontology, true, default)).ErrorCode.Should().Be("ruleset.not_found");
        h.Writes.Should().ContainSingle();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Clause visibility (the one decision 130 / 134 / 135 call)
    // ═══════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(false)]
    [InlineData(null)]
    public void ForFirm_WithTheFlagOffOrUnknown_ReturnsNoClauseText(bool? flag)
    {
        var text = RuleSetClauseVisibility.ForFirm(flag, "No timekeeper may bill more than 10 hours.", "Section 4.2");

        text.Quote.Should().BeNull();
        text.SectionReference.Should().BeNull();
        text.Should().Be(FirmClauseText.None);
    }

    [Fact]
    public void ForFirm_WithTheFlagOn_ReturnsQuoteAndSectionReference_AndNeverADocumentLink()
    {
        var text = RuleSetClauseVisibility.ForFirm(true, "No timekeeper may bill more than 10 hours.", "Section 4.2");

        text.Quote.Should().Be("No timekeeper may bill more than 10 hours.");
        text.SectionReference.Should().Be("Section 4.2");
        typeof(FirmClauseText).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name).Where(n => n != "EqualityContract")
            .Should().BeEquivalentTo(new[] { "Quote", "SectionReference" }, "the result has no member that could carry the source document or a link to it");
        RuleSetClauseVisibility.ForFirm(true, " ", null).Should().Be(FirmClauseText.None);
    }

    [Fact]
    public async Task GetFirmClause_ReadsTheRuleSetFlag_AndFailsClosedOnEveryOtherPath()
    {
        var h = new Harness();
        var on = h.RuleSet(RuleSetService.RuleSetPublished, visible: true);
        var off = h.RuleSet(RuleSetService.RuleSetPublished, visible: false);
        var draftSet = h.RuleSet(RuleSetService.RuleSetDraft, visible: true);
        var ontologySet = h.RuleSet(RuleSetService.RuleSetPublished, visible: true, domain: RuleDomains.PolicyDomainOntology);
        var (_, vOn) = h.Rule(on, "BILL-ON", ProposalState.Accepted, current: true);
        var (_, vOff) = h.Rule(off, "BILL-OFF", ProposalState.Accepted, current: true);
        var (_, vEnded) = h.Rule(on, "BILL-ENDED", ProposalState.Accepted, to: Utc(2026, 6, 1));
        var (_, vNeverInForce) = h.Rule(on, "BILL-DRAFT", ProposalState.Accepted);
        var (_, vDraftSet) = h.Rule(draftSet, "BILL-DS", ProposalState.Accepted, current: true);
        var (_, vOntologyPolicy) = h.Rule(on, "ONT-P", ProposalState.Accepted, current: true, domain: RuleDomains.PolicyDomainOntology);
        var (_, vOntologySet) = h.Rule(ontologySet, "BILL-OS", ProposalState.Accepted, current: true);
        var service = h.Service();

        (await service.GetFirmClauseAsync(vOn, default)).Should().Be(new FirmClauseText("No timekeeper may bill more than 10 hours a day.", "Section 4.2, p. 7"));
        (await service.GetFirmClauseAsync(vEnded, default)).Quote.Should().NotBeNull("a superseded published version's clause is still citable");
        (await service.GetFirmClauseAsync(vOff, default)).Should().Be(FirmClauseText.None, "the flag is off");
        (await service.GetFirmClauseAsync(vNeverInForce, default)).Should().Be(FirmClauseText.None, "a draft's clause is never shown");
        (await service.GetFirmClauseAsync(vDraftSet, default)).Should().Be(FirmClauseText.None, "the rule set is not Published");
        (await service.GetFirmClauseAsync(vOntologyPolicy, default)).Should().Be(FirmClauseText.None, "an Ontology policy has no firm clause");
        (await service.GetFirmClauseAsync(vOntologySet, default)).Should().Be(FirmClauseText.None, "an Ontology rule set is not a billing rule set");
        (await service.GetFirmClauseAsync(Guid.NewGuid(), default)).Should().Be(FirmClauseText.None);
    }

    [Fact]
    public async Task TurningTheFlagOff_StopsTheClauseImmediately()
    {
        var h = new Harness();
        var set = h.RuleSet(RuleSetService.RuleSetPublished, visible: true);
        var (_, version) = h.Rule(set, state: ProposalState.Accepted, current: true);
        var service = h.Service();

        (await service.GetFirmClauseAsync(version, default)).Quote.Should().NotBeNull();
        await service.SetVisibilityAsync(set, false, default);

        (await service.GetFirmClauseAsync(version, default)).Should().Be(FirmClauseText.None);
    }
}
