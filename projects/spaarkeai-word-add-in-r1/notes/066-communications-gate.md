# Task 066 — F9: the three `/api/office/communications/*` routes

**Finding**: `notes/fable-review-2026-09-21.md` §2, **F9 (LOW-MEDIUM)** · **Date**: 2026-09-21
**Outcome**: 🔔 **ESCALATED — no code change shipped.** The disclosure is **real and larger than the review
stated**, but *every per-resource read check available to this task denies 100% of the pane's legitimate
users*. Verified live against the dev Dataverse. Details in §4; the decision the owner must make is in §6.

> The review explicitly flagged that it had **not read the handler bodies**. §2 records what all three
> actually return, verbatim, and marks each of its claims CONFIRMED or CORRECTED.

---

## 1. Summary table — the review's claim, confirmed or corrected

| Route | Review's claim | Verdict | What it actually discloses |
|---|---|---|---|
| `GET /by-message-id/{internetMessageId}` (`:72` → `FindByMessageIdAsync` `:124-211`) | "whether a given email was captured and its subject" | ✅ **CONFIRMED**, plus one item the review did not name | `communicationId` (the GUID) **and** `subject`. The GUID is the key to route 3 and to `POST /api/communications/{id}/suggest-associations` |
| `GET /by-message-id/{id}/suggestions` (`:93` → `GetSuggestionsByMessageIdAsync` `:247-336`) | "the Association Engine's predicted record and its alternates" | ⚠️ **CONFIRMED AND EXCEEDED** | Everything the review said, **plus the display NAMES of every candidate record**, fetched by a *second*, independent app-only read the review could not have seen (`ResolveCandidateNamesAsync` `:344-370`). This is the most serious of the three |
| `GET /{commId:guid}/linked-todos` (`:108` → `GetLinkedTodosAsync` `:375-449`) | "the linked To Dos" | ✅ **CONFIRMED**, with one doc-comment **CORRECTED** | `sprk_todoid` + `sprk_name` + `statecode` + `statuscode`, ≤10. The route's own XML doc (`:29-32`) claims it 404s "if the communication itself does not exist" — **false**, the handler never checks; a bogus GUID returns `200 {count:0,todos:[]}` |

All three **do** genuinely disclose. None qualified for "leave it alone as non-disclosing".

---

## 2. What each handler actually returns — verbatim (acceptance criterion 1)

All three share one property: **no query on any of these paths carries the caller's identity.** Every
Dataverse read is `IGenericEntityService` app-only. `CallerResolution.ResolveObjectId(context.User)` is
resolved in all three handlers — and used **only as a log field**. It never reaches a query or a decision.

### 2.1 `FindByMessageIdAsync` (`CommunicationsEndpoints.cs:124-211`)

The read (`QueryCommunicationByMessageIdAsync` `:220-238`), verbatim:

```csharp
var query = new QueryExpression("sprk_communication")
{
    ColumnSet = new ColumnSet(
        "sprk_communicationid",
        "sprk_subject",
        "sprk_internetmessageid"),
    TopCount = 1
};
query.Criteria.AddCondition(
    "sprk_internetmessageid", ConditionOperator.Equal, internetMessageId);

var results = await entityService.RetrieveMultipleAsync(query, ct);
```

The disclosure (`:189-193`):

```csharp
return Results.Ok(new CommunicationLookupResponse
{
    CommunicationId = communicationId,
    Subject = subject
});
```

Wire: `{"communicationId":"<guid>","subject":"<sprk_subject>"}`. No row ⇒ 404 `OFFICE_COMM_NOT_FOUND`.

### 2.2 `GetSuggestionsByMessageIdAsync` (`:247-336`)

Same app-only resolution, then the engine path (`:297-299`) and then — **the part the review did not
see** — an app-only retrieve of each candidate record's primary-name field (`:360`):

```csharp
var record = await entityService.RetrieveAsync(c.TargetEntity, recordId, new[] { nameField }, ct);
var name = record.GetAttributeValue<string>(nameField);
if (!string.IsNullOrWhiteSpace(name)) names[c.TargetId] = name;
```

The disclosure (`:312-318`):

```csharp
return Results.Ok(new CommunicationSuggestionsResponse
{
    CommunicationId = communicationId,
    Subject = subject,
    Suggestions = suggestions,
    Names = names
});
```

