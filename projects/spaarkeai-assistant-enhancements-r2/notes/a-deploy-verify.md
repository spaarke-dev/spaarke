# Deploy + Verify — Workstreams E + A (tasks 002 + 013)

**Date**: 2026-08-05
**Target**: SPAARKE DEV 1 (`spaarkedev1.crm.dynamics.com`) + Azure `spaarke-bff-dev` (Spaarke Dev subscription)

## What was deployed

| Component | Ships | Result |
|---|---|---|
| BFF (`spaarke-bff-dev`) | task 012 (FR-A3/A4 focus-stamp prefer + ADR-015 narrowing) | ✅ `Deploy-BffApi.ps1`; package 48.25 MB (< 60 ceiling, < 49.63 baseline); **4/4 critical files SHA-256 verified**; `/healthz` 200 |
| SpaarkeAi code page (`sprk_spaarkeai`) | tasks 001 (FR-E1 banner removal), 010 (FR-A1 subscriber), 011 (FR-A2 decorate) | ✅ `Deploy-SpaarkeAi.ps1` → web resource `5206a442-…` updated + published; bundle 5152 KB |

## Automated smoke checks (done)
- BFF: `/healthz` 200, `/ping` 200, `/api/documents/test/preview-url` → **401** (route registered, complete package — not 404).
- Code-page bundle: `activeContext` **present** (task 011 reached bundle); `suggestion-banner` testid **absent** (task 001 banner removed); `suggestion-card-` **present** (rerun-analysis card retained per deviation).

## Manual verification — OWNER (pending)
Owner elected to verify manually. In the SpaarkeAI workspace on spaarkedev1:

1. **FR-A (Success Criterion 1)** — Open the workspace, open/focus an **email** tab, ask the Assistant **"summarize this"**. Expected: it resolves to the **focused email**, not the most-recently-updated tab. (Server now prefers the focus-stamp over the UpdatedAt heuristic.)
2. **FR-E regression** — Confirm: the "You have N new notifications" banner is **gone** from the Assistant pane; the **Communications** widget badge/toast still works; the **Daily Briefing** widget still renders; the reactive "Suggested Next Steps" chips still appear. (Bundle + code-review confirmed preservation; this is the live confirmation.)
3. Note: full email-subject/sender visibility (Success Criterion 3) is **Workstream C** (not yet built) — the Assistant seeing *which* email is focused (A) lands now; stating its subject/from/thread (C) comes later.

Report back and we'll mark verification closed.

---

## ✅ OWNER VERIFICATION CLEARED — 2026-08-06
Owner confirmed Phase A (+ E) E2E verification. Task 013 (and 002) move `✅*` → `✅`. FR-A focus-stamp + FR-E banner-removal regression accepted. Full email-subject/sender visibility remains Workstream C (tasks 040–043).
