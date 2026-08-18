# Spaarke Compose R7 — UAT Issue Tracking

> **Status**: R7 is **IN UAT — NOT closed.** The 090 wrap-up (deploy, docs, merge) is done, but the project
> stays open until every UAT issue below is **Fixed**, **Deferred (explicitly moved)**, or **Won't-Fix (with
> rationale)**. Code is on master; dev has BFF + `sprk_spaarkeai` deployed (pending redeploys noted per issue).
> **Owner**: Ralph Schroeder · **Last updated**: 2026-08-18
> **Rule**: no issue is dropped silently. Each row carries a root cause, a resolution, and a disposition.

---

## Status legend

| Status | Meaning |
|---|---|
| ✅ Fixed | Code fix committed + verified; may still need a deploy to reach the env (noted) |
| 🔧 In Progress | Being worked now |
| 🔎 Investigate | Root cause not yet confirmed |
| 📦 Deferred | Explicitly moved to a named follow-up (DEF/# link) |
| 🟡 Open | Triaged, awaiting disposition decision |

Disposition = **R7** (fix in this UAT round) · **Fidelity** (Compose render/anchor engine fast-follow) · **SpaarkeAi** (Assistant/workspace product) · **Platform** (notifications/auth).

---

## Summary table

| ID | Issue | Round | Severity | Root cause (short) | Status | Disposition |
|----|-------|-------|----------|--------------------|--------|-------------|
| UAT-01 | "no storage container configured" save error (BU HAS a container) | 1 | Critical | Container resolver read only `globalThis.Xrm`; code page is an iframe → Xrm on `parent`/`top` | ✅ Fixed (redeploy `sprk_spaarkeai`) | R7 |
| UAT-02 | Save 500: "not defined as keys" / "Found multiple records" | 1 | Critical | `sprk_graphitemid_uk` in `Failed` over 417 duplicate `sprk_document` rows | ✅ Fixed (dev) + hardening | R7 (+DEF-003) |
| UAT-03 | No name-on-save modal on first save of a new/uploaded file | 2 | Medium | FR-02 modal skipped when the file already carries a name — `saveNeedsName` gated on `isUntitledDraftName` | ✅ Fixed `cdb1dbcb4` (redeploy) | R7 |
| UAT-04 | No progress indicator on toolbar actions (save, memo, …) | 1+2 | Medium | R7 added save-*state* text but no click-time busy/spinner feedback | 🟡 Open (next) | R7 |
| UAT-05 | Header banner messages can't be dismissed/cleared | 1 | Low-Med | Save-error banner (+ checkout/pending) had no dismiss ✕ | ✅ Fixed `cdb1dbcb4` (redeploy) | R7 |
| UAT-06 | "suggested edit couldn't be placed — wording differs slightly" | 1+2 | Medium | AI redline target text didn't byte-match the doc → strict anchor placement failed (warn-don't-drop). Intermittent. | 🟡 Open | R7 (UX); engine=owner-decide |
| UAT-07 | "content simplified when saving" warnings — unhelpful + real fidelity loss | 2 | Med-High | (a) render-on-save fidelity wideners not implemented; (b) warnings were cryptic (raw codes) | 🔧 **07b Fixed** `cdb1dbcb4`; 07a open | R7 (07b done); 07a=DEF-002 (owner: expand R7) |
| UAT-08 | Create Summary Memo needs an Analysis; promote option missing; should auto-create | 1 | Med-High | Memo requires a session→Analysis link; "Promote to Analysis" not visible in History; design wants auto-create on analyze | 🟡 Open | R7 (Compose-triggered) |
| UAT-09 | Advisory comments couldn't be anchored (2 of 10) + op-log unrepresentable `commentAnchor` | 2 | Medium | Comment anchor strict-resolution failed for some; `commentAnchor` mark outside the op-log closed set | 🔎 Investigate | R7 (investigate) |
| UAT-10 | SignalR notifications 401 on negotiate (poll-fallback active) | 2 | Medium | Notifications hub auth returns 401 on negotiate | 🔎 Investigate | R7 (investigate) |

**Working / no issue (context)**: UAT-2 steps 1,2,3a,3e,4 (open new file, quick-scan analysis, save created the doc + SPE file + index, opened in Word web/desktop) — all functioned.

---

## Detail

