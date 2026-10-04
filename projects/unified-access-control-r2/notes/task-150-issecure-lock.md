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

**Ribbon (step 5) — BUILT on the integration branch (§21), pending its live gate G-11.** ~~STOP~~ superseded: task 142's
Access group merged into `integ/uac-r2-batch4`; Make Secure and Remove Secure are in 142's ONE group, ONE ribbon file
and ONE command script, Make Secure release-gated to task 148 (same release) and confirmed with owner round 27's copy.

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
| G-11 | Access ribbon (§21): after G-1 (BFF with 148 + 150) and the web resources (`sprk_/scripts/access_ribbon.js` 1.1.0, `assignedaccess_postsave.js`, `bff_auth.js`): `pwsh infrastructure/dataverse/ribbon/AccessRibbons/Set-AccessRibbon.ps1 -SecureTransitionDeployed` (dry run), then `-EnvironmentUrl … -SolutionName <ribbon solution> -SecureTransitionDeployed -Apply` (exports, checks in the work-assignment ribbon — commit it — merges, imports, verifies), then the POML ui-tests | `-Verify` exit 0; the four amendment ui-tests pass. Omit `-SecureTransitionDeployed` only where the BFF lacks task 148 |

**Merge gates (all hold before task 150 reaches master):**
1. **F6: MET.** Rows 1-6 closed by owner round 10 item 9 (c1, §15); rows 7-11 closed by owner round 13 item 10
   (option B, c1-r2, §17). No DRAFT marker remains in code or tests; every string is pinned verbatim.
2. **G-0 in dev first** (r1 item 11): **MET in dev** — `-Verify` PASS on spaarkedev1, 2026-10-03 (G-0 row above). The
   `-Apply` exit 1 seen there was the after-check defect fixed in c1-r2; with the fix, `-Apply` exits 0 after a full
   repair, so the guide §7c order ("each must exit 0") holds.
3. **Task 133 merges first, or together** (r1 item 12): `task/uac-r2-150` is stacked on `task/uac-r2-133-b2-r2`, which
   is not in `work/unified-access-control-r2`. F3 trusts `sprk_createdbyperson`, which is safe only under 133's FLS
   lock on that column, so 150 must never land without 133 (and 133's own live gate G-3 precedes 150's G-4 anyway).

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
