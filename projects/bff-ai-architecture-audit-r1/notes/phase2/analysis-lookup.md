# Phase 2 Analysis — Category 2: Lookup Services

> **Authored by**: Phase 2 W1 Sub-Agent B
> **Pinned to**: commit `357e6936` (inventory snapshot)
> **HEAD at analysis time**: `12275b10` (5 commits since snapshot; zero code drift in lookup files — verified)
> **Scope boundary**: lookup-service decisions only; out-of-scope = Dataverse alternate-key schema, IGenericEntityService refactor

---

## §1 Phase 1 baseline (verbatim from inventory.md §2.2 + §6.2 + §7.2)

### §1.1 Inventory §2.2 — 4 near-identical implementations
- **§2.2.1 `PlaybookLookupService`** — `sprk_playbookcode` lookup; 1-hour TTL via `IMemoryCache`. Consumers: `InvoiceExtractionJobHandler.cs`, `DefaultPlaybookConstants.cs` (2 production consumers claimed). State: ACTIVE. DI: Scoped in `FinanceModule.cs:114`. Cache prefix: `playbook:code:`.
- **§2.2.2 `ActionLookupService`** — same pattern for `sprk_actioncode`. Consumers: ONLY `FinanceModule.cs` registration. State: **UNUSED/ORPHANED**. DI: Scoped in `FinanceModule.cs:123`. Cache prefix: `action:code:`.
- **§2.2.3 `SkillLookupService`** — same pattern for `sprk_skillcode`. Consumers: ONLY `FinanceModule.cs`. State: **UNUSED/ORPHANED**. DI: Scoped in `FinanceModule.cs:132`. Cache prefix: `skill:code:`.
- **§2.2.4 `ToolLookupService`** — same pattern for `sprk_toolcode`. Consumers: ONLY `FinanceModule.cs`. State: **UNUSED/ORPHANED**. DI: Scoped in `FinanceModule.cs:141`. Cache prefix: `tool:code:`.

**Cross-cutting note**: All four are line-for-line near-identical (≤5-line diff each — entity name + cache prefix). All depend on `IGenericEntityService` + `IMemoryCache`. All have 1-hour TTL. XML docstrings differ only in entity noun. Three of four have ZERO consumers — classic DRY violation + dead code.

### §1.2 Inventory §6.2 — The four orphans (verbatim)
4 confirmed UNUSED — strongest "dead code" finding:
1. `ActionLookupService`, 2. `SkillLookupService`, 3. `ToolLookupService`, 4. `IntentClassificationService` (different category — Cat 1). Combined ~600 LOC + ~150 LOC interfaces. Part of `Sprk.Bff.Api.dll` publish artifact.

### §1.3 Inventory §7.2 — Open questions
- Three of four are unused. Did historical projects WRITE the code without WIRING it? Or did consumers exist and get refactored away?
- Generic candidate: `ILookupService<TEntity, TResponse>(string code)` with entity-specific bits as type params or generic factories.
- The XML docstrings are line-for-line copy/paste — what % is templated vs hand-edited?

---

## §2 Empirical reproduction (HEAD `12275b10`)

Per CLAUDE.md §10 F.3 (Empirical-Reproduction-FIRST), every orphan claim was re-grepped at HEAD before being accepted.

### §2.1 Grep table — `src/` only

| Pattern | Files matched | Production consumers (exc. self + interface + FinanceModule reg) | Test consumers |
|---|---|---|---|
| `IPlaybookLookupService` | 5 | **2** (`InvoiceExtractionJobHandler.cs`, `Chat/DefaultPlaybookConstants.cs`) | 0 |
| `IActionLookupService` | 3 | **0** | 0 |
| `ActionLookupService` (concrete) | 4 | **0 real** (the 4th match in `InsightsActionRouter.cs` is XML-cref-only) | 0 |
| `ISkillLookupService` | 3 | **0** | 0 |
| `IToolLookupService` | 3 | **0** | 0 |
| Rename-variation patterns (`IsActionLookupService`, etc.) across whole repo | 0 | 0 | 0 |
| Same patterns in `tests/`, `scripts/`, `infra/`, `infrastructure/` | 0 | 0 | 0 |

