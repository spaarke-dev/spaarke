# Task 144: secure records owned by a named, memberless owner team (C10 part 1, #967)

> **Date**: 2026-10-01 · **Branch**: `task/uac-r2-144` · **Rigor**: FULL (opus, xhigh)
> **Status**: code, tests, docs and migration script complete. The live cutover (POML step 9) is a **pending manual
> gate**: this run was read-only against Dataverse by instruction. Three items need an owner decision first (§3).

---

## 1. What changed

| Area | Change |
|---|---|
| Provisioning (`ProvisionProjectEndpoint`) | Owns the record by the Secure Record BU's **named, non-default** owner team (`SecureRecord:OwnerTeamName`, default `Secure Record Owners`), never the default team. Before any mutation it proves: the team resolves exactly once, has **zero members of any kind**, and **zero systemusers** sit in the BU. An unreadable answer to either refuses. Works for `sprk_project`, `sprk_matter` and `sprk_workassignment` (`recordType` + `recordId`, legacy `projectId` kept). |
| New refusals (all before any write) | `secure_owner_team_has_members`, `secure_owner_team_membership_unreadable`, `secure_bu_has_users`, `secure_bu_users_unreadable` (500, the endpoint's env-fault convention). A record still owned by another team inside the Secure BU (the retired default team) → 409 `owned_by_other_secure_team`, which points at the migration script instead of creating a second container. |
| Unsecure (`UnsecureProjectEndpoint`) | Same three root types, same shape. Response adds `recordType`/`recordId`; `projectId` is empty for a matter or work assignment. |
| Delegation filter | Provision and unsecure cases resolve the SAME root the handler re-owns (`SecureRecordRoot.ResolveTarget`, which delegates to `GrantExternalAccessEndpoint.ResolveGrantRoot`). An unknown `recordType` resolves to nothing → 403. |
| `RecordOwnershipResolver` | BU → team now has one exception: the Secure Record BU → its named team, or `null` (refuse). Never its default team. Ambiguous Secure BU name → refuse. No Secure BU in the environment → every BU keeps its default team. |
| Registration | `CreateSystemUserAsync` refuses a BU that resolves **by ID** to the target environment's Secure Record BU. `AddUserToTeamAsync` refuses any team in it (discovered vector: a configured team name could add a member to the named team). An unreadable or ambiguous Secure BU lookup refuses. Throws `SecureRecordPlacementRefusedException` before any POST. |
| NFR-05 assertion | Moved into the BFF (`Infrastructure/Dataverse/SecureBuRoleDepthAssertion.cs`) with a shared `SecureBuRoleDepthCensusBuilder`, so the manual gate and the job use one evaluator. Clause 2 targets the named team and fails on ANY member; new clause 2b (named team must resolve to exactly one); new clause 4 (no systemuser of any kind in the BU); clause 3 fails when the default team holds the owner role. `prvReadsprk_WorkAssignment` added to the guarded privileges (it was missing). |
| Census job (owner decision F2 = a) | `SecureRecordIsolationCensusJob`, read-only, every 15 minutes, CRITICAL log line per finding, heartbeat every attempt, throws on an unreadable census so the scheduler retries. Registered with `AddScheduledJob`. |
| Migration | `scripts/Migrate-SecureRecordsToNamedOwnerTeam.ps1`. Dry run by default; `-Apply` writes; `-Verify` is the live gate. Details in §5. |
| Client | `provisioningService.ts` classifies the five new codes. The four pre-mutation refusals map to `environment-not-configured`, and `owned_by_other_secure_team` maps to `already-provisioned`. Copy comments were updated; the user-facing copy did not change. |
| Docs | Setup guide §0/§1/§2, §4 (rewritten: named team plus the cutover order), §5.5, §7 (items 3, 3b, 5, 5b, 7, 9, 10, 11), §8 and §9. Write-path doc: I-2 row, I-6 row, owner row. `appsettings.template.json` gains the `SecureRecord` section. The e2e spec header and TC-070-01 now assert the named team. |

---

## 2. Step 1: live census (READ-ONLY, spaarkedev1, 2026-10-01)

All GET requests made with the operator's `az` token against the explicit URL. Scripts are under the session
scratchpad (`task144/census*.ps1`).

| Fact | Value |
|---|---|
| `organization.sharetopreviousowneronassign` | **false** |
| Secure Record BU | `d9ec0b6f…` (parent: root `Spaarke`), `sprk_containerid` null |
| Teams in the Secure BU | only the default team `Secure Record` (`daec0b6f…`), 0 members, holds `Secure Record Owner` |
| Team named `Secure Record Owners` | **does not exist yet** |
| Systemusers in the Secure BU | **0** (any kind) |
| `Secure Record Owner` holders | the default team only; 0 users |
| Read privilege names | `prvReadsprk_Project`, `prvReadsprk_Matter`, `prvReadsprk_WorkAssignment` |
| `sprk_issecure eq true` rows | **1 project, 0 matters, 0 work assignments** |
| That project | `65a3fab2…` "Test New Matter via Workspace", created by Ralph Schroeder (human), modified 2026-09-09. Owned by **`Spaarke Business Unit 1`'s default team (an ORDINARY BU)**, not the Secure BU. 0 POA shares. Has its OWN container (no BU or other root carries that id). No children, no documents, no grants. |
| Anything owned in the Secure BU | **nothing** (roots and 6 child tables checked) |
| `sprk_issecure` null | 9 projects, 18 matters, 11 work assignments (the 153 backfill item, owner-blocked) |
| Assign cascade from roots | project and matter → `team`, `sharepointdocumentlocation`, `sharepointdocument` (all `Cascade`, platform-managed); work assignment → none. 0 rows on those children for the secure project. |

⚠️ **The background's "1 secure project, provisioned, owned by the default team" is not true today.** It is owned by
an ordinary business unit's default team, so it is readable by business-unit depth there (not isolated). It was most
likely reassigned out during earlier testing. The migration script reports it as NOT ISOLATED and does not touch it.

---

## 3. Escalation triggers, evaluated read-only

| Trigger | Result |
|---|---|
| `sharetopreviousowneronassign` true | **Did not fire** (false). The script still checks it before any assign. |
| A user in the Secure BU, or default-team members | **Did not fire** (0 and 0). |
| Unprovisioned secure matter/WA with content | **Did not fire** (0 secure matters or work assignments). The one NOT-ISOLATED row is a project with no content. 🔔 It still needs an **owner decision**: (a) provision it via `/provision-project` (a new container; the share goes to the caller, who must be its creator), (b) unsecure it, or (c) delete the test record. Recommendation: **(c) or (b)**. It is a test record with no content, and today it is exposed to everyone in `Spaarke Business Unit 1`. |
| Assign privilege error other than Read | Not evaluable without a live assign (step 9). |
| Assign cascade = Cascade on a root relationship | **FIRED, literally**: 6 relationships (project/matter → `team.regardingobjectid`, `sharepointdocumentlocation.regardingobjectid`, `sharepointdocument.regardingobjectid`), all platform-managed (record access teams, legacy SharePoint integration). No custom child cascades Assign. There are 0 rows on those children for any secure root, and 0 rows to migrate in dev today. The migration part therefore STOPS until the owner accepts the list. Once accepted, the script takes `-AcceptedAssignCascade team,sharepointdocumentlocation,sharepointdocument`. Note that provisioning's own assign has always triggered the same cascade, so this is not new behaviour. |
| Named-team name (F9) | **Answered**: `Secure Record Owners`. |
| Detection between provisioning calls (F2) | **Answered (a)**: the census job was built. |
| **NEW: F11's premise is no longer true** | 🔔 **FIRED by the live NFR-05 census (§6).** Three root-BU humans reach the Secure BU at Deep depth on project, matter and work assignment: the hotmail `#EXT#` account (Spaarke Basic User), plus **Chelsea Friez** and **Lori Witkin** (`@demo.spaarke.com`, **created 2026-10-01** in root BU `Spaarke`, with Spaarke Core User and Spaarke Office Add In User). The census shows the reach is structural: new users land in the root BU with Deep-read roles. F11's own recommendation applies: bring **(a) remove the roles from these accounts** versus **(b) narrow the roles' depth** back to the owner before the live cutover's NFR-05 gate can pass. Do not relocate anyone. This run changed no role. |

---

## 4. Step 2: every secure/unsecure path, and every default-team reader

| Path | Before | Now |
|---|---|---|
| `ProvisionProjectEndpoint` | default team; projects only | named team plus invariants; 3 roots |
| `UnsecureProjectEndpoint` | projects only | 3 roots |
| `DelegationRuleFilter` provision/unsecure cases | `FromProjectId` | same resolver as the handler |
| `RecordOwnershipResolver` | Secure BU → default team | Secure BU → named team, or refuse |
| `RegistrationDataverseService` create user / add to team | unguarded | refuses the Secure BU and its teams by ID |
| `SecureBuRoleDepthAssertion(Tests)` | "owner team = default team of the secure BU"; human members only; no WA privilege; no BU-user clause | named team; any member; WA guarded; BU-user clause; default-team role holder is a finding |
| `ProvisionProjectTestFixture` | default team is the owner | default team is a never-chosen decoy; named team plus members plus BU users modelled |
| `ProvisionProjectIdempotencyTests` | 2 tests pinned the default team | rewritten (reason below) |
| `SecureProjectShareTests` | project only | adds matter and work-assignment unsecure |
| e2e `secure-project-creation.spec.ts` | default team in the header and TC-070-01 | named team asserted |
| `provisioningService.ts` / `SecureProjectSection.tsx` comments | "provisioning never asserts the team is empty" | updated (it now does, at provisioning time) |
| Setup guide §4/§7/§8, write-path doc I-2/I-6 | default team | named team |
| `ProjectClosureEndpoint` | checked: does not read ownership or the default team | unchanged |
| Office quick-create (`RecordCreationService`) | creates matters/projects via `RecordOwnershipResolver` (acting user's BU) | unchanged; it never sets `sprk_issecure` (secure is set on the form or wizard, then provisioned) |
| `scripts/Backfill-RecordOwnership.ps1` | word-add-in-r1's I-6 backfill | checked: it resolves BU default teams for non-secure rows. **Hand-off**: if it ever runs over rows in the Secure BU it would choose the default team. Its owner (word-add-in-r1) should route the Secure BU to the named team the way the resolver now does. Recorded here and not edited (another project's script). |

**Tests rewritten to the new contract (never deleted):** `ProvisionProject_AssignsTheProject_ToTheBusinessUnitsDefaultOwnerTeam` →
`…NamedOwnerTeam_NeverItsDefaultTeam` (the old contract WAS the defect); `ProvisionProject_WhenTheBusinessUnitHasNoDefaultOwnerTeam_FailsClosed`
→ `…WhenTheNamedOwnerTeamIsMissing_FailsClosedAndNeverUsesTheDefaultTeam`; `RecordOwnershipResolverTests…OwnsItInTheSecureUnit…` → named team;
`SecureBuRoleDepthAssertionTests…ReportsOwnerTeamHasHumanMembers` → `…HasAnyMember…NamingEach` (verdict renamed `OwnerTeamHasMembers`).

---

## 5. The migration script

`scripts/Migrate-SecureRecordsToNamedOwnerTeam.ps1 -EnvironmentUrl <url> [-Apply | -Verify] [-AcceptedAssignCascade …] [-ReportPath …]`

- **Categories**:
  - `MIGRATE`: the row is in the Secure BU, owned by another team. The script moves it.
  - `DONE`: already owned by the named team.
  - `NOT-ISOLATED`: a secure row outside the BU. Reported with its creator, its container (`own`, `SHARED` or `none`) and its content; never touched.
  - `ANOMALY`: a non-secure row inside the BU. Reported only.
- **Apply**: per row, it counts POA shares (`accessrightsmask > 0`), PATCHes the owner, reads the owner back, and counts again. It stops on a failed read-back or a changed count.
- **Idempotent**: a second run finds no `MIGRATE` rows.
- **Never** shares to anyone (F8: the operator is never shared to), never provisions, never moves users, never touches roles.
- **Dry run today (read-only)**: plan = **0 rows**; 1 NOT-ISOLATED (`65a3fab2`, own container, no content); 2 STOPs (the named team is missing; the Assign-cascade list is not accepted). Exit code 2. The `-Verify` run fails as expected: the named team is missing, the role sits on the default team, and the row is not isolated.

---

## 6. Live NFR-05 census through the new evaluator (READ-ONLY)

`SPAARKE_NFR05_DATAVERSE_URL=https://spaarkedev1.crm.dynamics.com`, `AZURE_TOKEN_CREDENTIALS=AzureCliCredential`, test
`SecureBusinessUnitRoleDepth_InTheTargetEnvironment`. Census: 6 BUs, 282 effective grants (33 human), 1 owner-role
holder, 0 named teams, 0 BU users. **Verdict: HumanPrincipalReachesSecureBusinessUnit, 17 findings.** Findings 1–15
are clause 1: Chelsea Friez (Core User + Office Add In User), Lori Witkin (the same two roles) and hotmail `#EXT#`
(Basic User), each at Deep on project, matter and work assignment, anchored at root. Finding 16 is
`SecureOwnerTeamNotResolved` (expected until step 9). Finding 17 is `SecureOwnerRoleHeldBeyondOwnerTeam`: the default
team holds the role (expected until step 9's role move). Clause 2 (members) and clause 4 (BU users) pass.

Once deployed, the census job will log those findings as CRITICAL every 15 minutes until they are resolved. That is
by design.

---

## 7. Owner decisions applied (round 3, binding)

- **F9**: the team name is `Secure Record Owners`. Used as the compiled default, the config key, the pinning test and guide §4.
- **F2**: the read-only `IScheduledJob` was built (§1). Placement: BFF, schedule (ADR-052). Registered with `AddScheduledJob` (ADR-036 A1-6).
- **F8**: the interim default ships. Provisioning shares only to the caller resolved from their own token via WhoAmI. This task adds **no resume path**: a record already owned by the team returns 409, so nothing is ever shared to "whoever calls resume". The migration script shares to nobody. A NOT-ISOLATED row is never provisioned by the script; provisioning it is an owner decision (§3). The "createdby for human-created rows, refuse app-created rows" rule for the resume path stays with task 133.
- **F11**: no census exception was added. The live census now shows the reach is structural (§3).

---

## 8. Placement decision and component justification (CLAUDE.md §10 / §11)

**Placement (bff-extensions.md):** every change lives in the BFF. It is BFF domain code on the existing external-access
and Dataverse write path, under BFF identity, with no AI dependency. There is **no new endpoint, package, interface or
plugin**. One new DI registration was added: the job, through `AddScheduledJob`.

| New surface | Existing (grep) | Extension? | Cost of doing nothing |
|---|---|---|---|
| `SecureRecordOwnerTeam` (static, `Infrastructure/Dataverse`) | Default-team lookup inline in `ProvisionProjectEndpoint`; `SecureRecord:` keys duplicated in the endpoint, unsecure endpoint and tests; no owner-team-name key (grep `SecureRecord:` found only 2 keys) | It extends the endpoint's lookup. It is pulled out because provisioning and the job must check the SAME invariants, and four consumers need the same two fail-closed keys. Static: no DI, no interface (ADR-010 ceiling). | Two copies of the invariant logic drift, and a Change-BU goes unseen between provisioning calls. |
| `SecureRecordRoot` (internal record, `Api/ExternalAccess`) | `ExternalGrantRoot` maps type → entity set and logical name | Yes: it wraps `ExternalGrantRoot` and adds only the id and name columns and the container label (the POML asks for "one table, not three copies"). | Three copies of per-entity selects and labels, or no matter/WA provisioning (uploads to secure matters 409 forever). |
| `SecureRecordIsolationCensusJob` + `AddScheduledJob` | `GrantExpiryReminderJob` and `ExternalAccessReconciliationJob` (other domains); NFR-05 ran only manually | No: no existing job reads role topology. It reuses the evaluator rather than adding a second one. | A user Change-BU'd into the Secure BU reads every secure record until the next provisioning call or manual run (F2). |
| `SecureBuRoleDepthAssertion` moved to src + `SecureBuRoleDepthCensusBuilder` | The evaluator lived in the test project | It is moved, not duplicated. The builder is extracted from the test's reader so both readers compose identically. | The job would need its own evaluator, and two evaluators drift. |
| Config `SecureRecord:OwnerTeamName` | `SecureRecord:BusinessUnitName` | Same section, a sibling key, with a compiled default. | The name could not be changed without a redeploy. |
| `SecureRecordPlacementRefusedException` | Registration throws `InvalidOperationException` for BU/team not found | It derives from `InvalidOperationException`, so existing catches still apply. | A security refusal reads like an outage in logs and tests. |
| `scripts/Migrate-SecureRecordsToNamedOwnerTeam.ps1` | `Backfill-RecordOwnership.ps1` (non-secure I-6 backfill); grep `Secure Record` over scripts/ found 0 migration scripts | No: that backfill resolves default teams by design. An endpoint for a one-time migration would be permanent surface for a one-shot need. | Rows provisioned before the change stay owned by the default team, and provisioning refuses them (409). |

**Publish size**: not measured in this run (the main session measures after merging). No package was added.

---

## 9. Tests

- New: `SecureNamedOwnerTeamProvisioningTests` (22 cases), `SecureRecordIsolationCensusJobTests` (7), `RegistrationSecureRecordPlacementTests` (8). Plus additions to `SecureBuRoleDepthAssertionTests`, `RecordOwnershipResolverTests`, `SecureProjectShareTests` and `ProvisionProjectIdempotencyTests`, and the jest `provisioningService.test.ts`.
- **Perturbation sweep: 17/17 seeded violations bite** (each run as backup → seed → build → run → restore from a byte copy):

| # | Seeded violation | Result |
|---|---|---|
| P1 | Named-team filter drops `isdefault eq false` | BITES |
| P2 | Named-team filter drops `teamtype` | BITES |
| P3 | Team members ignored | BITES |
| P4 | Unreadable membership read as zero | BITES |
| P5 | BU-user read excludes disabled users | BITES |
| P6 | Filter authorizes the legacy `projectId` | BITES |
| P7 | Retired-default-team owner not refused | BITES |
| P8 | Resolver gives the Secure BU its default team | BITES |
| P9 | Resolver drops `isdefault=false` | BITES |
| P10 | Registration guard bypassed | BITES |
| P11 | Ambiguous Secure BU allowed | BITES |
| P12 | Clause 4 dropped | BITES |
| P13 | Builder counts only human members | BITES |
| P14 | Builder treats the default team as the owner | BITES |
| P15 | Job swallows a read failure | BITES |
| P16 | Job reads only page 1 | BITES (re-seeded, because the first seed did not compile) |
| P17 | Unsecure handler re-owns the legacy id | BITES. It did NOT bite at first, which exposed a real test gap: the unsecure same-root test never sent a conflicting `projectId`. Fixed, then it bit. |

- 🔴 **Hazard in the perturbation tooling, found and fixed during this task.** Restoring a seeded file with
  `Copy-Item` keeps the BACKUP's older timestamp, so MSBuild's incremental build skips the recompile. The binary
  then still contains the last seeded violation. The first full-suite run executed against the P16-seeded job and
  failed `Run_WhenTheOffendingUserIsOnTheSecondPage` for that reason, not because of the code. The verdicts stay
  valid, because each seed was written with a fresh timestamp. The FINAL binary did not. Fix: bump
  `LastWriteTime` after every restore (or build with `--no-incremental`), and re-run the suite.
- **A real failure the full suite caught**: `ExternalAccessReconciliationTests.S2_TheJobShipsDisabled…` enumerates every
  `ScheduledJobRegistration` from a minimal container, which constructs each job. The census job injected
  `IGenericEntityService` directly, so it could not be constructed there. It now resolves the seam per run through
  `IServiceScopeFactory`, as the sibling jobs do (`GrantExpiryReminderJob`, `ExternalAccessReconciliationJob`).
  P16 was re-proved to bite after that change.
- jest: `provisioningService.test.ts` and `SecureProjectSection.test.tsx`, 38/38 passing.
- **Final runs on the committed code**:

  | Suite | Passed | Failed | Skipped | Total |
  |---|---|---|---|---|
  | `tests/unit/Sprk.Bff.Api.Tests` | 13,243 | 0 | 54 | 13,297 |
  | `tests/Spaarke.ArchTests` | 337 | 0 | 0 | 337 |
  | Affected classes (incl. `ExternalAccessReconciliationTests`, `GrantExpiryReminderJobTests`, `DelegationRule*`) | 252 | 0 | 0 | 252 |
- **Beyond the stated contract** (one line each):
  - The census job tests: owner decision F2 added the job.
  - The registration team-membership guard and its tests: a discovered membership vector into the named team.
  - The jest classifier cases: the endpoint's reason-code set changed, and the existing test requires the client to classify every code.
- ADR-038 B1: `RegistrationSecureRecordPlacementTests` uses a hand-written scripted `HttpMessageHandler`. This is a §6.5 **path-A exception** following the `DataverseRecordShareWireTests` precedent. The registration service talks raw HTTP to an environment chosen at runtime, and "no systemuser row is created" is only observable as "no POST was sent". Request bodies are not asserted.

---

## 9.5 Quality gates (task-execute Step 9.5)

**code-review, fixed in-task:**

| Finding | Severity | Fix |
|---|---|---|
| The new refusals returned the systemuser GUIDs of the named team's members and the business unit's users to the caller. The caller is any Write holder on any secure record, and the remedy is an administrator's. | Warning (data exposure) | The response now carries counts only. The ids go to the CRITICAL operator log, and the tests assert both halves. |
| The census job could not be constructed from a minimal container. | Critical (a test broke) | Fixed as described in §9. |

**code-review, accepted:**

- `ProvisionProjectEndpoint.cs` is about 1,030 lines (was about 1,040). It is one cohesive endpoint, heavy with history comments. `TopologyRefusal` is an exhaustive status-to-ProblemDetails mapping with 11 cases. That is legitimate per COMPONENT-COMPLEXITY, and nothing was decomposed.
- `RegistrationDataverseService.cs` grew by the guard plus a GET helper, still for one reason to change.
- The census job's CRITICAL lines name principals, including UPNs, in logs. This is deliberate: the line exists to tell an operator WHO reaches the secure BU. The existing NFR-05 test output already does the same.
- `RecordOwnershipResolver` now makes one extra uncached Dataverse read per resolution (the Secure BU lookup). This is consistent with the class's documented "uncached for now" stance; if it matters, cache it behind `ITenantCache`.
- AI-smell scan:
  - no new interface;
  - no catch-log-rethrow;
  - one `ArgumentNullException.ThrowIfNull` on a non-nullable public static parameter (`SecureRecordOwnerTeam.ResolveAsync`), kept as a public-API guard;
  - no "And" god-methods added. The provisioning handler's step sequence is pre-existing.

**adr-check:**

| ADR | Result |
|---|---|
| ADR-001, ADR-052 | Compliant (BFF; the job sits on `ScheduledJobHost`; no Functions; no timer `BackgroundService`). |
| ADR-002 | Compliant: no plugin, and both invariants are enforced server-side. WP-6: an unreadable answer fails closed. |
| ADR-003 | Compliant: every refusal fails closed with a stable reason code. |
| ADR-007, ADR-009, ADR-013 | Compliant (no new Graph types outside Infrastructure, no `IMemoryCache`, no AI dependency). |
| ADR-008 | Compliant: no new endpoint; the delegation filter resolves the handler's root. |
| ADR-010 | Compliant: no new interface (the only hit is the pre-existing `IRecordOwnershipResolver` seam), and the job is concrete. |
| ADR-019 | Compliant (`Results.Problem` throughout). |
| ADR-028 A4 | Compliant: no secrets; the script uses the operator's `az` token for an explicit URL. |
| ADR-036 | Compliant: A1-4 retry on read failure, A1-5 heartbeat, A1-6 `AddScheduledJob`, A1-7 host-neutral. A1-3 does not apply (no side effect). |
| ADR-038 | Compliant except one documented **§6.5 path A**: B1, a hand-written scripted handler in `RegistrationSecureRecordPlacementTests` (precedent: `DataverseRecordShareWireTests`). No DI-registration or ctor-null tests. |

**Lint:** `dotnet build` is clean (0 warnings, 0 errors; the repo builds with warnings as errors), and `dotnet list package --vulnerable --include-transitive` reports no vulnerable packages. There were no package changes.

---

## 10. Pending manual live gates (exact commands; the owner or main session runs them after deploy)

1. **Owner decisions first**: (i) accept the Assign-cascade list (§3); (ii) F11 is structural: decide between removing roles and narrowing depth for Chelsea Friez, Lori Witkin and hotmail `#EXT#`; (iii) decide what to do with the NOT-ISOLATED project `65a3fab2…`.
2. Deploy the BFF build carrying task 144.
3. Guide §4.2: create `Secure Record Owners` (Owner type, in the Secure BU, no members). Guide §5.5a: assign `Secure Record Owner` to it. Prove an assignment (§7 item 6).
4. `.\scripts\Migrate-SecureRecordsToNamedOwnerTeam.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com` (dry run; expect a plan of 0 in dev).
   Then `… -Apply -AcceptedAssignCascade team,sharepointdocumentlocation,sharepointdocument`, then a second dry run (the plan must be empty).
5. Guide §4.3 step 4: remove the role from the default team `daec0b6f…`.
6. `… -Verify` → exit 0. Run the NFR-05 live test (§6 environment) → it passes once (1)(ii) is resolved. Run the guide §7 item 7 impersonated probe (existing non-admin test user) on a fresh secure project, a secure matter and a secure work assignment, each provisioned through the endpoint → DENIED. Record both outputs here.
7. Close GitHub #967 with this note's outcome (not done here: no external writes in this run).

## 11. Deviations from the POML

- **Step 9** (live cutover) was not executed. The run was read-only by instruction, and three owner decisions block it anyway (§10.1).
- **Step 10**: the publish-size measurement was skipped by instruction. The main session measures after merging.
- **Step 11**: `TASK-INDEX.md` was not edited (the main session owns it), and #967 was not closed (§10.7). The POML status is set to `completed-with-escalation`, the project's existing value for code that is done while an escalation is open.
- **`RecordType` vocabulary**: the endpoints accept `project | matter | workassignment`, the token set every other route in the group accepts (`ExternalGrantRoot.TryParse`), not the logical names in the POML's parenthetical. One vocabulary per route group; logical names would have needed a second parser.
- **Status codes**: the new invariant refusals return 500 with a reason code, following the endpoint's existing convention for environment faults (the client classifies by reason code, not status).
