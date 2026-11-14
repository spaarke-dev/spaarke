# Wave 7 review (tasks 010, 031; 040 held) — 2026-11-14

Task 010: two Fable adversarial passes (auth), one fix round, a scoped Sonnet re-check, then a peer review by uac-r2 against FR-02's four checks and a second fix round. Task 031: one Fable pass, one fix round, a scoped Sonnet re-check and a second, small fix commit. Task 040 is held for the owner's batch D results (T2, T14, T10).

## Fixed (F-class)

| Task | Finding | Fix commit |
|---|---|---|
| 010 | Only one caller: no test could see a context leak between requests (F3) | `f00e445910` (two callers in flight at once; second caller with no cached token fails closed) |
| 010 | Real-host schema test vacuous for the two static-schema tools (F3) | `f00e445910` (narrowed to inferred-schema tools; static ones pinned separately) |
| 010 | No-app-only-fallback proof refused every mint, so a fallback also failed and the test stayed green (F3, uac-r2) | `3e053cd66e` (app-only cache seeded with a distinct token; wire assertion first; mints exactly +1) |
| 010 | "Nothing logged or forwarded" (FR-02 check 4) untested: NullLogger, wire recorded only Authorization (F3, uac-r2) | `3e053cd66e` (Trace log capture; all headers and body recorded; six seeded faults go red) |
| 031 | Build failed in CI: `@fluentui/react-icons` undeclared and unmapped (F4) | `011aad8e50` |
| 031 | Review bar and notice placement and ARIA (F1) | `011aad8e50` (`belowHeader` slot on the shared `WorklistLanePanel`, additive) |
| 031 | `OpenWizardEvent.core` emitted an invented `recordType` (F4) | `011aad8e50` (`{ recordId }` only) |
| 031 | Billing row dropped `ruleShortName` (F1) | `011aad8e50` |
| 031 | Storage scan on source, category test hand-restated (F3) | `011aad8e50` (built-file scan; parity corpus) |
| 031 | Built-file storage scan never ran in CI (skipped without `dist/`) (F3) | `445a9786a3` (runs as the last step of `npm run build`; seeded `localStorage` fault fails the build) |
| 031 | `@spaarke/sdap-client` unmapped for jest and vite (F4) | `445a9786a3` |

Also: `PerSessionExecutionContext` documented as inert in stateless mode (SDK `StreamableHttpHandler.cs:551/:629`); Stateless is the guard (`f00e445910`, `7f640afad0`).

## Known limits (K-class)

| Task | Limit | Owner / next |
|---|---|---|
| 010 | Tests 1–4 run on a host composed from `AddMcpModule` + `MapMcpEndpoints`, not `Program` (uac-r2 checked `UseSpaarkeMiddleware`) | Production-DI schema test: owner question (010 or 025) |
| 010 | The decision tools' own app-only reads (`SignalCoreRecordAccess`) are not observed here | 025 |
| 010 | No negative for another caller's plan read satisfying a commit (stamp keyed by oid; outside FR-02) | 042 |
| 010 | The capturing logger records message text and structured values, not the exception object | Accepted unless uac-r2 asks |
| 031 | A pending `openLink` can set a stale (generic) notice after `service` changes | 032 |
| 031 | A repeated "Nothing in this view" notice is not re-announced (state batching) | 032 |
| 031 | Filter key not reset when `service` changes | 032 |
| 031 | Built-file scan regex expects the lane key as its own declaration; a minifier change fails it (not a false pass) | Known |
| 031 | "Done today" not supplied by any tool | DEF-001, #2832, owner decision |
