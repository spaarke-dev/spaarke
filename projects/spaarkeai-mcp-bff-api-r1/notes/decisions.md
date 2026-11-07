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
