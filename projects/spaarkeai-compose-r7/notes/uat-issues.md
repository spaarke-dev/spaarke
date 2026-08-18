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
| UAT-06 | "suggested edit couldn't be placed — wording differs slightly" | 1+2 | Medium | AI redline target text didn't byte-match the doc → strict anchor placement failed (warn-don't-drop). Intermittent. | 🟡 Open | Fidelity |
| UAT-07 | "content simplified when saving" warnings — unhelpful + real fidelity loss | 2 | Med-High | (a) render-on-save fidelity wideners not implemented; (b) warnings are cryptic to users | 🟡 Open | (a) Fidelity DEF-002; (b) R7 UX |
| UAT-08 | Create Summary Memo needs an Analysis; promote option missing; should auto-create | 1 | Med-High | Memo requires a session→Analysis link; "Promote to Analysis" not visible in History; design wants auto-create on analyze | 🟡 Open | SpaarkeAi (design) |
| UAT-09 | Advisory comments couldn't be anchored (2 of 10) + op-log unrepresentable `commentAnchor` | 2 | Medium | Comment anchor strict-resolution failed for some; `commentAnchor` mark outside the op-log closed set | 🔎 Investigate | Fidelity |
| UAT-10 | SignalR notifications 401 on negotiate (poll-fallback active) | 2 | Medium | Notifications hub auth returns 401 on negotiate | 🔎 Investigate | Platform |

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
- **Hypothesis**: the modal (`ComposeSaveNameDialog` + task-030 `requestSave` interception) fires only when the draft `saveNeedsName`/`isUntitledDraftName` — an **uploaded** file already carries a filename, so it may auto-use that and skip the prompt. Need to confirm whether that's intended (uploaded files keep their name) vs a gap (user expected the prompt for any new-to-system doc).
- **Resolution**: TBD — confirm intended behavior with owner; if the prompt should fire for uploaded new docs too, widen the `saveNeedsName` gate.

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
- **Root cause**: the Review Summary Memo requires the session to be linked to an **Analysis**; when a review runs on a document that isn't promoted, the memo has nowhere to save. Two problems: **(a)** the "Promote to Analysis…" affordance isn't visible in History (gap/bug); **(b)** design — the user expects an **Analysis to auto-create when an analysis is run** on a document, not a manual promote step.
- **Resolution**: TBD — SpaarkeAi Assistant design decision (auto-create Analysis on analyze) + fix/expose the promote affordance. Likely **moves to a SpaarkeAi/assistant project** (not Compose).

### UAT-09 — Advisory comments couldn't be anchored + op-log `commentAnchor` — 🔎 Investigate
- **Rounds**: R2 item 6 (console).
- **Signals**: `[ComposeWorkspace] 2 of 10 advisory comment(s) could not be anchored (strict resolution failed)`; `[ComposeEditor] op-log: unrepresentable step … mark-outside-closed-set:commentAnchor`.
- **Root cause (hypothesis)**: some AI-advisory comment anchors don't resolve against the current doc (same class as UAT-06); and a `commentAnchor` mark is outside the op-log's representable closed set, so a comment-bearing edit can't be captured.
- **Resolution**: TBD — Fidelity/comment-anchor investigation (relates to UAT-06). Confirm whether the 2 unanchored comments are silently lost or surfaced.

### UAT-10 — SignalR notifications 401 on negotiate — 🔎 Investigate
- **Rounds**: R2 item 6 (console).
- **Signal**: `[SpaarkeAi] Notifications client failed to connect (poll-fallback is active): Failed to complete negotiation … Status code '401'`.
- **Root cause (hypothesis)**: the notifications hub negotiate is unauthorized (token/scope/hub-auth config). Poll-fallback keeps notifications working, so it's degraded not broken.
- **Resolution**: TBD — Platform/notifications auth investigation (token audience/scope for the hub; env config). Likely **moves to the notification-spine/platform** owner.

---

## Disposition roll-up (for owner decision)

- **Stay in R7 (UAT round)**: UAT-01 ✅, UAT-02 ✅ (+DEF-003), UAT-03, UAT-04, UAT-05, UAT-07(b UX).
- **Move to Fidelity fast-follow** (`spaarkeai-compose-fidelity-wideners-r1` / DEF-002): UAT-06, UAT-07(a), UAT-09.
- **Move to SpaarkeAi/Assistant**: UAT-08.
- **Move to Platform/notifications**: UAT-10.

R7 closes only when every row is ✅ Fixed or 📦 Deferred-with-a-link. Update this file as each is resolved.
