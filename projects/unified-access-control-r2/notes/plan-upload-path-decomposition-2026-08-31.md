# Plan — decomposing the upload path work (2026-08-31)

> **Why this exists**: a single conversation grew task 076 from "steps 4–6" into six distinct workstreams
> spanning 7 wizards, a new BFF endpoint, a schema change and a wizard UI. Running that as one task
> produces a change nobody can review. This note is the decomposition, the dependency order, and the
> facts each task must not re-derive.
>
> **Owner-directed 2026-08-31.** Numbering: `093`+ are free (`085`, `091`, `092` taken — `ls tasks/` before
> assigning, this index has been wrong twice).

---

## 1. The one-paragraph problem statement

Every SPE upload path must have the **server** choose the container from a record it authorized. Three
client flows currently violate that in different ways, and fixing them surfaced two adjacent defects (a
silent file-overwrite, and a stale 4 MB size cap) plus one missing UI (the Secure Project step) and one
schema limit (a document can belong to only one matter).

## 2. The five flows, and what each needs

| Flow | Owning record when bytes move | Container today | Needs |
|---|---|---|---|
| DocumentUploadWizard, **associated** | parent exists | parent's, resolved **client-side** (`AssociateToStep.tsx:147`, **fails OPEN**) | cut over to the record-keyed route |
| DocumentUploadWizard, **Skip** | none — association is skippable (`DocumentUploadWizardDialog.tsx:422`) | acting user's BU, pre-resolved client-side (`:224-241`) | record-less route + `ResolveForActingUserAsync` |
| **7 Create wizards** | **being created — does not exist** (`EntityCreationService.ts:26-28`: upload → create → link) | acting user's BU (`applyUserBuDefaults`) | **reorder**: create → provision-if-secure → upload → link |
| AI pre-fill staging | n/a | `SpeOptions.StagingContainerId` — **already server-side** | ✅ nothing. Verified `MatterPreFillService.cs:307` |
| Compose create-on-save | matter-less draft is a *designed* flow | client-supplied `ContainerId` | #858 (separate) |

**The AI pre-fill finding is what makes the Create-wizard reorder possible.** Pre-fill uploads to a
server-configured *staging* container, and the final document upload is a **separate** browser call
holding the same `File` objects. So the two legs are independent: reordering the final upload to after
record creation does not disturb pre-fill's field extraction. Bytes travel twice — true today as well, so
not a regression.

## 3. Task decomposition

### 076 (CONTINUES) — the container contract
- Client cutover U1 `EntityCreationService.ts:493` / U2 `SdapApiClient.ts:110` / U3 `UploadOperation.ts`
  to `(entity, recordId)`
- **250 MB threshold correction** (see §4 — the 4 MB branch is stale by ~3 years)
- Record-less route for the Skip path, container from `ResolveForActingUserAsync`
- Classify all 12 container suppliers; delete W1 (`EntityCreationService.ts:327` `applyDefaultContainerId`
  via `applyUserBuDefaults:374`) + W2 (`DocumentUploadWizard/sprk_subgrid_commands.js`)
- **Delete the legacy `PUT /api/obo/containers/{id}/files/{*path}`** — AFTER the cutover, never before
- 7 server-side Communication sites; delete the `Pending` OBO waivers; tests; absence-grep; build/publish

### 093 (NEW) — Create-wizard flow reorder + Secure Project wizard UI
Owner: *"for the Secure Project UI it is a part of this solution so need to include it."*
- Reorder all 7 Create wizards: **collect** IsSecure early (owner: **before the Info step**) → create the
  record → provision the container if secure → upload against `(entity, recordId)` → create document rows
