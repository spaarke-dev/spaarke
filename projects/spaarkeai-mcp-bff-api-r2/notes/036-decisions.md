# Task 036 — task write tools (`spaarke_create_task`, `spaarke_complete_task`, FR-07 write half) and the #3181 fix (D-3181)

Two commits: (A) the policy fix for #3181, (B) the two tools. Nothing was closed on GitHub, and nothing was pushed.

## A. #3181: `event.attach_regarding` is Read + AppendTo (owner option (b), D-3181)

| Piece | Where |
|---|---|
| The one policy line | `src/server/shared/Spaarke.Core/Auth/OperationAccessPolicy.cs`: `["event.attach_regarding"] = AccessRights.Read \| AccessRights.AppendTo` |
| Comments that named AppendTo alone | `EventsCapability.cs`, `Api/Events/EventEndpoints.cs`, `IEventsCapability.cs`, `ExternalProjectDataEndpoints.cs`, `OperationPolicyCharacterizationTests.cs` |
| Pinned expectations | `OperationAccessPolicyCompletenessTests` (the InlineData), `EventEndpointsAuthorizationContractTests` (eight grants), `EventRoutesContractBaselineTests` (one), `SecureChildOwnershipEndpointTests.MayCreateEventsUnder` |
| Role-privilege guard | `FeatureRolePrivilegeGuardTests`: an `EventRegardingRead` set (the Read privileges the regarding types need) and two `For(...)` rows at Deep |
| New regression test | `tests/integration/regression/Issue3181_EventCreateRegardingNeedsReadTests.cs` (5 tests: must-fire on REST and on the capability, byte-identical deny body, must-not-fire on both) |

**The deny bytes are unchanged.** The 403 is the fixed `InsufficientRightsDetail` with `reasonCode` `sdap.access.deny.insufficient_rights`; it names no right. The only text that changed is the server log line for the requirement ("Read, AppendTo"). The regression test asserts the AppendTo-only body equals the no-rights body and names neither right.

