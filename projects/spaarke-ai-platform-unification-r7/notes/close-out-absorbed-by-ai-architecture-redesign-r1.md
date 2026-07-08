# R7 Close-Out — Absorbed by `spaarke-ai-architecture-redesign-r1`

> **Date**: 2026-07-05
> **Authored by**: `spaarke-ai-architecture-redesign-r1` task 013 (FR-P0-11 portfolio reconciliation)
> **Decision**: R7 (GitHub Issue [#501](https://github.com/spaarke-dev/spaarke/issues/501)) is **CLOSED / RE-SCOPED**. All remaining R7 scope is either absorbed by a named phase of [`spaarke-ai-architecture-redesign-r1`](../../spaarke-ai-architecture-redesign-r1/) (Issue [#550](https://github.com/spaarke-dev/spaarke/issues/550)) or dropped with a written reason below. No silent closure.
> **Branch disposition**: the `work/spaarke-ai-platform-unification-r7` tactical BRANCH is NOT handled here — that is redesign-r1 task 025 (FR-P1-06): keep the session-id fix, ExtractedText persistence, auto-promote, and field_delta synthesis; drop `TryDetectExplicitConsumerType` regex, the `linear_dispatch` SSE set, `executeLinearDispatch.ts`, and the diagnostic log. No open PRs from the r7 branch remain (last merged: PR #546, 2026-07-03).

---

## 1. What R7 shipped (no transfer needed)

Per [`tasks/TASK-INDEX.md`](../tasks/TASK-INDEX.md) status snapshot (last updated 2026-06-30) — these waves are COMPLETE and merged to master:

| R7 wave | Scope | Status at close |
|---|---|---|
| **Wave 1** | AiCompletionNodeExecutor build (FR-12..FR-15) | 🟢 COMPLETE (001-010 ✅) |
| **Wave 2** | Dispatch refactor + `ActionType`→`ExecutorType` enum rename (FR-07..FR-10) | 🟢 COMPLETE (020-029 ✅) |
| **Wave 3** | Typed config schemas (FR-16) | ✅ COMPLETE (030-036 ✅) |
| **Wave 4** | Schema cleanup + legacy direct-path removal (FR-03, FR-04, FR-11) | 🟢 COMPLETE (040-047 ✅) |
| **Wave 9** | Consumer migration — chat-summarize + Playbook Library ≥3 surfaces (FR-17, FR-18) | 🟢 COMPLETE (090-096 ✅) |
| **Wave 5** (except T056) | 94-node executor-type backfill in spaarkedev1 (FR-19, FR-20) | 050-055 ✅ |
| **Wave 6** (except T068) | Documentation deletion + updates (FR-28..FR-31) | 060-067, 069 ✅ |
| **Wave 8** (except T087/089/089d) | Playbook Builder UI updates (FR-21..FR-27) | 080-086, 088, 089a-c ✅ |
| **Wave 11** (except T116/117/119) | Orchestrator runtime variable resolution (Layers 1+2, 7 helpers, fan-out) | 110-115 + 111a ✅; T118 achieved via POC pivot (operator-verified live render) |
| **Wave 12.1-12.4** (except T124/136/145/154) | MVP completion — Daily Briefing 6-entity, 5 wizards restored, Assistant↔Workspace plumbing | 120-123, 130-135, 140-144, 150-153 ✅ |

## 2. Absorbed-waves map — remaining R7 scope → redesign-r1 phase (or dropped-with-reason)

Redesign-r1 phases: **P0** Foundations (engineering gate) · **P1** First capability end-to-end (gate G-P1) · **P2** Text-path hard cutover (G-P2) · **P3** Consumer + client consolidation (G-P3) · **P4** Sweep + hardening + graduation (G-P4/G-M) · **Track B** continuous deletion sweep.

### 2a. Remaining TASK-INDEX tasks

| R7 item (exact id) | What it was | Disposition |
|---|---|---|
| **W5 T056** — sanity redeploy 3 representative playbooks | Backfill verification | **DROPPED**. The playbook engine is FROZEN under redesign-r1; verification is superseded by the eval suite (FR-P0-09, merge gate per NFR-02) + browser gates G-P1..G-P3. |
| **W6 T068** — root CLAUDE.md entry-points update | Doc alignment | **ABSORBED → P4 FR-P4-03** (documentation reconciliation + doc-drift-audit clean). |
| **Wave 7 T070-075** — rewrite `jps-action-create` / `jps-playbook-design` / `jps-playbook-audit` / `jps-validate` + minor `jps-scope-refresh` (FR-32/FR-33, node-first dispatch model) | Skill rewrites | **ABSORBED → P4 FR-P4-03/FR-P4-04**, RE-BASED: skills are re-authored against the Action/Binding catalog model, NOT the node-first dispatch model — the R7-era node-first rewrite is CANCELLED (already recorded in `projects/INDEX.md` skills-coordination section). |
| **W8 T087** (Prompt tab UAT), **T089** (unknown-executor warning), **T089d** (deploy PlaybookBuilder Code Page) | Playbook Builder canvas finish | **SUPERSEDED → P4 FR-P4-04**: PlaybookBuilder canvas is DE-SCOPED to a BA scope/prompt/binding editor; deploying the 33-executor canvas has no remaining consumer. Consistent with R7's own close-plan decision D-12 (Builder UI deferred — "no users on it"). |
| **W10 T101** — UAT `/narrate` via Daily Briefing widget (**R4 graduation gate**, FR-15) | R4 close-out gate | **ABSORBED → P3 FR-P3-04 + gate G-P3**: Daily Briefing becomes the first full `coded` composite Action; `/narrate` engine-default + `Features:NarrateUseCodeBasedNarrator` flag deleted; browser UAT at G-P3 is the graduation gate. (R7 W11 T118 already operator-verified the live-render path in spaarkedev1.) |
| **W10 090-project-wrap-up** | R7 wrap-up | **SUPERSEDED** by this close-out note + redesign-r1 FR-P4-07 wrap-up. |
| **W11 T116** (deploy+smoke), **T117** (UAT — substantively satisfied by T118 POC), **T119** (publish gate) | Wave 11 formal close | **ABSORBED**: the briefing path lands as **P3 FR-P3-04** (G-P3 browser gate); publish-size/CVE gating is continuous in redesign-r1 (NFR-01 per-task + **P4 FR-P4-06** baseline update). |
| **W12.1 T124** — backfill-health sweep (stub configJson + orphan FK + clobbering overrides) | Frozen-engine data hygiene | **ABSORBED → P4 FR-P4-02** (catalog governance; `sprk_nodetype` option-set gap and frozen-engine data state resolved-or-documented). |
| **W12.2 T136** — Daily Briefing UAT (AC1-AC7 pending-operator) | Browser signoff | **ABSORBED → P3 gate G-P3** (FR-P3-04 briefing coded path, operator browser UAT per redesign NFR-11). |
| **W12.3 T145** — 5-wizards UAT (AC8-AC12 pending-operator) | Browser signoff | **ABSORBED → P3 FR-P3-01 + gate G-P3**: `document-profile`, `matter-pre-fill`, `project-pre-fill`, workspace `summarize-file` become Binding rows; wizard flows verified at the G-P3 browser gate. |
| **W12.4 T154** — Assistant↔Workspace UAT (AC13-AC15 pending-operator) | Browser signoff | **ABSORBED → P2 gate G-P2** (FR-P2-05 hard cutover of chat NL to the agent loop; assistant behavior verified in-browser at G-P2). |
| **W12.5 placeholders 160-162** (never generated) | Wave 12 wrap-up | **SUPERSEDED** by this close-out + redesign-r1 FR-P4-07. |

### 2b. R7 close plan (2026-07-03) phases — [`r7-close-plan-2026-07-03.md`](r7-close-plan-2026-07-03.md)

| Close-plan phase | Disposition |
|---|---|
| **Phase 12.3a** — chat-summarize single dispatch path + Doc Upload PlaybookId retire | **ABSORBED → P1**: FR-P1-01 (chat-summarize as Action row + Binding via prompted executor; `SessionSummarizeOrchestrator` dual-path dissolves) + FR-P1-04 (ONE client `dispatchConsumer(bindingId, args)` helper) + FR-P1-06 (task 025 tactical-branch disposition). Partially landed already via PR #546 + tactical branch. PlaybookId appsettings retire → **P3 FR-P3-01** (`LinearConsumers`, `Workspace.*PlaybookId`, `Insights.Playbooks.Map` blocks deleted, grep-zero). |
| **Phase 12.3b** — output schema single-source-of-truth | **ABSORBED → P1 FR-P1-01** (SUM-CHAT@v1 schemas rendered via the canonical prompted executor `ActionRunner` + `PromptSchemaRenderer`) + **P4 FR-P4-02** (catalog governance). |
| **Phase 12.4** — Persona + Playbook slash-menu schema | **SPLIT**: slash-menu-from-Dataverse-columns is **SUPERSEDED → P2 FR-P2-05** (four retained soft slashes map to deterministic direct invocations; the Binding table `sprk_playbookconsumer` is the SINGLE routing surface — redesign MUST NOT create new manifest tables/columns for routing). Persona (`sprk_aipersona` FK on Action, `IPersonaLibrary`) is **DROPPED** from portfolio-committed scope — not in the redesign's ratified catalog contract (canonical v0.4); re-proposable via `/defer` against the redesign backlog. |
| **Phase 12.5** — Skills formalization on Node (`sprk_aiskill`, `ISkillLibrary`) | **DROPPED** — `sprk_playbooknode` is FROZEN under redesign-r1; new prompt-composition primitives were not ratified in canonical v0.4. Re-proposable via `/defer`. |
| **Phase 12.6** — Knowledge references on Node (`IKnowledgeRetriever`) | **DROPPED** — node frozen; retrieval capability arrives via the CLOSED tool catalog on the grounded tool plane (ADR-039), not via node-level references. |
| **Phase 12.7** — Retrofit 5 Linear consumers to manifest model + CI drift check | **ABSORBED → P0 FR-P0-04** (boot reconciliation: `ConsumerTypes` constants ↔ Binding rows + tool row ↔ handler bijection — the drift check, enforced at startup health check rather than a CI architecture test) + **P3 FR-P3-01** (every remaining consumer becomes a Binding row; config-key routing deleted). The hardcoded-FK manifest design is superseded by the Binding-table single-routing-surface rule. |
| **Phase E** — deactivate 6 migrated playbook rows | **ABSORBED → Track B FR-TB-01** (sweep-as-you-go) + **P4 FR-P4-01** (Track-B completion audit — every item grep-verified deleted or keep-with-reason). |
| **Phase G** — docs (`BUILD-A-NEW-LINEAR-AI-CONSUMER` rewrite, composition-model doc, ADR-013 update) | **ABSORBED → P4 FR-P4-03** (consumer-wiring guide → capability-wiring; new data-model docs; ADR A-3 refreshes). ADR-013 was already amended 2026-07-05 (Path B) before redesign-r1 started. |

## 3. Trigger re-points executed alongside this close-out

| Trigger | Old | New |
|---|---|---|
| **R4 daily-update graduation gate** (`projects/spaarke-daily-update-service-r4/current-task.md` open items) | "R7 ships FR-15 / /narrate UAT via R7 W10 T101" | Resolves at **`spaarke-ai-architecture-redesign-r1` Phase P3 — FR-P3-04, gate G-P3** (Issue #550) |
| **Action Engine R1 resumption** (held at Phase 0 per R7 Q14) | "holds at Phase 0 spike until R7 ships" | Re-based on redesign-r1 confirmation gate + Binding model; resumes after **gate G-P3** — see [`../../ai-spaarke-action-engine-r1/notes/rebase-on-ai-redesign-r1-stub.md`](../../ai-spaarke-action-engine-r1/notes/rebase-on-ai-redesign-r1-stub.md) |
| **insights-engine-r3 resumption** (paused on "R6 ships Pillars 3+5+6"; R6→R7→redesign supersession chain) | R6 pillar condition | Condition 1 becomes **`spaarke-ai-architecture-redesign-r1` passes gate G-P3** (FR-P3-01 Insights `ask`/`search` as Bindings; FR-P1-05 engine-output→ledger adapter ships at P1) |

## 4. Traceability

- Redesign-r1 spec: [`projects/spaarke-ai-architecture-redesign-r1/spec.md`](../../spaarke-ai-architecture-redesign-r1/spec.md) (FR-P0-11 mandates this note)
- Redesign-r1 migration map (frozen audit input): [`projects/spaarke-ai-architecture-redesign-r1/notes/audit-inputs/SPAARKE-AI-MIGRATION-MAP.md`](../../spaarke-ai-architecture-redesign-r1/notes/audit-inputs/SPAARKE-AI-MIGRATION-MAP.md)
- Portfolio: Issue [#501](https://github.com/spaarke-dev/spaarke/issues/501) (closed with this map as rationale) · Issue [#550](https://github.com/spaarke-dev/spaarke/issues/550) (successor) · Epic [#421](https://github.com/spaarke-dev/spaarke/issues/421)
- Dropped items (Persona, Node Skills, Node Knowledge) are re-proposable via `/project-defer-issue-tracking` against redesign-r1 — dropping here is a portfolio decision, not a design rejection.
