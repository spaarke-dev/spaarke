# Current Task State

> **Last Updated**: 2026-11-08 (session ebilling-r1-c7, authoritative). Work head = `origin/work/ebilling-r1` @ `8bd27cab66` (165b-4 and 085 merged, pushed). Dev BFF: uac-r2's `6fbb3a7642` (master 23567b2f93 + fm 049c + MCP spike; reconcile false). **None of this project's work is on master or the dev BFF.**

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Mode** | Owner: "keep progressing on open tasks so we can complete the project". At most 4 agents. Merge each finished branch after its reviews; verify the combined tip (build, full ArchTests, FILTERED unit). One adversarial pass per change set; two for partner-readable / auth / tenant-isolation; a scoped Sonnet re-check when a fix changes the approach. Every agent launch states a model. Never `--no-verify`. |
| **Agent worktrees** | `isolation: worktree` starts on MASTER: every brief names the base sha and checks `git merge-base --is-ancestor`. Never cd into agent worktrees (use `git -C`, absolute paths). Remove merged worktrees with `git worktree unlock` + `scripts/worktree/Remove-WorktreeSafe.ps1 -Path <paths>`. |
| **Public-repo rule** | Never put an access-gap mechanism, identity names or role maps in notes, comments, commits, PRs or issues. Private findings go to uac-r2 by message only. |
| **Peers (sessions)** | uac-r2 = desktop `uac-r2`; ontology-r1 = `ontology-r1` (Claude Desktop, from 2026-11-09); provisioning = `provisioning-r1`; word-add-in = `word-add-in-r1`; spaarkeai-mcp-bff-api-r1 = `mcp-r1`; External Access R4 = `spa-r4`. |
| **Live-change rule** | Live Dataverse writes / role changes need the owner's OK typed in THIS session. A relayed "task 166 - ye proceed" (via ontology-r1) was NOT accepted; owner asked directly, no answer yet. |

## Exact next action