`Suggestions` carries, per candidate: `field`, `targetEntity`, `targetId`, `reinforcedConfidence`,
`deterministicConfidence`, `written`, `conflict`, and each contributor's `rung` / `confidence` /
`provenance`; plus `status` and `autoFileEligible`. `Names` maps each `targetId` to that record's **human
title** — e.g. a matter name. **Two independent app-only disclosures in one handler**: the communication,
and then N unrelated records that the engine merely *guessed* are related. A caller with no rights on the
matter learns the matter's name. The code comment at `:301-304` states the intent plainly ("the client
would otherwise show a GUID in the picker") — it is a deliberate feature, authored without an access check.

### 2.3 `GetLinkedTodosAsync` (`:375-449`)

```csharp
var query = new QueryExpression("sprk_todo")
{
    ColumnSet = new ColumnSet(
        "sprk_todoid",
        "sprk_name",
        "statecode",
        "statuscode"),
    TopCount = LinkedTodosTopCount
};
query.Criteria.AddCondition(
    "sprk_regardingcommunication", ConditionOperator.Equal, commId);
```

Returns `{count, todos:[{sprk_todoid, sprk_name, statecode, statuscode}]}`. **The communication itself is
never read**, so the route neither confirms nor denies its existence — the doc-comment correction in §1.

---

## 3. REPRODUCE-FIRST (criterion 2) — partially met, and stated as such

**Met at the code level.** The reproduction is not a subtle one: the three queries above are reproduced
verbatim, and *none of them takes a caller identity*. There is no branch, no filter, no post-trim anywhere
between `RequireAuthorization()` and the response — so "a caller with no rights receives the record" is not
an inference about behaviour, it is the shape of the code. §4 then establishes the stronger, non-obvious
fact: in this environment **no ordinary caller has rights on any of these rows**, so *every* caller of these
routes is an unauthorized one.

**NOT met as an executable red test.** ⚠️ Task 062 backed its reproduce-first with a failing test. I did not
write one here, deliberately: a negative test is only meaningful against the gate it guards, and §4 concludes
that no gate should be added until the owner decides §6. Committing a red test would break the suite;
committing a green test that *asserts the current disclosure* would codify the defect. This is a real gap in
this note relative to 062's standard, recorded rather than glossed. It should be written **with** the fix.

---

## 4. 🔴 Why no filter was added — the load-bearing finding (verified live)

The obvious fix is a `CallerRecordAccessProbe` read check on `sprk_communications({id})`, in an endpoint
filter, exactly as `RecordRouteAccessAuthorizationFilter` does for the upload routes. I designed it, then
checked whether it would actually discriminate. **It cannot.** Three facts, all queried live against the dev
environment via the Dataverse MCP connection, not assumed:

### 4.1 Every `sprk_communication` row is owned by a BFF application user

`EmailUploadCaptureService.BuildCommunicationEntity` (`:212-247`) sets **no `ownerid`**, and the create runs
app-only — so the owner defaults to the BFF's own Dataverse application user. Live census of all 262 rows:

| `owneridname` | rows |
|---|---|
| `SDAP-BFF-SPE-API` (BFF app user) | 209 |
| `# mi-bff-api-dev` (BFF managed identity) | 26 |
| `Ralph Schroeder` (System Administrator) | 26 |
| `Spaarke` | 1 |

**Zero rows are owned by a non-admin human.**

### 4.2 The add-in's own user population holds no usable read privilege on the table

`prvReadsprk_Communication` = `effd0e4a-e9a9-425b-88b9-99936d8b4868`. Every role granting it, live:

| Role | depth |
|---|---|
| Service Writer / Service Reader / System Administrator / System Customizer | 8 (Organization) |
| Support User | 1 (User — own records) |
| **Spaarke Basic User** | **1 (User — own records)** |

`Test User 1` (`testuser1@spaarke.com`) — the environment's realistic non-admin user — holds **Spaarke Basic
User, Spaarke Office Add In User, Spaarke AI Analysis User, Spaarke Reporting Access Viewer**. The
Spaarke-authored **`Spaarke Office Add In User` role grants read on exactly three `sprk_*` tables** —
`sprk_EmailArtifact`, `sprk_ProcessingJob`, `sprk_AttachmentArtifact`, all depth 1 — and **not**
`sprk_Communication`. The only communication grant that user has is `Spaarke Basic User`'s **depth 1**, which
means *own records only* — and per §4.1 they own none.

