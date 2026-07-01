# Lessons Learned — Spaarke AI Platform Chat Routing Redesign (R1)

> **Project**: `spaarke-ai-platform-chat-routing-redesign-r1`
> **Period**: 2026-06-21 (project-pipeline) → 2026-06-28 (closeout)
> **Status**: ✅ Complete
> **Author**: Task 150 wrap-up

---

## 1. Executive Summary

R1 redesigned chat routing by collapsing two parallel routers (`PlaybookDispatcher` + `CapabilityRouter`) onto one matcher driven by playbook embeddings; stabilized playbook identification via `sprk_playbookcode`-based consumer routing (the `sprk_playbookconsumer` table); wired data-driven destinations into `NodeRoutingConfig`; built a 6-tier stateful chat memory substrate (MVP-cut to 12 active tasks of 42); shipped 4 specialized playbooks; and retired `CapabilityRouter` in a single-phase cutover. All 7 work packages delivered. Branch merged via PR #491 (2026-06-25); follow-up docs via PR #509 (2026-06-28).

The project shipped its primary structural reforms cleanly. The most consequential **discovery** during execution was that the project's deliverable-shape was platform-led (refactor + substrate) rather than product-led (demoable user verbs) — which produced sophisticated infrastructure but only one new user-facing LLM tool handler (`RecallSessionFileHandler`). This finding catalyzed the **product-led development methodology pivot** captured in `docs/procedures/PRODUCT-LED-DEVELOPMENT.md` and gave birth to the first product-led successor project `spaarkeai-widget-summarize-document-r1`.

---

## 2. What surprised us

**S1. The MVP cut was the right call AND it surfaced the platform-led/product-led gap (2026-06-22, Phase 4 Q5b).**
30 of 42 Phase 4 (6-tier memory) tasks deferred mid-project. At the time it felt like scope discipline. In retrospect it exposed that the original scope was substrate-heavy and demo-thin: even shipping all 42 would have produced one new user-facing capability (matter memory promotion). The MVP cut was correct; the underlying framing was the bigger lesson.

**S2. Subject-matter-aware summarization is playbook matching, not classification (2026-06-26, late discovery).**
Initial design assumed an R1 Phase 4b `FileClassificationService` was needed to detect "this is an NDA" → route to NDA playbook. Late-stage product-led discussion revealed: `PlaybookDispatcher` Phase B vector match + multiple area-specific playbooks (`summarize-vendor-contract@v1`, `summarize-employment-agreement@v1`, etc.) IS the classification mechanism. No separate classifier service needed. This finding shrinks the follow-on product project's scope substantially — but we didn't know it until the project was effectively closed.

**S3. The asymmetric-registration anti-pattern struck during UAT (2026-06-26, GraphModule.cs:74).**
`IFieldMappingDataverseService` was wired to the stub `DataverseServiceClientImpl` instead of the real `DataverseWebApiService`. This is exactly the failure mode CLAUDE.md §10 F.1 (added 2026-06-01 by r2 task 081) warns against. Document upload `/api/ai/chat/sessions/{id}/documents` returned 500 with NotImplementedException ("QueryChildRecordIdsAsync is implemented in DataverseWebApiService. Configure DI to use Web API implementation"). The error message itself pointed at the fix; the stub author knew this would happen. Caught in our UAT, fixed in this project (commit `3119c11ef`), merged via PR #491. The defensive `throw new NotImplementedException(helpful message)` pattern saved time.

**S4. AI Search dev infrastructure deletion mid-project (2026-06-25, NXDOMAIN).**
The `spaarke-search-dev.search.windows.net` service was deleted from dev sometime during this project. Discovered when summarize follow-up Q&A returned NXDOMAIN. Sister project `spaarke-ai-azure-setup-dev-r1` (started in response) restored it 2026-06-26 with 194 docs ingested. Lesson: dev infrastructure that an active project depends on can disappear without notice; sister-project coordination needs explicit handoff.

**S5. R6 PR #401 unblocked Phase 7 with zero merge-timing friction.**
The original plan (CLAUDE.md "Related PRs" section) flagged PR #401 as blocking Phase 7 WP4 cutover. In practice: R6 closeout happened cleanly, PR #401 merged smoothly, and Q8's single-phase WP4 cutover (no parallel run) landed without coordination friction. The conservative Q8 design (no parallel run) was right — but the predicted timing risk did not materialize.

**S6. The two-pane chat substrate isn't usable until R6 Tier C Dataverse rows are seeded.**
Workspace tools (send/update/close tab) are coded correctly but silently broken when the `sprk_analysistool` rows don't exist. R6 Tier C handed this off; we accepted it in our R7-backlog. End-user impact: LLM tool calls fail silently. Surface-level discovery: requires actually running a session. Code review caught zero issues; only UAT exposed it.

