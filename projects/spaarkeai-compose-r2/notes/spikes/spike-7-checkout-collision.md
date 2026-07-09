# Spike 7 — SPE checkout vs Word-for-Web open collision UX

> **Task**: 007 · **Phase**: 0 Spikes · **Date**: 2026-07-08 · **Model**: sonnet @ high
> **Method**: static code trace (grounded, file:line evidence) over the existing R1 checkout /
> heartbeat / save plumbing + reconciliation with `research/openxml-docx-research.md` for the
> Word-for-Web / Graph runtime behavior that cannot be observed headlessly.
> **Deliverable**: this note (collision matrix + recommended conflict UX). No production code.
> **SPE-facade compliance (acceptance #3)**: every SPE touch traced below goes through
> `SpeFileStore` / `ISpeFileOperations` (`ReplaceFileContentAsUserAsync`,
> `GetFileMetadataAsUserAsync`); the checkout state lives in Dataverse via `DocumentCheckoutService`.
> **No raw `Microsoft.Graph` types appear in `Services/Compose/`** — confirmed: `ComposeService.cs`
> injects `ISpeFileOperations` only (`ComposeService.cs:42`), and the recommendations below add no
> new Graph dependency there. ADR-007 holds.

---

## 1. Decision (the one thing this spike unlocks)

**The concurrency-UX model for FR-27 / FR-28 must be built on one load-bearing fact that the design
premise under-states: Compose "check-out" is a _Dataverse advisory lock_, not an SPE / Word-for-Web
_editing_ lock. The two lock systems do not know about each other.** There is exactly one place where
the platform enforces a real, mandatory lock, and it is **not** at checkout time — it is at
**write-back** (`PUT …/content`), where Graph returns **HTTP 423 Locked** if Word for Web holds a
live co-authoring session on that drive-item. Therefore:

- Checkout **cannot** be relied on to block a Word-for-Web open, and a Word-for-Web open **cannot** be
  detected by the checkout path. The collision is real but **asymmetric** — it only becomes
  _observable_ (as an error) on the Compose→SPE write, or (silently) as a lost update.
- The Compose conflict UX must be driven by **write-back outcomes (423 / 412), plus the return-from-Word
  version-delta signal**, not by checkout state. The checkout lock stays useful only as a
  **same-app, cross-Compose-user** advisory ("Jane has this open in Compose"), which is what
  `DocumentCheckoutService` already delivers.

**Recommended model (summary; matrix in §4, per-case UX in §5):**

1. **Do NOT try to make Compose checkout mutually exclusive with Word for Web.** It can't be, cheaply —
   there is no drive-item-level SPE checkout in this codebase, and even Graph's document check-out only
   makes Word open read-only, degrading the user's own Word experience. Treat the two as coexisting.
2. **Make the write-back path lock-aware.** The 423 (Word has it open) and 412 (etag moved under us)
   responses are the *only* trustworthy conflict signals. Today the code handles **neither** (§3, §6).
3. **Surface conflict at two moments**: (a) an inline **"Open in Word" banner state** driven by the
   existing checkout/heartbeat status when the doc is known-open elsewhere, and (b) a **push/save gate
   outcome** ("Couldn't save — this document is open in Word. Close it there, or push your changes as
   tracked edits when you return.") driven by the 423/412 response.

**Three material corrections to the design/POML premise (the high-value findings — §6):**

- **C-1** The R1 "SPE check-out plumbing" is a Dataverse lock; it issues **no SPE lock call**. The
  Compose checkout/checkin endpoints are still **501 stubs** (`ComposeEndpoints.cs:324,339`). "Compose
  holds a checkout while the user opens in Word" does **not** produce any platform-level mutual
  exclusion today.
- **C-2** `ReplaceFileContentAsUserAsync` is a **blind PUT with no `If-Match`** and **does not catch
  423**. FR-24's spec text ("saves to SPE via `ReplaceFileContentAsUserAsync` with `If-Match`",
  `spec.md:103`) describes a capability the method **does not yet have**. This is the primary R2 gap.
- **C-3** The only `Lock*`/`Unlock*` methods in the SPE layer are **container-level**
  (`SpeAdminGraphService.LockContainerAsync:1231`), unrelated to per-document co-authoring. Do not
  mistake them for a file lock.

**Runtime caveat (honest scope):** the exact Word-for-Web user experience (does the user see
read-only? a merge banner? silent autosave win?) and the precise Graph status codes (423 vs 409 vs
locked-with-`retry-after`) **cannot be observed in a headless code session**. Those rows in §4 are
marked **runtime-deferred** and carry the verification recipe in §7. The *code paths* in this worktree
are statically confirmed.

---

## 2. What "checkout" actually is (evidence)

| Fact | Status | Evidence (file:line) |
|------|--------|----------------------|
| Checkout writes only Dataverse fields (`sprk_checkedoutdate`, `sprk_CheckedOutBy`, `versionnumber`, current-version link) | ✅ code-confirmed | `DocumentCheckoutService.cs:186-194` → `UpdateDocumentCheckoutStatusAsync:956-996` |
| Checkout issues **no SPE lock** — it only fetches a *preview/edit URL* (best-effort, non-fatal) | ✅ code-confirmed | `CheckoutAsync:196-206`; `GetEditUrlAsync:1109-1138` calls `_speFileStore.GetPreviewUrlAsync` and string-swaps `embed.aspx`→`embedview`. No lock verb. |
| Cross-Compose-user checkout returns **409 `document_locked`** with holder name/date | ✅ code-confirmed | `CheckoutAsync:154-171`; `ConflictCheckoutResult` → `StatusCode 409` (`:1280`) |
| Same-user re-checkout is idempotent | ✅ code-confirmed | `CheckoutAsync:120-152` |
| Heartbeat is a Dataverse `PATCH sprk_lastheartbeatutc`, same-user-guarded | ✅ code-confirmed | `RefreshHeartbeatAsync:453-520` (guard `:487-494`) |
| Stale sweeper releases locks with heartbeat older than 15 min (≤17 min orphan ceiling), under MI, bypassing same-user check | ✅ code-confirmed | `StaleCheckoutSweeperHostedService.cs:64-70,135-210`; `ReleaseCheckoutSystemAsync:656-724` |
| Compose's own checkout/checkin endpoints are **501 stubs** (R1 routes callers to `/api/documents/{id}/checkout`) | ✅ code-confirmed | `ComposeEndpoints.cs:67-81,324-352` |
| The only heartbeat endpoint that *is* wired is `POST /api/compose/document/{id}/heartbeat` → 204/404 | ✅ code-confirmed | `ComposeEndpoints.cs:83-91,354-406` |

**Interpretation.** The lock is **advisory and same-application**. It protects Compose users from each
other (two Compose editors on one matter), and it drives the stale-sweep hygiene. It does **not** and
**cannot** gate a Word-for-Web WOPI session, because Word opens the drive-item directly against SPE and
never reads `sprk_document`.

---

## 3. Where the real lock lives — write-back (evidence)

| Fact | Status | Evidence (file:line) |
|------|--------|----------------------|
| Compose Save → SPE write is `ISpeFileOperations.ReplaceFileContentAsUserAsync` | ✅ code-confirmed | `ComposeService.SaveAsync:167`; facade `SpeFileStore.cs:191-197` → `UploadSessionManager.cs:317` |
| The write is a **plain `PUT …/content`** — **no `If-Match`**, no ETag param on the signature | ✅ code-confirmed | `UploadSessionManager.cs:317-332` (`.Content.PutAsync(content…)`); `ISpeFileOperations.cs:54` signature has no etag |
| The write handler catches **404 / 403 / 429** only — **not 423, not 412** | ✅ code-confirmed | `UploadSessionManager.cs:356-369` (404→null, 403→`UnauthorizedAccessException`, 429→retry). No `423`/`412` filter → falls to generic catch. |
| Generic catch surfaces any other Graph error as an opaque failure → Compose Save endpoint maps to **500** | ✅ code-confirmed | `UploadSessionManager.cs` generic `catch`; `ComposeEndpoints.Save:262-269` → `Status500InternalServerError` |
| Word for Web persists comments/track-changes **inside the DOCX bytes** (no parallel SPE metadata) — so a Compose blind-PUT overwrites Word's in-flight content wholesale | ⚠️ runtime-deferred (research, confidence med-high) | `research/openxml-docx-research.md:83,144` |
| `PUT /content` against a drive-item with an active Word editing session returns **HTTP 423 Locked**; Graph exposes **no pre-check** API for lock state | ⚠️ runtime-deferred (research, Microsoft Q&A) | `research/openxml-docx-research.md:101-102,116-117` |

**Interpretation.** Today a Compose Save while Word for Web has the file open will, at best, throw an
unhandled 423 that the user sees as a generic 500 ("Save failed"), and at worst — if the timing misses
Word's lock and hits between autosaves — **silently overwrite** the user's Word edits because there is
no `If-Match` guard (last-write-wins). Both are unacceptable for FR-28's "deterministic path."

---

## 4. Collision matrix

Legend: **CC** = code-confirmed in this worktree · **RD** = runtime-deferred (Word-for-Web / Graph
behavior, verify per §7).

| # | Scenario | What Compose does today | Observed platform behavior | Conf |
|---|----------|-------------------------|----------------------------|------|
| **A** | **Compose checkout → then user opens in Word for Web** | Checkout sets Dataverse fields only; returns an `embedview` edit URL | Word open is **not blocked** — Word never reads `sprk_document`. User co-authors freely. Compose's lock is invisible to Word. | A: CC (checkout path). Word-not-blocked: RD |
| **B** | **User has Word for Web open → then Compose checkout** | `CheckoutAsync` checks only Dataverse `IsCheckedOut`; sees no Compose lock → **succeeds** | Checkout succeeds despite the live Word session. Compose now believes it "owns" a doc Word is actively editing. No conflict raised. | B: CC (checkout ignores Word). |
| **C** | **Compose write-back (Save / FR-24 push) while Word holds the file open** | Blind `PUT /content`, no `If-Match`; catches 404/403/429 only | Graph returns **423 Locked** → falls through to generic catch → Compose surfaces **500 "Save failed"** (opaque). No retry, no conflict UX. | Write path + missing-423-catch: CC. 423 itself: RD |
| **C′** | **Compose write-back races Word autosave (no live lock at the instant of PUT)** | Blind PUT, no `If-Match` | **Last-write-wins**: Compose bytes overwrite Word's most recent autosave (or vice-versa). Silent lost update; no 412 because no precondition is sent. | CC (no If-Match) + RD (autosave timing) |
| **D** | **Heartbeat expiry / stale-sweep fires during a long Word session** | Sweeper releases the Compose lock after ≤17 min of no heartbeat (browser/tab closed, user moved to Word) | Compose lock silently cleared; open FileVersion marked Discarded. Word session **unaffected** (independent). On return, Compose shows no lock — correct, but any pending Compose annotations tied to that checkout must survive as ledger state, not lock state. | Sweep behavior: CC (`:656-724`). Word-unaffected: RD |
| **E** | **Return from Word: new SPE version exists, Compose reloads + re-anchors (FR-27)** | Not yet built (Phase 5). Load path reads metadata ETag (`ComposeService.LoadAsync:129-139`) | Requires webhook/delta detection + re-anchor bands (Spike 6's job). Conflict = ambiguous anchors, handled by confidence banner — a *content* reconciliation, distinct from the *lock* conflict in C/C′. | Load/ETag read: CC. Detection + bands: out of scope (Spike 6 / FR-27) |

---

## 5. Recommended Compose conflict UX (per case: banner · blocked action · recovery)

Design constraints honored: Fluent v9 + dark mode (ADR-021); **no bespoke confirmation banner where a
Policy-v2 gate already owns the modality** (design §2.4) — the push/save confirmation stays the gate's
one dialog; the conflict states below are **status/outcome surfaces**, not new confirm prompts; the
**Context pane stays audit-only** (never a decision surface).

### Case A & B — Word session coexists with Compose (advisory, not an error)
- **Banner (Workspace, info severity):** *"This document is also open in Word for Web. Your Compose
  edits are tracked separately and will be pushed as tracked changes when you save."* Drive it from
  the doc's known-open state (see §6 gap — needs a Word-open signal; until then, show it optimistically
  whenever a Word edit URL has been handed out this session).
- **Blocked action:** none. Do **not** block Compose editing — Compose edits are ledger-first
  (ADR-040) and reconcile on push. Blocking would break the core value prop.
- **Recovery:** on push/save, route through Case C handling.

### Case C — write-back rejected because Word holds the lock (**the primary UX**)
- **Banner / OutcomeCard (Assistant, warning):** the push/save **job-aware OutcomeCard** reports the
  step failure: *"Couldn't save to Word — the document is open in Word for Web right now. Close it in
  Word, then Save again. Your Compose changes are safe and still pending."*
- **Blocked action:** the Save/Push step fails **cleanly** (not a 500). Compose pending annotations are
  **retained** (they live in the ledger, not the file), so nothing is lost.
- **Recovery affordances:** (1) **"Retry save"** (after the user closes Word); (2) **"Keep editing in
  Compose"** (dismiss, stay pending); (3) optional **"Push as tracked changes on return"** — defer the
  write so FR-27's return-from-Word flow re-anchors and writes once the Word session releases.

### Case C′ — etag race / silent lost-update (**must be made impossible, not just messaged**)
- **Mechanism first, UX second:** add `If-Match: <etag-from-load>` to the write so a moved file returns
  **412** instead of clobbering. Only then can UX exist.
- **Banner (Assistant, warning):** *"This document changed in Word since you loaded it. Reload the
  latest version to keep both sets of changes — nothing was overwritten."*
- **Blocked action:** the un-guarded overwrite is **blocked by the 412 precondition**.
- **Recovery:** **"Reload & re-anchor"** → hands off to the FR-27 return-from-Word re-anchor path
  (reload latest bytes, re-anchor pending Compose annotations, then re-offer push).

### Case D — stale-sweep released the lock mid-Word-session
- **Banner:** none needed at sweep time (silent is correct). On the user's **return to Compose**, if
  pending annotations exist without an active checkout, show the Case A banner and let FR-27 reconcile.
- **Blocked action:** none.
- **Recovery:** pending annotations survive as ledger entries; re-acquire checkout lazily on next
  Compose edit. **Requirement:** pending-annotation durability must **not** be coupled to lock lifetime
  (see §6 gap G-4).

### Case E — return-from-Word content conflict
- Owned by **FR-27 + Spike 6** (confidence bands ≥0.85 / 0.6–0.85 / <0.6, banner "N re-anchored, M
  need review", `spec.md:106`). Listed here only to draw the boundary: E is a **content**-merge
  conflict; C/C′ are **lock/write** conflicts. They share the "Reload & re-anchor" recovery affordance
  but are triggered by different signals (version-delta vs 423/412).

---

## 6. Gaps in the current checkout/heartbeat/write implementation R2 must close

| ID | Gap | Evidence | Impact if unclosed | Suggested owner task |
|----|-----|----------|--------------------|----------------------|
| **G-1** | `ReplaceFileContentAsUserAsync` sends **no `If-Match`** → silent lost-update vs Word autosave (Case C′) | `UploadSessionManager.cs:317-332`; `ISpeFileOperations.cs:54` | Word edits silently overwritten; violates FR-28 "deterministic" | FR-24/FR-28 (task 053/055): add optional `ifMatch` param to the facade + pass load-time ETag on Save/push |
| **G-2** | Write handler **does not catch 423 Locked** → 423 surfaces as opaque 500 (Case C) | `UploadSessionManager.cs:356-369`; `ComposeEndpoints.Save:262-269` | User sees "Save failed" with no actionable guidance; can't tell "close Word" from a real server fault | Same task: add `catch (ServiceException ex) when (ex.ResponseStatusCode == 423)` → typed `DocumentLockedByWordException` → 409/423 problem response consumed by the OutcomeCard |
| **G-3** | **No Word-open signal** available to Compose UI — checkout state can't reflect a WOPI session | §2 (checkout reads only Dataverse) | Case A/B banner can only be optimistic; cannot proactively warn before a failed push | FR-27: derive "open elsewhere" from the return-from-Word webhook/delta signal (a fresh `lastModifiedBy`/version implies external activity), not from checkout |
| **G-4** | Pending-annotation durability is not shown to be **decoupled from lock lifetime** (stale-sweep discards the FileVersion at `:682-687`) | `ReleaseCheckoutSystemAsync:677-694` | If pending Compose edits were ever tied to the open FileVersion row, a stale-sweep during a Word session would drop them | Verify (Phase 1/5) that ledger `SessionOutput`s (ADR-040) are the sole store of pending edits; the discarded FileVersion must carry no pending-edit state |
| **G-5** | Compose checkout/checkin endpoints are **501 stubs**; the only lock write is via the R1 `/api/documents/{id}/checkout` path | `ComposeEndpoints.cs:324-352` | If R2 wants Compose to *hold* an advisory lock during a drafting session, that wiring doesn't exist yet | Phase 5: either wire the stubs to `DocumentCheckoutService` or document that Compose relies on the R1 endpoint + heartbeat only |
| **G-6** | 429 on write is retried inside the facade but there is **no bounded backoff surfaced to the gate** for a *sustained* Word lock (423 could recur across retries) | `UploadSessionManager.cs:366-369` | A doc kept open in Word for a long time yields repeated failed saves with no "give up / defer" affordance | FR-28: OutcomeCard offers "Push on return" defer path (Case C recovery #3) after N failed attempts |

---

## 7. Runtime-verification recipe (run on `spaarkedev1` when Phase 5 begins)

The §4 **RD** rows require a live SPE container + a Word-for-Web session; confirm each before building
the FR-27/FR-28 UX on the assumed status codes.

1. **423 on locked write (Case C):** upload a DOCX to a test SPE container; open it in **Word for Web**
   and start typing (establish a live co-authoring session). From a REST client, `PUT
   /drives/{driveId}/items/{itemId}/content` with new bytes (mimic `ReplaceFileContentAsUserAsync`).
   **Record the exact status** (expect **423**; note any `Retry-After` header and the ODataError code
   string). Repeat with the Word tab open-but-idle to see if the lock releases between autosaves.
2. **412 on etag race (Case C′):** `GET` the item's ETag; edit + autosave once in Word for Web (ETag
   moves); then `PUT` with `If-Match: <old-etag>`. **Confirm 412 Precondition Failed** (validates the
   G-1 fix design).
3. **Word-open UX (Case A/B):** with Compose "checked out" (via `/api/documents/{id}/checkout`), open
   the same item in Word for Web. **Record what the Word user sees** — read-only? full edit? any banner?
   (Confirms checkout is invisible to Word, i.e. no accidental mandatory lock.)
4. **Stale-sweep vs Word (Case D):** check out in Compose, stop heartbeating (close the Compose tab),
   keep Word for Web open >17 min. Confirm the sweeper clears the Compose lock (`:719` log line) and
   the Word session is **unaffected**.
5. Record all four in a follow-up note or in FR-27/FR-28 task notes; wire the 423/412 handlers (G-1,
   G-2) to the observed codes, not the assumed ones.

---

## 8. Acceptance criteria — disposition

| # | Criterion | Result |
|---|-----------|--------|
| 1 | Checkout-vs-Word-open collision matrix with observed platform behavior per case | ✅ **Met** (§4, 6 cases A/B/C/C′/D/E). Code-confirmed vs runtime-deferred marked per row; RD rows carry the §7 recipe. The decision the spike unlocks (design §13 concurrency UX) is stated in §1. |
| 2 | Each collision case has a recommended Compose conflict UX (banner + blocked action + recovery) | ✅ **Met** (§5), honoring the Policy-v2 one-gate + audit-only-Context constraints. |
| 3 | SPE interaction is via `SpeFileStore` facade (no raw `Microsoft.Graph` in `Services/Compose`); note states it | ✅ **Met** (header + §2/§3 evidence): `ComposeService` injects `ISpeFileOperations` only (`ComposeService.cs:42`); checkout state via `DocumentCheckoutService`. Recommendations add no Graph dependency to `Services/Compose`. |

**Design-assumption corrections recorded** (§1 C-1..C-3): checkout is a Dataverse advisory lock not an
SPE lock (C-1); the write path has neither `If-Match` nor a 423 handler despite FR-24's spec text (C-2);
the only SPE `Lock*` verbs are container-level (C-3). These change how FR-27's conflict banner and
FR-28's deterministic push/save must be built: **drive conflict UX from write-back outcomes (423/412) +
version-delta, not from checkout state.**
