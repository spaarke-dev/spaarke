# `SecurityEvents.Read.All` — grant record

> **Task 013** (spec FR-B04) · granted **2026-08-23** · verified live against Microsoft Graph
> **No secret value, token, or assertion appears in this file.** App ids, tenant ids, role ids and
> credential key ids are public identifiers, not credentials.

---

## The grant

| Field | Value |
|---|---|
| **Tenant** | `a221a95e-6abc-4434-aecc-e48338a1b2f2` — Spaarke ("Spaarke Dev") |
| **Registration granted** | `SDAP-PCF-CLIENT` — `170c98e1-d486-4355-bcbe-170454e0207c` |
| **Service principal** | `ab1eae64-ef31-4d72-bcab-62db4610075e` |
| **Permission** | `SecurityEvents.Read.All` (**application**) — `bf394140-e372-4bf9-a898-299cfc7564e5` |
| **Resource** | Microsoft Graph — SP `ba630d35-4fd8-4c4f-a3f5-c253c2a85a90` |
| **Assignment id** | `ZK4eqzHvck28q2LbRhAHXjk_FUWkSjRAm0xqNk0hqwg` |
| **Consented** | 2026-08-23T20:01:01Z, by `ralph.schroeder@spaarke.com` (tenant admin) |
| **Method** | manifest declaration + a **single** `appRoleAssignments` POST |

### Why not `az ad app permission admin-consent`

That command consents **everything** declared in the manifest, including anything requested but
deliberately left unconsented. A direct `appRoleAssignments` POST grants exactly one permission.
Verified by diffing the assignment set before and after: **added `SecurityEvents.Read.All`, removed
nothing, one permission total.**

`SecurityEvents.ReadWrite.All` (`d903a879-88e0-4c09-b0c9-82f6a1333f84`) was **NOT** granted — the screen
is read-only (POML constraint + acceptance criterion 3).

---

## Which registration, and why it is not the BFF

The Security screen authenticates as the **container-type config's owning app**, not the BFF:

`SecurityEndpoints` → `GetSecurityAlertsForConfigAsync(config)` → `GetClientForConfigAsync(config)` →
reads `sprk_specontainertypeconfig` → fetches its Key Vault secret → app-only token **in the tenant named
by that config's `sprk_speenvironment.sprk_tenantid`**.

**This is correct, and it is load-bearing.** A Spaarke environment can manage container types living in
**customers' own Entra tenants** (operator-confirmed 2026-08-23). Secure Score is a property of *a
tenant*, so the config selection determines *whose* tenant is read. The BFF's own app-only identity
(`IGraphClientFactory.ForApp()`) authenticates in the BFF's home tenant and therefore **could not read a
customer tenant's Secure Score at all**.

> ⚠️ **A correction, recorded so it is not repeated.** During analysis I argued the opposite — that
> routing tenant-wide security data through a container-type config was a modeling error and the grant
> belonged on the BFF. That reasoning assumed one tenant per environment. Under the real multi-tenant
> model it is wrong, and the BFF-identity option is not merely worse but unworkable. The POML's literal
> instruction was right. See [`app-registration-topology.md`](app-registration-topology.md).

### 🔴 Consequence: this is per-customer onboarding, not one-time setup

Every customer tenant Spaarke manages needs this permission granted **and admin-consented in that
tenant**, on that customer's owning app. Recorded in
[`docs/guides/auth-deployment-setup.md`](../../../docs/guides/auth-deployment-setup.md) **§5e**, with the
full owning-app permission table.

---

## Verification (live, after the grant)

App-only token acquired as the owning app against tenant `a221a95e-…`, then replayed against Graph:

| Check | Result |
|---|---|
| `roles` claim carries `SecurityEvents.Read.All` | ✅ present (was absent before) |
| `GET /v1.0/security/secureScores?$top=1` | ✅ **HTTP 200** — real data, `azureTenantId a221a95e-…` |
| `GET /v1.0/security/alerts` (legacy) | ✅ **HTTP 200**, `"value": []` |
| `GET /v1.0/security/alerts_v2` ← **what the code calls** | ⚠️ **HTTP 403** — `"Unauthorized request - Account is not provisioned."` |