### UAT-01 — "no storage container configured" on save (BU has a container) — ✅ Fixed
- **Rounds**: R1 item 6.
- **Root cause**: `ComposeDirectWidget.tsx` and `composeEditor.registration.ts` resolved the BU container via `(globalThis as any).Xrm` only. The SpaarkeAi/LegalWorkspace code page runs in an iframe where `Xrm` lives on `window.parent`/`window.top`; `globalThis.Xrm` is `undefined`, so `resolveUserBuDefaults` never ran → `containerId` stayed undefined → the create-on-save gate threw the false "no container" error even though the BU's `sprk_containerid` is set (verified in Dataverse). Distinct from UAT-02 (data/key).
- **Resolution**: use the iframe-safe `w?.Xrm ?? w?.parent?.Xrm ?? w?.top?.Xrm` fallback (the pattern every other SpaarkeAi Xrm consumer already uses). **Commit `68e1ffcc8`.** tsc-surface-gate: 0 surface-owned errors.
- **Remaining**: needs a **`sprk_spaarkeai` code-page redeploy** to reach dev. Verify by saving a create-on-save doc after redeploy.

### UAT-02 — Save 500 "not defined as keys" / "Found multiple records" — ✅ Fixed (dev) + hardening
- **Rounds**: R1 (2 variants).
- **Root cause**: FR-07(d) atomic upsert keys on `sprk_graphitemid_uk`, which was in `Failed` state because `spaarkedev1` had **105 duplicated graphitemids / 417 excess `sprk_document` rows** (mis-scoped D1 debt). A unique key can't build over duplicates.
- **Resolution**: (1) **dev data**: deleted 417 duplicate rows (kept newest per graphitemid) + reactivated the key → **Active**; (2) **code #1** graceful ProblemDetails for the two fault signatures (`ComposeEndpoints.Save`, commit `1b1adb783`); (3) **#4a** deploy check `scripts/Verify-ComposeIdentityKey.ps1`.
- **Remaining prod-safety**: **DEF-003 / GitHub #781** (self-heal on "found multiple", retroactive dedup admin tool, runtime key-health probe). #1 needs a BFF deploy to reach dev (dev key is now Active so the fault won't recur there).

### UAT-03 — No name-on-save modal on first save of a new file — 🔎 Investigate
- **Rounds**: R2 items 3a/3b.
- **Expected (FR-02)**: first save of a new document (create-on-save) prompts for document name + file name.
- **Root cause (CONFIRMED)**: `saveNeedsName` (`ComposeWorkspace.tsx:2186`) deliberately skips the modal for a normal Save unless the doc is `neverPersisted` **AND** `isUntitledDraftName(fileName)`. An imported/uploaded file already carries a real filename → `isUntitledDraftName` is false → no prompt (code comment @2184 states this explicitly). Design vs FR-02 intent gap: FR-02 says "first save of a new document (create-on-save) prompts", which an uploaded first-save is.
- **Resolution (proposed, R7)**: widen the first-save gate to prompt on **any** `neverPersisted` create-on-save, seeding the modal `defaultName` with the current filename so the user confirms/renames rather than being blocked. Small change in `saveNeedsName`/`requestSave`. Owner: confirm this is the desired behavior (prompt-on-every-new vs keep-uploaded-name).

### UAT-04 — No progress indicator on toolbar actions — 🟡 Open
- **Rounds**: R1 item 8, R2 item 3c.
- **Root cause**: R7 added the save-*state* indicator (Saving…/Saved/Unsaved) but there is no immediate **busy/spinner** feedback when a toolbar action is clicked (save, run memo, AI actions) — the user clicks and waits with no signal.
- **Resolution**: TBD — add a per-action busy state (button spinner/disabled + optional progress text) on save / memo / AI-action dispatch.

### UAT-05 — Header banners can't be dismissed — 🟡 Open
- **Rounds**: R1 item 9.
- **Root cause**: the workspace header banner messages lack a consistent working dismiss/clear (one had an ✕, but not uniformly).
- **Resolution**: TBD — ensure every header banner is dismissible; consider auto-expiry for informational ones.

### UAT-06 — "suggested edit couldn't be placed — wording differs slightly" — 🟡 Open
- **Rounds**: R1 item 4a, R2 item 5b. **Intermittent** (other same-action runs succeeded).
- **Root cause**: the AI-drafted redline's target text does not byte-match the current document, so strict `(paraId,runIndex,offset)` anchor placement fails; per ADR-049 the op is surfaced (warn-don't-drop) but not auto-applied. The user can still edit/save.
- **Resolution**: TBD — Fidelity: fuzzy/tolerant anchor placement (or a "show me where" affordance) so near-match edits still place. Likely the **fidelity fast-follow** (relates to DEF-002 / UAT-09).

