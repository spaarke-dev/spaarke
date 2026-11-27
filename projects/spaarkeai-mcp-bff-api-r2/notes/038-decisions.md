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

## Verification (2026-11-26)

- Build: `dotnet build src/server/api/Sprk.Bff.Api/` 0 warnings, 0 errors.
- Tests (`Sprk.Bff.Api.Tests`, filter Mcp|Playbook|DetachedPlaybook): 2040 passed, 3 skipped (widget-not-built skips, pre-existing), 0 failed.
- ArchTests: filter Mcp|DetachedHttpContextGuard|RouteAuthorization 122/122; full run 2867/2867 passed.
- Format: `bash scripts/quality/check-changed-cs-format.sh origin/master` all 171 changed files formatted (after the commit hook ran `dotnet format whitespace`).
- Fail-before (copy-the-old-behaviour-back mutations; 67 tests in the 038 and caps filter):
  - Mutation A (tool does not accept `instructions`, runner does not set `UserContext` = the old behaviour): 9 failed: runner user-context (text case), 2,000 and 2,001 length cases, absent-shape cases `null`/`empty`/`blank` (the call is refused as an unknown argument), the no-echo test, the engine-request test, the fingerprint-in-retry test. The `absent` shape and the negative refusal rows pass before and after by design (same as today).
  - Mutation B (cap removed = `int.MaxValue`, fingerprint part dropped): 5 failed: length cap, auth caps row "2,001-char instructions", `Fingerprint_WithoutInstructions_IsUnchanged` (its `NotBe(before)` arm), schema/description, different-instructions retry.
  - Final code restored; all green.
- Publish size (BFF hygiene item 4; fresh worktrees `C:\wt038m` / `C:\wt038b`, `dotnet publish -c Release -r linux-x64 --self-contained false`, `Compress-Archive -CompressionLevel Optimal` over `deploy/api-publish/*`, PDBs included, 4 PDBs):
  - task commit 574b443cd1: 43,303,991 bytes (41.298 MB), 186 files
  - parent (work-branch tip cfc92e4b0e): 43,302,831 bytes (41.297 MB), 186 files. **Task 038 delta: +1,160 bytes (+0.001 MB).**
  - fresh origin/master 51ce01a116: 43,661,130 bytes (41.638 MB), 194 files. The 8 extra files are the Microsoft.Agents.* / AdaptiveCards assemblies the work branch removed with the retired `/api/agent` gateway (not this task); the branch is -0.340 MB against master, of which task 038 is +0.001 MB.
  - Neither fresh worktree has the built widget bundles (conditional EmbeddedResource), the same on both sides.
- CVE: no package added or changed, so no new `dotnet list package --vulnerable` finding is possible from this task (not re-run).
- Out-of-scope defects found: none.
- rework: none task 038

## Close-out (2026-11-26)

- **The review's F4 is resolved by #3268.** No prompt builder read `UserContext`, so the `instructions` argument did nothing. Under the owner's approval (D-3268-user-focus, ADR-039 Path C) the run's `UserContext` now reaches the prompt as a fenced "User focus" block. Merged at cd12bb4c2f from `9aa3357eb9`, after one opus adversarial pass: no F in the code, one F2 test deleted, and K1/K2/K4 hardened (escaping, a 2,000-character cap in the helper, the malformed-JPS fallback). The builders that still ignore the focus are #3281.
- **The pre-existing chat-rerun claim is fixed by the same change.** "Reran with additional instructions" (`AnalysisExecutionHandler.cs:376-378`) is now true on the main analysis builders.
- **Four internal callers no longer put system text in `UserContext`.** They are the matter and project pre-fill, the workspace summary and the scheduler. Their prompts are unchanged; see notes/3268-decisions.md.
- Merged-tree run (cd12bb4c2f): `Sprk.Bff.Api.Tests` filtered over the AI prompt builders, playbook runs, MCP and finance gives 10,815 passed, 0 failed, 7 skipped (existing). The ArchTests gave 2,867 passed on the fix branch; the merge added docs only.
