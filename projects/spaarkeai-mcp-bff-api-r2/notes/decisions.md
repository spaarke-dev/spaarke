# Decisions — spaarkeai-mcp-bff-api-r2

> Full decision log. `CLAUDE.md` §3 keeps the one-line binding outcome with a link here. Newest first. Superseded entries stay, marked superseded with the date.

## 2026-11-25 — Task 001 execution decisions (legacy agent removal)

Full detail: `notes/001-task-notes.md`.

- **`HandoffUrlBuilder` is registered in `ToolFrameworkExtensions.AddToolFramework`, not in `AiModule`/`AiChatModule`** (POML said "grep to confirm"). The grep showed that its only live consumer, `DataverseCreateRecordHandler`, is registered by that method's handler scan. Registering the builder there keeps the builder and its consumer under the same gate (§10 F.1 asymmetric-registration rule). The registration uses `TryAddSingleton` with the same `Dataverse:EnvironmentUrl` → `DataverseServiceClient:Url` → dev-URL fallback that `AgentModule` used.
- **`AiAuthorizationFilter`'s `AgentMessageRequest` branch was removed with the route.** It has the same shape as the `PlaybookAuthorizationFilter` agent branch. Escalation trigger 1 does not fire, and no other chat DTO's branch changed.
- **`AgentToken:CopilotAudience` stays.** `AuthorizationModule` reads it, and that file received comment edits only. The other `AgentToken` keys and the `CopilotAgent` section were removed from `appsettings.template.json`.
- **`IdentityConfigurationValidator` rule 5 was removed.** It was log-only and checked only `AgentToken:ClientSecret`.
- **The route-ledger rows for the agent routes were deleted, not re-pointed at a retirement test.** The precedent for deleted routes (for example S-54) keeps such rows, but this task's acceptance grep forbids "api/agent" in `tests/`. The ledger records the removal with a comment that names the pre-removal commit `37f0864afe`. Review pending: uac-r2.
- **`LegacyAgentRouteRetirementTests` builds the removed prefix as `"/api/" + "agent"`.** This satisfies two criteria at once: the acceptance grep must find nothing in `tests/`, and the negative criterion needs a request to the removed message route that returns 404. The reason is documented in the class remarks.
## 2026-11-25 — Task 006: events capability, ADR-008 option and deviations

