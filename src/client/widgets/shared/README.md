# Shared widget source (`src/client/widgets/shared/`)

Source shared by the Spaarke MCP App widgets (`src/client/widgets/*`, the single-file `ui://spaarke/*.html` pages the BFF serves
through MCP `resources/read`). It is a **source folder, not a package**: it has no `package.json`, and each widget imports it by
relative path and builds it with its own toolchain and `node_modules`. It is not in `@spaarke/ui-components`, because everything
here runs only inside the widget sandbox and imports the MCP Apps SDK (`@modelcontextprotocol/ext-apps`), which no other surface
should inherit (planning decision "shared widget transport placement", `projects/spaarkeai-mcp-bff-api-r2/notes/decisions.md`; ADR-012).

## `mcp/`: the widget MCP transport (spaarkeai-mcp-bff-api-r2 task 021, FR-16)

A widget has no network origin, no token and an empty `_meta.ui.csp`: every read and write is an MCP tool call that the host proxies
(`app.callServerTool`). No source file in this folder or in any widget (`src/client/widgets/`) calls `fetch`, `XMLHttpRequest`, sockets or beacons, or uses a token, MSAL,
browser storage, the page's colour scheme or a raw colour. `mcp/__tests__/widgetMcpTransport.test.ts` checks this; it skips `node_modules`, `dist`, `__tests__` and test
files, and `npm run build` in `spaarke-decisions` runs it (so PR CI does). The built page is checked separately by the widget's `widgetSurface.test.ts`.

| File                        | What it gives a widget                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                  |
| --------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `mcp/mcpHost.ts`            | `McpHostProvider`: the handshake with the host, the latest host context (theme, display mode; every change is merged, so a theme switch re-renders), the result that opened the widget and the arguments of that call (`toolInput`, optional; task 035), `callTool` (rejects on a transport failure), `openLink`, `requestDisplayMode`. Imports the SDK.                                                                                                                                                                                                                                                                                 |
| `mcp/mcpHostContext.ts`     | The connection shape (`McpHostConnection`, `HostContext`), `useMcpHost()` and `DISCONNECTED`. No SDK import, so a component test renders under `McpHostContext.Provider` with a plain object.                                                                                                                                                                                                                                                                                                                                                                           |
| `mcp/widgetMcpTransport.ts` | `createWidgetMcpTransport(callServerTool, { timeoutMs })` / `useWidgetMcpTransport()`: `callTool(name, args)` that never rejects and never waits longer than the timeout (default 90 s), returning `{ structuredContent, isError, reasonCode, content }`. Structurally an `McpToolCaller` (`@spaarke/ui-components` EmailComposer, task 011). A missing answer (rejected or throwing bridge, timeout, unreadable answer) is `isError: true` with a `TransportReason` code, fixed user-safe `content` (the bridge's own error text goes to `diagnostic`, never to `content`) and no `structuredContent`, which the email host reads as an outcome-unknown `NETWORK` error. |
| `mcp/mcpFetch.ts`           | `createMcpFetch(callTool, routes, { title })`: a `ResponseFetchFn` that turns a shared service's REST request into a tool call and answers with the `Response` REST would have sent (ProblemDetails on failure). A rejected host call is rethrown, as `fetch` rejects on a network failure. Use it to run a `@spaarke/ui-components` service that takes an injected `fetch`.                                                                                                                                                                                            |
| `mcp/index.ts`              | Barrel of all of the above. It loads the SDK (through `McpHostProvider`); a test that must not load the SDK imports the deep paths.                                                                                                                                                                                                                                                                                                                                                                                                                                     |

Which call path to use: a widget that calls tools itself uses `useWidgetMcpTransport()` (or hands it to `useMcpEmailHostProps` as the
`caller`). A widget that runs a shared service over an injected `fetch` uses `createMcpFetch` over `useMcpHost().callTool`. The
Decisions widget uses only `createMcpFetch`; its own titles for a failed commit are in `spaarke-decisions/src/mcp/mcpFetch.ts`.

## Importing it from a widget

The Decisions widget (`src/client/widgets/spaarke-decisions/`) is the reference:

- **Imports**: by relative path, for example `import { McpHostProvider } from '../../../shared/mcp/mcpHost'` from `src/mcp/`.
- **`tsconfig.json`**: add `"../shared"` to `include` (so `tsc --noEmit` checks this folder and its tests), and map the bare names its
  files import to the widget's `node_modules`: `react` (already mapped), `@spaarke/ui-components/*` (already mapped) and
  `"@modelcontextprotocol/ext-apps/react-with-deps": ["node_modules/@modelcontextprotocol/ext-apps/dist/src/react/index.d.ts"]`.
- **`vite.config.ts`**: resolve bare imports from files under `../shared` from the widget's own `node_modules` (the
  `resolveSharedLibDeps` plugin covers both shared source roots), and keep `dedupe: ['react', 'react-dom']`.
- **`jest.config.cjs`**: `react` is already mapped and `modulePaths` points at the widget's `node_modules`. The Decisions widget also
  lists `'<rootDir>/../shared'` in `roots`, so this folder's tests run in its suite; a second widget need not run them again.
- **Tests that mount `McpHostProvider`** replace the ESM-only SDK with `jest.mock('@modelcontextprotocol/ext-apps/react-with-deps', ..., { virtual: true })`
  (see `spaarke-decisions/src/__tests__/mcpHost.test.tsx`).
