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
| UAT-03 | No name-on-save modal on first save of a new/uploaded file | 2 | Medium | FR-02 modal likely skipped when the file already carries a name (upload) — expectation vs design | 🔎 Investigate | R7 |
| UAT-04 | No progress indicator on toolbar actions (save, memo, …) | 1+2 | Medium | R7 added save-*state* text but no click-time busy/spinner feedback | 🟡 Open | R7 |
| UAT-05 | Header banner messages can't be dismissed/cleared | 1 | Low-Med | Banners lack a consistent working dismiss control | 🟡 Open | R7 |
| UAT-06 | "suggested edit couldn't be placed — wording differs slightly" | 1+2 | Medium | AI redline target text didn't byte-match the doc → strict anchor placement failed (warn-don't-drop). Intermittent. | 🟡 Open | R7 (UX); engine=owner-decide |
| UAT-07 | "content simplified when saving" warnings — unhelpful + real fidelity loss | 2 | Med-High | (a) render-on-save fidelity wideners not implemented; (b) warnings are cryptic to users | 🟡 Open | R7 (b UX); a=backlog DEF-002 (owner) |
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
  - **(a) Real fidelity loss** — the render-on-save model flattens these features today. This is the **fidelity-widener backlog → DEF-002 / GitHub #777** (`spaarkeai-compose-fidelity-wideners-r1`).
  - **(b) UX** — the raw warnings ("paragraph-style-flattened ×62") are meaningless to end users. R7-scope UX fix: aggregate + translate into plain language (e.g., "Some formatting (indentation, table styling) was simplified to save. The text is intact.") or gate behind a "details" affordance.
- **Resolution**: (a) DEF-002 fast-follow; (b) R7 UX rewrite of the warning surface.

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
