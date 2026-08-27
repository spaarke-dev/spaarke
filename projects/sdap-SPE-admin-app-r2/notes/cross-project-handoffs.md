# Cross-project handoffs — `sdap-SPE-admin-app-r2`

> Log of requirements/capabilities this project raised on OTHER active projects, per the repo's
> `project-defer-issue-tracking` convention (two-write rule: a substantive note in the receiving project's
> `notes/` folder + a tracked GitHub Issue for visibility). This file is the local pointer + link for each
> handoff — the substance lives in the receiving project's notes.

---

## ISS-001 — SPE billing-profile attach → `customer-provisioning-orchestration-r1`

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | next-round (not blocking the receiving project's current PR #779; blocking *this* project's close per spec success criterion 13) |
| **Filed** | 2026-08-26 |
| **Source** | spec.md FR-X02 / design.md §4.2d — billing-profile attach explicitly scoped out of SPE Admin |
| **GitHub Issue** | [#831](https://github.com/spaarke-dev/spaarke/issues/831) |
| **Requirement doc** | [`../../customer-provisioning-orchestration-r1/notes/spe-billing-attach-requirement.md`](../../customer-provisioning-orchestration-r1/notes/spe-billing-attach-requirement.md) |

**What**: `Add-SPOContainerTypeBilling` + `New-SPOContainerType` — the PowerShell cmdlets that attach a
billing profile to (and create) an SPE container type — have no home. SPE Admin's design.md §4.2d
evaluated and rejected running them from the BFF (no supported PowerShell host on Linux .NET 10, no
documented Graph/REST equivalent) and from a PCF (browser JS cannot run PowerShell or hold Azure
subscription owner credentials). `customer-provisioning-orchestration-r1` already owns repeatable
per-customer setup and is already PowerShell-based (`Provision-Customer.ps1` + Bicep), so the cmdlets drop
straight into that existing pipeline — no new tooling proposed.

**Why it matters**: without an owner, a new customer's `standard`-classification container type can be
created with no billing profile attached, `billingStatus` reports `invalid`, and — as of task 029 (shipped
2026-08-24) — SPE Admin now surfaces a classification-aware warning for exactly that state, but there is no
supported remediation path anywhere in either project. An operator would have to reach for the raw
cmdlets by hand, untracked, every time.

**The boundary, stated so neither project builds it twice**:

- **SPE Admin reads.** `billingClassification` + `billingStatus` off the container type (Graph v1.0,
  `FileStorageContainerType.Manage.All`), with a classification-aware warning when `billingStatus` is not
  `valid`. Read-only — this project never writes billing state. Shipped in task 029; see
  [`task-029-findings.md`](task-029-findings.md).
- **Provisioning writes.** `customer-provisioning-orchestration-r1` would be the sole writer, via
  `New-SPOContainerType` + `Add-SPOContainerTypeBilling`, once it picks this requirement up.

**What was filed** (full detail in the requirement doc linked above): the exact cmdlets, the required
privilege set (SharePoint Embedded Administrator role **plus** Azure subscription Owner/Contributor — a
combination no existing Spaarke identity currently holds), the `SubscriptionNotRegistered` retry caveat
for the `Microsoft.Syntex` resource provider (wait-and-retry, not terminal), and that the billing choice is
**one-shot / irreversible** (no re-run, no change-billing-owner operation once created).

**Not implemented here or there.** This is a requirement handoff only — no billing-attach code was written
in either project as part of filing it. `customer-provisioning-orchestration-r1`'s code, tasks, and TASK-
INDEX were not touched; only its `notes/` folder received the new requirement doc.

**Satisfies**: spec.md FR-X02, success criterion 13 (task 062).

---

## Notes

- ID scheme follows `.claude/skills/project-defer-issue-tracking/SKILL.md`: `ISS-{NNN}` for capability/work
  raised on a sister project (as opposed to `DEF-{NNN}` for scope simply dropped with no destination).
- When `customer-provisioning-orchestration-r1` picks this up (or explicitly declines it), update the
  Status row here and on GitHub Issue #831, per the skill's status-lifecycle table.