### §2.2 The single near-miss: `InsightsActionRouter.cs`

`Grep ActionLookupService` matched `Services/Ai/Insights/Routing/InsightsActionRouter.cs` (lines 400-405):

```csharp
/// <summary>
/// Normalize a code value for cache keys + Dataverse equality: trim + upper.
/// Mirrors <c>ActionLookupService.GetCacheKey</c> casing semantics.
/// </summary>
private static string NormalizeCode(string code) => code.Trim().ToUpperInvariant();
```

This is a **doc-comment cref only** — no `using`, no constructor injection, no runtime dependency. **HARD GATE remains satisfied**: NOT a non-test consumer. (Aside: the cref will become a dangling reference once `ActionLookupService` is deleted — needs cleanup in same PR.)

### §2.3 Reclassification of `PlaybookLookupService` consumer count

Phase 1 inventory counted **2** consumers; empirical re-check finds only **1** is a real runtime dependency:
- `InvoiceExtractionJobHandler.cs` lines 31, 52 — constructor-injected `IPlaybookLookupService _playbookLookup` (REAL consumer)
- `Chat/DefaultPlaybookConstants.cs` line 43 — doc-cref `<c>IPlaybookLookupService.GetByCodeAsync</c>` (NOT a runtime consumer)

`PlaybookLookupService` is still ACTIVE (1 real consumer is sufficient) but the load-bearing population is smaller than the inventory suggested.

### §2.4 LOC inventory

| Service | Impl LOC | Interface LOC | Total | Notes |
|---|---|---|---|---|
| PlaybookLookupService | 192 | 53 | 245 | Has typed `PlaybookNotFoundException` |
| ActionLookupService | 185 | 53 | 238 | Throws generic `InvalidOperationException` |
| SkillLookupService | 185 | 53 | 238 | Throws generic `InvalidOperationException` |
| ToolLookupService | 185 | 53 | 238 | Throws generic `InvalidOperationException` |
| **3 orphans subtotal** | **555** | **159** | **714** | Inventory's "~600 + ~150" was conservative |
| **All 4 total** | **747** | **212** | **959** | |

### §2.5 4-way diff (DRY magnitude)

Everything except entity-name strings, ~3 LOC of `MapEntityToXResponse` body, and (for Playbook only) +4 response fields + typed exception is **byte-identical modulo substitution**. Phase 1's "≤5-line diff" claim is slightly understated for Playbook (~15-line diff) but accurate for the 3 orphans (~3-5 line diff among each other).

---

## §3 Per-service decisions

| Service | Decision | Migration cost | Rationale (HARD GATE) |
|---|---|---|---|
| `PlaybookLookupService` | **KEEP** | None | 1 real production consumer; load-bearing for invoice extraction. |
| `ActionLookupService` | **DELETE** | **S** (≤2 hr: 2 files + 1 DI line + 1 cref edit) | 0 production consumers at HEAD; cref-only near-miss in `InsightsActionRouter.cs` does NOT count under the same yardstick. |
| `SkillLookupService` | **DELETE** | **S** (≤1 hr) | 0 production consumers; no near-misses. |
| `ToolLookupService` | **DELETE** | **S** (≤1 hr) | 0 production consumers; no near-misses. |

### §3.5 Generic consolidation evaluation (`ILookupService<T>`) — NOT RECOMMENDED

Phase 1 §7.2 floated a generic abstraction. Rejected per:
1. Post-deletion, only ONE concrete remains (`PlaybookLookupService`) with ONE real consumer — generic abstraction over 1 closed type is YAGNI per ADR-010 ("MUST NOT create interfaces without genuine seam requirement").
2. No documented near-term consumer would resurrect Action/Skill/Tool lookups.
3. ADR-010 budget (~265 registrations baseline) — a generic + 3 closed-type registrations would ADD net registrations vs DELETE.

