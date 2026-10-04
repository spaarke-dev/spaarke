using Azure.Core;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Spaarke.Scheduling;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services.ExternalAccess;
using Sprk.Bff.Api.Services.Registration;

namespace Sprk.Bff.Api.Infrastructure.DI;

/// <summary>
/// DI registration for external access (Secure Project Workspace) services.
/// Registers participation data and project data services used by the external SPA.
///
/// ADR-010: Concrete type registrations — no unnecessary interfaces.
/// ADR-009: ExternalParticipationService uses Redis via ITenantCache (60s TTL).
/// </summary>
public static class ExternalAccessModule
{
    /// <summary>
    /// Static allow-list of the outside-counsel widget <c>sprk_gridconfiguration</c> record ids
    /// authored by task 016 (2026-08-06). See the "grid-configuration" module registration below.
    /// </summary>
    /// <summary>
    /// Shared empty-set singleton for always-fail-closed predicates (e.g. the "matters" module,
    /// D-016-1) — avoids allocating a new empty <see cref="HashSet{T}"/> on every predicate call
    /// (code-review Step 4 perf nit).
    /// </summary>
    private static readonly IReadOnlySet<Guid> EmptyRecordIds = new HashSet<Guid>();

    private static readonly IReadOnlySet<Guid> OutsideCounselGridConfigurationIds = new HashSet<Guid>
    {
        Guid.Parse("61711823-1092-f111-b8dc-7ced8ddc4a05"), // Projects
        Guid.Parse("3af4102c-1092-f111-b8dc-7ced8ddc4a05"), // Documents
        Guid.Parse("3ff4102c-1092-f111-b8dc-7ced8ddc4a05"), // Invoices
        Guid.Parse("42f4102c-1092-f111-b8dc-7ced8ddc4a05"), // Work Assignments
        Guid.Parse("583a2a33-1092-f111-b8dc-7ced8ddc4a05"), // Matters
        Guid.Parse("403e5d37-cb94-f111-b8db-00224835447a"), // Service Requests (internal-only, task 028)
    };

    /// <summary>
    /// Adds external access services: participation data loading and project data queries.
    /// </summary>
    public static IServiceCollection AddExternalAccess(this IServiceCollection services)
    {
        // Clock for grant expiry (spec FR-33, task 097): /grant and /invite-and-grant reject a past expiry and
        // default an absent one from "today". TryAdd, matching the idempotent convention in DocumentsModule /
        // MembershipModule / CommunicationModule — whichever module loads first wins, the rest no-op.
        // Registered HERE so the grant routes do not depend on an unrelated module having been added.
        services.TryAddSingleton(TimeProvider.System);

        // Participation service — queries sprk_externalrecordaccess with Redis caching (60s TTL): the
        // grant-DATA reader. (Contact resolution moved to ContactIdentityBinder below — task 141.)
        services.AddHttpClient<ExternalParticipationService>((sp, client) =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var dataverseUrl = config["Dataverse:ServiceUrl"];
            if (!string.IsNullOrEmpty(dataverseUrl))
            {
                client.BaseAddress = new Uri($"{dataverseUrl.TrimEnd('/')}/api/data/v9.2/");
                client.DefaultRequestHeaders.Add("OData-MaxVersion", "4.0");
                client.DefaultRequestHeaders.Add("OData-Version", "4.0");
            }
            client.Timeout = TimeSpan.FromSeconds(15);
        });

