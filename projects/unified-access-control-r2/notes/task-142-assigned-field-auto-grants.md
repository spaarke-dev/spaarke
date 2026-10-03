# Task 142 — Assigned-To auto-grants (#1065)

> Branch `task/uac-r2-142` (from `task/uac-r2-143-r2` + `task/uac-r2-137-b2` + `work/unified-access-control-r2`, base
> `ee925b903`); verifier fix rounds **`task/uac-r2-142-r1`** (§11) and **`task/uac-r2-142-r2`** (§12). Server, client, web
> resources, ribbon source and schema script complete. **Live writes: none** — every live step is a pending manual gate
> (§7). The ADR-034 amendment A4 is drafted and **awaits the owner's §6.5 acceptance** (§8); the code merges with it,
> never before — and only after the dependency merges in §7 G-0.

## 1. Owner answers applied (no escalation fired as a stop)

| POML trigger | Answer (source) | Applied as |
|---|---|---|
| (a) Secure | A3 = prompt; existing auto grants KEPT when a record becomes secure (round 3, accepted as recommended) | Secure root: contact grant / POA share → ledger `PendingConfirmation`, shown in Manage Access "Suggested Access" naming the source field, **Grant** (normal `/grant` at Collaborate, or `/share-user` for a linked user) → `Adopted`, **Dismiss** (`POST /assigned-access/dismiss`) → `Declined`. A grant that existed before the record became secure stays `Granted`; an auto share is never removed on a secure record by the rule (S5) |
| (b) Changed/cleared | A4 = revoke the unmodified auto access (round 3) | `EndAssignmentAsync`: revokes only `Granted`/`Shared` rows whose level/expiry (mask) still equal what the owner wrote, not when another registry column still names the subject, never `Adopted`/`Declined`. A raised manual grant is put back to its prior level AND date (`raised-from:{level}@{date}`, r2), a raised share to its prior mask (`raised-from-mask:`); a restore the record's policy forbids waits (r2, `restore-pending:`) |
| (c) Q1 vs Q5 | A1 = Q5 governs: always Collaborate, NO grantor cap (round 3 + 3b); `sprk_grantedby` = saving user, empty for the job | `GrantCeiling.AssignedToRule` (Collaborate, uncapped) through the ONE grant core `CreateGrantAsync`; grantor oid = the sync caller / the Office maker; `null` for the job **and for the AI-tool and field-mapping L1 triggers** (reminders go to the record owner — residual R-4) |
| (d) Retire read-time terms? | A2 **REVERSED** = KEEP standing and organization access (round 3) | `AccessibleRecordSetService` untouched. Criterion 17 "keep" branch: removing an auto grant whose subject still reaches the record names the term BEFORE (Manage Access confirm, from `GET /assigned-access` `residualAccessTerms`) and AFTER (`RevokeAccessResponse.ResidualAccessTerms`): `standing-grant`, `organization-standing-grant`, `organization-members-standing-grant` (an organization's own grant), or `unknown` |
| (e) Expiry | A5 = renew (round 3) | An unmodified, still-assigned auto grant inside the FR-33 reminder window (`GrantExpiryReminderJob.ReminderDays.Max()` = 30 days) — or already lapsed — is renewed to today + 90 through `DefaultExpiry`, by any trigger |
| (f) Child entities | A6 = no root grant (round 3) | Only the three roots are materialized (`RegistryFor` returns nothing for a child; `RunAfterWriteAsync` ignores non-root tables). Live: no gate composes a child type (`CallerPrincipalResolver` composes the three roots only) — trigger not fired |
| (g) Job posture | R3 = enabled, writes on for create/convert/renew; removal behind a positive switch, report-only by default; **cadence ≤ 5 min** (round 3 R3/R4 overrides "hourly") | `AssignedAccessReconciliationJob`, `*/5 * * * *`, `AddScheduledJob`; switch `ExternalAccess:AssignedAccess:JobRevokeOnChangeEnabled` (absent/false = report-only, result `wouldRevoke`). The sync route and L1 writers always revoke. Turning it on in dev is a manual gate AFTER criterion 21 (§7) |
| (h) Registry vs live | — | Not fired: live metadata matches the registry (§2) |
| (i) Writer cannot call after commit | — | Not fired: every census writer calls after its write without an API or transaction change (two AI handlers take an OPTIONAL `IServiceScopeFactory`; `RecordCreationService` takes one more DI parameter) |
| (j) Live gate user/root pair | — | Not reached (live gates are the main session's). It may fire at criterion 21 (ii) — see §7 |
| A7 (reversed) | Office quick-create assigns the maker (round 3) | Already implemented by task 152 (`RecordCreationService.ApplyMakerAssignedInternalAsync`); this task's L1 call after the create issues the maker's share/grant |
| A8 | Limited: write contact grants; Restricted refused (round 3) | Limited → contact grants written, organization grants not (`IsDirectOnly`); Restricted → no contact/org grant, linked internal users still shared |
| T1 | (b) fresh test records for the 142 live gate (round 3) | Recorded in §7 — creating them is a live write (main session) |
| #1081 / round 5 | root-BU users are a dev artifact | Live gate 21 uses child-BU non-admin users only; never root-BU or "Spaarke Demo" team members |

Interpretation recorded (no owner question needed): **Declined is per subject on the root**, carried to every field
naming that subject while any of them persists — one grant row exists per (subject, root), so re-creating it through a
second field would undo the operator's removal. An ended assignment's `Declined` row becomes `Revoked(assignment-ended)`,
so a LATER re-assignment is a new assignment.

## 2. Inventory (criterion 1, step 1)

Bound registry = `MembershipOptionsDefaults.CanonicalAccessConferringRegistry` (`Services/Ai/Membership/MembershipOptions.cs:303-372`).
Verified live on spaarkedev1 2026-10-03 (read-only `describe`): every column below exists as a Lookup to the stated
target; no other Assigned-type lookup exists on the three roots (trigger (h) not fired).

| Root | Contact columns | Organization columns |
|---|---|---|
| `sprk_matter` | assignedattorney1, assignedattorney2, assignedparalegal1, assignedparalegal2, assignedtoexternal, assignedtointernal | assignedlawfirm1, assignedlawfirm2 |
| `sprk_project` | same six | assignedlawfirm1, assignedlawfirm2 |
| `sprk_workassignment` | assignedattorney1, assignedattorney2, assignedlawfirmattorney1, assignedparalegal1, assignedparalegal2, assignedto, assignedtoexternal, assignedtointernal | assignedlawfirm1, assignedlawfirm2 |

Child entries (A6 — **no root grant**): `sprk_event` (:338-351), `sprk_invoice` (:352-360), `sprk_todo` (:361-364),
`sprk_analysis` (:365-371). Also verified live: `sprk_workassignment` carries `sprk_regardingmatter` /
`sprk_regardingproject`, so a field-mapping push from a matter/project can target a root (hence the push L1 hook).

## 3. Writer census (criterion 1, step 2)

| Writer | file:line (call after commit) | Trigger |
|---|---|---|
| Office quick-create matter / project (`RecordCreationService`, incl. A7 maker + `CreateTimeFieldMapping` copies) | `Services/Office/RecordCreationService.cs:432` (matter), `:527` (project) | L1 inline, grantor = maker |
| AI node `UpdateRecordActionCore.UpdateAsync` (any table; payload keys incl. `@odata.bind`) | `Services/Ai/Nodes/ActionCore/UpdateRecordActionCore.cs:145` | L1 (`RunAfterWriteAsync`) |
| AI chat tool `dataverse_update_record` | `Services/Ai/Handlers/DataverseUpdateRecordHandler.cs:185` | L1 |
| AI chat tool `dataverse_create_record` | `Services/Ai/Handlers/DataverseCreateRecordHandler.cs:335` | L1 |
| Field-mapping push (`POST /api/v1/field-mappings/push`, ribbon `sprk_fieldmapping_push.js`) | `Api/FieldMappings/FieldMappingEndpoints.cs:726` | L1 per updated child |
| Create Matter wizard | `CreateMatterWizard/matterService.ts:354` | client → sync route |
| Create Project wizard (standard project) | `CreateProjectWizard/projectService.ts:402` | client → sync route |
| Create Project wizard (secure project — after provisioning, so A3's prompt applies) | `CreateProjectWizard/CreateProjectWizard.tsx:738` | client → sync route |
| Create Work Assignment wizard | `CreateWorkAssignmentWizard/workAssignmentService.ts:544` | client → sync route |
| MDA main forms (project, matter, work assignment) | `src/solutions/webresources/sprk_assignedaccess_postsave.js` (`addOnPostSave`) | form post-save → sync route |
| "Access › Update Access" ribbon | `src/client/webresources/js/sprk_access_ribbon.js` | ribbon → sync route |
| MDA grid edit, quick-create forms, import, flows, Web API | — | L4 job (≤ 5 min) |

Not writers of a root registry column (no hook, with evidence): `WorkAssignmentEndpoints.CreateWorkAssignmentAsync`
(`Api/WorkAssignmentEndpoints.cs:122` — sets `ownerid` to a systemuser, no Assigned column); `OfficeService` to-do create
(`:2016`, `sprk_todo.sprk_assignedto` — child); `AssignedToDefaults`, `TaskActionCore`, `CreateTaskNodeExecutor`,
`CommunicationCreateTaskApplyService`, `TodoGenerationService`, `EventEndpoints` (to-do / event children — A6);
`DataverseWebApiFieldMappingSeeder` (provisioning; seeds mapping profiles, not records). The legacy
`src/solutions/LegalWorkspace/src/components/CreateMatter/matterService.ts:222` writes Assigned columns but its
`MatterService` class is never instantiated (only `searchUsersAsLookup` is imported, `FilePreviewDialog.tsx:18`; the
standalone page is retired) — not a live writer; the job covers it if revived. Secure/unsecure provisioning changes flags,
not columns: the job (and "Update Access") applies the transition.

## 4. What was built — placement (§10) and justification (§11)

**Placement**: all server code in the BFF beside the other external-access writers (`Services/ExternalAccess/`,
`Api/ExternalAccess/`) — BFF domain code over BFF-owned tables (`sprk_externalrecordaccess`, the new ledger), the BFF's
identity, low volume; the job on the in-process scheduler (ADR-052 B2/B3, ADR-036 `AddScheduledJob`). No new package, no
AI-internal type in CRUD code (ADR-013; the materializer reads `MembershipOptions`, as `AccessibleRecordSetService`
does), no `.WithClientSecret`, no plugin/flow/service endpoint (ADR-002, D-1), `GrantMembershipAsync` untouched.
Publish size: not measured (harness instruction — main session). CVE: no package added.

| New surface | Existing (grep) | Extension? | Cost of doing nothing |
|---|---|---|---|
| `AssignedAccessMaterializer` (scoped) — the ONE invariant owner | Nothing derives access from Assigned columns (`rg sprk_assignedattorney1 src/server` → registry, read paths, seeder only); `ExternalAccessReconciliationJob` repairs row lifecycle only | Extends the cores, never forks them: grants via `CreateGrantAsync`, shares via `IDataverseRecordShareService` (strict read + read-back), walls via 143's `SecureShareNoAccessGuard`, cache via 143's `InvalidateForTenantAsync` | Owner Q5 unmet; when 036's flag turns on, internal assignees lose Teams/SPA access |
| `AssignedAccessStore` (scoped) — ledger CRUD + the reads no reader answers (a root's registry columns, the systemusers a contact represents incl. the oid binding, an organization's state, the job's scans) | `ExternalParticipationService` (flags, memberships), `ExternalGrantLifecycle` (grant rows), `IContactIdentityStore` (contact status), `NoAccessEnforcementStore` (No Access rows) | None of them owns the ledger; adding ledger CRUD to `ExternalParticipationService` mixes a second reason to change into a participation reader | No ledger → no provenance → removal cannot stick, revoke-on-change cannot tell auto from manual |
| `AssignedAccessReconciliationJob` (`AddScheduledJob`, `*/5`) | `ExternalAccessReconciliationJob` (R1–R3), `NoAccessShareReconciliationJob` | Rejected: R1–R3's contract is "a row's own state is the truth" and it ships report-only (round 7), which would switch off a required feature | Grid edits/imports/flows never get access; a failed form/wizard call is never repaired; 141 links never convert |
| Routes `POST /assigned-access/sync`, `GET /assigned-access`, `POST /assigned-access/dismiss` (on the delegation-filtered admin group) + 3 `DelegationRuleFilter` cases | `/grant` (client-chosen subjects), `/share-user`, `/user-shares` (shares only), `/no-access/enforce` | The sync cannot be `/grant` (subjects must come from the record — constraint); list/dismiss serve ledger data no route exposes; the filter cases are mandatory (default branch denies) | No post-save/wizard/Update-Access trigger; no Manage Access suggestion surface (A3 unimplementable); Dismiss = Declined impossible |
| `GrantCeiling.AssignedToRule` | `GrantCeiling.FromGrantorRights` | A grantor-derived ceiling would cap (A1 forbids) | The core (task 139 WP-1) requires an explicit ceiling; without one the grant cannot be written |
| `InternalShareEndpoints.ClassifyEligibility` (extracted) | `EligibilityRefusal` | Extracted from it unchanged (reuse, not a second rule) | The materializer would copy the eligibility rule (constraint: reuse it) |
| `RevokeAccessResponse.ResidualAccessTerms` (optional, additive) | — | Additive field on the existing DTO | Criterion 17 "after" message impossible |
| Config `ExternalAccess:AssignedAccess:JobRevokeOnChangeEnabled` | `ExternalAccess:Reconciliation:WritesEnabled` | That switch gates a different job's writes | Owner (g) requires a positive, job-only removal switch |
| Table `sprk_assignedaccess` (11 columns + key `sprk_AssignedAccessLedgerKey`), `scripts/Set-AssignedAccessLedgerSchema.ps1` | `sprk_externalrecordaccess`, `sprk_userentityassociation`, `sprk_noaccessentry` (see `entity-schema.md`) | Rejected per the POML justification (2): no row for a share; manual `/grant` upserts the same row; the membership junction deletes orphans exactly when revoke-on-change needs them | Removal does not stick; changed fields revoke manual grants or leave stale access |
| `sprk_assignedaccess_postsave.js` | `sprk_kpiassessment_quickcreate.js` (KPI-specific) | Reuses `sprk_bff_auth.js`; the KPI script does unrelated work | MDA edits (the dominant writer) wait a job cadence |
| `sprk_access_ribbon.js` + `infrastructure/dataverse/ribbon/AccessRibbons/` (template + `Merge-AccessRibbon.ps1`) | `sprk_fieldmapping_push.js`, `sprk_registrationribbon.js` (other domains) | Per the UX amendment justification: one group/script, extended by task 150 | R3's "Update Access" has no surface; 150 would create a second group |
| `assignedAccessSync.ts` (client helper) | each wizard service holds `authenticatedFetch` | Inlining in three services would triplicate the never-reject contract | Wizard-created assignees wait for the job (criterion 14) |
| TrackingFieldTrio 1.0.33 → 1.0.34 | — | Version bump only; bundles the modal change | — |

**Component complexity (§11.5)**: `AssignedAccessMaterializer.cs` is ~1,720 lines and takes 13 constructor
dependencies — every one an existing seam the constraints require it to reuse. It is cohesive (one invariant: decide,
mark, list). Decomposition seed if it grows: the Manage Access read model (`ListAsync`, residual terms, `SourceFieldLabel`)
has its own reason to change and could move to a `AssignedAccessReadModel` class.

## 5. Tests

ADR-038 KEEP paths; no banned shapes; subclass/virtual seams only (`AssignedAccessStore` internal-virtual methods, the
existing `FakeRecordShareTable`, a `GrantTable` interpreting the core's real OData filters through a forwarding
`DataverseWebApiClient`).

| File | Tests | Covers |
|---|---|---|
| `tests/integration/auth/UnifiedAccessControl/AssignedAccessMaterializerTests.cs` | 78 (55 + 14 in round r1 + 9 in round r2) | criteria 2–11, 16 (conversion, renewal, cache key, Restricted after a grant), A6, inline gating; r1: expired grants never cover (P1/P2 + twins), never "granted"/"restored" over a lapsed grant (P3, the read race, the lapsed restore), the share half of criterion 10 (other field, modified mask, S5), the R2 known cause, a disabled linked user |
| `tests/integration/auth/UnifiedAccessControl/AssignedAccessSyncEndpointTests.cs` | 12 | criterion 12 THROUGH the real `/api/v1/external-access` filter pipeline (Write → handler; no Write and unknown id → the same filter 403; 401; body subject ids ignored; ProblemDetails with reason codes; list + dismiss gated; cache key = caller tenant) |
| `tests/integration/auth/UnifiedAccessControl/AssignedAccessMarkerTests.cs` | 13 | criteria 6, 9, 17 through the production `/revoke`, `/unshare-user`, `/grant`, `/share-user` handlers |
| `tests/unit/Sprk.Bff.Api.Tests/Services/ExternalAccess/AssignedAccessReconciliationJobTests.cs` | 14 | criterion 16 (sweep, idempotent second run, switch on/off, cadence/enabled values, faulted scan → Success=false, TRUNCATED, ROTATING + recent-first, deployment-tenant cache key, no tenant, incomplete root, link conversion) |
| `tests/integration/data-mutation/ExternalAccess/AssignedAccessWriterTriggerTests.cs` | 10 | criterion 13 — one per census writer + the no-op twins + "a materializer fault never fails the create" |
| `AssignedAccessTestDoubles.cs` | — | the shared harness |
| Client: `services/__tests__/assignedAccessSync.test.ts` | 9 | criterion 14 (each wizard; failure logged, wizard not failed; secure project synced after provisioning only) |
| Client: `AccessGrantModal/__tests__/AccessGrantModal.assignedAccess.test.tsx` | 12 | criteria 6 + 17 UI (suggestion names its field, Grant/Dismiss paths, provenance, residual warnings before/after, the organization copy) |
| Client: `CreateProjectWizard.provisioningHost.test.tsx` | +1 | secure project synced only after provisioning |
| Modified for new signatures (`InertMaterializer()`): `GrantLifecycleCharacterizationTests`, `GrantorCeilingTests`, `InternalUserShareTests`, `SpeRevokeMatcherTests`, `RecordCreatorPersonStampTests`, `RecordCreationAssignedInternalTests`; wizard tests filter the new sync call |

**Beyond the closed contract (one line each)**: two systemusers representing one contact → `link-ambiguous` skip (never
pick one of two — 141 §5); a user bound to the contact's oid with no link yet → represented (141's link contract); a user
linked to ANOTHER contact → not this contact's; an inactive organization / inactive root → no grant (R2 / closure are
known causes, not declines); `SourceFieldLabel` (the suggestion must NAME the field — criterion 6); job rotation +
recent-first and "no deployment tenant" (the job must not silently skip roots or clear the anonymous key); the
organization residual term (criterion 17 for organization subjects, found in code review).

**Bite proof** — each violation seeded alone (build + the 104 AssignedAccess tests, or the client jest suites), restored
from a backup and touched:

| Seed | Failed |
|---|---|
| F1 filter case for the sync request removed | 9 |
| M1 Declined not sticky | 6 |
| M2 never-lower covering check removed | 2 |
| M3 secure records granted, not suggested | 6 |
| M4 Restricted does not skip contact grants | 2 |
| M5 a modified grant revoked on change | 1 |
| M6 another field naming the subject ignored | 1 |
| M7 an out-of-band removal re-created | 2 |
| M8 every ledger write made even when unchanged | 4 |
| M9 no renewal | 1 |
| M10 residual terms never reported | 1 |
| M11 a linked internal assignee granted, not shared | 20 |
| M12 the wall not checked for a secure share | 1 |
| M13 cache cleared under "anonymous" | 3 |
| M14 an organization's residual never named | 1 |
| J1 the job always revokes | 1 |
| J2 a faulted scan is a clean run | 1 |
| K1 `/revoke` does not mark Declined | 4 |
| K2 `/unshare-user` does not mark Declined | 1 (0 before the test was strengthened — the next sync's out-of-band detection masked it; the test now asserts the state right after the unshare) |
| L1 Office quick-create does not materialize | 1 |
| C1 Create Matter does not sync | 2 |
| C2 Create Work Assignment does not sync | 1 |
| C3 secure project not synced after provisioning | 1 |
| C4 the sync helper rejects on a network failure | 2 |
| C5 the organization residual copy is generic | 1 |

Round r1 seeds (each alone; build + the now **118** AssignedAccess tests; restored byte-for-byte and touched — §11):

| Seed | Failed |
|---|---|
| S1 the fresh covering check counts a non-conferring (expired) row | 3 |
| S2 the covered re-check counts a non-conferring row | 1 |
| S2b a lapsed covering grant is not a known cause (→ Declined) | 1 |
| S3 no explicit today + 90 when no row on the key confers | 4 |
| S4 the fresh path ignores the core's `expired_not_restored` Warning | 1 |
| S5 the restore path ignores the core's Warning | 1 |
| J (verifier) share: another registry column naming the subject ignored | 1 |
| F (verifier) share: a modified mask removed on change | 2 |
| G (verifier) share: the S5 keep-on-secure rule disabled | 1 |
| D (verifier) R2 inactive organization not a known cause | 1 |
| R the materializer's renewal guard ignores Restricted | **0** — the grant core's own task-138 Restricted refusal is a second layer |
| R2 the renewal guard AND the core's Restricted refusal both removed | 1 |
| E a Shared row is re-decided when its user turns ineligible | 1 |

Round r2 seeds (each alone, applied by exact string replacement; build + the now **127** AssignedAccess tests; restored
byte-for-byte — MD5 re-checked after every restore — and touched; §12):

| Seed | Failed |
|---|---|
| S6 the restore writes the level only (expiry `null`, the r1 behaviour) | 2 — both new finding-1 tests |
| S7 the raise records the level only (`raised-from:L`, no date) | 10 — every raise/restore test |
| S8 no grant-cache invalidation when the restore leaves a grant that confers nothing | 1 |
| S9 a restore whose date has passed is always reported `ledger` | 1 (the renewed-past-the-date test) |
| S9b … always reported `revoked` | 1 (r1's already-lapsed test) |
| S10 no policy hold (every restore refusal is a failure — the r1 behaviour) | 5 — every row of the hold theory |
| S11 every restore refusal is a hold | 1 — the unreadable-policy twin |
| S12 an expired grant above Collaborate is skipped instead of given Collaborate | 1 — the finding-3 deviation pin |

Web resources (no test harness exists for classic scripts): `node --check` both; a scratch smoke run in a fake form
confirmed — helper absent: OnPostSave logs "BffAuth is not loaded" and makes no call, `canUpdateAccess` = false,
Update Access shows the reload message; no token: no call + "Sign-in needed"; with a token: exactly one POST to
`/assigned-access/sync` (criterion 15, UX (d)).

**Full runs.** Full BFF unit suite (once, at the end): **14,586 passed, 0 failed, 54 skipped (14,640)**, 17m38s; BFF
build 0 errors / 0 warnings. `Spaarke.ArchTests` first run **343/346** — three guards caught real issues, each fixed by
complying (§6.5 path C): `GrantCeilingGuardTests` saw the materializer's private wrapper named `CreateGrantAsync` as
four ceiling-less core calls (renamed `WriteGrantAsync`; the one real core call passes `GrantCeiling.AssignedToRule`);
`InboundBodyDtoMappingGuardTests` read the OUTBOUND `AssignedAccessSyncResponse` as an inbound parameter of the private
`Refused` helper (now typed `object`); `RouteAuthorizationGuardTests` endpoint-file census 121 → 122 with its
classification comment. After the fixes: **ArchTests 346/346**; the affected BFF set (AssignedAccess, AccessControl,
ExternalAccess, RecordCreation, FieldMapping, the AI record handlers) **2,635 passed, 0 failed, 1 skipped**.
Client: `Spaarke.UI.Components` full jest **3,429 passed, 13 failed in 8 suites — none touches this task's files**
(RecordHeader `configResolution`, WorkspaceShell `buildDynamicWorkspaceConfig`, `RichFilePreview`, the
`todoScoreMappings` source hash, ConversationView forward / emailInFlow, `TimelineComposeBox`, `surfaceLaunchRegistry`);
inherited from the merged base, not caused here. TrackingFieldTrio `build:prod` succeeded (bundle re-copied after the
last modal change).

## 6. Deviations

- **Test paths**: the POML lists `tests/unit/.../AssignedAccessMaterializerTests.cs`; the materializer, endpoint and
  marker tests live in the ADR-038 KEEP path `tests/integration/auth/UnifiedAccessControl/` (authorization write path,
  beside `InternalUserShareTests`), the writer tests in `tests/integration/data-mutation/ExternalAccess/`. The job tests
  are at the POML path (143 precedent). All compile into `Sprk.Bff.Api.Tests`.
- **Ledger uniqueness**: step 3's second option — the BFF-computed single-string key `sprk_ledgerkey`
  (`{root}:{id}:{field}:{kind}:{subject}`) — instead of live-verifying a key over three nullable lookups (a live write).
- **`ImpersonatedRootSetSource` not modified**: the census found task 143's `DeploymentCacheTenant` +
  `InvalidateForTenantAsync`, reused.
- **`AccessibleRecordSetService` not modified** (A2 reversed = keep).
- **`CANDIDATE_ROLE_FIELDS` kept** in TrackingFieldTrio (it feeds the email-members feature and the generic candidate
  list); the secure-record suggestions are SERVER-derived from the ledger, and a subject the server already suggests is
  offered once. The client list still omits `sprk_assignedlawfirmattorney1` / `sprk_assignedto` (residual R-6).
- **Ribbon**: the per-entity RibbonDiffs are not checked in — they must be generated from FRESH exports (an export is
  read-only, but the merged import is a live write and the export needs a solution containing the entities). The work-
  assignment ribbon export/check-in (UX (f)) is therefore a manual gate (§7). The mechanism was verified locally.
- **Office A7** was already delivered by task 152; this task adds only the L1 materialization after the create.
- **"Never downgrades a level" — an EXPIRED grant above Collaborate** (r2, verifier finding 3; POML constraint "never
  lower access"). When the key's only rows are lapsed (none confers) and one carries Full Access, the rule writes
  Collaborate with today + 90 over it: the core upserts in place on the key, the rule's ceiling `GrantCeiling.AssignedToRule`
  (owner A1) cannot write Full, and the core's never-lower refusal guards only a NARROWED request. No ACCESS is lowered —
  the expired row conferred nothing, so the subject goes from none to Collaborate — but the stored Full level is
  overwritten and is not restored at the end of the assignment (the row is then removed, as for any lapsed row: the
  subject had no access here before). Alternatives rejected: refusing (skip with `grant-refused:…would_lower_existing`)
  leaves an assigned subject with nothing, against rule 5 / Q5 (binding); renewing at Full would grant above the rule's
  Collaborate for 90 days, and the core refuses it anyway. Pinned by
  `AnExpiredFullAccessGrant_IsGivenCollaborate_TheRecordedDeviation_ItConferredNothing` (seed S12). The owner may revisit;
  no ADR is involved (§6.5 not triggered).

## 7. Pending manual gates (main session; live writes — exact commands)

| # | Gate | Command / action |
|---|---|---|
| G-0 | **Dependency merges FIRST** (verifier r0 finding 7). This branch carries the non-merge commits of tasks 133, 137 and 143 that `work/unified-access-control-r2` does not have yet, including two `WIP … UNVERIFIED, do not merge` commits — `b38756ba6` (133-b1, an ancestor of `task/uac-r2-133-b2` / `-b2-r2`) and `33108909e` (137-b1, an ancestor of `task/uac-r2-137-b2`), each superseded inside its own verified line. Merging 142 first would bring them in unreviewed. | Into `work/unified-access-control-r2`, in order: `task/uac-r2-133-b2-r2` (`5a8b66c15`), `task/uac-r2-137-b2` (`a8fe5b428`), `task/uac-r2-143-r2` (`7668bbc1f`) — each verified in its own round. ⚠️ 137-b2 + 143-r2 have a SEMANTIC conflict with no textual one (143 r1 added `IContactIdentityStore` to `AccessibleRecordSetService`'s constructor; 137's seam test used the old one): bring `843d62b46` (`tests/integration/seam/ExternalAccess/UnifiedEvaluatorSeamTests.cs`, +3 lines) with the second of the two merges, or the test project does not compile. Then re-merge `work` into the 142 line and merge it. Confirm with `git log --oneline work/unified-access-control-r2..task/uac-r2-142-r1 --no-merges` = only 142 commits |
| G-1 | Ledger schema, BEFORE any BFF deploy of this branch (without it every materialization reads `ledger-unreadable` and writes nothing — fail closed) | `pwsh scripts/Set-AssignedAccessLedgerSchema.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com` (dry run), then `-Apply`, then `-Verify` (exit 0) and `describe('tables/sprk_assignedaccess')`; confirm `sprk_AssignedAccessLedgerKey` Active and the privilege census lists only System Administrator / System Customizer for Create/Write/Delete. Record in `src/solutions/SpaarkeCore/entities/sprk_assignedaccess/entity-schema.md` |
| G-2 | BFF deploy | the usual BFF deploy of the merged branch (job registers enabled; `ExternalAccess__AssignedAccess__JobRevokeOnChangeEnabled` absent = report-only) |
| G-3 | Web resources | dataverse-deploy: `sprk_/scripts/assignedaccess_postsave.js` ← `src/solutions/webresources/sprk_assignedaccess_postsave.js`; `sprk_/scripts/access_ribbon.js` ← `src/client/webresources/js/sprk_access_ribbon.js`; publish |
| G-4 | Form libraries | project, matter, work assignment MAIN forms: `sprk_/scripts/bff_auth.js` FIRST, then `sprk_/scripts/assignedaccess_postsave.js`; OnLoad `Spaarke.AssignedAccess.onLoad` (pass execution context) |
| G-5 | Ribbon | per `infrastructure/dataverse/ribbon/AccessRibbons/README.md`: record the before-import command lists; export the three form ribbons (dedicated ribbon solution); check in the exported work-assignment `RibbonDiff.xml`; `Merge-AccessRibbon.ps1` per entity; import; verify every before-list command still renders and runs, Update Access visible to a Write-holder (cold cache too) and hidden for a Read-only user whose direct sync call gets 403 |
| G-6 | TrackingFieldTrio 1.0.34 | pack + import (`src/client/pcf/TrackingFieldTrio/Solution/pack.ps1`) |
| G-7 | Live gate criterion 21 (i)–(v) + the UX ui-tests | child-BU non-admin users only (#1081); fresh test records per owner T1 (b). (ii) needs a LINKED internal user with no prior Dataverse access to the chosen root — **if no such pair exists without relocating a user, escalation (j) fires**: ask the owner for the record/user pair |
| G-8 | After G-7 passes | set `ExternalAccess__AssignedAccess__JobRevokeOnChangeEnabled=true` on the dev BFF app settings (owner R3/(g)); record the date here |
| G-9 | ADR-034 A4 | owner's §6.5 acceptance; then the main session applies the concise `.claude/adr/ADR-034-user-record-membership.md` edit and the spec/design text (§8) with the PR |

Ordering note for **task 036**: 036's flag must not be turned on until G-1..G-4 are live and the job has completed a full
sweep (its ROTATING report clears) — that sweep IS the backfill.

## 8. ADR-034 amendment A4 (§6.5 path B) — PR description block

Drafted in full in `docs/adr/ADR-034-user-record-membership.md` § "Amendment A4 (2026-10-03, PROPOSED)", incl. the
spec.md (MUST NOT list, FR-32) and design.md §7 amendment text. Paste into the PR:

> 🔔 **ADR Conflict — Resolution Required**
> - **ADR in question**: ADR-034 User-Record Membership (A1 read-time conferral) + spec.md "❌ MUST NOT materialize derived
>   access into grant rows", FR-32 acceptance, design.md §7.
> - **Specific rule**: "MUST NOT materialize derived access into grant rows."
> - **Conflict**: owner round 2 Q5 requires the Assigned-To access to be a REMOVABLE entry on the grant-access list; a
>   read-time term is invisible on MDA (C9) and removable only by a veto (the No Access List — a different statement).
> - **Proposed path**: **B** — amendment A4: the registry gains a write-time consumer; one invariant owner
>   (`AssignedAccessMaterializer`) materializes Collaborate grants / POA shares with a provenance ledger; read-time
>   terms KEPT (A2 reversed).
> - **Rationale**: the rule is repo-wide via ADR-034; a project exception (A) leaves every later reader facing the
>   contradiction.
> - **Impact**: grants for Assigned subjects become ordinary, audited grant rows (FR-32 logs them through
>   `CreateGrantAsync`); FR-24/FR-25 unchanged.
> - **Alternatives rejected**: (A) project exception — repo-wide rule; (C) keep the read-time term + a per-record
>   "declined" veto — a second deny mechanism (§11), still invisible on MDA.
> - **Residual stated**: a non-product write waits ≤ 5 min; in the removal direction the job is report-only until
>   `ExternalAccess:AssignedAccess:JobRevokeOnChangeEnabled=true` (fail-open for fields cleared OUTSIDE the product only).

## 9. Residuals (stated, not hidden)

- **R-1** Non-product writes (grid edit, import, flow) wait for the job (≤ 5 min). Removal direction: report-only until
  G-8, so a field cleared outside the product keeps its auto grant indefinitely until then (fail-open in that direction
  only; the sync route and L1 writers always remove).
- **R-2** No per-root lease: a sync, an L1 call and the job can run on one root at once. Writes are idempotent (ledger
  alternate key → re-read + update; the grant core's upsert dedupes; share writes are unions), so races converge on the
  next pass; a revoke racing a re-assignment is corrected on the next pass.
- **R-3** Job scale: each candidate scan reads at most 4,999 rows per root type (more → TRUNCATED, run partial) and
  materializes 200 roots per run (ROTATING); more than 200 roots modified within 15 minutes, continuously, would starve
  the rotating window.
- **R-4** `sprk_grantedby` is empty for grants made by the AI-tool and field-mapping L1 triggers (as for the job):
  FR-33 reminders go to the record owner.
- **R-5** Declined is per subject on the root (§1).
- **R-6** The client `CANDIDATE_ROLE_FIELDS` omits two work-assignment columns (generic candidate list only).
- **R-7** The field-mapping push route has no record-level authorization (route sweep finding 67, medium, owned by the
  sweep's fix task). The L1 hook inherits it: a push writes the PARENT's own mapped values to its children and the
  materializer then grants the subjects those values name. No new hole — the push already writes as the app — but its
  effect now includes access; the sweep's fix closes both.
- **R-8** A contact whose internal user is not yet linked (141) receives a contact GRANT first; the job converts it to a
  share (Declined carried) once the link appears.
- **R-9** Coordination: tasks 064/066 should show Assigned-To provenance from `GET /api/v1/external-access/assigned-access`
  (source field + state), not re-derive it; task 087's FR-32 hooks see auto grants automatically (`CreateGrantAsync`).
- **R-10** (r1, verifier finding 9 — disproven, coordination kept) The wizard creates a secure project WITH
  `sprk_issecure = true` in the create payload (`CreateProjectWizard/projectService.ts:281-286`, pinned by
  `projectService.test.ts:116-132`), and `IsSecure` is read from that column alone (`ExternalParticipationService.FlagsFrom`,
  `RootFlagColumns`). So the job sees a secure record from the first instant and SUGGESTS (A3); provisioning changes
  ownership and storage, not the flag. **Coordination**: whoever applies round 2 item 2's lock on `sprk_issecure`
  (FLS + endpoint-only writes) must keep the record secure FROM ITS CREATE (e.g. secure it in the server create path);
  if the client create can no longer carry the flag, this window opens.
- **R-11** (r1, verifier finding 11) A `Shared` ledger row follows its user, not later link changes. Under the 141
  contract (`notes/141-link-contract.md` §2–§3, owner decision I2 = (1)) a link is never re-pointed and never cleared,
  `systemuser.sprk_primarycontact` is FLS-locked to the BFF, and a second user cannot link to a contact bound to
  another oid (a collision, refused and flagged) — so "the link moves to another systemuser" has no product path. A
  disabled user keeps the share (pinned: `ALinkedAssigneeWhoseUserIsLaterDisabled_KeepsTheShare_WithZeroWrites`):
  Dataverse refuses a disabled user's sign-in, and re-enabled, the still-assigned user should have it (owner round 7:
  inactive principals are read guards, no data repair). An operator hand-editing the locked link outside the product
  would leave the old user's share until the field changes.
- **R-12** (r1; **understated there, FIXED in r2** — verifier r2 finding 2) A RAISED manual grant whose assignment ends
  while the grant core refuses this grantee kind on the record cannot be put back. r1 named only Restricted and called it
  temporary; it is wider and can be permanent: an ORGANIZATION grant on a Secure or Limited record
  (`org_grant_direct_only_record` — and owner round 6: a secure child stays secure), any grant on a Restricted record
  (`record_restricted`), and a subject now on the No Access list (`grantee_denied`). Before r2 every sync and every job
  pass then failed with `restore-refused` indefinitely (`Success=false`; each form save got HTTP 500 `sync_incomplete`
  and the false "retried within 5 minutes" warning). Now (§12): those three refusals are a **hold**, not a failure — the
  outcome entry reads `Granted` / `restore-pending:{reasonCode}` / action `none`, nothing is written, the ledger row stays
  `Granted` (kept live on purpose: a `Revoked` row is never revisited, and the job's ledger scan skips roots whose rows
  are all `Revoked`, so the restore would be lost and the grant would come back at Collaborate when the policy changed),
  and the first pass after the policy allows it puts the level and date back. Nothing is exposed meanwhile (the read
  path suppresses the same grant on the same terms; the deny check fails closed on both paths). Residual: between the
  policy change and that pass the grant confers at Collaborate, not the operator's lower level — a form save or Update
  Access applies it at once (the post-save script syncs on every save), otherwise the job within 5 minutes (owner
  R3/R4 cadence). The core reports an unreadable deny list with the same `grantee_denied` code as an entry, so that case
  also holds rather than fails; it is retried every pass. An unreadable POLICY (`policy_unreadable`) is still a failure
  (pinned by the unreadable-policy twin, seed S11). The form shows nothing for a hold (no message names it).

## 10. Quality gates (Step 9.5)

**code-review** (coverage-first): 2 fixed — (W) an organization's auto grant revoked while its standing grant still
reached its members said nothing (criterion 17 gap for organization subjects) → `organization-members-standing-grant`
server + client copy + tests + bite M14/C5; (W) the `/unshare-user` marker was untested in isolation → test
strengthened, bite K2. Not changed, recorded: (W) field-mapping push inherits route finding 67 (R-7); (W) materializer
size / 13 ctor deps (cohesive; decomposition seed in §4); (S) grantor oid null for AI/field-mapping L1 (R-4); (S) job
rotation starvation under sustained churn (R-3); (S) no per-root lease (R-2); (I) Declined per subject (R-5).
**adr-check**: ADR-001/052/036 (job via `AddScheduledJob`, no timer), ADR-002 (no plugin; one server owner; WP-1..WP-6),
ADR-003 (every unreadable input writes nothing), ADR-007 (no Graph), ADR-008 (routes on the delegation-filtered group),
ADR-009 (tenant cache only), ADR-010 (no new interface; concretes registered), ADR-013 (no `IOpenAiClient` /
`IPlaybookService` outside AI), ADR-021 (Fluent v9 tokens only in the modal changes), ADR-028/A4 (no new
`.WithClientSecret`; client calls through the host's `authenticatedFetch`), ADR-032 (unconditional registrations),
ADR-038 (KEEP paths, no banned shapes) — compliant; NetArchTest 346/346 after the three path-C fixes in §5. Warnings: ADR-006 (two classic `.js` web resources — mandated by
the POML/UX amendment, thin, no UI; ADR-006 permits ribbon-command scripts); ADR-028 client contract (the web resources
send a Bearer token with `fetch` — through `Spaarke.BffAuth`, the established classic-form helper, as
`sprk_kpiassessment_quickcreate.js` does; not React code). **ADR-034: path B in flight** (A4 drafted; acceptance G-9).

## 11. Round r1 — verifier findings (2026-10-03, branch `task/uac-r2-142-r1`)

Each finding, what changed (or why nothing did), and the proof. Seeds and counts are in §5 ("Round r1 seeds").

| # | Finding | Disposition |
|---|---|---|
| 1 | DEFECT (high): an expired grant counted as covering — `QueryActiveRowsAsync` filters on statecode only, and expired rows stay at statecode 0; P1/P2 left an assigned contact `CoveredByExisting` with no access, permanently | **Fixed.** A grant covers only while it CONFERS access: `Conferring()` = `ExternalParticipationService.ConfersAccessOn`, the read filter's own predicate (no private copy). `FreshAsync`'s covering check and `ContinueCoveredAsync`'s re-check use it. A covering grant that is still there but LAPSED is a known cause (expiry, owner (e)/A5) — decided fresh, which gives the still-assigned subject Collaborate again; a covering grant DEACTIVATED is still an operator's removal (Declined). Tests: P1, its expiring-today twin (the date itself still confers), P2, its deactivated twin. Seeds S1 (3), S2 (1), S2b (1) |
| 2 | DEFECT (medium, ADR-003): `FreshAsync` and the restore path ignored the core's `expired_not_restored` Warning (P3: "raised" over a grant that confers nothing) | **Fixed at the cause and guarded at the symptom.** Cause: when rows exist on the key but NONE confers, the rule asks the core for an explicit `DefaultExpiry(today)` (today + 90 — the date renewal writes; no conferring date exists to shorten), so the core never warns there; a lapsed row's level is not a "prior" to put back (it conferred nothing), so the end of the assignment removes the access instead of resurrecting an expired manual grant. Guard: a Warning on the fresh path is a failure (`grant-not-conferring`), never `Granted`, and the ledger is untouched so the next pass decides again (reachable only when a row lapses between the rule's read and the core's — tested through a `GrantTable.BeforeGrantQuery` hook). On the restore path, a Warning means the raised grant had lapsed: the level is put back, recorded `Revoked(prior-level-restored-lapsed)` with action `ledger` (no access was restored, none is reported; nothing to retry). New reason code added to `AssignedAccessReason` and the ledger schema doc. Tests: P3, the read race, the lapsed restore. Seeds S3 (4), S4 (1), S5 (1) |
| 3 | UNPROVEN GUARD: share `stillNamedElsewhere` | **Test added** (`ClearingOneField_DoesNotRemoveTheShare_WhenAnotherRegistryColumnStillNamesTheLinkedAssignee`, incl. a following job pass — no Declined). Verifier seed J now fails 1 |
| 4 | UNPROVEN GUARD: share `written != current` (modified mask) | **Test added** (theory: widened to Full Access and narrowed to Read through the OOB dialog, which marks nothing Adopted). Seed F fails 2 |
| 5 | UNPROVEN GUARD: S5 never removes an auto share on a secure record | **Test added** (`ClearingTheField_OnASecureRecord_NeverRemovesTheAutoShare_OwnerS5`). Seed G fails 1 |
| 6 | UNPROVEN GUARD: R2 inactive organization = known cause | **Test added** (org grant deactivated with its organization → `Skipped(subject-inactive)`, never Declined; reactivated → granted again). Seed D fails 1. The §5 "beyond the contract" claim is now true |
| 7 | MERGE PRECONDITION: 133/137/143 commits (two WIP) not in `work` | **Not closable by this agent** (no merges into `work/*`). Verified: `b38756ba6` is an ancestor of `task/uac-r2-133-b2` / `-b2-r2`; `33108909e` of `task/uac-r2-137-b2`; none of 133-b2-r2 / 137-b2 / 143-r2 is in `work`. The exact merge order, incl. the 137×143 semantic-conflict fix `843d62b46`, is gate **G-0** (§7) |
| 8 | MERGE PRECONDITION: ADR-034 A4 only PROPOSED; no PR yet | **Not closable by this agent** (owner acceptance; sub-agents cannot write `.claude/`; no PRs). The path-B block is ready to paste (§8); gate **G-9** |
| 9 | LOW (race): wizard secure project "created non-secure, secured afterwards" | **Disproven from code.** The create payload carries `sprk_issecure = true` (`projectService.ts:281-286`, pinned by `projectService.test.ts:116-132`), and `IsSecure` is that column alone (`ExternalParticipationService.FlagsFrom`). The job therefore sees a secure record from its first instant and SUGGESTS (A3); provisioning changes owner and container, not the flag. Coordination kept as R-10 (round 2 item 2's future lock on `sprk_issecure`) |
| 10 | LOW: Restricted after Granted untested | **Test added** (kept, not renewed inside the window, read-time suppression flag asserted; renewed once Standard again). Seed R (the materializer's renewal guard alone) does NOT bite — the grant core's own task-138 Restricted refusal is a second layer; seed R2 (both) fails 1. Found while pinning it: R-12 (a raised grant cannot be put back while Restricted — reported, not changed) |
| 11 | LOW: Shared rows ignore later link/eligibility changes | **Premise disproven for links; eligibility pinned.** The 141 contract never re-points or clears a link and refuses a second link to a contact bound to another oid, so there is no product path by which "the link moves" (R-11). A disabled user keeps the share with zero writes — pinned (`ALinkedAssigneeWhoseUserIsLaterDisabled_KeepsTheShare_WithZeroWrites`); seed E (re-decide a Shared row whose user turned ineligible) fails 1 |
| 12–13 | Verified-OK / criterion status | No action |
| 14 | criterion 8 not met | Met — finding 1 |
| 15 | criterion 9 not met | Met for grants — finding 1 (lapse = known cause) and finding 6 (R2 proven) |
| 16 | criterion 10 not met for shares | Met — findings 3, 4, 5 |
| 17 | criterion 19 not met | Met — every guard named now has a biting test (§5) |
| 18 | criterion 18 partial | Still partial — G-9 owner acceptance and the PR description (main session) |
| 19 | ADR-003 (raise/restore over a Warning) | Met — finding 2 |

**Not changed** (scope): R-12 (restore refused while Restricted) is reported for the next round; the wizard's step-1e
comment ("synced only now, after provisioning, so the rule sees the secure flag") over-explains (the flag is there at
create) but is harmless and was left as is.

**Round r1 runs.** AssignedAccess server set **118/118** (104 + 14 new). `Spaarke.ArchTests` **346/346**. Full BFF unit
suite (once, at the end): **14,600 passed, 0 failed, 54 skipped (14,654)**, 17m39s — the previous 14,640 plus the 14 new
tests. BFF build 0 errors / 0 warnings. No client file changed in this round (no client build/test needed). No new
service, DI registration, endpoint, option, job, column or package — one reason-code constant
(`AssignedAccessReason.PriorLevelRestoredLapsed`) and one test-double hook (`GrantTable.BeforeGrantQuery`); publish size
left to the main session.

## 12. Round r2 — verifier findings (2026-10-03, branch `task/uac-r2-142-r2`)

| # | Finding | Disposition |
|---|---|---|
| 1 / 11 | DEFECT (medium-high, criterion 10, ADR-003): a raised MANUAL grant renewed by the rule (A5) was "restored" at the end of the assignment with the level only (`WriteGrantAsync(priorLevel, expiry: null)` — the core keeps the survivor's date), so the operator's View Only grant kept the rule's today + 90 (probe: chosen 2026-10-13, ended 2027-01-01) and was reported `prior-level-restored`. The ledger never recorded the earlier date; the `:844` comment ("never extend someone else's live grant") contradicted the renewal | **Fixed (the verifier's first option).** A raise now records the earlier level AND date — `raised-from:{level}@{yyyy-MM-dd}` (`AssignedAccessReason.RaisedFromLevel`; the highest conferring level and the latest conferring date; every conferring row here is below Collaborate and carries a date, since `null` confers nothing). The end of the assignment writes BOTH back through the grant core (an explicit lower request with an explicit date — not narrowed, so no never-lower refusal). If that date has passed, the core writes it and answers its ADR-003 warning: recorded `Revoked(prior-level-restored-lapsed)`, action **`revoked`** when the grant still conferred before the write (the rule's renewal had kept it alive — putting the date back ENDED the access) or `ledger` when it had already lapsed (r1's case); the materializer then clears the grantee's cached grant set itself, because the core returns before its own invalidation on that path. Renewal while assigned is unchanged (rule 5: an assigned subject holds Collaborate). A reason without the date (never written to a live ledger — the table is not deployed) is not restorable exactly and takes the removal path (fail closed). The `:844` comment and the class remarks now say what the code does. Tests: `ARaisedGrantRenewedWhileAssigned_IsPutBackToTheOperatorsLevelAndDate_WhenTheAssignmentEnds` (the probe scenario: ends at ViewOnly + 2026-10-13), `ARaisedGrantRenewedPastTheOperatorsDate_EndsWithTheAssignment_ReportedRevoked_NeverRestored` (no access outlives the operator's date; cache cleared); r0's raise test now also asserts the restored date. Seeds S6 (2), S7 (10), S8 (1), S9 (1), S9b (1) |
| 2 / 12 | DEFECT (medium; R-12 understated): a raised manual ORGANIZATION grant whose assignment ends on a Secure/Limited record failed `restore-refused` on every pass indefinitely (`org_grant_direct_only_record`; secure stays secure, owner round 6) — job `Success=false` for good, HTTP 500 `sync_incomplete` on every form save with a false "retried within 5 minutes" warning | **Fixed (the verifier's suggestion, with one correction).** The grant core's refusals that state the record's CURRENT policy — `record_restricted`, `org_grant_direct_only_record`, `grantee_denied` (`IsPolicyHold`) — are a hold, not a failure: the entry reads `Granted` / `restore-pending:{reasonCode}` (`AssignedAccessReason.RestorePendingPrefix`) / action `none`, nothing is written, `Complete` stays true (job green, no 500, no warning). The correction: the row is NOT settled `Revoked` — a `Revoked` row is never revisited by the ended-assignment loop and the job's ledger scan skips roots whose rows are all `Revoked`, so the restore would be lost and the grant would confer at Collaborate again when the policy changed (fail-open). The row stays `Granted`, every pass retries, and the first pass after the policy allows it restores level and date. `policy_unreadable` stays a failure (ADR-003). R-12 rewritten (§9) with the true scope and the residual (≤ one pass at Collaborate after the policy relaxes). Tests: the theory `ARaisedGrantWhoseRestoreThePolicyForbids_WaitsWithoutFailing_AndIsPutBackOnceThePolicyAllows` — organization on Secure, on Limited, on Restricted; contact on Restricted; contact on the No Access list — each: two passes complete with zero writes, then restored once allowed; twin `ARestoreRefusedBecauseThePolicyCouldNotBeRead_IsStillAFailure_TheTwinOfThePolicyHold` (the core's flag read faults through the existing `BeforeGrantQuery` hook). Seeds S10 (5), S11 (1) |
| 3 / 13 | LOW: a lapsed key at FULL access is rewritten at Collaborate (contradicts "never downgrades a level" literally; not recorded) | **Recorded as a deviation (the verifier's second option), §6**, with the code comment beside the write and a pinning test (`AnExpiredFullAccessGrant_IsGivenCollaborate_TheRecordedDeviation_ItConferredNothing`, seed S12). The first option is not available: the rule's ceiling (A1) cannot write Full, the core refuses a narrowed request over a higher row, and renewing at Full would grant above the rule's level. No access is lowered (none → Collaborate) |
| 4 | LOW (POML hygiene): `<status>completed</status>` while criteria 18, 20 (publish size), 21 and steps 10–11 are open | **Fixed**: `<status>in-progress</status>` plus a `<status-note>` naming exactly what is open (TASK-INDEX already shows 🔲 [open]; not edited per the harness) |
| 5–9 | Verified OK | No action |
| 10 | MERGE PRECONDITIONS G-0 (133/137/143 commits incl. WIP `b38756ba6`, `33108909e`; the 137×143 fix `843d62b46`) and G-9 (ADR-034 A4 PROPOSED) | **Not closable by this agent** (no merges into `work/*`; owner acceptance). Unchanged: §7 G-0, G-9 |
| 14 | criterion 18 (ADR-034 A4 owner §6.5 acceptance; concise `.claude/adr` edit; PR path-B block) | **Not closable by this agent** — owner gate G-9; sub-agents cannot write `.claude/`; no PRs. Draft and §8 block ready |
| 15 | criterion 20 (publish size) | **Not closable here** — the main session measures (harness). Build, suites, ArchTests below; no package added (no CVE delta) |
| 16 | criterion 21 (live gate G-7; UX live items (a)(b)(c)(e)(f) incl. the work-assignment ribbon export check-in, G-5) | **Not closable by this agent** (live writes are the main session's). Unchanged: §7 G-5, G-7 |

**Round r2 surface**: no new service, DI registration, endpoint, option, job, column or package. Two `AssignedAccessReason`
members (the `RaisedFromLevel` formatter and the outcome-only `RestorePendingPrefix`), two private materializer helpers
(`TryParseRaisedGrant`, `IsPolicyHold`), the `sprk_reason` value format (`raised-from:{level}@{date}` — same Text 100
column, no schema change; the script's column description and `entity-schema.md` updated), 9 tests. The PROPOSED
ADR-034 A4 draft's never-lower bullet now says "level AND date" and names the expired-grant case (finding 3). No client
file changed. Live writes: none.

**Self-review (Step 9.5 scope, this round's diff only)**: ADR-003 — a hold is granted only for refusals that STATE the
record's policy; an unreadable policy stays a failure (twin + seed S11), and a restore whose date has passed ends access
and clears the cache rather than leaving it served (seed S8). ADR-036 A1 — a held restore is reported as such
(`restore-pending:{code}`), never as done and never as a failure it is not. ADR-002 / D-1 — no plugin, the same three
triggers. ADR-038 — tests run the production materializer and grant core between module-boundary doubles; no banned
shape; each guard bites. CLAUDE.md §10/§11 — no new surface beyond the members listed above.

**Round r2 runs.** AssignedAccess server set **127/127** (118 + 9 new). The verifier's affected BFF set (AssignedAccess,
GrantLifecycleCharacterization, InternalUserShare, GrantorCeiling, RecordCreation, DelegationRule, NoAccess) **524/524**
(515 + 9). `Spaarke.ArchTests` **346/346** (after the last code change, and again after the last doc change). Full BFF
unit suite (once, at the end): **14,609 passed, 0 failed, 54 skipped (14,663)**, 17m46s — the previous 14,654 plus the 9
new tests. BFF build 0 errors / 0 warnings. No client file changed (no client build/test needed). Publish size left to
the main session.
