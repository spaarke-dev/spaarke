# Owner decision brief — 2026-09-18

> Companion to [`remediation-and-sequencing-plan-2026-09-18.md`](remediation-and-sequencing-plan-2026-09-18.md).
> Nine decisions, each self-contained: the question, what it blocks, the **verified** evidence, the
> options with real consequences, a recommendation, and what would change that recommendation.
>
> **Every file:line below was read this session.** Where a prior artifact (register, audit, agent
> memo) disagreed with the file, the file won and the disagreement is named. Where something could
> not be determined, it says so rather than guessing — the `dataverse` MCP server failed to connect
> (`CONNECTION_CLOSED`), so nothing requiring live metadata was resolvable.

---

## ✅ Outcomes — owner decisions received 2026-09-18/19

| # | Decision | Consequence |
|---|---|---|
| **D-1** | 🔴 **"We do not use Dataverse plugins."** Then, 2026-09-19: **"follow recommendation; for C if this is needed for the best solution then do not defer"** | ✅ **DECIDED: A + B, and C is IN SCOPE — not deferred.** Plugins are OFF the table repo-wide; no ADR-002 exception is sought. **A** — invert the read default so a null `sprk_expiresdate` confers **NOTHING** (drop the `eq null` branch from `ExpiryPredicate`, `ExternalParticipationService.cs:99-100`). **B** — one **scheduled reconciliation job** on ADR-036, reusing task 103's lease + slot guard. **C** — remove `Create` from human roles **and** re-point `TrackingFieldTrio:43`'s UI gate off `hasEntityPrivilege(…Create…)` onto the server's real rule (Write-on-record, task 008's delegation). Task **107 is RESCOPED** off every Dataverse-side mechanism. |
| **D-2** | **"If an external user is removed from an organization then that external user must be reassigned access."** Then, 2026-09-19: **"follow recommendation"** | ✅ **FULLY DECIDED.** **Part 1 = YES** — bound the **additive** path; a date-ended membership stops conferring, and access must be re-granted explicitly. **Part 2 = NO** — an org-keyed **ethical wall KEEPS binding a former member**; the veto subject stays on `statecode` alone and deliberately over-matches (FR-23 `spec.md:86`; `AccessibleRecordSetService.cs:550-554`). **Part 3** — count and list date-ended-but-active rows **before** deploy; shipping part 1 removes live access. **Part 4** — the deactivation writer is **D-1 option B**, designed once and shared. Implementation: **ONE read** projecting `sprk_enddate`, yielding `ConferringOrganizationIds` (date-bounded) and `WallSubjectOrganizationIds` (statecode-active) — preserving task 043's single-snapshot invariant. **109 + 110 ship as one change.** |
| **D-3** | ✅ **Approve** | 044 and 065 move out from behind owner-blocked 036 into the immediate runnable wave. |
| **D-4** | ✅ **Yes** — a workforce caller MAY PATCH a to-do parented to their own service request | 054 rescopes to "add the requester read" **and** its `<goal>` is rewritten off the retired fourth-root premise. Unblocks 055/056/057/058. |
| **D-5** | ✅ **#1 before merge; #2/#3 after** | 063's real-Dataverse DOWNGRADE check gates the merge of #950. Task 100's `appnotification` write and H9's ARM slot guard become **recorded verification debt** on the PR — recorded, not dropped. |
| **D-6** | ✅ **Yes** | ISS-025 queued as **task 116**. |
| **D-7** | ✅ **Option 1** — pin what exists, no contract change | `AccessLevel` stays non-nullable. Tests pin that `set-record-share-expiry` writes no level. **Task 099 gains the level constraint** (non-negotiable). |
| **D-8** | ✅ **Follow recommendation** | **093 CLOSES** as delivered; adopt task 076's "parentless" reading and delete the stale comment; residue re-homed (047 gains the live container assertion; matter provisioning gets its own task). **Unblocks 047.** |
| **D-9 / ISS-026** | ✅ **"Need to check statecode and deactivate if org is inactive."** | Both a **read guard** and a **write action**. The write half is the same mechanism D-1 option B and ISS-020 part 3 need — see §D-2 Addendum part 4. |
| **D-9 / ISS-027** | ✅ **"Fix; To Do needs files."** | The To Do wizard must actually upload and link its files — not merely stop promising to. Own task. |
| **D-9 / ISS-028** | ✅ **"Fix."** | Reorder the level write behind the ADR-003 conferral check, in task **113**. |
| **034** | ✅ **Option A** | Scheduled nightly canary + a required manual gate. **No Dataverse secrets in CI.** Unblocks 036 once implemented. |
| **NOTE** | **"The ISS need to be addressed in this project not deferred."** Clarified 2026-09-19: **DO NOT pull back** the nine previously handed off as immaterial or another project's confirmed subject | So the directive binds every **in-project** entry — including ISS-026/027/028 — and leaves ISS-001, 006, 007, 011, 012, 014, 015, 016, 017 handed off. The disposition rule stands unchanged: out only when **immaterial** or **another project's confirmed subject**. |

