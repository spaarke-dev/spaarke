# NFR-02: Create Matter wizard create latency, before and after (task 021)

> **STUB — no measurement has been taken.** Written by the code-part session (2026-11-04), which creates nothing on dev. The live part (POML steps 1 and 8) is run from the main session after the owner's OK, because each sample creates a real matter on `spaarkedev1`.
> Spec: NFR-02 (the wizard create p95 may rise by at most **+500 ms**; above that, STOP and escalate, profile caching is an owner decision). Design §4.

## Proposed method

**What is measured.** "Finish click to record id": the time from the click on the wizard's Finish button to the moment the new matter's id is known to the wizard, that is, the end of `MatterService.createMatter` step 1 (before the access sync, the file upload and the follow-on steps, which are identical before and after). Same definition for both runs.

**Instrumentation (no code change in the bundle, so the "before" bundle stays the deployed one).** A DevTools/Playwright snippet run in the code page's frame:

1. `click` listener on the Finish button (capture phase) records `t0 = performance.now()`.
2. A `PerformanceObserver` on `resource` entries records the create request: **before** — `POST …/api/data/v9.*/sprk_matters` (Xrm.WebApi), after the wizard's pre-create reads (the Matter Type code read, the nav-prop metadata read, the `field-mappings/profiles/{source}/sprk_matter` GET and the profile's source reads); **after** — `POST …/api/v1/child-records/sprk_matter[?mappingSourceEntity=&mappingSourceId=]`.
3. `t1` = that create request's `responseEnd`. Sample = `t1 − t0`.
4. Cross-check from the Network panel: the same request's total time, and `x-ms-request-id` / the server `[CHILD-RECORD]` log line for the server-side share.

**Controls (same for both runs).** Same user, same business unit, same browser profile and network; the same parent (an association with an ACTIVE Mapping Profile onto `sprk_matter`, so the server does real mapping work) AND the same payload shape (name, Matter Type, Practice Area, one assignee; no files). One discarded warm-up create per run. At least 20 timed creates per run, interleaved over a single sitting (no deploy or App Service restart between samples; note the BFF instance warm-up state). Record raw samples, the run's start time, and the bundle/BFF build identifiers.

**Statistic.** p95 by nearest rank over the n samples (n = 20: the 19th smallest), plus median and max, for before and after. Delta = after p95 − before p95. Report n, the raw numbers and the hash of each bundle.

**Order.** BEFORE the BFF `sprk_matter` route and the new wizard bundle are deployed (the current dev bundle is the baseline); AFTER once both are on dev. The "before" run is not redoable once the bundle is replaced; if it is lost, rolling back to the saved previous bundle regenerates it.

**Cleanup.** The 2 × (≥ 20 + 1) test matters are test data on dev. Name them with a fixed prefix (`NFR02-before-…`, `NFR02-after-…`) so they can be found; deleting them is a write to existing rows and needs the owner's OK.

## How the 2026-11-05 run differed, and how a browser run must observe (fix round 1)

The stub's in-frame `PerformanceObserver` cannot see the OLD path's create: `Xrm.WebApi` sends `POST sprk_matters` from the PARENT frame, not the code page's iframe, so a `resource` entry never appears in the frame's timeline. The 2026-11-05 run therefore used a **scripted HTTP replay** of each side's network sequence (see `task-021-live.md`, deviation 1), not this method. A future browser run must observe at the **top window** (the observer registered in the page that sends the request), or use Playwright `requestfinished` (or `page.on("response")` with `request.timing()`) so both the parent-frame Xrm create and the iframe's BFF call are seen.

## Results

The live results are in `task-021-live.md` (verdict FAIL: p95 +2634 ms against the +500 ms budget).

| Run | Bundle / BFF build | n | p50 (ms) | p95 (ms) | max (ms) |
|---|---|---|---|---|---|
| Before | _not measured_ | | | | |
| After | _not measured_ | | | | |
| **Delta (p95)** | | | | | |

Verdict: _pending_ (within +500 ms, or escalated).
