# Task 052 — item recycle bin: measured semantics (the spec's 207 premise is half right)

> **2026-08-27** · Spec FR-E03 · Graph CSDL (both versions, no token) + live against Spaarke Dev on
> **throwaway containers** (created → activated → files uploaded → deleted → probed → torn down 204/204,
> NFR-07). No pre-existing container was mutated.
> **Status: ✅ IMPLEMENTED and live-verified.** Discovery is §1–§4; what shipped is **§5**.

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

## 3. Implementation plan (✅ DONE — see §5 for what actually shipped and where it diverged)

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

---

## 5. What shipped (implementation, 2026-08-27)

### 5.1 Code

| Layer | Added |
|---|---|
| `SpeAdminGraphService.cs` (+517 lines) | `SpeRecycleBinItem`, `SpeRecycleBinItemOutcome`, `SpeRecycleBinRestoreResult`, `SpeRecycleBinDeleteResult`, `RecycleBinRestoreRejectedException`; `ListRecycleBinItemsAsync` / `RestoreRecycleBinItemsAsync` / `PermanentDeleteRecycleBinItemsAsync` + three `…ForConfigAsync` wrappers; helpers `RecycleBinItemsUrl`, `TryMapItemNamesAsync`, `ReadReturnedIds`, `ParseRecycleBinItem` |
| `Api/SpeAdmin/RecycleBinEndpoints.cs` | 3 routes on the existing `/api/spe` group, 5 DTOs, 3 shared helpers |
| `types/spe.ts` · `speApiClient.ts` | `RecycleBinItem`, `RecycleBinItemOutcome`, `RecycleBinItemActionResult`; `speApiClient.recycleBinItems.{list,restore,permanentDelete}` |
| `components/recycle-bin/ContainerItemRecycleBin.tsx` (new) | The item-bin surface: grid, toolbar, per-item `OutcomeReport`, ADR-050 `ConfirmModal` |
| `components/containers/ContainerDetail.tsx` | 5th tab, **Recycle Bin** |
| `tests/integration/contract/SpeAdmin/SpeAdminRecycleBinItemContractTests.cs` (new) | 16 tests |
| `GraphWireMockFixture.cs` | `StubGetSequence(...)` — sequenced GET responses |

**No `Program.cs` edit and no new DI registration** — `MapRecycleBinEndpoints` was already wired at
`Api/SpeAdminEndpoints.cs:50`. **No new NuGet** (NFR-02).

### 5.2 Six decisions, and why

1. **Raw JSON through `SendGraphJsonAsync` for all three operations**, not the Kiota request builders.
   The beta actions force it anyway (`restore`/`delete` are bound to `Collection(recycleBinItem)` on
   beta only), and it **dissolves trap #3 rather than handling it**: parsing the response ourselves
   means `deletedBy` never becomes an `UntypedObject`. Better than writing a third reader for a shape
   this project has already had to measure twice.
2. **Restore → 200 only when every item restored; 207 otherwise.** Graph's 207 lists only successes,
   so partial failure is `requested − returned`.
3. **Restore rejection → 409 Conflict**, not 400. The request was well-formed; the caller's view of
   the bin is stale. Carries `remediation`, `requestedIds`, `graphMessage`.
4. **Delete never trusts its own 204** — lists the bin before AND after, then diffs. The **before**
   list is load-bearing: without it an id that was never in the bin is absent afterwards and would be
   reported as purged. That would be a fabricated success on an irreversible operation.
5. **Unverified delete → 207 with `verified: false`**, not a 5xx. The delete *was* issued and data may
   be gone; an error status would imply nothing happened. Neither direction may be asserted.
6. **Batch cap of 200 ids** — a bound on an irreversible operation.

### 5.3 🔴 New finding — Graph's error CODE for a rejected restore is not stable

Same request, same condition, measured **twice on 2026-08-27**:

