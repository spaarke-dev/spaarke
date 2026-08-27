# Current Task State — sdap-SPE-admin-app-r2

> **Last Updated**: 2026-08-27 02:05 UTC (by `context-handoff`)
> **Recovery**: read "Quick Recovery" first, then §1 (the wave plan you were asked to run).
> §7 is preserved history from earlier sessions — do not re-derive it.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Phase** | UAT remediation **finished**; returning to **POML task execution** |
| **Status** | App is **FUNCTIONAL** and deployed to dev. 8 UAT rounds closed. |
| **Branch** | `work/sdap-SPE-admin-app-r2` · clean · pushed · 0 behind `origin/master` |
| **Head** | `4a5982ef2` |
| **Open PR** | [#828](https://github.com/spaarke-dev/spaarke/pull/828) → master · **MERGEABLE** · see §2 before merging |
| **Next Action** | Run **Wave A** (§1): `061` + `062` in parallel via `task-execute`. Then `041` alone. |

### Operator's standing instruction (2026-08-27)

> *"continue with `/task-execute` working in parallel and autonomous where possible"*

Parallel = ONE message with MULTIPLE `Skill` invocations. Never sequential. Per project
[`CLAUDE.md`](CLAUDE.md), **every** task goes through `task-execute` — never read a POML and implement
by hand.

---

## 1. The wave plan — start here

**8 tasks open.** Statuses below are from `tasks/TASK-INDEX.md`, which is authoritative and richer than
the POML `<status>` fields (see the ⚠️ in §5).

| Wave | Tasks | ∥-safe | Run how | Gate |
|---|---|---|---|---|
| **A — do this first** | **061** (refresh SPE knowledge corpus) · **062** (billing-attach handoff) | ✅ ✅ | **Parallel**, 2 agents | W18 is the ONLY `/goal`-eligible span. MINIMAL rigor. Deps (025/029) satisfied. |
| **B** | **041** (LiveIntegration suite + throwaway container fixture) | ✅ | **Alone** | ⚠️ Live tenant. NOT goal-eligible. Builds the fixture 052 depends on. |
| **C** | **042** (retire scaffolding tests) | ✅ | Alone, after 041 | ADR-038 deletion-safety; escalation trigger on unreplaced coverage |
| **D** | **050** (archival) · **051** (quota ceiling) | ❌ ❌ | **Sequential, main session** | Both unblocked (deps done). Not parallel-safe. |
| **E** | **052** (item recycle bin) | ❌ | Alone, after 041 | 🚨 **Destructive.** Throwaway container only |
| **F** | **090** (wrap-up) | ❌ | Last | `/test-diet` is a **BINDING** gate |

**Why 061+062 first**: both `MINIMAL` rigor, both `∥-safe ✅`, both W18, dependencies satisfied, and W18 is
the only wave `task-create` marked goal-eligible. It is the one place "parallel and autonomous" is
already sanctioned by the plan rather than by improvisation.

### 🚨 The scheduling constraint that governs every wave

Project [`CLAUDE.md`](CLAUDE.md): nearly every task modifies `SpeAdminGraphService.cs` (4,911 LOC).
**At most ONE task per wave may modify it.** Realistic concurrency is **2–3 agents, not 6**. Check each
POML's `<outputs>` before dispatching a wave.

### Two tasks are PARTIAL, not open — do not restart them

- **025** — server complete, **form deferred**. FR-C07 named a property that does not exist
  (`agent.chatEmbedAllowedHosts`) and omitted one that does (`sharingCapability`).
- **026** — **AC-2 escalated, not achievable**: `consumingTenantOverridables` is a *permission*, not a
  state, and cannot be read from an owning tenant. Do not "fix" this by trying harder.

---

## 2. PR #828 — read before merging

**Tenant Isolation now passes (34/34, was 31/34).** But **Build & Test (Debug) failed**, and three
failures are `TimeoutException` / `TaskCanceledException` in:

- `ScheduledJobHostTests.StopAsync_CancelsInFlightJobWithinDrainTimeout_NFR07`
- `RetryAndIdempotencyTests.CancellationDuringRetryLoop_StopsImmediately_DoesNotSleepThroughToken`
- `SseStreamingIntegrationTests.Cancellation_NoLingeringBackgroundTask_AfterClientAbort`

**None of them touch anything this branch changed** (a PowerShell script, an ArchTest, a comment in
`RecordMatchService.cs`, and SpeAdminApp `.tsx`). All three use `Task.Delay` / `Stopwatch` / wall-clock
timeouts — exactly the constructs [`tests/CLAUDE.md`](../../tests/CLAUDE.md) **bans** as *"sources of
flakiness on shared CI runners"*, prescribing `FakeTimeProvider` instead.

**A re-run of the failed jobs was in flight when this handoff was written.** Check it before merging:

```bash
gh run view 33029952218 --json status,conclusion --jq '"\(.status)/\(.conclusion)"'
gh run view 33029952218 --json jobs --jq '.jobs[] | select(.conclusion=="failure") | .name'
```

- **Green on re-run** → flaky confirmed; merge #828.
- **Same three fail again** → still almost certainly pre-existing, but confirm against master's own
  Build & Test history before blaming this branch.

> ⚠️ **Branch protection is DISABLED.** `gh pr merge --auto --merge` merges **immediately**, without
> waiting for CI. The `Tenant Isolation` job is labelled *merge-blocking* and cannot block anything.
> That is how PR #826 merged with a red gate. Verify checks **before** invoking merge, not after.

---

## 3. The recurring defect shape — read before debugging anything here

> **A lower layer collapses a real value — or a real failure — into an absent/empty/garbage result
> that an upper layer reads as benign.**

Confirmed **17** times in this project. The four newest:

- **Flat wire vs nested client type.** `SpeContainerItemSummary` sends `isFolder`/`mimeType`/
  `createdByDisplayName`; `DriveItem` declares `folder`/`file.mimeType`/`lastModifiedBy.user.displayName`.
  `isFolder(item)` tests `!!item.folder` → **false for every item**. Every folder rendered as a file and
  could not be opened. Third instance of this exact shape after `id`→`containerTypeId` and five
  `{items,count}` envelopes.
- **A second `.ToString()` on a collection.** The 2026-08-26 fix patched `MapConsumingTenant` and missed
  `MapContainerTypePermission` — the path the Permissions tab actually reads.
- **Argument-order swap.** `items.download(containerId, itemId, configId)` called as
  `(containerId, configId, item.id)`. Three `string` params, so the compiler saw nothing; a 404 resolves
  rather than throws; the catch wrote to `console.error`. Auditing the same shape found `items.delete`
  swapped identically — **broken and unreported**.
- **A scan that could not tell code from prose.** The I5 ArchTest reported a CATASTROPHIC credential
  violation against a **doc comment warning against that very construct**.

**The method that keeps working:** when N things fail identically, find the one that *works* and ask what
it does differently. And when you fix one instance of a shape, **grep for the shape**, not the instance.

---

## 4. Highest-value work that is NOT in the POML backlog

**1. Add `tsc --noEmit` to the SpeAdminApp build + a test runner.** `vite build` does **not** typecheck;
~38 type errors ship; `lint` invokes an uninstalled ESLint. **Three total client/server shape mismatches
reached operator UAT for exactly this reason** — all three were invisible to the build and obvious to
`tsc`. Every typecheck in rounds 5–8 was run by hand. Roughly an afternoon; closes the hole that
generated most of the last four UAT rounds. **Recommend doing this before 050/051/052.**

**2. I2 cross-tenant search bleed — waived, not fixed.** `RecordMatchService` queries with no tenant
predicate. Waived on an owner ruling that `spaarke-records-index` is single-tenant. The waiver expires
with the **deployment model, not a date**: both call paths must be scoped before the first shared tenant
is onboarded. `JobContract` has no tenant field — that is the real work.

**3. Container-type DELETE does not exist.** Operator asked for it. Graph supports
`DELETE /storage/fileStorage/containerTypes/{id}` and refuses when containers still exist, so blast
radius is bounded by Graph. Not added unilaterally: a new destructive BFF endpoint trips root §10 +
§6.

**4. `Publish Dataverse Solutions Manifest`** has failed on master for ≥3 commits. Unowned.

---

## 5. Traps that cost real time here

⚠️ **POML `<status>` fields are STALE — trust `TASK-INDEX.md`.** Task 011's POML says `not-started`;
TASK-INDEX says ✅ completed 2026-08-24 with a detailed note. Reading the POML alone would restart
finished work.

⚠️ **Hash-check the deployed BFF DLL before re-diagnosing any BFF bug.** A landed fix was silently
reverted 52 minutes later by another session's deploy, and the recurrence looked exactly like a wrong
diagnosis. Recipe in §6.

⚠️ **We have NEVER deployed with slots.** `config/environments.json` declares `appServiceSlot: staging`;
zero slots exist. Do not claim slot-swap rollback.

⚠️ **Another session is active on this branch.** Commit `4a5982ef2` (the I2 waiver) and PR #828 were
authored elsewhere while this session worked. `git fetch` and re-read before assuming your view is
current.

⚠️ **Nine POML premise errors, nine for nine** in earlier waves. Verify a task's premise against
code/CSDL **before** implementing it.

---

## 6. Verification recipes

```bash
# Is the deployed BFF actually my build?  RUN BEFORE RE-DIAGNOSING ANY BFF BUG.
TOKEN=$(az account get-access-token --resource https://management.azure.com --query accessToken -o tsv)
L=$(sha256sum deploy/api-publish/Sprk.Bff.Api.dll | cut -d' ' -f1)
curl -s -H "Authorization: Bearer $TOKEN" \
  "https://spaarke-bff-dev.scm.azurewebsites.net/api/vfs/site/wwwroot/Sprk.Bff.Api.dll" -o /tmp/r.dll
[ "$L" = "$(sha256sum /tmp/r.dll | cut -d' ' -f1)" ] && echo MATCH || echo "STALE — redeploy"

# What is ACTUALLY published to Dataverse (never trust the deploy message):
#   webresourceset(5f86c079-cd1f-f111-88b3-7ced8d1dc988)?$select=content -> base64 decode -> grep
#   NOTE: grep the bundle for camelCase JS identifiers, not kebab-case CSS —
#   Griffel emits `scrollbarWidth` and converts at runtime. I got this wrong once.

# Client typecheck (NOT in the build — must be run by hand):
cd src/solutions/SpeAdminApp && npx tsc --noEmit -p tsconfig.json

# Tenant-isolation gate:
dotnet test tests/Spaarke.ArchTests/ --filter "FullyQualifiedName~TenantIsolation"
```

**Deploys — always `pwsh`, never `powershell`** (5.x lacks `Get-FileHash` in this harness):

- BFF: `rm -rf src/server/api/Sprk.Bff.Api/publish; pwsh -ExecutionPolicy Bypass -File scripts/Deploy-BffApi.ps1`
- Code page: clear `dist/* node_modules/.vite/ .vite/` first, then
  `pwsh -ExecutionPolicy Bypass -File scripts/Deploy-SpeAdminApp.ps1 -Environment dev -DataverseUrl "https://spaarkedev1.crm.dynamics.com"`

### Domain facts learned live

- **"Config"** = `sprk_specontainertypeconfig` — binds container type + business unit + environment +
  owning app + Key Vault secret name. Labelled "Container Type" in the UI, but it is **not** the
  container type.
- **ADR-028 E-1** covers per-customer **owning-app** credentials — **not** the BFF's own identity
  reaching Key Vault. auth-v4 applied the exclusion one layer too wide.
- `spaarke-bff-dev` has a **user-assigned** identity (`mi-bff-api-dev`, clientId
  `5967251e-171c-46fe-a6c2-ef843c90309d`) and **no system-assigned one** — every credential MUST pin the
  client id.
- Fluent v9: **`Divider` defaults to `flex-grow: 1`**; **`<Text truncate>` does NOT stop wrapping**
  (needs `wrap={false}`); **`columnSizingOptions` must be a stable reference** or drags reset on every
  render; Fluent **does not style scrollbars at all**.
- Publish size **45.07 MB** incl. PDBs (baseline 44.96, ceiling 60).

---

## 7. Preserved history — earlier sessions

### 🔑 The PATCH-400 was a missing `etag` (2026-08-25)

Two days of "container-type writes are impossible" was a missing **required body property**. Graph's
Update API lists `etag` as REQUIRED **in the body**.

| Identical no-op PATCH | |
|---|---|
| without `etag` | **400** |
| with `etag` in the body | **200** (beta AND v1.0) |

⚠️ It is a **BODY property, NOT the `If-Match` header.** An earlier session tried the header, correctly
recorded that it changed nothing, and read that as "the etag is irrelevant" — which aimed the whole
investigation at auth. Full record: [`notes/patch-400-resolution.md`](notes/patch-400-resolution.md).

> **THE LESSON, twice in one day:** both 400s were documented requirements returned as `invalidRequest`
> naming no cause, and both were one fetch of Microsoft's reference page away. **The corpus and the CSDL
> being silent is not the platform being silent.**

### Other prior findings

- **Master was red before our merge** — 6 tests asserted endpoints auth-v4 deliberately deleted; two
  passed **vacuously** (asserting a no-access user gets 403 while *every* user got 403).
- **A remembered failure mode is a hypothesis, not a diagnosis** — 66 test failures were blamed on a
  remembered WireMock/MimeKit issue; reading the actual exception took one command and showed a ctor
  change.
- **Security alerts 403** — Graph says *"Account is not provisioned"*: the tenant lacks the Defender
  workload. **Not** the missing `SecurityEvents.Read.All` grant our message guesses at. Not fixable in
  code; the wording could be sharpened.
- **Those folders** (`communications` / `emails` / `exports`) — nothing in the repo creates them by name.
  The mechanism is path-based upload (`Drives[id].Root.ItemWithPath(path)`), which auto-creates parents
  from a caller-supplied `FolderPath`. Now that folders open, **the `Modified By` column inside them names
  whatever wrote the files** — one click, and it should be answered **before 052 touches anything
  destructive**.
