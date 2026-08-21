# Task 001 — completion record

> **Completed**: 2026-08-21 · **Rigor**: FULL · **Spec**: FR-A01
> Inventory: [`error-surface-inventory.md`](error-surface-inventory.md)

---

## What changed

**No new error type, no new service, no new package.** The mechanism already existed and the SpeAdmin
surface was routing around it.

| File | Change |
|---|---|
| `Infrastructure/Graph/SpaarkeStorageException.cs` | + `GraphRequestId` |
| `Infrastructure/Graph/GraphErrorTranslator.cs` | + `ExtractRequestId()` · + `ClientStatusFor()` · + rich `ToProblemDetails(summary, errorCode, statusCode, traceId, title)` **overload** |
| `Infrastructure/Errors/ProblemDetailsHelper.cs` | + `Redact()` · + `Explain()`; `FromGraphException` now redacts |
| `Api/SpeAdmin/**` (18 files) | **60 sites** routed — 23 via `ToProblemDetails`, 37 via `Explain` |
| `src/solutions/SpeAdminApp/**` (13 files) | + `describeApiError()`; 34 render sites wired; 4 dead `err.detail` reads removed |
| `tests/unit/domain/Errors/ErrorSurfaceTests.cs` | **new** — 28 tests |

---

## Scope was larger than the POML's 41 sites — 60

The POML's `<relevant-files>` named 5 endpoint files; `Api/SpeAdmin/` holds **18**. Step mode was
`directional` and step 1 says "every … in `Api/SpeAdmin/**`", so the inventory covered all of them. Three
categories the first catch-block scan under-counted, each found by a later check rather than the first pass:

| Category | Count | How it surfaced |
|---|---|---|
| Hardcoded `detail:` inside a catch | 41 | The original brace-matched scan |
| Error-shaping **helper methods** called *from* catches (`GraphError`, `UnexpectedProblem`, `GraphApiProblem`) | 6 helpers → covering ~18 call sites | A post-fix grep found `ConsumingTenantEndpoints.cs:518` still saying "Check the app registration credentials" — it lives in a helper, not a catch, so the catch-scan had logged its callers as "no-response" |
| `detail: ex.Message ?? "…"` — honest text, but **unredacted** and dropping code/request-id | 13 | The Step 9.5 ADR-019 gate |

The inventory's §4 called that last group "no change needed". That was right about the *wording* and wrong
about *secrets*: they bypassed `Redact()`, so a token-shaped substring in a Graph message would have reached
an admin's browser. Fixed.

---

## Deliberate decisions

**1 — `ToProblemDetails` added as an overload, not a signature change.** The parameterless form has **29
callers** in the document/OBO/upload endpoints, outside this task's SpeAdmin scope. Changing its signature
broke all 29 (caught at compile). The original is kept verbatim; the rich form sits alongside.

**2 — `Explain()` is a string helper, not an `IResult` factory.** The 37 generic-catch sites use three
different argument orderings for `Results.Problem` plus a mix of inline and block `extensions` dictionaries.
Rebuilding those calls would churn every line and risk silently changing a status code or dropping an
extension key. Wrapping only the `detail:` argument is order-independent and reviewable at a glance.

**3 — Status codes preserved, with exactly one exception.** This task changes error *content*, not status
semantics. The exception: an upstream Graph **401 → 502**. A Graph 401 means *our* credential to Graph is
bad, not that the caller's token is stale — but the client's `authenticatedFetch` reads any 401 as its own
token expiring, clears its cache, burns three silent retries, and throws a generic `AuthError`. Propagating
it verbatim would bury the very error this task exists to surface. Centralised as
`GraphErrorTranslator.ClientStatusFor()`; the upstream status is still reported as `graphStatusCode`.

**4 — `SecurityEndpoints` keeps its actionable hint.** Both sites are filtered `when (ex.StatusCode == 403)`,
so "access denied" *is* established. Which grant is missing is not — a 403 can also be conditional access or
a tenant policy. The hint stays a hint and the Graph code/message is appended. Task 013 grants
`SecurityEvents.Read.All`; task 012 owns the operator-role message.

**5 — Two `OperationCanceledException` sites left untouched.** "Request was cancelled." is established by the
exception type. Accurate as written.

---

## Corrections made during the task

- **"Zero callers repo-wide"** — an early inventory claim, from a grep scoped to `Api/SpeAdmin/`. There are
  29 outside it. Corrected in the inventory; changed the fix from a signature change to an overload.
- **6 regex false positives** — a forward-scanning regex attributed validation guards in the *next* method to
  catch blocks. A depth comparison did not fix it (after a catch closes, following code is still deeper than
  the `catch` line); real brace-matched block spans did. Recorded in inventory §5.
