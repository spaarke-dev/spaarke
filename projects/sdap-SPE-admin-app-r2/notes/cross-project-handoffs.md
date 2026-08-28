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

## ISS-002 — ArchTest findings in ControlPlane code → `customer-provisioning-orchestration-r1`

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | **Security-adjacent, not blocking.** None of these are in Tier-1's blocking ArchTest subset; they run in Tier 2 / `adr-audit.yml`. Nothing in either project is stalled on them |
| **Filed** | 2026-08-27 |
| **Source** | task 042 (ArchTest suite brought to a known state) + task 050 (re-verified unchanged) |
| **GitHub Issue** | [#839](https://github.com/spaarke-dev/spaarke/issues/839) |
| **Findings doc** | [`../../customer-provisioning-orchestration-r1/notes/archtest-findings-from-spe-admin-r2.md`](../../customer-provisioning-orchestration-r1/notes/archtest-findings-from-spe-admin-r2.md) |

**What**: `tests/Spaarke.ArchTests` went 102/108 → **106/111** during task 042. The 5 that remain red are
all in `customer-provisioning-orchestration-r1`'s code. The headline is that
`CosmosProvisioningSecretGuardTests` (FR-27) — a **CATASTROPHIC**-severity invariant (no cleartext secret
in Cosmos, a queryable audit log) — **was dead rather than passing**: its loader pointed at a directory
that ceased to exist at the L2 split, and it failed under the DisplayName *"types have no string-typed
secret-shape properties"*, so CI read as though the rule had an opinion. Repaired; it now reports **8
secret-shaped properties**, of which `SolutionVerificationRequest.ClientSecret` (KV-resolved, builds a
`ClientSecretCredential`) is the one to adjudicate first. Also: 4 `ClientSecretCredential` sites missing
from the FR-F1/F2 census, one real second `ServiceBusClient` construction site
(`ServiceBusModule.cs:144`), and the ADR-010 1:1-interface ceiling drifting 153 → 155.

**Why it matters**: a security detector that reports a *wrong* diagnosis is worse than one that is
absent, because it consumes the attention that would otherwise notice the gap. This one had been dark
since the split.

**Deliberately NOT forced green.** Refining the FR-27 shape rule to silence the five probable
name-vs-value false positives would re-dark the guard just repaired, based on our inference about
another team's data model. FR-F2's own failure message says *"A failure here is NOT a prompt to update
the number."* Both decisions are the receiving project's to make — they know which properties hold names.

**What we DID change in shared test code** (already merged, commit `1b1d03b23`): the FR-27 loader now
enumerates every `Sprk.Provisioning.ControlPlane*` assembly (its scan comment always said `*`; only the
loader was singular), and `ServiceBusClientGuardTests` now skips `*.Tests` projects — a **scope
narrowing, not an allowlist**; every file under `src/server/**` including all ControlPlane service
projects is still scanned, and no named production site is exempt.

**Not fixed here or there.** Findings handoff only. No ControlPlane production code was touched.

---

## DEF-001 — dead owning-app code, still DI-registered and shipped (INTERNAL to this project)

| Field | Value |
|---|---|
| **Status** | Open — needs an owner decision, no destination project |
| **Urgency** | Not blocking. Decide by task 090 (wrap-up) |
| **Filed** | 2026-08-27 |
| **Source** | task 042 — [`test-retirement-inventory.md`](test-retirement-inventory.md) |

**What**: three methods on `SpeAdminGraphService` have **zero callers** anywhere in `src/`
(grep-verified):

| Method | Note |
|---|---|
| `GetClientForOwningAppAsync` | no callers |
| `ValidateOwningAppSecretsAsync` | no callers — **despite a doc comment claiming it is "called during startup validation"** |
| `FetchOwningAppSecretAsync` | no callers |

Task 042 found **34 tests green against this code**, which is task 010's UNWORKABLE verdict surfacing at
the test layer: the SPE-084 multi-app OBO path has never executed successfully (see project CLAUDE.md
"Reopen condition — task 010"). The tests were retired. **The code was not** — it is still registered in
DI and ships in every deploy.

**Why it is filed rather than fixed**: deletion is a CLAUDE.md §11 judgement (is this dead, or is it
scaffolding for a Workstream B path that 011/012 will complete?), and it was out of task 042's and task
050's scope. `ValidateOwningAppSecretsAsync`'s misleading doc comment is itself a small instance of §2.4
— a comment asserting a call relationship that does not exist.

**Decision needed**: delete under §11, or keep with the doc comments corrected to say the methods are
currently unreachable and why.

---

## Notes

- ID scheme follows `.claude/skills/project-defer-issue-tracking/SKILL.md`: `ISS-{NNN}` for capability/work
  raised on a sister project (as opposed to `DEF-{NNN}` for scope simply dropped with no destination).
- When `customer-provisioning-orchestration-r1` picks this up (or explicitly declines it), update the
  Status row here and on GitHub Issue #831, per the skill's status-lifecycle table.
