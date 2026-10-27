# Model 1 workforce sign-in: decision and cross-project responsibilities (2026-10-13)

> Status: **Owner-approved 2026-10-13** ("yes r3 end to end", "yes approve A9"). Authority: **ADR-028 Amendment A9** (`.claude/adr/ADR-028-spaarke-auth-architecture.md`). Each project records this in its own notes and its project CLAUDE.md standing directives, then confirms back to R3.

## 1. The decision (owner, 2026-10-13)

| Population | Signs in with | Is in Dataverse | Uses |
|---|---|---|---|
| Licensed core users | B2B guest in the **Spaarke tenant** (A8, unchanged) | `systemuser` | Model-driven apps, Office add-ins, Teams; the browser SPA through a stored link |
| Workforce contacts | Their company account in the **customer's home directory** (A9) | `contact`, created by a profile form at first sign-in, no approval | **Browser SPA only** (no Office add-ins; Teams deferred) |
| Partners | CIAM (`spaarkeextid`, unchanged) | `contact` | Browser SPA |

**Terms.** "Spaarke tenant" = Spaarke's Entra directory; in Model 1 it holds every customer's Dataverse, Azure resources, app registrations and SPE. "Customer's home directory" = the customer's own Entra directory. Never write a bare "tenant" for the second.

**Why.**
- It is Microsoft's documented pattern, including for Teams and Office NAA. A8's cross-tenant guest NAA is undocumented.
- Workforce contacts get no guest "Review permissions" screen and no second MFA prompt.
- Off-boarding follows the customer's home directory, so no stale guests are left behind.
- No stamp identity needs an invite permission.

The cost is one admin consent per customer and Microsoft publisher verification before the first production customer. Research: `notes/2026-10-13-option-a-identity-and-licensing-research.md`.

**Licensing** (multiplexing; External Users relative to Spaarke as licensee) is open, and it is the same under any sign-in model. The owner has not decided how to pursue it.

## 2. Who does what

**Ownership (owner, 2026-10-13): R3 owns workforce-contact sign-in and registration end to end.** That covers the SPA client, the BFF token acceptance, the registration endpoint and the stored link. UAC owns only access, meaning how the baseline role applies to contact callers.

### spaarke-SPA-external-access-platform-r3 (R3): owner of A9

