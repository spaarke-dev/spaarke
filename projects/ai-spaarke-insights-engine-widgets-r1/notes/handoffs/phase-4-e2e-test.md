# Phase 4 End-to-End Form Test — Task 044

> **Task**: 044 — End-to-end form test (TTI unblocked NFR-03)
> **Phase**: 4 (Matter form integration)
> **Rigor Level**: STANDARD (per POML)
> **Authored**: 2026-06-11
> **Environment**: `https://spaarkedev1.crm.dynamics.com` (Spaarke Dev)
> **Form**: Matter main form (id `4fa382f2-c273-f011-b4cb-6045bdd6a665`)
> **Mode**: Sub-agent static verification + operator playbook (sub-agent cannot drive MDA UI)

---

## TL;DR — Acceptance Matrix

| FR / NFR | Acceptance | Verdict (sub-agent) | Verdict (operator, gated) |
|---|---|---|---|
| **FR-17** | OnLoad reads `sprk_performancesummary` + tolerates non-JSON | ✅ PASS (static — code review of deployed JS) | ⏳ Operator confirms via console signals |
| **FR-18** | Stale/absent → fire-and-forget POST to `/api/insights/ask`; not awaited | ✅ PASS (static — wire shape fixed; Promise detached + .catch swallow) | ⏳ Operator confirms POST in Network tab |
| **FR-19** | Existing stored summary renders IMMEDIATELY on form load | ⚠️ PARTIAL — code path wired; visible card render DEFERRED per Task 043 P1 gap (no IIFE bundle, no WebResource control on `tab_report card_section_3`) | ⏳ Operator confirms script LOADED via console; visible card render is r2/P1 retrofit |
| **FR-20** | Manual refresh button → blocking-style invocation + spinner + re-render | 🚧 DEFERRED — refresh button is part of the React card; not visible until IIFE bundle ships | Out of scope for Phase 4 demo |
| **NFR-03** | Form TTI unaffected by pre-warm | ✅ PASS (static — synchronous OnLoad return; rAF defer in mount glue; fire-and-forget POST) | ⏳ Operator confirms perceptual TTI + Performance API measurement |

**Overall Phase 4 demo verdict**: PASS for the staging path. Visible card render is documented as a P1 retrofit gated on the `@spaarke/ai-widgets` IIFE bundle (see Task 043 handoff `form-deploy.md` §"Documented Gap").

---

## Static verification (sub-agent)

### A. Script wiring (form attachments)

Confirmed via Task 043 deploy handoff (`form-deploy.md` §"Matter form patches"):

- Matter form (`4fa382f2-c273-f011-b4cb-6045bdd6a665`) has 4 libraries; 2 NEW:
  - `sprk_matter_insight_onload.js` (NEW — Task 040+041)
  - `sprk_matter_insight_card_mount.js` (NEW — Task 042)
- Matter form OnLoad event handlers (5; 2 NEW):
  - `Spaarke.MatterInsight.onLoad` (enabled=true)
  - `Spaarke.MatterInsightCard.onLoad` (enabled=true)
- Both handlers `passExecutionContext=true`.
- Pre-existing handlers (`Spaarke.MatterKpi.onLoad`, `Spaarke.SubgridRollup.onLoad`) preserved.

Deployed web resources (3 in solution `spaarke_insights`):

| Name | Id | Source |
|---|---|---|
| `sprk_matter_insight_onload.js` | `cf1b8b27-8b65-f111-ab0c-70a8a590c51c` | `insightWidgetOnLoad.js` 15,304 bytes |
| `sprk_matter_insight_card_mount.js` | `60c68a27-8b65-f111-ab0c-7ced8ddc4cc6` | `insightCardMount.js` 29,591 bytes |
| `sprk_matter_insight_card_host.html` | `d51b8b27-8b65-f111-ab0c-70a8a590c51c` | `matter_insight_card_host.html` 7,454 bytes |

`PublishAllXml` + `PublishXml(sprk_matter)` both 2xx per Task 043 deploy log.

### B. Wire-shape verification (FR-17 / FR-18)

Grep of deployed `insightWidgetOnLoad.js`:

- `ns._prewarmEndpoint = "/api/insights/ask"` (line 63) — same-origin relative path.
- `ns._playbookName = "matter-health-single"` (line 75) — bare playbook name; Q-U1 ban honoured (no `@v1`/`@vN`).
- `_firePrewarm` body (line 176-180): `{ question: ns._playbookName, subject: subject_1, parameters: {} }` — **wire shape corrected** per Task 041 P1 fix (2026-06-11); aligns with `BFF.Models.Insights.InsightAskRequest` accepting canonical playbook names via `InsightsPlaybookNameMapOptions.ResolveOrDefault`.
- `fetch` invocation (line 195) is followed by detached `.then` + `.catch` swallowing rejections — NO `await`, fire-and-forget per FR-18 contract.
- `keepalive: true` (line 190) — allows pre-warm to complete after form navigation.

