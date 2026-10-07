# Spaarke Office Add-in (Word + Outlook) r1 — AI Context

> **Purpose**: Context for Claude Code when working on `spaarkeai-word-add-in-r1`.
> **Always load this file first** when working on any task in this project.

---

## Project Status

- **Phase**: Phase 6 — owner UAT rounds (042). **103 of 105 tasks ✅** (per `tasks/TASK-INDEX.md`, 2026-10-06); open: 042 (UAT, ongoing) and 090 (wrap-up with `/test-diet`).
- **Last Updated**: 2026-10-06
- **Live state**: [`current-task.md`](current-task.md) — the authoritative current state; this block is a summary only

---

## Quick Reference

### Key Files

- [`spec.md`](spec.md) — the contract (20 FRs, 11 NFRs, closed acceptance set)
- [`design.md`](design.md) — original design + owner decisions
- [`README.md`](README.md) — overview + graduation criteria
- [`plan.md`](plan.md) — WBS, findings, risk register
- [`current-task.md`](current-task.md) — **active task state** (context recovery)
- [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) — task tracker + wave groups
- [`ADDIN-CONTEXT-FROM-EMAIL-R2.md`](ADDIN-CONTEXT-FROM-EMAIL-R2.md) · [`DEDUP-AND-SAVE-BACK-IDENTITY.md`](DEDUP-AND-SAVE-BACK-IDENTITY.md) — handoffs

### Project Metadata

- **Project Name**: `spaarkeai-word-add-in-r1`
- **Branch**: `work/spaarkeai-word-add-in-r1`
- **Type**: Office Add-in (client) + BFF endpoints + Dataverse
- **Complexity**: High — 47 tasks (34 at generation; the rest added during execution — see `tasks/TASK-INDEX.md`), 5 phases, 4 gating spikes
- **Hot paths**: BFF=Y · SpaarkeAi=N · ci-workflows=Y · skill-directives=N · root-CLAUDE=N

### Read before touching add-in code

- [`docs/architecture/office-outlook-teams-integration-architecture.md`](../../docs/architecture/office-outlook-teams-integration-architecture.md) — as-built map
- [`src/client/office-addins/CLAUDE.md`](../../src/client/office-addins/CLAUDE.md) — module pointer + six load-bearing facts

---

## Context Loading Rules

1. Load this file first.
2. Check [`current-task.md`](current-task.md) for active state (especially after compaction).
3. Reference [`spec.md`](spec.md) for requirements and acceptance criteria.
4. Load the task POML from `tasks/`.
5. Apply the ADRs in the task's `<constraints>`.

**Context Recovery**: [Context Recovery Protocol](../../docs/procedures/context-recovery.md)

---

## 🚨 MANDATORY: Task Execution Protocol

**ABSOLUTE RULE**: All task work MUST use the `task-execute` skill. DO NOT read POML files directly and implement manually.

| User Says | Required Action |
|---|---|
| "work on task X" | Execute task X via `task-execute` |
| "continue" / "keep going" / "next task" | Read `TASK-INDEX.md`, find first 🔲, invoke `task-execute` |
| "continue with task X" / "resume task X" | Execute task X via `task-execute` |
| "pick up where we left off" | Load `current-task.md`, invoke `task-execute` |

Bypassing loses ADR constraints, checkpointing, and the Step 9.5 quality gates.

### Parallel task execution

Tasks in the same wave still each use `task-execute` — ONE message with MULTIPLE Skill invocations. Max 6 concurrent. Dispatch each subagent at its POML's `<model-tier>` and `<effort>`.

### Multi-file decomposition

For tasks modifying 4+ files: group by module, parallelize where files are independent, serialize where coupled. See [task-execute Step 8.0](../../.claude/skills/task-execute/SKILL.md).

---

## Execution Model & Tiering

- **Planning** (this pipeline run): Opus 4.8 / Fable 5.
- **Execution**: default **Sonnet 5 @ effort `high`**. Each POML carries `<model-tier>` and `<effort>`; `opus` + `xhigh` are reserved for the identity resolver, the version-save server change, the creation service, and the authorization hardening.
- **Step modes**: `directional` by default; `prescriptive` for task 010 (adapter consolidation order is load-bearing) and the deploy tasks.
- **Escalation**: tasks with judgment boundaries carry `<escalation><trigger>`. Firing one is a legitimate stop, not improvisation.

---

## Key Technical Constraints

### MUST

- Canonicalize every Dataverse GUID via `cleanGuid` at every boundary — **ADR-044**. Never interpolate a raw GUID into an OData key predicate.
- Use endpoint filters for authorization; `.RequireAuthorization()` on the group — **ADR-008**.
- Keep Graph SDK types behind `SpeFileStore` — **ADR-007**.
- Use NAA via `@spaarke/auth` `OfficeNaaStrategy`. **No MSAL construction in the add-in package** — **ADR-028**, arch-test enforced.
- Use infinite lazy-scroll for Find results — **ADR-051**.
- Measure BFF publish size on every BFF-touching task, **against a fresh build of master**, not the recorded baseline — **ADR-029** + root CLAUDE.md §10.
- Pass `documentId` on every index call, or chunks land as orphans and tracking fields cannot be written — [`ai/indexing-pipeline.md`](../../.claude/patterns/ai/indexing-pipeline.md).
- Add a contract test for every new or modified endpoint — **ADR-038**.

### MUST NOT

- ❌ `.WithClientSecret` — **ADR-028**, arch-test enforced
- ❌ Import `Xrm`-bound components into the add-in — **NFR-03**. `Xrm.*` does not exist in an Office host; recreate layouts (the r2 precedent).
- ❌ Relax `sprk_graphitemid_uk` — **NFR-07**. Compose's transient-key dedup and promote-idempotency both rest on it.
- ❌ Use the immutable suppress path for editable documents — **NFR-08**. A Word document is editable; suppress-forever on a hash hit collapses two distinct drafts into one record. Mirror `ComposeService.PromoteIfEphemeralAsync`.
- ❌ Add a pager to any list — **ADR-051**
- ❌ Inject `IOpenAiClient` / `IPlaybookService` into CRUD code — use `Services/Ai/PublicContracts/` — **ADR-013**
- ❌ Edit `ci-router.yml`, `ci-tier1-blocking.yml`, `ci-tier2-advisory.yml` — **frozen** under the shadow-comparison window (open 2026-08-27)

### Gotchas verified during discovery

- **There is no `build:prod` script** in `src/client/office-addins` — `npm run build` *is* the production build (the module `CLAUDE.md` says so too; corrected 2026-10-06).
- **There is no concise `ADR-038`** in `.claude/adr/` — point at [`docs/adr/ADR-038-testing-strategy.md`](../../docs/adr/ADR-038-testing-strategy.md).
- **`ADR-049`** (Compose Shadow Document) governs the other `.docx` write path and was missing from the spec's ADR table. Read it before touching any `.docx` save path.
- **`deploy-office-addins.yml` runs on every push to `master`** (and on `workflow_dispatch`); a branch build needs `workflow_dispatch` (corrected 2026-10-06).
- **`npm install` needs `--legacy-peer-deps --no-audit --no-fund`** — a bare install fails with ERESOLVE (`@testing-library/react@14` peer-requires React 18; the project is on React 19).
- **The add-in site deploys from CI on every master merge** — confirm with `gh run list --workflow=deploy-office-addins.yml`. An agent-triggered `workflow_dispatch` only with the owner's go (corrected 2026-10-06; the old "never run it as an agent" line predated the owner authorizing it case by case).

