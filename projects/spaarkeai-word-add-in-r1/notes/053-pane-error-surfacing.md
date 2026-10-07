# Task 053: the pane stops swallowing quick-create/search failures

> **Task**: `tasks/053-pane-swallows-quickcreate-errors.poml`
> **Date**: 2026-09-17
> **Author**: task-execute sub-agent (sonnet / high / directional)
> **Status**: implemented, gated, ready to merge

---

## 1. The two sites, re-located by symbol (line numbers had drifted, exactly as the POML warned)

Both sites are in `src/client/office-addins/shared/taskpane/components/SaveFlow.tsx`:

- **`createRelatedRecord`** (the callback passed as `RelatedToPicker`'s `onCreateRecord` prop) — `POST /api/office/quickcreate/{type}`. Was `if (!res.ok) return null;`. This is the one task 031 made urgent: an unresolvable caller now gets a 403 `owner_unresolved` here, refused before any write.
- **`relatedSearch`** (passed as `onSearch`) — `GET /api/office/search/entities`. Was `if (!res.ok) return [];`, rendering a failed search identically to zero matches.

Both functions still had their original body shape (test-harness branch, config guard, fetch, parse, return) — task 025's ~56-line merge added the collision-handling machinery elsewhere in the file (the top-level `error`/`renderCollisionState` path), not inside these two functions, so only the line numbers moved, not the functions' shape.

## 2. What the server actually sends — verified against C#, not assumed

The POML said the 403's code lives at `extensions.code`. That is true for the **global exception handler** (`MiddlewarePipelineExtensions.cs`, the file task 050 fixed) — but `owner_unresolved` does **not** go through that handler. `OfficeEndpoints.QuickCreateAsync` has its **own** `catch (SdapProblemException problem)` block (pre-existing, from task 030) that calls ASP.NET's `Results.Problem(..., extensions: { ["errorCode"] = problem.Code, ["correlationId"] = traceId, ["entityType"] = entityType })`. `Results.Problem`'s `extensions` dictionary is a `ProblemDetails.Extensions` `[JsonExtensionData]` member, which **flattens to top-level JSON properties**, not a nested `"extensions"` object. Confirmed two ways:

1. Reading `OfficeEndpoints.cs:1777-1797` directly.
2. The **existing, already-passing** contract tests `OfficeQuickCreateContractTests.cs:464` and `OfficeQuickCreateProjectContractTests.cs:289` both assert `(await ReadProblemAsync(response)).Should().ContainKey("errorCode").WhoseValue.Should().Be("owner_unresolved")`, where `ReadProblemAsync` reads the response root's own top-level string properties.

So the wire shape is:
```json
{
  "type": "https://spaarke.com/errors/office/owner_unresolved",
  "title": "Project Not Created",
  "detail": "Your account could not be matched to a Dataverse user, so the new project could not be assigned to you and was not created. Ask an administrator to check that your user is provisioned in this environment.",
  "status": 403,
  "errorCode": "owner_unresolved",
  "correlationId": "...",
  "entityType": "project"
}
```
— which is exactly the shape `errorMessages.ts`'s existing `ProblemDetails` interface and `isProblemDetails`/`mapProblemDetailsToMessage` already parse (top-level `errorCode`/`detail`/`reasonCode`/`correlationId`, the same shape task 025's `OFFICE_020` collision code uses). **No new server surface was needed** — the escalation trigger did not fire.

## 3. The fix

### `errorMessages.ts`
- New catalog entry `owner_unresolved` in `ERROR_CODE_MAP` (recoverable: false — an immediate retry cannot fix an unprovisioned AAD identity; the message's own "ask an administrator" is the way forward, mirroring `OFFICE_009`'s existing posture).
- New exported `describeFetchFailure(response: Response): Promise<ErrorMessage>` — parses the body, prefers `mapProblemDetailsToMessage` when it's ProblemDetails-shaped, falls back to a status-aware generic message when it isn't (a 502/504 from a gateway has no such body). Never throws.

### `SaveFlow.tsx`
- `createRelatedRecord` and `relatedSearch` both now **throw** a descriptive `Error` (config-guard / network-exception / `describeFetchFailure(res).message`) instead of resolving `null`/`[]`. Return type of `createRelatedRecord` narrowed to `Promise<CreateRecordResult>` (no `| null`) — a narrower-return function is a valid substitute for the wider-return prop type `RelatedToPicker` still declares, so no type change was needed there.