**The Secure Score half of the screen now works.** Before the grant, every one of these returned 403
`accessDenied`.

---

## 🔔 The cause has changed — escalated, not assumed away

POML step 4 requires the *new* error be captured rather than explained away, and escalation trigger 2
forbids widening permissions to make a failure disappear. Both honored: **nothing further was granted.**

`alerts_v2` now fails with a **different error and a different cause**:

```
403  {"error":{"code":"Unauthorized",
      "message":"Unauthorized request - Account is not provisioned."}}
```

This is **not** a permissions failure. `GET /security/alerts_v2` surfaces Microsoft 365 Defender
incidents and requires a **Defender workload provisioned in the tenant**; Spaarke Dev has none.

**The decisive evidence** is the pair: legacy `/security/alerts` returns **200 with an empty array** on
the *same token, same tenant, same moment*. A permission problem could not produce 200 on one security
endpoint and 403 on another. So the permission is right and `alerts_v2`'s prerequisite is missing.

**No broader Graph permission can fix this** — `SecurityEvents.ReadWrite.All` would not, and granting it
to make the error go away is precisely the failure mode this project exists to correct.

### Options for the operator (none taken)

| Option | Effect |
|---|---|
| **Leave as-is** | Secure Score works; Alerts shows a 403 whose real Graph message is surfaced (task 001). Honest, partially functional. |
| **Provision Defender in the tenant** | Environment change, well outside R2. Would make `alerts_v2` return data. |
| **Fall back to legacy `/security/alerts`** when `alerts_v2` reports not-provisioned | Code change — the legacy endpoint already returns 200 here. See the finding below. |

---

## 🔎 Separate finding — recorded, NOT fixed (per the POML constraint)

The POML forbids modifying `SecurityEndpoints.cs` unless verification proves a code defect, and requires
any such defect be recorded separately. Two related items:

1. **The 403 hint is now misleading in this tenant.** `SecurityEndpoints.cs:~160` says *"The most common
   cause is a missing `SecurityEvents.Read.All` grant."* That grant is now present and provably working,
   so the hint points at a cause that has been eliminated. It is hedged ("most common cause") and task
   001 appends Graph's real message alongside it, so the operator does see the truth — but the hint
   should recognise the not-provisioned signal and say so instead.
2. **`alerts_v2` vs `alerts`.** `SpeAdminGraphService.cs:4593` calls `Security.Alerts_v2`. In a tenant
   without Defender, the legacy `Security.Alerts` returns 200 (empty) where `alerts_v2` 403s. A
   fallback, or an explicit "Defender not provisioned" empty state, would make the screen honest rather
   than broken-looking.

→ Both belong to a Workstream C task touching the Security screen, or to
`speadmingraphservice-decomposition-r1`. **Neither is a permissions problem.**

---

## Acceptance criteria

| # | Criterion | Status |
|---|---|---|
| 1 | `SecurityEvents.Read.All` granted with tenant admin consent on the correct registration | ✅ |
| 2 | Security screen renders secure-score / alert data | ⚠️ **partial** — Secure Score ✅ 200; alerts blocked by a **non-permission** tenant prerequisite (escalated) |
| 3 | Negative: only `SecurityEvents.Read.All`; no broader Security permission | ✅ verified by before/after diff — exactly one added |
| 4 | Negative: if it still fails, the NEW error is captured, not resolved by widening | ✅ captured above; nothing further granted |
| 5 | Recorded in `notes/` and added to `auth-deployment-setup.md` §5 | ✅ this file + **§5e** |
| 6 | No new client secret or credential introduced (ADR-028) | ✅ none — a permission on an existing registration |

**No code changed. No new NuGet. No BFF publish-size impact.**
