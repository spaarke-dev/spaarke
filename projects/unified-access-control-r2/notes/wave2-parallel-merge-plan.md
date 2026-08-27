# Merge plan — parallel batch (073 · 079 · Wave 2 075→076)

> **Created 2026-08-26.** Three `task-execute` agents ran in isolated worktrees. This is the
> integration checklist. **Nothing has been merged yet.** Written to survive compaction — if you are
> reading this after a context reset, this file plus `current-task.md` is the complete state.

---

## 1. Worktree inventory

| Task | Worktree branch | Commit | Status |
|---|---|---|---|
| **073** container-keyed write retirement | `worktree-agent-a088c001ee9c915f9` | `dd3e38f6d` | ✅ shipped · both gates returned |
| **079** version route re-key | `worktree-agent-aaa745a0a240a67bd` | **`8185c8fcc`** (docs-only on top of `0ddf90fc2`) | ✅ shipped · ⛔ **NEITHER GATE RAN — both must run here** |
| **075** record-aware container resolver | `worktree-agent-acee1a32adb1a9a0f` | `6153049` → `7db13de` → `ff45847` → `3289844` → **`3685e55`** (docs) | ✅ **READY TO MERGE — gate CLOSED, PASS** |
| **076** route call sites | same worktree | `792c38a` (inventory + escalation only) | 🔔 **ESCALATED — not implemented. Needs owner decision.** |

## ⛔⛔ 075 IS BLOCKED FROM MERGE — read before touching that worktree

The Step 9.5 gate agent ran a **second pass against source** rather than trusting `7db13de`'s
"all fixed" claim. All 3 original CRITICALs and all 6 WARNINGs genuinely were fixed — **but the C-1/C-2
restructure introduced three new CRITICALs**, two of them fail-open on the exact condition this wave
exists to detect. Defects sent back to the implementing agent; a fix round may be in flight.

| # | Severity | Defect (`Infrastructure/Dataverse/RecordContainerResolver.cs`) |
|---|---|---|
| **N-1** | CRITICAL | **Guaranteed outage at 25 secure records — and a test now asserts it.** `:238-247`: the secure probe lost its container-value filter, so it returns *any* 25 secure records having a container, not claimants of the requested one, and `secureRowCount` (`:258`) counts rows the trim-match later discards. At 25 secure records org-wide — **the intended steady state** — the bound fires on every call and `ResolveOwningRecordAsync` throws `container_ownership_indeterminate` for every container **including the correct owner's**, killing tasks 073 and 078. `Reverse_ProbeTruncation_Refuses` constructs exactly that shape and asserts the refusal, so it reads as intent. |
| **N-2** | CRITICAL | **The C-1 bug survives, mirrored onto the co-mingling probe.** `:313` filters `ConditionOperator.Equal` on the trimmed input with `ColumnSet(false)` (`:307`), so it cannot self-check — verbatim the defect C-1 fixed. A non-secure record stamped `"  b!x  "` sharing a secure record's `b!x` is invisible, the refusal never fires, and the secure record is named sole owner of a co-mingled container. |
| **N-3** | CRITICAL | **`NotEqual true` excludes NULL rows their own W-5 fix documents as legitimate.** `:314-315`: `NULL <> 1` is UNKNOWN under SQL three-valued logic, so a NULL-flagged non-secure claimant is invisible too. Second independent blind spot in the same detector. |
| **N-4** | WARNING | `IsRecordNotFound` matches **localized exception message substrings** — a non-English org silently reverts to W-6 behaviour, and `"Attribute sprk_issecure was not found"` (a real FLS/schema error) is misreported as "record does not exist", misdiagnosing the one condition W-5 exists to surface. Use `FaultException<OrganizationServiceFault>` + `Detail.ErrorCode == -2147220969`. |

**Knock-on to apply in the same commit as the N-2 fix:** it breaks the test double's query discriminator
(`ColumnSet.Columns.Contains("sprk_containerid")`), which must be re-keyed or the suite mis-reports.

**Also resolved:** trim-tolerance and query selectivity are *not* in tension — Dataverse `Like` supports
bracket escaping (`_` → `[_]`), which answers the wildcard objection without an unfiltered scan. (The
gate's earlier `LIKE '%…%'` suggestion was wrong — `_` is a T-SQL single-char wildcard and SPE drive ids
routinely contain them — and it withdrew it. The implementing agent was right to reject it.)

### Round 3 (`ff45847`) — all four fixed, plus a structural fix and a worse finding

N-1..N-4 accepted and fixed. The agent was explicit that it had been **wrong twice on N-1**: it used the
`_`-is-a-LIKE-wildcard objection to justify dropping the container filter *entirely* — worse than what it
was avoiding — and then wrote a test that enshrined that shape as intended. Bracket escaping
(`_`→`[_]`, `%`→`[%]`, `[`→`[[]` first) plus a code-side exact-after-trim compare restores selectivity
**and** trim tolerance.

**Structural fix beyond the four:** pass 2 now runs only when a secure claimant exists. Probing a shared
BU container — legitimately hundreds of non-secure claimants — would fill the page and turn the ordinary
case into a refusal, **killing task 078 for every normal container**. Same cliff as N-1, different door.

### 🔴🔴 THE FINDING THAT OUTRANKS THE DEFECTS — vacuous tests that look real

**Two of the four new regression tests passed VACUOUSLY on first run.** The test double:
- routed rows by `Flag == true`, so reverting the nested `Or` changed the query but **not the double**;
- looked only for a `Like` condition and **fell back to match-everything when it found none**, so
  reverting to `Equal` made the fallback match every row.

Both green, fast, correctly named — **indistinguishable from real passes.** Caught only by perturbing
each guard individually.

> **The rule: a test double that encodes what a query is *for* cannot detect a change in what it *does*.**

Note the double's failure mode was a **permissive default** — the same shape as task 070's closing
`default:`, now recurring inside test infrastructure. Both helpers now evaluate the query's real
conditions and **throw** on an unmodelled operator or a condition-less probe.

### Pass 3 verdict on `ff45847` — **conditionally clean, 075 CAN merge**

