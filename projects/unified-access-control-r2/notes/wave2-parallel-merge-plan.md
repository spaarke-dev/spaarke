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

## 8. Process note worth keeping

The POMLs marked 073 and 079 `∥-safe: true`, and on *modify* targets that was accurate. It did not
cover: (a) sub-agents share one worktree by default, so concurrent edits to a shared file are **lost
writes, not git conflicts**; (b) both tasks needed edits to the same ArchTest file; (c) concurrent
`dotnet build` contends on `bin`/`obj`. Worktree isolation plus a main-session-owned file list handled
all three. **Consider making that the standard dispatch pattern** for this project rather than an
ad-hoc choice — and consider whether `∥-safe` should be split into "disjoint modify targets" vs "no
shared-file coordination needed", because they are different properties and only the first is what the
POMLs currently assert.
