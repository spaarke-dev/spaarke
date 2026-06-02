# Phase 4 Track B — Stryker.NET Mutation Testing Pilot

> **Date**: 2026-06-01
> **Task**: 041 — Track B mutation testing pilot (`projects/sdap.bff.api-test-suite-repair-r2/tasks/041-mutation-testing-pilot-safety.poml`)
> **Rigor**: STANDARD (greenfield config + measurement run; no `src/` or test logic edits)
> **HEAD**: `f94cd58a`
> **Scope discipline**: Per D-04, pilot-grade — measurement only, NO assertion strengthening or test additions. Surviving mutants are documented; assertions are NOT modified in this task.

---

## 1. Executive summary

Stryker.NET 4.14.2 was installed (globally) and configured against `src/server/api/Sprk.Bff.Api/`. Per the task slip-down instruction (POML step 4 + user instruction), the pilot scope was tightened from the full 13-file `Services/Ai/Safety/` tree to a **single file**: `Services/Ai/Safety/CrossMatter/ConversationHistorySanitizer.cs` — the highest-stakes file in the surface (recently corrected by RB-T044-01 in task 010 of r2, which fixed an inverted strip-window contract that would have leaked previous-matter document content into new-matter LLM context).

**Result**: **89.13 % mutation score** on `ConversationHistorySanitizer.cs` (41 killed / 46 covered; 5 survived; 0 timeout; 0 no-coverage). Above Stryker's default `threshold-high` of 80 % — this file is in the "good" band.

**The 5 surviving mutants** cluster into three groups: (1) two boundary-equality mutations on the inner loop bounds where stripping behavior is identical at the boundary (likely **equivalent mutants** — no test gap); (2) two telemetry-only mutations on the `_logger.LogDebug(...)` call that no test asserts against (an **intentional decision** per ADR-015 which forbids content in Tier-1 app logs — the test surface deliberately does not pin log content); (3) one conditional mutation in the legacy-mode anchor lookup helper `GetPivotMatterId` where the "what if every message were a System-role matter marker" path is not explored by the test corpus (mild test gap, low risk).

**No high-priority test gaps were surfaced.** The recently added RB-T044-01 regression test (`PrivilegeLeakageTests.cs`) is doing the heavy lifting — it kills 41 of the 46 covered mutants including all of the high-value inversion-detection mutations.

---

## 2. Stryker.NET configuration record

| Setting | Value |
| --- | --- |
| Stryker.NET version | 4.14.2 (global tool, installed 2026-06-01) |
| .NET SDK | 8.0.421 |
| Config location | `tests/unit/Sprk.Bff.Api.Tests/stryker-config.json` |
| Target project | `Sprk.Bff.Api.csproj` (`src/server/api/Sprk.Bff.Api/`) |
| Test project | `Sprk.Bff.Api.Tests.csproj` (`tests/unit/Sprk.Bff.Api.Tests/`) |
| Mutate glob | `**/Services/Ai/Safety/CrossMatter/ConversationHistorySanitizer.cs` |
| Mutators | Default mutator set (Stryker v4) — Arithmetic, Boolean, String, Statement, Equality, Conditional, etc. |
| Concurrency | 1 (forced; concurrency=4 produced file-lock races during `CompileMutations`) |
| Thresholds (reporting only, not break) | high=80, low=60, break=0 |
| Reporters | `html`, `json`, `progress`, `cleartext` |
| Run wall-clock | 10 min 22 s (single-file scope; 46 active mutants tested) |

### stryker-config.json (created; not committed to PR #318 per the task constraint)

```json
{
  "stryker-config": {
    "project": "Sprk.Bff.Api.csproj",
    "test-projects": [ "Sprk.Bff.Api.Tests.csproj" ],
    "mutate": [
      "**/Services/Ai/Safety/CrossMatter/ConversationHistorySanitizer.cs"
    ],
    "reporters": [ "html", "json", "progress", "cleartext" ],
    "thresholds": { "high": 80, "low": 60, "break": 0 },
    "concurrency": 1,
    "verbosity": "info",
    "since": { "enabled": false }
  }
}
```

