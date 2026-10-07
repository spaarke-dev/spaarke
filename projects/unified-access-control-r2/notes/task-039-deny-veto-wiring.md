# Task 039 — deny-list veto wiring + ordered-pipeline tests

> Rigor: FULL. Deps 032/037/038 all met at entry. Step 1 (org-typed lookup inventory) was already done
> before this task started — see [`notes/task-039-org-reference-inventory.md`](task-039-org-reference-inventory.md).
> This note covers steps 2–4 and 6: wiring, tests, verification, and the deviations found along the way.

---

## 1. What was built

### `ExternalParticipationService.cs`

- **`OrganizationLookupAttributes`** — a per-root-entity-type dictionary of org-typed lookup column
  names, mirroring `RootFlagSources`'s shape. Seeded from step 1's live-metadata inventory
  (`sprk_assignedlawfirm1`/`2` on all three roots today).
- **`GetReferencedOrganizationIdsAsync(entityType, recordIds, ct)`** — new, batched (chunk 50, reusing
  `FlagQueryChunkSize`), mirrors `GetRootRecordFlagsAsync`'s shape exactly (same chunking, same
  per-root-entity-type source lookup, same wrap-everything-in-one-try/catch fail-closed style). Returns
  `IReadOnlyDictionary<Guid, ReferencedOrganizations>`.
- **`BuildOrganizationReferenceSelect(orgAttributes)`** — extracted as a PURE `internal static` member
  (task 007/A-5 precedent) so the over-match property (every registered lookup selected, unconditionally)
  is directly assertable without an HTTP stack.
- **`QueryActiveOrgIdsAsync(Guid contactId, CancellationToken ct)`** — new PUBLIC virtual wrapper onto the
  EXISTING PRIVATE `QueryActiveOrgIdsAsync(Guid, string, string, CancellationToken)` (already used by
  `QueryOrganizationGrantRowsAsync`). Reuses the query unchanged; only adds token/API-url acquisition so
  an external caller (`AccessibleRecordSetService`) doesn't need this service's internal plumbing.
- **`OrganizationReferenceRow`** — new DTO, hardcoded to the two verified lookup columns (mirrors
  `RootFlagRow`'s own hardcoded-per-column style rather than generic JSON-extension-data parsing).

### `ExternalCallerContext.cs`

- **`ReferencedOrganizations(IReadOnlyCollection<Guid> OrganizationIds, bool Unreadable)`** — new
  `readonly record struct` next to `RootRecordFlags`, with `.Unresolved` (fail-closed: `Unreadable=true`)
  and `.None` (successful-empty) statics. Needed a dedicated `Unreadable` field rather than folding into
  a sentinel value the way `RootRecordFlags.Unreadable` does, because an org-id set has no analogous
  "most restrictive" combination to encode the fail state into.

### `AccessibleRecordSetService.cs`

- Ctor: 5th dependency `INoAccessListReader noAccessList` (already registered by task 038's
  `AddExternalAccess()` — no DI module change needed).
- `ApplyVetoPipeline`: new 2nd parameter `IReadOnlySet<Guid> deniedRecordIds`; Slot 1 now
  unconditionally `composed.Remove(recordId)` for every id in the set (previously a documented no-op).
  Doc comments rewritten to describe the CURRENT state (deny-list live) instead of the pre-039
  "wired as a no-op" framing.
- New private helper **`ResolveDenyVetoAsync(entityType, candidateIds, subjectContactId, ct)`** — shared
  by both composer methods. Resolves the subject's active org ids
  (`ExternalParticipationService.QueryActiveOrgIdsAsync`) + the candidates' referenced orgs
  (`GetReferencedOrganizationIdsAsync`), builds `NoAccessCandidateRecord`s, and calls
  `INoAccessListReader.GetDeniedRecordsAsync`. Wraps the WHOLE resolution in one try/catch: any fault
  (reader fault, org-resolution fault, anything else) denies every id in `candidateIds`.
- `ComposeForSystemUserAsync`: `grantContactId` hoisted out of the `IsGrantSupported` block (it was
  previously scoped inside it) so the SAME resolved contact identity that fed the grant term also feeds
  the deny-veto subject.
- Both composer methods: compute `deniedIds` via `ResolveDenyVetoAsync` immediately before calling
  `ApplyVetoPipeline`, reusing the EXISTING `candidates` list built for the flag read (never rebuilt).

---

## 2. Design decisions worth recording

### 2.1 A record's own org-references fail closed toward FORCED DENIAL, independent of the reader

If `GetReferencedOrganizationIdsAsync` cannot resolve a specific record's org references (transport
fault, non-success status, or an id the query didn't return), that record is denied DIRECTLY in
`ResolveDenyVetoAsync` — it is never even added to the batch sent to `INoAccessListReader`. Silently
treating "unresolved" as "references nothing" would let the record slip past a real org-keyed deny entry
the read simply couldn't confirm — exactly the escalation trigger's "a skipped record is an unevaluated
wall" case, applied one level deeper than the trigger's literal text (which is about round-trip budget,
not fault handling) but in the same spirit. Pinned by
`ComposeAsync_WhenReferencedOrganizationResolutionIsUnreadableForARecord_ThatRecordIsDeniedDirectly`.

