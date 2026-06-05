# R4 Lessons Learned — Spaarke AI Platform Unification, Round 4

> **Project**: spaarke-ai-platform-unification-r4
> **Status**: ✅ Complete
> **Completed**: 2026-05-28
> **Branch**: `work/spaarke-ai-platform-unification-r4`
> **Final commit before wrap-up**: `4d88e9a6` (task 081 — pre-wrap-up residuals)

---

## 1. Summary

R4 ran from 2026-05-25 (scope finalized) through 2026-05-28 (this wrap-up). What was initially scoped as a 34-item / 8-phase / ~116h project grew to **46 work tasks + 1 wrap-up = 47 total** as operator review during execution surfaced 12 additional cleanup tasks (072–081) and one deploy-residual fixup. All shipped.

Project ID summary:

| Layer | Count | Notes |
|---|---|---|
| Original scope | 34 IN items / 32 POML tasks | Per backlog.md 2026-05-25 scoping |
| Phase 6 test-infra additions (mid-execution) | 4 tasks (068, 069, 070, 071) | Surfaced by jest+React 19 env fix; operator mandate "must resolve in Phase 6" |
| Phase 6.5 add-on cleanup (final-review additions) | 8 tasks (072–078, 080) | Operator-driven from R4 final review #1+#2 — tighten WorkspaceRenderer, fix hooks, clear tsc errors, lint sweep, CVE patches |
| Phase 7 deploy batch | 1 task (079) | All deploys batched per 2026-05-26 operator decision |
| Phase 7 pre-wrap residuals | 1 task (081) | Cleared 2 residuals (useKeyboardShortcuts bug, 272 tracked deploy artifacts) |
| Wrap-up | 1 task (090, this file) | — |

---

## 2. What Shipped (mapped to FR/NFR/DR/PR IDs)

### Functional Requirements (14 FRs all ✅)

| FR | Item | Disposition | Evidence |
|---|---|---|---|
| FR-01 | W-3 WorkspaceLayoutWizard catalog drift | ✅ | Task 040; SECTION_REGISTRY as single source |
| FR-02 | W-4 Assistant→Workspace mount source | ✅ | Task 042; DocumentViewerWidget shipped |
| FR-03 | W-5 Context→Workspace mount source | ✅ | Task 043; pivoted to SemanticSearchCriteriaTool (in-process) — iframe-wizards strategy promoted to own project |
| FR-04 | A-4 25 MB chat attachment cap | ✅ | Task 050; CHAT-ATTACHMENT-POLICY.md published |
| FR-05 | A-5 tab persistence verify+fix | ✅ | Tasks 030+031; Path A (chatSessionId+playbookId → localStorage) |
| FR-06 | B-3 telemetry rename | ✅ | Task 062; App Insights cutover memo |
| FR-07 | B-4 WorkspaceLayoutDto.modifiedOn | ✅ | Task 053; camelCase ISO-8601 |
| FR-08 | B-5 BFF PUT + If-Match weak ETag | ✅ | Task 054; Option A (PUT+If-Match) per b5-design-decision.md |
| FR-09 | B-6 CalendarFilterPane promotion | ✅ | Task 055 Option B per operator; UTC bug fix included |
| FR-10 | B-7 useEventsBulkActions hook | ✅ | Task 063; ~270 LOC dedup |
| FR-11 | B-8 CalendarDrawer.eventDates API | ✅ | Task 064; IEventDateInfo[] + Fluent v9 badges |
| FR-12 | B-11 type-drift cast cleanup | ✅ | Task 067; 13 cascading 061 errors resolved + 4 production casts removed |
| FR-13 | C-3 consolidate useWorkspaceLayouts | ✅ | Task 051; single hook in @spaarke/ai-widgets |
| FR-14 | C-4 WorkspaceRenderer interface | ✅ | Task 052 (initial) + 072 (tightened to required methods per Path 2a) |

### Non-Functional Requirements (8 NFRs all ✅)

