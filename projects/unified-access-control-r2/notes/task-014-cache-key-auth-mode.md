# Task 014 — Access cache key must include auth mode (SP vs OBO)

> Closes finding A-19 (spec FR-13, Medium). Status: complete.

## What changed

`src/server/api/Sprk.Bff.Api/Infrastructure/Caching/CachedAccessDataSource.cs` — the resource-access
cache key gained an `{authMode}` segment:

```
Before: sdap:auth:access:{userId}:{resourceId}
After:  sdap:auth:access:{authMode}:{userId}:{resourceId}   // authMode = "sp" | "obo"
```

`authMode` is computed as `string.IsNullOrEmpty(userAccessToken) ? "sp" : "obo"` — a two-value flag,
never the raw token. Nothing else in the method changed: same TTL (60s), same fire-and-forget write
(`_ = CacheSnapshotAsync(...)`), same roles/teams caching, same fail-open-on-cache-error behavior.
`DataverseAccessDataSource` (what the inner source returns) was NOT touched — that is tasks 004/005.

## Why a boolean mode flag is sufficient (the escalation trigger, resolved)

The POML's escalation trigger said: *if distinguishing OBO callers requires a token claim that is not
reliably present, STOP and escalate rather than silently keying only on an SP/OBO boolean (which would
still let two different OBO users collide on the same userId).*

Traced both call sites before deciding:

- `Spaarke.Core/Auth/AuthorizationService.cs:48-52` — always calls with `userAccessToken: null`
  (`context.UserId` is the only identity input; SP mode).
- `Sprk.Bff.Api/Services/Ai/AiAuthorizationService.cs:63,78,102,176-180` — extracts `userId` via
  `ExtractUserId(user)` (the `oid` claim, or its XML-schema/`NameIdentifier` fallbacks) from the
  **same** `ClaimsPrincipal` it pulls `userAccessToken` from (`TokenHelper.ExtractBearerToken(httpContext)`)
  in the same request. `userId` and `userAccessToken` are always sourced from one caller's own validated
  identity — there is no code path where two different OBO callers share a `userId`, because `userId`
  **is** the `oid` claim, i.e. the identity itself, not a coarser grouping (like a session or tenant id)
  that could plausibly alias two people.

**Conclusion: the escalation trigger's hypothetical does not apply here.** A boolean SP/OBO flag is
sufficient because the collision case the trigger warns about — "two different OBO users on the same
userId" — cannot occur in this codebase's current call sites; `userId` already disambiguates by caller.
No token-identity hash was added; doing so would have been unjustified complexity per CLAUDE.md §11
(no concrete failure mode it closes that the boolean flag doesn't already close). Did **not** stop/escalate
— proceeded with the boolean flag design, documented this reasoning inline in the class doc comment.

If a future call site ever passes a `userAccessToken` **without** `userId` reliably being that same
token's own `oid` (e.g., an impersonation path where one principal acts for another under a shared
`userId`), that would reopen this question and should re-trigger the escalation — noted inline in the
class summary for the next reader.

## Tests flipped

`tests/integration/auth/UnifiedAccessControl/AccessCacheCharacterizationTests.cs` (KEEP path:
`tests/integration/auth/**`, ADR-038 §2 security-auth; compiled into `Sprk.Bff.Api.Tests` via the
existing `LinkBase="AuthTests"` glob — did **not** create `tests/unit/.../AccessControl/` per the
orchestrator's explicit correction).

All three `Characterization_` tests pinning A-19 were flipped (renamed, doc comments changed from
"CURRENT (BROKEN) BEHAVIOR" to "✅ FLIPPED BY TASK 014"):

1. `Characterization_GetUserAccessAsync_ServesAppOnlySnapshotToOboCaller` →
   `GetUserAccessAsync_OboCallerAfterAppOnlySnapshotCached_MissesCacheAndConsultsInnerSource` — an
   SP-mode-seeded entry no longer answers an OBO call; asserts the OBO caller gets the true (different)
   inner-source answer AND that the inner source was reached with the OBO token (anti-vacuity — a
   "None" result reached some other way wouldn't prove the fix).
2. `Characterization_GetUserAccessAsync_ServesOboSnapshotToAppOnlyCaller` →
   `GetUserAccessAsync_AppOnlyCallerAfterOboSnapshotCached_MissesCacheAndConsultsInnerSource` — the
   mirror direction; an OBO-seeded entry no longer answers an SP-mode call.
3. `Characterization_CacheKey_DoesNotVaryWithAuthMode` →
   `GetUserAccessAsync_SameUserAndResourceDifferentAuthMode_ProducesDistinctCacheEntries` — proves the
   key now varies with auth mode: the SP-mode call still HITS its own seeded entry (no caching
   regression), while the OBO-mode call — same user, same resource — MISSES and reaches the inner
   source. Exactly one inner call (the OBO one) is asserted, proving the SP call stayed a genuine hit.

Added one new test (not in the original three, but needed to make acceptance criterion "the raw token
does not appear in the cache key" testable as a black-box assertion, without reflection —
`GetUserAccessAsync_CacheKey_DoesNotEmbedRawUserAccessToken`): seeds a key shaped as if the raw token
occupied the auth-mode position; proves that key is unreachable (a request with that exact token still
misses and reaches the inner source), which it would not if the token were literally used as/in the key.

The three negative tests (`OnCacheMiss_DelegatesToInnerSourceAndForwardsToken`,
`ForDifferentResources_DoesNotShareCacheEntry`, `ForDifferentUsers_DoesNotShareCacheEntry`) were left
logically unchanged — only the `ProductionCacheKey` constant's literal value was updated to the new
SP-mode key shape, since all three already use `userAccessToken: null`.

## Verification

- `dotnet build tests/unit/Sprk.Bff.Api.Tests/Sprk.Bff.Api.Tests.csproj` — succeeded, 0 errors.
- `dotnet test ... --filter "FullyQualifiedName~Sprk.Bff.Api.Tests.AccessControl"` — 82/82 passed
  (whole `AccessControl` namespace, 7 files).
- `dotnet test ... --filter "FullyQualifiedName~AccessCacheCharacterizationTests"` — 7/7 passed (6
  before this task → 7 after: 3 flipped + 1 new negative + 3 unchanged negative).
- `dotnet list ... package --vulnerable --include-transitive` — no vulnerable packages (no `.csproj`
  touched by this task).
- Publish size: `dotnet publish -c Release src/server/api/Sprk.Bff.Api/` zipped to **43.67 MB**
  compressed incl. PDBs — vs the ~44.96 MB root-CLAUDE.md baseline and task 003's more recent 43.65 MB
  measurement; a +0.02 MB delta from task 003's number, effectively noise (doc-comment-only IL/PDB
  growth). Well under the 55 MB review threshold and the 60 MB hard ceiling.
- Full `Sprk.Bff.Api.Tests` suite run was kicked off for final confirmation; see task completion report
  for its result (background run — reported once it lands).

## Ordering note (carried forward)

This MUST merge before task 004 (which flips `AuthorizationService` from `userAccessToken: null` to a
real caller token). Once task 004 lands, `AuthorizationService`'s calls become OBO too, so both callers
will present real tokens — the SP branch of the `authMode` flag then only fires for any remaining
app-only call sites (background jobs, etc.), and the key still separates them correctly by construction.
No follow-up change to this file is anticipated when task 004 lands.