### ✅ Nothing is blocked — all thirteen decisions received (2026-09-18 / 2026-09-19)

**One mechanism now serves four problems.** D-1 option B, ISS-026's write half, ISS-020 part 4, and
the rescoped task 107 all need the *same* thing: a **scheduled reconciliation job** on the ADR-036
`IScheduledJob` infrastructure task 103 hardened (distributed lease, staging-slot guard,
`AddScheduledJob<TJob>`), with `GrantExpiryReminderJob` as the working precedent — it already queries
these rows daily. **Design it once.** That is why the five new POMLs were held until D-1: authoring
them separately would have produced three overlapping mechanisms.

**Sequencing consequence of "C not deferred":** C has a hard prerequisite. Revoking `Create` from
human roles while `TrackingFieldTrio:43` still gates the grant button on
`hasEntityPrivilege('sprk_externalrecordaccess', Create, Global)` would **hide Manage Access for
every user** while the BFF's app-only write kept working. So the UI gate must be re-pointed at the
server's actual rule (**Write-on-record**, task 008's delegation) **before** the role change — and
that ordering is itself the fix for a real defect: the UI and the server currently ask *different
questions* about the same action, and the UI's check is **fail-open** when the API is unavailable.

---

## D-1 — ADR-002 plugin exception for task 107

### The question

Approve a **project-scoped (CLAUDE.md §6.5 path A) exception** to ADR-002, permitting a
pre-operation Dataverse plugin that stamps a default `sprk_expiresdate` on `sprk_externalrecordaccess`
rows created outside the BFF?

### What it blocks

Task **107** entirely, and task **101** (the "External shares by expiration" views) which depends on
107 enforcing that no undated grant exists.

### 🟢 The framing I got wrong last session — this is NOT an ADR conflict

I previously presented this as *"the first ADR-002 exception ever granted in this repo,"* which made
it sound like challenging the ADR. Reading ADR-002 properly changes that, in your favour.

`.claude/adr/ADR-002-thin-plugins.md` carries a **"⚠️ Restricted (Exception-Only)"** clause:

> Plugins **MAY** be used *only* when ALL of the following are true:
> - Execution is synchronous and deterministic
> - Work completes in < 50 ms p95
> - Logic is limited to: validation, invariant enforcement, denormalization/projection, audit stamping
> - No external calls of any kind
> - No orchestration or branching logic
>
> **Use of plugins requires explicit ADR exception approval.**

Plus: **MUST** keep < 200 LoC and < 50 ms p95.

**Task 107 fits every clause.** Stamping `today + 90` in a pre-operation step is synchronous,
deterministic, external-call-free, branch-free, a handful of lines, and squarely "invariant
enforcement". So the ADR is **not being challenged** — its own documented exception path is being
invoked for the first time. What is needed is the approval **ADR-002 itself requires**.

### Why every alternative fails (each verified, not asserted)

| Mechanism | Why it cannot work |
|---|---|
| **Entity-scope business rule** | Business rules *"are run on clients when a form is opened… they aren't executed inside Dataverse"*, and date arithmetic (`DateAdd`) belongs to **formula columns**, not a Set-Field-Value action. It can neither compute today+90 nor see a Web API create. **Covers ZERO API write paths.** |
| **`ApplicationRequired` alone** | *"Dataverse doesn't return an error when a column with `ApplicationRequired` applied doesn't have a value."* Only `SystemRequired` is enforced, and **custom columns cannot use it**. |
| **Dataverse webhook → BFF** | **Out-of-transaction.** The row is live-and-unbounded for the gap, and task 007's read filter treats an **undated** row as **never-expiring** — so the gap is not "briefly wrong", it is "permanently open until something else fixes it". Precedent exists (`WebhookSignatureFilter`), so it was rejected on merit, not availability. |

### Two traps in 107 as written

1. **Its reference implementation must not be copied.** The POML points at `BaseProxyPlugin.cs`,
   which is `[Obsolete("Violates ADR-002: makes HTTP calls, uses Thread.Sleep…")]`.
   `src/dataverse/plugins/` holds **three `.cs` files total**, all part of one HTTP-proxying Custom
   API. **There is no thin pre-operation plugin anywhere in this repo to copy.**
