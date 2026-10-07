# Task 061 — Step 1 inventory: what secure provisioning actually does today

> Written 2026-09-08 (session 4) BEFORE any code change, per the POML's step-1 ordering.
> **Read this before trusting the POML's `<background>` — that text is stale.**

## 1. 🔴 The POML's premise is out of date in two places

| POML says | Reality (verified in `ProvisionProjectEndpoint.cs`, as merged) |
|---|---|
| "ProvisionProjectEndpoint today creates each secure BU as a child of root — the exact anti-pattern §5.2 kills" | **Already fixed by task 021 (2026-08-25).** No BU is created anywhere. The endpoint resolves the ONE canonical BU **by name** from `SecureProject:BusinessUnitName` (default `Secure Project`, **singular** — verified against live metadata) and fails closed if it is missing or ambiguous. |
| goal: "assigns ownership to the configured **service account**" | **design.md §5.1a explicitly reversed this on 2026-08-25**: *"Ownership: an owner TEAM, not a service account."* The endpoint assigns the BU's **default owner team**. A service account would cost a licence, a credential to rotate and an identity to audit; a team costs none. **The POML predates that decision** — I am NOT re-litigating shipped, design-sanctioned work to match stale POML prose. |

**So neither `SecureProjects:ServiceAccountId` nor `SecureProjects:BusinessUnitId` should be added.**
The first is a mechanism the design rejected; the second is a GUID key the design rejected in favour
of a name (`SecureProject:BusinessUnitName`), because the GUID differs per environment.

## 2. What the endpoint does today (every side effect)

1. Validates `ProjectId`.
2. Reads `sprk_project`; 404 if absent, 400 if `sprk_issecure != true`.
3. Resolves the canonical Secure Project BU **by name** — fail closed on not-found / ambiguous.
4. Resolves that BU's **default owner team** (`teamtype = 0`) — fail closed on not-found / ambiguous.
5. 409 if already provisioned.
6. **Assigns `ownerid` to the owner team, and reads it back to verify** (does not trust the write).
7. Creates the project's SPE container via `SpeFileStore`.
8. PATCHes `sprk_containerid` — and **fails** if that write does not land (ADR-003).

Authorization: the whole `external-access` group carries `AddDelegationRuleFilter()` (task 008 /
FR-07), so the caller must hold **Write on the target record**, evaluated **as the caller** over OBO.
It fails closed when no caller token is present.

## 3. ⛳ The actual gap — and it is exactly the "locked box"

**Provisioning issues NO shares.** The response DTO says it plainly: *"no human holds access through
that ownership."* Combined with design §5.1 — *"All human access is by explicit Dataverse share,
including the creating attorney's"* — a provisioned secure project is owned by a memberless team and
shared with nobody. **That is the whole of task 061's remaining scope**, plus the reverse path.

## 4. Decisions this forces (stated, per the POML's "state the route decision")

| # | Decision | Rationale |
|---|---|---|
| D1 | **Do not touch ownership.** Owner stays the BU default owner team. | design §5.1a. The POML's service-account wording predates it. |
| D2 | **Config keys stay as shipped** (`SecureProject:BusinessUnitName`). No new BU-id / service-account keys. | Adding them would contradict §5.1 and §5.1a and create dead config. |
| D3 | **Share to the creator + optional named principals, per-user, through the 060 seam.** | Acceptance criterion 1 says exactly this. §5.1b prefers *access teams* but states **both satisfy "explicit Dataverse share"** — see D4. |
| D4 | **Access teams (§5.1b) are a follow-up, not this task.** | Native Dataverse access teams need an **entity team template** configured on `sprk_project` in the environment. That is environment setup — the same class of work the spec already reclassified to UAT (§ UAT and Environment Setup), and the POML explicitly says to fail closed on unconfigured environment rather than create environment objects. Because the seam takes a `DataversePrincipalRef`, switching to a team principal later is a *data* change at one call site, not a rewrite. |
| D5 | **Reverse path: owner returns to the caller by default**, with an optional `SecureProject:UnsecureOwnerUserId` override. | "Reassign per config" with a sane default. The caller already had to prove **Write** on the record to reach the route, so they are a legitimate owner; assigning to them moves the record out of the Secure Project BU deterministically, with no new required config. |
| D6 | **Caller identity comes from the existing `CallerRecordAccessProbe`**, extended to expose the systemuser id it already resolves via `WhoAmI()` on the OBO token. | CLAUDE.md §11 — extend, do not add a second identity path. `WhoAmI` under OBO cannot name anyone but the caller, so the creator's share cannot be aimed at someone else. |

## 5. Not in scope, deliberately

- **The §5.2 role-depth remediation.** design §5.1a-2 proves a child BU does **not** isolate from a role
  with `Deep` depth from root, so until users move out of root, a share is not the *only* way in. That
  is a BU restructure = environment work, already out of scope by owner decision (2026-08-21). Code
  here must be correct regardless, and the live-dev assertions belong in UAT acceptance.
- **Migrating secure projects provisioned under the old per-project-BU model** — the POML's escalation
  trigger. Inventory + escalate, never migrate here.