N-1..N-4 all verified correct against source (escape order `[`→`[[]` first, traced round-trip; nested
`Or` correctly on `Criteria.Filters` not `Conditions`; typed `FaultException` on `-2147220969` with a
German-message test proving the classification isn't reading English). Structural fix confirmed to have
**no third door**. Two findings, neither blocking — **sent back to the owner; merge on their fix**:

- **D-1 · the truncation refusal is STILL vacuous — third instance of the batch's own rule.** The double
  evaluates real conditions now but **never honours `query.TopCount`**. Both
  `container_ownership_indeterminate` tests pass only because the fixture supplies exactly 25 rows —
  **delete `TopCount = ClaimantProbeLimit` from either production query and both stay green.** The check
  and the cap read the same constant, so the test cannot distinguish "the page filled" from "there are
  25 rows". Fix: `.Take(query.TopCount ?? int.MaxValue)` + a 30-row test.
- **D-2 · two definitions of container equality, looser one on the byte path.** `IsSameContainer` uses
  `Ordinal`; `CommunicationContainerResolver.cs:83` builds `secureContainers` with
  `OrdinalIgnoreCase`. Two secure records differing only in case collapse to one entry, the ambiguity
  refusal never fires, and `Single()` writes bytes to whichever was inserted first. Negligible
  reachability, but it is a security-identity comparison defined two ways inside one task.

**Sharper than the agent framed it:** `NotNull`/`Null` on the container column are **unreachable in
production**, not merely unperturbed — neither pass emits them. Dead model code.

### Round 4 (`3289844`) — D-1/D-2 fixed, and the proposed PERTURBATION was wrong

D-1 fixed with the `TopCount` cap plus a **30-row** test and a **just-under-the-cap** test, so the bound
is pinned as a *threshold* rather than "any largish number refuses". D-2 fixed to `Ordinal`, with a
comment recording that Dataverse collation is case-*insensitive* so the comparison is deliberately
stricter than the platform. Cleanups: dead `isSecureProbe`/`MatchesSecureProbe` pair and its stale
"stable key" comment deleted; `ContainerConditionMatches` now **throws** on a nested container condition
rather than flattening and ANDing an `Or`'s members; dead `NotNull`/`Null` arms removed.

⚠️ **The reviewer's proposed proof for D-1 was wrong, and I relayed it verbatim.** *"Delete `TopCount`
and both tests stay green"* **does not bite** — removing the cap only *increases* the returned count,
and the guard is `Count >= ClaimantProbeLimit`, so the refusal fires **more** readily. Verified
empirically (0 red).

**The direction that matters is the inverse: cap BELOW the check threshold**, where truncation goes
undetected and a claimant beyond the page is silently missed — **fail-open**. Cap 5 vs threshold 25 →
**2 red**, including the new 30-row test. And that perturbation only bites *because of* the D-1 fix:
with the double ignoring `TopCount` it returned all 30 rows regardless, so `30 >= 25` fired and the test
passed anyway.

**The diagnosis was right; the proposed proof wasn't.** Worth keeping as its own lesson: a reviewer's
*suggested perturbation* is a hypothesis, not a specification, and running it is what distinguishes the
two. The agent tested rather than complied — correct behaviour, and it caught an error that had passed
through both the reviewer and me.

### Stale-assembly trap, third occurrence in this batch

The `isSecureProbe` cleanup left a dangling reference, the **test build failed**, and
`dotnet test --no-build` still reported **32 green off the stale assembly**. Caught only by reading the
build output rather than the test summary. This trap has now appeared three times in one batch (072's
false-PASS perturbation, 075's `if (false)` CS0162, this). **Always read the build result before the
test result.**

## 🔴🔴🔴 THE VERDICT ON THE LAYER — this needs a different KIND of test

Asked explicitly whether another review round was worth it, the reviewer said **no**, and the reasoning
is the most important output of this entire batch:

> Both verification mechanisms are blind in the same place. The **shared fixture** pins the decision and
> cannot see a query. The **double** now pins the query — *against a model of Dataverse written by the
> same agent that wrote the query.* The perturbations prove the code matches the double. **They cannot
> prove either matches Dataverse.**

Six defects, three rounds, all six in the fetch layer. Not six coincidences.

**Five claims in this component are currently unfalsifiable by any test in this repository:**
1. `Like` honours T-SQL bracket escaping
2. `NotEqual` excludes NULL under three-valued logic
3. `TopCount` does not populate `MoreRecords`
4. `Null` works on a two-option field
5. **Dataverse string collation is case-insensitive, while both `IsSameContainer` and the double use
   `Ordinal`** — so the double is strictly *stricter* than the platform and can never surface case
   behaviour. This is also why D-2 survived three rounds.
6. **Page determinism** (added round 5): the production queries carry **no `OrderBy`**, so Dataverse's
   `TOP` picks an arbitrary page while the double picks deterministically in fixture order. Same shape as
   #5 — the double is *more determinate than the platform*, so an order-dependent defect could not
   surface there either.

**Deferred to task 078 with reasoning on record** (not silently dropped): an off-by-one at the boundary —
exactly `ClaimantProbeLimit` matching rows diagnoses as `indeterminate` when `ambiguous` is the truer
code. Fail-closed either way; only the error code and log wording differ. Rounds 1–3 each re-entered this
query layer and each introduced a fresh defect, and round 4 was clean *because* it confined itself to the
double plus a one-word change — so re-entering for a cosmetic improvement immediately after closing the
gate is the trade the history argues against. 078 is already gated on 047 and will be reading this code.

### A fourth instance of the same shape, caught by inspection rather than by any test

The reviewer verified that `page++` sits **below** the filter `continue`s, so the double models
TOP-after-WHERE. Had the counter sat above them, the cap would have consumed budget on non-matching
rows, the 30-row test would have measured **query cost instead of page truncation** — **and it would
still have been green.** The implementing agent's own words: *"I got it right, but not deliberately, and
it wouldn't have been caught by any test I wrote."*

**Action:** task **047** (live-org assertion, already in scope) should gain an explicit **Dataverse
operator-semantics assertion list** covering those five plus the `sprk_issecure` field-security/NULL
check already booked onto it. **Task 078 must not ship before 047 runs** — it is the first real consumer
of the reverse direction.

⚠️ **One correction to that recommendation:** the reviewer paired 073 with 078. **Stale for 073** — it
retired its three routes rather than consuming the seam, so as shipped it consumes nothing from 075.
**078 only.**

### 🔴 THE TRANSFERABLE LESSON — why my own verification could not have caught this

> *"11,199 passed / ArchTests zero delta / publish unchanged is all true and all consistent with a
> guaranteed 25-record outage, because the new test **asserts** the faulty behaviour. Perturbation
> testing confirms a branch is load-bearing; it cannot tell you the branch encodes the **wrong rule**."*

Every verification standard used across this batch — mine included — is a **consistency** check:
tests green, failure-count parity, perturbation bites. **None of them can detect a test that encodes the
wrong requirement.** That is a reviewer-reading-source job, and it is the second time in this batch that
"the numbers are green" concealed a defect (the first was 075's own consumed ADR-010 ratchet). Treat a
green suite plus a passing perturbation as evidence the code does what its tests say — never as evidence
the tests say the right thing.

## Merge in TWO TRANCHES

**073 + 079 do not depend on 075** (073 deleted its routes rather than consuming the seam; 079 re-keyed
to document ids). And the census is **110 either way**, because 075 adds no endpoint file. So:

- **Tranche 1 — NOW:** merge 073 + 079, apply all §2 ArchTest edits with `ExpectedEndpointFileCount = 110`,
  apply §3 / §3b / §4, run §6 verification, re-run both gates for 079 (which never had them).
- **Tranche 2 — after N-1..N-4 are fixed and the gate's third pass is clean:** merge 075.
  **Take that third pass.** Two of three passes on this file found criticals the previous pass created.

### ✅ CENSUS IS **110** in either tranche

`111 − 1 (073 deleted UploadEndpoints.cs) + 0 (079 re-keyed in place) + 0 (075 added no endpoint file) = 110`.
All three deltas are in. Apply `ExpectedEndpointFileCount = 110` at `:337`.

Both completed agents based correctly on `4dee62a0f` (verified: 073's tree carries 072's `["share"]`
key and the `DocumentAuthorizationFilter` fix). 079 disclosed that its worktree was cut from
`origin/master` and it reset to the project branch before starting — verify that reset held before
merging.

## 2. ⛔ MERGE BLOCKER — task 074's gate is RED and it is a BLOCKING CI gate

`tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.cs` is **main-session-owned** and was
deliberately untouched by both agents (correct — two worktrees editing it would conflict). It is
currently **+5 red** on top of master's 9-failure baseline (= 14 failed / 100 passed).

**Three of the five sit on `ci-tier1-blocking.yml`'s `arch-tests` filter** (074 became BLOCKING
2026-08-26 per `TASK-INDEX.md`), so this genuinely cannot merge red.

### The edits (apply ONCE, after Wave 2 lands, sequenced so the census is last)

| # | Location | Edit |
|---|---|---|
| 1 | `:112` `GovernedFile("Api/UploadEndpoints.cs", …)` | **DELETE the entry.** `ScanFile` does an unguarded `File.ReadAllText` at `:1036` → `FileNotFoundException`. **This single entry causes 4 of the 5 failures.** |
| 2 | `:337` `ExpectedEndpointFileCount = 111` | **→ 110** — but ONLY after adding Wave 2's delta. 073 = **−1**. 079 = **0** (re-keyed within `DocumentVersionEndpoints.cs`, no file added/removed). Wave 2 delta **unknown until it reports**. |
| 3 | `:199, :205, :208` waivers | **DELETE** — routes are deleted, so `NoWaiverIsStale` can never see them, and a waiver for a nonexistent route "reads as unfinished work". |
| 4 | `:213, :216` waivers | **RETAIN, re-point owner off `"073"`.** These routes **still exist and are still ungated**, in `DocumentsEndpoints.cs` — outside 073's scope. Deleting them would silently un-waive live holes. |
| 5 | `:287-289` `PolicyOnlyRoutes` | **DELETE 3 strings** (the container/chunk routes). **RETAIN** `:290-291` (drive-keyed upload + delete — still live, still policy-only). Note this is a **separate list** from `Waivers`, which is why `PUT /api/upload-session/chunk` appears twice in the file. |
| 6 | `:249-258` | **DELETE both 079 waivers** — same deleted-not-gated reasoning. |
| 7 | `:106-107` `GovernedFile` reason | 079 reports all three claims in it are now false. Its notes §12.2 has suggested text. |
| 8 | `:234, :240, :244` + lead-in + `:982` | OBO trio owner `"073/075/076"` → **`"075/076"`** (073 is done and did not gate them). |
| 9 | `:265` | The `UNOWNED` waiver suggests "folding into task 073". 073 is done and correctly didn't — it's a collection **read** whose control is result trimming (Wave 3), not a per-resource gate. Reword. |
| 10 | `:50-51` class doc | Says the container route is "pinned here in `PolicyOnlyRoutes` so its mechanism cannot change silently" — it's deleted, not pinned. The wrong-resource-domain example now survives only as the inline fixture at `:785`. |
| 11 | `:234` | **Pre-existing false citation**: cites *"ADR-008 §6.5"*. **ADR-008 has no §6.5** (verified against its heading list). §6.5 is root CLAUDE.md's ADR-conflict protocol. Fix the citation. |
| 12 | `NoWaiverIsStale` | **Extend to flag waivers whose route is ABSENT entirely.** The rule fires only when a waived route becomes *gated*; three tasks (071, 073, 079) have now each left dead waivers it structurally cannot see. Both agents independently asked for this. |

**Do NOT** make the census pass by removing files from `GovernedFiles` — the guard's own warning at
`:394` calls that "the failure this guard exists to prevent". Edit 1 is legitimate because the file is
genuinely gone, not because it was inconvenient.

## 3. 🔴 MUST-FIX before merge — a re-introduction guard with a false-PASS vector

**073's `RetiredMiWriteRoutes_AreAbsentFromTheEndpointTable` can pass while the vulnerable route is
live.** It compares `RouteEndpoint.RoutePattern.RawText` against literals pinning `{containerId}`. A
re-registration spelled `{id}` yields different `RawText`, matches nothing, `survivors` is empty, test
green.

**This is the likeliest re-add spelling, verified:**
- The surviving sibling is registered `"/api/obo/containers/{id}/files/{*path}"` — `{id}`, not `{containerId}`.
- `docs/architecture/sdao-overview.md:129` and `ai-document-summary-architecture.md:37` both write the
  deleted route as `/api/containers/{id}/files/{*path}`.

So copying either the sibling or the docs — the two most probable paths — evades the guard.

**Fix**: normalize route patterns by erasing parameter names before comparing (or add `{id}` variants
to `RetiredRoutes`). File is in 073's worktree and is **not** off-limits, so fix it during merge.
Ask 079 whether its route-absence tests share the shape (asked; awaiting reply).

## 3b. Route-absence guards: what each layer actually covers (079, perturbation-tested)

079's route-absence tests are **immune to 073's parameter-name vector** — they issue a real GET to a
concrete URL, and ASP.NET matches on URL *shape*, so parameter names are only capture labels. It
verified this rather than arguing it, by re-registering the deleted pair under `{id}`/`{speItemId}`/
`{verId}`. But it found its own limitation the same way:

| Perturbation | 079's 4 behavioural tests | 074 Rule A |
|---|---|---|
| Re-registered, **different param names** | ✅ all 4 FAIL (caught) | ✅ FAIL, names both spellings |
| Re-registered, **different URL shape** (`/api/obo/items/{itemId}/version-history`) | ❌ **all 4 FALSE-PASS** | ✅ FAIL, names both |

**So Rule A is the load-bearing guard and the 404 tests are cheap corroboration** — the opposite of how
the test file's own framing reads. The composite still holds (Rule A scans source registrations in the
governed file irrespective of spelling; the endpoint-file census is the third layer for a re-add in a
*new* file — untested by 079).

**The better guard shape, not built:** assert on the **capability** — that no route anywhere reaches
`ListFileVersionsAsUserAsync` / `DownloadFileVersionAsUserAsync` except the two gated ones — rather
than on URL strings. That is shape-drift-proof and is the fix worth making if the behavioural layer
should stand on its own. Applies to 073's guard too (see §3). Weigh at merge; it is a real gap.

### Verified benign — 079's duplicate `MapGroup`

079 flagged that there are now two `MapGroup("/api/documents")` declarations (its own +
`FileAccessEndpoints`). **Checked: not a hole.** `DocumentVersionEndpoints.cs:100` is
`MapGroup("/api/documents").RequireAuthorization()` — same as the sibling group — and both new routes
carry `.AddDocumentAuthorizationFilter("read")` + `.RequireRateLimiting("graph-read")` (`:133-134`,
`:182-183`). Templates are disjoint. No action.

### For the reviewer, 079's own author-flagged item

`ResolveSpePointerAsync` **duplicates the checks** in `private static FileAccessEndpoints.ValidateSpePointers`
rather than sharing it, bounded by reusing its five error codes. Two implementations of the same
SPE-pointer validation is a drift risk — evaluate whether to extract a shared helper at merge.

## 4. Other fixes to apply during merge

| Item | Where | Source |
|---|---|---|
| Orphaned `PathValidator.cs` — **zero references repo-wide** after the deletion; 19 lines, zero risk | `Infrastructure/Validation/PathValidator.cs` | 073 review W3 |
| `.claude/` drift — lists the deleted file as canonical endpoint file #3. **Main-session-only** (§3) | `.claude/patterns/api/endpoint-definition.md:13` | 073 adr-check V-2 |
| Stale comment cross-referencing the deleted route as extant | `Api/OBOEndpoints.cs:36` | 073 review W5 |
| Stale comment naming a file that no longer exists (**doubly** stale — the route it describes never existed either) | `Api/DocumentsEndpoints.cs:25-26` | 073 W5 + adr-check W7 |
| Stale comment naming a deleted route | `Infrastructure/Graph/SpeFileStore.cs:157` (079 fixed the twin in `ISpeFileOperations.cs:100`) | 079 |
| Commented-out `using` should be **deleted**, not commented (and two sibling usings are now orphaned too) | 073's `EndpointGroupingTests.cs` | 073 review N1 |
| Orphaned `[Fact(Skip)]` asserting `POST /api/containers` — **a route registered nowhere**. By 073's own stated rule it should have gone with the other three | `EndpointGroupingTests.DocumentsEndpoints_ReturnsProblemDetailsOnError` | 073 adr-check W-8 |
| Record a one-line ADR-038:559 citation (Path A) so `/test-diet` reads the endpoint-table assertion as classifier noise, not a finding | project notes | 073 adr-check W-1 |

## 4a. ✅ 075 GATES CLOSED — PASS at `3289844` (4 rounds, 10 defects, 0 in round 4)

| Round | Found | Fixed in |
|---|---|---|
| 1 `6153049` | C-1 reverse trimmed on the wrong side · C-2 silent 25-row truncation · C-3 TS half fails open on empty read | `7db13de` |
| 2 `7db13de` | N-1 probe lost selectivity → guaranteed outage at 25 secure records · N-2 C-1 mirrored onto co-mingling probe · N-3 `NotEqual true` excludes NULL · N-4 localized-substring fault matching | `ff45847` |
| 3 `ff45847` | D-1 truncation refusal still vacuous · D-2 two definitions of container equality | `3289844` |
| **4 `3289844`** | **none** | — |

**adr-check: 0 violations.** ADR-003/007/012/032 pass · ADR-009 now compliant · ADR-010 improved to
**152 with headroom restored** · ADR-029 pass (45.10 MB / 60) · ADR-038 pass.

The reviewer also **corrected its own D-1 proof** and credited the implementing agent: removing the cap
moves the refusal fail-*closed*, so a green test there is correct, not vacuity. *"A perturbation only
proves something when it introduces a defect; mine didn't."*

### Two conditions attached to the PASS

1. **Task 078 must not ship before task 047 runs** (073 explicitly **not** gated — it retired its routes
   rather than consuming this seam). 047 needs the operator-semantics assertion list.
2. **The two-hop child gap stays open and filed** — `communication → sprk_invoice → secure matter` still
   lands in the shared archive because `sprk_invoice` is a regarding target but not securable. Needs the
   Phase 3 ancestor stamp (050–055).

### One-line residual to fix at merge

075's notes don't cite the project's existing **path-B ADR-003 amendment (task 030)** covering the
new-seam tension. Add the citation.

### Why four rounds — the structural read, worth carrying forward

**All ten defects were in the fetch/query layer. None in the decision layer**, which sat still and
correct throughout. Rounds 1–3 each restructured that layer and each introduced a fresh defect there;
round 4 confined itself to the double plus a one-word production change and introduced none. **The
component has outgrown what a hand-written double can verify** — recorded as §12 verification debt in
the task notes rather than claimed away.

## 4b. 🔔 076 ESCALATION — blocks 076 only; 075 merges independently

Full options in `notes/task-076-callsite-inventory-and-ESCALATION.md` §2. The trigger fired materially:

**Every `Create*Wizard` resolves its container when the wizard OPENS — before the record exists.** So
075's seam, which takes `(entity, recordId)`, cannot be asked where those call sites currently sit. And
the POML contradicts itself: its constraint says "route every site through the resolver" while its own
worked example says "consume `provisionResult.data.speContainerId`" — different mechanisms with
different coverage.

> ✅ **RESOLVED — the escalation note is now complete at `dbc2a62`.** The two-hop gap had been recorded
> in 075's notes but **not** in the 076 escalation note, which is the document the operator actually
> reads to choose. It is now in §2 with the per-option consequence spelled out:
>
> | Option | Effect on the two-hop gap |
> |---|---|
> | **(A)** | Doesn't close it, but routes every path through **one** place where closing it later is a single change |
> | **(B)** | **Cannot** fix it on the create path at all, so closing it would need a *third* mechanism. **A second argument against (B), independent of the F-8 silent-skip one.** ⚠️ **See the mechanism correction below — the conclusion holds but the note's stated reason does not.** |
> | **(C)** | Closes it server-side in one place; client untouched |
>
> It is **not a prerequisite** for 076 (closing it needs the Phase 3 ancestor stamp, 050–055). It is a
> **constraint on the choice**: pick the option that leaves the gap closable in one place, not three.

> ⚠️ Original framing, for the record — the gate reviewer flagged that the
> **two-hop child gap** (`communication → sprk_invoice → secure matter`, currently landing in the shared
> archive) is *substantively the same question* as 076's escalation: **"which record is the decision
> about?"** So whichever resolution point is chosen for 076 should be checked against the two-hop case
> deliberately, rather than having that case settled as a side effect. Phase 3's ancestor stamp
> (tasks 050–055) is the other half of the same answer.

## 4b-0. 🔴🔴 THE ROW IS RIGHT AND THE BYTES ARE WRONG — verified in main session

Chasing the mechanism correction below produced the batch's most operationally significant finding. The
full create sequence, each step traced to source and **independently re-verified here**:

| # | What happens | Where |
|---|---|---|
| 1 | `sprk_issecure = true` set on the create payload | `projectService.ts:283-285` |
| 2 | BU cascade applied **unconditionally**, no secure suppression — **write W1** | `projectService.ts:291-292` |
| 3 | Provisioning **overwrites** the stamp with the project's own container | `ProvisionProjectEndpoint.cs:698-705` |
| 4 | Upload uses `context.speContainerId` **from wizard-open** — `provisionResult` is checked only for `.success` and its container id is **discarded** | `CreateProjectWizard.tsx:707-712` |

**Net effect: the Dataverse row ends up CORRECT and the BYTES end up in the shared BU container.**

Two consequences:

1. **"A container id is set on the project" is a FALSE POSITIVE** — it cannot be used as evidence of
   correct isolation. Task **047** must not accept it. This is a live sequencing consequence, not a
   data-state observation.
2. **This may need data remediation, not just a code fix.** SPE is **additive-only** — this project's own
   CLAUDE.md: *"you can't break inheritance on arbitrary files"* — so files already written to a shared
   container cannot be retroactively isolated by permission change. If dev has ever created a secure
   project **with files**, those bytes are in shared storage now and may need **moving**. Worth a
   dedicated task; 076 fixes the forward path only.

### ⚠️ CORRECTION — ARMED, NOT FIRED. Two claims above are overstated.

**Zero secure projects exist in any environment** (`TASK-INDEX.md:110`, re-verified). The trigger for
F-9 is *creating a secure project with files attached in the wizard*, which has therefore **never
occurred**. Consequences:

- **Consequence 2 above ("may need data remediation") is almost certainly moot.** No bytes are
  misplaced today. 076 fixing the forward path is sufficient; there is nothing to move.
- **The window closes on FIRST USE, not on a date.** This is the strongest form of the build plan's own
  "build it before the first one and there is never a migration" argument — and it makes the finding
  more urgent to fix, not less, even though it is currently harmless.
- **State it explicitly wherever F-9 appears.** Without it, someone checks App Insights, finds nothing,
  and downgrades a live-but-unfired defect to a non-issue.

### 🔊 The Warning line exists and is correctly worded — but it has almost certainly never emitted

`ProvisionProjectEndpoint.cs:690-695`, verified verbatim:

```
[PROVISION] Overwriting sprk_containerid on project {ProjectId}: '{Previous}' → '{New}'.
The previous value was cascaded from the creating user's business unit and
is shared storage, not this project's container.
```

A `LogWarning` naming the defect precisely, on every secure-project creation — **of which there have been
none.** So this is *a trap set correctly and never sprung*, which is a different and considerably less
damning story than "the system reported it and nobody looked."

**My earlier framing of this as "logs losing to nobody looking" was wrong** and is retracted here. The
log line is verbatim-accurate and well-worded; what it is *not* is evidence of an ignored signal. Do not
carry the stronger version forward — it would misrepresent both the team and the severity.

### 🔁 THIS REFRAMES THE 076 DECISION — A and C read differently now

The gate reviewer's closing point, and it is the sharpest thing in the batch:

> This is currently documented as a **rationale bullet** inside 076's design note, where it reads as
> *argument* rather than *defect*. It changes what the operator is deciding: **not "which resolution
> point is cleaner" but "which resolution point closes a fail-open that is live today."**
> **Options A and C read differently under that framing.**

Concretely: **(A)** puts the decision at each of ~12 client upload paths — correct only if every one is
updated correctly, now and forever. **(C)** closes it **server-side in one place, client untouched** —
which under "stop a live fail-open" is the stronger property, because it does not depend on client
call-site discipline. (A) was recommended on *architectural* grounds — one seam, one decision point,
two-hop gap closable in one place — which was the right recommendation for the question as originally
framed. **The framing has changed; the recommendation deserves a second look before it is acted on.**

**Action: elevate this from a rationale bullet to a named finding with a severity, in the escalation
note's opening summary**, so the operator decides against the accurate framing.

### ✅ The Warning-log claim is VERIFIED (the reviewer explicitly did not vouch for it)

The gate reviewer flagged the "already logs a warning" claim as the implementer's, not verified by it.
**Verified verbatim in the main session** at `ProvisionProjectEndpoint.cs:690-695` — text quoted above in
§4b-0. It is confirmed, not merely claimed, and belongs alongside this project's other
"docs lose to live metadata" findings as the logs-lose-to-nobody-looking instance.

### ⚠️ MECHANISM CORRECTION — verified in main session, fix in the 076 note before the operator acts

The 076 escalation note argues against option (B) by saying the create path *"takes its container from
provisioning's return value and never asks the resolver."* The gate reviewer challenged that and
**deliberately did not investigate**, since 076 is escalated and outside its scope. **I verified it. The
reviewer is right — the stated reason is wrong.**

`src/client/shared/Spaarke.UI.Components/src/components/CreateProjectWizard/projectService.ts`:

- `:283-285` — the `formValues.isSecure === true` branch sets **only** `entity['sprk_issecure'] = true`.
- `:290-293` — `EntityCreationService.applyUserBuDefaults(entity, cascadeDefaults)` then runs
  **unconditionally**, with **no suppression for secure projects**.

So a secure project's create payload is stamped with `sprk_containerid` from the **client-side BU
cascade** — not from provisioning's return value. (I did not check whether provisioning later overwrites
it, so both writes may occur.)