2. **Its test plan cannot build.** The plugin project is **net462**. `ci-tier1-blocking.yml` states
   the plugin is *"deliberately NOT in `Spaarke.sln`… If a net4x project is ever ADDED to the
   solution, this job will fail on ubuntu."* So `<output type="test">tests/ (KEEP path)</output>` is
   unbuildable as written.

### Cost of declining

Externally-created grant rows stay undated — and undated means **unbounded *and* unmonitored**. Task
100's reminder job selects only grants expiring from today through +30 days, so an undated row is
invisible to reminders as well as non-expiring. That is the cost-of-doing-nothing for the §11
justification.

### Options

| # | Option | Consequence |
|---|---|---|
| **1** | **Approve path A, mechanisms ADDITIVE** — `ApplicationRequired` for the MDA form/quick-create path **and** the plugin for API/flow/import paths | Closes 107 on every write path. Requires choosing the test topology (below). |
| 2 | **Decline** | Close 107. Accept undated externally-created rows permanently; task **101**'s views must then be specified to surface undated rows rather than assume none exist. |
| 3 | **Approve, plugin only** (no `ApplicationRequired`) | Form UX stays silently optional; a maker creating a row on a form gets no prompt. |

**If option 1**, also pick the test topology: **(a)** factor the date computation into a
**netstandard2.0** assembly the net10 tests can reference (then `tests/unit/domain/**` is a
legitimate KEEP path) — *recommended*; or **(b)** state explicitly that the plugin test project
stays **out of the solution** and runs manually.

### ✅ Recommendation

**Option 1 + topology (a).** It is the ADR's own sanctioned path, the alternatives are each provably
insufficient, and (a) keeps the logic testable inside the normal build.

### What would change this

Live metadata showing `sprk_expiresdate` is **already** `SystemRequired` would make the plugin
unnecessary. **Undeterminable this session** (MCP down), and the `Required = No` claim rests on a doc
whose adjacent sentence is demonstrably stale — `entity-schema.md:51` contains the self-nullifying
*"the live field is `sprk_expiresdate`, NOT the originally documented `sprk_expiresdate`"* (identical
names). **107's Step 0 must re-read live metadata regardless of this decision.**

---

## D-2 — ISS-020 / #999: does a date-ended org membership still confer access?

### The question — three parts

1. Should the **additive** path stop conferring once `sprk_contactorganization.sprk_enddate` has passed?
2. Should the **deny-veto subject** also be date-bounded?
3. Should existing date-ended junction rows be **backfilled/deactivated**?

### What it blocks

Tasks **109 + 110** — which are **one change**, not two (see below).

### 🟢 The dilemma dissolves — it was an artifact of the projection

The recorded framing was *"one query serves an additive term (over-inclusion = over-GRANT) and the
deny-veto subject (over-inclusion = a stricter wall), so one filter shape cannot be right for both."*

That is true **only because the query projects bare ids**:

```
ExternalParticipationService.cs:1092-1094
  $filter=_sprk_contact_value eq {id} and statecode eq 0
  &$select=_sprk_organization_value
```

Task 043 deliberately hoisted **ONE** junction read so the additive term and the veto subject share a
snapshot — a good invariant, worth keeping. **But it is one READ, not one FILTER.** Add
`sprk_enddate` to the `$select` and one read serves both consumers with *different in-memory
predicates*. No tradeoff survives, and the single-snapshot invariant is preserved.

**Consequence for sequencing**: ISS-019's suggested fix was a *second* query
(`QueryActiveOrgIdsOutcomeAsync`) for the veto path. That would **re-introduce the two-snapshot
hazard task 043 removed.** So **109 and 110 must ship as one change** — one read, projecting what
both consumers need, returning an outcome plus two named sets. 109 therefore inherits this gate.

### 🔴 A correction: my "exposure is nil" claim was FALSE

It appeared in the session-15 handoff and this plan's first draft. Verified:

- It covers only the org-**expansion** term, which **is** standing-gated (`:1209-1221`).
- The older org-**grant** term is **not**: `QueryOrganizationGrantRowsAsync` (`:1011-1051`) gates on
  active + unexpired *grant rows* only. **There is no `sprk_standinggrant` check on that path.**
- Live org grants **existed**: `notes/task-020-org-grant-spe-cleanup.md:18`, checked against live
  Dataverse — *"active org grants exist for `Morrison Foerster LLP`, which has an active member."*

So real exposure is bounded only by **dev being a test environment**, not by any control.

### This is a contract CHANGE, not a deviation