- 🔴 **CORRECTION 2026-09-01 — "the Secure Project wizard step UI was never specified" is WRONG.**
  It exists, in **two** implementations, and there is already a **pending task 068** to rework it:
  - `src/client/shared/Spaarke.UI.Components/src/components/CreateProjectWizard/SecureProjectSection.tsx`
  - `src/solutions/LegalWorkspace/src/components/CreateProject/SecureProjectSection.tsx`
  - [`tasks/068-create-project-wizard-secure-step.poml`](../tasks/068-create-project-wizard-secure-step.poml)
    (status `pending`, Phase 4, FR-31) — *"SecureProjectSection.tsx copy + behaviour match the 061 server
    contract … the permanence warning is gone; a reversibility note replaces it."*

  **So 093 MUST NOT author a new Secure Project UI** (CLAUDE.md §11 — extend, do not fork). What 093
  actually owns is narrower and should be scoped to exactly this: **move the IsSecure collection point
  earlier** (owner: before the Info step) so the flag is known before record creation, and **reuse the
  existing section component** where it renders. Whether that means depending on 068, folding 068 into
  093, or landing 093 first and letting 068 rework what it moved is a **sequencing decision for the task
  author** — but duplicating the component is not on the menu.

  ⚠️ **Two copies is itself an open question** 093 should surface rather than silently pick: a shared-lib
  component and a LegalWorkspace copy. Determine which is live for each of the 7 wizards before editing
  either; changing the wrong one produces a fix that never renders.

  *How this was missed*: the plan asserted "never specified" without a `Glob`/`Grep` for the component —
  the exact evidence step CLAUDE.md §11 question 1 requires ("Verify by `Grep`/`Glob` before claiming
  'none'"). It was caught only because filing 093 triggered a reuse check. Note also that the first
  `Glob` run here returned a **false negative** because a backgrounded `cd` had moved the session cwd
  into `projects/`; when a glob for a file you expect contradicts you, re-check the search root before
  believing it.
- ⚠️ **Provisioning requires the record to already exist** (task 008 finding: its final act is an
  `UpdateAsync` stamping three fields on the project). So IsSecure is *collected* early and *acted on*
  after creation. These are compatible — but the order cannot be inverted.
- ⚠️ This is why abandoning the wizard leaves **no orphaned container** (owner's concern): nothing is
  provisioned until the record exists.
- ⚠️ `provision-project` is **project-only**. Owner: projects first, **matters as a later add-on**.
- 🔴 **The reorder is what closes the real isolation gap**: today a secure Matter created with documents
  puts those documents in the shared BU container, because at upload time no matter exists to be secure.
  Server-side derivation alone cannot fix it — only the reorder can.

### 094 (NEW) — Upload collision: pre-flight probe + user decision + `conflictBehavior`
Owner: *"we do not want to silent fail (or fail at the end)"*, and *"open a dialog with user option to
either replace or rename/create new."*
- **Pre-flight existence probe** before bytes move. The container is now server-resolved, so the client
  cannot look in it — it must ask the server. **Check `DriveItemOperations.ListChildrenAsync` for reuse
  before adding an endpoint (§11).**
- Dialog: **Replace** (`conflictBehavior=replace`, update the existing row → no alt-key violation) ·
  **Rename / create new** (`rename`, new row) · **Use existing** (no upload — depends on **095**)
- Set `conflictBehavior` **explicitly** on the small-upload path (see §4)

### 095 (NEW) — Document ↔ record multi-association
Owner chose **option (b)**, an intersection entity — **not** native N:N.
- Live metadata (owner screenshots, 2026-08-31): Document→Matter has **two** Many-to-one relationships
  (`sprk_matter_document`, `sprk_sprk_matter_sprk_document_sprk_relatedmatter`); Document→Project likewise
  (`sprk_Project_Document_1n`, `…_sprk_relatedproject`); Document→WorkAssignment has **one**
  (`sprk_WorkAssignment_Document_1n`). All **Many-to-one** — i.e. *two slots per type*, not a
  many-to-many.
- **Why not native N:N**: (1) it breaks this project's access model — the project CLAUDE.md has documents
  inheriting **1 hop via a denormalized core ancestor**, and N:N makes "the ancestor" multi-valued;
  (2) Dataverse intersect tables **cannot carry columns** (no why/who/when/primary-flag) and cannot be
  secured; (3) the codebase already has the polymorphic-regarding pattern (`sprk_todo`, ADR-024;
  `sprk_event.sprk_regardingrecordid`).
- **Design decisions that belong IN the task, not before it**: the polymorphic target list, and **whether
  a link confers access** (default: NO — the primary lookup stays the access ancestor, so the evaluator is
  untouched). Getting the regarding shape wrong is expensive to undo.
- ⚠️ Blocks the naive workaround: a second `sprk_document` row for the same file violates the alternate
  key on the SPE item id, which is what produces the owner's 412. **Do not relax that key** — Compose's
  transient-key dedup and promote-idempotency both rest on it.

### Deferred (filed, not scheduled)
[`finding-secure-transition-container-migration.md`](finding-secure-transition-container-migration.md) —
flipping a record to secure moves nothing. Its own project, after core UAC-r2.

## 4. Facts no task should re-derive

**The 4 MB threshold is stale by ~3 years.** Repo researcher memory
(`src/server/api/Sprk.Bff.Api/.claude/agent-memory/researcher/graph-driveitem-upload-facts.md`, verified
2026-08-20 against MS Learn + `microsoftgraph/microsoft-graph-docs-contrib` + `SharePoint/sp-dev-docs`):
simple `PUT …/content` supports **250 MB** (4 MB → 25 MB → 256 MB → 250 MB on 2023-10-25, stable since).
*"Any Spaarke code branching at a 4 MB threshold is stale by ~3 years."* So
`SdapApiClient.uploadFile`'s `LARGE_FILE_UNSUPPORTED` throw at ≥4 MB is a **live defect on a false
premise**; the `upload-session` route stays for >250 MB only.

**`conflictBehavior` IS valid on the simple PUT** — same source: values `fail | replace | rename`, *"The
default for PUT is replace"*, and the two Microsoft docs disagree on the default so **always set it
explicitly**. It is **name-collision only**, never a content/version comparison. ⚠️ Earlier notes in this
project (and the folder-removal plan) claim the simple PUT *"takes no conflictBehavior at all"* — that is
**wrong**; the SDK's `.Content.PutAsync()` does not expose it, but the REST API honours it.

**Why the 412 happens** (owner's repro, `01 - Test Matter Create Fields Only.docx`): path-keyed PUT with
no explicit `conflictBehavior` silently REPLACES on name collision and returns the **same item id**; the
second `sprk_document` insert then violates the alternate key on the SPE item id → HTTP 412 with
Dataverse's unsubstituted `{0}`/`{1}` placeholders. **The first file's bytes are already gone.** So it is
not a failing duplicate check — it is an unguarded collision that destroys data and then errors
confusingly.

**Every server ingest path already works around this by folding an id into the filename** —
`MatterPreFillService.cs:335`, `ProjectPreFillService.cs:296-301`, `UploadFinalizationWorker.cs:1081`,
`EmailAttachmentProcessor.GenerateUniqueFileName`. The **client** upload path never got that guard.

## 5. Dependency order

```
076 (container contract)  ──┐
                            ├──> 093 (wizard reorder + Secure UI)
094 Replace/Rename ─────────┘
094 "Use existing" ──── needs ──> 095 (intersection entity)
```

076 first: it establishes the record-keyed contract 093's reorder uploads against. 094's Replace/Rename
can land independently; only its "Use existing" option waits on 095.

## 6. Ship-together obligation (all of 076 + 093)

Client and BFF **must deploy together** — no compatibility window. BFF-first 404s every upload;
client-first 404s every upload. Must appear in every PR description touching these.