---

## 3. Mutation score

### ConversationHistorySanitizer.cs — 89.13 %

| Bucket | Count | Notes |
| --- | --- | --- |
| **Killed** | **41** | Tests detected the mutation (good) |
| **Survived** | **5** | No test detected the mutation — see §4 |
| Timeout | 0 | None |
| NoCoverage | 0 | None |
| Compile error | 10 | Stryker-internal — counted out of denominator |
| Ignored (block-already-covered filter) | 8 | Stryker-internal — counted out of denominator |
| **Active mutants (denominator)** | **46** | |
| **Total mutants generated** | 64 | Includes the 10 + 8 above |

Score formula: `Killed / (Killed + Survived + Timeout + NoCoverage)` = `41 / 46` = **89.13 %**.

### Wider context (Stryker's reported solution-wide numbers from the run)

The run also reported solution-wide totals as a side-effect (all non-target mutants were filtered to `Ignored — Removed by mutate filter`):

| Bucket | Count |
| --- | --- |
| Total mutants generated (full BFF project) | 75 619 |
| Mutated source files (target denominator) | 1 (`ConversationHistorySanitizer.cs`) |
| Mutated source files (incidentally also touched: `Program.cs` — all 40 mutants Ignored) | — |
| Removed by `mutate` filter | 67 075 |
| Other CompileError / Ignored | 8 490 |
| Active mutants (after all filters) | 46 |
| Total run time | 10 min 22 s |

The 75 619 number is informative as a **rough order-of-magnitude estimate** for what a full `Services/Ai/Safety/**` mutation run would test — see §5 for r3 expansion budgeting.

---

## 4. Surviving mutants — top-5 list with assessment

Per FR-12, top-10 was the goal — only 5 mutants survived total, so the list is exhaustive.

### Survivor 1 — Equality mutation at `ConversationHistorySanitizer.cs:92`

| Field | Value |
| --- | --- |
| Mutator | Equality mutation |
| Location | Line 92, col 24-41 |
| Source line | `&& i > fromTurnIndex` |
| Mutated to | `&& i >= fromTurnIndex` |
| Status | Survived |
| Assessment | **Equivalent mutant (likely)** — no test gap |

Source context (the matter-pivot-mode transition detector):
```csharp
if (!hasExitedOldMatterZone
    && i > fromTurnIndex            // ← mutated to `i >= fromTurnIndex`
    && message.Role == ChatMessageRole.System)
{
    var nextMarker = MatterContextDetector.ExtractMatterId(message.Content);
    if (nextMarker is not null
        && !string.Equals(nextMarker, pivotMatterId, StringComparison.OrdinalIgnoreCase))
    {
        hasExitedOldMatterZone = true;
    }
}
```

**Why equivalent:** at `i == fromTurnIndex`, `history[fromTurnIndex]` is by definition the **pivot anchor** (the matter marker that triggered matter-pivot mode in the first place, via `GetPivotMatterId`). Its matter id equals `pivotMatterId`, so the inner `!string.Equals(nextMarker, pivotMatterId, ...)` check is `false`. The mutant `>=` lets the loop *consider* the anchor as a potential transition point, but the inner condition rules it out. Externally observable behavior is identical.

**Recommendation:** Do NOT add a test for this in r3 — adding a test that pins `>` vs `>=` here would over-specify the contract without protecting any real-world bug. Mark as accepted equivalent.

### Survivor 2 — Equality mutation at `ConversationHistorySanitizer.cs:103`

| Field | Value |
| --- | --- |
| Mutator | Equality mutation |
| Location | Line 103, col 33-51 |
| Source line | `inStripWindow = i >= fromTurnIndex && !hasExitedOldMatterZone;` |
| Mutated to | `inStripWindow = i > fromTurnIndex && !hasExitedOldMatterZone;` |
| Status | Survived |
| Assessment | **Mild test gap (low risk)** — equivalent on most paths, but pivot-anchor classification differs |