**Conditional override**: IF Sub-Agent A's cache canonical lands (`ISpaarkeCache<T>` or `IDataverseCodeLookupCache<T>`), `PlaybookLookupService` should migrate to it — but as `IPlaybookCodeLookup` (entity-specific) inheriting cache plumbing, NOT as a generic `ILookupService<T>`. This is a **HANDOFF to Sub-Agent A**, not a Phase 2 decision per Q-004.

### §3.6 Publish-size delta

- 714 LOC eliminated; estimated ~30-50 KB IL impact in `Sprk.Bff.Api.dll`; estimated **~10-20 KB compressed reduction** (DRY violations compress well, so savings are modest).
- Per CLAUDE.md §10 NFR-01 (45.65 MB baseline, ≤60 MB ceiling): this is a REDUCTION — no justification or escalation required.
- Real win is cognitive load + 3 fewer `AddScoped` lines in `FinanceModule.cs`.

---

## §4 Cross-cutting findings

### §4.1 Anti-pattern: "Templated Service Sprawl"
Developer wrote ONE service well (Playbook) then copy-pasted-and-renamed to generate sibling services for adjacent entities WITHOUT confirming consumers exist. Identical SaaS-portability docstring wording across all 4 is a strong replication signal. **Recommendation**: add to `docs/standards/ANTI-PATTERNS.md` — "Before adding `XLookupService` for a new entity, demonstrate ≥1 production consumer wired in the same PR."

### §4.2 Docstring template ratio (Phase 1 §7.2 question answered)
~95% of docstring content across the 3 orphans is templated. Playbook's docstring is ~75% templated + 25% hand-augmented (because it grew real consumers like `<example>` blocks).

### §4.3 Typed-exception asymmetry
`PlaybookLookupService` throws `PlaybookNotFoundException` (typed, correct). The 3 orphans throw generic `InvalidOperationException` ("X not found...") — anti-pattern: caller `catch (InvalidOperationException)` can't distinguish not-found from genuine invalid-operation. Deletion resolves the asymmetry; the **lesson** for any future lookup-by-code service: typed not-found exception is mandatory.

### §4.4 `FinanceModule.cs` cleanup scope
24 lines deleted (lines 116-141 collapse to just 114). `FinanceModule.cs` post-cleanup represents the *true* Finance Intelligence module boundary — only `PlaybookLookupService` as Finance Intelligence's lookup dependency. Textbook ADR-032 §F.1 cleanup: 3 unconditional registrations with no consumer (inverse of asymmetric-registration — *symmetrically* dead).

### §4.5 Forward-trace + Q-003 sequential coordination
If Finance Intelligence (or another team) later builds Action/Skill/Tool authoring or runtime resolution, deleted code can be regenerated in ≤2 hours per service. Trade-off "delete now, regenerate cheaply if needed" beats "keep dead code in publish artifact in case it's needed". **Q-003 obligation**: notify Finance Intelligence owner before deletion lands; recommended phrasing: *"We're deleting Action/Skill/Tool lookup services because they have 0 production consumers. If you have a planned project that needs them, flag now; otherwise we proceed."*

### §4.6 Sub-Agent A handoff
`PlaybookLookupService` uses `IMemoryCache` directly (Phase 1 §2.4 inline pattern). If Sub-Agent A recommends a canonical wrapper, `PlaybookLookupService` is the **prime first migration candidate**: sole survivor, simple 1-hour absolute TTL, clean invalidation API. Recommendation surfaced — NOT locked here per Q-004.

---

## §5 Canonical naming candidates (Q-004 framing only)

Per Q-004, these are CANDIDATES; owner locks final name.

**If only PlaybookLookupService retained**: keep `IPlaybookLookupService` (already in production code, never the subject of rename complaint).

