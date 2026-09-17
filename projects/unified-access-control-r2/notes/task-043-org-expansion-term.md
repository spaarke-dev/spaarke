# Task 043 — org-expansion membership term + the registry filter on the systemuser path

> **Status: IN PROGRESS (2026-09-17).** FR-24 / FR-25 / FR-22, design §4.5 term 4. Deps 037 ✅, 041 ✅,
> 042 ✅. Rigor FULL. Production code builds clean (0 warnings / 0 errors); verification pending.

---

## 1. What shipped

| Change | Where |
|---|---|
| `MembershipResolveOptions.OrganizationIds` — org ids to bind into the contact-plane identity | `IMembershipResolverService.cs` |
| `ResolveByContactAsync` binds them, so registry-listed **org-typed** descriptors emit conditions | `MembershipResolverService.cs` |
| 🚨 `HashOptions` now covers `AccessConferringOnly` **and** `OrganizationIds` | `MembershipResolverService.cs` |
| `ActiveOrgMemberships` outcome + `ReadActiveOrgMembershipsAsync` — ONE junction read per composition | `AccessibleRecordSetService.cs` |
| Org-expansion term: one walk per **distinct** org baseline, Secure-suppressed | `AccessibleRecordSetService.cs` |
| `ComposeForSystemUserAsync` requests `AccessConferringOnly: true` | `AccessibleRecordSetService.cs` |
| `AccessibleRecordSetSources.OrgExpansionMembership` provenance flag | `AccessibleRecordSetService.cs` |

---

## 2. 🚨 The finding that changed the task: the cache key was narrower than the query

`HashOptions` hashed Roles, IdentityTypes, IncludeRelated, Limit, ContinuationToken — **not
`AccessConferringOnly`**. Task 041 added that flag and deliberately left it unset at every call site,
so the omission was harmless and invisible.

Task 043's step 3 sets it `true` in `ComposeForSystemUserAsync`. From that moment:

- the **authorization** caller (filtered, `true`) and
- the **scoping** caller (`Api/Membership/MembershipEndpoints`, unfiltered, `false`)

share one cache id — same user, same entity, same Limit ⇒ same
`{systemUserId}:{entityType}:{optionsHash}` — for the 5-minute TTL. Whichever call arrives first
decides what the other sees, and the dangerous order is the likely one: a scoping call landing first
hands the authorization gate the **unfiltered descriptor set**. That is register A-8's over-inclusion
— the exact defect FR-24 exists to close — reintroduced through Redis rather than through code.

**So the hash fix is a prerequisite of step 3, not an adjacent tidy-up.** `OrganizationIds` carries the
same hazard in the new option: the evaluator issues one walk per distinct baseline, so a single contact
legitimately makes several contact-plane calls differing *only* by which orgs are bound; omitting it
would serve one baseline's row set as another's.

Pinned by two tests that fail on a regression:
`ResolveAsync_AccessConferringOnly_DoesNotShareACacheEntryWithTheUnfilteredCall` (runs the calls in the
dangerous order) and `ResolveByContactAsync_DifferentOrganizationIds_DoNotShareACacheEntry`.

**General rule now stated on the method**: an option that can change the returned rows MUST appear in
the hash. On an authorization path a too-narrow cache key is not a performance detail — it is a way to
answer the wrong question quietly.

---

## 3. Design decisions (mine, not escalations)

### 3.1 The evaluator resolves the org ids; the resolver receives them

The POML's step 1 says the resolver should "populate the identity's OrganizationIds from the contact's
active junction rows". Taken literally that makes `Services/Ai/Membership` depend on
`Infrastructure/ExternalAccess` (ADR-034 M1 keeps the binding *inside* the canonical resolver, but the
junction read is not the resolver's data), and it adds a **second, separately-cached** junction read
that could disagree with the first mid-composition.

The evaluator already performs that read for the FR-23 deny veto. Hoisting it above the membership walk
costs **zero** extra reads and makes the additive term and the ethical wall provably use the same
snapshot. `<steps mode="directional">` permits the adaptation; recorded here as the deviation.

**The pre-existing `IIdentityOrganizationResolver` seam is not a substitute** — its implementation
resolves via `MembershipOptions.OrganizationLookup.UserLookupField`
(`sprk_organization` → `systemuser`, default empty), a different mechanism that returns nothing for a
contact.

### 3.2 One outcome, two opposite fail directions

One junction read feeds two consumers whose safe failure directions are **inverted**:

| Consumer | Over-inclusion is | So a failed read must |
|---|---|---|
| additive org-expansion term | an over-**grant** | contribute nothing |
| FR-23 deny-veto **subject** | a stricter wall | deny every queried candidate |

`ResolveDenyVetoAsync` has always denied everything on this fault. Hoisting the read and passing a bare
empty list would have **silently converted that fail-closed into a fail-open**, because an empty
subject-org list is indistinguishable from "belongs to no organization" — the wall would simply stop
matching. Hence `ActiveOrgMemberships(OrganizationIds, Unreadable)` rather than `IReadOnlyList<Guid>`.

### 3.3 One walk per DISTINCT baseline

The term must credit each record at **its own** organization's baseline. Two alternatives, both wrong:

- **One walk per organization** — N queries for information that varies only by baseline, and FR-25
  defines exactly three baselines, so N collapses to ≤3.
- **One walk binding every organization** — can credit only a single level, so a record reachable only
  through a View Only firm silently inherits an unrelated Full Access firm's rights. **Undetectable
  downstream**: the record ids are identical either way and only the level differs.

Rejected a third option — reporting matched-org provenance on `MembershipResponse` — because a new
response field changes the wire shape and would trip the POML's own escalation trigger 2 (M10
byte-compatibility for existing membership API consumers).

