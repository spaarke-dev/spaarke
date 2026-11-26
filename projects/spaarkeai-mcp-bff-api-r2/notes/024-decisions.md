# Task 024 — spaarke_list_playbooks and the playbook-list shared method: decisions and notes

Date: 2026-11-25. Branch: agent worktree (wave F, merged into `work/spaarkeai-mcp-bff-api-r2` by the main session). Decision: D-024-extract (`notes/decisions.md`). House pattern: task 007 (`PlaybookAuthorizationFilter.AuthorizePlaybookRunAsync` over `DecideRunAsync`).

## What changed

- `Api/Ai/PlaybookEndpoints.cs`: the rules of `ListUserPlaybooks` and `ListPublicPlaybooks` moved into one `public static PlaybookEndpoints.ListPlaybooksForCallerAsync(HttpContext?, PlaybookListScope, page, pageSize, nameFilter, outputTypeId, sortBy, sortDescending, CancellationToken)`. It reads the caller from the context (never a token parameter), applies the owned route's oid test, resolves the owner through `PlaybookAuthorizationFilter.ResolveCallerSystemUserIdAsync`, returns the empty page when that fails (fail closed), sets `PublicOnly` for the public scope, builds the routes' `PlaybookQueryParameters`, and logs as the routes logged.
- New types in the same file (namespace `Sprk.Bff.Api.Api.Ai`): `PlaybookListScope` (Owned, Public), `PlaybookListOutcome` (UserContextRequired = 0, Listed, OwnerUnresolved, Failed) and `PlaybookListResult` (Outcome, Status, ReasonCode, Page, Items). No `IResult`, no ProblemDetails. The page is the route's own `PlaybookListResponse` (`Models/Ai`, not `Services/Ai`): mapping it to a new DTO would have made the REST handlers map it back, risking the bytes.
- Both handlers are now one-liners over `ListForRouteAsync`, which maps the result to the responses they always gave: 200 with the page (Listed, OwnerUnresolved), 401 "User identity not found" (UserContextRequired), 500 with the route's own detail (Failed, and a cancelled request, as the old catch-all did). The handlers no longer take `IPlaybookService` / `ILoggerFactory` parameters; the method resolves both from `RequestServices` (the same scoped instances).
- `Api/Mcp/LegalOperations/Playbooks/ListPlaybooksTool.cs` (new): `spaarke_list_playbooks`, read-only, idempotent, model-visible, no widget. Inputs `nameFilter` (≤ 100 chars) and `page` (≥ 1), schema stated with `additionalProperties: false`. Calls the method for Owned, then Public, at the routes' defaults (20 per page, `modifiedon` desc) for the same page and filter; unions, de-duplicates by id (owned first, the legacy agent's order: `git show 37f0864afe:…/AgentEndpoints.cs` :334-:388), and slims each row to `{id, name, description, ownerKind, format}`. Logs through `DecisionToolCall.LogAroundAsync` (tool, outcome, rows, ms, correlation id).
- Ledger row in `tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.Mcp.Playbooks.cs` (seam `PlaybookEndpoints`).
- `McpHostContractTests.RealBffHost_ToolsList_…_IsExactlyTheFiveV1Tools`: `spaarke_list_playbooks` added; count 5 → 6 (method name kept to avoid churn for the other wave tasks that edit the same list — expect a merge conflict on this list with 023/026).

## Outcome → REST → tool

| Method outcome | REST (unchanged) | Tool |
|---|---|---|
| `UserContextRequired` (null context, unauthenticated, no request services; owned: oid missing / not a GUID) | 401 "User identity not found" (owned route only; RequireAuthorization keeps others out) | `isError` `DATAVERSE_USER_CONTEXT_REQUIRED`, 401 |
| `Listed` | 200 with the service page | rows |
| `OwnerUnresolved` (owned) | 200 with the empty page (raw page, normalized page size) | `isError` `DATAVERSE_USER_CONTEXT_REQUIRED`, 401; public list not read |
| `Failed` | 500 "Failed to list playbooks" / "Failed to list public playbooks" | `isError` `playbook.list-failed`, 500; never a partial list |

## Deviations and decisions (code wins, root CLAUDE.md §2)

- **No-systemuser caller on `/mcp` is refused (ADR-028 A10, Path C — comply).** A10: "MUST serve only callers that resolve to a `systemuser` … gets the existing `UserContextRequired` refusal." The owned REST route answers that caller an empty page and the public route lists public playbooks, so for this caller the tool is NOT the REST union. This follows 007's note for task 030 (resolve the systemuser first, refuse a no-row caller). It also refuses a transient WhoAmI fault (fails closed). Same family as 008's open conflict (A vs C); if the owner chooses parity there, this mapping is one `if` in the tool. REST bytes are unaffected.
- **New reason codes (ADR-019, recorded here):** `playbook.list-failed` (the 500 bodies carry no code) and `playbook.invalid-argument` (the routes answer a bad query value with a bare binding 400).
- **Name clipped to 200 characters** (the POML caps only the description). Needed for the size bound; real names are far shorter.
- **Byte budget (`RowBudgetBytes` = 56 KiB) instead of a smaller page.** With escape-heavy text (`"é<` → 6 bytes per character under the default encoder) 40 rows of 200 + 200 characters exceed 64 KiB, so no page size that keeps the routes' 20 per list fits by caps alone. Rows are kept while their measured cost fits; a cut list says `truncated: true` and asks for `nameFilter`. Realistic long text (200-char names, 2,000-char descriptions clipped to 200) keeps all 40 rows (test). Over-engineering check: ~15 lines, no setting, no new type beyond the row.
- **Text content lists name, owner kind and id per row** (no description), unlike the plan/lookup tools' count-only text: the model needs the ids to use the list, and some hosts read `content` only. Names are catalogue names, not record content (ADR-015: no definition, prompt or scope is ever read — `PlaybookSummary` carries none).
- **Service now receives the request's cancellation token** (before: `default`). Only an aborted request behaves differently (it was and still is a 500 to a client that has gone). Existing mocks use `It.IsAny<CancellationToken>()`.
- **Plugin skill not updated** (`src/solutions/CopilotPlugins/spaarke/skills/`, guide Step 4): outside this task's file list; routing is task 041's eval. Listed for the main session.

