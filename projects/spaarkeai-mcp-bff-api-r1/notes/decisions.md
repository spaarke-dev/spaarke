# Decisions — spaarkeai-mcp-bff-api-r1

> Full decision log. The project `CLAUDE.md` §3 keeps the one-line binding outcome of each decision that still constrains work and links here. Mark superseded entries with the date; never delete them.

## 2026-11-05 — Owner decisions O-1 to O-24 (design rounds 1–3)

Recorded in full in `design.md` §0 (O-1 to O-24, with O-25 and O-26 added 2026-11-06). Highlights that bind execution:
- O-2 / O-14: v1 includes completing a Work Item in the wizard; no model-callable complete tool.
- O-3 / O-23: federated connector primary; plugin package also built; Spaarke AI agent only as the T1 fallback.
- O-4: v1 users are in the BFF's own tenant (dev tenant).
- O-12 / O-24: production auth — OAuth at the Spaarke-tenant authority; ADR-028 amended by path B as **A10** (written, owner-approved).
- O-13: server-side lookups (B-3) in scope.
- O-15: the April 2026 Copilot surface stays (#2575, #1523) unless it interferes.
- O-16: widget embedded in the BFF; aim < 1 MB, hard ceiling 3 MB.
- O-17 / O-20: this project creates the connector and plugin package; the owner (Global Admin) runs the admin-center steps.
- O-18: `/` skill first plus direct prompts; org prompts later.
- O-19: one Copilot connector per product area, "Spaarke" + area; core member "Spaarke"; routes `/mcp/{area}`.
- O-22: a Signal produces a Work Item; resolving it writes a Decision Record.

Spec-interview decisions (2026-11-05): privileged Work Items appear to the model only as a neutral count line (FR-04); long lanes page through an app-only call (FR-05); the channel is not recorded on the Decision Record.

## 2026-11-06 — Naming conventions approved (O-25)

Route `/mcp/legal-operations`; functions are tool groups and skills, not routes; skills `spaarke-{area}-{capability}` (area dropped for core: `spaarke-decisions`); widget `ui://spaarke/decisions.html` in `src/client/widgets/spaarke-decisions/`; capability layer in `Services/Signals/`; "Spaarke AI" stays the agent name; "Spaarke Copilot" retired. Full table: `notes/coordination/2026-11-06-copilot-naming-and-ownership.md` §1.

## 2026-11-06 — Cross-project coordination is binding (O-26)

Agreements with legal-ai-accelerator-r1, ontology-platform-r1, ebilling-r1 and unified-access-control-r2: `notes/coordination/2026-11-06-copilot-naming-and-ownership.md` §2; design §20. Includes ontology rulings D-208 ("Finding" only for billing link-outs) and D-209 (qualify "playbook" by format).

## 2026-11-06 — Merge of #2580 / #2579 into this branch

The owner chose to merge ontology's held PRs into this branch so the plan covers all work with nothing deferred: #2580 `9423f37004` (merge `489f6508fc`) and #2579 `ab04492ab7` (merge `026b61554a`). BFF builds 0 warnings / 0 errors. Ontology's three conditions apply:
1. No dev BFF deploy from this branch until their pair has deployed (waits for ebilling task 112). Phase 0 runs on a master-based spike branch; if this branch must deploy sooner, back the merge out first.
2. D-207 order and replay guard unchanged; MCP commit only through `DecisionCommitService`.
3. Their pair merges to master first; re-merge on moves, never rebase.

Consequence: spec FR-12, Dependencies and the "PR #2580 timing" open question were updated the same day (only the deploy gate remains). Facts: `notes/pre-pipeline-context.md`.

## 2026-11-06 — Portfolio Epic

The project registers under Epic **#421 SPAARKE AI AND AUTOMATION** (owner choice).

## 2026-11-06 — Hot-path declaration: root-CLAUDE-md = YES

FR-18 adds a root `CLAUDE.md` §1.1 row ("Spaarke for Microsoft 365 Copilot" / "Spaarke"), so the hot-path declaration in design §16 and spec says `root-CLAUDE-md` = YES (commit `c384cf22fd`).

## 2026-11-06 — Plan-level choices (project-setup, delegated by the spec)

- FR-05 paging: a fifth **app-only, readOnly** tool with a cursor (working name `spaarke_list_decision_page`; final name fixed in task 022 under the naming note). The model-visible tool keeps its closed schema.
- Plugin package folder: `src/solutions/CopilotPlugins/spaarke/`; runbooks in `docs/guides/`. Task 040 may adjust if build tooling requires, and records the change here.
- B-4 split into two tasks (010 caller context + Dataverse client; 012 thread through executors, route cores and notifier) after discovery found `HttpContext` beyond the spec's list. **Superseded 2026-11-07** by option C (below): B-4 is deferred, 010 becomes a verification task and 012 is cancelled.
- B-5 adds `sprk_privilegeflagged` to the worklist list projection (projection only; read rules unchanged) so FR-04 can be met.

## 2026-11-06 — Phase 0 spike (task 001): deviations and findings that reach production tasks

Evidence: `notes/spikes/phase0-spike-measurements.md`. Spike head `5c9e301572` on `spike/spaarkeai-mcp-phase0` (never merged).
- Adding `ModelContextProtocol.AspNetCore` 2.3.1 lifts `Microsoft.Extensions.AI.Abstractions` to 10.11.0 and needs `Microsoft.Extensions.Logging.Abstractions >= 10.0.13`. `Spaarke.sln` restore then fails with NU1605 in `Sprk.Provisioning.ControlPlane.NightlyTests` (pins 10.0.11; references the BFF). Task 020 must bump that pin (and any other BFF-referencing project that pins lower) and fold the `Microsoft.Extensions.AI` alignment into the NFR-08 AI suites.
- Widget variants: the 1, 3 and 6 MB widgets are three extra resources plus `spike.html` (alias of 1 MB); the 3 and 6 MB ones are not bound to a tool because T13 fixes each route's tool set. 003 binds a variant by editing the spike or the tool meta.
- `spike_whoami` returns `tid` and `oid` GUIDs plus booleans for `aud` (matches the BFF app) and `scp` (has `user_impersonation`); no claim text.

## 2026-11-06 — Phase 0 spike dev deploy: option (a), short window (owner, in uac-r2's session)

- **Decision:** the spike is deployed to the shared dev BFF in a time-boxed window. A separate slot or App Service (option b) is rejected. That copy would run the whole BFF, including its scheduled reconcilers and Service Bus consumers, so two builds would compete for the same subscriptions and Dataverse rows. Feature-flagged code on master (option c) is the long-term route, through tasks 020/021.
- **Conditions (uac-r2):**
  1. The spike is rebuilt on the exact master SHA of uac-r2's queued batch deploy (#2292/#2610/044). `git diff <batch SHA>..<spike SHA>` must contain only `5c9e301572`'s files.
  2. Never-merge draft PR **#2615** with Router and Client lint green. Re-confirm the §10 numbers (+0.703 MB, no vulnerable packages) on the rebuilt SHA.
  3. The window opens after the batch gate passes and lasts at most 24 h unless the owner extends it. Master deploys pause during it. An urgent peer fix may end it early, with the owner's go.
  4. The spike gate is the master server gate plus: anonymous `/mcp/legal-operations` and `/mcp/spike-area-b` return 401, and signed-in requests return 2xx.
  5. At the end, uac-r2 redeploys the then-current master.
  6. Auth uses the default workforce scheme only. T5 includes one negative check: a CIAM or A9 token returns 401.
- **Order:** the 046c watch (until 05:40Z), then the batch deploy (its own owner go), its gate, and then our window. uac-r2 sends the batch master SHA. We merge it into `spike/spaarkeai-mcp-phase0`, push, wait for green CI, and the owner gives the per-SHA go in uac-r2's session.

### Update 2026-11-06 (owner, in uac-r2's session): no time limit; master + spike on every dev deploy
- The owner wrote: "this is a dev/test environment so no risk -- there does not need to be a short time limit". **The 24 h window is dropped, and master deploys do not pause.**
- Until we declare Phase 0 done, every dev BFF deploy uac-r2 runs is **master + `5c9e301572`**. uac-r2 merges the spike commit onto the master SHA, checks that the diff is only the spike's files, and runs the spike gate with the usual server gate.
- Our obligations:
  - keep `5c9e301572` as one commit that cherry-picks cleanly onto master, and rebase it and send the new SHA if it conflicts;
  - keep draft PR #2615 open and unmerged, for CI;
  - tell uac-r2 when Phase 0 is done, so the next deploy is plain master.
- The first combined deploy is uac-r2's queued batch (#2292/#2610/044) after the 046c watch. It needs its own owner go.

## 2026-11-07 — Option C: defer `ICallerContext` (B-4); FR-10 amended (owner)

**Decision.** The owner chose option C. B-4's `ICallerContext` is not built in v1. MCP tools get the request `HttpContext` from `IHttpContextAccessor`. When it is null or the caller is unauthenticated, a tool returns an `isError` result with `UserContextRequired`. Otherwise it calls exactly the decision services the REST endpoints call:
- `SignalReadService.ReadAsync(set, httpContext.User, ct)`;
- the `DecisionPlanEndpoints` body: `SignalCoreRecordAccess`, `DecisionPlanService.GetAsync`, `DecisionPlanReadStamp.StampAsync(http)`, `DecisionParameterSources.OptionsAsync(http)`;
- `DecisionCommitService.CommitAsync(…, http, ct)`.

B-1 only moves the endpoint bodies into capability methods that take `HttpContext` (or `ClaimsPrincipal` where that is enough). B-2 (the mapper) needs no caller context. Nothing changes in `DataverseUserClient`, `CallerWhoAmIMemo`, `CallerRecordAccessProbe` or any chat/AI path. WhoAmI stays at 1 per commit (T170). Spec FR-10 is rewritten and supersedes design §8 B-4 (design.md is not edited).

**Evidence.** In `ModelContextProtocol.AspNetCore` 2.3.1 the stateless transport runs tool handlers inside the ASP.NET request:
- `StreamableHttpHandler` passes `context.RequestServices` with `ScopeRequests=false`, so a tool resolves services from the request scope;
- the default `PerSessionExecutionContext=false` keeps the request's execution context, so `IHttpContextAccessor` keeps working inside the tool.

**v1 limitation.** Decision capabilities run only inside an HTTP request. This holds while the server stays stateless with `PerSessionExecutionContext=false`. Future off-request scenarios (durable agents, queued continuations, Teams proactive) are blocked by the user-token problem, not by this refactor. `ComposeProfileDispatcher.cs:88/117-134` shows the existing detached-context workaround.

**Regression-test set** (task 010, rewritten as the MCP caller-path verification):
1. On a real host, a tool sees a non-null accessor `HttpContext` that is the same instance a test middleware captured, and its `IServiceProvider` is `RequestServices`.
2. The real `DataverseUserClient` behind a hand-written fake wire handler (the `ConfidentialClientMigrationSeamTests` pattern): WhoAmI succeeds over the MCP route; with no bearer token or no context it returns `UserContextRequired`.
3. An MCP `spaarke_complete_decision` variant of T170 with WhoAmIs == 1.
4. A plan-read stamp written by the MCP plan tool satisfies an MCP commit.
5. A pin on the stateless transport and `PerSessionExecutionContext=false`.
6. Existing suites unchanged: T170, `CallerWhoAmIMemoIsolationTests`, the chat `dataverse.*` handler tests, the `DecisionCommitService`/Signal tests and the 011 baselines.

**Task consequences.**
- 010 keeps its id, the auth tag, FULL rigor, opus and the uac-r2 review. It depends on 020, 021, 023 and 024, and finishes before 025.
- 012 is cancelled, together with its `DecisionSliceHttpContextGuardTests` ArchTest guard and 014's "empty the ratchet baseline" step.
- 017 depends on 011 only; 013 depends on 011 and 017. The critical path now starts 011 → 017 → 013 → 014.
- The commit invariants and D-207 text in 014 are unchanged.

## 2026-11-07 — Task 017 (B-2 result mapper): deviations and decisions

- **Shape.** `Api/Signals/DecisionResultMapper.cs` (static, no DI) holds every failure mapping of the worklist, decision-plan, suppression and decision routes. REST: `ToHttp(SignalReadResult|DecisionCommitResult|SignalAccessOutcome, HttpContext)`, `PlanRefused`, `AccessUnavailable`, `RecordNotFound`, `SuppressionUnavailable`, `SuppressionPartitionUndetermined`. MCP: `ToToolError(SignalReadResult|DecisionCommitResult|SignalAccessOutcome)` and `ToToolError(reasonCode, status)`, returning `DecisionToolError(ReasonCode, Message, Status)`. The plan route has no single result type yet, so its failures are named helpers; 013 adds a typed overload when it introduces the capability's plan result.
- **No `IsError` member on `DecisionToolError`.** A non-null `DecisionToolError` is the error; 020's `McpToolResults` sets `isError: true`. Success maps to null.
- **Reason-code constants stay where they are** (`DecisionPlanEndpoints.*ReasonCode`, `SignalReadEndpoints.WorklistUnavailableReasonCode`, `DecisionCommitReasons`, `DecisionActionReasons`, `ProblemDetailsHelper.RecordUnavailableReasonCode`). Moving them would rename public-to-assembly members used by the Ontology admin endpoints and tests (POML: reuse unchanged). The one reason code written as a literal in an endpoint description (`DecidedInInvoiceReview` in `DecisionCommitEndpoints`) is now interpolated from the constant; the OpenAPI text is unchanged.
- **`DecisionPlanEndpoints.cs` L251-345 (the POML's range) is not moved.** Those lines are the success-body DTOs (`DecisionPlanResponse.From`, `DecisionActionDto.From`, ...), not failure mapping; the MCP plan tool (023) can call `DecisionPlanResponse.From` as it is. Only the failure statics (`AccessUnavailable`, `CallerUnresolved`, the inline 422/503 problems) moved.
- **The worklist `set` 400 (`Results.ValidationProblem`) stays in `SignalReadEndpoints`.** It is input validation with no reason code; the MCP list tool takes a closed enum.
- **MCP messages are fixed text per reason code**, never a result's `Detail` (some details carry writer text, e.g. `record_refused`'s `({ex.Reason})`): ADR-015. An unknown code passes through with a general message for its status class.
- **A commit failure with a null or blank reason code** (none is built today) maps to the service's own default for that status (`step_failed`, `idempotency_unavailable`, `review_in_progress`, `not_authorized`), `signal_not_open` for Conflict, `review_invalid` for any other refusal, so a domain failure never becomes an exception. REST is unchanged (it keeps the null).
- **Test scope beyond 011's inventory** (one test, `ToToolError_EveryDeclaredReasonCode_HasItsOwnFixMessage`): every `DecisionCommitReasons`/`DecisionActionReasons` constant has its own message. Justification: the goal is every code REST can return, and `gate_undeclared`, `step_failed` and the executors' own codes are returnable but not baselined; it also fails when 015 adds `ontology.worklist.cursor_invalid` without a message. `ontology.signal.partition_undetermined` (not baselined) has its own test.
- **Finance drift (recorded, not changed; POML constraint).** Finance answers with an `errorCode` extension, not `reasonCode`: 30 `["errorCode"]` sites in 13 files under `Api/Finance/` (e.g. `ApExportEndpoints.cs`, `RuleSetEndpoints.cs`). A Finance MCP slice (design §17) must map through this mapper's vocabulary or fix that drift first.

## 2026-11-07 — Spike route guard and uac-r2 approval

- The spike failed the Router ArchTests: the scanner could not read `MapMcp`.
- Guard commit `fdfd7b1d23` on `spike/spaarkeai-mcp-phase0` fixes it. The scanner now reads `MapMcp(path)` as one POST route, and the commit adds must-fire and must-not-fire controls.
- The spike routes carry Permanent waivers: CallerScopedOnly for `legal-operations` and CallerSuppliedContentOnly for `spike-area-b`.
- Spike HEAD `22487c6537` contains master `d53d67f604`. CI is green.
- **uac-r2 approved it for the next dev window.** Deploying still needs the owner's go in their session.
- **uac-r2 condition for task 021:** no Permanent waiver carries over. Real tool-level policies replace them, and the ledger entries are rebuilt from scratch. This is now a constraint in the 021 POML.

## 2026-11-07 — Ontology review of 017: OK; binding K1 for 014

- Ontology reviewed `21882337f7`: OK, no F findings.
- **Binding for 014:** the MCP tool error for a Failed commit must carry the D-17 partial-write facts as an optional `DecisionToolError.Partial`: `failedStep`, `landed`, `possiblyLanded`, `recordMayExist`, `sentButNotRecorded`, `resumable`, `replayed`. It uses codes, ids and booleans only, never record text, and a test pins it.
- **Task 024** returns it in `structuredContent`.
- Both are now constraints in the POMLs.


## 2026-11-07 — Task 013 (B-1 capability: list and plan): deviations and decisions

- **Shape.** `Services/Signals/IDecisionCapability.cs` + `DecisionCapability.cs` (sealed partial; 014 adds `DecisionCapability.Commit.cs`). Members: `ListDecideAsync(ClaimsPrincipal?, ct, WorklistSet set = Lanes)`, `GetPlanAsync(HttpContext?, Guid, ct)`, `GetSuppressionAsync(HttpContext?, Guid, ct)`. Registered `AddScoped<IDecisionCapability, DecisionCapability>()` in `SignalsModule`. The interface is the seam two hosts (REST, MCP) consume.
- **`set` on the list.** The POML signature is `ListDecideAsync(ClaimsPrincipal, ct)`; the background says it calls `ReadAsync(set, user, ct)`. The REST route serves five sets, so the set is an optional trailing parameter (default `Lanes`). The list stays unfiltered; category partitioning is 022's.
- **Typed plan and suppression results (017's follow-up).** New `DecisionPlanReadResult` / `SuppressionReadResult` (with outcome enums) in `IDecisionCapability.cs`, and typed `DecisionResultMapper.ToHttp`/`ToToolError` overloads. The named helpers (`PlanRefused`, `AccessUnavailable`, ...) stay and the overloads call them, so the REST bytes come from the same code.
- **`UserContextRequired`.** The guard is "no `HttpContext`, or no authenticated identity" (the same test as `RequireAuthorization`'s default policy, so a request that reached a REST route always passes it). Both hosts answer it alike: 401 with `reasonCode` `DATAVERSE_USER_CONTEXT_REQUIRED` (REST never reaches it: the route's own 401 comes first, so no baseline moves). It is new in the mapper vocabulary and has its own fix message. For the worklist, `SignalReadResult` is ontology's type with no such outcome, so the capability returns `DecisionCapability.UserContextRequiredReadResult` (the `CallerUnresolved` outcome tagged with that code as its `Reason`). After review round 1 (K2) the mapper recognises it by reference, not by reason, so a read-service `CallerUnresolved` that ever carries the same reason stays the single 403. No edit to `SignalReadService.cs`.
- **Log category kept.** The suppression body logs through `ILogger<SignalSuppressionService>` as the route did.
- **"Full Work Item row" (goal text, FR-06) is not in 013.** No route returns a single full row today; adding one would be a new read path, and the acceptance criteria name only the plan and the stamp. Left to 023 (with 015's projection). Recorded here so 023 does not assume 013 built it.
- **Test harness changes (no golden edit).** The 011 plan/suppression contract host registers `IDecisionCapability` (and a stateless `SignalReadService` placeholder, the host maps no worklist route). Existing route tests that called the endpoint statics with an unauthenticated `DefaultHttpContext` now pass a signed-in context (the state `RequireAuthorization` guarantees) and build the capability through `tests/unit/domain/Signals/DecisionCapabilityFixtures.cs`. The ArchTest ledger hops for the three routes now name `DecisionCapability.*` then the original seam (2 hops).
- **Test scope beyond the criteria** (one line each): the suppression no-caller case (FR-06 pairs plan + suppression in one tool, same guard); the REST-route stamp test (the plan-read stamp behaviour must not change on REST); mapper parity rows for every plan/suppression outcome (FR-09: every REST code has an MCP mapping test).
- **Review round 1 (adversarial, `ffe5b3a84f`).** F3: no test pinned that the plan read's 503 from the inquiry check writes no stamp; `DoLanePrivacyTests` now asserts it on both 503 paths, with the ordinary To Do as the positive control. F3: stale ledger line citation fixed. K2: by-reference match (above).
- **Known limit for 020/022 (review K1).** The guard and the stamp key read the `HttpContext` passed in, while the Dataverse reads take the bearer token from `IHttpContextAccessor.HttpContext`. On REST they are the same object. The MCP tools must pass the ambient context (the POML says so) and the host must stay stateless (FR-01), or a session-held context could pass the guard as one principal and read as another. 020 should test this.

## 2026-11-07 — 013 done; one-caller rule for MCP tools

- 013 is done (`51f124bb6d`, `ffe5b3a84f`, `2323a9bb3a`). The 011 baselines pass, and `IDecisionCapability` is now the REST/MCP seam.
- **Known limit, now a binding constraint in 020 and 022:** the capability's guard and stamp read the `HttpContext` argument, but the Dataverse reads use `IHttpContextAccessor`. MCP tools must pass the ambient accessor context only, never a constructed or cached one.
- 020 pins this by reference equality on a real stateless host. Task 010 test 1 covers the same ground.
- The full unit suite in 013's run stopped at about 80 minutes, with 40 failures outside Signals. These look like load timeouts on a shared machine: 4 of the classes re-ran and passed. Before the next push, we confirm them through the CI Tier 2 run on PR #2587.


## 2026-11-07 — Task 014 (B-1 capability: commit): deviations and decisions

- **Direct delegation, service untouched.** `IDecisionCapability.CommitAsync(HttpContext?, signalId, review, ct)` (`Services/Signals/DecisionCapability.Commit.cs`) calls `DecisionCommitService.CommitAsync(signalId, review, http, ct)`; `DecisionCommitService.cs` has an empty diff. Every invariant with before/after file:line: `notes/b1-commit-invariants.md`.
- **No-caller answer.** `DecisionCommitStatus` is ontology's enum (not editable here), so the guard returns `DecisionCapability.UserContextRequiredCommitResult`, a `CallerUnresolved` result tagged with `DATAVERSE_USER_CONTEXT_REQUIRED`. `DecisionResultMapper` recognises it by reference, as 013 does for the worklist: 401 on both hosts. A service `CallerUnresolved`, or a `with` copy (a ledger replay), stays the single 403.
- **D-17 facts for MCP (ontology review of 017, binding).** `DecisionToolError` has an optional `Partial` (`DecisionToolPartial`), set only for a `Failed` commit. It carries `failedStep`, `landed`, `possiblyLanded`, the steps (`DecisionToolStep`: key, code, phase, status, written, possiblyWritten, reasonCode; no `Detail`), `recordMayExist`, `sentButNotRecorded`, `resumable` and `replayed`, with the values REST returns. `reviewId` and `actionCode` (also REST Failed extensions) are not in it, because the constraint's list leaves them out; the failed action's code is in `Steps`. **For 024:** `Partial` serialises as `"partial": null` on every other error unless the tool omits nulls.
- **Deviation: one-line test-helper edit.** The POML says every `DecisionCommitServiceTests` partial passes "with no test edits". The handler now takes `IDecisionCapability`, so `ThroughTheRoute` (`DecisionCommitServiceTests.cs:1460`), which called the handler with `rig.Service()`, could not compile unchanged. It now passes `DecisionCapabilityFixtures.Capability(commits: rig.Service())`, and the three D-119 tests go through the real route path. No assertion changed; Task174 (13) and T170 were not touched. `DecisionCapabilityFixtures.Capability` gained an optional `commits` parameter.
- **Deviation: ArchTest hop limit.** The commit route's declared chain becomes handler → `DecisionCapability.CommitAsync` → `DecisionCommitService.CommitAsync` → `SignalCoreRecordAccess.AuthorizeAsync` (3 hops). First commit `d5bee692a6` raised the global cap to 3. After the adversarial review (K2: wider than needed) it was narrowed: the cap stays 2, and only `ThreeHopHandlerDecisionRoutes` (the commit route) may use 3. Must-fire controls: 3 hops on another route fails; 4 on the pinned route fails. uac-r2 should see this guard change.
- **Known limit (review K3, cost).** `DecisionCapability` is scoped and now takes `DecisionCommitService`, so worklist, plan and suppression requests also build the commit service's graph (executors, follow-ups, notifier). A constructor fault in that graph would now fail the read routes too. Every registration involved is unconditional, so nothing fails to resolve today. Fixing it means `Lazy<DecisionCommitService>` or a separate commit capability, and both need a `SignalsModule.cs` edit that this task may not make. Left as is; revisit if 015/022 measure a cost.
- **Spec/code gap: ISS-004 (#2628).** The spec says a commit needs a prior plan read, but the service takes "no stamp" as "the review started now". The acceptance criterion "no prior plan read returns the existing refusal reason code" is met in the one case where a refusal exists (`signal_not_open` for a system-closed Signal), and a positive control pins it. Making the plan read a hard gate is an owner/ontology decision.
- **Test scope beyond the criteria** (one line each): the WhoAmI = 1 test through the capability (FR-10 names it for the MCP commit, and the capability is that entry); the plan-read positive control (without it, the no-plan-read test could pass for an unrelated reason); `ToToolError_ServiceCallerUnresolvedWithTheUserContextRequiredReason_StaysTheSingle403` (pins the by-reference match against a `with` copy).
- **Mutation check.** With the guard narrowed to `http is null`, the mapper's 401 branch removed and `Partial` not set, 7 of the 17 new tests fail. With the code restored, all pass.

## 2026-11-07 — Task 016 (B-3 lookup search and REST route): decisions and deviations

- **Concrete choices for FR-07.** Cap: **25** candidates per call (`DecisionLookupSearch.MaxResults`; `$top=25` plus a server-side cut). Route: **`GET /api/v1/signals/{signalId:guid}/lookup?parameterCode=&query=`** in the existing Signals group of `DecisionPlanEndpoints.cs`, inheriting the group's `RequireAuthorization` (default workforce policy) and `dataverse-query` rate limit; no `ExternalCollaboration`, no external access guard; `EndpointMappingExtensions.cs` unchanged.
- **Deviation from step 2 (directional): not through `IDecisionCapability.GetPlanAsync`.** It writes the plan-read stamp the commit route reads as the review start (`DecisionPlanReadStamp`, "latest read wins"), so a search per keystroke would keep moving that start; its response also carries no matter id, which the budget scope needs. The search runs the plan route's own first two calls instead, unchanged and in the same order: `SignalCoreRecordAccess.AuthorizeAsync` (so an unreadable Signal gets the plan route's exact refusal) then `DecisionPlanService.GetAsync` (a refused plan is the plan route's 422). The Create budget / Mark complete plan adjustments change no Person or Lookup parameter, so they are not applied (known limit: on a matter with no active budget, `budget` answers an empty list rather than invalid_parameter).
- **Scope per table (design §8 B-3).** Person → active contacts (name or email contains the text). `sprk_budget` → active budgets with `_sprk_matter_value` = the Signal's core record when its table is `sprk_matter` (D-18; the rule `ReviseBudgetExecutor` refuses against). A Signal with no matter core reads nothing and gets an empty list (the Console also offered project budgets; the executor refuses them, so the server does not). `sprk_organization` → active organisations by name (`statecode eq 0` added; the Console's picker has none — narrowing only). A code two offered parameters disagree on, or a parameter that is not Person or a known Lookup, is invalid_parameter.
- **Typed text.** Trimmed; > 100 characters is invalid_parameter; SQL `LIKE` wildcards bracket-escaped (`[`→`[[]` first, then `%`→`[%]`, `_`→`[_]`; Dataverse runs `contains()` as `LIKE`, so a typed `%%` or `__` would otherwise list every row: adversarial review F4, same rule as `RecordContainerResolver.EscapeForLike`, which is private to another subsystem, so a local three-call chain rather than widening its visibility), then quotes doubled, then URL-encoded (`Uri.EscapeDataString`), so `'`, `&`, `#`, `?`, `+`, `%` cannot end the literal, the option or the URL. Contacts/organisations need ≥ 2 characters (empty list, no read, as the Console); a budget search with less lists the matter's budgets.
- **Vocabulary: no new reason code.** Unknown/unsearchable parameter → `ontology.decision.invalid_parameter` (422, `DecisionCommitReasons.InvalidParameter`). Search or plan read fault → `ontology.signal.access_unavailable` (503, the mapper's answer for "a read the route needs after the access check"). Mapping lives in `DecisionResultMapper.Lookup.cs`; `DecisionResultMapper` became `partial` (one-word change, to stay clear of 014's edits).
- **`[FromServices]` on the handler's search parameter** so the task 011 golden-baseline host (maps this group, does not register the search) still starts; production registers it unconditionally in `SignalsModule`.
- **Test scope beyond the criteria** (one line each): the client's `UserContextRequired` from the OBO client is passed through (FR-02 no app-only fallback); a plan-read fault is 503 (fail closed); the 200 JSON shape is pinned (new route, no golden baseline; 023 consumes it).
- **Seeding proofs (mutation, then restored):** dropping the matter clause fails the other-matter-budget tests (7 fail); removing the escaping fails the quote/operator tests; removing the cut fails the cap test; weakening the caller guard to a null check fails the no-caller tests; removing the LIKE escaping fails the wildcard tests (5 fail).
- **Known limit (K-class): "a record the caller cannot read never appears"** is proven at the module boundary (every lookup read goes through `IDataverseUserClient`; no app-only read is made), not against live Dataverse row security; a live check belongs to the deploy-gated E2E (041/042).
- **Review (adversarial, opus, one pass) and fixes.** No blocking defect. Fixed: F4 LIKE wildcards (above); F3 the unreadable-record test asserted a property of its own fake, now it asserts what the search owns (lookup rows only through the caller client; the strict app-only mock reads configuration only); F4 no end-to-end Do-lane reassign test, now `DoLanePrivacyTests` pins that the assignee lookup searches contacts for the assignee and answers a same-BU colleague exactly as an unreadable Signal (no contact read past the gate).
- **Known limits (K-class, not defects):** K4 the MCP fix text for `invalid_parameter` is commit-oriented (task 023 owns the tool wording); K2 a caller 403 on the lookup table maps to 503 "try again" rather than a permission message (fail closed; same as the plan route's after-gate reads); K3 one keystroke costs 4–6 caller round trips (WhoAmI, Signal, core pair, policy version, search) on the `dataverse-query` limiter (the Console debounces; caching the gate is out of scope); K1 a Signal whose core is a project gets an empty budget list (matches `ReviseBudgetExecutor`).
- **Observed, not adjudicated:** `IDecisionCapability` (task 013) is also a 1:1 interface counted in the ADR-010 ratchet without a documented entry; reported to the main session.

## 2026-11-07 — Ontology reviews of 014 and 016: OK; ISS-004 ruled intended

- **014 and 016:** OK, no F findings.
- **ISS-004 (#2628), closed.** "No plan-read stamp = review starts now" is intended. It is the fail-safe direction: earlier system closures count as before the review, so a system-closed Signal gets `signal_not_open`. The stamp is best-effort: cache, 4-hour TTL, never fails a read. A commit is always validated against the current plan. The spec wording is amended. For MCP, requiring the plan tool first is a usability guard only.
- **016 K2:** the class remarks now say a new plan adjustment that touches Person or Lookup parameters must also be applied in the lookup search.
- **016 K1:** type-ahead must debounce at 300 ms or more and keep the 2-character minimum. Added to task 032.
- **014 note (not blocking):** `DecisionCapability` builds `DecisionCommitService` on read routes too. If that becomes costly, use `Lazy<>`.

## 2026-11-07 - Task 015 (B-5 slim summary, privilege flag, paging cursor): decisions and deviations

- **Slim row vs. REST.** `WorklistItemRow` (the REST DTO, pinned byte for byte by the 011 baselines) gains no member. `WorklistItemSummary` is a separate projection. The privilege flag and the rank keys reach it through an `extras` side channel: `SignalReadService` keeps one public `ReadAsync`, which gains an optional last parameter `IDictionary<Guid, WorklistItemExtras>? extras = null` that it fills when given (`WorklistItemExtras`, `RankKey`). The routes call it without `extras`, so their behaviour and responses are unchanged. There is no `internal` overload: an overload pair broke the task 167 authorization guard (see Review below). The Lanes FetchXML merely selects one more column (`sprk_privilegeflagged`, added in code like the Decision Record column: `WithColumn`), so the config row in Dataverse is not edited. The 011 baselines and every existing golden file pass unedited. Step 6b (D-210) is untouched.
- **Deviation: the 600-byte row (FR-11) drops `Sentence`.** Title, subject name and rule short name are clipped by escaped-byte budget (48/56/32 bytes, ellipsis when cut) and nulls are not written. The rule sentence (up to 2,000 characters) cannot share a 600-byte row; the full row, sentence included, arrives with the plan. The members a Decide-lane row never carries (evidence lines, Decision Record, closed-set members, Do-lane subject reference) are omitted. No widget-needed member was dropped. Owner awareness: if the widget wants the sentence in the list, the row needs a bigger budget (50 rows are 30 KB at 600 bytes, so the cap has room to 1 KB).
- **Paging (FR-05).** Page size 50, stateless opaque cursor (base64url of a small versioned JSON). It carries only ids, rank keys, times: no record text. It records the cursor item, its group's core id and best rank, and the core ids of the groups already returned in full (the last 256). Resuming finishes the cursor's own group first, then the other groups in the current Console order, skipping returned groups. Reason: when a returned group's best item is resolved between pages (the Start-review walk does exactly that) the group moves later in the list, and a plain position cursor lists its remaining items twice. A mutation check (dropping `Returned`) fails the stale-cursor tests. Not signed: it only positions a window in the list the caller's own read returns.
- **Reason code.** `ontology.worklist.cursor_invalid` (400, `SignalReadEndpoints.CursorInvalidReasonCode`), mapped to an isError tool error by `DecisionResultMapper.ToToolError(DecidePageResult)`. The malformed-cursor check runs before any Dataverse read.
- **Capability.** `IDecisionCapability.ListDecidePageAsync(ClaimsPrincipal?, cursor, ct)` (option C: no `ICallerContext`). A null or unauthenticated caller is `UserContextRequired`, no app-only fallback. No new interface, DI registration, endpoint or package.
- **Seed note.** The world seeded for the paging tests now has truly mixed severities (a matter's best item is not its only item); the first draft passed severity per policy and the fake ignored it.
- **Known limits (K-class).** (1) A group's remainder is remembered only for the last 256 returned groups; a lane with more groups than that may list the remainder of a long-ago group again, and (only once the cursor holds 256 groups) passes over a group that moved ahead of the cursor's group between pages. Below 256 every group not yet returned is listed wherever it now ranks (015 review F1). (2) A tampered cursor can move the window but never shows a row the caller cannot read. (3) The Decide lane never holds a no-core group (a no-core Decide row is never returned), so the cursor does not need a Not filed key.
- **Build and tests.** Sprk.Bff.Api 0 warnings, 0 errors. Sprk.Bff.Api.Tests (Signals, WorklistItemSummary, ContractBaseline): 4393 pass, 2 skipped (live). Sprk.Bff.Api.IntegrationTests (Signal filter, includes the 011 worklist baselines and the one-batched-BillingEventRead test): 153 pass, 2 skipped (live). Spaarke.ArchTests: 1998 pass. 11 new paging/privilege integration tests; the unit tests cover the slim row, the cursor wire contract (malformed, tampered, byte-flipped) and the capability guards. Mutation checks: dropping `Returned` fails the stale-cursor tests; dropping the privilege column fails the privilege test.
- **Publish size** (dotnet publish -c Release -r linux-x64 framework-dependent, Compress-Archive Optimal over the output, PDBs included, fresh worktrees C:\wt015m / C:\wt015b, 191 files each side): master 23567b2f93 = 41,168,000 B (39.26 MB); branch 4b23aec7fd = 41,199,767 B; delta +31,767 B. Under 55/60 MB. dotnet list package --vulnerable --include-transitive: none. No new package.
- **Review (self, code-review + adr-check; no subagent).** Fixed in scope: (F) the first cursor carried the group's record number and type, i.e. record text in a model-visible result (FR-04): now ids, ranks and times only; (F) the task 167 authorization guard (`ArchTests`, 2 failures) did not credit `GET /api/v1/signals/worklist` once `ReadAsync` became an overload pair whose delegating body does not reach the caller client: now one `ReadAsync` with an optional `extras` sink, and `RankKey` / `WorklistItemExtras` are public records because the public method names them; (S) the paging test seed claimed mixed severities but the fake ignored per-policy severity: now truly mixed. ADR-010 (no new interface), ADR-013 (no AI type touched), ADR-008/028 (caller identity only, no app-only fallback): no conflict. Component justification is in the headers of `WorklistItemSummary.cs` and `DecideLanePaging.cs`.
- **PrivilegeFlagged has no production writer (ontology DI-64); FR-04 source pending owner decision (2026-11-07).**
- **Review fix F1 (adversarial review).** Resuming skipped any group not yet returned whose best item now ranked ahead of the cursor's group (a new Critical Signal on its matter, a subject turned high priority, a policy severity change), so its unseen items were lost. The rank check now applies only when the cursor holds `DecideCursor.MaxReturnedGroups` (256) groups; below that `Returned` is complete, because every Decide group has a core record. Regression test: `DecidePaging_AStaleCursor_WhenAnUnreturnedGroupNowRanksAheadOfTheCursorGroup_StillReturnsItsItemsOnce`.
- **Master sync.** origin/master has no change to `Services/Signals/**`, `Api/Signals/**` or the two test projects since the merge base. PR #2580 (task 174) overlaps the files but is already an ancestor of this branch. Ontology reviews `Services/Signals/**` and `Api/Signals/**` (noted in the commit body).
