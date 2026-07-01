# Wave 6 NFR-08 Post-Audit Report

> **Date**: 2026-06-29
> **Author**: task-execute (R7-069)
> **Task**: `projects/spaarke-ai-platform-unification-r7/tasks/069-postaudit-grep-for-deprecated-superseded.poml`
> **Spec constraints verified**: NFR-08 (no SUPERSEDED markers), FR-28 (DELETE outdated content)
> **Result**: ✅ **PASS** — Zero NEW SUPERSEDED markers, deprecated language, redirect stubs, or archive folders introduced by Wave 6.

---

## Audit Scope

Per user request: limit scope to the **`docs/` tree** plus the two Wave-6-relevant hot-path files (`.claude/constraints/bff-extensions.md` and root `CLAUDE.md`). Do NOT scan `projects/`, `src/`, or other `.claude/` paths.

Wave 6 completed-to-date tasks audited:
- **060** ✅ audit/disposition (no doc writes)
- **061** ✅ DELETE §5 from `docs/architecture/ai-architecture-playbook-runtime.md`
- **062** ✅ UPDATE 4-Home decision tree in `docs/architecture/ai-architecture-actions-nodes-scopes.md`
- **065** ✅ MAJOR UPDATE `docs/guides/JPS-AUTHORING-GUIDE.md`
- **066** ✅ MAJOR UPDATE `docs/guides/PLAYBOOK-AUTHOR-GUIDE.md`
- **067** ✅ CREATE `docs/guides/ai-guide-consumer-wiring.md`

Wave 6 pending tasks (not in scope of this audit; will need their own post-task check):
- **063** ⏸ blocked on Wave 5 — `docs/guides/ai-guide-playbook-deploy-recipe.md`
- **064** ⏸ main-session-only — `.claude/constraints/bff-extensions.md` §G
- **068** ⏸ main-session-only — root `CLAUDE.md`

---

## Grep Commands Run

```text
# 1. docs/ tree, case-insensitive, deprecated|superseded
Grep pattern="deprecated|superseded" path="docs/" -i=true output_mode="content" -n=true

# 2. docs/ tree, exact case, SUPERSEDED|redirect stub|archive folder
Grep pattern="SUPERSEDED|redirect stub|archive folder" path="docs/" output_mode="content" -n=true

# 3. Per Wave-6 modified file, case-insensitive (5 files)
Grep pattern="deprecated|superseded|SUPERSEDED" -i=true (per file)

# 4. Hot-path files (Wave 6 tasks 064 + 068)
Grep pattern="deprecated|superseded|SUPERSEDED" path=".claude/constraints/bff-extensions.md"
Grep pattern="deprecated|superseded|SUPERSEDED" path="CLAUDE.md"

# 5. Redirect-stub / archive-folder smell, per Wave-6 modified file
Grep pattern="archive folder|redirect stub|moved to|see new|see updated|see also.*supersed" -i=true (per file)
```

---

## Wave-6-Touched Files: PER-FILE PASS/FAIL

| Wave 6 file | Task | "deprecated"/"superseded" hits | Redirect/archive language | Verdict |
|---|---|---|---|---|
| `docs/architecture/ai-architecture-playbook-runtime.md` | 061 (DELETE §5) | 1 (line 72, PRE-EXISTING) | None | ✅ PASS |
| `docs/architecture/ai-architecture-actions-nodes-scopes.md` | 062 (UPDATE decision tree) | 0 | None | ✅ PASS |
| `docs/guides/JPS-AUTHORING-GUIDE.md` | 065 (MAJOR UPDATE) | 1 (line 1193, NFR-08 rule citation) | None | ✅ PASS |
| `docs/guides/PLAYBOOK-AUTHOR-GUIDE.md` | 066 (MAJOR UPDATE) | 0 | None | ✅ PASS |
| `docs/guides/ai-guide-consumer-wiring.md` | 067 (CREATE) | 0 | None | ✅ PASS |
| `.claude/constraints/bff-extensions.md` | 064 (PENDING) | 0 | None | N/A — task not run |
| `CLAUDE.md` (root) | 068 (PENDING) | 0 | None | N/A — task not run |