| When | `code` | `message` |
|---|---|---|
| Discovery | `badArgument` | "Invalid Recyle Bin Restore Ids" *(Microsoft's typo)* |
| Implementation verification, hours later | `invalidRequest` | "One of the provided arguments is not acceptable." |

Nothing about the request changed.

**This validates keying the diagnosis on the 400 STATUS rather than on the code string.** Had the
detector matched `badArgument` — the way `IsArchivalNotEnabled` is *obliged* to match `notAllowed`,
because there a bare 403 is ambiguous — it would have stopped detecting within a week of being
written, and silently: rejections would have fallen through to the generic error path and been
reported as ordinary failures instead of "nothing was restored".

The contract test is now a `[Theory]` over **both** observed payloads, so neither wording can regress
the mapping. **Match on the narrowest signal that is actually unambiguous, and no narrower.**

### 5.4 Live verification (NFR-07)

`scratchpad/probe052_impl.py` — provisioned its own throwaway container
(`ZZ-Task052-ImplVerify-…`), uploaded 3 files, deleted them, then issued the exact URLs and bodies
the shipped code emits. **18/18 checks passed**, including:

- every field `ParseRecycleBinItem` reads is present and of the assumed JSON type, incl.
  `deletedBy.user.displayName` = "SharePoint App";
- restore → **207**, body echoes the restored id, and the item really left the bin;
- restore with one nonexistent id → **400, atomic** — the valid id was NOT restored;
- delete with one nonexistent id → **204, non-atomic** — the valid id *was* purged, and the 204
  reported none of it. This is the case the re-list-and-diff exists for.

⚠️ **Teardown initially failed** (`POST …/deletedContainers/{id}/permanentDelete` → 400). The probe's
purge verb was wrong: production uses **`DELETE /storage/fileStorage/deletedContainers/{id}`**.
Cleaned up with `scratchpad/cleanup052.py` and **verified**: 0 `ZZ-Task052` containers active, 0 in
the deleted bin. Per the live-tenant note, a teardown failure is a bug to fix, and it was fixed.

### 5.5 Verification results

| Gate | Result |
|---|---|
| `dotnet build` | **0 errors, 0 warnings** |
| BFF unit suite | **10,771 passed / 0 failed** (77 skipped) |
| SpeAdmin subset | **247 passed / 0 failed** |
| ArchTests | 122 passed / **5 failed — identical to the no-change baseline**, proven by `git stash -u` |
| Client typecheck | **124 errors — exactly the pre-existing baseline; 0 in files this task touched** |
| Client build | ✅ `npm run build` succeeds |
| **Publish size** | **45.12 MB compressed incl. PDBs** (44.19 excl.) vs 44.96 baseline → **+0.16 MB**. Ceiling 60 MB |
| CVEs | No HIGH/CRITICAL |
| New NuGet | None — no `.csproj` change |

⚠️ **Publish size must be measured COMPRESSED.** The uncompressed publish is ~138 MB, which looks
like a catastrophic breach but is not the gated metric — `Microsoft.Graph.dll` alone is 44 MB
uncompressed. CLAUDE.md §10 says "measure compressed output" but names no command; on Windows,
`Compress-Archive -CompressionLevel Optimal` over the publish folder reproduces the ~45 MB baseline.

### 5.6 Deviations from the POML

| POML said | What happened | Why |
|---|---|---|
| Tests in `tests/unit/Sprk.Bff.Api.Tests/Api/SpeAdmin/` | `tests/integration/contract/SpeAdmin/` | That path is **not** a KEEP path (task 042); tests there are scheduled for deletion at `/test-diet`. Same deviation task 050 recorded |
| Step 1 "verify the API shape against current docs" | Cited §1–§2 rather than re-probing | Already measured live during discovery, the same day |
| Step 4 "render as a surface distinct from deleted-containers" | A **tab on the container detail pane**, not a top-level screen | A deleted *file* only has meaning relative to its container. A top-level screen would need a container picker before it could show anything, and would sit next to the deleted-CONTAINERS screen inviting exactly the conflation spec D3 forbids |
| — | A true **207-partial** could not be produced live | Any invalid id makes the whole restore 400. Partial success is reachable only when an id becomes invalid between listing and restoring — a race. Covered by contract test, not by live probe |

### 5.7 §11.5 complexity note (stated, not silently grown)

`SpeAdminGraphService.cs` is now **7,271 lines** (+517 this task). This is the acknowledged god-file;
its decomposition is already scoped to **`speadmingraphservice-decomposition-r1`**, gated on
workstreams A–E. The addition is **one cohesive region with a single reason-to-change** (the
per-container item bin) and does not introduce a responsibility the file did not already hold — it is
the Graph facade for SPE admin operations. Under §11.5 that is legitimate; the LOC ratchet was retired
2026-08-20 precisely because line count is the wrong instrument. Recording it here so the growth is
visible to the decomposition project rather than discovered by it.

### 5.8 Placement Justification (CLAUDE.md §10, binding)

| Question | Answer |
|---|---|
| **Does it belong in the BFF?** | Yes. It calls Graph SPE admin endpoints with the per-customer owning-app credential resolved from Dataverse + Key Vault. No client can hold that credential, and `Xrm.WebApi` cannot reach Graph — the [data-access criteria](../../../docs/standards/DATA-ACCESS-DECISION-CRITERIA.md) resolve to BFF unambiguously |
| **New endpoints?** | 3, registered through the existing `MapRecycleBinEndpoints` extension on the `/api/spe` group — not in `Program.cs` (ADR-001) |
| **New DI registrations?** | **None** |
| **New packages?** | **None** (NFR-02) |
| **New background work?** | None |
| **§11 existing** | `RecycleBinEndpoints.cs` exists but serves `/deletedContainers` — deleted CONTAINERS, a different Graph resource and a different admin need |
| **§11 extension** | Extended that file rather than creating a parallel surface. Both features coexist per spec D3 |
| **§11 cost-of-doing-nothing** | Deleted **items** are unrecoverable through the admin tool; an admin must drop to PowerShell for what a screen named "Recycle Bin" implies it already does |
