# Task 076: record numbering, so no pane-created record has a blank name

> **Task**: `tasks/076-record-numbering-no-blank-primary-name.poml` (FR-13, SC-8) · FULL · opus @ xhigh
> **Date**: 2026-10-02/03 · **Commits**: `2d50f338d` (script, applied to dev) · `a09131522` (BFF + tests) · the close
> (Step 9.5 fixes, docs, task 087)
> **Owner decisions (2026-10-02)**: the number format and mechanism, the backfill, the FR-13 wording, and the dev apply
> (§2). Every one was asked before it was acted on.

## 1. The defect, reproduced first (acceptance criterion 1)

`sprk_matter.sprk_matternumber` and `sprk_project.sprk_projectnumber` are their tables' **primary name**. Re-verified
live on 2026-10-02 (`EntityDefinitions … PrimaryNameAttribute`). Nothing wrote either column on the pane's path, so
the records had no name.

Through the **deployed** BFF (`spaarke-bff-dev`, master `5e39f2bea`), `POST /api/office/quickcreate/matter` and
`/project` both returned 201, then:

| Where the name shows | Before (verbatim) |
|---|---|
| The row's primary-name column, which is what a subgrid renders | `sprk_matternumber=[]`, `sprk_projectnumber=[]` |
| The pane's To Do regarding the Matter (`POST /api/office/todo`, `regardingEntityType: "Matter"`) | `_sprk_regardingmatter_value` = the id, but **no** `@OData.Community.Display.V1.FormattedValue`; the sibling `_sprk_regardingrecordtype_value` shows `Matter`; `sprk_regardingrecordnumber = []` |

It was wider than the pane. **7 of 19 dev projects** were nameless, all made by the Create Project wizard, which
sends no number at all (`projectService.ts`).

## 2. Decisions

| # | Question | Answer | By |
|---|---|---|---|
| a | Format | `MAT-######`, `PRJ-######`, both **sequential**; *"we need to build a numbering schema component; for now just use PRJ-###### sequential; for matters MAT-######; … don't make it a project spike"* | Owner, 2026-10-02 (in the POML) |
| b | Where the sequence lives | **Dataverse platform autonumber** (recommended), not a BFF-owned counter | Owner, 2026-10-02 |
| c | Existing blank records | **Numbered on the script's first run** in each environment | Owner, 2026-10-02 |
| d | FR-13 wording | *"the dataverse auto numbering is just an interim solution until we build the numbering function"*. FR-13 is amended with that framing (`spec.md` FR-13, "Mechanism") | Owner, 2026-10-02 |
| e | Apply to dev | **Yes**, after seeing the dry run | Owner, 2026-10-02 |
| f | Production uniqueness | *"we will manually check it"*. The key step refuses and lists duplicates if any exist | Owner, 2026-10-02 |

**Why autonumber, not a BFF-owned counter.** The 030 hand-off listed the platform's autonumber as a candidate and
rejected it for one reason: it cannot include the type code (`{typeCode}-…`). The owner's new format has no type
code, so that objection no longer applies. Autonumber is:
- atomic under concurrency;
- not a plugin, so ADR-002's plugin ban holds (its WP-1 "BFF owner" rule did not, so it needed an owner decision; see §10);
- the number for **every** create that sends none: the pane, the Create Project wizard, MDA forms and the AI
  create tool.

A BFF counter would have needed a new table, its own concurrency control and its own retry, and it would have
numbered the pane only. That is more code, less coverage, and it would be thrown away when the numbering function
arrives.

**The scope decision, stated explicitly (acceptance criterion 3).** Option chosen: full sequential numbering
**by the platform**, interim. A deterministic non-blank name alone was not chosen.

**Matter and Project are not symmetric, so each is stated separately:**

| | Matter | Project |
|---|---|---|
| Format | `MAT-{SEQNUM:6}` | `PRJ-{SEQNUM:6}` |
| Who supplies a number today | The Create Matter wizard sends `{typeCode}-{random6}`, but only when a type is picked. The pane, MDA form and AI tool send none | **Nobody.** Every path sends none |
| Effect | Wizard matters keep `PAT-123456`, because a supplied value is kept (probed live). Everything else gets `MAT-…`. **Two formats coexist** until the numbering function | Every new project gets `PRJ-…`, the wizard's included |
| Alternate key | `sprk_MatterNumber`, **created** by this task | `sprk_ProjectNumber`, **already existed** in dev |
| Backfill | 1 blank row (the repro) | 8 blank rows (7 wizard-made plus the repro) |

