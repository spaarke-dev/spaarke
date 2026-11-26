# Task 034 — spaarke_get_briefing and the Daily Briefing widget (FR-09, FR-16)

## D-034-render — which briefing call the tool makes

- **Decision:** the tool returns what `POST /api/ai/daily-briefing/render` returns (`DailyBriefingNarrateResponse`).
- **Why:** `/render` is the only briefing call the Console's `DailyBriefingApp` makes (`useBriefingRender` -> `fetchBriefingLive`).
  `/summarize` is not called by it, and `/narrate` takes a payload a tool cannot supply. So the Console and Copilot show the same briefing
  for the same user and windows.
- **How:** the route's rules moved into one private `RenderCoreAsync` in `DailyBriefingEndpoints.cs`. The route handler maps its answer to the
  same `IResult` as before (REST bytes unchanged: the existing route tests pass untouched). A new public
  `DailyBriefingEndpoints.RenderForCallerAsync(HttpContext?, RenderDailyBriefingRequest?, ct)` runs the same core for the tool and reads the three
  services from `HttpContext.RequestServices`, so `Api/Mcp/**` names no `Services.Ai` type (ADR-013; same precedent as `ListPlaybooksForCallerAsync`).
- **Not cached:** the briefing is regenerated per call, as the Console's Refresh does.

## Decisions

- **No FR-14 start/status.** The render is one synchronous composite call, not a playbook run: there is no run record to start and poll. The
  tool enforces `McpLongRunning.Budget - 3 s` (27 s) with a linked cancellation token and answers `briefing.timeout` (504, fixed text that says to
  ask again) instead of hanging. Whether real briefings exceed 27 s is unmeasured: measure at dev deploys 039/043 (deferred, D-034-1).
- **Argument check.** `dueWithinDays` (1-31) and `recencyHours` (1-744), both optional; the schema is closed. The route clamps; the tool refuses
  an out-of-range or mistyped value with `briefing.invalid_parameter` (400), because a model that asks for 40 days should be told, not silently
  given 31. The message names the fix and never echoes the value.
- **Reason codes.** `DATAVERSE_USER_CONTEXT_REQUIRED` (no caller 401; a caller with no Spaarke user 403, as the route), `briefing.unavailable`
  (503: feature off, binding unconfigured, circuit open), `briefing.failed` (500), `briefing.timeout` (504), `briefing.invalid_parameter` (400).
  The route's own 503 detail for an unconfigured binding (`ex.Message`) is never copied into a result.
- **Model-visible text (ADR-015).** Counts line, then the TL;DR summary and key takeaways, each value through `McpText.SingleLine`, clipped to at
  most 1,000 characters. Per-channel detail (record names, descriptions) is only in `structuredContent`, for the widget.
- **Size bound (NFR-02).** At the route's worst case (50 rows in each of 4+ channels) the body is over 64 KiB. `BriefingSizeBound` keeps the
  structured briefing under 56 KiB: it blanks the bullets' descriptions first, then keeps the first N bullets of every section (N halving), so no
  section disappears; the text says how many highlights were left out. A briefing that fits is returned untouched (REST parity).
- **`ReadOnly = true` although the route writes.** The route writes a capability-ledger entry and the proactive suggestions on every render
  (FR-15). Neither is a user-visible change and the Console does the same on load, so the tool is annotated read-only and idempotent. Known limit
  K1: a client that treats `readOnlyHint` as "no side effect at all" is slightly wrong about the ledger write.

## Widget decisions

- **First read = the opening result.** The host delivers the tool result that opened the widget (`toolResult` on the shared host connection). The
  widget's first `renderBriefing` takes it (single use, up to 45 s wait) instead of calling the tool again, so opening the briefing costs one
  generation. A second read (Refresh) calls the tool. If no result arrives, the widget asks for the briefing itself.
- **Default window mismatch (known limit K2).** The opening result is generated with the tool's defaults (the BFF's `BriefingWindowOptions.Default`:
  5 days, 120 hours). The Console's default preference (`timeWindow` 7d) sends 168 hours, which the widget sends on its own reads (Refresh). So the
  first view and a refreshed view can differ at the edges of the window. Fix, if wanted: make the BFF default 168 h or have the model pass the
  window; owner's call (D-034-2).
- **Bundle: 1.13 MB** (target 1 MB, ceiling 3 MB; `check-bundle-size.mjs` warns, passes). Kept down by build-time shims
  (`src/shims/*`), not by changing the Console component:
  - `@spaarke/auth` -> a shim whose `authenticatedFetch` always rejects (no fetch, no MSAL in a sandboxed page);
  - `@spaarke/ui-components` (barrel) -> deep-path re-exports of what the briefing package uses, plus stand-ins for `SendEmailDialog` and
    `RichFilePreviewDialog` that draw nothing (the component opens them only from actions a widget hides; the composer carries lexical; 1.56 MB -> 1.16 MB);
  - `@spaarke/ui-components/services` (barrel, which pulls marked, DOMPurify, Application Insights, chat) -> five deep-path re-exports.
  - Types come from the package's deep paths, not its barrel (the barrel registers the Console's workspace widget).
- **Hidden, not stubbed:** with an injected `BriefingDataService` the component hides inline To Do, dismiss, email, document preview, the
  Preferences menu and record links (task 010). The widget supplies `renderBriefing` only and `navigation.openUrl` (mapped to the host's open-link).
- **Zero network/Xrm:** `App.test.tsx` mounts the real component, mocks `@spaarke/auth` and `getXrm`, defines a counting `window.Xrm`, and asserts none is
  touched on open, on Refresh and with the actions hidden.
- **Theme:** `hostTheme.ts` is now in `src/client/widgets/shared/mcp/` (exported from the barrel) for the r2 widgets; the Decisions widget keeps its
  own identical copy (r1 file, not touched; D-034-3).
- **Not StrictMode** in `main.tsx`: an effect running twice would trigger a second generation.

## CI and deploy

The widget is embedded the same way the Decisions widget is, so each place that builds the Decisions widget builds this one too:
`publish-bff-artifact.yml` (build, embed check, and the built-widget gate now requires BOTH `*WidgetResourceTests` built cases to pass, per class),
`deploy-bff-api.yml`, `ci-tier1-blocking.yml` (mcp-conformance build + the `mcp_conformance` path filter), `tests/integration/Mcp.Conformance.Host/run-conformance.sh`
and `scripts/Deploy-BffApi.ps1` (now loops over both widgets; parse-checked). The workflows' YAML parses. Not run: GitHub Actions itself.

## Tests (summary)

- Server: `tests/integration/contract/Api/Mcp/Briefing/GetBriefingToolTests.cs` (descriptor and widget meta; REST parity both call orders; windows forwarded;
  token's caller only; unknown Spaarke user = 403; text single-line, no record names, <= 1,000; empty; bad arguments x8 with no echo; no-caller x3; 401/503;
  faults x3 with no exception or binding text; timeout and client cancel; worst-case size; size bound x4; one telemetry line, no oid) and
  `BriefingWidgetResourceTests.cs` (list/MIME/empty csp; unknown uri; 401/503; built page; not-built error; startup warning). Both tool-set pins and the
  Decisions resource list assertion updated; one ArchTests ledger row (`RouteAuthorizationGuardTests.Mcp.Briefing.cs`).
- Widget: `App.test.tsx`, `briefingMcpService.test.ts`, `widgetSurface.test.ts` (source scan + built-page scan), shared `hostTheme.test.ts`.

rework: none task 034