The junction's original design contract (sibling project `SPA-external-access-platform-r2`,
`notes/task-073-org-grant-design.md:83, 90-92`) made **`statecode` the sole active/former gate** and
never listed `sprk_enddate` as access-bearing. So ISS-020 asks you to *change* the contract, not to
correct a departure from it. That is precisely why it is yours and not an implementer's.

### The policy question, stated plainly

**Does an org-keyed ethical wall still bind a person whose membership has ended?**

- If **YES** (it should still bind them): keep the veto on `statecode` alone, so the wall
  over-matches. Documented support: FR-23 *"denied … even holding Full Access"* (`spec.md:86`), and
  `AccessibleRecordSetService.cs:550-554` *"Denial deliberately OVER-matches."*
- If **NO** (a former member should fall outside the wall too): bound everything and backfill.

### Options

| # | Option | Effect |
|---|---|---|
| **(a)** | **Bound additive YES · veto NO-CHANGE · no backfill** | Stops the over-grant; wall keeps over-matching (fail-closed). Implemented as ONE read, two named sets: `ConferringOrganizationIds` (date-bounded, mirroring `ExpiryPredicate`'s `ge` + null-never-ends at `:99-100`) and `WallSubjectOrganizationIds` (statecode-active). |
| (b) | Bound everything + backfill | Correct **if** you rule a wall does not bind former members. No tradeoff ever existed under this reading. |
| (c) | Leave as-is; treat `sprk_enddate` as informational | Control it in MDA process instead (deactivate the row on end). Also answers register C-5, "who deactivates?" — currently open, and **nothing in the repo writes the junction**; rows are maker-authored. |

### ✅ Recommendation

**(a).** It closes both fail directions, keeps 043's single-snapshot invariant and the NFR-02
one-read budget, and leaves the veto in its documented over-matching posture. **No existing rule
decides part 1 — that is genuinely yours.**

### What would change this

- You rule an org wall does **not** bind former members → **(b)**.
- You declare `sprk_enddate` a *planned* end date rather than an effective one → **(c)**.
- `sprk_enddate` turns out to be `DateTime`/UserLocal-behaved rather than Date-Only → a read-time
  comparison is wrong, and this belongs in a Dataverse-side deactivation instead.

### ⚠️ Undeterminable (MCP down)

Whether any date-ended-but-active rows exist **right now**; `sprk_enddate`'s exact behaviour;
whether `sprk_startdate` exists at all. **Adjacent gap found while verifying** → see **D-9 / ISS-026**.

---

## D-3 — Resequencing: pull tasks 044 and 065 forward

### The question

Approve moving **044** and **065** out from behind owner-blocked **036**, into the immediate runnable
wave?

### Evidence

| Task | Finding |
|---|---|
| **044** (unified-evaluator seam suite) | Deps **039, 041, 042, 043 are all ✅**. The index itself notes 044 *"excludes FR-20 by design"* — and FR-20 is exactly what 034/036 own. So the thing it waits for is the thing it deliberately does not test. |
| **065** (`AccessGrantModal` "+ User" picker) | Only dep is **063**, which is ✅. FR-07's delegation gate shipped in task 008, so the privilege-escalation concern that originally gated it is closed. Pulling it forward unblocks **066 → 067 → 099** *and* **069**. **099's escalation has already fired, waiting on 065's M8.** |

### Cost

This reorders the dependency-ordered sequence you approved on **2026-09-15**. The topology is
unchanged — only the position of two tasks whose dependencies were already met.

### ✅ Recommendation

**Approve.** Both are mechanical consequences of their own `<deps>`; leaving them behind 036 means
owner-blocked work blocks work that does not depend on it.

---

## D-4 — ISS-003 / #964: the product question blocking task 054

### The question

Can a **workforce** caller PATCH a to-do parented to **their own** service request?

### What it blocks

**054**, and **055 / 056 / 057 / 058** sequenced behind it.

### Evidence

- Task **028**'s owner ruling (2026-09-09): service requests are **core** but **never externally
  grantable**; `CallerPrincipal` must **not** carry an accessible-service-request set. The grant
  table deliberately has no service-request lookup.
- **054's `<goal>` still asserts that set exists** — `054-accessible-root-set-generalization.poml:23`:
  *"CallerPrincipal exposes an accessible-service-request set filled by BOTH the workforce and CIAM
  strategies…"* — and its criteria `:87-89` inherit it. The amendment exists **only in `<notes>`**
  (`:98`), i.e. the *binding* elements still state the retired premise. This needs the §2 rewrite
  **regardless of your answer**.
- The genuine residual is narrower than the POML: a workforce caller needs a **Dataverse read**, not
  a set lookup. Service-request scoping already exists by a different mechanism — the
  `service-requests` external module scopes by *requester* (`sprk_requestedby == caller`) and returns
  empty for any non-workforce plane (shipped by the sibling project, 2026-08-10).

### Options

1. **Yes** — a requester may PATCH to-dos on their own SR → 054 rescopes to "add the requester read",
   small.
2. **No** — 054 rescopes to deleting the fourth-root premise only, and ISS-003 closes as
   working-as-intended.
3. **Defer** — 054 and its four dependants stay blocked.

### ✅ Recommendation

Answer 1 or 2 so the chain unblocks; either way **054's goal must be rewritten** (§2 wave 0). I have
no product basis to prefer 1 over 2 — it depends on whether a requester is meant to act on their own
request's to-dos.

---

## D-5 — The three pre-merge live-Dataverse checks

### The question

Which of these must be discharged **before** merging PR #950, and who runs them?

None can be done offline; the `dataverse` MCP server is down this session, so this is not effort.

| # | Check | Why it is not optional | Owner task |
|---|---|---|---|
| **1** | **063 DOWNGRADE.** Seed Full Access on a scratch record → `share-user` at View Only → read `accessrightsmask` → **assert exactly 1** | 🔴 The **only** evidence that `ModifyAccess` **replaces** rather than **adds**. The entire share write path assumes replacement. If it adds, the read-back refuses loudly while the user **silently keeps Write and Delete**. A create-and-read would confirm nothing — the undocumented case *is* the assumption. | 063 |
| 2 | Real-Dataverse smoke of task 100's reminder **`appnotification`** write | The MDA bell is the delivery mechanism and has never been exercised live. Task 100 must be live by **2026-11-10** (097's backfill set existing grants to 2026-12-10; first reminder fires 30 days out). | 100 |
| 3 | Real-**ARM** check of H9's scheduled-jobs **slot guard** (#987) | Guards "scheduled jobs dispatch exactly once" across slot swaps. Fixed in `6149edecf`; never verified against real ARM. | 103 |

