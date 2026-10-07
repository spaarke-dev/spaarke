# Task 075: dead code and stale premises

> Base: master `08b70d6cc` (after #1091). Rigor FULL, sonnet @ high (run on Opus 5.5), directional. Symbols are named
> by symbol; line numbers drift (every POML line reference had moved since the 2026-09-21 review).

## 1. What each finding turned out to be

| # | The review said | What the code says now | Done |
|---|---|---|---|
| (a) | `outlook/OutlookHostAdapter.ts`, 502 lines, dead; edited 5× to keep `tsc` quiet | Confirmed: its only references were itself and a doc example in `HostAdapterFactory.ts`; the Outlook pane registers `shared/adapters/OutlookAdapter.ts` (`outlook/taskpane/index.tsx`) | **Deleted** (`git rm`); the doc example now registers `OutlookAdapter` |
| (b) | `mintDocumentShareLink` duplicated in `shareLinkService.ts` and `sendEmailService.ts` | Confirmed; the copies differed only in wording ("…to link." / "…document link" vs "…to share." / "…share link") | `sendEmailService` imports the exported one from `shareLinkService` (the share-link service is its home). Send Email's minting failures now read "share link". `shareLinkService`'s header no longer defends the duplicate |
| (c) | `HostAdapterFactory` uncalled API + unused `cachedAdapter` | Confirmed: production calls only `registerAdapter` and `createAndInitialize` (plus `create`/`detectHostType` inside them) | **Removed** `getOrCreate`, `clearCache`, `hasAdapter`, `getRegisteredHosts`, `waitForOfficeReady`, `cachedAdapter`. The one test that read `hasAdapter`/`getRegisteredHosts` now asserts the same fact through the public API (`create()` throws for both hosts while nothing is registered). `shared/adapters/index.ts` example updated |
| (d) | Stale "the no-target branch exists for the contract rather than for traffic" comments | Already handled by task 065: the only remaining match (`OfficeService.cs`) is 065's correction, which quotes the old claim to say it was false | Nothing to do; grep evidence in §3 |
| (e) | An unexpected save exception → `OFFICE_INTERNAL` → **HTTP 400** with `ex.Message` on the wire | Confirmed, and worse: the `SaveError` also carried `Details = ex.ToString()` (the stack trace). It never reached the wire (`MapSaveErrorToProblem` sends only the message), but it was one mapping away | `OfficeErrorCodes.InternalError` (`OFFICE_INTERNAL`) added: title "Save Failed", type `internal-error`, **500**. The save's catch-all returns a generic message, no `Details`; `MapSaveErrorToProblem` renders the code with its own status. Every other `OFFICE_INTERNAL` site in `Api/Office/**` was already a 500 with a generic message |
| (f) | `OfficeDocumentPersistence` hard-codes `spaarkedev1.crm.dynamics.com` and an app id → "replace with configuration" | **The method (`GenerateDataverseUrl`) has no caller anywhere** | **Deleted** rather than configured. Configuring a method nothing calls would keep dead code alive; the configurable record-link builder already exists (`DeepLinkBuilder`, `Spaarke:Environment:OrgUrl` + `Spaarke:ModelDrivenApps:DefaultAppId`). Deviation from the POML wording, same goal: no hard-coded environment in the Office surface |

**Escalation trigger (the pane's handling of a 5xx): did not fire.** `useSaveFlow` renders any non-OK ProblemDetails
through `describeCollisionFailure` → `mapProblemDetailsToMessage`, which keys on `errorCode` and prefers the server's
`detail`; the status code does not change the UI path. `OFFICE_INTERNAL` has no client catalog entry, so the pane shows
the server's title and detail, with Retry (`retryable: true`), exactly as before. A jest test pins it (§2).

## 2. Tests

| Test | What it proves |
|---|---|
| `tests/integration/contract/Api/Office/OfficeSaveUnexpectedErrorContractTests.cs` (new) | A save whose `sprk_document` create throws → **500** `application/problem+json`, `errorCode` `OFFICE_INTERNAL`, `retryable` true, a correlation id, and **no** exception text or stack trace in the body. **Seeded control:** with `InternalError` taken out of `MapSaveErrorToProblem`'s own-status arm, it fails: *"Expected response.StatusCode to be HttpStatusCode.InternalServerError {value: 500} … but found HttpStatusCode.BadRequest {value: 400}"* |
| `useSaveFlow.test.ts` › *Unexpected server error (task 075)* (new) | A 500 `OFFICE_INTERNAL` problem from the save ends in `flowState` `error`, with the server's detail as the message, recoverable, `onError` called once |
| `HostAdapterFactory.test.ts` › *starts empty* (changed) | Same fact through the public API, since the members it read are gone |
| Existing `shareLinkService.test.ts`, `sendEmailService.test.ts`, `commands.test.ts` | Both share-link call sites (Word ribbon Share, Send Email), unchanged and green |
| Existing `OfficeSaveSpineIdempotencyTests.RetryAfterACreateSaveThatThrewMidway…` | Uses the same fault; asserts only "not a success", so a 500 still satisfies it |

## 3. Gates

| Gate | Result |
|---|---|
| (a) grep | `OutlookHostAdapter`: **0** references in `src/client/office-addins` |
| (b) grep | `mintDocumentShareLink` defined **once** (`shareLinkService.ts`), imported by `sendEmailService.ts` and `word/commands/index.ts` |
| (d) grep | `for the contract rather than|rather than for traffic|traffic-free` in `Services/Office/**`, `Api/Office/**`: only 065's correction |
| office-addins | typecheck **68** (the test-file baseline; 0 production); lint **0**; jest **62 suites / 817 tests** (+1); `npm run build` exit 0 (with placeholder env values; the build refuses to run without them) |
| BFF suite | **13,072 passed / 0 failed / 54 skipped**: 068's 13,071 + the 1 new contract test, reconciled exactly |
| ArchTests | **337 / 337** |
| Publish size | fresh master `08b70d6cc` **47,673,759 B** → + this change **47,673,429 B** = **−330 B** (a reduction); **212 = 212 files**; `Compress-Archive` Optimal, PDBs included, fresh short-path worktrees (removed) |
| CVE | No package change |

## 4. Quality gates: code review and ADR check

**Code review** (coverage-first, own pass over a ~250-line diff that is mostly deletion):

| Finding | Severity | Decision |
|---|---|---|
| Send Email's minting-failure text now says "share link" where it said "document link" | Suggestion | Accepted: the one minter's wording; no test pinned the old text |
| `"OFFICE_INTERNAL"` is still a literal at ten other sites (workers, the job stream, other routes' catch blocks) | Suggestion | Left: each already returns a 500 with a generic message; converting them is churn without behaviour |
| The failed job's row still stores `ex.Message` in `sprk_errormessage` | Suggestion | Accepted: server-side column. A 060+ job read uses the saved view, which carries no error text; only a pre-060 row would surface it, and no new failure writes one |
| Two docs named the deleted file (`office-outlook-teams-integration-architecture.md` tree, `knowledge/sharepoint-embedded/NOTES.md` ×3) | Warning | **Fixed**: both point at `shared/adapters/OutlookAdapter.ts` |
| Verified | — | The Outlook pane registers `OutlookAdapter` (unchanged); both share-link call sites import one function; every other `OFFICE_INTERNAL` site in `Api/Office/**` is a 500 with a generic message; the pane's error path keys on `errorCode`, not status |

**ADR check:** ADR-019 ✅ (500 ProblemDetails with `errorCode`, no internals on the wire) · ADR-008 ✅ (filters
unchanged) · ADR-010 ✅ (no new DI) · ADR-021 n/a (no UI change) · ADR-038 ✅ (tests in KEEP paths: `contract/`, the
hook's unit suite). No violations.
