using System.Reflection;
using System.Text.RegularExpressions;
using NetArchTest.Rules;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// ADR-010: Dependency Injection minimalism and feature modules
/// Validates that concrete services are registered unless a seam is required.
/// Checks for proper lifetime management (Singleton for expensive resources).
/// </summary>
public class ADR010_DITests
{
    /// <summary>
    /// Root path to the source code, resolved from the test output directory.
    /// </summary>
    private static readonly string SourceRoot = ResolveSourceRoot();

    [Fact(DisplayName = "ADR-010: Expensive resources should be registered as Singleton")]
    public void ExpensiveResourcesShouldBeSingleton()
    {
        // Arrange — scan DI module source files to verify lifetime registrations.
        // ADR-010 requires ServiceClient, GraphServiceClient, and HttpClient factory
        // to be registered as Singleton (these are expensive to create: ~500ms init).
        var diModulePath = Path.Combine(SourceRoot,
            "src", "server", "api", "Sprk.Bff.Api", "Infrastructure", "DI");
        var programPath = Path.Combine(SourceRoot,
            "src", "server", "api", "Sprk.Bff.Api", "Program.cs");

        Assert.True(Directory.Exists(diModulePath),
            $"DI module directory not found: {diModulePath}");

        // Collect all DI registration source code
        var diSourceFiles = Directory.GetFiles(diModulePath, "*.cs", SearchOption.AllDirectories).ToList();
        if (File.Exists(programPath))
            diSourceFiles.Add(programPath);

        var allDiSource = string.Join("\n", diSourceFiles.Select(File.ReadAllText));

        var violations = new List<string>();

        // Expensive types that MUST be Singleton
        var expensiveTypes = new[]
        {
            "ServiceClient",       // Dataverse ServiceClient (~500ms init)
            "GraphServiceClient",  // Microsoft Graph client
        };

        foreach (var typeName in expensiveTypes)
        {
            // Check for Scoped or Transient registrations of expensive types
            // Pattern: AddScoped<...TypeName...> or AddTransient<...TypeName...>
            var scopedPattern = $@"Add(?:Scoped|Transient)\s*<[^>]*{Regex.Escape(typeName)}";
            if (Regex.IsMatch(allDiSource, scopedPattern))
            {
                violations.Add($"{typeName} is registered as Scoped or Transient. " +
                    "ADR-010 requires expensive resources to be Singleton.");
            }
        }

        // Verify IHttpClientFactory is used (not new HttpClient()) in service registrations
        // Check across all server source for direct HttpClient construction
        var serverSourcePath = Path.Combine(SourceRoot, "src", "server", "api", "Sprk.Bff.Api");
        var bffFiles = Directory.GetFiles(serverSourcePath, "*.cs", SearchOption.AllDirectories);

        // Check that IHttpClientFactory is registered somewhere
        var hasHttpClientFactory = allDiSource.Contains("AddHttpClient") ||
                                   allDiSource.Contains("IHttpClientFactory");
        if (!hasHttpClientFactory)
        {
            violations.Add("IHttpClientFactory is not registered in DI modules. " +
                "HttpClient instances should be managed via IHttpClientFactory per ADR-010.");
        }

        // Check BFF service files for direct HttpClient construction (new HttpClient())
        // that bypasses IHttpClientFactory
        foreach (var file in bffFiles)
        {
            var content = File.ReadAllText(file);
            var fileName = Path.GetFileName(file);

            // Skip test files and DI module files (which register the factory)
            if (fileName.Contains("Test") || fileName.Contains("Module"))
                continue;

            // Detect: new HttpClient( — direct instantiation bypassing factory
            if (Regex.IsMatch(content, @"new\s+HttpClient\s*\("))
            {
                violations.Add($"{fileName}: directly instantiates HttpClient. " +
                    "Use IHttpClientFactory for proper connection pooling per ADR-010.");
            }
        }

        // Assert — test fails when expensive resources are not properly registered
        Assert.Empty(violations);
    }

