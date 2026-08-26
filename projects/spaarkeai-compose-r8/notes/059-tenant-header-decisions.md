# Task 059 — tenant resolution: enumeration, per-caller decisions, and a correction to the filed severity

> **Status**: implementation complete; **human sign-off still required before merge** (CLAUDE.md §6 — security-sensitive).
> **Date**: 2026-08-26 · **Project**: `spaarkeai-compose-r8` · **Gates**: arming `SessionFileStore:BlobEndpoint`.

---

## 0. The headline, before the detail

Two things came out of the mandated enumeration that change what this task is:

1. **The defect is 21 resolution sites across THREE mechanisms**, not "four handlers plus the auth
   path". (21 mechanism-instances across **20 distinct handlers** — `IndexTemporaryContent` carried
   both a header tier and a query parameter. A 22nd header read existed but was a diagnostic log,
   not a resolution.) The header was the mechanism that had been noticed; it was neither the only
   one nor the worst one.
2. **The filed defect — the `X-Tenant-Id` header — was the LESS severe class, and is in fact
   LATENT.** The mechanism nobody had filed — a `?tenantId=` query string on **four** routes, two of
   them **Compose's own** — was **live and exploitable by any authenticated user** for both read and
   write, with no claim consulted anywhere in the handler. §3 has the proof.
3. The worst single instance is `GET /api/compose/documents/{documentSpeId}` — the Compose document
   **open** path. It took the tenant from the URL, so a caller could load and resume another
   tenant's Compose session, with its anchored annotations, defined terms and action history.

Both are now closed.

---

## 1. Enumeration (step 1 — completed before any modification)

### Mechanism A — `X-Tenant-Id` header, last tier of a `??` chain (16 sites)

Shape: `tid claim ?? schema-URI claim ?? Request.Headers["X-Tenant-Id"]`.

| # | File | Line (pre-fix) |
|---|---|---|
| 1–2 | `Api/ComposeEndpoints.cs` | 955, 2206 |
| 3 | `Api/Agent/AgentEndpoints.cs` | 527 |
| 4 | `Api/Ai/AnalysisEndpoints.cs` | 1462 |
| 5–9 | `Api/Ai/ChatDocumentEndpoints.cs` | 327, 851, 941, 1195, 1612 |
| 10 | `Api/Ai/ChatWordExportEndpoints.cs` | 272 |
| 11 | `Api/Ai/KnowledgeBaseEndpoints.cs` | 545 |
| 12 | `Api/Ai/ChatEndpoints.cs` | 3056 |
| 13 | `Api/Ai/AnalysisChatContextEndpoints.cs` | 148 |
| 14 | `Api/Ai/ReviewMemoEndpoints.cs` | 336 |
| 15 | `Api/Ai/StandaloneChatContextEndpoints.cs` | 189 |
| 16 | `Api/Ai/VisualizationEndpoints.cs` | 205 |

Plus a **diagnostic** read at `Api/Ai/ChatEndpoints.cs:1859` (logged the header when tenant was
missing) — not a resolution site, but removed: after the fix it would point a reader at a value that
had no bearing on the outcome.

### Mechanism B — a second header name, with no claim at all (1 site)

`Api/Insights/PrecedentAdminEndpoints.cs:246` read `X-Spaarke-Tenant-Id` and consulted **no claim**.
An admin authenticated in tenant A could write a Precedent projection tagged tenant B, and nothing
downstream cross-checks it. Its own comment described it as a placeholder pending "D-P15 centralized
tenant resolution" — that note is now discharged, because `TenantResolution` **is** that central point.

### Mechanism C — a `?tenantId=` query string (4 sites) — **the live one**

| Site | Route | Behaviour before 059 |
|---|---|---|
| `VisualizationEndpoints.GetRelatedDocuments` | `GET /api/ai/visualization/related/{documentId}` | Tenant came **exclusively** from `?tenantId=`. No claim consulted anywhere in the handler. |
| `VisualizationEndpoints.IndexTemporaryContent` | `POST /api/ai/visualization/related-from-content` | `?tenantId=` **outranked** the `tid` claim (`tenantId ?? claim ?? header`). |
| `ComposeEndpoints.Load` | `GET /api/compose/documents/{documentSpeId}` | Tenant **exclusively** from `?tenantId=`, rejected as a 400 if absent under the message *"required for multi-tenant isolation"* — isolation the caller chose. This is the document **open/resume** path. |
| `ComposeEndpoints.GetAnnotations` | `GET /api/compose/sessions/{sessionId}/annotations` | Same shape; reads another tenant's anchored annotations and defined terms. |

