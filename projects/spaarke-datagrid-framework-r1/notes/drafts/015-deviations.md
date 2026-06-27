# 015 — Deviations from Task 015 Design

> **Status**: Recorded at task 015 completion (2026-06-01)
> **Filer**: task 015 sub-agent
> **Audience**: project owner + reviewer at PR time + sibling agents (017, 023, 024, 041, 052) that consume `BffDataverseClient`

---

## D1. `authenticatedFetch` is injected via constructor, NOT imported from `@spaarke/auth`

### POML guidance
The task POML (`<constraints>` + `<knowledge>`) and the task brief said:
> "Uses `@spaarke/auth.authenticatedFetch` for ALL HTTP calls (no direct `fetch`)."
> "If you can't determine the exact import, document in deviations and use a reasonable default."

### What 015 actually shipped
`BffDataverseClient` takes `authenticatedFetch` via constructor options:

```typescript
new BffDataverseClient({
  authenticatedFetch,   // REQUIRED — caller passes from `@spaarke/auth`
  bffBaseUrl,           // optional — falls back to window/env
});
```

The class does NOT contain `import { authenticatedFetch } from '@spaarke/auth'`.

### Why
1. **`@spaarke/auth` is NOT a dependency of `@spaarke/ui-components`.**
   - It is not listed in `src/client/shared/Spaarke.UI.Components/package.json` (`peerDependencies`, `dependencies`, or `devDependencies`).
   - It is not installed in `Spaarke.UI.Components/node_modules/@spaarke/`.
   - It is not aliased in `tsconfig.json` or `jest.config.js` `moduleNameMapper`.
   - Adding a direct import would cause an unresolved-module TypeScript error at build time.

2. **The established shared-library pattern is DI of `authenticatedFetch`.** Existing precedents in the same package:
   - `src/services/EntityCreationService.ts` — defines `AuthenticatedFetchFn` as a local type alias, takes it via constructor.
   - `src/utils/adapters/bffDataServiceAdapter.ts` — same pattern, file header explicitly documents this decoupling.
   - `src/services/communicationApi.ts` — file header explicitly states "Does NOT import from `@spaarke/auth` directly — `authenticatedFetch` is injected so the shared library stays decoupled."

3. **ADR-028 spirit is preserved.** The ADR mandates `authenticatedFetch` as the only sanctioned auth surface for BFF calls. Consumers of `BffDataverseClient` still pass `authenticatedFetch` from `@spaarke/auth` at the call site — token acquisition, 401 retry, and `Authorization` header attachment happen inside `@spaarke/auth`. This client never sees raw tokens and never attaches a manual `Authorization` header. The constructor enforces this contractually: `authenticatedFetch` is required, the missing-fn case throws `BffDataverseClientConfigurationError`.

### Risk / mitigation
- **Risk**: A consumer could in principle pass a hand-rolled `fetch` wrapper that does not handle token acquisition correctly. Mitigation: JSDoc on `AuthenticatedFetchFn` and `BffDataverseClientOptions.authenticatedFetch` explicitly cites `@spaarke/auth` as the canonical source and warns against alternatives. Same risk affects `bffDataServiceAdapter` already.
- **Impact on sibling tasks**: tasks 017, 023, 024, 041, 052 that wire `BffDataverseClient` into hosts must `import { authenticatedFetch } from '@spaarke/auth'` and pass it to the constructor. Documented in `BffDataverseClient` file-level JSDoc.

---

## D2. Pagination = Option A (always pass `pagingCookie: undefined`)

### Task brief options
> "**Option A**: Always pass `pagingCookie: undefined` (caller can't paginate via this client). Simpler."
> "**Option B**: Extend the method with a 3rd optional param. Changes the interface contract."

### What 015 shipped
**Option A**. The `retrieveMultipleRecords` body is:
```typescript
JSON.stringify({ entityName, fetchXml, pagingCookie: undefined })
```
`JSON.stringify` drops `undefined` properties, so the wire-level body is `{"entityName":"...","fetchXml":"..."}`. The BFF treats absent `pagingCookie` identically to `undefined`/null (verified against `B-Wave-1` endpoint contract).

