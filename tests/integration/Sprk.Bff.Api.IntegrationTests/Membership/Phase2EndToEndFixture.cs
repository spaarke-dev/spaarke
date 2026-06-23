// R3 Part 1 Phase 2 — Task 087 (2026-06-22): End-to-end integration-test
// fixture covering AC-1P2.3 through AC-1P2.8 (Phase 2 acceptance criteria).
//
// STRATEGY (Option 1 — In-memory fakes per task 087 harness):
//   The Service Bus topic `sprk-membership-changes` is operator-deploy-gated
//   (task 071 ❌ blocked-operator). For the CI baseline we substitute the
//   wire transport with in-memory test doubles that preserve the FLOW
//   contract — publisher → consumer handoff → junction write → cache
//   invalidation — without standing up Azure. This proves AC-1P2.3..8
//   against the registered production types (NOT the Null-Object peers):
//
//     • IMembershipEventPublisher → CapturingMembershipEventPublisher
//         records every published event AND forwards it synchronously to
//         the registered IMembershipJunctionUpdater. Simulates the topic
//         + subscription consumer in a single in-process hop. Fire-and-
//         forget contract (FR-2P2.6 + Q2) preserved — handler exceptions
//         are caught + logged.
//
//     • IMembershipCacheInvalidator → SpyMembershipCacheInvalidator
//         records every (personId, entityLogicalName, correlationId) call.
//         No Redis dependency. The real MembershipJunctionUpdater (task 084)
//         invokes this after every junction write, so spy invocations are
//         direct evidence of AC-1P2.7.
//
//     • IDataverseService → Moq Loose mock backed by an in-memory junction
//         store keyed on the 5-tuple natural key (matches sprk_uea_natural_key).
//         Also serves the AAD-oid → systemuserid systemuser lookup for the
//         membership endpoint identity-resolution path (AC-1P2.6 — Phase 1A
//         contract path) and a parent-entity store for the reconciliation
//         job's source-of-truth scan (AC-1P2.3 orphan + missing junction
//         detection). Moq Loose returns default(T) for unmocked members so
//         we don't have to implement every IDataverseService method.
//
//     • IOfficeService → Moq Loose mock returning a deterministic 201
//         Created for /api/office/quickcreate/matter requests so the
//         integration test can drive the real OfficeEndpoints code path
//         without touching Dataverse. The event-publish call hangs off
//         the endpoint, NOT the service, so this stub doesn't interfere
//         with the test's publish-assertion.
//
//     • IMembershipResolverService → StubMembershipResolverService
//         tiny in-memory resolver that projects from the junction store
//         (proving the Phase 1A contract is unchanged after the Phase 2
//         swap-in — AC-1P2.6).
//
// LIVE-MODE deferred to post-operator-deploy (task 071):
//   Tests carrying [Trait("Category","Live")] connect to a real Service
//   Bus topic + Redis instance via env vars SPAARKE_SB_NAMESPACE +
//   SPAARKE_REDIS_CONNECTION. They auto-skip when env vars are absent
//   (the default state today). The runbook for activating these post-
//   deploy lives at projects/.../notes/phase2-live-e2e-runbook.md.
//
// PERMISSION BOUNDARY (per CLAUDE.md §3): this file ships under tests/
// only — no .claude/ writes. The fixture mirrors the canonical test-
// fixture pattern (TransitiveMembershipIntegrationFixture, task 054;
// AdminJobsIntegrationFixture, task 025) per
// test-fixture-contracts.md §F.2 (fixture-config FIRST inspection
// protocol — Program.cs validators all satisfied by the canonical config
// key set).
//
// Reference: projects/spaarke-platform-foundations-r3/spec.md AC-1P2.3
//            through AC-1P2.8, NFR-03;
//            projects/spaarke-platform-foundations-r3/notes/
//            event-source-inventory.md (AC-1P2.3 inventory artifact);
//            .claude/adr/ADR-032-bff-nullobject-kill-switch.md (kill-switch
//            peers swapped OUT for real types in this fixture so the FLOW
//            is exercised);
//            sibling Membership/TransitiveMembershipIntegrationFixture.cs
//            (canonical fixture conventions mirrored here).

using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Ai.Membership.Events;
using Sprk.Bff.Api.Services.Office;

namespace Sprk.Bff.Api.IntegrationTests.Membership;

/// <summary>
/// WebApplicationFactory fixture for R3 task 087 — Phase 2 end-to-end
/// integration tests. Boots the full BFF with in-memory test doubles
/// substituting Azure-dependent collaborators (publisher → topic transport,
/// Redis pub/sub) while keeping the production junction-write handler
/// (<see cref="MembershipJunctionUpdater"/>) live so AC-1P2.3..8 are
/// exercised against real code paths.
/// </summary>
public sealed class Phase2EndToEndFixture : WebApplicationFactory<Program>
{
    /// <summary>
    /// In-memory publisher that captures every published event and forwards
    /// it synchronously to <see cref="IMembershipJunctionUpdater"/> —
    /// simulates the topic + subscription consumer in a single in-process
    /// hop. Tests inspect <see cref="CapturingMembershipEventPublisher.Captured"/>
    /// to assert publish-side behavior.
    /// </summary>
    public CapturingMembershipEventPublisher CapturingPublisher { get; } = new();

