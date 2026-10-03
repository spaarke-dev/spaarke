# Task 150 — lock `sprk_issecure` (field-level security; endpoint-only writes)

> GitHub #1067 · branch `task/uac-r2-150` (from `task/uac-r2-133-b2-r2` + `work/unified-access-control-r2`) ·
> 2026-10-02 · outcome: **partial** — code, scripts, tests and docs complete; every live step is a pending manual gate;
> the ribbon surface (step 5) is STOPPED on task 142.

## 1. What changed, in one paragraph

`sprk_issecure` is now set by the BFF and only the BFF. Provisioning no longer requires the record to arrive flagged: it
marks it secure as its **first write** (read back) after every pre-mutation refusal, and accepts a record that is already
flagged (old client, pre-150 rows) on exactly the same path. The client never writes the flag, and the Create Project
wizard runs provisioning **before** any child record, file or email, and adds none of them when secure was requested and
provisioning did not succeed (the ordering trap). The unsecure endpoint enforces owner decision **F3** (only a Full
Access holder or the record's creator may remove the designation) and refuses an EMPTY flag instead of answering
"already not secure". `RecordContainerResolver` refuses an ABSENT flag (owner's ABSENT-branch decision) on the record and
on every root above it. Two operator scripts (NULL backfill; the FLS lock, reusing task 133's profiles) and a standing
assertion (with a read-only live census) carry the platform configuration. Nothing was written live.

## 2. Owner rulings applied (all binding; none newly asked)

| Ruling | Where it comes from | How the shipped behaviour matches |
|---|---|---|
| Lock = FLS + endpoint-only writes | round 2 item 2 | §5, §7; no client writer remains (§3) |
| **F3** — removing Secure: Full Access holders + the creator; securing stays open to Write holders | round 3b | `UnsecureProjectEndpoint.RefuseUnlessPermittedToRemoveAsync`: creator (`createdby` / `sprk_createdbyperson`) first, then Dataverse's own rights on the record must include Write AND Delete (Full Access = Collaborate + Delete; an administrator qualifies by role). 403 `sdap.unsecure.not_permitted` otherwise |
| **F4** — System Administrator stays a residual writer (platform, not narrowable) | consolidated questions (accepted as recommended, round 3) | Documented (guide §7c, schema doc); the lock script and the standing assertion LIST every holder; nobody's role is touched |
| **F5** superseded → a ribbon button in task 142's shared Access group | round 3b + UX amendment | **STOPPED** — 142 has not merged (§9) |
| **F6** — the agent drafts copy options, the owner picks before merge | consolidated questions | Option A is implemented and marked DRAFT in code; options in §6. **Merge gate: owner pick** |
| **F7** — "secure an existing record" ships with 148 | consolidated questions | No surface added; Make Secure is not in any ribbon (§9). See §11.3 for the API |
| **ABSENT branch** — backfill NULL → No, then absent fails closed | `droppedAsAnswered` in `raw/session27-owner-questions.json` (the standing fail-closed directive, ADR-003) | §5.3; backfill script §7.1 |
| **Q1** — one-time NULL cleanup, default No, operator script recording counts + ids | UX note (O1/Q1, 2026-10-01) | `scripts/Repair-SecureFlagNulls.ps1` (dry run executed read-only, §8) |
| **R6** — a related record may be unsecured by F3 holders; refusal under a still-secure parent is task 158 | round 6 | NOT implemented here (158). F3's check is the one 158 will reuse per related record |
| **R5 / #1081 peer report** — root-BU reach and the Spaarke Demo team's System Administrator are dev artifacts | rounds 5, peer report | Listed by the lock script / assertion as residual writers (the Spaarke Demo TEAM shows up there); no exception coded, no action in dev |
| Task 133's FLS profiles are reused, not forked | task context | The lock script REFUSES when the profiles are missing; an agreement test pins both scripts to the same names |

## 3. Step 1 — inventory of writers and readers

### 3.1 Live, read-only (spaarkedev1, 2026-10-02)

| Surface | Query | Result |
|---|---|---|
| Field security on the column | `EntityDefinitions(...)/Attributes(LogicalName='sprk_issecure')` | `IsSecured = false` on all three; `DefaultValue = false`; `CanBeSecuredFor{Read,Create,Update} = true` |
| `fieldpermission` rows | `attributelogicalname eq 'sprk_issecure'` | **0** |
| Profiles | `fieldsecurityprofiles` | task 133's `Spaarke BFF-Managed Field Readers/Writers` **do not exist yet** (133's schema gate has not run); task 141's `Spaarke Identity Link Readers` is on **6 of 6** default teams — the reader-on-every-default-team mechanism works live |
| Forms | every `systemform` of the three tables, `formxml` scanned client-side (positive control: 7 forms match `sprk_accesspermission`) | **0** reference `sprk_issecure` (confirms the UX amendment's premise correction) |
| Views | every `savedquery` of the three tables (25), fetch + layout scanned | **0** |
| Business rules / classic workflows / cloud flows / actions | every `workflow` definition of category 0/1/2/5 (26), body fetched and scanned | **0** reference `sprk_issecure`; none is bound to the three tables |
| Plugin steps | `sdkmessagefilter` on the three tables | platform "ObjectModel / External plug-in implementation" steps only (no Spaarke plugin — ADR-002) |
| Values | `sprk_issecure eq null / true / false` | project 9 / 1 / 9 · matter 18 / 0 / 42 · work assignment 11 / 0 / 11 · **sprk_invoice 4 NULL of 10** (a FOURTH carrier — found by this task's code review; the metadata-driven registry treats it as securable). **Every NULL row predates the column** (newest NULL 2026-03-15; column created 2026-03-17; oldest non-NULL after it) |
| Business units | `businessunits` + default teams + enabled users | 6 BUs, 6 default teams; enabled users: root `Spaarke` 169, `Spaarke Business Unit 1` 1, others 0 |
| System Administrator holders | role associations | users: 2 humans (`Ralph Schroeder`, `Delegated Admin`) + application users incl. `# mi-bff-api-dev`, `SDAP-BFF-SPE-API`; TEAM `Spaarke Demo` (every member) |

Escalation trigger 2 ("a flow/workflow/business rule writes it as a user") **did not fire**. The schema doc's "Lock Secure
Project Flag After Creation" business rule does not exist live (corrected).

### 3.2 Code (grep, case-insensitive)

**Writers** — before: `projectService.ts:283-284` (client create, user context) and `UnsecureProjectEndpoint` (clear, app).
After: `ProvisionProjectEndpoint.EnsureSecureFlagAsync` (set, app) and `UnsecureProjectEndpoint` (clear, app). **No file
under `src/client` writes it** (`projectService.test.ts` pins the payload).

**Readers, by identity** — each is unmasked by the configuration in §4; the ones that changed behaviour are marked.

| Reader | Identity | Empty value handled as | Changed? |
|---|---|---|---|
| `RecordContainerResolver` (record + ancestor walk; every upload path, communications via `CommunicationContainerResolver`) | app-only (`IGenericEntityService`) | **refuse** `secure_flag_unreadable` 503 | yes (§5.3) |
| `ProvisionProjectEndpoint` (Step 1 + read-back) | app-only | a failed read-back refuses `secure_flag_not_set` | yes |
| `UnsecureProjectEndpoint` (Step 1) | app-only | **refuse** `sdap.unsecure.secure_flag_unreadable` | yes |
| `ExternalParticipationService:564/594` (FR-22 Secure suppression), `ExternalDataService:181/197`, `ExternalGrantLifecycle`, `InternalShareEndpoints`, `SubjectStandingGrantReader`, `RecordOwnershipResolver` | app-only | not secure | **no** — see §11.1 |
| client `RecordContainerResolver.ts`, `TrackingFieldTrio/index.ts:459/1066`, `AccessGrantModal` gating | user (`Xrm.WebApi`) | not secure / "unreadable" gating | no — the reader profile on every default team covers every user |
| external SPA `ProjectPage.tsx:508` | via the BFF's app-only DTO | no badge | no |

## 4. Step 2 — the FLS design

- **Reader profile** `Spaarke BFF-Managed Field Readers` (task 133's): Read on the column, associated with **every
  business unit's default team**. Membership of a default team is automatic, so every current and future user of an
  existing BU reads the true value; a new BU is a re-run of `Set-RecordCreatorPersonSchema.ps1 -Apply` (it adds every
  default team) and the standing assertion fails until it is done. Live evidence the mechanism works: task 141's
  identical reader profile is on 6/6 default teams. **Escalation trigger 1 did not fire.**
- **Writer profile** `Spaarke BFF-Managed Field Writers` (task 133's): Read/Create/Update; members = the BFF application
  user(s) only, associated explicitly (D-13: the app ids are a setup input, never hard-coded).
- **System Administrator** profile: full, created by the platform, not narrowable — F4.
- **The masked window**: the Web API cannot create a field permission on an unsecured column (0x8004f508), so a single
  solution import of IsSecured + permissions is not available on this path; the lock script secures each column and
  grants both profiles immediately, measuring the window per table. During it the task 150 BFF refuses, never mis-routes.
- **Live verification on "a user in each BU"** is a post-lock gate (G-8): before the lock nothing is masked to verify.

## 5. Server changes

### 5.1 `ProvisionProjectEndpoint`
- The 400 "is not secure — mark it secure before provisioning" is gone. `EnsureSecureFlagAsync` (Step 4.1 forward; the
  same call before the resume's first share): skip when already `true`; else PATCH `true`, read back
  `{id},sprk_issecure`, refuse 500 `sdap.provision.secure_flag_not_set` unless it reads `true`. Placed after every read
  and refusal, before the shared-container unlink — so every refusal before it still leaves "nothing changed" true and
  the record unflagged, and every failure after it leaves the record flagged (uploads refused).
- Compensation never clears it (pinned).
- The existing 409 for a provisioned record is unchanged and writes nothing.

### 5.2 `UnsecureProjectEndpoint`
- Step 1 reads `_createdby_value` too. An EMPTY flag → 500 `secure_flag_unreadable`, nothing written.
- Step 1.5 (F3): caller via WhoAmI (unresolved → 403 `permission_unverifiable`); creator = `createdby` or, in its own
  query, `sprk_createdbyperson` (400 = column missing → admits nobody; other failure → 500 `permission_unverifiable`);
  otherwise `RetrievePrincipalAccess` (OBO) must include Write AND Delete; else 403 `not_permitted`, whose detail names
  who CAN remove it (the ribbon will show it as is).
- The owner fallback reuses the caller id F3 established (no second WhoAmI); `owner_unresolved` is now defensive only.

### 5.3 `RecordContainerResolver`
- The ABSENT branch's comment states the new invariant; ABSENT on the record (securable) or on any securable root above
  it throws `secure_flag_unreadable` (503). An explicit `false` still resolves as before. Response detail names entity
  types only; the root's id goes to the log.

### 5.4 Tests (all in KEEP paths)
- `tests/integration/data-mutation/ExternalAccess/SecureFlagEndpointWriteTests.cs` (26): first-write ordering on all
  three roots; already-flagged ≡ unflagged; 409 unchanged; refused write / not-applied write / empty read-back stop
  with nothing else written; pre-mutation refusal leaves it unflagged; compensation keeps the flag; resume sets it before
  the share; F3 positive (creator, recorded creator person, Full Access) and negative (Collaborate-level colleague ×3
  root types, unreadable person, missing column, no Write ×2); empty flag on unsecure.
- `RecordContainerResolverTests` (+4), `ChildRecordContainerResolutionTests` (+2, 2 converted), fixture switches in
  `ProvisionProjectTestFixture` (`CallerHoldsDelete`, `SecureFlagReadsEmpty`, `SecureFlagWriteFails`,
  `SecureFlagWriteNotApplied`).
- Converted (premise changed by the owner): `ProvisionProject_WhenTheProjectIsNotSecure_IsRejected…` →
  `…NotYetFlagged_MarksItSecureAndProvisions`; `Unsecure_WhenNoOwnerCanBeResolved…` → `…CallerCannotBeIdentified…`
  (refused earlier, by F3); `WorkAssignment_NullFlag…ResolvesItsBusinessUnit` → `…FlaggedNo…` + a new `AbsentFlag_IsRefused`;
  two fixtures now seed an explicit `false` on a work-assignment row (post-backfill reality).

## 6. Client changes, and the F6 copy options (owner picks before merge)

Code: `projectService.ts` (flag never written), `CreateProjectWizard.tsx` (provisioning first; `secureBlocked` gates
work assignment, event, upload + document records, email; one held-back warning), `provisioningService.ts` (new code,
environment copy, `describeHeldBackForSecure`), `SummarizeFilesDialog.tsx` (two branches), `projectFormTypes.ts` (doc).
Tests: `projectService.test.ts` (+2), `provisioningService.test.ts` (+2, code count 26 → 27),
`CreateProjectWizard.secureHoldBack.test.tsx` (new, 10).

`SecureProjectSection.tsx`'s "An administrator can remove the secure designation later" is **unchanged and still true**
(an administrator holds Delete, so F3 admits them; no self-service unsecure surface ships until the ribbon). Rewording it
to "you, or someone with Full Access" belongs with the Remove Secure command (task 142 / this task's ribbon part).

Each string below is **implemented as option A** and marked DRAFT in code:

| # | Where | A (implemented) | B | C |
|---|---|---|---|---|
| 1 | Held back (wizard) | "Because securing the project did not finish, these were not added to it, so nothing reached shared storage: {list}. Add them once the project is secured." | "Securing the project did not finish, so nothing else was added to it — {list}. Add them after it is secured, so they are stored securely." | "{List} {was/were} not added: the project is not secured yet, and anything added now would be stored where other people can reach it. Add them once it is secured." |
| 2 | Environment refusal (`secure_bu_not_found` …) | "… The project was created but not secured, and nothing about it changed; an administrator can secure it once the setup is fixed." | "… The project was created as an ordinary project for now; an administrator can secure it once the setup is fixed." | "… The project was created, but it could not be secured, and nothing about it changed. An administrator needs to fix the setup and then secure it." |
| 3 | `secure_flag_not_set` | "The project could not be marked secure, so securing it stopped before anything else changed: its ownership, sharing and document storage are as they were." | "Securing the project did not start: it could not be marked secure. Nothing else about it changed." | "The project could not be marked secure. Nothing else was changed." |
| 4 | Summarize Files, no BFF | "The project was created but not secured, because securing it needs a connection to the Spaarke service that this dialog does not have. An administrator can secure it." | "The project was not secured: this dialog cannot reach the Spaarke service. It was created as an ordinary project; an administrator can secure it." | "The project was created. It could not be secured from here; an administrator can secure it." |
| 5 | Summarize Files, unexpected error | "Securing the project did not finish ({error}). The project was created; an administrator can check how far securing it got and finish it." | "The project was created, but securing it did not finish ({error}). An administrator can check it and finish securing it." | "Securing the project did not finish ({error}); an administrator needs to check it." |
| 6 | Unsecure F3 refusal (server ProblemDetails, shown by the ribbon) | "Only someone with Full Access to this {record}, or the person who created it, can remove its secure designation. It is still secure, and nothing was changed." | "You can't remove the secure designation: that needs Full Access to this {record}, or being the person who created it. Nothing was changed." | "Removing the secure designation needs Full Access or being the creator. The {record} is still secure." |

**Row 2 on a RESUME (verifier r1, item 10; an input to the F6 pick).** Options A, B and C for row 2 are accurate on a
FIRST call only: every environment refusal comes before the flag write, so the project is unflagged and unchanged. On a
resume (a retry after a partial run: the wizard's or Summarize Files' retry host after a retryable failure), or for a row
an older client flagged, the project is already flagged, and on a resume it is already owned by the secure team. "Not
secured / nothing about it changed" then overstates it, and the refusal does not say which case applies. A
resume-neutral option **D**: "Secure projects cannot be set up in this environment right now — its Secure Record business
unit, owner team or document storage is missing or not in a safe state. The project was created, but securing it could
not be finished; an administrator can finish securing it once the setup is fixed." Option A stays implemented (DRAFT)
until the owner picks; the code comment now states the resume case.

The wizard's existing no-BFF copy ("… created as a normal project; an administrator can secure it.") was inaccurate
before (the client had flagged it) and is accurate now; it is unchanged.

## 7. Scripts and the standing assertion

### 7.1 `scripts/Repair-SecureFlagNulls.ps1` (Q1)
Dry run default · `-Apply` (PATCH `If-Match: *`, sets the default to No if it is not) · `-Verify` · JSON report with
before/after counts and every id. **Must run before the task 150 BFF deploys.**

### 7.2 `scripts/Set-SecureFlagFieldSecurity.ps1`
Reuses task 133's profiles (never creates them, never edits membership). Preconditions p1–p5 (profiles in SpaarkeCore;
reader on every default team; writer = exactly the BFF app users; no NULL rows; `-ClientNoLongerWritesFlag`) — `-Apply`
refuses unless all pass. Then per table: secure → reader grant → writer grant (window measured), other-writer FAIL,
System Administrator holders listed, publish. `pwsh -File` comma-list fixed (found on the first dry run).
**r1 (item 15):** the GET-then-PUT of the attribute is proven live only on string/lookup columns; `sprk_issecure` is a
Boolean (the dry run now prints the type: `#Microsoft.Dynamics.CRM.BooleanAttributeMetadata` on all three tables,
2026-10-03). If the plain PUT is refused, the script retries ONCE with the Boolean cast and `$expand=OptionSet`; any
other type, or a second refusal, throws. A refused PUT leaves `IsSecured` unchanged on that table (tables are secured
and granted one at a time), so nothing is masked. Still unproven live: watch the first table at G-4.

### 7.3 Standing assertion (the sibling of `SecureBuRoleDepthAssertion`)
`tests/integration/auth/UnifiedAccessControl/SecureFlagFieldSecurityAssertion.cs` + `…AssertionTests.cs`: clauses
(1) locked on all three tables, (2) reader profile Read on each + on every default team, (3) writer profile R/C/U on each
and members exactly the BFF app users, (4) no other writer except System Administrator; holders listed (F4). 14
perturbations (both directions, incl. the AC's "census lacking the BFF read" and "an extra writer"), an opt-in live census
(`SPAARKE_NFR05_DATAVERSE_URL` + `SPAARKE_BFF_APPLICATION_IDS`), and `SecureFlagFieldSecurityScriptAgreementTests`
(both scripts and task 133's agree on column, tables and profile names; 5 seeded drifts).

## 8. Live runs performed (read-only only)

| Run | Result |
|---|---|
| `Repair-SecureFlagNulls.ps1` dry run | default No ×3; WOULD set 9 / 18 / 11 NULL rows (ids in the report); zero writes |
| `Set-SecureFlagFieldSecurity.ps1` dry run | FAIL p1 ×2 (task 133's profiles not created), FAIL p4 ×3 (NULLs); WOULD secure ×3; SysAdmin holders listed; zero writes |
| Standing assertion, live census | **FAIL (5)** — NOT LOCKED ×3, READER PROFILE 0, WRITER PROFILE 0 — the correct pre-lock verdict; residual writers listed |

## 9. Stops and pending manual gates

**STOP — ribbon (step 5).** Task 142's shared Access group (`infrastructure/dataverse/ribbon/AccessRibbons/`,
`Spaarke.Access.Ribbon`) has not merged (no branch exists). Per the UX amendment the ribbon work stops here and is told
to the orchestrator; no group, file or script was created. When 142 lands: add **Remove Secure** (enabled when secure
and the caller has Write; F3 enforced server-side; refusal shows the ProblemDetails detail, option 6 above) to 142's
group; **Make Secure** stays out of the ribbon until 148's transition ships (F7), and its confirmation copy is owner-
authored. Acceptance (a)–(f) and the four amendment UI tests are not met.

**Pending live gates — run by the main session, in this order (task 150 step 6).** Each: dry run → `-Apply` → `-Verify`.

| Gate | Command | Notes |
|---|---|---|
| G-0 | `.\scripts\Repair-SecureFlagNulls.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -Apply` then `-Verify` | **Before G-1, and in dev before task 150 merges to master** (peers deploy master to dev: #1081 report, `5e39f2bea`); in every other environment before it receives a BFF carrying task 150. The task 150 BFF refuses uploads (503) to the 42 NULL rows (38 on the roots + 4 invoices; the script discovers every carrier of the column) and their children. Harmless to the old BFF (NULL and false already route the same). No deploy script enforces it (r1 item 11) |
| G-1 | Deploy the BFF carrying task 150 | after task 133's own ordering (its schema gate first) |
| G-2 | Deploy the client (`@spaarke/ui-components` consumers: Create Project wizard, Summarize Files) and confirm no cached old bundle is served | the old client's create payload names `sprk_issecure` |
| G-3 | `.\scripts\Set-RecordCreatorPersonSchema.ps1 -EnvironmentUrl … -BffApplicationIds 5967251e-171c-46fe-a6c2-ef843c90309d,1e40baad-e065-4aea-a8d4-4b7ab273458c -Apply` then `-Verify` | task 133's gate — creates both profiles and their members |
| G-4 | `.\scripts\Set-SecureFlagFieldSecurity.ps1 -EnvironmentUrl … -BffApplicationIds … -ClientNoLongerWritesFlag -Apply` then `-Verify` | record the masked window per table |
| G-5 | standing assertion live: `$env:SPAARKE_NFR05_DATAVERSE_URL=…; $env:SPAARKE_BFF_APPLICATION_IDS=…; $env:AZURE_TOKEN_CREDENTIALS='AzureCliCredential'; dotnet test tests/unit/Sprk.Bff.Api.Tests --filter "FullyQualifiedName~SecureFlagFieldSecurity_InTheTargetEnvironment"` | must PASS |
| G-6 | NEGATIVE: as an existing non-admin test user with Write, `PATCH sprk_projects(<id>) {"sprk_issecure":false}` (and on a matter / work assignment), and a create naming it | refused, or unchanged on read-back. The user must hold no System Administrator, directly or through a team: **not a member of the "Spaarke Demo" team** (it holds System Administrator — #1081 peer report; the lock script's dry run lists it, 2026-10-03). If no such user with Write exists, ask the owner to create one (round 4 item 1) |
| G-7 | NO MASKING: the same user's `GET …?$select=sprk_issecure` on the secure project returns `true` (present); the client `RecordContainerResolver.ts` resolves it to its own container; the BFF logs no `secure_flag_unreadable` | |
| G-8 | a user in each populated BU (root `Spaarke`, `Spaarke Business Unit 1`) reads the true value | escalation trigger 1's live check |
| G-9 | Unsecure F3 live: a Collaborate-level colleague → 403 `not_permitted`; the creator and a Full Access holder → 200 | through the API until the ribbon ships |
| G-10 | Wizard: secure + attached file with provisioning forced to fail — a LOCAL BFF pointed at dev with a misconfigured `SecureRecord:OwnerTeamName`, never the shared dev BFF | no `sprk_document`, no file in any container, the held-back warning |

**Merge gates (all three hold before task 150 reaches master):**
1. **F6:** the owner picks the copy options in §6 (row 2 now has a resume-neutral option D).
2. **G-0 in dev first** (r1 item 11): `Repair-SecureFlagNulls.ps1 -Apply` then `-Verify` exits 0 in spaarkedev1.
3. **Task 133 merges first, or together** (r1 item 12): `task/uac-r2-150` is stacked on `task/uac-r2-133-b2-r2`, which
   is not in `work/unified-access-control-r2`. F3 trusts `sprk_createdbyperson`, which is safe only under 133's FLS
   lock on that column, so 150 must never land without 133 (and 133's own live gate G-3 precedes 150's G-4 anyway).

## 10. Placement and justification (CLAUDE.md §10 / §11)

**Placement.** Everything server-side extends EXISTING BFF surface: two existing endpoints and one existing resolver —
no new endpoint, service, DI registration, option, job or package (bff-extensions.md: modifying existing endpoints, no
new surface). The BFF is where the flag's only writer must live (the app identity is the only FLS writer). No plugin
(ADR-002); FLS is platform configuration.

| New item | Existing | Extension | Cost of doing nothing |
|---|---|---|---|
| Reason codes `secure_flag_not_set`, `not_permitted`, `permission_unverifiable`, `secure_flag_unreadable` (×2) | the endpoints' `reasonCode` contracts | added to them | the client cannot tell "could not mark secure" / "not allowed to unsecure" / "masked flag" from other failures |
| `scripts/Repair-SecureFlagNulls.ps1` | none does the NULL backfill (grep `issecure eq null` over `scripts/`) | not an extension of 133's script, which creates a column; this is a data fix with a report | the ABSENT refusal would refuse every upload to the 38 pre-column rows |
| `scripts/Set-SecureFlagFieldSecurity.ps1` | `Set-RecordCreatorPersonSchema.ps1` owns the profiles + members | REUSES its profiles (refuses without them); adds only the column's IsSecured + permissions | the column stays writable by every Write holder (the defect) |
| `SecureFlagFieldSecurityAssertion` (tests) | `SecureBuRoleDepthAssertion` checks roles, not field permissions; no FLS check exists (grep `fieldpermission` over `tests/`) | sibling at the same KEEP path, kept separate per the POML | the BFF's FLS Read stays an unchecked operator step — the standing-grant precedent shows it then stays unapplied |
| `describeHeldBackForSecure` (client) | none | a pure helper beside the classification copy | the user is not told why the files/children they asked for are missing |

No new Dataverse column (the lock is on an existing one). `sprk_accesspermission` is untouched.

## 11. Residuals and observations (not fixed here)

1. **Other app-only readers still map an EMPTY flag to "not secure"** (§3.2, unchanged row). With the BFF's writer
   membership (which carries Read) they are never masked, and the standing assertion fails if that membership is lost.
   Making each refuse would be a scope expansion across the external plane; recorded for the owner / a follow-up.
2. **A team-owned record with a container and the flag cleared** (only a System Administrator can do that now) answers
   409 `already_provisioned`; provisioning does not re-flag it. Edge case under F4.
3. **The provision API accepts an existing non-secure record** (it always did: before, a form edit plus the call did
   it). F7 gates the SURFACE (Make Secure ships with 148); the API itself does not distinguish "just created".
4. **`sprk_invoice.sprk_issecure` exists and is NOT locked** (default No, not field-secured; the owner's lock covers the three roots). Under the ABSENT refusal its NULLs must be backfilled (the script does); a Write holder can still change it. Setting it true only makes uploads to that invoice fail closed; clearing it leaves the ancestor walk to decide. Whether invoices should carry the flag at all — or be locked too — is an owner question.
5. The client `RecordContainerResolver.ts` and the PCF still treat empty as not secure — covered by the reader profile on
   every default team (G-7/G-8 prove it live).
6. **OWNER QUESTION (r1 item 13, F7 residual; not answered by rounds 1–9).** After the lock, `/provision-project` is
   the single-call way to secure ANY unflagged, unprovisioned record the caller can Write, including a colleague's
   ordinary record reached through BU-depth Write, without task 148's transition (which carries the children). Before
   the lock the same took a flag write plus the call, so the capability is not new, and the POML's rollout constraint
   requires accepting unflagged records. Round 3b ("securing stays open to Write-holders") covers WHO may secure, not
   whether the API may secure an EXISTING record ahead of 148. Route-sweep "other observations" row
   (`provision-project`, `ShareToCreatorAndPrincipalsAsync`) flags the same Write-only gate. Options: (a) acknowledge
   it as accepted until 148; (b) restrict an UNFLAGGED record to its creator (`createdby` / `sprk_createdbyperson`),
   leaving already-flagged rows on the Write gate; (c) restrict to records created in the last N minutes. **Not
   implemented** (a behaviour change awaiting the owner); recommended (b), which matches the wizard's only use and
   leaves "secure an existing record" to 148's surface.
7. **Handoff to task 146 and the route-sweep fix (r1 item 14).** FLS gives the BFF application identity Update on
   `sprk_issecure` everywhere, so "only the two endpoints write it" holds only while every OTHER app-only generic writer
   that takes maker- or user-supplied column names refuses the column: `POST /api/v1/field-mappings/push` (route sweep
   row 67: admin-configured mapping rules write child columns app-only) and task 146's planned app-identity AI creates
   (`DataverseCreateRecordHandler`, `EmailDraftToolHandler`, round 7 item 3). Each must deny `sprk_issecure` (and
   `sprk_createdbyperson`) as a target column. Setting it true only fails closed (uploads refuse until provisioned);
   clearing it on a secure record is the defect this task closes. Recorded here and in the POML; 146 and the row 67 fix
   own it.
8. **#1081 peer report (relayed 2026-10-02).** The root team "Spaarke" now holds Spaarke Basic User (whole-org read for
   every root-team member, Secure Record included) and the "Spaarke Demo" team holds System Administrator. Both are dev
   artifacts under round 5; nothing is coded. For this task: System Administrator holders write `sprk_issecure` by
   platform rule (F4), and the lock script's dry run (2026-10-03, read-only) LISTS the "Spaarke Demo" team among them;
   G-6 must use a user outside that team.

## 12. Seeded-violation proofs (each restored and touched afterwards)

Server: S1 flag never written (11 fail) · S2 read-back ignored (2) · S3 F3 admits every Write holder (5) · S4 empty flag
read as not secure (1) · S5 Full Access = Write only (4) · S6 forward flag write removed (10) · S7 flag written after the
owner move (6) · S8 resolver ABSENT on the record read as not secure (4) · S9 ABSENT on an ancestor (1) · S10–S12 the
assertion's default-team / BFF-membership / extra-writer clauses disabled (1 each). Client: C1 the client writes the flag
again (1) · C2 the hold-back removed (5) · C3 provisioning skipped/after the children (5).

## 13. Verifier round 1 (2026-10-03, branch `task/uac-r2-150-r1`)

| Item | Disposition |
|---|---|
| 1–7 | Verifications of the shipped code, tests and merge; nothing to change |
| 8 | **Fixed.** `UnsecureProjectEndpoint.RefuseUnlessPermittedToRemoveAsync` remarks: an unestablished caller identity → 403 `sdap.unsecure.permission_unverifiable`; an unreadable creator person → 500 (same reason key, retryable) |
| 9 | **Fixed.** `ProvisionProjectEndpoint` Step 1 heading: confirms only that the record exists |
| 10 | **Recorded for F6** (§6, option D, resume-neutral) and the client comment now states the resume case. Copy unchanged |
| 11 | **Merge gate** (§9): G-0 in dev before master; guide §7c step 0 and handoff S13 say the same. Not enforced by any deploy script |
| 12 | **Merge gate** (§9): 133 first, or together |
| 13 | **Owner question** (§11.6), recommended (b). Not implemented |
| 14 | **Handoff** (§11.7) to task 146 and the route-sweep row 67 fix |
| 15 | **Hardened** (§7.2): Boolean cast + `$expand=OptionSet` retry once; dry run confirms the type. Still watch G-4 |
| 16 | Open — main session: publish size against a fresh master, and the CVE check (no package change in this task) |
| 17–20 | Open — live gates G-3/G-4/G-8, G-6, G-7, G-5 (§9) |
| 21–22 | Open — STOPPED on task 142 (and 148 + owner copy for Make Secure) |
| 23 | Open — F6 merge gate |
| 24 | Open — publish size/CVE (main session); UI jest failures in untouched suites pre-exist on the work branch |
| 25 | **Fixed** by 8 and 9 |
| #1081 | G-6 user must be outside the "Spaarke Demo" team (§9, §11.8) |

**Tests (r1).** Affected BFF classes (SecureFlag, SecureProjectShare, ProvisionProject, RecordContainerResolver,
ChildRecordContainerResolution, RecordKeyedUploadAuthorization, UnsecureProject): 358/358. Full BFF unit suite: 14383
passed, 0 failed, 54 skipped (14437). `Spaarke.ArchTests`: 346/346. Jest (CreateProjectWizard + SummarizeFilesWizard):
9 suites, 117/117 (after building the local `Spaarke.SdapClient` and `Spaarke.Auth` packages, which a fresh worktree
lacks). `@spaarke/ui-components` `tsc` build: exit 0. `Set-SecureFlagFieldSecurity.ps1`: parse 0 errors; dry run
against spaarkedev1 read-only, zero writes. No live write.
