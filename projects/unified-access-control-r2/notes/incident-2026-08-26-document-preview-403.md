# Incident 2026-08-26 — every document 403s: `DocumentAuthorizationFilter` reads `sub`, not `oid`

> **From**: `spaarke-auth-v4-dataverse-MI` (archived 2026-08-25). Filed here because the defect is in
> `Api/Filters/DocumentAuthorizationFilter.cs`, added by `f076b1e38` (this project, task 002 / FR-01).
> **Status**: root cause proven from live logs. **NOT fixed** — you own the file and the fix.
> **Severity**: total document-access outage on `spaarkedev1`. Every user, every document, five routes.

> ## ⚠️ THIS DOCUMENT WAS REWRITTEN 2026-08-26
> An earlier revision blamed **document ownership** — service-owned `sprk_document` rows colliding with the
> new per-document Read gate. **That diagnosis was WRONG** and is retracted in full. It was plausible,
> internally consistent, and disproved in one step the moment the operator tested a document *they
> personally own* and it 403'd too. If you read the earlier version, discard it; §"Retracted" records what
> it claimed and why it was wrong, because a silently-replaced falsehood teaches nobody.

---

## Root cause — one line

```csharp
// Api/Filters/DocumentAuthorizationFilter.cs:49
var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
```

`MapInboundClaims` is not disabled anywhere in `src/`, so it defaults to **true** and
`ClaimTypes.NameIdentifier` silently aliases the **`sub`** claim — Entra's *pairwise, per-application*
subject identifier. It is not a GUID and it is not the user's directory object id.

`DataverseAccessDataSource.GetUserAccessAsync` expects an **Azure AD OID**. Its own log parameter says so:
`AzureAdOid=`. Given a `sub`, no `systemuser` matches, the class is documented *"fail-closed: returns
`AccessRights.None` on errors"* — and every caller is denied every document.

**`DocumentAuthorizationFilter` is the sole outlier in the codebase.** Every peer filter reads `oid` first:

| Filter | Line | Pattern |
|---|---|---|
| `AiAuthorizationFilter` | 58 | `oid ?? objectidentifier ?? …` |
| `AnalysisAuthorizationFilter` | 94 | `oid ?? objectidentifier ?? …` |
| `AgentAuthorizationFilter` | 53 | `oid ?? objectidentifier ?? …` |
| `AiAuthorizationService` | 229 | `oid ?? objectidentifier ?? …` |
| **`DocumentAuthorizationFilter`** | **49** | **`ClaimTypes.NameIdentifier` only** ← |

---

## Live proof — both identities, one request

Captured via `az webapp log tail` while the operator reproduced (2026-08-27T02:58Z):

```
[UAC-DIAG] GetUserAccessAsync START: AzureAdOid=d12L59FRq5S6dJP4qZ-wuS3RS5TYJnXdFpPUZH-rkjg …
AUTHORIZATION DENIED: User d12L59FR…rkjg denied read on 02d7362b-… by OperationAccessRule
    Reason: sdap.access.deny.insufficient_rights (AccessRights: None)

[UAC-DIAG] RetrievePrincipalAccess SUCCESS: User=1d02f31c-1872-f011-b4cb-7c1e52671ad0,
    Resource=02d7362b-…, GrantedAccess=Read, Write, Delete, Create, Append, AppendTo, Share
Access snapshot retrieved for user c74ac1af-ff3b-46fb-83e7-3063616e959c:
    AccessRights=Read, Write, Delete, Create, Append, AppendTo, Share, Teams=5, Roles=1
[AI-AUTH] Access check PASSED: UserId=c74ac1af-… AccessRights=Read, Write, …
```

Two identities for one human, resolving oppositely:

| value | claim | outcome |
|---|---|---|
| `d12L59FRq5S6dJP4qZ-wuS3RS5TYJnXdFpPUZH-rkjg` | **`sub`** (base64url, not a GUID) | **DENIED**, `AccessRights: None` |
| `c74ac1af-ff3b-46fb-83e7-3063616e959c` | **`oid`** | **PASSED**, full rights, Teams=5 Roles=1 |

**`RetrievePrincipalAccess` is working correctly.** It returns `Read, Write, Delete, Create, Append,
AppendTo, Share` for this user on this exact document. The lookup is not broken and rights are not missing —
the filter simply asks about the wrong principal.

---

## Retracted: the ownership theory, and why it was wrong

The first diagnosis was that `f076b1e38`'s narrowing was catching **service-owned rows** — BFF-created
`sprk_document` records are owned by an application user (`# mi-bff-api-dev` now, `SDAP-BFF-SPE-API` before
2026-08-13), so the human had no ownership-derived Read.

Every fact in that chain was true. The conclusion was still wrong:

- **Disproving step**: the operator opened `8f6b371f-8a96-f111-b8db-0022482fb5a7` — created 2026-08-12,
  `ownerid` = *Ralph Schroeder*, a document he personally owns. **It 403'd too.** Ownership cannot explain a
  denial on a self-owned row.
- The logs then showed why: rights were never the question. `RetrievePrincipalAccess` grants full access;
  the filter asked about a principal that does not exist.