### `RelatedToPicker.tsx` — **scope widened beyond the POML's `<relevant-files>` list; see §4**
- New `searchError` state, cleared on a new search attempt and on a type-chip switch.
- `runSearch`'s `catch` now reads the thrown `Error`'s message into `searchError` (instead of silently clearing to `[]`); rendered with the exact `matterTypesError` message+Retry idiom already shipped for the Matter Type load failure (`styles.matterTypeErrorRow` / `styles.fieldError`, `role="alert"`) — reused, not reinvented.
- `handleCreate`'s `catch` now reads `err.message` into `createError` instead of a hardcoded `Couldn't create the {type}.` string (that hardcoded string is kept as the fallback for a non-`Error` throw or a legacy `null` resolution — defensive, not the primary path any more).

## 4. Deviation: `RelatedToPicker.tsx` was not in the POML's `<relevant-files role="modify">` list

**Why the deviation was necessary, not optional.** `createRelatedRecord`/`relatedSearch` are *callback props* — `SaveFlow.tsx` never renders their result itself. The actual DOM text a failure produces is owned entirely by `RelatedToPicker.tsx`'s own local state:
- Before this task, `handleCreate`'s `catch {}` was a **bare catch with no binding** — it discarded whatever was thrown and always rendered the same hardcoded string, regardless of cause.
- `runSearch`'s `catch {}` cleared to an empty array with **no rendered feedback at all** for either a failure or a genuine zero-result search — the two were indistinguishable because neither one showed anything.

Making `SaveFlow.tsx` throw a better message was necessary but **not sufficient**: without a matching change on the `RelatedToPicker.tsx` side to *read* what was thrown, AC1 ("the pane shows the server's `detail` message") is unreachable — the message would be thrown and then discarded one file downstream. This was verified empirically, not assumed: the failing-first test in `RelatedToPicker.errorSurfacing.test.tsx` fails against the pre-053 `RelatedToPicker.tsx` (generic string rendered) and passes only once both files change together (see §6 for the actual failing→passing transcript).

`RelatedToPicker.tsx` is a sibling, non-contested file — not touched by any concurrently active task (025 already merged into this branch; 045 has not started; task 014 is server-side only, in a different worktree). Per task-execute's directional-steps rule ("adapt the sequence... if a step is wrong, do the right thing and note the deviation") and the binding contract being `<goal>` + `<acceptance-criteria>` + `<constraints>` (not the POML's `<relevant-files>` list, which the POML itself already flagged as stale on line numbers), this was treated as a legitimate scope-completion, not a scope-creep — reported here rather than silently absorbed.

This is **not** the escalation trigger firing (that trigger is specifically about needing a *server-side* response-shape change, which was not needed — §2).

## 5. Success-with-`warnings` — already surfaced, verified with evidence (Step 4)

Checked whether `QuickCreateResponse.Warnings` (business-unit defaults unreadable, an unknown Matter Type ignored) reaches the user. It does, end to end, unmodified by this task:

- `createRelatedRecord`'s success path already returns `...(data.warnings && data.warnings.length > 0 ? { warnings: data.warnings } : {})` on `CreateRecordResult`.
- `RelatedToPicker.handleCreate` already does `if (result.warnings && result.warnings.length > 0) setCreateWarning(result.warnings.join(' '));`, rendered as `<Text className={styles.fieldWarning} role="status">{createWarning}</Text>`.
- **Existing, already-passing test evidence** (unmodified by this task): `SaveFlow.matterTypeQuickCreate.test.tsx`'s third case, *"a 'not found' quick-create warning clears the matter-types cache"*, mocks exactly this response shape (`warnings: ['The selected matter type was not found; the matter was created without it.']`) and the suite passes.

No code change was needed here; this is recorded per the acceptance criterion's own "or the note records, with evidence, that they already did" clause.

Also checked the OTHER success path (`POST /api/office/save`, the main Save button, `useSaveFlow.ts`'s `SaveResponse`): its server-side model (`Models/Office/SaveResponse.cs`) has **no `Warnings` field at all** — only `Success`/`Duplicate`/`Artifact`/`Error`. There is nothing to surface there; this is not a second instance of the same defect class, just a route that has never carried warnings.

## 6. Failing → passing evidence

Before the fix, `RelatedToPicker.errorSurfacing.test.tsx`'s first case (run against the pre-053 `RelatedToPicker.tsx`, with `onCreateRecord` mocked to reject with the server's exact `detail` string) failed:
```
expect(error).toHaveAttribute('role', 'alert')
  — found the text "Couldn't create the Project." instead of the server's detail sentence