---

## Detailed Classification of the 2 Hits in Wave-6-Touched Files

### Hit 1: `docs/architecture/ai-architecture-playbook-runtime.md:72`

```text
| `AnalysisOrchestrationService.cs:970` | `"DEPRECATED Legacy mode: No nodes found for playbook ..."` | Deeper warning emitted by the legacy orchestrator after JIT canvas sync also yields zero nodes |
```

- **Classification**: PRE-EXISTING
- **Git blame**: introduced by commit `f91981965` on 2026-06-26 (canonical-truth doc commit, BEFORE Wave 6 task 060)
- **Nature**: code-literal log message quoted in doc as triage reference (the BFF logs this exact string when entering Legacy mode)
- **NFR-08 verdict**: ✅ ALLOWED — explicitly excepted per task POML `<constraint source="false-positive-classification">` ("code-comment examples", "operational guides referencing third-party deprecation" — here, internal BFF log messages quoted for incident triage). Removing this quote would harm operations docs.
- **Recommendation**: LEAVE AS-IS

### Hit 2: `docs/guides/JPS-AUTHORING-GUIDE.md:1193`

```text
| NFR-08 | Documentation discipline (DELETE / UPDATE in place — no SUPERSEDED markers) |
```

- **Classification**: NEW (introduced by Wave 6 task 065 commit `aa85c97e4`)
- **Nature**: **rule citation** in an FR/NFR cross-reference table at the END of the rewritten guide. The cell explains *why* the document was rewritten in place rather than archived.
- **NFR-08 verdict**: ✅ ALLOWED — this is NOT a SUPERSEDED marker on the document; it is the *definition* of the NFR-08 rule. Discussing the rule is required for the rule to be teachable. The task POML explicitly states "Pre-existing matches (e.g., historical commit messages quoted in docs, references to deprecated third-party APIs in operational guides) carry forward unchanged" — and a rule citation is in the same spirit.
- **Recommendation**: LEAVE AS-IS

---

## Broader `docs/` Tree: Existing Hits Inventory (PRE-EXISTING, NOT Wave 6)

The following files contain `deprecated`/`superseded`/`SUPERSEDED` markers that PREDATE Wave 6 (verified by inspection: NONE of them were modified by R7 Wave 6 commits `026b1d6e3`, `cfe789039`, `aa85c97e4`, `5c54f4941`, `e56984801`). These are out-of-scope for NFR-08 (R7 only governs documents R7 itself authors or modifies):

