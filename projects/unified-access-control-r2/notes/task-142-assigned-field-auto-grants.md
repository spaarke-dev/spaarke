# Task 142 — Assigned-To auto-grants (#1065)

> Branch `task/uac-r2-142` (from `task/uac-r2-143-r2` + `task/uac-r2-137-b2` + `work/unified-access-control-r2`, base
> `ee925b903`); verifier fix rounds **`task/uac-r2-142-r1`** (§11), **`task/uac-r2-142-r2`** (§12),
> **`task/uac-r2-142-r3`** (§13), the owner-round-13 fix round **`task/uac-r2-142-r4`** (§14), the r4 verifier's fix
> round **`task/uac-r2-142-r5`** (§15) and the round-18 / r5-verifier fix round **`task/uac-r2-142-r6`** (§16; R-14
> closed — the No Access reader evaluates any subject set). Server, client, web
> resources, ribbon source and schema script complete. **Live writes: none** — every live step is a pending manual gate
> (§7). The ADR-034 amendment A4 was **ACCEPTED by the owner in round 11 (2026-10-03)** (§8); the main session applies the
> concise `.claude/adr` edit (exact text: §13.4) with the PR, and the code merges only after the dependency merges in §7
> G-0. The write-path invariant row is **I-12** in `DATAVERSE-WRITE-PATH-ARCHITECTURE.md` (master took I-11 for record
> numbering; renumbered at integration).

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
| (j) Live gate user/root pair | — | Not reached (live gates are the main session's). It may fire at criterion 21 (ii) — see §7. Since round 11 a child-BU test user exists (`uac.child.user@demo.spaarke.com`, Spaarke Business Unit 1, Basic + Core User, no shares — owner note, "Test user"), so (j) need not fire if that user can be linked (141) and a NON-secure root it cannot open is found (§7 G-7) |
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
| `tests/integration/auth/UnifiedAccessControl/AssignedAccessMaterializerTests.cs` | 83 (55 + 14 in round r1 + 9 in round r2 + 5 in round r3) | criteria 2–11, 16 (conversion, renewal, cache key, Restricted after a grant), A6, inline gating; r1: expired grants never cover (P1/P2 + twins), never "granted"/"restored" over a lapsed grant (P3, the read race, the lapsed restore), the share half of criterion 10 (other field, modified mask, S5), the R2 known cause, a disabled linked user |
| `tests/integration/auth/UnifiedAccessControl/AssignedAccessSyncEndpointTests.cs` | 12 | criterion 12 THROUGH the real `/api/v1/external-access` filter pipeline (Write → handler; no Write and unknown id → the same filter 403; 401; body subject ids ignored; ProblemDetails with reason codes; list + dismiss gated; cache key = caller tenant) |
| `tests/integration/auth/UnifiedAccessControl/AssignedAccessMarkerTests.cs` | 13 | criteria 6, 9, 17 through the production `/revoke`, `/unshare-user`, `/grant`, `/share-user` handlers |
| `tests/unit/Sprk.Bff.Api.Tests/Services/ExternalAccess/AssignedAccessReconciliationJobTests.cs` | 16 (14 + a 2-row theory in round r3) | criterion 16 (sweep, idempotent second run, switch on/off, cadence/enabled values, faulted scan → Success=false, TRUNCATED, ROTATING + recent-first, deployment-tenant cache key, no tenant, incomplete root, link conversion) |
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

Round r3 seeds (each alone, applied by exact string replacement through a scratch script; build + the now **134**
AssignedAccess tests; restored from a backup with the MD5 re-checked after every restore, and touched; §13):

| Seed | Failed |
|---|---|
| V2 the raise records the EARLIEST conferring date (`conferring.Max(r => r.ExpiresDate)` → `.Min(…)`, the verifier's r2 seed) | 1 — the new two-row test (0 in r2) |
| T1 the grant core answers a THROWING No Access check with the plain `GranteeDenied` (the r2 behaviour) | 4 — restore, fresh grant, renewal, job (throws) |
| T2 `IsPolicyHold` ignores the fault flag | 1 — the restore test |
| T7 the restore path's explicit fault branch removed (a generic `restore-refused`, kind not distinct) | 1 — the restore test |
| T3 the secure-suggestion check's throw reads as an entry again (warning + `denied = true`, the r2 behaviour) | 1 — the secure-suggestion test |
| T4 a renewal refused by the fault is only a warning | 1 — the renewal test |
| T5 the fresh path reads the fault as `no-access` | 2 — the fresh-grant test, the job (throws) |
| T6 the job does not count the fault | 1 — the job (throws) |

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
| G-0 | **Dependency merges FIRST** (verifier r0 finding 7). This branch carries the non-merge commits of tasks 133, 137 and 143 that `work/unified-access-control-r2` does not have yet, including two `WIP … UNVERIFIED, do not merge` commits — `b38756ba6` (133-b1, an ancestor of `task/uac-r2-133-b2` / `-b2-r2`) and `33108909e` (137-b1, an ancestor of `task/uac-r2-137-b2`), each superseded inside its own verified line. Merging 142 first would bring them in unreviewed. | Into `work/unified-access-control-r2`, in order: `task/uac-r2-133-b2-r2` (`5a8b66c15`), `task/uac-r2-137-b2` (`a8fe5b428`), `task/uac-r2-143-r2` (`7668bbc1f`) — each verified in its own round. ⚠️ 137-b2 + 143-r2 have a SEMANTIC conflict with no textual one (143 r1 added `IContactIdentityStore` to `AccessibleRecordSetService`'s constructor; 137's seam test used the old one): bring `843d62b46` (`tests/integration/seam/ExternalAccess/UnifiedEvaluatorSeamTests.cs`, +3 lines) with the second of the two merges, or the test project does not compile. Then re-merge `work` into the 142 line and merge it. Confirm with `git log --oneline work/unified-access-control-r2..task/uac-r2-142-r3 --no-merges` = only 142 commits. **Re-checked in round r3** (`git merge-base --is-ancestor`, work at `3850eda5a`): none of `task/uac-r2-133-b2-r2`, `task/uac-r2-137-b2`, `task/uac-r2-143-r2` or `843d62b46` is an ancestor of `work` yet; still open — the main session's integration order. **Re-checked in round r5** (work at `e6dd48b43`): still none of the four is an ancestor; still open. **Re-checked in round r6** (work at `e1dd17dcf`): still none of the four is an ancestor; still open |
| G-1 | Ledger schema, BEFORE any BFF deploy of this branch (without it every materialization reads `ledger-unreadable` and writes nothing — fail closed) | `pwsh scripts/Set-AssignedAccessLedgerSchema.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com` (dry run), then `-Apply`, then `-Verify` (exit 0) and `describe('tables/sprk_assignedaccess')`; confirm `sprk_AssignedAccessLedgerKey` Active and the privilege census lists only System Administrator / System Customizer for Create/Write/Delete. Record in `src/solutions/SpaarkeCore/entities/sprk_assignedaccess/entity-schema.md` |
| G-2 | BFF deploy | the usual BFF deploy of the merged branch (job registers enabled; `ExternalAccess__AssignedAccess__JobRevokeOnChangeEnabled` absent = report-only) |
| G-3 | Web resources | dataverse-deploy: `sprk_/scripts/assignedaccess_postsave.js` ← `src/solutions/webresources/sprk_assignedaccess_postsave.js`; `sprk_/scripts/access_ribbon.js` ← `src/client/webresources/js/sprk_access_ribbon.js`; publish |
| G-4 | Form libraries | project, matter, work assignment MAIN forms: `sprk_/scripts/bff_auth.js` FIRST, then `sprk_/scripts/assignedaccess_postsave.js`; OnLoad `Spaarke.AssignedAccess.onLoad` (pass execution context) |
| G-5 | Ribbon | per `infrastructure/dataverse/ribbon/AccessRibbons/README.md`: record the before-import command lists; export the three form ribbons (dedicated ribbon solution); check in the exported work-assignment `RibbonDiff.xml`; `Merge-AccessRibbon.ps1` per entity; import; verify every before-list command still renders and runs, Update Access visible to a Write-holder (cold cache too) and hidden for a Read-only user whose direct sync call gets 403 |
| G-6 | TrackingFieldTrio 1.0.34 | pack + import (`src/client/pcf/TrackingFieldTrio/Solution/pack.ps1`). The checked-in bundle was rebuilt in round r6 (`build:prod`; it differs from r0's only by the new `no_access_unverifiable` code in the modal's policy-code set, §16). 1.0.34 has never been imported, so no version bump; if it HAS been imported anywhere before this gate, bump to 1.0.35 first (Dataverse keeps a same-version control) |
| G-7 | Live gate criterion 21 (i)–(v) + the UX ui-tests | child-BU non-admin users only (#1081); fresh test records per owner T1 (b). (ii) needs a LINKED internal user with no prior Dataverse access to the chosen root — **if no such pair exists without relocating a user, escalation (j) fires**: ask the owner for the record/user pair. Candidate since round 11: `uac.child.user@demo.spaarke.com` (systemuser `d6f8f439-40bf-f111-a05b-3833c5e9614d`, Spaarke Business Unit 1, Basic + Core User, no shares). Before the save: link it (141 first sign-in, or the 141 link job), choose a NON-secure root (on a secure root A3 suggests instead of sharing) and record `RetrievePrincipalAccess` = None for that user on it; a root its role depth reaches does not qualify |
| G-8 | After G-7 passes | set `ExternalAccess__AssignedAccess__JobRevokeOnChangeEnabled=true` on the dev BFF app settings (owner R3/(g)); record the date here |
| G-9 | ADR-034 A4 | **Owner's §6.5 acceptance: DONE** (round 11 item 1, 2026-10-03; `docs/adr` marked ACCEPTED in round r3). Remaining (main session, with the PR): apply the concise `.claude/adr/ADR-034-user-record-membership.md` edit — exact text in §13.4 — plus its `.claude/CHANGELOG.md` entry; apply the spec/design amendment text (`docs/adr` A4 § "Spec / design amendment text"); paste the §8 block into the PR description |

Ordering note for **task 036**: 036's flag must not be turned on until G-1..G-4 are live and the job has completed a full
sweep (its ROTATING report clears) — that sweep IS the backfill.

## 8. ADR-034 amendment A4 (§6.5 path B) — PR description block

Drafted in full in `docs/adr/ADR-034-user-record-membership.md` § "Amendment A4 (2026-10-03, accepted)" — **ACCEPTED by
the owner in round 11, item 1 (2026-10-03)** — incl. the spec.md (MUST NOT list, FR-32) and design.md §7 amendment
text. Paste into the PR:

> 🔔 **ADR Conflict — Resolved (§6.5 path B, owner-accepted 2026-10-03, round 11 item 1)**
> - **ADR in question**: ADR-034 User-Record Membership (A1 read-time conferral) + spec.md "❌ MUST NOT materialize derived
>   access into grant rows", FR-32 acceptance, design.md §7.
> - **Specific rule**: "MUST NOT materialize derived access into grant rows."
> - **Conflict**: owner round 2 Q5 requires the Assigned-To access to be a REMOVABLE entry on the grant-access list; a
>   read-time term is invisible on MDA (C9) and removable only by a veto (the No Access List — a different statement).
> - **Path**: **B** (ACCEPTED by the owner, round 11 item 1) — amendment A4: the registry gains a write-time consumer; one invariant owner
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
  effect now includes access; the sweep's fix closes both. **Coordination (round r3, verifier r2 finding 11)**: finding 67
  is task **166**'s F11 (#1105; `tasks/166-…poml` F11 + its F11 criterion — caller probe on the source first, the child
  query AS the caller through `IImpersonatedCommunicationQuery`, every child write with `impersonateSystemUserId`). No
  change here. For 166 / the integrator: (1) both tasks edit `Api/FieldMappings/FieldMappingEndpoints.cs` — 142's L1 hook
  is the `RunAfterWriteAsync(scopes, targetEntity, childRecordId, updatePayload.Keys, grantorOid: null, …)` call right
  after `UpdateRecordFieldsAsync` in `ApplyMappingsToChildRecordsAsync` (~`:726`), and the handler passes its
  `IServiceScopeFactory` (~`:439`, `:563`); 166's rewrite must keep that call after each SUCCESSFUL child update (its
  writer test `AssignedAccessWriterTriggerTests.FieldMappingPush_ThatWritesAnAssignedColumnOfARootChild_MaterializesIt`
  fails if it is dropped, and calls `ApplyMappingsToChildRecordsAsync` directly, so a signature change there updates it
  too), expect a textual conflict there at integration,
  and resolve it keeping both; (2) once 166 lands, the push acts only on children the caller can write, so R-7 closes with
  it; (3) optional for 166: the push then has a caller, so the hook's `grantorOid: null` could pass the caller's oid
  (closes R-4 for this trigger) — not required by either task.
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
  R3/R4 cadence). ~~The core reports an unreadable deny list with the same `grantee_denied` code as an entry, so that case
  also holds rather than fails~~ — **fixed in r3 (§13.2)**: a No Access check that THROWS is a fault
  (`deny-list-unreadable`, `Success=false`), never a hold; faults the deny-veto code absorbs itself are R-13. An
  unreadable POLICY (`policy_unreadable`) is still a failure (pinned by the unreadable-policy twin, seed S11). The form
  shows nothing for a hold (no message names it).
