# Task 065 — F4: the no-`TargetEntity` save bypasses `EntityAccessFilter` entirely

**Finding**: `notes/fable-review-2026-09-21.md` §2 **F4** · **Date**: 2026-09-21 ·
**Outcome**: 🔔 **ESCALATED — partially shipped.** The bypass is **reproduced and proven**, the false
premise in the code is **corrected**, the record-less branch is **narrowed** from one tenant-wide
container to one per business unit, and the account/contact case is **defined and pinned**. The
task's headline change — **making `TargetEntity` required — was NOT shipped**, for three independent
reasons, each verified rather than argued. §6 is the escalation.

---

## 0. TL;DR for a reviewer

| Criterion | Status |
|---|---|
| 1 REPRODUCE-FIRST | ✅ §2 — verbatim, three observations |
| 2 `TargetEntity` required | ❌ **NOT MET — ESCALATED** (§6) |
| 3 Ribbon sends a real target + acting-user container | ⚠️ **HALF.** Container: ✅ shipped (§4). "Sends a real target": ❌ escalated — there is no target to send (§6.1) |
| 4 account/contact defined + tested | ✅ §5.3 |
| 5 False comments corrected | ✅ §3 |
| 6 Seed both directions | ✅ §5.4 — and §6.4 seeds the *escalated* requirement to measure what it costs |
| 7 Full-suite reconciliation | ✅ §7 — **12,411 passed / 0 failed / 56 skipped**; Δ = exactly this task's 8 |
| 8 Task 061's guard no longer flags this route | ❌ **NOT MET, and not meetable here** — §8 item 1 |
| 9 ArchTests / publish size / CVE | ✅ §7 |
| 10 Test scope justified | ✅ §5.5 |

---

## 1. Placement Justification (CLAUDE.md §10 / `.claude/constraints/bff-extensions.md`)

All production change is **one method body plus its documentation** inside an existing BFF service
(`Services/Office/OfficeService.ResolveContainerAsync`). **No new endpoint, no new service, no new
interface, no new DI registration, no new package, no new background work, no new Dataverse or SPE
surface.** Nothing crosses the `Services/Ai/PublicContracts/` boundary.

Three-question test (CLAUDE.md §11) — asked for the **container derivation**, the only thing that
could have been a new component:

1. **Existing** — `RecordContainerResolver.ResolveForActingUserAsync` already answers exactly this
   question ("which container, when there is no owning record?"). Verified by
   `grep -rn "ResolveForActingUserAsync" src tests`: two production consumers already
   (`Api/OBOEndpoints.cs:252`, `Services/Compose/ComposeService.cs:1310`) plus a dedicated test suite.
2. **Extension** — nothing to extend: the call is **reused verbatim**, on a resolver already injected
   into `OfficeService` (`_containerResolver`, ctor param since task 085). The diff adds a call, not a
   capability.
3. **Cost of doing nothing** — concrete: every Word ribbon quick-save, every pane save with no
   "Related to" selected, and the create half of the pane's document flow all land in **one
   tenant-wide container shared by every business unit** (`EmailProcessing:DefaultContainerId`,
   provisioned from `SPE_DEFAULT_CONTAINER_ID`). Demonstrated in §2, demonstrated narrowed in §5.2.

**Hot path**: BFF **Y** · SpaarkeAi N · ci-workflows N · skill-directives N · root-CLAUDE N.

**§6.5 ADR conflict**: none arose. ADR-008 keeps the association check in `EntityAccessFilter`; this
task removed no check and moved none. The escalation in §6 is a **requirements/sequencing** matter,
not an ADR tension.

**Publish-size + CVE**: §7.

---

## 2. REPRODUCE-FIRST (criterion 1) — verbatim

No deployed environment is in this worktree's loop, so the reproduction is a test run against
**shipped code, before any change**. New file
`tests/integration/contract/Api/Office/OfficeSaveNoTargetContainerContractTests.cs`, driven through
the real route, the real filter chain and the real `OfficeService`; the only doubles are module
boundaries (`CallerRecordAccessProbe`'s `virtual` seam, the `SpeFileStore` facade,
`IDataverseService`).

```
Failed!  - Failed:     1, Passed:     7, Skipped:     0, Total:     8, Duration: 10 s - Sprk.Bff.Api.Tests.dll (net10.0)
```

**All three F4 reproduction tests PASSED against unchanged code** — which is the point: they assert
the defective behaviour. The single failure was §5.2's not-yet-implemented narrowing.

### (a) Accepted, and **no resource authorization runs at all**

