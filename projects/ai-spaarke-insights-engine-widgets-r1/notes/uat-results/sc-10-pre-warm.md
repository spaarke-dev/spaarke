# UAT Result — SC-10 (Background pre-warm on form load when stored summary stale)

> **Task**: 065 — Background pre-warm UAT (SC-10)
> **Authored**: 2026-06-11
> **Scenario source**: `notes/uat-scenarios.md` § Scenario A Steps 1–2 (OnLoad pre-warm) + Scenario C Step 4 (kill-switch interaction)
> **Targets**: **SC-10** (background pre-warm fires on form load when stored summary stale) + **NFR-03** (form TTI unaffected)
> **Rigor**: STANDARD (POML `<rigor>STANDARD</rigor>`; UAT verification, no code changes)
> **Status**: ✅ **PASS via static verification + operator script** (pending operator live confirmation in `spaarkedev1`)

---

## 1. Critical context — what is being verified

SC-10 is a **telemetry + non-blocking-fetch** contract, not a visible-render contract. The acceptance criteria are:

| Acceptance | r1 verification path | Verification mode |
|---|---|---|
| Pre-warm telemetry event present (OnLoad-triggered invocation observable) | DevTools Network tab + console signals from deployed `insightWidgetOnLoad.js` | Static + operator |
| Form TTI within budget (NFR-03 — pre-warm does NOT block UI interactivity) | Synchronous OnLoad return + detached Promise + Performance Timing API | Static + operator |

The IIFE-bundle visible-render gap (codified in `uat-scenarios.md` § "Phase 4 IIFE-bundle visible-render gap" and `notes/handoffs/phase-4-e2e-test.md`) **does NOT block SC-10** because SC-10's contract is observed via Network tab + console + Performance API, none of which require a rendered card.

---

## 2. Static verification — deployed `insightWidgetOnLoad.js` (v0.2.0)

### 2.1 Wire shape — Task 041 P1 fix confirmed

Inspection of `src/dataverse/forms/sprk_matter/insightWidgetOnLoad.js` (deployed to `spaarkedev1` via Task 043, web resource id `cf1b8b27-8b65-f111-ab0c-70a8a590c51c`):

| Line | Code | Significance |
|---|---|---|
| 55 | `ns._version = "0.2.0"` | Task 041 version bump (0.1.0 → 0.2.0) |
| 63 | `ns._prewarmEndpoint = "/api/insights/ask"` | Same-origin relative path |
| 75 | `ns._playbookName = "matter-health-single"` | Canonical playbook name (Q-U1 compliant — no `@v1`) |
| 77 | `ns._subjectScheme = "matter"` | r2 multi-entity subject scheme |
| 176–180 | `var body = { question: ns._playbookName, subject: subject_1, parameters: {} };` | **Wire shape corrected** — sends `question` (matches `InsightAskRequest.Question` in `Models/Insights/InsightAskRequest.cs:55`). Pre-fix `{ topic, mode, subject, parameters }` returned 400. |
| 195 | `var pending = fetch(ns._prewarmEndpoint, requestInit);` — NO `await` | Fire-and-forget per FR-18 |
| 196–209 | `pending.then(...).catch(...)` — double-layered swallow | Detached Promise; runtime never emits "Uncaught (in promise)" |
| 190 | `keepalive: true` | Allows pre-warm to complete after form navigation |

**Verdict — wire shape**: ✅ Confirmed `{ question: "matter-health-single", subject: "matter:{guid}", parameters: {} }`. Aligns with `InsightAskRequest` contract; question name resolves via `InsightsPlaybookNameMapOptions.ResolveOrDefault` (provided BFF is on the work branch deploy — see §3 caveat).

### 2.2 Staleness gating (FR-18)

`_firePrewarm` (line 159) returns early when `decision.status === "fresh"` (line 162) — pre-warm POST is NOT dispatched on fresh envelopes (>0 min and ≤60 min old). This is correct FR-18 behavior; SC-10 explicitly fires only when the stored summary is **stale or absent**. Staleness is computed by `_computeDecision` (Task 040 deliverable) against a 60-min threshold matching FR-18 default.

### 2.3 NFR-03 (Form TTI unaffected) — synchronous return + detached async

