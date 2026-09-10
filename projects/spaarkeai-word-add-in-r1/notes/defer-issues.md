# Deferrals & Issues — `spaarkeai-word-add-in-r1`

> Source of truth. Every entry must ALSO have a GitHub Issue (visibility). `push-to-github` blocks on entries missing a GitHub URL.
> File via `/project-defer-issue-tracking` (alias `/defer`) — it writes both places in one step.
> Per CLAUDE.md §11, every entry names a concrete behavior or contract that fails. "Future flexibility" is not a reason.

---

## ISS-001 — `sprk_event` is written to `sprk_document` but does not exist on the entity

| Field | Value |
|---|---|
| **Type** | Issue (live defect in shipped code) |
| **Found** | 2026-09-04, during `/project-pipeline` initialization (finding **F-g**) |
| **Owner** | **`unified-access-control-r2`** — its 2026-09-03 Q4 widening added the `event` entry, and `EntityAccessFilter` is its component |
| **Severity** | Filing a document to an Event is authorized and then cannot associate |
| **GitHub Issue** | ➖ **Not filed — operator decision 2026-09-04**: no GitHub Issue needed. Routing to `unified-access-control-r2` handled out-of-band. This entry remains the record; do not treat the missing URL as an unfiled obligation or block push on it. |

### What fails

`sprk_document` has **no `sprk_event` attribute**. Verified live via Dataverse MCP 2026-09-04 and corroborated by the maker-portal Columns view:

```
SELECT sprk_event FROM sprk_document
→ 'sprk_Document' entity doesn't contain attribute with Name = 'sprk_event'
```

The entity has **four** direct lookups (`sprk_matter`, `sprk_project`, `sprk_invoice`, `sprk_workassignment`) and **twelve** `sprk_related*` lookups. The Event column is **`sprk_relatedevent`** only.

Yet three shipped layers treat a direct `sprk_event` as real:

| Layer | Behaviour |
|---|---|
| `Api/Filters/EntityAccessFilter.cs:126-127` | Accepts `event` / `sprk_event` — authorizes the record-keyed upload route |
| `Spaarke.Dataverse/Models.cs` `DocumentAssociationMap.TryApply` | Maps it → `EventLookup`, returns `true` (success) |
| `Spaarke.Dataverse/DataverseServiceClientImpl.cs:916` | Writes `document["sprk_event"]` |

Both call sites carry a comment claiming the columns were "verified against live Dataverse metadata 2026-09-03". That holds for `sprk_workassignment`; it is wrong for `sprk_event`.

### Why it matters

This is exactly the failure mode the lockstep invariant exists to prevent. [`coordination-document-association-map-from-email-r2-2026-09-04.md`](../coordination-document-association-map-from-email-r2-2026-09-04.md) §3 states the rule — *"a type belongs in **both** maps or **neither**"* — and then names `event` as compliant. `event` is in both maps with no column.

Likely mechanism: the 2026-09-03 verification checked the `sprk_related*` family (where `sprk_relatedevent` does exist) while the code writes the direct family — the same family confusion visible in that doc's §1, which describes the write target as `sprk_related{recordtype}` when the code writes direct slots.

### Two further false claims from the same source

| Claim | Reality |
|---|---|
| `sprk_todo` is "unmappable — a document cannot be associated to a to-do at all … needs a schema change first" (§3) | **`sprk_relatedtodo` exists.** True only of a column literally named `sprk_todo`. No schema change required. |
| "`sprk_document` has no account/contact lookup column" (§4.1) | **`sprk_relatedcontact` exists.** `account` is correct — no account lookup at all. |

Knock-on: §4.2 repoints `RecordKeyedUploadAuthorizationTests`' deny-path example at `sprk_todo` as "genuinely unmappable" — that example rests on the same false premise and should be re-examined.

### What r1 did (and deliberately did not do)

