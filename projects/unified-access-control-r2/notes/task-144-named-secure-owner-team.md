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

---

## 12. Verifier round 2: fixes on `task/uac-r2-144-f1` (2026-10-01)

An adversarial verifier re-ran the suites, seeded 8 perturbations (7 bit) and reviewed the migration script. It found
one unpinned fail-closed guard, one robustness gap and three items that need a reviewer's explicit word in the PR. Each
item, and what happened to it:

| # | Verifier item | Outcome |
|---|---|---|
| F1 / AC9 | The status guard in `RegistrationDataverseService.EnsureNotSecureRecordBusinessUnitAsync` (`if (!response.IsSuccessStatusCode) throw`) could be deleted with every test green. The fake's failed lookup returned a 503 with an EMPTY body, so `ReadFromJsonAsync` threw on its own. With a real Dataverse error body (`{"error":{...}}`) and the guard removed, the body deserializes to `Value = null`, which reads as zero matches, "no Secure BU", and the user is created in the Secure BU (fail open). | **Fixed (test).** The fake's failure now returns a Dataverse-shaped JSON error body (`SecureLookupFailure` = the status). `CreateSystemUser_WhenTheSecureRecordLookupFails_IsRefused_FailClosed` is a theory over **503 and 403**, and a sibling theory `AddUserToTeam_WhenTheSecureRecordLookupFails_IsRefused_FailClosed` covers the team-membership guard. **Seeded** the guard off (`if (false && …)`): **4/4 red** (both theories × both statuses); restored. The shipped code is unchanged. |
| F2 | `SecureBuRoleDepthAssertion.Evaluate` and `SecureBuRoleDepthCensusBuilder.Build` picked the Secure BU with `FirstOrDefault`. With two BUs carrying the configured name, only the first was graded, so the job and the manual gate could report isolated. | **Fixed (code).** One shared `SecureBuRoleDepthAssertion.SecureBusinessUnitsNamed`. `Evaluate`: two or more matches give a single finding with the new verdict **`SecureBusinessUnitAmbiguous`** (never a pass, never inert; the job logs it CRITICAL), naming every matching BU id. `Build`: lists users and named teams across EVERY BU bearing the name, so it over-reports rather than hiding the second. Tests: `Evaluate_WhenTwoBusinessUnitsCarryTheSecureName_ReportsAmbiguous_NeverIsolated`, `Build_WhenTwoBusinessUnitsCarryTheSecureName_ListsUsersInEachOfThem_NotJustTheFirst`, and the job test `Run_WhenTwoBusinessUnitsCarryTheSecureName_LogsCriticalAndNeverReportsIsolated`. In each, the second BU is listed AFTER the real one, so first-match grading would pass the clean one. **Seeded** P-B (the evaluator's ambiguity block off, so it grades the first match): **3 red**. P-C (the builder takes `.Take(1)`): **1 red**. Both restored with a fresh timestamp. |
| F3 | `RecordOwnershipResolver`: a misconfigured `SecureRecord:BusinessUnitName` reads as "no Secure BU", so a child of a secure record resolves to the Secure BU's DEFAULT team. The only Dataverse-side mitigation exists after the live cutover. | **Documented and accepted; no code change.** The resolver's remarks now state the edge explicitly, and that the mitigation exists only after guide §4.3 step 4. PR callout below. |
| B1 | `RegistrationSecureRecordPlacementTests` uses a hand-written `HttpMessageHandler` (not `Mock<HttpMessageHandler>`), declared a §6.5 path-A exception. | **Needs explicit reviewer sign-off.** PR callout below. The round-2 change keeps the same shape and adds only a realistic error body. |
| Vocabulary | The POML lists `RecordType` as `sprk_project \| sprk_matter \| sprk_workassignment`. The code accepts the grant-route tokens `project \| matter \| workassignment` (`ExternalGrantRoot.TryParse`; `sprk_project` gets a filter 403). Recorded in §11. | **Needs explicit reviewer acceptance.** PR callout below. No code change. |
| Escalations (a)–(c) | Assign cascade; F11 premise; NOT-ISOLATED project. | **Still open; re-evaluated read-only today** (below). Not defects. |
| AC11 / AC12 / AC14 | Live gate; the second `-Apply` run; publish size. | **Not closable here.** AC11 and AC12 wait on the live gate and the owner decisions; AC14 waits on the main session's fresh-master measurement. |
| Main-session items | TASK-INDEX, close #967, hand-off to word-add-in-r1 (`scripts/Backfill-RecordOwnership.ps1` would choose the Secure BU's default team). | Main session (this branch must not edit TASK-INDEX). |

### Live re-check (READ-ONLY, spaarkedev1, 2026-10-01, after the fixes)

- **Migration dry run** (`Migrate-SecureRecordsToNamedOwnerTeam.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com`; GET-only before the dry-run exit):
  - `sharetopreviousowneronassign = False`.
  - Secure BU `d9ec0b6f`. Default team `daec0b6f`, **0 members**. **0 users** in the Secure BU.
  - Named team **NOT RESOLVED (0)**. The role is on the **default team**.
  - 6 Assign-cascade relationships (project/matter → `team`, `sharepointdocumentlocation`, `sharepointdocument`), **NOT ACCEPTED**.
  - Census, 1 row: project `65a3fab2` is **NOT-ISOLATED**. It is owned by `cb15f587` (Spaarke Business Unit 1's default team), has its own container, no content and 0 shares.
  - Unprovisioned secure matters or WAs with content: **none** (0 secure matters, 0 secure WAs).
  - **PLAN 0 rows**; 2 STOPs; exit 2; nothing written.
- **NFR-05 live census through the round-2 evaluator** (`SPAARKE_NFR05_DATAVERSE_URL`, `AZURE_TOKEN_CREDENTIALS=AzureCliCredential`):
  - 6 BUs, **exactly one** named `Secure Record` (no ambiguity finding). 282 grants (33 human), 1 owner-role holder, 0 named teams, 0 BU users.
  - **17 findings, unchanged**:
    - 1–15, clause 1: **Chelsea Friez** and **Lori Witkin** (`@demo.spaarke.com`; Spaarke Core User + Spaarke Office Add In User) and the hotmail `#EXT#` account (Spaarke Basic User), Deep at root, on project, matter and work assignment.
    - 16: `SecureOwnerTeamNotResolved`.
    - 17: `SecureOwnerRoleHeldBeyondOwnerTeam` (the default team).
- 🔔 **The F11 stop still fires.** The owner's round-3 F11 answer rests on "the only account reaching the Secure BU through role depth is the hotmail `#EXT#` account". The live census says **three** accounts reach it, and that answer does not cover Chelsea Friez or Lori Witkin. Nothing was improvised: no role or user was touched, and no census exception was added. Owner decision needed: remove those roles from Chelsea Friez and Lori Witkin, or narrow the depth of `Spaarke Core User` / `Spaarke Office Add In User` (do not relocate users).

### PR description: items the reviewer must address explicitly

1. **A fail-open edge, accepted on a condition (F3).** With a misconfigured `SecureRecord:BusinessUnitName`, `RecordOwnershipResolver` resolves a child of a secure record to the Secure BU's DEFAULT team. Dataverse refuses that assignment only after the live cutover removes the `Secure Record Owner` role from the default team (guide §4.3 step 4). Until then, the only signals are provisioning's `secure_bu_not_found` refusal and the census job's inert warning.
2. **ADR-038 B1, §6.5 path-A exception: reviewer sign-off required.** `RegistrationSecureRecordPlacementTests` uses a hand-written scripted `HttpMessageHandler` (precedent: `DataverseRecordShareWireTests`). It asserts only whether the creating POST was sent; request bodies are not asserted. Round 2 makes its failure responses carry a realistic Dataverse error body.
3. **`RecordType` vocabulary deviation: reviewer acceptance required.** The endpoints take `project | matter | workassignment`, the tokens of the grant routes' `ExternalGrantRoot.TryParse`, as the POML's canonical reference `ResolveGrantRoot` does. They do not take the logical names in the POML constraint; a logical name gets a filter 403. There is no existing client for matter or work assignment.

### Round-2 test scope beyond the stated contract (one line each)

- `AddUserToTeam_WhenTheSecureRecordLookupFails_IsRefused_FailClosed`: the team-membership guard shares the status check F1 found unpinned.
- The three ambiguity tests: verifier finding F2.

---

## 13. Live gate run 2026-10-02 (spaarkedev1)

> Section 12 was already taken by verifier round 2, so this is section 13. The instruction named it "12. Live gate run 2026-10-02".

- **Authority:** owner round 4 (2026-10-01), items 1–3 and 6, `session27-owner-decisions-and-research.md`.
- **Operator:** the `az` login `ralph.schroeder@spaarke.com`, systemuser `1d02f31c-1872-f011-b4cb-7c1e52671ad0`. Dataverse tokens were minted for the explicit URL.
- **Scripts:** from worktree `C:\wt28b` at `bca0941f6`.
- **BFF:** `https://spaarke-bff-dev.azurewebsites.net`. The 144 build is confirmed deployed: `GET /api/admin/jobs` lists `secure-record-isolation-census`.
- **Times:** all UTC, 03:46–04:00Z.

**Outcome:** the cutover is done and `-Verify` passes.

🔴 **The run STOPPED at step 8 (NFR-05).** Clause 1 now names six principals beyond the three accepted root-BU users. The cause is a change outside this run: the **root BU's default team `Spaarke` holds `Spaarke Basic User` (Deep Read on project, matter and work assignment)**. So every human in the root BU reaches the Secure BU by depth. Steps 9 and 10 were **not run**.

### 13.1 Step 1: pre-checks (read-only)

| Check | Result |
|---|---|
| `organization.sharetopreviousowneronassign` | **False** |
| systemusers in Secure BU `d9ec0b6f-80a0-f111-aaac-000d3a99d1d7` | **0** |
| default team `Secure Record` (`daec0b6f-80a0-f111-aaac-000d3a99d1d7`) members | **0** |
| teams named `Secure Record Owners` (anywhere) | **0** |
| `Secure Record Owner` (`e4ebabd9-b4a0-f111-aaac-000d3a99d1d7`) holders | the default team only; 0 users; 8 privileges |
| `65a3fab2-77a5-f111-aaad-70a8a590c51c` | secure; owned by team `cf15f587` (default team of `Spaarke Business Unit 1`); BU `cb15f587`; container `b!HBRbokLXnUGzaDLSTdNFvM5RFHtaaUZCi0Jm-xs-hDQV_6QuLuKmR4jrMdC6UgMm`; 0 shares; created by `ralph.schroeder@spaarke.com` (the operator) |

### 13.2 Step 2: the named team, the role, and the assignment probe

- **Guide §4.2:** `POST teams` → 204. Team `Secure Record Owners` = **`6eabc7f9-13be-f111-a05b-0022482913fc`**: `teamtype` 0, `isdefault` false, BU `d9ec0b6f`, administrator = the operator. **0 members** read back.
- **Guide §5.5a:** `POST teams(6eabc7f9…)/teamroles_association/$ref` → `roles(e4ebabd9…)` → 204. Read back: the team holds exactly `Secure Record Owner`.
- **§7 item 6, 3 polls ~20 s apart.** Each poll:
  - created a probe `sprk_project` (`sprk_issecure=true`, owned by the operator in root BU `06fbf21c`);
  - ran `PATCH ownerid@odata.bind → /teams(6eabc7f9…)`;
  - read it back;
  - deleted it.

| Poll | Probe id | PATCH | Owning team | Owning BU flipped to Secure BU | Delete / read-back |
|---|---|---|---|---|---|
| 1 03:48:10Z | `2fe1b215-14be-f111-a05b-0022482913fc` | 204 | `6eabc7f9` ✅ | ✅ | 204 / 404 |
| 2 03:48:33Z | `868f7223-14be-f111-a05b-0022482913fc` | 204 | `6eabc7f9` ✅ | ✅ | 204 / 404 |
| 3 03:48:55Z | `23b06e2f-14be-f111-a05b-3833c5e9614d` | 204 | `6eabc7f9` ✅ | ✅ | 204 / 404 |

### 13.3 Step 3: migration

- **Dry run.** Exit **2**. Plan **0 rows**. Census shows 1 row: `[NOT-ISOLATED] project 65a3fab2 'Test New Matter via Workspace' owningTeam=cf15f587 owningBu=cb15f587 shares=0 createdBy=Ralph Schroeder (app=False) container=own content=none`. The only STOP: `Assign cascades from a root to: sharepointdocument, sharepointdocumentlocation, team`. Role holders: the default team plus `Secure Record Owners`. Named team: 0 members. BU: 0 users.
- **`-Apply -AcceptedAssignCascade team,sharepointdocumentlocation,sharepointdocument`:** exit **0**. All 6 cascades `ACCEPTED`. `Nothing to migrate: every secure row in the Secure Record BU is already owned by the named team.` **No row was written.**
- **Second dry run** (cascade list passed): exit **0**. **`PLAN: 0 row(s)`**, no STOP. `DRY RUN: nothing was written.`

### 13.4 Step 4: provision `65a3fab2` through the endpoint

`POST /api/v1/external-access/provision-project`, called with a **delegated** token: `az account get-access-token --resource api://1e40baad-e065-4aea-a8d4-4b7ab273458c`, which gives `scp SDAP.Access user_impersonation`, upn `ralph.schroeder@spaarke.com`. Body: `{"recordType":"project","recordId":"65a3fab2-77a5-f111-aaad-70a8a590c51c","projectRef":"Test New Matter via Workspace"}`. Result **200**:

```json
{ "businessUnitId": "d9ec0b6f-80a0-f111-aaac-000d3a99d1d7", "businessUnitName": "Secure Record",
  "ownerTeamId": "6eabc7f9-13be-f111-a05b-0022482913fc", "ownerTeamName": "Secure Record Owners",
  "speContainerId": "b!MVasATu_GE6Lqs6JOGaeghG_EVjnHABFpxLm1FYg2Ah7gIBcg3lNSbiHX5dqt_eN",
  "sharedToCreatorSystemUserId": "1d02f31c-1872-f011-b4cb-7c1e52671ad0", "additionalPrincipalsShared": 0,
  "recordType": "project", "recordId": "65a3fab2-77a5-f111-aaad-70a8a590c51c" }
```

Read back from Dataverse:
- **Owner:** owning team `6eabc7f9` (the named team), owning BU `d9ec0b6f` (Secure Record). `sprk_issecure` is true.
- **Shares:** exactly one POA row, to systemuser `1d02f31c` (the operator, who is the record's creator, per F8), `accessrightsmask` **262167**. That is Read+Write+Append+AppendTo+Share, which equals `CreatorAccessRights` (Collaborate + ShareAccess).
- **Container:** `sprk_containerid` is now a **new** container of its own, `b!MVasATu…`, referenced by 1 root.
  - ⚠️ This was **not** "its own container intact". The endpoint creates a container on every provisioning and overwrites `sprk_containerid` (`RecordContainerAsync`; the §3 option (a) wording, "a new container", says so).
  - The previous container `b!HBRbokLXnUGzaDLSTdNFvM5RFHtaaUZCi0Jm-xs-hDQV_6QuLuKmR4jrMdC6UgMm` is now referenced by **0** roots. The migration census had recorded "content=none" for it.
  - It is an orphaned, empty SPE container. **Not deleted** (outside this run's allowed writes). Its removal is an owner or operator decision.

### 13.5 Step 5: the role leaves the default team (guide §4.3 step 4)

`DELETE teams(daec0b6f…)/teamroles_association(e4ebabd9…)/$ref` → **204**. Read back:
- the default team holds **no roles**;
- `Secure Record Owner` is held by **`Secure Record Owners` alone** (`isdefault=False`), with 0 users.

### 13.6 Step 6: `Migrate-SecureRecordsToNamedOwnerTeam.ps1 -Verify` → **exit 0**

```
Named owner team     : Secure Record Owners (6eabc7f9-13be-f111-a05b-0022482913fc)
Named team members   : 0
Default team members : 0
Users in Secure BU   : 0
Owner role holders   : team 'Secure Record Owners'
   [DONE                           ] project        65a3fab2 'Test New Matter via Workspace' owningTeam=6eabc7f9 owningUser=- owningBu=d9ec0b6f defaultTeam=False shares=1
PLAN: 0 row(s) to move to 'Secure Record Owners'.
VERIFY: PASS
```

### 13.7 Step 7: task 145 G1

Recorded in `task-145-secure-owner-role-privileges.md` §11. Summary:
- **Negative control:** refused 3/3, naming only `prvReadsprk_Invoice`, with privilegeCount=8.
- **Script:** dry run → `-Apply` → §5.4 strip (removed exactly the re-injected SharePoint four) → `-Verify` exit 0 (9/9).
- **Positive probes:** 3/3 succeeded. The sprk_analysis control was refused 3/3 with privilegeCount=9.

### 13.8 Step 8: NFR-05 live test. 🔴 STOP

Command: `SPAARKE_NFR05_DATAVERSE_URL=https://spaarkedev1.crm.dynamics.com`, `AZURE_TOKEN_CREDENTIALS=AzureCliCredential`, then `dotnet test C:\wt28b\tests\unit\Sprk.Bff.Api.Tests --filter FullyQualifiedName~SecureBusinessUnitRoleDepth_InTheTargetEnvironment`. Result: **Failed (1 of 1)**.

> NFR-05 role-depth census for https://spaarkedev1.crm.dynamics.com: 6 business unit(s), 795 effective grant(s) of
> prvReadsprk_Project / prvReadsprk_Matter / prvReadsprk_WorkAssignment (60 held by humans), 1 holder(s) of 'Secure
> Record Owner', 1 team(s) named 'Secure Record Owners' with 0 member(s) of any kind, 0 systemuser(s) in 'Secure
> Record', 1 owner role(s) in it covering 9 of 9 codified table(s) at Basic. Verdict: HumanPrincipalReachesSecureBusinessUnit.

**Clauses 2, 2b, 3, 4 and 5 PASS.** No finding of those verdicts appears. This is the first live run in which they all pass:
- the named team resolves;
- it has 0 members;
- it is the sole holder;
- the BU has 0 users;
- the role covers 9 of 9 codified tables.

**Clause 1: 42 findings, all `HumanPrincipalReachesSecureBusinessUnit`, all Deep, all anchored at root BU `06fbf21c`.** The 2026-10-01 census had 17 findings and 282 grants (33 human).

| Findings | Principal | How |
|---|---|---|
| 1–12 | Chelsea Friez, Lori Witkin | Spaarke Core User + Spaarke Office Add In User, direct. **Accepted** (owner round 4, item 1) |
| 13–15 | `ralph.schroeder_hotmail.com#EXT#` | Spaarke Basic User, direct. **Accepted** |
| 16–21 | Chelsea Friez, Lori Witkin | **Spaarke Basic User via team `Spaarke`**. NEW mechanism (accepted users) |
| 22–24 | Ralph Schroeder `ralph.schroeder@spaarke.onmicrosoft.com` | Spaarke Basic User via team `Spaarke`. **NEW, not accepted** |
| 25–27 | Final Test `final.test@demo.spaarke.com` | the same. **NEW** |
| 28–30 | Ralph Schroeder `ralph.schroeder@spaarke.com` (the operator; the census calls him non-administrator) | the same. **NEW** |
| 31–33 | Eyal Iffergan `eyal.iffergan@spaarke.com` | the same. **NEW** |
| 34–36 | `ralph.schroeder_hotmail.com#EXT#` | the same (a second path for an accepted user) |
| 37–39 | Jake Schroeder `jake.schroeder@demo.spaarke.com` | the same. **NEW** |
| 40–42 | E2E Test `e2e.test2@demo.spaarke.com` | the same. **NEW** |

**Read-only diagnosis.**
- `Spaarke Basic User` has root copy `11f93c04-ddf6-f011-8406-7c1e520aa4df`, in root BU `06fbf21c`, modified 2026-09-30 02:43Z. It holds `prvReadsprk_Project`, `prvReadsprk_Matter` and `prvReadsprk_WorkAssignment` at **Deep**.
- Its holders now:
  - the hotmail guest (direct);
  - the **root BU's default team `Spaarke` (`09fbf21c-1872-f011-b4cb-7c1e52671ad0`, 171 members; 9 enabled interactive users in the root BU)**;
  - `Spaarke Business Unit 1`'s default team `cf15f587` (a sibling BU, which does not reach the Secure BU).
- The 2026-10-01 censuses (§6, §12; 145 note §5) saw `Spaarke Basic User` only on the hotmail account. So the root default team's association happened after them, and outside this run.
- The `audits` query on team `09fbf21c` returns no rows, so who made the change and when is unknown.
- **This run wrote nothing to `Spaarke Basic User`, to team `Spaarke`, or to any user.**

**Consequence:** every human in the root BU reads every secure project, matter and work assignment by depth, including the freshly provisioned `65a3fab2`. This is outside the owner's round-4 acceptance, which named exactly three users. Owner decision needed:
- (a) remove `Spaarke Basic User` from the root default team `Spaarke`; or
- (b) narrow the role's three Reads from Deep to Local; or
- (c) accept it for dev.

Recommendation: (a), and find who associated it. This run changed nothing, by instruction.

### 13.9 Steps 9 and 10: NOT RUN (stopped at step 8)

- **Step 9** (§7 item 7, the impersonated probe on `65a3fab2` plus the (g) matter and work assignment): not run.
  - **No (g) test records were created.**
  - With step 8's finding standing, a root-BU non-admin would read them. The probe user must sit in a NON-root BU (for example `Spaarke Business Unit 1`) once the run resumes.
- **Step 10** (census job). Observed once **before** the stop, read-only, via `GET /api/admin/jobs` (the delegated token carries role `Admin`): `secure-record-isolation-census` is enabled, cron `*/15 * * * *`, last run 2026-10-02T03:45:00Z → 03:45:01Z, **`lastRunStatus: Failed`**.
  - That run came BEFORE this run's first write (03:47Z), so it graded the pre-cutover state.
  - `/api/admin/jobs/secure-record-isolation-census/status` was not queried after the stop.

### 13.10 Every live write in this run

| # | Entity | Id | What |
|---|---|---|---|
| 1 | team | `6eabc7f9-13be-f111-a05b-0022482913fc` | created `Secure Record Owners` (Owner, Secure BU, no members) |
| 2 | teamroles_association | team `6eabc7f9…` ↔ role `e4ebabd9…` | associated `Secure Record Owner` |
| 3–5 | sprk_project | `2fe1b215-14be-f111-a05b-0022482913fc`, `868f7223-14be-f111-a05b-0022482913fc`, `23b06e2f-14be-f111-a05b-3833c5e9614d` | §7 item 6 probes: created, assigned to the named team, deleted (404 on read-back) |
| 6 | (none) | — | migration `-Apply`: 0 rows to move, nothing written |
| 7 | sprk_project | `65a3fab2-77a5-f111-aaad-70a8a590c51c` | provisioned via the endpoint: owner → named team; POA share to `1d02f31c` (262167); new SPE container `b!MVasATu_GE6Lqs6JOGaeghG_EVjnHABFpxLm1FYg2Ah7gIBcg3lNSbiHX5dqt_eN` recorded; old container `b!HBRbo…` orphaned (empty, not deleted) |
| 8 | teamroles_association | team `daec0b6f…` ↔ role `e4ebabd9…` | **removed** (default team no longer holds the role) |
| 9–13 | role `e4ebabd9…` privileges | — | task 145 G1: `AddPrivilegesRole` +`prvReadsprk_Invoice` (Basic), which re-injected the SharePoint four; then `RemovePrivilegeRole` ×4 removed exactly those four. Net change: +1 privilege |
| 14–16 | sprk_invoice | `db6ecf10-15be-f111-a05b-0022482913fc`, `5959bb20-15be-f111-a05b-0022482913fc`, `76e58e2f-15be-f111-a05b-3833c5e9614d` | G1 positive probes: created team-owned, deleted (404 on read-back) |

Refused creates (nothing written): 3 negative-control invoices and 3 sprk_analysis controls.
