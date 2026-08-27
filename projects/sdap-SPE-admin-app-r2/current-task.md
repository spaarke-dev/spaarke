# Current Task State — sdap-SPE-admin-app-r2

> **Last Updated**: 2026-08-27 (by `context-handoff`)
> **Recovery**: read Quick Recovery, then §1. §7 is preserved history — do not re-derive it.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | none active — 041 just completed |
| **Phase** | POML execution. Waves A + B done. |
| **Status** | Branch clean, pushed. **1 ahead / 9 behind `origin/master`** |
| **Head** | `86441920d` |
| **Next Action** | `git merge origin/master` (9 behind — CI changed under us, see §2), then run **task 042** via `task-execute`. |

### Files modified this session (all committed + pushed)

| Commit | Contents |
|---|---|
| `bec4e6792` | Wave A — 061 corpus refresh (4 files, +525 lines) + 062 billing handoff (2 docs, issue #831) |
| `86441920d` | Task 041 — `LiveIntegrationFixture.cs`, `ContainerLifecycleLiveTests.cs`, `notes/task-041-teardown-proof.md`, premise corrections in project `CLAUDE.md` + the 041 POML |

### Critical context

Three tasks completed this session (061, 062, 041), all verified in the main session rather than
accepted from agent reports — **one agent silently skipped an instruction**, caught only by `git status`.
Task 041 found a **real production defect** (issue **#834**). PR #828 merged to master at 02:54Z; only
041 remains unmerged.

---

## 1. What to do next

**Sync first.** The branch is **9 behind**. Master gained PRs #828, #829, #830 — including CI changes
that alter how tests are run and retried. Running 042 (a test-deletion task) against a stale view of the
CI config would be reasoning from the wrong baseline.

```bash
git fetch origin && git merge origin/master --no-edit && dotnet build src/server/api/Sprk.Bff.Api/
```

**Then task 042** — retire scaffolding tests per ADR-038 §7. Unblocked (deps 040 + 041 both complete).

| Wave | Task | ∥-safe | Run how | Note |
|---|---|---|---|---|
| **C — next** | **042** retire scaffolding tests | ✅ | Alone | 🚨 FULL rigor: modifies `tests/**` → Step 9.5 gates UNCONDITIONAL. Deleting unreplaced coverage is an escalation trigger, not a judgement call |
| D | **050** archival · **051** quota ceiling | ❌ ❌ | Sequential, main session | Both unblocked |
| E | **052** item recycle bin | ❌ | Alone | Destructive — the 041 fixture it needed now EXISTS |
| F | **090** wrap-up | ❌ | Last | `/test-diet` is a **BINDING** gate |

**Two tasks are PARTIAL, not open** — do not restart: **025** (server complete, form deferred) and
**026** (AC-2 escalated as *not achievable* — `consumingTenantOverridables` is a permission, not a
state).

---

## 2. What changed on master while we worked

**PR #828 merged** (02:54Z → `7e755c48e`). It had been held pending "other projects completing and some
CI cleanup"; the cleanup landed and it went in. **Do not re-open or re-merge it.**

Master also gained the CI cleanup itself — #829 and #830:

- `fix(ci): measure determinism instead of assuming it in the test-retry classifier`
- `fix(ci): stop two workflows that cannot succeed as wired`
- `fix(ci): pin trivy-action to a tag that exists, and honor the npm install convention`

🔴 **The first one bears directly on an open question from this session.** Three tests
(`ScheduledJobHostTests.StopAsync_…NFR07`, `RetryAndIdempotencyTests.CancellationDuringRetryLoop_…`,
`SseStreamingIntegrationTests.Cancellation_…`) failed on PR #828 with `TimeoutException` /
`TaskCanceledException`. They use `Task.Delay` / `Stopwatch` / wall-clock timeouts — the constructs
[`tests/CLAUDE.md`](../../tests/CLAUDE.md) **bans** as CI-flaky, prescribing `FakeTimeProvider`. **I
never obtained a verdict on whether they are flaky or real** — see §5. The new retry classifier may now
answer that; check it before spending time on them.

---

## 3. Completed this session

### Wave A — 061 + 062 (parallel, `bec4e6792`)

**061 knowledge corpus refresh.** The corpus was not merely stale, it was **actively misleading**: the
2026-05-14 snapshot said container-type CREATE requires an *application permission*. `containerTypes`
supports **no** application permissions at all (403 live, task 010). Anyone designing app-only auth off
that page would build something architecturally impossible — which is what this project spent its first
phase discovering. Also flagged **`agent.chatEmbedAllowedHosts` as fictional** (a prior R2 requirement
doc invented it; absent from both CSDL docs, the SDK model, and all four live container types). The
create-role doc-vs-doc contradiction is recorded with **both** sides verbatim + source URLs, unresolved.

**062 billing handoff.** Issue **#831** + requirement doc on `customer-provisioning-orchestration-r1`
(notes only). The boundary is stated in both documents: SPE Admin **reads** billing and warns;
provisioning is the **sole writer**.

### Wave B — 041 (`86441920d`)

Fixture + guard + 6 tests. **Both hard stops were PROVEN, not merely implemented** — teardown-on-failure
confirmed by a separate live query after a forced mid-test failure; the guard proven by a
credential-free negative+positive control *and* structurally inside the destructive helper.

Verified independently here: **6/6 green in 10 ms with no credentials** (the CI-exclusion criterion),
`[Trait("Category","LiveIntegration")]` with zero `[Category(`, no secret values committed.

🔴 **Found a real production defect → issue #834.** `RegisterConsumingTenantAsync`'s POST returns
`400 apiNotFound` on **both** API versions while a GET on the identical URL succeeds. So
`ConsumingTenantEndpoints.cs`'s POST/PUT/DELETE are suspected non-functional — though the app's Register
button uses a *different* SharePoint-REST path, so the UI may work while those endpoints do not.
Captured as a characterization test that **fails loudly if the defect is ever fixed**.

---

## 4. Orchestration lessons — read before the next wave

⚠️ **`parallel-safe: true` describes the WORK, not the bookkeeping.** Both Wave A POMLs end by writing
the same `TASK-INDEX.md` (061 step 7, 062 step 5). Two agents editing one file concurrently is a
lost-update race. Tell agents to skip it; make the single write in the main session.

⚠️ **An agent silently skipped an instruction and did not report the omission.** Task 041's agent was
told to correct the stale "signed NDAs" wording; it did not, and its report did not mention it. Caught
only because `git status` showed just two new paths. **Verify agent claims against the filesystem — do
not accept the summary.**

⚠️ **A CI observation and a `git push` cannot be interleaved.** Twice this session an in-flight CI run
was cancelled by my own subsequent push, destroying the evidence being gathered. If you need a verdict,
that push must be the last one before waiting.

⚠️ **Stale POML `<status>` fields caused trouble TWICE.** Task 011's said `not-started` while TASK-INDEX
said completed 2026-08-24; 041's dependency block said both its gates were `pending` when both were
complete. **`TASK-INDEX.md` is authoritative.** (041's are now corrected.)

---

## 5. Open questions — not tasks, but they block or mislead

1. **The folders.** `communications` / `emails` / `exports` in `Spaarke Inc`. Nothing in the repo creates
   them by name; the mechanism is path-based upload auto-creating parents from a caller-supplied
   `FolderPath`. **Folders now open (fixed this project), so the `Modified By` column inside them names
   whatever wrote the files.** One click. **Answer before 052 touches anything destructive.**
2. **The three flaky-looking tests** — see §2. Never actually adjudicated.
3. **I2 cross-tenant search bleed** — waived on the deployment (single-tenant `spaarke-records-index`),
   not fixed. `JobContract` has no tenant field. **The waiver expires with the deployment model, not a
   date**: both call paths must be scoped before the first shared tenant is onboarded.
4. **Container-type DELETE does not exist.** Operator asked for it. Graph supports it and refuses when
   containers exist, so blast radius is bounded. Not added unilaterally — a new destructive BFF endpoint
   trips root §10 + §6.
5. **The typecheck gap.** `vite build` does not typecheck `SpeAdminApp`; ~38 errors ship; no test runner.
   Three total client/server shape mismatches reached UAT because of it. ⚠️ **Correcting myself: this is
   NOT a prerequisite for 041/042 — those are pure .NET.** I claimed otherwise this session and was
   wrong.

---

## 6. Verification recipes

```bash
# Client typecheck — NOT in the build, must be run by hand
cd src/solutions/SpeAdminApp && npx tsc --noEmit -p tsconfig.json

# The 041 suite in default mode — MUST be 6/6 green, ~10 ms, zero network
dotnet test tests/unit/Sprk.Bff.Api.Tests/ --filter "FullyQualifiedName~SpeAdmin.ContainerLifecycleLive"
#   NOTE: tests/integration/seam/** is globbed INTO Sprk.Bff.Api.Tests via a <Compile Include>.
#   There is no csproj under tests/integration/seam/ — `dotnet test` there fails with MSB1003.

# Tenant-isolation gate (34/34 expected)
dotnet test tests/Spaarke.ArchTests/ --filter "FullyQualifiedName~TenantIsolation"

# Is a failing ArchTest pre-existing or did I cause it?  Stash and re-run — this settled FR-F1/FR-F2:
git stash -u && dotnet test tests/Spaarke.ArchTests/ --filter "..." ; git stash pop

# Is the deployed BFF actually my build?  RUN BEFORE RE-DIAGNOSING ANY BFF BUG.
TOKEN=$(az account get-access-token --resource https://management.azure.com --query accessToken -o tsv)
L=$(sha256sum deploy/api-publish/Sprk.Bff.Api.dll | cut -d' ' -f1)
curl -s -H "Authorization: Bearer $TOKEN" \
  "https://spaarke-bff-dev.scm.azurewebsites.net/api/vfs/site/wwwroot/Sprk.Bff.Api.dll" -o /tmp/r.dll
[ "$L" = "$(sha256sum /tmp/r.dll | cut -d' ' -f1)" ] && echo MATCH || echo "STALE — redeploy"
```

**Known-failing ArchTests, all pre-existing** (proven by the stash recipe): FR-F1, FR-F2, FR-27 ×2,
ADR-010, ServiceBusClientGuard. Do not attribute them to new work without stashing first.

**Deploys — always `pwsh`, never `powershell`** (5.x lacks `Get-FileHash` here):
- BFF: `rm -rf src/server/api/Sprk.Bff.Api/publish; pwsh -ExecutionPolicy Bypass -File scripts/Deploy-BffApi.ps1`
- Code page: clear `dist/* node_modules/.vite/ .vite/`, then
  `pwsh -ExecutionPolicy Bypass -File scripts/Deploy-SpeAdminApp.ps1 -Environment dev -DataverseUrl "https://spaarkedev1.crm.dynamics.com"`

---

## 7. Preserved history + domain facts

### Live-tenant safety — CORRECTED 2026-08-27

The dev containers hold **TEST documents**, not confidential ones (operator). The prior claim of "signed
NDAs, Compose drafts, matter files" came from a File Browser walkthrough that read filenames and
*inferred* sensitivity. **The throwaway-container rule survives on better reasons than the one it lost:**
repeatability (a destructive suite mutating shared containers is non-idempotent), a shared tenant (other
sessions work `spaarkedev1` concurrently), and evidence preservation (the unresolved folders are a 052
prerequisite). What changed is severity — a teardown failure is a bug, not a catastrophe.

### The recurring defect shape — 17+ confirmed instances

> **A lower layer collapses a real value — or a real failure — into an absent/empty/garbage result that
> an upper layer reads as benign.**

Newest instances: flat wire vs nested `DriveItem` (every folder rendered as a file); a *second*
collection-`ToString()` site the first fix missed; an argument-order swap invisible to the compiler
because all three params were `string`; and an ArchTest scan that **could not tell code from prose** and
reported a CATASTROPHIC violation against a comment warning against that very construct.

**The method that keeps working:** when N things fail identically, find the one that *works* and ask what
it does differently. And when you fix one instance of a shape, **grep for the shape, not the instance**.

### The PATCH-400 was a missing `etag` (2026-08-25)

Two days of "container-type writes are impossible" was a missing **required body property** — `etag`, in
the BODY, **not** the `If-Match` header. An earlier session tried the header, correctly recorded that it
changed nothing, and read that as "the etag is irrelevant", which aimed the whole investigation at auth.
Full record: [`notes/patch-400-resolution.md`](notes/patch-400-resolution.md).

> **THE LESSON, twice in one day:** both 400s were documented requirements returned as `invalidRequest`
> naming no cause, and both were one fetch of Microsoft's reference page away. **The corpus and the CSDL
> being silent is not the platform being silent.**

### Domain facts

- **"Config"** = `sprk_specontainertypeconfig` — binds container type + BU + environment + owning app +
  Key Vault secret name. Labelled "Container Type" in the UI, but it is **not** the container type.
- **ADR-028 E-1** covers per-customer **owning-app** credentials — **not** the BFF's own identity
  reaching Key Vault. auth-v4 applied the exclusion one layer too wide.
- `spaarke-bff-dev` has a **user-assigned** identity (`mi-bff-api-dev`, clientId
  `5967251e-171c-46fe-a6c2-ef843c90309d`) and **no system-assigned one** — every credential MUST pin it.
- Fluent v9: `Divider` defaults to `flex-grow: 1`; `<Text truncate>` does **not** stop wrapping (needs
  `wrap={false}`); `columnSizingOptions` **must be a stable reference** or drags reset every render;
  Fluent does **not** style scrollbars at all.
- **Security alerts 403** — Graph says *"Account is not provisioned"*: the tenant lacks the Defender
  workload. **Not** the missing `SecurityEvents.Read.All` grant our message guesses at. Not fixable in
  code.
- Publish size **45.07 MB** incl. PDBs (baseline 44.96, ceiling 60). Master branch protection is
  **DISABLED** — `--auto` merges immediately, without CI.
