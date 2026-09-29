# The 88 skipped tests were five problems, not one

> **2026-08-31.** Closes the live-service deletion arc opened by PR #912.
> Owner instruction: *"delete them — we do not want 'human in the loop' dependent running ci tests;
> if there are others we need to remove them."*

---

## 1. The finding

The handoff note framed the remaining skips as *"~35 live-service skips in partial files"*. That was
wrong, and the error came from grouping by **file** instead of by **skip reason**. Grouped by reason,
the 88 split into five categories with different causes and different correct treatments — only one
of which is the owner's target:

| # | Category | Count | Correct treatment |
|---|---|---:|---|
| **A** | **Live-service dependent** — needs real Dataverse / Redis | **21** | **DELETE** (this PR) |
| B | *"Requires fully mocked X"* — endpoint 404s because the service isn't registered in `CustomWebAppFactory` | ~35 | **Wire the factory** — this is a test-infrastructure gap, and it is what PR #894 addresses. Deleting them destroys real coverage. |
| C | Stale assertions — signature/format drift (`Render` 6-param overload, `AgentCostControlMiddleware` ctor, …) | ~11 | Fix or delete per test; each needs reading |
| D | CI timing flakes — *"passes locally"*, pre-existing | 5 | Belongs in a perf lane, not deletion |
| E | Endpoint genuinely not implemented | 2 | Delete with the feature decision, not before |
| F | `Graph SDK sealed classes cannot be mocked with Moq` | 4 | Needs `IGraphClientWrapper` or WireMock — a production seam, not a test edit |

**Had the 88 been deleted as one batch — the reading the handoff note invited — categories B–F would
have gone with them: ~57 tests, most of them recoverable coverage.** Category B alone is the exact
set PR #894 is wiring back up.

The lesson is the same one this project learned six times in the classifier rounds: **the grouping
key determines the answer.** File was the wrong key; the skip reason was the right one.

---

## 2. What was deleted (21, all Category A)

| File | Tests | Why |
|---|---:|---|
| `Spe.Integration.Tests/PlaybookExecutionIntegrationTests.cs` | **23** (whole file) | 19 live-Dataverse + the 4 that remained were all ADR-038 §7 **B3** DI-registration tests — nothing of value survived |
| `Sprk.Bff.Api.Tests/Api/Ai/AnalysisChatContextEndpointsTests.cs` | 1 | 404 path against a stub resolver that always returns non-null |
| `Sprk.Bff.Api.Tests/Infrastructure/DI/CacheModuleTests.cs` | 1 | Live Redis required at DI-registration time |

Plus **2 further ADR-038 B3 violations** found while investigating (§4), removed under the same ADR
rather than the owner's live-service instruction — stated separately so the scope stays honest:
`ToolFrameworkIntegrationTests.ToolHandlerRegistry_IsRegisteredInDI` and
`ReportingDeploymentModelTests.MultiCustomerModel_ProfileManager_AvailableForSpProfileResolution`.

**Result: Skip= 88 → 67. Live-service skips: 21 → 0.**
No file was under a deletion-protected KEEP path (checked explicitly before deleting).

---

## 3. Two claims in the skip reasons were false

Skip strings are prose, and prose drifts. Both of these were asserted in a `Skip=` message and both
turned out to be wrong when checked:

1. **`CacheModuleTests`** — *"Covered by manual harness `tests/manual/RedisValidationTests.ps1`."*
   The harness exists, but it **greps config files and source text**; it never resolves a
   `ConnectionMultiplexer`. It does not cover the path. The gap is now recorded in the file itself
   instead of being papered over by a false citation — see §5.
2. **The same skip** cited `AbortOnConnectFail=true` as the blocker while the test passed
   `abortConnect=false` in its own connection string. Both are true simultaneously:
   `CacheModule.cs:87` **hard-sets `configOptions.AbortOnConnectFail = true`**, overwriting the
   connection string. The test could never have run, regardless of its config.

---

## 4. The armed B3 guard has a blind spot — deliberately left open

The four surviving tests in `PlaybookExecutionIntegrationTests` were textbook B3
(`GetService<T>()` → `.Should().NotBeNull()`), yet the **armed** B3 guard did not flag them. Cause:
both of its regexes require the assertion to be **directly chained or directly wrapped**:

```
\.Get(?:Required)?Service<[^>]*>\(\)\s*\.\s*Should\(\)\s*\.\s*NotBeNull...   # chained
Assert\.NotNull\(\s*[^;)]*\.Get(?:Required)?Service                          # wrapped
```

Neither matches the **assign-then-assert** form, which is how most of them are actually written:

```csharp
var service = scope.ServiceProvider.GetService<IPlaybookService>();
service.Should().NotBeNull("...");
```

### The widening was measured, then rejected

A candidate pattern capturing the variable and requiring the statement to end at the assertion was
run across the whole tree. **Four sites. Two genuine, two false positives — a 50% over-call rate.**

| Site | Verdict |
|---|---|
| `ToolFrameworkIntegrationTests` | genuine B3 → **deleted** |
| `ReportingDeploymentModelTests` | genuine B3 → **deleted** |
| `StubInsightGraphTests` | **false positive** — `NotBeNull` is a redundant prefix to `BeOfType<StubInsightGraph>` |
| `MetricsDistributedCacheRegistrationTests` | **false positive** — prefix to `GetType().Name.Should().Be(nameof(MetricsDistributedCache))` |

Both false positives are the **ADR-032 "which implementation resolved" shape** — the very shape the
guard's own documentation says it must not attack (root CLAUDE.md §10 bullet 6). A regex cannot
separate "`NotBeNull` as the whole test" from "`NotBeNull` as a redundant prefix to a real type
assertion" without semantic analysis.

**Decision: fix the instances, do not arm the detector.** Arming it would have made the guard fire on
the kill-switch pattern ADR-032 prescribes — turning a correctness guard into a reason to delete
correct tests. The blind spot stays open and documented; `/test-diet` covers the shape by judgment.

This is the same adjudication rule that governed task 084: **doubt = KEEP**, and every classifier
defect this project has found was an over-call.

---

## 5. Residual gaps, stated plainly

- **Nothing automated now asserts "Redis on ⇒ real multiplexer, not the null object."** The Redis-OFF
  branches (b/c/d) and the `NullConnectionMultiplexer` ADR-032 semantics remain covered. Closing this
  needs a Redis container in the test lane, not a resurrected skip. Recorded in `CacheModuleTests.cs`.
- **67 skips remain**, all categories B–F. They are *not* deletion candidates on the current
  instruction; B is PR #894's work, and C/D/E/F each need a different fix.
- **B3's assign-then-assert form is undetected.** Two instances were removed by hand; new ones will
  not be caught mechanically.
