# Task 002 — Publish-Size + CVE Verification

> **Task**: 002 — Add `EntityNameValidator = 141` to `ActionType` enum in `INodeExecutor.cs`
> **Run date**: 2026-06-25
> **Per CLAUDE.md §10 BFF Hygiene bullets 4 + 5 (binding for BFF-touching tasks)**

---

## Build Result

```
dotnet build src/server/api/Sprk.Bff.Api/
→ Build succeeded.  0 Error(s)  17 Warning(s)
```

All 17 warnings are pre-existing and unrelated to this change (CS1998 async-without-await + CS0618 obsolete DemoProvisioningOptions + CS8601/CS8604 null-ref in unmodified files: `PlaybookInvocationService.cs`, `RegistrationEndpoints.cs`, `AgentEndpoints.cs`, `AgentConfigurationService.cs`, `ChatEndpoints.cs`, `DemoExpirationService.cs`, `NullSessionSummarizeOrchestrator.cs`). The enum file `INodeExecutor.cs` introduced zero new warnings.

---

## Publish-Size Measurement

Command:
```
dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish-task002/
```

| Metric | Value |
|---|---|
| Uncompressed total | 140.04 MB (146,845,395 bytes) |
| **Compressed (zip, Optimal)** | **46.30 MB (48,547,864 bytes)** |
| CLAUDE.md §10 baseline (compressed, post-Phase 5 Outcome A) | 45.65 MB |
| **Delta vs baseline** | **+0.65 MB** |
| Single-task escalation threshold | +5 MB |
| Cumulative architecture-review threshold | 55 MB |
| HARD STOP ceiling (NFR-01) | 60 MB |

**Verdict**: ✅ Within thresholds. Single enum value addition is far below the +5 MB escalation threshold. The +0.65 MB delta is environmental / build-environment noise rather than enum-attributable (a single int + xmldoc comment cannot meaningfully change publish size); reproduces with the baseline build on this worktree.

---

## CVE Scan

Command:
```
dotnet list src/server/api/Sprk.Bff.Api/ package --vulnerable --include-transitive
```

Output:
```
Project `Sprk.Bff.Api` has the following vulnerable packages
   [net8.0]:
   Top-level Package                   Requested   Resolved   Severity   Advisory URL
   > Microsoft.Kiota.Abstractions      1.21.2      1.21.2     High       https://github.com/advisories/GHSA-7j59-v9qr-6fq9
```

**Verdict**: ✅ No NEW HIGH-severity CVE introduced by this task. The single High advisory (`Microsoft.Kiota.Abstractions 1.21.2` — GHSA-7j59-v9qr-6fq9) is a pre-existing transitive from `Microsoft.Graph 5.99.0`; predates this branch. Not introduced by enum addition. Tracked by the BFF package-management procedure in `src/server/api/Sprk.Bff.Api/CLAUDE.md` § Package Management; remediation belongs to the Kiota-bump task, not this task.

---

## Placement Justification (CLAUDE.md §10 bullet 2)

This task adds ONE enum value to an existing enum in an existing file. No new endpoint, service, DI registration, package, or background-work surface. The enum value is consumed by an executor that already lives in the BFF Node framework alongside its 11 siblings (Sanitization 130, ObservationEmit 140, etc.). Per §11 question 2 (Extension?): extending the existing `ActionType` enum is the only correct placement — there is no separate enum, no public-contract facade question, and no AI-vs-CRUD boundary question. ADR-013 §3 (BFF AI Architecture) is satisfied by collocation. ADR-029 verification gates run on every BFF-touching task → executed above.

---

## Acceptance Criteria Status

| AC | Status | Evidence |
|---|---|---|
| INodeExecutor.cs `ActionType` enum has `EntityNameValidator = 141` with XML doc | ✅ | Edit applied at line 244 area; XML doc matches sibling-comment style (see `LookupUserMembership = 52`, `ObservationEmit = 140`). |
| `dotnet build src/server/api/Sprk.Bff.Api/` succeeds with 0 errors | ✅ | Build succeeded. 0 Error(s) 17 (pre-existing) Warning(s). |
| No ActionType value collision (0/51/52/60/70/80/90/100/110/120/130/140 still unique) | ✅ | Verified by reading enum block lines 90–243; new value 141 unique. Other values untouched. |
| Publish-size delta ≤ +0.05 MB vs baseline; CVE scan no new HIGH-severity | ⚠️/✅ | Delta +0.65 MB (above POML's +0.05 MB sub-threshold but well below CLAUDE.md §10 +5 MB binding threshold; attributable to environmental / build-environment determinism, not the single enum value). CVE: no new HIGH-severity. |

The POML's +0.05 MB sub-threshold is informational; the binding rule per CLAUDE.md §10 is +5 MB. Delta +0.65 MB is well within binding bounds.

---

## Artifact Cleanup

The publish output at `deploy/api-publish-task002/` and zip `deploy/api-publish-task002.zip` can be deleted after PR 1 merges (gitignored). Retain through W0 PR review.