        // Data service — queries project data (projects, documents, events, contacts, organizations)
        // for external SPA users using managed identity app-only access.
        services.AddHttpClient<ExternalDataService>((sp, client) =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var dataverseUrl = config["Dataverse:ServiceUrl"];
            if (!string.IsNullOrEmpty(dataverseUrl))
            {
                client.BaseAddress = new Uri($"{dataverseUrl.TrimEnd('/')}/api/data/v9.2/");
                client.DefaultRequestHeaders.Add("OData-MaxVersion", "4.0");
                client.DefaultRequestHeaders.Add("OData-Version", "4.0");
            }
            client.Timeout = TimeSpan.FromSeconds(15);
        });

        // Module entitlement resolver (task 072, owner Option B) — resolves the Tier-1 module-code set
        // the external-spa widget registry gates tabs on (/api/v1/external/me/entitlements): workforce
        // from sprk_approlemodulemap (App-Role → module), CIAM blanket outside-counsel set. Typed
        // HttpClient (app-only Dataverse read, broker-only NFR-02) with a 60s Redis map cache (ADR-009 —
        // caches the small GLOBAL config map as DATA, never an authorization decision). Concrete
        // registration (ADR-010) — single implementation, mirroring ExternalParticipationService.
        services.AddHttpClient<ModuleEntitlementResolver>((sp, client) =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var dataverseUrl = config["Dataverse:ServiceUrl"];
            if (!string.IsNullOrEmpty(dataverseUrl))
            {
                client.BaseAddress = new Uri($"{dataverseUrl.TrimEnd('/')}/api/data/v9.2/");
                client.DefaultRequestHeaders.Add("OData-MaxVersion", "4.0");
                client.DefaultRequestHeaders.Add("OData-Version", "4.0");
            }
            client.Timeout = TimeSpan.FromSeconds(15);
        });

        // SPE container membership — manages Graph API permissions for external users.
        services.AddScoped<SpeContainerMembershipService>();

        // Caller-scoped record-rights probe (unified-access-control-r2 task 008, FR-07, finding A-6) —
        // the OBO evaluation behind DelegationRuleFilter's Write-on-target rule.
        //
        // UNCONDITIONAL, and it must stay that way: every route on /api/v1/external-access carries the
        // delegation filter, and those routes map unconditionally. A conditional registration here would
        // be the asymmetric-registration anti-pattern (CLAUDE.md §10 F.1 / ADR-032) with the worst
        // possible blast radius — the six mutation endpoints would throw at request time instead of
        // authorizing. Note this is fail-CLOSED rather than fail-open when misconfigured: the probe
        // denies (returns AccessRights.None) when OBO configuration is absent, it does not fall back to
        // app-only. A Null-Object peer is therefore neither needed nor desirable.
        //
        // Typed HttpClient (transient) matching the ExternalParticipationService / ModuleEntitlementResolver
        // precedent above; the MSAL confidential client is static-cached inside the type so the transient
        // lifetime does not discard MSAL's per-user OBO token cache. No BaseAddress is configured — the
        // probe issues absolute URLs so its two calls (WhoAmI, RetrievePrincipalAccess) are readable at
        // the call site.
        services.AddHttpClient<CallerRecordAccessProbe>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(15);
        });

        // ── Identity binding (unified-access-control-r2 task 141, defect C7) ──────────────────────────
        // Placement: in the BFF (bff-extensions.md §A) — it runs inside the authorization path of every
        // Teams/SPA request (B4, latency-coupled), over BFF-owned identity data, under the BFF identity.
        //
        // The customer-workforce-tenant list (owner decision I1 = (b)): EMPTY = DENY every email bind and
        // creation; never defaults to AzureAd:TenantId. Malformed entries fail startup (ADR-010 ValidateOnStart).
        services.AddOptions<WorkforceIdentityOptions>()
            .BindConfiguration(WorkforceIdentityOptions.SectionName)
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<WorkforceIdentityOptions>, WorkforceIdentityOptionsValidator>();

        // The one binding store for THIS environment (Dataverse:ServiceUrl), app-only under the BFF's managed
        // identity. Named client from IHttpClientFactory (pooled; safe in a singleton). The interface is the
        // testing seam (ADR-010): HTTP doubles are banned (ADR-038 B1), so tests run the binder over an
        // in-memory store.
        services.AddHttpClient(DataverseContactIdentityStore.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(15);
        });
        services.AddSingleton<IContactIdentityStore>(sp => DataverseContactIdentityStore.ForDefaultEnvironment(
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<TokenCredential>(),
            sp.GetRequiredService<IConfiguration>(),
            sp.GetRequiredService<ILogger<DataverseContactIdentityStore>>()));

        // The one binding writer, shared by the workforce resolver, the CIAM strategy, the invite endpoints and
        // the reconciliation job. Concrete (ADR-010). Its factory builds a binder over ANOTHER environment for
        // RegistrationDataverseService (a new systemuser's link goes to the environment it was created in).
        services.AddSingleton<ContactIdentityBinder>();
        services.AddSingleton<ContactIdentityBinderFactory>();

        // Workforce-token → principal resolver (ADR-028 Amendment A2 · teams-app-r1 FR-04, task 020).
        // Composes the existing AAD-oid→systemuser conversion (MembershipEndpoints.ResolveSystemUserIdAsync)
        // with the oid BINDING (ContactIdentityBinder, task 141) into one principal (systemuser / contact-only
        // / deny). Singleton is safe: all deps (IIdentityNormalizationService, IDataverseService, ITenantCache,
        // ContactIdentityBinder) are singletons. The interface is the testing seam (ADR-010). No Graph SDK /
        // AI-internal types are injected (broker-only, NFR-02).
        services.AddSingleton<IWorkforcePrincipalResolver, WorkforcePrincipalResolver>();

        // Standing-grant flag reader (teams-app-r1 task 022 — the task-051 composition seam). Reads the
        // FLS-secured contact.sprk_standinggrant boolean app-only via the already-registered
        // IDataverseService; gates a contact principal's standing-grant runtime membership term.
        // Interface is the ADR-010 testing seam. Singleton is safe (IDataverseService is a singleton).
        services.AddSingleton<ISubjectStandingGrantReader, SubjectStandingGrantReader>();

        // Deny-list reader (unified-access-control-r2 task 038, FR-23) — the fail-closed reader
        // over sprk_noaccessentry (the ethical-wall / per-child-revocation VETO store; store +
        // reader only, task 039 wires the veto into AccessibleRecordSetService.ApplyVetoPipeline).
        // Typed HttpClient with its own app-only token management, matching the established
        // QUERY-shaped-reader style of this module (ExternalParticipationService,
        // ModuleEntitlementResolver) rather than SubjectStandingGrantReader's single
        // retrieve-by-id via the shared IDataverseService broker (no batched/filtered query
        // capability). Interface is the ADR-010 testing seam for task 039's future consumer; the
        // concrete type additionally exposes an internal-virtual query seam
        // (InternalsVisibleTo("Sprk.Bff.Api.Tests"), matching ExternalParticipationService's own
        // convention) for THIS task's unit tests to exercise the real chunking/matching/
        // fail-closed orchestration without mocking HttpMessageHandler (banned, testing.md B1).
        // Transient (AddHttpClient default) — safe to inject into the Scoped
        // AccessibleRecordSetService; no shared mutable state crosses requests.
        services.AddHttpClient<NoAccessListReader>((sp, client) =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var dataverseUrl = config["Dataverse:ServiceUrl"];
            if (!string.IsNullOrEmpty(dataverseUrl))
            {
                client.BaseAddress = new Uri($"{dataverseUrl.TrimEnd('/')}/api/data/v9.2/");
                client.DefaultRequestHeaders.Add("OData-MaxVersion", "4.0");
                client.DefaultRequestHeaders.Add("OData-Version", "4.0");
            }
            client.Timeout = TimeSpan.FromSeconds(15);
        });
        services.AddTransient<INoAccessListReader>(sp => sp.GetRequiredService<NoAccessListReader>());

        // unified-access-control-r2 task 143 (owner Q4) — the No Access list for INTERNAL users on secure records.
        // Three components, all UNCONDITIONAL (ADR-032 — every dependency is unconditional, so no Null-Object):
        //   • SecureShareNoAccessGuard — the ONE write-time "is this systemuser walled off this secure record?" check,
        //     asked by /share-user and secure provisioning before any share write. Scoped: it composes the scoped
        //     typed-HttpClient readers above.
        //   • NoAccessEnforcementStore — the app-only Dataverse reads the enforcer needs that no reader answers (the
        //     entry, systemusers by oid, team membership, RetrievePrincipalAccess for ANOTHER principal). Its own
        //     typed HttpClient, the NoAccessListReader shape; the internal-virtual reads are the test seam.
        //   • NoAccessShareEnforcer — removes the direct shares an entry walls off (never a team or role share; never
        //     the last person; only where the entry's author holds Write). Used by the enforce route and the job.
        services.AddScoped<SecureShareNoAccessGuard>();
        services.AddHttpClient<NoAccessEnforcementStore>((sp, client) =>
        {
            client.Timeout = TimeSpan.FromSeconds(15);
        });
        services.AddScoped<NoAccessShareEnforcer>();

        // Principal-agnostic caller resolution (teams-app-r1 task 025 · R2 FR-22 · Option A). The
        // reusable abstraction that lets the /api/v1/external collaboration endpoints serve BOTH the
        // CIAM external contact AND the workforce (Teams-host) user through ONE endpoint set. The two
        // strategies are registered as ICallerPrincipalStrategy; the resolver selects by token issuer.
        // A THIRD plane plugs in by adding one more ICallerPrincipalStrategy registration (R2 lifts
        // this into its module framework unchanged). Scoped: CiamContactPrincipalStrategy depends on
        // the scoped typed-HttpClient ExternalParticipationService; WorkforcePrincipalStrategy depends
        // on the scoped IAccessibleRecordSetService — so the strategies + resolver are scoped too.
        // Broker-only (NFR-02): app-only reads, no OBO, no Graph SDK / AI-internal types.
        services.AddScoped<ICallerPrincipalStrategy, CiamContactPrincipalStrategy>();
        services.AddScoped<ICallerPrincipalStrategy, WorkforcePrincipalStrategy>();
        services.AddScoped<ICallerPrincipalResolver, CallerPrincipalResolver>();

        // FR-20 / task 035 — the impersonated root-set source. Asks Dataverse which root records a
        // systemuser can actually read (one impersonated id-only query per root type) instead of
        // pattern-matching its rules in C#, which gets it wrong in BOTH directions: a business-unit
        // column match over-grants past the user's role depth, and it misses records reachable only
        // through a POA share.
        //
        // ADR-010: ONE interface, and it is a genuine seam — task 036 swaps this source into the
        // evaluator behind a flag, so the swap point must be substitutable. It deliberately does NOT
        // introduce a second impersonated-query interface: IImpersonatedCommunicationQuery
        // (CommunicationModule, registered UNCONDITIONALLY per ADR-032) already wraps
        // RetrieveMultipleImpersonatedAsync with a fully generic (entitySet, odataQuery, callerId)
        // contract — communication-specific in NAME only. Declaring an identical second interface is
        // the duplication CLAUDE.md §11 exists to prevent.
        //
        // ⚠️ NOT consumed by AccessibleRecordSetService yet — that swap is task 036's obligation, and
        // it is gated on the NFR-04 negative canary (task 034). Registering it here is inert until then.
        // Scoped: it reads the caller's tenant claim off IHttpContextAccessor for the cache key.
        services.AddScoped<IImpersonatedRootSetSource, ImpersonatedRootSetSource>();

        // Module-host registration framework (spaarke-SPA-external-access-platform-r2 task 015 · FR-22 ·
        // ADR-028 A3). Generalizes the resolver seam into a per-module registry: each module registers a
        // Tier-2 record predicate (over the plane-agnostic CallerPrincipal) that the BffDataverseClient
        // read-data group applies. Registering a module is purely additive (call AddExternalModule) — no
        // route/filter/handler change. The registry is a singleton built from every registered
        // ExternalModuleDescriptor; it holds no per-request state (the predicate delegates run on the
        // request's CallerPrincipal), so it is read-only + thread-safe after startup. ADR-010: concrete
        // registration — the pluggable seam is the per-module descriptor delegates, not an interface.
        services.AddSingleton<ExternalModuleRegistry>(sp =>
        {
            var registry = new ExternalModuleRegistry();
            foreach (var descriptor in sp.GetServices<ExternalModuleDescriptor>())
            {
                registry.Register(descriptor);
            }
            return registry;
        });

        // FIRST module — the collaboration / Secure-Project surface delivered by teams-app-r1, now
        // registered OVER the framework (design.md §3 reuse-as-is + generalize). Its Tier-2 predicate IS
        // the project scope both plane strategies already composed onto CallerPrincipal.ProjectAccess
        // (CIAM → sprk_externalrecordaccess participations; workforce → accessible-record-set). Task 016
        // registers the remaining outside-counsel modules (matter/document/invoice/work-assignment) the
        // same way — AddExternalModule with one descriptor each, no framework change.
        //
        // COLUMN allow-lists (unified-access-control-r2 task 134, defect C6). Every descriptor declares the
        // exact columns an external caller may read; the read seam refuses anything else before execution
        // and strips it from the result afterwards (ExternalModuleDataEndpoints). Each list was DERIVED
        // FROM LIVE DATA on 2026-09-30, never written from memory — a guessed list either leaks or blanks a
        // grid. Derivation rule: (a) every attribute the module's sprk_gridconfiguration record references
        // (attribute / condition / order); (b) every attribute of a sibling saved view the grid's
        // ViewSelector offers AND that can render rows today, i.e. one that projects a scope-dimension
        // attribute (a view that projects none returns 0 rows after ScopeRows, so it shows nothing today
        // and contributes no column); (c) the scope-dimension attributes; (d) the /record default
        // projection (primary id + primary name, from EntityDefinitions — each descriptor DECLARES its
        // PrimaryNameAttribute and Register refuses a list missing either). No live grid or view references
        // a pointer column, an alias or an aggregate. Full table: projects/unified-access-control-r2/
        // notes/task-134-external-module-column-allow-list.md. ⚠️ Changing a grid configuration or a main
        // view to show a new column now REQUIRES adding the column here — otherwise that grid gets a 400.
        //
        // sprk_project: grid 61711823 + views "Active Projects" 195ab203, "My Projects" 0e36d0a4.
        // Primary name = sprk_projectnumber.
        services.AddExternalModule(new ExternalModuleDescriptor
        {
            Name = "collaboration",
            RecordEntity = "sprk_project",
            RecordIdAttribute = "sprk_projectid",
            AccessibleRecordIds = principal => principal.GetAccessibleProjectIds().ToHashSet(),
            PrimaryNameAttribute = "sprk_projectnumber",
            ReadableColumns = new HashSet<string>
            {
                "sprk_projectid", "sprk_projectname", "sprk_projectnumber", "statuscode", "statecode",
                "modifiedon", "createdon", "ownerid", "sprk_practicearea", "sprk_projecttype_ref",
            },
        });

        // Task 028 (2026-08-10) — POLYMORPHIC Tier-2 scoping. Supersedes task 016's single-parent
        // (project-only) wiring. Access is held at ROOT records (Project / Matter / Work Assignment);
        // a child (Document / Invoice) rolls up to ANY accessible root, so each child module declares a
        // LIST of OR'd scope dimensions — one per typed parent lookup (all verified live: sprk_document
        // has sprk_project/sprk_matter/sprk_workassignment; sprk_invoice has sprk_matter/sprk_project).
        // The accessible-root id sets (P/M/W) are already composed onto CallerPrincipal by both plane
        // strategies (CIAM → grants-only; workforce → membership ∪ grants), so every predicate stays a
        // pure synchronous read of the principal (no extra Dataverse round-trip). Each widget's inline
        // FetchXML projects the parent-lookup attributes (hidden columns) so ScopeRows can evaluate
        // them; the server-side injector emits <filter type='or'> across the non-empty dimensions.

        // Documents — visible when attached to an accessible project OR matter OR work assignment.
        services.AddExternalModule(new ExternalModuleDescriptor
        {
            Name = "documents",
            RecordEntity = "sprk_document",
            ScopeDimensions = new[]
            {
                new ScopeDimension { Attribute = "sprk_project", AccessibleIds = p => p.GetAccessibleProjectIds().ToHashSet() },
                new ScopeDimension { Attribute = "sprk_matter", AccessibleIds = p => p.GetAccessibleMatterIds() },
                new ScopeDimension { Attribute = "sprk_workassignment", AccessibleIds = p => p.GetAccessibleWorkAssignmentIds() },
            },
            // Grid 3af4102c only: none of the four sprk_document main views projects a scope lookup, so
            // each renders 0 rows today and contributes no column (they would add AI-triage columns —
            // classification, invoice hints — that no external caller can currently see). Primary name =
            // sprk_documentname. No pointer column (sprk_graphdriveid / sprk_graphitemid / sprk_filepath …).
            PrimaryNameAttribute = "sprk_documentname",
            ReadableColumns = new HashSet<string>
            {
                "sprk_documentid", "sprk_documentname", "sprk_documenttype", "createdon",
                "sprk_project", "sprk_matter", "sprk_workassignment",
            },
        });

        // Invoices — visible when attached to an accessible matter OR project (invoices carry both
        // sprk_matter and sprk_project lookups; R1's project-only scope silently hid matter-parented
        // invoices — the concrete over-hide task 028 fixes).
        services.AddExternalModule(new ExternalModuleDescriptor
        {
            Name = "invoices",
            RecordEntity = "sprk_invoice",
            ScopeDimensions = new[]
            {
                new ScopeDimension { Attribute = "sprk_matter", AccessibleIds = p => p.GetAccessibleMatterIds() },
                new ScopeDimension { Attribute = "sprk_project", AccessibleIds = p => p.GetAccessibleProjectIds().ToHashSet() },
            },
            // Grid 3ff4102c + view "Invoice - Matter Context" b9f6d045 (the only sprk_invoice main view that
            // projects a scope lookup, sprk_matter). Primary name = sprk_name.
            PrimaryNameAttribute = "sprk_name",
            ReadableColumns = new HashSet<string>
            {
                "sprk_invoiceid", "sprk_name", "sprk_invoicenumber", "sprk_invoicedate", "sprk_invoicestatus",
                "sprk_totalamount", "sprk_project", "sprk_matter", "sprk_visibilitystate", "modifiedon",
                "statecode",
            },
        });

        // Work Assignments — a FIRST-CLASS ROOT (task 028): a standalone WA (no project/matter) can be
        // granted to outside counsel and carry its own documents. Scoped by the WA's OWN id ∈ the
        // caller's accessible work-assignment set (was: scoped by sprk_regardingproject, which hid any
        // WA not tied to an accessible project — including grant-only standalone WAs).
        services.AddExternalModule(new ExternalModuleDescriptor
        {
            Name = "work-assignments",
            RecordEntity = "sprk_workassignment",
            RecordIdAttribute = "sprk_workassignmentid",
            AccessibleRecordIds = p => p.GetAccessibleWorkAssignmentIds(),
            // Grid 42f4102c + views "Active Work Assignments" c8391ddf, "Inactive Work Assignments"
            // d73b2239, "My Work to Assign" b7cf5593. Primary name = sprk_name.
            PrimaryNameAttribute = "sprk_name",
            ReadableColumns = new HashSet<string>
            {
                "sprk_workassignmentid", "sprk_name", "sprk_workassignmentnumber", "sprk_priority",
                "sprk_responseduedate", "statuscode", "statecode", "sprk_regardingproject", "createdon",
                "ownerid", "sprk_assignedto",
            },
        });

        // Matters — a first-class ROOT (task 028; supersedes the D-016-1 always-empty stub). Scoped by
        // the matter's own id ∈ the caller's accessible matter set (CIAM → matter grants; workforce →
        // membership ∪ matter grants). No Contact→Organization resolution needed: matter access is an
        // explicit sprk_externalrecordaccess grant of recordtype=Matter, exactly like project access.
        services.AddExternalModule(new ExternalModuleDescriptor
        {
            Name = "matters",
            RecordEntity = "sprk_matter",
            RecordIdAttribute = "sprk_matterid",
            AccessibleRecordIds = p => p.GetAccessibleMatterIds(),
            // Grid 583a2a33 + views "Active Matters" 3ba2301f, "My Matters" 6c3c5d88, "All Matters"
            // 694cd4b7. Primary name = sprk_matternumber.
            PrimaryNameAttribute = "sprk_matternumber",
            ReadableColumns = new HashSet<string>
            {
                "sprk_matterid", "sprk_mattername", "sprk_matternumber", "statuscode", "statecode",
                "createdon", "sprk_mattertype", "sprk_practicearea",
            },
        });

        // Service Requests (task 028) — INTERNAL-ONLY. Shows the caller's OWN submitted requests
        // (sprk_requestedby == caller contact). Fail-closed for the CIAM partner plane: the accessible
        // set is empty for a non-workforce caller, so a partner sees 0 rows even if the tab were somehow
        // requested — server-side enforcement of "internal-only" that does not rely on the client hiding
        // the tab (the external-spa also entitlement-gates the tab off the partner surface).
        services.AddExternalModule(new ExternalModuleDescriptor
        {
            Name = "service-requests",
            RecordEntity = "sprk_servicerequest",
            RecordIdAttribute = "sprk_requestedby",
            AccessibleRecordIds = p =>
                p.Plane == CallerPrincipalPlane.Workforce && p.ContactId != Guid.Empty
                    ? new HashSet<Guid> { p.ContactId }
                    : EmptyRecordIds,
            // Grid 403e5d37 only: the one sprk_servicerequest main view ("Inactive Service Requests")
            // does not project the scope attribute sprk_requestedby, so it renders 0 rows today.
            // Primary name = sprk_name.
            PrimaryNameAttribute = "sprk_name",
            ReadableColumns = new HashSet<string>
            {
                "sprk_servicerequestid", "sprk_servicerequestnumber", "sprk_name", "statuscode", "createdon",
                "sprk_requestedby",
            },
        });

        // grid-configuration (D-016-2): every <DataGrid configId=…/> widget fetches its own
        // sprk_gridconfiguration record via BffDataverseClient.retrieveRecord BEFORE it can resolve
        // an entity/fetchXml at all (DataGrid.tsx fetchConfigRecord) — that read goes through this
        // SAME Tier-2-gated seam (GET /api/dataverse/record/{entity}/{id}), so sprk_gridconfiguration
        // must ALSO be a registered module or every widget's config load is denied and no grid ever
        // renders. A grid-configuration record carries only column/layout metadata (no tenant PII, no
        // Graph pointer) — safe to expose by a small static allow-list of the exact config record ids
        // task 016 authored (NOT "all gridconfiguration records"), fail-closed to any other id.
        services.AddExternalModule(new ExternalModuleDescriptor
        {
            Name = "grid-configuration",
            RecordEntity = "sprk_gridconfiguration",
            RecordIdAttribute = "sprk_gridconfigurationid",
            AccessibleRecordIds = _ => OutsideCounselGridConfigurationIds,
            // The only external read of this entity is the shared DataGrid's config load,
            // `retrieveRecord('sprk_gridconfiguration', configId, ['sprk_configjson'])`
            // (Spaarke.UI.Components DataGrid.tsx fetchConfigRecord) + the /record default projection.
            // No external grid lists grid configurations. Primary name = sprk_name.
            PrimaryNameAttribute = "sprk_name",
            ReadableColumns = new HashSet<string>
            {
                "sprk_gridconfigurationid", "sprk_name", "sprk_configjson",
            },
        });

        // Accessible-record-set composition + enforcement gate (teams-app-r1 task 022, spec FR-06 /
        // design §5). Composes accessible(principal) = systemuser→ADR-034 membership (auto) ∪
        // contact→sprk_externalrecordaccess grants ∪ contact→standing-grant runtime membership, and is
        // the single record∈set enforcement point (consumed by the ADR-008 endpoint filter + task 030's
        // authz-before-stream broker gate). Interface is the ADR-010 testing seam. No Graph SDK /
        // AI-internal types (broker-only, NFR-02). Scoped: ExternalParticipationService is a typed
        // HttpClient (scoped), so this composer is scoped too.
        services.AddScoped<IAccessibleRecordSetService, AccessibleRecordSetService>();

        // Cross-tenant CIAM Graph client (app-only) for admin-initiated external-user provisioning
        // (task 022; consumed by the provisioner in task 025). Singleton: caches a per-authority MSAL
        // confidential client (one Key Vault certificate fetch); stateless otherwise.
        // Depends on the shared SecretClient (registered in SpeAdminModule) to load the provisioner
        // certificate from Key Vault, and the resilient "GraphApiClient" HttpClient (GraphModule).
        // ADR-028 A1: app-only client-credentials against the CIAM authority — never OBO (broker-only).
        services.AddSingleton<CiamGraphClientFactory>();

        // Admin-initiated CIAM user provisioner (task 025). Creates CIAM local accounts via the
        // cross-tenant client above; reuses PasswordGenerator (RegistrationModule). Singleton per ADR-010.
        services.AddSingleton<CiamUserProvisioningService>();

        // FR-33 (d), task 100 — reminders 30/14/7/3/1 days before an external grant expires, to the granter
        // (else the record's owner, else its creator), never the grantee. A job on the in-process
        // Spaarke.Scheduling host, registered through AddScheduledJob (ADR-036 A1 rule 6). UNCONDITIONAL: its
        // dependencies (NotificationService, IIdempotencyService, IGenericEntityService, TimeProvider) are all
        // unconditional, so ADR-032 needs no Null-Object here. There is no durable pause: the admin disable applies
        // to the one instance that served it and a restart re-enables the job (ADR-036 A1 §2).
        services.AddScheduledJob<GrantExpiryReminderJob>(GrantExpiryReminderJob.DefaultCronSchedule);

        // Owner decision D-1 option B + D-2 part 3, task 117 — the reconciliation pass that makes a row's own
        // statecode / sprk_expiresdate the truth (stamp an undated grant, deactivate a grant whose organization
        // is inactive, deactivate a membership whose end date has passed). Same host, same registration seam as
        // the reminder job above (ADR-036 A1 rule 6); ADR-052 places it in the BFF.
        //
        // ⚠️ enabled: false IS THE SHIPPING STATE, not an oversight. Rules R2 and R3 REMOVE access that exists
        // today, and R1 turns a row that (since task 107) confers nothing into one that confers access for 90
        // more days. Enabling it is an owner action. It is belt AND braces: even a manual admin trigger of the
        // disabled job writes nothing, because writes are separately gated on
        // ExternalAccessReconciliationJob.WritesEnabledConfigKey, which defaults to report-only.
        //
        // UNCONDITIONAL registration (ADR-032): every dependency — IServiceScopeFactory, TimeProvider,
        // IConfiguration, IGenericEntityService, IIdempotencyService — is itself registered unconditionally, so
        // there is no feature flag around this line and no Null-Object is needed. The job's OWN disabled state
        // is carried by the scheduler's registration data, not by an `if` around the registration, which is
        // exactly what § F.1's asymmetric-registration anti-pattern asks for.
        services.AddScheduledJob<ExternalAccessReconciliationJob>(
            ExternalAccessReconciliationJob.DefaultCronSchedule, enabled: false);

        // Task 141 — the identity-link reconciliation (every licensed systemuser linked to its contact, or
        // flagged). Systemusers are created outside the product (Entra / PPAC sync), so this is the safety net
        // behind the inline link (WP-5). Same host and seam as the two jobs above (ADR-036 A1 rule 6; ADR-052
        // places it in the BFF). ENABLED but REPORT-ONLY: writes need IdentityLink:Reconciliation:WritesEnabled
        // = true — absent, empty or unparseable writes nothing. The switch is a rollout guard, not a deferral:
        // the dev live gate runs report-only, the report is reviewed, then writes are enabled.
        // It reconciles this BFF's own environment AND every environment it provisions users into (DATAVERSE_URL
        // and the active sprk_dataverseenvironment rows, through RegistrationDataverseService's per-environment
        // token path), so a registration link that did not land in a target is retried (task 141, third fix round).
        // UNCONDITIONAL (ADR-032): every dependency is registered unconditionally above; the registration services
        // (RegistrationModule) are resolved per run and only when DATAVERSE_URL is configured.
        services.AddScheduledJob<IdentityLinkReconciliationJob>(IdentityLinkReconciliationJob.DefaultCronSchedule);

        // unified-access-control-r2 task 144 (C10 part 1, #967; owner decision F2 = a) — the read-only Secure Record
        // isolation census: no role reaches the Secure Record BU by depth, the BU holds no users, its named owner team
        // resolves with no members and alone holds the owner role. Logs CRITICAL per finding; writes nothing. Provisioning
        // checks the same invariants only when something is provisioned; a Change-BU between provisioning calls cannot
        // be blocked from the BFF (no plugins, ADR-002), so this bounds how long it goes unseen. ADR-052 places it in
        // the BFF on the in-process scheduler (ADR-036 A1 rule 6). ENABLED: it has no side effect to gate.
        // UNCONDITIONAL registration (ADR-032): IServiceScopeFactory, IConfiguration and TimeProvider are all
        // unconditional (and IGenericEntityService, resolved per run from a scope, is too), so no Null-Object is needed.
        services.AddScheduledJob<SecureRecordIsolationCensusJob>(SecureRecordIsolationCensusJob.DefaultCronSchedule);

        // unified-access-control-r2 task 149 (C10 part 2, sharees; ships with task 146) — the ONE synchronizer that keeps
        // every child of a secure record shared with exactly its root's internal sharees (never wider; Share and Assign
        // never mirrored). Called by /share-user and /unshare-user (fan-out in the request), by secure provisioning, and by
        // the reconcile job below, and by NoAccessShareEnforcer after it removes a root share (task 143 merge, r3). Concrete
        // class (ADR-010: no second implementation, no interface). SCOPED since r3: it consults task 143's scoped
        // SecureShareNoAccessGuard before every child grant or widening; every consumer resolves it from a request or job
        // scope (the endpoints' handler parameters, the reconcile job's per-run scope, the scoped enforcer). Every
        // dependency — IGenericEntityService, the one POA seam IDataverseRecordShareService, the guard — is registered
        // unconditionally. Placement + §11 justification: notes/task-149-secure-child-sharee-access.md §6 and §13.
        services.AddScoped<Sprk.Bff.Api.Services.Access.SecureChildShareSynchronizer>();

        // Task 149 — the scheduled safety net and the mechanism for every writer that does not pass through the share
        // endpoints: children created or re-filed under a secure record, and model-driven-app Share/Unshare of a secure root
        // (no relationship cascades either, live metadata 2026-10-02). Every two minutes. ENABLED WITH WRITES: it IS the
        // mechanism (report-only would leave new children invisible to the root's sharees), and every write is bounded by
        // the root's own shares. ADR-052 places it in the BFF on the in-process scheduler (ADR-036 A1 rule 6).
        // UNCONDITIONAL registration (ADR-032): IServiceScopeFactory, TimeProvider and the synchronizer are unconditional.
        services.AddScheduledJob<Sprk.Bff.Api.Services.Access.SecureChildShareReconciliationJob>(
            Sprk.Bff.Api.Services.Access.SecureChildShareReconciliationJob.DefaultCronSchedule);

        // unified-access-control-r2 task 148 (C10 part 2, transitions + backfill) — the ONE engine that brings every EXISTING
        // child of a root into the state task 146's rule gives it (re-owned through IRecordOwnershipResolver, sharees mirrored
        // through the synchronizer above, the platform-cascade rows placed through AssignCascadeChildOwners). Called by
        // /provision-project, /unsecure-project and the sweep below. Concrete (ADR-010: one implementation); SCOPED because
        // the synchronizer it composes is. Every dependency — IGenericEntityService, IRecordOwnershipResolver, the
        // synchronizer, DataverseWebApiClient — is registered unconditionally. §10/§11: notes/task-148-secure-child-backfill.md.
        services.AddScoped<Sprk.Bff.Api.Services.Access.SecureChildReconciler>();

        // Task 148 — the sweep over every sprk_issecure = true root: the one-time backfill (scripts/Invoke-SecureChildBackfill.ps1)
        // and, once task 147 schedules it, the L4 safety net. ⚠️ enabled: false IS THE SHIPPING STATE and writes are
        // separately gated on SecureChild:Reconciliation:WritesEnabled (absent = report-only) — the
        // ExternalAccessReconciliationJob posture: it MOVES ownership of existing rows, so enabling it is an owner action.
        // ADR-052 places it in the BFF on the in-process scheduler (ADR-036 A1 rule 6). UNCONDITIONAL (ADR-032):
        // IServiceScopeFactory, IBackgroundJobStore, TimeProvider and IConfiguration are unconditional.
        services.AddScheduledJob<Sprk.Bff.Api.Services.Access.SecureChildReconciliationJob>(
            Sprk.Bff.Api.Services.Access.SecureChildReconciliationJob.DefaultCronSchedule, enabled: false);

        // unified-access-control-r2 task 158 (owner round 6) — a work assignment or project FILED UNDER a secure matter or
        // project is itself secure: secured through /provision-project's own steps (ProvisionInheritedAsync, for its
        // creator) and given its parents' sharees through the synchronizer above. Called by provisioning's Step 8 (a parent
        // becoming secure), by the unsecure endpoint (the still-secure-parent rule, the related-records list), by the BFF
        // create / re-file writers (through SecureRootFilingGate) and by the job below. Concrete (ADR-010); SCOPED because
        // the reconciler and synchronizer it composes are. Every dependency — IGenericEntityService, DataverseWebApiClient,
        // SpeFileStore, the POA seam, the reconciler, the synchronizer, the guard — is registered unconditionally.
        // §10/§11: notes/task-158-secure-inherit-filed-records.md.
        services.AddScoped<Sprk.Bff.Api.Services.Access.SecureRootInheritance>();

        // Task 158 — the safety net for filed records written outside the BFF (wizards, forms, imports, flows) and for a
        // parent's later share changes. Every five minutes (owner R3/R4), ENABLED WITH WRITES: owner round 6 says such a
        // record IS secure, every write is provisioning's own or the synchronizer's add-only mirror, and nothing here ever
        // takes a record out of isolation. ADR-052 places it in the BFF on the in-process scheduler (ADR-036 A1 rule 6).
        // UNCONDITIONAL (ADR-032): IServiceScopeFactory and TimeProvider are unconditional.
        services.AddScheduledJob<Sprk.Bff.Api.Services.Access.SecureRootInheritanceJob>(
            Sprk.Bff.Api.Services.Access.SecureRootInheritanceJob.DefaultCronSchedule);

        // unified-access-control-r2 task 143 (owner Q4; round 3 R3/R4) — the No Access safety net: every 5 minutes,
        // every active entry is enforced through NoAccessShareEnforcer (out-of-band MDA shares after an entry, records
        // that became secure, links that appeared). ENABLED with writes ON, per the owner's R4 answer — it only ever
        // REMOVES, inside the enforcer's rules. ADR-052 places it in the BFF on the in-process scheduler (ADR-036 A1
        // rule 6). UNCONDITIONAL (ADR-032): IServiceScopeFactory, TimeProvider and IConfiguration are unconditional,
        // and the store/enforcer it resolves per run are registered unconditionally above.
        services.AddScheduledJob<NoAccessShareReconciliationJob>(NoAccessShareReconciliationJob.DefaultCronSchedule);

        return services;
    }

    /// <summary>
    /// Registers one external module (widget) on the module-host platform (FR-22 · ADR-028 A3). Purely
    /// additive: the <see cref="ExternalModuleRegistry"/> singleton aggregates every registered
    /// <see cref="ExternalModuleDescriptor"/> at startup. This is the "add a module = register" seam —
    /// no route, filter, resolver, or handler changes. Task 016 uses it to register the outside-counsel
    /// modules. Registration order does not matter — the registry factory enumerates every registered
    /// descriptor at resolution time — but keep module registration in <c>AddExternalAccess</c>'s
    /// composition path so the registry singleton is also registered.
    /// </summary>
    public static IServiceCollection AddExternalModule(
        this IServiceCollection services, ExternalModuleDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        services.AddSingleton(descriptor);
        return services;
    }
}
