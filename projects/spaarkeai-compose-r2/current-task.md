# Current Task State

> **Auto-updated by task-execute and context-handoff skills**
> **Last Updated**: 2026-07-09 (by context-handoff — PRE-COMPACT; working tree CLEAN, all committed, no agents running; branch 1 ahead of origin/master = tracking commit `17bdc8d09`)
> **Protocol**: [Context Recovery](../../docs/procedures/context-recovery.md)

---

## 🌙 AUTONOMOUS OVERNIGHT CONTINUATION PLAN (owner asleep 2026-07-09 — keep progressing)

**Mandate**: work the Phase-9 E2E remediation queue autonomously. Source of truth = [notes/e2e-gap-register.md](../notes/e2e-gap-register.md). Definition-of-done = the binding rule in project [CLAUDE.md](../CLAUDE.md) "E2E Definition-of-Done" (through-the-wire WebApplicationFactory slice test — NON-WAIVABLE).

**The queue + sequence** (author each POML from its register cluster JUST BEFORE launching, using [tasks/100-getting-started-vertical-e2e.poml](tasks/100-getting-started-vertical-e2e.poml) as the exemplar template):
1. **100 getting-started vertical (Cluster 1)** — ✅ **DONE + COMMITTED** (`98a6d71d1`, 2026-07-10). 3/3 WebApplicationFactory slice tests pass; DEF-06/07 removed. Reconciled + verified.
2. **102 memory-resume (Cluster 4)** — ✅ **DONE + COMMITTED** (`c167376bf`, 2026-07-10). Linchpin 4.1 fixed (Load binds sessionId+matterId → same-session resume); gaps 4.2-4.5 closed; 5/5 reopen-restores-state slice tests pass; DEF-04/05 marked FIXED. Reconciled + verified.
3. **104 three-pane coordination (Cluster 5)** — ✅ **DONE + COMMITTED** (`614522b14`, 2026-07-10). All 6 compose_* flows typed on the ai-widgets bus (zero as-any on compose events); real receivers on owning panes (1&2 relocated, 3/4/6 built, 5 preserved); 7/7 real-bus cross-pane forcing test pass. Client-only. **Task 070 marked SUPERSEDED.** Reconciled + verified.
4. **101 AI-action activation code-side (Cluster 2)** — ✅◐ **DONE (E2E-pending on 047) + COMMITTED** (`9ab65aec4`, 2026-07-10). Gap 2.3 (2 overflow triggers via 046 seam) + the 084 dispatch forcing test (3/3, compose disposition ADMITTED not 422) landed green. **Gap 2.2 (toolbar real-bindingId activation) is genuinely deploy-gated**: bindingId is a per-env sprk_playbookconsumer row GUID minted at 047 seed time, not knowable at build time, no client consumer→GUID resolver, /dispatch is GUID-only. Agent escalated per §6.5 (correct — no hardcoded GUID, no new resolver). **Running 047 lights up all 5 toolbar actions.** Marked ✅◐ (not clean ✅) in TASK-INDEX.
5. **103 Word-shuttle wiring (Cluster 3)** — 🔄 **RUNNING** (agent, opus). Wires the built-but-unconnected Word shuttle: push-to-Word trigger (3.1) + EnsureSubscriptionAsync origin call (3.2) + pull caller (3.4) + reanchor mount + POLL check-changes path (3.5) + in-process poll-path forcing test. **Scoped OUT (named, not deferred): 3.3 webhook KV config = DEF-03/task 056 (owner/live-env; webhook-delivery leg ✅◐ E2E-pending on it); 3.6 = task 055 (separate 🔲 build task).** Last task in the serial tail.

**Per-task reconciliation procedure** (on each agent completion):
- Verify: build + tests green AND the forcing-function slice test PRESENT + PASSING. If the slice test is absent/red → task is NOT done: SendMessage the agent back to add/fix it, do not mark ✅.
- If green: mark ✅ in TASK-INDEX, remove any now-fixed DEF rows from defer-issues.md, run the consolidated build gate, `git commit` to the BRANCH, then author + launch the next task.
- Keep current-task.md updated each cycle.

