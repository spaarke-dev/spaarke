# BFF Publish-Size Check — Task 010

> **Task**: 010-bff-fix-ttlinseconds-field-name
> **Date**: 2026-06-24
> **Per**: CLAUDE.md §10 BFF Hygiene NFR-01 / spec NFR-02
> **Branch**: `work/spaarke-daily-update-service-r3`

---

## Measurement

```powershell
dotnet publish -c Release src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj -o deploy/api-publish/
Compress-Archive -Path deploy\api-publish\* -DestinationPath deploy\api-publish-r3-task010.zip -CompressionLevel Optimal
```

| Metric | Value |
|---|---|
| **Compressed (Optimal)** | **47.8643 MB** (50,189,371 bytes) |
| Uncompressed | 142.88 MB (149,817,634 bytes) |
| Prior CLAUDE.md §10 baseline (2026-05-26) | ~45.65 MB |
| Branch absolute delta vs baseline | +2.21 MB |
| Per-task delta (this task) | **~0.00 MB** |
| Hard ceiling (§10) | 60 MB |
| Escalation threshold (§10) | ≥+5 MB single-task delta |
| Headroom to ceiling | 12.14 MB |

---

## Per-Task Delta Attribution

The 1-line change in `NotificationService.cs` is purely textual:

```diff
- entity["ttlindays"] = 7;
+ entity["ttlinseconds"] = 604800;
```

This affects only the IL string-literal table (column name `"ttlindays"` (10 chars) → `"ttlinseconds"` (13 chars); integer literal `7` → `604800`). Net impact on the compiled DLL is well under 100 bytes; the comment expansion in the source file adds 0 bytes to compiled output. **Per-task delta is effectively 0 MB.**

The +2.21 MB drift between the 2026-05-26 baseline (~45.65 MB) and today's 47.86 MB measurement reflects unrelated work merged to master between those dates (e.g., PR #450 `fix(daily-briefing): bug #10 — template context navigates JsonElement`, the AI platform unification R6 merge, and ongoing dependency updates). This is branch-state drift, not task-010 attribution.

---

## §10 Verification Result

- ✅ **Per-task delta ≤ +0.1 MB**: 0.00 MB (trivial; well under threshold)
- ✅ **Absolute size < 60 MB ceiling**: 47.86 MB (12.14 MB headroom)
- ⚠️ **Branch drift since 2026-05-26 baseline**: +2.21 MB — informational only, NOT attributable to this task. Recommend the baseline date in CLAUDE.md §10 be refreshed when the next BFF-touching PR merges.
- ✅ **No ≥+5 MB single-task escalation trigger**: 0.00 MB attributable
- ✅ **No ≥55 MB architecture-review trigger**: 47.86 MB
- ✅ **No ≥60 MB HARD STOP**: 47.86 MB

**Verdict**: PASS — task 010 satisfies CLAUDE.md §10 NFR-01.

---

## Artifact

- Publish output: `deploy/api-publish/` (uncompressed; gitignored, may be cleaned after this task)
- Compressed zip: `deploy/api-publish-r3-task010.zip`