### ⚠️ The environment lens (your ruling, 2026-09-17)

The connected system is **dev, and its records are test records**. That changes **urgency, not
validity** — each becomes materially riskier the moment a real customer environment is provisioned,
and #1 in particular underpins the whole share write path.

### ✅ Recommendation

**#1 before merge** — it is the one whose failure mode is silent privilege retention. #2 and #3 can
follow the merge **if** recorded as open verification debt on the PR, rather than dropped.

---

## D-6 — Queue ISS-025 as a task?

### The question

Queue **ISS-025 / #1004** (the drift check cannot see an index row with no POML) as a task?

### Why this is a decision at all — it is a gap in my own work

Your directive of 2026-09-18 was *"we need the issues tracked AND queue to be fixed in this project
not deferred."* I queued **109–114** for ISS-019…ISS-024 and **left ISS-025 out**. The register says
so plainly: *"No task yet."* Verified today — there is no `115`/`116` POML and no index row for one.

The irony is exact: **the one tracked-but-unqueued entry is the one about a check that cannot see
unqueued work.**

### The fix

Two or three lines in `scripts/check-task-status-drift.ps1`: after the POML loop, assert the row set
and POML set are **equal** and report unpaired **index** rows as drift with their ids. It already
guards the inverse case (POMLs present, zero index rows parsed); this is the mirror.

I performed that comparison manually today: **111 POMLs / 111 index rows, zero orphans in either
direction.** So there is no live drift — only a blind instrument.

### ✅ Recommendation

**Yes — queue as task 116** (115 reserved for the §2 accuracy-repair pass). Small, and it closes the
one hole in the instrument that guards all the others.

---

## D-7 — ISS-023: does `/grant` need a "keep the level" mode? (contract change)

### The question

Your rule — *"when access is renewed the user should have the same level as previous access period"* —
**already holds on the renewal route**. Does `/grant` also need a keep-level mode, at the cost of a
request-shape change?

### 🟢 The distinction already exists as two ROUTES

| Route | Semantics | Evidence |
|---|---|---|
| `POST …/set-record-share-expiry` | **RENEW** — writes **only** `sprk_expiresdate`, lapsed rows included | `SetRecordShareExpiryEndpoint.cs:213-216`, `:29` |
| `POST …/grant` | **SET** — level required | `GrantExternalAccessEndpoint.cs:248-255` |

So your rule holds by construction on the renewal path. **Nothing pins it** — that is the real gap.
Task **099**'s picker is already specified to call the renewal endpoint (`099-*.poml:39, :75`).

### On `/grant` it is not expressible without a contract change

