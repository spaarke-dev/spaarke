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
   - **B (running):** the narrowing inside `DocumentAuthorizationFilter` for EVERY document-id route (all 26, all ops) + the bulk-download filter + the two unfiltered list routes (`DataverseDocumentsEndpoints.cs:550`, `:646`); lookup fault → narrow (same rule as A).
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
   - **Task 189 = PR #2169** (C:/wt189, head abcf516022): pass-1 fixes F3 c20e9882aa, K1+K2 raise-only b7f74100f3, K5 f69b007da8; wide run 10066/0, ArchTests 1211, +7,787 B. **F1 (record for every parented doc) being committed now** with deploy-order gate: #2169 deploys before #2161, and #2161 does not deploy until 189's record backfill reports zero unchanged-value record writes (~N/500 runs; #2161's re-file pass looks back 60 min); #2161 pre-deploy gate adds a read-only misplaced-file count. Then security pass 2. Merges before 188 (I-16 line conflict) and before word-add-in #2177 (T8).
   - **Task 190 (T7b)** starts after 188 merges. **#2177 (word-add-in T8):** two security passes pending a slot.
   - 036 flag-on gates DONE: PASS server-side (0a4fbaa38); #1567 closed; sent to R3; filed #2167 (privilege-after-RPA invariant), #2168 (disabled/unlicensed refusal surfaces as a fault). Owner SPA steps (ii)–(vi) are in the gates note.
   - ebilling 050a r3: FIX-ROUND-NEEDED sent (F3 tests for walk-Failed / no-Secure-team; F1 pre-existing D-140 thread hop; K4 accepted as known limit). Re-check the fix diff when sent.
2. **#2150 (#2143) round 10 done** (C:/wt2143, draft; head **0ba86217e2**): F2-b (Failed sync falls through to `RemoveMirrorAsync`), F2-a (hook before the grant on anchor + threadless paths), cheap closes; all mutants killed; filter 2133/2133, ArchTests 1152/1152.
   - r10 re-check DONE: behaviour correct and fail-closed on every path; 950/950 filtered. Two NEW log-only F2s: F2-1 false ERROR "sharees may still open it" after a transient Failed mirror step (`SecureChildShareSynchronizer.cs:451-454`; fix: `exhaustedLevel` param, Warning on the earlier calls, Error only on the last step `SecureChildReconciler.cs:968`; "could not be read" wording when Failed; assert `RemovalErrors()` empty in the NoThreadMove test). F2-2 "Re-own it first" on a correctly isolated row (`:610-612`; reword as unsafe-to-re-own). **Owner chose (a), round 111.** **Running:** round 11. F2-1 takes an `exhaustedLevel` parameter: Warning on the earlier calls, Error only on the last step. F2-2 rewords the message. No behaviour change. Next: quick scoped re-check → owner sign-off (as stated in option (a)) → merge → deploy → gate.
   - Then owner sign-off → merge → R3 deploy → gate. At merge tell ebilling (K2: D-140 thread hop → `RecordThreadFiling.ParentsOf`; FinanceEndpoints confirm catch dead) and word-add-in (wire the caller into the attachment-branch `FileIfUnfiledAsync`).
3. **#2161 (#2159/#2151)** (C:/wt2159, draft; head 2662545da2): slices 1–4 done; #2176 filed (B5 > 250 MB). Final pass: FIX-ROUND-NEEDED (no widening found). **Running:** fix round for 1 F1 checkout-mid-copy not re-checked (source deleted under the editor), 2 F1 same-container re-copy consumes the F3 release (S1→S2 held for good), 3 F2 unrecordable release has no working recovery (durable ADR-004 retry preferred; agent stops if disproportionate), 4 F1-low MakeSecure skips B9 source proof on S1→S2, 5 pre-existing cancelled-copy orphan (finally cleanup). K1–K7 accepted. Then scoped re-check → owner sign-off → merge → deploy → gate.

## Next after those
- **#2153 (#2152)** (C:/wt2152, draft, head 863326f0b): owner sign-off → merge → R3 deploy → gate.
- **D7 T7** (after the 036 gates pass): one predicate (root grants ∧ own Access Permission ≠ Restricted ∧ not from an internal-only/non-Open communication via `_sprk_relatedcommunication_value` → ParentDocumentLookup ≤ 3 hops, cycles fail closed; reuse `ExternalCallerCommunicationGuard.ReadVisibleCommunicationIdsAsync`) on list, download, versions, preview, SPE routes and AI tools; `sprk_relatedcommunication` BFF-only writes; I-14 + ADR amendment. **T7a**: `ChildAccessPermissionReconciler` must not widen a child's own Restricted. **T7b**: legacy unlinked-document backfill by the recorded keys only (no heuristics; unmatched listed for the owner). ebilling 081.5/086.4 and word-add-in T8 (task 130) depend on it.
- **word-add-in task 130**: review its PR with two security passes when the number arrives.
- **field-mapping 017**: review when sent.
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
