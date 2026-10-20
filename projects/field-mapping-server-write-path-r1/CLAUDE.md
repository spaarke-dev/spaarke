# field-mapping-server-write-path-r1 — project operating manual

> Read with `current-task.md` (current state). Repo-wide rules are in root `CLAUDE.md`; this file holds only what is
> specific to this project. Guidance: `.claude/skills/project-setup/references/claudemd-template.md`.

## 1. Scope and status

The BFF becomes the single owner of what a new record inherits (I-3 field mapping, fill-only), its search-index
default (I-5) and its owner (I-6, record-first). One `RecordCreationService` (Create + Repair modes) replaces the two
BFF create cores. Every product creator moves onto it, the client mapping engine is deleted, `/push` runs on the same
Copy logic, and a forward-only scheduled repair pass covers records created outside the product.

**Out of scope**:
- external-portal creates (external-access-r3);
- historic backfill;
- `/push` performance (#2098);
- removing `prvAssign` from roles;
- a change-signal channel;
- I-2 logic itself;
- field-mapping schema changes;
- wizard UI redesign;
- a runtime kill switch.

Status: see `tasks/TASK-INDEX.md` and `current-task.md`. Spec: `spec.md`. Design: `design.md`. Plan: `plan.md`.
Evidence (file:line): `notes/design-review-2026-10-19.md`. Line numbers drift; re-grep before use.

## 2. Binding rules for this project

- MUST apply field mapping server-side, inline, in the create request; the client never maps (FR-02, FR-22; WP-1/WP-2).
- MUST be **fill-only** at create. A caller-supplied value wins. "Not supplied" means:
  - **scalars**: absent, null or empty/whitespace;
  - **lookups**: no bind;
  - **booleans and option sets**: absent only.

  (FR-02)
- MUST resolve the mapping source as: explicit `mappingSource`, else the filed-under parent (regarding pair first, then
  the single typed parent lookup). Ambiguous → no source + a warning (FR-03).
- MUST probe **Read as the caller** on the source before reading it. Denied → no mapping + a warning, never a refusal
  and never an existence leak (FR-04).
- MUST run caller checks on the **merged** payload:
  - a caller-supplied value that fails → refuse (uniform 404/403);
  - a **mapped** value that fails AppendTo or field security → dropped with a warning; the create proceeds.

  (FR-08)
- MUST resolve the owner record-first via `ResolveOwnerAsync` for roots and children. Never user- or app-owned except
  a named registry exception (FR-09).
- MUST keep re-file behaviour on push: `ReparentAsync`, the root filing gate, restamp, `AfterRefileAsync` (FR-23).
- MUST make the repair pass forward-only, fill-only, idempotent, leased and heartbeated (FR-25; ADR-036). Never write
  a row created before go-live.
- MUST NOT add a plugin, business rule, flow or OOB relationship mapping as an invariant owner (ADR-002).
- MUST NOT add a NuGet package or a hand-rolled timer `BackgroundService` (NFR-05; ADR-052).
- MUST NOT write to `.claude/` from a sub-agent (root §3). Task 062 edits root `CLAUDE.md` + `.claude/CHANGELOG.md` in
  the main session.
- Every BFF-touching task:
  - states its Placement Justification (`.claude/constraints/bff-extensions.md`);
  - measures the publish-size delta against a FRESH master build (root §10.4: Compress-Archive, short path,
    file-count check);
  - runs `dotnet list package --vulnerable --include-transitive`.
- Security-sensitive tasks (FR-06…FR-09, FR-25) run at FULL rigor and get an adversarial review (NFR-01).
- Deferred work and newly found issues: `/project-defer-issue-tracking` writes `notes/defer-issues.md` AND a GitHub
  issue — never one without the other.

ADR tensions approved for this project (root CLAUDE.md §6.5):
- **ADR-008 — path A** — payload-dependent create checks live in `CallerWriteAuthorization`, called by the service.
  Route-level authorization stays in endpoint filters. This continues the `OwnedChildWrite` / UAC-r2 task 147
  precedent. Cite it in each W2/W3 PR description. See spec "ADR Tensions".

## 3. Owner directives and standing decisions

All dated 2026-10-19 unless noted; rationale in design.md §7 and spec "Owner Clarifications".

- Architecture: one service on `OwnedChildWrite`'s authorization model; Office `RecordCreationService` folds in and is
  deleted (Q8).
- Configuration surface: the existing `sprk_fieldmappingprofile` / `sprk_fieldmappingrule` tables and their native
  forms. No new schema (Q1a).
- Push: Copy rules only, selected by `sprk_syncmode` = Manual Refresh. Existing Copy rules migrate to Manual Refresh
  (Q3).
- Precedence: fill-only, caller wins (Q6). Source: inferred from the filed-under parent, explicit override (Q7).
- Owner: record-first everywhere (Q9). Office Project-under-Matter changes owner to the Matter's BU team; call this out
  in release notes.
- Path scope: all four bespoke groups (Office; Events + Analysis routes; invoice extraction; system creators) (Q10).
- OOB Assign: hidden on standard-owned tables; the repair pass restores ownership; `prvAssign` removal is a UAC-r2
  follow-up after an audit (Q12).
- A mapped value failing AppendTo or field security is skipped with a warning.
- **No backfill**: forward-only.
- Rollout: per-wizard cutover, Matter first. Rollback is redeploying the previous bundle; no runtime flag.
- Matter number: platform autonumber (owner 2026-10-02, task 076). The wizard stops generating `{typeCode}-{random6}`.
- OOB relationship mappings (EntityMaps) are form prefill only and never an invariant owner. Remove the
  `businessunit → sprk_matter` map. Recommend a quick view when a child only needs to *show* a parent value.

## 4. Coordination

- **unified-access-control-r2**: owns `OwnedChildWrite.cs` (re-file half), the census, re-file and route-authorization
  guards, and roles.
  - Open PRs **#2110** (`OwnedChildWrite.cs`, `ChildRecordEndpoints.cs`, `CommunicationService.cs`,
    `RefileWriterAfterRefileGuardTests`, `DATAVERSE-WRITE-PATH-ARCHITECTURE.md`) and **#2078**
    (`RouteAuthorizationGuardTests.*`).
  - Task 010 records sign-off and timing. Run `/conflict-check` before every W2 task. `RefileAsync` stays theirs.
  - Re-owning non-secure rows (repair pass) and the `prvAssign` audit are coordinated with them.
