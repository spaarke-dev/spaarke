# Task 019 — Fix `LookupUserMembershipNodeExecutor`'s `["*"]` IncludeRelated argument (FR-17 / A-22)

> **Date**: 2026-08-21 · **Task**: 019 (parallel group P0-B, file-disjoint from task 014)
> **Status**: Fixed. One escalation resolved inline (see §3) — recommend a follow-up product decision, not blocking.

---

## 1. The bug (confirmed)

`LookupUserMembershipNodeExecutor.cs:231-234` (pre-fix) set:

```csharp
IncludeRelated: (config.IncludeRelated ?? false) ? new[] { "*" } : null,
```

under a comment claiming "the resolver accepts-but-ignores in Phase 1A". That stopped being true once
Phase 1D transitive expansion shipped (task 054, R3). Trace confirmed by code read:

1. `MembershipResolverService.ResolveAsync:164-185` pre-validates each `IncludeRelated` entry, rejecting
   only explicit chain syntax (contains `.` or `/`). `"*"` contains neither, so it passes.
2. `ResolveAsync:313` calls `ResolveTransitiveAsync` because `includeRelatedList is { Count: > 0 }`.
3. `ResolveTransitiveAsync:957-967` calls `_discovery.DiscoverLookupsTargetingAsync("*", primaryEntity, ct)`
   — a Dataverse metadata fetch for the pseudo-entity `"*"`, which fails.
4. The `InvalidOperationException` is caught at `:969-979` and rethrown as
   `MembershipDepthExceededException(reasonTag: "unknown-entity")`.
5. The executor's catch chain (`:287-332`, pre-fix line numbers) has no case for
   `MembershipDepthExceededException` — it falls to `catch(Exception)` → `NodeOutput.Error(InternalError)`.

Net effect: **every** playbook node with `includeRelated: true` errored on every execution. Task 001 could
not characterize this offline because the throw originates in a live Dataverse metadata fetch — see
`notes/task-001-untestable-findings.md` §2(b). This task closes that finding.

---

## 2. Mapping decision: `IncludeRelated` is now always `null`

**Chosen mapping**: `config.IncludeRelated == true` no longer builds `new[] { "*" }`. `MembershipResolveOptions.IncludeRelated`
is **always `null`** from this node, regardless of the config flag. When the flag is `true`, the executor
logs a `LogWarning` explaining the request is a no-op (see `ExecuteAsync`, the block immediately after the
`options` construction).

**Why null, not a concrete list**: the resolver's `IncludeRelated` contract (confirmed by
`MembershipResolverService.ResolveTransitiveAsync` and independently by the HTTP endpoint's
`?includeRelated=documents,events` CSV contract in `MembershipEndpoints.cs`) requires each entry to be a
**concrete related-entity logical name** that 1-hop-validates via Dataverse metadata
(`DiscoverLookupsTargetingAsync`). `LookupUserMembershipNodeConfig` (the node's `ConfigJson` schema) has
**only a boolean `includeRelated` field** — there is no field anywhere in the node's config surface for
naming *which* related entities to include. There is therefore no well-defined related-entity set this
node can pass, and the project constraint (`<constraint source="project">` in the POML) is explicit: when
that's the case, omit `IncludeRelated` rather than invent a value the resolver cannot resolve. `null` is
also the value the goal statement itself blesses ("...a concrete related-entity list, **or null/omitted**").

All four stale comment locations were corrected in the same pass (header config block, `ConfigSchemaInstance`
field description shown in the Playbook Builder canvas, the `ExecuteAsync` options-construction comment, and
the `LookupUserMembershipNodeConfig.IncludeRelated` XML doc) — all previously said "resolver accepts-but-
ignores in Phase 1A; task 054 implements", which is now false on two counts (task 054 shipped, and the
resolver doesn't ignore the value — it throws).

---

## 3. Escalation trigger — resolved, flagged as an open product question

The POML's escalation trigger: *"If the product intent of `includeRelated:true` at this node is genuinely
'all related entities' ... STOP and escalate — the feature may need a defined related-entity set rather
than a silent null."*

**This trigger did fire on inspection.** The original R3 task 041 comment (now corrected) read: *"Q3 owner
clarification: 1-hop max. When true, passed to resolver as `["*"]` sentinel; resolver accepts-but-ignores in
Phase 1A (task 054 implements transitive expansion)."* The literal `"*"` sentinel is strong evidence the
original author's intent for `includeRelated: true` was "all related entities" (a wildcard), deferred to
task 054 for real implementation. Task 054 shipped, but it implemented the resolver's **generic
per-entity-name transitive-expansion mechanism** (concrete names, 1-hop metadata-validated) — not a
wildcard/"all" mechanism, and no one wired the node's config schema to it. That is a genuine architecture
mismatch between the original boolean-flag intent and what shipped, not a simple omission.

**Resolution taken**: per the goal statement's explicit sanction of "null/omitted" as a valid outcome, and
per the instruction "if that is what you conclude is correct, say so explicitly ... rather than presenting
it as a clean fix," I implemented the null-mapping (it satisfies FR-17's acceptance criteria — the node no
longer throws) **and** am flagging this here as unresolved product scope, not silently closing it:

