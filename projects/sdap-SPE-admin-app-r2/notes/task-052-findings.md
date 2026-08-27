# Task 052 — item recycle bin: measured semantics (the spec's 207 premise is half right)

> **2026-08-27** · Spec FR-E03 · Graph CSDL (both versions, no token) + live against Spaarke Dev on
> **throwaway containers** (created → activated → files uploaded → deleted → probed → torn down 204/204,
> NFR-07). No pre-existing container was mutated. **Status: discovery complete, implementation not started.**

---

## 1. The API surface

`recycleBinItem` is `BaseType="graph.baseItem" OpenType="true"`, so the wire shape is wider than the
CSDL's three declared properties. Measured live:

| Field | Source | Notes |
|---|---|---|
| `id` | entity | GUID |
| `name` | baseItem | |
| `title` | **OpenType extra** | duplicates `name` in every row observed |
| `size` | declared | bytes |
| `deletedDateTime` | declared | |
| `deletedFromLocation` | declared | e.g. `contentstorage/CSP_…/Document Library` |
| `deletedBy` | **OpenType extra** | `{"user":{"displayName":"SharePoint App","email":"","id":"1073741822"}}` |

⚠️ `deletedBy` and `title` are **not in the CSDL** — they arrive through `AdditionalData`, which per task
050's lesson means Kiota will materialise `deletedBy` as an **`UntypedObject`**, not a `JsonElement` and
not an `IDictionary`. `deletedBy` is the most operationally useful field here (who deleted it) and it is
exactly the kind that gets silently dropped by a wrong-shaped reader.

### Version: the actions are beta-only

| | v1.0 | beta |
|---|---|---|
| `recycleBin` / `recycleBinItem` entity types | ✅ | ✅ |
| `restore` action (bound to **`Collection(recycleBinItem)`**, param `ids`) | ❌ **absent** | ✅ |
| `delete` action (bound to the collection, param `ids`) | ❌ **absent** | ✅ |

🔴 **The knowledge corpus says v1.0** — `knowledge/sharepoint-embedded/docs/learn-containers.md` cites
*"Restore recycleBinItem — Graph **v1.0**"*. There are **no** recycleBin-bound actions in the v1.0 CSDL
at all. Same class of error as the archival "GA Feb 2026 ⇒ v1.0" assumption corrected by task 050.
**The corpus needs the same correction.** No ADR issue — the container surface is already beta-pinned by
task 020.

---

## 2. 🔴 The two operations have OPPOSITE failure semantics

This is the finding that shapes the implementation. Measured on throwaway containers with three real
uploaded-then-deleted files.

| | all ids valid | any id invalid/unknown | response body |
|---|---|---|---|
| **`restore`** | **207 Multi-Status** | 🔴 **400 `badArgument`** — *nothing* restored, atomic | `value: [{id}, …]` — **the ids that SUCCEEDED** |
| **`delete`** (permanent) | **204** | 🔴 **204** — *and it deletes the valid ones anyway*, non-atomic | **none** |

Verified transitions:
- `restore` 1 valid → `207`, `value` = that one id.
- `restore` 2 valid → `207`, `value` = both ids.
- `restore` valid + well-formed-but-nonexistent GUID → `400`, bin unchanged.
- `restore` nonexistent GUID alone → `400`.
- `delete` valid + nonexistent GUID → **`204`, bin went 3 → 2** — the valid one WAS purged.
- `delete` 2 valid → `204`, bin went 2 → 0.

(The first probe's `400 "Invalid Recyle Bin Restore Ids"` — Microsoft's typo — came from a
**malformed** id. A well-formed but nonexistent GUID gives the different `badArgument` message. Two
distinct rejection paths, both 400.)

### What this means for the spec

Spec FR-E03 says *"**`207 Multi-Status` partial success is handled explicitly** — per-item outcomes
reported, not collapsed to pass/fail."* That is **half right**, and the half that is wrong is the
dangerous half:

- ✅ **Restore does return 207** and partial outcomes are real — but they are expressed as
  **`requested ids − returned ids`**. There is no per-item error object. If you send 3 and get 2 back,
  the third silently did not restore, and Graph does not say why. The implementation must compute the
  set difference and name the missing items; treating 207 as "success" hides them.
- 🔴 **Permanent delete has no 207 and no per-item reporting at all.** It answers `204` whether it
  purged everything, some, or nothing, with an empty body. **For an irreversible operation this is the
  worst reporting shape in the API surveyed so far.** The only way to know what actually happened is to
  re-list the bin afterwards and diff.

So the acceptance criterion is achievable for restore, and for delete it must be **re-read the bin, do
not trust the 204** — the same discipline task 051 applied to the quota write.

---

## 3. Implementation plan (not started)

| Layer | Work |
|---|---|
| `SpeAdminGraphService` | `SpeRecycleBinItem` record (incl. `DeletedBy` via an `UntypedObject`-aware reader — see `ReadArchiveStatus` for the pattern); `ListRecycleBinItemsAsync`; `RestoreRecycleBinItemsAsync` returning **per-id outcomes** from the 207 set difference; `PermanentDeleteRecycleBinItemsAsync` that **re-lists and diffs** rather than trusting the 204 |
| `RecycleBinEndpoints` | `GET /api/spe/containers/{id}/recyclebin/items`, `POST …/restore`, `POST …/delete`. Extends the existing file per the POML's `<justification>`; **must stay distinct from the deleted-CONTAINERS routes** (spec D3) |
| Errors | `400 badArgument` on restore → a distinct "nothing was restored; one or more ids are no longer valid — refresh and retry" message, NOT a generic 400. It is materially different from a partial success |
| Client | A recycle-bin **items** surface distinct from the deleted-containers view; per-item restore outcomes (restored / not restored, both named); ADR-050 `ConfirmModal` for permanent delete naming the items |
| Tests | WireMock: 207 with fewer ids than requested (the partial case), 207 with all ids, 400 badArgument, and the delete-204-that-did-not-delete-everything case |

### Traps to carry forward

1. **Do not treat 207 as success.** Diff requested vs returned.
2. **Do not trust delete's 204.** Re-list and diff; it is non-atomic and silent.
3. **`deletedBy` is `UntypedObject`** — the third time this project has had to learn the AdditionalData
   shape by measurement rather than assumption (022 `deletedDateTime`, 050 `archivalDetails`).
4. **Uploads for the live fixture go through `/drives/{driveId}/root:/{name}:/content`.**
   `/storage/fileStorage/containers/{id}/drive/root:/…` answers `400 invalidRequest: "API not found"`.

---

## 4. Side finding — the `communications` / `emails` / `exports` folder origin is RESOLVED

Standing open question since the File Browser walkthrough; flagged as a prerequisite for this task
because destroying the container would destroy the investigation. Answered read-only, no mutation:

| Folder | `createdBy` | Created |
|---|---|---|
| `communications` | **SharePoint App** (app-only identity) | 2026-03-11 |
| `emails` | **SharePoint App** | 2026-01-13 |
| `exports` | **Ralph Schroeder** (interactive) | 2026-03-22 |

So `communications` and `emails` were created by **Spaarke's own app-only identity** — the platform
writing its own artifacts, consistent with the active email/communications projects. `exports` was
created by the operator by hand. Nothing foreign, nothing unexplained.

This **closes** the third bullet of the live-tenant safety note's evidence list. The note's other two
reasons (repeatability; the tenant is shared) are unaffected and still hold, so the
throwaway-container rule stands unchanged.

⚠️ Task 052 never needed this resolved for safety anyway: the 041 fixture provisions and tears down its
own container, so `Spaarke Inc` is never a target.