Verified: `InviteExternalUserRequest.cs:29` is `int AccessLevel` — **non-nullable**, and deliberately
so: it sits among `string? FirstName`, `DateOnly? ExpiryDate`, `Guid? OrganizationId`, which **are**
nullable. `GrantAccessRequest.cs:22-25` documents `ExpiryDate` as *"Optional in the REQUEST"*;
`AccessLevel` (`:21`) carries no such clause. The enum has no zero member, so an omitted value binds
to `0` and is refused 400. Every shipped caller sends a level, and `AccessGrantModal` **forces** a
per-row pick with no default.

### Why "infer renewal from row state" is rejected

The tempting option — treat an expired survivor as a renewal — fails on an argument I would not have
found unaided:

- It is **clock-dependent**: the identical payload is a "set" at 23:59Z and a "renew" at 00:01Z.
- Reminders fire **30/14/7/3/1 days BEFORE** expiry (`spec.md:119`), so the *ordinary* renewal
  arrives on a **still-live** key → classified "set" → **ISS-023 goes unfixed in the common case.**

### Options

| # | Option | Contract | Downgrade preserved | Cost |
|---|---|---|---|---|
| **1** | **Pin what exists.** Tests asserting `set-record-share-expiry` writes no level; 099's picker sends none; constrain 099 | **unchanged** | yes | ~0 risk. Leaves `/grant` able to lower a level on re-grant — but **no in-repo caller does that** |
| 2 | **Make `AccessLevel` nullable.** Present = set exactly (downgrade preserved); omitted = keep the highest level conferring on the key's last conferring day | loosening, backward-compatible | yes | Request-shape change; `BuildGrantPayload` and the `Request()` test helper must change with it |

### ✅ Recommendation

**Option 1 now.** It satisfies the decision you actually gave. Option 2 widens the public API to
defend against a caller that does not exist — worth doing only if you want `/grant` itself to be
renewal-safe for future/external callers.

### ⚠️ Non-negotiable either way

**Task 099 must gain the constraint.** As specified its criteria never mention level, so it would
present a date picker while the server changed the level. Exact text: *the Expiration picker is the
only renewal control and MUST NOT change any level — a picker change calls only
`set-record-share-expiry` (test: zero `/grant` calls, body carries no `accessLevel`); after refresh
every Current Access level badge is unchanged; a `+ Contact`/`+ Organization` re-add of a principal
already listed is an explicit SET and MUST send the picked level.* Task **101** needs nothing — its
column list already includes access level (`101-*.poml:34`).

---

## D-8 — Close task 093, and settle one question it leaves behind

### The question

Close **093** as delivered, and rule on whether two record-less upload callers are debt or by design?

### Why 093 closes

Create-first is **enforced by the compiler**, not by convention:
`uploadFilesToSpe(entityLogicalName, recordId, files, onProgress?)`
(`EntityCreationService.ts:479-484`), `:475` *"must already exist when this is called"*, and
`:467-469` — *"Signature changed 2026-09-03 from `(containerId, files, onProgress)`. The arity change
is deliberate: it makes every un-migrated call site a **COMPILE ERROR** rather than a silent
pass-through."* Acceptance criterion 1 is met across all five uploading wizards.

The POML was **born stale**: the second `SecureProjectSection` was deleted `144ef43c4` on
**2026-09-01**; 093 was filed **2026-09-03**. Its escalation trigger 1 was unfireable from birth.

Closing 093 **unblocks 047** (live end-to-end provisioning validation). Only 047 and 090 depend on it.

### ⚠️ A correction to my own earlier reading

I first took `EntityCreationService.ts:508-513` — *"exactly three such flows… reordering them is task
093, after which this method should lose callers"* — as live scope both audits missed, and said it
overturned the verdict. **A repo-wide grep proves me wrong.** There are exactly **two** callers
(`createXrmEmailComposeHandlers.ts:262`, `CreateAnalysisWizardWidget.tsx:783`); the third
(DocumentUploadWizard "skip associate") was **cut over by task 076**. And
`notes/task-076-client-cutover-and-supplier-classification.md:54-55` classifies both survivors as
**"parentless"** — legitimately record-less. Two of three sources say they are correct as they stand,
and the one that disagrees also miscounts them. **The comment is the stale artifact, not the code.**

### The question to settle

`EntityCreationService.ts:508-513` says those two flows are debt to be reordered. Task 076's
classification says they are legitimately parentless. **Both cannot be right.**

- Confirm **076's** reading → delete the comment's claim; nothing else to do.
- Confirm **the comment's** reading → one new small task to reorder them (the Analysis wizard's own
  comment at `:22` says its `sprk_analysis` row *"does not exist yet when the bytes move"*, so this
  may not be achievable without a design change).

