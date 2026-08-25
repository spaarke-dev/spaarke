# Deferred Issues — spaarkeai-assistant-enhancements-r4

> Tracks deferred work + defects surfaced during the project. Per the deferral protocol (`/project-defer-issue-tracking`), each open entry is ALSO filed as a GitHub Issue at PR time for team visibility. **No open deferrals** — the one candidate (D-024-01) was fixed in-project rather than deferred (there was no better owner).

---

## D-024-01 — Typed SSE `suggestions` endpoint guard — ✅ RESOLVED (not deferred)

| Field | Value |
|---|---|
| **Raised by** | task 024 (E2 eval cases), 2026-08-17 |
| **Surface** | `src/server/api/Sprk.Bff.Api/Api/Ai/ChatEndpoints.cs` (the `suggestions` SSE emission) |
| **Concern** | No direct regression guard that the `ChatEndpoints` `/messages` path emits the **typed two-kind** `suggestions` payload (`ChatSseSuggestionsData`/`ChatSseFollowupItem[]`) rather than the retired ungrounded free-string generator. |
| **Resolution** | **Fixed, not deferred** (owner steer 2026-08-17: don't defer without a better owner — there is none). The inline emit block was extracted to the testable `ChatEndpoints.BuildTypedFollowups(missingContextActionChips, grounded)` (behavior-preserving refactor) and guarded by `tests/unit/Sprk.Bff.Api.Tests/Api/Ai/ChatEndpointsTypedFollowupsTests.cs` (via `InternalsVisibleTo` — the same precedent as this file's other internal tests): asserts the §9a order (action → capability → question), the typed-kind mapping, that a capability with a null binding id is DROPPED (no dead-end reaches the wire), and that an empty result emits nothing (meaningful absence). Full BFF test suite green; no new packages / CVEs; publish size unchanged (pure refactor). |
| **GitHub Issue** | n/a — resolved in-project |