1. **DEF-059 (owner typed in THIS session, 2026-11-09):** "review the options and implement the one that is the best solution without unnecessarily adding more roles -- but if the only way to meet the requirement and stay within our access control is to create a new role that is acceptable." ontology-r1 proposed C (new role, one privilege: org-wide Read on `sprk_decisionrecord`) vs A (nothing; safe while System Administrator stays on the BFF identity). FIRST check (read-only) whether the BFF identity already holds a Spaarke role held by NO human and NOT by the writer identity; if yes, adding the one org Read privilege there beats a new role. Implement via SpaarkeMaster solution source + `Import-SolutionScoped.ps1` only; uac-r2 notices before and after; tell ontology-r1 and provisioning-r1 (stamps). Update DEF-059 (#2635) notes + issue.
2. When 130-0b fix1 sends the shared redaction-set sha, relay it to 130-0 fix2 to merge (exact sha only).
3. Then per agent table below; check `git worktree list` for a leftover reviewer worktree `...scratchpad
v2` (task `bgz2bshd9` output) and remove with `Remove-WorktreeSafe.ps1` if listed.

## DEF-059 status (2026-11-09)

- **Step 1** is on `task/ebilling-def059` at `ad59d94a2f`, `f0b5ed1112` and `f764a7fb3c`. **uac-r2 APPROVED** the shape.
- **Ruling:** the pin stays STRICT. `-Apply` FIRST strips this role's privileges outside {DR Read Global}, reads back, then assigns. It exits 2 when the role is missing, or the declared privilege is missing or has the wrong depth. A Sonnet agent is pushing this ff onto `task/ebilling-def059` (`task/ebilling-def059-f1`).
- **Pre-check done (read-only, dev):** SpaarkeMaster is Unmanaged at 1.2.3.1; the role is not present yet.
- **Step 3** runs after the script fix:
  1. uac-r2 before-notice.
  2. Carrier solution `SpaarkeDecisionRecordReaderRole` (SpaarkeMaster publisher, one type-20 component).
  3. `pac solution pack`, then `Import-SolutionScoped.ps1 -SolutionUniqueName SpaarkeDecisionRecordReaderRole`.
  4. AddSolutionComponent (type 20) → SpaarkeMaster.
  5. DELETE the carrier solution record. Its components stay.
  6. Script `-Apply`, then `-Verify`, which must exit 0.
  7. One DR smoke read through the BFF DR read path.
  8. uac-r2 after-notice, saying the carrier was deleted.
- **Done:** provisioning-r1 told about H10 and the role-expectations file. It accepted the work, which waits on the owner's go. **Owed to provisioning-r1:** the master SHA once def059 merges, plus the SpaarkeMaster version carrying the role.
- **Owner question (open):** remove Ontology Service from the BFF identity? uac-r2 says it belongs in the SA-removal work, which first needs minimal Assign/Share on DR/Signal (reconciler re-own, GateDecisionRecordSharing, and maybe Repair on Signal). `SpendReaderSourceAccessGuardTests.cs:25` moves when Ontology Service comes off.

## Agents in flight (2026-11-09)

| Work | Branch / base | Next |
|---|---|---|
| 130-0 fix round 2 (Opus) | `task/ebilling-130-0-fix2` → ff `task/ebilling-130-0-fix1` (from `4102b719b9`) | F1 writes, F2 truncated, F3, K1s, K2, K3 (WA parent + `ExternalAccessModule.cs` ScopeDimension/RootLinks/LiveParents, uac-r2 approved), create-shape pin, REST heads-up (number/type cols, link-col filters, descendants). Must MERGE 130-0b's shared redaction-set commit (sha to relay) and use it. Then Sonnet re-check, diff to uac-r2. |
| 130-0b fix round 1 (Opus) | `task/ebilling-130-0b-f1` → ff `task/ebilling-130-0b` (from `0acfebe470`) | uac-r2 rulings: F1-1 shared derived redaction set FIRST (own commit, sha to lead), F1-2 column-name refusal incl. record type, neutral text; F1-3 descendants + guard; K2; R1 row cap on visible rows + fixed note; R2 design statement + census line (sent). Then pass 2 over combined seam, diff to uac-r2. |
| 130b small fix (Sonnet) | `task/ebilling-130b-fix3` → ff `task/ebilling-130b-fix1` (from `d303a11e5d`) | Re-check passed except F2 (accepted-without-invoice Warning+code on undecided path, route test) and K4 (reason order). Then diff to uac-r2 (incl. gate hunk), merge 130a+130b, 130c. |

**uac-r2 rulings 2026-11-09:**
- **F3 → F3b, one task and one agent after the 130-0b tip:**
  - **Scope:** the four named descendants plus the registry-derived edge set, which the 130-0b report lists.
  - **Guard:** every edge is decided or excluded with a one-line reason.
  - **Family path:** a fault hides the row instead of throwing.
  - **uac-r2 ruling on the three cases, each with a must-fire test:**
    - **No firm** (decided): hide all invoice-family rows; no error.
    - **Unknown** (an identity or binding fault, or a failed firm read): fail the whole call (a tool error / K1 503).
    - **A per-row seam fault with known firms:** hide that row.
  - **REST when the firm read fails (uac-r2 (B), relayed to 130-0 fix2):**
    - Lists and collections: 200, hide every invoice-linked row, `truncated: true`.
    - A single invoice-linked record: 503 `InvoiceUnreadableReasonCode`, never 404. A single non-invoice record: 200.
    - K1 invoice routes and F1 writes: 503.
    - read_query: tool error.
  - **Also in F3b (uac-r2 2026-11-09, from 130-0b pass 2):**
    - **K-a, now F:** a guard over EVERY Entity.xml lookup to `sprk_invoice` on any table. Each lookup is decided (a child → hide, a root → redact) or excluded with a reason. `sprk_servicerequest` follows the child rule on BOTH planes, REST workforce routes included.
    - **K-b, option 1:** a regarding pair typed `sprk_invoice` with a GUID id is a candidate invoice alongside `sprk_regardinginvoice`. Tests: the pair is hidden and the link is null → hide; the pair is visible and the link is hidden → hide.
    - **K-d:** fix the `RootInvoiceLinkRedaction` doc comment in the batch merge.
  - **Review:** its own review.
  - **Gate:** neither 130-0 nor 130-0b reaches dev until F3b is merged with them.
- **Faults:** a fault on a decided row hides that row, on the family path too. A whole-read failure still fails the call: a 503 on REST, a tool error in read_query.
- **K3:** WA in the invoices module's ReadableColumns is accepted, on three conditions: sibling-exact forms; a test that a secure, unreadable WA row never returns; and its own diff item.
- **Shared redaction set:** `77d6becd24`; 130-0 merges that exact sha.

**130-0 fix round 2 is pushed:** `4102b719b9..7a7f5d2808` on `task/ebilling-130-0-fix1`. A scoped Sonnet re-check is running; the diff goes to uac-r2 after it. uac-r2 rulings so far:
- **Q1 → F3b.** A child document is decided by EVERY invoice link up its parent chain. It reuses the 188 hop bound (≤3; a cycle fails closed). Test on both planes.
- **Q2 → fix round 3.** A single invoice-linked record whose decision faults, whether the firm read or the per-invoice check, returns 503 `InvoiceUnreadableReasonCode`. A decided hidden record keeps the uniform refusal.
- **Q3 → K.** One line in the notes for the deactivated WA.
- **Fix round 3 also covers** the `/todos/{id}` single-record routes and their siblings in the same file: a missing id must give the uniform 403 with the same body as another firm's id, for each verb. Neutral commit text. uac-r2 authorized it.
- **Fix round 3 starts after the re-check** and bundles its findings.

**130b review by uac-r2 (fc68d0b44a):** approved once FIX-1, FIX-2 and the Q1 answer land.
- **FIX-1:** a firm-read fault returns 503; "no firm" stays 403.
- **FIX-2:** an ArchTest pins that only the endpoints and DI may call FirmFindingsProjection.
- **Q1:** is "exactly one core record" enforced? If yes, cite it; if not, use the invoices-module precedence.
- **Status:** a Sonnet agent is on the `task/ebilling-130b-fix4` branch and will push onto fix1. Q2 and Q3 are accepted.
- **Batch diff note:** must include the merged-tip grep for `ReadInvoiceSubmissions`, showing a single constant.
- **130c starts after fix4.**

**Merge plan for the 130 batch:** merge 130-0 fix (incl. 130-0b helper `446a40bb4e`), 130-0b head, 130b fix1 into work; expect conflicts in the 130-0 notes file (combine both sections by hand) and `WorkforceRoleCeilingGate.cs` (adjacent sets; remove `ReadInvoiceSubmissions` if unused); after merge replace fix1's `InvoiceLinkedChildAttributes` (`ExternalModuleDataEndpoints.cs` ~:502) with 130-0b's `ExternalInvoiceVisibility.IsInvoiceLinkedChildTable`. Verify tip, TASK-INDEX 130-0/130-0b/130a/130b ✅, remove agent worktrees.

## Decisions made this stretch (2026-11-08)

- **165b-4** merged `5ebe26a03d` + K1/K2 `24641fdf1a`; tip clean, filtered 2167/0, ArchTests 2189/0; TASK-INDEX ✅; ontology-r1 told. **Deploy gate:** AuthorableColumns full-green on dev after 166's apply, counts to ontology-r1, before any BFF carrying 165b-4 reaches dev.
- **085** merged `052307dc0b` (uac-r2 + ontology-r1 approved; ontology D-213); native-write K-line in notes (uac-r2's service-role condition checked read-only on dev: holders are all application users; detail sent privately); tip clean, filtered 1531/0, ArchTests 2189/0; TASK-INDEX ✅; ontology-r1 told about `approvedRate.rank`.
- **D-70 final owner ruling** (relayed by uac-r2): own firms only; no firm sees none; a fault hides all. In project CLAUDE.md.
- **130 split**: 130-0, 130-0b, 130a (built), 130b, 130c. **130-0 scope widened** by uac-r2 (same batch).
- **085 F1-b**: ruling (c); durable fix DEF-061 (owner-gated schema).
- Filed: DEF-060 #2637, DEF-061 #2640, DEF-062 #2641.

## Open owner questions

1. Typed go for task **166** dev apply (field security incl. invoice totals + forms), with uac-r2 before/after notices, `Import-SolutionScoped.ps1` only.
2. OK to run `FinanceBaseValuesSeamTests` live (creates and sweeps `zz-025b-` rows on dev).
3. DEF-059 (#2635): ANSWERED 2026-11-09 (see Exact next action 1).
4. DEF-061: schema change `sprk_publishedon` on `sprk_procedureversion`.

## Owed to peers

- provisioning-r1: master SHA + profile grants on `sprk_totalamount`/`sprk_approvedtotal` after 166 merges; 089d option R SHA (H7c #2429).
- word-add-in-r1: master SHA of the dev deploy carrying 095c-2..4 (pane PR #2552); 095a/095b/151e PR + SHA.
- uac-r2: dev-batch sha for 165e + 165f; 130-0 (+fix1) / 130-0b / 130b diffs after their passes.
- ontology-r1: AuthorableColumns full-green counts after 166.

## Standing gates (unchanged)

Creator-person `-Verify` exits 0 before any BFF deploy. Reconcile OFF until DEF-044 on master AND L6 passes. No stamp gets a BFF with 092+ before SpaarkeMaster 1.3.0.0; none with 095a before `sprk_sourceemaildocument` is field-secured (1.3.0.1). One master PR, reviewers ontology-r1, uac-r2, R4 (`spa-r4`).