`PostOfficeSave_WithNoTargetEntity_IsAccepted_AndNoResourceAuthorizationRuns` — green pre-change. The
request returns 202 and `ProbedRecords` is **empty**. `CallerRecordAccessProbe` is the single
primitive behind every per-record check on this route (`EntityAccessFilter`,
`TodoSourceAccessFilter`, `QuickCreateSourceAccessFilter`), so an empty recording is the direct
statement of "no resource authorization ran" — not a proxy for it. The route's one other resource
gate, `OfficeVersionSaveAuthorizationFilter`, is scoped to version saves and this body is not one.

### (b) It reaches the tenant-wide default container, **and the ProcessingJob row records that it did**

The failure output of the AI-processing test printed the created job verbatim. This is the single
best piece of evidence in the task — F4 in one row:

```
{"Name":"Document Save - 2026-09-21 21:47:15","JobType":2,"Status":0,"Progress":0,
 "IdempotencyKey":"041f3c7ff68445f38ea3706d2afae358",
 "CorrelationId":"6d19fcb1-7406-4ec4-ad96-5525eca6591d",
 "Payload":"{\"ContentType\":\"Document\",\"TargetEntity\":null,
             \"ContainerId\":\"b!test-office-save-drive\",
             \"Email\":null,\"Attachment\":null,
             \"Document\":{\"FileName\":\"quick-save.docx\",\"Title\":\"quick-save\", … },
             \"TriggerAiProcessing\":true}"}
```

`TargetEntity: null` · `ContainerId` = the configured `EmailProcessing:DefaultContainerId` ·
`TriggerAiProcessing: true`. The upload double recorded the same container id.

> ⚠️ That verbatim block is also **why the AI assertion first failed**, and it is worth recording:
> `Payload` is a **nested JSON string**, so serializing the job once leaves every inner quote escaped
> (`"`). The first assertion was matching against the outer encoding rather than the payload.
> Fixed by parsing the job and reading `Payload` out, so the test asserts the value and not an
> encoding artefact. Same family as task 064 §10's derived-scalar lesson: the observation had a
> **parser** to get wrong, and it did.

### (c) It is profiled and RAG-indexed

`SaveRequest.TriggerAiProcessing` and `AiProcessingOptionsRequest.RagIndex` both default `true`
(`Models/Office/SaveRequest.cs`), asserted directly, and the payload above carries
`TriggerAiProcessing: true` forward to the finalization worker. **Stated honestly**: the indexing
itself runs in that background worker, which this test host does not execute — what is proven is the
*instruction* that reaches it, written into a Dataverse row.

**So: accepted → tenant-wide container → queued for profiling + RAG indexing, with no per-record
authorization anywhere in the path.** F4 confirmed in full.

---

## 3. THE FALSE PREMISE, corrected (criterion 5)

Two comments asserted that the no-target branch *"exists for the contract rather than for traffic"*
and that acting-user resolution was unnecessary because *"Office save always carries a TargetEntity
from the shipped add-in"*. **Both were false, and had been since task 037.** Corrected in place, with
the enumeration that disproves them, at:

- `Services/Office/OfficeService.cs` — `ResolveContainerAsync`'s `<remarks>` (was `:154-158`)
- `Services/Office/OfficeService.cs` — the `SERVER-DERIVED CONTAINER` comment block at the call site
  (was `:436-449`)

Two further stale claims were found and amended rather than left to rot, because both assert the same
false premise in places a reviewer would trust:

- `tests/integration/auth/UnifiedAccessControl/OfficeSaveContainerProvenanceTests.cs` — its third
  test's `<remarks>` said the no-record container "is NOT derived from the acting user's business
  unit" and that the derivation "was deliberately not implemented". Amended, **stacked rather than
  rewritten**, so a reader sees the claim existed, had a real argument behind it, and had its scope
  narrowed rather than overturned. The test's own *assertion* is unchanged and still passes.
- `tests/Spaarke.ArchTests/SpeWriteSinkContainerProvenanceGuardTests.cs` — the `UploadSmallAsync`
  sink's provenance string said the path falls back to `EmailProcessing:DefaultContainerId` "when no
  target entity is named". Now states the acting-user step. Provenance class is unchanged
  (`ServerDerivedRecord`): both branches are server-derived, which is what that guard asks.

**The argument that rejected acting-user derivation was not overturned — its scope was stated.**
`RecordContainerResolver.ResolveForRecordAsync`'s remarks (users sit in the Operations subtree while
secure records are owned in Secure Projects, so acting-user resolution writes a secure record's
content into the general Operations container) are about a save that **names a record**, and that
case is untouched — pinned by a test that fails if it ever is (§5.2).

---

## 4. What shipped

### `Services/Office/OfficeService.cs` — the only production change

