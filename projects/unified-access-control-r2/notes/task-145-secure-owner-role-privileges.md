# Task 145 — the Secure Record Owner role covers every child table UAC-r2 re-owns (GitHub #1046)

> **Date**: 2026-10-01 · **Branch**: `task/uac-r2-145` (from `task/uac-r2-144-f1`) · **Rigor**: FULL
> **Binding inputs**: owner rounds 1, 2, 3, 3b (`session27-owner-decisions-and-research.md`); POML amendment R3.
> **This run made NO live writes.** Every Dataverse call below is a GET. Each change to the live role is listed in §4
> as a pending manual gate with its exact commands.

## 1. Tracking (step 1)

| Item | State at start (2026-10-01) | Source |
|---|---|---|
| word-add-in-r1 task 082 | `completed-with-escalation`; on master | `projects/spaarkeai-word-add-in-r1/tasks/082-…poml` (origin/master) |
| 082's codified set + script + guide rows | merged in **PR #1051** (2026-09-30 23:44Z) | `config/secure-record-owner-role.json`, `scripts/Set-SecureRecordOwnerRolePrivileges.ps1`, guide §5.1/§5.3/§5.4/§7 |
| PR #1045 (RecordOwnershipResolver wiring, #1038/#1043) | merged 2026-09-30 23:40Z | `gh pr view 1045` |
| **#1046** | **CLOSED** 2026-09-30 23:44Z by the peer; last comment records F1 (32 drift privileges removed, 40 → 8) | `gh issue view 1046` |
| Drift decision (F1) | **asked once, jointly with 082; answered** "yes can remove them if not needed"; done by the peer 2026-10-01 | peer note 082 §4.1; session27 round 3b |
| Peer exchange | **NOT DONE — open (verifier findings 7/11).** The peer session (`spaarke-wt-spaarkeai-word-add-in-r1-54`) is live and busy. Neither the start-of-task message (POML step 1: "message the word-add-in-r1 session with this task's plan") nor the close message was sent from the sub-agents: no live write was made here, so nothing raced the shared role, but the AC requires the exchange at start AND at close. The main session sends both — the plan message (text in §8a) before gate G1 runs, the outcome after it. Until then the TRACKING criterion is **partially met** | — |

The first escalation trigger (082 not landed) did **not** fire: 082 is live and merged.

## 2. The live role, re-measured (read-only, spaarkedev1, 2026-10-01)

