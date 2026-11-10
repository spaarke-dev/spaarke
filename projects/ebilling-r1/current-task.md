# Current Task State

> **Last Updated**: 2026-11-09 (session ebilling-r1-c7, authoritative). Work head = `origin/work/ebilling-r1` @ `35dc4dafd1` (DEF-059 merged at `1678e1ae85`). Dev BFF: uac-r2's `6fbb3a7642` (reconcile false). **None of the 130 batch is on work, master or the dev BFF.**

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Mode** | Owner: "keep progressing on open tasks so we can complete the project". At most 4 agents. Merge each finished branch after its reviews; verify the combined tip (build, full ArchTests, FILTERED unit). One adversarial pass per change set; two for partner-readable / auth / tenant-isolation; a scoped Sonnet re-check when a fix changes the approach. Every agent launch states a model. Never `--no-verify`. |
| **Agent worktrees** | `isolation: worktree` starts on MASTER. Every brief names the base sha and checks `git merge-base --is-ancestor`. Never cd into agent worktrees; use `git -C` and absolute paths. To remove one: `git worktree unlock`, then run `Remove-WorktreeSafe.ps1 -Path <one path>` from the **PowerShell tool**, one path per call (bash mangles the backslashes). |
| **Public-repo rule** | Never put access-gap mechanisms, identity names or role maps in notes, comments, commits, PRs or issues. Private findings go to uac-r2 by message only. |
| **Peers** | uac-r2 = `uac-r2`; ontology-r1 = `ontology-r1` (messages to it were HELD/expired — not delivered; resend only if owner asks); provisioning = `provisioning-r1`; word-add-in = `word-add-in-r1`; mcp = `mcp-r1`; R4 = `spa-r4`; field-mapping = `field-mapping-r1` (from 2026-11-09; their 032b-2/032b-3 will run on the `work/ebilling-r1` tip, and we receive the 032b-2 diff after their two adversarial passes; not started). |
| **Live-change rule** | Live Dataverse writes / role changes need the owner's OK typed in THIS session, plus uac-r2 notices before and after. |

## Exact next action

Wait for the agent notifications below and act on each:
1. **130b is FINAL** at `2367fbab2c` (uac-r2 signed off). Nothing pending except the SubmissionCoreColumns nit at the batch merge.
2. **130-0 fix round 3** (Sonnet, branch `task/ebilling-130-0-fix3` → ff `task/ebilling-130-0-fix1` from `7a7f5d2808`). It covers:
   - Q2: a single invoice-linked record whose decision faults (the firm read or the per-invoice check) gets a 503.
   - A uniform 403 for a missing id on every verb of the single to-do and sibling routes.
   - The Q3 note.
   - Re-check hygiene.
   - F (uac-r2): `InvoiceLinksOf` JsonElement overload — an absent key means `UnreadableLink`; pin that FetchXML callers select `LinkColumnsOf`; must-fire on thread read, delete target and document route.
   - A RootLinks guard.
   - FULL ArchTests from a normal path. uac-r2 needs a clean count.

   When it's done, send uac-r2 the round 3 diff.
