# Task 142 — Assigned-To auto-grants (#1065)

> Branch `task/uac-r2-142` (from `task/uac-r2-143-r2` + `task/uac-r2-137-b2` + `work/unified-access-control-r2`, base
> `ee925b903`). Server, client, web resources, ribbon source and schema script complete. **Live writes: none** — every
> live step is a pending manual gate (§7). The ADR-034 amendment A4 is drafted and **awaits the owner's §6.5 acceptance**
> (§8); the code merges with it, never before.

## 1. Owner answers applied (no escalation fired as a stop)

| POML trigger | Answer (source) | Applied as |
|---|---|---|
| (a) Secure | A3 = prompt; existing auto grants KEPT when a record becomes secure (round 3, accepted as recommended) | Secure root: contact grant / POA share → ledger `PendingConfirmation`, shown in Manage Access "Suggested Access" naming the source field, **Grant** (normal `/grant` at Collaborate, or `/share-user` for a linked user) → `Adopted`, **Dismiss** (`POST /assigned-access/dismiss`) → `Declined`. A grant that existed before the record became secure stays `Granted`; an auto share is never removed on a secure record by the rule (S5) |
| (b) Changed/cleared | A4 = revoke the unmodified auto access (round 3) | `EndAssignmentAsync`: revokes only `Granted`/`Shared` rows whose level/expiry (mask) still equal what the owner wrote, not when another registry column still names the subject, never `Adopted`/`Declined`. A raised manual grant/share is put back to its prior level (`raised-from:` / `raised-from-mask:`) |
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
| `tests/integration/auth/UnifiedAccessControl/AssignedAccessMaterializerTests.cs` | 55 | criteria 2–11, 16 (conversion, renewal, cache key), A6, inline gating |
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

## 7. Pending manual gates (main session; live writes — exact commands)

| # | Gate | Command / action |
|---|---|---|
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