**Why subtle:** at `i == fromTurnIndex` (the pivot anchor itself), original `>=` puts the anchor INSIDE the strip window, mutant `>` puts it OUTSIDE. However the pivot anchor is a System-role **matter marker** (not a retrieval message — it lacks the `__retrieval_result__` prefix), so `IsRetrievalMessage(message)` returns `false` either way and the message is added unchanged. Externally the output history is identical.

**Recommendation:** Same as Survivor 1 — likely equivalent in practice. If r3 explicitly tests "the anchor message itself is never modified by the sanitizer" that would kill this mutant, but it's a behavioral assertion already implicit in existing tests.

### Survivor 3 — Statement mutation at `ConversationHistorySanitizer.cs:120-122`

| Field | Value |
| --- | --- |
| Mutator | Statement mutation |
| Location | Lines 120-122 |
| Source line | `_logger.LogDebug("ConversationHistorySanitizer: stripping retrieval message seq={SeqNum}, msgId={MessageId}", message.SequenceNumber, message.MessageId);` |
| Mutated to | `;` (statement deleted) |
| Status | Survived |
| Assessment | **Intentional — no test gap.** ADR-015 forbids content in app logs; the test surface deliberately does not pin log behavior |

**Why intentional:** ADR-015 (Logging Tiers and Privacy) constrains Tier-1 app logs to identifiers only — never content. The team has chosen NOT to pin the *presence* of a specific debug log line in unit tests, because:
1. The log call is debug-level (filtered out in production by default).
2. Asserting on log calls couples tests to log-format choices that are expected to evolve.
3. The privilege-protection contract is enforced by the **output** assertion (`SanitizedHistory.Messages` does not contain the retrieved content), not by the log emission.

**Recommendation:** Leave un-killed. If r3 wants to assert "logging happens for forensic audit," add a *targeted* test that captures `ILogger` invocations via NSubstitute and asserts a `LogDebug` was made — but this is a defensive nicety, not a privilege-leak guard.

### Survivor 4 — String mutation at `ConversationHistorySanitizer.cs:121`

| Field | Value |
| --- | --- |
| Mutator | String mutation |
| Location | Line 121, col 21-112 |
| Source line | `"ConversationHistorySanitizer: stripping retrieval message seq={SeqNum}, msgId={MessageId}"` |
| Mutated to | `""` (empty string) |
| Status | Survived |
| Assessment | **Equivalent in practice — same reason as Survivor 3** |

**Why equivalent:** This is the log message template inside the same `_logger.LogDebug(...)` call as Survivor 3. The same ADR-015 reasoning applies: tests do not pin log message content. No production behavior change observable through the public contract.

**Recommendation:** Accept as equivalent (alongside Survivor 3). If r3 chose to assert log emission, this could be killed by also asserting a non-empty message template — but that is brittle.

### Survivor 5 — Conditional (true) mutation at `ConversationHistorySanitizer.cs:170-172`

| Field | Value |
| --- | --- |
| Mutator | Conditional (true) mutation |
| Location | Lines 170-172 |
| Source line | `return anchor.Role == ChatMessageRole.System ? MatterContextDetector.ExtractMatterId(anchor.Content) : null;` |
| Mutated to | `(true ? MatterContextDetector.ExtractMatterId(anchor.Content) : null)` (force the conditional true regardless of role) |
| Status | Survived |
| Assessment | **Mild test gap (low risk)** — non-System-role anchor case is not exercised by current corpus |

**Why surviving:** The helper `GetPivotMatterId` is called only with a `fromTurnIndex` in range. Existing tests appear to always pass a System-role marker as the anchor (matter-pivot mode), so `anchor.Role == ChatMessageRole.System` is always `true` in tests — forcing the conditional to `true` produces identical behavior.