Found only because the tripwire was written to match by shape: a `[FromQuery] … tenantId` scan over
the whole BFF tree turned up the two Compose sites after the visualization pair was already fixed.
A name-based grep for `X-Tenant-Id` would have found none of the four.

Note the irony: `StandaloneChatContextEndpoints.cs:78` documented the rule all four were breaking —
*"never from query string (ADR-008, ADR-014)"*.

---

## 2. Who actually sends these (the "enumerate before modifying" answer)

| Caller | Evidence | Decision | Why |
|---|---|---|---|
| **6 client SSE/chat senders**, all via one function (`useSseStream.ts` `readSseStream`, header set at `:343`) | The value is `extractTenantId(token)` (`:222-231`), which **base64-decodes the very JWT it puts in the same request's `Authorization` header** and reads `tid` | **Remove the server tier.** Client send left inert. | The server reads the identical claim at tier 1, from a source the caller cannot forge. Touching the shared hook would force a rebuild of 7 PCF bundles for zero security gain; the header is now simply ignored. Follow-up cleanup filed in §7. |
| **`authenticatedFetch`** (`Spaarke.Auth/src/authenticatedFetch.ts:27-77`) | Sets **only** `Authorization` | No action | ~6 comments across `SprkChat.tsx` / `useChatSession.ts` claim it "owns X-Tenant-Id". **They are stale** — see §7. |
| **`RagApiKey` scheme** (`ApiKeyAuthenticationHandler.cs:89-96`) — the ONE principal in the system with no `tid` claim | Mints `ClaimTypes.Name` + `auth_scheme` only. Its single route `POST /api/ai/rag/enqueue-indexing` takes tenant from the **request body** (`RagEndpoints.cs:805-810`) | **Unaffected — no change.** | It never consumed this tier. Verified, not assumed: this is precisely the caller the POML warned about breaking. Separately flagged in §6. |
| **Dataverse plugins** (`BaseProxyPlugin.cs`) | Target `/documents/{id}/preview-url`; send `X-API-Key` or a client-credentials Bearer; never set a tenant header | Unaffected | — |
| **`DocumentRelationshipViewer` PCF** (`VisualizationApiService.ts:73`) | Sets `?tenantId=` as a **required** query param | **Server stops trusting it; PCF unchanged.** | Model binding ignores unknown query keys, so no redeploy is needed. The bound property was **deleted** rather than left readable, so reading it again is a compile error. |
| **Compose client** (`tenantId` host prop, passed on load + annotations) | Supplied by the host (SpaarkeAi / PCF) from the Dataverse org context — i.e. the user's own tenant | **Server stops trusting it; client unchanged.** | The claim carries the same value for every legitimate caller, since a Dataverse-hosted user's org *is* their tenant. Handler parameters removed, so the sent value is ignored rather than re-read. |
| **`scripts/Test-SessionRestoreLatency.ps1`** | Sent `X-Tenant-Id: "test-tenant-loadtest"` — a tenant that does not exist | **Updated** — header removed, variable retained as a run label | The only sender whose value was NOT derived from a token. |
| **3 test fixtures** | `FakeAuthHandler`, `ComposeActiveDocFakeAuthHandler` — neither emits `tid` | **Fixture repaired** (claim added), not compensated | See §4. |
| **`X-Spaarke-Tenant-Id`** | **No sender anywhere** — 5 repo hits total, all the endpoint itself or a manual operator runbook | Removed | — |

**Nothing in production broke, and that was established before the change, not after it.**

---

## 3. The correction: the filed defect is latent; the unfiled one was live

The header sat at the **end** of a `??` chain. It was therefore only ever reached by a principal
carrying **no** `tid` claim in either form. A caller holding a valid Entra token in tenant A could
not use it to become tenant B — tier 1 short-circuited first. Every workforce and CIAM access token
carries `tid`.

So the header defect required a **claim-less authenticated principal**. One exists
(`ApiKeyAuthenticationHandler`), but it is pinned to a single route that never read this tier. The
hole was therefore **one route registration away from live**, not live.

I discovered this the honest way: my first cross-tenant test used a tenant-A principal **with** a
`tid` claim and spoofed the header. Its security assertion **passed against the unfixed code** —
a vacuous pass. The test that actually fails pre-fix is the one using a principal with no tenant
claim at all. Both are kept, and the file says plainly which one proves the fix and which one is
only a precedence guard.

Meanwhile the query-string mechanism had **no guard of any kind**. Three of its four sites
(`GetRelatedDocuments`, `ComposeEndpoints.Load`, `ComposeEndpoints.GetAnnotations`) took the caller's
word for the tenant, full stop — no claim was consulted anywhere in the handler. That is a live
cross-tenant read, by URL edit, for any authenticated user; on `Load` it is the Compose document
**open** path, which also resumes the target session's annotations, defined terms and action history.
None of the four was in the filed scope.

