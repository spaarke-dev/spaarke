# Spike-4: does the add-in save path share the shipped collision semantics?

> **Answer: NO.** Finding **F-a** is confirmed, and the picture is worse than F-a states.
> **Status**: evidence gathered 2026-09-08 by direct code verification during a review repair.
> **⚠️ Not produced under `task-execute`.** Task 005 must still be closed formally with this
> report as its input. The FR-12 disposition below needs an operator decision (root CLAUDE.md §6.5).

---

## 1. The question

FR-12 asserts the collision fix is already shipped and instructs: *"Consume the shipped collision
handling (verify, do not rebuild)… **Do not rebuild any of this.**"* Its acceptance criterion is
**"no new collision logic is introduced."** Spike-4 exists to verify that premise.

## 2. The premise is false

The add-in does **not** ride the path UAC-r2 fixed.

| | Shipped/protected path | The add-in's actual path |
|---|---|---|
| Entry | client-side upload via `@spaarke/sdap-client` and the OBO endpoint | `POST /api/office/save` (JSON/base64), [`useSaveFlow.ts:901`](../../../src/client/office-addins/shared/taskpane/hooks/useSaveFlow.ts#L901) |
| Server | `UploadSmallAsUserAsync(..., ConflictBehavior.Fail, ...)` | `OfficeService` → `OfficeStorageUploader` → the **no-policy** `UploadSmallAsync` overload |
| Collision behavior | `Fail` → Graph 409, existing item untouched | **`Replace`** — [`UploadSessionManager.cs:103`](../../../src/server/api/Sprk.Bff.Api/Infrastructure/Graph/UploadSessionManager.cs#L103), *"preserving the historical `ConflictBehavior.Replace` behaviour"* |
| Typed client error | `UploadNameConflictError` ([`sdap-client/index.ts:13`](../../../src/client/shared/Spaarke.SdapClient/src/index.ts#L13)) | not reachable — [`office-addins/package.json`](../../../src/client/office-addins/package.json) depends only on `@spaarke/auth` |

**FR-12 as written cannot be satisfied on this path.** "Consume the shipped handling" has nothing to
consume: the protection lives on a different code path, and the add-in's server-side save re-enters
SPE through an overload that hard-codes `Replace`.

## 3. Three distinct defects (the reverted commit addressed only the third, incorrectly)

### D1 — Filename collision silently overwrites

A same-named Office save replaces the existing SPE item before any Dataverse reconciliation. This is
a live overwrite on the Word/Outlook save path.

### D2 — An **editable** Word document runs the **immutable suppress** path 🔴

[`OfficeDocumentPersistence.cs:78-85`](../../../src/server/api/Sprk.Bff.Api/Services/Office/OfficeDocumentPersistence.cs#L78-L85):
on a byte-identical `quickXorHash` hit it **skips the create entirely** and returns the existing
canonical id — the caller then cleans up the transient blob. This runs for every content type,
including `SaveContentType.Document`.

`DEDUP-AND-SAVE-BACK-IDENTITY.md` §3 predicted exactly this and forbade it:

> *"if the save-back path ever treats it like an immutable copy (suppress-forever on a hash hit), two
> genuinely-different drafts that happen to be byte-identical right now would collapse into one
> record — **data loss**."*

That doc's §5 already labels `OfficeDocumentPersistence.cs` the *"immutable suppress caller"*, and
NFR-08 requires **link/graduate** for editable documents, mirroring
`ComposeService.PromoteIfEphemeralAsync`. Word is editable. **This is arguably more serious than D1**:
D1 overwrites a file (SharePoint retains prior content as a version); D2 discards a distinct draft's
record outright.

### D3 — `ExistingDocumentId` / `IsNewVersion` are inert

Finding **F-d**. FR-11's version-save — the mechanism that makes most collisions *impossible* — does
not exist yet on either side.

## 4. The ordering insight (why the reverted commit was the wrong first move)

`DEDUP-AND-SAVE-BACK-IDENTITY.md` §3 closes with the load-bearing note:

> *"if you stay on the item-identity → version-save path (Layer A) for Spaarke-sourced documents,
> you sidestep this entirely — version-save targets the same record, so there's no dedup decision to
> make. Layer B only matters for the 'returned as a new item' edge case."*

**Build FR-11 (023/024) first and most of the collision surface disappears.** A Spaarke-sourced
document that is identified resolves to its own record and versions it — no filename collision, no
hash decision. D1 and D2 then apply only to the residual: documents that left Spaarke and returned
as a new item, or were opened from outside.

The reverted commit `45f45f626` attacked the *last* link in the chain (surface a 409) while the
*first* (identity → version) is unbuilt — and did so by adding the server-side collision logic FR-12
forbids, without closing this spike or coordinating with UAC-r2 task 094.

## 5. 🔔 ADR/spec conflict — resolution required (root CLAUDE.md §6.5)

- **Rule in question**: spec FR-12 — *"Do not rebuild any of this"*; acceptance *"no new collision
  logic is introduced."*
- **Conflict**: the rule assumes the add-in rides the protected path. §2 proves it does not. Strict
  compliance ships D1 and D2 unfixed; literal non-compliance is what the reverted commit did.
- **Proposed path**: **C then B** — pivot to comply first, then amend.
  1. **(C)** Build FR-11 (023 → 024) as specified. Re-measure the residual collision surface.
  2. **(B)** Amend FR-12 against the measured residual: its premise is factually wrong and should
     not survive as written.
- **Rationale**: 023/024 is already on the critical path, is already scoped, and shrinks the problem
  before anyone designs a fix for it. It also avoids duplicating UAC-r2 task 094, which owns the
  pre-flight probe and "Use existing".
- **Alternative considered and rejected**: a project-scoped exception (path A) authorizing new
  Office-path collision logic now. Rejected — it would re-do the reverted commit's *shape*, sizing a
  fix against a surface that FR-11 is about to shrink, and it leaves FR-12's false premise standing.
- **Impact**: FR-12 and task 025 are re-scoped, not descoped. D2 needs its own owner (see §6).

## 6. Recommended plan

| # | Work | Owner | Depends on | Note |
|---|---|---|---|---|
| 1 | Close **005** formally via `task-execute`, this report as input | 005 | — | Ratifies §2 and unblocks 025 |
| 2 | Operator decides the §5 path (C-then-B recommended) | operator | 1 | Blocks 3 and 5 |
| 3 | Build **FR-11** version-save: 023 (server) → 024 (client) | 023, 024 | 012 | **Strictly serial** — data-integrity. NFR-08: override routes link/graduate |
| 4 | **D2 as its own defect**: editable saves must link/graduate, not suppress | ⚠️ **unowned — needs a task** | — | Not currently any task's job. `/defer` it if it lands outside r1 |
| 5 | Re-scope **025** against the measured residual; `/conflict-check` vs UAC-r2 **094** first | 025 | 2, 3 | May shrink to "surface the typed error", or may need a coordinated server change |
| 6 | Amend FR-12 in `spec.md` to match §2 | — | 2 | Its premise is false as written |

Also correct `design.md` per `DEDUP-AND-SAVE-BACK-IDENTITY.md` §6 (three edits, still unapplied).

## 7. What was reverted and why

`45f45f626` (reverted by `0b68943d1`) passed `ConflictBehavior.Fail`, caught the 409, mapped
`OFFICE_011`, and rewrote the pane's error copy. It was reverted because:

1. It introduces new collision logic — FR-12's acceptance criterion is that none is introduced.
2. It pre-empted this spike; task 005 was still `not-started`.
3. It overloaded `OFFICE_011`, which already means duplicate **content** (dedup), with duplicate
   **filename** — collapsing the two layers `DEDUP-AND-SAVE-BACK-IDENTITY.md` §2 says to keep apart.
4. It hand-rolled `Results.Problem` beside the existing `OfficeProblemDetailsExtensions.DocumentAlreadyExists`,
   giving `OFFICE_011` two divergent wire shapes.
5. Its error copy directed users to a *Save version* / *Save as new* choice that the pane cannot
   offer — the shipped two-option dialog lives in `Spaarke.UI.Components`, which the add-in does not
   depend on.
6. No tests, against the CLAUDE.md §10 test-update obligation.

Its one correct instinct — that the Office path lacks protection — is preserved as **D1** above.