### Why
1. The `IDataverseClient` contract from task 001 (`src/client/shared/Spaarke.UI.Components/src/services/IDataverseClient.ts`) defines:
   ```typescript
   retrieveMultipleRecords<T>(entityName: string, fetchXml: string): Promise<FetchMultipleResult<T>>
   ```
   The 2-arg signature does not expose `pagingCookie`. Adding a 3rd parameter would diverge from the contract, breaking the "consumer swap requires zero code change" acceptance criterion (#1) for `BffDataverseClient` ↔ `XrmDataverseClient`.

2. Paging is a framework concern — `useLazyLoad` (task 003) handles paging via `XrmDataverseClient`'s direct path. The framework's lazy-load loop interacts with `pagingCookie` outside the `IDataverseClient` boundary. Extending the contract just for `BffDataverseClient` would create asymmetry.

3. R1 read-only scope: full paging through the BFF surface is a future-R extension. The `FetchMultipleResult<T>` return shape already includes `pagingCookie?` — the consumer receives it from the response body and can implement paging downstream once a contract extension is agreed.

### Documented in code
`BffDataverseClient.retrieveMultipleRecords` JSDoc explicitly notes the Option A choice and the rationale (file lines reference §"R1 — Option A" in the doc).

### Risk
None for R1. If a future task needs paging via `BffDataverseClient`, the interface extension can be added with a default-undefined parameter, preserving back-compat.

---

## D3. `BFF_UNKNOWN_ERROR` fallback when response body is not parseable ProblemDetails

### Task brief
> "If body isn't JSON or doesn't have ProblemDetails shape, throw `BffServerError` with `errorCode = 'BFF_UNKNOWN_ERROR'`."

### What 015 shipped
Implemented exactly as specified, with a small clarification: `mapErrorResponse` is invoked for ANY non-2xx response, not just 5xx. The error-class selection cascades:
- 404 → `BffNotFoundError`
- 403 → `BffForbiddenError`
- 400 → `BffBadRequestError`
- 5xx → `BffServerError`
- All other 4xx (401, 405, 409, etc.) → generic `BffDataverseClientError`

This preserves the brief's intent (typed errors for the documented status codes) while ensuring nothing falls through to a generic `Error` (which would lose `errorCode` + `correlationId`).

### Test coverage
- `non-JSON error response → BFF_UNKNOWN_ERROR fallback` test (returns 503 with text/plain body).

---

## D4. Stable export surface — typed errors exported from `services/index.ts` barrel

The brief asked for the typed errors to be exported. Done in `src/services/index.ts`. The barrel exports:
- `BffDataverseClient` (class)
- `BffDataverseClientOptions` (type)
- `BffAuthenticatedFetchFn` (re-exported `AuthenticatedFetchFn` under a Bff-prefixed alias to avoid collision with the existing `AuthenticatedFetchFn` re-export from `EntityCreationService`)
- All 6 typed-error classes (`BffDataverseClientError`, `BffDataverseClientConfigurationError`, `BffNotFoundError`, `BffForbiddenError`, `BffBadRequestError`, `BffServerError`)

The alias `BffAuthenticatedFetchFn` was needed because `services/index.ts` already exports an unrelated `AuthenticatedFetchFn` from `EntityCreationService` (line 27). Reusing the same name would cause a TS duplicate-export error. Consumers can also import `AuthenticatedFetchFn` directly from `'./BffDataverseClient'` if preferred — both are the same structural type.

---

## D5. No write methods to throw from (R1 read-only)

The POML notes mentioned: "R1 BffDataverseClient is READ-ONLY — there's no write endpoint yet; calling write methods throws a clear error so devs know to use Xrm impl for writes."

Closer inspection of `IDataverseClient` shows it defines NO write methods at all (5 methods are all reads: `retrieveSavedQuery`, `retrieveSavedQueriesForEntity`, `retrieveEntityMetadata`, `retrieveMultipleRecords`, `retrieveRecord`). There are no `createRecord` / `updateRecord` / `deleteRecord` to throw from. The read-only nature is structural — enforced by the interface, not by runtime throws.

The brief's mention of `BffDataverseClient.WritesNotSupportedError` is therefore obsolete for R1. File-level JSDoc documents this and points consumers to `bffDataServiceAdapter` / `XrmContext` CRUD paths for writes.

---

## Files touched

- **NEW**: `src/client/shared/Spaarke.UI.Components/src/services/BffDataverseClient.ts` (~360 LOC)
- **NEW**: `src/client/shared/Spaarke.UI.Components/src/services/__tests__/BffDataverseClient.test.ts` (~430 LOC, 22 tests)
- **MODIFIED**: `src/client/shared/Spaarke.UI.Components/src/services/index.ts` (added BffDataverseClient + typed errors to barrel)
- **MODIFIED**: `projects/spaarke-datagrid-framework-r1/tasks/015-bff-dataverse-client.poml` (status → completed)

## Build / test evidence

- `npm run build` → 0 errors.
- `npm test -- --testPathPatterns=BffDataverseClient` → 22 passed, 0 failed, 13.3s.
- `grep -E "fetch\(" BffDataverseClient.ts` → 0 matches (no raw fetch).
- `grep -E "Authorization:" BffDataverseClient.ts` → 1 match (in JSDoc comment describing `authenticatedFetch` behavior — not in code).
