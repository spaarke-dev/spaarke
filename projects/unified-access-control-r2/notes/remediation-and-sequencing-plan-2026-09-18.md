# Remediation & sequencing plan — 2026-09-18 (session 16)

> **Owner request**: *"put together a full plan of how to best address these issues and open tasks."*
> Written after re-establishing ground truth from files and the live GitHub API, not from the prior
> session's summary. Every number below was recomputed; where a prior artifact disagrees, the
> disagreement is named rather than smoothed over.
>
> **Scope**: 32 open tasks · 25 register entries · 1 open PR (#950) · 6 owner decisions.

---

## 0. Corrections to the session-15 handoff (found while rebuilding ground truth)

The handoff is the artifact the next session trusts. Four of its claims were wrong. Recording them
first, because three of the four would have propagated into this plan.

| # | What the handoff said | What is actually true | Why it matters |
|---|---|---|---|
| **C-1** | *"on `3b409bfc1` two checks were pending and `Router` (the ONLY required context) had not reported at all; **no failures**"* | `Router` **reported `failure`** on `3b409bfc1` **and** on `597882f7c`. Trivy also failed. | I recorded a non-failure where a **red required check** stood. Wrong in the dangerous direction. |
| **C-2** | *"count pending explicitly (`gh pr checks 950 \| grep -c pending` must be 0)"* | `gh pr checks` **never lists `Router` at all** when the job has not started. The recipe cannot see the one check that gates the merge. | A merge-readiness probe blind to the required check. Same class as this project's recurring root error: **an observation taken outside the thing being observed.** |
| **C-3** | *"docs-only shapes skip Tier 1 while `Router` still succeeds"* | 🔴 **WRONG FOR THIS PR — corrected 2026-09-19 by observation.** True for a docs-only **PR**; false for a docs-only **commit** on a mixed PR. Verified on `d8c139697`, a commit touching only `projects/**`: **both tier jobs dispatched**, and they are gated `if: needs.classify.outputs.docs_only != 'true'` (`ci-router.yml:211`, `:229`) — so `docs_only` was **false**. Run `event` = `pull_request`, and `:140-142` states PRs classify against the **PR base**; #950 touches BFF throughout, so **no push on this PR can ever be `docs_only`, and Tier 1 always runs.** | Third time this claim has been wrong in a different way, and it is load-bearing for a merge decision. **Tier 1 must be green on the head SHA — never expect it to be skipped here.** |
| **C-4** | *"75 done · 32 open · **3 escalated / blocked**"* | 75 done · 32 open · **3 escalated (012, 023, 062) + 1 blocked (034)** = 111. | Undercount of one; 034 is a distinct state with a distinct owner decision behind it. |

### Verified clean (recomputed independently, not trusted)

- **111 POMLs / 111 index rows, and zero orphans in BOTH directions.** POML `<status>pending` = 32
  matches index `open` = 32 exactly; `completed` 74 + `completed-with-escalation` 4 +
  `blocked-shipped` 1 = 79 = index `done` 75 + `escalated` 3 + `blocked` 1.
  This is the both-sides comparison **ISS-025 says `check-task-status-drift.ps1` structurally cannot
  perform** — it pairs from the POML side only. Recomputing it was the point.
- The 32 open ids: `036 044 047 054 055 056 057 058 064 065 066 067 069 082 087 088 089 090 093 094
  095 099 101 105 107 108 109 110 111 112 113 114`.

---

## 1. Workstream A — make PR #950 mergeable

### 1.1 The CI mechanism, established rather than assumed

`Router` is the single required status check (`ci-router.yml:272-322`, job name `Router`, workflow
`CI`; the ruleset confirms the required context is literally `Router`). It is `if: always()` and
`needs: [classify, tier1, tier2]`, and adjudicates via `re-actors/alls-green` over **classify +
tier1 only** — tier2 is excluded *by construction* (`:302-311`), because a `timeout-minutes` kill
reports `cancelled`, which neither `allowed-failures` nor `allowed-skips` covers.

**Why the two earlier SHAs were red — and it was me, not the code:**

`concurrency.group` is `ci-router-${{ github.event.pull_request.number || github.sha }}` with
`cancel-in-progress: true` (`:108-109`). On a PR the group is keyed on the **PR number**, so **every
push cancels the previous run**. alls-green treats a `cancelled` dependency as a hard failure.

| SHA | Run | classify | tier1 | `Router` |
|---|---|---|---|---|
| `597882f7c` | cancelled | success | **all jobs cancelled** | **failure** |
| `3b409bfc1` | cancelled | **cancelled** | skipped | **failure** |
| `cb9ec2ea2` | **completed** | success | **all 8 jobs success** | ✅ **success** (final) |
| `d8c139697` (head, session 16) | in_progress | success | running — **not skipped**, see C-3 | not started |

So both reds are artefacts of pushing a follow-up commit seconds later — the exact hazard
`ci-router.yml:79-96` documents for master and explicitly leaves in place for PRs ("PR behaviour is
UNCHANGED… which is where the savings actually matter"). They carry **no quality signal**.

On the head, `Router` has simply not started: it waits on the advisory Tier 2 unit-test job. With
classify green and all 8 Tier 1 jobs green, and tier2 not adjudicated, **`Router` should go green
when Tier 2 finishes.** `mergeStateStatus: BLOCKED` today reflects a required check that has not yet
reported — not a failure.

### 1.2 The correct merge-readiness probe (replaces C-2)

```bash
SHA=$(gh pr view 950 --json headRefOid --jq .headRefOid)
gh api "repos/spaarke-dev/spaarke/commits/$SHA/check-runs?per_page=100" \
  --jq '.check_runs[] | select(.name=="Router") | "\(.status)\t\(.conclusion)"'
# REQUIRE exactly: completed   success
```

Three rules that follow from §1.1 and belong in the merge routine:

1. **Name `Router` explicitly on the head SHA.** `gh pr checks` is not sufficient — absence there is
   indistinguishable from pending.
2. **A red `Router` on a superseded SHA is expected ONLY IF that run was still in flight** when the
   next push landed — narrowed 2026-09-19, because I had written this too strongly. Evidence:
   `cb9ec2ea2` finished its run *before* the session-16 push and kept a genuine
   **`completed / success`**, whereas `3b409bfc1` and `597882f7c` were both cancelled mid-run and went
   red. So: check whether the superseded run **completed** before dismissing its red. Writing
   "expected" without that qualifier teaches the next session to wave away a real failure.
3. **Do not push while judging.** A push during adjudication cancels the run and manufactures the
   red. If the head must change, re-probe after.
4. **Do not poll it in a loop.** Probed twice on 2026-09-18, minutes apart: both returned *no `Router`
   check run*, with `Tier 2 / Full Unit Tests` and legacy `Build & Test (Debug)` still `in_progress`
   and `mergeStateStatus=BLOCKED`. A third probe in the same window teaches nothing — Tier 2 carries a
   **30-minute** runaway guard and is **excluded from adjudication anyway**, so `Router`'s outcome is
   already fixed by jobs that are green. Re-probe **once, at the moment the merge decision is taken**.
   Repeated polling of a determined-but-unreported result is the same error as trusting a too-small
   fixture: the observation is taken before the thing observed has happened.

### 1.3 The three pre-merge live-Dataverse checks (unchanged, still owner-gated)

None can be discharged offline; the `dataverse` MCP server is **down this session**
(`CONNECTION_CLOSED`), so this is not a matter of effort.

| Check | Why it is not optional | Task |
|---|---|---|
| **063 DOWNGRADE** — seed Full Access, `share-user` at View Only, read `accessrightsmask`, assert **1** | The **only** evidence `ModifyAccess` *replaces* rather than *adds*. The entire share write path assumes replacement. If it adds, the read-back refuses loudly while the user **silently keeps Write and Delete**. | 063 |
| **task 100 reminder `appnotification` write** | The MDA bell is the delivery mechanism; never exercised live | 100 |
| **H9 slot guard, real ARM** | #987; guards "scheduled jobs run once" across slot swaps | 103 |

⚠️ **Environment lens (owner, 2026-09-17)**: the connected system is **dev with test records**. That
changes **urgency, not validity** — 063's downgrade check in particular becomes materially riskier
the moment a real customer environment is provisioned.

---

## 2. Workstream B — repair the inaccurate task files FIRST

The 2026-09-18 audit found **8** task files with a wrong load-bearing sentence. Re-verified against
the files today: **it is 9.** Every citation below was confirmed verbatim by grep, except where noted.

| Task | Exact defect (file:line, verified) | Class |
|---|---|---|
| **093** | Four false premises. Agent audit in flight — see §6. | **major** |
| **082** | 🔴 **Not a patch — a RESCOPE, like 093.** Audit's claims verified true: `<step order="0">` *"This is a hard gate, not a preference"* (`:116`); `<deps>…PR-832` (`:16`); `CallerIdentityCensusTests.cs` at `:87` + `:136`. **But it stopped counting far too early.** The staleness is the whole file **except `<goal>`**: criteria **1, 2, 3, 4, 7** (`:141-147` — not "1/2/7"), steps 1–3 (`:117-119`), the seed-last constraint (`:97`), `<parallel-reason>` (`:15`), all of `<justification>` (`:130-132`), and the stale 71/40 census numbers in `<origin>` (`:19`), `<notes>` (`:152`), `<model-tier-reason>` (`:10`) and `<effort-reason>` (`:12`). The file's own header (`:28-59`) already records that **PR #840** shipped the ratchet plus a rule more, and migrated 41 sites across 37 files to zero. **Genuine remaining scope = ONE deliverable**: answer the CLAUDE.md §11 four-primitive question in writing (step 4 / criterion 5 / the notes output). Re-tier `opus`/`xhigh` → `sonnet`/`medium`. | **rescope** |
| **089** | 🔴 **NEW — the audit called this benign.** `<gate>blocked — requires 081 + 082 merged</gate>` (`:6`); `<dependency task="081">Event hooks.</dependency>` (`:33`). Real deps are **087 + 088**. The "082" it names is a **live, unrelated task** (caller-identity census) — an implementer would block on the wrong work. | **major** |
| **088** | `<gate>requires 081 merged</gate>` (`:6`); `<dependency task="081">` (`:34`); `<step order="1">…thread it into 081 events</step>` (`:64`); `<constraint>…pre-081 events</constraint>` (`:42`). **Four** sites, not three. | numbering |
| **087** | `<gate>requires 080 (table)</gate>` (`:6`); `<goal>…with the 080 vocabulary</goal>` (`:23`); `<dependency task="080">` (`:37`). | numbering |
| **036** | `<file role="modify">…/appsettings.json` (`:30`) and `<step order="1">…document both states in appsettings.json comments</step>` (`:69`). **That file does not exist.** | minor |
| **054** | `<goal>CallerPrincipal exposes an accessible-service-request set…</goal>` (`:23`) contradicts task 028's owner ruling. The amendment exists only in `<notes>` (`:98`) — i.e. the binding element still states the retired premise. Criteria `:87-89` inherit it. | **major** |
| **095** | 🔴 **Worse than the audit said — wrong on TWO of three claims.** Real text at `:54-56`: *"`sprk_document` has real lookup columns for matter / project / invoice / workassignment / event — but **NOT** for `sprk_todo`, `account` or `contact`."* Against `Models.cs` (live-metadata-verified, `spaarkedev1` 2026-09-05): `sprk_relatedtodo → sprk_todo` (`:214`) **exists**, and `sprk_relatedcontact → contact` (`:210`) **exists**. Only **`account`** is genuinely absent (`:408`, "no account lookup in either family"). The audit named only `sprk_todo`. | **major** |
| **094** | Accurate as written; needs one **addition**: `ListChildrenAsync` (`:54`, `:83`) is **drive-keyed**, but under 076/083 the container is server-resolved. A drive-keyed probe reintroduces the `ClientSupplied` shape 083 deleted, which `SpeWriteSinkContainerProvenanceGuardTests` pins at **0**. The probe must be **record-keyed**. | addition |

**Non-actionable but noted**: tasks **041** (`:42`) and **061** (`:30`, `:79`) also cite the
nonexistent `appsettings.json`. Both are ✅ done — historical, not blocking.

### Why this workstream goes first

A wrong load-bearing sentence in a POML is not cosmetic here. This project's own count stands at
**seventeen** instances of a task file being wrong, and **the code has won every single time**.
Executing 054 as written would build a set the owner ruled must not exist; executing 089 as written
blocks on the wrong task. Repairing nine files costs one focused pass and removes nine chances to
build the wrong thing.

**Proposed shape**: ONE task, `115-task-file-accuracy-repair`, doing **087 / 088 / 089 / 036 / 054 /
095** plus 094's addition mechanically. Each edit asserted by expected-occurrence-count so a miss
throws rather than silently leaving a stale sentence — the discipline that caught the fabricated SHA
and the invalid XML last session.

**Two are excluded, because they need judgment rather than a patch**: **093** (four false premises;
agent audit in flight) and **082** (rescope to a single §11 decision memo). Both are *sequenced*, not
repaired, in §4 wave 3. A mechanical pass over either would produce a file that reads consistently
and still describes the wrong work — which is the exact failure mode this workstream exists to
remove.

---

## 3. Workstream C — the six queued stubs, plus the one I missed

Tasks **109–114** exist as deliberately-marked stubs (`<status>pending</status>` + a
`PREMISES NOT YET VERIFIED` comment). They must be authored via `/task-create` so each gets its own
premise check — writing six task files from unverified premises is precisely what §2 exists to undo.

| Task | Register | State |
|---|---|---|
| 109 | ISS-019 / #998 | 🔴 **NOT independently runnable — corrected 2026-09-18.** The register's suggested fix (a *second* query, `QueryActiveOrgIdsOutcomeAsync`, for the veto path) would **re-introduce the two-snapshot hazard task 043 deliberately removed**. 109 and 110 are **ONE change**: one read, projecting what both consumers need, returning an outcome plus two named sets. So 109 inherits 110's owner gate. |
| 110 | ISS-020 / #999 | 🔔 **Owner-gated — recommendation now on file, see D-2.** The "one shape cannot be right for both" dilemma **dissolves**; it was an artifact of projecting bare ids. |
| 111 | ISS-021 / #1000 | Ready. N is currently **0** (no org holds a standing grant), so the shape is wrong but the cost is unpaid. Lowest priority of the six. |
| 112 | ISS-022 / #1001 | Ready. `If-Match` on the deactivation; the read-then-write pair has **no optimistic concurrency at all**. |
| 113 | ISS-023 / #1002 | Owner-decided; design is the hard part. Agent audit in flight — §6. |
| 114 | ISS-024 / #1003 | Owner-decided. Ship the contract test **with** the code (`InternalUserShareContractTests.cs:94` asserts the retired 422). |

### 🔴 A gap in my own session-15 work

**ISS-025 / #1004 is in-project and has NO task.** The register's disposition table says so in as
many words: *"No task yet."* The owner's 2026-09-18 directive was *"we need the issues tracked AND
queue to be fixed in this project not deferred"* — I queued 109–114 for ISS-019…ISS-024 and left
ISS-025 out. Confirmed today: no `115` POML exists, and no index row names it.

The irony is exact: **the one tracked-but-unqueued entry is the one about a bookkeeping check that
cannot see unqueued work.** Recommendation: queue it as **task 116** (115 reserved for §2's repair
pass) — two or three lines in `check-task-status-drift.ps1`, asserting the row set and POML set are
**equal** and reporting unpaired index rows as drift.

---

## 4. Workstream D — revised execution sequence

Honouring the two standing rules: **sequence by dependency so each task builds on its dependencies'
finished work**, and **issues stay in-project unless immaterial or another project's confirmed
subject**.

| Wave | Tasks | Rationale |
|---|---|---|
| **0 — bookkeeping** | ✅ **AUTHORING DONE 2026-09-19 (session 17)** — **109–119 all authored/rewritten**; EXECUTION of **115** (§2 repair) and **116** (ISS-025 + ISS-029) still pending | Nothing here touches the evaluator. Removes nine wrong-instruction hazards before any execution. Verified after authoring: `Validate-TaskPoml.ps1` **110 clean / 0 errors / 6 pre-existing warnings, PASS** (it had been exiting 1 since session 15 on six stubs missing `&lt;steps&gt;`), drift **116/116 rc=0**, zero stub markers, zero `not-started`. |
| **1 — clean runnable** | **108** → **065** → **044** | All deps ✅. 108 is small (ISS-018/#995, needs only 063's strict read). **065 unblocks 066→067→099 *and* 069** — 099's escalation has already fired waiting on its M8. **044**'s deps (039/041/042/043) are all ✅ and it "excludes FR-20 by design", so it does **not** belong behind owner-blocked 036. |
| **2 — decided register work** | **114** → **112** → **111** | 114 first (owner-decided, small, and 065's picker consumes the same surface). ⚠️ **109 MOVED OUT to wave 4** — it is one change with 110 and therefore owner-gated (§3). 111 still touches `QueryActiveOrgIdsAsync`, so it should follow 109/110 if those land first. 🔴 **"Safe alone because N is currently 0" is FALSE — corrected 2026-09-19.** The loop iterates active org **MEMBERSHIPS** (`AccessibleRecordSetService.cs:1197`, `:1200`) and the `Rights == None` skip is **after** the read (`:1218-1221`), so reads are paid per membership regardless of standing grants; live data shows **2 memberships across 2 organizations**. 111 is still low-priority (N is small) but the stated reason was wrong, and it must re-measure N. |
| **3 — judgment, now resolved** | **093 → CLOSE** · **047** · **094** · **095** · **082 → decision memo** | 093 **closes as delivered** (§6.2, D-8), which **unblocks 047** — its only other dependant is 090. **082** becomes a single CLAUDE.md §11 decision memo, re-tiered `opus`/`xhigh` → `sonnet`/`medium`; it is **not** in §2's mechanical batch. **094** needs its record-keyed-probe note before starting; **095** needs the two-of-three-false-claims fix. |
| **4 — owner-gated** | ~~**107** (ADR-002 exception)~~ · ~~**110** (ISS-020)~~ · **054** (ISS-003/#964) · **036** (034's A-vs-B) | ✅ **Two gates ANSWERED 2026-09-19.** 107 is rescoped by **D-1** off every Dataverse-side mechanism into **A (107) + B (117) + C (118)**, plugins ruled out repo-wide. 110 is answered by **D-2** and is now the *verifier* for work task **109** performs. 054 and 036 still wait on a named decision in §5, not on engineering. |
| **5 — downstream chains** | 101 · 105 · 064 · 055 · 066 · 069 · 056 · 087 · 088 · 067 · 099 · 057 · 058 · 089 | Unchanged topology; 113 folds in beside 099 (see §6). |
| **6 — wrap-up** | **090** | `/test-diet` + the defer/issue **disposition gate** — which now has **30 entries (29 ISS + 1 DEF)** as of 2026-09-19 and must account for **ISS-025 and ISS-029** (both in `scripts/check-task-status-drift.ps1`, both folded into task 116). The "25 entries" figure this row carried was stale. |

**Do NOT start 107 or 093 as written.** That conclusion from session 15 survives re-verification.

---

## 5. Owner decision docket

### D-1 🔔 ADR-002 exception for task 107 — and it is NOT an ADR conflict

I framed this last session as *"the first ADR-002 exception ever granted in this repo"*, which made
it sound like a challenge to the ADR. Reading ADR-002 properly changes the framing, and the change
is in your favour.

ADR-002 has a **"⚠️ Restricted (Exception-Only)"** clause. Plugins **MAY** be used when **ALL** of:
*execution is synchronous and deterministic · completes in <50 ms p95 · logic limited to validation,
invariant enforcement, denormalization/projection, **audit stamping** · no external calls of any
kind · no orchestration or branching logic* — plus **MUST** be <200 LoC and <50 ms p95.

**Task 107 fits that envelope on every clause.** Stamping `today + 90` onto `sprk_expiresdate` in a
pre-operation step is synchronous, deterministic, external-call-free, branch-free, a handful of
lines, and squarely "invariant enforcement". So:

- This is **CLAUDE.md §6.5 path A** (project-scoped exception), not path B. The ADR is **not being
  challenged** — its own documented exception path is being invoked for the first time.
- What is needed is the approval **ADR-002 itself requires**, nothing more.
- **Rejected alternatives, recorded**: an entity-scope **business rule cannot work** (business rules
  run client-side on form open, and date arithmetic belongs to formula columns — so it covers *zero*
  API write paths); `ApplicationRequired` is **insufficient alone** (Dataverse does not error on a
  missing `ApplicationRequired` value; only `SystemRequired` is enforced, and custom columns cannot
  use it); a **webhook to the BFF** is **out-of-transaction**, so the row is live-and-unbounded for
  the gap and the read filter treats an undated row as never-expiring.
- **Make the mechanisms additive, not alternative**: `ApplicationRequired` for the MDA form path
  **and** the plugin for API/flow/import paths.
- ⚠️ Two build-topology facts that must be settled in the same breath: the plugin project is
  **net462**, and `ci-tier1-blocking.yml` states adding a net4x project to `Spaarke.sln` is a
  **deliberate Tier-1 failure on ubuntu**. Either factor the date computation into a
  **netstandard2.0** assembly the net10 tests can reference, or state explicitly that the plugin test
  project stays **out of the solution** and runs manually. Its cited reference implementation
  `BaseProxyPlugin.cs` is `[Obsolete]` *for violating ADR-002* and must not be copied.

**Ask**: approve the path-A exception (or decline, in which case 107 has no viable in-transaction
mechanism and the honest outcome is to close it and accept undated external-created rows).

### D-2 🔔 ISS-020 / #999 — the dilemma DISSOLVES, and my stated exposure was wrong

Fable audit complete; every load-bearing claim below re-verified by me against source.

**1. The tradeoff is an artifact of the projection, not a real conflict.** The reason "one shape
cannot be right for both" is that the junction query projects **bare ids**:
`ExternalParticipationService.cs:1092-1094` — `$filter=_sprk_contact_value eq {id} and statecode eq
0&$select=_sprk_organization_value`. Task 043 deliberately hoisted **ONE** junction read so the
additive term and the veto subject share a snapshot, and that invariant is worth keeping — **but it
is one READ, not one FILTER.** Add `sprk_enddate` to the `$select` and one read serves both
consumers with *different in-memory predicates*. No tradeoff survives.

**2. 🔴 My "exposure is nil" claim was FALSE** — it appeared in the session-15 handoff and in this
plan's first draft. It covers only the org-**expansion** term, which *is* standing-gated
(`:1209-1221`). The older org-**grant** term is **not**: `QueryOrganizationGrantRowsAsync`
(`:1011-1051`) gates on active + unexpired *grant rows* only — verified, there is no
`sprk_standinggrant` check on that path. And live org grants **existed**:
`notes/task-020-org-grant-spe-cleanup.md:18`, checked against live Dataverse — *"active org grants
exist for `Morrison Foerster LLP`, which has an active member."* Real exposure is bounded only by
**dev being a test environment**, not by a control.

**3. The question was never asked as policy.** It is: **does an org-keyed ethical wall still bind a
person whose membership has ended?** The junction's original design contract (sibling project
`SPA-external-access-platform-r2`, `notes/task-073-org-grant-design.md:83,90-92`) made `statecode`
the sole active/former gate and never listed `sprk_enddate` as access-bearing. So ISS-020 is a
contract **change**, not a deviation from one.

**Recommendation** (mine, on the audit's evidence): **bound the additive path YES, leave the veto
subject on `statecode` alone NO-CHANGE, and do not backfill** while the veto stays over-matching.
Implement as **one read, two named sets** — `ConferringOrganizationIds` (date-bounded, mirroring
`ExpiryPredicate`'s `ge` + null-never-ends at `:99-100`) and `WallSubjectOrganizationIds`
(statecode-active). This keeps task 043's single-snapshot invariant, keeps the NFR-02 one-read
budget, and closes **both** fail directions. Documented support for leaving the veto wide:
`spec.md:86` FR-23 *"denied … even holding Full Access"*, and `AccessibleRecordSetService.cs:550-554`
*"Denial deliberately OVER-matches."* **No existing rule decides part 1 — that is yours.**

**What would change it**: (a) you rule an org wall does *not* bind former members → bound everything
and backfill, no tradeoff ever existed; (b) you declare `sprk_enddate` merely informational → leave
as-is and control it in MDA process; (c) `sprk_enddate` turns out to be DateTime/UserLocal-behaved →
a read-time compare is wrong and it belongs in a Dataverse-side deactivation instead.

⚠️ **Undeterminable this session** (`dataverse` MCP down): whether any date-ended-but-active rows
exist right now, `sprk_enddate`'s exact behaviour, and whether `sprk_startdate` exists at all.

### D-3 Resequencing sign-off
Pulling **044** and **065** out from behind owner-blocked **036** (§4 wave 1). Mechanical
consequence of their deps, but it reorders your agreed 2026-09-15 sequence, so it is your call.

### D-4 ISS-003 / #964 — the product question blocking 054
054, 055, 056, 057, 058 all wait on it. 054 additionally needs the §2 goal rewrite regardless.

### D-5 The three live-Dataverse checks (§1.3)
Owner-driven because they need a tenant and a deploy.

### D-6 ISS-025 → task 116? (§3)
Recommend yes, per your own "tracked AND queued" directive.

### D-7 🔔 ISS-023 — does `/grant` need a "keep the level" mode? (contract change)

Your rule already holds on the renewal route (§6.1). The decision is whether `/grant` also needs it.

- **Option 1 — pin what exists, change no contract.** Add tests asserting
  `set-record-share-expiry` writes no level and 099's picker sends none; constrain 099. **Zero
  contract risk, closes the owner rule as stated.** Leaves `/grant` able to lower a level on a
  re-grant, but **no in-repo caller does that** — the modal forces a deliberate pick.
- **Option 2 — make `AccessLevel` nullable** (`ExternalAccessLevel?`): present = set exactly
  (downgrade preserved), omitted = keep the highest level conferring on the key's last conferring
  day. Request-independent, and the collapse can then never lower access. Cost: a request-shape
  change, plus `BuildGrantPayload` and the `Request()` test helper must change with it.

**My recommendation: Option 1 now, Option 2 only if you want `/grant` itself to be renewal-safe.**
Option 1 satisfies the decision you actually gave; Option 2 widens the API to defend against a
caller that does not exist. ⚠️ Either way, **099 must gain the constraint** — as specified it would
present a date picker while the server changed the level.

### D-8 🔔 093 — close it, and settle one question it leaves behind

093 closes as delivered (§6.2). Residue re-homes cleanly: a wizard-ordering jest test (small); the
live "files land in the project's own container" assertion → **add to 047**; secure **matter**
provisioning → its own task (`provision-project` is project-only, owner-deferred); comment hygiene.

**The question to settle**: the two remaining record-less upload callers (EmailComposer attachment,
Analysis wizard) — `EntityCreationService.ts:508-513` says they are debt to be reordered; task 076's
classification says they are legitimately **"parentless"**. Both cannot be right. Confirming 076's
reading lets the comment be deleted; confirming the comment's makes a new small task. **Also
undeterminable without config**: `ProjectPreFillService.cs:294-317` writes AI pre-fill files to a
`StagingContainerId` with **no cleanup job anywhere**, and the staging POST fires *before* the
IsSecure toggle renders — which is the one real mechanical argument for "collect IsSecure earlier".

### Also outstanding
**034**: option (A) scheduled nightly canary + required manual gate *(recommended)* vs (B) Dataverse
secrets in CI — blocks **036**. Owners still needed for **#988**, **#971**, **#993**. Portfolio sync
blocked on `gh auth refresh -s read:project,project`. **`/merge-to-master` remains a separate,
unmade decision** — pushing ≠ merging.

---

## 6. The three Fable audits — all complete, all spot-checked

Every load-bearing claim below was re-verified by me against source. Two of the three memos had a
wrong citation; none had a wrong conclusion. Where I checked and disagreed, my correction is marked.

| Audit | Verdict | Feeds |
|---|---|---|
| **ISS-020** | Framing **dissolved** — it was an artifact of projecting bare ids. 9-row consumer census. | **D-2**, tasks **109 + 110** |
| **ISS-023** | Renew-vs-set is **not** expressible on `/grant` without a contract change — **but it already exists as two ROUTES**. | **D-7**, task **113**, constraint on **099** |
| **093** | **Close as delivered**, residue re-homed. Create-first is **compiler-enforced**. | **D-8**, pulls **047** |

### 6.1 ISS-023 — the rule already holds on the renewal path; the question is `/grant`

**The distinction exists at system level already.** `/grant` **sets** (level required) and
`POST …/set-record-share-expiry` **renews** — and the renewal endpoint writes **only**
`sprk_expiresdate` (`SetRecordShareExpiryEndpoint.cs:213-216`), lapsed rows included. So the owner's
rule *"a renewal keeps the previous level"* **already holds there by construction**. Nothing pins it,
which is the actual gap. Task **099**'s picker is already specified to call that endpoint
(`099-*.poml:39, :75`).

**On `/grant` it is not expressible.** Verified: `InviteExternalUserRequest.cs:29` is `int
AccessLevel` — **non-nullable**, and deliberately so: it sits among `string? FirstName`, `DateOnly?
ExpiryDate`, `Guid? OrganizationId`, which *are* nullable. `GrantAccessRequest.cs:21-25` documents
`ExpiryDate` as *"Optional in the REQUEST"* while `AccessLevel` carries no such clause. The enum has
no zero member, so an omitted value binds to `0` and is refused 400. Every shipped caller sends a
level, and `AccessGrantModal` **forces** a per-row pick. So making it optional is a **contract
change** — which is why this is a decision (D-7), not an implementation detail.

**"Infer renewal from row state" is decisively rejected**, on an argument I would not have found:
it is **clock-dependent** — the same payload is a "set" at 23:59Z and a "renew" at 00:01Z — and
reminders fire **30/14/7/3/1 days BEFORE** expiry (`spec.md:119`), so the ordinary renewal arrives
on a **still-live** key, gets classified "set", and ISS-023 goes unfixed.

### 6.2 093 — close as delivered. Create-first is enforced by the COMPILER

`uploadFilesToSpe(entityLogicalName, recordId, files, onProgress?)`
(`EntityCreationService.ts:479-484`), with `:475` *"must already exist when this is called"* and
`:467-469` recording that the **arity change was deliberate — "it makes every un-migrated call site
a COMPILE ERROR rather than a silent pass-through."** Stronger than "by type": the old
`(containerId, files, onProgress)` shape cannot compile. AC-1 verified met across five wizards.

The POML was **born stale**: the second `SecureProjectSection` was deleted `144ef43c4` on
**2026-09-01**; 093 was filed **2026-09-03**.

⚠️ **A correction to my own first reading.** I initially took `EntityCreationService.ts:508-513`
(*"exactly three such flows … reordering them is task 093, after which this method should lose
callers"*) as live 093 scope that both audits missed. A repo-wide grep says otherwise: there are
**exactly TWO** callers — `createXrmEmailComposeHandlers.ts:262` and
`CreateAnalysisWizardWidget.tsx:783` — the third (DocumentUploadWizard "skip associate") was **cut
over by task 076**, and `notes/task-076-client-cutover-and-supplier-classification.md:54-55`
classifies both survivors as **"parentless"**, i.e. legitimately record-less. Two of three sources
say they are correct as they stand; the one that disagrees also cannot count them. **So the comment
is the stale artifact, not the code**, and 093 closes. What remains is a genuine open question for
the owner, not for me: see **D-8**.

---

## 7. The risk I most want on the record

Task 106's lesson generalizes to this plan. Build, 51/51 tests and 4/4 perturbations **all passed
over a real privilege-loss defect**, because every one of those instruments was single-request and
the defect lived *between two requests*. Both Step 9.5 gates found it by **reading**.

Three items queued here are of exactly that shape — **112** (a stale snapshot across two writers),
**109** (a fault indistinguishable from an empty result), **110** (one query, two consumers with
inverted fail directions). A green suite is not evidence for any of them. Each needs a test that
spans the dimension the defect lives in, and none of the three can be validated by a
single-request test no matter how many are added.

---

## 8. New findings from this session's verification (not previously tracked)

All four were found by checking a claim against the artifact it described, and all four are of the
project's signature class: **a confident statement that stopped anyone looking.**

### 8.1 🔴 ISS-026 (new) — a DEACTIVATED organization still confers access

Neither query on the org path ever consults `sprk_organization.statecode`:

- `BuildOrganizationGrantFilter` (`ExternalParticipationService.cs:68-72`) —
  `({orgs}) and _sprk_contact_value eq null and statecode eq 0 and {ExpiryPredicate(today)}`.
  That `statecode` is the **grant row's**.
- `QueryActiveOrgIdsAsync` (`:1092-1094`) — `statecode eq 0` is the **junction row's**.

So deactivating a firm revokes nothing: its grants stay active, its memberships stay active, and
every member keeps inherited access. Deactivation is the obvious operator gesture for "this firm is
no longer engaged", and it is a silent no-op. **Adjacent to ISS-020 but strictly separate** — ISS-020
is about an ended *membership*, this is about an ended *organization*. Per the standing rule, needs
filing as a GitHub issue **and** queuing in-project.

### 8.2 🔴 A false justification inside a GUARD TEST

`ExternalAccessQueryIntegrityGuardTests.cs:54-57` states the junction query *"rightly has no expiry
predicate, **because a membership row has no expiry**. Expiry is a property of a GRANT."* The
junction carries **`sprk_enddate`** (live-verified, task 020). **Precisely**: the rule itself is
correct and does not malfunction — keying on the entity set rather than the column is right, and it
would not trip. Only its *stated reason* is false. But it is a standing argument, sitting in the test
suite, that the very query ISS-020 wants bounded is *rightly* unbounded. Fix with 109/110.

### 8.3 A false mechanism claim in the query's own doc comment

`ExternalParticipationService.cs:1081-1082`: *"`statecode eq 0` = active membership; a former member
is a deactivated row and is excluded, **so leaving a firm drops inherited access**." * False in
exactly the ISS-020 way — a member whose `sprk_enddate` has passed but whose row is still active has
left the firm in every business sense and is **not** dropped.

### 8.4 A completed task's record does not match the code

TASK-INDEX (Phase 0, task 020 note) states 020 *"deletes the caveat"* — the *"confirm against the
created junction schema"* caveat in `QueryActiveOrgIdsAsync`. **It is still there**, at
`:1083-1085`, even though `notes/task-020-org-grant-spe-cleanup.md:38` records the schema as
live-confirmed. Small, but it is the *completion record* that is wrong, which is the direction the
2026-09-03 drift audit was built to catch and cannot see (it compares statuses, not claims).

### 8.5b 🔴 ISS-027 (new) — the To Do wizard silently DISCARDS uploaded files

`CreateTodoWizard/TodoWizardDialog.tsx:178` shows a files step promising *"Upload documents to
associate with this to do"*. Its `onFinish` (`:236-305`) reads `context.association`,
`selectedActions` and `followOn` — and **never `context.uploadedFiles`**. A grep of the whole
`CreateTodoWizard` directory for `uploadedFiles|uploadFilesToSpe|createDocumentRecords` returns
**no matches**, and the generic shell does not upload either: `CreateRecordWizard.tsx` holds the file
state and hands it to `onFinish` (`:612`) but calls no upload method anywhere. The user sees
**"To Do created!"** with no warning; the files are gone. `resolveSpeContainerId` also falls back to
`() => Promise.resolve('')` (`:224`) — vestigial. User-facing and silent; needs filing **and**
queuing.

### 8.5c 🔴 ISS-028 (new) — a 409 that says "did not take effect" has already written the level

`GrantExternalAccessEndpoint.cs` writes `sprk_accesslevel` at **`:260`**, and the ADR-003 conferral
check that returns the 409 sits at **`:306-319`** — *after* it. So a re-grant over an expired row
with **no new expiry but a different level** writes the level, then tells the caller *"it still
confers no access. Re-send with an expiryDate to restore it."*

Precise scope: it bites **only** when expiry is absent **and** level differs — `:281-282` correctly
notes a supplied expiry resolves first. It is **pre-existing**, not introduced by task 106. It is
unpinned because the existing characterization test
(`GrantLifecycleCharacterizationTests.cs:812-825`) sends the **same** level, so `levelChanged` is
false and the write is skipped. **Sharpest detail**: task 106's own comment (`:296-302`) cites *"the
ADR-003 ordering below"* as **load-bearing and true** — and it is, for *expiry*. It is wrong for
*level*. The comment defends the ordering that conceals the defect. Fix belongs with task **113**.

### 8.5d Stale-artifact inventory (all verified, all cheap)

| Artifact | Defect |
|---|---|
| `EntityCreationService.ts:5-9` | "Responsibilities" still reads **upload → create → link**, 20 lines above an example (`:27-31`) that says the opposite |
| `EntityCreationService.ts:508-513` | Says *"exactly three such flows"* — there are **two**; and assigns them to 093 as debt where 076 classified them legitimate |
| `ExternalAccessQueryIntegrityGuardTests.cs:54-57` | False justification, in a guard test (§8.2) |
| `ExternalParticipationService.cs:1081-1082` | False mechanism claim (§8.3) |
| `ExternalParticipationService.cs:1083-1085` | Caveat TASK-INDEX says task 020 deleted (§8.4) |
| Three "is task 093" comments | `EntityCreationService.ts:510-513`, `CreateAnalysisWizardWidget.tsx:772`, `createXrmEmailComposeHandlers.ts:247` |

### 8.5e Citation drift on the same line, in three places

The register cites the junction query at `:1067-1069`; the guard test cites `:1068`; the query is at
`:1092-1094`, and `:1067-1069` is now inside a **doc comment**. A shared wrong ancestor. Harmless
today, and exactly how a future reader "verifies" a claim against the wrong lines.
