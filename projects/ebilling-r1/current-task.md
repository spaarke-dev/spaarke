# Current Task State

> **Last Updated**: 2026-11-09 (session ebilling-r1-c7, authoritative). Work head = `origin/work/ebilling-r1` @ `35dc4dafd1` (DEF-059 merged at `1678e1ae85`). Dev BFF: uac-r2's `6fbb3a7642` (reconcile false). **None of the 130 batch is on work, master or the dev BFF.**

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Mode** | Owner: "keep progressing on open tasks so we can complete the project". At most 4 agents. Merge each finished branch after its reviews; verify the combined tip (build, full ArchTests, FILTERED unit). One adversarial pass per change set; two for partner-readable / auth / tenant-isolation; a scoped Sonnet re-check when a fix changes the approach. Every agent launch states a model. Never `--no-verify`. |
| **Agent worktrees** | `isolation: worktree` starts on MASTER. Every brief names the base sha and checks `git merge-base --is-ancestor`. Never cd into agent worktrees; use `git -C` and absolute paths. To remove one: `git worktree unlock`, then run `Remove-WorktreeSafe.ps1 -Path <one path>` from the **PowerShell tool**, one path per call (bash mangles the backslashes). |
| **Public-repo rule** | Never put access-gap mechanisms, identity names or role maps in notes, comments, commits, PRs or issues. Private findings go to uac-r2 by message only. |
| **Peers** | uac-r2 = `uac-r2`; ontology-r1 = `ontology-r1` (messages to it were HELD/expired — not delivered; resend only if owner asks); provisioning = `provisioning-r1`; word-add-in = `word-add-in-r1`; mcp = `mcp-r1`; R4 = `spa-r4`. |
| **Live-change rule** | Live Dataverse writes / role changes need the owner's OK typed in THIS session, plus uac-r2 notices before and after. |

## Exact next action

Wait for the agent notifications below and act on each:
1. **130b fix7** (Sonnet): K2 makes the second submission read self-sufficient (Read on one of `row`'s cores), K4 adds a row-gone test, K3 rewords the notes into neutral language. When done, send uac-r2 the 130b diff `e991dba7f8..<fix7 head>`. That covers fix6 `42cfdbce1b`, with the re-check finding no F, plus fix7. uac-r2 then finishes the 130b sign-off.
2. **130-0 fix round 3** (Sonnet, branch `task/ebilling-130-0-fix3` → ff `task/ebilling-130-0-fix1` from `7a7f5d2808`). It covers:
   - Q2: a single invoice-linked record whose decision faults (the firm read or the per-invoice check) gets a 503.
   - A uniform 403 for a missing id on every verb of the single to-do and sibling routes.
   - The Q3 note.
   - Re-check hygiene.
   - F (uac-r2): `InvoiceLinksOf` JsonElement overload — an absent key means `UnreadableLink`; pin that FetchXML callers select `LinkColumnsOf`; must-fire on thread read, delete target and document route.
   - A RootLinks guard.
   - FULL ArchTests from a normal path. uac-r2 needs a clean count.

   When it's done, send uac-r2 the round 3 diff.
3. **130c review pass 1** (Opus adversarial on `ecf694a8d1..77a0f8f312`, branch `task/ebilling-130c`). After it, fix any F, run pass 2 (partner-readable, so two passes), then send the diff to uac-r2.
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
| `task/ebilling-130-0-fix1` | `7a7f5d2808` | Round 2 approved by uac-r2, with one F going into round 3. Round 3 is running. |
| `task/ebilling-130-0b` | `0f6b49b5ab` | FINAL, approved |
| `task/ebilling-130b-fix1` | `42cfdbce1b` | Fix rounds 4 and 5 approved. Fix 6 was re-checked with no F. Fix 7 is running. |
| `task/ebilling-130c` | `77a0f8f312` | Built on `ecf694a8d1`. Pass 1 is running. It will need 130b's later fixes merged at batch time. |

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

## Open owner questions

1. Typed go for task **166** dev apply (field security incl. invoice totals + forms).
2. OK to run `FinanceBaseValuesSeamTests` live (creates/sweeps `zz-025b-` rows on dev).
3. DEF-061: schema change `sprk_publishedon` on `sprk_procedureversion`.
4. 130c: show the rule set name when the firm flag is off (D-79 wording) or keep it hidden (current).
5. provisioning-r1 needs the owner's go to open its DEF-059 H10 task.

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
