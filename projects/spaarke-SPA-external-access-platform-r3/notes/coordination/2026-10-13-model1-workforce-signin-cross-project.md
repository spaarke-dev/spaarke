# Model 1 workforce sign-in: decision and cross-project responsibilities (2026-10-13)

> Status: **DRAFT for owner approval**. After approval, each project records this in its own notes and its project CLAUDE.md standing directives. Authority: ADR-028 **A9** once it is applied (draft: `notes/drafts/adr-028-a9-draft.md`).

## 1. The decision (owner, 2026-10-13)

| Population | Signs in with | Is in Dataverse | Uses |
|---|---|---|---|
| Licensed core users | B2B guest in the **Spaarke tenant** (A8, unchanged) | `systemuser` | Model-driven apps, Office add-ins, Teams; the browser SPA through a stored link |
| Workforce contacts | Their company account in the **customer's home directory** (A9, option A) | `contact`, created by a profile form at first sign-in, no approval | **Browser SPA only** (no Office add-ins; Teams deferred) |
| Partners | CIAM (`spaarkeextid`, unchanged) | `contact` | Browser SPA |

**Terms.** "Spaarke tenant" = Spaarke's Entra directory. "Customer's home directory" = the customer's own Entra directory. Never write a bare "tenant" for the second.

**Licensing** (multiplexing; External Users relative to Spaarke as licensee) is open and the same under any sign-in model. The owner has not yet decided how to pursue it.

## 2. Who does what

### spaarke-SPA-external-access-platform-r3 (R3)

1. ADR-028 **A9** text: drafted here; the owner approves; the R3 main session applies it (`.claude/` is main-session only). It also updates the A2/A1/A8 cross-reference notes, `.claude/adr/INDEX.md` and `.claude/constraints/auth.md`.
2. **Task 040, re-planned:** the browser SPA's workforce realm moves to one **multitenant** SPA client, with its authority set to the customer's home directory from Spaarke-owned data (`organizations` only before the customer is known). The **Teams** parts (NAA, Teams package) are deferred to a new DEF item. The A8 single-tenant "Spaarke Workspace" client (`9f1da340`) is kept for licensed users' surfaces or retired; decide in the re-plan.
3. **Task 041 (backend selection):** each directory entry also needs the customer's home directory id(s). This changes the T240c directory contract; agree it with provisioning.
4. **Task 056 (join page):** becomes the first-sign-in **profile form** and calls UAC 186's registration endpoint.
5. **Tasks 012 (guest member test) and 060 (guest registration service):** withdrawn for workforce contacts. Task 060's open owner questions lapse.
6. Keep the held dev SPA deploy until the re-planned 040 is built.
7. Open R3 item (owner decision pending): intake submit fails on dev because the business unit's owning team lacks `prvReadsprk_ServiceRequest`. This ties to UAC's baseline role (§2, UAC item 5).

### unified-access-control-r2 (UAC)

1. **Merge master first.** UAC's branch carries ADR-028 A1-A5 only (checked 2026-10-13); A6-A8 never reached it. That is how round 97 was recorded without seeing the A8 conflict.
2. **Correct the round 97 record:** "no B2B guests" applies to **workforce contacts** only. Licensed core users stay B2B guests (A8).
3. **Task 186 design note:** build to A9's MUST rules:
   - token acceptance (`iss` exact, `tid` in the stamp's list, `aud`, user token, `acct` = 0; keyed on `tid`+`oid`);
   - `registration_required`, then one contact per (`tid`, `oid`), with no approval and idempotent submits;
   - the **stored link** for licensed users: home (`tid`, `oid`) to `systemuserid`, created when both identities are proven, never by email. UAC owns how the proof works.
4. Cite A9 in 186; the owner signs off (auth code).
5. **Baseline role** "Spaarke Internal Contact User (Workforce)": how the BFF applies it to contact callers. Include `sprk_servicerequest` Read if the owner chooses that fix for R3's intake failure.

### spaarkeai-word-add-in-r1

1. The Office add-ins serve **licensed core users only**, on A8's Spaarke-tenant B2B sign-in (single-tenant client, NAA then popup). No change from A9. Do not adopt `organizations` for the add-in.
2. **Verify live:** a licensed B2B guest signing in to the add-in from **their own home Office or Teams** with the authority pinned to the Spaarke tenant. Microsoft documents no such case (research §1, "NAA, A8"). If it fails, raise it with the owner before any workaround.
3. Teams for licensed users: same check. Teams for workforce contacts is deferred (owner).

### customer-provisioning-orchestration-r1 (affected; informed)

1. Each stamp's **BFF API app** becomes **multitenant** (`requestedAccessTokenVersion` = 2), plus one shared multitenant SPA client.
2. **Customer onboarding:** an admin-consent link (combined via `knownClientApplications`) for the customer's IT, plus the `CustomerTenantIds` intake (H11).
3. **Publisher verification** for Spaarke's multitenant apps before the first production customer (needs a Partner Program account and a verified domain).
4. The T240c directory entry gains the customer's home directory id(s), agreed with R3 task 041.

## 3. Order

1. Owner approves A9 and this note.
2. R3 applies A9 to master (PR). UAC merges master.
3. UAC 186 design note (A9-cited) goes to owner review. In parallel, R3 re-plans 040/056 and withdraws 012/060.
4. UAC 186 BFF code, then R3's SPA client and profile form, then the dev deploy and live probes.
5. Teams for workforce contacts: a later release, after a live NAA test.

## 4. How we avoid another cross-project conflict

1. **One authority.** ADR-028 (with A9) is the rule. A project-local "owner decision" that changes an auth rule is not in force until it is an ADR amendment (CLAUDE.md §6.5 path B).
2. **Check before recording.** Before recording an owner decision about auth, the recording session merges master and checks the decision against ADR-028. On a conflict it outputs the §6.5 "ADR Conflict" block **before** writing the decision down.
3. **Named owners per surface:**

   | Surface | Owner |
   |---|---|
   | SPA client and A9 text | R3 |
   | BFF token acceptance, registration, link, baseline role | UAC |
   | Add-in sign-in | word-add-in |
   | App registrations, consent, directory | provisioning |

   A change to another project's surface goes to that project as a coordination note, not straight into its code.
4. **Shared vocabulary:** "Spaarke tenant" / "customer's home directory" (A9 Terms).
5. **Possible mechanical guard (proposal; owner decides):** an ArchTest or source scan that fails when code adds an `organizations`/`common` authority outside the A9 SPA client, or accepts a foreign `tid` outside the A9 validation path.
