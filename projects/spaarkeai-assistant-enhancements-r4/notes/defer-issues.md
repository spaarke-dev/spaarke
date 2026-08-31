# Deferred Issues — spaarkeai-assistant-enhancements-r4

> Tracks deferred work + defects surfaced during the project. Per the deferral protocol (`/project-defer-issue-tracking`), each open entry is ALSO filed as a GitHub Issue at PR time for team visibility. **One open deferral**: D-UAT-01 (structural `surfaces`-aware projection — interim mitigation shipped). D-024-01 was fixed in-project.

---

## D-024-01 — Typed SSE `suggestions` endpoint guard — ✅ RESOLVED (not deferred)

| Field | Value |
|---|---|
| **Raised by** | task 024 (E2 eval cases), 2026-08-17 |
| **Surface** | `src/server/api/Sprk.Bff.Api/Api/Ai/ChatEndpoints.cs` (the `suggestions` SSE emission) |
| **Concern** | No direct regression guard that the `ChatEndpoints` `/messages` path emits the **typed two-kind** `suggestions` payload (`ChatSseSuggestionsData`/`ChatSseFollowupItem[]`) rather than the retired ungrounded free-string generator. |
| **Resolution** | **Fixed, not deferred** (owner steer 2026-08-17: don't defer without a better owner — there is none). The inline emit block was extracted to the testable `ChatEndpoints.BuildTypedFollowups(missingContextActionChips, grounded)` (behavior-preserving refactor) and guarded by `tests/unit/Sprk.Bff.Api.Tests/Api/Ai/ChatEndpointsTypedFollowupsTests.cs` (via `InternalsVisibleTo` — the same precedent as this file's other internal tests): asserts the §9a order (action → capability → question), the typed-kind mapping, that a capability with a null binding id is DROPPED (no dead-end reaches the wire), and that an empty result emits nothing (meaningful absence). Full BFF test suite green; no new packages / CVEs; publish size unchanged (pure refactor). |
| **GitHub Issue** | n/a — resolved in-project |

---

## D-UAT-01 — `SelectTextProjectable` ignores `sprk_surfaces` (chat-loop projection over-includes) — 🔲 DEFERRED (design change)

| Field | Value |
|---|---|
| **Raised by** | R4 UAT 2026-08-18 (daily-briefing raw-JSON investigation) |
| **Surface** | `src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/ConsumerRoutingService.cs` — `SelectTextProjectable` (~L320-330) + `QueryTextProjectableCandidatesAsync` |
| **Concern** | The chat-loop projectable catalog opts in a Binding on the SOLE criterion of a non-empty `sprk_tooldescription` — it **does not honor `sprk_surfaces`**. So bindings tagged for other surfaces leak into the agent tool set + the 021 suggestion proposer: the `daily-briefing-narrate` email leg's own description literally says *"Scheduler surface only - not offered to the chat loop"* yet it was projectable, and compose-only capabilities (`compose-make-concise` `workspace,compose`; `compose-defined-terms` `context,compose`; `compose-rewrite-instruction`; `compose-draft-alternative`) are projectable to the assistant chat regardless of surface. **Concrete failure**: a capability whose output isn't chat-renderable (Informational structured payload) gets selected and dumps raw JSON (the UAT bug), or a compose-surface op is offered in a non-compose chat. |
| **Interim mitigation (shipped)** | Nulled `sprk_tooldescription` on both `daily-briefing-narrate` bindings to pull them out of the loop (the reported raw-JSON case). This is a per-binding patch, not the structural fix. |
| **Proper fix (deferred — needs design)** | Make `surfaces` meaningful: `ListTextProjectableBindingsAsync` should take the **current surface** (assistant / compose / workspace / context) and filter to bindings whose `surfaces` is empty (all) OR contains that surface. Requires threading the active surface through the projection call sites (agent factory + suggestion proposer) — a broader change with regression surface across compose capabilities, so NOT rushed mid-UAT. |
| **Why deferred vs fixed-now** | The structural fix touches the ADR-039 projection contract + multiple call sites and risks excluding currently-working compose capabilities without the surface-context plumbing. The interim null-toolDescription mitigation resolves the user-visible defect safely. No known better owner — R4 owns the projection surface; carry into a follow-up task or the next assistant project. |
| **GitHub Issue** | TBD — file at next push per `/project-defer-issue-tracking`. |
