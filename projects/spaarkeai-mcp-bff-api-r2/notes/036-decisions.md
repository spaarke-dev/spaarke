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
