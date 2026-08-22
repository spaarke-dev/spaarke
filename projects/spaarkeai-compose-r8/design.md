# Spaarke Compose R8 — Make Compose Work: Save Reliability, Faithful Write Model, Durable Files

> **Project**: `spaarkeai-compose-r8`
> **Created**: 2026-08-19 · **Revised**: 2026-08-19 (post owner Q&A + Fable architectural review + external research) · **Author**: Ralph Schroeder + Claude (Opus 5)
> **Status**: DESIGN (hand-authored input to `/design-to-spec` → `/project-pipeline`)
> **Governing ADR**: [ADR-049 Compose Shadow Document](../../.claude/adr/ADR-049-compose-shadow-document.md) — R8 files a **third Path-B amendment** (§10).
> **Seeded by**: [`notes/fidelity-architecture-investigation.md`](notes/fidelity-architecture-investigation.md) · [`notes/durable-session-files.md`](notes/durable-session-files.md)
> **Evidence base**: [`../spaarkeai-compose-r7/notes/uat-issues.md`](../spaarkeai-compose-r7/notes/uat-issues.md) · the 2026-08-19 Fable review of the save path · external research on browser-editor OOXML fidelity (§4)
> **Mandate**: this is release **eight**. Compose must work. Not "be architecturally correct" — **work**.

---

## 1. The mandate, restated honestly

Two things are true at once and R8 must fix both, in this order:

1. **Users cannot reliably save.** This is the owner's stated blocker and it outranks everything. A document
   that will not persist makes fidelity irrelevant.
2. **Saves that do land silently destroy formatting.** Fonts, sizes, colors, paragraph spacing, footnotes,
   cross-reference fields, content controls and floating objects are gone on the first save of any imported
   legal document — the two worst losses with **no warning at all**.

The move to the render-on-save engine in R6 was supposed to fix fidelity. It did not; it traded a hard-failure
mode for a silent-loss mode. R8 is not another swing of that pendulum — §4 establishes, from primary sources
and from a full review of our own save path, what the third answer actually is.

### 1.1 A framing correction, recorded because it shaped R5–R7

**"Fix fidelity and it will save" inverts the actual causality.** R6 did not sacrifice saves to protect
fidelity — it **sacrificed fidelity to protect saves**. The ADR-049 R6 amendment says so in terms: hard-tier
constructs "accept-flatten **with a warning, never a 422**." The *"certain formatting was dropped"* banners
users see are not a symptom of the save problem; they are **the receipt for the trade made to stop it**.

