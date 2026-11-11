# uac-r2 defer / issue disposition (task 090, 2026-11-11)

Source: `gh issue list --repo spaarke-dev/spaarke --label unified-access-control-r2 --state all --limit 300`, run 2026-11-11 after the closures below (161 issues: **83 open, 78 closed**). Register: [`defer-issues.md`](defer-issues.md); decisions: [`wrap-up-uat-and-decisions-2026-11-11.md`](wrap-up-uat-and-decisions-2026-11-11.md) (owner "Accept all", D-1..D-12).

Rule applied (owner, 2026-09-15): an entry leaves this project only if it is not material, with evidence, or another project's ownership is confirmed. Every hand-off below therefore waits for the owner's **one batch confirmation** (section 2). Until confirmed, a "Carry" or "Hand-off" row is a proposal, not a transfer.

## 0. Actions taken by task 090

| Issue | Action |
|---|---|
| #1059, #1063, #1077, #1104 | **Closed** with a comment (D-1 tracking issues for 136, 140, 153, 165; UAT carried as U-1, U-2, U-4, U-6). #1104 carries no project label |
| #2203 | **Commented** (D-7, roadmap), kept open. No `roadmap` label exists in the repo, so none was added |
| #2245 | **Commented** (D-4, follow-on; design: bind readers to the locked copies), kept open |
| #2270, #2278 | **Commented** (D-6, follow-on secure-family project), kept open. Both are labelled for SPA-r3, not this project |
| #1830, #1014 | **Closed** with a one-line comment after the main session's ruling (#1830: fixed by task 192, deployed, gate passed; #1014: ADR-038 now has a single A2). They were close candidates in section 3 |
| #2329 | **Commented** with two findings: `ExternalCallerContext` appears to have no production consumer (likely dead code), and the Contact `Entity.xml` description still names the deleted `ExternalCallerAuthorizationFilter`. Neither is changed here |
| #2179, #2338 | **Kept in uac-r2 (owner D-13/D-14, 2026-11-11: "Build in uac-r2 now").** Not follow-on hand-offs; neither is in section 2. #2179 has no project label, so it appears only in section 4 |
| #1304 | **Not reopened.** Closed 2026-10-16 as "not planned" with the comment "Folded into #1303. Attestation tests and docs belong with replay itself." The register's 2026-10-16 FUTURE table records the same ("#1303 (absorbs #1304)"). Task 089 stays `[deferred]`; its tracking issue is now #1303 |

Fix-now decisions: #2033 (D-3: refuse the send for a user disabled in Dataverse; the issue has no project label, so it is not in the tables below) and #2250 (D-5, in section 1). Both fixes run outside 090.

## 1. Open issues (83)