**Static verdict — FR-17**: ✅ PASS. OnLoad reads `sprk_performancesummary`, parses as JSON via `_parseEnvelope`, treats non-JSON / parse failure as "no stored summary" (decision.status="absent") triggering pre-warm.

**Static verdict — FR-18**: ✅ PASS. Fire-and-forget POST dispatched as detached Promise; `.then`/`.catch` log only; rejections swallowed via two-layer .catch defense.

### C. Pre-warm wire-shape mismatch (Task 041 P1) — RESOLVED

The Task 042 handoff documented Task 041's pre-warm posting an incorrect `{ topic, mode, subject, parameters }` body that returned 400. Inspection of the deployed `insightWidgetOnLoad.js` (line 177) confirms the body NOW sends `{ question: ns._playbookName, ... }` — the P1 was fixed before deploy. Operator should observe 200 (or 202) responses, not 400, in the Network tab.

### D. NFR-03 (Form TTI) — wiring verification

- `Spaarke.MatterInsight.onLoad` (`insightWidgetOnLoad.js`) — Xrm.WebApi.retrieveRecord returns a Promise; handler does NOT await; calls `_firePrewarm` from the `.then` callback after the synchronous OnLoad return path has already completed.
- `Spaarke.MatterInsightCard.onLoad` (`insightCardMount.js`) — runs through `_resolveHost()` then defers mount through a single `requestAnimationFrame` so the React render is detached from the synchronous OnLoad return path (verified by the comment at line 58-60: "Mount runs after a single `requestAnimationFrame` so the card render is detached from the synchronous OnLoad return path").
- Both handlers wrap the entire body in `try/catch` so synchronous failures log only and do NOT propagate to the form runtime.

**Static verdict — NFR-03**: ✅ PASS. Both OnLoad handlers return synchronously; all async work is detached. Operator confirms perceptually + via Performance Timing API.

### E. Render path (FR-19) — wired but visible-render gated

The mount glue (`insightCardMount.js`) executes `_resolveHost()` which looks for the host DOM element by id `spaarke-matter-insight-card-host`. Per Task 043 P1 gap, the WebResource control on `tab_report card_section_3` was NOT deployed — therefore the host element will NOT exist on the form. The mount glue emits a warning (line 492-496):

```
[Matter Insight Card] v0.1.0 host element 'spaarke-matter-insight-card-host' not found on form.
    FormXml patch may not be deployed (Task 043).
```

This warning IS the expected Phase 4 staging signal: it proves the script LOADED, the namespace bound, `_resolveHost()` executed, and contract resolved the subject. The visible card render is gated on the IIFE bundle + WebResource control work (P1 retrofit per Task 043 handoff §"Recovery / completion path").

**Static verdict — FR-19**: ⚠️ PARTIAL. Mount-glue code path verified; FR-19 immediate-render acceptance (zero perceptible UI block on form load + immediate render of stored summary) is structurally satisfied because (a) the script is non-blocking, (b) when the IIFE bundle ships the `INIT_FROM_STORED_ENVELOPE` action in `state.ts` honours immediate render. Visible card render DEFERRED.

### F. Refresh button (FR-20) — deferred with FR-19

The manual refresh button is part of the React `InsightSummaryCard` component (idle → loading transition + spinner + re-render on settle). Like FR-19, it is structurally wired in the deployed `insightCardMount.js` (`onFetchInsight` callback sends `force=true` query param per Task 042 decision) but not visible until the IIFE bundle ships.

**Static verdict — FR-20**: 🚧 DEFERRED. Out of scope for Phase 4 demo per Task 043 P1 gap.

---

## Operator playbook (live MDA smoke test)

> Sub-agent CANNOT drive the MDA UI. Operator runs this script in a browser. ~10 minutes.

### Prerequisites

- Logged into `https://spaarkedev1.crm.dynamics.com` as a user with read access to `sprk_matter`.
- At least one Matter record exists (any one).
- Chrome DevTools (F12) familiarity.

### Step 1 — Hard refresh + open Matter

```
1. https://spaarkedev1.crm.dynamics.com → hard refresh (Ctrl+Shift+R).
2. Open the Spaarke Engineering app.
3. Navigate: Matters list → click any Matter to open the record.
```

### Step 2 — Observe console signals (FR-17 + FR-18 evidence)