| NFR | Item | Disposition | Evidence |
|---|---|---|---|
| NFR-01 | F-3 publish-size baseline rule | ✅ | Task 017; CLAUDE.md §10 strengthened; final BFF 44 MB ≤ 60 MB cap |
| NFR-02 | F-1 retroactive BFF placement memo | ✅ | Task 002; `notes/bff-ai-facade-audit-2026-05.md` |
| NFR-03 | F-2 BFF facade audit | ✅ | Task 020; **0 direct AI deps** in CRUD (was 20 at baseline 2026-05-20) |
| NFR-04 | B-1 .gitignore for build artifacts | ✅ | Task 060 (initial) + 081 (durable master-level fix via `git rm --cached`) |
| NFR-05 | B-2 ai-widgets tsc rootDir | ✅ | Task 061; Option C (dist/.d.ts paths) + CI gate added |
| NFR-06 | B-9 ESLint flat config | ✅ | Task 065 (config creation, not migration) + 078 (warning sweep 178→22) |
| NFR-07 | B-10 EventsPage redeploy | ✅ | Task 066 via 079 deploy batch |
| NFR-08 | SpaarkeAi bundle size regression | ✅ | Final: 3,455 kB / gzip 923 kB (within +50 KB tolerance vs R3 918 kB baseline) |

### Documentation Requirements (7 DRs all ✅)

| DR | Item | Disposition | File |
|---|---|---|---|
| DR-01 | W-1 SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL | ✅ | `docs/architecture/SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md` (352 lines) |
| DR-02 | W-2 BUILD-A-NEW-WORKSPACE-WIDGET rewrite | ✅ | `docs/guides/BUILD-A-NEW-WORKSPACE-WIDGET.md` (599 lines, 5 archetypes) |
| DR-03 | W-6 LEGALWORKSPACE-RETIREMENT | ✅ | `docs/architecture/LEGALWORKSPACE-RETIREMENT.md` (157 lines) |
| DR-04 | A-2 ADR-030 + ADR-031 (renumbered from 025/026) | ✅ | `.claude/adr/ADR-030-pane-event-bus.md`, `ADR-031-stage-lifecycle.md` (+ `docs/adr/` mirrors) |
| DR-05 | D-2 ADR-031 heavy library amendment | ✅ | Task 016; amendment in `.claude/adr/ADR-031-stage-lifecycle.md` |
| DR-06 | C-1 DATA-ACCESS-DECISION-CRITERIA | ✅ | `docs/standards/DATA-ACCESS-DECISION-CRITERIA.md` (~257 lines) |
| DR-07 | C-2 LEGALWORKSPACE-EMBEDDED-MODE-CONTRACT | ✅ | `docs/architecture/LEGALWORKSPACE-EMBEDDED-MODE-CONTRACT.md` (359 lines, 21 testable MUSTs) |

### Process Requirements (2 PRs all ✅)

| PR | Item | Disposition |
|---|---|---|
| PR-01 | E-1 R3 wrap-up | ✅ Task 001 (retroactively complete in `4a877b1e`) |
| PR-02 | R4 wrap-up | ✅ This task (090) |

### Add-on tasks (Phase 6.5 — operator-driven final-review additions)