`ResolveContainerAsync` gains an `actingUserObjectId` parameter (the caller's `oid`, as
`OfficeAuthFilter` already resolved it and `SaveAsync` already holds it) and an `else` branch:

```
no TargetEntity
  → RecordContainerResolver.ResolveForActingUserAsync(oid)      ← task 076's owner-sanctioned shape
      → business unit's sprk_containerid, if stamped            ← the narrowing
      → business unit with none stamped  → fall through
      → SdapProblemException (caller identity) → log Warning, fall through
  → EmailProcessing:DefaultContainerId                          ← last resort, still fail-closed when unset
```

Three properties a reviewer should check, because each is a decision:

1. **Confined to the no-record branch.** A save naming a record is byte-for-byte unchanged, and a
   test fails if that ever stops being true (§5.2). This is what keeps §3's isolation argument intact.
2. **Strictly non-regressive.** Every path that reaches the configured default today still reaches
   it. The narrowing can only *improve* placement — it never turns a working save into a refusal.
3. **The `SdapProblemException` catch is deliberate, and narrow.** `ResolveForActingUserAsync`
   throws for a caller who maps to no Dataverse user, to more than one, or to one with no business
   unit. Those are **caller-identity** failures, not isolation failures: this branch is only reached
   when there is no record, and the resolver's own contract states that `FailClosed` is *unreachable
   here by construction* because nothing on this path **can** be secure. Refusing would convert an
   unprovisioned user into a total quick-save outage — for every Word ribbon click, every pane save
   with no "Related to" selected, and the create half of the pane's document flow (§5.1). The
   **record** branch keeps its no-catch posture, for the opposite reason: there, a refusal *is* the
   isolation guarantee.

### Not changed, deliberately

- **`src/client/office-addins/**`** — no client change. See §6.1: there is no target for the Word
  ribbon to send, and inventing one is an owner decision.
- **`Api/Office/OfficeEndpoints.cs`** — untouched (`git diff` reports no change). Requiring the
  target is the escalated half.
- **`Api/Filters/EntityAccessFilter.cs`** — untouched. Its absent-target pass-through is the bypass,
  and removing it without the endpoint requirement and the role grants would be worse than the
  finding.
- **`tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.cs`** — contested with another project,
  owned by deferred task 061. Not touched. §8 item 1.

---

## 5. Tests

### 5.1 Enumeration of every in-repo caller of `POST /office/save` (done BEFORE any contract thinking)

`grep -rn "office/save" src tests`. **Three production client paths, and the majority send no target:**

| # | Caller | Sends `targetEntity`? |
|---|---|---|
| 1 | `outlook/commands/index.ts:138` — Outlook ribbon quick-save | ✅ **always.** `buildEmailSaveRequest` files to the association engine's predicted record; with **no** prediction it opens the pane rather than filing a guess |
| 2 | `word/commands/index.ts:153` — **Word ribbon quick-save** | ❌ **never.** `buildDocumentSaveRequest` (`quickSaveHelpers.ts:179-200`) omits it entirely — task 037 / FR-17 |
| 3 | `shared/taskpane/hooks/useSaveFlow.ts:1109` — the task pane, both hosts, all three content types | ⚠️ **conditionally.** `if (sentEntity?.id)` (`:968`) — none when no "Related to" is selected; **and** `sentEntity = versionTarget ? null : selectedEntity` (`:941`) — **an FR-11 version save NEVER sends one** |

**(3)'s version-save exclusion is a documented OWNER DECISION, not an oversight** (task 023 D-4/D-5,
stated in the code): a version save "writes to the existing document's own item and never
re-associates it", so a picker selection "would be inert — *and would add an unrelated authorization
check (`EntityAccessFilter`) that could refuse a legitimate version save*". It is authorized instead
by `OfficeVersionSaveAuthorizationFilter`, requiring `write` on the document. **That is a real
resource-authorization gate**, so the version save is not part of F4 at all — but it **would** be
broken by F4's fix.

Non-client callers, checked so the enumeration is complete rather than plausible:

- `tests/load/office-endpoints.k6.js:218` — sends a target.
- `tests/e2e/specs/{word,outlook}-addins/save-flow.spec.ts` + `tests/e2e/pages/addins/WordTaskPanePage.ts`
  — **never reach the BFF**: every reference is a `page.route(...)` network intercept.
- 27 server-side test files post to the route. The rest of the `src/server` hits are the endpoint
  itself, its ProblemDetails helpers, the rate-limit config and filter doc comments.

### 5.2 The new suite — 8 tests

`tests/integration/contract/Api/Office/OfficeSaveNoTargetContainerContractTests.cs`. ADR-038 KEEP path
`tests/integration/contract/**`; no `Mock<HttpMessageHandler>`, no DI-registration assertion, no ctor
null-check (B1/B16/B17).