**Why the distinction changes the argument, not the conclusion:**

- **Conclusion unchanged, arguably stronger** — (B) still cannot reach the create path through the resolver.
- **But the argument type flips.** A client-side create-time write is a **scope** argument against (B) —
  the write is inside 076's own declared scope, whose POML title literally reads *"stop the wizard
  stamping secure records"*. A different-subsystem write would have been an **architectural** argument.
  An operator weighing A vs B deserves the accurate one.
- **It also corroborates 075's finding #2** (`AssociateToStep.tsx:154-160` fails OPEN): the wizard
  stamping secure records unconditionally is the same class of already-shipped defect, on the create side.

**Action:** correct that sentence in `notes/task-076-callsite-inventory-and-ESCALATION.md` §2 when the
worktree merges, before the decision is made on it.

Agent's recommendation is **option (A)**: wizards keep resolving the BU container at open (INV-7
unchanged) but treat it as the **fallback only**, and each upload path asks the resolver immediately
before the first byte moves. One mechanism, covers create *and* existing-record uploads, and it also
closes a silent-skip hole where an absent `authFetch`/`bffBaseUrl` skips provisioning while the success
screen claims the container was provisioned.

It deliberately did **not** half-implement 076 — a half-routed surface reads as "076 landed" while paths
stay open. The seam is shipped and the site list is complete, so this is a clean stop.