## 3. Platform facts, measured live (Microsoft Learn is silent or wrong on some)

These are encoded in the script. Researcher record:
`.claude/agent-memory/researcher/dataverse-autonumber-existing-primary-name-2026-10-02.md`.

| Fact | How it was established |
|---|---|
| A **supplied** value is kept; an **omitted** or **empty-string** value is generated; an **update** after create is allowed | Probes on the empty `sprk_userentityassociation` table (`UEA-{SEQNUM:6}`), all deleted; then on `sprk_matter` itself (`PAT-0023200` kept) |
| Server-to-server creates by the BFF's application user are numbered | `sprk_notificationoutbox` rows `OUTBOX-001341…`, created by the BFF; then the pane creates in §5 |
| `GetNextAutoNumberValue` and `GetAutoNumberSeed` are **POST actions**. A GET with parameters is refused with `0x80060888` | The first `-Apply` run failed on it |
| `GetNextAutoNumberValue` returns the **raw** number (`1001`), not `MAT-001001` | The second run failed on it |
| After `SetAutoNumberSeed(1002)` the next matter got **`MAT-001002`**, yet `GetNextAutoNumberValue` read **1003** until that create. It **reads one high until the first number is issued after a seed** | A probe matter through the BFF (deleted) |
| Just after the format is set, `SetAutoNumberSeed` can refuse `0x80060884` ("not an Auto Number attribute"), because the change propagates asynchronously | Third run, on `sprk_project` |
| A number the sequence issues that a row already holds is **refused**: `0x80060892 Entity Key Matter Number (unique) violated`. The next attempt gets the **next** number | §5 check 5 |
| The default seed is 1000, and the seed is **not** carried by a solution import | Microsoft Learn plus the `UEA-001000` probe |

## 4. What was built

| File | Change |
|---|---|
| `scripts/Set-RecordNumberingSchema.ps1` (new) | Dry run by default, `-Apply`, `-Verify`; idempotent. Sets the format; seeds from the **data** (one above the highest in-format value, plus one slot per blank row); numbers blank rows oldest first; creates the keys; confirms `SpaarkeCore` carries both tables with all subcomponents; publishes. It never renumbers a row and never overwrites a different format |
| `Services/Office/RecordCreationService.cs` | `CreateNumberedAsync`: retries only on `DataverseServiceClientImpl.IsAlternateKeyDuplicate`, up to `MaxNumberAttempts = 3`, then returns `NumberUnavailable`. `WarnIfNumberMissingAsync`: one read-back; a blank number gives the user a warning and logs `record_number_unassigned`; a failed read is only logged. Stale "numbering out of scope" comments rewritten |
| `Services/Office/OfficeService.cs` | `NumberUnavailable` → **409** |
| `Api/Office/OfficeEndpoints.cs` | Quick-create OpenAPI description: removed "a planned separate numbering component … blank name" |
| Contract tests (matter, project) | See §6 |
| Docs | Spec FR-13 ("Mechanism", interim); write-path registry **I-11**; customer deployment guide §7.4 (run the script after H6 in every environment); `scripts/README.md`; project `CLAUDE.md` decision row; 030/031 notes marked superseded on numbering |

**Protected-attribute sets (acceptance criterion 5).** They are kept, and they still block a field-mapping rule from
writing the number. The reason has changed. A mapped value would **pre-empt the platform's sequence**, and a number
copied from a parent would collide on the key. The service never writes the number itself, so nothing needs to get
past its own guard.

**Collision behaviour, defined (acceptance criterion 4).**
- **Generated numbers:** the platform's sequence is unique against itself under concurrency (proven live below).
- **Typed-in numbers:** a number typed in ahead of the sequence is refused by the alternate key, not duplicated.
- **The BFF's response:** it retries, and each attempt draws the next number. After 3 refusals it returns 409
  `record_number_unavailable` with no row written, and the operator re-runs `-Apply`, which moves the seed past the
  typed values.
