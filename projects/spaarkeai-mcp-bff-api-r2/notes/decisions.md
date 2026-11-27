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
  - **Agreed (specialist-legal-agents-r1, 2026-11-25): one shared writer.** 005's `PlaybookRunRecordStore` owns the row shape and the write rules. Engine runs call it with run kind = engine. Phase 1 calls it from the PublicContracts facade and the coded-workflow seam for prompted and coded runs. Phase 1 adds its profile-version and gate-decision columns to the store and never writes the table directly.
  - All writes are fail-safe today. Phase 1 may need a "required" write mode, where a gated side-effect run fails closed if its gate attribution does not persist, or it may record that attribution in the Decision Record instead; Phase 1 design decides. 005 keeps the write call extensible (an options object) and does not add that mode now.
  - Docs PR #3213 (ADR-039 amendment, AI architecture doc §6.6) was checked against 005. Reported differences: the node-run started-on, completed-on and sequence columns, and the CAPABILITY-INDEX row 34/35 collision; on the master merge, fold our row 35 into #3213's row 35 and renumber ours from 36.
- **Still needed:**
  - the owner's explicit go to run the widened `scripts/dataverse/Add-PlaybookRunColumns.ps1` on dev (root §5; this decision is a design choice, not that go);
  - task 030 keeps its ADR-039 check (the node engine stays frozen);
  - check specialist-legal-agents-r1's docs/ADR PR (ADR-039 + AI architecture doc) against 005's column list when it arrives.

## D-030-adr039 — task 030 passes the ADR-039 check (Path C, comply) — 2026-11-26

- **Check:** the 030 POML's ADR-039 HOLD asks whether exposing existing node-engine playbooks to Copilot is "new capability on the frozen node-graph engine".
- **Result: no.**
  - `spaarke_run_playbook` / `spaarke_get_playbook_run` are thin MCP adapters over the existing playbook execute path.
  - Authorization goes through task 007's `PlaybookAuthorizationFilter.AuthorizePlaybookRunAsync`, a shared method.
  - Runs are recorded through task 005's `PlaybookRunRecordStore`.
  - They add no node types, no executors and no change to `PlaybookOrchestrationService` or the `sprk_playbooknode` graphs.
  - The freeze is on the engine, not on invoking its existing playbooks from another host.