| Issue | Title | State | Disposition | Owner project |
|---|---|---|---|---|
| #2590 | A project created app-only with no person lands in the root business unit | open | Carry: ownership defect on the app-only create path (project lands in the root BU). Material to UAC ownership rules | uac follow-on (proposed: uac-secure-project-r3) |
| #2585 | Re-file path reads every column of the row; derive the column set from metadata instead | open | Carry: re-file path reads every column app-only; derive the column set. No exposure (server-internal read) | uac follow-on (proposed: uac-secure-project-r3) |
| #2543 | Flaky under load: ProvisionProject_WhenRecordingTheContainerTimesOut (real-clock 30 s wait t... | open | Known limit: flaky real-clock test under load; production behaviour unaffected | test hygiene, no confirmed owner |
| #2524 | A project filed under a matter by a server path gets no Project-Matter link row (missing fro... | open | Carry: a server-filed project gets no Project-Matter N:N row, so it is missing from the matter subgrid (195 path; found by field-mapping 019) | field-mapping-server-write-path-r1 or uac follow-on |
| #2473 | Semantic Search: saving a search fails for end users (no Create on grid configuration) | open | Hand-off: Semantic Search saved searches need Create on sprk_gridconfiguration (role privilege for a code-page feature) | Semantic Search owner (no confirmed project) |
| #2472 | Playbook organization-wide share reports success but does nothing | open | Hand-off: playbook organization-wide share is a silent no-op (AI line) | AI line (no confirmed project) |
| #2465 | SecureRootInheritance: scope the filed-through-intermediate listing to the asked-for parents... | open | Carry: scale only; SecureRootInheritance scans every filed-through row then filters | uac follow-on (proposed: uac-secure-project-r3) |
| #2444 | RecordOwnershipResolver: expose a reads-agnostic primary-order helper shared with RecordFami... | open | Carry: shared primary-order helper between RecordOwnershipResolver and RecordFamilyReader (duplication) | field-mapping-server-write-path-r1 with uac |
| #2427 | Ownership offer: a row's output lookup is offered as its parent (analysis output file, invoi... | open | Carry: an output lookup can be offered as an ownership parent | uac follow-on (proposed: uac-secure-project-r3) |
| #2423 | Communication send logs the sender's email address at Information | open | Hand-off: CommunicationService logs the sender email at Information (personal data in logs) | communication flow (no confirmed project) |
| #2407 | Current Access does not list team shares | open | Carry: Current Access does not list or remove team shares (enhancement) | uac follow-on |
| #2406 | JIT Office-edit grants are not pruned on a container whose record is no longer Secure | open | Carry: JIT Office-edit grants are not pruned once a container-owning record is unsecured | uac follow-on (proposed: uac-secure-project-r3) |
| #2398 | Platform-role holder check in Set-WizardLinkingAndMembershipPrivileges accepts any applicati... | open | Carry: Set-WizardLinkingAndMembershipPrivileges -Verify accepts any application user (tooling) | uac follow-on |
| #2397 | Core User linking ratchet misses data-driven @odata.bind targets | open | Carry: Core User linking ratchet misses runtime-built @odata.bind targets (guard gap) | uac follow-on |
| #2396 | Report Card wizard cannot save for a Spaarke Core User | open | Carry: Report Card wizard cannot save for a Core User (role privilege) | uac follow-on (roles) |
| #2395 | Child-record refusal detail names the privilege | open | Carry: child-record refusal detail names the privilege (cosmetic) | uac follow-on |
| #2393 | Role 'Spaarke Internal Contact User (Workforce)' exists on dev with no role XML in the repo | open | Open with task 186: the Workforce role exists on dev with no role XML in the repo | uac-r2 task 186 closed under round 90; the role export into SpaarkeMaster is a follow-up (section 4) |
| #2389 | UAC: guard that every caller-scoped Dataverse read stamps the caller, and confirm it live wi... | open | Carry: guard that every caller-scoped Dataverse read stamps the caller | uac follow-on |
| #2381 | K4: An undetermined Signal or Decision Record blocks its root's Unsecure. | open | Known limit (194b K4): an undetermined Signal or Decision Record blocks its root's Unsecure; fails closed | uac follow-on |
| #2380 | K-c: `RecordCreationService` Repair has no live caller. | open | Hand-off (194b K-c): RecordCreationService Repair has no live caller; field-mapping decides | field-mapping-server-write-path-r1 |
| #2379 | K-b: The ordinary-moves time slice does not bound a single root's walk. | open | Known limit (194b K-b): the ordinary-moves time slice does not bound one root's walk | uac follow-on |
| #2378 | K-a: Undetermined rows from bad data block their own root's Unsecure. | open | Known limit (194b K-a): undetermined rows from bad data block their own root's Unsecure; fails closed | uac follow-on |
| #2377 | /invite-and-grant: an internal user's email with no active contact still gets a CIAM invite | open | Open with task 193 (review finding): an internal user's email with no active contact still gets a CIAM invite | uac-r2 (owner D-13/D-14): task 193, build in uac-r2 |
| #2371 | Granting access to an inactive contact: /grant writes a dormant row, the contact-side route ... | open | Carry: owner to choose one rule for granting to an inactive contact (/grant vs contact-side route) | uac follow-on (owner decision) |
| #2369 | read_query and semantic search: base completeness and labels on the caller-visible result se... | open | Open with task 191: read_query and semantic search completeness on the caller-visible set | uac-r2 (owner D-13/D-14): task 191, build in uac-r2 |
| #2368 | Unsecure and ordinary creates fail on tables no default team can read (stored file, procedur... | open | Open with task 194 (194b built fix option (a)); verify against 194b and close or carry | uac-r2 task 194 (open for its post-194b report-only gate) |
| #2362 | Record family entity (sprk_securefamily): deferred to roadmap, with triggers and build-later... | open | Roadmap (owner round 130): sprk_securefamily not built now; issue tracks triggers | roadmap |
| #2348 | sprk_invoicelineitem: add to the Secure family (lineage + role set) when the table is deployed | open | Carry: add sprk_invoicelineitem to the Secure family when the table is deployed (trigger) | uac follow-on, with ebilling-r1 |
| #2343 | Legacy sprk_securitybu: a matter created from a business unit's Matters view cannot be made ... | open | Carry: legacy sprk_securitybu system attribute map blocks Make Secure on matters created from a BU view | uac follow-on (proposed: uac-secure-project-r3) |
| #2341 | Grant rows do not follow an ordinary record moved to another business unit | open | Open with task 194: grant rows do not follow an ordinary record moved to another BU | uac-r2 task 194 (open for its post-194b report-only gate) |
| #2338 | Manage Access: one 'add person' entry; the server shares with a contact's internal user inst... | open | Open with task 193 (its tracking issue): one Add person entry in Manage Access | uac-r2 (owner D-13/D-14): task 193, build in uac-r2 |
| #2337 | Close matter and close work assignment: end every grant, as close-project does for projects | open | Carry: close matter and close work assignment should end every grant, as close-project does | uac follow-on |
| #2332 | BFF application user: reduce the System Administrator role to least privilege | open | Carry: BFF application user holds System Administrator; reduce to least privilege | uac follow-on or spaarke-auth (owner to confirm) |
| #2329 | Stale UAC code comments: grant-table Create privilege, organization standing grant | open | Carry: two stale UAC code comments (comment-only) | uac follow-on |
| #2327 | Invite-and-grant: tell a portal-account invitee when the grant does not go through | open | Carry: tell a portal-account invitee when the grant does not go through (enhancement) | uac follow-on or spaarke-SPA-external-access-platform-r3 |
| #2326 | Re-send the welcome email to a bound external person | open | Carry: re-send the welcome email to a bound external person (enhancement; needs a secured column and the owner's go). Filed by R3 | spaarke-SPA-external-access-platform-r3 |
| #2276 | Set-ChildRecordCreatorPersonSchema secures a pre-existing unsecured sprk_createdbyperson wit... | open | Carry: values of sprk_createdbyperson written before the column was secured are still trusted | uac follow-on (proposed: uac-secure-project-r3) |
| #2267 | SPE: stamp-configured container ids (OwnedContainerIds etc.) are never checked for existence... | open | Carry: stamp-configured container ids are never existence-checked (found by task 192) | uac follow-on |
| #2262 | Playbook writing steps other than Update Record still write as the BFF on a person's run (Cr... | open | Carry: playbook writing steps other than Update Record still write as the BFF on a person's run | uac follow-on with the AI line |
| #2260 | Hardening after #2251: contact email as an access input; route BFF metadata URLs through Dat... | open | Carry: hardening after #2251 (contact email as an access input; metadata URLs through DataverseIdentifier); nothing exploitable today | uac follow-on |
| #2250 | App-only playbook writers can set sprk_assigned* and so cause an Assigned To access grant wi... | open | FIX NOW (owner D-3/D-5 round): refuse sprk_assigned* on the app-only playbook writers (D-5). Separate small fix outside 090 | uac-r2 (D-5 fix in progress) |
| #2245 | BFF-owned document columns that are not field-secured (sprk_driveitemid, sprk_filepath, sprk... | open | Follow-on (owner D-4): bind readers to the locked copies; commented 2026-11-11 | uac follow-on (owner D-4) |
| #2241 | Document profile: BuildSearchProfile looks up unprefixed keys, so AI content never reaches s... | open | Hand-off: BuildSearchProfile looks up unprefixed keys (search quality, AI line) | AI line (no confirmed project) |
| #2238 | RecordCreationService step-9 server binds (owner, creator person, issecure) are not de-dupli... | open | Hand-off: step-9 server binds not de-duplicated case-insensitively; offered to field-mapping 018b | field-mapping-server-write-path-r1 |
| #2227 | sprk_document.sprk_emailmessageid / sprk_isemailarchive are user-writable but drive server-s... | open | Carry: sprk_emailmessageid / sprk_isemailarchive user-writable but drive server linking (same family as D-4 #2245) | uac follow-on (with #2245) |
| #2226 | Scheduled sweep job remarks claim durable run history and a standing /enable, but the schedu... | open | Known limit: sweep job remarks claim durable history; scheduler store is in memory (documentation accuracy) | BFF platform (no confirmed project) |
| #2225 | A communication with several linked .eml archives: four consumers pick one at random (TOP 1,... | open | Carry: several linked .eml archives, four consumers pick one at random | communication flow, with uac |
| #2222 | Archive-by-communication lookups reveal whether a document exists (404 vs 403, unread Archiv... | open | Known limit: archive-by-communication 404 vs 403 reveals existence only, no content | uac follow-on |
| #2217 | Deploy-WebResourceInline.ps1 resolves -FilePath against the process directory, so it can sil... | open | Known limit: Deploy-WebResourceInline.ps1 relative -FilePath; workaround is absolute paths plus read-back hash | tooling (no confirmed project) |
| #2213 | CrossPathLink re-points an archive document already linked to another communication (can un-... | open | Hand-off: CrossPathLink re-points an archive document already linked to another communication | word-add-in / communication flow, with uac review |
| #2207 | [uac-r2] #2161 K2: F3 walk and container derivation disagree on non-secure intermediates (ov... | open | Known limit (#2161 K2): F3 walk and container derivation disagree; over-refusal, never exposure | uac follow-on |
| #2204 | uac-r2 #2159 residual: a late-committing re-point after a definite refusal can leave the row... | open | Known limit: late re-point after a definite refusal; fails closed (404) | uac follow-on |
| #2203 | [uac-r2] sprk_agreement becomes a core record (root, or child of a matter/project): UAC mode... | open | Roadmap (owner D-7); commented 2026-11-11; kept open (no roadmap label exists) | roadmap |
| #2202 | [uac-r2] Relocation pointer write is not row-version conditional: a checkout landing between... | open | Known limit: relocation pointer write not row-version conditional; narrow race toward a refused stale pointer | uac follow-on |
| #2201 | [uac-r2] Office edit-open gives a flagged-external caller writer on the whole secure contain... | open | Open with task 191: Office edit-open gives a flagged-external caller writer on the whole secure container | uac-r2 (owner D-13/D-14): task 191, build in uac-r2 |
| #2196 | uac-r2 operator check: who holds Data Owner on the sdap-jobs Service Bus namespace | open | Operator question: who holds Data Owner on the sdap-jobs Service Bus namespace (not code) | platform operator |
| #2195 | uac-r2 K2: a crash between the re-file write and the F3 release record/enqueue loses the rel... | open | Known limit (K2): crash between re-file write and F3 release record; record stays held | uac follow-on |
| #2194 | uac-r2 K2: relocation copies kept after a pointer write of unknown outcome are only logged | open | Known limit (K2): relocation copies kept after an unknown-outcome pointer write are only logged; unreachable | uac follow-on |
| #2193 | uac-r2 K1: queued F3 release retry does not re-check the release permission at run time | open | Known limit (K1): queued F3 release retry does not re-check permission; runs only after a permitted re-file | uac follow-on |
| #2125 | [ISS-014] spaarke.grid_overview lists internal-only and Private-thread communications to cal... | open | Open with task 191: grid_overview lists internal-only communications to flagged-external callers | uac-r2 (owner D-13/D-14): task 191, build in uac-r2 |
| #2047 | Invoice-line storage: read sprk_invoice as a carrier before any upload route accepts invoice... | open | Carry (latent): invoice-line storage must read sprk_invoice as a carrier before any upload route accepts lines (trigger) | uac follow-on, with ebilling-r1 |
| #2028 | Shared Graph handler retries every non-idempotent POST on 5xx/408/transport faults (duplicat... | open | Known limit: shared Graph handler retries non-idempotent POSTs; SPE create opted out (#2024); none is an access decision | BFF platform (no confirmed project) |
| #1864 | [ISS-010] Internal Only is enforced by BFF reads, not by Dataverse security (licensed users ... | open | Open with task 191 (expected real fix): Internal Only enforced by BFF reads, not Dataverse security | uac-r2 (owner D-13/D-14): task 191, build in uac-r2 |
| #1830 | [ISS-006] Dev: SPE JIT-grant revocation sweep fails every 5 min (Graph 'Invalid hostname for... | open | Done by task 192 (#2268, deployed 1fa0cf5cb8 2026-10-23, server gate PASS) | **closed 2026-11-11** (section 0) |
| #1735 | SPE container create: a caller cancel during the create POST can strand a live, unbound, unm... | open | Create-POST site closed by #2024; orphan sweep FUTURE with #1313 | uac follow-on (orphan sweep); SpeAdmin site to sdap-SPE-admin-app-r2 |
| #1574 | [ISS-017] BFF auth pipeline hardening: agent filter checks no audience; one Admin role unloc... | open | Hand-off (register 2026-10-16): BFF auth pipeline hardening | spaarke-auth-system-of-record-r1 |
| #1564 | [ISS-009] sprk_isexternal has no write path, and Set-ExternalFlagForB2BGuests.ps1 would mark... | open | Hand-off: sprk_isexternal has no write path (routed to uac-r2 plus a future user-admin app); related to task 191 | future user-admin app / uac follow-on (owner to confirm) |
| #1355 | Review request (ontology-platform-r1 D-33/D-36): register sprk_signal and sprk_decisionrecor... | open | Hand-off (register 2026-10-16): register sprk_signal / sprk_decisionrecord as secure children | spaarke-ontology-platform-r1 |
| #1313 | SPE container-binding backfill cannot list containers of a config with no secret name (Model... | open | FUTURE (register 2026-10-16): binding backfill skips a secret-less config; fails closed | uac follow-on (with #1735 orphan sweep) |
| #1307 | Duplicate record-target authorization filters: FinanceAuthorizationFilter vs RecordRouteAcce... | open | FUTURE (register 2026-10-16): two record-target authorization filters; maintainability, both fail closed | uac follow-on or uac-secure-project-r3 |
| #1306 | [uac-r2 deferred] 095 — Document-to-record multi-association via an intersection entity | open | Deferred to a new project (owner round 59): task 095 | new project (unassigned) |
| #1305 | [uac-r2 deferred] 094 — Upload collision: ask before the bytes move (pre-flight probe) + "Us... | open | Deferred to a new project (owner round 59): task 094 | new project (unassigned) |
| #1303 | [uac-r2 deferred] 088 — Evaluator versioning + point-in-time access replay (FR-32 read half) | open | Deferred to a new project (owner round 59): tasks 088 + 089 (absorbs #1304); FR-32 replay known gap | new project (unassigned) |
| #1072 | [uac-r2 deferred] 056 — Contacts cannot see a matter's or work assignment's events, communic... | open | Hand-off (register 2026-10-16): 056 remainder; FR-27 acceptance met | spaarke-SPA-external-access-platform-r3 |
| #1014 | ADR-038 amendment numbering: two different "Amendment A2" sections are about to collide | open | Resolved on master: ADR-038 now has one Amendment A2 (and A3) | **closed 2026-11-11** (section 0) |
| #993 | [ISS-016] SpeAdmin: DataverseWebApiClient receives the container's singleton TokenCredential... | open | Hand-off (register 2026-10-16): SpeAdmin DataverseWebApiClient takes the singleton TokenCredential | sdap-SPE-admin-app-r2 |
| #992 | [ISS-015] Front-end lockfiles: @xmldom/xmldom 0.8.13 (8 HIGH, via mammoth) and dompurify 3.4... | open | FUTURE (register): xmldom / dompurify HIGH CVEs in front-end lockfiles; pre-existing, not access control. Needs an owner soon | front-end dependency hygiene (no confirmed owner) |
| #991 | [ISS-014] Compose: @tiptap/core 2.x carries GHSA-cp6q-959q-f8rh (fixed only in 3.30.4) | open | Hand-off (register): TipTap 2.x advisory; Compose owns the editor | spaarkeai-compose (next round) |
| #989 | JobContract: typed requester field (oid + tid) set only by the BFF from the validated token | open | FUTURE (register): JobContract typed requester; prerequisite only for an impersonating job handler | first project that builds one (no confirmed owner) |
| #988 | Service Bus: Entra-only — disable SAS (local auth), remove the namespace-wide SpaarkeAppAcce... | open | Hand-off (register 2026-10-16): Service Bus Entra-only remainder | customer-provisioning-orchestration-r1 |
| #972 | WorkspaceLayoutService writes a new default layout even when clearing the old defaults faile... | open | Not material (register): workspace layout two defaults | Workspace/dashboard (no confirmed owner) |
| #971 | Singleton ServiceClient: failed calls throw the client-wide LastException, so concurrent req... | open | Not material (register): singleton ServiceClient LastException crosstalk; no access path branches on exception text | BFF platform (no confirmed owner) |
| #962 | [ISS-001] PrivilegeGroupResolver double-counts the first page of Graph group memberships | open | Not material (register): PrivilegeGroupResolver page-1 double count; RAG membership filter only | AI line (no confirmed owner) |

