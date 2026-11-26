# Task 038 — `spaarke_run_playbook` optional `instructions` (2026-11-26)

Owner Q18: add the feature, make it easy for the user, add no extra steps. The model fills `instructions` from what the user already
said and leaves it out otherwise. No confirmation, no extra tool call, no widget change.

## Step 1 findings: the REST route and the engine field

| Question | Answer (code) |
|---|---|
| Engine field on 030's path | `PlaybookRunRequest.UserContext` (`Services/Ai/IPlaybookOrchestrationService.cs`). `PlaybookOrchestrationService.ExecuteAsync` (person entry, line ~148) and the app-only entry (~224) both put `request.UserContext` into `PlaybookRunContext`. 030's `DetachedPlaybookRunner` calls the person entry. No engine edit needed, so the escalation trigger did not fire (ADR-039 holds: no edit to the orchestration service, executors or graphs). |
| REST route | `PlaybookRunEndpoints.cs` line ~190: `UserContext = request.UserContext` straight from `ExecutePlaybookRequest.UserContext` ("Optional user-provided context or instructions"). **No trim, no length cap.** So the cap comes from the project constraint: 2,000 characters (= the parameter value cap). |
| Stored? | The durable record never holds it: `PlaybookRunRecordStore` maps `PlaybookRunDetail.UserContext = null` (ADR-015). |
| Logged? | Nothing in the engine or the helper logs it (grep of `UserContext` finds no logger use). |

## What changed

| File | Change |
|---|---|
| `Api/Mcp/LegalOperations/Playbooks/RunPlaybookTool.cs` | New optional string `instructions` (closed argument set, schema `maxLength` 2000, schema description is the owner's sentence); read in `TryReadArguments`; passed to `StartAsCallerAsync`; included in `FingerprintOf`; tool description gains "Optional instructions: what the user asked the run to focus on, in their words; omit if they said nothing." |
| `Services/Ai/PublicContracts/IPlaybookRunAi.cs` | `StartAsCallerAsync(..., parameters, string? userContext, ct)` (one new parameter, before the token). |
| `Services/Ai/PlaybookRuns/DetachedPlaybookRunner.cs` | Threads `userContext` to `PlaybookRunRequest.UserContext`. **Authorization, caps, token handling untouched.** |
| Tests | contract + runner + auth caps rows + `McpCallerPathTests` schema pin (see below). |

## Decisions

1. **Absent, null, empty and blank are the same: `UserContext` stays unset.** The request is then field-for-field today's.
2. **Cap on the text as sent (raw length), then trim.** Over 2,000 characters is refused (`mcp.invalid_argument`, 400, "Shorten it, or leave it out"), never cut. This matches the schema's `maxLength`, so a client that validates the schema agrees with the server. A non-string value is refused the same way. The refusal names the limit only, never the text.
3. **Fingerprint.** `instructions=<trimmed text>` is added as a part only when present, so (a) different instructions under one key are `mcp.idempotency_key_reused`, and (b) a call without instructions keeps the exact fingerprint task 030 computed (a retry across a deploy still replays; pinned by `Fingerprint_WithoutInstructions_IsUnchanged`). The fingerprint is a hash, so the stored retry entry holds no text.
4. **Trim in the tool, not the helper.** The tool owns argument rules (as for parameters); the helper passes the string on unchanged, so its authorization, caps and token handling are not touched. `IPlaybookRunAi` documents that the caller trims and caps.
5. **No new log line.** The constraint says log only presence and length; the per-call telemetry line already exists and nothing new is needed to operate this. Adding a line would be new surface with no failing behaviour behind it (§11). The tests prove no log, result, retry entry or status contains the text.
6. **Sentence wording.** The schema property description is the owner's sentence verbatim ("Optional: what the user asked the run to focus on, in their words; omit if they said nothing."). The tool description carries it as "Optional instructions: ..." so it reads in context. Description length checked by test (<= 1,024).
7. **No routing-eval description check exists** in the repo for this tool (grep `spaarke_run_playbook` finds only the tool, ledger, ADR and test files); the contract test's 1,024-character check is the one that applies.
8. **`/conflict-check` not invoked** (isolated agent worktree; main session coordinates 037 sequencing: 038 first, because 037 may touch the same two files).

## Known limits (K-class)

- K1: the fingerprint joins parts with `|`; a parameter value containing `|instructions=...` could be made to collide with an instructions part. Same caller, same key, own retry only: no cross-user effect. Same shape as every existing part.
- K2: model-written instructions can carry prompt-injection text from a document the model read. The text goes to the same engine field the Console user types into, and the run is already caller-authorized (007) with the playbook's own closed node graph (ADR-039); instructions steer the prompt, they grant no access.
- The engine may include the text in node prompts (that is its purpose); it is not stored on the run record (null) and not logged.

## CAPABILITY-INDEX

Row 50 needs a one-line signature edit: `notes/capability-index-pending.md` "Task 038" (sub-agents cannot write `.claude/`).