| Fact | Value |
|---|---|
| Secure Record BUs | **1** (`d9ec0b6f-80a0-f111-aaac-000d3a99d1d7`, parent = root `06fbf21c-…`) |
| `Secure Record Owner` copies | **1**, a root role in the secure BU (`e4ebabd9-b4a0-f111-aaac-000d3a99d1d7`) — not a replica |
| Privileges held | **8**, every one `Read` at `Basic`: project, matter, workassignment, document, todo, communication, event, memo. Nothing outside the file (the F1 strip held) |
| Holders | **1 team: `Secure Record` — the BU's DEFAULT team** (`daec0b6f-…`). 0 users |
| Named team `Secure Record Owners` | **does not exist** — task 144's guide §4.3 cutover has not been run in dev |
| Users in the secure BU | **0** |
| `sharetopreviousowneronassign` | false |
| Secure roots | 1 secure project; **0 of them owned inside the secure BU** (task 144's migration reports it "not isolated") |

**AUTHORIZATION criterion ("held only by task 144's named team") is therefore NOT met live**, and cannot be until the
task-144 cutover runs (a live write — task 144's pending gate, not this task's).

## 3. The table set (step 2)

Rule (POML constraint): a table is in the set only when a writer (file:line) or a C10 part-2 task assigns its rows to
the secure owner team.

| Table | Kind | Writer that assigns it to the secure team | In the file | Live role |
|---|---|---|---|---|
| sprk_project / sprk_matter / sprk_workassignment | root | `ProvisionProjectEndpoint` (task 046/144) | ✅ | ✅ Basic |
| sprk_document | child | `OfficeService.cs:412` (save), `UploadFinalizationWorker`, `DataverseDocumentsEndpoints.cs:35` (I-6, record-first) | ✅ | ✅ Basic |
| sprk_todo | child | `OfficeService.cs:2210` (CreateTodoAsync, record-first) | ✅ | ✅ Basic |
| sprk_communication, sprk_event, sprk_memo | child | owner C10 part 2; task 146 (server) / 147 (client) | ✅ | ✅ Basic |
| **sprk_invoice** | child | **task 130 `InvoiceReviewService.CreateInvoiceRecordAsync` — `Services/Finance/InvoiceReviewService.cs:604` on `work/unified-access-control-r2` @ `7275472d9`**: owned by the MATTER's team, record-first, so a secure matter's invoice goes to the secure team. Owner amendment Q12 names invoice explicitly | ✅ **added by this task** | ❌ **missing** — gate §4 |
| sprk_analysis, sprk_communicationthread (record threads), sprk_spendsignal, sprk_spendsnapshot, sprk_emailreviewlog, sprk_analysisoutput, sprk_reportcard, participant-index / artifact rows | child | **none yet.** Task 146's census classifies each and wires the writers; task 147 the client creates; task 148 the backfill. Per the agreed split and 146's own constraint, 146 adds each to the file through the procedure below, with its refusal, in the task that wires the writer | ❌ not yet (correctly: no writer) | — |

Read-privilege names from live metadata (the casing the file must use): `prvReadsprk_Invoice`,
`prvReadsprk_analysis`, `prvReadsprk_CommunicationThread`, `prvReadsprk_SpendSignal`, `prvReadsprk_SpendSnapshot`,
`prvReadsprk_ReportCard`, `prvReadsprk_EmailReviewLog`, `prvReadsprk_analysisoutput` (all UserOwned tables).

Writers on THIS branch that do NOT assign to the secure team (checked): `RecordCreationService.cs:223` and
`OfficeService.cs:1923` (Office invoice) resolve caller-first with no target, so they land in the caller's BU, never
the user-free secure BU.

**Platform cascade (a writer nobody listed).** Live metadata: `sprk_project` and `sprk_matter` cascade **Assign** to
`team`, `sharepointdocumentlocation` and `sharepointdocument`; `sprk_workassignment` cascades nothing. Assigning a
secure root therefore also assigns its SharePoint location rows to the team. dev has **0** `sharepointdocumentlocation`
rows, so this has never run against the 8-privilege role. Whether it is refused now that the SharePoint four were
stripped is UNTESTED — recorded as open item 1 in the handoff note, with the probe that settles it.

**One list, grep recorded**: `secure-record-owner-role` appears in code/config only in
`config/secure-record-owner-role.json`, `scripts/Set-SecureRecordOwnerRolePrivileges.ps1`, the guide, and (new)
the `Sprk.Bff.Api.csproj` link. The BFF embeds the file by LINK; the tests build their fixtures by iterating the
embedded set. No second list exists.

## 4. PENDING MANUAL GATE — grant `sprk_invoice` (LIVE WRITE, not performed)

Order matters: run it **before** task 130's invoice confirm is deployed against a secure matter in dev, or that confirm
fails closed (`OwnerTeamUnresolved` is not what fires — Dataverse refuses the create).

```powershell
# From the repository root. Pin the environment; never an ambient profile (guide §2).
$DvUrl = 'https://spaarkedev1.crm.dynamics.com'; $Api = "$DvUrl/api/data/v9.2"
$tok = az account get-access-token --resource $DvUrl --query accessToken -o tsv
$H = @{ Authorization = "Bearer $tok"; Accept = 'application/json'; 'OData-MaxVersion' = '4.0'; 'OData-Version' = '4.0'; 'Content-Type' = 'application/json; charset=utf-8' }
# The team that HOLDS the role. Today: the default team daec0b6f-80a0-f111-aaac-000d3a99d1d7.
# After task 144's §4.3 cutover: the named team 'Secure Record Owners' (guide §4.2 query).
$teamId = 'daec0b6f-80a0-f111-aaac-000d3a99d1d7'

# 1) NEGATIVE CONTROL — 3 polls ~25 s apart. Expect a refusal naming prvReadsprk_Invoice.
#    REFUSED  -> REPLACE the sprk_invoice entry's "evidence" in config/secure-record-owner-role.json (today it starts
#                "VERBATIM REFUSAL PENDING") with the date, the poll count and the message VERBATIM, and commit that
#                BEFORE step 2. The file's howToExtend rule requires the quoted refusal; the entry was committed ahead
#                of it (verifier finding 5), so this write-back is what makes it compliant.
#    NOT REFUSED in 3 polls -> the premise is wrong (POML AC 3): do NOT run -Apply; remove the sprk_invoice entry in a
#                revert commit, record the result here, and escalate (task 130's confirm then needs no grant).
#    ESCALATE (POML trigger 3) if it names anything other than a Read privilege.
$b = @{ sprk_name = 'task145-probe-invoice'; 'ownerid@odata.bind' = "/teams($teamId)" } | ConvertTo-Json
try { Invoke-RestMethod -Method Post "$Api/sprk_invoices" -Headers $H -Body ([Text.Encoding]::UTF8.GetBytes($b)) } catch { $_.ErrorDetails.Message }

# 2) Dry run (expect: MISSING sprk_invoice), apply, then the guide §5.4 strip — AddPrivilegesRole RE-INJECTS the
#    SharePoint four — then verify (expect exit 0, "Outside the file" empty).
.\scripts\Set-SecureRecordOwnerRolePrivileges.ps1 -EnvironmentUrl $DvUrl
.\scripts\Set-SecureRecordOwnerRolePrivileges.ps1 -EnvironmentUrl $DvUrl -Apply
#    guide §5.4 snippet, with $roleId = 'e4ebabd9-b4a0-f111-aaac-000d3a99d1d7' and $keep read from the file
.\scripts\Set-SecureRecordOwnerRolePrivileges.ps1 -EnvironmentUrl $DvUrl -Verify

# 3) POSITIVE — re-run the step-1 POST until it SUCCEEDS on 3 consecutive polls (principal-privilege cache, guide §7);
#    read back owningteam (= $teamId) with Prefer: return=representation; DELETE every probe row and record each 404.
#    Control in the same polls: a create on a table NOT in the file (e.g. sprk_analysis) must still be refused with
#    privilegeCount=9, proving the probes saw the new role.

# 4) NFR-05 live run — expect clause 5 PASS (9 of 9 at Basic):
$env:SPAARKE_NFR05_DATAVERSE_URL = $DvUrl; $env:SPAARKE_NFR05_REQUIRED = 'true'; $env:AZURE_TOKEN_CREDENTIALS = 'AzureCliCredential'
dotnet test tests/unit/Sprk.Bff.Api.Tests --filter "Category=LiveDataverseRoleDepth" --logger "console;verbosity=detailed"
```

Before/after privilege lists for the acceptance criterion: before = the 8 in §2; after must be those 8 + `prvReadsprk_Invoice`
at Basic, count 9, diff = +1, nothing removed.

**The same procedure is how task 146 adds every table it wires** (negative control → file entry with the verbatim
refusal → dry run → `-Apply` → §5.4 strip → `-Verify` → positive probes → census).

## 5. The census clause (step 5) — built

`SecureBuRoleDepthAssertion` clause 5: the ONE `Secure Record Owner` role in the secure BU must hold Read at `Basic` on
every table of the codified set. Each missing table, or one held wider than Basic, is a finding
`SecureOwnerRoleLacksCodifiedPrivilege` naming the table and privilege; no owner role, two owner roles, a role that is
only a REPLICA of one created in an ancestor BU (added in verifier round 1, §10), or an empty set is a finding too
(never a quiet pass). Same rule as `-Verify`. Ranked below every exposure verdict (it fails closed).

- The set is read from the file itself: `config/secure-record-owner-role.json` is LINKED into `Sprk.Bff.Api` as an
  embedded resource (`SecureRecordOwnerRoleSet.Embedded`), so the scheduled job (deployed, no repo on disk) and the
  manual live gate read the same bytes as the script and the guide.
- Both census readers now read the guarded Reads ∪ the set's privileges. Only the three GUARDED root Reads must come
  back exactly or the census throws (`RequireGuardedPrivilegesResolved` — clause 1 cannot be graded without them). A
  codified privilege the environment does not have by that exact name (table not installed, renamed, or mis-cased in
  the file) is a clause-5 FINDING, not a throw. Code review caught the first version throwing on it: that would have
  blinded clauses 1–4 — the exposure checks — on every run in any environment missing one optional table.
- `SecureRecordIsolationCensusJob` logs clause-5 findings at **Error**, not Critical (fail-closed, not a disclosure).

### Manual live run (read-only), spaarkedev1, 2026-10-01

`SecureBuRoleDepthAssertionTests` live test with `SPAARKE_NFR05_DATAVERSE_URL`, `SPAARKE_NFR05_REQUIRED=true`,
`AZURE_TOKEN_CREDENTIALS=AzureCliCredential`:

> NFR-05 role-depth census for https://spaarkedev1.crm.dynamics.com: 6 business unit(s), 282 effective grant(s) …
> (33 held by humans), 1 holder(s) of 'Secure Record Owner', 0 team(s) named 'Secure Record Owners' with 0 member(s)
> of any kind, 0 systemuser(s) in 'Secure Record', **1 owner role(s) in it covering 8 of 9 codified table(s) at
> Basic**. Verdict: HumanPrincipalReachesSecureBusinessUnit

18 findings:

| # | Verdict | Principal / object |
|---|---|---|
| 1–6 | HumanPrincipalReachesSecureBusinessUnit | **Chelsea Friez** (`chelsea.friez@demo.spaarke.com`) — `Spaarke Core User` and `Spaarke Office Add In User`, Read on project/matter/work assignment at **Deep from the root BU** |
| 7–12 | HumanPrincipalReachesSecureBusinessUnit | **Lori Witkin** (`lori.witkin@demo.spaarke.com`) — same two roles, same reach |
| 13–15 | HumanPrincipalReachesSecureBusinessUnit | `ralph.schroeder_hotmail.com#EXT#` — `Spaarke Basic User` (owner F11: the owner will change this account; no exception added) |
| 16 | SecureOwnerTeamNotResolved | 0 teams named `Secure Record Owners` (task 144 cutover pending) |
| 17 | SecureOwnerRoleHeldBeyondOwnerTeam | the BU's DEFAULT team holds the role (task 144 cutover pending) |
| 18 | **SecureOwnerRoleLacksCodifiedPrivilege** | **`prvReadsprk_Invoice` (sprk_invoice)** — the new clause, failing the right way on live data (§4 gate) |

Clause 5's two directions are therefore recorded: **FAIL naming the table** live (finding 18) and in seeded unit tests;
**PASS** with the full set in seeded unit tests. The live PASS direction is the §4 gate's step 4.

### 🔔 NEW EXPOSURE — two users created 2026-10-01 read every secure record (escalation; owner decision)

Chelsea Friez (created 2026-10-01 01:02Z) and Lori Witkin (created 2026-10-01 02:44Z) are enabled, interactive
systemusers in the **root BU `Spaarke`** holding `Spaarke Core User` + `Spaarke Office Add In User`, whose Read on
project/matter/work assignment is **Deep** — reaching the Secure Record BU (a child of root). They were not present in
the peer's 2026-09-30 census. They read the dev secure project today. This is design §5.2's hole, reopened by user
placement, exactly what NFR-05 exists to catch.
- Not fixed here: relocating users (or narrowing role depth) is an owner decision (task 144; guide §6), and any fix is a
  live write.
- Options for the owner: (a) move both users into a non-ancestor BU (e.g. `Spaarke Business Unit 1`), the Fix-A shape
  design §5.2 validated; (b) narrow the two roles from Deep to Local (Fix B — affects every holder); (c) accept for dev
  with a dated note. **Recommendation: (a)**, and find what created them in the root BU (registration / demo seeding) so
  the next user does not land there — if a provisioning path places users in root, that is the defect.

## 6. The guide re-run (criterion: re-running §5.3 then §5.4 removes nothing in the set)

Verified by reading, no live run needed for the logic: §5.3's `$securable` and §5.4's `$keep` both read
`config/secure-record-owner-role.json` (`tables.logicalName` / `tables.privilegeName`), so the strip keeps every file
entry, including the new `prvReadsprk_Invoice`. §5.1 and §7 point at the file. The peer's read-only simulation (note
082) showed the old literal list would have removed `prvReadsprk_Document`; the file-driven `$keep` keeps all of them.
This task updated §7 rows 1 (stale "32 pending" — the strip ran 2026-10-01; 9 tables now), 9 and 11 (clause 5).