- ✅ Corrected tasks **026** and **035** so they do not inherit the false premise; 026 reads `sprk_relatedevent`, never `sprk_event`
- ✅ Appended a §0 correction to the coordination doc
- ✅ Recorded as finding **F-g** in `plan.md` §3, `TASK-INDEX.md`, and project `CLAUDE.md`
- ❌ **Did not change `EntityAccessFilter`, `DocumentAssociationMap`, or `DataverseServiceClientImpl`** — out of r1's scope, and UAC-r2 is live on those files (`parallel-safe:false`). Fixing them here would collide.

### Suggested fix for the owner

Either add a direct `sprk_event` column to `sprk_document`, or repoint `EventLookup` at `sprk_relatedevent` and drop `event` from `EntitySetByType` until the families are reconciled. The broader question — why two lookup families exist and which one the association map should target — is worth settling before either.

---

## ISS-002 — 🟡 CI shadow window false green — **DIAGNOSED**; residual is an operator decision

| Field | Value |
|---|---|
| **Type** | Issue (blocks a repo-wide CI cutover) |
| **Found** | 2026-09-09, while scoping task 043 |
| **Diagnosed** | ✅ 2026-09-10 by task 044 — **ROUTER DEFECT, already remediated by PR #944**. See [`notes/044-false-green-diagnosis.md`](044-false-green-diagnosis.md). |
| **Owner** | 🔴 **OPERATOR** — the CI defect is fixed; what remains is a §6.5 **path B** decision on the shadow-window exit criterion (see the DIAGNOSED block below). Task 044 is complete; it deliberately applied no fix to the criterion. |
| **Severity** | ⬇️ Downgraded. The new tier is no longer unproven — the gap is closed and verified live. `sdap-ci.yml` still cannot retire, but now only because the *measurement* latches on a fixed defect. |
| **GitHub Issue** | ➖ not filed. Diagnosed under r1 task 044 (operator, 2026-09-09), which carried explicit authorization to touch the frozen tier files — **authorization not exercised; no tier file changed**. |

`scripts/ci/shadow-window-status.ps1` reports, as of 2026-09-09:

```
Window opened           : 2026-08-27 20:47 UTC
Comparable PRs examined : 44
Agreeing                : 11 / 20
Calendar-day span       : 4.1 / 5
False reds (logged)     : 0
FALSE GREENS            : 1
```

The tool's own words: *"A false green is DISQUALIFYING — the new tier passed a commit the legacy system
failed. Diagnose before continuing; the count above restarts from the most recent one."*

The offending merge is **PR #934 — `feat(email-intelligence-r2): Outlook/Word add-in — Create To Do
(sprk_todo)`**: legacy `failure`, Router `success`. That it is an **add-in PR** is why this project
found it, and is a reason r1 should care — the new tier passed something on our own surface that the
old one caught.

**Deliberately NOT absorbed by task 043.** 043 adds a jest gate for `office-addins`; diagnosing a
false green in the tier-cutover measurement is a different problem on a frozen surface, and merging
the two would put a project-scoped CI task in charge of a repo-wide cutover decision. 043 is
constrained to surface this and leave it alone.

---

### ✅ DIAGNOSED 2026-09-10 by task 044 — verdict: **ROUTER DEFECT, already remediated**

Full report: [`notes/044-false-green-diagnosis.md`](044-false-green-diagnosis.md).
**Task 044 changed no workflow file. Cost to the window: 0 PRs, 0 days.**

**What legacy caught** — run `33775635122`, job `100716509606` `Build & Test (Debug)`, **step 7
`Build`** (every test step `skipped`, so it is a **compile** failure, not a test failure):

```
Phase2EndToEndFixture.cs(443,17): error CS1503: Argument 4: cannot convert from
  'System.Threading.CancellationToken' to 'string?'  [Sprk.Bff.Api.IntegrationTests.csproj]