### The inventory is bigger than the POML said — 076 must be re-scoped whichever option wins

| POML said | Actually |
|---|---|
| Strategy 2: 2 sites in 1 file | **9 sites in 3 files** (`CommunicationService.cs` alone has 5) |
| 7 client resolution sites | **12** (+ `CreateWorkAssignmentWizard`, `CreateTodoWizard`, a form-context read in `sprk_analysis_commands.js`) |
| — | **`AssociateToStep.tsx:154-160` is ALREADY record-aware and FAILS OPEN** — reads the record's `sprk_containerid` and falls through to the BU container on *any* failure. The exact shape 075 exists to remove, already shipped, and absent from the POML. **Highest-value single site.** |
| — | `SmartTodo/src/services/xrmProvider.ts:97` is a full duplicate of the canonical resolver — same name, same line number as the LegalWorkspace copy |

## 4c. 075's best finding — a ratchet consumed without failing

**"ArchTests zero delta" concealed a consumed ratchet.** 075's two 1:1 interfaces took ADR-010's
interface count to **exactly its 153 ceiling** — still passing, but with **zero headroom**, so the next
interface added anywhere in the BFF would fail the build while blaming an unrelated project. It deleted
`IRecordContainerResolver` and registered the concrete class (ADR-010 compliant anyway).

**Generalizable, and worth adding to the verification habit: failure-count parity ≠ ratchet-headroom
parity.** Every check in this batch used "9 failures = baseline" as the pass criterion, which by
construction cannot see a ratchet that was consumed but not breached.

## 5. Follow-up tasks to FILE (not fix now)

1. **🔴 Reachable ungated DESTROY path — needs an owner, not a note.**
   `DELETE /api/drives/{driveId}/items/{itemId}` (`DocumentsEndpoints.cs:98`) is policy-only
   (wrong-resource-domain). Its caller `src/dataverse/webresources/spaarke_documents/DocumentOperations.js:578`
   takes `driveId`/`itemId` from form attributes, so unlike its sibling it does **not** depend on any
   deleted route. **No XML anywhere in the repo references that web resource**, but its README documents
   a manual "Deploy via Power Apps Portal" path — so the repo cannot prove it isn't live.
   *Corroboration I added:* of the four routes that file calls, **only two exist server-side** —
   `downloadFile` and `getFileMetadata` would 404 today, so a live ribbon would be visibly broken. That
   strengthens "not deployed" without proving it. **The task must FIRST resolve whether the web resource
   is deployed.**
2. **Dead MI chunked-upload chain** in `SpeFileStore.cs` (off-limits to 073): `CreateUploadSessionAsync`
   (0 production callers), `UploadChunkAsync` (0 callers), `UploadSessionManager` equivalents,
   `UploadSessionDto` dead by transitivity, plus an orphaned test at `SpeFileStoreTests.cs:124`.
3. **Unarchived project spec targets deleted routes** — `projects/sdap-file-upload-document-r2/`
   (`spec.md:62` FR-12, `plan.md:96`, `tasks/012-*.poml:21`) names `POST /api/containers/{id}/upload`
   + `PUT /api/upload-session/chunk` as its **target contract**, with no `.archived` marker. A future
   task would implement against dead routes. Also two operator scripts print a retired route as their
   post-provisioning smoke test (`Create-NewContainerType.ps1:202`,
   `Register-BffApiWithContainerType.ps1:115`).