- **Any other fault** propagates on the first attempt, because a timeout may already have created the row.

## 5. Live verification in dev (acceptance criterion 2), through the deployed BFF

The deployed BFF does not contain this task's code. The platform assigns the number either way, so the checks are
valid. The retry and warning code is covered by the contract tests.

| # | Check | Result |
|---|---|---|
| — | Script dry run (shown to the owner), then `-Apply`, then `-Verify` | Matter: format plus key **created Active**, repro → `MAT-000001`. Project: format set, 8 rows → `PRJ-000001…008`. **VERIFY PASS**; a re-run proposes **0** writes |
| 1 | Pane quick-create | Matter → **`MAT-000002`**; Project → **`PRJ-000009`** |
| 2 | **6 concurrent** matter quick-creates | 6 × HTTP 201 → `MAT-000003…008`: **6 distinct** |
| 3 | The To Do regarding the repro matter, which was blank in §1 | Lookup display **`MAT-000001`**; the subgrid column shows **`MAT-000001`** |
| 4 | Wizard-style create supplying `PAT-0023200` | Stored **`PAT-0023200`**, so the wizards are unaffected |
| 5 | Typed-ahead collision: a row given `MAT-000009` by hand, then a create with no number | Attempt 1 **refused `0x80060892`**; attempt 2 → **`MAT-000010`**; exactly 1 row holds `MAT-000009` |

All **14** test rows, each named "076 …" (11 matters, 2 projects, 1 To Do), were deleted afterwards. Dev now has
the 7 wizard projects as `PRJ-000001…007`, no `MAT-` rows, and next numbers **`MAT-000011`** and **`PRJ-000010`**.

## 6. Tests (acceptance criteria 5–8)

- **`AssertNoMatterNumberSent` / `AssertNoProjectNumberSent` → `AssertNumberLeftToThePlatform`** (acceptance
  criterion 5).
  - It is the same assertion, for the opposite reason. It encoded "numbering is out of scope", and SC-8 had been
    recorded PASS against it.
  - It is still right: the platform fills the number only when the create leaves it empty.
  - Its doc comment records this.
- **New tests:**

  | Test | Entity |
  |---|---|
  | One key collision → retry → 201, and **every attempt's payload** still leaves the number out | Matter (payloads) and project |
  | Every attempt collides → **409 after exactly 3** (`RecordCreationService.NumberUnavailableCode`) | Matter |
  | A non-key fault is **not** retried | Matter |
  | Blank read-back → warning | Matter and project |
  | Failed read-back → **no** warning | Matter |
  | **Request cancelled after the create commits** → still a success (service-level: `RecordCreationNumberReadBackTests`, both entities; an HTTP test cannot cancel and still observe the response) | Matter and project |

  The collision faults have the production shape (`InvalidOperationException` wrapping the `0x80060892` fault, as
  `DataverseServiceClientImpl.CreateAsync` throws it). The read-back mocks only match a request for the number column,
  so reading the wrong column fails the tests.

- `CaptureCreate` now arranges a number on read-back, so every happy path takes the "number assigned" branch, not
  the "unknown" one.
- One existing project test asserted "no `sprk_project` read at all". It was narrowed to the **source** record's
  id, because the created project is now read back.
- **Seeded faults** (each restored byte-identical):

  | Fault planted | Tests that went red |
  |---|---|
  | No retry | 3 |
  | Retry on any fault | 1 |
  | Warning dropped | 2 |
  | Cancellation rethrown after the create commits (the pre-review code) | 2 |

- **Scope (acceptance criterion 8):** a non-blank name for both entities (live, plus the read-back tests), the
  collision case, and the protected-attribute interaction (the existing protected-fields tests). There is one
  addition: the "failed read-back gives no warning" test. Justification: a read-back that could alarm users over a
  create that worked would be a new false alarm, so it is pinned.

## 7. SC-8 (acceptance criterion 6)