```
4. Open DevTools (F12) → Console tab. Clear console.
5. Reload the Matter form (Ctrl+R while focused on the form).
6. Expected NEW console output (alongside pre-existing handlers):

   [Matter Insight] v0.2.0 onLoad start (matter id=...)
   [Matter Insight] v0.2.0 envelope read: <absent|fresh|stale>
   [Matter Insight] v0.2.0 prewarm POST fired (non-blocking). status=<absent|stale>, playbook=matter-health-single, subject=matter:<guid>
   [Matter Insight] v0.2.0 prewarm dispatched. status=<absent|stale>, httpStatus=<200|202>, subject=matter:<guid>

   [Matter Insight Card] v0.1.0 onLoad start
   [Matter Insight Card] v0.1.0 envelope read: <absent|fresh|stale>
   [Matter Insight Card] v0.1.0 host element 'spaarke-matter-insight-card-host' not found on form.
       FormXml patch may not be deployed (Task 043).
```

PASS criteria for Step 2:
- Both `[Matter Insight] v0.2.0 onLoad start` AND `[Matter Insight Card] v0.1.0 onLoad start` appear.
- The "host element not found" warning is the EXPECTED Phase 4 signal (proves mount glue ran).
- No JavaScript errors that block form interactivity.

FAIL signals (file P0 if any of these):
- Either handler does NOT log "onLoad start" → library not loaded or function not registered.
- `Uncaught` error from `Spaarke.MatterInsight.*` or `Spaarke.MatterInsightCard.*` → bug in deployed JS.
- Form fields become un-editable or load spinner persists > 5s → NFR-03 violation.

### Step 3 — Verify pre-warm POST (FR-18 evidence)

```
7. DevTools → Network tab → filter "/api/insights/ask".
8. Reload the Matter form once more if needed.
9. Expected: ONE POST request shortly after form load.
   - Request body: { "question": "matter-health-single", "subject": "matter:<guid>", "parameters": {} }
   - Response status: 200 (cached) OR 202 (queued) — NOT 400.
   - Type: fetch
   - Initiator: insightWidgetOnLoad.js
```

PASS criteria for Step 3:
- POST observed (not awaited — confirmed by the form staying interactive throughout).
- Status 200 or 202.
- Body matches the corrected wire shape.

KNOWN-OK condition: If `decision.status === "fresh"`, the pre-warm SKIPS and you'll see the "prewarm skipped (fresh envelope; ageMinutes=...)" log instead, with NO POST in Network. This is correct FR-18 behaviour. To force the POST, edit `sprk_performancesummary` to either clear it or set its `generatedAt` to > 1 hour ago.

FAIL signals (file P0):
- POST returns 400 → wire shape regressed (Task 041 P1 fix lost).
- POST returns 401/403 → auth issue (Sprk.Bff.Api session cookie not flowing; check `credentials: "include"`).
- No POST when `decision.status === "absent"` or `"stale"` → pre-warm not firing.

### Step 4 — TTI measurement (NFR-03 evidence)

Two methods; either suffices.

**Method A — Performance Timing API (quantitative)**

```
10. Console (after form fully loaded):
    performance.getEntriesByType('navigation')[0].domInteractive
    performance.getEntriesByType('navigation')[0].loadEventEnd
11. Expected: domInteractive — loadEventEnd ≤ baseline (a comparable Matter form WITHOUT the new handlers — sample from a non-spaarke environment or pre-Task-043 state).
12. Acceptance: Form is interactive (fields focusable, ribbon responsive) within 1.5s perceptual on a typical broadband connection.
```

**Method B — Perceptual TTI (qualitative — sufficient for staging)**

```
10. Reload the Matter form.
11. Immediately attempt to click into a text field (e.g., Matter name) or ribbon button.
12. Expected: form is interactive within 1-2 seconds; no perceptible delay vs. a Matter form before Task 043.
```

PASS criteria for Step 4:
- Method A: domInteractive within expected baseline; no >500ms regression attributable to our handlers.
- Method B: form interactive within 1-2s perceptual; no spinner persisting on form fields.

FAIL signals (file P0):
- TTI regression > 500ms vs. baseline → NFR-03 violation; the OnLoad handlers are blocking somehow (likely an await leaked in).
- Form fields un-clickable for > 3s → critical NFR-03 violation.

### Step 5 — Optional: stale envelope path

```
13. Open the Matter record via an Advanced Edit / data-entry quick form, OR via the BFF directly.
14. Manually set `sprk_performancesummary` to a JSON envelope with `generatedAt` > 1 hour in the past.
    Example: { "generatedAt": "2026-06-10T00:00:00Z", "summary": "stale test", "version": "0.2.0" }
15. Reload the Matter form.
16. Expected console: "envelope read: stale" + pre-warm POST dispatched.
```