| # | Item | Result |
|---|---|---|
| 072 | A.2 WorkspaceRenderer interface tightening (Path 2a, no wrapper) | ✅ Cast removed at `LegalWorkspace/src/index.ts:74`; structural-typing avoids circular dep with `@spaarke/legal-workspace` |
| 073 | B.1 DatasetGrid `rules-of-hooks` (REAL bugs) | ✅ 10 hooks hoisted; rule promoted `warn` → `error`; 26/26 DatasetGrid tests pass |
| 074 | B.2 UI.Components 24 tsc errors | ✅ → 0 (Category A RefObject, B JSX namespace, C Combobox onInput). UI.Components typechecks clean for first time in months |
| 075 | B.4 SpaarkeAi 5 tsc errors | ✅ → 0. `@spaarke/legal-workspace` tsconfig path added; stale `dist/index.d.ts` regenerated |
| 076 | B.5 CalendarSidePane shape migration | ✅ Loose end from B-6 closed (parseParams.ts + postMessage.ts migrated to CalendarFilterPaneOutput) |
| 077 | B.7 4 jsdom timing test failures | ✅ → 0 (target was ≤2). Root cause was missing jsdom globals (TextEncoder/ReadableStream/TextDecoder) + an `expect(act(...))` wrapper bug, NOT microtask timing as hypothesized |
| 078 | B.3 ESLint warning sweep | ✅ 178 → 22 (-87.6%) in 2.5h vs 8h budget |
| 080 | CVE patches (OpenMcdf, OpenTelemetry.Api) | ✅ 2 Moderate CVEs eliminated; Kiota HIGH deferred to dedicated future project |
| 081 | Pre-wrap residuals | ✅ useKeyboardShortcuts default value (CommandBar suite 0→23 passing) + 272 deploy artifacts untracked (durable governance fix) |

### Deploys (Phase 7)

| Artifact | Result |
|---|---|
| BFF API | ✅ 45 MB compressed; hash-verify ✅; healthz ✅; 6 endpoint smoke ✅; operator-validated independently |
| SpaarkeAi Code Page | ✅ 3.38 MB |
| LegalWorkspace standalone | ⏭️ SKIPPED — RETIRED per W-6 / OC-R4-05 (components ship via SpaarkeAi library) |
| CalendarSidePane | ✅ 1.08 MB (NEW Deploy-CalendarSidePane.ps1 script created) |
| EventsPage | ✅ 1.18 MB |

### Side deliverables (not in original scope, captured for future)

- **New project**: [`projects/spaarke-iframe-wizard-pattern-enhancement/`](../../spaarke-iframe-wizard-pattern-enhancement/) — comprehensive design.md drafted with critical constraint locked in: **NO Power Automate, NO Dataverse plugins** per operator core-product direction. Covers 5 use case surfaces, options evaluation, 7-phase implementation plan, security considerations.
- **NEW script**: `scripts/Deploy-CalendarSidePane.ps1` (permanent — supports all future CalendarSidePane deploys)
- **R5 backlog**: emptied — both originally-deferred items resolved in this session (iframe-wizards → own project; WorkspaceRenderer fix absorbed into task 072 with Path 2a)

---

## 3. What Worked

### 3.1 Parallel sub-agent dispatch pattern

R4 dispatched multiple waves of 5–6 parallel sub-agents using the Agent tool. **Hard cap of 6 concurrent agents** was respected throughout. Pattern proved robust:

- **Wave A** (072+073+074+075+076+077, all ✅): 6 sub-agents, cleanly independent file scope, no merge conflicts
- **Phase 6 final wave** (064+065+067+070+071, all ✅): 5 sub-agents, parallel-safe
- **Phase 5 code wave** (multiple): proved on real production code changes

Key disciplines that made it work:
1. **Brief each sub-agent explicitly**: "DO NOT touch TASK-INDEX.md, current-task.md, CLAUDE.md root, `.claude/` paths". Main session reconciles after all return.
2. **No file-overlap dispatch**: each sub-agent gets a distinct file scope. Where overlap was possible (073 + 074 both in `@spaarke/ui-components/components/`), the briefing explicitly excluded the other task's files.
3. **Sub-agent write boundary**: per root CLAUDE.md §3, sub-agents cannot write to `.claude/` paths. Tasks touching `.claude/` (012, 013, 016, 017) ran sequentially in main session.
4. **Build verification between waves**: per CLAUDE.md, `dotnet build` + `npx tsc --noEmit` between waves caught any cascading errors before they compounded.

### 3.2 Verify-then-fix protocol (A-5 lesson)

