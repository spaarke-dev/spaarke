# Task 020 — Publish-Size Verification

> **Date**: 2026-06-25
> **Task**: 020 — Enrich CreateNotificationNodeExecutor.BuildNotificationEntity with viaMatter / regardingName / source
> **Constraint**: CLAUDE.md §10 BFF Hygiene — ≤60 MB ceiling, ≤+5 MB single-task delta
> **Baseline**: 44.98 MB (PR 1 baseline from task 003)

---

## Measurement

```powershell
dotnet publish -c Release src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj -o deploy/api-publish-task020/
Compress-Archive -Path deploy/api-publish-task020/* -DestinationPath deploy/api-publish-task020.zip -CompressionLevel Optimal
```

| Metric | Value |
|---|---|
| Compressed publish output | **46.31 MB** (48,555,100 bytes) |
| Delta vs PR 1 baseline (44.98 MB) | **+1.33 MB** |
| Distance from §10 ceiling (60 MB) | **13.69 MB headroom** |
| Single-task delta threshold (≤+5 MB) | ✅ within limit |
| Cumulative ceiling check (≤55 MB) | ✅ within limit |

---

## Source of delta

The +1.33 MB delta is **not introduced by task 020**. Task 020 only modifies
`CreateNotificationNodeExecutor.cs` (one C# file, ~140 added LOC of pure data
enrichment logic — no new NuGet refs, no new DI registrations). The delta
reflects accumulated changes since the PR 1 baseline (PR 2 W0 work landed
`EntityNameValidatorNodeExecutor.cs`, `PlaybookBuilder` form additions, and
`DAILY-BRIEFING-NARRATE` deployment scaffolding).

The next per-task delta would be measured against this 46.31 MB checkpoint.

---

## CVE check

```
dotnet list package --vulnerable --include-transitive
```

| CVE | Severity | Pre-existing? |
|---|---|---|
| Microsoft.Kiota.Abstractions 1.21.2 — GHSA-7j59-v9qr-6fq9 | High | ✅ Pre-existing in master (carried across multiple R4 task commits — `9268b86f0`, `322b2658b`, `da8348ce3`) — NOT introduced by task 020 |

**No NEW HIGH CVE introduced by this task.**

---

## Justification (per CLAUDE.md §10 + §11)

| Question | Answer |
|---|---|
| Existing — what does this overlap with? | `BuildNotificationEntity` itself (lines 471–546). New code lives *inside* the existing method, not a sibling. |
| Extension — can I extend the existing instead? | YES — task is purely an extension of the existing executor. No new types, no new DI, no new endpoints. |
| Cost-of-doing-nothing — concrete failure mode? | (1) `EntityNameValidator` allow-list (FR-14) cannot be built — has no `regardingName` / `viaMatter.name` / `source.owningUser` to compose against. (2) Widget narration loses grounding — produces ungrounded sentences mentioning entity names the LLM hallucinated. (3) FR-13 (consumer narration grounded in payload) + FR-14 (entity-name validation Tool) FAIL without these fields. |

**Placement decision**: extension of existing `CreateNotificationNodeExecutor.cs`
— canonical NodeExecutor surface, no new types. Conforms to ADR-013 (no new AI
endpoints) and bff-extensions.md §A (no new DI registrations).

**Asymmetric-registration check (§F.1)**: not applicable — no DI changes,
no `*Module.cs` modifications, no feature-gated services introduced.