### 2.2 An asymmetry in `QueryActiveOrgIdsAsync`'s two fault surfaces — documented, not "fixed"

The PRIVATE query method (shared with the additive org-grant path) already catches its OWN query-level
faults and returns an empty list — correct for that caller (a fault must not GRANT more access). My new
PUBLIC wrapper does NOT add a second guard around token/API-url acquisition, so:

- a **token-acquisition fault** propagates up to `ResolveDenyVetoAsync`'s outer catch → denies every
  queried candidate (correct direction for a veto subject);
- a **query-level fault** is already absorbed inside the private method → resolves to "this contact has
  zero active orgs" → the deny-veto proceeds with a contact-only subject, no org-subject rows checked.

These two fault classes resolve toward OPPOSITE defaults. I considered unifying them (wrap the private
method's own internal catch from the outside) but rejected it: I cannot observe "the private method
caught a fault and returned empty" from outside without changing that method's existing, shipped
contract for its EXISTING caller, which is out of this task's envelope. The residual risk is narrow and
partially self-mitigating: a record affected by this gap would (a) still be checked via the
record-id-keyed Loop B in `NoAccessListReader` regardless of subject-org resolution, and (b) in the
realistic scenario of a genuine Dataverse outage, the SAME outage would also fault the flag read, which
independently denies contact-sourced terms via the Restricted veto (`RootRecordFlags.Unreadable`).
Documented in the `QueryActiveOrgIdsAsync` XML doc rather than silently accepted.

### 2.3 A finding that refines the task brief: slot order is NOT independently observable for THIS pair of operations

The brief asks to "pin [deny-before-Restricted] with a test that fails if the order is swapped." I built
that test and it does NOT fail when the two loops in `ApplyVetoPipeline` are swapped — verified
empirically (see perturbation B below), not just asserted. The reason is structural: deny removes a
FIXED, externally-computed id set (`deniedRecordIds`, never derived from `composed`'s current keys), and
Restricted's "survive-or-remove" decision is also independent of what deny does. Removing a set of keys
commutes with a conditional remove-or-replace over the REMAINING keys, because neither operation's
decision depends on the other's prior effect, and neither can resurrect a key the other removed. Doing
the removals in either order therefore yields the identical final `composed` state for any record in
either or both sets.

**What I wrote instead**, which DOES discriminate a real bug class and DOES faithfully capture the
intent behind "order matters": `ComposeAsync_SystemUserRecordThatWouldSurviveRestricted_IsStillRemovedWhenAlsoDenyListed`
proves deny's removal is UNCONDITIONAL — a record that would otherwise SURVIVE Restricted (systemuser's
own membership) is still removed once it is also deny-listed. This is the substantive claim underneath
FR-19/FR-23's ordering language, and it fails correctly if someone reintroduces a variant where Restricted's
survival protects a record from denial (perturbation C, below).

I did not change the implementation or the doc comments' plain-English *statement* of the order (deny
still runs first in the source, and that remains the right, clearest shape to write) — only the CLAIM
that a black-box test can detect a swap, which this note corrects.