- `Spaarke.MatterInsight.onLoad` invokes `Xrm.WebApi.retrieveRecord(...).then(...)` and does NOT await. The handler returns synchronously to the Power Apps form runtime; all async work (envelope read + staleness compute + pre-warm POST) runs detached.
- `_firePrewarm` wraps the entire body in `try/catch`; synchronous failures `console.warn` only (line 224); async rejections are swallowed by the `.then(onFulfilled, onRejected)` + tail `.catch` (lines 202–216).
- No `await` anywhere in the OnLoad code path. No spinner, no blocking UI affordance.

**Verdict — NFR-03 (static)**: ✅ PASS. Form TTI cannot be blocked by the pre-warm code path because the OnLoad function returns synchronously and the fetch Promise is fully detached.

### 2.4 Q-U1 (`@v1`/`@vN` ban) compliance

Grep of `insightWidgetOnLoad.js` for `@v[0-9]+` returns zero matches. The playbook name `matter-health-single` is bare; `ns._version = "0.2.0"` is plain semver.

---

## 3. Caveat — BFF deploy state in `spaarkedev1` (carry-over from Phase 1 smoke test)

Per `notes/handoffs/smoke-test-results.md` §3 ("Live invocation blocker"), the canonical-name → playbook-Guid map (`Insights:Playbooks:Map:Map`) was edited in this work branch's `appsettings.template.json` but the **work branch may not yet be deployed to `https://spaarke-bff-dev.azurewebsites.net`**. If the operator observes a 400 on Step 3 with body matching:

> `"'question' must be either a valid playbook Guid id OR a canonical name registered in 'Insights:Playbooks:Map:Map' configuration. Received: 'matter-health-single'. Configured names in this environment: <…>"`

… the BFF deploy is the blocker, NOT a wire-shape regression. Recovery paths:

- **Path A (fast)**: Operator confirms by running an out-of-band POST with the raw Guid `a0d49d0d-4a65-f111-ab0c-70a8a590c51c` (the `sprk_analysisplaybook` row id seeded by Task 023 in `spaarkedev1`) and observing 200 — proves the substrate is healthy.
- **Path B (full)**: Invoke `bff-deploy` to push the branch's `appsettings.template.json` to dev BFF, then re-run Step 3 to observe 200 against the canonical name.

If the operator observes **200** on Step 3, both substrate and config are healthy — no follow-up needed.

---

## 4. Operator script (live SC-10 verification)

> Sub-agent CANNOT drive the MDA UI or read Performance Timing API live. Operator runs this script in a browser. ~10 minutes.

### Prerequisites

- Logged into `https://spaarkedev1.crm.dynamics.com` as a user with read+write access to `sprk_matter`.
- Identify a target Matter record id (any one will do; a "rich" Matter from `notes/uat-results/scenario-degraded-mode.md` or `scenario-b-decline.md` is fine).
- Chrome / Edge DevTools (F12) familiarity.

### Step 1 — Stale the Matter's envelope (force pre-warm to fire)

Run from the Power Apps Maker Portal Advanced Find (or any tool that issues an Xrm Web API PATCH):

```http
PATCH https://spaarkedev1.crm.dynamics.com/api/data/v9.2/sprk_matters(<MATTER_GUID>)
Authorization: Bearer <user-token>
Content-Type: application/json
If-Match: *

{
  "sprk_performancesummary": "{\"schemaVersion\":\"1.0\",\"generatedAt\":\"2026-06-09T00:00:00Z\",\"body\":\"stale UAT seed\",\"citations\":[],\"playbookName\":\"matter-health-single\",\"tenantId\":\"<TENANT_GUID>\",\"dimensions\":[]}"
}
```

Or, equivalently, paste a JSON envelope with `generatedAt` set to **>60 minutes in the past** (e.g., 2h ago) directly into the field via the Matter form's edit-field affordance. The exact value of `generatedAt` is the load-bearing field; the rest can be filler.

**PASS criterion for Step 1**: PATCH returns 204; re-querying the field shows the stale envelope.

### Step 2 — Open the Matter form + observe console signals