## 7. New environments (step 6) — owner F10 = (a), handed off

`notes/handoffs/INCOMING-145-secure-setup-handler.md`: a control-plane handler (`H7b`, `[H7b]={H6}`, H13 waits on it)
with inputs, read-then-write steps S0–S9, the §5.4 strip after every grant, idempotency key over a hash of the set,
dry run, rejection codes, tests, and the open cascade/SharePoint item. UAC-r2 does not edit the control plane.

## 8. Closing #1046 (step 8)

#1046 is already closed (by the peer, citing 082). Nothing was posted from this sub-agent. For the main session to
post once (it cites both tasks):

> unified-access-control-r2 task 145 (follow-up to word-add-in-r1 task 082): the NFR-05 census now has clause 5 — the
> `Secure Record Owner` role must hold Read at Basic on every table in `config/secure-record-owner-role.json`, read from
> that file (embedded in the BFF; the scheduled census job and the manual gate both run it). `sprk_invoice` was added
> to the file for task 130's team-owned invoice confirm (owner Q12); the live grant is a pending manual gate. New
> environments get the setup through a control-plane handler (owner F10), designed in
> `projects/unified-access-control-r2/notes/handoffs/INCOMING-145-secure-setup-handler.md`.

### 8a. Peer messages for the main session to send (word-add-in-r1 session)