| § | Test | Proves |
|---|---|---|
| 1 | `…IsAccepted_AndNoResourceAuthorizationRuns` | **F4's headline.** Accepted, and the probe is never consulted |
| 1 | `…AndAnUnresolvableCaller_StillLandsInTheConfiguredDefaultContainer` | the tenant-default landing — **and the non-regression pin** for the ≈80 existing save tests, whose caller `oid` is not a GUID |
| 1 | `…RequestsAiProcessingAndRagIndexingByDefault` | both flags default true; the ProcessingJob payload carries the instruction forward |
| 2 | `…AndAResolvableCaller_LandsInThatCallersBusinessUnitContainer` | **the narrowing.** The RED half of §5.4's seed |
| 2 | `…WhenTheCallersBusinessUnitHasNoContainer_FallsBackToTheConfiguredDefault` | an unstamped business unit (3 of 6 live units) is a configuration state, not an outage |
| 2 | `…WithATargetEntity_DoesNotConsultTheActingUsersBusinessUnit` | **the confinement.** Fails if the narrowing ever reaches a record-bearing save — i.e. if §3's isolation argument is ever violated |
| 3 | `…TargetingAccountOrContact_…` ×2 | §5.3 |

The resolver stays **real** (it is `sealed` precisely so it cannot be mocked); only its Dataverse
boundary is arranged, through the shared `TestActingUserBusinessUnit`, whose matchers pin the actual
derivation (`systemuser` filtered on `azureactivedirectoryobjectid` → that user's `businessunit` →
`sprk_containerid`). A production regression that looked the user up by a different column would
**stop matching and fail**, which a constant-returning double could not give.

### 5.3 The account/contact case — explicitly defined (criterion 4)

`account` and `contact` are accepted association targets that `sprk_document` has **no lookup column
for**, so the association is dropped at persistence. **Task 065 does not change that** — it is a
standing owner decision recorded at `OfficeEndpoints.ValidateSaveRequest` (accept + log loudly rather
than silently reject a user-visible flow). What was **undefined** is where the bytes go, and the
plausible wrong reading is that "no lookup column" makes them behave like a no-target save. **Both
halves of that reading are wrong, and are now pinned by test:**

| Question | Defined behaviour |
|---|---|
| Are they authorization-gated? | **Yes** — `EntityAccessFilter` probes `accounts`/`contacts` and requires `AppendTo`, exactly as for a matter. A document that cannot be *associated* to a record can still be *filed against* it, so skipping the gate would be wrong |
| Which container? | **The record's** (`ResolveForRecordAsync`), via that account's/contact's own owning business unit. They **never** reach the record-less branch and never reach the tenant default by way of it |
| Is the association written? | **No** — no lookup column. Logged loudly at both persistence sites. Unchanged, owner-decided |

Relevant to §6: `account` and `contact` are also the **only two** target types whose `AppendTo` the
shipped Spaarke roles actually grant (§6.3).

### 5.4 Seed, both directions (criterion 6)

**SEED-065-A** — the narrowing disabled (`ResolveForActingUserAsync` replaced by an empty decision),
everything else intact:

```
Failed!  - Failed:     1, Passed:     7, Skipped:     0, Total:     8, Duration: 11 s - Sprk.Bff.Api.Tests.dll (net10.0)

Sprk.Bff.Api.Tests.Api.Office.OfficeSaveNoTargetContainerContractTests.PostOfficeSave_WithNoTargetEntity_AndAResolvableCaller_LandsInThatCallersBusinessUnitContainer [FAIL]
  Expected factory.UploadedToContainers {"b!test-office-save-drive"} to contain "b!acting-user-bu-container-0001"
```

**Identical to the pre-change run in §2** — same count, same single test, same message. That
agreement is the evidence: the test fails because the **narrowing** is absent, not because something
else in the change is missing. The other seven — including all three F4 reproductions — stay green,
which is the honest statement that this change did not close F4.

**Restored GREEN**, after `touch` + an explicit build (task 064 §7.1's trap: a `cp` restore preserves
the backup's older timestamp, MSBuild then skips the rebuild, and `--no-build` runs the *seeded*
binary while `grep` says the source is clean):

```
Build succeeded.  0 Warning(s)  0 Error(s)
Passed!  - Failed:     0, Passed:     8, Skipped:     0, Total:     8, Duration: 12 s - Sprk.Bff.Api.Tests.dll (net10.0)
```

`grep -c 'SEED-065' OfficeEndpoints.cs OfficeService.cs` → `0`, `0`.

**SEED-065-B** is the *escalated* requirement, seeded to **measure** rather than argue — §6.4.