**HARD LIMITS while owner sleeps (do NOT cross)**:
- ❌ NO merge to master, NO push to master, NO deploy to spaarkedev1 / any live env (047/017/056 are owner/live-env — QUEUE them).
- ❌ NO owner-only decisions: if a task hits a real contract divergence or a product-scope choice (like 034's undo scope, still OPEN) → STOP that task, document in its POML `<notes>` + current-task, move to the next INDEPENDENT task. Do NOT guess.
- ❌ NO false-greens: unit-green ≠ done; the slice test is the gate.
- ✅ OK: author POMLs, run remediation agents, fix wiring, write through-the-wire tests, commit to the branch (`work/spaarkeai-compose-r2`), keep the register/TASK-INDEX/current-task in sync.

**OWNER WAKE-UP QUEUE** (surface these when owner returns): live-env deploys (047 catalog → flips /healthz green, then 017/056/081); the 034 undo/replace scope decision (Full Path B vs replace-only — E-20 coded-workflow seam makes durable pure-undo feasible); the sandbox deploy of the getting-started vertical for the demo; any escalations logged during the night.

**HEAD**: `ad5d0aed3` (branch, merged with master — 0 behind / 20 ahead). **PHASE-9 E2E REMEDIATION COMPLETE** — all 5 clusters landed + verified through the wire: 100 create-on-save ✅ (3/3), 102 memory-resume ✅ (5/5, linchpin), 104 three-pane ✅ (7/7 real-bus, 070 superseded), 101 dispatch ✅◐-047-pending (2.3+084/3 green; 2.2 activation deploy-gated), 103 Word-shuttle ✅ (4/4 poll path; webhook-delivery leg ◐-056-pending).

**POST-103 SEQUENCE DONE (2026-07-10, owner-approved commit→merge-from-master→deploy):**
- ✅ **Merge-from-master** — pulled 25 master commits (daily-briefing/set-regarding/daily-update/cleanGuid-barrel) into the branch. **Clean auto-merge, zero conflicts** — none touched the compose surface. NOT a master merge (master ← branch is still deferred until UAT green).
- ✅ **Full consolidated gate GREEN**: BFF build 0 err · compose-surface band 468/468 · publish **45.24 MB compressed** (< 60 ceiling, −4.4 vs 49.63 baseline) · compose-components tsc clean + jest 79/79 · SpaarkeAi jest 410/410 (incl 104 forcing test) · CVE only pre-existing Kiota HIGH (no new).
- ✅ **Staged deliverables** (owner-facing): `notes/UAT-tier1-getting-started.md` (Tier-1 UAT script — getting-started vertical, needs only BFF+code-page deploy, no 047/056) + `notes/HANDOFF-to-core-shared-surface-heads-up.md` (the 3 shared-surface touchpoints for redesign-r2; recommends master-as-channel, core-first order, and asks core to absorb the StoredSession contract).
- Publish artifact ready at `deploy/api-publish/` (gitignored; Release, 45.24 MB compressed).

**🚀 DEPLOYED TO SANDBOX (2026-07-10, owner-directed) — HEAD `5a9b23f6a`:**
- ✅ **Re-merged master** (core memory-wave PR #620: tasks 050/051/052/053/057/062/064/017 + Context Binder convergence) into the branch (`e8508cbba`). Clean auto-merge; core built a NEW memory-items container alongside the existing session store (did NOT touch StoredSession/ChatSessionManager/ComposeEndpoints) → no collision. Critical bands 647/647 green post-merge.
- ⚠️ **Merge-defect caught + fixed** (`5a9b23f6a`): the master merge left a **duplicate `cleanGuid` barrel export** in `Spaarke.UI.Components/src/services/index.ts` (master `0c4c85d39` + our task-100 export, git auto-merged both textually). ts-jest tolerated it (my post-merge gate was green) but the **vite/esbuild production build rejected it** — surfaced during the code-page deploy build. Dropped our duplicate, kept master's. **LESSON: a full test-gate can be green while the production bundle is broken — the deploy build is the real gate for shared-lib barrel integrity.**
- ✅ **BFF deployed** → `spaarke-bff-dev` via hardened `Deploy-BffApi.ps1`: package 46.59 MB, all 4 critical files SHA-256-verified, `/healthz` = 200 **Healthy** (RoutingConsumerTypeHealthCheck green even pre-047 — core's merge or ConsumerTypes registration resolved it). Compose routes verified registered: create-on-save (100) → 401, annotations (102) → 401 (not 404).
- ✅ **SpaarkeAi code page deployed** → spaarkedev1 web resource `sprk_spaarkeai` (id 5206a442-3451-f111-bec7-7ced8d1dc988), updated + published. Bundle 4910 KB; Vite cache cleared; verified bundle contains create-on-save / compose_context_insert / Push-to-Word / compose/sessions.

**▶ Owner runs Tier-1 UAT** (script: `notes/UAT-tier1-getting-started.md`) against the live sandbox — NOT a gate for continuing work (owner directive 2026-07-10: "I'll UAT and come back with feedback"). Bring UAT results back for gap remediation. Owner queue stays: 047 (Tier-2 AI actions) → 056 (Tier-3 webhook) → merge-to-master (core-first) → 034 undo decision.

**▶ CONTINUED (non-UAT-gated work, 2026-07-10) — DONE this cycle:**
- ✅ **055** FR-28 push/save DETERMINISTIC half (`14968ddc1`) — ComposeService orchestration + ComposePushSavePreviewCalculator + ComposePushSaveStatusStore (Redis) + push-preview route; 15 unit + WebApplicationFactory forcing test (4/4, incl. route-existence proof + etag-412 no-partial-write + Redis-persisted state through the wire). **Sent the agent back once** to add the WAF test — held 055 to the same E2E DoD as every other task (service-only tests didn't cross the boundary). Gated UI halves = clean seam pending core A0.
- ✅ **081** BFF-hygiene NFRs (`e18d9170b`) — publish 45.27 MB compressed (−4.36 vs baseline; no lever needed), no new CVE, NetArchTest facade 2/2. Report: `notes/publish-size-cve-netarch-verification.md`. ADR-029 baseline ratcheted.
- ✅ **083** AnchoredAnnotation Path-A ACCEPT (`e18d9170b`) — 5/5 claims verified; implementation self-documents the boundary; facade-enforced. Report: `notes/anchored-annotation-path-a-signoff.md`.
- ✅ **Index reconciled**: 046 →✅, 017 →✅◐ deployed, 084 →✅ via 101, 070 ⭐ superseded.

**▶ "COMPOSE HAS NO AI" — root cause found + fixing (owner escalation 2026-07-10):**
Owner caught that deployed Compose only loads files — no AI function is usable. Root cause = THREE linked gaps: (1) 047 catalog not deployed; (2) the 5 compose bindings declared `sprk_surfaces` as result-destinations (assistant/context/workspace) not trigger-placement, so `capability-discovery?surface=compose` returned nothing; (3) NO activation wiring — `registerComposeAiToolbarAction` called only in a test.
- ✅ **Gap 2 FIXED** (`fb...`/mirror) — additively added `compose` to the 5 bindings' surfaces in `infra/dataverse/sprk_playbookconsumer-rows.json`.
- ✅ **Gap 3 FIXED — task 048** (`83dfd3067`) — `useComposeToolbarActivation` hook fetches `/api/ai/capabilities?surface=compose` on Compose mount → registers each bindingId onto the matching toolbar action (id===consumerType); wired at `composeEditor.registration.ts` (covers SpaarkeAi + LegalWorkspace); module-subscription re-render; 3/3 real-render forcing test; jest 82/82; Step 9.5 clean. **Closes 101 gap 2.2 code-side.**
- ✅ **MERGED TO MASTER (PR #626, master `14dd8ee3c`) + REDEPLOYED (both projects live) 2026-07-10**. Coordinated with core: re-merged core PRs #622/#624/#625 (clean, heads-ups verified zero compose impact), merged compose→master, redeployed BFF + code page from the merged state. `/healthz` Healthy; compose AI routes live. **⏳ ACTION ON CORE**: deactivate the 3 retired `sprk_analysistool` rows (their `notes/075-legacy-workspace-tools-verdict.md`) — deploy done, flip at convenience. Handoff: `notes/HANDOFF-to-core-deploy-done-flip-rows-2026-07-10.md`.
- ✅ **AI ACTIVATION DEPLOYED 2026-07-10 (freeze lifted)** — the "Compose has no AI" gap is CLOSED end-to-end. 5 actions + 5 bindings deployed (direct API, working around both drifted scripts); found+fixed a schema gap (added the Compose disposition optionset value 100000006 via InsertOptionValue); code page redeployed (048 activation wiring + 072 Q&A). **PENDING: owner UI smoke test** — open Compose → select clause → buttons enabled → click Explain → dispatches (allow ≤5 min for the BFF 5-min catalog cache TTL). Details: `notes/catalog-deploy-tooling-debt-and-resume.md`. 047 → ✅, 101 gap 2.2 → fully closed, 048 → ✅.
- 🧊 **(historical) DEPLOY FREEZE 2026-07-10** — was lifted; the resume above completed.
- ◐ **Gap 1 PARTIALLY DEPLOYED (then frozen)**: the **5 `sprk_analysisaction` rows ARE deployed** to spaarkedev1 (direct API upsert via scratchpad/deploy-compose-actions.ps1 — the mirror↔`Deploy-AnalysisAction.ps1` drift was a phantom: the real schema has NO action-type field, just actioncode/name/description/systemprompt/outputschemajson/temperature/inputschema; ids captured in git log). The **5 bindings are NOT deployed** — `Seed-PlaybookConsumers.ps1` is ALSO drifted (400 on 22/23 rows; 400=rejected so NO damage, existing rows intact, only insights-search no-op re-upserted). **RESUME PLAN (after freeze)**: bindings via a DIRECT API POST to sprk_playbookconsumer (schema now known: sprk_consumertype/consumercode/environment/name/priority/enabled/disposition/risk/capturemode/surfaces/tooldescription + `sprk_Action@odata.bind` lookup to the new action ids + resolve actionCode) — work around the drifted seed like I did the actions. Then verify capability-discovery?surface=compose returns 5 → redeploy code page (048 wiring) → user can click Explain/Draft/Compare. State: 5 orphan actions on dev (harmless, no binding routes to them yet).
- ⛔ **Gap 1 original blocker note (superseded — the actionType ambiguity was a phantom)**: verified the 5 `sprk_analysisaction` rows are NOT on spaarkedev1. Binding seed is READY + SAFE (`Seed-PlaybookConsumers.ps1 -DiffOnly` = exactly 5 net-new, zero drift on the other 18) but HELD (bindings ref actions). Action deploy BLOCKED: `Deploy-AnalysisAction.ps1` format-drift (wants `actions[]` + `actionTypeName` string; mirror is bare object + `actionType:0` int) + ambiguous actionType→lookup mapping (live `sprk_analysisactiontype` has BOTH "04 - Analysis" AND "Analyze") + live-env catalog conventions (existing `INS-FACT@v1` rows). 047 POML names PlaybookBuilder/Dataverse-MCP as deploy surfaces. **DECISION NEEDED**: (A) owner confirms actionType 0 → "04 - Analysis" or "Analyze" → I deploy via Dataverse API (dry-run first) + seed bindings + verify dispatch; (B) owner deploys via PlaybookBuilder; (C) I reconcile mirror↔script format. **Then redeploy code page → user can click Explain/Draft/Compare for real.**

**▶ NON-DEPLOY QUEUE EXHAUSTED (2026-07-10, under dev freeze):** 055 ✅, 081 ✅, 083 ✅, 048 ✅ (toolbar activation wiring), 072 ✅◐ (Doc Q&A — UX+17 tests done, live grounded-answer deploy-pending), seed-script casing bug FIXED, binding-deploy dry-run-validated + resume-ready. **Everything remaining needs either the DEPLOY (frozen) or an OWNER DECISION.** No further independent non-deploy code work of value remains.
  - **DEPLOY-FROZEN resume (1 command + redeploy)**: `notes/catalog-deploy-tooling-debt-and-resume.md` — deploy 5 bindings → verify capability-discovery → redeploy code page → smoke Explain. Then Compose surfaces AI E2E + 072 live-validates.
  - **OWNER DECISION open**: **034** (FR-17 undo/replace scope — Full durable-retract Path B vs replace-only). This is the one remaining FEATURE that's non-deploy-buildable ONCE the owner picks the scope. Everything for it (E-30 coded-workflow seam, ledger supersession from 033) is in place.
  - Owner-gated deploys: 047-bindings, 056 webhook, 082 flagship, code-page redeploy. Core-blocked: 063/064/071. 090 wrap-up needs all above.

**▶ BOUNDARY REACHED — historical note (now superseded by the exhausted-queue line above):**
- **072** (FR-35 Document Q&A over session-mounted document) — 🟢 STANDARD, has POML, inherits 104's Flow-6. **STRETCH goal.** Awaiting owner priority call (stretch + imminent UAT feedback = judgment decision, not an obvious default).
- **Owner-gated**: 034 (undo scope decision, OPEN) · 047 (Tier-2 catalog deploy) · 056 (Tier-3 webhook config) · 082 (flagship browser gate) · 017 mount-verify (= Tier-1 UAT).
- **Core-blocked (⛔)**: 063/064 (memory.write / D-F4 trace) · 071 (UI ack-on-frame-id) · 055 gated-UI + 072 Flow-6 depth both attach to core Phase A0 contracts.
- **090 wrap-up**: needs all of the above; not yet startable.
- **Low-value optional**: 001/008 spikes. No more independent work remains within the hard limits. **OWNER wake-up queue** (all owner/live-env): (1) **deploy** BFF (017) + SpaarkeAi code page to sandbox → run **Tier-1 UAT** (script staged); (2) **047** catalog deploy → Tier-2 (AI actions) + flips /healthz green; (3) **056** webhook KV config → Tier-3 (instant Word return); (4) **merge to master** after UAT (coordinate core-first per the handoff); (5) the **034 undo/replace scope** decision (still OPEN). Expect Tier-1 UAT to surface 1–3 real-env gaps (auth/container-resolution/CORS) — that is the point of deploying early.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Where we are** | **Compose dispatch is UNBLOCKED + 016 RE-VERIFIED GREEN** (E-20 on master `c300ab12d`). Big blocks done this session: Phase-1 Entry (010/011/012/013/014/**016** ✅, 015 pending), Phase-3 Inline (030/031/032/033 ✅), Phase-5 Word shuttle COMPLETE (050/051/052/053/054 ✅), Phase-6 memory started (060 ✅◐/061 ✅). ADR-043 reviewed; core coordination loop closed. Worktree + master + main-repo all at `c300ab12d`; branch +N tracking commits. |
| **🚨 E2E GAP AUDIT (2026-07-09) — READ THIS** | A 5-slice parallel read-only audit found the compose feature is **built in halves, unit-green, but E2E-inert on every create/write/coordinate path** (~30 gaps, 5 clusters). Read paths (open-in-Compose, search, replace-save) work; create-on-save, AI-action dispatch, Word shuttle, memory-resume, and three-pane coordination do NOT run E2E. Root cause: no HTTP-boundary vertical-slice test → unit-green ≠ working. **Full register: [notes/e2e-gap-register.md](../notes/e2e-gap-register.md)** — this is the fix backlog (owner directive: fix, do not defer). `/healthz` is currently Unhealthy on AI envs (ConsumerTypes ahead of the 047 row deploy). Project ✅ count (39/57) OVERSTATES completeness. |
| **Status** | 016 false-green CLEARED + Wave 046/062 landed (2026-07-09). Dispatch plumbing wired, BUT E2E audit shows systemic wiring gaps — see register above. |
| **✅ WAVE 046 + 062 DONE (2026-07-09, parallel — disjoint toolchains)** | **046** FR-13 dispatch wiring — **ESCALATED then Path-C re-scoped** (POML named 2 retracted "invented" discriminants `compose_action_request`/`compose_edit_apply_request` that Spike 0 + design.md §7.2 + compose-contracts.ts forbid; agent correctly stopped; orchestrator confirmed Path C, rewrote POML to real flows). Wired via React-context bridge (`composeActionBridge.ts`, NOT a bus discriminant) threading enqueueComposeAction→ConversationPane.dispatchComposeAction→dispatchConsumer + Flow-5 apply leg emitting EXISTING `compose_assistant_insert` (ledgerRef). ZERO new routes / discriminants / string-key routing. jest 11/11 new + 61/61 + 69/69, tsc clean, code-review clean, adr-check 0. · **062** FR-33 cross-version persistence — session keyed by DocumentId+MatterId (Word save/new DOCX version no longer resets/forks); consumes existing compacted digest (ChatHistoryManager) + task 061 GetActionHistory into restored view. 9/9 new + 162/162 Compose regression, build 0 err, publish 46 MB (~0 delta), ADR-clean. Scope note: touched IComposeService.cs (DTOs live there; additive nullable, accepted). Consolidated dotnet build gate: 0 errors. |
| **✅ 016 RE-VERIFY DONE (2026-07-09)** | Compose disposition proven to dispatch E2E through the REAL `SessionDispatchOrchestrator` — `DispositionRoutabilitySeamTests.DispatchAsync_ComposeDisposition_Admits_Routes_Stores_AndRenders` (admit→route→store `"compose"`→render terminal complete) + `ComposeDispositionContractTests` (frame shape) + `ComposeDraftDispositionTests` (FR-04 ledger-first consumer): **24/24 green**. No code change (verification pass), no publish/CVE delta, Step 9.5 N/A. **Same admit-gate carries 033 + 042 → their prior false-green is cleared by the same landing** (one admit-gate, one OutputRouter compose leg). 016 POML→completed, TASK-INDEX 016→✅. |
| **NEXT ACTION (explicit)** | **034** (FR-17 undo/replace, Path B durable retract — mechanism already in A0; dotnet+frontend so it runs SOLO, cannot overlap another BFF/frontend wave) → **047** (deploy compose Binding row — atomic w/ ConsumerTypes per `/healthz` parity gate; deploy/live-env) → **084** (consumer vertical-slice seam test — largely SATISFIED by `DispositionRoutabilitySeamTests`; confirm scope vs ADR-043 B6/O3 before spending) → **082** (flagship, live env, owner-run). Alt independent tracks: **015** (FR-06a upload fidelity, serial on `ComposeService.cs`); **DEF-04** (wire 060's annotation endpoint); parked **auto-save** decision. |
| **🎉 E-20 LANDED — DISPATCH UNBLOCKED (2026-07-09, master `c300ab12d`)** | The Phase-E batch (E-20/E-12/E-30/E-42) merged to master + into this worktree. `DispositionRoutability` admits `Compose` → **the compose-dispatch 422 is GONE**. Our routing hand-patch cleanly superseded (core owns the collapse). **Freeze on OutputRouter.cs/Binding.cs LIFTED.** Caught+fixed core's **E-30 fixture drift** (23 dispatch-contract tests red on master — `ICodedWorkflowRegistry` ctor dep unregistered in Dispatch+Summarize fixtures; verified pre-existing on origin/master; fixed both, commit `5f59cbc0b`, flagged to core in `notes/HANDOFF-to-core-e30-fixture-drift-flake.md`). Residual: pre-existing `AuditLogService` full-suite flake (passes in isolation). Full BFF unit suite 8028 pass / 1 flake; dispatch+compose+summarize 408/408. **NEXT: execute the unblocked chain — 016 re-verify E2E → 042/033/046 → 034 → 047 → 084 → 082.** |
| **Wave 060+014 (2026-07-09)** | ✅ **014** parent-association prompt — SpaarkeAi-only (5-choice UI + `associateDocumentToParent` reusing `AssociateToStep`+`discoverNavProps`; gate-hosting guarded by inert `GateDecisionV2` adapter). 8 tests, tsc clean. Deferred: live dialog wire + `associate()` from save-completion (call sites out of boundary — see defer-issues). · **✅◐ 060** anchored annotations — SERVICE LAYER + model + client types + 7 tests done (Compose 270/270, Path-A grep-clean, no memory.*). **NOT functional E2E**: no HTTP endpoint wire (**DEF-04/#604** — client rehydrate always empty) + Redis-hot-tier only, no Cosmos (**DEF-05/#605**). Touched `ChatSession.cs` (additive, the payload carrier). **NEXT (serial on ComposeService.cs): 062 → 015.** |
| **Task 054 (2026-07-09)** | ✅ FR-27 return-from-Word re-anchoring — Word round-trip path COMPLETE (050 write → 051 read → 052/053 detect → 054 re-anchor). NEW `AnnotationReanchorService` (Levenshtein content-match + structural hint → auto≥0.85/review0.6-0.85/orphan<0.6 bands; **Spike-6 ambiguity guard**: secondBest≥best−0.05 && >0.5 demotes Auto→Review; Redis sibling key `sdap:compose:reanchor:{speId}`). Endpoint 13 `POST /document/{speId}/reanchor-annotations` (all 12 preserved; webhook on `routes` for AllowAnonymous). Frontend: ComposeReanchorBanner + ConflictPanel + useComposeReanchor + types (11 tests). 14 BFF tests, Compose 262/262, integration compiles, publish 46.54 MB; compose-components tsc clean, jest 64/64 (7 suites). No new packages/CVE. |
| **Wave 053+032 (2026-07-09)** | ✅ **053** FR-26 webhook receiver (`POST /webhooks/spe-doc-changed`) + poll (`POST /document/{speId}/check-changes`) — all 10 prior routes preserved; new `SpeWebhookNotificationVerifier` (pure, 12 unit tests) + `SpeSyncOrchestrator.ResolveContainerIdForDriveIdAsync`; webhook auth mirrors CommunicationEndpoints HMAC+clientState fail-closed. Compose 248/248 (+16), integration project compiles clean, publish 45.18 MB. Filed **DEF-03/#602** (Compose:Webhook:SigningKey+ClientState → Key Vault before prod). · ✅ **032** serial queue — ALREADY delivered (`189f972f6` 016/030/032 wave); verified 235 tests, correct `dispatchConsumer` seam, no `compose_action_request`. **NEXT: 054** (re-anchor, full-stack) solo — can't parallelize with 053 (both edit Sprk.Bff.Api); 053→054 is also the dep order. |
| **Wave 013+030 (2026-07-09)** | ✅ **013** (rescoped Option 1) create-on-save BACKBONE: `SaveAsync` → container→record→indexing→projection; client-supplied `ContainerId` (DTO relaxed, wire-compatible 15/15 contract); Fork-B drive-item create via `ISpeFileOperations.UploadSmallAsUserAsync` (additive); Fork-C profile = `deferred` step (JobAwareState.Queued+Detail); interim R5-E = `IsInterimCreateOnSaveSuccess` (container+record+indexing Completed; aggregate=Partial while profile deferred). 8 new tests, Compose 232/232, NetArchTest facade green (no AI internals injected), publish 45.19 MB. Fork-C handed to core: `notes/HANDOFF-to-core-profile-analysis-facade.md`. · ✅ **030** BubbleMenu AI toolbar — ALREADY delivered in 016/030/032 wave (zero code diff); verified clean (0 findings), correct real dispatch seam, honest-stub disabled buttons for Phase-4 gate. · 🔧 **FIXED regression on master**: `Spe.Integration.Tests/CrossPillarIntegrationTests.cs:91` ctor drift — `SendWorkspaceArtifactHandler` gained `IUiActionAckCoordinator` (Wave-J merge) + `ILogger` (012 `b53dc4871`); test only partially updated → integration project didn't compile on master. Added the missing `IUiActionAckCoordinator` mock arg; project compiles. **Process gap**: 012 reconciliation ran unit+Compose filter only, not the integration test project — that's how it slipped to master. |
| **Wave 051+011 (2026-07-09)** | ✅ **051** DocxAnnotationReader (357-line pure OOXML reader + `POST /pull-annotations`; all 10 routes preserved; fixed Spike-6 EDGE-R4 multi-paragraph anchor; DI-free stateless parser) — 9/9 new, Compose 224/224, ArchTest facade green, publish +0.12 MB, 1 pre-existing HIGH CVE (Kiota, not introduced). · ✅ **011** FR-02 Search→reuse-1c (Xrm lookup → resolve speId/driveId/tenantId/recordId → existing `GET /api/compose/documents/{speId}` load; REAL loaded doc w/`documentRecordId`, distinct from 010's transient; removed dead `compose_search_requested` bus dispatch) — adr-check 0 violations, filed **DEF-02** (searchResolvedDriveId reset gap, non-blocking). **RECONCILED**: BFF 0 errors + Compose 224/224; compose-components tsc clean + jest 53/53 (6 suites, 010 browse + 011 search coexist); no conflict markers. |
| **Status** | ✅ **010** FR-01 Browse→transient mount (wired `handleBrowseRequested` → hidden `.docx` input → `FileReader.readAsArrayBuffer` → existing `mountTransient` reducer + `setIsDirty(true)`; removed stale no-op `compose_browse_requested` bus dispatch; ComposeEmptyState untouched). tsc clean, **jest 46/46** (6 new), code-review/adr-check clean. Filed **DEF-01 / issue #601**: `ComposeEditor` `docxBytes` effect resets `dirty=false` after transient mount (pre-existing, shared w/012). · 🛑 **013** FR-05 create-on-save: correctly escalated at Step 0 (POML pre-authorized) — the "BU→container primitive" is **client-side, not server-side**; needs 3 coupled forks: **A** client passes container id (existing convention), **B** create SPE drive-item for transient drafts (no `DocumentSpeId` yet), **C** new `Services/Ai/PublicContracts/IDocumentProfileAi` facade (ADR-013 NetArchTest + MI-403 on OBO file) = **core's territory**. Clean-once-settled: indexing (`IPostUploadIndexingEnqueuer`), record step, `JobAwareCompletionState` (already names the 4 steps). Full detail: `notes/defer-issues.md` "Open architectural escalations". |
| **Next Action** | **AWAITING OWNER on 013**: (1) re-scope 013 to A+B backbone (compose) + file C to core, or (2) hold all 013 for core. Then **next independent waves**: **051** DocxAnnotationReader (dotnet; 006 gotchas: DeletedText/w:date-UTC/comment-anchor-crosswalk) + **011** search→reuse-1c (npm); **054** re-anchor (apply 006's ambiguity-guard AUTO→REVIEW when 2nd candidate within ~0.05); **053** webhooks (052 done); **060/062** memory. **HELD** (dispatch/operand-gated): 016/034/046/047, 030/032, 063/064/071. **Core**: awaiting ContextEnvelope operand-home decision + ADR-043 draft (ack in `notes/HANDOFF-compose-r2-ack-execution-foundation.md`). |

### 🚨 Uncommitted-on-disk at checkpoint (survives compaction; commit after 012 reconciles)
- **050**: NEW `Services/Compose/DocxAnnotationWriter.cs`, `Infrastructure/Graph/SpeConcurrencyExceptions.cs`, `tests/unit/Sprk.Bff.Api.Tests/Services/Compose/DocxAnnotationWriterTests.cs` · MOD `Infrastructure/Graph/{ISpeFileOperations,SpeFileStore,UploadSessionManager}.cs` (If-Match overload), `Services/Compose/{IComposeService,ComposeService}.cs`, `Infrastructure/DI/ComposeModule.cs`, `Api/Ai/ComposeEndpoints.cs` (push-annotations route — **shared w/012**).
- **006**: `notes/spikes/spike-6-word-roundtrip.md` + `word-roundtrip-prototype.cs` + `spike6-*.docx`, 006 POML.
- **080**: `projects/spaarkeai-compose-r1/{spec.md,CLAUDE.md}` (amendments), 080 POML.
- **012** (in-flight): SpaarkeAi upload-mount + Compose lib + `Api/Ai/ComposeEndpoints.cs` upload endpoint (**shared w/050 — the reconciliation risk**).
- **Also uncommitted**: `notes/HANDOFF-compose-r2-ack-execution-foundation.md` (ack to core: confirmed B1-B6 + caught ContextEnvelope operand-home gap — no slice for selectionText/changesText/documentText; operand volatile vs StablePrefix; awaiting core's operand-home decision + ADR-043 draft), this `current-task.md`.
- **Lesson**: 012 was mis-partitioned as npm-only — FR-03 also activates the BFF `POST /api/compose/upload`, so it shares `ComposeEndpoints.cs` + the dotnet build with 050. Don't co-schedule two BFF-endpoint tasks.

### Wave results (2026-07-09) — uncommitted
- **033 files**: NEW `src/client/shared/Spaarke.Compose.Components/src/widgets/hooks/usePendingRedline.ts` (+ `.test.tsx`, 16 tests) · MODIFIED `ComposeEditor.tsx` (materializeComposeDraft repointed → redline; +materializePendingRedline handle; accept/reject + unresolved-target banner) · `hooks/index.ts` barrel.
- **042 files**: NEW `infra/dataverse/actions/compose-draft-alternative.action.json` + `inputschemas/…` + `outputschemas/…` · MODIFIED `infra/dataverse/sprk_playbookconsumer-rows.json` (5th compose Binding, disposition=compose 100000006). Seed-time flag (non-blocking): `surfaces="workspace"` not in Binding.cs Surfaces vocab but degrades gracefully (parity w/044 `context`).
- **061 files**: MODIFIED `src/server/api/Sprk.Bff.Api/Services/Compose/ComposeService.cs` (+`GetActionHistory` read-only ledger query + `ComposeActionHistoryEntry`) · NEW `tests/unit/Sprk.Bff.Api.Tests/Services/Compose/ActionHistoryLedgerQueryTests.cs`.
- **Deviations (033, directional)**: contracts path is shared-lib `src/types/compose-contracts.ts`; `compose_edit_apply_request` event doesn't exist (used shipped Flow 5 `compose_assistant_insert`+`ledgerRef`); no client FR-19 validator → strict/first/all + ambiguity implemented in hook. Escalation NOT fired (contract matched HANDOFF §1). FR-17 ledger-write supersession = task 034.

### ▶️ PARALLEL INDEPENDENT WAVE while core builds the foundation (2026-07-09)
Owner: "work any items that can be done in parallel so we don't lose time to the fix." Dispatched 4 sub-agents on tracks INDEPENDENT of the compose dispatch foundation (core Phase E, `Services/Ai/`) AND the unsettled operand contract. Toolchain-partitioned (≤1 dotnet + ≤1 npm + no-build):
- **050** (a10e2b200bf2898b8, dotnet/opus/xhigh) — FR-24 DocxAnnotationWriter (w:comment/w:ins/w:del) + push endpoint + If-Match/ETag conflict. THE dotnet builder. `Services/Compose` + `Api`.
- **012** (abc44c89fe3b652a9, npm/opus) — FR-03 1b upload→transient mount (send_workspace_artifact flip + docxBytes seam). THE npm builder. SpaarkeAi + Compose lib.
- **080** (a86abb8a6b957311f, docs/sonnet) — R1 spec amendment (2 Word non-goals → shipped in R2). No build.
- **006** (af53ec52552dfc786, spike/sonnet) — reverse round-trip + tune re-anchor bands (gates 051/054/008). Standalone scratch prototype (no BFF/npm build). notes/spikes.
Each via task-execute FULL rigor (080 MINIMAL); write-boundary = own POML only, main session reconciles TASK-INDEX/current-task + consolidated build after.
**On deck (next waves, independent)**: 013 create-on-save (dotnet), 010/011 entry mounts (npm), 051→054 Word reader/reanchor (after 006), 053 webhook endpoints, 060 anchored annotations, 062 compaction, 083. **Held** (dispatch-gated / operand-gated): 016/034/046/047, 030/032, 063/064/071.
**Ack to core on disk (uncommitted)**: `notes/HANDOFF-compose-r2-ack-execution-foundation.md` — confirmed B1-B6 + caught the ContextEnvelope operand-home gap (no slice for selectionText/changesText/documentText; operand is volatile vs StablePrefix). Awaiting core's operand-home decision + ADR-043 draft.

### 🏛️ PLATFORM-LEVEL ARCHITECTURE ASSESSMENT (2026-07-09) — DONE, on master
**Owner reframed the question**: don't just patch the compose-dispatch seam (tactical option "a" = kicks the can + orphans the cross-cutting debt between projects/teams). Strict project boundaries are THE root cause. Wants a proper **requirement / problem / solution analysis for the WHOLE legal-AI solution** (not compose-projected), covering BOTH the technical architecture AND the ownership/governance model that lets cross-cutting execution-layer debt fall between platform-core and satellites.
**Deliverable**: a platform-level assessment in `docs/assessments/` (same home as `bff-ai-extraction-assessment-2026-05-20.md` that CLAUDE.md §10 governance was built from) → then an ADR as decision-of-record. Placed at platform level ON PURPOSE (anti-orphaning). Ties the org fix to the EXISTING proven precedent: CLAUDE.md §10 (BFF Hygiene governance) + §11 (Component Justification) — extend that pattern to the AI execution layer.
**Thesis so far** (from the 3 validation agents): the AI capability EXECUTION engine realizes only a narrow slice of the declarative ADR-039 catalog contract — input hardcoded to session files, disposition a hardcoded allow-list (drifts from OutputRouter), action-kind prompted-only; AND there are TWO asymmetric execution engines (playbook `AiCompletionNodeExecutor` HAS input-binding/`runtimeInput`; canonical dispatch `ActionRunner` LACKS it) — the canonical one is the weaker; AND an ownership gap (shared seam = core's; need = satellites'; nobody owns generalization) + per-component definition-of-done (no vertical-slice test) hid it.
**3 platform surveys dispatched 2026-07-09** (read-only): `survey-dispositions` (a3954d0cf7bc8bf5c — is the narrow-slice problem systemic across all dispositions/capabilities?), `survey-engines` (a43752aa5afb2fe63 — two-engine asymmetry + platform input-source landscape + convergence intent), `survey-governance` (a3c2af1d28959b2d0 — §10/§11 governance template + core/satellite intake gap + definition-of-done/integration-test gap + other orphaned cross-cutting items).
**DONE**: wrote `docs/assessments/spaarke-ai-capability-execution-assessment-2026-07-09.md` (platform-level R/P/S + governance). 6 investigations complete + synthesized. **Awaiting owner review of §4 (decisions) + §5 (open Qs Q1-Q4).** Next: owner picks direction → core-owned ADR + re-plan (foundation owned by redesign-r2, compose = forcing consumer). Uncommitted on disk: the assessment + 045 eval work + validation note + this checkpoint. NO production code yet.
**Key findings**: (1) TWO disjoint capability spines — declarative disposition spine only realizes informational+2 legs; ALL side-effects live on a parallel loop-tool-handler spine. (2) Disposition triplicated across 3 drift-prone lists (my compose promotion left the 3rd — the admit gate — un-widened = the live 422). (3) THREE executors; the ADR-039-canonical `ActionRunner` is input-POOREST (doc-text only); the intended unifier `ContextEnvelope`+`ContextBinder` (core task 053) is UNBUILT + unconsumed. (4) Two-engine split is BY DESIGN (ADR-039 freezes node engine; unify=R8+) → fix is NOT "unify engines" but "complete the canonical spine + single-source dispositions + reconcile the 2-spine boundary". (5) Governance owns contract SHAPES not WIRING → orphaning is systemic (FAILURE-MODES AP-2/AP-4 isomorphic; a 2nd live UNASSIGNED orphan in the same seam). Fix = extend §10/§11 template to the execution engine (named owner + vertical-slice KEEP test + deferral re-parenting).

### 🛑 COMPOSE-DISPATCH FOUNDATION GAP (2026-07-09) — validated; SUBSUMED into the platform assessment above; code paused
**Finding (investigating deeper)**: Compose AI actions CANNOT dispatch/execute through the shared `SessionDispatchOrchestrator.DispatchAsync` (the only path from client `dispatchConsumer` → `/dispatch`):
1. **Disposition gate** (`SessionDispatchOrchestrator.cs:224`): only `Informational`/`WorkProduct` execute; `BindingDisposition.Compose` (100000006) → rejected `dispatch.disposition-not-supported` BEFORE reaching the OutputRouter compose case I built.
2. **File-oriented execution** (`~249-331`): resolves session FILES, hard-errors if none ("No session files were available"), reads only `fileIds` from args, builds `DocumentText` from file text — NEVER passes selection text. But all 5 compose actions run on `selectionText`/`changesText` (compose-selection), not uploaded files. So even the 4 Informational compose actions likely fail the file requirement.
3. Corroborating: 045 found the 5 compose consumer types are NOT registered in `ConsumerTypes.cs` (marked `catalogStatus: "planned"`).
**Implication**: the compose-dispatch chain (016/042/046/033/034/047/055) rests on a foundation that isn't built. Task **046 is mis-scoped** ("reuse orchestrator unchanged, client choreography only" — but the orchestrator MUST be modified). This is effectively the unbuilt remainder of core task 010's routing promotion (I did the OutputRouter half; the orchestrator-execution half was never done). My earlier "replace is fully supported" claim was WRONG (I'd only checked OutputRouter, not the orchestrator entry gate).

**OWNER DECISION (2026-07-09)**: Option **2 — re-plan the compose-dispatch chain**, BUT **validate the issue + resolution first** (think through ALL scenarios, component interactions, dependencies) before re-planning/coding. NO production code until diagnosis + resolution shape confirmed.

**VALIDATION IN FLIGHT** — 3 read-only investigation sub-agents dispatched 2026-07-09:
- `validate-paths` (af2ab460c0d67fadd): is SessionDispatchOrchestrator the ONLY exec path? enumerate all RouteAsync/RunAsync callers + gates; any working compose exec today?
- `validate-input` (a126d02b87d26d46e): how does ActionRunner/PromptSchemaRenderer receive input? can args/slots (selectionText) flow instead of session-file text? classify all 5 compose input schemas.
- `validate-deps` (a16a4505f6f92fe58): map affected/mis-scoped tasks (016/042/046/033/034/047/055/032) + corrected dependency graph + ConsumerTypes registration requirement.
**Next**: synthesize their findings → validation writeup + corrected task/dep graph → present to owner for approval → THEN re-plan tasks → THEN code.

### 045 (FR-12 eval cases) — DONE 2026-07-09, UNCOMMITTED (on disk)
50 new cases (10 families golden+dispatch × 5 rows), 117 total; NEW `tests/integration/contract/Catalog/ComposeR2OutputSchemaContractTests.cs` (15/15 — all 5 output schemas pass OpenAiFunctionSchemaValidator, no property-level required). 60/60 Eval + 52/52 Catalog + 447/447 combined; Step 9.5 clean; no banned shapes / no version suffix. Judgment call: 5 consumer types `catalogStatus:"planned"` (not in ConsumerTypes.cs — mirrors create-matter precedent). **Files uncommitted** — fold into next commit once dispatch direction settled.

### 034 (FR-17) — Path B chosen (2026-07-09, owner) — PAUSED pending foundation validation
**Decision**: durable pure-undo via a NEW `compose-retract` Action+Binding dispatched through the shipped seam (ADR-039 compliant, NO new endpoint). Escalation (§6.5) resolved by owner: Path B (full durable undo), accepting BFF+catalog scope expansion.
- **Replace** ("try another approach") = re-dispatch `compose-draft-alternative` → new higher-turn compose output supersedes → re-materialize. Fully supported by existing server code.
- **Undo** ("undo that") = dispatch `compose-retract` → executor writes a higher-turn compose output with a Compose-owned `retracted:true` payload → `ResolveCurrent` returns it → client renders nothing + clears prior marks. `ComposeDraftPayload` is Compose-owned/opaque to core → add `retracted` flag with NO core coordination.
- **Scope**: (a) `compose-retract` Action+Binding + mirrors; (b) retraction producer/executor (deterministic — investigating framework support); (c) `retracted` flag on `ComposeDraftPayload` (.cs + .ts); (d) client `useEditSupersession.ts` (SpaarkeAi) + `usePendingRedline` retraction handling + ConversationPane affordance + tests; (e) ≥10 eval cases for compose-retract (ADDITIVE to 045's 5-row scope — 045 running now, unaffected; coordinate/append after).
- **Open design Q (investigating)**: does the dispatch→executor path support a DETERMINISTIC (non-LLM) action for the retraction, or must the producer be wired differently? This gates the producer design.
- **045 sub-agent still running** (5-row eval scope, unaffected by the new row).

### 033 execution notes (main session, DONE — committed d23ec9c2e)
- **Design**: repoint `ComposeEditor.materializeComposeDraft` (only caller = ComposeWorkspace:589) to render a pending **redline pair** via new `widgets/hooks/usePendingRedline.ts`. Leaves ComposeWorkspace render-follows-store + refresh-durability + `lastMaterializedKey` idempotency + highest-turn (superseded) selection UNTOUCHED. Adds `materializePendingRedline` to the handle; keeps `materializeComposeDraft` as its delegate for back-compat.
- **Deviations (directional mode, grounded in code)**: (1) contracts file is `src/client/shared/Spaarke.Compose.Components/src/types/compose-contracts.ts`, NOT the POML's `src/solutions/SpaarkeAi/...` path. (2) POML's `compose_edit_apply_request` event does NOT exist (spike-0 correction; ComposeEditor JSDoc bans adding it) — real seam is Flow 5 `compose_assistant_insert` + additive `ledgerRef`, already wired. (3) No client FR-19 validator exists (task 020 = BFF-side) → implement strict/first/all target-resolution + ambiguity locally in the hook.
- **Marks API (031, verified)**: `setMark('deletion',{binding,ledgerRef})` over target span + insert new_text as `<span data-compose-mark="insertion" data-binding data-ledger-ref>` (parses back to InsertionMark). accept/reject = ledgerRef-keyed doc ops (not raw DOM). 034 does the true ledger-supersession write.

### ▶️ NEXT: Parallel wave plan (user asked to run 042 + 033 + any safe parallels)
Analysis of newly-unblocked tasks (parallel-safe flag / deps / files / build-system):
| Task | Tier | parallel-safe | Files (footprint) | Build | Notes |
|---|---|---|---|---|---|
| **042** draft-alternative | opus | **true** | `infra/dataverse/actions|inputschemas|outputschemas/compose-draft-alternative.*` + Binding row in `sprk_playbookconsumer-rows.json` | none (JSON) | 5th catalog row. **Binding now declares `disposition=compose` (100000006)** — routing live. Follow the compose catalog template (see 040 `.action.json`). Output schema per POML. Owner hygiene: no `@v1`. |
| **033** pending-redline | opus | **false** | `ComposeEditor.tsx` + NEW `widgets/hooks/usePendingRedline.ts` | npm | Materialize pending redlines from ledger using the 031 marks + compose-outputs read. parallel-safe=false (shares ComposeEditor) → **run in MAIN SESSION**. |
| **034** undo/replace | opus | false | `usePendingRedline.ts` (shared w/033) + `useEditSupersession.ts` | npm | **dep 033 → serialize AFTER 033.** |
| **050** DocxAnnotationWriter | opus/xhigh | true | BFF `Services/Compose` (C#) | dotnet | Word writer (hard). Disjoint. |
| **051** DocxAnnotationReader | sonnet | true | BFF (C#) | dotnet | Disjoint. |
| **060** anchored annotations | sonnet | true | (verify files before adding — likely ComposeWorkspace/BFF) | ? | deps none. Check footprint. |
| **061** action history via ledger | sonnet | true | BFF (C#) | dotnet | deps none. Disjoint. |
| **064** context-pane trace | sonnet | true | frontend Context pane | npm | core 038 landed → unblocked. |

**Build-contention rule** (shared worktree): NO concurrent `dotnet build`; NO concurrent `npm build`. So a safe wave = at most 1 dotnet-building task + 1 npm-building task + unlimited no-build (catalog JSON) tasks, running as sub-agents that write DISJOINT files; MAIN SESSION runs the consolidated build/test after.

**RECOMMENDED SAFE WAVE (3-way):**
- **Sub-agent A → 042** (catalog JSON, no build, parallel-safe=true)
- **Sub-agent B → 061** (action-history-ledger, BFF/dotnet, sonnet, deps none, disjoint) *(or 050 writer if you prefer the Word push next)*
- **MAIN SESSION → 033** (pending-redline, ComposeEditor/npm, opus, parallel-safe=false)
Then **034** after 033 (shares usePendingRedline.ts). Each task STILL runs via `task-execute` (FULL rigor; 042/033 also test-touching/opus). Cap 6 agents. After the wave: main session `dotnet build` + `npm run build`/typecheck + jest.

### Merged-to-master commit trail (2026-07-09)
`540760eac` routing promotion · (Wave K merge) · `7f5e592c4` task-035 OutcomeCard reconciliation (HEAD/master). Earlier: `978333245` 016/030/032 · catalog wave · gating.

### Still core-gated (not startable)
**042/033/034/064/060/061 NOW unblocked.** 071 needs 070 (070 deps 030/031 done → 070 startable but parallel-safe=false). Still blocked: **063** (core 057 memory.write — LAST A0 seam before the 017 "Compose UNBLOCKED" milestone). 045 (eval) needs 042; 047 (deploy) needs 045; 034 needs 033.

### ⬇️ Prior-session history below (016/030/032 integration wave — 2026-07-08)

### ✅ Integration wave complete (016/030/032) — 2026-07-08
Committed the frontend+BFF integration wave. Verification:
- **Compose.Components typecheck**: clean (against built `@spaarke/*` dists — built via `scripts/Build-AllClientComponents.ps1 -Component SharedLibs`).
- **jest** (now enabled — first config for the package): `ComposeAiToolbar.test.tsx` **10/10 green**.
- **BFF**: builds clean; Compose+ADR-013 suite **169/169 green** (incl. 4 new `ChatComposeOutputsProjectionTests`). Publish size **46.46 MB compressed incl PDBs** (< 60 MB ceiling; ~0 delta — no new packages).

**What landed:**
- **016 HOOK #1** (BFF read endpoint): `GET /api/ai/chat/sessions/{sessionId}/compose-outputs` in `ChatEndpoints.cs` — reads the existing `session.Outputs` ledger surface (ADR-040), projects `compose`-disposition entries via new pure `ProjectComposeOutputs` (skips truncation markers). New `ComposeLedgerOutputDto` in `SessionLedgerEntries.cs`. §10/§11: extends ChatEndpoints + reuses `session.Outputs`; no new service/DI/package.
- **016 HOOK #2** (editor materialize): `materializeComposeDraft(draft, provenance)` added to `ComposeEditorHandle` + implemented (clean cursor insertion of `new_text` as escaped paragraphs; positioned `target_text` replace + pending-redline marks + provenance badge are **task 031**). Shared `ComposeDraftPayload`/`ComposeDraftProvenance` types now owned by ComposeEditor + imported by ComposeWorkspace (removed the local mirror + `ComposeDraftMaterializeCapable` hack).
- **016 HOOK #3** (contract): additive `ledgerRef?` on shared `ComposeAssistantToWorkspaceFlow` (compose-contracts.ts); ComposeWorkspace's local `ComposeAssistantInsertLedgerSignal` hack removed.
- **FR-18 seam (near-side)**: optional `enqueueComposeAction?` threaded toolbar → editor → workspace (`ComposeActionEnqueue` type). When a host supplies it, toolbar routes dispatch through 032's serial queue; else falls back to its own bound dispatcher.

**Deferred (with rationale):**
- **FR-18 far-side host wiring** — delivering ConversationPane's `dispatchComposeAction` across panes to `ComposeWorkspace.enqueueComposeAction` is a host/shared-context decision, and no toolbar action can dispatch until Phase-4 catalog (core-gated) wires real `bindingId`s. Near-side seam is ready; host wires it at Phase 4.
- **SpaarkeAi solution typecheck** — the 032 files (`ConversationPane.tsx`, `useSerialActionQueue.ts` + test) are prior-session WIP UNCHANGED this session; my edits are confined to the shared Compose lib (typechecks clean). Full SpaarkeAi typecheck needs a full solution `npm install` — deferred (unmodified-by-this-session files).
- **Core follow-up**: the compose WRITE path (`BindingDisposition.Compose` + `OutputRouter` case) is core task 010, not present — so the read endpoint returns `[]` until then (render-follows-store path is correctly dormant end-to-end).

### Files Modified This Session
- `notes/spikes/spike-0-dispatch-path.md` - Created - Spike 0 (dispatch seam confirmed; `compose_action_request` correction)
- `notes/spikes/spike-2-edit-validator.md` (+prototype) - Created - adeu match_mode + ambiguity errors VALIDATED (ran headless)
- `notes/spikes/spike-3-edit-batch.md` (+prototype) - Created - 4-phase batch + rollback VALIDATED (ran headless)
- `notes/spikes/spike-4-semantic-appendix.md` - Created - design-confirmed; hallucination measurement deferred
- `notes/spikes/spike-5-openxml-write.md` (+sample-annotated.docx) - Created - Open XML w:ins/w:comment writer VALIDATED (real .docx, 0 errors)
- `notes/spikes/spike-7-checkout-collision.md` - Created - checkout=Dataverse advisory lock; conflict UX from 423/412
- `design.md` - Modified - Spike 0 dispatch-contract correction (§2.1/§3/§5/§7.2/§13 + revision log)
- `tasks/000/002/003/004/005/007-*.poml` - Modified - status→completed
- `tasks/TASK-INDEX.md` - Modified - 000/002/003/004/005/007 🔲→✅

### Critical Context
Planning complete; execution started. **Spike 0 result**: the ADR-039 session-dispatch seam is
confirmed (static trace) end-to-end for a Compose Binding — ZERO new BFF dispatch routes.
**Correction for Phase 1/3/4**: the design's `compose_action_request` event does NOT exist;
use the R1-shipped six-flow contract (`compose-contracts.ts`) — a selection emits
`conversation.compose_selection_offer` (Flow 2), dispatch is a direct `dispatchConsumer(bindingId,
{slots})` call (useConsumerChips pattern), editor insertion is `workspace.compose_assistant_insert`
(Flow 5). Tasks **016/030/046** must be authored against this. The parallel Compose action endpoint
is confirmed deleted (design §2.1/§7.2 holds). Remaining split unchanged: independent tracks
startable now; core-gated tracks (⛔) wait on core R2 Phase A0.

---

## Active Task (Full Details)

| Field | Value |
|-------|-------|
| **Task ID** | 040/041/043/044 (Phase 4 catalog wave) |
| **Task File** | tasks/04{0,1,3,4}-*.poml |
| **Title** | FR-07/08/10/11 compose Action + Binding catalog rows |
| **Phase** | 4 Catalog |
| **Status** | ✅ completed (2026-07-09) — 042 DEFERRED (core 010) |
| **Rigor** | FULL · sonnet@high (session on Opus) · directional |

**Phase-4 catalog wave (040/041/043/044) done — mirror-first, ADR-039.** Each capability = action-only seed (`infra/dataverse/actions/{code}.action.json`, systemPrompt home) + input mirror (`inputschemas/`) + output mirror (`outputschemas/`) + Binding row (`sprk_playbookconsumer-rows.json`). 13 files valid JSON; no banned property-level `required:true`; OptionSet codes verified vs `Binding.cs` (disposition=Informational/risk=None/captureMode=LoopElicitation = 100000000). Deploy = task 047 (`Deploy-AnalysisAction.ps1` + `Seed-PlaybookConsumers.ps1`); eval cases = task 045.
- **SystemPrompt-home decision** (was the open review question): lives on `sprk_analysisaction.sprk_systemprompt` in an **action-only** seed file (no playbook — engine frozen); grounded in the R5 rule "sprk_systemprompt IS the JPS prompt primitive."
- **042 (draft-alternative) DEFERRED**: its Binding declares the `compose` disposition = core task **010** (`BindingDisposition.Compose` + OutputRouter case), not landed. TASK-INDEX 042 → 🔴/⛔ (was wrongly unblocked).
- **In-file REVIEW FLAGS** (non-blocking, for deploy/seed validation): 044 `surfaces="context"` (renders in Context pane — confirm the surface value is recognized); 043 input `changesText` upstream wiring finalizes with tasks 051/054; `ucid` left null pending a compose use-case id; span fields authored as text snippets (LLM-reliable).
- **031 (prior) done**: 3 custom marks; jest 19/19; on branch.

**Next**: 045 (eval cases ≥5 golden + ≥5 dispatch per row — FULL, modifies tests/) then 047 (deploy). 046 (dispatch wiring) also startable. 042 waits on core 010.

---

## Progress

### Completed Steps
*No steps completed yet — task decomposition pending*

### Files Modified (All Task)
*No task files yet*

### Decisions Made
*Project-level decisions recorded in CLAUDE.md §Decisions Made*

---

## Next Action

**Next Step**: `/task-create projects/spaarkeai-compose-r2`

**Pre-conditions**:
- plan.md phase breakdown reviewed (done)
- Worktree synced to master (done — 0 behind)

**Key Context**:
- Refer to `plan.md` §4 for phase deliverables + core-A0 gating markers
- Refer to `spec.md` for FR/NFR acceptance criteria
- ADR-039/040 govern the AI dispatch + ledger surface

**Expected Output**:
- `tasks/*.poml` files + `tasks/TASK-INDEX.md` with dependency graph + `blocked-on: core-A0` markers + parallel groups

---

## Blockers

**Status**: None (planning) — note: several implementation phases are gated on core R2 Phase A0 (see CLAUDE.md §Core Phase A0 dependency)

---

## Session Notes

### Current Session
- Started: 2026-07-08
- Focus: project initialization (design refinement → spec → adr-check → planning artifacts)

### Key Learnings
- Entry-point state verified in code: 1c works; 1a/1b are build items; mount seam (`docxBytes`) + `PromoteIfEphemeralAsync` already exist (shrinks scope)
- Core R2 authored this project's initial design.md; core setup being finalized — dependency is real but coordinated

### Handoff Notes

**W0 spike-surfaced corrections (fold into design/spec + task authoring before the affected tasks run):**
- **Spike 0** → design.md ALREADY corrected (`compose_action_request`/`compose_edit_apply_request` don't exist; real contract = Flow 2 `compose_selection_offer` + direct `dispatchConsumer` + Flow 5 `compose_assistant_insert`). Affects tasks 016/030/046.
- **Spike 2** → task 020 (FR-19): adopt adeu `match_mode` + structured ambiguity errors verbatim; **fuzzy/typo matching is Phase-2 deferred** (not in validator); task 020 must state which document projection the offsets are relative to.
- **Spike 3** → ✅ APPLIED design §6.1: overlap (non-fatal skip-and-report) vs validation-failure (fatal whole-batch rollback) are **two separate code paths**. Tasks 021/022 must model both.
- **Spike 4** → stale "defer defined-terms to R3" superseded by 2026-07-08 scope-lock; `SemanticAppendixGenerator` (deterministic pre-scan) ≠ `compose-defined-terms` Action (LLM checker) — keep distinct. Cross-refs need OOXML (Phase-2 reader), not flat text. (No design edit needed — captured for task 004/060/044 authoring.)
- **Spike 5** → ✅ APPLIED design §14 + publish-size: `DocumentFormat.OpenXml` 3.4.1 already a BFF dep (`Sprk.Bff.Api.csproj:128`) → **zero package/size delta** for the writer; `Codeuctivity.OpenXmlPowerTools` only needed IF diff/redline is built (reader/compare), NOT the writer. Task 050 gotcha: `w:del` text = `DeletedText` not `Text`.
- **Spike 7** → ✅ APPLIED spec FR-24: `If-Match`/ETag is a NEW capability task 050 must ADD to the SPE write facade (not existing); catch 423/412 → typed conflict. Remaining 4 gaps for tasks 054/055 (Word-open signal via webhook/delta; pending-annotation durability decoupled from lock; wire checkout stubs; defer path). Conflict UX driven by write-back outcomes (423/412), not checkout state.

**Runtime-deferred verifications** (need deployed env / live LLM — recipes in each note): Spike 0 §6 (SSE+ledger live), Spike 4 (hallucination A/B measurement), Spike 5 (Word-for-Web native render), Spike 7 (423/412 status + Word-for-Web UX).

---

## Quick Reference

### Project Context
- **Project**: spaarkeai-compose-r2
- **Project CLAUDE.md**: [`CLAUDE.md`](./CLAUDE.md)
- **Task Index**: [`tasks/TASK-INDEX.md`](./tasks/TASK-INDEX.md) (pending)

### Applicable ADRs
- ADR-039 (dispatch/catalogs), ADR-040 (ledger), ADR-013 (AI facade) — the load-bearing three; full list in CLAUDE.md §Resources

---

## Recovery Instructions

1. **Quick Recovery**: Read the "Quick Recovery" section above
2. **If more context needed**: Read CLAUDE.md + plan.md §4
3. **Load task file**: (none yet — run task-create first)
4. **Resume**: from the "Next Action" section

**Commands**: `/project-continue` · `/context-handoff` · "where was I?"

---

*This file is the primary source of truth for active work state. Keep it updated.*