**Proof it fails before the fix:** with the policy line reverted, 3 of the 5 regression tests fail (both must-fire tests and the byte-identity test's control); with it restored all 5 pass.

### D-036-scan: the completeness scan could not see a qualified constant (a pre-existing failure, fixed here because the line it pins moved)

`SourceScan_DiscoversKnownCallSiteOperation("event.attach_regarding")` was ALREADY failing on this branch before the change: task 006 moved the operation into `EventsCapability.AttachRegardingOperation` (a constant in another file) and the scan only resolved constants in the call site's own file. `DiscoverCallSites` now reads every source once and resolves `AddXAccessFilter(Type.Constant, ...)` across files (`FilterQualifiedConstPattern`). Must-fire and must-not-fire are the existing completeness theory rows.

## B. The tools

### Shape
Two thin adapters in `Api/Mcp/LegalOperations/Tasks/`, found by `[McpArea]`; no DI registration, no new store, no new package.
- `spaarke_create_task(subject, idempotencyKey, description?, dueDate?, priority?, regardingRecordType?, regardingRecordId?)` -> `IEventsCapability.CreateAsync` with exactly `IHttpContextAccessor.HttpContext`.
- `spaarke_complete_task(taskId, idempotencyKey)` -> `IEventsCapability.CompleteAsync`.
- Both: `Destructive = false`, `Idempotent = true`, required string `idempotencyKey`; both names added to `McpToolAuthorization.ModelCallableWrites` (the closed list; startup refuses an unlisted write).
- Neither copies a check. The tool checks argument shape and length only (closed argument set, `yyyy-MM-dd`, whole numbers, GUID text, subject <= 400, description <= 2,000, key <= 200). The capability decides the Create privilege, Read + AppendTo on the regarding record, the shape rules and the complete's Write; the tools map its status to `McpToolResults.FromError` with the REST `reasonCode` and status and fixed text (ADR-015: no record name, id or refusal detail).
- Retry key: `McpIdempotency.RunOnceAsync` (task 031), per tenant + caller + key, fingerprint mismatch is `KeyReused`, a refused call is not remembered. No new store (the brief's stop condition did not apply).
- Results: create `{ id, subject, dueDate, status, link, replayed }`; complete `{ id, status, link, alreadyCompleted, replayed }`. No description, owner or audit field.

### D-036-complete-key: `spaarke_complete_task` takes an `idempotencyKey` (a deviation from the POML's text)
The POML described complete as idempotent by status alone. `McpToolAuthorization.ValidateTool` (task 031) requires every model-callable write to declare a required string `idempotencyKey`, and spec FR-07/NFR-06 say the same, so the tool takes one. Two layers: the key replays the first answer for the same task without calling the capability, and a completed task completed again (new key) is still success.

### D-036-already-completed: the capability's 400 is turned into success by reading the task back
`CompleteAsync` refuses a complete of a task that is not open work (`InvalidTransition`, REST 400). The tool treats exactly that one status specially: it reads the task back through `IEventsCapability.GetAsync` (the caller's own Read) and answers success with `alreadyCompleted: true` only when the status is Completed. A cancelled or otherwise closed task keeps the refusal (`events.invalid_transition`, 400, the capability's text). A caller with no Read gets the uniform not-found from the capability before this point, so the read-back discloses nothing.

### D-036-fault codes
Faults the REST route answers as a bare 500 carry this host's own codes: `events.create_failed`, `events.complete_failed`; the complete's 400 carries `events.invalid_transition` (REST has no reason code for it).

## Tests (KEEP paths only)
| File | Path | Cases |
|---|---|---|
| `tests/integration/data-mutation/Mcp/Tasks/TaskWriteToolsTests.cs` | data-mutation | create writes the same columns and values as POST /api/v1/events; retried key writes once and answers the same id; different key is a second task; same key from another caller is not a retry; same key with different inputs is `KeyReused`; a refused create is not remembered; 8 wrong-argument shapes refused before any access question; regarding type without an id is the capability's 400; complete once; complete retried replays without calling the capability; already completed with a new key is success and writes nothing; cancelled keeps the refusal; 5 wrong-argument shapes |
| `tests/integration/auth/Mcp/Tasks/TaskWriteToolsAuthTests.cs` | auth | B completing A's private task is the uniform not-found, equal to an absent id, never read, nothing written; Read-only complete is 403 with the capability's reason; create without the privilege is 403; create under a record with None, AppendTo only (#3181) or Read only is 403 and names nothing; both tools with no signed-in caller (3 shapes) ask Dataverse nothing |
| `tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.Mcp.Tasks.cs` | ledger | rows for `CreateTaskTool` and `CompleteTaskTool` |
| `McpCallerPathTests`, `McpHostContractTests` | pinned | the two tools' argument sets and the tools/list |

Not tested through the wire (known limit, K-036-1): the tools are driven through `RunAsync` over the real capability and the real create service, not through a JWT-bearing MCP route, because `TaskToolsHost` has no write path and the capability's write seams are the same ones the REST tests already drive. `McpHostContractTests` pins that both tools are listed; the startup validator is exercised by the real module build in the host contract tests.

## Runs
- `Sprk.Bff.Api.Tests` (events, MCP, access, secure-child, external-event and Issue3181 classes): 3,587 passed, 1 failed (the pre-existing deprecation scan below), 4 skipped. The new tests (`TaskWriteToolsTests` and `TaskWriteToolsAuthTests`, theory rows counted): 33 passed.
- `Spaarke.ArchTests`: 2,898 passed, 1 failed (the pre-existing ADR-010 count below).
- MCP conformance host (`run-conformance.sh`, `MCP_SKIP_WIDGET=1`): the only unexpected failure is `caching:sep-2549-resources-read-caching-hints`, the check the script says fails when the widgets are left unbuilt; every tool check passes.
- Publish size (Release, framework-dependent, PDBs as published, Optimal zip), fresh `origin/master` (60933c82be) vs this branch's working tree: 42.683 MB -> 42.371 MB compressed (-0.311 MB), 144.89 -> 144.17 MB uncompressed. Neither embeds the widgets (no `dist`). Under +5 MB, 55 MB and 60 MB; the two tools themselves are two small classes.
- `dotnet list package --vulnerable --include-transitive`: no vulnerable packages.

## Pre-existing failures seen, not caused here (to report)
1. `EventStatusDeprecationTests.NoSourceFile_ReadsOrWrites_TheDeprecatedColumn`: offenders `scripts/demo-data/DemoDataLib.ps1`, `scripts/demo-data/reference-names.json`, `scripts/seed-data/reference/sprk_gridconfiguration.json` (not touched by this task).
2. ArchTest "ADR-010: Services should be concrete unless seam required": the 1:1 interface count is 174 against a limit of 173 (before this task's code; no interface was added here).

rework: none task 036

## Review (two opus passes)

### F1 (fixed): a create whose outcome is unknown was forgotten, so a host retry created a second task
`IEventsCapability.CreateAsync` answers `Failed` where the `sprk_event` row may already exist: the app-only create faulting after Dataverse stored the row (`RecordCreationService`: "a create that faulted may have landed... it propagates"), or anything after the row throwing, including a cancellation out of `WriteCreatedAuditLogAsync` (`EventsCapability.CreateCoreAsync`'s catch-all turns it into `Failed`). The tool answered "Nothing was saved. Try again shortly." and kept no entry; a cancelled caller rethrew and kept nothing. A retry with the same `idempotencyKey` then created a second task, on exactly the timeout path the key exists for.

Fix, in `CreateTaskTool.cs` only (`EventsCapability` and `McpIdempotency` unchanged):
1. `Refused` (and every other non-Ok, non-Failed status) is guaranteed to have written nothing: still not remembered, REST reason codes kept. `Failed`, an `Ok` with no value, and an exception out of the capability are an unknown outcome: the tool remembers a `RetryEntry(Guid.Empty, fingerprint, OutcomeUnknown: true)` under the key and answers `events.create_failed` (500) with "The task may or may not have been created. Check the matter's tasks with spaarke_list_tasks before creating it again." A retry with the same key and inputs replays that same text and creates nothing; the same key with other inputs is still `KeyReused`. "Nothing was saved" stays only where it is true (a refusal REST gives no code).
2. Once the arguments are valid and the create is about to run, `CreateAsync` is called with `CancellationToken.None`, so a caller going away cannot cancel it in flight (the reasoning `McpIdempotency.RememberAsync` documents). A caller already cancelled before the create starts keeps today's behaviour: the caller's token goes through, nothing is remembered, the call throws.
3. `CompleteTaskTool` has the same shape (a status write that landed, then a later fault answered "could not be completed", nothing remembered) but the reviewer classed it K and the fix is not the same two lines: the next call heals it (`alreadyCompleted: true`), and "unknown" there needs a different replay answer. Left unchanged; see K4.

Tests (`TaskWriteToolsTests`): must-fire, the app-only create double records the create then throws, and a retry performs no second create and answers the "may or may not" text; must-fire, the audit write cancels the caller's token after the row exists, the call still answers and a retry replays the id with one create; must-not-fire, a `Refused` create (the regarding-stamp refusal) writes nothing, answers its own reason code twice and never the "may or may not" text. Run against the previous `CreateTaskTool`, the first two fail (two rows created), the third passes; against this change all pass.

### Known limits (not fixed, on purpose)
- **K1** Two calls with the same key inside one cache round trip can both pass the get-then-set lock and both write (D-031-1, #3231). Belongs to `ITenantCache` (needs an atomic set-if-absent), not to a tool.
- **K2** Neither write tool has a 30 s `CancelAfter`, on purpose: cancelling a write mid-flight worsens F1. A host retry during a slow write is answered `KeyInProgress` by the 2-minute lock.
- **K3** The tool trims `subject`; REST does not. The parity test sends already-trimmed input.
- **K4** A complete replay says Completed even if the task was reopened since. A status write that landed followed by a later fault reports "could not be completed" until the next call heals it (`alreadyCompleted: true`).
- **K5** A replay is answered from the cache for 24 hours without re-checking access. It returns only the caller's own inputs and the task id, so nothing is disclosed.