## Placement (ADR-013 / ADR-029, `.claude/constraints/bff-extensions.md` §A)

- The shared method sits in `Api/Ai/PlaybookEndpoints.cs`, grandfathered by `ADR013_AiBoundaryTests` (prefix `Sprk.Bff.Api.Api.Ai.PlaybookEndpoints`), as D-024-extract decided. Its public signature carries only `HttpContext`, BCL types, the new enums/record and `Models/Ai` DTOs; the tool never names `IPlaybookService`, so the MCP ledger's ADR-013 text and reflection checks stay green. No package, no DI registration, no background work (ADR-052 n/a).
- Component justification (root §11): **Existing** — the two list handlers (reuse-scan row "Playbook list"); the legacy de-duplication (deleted by 001). **Extension** — extended in place; the new types are its result shape. **Cost of doing nothing** — Copilot users cannot discover their JPS playbooks (FR-04), or the tool would copy the owner/fail-closed rules (spec MUST NOT).

## Tests

- New `tests/integration/contract/Api/Ai/PlaybookListRoutesFrozenBytesTests.cs` (7): status, Content-Type, body bytes and the service query of both routes — owned resolved, owned unresolved (page 0, pageSize 500 → raw page / normalized size), owned non-GUID oid 401, owned 500, public (GUID and non-GUID oid), public 500. **Run against the pre-change code first: 7/7 green**; green again after.
- New `tests/integration/auth/Ai/PlaybookListSharedMethodTests.cs` (12): the method direct vs the REAL route over the same substitutes — rows, status, and the same service questions with the same query (Listed owned/public; OwnerUnresolved; Failed owned/public; no caller ×4; owned non-GUID oid; public non-GUID oid still listed) plus cancellation propagating.
- New `tests/integration/contract/Api/Mcp/Playbooks/ListPlaybooksToolTests.cs` (14): tools/list entry; ids = union of the two REST lists for the same caller and query (default, and page 2 + nameFilter); a playbook in both appears once (rows and text); no caller context; no resolvable systemuser; public-list fault → isError with the route's status; 4 invalid arguments; slim rows and clipped description; 40 realistic rows < 64 KiB untruncated; 40 escape-heavy rows < 64 KiB truncated.
- Scope beyond the named criteria (one line each): OwnerUnresolved (A10 mapping); list fault (ADR-019 error mapping); invalid arguments (guide Step 4 requires one); cancellation (the method's only propagating exception); non-GUID oid rows (the routes' existing asymmetry).
- Seeding proof: (1) removing the de-dup on the public rows, (2) dropping the OwnerUnresolved refusal, (3) `PublicOnly = false` → 7 failures across the frozen-bytes, parity and tool tests; restored.

## Results (2026-11-25)

- Test results:
  - Before the change, 63/63 passed: the frozen-bytes tests plus the existing playbook list and chat-context suites.
  - After the change, the focused suites (`Playbook|Mcp|ChatContextAuthorization` in `Sprk.Bff.Api.Tests`) gave **1695 passed, 0 failed, 1 skipped**. Every existing playbook list contract and auth test passed unchanged.
  - The only existing test edited is `McpCallerPathTests` Test5, the static-schema pin; the new tool's row was added. `McpHostContractTests` changed its count from 5 to 6.
- ArchTests 2848/2848.
- The BFF builds with `-warnaserror`: 0 warnings, 0 errors.
- `check-changed-cs-format.sh origin/master`: all 102 changed files formatted.
- Publish size (Release, compared with a fresh master build `de76599830`, run in C:\wt024m and C:\wt024y, both removed with Remove-WorktreeSafe):
  - Uncompressed: 144,594,243 → 143,415,843 bytes, **−1,178,400**.
  - Compressed: 43,633,296 → 43,177,471, −455,825.
  - These figures are for the whole r2 branch against master. The legacy agent was deleted by 001, so the branch is smaller. Task 024 adds roughly 450 lines of source and no package.
- CVE: `dotnet list package --vulnerable --include-transitive` reports no vulnerable packages.
- Inline code-review:
  - No F-class finding is open.
  - Three items are accepted as K-class:
    - (K) A playbook that is the caller's own and public can show up twice across pages, or carry the wrong ownerKind, when the two lists page differently. REST paging has the same property.
    - (K) User-authored names and descriptions are model-visible, as they are in REST and in the legacy agent.
    - (K) The kill switch maps to a 500, not a 503. This is D-024-1 and pre-existing.
- Inline adr-check:
  - ADR-013: the AI boundary checks pass. The method lives in the grandfathered `PlaybookEndpoints`, and the tool never names `IPlaybookService`.
  - ADR-015: rows are slim.
  - ADR-019: reason codes are on every `isError`.
  - ADR-028 A10: Path C (comply).
  - ADR-038: KEEP paths, with mocks only at boundaries.
  - ADR-052: not applicable.

rework: none task 024