Worth stating plainly because it is the trap this endpoint class sets: **a 403 here is ambiguous by
construction.** Fail-closed means a *failed lookup* and a *genuine denial* emit the identical status, body
and reason code. The ownership theory fit every client-side observation perfectly. Only `[UAC-DIAG]`
separated them — the client cannot, and neither can a reviewer reasoning from the symptom.

---

## Blast radius

All five URL-minting reads gated by `f076b1e38`, plus the write:

```
GET /api/documents/{id}/preview-url    ← confirmed
GET /api/documents/{id}/view-url       ← confirmed
GET /api/documents/{id}/preview
GET /api/documents/{id}/office
GET /api/documents/{id}/open-links
    …/analyze (write)
```

Reproduced from two independent clients (`useDocumentPreview`, `SemanticSearchApiService.getPreviewUrl`) —
server-side, not client-specific. Reached dev because auth-v4 deployed the BFF from a master-merged branch
on 08-24/08-25, carrying `f076b1e38` with it.

---

## The fix

```csharp
var userId = httpContext.User.FindFirst("oid")?.Value
    ?? httpContext.User.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value
    ?? httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
```

Mirrors the four peers exactly. Risk is minimal in the strict sense that the filter currently denies
*everyone* — it can only begin resolving correctly — and the logs already prove the right id yields the
right answer.

---

## ⚠️ The regression test is the part that matters

Credit to the reviewing project for this; it is the durable half of the incident.

**The auth fixtures set `oid` and `NameIdentifier` to the same constant**, which is why ~11,932 tests were
green while every document request 403'd. Confirmed in this project's own fixture:

```csharp
// tests/integration/auth/UnifiedAccessControl/DocumentDestroyAuthorizationTestFixture.cs:203-204
new Claim("oid", WorkspaceTestConstants.TestUserId),
new Claim(ClaimTypes.NameIdentifier, WorkspaceTestConstants.TestUserId),   // ← identical
```

With both claims equal the two are **indistinguishable**, so no test can detect reading the wrong one. A
regression test written against this fixture would pass before *and* after the fix — rebuilding the blind
spot rather than covering it.

**The divergent pattern already exists in-repo**, so this is closing an inconsistency, not inventing a
convention:

```csharp
// CommunicationCreateRecordThreadContractTests.cs:292-293
new Claim("oid", "test-user-oid"),
new Claim(ClaimTypes.NameIdentifier, "test-user-id"),   // ← divergent
```

`ExternalAccessContractTests.cs:505-507` is also collapsed and worth the same sweep.

Recommendation: assert the filter resolves the **`oid` specifically** — not merely that access is granted —
so a future fixture change cannot silently re-collapse the two.

---

## On the root fix (`MapInboundClaims = false`) — sequencing

The reviewing project proposes disabling `MapInboundClaims` globally so `ClaimTypes.NameIdentifier` stops
aliasing `sub` and the class cannot recur. **Agreed on direction** — this has now bitten three times (F8
here, `OfficeEndpoints` 2026-08-25, this today), and per-site fixes only ever catch the site that already
failed.

Measured blast radius before recommending order:

```
74 sites read ClaimTypes.NameIdentifier across src/server, in 12+ files
only ~28 have an oid fallback nearby
```

With the flag off, all 74 return **null**. Sites with `?? oid` are unaffected; roughly half are not, and
those move from silently-wrong to null-identity.

**The sequencing point: the fixture sweep is a PREREQUISITE, not a companion.** While `oid ==
NameIdentifier` in the fixtures, the suite stays green whichever claim any site reads — so a 74-site change
would ship with **zero verification of the thing it changes**. Diverge the fixtures first and the root fix
becomes checkable; do it in the other order and you are changing auth resolution at 74 sites on a
fail-closed surface with a suite that provably cannot see the bug class.

Suggested order: **(1)** one-line fix here + divergent-value regression test → **(2)** fixture sweep
repo-wide → **(3)** audit the 74 sites and add `oid` fallbacks → **(4)** flip `MapInboundClaims = false`.
Steps 2–4 are F8's scope and deserve their own funding rather than riding a hotfix.

---

## Evidence trail

| | |
|---|---|
| Defect | `Api/Filters/DocumentAuthorizationFilter.cs:49` |
| Introduced | `f076b1e38` 2026-08-24 21:23 — `fix(auth)!: gate analyze (write) + the five URL-minting reads` |
| Chain | filter → `Spaarke.Core/Auth/AuthorizationService.cs` → `IAccessDataSource` → `DataverseAccessDataSource` → `RetrievePrincipalAccess` |
| Fail-closed | `DataverseAccessDataSource.cs:14` — *"returns AccessRights.None on errors"* |
| Denied id | `d12L59FRq5S6dJP4qZ-wuS3RS5TYJnXdFpPUZH-rkjg` (`sub`) |
| Granted id | `c74ac1af-ff3b-46fb-83e7-3063616e959c` (`oid`) → systemuser `1d02f31c-1872-f011-b4cb-7c1e52671ad0` |
| Self-owned doc that still 403'd | `8f6b371f-8a96-f111-b8db-0022482fb5a7` (owner: Ralph Schroeder) |
| Capture | `az webapp log tail -g rg-spaarke-dev -n spaarke-bff-dev \| grep -E "UAC-DIAG\|AUTHORIZATION"` |
