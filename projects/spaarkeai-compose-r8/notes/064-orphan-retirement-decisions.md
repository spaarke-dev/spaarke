# Task 064 — retiring the orphaned text-offset edit-batch surface: consumer check + decisions

> **Task**: `064-retire-orphaned-edit-batch-surface.poml` · **Rigor**: FULL · opus @ xhigh · **Date**: 2026-08-25
> **Scope authority**: the owner's 2026-08-25 decision — *"whatever is the 'best practice' coding approach."*
> For code with no producer and no consumer, that is deletion. This task executes the recommendation
> already recorded in [`052-text-search-demotion-decisions.md` §1.4](052-text-search-demotion-decisions.md).
> **Predecessor**: task 052 (which surfaced the orphans and deliberately left them, as deleting ~500 lines
> outside its DELETE list would have been scope expansion).

---

## 0. Executive summary

| # | Finding | Consequence |
|---|---|---|
| **G-1** | The orphan claim re-verified independently and holds **exactly**. `ComposeEditBatch` / `ComposeEditTransaction` were DI-registered and injected **nowhere**; `POST /api/compose/edit-batch/validate` has **zero** `.ts`/`.tsx`/`.json` callers repo-wide. | Deletion cannot change any observable behavior. |
| **G-2** | **The POML's sketched partition is correct on the seven types it names, and incomplete on one.** `CamelCaseStringEnumConverter` also lives in `ComposeEditModels.cs` and has **three consumers outside the edit surface** (`IComposeService.cs`, `DocxAnnotationReader.cs`, `AnnotationReanchorService.cs`). The FILE therefore survives; only types within it die. | §3. |
| **G-3** | `EditVerdict.Matches` / `EditValidationError.Examples` are **forced** deletions, not judgment calls: they are typed on dying types, so keeping them means keeping `ResolvedMatch`/`MatchExample` and the retirement does not happen. | §3.3. |
| **G-4** | Three further members are provably-always-default fossils of the same span vocabulary (`EditValidationError.MatchCount`, `EditErrorKind.Overlap`, `BatchValidationResult.BatchErrors`). **Deleted, and surfaced here as the one place this task went beyond the POML's enumerated list.** | §3.4 — the reviewer's revert point if they disagree. |
| **G-5** | **`ComposeEditAnchorPass` now has ZERO production callers.** The deleted endpoint was its only one. Per the POML's escalation trigger it is **KEPT and surfaced, not deleted** — its retirement is an owner decision. | §4. Escalation raised. |
| **G-6** | Three edited `.cs` files came back from the editor as **pure LF**, which `.gitattributes` (`*.cs text eol=crlf`) + `.editorconfig` + CI's format check all reject. Caught by `dotnet format --verify-no-changes` and fixed. **This would have turned the whole PR red and triggered a CI auto-format push.** | §6.3. |

---

## 1. Consumer check — recorded per deleted symbol (POML constraint)

Repo-wide, **all file types**, not just `.cs`: `src/`, `tests/`, `infra/`, `docs/`, `.claude/`, `scripts/`,
`.github/`. Scoped by symbol name rather than by the string `compose` — the worktree path is
`spaarke-wt-spaarkeai-compose-r8`, so a bare `grep -i compose` matches every line in the repo.

### 1.1 The two classes

| Symbol | Consumers found (before this task) | Verdict |
|---|---|---|
| `ComposeEditBatch` | `ComposeModule.cs:35` (DI registration) · `ComposeEditTransaction` (ctor dep) · `ComposeEditBatchTests` + `ComposeEditTransactionTests` · XML-doc prose in `ComposeEditModels.cs` / `ComposeEditAnchorPass.cs` / `ADR013_ComposeFacadeTests.cs` | **DELETE** — every consumer is the type itself, its own tests, its wrapper, or prose. **Injected into no endpoint and no service.** |
| `ComposeEditTransaction` | `ComposeModule.cs:36` (DI registration) · `ComposeEditTransactionTests` · prose | **DELETE** — same. Its only non-test consumer was its own DI registration. |