SC-8 says "a Matter created from the pane has number + owner + mapped fields". It is now **PASS**, on evidence that
asserts it:
- **Number:** §5 checks 1 and 3 (live). The contract test
  `Post_Matter_WithMatterType_Returns201_…_AndLeavesTheNumberToThePlatform` pins the BFF's half (the create leaves
  the number to the platform, and the read-back sees it).
- **Owner:** unchanged evidence.
- **Mapped fields:** unchanged evidence.

Corrected in `notes/042-uat-results.md` and the `defer-issues.md` register.

## 8. Gates

| Gate | Result |
|---|---|
| Build | 0 warnings / 0 errors |
| Touched test classes | **78 / 78** (both quick-create contract classes, the record-creation unit tests, membership publishing) |
| Full BFF suite (at `a09131522`) | **14,217 passed · 1 failed · 54 skipped** (14,272). The failure is `PinnedMemoryEndpointsContractTests.CreatePin_Authenticated_Returns201AndEmitsCounter`: a metrics tag read another test's tenant (`tenant-test-fixture`) under the parallel run, in Memory endpoints this task does not touch. **Passes alone (15 / 15).** Pre-existing: UAC-r2 recorded its sibling `DeletePin_…` failing the same way (their tasks 130 and 135) |
| ArchTests | **345 / 345** |
| Publish (fresh worktrees, `Compress-Archive`, incl. PDBs) | Master merge base `5e1fdcc0b` **47,874,534 B** → branch `a09131522` **47,875,515 B**: **+981 B**, 212 = 212 files. The later review fixes and the OpenAPI description add a few hundred bytes of string and one small method change |
| CVE | `dotnet list package --vulnerable --include-transitive`: **no vulnerable packages**; no package or project file changed |
| Quality gates (Step 9.5) | §9 |

## 9. Quality gates (Step 9.5): what was found and what was done

**Code review** (coverage-first, 0 Critical):

