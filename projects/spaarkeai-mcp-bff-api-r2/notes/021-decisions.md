# Task 021 - shared widget MCP transport: notes and decisions

## Step 1: what was Decisions-specific vs general (the four files at the merge of work/spaarkeai-mcp-bff-api-r2, 2026-11-25)
- `mcp/mcpHostContext.ts`: general (the connection shape, `useMcpHost`, `DISCONNECTED`). Moved unchanged.
- `mcp/mcpHost.ts`: general (`McpHostProvider` over ext-apps 2.0.4: handshake, host-context merge = theme subscription, tool result,
  `callTool`, `openLink`, `requestDisplayMode`). Moved unchanged.
- `mcp/mcpFetch.ts`: general REST-to-tool shim EXCEPT the titles: `TITLES[500] = 'The decision was not recorded'` and the
  "partial + 409 = failed commit" title are the Decisions commit route's (`DecisionResultMapper`). Moved, with those two rules turned into
  a `title(status, hasPartial)` option; the Decisions wrapper (`spaarke-decisions/src/mcp/mcpFetch.ts`) passes them, so every
  Decisions title is what it was. Default 500 title in the shared shim: "Internal Server Error".
- `lane/decideLaneFetch.ts`: entirely Decisions-specific (paging `spaarke_list_decision_page`, lane body, cursor-loop guard). Stays
  in the widget, unchanged: it imports `createMcpFetch` from `../mcp/mcpFetch`, which is now the shared shim (through the wrapper).

## Decisions
- D1 Files: the shared code is four files, not one (`mcpFetch.ts`, `mcpHost.ts`, `mcpHostContext.ts`, `widgetMcpTransport.ts`, plus
  `index.ts`). Reason: `mcpHost.ts` imports the ESM-only SDK; components and their tests must reach the connection shape without it
  (the reason r1 split them). The POML listed only `widgetMcpTransport.ts` + `index.ts`.
- D2 Two call paths over one host connection: `createWidgetMcpTransport` / `useWidgetMcpTransport` (`callTool` that never rejects,
  `{ structuredContent, isError, reasonCode, content }`, McpToolCaller-assignable) for widgets that call tools themselves (034, 035,
  040, task 011's email host); `createMcpFetch` (rethrows a rejection, as `fetch` does) for widgets that run a shared service over
  an injected fetch. The Decisions widget uses only the second, so its error handling is byte-for-byte what it was (its
  `McpHostConnection.callTool` still rejects; `mcpHost.test.tsx` asserts it, unchanged).
- D3 Timeout (task 011 review K1): in `createWidgetMcpTransport` only, default 90 s, option `timeoutMs`. A bridge that never answers,
  rejects or throws, or answers with a non-result, becomes `isError: true`, `reasonCode` = `widget.host_timeout` /
  `widget.host_unavailable` / `widget.unreadable_result`, NO `structuredContent`; 011's email host maps that to
  `SendCommunicationError(0, 'NETWORK')` (outcome unknown, key kept for a safe retry). Tested end to end through
  `createMcpEmailComposeHandlers`. A late answer after the timeout is dropped (and a late rejection marked handled).
  Not added to the Decisions path: ext-apps' `callServerTool` already rejects after its own 60 s request timeout (`HB=60000` in
  `react-with-deps.js`), and a second, different timer there would change the widget's behaviour (brief: keep its error handling exactly).
- D4 Imports by relative path (`../../../shared/mcp/...`), no alias. Build wiring in the widget: tsconfig `include` += `../shared`
  and a `paths` entry for `@modelcontextprotocol/ext-apps/react-with-deps` (tsc cannot find the package from the shared folder);
  vite's `resolveSharedLibDeps` covers `../shared`; jest `roots` += `../shared` so the shared tests run in this suite.
- D5 Placement (ADR-012): a source folder under `src/client/widgets/shared/`, no `package.json`, so not a shared package and outside
  the census (`SharedPackageCensusTests` reads `src/client/shared/*/package.json`). Path C (comply) per the planning decision; no ADR
  conflict raised.
- D6 Test scope beyond the criteria, one line each: synchronous-throw bridge (a rejection by another route); default timeout for a
  non-positive value (the option's contract); email-host NETWORK mapping (the reason the timeout exists, from the brief); the
  `createMcpFetch` `title` option default and override (a new option this task added); token, storage, page-theme and raw-colour
  patterns in the source guard (the Decisions widget's own guard applied them and no longer sees the moved files).

## Results
- Decisions widget jest: before 18 suites / 351 passed / 2 skipped (the two built-page checks skip until `dist/` exists); after
  19 suites / 381 passed / 0 skipped (the shared suite adds 28; the two built-page checks ran because `dist/` existed). No test file changed.
- `npm run build` (tsc --noEmit incl. ../shared, vite build, check-bundle-size.mjs, built-page surface test 40/40): passes.
- Bundle (`dist/index.html`): before 1,630,490 bytes, after 1,630,688 bytes (+198 B; limit +5 KB). Still WARN over the 1 MB aim, under 3 MB.
- Mutation check: removing the timeout race and the catch fails 5 of the (then 25) shared tests.

## Quality gates (inline)
- code-review: one F2 fixed (the moved files had lost the Decisions guard's page-theme / raw-colour checks; added to the shared guard
  with must-fire controls). K4: the transport's no-answer text carries the bridge's error message (client-side only; the email host
  shows it as a NETWORK detail). K4: vite's react plugin `include` does not list `../shared` (dev fast-refresh only; the shared files
  have no JSX). Out of scope F2, filed: D-021-1 (CI path filters).
- adr-check: ADR-012 compliant (Path C, source folder, no package); ADR-021, ADR-028, ADR-038, ADR-010 compliant; no violations.

rework: fix-round-1 task 021

## For the main session
- CAPABILITY-INDEX row proposed in notes/capability-index-pending.md (task 021).
- D-021-1 in notes/defer-issues.md: CI path filters (`ci-tier1-blocking.yml`, `publish-bff-artifact.yml`) miss `src/client/widgets/shared/**`.
- notes/decisions.md: nothing new beyond D1-D6 here (the placement decision was already recorded).

## Verifier pass and fix round (2026-11-25)
- Pass (opus): Decisions behaviour parity confirmed (titles, exports, single module instance). 4 F2 fixed in c4b9ae2066:
  - the fetch/MSAL guard now runs in the PR build;
  - no-answer results carry fixed user-safe text, with the raw bridge text in a non-displayed `diagnostic` field;
  - fake-timer tests cover the late answer and timer cleanup;
  - the guard scans all of src/client/widgets/;
  - the EmailComposer path was added to both CI filters.
- Seeding: every fix has a failing-when-reverted test except the `call.catch` line, which is redundant because Promise.race already subscribes. It was kept and its comment says so.
- Widget: npm test 390 pass, tsc 0, build ok, 1.56 MB.
- K: the ext-apps 60 s timeout fires before the 90 s backstop, so `widget.host_unavailable` covers both not-connected and SDK-timeout; both are treated as outcome-unknown. #3196 is filed for the pre-existing Worklist/DecisionWizard filter gap.