- **Publish size measured wrong first** — 138 MB uncompressed against a 44.96 MB **compressed** baseline. The
  documented method is a compressed framework-dependent linux-x64 publish.
- **Redaction gap** — `"access_token":"…"` (JSON form) was not redacted; only `client_secret=…` (query form)
  was. Caught by the new tests, not by review.
- **`package-lock.json`** — modified as a side effect of `npm install`; reverted, not part of this task.

---

## Verification

| Gate | Result |
|---|---|
| `dotnet build src/server/api/Sprk.Bff.Api/` | ✅ **0 errors**, 7 warnings (all pre-existing `DemoProvisioningOptions` obsolescence) |
| Unit tests | ✅ **10,564 passed**, 0 failed, 97 skipped (+28 new) |
| ArchTests (ADR enforcement) | ✅ **36/36** — ADR-007 Graph isolation holds |
| `dotnet list package --vulnerable --include-transitive` | ✅ no vulnerable packages |
| Publish size (compressed, framework-dependent linux-x64) | ✅ **43.68 MB** incl. PDBs / 42.80 MB excl. — under the ~44.96 MB baseline, far under the 60 MB ceiling |
| New NuGet | ✅ none — zero `PackageReference` changes (NFR-02) |
| Client type-check | ✅ **38 errors vs 42 before** — my changes *removed* 4 (the dead `err.detail` reads were themselves TS errors) |
| Misleading cause-assertions remaining in `Api/SpeAdmin/**` | ✅ **0** |
| Unredacted `ex.Message` in a SpeAdmin payload | ✅ **0** |

### ADR compliance

ADR-019 ✅ (the point of the task) · ADR-007 ✅ (ArchTests; the only `Microsoft.Graph` strings in
`Api/SpeAdmin/` are comments) · ADR-028 ✅ (no new `WithClientSecret`, no raw Bearer fetch) ·
ADR-021 ✅ (no hard-coded colors added) · ADR-038 ✅ (new tests are pure-logic, `tests/unit/domain/**` KEEP
path, no `Mock<HttpMessageHandler>`, no DI-registration or ctor-null tests). **No ADR tensions surfaced** —
CLAUDE.md §6.5 not invoked.

### CLAUDE.md §10 Placement Justification

All changes are **modifications to existing BFF files** plus three additions to existing infrastructure
types (`Redact`, `Explain`, `ExtractRequestId`/`ClientStatusFor`/`ToProblemDetails` overload). No new
service, no new DI registration, no new endpoint, no new package. §11's three-question gate does not apply
(modify-only), and the one judgment call it *would* have covered — whether to add a mapping helper — was
resolved by extending `GraphErrorTranslator`/`ProblemDetailsHelper` as POML step 3 directs.

---

## ⚠️ Not verified — POML step 7 and the four `<ui-tests>`

**The `SpeAdminApp` code page does not build in this worktree, for a reason unrelated to this task.**

```
[vite]: Rollup failed to resolve import "@microsoft/applicationinsights-web"
        from src/client/shared/Spaarke.UI.Components/src/services/AppInsightsService.ts
```

`@microsoft/applicationinsights-web` is not a declared dependency of `SpeAdminApp` and is not installed.
**Confirmed pre-existing** — the identical failure reproduces with all of this task's client changes stashed.

Consequences, stated plainly:

- POML **step 7** ("Verify against the Spaarke Dev tenant: the Container Types screen now reports the actual
  Graph error") — **NOT DONE**.
- All four `<ui-tests>` — **NOT RUN**. (No `--chrome` session either.)
- Acceptance criterion 1 is verified at the **API layer** (unit tests assert the payload no longer says
  "Check the app registration credentials") but **not** end-to-end in a browser.

What *was* substituted: a before/after `tsc` comparison proving the client changes introduce no type errors
and remove four, plus payload-level tests of the exact strings the UI renders.

**This does not block task 002** (server-side audit of the 70 `catch (ODataError)` sites). It does block
honest sign-off on the visual half of FR-A01. Recommended: fold the build fix into task 003 or 030 — both
already own `SpeAdminApp` client work — or raise it as a hygiene item alongside task 060.

---

## Bounded by task 002

**28 of the 70 `catch (ODataError)` sites in `SpeAdminGraphService.cs` swallow the error** (13 → `null`,
11 → empty/default, 4 rethrow-other). On those paths there is no error for task 001 to surface — the caller
cannot distinguish *absent* from *failed*. Those screens stay silent until **task 002** lands. This is the
correct division per this task's POML note, but it means "the app now tells the truth" is only true for the
42 translating paths.