**If migrating to Sub-Agent A's cache canonical** (conditional):
- `IPlaybookCodeLookup` (concrete, single-entity scope)
- `IDataverseCodeLookup<TPlaybookResponse>` (generic; YAGNI per §3.5)
- `IAlternateKeyLookup<TResponse>` (Dataverse mechanism leaks into name)
- `ICodeBasedPlaybookResolver` (domain-flavored; potential conflict with `PlaybookDispatcher`)

Recommendation (deferred): `IPlaybookLookupService` unless Sub-Agent A's analysis recommends a uniform family. Do NOT adopt a generic `ILookupService<T>` per §3.5.

---

## §6 Drift report (snapshot `357e6936` vs HEAD `12275b10`)

- **Lookup files (8 files + FinanceModule.cs)**: `git log --oneline 357e6936..HEAD` for these paths returned **0 commits**. Byte-identical. Three-orphan claim holds.
- **HEAD context**: `12275b10` is Merge PR #342 (`work/insights-engine-r3-init`) — added `projects/ai-spaarke-insights-engine-r3/` scaffolding only; did NOT touch lookup files.
- **Consumer-side drift**: `InvoiceExtractionJobHandler.cs` still injects `IPlaybookLookupService`; `DefaultPlaybookConstants.cs` still has doc-cref only. No change.
- **Tests/scripts/infra drift**: 0 matches for any of the 4 service names across `tests/`, `scripts/`, `infra/`, `infrastructure/` at HEAD. Orphans remained orphans.

**Conclusion**: No drift affects audit conclusions. Findings actionable at PR time within next 1-2 weeks; longer gaps should re-run §2 grep table.

---

## §7 Open questions for owner (Q-002 single end-of-audit review)

1. **Cross-team (Q-003)**: Finance Intelligence owner — confirm no near-term plan (≤3 months) to wire Action/Skill/Tool lookups. If confirmed, proceed with §3 deletions.
2. **`PlaybookLookupService` retention**: with only 1 real consumer + 1 doc-cref, is it a reasonable candidate for direct inlining into `InvoiceExtractionJobHandler`? **Recommendation**: KEEP (SaaS-portability rationale sound; Insights Engine may need playbook-by-code resolution later).
3. **Cache canonical migration timing (HANDOFF to Sub-Agent A)**: migrate `PlaybookLookupService` immediately or wait for additional consumers?
4. **Typed-exception standardization (HANDOFF to broader audit)**: standard `XNotFoundException` base type for lookup-by-code services? Probably YES, out of Category 2 scope.
5. **Doc-cref cleanup timing**: clean `InsightsActionRouter.cs:402-403` cref in same PR as deletion or separate hygiene PR? **Recommendation**: same PR (atomic delete + dangling-cref fix).

---

## §8 ADR candidates (Q-005 DEFERRED — bullets only)

- **ADR candidate: "Lookup-Service-Per-Entity Rule"** — `XLookupService` over `IGenericEntityService` + `IMemoryCache` MUST NOT be added without ≥1 production consumer wired in same PR. Likely belongs in `docs/standards/ANTI-PATTERNS.md` rather than full ADR.
- **ADR candidate: "Lookup-Service Typed Exceptions"** — `Get*ByCodeAsync()` MUST throw typed `*NotFoundException` (per `PlaybookNotFoundException` precedent). Generic `InvalidOperationException` for not-found is anti-pattern.
- **ADR candidate: "Canonical Dataverse Cache Wrapper"** (HANDOFF to Sub-Agent A) — typed canonical (`ISpaarkeCache<T>` or similar) over `IMemoryCache`/`IDistributedCache`. References inventory §2.4 `EmbeddingCache` canonical. Adoption obligation should mention `PlaybookLookupService` as first migration target.
- **No new ADR for "Generic `ILookupService<T>`"** — explicitly NOT recommended per §3.5 YAGNI. Future project resurrecting Action/Skill/Tool lookups must make its own ADR-010 case.

