# Task 035 — search results widget (ui://spaarke/search-results.html, FR-16)

Built on task 034's scaffold, task 021's shared transport and task 022's tools. The Console's `SearchResultsWidget`
(`src/client/shared/Spaarke.AI.Widgets/src/outputs/output-widgets/SearchResultsWidget.tsx`) is used as-is through its props; no file under
`Spaarke.AI.Widgets` changed (its own tests are untouched, so they pass unchanged).

## What was added

| Piece | Where |
|---|---|
| Resource `ui://spaarke/search-results.html` (empty `_meta.ui.csp`) | `Api/Mcp/LegalOperations/Search/SearchResultsWidgetResource.cs`, registered in `McpModule`'s resource collection (the only place); startup warning in `McpEndpoints` |
| Both search tools point at it | `SearchDocumentsTool.cs`, `FindRecordsTool.cs`: `_meta.ui.resourceUri` only (no text or `structuredContent` change; no visibility override, so the default model + app applies) |
| Embed | `Sprk.Bff.Api.csproj` (`Condition=Exists`, logical name `Sprk.Bff.Api.Widgets.search-results.html`) |
| Widget | `src/client/widgets/spaarke-search-results/` (single file, `check-bundle-size.mjs`, `widgetSurface` guard) |
| Build / verify wiring | `publish-bff-artifact.yml`, `deploy-bff-api.yml`, `ci-tier1-blocking.yml` (build step and path filters, including `Spaarke.AI.Widgets/src/**`), `run-conformance.sh`, `scripts/Deploy-BffApi.ps1` (all three widget loops) |
| Host `toolInput` | `src/client/widgets/shared/mcp/mcpHost.ts` + `mcpHostContext.ts` (optional field; see D-035-input) |

## Mapping 022's `structuredContent` onto the component's props (step 1: the gaps)

| Component prop | From | Gap and handling |
|---|---|---|
| `data.results[].id` / `title` | `items[].id` / `name` | none |
| `data.results[].excerpt` | `snippet` plus details | One text line only. Documents: `fileType · in <parentType> <parentName> — snippet`. Records: `<Type> · ref <numbers> — snippet`. Plain text, rendered as React text (a test pins `<img onerror>` and `<script>` staying text). |
| `data.results[].url` | `openLink` | Only an absolute http(s) URL is passed on; anything else leaves the title as plain text (no dead link). |
| `data.results[].score` (required `number`) | none | **No score exists** (022: ADR-015, "not exposed"). See D-035-score. |
| `data.query` | none | Not in the result; taken from the call's arguments (`toolInput`). See D-035-input. |

## Decisions

### D-035-score — the badge is hidden, not faked (no `Spaarke.AI.Widgets` edit)
`score` is a required `number` and the component always draws a relevance `Badge`. The tools expose no score, and 022's
constraint keeps their `structuredContent` as it was. The widget passes `score: 0` and hides the badge's slot with a nested rule on
the component's `className` prop (`& .fui-CardHeader__action { display: none }`, Fluent's stable slot class). A rank-derived or "0%" badge
would tell the reader something false. This stays inside ADR-012 (no prop change; "STOP and escalate" applies only to a prop change).
**Cleaner end state:** make `score` optional in the component and skip the badge when absent; that edits the shared component and
the Console's tests, so it is deferred (D-035-1).

### D-035-input — the shell reads the call's arguments (`toolInput`) from the host
The result does not carry the query, and "Load more" must repeat the call. MCP Apps hosts send the tool call's arguments to the widget
(`ui/notifications/tool-input`; the SDK's `App.ontoolinput`). `McpHostProvider` now keeps them as an optional `McpHostConnection.toolInput`
(object arguments only; set before the result). It is an addition to a shared file task 021 created and r2 owns; the existing members and
the Decisions and briefing widgets are unaffected (a test double without it stays valid). Test: `src/__tests__/mcpHostToolInput.test.tsx`
(fails with the handler removed). Whether Copilot Chat and Cowork send it is **unverified until the dev deploys** (D-035-2): if a host does
not, the heading reads "your search" and Load more is hidden (the widget points at the chat), so nothing is broken.

### D-035-paging — 022's contract, honoured
- `hasMore` is never read. "Load more" appears only when the tool advised `nextOffset` AND the host sent the call's arguments (a further
  call needs the query). It calls the same tool with the call's own arguments (closed set: `query, limit, entityType, entityId` /
  `recordTypes`) and the new `offset`. Pages append; a repeated id is shown once; a failed page keeps the list, says why, and can be asked again.
- A document search over every record never advises an offset. For a full or cut page there the widget says "There may be more matches. To
  see more, search inside one matter, project or invoice." and shows no Load more. A short page says "This may not be every match." (rows are
  checked for access after ranking, so a short page is never called the end).
- `truncated` -> "Some results were left out to keep this list short."; a `PARTIAL_RESULTS` warning -> "These results may be incomplete."
  (shown for as long as any shown page carried it).
- A new opening result starts the list again (a new search).

### D-035-links — only through the host
The component renders an `<a href target=_blank>` for a hit with a `url`. The widget takes every click and middle-click on an anchor in the list
(`preventDefault`), checks the scheme again (http/https), and calls the host's `openLink`. Enter on a focused link is a click, so the keyboard
path is the same one. A host that refuses or rejects shows "Your host did not open that link." The widget never calls `window.open` or sets
`location` (the surface guard forbids both).