1. **A9 applied** to ADR-028, plus the cross-reference notes in A1/A2/A8, `.claude/adr/INDEX.md` and `.claude/constraints/auth.md`. Merged to master by PR.
2. **BFF (R3 builds; UAC reviews shared files):**
   - workforce token acceptance (`iss` exact, `tid` in the stamp's `CustomerTenantIds`, `aud`, user token, `acct` = 0; keyed on `tid`+`oid`);
   - `registration_required`, then a registration endpoint that creates one contact per (`tid`, `oid`), with no approval and idempotent submits;
   - the **stored link** for licensed users: home (`tid`, `oid`) to `systemuserid`, created when both identities are proven, never by email.
   - The owner signs off on this (auth code).
3. **Task 040, re-planned:**
   - The browser SPA's workforce realm moves to one **multitenant** SPA client, with its authority set to the customer's home directory from Spaarke-owned data (`organizations` only before the customer is known).
   - **Teams** for workforce contacts is deferred to a new DEF item.
   - The A8 single-tenant "Spaarke Workspace" client (`9f1da340`) stays for licensed users' surfaces.
4. **Task 041 (backend selection):** each directory entry gains the customer's home directory id(s). This changes the T240c directory contract, so it is agreed with provisioning.
5. **Task 056 (join page):** becomes the first-sign-in **profile form**, calling R3's registration endpoint.
6. **Tasks 012 (guest member test) and 060 (guest registration service):** withdrawn. Task 060's open owner questions lapse.
7. The dev SPA deploy stays held until the re-planned 040 is built.
8. Open item (owner decision pending): intake submit fails on dev because the business unit's owning team lacks `prvReadsprk_ServiceRequest`. It ties to UAC item 4.

### unified-access-control-r2 (UAC)

**What UAC must change:**
1. **Merge master.** UAC's branch carries ADR-028 A1–A5 only (checked 2026-10-13); A6–A9 never reached it. That is how round 97 was recorded without anyone seeing the A8 conflict.
2. **Correct the round 97 record.** "No B2B guests" applies to **workforce contacts** only; licensed core users stay B2B guests (A8). Cite A9.
3. **Re-scope task 186** to baseline access only. Workforce token acceptance, registration and the stored link move to R3.
4. **Baseline role** "Spaarke Internal Contact User (Workforce)": define how the BFF applies it to contact callers. Include `sprk_servicerequest` Read if the owner chooses that fix for R3's intake failure.

**What UAC allows R3 to change (with UAC review of the PR):**
- workforce token acceptance in `Infrastructure/ExternalAccess/WorkforceIdentityOptions.cs` (`WorkforceMembershipTest`) and `WorkforcePrincipalResolver.cs`, plus their tests;
- the caller-identity binding for home-directory members in `ContactIdentityBinder.cs` (`tid`+`oid` to contact, and the stored link to `systemuser`);
- a new registration endpoint and its service.

**UAC's conditions (UAC reply 2026-10-13, accepted by R3):**
- **`ContactIdentityBinder.cs`:** R3 builds on top of UAC **PR #1801** (task 184) **after it merges**, not alongside it.
  - #1801 changes `ContactIdentityBinder.DecideUnlinkedAsync`, `ContactBindingDecision.cs` and `ContactIdentityStore.cs`, plus E11 bind-intent, `AsOf` stamping and `contact.sprk_externalinvitedon`. UAC task 185 adds an invite-code proof to the same E11 code later.
  - R3's (`tid`, `oid`) path is a **new branch**. It does not reshape E10/E11.
- **Task 141 invariants stay binding** (E10 collisions, oid-first resolution, never clear or re-point a link). The stored link home (`tid`, `oid`) → `systemuserid` is a **new link type in its own field or store**, not in `sprk_externalobjectid` or the plane columns. That keeps "plane marker without oid = Unreadable" true. UAC reviews it as a gate.
- **No email:** word-add-in's review findings (a), (b), (d) and (e) on its shelved `wip/t126-workforce-customer` scheme apply to R3's token acceptance; (c) is superseded by keying on (`tid`, `oid`).
- **No unguarded systemuser lookup:** the oid → systemuser lookup in `MembershipEndpoints.cs` (~:436) must not run for home-directory tokens.
- **Baseline role:** UAC's draft recommends the BFF apply the Workforce role's privileges as a **ceiling**, with record scope from grants and participation, rather than assigning the role directly. On dev the role has Deep Read on matters, projects, documents and invoices, and **no `sprk_servicerequest` privilege** (relevant to R3's intake 403).
- `WorkforceIdentityOptions.cs`, `WorkforcePrincipalResolver.cs` and a new registration endpoint and service: no conflict with in-flight UAC work.
- **Status:** UAC is waiting for its owner to confirm A9 to it directly before it applies its items 1–4. Its design draft `projects/unified-access-control-r2/notes/186-model1-workforce-design.md` is uncommitted and is shared after that confirmation.

**Confirmations received:**
- **word-add-in-r1**, 2026-10-13 (commit `3235e5452`). No code change. Live check: Outlook on the web verified 2026-10-08, desktop Word not yet.
- **customer-provisioning-orchestration-r1**, 2026-10-13 (commit `d14362fcd`). Items 1–4 confirmed, no conflict. Its project CLAUDE.md directive waits for its owner's OK.
  - H3 already creates `AzureADMultipleOrgs` BFF apps; it adds `requestedAccessTokenVersion` = 2, `knownClientApplications` and pre-authorization.
  - The workforce SPA client is a one-time operator app; its id is a platform parameter.
  - The intake gains `customerWorkforceTenantIds`, which H4b writes to both slots. The run hand-off prints the admin-consent URL; consent is a manual gate.
  - Publisher verification is tracked as ISS-026 / #1811. Provisioning accepts task 041's directory field shape.
- **unified-access-control-r2**, 2026-10-13 (commit `15fb8cf6b`, after its owner confirmed A9 directly):
  - merged master (`30f4a6e4e`);
  - round 97 marked SUPERSEDED by A9;
  - task 186 re-scoped to baseline access only (role as a ceiling plus grant/participation scope, fail-closed);
  - A9 and the conditions recorded in UAC's project CLAUDE.md §3;
  - the "allows R3 to change" list accepted on the four conditions, with **each of those PRs sent to UAC for review**.
  - #1801 is owner-approved and auto-merges; UAC tells R3 when it is on master. Design input: `projects/unified-access-control-r2/notes/186-model1-workforce-design.md` (its §5 role-as-ceiling is UAC's).
  - Intake 403: R3 owns the fix. Only Read on `sprk_servicerequest` is needed, for the **owning team** (the create is app-only). It was put to the owner on 2026-10-13 with the recommendation of Basic-level Read in Spaarke Basic User. UAC's 186 references it rather than raising it again.

### spaarkeai-word-add-in-r1

**What the add-in project must change:** nothing in code.
- The Office add-ins serve **licensed core users only**, on A8's Spaarke-tenant B2B sign-in (single-tenant client, NAA then popup).
- Do not adopt `organizations` or the multitenant client for the add-in. A9 forbids it.

**Verify live:**
- A licensed B2B guest signs in to the add-in from **their own home Office or Teams**, with the authority pinned to the Spaarke tenant. Microsoft documents no such case (research §1, "NAA, A8"). If it fails, raise it with the owner before any workaround.
- Teams for licensed users: same check. Teams for workforce contacts is deferred.

**What R3 will change in your area:** nothing.

### customer-provisioning-orchestration-r1

**What provisioning must change:**
1. Each stamp's **BFF API app** becomes **multitenant** (`requestedAccessTokenVersion` = 2). One shared multitenant SPA client is added for workforce contacts.
2. **Customer onboarding:**
   - an admin-consent link (combined via `knownClientApplications`) for the customer's IT;
   - the `CustomerTenantIds` intake (H11).
3. **Publisher verification** for Spaarke's multitenant apps before the first production customer. It needs a Partner Program account and a verified domain.
4. The T240c directory entry gains the customer's home directory id(s), agreed with R3 task 041.

**What R3 will change in your area:** nothing in your code. R3 proposes the directory field shape (task 041) for you to accept.

## 3. Order

1. A9 is applied and merged to master (R3 PR). UAC merges master and corrects round 97.
2. R3 re-plans 040/041/056, withdraws 012/060 and writes the BFF design note (A9-cited) for owner review. UAC defines the baseline role application.
3. R3 builds the BFF token acceptance, registration and stored link, then the SPA client and profile form. UAC reviews the shared files.
4. Dev deploy and live probes (owner approval, commands shown first).
5. Teams for workforce contacts: a later release, after a live NAA test.

## 4. How we avoid another cross-project conflict

1. **One authority.** ADR-028 (with A9) is the rule. A project-local "owner decision" that changes an auth rule is not in force until it is an ADR amendment (CLAUDE.md §6.5 path B).
2. **Check before recording.** Before recording an owner decision about auth, the recording session merges master and checks the decision against ADR-028. On a conflict it outputs the §6.5 "ADR Conflict" block **before** writing the decision down.
3. **Named owners per surface:**

   | Surface | Owner |
   |---|---|
   | SPA client, A9 text, BFF workforce token acceptance, registration, stored link | R3 |
   | Baseline role application (access) | UAC |
   | Add-in sign-in | word-add-in |
   | App registrations, consent, directory | provisioning |

   A change to another project's surface goes to that project as a coordination note, not straight into its code.
4. **Shared vocabulary:** "Spaarke tenant" / "customer's home directory" (A9 Terms).
5. **Possible mechanical guard (proposal; owner decides):** an ArchTest or source scan that fails when code adds an `organizations`/`common` authority outside the A9 SPA client, or accepts a foreign `tid` outside the A9 validation path.