### 5.5 Test-scope justification (criterion 10)

The eight cover exactly the stated scope: the record-less contract, the ribbon's path, the
account/contact case, the unchanged happy paths, and the seed direction. Two go beyond the literal
list and each earns its line: `…WithATargetEntity_DoesNotConsultTheActingUsersBusinessUnit` is the
**only** test that fails if the narrowing ever leaks onto a record-bearing save — the one regression
that would turn this change into the isolation failure it is built to avoid — and
`…WhenTheCallersBusinessUnitHasNoContainer…` pins the configuration state (half the live business
units) that a stricter reading of "derive it server-side" would have turned into an outage.

---

## 6. 🔔 ESCALATION — why `TargetEntity` was NOT made required

> 🔔 **Human Input Required** (CLAUDE.md §6). The POML's own `<escalation><trigger>` fired — **both
> triggers**, plus a third condition the POML could not have known about. Each reason below is
> independently sufficient; together they mean shipping the requirement would be a **save outage**,
> not a tightening.

### 6.1 There is no target for the Word ribbon to send (POML trigger 2)

Criterion 3 asks the ribbon to "send a real target". A Word ribbon click carries **no Spaarke
context**: no record is selected, no pane is open, and unlike Outlook there is **no association-engine
prediction for documents** to stand in for one (stated in `quickSaveHelpers.ts:110-116` and
`word/commands/index.ts:128-133`). Giving it a target requires one of:

| Option | Why it is an owner decision, not this task's |
|---|---|
| **(a)** a document-side association predictor | New surface. POML trigger 2 verbatim |
| **(b)** the ribbon opens the pane instead of quick-saving, as Outlook's does when it has no prediction | Symmetric, needs no new surface, and is arguably the right answer — but it **deletes FR-17's one-click behaviour**, which task 037 shipped deliberately |
| **(c)** resolve a container by acting-user BU and keep saving unfiled | ✅ **this is what shipped** — but it is a *placement* fix, and leaves F4 open |

### 6.2 Two more shipped paths legitimately save without an association (POML trigger 1)

Checked before concluding, as the trigger requires (§5.1). The decisive one is the **FR-11 version
save**: it must **not** carry a target, by owner decision D-4/D-5, precisely *because* a target would
add an `EntityAccessFilter` check that could refuse a legitimate version save. Requiring the target
breaks **every version save** — and the version save is already gated, by
`OfficeVersionSaveAuthorizationFilter` on `write` over the document, so requiring the target would
trade a real gate for a worse one. The third path (pane save with no "Related to" selected) is a
user-visible flow change.

### 6.3 🔴 The roles do not grant what the gate demands — verified live today

**What `EntityAccessFilter` demands**: `AccessRights.AppendTo` on the **target record**
(`OperationAccessPolicy["entity.associate_document"] = AccessRights.AppendTo`), evaluated by
`CallerRecordAccessProbe.GetCallerRightsAsync` → OBO `RetrievePrincipalAccess` against the target's
own collection.

**What the shipped roles grant** — live Dataverse queries over
`roleprivileges ⋈ privilege ⋈ role`, 2026-09-21:

| Privilege | Roles that grant it | Depth | Either Spaarke end-user role? |
|---|---|---|---|
| `prvAppendTosprk_Matter` | Service Writer · System Administrator · System Customizer | 8 | ❌ **no** |
| `prvAppendTosprk_Project` | same three | 8 | ❌ **no** |
| `prvAppendTosprk_Invoice` | same three | 8 | ❌ **no** |
| `prvAppendTosprk_WorkAssignment` | same three | 8 | ❌ **no** |
| `prvAppendTosprk_Event` | same three | 8 | ❌ **no** |
| `prvAppendToAccount` | + **Spaarke Basic User**, **Spaarke Office Add In User** | 1 | ✅ yes |
| `prvAppendToContact` | + **Spaarke Basic User**, **Spaarke Office Add In User** | 1 | ✅ yes |

All Spaarke-named roles were enumerated (`role.name LIKE 'Spaarke%'` → Basic User, Office Add In
User, AI Analysis User/Admin, Reporting Access ×3, Provisioning Registry); none of the others is an
end-user save role.

**Two consequences, in order of severity:**

1. 🔴 **The targeted save path already refuses ordinary users, on shipped code.** Filing a document to
   a **Matter** — the flagship flow — requires `AppendTo` that no Spaarke end-user role grants. This
   is **not** something task 065 introduces; it is a pre-existing condition that the task surfaced.
   **It is the same finding as `notes/role-grant-gap-2026-09-21.md`, one face further on**, and it
   should be added to that note's §1 table as a sixth row.