**At start (before G1 runs)** — "UAC-r2 task 145 plan for the shared `Secure Record Owner` role: one table added to
`config/secure-record-owner-role.json` (`sprk_invoice`, for task 130's team-owned invoice confirm, owner Q12). Live
sequence (G1): negative control → verbatim refusal written into the entry → script dry run → `-Apply` → guide §5.4
strip (re-injected SharePoint four) → `-Verify` → positive probes ×3 → NFR-05 run. Role goes 8 → 9, all Read at
Basic; nothing removed. Census clause 5 now grades the role against the file. Please say if you have a pending change
to the role or the file so we do not race."

**At close (after G1)** — the before/after lists, the verbatim refusal, the probe results and the clause-5 PASS
output, plus the #1046 comment link (§8).

## 9. Test scope

Clause 5's two seeded directions (lacks one table / covers all), plus: wider-than-Basic; 0 or 2 owner roles; an empty
set (each a distinct fail-closed shape of the same clause); the builder grading the secure-BU root copy; the embedded
set's invariants (one-line justification: a set missing a root table would let provisioning's root assignment fail
while clause 5 said "covered"); the parser refusing what the script refuses (justification: the census now PARSES the
file — a parser accepting `Write` or an empty list would grade a widened role as covered); guarded exact-name
resolution (justification: a mis-cased guarded name would under-report clause 1); a codified privilege absent from the
environment being a finding, not a throw (builder test + job test that also proves the exposure clauses are still
graded in that run); a missing GUARDED privilege still throwing (job test); and Error-not-Critical logging (job test).
Existing NFR-05 clauses unchanged and green.