    [Fact(DisplayName = "ADR-010: Services should be concrete unless seam required")]
    public void ServicesShouldBeConcreteUnlessSeamRequired()
    {
        // Arrange
        var assembly = typeof(Program).Assembly;

        // Act - Find interface/implementation pairs in our application namespace
        var allTypes = Types.InAssembly(assembly).GetTypes();
        var interfaces = allTypes.Where(t => t.IsInterface).ToList();
        var concretes = allTypes.Where(t => !t.IsInterface && !t.IsAbstract).ToList();

        // Framework interfaces are always allowed (not subject to ADR-010)
        var frameworkPrefixes = new[] { "System", "Microsoft", "Azure", "StackExchange" };

        // Count 1:1 interface-to-implementation mappings in our application namespace
        var oneToOneInterfaces = new List<string>();

        foreach (var iface in interfaces)
        {
            // Skip framework interfaces
            if (frameworkPrefixes.Any(p => iface.Namespace?.StartsWith(p) == true))
                continue;

            // Only check our application namespace
            if (iface.Namespace?.StartsWith("Sprk") != true &&
                iface.Namespace?.StartsWith("Spaarke") != true)
                continue;

            // Find implementations
            var implementations = concretes.Where(c => iface.IsAssignableFrom(c)).ToList();

            // If exactly 1 implementation, it's a 1:1 mapping that ADR-010 discourages
            if (implementations.Count == 1)
            {
                oneToOneInterfaces.Add($"{iface.Name} -> {implementations[0].Name}");
            }
        }

        // Ceiling-based assertion: the current count of 1:1 interface mappings must not increase.
        // ADR-010 says "register concretes unless a genuine seam is required."
        // Many existing 1:1 interfaces exist for testing (mock injection). These are grandfathered.
        // NEW 1:1 interfaces must be reviewed — either justify the seam or use concrete registration.
        //
        // To update the ceiling: review the new interface, determine if the seam is justified,
        // then increase the ceiling with a comment explaining the addition.
        //
        // Current inventory (as of 2026-03-14):
        //   - Testing seams (mock injection): ~40 interfaces
        //   - Architecture seams (ADR-013 AI, ADR-007 facade): ~15 interfaces
        //   - Worker/Job handler seams (multiple implementations): ~5 interfaces
        //   - Infrastructure seams (resilience, auth): ~8 interfaces
        //
        // Ceiling re-armed 2026-08-12 (dotnet-10-upgrade-r1 task 030): 76 → 153. The +77 growth
        // over ~5 months is legitimate DI/test seams accumulated across many feature projects
        // (Office/Job status, workspace/scope services, finance analysis, precedent/insight graph,
        // communication engine, membership, etc.) — every entry is an interface mocked in tests
        // (tests/CLAUDE.md mandates Moq at module boundaries) or a genuine multi-impl/architecture
        // seam per ADR-010's own testing-seam exception. The tripwire had simply not been maintained
        // as the codebase grew (it had drifted from 76 to 153 while still asserting 76). Setting the
        // ceiling to the current audited count grandfathers the legitimate set and RE-ARMS the ratchet:
        // any NEW 1:1 interface beyond 153 will fail this test and force the same justify-or-concrete
        // review. This is the maintenance procedure this test documents, not a suppression.
        // ⚠️ auth-v4 task 020 (2026-08-21) DID NOT RAISE THIS, though the task instructed it to.
        // Empirically verified instead of assumed: the real count is 151, not 153, and the new
        // IClientAssertionProvider -> ManagedIdentityAssertionProvider pair does NOT appear in the
        // list at all. Reason: this test scans `typeof(Program).Assembly` — the BFF assembly only —
        // and IClientAssertionProvider is declared in Spaarke.Dataverse. A CROSS-ASSEMBLY 1:1 seam
        // (contract in a shared library, implementation in the BFF) is therefore invisible here.
        // Raising the ceiling would have granted headroom for a future IN-assembly interface to
        // land unreviewed, which is the opposite of what this ratchet is for. Left at 153.
        // The blind spot is booked onto tasks 060/061 (the forcing-function tasks), where a
        // cross-assembly credential census belongs.
        //
        // ───────── Ceiling raised 153 → 155, 2026-08-27 (issue #839, spaarkeai-compose-r8) ─────────
        // This test began FAILING at 155 once the Tier 2 aggregator was repaired and stopped
        // reporting every ADR line as `pass` regardless of outcome (PR #840). The failure is
        // inherited from master, not from the branch that raised the ceiling: that branch adds no
        // interface to any assembly.
        //
        // ⚠️ READ THE NET NUMBER CAREFULLY. It is +2, and that is misleading: SEVEN 1:1 interfaces
        // were added and FIVE were removed. The removals bought headroom that hid five of the
        // additions from the ratchet entirely. A ceiling on a net count cannot see this; only the
        // list diff can. Anyone maintaining this number should diff the printed lists, not trust
        // the delta. (Method: check out the prior ceiling commit, set the ceiling to 0 so the list
        // prints, capture both lists, `comm` them.)
        //
        // Diff vs 5c3652f8d (the 2026-08-12 re-arm):
        //   ADDED (7):
        //     IFileSummarizeAi          -> FileSummarizeAi             ADR-013 / CLAUDE.md §10
        //     IPreferenceMemoryCapture  -> PreferenceMemoryCapture       bullet 3 REQUIRE CRUD→AI
        //       Both live in Services/Ai/PublicContracts/. These interfaces are not optional
        //       indirection — the facade boundary is a binding architecture rule, and removing them
        //       would violate it. 2 impls exist for IPreferenceMemoryCapture.
        //     IProvisioningEnqueuer     -> ServiceBusProvisioningEnqueuer   test seam (2 doubles)
        //     IAdvisoryCapabilityRunner -> AdvisoryCapabilityRunner         test seam (1 double)
        //       Each is mocked/faked in tests, which is ADR-010's own testing-seam exception.
        //     ITenantBudgetPolicy       -> TenantBudgetPolicy         ⚠️ WEAKEST — see below
        //     ITenantTokenLedger        -> InMemoryTenantTokenLedger  ⚠️ WEAKEST — see below
        //   REMOVED (5): IConfidenceScoringService, IEmailTemplateService, IOwnershipValidator,
        //     IScopeCopyService, IScopeInheritanceService.
        //
        // ⚠️ The two weak seams are grandfathered here but NOT endorsed. Neither has a second
        // implementation nor a test double today; their justification is documented future
        // evolution (ITenantTokenLedger's impl is named InMemory* with a Redis successor named per
        // ADR-009; ITenantBudgetPolicy is a fail-open pre-call gate). "Future flexibility" is
        // exactly the reasoning CLAUDE.md §11 question 3 rejects. They are grandfathered rather
        // than fixed because the AI-metering surface belongs to another project and converting it
        // to concrete registration is a behavior-affecting refactor outside an ArchTest
        // adjudication branch. Tracked so it is not lost — see the #839 PR description.
        //
        // 155 -> 156, same day, on merging master: ISecurableEntityRegistry ->
        // SecurableEntityRegistry (unified-access-control-r2 task 075). ACCEPTED as an
        // external-dependency seam: it wraps a live Dataverse metadata retrieval, and its XML doc
        // states an implementation contract the interface exists to impose — "Implementations MUST
        // throw rather than return an empty or partial set when the answer cannot be determined",
        // because "I could not find out whether this entity is securable" read as "it is not
        // securable" places content in a shared container, where SPE's CONTAINER-level permission
        // model exposes it to every member with no per-file deny available — and does so silently.
        // (Corrected 2026-09-02: this said "makes irreversible". Removing the item from the container
        // does end the access; the hazard is silent exposure until someone notices, not permanence.)
        // That is a contract, not indirection. Worth noting as the
        // ratchet behaving correctly: the count moved for a real reason and named the interface.
        //
        // ───────── Ceiling raised 156 → 157, 2026-09-09 (unified-access-control-r2) ─────────
        // IImpersonatedRootSetSource -> ImpersonatedRootSetSource.
        //
        // ⚠️ ADDED BY TASK 035, WHICH DID NOT RAISE THE CEILING. Found by task 024 — the branch had
        // been red here since e38548ce5. It survived CI because Tier 1 runs only the MUST-NOT subset
        // of Arch Tests and Tier 2 is advisory by design, so nothing blocking ever executed this
        // test. Worth knowing: this ratchet is effectively local-only enforcement today.
        //
        // The seam, assessed honestly rather than asserted:
        //   FOR  — it mirrors IImpersonatedCommunicationQuery, which is a GENUINE seam (one impl,
        //          and a real double, StubImpersonatedQuery, in ImpersonatedRootSetSourceTests).
        //          Task 035's POML mandated the interface explicitly as the ADR-010 testing seam.
        //   AGAINST — as of today it has ONE implementation, ZERO test doubles and ZERO consumers.
        //          Its justification is a FUTURE substitution, and "future flexibility" is exactly
        //          what CLAUDE.md §11 question 3 rejects. It is accepted on the strength of a NAMED
        //          IMMINENT consumer, not on a general principle.
        //
        // 🔴 FALSIFIABLE CONDITION — this is the point of writing it down. Task 036's whole job is
        // the flag-gated swap that substitutes this interface in the evaluator. If 036 lands and
        // does NOT substitute it (no double, no second implementation), the interface has no seam,
        // and the correct action is to register the concrete per ADR-010 and drop this ceiling back
        // to 156 — NOT to leave it grandfathered because the number already moved.
        // ───────── Ceiling raised 157 → 158, 2026-09-28 (spaarkeai-word-add-in-r1 task 080) ─────────
        // IRecordOwnershipResolver -> RecordOwnershipResolver.
        //
        // Surfaced by merging master (229 commits) rather than by a new commit here: the interface
        // has existed on this branch since db046e534, and the ratchet only saw it once master's
        // ceiling of 157 arrived. The addition is genuinely ours, confirmed by the printed list.
        //
        // SEAM JUSTIFICATION, against ADR-010's "unless seam required" exception:
        //   1. It is a NAMED architectural seam, not incidental indirection. The ADR-002 server-side
        //      write-path guidance (now on master via #1012) designates
        //      `Services/Dataverse/RecordOwnershipResolver.cs` the single BFF owner of invariant I-6
        //      (owner = a business-unit default owner team). WP-1 requires exactly one owner per
        //      invariant, so the seam is where that ownership is expressed and enforced.
        //   2. It is a TEST seam with real doubles. The resolution order it implements is
        //      record-first → REFUSE on a named-but-unreadable target → acting-user fallback →
        //      REFUSE, and the two refuse branches are secure-record isolation. Those branches must
        //      be provable without a live Dataverse, which is ADR-010's own testing-seam exception.
        //   3. Consumers take it as an OPTIONAL dependency so the shared `Spaarke.Dataverse` library
        //      never depends on a BFF service (create paths there receive an already-resolved team
        //      id). Registering the concrete instead would push a BFF type across that boundary.
        //
        // Not grandfathering a mistake: if task 080 is ever abandoned and the resolver deleted, drop
        // this ceiling back to 157 rather than leaving the headroom for an unreviewed interface.
        //
        // ───────── Reviewed at 158, 2026-10-05 (unified-access-control-r2 task 166 f1-v1) ─────────
        // IRelocatedFileIndexing -> RelocatedFileIndexing. The branch measured 157 before it (an
        // interface had been removed since the 158 above, leaving one slot of headroom), so the
        // addition consumed headroom and the ratchet stayed green — recorded here so it is not an
        // unreviewed one (the list was printed with the ceiling at 0 and diffed).
        // SEAM JUSTIFICATION: it is a Services/Ai/PublicContracts facade (ADR-013 / CLAUDE.md §10
        // bullet 3) that owner round 37 item 1 MANDATES by name ("ONE Services/Ai/PublicContracts
        // facade method"): CRUD code (DocumentContainerRelocator) may reach AI indexing only through
        // such an interface, never IRagService / IPostUploadIndexingEnqueuer. It is also a real test
        // seam: DocumentContainerRelocatorTests.RecordingIndexing is its double (index-pending and
        // retry are proven through it). Same category as IFileSummarizeAi / IPreferenceMemoryCapture.
        //
        // ───────── Ceiling raised 158 → 159, 2026-10-05 (batch-4 integration of unified-access-control-r2 task 165) ─────────
        // ISpeAdminContainerScopedRequest -> SearchItemsRequest. Surfaced by the merge, not a new commit: on task 165's own
        // branch the count stayed within 158. SEAM JUSTIFICATION: it is how SpeAdminTenantScopeFilter — an endpoint filter,
        // which sees route arguments only as object — finds a container id in a BOUND BODY (owner round 20 item 2, the
        // per-container rule; SpeAdminTenantScopeFilter.cs, the bound-body read). Its sibling ISpeAdminConfigScopedRequest has
        // two implementations and is not counted; this one has one today because only the item search names a container in
        // its body. Registering a concrete would mean the filter naming each request type, so a new container-scoped body
        // would silently skip the rule. It is a request contract, not a service, and never registered in DI.
        //
        // ───────── Ceiling LOWERED 159 → 158, 2026-10-06 (customer-provisioning-orchestration-r1 task 227f) ─────────
        // ITenantContainerResolver -> OptionsTenantContainerResolver REMOVED with the I4 diagnostic route it served: no
        // consumer remained (the L2 probe that called the route was unregistered; H13's I4 reads the deployed app
        // settings), and SpeContainerOwnershipGuard is the BFF's one definition of this stamp's containers (T227d).
        //
        // ───────── Ceiling raised 158 → 159, 2026-10-06 (customer-provisioning-orchestration-r1 task 230b) ─────────
        // IAiKeylessProbe -> AiKeylessProbe. SEAM JUSTIFICATION: it is the Services/Ai/PublicContracts facade (ADR-013) through
        // which Infrastructure/Diagnostics/KeylessProofService reaches the AI-owned probes, and the module boundary the
        // keyless-proof contract test substitutes — the real probes call Azure OpenAI, Document Intelligence, AI Search,
        // Cosmos, Blob and Content Safety. Same category as IFileSummarizeAi / IPreferenceMemoryCapture.
        //
        // ───────── Ceiling LOWERED 159 → 157, 2026-10-06 (customer-provisioning-orchestration-r1 task 254) ─────────
        // The two "⚠️ WEAKEST" seams grandfathered above are gone. ITenantBudgetPolicy is replaced by the concrete
        // AiSpendLimit; ITenantTokenLedger by IAiSpendLedger, which has TWO implementations (RedisAiSpendLedger — the
        // "Redis successor" that note anticipated — and InMemoryAiSpendLedger), so it is not a 1:1 mapping.
        //
        // ───────── Ceiling raised 157 → 158, 2026-10-09 (unified-access-control-r2 task 176, #1511) ─────────
        // IRetrievalAccessTrim -> RetrievalAccessTrim. SEAM JUSTIFICATION: it is the Services/Ai/PublicContracts facade
        // (ADR-013 / CLAUDE.md §10) the task POML requires for the one access trim every AI retrieval path applies, so a
        // CRUD caller can trim without reaching IRagService. It is also a real test seam: PermitAllRetrievalAccessTrim
        // (tests/integration/Shared) is its double in the suites that test what retrieval callers do with rows they MAY
        // see, while the trim itself is tested for real in Issue1511_AiRetrievalAccessTrimTests. Same category as
        // IRelocatedFileIndexing / IAiKeylessProbe. Verified as the only addition (master measured 157).
        //
        // ───────── Ceiling raised 158 → 159, 2026-10-10 (spaarke-SPA-external-access-platform-r3 task 033) ─────────
        // IAppOnlyCommunicationQuery -> DataverseAppOnlyCommunicationQuery. SEAM JUSTIFICATION: the app-only twin of
        // IImpersonatedCommunicationQuery (accepted above as a GENUINE seam), for the external-plane message read (FR-04):
        // a contact has no systemuser to impersonate and the external plane forbids OBO. It is a real test seam — the
        // internal-only negatives (a CIAM or workforce-contact caller never receives an internal-only message) are proven
        // through its Moq double in ExternalMessageEndpointsTests, and ADR-038 bans Mock<HttpMessageHandler>. Adding an
        // app-only member to IImpersonatedCommunicationQuery instead would contradict that interface's "no app-only
        // fallback" contract and break its hand-written test doubles.
        //
        // ───────── Ceiling raised 159 → 160, 2026-10-16 (spaarke-ontology-platform-r1 task 101) ─────────
        // IOntologyAdminAuthoringSignalEngine -> OntologyAdminAuthoringSignalEngine. SEAM JUSTIFICATION: the module boundary
        // between the admin authoring routes (which run AS THE CALLER) and the evaluator identity: the dry run's
        // SignalEvaluator.EvaluateAsync and the publish's SignalReevaluationJob.ReevaluatePolicyAsync. Both targets are sealed
        // concrete singletons over the application-identity client and the writer, so the route tests could not run them
        // without a full Dataverse fake. The production implementation is two delegating lines and holds no logic. It is a real
        // test seam: RecordingEngine (tests/integration/auth/Ontology/OntologyAdminAuthoringEndpointTests.cs) is its double, and
        // the security negatives are proven through it. They are: the dry run evaluates a policy built from the CALLER's read,
        // never the request's lane; a non-Ontology or unreadable policy is never evaluated; publish re-evaluates once, after the
        // pointer moved. Same category as IAppOnlyCommunicationQuery.
        // ───────── Ceiling raised 159 → 160, 2026-10-15 (ebilling-r1 task 025) ─────────
        // IEbillingSettingsReader -> EbillingSettingsReader. SEAM JUSTIFICATION: it is the Dataverse boundary of the eBilling
        // business settings (FR-48, D-59) and the module boundary every eBilling consumer fakes: the rejection gate (task 046),
        // coding chunking and the default code set (047), approver resolution (103), the AP export mapping (150) and the firm-notice
        // AI toggle (131) are each proven by feeding them an EbillingSettings value through a Moq double, not by standing up a
        // FetchXml-reading singleton with a scoped IGenericEntityService behind it. The reader is a singleton that takes a scope per
        // cache-miss load (like IAllowedIndexesProvider above, which is the accepted precedent); a concrete registration would put
        // that Dataverse plumbing into every consumer test. Verified as the only addition on this branch (master measured 159).
        // ───────── Ceiling raised 160 → 161, 2026-10-15 (ebilling-r1 task 016) ─────────
        // IInvoiceTotalsService -> InvoiceTotalsService. SEAM JUSTIFICATION: it is the one server-side owner of invoice totals (FR-07,
        // I-1; ADR-002 one owner per invariant) and the module boundary its four named callers fake: the submission job (049) and the
        // line-decision, adjustment and approval operations (100-102) are each proven by a Moq double that returns a calculation or a
        // refusal (FR-32), not by standing up the Dataverse read-compute-conditional-write behind it. Same category as
        // IEbillingSettingsReader above. Falsifiable: if no caller fakes it by 102, register the concrete and drop this back to 160.
        // ───────── Ceiling raised 161 → 162, 2026-10-15 (ebilling-r1 task 014) ─────────
        // IInvoiceLifecycleService -> InvoiceLifecycleService. SEAM JUSTIFICATION: it is the single server-side owner of invoice status
        // (FR-06, ADR-002) and the module boundary of four later write-path callers: approve (103), reject / return (104), send to AP (150)
        // and mark paid (151). Each of their tests must prove what the caller does with every outcome (Transitioned, AlreadyInTarget,
        // Refused with a reason) by feeding it that InvoiceTransitionResult through a Moq double; without the interface each caller's test
        // would stand up the Dataverse conditional-write plumbing (versionnumber reads, If-Match, retry) behind a concrete. The service has
        // exactly one implementation, so it is a 1:1 mapping, accepted on the same grounds as IEbillingSettingsReader above.
        // ───────── Ceiling raised 162 → 163, 2026-10-16 (ebilling-r1 task 046) ─────────
        // IInvoiceResolutionService -> InvoiceResolutionService. SEAM JUSTIFICATION: it is Processing Pipeline step 2 (FR-23, D-47) and the
        // module boundary the pipeline orchestration (049) is proven against: 049's tests must drive every branch of what it does with a
        // resolution (matched / unmatched timekeepers, a missing or ambiguous rate, a failed arithmetic check, a confidence on either
        // side of the reliability threshold) by feeding it an InvoiceResolutionResult through a Moq double, not by standing up the roster
        // and rate-card Dataverse reads plus the embedding call behind it. One implementation, so a 1:1 mapping, accepted on the same
        // grounds as IInvoiceTotalsService above. Its collaborators (TimekeeperMatcher, the static calculators and resolvers) are
        // concrete. Falsifiable: if 049 does not fake it, register the concrete and drop this back to 162.
        // ───────── Ceiling raised 163 → 165, 2026-10-16 (ebilling-r1 task 047; +2 interfaces) ─────────
        // (1) ILineCodingService -> LineCodingService. SEAM JUSTIFICATION: it is the module boundary of two named callers that each
        // fake it: the invoice submission job (049) is proven by feeding it an InvoiceCodingResult (coded, refused-code, NotReturned and
        // firm-code-mismatch lines, per-line code-set source) through a Moq double, not by standing up the code-set reads, the chunked
        // IInvoiceAi calls and the server-side guard behind it; the code-set suggestion flow (048) calls it the same way. One
        // implementation, so a 1:1 mapping, accepted on the same grounds as IInvoiceTotalsService above. Falsifiable: if neither 048 nor
        // 049 fakes it, register the concrete and drop this back.
        // (2) ICodeSetSuggestionProvider -> NoCodeSetSuggestionProvider. SEAM JUSTIFICATION: FR-22 step 4 is produced by a different
        // Action in a later task (048). The seam lets 048 supply the AI provider by replacing one DI registration instead of editing
        // CodeSetResolver, and lets the resolver's tests fake the proposal. It has one implementation today (the Null-Object default,
        // ADR-032) and gains the second in 048; if 048 does not, collapse it into the resolver and drop this back.
        // Measured: master + the earlier eBilling tasks = 163 (after 046), this task adds exactly these two.
        // ───────── Ceiling raised 165 → 166, 2026-10-16 (ebilling-r1 task 045) ─────────
        // ITrackingNumberAllocator -> TrackingNumberAllocator. SEAM JUSTIFICATION: it is the single server-side owner of a submission's
        // {firm invoice number}.{n} (FR-10, D-37, D-87, ADR-002) and the only creator of sprk_invoicesubmission rows: the module boundary of the
        // submission operation (044, which creates every submission through it, D-87; a resubmission is a new submission through the same
        // route). Its tests must prove what the caller does with every outcome (Created, AlreadyCreated, Refused with ebilling.tracking.unavailable)
        // by feeding it that TrackingNumberResult through a Moq double; without the interface each would stand up the Dataverse
        // highest-sequence read, the alternate-key retry and its faults behind a concrete. One implementation, so a 1:1 mapping, accepted on
        // the same grounds as IInvoiceLifecycleService above. Falsifiable: if 044's tests do not fake it by 044's completion, register the
        // concrete and drop this back to 165.
        // ───────── Ceiling LOWERED 166 → 165, 2026-10-16 (ebilling-r1 task 048; +0 interfaces, -1 mapping) ─────────
        // ICodeSetSuggestionProvider now has two implementations (NoCodeSetSuggestionProvider and CodeSetProposalService, the AI provider that
        // 048 registers in its place), so it is a genuine seam and no longer a 1:1 mapping; this is the "gains the second in 048" the task 047
        // entry above promised. 048 adds no interface (CodeSetProposalService is registered concrete, ADR-010). Measured with the ceiling at 0:
        // 165 mappings, ICodeSetSuggestionProvider not among them.
        // ───────── Integration 2026-10-17 (ebilling-r1 merge of origin/master 1df4ee12d) ─────────
        // Both sides raised from 159: master +1 (ontology-r1 task 101, 160) and ebilling-r1 net +6 (165). Combined 166.
        // ───────── Ceiling RAISED 166 → 167, 2026-10-19 (ebilling-r1 task 049 part A; +1 interface) ─────────
        // IEbillingNotificationSink -> NoOpEbillingNotificationSink. Seam justification: task 132 replaces the no-op with a job-enqueuing sink
        // (recipients, email, SPA feed, approver in-app), and 049's processor tests fake the seam. Falsifiable: if 132 is dropped, register the
        // concrete and lower this back to 166. The 049 processor (part B) may add its own entry; sum the raises at merge.
        // ───────── Ceiling RAISED 167 → 168, 2026-10-19 (ebilling-r1 task 049 part C; +1 interface) ─────────
        // IInvoiceSubmissionPersister -> InvoiceSubmissionPersister. Seam justification: part E's handler tests fake persistence (the commit,
        // retry, re-evaluate and resume branches) the same way they fake IInvoiceSubmissionProcessor, without a Dataverse transaction double.
        // Falsifiable: if the handler tests drive the real persister over a fake IGenericEntityService, register the concrete and lower to 167.
        // ───────── Ceiling RAISED 168 → 169, 2026-10-19 (ebilling-r1 task 049 part D; +1 interface) ─────────
        // IInvoiceSubmissionAfterSteps -> InvoiceSubmissionAfterSteps. Seam justification: part E's handler tests fake the after-commit
        // steps (Completed / Retry / Refused / SubmissionMissing branches and the preflight refusal) as they fake the processor and the
        // persister; the real component needs the concrete SignalWriter, recorder and job queue. Falsifiable: if part E's handler tests
        // drive the real after-steps, register the concrete and lower to 168.
        const int knownOneToOneCeiling = 169;

        Assert.True(
            oneToOneInterfaces.Count <= knownOneToOneCeiling,
            $"ADR-010: 1:1 interface mapping count increased from {knownOneToOneCeiling} to {oneToOneInterfaces.Count}. " +
            $"New interfaces added without seam justification. " +
            $"Either register concrete per ADR-010 or update the ceiling with documentation.\n\n" +
            $"Current 1:1 interfaces ({oneToOneInterfaces.Count}):\n" +
            string.Join("\n", oneToOneInterfaces.Select(i => $"  - {i}")));
    }