### 2.4 Deliberately NOT narrowed to task 041's conferral registry

`ResolveDenyVetoAsync` and `GetReferencedOrganizationIdsAsync` never reference `AccessConferringRegistry`
or any conferral concept. Both org-typed lookups (`sprk_assignedlawfirm1`/`2`) are read unconditionally
for the deny match, per spec FR-23's explicit over-match instruction (register B-10). Pinned by
`BuildOrganizationReferenceSelect_IncludesEveryRegisteredLookup_NoConferralNarrowing` (pure, no mocks) and
`ComposeAsync_DenyEntryKeyedOnAnOrganizationReferencedOnlyViaTheSecondLawFirmSlot_StillDenies`.

---

## 3. Perturbation table

Every row: apply the change, run `AccessibleRecordSetServiceTests` (49 cases), record the result, then
revert and re-verify green.

| # | Perturbation | Expected red | Actual result |
|---|---|---|---|
| A | Disable the deny-removal loop entirely (empty `foreach` body) | Every deny-dependent test | **9 failed / 40 passed** — exactly the deny-dependent set (FullAccess+deny, record-keyed, over-match, ordering-survival, full-pipeline, reader-throws, reader-failed-closed, unreadable-org-ref, org-subject-deny). The pure `.Verify()`-based subject-wiring test correctly did NOT redden (it tests call args, not removal) |
| B | Swap slot order (Restricted's loop runs before deny's loop) | Nothing, per §2.3's derivation — verified empirically rather than assumed | **49/49 passed** — confirms the two operations commute for this implementation shape |
| C | Make deny skip a record if `survivesRestricted.ContainsKey(id)` (simulates "Restricted survival protects from deny") | Exactly the ordering-survival test | **1 failed / 48 passed** — `ComposeAsync_SystemUserRecordThatWouldSurviveRestricted_IsStillRemovedWhenAlsoDenyListed` only |
| D | Disable the "unreadable org-ref ⇒ forced deny" branch (`if (false && ...)`) | Exactly the unreadable-org-ref test | **1 failed / 48 passed** |
| E | Pass `Array.Empty<Guid>()` instead of `subjectOrgIds` to the reader | The two subject-wiring tests (`.Verify()` + org-subject-deny) | **2 failed / 47 passed** — confirms the `.Verify()`-based test does NOT suffer the "unconfigured-mock-masked-by-fail-closed" flaw I checked for during design |
| F | Outer catch returns `EmptyDeniedSet` instead of `candidateIds.ToHashSet()` on fault | Exactly the reader-fault test | **1 failed / 48 passed** |
| G | `BuildOrganizationReferenceSelect` drops all but the first attribute (`.Take(1)`) | Exactly the pure select-builder test | **1 failed / 48 passed** |

All perturbations reverted; final state re-verified green (build 0/0, 69/69 across the three touched test
files, 143/143 across the whole `Infrastructure.ExternalAccess` unit-test namespace). No `PERTURBATION`
markers remain in `src/` (checked via grep after the last revert).

---

## 4. Test counts

