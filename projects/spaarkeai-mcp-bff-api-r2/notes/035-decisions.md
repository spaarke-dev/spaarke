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

## Review fixes

Seven review findings, all confirmed against the source and fixed. The fail-before column names what was reverted, and the tests that then failed.

| # | Finding | Fix | Fail-before proof |
|---|---|---|---|
| 1 | Right-click opened the link: `openThroughHost` was on `onAuxClick`, and Chromium fires `auxclick` for the context-menu button | `openThroughHost` returns early (no `preventDefault`, no `openLink`) when `event.type === 'auxclick'` and `button !== 1` (`App.tsx`) | Early return removed: "a right click ... is left to the browser" fails. The middle-click test is unchanged and still passes |
| 2 | The server advised an offset it then refused (022's code): `DescribePage` set `next = offset + limit` (or `+ kept`) with no cap, and the argument check refuses an offset above `MaxOffset` (1000) | `SearchToolSupport.DescribePage`: a next offset above `MaxOffset` is not advised: `nextOffset` null, `hasMore` false, and the text says `PagingLimitAdvice` ("That is as far as paging goes. For more, narrow the search with a more specific query.") instead of "send offset N". An offset of exactly 1000 is still advised. Widget: `nextCallArguments` returns null above 1000 (no Load more), and `noticesFor` says to narrow the search rather than "Ask for the next page in the chat" | Server: the cap condition disabled, 3 of 15 `SearchToolsPagingTests` fail (offsets 998 and 1000 for both tools, and the size-cut page at 1000). Widget: guards removed, 3 tests fail (`nextCallArguments` bound, `noticesFor` bound, App "largest offset ... narrow") |
| 3 | Load more ended silently after an empty page, and an opening result at offset > 0 with no rows said "No results found." | `SearchPage` now carries `offset`. `noticesFor` says "No more matches after these." when the newest page is empty and the reader paged into the list; for an opening result at offset N > 0 with no rows the component is not drawn and the widget says "No more matches after the first N." (`App.tsx`, `searchResults.ts`) | Empty-page branches disabled: 4 tests fail (two `noticesFor`, two App) |
| 4 | Keyboard focus lost on Load more (NFR-09): `disabled={loading}` | `disabledFocusable={loading}`; the label stays "Load more"; the footer carries `aria-busy` while the existing spinner ("Loading more results") shows. A click while loading does nothing (the handler already guards `loading`; Fluent also suppresses it) | `disabled={loading}` restored: the focus test fails (button disabled, focus lost) |
| 5 | The widget behaviour suites ran in no workflow (each `build` runs only its `widgetSurface` guard) | `ci-tier1-blocking.yml` (`mcp_conformance` job): a `npx jest --ci` step after each of the three widget builds (Decisions, Briefing, Search results), run from the widget's folder. No `package.json`, publish or deploy workflow changed; nothing under `spaarke-decisions/` changed. D-021-1's text in `defer-issues.md` now states what runs. YAML parses (`yaml` package: the six widget steps listed in order). All three suites pass here: Decisions 398 pass / 2 skipped, Briefing 129 pass / 1 skipped, Search results 131 pass | n/a (workflow change) |
| 6 | The "host rejects" App test proved nothing: it asserted a warning that was already on screen from the first click | The test now goes false (warning), true (warning gone), reject (warning back) | `setLinkError(!opened)` replaced by `setLinkError(true)`: the test fails (the old version of it passed under that mutation) |
| 7 | The built-page guard's must-fire table lacked rows | `BUILT_FORBIDDEN` already named `msal`, `acquireToken`, `sessionStorage` and `indexedDB`; rows added for each. The built page passes | The four patterns dropped from `BUILT_FORBIDDEN`: the four new rows fail |

Tests added: server `SearchToolsPagingTests` (+4: full page at next offset 1000 still advised; offsets 998 and 1000 advise none for both tools; a size-cut page at offset 1000 advises none). Widget: `App.test.tsx` (+5, one rewritten), `searchResults.test.ts` (+5), `widgetSurface.test.ts` (+4 rows). 022's contract tests were not edited; none pinned the old text for a case at the cap.

Results: widget `npm run build` passes, bundle **905,979 bytes (0.86 MB)**; widget jest 131 pass; Briefing 129 pass / 1 skipped; Decisions 398 pass / 2 skipped. `dotnet build src/server/api/Sprk.Bff.Api/ -warnaserror`: 0 warnings, 0 errors. `dotnet test tests/unit/Sprk.Bff.Api.Tests --filter "FullyQualifiedName~Mcp|FullyQualifiedName~Search"`: 1542 passed, 0 failed, 6 skipped (the earlier 1538 plus the four new server tests). `dotnet format whitespace --verify-no-changes` on the two changed `.cs` files: clean. REST bytes untouched (MCP tool text only).

### Known limits (recorded, not fixed)

- The browser context menu's "Open link in new tab" and dragging a link bypass the interception. The href is always a server-built http(s) link.
- A failed or cancelled opening call leaves the widget spinning for 45 s, and then it says "No search results arrived." That is shared-host behaviour.
- The two tools do not set `_meta.ui.visibility`; the spec default is the same.
- `parentType` is shown exactly as the index stores it (022's data).

rework: none task 035
