# Incident 2026-08-26 — every BFF-created document 403s on preview/view

> **From**: `spaarke-auth-v4-dataverse-MI` (archived 2026-08-25). Filed here because the cause is
> `f076b1e38` — this project's task-002/FR-01 document authorization filter — not the credential migration.
> **Status**: diagnosed, NOT fixed. Deliberately left to you: it is your filter, your FR-01, and reverting a
> deliberate security narrowing from outside the project would be wrong.
> **Reported by**: operator, live on `spaarkedev1`, 2026-08-26.

---

## Symptom

Creating/saving a document succeeds. **Opening its preview immediately fails**, on two independent surfaces:

```
GET /api/documents/1d761626-b8a1-f111-aaad-7ced8ddc4a05/view-url    → 403
GET /api/documents/02d7362b-bba1-f111-aaad-70a8a590c51c/preview-url → 403
{status: 403, code: undefined, message: 'Access denied', correlationId: 'b2b07896-…'}
```

UI: *"You do not have permission to access this file."* — on a document the operator had **just created**,
whose AI Profile (TL;DR + Summary) renders fine beside the error.

Auth is healthy: `[SpaarkeAuth] Token acquired via in-memory-cache(browser-msal)` precedes each call.

---

## Root cause

`f076b1e38` (2026-08-24 21:23, **`fix(auth)!`** — you marked it breaking) added
`.AddDocumentAuthorizationFilter("read")` to the five URL-minting reads in `FileAccessEndpoints.cs`.
That filter enforces the **caller's own Dataverse Read on the `sprk_document` row**.

**BFF-created document rows are owned by a service application user, not by the human who caused the
creation.** So the caller has no ownership-derived Read, and the filter denies.

Your own comment on those lines predicts the symptom exactly:

> *"a caller with container access but no Read on the `sprk_document` row previously succeeded and now
> gets 403. That caller seeing another client's document is precisely the disclosure this project exists
> to close (spec FR-01), so the narrowing is the point, not a side effect."*

**The narrowing is doing what it says. The problem is the population it catches.** The intended target was
"a caller reading *another client's* document." The actual population is **every user opening a document
they just created themselves**, because the BFF stamps the service identity as owner.

---

## It is NOT the auth-v4 credential migration — the check that settles it

The obvious hypothesis is that auth-v4 changed document ownership. It did not change the *class*, and this
is the query that shows it:

```sql
SELECT TOP 6 d.createdon, o.fullname AS owner_name, o.applicationid
FROM sprk_document d JOIN systemuser o ON d.ownerid = o.systemuserid
WHERE d.createdon < '2026-08-13' ORDER BY d.createdon DESC
```

| createdon | owner | applicationid |
|---|---|---|
| 2026-08-26 *(the failing doc)* | `# mi-bff-api-dev` | `5967251e-…` (UAMI) |
| 2026-08-12 | Ralph Schroeder | *null* — user-created, not via the BFF path |
| **2026-08-06** | **`SDAP-BFF-SPE-API`** | `1e40baad-…` (app registration) |
| **2026-08-06** | **`SDAP-BFF-SPE-API`** | `1e40baad-…` |
| **2026-08-05** | **`SDAP-BFF-SPE-API`** | `1e40baad-…` |

BFF-created rows were owned by an **application user** well before auth-v4. auth-v4 changed *which*
application user (`SDAP-BFF-SPE-API` → `# mi-bff-api-dev`); it did not introduce service ownership.

**The user never had ownership-based Read on these rows. What changed on 2026-08-24 is that something
started enforcing it.**

⚠️ One consequence worth pricing in: because the ownership pattern is old, **the back catalogue is affected
too**, not just newly-created documents. Any `sprk_document` owned by either service identity is currently
unreadable through these five routes by a caller who lacks a share or a team grant.

---

## Blast radius

All five URL-minting reads gated by `f076b1e38`, plus the write it also gated:

```
GET /api/documents/{id}/preview-url     ← confirmed failing
GET /api/documents/{id}/view-url        ← confirmed failing
GET /api/documents/{id}/preview
GET /api/documents/{id}/office
GET /api/documents/{id}/open-links
    …/analyze (write)
```

Confirmed reproducing from **two independent clients** — the document form preview
(`useDocumentPreview`) and `SemanticSearchApiService.getPreviewUrl` — so it is server-side, not a
client-specific regression.

---

