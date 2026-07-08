# Current Task State — spaarke-ai-architecture-redesign-r1

> **Last Updated**: 2026-07-07 ~23:30 (context-handoff — pre-compaction; operator compacts next)
> **Recovery**: Read Quick Recovery + run `git log --oneline -15`. Findings history: notes/g-p3-uat-round1..4-findings.md.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | P4 finish: **055 agent RUNNING** (publish/CVE baseline + ADR-029 update + ADR-040 size-cap enforce). Then 090 → PR ready → merge. |
| **Step** | **50/51 done.** 050 ✅ (`61680b96d`: audit 62 rows zero-survivors + 42-file builder delete + .claude fixes) · 054 ✅ (`cfc20ce55→c9eb3dd74`: metering 4 counters + KQL pack, live dev evidence) · r2 design v0.2 ✅ (`03f9a5bbc`). Portfolio = 50. |
| **Status** | autonomous close-out; 1 background agent in flight (055) |
| **Next Action** | On 055 return: verify build+suite vs KNOWN, apply any .claude ADR-029 twin edit (MAIN session), flip TASK-INDEX 055, commit+push (`git pull --rebase` first), portfolio 51. Then **090 wrap-up in MAIN session**: /test-diet (BINDING), /defer filings (r2 backlog + validator triple-twin + trace ledger-read + gate pre-suspend + doc-consolidation-v0.5-yardstick), doc-drift-audit, ADR A-3 .claude twin refreshes (033/034/010/016/018/038 + 039/040 status verify), FR-by-FR spec acceptance, G-M DEFERRAL amendment record, devops-project-archive, projects/INDEX.md. Then re-merge master → full suite → PR #551 ready (offer /code-review ultra ONCE) → merge → post-merge smoke. |

## In-flight background agents (post-compaction: their notifications re-invoke you)
1. **task-050** — Track-B completion audit + accumulated candidates (briefing playbook orphan, embeddings index=DOCUMENT-ONLY for operator, DocumentStream, playbook_options leg, trace emitters, FieldDelta model, 4 legacy workspace-tab tools [Workspace layout variant of Send IS LIVE — keep], WorkingDocumentService, TL/KNW row dupes) + **task-053's deferred server leg** (AiPlaybookBuilderService + dead graph endpoints deletion). Output: notes/track-b-completion-audit.md.
2. **task-054** — per-tenant metering counters + KQL pack with live dev query output (App Insights: spe-insights-dev-67e2xz). Owns AiTelemetry.cs + KQL files.
3. **r2-design-v2** — updates projects/spaarke-ai-architecture-redesign-r2/design.md → v0.2: NEW D-F0 Resourcefulness Doctrine (strategy meta-prompt, read/write safety asymmetry, degradation ladder, refusal-affordance links, resourcefulness evals); platform-core+satellites re-cut (Compose r2 = separate satellite absorbing D-C*, briefing hallucinations = immediate fix wave, insights widget refurbish = satellite post-Phase-A); industry-parity section (Harvey/Copilot chassis mapping). Project name STAYS spaarke-ai-architecture-redesign-r2.

## Operator decisions 2026-07-07 (final session — ALL RATIFIED)
- **048 CLOSED**; refusal-with-affordance-link (Document Upload page) noted → r2 D-F0(d).
- **Rulings applied by recommendation** (operator asked for plain-language; defaults applied, can override): (1) chat-created tasks STAY sprk_event, revisit in r2 (switch = catalog-data-only); (2) analysis.rerun stays UNGATED accept-with-note (last app-only engine leg, bounded, FR-P4-01 re-verified); (3) **ADR-040 size-cap enforcement → task 055**; (4) office-addins SseClient keep-with-reason.
- **G-M maker gate DEFERRED** (operator: revisit post-r2 when really working with actions) — 090 must record the graduation amendment: evidence = BA editor shipped + jest; live maker walkthrough post-r2.
- Operator deletes orphan sprk_document dd97bad5-6e7a-f111-ab0e-7ced8ddc4cc6 themselves.
- /code-review ultra on PR #551: OPTIONAL (operator lukewarm; every task had Step 9.5 review) — offer once at PR-ready, don't push.
- **Master alignment is an operator priority**: branch merged master 2026-07-07 AM (0 behind then); RE-MERGE master before PR-ready + full suite; merge PR promptly after 090 (CI auto-deploys master→spaarke-bff-dev — the clobber incident 18:09Z proves it; post-merge that pipeline ships OUR code).
- **Docs**: canonical doc v0.5 (SPAARKE-AI-ARCHITECTURE-AND-COMPONENT-DESIGN.md, §8.1 as-built) = THE authoritative reference for the operator's future doc-consolidation effort (NOT this project); 090 files a /defer for doc-consolidation-using-v0.5-as-yardstick.

