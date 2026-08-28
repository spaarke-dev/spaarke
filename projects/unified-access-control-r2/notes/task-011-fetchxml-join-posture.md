# Task 011 — FetchXML guard posture: reject joins, do not scope them

> Spec **FR-10** · finding **A-17** (register §A.2, rank 2, High) · 2026-08-26
> Surface: `src/server/api/Sprk.Bff.Api/Api/ExternalAccess/ExternalModuleDataEndpoints.cs`

---

## The hole

`ExecuteScopedFetchAsync` refused a caller-submitted fetch iff
`referenced.Count == 0 || referenced.Any(e => e != module.RecordEntity)`.

The input to that predicate is a **set of entity names** produced by `FetchXmlEntityExtractor`. A
**self-join** — `<link-entity name='{module.RecordEntity}'>` — contributes only the module's own name, so
the referenced set of an exfiltrating self-join is *byte-identical* to that of a benign single-entity read.
The guard's only input structurally could not tell them apart.

That mattered because Tier-2 scoping filters **primary rows only** (`Tier2ScopeFilterInjector.Inject`
adds an `in`-filter on the entity's own scope attribute; `ExternalModuleDescriptor.ScopeRows` keeps a
primary row when its own attribute ∈ accessible set). Columns pulled through a self-joined link-entity are
**extra attributes ON an in-scope primary row**, never scope-checked, and `FetchService.ProjectEntity`
serializes `AliasedValue` straight to the client. Per project fact #1, reads on this seam are app-only, so
this filter is the *entire* security boundary — the result was real cross-matter / cross-client field
disclosure on a caller-controlled surface.

## Posture chosen: **reject all joins** (not "scope the join")

Per FR-10's wording ("rejected, not scoped") and the task constraint's default posture.

The guard now applies **two independent refusal checks**:

| # | Check | Status |
|---|---|---|
| 1 | **Entity identity** — every referenced name must equal the module's `RecordEntity` | UNCHANGED (so the cross-entity protection cannot regress), and evaluated FIRST so a foreign join keeps reporting `DV_FETCHXML_ENTITY_MISMATCH` exactly as before |
| 2 | **Structural join detection** — any `<link-entity>` at any depth | NEW. Emits `DV_FETCHXML_LINK_ENTITY_NOT_PERMITTED` |

### Why reject rather than scope

- **Scoping a join is materially harder to get right.** It would require scope-checking every aliased
  column's source row against the caller's accessible set, per dimension, for arbitrary join predicates.
  The failure mode of getting that subtly wrong is silent disclosure — the exact class of bug A-17 is.
- **No consumer needs it** (see the consumer audit below), so per root CLAUDE.md §11 a per-module join
  allow-list would be new surface with no articulable cost-of-doing-nothing. **Deliberately not added.**
- Adding one later is a purely additive change to one method, so the option is not foreclosed.

### Why join detection is broader than the extractor's

`FetchXmlEntityExtractor` matches `Descendants("link-entity")` — an exact, namespace-qualified `XName`.
The guard instead matches the **local name, case-insensitively, ignoring namespace**. So
`<LINK-ENTITY>` and `<x:link-entity>` — which the extractor does not see at all — are still refused.
This is strictly more conservative: any fetch Dataverse would itself reject is simply refused earlier, and
no legitimate single-entity read is affected. Elements only, so the literal text "link-entity" in a comment
or a filter value is not a false positive.

### Fail-closed (ADR-003)

Every parse failure, empty referenced set, and unmodelled verdict refuses. In particular, when the
extractor parses a payload but the guard's own parse fails, the guard **cannot prove the fetch join-free
and therefore refuses**. The `default:` arm of the response mapper returns a 500 rather than falling
through to an admit — a permissive default is the failure mode this project has now hit repeatedly.

## Consumer audit (escalation trigger — NOT fired)

The POML's `<escalation>` trigger fires if an existing legitimate external consumer submits self-joins.

- `BffDataverseClient.retrieveMultipleRecords` → `POST {bffBaseUrl}/api/dataverse/fetch` is the only client
  path onto this seam.
- **No client code constructs a link-entity for this seam.** The `link-entity` hits in client TS are: a
  doc comment in `IDataverseClient.ts`, a comment in `DataGrid/fetchXmlOverlay.ts`, and the **VisualHost
  PCF**, which runs in MDA host context over `Xrm.WebApi` — not this seam.
- **Mitigating fact:** cross-entity joins were ALREADY refused before this task. The marginal breakage
  surface added here is therefore *self-join views only*, not "views with joins".

⚠️ **Residual risk that code search cannot falsify** — see below.

## What the tests CANNOT falsify

1. **Maker-authored saved-query views are data, not code.** A module DataGrid can fetch a `savedquery`'s
   FetchXML and replay it into `/fetch`. If a maker builds a view on a module entity that surfaces a
   column through a **self-referential lookup**, Dataverse emits a same-entity `<link-entity>` and that
   view will now 400 with `DV_FETCHXML_LINK_ENTITY_NOT_PERMITTED`. No repo grep can rule this out.
   **Recommended follow-up:** enumerate `savedquery.fetchxml` for every registered module entity in each
   environment and check for `<link-entity>`. Cheap, one-time, and it converts this from unknown to known.
2. **Whether Dataverse itself accepts `<LINK-ENTITY>` or a namespace-qualified join.** The guard refuses
   both regardless, so the guard is safe either way — but the tests pin *our* behavior, not Dataverse's.
3. **Runtime behavior of the aliased-column leak itself.** The exploit is proven blocked at the guard;
   nothing here executes against a live Dataverse, so the downstream `AliasedValue` serialization path is
   asserted by code reading, not by execution.

## Verification that the tests are load-bearing

Each check was **perturbed individually** in production code and the suite re-run — green tests that
survive a perturbation are vacuous, and this project has already paid for exactly that.

| Perturbation | Result |
|---|---|
| Delete join detection (check 2) | **11 tests fail** |
| Delete entity-identity check (check 1) | **5 tests fail** |
| Make join match case/namespace-exact | **2 tests fail** |
| Make guard parse-failure permissive | **1 test fails** |

The task-001 characterization suite's hand-**transcribed** copy of the guard predicate was also removed
and repointed at the real guard. A transcription can pin a snapshot but cannot verify a fix: it does not
change when production changes, so post-fix it would have kept answering for the old code and passed
vacuously. The same reasoning drove `ScriptedEntityExtractor` to **throw** on unmodelled input rather than
return a default.

## Trap encountered (worth propagating)

`Copy-Item` **preserves** `LastWriteTime`. Restoring a file from a backup therefore moves its timestamp
*backwards*, MSBuild judges the previously-compiled DLL up-to-date, and `dotnet test --no-build` silently
runs a **stale assembly** — producing one phantom failure that did not match the source on disk. Confirmed
by inspecting the source, then `touch` + rebuild (DLL timestamp 23:28 → 23:43) and re-run: clean. Read the
build result — and the artifact timestamp — before believing a test result.
