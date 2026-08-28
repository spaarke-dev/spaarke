# Task 072 — Gate `POST /api/documents/{documentId}/share-link`

> **Status**: complete 2026-08-26 · FULL rigor · opus @ high · deps 046
> The one route on the `/api/documents` group with no per-document filter — and the one that mints a
> credential. Filed by the review that produced Wave 1; waived in task 074's ArchTest pending this task.

---

## 1. What was wrong

`FileAccessEndpoints.cs` registered the route with **no** `AddDocumentAuthorizationFilter`, while all
eight siblings on the same group carry `.AddDocumentAuthorizationFilter("read")`. The handler called:

```csharp
CreateSharingLinkAsUserAsync(context, driveId, itemId,
    linkType: "view", scope: "anonymous", expiration: null, ct: ct);
```

Three independent defects, in severity order:

1. **No per-document check.** Authority was the caller's container-scoped OBO access — coarser than
   per-document Dataverse rights, which is the exact coarseness the id-keyed routes in this file exist to
   close (its own header comment says so).
2. **`scope: "anonymous"` unconditionally** — converts "someone with container access" into "anyone with
   the URL", including unauthenticated parties outside the tenant.
3. **`expiration: null`** — the link outlives revocation entirely. A minted SPE URL is **not** revocable
   through Dataverse: removing the caller's rights afterwards does not invalidate it. So lifetime is the
   route's *only* revocation mechanism, and it was infinite.

## 2. The right required: `share`, and it was not a judgement call

The POML framed this as a decision between `"read"` (matches the siblings) and a new `"share"`. In fact
**`OperationAccessPolicy` already answers it twice for the same act** — `["driveitem.createlink"] =
AccessRights.Share` (:54) and its legacy alias `["share_document"] = AccessRights.Share` (:146). Task 072
follows that precedent rather than re-deriving it. It also matches Graph's own permission model for
`driveitem.createLink`.

**Why a bare `["share"]` key and not reuse of `driveitem.createlink`**: the *resource* differs. The
`driveitem.*` family authorizes an SPE item; `DocumentAuthorizationFilter` authorizes an `sprk_document`
ROW. That is the record-scoped convention task 003 established, and the table's own comment spells out
the hazard of ignoring it: *"reusing a driveitem.* key here would misdescribe the resource, and the legacy
delete_file/download_file aliases already show what happens when one table carries two conventions for the
same act."* The rights coincide; the subject does not.

**Why Share and not Read**: the eight siblings return content to an authenticated caller the platform can
still identify and revoke. This route publishes a durable handle that outlives revocation and — for
anonymous scope — is openable by parties with no Spaarke identity. Reading a document and publishing it
are different acts; Dataverse models the second as `Share`.

### Consumer audit (POML constraint)

Adding a key to `OperationAccessPolicy` is behaviourally inert for existing consumers. Verified rather
than assumed:

- The only public members are `GetRequiredRights` / `HasRequiredRights` / `GetMissingRights` /
  `IsOperationSupported` / `GetRequirementDescription` — all **keyed lookups**. A new key cannot change
  the answer for an existing key.
- The two enumeration APIs (`GetSupportedOperations`, `GetOperationsByCategory`) have **zero call sites**
  anywhere in `src/` or `tests/` (grep). Nothing asserts a closed set or a count.
- `OperationAccessPolicyCompletenessTests` is a **source scan**, so it discovers the new
  `AddDocumentAuthorizationFilter("share")` literal automatically and *requires* the policy key to exist.
  It went from "would fail" to green with the key added — the forcing function working as designed.
- `RegressionA3A20_Operation_DoesNotRequireDeleteOrShare` covers only task 003's four operations, so it is
  unaffected.

⚠️ **`share` inherits the same RPA dependency as `write`/`delete`**: `DataverseAccessDataSource`'s
fallback probe caps rights at Read by construction, so on a `RetrievePrincipalAccess` outage every
`share` gate denies and link-minting is *unavailable* rather than degraded. Correct fail-closed direction,
same trade tasks 008/022 accepted; recorded on the policy entry so it is not mistaken for a bug.

## 3. Anonymous scope — the escalation trigger's neighbourhood, resolved without stopping

The POML's trigger: *"If a shipped product flow depends on non-expiring anonymous links (e.g. an
external-recipient email flow), STOP and surface it."*

**Investigated. The trigger does not fire, and the POML's own constraint sanctions the path taken.**

The single live caller is the email composer's "Link" attachment path
(`createXrmEmailComposeHandlers.ts:353`, `onResolveShareLink`). Two findings:

| Question | Answer |
|---|---|
| Does the flow depend on **anonymous**? | **Yes, materially.** R2 item 12 exists so an emailed link opens the file *"including for external recipients"*. An `organization`-scoped link cannot open for someone outside the tenant. |
| Does the flow depend on **non-expiring**? | **No.** An emailed link is consumed in days, not years. Nothing reads or stores the lifetime. |

The dependency is on external *reach*, not on permanence — so the trigger's conjunction is not satisfied.
The POML's constraint then gives the path explicitly: *"If a product flow genuinely needs anonymous links,
make it opt-in per call, gate it on the higher right, and cap its lifetime harder."* That is what shipped.

**Also relevant: gating cannot break the flow.** The caller is best-effort by construction —
`if (!resp.ok) return null;` inside a `try/catch`, and the composer keeps the prior internal URL. A 403
(no `Share`, or anonymous switched off) degrades silently and never blocks a send.

### What shipped

- Route default is **`organization`**. Anonymous requires `allowExternalRecipients: true` in the body.
- Both scopes require **`Share` on the document**. Opting into external reach is **not** a way around the
  gate (there is a test for exactly that).
- Anonymous is capped **harder**: `AnonymousMaxLifetimeDays` (7) < `MaxLifetimeDays` (14).
- Anonymous minting logs at **Warning** with the caller's `oid` — the one branch that publishes a handle
  reachable by parties with no Spaarke identity, so attribution matters most there.
- `Documents:ShareLinks:AnonymousLinksEnabled` is a tenant-wide off switch that **refuses (403) rather
  than silently downgrading**. A silent downgrade yields a link that looks fine to the sender and is dead
  on arrival for the recipient — the failure mode hardest to diagnose from a support ticket.
- The client opts in explicitly, with the reason recorded inline.

### Residual, stated plainly

**Anonymous links still exist**, because the shipped feature needs them. They are now bounded (≤7 days),
gated on `Share`, explicitly requested, attributable, and disableable. What task 072 removed is
*permanent + anonymous + ungated*. It does not solve "minted URLs outlive revocation" in general — this
file's header already records that as open — and it is not claimed to.

### Why the lifetime ceilings are `[Range]`-validated

`MaxLifetimeDays` is `[Range(1, 90)]` and `AnonymousMaxLifetimeDays` is `[Range(1, 30)]`, bound with
`ValidateDataAnnotations().ValidateOnStart()`. That is deliberate: since lifetime is this route's only
revocation mechanism, an operator must not be able to configure an effectively-permanent link. Failing
startup on a bad value is the right direction; silently clamping would hide the misconfiguration. An
absent section binds valid defaults and boots.

## 4. A pre-existing defect this task had to fix to work at all

`DocumentAuthorizationFilter` had `return await next(context)` **inside its own try/catch**:

```csharp
try {
    var result = await _authorizationService.AuthorizeAsync(authContext);
    if (!result.IsAllowed) return ProblemDetailsHelper.Forbidden(result.ReasonCode);
    return await next(context);          // <-- inside the try
} catch (Exception ex) {
    return Results.Problem(500, "Authorization Error", "An error occurred during authorization");
}
```

So **every exception the downstream handler threw was rendered as `500 "Authorization Error"`** on all
nine routes carrying this filter. That silently converted each handler's intended status into a
misleading 500: a document 404, the 409 `no_file_attached`, the 409 `invalid_drive_id`, and — the case
that surfaced it — task 072's own **403** for a disallowed anonymous link. It also defeats the global
`UseExceptionHandler` that renders `SdapProblemException` as canonical ProblemDetails per ADR-019.

`AuthorizeAsync` itself cannot throw (it catches everything and fail-closed denies), so the catch block
was, in practice, catching only handler faults — i.e. exactly what it should not.

**Fixed by narrowing the try to the authorization call.** Not scope creep: task 072's own 403 path was
unreachable without it (its test proved it). Two reasons beyond the status code — the response contract,
and log honesty: *"Authorization failed for user…"* was being written for faults with nothing to do with
authorization, which is the kind of log line that misdirects an incident.

Blast radius checked: **1,085 auth/document tests pass** with the change, plus the full suite.

## 5. Tests — 12, all perturbation-verified

`tests/integration/auth/UnifiedAccessControl/ShareLinkAuthorizationTests.cs` (security-auth KEEP path).

**The load-bearing assertion in every denial test is `MintedLinks` being empty, not the status code.**
Task 009's lesson, and it matters more here than for a destroy: a 403 returned *after* Graph has issued
the URL is not a denial, and unlike a delete there is nothing to roll back.