## 2. HAND-OFFS FOR OWNER CONFIRMATION (68)

One batch. For each row, confirm the owner project (or name another). "uac follow-on" means the next access-control project; the owner has named `uac-secure-project-r3` as the secure-family follow-on (D-6), and the other rows propose it or a project named in the row. Rows marked "no confirmed owner" need an owner before they leave uac-r2; that is stated here as the 090 POML requires.

| Issue | Title | Proposed owner |
|---|---|---|
| #2590 | A project created app-only with no person lands in the root business unit | uac follow-on (proposed: uac-secure-project-r3) |
| #2585 | Re-file path reads every column of the row; derive the column set from metadata instead | uac follow-on (proposed: uac-secure-project-r3) |
| #2543 | Flaky under load: ProvisionProject_WhenRecordingTheContainerTimesOut (real-clock 30 s wait t... | test hygiene, no confirmed owner |
| #2524 | A project filed under a matter by a server path gets no Project-Matter link row (missing fro... | field-mapping-server-write-path-r1 or uac follow-on |
| #2473 | Semantic Search: saving a search fails for end users (no Create on grid configuration) | Semantic Search owner (no confirmed project) |
| #2472 | Playbook organization-wide share reports success but does nothing | AI line (no confirmed project) |
| #2465 | SecureRootInheritance: scope the filed-through-intermediate listing to the asked-for parents... | uac follow-on (proposed: uac-secure-project-r3) |
| #2444 | RecordOwnershipResolver: expose a reads-agnostic primary-order helper shared with RecordFami... | field-mapping-server-write-path-r1 with uac |
| #2427 | Ownership offer: a row's output lookup is offered as its parent (analysis output file, invoi... | uac follow-on (proposed: uac-secure-project-r3) |
| #2423 | Communication send logs the sender's email address at Information | communication flow (no confirmed project) |
| #2407 | Current Access does not list team shares | uac follow-on |
| #2406 | JIT Office-edit grants are not pruned on a container whose record is no longer Secure | uac follow-on (proposed: uac-secure-project-r3) |
| #2398 | Platform-role holder check in Set-WizardLinkingAndMembershipPrivileges accepts any applicati... | uac follow-on |
| #2397 | Core User linking ratchet misses data-driven @odata.bind targets | uac follow-on |
| #2396 | Report Card wizard cannot save for a Spaarke Core User | uac follow-on (roles) |
| #2395 | Child-record refusal detail names the privilege | uac follow-on |
| #2389 | UAC: guard that every caller-scoped Dataverse read stamps the caller, and confirm it live wi... | uac follow-on |
| #2381 | K4: An undetermined Signal or Decision Record blocks its root's Unsecure. | uac follow-on |
| #2380 | K-c: `RecordCreationService` Repair has no live caller. | field-mapping-server-write-path-r1 |
| #2379 | K-b: The ordinary-moves time slice does not bound a single root's walk. | uac follow-on |
| #2378 | K-a: Undetermined rows from bad data block their own root's Unsecure. | uac follow-on |
| #2371 | Granting access to an inactive contact: /grant writes a dormant row, the contact-side route ... | uac follow-on (owner decision) |
| #2348 | sprk_invoicelineitem: add to the Secure family (lineage + role set) when the table is deployed | uac follow-on, with ebilling-r1 |
| #2343 | Legacy sprk_securitybu: a matter created from a business unit's Matters view cannot be made ... | uac follow-on (proposed: uac-secure-project-r3) |
| #2337 | Close matter and close work assignment: end every grant, as close-project does for projects | uac follow-on |
| #2332 | BFF application user: reduce the System Administrator role to least privilege | uac follow-on or spaarke-auth (owner to confirm) |
| #2329 | Stale UAC code comments: grant-table Create privilege, organization standing grant | uac follow-on |
| #2327 | Invite-and-grant: tell a portal-account invitee when the grant does not go through | uac follow-on or spaarke-SPA-external-access-platform-r3 |
| #2326 | Re-send the welcome email to a bound external person | spaarke-SPA-external-access-platform-r3 |
| #2276 | Set-ChildRecordCreatorPersonSchema secures a pre-existing unsecured sprk_createdbyperson wit... | uac follow-on (proposed: uac-secure-project-r3) |
| #2267 | SPE: stamp-configured container ids (OwnedContainerIds etc.) are never checked for existence... | uac follow-on |
| #2262 | Playbook writing steps other than Update Record still write as the BFF on a person's run (Cr... | uac follow-on with the AI line |
| #2260 | Hardening after #2251: contact email as an access input; route BFF metadata URLs through Dat... | uac follow-on |
| #2241 | Document profile: BuildSearchProfile looks up unprefixed keys, so AI content never reaches s... | AI line (no confirmed project) |
| #2238 | RecordCreationService step-9 server binds (owner, creator person, issecure) are not de-dupli... | field-mapping-server-write-path-r1 |
| #2227 | sprk_document.sprk_emailmessageid / sprk_isemailarchive are user-writable but drive server-s... | uac follow-on (with #2245) |
| #2226 | Scheduled sweep job remarks claim durable run history and a standing /enable, but the schedu... | BFF platform (no confirmed project) |
| #2225 | A communication with several linked .eml archives: four consumers pick one at random (TOP 1,... | communication flow, with uac |
| #2222 | Archive-by-communication lookups reveal whether a document exists (404 vs 403, unread Archiv... | uac follow-on |
| #2217 | Deploy-WebResourceInline.ps1 resolves -FilePath against the process directory, so it can sil... | tooling (no confirmed project) |
| #2213 | CrossPathLink re-points an archive document already linked to another communication (can un-... | word-add-in / communication flow, with uac review |
| #2207 | [uac-r2] #2161 K2: F3 walk and container derivation disagree on non-secure intermediates (ov... | uac follow-on |
| #2204 | uac-r2 #2159 residual: a late-committing re-point after a definite refusal can leave the row... | uac follow-on |
| #2202 | [uac-r2] Relocation pointer write is not row-version conditional: a checkout landing between... | uac follow-on |
| #2196 | uac-r2 operator check: who holds Data Owner on the sdap-jobs Service Bus namespace | platform operator |
| #2195 | uac-r2 K2: a crash between the re-file write and the F3 release record/enqueue loses the rel... | uac follow-on |
| #2194 | uac-r2 K2: relocation copies kept after a pointer write of unknown outcome are only logged | uac follow-on |
| #2193 | uac-r2 K1: queued F3 release retry does not re-check the release permission at run time | uac follow-on |
| #2047 | Invoice-line storage: read sprk_invoice as a carrier before any upload route accepts invoice... | uac follow-on, with ebilling-r1 |
| #2028 | Shared Graph handler retries every non-idempotent POST on 5xx/408/transport faults (duplicat... | BFF platform (no confirmed project) |
| #1735 | SPE container create: a caller cancel during the create POST can strand a live, unbound, unm... | uac follow-on (orphan sweep); SpeAdmin site to sdap-SPE-admin-app-r2 |
| #1574 | [ISS-017] BFF auth pipeline hardening: agent filter checks no audience; one Admin role unloc... | spaarke-auth-system-of-record-r1 |
| #1564 | [ISS-009] sprk_isexternal has no write path, and Set-ExternalFlagForB2BGuests.ps1 would mark... | future user-admin app / uac follow-on (owner to confirm) |
| #1355 | Review request (ontology-platform-r1 D-33/D-36): register sprk_signal and sprk_decisionrecor... | spaarke-ontology-platform-r1 |
| #1313 | SPE container-binding backfill cannot list containers of a config with no secret name (Model... | uac follow-on (with #1735 orphan sweep) |
| #1307 | Duplicate record-target authorization filters: FinanceAuthorizationFilter vs RecordRouteAcce... | uac follow-on or uac-secure-project-r3 |
| #1306 | [uac-r2 deferred] 095 — Document-to-record multi-association via an intersection entity | new project (unassigned) |
| #1305 | [uac-r2 deferred] 094 — Upload collision: ask before the bytes move (pre-flight probe) + "Us... | new project (unassigned) |
| #1303 | [uac-r2 deferred] 088 — Evaluator versioning + point-in-time access replay (FR-32 read half) | new project (unassigned) |
| #1072 | [uac-r2 deferred] 056 — Contacts cannot see a matter's or work assignment's events, communic... | spaarke-SPA-external-access-platform-r3 |
| #993 | [ISS-016] SpeAdmin: DataverseWebApiClient receives the container's singleton TokenCredential... | sdap-SPE-admin-app-r2 |
| #992 | [ISS-015] Front-end lockfiles: @xmldom/xmldom 0.8.13 (8 HIGH, via mammoth) and dompurify 3.4... | front-end dependency hygiene (no confirmed owner) |
| #991 | [ISS-014] Compose: @tiptap/core 2.x carries GHSA-cp6q-959q-f8rh (fixed only in 3.30.4) | spaarkeai-compose (next round) |
| #989 | JobContract: typed requester field (oid + tid) set only by the BFF from the validated token | first project that builds one (no confirmed owner) |
| #988 | Service Bus: Entra-only — disable SAS (local auth), remove the namespace-wide SpaarkeAppAcce... | customer-provisioning-orchestration-r1 |
| #972 | WorkspaceLayoutService writes a new default layout even when clearing the old defaults faile... | Workspace/dashboard (no confirmed owner) |
| #971 | Singleton ServiceClient: failed calls throw the client-wide LastException, so concurrent req... | BFF platform (no confirmed owner) |
| #962 | [ISS-001] PrivilegeGroupResolver double-counts the first page of Graph group memberships | AI line (no confirmed owner) |

