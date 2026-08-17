# Task 012 — Save As uniquify: root-cause analysis + implementation plan (FR-07a)

> Captured 2026-08-15 after full code trace (main session, opus). NOT yet implemented — this is the
> precise plan so 012 can be executed cleanly (it shares `ComposeService.cs` with 013 → serialize).

## Root cause (confirmed by trace)

Save As (`saveMode === 'new'` → `forkNew=true`) flows: `ComposeWorkspace.triggerSave` (@~1365) →
create-on-save POST with `forkNew:true` + `displayName: state.documentRef.fileName` (the **original's**
name, @~1564) → `ComposeService` create-on-save (@~1346 `ResolveFileName`) → the non-dedup create branch
(@~1392) → `_spe.UploadSmallAsUserAsync(driveId, fileName, …)` (@1409).

`UploadSmallAsUserAsync` (UploadSessionManager.cs:255-258) does:
```csharp
graphClient.Drives[containerId].Root.ItemWithPath(path).Content.PutAsync(content, ct)
```
A Graph **PUT-by-path content upload defaults to `conflictBehavior=replace`**. So when a fork reuses the
original's filename in the SAME BU-container drive, Graph **replaces/versions the existing drive-item**
instead of minting a distinct one → the "fork" silently re-versions the original. That is the FR-07a
coalescing bug. (`forkNew` already correctly skips the transient-key dedup lookup @1359, and mints a fresh
`transientKey` @1389 — but the filename collision defeats the fork at the Graph layer.)

## Chosen fix — Graph `conflictBehavior=rename` for the fork create (atomic, no round-trip)

Do NOT client-side-suffix `" (2)"` (not collision-safe without a drive listing → the round-trip the
escalation trigger warns about). Instead use Graph's atomic **rename** conflict behavior, which appends
`" 1"/" 2"/…` server-side and creates a **distinct** item with **no duplicate window**. Infra already has
the enum + a session path that sets it:
- `Models/UploadModels.cs`: `ConflictBehavior { Replace, Rename, Fail }` + `ToGraphString()`.
- `UploadSessionManager.cs:531` — an upload-session create that already passes `conflictBehavior` via
  `DriveItemUploadableProperties.AdditionalData["@microsoft.graph.conflictBehavior"]`.

### Steps
1. **SPE facade**: add a `ConflictBehavior` param (default `Replace` — preserves all existing callers) to
   `UploadSmallAsUserAsync` (SpeFileStore + UploadSessionManager + the `ISpe*` interface). For the small
   PUT-by-path, set conflict behavior by either (a) switching the fork create to `CreateUploadSessionAsync`
   with `Rename` (the method @531 already does this), or (b) setting the `@microsoft.graph.conflictBehavior`
   header/query on the content PUT. Prefer routing the **fork** create through the existing session path
   (proven rename support) rather than inventing a new small-PUT conflict path. Keep non-fork creates on
   the unchanged Replace PUT.
2. **ComposeService** create branch (@1392): when `request.ForkNew`, call the upload with
   `ConflictBehavior.Rename`; else `Replace` (unchanged). The fork then lands a distinct item; capture
   `created.Name` (the Graph-renamed name) back into `fileName` (already done @1424) so the record + client
   reflect the real name.
3. **Fork logical id**: the fork must carry a NEW task-010 logical id, not the original's. Client:
   `triggerSave` already mints a fresh `transientKey` for `forkNew`; ALSO mint a fresh `composeLogicalId`
   for the fork (call `startNewComposeLogicalId()` in the `forkNew` branch and thread it so the new record's
   client identity is distinct). Verify the promoted fork's `documentRef` gets the new id, not the original's.
4. **Tests**: xUnit — a `ForkNew` create uses `Rename` conflict behavior (mock the SPE facade, assert the
   behavior arg) and yields a distinct SPE id vs the original. Client jest — `forkNew` mints a distinct
   `composeLogicalId` + `transientKey` (accessor returns a different identity than the original ref).
   Add a seam test if the create path is dispatch-spine (ADR-038).
5. **BFF gates**: publish ≤60 MB, delta vs 44.96 MB incl PDBs (net10); no new HIGH CVE; `/conflict-check`
   before the BFF PR; Placement Justification (stays in `Services/Compose/` + a defaulted param on the SPE
   facade — additive, no new service). `docxBridge.ts` untouched. Enum `'version' | 'new'` unchanged.

### Escalation check
The escalation trigger (uniquify needing a round-trip that risks a duplicate window) does **NOT** fire —
`conflictBehavior=rename` is atomic and window-free. So implement (path C-clean), do not escalate.

## Sequencing note
012 and 013 both edit `ComposeService.cs` → **serialize** (do 012, commit, then 013). Neither can run as a
parallel Group-B subagent against 013. 075 (tests) remains the only concurrent stream.