## Remaining sequence (after 050/054 land)
1. **055** (dispatch solo): publish-size + CVE verification + ADR-029 baseline update + **ADR-040 inline size-cap enforcement** (warn→enforce; deferred from 021/047 per ruling).
2. **090 wrap-up** (main session drives; skills: /test-diet [BINDING], /defer filings [structure as r2 backlog: r2 pillars, TimeProvider probes, AnalysisWorkspace jest debt, capability-discovery endpoint for slash, progressive render, validator triple-twin hoist, trace ledger-read, gate pre-suspend validation, doc-consolidation], doc-drift-audit, ADR A-3 refreshes [.claude twins: 033/034/010/016/018/038 minor + ADR-039/040 status verification], FR-by-FR spec acceptance check, G-M amendment record, devops-project-archive, projects/INDEX.md update).
3. Re-merge master → full suite → PR #551 ready (title/body refresh; hot-path declaration; cite publish baseline + known-failures list) → operator merges (or auto) → post-merge smoke on spaarke-bff-dev (CI deploys master) → SpaarkeAi web resource redeploy from master worktree if needed.
4. r2 kickoff: operator reviews design v0.2 → /design-to-spec → /project-pipeline (new worktree). Satellites: Compose r2 (own project, parallel), briefing-hallucination fix wave (immediate — operator to供 example), insights widget refurbish (post-core-Phase-A).

## Key facts for post-compaction
- Repo: worktree c:\code_files\spaarke-wt-spaarke-ai-architecture-redesign-r1, branch work/spaarke-ai-architecture-redesign-r1, PR #551 (draft, mergeable). HEAD ~`229817ce8`+ (agents uncommitted on top).
- Deploys: BFF `scripts/Deploy-BffApi.ps1` (bg-safe); SpaarkeAi `npm run build` in src/solutions/SpaarkeAi then `scripts/Deploy-SpaarkeAi.ps1`. Health: /healthz + /healthz/catalog (both 200/Healthy @ 229817ce8). **CI clobber check**: az rest deployments list — if active sha ≠ ours, redeploy.
- KNOWN test failures (NOT ours): ExecutorConfigSchemas, KnowledgeDeploymentConfig, DailyBriefingCollector resolver, TemplateContextBuilder TextOnly, SessionFilesCleanup + AuditLogService & PlaybookDispatcherPhaseB-era flakes (isolation-pass) + NetArchTest 5 (+1 documented IEmailDispositionSender). SpaarkeAi 3-5 non-conversation jest suites pre-existing.
- Portfolio: Issue #550, project PVT_kwHODW0Pv84BEgWu, item PVTI_lAHODW0Pv84BEgWuzgxza1E, field PVTF_lAHODW0Pv84BEgWuzhWPlLY (Tasks Completed = 48; 050/054 → 50; 055 → 51 pre-090).
- Wave protocol: agents get POML path + task-execute + boundaries (no commit/TASK-INDEX/.claude) + KNOWN-failures list; main session verifies/commits selectively (parallel agents' files excluded); `git pull --rebase` before every push.
- Catalog rows/GUIDs + all round fixes: see notes/g-p3-uat-round*-findings.md + notes/task-0xx-*-notes.md. .claude/ edits are MAIN SESSION ONLY.