### Residue re-homing (no decision needed, listed for completeness)

| Item | Home |
|---|---|
| Wizard-level ordering jest test (no client-side test of `onFinish` order exists today) | small STANDARD task |
| Live "files land in the project's own container via the wizard" | **add to 047** |
| Secure **matter** provisioning (`provision-project` is project-only, `ProvisionProjectEndpoint.cs:79, :261`; owner-deferred) | own task |
| Comment hygiene (§8.5d of the plan) | task 115 |

### ⚠️ A second, separable question — AI pre-fill staging

`ProjectPreFillService.cs:294-317` writes AI pre-fill files to a `StagingContainerId`, **no cleanup
job exists anywhere** (`Workers/`, `Services/Jobs/` → 0 hits), and the staging POST fires on
Info-step mount *before* the IsSecure toggle renders. This is the **one real mechanical argument**
for "collect IsSecure before the Info step" — the thing 093's POML waves off. **Undeterminable**
whether `StagingContainerId` is even set in dev, or equals the shared default (config lives outside
the repo; MCP down). If unset, this is moot.

### ✅ Recommendation

**Close 093**, adopt 076's reading (delete the stale comment), re-home the residue as above, and
treat the staging question as its own small decision once `StagingContainerId` can be read.

---

## D-9 — Three NEW register entries found while verifying (file + queue?)

All three were found by checking a claim against the artifact it described, and **all three are
verified**. Under your standing rule — *issues stay in the project unless immaterial or confirmed as
another project's subject* — each needs a GitHub issue **and** a queued task.

### ISS-026 — a DEACTIVATED organization still confers access

Neither query on the org path ever consults `sprk_organization.statecode`:

- `BuildOrganizationGrantFilter` (`ExternalParticipationService.cs:68-72`) —
  `({orgs}) and _sprk_contact_value eq null and statecode eq 0 and {ExpiryPredicate(today)}`;
  that `statecode` is the **grant row's**.
- `QueryActiveOrgIdsAsync` (`:1092-1094`) — `statecode eq 0` is the **junction row's**.

So deactivating a firm revokes nothing: its grants stay active, its memberships stay active, every
member keeps inherited access. Deactivation is the obvious operator gesture for *"this firm is no
longer engaged"* and it is a **silent no-op**. Strictly separate from ISS-020 (ended *membership* vs
ended *organization*). **Severity: same class as ISS-020, arguably worse** — it is the gesture an
operator would most expect to work.

### ISS-027 — the To Do wizard silently DISCARDS uploaded files

`TodoWizardDialog.tsx:178` shows a files step promising *"Upload documents to associate with this to
do"*. Its `onFinish` (`:236-305`) reads `association`, `selectedActions`, `followOn` — and **never
`context.uploadedFiles`**. A grep of the entire `CreateTodoWizard` directory for
`uploadedFiles|uploadFilesToSpe|createDocumentRecords` returns **no matches**, and the generic shell
does not upload either (`CreateRecordWizard.tsx` holds the state, hands it to `onFinish` at `:612`,
calls no upload method). The user sees **"To Do created!"** with no warning; the files are gone.
`resolveSpeContainerId` also falls back to `() => Promise.resolve('')` (`:224`) — vestigial.
**User-facing and silent.**

### ISS-028 — a 409 saying "did not take effect" has already written the level

`GrantExternalAccessEndpoint.cs` writes `sprk_accesslevel` at **`:260`**; the ADR-003 conferral check
returning the 409 sits at **`:306-319`** — *after* it. A re-grant over an expired row with **no new
expiry but a different level** writes the level, then tells the caller *"it still confers no access.
Re-send with an expiryDate to restore it."*

Precise scope: bites **only** when expiry is absent **and** level differs (`:281-282` correctly notes
a supplied expiry resolves first). **Pre-existing**, not introduced by 106. Unpinned because the
existing characterization test (`GrantLifecycleCharacterizationTests.cs:812-825`) sends the **same**
level, so `levelChanged` is false and the write is skipped. **Sharpest detail**: task 106's own
comment (`:296-302`) cites *"the ADR-003 ordering below"* as **load-bearing and true** — and it is,
for *expiry*. It is wrong for *level*. The comment defends the ordering that conceals the defect.

### ✅ Recommendation

File all three. Queue **ISS-028 into task 113** (same endpoint, same commit, and 113 is already
opening that file). Queue **ISS-026 with 109/110** (same query family, same change). **ISS-027** is a
separate surface — its own small task, and it is the only one of the three a *user* can currently
notice.

---

## Also outstanding (not new, listed so nothing is lost)