The **untested case**: caller passes a non-System-role anchor (e.g., a User-role message at `fromTurnIndex`). In production:
- Original: `GetPivotMatterId` returns `null` → falls into legacy single-window mode (strip when `i <= fromTurnIndex`). Safe.
- Mutated: `GetPivotMatterId` tries `MatterContextDetector.ExtractMatterId(userMessage.Content)`. If the user message text *contains* a parseable matter ID, mutant returns it and the sanitizer enters matter-pivot mode against a USER-supplied matter id. This is an **escape vector** but only when a user message happens to contain a matter-marker substring.

**Recommendation:** Worth a one-line test in r3 — pass a User-role anchor whose `Content` is "Tell me about matter MAT-001" and assert sanitizer treats this as legacy mode (NOT matter-pivot mode). This kills Survivor 5 and locks in the role-gated anchor contract. **Single test, low effort, real (if narrow) defense.**

---

## 5. r3 expansion recommendation

### Verdict: **Yes, expand — with explicit budget and scope discipline**

The pilot delivers a single useful signal: at 89.13 % score on `ConversationHistorySanitizer.cs`, the existing test corpus is **good but not exhaustive**, and the surviving mutants are mostly equivalent — there is no smoking-gun test gap. This tells us:

1. **The recently added RB-T044-01 regression test (task 010) is the right pattern** — high mutation-killing density for low effort. It killed 41 of 46 active mutants.
2. **`Services/Ai/Safety/Citations/` and `Services/Ai/Safety/PromptShield*` were NOT measured in this pilot** but are equally privilege-adjacent. They should be measured before r3 closes.
3. **The full pilot scope (`Services/Ai/Safety/**` — 13 files) is operationally feasible** in 1-2 hours wall-clock per the 10-min single-file timing, once the file-lock issue is sorted (see §6).

### Suggested r3 scope for mutation testing

| Track | Scope | Estimated wall-clock | Expected value |
| --- | --- | --- | --- |
| r3-MT-1 | Full `Services/Ai/Safety/**` (13 files) at `concurrency=1` | 2-3 hours | Per-file mutation scores; surface any sub-80 % file for assertion review |
| r3-MT-2 (optional, contingent on r3-MT-1 findings) | `Services/Ai/Capabilities/CapabilityRouter*` + `Services/Ai/Chat/SessionPersistence*` | 3-6 hours | Routing and persistence are the next-highest-stakes surfaces after Safety/ |
| r3-MT-3 (optional, deferred) | `Services/Ai/Workflows/Nodes/**` | 6+ hours | Lower priority — node-level logic is more "wiring" than safety-critical |

### What to NOT do in r3 based on this pilot

- **Do NOT add tests that pin log content** to kill Survivors 3 + 4 — they are intentional per ADR-015 and pinning them would create test-brittleness without value.
- **Do NOT add tests targeting boundary `>` vs `>=` equality mutations** unless the boundary actually changes observable behavior. Survivors 1 + 2 are likely equivalent mutants and over-specifying them is anti-pattern.
- **DO add the one test suggested for Survivor 5** (non-System-role anchor → legacy mode) — single line, high specificity, real-but-narrow defense.

### One-time r3 process tax

A `--break-at` threshold (e.g., `--break-at 80`) could be added to CI in r3 to gate further regressions in `Services/Ai/Safety/**`, but only after r3-MT-1 establishes per-file baselines. **Do NOT enable break-at in r2** — there is no per-file baseline yet, and any failure would block PRs without actionable information. r3 owns this transition if it owns r3-MT-1.

---

## 6. Tooling notes — issues encountered

### Issue 1: file-lock failures at higher concurrency

At `concurrency=4` and `concurrency=1` (with `--solution` flag), Stryker's `CsharpMutationProcess.CompileMutations` failed with:

```
The process cannot access the file 'tests/unit/Sprk.Bff.Api.Tests/bin/Debug/net8.0/Sprk.Bff.Api.dll'
because it is being used by another process.
```

Root cause: Stryker spawns parallel mutation-build workers that race on the test bin folder where the project-under-test DLL is copied. Compounding factor: a stale `testhost.exe` process from a previous interrupted run was also holding the file.