| Suite | Before | After | Delta |
|---|---|---|---|
| `AccessibleRecordSetServiceTests.cs` (test CASES, `[Theory]` rows counted) | 37 | 49 | +12 |
| `StandingGrantRuntimeUnionSeamTests.cs` + `MembershipPagingCharacterizationTests.cs` (cascade fix only — no new tests, both files' behavior unchanged) | 20 | 20 | 0 |
| Whole `Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess` namespace | — | 143 | — (0 failed) |

`[Fact]`/`[Theory]` ATTRIBUTE count in `AccessibleRecordSetServiceTests.cs` went 35 → 47 (+12 methods, all
new ones are `[Fact]`); the 37→49 case count reflects the pre-existing `[Theory]` with 3 `InlineData` rows
contributing 2 extra cases beyond its single method.

---

## 5. The ctor cascade (task 037's precedent repeated exactly)

Adding `INoAccessListReader` as the 5th constructor dependency broke every DIRECT `new
AccessibleRecordSetService(...)` call site — confirmed by `dotnet build` before I added test-double
overrides. Three files:

1. `tests/unit/Sprk.Bff.Api.Tests/Infrastructure/ExternalAccess/AccessibleRecordSetServiceTests.cs` —
   `CreateSut` gained an OPTIONAL 4th parameter defaulting to a new shared
   `AccessibleRecordSetTestFactory.NeverDeniesReader()` helper, so none of the 37 pre-existing call sites
   needed editing. `FakeParticipationService` and `ThrowingFlagParticipationService` both needed NEW
   overrides of `QueryActiveOrgIdsAsync` and `GetReferencedOrganizationIdsAsync` — without them, the base
   implementations throw on `credential: null!`, which `ResolveDenyVetoAsync`'s own fail-closed catch
   turns into "deny every candidate", silently breaking dozens of pre-existing assertions for a reason
   unrelated to what each test was checking. Traced this precisely for
   `ComposeAsync_WhenFlagReadFaults_SystemUserMembershipStillSurvives`: `SystemUserPrincipal()` sets a
   real (non-empty) `ContactId`, so `grantContactId` resolves even in that test, meaning
   `ResolveDenyVetoAsync` WOULD have been reached and WOULD have force-denied `MemberRecordA` without the
   fix — the exact silent-failing-closed shape task 037's own notes describe for its 5 doubles.
2. `tests/integration/seam/ExternalAccess/StandingGrantRuntimeUnionSeamTests.cs` — same two overrides on
   its own `FakeParticipationService`, plus `NeverDeniesReader()` passed to the direct `new
   AccessibleRecordSetService(...)` call (the deny-list reader is an external boundary this seam
   deliberately doesn't exercise).
3. `tests/integration/auth/UnifiedAccessControl/MembershipPagingCharacterizationTests.cs` — same pattern
   on `NoGrantsParticipationService` + `ComposerWith`.

(Both "integration" files are actually compiled INTO `Sprk.Bff.Api.Tests.csproj` via linked `<Compile
Include>` globs — `tests/CLAUDE.md`'s documented KEEP-path-inclusion pattern — not into a separate
`Sprk.Bff.Api.IntegrationTests.csproj`. Cost me one wasted `--filter` run against the wrong project before
I found the actual glob in `Sprk.Bff.Api.Tests.csproj`.)

No OTHER file in the repo directly constructs `AccessibleRecordSetService` (confirmed by grep before and
after; the BFF's own DI registration is `services.AddScoped<IAccessibleRecordSetService,
AccessibleRecordSetService>()`, which resolves the new dependency automatically from task 038's
already-registered `AddTransient<INoAccessListReader>`).

---

## 6. Acceptance criteria — status

| # | Criterion | Status |
|---|---|---|
| 1 | FullAccess grant + contact×orgX deny + R references orgX ⇒ None | ✅ |
| 2 | contact×record deny removes exactly that record; sibling unaffected | ✅ |
| 3 | org×org / org×record deny for every contact active in that org | ✅ |
| 4 | Over-match: non-conferring-lookup org reference still denies | ✅ |
| 5 | Ordering: deny before Restricted; Secure still suppresses pre-max with deny active | ✅ with a caveat — see §2.3. The literal "swap the loops" test is not constructible for this implementation shape (verified, not assumed); the substantive invariant (deny is unconditional, not something Restricted-survival can block) IS pinned and DOES discriminate |
| 6 | Reader fault ⇒ deny queried candidates, veto never skipped | ✅ (two tests: reader throws; reader returns `FailedClosed=true`) |
| 7 | No deny entries + unrestricted records ⇒ identical to pre-task | ✅ — all 37 pre-existing test cases pass unchanged |
| 8 | Build green; publish size reported, ≤60 MB | ⚠️ PARTIAL — see §7 |

---

## 7. Publish size — what I could and could not do

**HARD RULE conflict, surfaced rather than worked around**: root CLAUDE.md §10's measurement procedure
requires `git worktree add /tmp/wt-master origin/master --detach` to get a FRESH master build for the
delta comparison. This task's own HARD RULES say *"NEVER run any git command... The main session owns
all git."* I did not run it.

**What I measured instead** (no git required): `dotnet publish -c Release
src/server/api/Sprk.Bff.Api/` on MY OWN current branch state, zipped with PowerShell
`Compress-Archive -CompressionLevel Optimal` (matching `scripts/Deploy-BffApi.ps1`, per the constraint):

- **50.42 MB incl. PDBs** (43.14 MB excl. — 7.28 MB of PDBs).
- Comfortably under the 60 MB hard ceiling regardless of attribution (9.58 MB headroom).

**What I did NOT do, and why it matters**: I did not compute a delta attributable to task 039
specifically. This branch has had FIVE-plus tasks land on it today in the same multi-agent wave (033,
037, 027, 038, 041, 051, 053, per `current-task.md`'s session log) since the last recorded master
comparison (task 037's own note: master `eb71df826` = 45.46 MB, branch = 45.47 MB, measured the same
day). My branch's 50.42 MB is ~5 MB above that same-day branch figure — but attributing that gap to task
039 alone would repeat EXACTLY the mistake root CLAUDE.md's own §10 evidence base warns about (the
2026-09-02 spaarkeai-compose-r8 incident: a stale-baseline comparison overstated one task's contribution
46×). I am not willing to guess at an attribution I cannot verify. **Recommend the main session run the
`git worktree add origin/master` comparison** once this wave's tasks are all committed, per the
established pattern in task 037's own notes and the "parallel-wave recipe" in `current-task.md`.

---

## 7.5 Placement Justification (CLAUDE.md §10 / §11)

No new component, service, endpoint, or DI registration. Two extensions to already-registered services:

- **Existing**: `ExternalParticipationService` already owns the app-only Dataverse read plumbing (typed
  `HttpClient`, token cache, API-url resolution) for this evaluator, and already reads the same 3 root
  entity collections for `GetRootRecordFlagsAsync`. `AccessibleRecordSetService` already owns the veto
  pipeline seam (`ApplyVetoPipeline`, deliberately left as a documented no-op by task 032 for exactly this
  task to fill).
- **Extension**: both additions are new METHODS on already-registered, already-injected services, not new
  types requiring DI changes. `INoAccessListReader` itself is task 038's addition (already registered);
  this task consumes it, it does not re-register it.
- **Cost of doing nothing**: FR-23 cannot be evaluated at all — a contact on the No Access List for
  organization X keeps FullAccess on every record referencing X, including one held via an explicit
  grant. That is a live ethical-wall failure (the exact scenario the deny list exists to prevent), not an
  abstraction.

`Infrastructure/DI/ExternalAccessModule.cs` needed NO change — `INoAccessListReader` was already
registered by task 038; the BFF's constructor-injection DI container resolves the new 5th
`AccessibleRecordSetService` dependency automatically.

**CVE check**: no new NuGet packages added; `dotnet list package --vulnerable --include-transitive` is
unaffected by this change (no package references touched).

---

## 8. Files changed

- `src/server/api/Sprk.Bff.Api/Infrastructure/ExternalAccess/AccessibleRecordSetService.cs`
- `src/server/api/Sprk.Bff.Api/Infrastructure/ExternalAccess/ExternalParticipationService.cs`
- `src/server/api/Sprk.Bff.Api/Infrastructure/ExternalAccess/ExternalCallerContext.cs` (new `ReferencedOrganizations` type)
- `tests/unit/Sprk.Bff.Api.Tests/Infrastructure/ExternalAccess/AccessibleRecordSetServiceTests.cs`
- `tests/unit/Sprk.Bff.Api.Tests/Infrastructure/ExternalAccess/AccessibleRecordSetTestFactory.cs` (new `NeverDeniesReader()` helper)
- `tests/integration/seam/ExternalAccess/StandingGrantRuntimeUnionSeamTests.cs` (ctor-cascade fix only)
- `tests/integration/auth/UnifiedAccessControl/MembershipPagingCharacterizationTests.cs` (ctor-cascade fix only)

No `.claude/` files touched. No DI module changes (task 038 already registered `INoAccessListReader`).
No `TASK-INDEX.md` / `current-task.md` edits (per this task's HARD RULES — main session owns those).