3. **130c:**
   - Pass 1 found three F findings:
     - F1-a: values projected to the firm are only those extracted from its own invoice; no ids.
     - F3: tests use the real writer's JSON shape.
     - F1-b: RequiredData names the missing field.
   - It also found K1–K4.
   - **Fix round 1 is DONE** at `adb765caa3` on `task/ebilling-130c` (filtered 177/0, full ArchTests 2183/0). Side effect to decide after pass 2: credit-test reads now show as empty-valued entries on ApprovedRate findings (one-line restriction to RequiredData if wanted).
   - **Pass 2 DONE:** no disclosure path; "has a `field`" boundary holds for every reject-capable reason; keep the empty-value rule (credit-test placeholders can't reach a rejecting finding). F1: the 200 cap truncates silently. K1 AI-assigned skip, K2 merge-fragile `approvedRate.scope` test, K4 line-number wording, K5 wrong note + reachability test, K6 surrogate cut / per-entry catch. K3 = owner question 4b.
   - **Fix round 2 DONE** at `9a84a1f346` (build 0 err, ArchTests 2183/0, filtered 2403/0): additive `FirmOutcome.NotShownCount`, AI-assigned values dropped, per-entry fault isolation, surrogate-safe cut, reachability tests via `SubmissionRejectionRule.Decide`, notes corrected. Agent worktrees `agent-aff95c719a54a6e5b`, `agent-acfabc1702a61b70f` to remove at batch.
   - Diff `ecf694a8d1..9a84a1f346` sent to uac-r2: **approved, no F** (subject to the re-check). uac-r2 relays owner Q4/Q4b. Scoped Sonnet re-check of `adb765caa3..9a84a1f346` running.
   - Fix-2 re-check DONE: no F → uac-r2 approval stands.
   - **Fix round 3 (Sonnet) is RUNNING** on `task/ebilling-130c-f3`, fast-forwarding `task/ebilling-130c` from `9a84a1f346`. Scope: the guard below; a case-insensitive AI-assigned skip; dropped rejecting entries (malformed / refused / undecodable) counted in `NotShownCount`; one vacuous assertion fixed. The guard is uac-r2's hardening item: The guard is a catalogue-driven test: every `BillingRuleCatalogue.RejectCapableReasons` handler runs through `BillingHandlerKit.Run` with reference data present, and no reference-sourced value may carry `field`; a reason with no fixture fails. Send uac-r2 the round 3 diff.
4. **Then F3b** (one agent, Opus/Sonnet-xhigh), on a base that merges 130-0 fix tip + 130-0b `0f6b49b5ab`. Scope:
   - The four named descendants: analysisoutput, eventlog, fileversion, and document via parentdocument.
   - The child document is decided by EVERY invoice link up its parent chain, reusing the 188 hop bound (≤3, a cycle fails closed).
   - The registry-derived edge set, as listed in the 130-0b fix1 report (in the 130-0b notes).
   - K-a: a guard over EVERY Entity.xml lookup to `sprk_invoice`, each one decided or excluded with a reason. `sprk_servicerequest` follows the child rule on both planes, REST workforce routes included.
   - K-b option 1: a regarding pair typed invoice counts as a candidate.
   - The family path: a fault hides the row, not throws.
   - Firms: an unknown firm read fails the call (tool error / 503); no firm hides all; a per-row fault with known firms hides the row. Must-fire for each.
   - K-d: fix the doc comment.

   Then its own review.
5. **Batch merge** (130-0 + 130-0b + 130a/130b + 130c + F3b) into work:
   - Expect conflicts in the notes and `WorkforceRoleCeilingGate.cs`.
   - Use 130-0's `ReadInvoiceSubmissionsWithInvoice` on both GET /invoice-submissions routes, and delete `ReadInvoiceSubmissions` if unused. Put the merged-tip grep in the batch diff note for uac-r2.
   - Replace fix1's `InvoiceLinkedChildAttributes` (ExternalModuleDataEndpoints ~:502) with `ExternalInvoiceVisibility.IsInvoiceLinkedChildTable`.
   - Fix the K-d comment.
   - Verify the tip: build, full ArchTests, filtered.
   - Mark 130-0, 130-0b, 130a, 130b, 130c ✅ in TASK-INDEX.
   - Remove the agent worktrees.
   - **Dev gate:** no part reaches dev until F3b is merged with them.

## Branch heads (2026-11-09)

| Branch | Head | State |
|---|---|---|
| `task/ebilling-130-0-fix1` | `17a283c00c` (3b pushed `ed11b30f95..17a283c00c`: ArchTests 2181/0, filtered 17504 + rerun 706/0; fail-before 55/104). 3c pushed `17a283c00c..be36c99e98` (LiveParentsOf; filtered 19330/0, ArchTests 2181/0; fail-before 54/69). 3d pushed `be36c99e98..abc67173e2`:
- 11 sites fixed.
- Guard `ExternalLookupAbsentReadGuardTests`: 51 entries, `KnownOpenSites = 4`.
- ArchTests 2208/0; rerun 1211/0.

Four known-open sites (admin, revoke and impersonated paths) plus the `sprk_accesspermission` choice column were sent to uac-r2 privately for a ruling (3e or their side). The details are in the 3d notes; never put them in a public issue.

**Now:**
- uac-r2 ruled (privately): every open site is fixed in **round 3e**, which takes `KnownOpenSites` to 0 unless a site is proven unreachable. 3e is running (Sonnet xhigh) from `abc67173e2`.
- uac-r2 takes `ChildAccessPermissionReconciler` into its 198. We send uac-r2 its census entries from the 3e report. Do NOT add the file to the guard until 198 merges.
- Re-check A (`ed11b30f95..abc67173e2`) is DONE with no F. Folded into 3e:
  - `ExternalGrantRow` reclassified, with typed-row presence and a `RowSelect` pin.
  - Select pins added for the membership, NoAccess, thread-regarding and `SelectColumns` reads.
  - Guard: member resolution for expression-bodied members, repo-relative keys, and `CommunicationOpenThreadGate` added.
  - Census additions.
  - Notes and allow-list reasons rewritten as neutral hardening.
- **3e part 1** is pushed as `abc67173e2..21bdb6076e`. It covers rulings 1/2/3/5. `KnownOpenSites` is 0, ArchTests 2208/0, and fail-before is 34.
- **3f** is pushed as `21bdb6076e..c5c30a9fc4` (ruling 4: an absent `sprk_accesspermission` maps to `Unreadable`; pins added; 3 double sites; ArchTests 2208/0; fail-before 13 of 39). **uac-r2 CONFIRMED the Unreadable mapping** (2026-11-09) and asked that the guard's stated misses name choice columns (relayed to 3g). It will add the :419-420 projection pin in 198.
- **3g** is running (Sonnet high, agent worktree) from `c5c30a9fc4`. It pushes FF to `task/ebilling-130-0-fix1`.
- **Then 3g:**
  - `ExternalGrantRow` presence plus a `RowSelect` pin.
  - Guard re-keying: real member names, repo-relative keys, and an expression-bodied control.
  - Census additions: `RightsThroughParents`, `CommunicationOpenThreadGate`, and a note on `LookupValueAttribute`.
  - Neutral rewording of the remaining "gives fewer grants" reasons.
  - Pin the `ReadVisibleThreadRegardingAsync` select.
- **Then:** re-check B over `abc67173e2..<3g tip>`. Then send uac-r2 `ed11b30f95..<3g tip>`. Then 130-0 is done.
- uac-r2 is reachable again by name `uac-r2` (its session restarted). The pending 3e/3f message was delivered 2026-11-09. Agent worktrees `agent-ad9cdf00a103ae635` (+3c) to remove at batch. Earlier: | Round 3 pushed (`7a7f5d2808..ed11b30f95`; ArchTests 2181/0, filtered 7130/0). uac-r2 approved round 3 (subject to re-check no F). **Round 3b** (Sonnet) running on `task/ebilling-130-0-fix3b` → ff fix1 from `ed11b30f95`: DocumentRootLinks absent column → malformed/unreadable; `/data/record/{entity}/{id}` uniform refusal (missing / gone / out-of-set). Then scoped re-check, then the 3b diff to uac-r2 → 130-0 done. Round 3 re-check DONE: no F (uac-r2's condition met). Folded into 3b: uniform per-invoice fault notion (AnyFaulted/IsFaultedFor on lists, submission single read, redaction); confirm the `/data/record` gate applies the per-invoice decision; registry validator early-return gap. For the batch note to spa-r4: `ProjectPage.tsx:~465` 404 branch is now dead (stale grant → 403). Agent worktree `agent-a56d6943daafbbf5e` to remove at batch. |
| `task/ebilling-130-0b` | `0f6b49b5ab` | FINAL, approved |
| `task/ebilling-130b-fix1` | `2367fbab2c` | **FINAL, approved by uac-r2.** At the batch merge, derive `SubmissionCoreColumns` from the submission core column names `InvoiceSubmissionContracts` (:69) already maps from `InvoiceCoreRecordKind`, or add a one-line comment pointing at it. |
| `task/ebilling-130c` | `3a49f67f65` (round 4: value allow-list; ArchTests 2183/0, filtered 2422/0) — **CLOSED: uac-r2 approved rounds 1–4, no F. FINAL.** — earlier `a357a5c7e1` | Built on `ecf694a8d1`. **Done:** both Opus passes and 3 fix rounds; uac-r2 approved with no F. Round 3 adds the catalogue-driven value-boundary guard, and its diff has been sent. Agent worktree `agent-a0af46b7a8db4e045` to remove at batch. It will need 130b's later fixes merged at batch time. |

## Rulings in force (uac-r2, 2026-11-09)

- **REST, firm read fails:**
  - Lists and collections: 200, every invoice-linked row hidden, `truncated: true`.
  - A single invoice-linked record: 503 `InvoiceUnreadableReasonCode`, never a 404 or 403. A single non-invoice record: served.
  - K1 invoice routes and F1 writes: 503.
  - read_query: tool error.
- **No firm (a decided answer):** hide all, with no error.
- **Firm-outcome filter (130b):**
  - Unknown firms: 503. No firm: 403.
  - Core union kept (matches the module's OR, D2 option A).
  - The secure-roots rule is applied explicitly to the invoice's cores AND the submission's own row.
  - The caller boundary on `FirmFindingsProjection` is pinned by an ArchTest.
- **K3 WA:** WA is in the invoices module's ReadableColumns, in the same forms as matter/project. A row under a secure, unreadable WA never returns.
- **130c policy, open for the owner:** with the firm flag off, the rule set name is HIDDEN today, per FR-28 and the 130b ruling. D-79's wording says the reason plus the rule set name. It's a small change if the owner wants the name.

## DEF-059 — DONE (2026-11-09)

- **Role:** `Spaarke Decision Record Reader`, one privilege: DR Read at Global.
- **Dev:**
  - Carrier imported, added to SpaarkeMaster, carrier record deleted.
  - Four platform-added SharePoint privileges were stripped.
  - Assigned to the BFF app registration and UAMI app users; `-Verify` exits 0. Smoke worklist 200.
- **Merged:** `1678e1ae85`; #2635 closed.
- **provisioning-r1:** accepted the H10 and role-expectations work, which waits on the owner's go. It is owed the master SHA plus the SpaarkeMaster version that carries the role.
- **Ontology Service removal:** belongs in the SA-removal work. That first needs minimal Assign/Share for the reconciler re-own, GateDecisionRecordSharing, and maybe Repair on Signal. `SpendReaderSourceAccessGuardTests.cs:25` moves then.
- **Dev identity-hygiene question:** sent privately to uac-r2.

## Filed this session

DEF-063 #2642 (Import-SolutionScoped exits 1 when there is nothing to publish), DEF-064 #2643 (guard: every BFF writer of invoice core lookups calls InvoiceFamilyCoreRecordGuard), DEF-065 #2644 (BillingFactLoader half only; waits for 198's `IsSecureOwnerTeamAsync` on master, no local copy).

## Owner answers 2026-11-09 (typed in THIS session)

1. **166 go:** applied on dev, `-Verify` passed (notes 166 section 7). Follow-up filed as DEF-066 #2651 (a new business unit's default team doesn't get the Readers profile).
2. **`FinanceBaseValuesSeamTests` live: OK.** SpendQueries passes. RowReads fails before writing anything because dev lacks `sprk_invoicesubmission.sprk_trackingscope` (095c-1 schema). Rerun after 095c-1's dev apply. AuthorableColumns live guard passes (tell ontology-r1).
3. **DEF-061 go:** agent running on `task/ebilling-def061`. It does the column, writer, resolver and backfill, `-WhatIf` only; I run the live apply. Then ontology-r1 reviews the resolver.
4. Answered in uac-r2's session (relayed): EB-D-209 (rule set name hidden when the flag is off) and EB-D-210 (firm-safe reason text table, option C) → **130d** running on `task/ebilling-130d`. The DEF-060 writer is in scope: design first, to uac-r2 and ontology-r1.
5. **provisioning H10: go.** b8 forwarded it to `provisioning-r1`, which is NOT reachable by that name from here. The 166 grants message is unsent: resend when it appears.
6a. **120 role change:** already done live on 2026-10-23 (task 120 ✅). Nothing to do.
6b. **116 `sprk_sourcedocument` delete:** yes, if it's unused. Task 116 retires the readers and writers on `sprk_ruleset`/`sprk_ratecard` (the `sprk_storedfile` column stays). The delete script is read-only by default, and the delete runs after 079. Confirm zero code use and zero live data (non-null counts) before deleting.
- Owner also asked: **move the uac-r2 review cycle as fast as possible.** Use split diffs: diff 1 `ed11b30f95..c5c30a9fc4` was APPROVED by uac-r2 with no F (nit checked: a malformed body in `ParseRows` → caught at :795 → the chunk is denied). Diff 2 = 3g, diff 3 = F3b.

## Owed to peers

- provisioning-r1: the master SHA plus profile grants after 166; the 089d SHA; the DEF-059 master SHA plus SpaarkeMaster version.
- word-add-in-r1: the 095c-2..4 dev deploy master SHA; the 095a/095b/151e PR plus SHA.
- uac-r2: the 165e/165f batch sha; the 130b fix6+7 diff; the 130-0 round 3 diff; the 130c diff; F3b; the batch diff note.
- ontology-r1: AuthorableColumns counts after 166; the DEF-059 outcome (not delivered).

## Standing gates (unchanged)

- Creator-person `-Verify` exits 0 before any BFF deploy.
- Reconcile stays OFF until DEF-044 is on master AND L6 passes.
- No stamp gets a BFF with 092+ before SpaarkeMaster 1.3.0.0, nor with 095a before 1.3.0.1.
- The AuthorableColumns full-green after 166 goes to ontology-r1 before any BFF carrying 165b-4 reaches dev.
- One master PR; reviewers ontology-r1, uac-r2, R4.