Seeded and watched fail, then restored: clause 5 removed from `Evaluate` → the clause-5 evaluator tests and the job
test red; the exact-name check made case-insensitive → the resolution test red; the job's Error branch disabled → the
job test red alone; the embedded resource's `LogicalName` changed → 41 tests red (every census test needs the set);
the "exists in environment" flag forced true → the builder test red.

Second live run on the final code (after the review fix): identical — 18 findings, "covering 8 of 9", finding 18 names
`prvReadsprk_Invoice`.

## 10. Verifier round 1 (branch `task/uac-r2-145-f1`, 2026-10-01)

Code and tests (no live call of any kind in this round):

| Finding | Change | Proven by seeding |
|---|---|---|
| 2 — "clause 5 ranks below every exposure" unpinned | `Evaluate_WhenAnExposureAndACodifiedGapCoexist_HeadlinesTheExposure` (clause-1 exposure + clause-5 gap → headline = `HumanPrincipalReachesSecureBusinessUnit`) and `Verdict_RanksACodifiedGapBelowEveryExposure` (theory over all 7 exposure/unknown verdicts, the clause-5 finding enumerated FIRST) | rank 7 → −1: 8 red (both tests). The verifier's exact seed, rank 7 → 0: 7 red (the theory; the Evaluate test alone survives a tie because clause 1 is enumerated first and the sort is stable — which is why the theory exists) |
| 3 — owner-role secure-BU filter unpinned | `Build_WhenASameNamedRoleLivesInASiblingBu_GradesOnlyTheSecureBuRole` (sibling role covers the whole set; the secure role lacks invoice → count 1, gap = invoice) | filter → `true`: 2 red |
| 4 — replica promised, not detected | Builder sets `SecureOwnerRoleCoverage.InheritedFromRootRoleId` when the one owner role in the secure BU has `roleid != parentrootroleid`; clause 5 reports it (`SecureOwnerRoleLacksCodifiedPrivilege`, message names the root role and "REPLICA") and does not grade it — the script's refusal (`Set-SecureRecordOwnerRolePrivileges.ps1:134-138`) and handoff S4. Test `Build_WhenTheSecureBuRoleIsAReplicaOfAnAncestorRole_ReportsIt_AndDoesNotGradeIt`; `Build_GradesTheOwnerRole…` now also asserts a native role is NOT flagged | builder detection off: 1 red; evaluator branch off: 1 red |
| 5 — entry precedes its quoted refusal | The `sprk_invoice` evidence now opens with **"VERBATIM REFUSAL PENDING"**, states that G1 step 1 replaces it with the verbatim text and that a not-refused result removes the entry by revert, and adds the one quoted invoice owner-check message that exists (note 085, root default team). §4 step 1 spells out both branches. The verbatim Secure-team text cannot be captured without a live create — that IS G1 step 1 | — (data) |