| Item | State |
|---|---|
| **034** — option **(A)** scheduled nightly canary + required manual gate *(recommended)* vs **(B)** Dataverse secrets in CI | Blocks **036**. The mechanism is shipped and perturbation-proven; only the live run is blocked. |
| Owners needed for **#988** (Service Bus accepts SAS), **#971** (singleton `LastException`), **#993** (`DataverseWebApiClient` credential) | All three handed off as "not material to this project", none has a confirmed owner |
| Portfolio sync | Blocked: `gh auth refresh -s read:project,project` |
| **`/merge-to-master`** | A **separate, unmade decision**. Pushing ≠ merging. |
| `Router` on PR #950 | All 8 Tier 1 jobs **green**; `Router` pending on an advisory job **excluded from adjudication**. Probe once, by name, on the head SHA at the moment you decide to merge. |

---

## D-10 — `sprk_startdate`: does a not-yet-started membership confer access today?

> **Asked** 2026-09-21 (session 22). **Answered** the same day.
> **Owner's words**: *"membership with future start date confers as of the access date."*

### The answer

**NO — a membership whose `sprk_startdate` is in the FUTURE confers nothing yet.** Access begins **on**
the start date. This is D-2 part 1's bound applied at the other end of the same range.

### Why it was asked separately rather than inferred from D-2

D-2 part 1 ruled that a **date-ENDED** membership stops conferring. The symmetry to a not-yet-STARTED
one is obvious but it is **not** the same product statement: a future start date could plausibly be a
scheduling commitment (access waits for it) **or** purely informational (access is already intended and
the date is a record-keeping artifact). Inferring D-2 onto it would have been an implementer deciding a
product question. Task 109 carried an explicit escalation trigger for exactly this, and it fired.

### What was verified before asking (2026-09-21, at HEAD)

| Fact | Evidence |
|---|---|
| `sprk_startdate` EXISTS on `sprk_contactorganization`, **DATE ONLY** | live-verified by task 117 — `notes/task-117-reconcile-grant-and-membership-state.md:42` |
| The junction query bounds on `statecode` **ALONE** — no `sprk_enddate`, no `sprk_startdate` | `AccessibleRecordSetService.cs:478`, which says so in as many words |
| **ZERO** server-side consumers of `sprk_startdate` | all 8 repo hits are client-side fixtures / a Calendar field-picker option |

**Consequence at HEAD**: a `sprk_contactorganization` row with a future `sprk_startdate` and
`statecode=0` **confers access today**. This was the only live ACCESS GAP outstanding on the list.

### Where it is implemented — and where it deliberately is NOT

**Task 109** owns it: the junction read is the single place both date bounds belong, and 109 is already
rewriting that read as one snapshot. It is **not** a new task and **not** task 117's writer.

| Path | Bound? | Why |
|---|---|---|
| **Additive / conferring set** | ✅ `(sprk_startdate eq null or sprk_startdate le {today})` | D-10 |
| **FR-23 deny-veto subject** | ❌ `statecode` ONLY, both ends | Identical to D-2 part 2: the org-keyed ethical wall over-matches **by design**. Date-bounding a veto in either direction is a fail-OPEN change. |

**Two mechanics that are load-bearing** (both mirror `ExpiryPredicate`'s documented reasoning):

1. **The `eq null` branch is not optional.** OData `le` **excludes nulls**, and a membership with no
   start date is an ordinary current membership that must still confer. Drop the branch and every
   open-ended membership silently loses access.
2. **`le`, not `lt`** — the column is Date Only and access holds **on** the start date.

🔴 **Do NOT mirror task 107's inversion.** 107 inverted the null branch for a GRANT's
`sprk_expiresdate` so an undated grant confers nothing. That is correct *for a grant* and wrong here:
a grant with no expiry is unbounded; a membership with no start date is simply current. An executor
"being consistent across the date columns" would revoke every open-ended membership in the system.

### Deploy consequence — this REMOVES live access

Folded into 109's Step 1 before-state, which now counts and lists **three** categories, all removals:
date-ended-but-active rows · **future-start-but-active rows (this decision)** · active grants under an
inactive organization. Same discipline D-2 part 3 required: count and list **before** anything ships.

### Artifacts updated

- `tasks/109-…poml` — escalation trigger **retired as answered**; two D-10 constraints (additive bound,
  veto explicitly unbounded); four acceptance criteria incl. the null-start-date pin and a perturbation;
  steps 0/1/2/3 amended.
- `tasks/110-…poml` — a D-10 verification criterion, since 110 is where the date-bounding contract is
  checked end to end and the bound is now two-sided.