⇒ **`RetrievePrincipalAccess` would return `AccessRights.None` for Test User 1 on every communication in the
environment.** A read gate would 403/404 the Outlook pane's three primary flows for its own target user.

### 4.3 The security model on this table cannot discriminate at all — this is structural, not a dev quirk

Because **100% of rows share one owner**, ownership-depth security degenerates to a table-level switch:

| Caller's depth on `sprk_communication` | Rows readable | Gate's effect |
|---|---|---|
| 1 / 2 / 4 (User / BU / Deep) | **0** | 🔴 total outage of the pane |
| 8 (Organization) | **all 262** | 🟡 no-op — the gate permits exactly the caller the finding is about |
| no privilege | **0** | 🔴 total outage |

There is **no configuration in which a per-record Dataverse read check on `sprk_communication` both permits
the pane's normal users and refuses an unauthorized one.** That is the finding. It is a property of how these
rows are created (app-owned, no `ownerid`), not of this environment's role assignment — so "it might be
configured differently in production" does not rescue it: in production the check is either an outage or
security theatre. Shipping it would satisfy the acceptance criterion while making the product worse.

### 4.4 The same is true of route 3's to-dos

`prvReadsprk_Todo` is granted **only** to Service Writer / Service Reader / System Administrator / System
Customizer (depth 8) and Support User (depth 1). **Neither `Spaarke Basic User` nor `Spaarke Office Add In
User` holds it at any depth.** A caller-scoped trim of the linked to-dos — the natural fix for route 3, and
the shape task 062 used — therefore also returns zero rows for every add-in user.

### 4.5 The two alternative authorization subjects, and why each fails

**(a) Authorize against the communication's REGARDING record** (the matter/project it is filed to). Those
types *do* carry a real discriminating grant (`Spaarke Basic User` has depth **4** on `sprk_Matter`,
`sprk_Project`, `sprk_Invoice`, `sprk_Document` — which is also why task 062's impersonated search works),
and `EntityAccessFilter.TryResolveEntitySet` + `CallerRecordAccessProbe` already cover them. **But route 1
exists to answer "was this email captured?" in the FR-B2 flow — before any association exists.** An
unassociated save is explicitly supported. "No regarding ⇒ allow" fails open; "no regarding ⇒ deny" breaks
the primary flow. **This is verbatim the escalation trigger the POML anticipated**, reached from a different
direction than expected.

**(b) Authorize by participation** — "were you on this email?" — against the first-class participant index
`sprk_communicationparticipant` (`sprk_systemuser` lookup; ADR-048). Non-heuristic, one cheap query, and it
fits the routes' purpose better than anything else considered. **It fails on a pipeline gap, not on
principle**: `CommunicationParticipantIndexer.WriteParticipantsAsync` is called by `MessagingIngestor`,
`CommunicationService` (outbound) and `IncomingCommunicationProcessor` — and **not** by
`EmailUploadCaptureService`, the Office add-in's own save path (its ctor takes four dependencies, none of
them the indexer). An email saved from the add-in therefore has **no participant rows at all**, so a
participation gate would refuse the saver their own just-saved email. Wiring the indexer into the capture
path is a capture-pipeline change, not an authorization fix, and is well outside a LOW-MEDIUM remediation.

---

## 5. What I deliberately did NOT do

- **No filter added, no route changed.** `CommunicationsEndpoints.cs` is byte-identical to its state at
  `1e62f7c4c`. Per the POML's own instruction: *"STOP and escalate rather than inventing a check that does
  not fit the route's purpose."*
- **No `tests/integration/contract/Api/Office/CommunicationsAuthorizationContractTests.cs`.** See §3.
- **No partial hardening.** Trimming route 2's candidate *names* to records the caller can read **would**
  work (those types discriminate — §4.5a) and is the single highest-value improvement available. I did not
  ship it: it does not close F9, it is a different mechanism from the one the task specified, and inventing
  scope inside an escalation is what CLAUDE.md §11 forbids. It is recommended in §6 as option **C**.
- **No edits to sibling-owned files.** `OfficeEndpoints.cs` / `OfficeService.cs` (task 064),
  `RagEndpoints.cs` / `TenantAuthorizationFilter.cs` / `FileIndexingService.cs` (task 063),
  `RouteAuthorizationGuardTests.cs` (task 061) — all untouched.

---

## 6. 🔔 ADR/Design conflict — resolution required (CLAUDE.md §6 + §6.5)