- **R-13 — CLOSED in r4 (§14.1; owner round 13 item 4: "fix now").** The deny-veto check now answers a tri-state
  (`NoAccessCheckAnswer` Allowed / Denied / Unverifiable); every fault below is Unverifiable and reported as a
  `deny-list-unreadable` failure (job red), and the grant routes answer it 503 `no_access_unverifiable`. The r3 text,
  kept for the record: (r3) Only a No Access check that THROWS is distinguishable as a fault. The deny-veto code
  (`AccessibleRecordSetService.ResolveDenyVetoAsync` / `IsGranteeDeniedOnRecordAsync`) ABSORBS other read faults — the
  contact's memberships unreadable, a referenced-organization read unreadable, the deny-list reader's own `null`, any
  non-cancellation exception — into "denied" (fail closed) and returns a plain `bool`, so they still reach the
  materializer as `grantee_denied`: a restore HOLDS (green) and a fresh grant is `Skipped(no-access)`. Nothing is exposed
  (fail closed on both paths) and each is retried every pass; monitoring sees them only through the veto code's own
  error lines (`[WF-AUTHZ] Deny-veto resolution FAILED …` / `… cannot proceed …`), not in the job result. Closing it means
  a tri-state answer from `IsGranteeDeniedOnRecordAsync` (or a fault-reporting overload) — an interface change to
  `AccessibleRecordSetService`, which this POML scopes out (relevant-files: "ONLY per the owner's answer to escalation
  (d)" = keep → untouched) and which tasks 139/140 also call. Not changed; reported for the owner/main session.
- **R-14 — CLOSED in r6 (§16; round 18 item 1, 2026-10-03: neither (A) accept nor (B) a non-retryable code — fix the
  CAUSE, option (C)).** `NoAccessListReader` now evaluates a subject set of ANY size: the subject side is chunked within one
  query's bound (5 contacts / 25 organizations), one query per subject chunk × object chunk, the matches unioned; the
  deterministic "too large → FailedClosed" answer is gone, so the grant routes no longer answer a permanent 503, the job is
  no longer red for as long as such a subject stays assigned, and the read path no longer hides every record from such a
  contact. A genuine read fault in ANY chunk still fails the whole answer closed (Unverifiable). The r5 text, kept for the
  record: (r5) a subject the deny-list reader will not EVALUATE is classified as a transient fault (§15.4).
- **R-15 (r6, stated)** Query volume now scales with the subject set: `ceil(max(contacts/5, organizations/25))` subject
  chunks × the object chunks. No ceiling, by round 18's decision ("evaluate any subject set"); every query stays within
  NFR-02's per-query bound. A run with more than one subject chunk logs `[NO-ACCESS] Evaluating … in N subject chunks`
  (Information), so an unusually large set is visible. The register's own estimate is "a small handful" of organizations
  per contact (C-5).

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
`sprk_kpiassessment_quickcreate.js` does; not React code). **ADR-034: path B** (A4 drafted; **accepted by the owner in round 11**, 2026-10-03 — the concise edit is the main session's, §13.4).

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

## 13. Round r3 — the r2 verifier's open items + owner round 11 (2026-10-03, branch `task/uac-r2-142-r3`)

Base: `task/uac-r2-142-r2` (`aa7ab9cbf`) plus two merges of `work/unified-access-control-r2` (`6b243f092`, then
`3850eda5a`; docs, POMLs, the owner note and the batch-4 integration checklist only — no conflict, nothing resolved) =
**`655450ab0`**. Binding input: owner rounds 1–11 (round 11 item 1: **ADR-034 A4 ACCEPTED**) and the "Peer report: #1081"
section, read from `work/unified-access-control-r2`. Live writes: none.

### 13.1 The verifier's items (`b4c-findings.json` key "142")

| # | Verifier item | Disposition |
|---|---|---|
| 1 | The r2 code fixes (findings 1, 2) are correct and proven | Verified OK — no action |
| 2 | Its seed V1 (policy hold settles `Revoked`) bites 5 | No action |
| 3 | LOW, UNPROVEN GUARD: `priorExpiry = conferring.Max(…)` — seed V2 (`Min`) failed 0 of 127 | **Closed — §13.3** (V2 now fails 1) |
| 4 | LOW, MONITORING GAP: a No Access check that THROWS reads as `grantee_denied`, so a restore is a green `restore-pending` | **Closed — §13.2** |
| 5 | Merge precondition G-0 (133/137/143 not ancestors of `work`) | **Recorded, not closable here** — the main session's integration order. Re-checked this round (§7 G-0): still open |
| 6 | Merge precondition G-9 / criterion 18 (A4 PROPOSED; no concise edit; no PR block) | **Owner accepted A4 (round 11 item 1).** `docs/adr` marked ACCEPTED (§13.4); the concise `.claude/adr` text is §13.4; the PR block is §8. Criterion 18 is met apart from the main session's `.claude` edit and the PR text |
| 7 | Independent re-runs at `aa7ab9cbf` | No action |
| 8 | POML well-formed; status-note correct | Status-note and execution block updated this round |
| 9 | No live write (G-1 not applied) | No action — G-1 is the main session's (round 11 approved it) |
| 10 | Peer report #1081 consistent with the gate plan | No action |
| 11 | Coordination: route-sweep finding 67 (field-mappings push) now also confers access | **Recorded — §13.5 / R-7** (task 166 F11, #1105); no change here |
| 12 | Finding-3 deviation accepted as an owner-revisit item | No action |
| 13 | Criterion 18 not met | As item 6 |
| 14 | Criterion 20 (part): publish size not measured | **Pending — main session** (harness: skip publish size). No package added |
| 15 | Criterion 19 (minor): V2 survived | **Met** — §13.3 |
| 16 | Criterion 21: live gate G-7 + UX live items (a)(b)(c)(e)(f), G-5 | **Pending — main session** (live writes). §7 G-7 now names the round-11 child-BU test user as the (ii) candidate |
| esc (j) | May fire at 21 (ii) | Not reached; likely avoidable with the round-11 test user (§1, §7 G-7) |

### 13.2 Item 3 — a No Access check that THROWS is a fault, never an entry or a policy hold

**The defect (verifier r2 finding 4).** `GrantExternalAccessEndpoint.CheckGrantAsync` turned a THROWING No Access check
(an HttpClient timeout, which the deny-veto code rethrows) into `GrantPolicyDecision.GranteeDenied` — the code an ENTRY
on the list gets. `IsPolicyHold` treated that code as the record's policy, so a deny-list read outage during a restore
was a green, non-failing `restore-pending:…grantee_denied` pass; on the fresh path it was `Skipped(no-access)`; on a
renewal a warning; on a secure suggestion (the materializer's own check) `Skipped(no-access)`. Nothing was exposed
(every path failed closed) but a sustained outage was invisible in the job result.

**The fix.**
- **Grant core** (`GrantExternalAccessEndpoint.cs` catch at `:605`): returns
  `GrantPolicyDecision.GranteeDenyListUnreadable` = `GranteeDenied with { IsDenyListReadFault = true }`
  (`ExternalGrantLifecycle.cs`). **The wire contract is unchanged** — same reason code, 422 and detail ("… or that list
  could not be checked"), so `/grant`, `/invite-and-grant` and task 140 answer exactly as before (pinned by task 139's
  `GrantorCeilingTests.Grant_WhenTheNoAccessCheckThrowsATimeout_Is422GranteeDenied_AndWritesNothing`, still green). The
  flag is in-process only (`PolicyRefusalProblem` maps code, status and detail explicitly).
- **Materializer** — the four places a No Access check result is consumed:
  - restore (the verifier's case): `IsPolicyHold` excludes the fault; the fault is reported, nothing written, the row
    stays `Granted` so every pass retries;
  - fresh grant: reported, `Skipped(no-access-unverifiable)` (not `no-access` — nobody is known to be on the list),
    re-decided next pass;
  - secure suggestion (its own `IsGranteeDeniedOnRecordAsync` call): reported, not suggested,
    `Skipped(no-access-unverifiable)`;
  - renewal: reported, not renewed this pass, the grant kept as it is.

  Each goes through one private helper `DenyListFault`: an ERROR log line tagged `DENY-LIST-UNREADABLE` and an
  `AssignedAccessFailure` of kind `deny-list-unreadable` (`AssignedAccessMaterializer.DenyListUnreadableFailure`), so
  `Complete` is false → the sync answers 500 `sync_incomplete` with that message, and the job's root is incomplete.
- **Job**: counts those failures — `denyListUnreadable` in the heartbeat line and in `ResultJson`, plus a
  `DENY-LIST-UNREADABLE: …` problem line; the run is `Success=false` (the roots are incomplete). Fail closed throughout:
  no grant, suggestion, renewal or restore is written while the list cannot be read.

**Tests** (5 materializer + a 2-row job theory): `ARestoreWhoseNoAccessCheckThrows_IsADenyListFault_NeverAPolicyHold_AndIsPutBackOnceTheListReads`
(twin: the existing hold theory's "no-access" row — an ENTRY still holds green), `AFreshGrantWhoseNoAccessCheckThrows_…`,
`ASecureSuggestionWhoseNoAccessCheckThrows_…`, `ARenewalWhoseNoAccessCheckThrows_…` — each asserts the failure kind for
the subject, `Complete=false`, the distinct ERROR line (existing `CapturingLogger<T>` from
`Services/Communication/RungTestSupport.cs`, set through a new test-only `Harness.Logger`), never a hold and never
`no-access`, nothing written, then the normal outcome once the list reads again; and
`AssignedAccessReconciliationJobTests.ANoAccessCheckThatThrows_FailsTheRun_CountedAsADenyListFault_WhileAnEntryOnTheListDoesNot`
(throws → `Success=false`, `DENY-LIST-UNREADABLE`, `denyListUnreadable=1`; an entry → clean, `0`). The throw is a
`TaskCanceledException` from the deny-list reader's wire seam (`SeamNoAccessListReader.Throws`, task 139 r1's double).
Seeds T1–T7 (§5) each bite.

**Not closed — R-13 (§9).** Read faults the deny-veto code ABSORBS (memberships unreadable, a referenced-organization
read unreadable, the reader's own `null`, any non-cancellation exception) still arrive as a plain `true` and read as an
entry; distinguishing them needs an interface change to `AccessibleRecordSetService`, which this POML scopes out.

### 13.3 Item 2 — the raise records the LATEST conferring date (criterion 19)

`TwoConferringLowerGrantsOnOneKey_TheRaiseRecordsTheLaterDate_AndTheRestoreNeverShortensTheSurvivor`: two conferring View
Only rows on one key (today + 30, today + 200) before the raise. The core collapses the key onto the longest-conferring
survivor (today + 200); the ledger reason must be `raised-from:100000000@{today + 200}`, and the end of the assignment must
put the SURVIVOR back to View Only with today + 200. The verifier's seed V2 (`.Min(…)`) now fails it (1); a `First()`
variant would too (the earlier row is seeded first). Code unchanged — the choice was right, now it is proven.

### 13.4 Item 4 — ADR-034 A4 ACCEPTED; the concise `.claude/adr` text (exact, for the main session)

`docs/adr/ADR-034-user-record-membership.md` (this branch): the header table's "Updated" row, the A4 call-out (now
"ACCEPTED by the owner, round 11, 2026-10-03"), the A4 heading (now "(2026-10-03, accepted)" — anchor
`#amendment-a4-2026-10-03-accepted-assigned-to-access-for-contacts-is-materialized-as-removable-grants`, the call-out link
updated with it; no other file links the old anchor) and the Status block (quotes round 11 item 1). The A4 rules are
unchanged — the owner accepted that text.

The MAIN session applies the following to `.claude/adr/ADR-034-user-record-membership.md` with the 142 PR (sub-agents
cannot write `.claude/`), plus a `.claude/CHANGELOG.md` entry. It is independent of task 152's A3 concise edit; if A3's
call-out has been applied first, put A4's after it.

**Edit 1** — insert after line 12 (the end of the A1 call-out, `…#amendment-a1-2026-09-04-the-access-conferring-allow-list-becomes-first-class-and-per-surface).`) and before `> **Domain**: …`:

```markdown
>
> ⚠️ **Amendment A4 (2026-10-03, `unified-access-control-r2` task 142, path B — ACCEPTED by the owner, round 11)**: the
> access-conferring registry gains a second, **write-time** consumer. Every registry-listed Contact- or
> Organization-typed "Assigned *" column on a project, matter or work assignment gives the named subject **Collaborate**
> as an explicit, **removable** grant (`sprk_externalrecordaccess`), or a POA share when the contact is linked (task 141)
> to an eligible internal user — maintained by ONE invariant owner, `AssignedAccessMaterializer`, with provenance in the
> `sprk_assignedaccess` ledger. The read-time standing-grant and organization-expansion terms are **kept** (owner A2
> reversed). Full rules: [full ADR](../../docs/adr/ADR-034-user-record-membership.md#amendment-a4-2026-10-03-accepted-assigned-to-access-for-contacts-is-materialized-as-removable-grants).
```

**Edit 2** — replace line 14:

```markdown
> **Last Updated**: 2026-06-22 (post-implementation polish per R3 task 100)
```

with:

```markdown
> **Last Updated**: 2026-10-03 (Amendment A4 accepted, `unified-access-control-r2` task 142); 2026-06-22 (post-implementation polish per R3 task 100)
```

**Edit 3** — in the ✅ MUST list, after the bullet that begins `- **MUST** (A1) treat adding a conferring column as a **registry edit**.`, append:

```markdown
- **MUST** (**A4**, 2026-10-03, task 142, owner-accepted §6.5 path B) materialize Assigned-To access only through the ONE invariant owner, `AssignedAccessMaterializer` (`Services/ExternalAccess/`), from its three triggers: L1 inline after every BFF writer of the columns, `POST /api/v1/external-access/assigned-access/sync` (form post-save, the create wizards, "Update Access"), and L4 `AssignedAccessReconciliationJob` (every 5 min). The conferring columns come from the bound registry (`MembershipOptions.AccessConferringRoles`); child-entity entries (event, invoice, to-do, analysis) materialize nothing (owner A6).
- **MUST** (A4) write at Collaborate through the existing cores only — grants via `GrantExternalAccessEndpoint.CreateGrantAsync` with `GrantCeiling.AssignedToRule` (uncapped by the saver's level, owner A1; an absent expiry becomes today + 90), shares via `IDataverseRecordShareService` with the `RecordShareLevels` Collaborate mask — and never lower existing conferring access: a lower grant is raised and put back, its level AND date, when the assignment ends.
- **MUST** (A4) make an operator's removal stick: a Manage Access revoke/unshare, a Dismiss of a suggestion, or a removal outside the BFF with no known cause is recorded `Declined` and never re-created while the assignment persists. Declined is not a veto — a manual grant still succeeds (`Adopted`). When the column changes or is cleared, remove only the owner's own UNMODIFIED access, never while another registry column on the root still names the subject.
- **MUST** (A4) apply the record's policy before writing: Restricted → no contact or organization grant (a linked internal user's share is unaffected); Secure or Limited → no organization grant; Secure → contact grants and shares are SUGGESTED (`PendingConfirmation`), not written; the No Access list always. Write nothing when a flag set, deny list, link or ledger cannot be read (ADR-003).
```

**Edit 4** — in the ❌ MUST NOT list, after the bullet that begins `- **MUST NOT** (A1) build a second membership mechanism for the registry.`, append:

```markdown
- **MUST NOT** (A4) add a second writer of Assigned-To access, a plugin, a flow or a service-endpoint step (ADR-002), or write `sprk_externalrecordaccess` for it directly.
- **MUST NOT** (A4) read "derived access is not materialized into grant rows" (spec / FR-32 / design §7) as covering Assigned-To access: A4 is the one exception, and its grants are ordinary, audited grant rows.
```

**Edit 5 (recommended accuracy fix, same PR)** — line 41 says the 8 Q4 `sprk_assigned*` fields "are exclusively maker-portal
edits, NOT mutated by any BFF endpoint". 142's writer census (§3) found BFF writers. Replace exactly:

```markdown
the 8 Q4 sprk_assigned* fields are exclusively maker-portal edits, NOT mutated by any BFF endpoint
```

with:

```markdown
the 8 Q4 sprk_assigned* fields were then exclusively maker-portal edits (since 2026-10-03, task 142's writer census lists BFF writers — Office quick-create, the AI record tools, `UpdateRecordActionCore`, the field-mapping push — each of which calls the A4 materializer after its write)
```

### 13.5 Item 5 — route-sweep finding 67 is task 166's (#1105)

Recorded in R-7 (§9): sweep row 67 = task 166's **F11** (`POST /api/v1/field-mappings/push`, caller probe on the source
first; the child query and every child write AS the caller). No change here. The integration points for 166 — the L1 hook
it must keep after each successful child update, the textual conflict to expect in `FieldMappingEndpoints.cs`, the writer
test that pins the hook, and the optional `grantorOid` follow-up (R-4) — are listed in R-7.

### 13.6 Surface (CLAUDE.md §10/§11), self-review, runs

**Placement**: unchanged — BFF, beside the external-access writers. No new service, DI registration, endpoint, option,
job, column or package; no plugin (ADR-002). New members, each with the three-question test:

| New | Existing (grep) | Extension? | Cost of doing nothing |
|---|---|---|---|
| `GrantPolicyDecision.GranteeDenyListUnreadable` + init property `IsDenyListReadFault` | `GranteeDenied` (same wire); `Unreadable` (`policy_unreadable`, 503) | Extends `GranteeDenied` (`with`), no new reason code — a new code (or `Unreadable`'s 503) would change task 139's tested `/grant` contract | The materializer cannot tell a deny-list read fault from an entry: a sustained outage reads green (finding 4) |
| `AssignedAccessMaterializer.DenyListUnreadableFailure` (const) + private `DenyListFault` | Failure kinds are inline strings (`restore-refused`, `grant-not-conferring`, …) | One more kind; a const because the job counts it | The job cannot count the cause; four call sites would each format their own log and failure |
| Job result `denyListUnreadable` (heartbeat, `ResultJson`, a problem line) | `incompleteTotal` (counts roots, not causes) | Additive field on the existing result | Monitoring cannot tell a deny-list outage from any other incomplete root |
| `Harness.Logger` (test-only) | `NullLogger` hard-coded in the harness | Settable property, default unchanged | The distinct log line could not be asserted |

**Self-review (Step 9.5 scope, this round's diff)**: ADR-003 — every fault site writes nothing (no grant, suggestion,
renewal or restore) and is reported, never "done"; the wire contract of the grant routes is unchanged (task 139's tests
green). ADR-036 A1 — a fault makes the root incomplete and the run `Success=false`, counted by name; a real policy hold
stays green (the existing hold theory, untouched). ADR-038 — production materializer, grant core and deny-veto code run
between module-boundary doubles; the throw is injected at the reader's wire seam; no banned shape; every new guard bites
(§5). ADR-010 — no new interface. `.claude/**`: none written (§13.4 is the main session's).

**Round r3 runs.** AssignedAccess set **134/134** (127 + 5 materializer + 2 job rows); every seed in §5 ("Round r3
seeds") run alone against it. Wider affected set (AccessControl, ExternalAccess, AssignedAccess, Grant*, NoAccess,
RecordCreation, FieldMapping, UpdateRecordActionCore, the two AI record handlers) **2,695 passed, 0 failed, 1 skipped**
(incl. task 139's `GrantorCeilingTests` — the grant routes' wire answer to a throwing No Access check is unchanged). Once at
the end, sequentially: BFF build 0 warnings / 0 errors; full BFF unit suite **14,616 passed, 0 failed, 54 skipped
(14,670)**, 22m17s — the previous 14,663 plus the 7 new; `Spaarke.ArchTests` **346/346**;
`Sprk.Bff.Api.IntegrationTests` **104/104**; `Spe.Integration.Tests` **403 passed, 0 failed, 25 skipped (428)**. No
timing failure, so no isolated re-run was needed. No client file changed (no client build/test). Publish size: skipped
(harness) — main session; no package added.

## 14. Round r4 — owner round 13 items 4 and 5 (2026-10-03, branch `task/uac-r2-142-r4`)

Base: `task/uac-r2-142-r3` (`d48f5191e`). Binding input: owner rounds 1–13 and the "Peer report: #1081" section, read
from `work/unified-access-control-r2` (`d7d1af61b`). Round 13 answers this round: **item 4** — "142 R-13: fix now. The
deny-veto check gets a tri-state answer (allowed / denied / unverifiable) … It stays fail closed. The
`AccessibleRecordSetService` interface change is accepted (139 and 140 consume it)"; **item 5** — "a 143 wall-guard
Unverifiable answer on the internal-user share path fails the run, exactly like the deny-list fault". No escalation
trigger fired. Live writes: none.

### 14.1 Item 1 (owner round 13 item 4) — the deny-veto check answers a tri-state; R-13 closed

**The interface change.** `IAccessibleRecordSetService.IsGranteeDeniedOnRecordAsync` (`Task<bool>`) is replaced by
`CheckGranteeNoAccessAsync` → `Task<NoAccessCheckAnswer>` (new enum beside it: `Allowed`, `Denied`, `Unverifiable`). The old
`bool` method is REMOVED, not kept beside the new one: a `bool` that absorbs faults is the defect, and a removed member
makes any caller on another branch fail to compile at integration rather than keep absorbing silently.

**Where the faults go** (`AccessibleRecordSetService`):
- `ResolveDenyVetoAsync` now returns a private `DenyVetoResult(Denied, Unverifiable)` — a provable entry in `Denied`, and
  in `Unverifiable` every fault it meets: the subject's memberships unreadable (`ActiveOrgMemberships.Failed`), a record's
  referenced organizations unreadable, a fail-closed deny-list answer (`NoAccessListResult.FailedClosed`), a reader that
  returns no answer (`null` — before r4 a `NullReferenceException` caught by the catch-all), and any non-cancellation
  exception. Cancellation still propagates (unchanged).
- **The read path is unchanged**: the composition removes `DenyVetoResult.Removed` = `Denied ∪ Unverifiable`, exactly the
  set the veto removed before (fail closed). Seed R5 proves the existing read-path tests guard it.
- `CheckGranteeNoAccessAsync` answers `Denied` for a provable entry, `Unverifiable` for a fault, a call without a record
  (a caller bug — never an entry), and any exception other than the caller's own cancellation (an HttpClient timeout,
  which the veto code rethrows, included). It never throws except for the caller's cancellation. Each `Unverifiable` is
  logged at ERROR (`[WF-AUTHZ] … UNVERIFIABLE`).

**Every consumer updated** (fail closed everywhere — only `Allowed` grants or suggests):
- **The grant core** (`GrantExternalAccessEndpoint.CheckGrantAsync`, task 139's): `Denied` →
  `GrantPolicyDecision.GranteeDenied` (422 `sdap.access.grant.grantee_denied`); `Unverifiable` (or an unknown value) →
  `GrantPolicyDecision.GranteeDenyListUnreadable`, logged `[EXT-GRANT] DENY-LIST-UNREADABLE`; a throw anyway → the same.
- **Task 139's route callers** (`/grant`, `/invite-and-grant` — the only `CheckGrantAsync`/`CreateGrantAsync` callers on
  the work branch besides the materializer): the fault is now REPORTED as one on the wire. `GranteeDenyListUnreadable`
  is its own decision — **503 `sdap.access.grant.no_access_unverifiable`**, retryable ("…could not be checked, so nothing
  was granted. Try again in a moment."), `IsDenyListReadFault = true` — the grant routes' sibling of `/share-user`'s
  existing `sdap.access.user_share.no_access_unverifiable`. Before r4 it was `GranteeDenied` with an in-process flag, so
  the routes answered an outage as 422 "on the No Access list, or that list could not be checked", and the absorbed faults
  as a plain entry: an outage looked like the record's policy to the operator, and a 422 is not counted as a server
  failure. `grantee_denied`'s detail now names an entry only. **This changes task 139's tested route contract** for the
  fault case only, as round 13 accepted ("139 and 140 consume it"); its tests were rewritten accordingly (§14.3). Client:
  the Manage Access modal treats an unknown reason code as a retryable failure ("…N failed. Please try again.") — the
  right advice for a fault — so no client change was needed; adding the code to `GRANT_POLICY_REASON_CODES` to show the
  server's sentence verbatim is optional polish (it would mean a TrackingFieldTrio rebuild) and was left out under "do not
  change anything else".
- **Task 140's callers**: **none exist on the work branch** — proven: `git grep` on `work/unified-access-control-r2` finds
  `CheckGrantAsync` called only by `GrantExternalAccessEndpoint` and `InviteAndGrantExternalUserEndpoint` (task 139's), the
  140 POML is `<status>pending</status>` and TASK-INDEX row 140 is 🔲 open; its spec puts every grant through the same core,
  so it inherits the tri-state and the 503 when it is built (`GranteeNoAccessUnverifiableReasonCode`'s doc says "Task 140
  reuses it verbatim").
- **The materializer**: its own call (the secure suggestion) switches on the answer — `Denied` → `Skipped(no-access)`,
  anything else not `Allowed` → `DenyListFault` + `Skipped(no-access-unverifiable)`, never suggested. Its three grant-core
  paths (fresh grant, renewal, restore) already branch on `IsDenyListReadFault` (r3), which now carries every fault; a
  restore is never a green `restore-pending` for a fault.
- **The job**: counts every `deny-list-unreadable` failure (unchanged mechanism), so the absorbed faults now turn it red;
  its problem line names "shared" too (item 2).
- Docs following the code: `docs/architecture/uac-access-control.md` (the write-time No Access bullet) and
  `docs/guides/EXTERNAL-ACCESS-ADMIN-SETUP.md` (a new 503 row in the grant-route outcome table; the 422 row now says
  "a matching entry").

### 14.2 Item 2 (owner round 13 item 5) — a wall-guard Unverifiable on the share path fails the run

`AssignedAccessMaterializer`:
- `ContinueOursAsync` (r3 `:731-736`): an auto share gone from a SECURE record whose task-143 wall check answers
  `Unverifiable` still decides nothing this pass (never Declined on a guess), and now calls
  `DenyListFault(run, subject, "the reason its share was removed", detail: wall.Fault)`.
- `FreshShareAsync` (r3 `:999-1003`): a fresh or restoring share on a secure record whose wall check answers
  `Unverifiable` still writes nothing (no share, no suggestion; the ledger keeps `removed-by-no-access` so a restore is
  still owed), and now calls `DenyListFault(run, subject, restoring ? "its share" : "its suggestion", detail: wall.Fault)`.
- Both are the SAME failure kind as the deny-list fault (`deny-list-unreadable`): an ERROR line tagged
  `DENY-LIST-UNREADABLE` naming the guard's fault (`flags`, `link`, `binding`, `memberships`, `referenced-organizations`,
  `deny-list`, `exception`), `Complete = false` (the sync answers 500 `sync_incomplete`), and the job counts it
  (`denyListUnreadable`, `Success=false`). `DenyListFault` gained an optional `detail` argument for that name.

### 14.3 Tests

New file **`tests/integration/auth/UnifiedAccessControl/GranteeNoAccessCheckTests.cs`** (16) — the production
`AccessibleRecordSetService` and `NoAccessListReader` behind their seams: Allowed (twin), Denied for four entry shapes,
nothing-to-check = Allowed, Unverifiable for seven faults (memberships unreadable, referenced organizations unreadable,
reader fail-closed, reader 5xx-shaped throw, referenced-organization throw, both timeouts), a reader that returns no
answer, a call without a record; the caller's own cancellation propagates (the timeout rows' twin).

`GrantorCeilingTests` (task 139's; 43 → 48): the fault theory rewritten — `/grant` answers **503
`no_access_unverifiable`** for six faults (was 422 `grantee_denied` for two absorbed faults and two timeouts),
`/invite-and-grant` 503 before onboarding for three; `CreateGrantAsync` called directly reports a fault as
`IsDenyListReadFault` + its own code and an entry as `grantee_denied`; the entry test now also asserts its detail never
says "could not be checked".

`AssignedAccessMaterializerTests` (+20): per consumer path, each over the three formerly-absorbed faults — restore (a
fault, never a hold; restored once the inputs read), fresh grant (`no-access-unverifiable`, never `no-access`), secure
suggestion (not suggested) plus its twin (an entry: `no-access`, a clean run), renewal (not renewed, kept); item 2 —
a gone auto share on a secure record (three wall faults: decided nothing, Shared kept, then Declined once readable), a
fresh share on a secure record (three wall faults: neither shared nor suggested, then suggested), a share the enforcer
removed whose lifted wall is unreadable (not restored, still owed, then restored).

`AssignedAccessReconciliationJobTests` (+3): an Unverifiable answer fails the run, counted as a deny-list fault, for
memberships unreadable, a fail-closed deny list and a wall Unverifiable on a secure share. `GrantPolicyTestDoubles.
DenyListAnswering` now answers the tri-state.

**Bite proof** — each seed applied ALONE by exact string replacement (a scratch script), build + the 553-test set
(AssignedAccess, GrantorCeiling, GranteeNoAccessCheck, AccessibleRecordSet, UnifiedEvaluator, GrantLifecycle, NoAccess),
restored byte-for-byte (MD5 re-checked) and touched:

| Seed | Consumer | Failed |
|---|---|---|
| R1 an Unverifiable veto answer returned as Denied (the pre-r4 absorb) | the check | 27 |
| R2 a fail-closed / absent reader answer counted as a provable denial | the veto | 10 |
| R3 unreadable memberships counted as provable denials | the veto | 9 |
| R4 the veto's per-record unverifiable set returned as denials | the veto | 16 |
| R5 the read path stops removing unverifiable candidates (fail OPEN) | the read path (unchanged behaviour) | 8 — existing `AccessibleRecordSetServiceTests` / `UnifiedEvaluatorSeamTests` |
| R6 a timeout (not the caller's) rethrown instead of answered | the check | 2 |
| C1 the grant core maps Unverifiable to GranteeDenied | the grant core | 25 |
| C2 the grant core treats Unverifiable as Allowed (fail OPEN) | the grant core | 26 |
| D1 the fault answered with the r3 wire (422 `grantee_denied`) | task 139's routes | 10 |
| M1 the materializer's suggestion does not report an Unverifiable answer | materializer | 4 |
| M2 … reads it as an entry (`no-access`, clean) | materializer | 4 |
| M3 … suggests on it (fail OPEN) | materializer | 4 |
| W1 item 2: `ContinueOursAsync` wall Unverifiable not reported | materializer | 3 |
| W2 item 2: `FreshShareAsync` wall Unverifiable not reported | materializer + job | 5 |
| J1 the job does not count deny-list faults | job | 4 |

(Two first-pass seeds did not run as intended and were re-run: R4's first form did not compile — replaced by the form
above; C2's first build died on an MSBuild worker-node crash, an environment fault — re-run after
`dotnet build-server shutdown`.)

### 14.4 Item 3 — the write-path invariant row is I-12

`docs/architecture/DATAVERSE-WRITE-PATH-ARCHITECTURE.md`: the Assigned-To row renumbered **I-11 → I-12** (as the main
session did at integration — master took I-11 for record numbering), and "Code on branch `task/uac-r2-142` (2026-10-03)."
dropped from its status cell. The POML's r0 outcome now says I-12 too; nothing else on this branch named I-11.

### 14.5 Surface (CLAUDE.md §10/§11), ADRs, self-review

**Placement**: unchanged — BFF, beside the external-access writers. No new service, DI registration, endpoint, option,
job, column or package; no plugin (ADR-002). New members, each with the three-question test:

| New | Existing (grep) | Extension? | Cost of doing nothing |
|---|---|---|---|
| `NoAccessCheckAnswer` (enum) + `IAccessibleRecordSetService.CheckGranteeNoAccessAsync` (replaces `IsGranteeDeniedOnRecordAsync`) | `IsGranteeDeniedOnRecordAsync` (`bool`); `SecureShareWallOutcome` (task 143's guard — a different question: a systemuser on a secure record, four states incl. `NotSecure`) | The existing member CHANGED shape (owner-accepted interface change); reusing `SecureShareWallOutcome` would import `NotSecure`, meaningless for a contact grant | Owner round 13 item 4 unmet: a read fault reads as an entry — a restore holds green, a fresh grant is `no-access`, the job stays green through an outage |
| private `DenyVetoResult` | `IReadOnlySet<Guid>` return; `SystemUserDenyVeto` (the systemuser plane's, split by kind not by fault) | Same shape as the existing private `SystemUserDenyVeto`; one method, one result | The write-time check cannot tell a fault from an entry without a second veto implementation (forbidden) |
| `ExternalGrantLifecycle.GranteeNoAccessUnverifiableReasonCode` (`sdap.access.grant.no_access_unverifiable`); `GranteeDenyListUnreadable` re-shaped to 503 | `grantee_denied` (an entry); `policy_unreadable` (the flags); `/share-user`'s `no_access_unverifiable` | A new code in the existing decision family, mirroring `/share-user`'s; `policy_unreadable` names a different read | The grant routes keep answering an outage as the record's policy (422 — uncounted, non-retryable advice) |
| `DenyListFault(…, detail)` optional argument | — | Additive | The wall guard's fault would not be named in the log line |

**ADR check (this round's diff)**: ADR-003 — every Unverifiable refuses (grant core, materializer, wall paths), never
grants; the read path still removes it. ADR-036 A1 — a fault is a counted failure (`Success=false`), never a hold and
never "done". ADR-008 / ADR-010 — no new endpoint, no new interface (an existing member changed); concretes registered as
before. ADR-002 — no plugin. ADR-038 — production code between module-boundary doubles (`Mock<INoAccessListReader>` only
for the null-answer case; no `Mock<HttpMessageHandler>`, no DI-registration or ctor-null tests); every new guard bites
(§14.3). ADR-034 A4 rules unchanged (they already say "write nothing when … deny list … cannot be read"). CLAUDE.md §6.5 —
task 139's route contract change is the owner's round-13 decision, not a silent deviation. `.claude/**`: nothing needed
this round (no `.claude` file names `IsGranteeDeniedOnRecordAsync`, `grantee_denied` or I-11 — grepped).

**Formatting**: `dotnet format` (the pre-commit hook's command, run first so the suites ran on the committed bytes)
re-indented the `switch` cases in `EndAssignmentAsync` (whitespace only) and sorted one `using` in
`GrantExternalAccessEndpoint.cs`.

**Round r4 runs.** Affected sets first: AssignedAccess **157/157** (134 + 23), `GrantorCeilingTests` **48/48** (43 + 5),
`GranteeNoAccessCheckTests` **16/16**; the seed set **553/553**; the wider affected set (AccessControl, ExternalAccess,
NoAccess, Grant*, AccessibleRecord*, RecordCreation, FieldMapping, UpdateRecordActionCore, the two AI record handlers,
DelegationRule, InternalUserShare) **2,739 passed, 0 failed, 1 skipped** (r3: 2,695 + the 44 new). Once at the end,
sequentially, after `dotnet format` on the changed files: BFF build 0 warnings / 0 errors; full BFF unit suite **14,643
passed, 17 failed, 54 skipped (14,714 = r3's 14,670 + the 44 new)**, 37m19s — the 17 are timing failures under
contention (the machine was at ~85% CPU with 1.4 GB of 61.6 GB free and ~270 dotnet processes from other agents'
suites: 15 WebApplicationFactory tests that timed out after ~3 minutes in Compose, SpeAdmin, Office, Insights, Memory and
Documents, one regex-engine timeout in `EmailAttachmentProcessorTests`, one `TaskCanceledException` in
`SseStreamingIntegrationTests`); none touches this round's code. **Re-run in isolation: all pass** (19/19 — the filter
selects every row of the one theory). `Spaarke.ArchTests` **346/346**; `Sprk.Bff.Api.IntegrationTests` **104/104**;
`Spe.Integration.Tests` **403 passed, 0 failed, 25 skipped (428)**. No client file changed (no client build/test).
Publish size: skipped (harness) — main session; no package added (no CVE delta).

## 15. Round r5 — the r4 verifier's findings (2026-10-03, branch `task/uac-r2-142-r5`)

Base: `task/uac-r2-142-r4` (`b36c89ede`). **Branch name**: the harness asked for
`switch -c task/uac-r2-142-r1 task/uac-r2-142-r4`; `task/uac-r2-142-r1` already exists (this task's round r1,
`4ac362f8e`, checked out in another worktree), so the switch refused. Nothing was overwritten: the round runs on
`task/uac-r2-142-r5`, the next name in this task's round sequence (r1 → r2 → r3 → r4 each built on the previous).
Binding input: owner rounds 1–13 and the "Peer report: #1081" section, read from `work/unified-access-control-r2`
(`e6dd48b43`). Live writes: none. Escalation: one, item 7 (§15.4) — not answered by rounds 1–13.

### 15.1 Per item

| # | Verifier item | Disposition |
|---|---|---|
| 1 | Scope and method | Acknowledged; no action. |
| 2 | Item 1 (R-13 tri-state) implemented correctly | Acknowledged; no action. |
| 3 | Item 2 (wall-guard Unverifiable fails the run) implemented correctly | Acknowledged; no action. |
| 4 | Item 3 (I-12) done | Acknowledged; re-grepped `DATAVERSE-WRITE-PATH-ARCHITECTURE.md`: no `I-11`. |
| 5 | MEDIUM — the mixed-batch union of `DenyVetoResult.Removed` is unguarded (seed V1 survived; fail OPEN on the read path) | **FIXED** — §15.2. V1 now fails 1; its mirror V1b fails 1. |
| 6 | LOW — the defensive "unknown answer" branches are untested (seed V7 survived) | **FIXED** — §15.3. V7 now fails 2; the materializer's analogue M4 fails 1. |
| 7 | LOW — a subject the reader will not evaluate is classified as a transient fault | **ESCALATED (R-14)** — §15.4; the main session decides. As-built operator guidance added to the admin guide (fail-closed behaviour unchanged). |
| 8 | LOW — comment and fixture drift | **FIXED**: (a) `GrantExternalAccessEndpoint.PolicyRefusalProblem`'s XML doc lists 503 `no_access_unverifiable` beside `policy_unreadable`, and names which codes task 139 added; (b) `AccessGrantModal.grantOutcome.test.tsx` mocks the current `grantee_denied` detail ("…it is on the record's No Access list. Nothing was granted."); (c) the `ContinueOursAsync` wall fault now reads "…so **the decision on its removed share** was not written" (was "the reason its share was removed was not written"). Also seen, NOT changed: `notes/task-139-grant-model.md:41-43` still describes the r3-era `grantee_denied` detail — task 139's own decision record of its time, superseded by §14.1; left for that task's owner / the integrator. |
| 9 | The wire change is within scope | Acknowledged; no action. |
| 10 | The verifier's seeds | V1 and V7 (the two survivors) now fail — §15.2/§15.3. |
| 11 | Test quality | The new tests keep the shape: §15.2 runs the production `AccessibleRecordSetService` with the same seam fakes as its neighbours; §15.3's double (`Mock<IAccessibleRecordSetService>`, Strict) sits at the module boundary and exists only to hand the consumer a value no production check returns — the r4 rule for the null-answer `Mock<INoAccessListReader>`. Every refusal row has an Allowed twin through the same double. No `Mock<HttpMessageHandler>`, no assertion on a value the test itself supplied. |
| 12 | Suites | r5's own runs: §15.5. |
| 13 | Criterion 19 NOT MET (V1) | **Closed** by item 5 (plus item 6's defensive branches). |
| 14 | Criterion 18 NOT MET | Unchanged — main session: the concise `.claude/adr/ADR-034` edit (exact text §13.4) and the PR §6.5 path-B block (§8). Sub-agent write boundary. |
| 15 | Criterion 20 PARTIALLY MET | Build, ArchTests and the full unit suite re-run in r5 (§15.5). CVE check run this round: `dotnet list … package --vulnerable --include-transitive` on `Sprk.Bff.Api` — "no vulnerable packages" (no package added). Publish size: main session (harness: skip). |
| 16 | Criterion 21 PENDING-LIVE-GATE | Unchanged — main session (G-7, G-5). |
| 17 | G-0 NOT MET | Re-checked (merge-base `--is-ancestor`, work at `e6dd48b43`): none of `task/uac-r2-133-b2-r2` (`5a8b66c15`), `task/uac-r2-137-b2` (`a8fe5b428`), `task/uac-r2-143-r2` (`7668bbc1f`) or `843d62b46` is an ancestor yet. Main session's integration order. |

### 15.2 Item 5 — the mixed-batch read path is pinned

`AccessibleRecordSetServiceTests.ComposeAsync_ABatchWithAnEntryAndAnUnverifiableRecord_RemovesBoth_AndKeepsTheirTwin`
(unit project, beside the single-kind veto tests): one contact, three Full Access project grants in ONE composition — a
record an entry names (`DenyingReader`, record-keyed), a record whose referenced organizations are unreadable
(`UnreadableOrgReferences`; never sent to the reader, so only the unverifiable set can remove it) and a twin (resolvable,
no entry). Asserts both vetoed records are absent and the twin keeps exactly
`ExternalAccessLevels.ToAccessRights(FullAccess)`. This is the only batch shape in which `DenyVetoResult.Removed`'s third
branch (`Denied.Concat(Unverifiable)`) runs; it is reachable on both contact planes (one
`ApplyVetoPipeline(…, veto.Removed, …)` call, `AccessibleRecordSetService.cs` ~`:2005`). No production change — the code
was correct; the guard was missing.

### 15.3 Item 6 — an answer outside the enum refuses, at both consumers

- **Grant core** (`GrantorCeilingTests`, task 139's file):
  `CreateGrantAsync_CalledDirectly_RefusesEveryAnswerButAllowed_AnUnknownAnswerAsAFault` — Denied → 422 `grantee_denied`,
  not a fault; Unverifiable → 503 `no_access_unverifiable`, `IsDenyListReadFault`; `(NoAccessCheckAnswer)99` → the same
  503 fault; nothing written in any row. Twin `CreateGrantAsync_CalledDirectly_GrantsWhenTheCheckAnswersAllowed` (same
  double, Allowed → the row is written). The private `Core(...)` helper gained an optional check argument;
  `GrantPolicyTestDoubles.DenyListAnswering` gained a `NoAccessCheckAnswer` overload (the `bool` one now delegates to it).
- **Materializer** (`AssignedAccessMaterializerTests.AnUnknownNoAccessAnswer_IsADenyListFault_NeverSuggestedNorGranted`,
  secure / standard): on a secure record the value reaches the materializer's own `!= Allowed` branch; on a standard one
  it reaches the core's `default` through the fresh-grant path. Both: the deny-list fault fingerprint
  (`AssertDenyListFault`), `Skipped(no-access-unverifiable)`, no grant; then the same double answering Allowed suggests /
  grants. Seam: `Harness.NoAccessCheckOverride` (test-only; null = the real check, so every other test is unchanged).

**Bite proof** — each seed applied ALONE by exact string replacement (scratch script), build + the seed set
(AssignedAccess, GrantorCeiling, GranteeNoAccessCheck, AccessibleRecordSet, UnifiedEvaluator, GrantLifecycle, NoAccess:
**560** tests), restored byte-for-byte (MD5 re-checked) and touched:

| Seed | What it breaks | Failed |
|---|---|---|
| V1 (the r4 verifier's) | `Removed`'s mixed branch returns `Denied` only — the unverifiable record is returned (fail OPEN) | 1 (§15.2's test) |
| V1b | … returns `Unverifiable` only — the walled record is returned (fail OPEN) | 1 (§15.2's test) |
| V7 (the r4 verifier's) | the core's switch gains `case (NoAccessCheckAnswer)99:` → allowed | 2 (the core's 99 row; the materializer's standard row) |
| M4 | the materializer's own check refuses only `Unverifiable` (`!= Allowed` → `== Unverifiable`) | 1 (the materializer's secure row) |

### 15.4 Item 7 — ESCALATION (R-14): a subject the deny-list reader will not evaluate

**Not answered by owner rounds 1–13** (round 13 item 4 speaks of read faults — "Dataverse 5xx, throttling, unreadable
memberships"; a deterministic "will not evaluate" is not named). Stopped and recorded; no classification change made.

🔔 **Human Input Required — classification of the reader's query-bound refusal**

- **The behaviour.** `NoAccessListReader.GetDeniedRecordsAsync(NoAccessSubjects, …)` returns `FailedClosed` when the
  subject set exceeds its safe query bound — more than 25 organizations (`MaxSubjectOrganizationIds`) or more than 5
  contacts (`MaxSubjectContactIds`) — logged ERROR `[NO-ACCESS] FAIL-CLOSED: … exceed the safe query bound …`. That is a
  deterministic "cannot safely evaluate", not a transient read fault. Since r4 every consumer maps `FailedClosed` to
  Unverifiable: the grant routes answer **503 `no_access_unverifiable` "Try again in a moment"** (it never succeeds for
  that grantee), the materializer reports `deny-list-unreadable` **on every pass** (the job stays red for as long as the
  subject is assigned), and task 143's guard (`SecureShareNoAccessGuard`, fault `deny-list`) does the same on the share
  path. Fail closed throughout; nothing is exposed.
- **Reach.** The grant core and the materializer's suggestion check pass ONE contact, so the contact bound cannot fire
  there; the organization bound fires when the contact's WALL set (every `statecode`-active membership row — date-ended
  ones count until deactivated, owner D-2/D-10) plus the request's firm exceeds 25. Task 143's guard passes the linked
  contact and any contact bound to the user's oid (one, under the 141 contract), so in practice only the organization
  bound. The reader's own design note calls a set that large implausible ("exists to make an implausible case fail
  SAFE"). The READ path's handling is unchanged by r4 (tasks 038/039: every candidate removed — that contact sees nothing).
- **Options.**
  - **(A) Accept as built** (fail closed; every consumer honestly reports it could not check). Operator guidance added
    this round, as-built: the admin guide's 503 row now says what a persistent single-grantee 503 means and which log
    line names it (`docs/guides/EXTERNAL-ACCESS-ADMIN-SETUP.md`).
  - **(B) A distinct non-retryable classification**: a "not evaluable" flag on `NoAccessListResult`, a fourth
    `NoAccessCheckAnswer` (or a reason on Unverifiable), a 422 code (e.g. `sdap.access.grant.no_access_not_evaluable`,
    "ask an administrator"), and a distinct materializer failure kind. Touches task 038's reader, task 143's guard and
    task 139's routes (a second wire change); the read path's blackout for that contact remains.
  - **(C) Remove the case**: split the SUBJECT side into chunks and union the per-chunk matches (sound for a veto — the
    union over subject chunks equals the single OR'd query, and any chunk fault still fails the whole answer closed).
    Touches task 038/143's reader only; ends the permanent red, the wrong retry advice AND the read-path blackout.
- **Recommendation**: **(A) now**, plus a GitHub issue for **(C)** if the owner wants the case gone — (C) is the only
  option that also fixes the read path; (B) adds a wire state to label a case (C) removes. No 142 code changes for (A).

### 15.5 Surface (CLAUDE.md §10/§11), ADRs, runs

**Surface**: no new service, DI registration, endpoint, option, job, column or package; no plugin (ADR-002). Production
diff: one XML-doc paragraph (`GrantExternalAccessEndpoint.PolicyRefusalProblem`) and one log/user-message phrase
(`AssignedAccessMaterializer.ContinueOursAsync`). Test-only additions: `GrantPolicyTestDoubles.DenyListAnswering(NoAccessCheckAnswer)`
(overload of an existing double; the bool one delegates), `Harness.NoAccessCheckOverride` (null = unchanged), the
`Core(...)` helper's optional argument. No §11 justification needed (no new product surface). `.claude/**`: nothing
needed this round.

**ADR check (this round's diff)**: ADR-003 — no behaviour change; the new tests pin two fail-closed guards (the union, the
unknown-answer branches). ADR-038 — production code between module-boundary doubles; the one Strict
`Mock<IAccessibleRecordSetService>` only injects an out-of-range value and its consumers' outcomes are asserted; KEEP
paths (`tests/integration/auth/**`; the unit `Infrastructure/ExternalAccess` file it extends). ADR-036 A1 — unchanged (a
fault is still a counted failure). CLAUDE.md §6.5 — item 7 surfaced as an escalation, not decided silently.

**Formatting**: `dotnet format` (the pre-commit hook's command) on the seven changed C# files also reformatted three
pre-existing object initializers (one property per line — `AssignedAccessTestDoubles.Person`, two `NoAccessEntryRow`
initializers in `AccessibleRecordSetServiceTests`) and moved one `using Spaarke.Dataverse` into sorted order — whitespace
and ordering only. Prettier `--check` on the changed `.tsx`: clean.

**Round r5 runs.** Affected first (after format): the seed set **560/560** (r4 553 + 7 new); AssignedAccess **159/159**
(157 + 2); `GrantorCeilingTests` **52/52** (48 + 4); `AccessibleRecordSetServiceTests` **80/80**; the wider affected set
(AccessControl, ExternalAccess, NoAccess, Grant*, AccessibleRecord*, RecordCreation, FieldMapping, UpdateRecordActionCore,
the two AI record handlers, DelegationRule, InternalUserShare, AssignedAccess, UnifiedEvaluator) **2,746 passed, 0
failed, 1 skipped** (r4 2,739 + 7). Client (`Spaarke.UI.Components`, after `npm install --legacy-peer-deps --no-audit
--no-fund`): the AccessGrantModal suites **89/89** with `--runInBand`; the first parallel run had 2 `findByText` timeouts
in `AccessGrantModal.userShare.test.tsx` (a file this round did not touch) that pass alone (17/17) — load, not a defect.
Package build (`npm run build` = `tsc`; the changed file is a `__tests__` file the tsconfig excludes): green once the two
`file:` siblings it imports (`Spaarke.SdapClient`, `Spaarke.Auth`) were built in this fresh worktree (before that, 9
`TS2307`/`TS18046` errors, all from "cannot find module @spaarke/…").

Once at the end, sequentially, after `dotnet format`: BFF build **0 warnings / 0 errors**; full BFF unit suite **14,667
passed, 0 failed, 54 skipped (14,721 = r4's 14,714 + the 7 new)**, 36m16s; `Spaarke.ArchTests` **346/346**. Both
integration suites' FIRST run aborted before any test ran — "vstest.console process failed to connect to testhost process
after 90 seconds … machine slowness" (other agents' suites were running) — and were re-run at once with
`VSTEST_CONNECTION_TIMEOUT=900` and `--no-build` (same bytes): `Sprk.Bff.Api.IntegrationTests` **104/104**;
`Spe.Integration.Tests` **403 passed, 0 failed, 25 skipped (428)**. CVE: no vulnerable packages. Publish size: skipped
(harness) — main session.

## 16. Round r6 — the r5 verifier's items + round 18 (2026-10-03/04, branch `task/uac-r2-142-r6`)

Base: `task/uac-r2-142-r5` (`0b489d8d1`). **Branch name**: the harness asked for
`switch -c task/uac-r2-142-r2 task/uac-r2-142-r5`; `task/uac-r2-142-r2` already exists (this task's round r2,
`aa7ab9cbf`), so the switch refused. Nothing was overwritten: the round runs on `task/uac-r2-142-r6`, the next free name
in this task's round sequence (r1 → r2 → r3 → r4 → r5 → r6, each built on the previous). The orchestrator must pick the
work up by the **r6** name.

**Binding input**: owner rounds 1–13 and the "Peer report: #1081" section, plus **round 18** (2026-10-03, the main
session's decision under the owner's standing directive of round 15), all read from `work/unified-access-control-r2` at
`e1dd17dcf`. Round 18 answers this task's open escalation R-14 (item 1) and names the r5 verifier's two LOW items
(item 2). The harness's item list predates round 18, so round 18 item 1 was not in it; the main session confirmed in the
worktree that it belongs to this round (a coordination file, deleted before commit, never committed). Live writes:
none. No escalation trigger fired.

### 16.1 Per item

| # | Verifier item (r5 verifier) | Disposition |
|---|---|---|
| 1 | Scope and method | Acknowledged; no action. |
| 2 | r4 item 1 (R-13 tri-state) implemented correctly | Acknowledged; no action. |
| 3 | r4 item 2 (a wall-guard Unverifiable fails the run) implemented correctly | Acknowledged; no action. |
| 4 | r4 item 3 (I-12) done | Acknowledged; no action. |
| 5 | r5 changes are behaviour-free | Acknowledged. r6 DOES change behaviour, by round 18 item 1 (§16.2). |
| 6 | The verifier's own seeds (A, G, B, D, H, K bite; F 0 by design) | Acknowledged; agreed that F (`IsPolicyHold`'s `!IsDenyListReadFault` guard) is defence-in-depth since r4's own reason code. No action. |
| 7 | Suites on `0b489d8d1` | Acknowledged; r6's own runs: §16.5. |
| 8 | Cross-branch check for the removed `IsGranteeDeniedOnRecordAsync` | Acknowledged. r6 also checked the reader it changes: no task branch other than 143's own feature commit `d248dff11` (already in this line, and identical to `task/uac-r2-143-r2`'s copy) changes `NoAccessListReader.cs` relative to work, and no branch adds a test relying on the old bound (`git log -S MaxSubjectOrganizationIds` over every task branch: only 038, 143 and 142 r5). |
| 9 | Merge check | Acknowledged; work moved to `e1dd17dcf` (rounds 17–18, docs only). |
| 10 | POML well-formedness | Re-checked after this round's edits (§16.5). |
| 11 | LOW — the Manage Access client omits the 503 `no_access_unverifiable` code | **FIXED** (round 18 item 2) — §16.3. |
| 12 | LOW — `notes/task-139-grant-model.md:41-43` quotes the r3-era `grantee_denied` detail | **FIXED** (round 18 item 2) — the bullet now says it is superseded and quotes the current 422 and 503 details (§16.3). |
| 13 | Branch name (r5 on `-r5`) | Acknowledged. This round: `task/uac-r2-142-r6` (the requested `-r2` exists). |
| 14 | Criterion 18 NOT MET | Unchanged — main session: the concise `.claude/adr/ADR-034` edit (exact text §13.4) with its `.claude/CHANGELOG.md` entry, and the PR §6.5 path-B block (§8). Sub-agent write boundary. Nothing new is needed in `.claude/**` this round. |
| 15 | Criterion 20 PARTIALLY MET | Build, ArchTests, the full unit suite and both integration suites re-run (§16.5). No package added (no CVE delta). Publish size: main session (harness: skip). |
| 16 | Criterion 21 PENDING-LIVE-GATE | Unchanged — main session (G-7, G-5; escalation (j) may fire at 21 (ii)). G-6's bundle was rebuilt (§7 G-6). |
| 17 | G-0 NOT MET | Re-checked (`git merge-base --is-ancestor`, work at `e1dd17dcf`): none of `5a8b66c15`, `a8fe5b428`, `7668bbc1f`, `843d62b46` is an ancestor yet. Main session's integration order. |
| 18 | Criteria 1–17 and 19 met from code and tests | Acknowledged; round 18's tests extend criterion 7 / 19 coverage (§16.4). |
| R18-1 | Round 18 item 1 — R-14: evaluate any subject set | **FIXED** — §16.2. R-14 closed (§9). |

### 16.2 Round 18 item 1 — the No Access reader evaluates any subject set

`NoAccessListReader.GetDeniedRecordsAsync(NoAccessSubjects, …)` (task 038's reader, extended by task 143):

- **Removed**: the pre-check that returned `FailClosed(candidates)` WITHOUT querying when the subject set held more than
  `MaxSubjectOrganizationIds` (25) organizations or `MaxSubjectContactIds` (5) contacts, and its ERROR line
  `[NO-ACCESS] FAIL-CLOSED: … exceed the safe query bound …`.
- **Added**: `internal static ChunkSubjects(NoAccessSubjects)` splits the subject side into chunks of at most 5 contacts and
  25 organizations (chunk `i` takes the `i`-th slice of each kind; the systemuser rides in the first chunk; every chunk
  carries at least one subject). The two constants keep their values and now mean "per query". Every subject chunk is
  queried against every object chunk (referenced-organization chunks, then candidate-record chunks — the unchanged object
  chunking), and every row is folded into the one `denied` / `kinds` accumulation, so the matches are unioned with their
  provenance and subject kinds. A within-bound set is one chunk whose filter is byte-identical to the old single filter.
- **Fail closed unchanged**: a `null` (non-success status) from ANY query returns `FailClosed(candidates)` for the whole
  answer, and any non-cancellation exception does so through the existing catch — a partial union is never returned as
  "all the denials". Consumers are unchanged: `FailedClosed` still maps to Unverifiable everywhere (grant core 503
  `no_access_unverifiable`, the materializer's `deny-list-unreadable`, task 143's guard `deny-list`, the read path's
  removal of every queried candidate).
- **Why the union is sound for a veto**: an entry names exactly ONE subject (schema Business Rule 1; malformed rows deny
  nothing), so it matches the single OR'd subject filter iff it matches the filter of the one chunk holding its subject.
- **Effect**: a grantee whose wall set exceeds 25 organizations is now Allowed or Denied, not a permanent 503; the
  Assigned-To job is no longer red for as long as such a subject stays assigned; the read path no longer hides EVERY
  record from such a contact (only the walled ones).
- One Information line `[NO-ACCESS] Evaluating … in N subject chunks …` when more than one chunk runs (R-15).
- **Admin guide** (`docs/guides/EXTERNAL-ACCESS-ADMIN-SETUP.md` §7.1, the 503 `no_access_unverifiable` row): the r5
  sentence describing the "more than 25 organizations" limit is replaced — the size of a grantee's organization set is
  never the cause; look for the `[NO-ACCESS] Deny-list query FAILED` / `… THREW` line that names the Dataverse fault.

### 16.3 Round 18 item 2 — the two LOW items

- **Client** (`AccessGrantModal.tsx`, `GRANT_POLICY_REASON_CODES`): `sdap.access.grant.no_access_unverifiable` added, so a
  503 from `/grant` or `/invite-and-grant` shows the server's own sentence ("…could not be checked, so nothing was granted.
  Try again in a moment.") instead of the generic "N failed. Please try again." The doc comment names the code, and both
  it and the notice-builder comment now say the 503 details carry their own retry advice (the old comment said every code
  in the set fails the same way on retry). Tests (`AccessGrantModal.grantOutcome.test.tsx`): a 503 row in the
  `/invite-and-grant` refusal table, and a `/grant` (internal contact) case; both assert the detail renders and the
  generic failure does not. The shipped artifact: TrackingFieldTrio's checked-in `bundle.js` was rebuilt
  (`npm run build:prod`) and differs from r0's only by that one string (verified byte-for-byte after normalizing line
  endings); version stays 1.0.34 (never imported; §7 G-6).
- **`notes/task-139-grant-model.md`** (the "Deny detail" bullet, formerly :41-43): marked SUPERSEDED by r4, keeping what
  task 139 built, and quoting the current contract — 422 `grantee_denied` = an entry ("…it is on the record's No Access
  list. Nothing was granted."), 503 `no_access_unverifiable` = a read fault.

### 16.4 Tests and seeds

New: 15 server rows and 2 client cases. The r5 test
`GetDeniedRecordsAsync_ExcessiveOrganizationIds_ReturnsDenyAllQueriedFailClosedWithoutQuerying`, which pinned the removed
behaviour, is deleted, so the server suite grows by 14:

- `NoAccessListReaderTests` (unit, task 038's file) over a new `TableNoAccessListReader` — the same `QueryChunkAsync` seam,
  answering each query the way Dataverse answers the combined `$filter` (only entries whose subject AND object the query
  names), so an entry is found only if its subject actually reached a query: 26 and 51 organizations with the entry on the
  LAST (found; subject-chunk count asserted); 30 organizations with no entry (a considered zero); 7 contacts with a wall
  entry on the last; the union across chunks (a systemuser entry in chunk 1 + an organization entry in chunk 2 on one
  record → both entry ids, both kinds); a fault in the second subject chunk, as `null` and as a throw (the whole answer
  fails closed, provenance empty, the first chunk was queried); the bound (12 contacts + 60 organizations + a systemuser:
  three distinct subject filters, each ≤ 5 contacts and ≤ 25 organizations, every subject in exactly one chunk, the
  systemuser once); within the bound = one chunk with the unchanged filter.
- `GranteeNoAccessCheckTests` (the production check over the production reader): a grantee with 30 memberships is Denied
  when the 30th is walled and Allowed when not — never Unverifiable — and Unverifiable when one subject chunk faults
  (`SeamNoAccessListReader.FaultsWhenSubjectNames`, a new test-double property).
- `AssignedAccessMaterializerTests`: an assigned contact with 30 memberships is granted (or Skipped(no-access) when the 30th
  is walled), the run Complete with no failures.
- `AccessibleRecordSetServiceTests` (read path): a contact with 30 active memberships loses only the walled record; its
  sibling keeps Full Access.
- Client: two jest cases (§16.3).

**Bite proof** — each seed applied ALONE by exact string replacement (scratch script), build + the seed set (AssignedAccess,
GrantorCeiling, GranteeNoAccessCheck, AccessibleRecordSet, UnifiedEvaluator, GrantLifecycle, NoAccess, InternalUserShare:
**675** tests), restored byte-for-byte (MD5 re-checked) and touched; the client seed against the AccessGrantModal suites (91):

| Seed | What it breaks | Failed |
|---|---|---|
| S1 | the r5 refusal back: more than 25 organizations / 5 contacts → `FailClosed` without querying | 13 (every new server row that needs a large set evaluated) |
| S2 | the chunker never builds the last subject chunk (silent under-deny) | 12 |
| S3 | a faulted record-object chunk is skipped (`continue`) instead of failing the whole answer | 20 (the new chunk-fault rows plus every existing fail-closed reader/guard/check/materializer/job row) |
| S4 | the systemuser is dropped from every chunk | 15 |
| S5 | the systemuser rides in every chunk (asked about more than once) | 1 (the bound test) |
| S6 | a chunk embeds 26 organizations (off by one past the bound) | 1 (the bound test) |
| C1 | the client set loses `no_access_unverifiable` | 2 (both new jest cases) |

### 16.5 Surface (CLAUDE.md §10/§11), ADRs, runs

**Surface**: BFF placement unchanged (task 038/143's reader, in place). No new service, DI registration, endpoint, option,
job, column or package; no plugin (ADR-002). New members: `NoAccessListReader.ChunkSubjects` (internal static, a private
helper made assertable — existing: the single-filter build it generalizes; extension: it IS the extension of
`GetDeniedRecordsAsync`'s subject handling; cost of doing nothing: R-14 — a permanent 503, a permanently red job and a
read-path blackout for any contact whose wall set exceeds 25 organizations). Test-only: `TableNoAccessListReader`,
`SeamNoAccessListReader.FaultsWhenSubjectNames`. One client string. `.claude/**`: nothing needed this round.

**ADR check (this round's diff)**: ADR-003 — fail closed preserved (a fault in any chunk fails the whole answer; seeds S3,
S2 prove a dropped or skipped chunk is caught). NFR-01 / NFR-02 — every query stays within the per-query bound (seeds S5,
S6). ADR-038 — production reader behind its wire seam, production check / materializer / read path; no
`Mock<HttpMessageHandler>`, no assertion on a value the test supplied; KEEP paths (`tests/integration/auth/**`, the unit
`Infrastructure/ExternalAccess` files it extends). CLAUDE.md §6.5 — not triggered (round 18 decided R-14).

**Round r6 runs.** Affected first: the seed set **675/675** (the r5 verifier's 661-test set + 14); the wider affected set
(AccessControl, ExternalAccess, NoAccess, Grant*, AccessibleRecord*, RecordCreation, FieldMapping, UpdateRecordActionCore,
the two AI record handlers, DelegationRule, InternalUserShare, AssignedAccess, UnifiedEvaluator) **2,760 passed, 0
failed, 1 skipped** (r5 2,746 + 14). Client (`npm install --legacy-peer-deps --no-audit --no-fund`; no lockfile
changed): the AccessGrantModal suites **91/91** (`--runInBand`; 89 + 2); package build (`npm run build` = `tsc`) green
after building its two `file:` siblings (`Spaarke.Auth`, `Spaarke.SdapClient`); Prettier `--check` clean on both changed
files; TrackingFieldTrio `npm run build:prod` succeeded (webpack's usual bundle-size warnings). The package's full jest
run: **3,429 passed, 15 failed in 10 suites (3,444)** — 8 of the suites are r0's inherited list (§5: RecordHeader
`configResolution`, WorkspaceShell `buildDynamicWorkspaceConfig`, `RichFilePreview`, `todoScoreMappings`,
ConversationView forward / emailInFlow, `TimelineComposeBox`, `surfaceLaunchRegistry`); the other two
(`MessageQuickView`, EmailComposer `templatePicker`) pass alone (**16/16**, `--runInBand`) — load, not a defect; none
imports the modal.

Once at the end, sequentially, after `dotnet format` (the hook's command; it changed nothing): BFF unit test project build
**0 warnings / 0 errors**; full BFF unit suite **14,681 passed, 0 failed, 54 skipped (14,735 = r5's 14,721 + 14)**,
22m25s; `Spaarke.ArchTests` **346/346**; `Sprk.Bff.Api.IntegrationTests` **104/104**; `Spe.Integration.Tests` **403
passed, 0 failed, 25 skipped (428)**. CVE: `dotnet list … package --vulnerable --include-transitive` on `Sprk.Bff.Api`
— no vulnerable packages (no package added). Publish size: skipped (harness) — main session. POML parses (minidom).
