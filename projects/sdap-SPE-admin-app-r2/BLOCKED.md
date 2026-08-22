> ## ✅ RESOLVED 2026-08-22 — operator chose **path A** (BFF identity, existing OBO path)
> 
> Container types now use `IGraphClientFactory.ForUserAsync`, the BFF's existing OBO exchange —
> already used by SPE file operations, the Agent, and the Dataverse user client. **No new
> `.WithClientSecret` site was created**, so the A4/E-3 concern below was overstated: the BFF
> already had four OBO sites, and SpeAdmin now reuses one instead of adding a fifth.
> 
> **Still open, and now the binding constraint for Model 1:** the BFF identity can reach every
> container type, so cross-customer isolation moved from Entra into our code — where it does not
> yet exist. See [`notes/tenant-isolation-gap.md`](notes/tenant-isolation-gap.md). Required before
> multi-customer go-live; not required for Model 2 (dedicated).
> 
> ADR-028 still needs amending so E-1 stops describing a per-customer owning app that does not
> exist for SpeAdmin (path B remains outstanding as a docs deliverable).

---

# BLOCKED — task 010 (OBO spike) · Workstream B halted

> Written 2026-08-21 by task 010 per its escalation contract and root CLAUDE.md §6 / §6.5.
> Evidence: [`notes/obo-spike-findings.md`](notes/obo-spike-findings.md).
> **Blocks**: 011 → 012, and everything from 020 onward that depends on 011.

---

## Blocking condition

The spike returned **UNWORKABLE**. The per-customer owning-app OBO shape cannot obtain a
Graph-audienced delegated token, and cannot be repaired within this task's constraints.

**Escalation triggers 1 and 2 both fired.** Trigger 3 (missing permission/consent) did not — the
permissions are already granted.

## What was attempted

Both defects were confirmed against the code and then against the live Spaarke Dev tenant. Read-only
throughout; no container type was created, modified, or deleted.

| Finding | Evidence |
|---|---|
| Defect 1 is fatal at the resource level, not merely a wrong audience | `api://{owningAppId}/.default` → `AADSTS500011: resource principal … not found`. The app exposes **no** `identifierUris` and **no** scopes. |
| Defect 2 is structural | OBO requires the exchanging confidential client to be the assertion's audience. Assertion `aud` = BFF `1e40baad-…`; the code builds the client as owning app `170c98e1-…`. |
| **The premise is wrong** | `sprk_owningappid` = `170c98e1-…` = **`SDAP-PCF-CLIENT`** — the SPA client the code page already signs in as. **There is no separate per-customer owning app in this environment.** |
| Permissions are fine | `FileStorageContainerType.Manage.All` is delegated + admin-consented on **both** service principals. |
| Spec §3.1 confirmed | App-only `GET …/containerTypes` → **403 accessDenied** on v1.0 *and* beta. |

## Why this reopens the §6.5 gate

The gate was resolved as **path C (comply under ADR-028 E-1)**. E-1 covers *"per-customer owning apps,
which are other applications' identities."*

**That premise does not hold.** The named owning app is this application's own SPA client. E-1's
exemption does not describe the situation, so path C was decided on facts that turn out to be false. A
decision reached on a false premise has to be re-taken, not reinterpreted.

## 🔔 ADR Conflict — Resolution Required

- **ADR in question**: ADR-028 (Spaarke Auth v2), Amendment **A4**, exceptions **E-1** / **E-3**
- **Specific rule**: A4 — a BFF-identity confidential client MUST use MI-FIC or a Key Vault
  certificate, **never** a client secret. E-3 enumerates transitional sites and explicitly *"does not
  license expansion."* E-1 exempts *per-customer owning apps*.
- **Conflict**: The only registration that can perform this OBO exchange is the **BFF**
  (`SDAP-BFF-SPE-API`, `1e40baad-…`) — it is the assertion's audience and already holds delegated
  `FileStorageContainerType.Manage.All`. But routing the exchange there makes the BFF a confidential
  OBO client, which is A4 territory and a *new* site under E-3. Meanwhile E-1's "owning app" exemption
  does not apply, because no such app exists here.
- **Also in tension**: `spaarke-auth-v4-dataverse-MI` `design.md:149` scopes `SpeAdminTokenProvider` /
  `SpeAdminGraphService` **out** of its MI-FIC migration *on the express grounds that they authenticate
  per-customer owning applications*. That rationale is now falsified, so auth-v4's scope boundary is
  also affected and this cannot be settled inside this project alone.

### Options (a human chooses — I am not choosing)

| Path | Shape | Cost / consequence |
|---|---|---|
| **A — exception** | BFF performs the OBO using the **existing MI-FIC / KV-certificate** credential (A4-compliant, no new client secret). Document a project-scoped exception; the "owning app" framing is retired for SpeAdmin. | Most likely to work — the grant is already in place. Needs auth-v4 to re-scope `SpeAdminTokenProvider` back **in**. Requires their agreement. |
| **B — amendment** | Amend ADR-028 so E-1 reflects reality: SpeAdmin authenticates via the BFF identity, not a per-customer owning app. | Honest and durable; ADR change is the deliverable and must land before or with the code. |
| **C — provision a real owning app** | Stand up a genuine per-customer owning app registration with an `identifierUri` + exposed scope, and have the code page acquire a second token audienced to it. | Preserves E-1 as written. But it is an **operator + client-contract change** (trigger 2), adds a second interactive token to every session, and needs a new registration per customer. |

**Recommendation: path A**, on the evidence — the grant already exists, it needs no new client secret
if the existing MI-FIC/KV-certificate credential is used, and it removes a fiction from the design.
**But A4/E-3 and auth-v4's scope boundary make this explicitly a human decision, not mine.**

## What is NOT blocked

Workstreams A, C (the parts not gated on 011), D, E and F continue. Specifically:

- **Search is fine.** Task 004 proved its failure was a wrong Graph entity type and fixed it. It never
  depended on this. **Do not let 011 inherit Search.**
- Tasks **040 ✅** and **004 ✅** are complete and unaffected.
- Task **013** (`SecurityEvents.Read.All` grant) is Azure config and does not depend on 010's verdict.
- Tasks **060 / 061 / 062** are independent.

## Decision needed

Choose **A**, **B**, or **C** above. Task **011** cannot start until then, and must not silently adopt
the BFF fallback — that is precisely the move escalation trigger 1 was written to prevent.
