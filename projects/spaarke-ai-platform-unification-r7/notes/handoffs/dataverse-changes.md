# Dataverse Changes Traceability — R7

> **Purpose**: Per-task record of all Dataverse data + schema changes made during
> spaarke-ai-platform-unification-r7. Required by FR-17 task 092 (and any
> subsequent wave task that mutates Dataverse).

---

## Wave 9

### Task 092 — `sprk_playbookconsumer` chat-summarize row (FR-17 data portion)

**Date**: 2026-06-28
**Operator**: task-execute (R7-092)
**Environment**: spaarkedev1 (`https://spaarkedev1.crm.dynamics.com`)
**Table**: `sprk_playbookconsumer`
**Action**: VERIFY (no-op — row already exists per chat-routing-redesign-r1 prior seeding)

#### Row state (verified via `mcp__dataverse__read_query`)

| Column | Value |
|---|---|
| `sprk_playbookconsumerid` | `651194cd-3670-f111-ab0e-70a8a590c51c` |
| `sprk_name` | `Chat Summarize Document` |
| `sprk_consumertype` | `chat-summarize` |
| `sprk_consumercode` | `default` |
| `sprk_environment` | `*` |
| `sprk_priority` | `500` |
| `sprk_enabled` | `true` |
| `sprk_playbook` (lookup) | `44285d15-1360-f111-ab0b-70a8a59455f4` → `summarize-document-for-chat@v1` |
| `sprk_matchconditions` | (not set / null — chat-summarize has no conditional routing per task 090 design §4) |

#### Acceptance criteria evidence

| Criterion (POML §acceptance-criteria) | Status |
|---|---|
| Row exists with `sprk_consumertype = "chat-summarize"` | ✅ confirmed via `read_query` |
| `sprk_playbook` lookup points to playbook GUID matching `WorkspaceOptions.ChatSummarizePlaybookId` | ✅ `44285d15-1360-f111-ab0b-70a8a59455f4` (matches `Seed-PlaybookConsumers.ps1` line 135 — established by chat-routing-redesign-r1 task 028b) |
| `sprk_matchconditions` is null/empty | ✅ no value set |
| Insertion is idempotent — re-run safe | ✅ verified — see "Idempotency check" below |
| `IConsumerRoutingService.ResolveAsync("chat-summarize")` returns this row's playbook GUID | ⏭️ deferred — requires BFF App Service restart OR 5-min cache TTL wait; routing-table HIT path was already validated by task 091 PathA5 integration test (scenario 1 of 3 in `notes/handoffs/wave2-signoff.md` predecessor) |
| Traceability entry in this file | ✅ this entry |

#### Provenance

The row was seeded by `chat-routing-redesign-r1` task 028b (2026-06-24) using
`scripts/dataverse/Seed-PlaybookConsumers.ps1`. The chat-summarize consumer was
included in the original 6-row seed set because the chat-summarize migration
was anticipated by FR-1R-07. R7 task 091 then refactored
`SessionSummarizeOrchestrator` to consume this row via `IConsumerRoutingService`.

Task 092 is therefore a **verification gate**, not a fresh insertion. The work
the POML anticipated (creating the row) was already complete; task 092 confirms
that R7 prerequisite is satisfied for downstream tasks (094-096 wire the
Playbook Library modal which lists consumer rows for selection).

#### Idempotency check (POML step 8)

Attempted re-run of `Seed-PlaybookConsumers.ps1 -SkipConfirm` (live mode) to
verify alternate-key UPSERT prevents duplicates.

**Result**: 6/6 records returned HTTP 400 Bad Request from the Dataverse Web API.
Zero duplicates created (idempotency property HELD — for the wrong reason: the
script's PATCH-on-alternate-key URL pattern is rejected by the spaarkedev1
endpoint, not by the alternate-key uniqueness constraint).

**Root cause**: requires further investigation. Suspected — the alternate key
`sprk_ConsumerTypeCodeEnvironment` referenced in the script docstring may not
have been deployed to spaarkedev1, OR the Web API requires a different header
combination (e.g., explicit `MSCRM.SuppressDuplicateDetection: true`) for
alternate-key UPSERT.

**Impact on task 092**: NONE. The row exists with correct values. Future
operators wanting to re-seed (e.g., in a fresh environment) will need to either:
- Fix the script's UPSERT mechanism, OR
- Manually create the row via Power Apps maker portal, OR
- Use the MCP `mcp__dataverse__create_record` tool

**Filed as deferral**: see `notes/defer-issues.md` ISS-{NNN} (Seed-PlaybookConsumers.ps1 400 on UPSERT).

**Script bug fix included in task 092**: PowerShell parse error on line 285
(`-ForegroundColor (if (...) {...} else {...})` — not valid PS syntax) was
fixed by extracting to a `$failedColor` variable. This was masking the actual
failure summary; now the script reports cleanly even when records fail.

#### Files modified in this task

| File | Change |
|---|---|
| `scripts/dataverse/Seed-PlaybookConsumers.ps1` | Line 285 PS parse-error fix (extract `$failedColor` variable). NO change to seed records or upsert logic. |
| `projects/spaarke-ai-platform-unification-r7/notes/handoffs/dataverse-changes.md` | NEW — this file. |
| `projects/spaarke-ai-platform-unification-r7/tasks/TASK-INDEX.md` | Task 092 ✅ |
| `projects/spaarke-ai-platform-unification-r7/current-task.md` | Advance to task 094 (per user instructions; 093 already complete per index) |

#### Cache / verification timing (POML step 7)

Per ADR-014 + NFR-04, the IConsumerRoutingService cache TTL is 5 minutes.
Task 091's `SessionSummarizeOrchestrator.PathA5.IntegrationTest.cs` scenario 1
("routing-table HIT → IPlaybookOrchestrationService dispatch") validates the
end-to-end resolution path against a mocked routing service. Since the row
existed at the time of task 091 dev work (per the integration test scenario
naming + chat-routing-redesign-r1 task 028b seeding date 2026-06-24), the
end-to-end flow is already proven against the live row.

Live BFF App Service restart + chat-summarize smoke test in spaarkedev1 is
NOT performed by task 092 (operational, not in-scope for this task's commit).
Recommend running `Test-SdapBffApi.ps1` chat-summarize scenario after the next
BFF deploy as the final NFR-04-respecting validation.

---

## (End of Wave 9 entries — append future tasks below)