    /// <summary>
    /// In-memory invalidator that records every PublishInvalidationAsync
    /// call. Tests inspect <see cref="SpyMembershipCacheInvalidator.Invocations"/>
    /// to assert AC-1P2.7 (cache invalidation on junction write).
    /// </summary>
    public SpyMembershipCacheInvalidator SpyInvalidator { get; } = new();

    /// <summary>
    /// In-memory junction + parent-entity + systemuser-lookup store shared
    /// by the Moq IDataverseService mock setup and by the
    /// <see cref="StubMembershipResolverService"/>.
    /// </summary>
    public InMemoryDataverseState DataverseState { get; } = new();

    /// <summary>The Moq mock backing IDataverseService — exposed for tests
    /// that need to assert call counts.</summary>
    public Mock<IDataverseService> DataverseMock { get; } = new(MockBehavior.Loose);

    /// <summary>The Moq mock backing IOfficeService — exposed for tests
    /// that need to assert QuickCreate invocations.</summary>
    public Mock<IOfficeService> OfficeMock { get; } = new(MockBehavior.Loose);

    /// <summary>Matter id returned by the next QuickCreate call. Reset per test.</summary>
    public Guid NextQuickCreateMatterId { get; set; } = Guid.NewGuid();

    protected override IHost CreateHost(IHostBuilder builder)
    {
        // Canonical config-key set per test-fixture-contracts.md §F.2.
        // Mirrors TransitiveMembershipIntegrationFixture + AdminJobsIntegrationFixture
        // so every Program.cs validator passes. Any new validator added later
        // needs a corresponding key here.
        builder.ConfigureHostConfiguration(config =>
        {
            var settings = new Dictionary<string, string?>
            {
                ["ConnectionStrings:ServiceBus"] = "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=dGVzdA==",
                ["Cors:AllowedOrigins:0"] = "https://localhost:5173",
                ["UAMI_CLIENT_ID"] = "test-client-id",
                ["TENANT_ID"] = "test-tenant-id",
                ["API_APP_ID"] = "test-app-id",
                ["API_CLIENT_SECRET"] = "test-secret",
                ["AzureAd:Instance"] = "https://login.microsoftonline.com/",
                ["AzureAd:TenantId"] = "test-tenant-id",
                ["AzureAd:ClientId"] = "test-app-id",
                ["AzureAd:Audience"] = "api://test-app-id",
                ["SpeAdmin:KeyVaultUri"] = "https://test-keyvault.vault.azure.net/",
                ["CosmosPersistence:Endpoint"] = "https://test.documents.azure.com:443/",
                ["CosmosPersistence:DatabaseName"] = "spaarke-ai-test",
                ["Graph:TenantId"] = "test-tenant-id",
                ["Graph:ClientId"] = "test-client-id",
                ["Graph:ClientSecret"] = "test-client-secret",
                ["Graph:UseManagedIdentity"] = "false",
                ["Graph:Scopes:0"] = "https://graph.microsoft.com/.default",
                ["Dataverse:EnvironmentUrl"] = "https://test.crm.dynamics.com",
                ["Dataverse:ServiceUrl"] = "https://test.crm.dynamics.com",
                ["Dataverse:ClientId"] = "test-client-id",
                ["Dataverse:ClientSecret"] = "test-client-secret",
                ["Dataverse:TenantId"] = "test-tenant-id",
                ["ManagedIdentity:ClientId"] = "00000000-0000-0000-0000-000000000001",
                ["ServiceBus:ConnectionString"] = "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=dGVzdA==",
                ["ServiceBus:QueueName"] = "sdap-jobs",
                ["Redis:Enabled"] = "false",
                ["OfficeRateLimit:Enabled"] = "false",
                ["DocumentIntelligence:Enabled"] = "true",
                ["DocumentIntelligence:OpenAiEndpoint"] = "https://test.openai.azure.com/",
                ["DocumentIntelligence:OpenAiKey"] = "test-key",
                ["DocumentIntelligence:OpenAiDeployment"] = "gpt-4o",
                ["DocumentIntelligence:AiSearchEndpoint"] = "https://test.search.windows.net",
                ["DocumentIntelligence:AiSearchKey"] = "test-search-key",
                ["DocumentIntelligence:RecordMatchingEnabled"] = "true",
                ["Analysis:Enabled"] = "true",
                ["AzureOpenAI:Endpoint"] = "https://test.openai.azure.com/",
                ["AzureOpenAI:ChatModelName"] = "gpt-4o",
                ["PowerBi:TenantId"] = "test-powerbi-tenant-id",
                ["PowerBi:ClientId"] = "test-powerbi-client-id",
                ["PowerBi:ClientSecret"] = "test-powerbi-client-secret",
                ["PowerBi:ApiUrl"] = "https://api.powerbi.com",
                ["PowerBi:Scope"] = "https://analysis.windows.net/.default",
                ["Reporting:ModuleEnabled"] = "false",
                ["AiSearchResilience:MaxRetryAttempts"] = "3",
                ["AiSearchResilience:CircuitBreakerFailureThreshold"] = "5",
                ["AiSearchResilience:CircuitBreakerDuration"] = "00:00:30",
                ["GraphResilience:MaxRetryAttempts"] = "3",
                ["GraphResilience:RetryDelay"] = "00:00:01",
                ["GraphResilience:CircuitBreakerFailureThreshold"] = "5",
                ["GraphResilience:CircuitBreakerDuration"] = "00:00:30",
                ["ModelSelector:IntentClassification"] = "gpt-4o-mini",
                ["ModelSelector:PlanGeneration"] = "o1-mini",
                ["ModelSelector:NodeGeneration"] = "gpt-4o",
                ["ModelSelector:ClarificationGeneration"] = "gpt-4o-mini",
                ["ModelSelector:AnalysisGeneration"] = "gpt-4o",
                ["ModelSelector:ExtractionGeneration"] = "gpt-4o-mini",
                ["ModelSelector:EmbeddingGeneration"] = "text-embedding-3-large",
                ["ModelSelector:FallbackGeneration"] = "gpt-4o",
            };
            config.AddInMemoryCollection(settings);
        });

        return base.CreateHost(builder);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        WireDataverseMock();
        WireOfficeMock();

        builder.ConfigureTestServices(services =>
        {
            // ── In-memory cache (ADR-009 — Redis disabled in tests) ──────
            services.RemoveAll<IDistributedCache>();
            services.AddSingleton<IDistributedCache, MemoryDistributedCache>();
            services.RemoveAll<IMemoryCache>();
            services.AddSingleton<IMemoryCache, MemoryCache>(sp =>
                new MemoryCache(Options.Create(new MemoryCacheOptions())));

            // ── Fake auth handler — emits `oid` from X-Test-Oid header ────
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = Phase2FakeAuthHandler.SchemeName;
                options.DefaultChallengeScheme = Phase2FakeAuthHandler.SchemeName;
            })
            .AddScheme<AuthenticationSchemeOptions, Phase2FakeAuthHandler>(
                Phase2FakeAuthHandler.SchemeName, _ => { });

            services.PostConfigure<AuthenticationOptions>(options =>
            {
                options.DefaultAuthenticateScheme = Phase2FakeAuthHandler.SchemeName;
                options.DefaultChallengeScheme = Phase2FakeAuthHandler.SchemeName;
            });

            // ── Strip hosted services — same rationale as sibling fixtures.
            // The Phase 2 host (MembershipJunctionUpdaterHost) drives the
            // Service Bus consumer in production; in this fixture the
            // CapturingMembershipEventPublisher forwards directly to the
            // junction updater, so the host loop is unnecessary AND would
            // try to dial real Service Bus. Same for the recon scheduler.
            services.RemoveAll<IHostedService>();

            // ── Swap IMembershipEventPublisher for the capturing-and-
            // forwarding double. CRITICAL: the capturing publisher needs a
            // reference to the IMembershipJunctionUpdater that will end up
            // resolved in the request scope. We register the publisher as
            // a Singleton (matching production lifetime) and inject the
            // service provider so it can resolve a scoped junction updater
            // on demand per published event (mirroring how the production
            // Service Bus host resolves the handler per message via
            // IServiceScopeFactory).
            services.RemoveAll<IMembershipEventPublisher>();
            services.RemoveAll<NullMembershipEventPublisher>();
            services.AddSingleton(CapturingPublisher);
            services.AddSingleton<IMembershipEventPublisher>(sp =>
            {
                CapturingPublisher.AttachScopeFactory(sp.GetRequiredService<IServiceScopeFactory>());
                return CapturingPublisher;
            });

            // ── Swap IMembershipCacheInvalidator for the spy. Replaces
            // both the real impl AND the Null peer (ADR-032) so the test
            // sees the production-ish invocation pattern from the live
            // MembershipJunctionUpdater.
            services.RemoveAll<IMembershipCacheInvalidator>();
            services.RemoveAll<MembershipCacheInvalidator>();
            services.RemoveAll<NullMembershipCacheInvalidator>();
            services.AddSingleton(SpyInvalidator);
            services.AddSingleton<IMembershipCacheInvalidator>(SpyInvalidator);

            // ── Swap IDataverseService for the Moq Loose mock. IGenericEntityService
            // is registered upstream as `sp => sp.GetRequiredService<IDataverseService>()`
            // (see GraphModule.cs line 70) so replacing IDataverseService
            // alone is sufficient — the IGenericEntityService factory
            // resolves to our mock automatically.
            services.RemoveAll<IDataverseService>();
            services.AddSingleton(DataverseMock.Object);

            // ── Swap IOfficeService for the Moq mock so the QuickCreate
            // matter endpoint returns 201 Created without a real Dataverse
            // roundtrip. The MembershipChangedEvent publish call hangs off
            // the endpoint, NOT the service, so this mock does not interfere
            // with AC-1P2.5 assertions.
            services.RemoveAll<IOfficeService>();
            services.AddSingleton(OfficeMock.Object);

            // ── Swap IMembershipResolverService for a tiny in-memory
            // impl so AC-1P2.6 (endpoint contract unchanged) is provable
            // without standing up the full resolver pipeline (it needs
            // metadata discovery, identity normalization — all out of
            // scope for an E2E test of the Phase 2 swap-in invariant).
            services.RemoveAll<IMembershipResolverService>();
            services.AddSingleton<IMembershipResolverService>(new StubMembershipResolverService(DataverseState));
        });
    }

    /// <summary>HttpClient with authenticated identity carrying the given AAD oid.</summary>
    public HttpClient CreateAuthenticatedClient(Guid aadOid)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });
        client.DefaultRequestHeaders.Add("X-Test-Oid", aadOid.ToString());
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "test-token");
        return client;
    }

    /// <summary>Reset all in-memory state between tests (the fixture is shared).</summary>
    public void ResetState()
    {
        CapturingPublisher.Reset();
        SpyInvalidator.Reset();
        DataverseState.Reset();
        NextQuickCreateMatterId = Guid.NewGuid();
        DataverseMock.Invocations.Clear();
        OfficeMock.Invocations.Clear();
    }

    /// <summary>
    /// Wires up the DataverseMock so calls go through the in-memory state
    /// store. Mirrors the production code paths the Phase 2 flow exercises:
    /// RetrieveByAlternateKeyAsync (handler probe), CreateAsync/UpdateAsync/
    /// DeleteAsync (junction writes), RetrieveMultipleAsync (recon scans +
    /// membership-endpoint systemuser lookup).
    /// </summary>
    private void WireDataverseMock()
    {
        DataverseMock
            .Setup(d => d.TestConnectionAsync())
            .ReturnsAsync(true);

        // Alternate-key probe — used by MembershipJunctionUpdater per event.
        // Hit → returns Entity with the row id; miss → throws
        // InvalidOperationException with "not found" in the message (matches
        // DataverseServiceClientImpl's behavior the handler relies on).
        DataverseMock
            .Setup(d => d.RetrieveByAlternateKeyAsync(
                It.Is<string>(s => s == "sprk_userentityassociation"),
                It.IsAny<KeyAttributeCollection>(),
                It.IsAny<string[]>(),
                It.IsAny<CancellationToken>()))
            .Returns<string, KeyAttributeCollection, string[], CancellationToken>((entityLogicalName, keyAttributes, _, _) =>
            {
                var key = JunctionNaturalKey.FromKeyAttributes(keyAttributes);
                if (DataverseState.TryGetJunction(key, out var row))
                {
                    return Task.FromResult(new Entity(entityLogicalName, row!.Id));
                }
                throw new InvalidOperationException(
                    $"Entity {entityLogicalName} not found with provided alternate key values");
            });

        // CreateAsync — used by MembershipJunctionUpdater on Added/Updated
        // when the row is missing.
        DataverseMock
            .Setup(d => d.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Returns<Entity, CancellationToken>((entity, _) =>
            {
                if (entity.LogicalName != "sprk_userentityassociation")
                {
                    throw new InvalidOperationException(
                        $"Phase2EndToEndFixture: unexpected CreateAsync for entity={entity.LogicalName}");
                }
                var newId = DataverseState.AddJunctionFromEntity(entity);
                return Task.FromResult(newId);
            });

        // UpdateAsync — used by MembershipJunctionUpdater on Added/Updated
        // when the row exists.
        DataverseMock
            .Setup(d => d.UpdateAsync(
                It.Is<string>(s => s == "sprk_userentityassociation"),
                It.IsAny<Guid>(),
                It.IsAny<Dictionary<string, object>>(),
                It.IsAny<CancellationToken>()))
            .Returns<string, Guid, Dictionary<string, object>, CancellationToken>((_, id, fields, _) =>
            {
                DataverseState.UpdateJunction(id, fields);
                return Task.CompletedTask;
            });

        // DeleteAsync — used by MembershipJunctionUpdater on Removed.
        DataverseMock
            .Setup(d => d.DeleteAsync(
                It.Is<string>(s => s == "sprk_userentityassociation"),
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .Returns<string, Guid, CancellationToken>((_, id, _) =>
            {
                DataverseState.RemoveJunction(id);
                return Task.CompletedTask;
            });

        // RetrieveMultipleAsync(QueryExpression) — used by:
        //   (a) MembershipEndpoints for the AAD oid → systemuserid lookup
        //   (b) MembershipReconciliationJob.ScanParentsAndDispatchAsync
        //   (c) MembershipReconciliationJob.ScanOrphansAndDispatchAsync
        DataverseMock
            .Setup(d => d.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .Returns<QueryExpression, CancellationToken>((query, _) =>
                Task.FromResult(DataverseState.ExecuteQuery(query)));

        // FetchExpression variant — not used by Phase 2 flow but might
        // be hit by incidental code paths in the BFF bootstrap.
        DataverseMock
            .Setup(d => d.RetrieveMultipleAsync(It.IsAny<FetchExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection());
    }

    /// <summary>
    /// Wires up the OfficeMock so QuickCreateAsync(Matter, ...) returns a
    /// deterministic 201-Created response carrying
    /// <see cref="NextQuickCreateMatterId"/>. Other QuickCreate types
    /// out of scope for Phase 2 (only Matter is wired to the publisher
    /// per event-source-inventory §3A).
    /// </summary>
    private void WireOfficeMock()
    {
        OfficeMock
            .Setup(o => o.QuickCreateAsync(
                It.Is<QuickCreateEntityType>(t => t == QuickCreateEntityType.Matter),
                It.IsAny<QuickCreateRequest>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns<QuickCreateEntityType, QuickCreateRequest, string, CancellationToken>((entityType, request, _, _) =>
                Task.FromResult<QuickCreateResponse?>(new QuickCreateResponse
                {
                    Id = NextQuickCreateMatterId,
                    EntityType = entityType,
                    LogicalName = QuickCreateFieldRequirements.GetLogicalName(entityType),
                    Name = request.Name ?? "Phase2 E2E Matter",
                    Url = $"https://test.crm.dynamics.com/main.aspx?etn={QuickCreateFieldRequirements.GetLogicalName(entityType)}&id={NextQuickCreateMatterId}",
                }));
    }
}

// ─────────────────────────────────────────────────────────────────────────
// In-memory state — shared between IDataverseService mock setup and
// StubMembershipResolverService. ConcurrentDictionary-backed so the
// fixture can be reused across tests (xunit shared-fixture invariant).
// ─────────────────────────────────────────────────────────────────────────

/// <summary>
/// Thread-safe in-memory junction-table + systemuser + parent-entity state.
/// Backs both the Moq IDataverseService mock setup and the
/// StubMembershipResolverService.
/// </summary>
public sealed class InMemoryDataverseState
{
    private readonly ConcurrentDictionary<JunctionNaturalKey, JunctionRow> _junction = new();
    private readonly ConcurrentDictionary<Guid, JunctionRow> _junctionById = new();
    private readonly ConcurrentDictionary<Guid, Guid> _aadOidToSystemUser = new();
    private readonly ConcurrentDictionary<(string EntityType, Guid Id), Entity> _parents = new();

    /// <summary>Public read-only snapshot of the in-memory junction rows.</summary>
    public IReadOnlyCollection<JunctionRow> Junction => _junctionById.Values.ToArray();

    /// <summary>Seed an AAD oid → systemuserid mapping for the membership endpoint.</summary>
    public void SeedSystemUser(Guid aadOid, Guid systemUserId)
        => _aadOidToSystemUser[aadOid] = systemUserId;

    /// <summary>
    /// Seed a parent entity row (e.g., a sprk_matter with an ownerid Lookup)
    /// for the recon job to scan. Attributes are passed through verbatim.
    /// </summary>
    public void SeedParentEntity(string entityType, Guid id, params (string Attr, object? Value)[] attrs)
    {
        var entity = new Entity(entityType, id);
        foreach (var (attr, value) in attrs)
        {
            if (value is not null)
            {
                entity[attr] = value;
            }
        }
        _parents[(entityType, id)] = entity;
    }

    /// <summary>
    /// Seed a junction row directly — used by the orphan-detection test
    /// (AC-1P2.3) to set up a row whose source-of-truth parent does NOT
    /// have a corresponding Lookup populated.
    /// </summary>
    public Guid SeedJunctionRow(
        Guid personId,
        PersonIdentityType personIdType,
        string entityLogicalName,
        Guid entityRecordId,
        string sourceField,
        string role)
    {
        var key = new JunctionNaturalKey(personId, personIdType, entityLogicalName, entityRecordId, sourceField);
        var id = Guid.NewGuid();
        var row = new JunctionRow(
            Id: id,
            PersonId: personId,
            PersonIdType: personIdType,
            EntityLogicalName: entityLogicalName,
            EntityRecordId: entityRecordId,
            SourceField: sourceField,
            Role: role,
            LastSyncedOnUtc: DateTime.UtcNow);
        _junction[key] = row;
        _junctionById[id] = row;
        return id;
    }

    /// <summary>Reset between tests.</summary>
    public void Reset()
    {
        _junction.Clear();
        _junctionById.Clear();
        _aadOidToSystemUser.Clear();
        _parents.Clear();
    }

    // ─── Read paths used by the Moq mock setups ─────────────────────────

    /// <summary>Lookup a junction row by its 5-tuple natural key.</summary>
    public bool TryGetJunction(JunctionNaturalKey key, out JunctionRow? row)
    {
        if (_junction.TryGetValue(key, out var found))
        {
            row = found;
            return true;
        }
        row = null;
        return false;
    }

    /// <summary>Add a junction row from a freshly-constructed Entity (CreateAsync path).</summary>
    public Guid AddJunctionFromEntity(Entity entity)
    {
        var personIdString = (string)entity["sprk_personid"];
        var personIdTypeOsv = (OptionSetValue)entity["sprk_personidtype"];
        var entityLogicalName = (string)entity["sprk_entitylogicalname"];
        var entityRecordIdString = (string)entity["sprk_entityrecordid"];
        var sourceField = (string)entity["sprk_sourcefield"];
        var role = (string)entity["sprk_role"];
        var lastSyncedOn = (DateTime)entity["sprk_lastsyncedon"];

        var key = new JunctionNaturalKey(
            Guid.Parse(personIdString),
            (PersonIdentityType)personIdTypeOsv.Value,
            entityLogicalName,
            Guid.Parse(entityRecordIdString),
            sourceField);

        var newId = Guid.NewGuid();
        var row = new JunctionRow(
            Id: newId,
            PersonId: key.PersonId,
            PersonIdType: key.PersonIdType,
            EntityLogicalName: key.EntityLogicalName,
            EntityRecordId: key.EntityRecordId,
            SourceField: key.SourceField,
            Role: role,
            LastSyncedOnUtc: lastSyncedOn);
        _junction[key] = row;
        _junctionById[newId] = row;
        return newId;
    }

    /// <summary>Update an existing junction row's role + last-synced-on (UpdateAsync path).</summary>
    public void UpdateJunction(Guid id, Dictionary<string, object> fields)
    {
        if (!_junctionById.TryGetValue(id, out var existing))
        {
            return;
        }
        var newRole = fields.TryGetValue("sprk_role", out var r) && r is string rs ? rs : existing.Role;
        var newLastSynced = fields.TryGetValue("sprk_lastsyncedon", out var ls) && ls is DateTime lsd
            ? lsd
            : existing.LastSyncedOnUtc;

        var key = new JunctionNaturalKey(
            existing.PersonId, existing.PersonIdType, existing.EntityLogicalName,
            existing.EntityRecordId, existing.SourceField);
        var updated = existing with { Role = newRole, LastSyncedOnUtc = newLastSynced };
        _junction[key] = updated;
        _junctionById[id] = updated;
    }

    /// <summary>Remove a junction row by row id (DeleteAsync path).</summary>
    public void RemoveJunction(Guid id)
    {
        if (_junctionById.TryRemove(id, out var removed))
        {
            var key = new JunctionNaturalKey(
                removed.PersonId, removed.PersonIdType, removed.EntityLogicalName,
                removed.EntityRecordId, removed.SourceField);
            _junction.TryRemove(key, out _);
        }
    }

    /// <summary>
    /// Execute a QueryExpression against the in-memory state. Handles three
    /// shapes the Phase 2 flow exercises: systemuser lookup, parent-entity
    /// scan (recon step A), junction scan (recon step B).
    /// </summary>
    public EntityCollection ExecuteQuery(QueryExpression query)
    {
        ArgumentNullException.ThrowIfNull(query);

        // ── systemuser lookup (Phase 1A endpoint identity-resolution) ───
        if (query.EntityName == "systemuser")
        {
            var oidCondition = query.Criteria?.Conditions?.FirstOrDefault(
                c => c.AttributeName == "azureactivedirectoryobjectid"
                  && c.Values.Count == 1);
            if (oidCondition is not null && oidCondition.Values[0] is Guid aadOid
                && _aadOidToSystemUser.TryGetValue(aadOid, out var systemUserId))
            {
                var collection = new EntityCollection();
                var entity = new Entity("systemuser") { Id = systemUserId };
                entity["systemuserid"] = systemUserId;
                collection.Entities.Add(entity);
                return collection;
            }
            return new EntityCollection();
        }

        // ── Junction scan (recon step B — orphan detection) ────────────
        if (query.EntityName == "sprk_userentityassociation")
        {
            var collection = new EntityCollection();
            var entityLogicalNameFilter = query.Criteria?.Conditions?.FirstOrDefault(
                c => c.AttributeName == "sprk_entitylogicalname");
            var sourceFieldFilter = query.Criteria?.Conditions?.FirstOrDefault(
                c => c.AttributeName == "sprk_sourcefield");

            foreach (var row in _junctionById.Values)
            {
                if (entityLogicalNameFilter is not null
                    && entityLogicalNameFilter.Values.Count > 0
                    && !string.Equals((string)entityLogicalNameFilter.Values[0]!, row.EntityLogicalName, StringComparison.Ordinal))
                {
                    continue;
                }
                if (sourceFieldFilter is not null
                    && sourceFieldFilter.Values.Count > 0
                    && !sourceFieldFilter.Values.Select(v => v?.ToString()).Contains(row.SourceField))
                {
                    continue;
                }

                var entity = new Entity("sprk_userentityassociation", row.Id);
                entity["sprk_personid"] = row.PersonId.ToString("D");
                entity["sprk_personidtype"] = new OptionSetValue((int)row.PersonIdType);
                entity["sprk_entitylogicalname"] = row.EntityLogicalName;
                entity["sprk_entityrecordid"] = row.EntityRecordId.ToString("D");
                entity["sprk_sourcefield"] = row.SourceField;
                entity["sprk_role"] = row.Role;
                collection.Entities.Add(entity);
            }
            collection.MoreRecords = false;
            return collection;
        }

        // ── Parent-entity scan (recon step A — source-of-truth) ────────
        if (_parents.Keys.Any(k => k.EntityType == query.EntityName))
        {
            var collection = new EntityCollection();
            foreach (var (key, entity) in _parents)
            {
                if (key.EntityType != query.EntityName) continue;

                // Honor the "at least one non-null Lookup" OR filter the recon
                // job builds — emit only entities that have at least one of
                // the projected ColumnSet attributes populated.
                bool hasAny = query.ColumnSet?.Columns?.Any(col =>
                    entity.Contains(col) && entity[col] is not null
                    && (entity[col] is not Microsoft.Xrm.Sdk.EntityReference er || er.Id != Guid.Empty)
                    && (entity[col] is not Guid g || g != Guid.Empty)) ?? false;

                if (!hasAny) continue;
                collection.Entities.Add(entity);
            }
            collection.MoreRecords = false;
            return collection;
        }

        return new EntityCollection();
    }
}

// ─────────────────────────────────────────────────────────────────────────
// Test doubles
// ─────────────────────────────────────────────────────────────────────────

/// <summary>
/// In-memory <see cref="IMembershipEventPublisher"/> that captures every
/// published event AND forwards it synchronously to the registered
/// <see cref="IMembershipJunctionUpdater"/>. Simulates the topic +
/// subscription consumer in a single in-process hop so the E2E flow
/// (publisher → handler → junction → cache) is exercised end-to-end
/// without Azure dependencies.
/// </summary>
/// <remarks>
/// <para>
/// <b>Fire-and-forget contract preserved</b>: per FR-2P2.6 + Q2 the
/// production publisher catches all transport / handler exceptions. This
/// fake honors the same contract — handler exceptions are caught + recorded
/// in <see cref="HandlerFaults"/> but never propagated. Callers can opt
/// the fake into a "publish fails" mode via <see cref="ShouldFailNextPublish"/>
/// to exercise the AC-1P2.8 fire-and-forget invariant
/// (mutation succeeds even if publish fails).
/// </para>
/// <para>
/// Singleton lifetime — matches production
/// (<see cref="MembershipEventPublisher"/> is Singleton per ADR-010).
/// </para>
/// </remarks>
public sealed class CapturingMembershipEventPublisher : IMembershipEventPublisher
{
    private readonly ConcurrentQueue<MembershipChangedEvent> _captured = new();
    private readonly ConcurrentQueue<Exception> _handlerFaults = new();
    private IServiceScopeFactory? _scopeFactory;

    /// <summary>All published events in arrival order.</summary>
    public IReadOnlyCollection<MembershipChangedEvent> Captured => _captured.ToArray();

    /// <summary>Exceptions raised by the forwarded junction-updater handler.</summary>
    public IReadOnlyCollection<Exception> HandlerFaults => _handlerFaults.ToArray();

    /// <summary>
    /// When <c>true</c>, the NEXT call to <see cref="PublishAsync"/> drops
    /// the event silently (no capture, no forward, no throw) — simulates a
    /// production-side Service Bus transport failure swallowed by the real
    /// publisher's catch block. Auto-resets to <c>false</c> after firing
    /// once. Exercises the FR-2P2.6 + Q2 fire-and-forget invariant.
    /// </summary>
    public bool ShouldFailNextPublish { get; set; }

    /// <summary>Wires up the per-request scope factory. Called by the fixture.</summary>
    internal void AttachScopeFactory(IServiceScopeFactory scopeFactory)
        => _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));

    /// <inheritdoc />
    public async Task PublishAsync(MembershipChangedEvent evt, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(evt);

        if (ShouldFailNextPublish)
        {
            ShouldFailNextPublish = false;
            // Do NOT capture, do NOT forward, do NOT throw — simulates the
            // production publisher's swallow-and-log behavior under Service
            // Bus transport failure.
            return;
        }

        _captured.Enqueue(evt);

        if (_scopeFactory is null)
        {
            // Fixture mis-wired — surface loudly so tests fail fast rather
            // than silently swallow the forward.
            throw new InvalidOperationException(
                "CapturingMembershipEventPublisher: scope factory not attached. " +
                "Fixture must call AttachScopeFactory during DI registration.");
        }

        // Forward synchronously to the registered IMembershipJunctionUpdater
        // in a fresh scope. Mirrors the production
        // MembershipJunctionUpdaterHost.ProcessMessageAsync lifetime (Singleton
        // host + per-message scope).
        using var scope = _scopeFactory.CreateScope();
        var updater = scope.ServiceProvider.GetRequiredService<IMembershipJunctionUpdater>();
        try
        {
            await updater.HandleAsync(evt, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Record the fault but DO NOT propagate — production publisher
            // is fire-and-forget; consumers' fault behavior is captured by
            // the host's retry+dead-letter mechanism, NOT the publisher.
            _handlerFaults.Enqueue(ex);
        }
    }

    /// <summary>Reset between tests.</summary>
    public void Reset()
    {
        _captured.Clear();
        _handlerFaults.Clear();
        ShouldFailNextPublish = false;
    }
}

/// <summary>
/// Spy <see cref="IMembershipCacheInvalidator"/> that records every
/// <see cref="PublishInvalidationAsync"/> call. Singleton — matches
/// production lifetime (<see cref="MembershipCacheInvalidator"/> +
/// <see cref="NullMembershipCacheInvalidator"/> are both Singleton).
/// </summary>
public sealed class SpyMembershipCacheInvalidator : IMembershipCacheInvalidator
{
    private readonly ConcurrentQueue<CacheInvalidationInvocation> _invocations = new();

    /// <summary>All invalidation calls in arrival order.</summary>
    public IReadOnlyCollection<CacheInvalidationInvocation> Invocations
        => _invocations.ToArray();

    /// <inheritdoc />
    public Task PublishInvalidationAsync(
        Guid personId,
        string entityLogicalName,
        string? correlationId,
        CancellationToken ct)
    {
        _invocations.Enqueue(new CacheInvalidationInvocation(personId, entityLogicalName, correlationId));
        return Task.CompletedTask;
    }

    /// <summary>Reset between tests.</summary>
    public void Reset() => _invocations.Clear();
}

/// <summary>Record of a single PublishInvalidationAsync call for assertion.</summary>
public sealed record CacheInvalidationInvocation(
    Guid PersonId,
    string EntityLogicalName,
    string? CorrelationId);

/// <summary>
/// Composite natural-key for an in-memory junction row. Matches the
/// <c>sprk_uea_natural_key</c> 5-tuple used by <see cref="MembershipJunctionUpdater"/>.
/// </summary>
public readonly record struct JunctionNaturalKey(
    Guid PersonId,
    PersonIdentityType PersonIdType,
    string EntityLogicalName,
    Guid EntityRecordId,
    string SourceField)
{
    /// <summary>
    /// Reconstruct a key from the alternate-key collection
    /// <see cref="MembershipJunctionUpdater"/> builds (Text(36) GUIDs +
    /// OptionSetValue for identity type — matches the production handler).
    /// </summary>
    public static JunctionNaturalKey FromKeyAttributes(KeyAttributeCollection keyAttributes)
    {
        ArgumentNullException.ThrowIfNull(keyAttributes);
        var personId = Guid.Parse((string)keyAttributes["sprk_personid"]);
        var personIdType = (PersonIdentityType)((OptionSetValue)keyAttributes["sprk_personidtype"]).Value;
        var entityLogicalName = (string)keyAttributes["sprk_entitylogicalname"];
        var entityRecordId = Guid.Parse((string)keyAttributes["sprk_entityrecordid"]);
        var sourceField = (string)keyAttributes["sprk_sourcefield"];
        return new JunctionNaturalKey(personId, personIdType, entityLogicalName, entityRecordId, sourceField);
    }
}

/// <summary>Snapshot record of an in-memory junction row.</summary>
public sealed record JunctionRow(
    Guid Id,
    Guid PersonId,
    PersonIdentityType PersonIdType,
    string EntityLogicalName,
    Guid EntityRecordId,
    string SourceField,
    string Role,
    DateTime LastSyncedOnUtc);

/// <summary>
/// Tiny in-memory <see cref="IMembershipResolverService"/> for the
/// AC-1P2.6 (endpoint-contract-unchanged) test. Returns a deterministic
/// <see cref="MembershipResponse"/> shape whose serialized JSON matches
/// the Phase 1A contract exactly — proving the endpoint surface didn't
/// drift in Phase 2.
/// </summary>
public sealed class StubMembershipResolverService : IMembershipResolverService
{
    private readonly InMemoryDataverseState _state;

    public StubMembershipResolverService(InMemoryDataverseState state)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
    }

    public Task<Sprk.Bff.Api.Services.Ai.Membership.Models.MembershipResponse> ResolveAsync(
        Guid systemUserId,
        string entityType,
        MembershipResolveOptions? options,
        CancellationToken ct)
    {
        // Project from the in-memory junction so AC-1P2.6 reflects an
        // actual Phase-2-style junction read (the assertion is on JSON
        // shape, not on the resolution pipeline — that's covered by unit
        // tests on MembershipResolverService).
        var matchingIds = _state.Junction
            .Where(j => j.EntityLogicalName == entityType && j.PersonId == systemUserId)
            .Select(j => j.EntityRecordId)
            .Distinct()
            .OrderBy(g => g)
            .ToList();

        var byRole = _state.Junction
            .Where(j => j.EntityLogicalName == entityType && j.PersonId == systemUserId)
            .GroupBy(j => j.Role)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<Guid>)g.Select(j => j.EntityRecordId).Distinct().ToList());

        var response = new Sprk.Bff.Api.Services.Ai.Membership.Models.MembershipResponse(
            EntityType: entityType,
            PersonIdentity: new Sprk.Bff.Api.Services.Ai.Membership.Models.PersonIdentity(systemUserId),
            Ids: matchingIds,
            ByRole: byRole,
            Count: matchingIds.Count,
            CacheExpiresAt: DateTimeOffset.UtcNow.AddMinutes(5),
            ContinuationToken: null,
            RelatedByRole: null);

        return Task.FromResult(response);
    }
}

/// <summary>
/// Fake authentication handler scoped to the Phase 2 E2E integration suite.
/// Emits an authenticated identity carrying the AAD <c>oid</c> claim from
/// the <c>X-Test-Oid</c> header. No <c>Authorization</c> header → 401.
/// </summary>
internal sealed class Phase2FakeAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "Phase2E2EFakeAuth";

    public Phase2FakeAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.ContainsKey("Authorization"))
        {
            return Task.FromResult(AuthenticateResult.Fail("No Authorization header"));
        }

        var oid = Request.Headers["X-Test-Oid"].ToString();
        if (string.IsNullOrWhiteSpace(oid))
        {
            return Task.FromResult(AuthenticateResult.Fail("No X-Test-Oid header"));
        }

        var claims = new List<Claim>
        {
            new("oid", oid),
            new(System.Security.Claims.ClaimTypes.NameIdentifier, oid),
            new(System.Security.Claims.ClaimTypes.Name, $"Phase2 E2E Test User {oid}"),
        };

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