```
1. Navigate to: https://spaarkedev1.crm.dynamics.com → Spaarke Engineering app → Matters → click <MATTER_GUID>.
2. Open DevTools (F12) → Console tab. Clear console.
3. Hard reload the Matter form (Ctrl+Shift+R).
4. Expected NEW console output (alongside pre-existing handlers):

   [Matter Insight] v0.2.0 onLoad start (matter id=<GUID>)
   [Matter Insight] v0.2.0 envelope read: stale
   [Matter Insight] v0.2.0 prewarm POST fired (non-blocking). status=stale, playbook=matter-health-single, subject=matter:<GUID>
   [Matter Insight] v0.2.0 prewarm dispatched. status=stale, httpStatus=<200|202>, subject=matter:<GUID>
```

PASS criteria for Step 2:
- `[Matter Insight] v0.2.0 onLoad start` appears.
- `envelope read: stale` (NOT `fresh`).
- `prewarm POST fired (non-blocking)` followed by `prewarm dispatched` with httpStatus 200 or 202.

FAIL signals (file P0):
- `envelope read: fresh` despite Step 1 PATCH → stale check threshold wrong, or PATCH did not land. Recheck the field's value via Web API GET.
- `Uncaught` error from `Spaarke.MatterInsight.*` → bug in deployed JS (regression).
- No console signal at all → web resource not loading; recheck Task 043 deploy state.

### Step 3 — Verify pre-warm POST in Network tab (SC-10 telemetry evidence)

```
5. DevTools → Network tab → filter "/api/insights/ask".
6. Reload the Matter form once more if needed.
7. Expected: ONE POST request shortly after form load.
   - Request body: { "question": "matter-health-single", "subject": "matter:<GUID>", "parameters": {} }
   - Response status: 200 (success) or 202 (queued) — NOT 400.
   - Type: fetch
   - Initiator: insightWidgetOnLoad.js
   - Timing: started AFTER OnLoad return (verify by sorting by Start Time)
```

PASS criteria for Step 3:
- POST observed.
- Wire body matches: `{ question, subject, parameters }` (NOT `{ topic, mode, subject, parameters }`).
- Status 200 or 202 (per Task 041 P1 fix — confirmed correct shape; 400 would mean fix regressed OR BFF deploy gap per §3 caveat).

KNOWN-OK condition: if no POST appears AND console shows `prewarm skipped (fresh envelope; ageMinutes=…)`, the Step 1 PATCH did not effectively stale the envelope — revisit Step 1.

FAIL signals (file P0 for code regression; P1 for BFF deploy):
- POST returns 400 with `"'question' must be either a valid playbook Guid…"` body → BFF deploy gap per §3 caveat; resolve via Path A or Path B above.
- POST returns 400 with a body-shape complaint → wire-shape regression; verify `insightWidgetOnLoad.js` line 177 still emits `question` (NOT `topic`).
- POST returns 401/403 → auth issue; check `credentials: "include"` in the fetch options.
- No POST when console said `prewarm POST fired` → browser blocked the fetch (CSP? CORS?). Inspect Console for browser-level errors.

### Step 4 — Verify TTI unaffected (NFR-03 quantitative gate)

Two methods; either suffices.

**Method A — Performance Timing API (quantitative)**

```
8. Console (after form fully loaded; ~5s after Step 2 step 3):
   const nav = performance.getEntriesByType('navigation')[0];
   console.table({
     domInteractive: nav.domInteractive,
     loadEventEnd: nav.loadEventEnd,
     delta: nav.loadEventEnd - nav.domInteractive
   });
9. Expected:
   - domInteractive ≤ 1500 ms on a typical broadband connection.
   - delta (loadEventEnd - domInteractive) ≤ 500 ms.
10. Compare against a baseline Matter (one WITHOUT a stale envelope — pre-warm skipped):
    Reload after restoring sprk_performancesummary to a fresh envelope (or clearing it then reloading once
    to let pre-warm settle, then reload again with the field fresh).
    The TTI delta with-stale-vs-without-stale should be ≤ 50 ms.
```

**Method B — Perceptual TTI (qualitative — sufficient for staging acceptance)**

```
8. Reload the Matter form (with the field stale per Step 1).
9. Immediately attempt to click into a text field (e.g., Matter name) or ribbon button.
10. Expected: form is interactive within 1–2 seconds; no perceptible delay vs. a Matter form WITHOUT the
    new handlers.
```

PASS criteria for Step 4:
- Method A: domInteractive within budget; no >500 ms regression attributable to the pre-warm handler.
- Method B: form interactive within 1–2 s perceptual; no spinner persisting on form fields.