| Finding | Done |
|---|---|
| **W1**: in a NEW environment the format arrives with the solution import, so the script left the platform's default seed (`MAT-001000`), contrary to its own docs | **Fixed**: a sequence still at the default (seed 1000, nothing issued) is seeded from the data; `-Verify` fails on it |
| **W2**: `Measure-Object` returns a `[double]`, so a re-seed would send `13.0` for an Int64 | **Fixed**: `[long]` throughout |
| **W3**: a cancelled read-back after a committed create became a 500 | **Fixed**, and pinned by the service-level test (seeded: the old code fails it) |
| **W4**: the backfill wrote with `If-Match: *`, so a row numbered after the plan could be renumbered | **Fixed**: written against each row's ETag; a 412 is skipped and reported |
| **W5**: the ADR-002 WP-1 tension | → owner decision (§10) |
| **W6**: the POML had no `<justification>` and an incomplete file list | **Fixed** |
| S3 (retry payloads), S4 (fault shape), S5 (read-back column), S7 (cancellation test), S10 (duplicates stop writes for that table), S12 (case-sensitive format compare), S13 (read-only actions separated from writes), S14 (publish only after a metadata write), S15 (`#Requires -Version 7`), S17 (solution name validated), S20, S21 (stale comments, incl. the add-in's `SaveFlow.tsx`), S25 (backfill bumps `modifiedon`, in the guide), S26 | **Fixed** |
| S1 (the classifier matches any key) and S2 (an Error log per refused attempt from the Dataverse layer) | **Documented** in `CreateNumberedAsync`'s remarks: the number's key is the only alternate key on both tables today |
| S8 (a hand-typed value equal to the seed cannot be told from the first issued number), S11 (first-run window), S16 (a "MAT" type code) | **Documented** as known limits in the script header |
| S6 (project parity tests), S18 (split the script into functions), S19 (shared post-create tail), S22 (two reads after a create), S23 (an add-in catalog entry for the new code; the add-in already shows the server's detail) | Not done: optional, with low value for an interim mechanism |
| S24 | Pre-existing (UAC-r2's identity service); not this task's |

**ADR check**: 1 violation and 6 warnings.
- **V-1 (ADR-002 WP-1)**: owner decision, §10.
- **W-2**: the script could customise a MANAGED column, key or solution directly in production, against ADR-027. **Fixed**: those steps refuse and say the change must arrive with the SpaarkeCore import; the script then only seeds and backfills.
- **W-3**: **fixed** with a named code constant.
  - One premise in the report was wrong: `owner_unresolved` **does** still exist. It is the code for an unresolved CALLER (lines 448 and 567); OFFICE_022 is for an unresolved TEAM. So the endpoint comment it flagged as stale is accurate.
- **W-4** (ADR-044 applied to the script): compliant in substance; the solution name is now validated.
- **W-5** (calling the public static classifier): accepted. It is that method's documented purpose, and ArchTests are green.
- **W-6** (test names, the unused variable, fault shape): **fixed**.

## 10. ADR-002 WP-1: resolved by the owner

The rule: *"every invariant has exactly one owner: a server-side component in the BFF write path"*. I-11's owner is the
platform. The owner chose, on 2026-10-03, ***"A now, B as its own task"***:
- **Path A, now:** a project-scoped, interim exception, recorded in `spec.md` ADR Tensions and on the I-11 registry
  row.
- **Path B, task 087:** amend ADR-002 so that a platform-native declarative mechanism may own an invariant, when it
  fires on every write path, runs no Spaarke code and has a per-environment `-Verify`. Autonumber is already used
  across the schema.

## 11. What remains

- **Every other environment** needs the script after its solution import (deployment guide §7.4). Production waits
  on the owner's uniqueness check. *(2026-10-03: run everywhere it can run; see §12.)*
- **The numbering function** (not yet a project) replaces this. To retire the interim: clear both formats and keep
  the keys. Still open from the 030 hand-off:
  - §4 overwrite-or-fill. The wizard's `{typeCode}-{random6}` coexists with `MAT-…`.
  - Whether a type change re-numbers a matter.
- The BFF's retry and warning are proven by contract tests and by the platform behaviour in §5, but **not yet run
  live**. They arrive with the next deploy from master.

## 12. Rollout to the other environments (2026-10-03, owner: *"you can run the clean up script for numbering"*)

Dry run first in each, then apply only where the plan was clean. The operator's own `az` identity
(`ralph.schroeder@spaarke.com`). `pac org list` shows three Spaarke environments: dev (done in §5), demo and Model 1
Prod. (`HIPC DEV 2` is another tenant; it is not a Spaarke environment.)

| Environment | Dry run found | Done | `-Verify` |
|---|---|---|---|
| `spaarke-demo` | **Matters**: 30 rows; **one duplicate number**, `LIT-2025-0847` on 2 rows. Both rows are the same matter, "Meridian Corp v. Pinnacle Industries": `39cde3e3-…` (created 03-26, **0 documents / 0 events**) and `a657db02-…` (created 04-06, **66 documents / 37 events**). A second seed run copied it. **Projects**: 12 rows, 10 blank. **No `SpaarkeCore` solution**: demo's unmanaged solution is `SpaarkeMaster`, which holds both tables with all subcomponents | The empty copy's number was blanked so the backfill numbers it (**reversal**: set `sprk_matternumber` on `39cde3e3-9d15-f111-8343-7ced8d1dc988` back to `LIT-2025-0847`, which the key will now refuse while the live copy holds it). Then `-SolutionUniqueName SpaarkeMaster -Apply`: both formats set; matter `39cde3e3-…` → `MAT-000001`; projects → `PRJ-000001…010`; key `sprk_MatterNumber` created (Active after 7 polls); `sprk_ProjectNumber` already Active; published | **PASS** — next numbers `MAT-000002` and `PRJ-000011` |
| `spaarke-model1-prod` | 0 matters, 0 projects. Both columns are **managed** | Nothing. The script refuses to customise a managed component (ADR-027), correctly | — waits for the next **SpaarkeCore** managed import, which carries the formats and keys from dev; then run `-Apply` (it seeds only) and `-Verify` |

**For the owner (demo):** two "Meridian Corp v. Pinnacle Industries" matters remain. The empty one (`MAT-000001`) can
be deleted if it is not wanted; it was renumbered, not deleted, because deleting demo data is your call.

**Deployment guide note:** the script's default solution is `SpaarkeCore`. An environment built from an unmanaged
`SpaarkeMaster` (demo) needs `-SolutionUniqueName SpaarkeMaster`.
