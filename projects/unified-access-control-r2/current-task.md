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
   - **A (running):** `RetrievalAccessTrim` narrows for external callers; drive-item keys pass only if a readable row on the item is visible after narrowing; external-lookup fault → treat as external (narrow), never refuse all.
   - **B:** the narrowing inside `DocumentAuthorizationFilter` for EVERY document-id route (all 26, all ops) + the bulk-download filter + the two unfiltered list routes (`DataverseDocumentsEndpoints.cs:550`, `:646`); lookup fault → narrow (same rule as A).
   - **C:** `sprk_analysis` rows hidden when their `sprk_documentid` is hidden (`AppOnlyDocumentNarrowing` overload, hide-only) in read_query + search_data; extended surface ArchTest; K7 allow-list of delegate consumers; publish size, merge-tree, notes, PR body.
   - **OWNER (🔔 pending):** path B for the AI-tool app-only seam ("188: path B approved"); **F1-B cascade** (RemoveLink on communication/parent delete un-hides; recommend (a) Restrict); **Limited** (hide too, or Restricted only as D7 says); #2179 (flagged-external direct Dataverse access). Before deploy: read-only count of partner-visible docs that will disappear (K4).
   - **Task 189 = PR #2169** (C:/wt189): pass-1 fix round running (F1 record for every parented doc + downstream `modifiedon` check; F3 regex; raise-only write on undecided record; K5 app-only writers refuse the columns). Then pass 2. Merges before 188 (I-16 line conflict) and before word-add-in #2177 (T8).
   - **Task 190 (T7b)** starts after 188 merges. **#2177 (word-add-in T8):** two security passes pending a slot.
   - 036 flag-on gates DONE: PASS server-side (0a4fbaa38); #1567 closed; sent to R3; filed #2167 (privilege-after-RPA invariant), #2168 (disabled/unlicensed refusal surfaces as a fault). Owner SPA steps (ii)–(vi) are in the gates note.
   - ebilling 050a r3: FIX-ROUND-NEEDED sent (F3 tests for walk-Failed / no-Secure-team; F1 pre-existing D-140 thread hop; K4 accepted as known limit). Re-check the fix diff when sent.
2. **#2150 (#2143) round 7** (C:/wt2143, draft; head f81b616bd7 + R7a–e): F3 asked about every secure root removed from the row's own columns, plus caller plumbing (fixes the round-6 regression: re-file off a secure record left the email stuck).
   - Next: scoped re-check of the fix diff → owner sign-off → merge → R3 deploy → gate. Exception name unchanged: `RecordOwnerUnsettledException` → 409 `record_owner_unsettled` (tell ebilling at merge; K2: FinanceEndpoints :281 catch becomes dead code after both merge).
3. **#2161 (#2159/#2151)** (C:/wt2159, draft; head d5a7cc5867): slices 1 → 4 in order, each reported; agent files the B5 over-250 MB issue.
   - Rule: secure→secure (S1→S2) moves are held unless an F3 release entry exists; `IsolationHoldAsync` guards every purpose.
   - Next: re-check → owner sign-off → merge → deploy → gate.

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