- **Rule challenged**: the POML constraint *"ADR-008 — add per-resource checks as endpoint filters"*, and
  acceptance criterion 3 *"each genuinely-disclosing route carries a per-resource read check"*.
- **Conflict**: the per-resource read check ADR-008 prescribes has **no discriminating subject** on these
  routes (§4.3). Complying literally produces either a total pane outage or a no-op. This is CLAUDE.md §6.5's
  named anti-pattern — *"ADR says no, so I'll write worse code to comply"* — inverted: *the rule says yes,
  and saying yes makes it worse*.
- **Proposed path**: **B (amend the task, not ADR-008)** — ADR-008 is correct in general and correct for
  every other route on this surface; what is wrong is F9's premise that a per-record check is available here.
- **Alternatives genuinely considered and rejected**: A (project-scoped exception permitting an ungated
  route) — rejected, it documents the hole without closing it; C (pivot to comply) — rejected on the
  evidence in §4.

**The three options, with their true costs:**

| # | Option | Closes F9? | Cost | Risk |
|---|---|---|---|---|
| **A** | **Grant the add-in user population a discriminating read on `sprk_communication`** — i.e. stop creating rows app-owned (set `ownerid` to the saving user in `EmailUploadCaptureService`) and give `Spaarke Office Add In User` depth-1/2 read. *Then* the ADR-008 filter works exactly as F9 imagined. | ✅ fully | Dataverse schema/role change + a capture-pipeline change + a **backfill decision for 236 existing app-owned rows**. Spans two other projects' surfaces | Highest value, highest blast radius. **Not a task-066-sized change** |
| **B** | **Wire `CommunicationParticipantIndexer` into `EmailUploadCaptureService`, then gate on participation** (§4.5b) | ✅ fully, and it is the check that actually fits the routes' purpose | One new ctor dependency + one call + backfill for existing rows; needs a new `OperationAccessPolicy`-adjacent concept ("participant read") | Medium. Cleanest *fit*; needs an owner decision on the new authorization concept |
| **C** | **Narrow, ship-today mitigation**: trim route 2's `Names` to candidate records the caller can actually read (`CallerRecordAccessProbe` + the existing entity-set map), leaving GUIDs to the client's documented id-fallback | ❌ partially — kills the **worst** disclosure (matter titles to non-participants) and nothing else | Small, no outage risk, no client change (`communicationSuggestionsService.ts:236-238` already falls back to the id) | Low. Does **not** close F9; F9 stays open behind A or B |

**Recommendation**: **C now, B next.** C removes the one disclosure that names a customer's confidential
matter to someone with no rights to it, at near-zero risk, today. B is the correct long-term gate and should
be its own task with the capture-pipeline change in scope. A is the most thorough but is really a data-model
decision about who owns a captured email, and belongs to the Communication project, not here.

**Not proceeding without a decision.** Criterion 3 cannot be met as written.

---

## 7. Client refusal handling — checked, and it is why §6 option C is safe (criterion 4)

Read-only inspection; no client file was modified.

| Caller | 404 handling | Non-404 (403/5xx) handling | Would a gate break the UI? |
|---|---|---|---|
| `communicationLookupService.ts:105-112` | → `null` = "not saved yet", the graceful path | re-throws → `useCreateTodoFromEmail.ts:197-203` sets `{kind:'error'}` and renders an error state | Not *broken*, but the Create-To-Do flow **stops**. A 404-shaped refusal would be invisible (user just saves their own copy) — which is why, **if** a gate is ever added, 404 (not 403) is the right refusal on the two by-message-id routes |
| `communicationSuggestionsService.ts:210-216` | → `null` ⇒ picker opens with no pre-selection (FR-B2 fallback) | re-throws; the ribbon path swallows it (`commands/index.ts:119` `.catch(() => null)`), `SaveFlow.tsx:903` does not | Best-effort by design — the most tolerant of the three |
| `useLinkedTodosForCommunication.ts:174-186` | no special case — any failure sets `error` | sets `error` from `detail`/`title`; `App.tsx:544-547` then shows the banner *because* `error !== null` | Renders a permissions message in a banner. Not broken, but user-visible |

**Direct consequence**: option **C** needs **no client change at all** — `toProvenanceDoc` already does
`...(names?.[c.targetId] ? { targetName: names[c.targetId] } : {})` and the shared `derivePrimaryReview`
falls back to the id, which is the code page's own un-resolved rendering.