2. 🔴 **Therefore the no-target bypass is currently the only general-purpose save path a non-admin
   has.** Stated precisely, because the exception matters: the two types a Spaarke end-user role *does*
   grant `AppendTo` on are `account` and `contact`, at **depth 1 — own records only**, and those are
   exactly the two types `sprk_document` has **no lookup column for** (§5.3), so filing to one persists
   the document **unassociated** anyway. So the only targets an ordinary user can name today are the two
   for which naming a target does nothing. Requiring the target would route every save through a gate
   that refuses everyone without System Administrator / System Customizer / Service Writer. That
   converts a silent authorization bypass into a **total save outage** — categorically worse than the
   finding.

**Bounded uncertainty, stated rather than glossed**: `RetrievePrincipalAccess` reports rights from
record **sharing** (POA) as well as from role privileges, so a user could in principle hold `AppendTo`
on an individually-shared record without the role privilege. No shipped Spaarke flow creates such
shares, and a per-record share cannot underwrite a general-purpose save path — but this was **not**
exhaustively verified, and it is the one way the table above could overstate the impact. **Verified in
dev only**; production roles were not examined.

### 6.4 SEED-065-B — the cost of the requirement, measured

Rather than assert the breakage, it was seeded: `ValidateSaveRequest`'s `else` (no target) made to
return `OfficeInvalidAssociationTarget`, then the Office suites run.

```
Failed!  - Failed:     9, Passed:   386, Skipped:    10, Total:   405, Duration: 2 m
```

Five are this task's own no-target tests. **The other four are all PANE VERSION SAVES** — §6.2's
owner decision, failing exactly as predicted:

```
Api.Office.OfficeSaveAddInWireContractTests.Post_OfficeSave_PaneVersionSaveBody_Returns202_WritesAVersion_AndLeavesExactlyOneRow [FAIL]
  Expected response.StatusCode to be HttpStatusCode.Accepted {value: 202}, but found HttpStatusCode.BadRequest {value: 400}.
Api.Office.OfficeSaveAddInWireContractTests.Post_OfficeSave_PaneVersionSave_WithAnUppercaseId_IsCanonicalizedAtTheServerBoundary [FAIL]
Api.Office.OfficeSaveAddInWireContractTests.PaneRetryAfterALockedRefusal_WithTheNextAttemptKey_WritesTheVersion [FAIL]
Api.Office.OfficeSaveAddInWireContractTests.SuccessivePaneRevisions_EachWithItsOwnKey_AreEachWritten_AndAnIdenticalResendIsNot [FAIL]
```

Seed removed; `grep -c SEED-065` → `0` in both files, and `git diff` reports `OfficeEndpoints.cs`
identical to HEAD.

> **Stated honestly, because the number is lower than the argument implies.** Only **9** tests broke,
> not the ~80 the corpus-wide grep suggested. The eight `OfficeVersionSave*` data-mutation suites
> (80 tests) stayed green under the seed — re-run under it and confirmed
> `Passed! - Failed: 0, Passed: 80`. They do not exercise `ValidateSaveRequest` over the wire, so
> the ~85-post grep count is a count of **string occurrences**, not of HTTP posts that would break.
> **The count is therefore a floor, not a measurement of production impact** — and it is production
> impact, not the test count, that the escalation rests on. The four that did break are the ones that
> matter, because they are the *shipped pane behaviour* the owner decided must send no target.

### 6.5 Proposed sequencing

| # | Step | Owner |
|---|---|---|
| 1 | **Grant the missing rights** — `AppendTo` on `sprk_matter`/`sprk_project`/`sprk_invoice`/`sprk_workassignment`/`sprk_event` to `Spaarke Office Add In User`, **depth chosen deliberately per table**. Prerequisite for the pane's *existing* Matter filing, independently of this task | Security-role change |
| 2 | Decide the Word ribbon's behaviour: **(b)** open the pane when there is no target (recommended — symmetric with Outlook, no new surface) or **(a)** build a document-side predictor | Owner / product |
| 3 | **Then** require `TargetEntity`, with an explicit carve-out for the FR-11 version save (`OfficeService.IsVersionSave` — the same predicate `OfficeVersionSaveAuthorizationFilter` already scopes on, so the gate and the requirement cannot disagree about what a version save is), and invert §2's reproduction tests | A follow-on task |
| 4 | Make refusals legible — `OFFICE_009`'s body already names "Append To" and a remedy; confirm the pane renders it (`errorMessages.ts` prefers `problem.detail`) | Small |

**Do not revert steps already taken.** Tasks 062/063/064 and this one each closed or narrowed a real
gap; the missing role grants predate all of them and were merely made visible.

---

## 7. Verification (real output)

