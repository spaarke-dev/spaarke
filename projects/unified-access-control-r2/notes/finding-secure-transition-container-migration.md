# Finding — a record flipped to SECURE leaves its existing files in the shared container

> **Filed**: 2026-08-31, by owner direction during the #858 design discussion.
> **Disposition**: **its own focused project**. Documented here; revisit after core UAC-r2 completes.
> **Status**: NOT implemented, NOT scheduled. This file is the handoff.

---

## 1. The gap, in one paragraph

Marking an existing `sprk_matter` / `sprk_project` as secure changes where **new** content goes and
nothing else. `RecordContainerResolver` will from that moment resolve new writes to the record's own
`sprk_containerid`, but every file already written stays in the shared business-unit container, and
each `sprk_document` row keeps pointing at the old drive + item. SPE permissions are **additive-only**,
so nothing retracts access at the old location. The result is a **half-secured record**: the UI says
secure, new content is isolated, and the existing content is still sitting in a container the whole
business unit can reach.

**Verified absent** (2026-08-31): no code in `src/**` moves or copies documents between containers.
The only move/copy matches are SPE Admin's recycle-bin and clipboard UI, which are unrelated.

## 2. Why it has not bitten yet

**Zero secure projects exist in any environment.** Dev holds none (task 047's premise). So no
half-secured record exists today. The window is open only until someone flips the first one — after
that, every such record needs the migration this file describes, retroactively.

This is the same "build it before the first one and there is never a migration" argument that put
Phase 0c ahead of the rest of the project. It applies here with the same force.

## 3. Prior art in this project — the gap was known, never scoped

`tasks/TASK-INDEX.md` lists, under work explicitly *not* in these waves:

> *FR-31's "secure is reversible" wizard copy, which is false without retro-securing migration
> (owner decision).*

So the gap is recorded. What never existed is a component, a design, or an owner. This file supplies
the first two in sketch form.

Related and distinct: **§5.2's BU-depth prerequisite** (task 046). `Spaarke Basic User` holds
`prvReadsprk_Project` at **Deep** depth, and Deep at the root BU reaches every descendant BU — so an
ordinary non-admin read a real secure project. **A correct container is worth little while role depth
reaches across every BU.** Any project taking on this migration must state which of the two it is
solving; they are independent, and fixing only the containers would produce a false assurance.

## 4. What the component has to do

Sketch, deliberately not a design — the hard parts are 5 and 6.

| # | Step | Note |
|---|---|---|
| 1 | Enumerate documents whose current container ≠ the record's now-correct container | Needs a query keyed on the owning record, not on the container |
| 2 | Copy bytes to the destination container | Graph's copy is **asynchronous** — needs a monitor/poll loop, not fire-and-forget |
| 3 | Update `sprk_graphdriveid` / `sprk_graphitemid` / `sprk_filepath` on each row | `sprk_filepath` stores the full resolved `webUrl`, so it changes too |
| 4 | Delete the source item **only after** the copy verifies | Verify means the destination item exists AND its size/hash matches |
| 5 | **Permanently** delete the source — not to the recycle bin | A recycle-bin item is still content in the shared container. This is why step 4 cannot be softened |
| 6 | Idempotent + resumable | A partial run must be safe to re-run; a run interrupted between copy and source-delete must not lose or duplicate content |

### 4.1 The part that is genuinely hard

**The migration cannot retract access already granted at the old location.** Anyone holding a link,
a delegated permission, or a cached pointer to a source item keeps it until that item is genuinely
destroyed. So:

- Step 5 is load-bearing, not hygiene.
- Any sharing link minted against a source item survives the migration unless separately revoked —
  which connects this to **task 012** (`completed-with-escalation`): anonymous share links carry a
  ≤7-day non-retractable window, and that window would span a migration.
- Honest claim at the end of a migration run is therefore *"new access paths point at the secure
  container, and old items are destroyed"* — **not** *"nobody who previously had access still has
  it."* The second is unachievable for already-minted anonymous links inside their expiry window.

## 5. Open questions a project must answer

1. **Trigger** — on `sprk_issecure` flipping true (plugin? webhook? polling?), or operator-invoked, or
   both? A plugin is banned by the sibling project's CLAUDE.md; check this project's constraints before
   assuming.
2. **Synchrony** — the flip cannot block on a copy of potentially thousands of files, so the migration
   is asynchronous. What does the record look like *during* it, and what does a user who opens a
   document mid-migration see?
3. **Failure** — a document that cannot be copied (locked, checked out, oversized) must not silently
   remain in the shared container while the record advertises itself as secure. Partial success is the
   dangerous state; decide whether it blocks the flip, reverts it, or surfaces loudly.
4. **Reverse direction** — FR-31's "secure is reversible" copy implies secure → non-secure as well.
   That direction is *easier* (no isolation guarantee to preserve) but still needs the pointer rewrite.
5. **Scope of "documents"** — `sprk_document` rows are the index, but Compose drafts, email
   attachments, and analysis outputs all land through different paths. The enumeration in step 1 must
   be keyed on the record, and it must be provably complete or the migration inherits the
   argument-from-absence problem this project keeps hitting.
6. **Interaction with the container-derivation consolidation** — if the acting-user container
   derivation moves server-side (see `notes/alignment-with-compose-r8-2026-08-30.md` and the #858
   discussion), a record-less draft can land in a BU container and be associated to a secure matter
   *later*. That is a second, ongoing source of exactly the state this migration repairs — so the two
   pieces of work are related and should be sequenced deliberately.

## 6. Why this is not being done inside UAC-r2

Scope. UAC-r2's remaining work is the container **selection** defect class — making the server choose
the destination from a record it authorized. This is a **data migration** component with an async
copy pipeline, an operator surface, a failure model, and an interaction with share-link revocation.
It is a project, not a task, and folding it in would delay the selection work that is already in
flight.

Recorded per owner direction 2026-08-31: *"yes this likely requires its own focused plan/project;
document it and we will revisit after we get the core UAC-r2 completed."*
