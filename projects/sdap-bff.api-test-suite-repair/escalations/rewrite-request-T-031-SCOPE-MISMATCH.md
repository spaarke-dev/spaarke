# Task 031 Scope-Mismatch Escalation — `Services/Ai/Capabilities/Streaming*` files do not exist

> **Filed by**: Task 031 execution (Phase 2+3 Wave 2.1, 2026-05-31)
> **Escalation type**: §4.8-adjacent — not a >50% rewrite, but a **scope-mismatch / glob-with-zero-hits**. Filed to make the no-op disposition visible per project §6.2 trait-tagging discipline + NFR-02 rewrite-not-repair governance.
> **Decision requested**: Owner sign-off on **NO-OP completion** of task 031.

---

## Headline

**Task 031's named file glob (`tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Capabilities/Streaming*.cs` AND `…/*StreamingTests.cs`) matches ZERO files in the worktree.**

The `Services/Ai/Capabilities/` directory contains exactly 5 test files — none of them are streaming tests:

```
tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Capabilities/
  CapabilityRouterBenchmarkTests.cs        (Layer-1 keyword router benchmark; no IChatClient)
  CapabilityRouterTests.cs                  (unit tests for CapabilityRouter; no IChatClient)
  CapabilityValidatorTests.cs               (validator unit tests; no IChatClient)
  DataverseCapabilityManifestLoaderTests.cs (Dataverse loader unit tests; no IChatClient)
  ManifestRefreshServiceTests.cs            (refresh service unit tests; no IChatClient)
```

`dotnet test --filter "FullyQualifiedName~Services.Ai.Capabilities.Streaming" --no-build` returns:

```
No test matches the given testcase filter `FullyQualifiedName~Services.Ai.Capabilities.Streaming`
in C:\…\Sprk.Bff.Api.Tests.dll
```

---

## Where the streaming tests actually live

Per `find tests/unit/Sprk.Bff.Api.Tests -iname "*stream*"`:

| Path | Cluster owner |
|---|---|
| `Api/Ai/AnalysisStreamChunkTests.cs` | API-tier (not Services/Ai/Capabilities) |
| `Infrastructure/Streaming/` (directory) | Infrastructure tier (not Services/Ai/Capabilities) |
| `Integration/SseStreamingIntegrationTests.cs` | `Integration.SseStreamingIntegrationTests` cluster (8 failures, absorbed by integration-tier task per failure inventory) |
| `Services/Ai/Chat/StreamingWriteIntegrationTests.cs` | **Task 030's scope** (`Services/Ai/Chat/Streaming*`) — sibling batch 1 of IChatClient cluster |

There is **no** `Services/Ai/Capabilities/Streaming*` file anywhere in the worktree at task 031 dispatch time (2026-05-31, post-Wave-1.3 authoritative baseline).

---

## Failure inventory confirmation

Per [`baseline/failure-inventory-post-018-2026-05-31.md`](../baseline/failure-inventory-post-018-2026-05-31.md), the only `Services.Ai.Capabilities.*` failures are:

| Class | Failures | Streaming-related? |
|---|---:|---|
| `Services.Ai.Capabilities.CapabilityRouterBenchmarkTests` | 2 | **No** — corpus-routing assertion failures (`falsePositiveCount.Should().Be(0)` at line 191; `confidentWrong.Should().Be(0)` at line 320). Layer-1 keyword classifier benchmark, no `IChatClient.GetStreamingResponseAsync` consumption. |

The 2 CapabilityRouterBenchmarkTests failures are **NOT** in scope for task 031 because they are not IChatClient streaming failures. They belong to a future Services.Ai.* (non-Safety) absorbing task per the 22-test rollup in [`failure-inventory-post-018-2026-05-31.md`](../baseline/failure-inventory-post-018-2026-05-31.md) §"Failures by namespace prefix — roll-up" (Services.Ai.* non-Safety: 22 failures, includes "Capabilities.CapabilityRouterBenchmark (2)").

---

## Root cause hypothesis (informational, not actionable for task 031)

The POML at lines 16-17, 41-42, 87-90, 112 prescribes the file glob `Services/Ai/Capabilities/Streaming*.cs`. The most likely origin is that the design.md §3.4 IChatClient cluster estimate ("~30-50 tests") was forecasted to span both `Services/Ai/Chat/Streaming*` AND `Services/Ai/Capabilities/Streaming*` at design time, but the Capabilities-tier streaming tests were either:

1. Never authored (the `Services/Ai/Capabilities/` tree only ever held the 5 non-streaming files above), OR
2. Authored under a different path (e.g., `Services/Ai/Chat/Streaming*` covers all of them, making task 030's glob the canonical IChatClient streaming cluster)

Either way, task 031's name-glob has no work to do in the current worktree.

---

## Disposition requested

**NO-OP completion**. Task 031 outputs:

- **0 files modified**
- **0 traits applied**
- **0 escalations of the §4.8 >50%-rewrite kind** (no file was touched; nothing to escalate)
- **1 escalation of this kind** (THIS file — scope-mismatch / glob-with-zero-hits)
- **0 new entries in `ledgers/real-bug-ledger.md`** (no production bugs surfaced — no tests were inspected end-to-end)

Build still clean: `dotnet build tests/unit/Sprk.Bff.Api.Tests/ -c Release` → **0 errors / 2 pre-existing warnings (Kiota CVE)** (verified 2026-05-31 by this task execution).

Targeted filter cleanly returns no matches: `dotnet test --filter "FullyQualifiedName~Services.Ai.Capabilities.Streaming" --no-build` → **"No test matches the given testcase filter"** (verified 2026-05-31 by this task execution).

Git status verified post-execution: zero modifications to `src/`, `power-platform/`, `infra/`, `scripts/`, `tests/`, or `CustomWebAppFactory.cs`. NFR-01 / §4.5 / NFR-03 / NFR-11 all preserved (none touched).

---

## Required approval

| Owner | Decision required | Status |
|---|---|---|
| Project owner | Approve NO-OP completion of task 031; confirm the 2 `CapabilityRouterBenchmarkTests` failures (lines 191 + 320) are absorbed elsewhere (likely a future Services.Ai.* non-Safety batch) | **PENDING** |
| Orchestrator | Confirm sibling batch 030 (`Services/Ai/Chat/Streaming*`) is the canonical IChatClient streaming task and task 031 was a planning over-estimate of cluster span | **PENDING** |
| Verification task 032 | Acknowledge that "cluster overall" verification covers task 030 alone for the IChatClient streaming cluster (task 031 contributed nothing) | **PENDING** |

---

## Per project CLAUDE.md output expectations

Per the orchestrator brief output expectation for this task: the verbal report (under 300 words) will state NO-OP per-file traits + counts (0/0), this single §4.8-adjacent scope-mismatch escalation, 0 `real-bug-pending-fix` entries, the targeted `dotnet test --filter` result ("No test matches"), the full build result (0/2), POML status (not changed — owner will dispose after sign-off), and Step 9.5 quality gates (PASS by triviality — no code touched).

This escalation file IS the §4.8-adjacent disposition record; no factory edit, no helper edit, no `[Trait(...)]` application, no `git commit` made by task 031.