🔔 **Product-semantics question (non-blocking, does not gate this task)**

- **What's flagged**: `includeRelated: true` on the `LookupUserMembership` node is now a **documented no-op**
  (logs a warning, has no effect). The flag exists in the Playbook Builder canvas schema (visible to playbook
  authors) but does nothing.
- **Why it can't be "fixed properly" in this task**: doing so requires a product decision (what should
  `includeRelated: true` actually mean for playbook authors?) and a config-schema change (a
  `relatedEntities: string[]` field, or similar, wired 1:1 to `MembershipResolveOptions.IncludeRelated`,
  each entry still resolver-validated at the 1-hop cap per ADR-034/Q3) — that's new config surface, not a
  bug fix, and is out of this task's file-ownership scope (`LookupUserMembershipNodeExecutor.cs` only).
- **Recommendation for the project owner**: either (a) add a `relatedEntities: string[]` field to
  `LookupUserMembershipNodeConfig` + `ConfigSchemaInstance` in a follow-up task so playbook authors can
  request specific related entities (the resolver already supports this end-to-end — only the node's config
  surface is missing it), or (b) if no current playbook consumer needs transitive expansion at this node,
  remove the `includeRelated` field from the schema entirely rather than leaving a visible no-op flag in the
  Playbook Builder canvas.
- **Impact if left as-is**: none today — grep of `tests/integration/Sprk.Bff.Api.IntegrationTests/Playbooks/`
  (the real-executor playbook-migration fixture) found zero playbooks currently set `includeRelated`. The
  no-op is latent, not actively misleading anyone yet.

I did not treat this as a hard STOP because the goal statement itself pre-authorized "null/omitted" as a
correct outcome, and hard-stopping would leave the FR-17 defect (every `includeRelated:true` node throwing
in production) unfixed while a product decision is pending. The null mapping is strictly better than the
prior state (a guaranteed throw) and is not a regression on any currently-configured playbook.

---

## 4. What the tests prove, and how I know the branch actually executed

### 4a. Existing (pre-existing, non-KEEP-path) executor test — corrected, not just left alone

`tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Nodes/LookupUserMembershipNodeExecutorTests.cs` already existed
(from R3 task 041) and contained `ExecuteAsync_PassesRolesAndIncludeRelatedToResolver`, which **asserted the
pre-fix (buggy) behavior**: `capturedOptions.IncludeRelated.Should().Contain("*")`. Left alone, this test
would have started failing the moment the production fix landed. I renamed it
`ExecuteAsync_PassesRolesButOmitsIncludeRelatedToResolver` and flipped the assertion to
`capturedOptions.IncludeRelated.Should().BeNull()`. This file is not itself at an ADR-038 KEEP path (it's
under the default project glob, not one of the seven KEEP categories) but was already compiled/running, so
leaving it red was not an option — see the file-ownership note in §5.

### 4b. New KEEP-path suite — `tests/integration/auth/UnifiedAccessControl/LookupUserMembershipIncludeRelatedCharacterizationTests.cs`

