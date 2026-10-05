# UAC-r2 batch 5: scope review under owner round 56

**Date:** 2026-10-05 · **Mode:** read-only, nothing edited or committed
**Sources:** POMLs and notes on `work/unified-access-control-r2` (`6532bb494`); code on `integ/uac-r2-batch4` (head moved during the review, `1937880dd` → `b4be59c93`); `origin/master`; two read-only live Dataverse queries, run for 047 and 101.
**The bar (owner round 56):** fix (a) runtime defects, (b) maintainability defects that compound, and (c) measurable performance problems. Anything new must answer a realistic failure, and a fix is never bigger than the problem.

## Task list check

- TASK-INDEX has **31** open rows outside 132–169. All 31 are in the list I was given.
- The list I was given has **34** tasks. The 3 extra are **136, 153 and 154**, which fall inside the 132–169 range. I reviewed them because they were named.
- Owner round 56 says "29 tasks". That is the batch-5 list in `current-task.md:175` (27 tasks) plus 170 and 090.
- The other 5 tasks (003, 013, 037, 039 and 136) are the "owner manual gates still open" in `current-task.md:176`. None of them has code work left.

## Summary table

| Task | (1) What it delivers | (2) Real-world failure if dropped | (3) Status in current code | (4) Size | (5) Owner gate | (6) Rec. | Reasoning against round 56 |
|---|---|---|---|---|---|---|---|
| 003 | A user who can read a matter can load its finance summary. | None beyond 130. | **Already met** by 130 (`7c5c4e8a0`, in integ and in the dev BFF `5e1fdcc0b`). The summary route checks the matter (`FinanceEndpoints.cs:81-82`). Only the live check is open. | S | yes (130's finance live check) | **MERGE → 130's live gate**, then close | No defect remains. A separate re-check would be ceremony. |
| 013 | An unlicensed employee is matched to their contact by Entra ID, so a matching email cannot take over a contact. | None beyond 141. | **Already met** by 141's single `ContactBindingDecision`. Only criterion 6 is open, and it is 141's G-8. | S | yes (141 G-7/G-8, plus the G-1b decision) | **MERGE → 141 G-8**, then close | The code is done. Verifying the same thing twice is ceremony. |
| 037 | Restricted records, and Secure records reached only through an organisation's grant, are invisible to external users on both sign-in types. | None in code. | **Already met** by 135 (`b477497aa`) and 136. Only the CIAM live check (criterion 5) is open. | S | yes (CIAM session) | **MERGE → 136's combined CIAM session** | Security behaviour deserves one real check, but not its own task. |
| 039 | A contact on a record's No Access List loses access, even with Full Access and a CIAM token. | None in code. | **Already met** by 135 and 136. Only the CIAM live check is open. | S | yes (CIAM session) | **MERGE → 136's combined CIAM session** | Same as 037. |
| 136 | External users list, open or download a project's documents only when they actually hold Read on it. | (Already fixed.) Without it, a Secure project reached only through an org grant would stream documents to a contact who holds nothing on it. | **Code met and on master.** `700763023` is an ancestor of both `origin/master` and integ, and dev runs it (`AccessibleRecordSetService.cs:664/1735/2044`, `HoldsReadOnProject` on 7 routes, `/me` reads `ReadableProjects`). The POML still says `pending`. | S | yes (one CIAM session, with live grant and No Access writes; no deploy) | **KEEP (gate only)**, as owner of the combined CIAM session that closes 135/136/037/039 | Real security behaviour, proven by tests. One live check is proportionate; build nothing more. |
| 082 | A written decision on whether four "who is calling" helpers should be merged. | None. | **Superseded.** PR #840's `CallerIdentityGuardTests.cs` (Rule 1 `:89`, Rule 2 `:121`, allowlist `:59-80`) is the ratchet. The four helpers have distinct jobs. | S | no | **CUT** (one paragraph in 090's closing note) | A second guard over the same code is a duplicate mechanism. **GUARD/TEST-MACHINERY.** |
| 170 | Renames `FinanceAuthorizationFilter` to `RecordTargetAuthorizationFilter`. | Cosmetic. Deny logs on AI, insights and scorecard routes say "Finance authorization DENIED" (`FinanceAuthorizationFilter.cs:325`). | Not done. About 20 src files, 7 test files and 3 docs would change. `RouteAuthorizationGuardTests` matches filters by name, so a missed rename would make routes look ungated. | M (S for the log text only) | no | **CUT** (optionally fix the log text while that file is already being edited) | Churn plus guard risk, and it fixes nothing. It would also sharpen the duplicate-filter problem (side finding 1). |
| 090 | Formal close: issue register, test-diet, stale comments, README marked Complete. | Material issues go unowned, and comments keep contradicting the code. | Not started. **H-8b is already met** (`WebRoleRemoved` removed by 017). **H-8a is not met**: 6 stale `ExternalCallerAuthorizationFilter` comments remain (`ExternalAccessEndpoints.cs:52`, `ExternalUserContextEndpoint.cs:14,18`, `CallerPrincipalAuthorizationFilter.cs:9`, `ExternalCallerContext.cs:7`, `ExternalDataService.cs:16`). Its deps list is stale. | M | yes (owner confirms issue hand-offs; live gates recorded or carried forward) | **KEEP, trimmed** | Register disposition and comment fixes are class (b). Trim the ceremony: short lessons list, no 9-criteria map, no publish-size run for a comment-only diff, drop step 4. |
| 036 | Dataverse, not the BFF's guesswork, decides which records an internal user in Teams or the external app sees, and what they may do on each. | **Security.** Every reachable record gets fixed Read, Write and Create rights (`AccessibleRecordSetService.cs:1646-1647`, `:482`). A user with only Read in Dataverse can still create to-dos, upload documents and create events. Business-unit pattern matching over-grants, and records shared with the user are missing (C9). | **Not met.** `IImpersonatedRootSetSource` is registered (`ExternalAccessModule.cs:266`) but nothing uses it. There is no flag. The comment at `:1574-1576` says "036 is still open". Blockers are cleared: 034's canary passed 2026-10-03, and 104 is done. | L (M with the cheaper rights shape) | yes (ADR-034 amendment §6.5 path B; live gate (i)–(v) after batch 4 deploys) | **KEEP** | Class (a), owner-mandated (C9/D1). Cheaper shape: the impersonated set gives Read, and one `CallerRecordAccessProbe` check runs at the write route. Reuse the existing `Capped`/`CapCeiling` (`:246-251`) for truncation. Put the runbook in the customer deployment guide. |
| 105 | External-app lists (documents, to-dos, events, contacts) show every row, or say plainly that the list was cut short. | A project with more than 200 documents silently shows only the newest 200, and the user cannot tell. | **Not met.** `GetCollectionAsync` (`ExternalDataService.cs:1143-1169`) never follows `nextLink`. `$top=200` is hard-coded at `:217/587/606/1133`, and the SPA also asks for 200 (`web-api-client.ts:400`). The "authorization-adjacent" premise is false: the read feeds display only. | S–M | no | **KEEP, slimmed** | Class (a): silently incomplete data. Follow pages up to a cap, add an optional `truncated` field and a small banner. The dependency on 036 is artificial; this can run now. |
| 054 | Workforce requesters could edit to-dos on their own service requests through the external route. | None. No SPA screen exists, dev has 0 service requests, and the model-driven app still works under Dataverse security. | **Not met, and nothing can trigger it.** The root set is project, matter and work assignment only (`AccessibleRecordSetService.cs:369-370`), and the service-requests module returns nothing to CIAM. The D-4 question was never answered. | — | decision only (close ISS-003 / #964) | **CUT** (known limit, class (e)) | A rare edge that already fails closed. Building it would be new machinery with no consumer. |
| 055 | Child records take the access of the project, matter or work assignment they belong to. | Essentially none: this already works through the core-ancestor stamp. | **Superseded.** `GetTodoRootAsync` reads the stamp (`ExternalDataService.cs:488-523`), and the documents and invoices modules scope by parent. The one gap is a No Access entry aimed at a single child; 154 only offers root types, so no realistic entry can target one. | — | no | **CUT** (per-child block becomes a known limit) | An evaluator term would duplicate the stamp, which is a duplicate mechanism. |
| 056 | New routes so contacts can work with a matter's or work assignment's events, communications, memos, analyses and documents. | No user can hit anything today: the external app has no screens for these, and the Documents widget is a read-only grid. | **Not met.** Modules: `ExternalAccessModule.cs:320-470`. Events and upload exist only under projects (`ExternalProjectDataEndpoints.cs:106-222`). | L | yes (product decision: the owner called contact coverage "CRITICAL" in C10) | **DEFER-TO-NEW-PROJECT** (with the SPA screens that would use it) | A new feature with no consumer, not a defect. |
| 057 | End-to-end tests for child inheritance at the external data seam. | None on its own. | Not met. It has no subject once 055 is cut and 056 deferred. The stamp path is already tested (029). | — | no | **CUT** (its scenarios go with 056) | Machinery with nothing new to guard. **GUARD/TEST-MACHINERY.** |
| 058 | The architecture doc explains parents vs children, how inheritance works, and that a matter does not inherit from its project. | Minor maintainability gap. `uac-access-control.md` has no child or stamp section, and the No Access schema doc overstates per-child blocking. | Not met. It was gated on 056, and that gate no longer applies. | S | no | **MERGE → 090** (docs pass) | Class (b), but too small for its own task. |
| 111 | Batches the one-read-per-organisation loop on the contact authorization path. | No measurable impact. The loop runs only on workforce contact-only sign-ins (never CIAM, `:1252`, never internal users), over realistically one membership; dev has 2 memberships in total. | Not met, but negligible (`AccessibleRecordSetService.cs:1919-1950`). Task 132's cache work did not touch it. | — | no | **CUT** (known limit) | Fails class (c): no real, measurable impact. |
| 064 | One server read that answers "is this record under No Access, and by which entries". | Without it, 153's banner and 067's list have nothing to read, and people with Write on a record cannot see who is walled off from it (O2). | **Mostly superseded.** The add/remove routes are replaced by 154's form and 143's `/no-access/enforce` (`NoAccessEnforceEndpoint.cs:55`). The provenance report is mostly in the modal already (142, 140). **The per-record read is not met.** | S–M | no | **KEEP, narrowed to the single per-record read defined below.** CUT the report endpoint, the add/remove routes and the per-entry enforcement state. | Promised feature (O2, 153). A second way to author walls would be a duplicate mechanism. |
| 066 | Every Current Access row shows how the person got access; rows that Secure or Restricted cancels show as cancelled. | On Secure or Restricted records, the modal lists standing, organisation and contact rows as live even though they confer nothing. It overstates access, in the safe direction. | **Mostly met.** Provenance labels already exist. Not met: suppressed-row styling. | S | routine PCF deploy | **MERGE → 067** (styled on the client from `accessPermissionState`/`isSecureRecord`). CUT the server-report rebase. | The only defect left is a display contradicting enforcement. A server-side inverse of the evaluator would be bigger than the problem. |
| 067 | A read-only No Access section in Manage Access, plus a veto marker on affected Current Access rows. | Write users cannot see who is walled off (O2), and a walled contact's grant row still reads as active. | **Not met.** Add/remove is superseded by 154. Opening straight to the section has no caller under O1-final. | S–M | routine PCF deploy | **KEEP, narrowed** (read-only list, veto marker, 066's suppressed rows). CUT add/remove, the direct-open plumbing, standing-level editing and the enforcement labels. | Promised feature. Everything else either duplicates 154 or serves a decision the owner reversed. |
| 069 | Phase-4 tests: share lifecycle, Secure explicit-only access, the Write gate, report accuracy. | None. | **Already met**: `InternalUserShareTests.cs` (~60 tests), `UnifiedEvaluatorSeamTests.cs:223-318,826-854`, `DelegationRuleCharacterizationTests.cs:64-100`. Report accuracy is moot once the report is cut. | S | no | **CUT** | Re-pins behaviour that is already tested. The narrowed 064 brings its own tests. **GUARD/TEST-MACHINERY.** |
| 087 | A custom append-only `sprk_accessevent` row written on every grant, share and deny change. | None realistic. Dataverse auditing has been ON for every access table since 2026-09-10 (`task-086-access-event-schema.md:183-191`), and it also sees edits made in the model-driven app. | Not met. The table exists only in `Deploy-AccessEventEntity.ps1`; it is not deployed. | L | yes (table deploy; failure-policy decision) | **CUT.** Fold a doc correction (table not deployed; Dataverse audit is the source) into 090. | A second audit log beside the platform's own is a duplicate mechanism, and a failed log write would fail every grant. |
| 088 | Versioned access rules plus "who could see record X on date D" replay. | Historical access questions need a manual audit review. Nobody is asking for this today. | Not met. | L | yes (product decision: FR-32 / success criterion 9 would be formally unmet) | **DEFER-TO-NEW-PROJECT** | A new capability, and the new abstraction is the over-engineering the owner fears. Deferring loses no data: audit history is already accumulating. |
| 089 | Attestation seam tests plus architecture docs. | None without 087 and 088. | Not met; nothing to test. | M | no | **DEFER** with 088 (keep one known-gap line in 090's docs) | Tests and docs for a deferred feature. **GUARD/TEST-MACHINERY.** |
| 099 | A user with Write can see and change the expiry date that applies to a record's external shares. | **Real.** Grants lapse at 90 days with no in-product way to extend: a re-grant keeps the old date (`GrantExternalAccessEndpoint.cs:371-387`), and `/set-record-share-expiry` has no client caller. Expired grants show as live, because `fetchExistingGrants` (`TrackingFieldTrio/index.ts:903-950`) does not select `sprk_expiresdate`. A failed read shows "No active grants". | **Not met.** The 065 M8 blocker is cleared. The dependency on 066 is not real. | M | routine PCF deploy and UI check | **KEEP** (also: select `sprk_expiresdate` and mark expired rows; show an error state when the read fails) | A promised feature with class (a) defects attached. |
| 101 | Two Dataverse views of external shares, soonest-expiring first and expiring within 30 days. | Nobody can see what is about to lapse across all records. | **Not met live** (a `savedquery` search found no such view). It is designed in `entity-schema.md:151-161` but never applied. | S | yes (the owner applies the views in the maker portal) | **KEEP** (write the definition into the existing `views-schema.md`/`entity-schema.md`) | Cheap, requested, configuration only. |
| 047 | Proof on live dev that a new secure project gets its own container and is owned by the secure team. | The happy path is already shown by live data. What is unproven live is provisioning under batch-4 code, plus the 409 refusal. | **Partly met.** Dev has 1 secure project (`65a3fab2…`) with its own container `b!MVasATu_…`, which differs from every business-unit container, owned by team `6eabc7f9…`. The refusals are covered offline (`ProvisionRecordedContainerTests.cs:314-470`). The batch-4 live gates do not run a provisioning. | S | yes (batch-4 deploy to dev) | **MERGE → batch-4 deploy live gates** (one wizard provisioning, the inequality and ownership checks, one 409 re-post). Fix or delete the stale lines in `secure-project-creation.spec.ts:71,120-134`. | A short live check of changed code is warranted. Everything else is already shown by live data or offline tests. |
| 094 | Asks about a duplicate file name before a large upload, not after; adds "Use existing". | Slower feedback on large uploads. No data loss: the server refuses duplicates (`93d5e673e`), and a 409 dialog exists (`09025ab39`). | Not met (no pre-check). The correctness half already shipped. | M | yes (new endpoint justification) | **DEFER-TO-NEW-PROJECT** | A speed-up rather than a defect, and it needs a new endpoint. |
| 095 | A linking table so one document can be filed to several matters or projects. | A document can't be filed to a third matter; there are already two slots per type. | Not met. | L | yes (schema; product decision on whether a link grants access) | **DEFER-TO-NEW-PROJECT** | A new capability. |
| 110 | When a firm membership has ended, the person loses inherited access, but an ethical wall on the firm still applies to them. | None at runtime. | **Already met** by 109 (PR #1093): two named sets (`AccessibleRecordSetService.cs:305-346`), with tests in `OrganizationMembershipReadTests.cs:86/93/118/131/271/552`. #999 is still open. | S (paperwork) | no | **CUT as a task.** Close #999, update ISS-020, mark done. | Only bookkeeping remains. |
| 112 | Detects a race where duplicate cleanup keeps a row another operator just shortened or revoked. | It needs pre-existing duplicate rows plus a concurrent change in the same moment, and the result is less access, never more. | Not met (no If-Match anywhere; `GrantExternalAccessEndpoint.cs:329/374/439`). | M–L | no | **CUT** (known limit, class (e)) | A rare edge with no realistic trigger, and the fix (concurrency in the shared client) is bigger than the problem. |
| 113 | A refused re-grant ("did not take effect") changes nothing; a renewal keeps the previous level. | **ISS-028, reachable from Manage Access.** Re-adding an expired person at a different level writes the new level (`:362-374`) before the refusal check runs (`:420-421`). The operator sees "did not take effect", and a later renewal then gives that level. | **Partly met.** ISS-023 is effectively done (`SetRecordShareExpiryEndpoint.cs:215`; tests `RecordShareExpiryTests.cs:133`, `GrantorCeilingTests.cs:232`). ISS-028 is **not fixed**. | S | no (already decided) | **KEEP, narrowed to ISS-028** (move the check above the write, add one failing-first test). Close #1002 by citing the existing tests. | Class (a): a refused request mutates data, and the fix is tiny. |
| 114 | "+ User" sharing works for a licensed user who is flagged external, instead of failing with 422 `user_not_internal`. | Operators cannot share directly with external licensed staff. This contradicts the owner's 2026-09-18 ruling (#1003). | **Not met, and the POML is stale.** The rule now sits in `ClassifyEligibility` (`InternalShareEndpoints.cs:1050-1059`). 142's materializer (`AssignedAccessMaterializer.cs:1366-1370`) uses the same rule, so Assigned-To behaviour changes too. | S | yes (decision: is "enabled person" an acceptable proxy for "licensed"? does Assigned-To follow the same rule?) | **KEEP** (change both consumers, so there is one rule) | Owner-decided behaviour change with a small fix. |
| 153 | A red text banner on project, matter and work-assignment forms when the record is Secure or under No Access, with an "Access status unavailable" fallback. | Nothing on the form tells a reader that an ethical wall covers the record. That is a missed promised feature, not a security gap: enforcement runs either way. | **Partly met; most of the POML is superseded.** The red "Secure" pill and the modal bar were done by 138 (`d32cc6f35`), and blank `sprk_issecure` values were repaired by 150's G-0. O1-final removed the PCF indicator. The banner script and route are not built. | M | yes (BFF deploy; register the script on 3 forms; the 150 lock live first) | **KEEP, reduced**: one route for 3 root types, plus a thin `sprk_accessstatus_banner.js`. CUT the Organization/Contact banners (their shared-map path-A exception), all PCF work, and the dark-mode and carry-over checks. | Promised feature. The Organization/Contact half adds an ADR exception for little user value. |
| 154 | Access administrators get a usable way to add and view No Access entries, and ordinary users stop seeing every entry's Reason. | **Security, fails open:** a record id typed with braces creates a wall that walls nothing (side finding 4). Every core user can read every Reason (O2 unapplied). 143's on-save enforcement never fires because its script is not registered on any form. | **Not met.** 143's server work is merged (`d248dff11`, `7557b0cd9`). No form, view, site-map or role change has been made. | S–M | yes (form, site-map and quick-create changes; O2 role change; menu location, escalation c) | **KEEP, reduced**: entry form with all fields and both scripts registered; a save-time id check in the existing `sprk_noaccessentry_postsave.js`; quick-create off; 3 subgrids; one view; one menu entry; O2; doc fixes. CUT the ObjectRecordPicker PCF, `sprk_objectrecordname`, the "Incomplete Entries" view and its seed test, and the extra views. | The kept parts close class (a) fail-open and disclosure defects plus class (b) docs. The picker and views are polish. |

## Totals

| Recommendation | Count | Tasks |
|---|---|---|
| **KEEP** | 12 | 036, 064 (narrowed), 067 (narrowed, absorbs 066), 090 (trimmed), 099, 101, 105, 113 (ISS-028 only), 114, 136 (live gate only), 153 (reduced), 154 (reduced) |
| **MERGE** | 7 | 003 → 130 gate · 013 → 141 G-8 · 037, 039 → 136 CIAM session · 047 → batch-4 deploy gates · 058 → 090 · 066 → 067 |
| **CUT** | 10 | 054, 055, 057, 069, 082, 087, 110 (close as met), 111, 112, 170 |
| **DEFER-TO-NEW-PROJECT** | 5 | 056, 088, 089, 094, 095 |
| **Total** | **34** | |

Of the 12 KEEPs, **10 involve build work** (036, 064, 067, 099, 101, 105, 113, 114, 153, 154). 136 is a live gate only, and 090 is closure. Only **036** is large; the rest are S or M.

**Tasks whose main value is test or guard machinery:** 082, 057, 069 and 089. All four are cut or deferred. No KEEP task is mainly machinery.

## Reconciling 064 and 153: one per-record No Access read

The two review groups disagreed:
- Group E: 153 can reuse `NoAccessEnforcementStore.ReadActiveEntryIdsCoveringAsync` and does not need 064.
- Group C: a narrowed 064 (a per-record read) should feed both 153 and 067's list.

**Decision: both are right about one thing.** There should be exactly one read, and it should be the one the enforcer already uses, not a new method on `NoAccessListReader`.

1. **Server: lift the lookup out of the enforcer, don't add a new one.** `NoAccessShareEnforcer.EnforceForRecordAsync` (`NoAccessShareEnforcer.cs:284-300`) already does the exact lookup:
   - `GetReferencedOrganizationIdsAsync(entity, recordId)`, which fails closed when the organisations can't be read;
   - then `NoAccessEnforcementStore.ReadActiveEntryIdsCoveringAsync(recordId, orgIds, max)` (`NoAccessEnforcementStore.cs:157`), which checks the record clause and every org chunk and returns a `Truncated` flag.

   Extract those lines into one method on the store, roughly `ReadCoveringEntriesAsync(entity, recordId) → (entryIds, truncated, unreadable)`. The enforcer and the new route both call it, so there is one source of truth for "what covers this record".
2. **One route (this is the narrowed 064):** `GET …/{entity}/{id}/no-access`, for project, matter and work assignment only.
   - Gated by the existing `RecordRouteAccessAuthorizationFilter` with a Read key, and an operation-specific refusal text in place of the "add content" text at `:309`.
   - **Every Read caller** gets `{ secure, noAccess }`, each as applies / does not apply / unknown. This is what 153's banner reads.
   - **Callers who also hold Write** additionally get the entry rows, read with the existing `ReadEntryAsync` per id (capped by `MaxEntriesPerRecord`). This is what 067's read-only list reads.
   - It is one route, not separate summary and entries routes. The Write check uses the same probe the filter already called.
3. **Not built:** no per-record method on `NoAccessListReader`, no report endpoint, no add/remove routes, no stored enforcement state.

**Resulting order:** 064 (this read) → 153 and 067. 153 does not depend on 067. 154's id normalisation should land first, so the string-equality record clause (`NoAccessEnforcementStore.cs:189`) matches what is stored (see side finding 4).

## Minimal ordered KEEP set, with dependencies

**Before batch 5:** batch 4 and the route sweep are integrated, merged and deployed to dev. This is handled separately and gates 036, 047's checks and 143's remaining gate.

| Order | Task | Depends on | Notes |
|---|---|---|---|
| 1 | **113** (ISS-028 only) | — | S. Grant endpoint only. |
| 1 | **114** | owner decision (licence proxy, Assigned-To) | S. `InternalShareEndpoints.cs` plus the 142 materializer. Disjoint from 113's lines, but both touch grant/share tests, so check for conflicts. |
| 1 | **105** | — | S–M. `ExternalDataService.cs` plus a small SPA banner. |
| 1 | **101** | — | S. Configuration and docs; the owner applies the views. |
| 2 | **154** (reduced) | — (143 has landed) | Unblocks 143's form steps and gate 14. Its save-time id check makes 064's lookup match stored ids. |
| 3 | **064** (the single per-record read) | 154's id check (soft) | Lifts the lookup out of the enforcer; one route. |
| 4 | **153** (reduced) | 064 | Thin form script on 3 forms. |
| 4 | **067** (narrowed, absorbs 066) | 064 | Read-only list, veto marker, suppressed rows. |
| 5 | **099** | — (in practice after 067) | Same PCF (`TrackingFieldTrio`/`AccessGrantModal`) as 067, so sequence them and ship one PCF deploy. |
| 6 | **036** | batch 4 deployed; owner accepts the ADR-034 amendment | The only L task and the largest security value. It can start in parallel with steps 1–5 once its gates clear. `AccessibleRecordSetService.cs` does not overlap the other KEEPs. |
| — | **136** (gate only) | — | Live session A below. |
| last | **090** (trimmed) | all of the above; live gates recorded or explicitly carried forward | Absorbs 058, 082's paragraph, 087's doc correction and 089's known-gap line. Remove the cut and deferred tasks from its deps. |

## Owner gates, grouped into live sessions

- **Session A: dev, now, no deploy needed.** The dev BFF `5e1fdcc0b` already runs 130, 135, 136 and 141.
  1. 130's finance check: a non-admin with Read on a matter gets 200 on the summary; without Read, 403. This closes **003**.
  2. 141's G-7/G-8 (sign in as `test.user@demo.spaarke.com`) and the G-1b stale Demo 1 row decision. This closes **013**.
  3. One combined CIAM session:
     - a Restricted project shows no list or download;
     - a Secure project with only an organisation's grant shows nothing;
     - a direct grant is readable;
     - adding a No Access entry blocks access, and removing it restores access after the cache timeout.

     This closes **135, 136, 037 and 039**.
- **Session B: the batch-4 and sweep deploy to dev.** Batch 4's existing live gates, plus **047's** checks (one wizard provisioning, the container differs from every business-unit container, the team owns it, a 409 on re-post), plus **036's** live gate (i)–(v) once 036 lands.
- **Session C: Dataverse configuration, maker portal.**
  - **154**: entry form fields and script registration; quick-create off; 3 subgrids; one view; menu entry (escalation c: which app); the O2 role change.
  - **153**: banner script on 3 forms, after the 150 field lock is live.
  - **101**: the two views.
  - Then 143's gate 14.
- **Session D: batch-5 BFF and PCF deploy.** The 064 route; the 067/099 PCF with a UI check (read-only user, Write user); 105, 113 and 114 smoke checks.
- **Decisions only, in one question round:**
  - 114's licence proxy and Assigned-To rule;
  - 036's ADR-034 amendment;
  - closing ISS-003 / #964 (054);
  - deferring 056, 088, 089, 094 and 095, which accepts that FR-32 and success criterion 9 stay formally unmet;
  - cutting 170;
  - 154's menu location.

## Side findings

1. **The duplicate record filter (found via 170).** There are two record-target authorization filters with different denial shapes:
   - `FinanceAuthorizationFilter` goes through `AuthorizationService.GetCallerRecordAccessAsync` → `IAccessDataSource`;
   - `RecordRouteAccessAuthorizationFilter` goes through `CallerRecordAccessProbe.GetCallerRightsAsync`. Task 159 added a fixed-entity-set overload to it, used at `EventEndpoints.cs:97/133/150`.

   Renaming the first to `RecordTargetAuthorizationFilter` would put two near-identical names side by side and hide the duplication. This is a round-56 class (b) defect. Fix it by converging on one filter, in a future project after 165–167 land. Don't do it now, because those tasks are in flight on the same routes. Until then, at most change the deny log text from "Finance" to "Record".
2. **The 136/037/039 status mismatch.**
   - 136's code (`700763023`, plus `task/uac-r2-136-f2` at `378a2ce2f`) is on integ **and on `origin/master`**, and runs on dev, yet its POML says `pending`.
   - 136's POML notes (`:183`) record a deliberate choice to keep 037 and 039 pending until the CIAM live check. That is why all three look open with no code left.
   - Fix: run Session A's CIAM block once, then mark 135's gate, 136, 037 and 039 completed together and update their TASK-INDEX rows.
3. **143's TASK-INDEX row is stale.** It still shows `[open]`, but its server code is merged on integ (`d248dff11`, `7557b0cd9`). What remains is the deploy, the form steps (reduced 154) and live gate 14. 143 is inside 132–169, so I didn't review it; this note is only for bookkeeping.
4. **154: a silent wall that protects nothing (fails open).** This is a class (a) security defect.
   - The cause: a `sprk_objectrecordid` typed with braces or in another non-canonical form passes `Guid.TryParse` (`NoAccessListReader.cs:596`), so the row is not flagged as malformed. But both readers match with string equality on the lowercase GUID (`NoAccessListReader.cs:330`; `NoAccessEnforcementStore.cs:189`), so the entry never matches any record and walls nothing, with no warning.
   - Two related gaps: 143's on-save script is not registered on any form, so enforcement on save never fires and only the 5-minute job applies; and every core user can read every entry's Reason until O2 is applied.
   - Minimal fix: the save-time normalise-and-refuse check in the existing `sprk_noaccessentry_postsave.js` (in reduced 154). The small server half: make the reader's existing malformed-row rule reject a non-canonical id, so an entry written outside the form (Web API or import) shows up as malformed instead of silently walling nothing.

## Smaller notes

- **Docs that contradict code (fold into 090 or 154):**
  - `entity-schema.md:104` (example 2, a per-communication block) is stored but never evaluated.
  - `docs/data-model/sprk_accessevent.md` implies the table exists.
  - `entity-schema.md:139/237` says Spaarke Core User holds Global Read, which O2 changes.
- **090's deps list** includes 082 and the tasks cut or deferred here. Prune it before 090 starts.
- **C:\wt4i is not a clean baseline.**
  - The integ head moved during this review.
  - One review agent saw a staged task-140 merge in progress there.
  - At the end, two `package-lock.json` files were modified.

  Treat it as a live working tree, not a read-only snapshot.
