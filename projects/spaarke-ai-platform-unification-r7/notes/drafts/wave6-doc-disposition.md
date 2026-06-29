# Wave 6 documentation disposition list

> **Produced by**: task `060-audit-playbook-runtime-doc-outdated` (STANDARD rigor, audit-only)
> **Date**: 2026-06-28
> **Branch**: `work/spaarke-ai-platform-unification-r7`
> **Spec authority**: FR-28 (DELETE outdated R4 canonical content), FR-29 (UPDATE bff-extensions §G), FR-30 (UPDATE author/JPS guides), FR-31 (CREATE consumer-wiring guide), NFR-08 (DELETE, don't deprecate — no SUPERSEDED markers, no redirect stubs, no archive folders)
> **Scope**: docs Wave 6 touches. `docs/architecture/ai-architecture-consumer-routing.md` is EXPLICITLY OUT OF SCOPE (READ-ONLY in R7 — `chat-routing-redesign-r1` owns).

---

## Why this disposition list exists

Per CLAUDE.md §11 (component justification), the cost-of-doing-nothing for this audit: without a centralized disposition list, downstream Wave 6 tasks 061-068 would each re-discover scope of the same source docs, risking inconsistent dispositions and accidental drift. This list is the single contract that drives every Wave 6 doc edit.

Per spec FR-28: the spec deliberately did NOT enumerate every section to DELETE; that enumeration is this audit task's responsibility ("Other sections discovered during Wave 6 audit").

---

## Doc 1 — `docs/architecture/ai-architecture-playbook-runtime.md`

| Section heading (verbatim) | Lines (approx) | Disposition | Owning task | Rationale |
|---|---|---|---|---|
| (header block — Last reviewed / Authored by / Status / Scope / NOT in scope) | 1-9 | **UPDATE** | 061 | Status line still says "Canonical. Supersedes runtime sections of `playbook-architecture.md` …". Refresh Last-reviewed date to R7 ship date and add R7 disposition note. Do NOT add a SUPERSEDED marker (NFR-08). |
| **§1 Component model — five runtime layers** | 11-28 | **UPDATE** | 061 | Layer C bullet currently references `mode branch at :246-253` (Legacy vs NodeBased) — post-R7 that branch is collapsed (FR-08); strike Legacy branching reference. File:line citations across all five layers need refresh after task 024 (single-hop) + 025 (structural-fallback delete) + 026 (override-branch delete) land. |
| **§2 Four dispatch shapes** | 31-42 | **KEEP** (informational refresh only) | 061 | The four-path taxonomy (Path A / B / C / A.5) is still architecturally accurate. R7 doesn't add/remove paths; only Path A's eventual deletion is tracked elsewhere (FR-11, Wave 4 task 042). Refresh file:line citations only. |
| **§3 Mode-is-emergent contract (binding)** including Legacy-mode log-site catalog | 46-75 | **UPDATE** (substantial) | 061 | The "no `sprk_playbookmode` column" rule remains true, BUT the implication paragraph + log-site catalog assume Legacy fall-through still exists. Post-R7 the Legacy mode log site at `PlaybookOrchestrationService.cs:250` is GONE (single-hop dispatch reads `node.sprk_executortype` directly; empty-node case is just empty-graph). Strip the Legacy-vs-NodeBased framing entirely. Reduce catalog to chat-summarize (`PlaybookExecutionEngine`) + app-only fan-out (`AppOnlyAnalysisService.cs:530`) if still applicable. |
| **§4 Three config columns — no overlap** | 79-97 | **UPDATE** | 061 | Row 3 (`sprk_playbooknode.sprk_configjson`) currently says orchestrator extracts `__actionType` at `PlaybookOrchestrationService.cs:867`. Post-R7 (FR-08) the orchestrator no longer reads `__actionType` from configJson for dispatch — single-hop reads `node.sprk_executortype` instead. Rewrite read-order list (steps 1-3) accordingly. R4 deploy-bug paragraph (subsection "What this means for the R4 deploy bug") can stay as historical note OR be deleted; recommend KEEP as illustration but UPDATE the resolution paragraph to mention single-hop dispatch eliminates the symptom class. |
| **§5 Action lookup precedence** (including 3-rung ladder, AI-nodes-require-Action-FK callout, Start node semantics R4 subsection, "Other canvas-only Control nodes (R4 2026-06-26)" subsection, Per-code lookup paths paragraph, "No IsDefault flag" paragraph) | 100-153 | **DELETE (entire §5 in full)** | **061** | **FR-28 explicit mandate.** The entire precedence ladder is the load-bearing example of the structural-fallback model R7 deletes (FR-08). Subsections describing `IsDeployedStartNode`, `IsDeployedLoadKnowledgeNode`, `IsDeployedReturnResponseNode` are direct descriptions of code that task 025 deletes. The Start/LoadKnowledge/ReturnResponse executor descriptions remain valid (executors are registered unconditionally) but belong in a NEW shorter section "Node dispatch" (post-R7 single-hop model) authored fresh by task 061. The per-code-lookup paragraph re `PlaybookExecutionEngine.cs:475` chat-summarize FK chain stays relevant ONLY until Wave 9 task 091 migrates chat-summarize; flag as orphan after that. |
| **§6 Scope arrays are ADVISORY, not enforcing** | 156-168 | **KEEP** | (none — leave-alone) | Scope-array advisory semantics unchanged by R7. Refresh file:line citations only if line numbers shifted after task 024. |
| **§7 Empty-payload contract** | 172-194 | **KEEP** (small refresh) | 061 | The NodeBased empty-payload contract remains valid. The "Legacy (pre R4 hotfix)" row is obsolete post-R7 (Legacy path eliminated by FR-11 in Wave 4 task 042). When Wave 4 lands, this row should be removed. Task 061 is the earlier Wave 6 pass — note the dependency: §7's Legacy row stays until Wave 4 deletes the code, then a follow-up doc pass removes it. Or: task 061 deletes the Legacy row preemptively and adds a forward-looking note. **Recommendation: delete the Legacy row in task 061 since the code-delete is already committed to.** |
| **§8 Two parallel orchestrators** | 198-207 | **UPDATE** (substantial) | 061 | The "TWO orchestrators" framing assumes Legacy `AnalysisOrchestrationService` is still in active use. Post-R7 (Wave 4 task 042 deletes `ExecuteAnalysisAsync` + cascading dead code), `AnalysisOrchestrationService` shrinks to its remaining responsibilities only. `PlaybookExecutionEngine` retains its chat-summarize role until Wave 9 task 091 migrates that too. Rewrite to one orchestrator + chat-summarize-engine residual, with R5/R6/R7 evolution narrative collapsed. |
| **§9 NodeType (5 values) vs ActionType (31 enum values) — two orthogonal axes** | 211-229 | **UPDATE** (substantial) | 061 | Heading itself outdated — post-R7 the enum is renamed `ExecutorType` (FR-10, Wave 2 task 022 ✅ already executed). Rename throughout. Heading should be: "NodeType (5 values) vs ExecutorType (33 enum values)". The "dispatch axis priority is: action FK → ConfigJson `__actionType` → NodeType-default" sentence at the end is DELETED (this IS the 3-rung ladder of §5). Replace with: "Dispatch reads `node.sprk_executortype` directly (single-hop, FR-07)." |
| **§10 Known runtime pitfalls (moved from AI-ARCHITECTURE.md)** | 233-242 | **KEEP** | (none) | G1-G6, G12 pitfalls are orthogonal to dispatch reform. Stay verbatim. |
| **§11 Relationship to other canonical docs** | 246-257 | **KEEP** (small refresh) | 061 | Table is accurate. Replace the bottom line "`playbook-architecture.md` is now a one-line redirect to this doc" with the post-R7 reality (the redirect may or may not still exist depending on R7 cleanup scope — verify during task 061 execution). |

**Summary for Doc 1**: **1 full-section DELETE (§5)** + **6 UPDATE sections** (§1, §3, §4, §7 partial, §8, §9) + **3 KEEP sections** (§2, §6, §10) + **2 KEEP-with-refresh** (§11, header). All UPDATEs flow into task 061.

---

## Doc 2 — `docs/architecture/ai-architecture-actions-nodes-scopes.md`

| Section heading (verbatim) | Lines (approx) | Disposition | Owning task | Rationale |
|---|---|---|---|---|
| (header block — Last reviewed / Authored by / Status / Scope / NOT in scope) | 1-9 | **KEEP** (refresh date only) | 062 | Status framing as "**Binding**" remains correct. Bump Last-reviewed date. |
| **§1 Why this doc exists** | 11-19 | **KEEP** | (none) | The three concrete design smells (node-level wire-up in Action config, scope decisions in node configjson, node-graph data in playbook configjson) are unchanged by R7. |
| **§2 The four homes for playbook config** | 23-34 | **UPDATE** | 062 | Home A row currently lists `sprk_ActionTypeId FK (executor selector)` — that FK is DROPPED in Wave 4 tasks 042-044 per FR-03/FR-04. Strike. Per the §8a interim note (lines 193-207, see below), executor-selection responsibility moves to Home C (`sprk_playbooknode.sprk_executortype`). Home C row should ADD `sprk_executortype` (Choice column — single-hop dispatch identity per FR-07). Both edits required for §2 to align with R7 truth. |
| **§3 `sprk_playbooknode.sprk_configjson` — the canonical per-node config column** | 38-56 | **UPDATE** | 062 | "Canonical contents" bullet 1 still says `__actionType` is structural fallback for action-type detection. DELETE that bullet entirely (FR-08 eliminates the fallback). Also delete the read-site sentence "The orchestrator extracts `__actionType` at `PlaybookOrchestrationService.cs:867`." Replace with note that `__actionType` is no longer read at runtime. |
| **§4 The decision tree** (the 4-Home decision tree FR-28 explicitly names) | 60-87 | **UPDATE** | **062** | **FR-28 explicit mandate.** Tree's third branch (Home C "per-node runtime config, …executor-specific input shape") is correct but needs to mention `sprk_executortype` as the dispatch identity. Recommend a small inline note at the Home C branch: "(includes `sprk_executortype` for single-hop dispatch — see ai-architecture-playbook-runtime.md)." The decision tree's logic is unchanged; the home-content enumeration is what needs touching. |
| **§5 Anti-patterns — what to avoid (with evidence)** + all `❌` subsections | 91-132 | **KEEP** | (none) | Anti-patterns (node-wire-up in Action config, scope decisions in node configjson, node-graph in playbook configjson, routing config tech-debt, DeliverComposite `sections[]` tech-debt) are all unchanged by R7. R7 does NOT introduce new anti-patterns. |
| **§6 Scope-array semantics — declarative, not enforced** | 134-145 | **KEEP** | (none) | Unchanged. |
| **§7 Cross-check examples (using R4 surfaces)** | 147-160 | **KEEP** | (none) | Example rows unchanged. The "Which Action a node uses" row points to `sprk_actionid` (FK) — still correct: R7 makes Action FK OPTIONAL but does not eliminate it (per Q-RESOLVED about prompt-driven executors requiring Action FK at Validate time). |
| **§8 ActionType allocation policy (R5 will codify)** | 164-189 | **UPDATE** | 062 | Heading currently says "ActionType allocation policy" — rename to "ExecutorType allocation policy" (FR-10, Wave 2 task 022 ✅ already executed). Throughout: `ActionType` → `ExecutorType`. The allocation table (cluster ranges) is unchanged numerically; only the term rename is required. |
| **§8a `sprk_analysisactiontype` lookup table — R7 disposition (FR-05)** | 193-207 | **KEEP** | (none — already R7-aware) | This subsection was added by Wave 4 task 045 (FR-05) — already aligned with R7 model. It is the binding interim disposition note. Wave 6 task 062 may collapse §8a back into §8 once §8 is rewritten, or leave §8a standalone. Recommendation: KEEP standalone for traceability. |
| **§9 Relationship to other canonical docs** | 211-220 | **KEEP** | (none) | Cross-reference table unchanged. |
| **§10 Binding summary** | 224-231 | **KEEP** | (none) | The "every project's design.md MUST state which Home" rule is unchanged. |

**Summary for Doc 2**: **0 full-section DELETE** + **4 UPDATE sections** (§2, §3, §4, §8) + **7 KEEP sections** (§1, §5, §6, §7, §8a, §9, §10) + **1 KEEP-with-refresh** (header). All UPDATEs flow into task 062.

---

## Doc 3 — `docs/guides/ai-guide-playbook-deploy-recipe.md`

| Section heading (verbatim) | Lines (approx) | Disposition | Owning task | Rationale |
|---|---|---|---|---|
| (header block) | 1-10 | **KEEP** (refresh date only) | 063 | Refresh Last-reviewed date. |
| **§1 What `Deploy-Playbook.ps1` does** | 11-24 | **KEEP** | (none) | High-level description unchanged. |
| **§2 Input file format** + JSON example block | 26-77 | **UPDATE** | **063** | **FR-28 mandate (name-detection workaround target).** The example node block at lines 65-69 contains `"__actionType": 11, // structural fallback (orchestrator uses FK first)` — this line is the **`__actionType` injection workaround** the spec names. POST-R7: `__actionType` is no longer read at runtime; replace with `"sprk_executortype": 11, // canonical dispatch (single-hop, FR-07)`. Also: the inline comment beneath ("structural fallback (orchestrator uses FK first)") must be deleted — structural fallback is gone. Update Wave 5 task 055 also updates `Deploy-Playbook.ps1` itself to write `sprk_executortype` explicitly — this doc edit aligns the recipe. |
| **§3 The 12-step deploy procedure** (table rows 1-12) | 81-99 | **UPDATE** (light) | 063 | Step 1 lint reference at `Deploy-Playbook.ps1:296-356` (the actionCode lint) is unchanged. Step 8 references writing `sprk_actionid@odata.bind` — confirm this stays accurate (Action FK becomes optional in R7 per FR-05; deploy script still writes it when present per Wave 5 task 055). The lint behavior may shift (deploy task 055 may add a new lint for `sprk_executortype` presence) — track this and update step 1 + step 8 in task 063 only after task 055 lands. |
| **§4 The `sprk_isactive` rule (load-bearing)** | 102-106 | **KEEP** | (none) | Unchanged. Still load-bearing. |
| **§5 Skip-vs-`-Force` behaviour (not a true upsert)** | 110-127 | **KEEP** | (none) | Unchanged. |
| **§6 Failure recovery — no rollback** | 131-145 | **UPDATE** (small) | 063 | "Playbook row exists, no nodes" row currently mentions Path A.5 → 503 `PLAYBOOK_INVOCATION_FAILED`. Post-R7 Wave 4 task 042 deletes `ExecuteAnalysisAsync` — the Legacy fall-through is gone. The empty-nodes case becomes simpler: no Legacy fall-back exists; the node-graph just runs empty. Refresh the consequences row. |
| **§7 Critical pre-conditions** | 148-157 | **KEEP** | (none) | Unchanged. |
| **§8 Verification queries (post-deploy smoke checks)** | 159-184 | **UPDATE** (small) | 063 | After Wave 4 schema changes (drop `sprk_actiontypeid` + `sprk_executoractiontype`), any verification query referencing those columns must be updated. Quick scan: verification queries reference `sprk_playbooknode` rows, `sprk_isactive`, action codes — all unchanged. **Likely no edit needed**; flag for verifier. |
| **§9 Troubleshooting — "Playbook has no nodes — using Legacy mode"** | 186-198 | **UPDATE** (substantial) | 063 | The entire heading + framing is obsolete post-R7 — there is no "Legacy mode" log to triage (`PlaybookOrchestrationService.cs:250` is deleted by Wave 2 task 024 + Wave 4 task 042 cascading). Rewrite the section as: "Troubleshooting — playbook executes with zero nodes" (the empty-node-graph diagnostic remains useful for ops). Strip all references to Legacy mode / `_legacyOrchestrator` / fall-through. |
| **§10 Coordinated deploy — Action rows before playbook rows before consumer rows** | 202-211 | **KEEP** | (none) | Dependency order unchanged. |
| **§11 Customer/external context** | 215-219 | **KEEP** | (none) | Unchanged. |
| **§12 Relationship to other canonical docs** | 223-234 | **KEEP** (small refresh) | 063 | First row references `ai-architecture-playbook-runtime.md` §3 (mode detection + log site catalog) — §3 itself is UPDATED by task 061 (no longer about Legacy mode). Update this row's description accordingly. |

**Summary for Doc 3**: **0 full-section DELETE** + **5 UPDATE sections** (§2, §3 light, §6, §9 substantial, §12 small) + **7 KEEP sections** (§1, §4, §5, §7, §8, §10, §11) + **1 KEEP-with-refresh** (header). All UPDATEs flow into task 063.

---

## Cross-doc tally

| Disposition | Doc 1 (playbook-runtime) | Doc 2 (actions-nodes-scopes) | Doc 3 (deploy-recipe) | TOTAL |
|---|---|---|---|---|
| **DELETE (full section)** | 1 (§5) | 0 | 0 | **1** |
| **UPDATE (substantial)** | 6 (§1, §3, §4, §7-partial, §8, §9) | 4 (§2, §3, §4, §8) | 3 (§2, §6, §9) | **13** |
| **UPDATE (light / refresh-only header)** | 2 (header, §11) | 1 (header) | 3 (§3 light, §8 maybe, §12 small) + header | **6-7** |
| **KEEP (unchanged)** | 3 (§2, §6, §10) | 7 (§1, §5, §6, §7, §8a, §9, §10) | 7 (§1, §4, §5, §7, §10, §11, §8 maybe) | **17** |

**Net Wave 6 doc-edit volume**: ~14 substantial edits + ~7 refresh-only edits, distributed across tasks 061 / 062 / 063.

---

## Ownership map — Wave 6 tasks 061-068

| Task | Scope | Sourced from this audit |
|---|---|---|
| **061** | DELETE §5 of playbook-runtime + structural-fallback descriptions; UPDATE §1, §3, §4, §7-partial, §8, §9 of playbook-runtime; refresh §11 + header | Doc 1 |
| **062** | UPDATE §2, §3, §4 (the 4-Home decision tree FR-28 names), §8 of actions-nodes-scopes; refresh header | Doc 2 |
| **063** | UPDATE §2 (name-detection / `__actionType` workaround removal — FR-28 mandate), §3 light, §6, §9 substantial, §12 small of deploy-recipe; refresh header. Depends on Wave 5 task 055 (Deploy-Playbook.ps1 update) for lint behavior wording. | Doc 3 |
| **064** | UPDATE `.claude/constraints/bff-extensions.md` §G — FR-29. **Not in this audit's scope.** | (out-of-scope for this audit) |
| **065** | MAJOR UPDATE `docs/guides/JPS-AUTHORING-GUIDE.md` — FR-30. **Not in this audit's scope.** | (out-of-scope for this audit) |
| **066** | MAJOR UPDATE `docs/guides/PLAYBOOK-AUTHOR-GUIDE.md` — FR-30. **Not in this audit's scope.** | (out-of-scope for this audit) |
| **067** | CREATE `docs/guides/ai-guide-consumer-wiring.md` — FR-31. **Not in this audit's scope** (new file, no audit possible). | (out-of-scope for this audit) |
| **068** | UPDATE root `CLAUDE.md` if entry-points table affected. Likely small. Out of this audit's primary scope but a passing observation: root CLAUDE.md §13 entry-points table at line ~441 references `PlaybookOrchestrationService.cs` as the AI Pipeline starting point — this stays accurate; the entry-point itself is unchanged. **Likely no edit required for task 068**, but task 068 should run a Grep for any cross-doc reference to "Legacy mode" / `ExecuteAnalysisAsync` / structural fallback / `__actionType` in `CLAUDE.md` and trim. | (passing observation only) |
| **069** | Post-audit: grep `docs/` for new "deprecated"/"superseded" instances per NFR-08 | (gate, no audit input) |

---

## Newly-discovered sections beyond spec-named scope

Per FR-28 the audit calls out items beyond §5 + structural-fallback + 4-Home tree + name-detection. The following surfaced during this read:

1. **Doc 1 §3 Mode-is-emergent contract Legacy-log-site catalog** (lines 67-75) — three log-site rows. Post-R7 the first row (`PlaybookOrchestrationService.cs:250`) disappears; the second row (`AnalysisOrchestrationService.cs:970`) disappears after Wave 4 task 042; the third row (`AppOnlyAnalysisService.cs:530`) MAY survive (app-only scheduler path — needs Wave 9 verification). Task 061 deletes rows 1+2 preemptively; flags row 3 for verifier.
2. **Doc 1 §8 Two parallel orchestrators framing** (lines 198-207) — needs substantial UPDATE per above.
3. **Doc 1 §9 final sentence**: "The dispatch axis priority is: action FK → ConfigJson `__actionType` → NodeType-default. The Deploy script lint pushes every dispatchable node to fall on the first rung (FK)." — DELETE both sentences; this is the §5 ladder restated and gone post-R7.
4. **Doc 2 §3 final note about `__actionType` extraction at `PlaybookOrchestrationService.cs:867`** — DELETE per above.
5. **Doc 3 §2 JSON example `__actionType: 11`** — the spec-named "name-detection workaround" target. UPDATE to `sprk_executortype`.
6. **Doc 3 §9 entire troubleshooting framing** — Legacy mode no longer exists; rewrite as "empty node-graph" troubleshooting.

All six items are folded into the owning-task table above.

---

## Constraints respected

- ✅ Audit-only — no `docs/architecture/*` or `docs/guides/*` file was modified by this task.
- ✅ Per CLAUDE.md §3 — writes only to `projects/spaarke-ai-platform-unification-r7/notes/drafts/`.
- ✅ `docs/architecture/ai-architecture-consumer-routing.md` is EXPLICITLY EXCLUDED (READ-ONLY in R7; `chat-routing-redesign-r1` owns).
- ✅ Per NFR-08 — every disposition is DELETE or UPDATE; no row recommends adding a SUPERSEDED marker, redirect stub, or archive folder.
- ✅ Spec-named sections confirmed:
  - playbook-runtime.md §5 action-lookup precedence ladder → **DELETE (task 061)** ✅
  - playbook-runtime.md structural-fallback section → **DELETE (folded into §5 deletion + §3/§4/§9 updates, task 061)** ✅
  - actions-nodes-scopes.md 4-Home decision tree (§4) → **UPDATE (task 062)** ✅
  - deploy-recipe.md Control-flow name-detection (`__actionType` injection in §2 + Legacy-mode framing in §9) → **UPDATE (task 063)** ✅

---

## Disposition table — at-a-glance summary

```
Doc 1 (playbook-runtime.md):  1 DELETE + 6 UPDATE + 3 KEEP + 2 refresh
Doc 2 (actions-nodes-scopes.md): 0 DELETE + 4 UPDATE + 7 KEEP + 1 refresh
Doc 3 (deploy-recipe.md):        0 DELETE + 5 UPDATE + 7 KEEP + 1 refresh
                              ----------------------------------------------
TOTAL                            1 DELETE + 15 UPDATE + 17 KEEP + 4 refresh
```

Tasks 061, 062, 063 execute these dispositions. Tasks 064-068 cover other docs per their own POMLs (out of this audit's scope per scope-boundary constraint).