```

#934 added a 4th argument to `IOfficeService.QuickCreateAsync` without updating the fixture.

**Why Router passed** — Tier 1's `Compile (Debug)` (job `100716653033`) ran
`dotnet build src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj`: **the production BFF csproj only,
no test project at all.** Tier 2's `Full Unit Tests` (job `100716654163`) hit the identical CS1503
at its own `Build` step — but Tier 2 is excluded from Router by construction. Green `CI / Router`
over a solution that does not build. **5 of 8 solution test projects had no blocking compile
coverage**; `Spaarke.Core.Tests` (46 tests) was not even in `Spaarke.sln`.

**Verdict — DEFECT, not DESIGNED-BEHAVIOR.** The failing *job* sat in Tier 2, but the failing
*concern* is **compilation**, which is Tier 1's declared charter (the job is named `Compile
(Debug)`). Tier 2 caught it only incidentally, as the build that precedes its tests. Per the tier
design's own words: *"the gate is 'the solution builds', not 'the tests pass'."* Test **execution**
staying advisory in Tier 2 remains correct and is not a gap.

**Already fixed** — PR **#944** / `ce5c2c3d7`, merged 2026-09-04T22:13Z: Tier 1 now builds
`Spaarke.sln`. Verified from a live recorded run (job `102698717302`, 2026-09-10) compiling
`Sprk.Bff.Api.IntegrationTests.dll`. Compile coverage now **9/9** test projects.

**🔴 STILL OPEN — the blocker moved, it did not clear. Owner: OPERATOR (§6.5 path B decision).**

#944 fixed the CI defect but not the *measurement*. `shadow-window-status.ps1` computes
`$falseGreens` over the whole window and gates on `$falseGreens.Count -eq 0`, never filtered by
`$countingFrom` — so **#934 is a permanent latch** and `$ready` can never become true, however many
PRs subsequently agree. This contradicts the script's own prose (*"the count restarts from the most
recent one"*). Separately, its `-Since` docstring still claims PR #841 is *"the last change to the
CI configuration under observation"* — false since #944.

Decision required (details + rationale in §6 of the report):

- **Option 1 (recommended)** — advance default `-Since` to `2026-09-04T22:13:10Z`. The script's own
  doctrine; criterion semantics unchanged. **Costs 6 PRs / 1.1 days** (today: 5/20, 3.0/5 days,
  **0 false greens, 0 false reds**).
- **Option 2** — de-latch `$ready` to evaluate false greens over the counting run only. **0 PRs,
  0 days** (keeps 11/20, 4.1/5) but relaxes a safety gate.

Neither was applied — path B requires operator sign-off, not a unilateral rewrite. Note the marginal
cost is **6 PRs / 1.1 days, not the 11 / 4.1 assumed at filing**: #934's own reset had already
discarded everything before it.

Minor, non-blocking: #944's comment in `ci-tier1-blocking.yml` says *"widened 2026-08-31"* — wrong
and chronologically impossible (it predates the 09-03 failure). Actual: 2026-09-04. Fix in passing;
not worth a dedicated edit of a frozen file.

---

## Deferrals

### ⚠️ D-032-1 — WITHDRAWN AS A FINDING 2026-09-09; reduced to a one-question verification

**Operator challenge, 2026-09-09** — and it was correct. The entry below rests on the premise
*"Read on a document does not imply Read on its matter."* That premise contradicts Spaarke's actual
access model: **access flows from the parent record → container → documents**. If a caller can read
the source document, that is normally *because* they hold the matter, so the hub node's matter name
discloses nothing they did not already have.

The codebase says the same thing in its own words. `Spaarke.Core/Auth/AuthorizationService.cs:246-249`
(written by `unified-access-control-r2` task 070) describes the parent check as *"what makes 'access
flows from the parent' an enforced property rather than a **stated intention**"*.

**What is genuinely unresolved** is narrow, and it is a Dataverse configuration question, not a code
defect: `DataverseAccessDataSource` hard-codes `sprk_documents({id})`, so it asks Dataverse about the
**document row itself** — it does not derive the answer from the matter. Whether matter access
therefore implies document Read depends on the `sprk_document` → `sprk_matter` relationship's cascade
configuration. The ERD records that lookup as **optional** (`sprk_document }o--o| sprk_matter`), and a
document may have no matter at all.

**The single question to close this**: is `sprk_document` → `sprk_matter` **Parental / cascading-share**,
or **Referential** (no cascade)?
- **Parental/cascading** → the operator's model holds, there is no disclosure, **delete this entry.**
- **Referential** → document Read can exist without matter Read, and the model is a stated intention
  rather than an enforced one. That is UAC-r2's Amendment 1 territory and would then warrant an
  ACTIVE hand-off (a note they are instructed to read — not a GitHub issue nobody opens).

Checkable in two minutes in the maker portal (Tables → sprk_document → Relationships → the
`sprk_matter` lookup → Advanced/cascade behavior). Dataverse MCP was down 2026-09-09 so it could not
be queried live.

**Do NOT hand this to another project until that question is answered.** Handing over a finding whose
premise is unverified is exactly the burial-by-deferral the project policy forbids, and it would
create a cross-project dependency for something that may not exist.

<details>
<summary>Original entry as filed by task 032 (premise now disputed — retained for the record)</summary>

### D-032-1 — Visualization parent HUB nodes are not authorized against their parent record

**Found by** task 032 while closing finding F-b. **Owner: `unified-access-control-r2`** (its surface).

`VisualizationService.CreateParentHubNode` builds a hub node carrying the SOURCE document's own matter /
project / invoice name (`sourceDoc.MatterName`, `BuildParentRecordUrl("sprk_matter", …)`). The caller is
authorized on the source document, but **Read on a document does not formally imply Read on its matter** —
so a matter's NAME, and a deep link to its record, can reach a caller holding no rights on the matter itself.
On a secure matter the name is frequently the sensitive fact (a matter named for a counterparty discloses the
engagement's existence), which is the same argument `RecordSearchAuthorizationFilter`'s remarks make.

**Not fixed in task 032, deliberately.** F-b is about RESULT ROWS; hub nodes are pre-existing and are the
source's own information. Widening a security task's scope silently is what CLAUDE.md §11 forbids, and the
correct check (`GetCallerRecordAccessAsync` against `sprk_matters`/`sprk_projects`/`sprk_invoices`) is
UAC-r2's mechanism, not a second one built here.

**Suggested fix**: authorize each hub against its parent record and drop the hub — and the source's edge to
it — when Read is absent. Task 032's `AuthorizeRowsAsync` already drops hubs left with no surviving
document, so the hook exists.

*(end of retained original D-032-1 entry)*

</details>

---

### ✅ D-032-2 — NOT DEFERRED. Folded into task 033 by operator decision 2026-09-09

This is **not** a hand-off and **not** a deferral. It has no external owner, and the parties it would
mislead — tasks **033** and **034** — are in this project. Per the project deferral policy (defer only
for a good technical reason *or* a clear hand-off; never bury), it becomes an acceptance criterion of
**task 033**, which renders this surface.

The original reasoning for not fixing it *inside task 032* still stands and is unchanged: a
response-contract change does not belong inside a security fix. That justified deferring it out of
**032**; it never justified deferring it out of the **project**.

Scope when 033 runs: add a warnings channel to `GraphMetadata` mirroring cross-record document
search's existing `PARTIAL_RESULTS` warning, and surface it in the Find view.

<details>
<summary>Original entry as filed by task 032 (retained for the record)</summary>

### D-032-2 — Rows dropped past the authorization budget are not announced to the client

**Found by** task 032. Same shape as the limitation `RecordSearchEndpoints.AuthorizeRowsAsync` recorded.

`VisualizationEndpoints.AuthorizeRowsAsync` bounds its work at `MaxDocumentAuthorizationChecks = 100` and
**drops** everything past it (fail closed). The caller sees a short graph that is indistinguishable from
"there simply are not many related documents". Cross-record document search announces exactly this with a
`PARTIAL_RESULTS` warning; `GraphMetadata` has no warnings channel.

**Not fixed in task 032, deliberately**: adding one is a response-contract change, and a response-contract
change does not belong inside a security fix — the same call `RecordSearchEndpoints` made and recorded.

**Relevant to task 033/034**, which render this surface: a short result page may mean "withheld", not
"nothing matched". See `notes/032-authorization-hardening.md` §5 for the counts that make this reachable
(five hardcoded relationship queries at `TopCount = 50` each).

</details>