---

## 3. What we'd do differently

**D1. Frame product-led from project-pipeline.** This project's spec was 45 FRs across 6 WPs, organized by technical work-package. A product-led shape would have organized by user verb ("summarize a document" / "draft an NDA" / "manage matter memory") with WPs as supporting decomposition. Tasks would have been ordered to ship one demoable verb at a time. Productization (UAT + docs + demo + sales training) would have been in-scope per verb, not deferred to task 146/150. This is exactly the methodology now captured in `docs/procedures/PRODUCT-LED-DEVELOPMENT.md`.

**D2. Identify sister-project dependencies BEFORE pipeline.** AI Search NXDOMAIN was discovered mid-execution. Dev infrastructure restoration is a multi-week project; we paid the cost of context-switching to scope it on the fly. A pre-pipeline sister-project audit ("what infrastructure must be healthy for this project's UAT to run?") would have caught it.

**D3. Verify external-system Dataverse rows during BFF deploy.** R6 Tier C surface-completion gaps (missing `sprk_analysistool` rows for workspace tools) are detectable mechanically. A startup health-log diffing `ConsumerTypes.All` vs deployed `sprk_playbookconsumer` rows is captured in this project's r7-backlog (task 028e). Similar check for `sprk_analysistool` rows vs registered handler classes would have caught the Tier C gap earlier.

**D4. Run the BFF deploy script with `pwsh` not `powershell.exe`.** Discovered 2026-05-27: `Get-FileHash` doesn't load in Windows PowerShell 5.x in certain harnesses; PowerShell 7+ (`pwsh`) auto-loads it. The hardened deploy script's hash-verify step silently failed under `powershell.exe`. Documented in `.claude/skills/bff-deploy/SKILL.md` Failure Modes — this project propagated the documentation but the failure-mode was inherited.

**D5. Don't over-scope a "redesign" — pick the structural reform, ship it, ship next.** The original spec bundled 6 structural reforms (one routing layer, stable codes, data-driven destinations, index governance, 6-tier memory, specialized playbooks) PLUS the CapabilityRouter retirement. Each is a legitimate project. Bundling them produced execution that was right-shaped technically but produced few demoable end-user changes. The product-led methodology (one demoable verb per project) addresses this directly.

---

## 4. What worked exceptionally well

**W1. The MVP cut decision discipline (Phase 4 Q5b, 2026-06-22).**
30 tasks deferred without losing structural integrity. The Cosmos doc-type schema for promotions was documented as a binding contract in spec.md; FR-45 invariant locked at `PlaybookChatContextProvider.cs:679`; ADR-030 v2 `memory` channel union locked. Future memory-subsystem project can pick up exactly where MVP cut.

**W2. Phase 1R consumer routing pattern (the `sprk_playbookconsumer` triangle).**
The `IConsumerRoutingService` + `IInvokePlaybookAi` + Dataverse routing table architecture replaced 9 hardcoded GUID/name-lookup consumers cleanly. The pattern proved so reusable that `daily-update-service-r4` task 031 adopted it for the daily-briefing rewrite (canonical Path A.5 reference). Now documented in `docs/architecture/ai-architecture-playbook-consumer-routing.md` with the Playbook-as-Orchestration-Boundary principle (added 2026-06-28 in task-150 doc work).

**W3. FR-45 invariant test (`PlaybookChatContextProviderFr45RegressionTests.cs`).**
Bound `PlaybookChatContextProvider.cs:679` (matter context invocation point) with 3 explicit binding-invariant tests: behavioral Moq.Verify, source-text grep with fail message, graceful no-matter-context path. The line had shifted +52 since architecture §11.1's documented line 627 due to XMLDoc additions; the source-text test caught this and the architecture doc was updated. Pattern is reusable for any "do not regress" invariant.

**W4. R6 alignment audit (2026-06-25).**
Cross-project structural compatibility check at end of Phase 7. Confirmed all 4 R6-marked "successor scope" items closed (P3 invoke_playbook convergence, P5 NodeRoutingConfig wiring, P7 memory composition, P8 soft-slash absorption); 0 silent regressions; 6 R6-deferred items remain in R6/R7 scope (Builder UI, ExecutionTraceWidget mount). The Explore subagent dispatch (parallel cross-project read) was the right tool — synthesis took 5 minutes vs ~30 minutes manual.

**W5. The hardened BFF deploy script (`Deploy-BffApi.ps1`).**
SHA-256 hash-verify on 6 critical files + auto-recovery via Kudu zipdeploy + 120s Linux cold-start health window. Multiple times during this project the deploy showed "success" but hash-verify caught a silent file-lock failure — the script then auto-recovered with stop/Kudu/start. The cost of the verify step (10 seconds) vs the cost of debugging a stale deploy mid-UAT (hours) is asymmetric — the verify wins every time.