## 3. Close candidates (2): both closed 2026-11-11

Evidence says done. 090 first left them open because its brief limited closures to the D-1 set; the main session then ruled to close both, and they were closed with a one-line comment (section 0).

| Issue | Title | Evidence |
|---|---|---|
| #1830 | [ISS-006] Dev: SPE JIT-grant revocation sweep fails every 5 min (Graph 'Invalid hostname for... | Done by task 192 (#2268, deployed 1fa0cf5cb8 2026-10-23, server gate PASS); issue still open |
| #1014 | ADR-038 amendment numbering: two different "Amendment A2" sections are about to collide | Resolved on master: ADR-038 now has one Amendment A2 (and A3); issue still open |

## 4. Issues open under tasks outside the D-1 set

Updated after the main session's rulings (2026-11-11):

- **186** closed under round 90 (merged #2061, deployed, server gate 2026-10-20 checks 1, 2, 4, 5, 7). Its NOT-RUN checks 3 and 6 are owner UAT **U-12**. #2393 (the Workforce role has no role XML in the repo) stays open: including the role in SpaarkeMaster needs the role exported into the solution, so its `package-scope.json` exclusion stays until then (follow-up).
- **191** (#2179, #2369, #2201, #2125, #1864) and **193** (#2338, #2377) stay in uac-r2: the owner answered **D-13** and **D-14** on 2026-11-11, "Build in uac-r2 now" (`wrap-up-uat-and-decisions-2026-11-11.md`). They are not follow-on hand-offs and are not in section 2. #2179 carries no project label, so it has no row in section 1.
- **194** (#2368, #2341): 194 closed under round 90 on 2026-11-11 after its post-194b gate passed (`task-194-ordinary-parent-move.md` §"Post-194b gate (2026-11-11)"). These issues stay open for their own disposition.

## 5. Closed issues (78)

| Issue | Title | State | Disposition | Owner project |
|---|---|---|---|---|
| #2669 | uac-r2 188: a document hidden by the narrowing's chain bound is withheld without truncated (... | closed | Closed before wrap-up (reason on the issue) | - |
| #2401 | Event type lookup is empty for Core Users homed in a sibling child business unit | closed | Closed before wrap-up (reason on the issue) | - |
| #2339 | Ordinary parent reassigned to a new business unit: its children stay behind (rule I-6 drift) | closed | Closed before wrap-up (reason on the issue) | - |
| #2320 | Access-record cascade: readability proof does not see team-held roles or profiles | closed | Closed before wrap-up (reason on the issue) | - |
| #2292 | Membership junction (sprk_userentityassociation) never populated: alternate-key miss misclas... | closed | Closed before wrap-up (reason on the issue) | - |
| #2273 | Secure-exit trusts sprk_createdbyperson on tables the BFF does not stamp (unsecured column c... | closed | Closed before wrap-up (reason on the issue) | - |
| #2263 | DataverseUpdateHandler 'Optimistic' mode sends versionnumber in the body, not If-Match: conc... | closed | Closed before wrap-up (reason on the issue) | - |
| #2259 | Playbook output mapping throws on any plain string or number value (invoice mapping never wr... | closed | Closed before wrap-up (reason on the issue) | - |
| #2257 | Any internal user can author and run an app-only Update Record node / output mapping (writes... | closed | Closed before wrap-up (reason on the issue) | - |
| #2251 | App-only playbook writers can update access-grant, membership, no-access and secure-root col... | closed | Closed before wrap-up (reason on the issue) | - |
| #2247 | Generic app-only update writers do not refuse ownerid / sprk_createdbyperson | closed | Closed before wrap-up (reason on the issue) | - |
| #2246 | ArchiveExistingAttachmentsAsync copies an attachment row's unverified graph item id into a n... | closed | Closed before wrap-up (reason on the issue) | - |
| #2242 | InvoiceExtractionJobHandler writes invoice extraction status via the sprk_document update, s... | closed | Closed before wrap-up (reason on the issue) | - |
| #2240 | RefuseAppOnlyWrite does not refuse BFF-only document pointer columns (sprk_graphitemid/bound... | closed | Closed before wrap-up (reason on the issue) | - |
| #2237 | DocumentProfileOutputMapper writes any AI-output key to sprk_document app-only, bypassing Re... | closed | Closed before wrap-up (reason on the issue) | - |
| #2229 | Spe.Integration.Tests: 9 Analysis/SemanticSearch integration tests fail on master with 403 (... | closed | Closed before wrap-up (reason on the issue) | - |
| #2206 | [uac-r2] Stamped sprk_ai_search_index is not re-derived when a record's owner changes (make-... | closed | Closed before wrap-up (reason on the issue) | - |
| #2127 | [ISS-016] RAG retrieval and chat archive ingest return internal-only email content to caller... | closed | Closed before wrap-up (reason on the issue) | - |
| #2049 | #2002 fix missed its source: CoreAncestorStampReconciliationJob still resolves with Write in... | closed | Closed before wrap-up (reason on the issue) | - |
| #2027 | Customer environments: Dataverse org auditing (isauditenabled + retention) is never set by p... | closed | Closed before wrap-up (reason on the issue) | - |
| #2002 | CoreAncestorResolver: 'carries no core-ancestor stamp' Warning fires on every read-only reco... | closed | Closed before wrap-up (reason on the issue) | - |
| #1755 | Secure-record provisioning: a client disconnect after the container is created leaves it unr... | closed | Closed before wrap-up (reason on the issue) | - |
| #1620 | uac-r2: /unshare-user cannot remove the record owner's own share (app-only RevokeAccess, 0x8... | closed | Closed before wrap-up (reason on the issue) | - |
| #1576 | [ISS-018] Authorization consistency: three oid->systemuser resolvers (disabled users), own-f... | closed | Closed before wrap-up (reason on the issue) | - |
| #1567 | [ISS-012] ADR-028 A5 not in force: SPA/Teams system-user record set uses the ADR-034 column ... | closed | Closed before wrap-up (reason on the issue) | - |
| #1563 | [ISS-008] Member test refuses Model 1 guests without a systemuser; licence-free customer sta... | closed | Closed before wrap-up (reason on the issue) | - |
| #1364 | [uac-r2 -> provisioning] Hand-off: how a new environment gets sprk_noaccessentry (blocks pro... | closed | Closed before wrap-up (reason on the issue) | - |
| #1363 | [uac-r2] Demo self-registration SPE grant: marker keyed to the demo Dataverse, and expiry le... | closed | Closed before wrap-up (reason on the issue) | - |
| #1304 | [uac-r2 deferred] 089 — Attestation seam tests + architecture docs (FR-32 closure) | closed | Closed 2026-10-16 as not planned: folded into #1303 (attestation belongs with replay). Deliberate, recorded in defer-issues.md; NOT reopened | - |
| #1080 | UAC-r2 155: uploads to child records (to-do/event/contact) get a misleading 409 and must fol... | closed | Closed before wrap-up (reason on the issue) | - |
| #1078 | UAC-r2 154: No Access (ethical wall) management on Organization and Contact forms, usable en... | closed | Closed before wrap-up (reason on the issue) | - |
| #1077 | UAC-r2 153: red text-only access-status banner (Secure / No Access) on record forms + 'Secur... | closed | Closed 2026-11-11 by task 090 (D-1; task 153, UAT U-4) | - |
| #1074 | UAC C9: SPA/Teams grants flat Read/Write/Create on BU-owned roots, ignoring the caller's Dat... | closed | Closed before wrap-up (reason on the issue) | - |
| #1073 | Daily Briefing and membership-driven notifications fan out via owner teams/BU; High Priority... | closed | Closed before wrap-up (reason on the issue) | - |
| #1071 | Secure isolation: once children are owned by the memberless Secure team, users shared on the... | closed | Closed before wrap-up (reason on the issue) | - |
| #1070 | Secure isolation: existing children are never re-owned when a record becomes (or stops being... | closed | Closed before wrap-up (reason on the issue) | - |
| #1069 | Secure isolation: children created in the browser (Xrm.WebApi) on a secure record stay owned... | closed | Closed before wrap-up (reason on the issue) | - |
| #1068 | RecordContainerResolver reads an unrecognised entity name as 'not securable' — harden it to ... | closed | Closed before wrap-up (reason on the issue) | - |
| #1067 | sprk_issecure is writable by any Write-holder — lock it with field-level security so only th... | closed | Closed before wrap-up (reason on the issue) | - |
| #1066 | UAC C9: the No Access List binds internal users nowhere once the systemuser plane follows Da... | closed | Closed before wrap-up (reason on the issue) | - |
| #1065 | UAC C9: 'Assigned To *' access disappears when the systemuser plane becomes Dataverse's answ... | closed | Closed before wrap-up (reason on the issue) | - |
| #1064 | C7: Workforce identity binding by oid (Type-2 users get 403 in dev) and the missing systemus... | closed | Closed before wrap-up (reason on the issue) | - |
| #1063 | UAC: contacts with Collaborate/Full Access cannot grant access; external SPA Invite User but... | closed | Closed 2026-11-11 by task 090 (D-1; task 140, UAT U-2) | - |
| #1062 | UAC C4: /grant and /invite-and-grant mint levels above the grantor's own; POA levels contrad... | closed | Closed before wrap-up (reason on the issue) | - |
| #1061 | UAC: Limited access permission does nothing; Restricted not enforced at grant-write time; co... | closed | Closed before wrap-up (reason on the issue) | - |
| #1060 | Soft revocation: deactivating a contact or root record revokes nothing, and grant-cache inva... | closed | Closed before wrap-up (reason on the issue) | - |
| #1059 | External read gates test key presence, not rights — None-rights roots still pass reads inclu... | closed | Closed 2026-11-11 by task 090 (D-1; task 136, UAT U-1) | - |
| #1058 | CIAM plane applies none of the FR-21/22/23 vetoes — Restricted, No Access List and Secure or... | closed | Closed before wrap-up (reason on the issue) | - |
| #1057 | External module /fetch and /record have no column allow-list — SPE pointers and internal/FLS... | closed | Closed before wrap-up (reason on the issue) | - |
| #1056 | Access caches store fault-derived results (empty grants / None snapshots / blank identity) a... | closed | Closed before wrap-up (reason on the issue) | - |
| #1055 | External access: grant cache drops DirectAccessLevel, so a direct grant on a secure root rea... | closed | Closed before wrap-up (reason on the issue) | - |
| #1054 | Secure provisioning: a creator-share failure after the owner move locks the creator out, and... | closed | Closed before wrap-up (reason on the issue) | - |
| #1053 | Finance recalculate is an auth-only IDOR (app-only read+write of any matter/project); financ... | closed | Closed before wrap-up (reason on the issue) | - |
| #1015 | RouteAuthorizationGuardTests: the whole Office route surface is ungoverned, and the FilterMa... | closed | Closed before wrap-up (reason on the issue) | - |
| #1011 | Membership resolver cannot bind a team-owned Owner column — team-owned records resolve to no... | closed | Closed before wrap-up (reason on the issue) | - |
| #1010 | Manage Access modal shows no existing grants for non-root users, and a failed read renders a... | closed | Closed before wrap-up (reason on the issue) | - |
| #1009 | Task-status drift check: the index-row parser keeps the wrong line for eight task ids (ISS-029) | closed | Closed before wrap-up (reason on the issue) | - |
| #1008 | External grant: a 409 saying the re-grant did not take effect has ALREADY written the access... | closed | Closed before wrap-up (reason on the issue) | - |
| #1007 | Create To Do wizard silently DISCARDS uploaded files (ISS-027) | closed | Closed before wrap-up (reason on the issue) | - |
| #1006 | External access: a DEACTIVATED organization still confers access (ISS-026) | closed | Closed before wrap-up (reason on the issue) | - |
| #1004 | Tooling: the task-status drift check cannot see an index row that has no POML (ISS-025) | closed | Closed before wrap-up (reason on the issue) | - |
| #1003 | External access: an external *licensed* system user is refused as "not internal" (ISS-024) | closed | Closed before wrap-up (reason on the issue) | - |
| #1002 | External access: a duplicate collapse can lower the effective access LEVEL (ISS-023) | closed | Closed before wrap-up (reason on the issue) | - |
| #1001 | External access: a stale snapshot of grant rows can SHORTEN access on the duplicate collapse... | closed | Closed before wrap-up (reason on the issue) | - |
| #1000 | External access: org-baseline read is an unbounded N+1 on the authorization hot path (ISS-021) | closed | Closed before wrap-up (reason on the issue) | - |
| #999 | External access: org-membership read bounds on statecode only, so a date-ended membership st... | closed | Closed before wrap-up (reason on the issue) | - |
| #998 | External access: a junction query failure silently removes the deny veto's ORGANISATION axis... | closed | Closed before wrap-up (reason on the issue) | - |
| #995 | UnsecureProjectEndpoint cannot detect a failed share read — it reports '0 shares revoked' as... | closed | Closed before wrap-up (reason on the issue) | - |
| #994 | PlaybookSharingService reads AccessRights bit 524288 as Share — that bit is Assign | closed | Closed before wrap-up (reason on the issue) | - |
| #990 | DataverseImpersonation: fail closed in the shared helper and prefer CallerObjectId (oid) | closed | Closed before wrap-up (reason on the issue) | - |
| #987 | H9 BFF deploy: set the scheduled-jobs slot guard on the staging slot (ADR-036 A1 rule 2) | closed | Closed before wrap-up (reason on the issue) | - |
| #974 | sprk_externalrecordaccess rows created outside the BFF can still have no expiry -- FR-33 hol... | closed | Closed before wrap-up (reason on the issue) | - |
| #973 | Re-grant over an EXPIRED survivor returns 409 without looking at live duplicates on the same... | closed | Closed before wrap-up (reason on the issue) | - |
| #970 | IGenericEntityService.BulkUpdateAsync claims transactional behaviour that ExecuteMultiple do... | closed | Closed before wrap-up (reason on the issue) | - |
| #968 | uac-r2 ISS-004: org-revoke N+1 on container permissions is amplified by task 024's paging | closed | Closed before wrap-up (reason on the issue) | - |
| #964 | Workforce caller cannot PATCH a to-do parented to their own service request (over-denial) | closed | Closed before wrap-up (reason on the issue) | - |
| #963 | External data plane silently truncates at $top=200 with no @odata.nextLink handling (4 call ... | closed | Closed before wrap-up (reason on the issue) | - |
| #961 | [DEF-001] External grant expiry becomes mandatory, bounded and renewable (spec FR-33) | closed | Closed before wrap-up (reason on the issue) | - |