Pinned by `ComposeAsync_TwoOrgsAtDifferentBaselines_CreditsEachRecordAtItsOwnOrgsLevel`.

### 3.4 An org-only walk must narrow identity types

`ResolveByContactAsync` binds the `ContactId` **unconditionally**. A walk that bound org ids without
narrowing would return contact-derived records too — and the org term credits everything it receives at
the organization's baseline, so the contact's own assignment would inherit its firm's level. The walk
therefore passes `IdentityTypes: ["Organization"]`; `FilterDescriptors` already supports it, so no new
machinery. Pinned at the FetchXml level by
`ResolveByContactAsync_OrganizationIdentityTypeOnly_ExcludesTheAlwaysBoundContactCondition`.

### 3.5 Org expansion is a contact-plane term

Design §5 composes a systemuser as ADR-034 membership ∪ the caller's own contact grants. The org-derived
access a Type 1 user reaches through their linked contact is the org-**inherited grant**, which term 2
already suppresses on a secure record via `DirectAccessLevel` (task 037). Adding a second org path on
that plane would invent access design §5 does not grant. Recorded as two `Times.Never` assertions in
`ComposeAsync_SecureRecord_Type1SystemUserGetsNoOrgInheritedAccessAndNoOrgExpansionWalk` rather than as a
comment.

### 3.6 Standing-gated, provisionally

Register B-1 says derived access is "default-on" with Secure as the veto, but **no FR assigns a level to
a non-standing derived contribution**. The POML says to encode, not decide (escalation trigger 1), so an
organization with the flag unset, no baseline, or an unreadable row contributes nothing —
`StandingGrantState.Rights` is `None` in all three cases, which is also task 042's fail-closed value.

**Escalation trigger 1 did NOT fire**: no non-standing derived term exists today, because the
contact-plane walk only runs when `standingRights != None`.

---

## 4. 🔔 The two task-020 constraints — decided, as instructed, either way

### 4.1 `sprk_enddate` on the read path — NOT bounded, and this is now a recorded decision

The junction carries `sprk_enddate`, but `QueryActiveOrgIdsAsync` filters `statecode eq 0` alone, so **a
membership ended by date but never deactivated still confers inherited access**. Task 020 filed the
asymmetry onto this task with the explicit instruction not to inherit the revoke's shape by default,
because the revoke's fail direction is inverted.

**Decision: leave the query shape unchanged, and record why — one shape cannot be right for both
callers.** `QueryActiveOrgIdsAsync` is shared by:

- the **additive** org-grant and org-expansion terms, where over-inclusion is an over-grant, so a date
  bound is a *tightening*; and
- the **deny-veto subject**, where over-inclusion is a stricter wall, so the same date bound makes the
  ethical wall match **fewer** subjects — a fail-OPEN change to a veto.

A blanket date bound would therefore fix one caller and quietly weaken the other. Doing it properly
means a date-bounded variant for the additive callers only, which also **changes who has access today**
(narrowing live org-grant inheritance) — beyond this task's scope and requiring the owner's word.

→ **Filed as an owner item and a register entry; not fixed here.** The cheap hedge in the meantime is
that the additive term is standing-gated, and **0 organizations hold a standing grant today** (task 042
§3), so the org-expansion term's exposure to this is currently nil.

### 4.2 Per-member cache invalidation — NOT added, deliberately

Task 020 declined it because its member list existed only when a `ContainerId` was supplied, making
invalidation fire on some org revokes and not others. That objection genuinely does not apply here — this
task derives the member list unconditionally.

**Decision: still do not add it**, for a different reason. The staleness window that matters is not the
junction read (uncached, live per composition) but the **membership resolver's own 5-minute response
cache** plus the 60-second participation cache. Invalidating one junction row would leave the resolver's
per-contact entries untouched, so the observable revocation lag would be unchanged while the code gained
a second invalidation contract to keep honest. ADR-034's Phase 2 pub/sub invalidator
(`Membership:CacheInvalidator:Enabled`, default off) is the mechanism that actually closes this, and it
is the right place for it.

