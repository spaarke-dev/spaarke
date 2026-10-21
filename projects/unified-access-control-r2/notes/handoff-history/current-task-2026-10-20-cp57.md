# Current Task State — `unified-access-control-r2`

> **Format:** this file holds the CURRENT state only. Rewrite it at each checkpoint (never prepend), and keep it at or under 10 KB.
> - **Standing rules:** the project `CLAUDE.md`.
> - **Decisions:** `notes/session27-owner-decisions-and-research.md`, now at **round 110 (2026-10-20)**.
> - **Defer list:** `notes/defer-issues.md`.
> - **Delete design:** `notes/core-record-delete-design.md` (§12 = ebilling D-146; supersedes §10/§11).
> - **D7 design:** `notes/2119-manage-access-all-forms-design.md` §6 (T7, T7a, T7b with match keys).
> - **Gates:** `notes/batch7-live-gates-2026-10-17.md`; canary: `notes/task-034-negative-canary.md` §10.
> - **Narrative:** the commit messages.

> **Last Updated**: 2026-10-20 (checkpoint #57).

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Mode** | Final push. Fix-round loop; merge only when BOTH required checks (Router + "Client lint (type-aware ratchet)") are green; one background merge job at a time; `--squash --match-head-commit`, no `--delete-branch`, after `git merge-tree`. Deploy via R3. A task closes on merge + deploy + a server-side gate. |
| **Fix bar** | The most technically correct fix; no complexity without value. Every defect found is fixed or filed. |
| **Agents** | Opus for executors and verifiers. Helper scripts get a unique prefix in a private folder; the scratchpad is shared (memory `agent-scratch-script-names`). Cap 4 concurrent. |
| **Dev live** | BFF `1.0.0+4f5f22694c` (036, master) with `ExternalAccess__ImpersonatedRootSets__Enabled=true` since **23:51:26Z** (owner said yes directly to R3 and ran the command). healthz 200, healthz/dataverse 200, `/api/me` anon 401 at 23:52:36Z. |
| **036 rollback** (owner OK required) | `az webapp config appsettings delete -g rg-spaarke-dev -n spaarke-bff-dev --setting-names ExternalAccess__ImpersonatedRootSets__Enabled` |
| **Peers** | Address by ListAgents session name (pipe ids don't resolve as `to`): R3 `spaarke-wt-spa-external-access-platform-05`, ebilling `spaarke-wt-ebilling-r1-5b`, word-add-in `spaarke-wt-spaarkeai-word-add-in-r1-a1`, field-mapping `spaarke-wt-field-mapping-server-write-pa-be`, provisioning `spaarke-wt-customer-provisioning-orchest-ff`. |

## In flight (agents)
1. **D7: task 188 = PR #2170** (C:/wt188, head c3da0a5d0e after round 2). Security pass 1 → round 3 split into fresh agents, in order (they share the ArchTest file):
   - **A DONE** (f1263870c0 + notes 7bba318b10): `RetrievalAccessTrim` narrows for external callers (doc + drive-item keys); 4/4 mutants; filtered 11,382 pass; ArchTests 1212; +17,158 B total. Limit: no "withheld" note to the tool on a narrowing fault (warning logged only).
   - **B DONE** (pushed to 7855b0540d): 8/8 mutants; broad filtered 13,234/0; ArchTests 1214; +22,143 B. Also fixed: an IdentityModel logger test-isolation defect. Filed #2185 (ProvisionProject timeout flake on master). `.claude/constraints/auth.md` D7 section committed by the main session: **936ab537af**.
   - **C DONE** (head 305ef2c3d4): analysis and analysisoutput hidden in `read_query` and `search_data`; K7 allow-list (7 consumers); test double kept out of src; texts updated; Limited test. Results: 6/6 mutants; 5,798 filtered; ArchTests 1220; +29,730 B total for 188. Follow-up DONE (head **8f0d957edc**): `describe` fixed; output file hides too; #2189 filed for the other AI-derived tables; 4/4 mutants; 5,817 filtered; ArchTests 1220.
   - **Pass 2 (#2170+#2186): FIX-ROUND-NEEDED.** Internal-plane routes outside the filter only check Read, for flagged-external systemusers:
     - F1-1 `IAiAuthorizationService`: chat document context, analysis execute, RAG search, visualization related;
     - F1-2 direct `AuthorizeAsync`: chat from-document .eml ingest, email-send attachments, playbook run, Finance Document path;
     - F1-3 `GET /api/ai/analysis/{id}` and chat analysis host;
     - F1-4 communication attachment text ignores the attachment's own value;
     - F1-5 the `read_query` row cap is measured after narrowing.

     K: the K7 bypass shapes (DynamicInvoke, `ExternalDataService` narrow methods, `Type.GetType`); 4 defensive legs unpinned; the 10-min externality cache; count/existence leaks (#2179 class); SPE direct access.

     **r4a DONE** (head 864a60c6bc): d3b589d151 `AiAuthorizationService` narrows + archive ingest; 37229a3aed `AnalysisAuthorizationFilter.IsAnalysisDocumentHiddenFromCallerAsync`; ffe869362b row cap on the raw page; bfca006249 ArchTest AI rule; 8eb347dc6d Option A double in 2 AI hosts; 864a60c6bc notes §17. 8/8 mutants killed; 14,658 filtered; ArchTests 1,222; publish +3,817 B; merge-tree clean.

     **Running: r4b** (launched 2026-10-21): F1-2 helper at Communication/Playbook/Finance sites + ArchTest ban; F1-4 attachment text; chat SPE drive-item ids; analysis `sprk_regardingdocument`; sessions/by-analysis; K7 hardening (3 bypass shapes); pin the 4 defensive legs. Then a scoped re-check.
     - slice-D race fix: DONE. 6869487e04 maps `0x80040227` to a 409 on all 3 routes; ecfaef0848 syncs the 3rd copy of `sprk_DocumentOperations.js`. Retry works on internal and v1. The external retry stays refused by the pointer check (`document_storage_unverified`): an existing task-032 known limit, and staff clear it via the internal route.

     **Next, after r4b:** a scoped re-check; a read-only count of partner-visible documents that will disappear (K4); deploy slice-D `-Apply`, then the web resource, then the BFF; R3 reviews the routes.
   - **D = PR #2186** (f4ad094013 XML+script, 7f469c36ca 409 `document_has_child_documents` before the file on all 3 delete routes, 6300a663f8 docs). Results: 390 filtered pass, ArchTests 1214, +6,872 B. Dev counts: 49 communications with linked docs, 46 parent docs. `-Verify` dry run shows live MISSING (RemoveLink), as expected.
     - Merge conflict with #2170: the I-14 row only. Take D7's row and add the slice-D sentence.
     - Main session added delete design §13 (6b41e1bb20).
     - MDA message fixed (dfdbfea392, `sprk_DocumentOperations.js` v1.28.2, 10 jest tests). Archive gap filed as **#2187**.
     - Deploy order for #2186:
       1. `-Apply` / `-Verify`
       2. `scripts/Deploy-WebResourceInline.ps1 … sprk_DocumentOperations.js`
       3. the BFF
     - **`-Apply` on dev at deploy time, together with the BFF** (main session).
     - Original slice-D brief: XML + `Set-DocumentLinkDeleteRestrict.ps1` (dry run + `-Verify` only; the main session runs `-Apply` after review), delete-path ordering, a 409 for a parent document that has children, and dev counts. If the agent recommends deleting the children instead, it stops and reports first.
   - Then security pass 2 over the whole of #2170 (+ D).
   - History of B below. Implementation done via `InternalPlaneDocumentNarrowing`. Coverage: 26 filter routes, plus 6 compose routes and the Office version save through delegating filters (R3 must review). Bulk download drops hidden ids. Lists narrow before paging. 44 route tests. About 85 legacy host tests broke (the test callers are not resolved systemusers). **Decision: Option A** — one shared `AllVisibleDocumentNarrowing` double, a guard that production uses the real seam, a fix for the `plane=` label, the list-cost limit recorded, and `ExternalWorkforceLinkEndpointsTests` checked on master (file it if it is pre-existing). Original slice-B scope: the narrowing inside `DocumentAuthorizationFilter` for EVERY document-id route (all 26, all ops) + the bulk-download filter + the two unfiltered list routes (`DataverseDocumentsEndpoints.cs:550`, `:646`); lookup fault → narrow (same rule as A).
   - **C:** `sprk_analysis` rows hidden when their `sprk_documentid` is hidden (`AppOnlyDocumentNarrowing` overload, hide-only) in read_query + search_data; extended surface ArchTest; K7 allow-list of delegate consumers; publish size, merge-tree, notes, PR body.
   - **OWNER round 111 (2026-10-21) decided:** path B APPROVED; cascade **(a) Restrict**; **Limited does not hide**; #2179 **(c)** accepted + closed. Next steps:
     - After slice B lands, the MAIN SESSION commits the `.claude/` amendment on the #2170 branch. Text is in the round-111 notes; find the right `.claude/adr` / `constraints/auth.md` home. The agent is not allowed to write `.claude/`.
     - **Slice C** additionally:
       - updates the `IDataverseUserClient.cs:13-19` rule text to name the exception;
       - changes I-14 "owner sign-off pending" to "approved round 111";
       - adds the #2179 known limit;
       - adds a Limited must-not-fire test.
     - **Slice D (after C):** set Restrict on `sprk_relatedcommunication` (`sprk_Communication.xml:151-152`) and `sprk_parentdocument` (`sprk_Document.xml:322-323`).
       - Script: dry run / `-Apply` / `-Verify`, copying `Set-DocumentAnalysisCascadeSchema.ps1`.
       - Every BFF delete path must delete or unlink the child documents first. The core-record delete runs documents (children first) before the communication. The single-document delete refuses cleanly (409) or handles the children.
       - The dev `-Apply` happens after review.
     - **Before deploy:** a read-only count of partner-visible documents that will disappear (K4).
   - **Task 189 = PR #2169** (C:/wt189, head abcf516022): pass-1 fixes F3 c20e9882aa, K1+K2 raise-only b7f74100f3, K5 f69b007da8; wide run 10066/0, ArchTests 1211, +7,787 B. F1 committed 7c7b245b10 (head 4546c0d6ed; wide 10068/0; dev N=279 parented of 596, backfill ≈2 runs ≈4 min). **Deploy-order gate:** #2169 deploys before #2161; #2161 deploys ≥60 min after the first run on #2169 with `GET /api/admin/jobs/secure-child-reconciliation/status` → `ResultJson.accessPermission.sweep.complete=true`, mode write, failure null, writeCapReached false, failed 0, carriedRows 0, `tablesWithoutColumn` not listing sprk_document. #2161 pre-deploy also gets a read-only misplaced-file count. Pass 2 = FIX-ROUND-NEEDED, docs only, no widening path. Pass-2 fix round DONE (head **70ef2bf299**: c8c591777f K1 push skip, 48869bc7c5 F1 docs, 70ef2bf299 F4 notes; #2183 filed for K3; 631 filtered pass, ArchTests 1211; main session re-checked the push-skip diff: clean; field-mapping has the SHA and test name). **Waiting: owner sign-off for #2169** → CI → merge → schema `-Apply` on dev already done → R3 deploy → backfill gate. The round covered:
     - F1: deploy guide `:1225` (BFF-before-schema makes a raised R permanent; schema must go first) + stale notes/guard reason;
     - F4: 60-min wait timed from the END of the sweep-completing run + 10 min;
     - K1→fixed: field-mapping push skips the two access columns on `sprk_document`; tell field-mapping peer;
     - K3 filed as issue.
     Then merge (before 188/#2177). **Owed to field-mapping:** the commit SHA and test name that pins the push skip. Their 040 keeps it in the engine's push path, and I review that part. Their 014 adds a criterion that `sprk_accessinheritance` is never a mapping target; `sprk_accesspermission` stays fill-only at create. Dev has 0 access mapping rules.
   - **ebilling design review queued** (procedure-and-file-model v4, `origin/work/ebilling-r1` c260f9de11, 9 security questions; acknowledged; my lead: K4 bytes-exposed window → likely F, use a not-yet-servable Stored File until the post-commit isolation check passes). Review DONE and sent. NOT READY: 6 F1, 3 F2, 3 F4. Worst is F1-a: org-level Read on `sprk_procedure` exposes the Secure exceptions Procedures; the recommended option keeps record rules in a record-scoped ruleset. Owner questions 1-5 go via ebilling, SPA-r3 questions 1-4 via ebilling to R3. Re-review v5 when it is sent. **Owed (mine):** update `core-record-delete-design.md` §1.3/§12 once the design is final: assignment rows → Delete; `sprk_procedure`, `sprk_procedureversion` and `sprk_storedfile` on the hard-deny list; drop the record-scoped ruleset and ratecard rows and the stale BU-scoped text at :401/:419/:430. Field-mapping already has 014/040 criteria (70fc869afa). They still need #2169's SHA and test name. Merges before 188 (I-16 line conflict) and before word-add-in #2177 (T8).
   - **#2177 (word-add-in T8)** at 65b4f5f468 (master merged, follows 189): security pass 1 running; pass 2 after fixes. **Follow-up owed:** widen `DocumentAccessPermissionWriterGuardTests` Key regex to `\w*AccessPermissionColumn` (+ must-fire/must-not-fire), send SHA to word-add-in.
   - **#2161:** fix rounds done through e0a4f2c133; merge-master round running (9 conflicts with #2150/#2169); then scoped re-check -> owner sign-off -> deploy after 189 backfill gate + 70 min.
   - **#2191 (field-mapping):** taking all items incl. AppendTo; awaiting their new head for a scoped re-check.
   - **ebilling v5** (70fc2cc616 / c6c5cb0c3b): full UAC review running; derived-row isolation gap -> owner options (a)/(b)/(c) pending the review's recommendation.
   - **Owner pending:** R3 dev deploy go (a10e814d02); task 191 Q1/Q2 (auto-share to one External Collaborators team) and Q5 (one-way BFF job).
   - **Task 191 (NEW, round 113, #2179 reopened): ACTIVE.** Flagged-external systemusers reach no internal-only record in Dataverse or the SPA (internal-only = Restricted, internal-only communications + their documents, all Procedure files; owner-confirmed). Design study running -> `notes/2179-flagged-external-dataverse-design.md`; owner checkpoint before any schema/role/share change; build after 188/189. Expected #1864 real fix (#2125, #2127). ebilling: internal file route refuses all `sprk_isexternal`; invoices/exports via the firm route; submission all-or-nothing (v5 pending).
   - **#2169 MERGED 2026-10-21** (1cc7b39ff3, owner round 112 sign-off). Next: R3 deploy -> backfill gate (sweep complete) -> 70 min -> #2161 may deploy. **#2150** signed off; conflict with #2169 in `SecureChildShareWorld.cs` resolved (252f28cf4c), CI running -> merge -> deploy with #2169.
   - **Task 190 (T7b)** starts after 188 merges. **#2177 (word-add-in T8):** two security passes pending a slot.
   - 036 flag-on gates DONE: PASS server-side (0a4fbaa38); #1567 closed; sent to R3; filed #2167 (privilege-after-RPA invariant), #2168 (disabled/unlicensed refusal surfaces as a fault). Owner SPA steps (ii)–(vi) are in the gates note.
   - ebilling 050a r3: FIX-ROUND-NEEDED sent (F3 tests for walk-Failed / no-Secure-team; F1 pre-existing D-140 thread hop; K4 accepted as known limit). Re-check the fix diff when sent.
2. **#2150 (#2143) round 10 done** (C:/wt2143, draft; head **0ba86217e2**): F2-b (Failed sync falls through to `RemoveMirrorAsync`), F2-a (hook before the grant on anchor + threadless paths), cheap closes; all mutants killed; filter 2133/2133, ArchTests 1152/1152.
   - r10 re-check DONE: behaviour correct and fail-closed on every path; 950/950 filtered. Two NEW log-only F2s: F2-1 false ERROR "sharees may still open it" after a transient Failed mirror step (`SecureChildShareSynchronizer.cs:451-454`; fix: `exhaustedLevel` param, Warning on the earlier calls, Error only on the last step `SecureChildReconciler.cs:968`; "could not be read" wording when Failed; assert `RemovalErrors()` empty in the NoThreadMove test). F2-2 "Re-own it first" on a correctly isolated row (`:610-612`; reword as unsafe-to-re-own). **Owner chose (a), round 111.** Round 11 DONE: 01a28cb962 (F2-1 overload `exhaustedLevel`), 23bb2b5a97 (F2-2 wording). Filtered 1279/1279, ArchTests 1152. Main session re-checked the diff: log-only, clean. **Waiting:** owner sign-off + CI. Plan notes: F2-1 takes an `exhaustedLevel` parameter: Warning on the earlier calls, Error only on the last step. F2-2 rewords the message. No behaviour change. Next: quick scoped re-check → owner sign-off (as stated in option (a)) → merge → deploy → gate.
   - Then owner sign-off → merge → R3 deploy → gate. At merge tell ebilling (K2: D-140 thread hop → `RecordThreadFiling.ParentsOf`; FinanceEndpoints confirm catch dead) and word-add-in (wire the caller into the attachment-branch `FileIfUnfiledAsync`).
3. **#2161 (#2159/#2151)** (C:/wt2159, draft; head 2662545da2): slices 1–4 done; #2176 filed (B5 > 250 MB). Final pass: FIX-ROUND-NEEDED (no widening found). **Running:** fix round for 1 F1 checkout-mid-copy not re-checked (source deleted under the editor), 2 F1 same-container re-copy consumes the F3 release (S1→S2 held for good), 3 F2 unrecordable release has no working recovery (durable ADR-004 retry preferred; agent stops if disproportionate), 4 F1-low MakeSecure skips B9 source proof on S1→S2, 5 pre-existing cancelled-copy orphan (finally cleanup). K1–K7 accepted. **Fix round DONE** (4df94d33a5: F1–F5 in one commit; new ADR-004 `RelocationReleaseRetryJobHandler`; 10/10 mutants; 4969 filtered; ArchTests 1210; +4,490 B; merge-tree clean; F4 behaviour change: unbound released legacy row → SourceUnverified until item-binding backfill). **Running:** fix for pre-existing `MoveCoreAsync` non-cancel catch deleting the copy when the pointer write may have landed (re-read on any failure; keep the copy unless proven not pointing). Then scoped re-check (security: the new job carries a release permission) → owner sign-off → merge → deploy → gate.

## Next after those
- **#2153 (#2152)** (C:/wt2152, draft, head 863326f0b): owner sign-off → merge → R3 deploy → gate.
- **D7 T7** (after the 036 gates pass): one predicate (root grants ∧ own Access Permission ≠ Restricted ∧ not from an internal-only/non-Open communication via `_sprk_relatedcommunication_value` → ParentDocumentLookup ≤ 3 hops, cycles fail closed; reuse `ExternalCallerCommunicationGuard.ReadVisibleCommunicationIdsAsync`) on list, download, versions, preview, SPE routes and AI tools; `sprk_relatedcommunication` BFF-only writes; I-14 + ADR amendment. **T7a**: `ChildAccessPermissionReconciler` must not widen a child's own Restricted. **T7b**: legacy unlinked-document backfill by the recorded keys only (no heuristics; unmatched listed for the owner). ebilling 081.5/086.4 and word-add-in T8 (task 130) depend on it.
- **word-add-in task 130**: review its PR with two security passes when the number arrives.
- **field-mapping 017**: review when sent.
- **field-mapping 013** (RecordCreationService, `task/fm-013` bad3195920..123b632315). Review result: FINDINGS, no runtime defect. Fixes requested:
  - F-1: an I-16 test.
  - F-2: a table-plus-id key test.
  - F-3: rename the Office `RecordCreationService.cs`, because the census can't fire on the new service.
  - F-4: the false premise in the escalation. Five writers stamp before the create, so the recommended FR-06 text is "pre-create derive + post-create re-derive; documents post-create via `DecideDocument`".

  The fix round (fad68a797b..36d885f72f) was re-checked by the main session: **NO-OBJECTION given**. Their 014/017/018 carry K-2 to K-4, the narrower SystemPrincipal, the document access columns, and no re-point of pre-create stamp writers until FR-06 step 10 is decided (owner, via them). Review 017 when it is sent. **014** (fad68a797b..13e6919692) review returned FINDINGS. Fix-now items:
  - F-A: the (table, id) key is untested; test text sent to the peer.
  - F-B: a filing predicate is needed. Use `ParentsOf`, because `workassignment.sprk_regardingcommunication` falls through today.

  #2188 is LIVE on master via the Office quick-create; it blocks 017, and I commented on the issue. Fix design **D agreed** (caller-mask: the app read supplies types; one caller read, null vs. not-null, gates every copied column; a fault copies nothing).
- Conditions:
  - non-comparable column types fail closed;
  - masked columns get the same warning text as empty ones;
  - an ArchTest ensures no path skips the mask;
  - tests and mutants included.
- Delivery: an Office hotfix PR, then 014 reuses the helper.
- **Review the hotfix PR when it is sent.** I filed #2190: the work-assignment `regardingcommunication` is missing from the plan's filing columns, a UAC topology issue. Fix round 3 (58d1a3548d..405a9b4f90) was re-checked by the main session: **NO-OBJECTION** for 014. R3-1 and R3-2 are accepted as K and carried to 017. Their PR **#2184** (013 + 001–006 + 010–012) renames the Office `RecordCreationService.cs` and re-keys census rows. **#2150 also edits `RecordOwnerAssignmentCensusTests.cs`**, so run merge-tree against #2184 before #2150 merges; whichever lands second rebases.
- **ebilling 050a round 3** (F1-a, F1-b, F2): re-check the fix diff when sent. D-145 on hold (owner reviewing the sprk_documenttype trigger concern).

## OWNER items outstanding
1. **#1864**: B recommended. **#2119 D1–D6**: as recommended. **Census** a/b/c: (a). **CustomerBusinessUnitId** auto-set: yes. **#2139**: (2). **#2157**: (a). **#2164** (#1629 alignment): (a). **#2160 K1** (field security on `sprk_communication.sprk_communicationthread`): (a). **#2163** (acme ID-LINK-RECON aborts): needs an owner go for an environment change.
2. **186**: the owner signs in as Tracy, submits one intake, and tells me first → check 3 + 5b remove/restore of `prvCreatesprk_ServiceRequest` (fd03f48f…) on role 9deaac9c…, at Basic. Check 6 needs a licensed internal sign-in. Close-out: export the Workforce role into SpaarkeMaster (package-scope.json:128-131 excludes it; verify prvCreatesprk_InvoiceSubmission Basic, prvReadsprk_InvoiceSubmission Deep).
3. **Sign-offs to come**: #2150, #2161, #2153 — each after its passes.
4. **036 gates (ii)–(vi)**: the owner's SPA end-to-end steps, as listed in the gate write-up.

## Done 2026-10-20 (since checkpoint #56)
- 036 deployed flag-off via R3; flag-off sanity clean; 034 negative canary on the deployed 4f5f22694 PASS 20/20 ×2 (0f345eace); flag flipped ON by the owner 23:51:26Z.
- ebilling 050a round-2 findings delivered.
- PR #2161 opened (#2159/#2151), including a real pre-existing widening fix (secure→secure).
- D7 T7a/T7b scope and T7b match keys recorded (e6dac7736, 910e78a3e).
- Filed: #2159, #2160 (+K1 decision), #2162 (per-family teams fail open), #2163, #2164, #2165 (unlink leaves the email Secure-owned).

## Cleanup / wrap-up
- Worktrees to remove when idle: C:/wt099b, C:/wt2103, C:/wt2091b, C:/wt2130, C:/wt2128, C:/wt036 (all merged).
- Wrap-up 090: the 058 docs and the UAT checklist.
