# Deferred Work & Issues — spaarke-ai-architecture-redesign-r1

> Two-write register per `.claude/skills/project-defer-issue-tracking/`. Filed at task 090 wrap-up, 2026-07-08.
> GitHub issue numbers filled after creation.

---

## Deferrals

### DEF-001 — Admin observability dashboards (audit-trail UI, cost dashboards, refusal-backlog view)

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | next-round |
| **Filed** | 2026-07-08 |
| **Source** | spec FR-P4-07 named deferral #1 |
| **GitHub Issue** | [#552](https://github.com/spaarke-dev/spaarke/issues/552) |

**Description**

Task 054 shipped the metering *counters* + KQL pack, but there is no admin-facing surface: no audit-trail UI over the session ledger/ToolChain, no cost dashboard over `ai.metering.tokens`, no refusal-backlog view over `RefusalCapabilityTool` outcomes. Concrete failure: an administrator today cannot answer "which tenant spent the most tokens this week" or "what did users ask for that we refused" without running KQL by hand in App Insights.

**Entry-points**

- `scripts/kql/ai-metering/` (7 queries — the data layer the dashboards would sit on)
- `src/server/api/Sprk.Bff.Api/Telemetry/AiTelemetry.cs` (counter definitions)
- App Insights: `spe-insights-dev-67e2xz`

**Suggested fix**: Azure Workbook (or SpaarkeAi admin widget) binding the KQL pack; refusal-backlog needs a `capability_invocations{outcome=refused}` drill-down.
**Estimated effort**: 2–4 days
**Blockers**: none
**Related**: ADR-015/016; r2 design §10

---

### DEF-002 — Assistant-initiated email SEND (draft-only shipped)

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | next-round |
| **Filed** | 2026-07-08 |
| **Source** | spec FR-P4-07 named deferral #2; design decision 2026-07-05 (DRAFT-only) |
| **GitHub Issue** | [#553](https://github.com/spaarke-dev/spaarke/issues/553) |

**Description**

`email.draft` (side_effect_class=communicate, confirmation-gated) creates a Spaarke `sprk_communication` DRAFT record; the user must open the Communication service to send. Concrete failure: "draft AND send this to the client" ends at a draft — the assistant cannot complete the send even with explicit confirmation. Gated send is a catalog addition (new typed tool + higher-tier side-effect class), not an architecture change.

**Entry-points**

- `src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/EmailDraftToolHandler.cs`
- `Services/Ai/EmailDispositionSender.cs` (OutputRouter email leg — the coded-path send that already exists for Daily Briefing)
- Binding row DRAFT-CORR@v1 `f7dc4a00…` / email.draft tool `bc11e90d…` (spaarkedev1)

**Suggested fix**: new `email.send` typed tool, side_effect_class=communicate-external, ONE-gate confirmation with recipient echo; r2 Confirmation Policy v2 tiers should classify it.
**Estimated effort**: 1–2 days server + catalog rows + eval cases
**Blockers**: r2 D-F1 Confirmation Policy v2 (tier line ruling)
**Related**: ADR-039 (closed catalog addition path); r2 design §7.1

---

### DEF-003 — /goal wave-condition pilot: promote into skills

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | next-round |
| **Filed** | 2026-07-08 |
| **Source** | spec FR-P4-07 named deferral #3; NFR-10 pilot; `notes/goal-feature-evaluation.md` |
| **GitHub Issue** | [#554](https://github.com/spaarke-dev/spaarke/issues/554) |

**Description**

Pilot verdict: **proved out.** Every wave in this project carried a pre-authored /goal condition (shown-evidence + scope bind + turn cap + Step 9.5 gates) and the pattern held across P0–P4 including the 6-agent parallel waves; gates were never wrapped (NFR-10 held). Concrete failure absent promotion: the next project hand-authors wave conditions from scratch (or skips them), losing the composition rules established in `notes/goal-feature-evaluation.md`.

**Entry-points**

- `projects/spaarke-ai-architecture-redesign-r1/notes/goal-feature-evaluation.md` (composition rules)
- `projects/spaarke-ai-architecture-redesign-r1/plan.md` §4 + `tasks/TASK-INDEX.md` wave headers (worked examples)
- Targets: `.claude/skills/project-pipeline/SKILL.md` Step 3, `.claude/skills/task-create/SKILL.md`

**Suggested fix**: task-create emits a pre-authored /goal block per wave header from the POML acceptance criteria; project-pipeline validates presence; add the "NEVER wraps a gate" rule verbatim.
**Estimated effort**: 0.5–1 day (skill edits are main-session-only)
**Blockers**: none
**Related**: NFR-10

---

### DEF-004 — G-M live maker walkthrough (post-r2)

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | someday (scheduled post-r2) |
| **Filed** | 2026-07-08 |
| **Source** | Operator ruling 2026-07-07 ("skip this for now until we're ready to really work with modifying actions"); graduation amendment `notes/g-m-evidence.md` |
| **GitHub Issue** | [#555](https://github.com/spaarke-dev/spaarke/issues/555) |

**Description**

Success Criterion 6 closed DEFERRED-WITH-EVIDENCE: the BA editor is shipped (task 053) and capability-as-data is proven through six UAT rounds, but no *observed business analyst* has authored a capability unassisted. Concrete gap: the "second product" claim (capability-as-catalog-row authorable by non-engineers) rests on engineering-session evidence only.

**Entry-points**

- `projects/spaarke-ai-architecture-redesign-r1/notes/g-m-evidence.md` (the amendment + shipped-evidence table)
- `docs/guides/ai-guide-consumer-wiring.md` (the tutorial the walkthrough follows)
- PlaybookBuilder BA editor: `src/client/code-pages/PlaybookBuilder/`

**Suggested fix**: run the original G-M script once r2's judgment/memory core settles the Action-authoring surface; operator observes; evidence file completes.
**Estimated effort**: half-day session
**Blockers**: r2 core Phase A (Action surface stability)
**Related**: spec Success Criterion 6; NFR-11

---

### DEF-005 — Documentation consolidation using canonical v0.5 as yardstick

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | next-round |
| **Filed** | 2026-07-08 |
| **Source** | Operator directive 2026-07-07 ("WAY TOO MANY docs… need ONE authoritative document") |
| **GitHub Issue** | [#556](https://github.com/spaarke-dev/spaarke/issues/556) |

**Description**

`docs/architecture/SPAARKE-AI-ARCHITECTURE-AND-COMPONENT-DESIGN.md` v0.5 (+§8.1 as-built) is now THE authoritative AI-architecture reference. The surrounding estate (AI-ARCHITECTURE.md, SPAARKE-PLAYBOOK-LLM-OUTPUT-PATTERN.md, workspace/component-model docs, 40+ guides) overlaps it in places. Concrete failure: a new contributor cannot tell which of ~6 overlapping AI docs is current; doc-drift-audit keeps re-finding the same class of drift.

**Entry-points**

- `docs/architecture/SPAARKE-AI-ARCHITECTURE-AND-COMPONENT-DESIGN.md` (the yardstick)
- `docs/architecture/AI-ARCHITECTURE.md`, `SPAARKE-PLAYBOOK-LLM-OUTPUT-PATTERN.md` (largest overlaps)
- Root `CLAUDE.md` §17 pointer table (the index to rationalize)

**Suggested fix**: dedicated small project — inventory AI-related docs, mark each SUPERSEDED-BY-v0.5 / MERGE-INTO / KEEP-distinct, collapse and redirect.
**Estimated effort**: 2–3 days
**Blockers**: none (explicitly NOT this project or r2 core scope)
**Related**: operator directive; doc-drift-audit history

---

### DEF-006 — r1 residual engineering debt (bundle → r2 backlog)

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | next-round |
| **Filed** | 2026-07-08 |
| **Source** | Accumulated across P1–P4; canonical dispositions in `projects/spaarke-ai-architecture-redesign-r2/design.md` §10 (19 rows) |
| **GitHub Issue** | [#557](https://github.com/spaarke-dev/spaarke/issues/557) |

**Description**

Bundle issue so the residuals are board-visible; the per-item dispositions live in the r2 design backlog (source of truth). Items: (a) 4 KNOWN failing unit tests (KnowledgeDeploymentConfig, DailyBriefingCollector resolver, TemplateContextBuilder TextOnly, SessionFilesCleanup) + AuditLog/NetArchTest contention flakes — need TimeProvider-style probes/fixture fixes; (b) AnalysisWorkspace jest debt (3–5 pre-existing failing non-conversation suites in SpaarkeAi); (c) capability-discovery endpoint for soft slash-commands (deferred at G-P1); (d) progressive render for long capability outputs; (e) validator triple-twin hoist (server `OpenAiFunctionSchemaValidator` + 2 client twins = three-mirror maintenance); (f) ExecutionTrace hard-refresh ledger read; (g) gate pre-suspend input validation (would have prevented the confirm-resume 502 class); (h) Track-B operator-decision leftovers O-1..O-5 (`notes/track-b-completion-audit.md` §11).

**Entry-points**

- `projects/spaarke-ai-architecture-redesign-r2/design.md` §10 (rows 13–19 carry most of these)
- `projects/spaarke-ai-architecture-redesign-r1/notes/track-b-completion-audit.md` §11 (O-1..O-5)

**Suggested fix**: r2 /design-to-spec turns §10 rows into FRs or explicit non-goals; the 4 KNOWN test fixes can ride any BFF wave.
**Estimated effort**: varies per item
**Blockers**: r2 spec authoring
**Related**: r2 design v0.2 (`03f9a5bbc`)

---

## Issues

(none filed — all wrap-up items are deferrals)