Live state is unchanged by this round, so dev's census still logs the clause-5 Error naming `prvReadsprk_Invoice` until G1
(intended). G1 must precede any dev deployment of task 130's invoice confirm against a secure matter. Once G1 is applied,
the file and the live role must change in lockstep (every entry → the §4 procedure), or the census goes red.

**Acceptance criteria — honest status after this round** (supersedes any "met" for these in the first report):

| Criterion | Status |
|---|---|
| TRACKING | **Partially met.** 082 status, #1046 state, PR #1045 recorded; the peer exchange at start and at close was NOT done (§1, §8a — main session) |
| Codified set covers 146/147/148 scope | **Not met as written; deferred by design.** 9 tables. analysis, communicationthread, spendsignal, spendsnapshot, emailreviewlog, analysisoutput, reportcard have no writer yet; 146 and 147's POMLs bind each to add its table through this procedure in the task that wires the writer (§3) |
| Negative control (live) | Pending G1 step 1 |
| Positive probes (live) | Pending G1 step 3 |
| Basic-only + before/after lists | Pending G1 (before = 8 recorded; expected after = 9, +1) |
| AUTHORIZATION (named team sole holder; clauses 2/3 live) | **Not met** — depends on task 144's §4.3 cutover (G2) |
| NEGATIVE census clause | Seeded both directions + live FAIL: met. Live PASS: pending G1 step 4 |
| Guide re-run removes nothing | Met (§6) |
| NEW ENVIRONMENT step | **Not built** — owner F10 = (a), handed to customer-provisioning-orchestration-r1 (INCOMING-145); dry-run/idempotency checks unverifiable until built |
| Drift asked once | Met (F1, by 082) |
| #1046 closed citing both + peer told | **Not met** — closed by the peer citing 082 only; the comment (§8) and peer message (§8a) are for the main session |
| Test scope | Met; the two test gaps (findings 2, 3) are closed above |

