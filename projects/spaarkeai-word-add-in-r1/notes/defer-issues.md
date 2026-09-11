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
| **Owner** | ✅ **RESOLVED 2026-09-10.** Operator chose **Option 1** — advance `-Since` to `2026-09-04T22:13:10Z` (immediately after PR #944, the last change to the CI configuration under observation). Applied to `scripts/ci/shadow-window-status.ps1`. |
| **Outcome** | Window re-measured live after the change: **0 false greens, 0 false reds, 5/20 agreeing, 3/5 days.** The banner flipped from "A false green is DISQUALIFYING" to "Window still open. Nothing to do — keep merging normally." The cutover is unblocked and simply needs merges to accumulate. Cost paid: 6 comparable PRs, 1.1 days. |
| **⚠️ Residual** | The **latch itself was NOT repaired** — `$falseGreens` is still computed over the whole window while `$ready` gates on it unfiltered, so the NEXT false green will latch exactly the same way. Advancing `-Since` sidestepped #934; it did not fix the mechanism. Repairing it means deciding what the exit criterion MEANS — a hard stop, or a reset — which belongs to whoever owns the cutover. Documented in the script's own `.PARAMETER Since` block so it cannot be lost. |
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

## ISS-003 — Global exception handler serves `application/json`, never `application/problem+json` (ADR-019, repo-wide)

| Field | Value |
|---|---|
| **Type** | Issue (live defect in shipped code — ADR-019 contract violation) |
| **Found** | 2026-09-11, task 016, while writing `DocumentIdentityContractTests.cs`'s malformed-URL contract test |
| **Owner** | Whoever next touches `MiddlewarePipelineExtensions.cs` — a one-line fix, no design work |
| **Severity** | Every `SdapProblemException`-driven HTTP error across the ENTIRE BFF API (400/404/409/503/…) is served with the wrong media type |
| **GitHub Issue** | [spaarke-dev/spaarke#975](https://github.com/spaarke-dev/spaarke/issues/975) |

**Description**

`src/server/api/Sprk.Bff.Api/Infrastructure/DI/MiddlewarePipelineExtensions.cs` `UseSpaarkeMiddleware`'s
global exception handler (the ONE place every `SdapProblemException` is rendered) does:

```csharp
ctx.Response.ContentType = "application/problem+json";
...
await ctx.Response.WriteAsJsonAsync(new { type, title, detail, status, correlationId, extensions });
```

`HttpResponseJsonExtensions.WriteAsJsonAsync` (no explicit `contentType` argument passed) unconditionally sets
`response.ContentType = contentType ?? "application/json; charset=utf-8"` — it does not consult the header
already set two lines above. **Every** `SdapProblemException`-driven response is therefore served as
`application/json`, never `application/problem+json`, contrary to ADR-019 ("MUST return ProblemDetails for all
HTTP failures" — RFC 7807 defines that media type as part of the contract).

**Concrete failure mode**: any client (or contract test) that dispatches on `Content-Type:
application/problem+json` to recognize an RFC-7807 error body — rather than sniffing the JSON shape — will not
recognize a Spaarke BFF error as one. This is exactly the class of client-complexity ADR-019 exists to prevent
("Consistent error shapes reduce client complexity, improve debuggability").

**Confirmed NOT a fixture artifact.** The SAME test fixture's 403 response — a DIFFERENT code path
(`DocumentAuthorizationFilter`'s `Results.Problem(...)`, which sets the header through ASP.NET Core's own
`ProblemHttpResult` rather than a raw `WriteAsJsonAsync`) — gets the header right in the identical in-process
host. Only the global-exception-handler path is affected.

**Confirmed genuinely untested, not previously known-and-worked-around**: `grep -rln
"application/problem\+json" tests/` returns zero hits anywhere in the repository before this task.

**Entry-points**

- `src/server/api/Sprk.Bff.Api/Infrastructure/DI/MiddlewarePipelineExtensions.cs:29-87` (the handler)
- `src/server/api/Sprk.Bff.Api/Infrastructure/DI/MiddlewarePipelineExtensions.cs:73` (the `WriteAsJsonAsync` call to fix)
- Reproduction: `tests/integration/contract/Api/Documents/DocumentIdentityContractTests.cs`
  `ResolveIdentity_ForAMalformedOrEmptyUrl_Returns400ProblemDetails_NotA500` — currently asserts the JSON body
  shape rather than the header (working around the defect, not fixing it) with the full diagnosis inline as a
  code comment.
- Full diagnosis: `notes/016-fixture-diagnosis.md` §3 and §6.

**Suggested fix**

Pass the content type explicitly: `ctx.Response.WriteAsJsonAsync(payload, contentType: "application/problem+json")`
(overload exists), or replace the raw `WriteAsJsonAsync` call with `Results.Problem(...)`/`TypedResults.Problem(...)`
so ASP.NET Core's own `ProblemHttpResult` owns the header the way `DocumentAuthorizationFilter` already does.
Either is a small, contained change to one file; add a regression test asserting
`response.Content.Headers.ContentType?.MediaType == "application/problem+json"` for at least one
`SdapProblemException` path once fixed (the KEEP path this file already uses,
`tests/integration/contract/**`).

**Estimated effort**: <1 hour (one-line fix + one regression test)
**Blockers**: none
**Related**: ADR-019 (concise: `.claude/adr/ADR-019-problemdetails.md`; full: `docs/adr/ADR-019-api-errors-and-problemdetails.md`)

---

## Deferrals

### ✅ D-032-1 — WITHDRAWN 2026-09-10 (final). The cascade setting was the wrong question.

**Operator, 2026-09-10**, stating the model precisely: *if a user has access to a record they have
access to its documents, and vice versa; SPE access is at the container level, not the file level;
secure access grants the record + its documents + the files in the secure BU's container. There is no
case where a user should hold a document but not its record.*

**Verified against CODE — deliberately not docs, since three docs in this area were stale today:**
- **Internal access is caller-scoped and fails closed.** `Spaarke.Core/Auth/AuthorizationService.cs:54`
  denies when there is no caller token and "must never degrade to app-only evaluation" (task 004,
  finding A-2; zero app-only consumers, verified 2026-08-21). `:79` passes the caller's token and
  `:224-225` "queries Dataverse AS THE USER". So Read on a document is decided by Dataverse's native row
  security for *that user* — BU ownership, role depth, teams — the same evaluation that decides Read on
  the matter.
- **External access is granted at the ROOT record only.** `GrantAccessRequest` targets exactly one
  Project, Matter or Work Assignment — never a document. External document visibility is
  `_sprk_project_value eq {projectId}` (`ExternalDataService.cs:209-213`): documents are visible
  *because of* the root.
- **SPE is broker-only for external users** — contacts are never added to containers.
- **No code shares an individual document row.** The only Dataverse SDK message import in the BFF is
  `UserPrivilegeChecker`'s `RetrieveUserPrivilegesRequest` — read-only.

**Why the same-day reinstatement below was wrong.** I asked whether `sprk_document → sprk_matter`
cascades. Cascade (Referential vs Parental) governs what happens during a *share / assign / delete of the
matter* — whether that operation propagates to its documents. The model never grants document access by
sharing the matter; it grants BOTH through the same BU/role evaluation (internal) or the same root grant
(external). So "Referential" answered a question the model does not depend on. The finding assumed
access can diverge; the model is built so it cannot, and the code enforces it.

**One honest residual — configuration, not code, and the operator's to own:** the equivalence holds as
long as each security role grants **aligned depth on `sprk_document` and `sprk_matter`** and documents
are owned by the same BU as their record. A role granting Org-depth Read on documents but BU-depth on
matters would reintroduce the divergence. (Dataverse MCP was down this session; role depths were not
inspected.) Separately, `unified-access-control-r2` already records that the secure-BU model is
currently non-functional in dev — `Spaarke` is the root BU and `Spaarke Basic User` holds Deep depth
reaching the secure BU. That is a real secure-isolation risk, but it exposes the record AND its
documents together — not one without the other — so it does not revive this finding.

**No hand-off.** Nothing goes to UAC-r2 for this entry.

**Doc drift found while verifying (not this entry's problem, recorded so it is not lost):**
`docs/architecture/uac-access-control.md` (corrected 2026-08-20) still states `AuthorizationService`
passes `userAccessToken: null` and runs app-only; the code was changed to fail-closed caller-scoped one
day later (2026-08-21). Relying on it would have produced a false caveat here.

#### ~~D-032-1 — REINSTATED 2026-09-10~~ (SUPERSEDED the same day — see the withdrawal above)

**Operator verified in the maker portal, 2026-09-10**: the `sprk_document` → `sprk_matter`
relationship is **Referential**, with **Delete: Remove Link** and no cascade share.

**So Dataverse does NOT automatically grant Read on a document when a caller is granted its matter.**
The one question this entry was reduced to has been answered, and it answered against the withdrawal.

**I withdrew this finding too readily on 2026-09-09** and should record why, because the mistake is
reusable. The operator challenged the premise ("aren't we granting access at the container level, via
the parent record?"), I checked, and I found the codebase describing exactly that model — so I treated
a description of INTENT as evidence of MECHANISM and withdrew. The very sentence I quoted should have
stopped me: `Spaarke.Core/Auth/AuthorizationService.cs:246-249` calls the parent check *"what makes
'access flows from the parent' an **enforced property rather than a stated intention**"*. That phrasing
only makes sense if, absent that check, it is NOT enforced. The cascade setting is the mechanism, and
nobody had looked at it.

**The precise position, which is narrower than either extreme:**
- The model holds **wherever code enforces it** — `GetCallerRecordAccessAsync` exists precisely to make
  it true on the paths that call it. `unified-access-control-r2` task 070 built it for that reason.
- The **platform** does not enforce it. Referential means document ACLs are independent of the matter's.
- `VisualizationService.CreateParentHubNode` is **not** one of the paths that enforces it.

**Therefore the original finding below stands as written**: a hub node carries the source document's
matter/project/invoice NAME and a deep link to that record, built without any check against the parent,
and reaches any caller authorized on the document alone. On a secure matter the name is frequently the
sensitive fact.

**Owner: `unified-access-control-r2`.** It owns parent→child access (its Amendment 1) and it owns
`GetCallerRecordAccessAsync`, the correct mechanism. This is NOT a second mechanism r1 should build —
root CLAUDE.md §11. The hook already exists: task 032's `AuthorizeRowsAsync` drops hubs left with no
surviving document, so the drop path is there; what is missing is authorizing the hub itself against
its parent record.

**⚠️ HAND-OFF OBLIGATION — a GitHub Issue is NOT a hand-off.** Per the operator's standing instruction
(2026-09-09): *"filing a github issue also does not explicitly raise it to the project — we need to
provide the note and actively instruct to read it."* This entry must be actively delivered to UAC-r2
with an instruction to read it, not filed and forgotten. Until that happens this is not handed off.

<details>
<summary>Superseded 2026-09-09 withdrawal reasoning (retained — it is the record of the wrong call)</summary>

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

> **✅ ANSWERED 2026-09-10 — REFERENTIAL.** The condition above is met, so the hand-off is now
> warranted rather than premature. See the reinstatement block at the top of this entry.

</details>

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

---

### D-043-1 — `deploy-office-addins.yml` path filter misses a file the add-ins ship

**Found by** task 043 while path-filtering the new office-addins CI gate.
**Owner**: whoever next touches `.github/workflows/deploy-office-addins.yml`. One-line change.

`src/client/office-addins/webpack.config.js:101` aliases
`@spaarke/communication-components/logic/connections/provenance` at
`src/client/shared/Spaarke.Communication.Components/src/logic/connections/provenance.ts`, and
`shared/taskpane/services/communicationSuggestionsService.ts:7` imports it. So that file is
**bundled into the shipped add-ins**.

`deploy-office-addins.yml` triggers only on `src/client/office-addins/**` and
`src/client/shared/Spaarke.Auth/**`. Editing `provenance.ts` alone therefore changes the add-in's
behaviour **without redeploying it** — the identical failure mode the `Spaarke.Auth` path was added
to prevent, and its comment says so in as many words: *"Without this path the add-ins would keep
deploying against a stale auth lib."*

**Fix**: add `- 'src/client/shared/Spaarke.Communication.Components/src/logic/connections/provenance.ts'`
to that workflow's `paths:`. `provenance.ts` has no imports of its own, so the exact file is the
precise filter; no wildcard needed.

**Not fixed in task 043, deliberately**: it is a different workflow with different blast radius
(a deploy trigger, not a test gate), and widening someone else's deploy trigger is not a change to
make as a side effect. The new gate `.github/workflows/office-addins-tests.yml` **does** carry the
path, so the test side is already covered.

Detail: `notes/043-office-addins-ci-gate.md` §8 F-1.

---

### D-043-2 — `identity-obj-proxy` is mapped by `jest.config.js` but is not a dependency

**Found by** task 043 on a from-scratch `npm install` of `src/client/office-addins`.
**Owner**: next person editing that package's test setup. One `devDependencies` line.

`src/client/office-addins/jest.config.js` maps `\.(css|less|scss|sass)$` → `identity-obj-proxy`,
which appears in neither `package.json` nor `package-lock.json`, and is not installed.

Latent, not currently breaking: jest resolves `moduleNameMapper` targets lazily, and no suite in
this package imports a stylesheet today — confirmed on a clean tree. The first CSS import added to
the test graph will fail with a "could not locate module" error that says nothing about CSS.

Detail: `notes/043-office-addins-ci-gate.md` §8 F-4.