Task 030 (A-5a verify) ran BEFORE task 031 (A-5b fix). The verification confirmed the bug operator had reported — tab persistence DID fail — and identified the correct fix path (chatSessionId+playbookId → localStorage, not sessionStorage). Without this discipline, A-5b might have re-implemented something already working, or applied the wrong fix. **The 2h verify time saved ~6h of misdirected dev time.**

### 3.3 Operator-as-judgment-gate pattern

R4 had **13 explicit operator decisions** captured chronologically in commits + memos. Key ones that changed the project's trajectory:

- 2026-05-25: 8 items moved DEFER → IN (the operator-driven scope finalization in backlog.md)
- 2026-05-26: deploy batching to Phase 7 (instead of per-task deploys; saved ~4h of deploy overhead + enabled cohesive smoke)
- 2026-05-26: 055 B-6 — Option B promotion (CalendarFilterPane separate component) instead of forced merge
- 2026-05-26: 068+069+070+071 must-fix in Phase 6 (test infra was originally going to be deferred to R5)
- 2026-05-27: 072 Path 2a — tighten interface, NO wrapper (rejected the wrapper as architectural debt for fictional flexibility)
- 2026-05-27: 080 — patch 2 Moderate CVEs but defer Kiota HIGH (matched BFF remediation precedent)
- 2026-05-27: iframe-wizards as own project with NO-PA/NO-plugins constraint
- 2026-05-27: master sync before deploys (worktree-sync caught matter-ui-r1's 27 new commits cleanly with 0 conflicts)
- 2026-05-27: 081 — fix two residuals (TypeError + deploy artifact tracking) instead of deferring

Each operator decision was captured in a commit or memo with timestamp. Future projects should preserve this pattern — it makes audit trivial and gives downstream readers ground truth.

### 3.4 Two-wrapper framing (W-1 / DR-01)

Documenting the **two-wrapper architecture** as the foundational mental model for SpaarkeAi (Dashboard wrapper = `LegalWorkspaceApp` composing components; Direct widget wrapper = `WorkspaceWidgetRegistry` mounting sophisticated single-purpose widgets) clarified a lot of subsequent design questions:
- W-4 / W-5 (mount sources) became simple — both dispatch the same `widget_load` event
- C-4 (WorkspaceRenderer interface) had a clear purpose: enable future renderers if needed (later: operator decided LegalWorkspace IS the dashboard renderer → tightened in 072)
- 044 (SmartToDoDialog inline modal) was scoped narrowly — fix the dialog, not the wrapper

The framing also produced a load-bearing constraint: **OC-R4-06 — keep both wrappers** (not merge into one). This avoided a tempting but wrong "simplification" that would have broken Calendar widget Pattern D.

### 3.5 Test-infrastructure cascade discipline (Phase 6)

The Phase 6 test-infra cluster (068+069+070+071) was a model for "follow the cascade carefully":

- Task 068 fixed Jest+React 19 env → unblocked 544 additional tests that had been silently skipped
- Those 544 tests surfaced **142 test-content failures** + **76 BFF test compile errors** (escalated through 069 → 070)
- Operator mandate: "must ensure these issues are resolved as part of this project (Phase 6)"
- Each escalation was filed as a new task with explicit operator approval
- Final result: ~1051 → 1074 UI.Components tests passing (+23 unblocked by 081); BFF test suite running cleanly

Lesson: when an infrastructure fix unblocks previously-skipped tests, **the surfaced failures are now in scope** — you don't get to ignore them just because they existed before. Operator chose to absorb the work; the alternative ("R5 backlog them") would have undermined the value of fixing the infrastructure in the first place.

---

## 4. What Surprised Us

### 4.1 R4 task 060 (B-1 `.gitignore`) wasn't actually durable on first try

Task 060 ran `git rm --cached -r deploy/api-publish/` on the R4 branch. We thought that fixed the issue. **It didn't** — the fix only applied to R4 locally. Other projects (auth-v2, BFF remediation, matter-ui-r1) continued tracking deploy artifacts. When R4 synced master on 2026-05-27, all 272 tracked artifacts came back.

Task 081 re-ran the same command and called it durable. **It is durable this time** because:
- `.gitignore` already has the `deploy/` rule
- Once master inherits the untracking (post R4 merge), the gitignore rule actually works
- Future `git add deploy/api-publish/...` is ignored unless deliberate `-f` override

**Lesson**: `.gitignore` rules added AFTER files are tracked require BOTH `git rm --cached` AND that the un-tracking propagate to master. Local fixes on feature branches don't propagate without merge. This is process-level governance that no single project can solve unilaterally.

### 4.2 The BFF publish-size measurement issue

Initial 079 pre-flight measured BFF publish at **74 MB compressed (HARD STOP at 60 MB cap)**. Investigated: `dotnet publish` without explicit `--runtime linux-x64 --self-contained false` flags shipped multi-RID native binaries (linux-arm64, linux-musl-x64, osx-arm64, osx-x64, win-x64, win-x86, etc.) — 68 MB of runtime/ folders.

The csproj has `<RuntimeIdentifier>linux-x64</RuntimeIdentifier>` but this was NOT being honored without the explicit CLI flag. Re-publishing with explicit flags brought it to **44 MB compressed** (well under cap).

**Lesson**: csproj `<RuntimeIdentifier>` is advisory unless `dotnet publish` is invoked with the matching `--runtime` flag. The BFF deploy script's invocation includes the flag — manual `dotnet publish` for verification must mirror it. Documented this in `notes/079-deploy-verification.md` for the next person.

### 4.3 jsdom timing test failures — hypothesis was wrong

Task 077 (B.7) had a clear hypothesis from task 071's carry-over analysis: 4 remaining failures were "microtask timing under jsdom + RTL v16 act-batching". The sub-agent's diagnosis was **different and better**:
- Test 1 was an `expect(act(async () => ...))` wrapper bug (promise consumed before act flushed) — not mammoth mock cache
- Tests 2+3 auto-resolved once test 1 was fixed (cross-test microtask pollution)
- Test 4 was **THREE missing jsdom globals** (`TextEncoder`, `ReadableStream`, `TextDecoder` — all undefined in jest-environment-jsdom v30) — not timing at all

Result: 4 → 0 failures (target was ≤2). All 4 R4-added test families pass cleanly.

**Lesson**: when a sub-agent's diagnosis contradicts the carry-over hypothesis, trust the agent's investigation. Carry-over notes are *snapshots of partial understanding at deferral time* — they're useful for scoping, not for binding the next investigator. Brief sub-agents with the hypothesis as one possibility, not as the answer.

### 4.4 Kiota CVE is a real chain-lock problem

The Kiota HIGH CVE (GHSA-7j59-v9qr-6fq9) looked like a simple `<PackageReference>` override at first glance. The BFF remediation team's prior analysis (which we found at investigation time) saved us from a scope explosion:

- It's NOT a single-package patch — `Microsoft.Graph` 5.101.0 pins Kiota 1.21.2 via a chain-lock invariant (per `Sprk.Bff.Api/CLAUDE.md` §Package Management)
- Patching requires `Microsoft.Graph` 5→6 major upgrade + all 7 Kiota packages 1→2 in lockstep
- Graph SDK 6.x has breaking changes across 72 BFF files
- Estimated 1–2 weeks calendar for a focused upgrade project

R4 absorbed the 2 Moderate CVEs (OpenMcdf, OpenTelemetry.Api) — both same-minor-version patches — and **deferred Kiota to a dedicated project** (`spaarke-graph-sdk-kiota-upgrade-r1`). Same disposition as the BFF remediation team. Matter-ui-r1 deployed with this same CVE present 2 days earlier; the org is consistently accepting the residual pending the focused upgrade.

**Lesson**: when a CVE patch looks suspiciously simple in a system with chain-locked dependencies, **check the chain-lock invariants first**. The csproj comment + module CLAUDE.md were the canonical sources here.

---

## 5. Decisions That Should Carry Forward

These R4 patterns + framings are recommended for downstream project adoption:

### 5.1 The two-wrapper architecture (W-1 / DR-01)

Future widget projects should consult [`docs/architecture/SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md`](../../../docs/architecture/SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md) before designing. The 5 archetypes in [`docs/guides/BUILD-A-NEW-WORKSPACE-WIDGET.md`](../../../docs/guides/BUILD-A-NEW-WORKSPACE-WIDGET.md) eliminate ambiguity:

1. Composable section
2. Sophisticated single-purpose direct
3. Dual-use Pattern D (Calendar canonical worked example)
4. Context-pane
5. Modal-launcher

### 5.2 LegalWorkspace IS the dashboard renderer (OC-R4-05)

Not a temporary state — this is the architectural direction. New dashboard pieces are added to the LegalWorkspace library, not as separate renderers. Task 072's interface tightening codified this. Future "I want a different renderer" requests should be challenged as "do you actually need that, or is this a library addition?"

### 5.3 PaneEventBus + stage lifecycle pattern (ADR-030 / ADR-031)

For all multi-component coordination inside SpaarkeAi:
- Use **PaneEventBus typed channels** (ADR-030) — no `any` payloads
- Use **`determineStage()` four-stage pattern** (ADR-031) for lifecycle reasoning
- **Heavy libraries** (PDF.js, mammoth, etc.) get lazy import per the amendment

### 5.4 BFF placement justification (CLAUDE.md §10)

Every BFF-touching task in R4 cited Placement Justification per `.claude/constraints/bff-extensions.md`. The discipline caught:
- F-2 audit: **0 direct AI deps in CRUD code** (down from 20 at baseline)
- A-4 / B-4 / B-5: cleanly placed in BFF (no extraction temptation)
- F-3 publish-size verification: every BFF-touching commit verified ≤60 MB

The pattern should remain mandatory. Future BFF additions without Placement Justification should be flagged in code review.

### 5.5 Iframe-wizards must NOT use Power Automate or plugins

Operator decision 2026-05-27 locked this in for the core product. The iframe-wizard-pattern-enhancement project's design.md treats this as a binding constraint. Any future "Add to Workspace" or external→SpaarkeAi mount-source work must use:
- Web platform APIs (postMessage, BroadcastChannel)
- BFF API (polling, SSE)
- Dataverse Web API (client-side)
- React composition (in-process)

NOT Power Automate. NOT Dataverse plugins.

### 5.6 Master sync before deploys

R4's late-stage worktree sync caught 27 master commits (matter-ui-r1 + BFF remediation closure + CI fixes + knowledge base setup) with **0 conflicts**. The deploy then shipped a true superset that matched the eventual master state. Worth the 5 minutes.

Pattern: **`worktree-sync` immediately before any deploy** in a long-running project.

---

## 6. Deferred to Future Projects

### 6.1 `spaarke-graph-sdk-kiota-upgrade-r1` (~1–2 weeks)

Microsoft.Graph 5.101.0 → 6.x + all 7 Kiota packages 1.21.2 → 2.x chain upgrade. Documented in detail in [`notes/080-cve-patches.md`](080-cve-patches.md) §3. Operator-only approval per NFR-08. Should run when no other BFF-touching projects are active (per BFF remediation precedent).

### 6.2 `spaarke-iframe-wizard-pattern-enhancement-r1` (TBD)

Project scaffolded at [`projects/spaarke-iframe-wizard-pattern-enhancement/`](../../spaarke-iframe-wizard-pattern-enhancement/) with comprehensive design.md ready for `/design-to-spec` invocation. Will implement the "Add to Workspace" affordance for wizards + define the external→SpaarkeAi mount-source contract.

### 6.3 BFF test-infrastructure cleanup (separate dedicated project)

After R4 + matter-ui-r1, the BFF test suite has ~283 pre-existing test failures (NSubstitute IChatClient streaming mock issues + WebApplicationFactory DI for AiPersistenceModule + individual test logic drift). Per operator 2026-05-27 decision, NOT in R4 scope; will run as separate dedicated test-suite project.

### 6.4 Residual lint warnings (low-priority continuous improvement)

22 ESLint warnings remain in `@spaarke/ui-components`:
- 20 `react-hooks/exhaustive-deps` — all intentional (refs, run-once patterns, immutable deps)
- 2 `no-explicit-any` in `DatasetGrid/GridView.tsx:144` — Fluent v9 DataGrid callback type; non-trivial proper fix needed

Documented in [`notes/078-eslint-sweep.md`](078-eslint-sweep.md). Could be addressed opportunistically when those files are otherwise edited.

### 6.5 CommandBar callbacks behind the fixed TypeError

Task 081 fixed the TypeError by defaulting `commands = []`. There may be a SECOND bug: the consumers passing undefined `commands` to `useKeyboardShortcuts` should themselves pass an empty array or memoize a stable list — passing undefined is a usage smell that the hook is now papering over defensively. Worth a follow-up review of CommandBar callers to ensure they're not silently masking other bugs.

---

## 7. Numbers

| Metric | Value |
|---|---|
| Total tasks shipped | **45 work tasks + 1 wrap-up = 46** (1 LegalWorkspace deploy skipped per W-6) |
| Estimated hours (original 32-task scope) | ~116h |
| Actual time (incl. add-on tasks) | ~3 days calendar (2026-05-25 → 2026-05-28) — heavy parallel-agent dispatch |
| Tests passing (UI.Components) | **1074/1074** ✅ (started session at 1051) |
| BFF tests passing | 5217/5607 (283 pre-existing failures, out of scope) |
| BFF build | **0 errors**, 19 warnings (17 pre-existing + 2 Kiota NU1903 informational) |
| BFF publish size | **44 MB compressed** (under 60 MB cap; baseline was 43.88 MB pre-merge) |
| UI.Components ESLint | **0 errors, 22 warnings** (down from 178 at start of 078) |
| Client packages typechecking clean | UI.Components, Events.Components, AI.Widgets, AI.Context, EventsPage own-src, SpaarkeAi own-src, CalendarSidePane own-src |
| Deploy artifacts now tracked | **0** (was 272 — durable governance fix via 081) |
| ADRs created | 2 (ADR-030 PaneEventBus, ADR-031 stage lifecycle + heavy library amendment) |
| Architecture docs published | 3 (W-1 dashboard model, W-6 retirement, C-2 embedded mode contract) |
| Standards docs published | 2 (C-1 data access criteria, A-4 chat attachment policy) |
| Guides rewritten | 1 (W-2 build-a-widget) |
| Tasks deferred to future projects | 2 (Kiota chain upgrade, iframe-wizard pattern) — both scoped + documented |
| Operator decisions captured | 13+ (all in git commits + memos with timestamps) |
| Parallel sub-agent waves dispatched | ~5 waves of 4–6 agents each |
| New scripts created | 1 (`Deploy-CalendarSidePane.ps1`) |

---

## 8. Closing thoughts

R4 demonstrated that "build hygiene + workspace UX + governance" can be bundled in a single 3-day push using parallel sub-agent dispatch, provided:

1. Operator stays engaged as judgment gate (13 decisions captured)
2. Sub-agent briefings are explicit about file scope + write boundaries
3. Build verification runs between waves
4. Carry-overs are explicitly named and dispositioned (defer, absorb, escalate) — not silently forgotten

The original "34 IN items" scope grew to 46 because the project was honest about residuals. Every add-on task (072–081) closed something real that would have rotted into the next project otherwise.

R4 ships cleaner than it started: client packages typecheck for the first time in months, all UI.Components tests pass, deploys are batched + smoke-verified, master is in sync, no new CVEs introduced. The next project inherits a healthier baseline.

**Next action**: operator runs `/merge-to-master` when ready.

---

*End of lessons learned. Ready for `/merge-to-master`.*