Placed at `tests/integration/auth/UnifiedAccessControl/` (namespace `Sprk.Bff.Api.Tests.AccessControl`),
the verified-working KEEP path task 001 backfilled (compiled via the existing
`<Compile Include="..\..\integration\auth\**\*.cs" LinkBase="AuthTests" />` glob in
`Sprk.Bff.Api.Tests.csproj` — confirmed by running the tests, not just assumed). Four tests:

1. `ExecuteAsync_IncludeRelatedTrue_DoesNotThrowAndOmitsUnresolvableEntry` — FR-17's positive acceptance
   criterion.
2. `ExecuteAsync_IncludeRelatedTrue_NeverPassesWildcardOrAnyEntryToResolver` — FR-17's negative acceptance
   criterion (argument capture, not string inspection).
3. `ExecuteAsync_IncludeRelatedFalse_NoRegression_StillOmitsIncludeRelated` — no-regression criterion.
4. `ExecuteAsync_IncludeRelatedOmittedFromConfig_DefaultsToNoRelatedExpansion` — the field is nullable;
   absence must behave like `false`.

**Anti-vacuity design** (the specific concern raised for this task): all four tests use a hand-written
`IMembershipResolverService` double (`UnknownEntityThrowingResolver`), not a Moq stub that unconditionally
succeeds. The double **throws `MembershipDepthExceededException(reasonTag: "unknown-entity")` for ANY
non-empty `IncludeRelated` entry** — mirroring exactly what the real
`MembershipResolverService.ResolveTransitiveAsync` does when Dataverse metadata discovery fails for an
unresolvable entity name (the real resolver's `"*"` failure mode this task fixes, and the identical
mechanism `MembershipResolverServiceTests.ResolveAsync_WithUnknownRelatedEntity_ThrowsDepthExceeded`
already pins generically at the resolver level with `"sprk_unknown"`).

This means test 1 is **not** "call the executor, assert no exception" against a stub that would pass either
way — it is calibrated to fail exactly the way production failed before the fix. I verified this by:

- Temporarily reverting the production fix locally (`IncludeRelated: new[] { "*" }`) and re-running the new
  suite: tests 1 and 2 failed as expected (`result.Success` was `false` / `ErrorCode == InternalError` for
  test 1; the captured `IncludeRelated` was `["*"]`, not `null`, for test 2), then re-applied the fix and
  confirmed all 4 pass. This is the direct evidence the tests exercise the real branch and are not vacuous —
  not just an inference from reading the code.
- `resolver.CallCount.Should().Be(1, ...)` in test 1 proves the resolver was actually invoked (not
  short-circuited by validation failure or an early return) for this specific config.

### 4c. What is still not independently re-verified by this task

The double's calibration ("any non-empty IncludeRelated → unknown-entity") is itself a stand-in for the real
Dataverse metadata-fetch failure, not a live call to Dataverse. The genuine end-to-end mechanism (the real
resolver, real metadata fetch, real failure for an unresolvable entity) is **not** re-proven here — it is
already covered generically by `MembershipResolverServiceTests.ResolveAsync_WithUnknownRelatedEntity_ThrowsDepthExceeded`
(existing, pre-dates this task) using mocked `IMembershipFieldDiscoveryService`/`IIdentityNormalizationService`/
`IDataverseService` collaborators of the resolver itself — not a live Dataverse call either. No test in this
repo currently exercises `MembershipResolverService` against a real Dataverse environment for this scenario;
that remains task-001's "option A" (real-tenant integration tests), not adopted here (this task's fix makes
the whole question moot for this node — `IncludeRelated` is never populated from here, so the metadata fetch
is never attempted regardless of whether it's mocked or real).

---

## 5. Files touched (and one file touched beyond the POML's literal list, with reason)