**Injection check (the load-bearing one).** `grep` for both type names across `src/` returns **no
constructor parameter, no `GetRequiredService`, no endpoint handler parameter** anywhere. They were
registered singletons that nothing resolved.

### 1.2 The endpoint

| Symbol | Consumers found | Verdict |
|---|---|---|
| `POST /api/compose/edit-batch/validate` | `ComposeEndpoints.cs:197` (route) + `:385` (handler) only | **DELETE** |
| `edit-batch` (route string, ALL file types) | Zero `.ts` / `.tsx` / `.json` / `.md`-in-`docs/` / `.claude/` hits. Only: the two server sites, three server XML-doc mentions, two comments in the dying `ComposeEditBatchTests`, and project notes/POMLs recording history. | Confirms task 052's F-1 independently. **No client, no fixture, no doc, no catalog seed names this route.** |

Real AI-edit placement happens client-side in `usePendingRedline`, which enforces the same anchor-first
contract in TypeScript (`composeAnchorResolution.ts:79` names the server contract it mirrors).

### 1.3 Symbols checked and NOT deleted (the near-misses)

| Symbol | Why it looked like a hit | Actual finding |
|---|---|---|
| `AppliedEdit` | `grep AppliedEdit` matches `src/solutions/SpaarkeAi/**` in 6 places | **Substring collision.** Those are `trackAppliedEdit` / `AppliedComposeEdit` — an unrelated client-side TS supersession type. Untouched (and outside this task's file boundary regardless). |
| `ComposeAnchorResolver` | Named in client prose (`usePendingRedline.ts:378`, `composeAnchorResolution.ts:79`, `ComposeWorkspace.tsx:2915`) | Prose references to the SERVER contract the client mirrors. The type survives, so the prose is still correct. No edit needed. |
| `CamelCaseStringEnumConverter` | Declared in `ComposeEditModels.cs` | **Three consumers outside this surface** — see §3.1. Survives. |

---

## 2. ADR-010 / bff-extensions.md §F.1 — DI symmetry (POML constraint: "confirm and state it")

Both removed registrations were **unconditional**:

```csharp
services.AddSingleton<ComposeEditBatch>();          // ComposeModule.cs:35 — top-level, no `if`
services.AddSingleton<ComposeEditTransaction>();    // ComposeModule.cs:36 — top-level, no `if`
```

Neither sat inside an `if (flag) { … }` block, so **no asymmetric registration is left behind**
(bff-extensions.md § F.1). The stronger form also holds: after removal there is **no endpoint, service,
or hosted service anywhere in the assembly that resolves either type**, and **no feature gate that could
reintroduce one** — so this is not "symmetric today, asymmetric when a flag flips", it is *absent*.

`ComposeModule` drops from **14 to 12** registrations (counting `AddSingleton`/`AddScoped`/`AddHostedService`),
moving further inside the ADR-010 ≤15 ceiling.
`ADR010_DITests`' `knownOneToOneCeiling` is unaffected (both types were concrete, with no interface).

---

## 3. `ComposeEditModels.cs` — the partition, type by type, with evidence

The POML flagged this file as the trap. It is, but not in the way sketched: the file contains **nine**
populations-relevant types, not the seven it lists, and the extra one is why the file survives at all.

### 3.1 SURVIVES — consumers outside the retired surface

| Type | Evidence |
|---|---|
| `CamelCaseStringEnumConverter` | **`IComposeService.cs:405`, `DocxAnnotationReader.cs:309`, `AnnotationReanchorService.cs:523`** each apply it via `[JsonConverter(typeof(…))]`; client prose in `compose-contracts.ts:406` + `ComposeReanchor.types.ts:32` documents the wire form it produces. **Not part of the edit surface at all** — it merely lives in this file. Deleting the file would have broken three unrelated surfaces. |
| `EditSource` | Consumed by `ProposedEdit.Sources`, which is part of the catalog-action payload mirror. `ComposeEditActionAnchorContractSeamTests.BuildPayloadFromSchema` emits `sources: []` derived from the Action's own `outputSchema`, so the member is pinned by a live contract test. |
| `ProposedEdit` | Deserialization target in `ComposeEditActionAnchorContractSeamTests` (schema-derived payload → `ProposedEdit`); input to `ComposeEditAnchorPass.Validate`; mirrored by client `ComposeEditor.tsx:514`. |
| `EditErrorKind` | Returned on every refusal by `ComposeEditAnchorPass.BuildAnchorError`; asserted in both seam files. |
| `EditValidationError` | Same. |
| `EditVerdict` | Same. |
| `BatchValidationResult` | Return type of `ComposeEditAnchorPass.Validate`. |

### 3.2 DIES — no consumer outside the retired surface

| Type | Every consumer found | Verdict |
|---|---|---|
| `ResolvedMatch` | `ComposeEditBatch` (dying) · `ComposeEditAnchorPass` (**only** as `Array.Empty<ResolvedMatch>()`) · `EditVerdict.Matches` · `AppliedEdit.Match` / `SkippedEdit.Match` · 2 dying test files | **DELETE.** No producer has ever emitted a non-empty one since task 052. |
| `MatchExample` | `EditValidationError.Examples` · `ComposeEditAnchorPass` (**only** as `Array.Empty<MatchExample>()`) · 2 dying test files | **DELETE.** Same. |
| `AppliedEdit` | `ComposeEditBatch.Apply` + `ComposeEditBatchResult.Applied` only | **DELETE** |
| `SkippedEdit` | `ComposeEditBatch.Apply` + `ComposeEditBatchResult.Skipped` only | **DELETE** |
| `ComposeEditBatchResult` | `ComposeEditBatch.Apply` return + `ComposeEditTransactionResult.Batch` only | **DELETE** |
| `ComposeEditTransactionResult` | `ComposeEditTransaction.Execute`/`.Rollback` only | **DELETE** |
| `EditBatchValidateRequest` | `ComposeEndpoints.ValidateEditBatch` `[FromBody]` parameter only | **DELETE** |

**The POML's sketched split is confirmed on all seven** — no symbol on the DELETE list turned out to have
a live consumer, so the escalation trigger in `<escalation><trigger>` #1 does **not** fire.

### 3.3 The `EditVerdict.Matches` / `EditValidationError.Examples` question — DECIDED: both members DELETED

This is **forced, not discretionary**, and the reasoning is worth stating because it is the reason the
POML singled it out:

- `Matches` is `IReadOnlyList<ResolvedMatch>`; `Examples` is `IReadOnlyList<MatchExample>`. Both element
  types are in the dying set.
- Keeping either member requires keeping its element type alive **solely to type a collection that no
  code can populate** — which would leave the offset vocabulary in the codebase and defeat the task.
- Nothing has produced a non-empty value for either since task 052 deleted `ComposeEditValidator`.
  `ComposeEditAnchorPass` passes `Array.Empty<…>()` at all four construction sites, and its own XML doc
  already said so.

**Decision: delete both members and both types.** The gain is not tidiness — it is that **ADR-049 I-7
becomes a property of the type system on this surface**: there is no longer any type in
`Services/Compose/` that can express a character offset into document prose, so no edit can be placed by
one. That is the same strengthening task 052 §6.4.1 achieved by removing `documentText` from the
`Validate` signature: a guarantee enforced by absence rather than by an assertion on the paths a test
happens to exercise.

The three per-test assertions that stated this at runtime (`verdict.Matches.Should().BeEmpty()`,
`Error.MatchCount == 0`, `Error.Examples.Should().BeEmpty()`) are replaced by ONE structural test —
`ComposeEditAnchorPassSeamTests.VerdictAndRefusalShapes_CannotExpressATextSpan` — which pins the public
property sets of `EditVerdict` / `EditValidationError` / `BatchValidationResult` as closed sets and
asserts the `Services.Compose` namespace declares no `ResolvedMatch` / `MatchExample`. It fails if anyone
re-admits an offset vocabulary through this door.

### 3.4 SCOPE DECISION — three further members deleted beyond the POML's list (SURFACED)

The POML enumerated seven dying types and asked only about `Matches`/`Examples`. Three members are
**not** typed on a dying type, so removing them is a judgment call. **They were removed. This is the one
place this task exceeded the POML's enumerated list, and it is the reviewer's revert point.**

| Member | Post-064 state | Why deleted |
|---|---|---|
| `EditValidationError.MatchCount` | Producer exists but hardcodes `0`; one seam assertion read it | It reported *how many `target_text` occurrences were found*. That search died in task 052. It is the third member of a single three-part reporting unit (`MatchCount` + `Examples` + the now-load-bearing `ResolutionHint`) — the record's own XML doc described them together. Keeping a permanently-`0` integer after removing its two siblings leaves a field whose only honest answer to *"count of what?"* is *"nothing — it is a fossil."* That is precisely the stale-claim defect class this project has now caught **four** times (052 §6.2). |
| `EditErrorKind.Overlap` | **Zero producers, zero consumers** after this task | Its own XML doc read *"a BATCH-level span collision detected on the apply side"* — the apply side is what this task deletes. `ComposeEditBatch` never produced it either (Phase 3 emitted a `SkippedEdit` with a string reason); its **only** construction site in the entire repo was a hand-built batch error inside the dying `ComposeEditBatchTests`. This is the owner's stated rule applied literally: no producer, no consumer → delete. |
| `BatchValidationResult.BatchErrors` | Producer exists but always `Array.Empty<>()`; consumed only by `IsValid` | With `Overlap` gone, **no remaining `EditErrorKind` value describes a batch-level condition** — every one is a per-edit anchor refusal. The channel was therefore not merely empty but *untypeable*: there was no longer a well-formed value it could carry. Keeping it while deleting `Overlap` would have been the worst of the three options. |

**Why not stop at the forced two?** Because the alternative is a surviving contract in which one member is
a compile-time constant, one enum value is unreachable, and one collection cannot be given a meaningful
element — each individually defensible, collectively a surface that lies about what it can do. Under the
owner's standard (*"best practice … no producer and no consumer → deletion"*) the coherent cut is the
right one.

**Blast radius, measured, not assumed:** all three are consumed only by `ComposeEditAnchorPass` and two
seam-test files. Neither is a wire contract any more — the endpoint that serialized `BatchValidationResult`
is deleted in the same change, and no client ever read it (§1.2). If the owner prefers a narrower cut,
restoring any of the three is a local edit to `ComposeEditModels.cs` + `ComposeEditAnchorPass.cs`.

### 3.5 What was deliberately NOT touched inside surviving types

`ProposedEdit.Rationale` and `.Sources` are **kept** even though nothing server-side reads them today.
They are not fossils: they are the catalog-action payload mirror, and `ComposeEditActionAnchorContractSeamTests`
asserts that a schema-derived payload binds onto this record. Narrowing the mirror would break the
declared contract with `compose-draft-alternative` et al. — the same reasoning task 052 §6.4.2 used to
keep `target_text` on the `comments[]` channel.

---

## 4. 🔔 SURFACED — `ComposeEditAnchorPass` now has no production caller

**The POML's second escalation trigger fires.** Answering the question it required rather than assuming:

> *With the endpoint gone, does `ComposeEditAnchorPass` still have a caller?*

**No.** `ComposeEndpoints.cs:394` was its **only** call site anywhere in `src/`. After this task its
callers are exactly two test files:

- `tests/integration/seam/Compose/ComposeEditAnchorPassSeamTests.cs` (11 `Validate` calls)
- `tests/integration/seam/Compose/ComposeEditActionAnchorContractSeamTests.cs` (2 `Validate` calls)

`ComposeAnchorResolver` is one level further down: its only caller is `ComposeEditAnchorPass.cs:48`, so it
inherits the same status.

**Per the POML constraint, neither is deleted.** Task 052 kept the anchor pass deliberately, and the
ADR-043/041 assessment (§7, C-7) designates it *the Compose-owned home for closed-set validation* —
retiring it is an owner decision, not cleanup. Both files are untouched apart from the mechanical member
removals in §3 and a status note added to the class doc so the next reader is not misled into thinking a
request path runs through it.

**What the owner needs to decide (three genuine options, not a recommendation dressed as one):**

| Option | What it means |
|---|---|
| **(a) Keep as designated home** — status quo | The server-side closed-set validator stays ready for the moment a server-side edit path needs it (e.g. a future server-applied whole-document revision). Cost: ~180 lines + 13 seam tests maintained against no production traffic. |
| **(b) Wire it** | Give it a caller — e.g. have the whole-document revise path validate `target_para_id`s server-side before they reach the client. This is the option that makes the ADR-043/041 designation real rather than aspirational. |
| **(c) Retire it too** | Accept that placement validation is a client concern (`usePendingRedline` + `composeAnchorResolution.ts` already implement the identical contract in TS) and delete the server twin. Cost: the ADR-043/041 §7 C-7 designation would need amending (root CLAUDE.md §6.5 path B). |

**Not decided here.** Recording it so the decision is one line of owner input rather than a
re-investigation — the same discipline task 052 §1.4 used for the orphans this task just retired.

---

## 5. Prose / fixture sweep (POML constraint: "fix EVERY stale reference, incl. non-`.cs`")

| File | What was stale | Fix |
|---|---|---|
| `Services/Compose/ComposeEditModels.cs` | File header described the offset contract for `ResolvedMatch`/`MatchExample` and named `/edit-batch/validate` as a live consumer | Header rewritten; every removed type/member now has a tombstone note saying what it was and why it went |
| `Services/Compose/ComposeEditAnchorPass.cs` | Comment attributed cross-edit overlap to "the apply side (`ComposeEditBatch`)"; `BuildAnchorError` doc described `MatchCount`/`Examples` | Both rewritten; class doc gained the §4 no-production-caller status note |
| `Api/ComposeEndpoints.cs` | Route + handler + a 10-line handler comment describing the retired mechanism | Replaced with a tombstone recording why the endpoint went and what enforces the contract now |
| `Infrastructure/DI/ComposeModule.cs` | §F.1 note said the removed 052 registration's "sole consumer" was the endpoint — which is now also gone | Rewritten to cover all three removals and to state the stronger symmetry claim (§2) |
| `tests/Spaarke.ArchTests/ADR013_ComposeFacadeTests.cs` | XML doc named `ComposeEditBatch`/`ComposeEditTransaction` in its guarded-types list **twice**, incl. a claim that those files "cite this test by task number" | Type list refreshed; added a note that the guard is namespace-scoped so it covered the removal with no edit. Second mention re-pointed to surviving files. |
| `tests/integration/seam/Compose/ComposeEditAnchorPassSeamTests.cs` | Header enumerated the two enforcement mechanisms; three assertions read deleted members | Header gained the task-064 update **and the §4 caveat**; assertions replaced by the structural test (§3.3) |
| `tests/integration/seam/Compose/ComposeEditActionAnchorContractSeamTests.cs` | One `Matches` assertion | Replaced by a pointer to the structural test |
| `notes/wording-differs-elimination-trace.md` | Its §3.3 "surfaces cleared" table listed `/api/compose/edit-batch/validate` as an extant server surface | Row annotated: endpoint now deleted; row retained as the audit record of what was checked |

**Non-`.cs` sweep result:** `grep` for every deleted symbol + `edit-batch` across `.json`, `.ts`, `.tsx`,
`.md`, `.ps1`, `.yml` returns **no live reference**. Specifically checked, because task 052 was burned by
exactly this: `tests/integration/contract/Eval/golden-utterances.json` (clean — it was rewritten to the
anchored model in 052), `infra/dataverse/{actions,outputschemas}/**` (clean — never referenced the apply
side), `docs/**` and `.claude/**` (clean — zero hits for any deleted symbol).

**Deliberately left alone:** dated historical records — `projects/spaarkeai-compose-r2/**` (where the
surface was built), `projects/spaarkeai-compose-r3/**`, `projects/spaarke-ai-architecture-redesign-r2/notes/072-*`,
`notes/052-text-search-demotion-decisions.md` (this task's evidence base), `notes/adr-043-041-assessment.md`,
and `design.md` §"Code to retire". These are accurate statements about the state of the code at the time
they were written; rewriting them would destroy the audit trail that justified this task.

---

## 6. Verification

### 6.1 Suites

| Check | Baseline | After | Δ |
|---|---|---|---|
| BFF build (`dotnet build src/server/api/Sprk.Bff.Api/`) | — | **0 errors**, 7 warnings | 7 warnings are pre-existing `CS0618` in `DemoExpirationService.cs` / `RegistrationEndpoints.cs`, unrelated |
| `Sprk.Bff.Api.Tests` (incl. the `integration/seam/**` glob) | **11,294 P / 0 F / 97 S** (re-run in this worktree before touching anything, not taken on trust) | **11,277 P / 0 F / 97 S** | **−17, fully accounted** — see below |
| `Spaarke.ArchTests` | 62 / 62 | **62 / 62** | 0 |
| `Sprk.Bff.Api.IntegrationTests` | 96 P / 6 S | **96 P / 6 S** | 0 |

**The −17, exactly:**

| Change | Cases |
|---|---|
| `tests/unit/Sprk.Bff.Api.Tests/Services/Compose/ComposeEditBatchTests.cs` **deleted** (370 lines) | **−11** (11 `[Fact]`, 0 `[Theory]`) |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Compose/ComposeEditTransactionTests.cs` **deleted** (215 lines) | **−7** (7 `[Fact]`, 0 `[Theory]`) |
| `ComposeEditAnchorPassSeamTests.VerdictAndRefusalShapes_CannotExpressATextSpan` **added** | **+1** |
| | **= −17** ✅ |

Both deleted files were ADR-038 `domain-logic` KEEP-path tests for the two deleted classes — deleted
*with* the code they covered, per the ADR-038 constraint. No KEEP-path scenario is left uncovered: they
tested offset-span apply ordering and snapshot/rollback, behaviors that no longer exist anywhere.
Total count 11,391 → 11,374; **skipped unchanged at 97** (no test was disabled to make this pass).

### 6.2 BFF hygiene (root CLAUDE.md §10)

| Check | Result |
|---|---|
| **Publish size** | **45.03 MB compressed incl. PDBs** (47,215,692 bytes) · **215 files, 4 `.pdb`** · **raw dir sum 137.41 MB** (144,079,689 bytes) · `Sprk.Bff.Api.dll` 12,465,152 B |
| vs. baseline | Baseline **45.03 MB / 215 files / 4 `.pdb` / raw 137.41 MB**. **Δ ≈ 0** — a decrease is present but ~354 deleted source lines compile to a few KB of IL, below the 0.01 MB resolution of the reported figure. **14.97 MB under the 60 MB ceiling.** |
| Shell | **pwsh 7.6.3** (canonical per 052 §6.1 — Windows PowerShell 5.1 reports ~43.7 MB for the *same bytes*; the raw sum + file count are the shell-independent cross-check, and both match the baseline exactly) |
| New NuGet | **None** |
| CVE | `dotnet list package --vulnerable --include-transitive` → *"no vulnerable packages"* |
| Placement justification | **N/A — nothing added.** This task is net-negative BFF surface: −2 types, −1 endpoint, −2 DI registrations, −7 model types, −5 members. |

### 6.3 What verification caught that the change itself did not

**Three `.cs` files came back from the editor as pure LF** (`ComposeEditAnchorPassSeamTests.cs`,
`ComposeEditActionAnchorContractSeamTests.cs`, `ADR013_ComposeFacadeTests.cs` — CR byte count **0**,
verified with `od`, not `grep`, which reported them misleadingly as CRLF). `.gitattributes` pins
`*.cs text eol=crlf` and `.editorconfig` sets `end_of_line = crlf` specifically so local and CI agree;
`dotnet format --verify-no-changes` flagged **692 `ENDOFLINE` errors** — one per line of both seam files.
An untouched control file in the same project (`ComposeProjectSeamTests.cs`) verified clean, proving the
condition was introduced by this change rather than pre-existing. Converted back to CRLF; all changed
files across all three projects now pass `--verify-no-changes` with exit 0.

Left unfixed, CI's format check would have auto-formatted and pushed, rejecting the next push — the exact
failure mode the task brief warned about. Worth noting for future tasks: **`grep -c $'\r$'` is not a
reliable CRLF check in this environment; `od -An -tx1 | grep -c '^0d$'` is.**

Formatting was scoped to changed paths only (`--include`), never project-wide — a bare
`dotnet format` also "fixes" ~22 pre-existing IDE1006 violations in unrelated files.

---

## 7. ADR disposition

| ADR | Rule | Path |
|---|---|---|
| **ADR-049 I-7** | No text search as a placement mechanism. | **C — comply, and strengthen.** The retirement removes the last *type* in which a text offset could be expressed on this surface, converting a documented guarantee into a structural one. Pinned by `VerdictAndRefusalShapes_CannotExpressATextSpan`. |
| **ADR-010** | DI minimalism / symmetry. | **C — comply.** Both removed registrations were unconditional; §2. `ComposeModule` 13 → 11. |
| **ADR-013** | Tier-1 facade — Compose must not reach AI internals. | **C — comply.** Nothing added; `ADR013_ComposeFacadeTests` green (namespace-scoped, so it covered the removal without an edit). |
| **ADR-038** | Tests for deleted code are deleted with it; KEEP-path deletions need a same-PR replacement covering the same scenario. | **C — comply.** §6.1: the 18 deleted cases covered behavior that no longer exists, so there is no scenario to re-cover; the one guarantee worth preserving was re-expressed structurally (+1). |
| **ADR-043 / ADR-041** | No new dispatch protocol; catalog is DATA. | **C — comply.** No dispatch, no catalog change. But see **§4** — the assessment's §7 C-7 designation of `ComposeEditAnchorPass` is now unbacked by any production caller, which is an owner decision, not a violation. |
| **CLAUDE.md §11** | Component justification. | **N/A** — deletion-only; no new surface. |

**No §6.5 ADR-conflict escalation is raised.** The one POML escalation trigger that fired (§4, the anchor
pass losing its caller) is a *reporting* obligation the POML itself defines, and it is discharged here.

---

## 8. Files changed

**Deleted (4):**
- `src/server/api/Sprk.Bff.Api/Services/Compose/ComposeEditBatch.cs` (211 lines)
- `src/server/api/Sprk.Bff.Api/Services/Compose/ComposeEditTransaction.cs` (143 lines)
- `tests/unit/Sprk.Bff.Api.Tests/Services/Compose/ComposeEditBatchTests.cs` (370 lines)
- `tests/unit/Sprk.Bff.Api.Tests/Services/Compose/ComposeEditTransactionTests.cs` (215 lines)

**Edited (8):**
- `src/server/api/Sprk.Bff.Api/Services/Compose/ComposeEditModels.cs` (283 → 183 lines)
- `src/server/api/Sprk.Bff.Api/Services/Compose/ComposeEditAnchorPass.cs`
- `src/server/api/Sprk.Bff.Api/Api/ComposeEndpoints.cs`
- `src/server/api/Sprk.Bff.Api/Infrastructure/DI/ComposeModule.cs`
- `tests/Spaarke.ArchTests/ADR013_ComposeFacadeTests.cs`
- `tests/integration/seam/Compose/ComposeEditAnchorPassSeamTests.cs`
- `tests/integration/seam/Compose/ComposeEditActionAnchorContractSeamTests.cs`
- `projects/spaarkeai-compose-r8/notes/wording-differs-elimination-trace.md`

**Untouched by design:** `ComposeEditAnchorPass`'s and `ComposeAnchorResolver`'s behavior (§4),
`redlineTextSearch.ts`, `docxBridge.ts`, `ComposeTextFold.cs`, `AnnotationReanchorService.cs`, and all of
`src/client/**` / `src/solutions/**` / `infra/dataverse/**` (a parallel agent owned the Compose client
during this task).