**W6. Defensive helpful error messages.** The `DataverseServiceClientImpl.QueryChildRecordIdsAsync` stub message ("QueryChildRecordIdsAsync is implemented in DataverseWebApiService. Configure DI to use Web API implementation") pointed at the fix when it fired. Total diagnosis time for the GraphModule.cs:74 issue: ~5 minutes from "500 on doc upload" to "I know exactly what to change." Worth emulating.

---

## 5. Cross-cutting patterns (worth carrying forward)

**P1. Playbook-as-Orchestration-Boundary.** Confirmed in task 150 doc work: `sprk_analysisaction` rows are NEVER directly callable; Actions are always wrapped in a playbook. Schema-level enforcement via `sprk_playbookconsumer.sprk_playbookid → sprk_analysisplaybook` FK. Captured in `docs/architecture/ai-architecture-playbook-consumer-routing.md` §10.2 as a binding principle for all future surfaces (chat-side, Outlook add-in, Word add-in, Teams, Action Engine Agents, etc.).

**P2. Path A.5 (consumer routing + non-streaming facade) is the default for new BFF surfaces.** Path A (legacy doc-bound streaming) and Path B (direct orchestrator with known playbook GUID) coexist for specific cases. Decision matrix codified in the architecture doc. New surfaces should default to A.5 unless the use case requires streaming SSE.

**P3. Vector-matched area awareness >> explicit classifier service.** The dispatcher's Phase B vector match against playbook descriptions IS the classification mechanism when multiple area-specific playbooks exist. Authoring new area variants is content-not-code. This is a structural advantage over competitor architectures that hardcode area detection in prompts.

**P4. Compile-time constants vs free-text Dataverse strings.** `ConsumerTypes.cs` defends BFF-side typos; a startup health-log diffing `ConsumerTypes.All` vs deployed Dataverse rows would close the gap on Dataverse-side typos. R7-backlog item.

**P5. R6 successor pattern (load-bearing).** This project demonstrated that "wire-and-refactor of R6 substrate" is a viable project scope. The architecture doc reference (`architecture/stateful-chat-architecture.md`) provided the binding contract. Future projects that build on R6 should follow the same pattern: explicit "do not regress" invariants + architecture doc reference + R6 alignment audit at closeout.

**P6. Sister-project explicit dependency mapping.** This project's late discovery of AI Search NXDOMAIN was a process gap. Future projects should list sister-project dependencies explicitly in design.md (now done by P1 — see `projects/spaarkeai-widget-summarize-document-r1/design.md` §10.1).

---

## 6. R7 Backlog Items

Already filed during this project (`notes/r7-backlog.md`):

| ID | Origin | Item |
|---|---|---|
| Architectural debt | R6 handoff (2026-06-25) | Chat ↔ workspace write-side unification (chat-handler vs playbook-output divergent SSE paths) |
| MINOR | Task 148 adr-check | ConsumerRoutingService cache key tenant-id doc claim |
| MINOR | Task 147 code-review | PlaybookDispatcher.cs:784 user-message verbatim embed (`"` escape hardening) |
| MINOR | Task 147 code-review | IntentRerankerService.cs:335 same pattern |
| MINOR | Task 147 code-review | PlaybookCandidateSelector.cs:93-101 ContributingFileCount HashSet refinement |
| MINOR | Task 147 code-review | CapabilityRouter stale comments in CommandRouter / ConversationPane / SoftSlashRouter / HardSlashExecutor |
| MAJOR/DEFER | Task 147 code-review | PlaybookDispatcher.cs:85 process-wide `AiConcurrencyLimiter` SemaphoreSlim(10,10) — capacity planning + scoped semaphore |
| MAJOR/DEFER | Task 147 code-review | OrchestratorPromptContext `MatterName` / `ActivePlaybookName` always null — prune dead paths OR wire (P1 chose: wire, per D-12) |

Additional items surfaced during task 150 wrap-up (added to `notes/r7-backlog.md` below):

- **Phase 4 MVP-cut roadmap (30 tasks)** — formal post-MVP memory-subsystem project: 4b enrichment pipeline (6), 4c per-turn optimizations (3), 4d 7 of 8 tool handlers, 4e entire promotion workflow (7), 4f audit cleanup (2). Cosmos doc-type + FR-45 invariant locked. Estimated 4-6 weeks for a dedicated memory project.
- **Phase 6 specialized playbooks audit completion** — PB-009 / PB-012 / PB-015 / PB-017 Dataverse-level audit was marked complete but the formal audit document should be authored as a successor handoff artifact.
- **Tool registry parity health-check (CLAUDE.md §10 F.1 follow-up)** — startup health-log diffing registered `IToolHandler` classes vs deployed `sprk_analysistool` rows (parallel to ConsumerTypes diff). R6 Tier C surface-completion gap would have been caught earlier.
- **Auto-deploy gap** — current deploy is manual (`Deploy-BffApi.ps1`). A CI/CD pipeline triggered by master merge would close the staleness window. Coordinate with `ci-cd-unit-test-remediation-r1` outcomes.
- **`voyage-law-2` precision audit** — original spec accepted 6-10% precision loss by deferring `voyage-law-2` embeddings migration. UAT did not isolate precision issues, but a formal audit at follow-on project's UAT graduation would close the loop.
- **`sprk_userpreferences` field expansion (Q13)** — deferred during planning. Surface when a product project needs explicit user preferences beyond the existing cached snapshot.
- **Per-turn cache invalidation benchmark (Q14)** — deferred during planning. Useful when multi-tenant scale becomes empirically pressing.