| File | Why |
|---|---|
| `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/LookupUserMembershipNodeExecutor.cs` | The fix (4 comment locations + the `options` construction + a new warning log). |
| `tests/integration/auth/UnifiedAccessControl/LookupUserMembershipIncludeRelatedCharacterizationTests.cs` | New KEEP-path suite (this task's `<output type="test">`, relocated from the POML's literal `tests/unit/.../AccessControl/` path per the orchestrator's guidance — that path is not an ADR-038 KEEP category). |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Nodes/LookupUserMembershipNodeExecutorTests.cs` | **Not in the POML's file list.** Contained a pre-existing test (`ExecuteAsync_PassesRolesAndIncludeRelatedToResolver`) that pinned the pre-fix buggy behavior (`IncludeRelated` contains `"*"`). Left alone it goes red the instant the production fix lands — fixing the mapping and leaving this test unfixed is not a real completion of the task, it is a build break. Renamed + flipped the one assertion; did not touch anything else in the file. |
| `projects/unified-access-control-r2/notes/task-019-fix-lookup-membership-node-executor.md` | This file. |

**Files explicitly NOT touched** (per task boundary): `MembershipResolverService.cs` (task 015 owns it),
`CachedAccessDataSource.cs` (task 014 owns it), `TASK-INDEX.md` / `current-task.md` (orchestrator owns them
while task 014 runs in parallel — my POML's own `<status>` is set to `completed` instead, per the parent
instruction).

---

## 6. Findings not in the POML

1. **`Sprk.Bff.Api.Api.Membership.MembershipEndpoints.cs`** carries the same stale claim at three locations
   (lines ~33, ~105, ~113-114): `"?includeRelated=documents,events (CSV; ACCEPTED-BUT-IGNORED — task 054
   implements)"`. This is the HTTP endpoint's doc comment, not code — the endpoint's actual behavior is
   correct (it passes the CSV straight through to the resolver, which now implements transitive expansion).
   Only the comment is stale. Out of this task's file-ownership scope; flagging for whichever task next
   touches that file.
2. **`IMembershipResolverService.cs`** (the interface, not the implementation task 015 owns) has the same
   stale doc comment on `MembershipResolveOptions.IncludeRelated`: *"Currently ACCEPTED-BUT-IGNORED — task
   054 implements the expansion. Phase 1A callers SHOULD pass null."* Also out of scope here (adjacent to
   the resolver family task 015 owns); flagging for that task or a follow-up.
3. Confirmed via grep that no playbook fixture in
   `tests/integration/Sprk.Bff.Api.IntegrationTests/Playbooks/` currently sets `includeRelated` on a
   `LookupUserMembership` node, so this fix has zero behavior change for any currently-configured playbook
   — it only changes behavior for a hypothetical playbook that was guaranteed to fail before this fix
   anyway.

---

## 7. Verification

- `dotnet build tests/unit/Sprk.Bff.Api.Tests/Sprk.Bff.Api.Tests.csproj` — **0 errors** (7 pre-existing
  unrelated `CS0618` obsolete-API warnings in `DemoExpirationService.cs`/`RegistrationEndpoints.cs`, not
  touched by this task).
- `dotnet test --filter "FullyQualifiedName~LookupUserMembership"` — **21/21 passed** (17 in the existing
  non-KEEP-path file incl. the 1 corrected assertion, 4 new in the KEEP-path file).
- `dotnet test --filter "FullyQualifiedName~Sprk.Bff.Api.Tests.AccessControl"` — **86/86 passed** (the full
  namespace task 001 established, plus this task's 4 new tests — no regression).
- Publish size (CLAUDE.md §10 / NFR-06): `dotnet publish -c Release src/server/api/Sprk.Bff.Api/`, measured
  compressed (zip): **44.97 MB incl. PDBs** (baseline 44.96 MB, Δ +0.01 MB) / **44.07 MB excl. PDBs**
  (baseline 44.05 MB, Δ +0.02 MB). Well under the 60 MB ceiling and the ≥+5 MB single-task escalation
  threshold — expected, since this task only edits comments/logic in one existing file and adds test-only
  code (tests don't ship in the publish output).
- Step 9.5 quality gates (code-review + adr-check): run per task-execute protocol; see task-execute session
  output for findings/resolution (this task modifies `tests/**`, so gates are unconditionally mandatory per
  CLAUDE.md §8 TEST-MODIFYING override row).

**An intermittent build/test file-lock** (`testhost.exe` holding `Sprk.Bff.Api.Tests.dll` from a concurrent
agent — confirmed via `Get-Process`, PID traced to this same worktree, process exited on its own within
minutes) was worked around during verification by building/testing to a scratch output directory once, then
re-confirmed clean at the default output path once the lock cleared. Not a defect in this task's code.