---

## Key file paths (absolute) for execution

- `C:\code_files\spaarke-wt-ai-spaarke-insights-engine-r2\src\server\api\Sprk.Bff.Api\Services\Ai\PlaybookLookupService.cs` (KEEP)
- `C:\code_files\spaarke-wt-ai-spaarke-insights-engine-r2\src\server\api\Sprk.Bff.Api\Services\Ai\IPlaybookLookupService.cs` (KEEP)
- `C:\code_files\spaarke-wt-ai-spaarke-insights-engine-r2\src\server\api\Sprk.Bff.Api\Services\Ai\ActionLookupService.cs` (DELETE)
- `C:\code_files\spaarke-wt-ai-spaarke-insights-engine-r2\src\server\api\Sprk.Bff.Api\Services\Ai\IActionLookupService.cs` (DELETE)
- `C:\code_files\spaarke-wt-ai-spaarke-insights-engine-r2\src\server\api\Sprk.Bff.Api\Services\Ai\SkillLookupService.cs` (DELETE)
- `C:\code_files\spaarke-wt-ai-spaarke-insights-engine-r2\src\server\api\Sprk.Bff.Api\Services\Ai\ISkillLookupService.cs` (DELETE)
- `C:\code_files\spaarke-wt-ai-spaarke-insights-engine-r2\src\server\api\Sprk.Bff.Api\Services\Ai\ToolLookupService.cs` (DELETE)
- `C:\code_files\spaarke-wt-ai-spaarke-insights-engine-r2\src\server\api\Sprk.Bff.Api\Services\Ai\IToolLookupService.cs` (DELETE)
- `C:\code_files\spaarke-wt-ai-spaarke-insights-engine-r2\src\server\api\Sprk.Bff.Api\Infrastructure\DI\FinanceModule.cs` (EDIT: remove lines 116-141 worth of orphan registrations + comment blocks)
- `C:\code_files\spaarke-wt-ai-spaarke-insights-engine-r2\src\server\api\Sprk.Bff.Api\Services\Ai\Insights\Routing\InsightsActionRouter.cs` (EDIT lines 402-403: remove dangling `<c>ActionLookupService.GetCacheKey</c>` cref)
- `C:\code_files\spaarke-wt-ai-spaarke-insights-engine-r2\src\server\api\Sprk.Bff.Api\Services\Jobs\Handlers\InvoiceExtractionJobHandler.cs` (sole real consumer of `IPlaybookLookupService` — no change)
- `C:\code_files\spaarke-wt-ai-spaarke-insights-engine-r2\src\server\api\Sprk.Bff.Api\Services\Ai\Chat\DefaultPlaybookConstants.cs` (doc-cref-only — no change)

---

# Sub-Agent B Final Status Report

1. **Status**: COMPLETED (READ-ONLY analysis; full 8-section content delivered)
2. **Output file path + size**: `projects/bff-ai-architecture-audit-r1/notes/phase2/analysis-lookup.md`
3. **Services analyzed**: 4 (Playbook, Action, Skill, Tool)
4. **Decision distribution**: KEEP 1 / DELETE 3 / CONSOLIDATE 0 / DEPRECATE 0
5. **Drift findings**: NONE — `357e6936` and HEAD `12275b10` are byte-identical for all 8 lookup files + `FinanceModule.cs`; orphans gained 0 new consumers
6. **Cross-cutting observations**: 4 (DRY violation pattern, ~95% docstring template ratio, typed-exception asymmetry, Sub-Agent A cache handoff)
7. **Open questions for owner**: 5 (Finance Intelligence cross-team confirmation, `PlaybookLookupService` inlining question, cache canonical timing, typed-exception standardization, doc-cref cleanup timing)
8. **Recommendations for W2**: §3 deletions are unblocked once Finance Intelligence owner confirms no near-term plans; `PlaybookLookupService` retention deferred pending Sub-Agent A cache canonical