### Size and surface
- **Bundle: 0.86 MB** (905,413 bytes; aim 1 MB, ceiling 3 MB). Only Fluent UI and the SDK; no lexical, no shims (the component imports Fluent only).
- `widgetSurface.test.ts`: the widget's own `src/` has no fetch / XHR / socket / beacon, token, storage, Xrm, raw colour, page theme, `innerHTML` /
  `dangerouslySetInnerHTML`, `window.open` or `location` navigation; the BUILT page has no `fetch(`, `.fetch(`, XHR, beacon, storage, cookie, token,
  MSAL or Xrm (must-fire controls for each pattern). The built page was clean.

## Tests

- Widget (jest): `App.test.tsx` (32), `searchResults.test.ts`, `widgetSurface.test.ts` (44), `mcpHostToolInput.test.tsx`: **117 pass**.
  Mutation checks (each made the named tests fail, then restored): no link interception -> 4 App tests fail; badge not hidden -> 1 fails;
  Load more offered without the call's arguments -> 3 fail; host provider without `ontoolinput` -> the toolInput test fails.
- Server: `tests/integration/contract/Api/Mcp/Search/SearchResultsWidgetResourceTests.cs` (list/MIME/empty csp; both tools carry the resourceUri and add
  nothing else; unknown uri; 401 and the 503 kill switch; built page; not-built error; startup warning). `DecisionsWidgetResourceTests` (resource list)
  and the ArchTests registration pin (`RouteAuthorizationGuardTests.Mcp.cs`) now include the third resource.
- Test clock: none read; the "no result arrived" case uses jest fake timers.

## Placement and component justification (CLAUDE.md section 10, 11)

1. **Existing** — `DecisionsWidgetResource`, `BriefingWidgetResource` and `McpModule`'s resource collection; the `SearchResultsWidget` component.
2. **Extension** — the component is reused unchanged. A new resource class is a third copy of the proven shape (each widget is its own `ui://`
   single-file resource, owner Q5); one factory for all three is #3227/#3228, not done here.
3. **Cost of doing nothing** — search results reach Copilot Chat and Cowork only as text; FR-16's search-results widget acceptance fails.

Placement: BFF, inside the existing `/mcp/legal-operations` host (no new host, package, DI registration or background work).

## Not done here

- The widget in Copilot Chat and Cowork (the task's `<ui-tests>`) needs deploy 039; light/dark and keyboard are covered in jest.
- Dev deploy, tenant registration and Dataverse writes: none.

## Verification

- Widget: `npm run build` (tsc, vite, size gate, built-page surface scan) passes; `npx jest`: 117 tests pass. Bundle **905,413 bytes (0.86 MB)**.
  The briefing widget's own suite (129 pass, 1 skipped) and the shared `mcp/__tests__` (45 pass) still pass with the `toolInput` addition.
- BFF: `dotnet build src/server/api/Sprk.Bff.Api/ -warnaserror`: 0 warnings, 0 errors.
- `dotnet test tests/unit/Sprk.Bff.Api.Tests --filter "FullyQualifiedName~Mcp|FullyQualifiedName~Search"`: **1538 passed, 0 failed, 6 skipped** (the skips are
  the not-built twin of each widget's built case, the other widgets' built cases in a tree where they were not built, and three pre-existing Office skips).
  This tree had the search-results widget built, so its built case RAN and passed (the CI gate in `publish-bff-artifact.yml` requires that).
- ArchTests (`RouteAuthorizationGuard|Mcp`): 117 passed, 0 failed. `dotnet format whitespace --verify-no-changes` on every changed `.cs`: clean.
- **Fail-before** (server): with `FindRecordsTool`'s `_meta.ui.resourceUri` and `McpModule`'s resource entry taken out, `SearchResultsWidgetResourceTests` fails
  3 of 9 (list, built read, both-tools descriptor); restored. Widget mutation checks are in "Tests" above. 022's contract tests were not edited and pass.
- **Publish size** (ADR-029): `dotnet publish -c Release` zipped (Optimal, all files), this tree with and without the widget's `dist/` (the embed is the only
  size-relevant change; the C# is a few hundred bytes):

  | Build | Compressed | Folder | Files |
  |---|---|---|---|
  | without the widget embed | 43,261,147 bytes (41.26 MB) | 143,687,099 | 186 |
  | with it (this task) | 43,501,327 bytes (41.49 MB) | 144,592,827 | 186 |

  Delta: **+240,180 bytes compressed (+0.23 MB)**, +905,728 bytes in the folder, 0 files. Under the 5 MB justification line; total 41.49 MB, under 55 MB.
  Method note: the baseline is this tree without the embed, not a fresh master worktree (the master gap is other projects' work; 022 used a pre-task base the same way).
- **CVE**: `dotnet list package --vulnerable --include-transitive` on `Sprk.Bff.Api`: no vulnerable packages. No package added (the widget's npm packages are the
  briefing widget's set minus lexical/icons; they are not part of the BFF's dependency graph).
- Not run here: the Spaarke.AI.Widgets jest suite (no `node_modules` in this worktree; no file under that folder changed, so its tests are unchanged by construction),
  the conformance host (`run-conformance.sh`; wiring added, not run) and the workflows themselves (YAML parses; Actions was not run).

## Inline review (no sub-agent; the brief says none)

- ADR-012 (component reused, no edit), ADR-013 (nothing under `Api/Mcp/**` names an AI-internal type), ADR-015 (no new model-visible output), ADR-021 (semantic
  tokens, host theme), ADR-029 (placement + size above), ADR-032 (`Mcp:Enabled=false` removes the resource: tested), ADR-038 (every test on a KEEP path or the widget's jest;
  no mocked `HttpMessageHandler`).
- Class check (Step 9.5 rule 6): the "tool result trimmed or partial" and "next offset" rules are applied in one module (`searchResults.ts`) for both tools.
  Pattern greps: `fetch(` / `.fetch(` / `innerHTML` / `window.open` in the new widget's `src/`: none (the surface guard fails the build otherwise).

rework: none task 035