    [Fact(DisplayName = "ADR-010: Feature modules should use extension methods")]
    public void FeatureModulesShouldUseExtensionMethods()
    {
        // Arrange
        var assembly = typeof(Program).Assembly;

        // Act - Look for service registration extension methods
        var extensionMethods = Types.InAssembly(assembly)
            .That()
            .HaveNameMatching(".*ServiceCollectionExtensions")
            .Or()
            .HaveNameEndingWith("Extensions")
            .GetTypes()
            .SelectMany(t => t.GetMethods())
            .Where(m => m.IsStatic &&
                       m.GetParameters().FirstOrDefault()?.ParameterType.Name == "IServiceCollection")
            .ToList();

        // Assert - Should have extension methods for feature module registration
        // This encourages modular service registration per ADR-010
        Assert.NotEmpty(extensionMethods);
    }

    [Fact(DisplayName = "ADR-010: Options pattern should be used for configuration")]
    public void OptionsPatternShouldBeUsedForConfiguration()
    {
        // Arrange
        var assembly = typeof(Program).Assembly;

        // Act - Find types ending with "Options" or "Settings"
        var configTypes = Types.InAssembly(assembly)
            .That()
            .HaveNameEndingWith("Options")
            .Or()
            .HaveNameEndingWith("Settings")
            .GetTypes();

        // Check that config types are POCOs (no complex dependencies)
        var violatingTypes = new List<string>();

        foreach (var type in configTypes)
        {
            if (HasConstructorDependencies(type))
                violatingTypes.Add($"{type.Name} has constructor dependencies (should be POCO)");
        }

        // Assert
        Assert.Empty(violatingTypes);
    }

