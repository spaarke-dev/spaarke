# Project Plan: Spaarke Daily Update Service R4

> **Last Updated**: 2026-06-26
> **Status**: Complete
> **Spec**: [spec.md](spec.md)
> **Result**: All 6 phases complete; 46/46 tasks ✅; all 20 FRs delivered; PR #456 graduation-ready; tag `daily-briefing-r4-complete`

---

## 1. Executive Summary

**Purpose**: Close four structural defects surfaced by R3 UAT — hallucination in AI narration, dead-code preferences, missing JPS deployment of the membership Action primitive, and 2 stub notification playbooks — while preserving every R3 deliverable.

**Scope**:
- W0 JPS deployment layer (4 Action rows + 1 playbook + 7-playbook reconciliation + 1 new C# executor)
- W1 Producer enrichments (customData schema + sprk_category dual-write + 2 stub implementations + membership migration of 2 playbooks)
- W2 Consumer redesign (/narrate → playbook dispatch + cache fix + empty-narrative fallback + preferences wiring + three-dot overflow menu UX + link click → modal + minConfidence removal)

**Timeline**: ~65 engineering hours across 5 phased PRs | **Estimated Effort**: 8–10 working days

---

## 2. Architecture Context

### Design Constraints

**From ADRs** (must comply):
- **ADR-013** (BFF AI Architecture): `/narrate` playbook dispatch follows established `AnalysisOrchestrationService` pattern. No new AI endpoints introduced beyond what JPS Actions/Playbooks naturally compose.
- **ADR-021** (Fluent v9 Design System): Three-dot overflow menu uses Fluent v9 `Menu` / `MenuItem` / icons; dark-mode required; semantic tokens only (no raw hex).
- **ADR-024** (sprk_todo Polymorphic Resolver): Preserved `useInlineTodoCreate` + `TODO_REGARDING_CATALOG` in overflow menu's "Add to To Do" action.
- **ADR-027** (Subscription Isolation): `appnotification` is CORE entity. `sprk_briefingstate` Choice column (R3) preserved. No new schema in R4.
- **ADR-028** (Spaarke Auth v2): Contact ↔ SystemUser cross-ref via `azureactivedirectoryobjectid` is the canonical mapping mechanism.
- **ADR-034** (User-record Membership): `LookupUserMembership` ActionType 52 is the canonical primitive. R4 deploys its missing Action row.
- **ADR-001** (Minimal API): `/narrate` wrapper follows existing endpoint-filter convention.
- **ADR-010** (DI Minimalism): NodeExecutor registration via focused module extension.
- **ADR-029** (BFF Publish Hygiene): Publish-size + CVE verification on every BFF-touching task.
- **ADR-037** (Multinode Output Composition): `DAILY-BRIEFING-NARRATE` playbook uses parallel/serial node patterns.

**From Spec / CLAUDE.md §10 BFF Hygiene** (binding):
- Every BFF-touching task MUST verify publish-size delta + run CVE check.
- BFF AI extraction assessment 2026-05-20 governs placement — extending NodeExecutor pattern is the explicit recommended path; NEW endpoints route through `Services/Ai/PublicContracts/` facade.
- §F.1 / §F.2 / §F.3 asymmetric-registration sub-mechanisms apply to any `*Module.cs` DI changes.

### Key Technical Decisions

| Decision | Rationale | Impact |
|----------|-----------|--------|
| `EntityNameValidator = 141` ExecutorActionType | Slots into post-LLM cluster (130/140); confirmed by owner | Edit `INodeExecutor.cs` enum; deploy 1 Action row |
| Use `sprk_category` column (not `customData.category`) for filter queries | Dataverse OData does NOT support `$filter` on nested JSON | FR-17c uses `sprk_category not in (…)`; FR-6 dual-writes column + JSON |
| Investigate `sprk_playbookconsumer` dispatch path before final design | Recently-built infrastructure may already support widget-as-consumer | Task 030 investigates; AC-12c records decision + rationale |
| Repo JSON files = canonical source-of-truth for playbook reconciliation | Easier audit + version control | W0 reconciliation reads deployed → compares against repo → redeploys from repo if divergent |
| Develop in parallel with R3 PR #451 (don't block on R3 merge) | Spec line 305: explicit author intent | Document file overlaps in notes/risks.md; run conflict-check per W2 PR |

### Discovered Resources

**Applicable Skills** (auto-discovered):
- `.claude/skills/jps-action-create/` — deploy 4 new Action rows (W0)
- `.claude/skills/jps-playbook-design/` — author + deploy `DAILY-BRIEFING-NARRATE` playbook (W0)
- `.claude/skills/jps-playbook-audit/` — reconcile `sprk_configjson` for 7 deployed playbooks (W0)
- `.claude/skills/jps-validate/` — validate each JPS document before deployment
- `.claude/skills/jps-scope-refresh/` — refresh PlaybookBuilder scope catalog post-deploy
- `.claude/skills/fluent-v9-component/` — three-dot overflow menu authoring (W2)
- `.claude/skills/bff-deploy/` — W0.1 + W2 BFF changes
- `.claude/skills/dataverse-deploy/` — W0 JPS Action + Playbook rows
- `.claude/skills/dataverse-mcp-usage/` — `mcp__dataverse__read_query` for verification + schema operations
- `.claude/skills/code-review/` — FULL rigor on all W0/W1/W2 code-change tasks
- `.claude/skills/adr-check/` — validate against applicable ADRs
- `.claude/skills/ui-test/` — Jest + visual testing for W2 consumer changes
- `.claude/skills/conflict-check/` — before each PR merge (R3 PR #451 overlap)
- `.claude/skills/merge-to-master/` — final safety merge with build verification

**Patterns**:
- `.claude/patterns/ai/node-executor-authoring.md` — Singleton-with-Scoped-dependency DI; mandatory test cases (`Validate_Missing*`, `ExecuteAsync_HappyPath`, `ExecuteAsync_UsesScopePerInvocation`)
- `.claude/patterns/ui/fluent-v9-component-authoring.md` — Griffel CSS-in-JS, semantic tokens, mergeClasses, dark-mode theming
- `.claude/patterns/api/endpoint-definition.md` — `/narrate` endpoint shape (request/response mapping)
- `.claude/patterns/testing/unit-test-structure.md` — xUnit fixture pattern for NodeExecutor tests
- `.claude/patterns/dataverse/polymorphic-resolver.md` — ADR-024 `TODO_REGARDING_CATALOG` preservation

**Constraints** (binding):
- `.claude/constraints/bff-extensions.md` — §A MUST checklist, §F test update obligation, §F.1/F.2/F.3 asymmetric-registration sub-mechanisms
- `.claude/constraints/azure-deployment.md` — publish-size baseline + CVE baseline
- `.claude/constraints/ai.md` — AI feature governance, JPS authoring compliance
- `.claude/constraints/testing.md` — xUnit + Jest expectations; `jest-environment-jsdom` for widget tests

**Guides**:
- `docs/guides/JPS-AUTHORING-GUIDE.md` — Action row schema definition, `sprk_systemprompt` structure
- `docs/guides/AI-DEPLOYMENT-GUIDE.md` — environment-specific configuration, playbook activation checklist
- `docs/guides/AI-MONITORING-DASHBOARD.md` — Application Insights query patterns for `hallucination_detected` events
- `docs/standards/DATA-ACCESS-DECISION-CRITERIA.md` — Xrm.WebApi vs BFF (W2 fetchNotifications uses Xrm.WebApi)

**Reusable Code (analogs for new EntityNameValidator triplet)**:
- `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/LookupUserMembershipNodeExecutor.cs` — sibling executor (ActionType 52); template for new executor
- `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/QueryDataverseNodeExecutor.cs` — Validate() + ExecuteAsync() canonical signatures
- `src/client/code-pages/PlaybookBuilder/src/components/properties/LookupUserMembershipForm.tsx` — template for `EntityNameValidatorForm.tsx`
- `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Nodes/LookupUserMembershipNodeExecutorTests.cs` — template for new executor tests
- `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Nodes/CreateNotificationNodeExecutorTests.cs` — model for FR-6 customData enrichment fixture
- `projects/spaarke-daily-update-service/notes/playbooks/notification-new-documents.json` — canonical migrated playbook (LookupUserMembership reference); template for tasks-overdue/due-soon migration
- `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/CreateNotificationNodeExecutor.cs` lines 471–546 (`BuildNotificationEntity`) — W1 customData enrichment target
- `src/client/shared/Spaarke.DailyBriefing.Components/src/components/DailyBriefingApp.tsx` lines 238–287 (`handleAddToTodo`) — reference for action-handler pattern in overflow menu wiring

---

## 3. Implementation Approach

### Phase Structure

```
PR 1: W0 JPS — Action rows + executor + form (Tasks 001–009)
└─ Set up + dispatch investigation + add ActionType 141 enum
└─ Author EntityNameValidatorNodeExecutor + xUnit tests
└─ Author EntityNameValidatorForm.tsx (PlaybookBuilder property panel)
└─ Deploy 4 Action rows: SYS-LOOKUP-MEMBERSHIP, BRIEF-NARRATE-TLDR/CHANNEL, BRIEF-VALIDATE-ENTITY-NAMES
└─ jps-scope-refresh + smoke + PR wrap

PR 2: W0 JPS — Narrate playbook + reconcile 7 (Tasks 010–019)
└─ Author DAILY-BRIEFING-NARRATE playbook + sprk_configjson node graph
└─ Deploy + validate via jps-playbook-design
└─ Audit deployed sprk_configjson for PB-016/PB-018/PB-019 (already migrated) — parallel
└─ Audit PB-020/PB-021 (need W1 migration) — parallel
└─ Audit PB-017/PB-022 (stubs — W1 implements) — parallel
└─ Reconcile + redeploy divergent configs
└─ jps-scope-refresh + BFF wrapper smoke + PR wrap

PR 3: W1 Producer — customData + stubs + membership (Tasks 020–029)
└─ Enrich CreateNotificationNodeExecutor.BuildNotificationEntity (viaMatter / regardingName / source)
└─ Ensure sprk_category column dual-write
└─ Migrate notification-tasks-overdue.json (membership-scope union FetchXml) — parallel
└─ Migrate notification-tasks-due-soon.json — parallel
└─ Implement notification-matter-activity.json (stub → full) — parallel
└─ Implement notification-work-assignments.json (stub → full) — parallel
└─ Standardize customData across all 7 playbooks
└─ Structured member_skipped warning logging
└─ customData schema-conformance xUnit fixture + PR wrap

PR 4: W2 Consumer — /narrate dispatch + cache + fallback (Tasks 030–036)
└─ Evaluate sprk_playbookconsumer dispatch path (AC-12c rationale)
└─ Replace DailyBriefingEndpoints.HandleNarrate with playbook dispatch wrapper
└─ Verify response-shape backward compat (existing widget unchanged)
└─ Remove hasFetchedRef from useBriefingNarration.ts (FR-15)
└─ Implement ActivityNotesSection empty-narrative fallback (FR-16)
└─ Endpoint xUnit + Jest test updates + PR wrap

PR 5: W2 Consumer — UX redesign + preferences + link (Tasks 040–049)
└─ Wire timeWindow preference (FR-17a) — parallel sub-wave
└─ Wire dueWithinDays preference (FR-17b) — parallel sub-wave
└─ Wire disabledChannels server-side $filter on sprk_category (FR-17c)
└─ Wire autoPopup workspace launcher hook (FR-17d)
└─ Remove minConfidence end-to-end + grep verify zero refs (FR-17e)
└─ NarrativeBullet three-dot overflow menu (FR-18) — semantic-search PCF visual pattern
└─ Wire overflow callbacks in ActivityNotesSection + DailyBriefingApp
└─ Xrm.Navigation.navigateTo modal + 403 fallback toast (FR-19)
└─ TL;DR ↔ Activities count smoke test (FR-20) + PR wrap

Wrap: Project closeout (Task 090)
└─ merge-to-master + lessons-learned + portfolio archive
```

### Critical Path

**Blocking Dependencies:**
- PR 2 BLOCKED BY PR 1 — playbook references Action rows that must exist
- PR 4 (task 031) BLOCKED BY task 030 — dispatch path decision must precede wrapper rewrite
- PR 4 BLOCKED BY PR 2 — /narrate wrapper dispatches to `DAILY-BRIEFING-NARRATE` (PR 2 deploys it)
- PR 5 task 046 BLOCKED BY task 045 — overflow menu shell must exist before callbacks can wire to it
- W1 tasks 021–028 BLOCKED BY task 020 — customData enrichment must land before playbooks consume it

**High-Risk Items:**
- R3 PR #451 file overlap (11 files in `Spaarke.DailyBriefing.Components/`) — Mitigation: conflict-check per W2 PR + `notes/risks.md` log
- `sprk_playbookconsumer` dispatch unknown — Mitigation: task 030 explicit investigation with documented fallback (direct `AnalysisOrchestrationService` invocation with degenerate playbook)

---

## 4. Phase Breakdown

### Phase 1 (PR 1): W0 JPS — Action Rows + EntityNameValidator (Tasks 001–009)

**Objectives:**
1. Establish project foundation; investigate `sprk_playbookconsumer` dispatch path
2. Add `EntityNameValidator = 141` to `INodeExecutor.cs` enum
3. Author `EntityNameValidatorNodeExecutor.cs` + xUnit tests
4. Author `EntityNameValidatorForm.tsx` PlaybookBuilder property panel
5. Deploy 4 Action rows to spaarkedev1; verify via MCP `read_query`

**Deliverables:**
- [ ] Project artifacts committed (this commit)
- [ ] `INodeExecutor.cs` enum extended with `EntityNameValidator = 141`
- [ ] `EntityNameValidatorNodeExecutor.cs` shipped (NEW)
- [ ] `EntityNameValidatorNodeExecutorTests.cs` shipped (NEW)
- [ ] `EntityNameValidatorForm.tsx` shipped (NEW) — mirrors LookupUserMembershipForm pattern
- [ ] 4 `sprk_analysisaction` rows deployed + Active in spaarkedev1
- [ ] `jps-scope-refresh` completes; PlaybookBuilder palette shows new tools

**Critical Tasks:**
- Task 001 — MUST BE FIRST (project setup + dispatch path investigation)
- Task 002 — BLOCKS 003 (enum value required before executor implementation)

**Inputs**: spec.md, existing LookupUserMembershipNodeExecutor / Form / Tests, JPS skills
**Outputs**: New NodeExecutor + Form + Tests + 4 deployed Action rows + PR

### Phase 2 (PR 2): W0 JPS — Narrate Playbook + Reconcile 7 (Tasks 010–019)

**Objectives:**
1. Author `DAILY-BRIEFING-NARRATE` playbook node graph
2. Deploy + smoke against BFF wrapper (placeholder dispatch)
3. Audit 7 deployed notification playbook configs against repo JSON canonical
4. Reconcile + redeploy divergent configs; ensure PB-016/PB-018/PB-019/PB-020/PB-021 reference ActionType 52

**Deliverables:**
- [ ] `DAILY-BRIEFING-NARRATE` row in `sprk_analysisplaybook` Active in spaarkedev1
- [ ] `sprk_configjson` reflects node graph: Start → LoadKnowledge → [GenerateTldr ‖ GenerateChannelNarratives] → ValidateEntityNames → ReturnResponse
- [ ] 7 deployed notification playbook configs match repo JSON
- [ ] `jps-scope-refresh` completes; PlaybookBuilder scope catalog reflects new state

**Critical Tasks:**
- Task 010 → 011 → 016 → 017 sequential anchor
- Tasks 012, 013, 014 parallel audit batches

**Inputs**: PR 1 deployed Action rows, 7 repo playbook JSON files
**Outputs**: New playbook deployed + 7 playbooks reconciled + PR

### Phase 3 (PR 3): W1 Producer — customData Enrichment + Stubs (Tasks 020–029)

**Objectives:**
1. Enrich `BuildNotificationEntity` with `viaMatter` / `regardingName` / `source` fields
2. Ensure `sprk_category` column dual-write on every notification
3. Migrate tasks-overdue + tasks-due-soon to membership-scope union FetchXml
4. Implement 2 stub playbooks (matter-activity, work-assignments)
5. Add structured `member_skipped` warning logging for Contact-only members

**Deliverables:**
- [ ] `CreateNotificationNodeExecutor.cs` enriched per FR-6 (lines 471–546 area)
- [ ] `sprk_category` column populated on every produced notification
- [ ] 7 playbook JSON files standardized (customData consistency per FR-10)
- [ ] Structured `member_skipped` warning emits via ILogger
- [ ] xUnit fixture asserts customData schema conformance for fixtures from all 7 playbooks
- [ ] AC-6c: payload size <2KB typical, <10KB ceiling

**Critical Tasks:**
- Task 020 BLOCKS 021–028 (customData enrichment is foundation)
- Tasks 022–025 parallel migration/implementation batches

**Inputs**: PR 2 reconciled playbook configs, customData template
**Outputs**: Enriched producer + 2 implemented stubs + standardized customData + PR

### Phase 4 (PR 4): W2 Consumer — /narrate Dispatch + Cache + Fallback (Tasks 030–036)

**Objectives:**
1. Investigate `sprk_playbookconsumer` dispatch path; document decision
2. Replace `DailyBriefingEndpoints.HandleNarrate` body with playbook dispatch wrapper
3. Remove `hasFetchedRef` cache from `useBriefingNarration.ts`
4. Implement `ActivityNotesSection` empty-narrative fallback rendering

**Deliverables:**
- [ ] AC-12c decision recorded in task 030 notes (sprk_playbookconsumer-mapped OR direct-invoke with rationale)
- [ ] `DailyBriefingEndpoints.HandleNarrate` is thin wrapper (no inline prompt construction)
- [ ] `useBriefingNarration.ts` refetches when channels/actionsRefresh change
- [ ] `ActivityNotesSection` renders raw channels with "AI summary unavailable" banner when narrative empty
- [ ] Jest + xUnit tests updated; response-shape backward compat verified

**Critical Tasks:**
- Task 030 BLOCKS 031 (dispatch decision must precede wrapper rewrite)
- Task 031 depends on PR 2 (`DAILY-BRIEFING-NARRATE` playbook must exist)

**Inputs**: PR 2 deployed playbook, existing endpoint code, R2 widget code
**Outputs**: /narrate playbook dispatch + cache fix + fallback rendering + PR

### Phase 5 (PR 5): W2 Consumer — UX Redesign + Preferences + Link (Tasks 040–049)

**Objectives:**
1. Wire 4 preferences end-to-end (timeWindow, dueWithinDays, disabledChannels, autoPopup)
2. Remove `minConfidence` from all layers; verify zero references via grep
3. Implement three-dot overflow menu UX (`NarrativeBullet.tsx`) matching semantic-search PCF visual pattern
4. Wire overflow callbacks in `ActivityNotesSection` + `DailyBriefingApp`
5. Implement `Xrm.Navigation.navigateTo` modal open + 403 fallback toast
6. TL;DR ↔ Activities count smoke test

**Deliverables:**
- [ ] FR-17a–e all wired + verified
- [ ] `grep -rn "minConfidence\|AiConfidenceThreshold"` returns 0 results
- [ ] Three-dot overflow menu with 6 actions (Mark as read, Remove from briefing, Keep +7 days, Add to To Do, Dismiss, Open record)
- [ ] Accessibility audit passes (keyboard nav, screen reader, dark mode per ADR-021)
- [ ] Click regarding name → modal opens; 403 → toast shown
- [ ] Smoke test asserts TL;DR `totalNotificationCount` == rendered Activity item count

**Critical Tasks:**
- Task 045 BLOCKS 046 (overflow shell must exist before callbacks)
- Tasks 040+041 parallel sub-wave; 042+043+044 parallel sub-wave (small file overlap on `PreferencesDropdown.tsx`)

**Inputs**: PR 4 playbook dispatch wrapper + cache fix
**Outputs**: Full preferences wiring + new overflow UX + link click handling + PR

### Phase 6 (Wrap-up): Task 090

**Objectives:**
- merge-to-master with build verification
- Author `notes/lessons-learned.md`
- Archive project artifacts; update portfolio status

---

## 5. Dependencies

### External Dependencies

| Dependency | Status | Risk | Mitigation |
|------------|--------|------|------------|
| Azure OpenAI (gpt-4o-mini deployment) | Production | Low | Existing wiring; no new model requirements |
| spaarkedev1 environment (Dataverse) | Operator + dev access | Low | Required for W0 deployments |
| Microsoft `appnotification` OOB entity | Production | Low | Only customData JSON content evolves |

### Internal Dependencies

| Dependency | Location | Status |
|------------|----------|--------|
| `MembershipResolverService` + `LookupUserMembershipNodeExecutor` | `src/server/shared/Spaarke.Core/Services/Membership/` + `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/` | Shipped (platform-foundations R3) |
| `sprk_briefingstate` Choice column | `appnotification` table in spaarkedev1 | Deployed (R3 task 001 ✅) |
| BFF TTL fix (`ttlinseconds = 604800`) | `CreateNotificationNodeExecutor.cs:490` | Deployed (R3 task 010 ✅) |
| `@spaarke/daily-briefing-components` package | `src/client/shared/Spaarke.DailyBriefing.Components/` | Present (R2 deliverable) |
| PlaybookBuilder code page | `src/client/code-pages/PlaybookBuilder/` | Present |
| `sprk_playbookconsumer` entity + service | `src/server/api/Sprk.Bff.Api/` (chat-routing-redesign-r1 Phase 1R) | Present; FR-12 evaluates extension |

---

## 6. Testing Strategy

**Unit Tests** (≥90% line coverage on changed files per NFR-03):
- `EntityNameValidatorNodeExecutorTests.cs` — Validate_Missing*, ExecuteAsync_HappyPath, ExecuteAsync_ScrubsHallucination, ExecuteAsync_AllowListPassThrough
- `CreateNotificationNodeExecutorTests.cs` — enriched customData fixtures asserting AC-6a–d
- Playbook customData schema-conformance fixture (FR-10) — applies to all 7 playbooks
- `useBriefingNarration.test.ts` — verify refetch on data change
- `PreferencesDropdown.test.tsx` — preferences wiring + minConfidence absence
- `NarrativeBullet.test.tsx` — three-dot menu rendering + a11y

**Integration Tests**:
- BFF wrapper dispatch to `DAILY-BRIEFING-NARRATE` playbook (AC-12a/b)
- Producer with unmappable Contact → `member_skipped` event (AC-11)
- Membership-scope FetchXml union (assignedAttorney sees overdue tasks on member-matter — AC-7)

**E2E / UAT** (manual in spaarkedev1):
- ACME-only test data → TL;DR mentions only ACME (AC-13b, AC-14a)
- Each preference change produces visible difference (AC-17a–d)
- Click Check/Remove/Keep → `/narrate` refires; TL;DR updates (AC-15)
- Click regarding name → modal opens; 403 → toast (AC-19a/b)

**UI Tests** (via ui-test skill, ADR-021 dark-mode compliance):
- Three-dot overflow menu — keyboard nav, screen reader, semantic tokens
- Activity Notes fallback — banner appears, raw channels render

---

## 7. Acceptance Criteria

### Technical Acceptance

**PR 1 (W0 Action Rows + Executor):**
- [ ] `mcp__dataverse__read_query` `SELECT … FROM sprk_analysisaction WHERE sprk_executoractiontype = 52` returns 1 row (SYS-LOOKUP-MEMBERSHIP)
- [ ] Both `BRIEF-NARRATE-TLDR` and `BRIEF-NARRATE-CHANNEL` rows Active; `jps-validate` passes
- [ ] `EntityNameValidator = 141` enum value present; no ExecutorActionType conflicts
- [ ] xUnit test passes: scrubs "Johnson & Lee LLP" when not in allow-list + emits `hallucination_detected` event

**PR 2 (W0 Narrate Playbook + Reconcile):**
- [ ] `DAILY-BRIEFING-NARRATE` row exists + Active in spaarkedev1; `jps-playbook-audit` passes
- [ ] BFF wrapper successfully dispatches narrate request; receives valid response
- [ ] All 7 playbook rows have `sprk_configjson` matching canonical repo source

**PR 3 (W1 Producer):**
- [ ] AC-6a–d: enriched customData fields present; backward compat preserved; payload size <2KB typical; every produced notification has `sprk_category` populated
- [ ] AC-7: assignedAttorney on Matter-X sees overdue tasks on Matter-X
- [ ] AC-8/AC-9: 2 stub playbooks produce notifications under expected conditions
- [ ] AC-11: `member_skipped` warning visible in App Insights for unmappable Contact

**PR 4 (W2 /narrate Dispatch):**
- [ ] AC-12a/b/c: endpoint is thin wrapper; response shape backward compat; dispatch decision documented
- [ ] AC-15: action click triggers new `/narrate` fetch (visible in DevTools); TL;DR updates
- [ ] AC-16: mocked narration failure → raw channels render with "AI summary unavailable" banner

**PR 5 (W2 UX + Preferences):**
- [ ] AC-17a–d: each preference change visibly changes rendered content
- [ ] AC-17e: `grep -rn "minConfidence\|AiConfidenceThreshold"` returns 0 results
- [ ] AC-18a–c: three-dot menu shows 6 actions; a11y passes; Add to To Do regression-free
- [ ] AC-19a/b: click → modal opens; 403 → toast shown
- [ ] AC-20: smoke test asserts TL;DR count == Activity card count

### Business Acceptance

- [ ] R4 graduates per spec §Success Criteria (all 15 boxes)
- [ ] No new HIGH-severity CVE
- [ ] BFF publish-size delta ≤ +1 MB (NFR-02)
- [ ] Hallucination eliminated under UAT (manual ACME-only test)

---

## 8. Risk Register

| ID | Risk | Probability | Impact | Mitigation |
|----|------|------------|---------|------------|
| R1 | R3 PR #451 file overlap (11 files in DailyBriefing.Components/) — merge conflicts | High | Med | Document in `notes/risks.md`; run `conflict-check` per W2 PR; rebase whichever lands second |
| R2 | `sprk_playbookconsumer` doesn't fit widget payload — fallback to direct invocation | Med | Low | FR-12 task 030 investigates first, documents decision with rationale (AC-12c) |
| R3 | LLM still hallucinates after grounding + temperature 0 | Low | Med | `EntityNameValidator` Tool node scrubs output as defense-in-depth |
| R4 | Producer dual-write `sprk_category` missed in some playbooks | Low | Med | Task 021 audits + adds writer if missing; xUnit fixture asserts AC-6d |
| R5 | W0 deployment skipped or partial | Low | High | 8 W0 tasks with explicit MCP `read_query` verification + `jps-scope-refresh` smoke |
| R6 | BFF publish-size delta >+1 MB on PR 1 or PR 4 | Low | Med | §10 per-task publish-size check; expected delta ≤+0.1 MB |

---

## 9. Next Steps

1. **Review this plan.md + README.md + CLAUDE.md** for accuracy
2. **Run** `/task-create projects/spaarke-daily-update-service-r4` (called automatically by `/project-pipeline` next) to generate task files
3. **Switch to Accept Edits mode** before task execution begins
4. **Begin** Phase 1 (PR 1) implementation via `/task-execute` with task 001

---

**Status**: Ready for task decomposition
**Next Action**: Run `/task-create` (next step of `/project-pipeline`)

---

*For Claude Code: This plan provides implementation context. Load relevant sections when executing tasks.*
