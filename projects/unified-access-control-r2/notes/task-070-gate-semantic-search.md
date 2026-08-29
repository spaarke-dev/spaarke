# Task 070 — Authorize `POST /api/ai/search`

> **Date**: 2026-08-25 · **Rigor**: FULL · **Model**: opus @ xhigh
> **Status**: implementation + tests complete; publish-size / CVE / Step 9.5 gates pending

---

## What was wrong

`SemanticSearchAuthorizationFilter.ValidateScopeAuthorization` returned `new AuthorizationResult(true, null)` for **every** branch — `entity`, `documentIds`, `all`, and `default`. The only check performed was the `tid` claim. The scope was caller-chosen and never caller-authorized, and the class remarks listed document-level authorization as a "future enhancement".

Because reads on this route are app-only, Dataverse row-level security is inert and this filter **was the entire security boundary**. Proven end-to-end on 2026-08-25: a non-admin denied Read on all 442 documents by Dataverse listed a matter's documents, opened them in the Viewer PCF, and downloaded the files — through this route, on an MDA form.

---

## What it does now

| Scope | Behaviour |
|---|---|
| `entity` | Authorizes the caller's **Read on the parent record** (Dataverse's own answer, as the caller). **1 round trip.** |
| `documentIds` | Authorizes each named document through the existing document path; narrows the query to the permitted subset. Refuses if none are readable. Bounded parallelism 8, cap 100 (the DTO's existing cap — no previously-valid request is newly rejected). |
| `all` | **Refused (403).** |
| empty / unknown | **Refused (400)** — malformed, never reaches the search. |

Plus, on the response: results are re-checked against the authorized scope, counts are recomputed, and `driveId`/`speFileId` are stripped.

---

## Six decisions, with reasoning

### 1. `scope=all` is refused, not reduced to the caller's accessible set

Refusing is the only option that cannot be *subtly* wrong: a reduction is one filter bug away from being the disclosure again, and that bug would be invisible because the response would still look plausible.

> 🔴 **CORRECTION — the original justification here was factually wrong, and this breaks live callers.**
> This decision was first recorded as *"there is no caller that needs it — the flagship consumers are
> parent-scoped."* **That is false.** Found at Step 9.5:
>
> - `src/client/code-pages/SemanticSearch/src/services/targetEntityNormalize.ts:109` emits
>   `{ scope: 'all' }` for the **"All" dropdown row**, and `:114` emits it again as the **defensive
>   default** when a row's target-entity label is empty.
> - `src/client/pcf/SemanticSearchControl/.../SemanticSearchApiService.ts:534-536` has an `'all'` branch.
> - `Models/Ai/SemanticSearch/SemanticSearchRequest.cs:29-32` still documents `scope` as *"Defaults to
>   `all` when not provided (e.g. Copilot API Plugin calls)."*
>
> The refusal is still the right call — but it is a **breaking change to shipped surface**, not a
> no-op, and it needs an owner decision on the "All" option rather than shipping as a silent 403.
> **See the Owner Decision section at the end of this file.** The reasoning survived scrutiny; the
> premise did not.

### 2. The canonical authorization seam could not be used as-is

`DataverseAccessDataSource.TryRetrievePrincipalAccessAsync` hard-coded its `RetrievePrincipalAccess` target as `sprk_documents({resourceId})`. So `AuthorizationService` could only authorize `sprk_document` and **could not answer "may this caller read this matter?"** — the exact question `scope=entity` needs.

### 3. Fixed additively, not by threading an entity type through the existing method

Threading it through `IAccessDataSource.GetUserAccessAsync` would have touched ~10 `AuthorizationContext` construction sites, both implementations, and `CachedAccessDataSource`'s `(userId, resourceId)` cache key — under which a document's cached snapshot could answer for a record of another type. That is a shared-authorization-surface refactor, not a side effect of gating one route.

Instead: a sibling `GetRecordAccessAsync`, existing call sites untouched, **same authority** (`RetrievePrincipalAccess`, as the caller, over OBO). Its cache entry uses a distinct namespace *and* includes the entity set, so cross-type confusion is structurally impossible rather than merely unlikely.

**Honest cost**: "additive" was not free. Six test doubles implement `IAccessDataSource` and all six had to be updated. A default interface method would have avoided that, but this file's own design philosophy — documented at length on `UserAccessToken` — is that authorization decisions must be *stated*, not inherited by omission. A silently-defaulted authorization method contradicts that.

### 4. `AccessibleRecordSetService` was deliberately NOT used for the workforce plane

The POML named it as the extension point. But `ComposeForSystemUserAsync` resolves **ADR-034 membership** (`sprk_assigned*` participation), not Dataverse's real answer. Gating the MDA Matter form on that would deny the document list to any user who can read the matter but is not an assigned participant — on the flagship form. It would be reverted, and reverting reopens the hole. Substituting Dataverse's real answer for workforce is task **031**'s ADR-028 A2 amendment, which has not landed. Contacts still route through the accessible-record-set path.

### 5. Parent types come from an allow-list, not string pluralization

An unrecognised `entityType` must DENY — and a mapping that *computes* an entity-set name can always compute one, so it can never deny. The value is also interpolated into the Dataverse request path, so the set of reachable tables should be fixed in source rather than a function of request input.

### 6. Result-level authorization is a parent-id equality check

The AI Search index is a separate data plane with no ACL data and no freshness guarantee. If a document is reparented in Dataverse and the index still holds the old parent, a parent-scoped query returns a row outside the authorized scope. Checking each row's parent costs **zero** extra round trips — the value is already on the row. Rows with no parent fail closed.

---

### 7. A malformed `documentIds` entry is a 400, not a 403

Added after Step 9.5. A GUID-shape guard was introduced to stop 100 arbitrary strings each buying a
Dataverse round trip — but the first version *silently dropped* unparseable ids, which meant a request
of entirely non-GUID ids left the authorized set empty and fell through to
*"You do not have access to any of the requested documents."*

That answer is confidently wrong. The caller's problem was their payload, not their permissions, and a
denial that misattributes its own cause sends the reader to the wrong place entirely. It now returns
400 with `INVALID_DOCUMENT_IDS`, matching how the entity path already treats a non-GUID `entityId`.

Two pre-existing tests used placeholder ids (`"doc-1"`, `"doc-2"`) that were harmless while
`documentIds` were never resolved to anything. They now carry real GUIDs.

---

## A defect the allow-by-default branch was hiding

The filter lower-cased the incoming scope and switched over the `SearchScope` constants — but `SearchScope.DocumentIds` is the camel-cased literal `"documentIds"`, so **a lower-cased value could never match that case label**. Every `scope=documentIds` request fell through to `default:` — which returned allow.

The bug was invisible *because* the fall-through was permissive. Closing `default:` turned it into a denial, which is how it surfaced. Comparison is now case-insensitive, pinned by a `[Theory]` over four casings.

This is worth remembering as a general shape: **a permissive default does not just fail to block attackers, it suppresses the signal from your own broken matching logic.**

---

## Tests

Final, after the Step 9.5 fixes: **unit 11,091 pass / 0 fail** · **integration 385 pass, 65 skipped, 0 fail** · **ArchTests 79/79**.

> An earlier revision of this file claimed "450 pass, 0 fail". 450 was the integration project's *total*
> including 65 skips — the pass count was 385. Corrected rather than quietly restated, because
> conflating "total" with "passed" is exactly the kind of number that gets copied into a PR description.

The pre-existing tests asserted the vulnerability. `Search_ScopeAll_IsAllowed` expected 200; `Search_EntityScope_IsAllowed` and `Search_DocumentIdsScope_IsAllowed` expected 200 for any authenticated caller. They were accurate descriptions of the code, which is why they passed while the route disclosed the tenant. `Search_EntityScope_AuthorizationGranted` passed `EntityId = "test-entity-id"` — a non-GUID that could not identify any record — and asserted 200, which was itself evidence that nothing was being resolved.

Two access doubles, deliberately opposite:

- `StubAccessDataSource` (authorization tests) — **denies by default**, so a test that forgets to grant sees a denial rather than an accidental allow.
- `AlwaysPermitAccessDataSource` (contract tests) — grants everything, and is named to say so out loud, because it is exactly the shape of the defect this task fixed and must never be mistaken for an authorization test.

Independent corroboration: task 074's route-authorization ArchTest — written without reference to this fix — **fails** against `SemanticSearchAuthorizationFilter.cs` at HEAD and **passes** against this working tree.

---

## Two findings this task did NOT fix, now filed

| Task | Finding |
|---|---|
| **077** | `POST /api/ai/search/records` — the twin defect, on the same route group. Its filter checks `tid`, logs, and calls `next()`. Leaks record *names* tenant-wide, which for a secure matter is often the sensitive part. Not fixed here: authorizing an arbitrary set of record types is a different and larger question, and hurrying it inside 070 risks the third access policy this project exists to remove. |
| **078** | `GET /api/v1/containers/{containerId}/documents` — lists any container's documents behind `RequireAuthorization()` alone. The read-side twin of task 073. Needs task 075's container→record mapping. |

Both were found by task 074's ArchTest **on its first run**, on a surface this project had already enumerated by hand four times.

---

## Files changed

**Server**
- `src/server/shared/Spaarke.Dataverse/IAccessDataSource.cs` — added `GetRecordAccessAsync`
- `src/server/shared/Spaarke.Dataverse/DataverseAccessDataSource.cs` — implemented it; parameterised the RPA target entity set; added an entity-agnostic probe and a token-explicit systemuser lookup
- `src/server/shared/Spaarke.Core/Auth/AuthorizationService.cs` — added `GetCallerRecordAccessAsync`
- `src/server/api/Sprk.Bff.Api/Infrastructure/Caching/CachedAccessDataSource.cs` — implemented it with a distinct, entity-set-qualified cache key
- `src/server/api/Sprk.Bff.Api/Api/Filters/SemanticSearchAuthorizationFilter.cs` — rewritten
- `src/server/api/Sprk.Bff.Api/Api/Ai/SemanticSearchEndpoints.cs` — result-level enforcement, pointer stripping, recomputed counts, fail-closed if the filter did not run

**Tests** — `SemanticSearchAuthorizationTests.cs`, `SemanticSearchIntegrationTests.cs`, `AuthorizationIntegrationTests.cs`, plus six `IAccessDataSource` doubles.

---

## ADR Tensions (CLAUDE.md §6.5)

Surfaced at Step 9.5. Both are **path B**, and both are *pre-declared* in this project's `CLAUDE.md` as belonging to **task 030**'s ADR-003 amendment. What matters is that task 070 **extends** the non-compliant surface while 030 has not landed — that must be stated in the PR, not inherited silently.

| # | Rule | Tension | Path |
|---|---|---|---|
| **V-1** | ADR-003: *"MUST cache UAC snapshots per-request only"* | `CachedAccessDataSource.GetRecordAccessAsync` adds a **60s cross-request Redis cache** for record access. Identical in kind, TTL and fail-closed semantics to the pre-existing `GetUserAccessAsync` cache it sits beside. Path C (no cache) means a Dataverse round trip on every search of the flagship form — not defensible. | **B** — fold into task 030 |
| **V-2** | ADR-003: *"MUST implement new auth logic as `IAuthorizationRule`"* | The scope decision lives in an endpoint filter. It is request-body-shaped (`scope` / `entityType` / `documentIds`), which `IAuthorizationRule`'s `AuthContext` cannot carry. Note this rule also pulls against **ADR-008**, which mandates the filter — the two ADRs conflict independently of this change. | **B** — fold into task 030 |

**Known follow-ups from the same review, not fixed here:**

- **ADR-038 (V-4)** — the new authorization tests sit at `tests/integration/Spe.Integration.Tests/SemanticSearch/`, which is **not** one of the eight KEEP paths. `tests/CLAUDE.md` requires new auth tests at `tests/integration/auth/**`. They are therefore not deletion-protected and are exposed at `/test-diet`. **Path C — move them.**
- **W-7** — `caller_not_a_dataverse_user` and exception-path denials are cached for the full 60s TTL, so a brief Dataverse blip denies a caller for a minute. Skip the cache write when the snapshot came from an error path.
- **W-6** — two private `LookupDataverseUserIdAsync` overloads whose first `string` parameter is a **token** in one and an **oid** in the other; the 2-arg one interpolates that value into an OData filter *and logs it*. Rename before an edit swaps them and puts a credential in the log stream.
- **W-9** — the 500 handler returns `ex.Message` to the client (pre-existing, ADR-019 MUST NOT).
- **S-6** — the "filter did not run → 500" backstop at `SemanticSearchEndpoints.cs` is untested.

---

## Placement Justification (CLAUDE.md §10 / §11)

No new BFF component. One method added to an existing shared authorization interface and its two implementations; one existing endpoint filter rewritten; one existing endpoint hardened. No new package, no new DI registration, no new endpoint.

**Existing (corrected at Step 9.5).** The first draft of this section asserted that the alternative *"would have been the second mechanism answering one question."* That was written without grepping, and §11 requires grep evidence. **A component answering this exact question already exists**: `Infrastructure/ExternalAccess/CallerRecordAccessProbe.cs` (this project, task 008) — entity-set-parameterised, OBO-only, `RetrievePrincipalAccess`, fail-closed.

**Why it was not extended instead.** It lives in the BFF (`Sprk.Bff.Api.Infrastructure`), and the consumer that needed the answer is `AuthorizationService` in `Spaarke.Core`, which cannot reference the BFF — the dependency runs the other way, and `LayerDependencyTests` enforces that. So the seam had to exist at or below `Spaarke.Core`. That is a legitimate reason, but it should have been *stated* rather than the existing component going unmentioned.

**Two consequences to carry forward, not to bury:**

1. **Task 032's scope shrinks.** `CallerRecordAccessProbe.cs:24-25` says generalizing this seam *"changes `IAccessDataSource` for every consumer and is **task 032's scope** (Phase 1 evaluator), not this task's."* Task 070 did exactly that. Either 032 is reduced accordingly or this is reverted — it must not sit as an undeclared land-grab.
2. **The new seam is weaker than the one it duplicates on identity resolution.** `CallerRecordAccessProbe` derives the principal via `WhoAmI()` under the OBO token; its own remarks explain why — *"an app-only implementation would carry the caller's identity as DATA… a wrong or defaulted id would then silently answer about the wrong person, which is the exact shape that let A-2 survive."* `GetRecordAccessAsync(userId, entitySetName, recordId, token)` takes `userId` and `token` as **independent parameters**. Today's only caller derives both from one `ClaimsPrincipal`, so it is not exploitable — but the cache is keyed on `userId`, so a future mismatched pair would write a snapshot under another user's oid. **Follow-up: derive the subject from the token rather than trusting the parameter.**

---

## 🔔 Owner decision required — `scope=all` breaks shipped callers

Refusing `scope=all` is correct, and I would not soften it. But it is a **breaking change**, and the "All" option in the SemanticSearch code page dropdown now returns 403 (see the correction under decision #1).

| Option | Consequence |
|---|---|
| **A — remove the "All" affordance** (recommended) | The UI stops offering a search the product will not authorize. Requires a client change; the defensive default at `targetEntityNormalize.ts:114` must also pick a real scope or surface a clear error. |
| **B — reduce `scope=all` to the caller's accessible set** | Preserves the feature, but reintroduces the failure mode decision #1 rejects: a reduction bug looks like a plausible response, so the disclosure would return silently. |
| **C — ship the 403 as-is** | Users hit an unexplained error on an option the UI still offers. Not acceptable as an end state. |

Not decided here. Recorded so it is chosen rather than discovered in UAT.