One narrow genuine fidelity→save link exists and R8 closes it: when the canonical projection *fails* at load
(not degrades — fails), the document falls back to the op-log path ([ComposeService.cs:608-613](../../src/server/api/Sprk.Bff.Api/Services/Compose/ComposeService.cs#L608))
and inherits all of R4's anchoring failure modes. Real, but narrow — and not the 412 loop, the dead client
error handler, the bricked Save button, or the size ceilings.

**Consequence for R8**: save reliability gets its own track, its own contract and its own telemetry (§3), so
the next save failure is diagnosed from data rather than re-argued from theory. Seven releases of
symptom-driven save fixes is the evidence that argument is not working.

**Prior-art discipline**: two independent reviews (an architectural review of our code, and research into how
Word for the web, Google Docs, OnlyOffice, LibreOffice, Syncfusion and EigenPal actually solve this) converged
on the same answer. Their findings are load-bearing throughout and are cited inline rather than asserted.

---

## 2. Why R1–R8 kept failing (the pattern under the pendulum)

| Release | Write model | Bought | Cost |
|---|---|---|---|
| R3 | Paragraph diff + a second text-search writer | — | Two write paths; structural edits inexpressible |
| **R4** | **Surgical byte-patch** — ops anchored `(paraId, runIndex, offset)`; untouched subtrees byte-identical; opaque atoms | Real fidelity | **HTTP 422** treadmill on real docs |
| **R6** | **Render-on-save** — re-author the whole `<w:body>` from a thin model | 422 dead by construction | **Silent fidelity loss** |
| **R8** | ← §5 | Both | — |

Four recurring mistakes, each with a structural fix R8 must carry:

1. **The release gate only ever encoded the current release's fear.** R4's gate proved byte-identity but never
   forced no-hard-fail, so 422s shipped. R6's harness [explicitly does not assert preservation](../../tests/integration/seam/Compose/ComposeFidelityGateHarnessTests.cs).
   Each swing was invisible-regression-by-design in the *other* dimension. → **The gate must carry both
   invariants permanently, and the ADR must state both as standing MUSTs** so no future amendment can trade
   one away quietly.
2. **Two independent OOXML walkers per release.** Every failure class lives in a disagreement between walkers:
   R3's Bug A (two writers), R4's 422s (patch walk vs projection walk on `mc:AlternateContent`/duplicate
   paraIds), UAT-12 (annotation reader vs projection). R4.5 fixed this on the read side ("one reader", F-2);
   **nobody ever stated the write-side analog.** → **The projection is the only coordinate system. Nothing
   else may independently resolve document positions.**
3. **Save reliability was never a tracked property.** Eight releases of meticulous fidelity accounting, and
   "the file will not save" arrives as an anecdote with zero telemetry, no save-success metric, and client
   error-path tests that validate **dead code** (§3 F1). → **Save-outcome telemetry + a wire-visible outcome
   contract.**
4. **Feature accretion on an unsettled core.** `SaveAsync` is a ~600-line method inside a 3,573-line service
   that also does summary pages, memory capture, profile dispatch, PDF intake, dedup and session rebind —
   accreted across R1–R7 while the write model flipped twice. → **Track D (§8) and a freeze rule.**

---

## 3. Track S — Save reliability (P0, starts immediately, no architecture gate)

**This track did not exist in the first draft of this design. Its absence was the draft's central error.** The
corpus harness exercises none of these: they are client-contract, lifecycle and storage-boundary defects, and
**none of them wait on the write-model decision.** Estimated days, not weeks.

Verified failure inventory from the Fable review, ranked by expected real-world frequency:

### S-1 (F1) — Every HTTP-status-specific save-error handler on the client is dead code ⛔
[`authenticatedFetch`](../../src/client/shared/Spaarke.Auth/src/authenticatedFetch.ts#L45) returns **only** when
`response.ok` and **throws `ApiError`** otherwise. So the entire `if (!response.ok)` block in the save path —
the 423 lock banner with Retry ([ComposeWorkspace.tsx:1928](../../src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeWorkspace.tsx#L1928)),
the 412 reload flow (`:1943`), the 403 copy (`:1947`) — is **unreachable**. Every server refusal lands in the
generic catch (`:2160`) as `Save failed: <detail>` with no Retry and no recovery. `ApiError` carries `.status`;
the catch ignores it. **The client tests mock `authenticatedFetch` to return `{ok:false}` — a shape the real
function cannot produce — so the suite green-lights dead code.**
→ Route on `ApiError.status`. Rebuild client error handling on the real `ApiError` contract, with tests that
drive the thrown path.

### S-2 (F2) — The 412 dead-end loop ⛔ **← top root-cause candidate for "the file will not save"**
R7's UAT-25/26 fix (`6a75eb7b7`, deployed **2026-08-18**) made the mainstream ContentModel save refuse with
**412** whenever the live SPE eTag differs from the stamp ([ComposeService.cs:1227](../../src/server/api/Sprk.Bff.Api/Services/Compose/ComposeService.cs#L1227)).
Correct server behavior — but combined with S-1 the user sees a failure with **no reload path**, and because
`state.etag` only updates on `saveSucceeded`, **every retry re-412s forever** until a full page reload. It also
compares **eTag, not cTag** — Graph eTags move on metadata-only changes and on Word-for-web sessions that write
no user-visible edit, so **false-positive 412 loops are possible, not just true concurrent-writer ones**. The
owner's report arrived immediately after this shipped.
→ **The owner's decision on concurrency (§7) resolves this directly**: last-writer-wins with a warning replaces
the 412 refusal. Where a refusal is still right, it must render a working reload-and-reapply flow.

### S-3 (F4) — A failed born-in-editor save permanently disables Save and reports "Saved" ⛔
[`buildContentModel()`](../../src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeEditor.tsx#L3005) resets the editor dirty flag
**at build time, before the POST**. If the POST fails, `isDirty` is false → Save button disabled, Ctrl+S inert,
toolbar shows `Saved · Auto Save On`, and **both the unmount flush and the `beforeunload` prompt are
disarmed**. The imported path explicitly avoids this (`:3010` — "NO dirty reset at build time — a rejected save
must leave the Save button live for retry"); the born-in-editor path never got the same fix. **This is the
literal shape of "the file will not save": the button is dead and the app claims success.**

### S-4 (F3) — 423 Word co-authoring lock has no working recovery
Compose ships "Open in Word"; the doc holds the SPE lock ~30 min after close. Under S-1 the user gets a
dead-end error instead of the designed Retry bar. Open-in-Word → return → edit → save is our own advertised
loop, so this fires routinely.

### S-5 (F5) — No in-flight guard, timeout, or AbortSignal → permanent "saving" wedge
The save fetch has none, though every *load* path does. A hung request leaves `status === 'saving'` forever:
`triggerSave` early-returns, the Ctrl+S listener is unmounted, the spinner never clears. Only a reload
recovers, losing up to 15s of work.

### S-6 (F11) — HTTP 200 with nothing written
`BuildContainerFailedResult` returns a failed-step result; the endpoint maps it `Results.Ok`, and
`SaveComposeDocumentResponse` **has no `CompletionState` field**. A total write failure presents as
**"Saved ✓"**.
→ Put a closed **save-outcome enum on the wire** (§3.1).

### S-7 (F12a) — Re-anchor failure clobbers a newer version with pre-edit bytes
If the re-download of current bytes fails, `ReanchorStaleSaveAsync` returns the **load-time baseline**, which
the caller then **writes** — overwriting the external writer's newer version with stale content, HTTP 200.

### S-8 (F14) — Document-size ceilings: 4 MB on first save, ~22 MB thereafter ⛔
**Two undocumented walls, both far below what legal documents need, and both fixable with code we already own.**

- **First save (create-on-save) dies at 4 MB.** [`UploadSmallAsUserAsync`](../../src/server/api/Sprk.Bff.Api/Infrastructure/Graph/UploadSessionManager.cs#L244)
  throws `ArgumentException` above 4 MB → surfaced as a raw **400**. This is a *method-selection threshold, not
  a platform limit*: Graph does simple PUT to 250 MB and upload sessions far beyond. **We already implement
  chunked upload** — [`SpeAdminGraphService.cs:2824`](../../src/server/api/Sprk.Bff.Api/Infrastructure/Graph/SpeAdminGraphService.cs#L2824)
  routes ≥4 MB to `LargeFileUploadTask` (5 MB chunks) and [`ContainerItemEndpoints.cs:156`](../../src/server/api/Sprk.Bff.Api/Api/ContainerItemEndpoints.cs#L156)
  auto-routes. **Compose simply never calls it.** A wiring gap, not a capability gap.
- **Subsequent saves die at ~22 MB.** The client base64-encodes the whole document into the JSON body (~33%
  inflation) and there is **no `MaxRequestBodySize` override anywhere in the BFF**, so Kestrel's 30 MB default
  applies → an opaque **413**. The replace path has no Graph-layer guard, so this ceiling is invisible until hit.

→ Route Compose to the existing chunked path; raise/justify the request-body limit or stop base64-in-JSON
(binary upload); give an oversized document an honest pre-flight message, never a raw 400/413.

### S-9 — The remaining honest-failure set
Silent guard drops (`status !== 'loaded'` returns with zero feedback) · the name-on-first-save modal as a silent
hard gate · container/drive/tenant preconditions (`canSaveNow` checks `bffBaseUrl` but not `tenantId`) · the
non-dismissible checkout-conflict modal whose force-close path has the same dead-`!response.ok` bug · Dataverse
promote failing **after** the SPE write (bytes persisted, user told "failed") · Graph 429 → generic 500 ·
`sprk_filesize`/`sprk_filepath` never refreshed on replace saves · the single global localStorage draft slot.

### 3.1 The save-outcome contract (the structural fix)

A **closed enum of terminal outcomes** — `persisted` · `persisted-with-warnings` · `refused-stale` ·
`refused-locked` · `refused-invalid` · `storage-failed` · `partially-recorded` — each with (a) a wire
representation on `SaveComposeDocumentResponse`, (b) a defined client recovery behavior, and (c) a seam test.
Today this taxonomy exists only as scattered ProblemDetails **the client cannot even receive**.

Plus **save-outcome telemetry**, mirroring the `cosmos.write_failures` precedent that R7's R-5 investigation
established after an 11-day silent write outage. A save failure must never again reach us as an anecdote.

**Binding rule going forward**: no change to a save-path status code merges without the paired client-recovery
test. S-2 shipped a new server refusal on the same day its client handler was dead.

---

## 4. What the third answer actually is (evidence, not preference)

### 4.1 Microsoft does not attempt what we were attempting

Word for the web — same vendor, same format, server-side, perfect model access — still refuses to represent
everything. From the [service description](https://learn.microsoft.com/en-us/office365/servicedescriptions/office-online-service-description/word-online),
verbatim: bibliographies, tables of authorities, citations, OLE embeds, watermarks and signature lines "appear
as **placeholders that you can delete but not edit or update**"; content controls can be viewed but not added;
a restricted-editing or password-protected document opens **read-only**.

Two shipped patterns we use neither of:
- **Opaque placeholder** — delete is the only mutation permitted, because delete is the only mutation that
  cannot corrupt a construct you don't model.
- **Capability gate** — when a document exceeds what the editor can safely handle, **refuse into read-only**.
  Refusing is a feature.

**No product ships a lossless browser editor for OOXML.** Google Docs converts (loses equations, content
controls, macros). Syncfusion's SFDT is a lossy re-model by their own docs. OnlyOffice and Collabora run real
engines and are AGPL/commercial. That is not our bar and never was.

### 4.2 `w14:paraId` cannot be an identity key — this is spec-level, not a data quirk

[MS-DOCX], normative: paraId is unique within the document **part**, "**with the exception that it need not be
unique across the choices or fallback of an Alternate Content block**."

Three bug classes, and R4 hit all three at once on `AppligentNDA_Signed.docx`:
1. **Duplicate paraIds are spec-legal.** Word writes shapes and text boxes as `mc:Choice` + `mc:Fallback` **by
   default** — same paragraph, same id, twice.
2. **Uniqueness is part-scoped.** `header1.xml`, `footnotes.xml`, `comments.xml` have independent id spaces; a
   globally-keyed map is wrong by construction.
3. **Text boxes nest paragraphs.** `w:txbxContent` holds full `w:p` elements, so `body.Descendants<Paragraph>()`
   interleaves them and destroys every ordinal assumption.

And separately, [Open-XML-SDK #925](https://github.com/OfficeDev/Open-XML-SDK/issues/925): **Word regenerates
paraId values on save** (`1AD69E69` → `6B99AEB6` after merely adding a comment).

**R4 did not fail because surgical patching is hard. It failed because its anchor does not have the properties
the spec grants it.** paraId is a hint, never a primary key. Note our code uses
`body.Descendants<Paragraph>()` in **ten** places including [ComposeDocxProjectionBuilder.cs:128](../../src/server/api/Sprk.Bff.Api/Services/Compose/ComposeDocxProjectionBuilder.cs#L128)
and [ComposeDocumentRenderer.cs:1742](../../src/server/api/Sprk.Bff.Api/Services/Compose/ComposeDocumentRenderer.cs#L1742).

### 4.3 The pattern that works has a name, and we already own half of it

**Grab bag** (LibreOffice `InteropGrabBag` — unmodeled properties set aside on import, re-emitted verbatim on
export; the Document Foundation describes it as deliberate design). EigenPal states the same: "unmodeled XML
elements and attributes are kept as **generic nodes and re-emitted in place**."

Four tiers:
1. **Block opaque atom** — verbatim XML subtree, non-editable, non-movable, deletable placeholder.
2. **Inline opaque atom** — fields (`fldSimple` *and* the `fldChar` begin/separate/end run triple), `w:sdt`,
   `w:drawing`/`w:pict`, footnote/endnote references.
3. **Property grab bag** — unmodeled children of `w:pPr`/`w:rPr`/`w:sectPr`/`w:tblPr` carried verbatim.
   **This is the tier whose absence caused R6's loss: fonts, sizes, colors and spacing all live here.**
4. **Part passthrough** — we already have this.

Crucially the carry rides on the model node itself, so **it needs no identity key at all** — the model is
document-ordered. That sidesteps §4.2 entirely.

**And R4 already built the client half.** [`opaqueAtomNode.ts`](../../src/client/shared/Spaarke.Compose.Components/src/widgets/opaqueAtomNode.ts)
defines `composeBlockAtom` and `composeInlineAtom` as ProseMirror `atom: true` leaves with exactly the
Word-for-the-web placeholder semantics. The research flagged "can a ProseMirror schema hold a catch-all node?"
as the make-or-break unknown; **our codebase already answers it.** The gap is narrow: those nodes carry
[identifiers only](../../src/client/shared/Spaarke.Compose.Components/src/widgets/opaqueAtomNode.ts#L88) — correct under R4 (the server
retained the bytes the id pointed into), fatal under render-on-save (no retained bytes at render time). Also
useful: `atomId` is **server-minted and collision-checked**, explicitly "never a paraId".

### 4.4 The honest name for the architecture: a three-way merge

The job — a lossy view-model editing a document it cannot represent, without destroying what it cannot see —
**is a three-way merge**, and every failed release avoided saying so. R4 put the merge on the **client** (ops
against a drifting view; anchors stale by construction → the 422 treadmill). R6 **deleted the base side** of
the merge (render from model only → loss by construction).

**Server-side three-way merge, with the projection as the single coordinate system:**
- **Base** = retained baseline bytes → projected by the *same* `ComposeDocxProjectionBuilder` **at save time**
  (fresh, so staleness is impossible — this **structurally kills** the R4 anchor bug class rather than
  mitigating it).
- **Theirs** = the posted model. The client already builds exactly this: `buildImportedContentModel` merges
  editor state over the retained loaded model, and **untouched blocks pass through verbatim by object
  identity** ([docxBridge.ts:344](../../src/client/shared/Spaarke.Compose.Components/src/utils/docxBridge.ts#L344)).
- **Mine** = the baseline XML itself.

Merge rule, per block:
- **Identical** → **clone the original `w:p` subtree whole.** Not a property splice — a clone. This restores
  everything the projection folded away (tabs→space, soft breaks, footnote refs, field results, `w:sym`) with
  **zero property logic**, and incidentally fixes interior section breaks.
- **Edited** → render from model with **property inheritance** from the base block (pPr clone +
  dominant-rPr), because run boundaries genuinely drift under editor rebuild + redline diffing.
- **Structural** (insert/delete/split/merge) → falls out of the alignment naturally.
- **Any per-block failure** → thin render + warning. **The save can never hard-fail on content.**

Stated plainly: *a renderer that copies what didn't change is a patch engine with an unfalsifiable anchor.*
`RenderIntoCarrier` and `ComposeShadowPatchEngine` converge into one thing, and ADR-049 D5 ("exactly one body
author") becomes satisfiable in spirit for the first time — the 3,000-line R4 engine can **retire** rather than
lurk.

---

## 5. Track A — the write model

**Hypothesis to prove at the Phase-0 gate** (the first draft's candidate B, corrected in the four ways the
review identified):

> **Stamp baseline → re-project baseline → per-block compare → clone untouched blocks whole → property-inherit
> edited blocks → thin-render-with-warning as the per-block floor.**

Four corrections that make it sound (the first draft would have failed the gate on most real documents):

1. **Stamp the baseline first.** For an uploaded doc whose paragraphs carry no `w14:paraId`, ids are minted
   client/projection-side and the **physical baseline has none** — a paraId-keyed lookup misses on *every*
   paragraph, silently reproducing thin render for exactly the documents R8 exists for. The fix is in-tree and
   the first draft missed it: [`ComposeBaselineParaIdStamper`](../../src/server/api/Sprk.Bff.Api/Services/Compose/ComposeBaselineParaIdStamper.cs)
   (text-verified, fill-gaps-only, fail-open) runs **only on the op-log path** today ([ComposeService.cs:1280](../../src/server/api/Sprk.Bff.Api/Services/Compose/ComposeService.cs#L1280)).
2. **The equality oracle is server-side re-projection, not text comparison and never client trust.** Comparing
   model text to raw baseline text fails on every paragraph containing a tab, break or field (the projection
   folds them). Comparing the posted block against **our own fresh projection of the same baseline** is exact,
   deterministic and unfakeable. *This is the difference between sound and unsound.*
3. **Clone whole blocks; don't splice properties.** Per-run `rPr` splice with "run-count mismatch → fallback"
   is the wrong unit for untouched content and unreliable for edited content.
4. **Give tables and atoms an identity.** `ComposeBlock.Table` carries no id; atoms are **absent from
   `ComposeBlockKind` entirely**. Promote the atom to a model block kind carrying `AtomId` + payload; a table's
   first descendant cell paraId is a sufficient key (cell paragraphs do carry ids).

**Known hazards to design against** (all verified): duplicate paraIds need consume-in-document-order matching
plus a dup-detected→fallback rule · cloned paragraphs bring their physical `commentRangeStart/End`, so
model-side anchors must be suppressed for cloned blocks and cross-boundary ranges validated · revision-id
seeding currently scans only for model-carried revisions and must not collide with cloned `w:ins`/`w:del` · the
baseline is a best-effort version re-fetch after a page refresh, **null for PDF-sourced docs** and 404 when
version history pruned it — that fallback tier must be stated. **Performance is a non-issue**: the carrier is
already fully buffered and re-opened several times per save.

### 5.1 Two document classes — the fidelity contract differs (owner ruling, 2026-08-19)

Conflating these is a design error; the codebase **already carries the discriminator**
(`ComposeOrigin.Authored` / `Imported`, persisted on `sprk_composeorigin`, resolved server-side from request
shape and hardened in R7 so a routing slip cannot mis-stamp an imported doc as authored).

| | **Imported `.docx`** | **PDF-import / born-in-editor** |
|---|---|---|
| Original OOXML exists? | **Yes** — authored by someone else | **No** |
| Save contract | **Preserve** what the user did not touch (§4.4 merge) | **Render from the model** — nothing to lose |
| "The file is ours now" | **False.** This assumption *is* the R6 defect | **True.** It is genuinely a new document |
| Degradation warnings | Report real loss against the original | **Must not** report loss — there is no original to lose against |

**The two are not in tension — they are different moments of one lifecycle.** A PDF synthesizes to a `.docx`
which lands in SPE, and **that synthesized file becomes the original for every subsequent save**. So a
PDF-sourced document lacks a baseline only for its *first* save, when there is nothing to preserve; from save
two onward it clones like any other document.

**Track A item that falls out**: the load-time version lookup is best-effort **null for PDF-sourced docs**
([ComposeService.cs:700-719](../../src/server/api/Sprk.Bff.Api/Services/Compose/ComposeService.cs#L700)) — the synthesized file's
version coordinates must be tracked or the clone path cannot resolve its baseline after a page refresh.

### 5.2 Capability gate = read-only **+ "Edit a copy"** (owner ruling, 2026-08-19 — supersedes plain read-only)

A document carrying constructs we cannot carry even opaquely **opens read-only with an explicit reason**, and
the user may then **choose to edit it — as a NEW document** (a Save-As fork). Never edit-and-hope, and never a
hard wall.

Why this is better than the plain Word-for-the-web gate: **the original is never written to, so it cannot be
damaged** — the safety property a read-only gate exists to provide — while **the user is never blocked**, which
is the cost a read-only gate normally imposes. It resolves the tension rather than trading one side away.

Binding requirements:
- The read-only banner states **what** we cannot carry, in plain language, and offers **"Edit a copy"**.
- The fork is **honest before it is created**: the user is told which constructs the copy will not include,
  and confirms.
- The fork is a genuine new document (new SPE item, uniquified filename — reuse R7's Save-As identity fix), so
  the original is untouched and still openable in Word with full fidelity.
- The forked copy is stamped **`ComposeOrigin.Authored`** (§5.1): it is a new document of ours from that point,
  so later saves must **not** report loss against an original it no longer claims to preserve.
- The gate's trigger list is **tight and owner-reviewed at Phase 0** — a false positive blocks a document we
  could have handled, which is the main risk this feature carries.

**Also in Track A**: **`If-Match` on the PUT**
— the plumbing already exists and is deliberately bypassed with `ifMatch: null`, leaving a multi-second TOCTOU
window between the metadata GET and the write. Enforce concurrency at the storage boundary, not in an
application check.

**Named and rejected**: embedding Word via WOPI — the only path to true 100% fidelity, excluded by ADR-049 D4,
and it forfeits the AI-native editing surface that is the product. **No library solves this**: Aspose,
Syncfusion DocIO, GemBox and Spire are per-developer licensed (NFR-03 bars them) and would leave us
re-rendering from someone else's model anyway. One useful permissive find: **Clippit** (MIT, actively
maintained) — the living successor to the archived Open-XML-PowerTools, carrying `WmlComparer`.

---

## 6. The gate — both invariants, permanently

R6's harness enumerates the corpus dynamically and classifies PASS/WARN/FAIL, but **explicitly does not assert
preservation**. R8 upgrades its contract, and the upgrade is what stops a ninth attempt:

1. **Preservation** — after a save editing exactly one paragraph, every *other* block is XML-equivalent to its
   original. **100% on the near tier** (character formatting, paragraph properties, indentation, tabs, footnote
   refs, fields); ≥95% overall.
2. **Outcome honesty** — every corpus save terminates in a **defined outcome** (§3.1), never an undefined
   content-refusal, and the response reports **exactly what persisted**. Without this, the pendulum's third
   failure mode — lying about the save — stays untested.

**Two comparison levels** (the part people get wrong): a *lenient* pass ignoring `paraId`/`textId` detects
content loss; a *strict* pass that does **not** ignore them detects identity drift. Ignoring identity
attributes at both levels hides exactly the R4 bug class. Normalize `w:rsid*`, `w:proofErr`, bookmark ids,
`numId`/`abstractNumId` remapping, attribute order, namespace-prefix choice.

**The only true gate is "Word opens it without the repair dialog."** `OpenXmlValidator` passing is necessary
but not sufficient — schema-valid-but-Word-repairs is the dominant real-world failure. Approximate in CI with
headless LibreOffice plus a periodic Word Online smoke test.

**Corpus**: cleared for use as-is (owner, 2026-08-19); **harder cases still to be evaluated**. Phase 0 adds the
three synthetic fixtures that are cheap to construct and broke R4 — `mc:AlternateContent` duplicate paraIds,
interior text boxes, multi-part paraId collisions — plus worst-offender documents for character formatting,
court-filing spacing, footnotes, `REF` cross-references and content controls.

---

## 7. Owner decisions (2026-08-19) — firm

- **D1 — Track C stays in R8.** No new project for Compose work. Tolerant edit anchoring (UAT-24) is R8 scope,
  sequenced last (§9).
- **D2 — Durable file bytes → blob** (§8 Track B). Cosmos holds *documents, not bytes* (verified: seven
  containers, all JSON). The Storage account is **already provisioned** with containers, managed-identity RBAC
  and lifecycle machinery.
- **D3 — Corpus cleared for now**; harder cases to be evaluated as they surface.
- **D4 — Fidelity bar confirmed**, and the reason is reliability: documents that will not save are the
  headline blocker (→ Track S).
- **D5 — Concurrency = last-writer-wins with a warning.** This **supersedes the 412 refusal** shipped
  2026-08-18 and directly resolves S-2. Combined with `If-Match` at the storage boundary, the posture is:
  writes are not blocked, the user is told when they overwrote a newer version, and SPE version history remains
  the recovery path.
- **D6 — Remove the god classes** (§8 Track D) — decompose, not work around.

---

## 8. The remaining tracks

### Track B — Durable session files (P1, parallel)

**Problem**: the manifest lives in Cosmos for 90 days, the searchable chunks die with the **24h Redis TTL**, and
the **raw bytes exist nowhere**. A conversation survives; the content that makes its files usable does not.
**Nuance the seed note missed**: a *filed* session sets `Ttl = -1` and is retained **indefinitely** — so the
requirement is "files live as long as their session lives," not a fixed 90 days.

**Design**: durable byte copy at upload time + **lazy re-index** into `spaarke-session-files` on recall if the
chunks were evicted. `SessionFilesCleanupJob` continues to evict the **hot index only**, never the durable
copy. Availability becomes server-authoritative (replacing R7's client-side 24h heuristic). Session deletion and
GDPR erasure delete the bytes, mirroring `memory-items`.

**Cost is much lower than the first draft assumed** — verified: [storage-account.bicep](../../infrastructure/bicep/modules/storage-account.bicep)
already provisions containers (`temp-files`, `document-processing`, `test-documents`), grants the App Service
managed identity Storage Blob Data Contributor, disables shared-key access and carries lifecycle policy
machinery; it is wired into [customer.bicep:145](../../infrastructure/bicep/customer.bicep#L145) and both stacks.
`Azure.Storage.Blobs` 12.29.1 is already referenced. **No new Azure resource, no new NuGet** — a container, a
lifecycle rule, and a service. The only existing consumer is a stub (GitHub #231).

**Do not rebuild** R7's re-attach-on-reopen client layer; it is correct.

### Track C — AI edit placement (P1 — **owner: "MUST be completely addressed"**)

**The defect is not a weak matcher. It is that we match at all.**

`ProposedEdit` carries [`target_text`](../../src/server/api/Sprk.Bff.Api/Services/Compose/ComposeEditModels.cs#L76) + a
`match_mode`, and `ComposeEditValidator` locates the edit by [`FindAll(doc, edit.TargetText)`](../../src/server/api/Sprk.Bff.Api/Services/Compose/ComposeEditValidator.cs#L58)
— **a whole-document text search for the model's echoed prose**. The client mirror (`resolveTargetSpans`) is
strict: a 1:1 character fold plus a whitespace-collapsed pass. An LLM paraphrases by nature, so the search
misses and the edit dead-ends. That is UAT-06 / UAT-09 / UAT-21 / UAT-24 — one root cause, four symptoms.

**This contradicts the governing ADR.** ADR-049: *"AI returns JSON operations **referencing paraId**; every
returned anchor is validated before apply,"* and I-7: fuzzy content-match "**never as a placement mechanism**."
The implemented contract is the one the ADR rules out.

**Placement is deterministic in every case that matters. We are discarding known coordinates and recovering
them by prose matching.** Three cases, only one of which involves the model in location at all:

| Case | How the target is known | LLM's role in location |
|---|---|---|
| **1. Selection-driven** (dominant) | The client **already captures `from`/`to`/`selectionText` at request time** and forwards `selectionAnchorStart` ([ComposeAiToolbar.tsx:747-784](../../src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeAiToolbar.tsx#L747)) | **None.** The model returns replacement *text* only |
| **2. Reference-driven** ("clause 4.2") | [`CitationResolver`](../../src/server/api/Sprk.Bff.Api/Services/Compose/CitationResolver.cs) — pure/static, computed from the numbering engine (R4.5 F-4). **Built; currently has no consumer in the BFF Compose path** | **None.** Deterministic resolution |
| **3. Model-initiated review pass** | The model selects from an **enumerated closed set of paraIds we supply**, validated on return; an invalid id is rejected loudly | Closed-set selection, not inference |

**Primary fix — thread the anchor; delete the search.** Case 1: carry the captured `(paraId, span)` through the
request→response→apply round trip instead of re-deriving it from echoed prose. Case 2: wire `CitationResolver`.
Case 3: the model returns a paraId from the supplied set. §4.2's paraId caveat does **not** apply — that
concerns ids surviving in the *file* across Word round-trips; placement needs ids stable only within the **live
session**, and those are ours.

**Root-cause note**: `ProposedEdit(target_text, match_mode)` appears designed for case 3 and was then reused for
case 1, discarding the exact anchor to fit a contract built for the inexact case. `selectionAnchorStart` is the
original intent surviving as a vestige. *(Do not carry a stale live selection to apply time — that was the
UAT-21 hazard. Capture at request time and thread it.)*

**INVARIANT (owner, 2026-08-19): "wording differs slightly" must never occur in the AI edit flow.** We mint the
ids and hold the document, so a target is either **known exactly** or **known to be gone**. There is no third
state. Every outcome is exact:

| Situation | Deterministic outcome |
|---|---|
| Sub-paragraph edit | Model returns the rewritten paragraph; **diff locally** against the known original for redline granularity. No search |
| User edited while the model was thinking | paraId unchanged, ProseMirror **mapped** the positions. A *staleness* fact, not a location one → "this clause changed since the suggestion — apply anyway?" |
| Paragraph split / deleted | Id absent from the document → "the text this suggestion referred to no longer exists" |

**One edit-capture mechanism (new invariant).** An AI edit's anchor is captured at **invocation** and mapped
forward through subsequent transactions by the **same rebasing machinery the op-log already uses**
(`RebasedOperationLog` / `stepOperationInterceptor`). The proposal is external; its anchor never had to be. Two
edit-capture paths built two releases apart is the same "two mechanisms for one job" failure as R3's two
writers and R6's two write paths (§2.2) — see §10.

**Code to retire** (consumers verified, blast radius contained to Compose):
- Server: `ComposeEditValidator` + `IComposeEditValidator` + `FindAll`; the `ProposedEdit.TargetText` /
  `match_mode` fields. Consumers: `ComposeEndpoints`, `ComposeModule` (DI), `ComposeEditBatch`,
  `ComposeEditTransaction`.
- Client: `resolveTargetSpans`, `findTargetMatches`, `MATCH_FOLD`, `collapseWhitespaceIndex`, `buildCharIndex`.
- **KEEP — different job**: `ComposeTextFold` (consumed only by `ComposeBaselineParaIdStamper`, which Track A
  needs). **KEEP — legitimately fuzzy**: `AnnotationReanchorService` — the ADR-049-carved-out **return-from-Word
  re-anchor** case, where Word itself regenerates paraIds (Open-XML-SDK #925) so our ids genuinely do not map.
  Different surface, and its message must be honest ("this document was edited in Word — here is what
  re-attached"), never the vague copy.

**Acceptance**: the "couldn't be placed — wording differs slightly" dead-end is **eliminated**, not reduced —
an AI edit either places at its anchor, or surfaces a confirmable proposal. Never a dead end, never a silent
mis-placement. Promoted to P1 per owner direction 2026-08-19.

### Track D — Remove the Compose god classes (D6)

Five of the thirteen ratchet-frozen files are Compose, **~14,600 lines**:

| File | Lines |
|---|---|
| `ComposeService.cs` | 3,573 |
| `ComposeDocxProjectionBuilder.cs` | 3,085 |
| `ComposeShadowPatchEngine.cs` | 2,999 |
| `ComposeEndpoints.cs` | 2,651 |
| `ComposeDocumentRenderer.cs` | 2,304 |

This is not hygiene — it is **causally related to why R8 exists** (§2.4). A ~600-line `SaveAsync` inside a
3,573-line service is how a write model flips twice without anyone holding it in their head. Decompose per
concern; **delete each waiver entry** from `GodClassGuardTests.cs` as a file drops below 2,000 (that ratchets
the floor down permanently). `ComposeShadowPatchEngine` likely **retires entirely** under §4.4 rather than being
decomposed — confirm at the Phase-0 gate before deleting.

**Freeze rule**: no new feature lands in the save path until the write-model gate is green.

---

## 9. Phasing

| Phase | Content | Gate |
|---|---|---|
| **S** | Track S — S-1..S-9 + the save-outcome contract + telemetry. **Starts immediately; no architecture dependency.** | Users can save; every failure is honest, recoverable, and measured |
| **0** | Build the preservation + outcome oracle **first** (it measures today's loss as the control); extend the corpus; prototype the §5 merge end-to-end | §6 invariants met → proceed. Missed → re-open the model decision with the owner |
| **1** | ADR-049 third amendment; author `spec.md` from the **proven** model | Amendment merged with or before dependent code |
| **2** | Track A build — baseline stamping, re-projection oracle, block clone, property inheritance, atom payload carry, capability gate, `If-Match` | Gate green in CI |
| **3** | Track D decomposition (interleaved with 2 where it unblocks; waivers deleted as files drop) | ArchTests green, waivers removed |
| **4** | Track B — durable bytes, lazy re-index, retention binding, erasure, authoritative availability | A day-60 session recalls from its files |
| **C** | Track C — **anchor-not-search** (AI returns paraId+span), then tolerant matching as a bounded confirmable fallback. **Runs parallel to A**, not after it: it is a separate contract (AI edit envelope + client resolver), owner-flagged P1, and does not wait on the write-model gate | The dead-end is eliminated; no mis-placement regression vs UAT-21 |
| **6** | Wrap-up — anti-clobber deploy (BFF + `sprk_spaarkeai` together), `/test-diet`, docs | — |

---

## 10. ADR tension (root §6.5)

🔔 **ADR Conflict — Path B (amendment), to be filed at the Phase-0 gate**

- **In question**: ADR-049's **R6 Path-B amendment** ("render-on-save supersedes surgical byte-patch on the
  save path") and, beneath it, R4's **I-4** (untouched subtrees byte-identical), which R6 set aside.
- **Conflict**: R8 restores what I-4 protected without restoring the mechanism R6 removed. The save still
  renders from the model (R6 holds), *and* untouched content is preserved (I-4's intent holds). Both amendments
  are partly right; neither is correct as written.
- **Resolution**: a third amendment recording (a) render-on-save is **fidelity-preserving by base
  re-projection and block copy-through**; (b) **two standing MUSTs** that no future amendment may trade away
  singly — *every save terminates in a defined outcome, never an undefined content-refusal* and *untouched
  blocks are preserved*; (c) **the projection is the only coordinate system** — nothing else may independently
  resolve document positions (the write-side analog of R4.5's "one reader"); (d) **paraId is a hint, not a
  primary key** *in the file* (§4.2, spec-cited) — but it **is** authoritative *within a live session*, because
  we mint it; (e) the concurrency posture is **last-writer-wins with warning** (D5), superseding the 412
  refusal, enforced by `If-Match` at the storage boundary; (f) **one edit-capture mechanism** — every edit,
  whether it originates from a keystroke or from the model, captures its anchor at invocation and is rebased by
  the same machinery; (g) **deterministic information available at capture time MUST be carried, not
  re-derived.**

> **(g) is the general rule under three of this project's four root causes** — it would have caught R6's thin
> model (discarded the original, then reconstructed it from a lossy view), the AI edit contract (discarded an
> exact selection, then recovered it by prose matching), and the demand for a fuzzy matcher (only needed once
> the anchor is gone). State it once, in the ADR, so it stops being rediscovered per surface.
- **Rejected**: Path A (project-scoped exception) — this is a durable change every future Compose project
  inherits. Path C (comply) — complying with the R6 amendment as written means shipping the silent loss.

**Comply (mention only)**: ADR-049 D5 (one body author — §4.4 satisfies it properly) · I-7 (no write-path
text-search — Track C's tolerant matching is a *client-side read/placement* resolver producing an exact op) ·
ADR-013/ADR-007 purity · ADR-039/040 (AI engine frozen) · ADR-014/015 (tenant isolation on Track B's store).

---

## 11. Success criteria (closed set)

1. **Save works.** Every failure mode in §3 is closed: client routes on `ApiError.status`; no unrecoverable
   refusal loop; a failed save always leaves Save live and never reports success; in-flight guard + timeout;
   documents over 4 MB save; every terminal outcome is on the wire and instrumented.
2. **No lying.** A save reports exactly what persisted — no HTTP 200 with nothing written, no "Saved ✓" on a
   failed write, no silent skip.
3. **Preservation**: a save editing one paragraph leaves every other block XML-equivalent to the original —
   100% near tier, ≥95% overall, asserted in CI at **two** comparison levels.
4. **Zero hard-fails** across the corpus, now an *asserted invariant* rather than an emergent property.
5. **Zero silent loss** — every residual degradation warns with friendly copy (UAT-15/18 closed); the residual
   list is **published** and matches what the gate enforces.
6. Footnotes, fields, content controls and complex objects round-trip as whole constructs, or appear on the
   owner-accepted residual list; a document we cannot carry opens **read-only with a stated reason**.
7. Concurrency is **last-writer-wins with a user-visible warning**, enforced by `If-Match`.
8. A session reopened at any point in its retention (incl. indefinite for filed sessions) recalls from its
   uploaded files; availability is server-authoritative; deletion deletes the bytes.
9. **The "couldn't be placed — wording differs slightly" dead-end is eliminated.** An AI edit places at its
   returned anchor, or surfaces a confirmable proposal — never a dead end, never a silent mis-placement (no
   regression vs UAT-21), proven by test.
10. **All five Compose waivers deleted** from `GodClassGuardTests.cs`.
11. Publish size ≤60 MB (report absolute + delta vs 44.96 MB); no new HIGH CVE; no new NuGet on Track A;
    placement + component justifications recorded; `/conflict-check` clean.

---

## 12. Governance seeds

```xml
<hot-path-declaration>
  <bff>Y</bff>                   <!-- Services/Compose write model + save path; Services/Ai/Sessions + Chat for durable files -->
  <spaarkeai>Y</spaarkeai>       <!-- Compose save error handling, outcome banners, capability-gate read-only state -->
  <ci-workflows>Y</ci-workflows> <!-- the fidelity gate's contract changes (preservation + outcome honesty) -->
  <skill-directives>N</skill-directives>
  <root-claude-md>Y</root-claude-md> <!-- §17 Compose pointer + god-class ratchet row updated -->
</hot-path-declaration>
```

**Placement Justification** — Track A/S/D stay in `Services/Compose/` + `Api/ComposeEndpoints.cs`, extending
and decomposing existing components; no new subsystem, no new package. Track B's durable store is the one new
BFF surface and must answer the §11 three questions against `SpeFileStore` and the stubbed blob path.

**Component Justification — reuse**: `ComposeBaselineParaIdStamper` (Track A needs it) · the `If-Match`
overload in `UploadSessionManager` · `ComposeFormatChange`'s opaque-carry contract · `ComposeBlockAtom` +
`opaqueAtomNode.ts` · `ResolveSaveBaselineAsync` · `ComposeFidelityGateHarnessTests` + `ComposeCorpusFixtureLocator`
· R7's `SAVE_DEGRADATION_COPY`, banner stack and `ApiError` contract · `SessionRestoreService` + R7's re-attach
layer · `AnnotationReanchorService` (reference for Track C).
**Do not create**: a second body author, a parallel content model, a second fidelity harness, a new degradation
copy layer, a new session-restore surface.

---

## 13. Next steps

1. **Start Track S now** — it needs no decision from anyone and it is what the owner is feeling.
2. Owner confirms the §7 decision set as recorded.
3. `/design-to-spec` → `spec.md`, carrying §10's tension and §12's seeds.
4. `/project-pipeline` → `plan.md` + `tasks/`; worktree `spaarke-wt-spaarkeai-compose-r8`.
5. **Phase 0 is a real gate.** The corpus, not the argument, picks the architecture — that is the only thing
   that stops an R9.
