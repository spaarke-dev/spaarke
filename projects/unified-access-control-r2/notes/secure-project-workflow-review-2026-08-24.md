# Secure Project workflow review — 2026-08-24

> Owner-requested ("we need to review the full secure project workflow to ensure we are supporting
> what we defined in the design"), triggered by the task-021 findings.
> Scope: end-to-end, wizard → BFF → Dataverse, compared against [`design.md` §5](../design.md) and
> spec FR-18 / FR-28 / NFR-05.

---

## Headline

**`sprk_issecure = true` currently confers no isolation on either surface.** It gates provisioning
and is displayed in the SPA. None of the three mechanisms design §5.1 specifies exist in code, and
the infrastructure provisioning *does* build is not wired into the security model at all.

Separately: **secure-project provisioning is very likely failing outright today** on a field
collision between the wizard and an idempotency guard added 2026-08-23 — see §4. That one is mine.

---

## 1. What design §5.1 requires

> - One **`Secure Projects` business unit**; secure records live there.
> - A **service account in that BU owns the records**, so they stay there naturally — no
>   matrix-data-access dependency, no BU-per-project proliferation.
> - **All human access is by explicit Dataverse share**, including the creating attorney's. Nobody
>   gets access by ownership; nobody by business unit.
> - For types 2 and 3, the §4.5 veto suppresses derived and org terms — explicit
>   `sprk_externalrecordaccess` grants only.

Plus **FR-28** (owned by a service account in `Secure Projects`; all human access by explicit share)
and **NFR-05** (standing assertion that no security role reaches the `Secure Projects` BU).

## 2. What the workflow actually does

| # | Step | Where |
|---|---|---|
| 1 | Create `sprk_project` with `sprk_issecure = true` | `CreateProjectWizard/projectService.ts:280-284` |
| 2 | Cascade `sprk_containerid` + `sprk_searchindexname` **from the creating user's BU** | same file, `applyUserBuDefaults` |
| 3 | `POST /api/v1/external-access/provision-project` | `provisioningService.ts` |
| 4 | Create a **new BU per project** under the **root** BU | `ProvisionProjectEndpoint` Step 2 |
| 5 | Create an SPE container | Step 3 |
| 6 | Create an OOB `account` owned by that BU | Step 4 |
| 7 | Stamp 3 references onto the project | Step 5 — **broken since 2026-03** |

The record is created by, and remains owned by, **the calling user**, in **the calling user's business
unit**. Nothing anywhere moves it.

## 3. Gap analysis — 3 of 3 core mechanisms absent

| Design requirement | Reality | Verified by |
|---|---|---|
| Secure records live in the `Secure Projects` BU | Project stays in the **creating user's BU**. **No code anywhere resolves or references a `Secure Projects` BU** | repo-wide grep: zero matches for a Secure-Projects BU lookup |
| A **service account** owns them | Owner is the **creating user** (default Dataverse ownership; the create payload sets no `ownerid`) | `projectService.ts` payload; no `ownerid` in `ProvisionProjectEndpoint` |
| **All human access by explicit share** | **No share is ever created.** `ProvisionProjectEndpoint` appears in none of the 14 files that perform POA/sharing | grep for `GrantAccess`/`PrincipalObjectAccess`/`AddUserToRecordTeam` |
| One `Secure Projects` BU, no per-project proliferation | Creates one BU per project, parented to **root** | `ProvisionProjectEndpoint` Step 2 |
| §4.5 Secure veto suppresses derived + org terms | Not implemented — Phase 1, task 037 | expected; not a defect |

**Consequences, stated plainly:**

- On the **MDA**, a secure project is an ordinary record owned by an ordinary user in an ordinary BU.
  Anyone whose role reaches that BU at Deep depth reads it. The design's isolation is entirely absent.
- The creating attorney has access **by ownership** — the one thing §5.1 explicitly forbids
  ("Nobody gets access by ownership").
- On the **SPA/Teams** plane, `sprk_issecure` is projected to the client for display
  (`ExternalDataService` `$select`) and drives no decision.
- **NFR-05's standing assertion would not protect the per-project BUs even if they were used**,
  because they are parented to root rather than under `Secure Projects`.
- The per-project BU is **inert**: nothing is owned by it, no record is moved into it, no role is
  scoped to it. It is created, stamped (or not), and never consulted.

## 4. 🚨 Live regression — provisioning likely 409s for every secure project

**This one is mine**, introduced 2026-08-23 as a task-008 follow-up.

The idempotency guard (`ProvisionProjectEndpoint` Step 1b) treats a project as already-provisioned when:

```csharp
(_sprk_securitybu_value is { } bu && bu != Guid.Empty)
|| !string.IsNullOrWhiteSpace(sprk_containerid);
```

But the wizard writes `sprk_containerid` **at creation time**, cascaded from the creating user's BU,
for every project including secure ones (`EntityCreationService.applyUserBuDefaults`, FR-WIZ-01..05).

So for any user whose BU carries a container id — and the cascade is live; a real
`sprk_project` row read today holds `sprk_containerid = "b!vzGDfDpd7km…"` — the sequence is:

1. Wizard creates the project **with `sprk_containerid` already populated** from the user's BU.
2. Wizard calls `/provision-project`.
3. The guard sees a non-empty `sprk_containerid` → **409 Conflict, "already provisioned"**.
4. No BU, no isolated container, no account. The secure project keeps pointing at the **shared BU
   container** that other users can reach.

Before the guard, provisioning ran and returned 200 having created orphaned infrastructure. After it,
provisioning refuses. The 409 is the more honest answer, but the net effect is that the workflow is
blocked rather than merely ineffective.

**Root cause of my error**: I chose `sprk_containerid` as a provisioning marker without checking who
else writes that field. That is the same mistake class as everything else in this review — assuming a
field's meaning instead of verifying its writers. `_sprk_securitybu_value` alone would have been a
sound marker; `sprk_containerid` is shared state.

## 5. Owner decisions taken 2026-08-24

| # | Decision |
|---|---|
| 1 | **Stop creating business units.** Use the single `Secure Projects` BU |
| 2 | **Stop creating accounts.** Firms are `sprk_organization`; use **`sprk_assignedlawfirm1`** (lookup → `sprk_organization`) |
| 3 | **Leave `sprk_externalaccount` alone.** The `account` table may serve other purposes, but it is not part of the secure-projects workflow |

## 6. Recommended work, in dependency order

### Immediate (re-scoped task 021)
1. Reduce the Step 5 payload to **`sprk_containerid` only** — a plain string, no navigation property.
   This also dissolves the `@odata.bind` blocker entirely.
2. **Delete Step 2 (BU create) and Step 4 (account create)**, plus their rollback paths.
3. Make the stamp **fail loudly** — non-2xx with a reason code and the container id that was created,
   so an operator can reconcile.
4. **Fix the idempotency marker** to `_sprk_securitybu_value` — or, once BUs are gone, to a dedicated
   marker that only provisioning writes. Do **not** key it on `sprk_containerid`.
5. Regression test: provisioning **never writes** `sprk_externalaccount`.
6. Fix `secure-project-fields-schema.md` (task 026 scope) — it is the source of the wrong names.

### Needs a task of its own — the actual security mechanism
None of design §5.1 is built. This is not part of 021 and should not be smuggled into it:

- Move / create secure projects **in the `Secure Projects` BU**
- **Service-account ownership** (needs the account provisioned + configured; FR-28)
- **Explicit share to the creating attorney** at creation (FR-28; the POA/teams plumbing already
  exists in `PlaybookSharingService` and `IDataverseAccessGrantService` — CLAUDE.md §11 says
  consolidate, do not write a third client)
- The wizard's BU cascade should **not** apply `sprk_containerid` to secure projects at all

### Already scheduled
- §4.5 Secure veto (suppress derived + org for types 2/3) — task **037**
- NFR-05 standing role-depth assertion — task **034** family
- BU restructure itself is **UAT/environment work**, per spec § UAT & Environment Setup

## 7. What this review did NOT cover

Closure (`ProjectClosureEndpoint`) was reviewed in tasks 016/017 and is not re-examined here. The
external grant/revoke lifecycle is covered by tasks 007/010/023. The MDA-side wizard UX and the
irreversibility of `sprk_issecure` were not assessed.
