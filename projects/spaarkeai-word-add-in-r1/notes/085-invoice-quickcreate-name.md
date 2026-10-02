# Task 085: the invoice quick-create names the row with `sprk_name` (#1079, our half)

> **Date**: 2026-10-01 · **Rigor**: FULL (sonnet / high, run on Opus) · **Mode**: directional
> **Fix**: `04158652e` · **Issue**: [#1079](https://github.com/spaarke-dev/spaarke/issues/1079) (ISS-015)

## 1. The defect

- `OfficeService.QuickCreateAsync` names a new invoice (the Invoice branch, which stays on the minimal path) with
  `entity["sprk_invoicename"] = name`.
- `sprk_invoice` has no such column, so Dataverse refuses the create, and **every invoice quick-create from the
  pane fails**.
- It came in with #934 (`f5fee2141`). Nothing asserted the invoice's name attribute, so it survived.
- UAC-r2's task-130 verifier reported it; it was confirmed live here.

## 2. Live metadata (read first, as the POML requires)

`spaarkedev1`, 2026-10-01:

| Fact | Value |
|---|---|
| `EntityDefinitions(LogicalName='sprk_invoice').PrimaryNameAttribute` | **`sprk_name`** (escalation trigger not fired) |
| `sprk_invoicename` on `sprk_invoice` | **does not exist** |
| `sprk_billingevent.sprk_invoicename` | exists. References on that entity are correct and untouched |

**Re-checked during the review: nothing else blocks the create.** I listed every `sprk_invoice` attribute whose
`RequiredLevel` is not `None`:

| Attribute | Required level | Enforced on an API create? |
|---|---|---|
| `ownerid`, `owneridtype`, `sprk_invoiceid`, `statecode`, the `*name` / `*yominame` shadows | `SystemRequired` | `ownerid` is sent; the platform sets the rest |
| `sprk_name` | `ApplicationRequired` | sent (this fix) |
| `sprk_regardingrecordtype`, `sprk_extractionstatus` | `ApplicationRequired` | **no**. Only the form enforces these |

So the create carries `sprk_name` + `ownerid` (task 080's BU default Owner team) and is accepted.

**Proven against real Dataverse** (`spaarkedev1`, Web API, my admin token, the BFF's exact attribute set; push-to-github
Step 1.7). First I checked that nothing fires on an invoice create: no active plugin steps, webhooks or classic
workflows are registered on `sprk_invoice`.

| Probe | Owner | Result |
|---|---|---|
| **Control**: the pre-fix set, `sprk_invoicename` | root BU default team | **HTTP 400**: *"The property 'sprk_invoicename' does not exist on type 'Microsoft.Dynamics.CRM.sprk_invoice'"*. #1079 reproduced live |
| **Fix**: `sprk_name` | **Spaarke Business Unit 1** default team (the customer BU) | **HTTP 204**. Read back: `sprk_name` = the name, `ownerid` = the team. Deleted (204); 0 probe rows remain |
| **Fix**: `sprk_name` | **root** BU default team ("Spaarke") | **Refused** by Dataverse: *"Read Privilege Check For Owner failed … Principal team … privilegeCount=0 … missing prvReadsprk_Invoice"*. Nothing was created. **Not an 085 defect; see below** |

### ⚠️ Found by the probe: the root business unit's default team has NO roles (ISS-016)

Authoritative `RetrieveTeamPrivileges` over every default team in dev:

| Default team | Privileges | Read on invoice / matter / project / document | Enabled users in the BU |
|---|---|---|---|
| Spaarke Business Unit 1 | 725 | Deep / Deep / Deep / Deep | 1 |
| Spaarke Demo | 7,262 (System Administrator) | Global | 0 |
| Secure Record | 8 (Secure Record Owner, 082) | none / Basic / Basic / Basic | 0 |
| **Spaarke (root)** | **0** | **none** | **169 = 9 interactive people + 157 application users + 3 other** |
| Spaarke Dev 1 / Spaarke Test 1 | 0 | none | 0 |

- `RecordOwnershipResolver` (task 080, on master, **not yet deployed**) hands a root-BU caller the root default team
  when there is no target record. It also does so for a target that is itself root-owned.
- Dataverse refuses to own a row by a team with no Read on its table, and **the caller's own privileges do not matter
  (an admin is refused too; 082 found the same)**.
- So after the next BFF deploy from master, the **9 people in the root BU, the owner's own account among them**,
  hit a Dataverse fault on every Office create that lands on the root team: the unfiled save, the Matter / Project /
  Invoice quick-creates, and a To Do.
- The resolver propagates a Dataverse fault on purpose, so this surfaces as a 5xx, not `OFFICE_022`.
- 080's backfill dry run also plans to re-own 7 To Dos to the root team; those would be refused the same way.
- 085 neither causes this nor changes it. It is filed as ISS-016 for an owner decision.

## 3. The fix

```csharp
entity["sprk_name"] = name;   // was: entity["sprk_invoicename"] = name;
```

With a comment saying why, and that `sprk_billingevent` is the entity with `sprk_invoicename`. Matter and Project go
through `RecordCreationService` and are untouched.

## 4. Evidence

**Regression test** `tests/integration/regression/Issue1079_InvoiceQuickCreateNameTests.cs`:
- It sends `POST /api/office/quickcreate/invoice` through `OfficeQuickCreateTestWebAppFactory` (the
  `OfficeQuickCreateContractTests` harness), with the name `"  INV-2026-0042  "`.
- It asserts 201, `LogicalName == "sprk_invoice"`, `sprk_name == "INV-2026-0042"` (trimmed), and **no**
  `sprk_invoicename` attribute.

| Run | Result |
|---|---|
| **Before the fix** | **FAILED**: *"Expected … sprk_name … to be "INV-2026-0042" … but found &lt;null&gt;"* |
| After the fix | passed |
| `Issue1079` + `OfficeQuickCreate*` | **34 / 0 / 3**. The Matter and Project quick-create tests are unmodified and green; the 3 skips predate this task |

## 5. Gates

| Gate | Result |
|---|---|
| Build | 0 warnings / 0 errors |
| **Full BFF suite** | **13,065 passed / 0 failed / 54 skipped**: 084's 13,064 + **exactly** the 1 new test |
| ArchTests | **337 / 337** |
| **Publish size (§10)** | Three fresh short-path worktrees (removed afterwards); `Compress-Archive` Optimal, incl. PDBs; **212 files on every side**; no MSB3030. Fresh master `c08ef6013` **45.458 MB** (47,666,117 B) → 084 closed `4466c4664` 47,671,242 B → **085 `04158652e` 47,671,272 B**. **085's own contribution: +30 bytes.** Against master, 084 + 085 together: **+5,155 bytes** (084 measured +5,043 in its own run; the ~80-byte difference is zip noise) |
| CVE / packages | No `.csproj`, `.props` or `package*.json` change |
| **Code review** | **0 critical, 0 warnings.** The Invoice branch writes only `sprk_name` and `ownerid`, both valid (§2). Office invoice **search** already uses `sprk_name` (`OfficeSearchService.cs:294`) and selects `sprk_description`, which exists, so search is unaffected. Two low findings, not fixed here (out of scope, nothing fails): (1) a pane-created invoice has no `sprk_regardingrecordtype`, which the invoice FORM marks required, so the form will ask for it on the next edit there; (2) the comment "no description field is verified to exist on sprk_invoice" is now known to be stale (`sprk_description` exists). The quick-create still sends only the name, because this task's constraint limits it to the name attribute |
| **ADR check** | **0 violations.** ADR-019: no new error code. ADR-038: the regression test is in `tests/integration/regression/`, drives the real host, and goes red without the fix. ADR-034 / task 080: ownership is unchanged. ADR-010: no registration. BFF §10/§11: no new surface; a one-attribute edit |

## 6. Not done here, stated

- **Live check through the pane:** no deploy has happened (the owner defers deploys; `spaarke-bff-dev` still runs
  pre-#1045 code), so the criterion stays open. The Dataverse half is already proven directly (§2). After the next
  BFF deploy from master, quick-create an invoice from the pane **as Test User 1**, the only person in a child BU, and
  read the row back. A root-BU account will be refused because of ISS-016 until the owner decides it.
- **The other owners' sites** are unchanged and routed in #1079: `Services/RecordMatching/DataverseIndexSyncService.cs:52-55`,
  `scripts/ai-search/Sync-RecordsToIndex.ps1:66,69` (which also selects the non-existent `sprk_invoicedescription`),
  and `scripts/backfill-multi-container-multi-index/Backfill-MultiContainerMultiIndex-ParentRecords.ps1:174`.
- **Prior evidence found:** `projects/spaarke-ai-azure-setup-dev-r1/notes/phase-5-ingestion-evidence.md` recorded
  *"0 invoices"* indexed for this exact reason and filed it as backlog. So invoices have likely never been in the
  records index. Added to #1079 for the index owners.