---

## 7. Quality gate evidence

| Gate | Status | Reference |
|---|---|---|
| `/code-review` | ✅ 0 CRITICAL (Tier-1/2/3 review); 3 MAJOR (1 fixed in-line + 2 deferred to R7); 6 MINOR (1 fixed in-line + 5 deferred to R7); exit-0 PASS | Task 147 closed 2026-06-25 (`notes/handoffs/147-code-review.md`) |
| `/adr-check` | ✅ 12/12 ADRs PASS; 6/6 special-case PASS; 0 CRITICAL; 1 MINOR R7-backlog item | Task 148 closed 2026-06-25 (`notes/handoffs/148-adr-check.md`) |
| `dotnet build src/server/api/Sprk.Bff.Api/` | ✅ 0 errors, 18 warnings (all pre-existing nullable-reference / async-without-await) | Final verify 2026-06-28 task-150 |
| BFF publish size | ✅ 46.67 MB (< 60 MB ceiling; +1.02 MB net vs Phase 0 baseline 45.65 MB; absorbs master merges from sister projects) | `notes/handoffs/final-publish-size-summary.md` |
| `dotnet test` | ✅ 7,818/7,818 pass (pre-mass-deletion) | Recorded 2026-06-25 |
| R6 alignment audit | ✅ CLEAN — 0 silent regressions; 4/4 successor items closed | 2026-06-25 explore-subagent dispatch |
| UAT regression (task 146) | 🟡 PARTIAL/WAIVED — 15/17 FRs verified via library-modal route ad-hoc UAT 2026-06-26; full regression rolled into successor project | TASK-INDEX task 146 row |

---

## 8. Project artifacts inventory

**Code commits** (selected high-impact):
- Phase 0 cleanup → Phase 7 retirement: see git log `e2d8d1e..bf866e7d5`
- GraphModule.cs:74 DI fix: commit `3119c11ef`
- Daily Briefing endpoint rewrite (merged from master via task-031): commit `88dd66a1c`

**Branches**:
- `work/spaarke-ai-platform-chat-routing-redesign-r1` → merged to master via **PR #491** (2026-06-25)
- Follow-up docs → merged via **PR #509** (2026-06-28)

**Documents on master** (added during this project or its task-150 wrap-up):
- [`docs/architecture/ai-architecture-playbook-consumer-routing.md`](../../../docs/architecture/ai-architecture-playbook-consumer-routing.md) — renamed + augmented with §10.2 Playbook-as-Orchestration-Boundary principle
- [`docs/data-model/sprk-playbookconsumer.md`](../../../docs/data-model/sprk-playbookconsumer.md) — new schema reference
- [`docs/guides/HOW-TO-ADD-A-CONSUMER-ROUTING-TYPE.md`](../../../docs/guides/HOW-TO-ADD-A-CONSUMER-ROUTING-TYPE.md) — new operational guide
- [`docs/procedures/PRODUCT-LED-DEVELOPMENT.md`](../../../docs/procedures/PRODUCT-LED-DEVELOPMENT.md) — new methodology doc

**Architecture documents** (binding contracts authored by this project):
- [`architecture/stateful-chat-architecture.md`](../architecture/stateful-chat-architecture.md) — 6-tier memory model + Insights reuse boundary

**Successor project**:
- [`projects/spaarkeai-widget-summarize-document-r1/`](../../spaarkeai-widget-summarize-document-r1/) — first product-led project under the new methodology. Design committed; ready for `/design-to-spec` → `/project-pipeline`.

---

## 9. Owner sign-off

| Field | Value |
|---|---|
| **Project** | `spaarke-ai-platform-chat-routing-redesign-r1` |
| **Completed Date** | 2026-06-28 |
| **Wrap-up Task** | 150 (this artifact) |
| **Successor Project** | `spaarkeai-widget-summarize-document-r1` (first product-led project) |
| **Methodology Captured** | `docs/procedures/PRODUCT-LED-DEVELOPMENT.md` |

*Project closed.*