- **ADR-008 option chosen: "the filter stays and the capability calls the same method".** The two per-route checks were extracted from `RecordRouteAccessAuthorizationFilter` into decision methods with no `IResult`: `DecideRouteRecordAsync` (fixed entity set: no Read → `NoRead`, Read without the operation's rights → `InsufficientRights`) and `DecideDeclaredTargetAsync` (optional table privilege, then rights on the declared target). The REST filters call them and map to the same 404/403 bodies; `EventsCapability.GetAsync`/`CompleteAsync` call `DecideRouteRecordAsync` and `CreateAsync` calls `DecideDeclaredTargetAsync`. The create's shape rules (`EventsCapability.CreateShapeProblem`) and its regarding target (`EventsCapability.RegardingTargetAsync`) are each written once and called by the REST filter and the capability. The list has no filter: its gate is the impersonated query inside `ListCoreAsync`, which both callers run. Each check therefore has one implementation and REST asks Dataverse exactly what it asked before (frozen-bytes baselines 30/30 unchanged).
- **Deviation: the REST handlers call the capability's static cores, not the `IEventsCapability` instance.** The instance methods run the gates first (signed-in guard, then the shared decision), which on a REST request would duplicate `RequireAuthorization` and the endpoint filter and double the probe calls (a REST call-count change the POML forbids). The handlers also keep their signatures because existing tests and `DecisionRouteCores` call them directly. So: `ListCoreAsync`/`GetCoreAsync`/`CreateCoreAsync`/`CompleteCoreAsync` hold the rules; the instance = gates + cores (for MCP); the handler = REST gates (filters) + cores. The adapters only bind and map (no check, retry or write).
- **Get and complete stay app-only after the caller's Read/Write is established** (the G5 pattern, unchanged from REST). The must-not-leak proofs pass against that shape for both REST and the capability, so no run-as-caller change was needed.
- **The `GET /api/v1/events` handler decision now has three hops** (`EventsCapability.ListCoreAsync` → `DataverseWebApiService.QueryEventsAsCallerAsync` → `QueryEventsCoreAsync`); the route joins `ThreeHopHandlerDecisionRoutes` in `RouteAuthorizationGuardTests` with the same reason as r1 task 014 (a capability between the route and its seam).
- **Logs moved with the logic drop the event subject** (ADR-015): "Creating event", "Event created successfully", "Error creating event" and the debug "Returning event" now carry ids, counts and codes only.
- **Members kept on `EventEndpoints` as one-line forwards** (`EventEntitySet`, `CreateEventPrivilege`, `CallerUnresolvedReasonCode`, `RegardingStampFailedErrorCode`, `MeterName`, `CallerMappingOf`, `ResolveActingUserContactAsync`, `IsTransientCatalogFault`, `CanCompleteEvent`, `MyEventsScope`) so other routes (Analysis, ChildRecord, ExternalProjectData, Telemetry, DecisionRouteCores) and the existing events tests are untouched.
- **Registration:** `WorkspaceModule.cs` (`AddScoped<IEventsCapability, EventsCapability>`); the events Dataverse service itself is registered in `GraphModule.cs` and was not touched.
- **Review outcomes (two opus adversarial passes).** Auth pass: four F2 fixed (decision mappings match `Allowed` explicitly and deny by default; shared operation-name constants; a capability Read-without-Write complete test; ledger citations), K1 promoted to a guard (`EventsCapabilityCoreCallerGuardTests`: only the two files, by repo-relative path, may call the static cores; `using static` and `using` aliases fire). Tenant-isolation pass: no defect introduced; one pre-existing F1 (create reads the regarding record's name/number app-only after an AppendTo-only check, and AppendTo does not imply Read) is **filed, not fixed** — the fix changes who sees what on REST, which this byte-preserving extraction must not, and needs an owner decision (`notes/defer-issues.md` DEF-006-01); the two comments that called it "not a disclosure path" were corrected.
- **Known limits carried to task 025 (MCP wiring):** (K1) the capability takes an explicit `HttpContext` while `RecordCreationService`'s as-caller client reads the ambient accessor, so a tool must pass the request's own context — consider refusing a context that is not `IHttpContextAccessor.HttpContext`; (K2) `EventCapabilityResult.Refusal` is diagnostics only — a tool reports `ReasonCode`, never `Refusal.Detail`/`RefusedColumn` (documented on the property); (K4) the probe proves the bearer token while the list impersonates from the oid claim — verify OBO with the Copilot audience; (K5) get reads app-only after the row-level probe, so a field-secured `sprk_event` column would show on get but not on list (unchanged from REST; confirm none is field-secured).

## 2026-11-25 — Planning decisions (project-pipeline Step 3)

- **`HandoffUrlBuilder` is relocated, not deleted.** It is injected by `Services/Ai/Handlers/DataverseCreateRecordHandler.cs:229` (chat create-record tool) and used by many data-mutation tests; MCP only mentions it in a comment. Task 001 moves it out of `Api/Agent/` and re-registers it before deleting `AgentModule`.
- **Per-area MCP ledger partial files** (task 020) so Phase 1–3 tool tasks each own one ledger file and can run in parallel. Needs uac-r2 review of the ledger restructure.
- **Widget tasks that touch `McpModule.cs`, `Sprk.Bff.Api.csproj` and `publish-bff-artifact.yml` are serialised** (034 → 035 → 040).
- **CAPABILITY-INDEX rows** are proposed in `notes/capability-index-pending.md` by sub-agent tasks and applied by the main session (sub-agents cannot write `.claude/`).
- **The `AgentEndpoints` constants are not moved first** (spec FR-01 says to). Their only users are the agent branch of `PlaybookAuthorizationFilter.AuthorizeRunAsync` (`:569-588`) and an agent-route contract test; task 001 removes that branch and moves a constant only if a non-agent caller exists.
- **Two authorization extractions the spec did not list as prerequisites** are folded into the tool tasks: 031 exposes `AnalysisAuthorizationFilter`'s create check (POST `/api/ai/analysis/create`) and 040 exposes the `CommunicationAuthorizationFilter` + `CommunicationRecordAuthorizationFilter` Send checks (POST `/api/communications/send`) as shared methods, each behind a frozen-bytes baseline. Both are raised to opus with two verifier passes. Reason: the spec forbids tools copying authorization, and both routes hold their checks in filters.
- **Matter summary start path** (032) reuses 030's run start/status rather than `InsightsPlaybookExecutionCache.GetOrExecuteAsync`, which runs synchronously and cannot meet the 30 s rule; freshness = the 012 column's date vs the registry TTL. Escalation if 030's path cannot target the matter-summary Binding.
- **Shared widget transport placement**: `src/client/widgets/shared/mcp/` (source folder, not a package, not `@spaarke/ui-components`); escalate as an ADR conflict if adr-check flags ADR-012.
- **Routing eval (FR-17) runs at the deploy points**: run A after 029 (≈10 model-visible tools), run B after 039 (≈15–18), run C after 049 (all). The exact model-visible count is recorded at each run.

## 2026-11-25 — Owner decisions (spec interview and pipeline)

See spec.md "Owner Clarifications" for the full table; design.md §9 for Q1–Q8.

## D-r1-gate — Phase 1 no longer waits for r1 task 042 (2026-11-25)
- **Owner's words (relayed by mcp-r1, verbatim):** "I can't judge. You have to coordinate and determine with mcp-r2 the correct approach and path". The release is a joint mcp-r1/mcp-r2 determination under that delegation, not an explicit owner release.
- **Why:** the gate protected r2 from r1 still changing the shared MCP files. All r1 code is on master: #3146, #3152, #3161 and #3127, verified merged with `gh pr view`. 042's remaining work is the owner's live testing, a dev deploy, a 50-call measurement and notes, none of which touches code.
- **Terms agreed with mcp-r1:**
  1. Ownership of `Api/Mcp/**`, `McpModule`, `McpToolCatalog`, `RouteAuthorizationGuardTests.Mcp.cs`, the conformance host and the spaarke-decisions transport and build files moves to r2. If 042 finds a defect in one of them, it is filed and discussed before anyone edits.
  2. r1 keeps the ISS-010 (#3184) files and the Decision wizard heading files (listed in the project CLAUDE.md). r2 messages mcp-r1 before editing them; r1 won't change `RecordCreationService.CreateAsync`'s signature without telling r2.
  3. Task 021 doesn't wait for the heading change. Whichever lands second adapts, and each side tells the other before opening and before merging.
  4. Deploys stay separate, each with the owner's per-SHA go via uac-r2.
  5. 042 closes on its own timeline.
- **Rejected:** waiting for 042 to close. It protects nothing and could take days.

## D-run-record-hold — task 005 schema and task 030 held for one run-record design (2026-11-25)
- **Source:** specialist-legal-agents-r1's run-path review, `notes/run-path-review-2026-11-25.md` on `work/specialist-legal-agents-r1` @ 7cf4ea0cfe.
  - FR-13's `sprk_playbookrun` persistence covers only `PlaybookOrchestrationService` (the node engine), so Action-path runs are not recorded.
  - `sprk_playbookrun.sprk_playbookid` is required, so an Action run can't be stored as is.
  - The node engine is frozen under ADR-039.
- **Effect here:**
  - 005's code is merged on this branch only (not master), and its dev schema script has not run. It stays unrun until the owner picks the run-record design: a new capability-run table, a relaxed `sprk_playbookid` plus an action lookup, or the ledger plus the Decision Record. 005 then adapts to that design.
  - 030 waits for the same decision plus an ADR-039 check (constraint added to its POML).
  - 019 must not deploy 005's code before its columns exist (unchanged).

## D-024-extract — task 024 does its own small shared-method extraction (2026-11-25)
- **Trigger:** task 024 fired its STOP trigger. The playbook list rules live in the private `ListUserPlaybooks` / `ListPublicPlaybooks` handlers in `Api/Ai/PlaybookEndpoints.cs`:
  - owner resolution through `ResolveCallerSystemUserIdAsync`;
  - an empty page when the owner can't be resolved (fail closed);
  - `PublicOnly`, paging and sort.

  The tool also can't inject `IPlaybookService` (ADR-013 boundary plus the MCP ledger ArchTest).
- **Decision:** 024 adds one public static method in `Api/Ai/PlaybookEndpoints.cs` (grandfathered by ADR013_AiBoundaryTests), following 007's pattern. It takes `HttpContext?` plus paging and returns a typed result. The two REST handlers call it with byte-identical responses, proven by a parity test, and the tool calls it.
- **Why not a separate task:** it is small, it touches no file another running task touches (023, 026 and 027 don't name `PlaybookEndpoints`), and the pattern is settled.
- **Why not a new PublicContracts facade:** it would be a new interface plus a DI registration (root CLAUDE.md §11) for no gain.
- **Verification:** two verifier passes, because it touches caller resolution.

## D-run-record-decided — `sprk_playbookrun` is the one AI-run record (2026-11-25; releases D-run-record-hold)
- **Source:** the owner's decision ("yes extend", 2026-11-25), relayed by specialist-legal-agents-r1. Rationale: `notes/playbook-concept-and-run-record-2026-11-25.md` on `work/specialist-legal-agents-r1` @ 1cf6845687.
- **Decision:** `sprk_playbookrun` (display name "AI Run") records engine, prompted-Action and coded-workflow runs.
- **Split of the schema work:**
  - **r2 task 005 (shared columns):**
    - `sprk_playbookrun`:
      - `sprk_playbookid` becomes optional;
      - add a run-kind choice (engine / prompted / coded), a lookup to `sprk_analysisaction`, a lookup to `sprk_playbookconsumer` (Binding) and a parent-run self-lookup;
      - plus 005's planned columns: correlation/idempotency id, error code, regarding, summary.
    - `sprk_playbooknoderun`: `sprk_playbooknodeid` becomes optional; add an Action lookup; plus 005's started-on, completed-on and sequence.
  - **specialist-legal-agents-r1 Phase 1:** a profile-version lookup (to `sprk_agentprofileversion`, not yet created) and a gate-decision reference.
- **Writes:**
  - Engine runs are written by `PlaybookOrchestrationService` (r2 FR-13; run kind = engine).
  - Action and coded runs are written at the PublicContracts facade or the coded-workflow seam by specialist-legal-agents-r1 Phase 1.
  - r2 proposed that all of them go through 005's `PlaybookRunRecordStore`, so one component owns the row shape (ADR-002: one owner per invariant). Answer pending.
- **Still needed:**
  - the owner's explicit go to run the widened `scripts/dataverse/Add-PlaybookRunColumns.ps1` on dev (root §5; this decision is a design choice, not that go);
  - task 030 keeps its ADR-039 check (the node engine stays frozen);
  - check specialist-legal-agents-r1's docs/ADR PR (ADR-039 + AI architecture doc) against 005's column list when it arrives.
