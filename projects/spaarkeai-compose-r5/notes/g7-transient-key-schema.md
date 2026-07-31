# G7 Transient-Key Dataverse Schema (Option B — operator-chosen 2026-07-30)

> Task 022 (G7). Fixes the 8-duplicate defect with a durable, cross-session/tab stable transient key
> (more robust than IDistributedCache). Requires a HUMAN Dataverse schema gate — operator making the
> change 2026-07-30. Implementation is BLOCKED until the field + alt-key are Active.

## Why (root cause)
A `sprk_document` is deduped ONLY by `sprk_graphitemid` (the SPE drive-item id). A transient (not-yet-
promoted) draft has NO id until its first save mints one; the transient branch mints a NEW SPE item on
EVERY create-on-save call, and the ONLY thing preventing duplicates is the client carrying the
server-minted id back before the next save. Lost/raced round-trips (concurrent saves, re-created mount,
new tab) → N SPE items → N duplicate records. There is no server-side stable transient key. R4.5 WS-1
did NOT provide one (it built a stateless projection reader, not a record identity) — so we add one.

## Schema change (operator)
**Table:** `sprk_document` (same table as G1 `sprk_composeorigin`).

**Column:**
- Schema name: `sprk_composetransientkey`
- Display name: Compose Transient Key
- Type: Single line of text (Format: Text), MaxLength 100, Optional
- Description: "Client-minted stable key for a transient (not-yet-promoted) Compose draft; dedups repeated
  create-on-save calls before the SPE drive-item id exists (G7/FR-06). Set once at first create-on-save;
  null for imported/existing docs."

**Alternate key:**
- Key name: `sprk_composetransientkey_uk`
- Column(s): `sprk_composetransientkey` (single-column)
- Existing rows null (allowed; nulls not enforced-unique). Activation async — wait for Active.

## Implementation plan (when field is Active) — for post-compaction recovery

### Client (`ComposeWorkspace.tsx`)
1. Mint a transient key `crypto.randomUUID()` ONCE when a transient draft is mounted — set it in
   `documentRef.transientKey` in `mountTransient`, `mountDraftHtml` (DEF-08 AI-seed), and the upload
   transient mount. Add `transientKey?: string` to the workspace state `documentRef` + types.
2. Send `transientKey` in the **create-on-save** request body (the `isTransientCreate` branch, ~:1096).
   (Replace-path saves don't need it — they already have the SPE id.)
3. **Split-button (Save Version / Save New):** extend `triggerSave` to accept `saveMode: 'version' | 'new'`
   (default 'version'). 'new' (fork) → mint a FRESH transientKey + force the create-on-save path even when
   `speDriveItemId` exists (so it forks a new record), and send `saveMode:'new'` / a `forkNew:true` flag.

### Server (`ComposeService.cs` + `IComposeService.cs` + `ComposeEndpoints.cs`)
4. Add `TransientKey` (string?) to `SaveComposeDocumentRequest` + the create-on-save endpoint body + the
   `PromoteComposeDocumentRequest`.
5. In the create-on-save flow (transient branch ~:866 / `PromoteIfEphemeralAsync` ~:1563): BEFORE minting
   a new SPE item, if `TransientKey` is present, `TryFindDocumentByTransientKeyAsync`
   (`RetrieveByAlternateKeyAsync` on `sprk_composetransientkey_uk`) → if a row exists, REUSE its
   `sprk_graphitemid` (SPE id) and take the REPLACE path (dedup — no new mint); else mint + create +
   stamp `entity[sprk_composetransientkey] = TransientKey`. Mirror `TryFindDocumentByGraphItemIdAsync`
   (:2213) exactly. No text-search (I-7) — resolve by the key.
6. **Save-New fork:** when `forkNew` is set, SKIP the transient-key dedup lookup and force a create (new
   record), even if an SPE id / transient key would otherwise match — the deliberate fork.

### Split-button UI (`ComposeFormatToolbar.tsx`)
7. Replace the primary Save `ToolbarButton` (:816-828; and the UX-1 duplicate :702-716) with a Fluent v9
   `SplitButton` inside `MenuTrigger`/`MenuPopover`/`MenuList`: primary "Save Version" → `onSave('version')`;
   menu `MenuItem` "Save New Document" → `onSave('new')`. Mirror the blessed pattern in
   `Spaarke.UI.Components/src/components/EmailComposer/subcomponents/ComposerActionBar.tsx:16-20,167-189`.
   Theme tokens only, dark-mode (ADR-021). `onSave` prop signature becomes `(mode)=>void` threaded
   ComposeWorkspace→ComposeEditor→ComposeFormatToolbar.

### Tests
8. Seam (`tests/integration/seam/Compose/`): (a) Save-Version replaces in place (one record);
   (b) Save-New forks a new record; (c) **8-duplicate:** repeated create-on-save with the SAME
   transientKey (RetrieveByAlternateKey returns the existing row on calls 2..N) → ONE record, no dup mint.
9. UI tests: split-button render / dark-mode / Save-Version-vs-Save-New interaction (jest — note the
   worktree @spaarke workspace-resolution limitation from tasks 020/021; server seam is the binding DoD).

### Verify
10. Build; full Compose suite (R4.5 non-regression); corpus byte-diff 24/24; publish ≤60 MB (+~0, no new
    package); ArchTests (3 pre-existing failures only); Step 9.5 gates.

## Key file:line anchors (from investigation subagent 2026-07-30)
- create-vs-replace discriminator: `ComposeService.cs:665` (`isTransientCreate = string.IsNullOrWhiteSpace(request.DocumentSpeId)`)
- transient branch mints new SPE item each save: `ComposeService.cs:866-899` (upload :883)
- replace branch: `ComposeService.cs:900-927`
- idempotent promote by SPE id: `ComposeService.cs:1563-1606`; alt-key lookup `:2213-2239` (`TryFindDocumentByGraphItemIdAsync`, `GraphItemIdAttribute`)
- client create-vs-replace routing: `ComposeWorkspace.tsx:983`, `:1080-1082`; carry-id-back `:1228-1239`
- live Save button: `ComposeFormatToolbar.tsx:816-828` (dup :702-716); wiring ComposeWorkspace:2511-2515 → ComposeEditor.tsx:2647-2649
- SplitButton mirror: `ComposerActionBar.tsx:16-20,167-189`; Fluent dep `Spaarke.Compose.Components/package.json:33`
- `sessionFileId` (rejected alt primitive, uploads-only): `compose-contracts.ts:534-541`