    /// <summary>
    /// Determines whether an "Options"/"Settings"-suffixed type is a genuine Options-pattern
    /// violation: an appsettings-bound config POCO that wrongly declares constructor dependencies.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Positional-record parameter DTOs (e.g. <c>MembershipResolveOptions</c>,
    /// <c>BriefingWindowOptions</c>, <c>DataverseAuthorizationFilterOptions</c>) merely SHARE the
    /// "Options" suffix — they are method-argument bundles, never bound via
    /// <c>Configure&lt;T&gt;</c> / <c>IOptions&lt;T&gt;</c> from configuration. The compiler
    /// synthesizes a primary constructor whose positional parameters are DATA, not injected
    /// services, so the raw "ctor has parameters" heuristic yields false positives on them.
    /// The Options-pattern POCO rule (parameterless ctor + property binding) simply does not apply
    /// to records, so they are excluded. Genuine config classes (hand-written POCOs) are still
    /// flagged if they take constructor dependencies.
    /// </para>
    /// </remarks>
    private static bool HasConstructorDependencies(Type type)
    {
        // Exclude records: their parameterized primary ctor carries data, not DI dependencies.
        if (IsRecordType(type))
            return false;

        return type.GetConstructors().Any(ctor => ctor.GetParameters().Length > 0);
    }

