# Requirement handoff → `customer-provisioning-orchestration-r1`: SPE billing-profile attach

> **From**: `sdap-SPE-admin-app-r2` (spec FR-X02 / design.md §4.2d) · **Filed**: 2026-08-26
> **Status**: Open — requirement handoff, no implementation attached
> **Type**: New scope proposal against an ACTIVE project (not a change request against shipped code)
> **Tracking**: GitHub Issue [#831](https://github.com/spaarke-dev/spaarke/issues/831) — `[ISS-001] SPE
>   billing-profile attach — needs an owner in customer-provisioning-orchestration-r1` (filed under the
>   repo's `project-defer-issue-tracking` convention; labels `issue`, `next-round`,
>   `sdap-SPE-admin-app-r2`, `customer-provisioning-orchestration-r1`)
> **Owner action needed**: triage into a task when convenient. This is not a block on your current PR
>   (#779) or wave — R2 is required to raise it before *our* project closes, not to force it into yours.

---

## 0. Read this first

**Please run your own independent verification before acting on anything below.** This was written from
`sdap-SPE-admin-app-r2`'s spec/design and from Microsoft Learn container-type docs, not from your branch.
Where this note asserts something about `Provision-Customer.ps1` / the Bicep modules, treat it as a
question ("does this still hold on your branch?"), not a finding.

**Why this is landing now, not earlier**: SPE Admin's design work (`design.md` §4.2d) evaluated whether
billing-profile attach could live in the BFF, in a PCF, or in the admin app itself, and rejected all three
(§2 below). The only place left is provisioning tooling — this project. R2's own spec (FR-X02) requires
this handoff to exist before R2 can close (its wrap-up task checks for it), which is why the timing looks
urgent from our side even though nothing about the *content* is time-sensitive for you.

---

## 1. What this is

Every SharePoint Embedded container type needs a billing profile attached before it can be used in
production. Two PowerShell cmdlets do this, and neither has a Graph or REST equivalent:

```powershell
New-SPOContainerType -ContainerTypeName <name> -OwningApplicationId <appId>
Add-SPOContainerTypeBilling -ContainerTypeId <id> -AzureSubscriptionId <sub> -ResourceGroup <rg> -Region <region>
```

- `New-SPOContainerType` creates the container type registration itself (the SharePoint-admin-center
  equivalent of the Graph `POST /storage/fileStorage/containerTypes` call, but through the SPO admin
  surface rather than Graph).
- `Add-SPOContainerTypeBilling` attaches an Azure subscription as the billing owner for that container
  type — this is the step that has no Graph/REST equivalent at all today.

### Privilege set required

Both cmdlets require **both** of the following simultaneously — a combination no existing Spaarke identity
currently holds:

1. **SharePoint Embedded Administrator** role (Entra admin role, tenant-scoped)
2. **Owner or Contributor on the target Azure subscription** (Azure RBAC, subscription-scoped)

This is a materially higher and differently-shaped privilege set than day-to-day SPE administration (which
needs only `FileStorageContainerType.Manage.All` delegated/app-only Graph permission — see §4 for what SPE
Admin already runs under).

### The `SubscriptionNotRegistered` retry caveat

The `Microsoft.Syntex` Azure resource provider must be registered against the target subscription before
`Add-SPOContainerTypeBilling` will succeed. Registration is not always already in place for a fresh
subscription, and propagation is not instant — the cmdlet can fail with `SubscriptionNotRegistered` on a
subscription where registration was *just* triggered. **This is a wait-and-retry condition, not a
permanent failure.** Any automation wrapping this cmdlet needs a retry-with-backoff around this specific
error rather than treating it as terminal. If `Microsoft.Syntex` is not yet registered for a subscription,
register it first (`Register-AzResourceProvider -ProviderNamespace Microsoft.Syntex` or the Azure Portal
equivalent) and expect a propagation delay before the cmdlet succeeds.

### One-shot / irreversible

**The billing method cannot be changed after creation.** Once a container type is created with an owning
application and a billing profile is attached, that binding is permanent — there is no re-run, no
"change billing owner" operation, no rollback path documented anywhere in the container-type surface.
This means:

- Any automation around this MUST get the inputs right before calling `Add-SPOContainerTypeBilling` —
  there is no cheap retry-with-different-parameters if the wrong subscription/resource group/region is
  supplied.
- A dry-run / confirmation step ahead of the actual call is worth the friction, given the blast radius of
  getting it wrong (a container type is capped at 25 per tenant and cannot be un-created — see design.md
  §4.2d for the source).
- Whatever validation exists elsewhere in `Provision-Customer.ps1` for other one-shot per-customer
  operations (app registration creation, subscription assignment, etc.) is the right pattern to reuse here,
  not a new one.

---

## 2. Why this belongs in provisioning, not in SPE Admin

SPE Admin's design.md §4.2d evaluated three possible homes and rejected two:

| Surface | Verdict | Why |
|---|---|---|
| **C# in the BFF** | Rejected | `Microsoft.Online.SharePoint.PowerShell` is a .NET Framework module. The BFF runs on Linux-hosted .NET 10 — there is no Windows PowerShell host to shell out to, and there is no documented, supported REST/Graph equivalent to reverse-engineer against instead. |
| **PCF (browser)** | Rejected | Browser JavaScript cannot run PowerShell, and must never hold Azure subscription owner/contributor credentials client-side. |
| **Provisioning tooling** | ✅ Correct home | Already PowerShell-based, already runs with elevated per-customer setup credentials, already owns the "things that happen once per customer" category of work. |

Concretely: **this project already owns repeatable per-customer setup and is already PowerShell**
(`scripts/Provision-Customer.ps1` + the `infrastructure/bicep/*.bicep` modules per this project's
`CLAUDE.md`). The billing-attach cmdlets are two more PowerShell calls that need the same category of
credential (elevated, per-customer, one-shot) that this pipeline already handles for app registration,
Key Vault provisioning, and Dataverse solution import. They drop into the existing orchestration shape —
this is not a new automation surface, it is two more steps in one you already run.

Per root CLAUDE.md §11 (Component Justification): the existing component is `Provision-Customer.ps1`; the
extension is adding these two cmdlet calls at the appropriate point in that script (after container-type
creation, likely adjacent to whatever step today creates or references the SPE container type / owning
app); the cost of doing nothing is stated in §5 below.

---

## 3. The read/write boundary — state this explicitly so neither project builds it twice

**SPE Admin reads. Provisioning writes.** This line needs to be visible in both projects so the capability
does not fall between them (built twice, or skipped by both assuming the other has it):

| | SPE Admin (`sdap-SPE-admin-app-r2`) | Provisioning (`customer-provisioning-orchestration-r1`) |
|---|---|---|
| **Role** | Reads `billingClassification` + `billingStatus` off the container type; warns when `billingStatus` is not `valid` | Runs `New-SPOContainerType` + `Add-SPOContainerTypeBilling` — the only place these are invoked |
| **Mechanism** | Graph `GET` (`fileStorageContainerType.billingClassification` / `.billingStatus`, both v1.0) | PowerShell (`Microsoft.Online.SharePoint.PowerShell` module) |
| **Privilege** | `FileStorageContainerType.Manage.All` (Graph, delegated/app-only) | SharePoint Embedded Administrator **+** Azure subscription Owner/Contributor |
| **Write access to billing** | **None — never writes billing state** | Sole writer |
| **What happens on invalid billing today** | Surfaces a classification-aware warning (see §4) — visibility only, no remediation action in-app | Would be the thing an operator runs to *fix* an invalid/missing billing attach, if this requirement is picked up |

If `customer-provisioning-orchestration-r1` ever needs to know whether a customer's billing is currently
valid (e.g., before running a fix), read it the same way SPE Admin does — `GET` the container type and
check `billingStatus`. Do not build a second read path; the Graph field is the single source of truth on
both sides.

---

## 4. What SPE Admin already ships on the read side (context, not a dependency)

R2 task 029 (2026-08-24) shipped the read side end-to-end. This is included so provisioning knows what a
successful `Add-SPOContainerTypeBilling` run should produce downstream, and so nobody re-derives it:

- `billingClassification` (`standard` / `trial` / `directToCustomer` / `unknownFutureValue`) and
  `billingStatus` (`valid` / `invalid` / `unknownFutureValue`) are both read from Graph v1.0
  (`fileStorageContainerType`), normalized, and rendered.
- The warning is **classification-aware**, sourced from Microsoft Learn's container-type documentation:
  - `standard` — the developer-tenant admin must establish a valid billing profile. This is the case
    `Add-SPOContainerTypeBilling` is for.
  - `directToCustomer` — billed to the consuming tenant; the developer tenant does **not** need a billing
    profile. A warning here would send an operator to do something that doesn't apply.
  - `trial` — not linked to any Azure billing profile at all.
- Any value SPE Admin cannot recognize (including future enum members) renders as an explicit "Unknown"
  rather than being coerced to "valid" — the app never asserts billing is fine when it does not know.
- Full detail: `sdap-SPE-admin-app-r2/notes/task-029-findings.md`.

**Practically**: once provisioning runs `Add-SPOContainerTypeBilling` for a new customer's `standard`
container type, SPE Admin's existing warning should clear on its own next read — no code change needed on
the SPE Admin side to observe the result.

---

## 5. Cost of doing nothing

If this requirement is not picked up anywhere, billing-profile attach has no home: SPE Admin explicitly
excluded it from its own scope (design.md §4.2d), and without this note, provisioning has no record that
the capability was assigned to it. The concrete failure mode: a new customer's `standard`-classification
container type gets created with **no billing profile attached**, `billingStatus` reports `invalid`, SPE
Admin's warning fires for that admin, and there is no supported remediation path anywhere in either
project — an operator would have to know to reach for the raw PowerShell cmdlets by hand, outside any
tracked automation, every time.

---

## 6. Suggested integration point (hypothesis only — validate against your branch)

Somewhere in `scripts/Provision-Customer.ps1`'s per-customer sequence, after whatever step creates or
registers the SPE container type / owning application for that customer, add:

1. `New-SPOContainerType` (if container-type creation is not already happening via a different path —
   check whether this project already creates container types some other way, e.g. via Graph, before
   adding a second creation path)
2. `Add-SPOContainerTypeBilling`, wrapped in retry-with-backoff on `SubscriptionNotRegistered`
3. A confirmation/dry-run gate ahead of step 2, given §1's irreversibility

This is a hypothesis for the implementer to validate, not a prescribed design — the actual per-customer
sequence and how container types currently get created (if at all) in this project's handler catalog
(H0–H14) may already answer some of this differently than assumed here.

**Estimated effort**: unknown — needs a spike to confirm current container-type creation state in this
project's handler catalog before scoping the actual PowerShell work.
**Blockers**: none identified from the SPE Admin side.
**Related**: `sdap-SPE-admin-app-r2` spec.md FR-X02, design.md §4.2d; `sdap-SPE-admin-app-r2` notes
`task-029-findings.md` (the read side); this project's `scripts/Provision-Customer.ps1` +
`infrastructure/bicep/*.bicep`.