---

## 8. Acceptance criteria — honest status

| # | Criterion | Status |
|---|---|---|
| 1 | Three handler bodies read; disclosure recorded verbatim; review claim confirmed/corrected | ✅ **met** — §1, §2. One claim exceeded, one doc-comment corrected |
| 2 | REPRODUCE-FIRST per disclosing route | ⚠️ **partially met** — reproduced at code level (§2, §3) and strengthened by §4; **no executable red test**, deliberately, reason in §3 |
| 3 | Each disclosing route carries a per-resource read check | ❌ **NOT met — escalated.** §4 shows the prescribed check cannot discriminate. §6 is the decision |
| 4 | Pane's three callers work, and handle refusal without a broken UI | ⚠️ **analysed, not exercised** (§7). No refusal exists to exercise. Findings pre-position the §6 options |
| 5 | SEED BOTH DIRECTIONS (RED then GREEN) | ❌ **NOT met** — nothing to seed; follows from 3 |
| 6 | Task 061's guard no longer flags `CommunicationsEndpoints.cs` | ❌ **NOT met, and not achievable here.** 061 is deferred; `RouteAuthorizationGuardTests.GovernedFiles` contains no `Api/Office/*` entry, so nothing flags this file today. Same residual task 062 recorded |
| 7 | Full BFF suite green, count reconciled; ArchTests; publish size; CVE | ⚠️ **N/A by construction** — **no source file changed**, so no suite/publish/CVE delta exists to measure. Baseline stands unchanged at **12,387 / 0 / 56**, ArchTests 191/191, publish 45.54 MB. Re-measure with the fix, not with this note. Stated rather than reported as a pass |
| 8 | Test scope justified | ✅ **met** vacuously — no test added |

**Four of eight criteria are unmet.** This task is **not complete** and is not marked ✅.

---

## 9. Two things the owner should know that are outside task 066

1. ⚠️ **Task 062's fix may be a live regression for this same user population.** 062 replaced the entity
   picker's app-only read with an **impersonated** one, so the picker now returns only rows the caller may
   read. `Spaarke Basic User` holds depth-**4** read on `sprk_Matter` / `sprk_Project` / `sprk_Invoice` — so
   that role is fine. But **`Spaarke Office Add In User` alone holds none of them**, and 062's own
   "all-types-failed ⇒ 500" rule means a user with only that role gets a **500, not an empty picker**. In dev
   this is masked because `Test User 1` also holds `Spaarke Basic User`. If any environment assigns the
   Office role *alone*, the picker breaks. Worth 10 minutes before deploy — and it is precisely the live
   verification 062 recorded as its own unmet criterion (§8 item 2 of `062-entity-search-trim.md`).
2. **`EmailUploadCaptureService` does not write participant rows** (§4.5b). Independent of authorization,
   that means add-in-saved emails are absent from the `participant=` search surface task 051 built and from
   the ADR-048 rollup. That is a data-completeness defect worth its own issue regardless of what §6 decides.

---

## 10. Provenance of every live fact in §4

All via the Dataverse MCP connection to the **dev** environment on 2026-09-21. Queries are reproducible:

| Fact | Query |
|---|---|
| Owner census of all `sprk_communication` | `SELECT owneridname, COUNT(sprk_communicationid) FROM sprk_communication GROUP BY owneridname` |
| Roles granting communication read + depth | `SELECT role.name, roleprivileges.privilegedepthmask FROM roleprivileges JOIN role … WHERE roleprivileges.privilegeid = 'effd0e4a-e9a9-425b-88b9-99936d8b4868'` |
| `Spaarke Office Add In User`'s `sprk_*` reads | `… WHERE role.name = 'Spaarke Office Add In User' AND privilege.name LIKE 'prvReadsprk_%'` |
| `Test User 1`'s roles | `… WHERE systemuser.domainname = 'testuser1@spaarke.com'` |
| To-do + matter/project/invoice/document read grants | `… WHERE privilege.name IN ('prvReadsprk_ToDo','prvReadsprk_Matter','prvReadsprk_Project','prvReadsprk_Invoice','prvReadsprk_Document')` |

⚠️ **Dev only.** Another environment may assign roles differently — but §4.3's conclusion does **not** depend
on role assignment, only on the fact that rows are app-owned, which is a property of the *code*.