Publish size (CLAUDE.md §10 item 4) was not measured, by orchestrator instruction; the change is ~6 KB of embedded
JSON plus code — the main session measures after merge.

## 11. Gate G1, live run 2026-10-02 (spaarkedev1)

- **Run as** part of the task-144/145 live gate run (task-144 note §13).
- **Operator:** the `az` login `ralph.schroeder@spaarke.com`.
- **Scripts:** from `C:\wt28b` @ `bca0941f6`.
- **The probe team** is the **named team `Secure Record Owners` (`6eabc7f9-13be-f111-a05b-0022482913fc`)**, not the default team named in §4's snippet. The task-144 cutover ran first (task-144 note §13.2–13.6), so at G1 time the named team was the role's sole holder.

### 11.1 Step 1: negative control (3 polls, ~25 s apart)

`POST sprk_invoices` with `ownerid@odata.bind → /teams(6eabc7f9…)`. **REFUSED 3/3** (03:52:35Z, 03:53:00Z, 03:53:25Z), HTTP 403, code `0x80040299`. Verbatim, identical on all three polls:

> Read Privilege Check For Owner failed with exception: Principal team (Id=6eabc7f9-13be-f111-a05b-0022482913fc, type=9, teamType=0, privilegeCount=8, MetadataCachePrivilegesCount=7264, businessUnitId=d9ec0b6f-80a0-f111-aaac-000d3a99d1d7), is missing prvReadsprk_Invoice privilege (Id=315f05b2-7417-43fa-8b47-8d0722bfbaf2) on OTC=10732 for entity 'sprk_invoice' (LocalizedName='Invoice'). context.Caller=1d02f31c-1872-f011-b4cb-7c1e52671ad0. Consider adding missed privilege to one of the principal (user/team) roles.

What the refusal establishes:
- **It names only a Read privilege**, so escalation trigger 3 did not fire.
- **`privilegeCount=8` matches the role's 8 privileges**, so the reading was current, not cached.
- **The premise held** (POML AC 3).
- **The evidence was recorded first:** the text above REPLACED the `VERBATIM REFUSAL PENDING` evidence of the `sprk_invoice` entry in `config/secure-record-owner-role.json`, committed as `ed579766c` BEFORE step 2. The file's `howToExtend` rule is now satisfied.

### 11.2 Step 2: dry run → `-Apply` → §5.4 strip → `-Verify`