4. **Facade-method inventory sweep** (079's suggestion, and it is a good one). The technique that found
   079's two routes was a **caller** inventory. `ComposeService` calls
   `DownloadFileVersionAsUserAsync` directly as an in-process facade call — byte reads keyed by
   `(driveId, itemId)` whose authorization story nobody has enumerated. Applying the caller-inventory
   technique to facade *methods* rather than routes is the obvious next sweep.
5. **Client test coverage hole** — `VersionHistoryModal.test.tsx` **cannot load at all** in this
   workspace (`@fluentui/react-icons` unresolvable through the `@spaarke/ui-components` `file:` link).
   079 confirmed by reproducing on unmodified HEAD. So 079's client regression assertion is
   **currently unenforced**.
6. **Flaky test, diagnosed** — `TenantCacheMetricsTests` failed 1 of 3 full runs: asserts exact
   equality against **process-global static** meter counters while xUnit runs classes in parallel. Not
   caused by 079, but new fixtures raise the race probability.
7. **`RouteAuthorizationGuardTests.cs` waiver-absence rule** (item 12 above) if not done inline.

## 6. Integration verification — nothing is verified until this runs HERE

Each agent verified only its own worktree. **No one has tested the combination.**

1. Merge all three worktree branches into `work/unified-access-control-r2`.
2. Apply §2 (census LAST, with all three deltas summed), §3, §4.
3. `dotnet build src/server/api/Sprk.Bff.Api/` — expect 0 warnings / 0 errors.
4. `dotnet test tests/unit/Sprk.Bff.Api.Tests/Sprk.Bff.Api.Tests.csproj` — baseline **11,172 / 0 / 82**;
   073 claims **+7 passed / −3 skipped**, 079 claims **+12 passed**. Reconcile the actual against the sum.
5. `dotnet test tests/Spaarke.ArchTests/Spaarke.ArchTests.csproj` — MUST return to **exactly 9 failures**
   (master's baseline: FR-27 ×2, FR-28, FR-29, FR-32, FR-F1, FR-F2, ADR-010, ServiceBusClientGuard).
   Anything else is ours.
6. Publish size (073 Δ0.00, 079 +0.01 → expect ~45.09 MB; ceiling 60) + `dotnet list package --vulnerable`.
7. **Re-run `code-review` + `adr-check` on the COMBINED diff.** 073's gates both returned
   (APPROVE-WITH-NITS / VIOLATIONS-FOUND-with-V-1-being-this-merge). 079's are **unconfirmed**.
8. `GrantMembershipAsync` must still have **zero callers** (project invariant).

## 7. Deploy-ordering obligations accumulating for the release note

- **072**: BFF + client must ship together, or emailed links silently stop opening for external
  recipients (organization scope), with no error signal.
- **079**: BFF + the AllDocuments Code Page must ship together, or version history 404s for cached
  bundles. Transient outage on one modal, **not** a disclosure.

## 7b. The distilled lesson from 075's four passes

The four review passes split evenly between two categories:

- **Rounds 1–2 — "the code is wrong"**: two fail-open defects. This is what a review is nominally for.
- **Rounds 3–4 — "the verification is wrong"**: two vacuously-passing tests, one false sentence in a
  decision document, one mis-severitied finding.

**The sequencing matters.** The verification errors were present from the start and only became
*findable* once the code stopped being loudly wrong.

> **A green suite is the point at which to start checking the verification, not the point at which to
> stop.**

Corollary, learned three times in this batch: **a green suite and an accurate document are separate
claims.** The wrong sentence in the 076 note would have mis-decided an operator question while every test
stayed green, and the mis-severitied finding would have had the operator deciding "which design is
cleaner" instead of "which option closes a live fail-open".

## 8. Process note worth keeping

The POMLs marked 073 and 079 `∥-safe: true`, and on *modify* targets that was accurate. It did not
cover: (a) sub-agents share one worktree by default, so concurrent edits to a shared file are **lost
writes, not git conflicts**; (b) both tasks needed edits to the same ArchTest file; (c) concurrent
`dotnet build` contends on `bin`/`obj`. Worktree isolation plus a main-session-owned file list handled
all three. **Consider making that the standard dispatch pattern** for this project rather than an
ad-hoc choice — and consider whether `∥-safe` should be split into "disjoint modify targets" vs "no
shared-file coordination needed", because they are different properties and only the first is what the
POMLs currently assert.

---

# WAVE A (dispatched 2026-08-27) — 011 · 013 · 015 · 018 · 020 · 081

## A1. 🔴 DISPATCH DEFECT FOUND — agent worktrees are cut from `master`, not from this branch

**`isolation: worktree` cuts from the repo's default checkout, NOT from the invoking session's branch.**
Verified: task 018's worktree had merge-base `3b87b07bc` (a master commit) with this branch, and
`tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.cs` **did not exist** in it — task 074's forcing
function lives only here. 081 independently detected the same thing, verified its HEAD was fully
contained in `origin/master` (so nothing was lost), and **reset its agent branch onto `2b3b07de2`**.
Confirmed: 081's merge-base with this branch is now exactly `2b3b07de2`.

**Blast radius this wave: contained, and verified rather than assumed.** `git diff 3b87b07bc..HEAD`
over all twelve Wave A target files returns **empty** — none were touched by this branch's 35 commits,
so every agent edited byte-identical files and their diffs apply cleanly.

**But two consequences are real and recur on every future dispatch:**
1. **Agents cannot run task 074's guard** — it is invisible to them. So no agent can self-verify that
   its gating satisfies Rule A/Rule B. They must report needed guard edits; the main session applies
   them against the real file. This is already the standing instruction, which is why it held.
2. **Agent test baselines are NOT this branch's baselines.** 018 reported 11,166 passing and a 6-failure
   ArchTest baseline; 081 reported 11,191 and **9** (matching here, because it had reset). Both were
   right *for their own tree*. Both established their baseline empirically instead of comparing against
   a number from elsewhere — that is the only reason the difference was harmless.
3. **A stale-tree agent can reason from stale facts.** Concrete instance: 018's tree lacks 072's
   `["share"]` key, so the A-23 rationale ("`share`/`attach` are not policy keys") is now half-false
   here. Deletion still correct on zero-callers grounds, but the *argument* had to be corrected.

**Action for the next dispatch:** either verify the base immediately after spawn (`git merge-base
<agent-branch> HEAD`) and instruct a reset before work begins, or state in every agent prompt that its
tree is master-based and it must not reason about project-branch-only facts.

## A2. Wave A worktree inventory

| Task | Worktree branch | Commits | Base | Status |
|---|---|---|---|---|
| **081** classify the caller | `worktree-agent-a6469d6b786a2b792` | `15b5dc6a3` → `14869cd61` | ✅ reset to `2b3b07de2` | ✅ shipped · both gates PASS · 0 violations |
| **018** dead filters + `in`-clause bound | `worktree-agent-a50b2ed919feb5169` | `2d189cfde` (+A-23 pending) | ⚠️ master `3b87b07bc` | 🔄 **A-23 half incomplete — agent resumed** |
| 011 · 013 · 015 · 020 | — | — | ⚠️ master-based | 🔄 running |

## A3. ArchTest edit #13 — 081's census comment (apply WITH 081's code, not before)

`RouteAuthorizationGuardTests.cs:324-330` describes the tenant-container-resolver defect in the
**present tense** ("an authenticated caller in tenant A **can** resolve tenant B's SPE container id").
081 fixed it, so after merge that comment documents a live vulnerability that no longer exists —
the same stale-claim class this project has now hit nine times.

**Ordering constraint: do NOT flip this to past tense before 081's code is in the tree, or the file
lies in the other direction.** Replacement text for lines 325-330:

> `READ, filed as task 081 and FIXED there (commit 15b5dc6a3): it took tenantId from the QUERY STRING and treated the caller's JWT tid claim as a mere fallback, so an authenticated caller in tenant A could resolve tenant B's SPE container id. Now gated on positively-classified app-only AND an operator allow-list, with denial before any resolver call; the tid fallback is gone.`

Lines 331-333 (why it stays out of `GovernedFiles`) remain accurate. **No waiver to remove** — the file
was never in `GovernedFiles`. `ExpectedEndpointFileCount = 111` stays correct for 081 (it added no
route); it becomes **110** only via 073's deletion, exactly as §2 edit #2 already sequences.

## A4. 081 — why its gate is sound, and the one thing it rests on

Verified in main session against the agent's source, not accepted on report:

- **`idtyp` is provably unusable.** Zero `optionalClaims` anywhere in the repo — every `idtyp` grep hit
  is ADR-028 prose or a substring false positive (`owneridtype`, `personidtype`). So the POML's premise
  that `AuditEnrichmentMiddleware` "reads `idtyp`" is true of the *code* and inert in *practice*.
- **The load-bearing signal is therefore `sub == oid`**, and that is correct Entra behaviour: app-only
  tokens carry `sub == oid == ` the service principal's object id, while a user token's `sub` is a
  pairwise identifier scoped to (user, app) and never equals `oid`. Unlike `idtyp` it needs no tenant
  configuration.
- **The trap is closed structurally, not by discipline.** The delegated-scope branch runs BEFORE any
  application branch, so nothing carrying `scp` can be classified `Application` — a human signing into
  the L2 app registration is `UserDelegated` and denied. `appid` alone can never grant.
- **`Indeterminate = 0`**, so a default-constructed value denies. Absence is never evidence of app-only.
- `roles` deliberately excluded as a determinant: it appears in user-delegated tokens too, and the usual
  "`roles` present AND `scp` absent" formulation smuggles in an absence test.

**The residual, stated by the agent and worth honouring:** no live token was ever decoded. If a real L2
UAMI token violated `sub == oid`, the probe 403s → InfraFault — **fail-closed and visible**, but H13
would not reach Ready. **Decode one real probe token at or before the first live H13 run.**

**Operator prerequisite, not a code change:** the allow-list must be populated with the L2 UAMI app id
per customer BFF, or the route 403s everyone (deliberate). This is a hand-off to
`customer-provisioning-orchestration-r1`.

## A5. 081's best finding — a rewire with no test behind it

081 deleted `AuditEnrichmentMiddleware`'s four private claim readers and repointed it at the new
classifier, then found **the file had no tests at all**: parity was argued in a comment and proved by
nothing. It added 8 tests pinning all five audit scope fields, and perturbation P8 (swap
`ObjectId`↔`TenantId`) turns 5 of them red.

> Without that, swapping two audit fields would have **compiled, passed all 11,183 tests, and silently
> corrupted the audit trail feeding customer SIEMs.**

This is AP-8 with a different victim: not a test encoding the wrong rule, but a refactor whose safety
claim rested on prose. It also hardened a pre-existing `FakeResolver` that answered *any* tenant with
one canned result — **the match-everything double from the 075 batch, found again in a different file.**
That double shape has now appeared four times in this project. It is not a coincidence; it is the
default shape a hand-written fake takes unless someone forces it to throw.

## A6. 018 — the two dead filters are always-deny, and one is booby-trapped

018 deleted `AccessibleRecordSetAuthorizationFilter` (−154) and chunked the `in`-clause (≤500-value
sibling conditions under the existing `or` filter, so the union is exact and no id is dropped). It
**structurally avoided** the cap-direction trap rather than getting the direction right by luck: with no
truncation there is no cap to point the wrong way, and NFR-03 has nothing to surface.

Verified in main session:
- `AddAccessibleRecordSetAuthorizationFilter` has **zero call-sites** — every hit is inside its own file,
  including a **commented-out usage example at `:15`**.
- `WorkforcePrincipal.HttpContextItemsKey` is **read** only by the deleted filter (`:102`) and
  **written nowhere in `src/**`**. Every other `HttpContextItemsKey` hit belongs to a different type
  (`CallerPrincipal`, `ResolvedTenantEnvironment`, `RecordSearchAuthorization`,
  `SemanticSearchAuthorization`) and is live.

**A-15 is always-deny BY CONSTRUCTION, not merely unattached** — and `:15` was an active invitation for
the next author to attach it to a live route, which would then have denied unconditionally. That is a
stronger deletion argument than "dead code" and belongs in the PR description.

### ⚠️ MY ERROR, corrected by the 018 agent — the two orphans are NOT the same kind of dead

I wrote "**both** orphans are always-deny by construction" into `spec.md` (`a09949cc9`) and into §A6 above.
**That is true only of A-15.** The agent pushed back and was right; verified in main session:
`OfficeAuthFilter.UserIdKey` **is** written at `OfficeAuthFilter.cs:126`, by a **live-attached**
`AddOfficeAuthFilter()` at `Api/Office/OfficeEndpoints.cs:172` (also `:480`, `:499`, `:774`). So **A-23's
precondition was satisfied — it was unused-but-FUNCTIONAL**, not a loaded trap.

The distinction is load-bearing, in the agent's words: *a reader who merges them draws the wrong lesson
about which one was dangerous.* Corrected in `spec.md` FR-17. Worth noting the shape — I generalized from
one verified instance to a second unverified one, in the same sentence, and it read as a stronger finding
*because* it was a pair. That is the exact move AP-8 warns about, committed by me while writing AP-8 up.

### A-23's deletion had ZERO test-count effect — which is itself the finding

Unit suite bit-identical before and after: **11,166 / 0 / 96 skipped**. A-15's deletion removed its own
8-test file; **A-23 had no coverage at all, not even its own.** The agent also checked the inverse
direction — that nothing downstream was orphaned: `AuthorizationService` (8+ live filter consumers),
`ExtractBearerTokenOrNull` (10 refs), and `ShareLinksRequest`/`ShareAttachRequest` (live on the real
share endpoints + `OfficeService`) all survive. Publish size **unchanged** at 45.07 MB, which
independently confirms the earlier +0.11 MB reading was measurement noise, not growth.

**Scoping error to own: mine.** I built 018's modify-set from `<relevant-files>` and missed
`<constraint source="task-003">` at POML line 44, which requires deleting the **A-23** filter
(`Api/Filters/OfficeDocumentAccessFilter.cs`) as well. The agent stayed in scope and reported instead of
widening — correct behaviour. Resumed with the modify-set widened. Verified before resuming: A-23 has
zero call-sites, no source test references (only compiled `bin/` binaries), and the file is
byte-identical across both trees.

## A7. NFR-03 is still violated on the 018 seam — and 015 owns it

018 flagged as out-of-scope, correctly, that **paging is still absent** on this seam: rows past one page
are silently dropped. That is precisely the silent cap NFR-03 forbids. So closing the `in`-clause bound
does **not** close NFR-03 here. **Task 015 is the owner** and is running now — check its report against
this specifically. Recorded in `spec.md` FR-17 (commit `a09949cc9`).

## A8. Wave A additions to §5 follow-ups

- **Decode a live L2 UAMI probe token** and confirm `sub == oid` before the first live H13 run (081).
- **Populate the operator allow-list** per customer BFF — hand-off to
  `customer-provisioning-orchestration-r1` (081).
- **`WorkforcePrincipal.HttpContextItemsKey` is now dead** (`ExternalCallerContext.cs:160`) — harmless
  unused `static readonly object`, left in place because another agent owns that file this wave.
- **081's `HandleAsync` carries 5 concerns** — kept inline deliberately (a filter would be a NEW
  component for ONE route per §11, and a `*AuthorizationFilter` would fall under Rule B, which demands
  an authorization *decision service* this config-driven gate does not use). Accepted, documented.
- **081 residuals**: no test exercises the real HTTP pipeline (all endpoint tests call `HandleAsync`
  directly, so nothing proves ASP.NET routes/authenticates through to the gate); rate limiting and
  concurrency unverified; nothing proves the denial log line reaches a sink.
- **018 residuals**: that 500 is the correct Dataverse per-condition limit is documented guidance, not
  measured; total FetchXML payload size is still unbounded; no end-to-end execution against a real
  `ServiceClient`.

**All of these belong on task 047's Dataverse/Entra live-assertion list**, which is now carrying
residuals from 075, 018 and 081.

## A9. 011 — the defect was invisible BY SHAPE, and the fix exposed a shared blind spot

**The defect**: the FetchXML guard compared only the *set of entity names*. A **self-join contributes
only the module's own name**, so an exfiltrating self-join was **byte-identical** to a benign read. No
amount of scrutiny of that check could have found it — the check had no signal to look at.

**The fix** is two independent checks: (1) entity identity, unchanged, evaluated first, so foreign joins
still report `DV_FETCHXML_ENTITY_MISMATCH`; (2) new structural join detection refusing any `<link-entity>`
at any depth via **local-name, case-insensitive, namespace-agnostic** match. Guards perturbed individually:
delete join detection → 11 fail · delete entity check → 5 fail · make match exact-name → 2 fail · make
parse-failure permissive → 1 fail. `FetchXmlEntityExtractor` deliberately untouched (shared with
`DataverseAuthorizationFilter`).

### 🔴 NEW FINDING (main session) — the shared extractor has the blind spot 011 fixed locally

011 chose namespace-agnostic local-name matching *because* "the extractor's exact-`XName` lookup misses
`<LINK-ENTITY>` and `<x:link-entity>`". Chasing that: `FetchXmlEntityExtractor.cs:94` is
`document.Descendants("link-entity")`, and **`XName` matching is case-sensitive and namespace-aware**.
That extractor backs `.AddDataverseAuthorizationFilter(EntitySource.FromFetchXmlBody)` on
**`POST /api/dataverse/fetch`** (`Api/Dataverse/FetchEndpoints.cs:67`).

So **if** Dataverse accepts a case-variant or namespace-qualified `link-entity`, a joined entity would be
invisible to that filter and never privilege-checked. The extractor's own header (`:19-21`) states that
Dataverse RBAC does **not** cascade Read through joins and that a filter missing them "creates a trivial
information-disclosure path" — while its implementation notes cover nesting, `intersect`, `outer`, and
case-insensitive *attribute values*, and **never mention element-name case or namespace**.

**This is conditional, not confirmed** — it depends entirely on whether Dataverse's own FetchXML parser
accepts those forms (XML is case-sensitive by spec, so it plausibly rejects them). **That is precisely the
question no test in this repo can answer**, and 011 said so. → **task 047 live-assertion list**; do not
change the shared extractor before 047 answers it, because the blast radius is every `/fetch` caller.

011's own `/fetch` flag was correctly hedged ("flagging, not claiming") and it was right not to claim
A-17 there — a self-join escalates nothing *entity-wise*. The sharper issue is the element-name form.

### 🔴 NEW FINDING (018) — live Office share routes: Rule A passes, Rule B does not

`POST /office/share/links` (`Api/Office/OfficeEndpoints.cs:1359`) and `/office/share/attach` (`:1378`)
carry `AddOfficeAuthFilter` — **authentication only**; their own comments say so verbatim ("Authorization:
OfficeAuthFilter validates user authentication"). Compare `/office/save`, which carries
`.AddEntityAccessFilter()`. Per-document share permission is a **stub**: `OfficeService.cs:938` reads
`CanShare = true // In real implementation, check user permissions` — **verified live in source**.

That is exactly the 077 shape (a filter attached, authorizing nothing) on **live routes that mint share
links and package documents for email**. Tracked as GitHub **#229**. The 018 agent checked whether its
deletion removed the only thing Rule B was passing on there — **it did not**; the filter was never
attached, so Rule B's verdict is identical before and after. What the deletion removes is *the impression
that the mechanism existed*, and `OfficeDocumentAccessFilter` was plausibly #229's intended implementation.

Out of FR-17 scope (Office add-in surface, not the SPA/Teams plane) — **file as a new task**, sibling of
072. Note 072 gated `share-link` on the documents group; this is the same capability on the Office group,
still ungated.

## A10. ⚠️ KEEP-path inconsistency INSIDE this wave — resolve before merge

ADR-038's KEEP paths are `tests/integration/{auth,contract,data-mutation,regression,seam,tenant}/**` and
`tests/unit/domain/**` (`tests/CLAUDE.md`: *"Tests authored elsewhere are anti-pattern by construction"*).

- **011 pivoted correctly** — its POML named `tests/unit/Sprk.Bff.Api.Tests/AccessControl/`, which is
  **not** a KEEP path; it filed in `tests/integration/auth/**` instead and documented the §6.5 path-C
  deviation in the file header. Same assembly via the csproj auth glob, so `InternalsVisibleTo` still
  reaches the guard.
- **018 did not** — it created `tests/unit/Sprk.Bff.Api.Tests/AccessControl/ScopeInjectorBoundTests.cs`
  (22 tests), the same non-KEEP path. Confirmed: that directory **does not exist** on this branch today,
  so 018 would be creating it.
- **025's POML also targets it** (`.../AccessControl/ExternalAccessEndpointTests.cs`) — so this is a POML
  authoring defect, not a one-off agent slip. **Fix the POMLs, not just the files.**

Decide at merge: 018's injector is a pure static function → `tests/unit/domain/**` is the right home.
Do **not** let the wave land with two agents disagreeing about where auth tests live.

## A11. Stale-assembly trap, 5th instance — and a NEW mechanism

011 hit a *phantom test failure that contradicted the source on disk*. Cause: **`Copy-Item` preserves
`LastWriteTime`**, so restoring a file from a backup moves its timestamp **backwards**; MSBuild then judges
the stale DLL up-to-date and `dotnet test --no-build` silently runs the **old assembly**. Caught by reading
the source, then `touch` + rebuild.

Also concretely: **`dotnet build Spaarke.sln` did NOT refresh the BFF test project's output** — the test
csproj must be built explicitly.

This is the 5th stale-assembly instance across two batches (072's false-PASS perturbation, 075's `if
(false)` CS0162, 075's dangling reference, 018's re-run, now this) but the **first with a backwards-time
mechanism** rather than a skipped build. "Read the build result before the test result" does **not** catch
this one — the build result says *up-to-date*, truthfully, about the wrong file. → **AP-8 addendum or its
own AP entry** (main-session-only, `.claude/`).

## A12. 011's residual worth escalating — maker-authored views are DATA

011's guard now refuses any `<link-entity>` on the external-module seam. **A maker-authored `savedquery`
view that surfaces a column through a self-referential lookup emits a same-entity `<link-entity>` and will
now 400.** A DataGrid can replay a saved query's FetchXML into this seam. **No repo grep can rule this
out** — it is environment data, not code.

→ Enumerate `savedquery.fetchxml` for registered module entities **per environment** before deploy.
This is a **deploy-ordering obligation** (§7), not a code fix.

## A13. 🔴 SYSTEMIC — the publish-size gate is measuring in two incompatible conventions

Only visible from the orchestrator position, because **every individual report was internally correct.**

| Agent | Base | Reported | Compared against | Delta |
|---|---|---|---|---|
| 011 | `3b87b07bc` | **45.07 MB** incl. PDBs | 44.96 | +0.11 |
| 018 | `3b87b07bc` | **45.07 MB** incl. PDBs | 44.96 | +0.11 |
| 020 | `3b87b07bc` | **43.78 MB** incl. PDBs | 43.69 | +0.09 |

**Identical base commit. Identical stated method ("compressed incl. PDBs"). 1.29 MB apart.**

The POMLs carry the same split — baselines cluster at **~43.65–43.71 MB** (24 POMLs) and **44.96 MB**
(31 POMLs), a ~1.3 MB gap. Each agent compared against whichever cluster its own POML cited, got a small
delta, and correctly reported "within ceiling". Each was right. **The set is incoherent.**

Root CLAUDE.md §10 states the baseline as **44.96 MB incl. PDBs / 44.05 excl.** — a 0.91 MB PDB delta. The
~43.7 cluster sits *below even the excl-PDB figure*, so it is a **third** convention, not the excl-PDB one.

**Why this matters even though nothing is near the 60 MB ceiling:** §10's escalation rule is "≥+5 MB
single-task delta → justify; ≥55 MB cumulative → architecture review". With two conventions differing by
1.3 MB in circulation, a genuine regression can be absorbed as a convention artifact, and a convention
change can be misread as a regression. The gate still bounds the absolute worst case; it no longer
reliably detects **drift**, which is the thing it was added for.

**Action (main session, not an agent):** pin ONE convention — exact command, RID, configuration,
compression level, PDB in/out — in `.claude/constraints/azure-deployment.md`, then re-baseline every POML
citing the stale cluster. Until then, treat cross-task size comparisons as unreliable and compare only
within a convention.

**This is AP-8 again, at the orchestration layer.** Six agents each ran the §10 check correctly and each
reported a passing number. The defect is in the *relationship* between their answers, which no agent can
see and no per-task gate can catch.

## A14. 020 — verified, and its own double failed a NEW way

Verified in main session, all three load-bearing claims:
- **`GrantMembershipAsync` is still at ZERO callers** (the project's binding rule) — the only hits are its
  definition (`SpeContainerMembershipService.cs:59`) and two doc comments, one of which
  (`RevokeExternalAccessEndpoint.cs:247`) states exactly that. Rule holds.
- **The M1/M2 paging false assurance IS inherited** — `SpeContainerMembershipService.cs:167-168` is a
  single `.Containers[id].Permissions.GetAsync(ct)` with **no `@odata.nextLink` loop**. So a per-member
  `NoPermissionFound` can be wrong on a multi-page container. The agent documented this in the method
  XML doc, the test-class header AND its notes rather than papering over it — and correctly declined to
  fix it here, because forking the matcher is exactly what task 017 deleted.
- **The `sprk_enddate` read-side asymmetry IS real** — the grant query passes `TodayUtc`
  (`ExternalParticipationService.cs:578`) while the membership query is `statecode eq 0` alone (`:620`).
  So **a membership ended by date but never deactivated still confers inherited access.** Revoke
  deliberately over-includes (fail-closed); the READ path is the exposure. → task 043 must decide.

**Two design refusals worth keeping**, both correct:
- It did **not** add a fifth enum state, because `PartiallyRemoved` "would be a state whose *name* sounds
  acceptable." That is the permissive-default instinct caught at the naming layer.
- It did **not** add per-member cache invalidation, because the member list only exists when a
  `ContainerId` was supplied — so it would fire on some org revokes and not others, and an inconsistent
  invalidation contract is worse than a uniform documented 60s TTL. Deferred to 043.

`MembersEnumerated` is nullable so that `null` = "could not establish the list", which `(0,0,0,0)` cannot
express — pinned by a serialization test specifically because a `WhenWritingNull` option added *elsewhere*
would omit the field and silently break a client's `=== null` check. Good defensive reasoning about a
change that would arrive from outside the file.

### The double-defect class has a 5th instance with a NEW mechanism

020's own test double derived member emails from **the first 8 GUID chars, which all three fixture members
shared** — so "three members" was **one email, three times**, and the fan-out was never actually tested.
Fixed by *stating* identities in a map rather than deriving them.

Note this is **not** match-everything. The previous four were permissive fallbacks; this one **collapsed
three distinct entities into one**. Same family — a double that models what the code is *for* rather than
what it *does* — different failure mode. Derived test identities are a trap of their own: they look
rigorous and silently alias.

Both of 020's doubles now **throw** on unmodelled input (wrong entity set, missing `statecode`/org-id
predicate, null `$top`, unknown contact), and the contact-path double throws if the junction is queried at
all.

### 020 → 024 hand-off is unusually good — honour it

020 left the test file's structure intact (existing 17 tests untouched except the one that *pinned the
gap*, flipped in place with its position preserved; new work in marked regions with their own fixtures).
And it named the convergence: **one *paged* read + local email match + delete-by-permission-id fixes both
the M1/M2 paging false assurance AND the N+1 020 introduced** (N members = N full permission-list reads —
negligible at 1 member/org today, real at the 200 bound). Also flagged that
`RemoveAllExternalMembersAsync` is **not** reusable, since it removes *every* external member. Put this in
024's POML.

### 020's un-falsifiable list → task 047

Three ways cleanup reports a healthy-looking `NoPermissionFound` for a member who still has access:
(1) the paging gap; (2) SPE permission lists are **eventually consistent**, so a removed permission may
remain observable for a window; (3) the match is case-insensitive but **not alias/proxy-address aware**, so
a member invited under an address other than `contact.emailaddress1` reads as absent. All three need live
SPE.

Also: `ExternalParticipationService.cs:610-612` carries a now-resolved caveat ("*confirm against the
created junction schema*") — §2 of 020's notes verified it. Delete at merge (outside 020's modify-set).

## A15. 015 — the strongest instance of this batch's theme yet

**Nine existing tests were not vacuous. They were pinning the defect as the contract.** Their Moq setups
matched the *literal argument* `options: null` — and `options: null` **is** A-10 (first page only, token
discarded). They would have stayed green for exactly as long as the bug stood, and gone red the moment
anyone fixed it.

That is a rung above everything else found here. A vacuous test asserts nothing; **this asserted the
wrong thing, precisely and on purpose-looking terms.** And the repair choice matters: the agent replaced
the matcher with one asserting *real paging options* rather than loosening to `It.IsAny` — loosening
would have converted "pins the bug" into "pins nothing," which reads as a fix and is not one.

### Anti-vacuity design worth copying

The cap pair: perturbation "cap never flagged" and "cap always flagged at ceiling" each fail exactly
**1** test. Both cases return **exactly 5,000 ids**, so *no count assertion can separate them* — only
`Capped` does, and it fails in **both directions**. Page size (500) is held strictly below the ceiling
(5,000) specifically so the two cannot collapse into one another. That is a guard designed against its
own vacuity, not just perturbed after the fact.

Perturbation P3 also caught a weak test of the agent's own: *"a record beyond the first page"* was picking
by construction order and passing **by luck** against a single-page build. Fixed to ask the simulator for
the record it actually places last.

### `Capped` closes NFR-03's DETECTION half — the SURFACING half is still open

I assigned the NFR-03 gap to 015 (§A7). Precise status now: 015 added `AccessibleRecordSet.Capped` /
`CapLimit`, so a cap is **knowable** where before it was silent. But NFR-03 requires *"the user sees
**Only 5,000 records displayed**"* — and **nothing renders `Capped`**. So NFR-03 is **half closed**:
server-side detection exists, user-visible surfacing does not. Do not mark NFR-03 satisfied.
`Capped` also covers the membership term only, not the unpaged grant term → **task 028** widens it.

### 🔴 F-1 VERIFIED — the A-10 shape is live on two MORE surfaces

There are exactly **three** `options: null` callers of the membership resolver. 015 fixed the one in its
modify-set; **two live ones remain**:

| Caller | Status |
|---|---|
| `Infrastructure/ExternalAccess/AccessibleRecordSetService.cs:193` | ✅ fixed by 015 |
| `Services/Ai/Narrators/DailyBriefingCollector.cs:628` | 🔴 **still `options: null`, reads only `.Ids`** |
| `Services/Workspace/BriefingService.cs:292` | 🔴 **still `options: null`, reads only `.Ids`** |

**Severity differs from A-10 and should be stated honestly.** On the accessible-record-set path an
under-grant is an authorization under-grant. On the Briefing path it is a **silent completeness failure**:
a daily briefing quietly omits every matter past the first page and tells the user nothing. Not a
disclosure — fail-closed security-wise — but it is NFR-03's exact prohibition ("caps must never be
silent") on a **shipped, user-facing** surface. **File as a task**; both files were outside 015's scope.

### Keyset paging is not expressible — good constraint to have recorded

Keyset would have been the better scheme; **Dataverse does not support `gt`/`lt` on a
`uniqueidentifier`**, so it cannot be written. Hence page/count + paging cookie + `<order>` on the
primary id, with has-more from `EntityCollection.MoreRecords` so **no data row is consumed to answer it**.

### 015's 10-item un-falsifiable list → task 047, and I closed part of #2 here

Everything is asserted against a simulator, so *the simulator's model of Dataverse* is what is under test.
Item **#2 was flagged the highest-value live check**: the `<order>` key is `{entity}id` **derived by
convention**, with nothing proving that column exists on the nine target entities. A wrong name is a
400 — fail-closed and loud, but undetected until it fires in production.

**Partially closed in main session** (the agent had no reason to consult these): the convention holds
**9/9**. Eight are confirmed in `docs/data-model/`; `sprk_todoid` is absent there but confirmed twice
elsewhere — live server code `TodoGenerationService.cs:761` uses `ColumnSet("sprk_todoid", …)`, and
`docs/architecture/spaarke-todo-architecture.md:32` marks it PK. The `docs/data-model/` miss is a
**documentation-placement gap for `sprk_todo`, not a schema gap** (worth folding into task 026).

**Residual for 047 is now narrower and better posed**: not *"does the column exist"* (answered, 9/9) but
*"does Dataverse accept `<order>` on it for this query shape, and is `page`/`count` paging honoured at
all"* — because if `page` is ignored for this shape, every test still passes and production re-serves
page 1 forever (015's item #1). Remaining items: GUID collation, paging-cookie escaping/expiry/replay,
whether `MoreRecords` is populated for FetchXml via `ServiceClient`, concurrent writes, whether the 5,000
ceiling is ever reached in production, OR-filter matching, the transitive `includeRelated` `in` operator,
and whether an **impersonated** read pages identically — that last one is the seam tasks 034/036 build on.

## A16. 013 — WAVE A COMPLETE (6/6), and the finding that outranks the task

### 🔴🔴 `ExtractVerifiedEmail` VERIFIES NOTHING — verified in main session

`WorkforcePrincipalResolver.ExtractVerifiedEmail` (`:178-186`) performs **zero verification**. It returns
the first present claim of `email` → `preferred_username` → `upn` → `ClaimTypes.Email`. Its own doc
comment asserts *"tenant-verified email/UPN"* and *"Entra populates it from the verified primary email"* —
that is a statement about **Entra's** behaviour, not a check performed anywhere in this codebase.
`preferred_username` and `upn` are documented by Microsoft as **mutable and unsuitable for authorization**,
and they sit in the fallback chain.

**The premise is not hypothetical — multitenancy is mandated here**, verified:
- `design.md:96` — Type 2 (customer employee, no licence, has contact) signs in via **workforce Entra
  (multitenant, per-customer consent)**
- `notes/design-register.md:129` — **E-6**: "Multitenant app registration + per-customer admin consent for
  Type 2 workforce sign-in"
- `ADR-028` **A2** — Teams-host collaboration users MUST authenticate against a **multitenant** app
  registration

"Tenant-verified by Entra" is a single-tenant property. The Type 2 door and the Teams host are both
multitenant **by design**, and that is exactly the population reaching this code.

### 🔴 The third site BYPASSES 013's fix by construction

`AccessibleRecordSetService.cs:208-213` (verified):

```csharp
if (grantContactId is null && !string.IsNullOrWhiteSpace(principal.Email))
{
    grantContactId = await _participations
        .ResolveExternalContactAsync(oid: null, email: principal.Email, ct)   // ← oid: null
```

…then unions that contact's grants. `principal.Email` is `ExtractVerifiedEmail`'s output
(`CallerPrincipalResolver.cs:444`).

013 hardened `TryResolveContactByWorkforceIdentityAsync`. This is a **different method**, and passing
`oid: null` bypasses the new guard **by construction**. Worse, the two compose badly: 013's guard makes a
mismatched caller resolve to **no** `ContactId` — which is precisely the condition (`grantContactId is
null`) that opens this email-only fallback. **A denied caller can fall through into the ungated path.**

⚠️ **Stated honestly — verified vs inferred.** VERIFIED: the code shapes above, the multitenant mandate,
and that project fact #1 makes this filter the entire security boundary on BFF reads. **INFERRED and NOT
yet proven**: that the `email` claim is actually attacker-controllable in *this* registration's live
configuration (013's own un-falsifiable #2 — needs live Entra), and that the deny→fallthrough composition
is reachable end-to-end rather than blocked earlier. **Both are testable. Neither has been tested.**
Do not report this as an exploited hole; report it as a hole that nothing currently prevents.

**This is the third instance of "a name or comment asserts a security property the implementation does not
provide"** — after 077's decorative filter (a filter *was* attached, so four human sweeps called it gated)
and 022's three false "enforcement happens elsewhere" comments. Here the assertion is in the **method
name**, which is worse: a name is read far more often than a body.

### Main-session fix applied

`IIdentityNormalizationService.cs:44-51` carried the load-bearing false claim — *"tenant-verified by
Entra, so no oid-binding / first-login hijack protection is required here (that concern is specific to the
CIAM external path)"*. **That sentence is why this path shipped without the protection CIAM has.**
Rewritten to record what is actually true, what 013 closed, what remains open, and the bypass. Outside
013's modify-set, so main session applied it.

### 013's two escalations — BOTH need an owner

**1 (§6) — FR-12 does not fully close A-18.** The guard denies *bound* contacts and resolves *unbound*
ones, exactly as FR-12 mandates. But **this plane never writes a binding**, whereas CIAM binds on first
login and thereby closes its own window. So A-18's original scenario stays live **indefinitely** for any
contact holding grants but no workforce oid. Options: bind-on-first-resolve (mirror CIAM) · require an
out-of-band invite token · accept + document. **Spec decision. Do not mark A-18 closed.**

**2 (§6.5) — ADR-038 ban B8.** Nine tests drive an `internal` pure function via `InternalsVisibleTo`; B8
bans that. The reviewer recommends **path B (amendment)**: ~10 sibling sites repo-wide do the same and
describe it as *"the convention already used across this codebase"*, so **B8 currently bans what the
codebase practices**, and B1/B8 contradict each other for this exact case (a pure internal function is
either tested through `InternalsVisibleTo` or not tested at all). Needs an ADR Tensions entry in
`design.md`/`spec.md` per §6.5 — at the point of decision, not deferred.

### Other 013 findings for triage

- **CIAM is now the weaker plane** — `ExternalParticipationService.cs:384` still `$top=1` silently picks
  one on an ambiguous email; `:313` compares oids **as strings**.
- **Cross-plane blind spot** — workforce reads only `azureactivedirectoryobjectid`, CIAM only
  `sprk_externalobjectid`. **A contact bound on one plane reads as unbound to the other.**
- `TryResolveContactIdAsync` keeps `TopCount = 1` with no ambiguity guard.
- **The guard is silently INERT** where `contact.azureactivedirectoryobjectid` is unprovisioned — it passes
  everything and looks identical to one that fired. No test can catch it (configuration, not code); only
  the new WARN makes it visible.

### Process notes worth keeping

- **13 mutations, all killed.** Three survived initially and the agent **fixed the cause rather than the
  report** — including a double that ignored `TopCount`/`ColumnSet` (it now projects like Dataverse). Same
  double-defect class as 020 and 075: **6th instance**.
- **A false green in its own tooling**: an early harness run reported all-8-detected only because `tail -3`
  **hid "Build succeeded"**. Cousin of [G-12] — the verification instrument lying, not the code.
- **A mutation harness run against a dirty tree wiped uncommitted gate fixes** via its `git checkout --`
  cleanup. Caught by grep rather than assumed. Commit before running destructive harnesses.

## A17. 081 HARDENING — the ordering is now provably redundant, and my risk model was wrong

Owner-directed after the owner correctly rejected my framing. I had written that a latent risk in
`CallerIdentity` was *"saved by"* the delegated-scope branch running before the application branch. That is
a **latent invariant, not a defence** — a safety property holding by accident of statement order,
documented nowhere, that a plausible refactor silently removes.

Commits `1a77288b0` (harden) + `41cb87310` (docs), base `2b3b07de2` unchanged, `CallerIdentity.cs` +
its tests only (+197/−7).

### Three layers, and the proof that order stopped mattering

1. **Provenance self-check** — resolution now returns *which claim type* supplied each value. Same claim
   type for both ⇒ the equality is a self-comparison ⇒ `Indeterminate`, never `Application`.
2. **Explicit conjunction** — `!hasDelegatedScope` now stated in **both** application branches' own
   conditions (rules 4 and 5). Early return kept as well.
3. **Disjointness assertion** — `ObjectIdClaimTypes ∩ SubjectClaimTypes = ∅` asserted by a test, so
   overlapping the lists fails the build **at the moment the mistake is made**.

| # | Perturbation | Result |
|---|---|---|
| P9 | Overlap the lists — the literal #832 refactor | 4 RED, and `Kind` **stayed `Indeterminate`** — layer 1 contained the damage, layer 3 flagged the edit |
| P10 | Overlap **+** disable the provenance check | RED with `Kind == Application` — isolates layer 1 as the specific defence |
| P11 | Remove the conjunction, ordering intact | **19 GREEN** — early return alone suffices |
| **P12** | **Invert the ordering, conjunction intact** | **19 GREEN — the conjunction alone suffices. Order is now genuinely redundant.** |
| P13 | Remove conjunction **and** invert ordering | 2 RED — the suite notices total loss |

**P12 is the deliverable.** The owner's question was "shouldn't this be fixed so the execution order is the
saving function" — the answer is now that order is *no longer* the saving function at all, and that is
demonstrated rather than asserted.

### 🔴 MY RISK MODEL WAS WRONG — the agent's retro-check said NO, as instructed

I asked it to confirm the new collapsed-read test would have failed against its previous commit, and to
say so if not. **It would not** — restored `CallerIdentity.cs` from `14869cd61`, ran the new tests, got
**19/19 pass**, including `TheBugShapeFromPr832_*`.

Why: **the bug-shape *principal* was already safe.** With the `objectId` read already correctly paired,
a token carrying `NameIdentifier` but no `oid`/`objectidentifier` yields `objectId == null`, and the old
rule-5 guard `!IsNullOrWhiteSpace(objectId)` already rejected the branch. **No input principal can trigger
the collapse while the lists are disjoint.**

So the threat is a **source edit**, not a crafted token. My *risk statement* said that correctly ("if
someone later normalises it…"), but **the test I prescribed conflated the two** — I asked for an
input-shape test to prove a source-edit risk. Those are different threat models and the input-shape test
cannot discriminate. P9/P10 are the discriminating evidence; `TheBugShapeFromPr832_*` is a **regression
pin** (it locks in that `NameIdentifier` must never satisfy the `oid` read), not commit-discriminating
evidence. Recorded in the agent's notes so the two are not later confused.

Verified both directions: the #832 refactor against the **old** classifier yields `Kind == Application`
(bypass unimpeded); against the **new** one it yields `Indeterminate` **and** fails the build. That gap is
what this round closed.

Consequence worth keeping: the provenance check is **unreachable today** and deliberately kept live, so the
failure mode remains a denial even if the disjointness test is ever deleted. Documented in code.

### Verification + two accepted trade-offs

Build 0 warnings → `Spaarke.Core.Tests` **64/64** → `Sprk.Bff.Api.Tests` **11,191 / 0** → `Spaarke.ArchTests`
**9 fail / 105 pass, identical to baseline**. Step 9.5 re-run: code-review **0 Critical / 0 Warnings**,
adr-check **0 Violations**.

- ⚠️ **`ObjectIdClaimTypes` / `SubjectClaimTypes` are `public` purely for testability.** The repo
  convention is `internal` + `InternalsVisibleTo`, but `Spaarke.Core.csproj` carries no such entry and
  adding one would modify a csproj otherwise reported as pristine. **This is the SECOND independent hit on
  ADR-038's B1/B8 contradiction this wave** — task 013 escalated it (§A16) after nine tests drove an
  `internal` pure function. B8 bans `InternalsVisibleTo` tests; the only alternative is widening public
  API for tests. Two tasks, two files, same fork. **Strengthens 013's path-B amendment case materially** —
  decide it once, for both.
- `CallerIdentity.cs` grew 244 → 361 lines, ~65% XML doc, still one responsibility. Legitimate per
  `COMPONENT-COMPLEXITY.md` (a large *cohesive* file is fine); the growth **is** the threat-model warning.
  Accepted, not decomposed.

Prior caveats unchanged: no live token decoded (`sub == oid` rests on documented Entra behaviour), no test
exercises the real HTTP pipeline, and the allow-list remains an operator prerequisite for
`customer-provisioning-orchestration-r1`.