---

## Findings that modify the spec (bound to tasks)

Discovery verified six spec assumptions as false or mis-sized. Full detail in [`plan.md`](plan.md) §3. Do not silently absorb these.

| ID | One-line | Owning task |
|---|---|---|
| **F-a** | FR-12's shipped collision handling is on a different upload path than the add-in uses | 005 (spike) → 025 |
| **F-b** | FR-16's similarity engine has **no per-row authorization** — the UAC-r2 failure mode | 032 (gates 033) |
| **F-c** | No single endpoint returns similar documents *and* records | 034 |
| **F-d** | FR-11's `ExistingDocumentId` hook is inert on both sides | 023, 024 |
| **F-e** | FR-04 as written would regress the `.docx` save; `HostAdapterFactory` is dead | 010 |
| **F-f** | `POST /api/office/save` has zero executing contract coverage | 016 |
| **F-g** | `sprk_event` is absent from `sprk_document` yet shipped code authorizes + writes it | 026, 035 · fix owned by UAC-r2 |

### 🔴 `sprk_document` association slots — read this before touching any association code

Verified live 2026-09-04 (MCP + maker-portal Columns view). **Two families, sixteen lookups:**

- **Direct (4)** — `sprk_matter`, `sprk_project`, `sprk_invoice`, `sprk_workassignment`. **There is no `sprk_event`.**
- **Related (12)** — `sprk_relatedagreement`, `sprk_relatedcommunication`, `sprk_relatedcontact`, `sprk_relatedevent`, `sprk_relatedinvoice`, `sprk_relatedmatter`, `sprk_relatedorganization`, `sprk_relatedproject`, `sprk_relatedservicerequest`, `sprk_relatedtodo`, `sprk_relatedvendororg`, `sprk_relatedworkassignment`.

The Office save path writes **only the direct family**, so a card reading only `sprk_related*` is empty on every document the add-in created. Read `sprk_relatedevent` for Event — `sprk_event` will throw.

**Do not trust these three claims**, all of which appear in shipped code comments and in [`coordination-document-association-map-from-email-r2-2026-09-04.md`](coordination-document-association-map-from-email-r2-2026-09-04.md) §3/§4.1 and are false against live schema: that a direct `sprk_event` column exists; that `sprk_todo` is "unmappable / needs a schema change first" (`sprk_relatedtodo` exists); that there is no contact lookup (`sprk_relatedcontact` exists). Only "no `account` lookup" is correct. See that doc's §0 correction.

---

## Decisions Made

