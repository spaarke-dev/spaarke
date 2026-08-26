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
| **075 → 076** Wave 2 | (agent `wave2-075-076`) | — | 🔄 **STILL RUNNING** |

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