→ Recorded so the decision is deliberate rather than forgotten.

---

## 5. POML premise errors — #18 and #19

| # | Claim | Reality |
|---|---|---|
| 18 | `<file role="canonical-reference">ContactStandingGrantReader.cs</file>` | Task 042 **renamed** it `SubjectStandingGrantReader.cs`; the cited file does not exist |
| 19 | Step 3: wire the flag into "the **flag-off branch of 036**" | **036 is `🔲 open`** (deps 032/**034**/035/**104**, and 034 is 🟡 blocked). `ComposeForSystemUserAsync` has no flag and no branches; its single `_membership.ResolveAsync` call **is** the path. 036 will add the impersonated answer alongside it |

Also stale: all three `<dependency status="pending">` attributes (037/041/042 are ✅), and the §10
publish-baseline note still quotes the retired ~44.96 MB convention.

**Running total: 19.** The code won again.

## 5.1 Docs-vs-reality drift fixed in passing

`IMembershipResolverService.ResolveByContactAsync`'s own XML doc still described the **retired
`sprk_assigned*` naming convention plus an exclusion list** as the allowlist mechanism. Task 041 deleted
that prefix check outright — it is not layered under the registry. A reader trusting the doc would have
concluded that naming a new column `sprk_assigned*` still confers access. Corrected in place, with the
correction dated and attributed so it reads as a repair rather than as fresh prose.

**Docs-vs-reality instances: twelve.**

---

## 6. Read accounting (NFR-02)

Per contact-plane composition:

| Read | Count | Note |
|---|---|---|
| grant set | 1 | cached 60 s, unchanged |
| `sprk_contactorganization` junction | **1** | was 1 (inside the veto); now hoisted and shared |
| org standing grant | 1 per active org | one per *subject*; the reader's contract is per-subject. 0 orgs today |
| membership walk — contact standing term | 1+ pages | unchanged, gated on the contact's own grant |
| membership walk — org expansion | 1+ pages per **distinct baseline** (≤3) | new; 0 today |
| root record flags | 1 | batched over every candidate incl. org-derived ids |

Net new reads when no organization holds a standing grant: **zero** beyond one baseline read per org.

---

## 7. What offline tests cannot cover

- **The `statecode eq 0` filter** (criterion 3's "inactive junction row confers nothing") lives inside
  `QueryActiveOrgIdsAsync`, which the test double replaces. Asserting it at evaluator level would assert
  the fake. Pinned where it lives; deliberately not restated.
- **`sprk_enddate`'s type/format** on the junction is unverified — the Dataverse MCP server failed to
  connect this session (`CONNECTION_CLOSED`). Relevant only if §4.1 is ever actioned.
- **No live check of the org-typed lookup metadata** for the same reason; the registry seed's org columns
  come from task 041's live pass (2026-09-04).

---

## 8. 🔴 A verification error I made, and how it was caught

I ran `dotnet test tests/integration/Sprk.Bff.Api.IntegrationTests/` with a filter naming
`StandingGrant`, `AccessibleRecordSet`, `Membership`, `ExternalScope`, `Delegation` and
`UnifiedAccessControl`, and got **"Passed! Failed: 0, Passed: 16"**. I nearly recorded that as the
affected-integration-suite figure.

It was the wrong 16. `tests/integration/auth/**` and `tests/integration/seam/**` are **not** compiled
into `Sprk.Bff.Api.IntegrationTests` at all — they are globbed into
`tests/unit/Sprk.Bff.Api.Tests.csproj` (lines 111 and 149, `LinkBase="AuthTests"` / `"SeamTests"`). The
16 that passed were `Sprk.Bff.Api.IntegrationTests.Membership.Phase2*`, an unrelated Phase-2 suite that
happened to match the word "Membership". `StandingGrantRuntimeUnionSeamTests` — the seam test task 042
recorded as breaking on *their* change to the same method I restructured — was never in the run.

**The mechanism: a filter that selects NOTHING reports identically to one that selects everything.**
Both print `Failed: 0`. This is the same error class as trusting a zero-failure count while checks are
still pending (`push-to-github` Step 8) and as session 13's orphaned `testhost` executing stale DLLs:
the observation was taken outside the thing being observed.

**Practice adopted for the rest of this task, and worth generalising:** before trusting a filtered test
run, prove the filter is non-empty — `--list-tests` with the same filter, and assert the count is > 0.
A test-count delta against `HEAD` serves the same purpose for added tests (used here: +14
`[Fact]`/`[Theory]`, matching 6 resolver + 9 evaluator − 1 inverted).

---

## 9. Verification

Commit `28f833a0e` (local; **not pushed**).

| Gate | Result | How it was made trustworthy |
|---|---|---|
| BFF build | **0 warnings / 0 errors** | — |
| The two edited classes | **108 / 108** | `[Fact]`/`[Theory]` delta vs `HEAD` = **+14** (evaluator 51→59, resolver 37→43), so the run provably covered the edited tree rather than stale DLLs |
| Affected unit namespaces (`Infrastructure.ExternalAccess` + `Services.Ai.Membership`) | **328 / 328** | Re-run after a **forced rebuild**, because a lint-staged pre-commit hook ran `dotnet format` over the five C# files and the first run had reused pre-format binaries (1 s duration gave it away) |
| Seam + auth surfaces (`Seam.ExternalAccess`, `Auth.UnifiedAccessControl`, …) | **197 / 197** | Filter proven non-empty first (**189** selected) after the §8 error; all three `StandingGrantRuntimeUnionSeamTests` methods confirmed present **by name** |
| ArchTests | **323 / 323** | Census unchanged from task 063's 323 — expected, since no route or DI registration was added |
| Perturbations | **11 / 11 CAUGHT** | 0 MISSED, 0 INVALID — but only after a matcher fix and four individual re-runs; see §10 |
| CVEs | **none** | `dotnet list package --vulnerable --include-transitive` on `Sprk.Bff.Api`; no package reference was added, so this is the expected result |
| Publish size | **45.45 MB vs master 45.35 MB = +0.10 MB** | Both sides measured from **fresh short-path worktrees** (`C:\wt043m` / `C:\wt043b`), master **re-measured** rather than compared to the recorded number, same zip tool both sides (`Compress-Archive -CompressionLevel Optimal`, matching `scripts/Deploy-BffApi.ps1`), **file counts EQUAL at 214/214**. 14.55 MB of headroom under the 60 MB NFR-01 ceiling. **043's own contribution is ≈0.00** — task 063 measured the same 45.45, and no `.csproj`/`.props` changed; the +0.10 is project-cumulative. See §12 for the false-green this measurement produced on its first attempt |
| Step 9.5 `adr-check` | **0 violations / 11 warnings** | Including an independent answer to the `CacheVersion` question — see §13 |
| Step 9.5 `code-review` | pending | — |

### 9.1 Doc drift repaired at the gate's prompting

`adr-check` found four documentation defects in the artifacts governing this task. All four are now
fixed (`.claude/**` is main-session-only per root CLAUDE.md §3, so a sub-agent could not have):

| # | Artifact | Was | Now |
|---|---|---|---|
| W2 | `.claude/adr/ADR-034` `Key Types` | `MembershipResolveOptions` missing **both** `AccessConferringOnly` (dropped by task 041) and `OrganizationIds` | both present, each dated and attributed |
| W3 | `.claude/adr/ADR-034` Identity Normalization Contract | one `Lookup → sprk_organization` row, describing only the `UserLookupField` mechanism — so a reader would conclude the **contact plane has no org path at all** | split into systemuser / **contact** rows; the contact row names the `sprk_contactorganization` junction, says the caller supplies it and why, and warns that `UserLookupField` is not a substitute |
| W4 | `.claude/adr/ADR-028` A2 cross-reference | "filtered to the access-conferring **`sprk_assigned*` role allowlist**" — a convention task 041 **deleted** | "the access-conferring column **registry**", with a dated note. **No ADR-028 rule changed** — a factual correction to a cross-reference describing ADR-034's mechanism |
| W7 | `design.md` §3 placement table | membership resolver = "**Reuse unchanged**" | "**Extend additively**", naming 041's gate and 043's binding + cache fix |

W2 and W4 are the same defect as the one this task already fixed in code (§5.1): **three artifacts
described the retired `sprk_assigned*` convention as live.** Docs-vs-reality instances now stand at
**fifteen**.

**Specifically re-verified because task 042 warned about it**: `StandingGrantRuntimeUnionSeamTests`
broke on 042's change to `ComposeForContactAsync`, and this task restructured the same method. All three
of its tests pass unchanged, including
`StandingGrant_WithNoBaseline_ConfersNoAccessEvenWhenTheFlagIsSet` — the assertion 042 added after
discovering its fixture mirrored the live state of both real standing-grant contacts.

**Housekeeping:** the pre-commit hook created and then removed its own stash (`72cf68afa`). The two
entries remaining on the shared stack (`stash@{0}` master pre-deploy, `stash@{1}` WIP on
`spaarke-ai-platform-unification-r2`) belong to other worktrees and were left untouched.

---

## 10. Perturbations — final: **11 / 11 CAUGHT** (first pass 6, re-run 5)

Every behaviour this task claims is pinned by a test that fails when the behaviour is removed. The
route there is the interesting part, and §10.1–§10.2 keep it rather than tidying it away: the first
pass was **CAUGHT 6 · MISSED 1 · INVALID 4**, and both of the numbers that were not CAUGHT turned out
to matter.

| Re-run (individually, on a cold build server) | Verdict |
|---|---|
| **P5** org term drops the `IdentityTypes` narrowing — *after* the matcher fix | **CAUGHT (4 tests)** |
| **P1** `HashOptions` drops `AccessConferringOnly` | **CAUGHT** |
| **P2** `HashOptions` drops `OrganizationIds` | **CAUGHT** |
| **P10** org expansion leaks onto the systemuser plane | **CAUGHT** |
| **P11** one walk for ALL orgs at the MAX baseline (the rejected alternative) | **CAUGHT** |

Two conclusions worth keeping:

- **P5's MISSED was a real defect in the tests, and only a perturbation could have found it.** The suite
  was green with the guard removed; it now fails 4 tests. Nothing about the passing suite before the fix
  distinguished "defends this behaviour" from "happens to pass".
- **All four INVALIDs became CAUGHT once re-run alone.** So they were lock/environment artifacts
  throughout, exactly as the harness's three-verdict design assumed — and had they been counted as
  either passes or failures, the conclusion would have been wrong in both directions. In particular P1
  and P2 cover §2's cache-key defect, the most consequential thing this task changed.

## 10.0 First pass, and why the two non-CAUGHT results were kept

| # | Break | Verdict |
|---|---|---|
| P3 | `ResolveByContactAsync` ignores the supplied org ids (pre-043 behaviour) | **CAUGHT** (4) |
| P4 | systemuser plane reverts to the UNFILTERED membership term (A-8 reopened) | **CAUGHT** (1) |
| P6 | org term drops Secure suppression (FR-22) | **CAUGHT** (1) |
| P7 | the two opposite fail directions collapse — a junction fault stops denying | **CAUGHT** (1) |
| P8 | org term gates on `Held` instead of `Rights != None` | **CAUGHT** (1) |
| P9 | provenance lies — `OrgExpansionMembership` set when no org contributed | **CAUGHT** (3) |
| **P5** | **org term drops the `IdentityTypes` narrowing** | 🔴 **MISSED** |
| P1 · P2 · P10 · P11 | (cache key ×2, systemuser leak, single-walk-at-max) | **INVALID** — did not build |

### 10.1 🔴 P5 MISSED — the test was decoration, and the fix is the matcher

Removing `identityTypes: OrganizationIdentityTypeOnly` from the evaluator's org walk left the suite
**green**. The behaviour is load-bearing (§3.4): without it the always-bound `ContactId` drags
contact-derived records into a walk whose results are credited at the ORGANISATION's baseline, so a
contact's own assignment silently inherits its firm's level — undetectable downstream, since the ids are
identical and only the level differs.

**Why nothing caught it**: `OrgWalkFor(orgId)` matched only on `OrganizationIds`, and a Moq setup is
indifferent to option fields it does not mention — so the stripped call still matched and still received
the canned response. The resolver-level test cannot cover it either: it passes `IdentityTypes` *itself*,
proving the resolver HONOURS the narrowing, never that the evaluator SENDS it.

**Fix**: tighten `OrgWalkFor` to require `IdentityTypes` contains `"Organization"`, rather than add a
test. One line, and it pins the narrowing across every org test at once — drop it in the evaluator and no
setup matches, so the org term contributes nothing and the tests fail. A perturbation that comes back
MISSED is the only thing that distinguishes a test which defends behaviour from one which merely passes.

### 10.2 The four INVALIDs are measurement failures, and not solely self-inflicted

`CS2012` — `Sprk.Bff.Api.dll` locked by **`VBCSCompiler`, under two different PIDs** (37460 on P2, 15416
on P11) — plus `CS2001` for a missing generated `Spaarke.Scheduling.GeneratedMSBuildEditorConfig`.

This project has recorded this failure mode three times before (session 13 P12 after eleven consecutive
runs; session 12 `CS0649`; session 11 `CS0006`) and attributed it to back-to-back builds. **This pass
adds a cause worth knowing: another agent is building the same solution concurrently on this machine.**
The orphaned 6.2 GB `testhost` was inspected via its command line **before** anything was done to it, and
it turned out to belong to `C:\code_files\spaarke\.claude\worktrees\agent-af442ccb79065ad1a` — a
different worktree — so it was **left running, not killed**. Checking first was the whole point: killing
it would have destroyed another agent's in-flight test run to speed up mine. `dotnet build-server
shutdown` therefore quiesces only THIS session's servers, and the contention is not fully removable.

Consequence for the method: the build environment is not fully controllable here, so each INVALID is
re-run **individually** on a cold build server, and a repeat INVALID is recorded as an environment limit
rather than promoted to a result. A broken perturbation is not evidence in either direction — and two of
these four (P1, P2) cover the cache-key defect of §2, which is the single most consequential thing this
task changed, so they are the last ones that may be left unresolved.

---

## 11. CVE check and status-drift baseline

- **CVEs (CLAUDE.md §10 rule 5)**: `dotnet list package --vulnerable --include-transitive` on
  `Sprk.Bff.Api` reports **no vulnerable packages**. This task added no package reference, so that is the
  expected result rather than a lucky one.
- **Status-drift baseline**: `scripts/check-task-status-drift.ps1` is **green before** the bookkeeping
  edit — 105 POMLs / 105 index rows, no drift. Taken deliberately in advance: if the check goes red after
  `ct043b.js` writes the POML status and the TASK-INDEX row together, the cause is this task's edit and
  not inherited drift.

## 12. 🔴 A false green from my own tooling — §8's lesson, restated the hard way

The first publish-size run reported **"completed (exit code 0)"** and measured **nothing**. The script
died on a PowerShell *parse* error before executing a single statement, and the apparent success came
from the shell pipeline: `powershell ... | tail -40` takes its exit status from `tail`, not from
PowerShell.

Two causes, both mine:

1. The `.ps1` was authored with em-dashes and arrows in its comments and strings, then invoked with
   **`powershell`** (Windows PowerShell 5.1, ANSI) instead of **`pwsh`** (PowerShell 7, UTF-8). The
   multi-byte characters were mangled — visible in the error output as
   `published ZERO files ?" measur...` — which broke a string literal and cascaded into brace and paren
   mismatches. A `pwsh` invocation in the very same message worked fine, which is what isolated it.
2. Piping to `tail` discarded the real exit code, so nothing downstream could distinguish "measured and
   passed" from "never started".

**Rules adopted**: scratchpad PowerShell stays **7-bit ASCII** and runs under `pwsh`; the interpreter's
own status is captured immediately (`rc=$?`) *before* any pipe. The rewritten script also fails loudly —
it checks `$LASTEXITCODE` after `dotnet publish`, refuses a zero-file publish, and `exit 1`s on any
failure — so a broken measurement can no longer masquerade as a clean one.

This is the **third distinct instance in this one task** of the same underlying error: **an observation
taken outside the thing being observed.** §8 (a filter that selected the wrong 16), §10.2 (perturbations
that never compiled), and this. They look unrelated and share a root. The defence is cheap and identical
every time: establish that the instrument registered something before believing what it reports —
`--list-tests` before trusting a filter, a build check before scoring a perturbation, the interpreter's
own exit code before trusting a script.

---

## 13. Step 9.5 — `adr-check`: **0 violations / 11 warnings**

Run read-only against `c3553730c..47a63314c` (no `dotnet`, so the ArchTests were named rather than
re-executed; the orchestrator's own run had them at 323/323).

### 13.1 The `CacheVersion` question — answered independently, and the answer is "no bump"

This was the one thing worth an outside opinion, because §2's fix **changed the options-hash
composition** and ADR-009 governs cache-key versioning. The reasoning that settles it:

Every post-change hash carries the new `|a:…|o:…` suffix, so **no running code can compute the key of
any pre-change entry** — old entries are unreachable and expire on their own 5-minute TTL. And the
hazardous direction specifically cannot occur: the authorization caller hashes `a:1`, a value no
pre-deploy instance ever produced, so a stale **unfiltered** entry can never be served to the gate —
including during a mixed-fleet rolling deploy, where old and new instances write to disjoint key spaces.

The contrast with the 3→4 bump is the useful part: there the key was **unchanged**, so old
silently-truncated entries stayed addressable and would have been served as valid answers; only the
version could orphan them. Here the changed composition *is* the orphaning mechanism. That distinction
is now written onto the `CacheVersion` doc-comment itself, since the next author is the one who will
hit it.

Residual, benign: every membership entry goes cold for up to 5 minutes at deploy. Worth a line in the
PR rather than a surprise in a latency chart.

### 13.2 W1 — the `Infrastructure → Services/Ai` dependency is COMPLIANT, not an exception

`bff-extensions` §A.4 bans CRUD code from injecting "AI-internal" types, and the evaluator consumes
`IMembershipResolverService` from `Services/Ai/Membership/`. The gate flagged it as a warning and named
the cheap deciding check: what does the ArchTest actually enforce?

**Checked, and it decides the question.** `ADR013_AiBoundaryTests.ForbiddenAiInternalTypes` is exactly
two entries — `Services.Ai.IOpenAiClient` and `Services.Ai.IPlaybookService`. `IMembershipResolverService`
is not among them, and `Sprk.Bff.Api.Services.Ai.` appears in the test's **grandfathered/allowed** prefix
list, not as a banned target. `LayerDependencyTests.cs` contains no `Services.Ai` reference at all.

So there is no rule — ArchTest or ADR — banning this dependency, and **ADR-034's own MUST mandates it**
("MUST use `MembershipResolverService` via `IMembershipResolverService` DI for any 'records this user is
associated with' query"). §A.4's "AI-internal" is operationalised by that enumerated list; the canonical
membership interface is merely *housed* under `Services/Ai/` for historical reasons.

**No §6.5 path is needed.** Recorded here so the next reviewer does not re-litigate it. The gate was
right to raise it — one file in the codebase (`Tier2ScopeFilterInjector.cs:32-35`) *does* decline this
dependency on principle, which is exactly the kind of inconsistency that deserves an answer rather than
a shrug.

### 13.3 Disposition of all eleven warnings

| # | Disposition |
|---|---|
| W1 | **Closed as compliant** — evidence in §13.2; no exception required |
| W2 · W3 · W4 · W7 | **Fixed** — see §9.1 (two ADR-034 passages, one ADR-028 cross-reference, one design.md row) |
| W5 publish size | **Discharged** — §9: 45.45 vs re-measured master 45.35 = +0.10 MB, 214 files each side |
| W6 CVE scan | **Discharged** — §11: no vulnerable packages |
| W8 hot-path element names | Pre-existing cosmetic drift in design.md's block; nothing is gated by it (`project-pipeline` hard-warns only on a *missing* block). Addressed while design.md was open |
| W9 trait tagging | **Declined, with reason.** §F's taxonomy is `repaired` / `real-bug-pending-fix` / `flaky-quarantined` — all three describe repair states from the test-suite-repair project. None applies to a newly authored passing test, and the gate agreed it is likely not a real obligation |
| W10 no kill-switch on the new term | **Declined, deliberately.** A config gate would introduce precisely the asymmetric-registration hazard §F.1 exists to prevent. The term is already data-gated on the organisation's standing-grant flag, and **zero organisations hold one today** (task 042 §3), so it is inert until an operator sets one. Register B-1's model is derived-access-default-on with Secure as the veto |
| W11 ADR-028 A5 systemuser root set | **Not this task's** — task 036 owns swapping in Dataverse's impersonated answer, and is blocked behind 🟡 034. This task **narrows** the documented interim state rather than widening it, by registry-filtering the interim path for however long it lives |

Noted for the record: the gate independently reached the same conclusion as §5 about the POML's step 3 —
that "the flag-off branch of 036" does not exist, and encoding that correction at the call site rather
than silently doing something else was the right handling of a stale brief.

---

## 14. Step 9.5 — `code-review`: 1 Critical · 2 High · 7 Medium · 6 Low

### 14.1 🚨 C-1 — ESCALATED, NOT DECIDED. `accessConferringOnly: true` also deletes owner/team/BU membership

**This is the finding of the task, and I missed it.** Verified from source before escalating:

| Evidence | Where |
|---|---|
| `systemuser` / `team` / `businessunit` ARE discovered as descriptors | `MembershipOptions.cs:252,254,255` (`CanonicalIdentityTables`) |
| The conferring registry contains **no** `ownerid` / `owningteam` / `owningbusinessunit`, and no `IdentityType = "SystemUser"/"Team"/"BusinessUnit"` entry | `MembershipOptions.cs` — grepped; zero matches outside the identity-tables list |
| Such an entry is **inexpressible**: anything not `Contact`/`Organization` is logged as *malformed* and dropped, so an operator could not add it even deliberately | `MembershipResolverService.cs:592-596` |
| The binding being lost | `MembershipResolverService.cs:782-784` — `case "SystemUser": AppendCondition(sb, d.Field, identity.SystemUserId)` |
| No compensating term exists | task **036** `🔲 open`, deps include **034** `🟡 blocked`; `IImpersonatedRootSetSource` is **not injected** into the evaluator |

An internal Type-1 user who owns 45 matters and is not named in any `sprk_assigned*` contact column
previously got all 45 via `ownerid`. With the flag on, the membership term returns nothing for them, so
their accessible set collapses to (contact-column matches ∪ their own contact grants) — **zero for an
owner-only user, and zero for any Type-1 user with no linked contact**. Every BFF-mediated read path is
affected.

**The damning detail: this task's own test asserts the regression as correct.**
`ResolveAsync_AccessConferringOnly_DoesNotShareACacheEntryWithTheUnfilteredCall` seeds `ownerid` and
asserts the filtered roles are exactly `{"assignedAttorney"}` — `owner` disappearing was written into the
expected value and read past.

Direction is **fail-closed** (under-grant, not disclosure), so it is availability/correctness rather than
exposure. That does not make it an implementer's call: design §4.5 puts a Type-1 user's owner/team access
in **term 1, the Dataverse answer via §4.4** — i.e. task 036 — and the POML's criterion scoped the intent
to *over-inclusion* closure (A-8), which is a categorically smaller effect. **Escalated per CLAUDE.md
§6; 043 is NOT marked complete and `ct043b.js` is NOT run pending the answer.**

### 14.2 H-2 — the fault test and its doc claimed more than the code delivers. Corrected

`ActiveOrgMemberships.Unreadable` is set only for faults that reach the reader as an **exception** —
in practice token/API-url acquisition. A junction **query** failure (500/403/timeout/any non-success) is
swallowed inside `ExternalParticipationService`'s private query, which returns an **empty list** — so it
arrives as `Unreadable: false` and the deny veto's **organization axis** silently stops matching for
that subject. Contact-keyed deny rows still apply, so the wall narrows rather than vanishes.

**The hole pre-dates task 043.** What task 043 did was document and test it as *closed* — with a double
that throws, which the real path mostly does not — and that is worse than silence, because a confident
comment plus a green test stops the next reader looking. Same error class as §8 and §12: the instrument
was not measuring what it claimed. Both the type's remarks and `ResolveDenyVetoAsync`'s remarks now state
the exact scope and name the residual. **Fixing it properly belongs one layer down**, in the junction
query, which also serves the org-GRANT path whose additive caller deliberately wants empty-on-fault — the
same inverted-fail-direction problem recursing. **Needs a register entry + issue.**

### 14.3 H-3 — `sprk_enddate`: the hedge is a data state, not a control

The §4.1 decision stands, but the gate sharpened why it cannot rest on "exposure is nil today":
**one admin ticking `sprk_standinggrant` on one organization arms an over-grant to every stale member of
that firm.** Belongs in the PR as an explicitly accepted risk with owner sign-off, plus a register entry
— not only in these notes.

### 14.4 Fixed and verified

| # | Finding | Fix |
|---|---|---|
| **M-1** | My edit inserted `OrganizationIdentityTypeOnly`'s doc immediately after `ResolveDenyVetoAsync`'s `</remarks>`, so the entire fail-closed safety explanation documented a `string[]` and the veto method had **no doc at all** | veto block moved to the method (deleting the field alone would have re-attached it to the next member); `<param>` for the new `subjectOrgs` added |
| **M-2** | Hoisting the junction read moved it **above** the veto's `candidateIds.Count == 0` early-return, so a composition with nothing to evaluate performed a read it previously skipped — multiplied per authorization decision | systemuser-plane read gated on candidates being non-empty. On the **contact** plane the read must precede the walk (it is an input), so that increase is the new term's honest cost, not a regression |
| **M-4** | Provenance doc conflated "could contribute" with "did contribute"; the code does the former, mirroring 042's `standingApplied` | doc tightened — the two flags must mean the same thing, since FR-30 provenance reads (task 064) consume both |
| **M-5** | **No test had the org term and the contact's own standing term active together** — a gap this task created, since the replaced 042 test was the only place they were co-configured. Two derived terms at two baselines feeding one `AccumulateTerm` is exactly where a level can be mis-credited | `ComposeAsync_OrgAndContactStandingBothActive_EachTermKeepsItsOwnBaseline`: shared record = Collaborate ∪ ViewOnly; org-only record stays Read |
| **L-1** | `ContactWalk` was dead, while `OrgWalkFor`'s doc relied on it to claim mutual exclusivity | the M-5 test is its consumer — fixed without deleting a referenced symbol |

**Verified**: build **0 / 0**; filter proven non-empty at **109** (was 108, +1 for the new test);
**109 / 109 pass**.

### 14.5 Accepted with reasons, not fixed

- **M-3** (N+1 org-baseline reads, N unbounded — no `$top` on the junction). Real shape issue; **0
  organizations hold a standing grant today**, so N is 0. Batching the baseline read is the right
  eventual fix. Register entry.
- **M-6** (§10 artifacts) — **discharged after the gate ran**: publish +0.10 MB with 214 files each side
  (§9), CVEs clean (§11).
- **M-7** (decompose `ComposeForContactAsync`, now ~11 concerns) — agreed in principle, and the gate is
  right that extracting the org term would have made M-1 and L-1 unlikely. Declined **in this task**:
  restructuring the evaluator's hottest security method while a Critical on the same method is
  unresolved trades a real risk for a cosmetic gain. Candidate for its own task.
- **L-2** redundant `Verify` · **L-3** KEEP-path tension (pre-existing, `bff-extensions` §F directs BFF
  tests here — a standing directive conflict, not this task's) · **L-4** over-keyed cache (harmless) ·
  **L-5** redundant `.Distinct()` · **L-6** non-deterministic bucket iteration order (log text only).

### 14.6 What the two gates agreed on independently

Both reached the same verdict on the cache-key finding — that it was a **latent disclosure armed by this
task's own step 3**, correctly diagnosed and correctly pinned by two tests that run the calls in the
dangerous order — and both flagged the POML's non-existent "flag-off branch of 036". `adr-check` and
`code-review` disagreed on nothing.