### UAT-07 — "content simplified when saving" warnings — unhelpful + real loss — 🟡 Open
- **Rounds**: R2 items 3d/3f. Observed: indentation-dropped ×23, paragraph-style-flattened ×62, table-formatting-flattened ×29, section-break ×3, tab ×5, line-break ×2, internal-link-flattened, link-target-not-preserved.
- **Two facets**:
  - **(a) Real fidelity loss — ⛔ BLOCKER (owner, 2026-08-18)**: the render-on-save model flattens these features today. Owner: *"if this isn't fixed then Compose is really not usable."* This is **no longer deferrable backlog** — it MUST be fully fixed. Whether it ships as an R7 phase or a new `compose-r8` is **semantic**; the binding point is full remediation of render-on-save fidelity (indentation, paragraph styles, tables, section breaks, tabs, line breaks, internal links survive the save round-trip). Supersedes the DEF-002 "fast-follow / deferred" framing.
  - **(b) UX — ✅ Fixed `cdb1dbcb4`**: raw warnings ("paragraph-style-flattened ×62") replaced with a concise plain-language summary ("Some formatting (indentation, paragraph styles, tables) was simplified … your text and content are intact").
- **Root-cause of the miss (process)**: the loss was **known and deliberately deferred** (R4/R6 "warn-don't-drop" + the R6 defer-register §C widener backlog), NOT hidden — but it was **mis-classified as "acceptable degradation / cosmetic widener"** when it is actually a usability blocker for real legal documents. Lesson: re-audit every "accepted degradation" for true severity (see the Proactive Hidden-Issue Audit section below).
- **Resolution**: (a) full render-on-save fidelity remediation — **must-fix**, scoped as its own workstream (R7 phase or compose-r8, owner's naming call); (b) done.

### UAT-08 — Create Summary Memo / Analysis workflow — 🟡 Open
- **Rounds**: R1 item 7.
- **Root cause (partly confirmed)**: the "Promote to Analysis…" affordance **exists** in `HistoryOverlay.tsx` (task 023) — but the code notes "task 034 owns turning it on," so it appears **gated / not fully enabled**, which is why the user doesn't see it. Plus the design gap **(b)**: the user expects an **Analysis to auto-create when analysis runs** on a document, so the manual promote step is confusing.
- **Resolution (R7, per owner — Compose-triggered, stays here)**: (a) verify the `HistoryOverlay` promote gate + enable/surface it; (b) design decision — auto-create an Analysis when a document analysis completes so the memo always has a home. Needs a design call with owner before coding (b).

### UAT-09 — Advisory comments couldn't be anchored + op-log `commentAnchor` — 🔎 Investigate
- **Rounds**: R2 item 6 (console).
- **Signals**: `[ComposeWorkspace] 2 of 10 advisory comment(s) could not be anchored (strict resolution failed)`; `[ComposeEditor] op-log: unrepresentable step … mark-outside-closed-set:commentAnchor`.
- **Root cause (hypothesis)**: some AI-advisory comment anchors don't resolve against the current doc (same class as UAT-06); and a `commentAnchor` mark is outside the op-log's representable closed set, so a comment-bearing edit can't be captured.
- **Resolution**: TBD — Fidelity/comment-anchor investigation (relates to UAT-06). Confirm whether the 2 unanchored comments are silently lost or surfaced.

### UAT-10 — SignalR notifications 401 on negotiate — 🔎 Investigate
- **Rounds**: R2 item 6 (console).
- **Signal**: `[SpaarkeAi] Notifications client failed to connect (poll-fallback is active): Failed to complete negotiation … Status code '401'`.
- **Root cause (partly confirmed)**: SpaarkeAi's notifications wiring (`services/notificationsBootstrap.ts`, task 021) is a **thin proof-of-wiring** that starts the SignalR client after auth init; the hub `negotiate` returns **401**, i.e. the token SpaarkeAi presents isn't accepted by the notifications hub (audience/scope mismatch or hub auth not configured for this env). **Degraded, not broken** — the `GET /api/notifications/pending` poll-fallback keeps notifications flowing.
- **Resolution (R7 investigate first; move only if a live notifications project is actively fixing hub auth)**: trace the negotiate token audience/scope vs the hub's expected auth; likely a BFF/hub auth-config fix (env or token-audience). Low user impact (poll-fallback), so lower priority than the save/UX items.

---

## Disposition roll-up (revised 2026-08-18 per owner principle)

**Governing principle (owner, 2026-08-18)**: *do NOT hand off or defer to other projects unless an
in-progress project is directly and currently addressing the issue.* No phantom projects. Default = fix in R7.

- **Fix in R7 (this UAT round)** — **UAT-01…UAT-10 all default here**, including:
  - UAT-01 ✅, UAT-02 ✅ (+DEF-003 prod-safety remainder).
  - UAT-03 (name modal), UAT-04 (progress), UAT-05 (dismiss), UAT-07b (warning UX): R7 UX/behavior fixes.
  - UAT-06 (anchor placement) + UAT-09 (comment anchoring): investigate in R7 — a UX-level improvement (surface the near-match text to insert) is R7-scope even if a deeper op-log/patch-engine change would be larger. Only the engine-level part, if it proves large, gets an explicit owner scope decision (NOT an automatic hand-off).
  - UAT-08 (Create Summary Memo / auto-create Analysis): **stays in R7** — it is a Compose/Assistant-surface-triggered flow the user hits inside Compose UAT (owner: "isn't this a Compose-triggered event? should not pass to another project").
  - UAT-10 (notifications 401): investigate in R7 first; only move if an in-progress notifications/platform project is actively fixing hub auth (verify before moving — not assumed).
- **The one genuine large-engine backlog** — **UAT-07a** (render-on-save fidelity wideners: indentation/paragraph-style/table survival). This is real engine work outside R7's editor-UX charter. It is **tracked** (DEF-002 / GitHub #777) as backlog, NOT handed to an active project. Whether it expands R7 or becomes its own scheduled project is an **explicit owner decision**, not a default deferral. `spaarkeai-compose-fidelity-wideners-r1` is only a *proposed name* for that backlog, not a created project.

R7 closes only when every row is ✅ Fixed, or 📦 Deferred with an explicit owner decision + a tracking link.
Update this file as each is resolved.

---

## Proactive Hidden-Issue Audit (owner-requested 2026-08-18)

**Why**: UAT-07a (render-on-save fidelity loss) was a *fundamental* problem that had been consciously
deferred/mis-classified rather than hidden — and it nearly shipped as "acceptable." To avoid being
surprised again, we ran a **proactive audit** hunting other "accepted limitations" that are actually
blockers, across three dimensions:
1. **Fidelity** — everything Compose drops/flattens/approximates on load AND save (07a's siblings).
2. **Silent failures & accepted limits** — swallowed errors, feature gates, stubs/"thin wiring", deferred-as-OK.
3. **Anchor/op-log/comment robustness** — where AI edits + comments can silently fail or lose work (UAT-06/09's siblings).

Findings are appended below as **UAT-11+** with the same fields (root cause, severity, resolution, disposition).
Severity here uses **⛔ BLOCKER** (Compose not usable / legally wrong for real documents) deliberately, so
severity mis-classification (the 07a mistake) is not repeated.

### Audit dimension 2 (silent failures & accepted limits) — findings

Recurring anti-pattern found: a `catch`/early-return degrades a value to `undefined`/empty, and the
downstream consumer emits a **generic or absent** message — the same shape as UAT-01/UAT-08.

| ID | Issue | Severity | Root cause / evidence | Resolution | Tracked? |
|----|-------|----------|-----------------------|------------|----------|
| **UAT-11** | Container-resolution residual — UAT-01 only fixed the iframe-Xrm READ | ⛔ BLOCKER | The resolver is a one-shot `useEffect([],)` with **no retry** + a swallowed `catch`→`console.warn`. Any *other* failure (Xrm not ready at that instant, transient 401, Dataverse query fault, half-provisioned BU) leaves `containerId` undefined → the save gate emits the **dishonest** "your BU has no storage container configured" — telling the admin to fix a correctly-configured BU. `ComposeDirectWidget.tsx:201,205-208` + `composeEditor.registration.ts` (same shape) → `ComposeWorkspace.tsx:1496-1506` | (a) distinguish "resolution failed" from "BU genuinely has no container" and show an honest message; (b) retry / re-resolve on the save attempt instead of one-shot-on-mount | ❌ (UAT-01 marked fixed; this residual new) |
| **UAT-12** | Imported Word **tracked-changes + comments silently dropped** on any annotation-read failure at load | ⛔ BLOCKER | `LoadAsync` annotation read wrapped in a catch that sets `importedRevisions=[]` + `importedComments=[]` on ANY exception, no warning, no client signal → a doc WITH redlines/reviewer comments mounts looking **clean**. Trust-breaking on a legal-review surface. `ComposeService.cs:681-688` (`LogWarning` only) | Surface an honest banner ("this document's tracked changes/comments couldn't be read — do not treat as clean") when the annotation read fails; never show a silently-clean doc | ❌ new |
| **UAT-13** | Create-on-save **matter/regarding association silently fails** ("non-fatal") | MAJOR | After a new `sprk_document` is created, `onCreateOnSaveComplete` writes the parent/regarding link; a throw is caught + `console.warn` only. Save shows success, but the doc is **orphaned** (not filed under its matter) — load-bearing for the Field-Mapping/set-regarding framework. `ComposeWorkspace.tsx:1986-1992`; contract `context/composeLaunchContext.ts:60-67` | Surface a warning when the association write fails (the doc saved but isn't linked); offer a retry/relink | ❌ new |
| **UAT-14** | Stale-base re-anchor degrades the **whole edit batch to orphan** on an AUTO-band patch failure | MAJOR | On a concurrent/stale-base save, a single AUTO re-anchor failure returns `currentBytes` + all-orphan summary — none of the user's edits land. Surfaced via the orphan/partial banner (same family as UAT-06), so not fully silent — but VERIFY the summary reliably reaches the banner in this branch. `ComposeService.cs:2148-2157` (`BuildAllOrphanSummary`) | Verify the orphan summary always surfaces here; consider a stronger "your edits weren't saved — reload and redo" prompt | ⚠️ partial (UAT-06) |

**MINOR (noted, low priority)**: re-anchor summary persist swallow (`ComposeService.cs:2159-2171`); load-time SPE version-id lookup swallow → retained-bytes fallback (`:708-713`); refresh-profile / background memory-capture best-effort swallows (`ComposeWorkspace.tsx:2046-2050`, `ComposeService.cs:2543-2549/3022-3024/3387-3392`) — the refresh-profile one pairs with UAT-04 (a click that throws shows spinner-then-nothing).

**Confirmed-sound (NOT issues)**: the PDF-intake gate is the exemplary model (typed `ComposePdfIntakeException`→503/422, never a silent empty mount) — the swallow sites above should follow it. Checkout lifecycle + the banner translation layer are honest.

### Audit dimension 1 (fidelity) — findings

**Architectural root cause**: the render-on-save path (`ComposeDocumentRenderer.RenderIntoCarrier`, `:399/:449`
`body.RemoveAllChildren()`) **re-authors the entire `<w:body>` from `ComposeContentModel`** — a THIN model whose
run type (`ComposeContentModel.cs:276`) carries only Text/Bold/Italic/Underline/Href + tracked-change facts.
Anything not in that model is lost on the FIRST save of ANY imported legal document. The original survives in
version history (ADR-049 net), but the live SPE doc that reopens in Word is degraded. **The most dangerous
losses are SILENT (no warning code at all)** — worse than the loud "×62" ones.

| ID | Loss | Severity | Silent? | Evidence |
|----|------|----------|---------|----------|
| **UAT-15** | **Direct character formatting** — font family, SIZE, COLOR, highlight/shading, super/subscript, caps/small-caps, underline style+color, char spacing — stripped whole-document to Normal default | ⛔ BLOCKER | **SILENT** (only bold/italic/underline captured; strikethrough warns) | `ComposeDocxProjectionBuilder.cs:2644-2677`; `ComposeInlineRun` `ComposeContentModel.cs:276` |
| **UAT-16** | **Footnotes / endnotes** dropped from flow (reference vanishes; `footnotes.xml` orphaned → footnote text invisible in saved doc) | ⛔ BLOCKER | warned but code not in friendly-copy → cryptic | `ComposeDocxProjectionBuilder.cs:2748-2753, 866-869` |
| **UAT-17** | **Word fields** — cross-references (`REF`), TOC, page/section refs, DATE/DOCPROPERTY — flattened to STATIC text; live reference lost (stale if numbering shifts) | ⛔ BLOCKER | warned but cryptic | `ComposeDocxProjectionBuilder.cs:2414, 2445` |
| **UAT-18** | **Paragraph spacing** — line spacing (single/1.5/double), space-before/after, shading, borders, keepNext/keepLines, tab-STOP defs — dropped (BLOCKER for court filings w/ double-spacing rules) | ⛔ BLOCKER (context) | **SILENT** | `ComposeDocxProjectionBuilder.cs:1935-2090` |
| **UAT-19** | **Content controls / SDT** (form fields, dropdowns, date pickers, repeating sections) flattened to text/opaque; data-binding lost | MAJOR (BLOCKER for templates) | warned cryptic | `:316, 1892, 1885, 2520, 410` |
| **UAT-20** | Grouped fidelity MAJORs: strikethrough dropped (`:2669`); numbering-unresolved loses number (`:1006-1021, 2166`); complex/floating objects dropped + text-boxes flattened/repositioned (`:1919-1923, 2438...`); comment rich-content + reply-thread flattened, 4-part threaded comments unrepresentable (`ComposeContentModel.cs:45-46`, `IComposeService.cs:571-572`) | MAJOR | warned cryptic | (see codes) |

**Meta**: (1) **Silent > loud** is the real hazard — UAT-15/18 emit NO warning, so UAT won't flag them; users just see a "wrong-looking" doc after save. (2) **Warned-but-cryptic gap**: ~12 emitted codes (`unrepresented-footnote-reference`, `field-flattened-to-text`, `content-control`, `strikethrough-flattened`, `numbering-unresolved`, …) are absent from `SAVE_DEGRADATION_COPY` → fall through to the raw-code line (UAT-07b added friendly copy for the widener family but NOT these).

### Audit dimension 3 (anchor / op-log / comment robustness) — findings

The op-log WRITE path is robust (paraId-anchored, refuse-don't-mis-place; server prong-1 partial-apply honors "never silently drop"). The **client resolution/placement layer leaks the invariant**. Representable op-log set = 13 ops (`compose-operations.ts:54-70`); `commentAnchor` is a mark outside it → the `mark-outside-closed-set:commentAnchor` line (UAT-09).

| ID | Failure | Severity | Evidence |
|----|---------|----------|----------|
| **UAT-21** | **AI redline falls back to the user's LIVE SELECTION on a target miss and reports "applied"** — can strike-and-replace the WRONG text (stale caret / replayed redline), presented as success. Silent mis-placement of a legal edit. | ⛔ BLOCKER | `usePendingRedline.ts:667-690` |
| **UAT-22** | **Comments silently dropped from the SAVE payload** — 3 `continue` paths (anchor mark gone / start unresolved / range in different paragraph), no banner/count → a comment the user sees in the gutter never reaches Word | ⛔ BLOCKER | `ComposeCommentThread.types.ts:240-263` |
| **UAT-23** | **`deletedContentFlag` ops filtered out of save with NO surface** — flag set by genuine deletion AND by rebasing drift (derive-null); the drift subcase discards a still-valid edit silently (no callback, unlike unrepresentable/refused which warn) | ⛔ BLOCKER | `ComposeWorkspace.tsx:1542-1544`; `stepOperationInterceptor.ts:1438-1483` |
| **UAT-24** | **Strict-only resolution, no fuzzy match** → AI edits miss on ANY paraphrase (normal for AI drafting). Root of UAT-06/09/21/22. Whole-doc `materializeMany` silently skips misses. | MAJOR | `usePendingRedline.ts:337-388, 811-816` |
| **UAT-25** | **Mainstream ContentModel save bypasses stale-base detection** — the eTag staleness assert only runs on the transitional op-log path; a post-cutover imported dirty save (ContentModel) never checks → **lost update** on a concurrent Word/tab writer | MAJOR | `ComposeService.cs:1216` |
| **UAT-26** | **First Compose save of a pre-existing item has no concurrency guard** (no prior stamp to assert against → blind overwrite of external edits since load); + op-log ambiguous-order resolved by unverified guess with no surface (`onAmbiguousOrder` never wired); + deferred/unrepresentable/refused collapse to ONE vague banner (user can't tell an EDIT was dropped vs formatting) | MAJOR | `ComposeService.cs:1180,1216-1221`; `stepOperationInterceptor.ts:1655-1662`; `ComposeEditor.tsx:1931-1957` |

**Highest-leverage fix (both auditors agree)**: the strict-only resolver (`resolveTargetSpans`) — UAT-06, 09, 21, 22 all trace to it. A **tolerant-but-surfaced** resolver ("propose, don't auto-place") neutralizes the wrong-location fallback AND the silent skips at once.

---

## Consolidated severity picture (post-audit, 2026-08-18)

- **⛔ BLOCKER (10)**: UAT-01(residual→11), 02✅, 07a, 11, 12, 15, 16, 17, 18, 21, 22, 23. Several are **SILENT data/fidelity loss** — the class UAT would NOT surface as a warning.
- **The architectural finding**: render-on-save re-authors the whole body from a THIN content model (text + bold/italic/underline/href). This is not a "widener" gap — the model itself can't carry real legal-document fidelity. UAT-07a + UAT-15/16/17/18/20 are all facets of ONE root cause. **This is compose-r8-architecture-sized**, not a batch of patches.
- **The trust finding**: the edit/comment placement layer can mis-place a redline onto the wrong text and report success (UAT-21), and silently drop comments (UAT-22) and valid edits (UAT-23). For a legal-review product these are correctness/trust defects, not cosmetics.

## Owner-approved split (2026-08-18): Honest-now (R7) / Faithful-next (compose-r8)

**R8 created** — 📦 the FAITHFUL-bar fidelity issues MOVE to a new project **`spaarkeai-compose-r8`**
(investigation-first): [`projects/spaarkeai-compose-r8/notes/fidelity-architecture-investigation.md`](../../spaarkeai-compose-r8/notes/fidelity-architecture-investigation.md).
Moved (tracked there, not dropped): **UAT-07a, UAT-15, UAT-16, UAT-17, UAT-18, UAT-19, UAT-20** (the
render-on-save architecture — preserve formatting/structure through save). R8's first job is a full
investigation/research pass to choose the correct write-model before building.

**Stays in R7 (HONEST + SAFE batch — proceed now)**:
- ✅ Done: UAT-01(base), 02, 03, 05, 07b.
- ✅ **UAT-21 Fixed** (2026-08-18): removed the silent live-selection fallback in `usePendingRedline.ts`
  (`resolveTargetSpans` not-found path). An unresolved AI-redline target now ALWAYS surfaces the banner and
  places NOTHING — no more strike-and-replace onto stale/replayed selection reported as "applied". Reverses the
  Round-3 UAT Test #4 fallback (an honest dead-end beats a silent wrong edit). 3 hook tests rewritten to the
  new honest behavior; 80/80 usePendingRedline green.
- ✅ **UAT-22 Fixed** (2026-08-18): `composeSessionCommentThreadsToAnchoredComments` now takes an optional
  `onDropped` sink fired at its 3 silent-drop `continue`s (anchor gone / non-paragraph / cross-paragraph);
  `getAnchoredComments(onDropped)` threads it; ComposeWorkspace counts drops and folds a
  `comment-anchor-unresolved` degradation warning (op-log path). Friendly banner copy added. +3 sink tests.
- ✅ **UAT-23 Fixed** (2026-08-18): the interceptor's `serialize` now splits the conflated `deletedContentFlag`
  into a new `anchorLostFlag` = re-derivation FAILURE that is NOT a genuine deletion (a still-valid edit the
  op-log filter drops). ComposeWorkspace counts these and folds an `edit-anchor-lost` degradation warning
  (op-log path) — genuine deletions stay quiet (no false alarm). +2 interceptor tests; 64/64 green.
- 🔧 Do next (the batch): **UAT-11** (container residual — honest signal + retry), **UAT-12** (surface dropped
  tracked-changes/comments on read failure), **UAT-13** (surface association orphan),
  **UAT-24** (tolerant-but-SURFACED resolver — propose-don't-auto-place; UAT-21 already applied the SURFACE half),
  the warned-but-cryptic copy gap (friendly copy for footnote/field/content-control/numbering codes), plus the
  earlier-triaged **UAT-04** (progress indicator), **UAT-08** (promote + auto-create Analysis), **UAT-09**
  (comment anchoring signal), **UAT-10** (notifications 401), **UAT-25/26** (concurrency-guard on the
  ContentModel save path — honest lost-update prevention/warning).
- **R7 batch theme**: make Compose *never lie* — no silent drops, no mis-placement, no false "saved/applied".
