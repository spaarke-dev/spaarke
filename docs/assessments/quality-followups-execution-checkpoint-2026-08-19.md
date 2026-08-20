# Execution Checkpoint — Quality Follow-ups Ready to Run

> **Created**: 2026-08-19 · **Purpose**: hand a fresh session a set of **executable, self-contained tasks** with enough context to finish them without re-deriving anything.
> **Parent**: [Epic #427 — Code Quality](https://github.com/spaarke-dev/spaarke/issues/427)
> **Scope note**: everything here is a **task**, not a project. Work that needs its own project is deliberately excluded — see [`test-suite-skipped-tests-assessment-2026-08-19.md`](test-suite-skipped-tests-assessment-2026-08-19.md).

---

## Context — what already landed (do not redo)

Master was red on three separate gates. All fixed and merged 2026-08-19:

| PR | What |
|---|---|
| #787 | `dotnet format` whitespace (26 files) **+** `.gitattributes` CRLF rule — the actual root cause |
| #788 | Removed a flaky single-sample `Stopwatch` perf assertion (redundant with a proper 100-sample canary) |
| #789 | Repaired 10 red tests — `Ciam:*` fixture config + 3 stale contract tests + GridOverview config shape |
| #793 | `Client Quality` gate — built the missing `Spaarke.Communication.Components` dist + added a required `assistantContract` |
| #796 | Routed the last two `ScheduledJobHost` sleeps through `TimeProvider` (groundwork only) |

**Master state**: `Format`, `Client Quality`, `Build & Test` all green. Working tree clean.

Two traps worth knowing before you start:

1. **A cancelled workflow run reports every job as `fail`.** Several "failures" during this work were superseded-run artifacts. Always check `gh run view <id> --json conclusion` before diagnosing a red check.
2. **Building a shared client lib without its dependency's `dist` produces a convincing false "API drift" signal** — missing exports that do exist. Replicate CI's build order before believing it.

---

## T1 — Correct the `FakeTimeProvider` documentation claim

**Issue**: [#795](https://github.com/spaarke-dev/spaarke/issues/795) · **Size**: ~15 min · **Do this FIRST** — T2 depends on it.

Two docs assert the testing package needs no reference. Both are wrong.

- [`docs/standards/TEST-ARCHITECTURE.md`](../standards/TEST-ARCHITECTURE.md) §4 "Wiring" — *"already a transitive dependency through `Microsoft.Extensions.Hosting`. No new package needed."*
- [`tests/CLAUDE.md`](../../tests/CLAUDE.md) frameworks table — lists `FakeTimeProvider` with no package guidance.

**Fact**: `Microsoft.Extensions.TimeProvider.Testing` appears in **no `.csproj` in the repo**. `FakeTimeProvider` does not resolve. Verified by `dotnet add package` resolving **10.9.0** as a genuinely new reference. (The `TimeProvider` *abstraction* is in the BCL — likely the source of the confusion — but the **testing** package is separate.)

**Done when**: both docs name the package and state it must be added per test project; consider adding it to the test projects that will need it so the documented path actually works.

---

## T2 — Make `Spaarke.Scheduling` deterministically testable

**Issue**: [#790](https://github.com/spaarke-dev/spaarke/issues/790) · **Size**: ~1 day · **Depends on**: T1

### Why it matters (not a test-hygiene nicety)

`ScheduledJobHost` runs two jobs: the hourly **notification-playbook scheduler** (produces Daily Briefing and other scheduled AI output) and the nightly **membership reconciliation** — which the code itself calls *"the load-bearing path"* for keeping the membership junction table fresh.

That junction decides what appears in every caller-membership-scoped grid ("my matters/documents/events"), and results are cached in **Redis with a 5-minute TTL**. So a reconciliation killed mid-run leaves the junction **partially updated**, and Redis then serves that partial state as authoritative. The notification path fails cleanly by comparison (no outbox row → no ping → nothing half-delivered).

NFR-07's 30-second drain guarantee is what prevents that, and the BFF **blue-green swaps on every release** — so this path runs on every deploy. Its only current test is the wall-clock one that flakes.

### State of the code

Already true (don't redo — merged in #796 and earlier):
- ctor takes `TimeProvider? timeProvider = null`, defaults to `TimeProvider.System`
- **all 5 clock reads** use `_timeProvider.GetUtcNow()`
- **every sleep** now uses `Task.Delay(_, _timeProvider, ct)`

### The blocker

**Job dispatch does not fire under a `FakeTimeProvider`**, despite the above. Three approaches failed:

1. Frozen clock → no cron tick at all (the cron fires on *virtual* seconds).
2. Pumping `Advance(1s)` in a loop with real yields between advances.
3. Settling ~250ms first so the host registers its timer before the first `Advance` (the classic FakeTimeProvider registration race), with larger yields.

Each failed at `startedTcs` — the job never started.

**Start here**: how Cronos' `GetNextOccurrence` behaves against `FakeTimeProvider`'s epoch (default 2000-01-01), and whether any timer is registered outside `_timeProvider`.

### Target test shape (already designed — reuse it)

Prove the invariant **by construction**, not by wall clock: advance virtual time only until the first attempt runs, then **freeze**. The retry sleep then has infinite virtual duration, so a completing `StopAsync` can *only* be explained by cancellation. Strictly stronger than the old threshold, and a slow runner cannot change the outcome — it only takes longer to reach the same conclusion. Use `WaitAsync` purely as a deadlock guard, never as a performance budget.

### Done when

- The cancellation test is deterministic with no wall-clock assertion and no `_isCi` headroom multiplier.
- The **8 tests skipped with `"needs TimeProvider refactor (see PR #415)"`** are un-skipped and passing.
- The remaining `Stopwatch`-assertion sites are swept: `RetryAndIdempotencyTests.cs` (139, 212, 333), `ScheduledJobHostTests.cs` (225, 566), `SseStreamingIntegrationTests.cs` (92, 775), `SystemIntegrationTests.cs` (177), `AsyncEnumerableHelpersTests.cs` (221).
- **Leave `TransitiveMembershipPerfTests` alone** — its `Stopwatch` use is the sanctioned perf canary (100 samples, p95, documented hard/soft gates).

---

## T3 — Fix the managed-identity flag gating defect

**Issue**: [#791](https://github.com/spaarke-dev/spaarke/issues/791) item 1 · **Size**: ~1–2 h · **Live defect on dev**

`DataverseAccessDataSource.cs:53` and `DataverseWebApiClient.cs:42` **never read** `Graph:ManagedIdentity:Enabled`. Secret *presence* alone selects the code path — so on dev, where `API_CLIENT_SECRET` is set, both run on the **client secret despite MI being enabled**. Every other Dataverse path is flag-gated.

**Done when**: both honour the flag consistently with `DataverseServiceClientImpl` / `DataverseWebApiService`, with a test covering flag-on-with-secret-present.

---

## T4 — Fix the confidential-client DI lifetime hazard

**Issue**: [#791](https://github.com/spaarke-dev/spaarke/issues/791) item 2 · **Size**: ~2–3 h

`DataverseAccessDataSource` is a **transient** typed HttpClient (`SpaarkeCore.cs:39`) and `AgentTokenService` is **scoped** (`AgentModule.cs:24`) — each builds a fresh MSAL `IConfidentialClientApplication` per request, discarding its token cache.

**Reference implementation**: `DataverseUserClient.cs:55-56,91` already does this correctly with a process-wide static CCA cache keyed `(tenant|client)`.

Independently correct today, **and** a hard prerequisite for auth-v4 — client assertions require shared clients.

---

## T5 — ADR-028 E-2 re-test (possible quick secret elimination)

**Issue**: [#791](https://github.com/spaarke-dev/spaarke/issues/791) item 6 · **Size**: ~30 min

E-2 documents Azure OpenAI falling back to an API key after persistent MI 401s. Microsoft's current docs identify the root cause of *exactly that symptom* as a **missing custom subdomain** (regional endpoints reject Entra tokens).

**Check whether `spaarke-openai-dev` has a custom subdomain first.** If it does not, this may be a one-config-change secret elimination — restore-to-MI is already a single setting per the ADR (clear `AzureOpenAI__ApiKey`).

---

## T6 — Documentation, infrastructure and secret hygiene

**Issue**: [#791](https://github.com/spaarke-dev/spaarke/issues/791) items 3–5, 7 · **Size**: varies; several are operator tasks

- `docs/architecture/auth-azure-resources.md` claims a **system-assigned** MI; it has been **user-assigned** (`mi-bff-api-dev`) since 2026-05-24. Same doc contradicts itself on which app registration owns `BFF-API-ClientSecret`. **Portal-confirm before automating any rotation.**
- `infrastructure/bicep/stacks/dev.bicepparam:12` declares **`B1`**; live `spaarke-dev-plan` is **`P1v3`**. Master IaC creates only a system-assigned identity; the UAMI module is on the unmerged provisioning branch.
- A **live Service Bus SAS key** sits in a local `appsettings.Development.json` (gitignored, but rotate-worthy).
- Duplicate lowercase Key Vault alias **`bff-api-client-secret`** (Office add-in deploy) plus an orphaned `Graph-API-ClientSecret`. Any rotation ignoring the alias breaks the add-in.
- `BaseProxyPlugin.cs:121-124` reads **plaintext secrets from Dataverse columns** — outside Key Vault entirely.
- Provisioning `design.md:1006` §9.1 states both readings of app-registration tenancy in consecutive sentences; scope the multitenant + consent mechanism explicitly to **Model 1**.

---

## T7 — Resolve the `pdfjs-dist` version drift

**Not yet filed** — file it when you pick it up, or fold into whichever PR touches those packages.

Running `npm install` in `Spaarke.AI.Widgets` and `Spaarke.Communication.Components` rewrote a `pdfjs-dist` range from `^5.7.284` to `^6.2.108` in both `package-lock.json` files. **I reverted it** rather than carry a major bump in an unrelated CI fix.

I do **not** understand the mechanism — `package.json` still declares `^5.7.284`, so the lock rewrite may be transitive. Someone should determine whether this is an intended bump before it lands accidentally in an unrelated PR.

---

## Suggested order

**T1 → T5 → T3 → T4 → T2 → T6 → T7**

T1 is a prerequisite. T5 is a possible 30-minute win worth checking early. T3/T4 are contained code fixes that also de-risk auth-v4. T2 is the long one. T6 is largely operator work. T7 needs a decision more than an edit.

## Explicitly NOT in this checkpoint

- **#794** — the 168-skipped-test problem. Needs its own project; see the companion assessment.
- **auth-v4** — has a worktree and a design; runs in its own session.
