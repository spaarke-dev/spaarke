# Workstream B — Deploy + Verify record (task 025)

**Date**: 2026-08-06
**Phase**: 3 B — Proactive follow-ons (tasks 020–025)
**Status**: Deployed ✅ · programmatic verification ✅ · **owner E2E verification pending** (live-UI chip behavior)

---

## What shipped in Workstream B

| Task | FR | What | Where |
|---|---|---|---|
| 020 | B1/C3 | Closed `WidgetContextType` set on widget metadata | shared lib (deployed prior) |
| 021 | B2/D11 | `sprk_contexttypetags` column + `Binding.ContextTypeTags` + seed + Reanalyze binding | Dataverse + BFF (deployed prior) |
| 022 | B3/B5 | Grounded suggest turn — `POST /api/ai/chat/sessions/{id}/suggest` + `AssistantSuggestionService` + `SUGGEST-FOLLOWUPS` Action + client trigger | BFF + code page |
| 023 | B4 | Manual "Refresh suggestions" control (force re-fire) | code page |
| 024 | B6 | Dev-only proactive-selection trace (`window.__sprkSuggestTrace`) | code page |

---

## Deploy record

| Artifact | Target | Result |
|---|---|---|
| **BFF** `spaarke-bff-dev` | `rg-spaarke-dev` | Deployed (022); publish 48.33 MB (on baseline); hash-verified; `/healthz` 200 |
| **Code page** `sprk_spaarkeai` | spaarkedev1 (`5206a442-3451-f111-bec7-7ced8d1dc988`) | Updated + published; bundle 5153 KB (022+023+024) |
| **Catalog** Action `suggest-followups` | spaarkedev1 | `64505c5b-5191-f111-b8db-7ced8ddc4cc6` (Prompted, Fast, temp 0.2) |
| **Catalog** Binding `assistant-suggest` | spaarkedev1 | `c58b1b57-5191-f111-b8db-7ced8ddc4a05` (enabled → Action) |

---

## Programmatic verification (done by the agent)

| Check | Result |
|---|---|
| Code page web resource updated + published | ✅ |
| BFF `POST …/suggest` route live + auth-gated | ✅ `401` unauthenticated (route registered) |
| BFF `/healthz` | ✅ `200` |
| Catalog round-trips (binding → action; prompt 3620ch, schema 1411ch) | ✅ |
| Bundle contains the feature (`/suggest`, "Refresh suggestions") | ✅ present |
| Dev trace stripped from prod bundle (`sprk:suggest-trace`, `__sprkSuggestTrace`) | ✅ 0 occurrences (inert in prod) |
| BFF unit tests (FilterByContextType + AssistantSuggestionService) | ✅ 14/14 |
| Client seam tests (proactive-suggest 5 + refresh 2) | ✅ 7/7 |

**Automated behavior coverage** (the seam tests assert the runtime contract):
- **≤3 chips** — `AssistantSuggestionService` caps at 3 (unit test) + client slices to 3.
- **Once per tab / no re-fire on switch-back (NFR-02)** — `proactive-suggest.e2e` asserts one `/suggest` per tabId, re-fire for a different tabId, guard on missing contextType.
- **Closed-catalog guard** — off-catalog `targetBindingId`s dropped (unit test).
- **Manual refresh** — `refresh-suggestions.e2e` asserts the control appears with chips and force-re-fires for the active tab.
- **Dev trace** — `proactive-suggest.e2e` asserts `window.__sprkSuggestTrace` populates in dev.

---

## Owner E2E verification checklist (live UI — pending)

Same handoff pattern as FR-A (task 013): the live-UI chip behavior needs a human in the Power Apps workspace. Open the SpaarkeAi Assistant with a workspace tab and confirm:

1. **Content-specific chips on tab open** — open a **document** or **summary** tab (these carry server-derived content today). Expect ≤3 chips SPECIFIC to that document (e.g. an NDA → NDA-shaped chips), not a fixed per-type menu. Open a different document → different chips.
2. **Once-per-tab (NFR-02)** — switch away to another tab and back. Expect **no** additional `/suggest` call (check the browser Network tab OR `window.__sprkSuggestTrace` — should still show one entry for that tabId).
3. **Manual refresh** — with chips shown, click **"Refresh suggestions"**. Expect a new `/suggest` call and the chips replace.
4. **Dev trace** — in the browser console, `window.__sprkSuggestTrace` lists `{ tabId, contextType, trigger, chips[] }` entries; each chip carries a `reason`. (`console.debug("[sprk:suggest-trace]", …)` also logs.)
5. **Dark mode** — no visual regressions on the refresh control (Fluent subtle button + icon).

### Known scope boundary — EMAIL tabs
Success Criterion 2 names an **email tab**. Email content-visibility is **Workstream C** (tasks 040–042, not yet built): the email widget does not emit compact content server-side until C. So **email-tab** suggestions will be thin/context-type-generic until C lands; **document/summary** tabs exercise the full content-specific path today. This is the same boundary the FR-A owner UAT noted ("summarize this → email" gated on C). Re-verify email-tab chips after Workstream C deploys (task 043).

---

## Result

Workstream B is **deployed and programmatically verified**. Marking 025 `✅*` (deployed + smoke/automated-verified; owner E2E of the live-UI proactive behavior pending, tracked in this doc). Phase B complete pending that owner confirmation.
