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
  - a **mapped** value that fails AppendTo or field security → dropped with a warning; the create proceeds;
  - **except a mapped filing lookup** (`SecureChildLineage` / `FilingParentOf`) that fails AppendTo → **refuse**
    (FR-08 amendment, owner 2026-10-20, UAC-r2 condition c).

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
- 2026-11-08/09 owner decisions:
  - 032 option B;
  - O1 inline; O2 gate move;
  - invoice Assigned To targets people for the Daily Briefing and notifications WITHOUT conferring access (032b-1b);
  - Repair pass: only contacts get an owner rule (the `ExternalAccess:CustomerBusinessUnitId` default team, fail closed), also for the chat contact create;
  - inbound mapping is a gap (DI-022);
  - the NFR-02 verdict moves to UAT;
  - Direct 1:1 messages are owned by the Direct team (task 054);
  - participants keep their shares when a Direct message is filed, except on secure or Restricted targets;
  - a Direct message records its own two participants (`sprk_sentby` + `sprk_directrecipient`, option A; threads do not move);
  - private messages in a record's context are deferred to `messages-enhancements-r1`.
- Mapping-source rule (022): values copied from a secure source are written only into a row owned by that source's isolation owner, on create, push and Repair, through one predicate (`MappingSourceCopyGate.DecideAsync`). For a secure source, a push that would change a child's filing skips the child whole.
- Public repo: access defects go to UAC-r2 by message only; repo text (commits, notes, comments, issues) states rules, never holes.
- Live Dataverse writes need the owner's explicit go in this project's own session; a peer cannot grant it. No deploys from this session.
- Every Agent call names a model; at most 4 agents at once. Never run Sprk.Bff.Api.Tests unfiltered: use `scripts/verify-suites.sh <repo> <logdir>`. Publish delta: `scripts/publish-delta.ps1` (main session only).
- OOB relationship mappings (EntityMaps) are form prefill only and never an invariant owner. Remove the
  `businessunit → sprk_matter` map. Recommend a quick view when a child only needs to *show* a parent value.

## 4. Coordination

**Authoritative, per file and per task: [`notes/w2-coordination.md`](notes/w2-coordination.md)** (task 010, updated
2026-10-20). Summary:

- **Every W2–W4 task:** rebase onto master and run `/conflict-check`. Whoever merges second rebases.
- **unified-access-control-r2:**
  - owns the `OwnedChildWrite.RefileAsync` half, the census, re-file and route guards, and roles;
  - conditional no-objection to W2, conditions (a)–(d);
  - reviews the 011 and 017 diffs before merge, and gets a ping before merge on refusal-code, owner or
    post-create-order changes;
  - #2110 and #2104 are merged. Task 022 still must not touch the closure files.
- **ebilling-r1** (#1911, draft, no date):
  - the invoice-family guard sits in `OwnedChildWrite` Create/Refile and in push;
  - the second lander reconciles: the create half goes to the registry (012), the refile half stays in RefileAsync;
  - single invoice creator; `InvoiceSubmissionPersister` uses fixed ids and one transaction;
  - reviews 025, 032 and the registry rule.
- **spaarke-ontology-platform-r1:**
  - #1501 is superseded;
  - 033 needs an ontology design-note review and must keep the writer invariants; no field mapping into Signal or
    Decision Record rows;
  - leaving those writers out of the service is valid.
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
- 2026-10-20 — An Agent launched with `isolation: worktree` gets a worktree cut from **origin/master**, not this
  branch. Every brief must start with `git switch -c task/fm-NNN <work-branch tip>` plus a check that the POML
  exists. The agent commits on that branch, and the main session merges it with `--no-ff` and verifies.
- 2026-10-20 — POMLs written at planning name new test files under `tests/unit/Sprk.Bff.Api.Tests/...`, which
  `.claude/constraints/testing.md:132` forbids. Use `tests/unit/domain/**` (globbed into Sprk.Bff.Api.Tests) or the
  matching KEEP path.
- 2026-11-09 — Shell calls time out under agent load: prefer Grep/Read, use timeouts ≥ 300 s and background runs; `timeout 400 git commit`. lint-staged backs up into the shared stash and drops it itself: never pop or drop a stash. Use `gh --jq`, not `jq`. Never print a token.
- 2026-11-09 — In Git Bash, `git sparse-checkout set --no-cone /path/` gets its leading `/` rewritten to `C:/Program Files/Git/...`; prefix with `MSYS_NO_PATHCONV=1`. A sparse worktree also needs `scripts/quality/`, the archive-prefix baseline in `tests/Spaarke.ArchTests/`, `package.json` and `.lintstagedrc.mjs` for the pre-commit hook to pass.
- 2026-11-09 — Web API `@odata.bind` keys use the navigation property name. The regarding pair's type lookup is `sprk_RegardingRecordType` on every pair table except `sprk_invoice` (lower case): use `OwnedChildWrite.RegardingRecordTypeNavigationPropertyOf(table)`, guarded by `PairTypeBindSpellingGuardTests`.
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