    /// <summary>
    /// Reflection-stable record detection covering BOTH record class and record struct.
    /// The C# compiler synthesizes a <c>&lt;Clone&gt;$</c> method for a record <b>class</b> only
    /// (value-type records are copied by value, so they get no clone method). It synthesizes a
    /// <c>PrintMembers</c> member-printer for <b>every</b> record — class and struct alike — so that
    /// is the marker that also recognizes positional <c>readonly record struct</c> parameter-DTOs
    /// (e.g. <c>AutoFileSettings</c>, <c>TrackingFooterSettings</c> in the Communication engine).
    /// Fixed 2026-08-12 (dotnet-10-upgrade-r1 task 030): the old <c>&lt;Clone&gt;$</c>-only check
    /// false-positived on record structs, wrongly flagging their positional DATA parameters as
    /// constructor DI dependencies.
    /// </summary>
    private static bool IsRecordType(Type type)
        => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
               .Any(m => m.Name == "<Clone>$"        // record class
                      || m.Name == "PrintMembers");  // record class AND record struct

    // --- Negative controls for HasConstructorDependencies (live in the TEST assembly) ---

    /// <summary>Non-record config class with a ctor dependency — a GENUINE violation.</summary>
    private sealed class FakeConfigOptions
    {
        public FakeConfigOptions(IServiceProvider services) => _ = services;
    }