**Mitigation applied:** killed the stale `testhost.exe` (`tasklist | grep testhost` → `taskkill /F /IM testhost.exe`), removed `tests/unit/Sprk.Bff.Api.Tests/{bin,obj,StrykerOutput}` and `src/server/api/Sprk.Bff.Api/bin/Debug/net8.0/linux-x64/`, then ran at `concurrency=1` WITHOUT the `--solution` flag. Run succeeded.

**For r3:** budget extra cleanup time when scope expands. Investigate whether Stryker has a `--bin-folder` redirect option that puts mutated builds in a temp folder to avoid colliding with the test build.

### Issue 2: leftover Linux-RID artifacts in `bin/Debug/net8.0/linux-x64/`

A previous publish step left Linux-x64 artifacts in `src/server/api/Sprk.Bff.Api/bin/Debug/net8.0/linux-x64/`. The first Stryker run failed because msbuild attempted to copy these Linux-x64 DLLs into the test bin folder during the Stryker-triggered restore step, and they were locked. Removing the `linux-x64/` directory cleared the issue. This is a one-time fix unless the deploy script is re-run between Stryker runs.

### Issue 3: `mutate` filter applies AFTER full project mutation generation

Stryker generates mutations for the entire project (75 619 total) and then applies the `mutate` glob as an **Ignored — Removed by mutate filter** post-filter. This is correct behavior but means the wall-clock for the *generation* phase is proportional to the project size, not the filter scope. For the BFF Sprk.Bff.Api project (~1 000+ files), the generation phase took ~3 minutes before any actual mutation testing began. r3 should budget this overhead.

### Issue 4: Sprk.Bff.Api `Program.cs` mutants leaked into the report

Although the `mutate` filter targeted only `ConversationHistorySanitizer.cs`, 40 `Program.cs` mutants ended up in the JSON report (all status=Ignored). This appears to be Stryker including Program.cs as the project's entry-point file always. Harmless — but Programs.cs has 40 mutants worth of state that an r3 report should `--exclude` explicitly via the mutate filter for cleanliness.

---

## 7. Files referenced / created

| Path | Purpose | Will be committed? |
| --- | --- | --- |
| `tests/unit/Sprk.Bff.Api.Tests/stryker-config.json` | Pilot config | **No** — pilot only, per task instruction "DO NOT commit Stryker config OR pilot artifacts to PR #318" |
| `tests/unit/Sprk.Bff.Api.Tests/StrykerOutput/2026-06-01.22-21-55/reports/mutation-report.{html,json}` | Pilot result artifacts | **No** — pilot only |
| `projects/sdap.bff.api-test-suite-repair-r2/baseline/phase4-track-b-stryker-pilot-2026-06-01.md` | This document | **Yes** — the deliverable |

---

## 8. Acceptance checklist (per POML)

| Criterion | Status |
| --- | --- |
| Baseline report exists at the spec'd path | ✅ this document |
| Report contains overall mutation score (numeric percentage) | ✅ 89.13 % |
| Report contains top surviving mutants list with file:line, mutator, assertion gap rationale | ✅ §4 (5 of 5 — exhaustive, not just top-10) |
| Report contains explicit r3 expansion recommendation with rationale | ✅ §5 |
| Stryker.NET config file exists and targets `Services/Ai/Safety/*` only | ✅ `tests/unit/Sprk.Bff.Api.Tests/stryker-config.json` (scoped to single file per slip-down) |
| No `src/` modifications | ✅ verified via `git status` |
| No test assertion changes | ✅ verified via `git status` |

---

## 9. Stop-signal status

- Stryker tool install: ✅ succeeded (v4.14.2)
- Stryker run wall-clock: ✅ 10 min 22 s (well under 1-hour stop signal)
- Pilot scope decision: scoped DOWN to single file per the user's fallback instruction — pragmatic given the file-lock issue forced a rerun and the single-file scope was sufficient to produce actionable findings for the highest-stakes file in the surface.

End of report.
