# Current Task State — spaarkeai-assistant-enhancements-r4

> **Last Updated**: 2026-08-17 (by task-execute — 033 COMPLETE; **E1 + E3 DONE**; CHECKPOINT for fresh session)
> **Recovery**: Read "Quick Recovery" + "FRESH SESSION START HERE" first. Tracks the **active task only**; history lives in `tasks/TASK-INDEX.md` + per-task `.poml`.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Project** | spaarkeai-assistant-enhancements-r4 — **EXECUTION STARTED 2026-08-15** (owner ran `/task-execute` + "parallel + autonomous where safe") |
| **Task** | ✅ **021a COMPLETE** (BFF grounded proposer, FR-04). ✅ **12 of 17 done**. **NEXT: 021b** (client typed two-kind chip render, sonnet/high). |
| **Status** | 021a done + committed locally: retired ungrounded generator; AssistantSuggestionService.SuggestForConversationAsync + typed SuggestedFollowup + ParseFollowups; FilterByContextTypes union pre-filter; two-kind action JSON; typed `suggestions` SSE event. 27/27 tests; publish 44.97 MB (Δ+0.01); CVE clean; Step 9.5 CLEAN (independent ADR-039 review, no Critical, W1 fixed). **NOT pushed; PR HELD.** |
| **Next Action** | **021b** — render the typed two-kind chip family. **Wire contract LOCKED in [`notes/021-grounded-suggestions-design-delta.md` §9a](notes/021-grounded-suggestions-design-delta.md)** (capability→Click-path dispatch by targetBindingId; question→re-send label; action→special-route by actionId; one chip family, arrow=capability/action, no-arrow=question). Client files: SprkChatSuggestions.tsx + SprkChat.tsx handleSuggestionSelect. `/conflict-check` (SprkChat hot-path, compose-r5/r6). |

---

## 021a DESIGN LOCKED (2026-08-17, opus/high FULL) — de-risked by code study