| Check | Result |
|---|---|
| `dotnet build src/server/api/Sprk.Bff.Api/` | `Build succeeded. 0 Warning(s) 0 Error(s)` |
| New suite, post-change | `Passed! - Failed: 0, Passed: 8, Skipped: 0, Total: 8` |
| New suite, pre-change (reproduce-first control) | `Failed! - Failed: 1, Passed: 7` |
| New suite, SEED-065-A (narrowing disabled) | `Failed! - Failed: 1, Passed: 7` — identical set |
| `tests/Spaarke.ArchTests` | `Passed! - Failed: 0, Passed: 191, Skipped: 0, Total: 191` |
| `dotnet list package --vulnerable --include-transitive` | `The given project 'Sprk.Bff.Api' has no vulnerable packages given the current sources.` **No package added or upgraded by this task** |

### Full-suite reconciliation (criterion 7)

Branch baseline after tasks 062/063/064: **12,403 passed / 0 failed / 56 skipped / 12,459 total**.

**Run 2 — the authoritative one, on the exact tree being committed**, preceded by an explicit
`dotnet build … Build succeeded. 0 Warning(s) 0 Error(s)` and run with nothing else on the machine:

```
Passed!  - Failed:     0, Passed: 12411, Skipped:    56, Total: 12467, Duration: 9 m 35 s - Sprk.Bff.Api.Tests.dll (net10.0)
```

**12,403 → 12,411 passed; 12,459 → 12,467 total; Δ = +8 = exactly this task's 8 new tests. 0 failed,
56 skipped — both unchanged.** No test was deleted, skipped or silently repaired.

**Run 1 is recorded rather than rounded away**, because a green final run is not a licence to delete
a red earlier one:

```
Failed!  - Failed:     1, Passed: 12410, Skipped:    56, Total: 12467, Duration: 10 m 31 s
Integration.SseStreamingIntegrationTests.Cancellation_NoLingeringBackgroundTask_AfterClientAbort [FAIL]
```

A cancellation-*timing* test on the SSE streaming path — no Office endpoint, no `OfficeService`, no
container code anywhere in its stack — and run 1 executed while a concurrent `git worktree add` plus
a Release publish of `origin/master` were saturating the machine. Re-run in isolation immediately
afterwards: `Passed! - Failed: 0, Passed: 18, Total: 18, Duration: 295 ms`. Run 2 above then executed
the whole suite serialized and clean. **Three independent observations, not one**; had run 2 still
shown it, this section would say so and the commit would not have been made.

> A run started between these two was **discarded rather than quoted**, because two doc-comment
> corrections landed in the tree while it was executing under `--no-build`: its number would have
> described a build that no longer matched the source. That is task 064 §7.1's trap in its other
> direction — there, a stale binary made a seeded run look clean; here it would have made a clean run
> unverifiable. Run 2 was started only after a fresh build of the final tree.

### Publish size — fresh build of `origin/master`, not the recorded number (CLAUDE.md §10 bullet 4)

| Field | Value |
|---|---|
| Command | `dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o <out>` |
| RID / mode | framework-dependent **linux-x64** (from the csproj), not self-contained |
| Compression | PowerShell **`Compress-Archive -CompressionLevel Optimal`**, the method `scripts/Deploy-BffApi.ps1` uses |
| PDBs | included |
| `origin/master` @ `99cdfe2ea`, freshly built + zipped today in a throwaway worktree | **45.46 MB** |
| This branch | **45.54 MB** |
| **Delta** | **+0.08 MB** |

Well under the **+5 MB** single-task escalation threshold and the **60 MB** ceiling. This is the same
pair of figures task 064 measured against the same master SHA earlier today — which is the expected
result, and is itself the evidence that this task's own contribution is ~0: two independent tasks
measuring the same branch-vs-master delta after one of them added ~60 lines of C# and no package.

The recorded 2026-09-02 baseline (45.42 MB @ `a826cf347`) was **not** used for the diff; master was
rebuilt from scratch at its current SHA, as the rule requires. This task adds **no package** and
~60 lines of C#; its own IL contribution is a few KB. The branch figure is the **whole branch** vs
master, so it is an **upper bound** on this task's contribution, not an under-count.

---

## 8. Recorded honestly — what is NOT met

1. 🔴 **Criterion 2 (`TargetEntity` required) is NOT met.** Escalated — §6. This is the task's
   headline and it is deliberately unshipped; **F4 remains open**, and §2's tests assert the
   defective behaviour rather than its absence so that whoever lands the requirement has the
   reproduction ready to invert.
2. 🔴 **Criterion 3 is HALF met.** The container half shipped; "sends a real target" did not, and
   cannot without an owner decision (§6.1).
