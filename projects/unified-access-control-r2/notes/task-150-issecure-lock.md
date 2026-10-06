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
| **F6** — the agent drafts copy options, the owner picks before merge | consolidated questions; **rows 1-6 picked in round 10 item 9** (2026-10-03) | **Rows 1-6 closed (c1, §15):** option A for rows 1, 3, 4, 5, 6 and the resume-neutral option D for row 2; their DRAFT markers removed, each string pinned verbatim by a test. **Rows 7-11 closed (c1-r2, §17) by owner round 13 item 10:** option B throughout; the alternatives removed, every DRAFT marker gone, each string pinned verbatim |
| **Round 13 item 6** — task 146's shared F3 helper (`SecureDesignationRemoval`) replaces this task's private F3 check at integration, with ITS behaviour | round 13 (2026-10-03) | Recorded, not coded here (no fork): §17, item 18 |
| **Round 10 item 10** — `/provision-project` secures an UNFLAGGED record only for its creator | round 10 (answers §11.6, recommended (b)) | **Closed (c1 §15 forward path; c1-r1 §16 resume)** — 403 `sdap.provision.not_record_creator` before any write, on a forward run AND on a resume of an unflagged record; flagged rows stay on the Write gate |
| **Round 10 item 11** — invoices follow their matter | round 10 (answers §11.4) | **Closed (c1, §15)** — `sprk_invoice` out of the securable registry; its column locked by the same script |
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
| Forms, views, bound workflows and plugin steps of **`sprk_invoice`** (c1-r1, 2026-10-03 — the table joined the lock in c1, round 10 item 11) | `systemforms?$filter=objecttypecode eq 'sprk_invoice'` with `formxml` scanned client-side; `savedqueries` (`returnedtypecode eq 'sprk_invoice'`, fetch + layout); `workflows` (`primaryentity eq 'sprk_invoice'`); `sdkmessageprocessingsteps` under its 29 `sdkmessagefilter` rows | **0 of 5 forms** ("Invoice main form" type 2, "Invoice quick create form" type 7, and three "Information" forms of types 2, 6 and 11; all active) and **0 of 11 views** reference `sprk_issecure`; **0** workflows or business rules are bound to the table; steps are the platform's "ObjectModel Implementation" / "External plug-in implementation" only, none filtering on the column. Positive control: the main form binds 14 `sprk_` columns (incl. `sprk_containerid`, `sprk_name`, `sprk_invoicenumber`). The column-wide rows below (26 workflows; three configured-writer channels) already covered the invoice. **Disposition: no writer to remove; the lock may include the invoice** |
| Views | every `savedquery` of the three tables (25), fetch + layout scanned | **0** |
| Business rules / classic workflows / cloud flows / actions | every `workflow` definition of category 0/1/2/5 (26), body fetched and scanned | **0** reference `sprk_issecure`; none is bound to the three tables |
| Plugin steps | `sdkmessagefilter` on the three tables | platform "ObjectModel / External plug-in implementation" steps only (no Spaarke plugin — ADR-002) |
| Values | `sprk_issecure eq null / true / false` | project 9 / 1 / 9 · matter 18 / 0 / 42 · work assignment 11 / 0 / 11 · **sprk_invoice 4 NULL of 10** (a FOURTH carrier — found by this task's code review; the metadata-driven registry treats it as securable). **Every NULL row predates the column** (newest NULL 2026-03-15; column created 2026-03-17; oldest non-NULL after it) |
| Business units | `businessunits` + default teams + enabled users | 6 BUs, 6 default teams; enabled users: root `Spaarke` 169, `Spaarke Business Unit 1` 1, others 0 |
| Configured writers (r2, verifier F5; re-run 2026-10-03) | rows naming `sprk_issecure` as their WRITE target: `sprk_fieldmappingrule.sprk_targetfield` (Field Mapping Framework — the client wizards' payload engine and `/field-mappings/push`), `sprk_aitopicregistry.sprk_targetfield` (`WorkProductRecordPersister`), `sprk_emailupdatefield.sprk_targetfieldlogicalname` (communication propose) | **0** on all three (positive control: 25 field-mapping rules in total; source-field matches also 0). Now a standing check: the lock script's precondition **(p6)** and the standing assertion's **clause 5** |
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
| `ExternalParticipationService` (`GetRootRecordFlagsAsync` → `FlagsFrom`; FR-22 Secure suppression), and through it `ExternalGrantLifecycle` (grant policy), `InternalShareEndpoints` (last-reader rule), `AccessibleRecordSetService` (standing-grant / derived / org terms, which is how `SubjectStandingGrantReader`'s contribution is suppressed — that reader reads no `sprk_issecure`) | app-only | **`RootRecordFlags.Unreadable`** (secure + restricted + limited + unreadable) | **yes — round 17 item 3 (c1-r2-r2, §19)**; was "not secure" |
| `ExternalDataService` (project label for the external SPA) | app-only | **shown as secure** (`?? true`) + error log | **yes — round 17 item 3 (§19)** |
| `RecordOwnershipResolver.ReadParentAsync` (task 146's branch only; not on this branch) | app-only | "No" on 146's branch | **handed over** to integration (§19) — exact change recorded there |
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
  grants both profiles immediately, measuring the window per table. During it the task 150 BFF (unless its identity holds System Administrator) reads the flag EMPTY too, with the FULL effect of the G-0 row in §9 for those seconds — external participants' contact-sourced rights withheld, grants 503 `policy_unreadable`, the last-reader rule, uploads 503, provisioning `secure_flag_not_set`: it refuses, never mis-routes (round 26 item 2(c)).
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
- `tests/integration/data-mutation/ExternalAccess/SecureFlagEndpointWriteTests.cs` (26; 31 after r2, §14): first-write ordering on all
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

## 6. Client changes, and the F6 copy (rows 1-6 picked in round 10 item 9; rows 7-11 picked in round 13 item 10)

Code: `projectService.ts` (flag never written), `CreateProjectWizard.tsx` (provisioning first; `secureBlocked` gates
work assignment, event, upload + document records, email; one held-back warning), `provisioningService.ts` (new code,
environment copy, `describeHeldBackForSecure`), `SummarizeFilesDialog.tsx` (two branches), `projectFormTypes.ts` (doc).
Tests: `projectService.test.ts` (+2), `provisioningService.test.ts` (+2, code count 26 → 27),
`CreateProjectWizard.secureHoldBack.test.tsx` (new, 10).

`SecureProjectSection.tsx`'s "An administrator can remove the secure designation later" is **unchanged and still true**
(an administrator holds Delete, so F3 admits them; no self-service unsecure surface ships until the ribbon). Rewording it
to "you, or someone with Full Access" belongs with the Remove Secure command (task 142 / this task's ribbon part).

**Owner pick (round 10 item 9, 2026-10-03): option A for rows 1, 3, 4, 5 and 6; option D (below) for row 2.** Implemented
in round c1 (§15); no DRAFT marker remains on rows 1-6, and each picked string is pinned verbatim by a test. Rows 7-11,
below the table, were picked too (option B, owner round 13 item 10), implemented in c1-r2 (§17) and pinned verbatim; no
DRAFT marker remains on any row. The table is kept as the record of what was offered for rows 1-6:

| # | Where | A (implemented for rows 1, 3, 4, 5, 6; row 2 ships D, below) | B | C |
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
not be finished; an administrator can finish securing it once the setup is fixed." **The owner picked D for row 2
(round 10 item 9); it is implemented since c1** and pinned verbatim by `provisioningService.test.ts`.

**Rows 7-11 — PICKED: option B throughout (owner round 13 item 10, 2026-10-03, BINDING; implemented in c1-r2, §17).**
Round c1 added two reason codes for round 10 item 10 (`sdap.provision.not_record_creator`,
`sdap.provision.record_creator_unverifiable`); c1-r1 added one more server `detail` (row 11: the unflagged RESUME whose
caller cannot be identified, reason `creator_unresolved`, whose client copy is task 133's existing authored string). The
options A/B/C offered in c1-r1 are removed per the owner's ruling; what ships, each pinned VERBATIM by a test
(`provisioningService.test.ts` rows 7-8; `SecureFlagEndpointWriteTests` rows 9-11, row 9 for all three root types):

| # | Where | Shipped (option B) |
|---|---|---|
| 7 | Client, `not_record_creator` (`provisioningService.ts`) | "Only the person who created this project can secure it this way. Nothing about the project changed." |
| 8 | Client, `record_creator_unverifiable` (`provisioningService.ts`) | ~~"Who created this project could not be checked, so it was not secured. Nothing about the project changed."~~ **Since round 17 item 2 (c1-r2-r2, §19), resume-neutral:** "Who created this project could not be checked, so securing it could not be finished. Nothing about the project changed." |
| 9 | Server `detail`, `not_record_creator` (`ProvisionProjectEndpoint.NotRecordCreator`) | "Only the person who created this {record} can secure it this way, and you did not create it. Nothing was changed." |
| 10 | Server `detail`, `record_creator_unverifiable` (`ProvisionProjectEndpoint.RecordCreatorUnverifiable`) | "Who created this {record} could not be looked up, so whether you may secure it could not be checked. Nothing was changed; you may try again." |
| 11 | Server `detail`, unflagged resume, caller unidentified (`creator_unresolved`, `RefuseUnflaggedResumeUnlessCreatorAsync`) | "Your account could not be confirmed, so whether you created this {record} could not be checked. Nothing was changed; you may try again." |

`{record}` is `SecureRecordRoot.DisplayLabel` lower-cased ("project", "matter", "work assignment"). Option B drops A's
"Securing the project did not start", which overstated an (anomalous) unflagged RESUME; row 8's "so it was not secured"
remained until round 17 item 2 replaced it with resume-neutral wording (§11.9, §19).

The wizard's existing no-BFF copy ("… created as a normal project; an administrator can secure it.") was inaccurate
before (the client had flagged it) and is accurate now; it is unchanged.

## 7. Scripts and the standing assertion

### 7.1 `scripts/Repair-SecureFlagNulls.ps1` (Q1)
Dry run default · `-Apply` (PATCH `If-Match: *`, sets the default to No if it is not) · `-Verify` · JSON report with
before/after counts and every id. **Must run before the task 150 BFF deploys.**
**c1-r2 fix (verifier c1-r1 items 7 and 13):** `-Apply`'s after-check counted `@(Get-DvAll …).Count`; `Get-DvAll` emits
its List as ONE object (`, $rows`), so the count was always 1 and every `-Apply` reported "after: 1 row(s) still hold
NULL" per table and exited 1, even after a full repair (seen live at G-0 in dev, 2026-10-03). It now counts the List
itself. Proven offline by a harness that shadows `az` / `Invoke-RestMethod` (§17). **Pinned since c1-r2-r2 (§19):**
`SecureFlagFieldSecurityScriptAgreementTests.TheBackfillScript_CountsTheRowsGetDvAllReturns_NotTheSingleListItEmits`
fails if any `@(Get-DvAll …)` reappears, or if `Get-DvAll` stops ending `, $rows` (the guard's premise).

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
**r2 (verifier F3):** a GRANT failing after the PUT landed (12 retries on 0x8004f508 exhausted, or any other error) used
to stop the script with that table secured and no reader permission — the fail-open state. It now REVERTS `IsSecured`
on that table (and publishes it, and reads it back) before rethrowing; if the revert fails too it prints
`!!! RECOVERY REQUIRED NOW` with two remedies (re-run `-Apply`, or clear "Enable column security" in the maker portal).
Re-running `-Apply` is a real resume path now: on a column already secured it grants only the missing permissions (it
used to skip a secured table entirely). **r2 (verifier F5):** precondition **(p6)** fails when any field-mapping rule, AI
topic-registry row or email update field targets `sprk_issecure`. All four paths exercised OFFLINE by a scratchpad
harness that shadows `az` / `Invoke-RestMethod` (grant fails → reverted, IsSecured back to false; grant + revert fail →
recovery banner, IsSecured stays true; resume on a secured table → both grants made; configured writer → `-Apply`
REFUSED, nothing written). The r1 script under the same harness leaves the table secured and ungranted (F3 bites) and
does not grant on resume.

### 7.3 Standing assertion (the sibling of `SecureBuRoleDepthAssertion`)
`tests/integration/auth/UnifiedAccessControl/SecureFlagFieldSecurityAssertion.cs` + `…AssertionTests.cs`: clauses
(1) locked on all three tables, (2) reader profile Read on each + on every default team, (3) writer profile R/C/U on each
and members exactly the BFF app users, (4) no other writer except System Administrator; holders listed (F4). 14
perturbations (both directions, incl. the AC's "census lacking the BFF read" and "an extra writer"), an opt-in live census
(`SPAARKE_NFR05_DATAVERSE_URL` + `SPAARKE_BFF_APPLICATION_IDS`), and `SecureFlagFieldSecurityScriptAgreementTests`
(both scripts and task 133's agree on column, tables and profile names; 5 seeded drifts). **r2 (verifier F5):** clause
(5) no configuration row (field-mapping rule, AI topic registry, email update field) names the column as its target —
census field `ConfiguredWriters`, channels pinned in `ConfiguredWriterChannels`, entity sets resolved live (never guessed);
+4 tests (one per channel, plus the channel list).

## 8. Live runs performed (read-only only)

| Run | Result |
|---|---|
| `Repair-SecureFlagNulls.ps1` dry run | default No ×3; WOULD set 9 / 18 / 11 NULL rows (ids in the report); zero writes |
| `Set-SecureFlagFieldSecurity.ps1` dry run | FAIL p1 ×2 (task 133's profiles not created), FAIL p4 ×3 (NULLs); WOULD secure ×3; SysAdmin holders listed; zero writes |
| Standing assertion, live census | **FAIL (5)** — NOT LOCKED ×3, READER PROFILE 0, WRITER PROFILE 0 — the correct pre-lock verdict; residual writers listed |
| r2, 2026-10-03: `Set-SecureFlagFieldSecurity.ps1` dry run (with p6) | as before (p1 ×2, p4 ×3) plus **OK (p6)** — the new live queries resolve all three entity sets; zero writes |
| r2, 2026-10-03: Dataverse MCP `read_query` | 0 rows targeting / sourcing `issecure` in `sprk_fieldmappingrule` (of 25), `sprk_aitopicregistry`, `sprk_emailupdatefield` |

## 9. Stops and pending manual gates

**Ribbon (step 5) — BUILT on the integration branch (§21, §22), pending its live gate G-11.** ~~STOP~~ superseded: task
142's Access group merged into `integ/uac-r2-batch4`; Make Secure and Remove Secure are in 142's ONE group, ONE ribbon file
and ONE command script, Make Secure release-gated to task 148 AND round 26 item 3's wired file relocation (§22.4), held to
the Write gate by its transition (round 33 item 1), confirmed with owner round 27's copy; Remove Secure confirmed with
round 33 item 2's copy.

**Pending live gates — run by the main session, in this order (task 150 step 6).** Each: dry run → `-Apply` → `-Verify`.

| Gate | Command | Notes |
|---|---|---|
| G-0 | `.\scripts\Repair-SecureFlagNulls.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -Apply` then `-Verify` | **Before G-1, and in dev before task 150 merges to master** (peers deploy master to dev: #1081 report, `5e39f2bea`); in every other environment before it receives a BFF carrying task 150. **Full effect of a task 150 BFF before G-0 (round 26 item 2(c)):** every NULL-flag root (42 in dev before G-0: 38 on the roots + 4 invoices; the script discovers every carrier of the column) reads `RootRecordFlags.Unreadable` — secure AND Restricted AND inactive — in the ONE shared flag reader, so: external participants lose EVERY contact-sourced right on it (direct and organization grants, standing-grant membership, organization expansion); grants on it answer 503 `policy_unreadable`; the internal last-reader rule applies to removing a user's share; uploads to it and its children answer 503 `secure_flag_unreadable`; the external SPA labels it secure; unsecure and the child-ownership pass refuse it. Every effect is a refusal (nothing mis-routes), but the records are unusable externally and for uploads until G-0. (Provisioning alone treats NULL as unflagged and writes the flag itself.) Guide §7d "Shipping the task 150 BFF before step 0". Harmless to the old BFF (NULL and false already route the same). No deploy script enforces it (r1 item 11). **DEV: PASS 2026-10-03** (main session, ahead of integration, work branch `92d5f3cc1`, `notes/batch4-live-gates-2026-10-03.md`, run with the `task/uac-r2-150-r2` script): dry run 42 NULL rows (project 9, matter 18, work assignment 11, invoice 4); `-Apply` set all 42 to No; `-Verify` PASS on 4 tables. That `-Apply` printed "1 row(s) still hold NULL" per table and exited 1 — the counting defect fixed in c1-r2 (§7.1, §17), not residual NULLs. **Still required in every other environment** |
| G-1 | Deploy the BFF carrying task 150 | after task 133's own ordering (its schema gate first) |
| G-2 | Deploy the client (`@spaarke/ui-components` consumers: Create Project wizard, Summarize Files) and confirm no cached old bundle is served | the old client's create payload names `sprk_issecure` |
| G-3 | `.\scripts\Set-RecordCreatorPersonSchema.ps1 -EnvironmentUrl … -BffApplicationIds 5967251e-171c-46fe-a6c2-ef843c90309d,1e40baad-e065-4aea-a8d4-4b7ab273458c -Apply` then `-Verify` | task 133's gate — creates both profiles and their members |
| G-4 | `.\scripts\Set-SecureFlagFieldSecurity.ps1 -EnvironmentUrl … -BffApplicationIds … -ClientNoLongerWritesFlag -Apply` then `-Verify` | record the masked window per table — **four tables since c1** (`sprk_invoice` included: round 10 item 11; approved for the main session at integration by round 11). **Run it straight after G-1 (and G-3) in every environment, with no gap:** between the task 150 BFF deploy and the lock, an already-flagged row bypasses the creator rule (§11.10), so the gap is a security exposure, not just a pending step |
| G-5 | standing assertion live: `$env:SPAARKE_NFR05_DATAVERSE_URL=…; $env:SPAARKE_BFF_APPLICATION_IDS=…; $env:AZURE_TOKEN_CREDENTIALS='AzureCliCredential'; dotnet test tests/unit/Sprk.Bff.Api.Tests --filter "FullyQualifiedName~SecureFlagFieldSecurity_InTheTargetEnvironment"` | must PASS |
| G-6 | NEGATIVE: as an existing non-admin test user with Write, `PATCH sprk_projects(<id>) {"sprk_issecure":false}` (and on a matter / work assignment), and a create naming it | refused, or unchanged on read-back. The user must hold no System Administrator, directly or through a team: **not a member of the "Spaarke Demo" team** (it holds System Administrator — #1081 peer report; the lock script's dry run lists it, 2026-10-03). If no such user with Write exists, ask the owner to create one (round 4 item 1) |
| G-7 | NO MASKING: the same user's `GET …?$select=sprk_issecure` on the secure project returns `true` (present); the client `RecordContainerResolver.ts` resolves it to its own container; the BFF logs no `secure_flag_unreadable` | |
| G-8 | a user in each populated BU (root `Spaarke`, `Spaarke Business Unit 1`) reads the true value | escalation trigger 1's live check |
| G-9 | Unsecure F3 live: a Collaborate-level colleague → 403 `not_permitted`; the creator and a Full Access holder → 200 | through the API until the ribbon ships |
| G-10 | Wizard: secure + attached file with provisioning forced to fail — a LOCAL BFF pointed at dev with a misconfigured `SecureRecord:OwnerTeamName`, never the shared dev BFF | no `sprk_document`, no file in any container, the held-back warning |
| G-11 | Access ribbon (§21, §22, §23, §24, §25): after G-1 (BFF with 148 + 150 — including round 46's and round 53's `can-manage-access` `includeOwner` answer with `owningTeamInSecureBusinessUnit`, and round 53's `caller_rights_unverifiable` — and, for Make Secure, round 26 item 3's relocation wired at integration with its scheduled backstop: 147's `SecureChildReconciliationJob` settling pending Make Secure relocations, writes on — §22.4, §24.2); after **the DEFAULT-TEAM part of task 144's migration in that environment** (round 53 item 2, a live gate before the ribbon ships; release order round 60 item 2: `pwsh scripts/Migrate-SecureRecordsToNamedOwnerTeam.ps1 -EnvironmentUrl …` dry run, then `-Apply -AcceptedAssignCascade <what the dry run reported>`; complete when the dry run's plan has no MIGRATE rows and the retired default team no longer holds the Secure Record Owner role — §25.2); and the web resources (`sprk_/scripts/access_ribbon.js` **1.5.0**, `assignedaccess_postsave.js`, `bff_auth.js`): `pwsh infrastructure/dataverse/ribbon/AccessRibbons/Set-AccessRibbon.ps1 -SecureTransitionDeployed` (dry run), then `-EnvironmentUrl … -SolutionName <ribbon solution> -SecureTransitionDeployed -BffBaseUrl <BFF> -ApiScope api://<id>/.default -Apply` (first CHECKS, read-only, that `secure-child-reconciliation` is enabled every 2 minutes and its latest run settled Make Secure relocations in write mode — round 46 item 2, refused otherwise; then exports, checks in the work-assignment ribbon — commit it — merges, imports, verifies), then the POML ui-tests | `-Verify` exit 0; the amendment ui-tests pass, plus round 40's "Make Secure finishes an unfinished transition" ui-test (§23.1), round 46's "Make Secure finishes a secure record not owned by the Secure Record Owners team" ui-test (§24.4 — also the G-11 check of legacy secure records still user-owned, the verifier's item 8; since round 53 it includes the hidden case for another team inside the Secure Record business unit), and round 53's "Make Secure run by the record's owner" and "Make Secure run by an administrator" ui-tests (§25.3 — the only proof of a REAL ownership-held or role-held Delete; the code delegates to `RetrievePrincipalAccess`). Then, AFTER the ribbon, the migration's full `-Verify` exit 0, once Make Secure's "finish" has settled the NOT-ISOLATED rows (user-owned legacy records, records owned by a team outside the Secure Record business unit, flagged records left before the owner move — round 60 item 2: the ribbon is the tool that settles them). Omit `-SecureTransitionDeployed` where the BFF lacks task 148's transition, the wired relocation or its backstop |

**Merge gates (all hold before task 150 reaches master):**
1. **F6: MET.** Rows 1-6 closed by owner round 10 item 9 (c1, §15); rows 7-11 closed by owner round 13 item 10
   (option B, c1-r2, §17). No DRAFT marker remains in code or tests; every string is pinned verbatim.
2. **G-0 in dev first** (r1 item 11): **MET in dev** — `-Verify` PASS on spaarkedev1, 2026-10-03 (G-0 row above). The
   `-Apply` exit 1 seen there was the after-check defect fixed in c1-r2; with the fix, `-Apply` exits 0 after a full
   repair, so the guide §7c order ("each must exit 0") holds.
3. **Task 133 merges first, or together** (r1 item 12): `task/uac-r2-150` is stacked on `task/uac-r2-133-b2-r2`, which
   is not in `work/unified-access-control-r2`. F3 trusts `sprk_createdbyperson`, which is safe only under 133's FLS
   lock on that column, so 150 must never land without 133 (and 133's own live gate G-3 precedes 150's G-4 anyway).
   **MET on the integration tree (§22.5 item 21):** `task/uac-r2-133-c1-r3.-r2` is an ancestor of the integration branch,
   so 133 and 150 land together when `integ/uac-r2-batch4` merges; 150 reaches `work/*` only through it.

Escalation trigger 2's precondition for G-4 — the live writer inventory of every table the lock covers — now includes
`sprk_invoice` (c1-r1, §3.1: 0 of 5 forms, 0 of 11 views, no bound workflow or business rule, platform plugin steps only).

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

1. **CLOSED by round 17 item 3 (c1-r2-r2, §19).** *As raised:* other app-only readers mapped an EMPTY flag to "not
   secure". They now fail closed through the one shared flag reader (`ExternalParticipationService.FlagsFrom` →
   `RootRecordFlags.Unreadable`); `ExternalDataService` labels such a project secure; `RecordOwnershipResolver`'s read
   (task 146's branch) is handed over with its exact change (§19).
2. **A team-owned record with a container and the flag cleared** (only a System Administrator can do that now) answers
   409 `already_provisioned`; provisioning does not re-flag it. Edge case under F4.
3. **The provision API accepts an existing non-secure record** (it always did: before, a form edit plus the call did
   it). F7 gates the SURFACE (Make Secure ships with 148); the API itself does not distinguish "just created".
4. **ANSWERED — round 10 item 11, closed in c1 (§15).** *As raised:* **`sprk_invoice.sprk_issecure` exists and is NOT locked** (default No, not field-secured; the owner's lock covers the three roots). Under the ABSENT refusal its NULLs must be backfilled (the script does); a Write holder can still change it. Setting it true only makes uploads to that invoice fail closed; clearing it leaves the ancestor walk to decide. Whether invoices should carry the flag at all — or be locked too — is an owner question.
5. The client `RecordContainerResolver.ts` and the PCF still treat empty as not secure — covered by the reader profile on
   every default team (G-7/G-8 prove it live).
6. **ANSWERED — round 10 item 10 chose (b); closed in c1 (§15) on the forward path and in c1-r1 (§16) on a resume of an
   unflagged record.** *As raised:* **OWNER QUESTION (r1 item 13, F7 residual; not answered by rounds 1–9).** After the lock, `/provision-project` is
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
9. **CLOSED by round 17 item 2 (c1-r2-r2, §19):** row 8 and task 133's `creator_unresolved` client copy are now
   resume-neutral ("… securing it could not be finished …"), each pinned verbatim. *As raised:* **Copy on an (anomalous) unflagged RESUME (verifier c1-r1 item 9; c1-r2).** Since c1-r1 an unflagged resume answers
   `not_record_creator`, `record_creator_unverifiable` or `creator_unresolved`, and at that point the record is ALREADY
   owned by the Secure Record owner team. The owner's pick (round 13 item 10, option B) avoids A's "Securing the project
   did not start", and rows 7 and 9-11 ("Nothing … changed") are accurate for the refused call. Two strings still
   describe a first call only: row 8 ("…, so it was not secured") and task 133's existing client copy for
   `creator_unresolved` ("Securing the project did not start, because your account could not be confirmed. Nothing about
   the project changed." — not part of rows 7-11, unchanged). This is the same overstatement the owner removed from row 2
   with option D, but it is reachable only on an anomalous row: every documented resume meets a FLAGGED row (§16 item
   4), which stays on the Write gate and never answers these codes. Recorded for the owner; the binding pick is shipped
   as is.
10. **Pre-lock bypass of the creator rule through an already-flagged row (verifier c1-r1 item 10; closes at G-4).** An
   already-flagged row skips the creator rule (round 10 item 10: "already-flagged rows stay on the Write gate"). Until the
   FLS lock is live (G-4), any Write holder can set `sprk_issecure = true` through the MDA form or the Web API and then
   provision a colleague's ORDINARY record through the Write gate — the outcome round 10 item 10 forbids for unflagged
   records. This is the pre-lock state the lock removes (after G-4 only the BFF and System Administrators can set the
   flag, F4), not a new capability: before task 150 the same flag write plus the call did it. Owner-decided (round 10
   item 10 keeps flagged rows on the Write gate); it closes when G-4 is applied in each environment, so G-4 MUST follow
   the task 150 BFF deploy (G-1) with no gap in every environment (c1-r2-r2: the §9 G-4 row now says so).

## 12. Seeded-violation proofs (each restored and touched afterwards)

Server: S1 flag never written (11 fail) · S2 read-back ignored (2) · S3 F3 admits every Write holder (5) · S4 empty flag
read as not secure (1) · S5 Full Access = Write only (4) · S6 forward flag write removed (10) · S7 flag written after the
owner move (6) · S8 resolver ABSENT on the record read as not secure (4) · S9 ABSENT on an ancestor (1) · S10–S12 the
assertion's default-team / BFF-membership / extra-writer clauses disabled (1 each). Client: C1 the client writes the flag
again (1) · C2 the hold-back removed (5) · C3 provisioning skipped/after the children (5).
**r2:** S13 the flag written AFTER the Step 4.2 shared-container unlink (the verifier's own F1 seed: all four new
shared-container tests fail, both kinds × both directions) · S14 the F6 catch disabled (1) · S15 the assertion's clause
5 disabled (3) · script: the r1 lock script under the offline harness (F3 revert and resume both absent).

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

## 14. Verifier round 2 (2026-10-03, branch `task/uac-r2-150-r2`)

| Item | Disposition |
|---|---|
| F1 (MEDIUM) | **Fixed.** `SecureFlagEndpointWriteTests`: `Provision_ARecordCarryingASharedContainer_SetsTheFlagBeforeUnlinkingIt` (business-unit and configured shared container: the `sprk_issecure` write's `Sequence` precedes the `sprk_containerid = null` write) and `…_WhenTheFlagWriteIsRefused_NeverUnlinksIt` (`SecureFlagWriteFails`: no unlink attempted, the record keeps its link, owner unchanged, no grant, no container). Seed S13 (the verifier's reorder) fails all 4. The first-write criterion is now pinned against the unlink |
| F2 (LOW) | **Fixed.** `RecordContainerResolver.cs` co-mingle query comment and the not-found predicate's remarks now name the `secure_flag_unreadable` refusal (task 150), not the retired absent-flag warning |
| F3 (LOW) | **Fixed** (§7.2): revert-on-grant-failure, loud recovery banner, and a real `-Apply` resume path. Header STEPS (b) says so. Exercised offline only; G-4 is the live proof |
| F4 (LOW) | **Fixed.** `secure-project-fields-schema.md`: the column row, the Field Security row, the correction paragraph and the form note now state the FLS configuration as the TARGET, pending G-3/G-4 (live 2026-10-03: `IsSecured=false`, 0 `fieldpermission` rows) |
| F5 (INFO) | **Adopted** (§3.1 row; lock script (p6); standing assertion clause 5). Widened beyond field-mapping rules to the two other maker-authored write-target tables found by grep (`sprk_aitopicregistry`, `sprk_emailupdatefield`); all three are 0 live |
| F6 (INFO) | **Fixed.** The Full Access probe is wrapped: a throw (other than this request's own cancellation) refuses 500 `sdap.unsecure.permission_unverifiable`, before any write. Fixture switch `FullAccessProbeThrows` (throws on a record's SECOND probe: the first is the route's Write gate); test `Unsecure_WhenTheFullAccessCheckThrows_RefusesWithAReasonBeforeAnyWrite`; seed S14 fails it |
| 7 | Verifications; nothing to change |
| 8 | **Closed** by F1 |
| 9–12 | Open — live gates G-3/G-4/G-8, G-6 (a user outside "Spaarke Demo"), G-7, G-5 (§9). Read-only only in this round |
| 13 | Open — STOPPED on task 142 (and 148 + owner copy for Make Secure) |
| 14 | Open — main session: publish size against a fresh master build, and the CVE check (no package change) |
| 15 | Open — merge gates: F6 copy pick, G-0 in dev before any build carrying 150 is deployed to dev or merged, 133 first or together |
| 16 | Open — owner question §11.6 (recommended (b)) |

**#1081 (relayed again 2026-10-03, owner-decided 2026-10-02).** Already recorded (§11.8): root-team Basic User is a dev
artifact, nothing is codified, and the FLS design does not depend on roles (the reader profile sits on every default
team either way). Master `5e39f2bea` in dev does not carry task 150, so G-0's ordering is unchanged.

**Tests (r2).** Affected BFF classes (SecureFlag*, ProvisionRecordedContainer, UnsecureProject, RecordContainerResolver):
139/139. Full BFF unit suite: 14392 passed, 0 failed, 54 skipped (14446 = r1 + 9 new). `Spaarke.ArchTests`: 346/346.
Lock script: parse 0 errors; offline harness 4 scenarios; dry run against spaarkedev1 read-only, zero writes. No live write.

## 15. Fix round c1 (2026-10-03, branch `task/uac-r2-150-c1`) — owner rounds 10 and 11

Branched from `task/uac-r2-150-r2`, merged `work/unified-access-control-r2` (owner rounds 10-11; no conflict) at
`a0f941dec`. Every item below is an owner decision from round 10 (2026-10-03); round 11 approved the live steps for the
main session at integration. Nothing was written live in this round. **The c1 work was never committed on
`task/uac-r2-150-c1`** (it stayed at `a0f941dec`); c1-r1 committed the verified snapshot unchanged as `6f7eb27f4` (§16).

| Item | Decision | What changed |
|---|---|---|
| 1. F6 copy | round 10 item 9: A for rows 1, 3, 4, 5, 6; resume-neutral D for row 2 | Rows 1, 3, 4, 5, 6 were already option A and are unchanged. Row 2 (`classifyProvisioningFailure`, every environment refusal) is now option D: "… The project was created, but securing it could not be finished; an administrator can finish securing it once the setup is fixed." Every DRAFT marker on rows 1-6 removed from code and tests (`provisioningService.ts` ×3, `SummarizeFilesDialog.tsx` ×2, the `describeHeldBackForSecure` test's describe title); each comment now cites the pick. Copy tests: each picked string pinned VERBATIM — row 1 singular + plural (`CreateProjectWizard.secureHoldBack.test.tsx`), row 2 and row 3 (`provisioningService.test.ts`; row 2 also asserts it claims neither "not secured" nor "nothing about it changed"), rows 4 and 5 (`SummarizeFilesDialog.provisioningHost.test.tsx`, new — row 5 overrides `provisionSecureProject` once to throw), row 6 (`SecureFlagEndpointWriteTests`, the ProblemDetails `detail` for all three root types) |
| 2. §11.6 | round 10 item 10: (b) creator-only for an UNFLAGGED record | `ProvisionProjectEndpoint.MoveWithCreatorShareAsync` (forward path), right after WhoAmI and before any write: when the record does not read `sprk_issecure = true`, `RefuseUnlessRecordCreatorAsync` admits only its creator — the caller is `createdby` (no read needed); else `createdby` is read and, when it is a PERSON (enabled or disabled), that person is the creator; when it is an application user or absent, the BFF-stamped `sprk_createdbyperson` decides. 403 `sdap.provision.not_record_creator` (extension `creatorColumn`) otherwise; a failed read is 500 `sdap.provision.record_creator_unverifiable` (retryable); a missing `sprk_createdbyperson` column (400) records nobody → `not_record_creator`. An already-flagged row stays on the Write gate. Client: both codes mapped (`not-started`; retryable false / true); `EMITTED` 27 → 29. **Extended to the resume in c1-r1 (§16)** |
| 3. §11.4 | round 10 item 11: invoices follow their matter | `SecurableEntityRegistry.FlagIsNotASecurityInput = { "sprk_invoice" }`, applied to the live metadata answer (`BuildCatalog`) AND to a cached value (`TryParseCacheEntry`, so a catalog cached by an older build — 6h TTL — is not read with the invoice securable). The invoice stays KNOWN (`NotSecurable`), so `RecordContainerResolver` reads its row for its typed matter / project / agreement links only — never its flag or a container of its own — and the ancestor walk decides. Readers grepped (every `sprk_issecure` / `SecureFlagAttribute` / `ISecurableEntityRegistry` use in `src/server`): the invoice flag reached security only through the registry (`RecordContainerResolver.ResolveCoreAsync`, its ancestor hops and `ResolveOwningRecordAsync`; `CommunicationContainerResolver`); every other reader (`ExternalParticipationService`, `ExternalDataService`, `ExternalGrantLifecycle`, `InternalShareEndpoints`, `ExternalCallerContext`, the two endpoints) reads the three ROOT tables only; nothing outside `Sprk.Bff.Api` reads the column. Lock: `scripts/Set-SecureFlagFieldSecurity.ps1` `$Tables` gains `sprk_invoice` (same profiles, preconditions, revert/resume, dry run / `-Apply` / `-Verify`; NOT run with `-Apply`); `scripts/Repair-SecureFlagNulls.ps1` lists it statically (it was discovered before); the standing assertion's `Tables` gains it. The shared test double `TestEntityCatalog` mirrors the rule, as its contract requires. **The invoice's live form inventory was missing; done in c1-r1 (§3.1, §16)** |
| 4. Ribbon | — | Unchanged: Make Secure / Remove Secure stay **STOPPED** on tasks 142 (the Access group) and 148 (Make Secure's transition). These are dependencies, not owner questions (§9) |

**Interpretation recorded in c1 — SUPERSEDED by c1-r1 (§16, verifier c1 item 4).** c1 bound the creator rule to the
FORWARD path only and left a RESUME of an unflagged record on its Write gate, "so an administrator's documented recovery
still works". The verifier showed every documented resume meets a FLAGGED row, so the exemption admitted only anomalous
states; c1-r1 applies the literal rule to unflagged resumes too.

**Tests (c1)** — added by the c1 implementer; the counts below are the **c1 verifier's**, run on the uncommitted snapshot
(diff sha256 `BD52084C…`), because c1 recorded none:
- Server, new: `SecureFlagEndpointWriteTests` +15 — not-creator refused ×3 root types, creator provisioned ×3,
  app-created for the recorded person, app-created for someone else, disabled-person `createdby` vs recorded person,
  unreadable `createdby`, unreadable recorded person, missing column, flagged row on the Write gate ×3; row 6 verbatim
  (existing theory tightened). `SecurableEntityRegistryTests` +5 (invoice not securable but known; cached pre-rule
  value; invoice flagged true under an ordinary matter → fallback, flag never selected; invoice under a secure matter
  → the matter's container, flag false and true). `SecureFlagFieldSecurityAssertionTests` +3 (invoice not locked fails;
  invoice-lock ⇔ registry exclusion; parser drift for the invoice).
- Server, converted (premise changed by the owner): `ChildRecordContainerResolutionTests.SecurableChild_ItselfSecure_KeepsItsOwnContainer`
  → `AnInvoiceFlaggedSecure_FollowsItsMatter` (secure / ordinary matter); `ChildRecordRead_RequestsEveryLinkAndIntermediateColumn`
  (the invoice row no longer selects `sprk_issecure` / `sprk_containerid`); `RecordContainerResolverTests.BareInvoice_…`
  (the alias reading now follows the invoice's matter link, not the invoice's own flag); the lock-script agreement
  (`lockScript.Tables` = the profile script's three roots + the invoice).
- Affected BFF classes: 356/356. Full BFF unit suite: 14415 passed, 1 failed, 54 skipped (14470) — the one failure,
  `ComposeUploadProjectionSeamTests.Upload_RealDocx`, timed out after 3m4s under machine load ("client aborted the
  request") and passes on an isolated re-run; unrelated to this task. `Spaarke.ArchTests`: 346/346.
  `Sprk.Bff.Api.IntegrationTests`: 104/104. `Spe.Integration.Tests`: 403 passed, 25 skipped, 0 failed (428).
- Client: jest `CreateProjectWizard` + `SummarizeFilesWizard`, 9 suites, 123/123 (r1 117 + 6 new/tightened);
  `@spaarke/ui-components` `tsc --noEmit` exit 0. Scripts: parse 0 errors (both).

**Seeded-violation proofs (c1; each restored from a copy and touched).** S16 creator gate disabled → 8 fail · S17 gate
applied to flagged rows too → 3 fail · S18 a disabled-person `createdby` deferred to the recorded person → 1 fail ·
S19 an unreadable creator folded into "not the creator" → 2 fail · S20 the live-answer exclusion removed → 3 fail ·
S21 the cached-value exclusion removed → 1 fail · S22 the exclusion set emptied (registry + shared double) → 8 fail ·
S23 the invoice dropped from the lock script → 1 fail · C4 row 2 back to option A → 1 fail · C5 the
`not_record_creator` mapping dropped → 2 fail · C6 rows 4 and 5 reworded → 2 fail. The c1 verifier re-ran its own
(creator gate off 8; BuildCatalog exclusion off 3; gate always on 3; cache `ExceptWith` off 1; disabled-person
fall-through + both unverifiable catches returning null 3; client row 2 to A + rows 4, 5 altered 3).

**Placement (CLAUDE.md §10 / §11).** No new endpoint, service, DI registration, option, job, package or column. New
surface, each extending an existing contract:

| New item | Existing | Extension | Cost of doing nothing |
|---|---|---|---|
| Reason codes `sdap.provision.not_record_creator`, `sdap.provision.record_creator_unverifiable` | the endpoint's `reasonCode` contract (grep `ReasonResumeColleaguesNotPermitted`: the nearest, resume-only) | added to it; client `REASON_STATES` gains both | the owner's refusal "with a stable reason code" has no code; a failed creator read would be indistinguishable from "not the creator" |
| `RefuseUnlessRecordCreatorAsync` (private) | `ResolveResumeCreatorAsync` (same column order, resume-only, decides whom to SHARE to) and the unsecure endpoint's F3 creator check | reuses `UnusablePersonStateAsync`, `ReadCreatorPersonAsync`, `IsColumnMissing`; a separate method because its answer differs (admit/refuse the caller, never a person to share to) | any Write holder can secure a colleague's ordinary record ahead of task 148, leaving its children and files behind |
| `SecurableEntityRegistry.FlagIsNotASecurityInput` | the metadata-derived securable set (no exclusion mechanism exists — grep `Securable` in `Infrastructure/Dataverse`) | one owner-ruled exclusion inside the same registry, applied in its two catalog paths | an invoice flagged true under an ordinary matter refuses every upload, and the invoice's own flag is read before its matter — the opposite of the owner's rule |
| `sprk_invoice` in the lock script, the backfill script and the standing assertion | the same three artefacts for the roots | one table added to each | a user-writable flag that looks meaningful and that the old BFF still reads |

ADR-002: no plugin. ADR-003: every unknown refuses (creator unreadable → 500; column missing admits nobody; the invoice
exclusion narrows only what the owner ruled). No `.claude/**` edit is needed.

**Residuals (c1).**
1. The client `RecordContainerResolver.ts` derives securability from metadata, so it still treats `sprk_invoice` as
   securable: an invoice flagged true with no container refuses a client-side upload (fail closed), and an invoice under
   a secure matter is not resolved through the matter on that path (the client has no ancestor walk — a pre-existing
   gap, task 155's scope). After G-4 nobody but the BFF and System Administrators can set the invoice's flag, and
   nothing does. Recorded for the owner / a follow-up.
2. ~~The two new reason codes carry client copy written in this round … not an F6 merge gate~~ — **withdrawn in c1-r1
   (§16, verifier c1 item 5):** that ruling was the agent's, not the owner's; the copy is DRAFT, rows 7-11 of §6.

**Pending live gates (unchanged order, §9).** G-0 now also repairs the 4 NULL invoices (the script already did); G-4
locks four tables. Approved for the main session at integration by round 11; run nothing here.

## 16. Fix round c1-r1 (2026-10-03, branch `task/uac-r2-150-c1-r1`) — the c1 verifier's 16 items

Branched from `task/uac-r2-150-c1` (`a0f941dec`). First commit `6f7eb27f4`: the c1 snapshot the verifier checked, applied
from its saved diff (sha256 `BD52084C47567184ED99CB672417CA174AA67E2D3AE5888D07BCE36218DC0CCC`, 22 files), compared
file-by-file against the c1 worktree (identical), committed through the hooks (lint-staged ran prettier and
`dotnet format`; neither changed a byte). Second commit: the items below. No live write; the only live calls were the
read-only GETs in item 6.

| # | Verifier item | Disposition |
|---|---|---|
| 1 | Nothing committed | **Closed.** `6f7eb27f4` = the verified snapshot; the hooks re-ran on it |
| 2 | §15 cited but absent; POML `<tests>` dangling | **Closed.** §15 appended (the c1 draft, its placeholders filled with the c1 verifier's measured counts, attributed as such); the POML's c1 `<tests>` now carries the counts |
| 3 | §6 stale ("Option A stays implemented (DRAFT)", "A (implemented)", heading "(owner picks before merge)") | **Closed.** §6 heading, table header and the row-2 paragraph now state the picks (A for 1, 3-6; D for 2, implemented since c1) |
| 4 | Unflagged RESUME exempt from the creator rule | **Closed — literal rule applied.** The verifier's analysis holds from the code: since task 150 the flag is the forward path's first write and compensation never clears it; before task 150 provisioning required it; `UnsecureProjectEndpoint` moves the owner (Step 3) before clearing the flag (Step 5). So every documented resume meets a FLAGGED row, and the c1 exemption admitted only anomalous rows. The resume branch now calls `RefuseUnflaggedResumeUnlessCreatorAsync` FIRST when the row is not flagged: WhoAmI (unresolved → 403 `creator_unresolved`), then the same `RefuseUnlessRecordCreatorAsync` as the forward path — all before any write. A flagged resume stays on the Write gate. A System Administrator who must finish an anomalous unflagged row sets the flag first (F4), which puts it on the Write gate. Guide §7c says the same |
| 5 | New copy for `not_record_creator` / `record_creator_unverifiable` not owner-picked | **STOPPED — owner pick required (escalation trigger 5; not answered by rounds 1-11).** The c1 ruling "not an F6 merge gate" is withdrawn. Every such string is marked DRAFT in code and offered as options A/B/C in §6 rows 7-11 (client rows 7-8, server rows 9-10, and row 11 — the new unflagged-resume `creator_unresolved` detail). Option A ships until the owner picks; F6 is a merge gate again for rows 7-11 (§9) |
| 6 | No form inventory for `sprk_invoice` | **Closed (read-only, spaarkedev1).** §3.1 row: 0 of 5 forms and 0 of 11 views reference `sprk_issecure`; no workflow or business rule is bound to the table; plugin steps are the platform's only. Positive control: the main form binds 14 `sprk_` columns. Disposition: no writer; the invoice may be locked at G-4 |
| 7 | A DISABLED application user in `createdby` read as a disabled person | **Closed.** `UnusablePersonStateAsync` checks `applicationid` before `isdisabled`: an application user is `application-user` whether enabled or not. Effect: the forward/unflagged-resume creator rule defers to `sprk_createdbyperson` for it (as for an enabled one); a resume's refusal names it `application-user` rather than `disabled` (so it no longer suggests re-enabling an app). New test pins it |
| 8 | Verifications | Nothing to change |
| 9 | Verifier's seeds | Nothing to change; recorded in §15 |
| 10 | Verifier's suite counts on the snapshot | Recorded in §15 |
| 11 | Round-11 item 1 / DRAFT markers on rows 1-6 met only in the working tree | **Closed** by item 1 (committed). The new-copy part is item 5 (STOPPED) |
| 12 | Round-10 item 10 met on the forward path only | **Closed** by item 4 |
| 13 | Round-10 item 11 not committed; invoice inventory missing | **Closed** by items 1 and 6 |
| 14 | Suites green but counts unrecorded; publish size + CVE not done | Counts: §15 (c1) and below (c1-r1). **CVE: done** — `dotnet list package --vulnerable --include-transitive` on `Sprk.Bff.Api`: "no vulnerable packages given the current sources" (no package change in this task). **Publish size: not measured** — skipped per this round's instruction; the main session measures it against a fresh master build (CLAUDE.md §10) |
| 15 | §15 absent; §6 stale | **Closed** by items 2 and 3 |
| 16 | Live gates G-0, G-3/G-4, G-5, G-6, G-7, G-10; 133 first or together | **Open — main session at integration** (round 11 approval; read-only here). Unchanged order, §9 |

**Tests (c1-r1).**
- Server, new in `SecureFlagEndpointWriteTests` (+9 cases): `Provision_ResumingAnUnflaggedRecord_ByAWriteHolderWhoDidNotCreateIt_IsRefusedBeforeAnyWrite`
  ×3 root types (403 `not_record_creator`; no update, grant, modify or container; flag still false; owner still the
  team; no container recorded); `…_WhenTheCallerCannotBeIdentified_IsRefusedBeforeAnyWrite` (403 `creator_unresolved`);
  `Provision_ResumingAnUnflaggedAppCreatedRecord_ByThePersonRecordedAsItsCreator_Resumes`;
  `Provision_ResumingAFlaggedRecord_ByAWriteHolderWhoDidNotCreateIt_StaysOnTheWriteGate` ×3 (200; the share goes to the
  creator, not the caller; no flag write); `Provision_AnUnflaggedRecord_CreatedByADisabledApplicationUser_IsSecuredForThePersonRecordedAsItsCreator`.
  `Provision_ResumingAnUnflaggedRecord_SetsTheFlagBeforeTheShare` unchanged (its caller is the creator); summary reworded.
- Affected BFF classes (SecureFlag*, ProvisionProject*, ProvisionRecordedContainer, UnsecureProject,
  RecordContainerResolver, ChildRecordContainerResolution, SecurableEntityRegistry, SecureProjectShare,
  RecordKeyedUploadAuthorization, CommunicationContainerResolver): 442/442. `SecureFlagEndpointWriteTests`: 55/55.
- Full BFF unit suite, run ONCE at the end under heavy machine contention (9 concurrent test hosts from other agents):
  14347 passed, **78 failed**, 54 skipped (14479 = c1 14470 + the 9 new). Every failure is a 2m17s–5m+ request timeout
  ("TaskCanceledException … The client aborted the request") across 51 classes unrelated to this task (Office, Insights,
  Memory, SpeAdmin, Compose seams, Documents, Workspace, Eval, …), plus one task-133 resume test
  (`ProvisionResumeCreatorPersonTests.Resume_WhenCreatedByCannotBeRead_DoesNotFallThroughToTheColumn`, same timeout).
  Isolated re-run of those 51 classes: 707 passed, 16 failed (same timeouts, load still present), 9 skipped — the
  `ProvisionResumeCreatorPersonTests` case passed; re-run of the 15 remaining tests alone: **20/20 passed** (theory
  cases included). So: contention, not regressions; nothing fails on its own.
- `Spaarke.ArchTests`: 346/346. `Sprk.Bff.Api.IntegrationTests`: 104/104. `Spe.Integration.Tests`: 403 passed, 25
  skipped, 0 failed (428). CVE: `dotnet list package --vulnerable --include-transitive` (Sprk.Bff.Api): none.
- Client (comments only changed): jest `CreateProjectWizard` + `SummarizeFilesWizard` 9 suites, 123/123;
  `@spaarke/ui-components` `npm run build` (tsc) exit 0 (after `npm install --legacy-peer-deps` and building the local
  `Spaarke.Auth` / `Spaarke.SdapClient` packages, which a fresh worktree lacks).

**Seeded-violation proofs (c1-r1; each restored from a copy and touched).** S24 unflagged-resume gate disabled → 4
fail (the three non-creator resumes + the unidentified caller) · S25 the gate applied to FLAGGED resumes too → 14 fail
(the three flagged-resume theories + 11 existing task-133 resume tests in `ProvisionProjectIdempotencyTests`, flagged
rows whose callers are not, or cannot be shown to be, the creator) · S26 `isdisabled` checked before `applicationid` again → 1 fail (the disabled-application
-user test).

**Placement (CLAUDE.md §10 / §11).** No new endpoint, service, DI registration, option, job, package, column or reason
code. One new private method, `RefuseUnflaggedResumeUnlessCreatorAsync`: (1) Existing — `RefuseUnlessRecordCreatorAsync`
(the creator decision) and `RefuseResumeColleaguesUnlessCreatorAsync` (the resume's WhoAmI precedent); (2) Extension — it
IS a thin wrapper: WhoAmI, then the existing creator decision; it exists only because the resume path has no caller id
(the forward path resolves it inside `MoveWithCreatorShareAsync`); (3) Cost of doing nothing — a non-creator Write holder
secures an unflagged, team-owned, container-less record, contrary to round 10 item 10. ADR-002: no plugin. ADR-003: an
unresolved caller refuses, never "the creator". No `.claude/**` edit needed.

**Owner questions raised by this round.** Only F6 rows 7-11 (§6). The verifier's item 4 named the c1 interpretation as
owner-reversible; c1-r1 resolves it by applying round 10 item 10 as written, which needs no new decision.

## 17. Fix round c1-r2 (2026-10-03, branch `task/uac-r2-150-c1-r2`) — the c1-r1 verifier's 18 items + owner round 13

Branched from `task/uac-r2-150-c1-r1` (`2d7e14f62`). Owner round 13 (2026-10-03) items 6 and 10 are binding here. No live
write; no live call at all in this round.

| # | Verifier item | Disposition |
|---|---|---|
| 1 | Forward creator gate verified | Nothing to change |
| 2 | Unflagged-resume creator gate verified | Nothing to change |
| 3 | Invoice exclusion verified (registry, cache, resolver, readers, scripts, assertion) | Nothing to change |
| 4 | F6 rows 1-6 verified | Nothing to change |
| 5 | Verifier's seeds S1-S9 + client seed | Nothing to change; recorded |
| 6 | Verifier's suite counts | Recorded; this round's own counts below |
| 7 | `Repair-SecureFlagNulls.ps1:168` after-check always 1 | **Fixed.** `$after = (Get-DvAll …).Count` with a comment naming the defect. Offline harness (scratchpad, shadows `az` / `Invoke-RestMethod`; 4 tables, 3+1+0+2 NULL rows): unfixed `-Apply` → "after: 1 row(s) still hold NULL" on all 4 tables (incl. the table that had 0) and exit 1 — the live G-0 symptom reproduced; fixed `-Apply` → "after: no row holds NULL" ×4, PASS, exit 0; fixed `-Apply` with 2 PATCHes forced to fail → "after: 2 row(s) still hold NULL", exit 1 (the true count); `-Verify` and dry run unchanged |
| 8 | G-0 status stale | **Reconciled.** §9 G-0 row: DEV PASS 2026-10-03 (work branch `92d5f3cc1`, `notes/batch4-live-gates-2026-10-03.md`: 42 NULL → No; `-Verify` PASS on 4 tables); merge gate 2 met in dev; still required in every other environment. The G-0 entries in §16 item 16 and the c1 / c1-r1 POML `<open>` are superseded by this row |
| 9 | Copy on an unflagged RESUME | **Recorded for the owner (§11.9).** Round 13 item 10's option B removes A's "did not start"; row 8's "so it was not secured" and task 133's existing `creator_unresolved` client copy still describe a first call. Reachable only on an anomalous row; the binding pick ships as is |
| 10 | Flagged-row bypass before the lock | **Recorded as residual §11.10** (owner-decided; closes at G-4 in each environment) |
| 11 | Publish size not measured | **Not done** — this round's instruction: skip it; the main session measures against a fresh master build (CLAUDE.md §10). CVE: no package change in this round (clean in c1-r1) |
| 12 | Five DRAFT markers (rows 7-11) | **Closed** by item 17: none remains in `src/` or the tests (grep `DRAFT` over the five files: 0) |
| 13 | Guide order needs `-Apply` exit 0 | **Closed** by item 7: `-Apply` now exits 0 after a full repair; guide §7c unchanged (it was right; the script was wrong) |
| 14 | Publish size | As item 11 |
| 15 | Endpoint-backed secure/unsecure surface | **STOPPED** on tasks 142 (the Access ribbon group) and 148 (Make Secure's transition) — dependencies, unchanged (§9) |
| 16 | Pending live gates | G-0 PASS in dev (item 8). Open, main session at integration (round 11): G-1, G-2, G-3/G-4 (four tables), G-5, G-6 (user outside "Spaarke Demo"), G-7, G-8, G-9, G-10 (LOCAL BFF); G-0 in every other environment |
| 17 | Owner round 13 item 10: option B for rows 7-11 | **Done.** Client rows 7-8 (`provisioningService.ts`), server rows 9-11 (`ProvisionProjectEndpoint.NotRecordCreator`, `RecordCreatorUnverifiable`, `RefuseUnflaggedResumeUnlessCreatorAsync`) now ship option B verbatim; §6's alternatives removed. Tests: `provisioningService.test.ts` +2 (rows 7, 8 verbatim); `SecureFlagEndpointWriteTests` row 9 pinned in the not-creator theory (all three root types), row 10 in the unreadable-creator test, row 11 in the unidentified-caller resume test (helpers `DetailOf`, `LabelOf`) |
| 18 | Owner round 13 item 6: 146's shared F3 helper replaces the private check at integration | **Recorded, not coded (no fork).** See below |

**Item 18 — the F3 hand-over at integration (owner round 13 item 6, BINDING).** Task 146 (`task/uac-r2-146-c1-r1`,
`Services/Access/SecureDesignationRemoval.cs`) extracted this task's F3 check into one static rule,
`SecureDesignationRemoval.DecideAsync`, with the same two reason codes (`sdap.unsecure.not_permitted`,
`sdap.unsecure.permission_unverifiable`). At integration `UnsecureProjectEndpoint.RefuseUnlessPermittedToRemoveAsync`
(this task's private F3 check) is REPLACED by a call to it, and ITS behaviour ships — this branch does not copy or fork it.
The differences the integrator must carry into this task's tests:

| Case | Task 150 private check (this branch) | 146 shared helper (ships) | Test to update at integration |
|---|---|---|---|
| `sprk_createdbyperson` column missing (400), caller neither creator nor Full Access | 403 `not_permitted` | **403 `permission_unverifiable`** | `SecureFlagEndpointWriteTests.Unsecure_WhenTheCreatorPersonColumnIsMissing_AdmitsNobodyThroughIt` (expects `not_permitted`) |
| Recorded creator person unreadable, caller holds Full Access | 500 `permission_unverifiable` (the read comes BEFORE the rights probe) | **admitted** (Full Access is asked before the creator-person read) | `Unsecure_WhenTheRecordedCreatorPersonCannotBeRead_RefusesBeforeAnyWrite` — keep it for a non-Full-Access caller (still `permission_unverifiable`; check the status the helper's mapping gives), add the Full Access case as admitted |
| Rights probe throws, caller is the recorded creator person | 500 `permission_unverifiable` | admitted on the creator-person read; otherwise `permission_unverifiable` | `Unsecure_WhenTheFullAccessCheckThrows_RefusesWithAReasonBeforeAnyWrite` — its caller is not the creator, so it should still refuse; confirm |

The **provisioning** creator rule (round 10 item 10: `RefuseUnlessRecordCreatorAsync`, `RefuseUnflaggedResumeUnlessCreatorAsync`)
is NOT F3 and is not replaced: it admits only the creator (no Full Access branch). ~~A missing creator-person column
there still records nobody (403 `sdap.provision.not_record_creator`) … not decided anywhere.~~ **Decided by round 17
item 1 and implemented in c1-r2-r2 (§19):** a missing column there now answers 403
`sdap.provision.record_creator_unverifiable` with `creatorState: column-missing`, aligned with 146's helper.

**Tests (c1-r2).**
- Affected BFF classes (SecureFlag*, ProvisionProject*, ProvisionResume*, ProvisionRecordedContainer, UnsecureProject,
  RecordContainerResolver, ChildRecordContainerResolution, SecurableEntityRegistry, SecureProjectShare,
  RecordKeyedUploadAuthorization, CommunicationContainer*, SecureNamedOwnerTeam*): **481/481** (no new case; rows 9-11
  are assertions added to existing cases). `SecureFlagEndpointWriteTests`: 55/55.
- Full BFF unit suite, run ONCE at the end under contention (8 concurrent test hosts from other agents): 14334 passed, **91 failed**, 54 skipped (14479, the same total as c1-r1: rows 9-11 added no case). Every failure is a `TaskCanceledException` ("The operation was canceled") after a 2-4 minute request, across 46 classes, mostly unrelated (Office, Compose seams, Insights, Documents, Memory, Workspace, Eval, SpeAdmin, ...); two are task classes (`SecureFlagEndpointWriteTests`, `SecureNamedOwnerTeamProvisioningTests`), which passed 481/481 in the affected run minutes earlier. Isolated re-run of all 46 classes: **500 passed, 0 failed**. So: contention, not regressions. `Spaarke.ArchTests`: **346/346**. `Sprk.Bff.Api.IntegrationTests`: **104/104**. `Spe.Integration.Tests`: **403 passed, 25 skipped, 0 failed** (428).
- Client: jest `CreateProjectWizard` + `SummarizeFilesWizard`, 9 suites, **125/125** (123 + rows 7 and 8);
  `@spaarke/ui-components` `npm run build` (tsc) exit 0 (after `npm install --legacy-peer-deps --no-audit --no-fund` and
  building the local `Spaarke.SdapClient` / `Spaarke.Auth`, which a fresh worktree lacks).
- Script: offline harness, 4 runs (item 7).

**Seeded-violation proofs (c1-r2; each restored from a copy and touched).** S27 rows 9, 10 and 11 server copy each
reworded → 5 fail (the not-creator theory ×3, the unreadable-creator test, the unidentified-caller resume test) · C7 rows
7 and 8 client copy back to option A → 2 fail (the two new verbatim tests) · script: the unfixed after-check under the
harness → exit 1 with "1 row(s)" on every table (item 7).

**Placement (CLAUDE.md §10 / §11).** No new endpoint, service, DI registration, option, job, package, column, reason
code or script. Changes are copy strings, one script line and test assertions/helpers. ADR-002: no plugin. ADR-003: no
fail-closed path changed. No `.claude/**` edit needed.

## 18. Fix round c1-r2-r1 (2026-10-03) — the c1-r2 verifier's 15 items

**Branch.** The round's instruction named `task/uac-r2-150-c1-r1` as the new branch, but that name already exists (it is
the c1-r1 round, `2d7e14f62`, an ancestor of c1-r2, and is checked out in another worktree). Moving it would change
another worktree's branch under it, so this round works on **`task/uac-r2-150-c1-r2-r1`**, created from
`task/uac-r2-150-c1-r2` (`765749d4d`). Nothing else differs. No live call; no live write. No code changed in this round:
every item is a verification, a hand-off or a stop already recorded, and each was re-checked against code, not taken
on trust.

| # | Verifier item | Disposition (re-checked here) |
|---|---|---|
| 1 | Scope of the round (7 files, `2d7e14f62`..`765749d4d`) | **Confirmed.** `git diff --stat` shows exactly those 7 files |
| 2 | Rows 7-11 ship option B verbatim; §6 lists only the shipped copy; no DRAFT | **Confirmed.** Each shipped string compared with column B of §6 at `2d7e14f62` (rows 7-8 in `provisioningService.ts`; rows 9-11 in `ProvisionProjectEndpoint.NotRecordCreator`, `RecordCreatorUnverifiable`, `RefuseUnflaggedResumeUnlessCreatorAsync`, `{record}` = `DisplayLabel` lower-cased). Grep `DRAFT` over the named file set: 0 |
| 3 | Verifier's seeds (server rows 9-11 → 5 fail; client rows 7-8 → 2 fail) | **Recorded.** Matches this task's own c1-r2 seeds (§17: S27, C7). No new pin was added in this round, so no new seed |
| 4 | `Repair-SecureFlagNulls.ps1:171` after-check | **Confirmed.** Local pwsh repro of `Get-DvAll`'s `, $rows` shape: fixed `(…).Count` → 0 / 1 / 3 for 0 / 1 / 3 rows; unfixed `@(…).Count` → 1 / 1 / 1. The other caller (line 147, `$nulls = Get-DvAll …` then `@($nulls \| ForEach-Object …)`) enumerates the List and counts 0 / 1 / 3 correctly, so it has no such defect. `Set-SecureFlagFieldSecurity.ps1` p4 (`@((Invoke-DvGet …).value).Count`) does not use `Get-DvAll` |
| 5 | G-0 reconciliation | **Confirmed** against `work/unified-access-control-r2:notes/batch4-live-gates-2026-10-03.md` "150 G-0 … PASS" (42 NULL rows: project 9, matter 18, work assignment 11, invoice 4; `-Apply` all 42 → No; `-Verify` PASS; the "1 row(s) still hold NULL" symptom). §9 G-0 row, merge gate 2 and the c1-r2 POML outcome agree with it |
| 6 | §17 hand-over table vs task 146's `SecureDesignationRemoval.DecideAsync` (`task/uac-r2-146-c1-r1`) | **Confirmed** on all 3 rows (step 2 creator ids → step 3 Full Access on every secure record → step 4 creator-person read → step 5 refusal order). One precision for the integrator, read from the helper's `SecureRemovalDecision.StatusCode`: row 2's non-Full-Access caller with an unreadable creator person gets basis `CreatorUnreadable` → **500** `sdap.unsecure.permission_unverifiable` (the same status this branch gives), so `Unsecure_WhenTheRecordedCreatorPersonCannotBeRead_RefusesBeforeAnyWrite` keeps its 500 for that caller; only the Full Access case is new (admitted). Row 1's `CreatorColumnAbsent` maps to **403**. The three tests exist (`SecureFlagEndpointWriteTests` lines 715, 731, 750). Not forked or copied here |
| 7 | No new surface; POML well-formed; comments match code | **Confirmed.** No endpoint, service, DI registration, option, job, package, column, reason code or script added in c1-r2 or here |
| 8 | Verifier's 5 BFF failures were its own seed (stale `--no-build` binaries) | **Confirmed by re-run** on a fresh build of the restored source: the affected classes 481/481 and the full BFF unit suite (counts below) show none of those 5 failing |
| 9 | Publish size not measured; ribbon not built | **Not closed here, by instruction and by dependency.** Publish size: this round's instruction says skip it; the main session measures against a fresh master build (CLAUDE.md §10). No package changed (CVE status as c1-r1: clean). Ribbon: STOPPED on tasks 142 and 148 (§9) |
| 10 | Merge gate 3: task 133 first or together | **Still open.** Checked: no `task/uac-r2-133*` branch is merged into `work/unified-access-control-r2` (`git branch --merged`: none) |
| 11 | Residuals §11.9, §11.10 | **Recorded**, unchanged |
| 12 | Criterion: suites green + publish size ≤60 MB + no HIGH CVE | Suites green (below); no package change. **Publish size open** — main session |
| 13 | Criterion: secure/unsecure through the endpoint-backed surface (manual live gate) | **STOPPED** on tasks 142 and 148 (ribbon) — unchanged |
| 14 | Criterion: FLS lock live (G-3/G-4) | **Pending live gate** — main session at integration (round 11 approval) |
| 15 | Criterion: G-5, G-6, G-7, G-10, G-1/G-2/G-8/G-9; G-0 outside dev | **Pending live gates** — main session at integration; G-0 PASSED in dev only |

**Tests (c1-r2-r1, on `765749d4d` unchanged).**
- Affected BFF classes (same filter as §17): **481/481**.
- Full BFF unit suite, once: **14425 passed, 0 failed, 54 skipped (14479)** — 29 min, no failure of any kind, so item 8's 5 failures were the verifier's stale seeded binaries.
- `Spaarke.ArchTests`: **346/346**. `Sprk.Bff.Api.IntegrationTests`: **104/104**. `Spe.Integration.Tests`: **403 passed, 25 skipped, 0 failed (428)**.
- Client: jest `CreateProjectWizard` + `SummarizeFilesWizard`, 9 suites, **125/125** (after building the local
  `Spaarke.SdapClient` and `Spaarke.Auth`, which a fresh worktree lacks — without them 4 suites fail to load
  `@spaarke/sdap-client`, an environment gap, not a test failure); `@spaarke/ui-components` `npm run build` (tsc) exit 0.

**Placement (CLAUDE.md §10 / §11).** Nothing new: no code changed in this round. ADR-002: no plugin. ADR-003: no
fail-closed path changed. No `.claude/**` edit needed.

## 19. Fix round c1-r2-r2 (2026-10-03, branch `task/uac-r2-150-c1-r2-r2`) — the c1-r2-r1 verifier's 22 items

Branched from `task/uac-r2-150-c1-r2-r1` (`7777251d3`). No live call; no live write. Two LOW items fixed (10, 11), one
gate row sharpened (13), the rest re-checked against code and recorded. **Round 17** (work branch `82e90ce1f`, BINDING,
committed after this round's instruction was computed) is ALSO done here: item 4 is exactly items 10 and 11, and the
main session answered this round's question in the worktree ("include 17.1-3": round 17 supersedes the instruction's
"do not change anything else" for exactly those three items) — see "Round 17" below.

| # | Verifier item | Disposition |
|---|---|---|
| 1 | Scope of c1-r2-r1 (note + POML only; branch name) | **Confirmed**; nothing to change |
| 2 | Merge into the work branch | **Re-checked against the NEWER tip `82e90ce1f`:** `git merge-tree --write-tree` gives a tree, no conflicts. The work-branch files changed since `6b243f092` are 8 docs/notes plus `tests/integration/auth/README.md`; none is in this task's diff (this round's files are listed in the POML outcome; the README is not among them). Re-run after this round's changes: still clean |
| 3 | Rows 7-11 option B verified | **Re-checked** by grep: client `provisioningService.ts:311/:320`, server `ProvisionProjectEndpoint.cs:1603/:1611/:1652`; `DRAFT` in `ProvisionProjectEndpoint.cs`: 0 |
| 4 | Repair script fix verified offline | **Confirmed**; now also pinned (item 11) |
| 5 | Verifier's seeds SA-SF + client rows 7-8 | **Recorded**; every guard bit. The `--no-build` stale-binary caution applies to this round's seed too, which is why the new guard reads the script AT RUN TIME (no rebuild between seed and run, so no stale binary can mask it) |
| 6 | Verifier's suite counts | **Recorded**; this round's own counts below |
| 7 | F3 hand-over vs 146's helper | **Confirmed**; and owner round 13 item 6 keeps the helper's behaviour. Not forked |
| 8 | No client writer | **Re-checked**: grep `sprk_issecure` over `src/client` outside tests — external-spa type/mocks/selects, `RecordContainerResolver` read, PCF `TrackingFieldTrio` reads, comments; no write |
| 9 | Flag first / already-flagged / 409 (as modified by round 10 item 10) | **Confirmed**; recorded divergence unchanged |
| 10 | §6 line 144 "rows 7-11 … still DRAFT" | **Fixed.** §6 now says rows 7-11 were picked (option B, round 13 item 10), implemented in c1-r2 and pinned; the table is the record of what was offered for rows 1-6 |
| 11 | After-count fix unpinned | **Fixed.** New `SecureFlagFieldSecurityScriptAgreementTests.TheBackfillScript_CountsTheRowsGetDvAllReturns_NotTheSingleListItEmits` reads `Repair-SecureFlagNulls.ps1` as text (the class's existing pattern: no PowerShell host, no network) and fails on any `@(Get-DvAll …)`; it also asserts the premise (`Get-DvAll` ends `, $rows`). Detector theory `TheArrayWrapDetector_SeesTheOneElementCountShape` (4 cases: the unfixed line and a spaced variant detected; the fixed line and the line-147 `$nulls` / `@($nulls \| ForEach-Object …)` shape not). The script comment names the test. Seeds below |
| 12 | Integrator observation: `rootcomponentbehavior` fix vs 150's FLS script | **Confirmed it does not apply.** `Set-SecureFlagFieldSecurity.ps1:169` checks only `componenttype eq 70` (field security profiles), which are ROOT components and never table subcomponents, so a parent table's `rootcomponentbehavior = 0` cannot include them; the script checks no attribute or table membership. The integration step's "150's FLS script" needs no edit; for the main session: tick it as "not applicable (componenttype 70 only)" in `notes/batch4-integration-steps.md` (work-branch file; not edited here) |
| 13 | §11.10 makes G-4's timing security-relevant | **Sharpened.** §9 G-4 row now says: run it straight after G-1 (and G-3) in every environment, no gap, and why; §11.10 says MUST |
| 14 | Publish size not measured | **Not closed** — instruction: skip; main session measures against a fresh master build (CLAUDE.md §10). No package change (CVE status as c1-r1: clean) |
| 15 | Endpoint-backed surface | **STOPPED** on tasks 142 and 148, unchanged |
| 16 | Merge gate 3 (task 133) | **Open**: no `task/uac-r2-133*` branch merged into `work/unified-access-control-r2` |
| 17-22 | Pending live gates G-3/G-4, G-6, G-7/G-8, G-5, G-10; G-0 outside dev | **Pending** — main session at integration (round 11). Dev G-0 PASSED 2026-10-03 |

### 19.1 Round 17 (BINDING, 2026-10-03) — task 150's open questions

| Item | Disposition |
|---|---|
| **17.1** Missing `sprk_createdbyperson` (400) in the provisioning creator rule → unverifiable, aligned with 146's F3 helper | **Done.** `ProvisionProjectEndpoint.RefuseUnlessRecordCreatorAsync`: the 400 now returns `RecordCreatorColumnMissing` — **403** `sdap.provision.record_creator_unverifiable` with `creatorState: column-missing` and `creatorColumn: sprk_createdbyperson`; detail: "Who created this {record} is not recorded in this environment, so whether you may secure it could not be checked. Nothing was changed; an administrator needs to finish setting up the environment." Forward AND unflagged resume (both call this rule). **Reason code:** round 17 names 146's `permission_unverifiable`; the provisioning plane's own "unverifiable" code is `record_creator_unverifiable` (the `sdap.unsecure.*` namespace belongs to the unsecure endpoint), so the existing provisioning code is used at 403 — the status 146's helper gives `CreatorColumnAbsent` — and the client tells it apart from a failed read (500, retryable) by `creatorState`, the extension the resume refusal already uses for the same fact. No new reason code. Client: `record_creator_unverifiable` + `creatorState: column-missing` → `needs-administrator`, not retryable, the same message as the resume's column-missing refusal (`CREATOR_COLUMN_MISSING`, renamed from `RESUME_CREATOR_COLUMN_MISSING`) — one environment fact, one message. Unchanged: `createdby` naming a person still decides without the column (definite "no", or admitted as the caller) |
| **17.2** Resume-neutral copy for row 8 and 133's `creator_unresolved` | **Done.** Row 8: "Who created this project could not be checked, so securing it could not be finished. Nothing about the project changed." `creator_unresolved`: "Securing the project could not be finished, because your account could not be confirmed. Nothing about the project changed." Neither says "did not start" or "not secured"; "could not be finished" is true of a first call and of a resume (row 2 option D's wording). Both pinned verbatim, each also asserting `not.toMatch(/did not start\|not secured/)`. Server rows 9-11 were already resume-neutral ("Nothing was changed") — unchanged |
| **17.3** Empty-flag readers fail closed | **Done for every reader on this branch; one handed over.** One shared helper: `ExternalParticipationService.FlagsFrom(null, …)` → `RootRecordFlags.Unreadable` (+ an error log naming the FLS cause in `GetRootRecordFlagsAsync`), so every consumer of the ONE flag reader inherits it: **`ExternalGrantLifecycle`** (grant policy → 503 `policy_unreadable`), **`InternalShareEndpoints`** (last-reader rule applies), **`AccessibleRecordSetService`** (FR-22 suppression of the standing-grant / derived / org terms). **`SubjectStandingGrantReader`** reads no `sprk_issecure` (checked on this branch and on `task/uac-r2-146-c1-r1`; it reads `sprk_standinggrant`); its contribution is suppressed through the shared reader, which is what the standing-membership test proves. **`ExternalDataService`** only labels the project for the external SPA: an empty flag is shown as **secure** (`?? true`) and logged. **`RecordOwnershipResolver`**: its `sprk_issecure` read exists only on task 146's branch (`task/uac-r2-146-c1-r1`, `ReadParentAsync`), not here — **handed over, not forked** (below) |

**Hand-over to integration — `RecordOwnershipResolver` (round 17 item 3; code on task 146's branch).** In
`Services/Dataverse/RecordOwnershipResolver.cs`, `ReadParentAsync`, replace

```csharp
// NULL sprk_issecure is "No" — owner decision Q1 (2026-10-01) sets every NULL to No and defaults the column
// to No; it is not read as secure. A flagged root that IS isolated takes the secure branch anyway by its BU.
var flaggedSecure = isSecureFlaggedRoot && row.GetAttributeValue<bool?>(IsSecureColumn) == true;
```

with

```csharp
// An EMPTY sprk_issecure fails CLOSED (task 150, round 17 item 3): since the task 150 backfill every row holds true or
// false and the column is field-secured, so empty means the BFF lost its field-level Read and the real value was
// masked. Read as flagged: a root that is not isolated is then refused (never an ordinary team); one that IS isolated
// takes the secure branch by its BU anyway.
var flaggedSecure = isSecureFlaggedRoot && (row.GetAttributeValue<bool?>(IsSecureColumn) ?? true);
```

plus a test beside 146's flagged-not-isolated refusal test: a parent root whose row omits `sprk_issecure`, owned in an
ordinary BU → refused exactly as a flagged-not-isolated parent; the explicit-`false` twin → resolved to the ordinary
team. Seed: revert to `== true` → the new test fails. Recorded for the main session in the same way as the F3 hand-over
(§17); task 146's own fix round may take it instead.

**Tests (c1-r2-r2).**
- `SecureFlagFieldSecurityScriptAgreementTests`: **14/14** (9 + 5 new).
- Affected + new BFF classes (the §17 filter plus `EmptySecureFlag*`, `InternalUserShare*`, `AccessibleRecordSetService*`,
  `PolymorphicGrantWrite*`, `ExternalDataService*`, `ExternalAccess*`, `GrantPolicy*`, `ExternalParticipation*`):
  **1252/1252**.
- New/changed BFF tests: `EmptySecureFlagFailsClosedTests` (new, KEEP `tests/integration/auth/**`; 11 cases: shared
  reader ×4 + control ×2, grant policy ×2, external label ×3); `InternalUserShareTests.Unshare_WhenTheSecureFlagReadsEmpty_AppliesTheLastPersonRule`;
  `AccessibleRecordSetServiceTests.ComposeAsync_ContactStandingMembershipOnARecordWhoseSecureFlagReadsEmpty_IsSuppressed`;
  `SecureFlagEndpointWriteTests`: the missing-column test rewritten to the new rule and widened to the 3 root types,
  + its resume twin, + "a `createdby` person still decides" (55 → 59); `PolymorphicGrantWriteTests.FlagsFrom_MapsTheRootColumns`
  row `(null, null)` → `(false, null)` (its premise changed; the empty case moved to the new class).
- Client: jest `CreateProjectWizard` + `SummarizeFilesWizard`, 9 suites, **127/127** (125 + `creator_unresolved`
  verbatim + `record_creator_unverifiable` column-missing; row 8's verbatim test re-pinned); `@spaarke/ui-components`
  `tsc` exit 0 (after building the local `Spaarke.SdapClient` and `Spaarke.Auth`).
- Full BFF unit suite, once at the end: **14447 passed, 0 failed, 54 skipped (14501** = 14479 + 22 new cases: 5
  agreement, 11 empty-flag, 1 last-reader, 1 standing-membership, 4 provisioning). `Spaarke.ArchTests`: **346/346**.
  `Sprk.Bff.Api.IntegrationTests`: **104/104**. `Spe.Integration.Tests`: **403 passed, 25 skipped, 0 failed (428)**.

**Seeded-violation proofs (c1-r2-r2; each restored from a copy and touched, every seeded build checked for "Build
succeeded" first so no stale binary ran).** S28 script line 172 back to `@(Get-DvAll …).Count` → 1 fail ("found True");
S29 `Get-DvAll`'s `, $rows` → `$rows` → 1 fail (the premise) · SG1 `FlagsFrom` maps empty to not secure again → **8 fail**
(shared reader ×4, grant policy ×2, the standing-membership test, the last-reader test) · SG2 `MapProject` passes an
empty flag through → 1 fail · SG3 the missing column answered `not_record_creator` again → **4 fail** (forward ×3 root
types, resume) · SG4 the `creatorState` extension dropped → 4 fail · client C8 row 8 back to "so it was not secured" →
1 fail · C9 `creator_unresolved` back to "did not start" → 1 fail · C10 column-missing not routed for
`record_creator_unverifiable` → 1 fail.

**Placement and justification (CLAUDE.md §10 / §11).** No new endpoint, service, DI registration, option, job,
package, column, reason code or script. Changes extend existing members: `FlagsFrom`'s mapping, `MapProject` (made
`internal`, with `ProjectRow`, so the mapping is tested without an HTTP stack — no `Mock<HttpMessageHandler>`, ADR-038
B1), one new private ProblemDetails helper (`RecordCreatorColumnMissing`) beside `RecordCreatorUnverifiable`, one
private log helper (`WarnOnEmptySecureFlag`), client copy + one classifier branch (a renamed constant). New test class
`EmptySecureFlagFailsClosedTests` — three-question check: (1) existing: per-reader tests exist for a FAILED flag read
(`Unreadable`), none for an EMPTY one (grep `FlagsFrom(isSecure: null` over `tests/`: 0 before); (2) extension: the
reader-specific cases were added to the existing classes where a double already exists (`InternalUserShareTests`,
`AccessibleRecordSetServiceTests`); the class holds only the pure mapping/policy/label cases; (3) cost of doing nothing:
nothing would fail if a reader went back to "empty = not secure". ADR-002: no plugin. ADR-003: two more fail-closed
paths (the shared reader, the label); none loosened. No `.claude/**` edit needed. Hot-path: BFF = Y (already declared
for this project).

## 20. Batch 4 integration (2026-10-04, `integ/uac-r2-batch4`)

Merged over 143, 137, 156, 142, 146, 149, 157, 133 (c1-r3.-r2), 132 and 148. Task 150 was stacked on an OLDER 133
(`task/uac-r2-133-b2-r2`); the conflicted 133 files were re-merged against 150's real fork point (`574988fef`), so the
newer 133 is kept and only 150's own changes are re-applied on top of it.

- **ONE F3 check (owner round 13 item 6; §17 item 18).** `UnsecureProjectEndpoint.RefuseUnlessPermittedToRemoveAsync`
  now only builds the question for a root and calls task 146's `SecureDesignationRemoval.DecideAsync` (146 note §16c,
  verbatim); `ReasonNotPermitted` / `ReasonPermissionUnverifiable` alias the helper's codes. No second F3 rule remains.
  Tests moved to the helper's behaviour (§17 table): missing creator column + neither creator nor Full Access → 403
  `permission_unverifiable`; Full Access is admitted when the creator person cannot be read and when the column is missing.
- **Round 17 item 3 hand-overs applied.** `RecordOwnershipResolver.ReadParentAsync` reads an empty flag as flagged
  (§19 hand-over, with its test and its explicit-`false` twin); the same fail-closed read in task 149's
  `SecureChildShareSynchronizer.ReadRootFactsAsync` (it mirrored the resolver's "NULL is No"; an empty flag on an
  unisolated root now leaves the child HELD). The ownership test double seeds `false` on root rows by default
  (post-backfill reality) and `WithMaskedSecureFlag` for the empty case.
- **`FlagsFrom`** keeps 137's `stateCode` and 150's empty → `Unreadable`.
- **Owner round 10 item 11 over task 156's premise:** 156's "an invoice with its own flag is a secure root" test now
  pins that a to-do under an invoice flagged secure follows the invoice's MATTER.
- **Guide:** this task's FLS section is §7d (148 took §7c); every 150 reference was renumbered.
- **`Set-SecureFlagFieldSecurity.ps1` (p1)** decides solution membership through `scripts/common/DataverseSolutionMembership.ps1`
  (profiles are root components: no `TableMetadataId`).

## 21. Integration round (2026-10-04, `task/uac-r2-150-integ` from `integ/uac-r2-batch4` @ `f54b5b29b`) — rounds 26, 27, 29

Round 26 item 2(b) (`RecordOwnershipResolver.ReadParentAsync` empty flag fails closed) was already applied at the batch 4
merge (§20). This round closes the rest.

| Item | Disposition |
|---|---|
| **A — round 26 item 2(a)** wire-path test | **Done.** `OrganizationMembershipReadTests` (its in-memory TestServer answering `sprk_projects`) gains `EmptySecureFlags` — a project's `sprk_issecure` served OMITTED (property absent) or NULL, the two shapes a masked field-secured column arrives in. `GetRootRecordFlagsAsync_TheRealReadOfAnEmptySecureFlag_IsUnreadable` (×2 shapes): the REAL `ExternalParticipationService.GetRootRecordFlagsAsync` returns `RootRecordFlags.Unreadable` for it, a `false` row beside it is mapped as stored (control), and the flag was asked for on the wire. `ComposeAsync_ARecordWhoseSecureFlagReadsEmptyOnTheWire_IsSuppressed` (×2 shapes × CIAM / workforce planes): with the flag readable the directly granted project is accessible (control); masked, `AccessibleRecordSetService` drops it and keeps the other. **Seed SA1** — the reader's call site `FlagsFrom(row.sprk_issecure ?? false, …)`: **6 fail** (all six new cases; every `EmptySecureFlagFailsClosedTests` mapping case still passes, which is why this test was owed) |
| **B — round 26 item 2(c)** full effect of the BFF before G-0 | **Done.** Guide §7d step 0 now points at a new paragraph "Shipping the task 150 BFF before step 0 — the full effect": every NULL-flag root (and everything below it) is Unreadable — secure AND Restricted AND inactive — so external participants lose EVERY contact-sourced right on it, grants answer 503 `policy_unreadable`, the last-reader rule applies, uploads answer 503 `secure_flag_unreadable`; the SPA labels it secure; unsecure and the child-ownership pass refuse; provisioning alone writes the flag itself. The guide's masked-window paragraph, §9 G-0 row and §4's masked-window bullet state the same full effect (the BFF identity reads EMPTY during the window too unless it holds System Administrator) |
| **C — round 26 item 2(d) + owner round 27** the ribbon | **Done (code, scripts, tests, ui-test specs); live = G-11.** In task 142's ONE template (`access-group.template.xml`: Make Secure Sequence 20, Remove Secure 30, their commands, enable rules and labels — label text only, no new tooltip copy), ONE generator (`Merge-AccessRibbon.ps1`) and ONE script (`sprk_access_ribbon.js` **1.1.0**); same three-library order, no second group/file/script/MSAL chain. See "The ribbon" below |
| **D — round 29** 143's five provisioning codes | **Done.** `provisioningService.ts`: `creator_no_access` (403, not retryable), `creator_no_access_unverifiable` (500, retryable), `resume_creator_no_access` classified BY STATUS — 409 → `needs-administrator`, not retryable; 500 → `not-started`, retryable; any other status → the generic state, never a retry (`classifyProvisioningFailure` gains an optional `httpStatus`, passed by `provisionSecureProject`). The two `principal_*` codes are per-person WARNINGS on a success, surfaced the way the wizard already surfaces per-person warnings (a `warnings` string list): the response mirror gains `skippedPrincipals`, `describeSkippedPrincipal(code, name)` holds round 29's copy, `provisionSecureProject` returns `warnings` (names from an optional `principalNames` map — the response carries only ids; an unnamed id shows as the id), and `CreateProjectWizard` pushes them into its warnings. Every string pinned verbatim; `EMITTED` **32 → 37** (rows gained an optional status; warning rows are checked through `describeSkippedPrincipal`). **Seed SD1** — `creator_no_access` copy "cannot" → "may not": **1 fail** |
| **E** external-spa `tsc --noEmit` | **Done: 6 → 0.** (1) `mock-data.ts`: the three event mocks named `_sprk_projectid_value`, which `sprk_event` does not have — now `_sprk_regardingproject_value` (the `ODataEvent` contract). (2) `OutsideCounselDashboard.tsx`: it selected `_sprk_projectid_value` on events and showed it AS the project name (a GUID or "Unknown Project"); the events are now kept tagged with their project and `recentActivity` / `upcomingItems` are derived (`useMemo`) with the project NAME from the project read, whichever read finishes last. (3) `EntityCreationService.ts` (`DriveItem` missing): the cause was external-spa's `any`-typed ambient shim `src/types/sdap-client.d.ts`, which shadowed the real package; the shim is deleted and `tsconfig.json` maps `@spaarke/sdap-client` to `../shared/Spaarke.SdapClient/src/index.ts` (type-check only — the module is type-only in the bundle; Vite ignores tsconfig paths). Also pinned in `tsconfig.json`: `react` / `react-dom` types to external-spa's own `@types` — with the shared library's `node_modules` installed (React 19 types) tsc otherwise reported 19 more errors (duplicate `ReactNode` types) that CI (which does not install them) never sees. No `any`, no `ts-ignore` |

### The ribbon (item C)

- **Enable logic (acceptance (e), (f)).** `canMakeSecure` / `canRemoveSecure` = 142's cached can-manage-access verdict
  (Write — the same rule as Update Access) AND the stored flag: Make Secure needs `false`, Remove Secure `true`. The flag
  is read with `Xrm.WebApi.retrieveRecord(<table>, id, "?$select=sprk_issecure")` (cached 30 s per record; forgotten by
  the commands before they refresh). Anything but a stored true/false — a failed read, a throw, an absent or null value
  (masked) — is unknown, and unknown is never equal: BOTH hidden. The flyout keeps 142's rule (both commands need the
  same Write verdict, so "any item available" is Update Access's rule; no flag read for the flyout).
- **Make Secure.** Confirms with `MAKE_SECURE_CONFIRMATION` — owner round 27's copy, ONE frozen constant, `{record}` =
  project / matter / work assignment by table; buttons Make Secure (confirm, primary) · Cancel. Cancel calls nothing.
  Confirmed → `POST /api/v1/external-access/provision-project` `{recordType, recordId}` (144's generalized endpoint;
  148's child pass; round 26 item 3's file relocation is the BFF's — not built here).
- **Remove Secure.** `POST /api/v1/external-access/unsecure-project` `{recordType, recordId}`. No client-side F3: a
  refusal shows the endpoint's ProblemDetails `detail` verbatim (a status-only line when none was sent).
- **Both** go through `Spaarke.BffAuth.authenticatedFetch` (ADR-028: the helper attaches the token; no token = no call
  and a sign-in message), re-read the form (`data.refresh(false)` then `ui.refreshRibbon()`) after any completed call —
  a 500 may follow a partial pass — and show the outcome as Update Access does (global notification on success, alert
  on refusal). Success lines: "This {record} is now secure." / "This {record} is no longer secure."
- **Acceptance (b) — release rule.** Make Secure ships only where task 148's transition is deployed, in the same
  release: `Merge-AccessRibbon.ps1 -SecureTransitionDeployed` keeps it; without the switch the generator removes its five
  nodes (and throws if it does not find exactly five). Tasks 148 + 150 are on this branch, so this release carries both:
  G-11 passes the switch. Documented in the AccessRibbons README, guide §7d.1 and here.
- **`Set-AccessRibbon.ps1`** (new, beside the generator). Dry run (default): merges into 142's checked-in exports
  (project, matter; the work assignment's is not checked in — reported, and `-Apply` checks it in, UX (f)), prints
  before/after commands and the Access menu, fails on a lost command or a wrong menu. `-Apply` (live; main session):
  records the live form command lists (`before.json`), exports the dedicated ribbon solution (never SpaarkeCore),
  checks in the work-assignment export, merges, packs, imports with publish, then verifies. `-Verify` (read-only):
  `RetrieveEntityRibbon` (Form) per table — the before-list intact, Update Access / Remove Secure (and Make Secure exactly
  when the switch is given) present and calling `access_ribbon.js`. Runs: dry run with and without the switch — PASS
  (project + matter); idempotent merge (same hash on a re-run); `-Verify` read-only against spaarkedev1 — **FAIL on all
  three forms (76 / 79 / 74 commands read, no `sprk.Access.*`)**, the correct pre-import verdict. No live write.
- **Tests.** `src/client/shared/Spaarke.UI.Components/src/__tests__/accessRibbon.secureCommands.test.ts` (25): injects the
  REAL `sprk_assignedaccess_postsave.js` and `sprk_access_ribbon.js` into jsdom as classic scripts, replacing only `Xrm`
  and `Spaarke.BffAuth`. Copy verbatim ×3 tables + the frozen constant; enable rules (not secure / secure ×3 tables,
  Read-only ×2, failed read, throwing read, masked absent / null, the flyout); Make Secure (dialog strings, request,
  refresh, notification; Cancel; refusal message; no token); Remove Secure (F3 refusal message verbatim and no confirm;
  success; status-only fallback; state re-read after the command). Seeds: **SC1** an unknown flag read as "not secure"
  → **4 fail**; **SC2** one word of paragraph 2 changed → **5 fail**; **SC3** the ProblemDetails `detail` ignored →
  **2 fail**. Each restored and touched.
- **ui-tests.** The POML `<ui-tests>` now carry the amendment's four (Access group, Make Secure after 148, F3 refusal,
  Read-only user), and the superseded "read-only on the form" check is replaced as the amendment directs.

### Residuals (proposed complete fixes; not decided here)

> **Since decided (round 33) and closed in §22:** R-150-1 (§22.1), R-150-2 (§22.2), R-150-3 accepted as written (round 33
> item 3), R-150-4 is task 140's merge (round 33 item 4: re-run external-spa lint and vitest after 140 merges into the
> integration branch), R-150-5 (§22.3).

- **R-150-1 — Make Secure is creator-only on the server today.** `/provision-project` still applies owner round 10 item
  10 to every UNFLAGGED record: a Write holder who did not create it gets 403 `not_record_creator` (shown verbatim by the
  ribbon). Round 10 item 10 placed "secure an existing record" on task 148's surface — which is this ribbon, calling the
  same endpoint — and R3b keeps securing open to Write holders. As shipped, the ribbon offers Make Secure to every Write
  holder and the server refuses all but the creator; the amendment's ui-test 2 therefore passes only as the creator.
  **Proposed complete fix (main session / owner):** now that 148's transition is in the endpoint, lift the creator rule for
  the ribbon path — the request names the Make Secure transition (e.g. `transition: "make-secure"`) and the endpoint
  holds it to the Write gate (R3b), keeping the creator rule for the wizard's create-then-secure path; or, if the owner
  keeps creator-only, the enable rule adds "the caller created it" from the same server verdict. Either way the
  confirmation copy (round 27) is unchanged.
- **R-150-2 — Remove Secure has no confirmation.** The amendment and acceptance (c) ask for none, and a confirmation
  would be new user-facing copy (owner-authored). Proposed: owner-authored confirmation copy in the next round, wired
  the same way as Make Secure's.
- **R-150-3 — success lines and the status-only fallback** ("This {record} is now secure." / "… no longer secure." /
  "{command} did not complete (status). Reload the record to see its current state.") are implementer copy, adjustable
  in UAT per owner round 27's stance.
- **R-150-4 — external-spa `npm run lint` cannot run**: `eslint` is not a dependency of `src/client/external-spa` and
  there is no ESLint config there (pre-existing; `'eslint' is not recognized`). There is no vitest suite in external-spa
  either. Proposed: add `eslint` + a flat config matching the shared library's, in its own change.
- **R-150-5 — an unknown `skippedPrincipals` reason code** produces no warning (logged): only the two task-143 codes
  exist server-side and both are pinned.

### Tests (this round)

BFF unit (full, twice under concurrent agent load): run 1 15555 passed, 48 failed, 54 skipped (15657); run 2 15565 passed, 38 failed, 54 skipped (15657) - every failure a TaskCanceledException in 25 unrelated contract/seam classes (Office*, Compose*, Insight*, Workspace*, DocumentProfile*, DocumentIdentity*, SearchItems, PlaybookRun, PinnedMemory, Phase1Smoke); those 25 classes isolated: 288 passed, 0 failed - contention, not regressions. OrganizationMembershipReadTests 46/46; EmptySecureFlag* 13/13. Spaarke.ArchTests 600/600. Sprk.Bff.Api.IntegrationTests 104/104. Spe.Integration.Tests 403 passed, 25 skipped, 0 failed (428). Jest (CreateProjectWizard 9 suites + SummarizeFilesDialog.provisioningHost + accessRibbon.secureCommands): 10 suites 179/179 (provisioningService.test.ts 88; ribbon 25). ui-components tsc exit 0. external-spa: tsc --noEmit 0 errors (CI layout and with the shared library's node_modules installed), vite build OK; lint and vitest cannot run (R-150-4). Seeds SA1 6 fail, SC1 4, SC2 5, SC3 2, SD1 1; each restored and touched. Set-AccessRibbon.ps1: dry runs PASS; -Verify read-only on spaarkedev1 FAIL x3 (pre-import, correct).

## 22. Integration round c (2026-10-04, `task/uac-r2-150-integ-c` from `wip/uac-r2-150-integ-restart` @ `244d69f1e`) — round 33 (R-150-1, R-150-2, R-150-5), round 26 item 3 hand-over, the verifier's items

**Start.** The previous lane was stopped by a machine restart; its in-flight work was saved unverified as `244d69f1e`
(WIP on `task/uac-r2-150-integ`, base `2a5f618f7` → `d6e768c51` → the batch 4 merge `f54b5b29b`). Reviewed line by line,
built and run before anything was changed: it built, and the affected classes passed (358/358 —
`SecureFlagEndpointWriteTests`, `Provision*`, `SecureProjectShareTests`, `OrganizationMembershipReadTests`,
`EmptySecureFlag*`). **Kept:** the `Transition` request field and its parsing, Make Secure's Write gate on the forward
path and on an unflagged resume, `ResolveMakeSecureCreatorAsync` (who created the record, read before any write, fail
closed), the creator joining the colleague step (same No Access check, same Collaborate level), the Remove Secure
confirmation constant, the wizard's generic per-person warning, the ribbon's `transition`, and their tests. **Changed or
added this round:** the exact-token rule, Make Secure naming no colleagues, failed shares named instead of only logged,
the ribbon's per-person warnings, the release rule including the file relocation, and one duplicate test removed (the
WIP's "without the transition" twin repeated `Provision_AnUnflaggedRecord_ByAWriteHolderWhoDidNotCreateIt_IsRefusedBeforeAnyWrite`
— that existing test, and its resume twin, ARE the wizard-path half of the pair, as the section header now says).

### 22.1 Round 33 item 1 (R-150-1) — Make Secure stays open to every Write holder

- **The request names the transition.** `ProvisionProjectRequest.Transition`: omitted = the wizards' create-then-secure
  path (owner round 10 item 10's creator rule, unchanged); `"make-secure"` (`ProvisionProjectEndpoint.TransitionMakeSecure`)
  = the form's Make Secure command, held to the route's Write gate (owner R3b) on the forward path AND on an unflagged
  resume. **Matched exactly (ordinal)** — the value relaxes a gate, so `Make-Secure`, a padded or empty value, or another
  spelling is refused 400 before any read or write, and never falls back to either rule.
- **The access afterwards is what owner round 27's copy says.** "Only the person who created this {record} and the people
  it is shared with will keep access": the record is shared to **the person who created it** (`createdby` when a usable
  person, else `sprk_createdbyperson`; read before any write — a read that fails refuses 500
  `record_creator_unverifiable`, a missing `sprk_createdbyperson` column where it is needed refuses 403 with
  `creatorState: column-missing`, a disabled creator is not shared to because nobody can use that clause), and the caller
  is one of "the people it is shared with" — shared to, as on every forward run. **Interpretation recorded:** the caller's
  share is not optional. The forward path's invariant (task 133: share first, move, PROVE a share on the moved record,
  compensate otherwise — never a record nobody can open, S5) needs an identity whose access is proven, and that identity
  is the caller; without it a record whose creator is disabled or walled and that has no sharees would be secured with no
  reader, and the ribbon's own refresh after the call would fail. A reading in which a non-creator caller with no prior
  share loses access would break that invariant, so it is not what is built; the alternative would be contained (Make
  Secure's proof target becomes the record's creator, and a record with no usable creator is refused) if the main session
  ever rules it.
- **Make Secure names no colleagues.** With `sharePrincipalIds` it is refused 400 before any write: a Write holder who did
  not create the record would otherwise widen its explicit access list through the application identity, skipping the
  eligibility and grantor checks Manage Access applies (the reason a resume refuses colleagues from anyone but the creator,
  task 133 verifier round 1). The ribbon sends none.
- **Never silent.** A person the server does not share the record with is NAMED in `skippedPrincipals`: the creator on the
  record's No Access list (`principal_no_access` — No Access wins, owner N6), that list unverifiable
  (`principal_no_access_unverifiable`), or — new — the share itself failed (`sdap.provision.principal_share_failed`; the
  same applies to a wizard colleague, whose failed share was only logged before). The record stays secured and shared to
  the caller, who adds the person through Manage Access.
- **Tests** (`SecureFlagEndpointWriteTests`, KEEP `tests/integration/data-mutation/**`): Make Secure by a non-creator ×3
  root types (secured; creator and caller each hold exactly the creator mask; `additionalPrincipalsShared` 1; nothing
  skipped); by the creator (one share); the refused tokens ×7; colleagues named → 400, nothing written; creator unreadable
  → 500, nothing written; app-created with the column missing → 403 `column-missing`, nothing written; app-created → the
  recorded person shared, the application user never; disabled creator → secured, not shared, nothing skipped; walled
  creator → secured, not shared, named `principal_no_access`; the creator's share fails → secured, named
  `principal_share_failed`; Make Secure resuming an unflagged team-owned row by a non-creator ×3 → resumes, the share goes
  to the creator and never the caller (F8) — **superseded by round 40 item 2 (§23.4): the caller is shared on the resume
  too**. The wizard half: the existing non-creator refusals (forward ×3, resume ×3).
  `SecureProjectShareTests.Provisioning_WhenAColleaguesShareFails_StillSucceeds` now also asserts the failed colleague is
  named.
- **Seeds** (each restored with `git checkout` and touched; each seeded build "Build succeeded"; run over
  `SecureFlagEndpointWriteTests | SecureProjectShareTests | ProvisionNoAccessTests | OrganizationMembershipReadTests |
  RecordOwnershipResolverTests`, 256 tests): **SM1** Make Secure's forward path back on the creator rule → **7 fail**;
  **SM2** the wizards' path (no transition) read as Make Secure → **17 fail** (every forward and resume creator-rule
  refusal, and the colleague tests); **SM3** a lenient token (trimmed, any case) → **3 fail**; **SM4** Make Secure accepts
  colleagues → **1 fail**; **SM5** the creator not shared to → **6 fail**; **SM6** a failed share only logged again → **2
  fail**; **SM7** Make Secure's unflagged resume back on the creator rule → **3 fail**.

### 22.2 Round 33 item 2 (R-150-2) — the Remove Secure confirmation

`REMOVE_SECURE_CONFIRMATION` in `sprk_access_ribbon.js` 1.2.0 — ONE frozen constant: title "Remove the secure designation
from this {record}?"; paragraph 1 "The {record} and its related records return to normal access: people who can see
records in its business unit will be able to see them, and the individual sharing set up while it was secure is
removed."; paragraph 2 "To secure it again later, use Make Secure."; buttons Remove Secure (primary) · Cancel. Built by
the same `confirmationFor` helper as owner round 27's `MAKE_SECURE_CONFIRMATION` (also ONE frozen constant, unchanged).
Remove Secure confirms first; Cancel calls nothing. Pinned verbatim per table (×3) and as the frozen constant; the F3
refusal and success tests confirm first. **Seed SC5** (paragraph 2 reworded) → **5 fail**.

### 22.3 Round 33 item 5 (R-150-5) — an unknown skipped-principal code is never silent

- **Wizard** (`provisioningService.ts`): an unknown code pushes `SKIPPED_PRINCIPAL_GENERIC` — "{name} was not given access
  to this project." — and logs the code. `principal_share_failed` is a KNOWN code: "{name} was not given access to this
  project. You can share it with them later from Manage Access." (round 33 item 5's sentence + round 29's recovery
  sentence, adjustable in UAT). `EMITTED` 37 → **38** (server ↔ client parity re-checked: 38 `Reason*` constants, 38
  rows). **Seeds SD2** (unknown code silent) → **1 fail**; **SD3** (the share-failed copy missing) → **4 fail**.
- **Ribbon** (`sprk_access_ribbon.js`): Make Secure can now return `skippedPrincipals` (the creator), so after the success
  notification the script shows one alert naming every person not shared to — `SKIPPED_PRINCIPAL_COPY` (ONE frozen
  constant: round 29's two sentences and the share-failed sentence, with `{record}` filled per table the way owner round
  27's copy is) or, for an unknown code, `SKIPPED_PRINCIPAL_GENERIC` "{name} was not given access to this {record}." plus a
  log line. Names come from `Xrm.WebApi.retrieveRecord("systemuser", id, "?$select=fullname")`; a name that cannot be read
  shows the id (the wizard's rule); the server's `message` is never shown. **Seeds SC6** (no warnings) → **5 fail**;
  **SC7** (unknown code dropped) → **1 fail**; **SC4** (the ribbon drops the transition) → **1 fail**.

### 22.4 Round 26 item 3 — the file relocation on Make Secure: an INTEGRATION STEP, not a residual of task 150

Round 26 item 3 names ONE relocation service with two callers; task 166 built it on `task/uac-r2-166-f1`
(`Services/Documents/DocumentContainerRelocator.cs`, still under verification): `RelocateDocumentsAsync(documentIds,
targetContainerId, RelocationPurpose.MakeSecure, apply: true)` — its `MakeSecure` purpose and batch entry point exist for
this caller. Task 150 builds no second relocator. **Integration step for the main session when 166 merges:** in
`ProvisionProjectEndpoint`, on the Make Secure path after Step 8 (148's child pass) and in the already-provisioned
re-entry branch: list the record's `sprk_document` rows (the record and its secured children, as the child pass names
them), call `RelocateDocumentsAsync` with the record's own container, and answer 500 `sdap.provision.files_incomplete`
with the counts when the batch is incomplete (the record stays flagged and provisioned; a repeat call completes it);
add the code to `provisioningService.ts`'s `EMITTED` and the ribbon's refusal path, with tests and a seed. (Round 40,
§23: the ribbon already offers `files_incomplete` in place — `MAKE_SECURE_RETRY_IN_PLACE` in access_ribbon.js 1.3.0 — and
the step also needs a SCHEDULED backstop for the files, which 166's job is not: §23.3.)
**Release rule tightened to match** (README, `Set-AccessRibbon.ps1`, `Merge-AccessRibbon.ps1`, guide §7d.1, §9 G-11):
the Make Secure confirmation the user accepts says the files move, so `-SecureTransitionDeployed` is passed only for a
BFF that carries task 148's transition AND that wiring.

### 22.5 The verifier's items (the round's brief, items 1–22)

| # | Disposition |
|---|---|
| 1 | WIP reviewed, kept (above), finished; `d6e768c51` / `2a5f618f7` hold — re-verified by seeds SA1 and RO1 below and by the suites |
| 2 | **Closed** — §22.1 |
| 3 | **Closed** — §22.2 (and owner round 27's Make Secure copy remains ONE constant, pinned verbatim ×3 tables) |
| 4 | **Closed** — §22.3 |
| 5 | **Recorded as an integration step** — §22.4 |
| 6 | Holds for this round: the POML parses (`xml.dom.minidom`), a new outcome block dated 2026-10-04, status `completed-with-escalation`. `git merge-tree` of this branch with `integ/uac-r2-batch4` (`3aa4ebce6`) and with `work/unified-access-control-r2` (`5e958f856`): both clean. No live write in the diff or the note |
| 7 | **Closed (holds)** — `OrganizationMembershipReadTests` drives the REAL `GetRootRecordFlagsAsync` with `sprk_issecure` omitted and null. **Seed SA1** (`FlagsFrom(row.sprk_issecure ?? false, …)`) → **6 fail** |
| 8 | **Closed by round 26 item 1** — `403 sdap.provision.record_creator_unverifiable` + `creatorState: column-missing` + `creatorColumn` is the contract; Make Secure uses the same code for the same fact |
| 9 | **Closed (holds)** — guide §7d step 0, "Shipping the task 150 BFF before step 0 — the full effect", the masked-window paragraph and §9 G-0 state the full effect; the guide's provisioning sentence now names Make Secure's Write gate too |
| 10–12, 14, 16 | Verified earlier; re-run green in this round's suites |
| 13, 19 | **Closed (holds)** — `RecordOwnershipResolver.ReadParentAsync` reads an empty flag as flagged (`?? true`, batch 4 merge §20) with its twin; **seed RO1** (`?? false`) → **1 fail** |
| 15 | Superseded by this round's runs (§22.7) |
| 17 | **Closed** — publish size measured against a fresh `origin/master` build (§22.6); no vulnerable package |
| 18 | **Code closed** (Make Secure and Remove Secure in 142's Access group, §21 C, this round); the live run is G-11 (manual gate) |
| 20 | **Closed (holds)** — item 7 |
| 21 | **Met on the integration tree** — `task/uac-r2-133-c1-r3.-r2` is an ancestor of this branch (`git merge-base --is-ancestor`); 133 and 150 land together when `integ/uac-r2-batch4` merges. It is not yet in `work/unified-access-control-r2`, so 150 must not reach `work/*` except through the integration branch |
| 22 | Live gates G-0…G-11 — manual gates with the exact commands in §9 (G-0 passed in dev; required in every other environment) |

### 22.6 Publish size and CVE check (CLAUDE.md §10, NFR-01)

Measured 2026-10-04, each tree extracted fresh with `git archive` into a short path (`C:\w150m`, `C:\w150p`, `C:\w150b`;
removed afterwards), `dotnet publish -c Release -o <root>\deploy\api-publish` in `src/server/api/Sprk.Bff.Api`
(framework-dependent linux-x64, from the csproj), `Compress-Archive -CompressionLevel Optimal` over `api-publish\*`,
**PDBs included** (4). No MSB3030 on any side.

| Tree | Commit | Files | Zip |
|---|---|---|---|
| `origin/master` | `8904abeb4` | 212 | **45.65 MB** (47,871,327 B) |
| integration tree before task 150's merge | `b8a1374c2` (`f54b5b29b`^1) | 212 | 46.04 MB (48,279,601 B) |
| this branch (code-final) | `2ae23559f` | 212 | **46.05 MB** (48,285,695 B) |

Task 150 (its batch 4 merge plus both integration rounds) adds **+0.006 MB** (+6,094 B) to the integration tree; the
whole integration tree is **+0.40 MB** over master (batch 4's tasks together). Far under the 60 MB ceiling and the +5 MB
escalation. `dotnet list package --vulnerable --include-transitive` on the branch tree and on master: "no vulnerable
packages" on both; this round adds no package.

### 22.7 Placement and justification (CLAUDE.md §10 / §11)

No new endpoint, service, DI registration, option, job, package or column. Extended: `ProvisionProjectRequest`
(`Transition`, from the WIP) and the existing endpoint. New contract items, each with the three questions:

| New item | Existing | Extension | Cost of doing nothing |
|---|---|---|---|
| `Transition` (`"make-secure"`) on the provision request | the request's `RecordType`/`RecordId`; nothing told the endpoint which surface asks (grep `Transition` over `Api/ExternalAccess/Dtos/` and `ProvisionProjectEndpoint.cs` at `2a5f618f7`: 0) | one optional field on the existing DTO, one branch in the existing handler | Make Secure is creator-only (R-150-1): every other Write holder gets `not_record_creator` from a command the ribbon shows them |
| Reason code `sdap.provision.principal_share_failed` | `skippedPrincipals`' two task-143 codes | a third code in the same list, the same `ProvisionSkippedPrincipal` record | a failed share — now including the record's creator on Make Secure — is only logged, so the caller is told the record is secure while the creator silently lost access |
| Ribbon `SKIPPED_PRINCIPAL_COPY` / `SKIPPED_PRINCIPAL_GENERIC` / `describeSkippedPrincipal` | the wizard's `describeSkippedPrincipal` (a module the classic ribbon script cannot import) | the same three sentences and the same rules, in the ONE ribbon script beside its other constants | Make Secure's `skippedPrincipals` are ignored by the ribbon — silent (round 33 item 5) |

ADR-003: two more refusals before any write (an unknown token; colleagues on Make Secure); none loosened beyond the
binding round 33 item 1. ADR-002: no plugin. ADR-006: the ribbon stays a thin command script. ADR-028: the token remains
`Spaarke.BffAuth.authenticatedFetch`'s. ADR-038: no `Mock<HttpMessageHandler>`, no DI or ctor tests. No `.claude/**` edit
needed. Hot-path: BFF = Y (already declared).

### 22.8 New finding — R-150-6 (a STOP: it changes a closed acceptance criterion; complete fix proposed)

> **Since decided (round 40 item 1) and closed in §23.1–§23.2.** One premise below did not hold: a failure before the
> owner move on a record that records a container (its own — re-securing after Remove Secure keeps it — or a shared one
> whose unlink failed) leaves the record flagged WITH a container and its pre-call owner, not "no container"; §23.1 covers
> it from the same read.

**What.** A Make Secure that fails AFTER its first write (the flag) leaves a record that reads `sprk_issecure = true`, so
the ribbon hides Make Secure (acceptance (e): "on a secure record Make Secure is hidden") — but the server's answer for
those states says "the same caller may retry" / "calling again completes it": `container_creation_failed`,
`container_not_recorded`, `owner_assignment_unverified`, `creator_share_failed_resumable` (owned by the team, no
container), and — once §22.4 is wired — `files_incomplete` (round 26 item 3 relies on "a repeat call completes it").
`children_incomplete` is covered by task 148's `SecureChildReconciliationJob` (round 28 E2: writes on, every 2 minutes);
the others have no job. From the product the only recovery is an administrator's direct API call (guide §7, "Calling
provisioning as an administrator"); uploads to the record are refused meanwhile (fail closed — nothing is exposed).

**Proposed complete fix** (needs the main session: it amends acceptance (e) and adds a dialog variant):
1. The Make Secure enable rule also shows the command when the record reads secure but its transition is unfinished —
   `sprk_issecure = true` and `sprk_containerid` empty, read in the ONE `retrieveRecord` the rule already makes (every
   failure between the flag write and Step 7 leaves exactly that shape). The confirmation (owner round 27) and the call
   are unchanged; the server already resumes such a record (and runs a flagged, non-isolated one from the start).
   Acceptance (e) becomes "on a PROVISIONED secure record Make Secure is hidden".
2. For a failure the server answers as retryable AFTER Step 7 (`files_incomplete`), the failure alert becomes a confirm
   dialog with the server's `detail` and the existing labels ("Make Secure" · "Cancel"), so the same call is repeated in
   place; with item 1 that covers every state the server tells the caller to retry.
3. Tests: the enable rule on the unfinished shape (×3 tables), the dialog repeat, seeds for both.

### 22.9 Tests (this round)

Affected classes first (`SecureFlagEndpointWriteTests` 82, the seed filter 256/256 before and after every seed), then
ONCE at the end, on the code-final commit `2ae23559f`, under other agents' concurrent load: **full BFF unit suite 15624
passed, 0 failed, 54 skipped (15678 = the previous round's 15657 + 21)**; **Spaarke.ArchTests 600/600**;
**Sprk.Bff.Api.IntegrationTests 104/104**; **Spe.Integration.Tests 403 passed, 25 skipped, 0 failed (428)**. Client
(`@spaarke/ui-components`, after `npm install --legacy-peer-deps --no-audit --no-fund` and building the two `file:`
dependencies `@spaarke/sdap-client` and `@spaarke/auth`): jest — CreateProjectWizard (8 suites), SummarizeFilesDialog
provisioning host, `accessRibbon.secureCommands` — **10 suites, 193/193** (`provisioningService.test.ts` 90, ribbon 37);
`tsc --noEmit` exit 0; `npm run build` exit 0; eslint on the changed files 0 errors (1 pre-existing warning: an unused
`eslint-disable` in the ribbon test); `node --check sprk_access_ribbon.js` OK. Seeds: §22.1–§22.3 and §22.5.

## 23. Integration round c-v1 (2026-10-05, `task/uac-r2-150-integ-c-v1` from `task/uac-r2-150-integ-c` @ `0f993b651`) — round 40, the verifier's 16 items

**Binding input:** round 40 (main session under round 15, `work/unified-access-control-r2` @ `83460442d`): item 1 R-150-6
(option 1, completed), item 2 (the non-creator keeps access on BOTH paths), item 3 (`principal_share_failed` copy and the
"Someone" fallback). No `NOTE-FROM-MAIN.md` existed. No live call of any kind was made.

### 23.1 Round 40 item 1 (R-150-6) — a Make Secure that fails after the flag write can always be finished

- **The enable rule** (`sprk_access_ribbon.js` **1.3.0**, `canMakeSecure`). Make Secure is offered to a caller with Write
  on a record that is NOT secure, **or** that is flagged secure but whose transition did not finish. "Finished" is what
  provisioning leaves: owned by the Secure Record owner TEAM, with the record's own container recorded. So a flagged record
  with **no container recorded**, or one a **user** owns, is unfinished. The ONE `retrieveRecord` the rules already made
  now selects `sprk_issecure,sprk_containerid,_owninguser_value` (no second read). A flag read that fails, or comes back
  empty, still hides both commands (acceptance (f)). **Acceptance (e) as amended:** Make Secure is hidden on a
  PROVISIONED secure record. Remove Secure is unchanged (flag true + Write).
- **A premise of §22.8 that did not hold, and why `_owninguser_value` is in the read.** §22.8 said every failure between
  the flag write and Step 7 leaves "flagged, no container". That is false for a record that ALREADY records a container:
  - its own container. That is the common re-secure case: the unsecure endpoint moves the owner to a USER and keeps
    `sprk_containerid` (`UnsecureProjectEndpoint.cs`, Step 3; it never writes the column);
  - a shared container whose Step 4.2 unlink failed.

  A failure at Steps 4.1–5.5 with the owner unchanged or restored leaves such a record flagged, WITH a container, and
  owned by its pre-call owner. "Flagged + no container" alone would hide Make Secure there.
  A provisioned record is never user-owned. So "user-owned" catches every such shape a Spaarke writer produces: the
  wizards create user-owned roots, and unsecure assigns to a user. Office/BFF creates are owned by the business unit's
  default team (task 080), and they never carry a container before provisioning: `RecordCreationService` refuses
  `sprk_containerid` in mappings, and provisioning is the only writer of the column on a root (task 076). The one shape the
  client cannot classify is a root reassigned OUTSIDE Spaarke to a non-default team while it records a container. Telling
  that team from the named owner team needs the server's configuration (`SecureRecord:OwnerTeamName`). The only GET the
  ribbon calls, `can-manage-access`, is by design a pure delegation answer (task 139). That shape is closed by the
  in-place retry below and, after it, by an F3 holder's Remove Secure → Make Secure or an administrator's call (§23.2).
  **Superseded by round 46 item 4 (§24.4):** accepting that shape as a documented edge was rejected; `can-manage-access`
  now answers, on request, the owning team and whether it is the Secure Record Owners team, and the ribbon offers Make
  Secure on it (1.4.0).
- **The in-place retry.** Round 40 named the after-Step-7 codes (`files_incomplete`, `children_incomplete`).
  `MAKE_SECURE_RETRY_IN_PLACE` (ONE frozen constant) holds every Make Secure failure after the flag write that the server
  answers as "the same caller may call again": `secure_flag_not_set`, `shared_container_not_cleared`,
  `creator_share_failed`, `owner_assignment_unverified`, `container_creation_failed`, `container_not_recorded`,
  `children_incomplete`, `files_incomplete`. For each, the alert becomes `openConfirmDialog` with the server's `detail`,
  confirm "Make Secure", cancel "Cancel". Make Secure repeats the same call (transition included, no second owner-copy
  dialog); Cancel ends it. The six before Step 7 are added because they are the same server answer, and the in-place
  offer is what closes the custom-team shape above.
  Refusals that need an administrator first (`creator_share_failed_resumable`, `cascade_children_not_restored`,
  `owner_assignment_failed`, `owner_assignment_not_applied`) stay an alert naming that step, because repeating them
  repeats the refusal. Remove Secure never offers it. `files_incomplete` is in the list now because round 40 names it,
  and the ribbon then needs no second release when §22.4 wires the relocation.
- **Server:** nothing new was needed for the retry itself. The endpoint already resumes a flagged team-owned record with
  no container and re-runs a flagged record not owned by the team; §23.4 makes the Make Secure resume finishable for
  every caller and creator shape.
- **Tests** (`accessRibbon.secureCommands.test.ts`, 37 → 67): unfinished, no container ×3 tables (Make Secure AND Remove
  Secure, one read); empty / whitespace / absent container ×3; user-owned with its own container; provisioned hidden
  (×3 tables + 1); masked flag on that shape; Read-only on an unfinished record; the frozen list; each of the 8 codes
  (dialog strings, second POST body, refresh ×2, notification); Cancel; 5 non-retry codes (alert only); Remove Secure
  never retries. **Seeds** (each restored from a backup and touched; hashes re-checked):
  - CR1, Make Secure only when not secure: 7 fail.
  - CR2, user-owned ignored: 1 fail.
  - CR3, no retry offered: 9 fail.
  - CR4, a retry for every failure: 6 fail.
  - CR7, the container ignored (every flagged record unfinished): 4 fail.
  - CR8, `files_incomplete` dropped: 2 fail.

  (CR4's first run looped to a jest out-of-memory, because three non-retry tests confirmed every dialog. Those now
  confirm once, so the seed fails cleanly.)
- **ui-test** (POML): "Make Secure finishes an unfinished transition (round 40)" — part of G-11.

### 23.2 Every post-flag failure code, and what closes it (round 40 item 1's table)

R-150-6 named five codes (`container_creation_failed`, `container_not_recorded`, `owner_assignment_unverified`,
`creator_share_failed_resumable`, `files_incomplete`). The table covers EVERY code a Make Secure call can answer at or
after its first write (Step 4.1), read from `ProvisionProjectEndpoint.cs`.

Key:
- **"Re-offer"** — the enable rule offers Make Secure again on the state the code leaves: a flagged record with no
  container, or owned by a user.
- **"In place"** — `MAKE_SECURE_RETRY_IN_PLACE`.
- **"Sweep"** — 148's `SecureChildReconciliationJob`. Round 28 E2: every 2 minutes, enabled, recent-changes pass writes
  on wherever 148 is deployed. That is `task/uac-r2-147-r1c` (`DefaultCronSchedule = "*/2 * * * *"`, registered
  enabled). On this branch and on `integ/uac-r2-batch4` the job is still registered disabled at `*/15`, and task 147
  changes it.

| Code (step) | State it leaves | Closed by |
|---|---|---|
| `secure_flag_not_set` (4.1) | flag maybe set; owner and container unchanged | **Ribbon**: in place; then re-offer (flag false → Make Secure; flag set → unfinished shape). A refused write or an unreadable read-back is an FLS fault: the administrator runs `Set-SecureFlagFieldSecurity.ps1 -Verify` (the detail says so), then the ribbon |
| `shared_container_not_cleared` (4.2) | flagged; pre-call owner; shared container maybe still recorded | **Ribbon**: in place; re-offer (user-owned, or the unlink landed) |
| `creator_share_failed` (4.5, ownership restored) | flagged; pre-call owner; container none or kept | **Ribbon**: in place; re-offer |
| `owner_assignment_failed` / `owner_assignment_not_applied` (5) | flagged; pre-call owner | **Administrator first**: the owner team's role (guide §5, named in the detail). Then the **ribbon** re-offers |
| `owner_assignment_unverified` (5) | owner unknown; caller's share issued | **Ribbon**: in place; re-offer when the move did not land or no container is recorded. If the move landed on a kept-container record, the record reads provisioned (owner round 10 item 5): its children are closed by the **sweep**, its files by §23.3 |
| `creator_share_failed` (5.5, compensated) | flagged; pre-call owner restored | **Ribbon**: in place; re-offer |
| `cascade_children_not_restored` (5.5) | flagged; pre-call owner; named children on the wrong owner | **Administrator first**: the named calls, by design (task 133 c1, "not a self-service retry"). Then the **ribbon** re-offers |
| `creator_share_failed_resumable` (5 / 5.5 double failure) | may be team-owned with nobody shared — the caller may not even open it | **Administrator** (holds Write by role). No container: the administrator's Make Secure on the ribbon (re-offered) or the API call (§7). Kept container: a Manage Access share (owner round 10 item 5), after which the **sweep** closes the children |
| `container_creation_failed` (6) | team-owned, flagged, no container | **Ribbon**: in place; re-offer |
| `container_not_recorded` (7) | team-owned, flagged, no container (an empty container named) | **Ribbon**: in place; re-offer |
| `children_incomplete` (8) | provisioned | **Both**: in place; **sweep** |
| `files_incomplete` (relocation, once §22.4 is wired) | provisioned | **Ribbon** in place; the scheduled backstop is §23.3, part of §22.4 |
| `creator_share_failed` (resume, `resumed: true`) | team-owned, flagged, no container | **Ribbon**: in place; re-offer |
| `creator_unresolved` / `creator_no_access(_unverifiable)` / `record_creator_unverifiable` (Make Secure resume, §23.4, before any write) | unchanged | **Ribbon**: re-offer (the shape is unfinished); transient ones are retried there |

Every code is closed by the ribbon's retry, by a scheduled job, or by both. The administrator-first rows are task 133's
owner-decided designs, and after the administrator's step the ribbon closes them. One gap remains, and it is not on this
branch: `files_incomplete` has no SCHEDULED backstop (§23.3).

### 23.3 STOP for the §22.4 integration step — `files_incomplete`'s job backstop is not scheduled (round 40's premise); complete fix

**Decided by round 46 item 2 (§24.2):** the backstop is 147's `SecureChildReconciliationJob`, as proposed below; the
release gate requires it registered with its writes on; wired at integration once 147, 150 and 166 are all on the
integration branch.

- **What.** Round 40 names "for files, 166's relocator re-entry" as the scheduled job. On `task/uac-r2-166-f1-v1`
  (`ca8291442`), `DocumentContainerMigrationJob` is registered DISABLED: "Never fires on its own", and it runs only when
  `scripts/Invoke-DocumentContainerMigration.ps1` triggers it, with writes switched on for that run. So after a
  `files_incomplete` whose in-place retry is cancelled, nothing scheduled finishes the files. The record reads
  PROVISIONED, so the ribbon does not re-offer Make Secure. Nothing is exposed: un-moved files are refused under the
  strict rule. But the transition stays unfinished until an operator acts. That is "neither" under round 40, so it is a
  defect to fix.
- **Why it is not fixed here.** `DocumentContainerRelocator` is not on this branch or on `integ/uac-r2-batch4`:
  `git merge-base --is-ancestor task/uac-r2-166-f1-v1 integ/uac-r2-batch4` is false. Round 26 item 3 forbids a second
  relocator, and `files_incomplete` is not emitted until §22.4 wires the relocation.
- **Complete fix (add to the §22.4 integration step).** The secure-child reconciliation sweep is already scheduled every
  2 minutes (round 28 E2, task 147), already enumerates every secure root, and already reports `incompleteRoots`. For
  each ISOLATED root it also settles the root's Make Secure relocation through the ONE `DocumentContainerRelocator`: its
  re-entry over the root's documents, purpose `MakeSecure`. Each root still owing a step is reported among
  `incompleteRoots`, as children are. That gives one relocator with a third caller and no new job (CLAUDE.md §11). Tests:
  a root left `files_incomplete` is finished by the next run, plus a seed that disables the call. The release rule
  becomes: Make Secure is imported (`-SecureTransitionDeployed`) only for a BFF carrying 148, the wired relocation AND
  this backstop. The AccessRibbons README, `Set-AccessRibbon.ps1` and guide §7d.1 already gate on the wired relocation;
  "and its sweep backstop" is added with the wiring.

### 23.4 Round 40 item 2 — whoever runs Make Secure keeps access, on BOTH paths

- **`ResumeMakeSecureAsync`.** A resume through Make Secure (`transition: "make-secure"`, flagged or not) follows the
  forward Make Secure rules, minus the move. Every refusal comes before any write:
  1. the caller by WhoAmI (403 `creator_unresolved`);
  2. the caller against the No Access list (403 `creator_no_access` / 500 `creator_no_access_unverifiable`);
  3. the record's creator (`ResolveMakeSecureCreatorAsync`: 500 `record_creator_unverifiable`, or 403 with
     `creatorState: column-missing`);
  4. the flag (skipped when set);
  5. the caller's share, read back (500 `creator_share_failed`, `resumed: true`).

  The creator joins the colleague step: shared, named in `skippedPrincipals`, or not shared when unusable. The response's
  `sharedToCreatorSystemUserId` is the caller, as on the forward path.
- **Interpretation recorded.** "Does the same" means the forward Make Secure rules in full, not only the caller's share.
  The resume's own rules (F8) would answer a retry with 409 for exactly the records the forward path secured anyway:
  - a creator on the No Access list (`resume_creator_no_access`);
  - a disabled or absent creator (`resume_creator_unavailable`).

  Those records would again be unfinishable from the command that started them, which is R-150-6. The caller's proven
  share keeps S5: the record always has a reader.
- **F8 holds without the transition.** A wizard or API resume shares to the creator only (now pinned on the flagged
  no-transition resume: the caller holds no grant). An administrator who uses the ribbon's Make Secure is shared like any
  caller. Guide §7a and §7 say so.
- **Never narrowed (a defect in round 33's forward path, fixed with item 2).** `EnsureCreatorShareAsync` set the proven
  share to EXACTLY the creator's level. That is correct for the wizards' new record, but it would MODIFY a Make Secure
  caller's existing Full Access share down to Collaborate. That drops Delete, and with it the caller's F3 right to remove
  the designation. "Keeps access" (round 40 item 2) and A4's "never lower existing access" both forbid that.
  `MakeSecureCallerMask(held) = (held & FullAccess) | Collaborate`, with `held` = the pre-call share ∪ the current one:
  - never lower than a level held;
  - never beyond Full Access, so no Assign (a secure record leaves the business unit only through unsecure);
  - applied at share-first, the Unverified ensure, Step 5.5 and the resume;
  - the wizards' path is unchanged (exact).

  **Corrected in c-v2 (round 46 item 1, §24.1):** as written here this held only for Full Access held BY A SHARE. A
  caller who held Delete through ownership or a role — what F3 decides from — ended at Collaborate. `held` now also
  carries the caller's EFFECTIVE rights before the call, and the Unverified fallback grant is issued at the floor too.
- **Shared helpers.** `CallerUnresolved` and `CallerWallRefusal` were extracted from the forward path, text unchanged,
  and both paths now use them.
- **Tests** (`SecureFlagEndpointWriteTests` 82 → 96):
  - the renamed unflagged Make Secure resume ×3 (caller AND creator at the creator's level, 1 additional);
  - flagged finish ×3 (`resumed`, `sharedToCreatorSystemUserId` = caller, no flag write);
  - walled creator finishes and is named;
  - disabled creator finishes for the caller;
  - caller walled → 403, nothing written;
  - caller unidentified → 403, nothing written;
  - creator unreadable → 500, nothing written;
  - caller share fails → 500, no container;
  - Full Access kept, forward and resume;
  - the level floor ×3 (View → Collaborate; Collaborate+Assign → Collaborate; Full+Assign → Full);
  - the F8 pin on `Provision_ResumingAFlaggedRecord_ByAWriteHolderWhoDidNotCreateIt_StaysOnTheWriteGate`.

  **Seeds** (each build "Build succeeded"; restored from a backup and touched; file hash `5c3ee0ce…` re-checked), over
  the 768-test filter:
  - SR1, the Make Secure resume back on F8 rules: 10 fail.
  - SR2, the caller's No Access check skipped: 1 fail.
  - SR3, narrowed to exact: 3 fail.
  - SR4, the creator not shared on the resume: 8 fail.
  - SR5, the caller's share not ensured: 8 fail.
  - SR6, F8 broken (no-transition resumes routed through Make Secure): 46 fail.
  - SR7, the creator read skipped: 1 fail.
  - SR8, WhoAmI's refusal skipped: 1 fail.

### 23.5 Round 40 item 3 — `principal_share_failed` copy, `{record}`, and "Someone"

- The copy was already as accepted, and each string is pinned verbatim.
  - Wizard: "{name} was not given access to this project. You can share it with them later from Manage Access."
  - Ribbon: the same sentence with `{record}`, and round 29's two sentences generalized to `{record}`
    (`SKIPPED_PRINCIPAL_COPY`, pinned in the frozen-constant test and per table).
  - The wizard keeps "project": it secures projects only.
- **"Someone"** (`UNNAMED_PERSON`, ONE constant on each side).
  - Ribbon: `personName("")` makes no read and answers "Someone". A name that cannot be read, or reads back empty or
    blank, is "Someone" (it used to show the id). `describeSkippedPrincipal` never fills a blank name.
  - Wizard: an id the host gave no name or a blank name for, and an empty id, are "Someone" (they used to show the raw
    id). `describeSkippedPrincipal` guards a blank too. **Corrected in c-v2 (§24.6):** the generic warning's own blank
    guard was redundant (it only ever receives a name `skippedPersonName` resolved) and untested; it was removed, and the
    generic path's "Someone" is pinned through `skippedPersonName`.
  - Item 4 of the brief, the empty-name warning, is this case.
- **Tests:** ribbon +5 (empty id / blank / null name; the constant; blank in describe); wizard +4. **Seeds:**
  - CR5, unreadable shows the id: 1 fail.
  - CR6, an empty id is read: 1 fail.
  - CW1, the wizard shows the id: 2 fail.
  - CW2, a blank name passes through: 1 fail.

### 23.6 The brief's 16 items

| # | Disposition |
|---|---|
| 1 | **Closed** — round 40 item 1, §23.1/§23.2 (one integration-step stop for files: §23.3) |
| 2 | **Closed** — round 40 item 2, §23.4 (and the narrowing defect it exposed) |
| 3 | **Closed** — `ShareToColleaguesAsync`'s summary and the call-site comment now name the share each path proves: the caller's on the forward path and on a Make Secure resume, the record creator's on a resume without the transition |
| 4 | **Closed** — round 40 item 3, §23.5 |
| 5–9 | Verified by the verifier; re-held here. The Make Secure resume no longer uses the creator rule, so the verifier's S8 is superseded by SR1/SR4/SR5. The wizards' half is still pinned by the existing non-creator refusals. SA1 / RO1 untouched, and the classes pass (768/768) |
| 10 | Re-checked on this branch: POML parses (`xml.dom.minidom`); no `NOTE-FROM-MAIN.md`; no live write. `git merge-tree --write-tree` against `integ/uac-r2-batch4` @ `a9c703276` and `work/unified-access-control-r2` @ `83460442d`: both clean |
| 11 | Superseded by this round's runs (§23.8) |
| 12 | **Code closed** (§23.1); the live run is G-11 (manual gate, with the new ui-test) |
| 13 | **Integration step, made complete:** the relocation wiring (§22.4) plus its scheduled backstop (§23.3, a STOP for the main session: round 40's premise did not hold). It cannot be built here: the relocator is not on this branch or on `integ` (166 not merged), and a second relocator is forbidden (round 26 item 3). The release gate (`-SecureTransitionDeployed`) holds until both land |
| 14 | §23.8: the full BFF unit suite, once, with the result as run |
| 15 | Live gates G-0…G-11 — manual, main session (§9; G-11 now names `access_ribbon.js` 1.3.0 and round 40's ui-test). G-0 is required in every environment other than dev |
| 16 | **Pre-verified, and a merge conflict found and resolved:** §23.7 |

### 23.7 Item 16 — external-spa lint and vitest against task 140 (pre-verified; a conflict, resolved)

- Task 140 is not merged into the integration branch: `git merge-base --is-ancestor task/uac-r2-140-x1-v1c
  integ/uac-r2-batch4` is false. Its newest branch is `task/uac-r2-140-x1-v1c` @ `2c2bcb232`.
- In a throwaway detached worktree (`C:\wv150e` at `0f993b651`), `git merge --no-commit --no-ff
  task/uac-r2-140-x1-v1c` raised **two conflicts with task 150's external-spa work**:
  - `src/pages/OutsideCounselDashboard.tsx`: both tasks fixed the same "Unknown Project" defect, 150 by keeping events
    tagged and deriving the items with names, 140 by a render-time `projectNameById`.
  - `src/types/sdap-client.d.ts`: a modify/delete. 150 deleted the `any`-typed shim and mapped `tsconfig` paths to the
    real package; 140 added `export type DriveItem = any` to the shim for the same `tsc` error.
- **Resolution** (an integration step for whichever of 140 / 150-integ-c merges second):
  - take 150's `OutsideCounselDashboard.tsx` whole. Every 140 edit to that file is the same fix: `projectNameById` /
    `withProjectName`, `projectId` on `UpcomingItem`, the `$select`.
  - keep the shim deleted. 150's path mapping supplies the real `DriveItem`.
- **On the resolved tree** (`npm install --legacy-peer-deps --no-audit --no-fund` in `src/client/external-spa`):
  - `tsc --noEmit -p tsconfig.json` exit 0;
  - `npm run lint` (140's `eslint.config.js`) exit 0, no problems;
  - `npx vitest run` 2 files, **13/13**, including 140's `OutsideCounselDashboard.projectName.test.tsx` against 150's
    implementation;
  - **seed**: 150's name lookup replaced by `'Unknown Project'` makes 140's test fail 1/1 (restored).
- The merge was abandoned with nothing committed, and the worktree was removed and pruned.
- So round 33 item 4's re-run is done against the merged tree. The definitive run is the same three commands on the
  integration merge, with this resolution.

### 23.8 Tests, publish size, CVE (this round)

**Affected classes first.** The filter is `SecureFlag*`, `SecureProjectShareTests`, `ProvisionNoAccessTests`,
`ProvisionProject*`, `ProvisionResume*`, `ProvisionRecordedContainer*`, `UnsecureProject*`, `RecordContainerResolver*`,
`OrganizationMembershipReadTests`, `RecordOwnershipResolverTests`, `EmptySecureFlag*`, `SecureNamedOwnerTeam*` and
`SecureChild*`.
- It passed **768/768** before and after every seed, and again after the pre-commit hook's `dotnet format` / prettier pass.
- `SecureFlagEndpointWriteTests`: **96** (82 + 14).

**Once at the end** (code-final commit `c0acfd7b2`; this box, other agents active):
- **Full BFF unit suite: 15638 passed, 0 failed, 54 skipped (15692 = the previous round's 15678 + 14)** — ONE clean full
  run, which closes the brief's item 14;
- **Spaarke.ArchTests 600/600**;
- **Sprk.Bff.Api.IntegrationTests 104/104**;
- **Spe.Integration.Tests 403 passed, 25 skipped, 0 failed (428)**.

**Client** (`@spaarke/ui-components`, after `npm install --legacy-peer-deps --no-audit --no-fund` and building the two
`file:` dependencies):
- jest **10 suites 227/227**: CreateProjectWizard (8 suites), `SummarizeFilesDialog.provisioningHost`,
  `accessRibbon.secureCommands`. That includes `provisioningService.test.ts` **94** (90 + 4) and the ribbon suite **67**
  (37 + 30), and both `provisioningHost` suites;
- `tsc --noEmit` 0; `npm run build` 0;
- eslint on the changed files: 0 errors (the pre-existing unused-`eslint-disable` warning in the ribbon test; the ribbon
  script itself sits outside the package's lint base path);
- `node --check sprk_access_ribbon.js` OK.

**external-spa** (§23.7, the merged tree): tsc 0, lint 0, vitest 13/13.

**Publish size** (CLAUDE.md §10, NFR-01). Each tree was extracted fresh with `git archive` into a SHORT path (`C:\w150vm`,
`C:\w150va`, `C:\w150vb`; removed afterwards) and built with `dotnet publish -c Release -o <root>\deploy\api-publish`
(framework-dependent linux-x64, from the csproj). Each was zipped with `Compress-Archive -CompressionLevel Optimal` over
`api-publish\*`, **PDBs included** (4). No MSB3030 on any side; 212 files each.

| Tree | Commit | Files | Zip |
|---|---|---|---|
| `origin/master` | `293fcd4c8` | 212 | **45.65 MB** (47,864,294 B) |
| this round's base | `0f993b651` | 212 | 46.05 MB (48,285,725 B) |
| this round (code-final) | `c0acfd7b2` | 212 | **46.05 MB** (48,288,275 B) |

This round adds **+0.002 MB** (+2,550 B). The integration tree is +0.40 MB over master (batch 4's tasks together). That is
far under the 60 MB ceiling and the +5 MB escalation.

**CVE:** `dotnet list package --vulnerable --include-transitive` on `Sprk.Bff.Api` reports "no vulnerable packages".
This round adds no package.

### 23.9 Placement and justification (CLAUDE.md §10 / §11)

No new endpoint, service, DI registration, option, job, column, package or PCF. No `.claude/**` edit needed. Hot path:
BFF = Y (already declared). Each new member below answers the three questions.

| New member | Existing | Extension | Cost of doing nothing |
|---|---|---|---|
| `ResumeMakeSecureAsync` (private) | The resume's F8 rules (`ResolveResumeCreatorAsync` …) and the forward Make Secure path | Reuses `ResolveMakeSecureCreatorAsync`, `EnsureSecureFlagAsync`, `EnsureCreatorShareAsync` and the colleague step. It is separate only because the resume has no move to make or undo | A Make Secure retry by a non-creator leaves them without access (round 40 item 2), and one on a record with a walled or disabled creator is refused 409 forever (R-150-6) |
| `MakeSecureCallerMask` (internal static) + `EnsureCreatorShareAsync`'s optional target | `CreatorAccessMask`, `RecordShareLevels` | A pure function over the existing masks; one optional parameter on the existing ensure | A Full Access holder who runs Make Secure is narrowed to Collaborate and loses F3 |
| `CallerUnresolved` / `CallerWallRefusal` (private) | Inline in the forward path | Extracted, text unchanged | Two copies of the refusal text would drift |
| Ribbon `MAKE_SECURE_RETRY_IN_PLACE`, `UNNAMED_PERSON`, the `unfinished` state | The ribbon's `readSecureState` / `runDesignation` / `personName` | One read widened, the existing dialog helper reused | R-150-6 stays open; an empty name is shown |
| Wizard `UNNAMED_PERSON` | `describeSkippedPrincipal` | One constant and a guard | The raw id, or an empty name, is shown |

ADR-002: no plugin. ADR-003: every new refusal comes before any write, and an unknown flag still hides both commands.
ADR-006: the ribbon stays a thin command script. ADR-028: the token is `BffAuth.authenticatedFetch`'s. ADR-038: no
`Mock<HttpMessageHandler>`, no DI or constructor tests; every new guard was seeded.

## 24. Integration round c-v2 (2026-10-05, `task/uac-r2-150-integ-c-v2` from `task/uac-r2-150-integ-c-v1` @ `1cecc8e31`) — round 46, the verifier's 17 items

**Binding input:** round 46 (main session under round 15, `work/unified-access-control-r2` @ `71320b657`; re-read at
`c998979df`, whose newer round 50 is task 140's and changes nothing here): item 1 (the caller's share floored on
EFFECTIVE rights, option b), item 2 (`files_incomplete`'s backstop is 147's job, wired at integration), item 3 (§23.4
confirmed), item 4 (the client tells a non-default owner team apart; "documented edge" rejected), item 5 (the resume's
unverifiable No Access refusal gets a test that bites). No `NOTE-FROM-MAIN.md` existed. No live call of any kind was
made (Dataverse, Azure, Entra, SPE: none, not even a read).

### 24.1 Round 46 item 1 — the Make Secure caller's share is floored on their EFFECTIVE rights (verifier items 3, 12)

- **What changed.** `ProvisionProjectEndpoint.ReadMakeSecureCallerFloorAsync` asks Dataverse, AS THE CALLER, what rights
  they hold on the record (`CallerRecordAccessProbe.GetCallerRightsAsync` — `RetrievePrincipalAccess`, the same answer
  owner F3 decides Full Access from, `SecureDesignationRemoval.cs` `FullAccess = Write | Delete`). It runs in the same
  pre-write step as WhoAmI: on the forward path right after WhoAmI in `MoveWithCreatorShareAsync`, on a Make Secure resume
  right after WhoAmI in `ResumeMakeSecureAsync` — before the No Access check, the creator read and every write.
- **The floor.** `MakeSecureHeldMask(effective)` = the Full Access share bits those rights carry
  (`RecordShareLevels.Intersect` over the Full Access level: Read, Write, Append, AppendTo, Share, Delete — never Create
  or Assign). The target is `MakeSecureCallerMask(held)` = `(held & FullAccess) | Collaborate`, with `held` = the
  pre-call explicit share ∪ the share a later read shows ∪ the effective-rights bits. So:
  - Full Access held by a share, by OWNING the record, or by a security ROLE → an explicit Full Access share (Delete
    kept, so F3's right to remove the designation is kept);
  - anything less → Collaborate (never less: the creator's level);
  - never Assign, never more than Full Access.
  It applies at share-first, the Unverified-move ensure, Step 5.5, the resume, **and the Unverified-move fallback grant**
  (issued without a read when the ensure fails — it used to grant the bare creator level; now the floor, so it cannot
  narrow a Full Access holder either).
- **Fail closed (ADR-003).** A probe that throws, or an answer without the Write the route's gate admitted moments earlier
  (the probe's "could not answer" is `AccessRights.None`, deliberately indistinguishable from "no rights"), refuses before
  any write: 500, the detail "Which access you hold on this {record} could not be read, so securing it could not make
  sure you keep that access. Nothing was changed; you may try again."
- ~~**Interpretation recorded — "the existing unverifiable code".**~~ **Superseded by round 53 item 1 (§25.1):** the
  refusal is now provisioning's own `500 sdap.provision.caller_rights_unverifiable` (codes are namespaced by endpoint,
  round 26 item 1), the detail unchanged. The original text: `sdap.unsecure.permission_unverifiable`
  (`SecureDesignationRemoval.PermissionUnverifiableReasonCode`): it is the one code that already means "the caller's rights
  on this secure record could not be read" (F3's `RightsUnreadable` basis), and the floor exists to keep exactly the right
  F3 decides from them. Every provisioning "unverifiable" code names a different fact — `creator_no_access_unverifiable`
  the No Access list, `record_creator_unverifiable` the record's creator — so reusing one would mislabel the refusal in the
  detail, the logs and any client mapping. No new reason code. The wizards' path never reads the floor (pinned below), so
  the wizard can never receive it; the wizard's EMITTED test comment says so. The ribbon shows the server's detail and
  does not offer it in place (it is a pre-write refusal; the record is unchanged).
- **Behaviour change, as round 46 intends.** A record OWNER who runs Make Secure on their own record now ends with Full
  Access (they held Delete by ownership); an administrator who runs it ends with Full Access (role). The wizards' creator
  still gets EXACTLY Collaborate (no transition, no floor read).
- **Tests** (`SecureFlagEndpointWriteTests`):
  - ownership-held, role-held (forward) and role-held (resume) Full Access → an explicit Full Access share; the creator at
    Collaborate; exactly two rights probes (the gate, then the floor) — theory ×3 **[the ownership and role rows ran the
    SAME fixture path here — corrected in §25.3: they now run different ones]**;
  - Collaborate-only (no Delete held, even as owner) → exactly Collaborate, forward and resume — theory ×2;
  - the floor read fails — the probe throws, or answers without Write — forward and resume: 500
    `sdap.unsecure.permission_unverifiable` **[since round 53: `sdap.provision.caller_rights_unverifiable`, §25.1]**, the
    detail verbatim, nothing written — theory ×4 (fixture switch
    `FollowUpRightsProbeAnswersNone`, beside `FullAccessProbeThrows`);
  - the wizards' path reads no floor: a caller holding Delete and a probe that would throw after the gate → 200, exactly
    Collaborate, one probe;
  - the Unverified-move fallback grant carries the floor (two grants for the caller, both Full Access).
  - The share-held case is the existing `Provision_MakeSecure_ByAFullAccessHolder_KeepsTheirFullAccess` (both paths).
- **Seeds** (affected filter, 890 tests; each restored from a backup, touched, hash re-checked
  `c0d4a2a0…`):
  - SF1, the floor ignores effective rights (`held = 0`): 4 fail.
  - SF2, a throwing probe read as "no floor": 2 fail.
  - SF3, an answer without Write accepted: 2 fail.
  - SF4, the resume reads the floor after the flag write: 2 fail.
  - SF5, the wizards' path reads the floor too: 4 fail (incl. three existing `SecureNamedOwnerTeam` probe-count pins).
  - SF6, the fallback grant at the bare creator level: 1 fail.
- **Docs corrected**: guide §7d.1 (the floor stated precisely: effective rights by share, ownership or role; Full Access
  or Collaborate; never Assign; the refusal code); §23.4 above (marked corrected); the POML ui-test's expected text.

### 24.2 Round 46 item 2 — `files_incomplete`'s scheduled backstop (verifier item 14; resolves the §23.3 STOP)

- **Decided:** 147's `SecureChildReconciliationJob` (every 2 minutes) is the backstop; each run also settles the PENDING
  Make Secure relocations recorded in the relocation ledger through the ONE `DocumentContainerRelocator`, capped per run
  and reported. Not a new job; not 166's `DocumentContainerMigrationJob` (registered disabled). `-SecureTransitionDeployed`
  is gated on that job being registered with its writes on.
- **Why it is not built on this branch.** It is wired at integration "after 147, 150 and 166 are all on the integration
  branch" (round 46). Checked at `integ/uac-r2-batch4` @ `bc6bc6c6d`: `git merge-base --is-ancestor` is false for
  `task/uac-r2-147-r1c-v1`, `task/uac-r2-166-f1-v2` and `task/uac-r2-140-x1-v1c-v1`. **[Stale — corrected in §25.5:** at
  `a8b811e66` 147 IS on `integ` (merge `13af65efe`); the wiring now waits only on 166 (and 150) being there.**]** The relocator and the job's
  relocation ledger exist only on 166's branch; a second relocator is forbidden (round 26 item 3).
- **The integration step, complete** (with §22.4's wiring, in one change, by the main session):
  1. §22.4: `DocumentContainerRelocator.RelocateDocumentsAsync` (purpose `MakeSecure`) wired into provisioning's Make
     Secure path and the already-provisioned re-entry branch, with 500 `sdap.provision.files_incomplete` and its client
     entries (`MAKE_SECURE_RETRY_IN_PLACE` already carries it — access_ribbon.js needs no second release).
  2. `SecureChildReconciliationJob`: after its recent-changes pass, settle the ledger's pending `MakeSecure` relocations
     through that relocator, at most a configured number per run; report `makeSecureRelocations` (pending, settled,
     failed, roots) in `ResultJson`; a root still owing a step is listed among `incompleteRoots`, and the run is not a
     success while any is owed (147's rule 4 posture). Its writes follow the job's recent-changes pass: on unless an
     emergency stop (round 28 E2 posture), so "registered with writes on" is its default.
  3. Tests: a root left `files_incomplete` is finished by the next run; the per-run cap; a failed relocation is reported
     and retried next run; a seed that disables the settle call fails them.
  4. The release gate made mechanical: `Set-AccessRibbon.ps1 -Apply -SecureTransitionDeployed` refuses unless a read-only
     `GET {BFF}/api/admin/jobs/secure-child-reconciliation/status` answers `Enabled: true` with `CronSchedule
     "*/2 * * * *"` and its latest run's `ResultJson` shows the Make Secure relocation settle in write mode; an
     offline-harness test of that check (the lock script's precedent).
- **Done here (text only, the rule already holds as a packaging rule):** the release rule now names the backstop in the
  AccessRibbons README (the "release-gated" section and Deployment steps 1, 4–6), `Set-AccessRibbon.ps1` and
  `Merge-AccessRibbon.ps1` help, guide §7d.1, §9 G-11, and the ribbon script's `MAKE_SECURE_RETRY_IN_PLACE` comment.
  Parse check 0 errors on both scripts; `Set-AccessRibbon.ps1 -SecureTransitionDeployed` dry run (no Dataverse call)
  PASSED.

### 24.3 Round 46 item 3 — §23.4 confirmed (the broader reading)

The code already ran the full forward rule set on a Make Secure resume (§23.4); round 46 adds the effective-rights read to
its first step (§24.1). Docs: the endpoint's class remarks and `ResumeMakeSecureAsync` remarks name the order (WhoAmI and
effective rights; No Access; creator read; flag; the caller's proven share; the creator in the colleague step); guide
§7d.1 states it and that an administrator who finishes a record through the ribbon is shared to like any caller; a direct
API call with no `transition` keeps the creator rule (F8 — pinned since c-v1).

### 24.4 Round 46 item 4 — the client tells a non-default owner team apart (verifier item 8)

- **Server.** `GET can-manage-access?recordType=&recordId=&includeOwner=true` answers, beside the unchanged delegation
  answer, `owningTeamId` (the owning team; null when a user owns it) and `ownedBySecureOwnerTeam` (true / false; null =
  could not be told). `RecordAccessGateEndpoint.ReadOwnerAsync`: one app-only read of the record's owner columns; a
  user owner answers false at once; a team owner is compared with the Secure Record Owners team as
  `SecureRecordOwnerTeam.IdentifyAsync` names it — steps 1–2 of `ResolveAsync`, extracted so the gate and provisioning
  use ONE rule (`ResolveAsync` now calls it; behaviour unchanged). An owner read that fails, a row with no owner, or a
  Secure team that is absent, ambiguous or unreadable → null, logged — never either answer.
  - **Opt-in, deliberately.** The route is the Manage Access gates' form-load path (`TrackingFieldTrio`, the flyout's own
    visibility), whose design is one rights probe and no read; only the ribbon's secure-state rule asks for the owner.
  - It changes nothing about who is admitted: a caller without Write still gets the filter's 403, and no owner read is
    made (pinned).
- **Client.** `sprk_access_ribbon.js` **1.4.0**: a flagged record with its container recorded and NO owning user is
  finished only when the server says the Secure Record Owners team owns it. Another team — a reassignment outside Spaarke
  — is unfinished: Make Secure ("finish") is offered beside Remove Secure. An answer it cannot get (no token, a non-200,
  an answer about another record, null, a failure) keeps Make Secure hidden on that record; Remove Secure still follows
  the flag. The question is asked only when the one `Xrm.WebApi` read leaves it open (flagged + container + no owning
  user) — never for a non-secure, container-less or user-owned record — and, being part of the secure state, it is
  forgotten with it before every refresh (asked again after a command).
- **The retry's server path.** A flagged root with its own container that is user-owned (a legacy record provisioned
  before task 133's owner move) or owned by an ordinary team in another business unit takes the FORWARD path: its
  container is classified as its own and kept (no second container, the value not rewritten), the flag is not written
  again, the record is re-owned to the Secure Record Owners team, the caller and the creator are shared. Pinned for both
  shapes. (A root owned by ANOTHER team INSIDE the Secure Record business unit — the retired default team before task
  144's migration — is still refused 409 `owned_by_other_secure_team`, whose detail names the migration script; ~~the
  ribbon offers Make Secure there too, and the refusal is shown, never silent~~ — **superseded by round 53 item 2
  (§25.2):** such a record is already isolated, the server reports it as inside the Secure Record business unit, and the
  ribbon HIDES Make Secure on it.)
- **Tests.** `RecordAccessGateTests` +8: user / Secure team / other team (theory ×3); owner read fails / no owner /
  Secure team ambiguous → null (theory ×3); not asked → no owner read; no Write → 403 and no owner read. Ribbon jest +13:
  another team ×3 tables (both commands, one owner request, the exact URL with `includeOwner=true` and the bearer token);
  the Secure team → hidden; unknown ×4 (null, non-200, another record, a failed request — **[overstated; corrected in
  §25.3:** three of the four used a synchronous mock, so they reached only the catch, never the guard they named**]**);
  no token → not asked, hidden;
  the read decides ×3 (not secure, no container, legacy user-owned) → not asked; after Make Secure asked again.
  `SecureFlagEndpointWriteTests` +2 (legacy user-owned; other-team-owned).
- **Seeds.** Server (890-test filter): SG5 any team counted as the Secure team — 1 fail; SG6 an unidentifiable Secure team
  read as "not it" — 1 fail; SG7 an owner-read failure read as user-owned — 1 fail; SG8 the owner read made without
  `includeOwner` — 1 fail (SG7 and SG8 were run together, each failing only its own test); SL2 a flagged record with a
  container treated as provisioned whoever owns it — 36 fail, incl. both new shapes. Ribbon: CR9 the owner answer
  ignored — 4 fail; CR10 an unknown answer read as unfinished — 5 fail; CR11 the owner asked even when the read decides
  — 3 fail; CR12 the owner answer cached across a command — 1 fail.
- **ui-test** (POML): "Make Secure finishes a secure record the Secure Record Owners team does not own (round 46 item 4;
  the verifier's item 8)" — part of G-11; it is also the G-11 check of acceptance (e) for legacy secure records still
  user-owned.

### 24.5 Round 46 item 5 — the resume's unverifiable No Access refusal (verifier items 2, 11)

`Provision_MakeSecure_FinishingARecord_WhenTheCallersNoAccessCheckCannotBeRead_IsRefusedBeforeAnyWrite`
(`NoAccessList.Faults = true`): 500 `creator_no_access_unverifiable`, `AssertResumeWroteNothing`. **Seed SR9** — the
verifier's own (`if (wall.RefusesShare)` → `if (wall.Outcome == SecureShareWallOutcome.Walled)` in `ResumeMakeSecureAsync`)
— now fails 1 (it survived all 1772 in c-v1).

### 24.6 Verifier item 6 (LOW) — seeds R2 and R4

- **R2** (the ribbon's `personName` on a whitespace-only id): pinned — a whitespace-only id is "Someone" and no user read
  is made with it. Seed CR13 (trim removed): 1 fail.
- **R4** (the wizard's `SKIPPED_PRINCIPAL_GENERIC` blank guard): the verifier is right — it was redundant (its only caller
  passes a name `skippedPersonName` already resolved) and the note's claim was untested. The guard is removed (one place
  decides "Someone"), the generic path's "Someone" is pinned (+2: no name, a blank name, for an unknown reason code), and
  §23.5 is corrected. Seed CW3 (`skippedPersonName` returns the name unresolved): 2 fail.

### 24.7 The verifier's 17 items

| # | Disposition |
|---|---|
| 1 | Re-checked on this branch: POML parses (`xml.dom.minidom`, §24.8); no `NOTE-FROM-MAIN.md`; no live write; merge-tree against the current tips in §24.8 |
| 2, 11 | **Closed** — §24.5 (round 46 item 5) |
| 3, 12 | **Closed** — §24.1 (round 46 item 1: floored on effective rights; docs corrected) |
| 4, 5, 7 | Verified by the verifier; re-held (the affected filter 890/890 and the client suites below include every pinned case) |
| 6 | **Closed** — §24.6 |
| 8 | **Closed** — §24.4 (round 46 item 4 makes it the intended completion, tested server-side; the G-11 ui-test checks legacy data) |
| 9 | Superseded by this round's client runs (§24.8) |
| 10, 13 | **Closed by this round's own runs** (§24.8: the full BFF unit suite, NetArchTest and BOTH integration suites, once, at the end). The verifier's `C:\wvf150` still exists (`git worktree list`: detached at `1cecc8e31`) with its `node_modules` JUNCTION into the r2 worktree; it is outside this agent's worktree, so it is not removed here — main session, in this order: `cmd /c rmdir C:\wvf150\src\client\shared\Spaarke.UI.Components\node_modules` (removes the junction only), then `git -C C:\code_files\spaarke worktree remove --force C:/wvf150`, then `git -C C:\code_files\spaarke worktree prune` |
| 14 | **Decided and specified** — §24.2 (round 46 item 2): an integration step by the main session after 147, 150 and 166 are on the integration branch; the release gate holds until it lands |
| 15 | Integration step unchanged (§23.7): 140 is still not on `integ` (§24.2's ancestry check); the resolution and the three commands are recorded |
| 16 | Live gate G-11 (manual, main session; §9 row updated: access_ribbon.js 1.4.0, round 46's ui-test) |
| 17 | Live gates G-0 (every environment other than dev) and G-1…G-10 — manual, main session (§9) |

### 24.8 Tests, publish size, CVE, merge (this round)

**Affected first** (filter: `SecureFlag*`, `SecureProjectShareTests`, `ProvisionNoAccessTests`, `ProvisionProject*`,
`ProvisionResume*`, `ProvisionRecordedContainer*`, `ProvisionAssignCascade*`, `UnsecureProject*`, `RecordContainerResolver*`,
`OrganizationMembershipReadTests`, `RecordOwnershipResolverTests`, `EmptySecureFlag*`, `SecureNamedOwnerTeam*`,
`SecureChild*`, `RecordAccessGate*`, `DelegationRule*`, `SecureRecordOwnerTeam*`, `SecureRecordIsolationCensus*`):
**890/890** before every seed, under none of them after restoring, and again on the committed code after the pre-commit
hook's `dotnet format` / prettier pass. `SecureFlagEndpointWriteTests` **110** (96 + 14); `RecordAccessGateTests` +8.

**Once at the end** (code-final commit `21be49981`; this box, other agents active):
- **Full BFF unit suite: 15660 passed, 0 failed, 54 skipped (15714 = c-v1's 15692 + 22)** — one clean run;
- **Spaarke.ArchTests 600/600**;
- **Sprk.Bff.Api.IntegrationTests 104/104**;
- **Spe.Integration.Tests 403 passed, 25 skipped, 0 failed (428)**.

**Client** (`@spaarke/ui-components` in THIS worktree: `npm install --legacy-peer-deps --no-audit --no-fund` for
`Spaarke.Auth` and `Spaarke.SdapClient` (each then `npm run build`) and for the library; no lockfile changed):
- jest **10 suites 243/243** (CreateProjectWizard 8 suites, `SummarizeFilesDialog.provisioningHost`,
  `accessRibbon.secureCommands` 81), before and after the hook's prettier pass;
- `tsc --noEmit -p tsconfig.json` exit 0; `npm run build` (the package's build, `tsc`) exit 0;
- eslint on the changed files: 0 errors (the pre-existing unused-`eslint-disable` warning in the ribbon test);
- `node --check sprk_access_ribbon.js` OK.
- Seeds, each restored from a backup, touched, hash re-checked (`6d7ab15d…` ribbon, `c2a01134…` wizard): CR9 4, CR10 5,
  CR11 3, CR12 1, CR13 1, CW3 2 failures.

**Publish size** (CLAUDE.md §10, NFR-01). Each tree extracted fresh with `git archive` into a SHORT path (`C:\w2m`,
`C:\w2a`, `C:\w2b`; removed afterwards) and published with `dotnet publish -c Release -o <root>\deploy\api-publish` from
the csproj (framework-dependent linux-x64); each zipped with `Compress-Archive -CompressionLevel Optimal` over
`api-publish\*`, **PDBs included** (4). No MSB3030 and no error on any side; 212 files each.

| Tree | Commit | Files | Zip |
|---|---|---|---|
| `origin/master` | `293fcd4c8` | 212 | **45.65 MB** (47,864,324 B) |
| this round's base | `1cecc8e31` | 212 | 46.05 MB (48,288,379 B) |
| this round (code-final) | `21be49981` | 212 | **46.06 MB** (48,298,174 B) |

This round adds **+0.009 MB** (+9,795 B); the integration tree is +0.41 MB over master (batch 4's tasks together). Far
under the 60 MB ceiling and the +5 MB escalation.

**CVE:** `dotnet list package --vulnerable --include-transitive` on `Sprk.Bff.Api`: "no vulnerable packages". This round
adds no package.

**Hygiene:** POML parses (`xml.dom.minidom`; 12 outcomes, 4 amendments — R3b, UX, R40, R46 — 10 ui-tests).
`git merge-tree --write-tree` against `integ/uac-r2-batch4` @ `bc6bc6c6d` and `work/unified-access-control-r2` @
`c998979df` (both newer than the brief's tips): clean. 140, 147 and 166 are not ancestors of `integ`.

### 24.9 Placement and justification (CLAUDE.md §10 / §11)

No new endpoint, service, DI registration, option, job, column, package, reason code or PCF. No `.claude/**` edit needed.
Hot path: BFF = Y (already declared). bff-extensions.md: modification of existing surface — one existing route gains an
optional query parameter and two optional response fields; one existing endpoint gains private steps; one existing static
helper is split, not forked. Each new member answers the three questions.

| New member | Existing | Extension | Cost of doing nothing |
|---|---|---|---|
| `ReadMakeSecureCallerFloorAsync`, `CallerAccessUnverifiable` (private), `MakeSecureHeldMask` (internal static) | `MakeSecureCallerMask` (share-held only), `CallerRecordAccessProbe.GetCallerRightsAsync` (the gate's and F3's read), `RecordShareLevels.Intersect` | One probe call and one pure function over the existing level table, feeding the existing mask; F3's existing code, no new one | A caller who held Delete by ownership or role loses it, and with it F3's right to remove the designation (verifier item 3, round 46 item 1) |
| `RecordAccessGateQuery.IncludeOwner`, `RecordAccessGateResponse.OwningTeamId` / `OwnedBySecureOwnerTeam`, `RecordAccessGateEndpoint.ReadOwnerAsync` / `OwnerSelect` / `RecordOwnerFacts` / `OwnerRow` | `can-manage-access` (the only GET the ribbon calls); provisioning's own root owner read | Opt-in on the existing route (round 46 names this route); the owner columns provisioning already reads | The ribbon cannot tell the Secure Record Owners team from another team, so a secure record reassigned outside Spaarke stays unfinishable from the command (round 46 item 4: the "documented edge" was rejected) |
| `SecureRecordOwnerTeam.IdentifyAsync` + `SecureOwnerTeamIdentity` (internal) | `SecureRecordOwnerTeam.ResolveAsync` steps 1–2 | Extracted; `ResolveAsync` calls it — ONE rule for "which team" | A second copy of the BU/team identification would drift from provisioning's (two definitions of the Secure Record Owners team) |
| Ribbon `queryOwnedBySecureOwnerTeam`, `gateUrl`, `answersFor` | `queryCanManage` | Its request shape and record check, shared | The 1.4.0 rule has no owner answer to read |
| Fixture switch `FollowUpRightsProbeAnswersNone` | `FullAccessProbeThrows` | The same second-probe hook, answering None | The "answer without Write" refusal is unpinned |

ADR-002: no plugin. ADR-003: every new refusal comes before any write; an owner answer that cannot be had is null and
offers nothing; a failed rights read refuses rather than flooring at nothing. ADR-006: the ribbon stays a thin command
script. ADR-008: `can-manage-access` is still decided by the group filter alone; the owner facts decide nothing
server-side. ADR-028: the ribbon's token is `Spaarke.BffAuth`'s (`getToken`, the established gate-call pattern). ADR-038:
no `Mock<HttpMessageHandler>` (the gate tests answer `DataverseWebApiClient`'s virtual `QueryAsync` seam), no DI or
constructor tests; every new guard was seeded.

### 24.10 Open — integration steps and manual gates (main session)

- **§22.4 + §24.2 in one integration change**, once 147, 150 and 166 are all on the integration branch: the relocation
  wired into Make Secure (`files_incomplete`), the `SecureChildReconciliationJob` settle pass, and the mechanical
  `-SecureTransitionDeployed` check. Until then the release gate withholds Make Secure.
- **§23.7** when 140 and 150-integ-c meet: the two external-spa conflicts' resolution, then tsc, lint and vitest.
- **Live gates** G-0 (every environment other than dev) and G-1…G-11 (G-11 with access_ribbon.js 1.4.0 and round 46's
  ui-test — **since §25: 1.5.0**, and task 144's migration before the ribbon), §9. No live write was made here.
- **`C:\wvf150`** (the c-v1 verifier's worktree): remove the junction first, then the worktree (§24.7 item 10).

## 25. Integration round d (2026-10-05, `task/uac-r2-150-integ-d` from `task/uac-r2-150-integ-c-v2` @ `ba3e315ce`) — round 53, the verifier's 12 items

**Binding input:** round 53 (main session under round 15, `work/unified-access-control-r2` @ `4b9139b74`) and round 46,
read in full; re-read at `6532bb494`, whose newer rounds are other tasks' (54: 166; 55: 132; 57: 165; 58: 158) except
**owner round 56** — the finishing bar from now on: classify every finding (fix (a) runtime, (b) compounding
maintainability, (c) measurable performance; record (d) adversarial-only bypasses, (e) rare fail-closed edges, (f) minor
seeding requests as **known limits**), the over-engineering check, and at most one more fix round per lane. Every change
here is a round-53 decision of class (a) or (b) (§25.1–§25.3); the new members pass §11's three questions (§25.6) and none
is bigger than its problem; this round's known limits are §25.7. A `NOTE-FROM-MAIN.md` arrived mid-round restating owner
round 56 and making this the lane's last fix round: read, applied (§25.4 classifies every item), never committed. No live call of any kind
was made (Dataverse, Azure, Entra, SPE: none, not even a read). Code-final commit `aea926764`.

### 25.1 Round 53 item 1 — provisioning's own code for the floor-read fault (supersedes §24.1's interpretation)

- **Server.** `ProvisionProjectEndpoint.ReasonCallerRightsUnverifiable = "sdap.provision.caller_rights_unverifiable"`;
  `CallerAccessUnverifiable` answers it (500) with the detail unchanged — "Which access you hold on this {record} could
  not be read, so securing it could not make sure you keep that access. Nothing was changed; you may try again." ({record}
  = the root's lower-cased display label). F3's `sdap.unsecure.permission_unverifiable` is no longer used by
  provisioning (round 26 item 1: codes are namespaced by endpoint). The method docs and the resume's order remarks name
  the new code.
- **Wizard client** (`provisioningService.ts`): a `REASON_STATES` entry — `not-started`, `retryable: true` — and the
  `EMITTED` row (the list is now 39; the old carve-out comment is gone), pinned verbatim by its own test.
  **Interpretation recorded (one sentence of copy):** the wizard's message is round 53's sentence with `{record}` =
  project **without its closing "; you may try again"**: "Which access you hold on this project could not be read, so
  securing it could not make sure you keep that access. Nothing was changed." The client's copy rule — binding since task
  068/133 (FR-31) and pinned for EVERY code by "never advises trying again in the message" — puts the retry in the host's
  action keyed on `retryable` ("Try securing again"), never in words, so advice and button cannot come apart. With
  `retryable: true` that action is exactly the ratified "you may try again". The ribbon and the server carry the sentence
  whole. (The wizards send no transition, so they never receive this code — `Provision_TheWizardsPath_ReadsNoFloor_…`.)
- **Ribbon** (`sprk_access_ribbon.js` 1.5.0): `REFUSAL_COPY` (ONE frozen constant) holds the ratified copy with
  `{record}`; `refusalFor` shows it, `{record}` filled per table, for that code (else the server's message, as before).
  It is an ALERT, not one of `MAKE_SECURE_RETRY_IN_PLACE`: a pre-write refusal leaves the record unchanged, so the enable
  rule still offers Make Secure — that is the ribbon's "you may try again" (pinned: after the refusal `canMakeSecure` is
  true). The in-place list stays the failures AFTER the flag write (round 40 item 1's definition).
- **Tests:** `SecureFlagEndpointWriteTests` read-fault theory ×4 now pins the wire code literally; ribbon +4 (×3 tables,
  the constant); wizard +2 (the `EMITTED` row, the verbatim pin).
- **Seeds:** SC1 (the old F3 code restored in `CallerAccessUnverifiable`) — 4 of 895 fail (the read-fault theory). C10
  (the `REFUSAL_COPY` lookup disabled) — 3 of 95 ribbon fail (the three tables). CW4 (the wizard entry renamed away) —
  3 of 98 fail; CW5 (not retryable) — 2 fail.

### 25.2 Round 53 item 2 — another team INSIDE the Secure Record business unit is already isolated: Make Secure hidden

- **Server.** `GET can-manage-access?…&includeOwner=true` answers a third fact, `owningTeamInSecureBusinessUnit`: for a
  TEAM owner, `true` when the record's owning business unit is the Secure Record business unit (the Secure Record Owners
  team, or ANOTHER team there), `false` when it is another business unit; `null` for a user owner (no team to place), or
  when it cannot be told (identification refused, or the record read without its owning business unit — logged). The
  owner read now selects `_owningbusinessunit_value`. "Inside" is ONE predicate,
  `SecureRecordOwnerTeam.IsInSecureBusinessUnit(owningBusinessUnitId, secureBusinessUnitId)` (`bool?`), which
  provisioning's `RootRow.IsOwnedInBusinessUnitByAnotherTeam` — the 409 `owned_by_other_secure_team` rule — now calls too
  (`== true`, so its old null/empty handling is unchanged). So the ribbon hides Make Secure on exactly the records that
  refusal answers.
- **Ribbon 1.5.0.** `OWNER_PLACEMENT` (ONE frozen constant: `secure-owner-team`, `other-team-inside`,
  `other-team-outside`); `ownerPlacementOf(body, record)` reads the three facts in ONE place; `queryOwnerPlacement`
  (was `queryOwnedBySecureOwnerTeam`) asks. A flagged, team-owned record with a container is **unfinished only when its
  team is OUTSIDE** the Secure Record business unit; the Secure Record Owners team → finished; another team inside →
  isolated already, Make Secure hidden, Remove Secure shown (logged as an explanation, not a warning); anything not
  definite → hidden.
- **The server's refusal is unchanged:** `Provision_MakeSecure_OnAFlaggedRecordOwnedByAnotherTeamInsideTheSecureBusinessUnit_IsStillRefused409`
  — on the make-secure path, 409 naming `scripts/Migrate-SecureRecordsToNamedOwnerTeam.ps1`, nothing written, and
  refused before the caller's rights are read (one probe: the gate).
- **Integration checklist — task 144's migration `-Apply`/`-Verify` is a live gate before this ribbon ships.** Written
  into the AccessRibbons README (Deployment step **1a**, the exact dry run / `-Apply -AcceptedAssignCascade …` / `-Verify`
  commands; steps 4–6 require 1a's `-Verify` to have passed), guide §7d.1 (the hidden case, and the deploy order),
  `Set-AccessRibbon.ps1`'s help (order), §9 G-11. Moving the record stays out of provisioning (task 144's decision).
- **Brief item 7** (the edge documented in §24.4) is closed by this: the ribbon no longer offers Make Secure there.
- **Scope as decided — "with a container".** A flagged root with NO container that another team inside the Secure
  Record business unit owns is still read as unfinished by the one `Xrm.WebApi` read (round 40 item 1: no container →
  unfinished, no owner question), so the ribbon offers Make Secure and the server answers its unchanged 409 with the
  migration script named — shown, never silent. The migration gate above empties that set before the ribbon ships: task
  144's `-Verify` passes only when no secure row is owned by any team but the named one, container or not. Recorded as a
  known limit (§25.7).
- **Tests:** `RecordAccessGateTests` — the owner theory gains `other-team-inside` (false / true) and `other-team-outside`
  (false / false), `secure-team` (true / true), `user` (false / null); a team owner read without its business unit →
  `null`; the unknown theory also asserts the new fact null; the owner read is answered only when it selects all three
  owner columns. Ribbon: inside ×3 tables (hidden, Remove Secure shown, one request, no warning) and the constant.
- **Seeds** (server: of 895; ribbon: of 95): SG9 the gate's inside fact always "outside" — 3 fail; SG10 an unread owning
  business unit read as "outside" — 1 fail; SG11 the owner read stops selecting `_owningbusinessunit_value` — 5 fail;
  SP1 provisioning's inside-another-team refusal disabled — 2 fail (the new make-secure 409 and the existing wizard-path
  one); C9 the inside placement read as unfinished — 3 fail; C8 an unknown inside fact read as outside — 2 fail.

### 25.3 Round 53 item 3 — the test gaps (the verifier's items 2 and 6)

- **The ribbon's "cannot be told" table (verifier item 2 — confirmed).** With `gateFetch.mockImplementation(() =>
  answer())` three of the four cases returned a plain object, so `fetch(...).then` threw and only the `.catch` ever made
  them "hidden"; §24.4's "unknown ×4" overstated the coverage (corrected there). Now: `mockImplementation(async () =>
  answer())` — a real Promise — and each case is a DEFINITE "another team, outside" answer except for the one fact its
  name says, so only that guard can stop it: `ownedBySecureOwnerTeam` null / omitted, a non-200 whose body still reads
  like an answer, an answer about another record, an answer that is not a delegation yes, `owningTeamInSecureBusinessUnit`
  null / omitted, a non-JSON body, a failed request (9). A positive twin (the same answer, unaltered) offers Make Secure.
  Each case also asserts the gate was asked once.
- **Seeds, each failing exactly the case it names** (of 95): **C2** (the record-echo check removed) — 1; **C4**
  (`ownedBySecureOwnerTeam: null` read as false) — 1; C4b (omitted read as false) — 1; **C6** (the owner query's
  status check removed) — 1; C7 (the `canManageAccess` check removed) — 1; C8 — 2 (the null and omitted inside cases).
- **The ownership-held and role-held floor cases (verifier item 6).** They ran the same fixture path (`CallerHoldsDelete`
  answered Delete whoever owned the record). Now `ProvisionProjectTestFixture.CallerDeletesWhatTheyOwn` models Delete
  held by OWNERSHIP (a user-depth role: the probe answers Delete only while the seeded record's CURRENT owning user is the
  caller, read at the moment of the probe), and `CallerHoldsDelete` stays Delete held by a ROLE (business-unit or
  organization depth). The "ownership" row uses the first, "role" and "role-resume" the second; a twin
  (`Provision_MakeSecure_DeleteHeldOnlyOnOwnedRecords_IsNotKept_OnARecordTheCallerDoesNotOwn`, forward and resume) pins
  that Delete-on-owned gives nothing on a record someone else owns. **Seed SO1** — the forward path reads the floor AFTER
  the owner move — fails the `ownership` row and NOT the `role` rows (plus the read-fault and fallback tests the moved
  read also breaks: 4 of 895), so the fake now tells the two apart.
- **Live proof (verifier item 6):** the code rightly delegates to `RetrievePrincipalAccess`, so a REAL ownership-held or
  role-held Delete is provable only live — two G-11 ui-tests are added to the POML: "Make Secure run by the record's
  OWNER keeps their Full Access" (a user-depth Delete role, owner but not creator, no share; RetrievePrincipalAccess read
  before; afterwards an explicit share of mask 327703 — never Assign — and Remove Secure succeeds for them) and "Make
  Secure run by an ADMINISTRATOR keeps their Full Access" (no owner, no creator, no share; afterwards 327703; the creator
  262167).

### 25.4 The verifier's 12 items

Each item's class under owner round 56 (the main session's `NOTE-FROM-MAIN.md`, received mid-round, asks for it: read,
binding, never committed — it says this is the lane's LAST fix round):

| # | Class | Disposition |
|---|---|---|
| 1 | (a)/(b) — round 53's items | Round 46 and round 53 read in full on `work/unified-access-control-r2` (4b9139b74; re-read at 6532bb494); every round 53 item closed — item 1 (b) a mislabelled refusal code, §25.1; item 2 (a) the ribbon offered a command the server always refuses, §25.2; item 3 (b) important fail-closed behaviour without a test that bites, §25.3 |
| 2 | (b) | **Fixed** — §25.3 (a real Promise; the omitted-field case and more; C2 and C4 re-seeded: each 1 fail; C6 too) |
| 3, 4, 5 | — (verified) | Verified by the verifier; re-held — the affected filter 895/895 includes every pinned case, and SR9's test is unchanged |
| 6 | (b) — decided by round 53 item 3 | **Fixed** — §25.3 (different fixture paths, the twin, seed SO1; the two live ui-tests) |
| 7 | (a) — decided by round 53 item 2 | **Fixed** — §25.2: the ribbon hides Make Secure there; the 409 is unchanged and pinned. The no-container variant is a known limit (e), §25.7 |
| 8 | — (information) | Superseded by this round's own runs (§25.5) |
| 9 | (b) — a doc that contradicted the state | POML parses (§25.5); no live write; §24.2's stale ancestry statement corrected (147 — and now 140 — are on `integ`; the backstop wiring waits on 166). `NOTE-FROM-MAIN.md` arrived during this round: read, never staged |
| 10 | (b) | **Fixed** — §25.3 (the record-echo and null-answer guards are pinned; C2 and C4 fail) |
| 11 | — (live gate) | Live gate G-11 (manual, main session; §9 row updated: access_ribbon.js 1.5.0, task 144's migration first, round 53's two ui-tests) |
| 12 | — (live gates) | Live gates G-0 (every environment other than dev) and G-1…G-10 — manual, main session (§9) |

### 25.5 Tests, publish size, CVE, merge (this round)

**Affected first** (§24.8's filter): **895/895** (890 + 5: `SecureFlagEndpointWriteTests` +3, `RecordAccessGateTests`
+2), before the seeds, after restoring each, and on the committed code after the pre-commit hook's `dotnet format` /
prettier pass.

**Once at the end** (code-final `aea926764`; this box, other agents active):
- **Full BFF unit suite:** first run **15600 passed, 65 failed, 54 skipped (15719 = 15714 + 5)** in 36 m 48 s under heavy
  contention (CPU 99–100 %, other agents' test hosts running beside it). All 65 failures are ONE signature —
  `TaskCanceledException` / "Error while copying content to a stream" / "The client aborted the request": the test
  `HttpClient`'s timeout on in-memory host requests that took minutes (e.g. `HealthAndHeadersTests.SecurityHeaders_Present`,
  3 m 22 s) — in 48 classes across Office, Compose, Insights, Workspace, SpeAdmin, Documents and others, none touched by
  this round. **Isolated re-run of those 48 classes: 561/561 passed, 0 failed.** **A second full run on the same commit:
  15665 passed, 0 failed, 54 skipped (15719)** in 32 m 51 s (the box still loaded) — the clean full run;
- **Spaarke.ArchTests: 600/600**;
- **Sprk.Bff.Api.IntegrationTests: 104/104**;
- **Spe.Integration.Tests: 403 passed, 25 skipped, 0 failed (428)**.

**Client** (`@spaarke/ui-components` in THIS worktree: `npm install --legacy-peer-deps --no-audit --no-fund` for
`Spaarke.Auth` and `Spaarke.SdapClient`, each then `npm run build`, and for the library; no lockfile changed): jest **10
suites 259/259** (243 + 16: ribbon 95 = 81 + 14, `provisioningService` 98 = 96 + 2), before and after the hook's
prettier pass; `tsc --noEmit -p tsconfig.json` exit 0; `npm run build` exit 0; eslint on the changed files 0 errors (the
pre-existing unused-`eslint-disable` warning in the ribbon test); `node --check sprk_access_ribbon.js` OK. Client seeds
each restored from a backup, touched, hash re-checked (ribbon `53c608a6…`, wizard `3f2f2b61…`). Server seeds restored the
same way (`8f31d4c0…` provisioning, `7a6ad35e…` gate, `85b57843…` owner team) and the test project rebuilt clean.

**Ribbon scripts:** `Set-AccessRibbon.ps1` and `Merge-AccessRibbon.ps1` parse with 0 errors;
`Set-AccessRibbon.ps1 -SecureTransitionDeployed` dry run (no Dataverse call) PASSED.

**Publish size** (CLAUDE.md §10, NFR-01). Each tree extracted fresh with `git archive` into a SHORT path (`C:\p150dm`,
`C:\p150da`, `C:\p150db`; removed afterwards) and published with `dotnet publish -c Release -o <root>\deploy\api-publish
-nodeReuse:false` from the csproj (framework-dependent linux-x64; the first master attempt died with MSB4166 — an MSBuild
child node exiting on this loaded box — and was re-run clean); each zipped with `Compress-Archive -CompressionLevel
Optimal` over `api-publish\*`, **PDBs included** (4). No MSB3030 and no error on any side; 212 files each.

| Tree | Commit | Files | Zip |
|---|---|---|---|
| `origin/master` | `b4b58a361` | 212 | **45.65 MB** (47,864,547 B) |
| this round's base | `ba3e315ce` | 212 | 46.06 MB (48,298,386 B) |
| this round (code-final) | `aea926764` | 212 | **46.06 MB** (48,299,093 B) |

This round adds **+707 B**; the integration tree is +0.41 MB over master (batch 4's tasks together). Far under the 60 MB
ceiling and the +5 MB escalation.

**CVE:** `dotnet list package --vulnerable --include-transitive` on `Sprk.Bff.Api`: "no vulnerable packages". This round
adds no package.

**Hygiene:** POML parses (`xml.dom.minidom`; 13 outcomes, 5 amendments — R3b, UX, R40, R46, R53 — 12 ui-tests). Status
stays `completed-with-escalation`. `git merge-tree --write-tree`:
- against `work/unified-access-control-r2` @ `6532bb494`: **clean** (and the code commit `aea926764` against `0caca7e3e`);
- against `integ/uac-r2-batch4` @ `a8b811e66`: **clean** (the code commit);
- against `integ/uac-r2-batch4` @ **`b4be59c93`** — which since then has taken master (`e172b3a01`) and **task 140**
  (`task/uac-r2-140-x1-v1c-v2`, merge `b4be59c93`): **exactly §23.7's two conflicts, now live** —
  `src/client/external-spa/src/pages/OutsideCounselDashboard.tsx` (content) and
  `src/client/external-spa/src/types/sdap-client.d.ts` (modify/delete); everything else merges cleanly (the guide
  auto-merges). They are not this round's: they are task 150's external-spa work (round 26/33) meeting 140's, recorded in
  §23.7 with their resolution. **The resolution still applies unchanged:** between `task/uac-r2-140-x1-v1c` @ `2c2bcb232`
  (against which §23.7 verified it: tsc 0, lint 0, vitest 13/13, a seed that bit) and `b4be59c93`, both conflicted files,
  the dashboard tests, `eslint.config.js`, `package.json` and `tsconfig.json` of external-spa are byte-identical
  (`git diff --stat` empty); 140's only later external-spa changes are `IssuedGrantsList.tsx` (+4) and a new
  `tests/ProjectPage.contactGrant.test.tsx`, which merge without conflict. The definitive tsc / lint / vitest run is the
  integration merge's, with that resolution (§25.8).
- Ancestry: 147 (`task/uac-r2-147-r1c-v1`) and 140 are on `integ`; 166 (`task/uac-r2-166-f1-v2`) is not. 166 conflicts
  with this branch in the same 66 paths as with this round's base `ba3e315ce` (none a file this round touches), so this
  round adds no conflict to 166's integration.

### 25.6 Placement and justification (CLAUDE.md §10 / §11)

No new endpoint, service, DI registration, option, job, column, package or PCF. **One new reason code** (round 53 item 1
decided it) and extensions of existing surface. Hot path: BFF = Y (already declared). bff-extensions.md: modification of
existing surface — one existing route's opt-in answer gains one optional field; one existing endpoint's refusal gets its
own code; one existing predicate is extracted, not forked.

| New member | Existing | Extension | Cost of doing nothing |
|---|---|---|---|
| `ReasonCallerRightsUnverifiable` (`sdap.provision.caller_rights_unverifiable`) | F3's `sdap.unsecure.permission_unverifiable` (the unsecure endpoint's), the provisioning `*_unverifiable` codes (other facts) | Round 53 item 1 decided a new provisioning code (codes namespaced by endpoint, round 26 item 1); same refusal, same detail | A provisioning refusal labelled with the unsecure endpoint's code — mislabelled in logs and client mappings (round 53 item 1) |
| `RecordAccessGateResponse.OwningTeamInSecureBusinessUnit`, `RecordOwnerFacts`' third member, `OwnerRow._owningbusinessunit_value` | round 46's opt-in owner facts on `can-manage-access`; provisioning's root read of the same column | One optional field on the existing opt-in answer; one more column on the existing owner read | The ribbon offers Make Secure on an already-isolated record that the server always refuses 409 (round 53 item 2) |
| `SecureRecordOwnerTeam.IsInSecureBusinessUnit` | `RootRow.IsOwnedInBusinessUnitByAnotherTeam` (provisioning's inline comparison) | Extracted; provisioning calls it — ONE predicate | The gate and the 409 decide "inside" twice and can drift: the ribbon would hide or offer Make Secure on records the server answers otherwise |
| Ribbon `OWNER_PLACEMENT`, `ownerPlacementOf`, `queryOwnerPlacement` (renamed), `REFUSAL_COPY`, `refusalFor` | `queryOwnedBySecureOwnerTeam` (two-state), `refusalText` | The same request; one reader of the facts; one copy table beside `SKIPPED_PRINCIPAL_COPY` | The inside case cannot be told apart; the ratified copy is not the ribbon's |
| Wizard `REASON_STATES['sdap.provision.caller_rights_unverifiable']` | `REASON_STATES` | One row | A provisioning code the client lands on the generic copy (the `EMITTED` guard's own rule) |
| Fixture switch `CallerDeletesWhatTheyOwn` | `CallerHoldsDelete` | The same probe override, keyed on the seeded owner | Ownership-held and role-held Delete stay indistinguishable in tests (verifier item 6) |

ADR-002: no plugin. ADR-003: the new refusal is before any write; every owner fact that cannot be had is `null` and
offers nothing; the ribbon hides Make Secure on anything not definite. ADR-006: the ribbon stays a thin command script.
ADR-008: `can-manage-access` is still decided by the group filter alone; the owner facts decide nothing server-side.
ADR-028: unchanged (the ribbon's token is `Spaarke.BffAuth`'s). ADR-038: no `Mock<HttpMessageHandler>` (the gate tests
answer `DataverseWebApiClient`'s virtual `QueryAsync` seam), no DI or constructor tests; every new guard was seeded. No
`.claude/**` edit needed.

### 25.7 Known limits (owner round 56: recorded, no fix round)

- **(e)** A flagged root with NO container owned by another team INSIDE the Secure Record business unit: the ribbon's one
  read calls it unfinished, so Make Secure is offered and the server answers its unchanged 409
  `owned_by_other_secure_team` (the detail names the migration script) — fails closed, shown, never silent. No realistic
  trigger once task 144's migration `-Verify` has passed, which the ribbon's release now requires (§25.2).

### 25.8 Open — integration steps and manual gates (main session)

- **§22.4 + §24.2 in one integration change**, once 166 (and 150) are on `integ` (147 already is): the relocation wired
  into Make Secure (`files_incomplete`), the `SecureChildReconciliationJob` settle pass, and the mechanical
  `-SecureTransitionDeployed` check. Until then the release gate withholds Make Secure.
- **Task 144's migration before the ribbon ships** (round 53 item 2): in each environment,
  `pwsh scripts/Migrate-SecureRecordsToNamedOwnerTeam.ps1 -EnvironmentUrl https://<org>.crm.dynamics.com` (dry run), then
  `… -Apply -AcceptedAssignCascade <what the dry run reported>`, then `… -Verify` (exit 0) — AccessRibbons README step 1a.
- **§23.7, now due**: 140 is on `integ` (`b4be59c93`), so merging this branch meets the two external-spa conflicts. Take
  150's `OutsideCounselDashboard.tsx` whole; keep `src/types/sdap-client.d.ts` deleted (`git rm`); then in
  `src/client/external-spa`: `npm install --legacy-peer-deps --no-audit --no-fund`, `npx tsc --noEmit -p tsconfig.json`,
  `npm run lint`, `npx vitest run` (now including 140's `ProjectPage.contactGrant.test.tsx`).
- **Live gates** G-0 (every environment other than dev) and G-1…G-11 (G-11 with access_ribbon.js **1.5.0**, round 40's,
  round 46's and round 53's ui-tests), §9. No live write was made here.
- **`C:\wvf150`** (the c-v1 verifier's worktree, still listed by `git worktree list`): remove its `node_modules` junction
  first (`cmd /c rmdir C:\wvf150\src\client\shared\Spaarke.UI.Components\node_modules`), then
  `git -C C:\code_files\spaarke worktree remove --force C:/wvf150`, then `git -C C:\code_files\spaarke worktree prune`.