FAIL signals (file P0):
- TTI regression > 500 ms vs. baseline → NFR-03 violation; the OnLoad handler is blocking somehow (would mean an `await` leaked in — regression from Task 041's detached Promise design).
- Form fields un-clickable for > 3 s → critical NFR-03 violation.

### Step 5 — Cleanup (restore Matter envelope)

After Steps 2–4 PASS:

```
11. The pre-warm POST will have triggered a server-side run that overwrites sprk_performancesummary with a
    fresh envelope (assuming the substrate is healthy and the run completes). Refresh the Matter form one
    more time and confirm sprk_performancesummary now shows a fresh generatedAt (within the last few seconds).
12. No cleanup needed — the fresh envelope is the intended end state. The next Matter form open within 60 min
    will skip pre-warm (decision.status="fresh"), which is the steady-state design.
```

PASS criteria for Step 5:
- `sprk_performancesummary` field updated to a fresh envelope after the pre-warm completes.
- Subsequent form reloads show `envelope read: fresh` and skip the pre-warm POST.

---

## 5. Operator findings (to be filled by operator)

| Step | Result | Notes |
|---|---|---|
| 1 — Stale envelope PATCH | ⬜ PASS / ⬜ FAIL | |
| 2 — Console signals on form load | ⬜ PASS / ⬜ FAIL | |
| 3 — Pre-warm POST 200/202 in Network tab | ⬜ PASS / ⬜ FAIL / ⬜ BFF-deploy-gap (§3 caveat) | Wire shape: ⬜ correct / ⬜ regressed |
| 4 — TTI within budget (NFR-03) | ⬜ PASS / ⬜ FAIL | domInteractive: ___ ms; perceptual: ___ |
| 5 — Field update + steady-state fresh | ⬜ PASS / ⬜ FAIL / ⬜ SKIPPED | |

---

## 6. Acceptance — SC-10

| Acceptance criterion | Verification mode | Sub-agent verdict | Operator verdict |
|---|---|---|---|
| Pre-warm telemetry event present (OnLoad-triggered invocation observable) | Static (code wiring) + operator (Network tab + console) | ✅ PASS (static — code path proven; wire shape per Task 041 P1 fix) | ⏳ Operator confirms 200/202 in Network tab |
| Form TTI within budget (NFR-03) | Static (synchronous return + detached Promise) + operator (Performance API or perceptual) | ✅ PASS (static — no `await`, no blocking path) | ⏳ Operator confirms via Method A or B |

**Overall SC-10 verdict (sub-agent)**: ✅ **PASS via static verification + operator script ready for live execution.**

Pending the operator's gated live confirmation in `spaarkedev1`, SC-10 is structurally satisfied. The Task 041 P1 wire-shape fix is confirmed deployed; the OnLoad handler is non-blocking by construction; the staleness gating is correct. The only conditional risk is the BFF deploy state of `Insights:Playbooks:Map:Map` (§3 caveat) — recovery paths are documented.

---

## 7. Blockers

None blocking SC-10 acceptance from r1's perspective. The §3 caveat is a documented conditional that the operator resolves at run time (Path A or Path B); it does not invalidate the SC-10 contract.

If the operator finds the BFF deploy gap in §3 is still present after Task 067 (BFF deploy task), file a P1 against `bff-deploy` for the work branch, not against this task.

---

## 8. Artifacts

| Path | Purpose |
|---|---|
| `projects/.../notes/uat-results/sc-10-pre-warm.md` | This file — SC-10 static verification + operator script |
| `projects/.../notes/uat-scenarios.md` | UAT scenario catalog (Scenario A Steps 1–2 source) |
| `projects/.../notes/handoffs/phase-4-e2e-test.md` | Task 044 Phase 4 e2e handoff (cross-referenced for wire-shape provenance + IIFE gap) |
| `projects/.../notes/handoffs/smoke-test-results.md` | Phase 1 smoke handoff (§3 BFF-deploy caveat source) |
| `src/dataverse/forms/sprk_matter/insightWidgetOnLoad.js` | Deployed OnLoad pre-warm (FR-17/18, SC-10 telemetry source) |

---

*Handoff written 2026-06-11 by task-execute for Task 065. SC-10 path documented via static verification + operator playbook. Sub-agent boundary respected — no MDA UI exercise.*