| Date | Decision | Rationale |
|---|---|---|
| 2026-09-04 | **FR-02 stamping is forward-only** — no retroactive rewrite of existing `sprk_document` bytes | Owner decision at pipeline time. FR-01's Graph + alternate-key path already identifies pre-existing Spaarke documents; the stamp is a fallback for round-tripped files. Retroactive stamping would mean rewriting stored bytes for every existing document. |
| 2026-09-04 | **Run scope: initialize only** — no task auto-execution from the pipeline | Operator reviews 34 tasks before execution begins. |
| 2026-09-04 | **ADR-012 → Path A** (project-scoped exception): the add-in keeps thin views under `shared/taskpane/`, consuming `@spaarke/auth` only | `@spaarke/ui-components` assumes React 19 and some components are Xrm-bound; importing them would break at runtime (NFR-03). r2 established the recreate-layouts precedent. |
| 2026-09-04 | **ADR-050 → Path C** (comply in spirit): the Office Dialog is host chrome, outside ADR-050's scope | ADR-050 governs Spaarke-rendered modals. Any Spaarke UI rendered *inside* the dialog still follows ADR-050 + ADR-021. |
| 2026-09-09 | **FR-18 = production types only** (A1 + B1). Acceptance narrows to the `office-addins` package; foreign files pulled in by a path alias do not count | 88 production errors were achievable and unblock Phase 1; 296 of 384 sat in test files entangled with a separate red-harness defect. Production is now **0**. |
| 2026-09-09 | **The 289 test-file typecheck errors are CONSCIOUSLY ACCEPTED, not deferred-and-forgotten.** Trigger to revisit: *when a build surfaces them.* | Verified inert: **no CI job typechecks `office-addins`** (`sdap-ci.yml` runs `tsc --noEmit` for `Spaarke.AI.Widgets` only); `npm run build` is webpack and test files are not in its graph — which is why the build is green today *with* all 289 present; and `ts-jest isolatedModules` is transpile-only so they cannot fail a test run. They surface ONLY on a manual `npm run typecheck`. Informed-consent note: a NEW test-file error would be invisible against this background, but **production is at 0 so new production errors stand out** — which is the benefit FR-18 was actually for. This is a measured, documented accept, not a burial. |
| 2026-09-09 | **Task 017 closed: RTL bump landed, but `useAnnounce`'s failure is a production-code defect, not the RTL-peer-mismatch 009 hypothesized** | RTL 14→16 bump (+ `@testing-library/dom` peer dependency, discovered mid-task and matched to the sibling pin) left the suite suite-for-suite identical to 009's baseline (12/21 failing, 92/329 failing tests). `useAnnounce.test.ts` still fails 16/16 with the same `NotFoundError` after the bump — root cause is `useAnnounce.ts` appending/removing DOM nodes directly on `document.body`, outside React's tracked tree, which collides with React 19's unmount bookkeeping regardless of RTL version. Fixing it needs a production-source change (e.g. a React-owned portal target), out of this task's scope. See `notes/017-testing-library-alignment.md`. |
| 2026-09-09 | **React 19 is the standard for code components; `@testing-library/react` aligns to it (v16), not the reverse** | Repo survey: PlaybookBuilder (RTL ^16.0.0) and SemanticSearch (RTL ^16.1.0) already run RTL 16 against React 19. `office-addins` was the ONLY React-19 package still on RTL ^14.2.1 — an outlier, not an architectural fork. PCF controls sit in their own internally-consistent React 16.14 / RTL 12 lane and are NOT React-19 compliant; `external-spa` is React 18. So this was never a two-sided major-version decision. Task **017** aligns it. |
| 2026-09-09 | **Task 010 closed: one Word adapter, and `HostAdapterFactory` is now LIVE on both panes.** `word/WordHostAdapter.ts` deleted; `shared/adapters/WordAdapter.ts` carries the ported `getFileAsync(Compressed)` extraction | Byte-identicality was PROVEN by executing both implementations side by side over the same bytes while the reference still existed (26 real .docx + 7 synthetic multi-slice cases up to 65 slices; `Buffer.equals`=true, SHA-256 equal, PK signature, identical slice order in every case). The corpus alone was NOT sufficient — every corpus file is < 64 KB and therefore single-slice, leaving multi-slice assembly, the actual risk, untested. See `notes/010-adapter-consolidation.md` §2. |
| 2026-09-09 | 🔴 **`createAndInitialize()` is called with NO host argument, so `detectHostType()` reads `Office.context.host` on both panes’ bootstrap — a property never previously exercised in production. UNCLOSED: must be verified live on Word AND Outlook, desktop AND web, at task 042.** | Passing an explicit host type would have been safer but would have left detection dead and silently dodged plan.md R-4 / escalation trigger 3. Unit tests can only pin the failure SHAPE (undefined host -> typed INVALID_HOST, rendered visibly — not a silently wrong adapter). **If Outlook fails there, the fix is NOT a host-sniffing fallback** (forbidden by trigger 3); the ADR-clean options are routing detection through the factory’s existing `waitForOfficeReady()` (which uses `Office.onReady`’s `info.host`, the signal both panes already await and discard) or passing the explicit host type as a deliberate decision. See `notes/010-adapter-consolidation.md` §6. |
| 2026-09-09 | **ADR-038 §2 → Path A (project-scoped exception): `src/client/office-addins/**/__tests__/**` is treated as KEEP-equivalent for `/test-diet` at project close.** `/test-diet` MUST classify these by CONTENT, not by path. | Surfaced by task 010’s adr-check (W-2). All eight ADR-038 KEEP paths are repo-root .NET paths (`tests/integration/**`, `tests/unit/domain/**`, `tests/Spaarke.ArchTests/**`); no KEEP category covers front-end jest at all, so a naive path check would flag the entire office-addins suite — including task 010’s `.docx` regression guard, the single artifact pinning the 2026-09-03 UAT defect — as scaffolding to delete. Relocating them is not viable (jest `roots: [‘<rootDir>/shared’]` + the `@shared/*` alias bind the suites to the package). **A Path B ADR-038 amendment adding a front-end KEEP category is the structurally correct fix and should be filed separately** — the precedent is Amendment A1, which added `tests/Spaarke.ArchTests/**` for exactly this “prescribed by practice, protected by nothing” reason. Not smuggled into task 010’s PR. |
| 2026-09-11 | **Matter numbering moves OUT of r1 into a separate project** (owner decision). The number must come from a **server-side record-numbering component that triggers on record create**; it need not be shown in the add-in. **Do NOT build a Dataverse plugin — Spaarke does not use plugins.** Task 030 keeps owner + BU defaults + create-time field mapping and must never write `sprk_matternumber`; FR-13's "number populated" acceptance and 030's AC2/AC3 transfer to that project. Hand-off: `notes/030-numbering-handoff.md` | Verified the component does not exist anywhere: repo, `origin/master` `e0a6f87c4`, and dev Dataverse (no autonumber on `sprk_matternumber`; no custom API / env var / PCF / web resource; no custom plugin step or flow on `sprk_matter` Create). The only type-code numbering today is the client-side random generator in the two `matterService.ts` wizards. |
| 2026-10-02 | **Numbering is back in r1, INTERIM: Dataverse platform autonumber `MAT-######` / `PRJ-######`, sequential** (owner: *"the dataverse auto numbering is just an interim solution until we build the numbering function"*). Supersedes the 2026-09-11 row above for r1. Blank rows are numbered on a script's first run; dev applied after an owner-approved dry run. `RecordCreationService` still never writes the number — it retries a key-refused create (409 `record_number_unavailable` after 3) and warns when none came back. Wizards unmodified (a supplied number is kept) | Task 076. The owner's new format carries no type code, which removed the 030 hand-off's one objection to autonumber; a BFF-owned counter would have numbered the pane only. Seed is per environment and not carried by a solution import → `scripts/Set-RecordNumberingSchema.ps1` in every environment (deployment guide §7.4); write-path invariant I-11. `notes/076-record-numbering.md` |
| 2026-09-11 | **Matter Type is required on pane quick-create and must always be sent — but the server does NOT reject when it is missing** (owner decision). The pane gains a required matter-type field (new client task); the BFF accepts an optional `matterTypeId` and creates the record regardless. | Rejecting would break the shipped pane until the client change lands; requiring it in the UI guarantees it in practice. |
| 2026-09-11 | **A supplied `matterTypeId` that does not resolve is treated like a missing one** — the Matter is created WITHOUT the type lookup, plus a warning; never a 400 or a 500 (main-session interpretation of the owner's "do not reject" rule, applied in task 030) | Without an existence read, an unknown id failed at Dataverse as an unhandled 500. Rejecting with 400 would contradict "do not reject". |
| 2026-09-12 | **Register jest-dom types for tsc (`tsconfig.json` `types` += `"@testing-library/jest-dom"`) — revisits the 2026-09-09 test-file-typecheck acceptance** (owner-approved) | The 2026-09-09 accept called the bucket "inert" without knowing its root cause. 179 of 290 errors (62%) were one gap: the explicit `types` list excluded jest-dom, whose only import is in `jest.setup.js` (outside tsc's `include`). Measured via an untracked tsconfig: 290 → 111, zero new errors, production 0. Types-only — no webpack/runtime effect. The remaining ~111 stay accepted under the 2026-09-09 decision; the gain is that a NEW test-file error is visible again (022 had added 6 unnoticed). |
| 2026-09-13 | **Matter types for the pane come from a live BFF call, cached client-side (~24h) — not a list hard-coded in the add-in** (owner decision; task 038). The new route `GET /api/office/search/matter-types` (added by 038 past its escalation trigger) is **accepted** | A hard-coded list was feasible — `scripts/Migrate-DataverseData.ps1` preserves `sprk_mattertype_ref` GUIDs across environments — but every new or renamed type would need an add-in redeploy and a customer-added type could never appear. The cost of the call is one small GET, cached to ~once a day; failures fall back to a user-initiated Retry. |
| 2026-09-14 → **corrected 2026-09-15** | **Outlook email `.eml` names: add a short unique suffix ONLY when the name was derived by the system (from the email's own subject); a name the user TYPED is never changed** — a clash on a typed name goes to task 025's refuse-and-ask prompt (owner decision B2, 2026-09-15; supersedes the 2026-09-14 blanket-suffix answer) | The 2026-09-14 answer rested on the main session's premise that the `.eml` name is never typed by the user. **That premise was FALSE** (task 046 escalation): in Outlook the pane's "Document Name" box replaces the email subject (`useSaveFlow.ts` sends `email.subject: effectiveDocumentName`), which becomes the `.eml` file name AND `sprk_documentname`. A blanket suffix would therefore change user-typed names. B2 needs the client to tell the server whether the name was typed. **Owner note 2026-09-15: most emails arrive through the Exchange / Graph integration (server-side inbound processing), not the pane — their names are auto-created, so they get the suffix too.** The suffix goes on the stored FILE name only; `sprk_documentname` stays the readable subject or typed name. Implemented by task 046(b). Owner principle recorded alongside: **a file name the user typed is never changed automatically** (no auto-rename, no overwrite). How Word-document name clashes and round-tripped copies are handled is **under owner discussion** (014/FR-02 + 025) — see `current-task.md`. |
| 2026-09-15 | **ADR-051 → Path A (project-scoped exception) for the Find results list only**: Find shows the single bounded, per-row-authorized response of `GET /api/ai/visualization/related/{documentId}` (at most 50) as a ranked "top matches" list — rows revealed progressively as the user scrolls, `hasMore` never implies more pages, no pager or "Load more", and the D-032-2 `PARTIAL_RESULTS` warning discloses any trimming (owner decision; task 034) | The route has no paging concept, and adding paging to this security-hardened route would re-run the per-row authorization per page (`notes/034-route-paging-escalation.md`). A ranked similarity answer is not a dataset with hidden pages. Recorded in `spec.md` ADR Tensions. |
| 2026-09-30 | **ADR-051 → Path A for the Find RECORDS half (task 077), owner-approved**: records page on *non-empty ⇒ more, empty ⇒ end*; offset advances by the requested window. | `AuthorizeRowsAsync` shortens pages by removing unreadable rows, so the literal page-fullness rule would stop after page 1 — the failure ADR-051 exists to prevent. Seeded both ways. Spec ADR Tensions row; `notes/077-find-gaps.md` § ADR-051. |
| 2026-09-30 | **Task 080 implemented for the Office writers: every record they create is owned by a BU default Owner team, record-first, and REFUSED (`OFFICE_022`, 403) when no team resolves — never app-owned.** Save (resolved before the email capture, job row, upload and row), upload-finalization worker (the team rides `UploadFinalizationPayload.OwningTeamId` so attachment children match their parent), To Do (record → document → communication → caller), quick-create Matter/Project/Invoice (Invoice was best-effort), `POST /api/v1/documents` | Owner decisions 2026-09-22/25. The resolver keeps an app-only read (background writers have no user token) and now refuses an ambiguous user or team (one fact, one answer — same as `RecordContainerResolver`). `notes/080-record-ownership.md` §6. |
| 2026-09-30 | 🔒 **`POST /api/v1/documents` let the CLIENT set `CreateDocumentRequest.OwningTeamId` and `.Id`** — both introduced on this branch as server-side inputs, neither excluded from `[FromBody]` binding. Fixed: `[JsonIgnore]` on both + server-resolved owner; pinned by `DocumentCreateOwnershipContractTests` (goes red with the attribute removed) | Found by task 080's full create-path inventory, not by review. ⚠️ **CORRECTED same day: it IS on master** — both fields reached master with #960 (2026-09-29) and are live on `spaarke-bff-dev`; filed as **#1043**, and this branch is its fix (UAC-r2 caught the wrong claim). **Lesson: a property added to a shared request model is a wire field wherever that model is bound — check every `[FromBody]` binding of the type.** |
| 2026-10-01 | **The Office save's job record is the Dataverse `sprk_processingjob` row (task 060, #1084).** The save's own view of its job lives as JSON in `sprk_result`; the status read and the 039 idempotency check both go through ONE rule, `OfficeJobStatusService.ToEffectiveView`, which includes a 5-minute "abandoned" rule. No in-process job store. Job reads are typed (`ProcessingJobRecord`), never `dynamic` | The finalization workers rewrite `sprk_status`, `sprk_currentstage` and `sprk_progress` after the synchronous save, so those columns describe the PIPELINE, not the save. `sprk_result` is written only by the save. **Never read a `Spaarke.Dataverse` anonymous type through `dynamic` from the BFF**: the binder cannot see another assembly's internals, which is how 039's idempotency silently never worked in production. `notes/060-job-status-store-and-extraction.md` |
| 2026-10-01 | **Generate Profile is a queued `AppOnlyDocumentAnalysis` job, not an in-process OBO run (task 068, #1086).** `OfficeProfileQueue` answers 202 only after the Service Bus submit; the key carries the request's own id via `AppOnlyDocumentAnalysisJobHandler.ProfileIdempotencyKey`, the ONE key helper for every profile producer. **Job processing locks: always release, whatever token; take back your own lock only after `IdempotencyService.OwnerTakeoverAge` (1 min).** SSE event ids are publisher sequence numbers from Redis `INCR` only | A graceful stop cancels the job's token and `RedisCache` refuses a cancelled token before it reaches Redis, so a lock released "in finally with ct" outlives the stop and the redelivery is completed as "already processing" — the work is silently lost. Same AI work on both paths since #919 Fix 2; `write` is checked before queueing. Six sibling handlers still have the crash half (ISS-020 #1089). `notes/068-durability-siblings.md` |
| 2026-09-30 | **Task 080 does NOT change `sprk_workassignment`, `sprk_analysis`, or the ~20 non-Office app-only writers** — filed ISS-007..010 (#1034–#1037). The backfill `-Apply` (sample or wholesale) is the OWNER's to run | Work assignment: `ownerid` is the only record of the assignee and the endpoint writes two non-existent columns (#1035). Analysis: AI zone, 6 callers, recipe in note §6.6. Backfill: owner — *"if we need to back fill something manually, let me know and i'll do it"*. Dry run done: 31 docs + 10 To Dos would re-own; 376 unfiled docs are left in root (cannot be inferred). |
| 2026-09-15 | **ADR-024 → Path A (project-scoped exception) for To Dos created from the pane**: the To Do sets its record lookup (Matter / Project / Invoice) AND a document or communication "source" lookup; the single-slot resolver fields (`sprk_regardingrecordid` / `-name` / `-type`) always describe the record (owner decision; task 035, FR-14) | FR-14 requires both links. ADR-024's single-lookup rule exists so there is one consistent "what is this about" answer — keeping the resolver fields on the record preserves it (`notes/035-todo-regarding-decision.md`). Recorded in `spec.md` ADR Tensions. |
| 2026-09-15 | **Task 029 (refresh profile + index after a version save): Option B — upload the new chunks, then trim the document's leftover chunks in the same index, for version re-indexes only; one new `IRagService` delete method allowed. No cleanup of stale chunks already in dev. The revert case (B → A → B) is closed in 029: every new version refreshes, retries of the same save still skip** (owner decisions) | The index stores chunks by position and never deletes them, so a shorter new version left stale tail chunks that Find could rank on (`notes/029-version-ai-refresh.md`). A content-hash key alone would have skipped a revert. Compose save-back and manual Run Index keep creating stale tail chunks through the same code → **owner 2026-09-15: new task here — task 048** (reuses 029's trim). |
| 2026-09-15 | **Send Email (task 036) is Outlook-only in r1: hidden in Word** (owner) | Word has no mailbox API. The alternatives (an Outlook-on-the-web compose link via `openBrowserWindow`, or `mailto:`) were offered and not chosen; options are in `notes/036-send-email-via-outlook.md`. |
| 2026-09-17 | **The pane's Document Name defaults to the FILE NAME** (owner). This answers `notes/025-residual-collision-surface.md` open question 8 — no "more specific" generated default. Task 020 shipped exactly this | Naming stays as the user made it; collisions are handled where they belong, in task 025's prompt, not by inventing names. |
| 2026-09-17 | **Document matching is still OPEN — the owner asked for a fuller explanation, now written at `notes/document-matching-explained.md`** | The key clarification: task 014's marker is an INVISIBLE custom XML part inside the `.docx`, NOT the file name (the earlier "we cannot add id to the document file name" answer was about the name). The note lays out A (invisible marker) / B (content hash) / C (ask the user), what each catches and misses, and recommends A+C with B kept. Decides 014, 025, 045 and 047's create-path leftover. |
| 2026-09-17 | **The Word manifest artifact for an M365 Admin Center upload is the BUILD OUTPUT `src/client/office-addins/dist/word/manifest.xml`, or the hosted copy at `https://icy-desert-0bfdbb61e.6.azurestaticapps.net/word/manifest.xml`** — never the repo source | `word/word-manifest.xml` is authored with `https://localhost:3000` placeholders; webpack copies it to `word/manifest.xml` substituting `ADDIN_BASE_URL` (the deployed SWA in production builds). Verified live 2026-09-17: the hosted XML is `1.0.8.0` and the hosted JSON `1.0.8`, both with the SWA host and no localhost. A re-upload is only needed if the INSTALLED version differs from 1.0.8.0 — our merged work changed no manifest file. |
| 2026-09-17 | **Task 041 is authorized to proceed ("the correct technical fix")** — register both SPA redirect URIs per environment against the deployed SWA host | Still binding: additive only (never delete an existing redirect), read back after writing, operator's own identity (never a service principal or client secret), and the three frozen CI tier files stay untouched. |
| 2026-09-17 | **Deploy: the BFF goes through the `/bff-deploy` skill (authorized). The add-in needs the SWA deploy workflow run, which today triggers only on `master` and `work/SDAP-outlook-office-add-in` — so either `workflow_dispatch` it or add this branch (task 041''s second half)** | The add-in build now also needs `ORG_URL` (added to the workflow by the main session) for task 027's Open buttons and 036's record links, and the Entra redirects from 041 for sign-in. |
| 2026-09-17 | **Project numbering is a SEPARATE project and is NOT a dependency of this one — a Project may be created with no number** (owner). Task 031 is re-scoped: creation completeness + QuickCreate routing only; it MUST NOT write `sprk_projectnumber` | Removes 031's blocking judgment call (its escalation trigger about inventing numbering semantics is answered: don't). Matches the 2026-09-11 Matter-numbering decision. |
| 2026-09-17 | **GitHub #975 (every BFF error is served as `application/json`, never `application/problem+json`) is assigned to THIS project** (owner) — not deferred, not handed on. Task 050 | ADR-019 makes the media type part of the error contract; the hand-rolled middleware sets the header and `WriteAsJsonAsync` overwrites it one line later. |
| 2026-09-11 | **ADR-044 local `cleanGuid` in the add-in (`shared/taskpane/utils/cleanGuid.ts`) — continuation of the ADR-012 Path A exception**, not a fresh violation | The canonical `cleanGuid` lives in `@spaarke/ui-components`, which this package may not import (Path A, 2026-09-04); `todoChoices.ts` is the approved precedent. Task 013 D-2. |
| 2026-10-04 | **ADR-012 Path A narrowed for ONE surface: the Word Email tab mounts the shared `EmailComposer` engine** through a new thin wrapper `SendEmailPane` (`@spaarke/ui-components`, ADR-045 one-engine/thin-wrappers), resolved by an EXACT alias to the wrapper's source (never the library barrel) and typed by a local `.d.ts`. Everything else in the pane still recreates layouts (Path A stands) | Owner, round 4: *"for the email form we should use our shared UI components so it looks consistent"*. The wrapper's 22-file import chain has no Xrm dependency and runs on the add-in's React 19 / Fluent 9; the 2026-09-04 reason (React-version + Xrm-bound components) does not apply to it. Task 096, `notes/096-email-tab.md` |
| 2026-10-04 | **`/api/communications/send` authorization belongs to UAC-r2 task 161, not this project; the Word Email tab (096) is held OFF until 161 is on master and deployed** (build setting `ADDIN_EMAIL_TAB_ENABLED`, default off; flip it on, no code change). Task 097 is re-scoped to the outbound-archive id fix (after UAC-r2 task 146, same lines) and the live checks | Owner, 2026-10-04, choosing option 1: *"yes we can follow your recommendation - ensure we have this fully documented"*. The route attached any document and linked any record without checking the caller (found by 096's review); 161 already implements the per-item checks on the same files, so a second implementation here would duplicate security code and conflict at merge (CLAUDE.md §11). `notes/097-communications-send-authorization.md` §6 |
| 2026-10-05 | **ADR-012: reuse the shared library by default.** The spec's ADR-012 exception is narrowed to the library **barrel** only; the add-in consumes shared components by exact-path alias (the task 096 mechanism), and a shared component that needs `Xrm` is made host-agnostic rather than copied. ADR-044's local `cleanGuid` goes back to the shared one (task 099) | Owner: *"we should use shared library wherever possible (if technically possible)"*. The "React 19" reason had expired; only the barrel's `Xrm`-bound re-exports still fail in an Office webview. `spec.md` ADR Tensions rows ADR-012 + ADR-044 amended |
| 2026-10-05 | **Document links in email go to the Spaarke record, never a file sharing link.** the shared composer's **Link** option inserts the Spaarke record link (task 098). The planned typed refusal on `POST /api/documents/{id}/share-link` was REVERTED (it failed UAC-r2's share-link tests); the route has no caller left and its retirement is handed to UAC-r2 (`notes/098-share-link-route-refusal.patch`). External recipients open the file through the external access platform, like an internal user opening it from Spaarke (task 098) | Owner, 2026-10-05: *"the link should just be to open Spaarke record; for external, they can open the file from their external access platform (same as internal user opening from Spaarke)"*. Graph refuses item sharing links on SPE containers (round 4 note §3) |
| 2026-10-05 | **UAT round 5 decisions**: (A) after a save the pane shows no Save button until the document is edited (task 094's change detection), then Save = new version; Cancel and Open Document leave the post-save view. (B) the create forms prefill Assigned To with the signed-in user's contact, **not required**; Practice Area is required for a Matter, like Matter Type. (C) the add-in's create form is built from shared field components (`LookupField` + dropdowns) and creates through the BFF (`/api/office/quick-create`, the WP-3 write path); the shared Create*Wizard steps (direct Dataverse writes, `Xrm`) are not reused | Owner 2026-10-05: "A. yes follow recommendation; B. yes … Assigned To not required; practice area same as matter type; C. yes". `notes/042-uat-round5-2026-10-05.md` §3 |
| 2026-10-06 | **Word Email tab switched on** (`ADDIN_EMAIL_TAB_ENABLED: "true"` in the deploy workflow; code default stays off) — UAC-r2 task 161 is on master (#1312) and live on dev | Owner 2026-10-06: *"uac-r2 task 161 is working so you can proceed"*; UAC-r2 confirmed live since ~04:47 UTC. PR #1316 |
| 2026-10-06 | **Outbound archive = option A, as protected as the source.** With `archiveToSpe`, each sent attachment is saved to SPE as its OWN new file with a normal `sprk_document` pointing at it, linked to the communication (reusing inbound archiving's building blocks); never two rows on one file. A **secure** source's copy goes to the source's secure container (owner via task 146's resolver); security unknown → that attachment is not archived (never the shared container). Cross-secure case (source secure in X, communication secure in Y) keeps its `sprk_relatedcommunication` link — readable today; must be settled before UAC-r2's `DocumentPointer__StrictDerivedContainer` is switched on (tracked) | Owner 2026-10-06: *"yes option A"*; *"yes follow recommendation"* (keep the copy as protected as the original). Notes `097-…md` §9-§11 |
| 2026-09-17 | **041 executed: the Entra redirects were ALREADY correct, so nothing was written; and the deploy trigger stays `workflow_dispatch` with `deploy-office-addins.yml` UNCHANGED** | Both NFR-09 URIs were already SPA-registered on `Spaarke Office Add-in` (`c1258e2d-…`) for `icy-desert-0bfdbb61e.6.azurestaticapps.net`, the only add-in SWA — so dev is the only environment and no additive write was needed. A push trigger was rejected on purpose: it would auto-deploy a feature branch onto the shared dev SWA, and three `workflow_dispatch` runs on this branch already prove the path. 042 launches with `gh workflow run deploy-office-addins.yml --ref work/spaarkeai-word-add-in-r1`. Evidence + the next-environment recipe: `notes/041-entra-redirects.md`. |
| 2026-09-17 | 🔑 **DOCUMENT MATCHING DECIDED (owner): follow the recommendation — A + C, with B supporting.** A = the invisible custom XML marker inside the `.docx` (**task 014**, GO). C = the collision prompt on save (**task 025**, GO). B = the content hash (028, already shipped) stays as the cheap supporting signal, no new work | Unblocks **014**, **025**, **045** and closes 047's create-path leftover. Note the letters: these are the A/B/C of `notes/document-matching-explained.md`, NOT the A–D options inside `notes/014-xml-part-stamp-decisions.md` §7. |
| 2026-09-17 | **014 ships as its note's own recommendation: option A, sequenced as C first** — wire **version-path** stamping now; wire **create-path** stamping only once 025 has landed | Version-path stamping has no D1 exposure and needs no shared-contract change. Create-path stamping makes D1 strictly worse until 025's refuse-before-upload lands (`notes/014-xml-part-stamp-decisions.md` §6e), so the ordering is a data-safety constraint, not a preference. |
| 2026-09-17 | **Identity precedence: a RESOLVED cloud URL (012) wins; the stamp is used when 012 answers `not_cloud_document` / `not_resolvable` / `not_spaarke_document`; a resolved URL whose id disagrees with the stamp is `identity_conflict`** | ⚠️ This **overrides task 014's POML step 5**, which says "prefer the stamp". Stamp-first has a documented data-loss case: a Spaarke document copied into SPE through a non-stamping path carries its *source's* stamp X while its URL resolves to row W, so a default version save would write W's content into X's file (`notes/014-xml-part-stamp-decisions.md` §9). AC2 still holds — a local file makes no Graph call at all. |
| 2026-09-17 | **The `PK`-prefixed 5-byte Office save fixtures MUST be migrated to a real minimal `.docx`, and the link test allowed to go red** | Documented false-green trap (`notes/014-xml-part-stamp-decisions.md` §6d): those fixtures classify as CORRUPT under stamping. "Fixing" them by dropping the `PK` prefix makes the stamper pass them through, so the link tests stay green while production has no link. |
| 2026-09-17 | **The CLIENT-side stamp reader is unowned → owned here as new task 051** | 019 conditions 1 + 4 (Common API `Office.context.document.customXmlParts`, runtime `isSetSupported` guard) and the precedence wiring have no owner: 015 is complete and did not do it, and 014 is server-side. Unowned work belongs to this project (deferral policy 2026-09-09). |
| 2026-09-17 | **The stamp's refusal code is `OFFICE_021`, NOT `OFFICE_020`** — an unreadable/corrupt `.docx` is refused before any SPE write | `OFFICE_020` was taken by task 025's name-collision refusal, which merged first. `OFFICE_001`–`OFFICE_020` are all in use (verified by grep across `src/` and `tests/`); `OFFICE_021`/`OFFICE_022` had zero hits. The two are kept **deliberately distinct** because both are reachable from one create request and they need different client behaviour: a collision offers a two-option retry, unreadable bytes must not. |
| 2026-09-17 | 🔴 **Stamping interacts with task 047, which the 014 design note had explicitly cleared — and the note was wrong.** `OfficeDocumentStamp.Stamp` MUST be **byte-deterministic**, and the request must be stamped BEFORE the duplicate comparison | Note §6a said idempotency was unaffected. That is true of the *keys*, but `IsStillTheSameOperationAsync` (task 047) compares the REQUEST bytes to the STORED bytes — and stamping makes those never match, so every identical version-save retry would have written a redundant SPE version. Fixed by stamping before comparing, which requires determinism; now pinned by a test. **Lesson: a design note's "unaffected" is a hypothesis, not a finding.** |
| 2026-09-17 | ⚠️ **METHODOLOGY — the stale-binary trap. A partitioned/concurrent `dotnet test` run can report GREEN for code that was never built.** Never pass `--no-build` when any other test process may be live | Found by task 014 the hard way: a backgrounded chunk's `testhost.exe` holds a lock on the DLL, the rebuild then fails `MSB3027`, and a subsequent `--no-build` run happily reports green for the *previous* binary. It was caught **only** because an expected count read 32 instead of 33; three results were discarded and re-run. This is the same class as a vacuous green — the observation was taken before the thing being observed had happened. Run the full suite single-process with nothing concurrent, and always let it rebuild. |

---

## Implementation Notes

*No notes yet — populated during execution.*

---

## Deferrals & Issues — tracking obligation

Deferred work and newly-discovered issues go in **both** `notes/defer-issues.md` (source of truth) **and** GitHub Issues (visibility). Invoke `/project-defer-issue-tracking` (alias `/defer`) — it writes both in one step.

Never add an entry only to `notes/defer-issues.md`. `push-to-github` blocks push on entries without GitHub URLs.

CLAUDE.md §11 applies: every entry must name a concrete behavior or contract that fails without the work. "Future flexibility" / "separation of concerns" is not a reason.

---

## Resources

### Applicable ADRs

**From spec** — ADR-001 (Minimal API BFF runtime — **where background/scheduled/event-driven work RUNS is [ADR-052](../../docs/adr/ADR-052-workload-placement.md)**, and how it runs inside the BFF is ADR-004 / ADR-036; this entry used to read "Minimal API + BackgroundService", which ADR-052's drift guard correctly flags as crediting placement to ADR-001) · ADR-007 (`SpeFileStore` facade) · ADR-008 (endpoint-filter authorization) · ADR-010 (DI minimalism) · ADR-012 (shared component library — Path A exception) · ADR-021 (Fluent v9 + dark mode) · ADR-028 (Auth v2, secret-free, NAA) · ADR-029 (publish hygiene + size ratchet) · ADR-038 (testing strategy — **`docs/adr/` only**) · ADR-044 (`cleanGuid`) · ADR-050 (modal shell — Path C) · ADR-051 (infinite lazy-scroll)

**Added during discovery** — **ADR-049** (Compose Shadow Document — the Word/OOXML ADR) · ADR-013 (`PublicContracts` facade) · ADR-004 / ADR-036 (job contract) · ADR-019 (ProblemDetails) · ADR-024 (polymorphic regarding)

### Patterns

[`ai/indexing-pipeline.md`](../../.claude/patterns/ai/indexing-pipeline.md) · [`auth/spaarke-sso-binding.md`](../../.claude/patterns/auth/spaarke-sso-binding.md) · [`auth/spe-writer-identity-matching.md`](../../.claude/patterns/auth/spe-writer-identity-matching.md) · [`ui/fluent-v9-host-visual-fit.md`](../../.claude/patterns/ui/fluent-v9-host-visual-fit.md) · [`ui/infinite-scroll-list.md`](../../.claude/patterns/ui/infinite-scroll-list.md) · [`ui/thin-scrollbar.md`](../../.claude/patterns/ui/thin-scrollbar.md) · [`api/endpoint-filters.md`](../../.claude/patterns/api/endpoint-filters.md) · [`dataverse/polymorphic-resolver.md`](../../.claude/patterns/dataverse/polymorphic-resolver.md)

### Constraints

[`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md) — binding pre-merge checklist for every BFF addition

### Related projects

| Project | Relationship |
|---|---|
| `email-communication-intelligence-r2` | Shipped the add-in's current state + content-dedup layer. Two handoff docs in this folder. |
| `unified-access-control-r2` | Tasks 094 (collision pre-flight) + 095 (two-slot association). **Do not duplicate.** |
| `spaarkeai-compose-r8` | The other `.docx` write path. `parallel-safe:false` across the Compose spine. `/conflict-check` before every BFF PR. |
| `spaarkeai-word-native-r1` | Owns MCP + the declarative agent. FR-20 only *launches* it. |

### External documentation

- Office Add-ins unified manifest (`outlook/manifest.json` is the in-repo precedent)
- Office Dialog API — Spike-2 subject
- Microsoft Graph `/shares/u!{base64url}/driveItem` — FR-01 identity path

## Standing directives & gotchas

> **Why this section exists (repo procedure change, 2026-10-06).**
> - **What changed:** `current-task.md` now holds CURRENT state only and is rewritten at each checkpoint (`.claude/skills/context-handoff/SKILL.md` "State, not history").
> - **Why:** here it had grown to 150 KB, because every checkpoint stacked a new block over the old ones, and task-execute reads it at Step 0 + Step 2 of every task.
> - **What this section is:** the items below were stated in that file as standing or binding and are still in force. They moved here so they survive the rewrite and are read on every recovery.
> - **Where the rest went:** the old file is archived verbatim at `notes/handoff-history/current-task-archive-2026-10-06.md`. Items the conversion could not classify, plus stale lines it noticed in THIS file, are in `notes/handoff-history/2026-10-06-conversion-review.md`; resolve them when convenient.
> - **Going forward:** add a new standing directive or gotcha HERE (one dated bullet), not in `current-task.md`.
>
> **Also new, repo-wide:** task-execute Step 9.5 "Finding triage and round limits".
> - F1–F4 fix-now / K1–K4 known-limit.
> - At most 2 fix rounds, each re-verifying the fix diff only.
> - 1 verifier pass per task (2 for auth/security/tenant-isolation).
> - Escalate any F1 still open instead of starting round 3.
>
> The skill files reach this worktree on the next master merge (`work/procedure-throughput-fixes-r1`); the rules apply now.
>
> Owner decisions with rationale stay in "Decisions Made" above.

### Owner directives
- **Fix findings here, don't defer them** (2026-09-19 / 2026-09-21): divergence and review findings are addressed in this project. Defer only with a good technical reason OR an ACTIVE hand-off note the other project is told to read — a GitHub issue alone is not a hand-off (2026-09-09).
- **Live-environment changes need the owner's go** (2026-10-01): Dataverse role/privilege config and data changes. A dry run comes first; owner-run scripts (e.g. backfill `-Apply`) stay the owner's.
- **Platform rules** (owner, standing): no Dataverse plugins and no form-event `.js` (ADR-002); records are always **team-owned**; `TargetEntity` is never required; access is **BU-assigned, never org-wide**.
- **Approvals** (owner, standing): every spec amendment needs the owner's sign-off; a dev BFF deploy needs the owner's go (given per deploy); no Dataverse role/security configuration change without the owner's go. The owner prefers the best long-term solution and often delegates the pick — recommend it, then execute it.
- **CI ownership** (2026-09-09): ci-cd-unit-test-remediation-r1 is CLOSED, so this project owns its own CI changes. React 19 stays; React Testing Library aligns to it.
- **Business-unit layout** (owner, 2026-10-06): each customer has ONE primary business unit; the secure-record business unit is a SIBLING of the primary one (not a child). Unified-access-control-r2 owns this design — ask them for detail.
- **Dev data is not indicative** (2026-09-10 / 2026-09-25): legacy/existing documents don't matter. Root-BU users and records are a setup artifact. Verify ownership/BU behaviour as **Test User 1** (`testuser1@spaarke.com`, BU `cb15f587…`), the only child-BU account.

### Git, PRs, CI
- **Merge with `gh pr merge N --merge`** (a merge commit, not squash); **never `--delete-branch`** — the branch continues (2026-09-30).
- **Only `Router` is a required check** (2026-09-21 / 2026-09-30). `Tier 2 (Advisory) / Full Unit Tests` cancelled at its 30-min cap is not a failure; do not make it blocking. Read `gh pr checks N` and require pending == 0 before trusting a verdict.
- **One build-heavy agent per worktree** (2026-09-21): concurrent agents' commits swallowed each other through the shared `.git/index`. Verify a commit with `git diff <base> HEAD -- <paths>`, never `git show --stat` or a derived count.
- **Agent dispatch** (2026-09-17):
  - `Set-Location` to this worktree and confirm "is a git worktree" first; create one worktree per turn.
  - Every brief: rebase onto the LOCAL branch; never `git stash`; long jobs in the foreground; new tests in NEW files; POML line numbers are stale, so re-locate symbols by name.
  - Agents never edit `TASK-INDEX.md`, `current-task.md`, this file or `ci-gated-suites.txt`.
- **lint-staged reformats on commit** (`prettier --write`, `dotnet format`) (2026-09-19): the committed bytes are not the tested bytes, so re-run gates on the committed tree.
- **Handoffs never pin the HEAD SHA** (2026-09-19): it self-invalidates on the commit that writes it. Use `git log -1`; cite only landmark/merge commits.
- **Never touch other sessions' measurement worktrees**: `C:/tmp/bffsize/{m,b}`, `C:\wt097`, `C:\wt097r` (2026-10-04 / 2026-10-06). Our own short-path worktrees are `C:\code_files\wtNNNx`, removed after use.
- **Before a commit, check untracked files** — no blind `git add -A` (2026-09-21).
- **Overlap with another branch: compare by function, not by line numbers** (2026-10-04).
- **`jq` is not installed** — use `gh … --jq`; `gh --jq` accepts no `--arg` (2026-10-06). In PowerShell, `R` is the `Invoke-History` alias — never name a helper function `R`.
- **Shell traps** (2026-09-19):
  - `grep -c … || echo 0` yields `0\n0`.
  - A pipeline's exit code is the last command's — use `${PIPESTATUS[0]}`.
  - Verify pushes by SHA (LOCAL == REMOTE).
  - grep matches prose and comments.

### Build / test
- **Before any .NET build** (2026-09-18):
  - Check `Get-Process testhost` and its command line before killing — it may belong to another worktree.
  - Check the build's exit code and confirm your test appears **by name**.
  - `--no-incremental` in a per-project loop breaks the next project's `obj/ref` (CS0006).
  - `dotnet clean` / `Remove-Item` near build paths is rejected by the permission layer and runs nothing.
- **Known pre-existing flakes** (2026-09-30 / 2026-10-03): `SseStreamingIntegrationTests.Cancellation_NoLingeringBackgroundTask_AfterClientAbort` (`Task.Delay`) and PinnedMemory (passes alone).
- **Office test hosts** (2026-09-30): the oid is `"test-user-oid"` (not a GUID); the factories register `RecordOwnershipResolverDouble` (`factory.Ownership`).
- **CI pins Node 20**; this desktop runs Node 22 (2026-09-21) — local office-addins gate runs are advisory.
- **Local add-in build needs the CI env values** from `.github/workflows/deploy-office-addins.yml` (2026-10-05): ADDIN_CLIENT_ID, TENANT_ID, BFF_API_CLIENT_ID, BFF_API_BASE_URL, ORG_URL, SPAARKE_APP_NAME, ADDIN_BASE_URL.
- **`ADDIN_EMAIL_TAB_ENABLED`** (2026-10-06): the code default is OFF (`webpack.config.js`, only the exact string `"true"` turns it on). The deploy workflow sets it ON; set it back to `"false"` to hide the tab, no code change.
- **`scripts/check-task-status-drift.ps1` and this TASK-INDEX** (2026-09-30 / 2026-10-06): status sits in cell 3. Fix `233ff9341` (customer-provisioning) IS on master as of 2026-10-06 — re-run the script once before trusting it; until it passes here, verify POML ⇔ index pairs by hand.

### Deploy
- **The add-in site follows master** (2026-10-05/06): every PR merge runs `deploy-office-addins.yml` on master. A branch build still needs `workflow_dispatch`.
- **BFF deploys only from master, from a fresh `origin/master` worktree** (2026-09-30 / 2026-10-02): `spaarke-bff-dev` is shared and other projects' branch deploys overwrite it.
- **`deploy-bff-api.yml` has failed every run since 2026-06-05** (re-verified 2026-10-06): BFF deploys are manual via `/bff-deploy`.
- **`Deploy-BffApi.ps1` checks** (2026-09-18 / 2026-09-21):
  - Run it under `pwsh`, not `powershell` — 5.x lacks `Get-FileHash`, which silently disables the hash check.
  - The Kudu SHA-256 file check is the only proof: `az webapp deploy` can return 200 with Kudu `status=4`. SCM basic auth is disabled, so Kudu needs an AAD token.
  - Package under 30 MB = incomplete zip. Since 2026-10-06 master publishes at about 36 MB (was about 45 MB) — expected, not a defect.
  - Linux cold start is 90–120 s — a `/healthz` timeout after a passing hash check is not a reason to redeploy.
  - Probe an authed route → 401 (404 = incomplete package).
- **A failed Kudu upload can leave the app STOPPED** after the script's recovery (2026-09-19): check state after every deploy and start it if not `Running`.
- **M365 Admin Center refuses a non-greater manifest/package version** ("Please update the version number") (2026-09-19): bump before any re-upload; never ask for a same-version re-upload.

### Dataverse / platform facts (measured — do not re-derive)
- **Default owner team** (2026-09-25): `team` WHERE `businessunitid` = BU AND `isdefault = true` AND `teamtype = 0`. Both predicates are required — dev has non-default Owner teams and Access teams.
- **Containers are BU-scoped, not per-record** (2026-09-18): only `sprk_issecure` records get their own. Filename collisions are BU-wide (`Untitled Document.docx`), so diagnostics query BU-wide, never by record.
- **Missing owner privilege** (2026-09-30): the error reads *"Read Privilege Check For Owner failed … Principal team … is missing prvRead<Table>"*. The caller's privileges are irrelevant. After a role edit the privilege cache lags about one poll — re-probe for 3 polls.
- **`config/secure-record-owner-role.json` is THE one Secure Record Owner list** (2026-09-30): UAC-r2's 145/146 read and extend it. Apply and verify with `scripts/Set-SecureRecordOwnerRolePrivileges.ps1 -Verify`.
- **Dataverse error codes** (2026-09-18): `0x80060891` = alternate-key miss; `0x80040217` = by-id miss; `0x80060892` = duplicate / not-Active key, which must stay 503. Match codes exactly, never by range. Do not apply the alt-key rule to `RecordContainerResolver.IsRecordNotFound` (a security decision).
- **Autonumber** (2026-10-03):
  - `GetNextAutoNumberValue` / `GetAutoNumberSeed` are POST actions.
  - GetNext returns the RAW number and reads ONE HIGH until the first number after a seed is issued.
  - `SetAutoNumberSeed(X)` → the next create gets X; right after the format is set it can refuse `0x80060884`.
  - A supplied value is kept; an omitted/empty one is generated.
- **The Office save accepts only friendly types** (`matter`, `project`…) (2026-09-30): anything keyed on logical names maps through `DocumentAssociationMap.ToLogicalName`.
- **`sprk_todo` is not in `RegardingNameFields.EntitySetName`** (2026-10-06): an Email-tab send associated to a To Do would be refused 403 `sdap.access.deny.communication.send`.
- **App Insights** (`spe-insights-dev-67e2xz`) (2026-10-01):
  - Get the appId with `az monitor app-insights component show … --query appId`.
  - Query via REST `POST https://api.applicationinsights.io/v1/apps/{appId}/query` — not `az monitor app-insights query` (Windows mangles KQL).
  - `first` / `last` / `kind` are KQL reserved words.
  - Retention in that component is about 2 h.
- **UAC-r2's live NFR-05 census** (2026-09-30) needs `SPAARKE_NFR05_DATAVERSE_URL=https://spaarkedev1.crm.dynamics.com SPAARKE_NFR05_REQUIRED=true AZURE_TOKEN_CREDENTIALS=AzureCliCredential`.
- **SPE link tests only work through the BFF** (2026-09-10): only the owning or a registered app reads SPE files, so `az` / Graph Explorer 403 regardless. Word's `document.url` returns raw spaces — send it exactly as returned.
- **Verify docs and agent reports against code/live before relaying** (2026-09-10): they have repeatedly been wrong in this area. The access model is ENFORCED in code (D-032-1 withdrawn, final).

### Coordination — `unified-access-control-r2` and others
- **Shared files: whoever lands second rebases** (2026-10-01): `OfficeService.cs`, `CallerRecordAccessProbe.cs`, `RouteAuthorizationGuardTests.cs`, `OfficeEndpointsContractTests.cs`.
- **UAC-r2 routes through our `RecordOwnershipResolver`** (130, 146) and will not fork it (2026-10-01): message them before any resolver behaviour change (e.g. a new refusal code).
- **UAC-r2 guard rules** (2026-09-30):
  - An Office-route filter whose name lacks "Authorization" must be added to `ExplicitlyCreditedFilterTypeNames`.
  - In `CommunicationsEndpoints.cs`, no code line may contain `entityService` / `IGenericEntityService`.
- **Attaching a filter: verify both dimensions** (2026-09-30): (1) it resolves the request type — `ExtractTargetEntity` returning null makes `EntityAccessFilter` pass through; (2) some end-user role actually holds the right it enforces.
- **No-record saves: authorize the destination container through UAC-r2's #1025** (2026-09-30), not a parallel mechanism.
- **`sprk_communication` create** (`EmailUploadCaptureService.BuildCommunicationEntity`) belongs to the Communication project (2026-09-30): coordinate or hand off, never edit their create path.
- **Do not edit UAC-r2's tests** (standing): `tests/integration/auth/UnifiedAccessControl/*` and `SecureBuRoleDepthAssertion*`. If our change fails one, revert or report it to UAC-r2 (task 098 precedent, 2026-10-05).
- **Caller-identity primitives** (UAC-r2 task 082): 080 uses `ICallerSystemUserResolver`; coordinate so it does not become a fifth primitive.
- **NFR-05 dev finding is UAC-r2's** (2026-09-30): the hotmail `#EXT#` guest in the root BU holds Spaarke Basic User Read at a depth reaching the Secure BU; UAC-r2 owns the owner decision.
- **#1011 is unrepaired but its GitHub issue is CLOSED** (corrected 2026-10-06): it was auto-closed 2026-09-30 when #960 merged — commit `34beafe78`'s "do not close #1011" was read as a closing keyword. UAC-r2 043 routed around the defect; `MembershipFieldDiscoveryService` still binds `systemuser` first, so do not assume it is safe for other Owner-column uses. **Reopened 2026-10-06** (owner go) with an explanatory comment; owned by UAC-r2, who were sent a hand-off message via the owner.

---

*Keep this file updated throughout the project lifecycle.*