| File | Hits | Nature (sample) |
|---|---:|---|
| `docs/architecture/INDEX.md` | 1 | SidePane catalog row tagged SUPERSEDED (pre-existing index entry) |
| `docs/architecture/SIDE-PANE-PLATFORM-ARCHITECTURE.md` | 2 | Standalone SUPERSEDED-stub doc (pre-R7) |
| `docs/architecture/event-to-do-architecture.md` | 2 | Standalone SUPERSEDED-stub doc (pre-R7, 2026-06-10) |
| `docs/architecture/playbook-architecture.md` | 1 | Standalone SUPERSEDED doc (pre-R7, from chat-routing-redesign-r1 / R4 era) |
| `docs/architecture/universal-dataset-grid-architecture.md` | 2 | Pre-R7 SUPERSEDED |
| `docs/architecture/LEGALWORKSPACE-RETIREMENT.md` | 3 | Pre-R7 retirement doc |
| `docs/architecture/SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md` | 2 | Pre-R7 R3 FR-25/NFR-10 supersession |
| `docs/architecture/LEGALWORKSPACE-EMBEDDED-MODE-CONTRACT.md` | 1 | Pre-R7 R3 supersession |
| `docs/architecture/auth-AI-azure-resources.md` | 1 | Pre-R7 (text-embedding-3-small DEPRECATED) |
| `docs/architecture/auth-azure-resources.md` | 1 | Pre-R7 (app reg deprecated label) |
| `docs/architecture/external-access-spa-architecture.md` | 2 | Pre-R7 (implicit grant deprecated) |
| `docs/architecture/AI-SEARCH-INDEX-CATALOG.md` | 1 | Pre-R7 (knowledge-index v1 superseded) |
| `docs/architecture/INSIGHTS-ENGINE-ARCHITECTURE.md` | 7 | Pre-R7 (Precedent lifecycle Deprecated state) |
| `docs/architecture/email-processing-architecture.md` | 1 | Pre-R7 (Communication Service R2 superseded the old handler) |
| `docs/architecture/sdap-pcf-patterns.md` | 2 | Pre-R7 (Custom Page wrapper deprecated by ADR-006) |
| `docs/architecture/caching-architecture.md` | 3 | Pre-R7 (sdap: prefix deprecated by ADR-009) |
| `docs/architecture/client-resources-inventory.md` | 4 | Pre-R7 (10 deprecated PCFs) |
| `docs/architecture/spaarke-todo-architecture.md` | 3 | Pre-R7 (sprk_eventtodo deprecated; classic Outlook add-in deprecated by MS) |
| `docs/architecture/VISUALHOST-ARCHITECTURE.md` | 1 | Pre-R7 (GradeMetricCard DEPRECATED) |
| `docs/adr/ADR-003-lean-authorization-seams.md` | 1 | Pre-R7 ADR amendment |
| `docs/adr/ADR-009-caching-redis-first.md` | 1 | Pre-R7 (sdap: brand deprecated) |
| `docs/adr/ADR-023-choice-dialog-pattern.md` | 2 | Pre-R7 ADR superseded by pattern file (2026-03-19) |
| `docs/adr/ADR-026-full-page-custom-page-standard.md` | 1 | Pre-R7 (Custom Page wrapper deprecated) |
| `docs/adr/ADR-VALIDATION-PROCESS.md` | 6 | Pre-R7 (process doc — explains how to write superseded ADRs) |
| `docs/data-model/schema-corrections.md` | 1 | Pre-R7 (active until superseded — content describing data semantics) |
| `docs/data-model/sprk_document-search-index-lifecycle.md` | 1 | Pre-R7 (legacy sibling field) |
| `docs/data-model/sprk_userentityassociation.md` | 1 | Pre-R7 (deprecated D-target) |
| `docs/data-model/sprk_financial-related-entities.md` | 3 (omitted) | Pre-R7 |
| `docs/guides/AI-EMBEDDING-STRATEGY.md` | 3 | Pre-R7 (embedding model deprecated) |
| `docs/guides/AI-DEPLOYMENT-GUIDE.md` | 3 | Pre-R7 (deployment catalog) |
| `docs/guides/AZURE-SETUP-SELF-SERVICE-REGISTRATION.md` | 1 | Pre-R7 (DemoProvisioning__Environments deprecated) |
| `docs/guides/CONFIGURATION-MATRIX.md` | 5 | Pre-R7 (config matrix lists deprecated settings) |
| `docs/guides/COMMUNICATION-DEPLOYMENT-GUIDE.md` | 1 | Pre-R7 (Auth v2 webhook secret deprecated) |
| `docs/guides/DATAVERSE-AUTHENTICATION-GUIDE.md` | 2 | Pre-R7 (ADAL deprecated) |
| `docs/guides/ENVIRONMENT-DEPLOYMENT-GUIDE.md` | 1 | Pre-R7 (auth setup superseded by Auth v2) |
| `docs/guides/HOW-TO-ADD-SDAP-TO-NEW-ENTITY.md` | 1 (omitted) | Pre-R7 |
| `docs/guides/SECRET-ROTATION-PROCEDURES.md` | 3 | Pre-R7 (Auth v2 secret rotation) |
| `docs/guides/SPAARKE-SELF-SERVICE-USER-REGISTRATION.md` | 1 | Pre-R7 |
| `docs/guides/VISUALHOST-SETUP-GUIDE.md` | 1 | Pre-R7 (GradeMetricCard deprecated) |
| `docs/procedures/workflow-incident-response.md` | 2 | Pre-R7 (incident response triage for deprecated GH actions) |
| `docs/procedures/testing-and-code-quality.md` | 1 | Pre-R7 (draft superseded example) |
| `docs/repo-cleanup-guide.md` | 2 | Pre-R7 (cleanup criteria mention "superseded") |
| `docs/standards/CODING-STANDARDS.md` | 1 | Pre-R7 (Fluent v8 deprecated) |

