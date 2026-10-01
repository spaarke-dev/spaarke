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
| Peer exchange | The peer session (`spaarke-wt-spaarkeai-word-add-in-r1-54`) is live and busy. Not messaged from this sub-agent: no live write was made here, so nothing races the shared role; the main session owns cross-session coordination and delivers §8's outcome | — |

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

# 1) NEGATIVE CONTROL — 3 polls ~25 s apart. Expect a refusal naming prvReadsprk_Invoice. Record the text VERBATIM
#    into the sprk_invoice entry's "evidence" in config/secure-record-owner-role.json.
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
`SecureOwnerRoleLacksCodifiedPrivilege` naming the table and privilege; no owner role, two owner roles, or an empty set
is a finding too (never a quiet pass). Same rule as `-Verify`. Ranked below every exposure verdict (it fails closed).

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
