# Task 022 notes — spaarke_show_decisions and spaarke_list_decision_page

Branch `worktree-agent-ae32a2b7d31c32606`, cut from `work/spaarkeai-mcp-bff-api-r1` @ 6ce612ff0e (wave 6, parallel with 023, 024, 030).
Rigor FULL (tags bff-api / mcp; modifies .cs, .ts, tests/**). 018 (privilege derivation) was done before this task started (HARD GATE).

## What was built

| File | What |
|---|---|
| `src/server/api/Sprk.Bff.Api/Api/Mcp/LegalOperations/Decisions/ShowDecisionsTool.cs` | `spaarke_show_decisions`: model + app visible, read-only, idempotent, one optional closed `category` enum, `additionalProperties: false`, `_meta.ui.resourceUri = ui://spaarke/decisions.html` |
| `.../Decisions/ListDecisionPageTool.cs` | `spaarke_list_decision_page`: app-only (`_meta.ui.visibility ["app"]`), read-only, `cursor` only; full slim rows for the widget |
| `.../Decisions/DecideLaneCategories.cs` | The Decide-lane category partition, mirroring `laneCountFilters.ts` `bucketLaneItems` (card order, "only when > 0" cards, ordinal comparisons) |
| `.../Decisions/DecisionsTextSummary.cs` | FR-04 reduction, Console / Invoice Review links, the FR-14 text |
| `.../Decisions/DecisionToolCall.cs` | Closed-argument check, result shapes, the one telemetry line |
| `tests/fixtures/decide-lane-category-parity/cases.json` | Shared parity corpus |
| `src/client/shared/Spaarke.UI.Components/src/components/Worklist/__tests__/laneCountFilters.parity.test.ts` | jest side of the parity (laneCountFilters.ts unchanged) |
| `tests/unit/domain/Mcp/DecideLaneCategoriesParityTests.cs` | xunit side of the parity |
| `tests/integration/contract/Api/Mcp/DecisionToolsTestHost.cs` | Production MCP host serving exactly the two tools over a boundary double that pages through the PRODUCTION `DecideLanePaging` / `DecideCursor`; 200-item seed; capturing logger |
| `tests/integration/contract/Api/Mcp/ShowDecisionsToolTests.cs` | Contract, size, paging, failure and telemetry tests |
| `tests/integration/auth/Mcp/DecisionsPrivilegeReductionTests.cs` | FR-04 tests |
| `tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.Mcp.cs` | `McpTools` ledger: one contiguous `// ── task 022 ──` block (both tools, default policy, seam `IDecisionCapability`); the reflection control now passes the ledger's classes (it assumed an empty ledger; 024 made the same change) |

## Decisions (also in notes/decisions.md)

- **Paging tool name: `spaarke_list_decision_page`** (final; naming note §1 `spaarke_{verb}_{object}`).
- **Counts come from the whole lane.** The Console's cards count every Decide item, so the tool reads `ListDecideAsync` for the partition and `ListDecidePageAsync(user, null)` for page 1: two lane reads per call. Folding the partition into the page result would mean a capability change (`Services/Signals`, ontology-reviewed, other owner); filed as a possible follow-up, not done here.
- **`category` is a lens.** It narrows the text's top items and is echoed (resolved) in `structuredContent.category`; page 1's rows stay unfiltered so the cursor chain the widget follows stays the lane's. A category with no card resolves to `all` and the text says so.
- **The widget reads page 1 again through the paging tool** to get privileged detail; `spaarke_show_decisions`' `nextCursor` equals the paging tool's page-1 cursor (tested).
- **Error vocabulary.** Read failures map through `DecisionResultMapper.ToToolError`: the worklist's 503 (`too_many_signals`, `core_pair_read_failed`, any Unavailable) is `ontology.signal.worklist_unavailable`, the REST route's reasonCode; the internal reason is hidden exactly as REST hides it. The POML criterion "becomes an isError result with that reasonCode" is read as the route's reasonCode (one vocabulary); owner question 2.
- **Argument errors** are `ontology.decision.invalid_parameter` isError results with a fix-the-call message listing the valid values; any argument other than the declared one is refused, not ignored.
- **Injected beyond capability + Api/Mcp helpers:** `IHttpContextAccessor` (FR-10 caller), `IOptions<DataverseOptions>` (the link base), `TimeProvider` and `ILogger<T>` (NFR-05 telemetry). None is AI-internal; the ADR-013 guard is green.
- **`is` keyword avoided in the tool class bodies.** The MCP guard's AI-internal-type text scan reports the C# `is` keyword as a type (a type named `is` is parsed out of `Services/Ai/**`). Null checks in the two tool classes use `==` / `!=`. Scanner not edited (main session files and fixes it).

## Known limits (K-class)

- **K2 — privileged count is page 1's.** Privilege is derived per page (018), so "N privileged items — open to review" counts page 1's flagged items, not the lane's. The whole-lane category counts include privileged items (they are counts only). Owner question 1.
- **K2 — no per-Work-Item Console deep link.** The Console (`sprk_spaarkeai`) has no launch parameter for one item, so non-billing items link to the Console root (`{Dataverse:EnvironmentUrl}/main.aspx?pagetype=webresource&webresourceName=sprk_spaarkeai`); billing items link to their Invoice Review page with `openInvoiceReview.ts`'s `data` rules. An existing setting gives the URL and nothing is hard-coded, so the escalation trigger did not fire. Owner question 3.
- **K2 — counts and page are two reads.** A Work Item that changes between them can make a count and page 1 disagree by that item for one call.
- **K3 — the communication-subject FR-04 test uses a flagged row from the boundary double.** 018's `DecidePrivilege_APrivilegedCommunicationSubject_...` proves the flag through the real capability; 022's test proves a flagged communication-subject row loses its subject name and title in model-visible output.

## Evidence

- HttpContext criterion (grep over `src/server/api/Sprk.Bff.Api/Api/Mcp/`): `DefaultHttpContext` — no match; an `HttpContext` field — no match; `accessor.HttpContext` read once per call, into a local, in `ShowDecisionsTool.cs:117` and `ListDecisionPageTool.cs:98`, passed straight through.
- Seeding proof (confidentiality guard), run 2026-11-14: `DecisionsTextSummary.Reduce` changed to keep one flagged row (`n == 0`) → the 7 `ShowDecisions_NeverCarriesAPrivilegedItemsDetail_*` cases failed; changed to keep the communication item (`n == 17`) → `ShowDecisions_APrivilegedCommunicationSubject_*` failed; file restored from a copy, all pass.
- Fail before: every new test targets types this task adds (none compiles without them).

## Test scope beyond the stated contract (one line each)

- `BothTools_AnArgumentTheyDoNotTake_*`: the schema says `additionalProperties: false`; this proves the handler makes it true (April issue 2: invented arguments).
- `ShowDecisions_ACategory_NarrowsTheTopItemsOnly_*`: pins the lens semantics chosen above, so a later change to them is visible.
- Privilege theory over every category: the reduction must hold under every lens, not only the default.

## Results

- jest parity: 7/7. xunit parity: 9/9. New tool tests: 27/27. MCP + mapper + WorklistItemSummary filter: 214/214. ArchTests `RouteAuthorizationGuardTests`: 101/101 (before the `is` and control fixes: 3 failed).
- `check-changed-cs-format.sh origin/master`: all files formatted.

## BFF hygiene (CLAUDE.md §10 + NFR-01)

Command `dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/`; framework-dependent linux-x64; Release;
`Compress-Archive -CompressionLevel Optimal` over `deploy/api-publish/*`; PDBs included. Each tree exported with `git archive` to a short
path (`C:\tmp\p22m`, `p22w`, `p22b`), not published from the iterated worktree.

| Tree | Commit | Compressed |
|---|---|---|
| master | 942b2889bf | 40.19 MB (42,143,598 bytes) |
| work branch base | 6ce612ff0e | 40.95 MB (42,939,793 bytes) |
| task 022 | this branch | 40.97 MB (42,960,476 bytes) |

Delta for task 022 against its base: **+0.02 MB** (+20,683 bytes). Against fresh master: **+0.78 MB**, almost all of it the project's
earlier work (020's ModelContextProtocol SDK). Under the 60 MB ceiling and the +5 MB trigger. No package or project-file change in 022;
`dotnet list package --vulnerable --include-transitive`: no vulnerable packages.

rework: none task 022