## ⚠️ Read this before diagnosing further: the 403 is ambiguous by design

`DataverseAccessDataSource` is documented **"Implements fail-closed security: returns `AccessRights.None`
on errors"**. So a **failed access lookup** and a **genuine denial** produce the *identical* 403 with the
identical body. You cannot tell them apart from the client.

This matters because there is a second, non-obvious candidate cause sitting right next to the first — your
own comment at `DataverseAccessDataSource.cs:443`:

> *"RetrievePrincipalAccess 'may not be available' with delegated tokens. **That claim is unverified**…
> rather than bet the fix on it, any RetrievePrincipalAccess failure falls back to the original…"*

If `RetrievePrincipalAccess` is failing under the OBO/delegated token, the fallback and the fail-closed
default would produce this same 403 **even for a user who does have rights**. That is a materially
different bug with a different fix.

**Do not assume which one this is.** The `[UAC-DIAG]` logging you added distinguishes them:

```bash
az webapp log tail -g rg-spaarke-dev -n spaarke-bff-dev \
  --subscription 484bc857-3802-427f-9ea5-ca47b43db0f0 | grep -E "UAC-DIAG|AUTHORIZATION (DENIED|GRANTED)|Fail-closed"
```

Reproduce the preview while that runs. `AUTHORIZATION DENIED … (AccessRights: None)` alongside a clean
`GetUserAccessAsync` = genuine denial (ownership). A `Fail-closed` line or a `RetrievePrincipalAccess`
failure = the lookup broke and the rights question was never actually answered.

*(The Kudu docker log carries only container lifecycle, not app logs — a live stream during a repro is the
only way to capture this. I could not capture it myself without an operator reproducing.)*

---

## Candidate fixes

Access resolution goes through **`RetrievePrincipalAccess`**, which honours Dataverse sharing (POA) — so
both of the first two options genuinely grant Read.

| # | Fix | Assessment |
|---|---|---|
| **1** | **BFF sets `ownerid` to the calling user** when creating a document on their behalf | **Recommended.** The user *is* the owner in every sense that matters; the service identity owning it is an artifact of how the write is executed, not a statement about the data. Also fixes the back catalogue's root cause going forward and needs no per-row grant. |
| **2** | **Share the row with the caller on create** (POA grant) | Works, and `RetrievePrincipalAccess` will see it — but it is a per-row side-effect that must never be missed, on every create path, forever. More moving parts than #1. |
| **3** | **Filter treats service-owned rows specially** | ❌ Weakest. It reintroduces exactly the hole FR-01 closes: "owned by the service" would become a blanket bypass, and every BFF-created row is service-owned. |

**Either way there is a back-catalogue question** — existing service-owned rows need a one-time remediation
(re-own or share), or they stay unreadable through these routes. Worth deciding explicitly rather than
discovering later.

---

## How it reached dev

`f076b1e38` merged to master 2026-08-24 21:23. `spaarke-auth-v4-dataverse-MI` deployed the BFF from a
master-merged branch several times on 08-24/08-25, so **auth-v4's deploys carried this change to
`spaarkedev1`** — the same way they carried `code-quality-and-assurance-r3`'s CORS narrowing, which
produced the UAT blocker two days earlier (`projects/spaarke-auth-v4-dataverse-MI/notes/uat-findings-2026-08-24.md`).

Not a complaint about your change — it is correctly scoped and correctly marked breaking. It is a note that
**dev deploys are shared**, so a deliberate narrowing lands for everyone the moment any project deploys,
which compresses the window between "merged" and "someone hits it" to whenever the next unrelated deploy
happens.

---

## Evidence trail

| | |
|---|---|
| Failing doc | `1d761626-b8a1-f111-aaad-7ced8ddc4a05`, created 2026-08-26T21:39:38, owner **`# mi-bff-api-dev`** |
| Filter commit | `f076b1e38` 2026-08-24 21:23 — `fix(auth)!: gate analyze (write) + the five URL-minting reads` |
| Filter | `Api/Filters/DocumentAuthorizationFilter.cs` → `Spaarke.Core/Auth/AuthorizationService.cs` → `IAccessDataSource` |
| Rights source | `DataverseAccessDataSource` → `RetrievePrincipalAccess` (honours sharing), fail-closed to `AccessRights.None` |
| Correlation IDs | `b2b07896-c869-4b96-8dea-fd49f8088794` (view-url) |