| Stage | Result |
|---|---|
| Before | 8 privileges, all Basic: `prvReadsprk_Communication`, `prvReadsprk_Document`, `prvReadsprk_Event`, `prvReadsprk_Matter`, `prvReadsprk_Memo`, `prvReadsprk_Project`, `prvReadsprk_Todo`, `prvReadsprk_WorkAssignment` |
| Dry run (exit 0) | `Present : 8 of 9` · `MISSING : sprk_invoice (prvReadsprk_Invoice)` · `DRY RUN: would add 1 privilege(s) at Basic: prvReadsprk_Invoice` |
| `-Apply` (exit 0) | `ADDED at Basic and read back: prvReadsprk_Invoice` · `Held now : 13 privileges (was 8)` · `WARNING: the platform also added 4 privilege(s) that were not requested: prvReadSharePointData, prvWriteSharePointData, prvCreateSharePointData, prvReadSharePointDocument` |
| §5.4 strip | `$keep` = the file's 9 `privilegeName`s. Strip candidates = exactly those four (guarded: anything else would have stopped the run). `RemovePrivilegeRole` → 204 ×4 |
| After | 9 privileges, all Basic: the 8 above plus `prvReadsprk_Invoice`. **Diff +1, nothing removed** |
| `-Verify` (exit 0) | `Held now : 9 privileges` · `Present : 9 of 9` · no "Outside the file" list · `VERIFY PASS: the role holds Read at Basic on all 9 tables.` |

### 11.3 Step 3: positive probes (3 consecutive polls, ~25 s apart)

Each poll:
- created `sprk_invoice` owned by the named team (`Prefer: return=representation`);
- read back `owningteam`;
- deleted the probe;
- ran a control create of `sprk_analysis` (not in the file) owned by the same team.

| Poll | Invoice id | Create | `owningteam` / BU | Delete / GET | Control `sprk_analysis` |
|---|---|---|---|---|---|
| 1 03:55:12Z | `db6ecf10-15be-f111-a05b-0022482913fc` | 201 | `6eabc7f9` / `d9ec0b6f` ✅ | 204 / 404 | REFUSED 403, `privilegeCount=9`, missing `prvReadsprk_analysis` |
| 2 03:55:38Z | `5959bb20-15be-f111-a05b-0022482913fc` | 201 | `6eabc7f9` / `d9ec0b6f` ✅ | 204 / 404 | REFUSED 403, `privilegeCount=9` |
| 3 03:56:05Z | `76e58e2f-15be-f111-a05b-3833c5e9614d` | 201 | `6eabc7f9` / `d9ec0b6f` ✅ | 204 / 404 | REFUSED 403, `privilegeCount=9` |

The control's `privilegeCount=9` proves that the probes saw the new role.

### 11.4 Step 4: NFR-05 live run

From task-144 note §13.8:
- **Clause 5 PASS live:** `1 owner role(s) in it covering 9 of 9 codified table(s) at Basic`, and no `SecureOwnerRoleLacksCodifiedPrivilege` finding.
- **Clauses 2, 2b, 3 and 4 also pass.** This meets the AUTHORIZATION criterion live: the named team is the sole holder, with 0 members.
- **The test as a whole FAILED on clause 1.** Clause 1 has 42 findings, 27 of them via the root BU default team `Spaarke` now holding `Spaarke Basic User`. That is an exposure outside this task and outside the owner's round-4 acceptance; see task-144 §13.8. Steps 9 and 10 of the run were stopped there.

### 11.5 Acceptance-criteria status after G1

This supersedes the §10 rows it names.

| Criterion | Status |
|---|---|
| Negative control (live) | **Met** (11.1) |
| Positive probes (live) | **Met** (11.3) |
| Basic-only + before/after lists | **Met**: 8 → 9, +`prvReadsprk_Invoice`, nothing removed (11.2) |
| AUTHORIZATION (named team sole holder; clauses 2/3 live) | **Met live** (task-144 §13.5–13.6, 13.8) |
| NEGATIVE census clause, live PASS | **Met for clause 5.** The test run fails on clause 1 for an unrelated exposure |
| Peer exchange (§8a) and #1046 comment | Unchanged: main session |