**Conflict-check**: soft pass — hot-path BFF touched; NO open PR overlaps ChatEndpoints.cs / AssistantSuggestionService.cs / suggest-followups.action.json / SSE models (PR #508 = Events/SmartTodo client; dependabot = csproj; compose-r5/r6 no open PR). PR is HELD anyway.

**Key code facts (traced):**
- `## Input` renders the operand `JsonElement` VERBATIM (`PromptInputSection.Render`) — the action's `input` schema is DESCRIPTIVE, not a runtime template. ⇒ adding an optional `conversationTail` to the CONVERSATIONAL operand does NOT touch the proactive `/suggest` operand (unchanged). No breakage.
- `AssistantSuggestionService` = the reference grounded proposer (candidate menu via `IConsumerRoutingService.ListTextProjectableBindingsAsync` + `FilterByContextType`; `BuildInput`; `ParseSuggestions` drops off-catalog ids). Registered `AddScoped` → injectable into `SendMessageAsync` via `[FromServices]`.
- `WidgetContextTypeResolver.ResolveOpenTabContextTypes(liveTabs)` already gives the open-tab context-type UNION (R3 tool-economy). `SendMessageAsync` has `liveTabs` + `request.ActiveContext?.{ContextType,TabId}` + `request.Message` + `fullResponse` + `tenantId` + `session` in scope.
- After-response emit (ChatEndpoints ~963-1022): 3 hidden skips = (1) `EmitMissingContextChipsIfNeededAsync` keyword hijack (mutually-exclusive `[action:*]` chips), (2) `>=150`-char skip, (3) 2s timeout. Untyped event `ChatSseSuggestionsData(string[])` at 3271 (generator) + 3417 (action chips). No outputschema mirror for suggest-followups. `Truncate` only used by the generator.

**LOCKED DECISIONS:**
1. **Typed SSE**: keep event type string `"suggestions"`, change `ChatSseSuggestionsData` payload `string[]` → `ChatSseFollowupItem[]` where `ChatSseFollowupItem(Kind, Label, TargetBindingId?, ActionId?)`. THREE kinds: `capability` (targetBindingId→Click-path dispatch), `question` (label→re-enter loop), `action` (actionId∈{upload,search,select}→client special-route; the `[action:*]` chips RE-TYPED, "as-is" behaviorally, NOT folded into the grounded menu = still deferred). Fully retires the untyped `string[]` event (AC met). `reason` stays dev-only, NOT on the wire.
2. **Generalize service**: add `SuggestedFollowupKind{Capability,Question}` + `SuggestedFollowup(Kind,TargetBindingId?,Label,Reason?)`; add `SuggestForConversationAsync(sessionId,tenantId,userMessage,assistantResponse,activeContextType,activeTabId,openTabContextTypes,ct)` → typed list. Candidate menu = union-filter over openTabContextTypes (+active type). Reuse `BuildActiveTabAsync`. Operand adds `conversationTail`. Keep `SuggestAsync` (proactive) returning `SuggestedChip` (map capability-kind only; questions dropped → proactive contract stable).
3. **Two-kind output schema** (OpenAI-strict-safe): item = `{kind(enum capability|question), targetBindingId(string, ""=question), label, reason}` ALL present/required. `ParseFollowups` infers kind if absent (targetBindingId non-blank→capability). Capability off-catalog id → dropped (existing guard preserved). Update systemPrompt (conversational moment + 2-kind split + label grammar: imperative=capability, interrogative=question) + example.
4. **ConsumerRoutingService**: add static `FilterByContextTypes(candidates, IEnumerable<string>)` (union; mirrors `FilterByContextType`).
5. **ChatEndpoints cadence**: remove `GenerateAndEmitSuggestionsAsync`+`SuggestionsTimeoutMs`+`>=150` skip. Refactor `EmitMissingContextChipsIfNeededAsync`→`BuildMissingContextActionChips` (returns items, doesn't write). ONE predictable pass: build action items (missing-context) + run conversational proposer (best-effort, timeout ~4s) → MERGE (actions→capability→question) → emit ONE typed `"suggestions"` event, or NONE if empty (absence = meaningful). No mutual-exclusion (fixes hijack), no length skip.

**Files**: (1) AssistantSuggestionService.cs (2) ConsumerRoutingService.cs (3) infra/dataverse/actions/suggest-followups.action.json (4) ChatEndpoints.cs + tests in tests/unit + tests/integration/seam. **Coordinate typed SSE shape with 021b.**


---

## 🟢 FRESH SESSION START HERE (2026-08-17 checkpoint)

**Git**: branch `work/spaarkeai-assistant-enhancements-r4`, HEAD **`9aabf8933`** (+ a 033 commit landing now). **All committed locally, 0 pushed** (owner holds the push + PR). Working tree clean. Runtime **.NET 10** (SDK ≥10.0.100; never deploy BFF from a net8 tree).

**Done (11/17)**: E1 spine **010→011→012→013 ✅** (the P1 grounded-recommend core: advisory `list-tasks` + `AdvisoryCapabilityRunner` nested turn + dispatch routing + eval). E3 loop **030→031→032→033 ✅** (Preference type + feedback→memory capture + governed injection-safe preference-producer + eval). Plus 001, 020, 022.

**Remaining (5 tasks), in recommended order:**
1. ✅ **021a DONE** (2026-08-17). **→ 021b** (client, sonnet/high) NEXT — render the TYPED two-kind chips. Wire contract LOCKED in [`notes/021-grounded-suggestions-design-delta.md` §9a](notes/021-grounded-suggestions-design-delta.md): `suggestions` SSE `data.suggestions` is now `FollowupItem[]` (kind=capability|question|action). capability→Click-path dispatch(targetBindingId); question→re-send label; action→special-route(actionId: upload/search/select). ONE chip family, arrow=capability/action, no-arrow=question. Retire the old `string[]` + `[action:*]`-prefix parse. `/conflict-check` (SprkChat, compose-r5/r6). **Merge-order gate: 021b must deploy WITH 021a (task 080) — not before.**
2. **023** (client, FR-06) — follow-on cards (Briefing/SmartToDo), open-tab-gated. Dep 022✅+012✅. NOTE: 012 deferred `chipTransitions` to here.
3. **024** (eval, FR-10) — E2 eval cases. Dep 021b+023.
4. **040** (D9, FR-11) — **needs a live-DOM `--chrome` session → NOT autonomous** (owner involvement). Confirm D9 still repros after the merged partial fix first.
5. **080** (deploy, owner-gated) — **MUST create the `sprk_groundedtoolallowlist` column on `sprk_analysisaction` + re-seed** before the BFF spine is deployable (010/012 depend on it). Deploy BFF + `sprk_spaarkeai` together. Also: 022 hardcoded live spaarkedev1 layout GUIDs (multi-env needs per-env update).
6. **090** — wrap-up + `/test-diet` gate.

**Standing context**: publish stable ~43.67 MB compressed (≤60); CVE clean; commit msgs end `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`; ADR-042 hard-governance DEFERRED to #616 (trustLevel inert). Two review catches this session worth remembering: (a) 031's inferred preference was mistakenly Confidence 1.0 → fixed to 0.5 (dormant below the 0.7 recall gate); (b) 011 left 2 invariant tests red (AgentToolFilterContext 6→7) → fixed in 032.

### 012 working state (2026-08-16, opus/high FULL) — analysis complete, machinery pending
**Conflict-check**: PASS (silent) — no master commits since divergence touch the Chat/catalog files; no open PR overlaps `SessionDispatchOrchestrator.cs`/`list-tasks.action.json`/`sprk_playbookconsumer-rows.json`. (PR #508 = Events/SmartTodo shared components, not my files.)

**Key code findings (traced 2026-08-16):**
- **`AnalysisActionService` `$select` materializes `sprk_groundedtoolallowlist` (010) but NOT `sprk_outputdeterminism`** — OutputDeterminism is NOT on the `AnalysisAction`/`AnalysisAction` model, column may not exist. ⇒ **Route the advisory nested turn on `action.GroundedToolAllowList.Count > 0`** (the materialized deterministic signal, already visible to `DispatchAsync` via `_scopeResolver.GetActionAsync`), NOT on OutputDeterminism. Directional-mode adaptation of POML AC-100 ("thread OutputDeterminism"): threading it would need a full 010-style column add for a field only used as a routing bool the allow-list already provides (§11 no unnecessary surface). Still author `outputDeterminism:"advisory"` in the JSON mirror + advisory grounding rules in the prompt (like `agreement-review`).
- **Advisory action JSON pattern** (ref `agreement-review.action.json`, `nda-standard-summary.action.json`): `modelTier:"Reasoning"` (→sprk_modeltier 100000002), `outputDeterminism:"advisory"` (intended home sprk_outputdeterminism, prompt is runtime enforcement until column lands), `temperature:0.3`, ADVISORY GROUNDING RULES block in systemPrompt, closed outputSchema.
- **Tools**: `spaarke.grid_overview` (arg `configId`; **My Tasks configId = `ac05e4f1-8d85-f111-8075-7c1e5268570d`**; injects `today` server-side, OBO); `spaarke.daily_briefing_overview` (no args, OBO). Both chat-context-only (`InvocationContextKind.Chat`), return grounded data + record-id citations.
- **list-tasks binding** (sprk_playbookconsumer-rows.json ~L281): consumerType/actionCode=`list-tasks`, disposition=`100000007` (SurfaceLaunch), risk=`100000000` (None). Live rows: action `57651aad-8e85-f111-8075-7c1e5268570d`, binding `5b1870b9-8e85-f111-8075-7c1e5268570d`.
- **011 primitive confirmed intact**: `AgentToolFilterContext.AdvisoryToolAllowList` (null-inert) + `PreFilter` drop-all-capability/keep-allow-list branch. Allow-list entries = raw `sprk_toolid`s.

**THE crux — RESOLVED by the architecture agent (blueprint captured below).** ADR-040 (`ProgressiveRenderGuard.EnsureStored`, `SessionDispatchOrchestrator.cs:695-698`) mechanically forbids rendering advisory output before it's stored → **no client streaming**; the nested turn's narration MUST be drained + assembled into ONE `JsonElement` regardless. So designs N and A′ **converge at the store boundary**; the only difference is HOW the two tools execute (nested LLM function-calling loop vs. in-code calls).

### 012 — CATALOG HALF DONE ✅ (committed WIP); MACHINERY HALF pending (fresh-context recommended)
**Catalog half (DONE, validated):**
- `infra/dataverse/actions/list-tasks.action.json` — UPGRADED ack-only → advisory: `modelTier:Reasoning`, `outputDeterminism:advisory`, `temperature:0.3`, `groundedToolAllowList:["spaarke.grid_overview","spaarke.daily_briefing_overview"]`, rewritten advisory systemPrompt (call both grounded tools, narrate cited summary + prioritized recommendation, no fabrication, no identity-ask), `acknowledgement` outputSchema widened 200→4000. JSON well-formed + OpenAI-subset compliant (object-level required, additionalProperties:false, no property-level required). actionType stays 0.
- `infra/dataverse/outputschemas/list-tasks.schema.json` — synced (maxLength 4000 + advisory description).
- Input mirror UNCHANGED (the `documentText` structured operand still needed so dispatch skips the file-operand hard-stop).
- **Binding row: NO change needed** — routing keys off the Action's non-empty allow-list; disposition already SurfaceLaunch (100000007) + risk None. **chipTransitions DEFERRED to task 023** (Briefing/SmartToDo *launch bindings* don't exist yet — only `daily-briefing-narrate` narration cap; authoring dead `target_binding_id`s = the P2 anti-pattern; AC-99 "can key off this capability" met by 012 delivering the capability, chip wiring lands with its 023 consumer — mirrors 011→012).

**4 LOCKED DECISIONS (rationale in the action.json $comments + design note):**
1. **Route on `action.GroundedToolAllowList.Count > 0`** (materialized signal via `_scopeResolver.GetActionAsync`), NOT OutputDeterminism (unmaterialized; adding it = 2nd task-080-deferred column w/ no independent effect). Directional adaptation of AC-100.
2. **Design N** (nested bounded agent turn via advisory `CreateAgentAsync` overload → sets `AdvisoryToolAllowList` → fires 011 PreFilter). Makes 011 live; satisfies AC-100/101. A′ = fallback only if Step 9.5 rejects the nested loop.
3. **Output shape** = runner drains nested-turn narration → `{"acknowledgement":"<narration>"}` (client already renders it; P1 fix = ack now rich). Nested turn's outputSchema is NOT decode-enforced (free function-calling turn); schema documents stored payload.
4. **chipTransitions → task 023**.

**MACHINERY BLUEPRINT (from Explore agent, code-grounded — build next):**
- **(1) `AdvisoryCapabilityRunner`** (new, `Services/Ai/Chat/`): given resolved advisory `AnalysisAction` (SystemPrompt, GroundedToolAllowList, ModelTier, Temperature) + session ctx → build nested `ISprkChatAgent` via advisory `CreateAgentAsync` overload → `agent.SendMessageAsync(msg, [], ct)`, DRAIN accumulating `ChatResponseUpdate.Text` (mirror `SprkChatAgent.cs:142-152`) → assemble `JsonElement {acknowledgement}` → return as the dispatch `output`.
- **(2) `SprkChatAgentFactory.CreateAgentAsync`** (`SprkChatAgentFactory.cs:366`, 17 params; single `AgentToolFilterContext` prod site **:915**): add advisory overload/params — thread `advisoryToolAllowList` into the filterContext@915 + override `context.SystemPrompt` with the Action's prompt. PreFilter then drops all Binding/Refusal tools (`AgentToolProjection.cs:171-183`), keeps only allow-list.
- **(3) `SessionDispatchOrchestrator.DispatchAsync`** Prompted `else` branch: replace the `_actionRunner.RunAsync(effectiveAction,...)` at **:628-630** with `if (action.GroundedToolAllowList.Count>0) output = await _advisoryRunner.RunAsync(...) else _actionRunner.RunAsync(...)`. Rest of tail (RouteAsync :678, terminal :709, SurfaceLaunch opens Tasks) UNCHANGED. boundInputs/operand resolution stays (structured `documentText` path succeeds w/o files); runner can take the user msg from the operand/args (default fallback).
- **OBO check (do FIRST)**: nested dispatch runs INLINE within the top-level turn's request (BindingCapabilityTool→DispatchAsync same async ctx) → `IHttpContextAccessor`-backed OBO for the handler tools flows ambiently → highly likely fine; VERIFY the handler `IDataverseUserClient` resolves OBO from ambient accessor (not an explicit token param) at first machinery step.
- **Client-render check**: confirm ConversationPane renders the surface_launch terminal chunk's `acknowledgement`/`result` text as a bubble (if not, minimal client change — but likely E2/023 owns richer rendering).
- **DI**: register `AdvisoryCapabilityRunner`; inject into `SessionDispatchOrchestrator` (optional param + Null-Object per ADR-032 pattern the orchestrator already uses for optional deps).
- **Tests**: unit (runner assembles JsonElement; routing selects runner on non-empty allow-list, ActionRunner otherwise) + seam `tests/integration/seam/**` (advisory Action → nested runner; non-advisory → linear; nested turn mounts ONLY allow-list, NO capability/refusal tool survives = no-second-decider). If Step 9.5 rejects nested loop → A′ fallback.
- **Then**: dotnet build + publish (COMPRESSED ≤60MB, baseline 44.96) + CVE + Step 9.5 gates + TASK-INDEX.

**Blueprint agent** (background, done): full file:line anchors in its result; `BindingDisposition` enum values, all `AgentToolFilterContext` construction sites (prod :915 + the test sites), `ActionRunner.RunAsync` sig (`LinearConsumers/ActionRunner.cs:108`), `IOutputRouter.RouteAsync` (`OutputRouter.cs:66`).

### 011 completion note (2026-08-16) — Option A projection primitive (opus/xhigh, FULL)
Re-scoped per owner-approved Option A. Original framing ("narrow the TOP-LEVEL PreFilter when advisory") was ADR-039-incompatible (top-level turn selects no Action yet → narrowing there = forbidden second decider). Option A = a NESTED advisory turn; **011 shipped the deterministic PROJECTION PRIMITIVE for it**, the machinery moved to 012 (§3a seam refinement — mirrors 010=data → 011=primitive → 012=runner+consumer). Shipped in `AgentToolProjection.cs`: (1) `AgentToolFilterContext.AdvisoryToolAllowList` optional null-inert structural fact (mirrors `OpenTabContextTypes` → every construction site byte-identical); (2) `PreFilter` narrowing — non-null ⇒ DROP all `BindingCapabilityTool` + `RefusalCapabilityTool` (nested turn structurally cannot dispatch a 2nd capability) + keep ONLY grounded handler tools whose `SanitiseToolName`-normalized name ∈ the allow-list (matches task-010 `sprk_toolid` entries); null=inert byte-identical; empty non-null=fail-closed zero; (3) `AdvisoryAllowListContains` helper. **5 new unit tests** (mounts-only-allow-list · drops-all-capability+refusal/no-second-decider · case+sanitisation robustness · null-inert · empty-fail-closed); **44/44** AgentTurnLoopContractTests pass; BFF build 0 errors; **publish 44.96 MB compressed (Δ0.00, ≤60 MB)**; CVE clean. **Step 9.5 CLEAN** (adr-check 0 violations — ADR-039/013/§10/§11; field grep-verifiable inert until the 012 runner sets it; code-review PASS, 0 blocking). Design note + 012 POML updated to record the runner/routing move. ⚠️ **The 011 primitive is inert until 012 wires the setter** (exactly as 010's field was inert until 011).

### 030 completion note (2026-08-16)
`MemoryFactType.Preference = 4` added + `MemoryWriteHandler.SupportedFactTypes["preference"]` wire-map entry + a `**Preferences**` section in `MemoryItemStore.RenderFragment` (shared by record + user fragments → recalls in both). **Deliberately did NOT expose `preference` in the LLM-facing `memory.write` schema** — preferences are authored by the GOVERNED E3 pipeline (031) + narrow-allow-list producer (032), not freely by the model (E3 governance intent). ADR-042 deferred hard-governance untouched (`trustLevel` inert; escalation trigger did not fire). 5 new tests (2 render + 3 wire-map) · 37/37 targeted pass · build clean (no CS8509) · **publish 44.96 MB, Δ0** · CVE clean. Unblocks 031/032.

### 020 completion note (2026-08-16) — accurate-scoping resolution
FR-05's identity-ask fix (P2) applies only to tools whose results are scoped BY the caller's identity: `spaarke.grid_overview` (My-Tasks) + `spaarke.daily_briefing_overview` (my portfolio). **Both already carried the OBO-identity assertion with byte-parity — shipped in R3** (`CatalogToolDescriptionParityContractTests` guards handler⇄seed). So the flow-critical DoD was already met; I added a **contract regression guard** (`tests/integration/contract/Catalog/UserScopedToolOboIdentityContractTests.cs`, 2 cases) locking the assertion's presence (byte-parity alone wouldn't catch removing it from BOTH mirrors). **Deliberately did NOT spray** the assertion onto the other 11 `dataverse-user-context` rows — the 6 generic `dataverse.*` tools query named tables (not "my records") + are GA-MCP-frozen; the `email.*`/`memory.write` tools act as the user but aren't identity-scoped (§11 accuracy, no spray). 8/8 targeted tests pass · build clean · CVE clean · **no production/dependency change → publish unchanged (44.96 MB, Δ0)**. ⚠️ **Owner: if you want the assertion on ALL user-scoped tools regardless, say so — I scoped to where it's semantically correct.**

### 🔴 011 escalation — ADR-039 conflict (2026-08-16, opus/xhigh, NO code written)
**Finding (traced end-to-end):** 011's scoped approach — "add an allow-list narrowing predicate to `AgentToolProjection.PreFilter`, applied *when the capability is advisory*" — cannot be implemented as written without a **forbidden ADR-039 second decider**. Evidence:
- `AgentToolProjection.PreFilter` / `ResolveToolsAsync` run in **exactly ONE place**: `SprkChatAgentFactory.CreateAgentAsync` (the single top-level Text-path turn). There is **no nested per-capability tool-projecting turn**.
- At that top-level turn **no single Action is selected yet** (the ONE probabilistic decider = the model choosing). Narrowing the projection to *one* Action's `GroundedToolAllowList` there requires knowing "the user wants the task-agenda capability" **before** the model decides = intent detection = ADR-039 MUST-NOT.
- `advisory`/`output_determinism` is consumed **only on the linear path** (`ActionRunner`), never in the chat path. `list-tasks` = `actionType:0` linear AiAnalysis, `allowstools=false`, single completion → ack + `surface_launch`. Dispatch (`BindingCapabilityTool → SessionDispatchOrchestrator → ActionRunner`/coded-workflow) **never re-projects tools**. `grid_overview`/`daily_briefing_overview` are handler tools mounted only in the top-level turn.
- ⇒ FR-02's "only the allow-listed tools mount **for that capability's turn**" presupposes a **per-capability (nested) tool-calling turn that does not exist**.

**Resolution options put to owner (§6.5):**
- **A — Nested advisory agent turn (recommended):** dispatching an advisory *tool-calling* Action runs its own bounded turn projecting ONLY its allow-list; 011's PreFilter narrowing becomes deterministic (Action already selected by binding id → no second decider) via an optional `AdvisoryToolAllowList` structural fact. Requires NEW nested-turn dispatch machinery (bigger than 011 as written) + re-scopes 012 (list-tasks stops being `actionType:0`). Keeps 010's field + makes 011's edit coherent.
- **B — Reuse existing per-playbook capability gate (pivot-to-comply, §11):** mount the two grounded tools in the top-level turn gated by the session's **resolved-playbook** `sprk_requiredcapability` (existing `IsCapabilityGateSatisfied`, deterministic, keyed off session fact not utterance). Needs NO new per-Action allow-list field/pre-filter — i.e. **partially invalidates shipped 010**.
- **C — Coded workflow:** list-tasks-advisory becomes an `ICodedWorkflow` that calls the two handlers in C# + one advisory completion; allow-list honored in code, NOT via `AgentToolProjection.PreFilter` (011's edit site is wrong).

**Owner action required before 011/012 resume.** Recommendation: **A** (best matches spec language + preserves 010) — but confirm the blast radius / advisory-execution reshape is acceptable, or pick B/C.

### Wave-1 completion notes (2026-08-15/16)
- **010** (FR-03): `sprk_groundedtoolallowlist` field as catalog DATA (DTO + 3 `$select` + `AnalysisAction.GroundedToolAllowList` model + 3 materialize sites + `ParseGroundedToolAllowList` fail-closed helper). 12 unit tests PASS · BFF build 0 errors · CVE clean · **Step 9.5 gates CLEAN** (ADR-039/013/038 compliant; field grep-verified inert = zero consumers) · **publish 44.96 MB compressed = baseline, delta +0.00, ≤60 MB** (§10: measure COMPRESSED zip, not the 137 MB raw folder). Agent-path MOUNTING deferred to 011.
- **001** (FR-12/10): behavior-gap register confirmed + `tests/integration/contract/Eval/assistant-r4-eval-cases.json` (template `AR4-001`) + convention docs. `.cs` harness deferred to the FR-01 task. Reused R1 net-new-family precedent.
- **022** (FR-06): 2 `workspace-tab` launch entries (daily-briefing, smart-todo) + 2 tests. ⚠️ **FLAG for owner/task-080**: Briefing + Smart To Do are `sprk_workspacelayout` rows opened via the generic `'workspace'` widget type keyed by a **per-environment auto-generated `layoutId` GUID** — the agent hardcoded live spaarkedev1 GUIDs (mirrors existing `ENTITY_VIEW_CONFIG_IDS`). Multi-env deploy needs these GUIDs updated per environment.
- **OWED before any PR**: `/conflict-check` on Services/Ai (010) + shared lib surfaceLaunchRegistry.ts (022) — compose-r5/r6 + assistant-r3 overlap. **Task 080**: create the `sprk_groundedtoolallowlist` column on `sprk_analysisaction`.

### Parallelism decision (why only 001+022 are background agents)
The three BFF wave-1 tasks (010/020/030) share the `Services/Ai` spine AND would run concurrent `dotnet build` in ONE worktree (bin/obj corruption) → cannot safely parallelize as agents here. Only the two non-BFF `parallel-safe:true` tasks (001 docs/eval, 022 client/npm) run as autonomous background agents; the BFF spine runs sequentially in the main session (Opus 4.8 = correct tier). 040 needs live-DOM → not autonomous.

### 010/011 boundary (LOAD-BEARING — don't re-litigate)
- **010** = the field as catalog **DATA**: `ActionEntity.GroundedToolAllowList` DTO (`sprk_groundedtoolallowlist`, multiline JSON array of grounded-tool ids), added to the 3 `$select` sites, `AnalysisAction.GroundedToolAllowList` model prop (parsed `IReadOnlyList<string>`; **empty = opt-out/ack-tier**), materialized at the 3 constructor sites via `ParseGroundedToolAllowList` (fail-closed → empty), + unit tests. Mirrors `sprk_allowsknowledge` read/materialize shape. **No agent-path edits.**
- **011** = agent-path **consumption**: thread the resolved allow-list into `AgentToolFilterContext` + the deterministic `AgentToolProjection.PreFilter` narrowing predicate (the ADR-039 boundary; has an escalation trigger). 010's "mounts zero/exactly" criteria are DATA-verified in 010, BEHAVIOR-verified in 011.

### Git / baseline state (all clean)
- Branch `work/spaarkeai-assistant-enhancements-r4` @ **`7fbb9f5f9`** — **0 uncommitted, 0 unpushed, 0 behind master**.
- **Runtime = .NET 10** (`global.json` 10.0.100; BFF csproj `net10.0`; `dotnet build -c Release` verified clean, 0 errors). BFF builds/deploys need SDK ≥10.0.100; **never deploy the BFF from a net8 tree**. If `dotnet` can't find the SDK → stale shell, open a fresh terminal (not a code problem).

### Critical Context (for continuation)
- **No code written yet** — only planning artifacts + 17 task POMLs. First real work is task 001.
- Plan was **verified aligned with master** after the BFF + code-quality review merged (2026-08-15): all 20 file anchors + key symbols intact (`sprk_allowsknowledge`, `MemoryFactType` 4-members-no-`Preference`, `spaarke.grid_overview`/`spaarke.daily_briefing_overview`, `list-tasks` registry entry). `output_determinism: advisory` confirmed authorable catalog data (actions JSON; precedent `agreement-review`). The review touched only `SprkChat/hooks/useChatFileAttachment.ts` (security tweak) — no R4-target contract reshaped.
- **Publish size**: re-baseline fresh under net10 (the ~49.63 MB figure was net8) on every BFF task + task 080.

---

## Full State (Detailed)

### What's done (this initialization arc)
1. `/design-to-spec` → `spec.md` (12 FR / 9 NFR / 3 ADR tensions), both open questions resolved with the owner (2026-08-13).
2. `/project-pipeline` (INITIALIZE-ONLY) → README, plan.md, CLAUDE.md, current-task.md, `notes/behavior-gap-register.md`, **17 task POMLs + TASK-INDEX.md** (validator PASS: 0 errors). Registered R4 in `projects/INDEX.md`.
3. net10 readiness: merged net10 master; BFF build clean; net10 notes baked into CLAUDE.md/plan/TASK-INDEX/current-task.
4. Post-review sync: merged master (BFF + code-quality review); alignment verified; fixed seed-script path `scripts/dataverse/Seed-PlaybookConsumers.ps1`.

### Owner decisions (2026-08-13) — binding for execution
- Build approach = **reuse the existing single decider** (advisory mode + pre-filter bounded tools; **no new executor**).
- Preference steering = **narrow closed allow-list → pre-turn tool hints only** (never grants a capability or alters a fact).
- Agenda surfaces = **Tasks only + inline grounded summary + Briefing/Smart-To-Do follow-on cards if not already open**.
- Operator promotion queue = **out of system scope** (CX/product-owner exercise).
- E3 memory = **owned entirely in R4** (redesign-r2 closed).
- Advisory tier = **ADR-016 Reasoning tier, temp ~0.2–0.3**.

### Execution order (from TASK-INDEX)
- **Foundation**: 001 (parallel-safe). 
- **E1 spine** (sequential, opus): 010 → 011(xhigh) → 012 → 013. The P1 value-proving DoD.
- **Wave-1 independent** (alongside E1): 020 (OBO wording), 022 (registry entries), 030 (Preference type), 040 (D9).
- **E2** (sequential SprkChat/ConversationPane spine): 021 → 023 → 024. Deps 012, 022.
- **E3**: 031, 032(xhigh) → 033. Dep 030.
- **Deploy/Wrap**: 080 → 090.

### Coordination before any PR
- `/conflict-check` before every BFF / `ConversationPane` / `SprkChat` PR. Live overlap: **compose-r5/r6** + **assistant-r3** (the review just touched an adjacent SprkChat hook). Memory files have no live contender (redesign-r2 closed).
- All BFF-touching tasks: measure publish ≤60 MB (re-baseline under net10) + no new HIGH CVE.

### Files Modified This Session
- `projects/spaarkeai-assistant-enhancements-r4/current-task.md` — this handoff.
- (Earlier this arc, all committed + pushed: spec.md, README, plan.md, CLAUDE.md, notes/behavior-gap-register.md, 17 task POMLs, TASK-INDEX.md, projects/INDEX.md.)

### Decisions (this session)
- INDEX.md merge conflicts resolved keeping R4 + master's new/updated rows (dotnet-10-upgrade-r1 now ✅ COMPLETE; code-quality-and-assurance-r3 added).
- Seed-script path corrected to `scripts/dataverse/Seed-PlaybookConsumers.ps1`.

---

## To resume in the fresh session
Say **"work on task 001"** (or "continue") → `task-execute` loads CLAUDE.md + the task POML + ADRs and begins. Or "where was I?" → re-read this file.