- **spaarke-ontology-platform-r1**: chat tools, decision route cores, `SignalWriter`, `DecisionRecordWriter`. Re-point,
  don't redesign. Open ontology PRs #1493/#1494/#1507 are in the notification area.
- **PR #1501** (`fix/136-create-analysis-hub-404`): edits `CreateAnalysisWizardWidget.tsx` and
  `ChildRecordEndpoints.cs`. Rebase before 028/020.
- **PR #2104** (UAC-r2): edits `CreateProjectWizard/closureService.ts` + `CloseProjectDialog.tsx`. Task 022 must not
  touch the closure files.
- **PR #1911** (ebilling-r1): edits `DATAVERSE-WRITE-PATH-ARCHITECTURE.md`. Merge-aware in 060/062.
- **external-access-r3**: `ExternalDataService` creates are an inventory exception; they adopt the mapping step
  themselves.

## 5. Environment and live actions

- Dev: `spaarkedev1` (Dataverse) and the dev BFF App Service.
- Deploy via skills only: `bff-deploy`, `code-page-deploy`, `dataverse-deploy`, `ribbon-edit`.
- Needs explicit owner OK:
  - the `sprk_syncmode` data migration (005) on any environment;
  - turning quick create off and hiding Assign (051) beyond dev;
  - enabling the repair pass (052/053) beyond dev;
  - any write to existing rows.
- Never delete `Dataverse-ClientSecret` / `BFF-API-ClientSecret`. Never commit secrets.
- Master is ruleset-protected: PR only. Never bare `git stash` (the stack is shared across worktrees).

## 6. Gotchas — do not re-learn

- 2026-10-19 — The design's original premise (no BFF create endpoint; owner resolver unwired) was stale. The
  child-records route and `RecordOwnershipResolver` exist. Re-verify against master before trusting older notes.
- 2026-10-19 — The profile mode column is spelled differently in code and solution (`sprk_capabilitymode` vs
  `sprk_compatibilitymode`). Use the deployed name (FR-05a).
- 2026-10-19 — The seeder filters rules on the wrong column; use `_sprk_fieldmappingprofile_value` (#1318).
- 2026-10-19 — Quick-create flags in solution XML may not match live (Project/Matter/Invoice Line Item). Verify live
  before 051 (task 050).
- 2026-10-19 — A user-owned row stays readable by Deep (Parent:Child) readers in the owner's BU and its ancestors. The
  harm of OOB Assign is the BU move and User-depth tables (e.g. `sprk_communication`), not "only the owner sees it".

## 7. Key documents

- `spec.md` · `design.md` · `plan.md` · `notes/design-review-2026-10-19.md` · `notes/decisions.md` ·
  `notes/defer-issues.md`
- Applicable ADRs: ADR-002, ADR-001, ADR-008 (path A), ADR-010, ADR-012, ADR-019, ADR-021, ADR-024, ADR-028, ADR-036,
  ADR-038, ADR-052
- Architecture: `docs/architecture/DATAVERSE-WRITE-PATH-ARCHITECTURE.md`,
  `docs/architecture/SPAARKE-FIELD-MAPPING-FRAMEWORK.md`; guide `docs/guides/FIELD-MAPPING-ADMIN-GUIDE.md`
- Related projects: unified-access-control-r2, spaarke-ontology-platform-r1, spaarkeai-word-add-in-r1,
  spaarke-SPA-external-access-platform-r3