```
(reconstructed manually by reverting the `RelatedToPicker.tsx` catch-block edit locally and re-running — the pre-053 bare `catch {}` always rendered the hardcoded string regardless of what `onCreateRecord` rejected with). After both `SaveFlow.tsx` (throw instead of swallow) and `RelatedToPicker.tsx` (read the thrown message) changed together, the full suite passes — see §7.

A parallel manual check also caught a real bug in my own first draft: `SaveFlow.quickCreateErrorSurfacing.test.tsx` initially called `screen.findByLabelText('Search Matter records', WAIT)` — `WAIT` (`{timeout}`) in the *second* argument position, which is `SelectorMatcherOptions`, not `waitForOptions` (the *third* position). `npx tsc --noEmit` caught this immediately (`error TS2769: No overload matches this call`) before the test was ever run; fixed to `findByLabelText('Search Matter records', {}, WAIT)`.

## 7. Gate results (all foreground, this worktree, HEAD rebased onto `d21e54e98`)

| Gate | Result |
|---|---|
| `npx tsc --noEmit` | **111 total / 0 production** — matches the stated baseline exactly. 0 errors in any touched or new file. (First run showed 114/+1-production-looking; both were environment artifacts of a fresh worktree — see §8 — not code defects.) |
| `npm run build` (placeholder env vars) | Exit 0. |
| `npx jest --ci` (full suite) | **10 failed / 39 passed suites (49 total), 84 failed / 600 passed tests (684 total)** — the failing 10 are exactly the known baseline list (OutlookAdapter, ApiClient, EntityPicker, SaveFlow.test.tsx, TaskPaneNavigation, TaskPaneShell, SaveView, ShareView, useEntitySearch, useSaveFlow — verified by name, not just count). All 3 new suites pass. The two pre-existing sibling suites this task's design leans on (`SaveFlow.matterTypeQuickCreate.test.tsx`, `RelatedToPicker.matterType.test.tsx`) still pass unmodified. |
| Gated list (`ci-gated-suites.txt`, run via `--runTestsByPath` exactly as CI does) | **34/34 suites, 432/432 tests — unchanged, all green.** See §9 for a numbers discrepancy against the task brief worth flagging. |
| `eslint` on the 6 touched/new files | **0 errors, 6 warnings** — all 6 are the pre-existing `@typescript-eslint/no-empty-function` warning on the `ResizeObserverStub` class, copied verbatim from the established `SaveFlow.matterTypeQuickCreate.test.tsx`/`RelatedToPicker.matterType.test.tsx` precedent (confirmed: running eslint on those two pre-existing files produces the identical 6 warnings). Not a regression. |
| `code-review` (Step 9.5, synchronous) | 0 Critical / 0 Warning / 1 Suggestion (a generic-fallback message wording note, not actionable). Quality direction: Improved. |
| `adr-check` (Step 9.5, synchronous) | 0 Violations / 0 Warnings across ADR-021, ADR-012, ADR-028, ADR-019, ADR-044, ADR-038, ADR-010, NFR-03, NFR-10. |
| BFF publish size | Not applicable — no `.cs` file touched. |

## 8. A build-order artifact worth recording (not a code defect)

This worktree was freshly created with no prior build state. `npx tsc --noEmit` initially reported 114 errors including one that looked production-shaped: `shared/services/AuthService.ts(2,71): error TS2307: Cannot find module '@spaarke/auth'`. Root cause: `node_modules/@spaarke/auth` is a workspace symlink to `src/client/shared/Spaarke.Auth/`, whose `package.json` declares `"main": "dist/index.js"` / `"types": "dist/index.d.ts"` — but that package's own `dist/` had never been built in this worktree (a sibling checkout used for earlier investigation already had a `dist/` from 2026-09-08). Running `npm install` + `npm run build` inside `src/client/shared/Spaarke.Auth/` resolved it; the count then matched the stated baseline exactly (111/0). Recorded here in case a future fresh-worktree task hits the same thing and wonders whether it's a regression — it is not.

## 9. `ci-gated-suites.txt` discrepancy — flagged, not fixed (forbidden file)

The task brief stated the gated list should be "36 suites / 443 tests." The **committed** file at this branch's rebased tip (`d21e54e98`) has **34 non-comment suite lines / 432 tests** — confirmed by both a direct `grep -c` and the actual `--runTestsByPath` run (§7). The gap is exactly task 025's own two new suites (`SaveFlowCollision.test.tsx` + `errorMessages.collision.test.ts`, 11 tests — 432 + 11 = 443, reconciles precisely): task 025 shipped and merged those suites but its PR did not add them to the gate allow-list, even though both pass today (confirmed in the full run, §7). I did not add them — `ci-gated-suites.txt` is a forbidden-edit file for this agent. **Recommend the main session promote both, plus this task's three new suites, in one ratchet update**, listed in the final report.