Two of those three carried a 400 whose message read *"tenantId query parameter is required for
multi-tenant isolation"* — an isolation guarantee stated in the error text of the very check that
let the caller choose the tenant. That phrasing is presumably why nobody looked.

> **This inverts the severity that `current-task.md`'s ARMING WARNING recorded**, which described the
> header as a live spoofing route on the DELETE path. It is not, for a caller with a normal token.
> The arming risk it flagged is real but arrives through the claim-less-principal path, and the
> *unfiled* visualization routes were the genuinely exploitable ones. `current-task.md` is corrected.

---

## 4. The fix

**One chokepoint**: `Infrastructure/Authentication/TenantResolution.cs`.

```csharp
public static string? ResolveTenantId(ClaimsPrincipal? user)
```

It takes a **`ClaimsPrincipal`, not an `HttpContext`** — so it *cannot* read a header, a query string
or a body. That is the enforcement mechanism, deliberately chosen over a rule someone has to
remember at each of 19 sites (and at the 20th). It is the same type-system idiom this project already
used twice: `ComposeEditAnchorPass.Validate` takes no document text, and after task 064 no type in
`Services/Compose/` can express a character offset.

**CLAUDE.md §11 justification** (this is a *promotion*, not a new component):

1. **Existing** — 16 inline copies plus three private near-duplicates already in the tree
   (`ChatDocumentEndpoints.GetTenantIdClaim`, `OfficeRateLimitFilter.ExtractTenantId`,
   `MembershipEndpoints.ExtractTenantId`) and one claims-only precedent
   (`SummarizeSessionEndpoint.cs:215-216`, which task 060's own comment named as "the shape to
   converge on").
2. **Extension** — yes, and that is what this is: the established private shape promoted to one
   public place. No new concept is introduced.
3. **Cost of doing nothing** — concrete, not abstract: with 19 independent copies, a fix applied to
   18 leaves the boundary open, and nothing stops a 20th. Since task 060 the resolved value is the
   leading path segment of `{tenantId}/session-files/{sessionId}/{fileId}` in a **90-day durable
   blob store** with soft-delete and versioning **off**.

No DI registration (static class) — ADR-010 budget unchanged.

**Fixture repair, not compensation.** Three test fixtures minted principals without `tid`, which is
not a shape Entra ever issues, and the tests compensated by sending the header. That made the
spoofable fallback the *only* tenant path those tests exercised — the fixture gap was holding the
hole open. Per `bff-extensions.md` §F.2 (Fixture-Config-FIRST) the fixture was the defect: `tid` was
added to `FakeAuthHandler` and `ComposeActiveDocFakeAuthHandler`, and the header sends were removed.

---

## 5. Tests — every one observed to FAIL first

`tests/integration/tenant/Ai/TenantSelectionByRequestTests.cs` (KEEP category, ADR-038 §2 path #4).
Pre-fix run: **4 failed / 4 passed**.

| Test | Pre-fix | Failure observed |
|---|---|---|
| `Delete_WithoutAnyTenantClaim_CannotReachAnotherTenantsSessionViaTheHeader` | **FAIL** | Tenant B's session was deleted by a claim-less caller naming tenant B in the header |
| `Index_WithSpoofedTenantQueryString_IndexesIntoTheCallersOwnTenant` | **FAIL** | `IndexTemporaryContentAsync(..., "bbbbbbbb-…", …)` — content indexed into tenant B |
| `Related_WithSpoofedTenantQueryString_ReadsOnlyTheCallersOwnTenant` | **FAIL** (added after the correction in §3) | Graph read executed with `TenantId == TenantB` |
| `NoBffSourceFileResolvesTenantFromTheRequest` | **FAIL** | Listed all 18 request-read sites by file and line |
| `Delete_WithATenantClaim_IgnoresASpoofedHeader` | **PASS** | Honest note: the claim already won. Kept as a precedence guard; **not** evidence of the fix. |

Post-fix: **9 / 9 pass.**

The two endpoint tests are **reachability** tests per the directory README — they assert on *tenant
B's* data (a session that must still exist; a service call that must never have been made), not on a
resolved string, because a string assertion stays green through the exact refactor that reopens the
hole. Both handlers were made `internal` so the assertions run against the shipping code rather than
a copy of its branch — the precedent `ChatEndpoints.DeleteSessionAsync` already set.

The tripwire matches **by shape** in both arms — `Headers[…Tenant…]` and `[FromQuery … tenantId]` —
never by header or key name, so a rename cannot slip past. Its regex is itself verified in both
directions (it must match all four removed shapes and must NOT match `[FromQuery] string driveId`,
the resolver call, or the CORS allow-list entry), because a tripwire nobody has seen fire on real
input is a tripwire nobody knows works.

**The query arm earned its place immediately**: it is what surfaced the two Compose sites, *after*
the visualization pair had already been fixed and the header sweep was believed complete. A
name-based grep for `X-Tenant-Id` would have found none of the four query-string holes.

---

## 5b. Verification (CLAUDE.md §10)

| Check | Result |
|---|---|
| BFF unit + contract | **11,344 passed / 0 failed / 97 skipped** (was 11,335 / 0 / 97; +9 new) |
| ArchTests | **62 / 62** |
| Integration | **96 passed / 6 skipped** |
| Client suites | **not run — no client file changed by this task** (verified: `git status` shows 0 `.ts`/`.tsx`) |
| Publish size | **45.05 MB compressed** under `pwsh` 7 — vs 45.03 MB prior and the 44.96 MB net10 baseline; ceiling 60 MB. Raw byte sum **137.47 MB**, **215 files, 4 `.pdb`** — composition identical to the prior measurement, which is the shell-independent check that makes the zip figure trustworthy. |
| New NuGet | **none** |
| CVE | `dotnet list package --vulnerable --include-transitive` → *"no vulnerable packages"* |
| DI budget (ADR-010) | **unchanged** — `TenantResolution` is a static class with no registration |

**Placement Justification (§10 bullet 2)**: `TenantResolution` belongs in the BFF because tenant
identity is derived from the BFF's own authenticated `ClaimsPrincipal`; there is no other host for it,
and it introduces no new dependency, package, endpoint or background work. It **removes** surface
rather than adding it: 21 inline resolution sites and three private near-duplicate helpers collapse to
one static method, and one endpoint parameter plus one bound property were deleted outright.

---

## 6. Surfaced, NOT fixed — deliberately out of scope

**(a) The cross-user DELETE gap.** `ChatSessionManager.DeleteSessionAsync(tenantId, sessionId)` has
**no owner check**, and `ChatSession` has **no owner field at all** — no `UserId`, no `Oid`, no
`CreatedBy`. An owner check is therefore not implementable without adding a field to the persisted
session model across Redis + Cosmos + Dataverse, populating it at creation, and deciding a policy for
sessions created before the field existed (failing closed would lock users out of their own history).
That is a schema change, not a 059-sized fix.

What 059 *does* change: the gap narrows from **cross-tenant** to **within-tenant**. Residual risk is
bounded by session IDs being `Guid.NewGuid().ToString("N")` — 128-bit random, not enumerable — so
exploitation requires a leaked session ID. **Owner decision needed** (see §8).

**(b) `RagEndpoints` body-supplied `TenantId` under an API-key principal.** The `RagApiKey` caller
asserts its own tenant in the request body (`RagEndpoints.cs:805-810`, and 7 further sites). That is
the same class of unauthenticated tenant assertion this task removed elsewhere, via a different
mechanism, guarded only by a shared secret. Out of 059's scope (it is not the header tier and not a
Compose surface), but it should not go unrecorded.

---

## 7. Stale comments corrected / found

- `useSseStream.ts:327` — *"the caller's authenticatedFetch owns Authorization / X-Tenant-Id"*.
  `authenticatedFetch` sets **only** `Authorization`. Same claim repeats in ~6 places across
  `SprkChat.tsx` and `useChatSession.ts`. **Not yet corrected** — client-side, and this task made no
  client changes; filed as follow-up together with retiring the now-inert header send.
- `KnowledgeBaseEndpoints.cs:534` — doc comment says the tenant resolves from "tid **or oid**". `oid`
  is the user object id, not a tenant. Code never did that; the comment was wrong. Corrected.
- `PrecedentAdminEndpoints.cs:88-91` — "D-P15 will centralize tenant resolution" placeholder, now
  discharged.

This is the fifth stale-comment finding in this project. The pattern holds: each one was load-bearing
for someone's decision not to look closer.

---

## 8. 🔔 What needs the owner

1. **Sign-off to merge** (CLAUDE.md §6 — security-sensitive). This is the acceptance criterion the
   task cannot self-satisfy.
2. **The cross-user question** (§6a): accept the residual within-tenant risk for now, or authorize a
   follow-up task to add an owner field to the session model? Recommendation: **accept for now**,
   file the follow-up. Session IDs are unguessable, and 059 removes the cross-tenant half, which is
   the half that mattered for arming.
3. **Arming** `SessionFileStore:BlobEndpoint` is no longer gated by *this* task once (1) lands — but
   the four operator steps in `current-task.md` are still outstanding and unrelated to it.