    /// <summary>Positional-record (class) parameter DTO — the false positive the exclusion removes.</summary>
    private sealed record FakeParameterDtoOptions(string Value, int Count);

    /// <summary>Positional <c>readonly record struct</c> parameter DTO — the value-type false
    /// positive that the <c>&lt;Clone&gt;$</c>-only check missed (record structs have no clone method).</summary>
    private readonly record struct FakeParameterDtoStructOptions(bool Enabled, double Threshold);

    [Fact(DisplayName = "ADR-010: Options ctor-dependency check flags non-record POCOs, excludes parameter-DTO records")]
    public void OptionsConstructorDependencyCheck_NegativeControl_StillFlagsGenuineViolations()
    {
        // A non-record "Options" class that takes a DI dependency in its ctor MUST still be flagged.
        // This proves the record-exclusion narrows false positives WITHOUT suppressing real violations.
        Assert.True(
            HasConstructorDependencies(typeof(FakeConfigOptions)),
            "Non-record Options POCO with constructor dependencies must still be flagged.");

        // A positional-record (class) parameter DTO MUST be excluded (its ctor params are data, not DI deps).
        Assert.False(
            HasConstructorDependencies(typeof(FakeParameterDtoOptions)),
            "Positional-record parameter-DTO must be excluded from the Options-pattern rule.");

        // A positional readonly record STRUCT parameter DTO MUST also be excluded — record structs
        // have no <Clone>$ method, so the old detection missed them (this is the task-030 fix).
        Assert.False(
            HasConstructorDependencies(typeof(FakeParameterDtoStructOptions)),
            "Positional record-struct parameter-DTO must be excluded from the Options-pattern rule.");
    }

    /// <summary>
    /// Resolve the repository source root by walking up from the test output directory.
    /// </summary>
    private static string ResolveSourceRoot()
    {
        var currentDir = AppContext.BaseDirectory;
        var dir = new DirectoryInfo(currentDir);

        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "src")) &&
                Directory.Exists(Path.Combine(dir.FullName, "tests")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }

        return currentDir;
    }
}