- **The ADR-039 amendment (PR #3213, open, owner-approved Path B):** it states this directly. The freeze "applies to the node-graph *engine* only; playbooks as a concept ... are the live model". Every run is written through `PlaybookRunRecordStore`, the one writer.
- **Constraints carried into 030's brief:**
  - no edit to the engine, its executors or the node graphs;
  - the run goes through the same entry path and budget as REST;
  - Kind = Engine on the run record;
  - no second run store or writer;
  - if the implementation would need any engine change, stop and report: that would fail the check, and the 🔔 ADR Conflict goes to the owner.
- **Peer:** specialist-legal-agents-r1 raised the HOLD. This result is reported to it in the shared notes status file.

## D-030-run-model — playbook runs continue after the start reply, acting as the user (Option A) — owner, 2026-11-26

- **Owner's words (this session, 2026-11-26):** "Copilot rarely executed in 30 seconds - many operations take minutes. We should not require max 30 window because likely will not stay in windows and then always have to rerun. I'm not following what it means that user then runs in console - are you saying then goes to Spaarke system? If yes they breaks the entire UX of working in Copilot", then "Yes option A".
- **Decision:** `spaarke_run_playbook` replies within seconds with a run id (FR-14 start + status, unchanged). The run continues in the BFF for minutes after that reply, **acting as the user who started it** (delegated access, every read limited to that user's access). Copilot reads progress and the result through `spaarke_get_playbook_run`. Nobody is sent to the Spaarke Console to finish a run.
- **Rejected:**
  - Option 4: run inside the request, else hand off to the Console. It breaks the Copilot experience.
  - Option 3: the app's identity. It reads data wider than the user's access.
  - Option 2: Dataverse impersonation plus an engine change. It is blocked on #988–#990, covers Dataverse only, and is an engine change. It stays the longer-term target.
- **ADR consequence, CLAUDE.md §6.5 Path B:**
  - ADR-052 §5 MUST NOT ("the caller's token must not leave the BFF request that holds it") and B1 are amended with a narrow, named exception: one shared helper, process memory only, never stored or logged, capped run length, the user's own started run only.
  - The amendment lands with the 030 code.
  - It needs the owner's security sign-off (given in principle by this decision; the final text goes back to the owner) and a review by unified-access-control-r2.
- **Replaces:** the "ADR-052 Path C" line in spec.md ADR Tensions for run persistence. The run continuing after the reply is now Path B.
- **Also unblocks:** task 032 (matter summary starts a run). Compose's informal detach (#3224) should move onto the same helper; that is a follow-up, not this project.

## D-owner-answers-2026-11-26 — owner answers to open questions 1–18 (current-task.md numbering), this session

Owner's words verbatim, then what each one means here.

| # | Question | Owner's words | Meaning / action |
|---|---|---|---|
| 1 | 005: run `Add-PlaybookRunColumns.ps1` on dev; accept acceptance criterion 3 as "identical except the documented differences" | "yes go ahead and run" | Go given. The main session runs the script on dev (spaarkedev1). AC-3 is accepted with the differences listed in notes/005-schema.md §4 items 1–3. |
| 2 | 012: registry-key PATCH on dev | "yes go ahead to update" | Go given. The main session runs `Set-MatterSummaryTopicRegistryKey.ps1` on dev. |
| 3 | 008: ADR-028 A10 conflict | "path A" | Path A, a project exception: FR-11 parity wins, and the code is unchanged. Recorded in spec.md ADR Tensions. |
| 4 | #3181 fix (a) or (b) | "what is #3181 provide details and recommendation" | Explanation sent. Answered 2026-11-27: option (b), see D-3181. |
| 5 | 019: is runbook E1 done? | "no this hasn't been done--need steps" | E1 is not done, so 019 stays blocked. Steps sent to the owner. |
| 6 | design.md lacks `<hot-path-declaration>` | "yes add it so have complete documentation" | Add the block from spec.md to design.md. The owner's instruction overrides the brief's "do not edit design.md" for this block only. |
| 7 | Consumer tsc via a draft PR | "ok" | Go: open a draft PR that runs consumer tsc for DailyBriefing and LegalWorkspace. |
| 8 | REST changes (#3205, #3208, #3215, D-027-2, 024 499) | "ok" | Accepted as made or proposed. |
| 9 | D-023-preview | "what is advantage to allowing preview link to be shown? if low risk and it is a user advantage then include it--but if minimal feature value then omit" | The owner set the rule. The main session's assessment: little user value in Copilot (the browser link already opens the document in the web viewer), and a real risk (an app-only link that works without sign-in until it expires, written into the chat transcript). So **omit** (D-023-preview stands, owner-confirmed by rule). |
| 10 | NFR-05: does an oid in a log count as a "claim"? | "not sure exactly what this means or the implications--what does this impact?" | Explanation sent. Open until the owner answers. |
| 11 | uac-r2: the document list checks Read on the matter only (023 D1) | "yes that's sufficient" | Accepted. |
| 12 | Stray python.exe processes | "yes stop--AND IMPORTANT we need some way to check this and not allow this to happen; it uses up a lot of resources and slows up other work processing--what is the fix?" | Stop them. Propose a mechanised prevention (root CLAUDE.md enforcement ladder). |
| 13 | Cowork review skill pairing (for accelerator-r1) | "ok BUT ensure it is happening or has been communicated so not lost" | Send it to acclerator-r1 and file a tracked issue. |
| 15 | MCP AI-tool rate limit | "accept" | Accepted as is: the `mcp` limiter is 60 per minute per user. No per-tool limiter. |
| 16 | FR-09 saved preferences | "default preferences" | Narrow FR-09 in the spec to the Console's default preferences. No preference reader. #3232 stays a backlog follow-up. |
| 17 | Run results in Copilot | "yes add these feature capabilities; present in modern UI/UX" | New scope: a run-results widget in Copilot (MCP App widget, modern Fluent v9 UI). A new task. |
| 18 | Run instructions argument | "yes add this feature but make easy for user to use and do not add many other steps" | New scope: an optional, capped `instructions` argument on `spaarke_run_playbook`. No extra prompt or step: the user just says it in chat. A new task. |
| — | ADR-052 amendment | "ADR-052 amendment is approved." | Security sign-off given 2026-11-26. Both ADR-052 files now read ACCEPTED. |

## D-3268-user-focus — per-run user instructions reach the prompt (owner, 2026-11-26)

- **Owner's words:** "Yes, approve (a)" — answering: approve the one engine edit so run instructions reach the prompt (#3268), or (b) withdraw.
- **Decision:** a non-empty `UserContext` (run instructions) is added to the AI prompt as a clearly delimited "User focus" block. The edit is made where prompts are built (`Tools/PromptContextHelper.cs` / `GenericAnalysisHandler`, plus any other prompt builder that receives `ToolExecutionContext`).
- **Repairs three paths at once:**
  - the Console chat "rerun with additional instructions", which today claims the instructions were used;
  - the REST execute `userContext`;
  - task 038's `instructions` argument.
- **ADR-039 check, Path C (compliant), recorded under CLAUDE.md §6.5:**
  - The rule "MUST NOT land new capability on the frozen node-graph engine" is not crossed. This repairs an input the REST contract and the chat already promise. It adds no node type, executor, graph, control flow or routing.
  - The rule "MUST NOT emit free-form LLM text untethered from a capability's prompt" holds: the block sits inside the capability's own prompt, marked as the user's focus, and cannot replace the action's system prompt or its output schema.
  - The owner approved the edit in any case.
- **Constraints:**
  - The text is never logged.
  - It is delimited and labelled as user-supplied: it may narrow emphasis, but cannot override the action's instructions or output format.
  - Absent or blank: the prompt is byte-identical to today's.

## D-037-results — a Copilot playbook run saves its results to the caller's analysis record (owner, 2026-11-27)

- **Owner's words:** "for 037 yes option 1a" — answering: should Copilot runs save to an analysis record like the Console (option 1a), or should the widget show status only?
- **Decision:** option 1 with sub-option (a) from `notes/037-decisions.md` §2.
  - The Copilot start creates the caller's `sprk_analysis` (the Analysis Workspace's create core and its authorization: Create on `sprk_analysis` + `analysis.attach`). The detached runner then drains the existing wrapper `IAnalysisOrchestrationService.ExecutePlaybookAsync` with that id, not the engine entry, so the output lands in `sprk_workingdocument` as it does for the Workspace.
  - The run record links to the analysis through its existing regarding columns (no new column).
  - The results tool reads the analysis as the caller, so the caller gets the uniform not-found when they can't read it.
- **Accepted costs (stated to the owner 2026-11-26):**
  - it reopens 030's start path;
  - one more refusal at start, when the caller can't create analysis records;
  - Copilot runs also do the wrapper's canvas-to-node sync, as the Workspace does.
- **ADR-039:** the engine is untouched. `PlaybookParameterPolicy` is the HTTP parameter policy (unified-access-control-r2 task 164), not the frozen engine, so editing it is Path C (comply). A uac-r2 review of the policy change is needed.
- **Security condition set by the main session:** the analysis id that the wrapper writes to app-only must be bound by the server at start. A caller-supplied analysis id must never choose which analysis gets overwritten: it is refused, or it needs the caller's Write on that record.
- **Related:** #3285 (pre-existing: a wrapper run with no analysis id writes to a random id).

## D-037-engine — option B: the engine saves a Copilot run's results to its analysis (owner, 2026-11-27)

- **Owner's words:** "Yes, option B". This answers: "May 037 make the small engine change (option B) so Copilot runs save their results?"
- **Decision:** option B from `notes/037-decisions.md` §3. It refines D-037-results (the runner keeps draining the engine and does not drain the wrapper):
  - `PlaybookRunRequest` gets a typed, server-set `AnalysisId`. Only the Copilot start sets it, from the `sprk_analysis` it created as the caller (`AnalysisEndpoints.StartAnalysisAsCallerAsync`).
  - The engine's legacy mode forwards the id to the wrapper. This fixes #3285 at the root for callers that set it.
  - A node run that has an `AnalysisId` saves its Deliver Output through `AnalysisResultPersistence` at the end of the run. The save code moves out of the wrapper's node branch into one shared method; it is not copied.
  - The run links to the analysis through the run record's existing regarding columns (no new column).
- **ADR-039:** the owner approves this engine edit, as with D-3268-user-focus. It is recorded as Path C (comply):
  - It adds no node type, executor, graph, control flow or routing.
  - It forwards one field and calls the existing persistence that the Workspace path already uses.
  - Output stays the capability's own Deliver Output.
- **Security:** no HTTP route binds `PlaybookRunRequest` from a body (it is built server-side in `PlaybookRunEndpoints.cs:186` and `AnalysisEndpoints.cs:714`). So the id cannot be chosen by a caller, and `PlaybookParameterPolicy` does not change. A test pins this.

## D-037-cleanup — a Copilot start that does not start deletes its empty analysis (owner, 2026-11-27)

- **Owner's words:** "Yes delete". This answers: "If a Copilot run fails to start, should its empty analysis record be deleted (recommended), or left?"
- **Decision:** when `DetachedPlaybookRunner.StartReservedAsync` has created the caller's `sprk_analysis` but does not return `Started` (the run was not accepted within the budget, the run table has no run, the caller cancelled, or anything faults after the create), it deletes that analysis in the same request.
  - The delete runs **as the caller** (`IDataverseUserClient.DeleteAsync`), never app-only (ADR-028 A10). If the caller's delete is refused or faults, the record is left, and an outcome code and the error type are logged; the tool's answer does not change.
  - The run is abandoned before the delete, so no save follows it. If a late save still reaches a deleted analysis, it fails and is logged (D-037-engine: a failed save never changes the run).
  - Only the analysis this request created is ever deleted; no id from a caller or a record is used.
- **Task:** 037d (follow-up to 037).

## D-3181 — attaching an event to a record needs Read and AppendTo (owner, 2026-11-27)

- **Owner's words:** "Yes option b". This answers: "#3181: should creating a task need only the matter's Read permission (a), or Read plus AppendTo (b, recommended)?"
- **Decision:** fix option (b) from #3181. `event.attach_regarding` in `OperationAccessPolicy` requires `Read | AppendTo` on the regarding record, for REST `POST /api/v1/events` and for `IEventsCapability.CreateAsync` (so for `spaarke_create_task`).
  - A caller with AppendTo but no Read on the regarding record is refused with the same 403 rights answer as today, so the copied name and number can no longer reach them.
  - This changes who may attach: a caller with AppendTo and no Read on that record could attach before and cannot now. It is intended.
  - The event create keeps reading the regarding record's name and number app-only after the check, as today; the check now proves the caller may read them.
- **Task:** lands with 036, before the create tool is exposed (the issue's "must land before" condition). A regression test `Issue3181_*` pins it. uac-r2 reviews the policy change.