PASS: stale envelope triggers POST.

### Step 6 — Document findings

```
17. Capture console output (right-click → "Save as..." or screenshot).
18. Capture Network tab POST to `/api/insights/ask`.
19. Note any P0/P1 findings inline in this file under "Operator findings" section.
```

---

## Operator findings (to be filled by operator)

| Step | Result | Notes |
|---|---|---|
| 2 — console signals | ⬜ PASS / ⬜ FAIL | |
| 3 — pre-warm POST | ⬜ PASS / ⬜ FAIL / ⬜ SKIPPED (fresh envelope) | |
| 4 — TTI | ⬜ PASS / ⬜ FAIL | TTI value: ___ ms |
| 5 — stale path (optional) | ⬜ PASS / ⬜ FAIL / ⬜ SKIPPED | |

---

## Known gaps

### G.1 — `@spaarke/ai-widgets` IIFE bundle (P1 — gates FR-19 + FR-20 visible render)

Documented in Task 043 handoff `form-deploy.md` §"Documented Gap". Until the IIFE bundle + WebResource control on `tab_report card_section_3` ships, no visible card renders on the Matter form. The OnLoad pre-warm (FR-17, FR-18) and the mount-glue script load (FR-19 structural wiring) are demonstrable WITHOUT the bundle.

Recovery path: see `form-deploy.md` §"Recovery / completion path" — 5 steps including `pac solution export → unpack → edit FormXml → repack → import`.

### G.2 — Performance API baseline (NFR-03 quantitative gate)

We don't have a recorded baseline TTI for the Matter form pre-Task-043. The operator's Method B (perceptual) is the practical gate for Phase 4; a quantitative baseline can be captured in r2 by sampling `performance.getEntriesByType('navigation')` on a fresh environment without the Spaarke handlers.

### G.3 — Sub-agent cannot exercise MDA UI (procedural)

`task-execute` Step 9.7 (UI Testing) requires Claude Code started with `--chrome` flag and operator-confirmed environment access. Sub-agents do NOT have Chrome integration. This handoff therefore ships static verification (sub-agent) + operator playbook (human), per the standard pattern for Phase 4 form-integration tasks.

### G.4 — Task 041 wire-shape mismatch (RESOLVED)

The Task 042 current-task.md noted "Task 041's `_firePrewarm` POSTs the broken `{ topic, mode, subject, parameters }` shape" as a P1 follow-up. Inspection of the deployed `insightWidgetOnLoad.js` (line 177) confirms the body NOW sends `{ question: ns._playbookName, ... }` — fix was applied 2026-06-11 before Task 043 deploy. This handoff therefore does NOT carry a P1 for wire shape; if the operator observes 400 responses in Step 3, file P0 for regression.

---

## Q-U1 ban verification

Grep of `insightWidgetOnLoad.js` and `insightCardMount.js` for `@v[0-9]+` returns zero matches. Version strings (`ns._version = "0.2.0"`, `ns._version = "0.1.0"`) are plain semantic strings — convention-compliant.

---

## Downstream task readiness

| Task | Status | Notes |
|---|---|---|
| Phase 4 demo | ✅ READY (console-observable signals) | Operator runs Steps 1-4 above; visible card render is r2/P1 retrofit. |
| (P1 retrofit) — IIFE bundle + WebResource control | ⏸ DOCUMENTED GAP | Owns `tab_report card_section_3` insertion via solution ZIP import path. Reference: `form-deploy.md` §"Recovery / completion path". |
| Phase 5 — telemetry events | ✅ INDEPENDENT | Task 045+ telemetry meter `Sprk.Bff.Api.InsightWidgets` can be verified independently via BFF App Insights. |

---

## Artifacts

| Path | Purpose |
|---|---|
| `projects/.../notes/handoffs/phase-4-e2e-test.md` | This file — static verification + operator playbook |
| `projects/.../notes/handoffs/form-deploy.md` | Task 043 deploy handoff (cross-referenced for P1 gap) |
| `src/dataverse/forms/sprk_matter/insightWidgetOnLoad.js` | Deployed OnLoad pre-warm (FR-17/18 source) |
| `src/dataverse/forms/sprk_matter/insightCardMount.js` | Deployed mount glue (FR-19 structural source) |

---

*Handoff written 2026-06-11 by task-execute for Task 044. Phase 4 e2e test path documented via static verification + operator playbook. Visible card render gated on P1 IIFE-bundle retrofit (per Task 043 handoff).*