| Perturbation | Red | Which |
|---|---|---|
| `.AddDocumentAuthorizationFilter("share")` detached | **3** | the three denial tests |
| `expiration: expiresAt` → `expiration: null` | **2** | both expiry tests |
| `scope` forced to `"anonymous"` (pre-072 behaviour) | **3** | the scope-default tests |

⚠️ **The first perturbation run reported a false PASS** — 12/12 green with the filter detached, because
`dotnet test` reused a stale build of the BFF assembly. Re-running with an explicit
`dotnet build src/server/api/Sprk.Bff.Api/` first produced the expected 3 failures. Worth recording as a
method note: **a perturbation that does not bite is not evidence until you have confirmed the
perturbation is actually in the built artifact.** Otherwise the "verification" step certifies nothing —
the same vacuity shape this project keeps finding in the code under test.

### Two fixture traps hit on the way

Both produced a *green denial suite next to a broken allowed path*, which is the shape that makes a gate
look verified when it is not — denials pass because endpoint-filter rejection happens **before** handler
parameters are resolved.

1. **`AddSingleton<SpeFileStore>`** — `SpeFileStore`'s four dependencies are all `Scoped`, so a singleton
   factory receives the ROOT provider and `GetRequiredService` throws *"Cannot resolve scoped service from
   root provider"* → 500 on the authorized path only. Registered `Scoped`. (The base fixture
   `DocumentDestroyAuthorizationTestFixture` has the same shape for its bulk-download stub; it happens not
   to matter there.)
2. **`GraphDriveId = "drive-{id}"`** — `ValidateSpePointers` requires SPE drive ids to start with `b!`, so
   every authorized request 409'd before reaching `createLink`. The base fixture's document double is
   sufficient for bulk download (which never validates the prefix) and not for this route; the derived
   fixture supplies its own with `b!drive-…` and `HasFile = true`.

`SpeFileStore.CreateSharingLinkAsUserAsync` was made `virtual` for the same reason `DownloadFileAsync`
already is: a denial test must be able to prove no link was minted.

## 6. ArchTest waiver removed

The task-072 `Pending` waiver in `RouteAuthorizationGuardTests.Waivers` is **deleted**, per that file's
maintenance rule 3 — a Pending waiver whose route has become gated is stale and fails `NoWaiverIsStale`.
Route guard: **10/10**. What 072 did and did not close is recorded at the deletion site so the next reader
does not have to find this file.

## 7. ⚠️ DEPLOY ORDERING — read before shipping

**The BFF change and the client change must ship together, or external recipients silently break.**

The route's new default is `organization` scope. A **deployed older client** posts `body: '{}'` — which
binds to `AllowExternalRecipients = null` → organization scope. So if the BFF ships before the rebuilt
`@spaarke/ui-components` bundle reaches the hosting code pages / PCFs:

- Emailed "Link" attachments keep working for internal recipients, and
- **silently stop opening for external recipients** — no error, no 4xx, just a link that demands a tenant
  sign-in the recipient does not have.

This is a *behaviour* regression with no error signal, which makes it the kind that gets reported as
"links are broken" days later. It is also the reason the request field is additive and optional rather
than required: making it required would have turned the same window into a hard 400 for every old
client, which is louder but breaks internal links too.

**Mitigation**: ship both halves in the same release, or set
`Documents:ShareLinks:AnonymousLinksEnabled` aside and accept organization-scope-only for the gap. There
is no server-side way to distinguish "old client that would have wanted anonymous" from "new client that
deliberately asked for organization", so this cannot be papered over in the BFF.

## 8. Follow-ups

- **F-1 — the `500 "Authorization Error"` mask may exist on other filters.** `DocumentAuthorizationFilter`
  is fixed. `BulkDownloadAuthorizationFilter`, `EntityAccessFilter`, `FinanceAuthorizationFilter`,
  `OfficeDocumentAccessFilter` and the rest were not audited for the same `next()`-inside-try shape. Worth
  a sweep; not done here because it is a contract change on surfaces this task does not otherwise touch.
- **F-2 — `GetOperationsByCategory()`'s "Legacy/Compatibility" bucket is `Keys.Where(k => !k.Contains("."))`,**
  so the record-scoped bare names (`read`, `write`, `delete`, now `share`) land there and are mislabelled
  as legacy. Pre-existing, cosmetic, zero call sites — recorded rather than fixed.
- **F-3 — minted links still outlive revocation.** Bounded now, not solved. A real fix needs either
  Graph permission deletion on revoke or a Spaarke-brokered redirect URL instead of a raw SPE link. Out of
  scope for a Wave 1 gate.