**All hits above predate Wave 6 commits and are out of scope for NFR-08.** No action required.

---

## Total Counts

| Bucket | Count |
|---|---:|
| Wave-6-touched files with NEW SUPERSEDED markers | **0** |
| Wave-6-touched files with NEW deprecated language | **0** |
| Wave-6-touched files with redirect stubs / archive folders | **0** |
| Hits in Wave-6-touched files (classified ALLOWED per POML constraints) | 2 (1 PRE-EXISTING code-literal quote; 1 NFR-08 rule citation) |
| PRE-EXISTING hits in `docs/` (out of scope for NFR-08) | ~70+ (not enumerated exhaustively; all in non-R7-modified files) |

---

## PASS / FAIL Summary

**✅ PASS — NFR-08 verified for Wave 6 to-date completed tasks (060, 061, 062, 065, 066, 067).**

Zero NEW SUPERSEDED markers. Zero NEW redirect stubs. Zero NEW archive folders. The two hits in Wave-6-touched files are both ALLOWED per the task POML's `<constraint source="false-positive-classification">` exception (code-literal log message in operations doc; NFR-08 rule citation in cross-reference table at end of guide).

**Wave 6 sub-tasks 063, 064, 068 still pending** — they have not yet executed and are NOT yet checked. Their post-task NFR-08 verification must occur when they ship.

---

## Recommendations

1. **No fixes required**. Both hits in Wave-6-touched files are correctly classified as ALLOWED.
2. **Continue to verify** NFR-08 at Wave 6 final closeout: re-run this audit after tasks 063, 064, and 068 ship.
3. **Out-of-scope note for downstream cleanup**: the broader `docs/` tree has ~70+ pre-existing "deprecated"/"superseded" hits. These are governed by *the projects that authored them*, not R7. If a future project (e.g., `docs-cleanup-rN`) wants to consolidate or remove standalone-SUPERSEDED-stub docs (`SIDE-PANE-PLATFORM-ARCHITECTURE.md`, `event-to-do-architecture.md`, `playbook-architecture.md`, `universal-dataset-grid-architecture.md`), that's a separate scoped effort. Not an R7 concern.

---

## Audit Provenance

- Audit task: `069-postaudit-grep-for-deprecated-superseded.poml`
- Audit ran from: worktree `spaarke-wt-spaarke-ai-platform-unification-r7`, branch `work/spaarke-ai-platform-unification-r7`
- Wave 6 commits inspected: `026b1d6e3` (061), `cfe789039` (062), `aa85c97e4` (065), `5c54f4941` (066), `e56984801` (067), `35ad51437` (060 audit)
- Tools used: `Grep` (ripgrep), `git blame`, `git log --grep`
- This audit document itself contains the words "deprecated"/"superseded"/"SUPERSEDED" because it MUST quote what it is auditing for. The audit document is in `projects/.../notes/spikes/` — OUTSIDE the `docs/` tree NFR-08 governs. No NFR-08 self-violation.