3. 🔴 **Criterion 8 ("task 061's guard no longer flags this route") is NOT met and is not meetable
   here** — identical in shape to tasks 062's and 064's item 1. The POML gates 065 on 061; 061 is
   deliberately deferred because `RouteAuthorizationGuardTests.cs` is contested with another project,
   and this task was instructed not to touch it. That guard governs **no Office route today**, so it
   does not flag this one and there is nothing to un-flag. When 061 lands, its census must classify
   `POST /office/save` as **partially gated**: route-level by `EntityAccessFilter` **only when a
   target is named**, and by `OfficeVersionSaveAuthorizationFilter` only for version saves — a
   classification the census needs a word for, because "has an authorization filter" is true and
   misleading.
4. 🔴 **No live verification.** Nothing here was exercised against a real Dataverse from this
   worktree, with one exception: §6.3's role queries, which **were** run live. Specifically unproven:
   that `ResolveForActingUserAsync` resolves for a real Office add-in caller in dev. Its failure
   direction is the configured default — i.e. today's behaviour — so the risk is "no improvement",
   not "outage". **Someone should quick-save from the Word ribbon against dev and confirm the
   document lands in the business-unit container.**
5. **The narrowing does not close F4, and must not be read as doing so.** Both containers are
   *shared*; the change reduces the blast radius from per-tenant to per-business-unit. The
   authorization hole is untouched.
6. **A known residual carries over unchanged** (`ResolveForActingUserAsync`'s own remarks): content
   placed in a business-unit container and LATER associated to a secure record is already in the
   shared container, and SPE permissions are additive-only, so nothing retracts it. Filed as its own
   project by owner direction 2026-08-31
   (`notes/finding-secure-transition-container-migration.md`). This task **widens the set of content
   that can hit that residual** — previously that content sat in the tenant default, which has the
   same property, so the residual's *shape* is unchanged, but its owner should know the Word ribbon
   now feeds it.
7. **`notes/role-grant-gap-2026-09-21.md` needs a sixth row.** §6.3's `AppendTo` finding is the same
   systemic gap and belongs in that note's §1 table. Not edited here — it is another task's
   deliverable, and appending to it unilaterally would obscure whose evidence is whose.

---

## 9. Step 9.5 quality gates

| Gate | Result |
|---|---|
| `adr-check` | **0 violations.** **ADR-008** — the association check stays in `EntityAccessFilter`; this task moved no authorization into the handler and added no filter ✓ · ADR-001 (no new endpoint) ✓ · **ADR-003** (fail-closed preserved where it is an isolation guarantee — the record branch's `FailClosed` throw and the both-unavailable refusal are unchanged; the one new catch is caller-identity on a branch where nothing can be secure, argued in §4) ✓ · ADR-004/ADR-028 (no new credential; `ResolveForActingUserAsync` uses the existing app-only Dataverse client, as its two existing consumers do) ✓ · ADR-007 (SPE access still through the `SpeFileStore` facade) ✓ · ADR-010 (no new interface, no new DI registration) ✓ · ADR-013 (no AI-internal type in CRUD code) ✓ · ADR-019 (error shape unchanged) ✓ · ADR-029 (publish size measured against a fresh master) ✓ · ADR-032 (no feature-gated registration introduced; `RecordContainerResolver` is registered unconditionally by design) ✓ · **ADR-038** (new tests at the `tests/integration/contract/**` KEEP path; doubles are module boundaries only; no banned shapes) ✓ · ADR-044 (ids typed `Guid`; the caller `oid` is a lookup KEY into `azureactivedirectoryobjectid`, never compared to a `systemuserid` — the `CallerIdentityGuardTests` Rule 2 defect class) ✓ |
| `code-review` | **0 critical.** No secrets · no new input-validation surface · no N+1 (the no-record branch costs at most 2 reads, on a branch that previously made 0 — see below) · no sync-over-async · **the one new `catch` is narrow, typed, logged at Warning, and argued in both the code and §4** — it wraps only the resolution call, never the save · no code-restating comments. **One observation accepted**: `ResolveContainerAsync`'s `<remarks>` is now longer than its body. Per CLAUDE.md §11.5 this is evaluated on cohesion, not length: one responsibility, one reason to change. The remarks carry the enumeration that a reader **cannot re-derive from the code** — and the fact that the previous, much shorter comment asserted a falsehood for a month is the argument for writing it down rather than trusting the next reader to re-check three client files. **Latency**: +1–2 Dataverse reads on a record-less save (systemuser query + business-unit read), on a path that already performs a Dataverse job create and an SPE upload. Not measured live |
| Lint / build | `Build succeeded. 0 Warning(s) 0 Error(s)` |
