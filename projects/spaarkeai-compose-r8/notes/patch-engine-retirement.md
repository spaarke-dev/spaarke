# Task 074 — `ComposeShadowPatchEngine` retirement: **NOT-CONFIRMED (second time)**

> **Determination** 2026-08-26 · Task 074, FULL rigor · Supersedes nothing — it **re-affirms**
> [`gate-decision.md`](gate-decision.md) §5 on stronger, empirical evidence.
> **The engine is NOT deleted. It is still load-bearing.**

---

## 1. Verdict

Task 031 recorded `ComposeShadowPatchEngine` subsumption as **NOT-CONFIRMED** on the grounds that the
merge prototype never exercised the op-log path. The owner funded the confirmation work. That work is
done, and it did not confirm subsumption — **it disproved it.**

The engine is not a dormant transitional remnant. It is the **sole applier of user edits** for a save
shape the current shipped client is **regression-tested to emit**, and it is the **sole implementation**
of two user-visible recovery behaviours that have no render-path equivalent.

Per the POML's second escalation trigger — *"a consumer exists ... the engine is still load-bearing.
Surface it; do not delete and patch afterwards"* — nothing was deleted.

---

## 2. Proof 1 — the `SaveAsync` fork's op-log branch

**The gate** (`ComposeService.cs:1421`):

```csharp
if (request.ContentModel is null && (hasOperations || hasComments))
```

All **three** live `Apply` call sites collapse into this one branch:

| Site | Context | Reached from |
|---|---|---|
| `ComposeService.cs:1450` | the op-log apply itself | the gate directly |
| `ComposeService.cs:2755` | `ReanchorStaleSaveAsync` (stale-base re-anchor) | `:1400`, inside `gate && baseMoved` |
| `ComposeService.cs:2987` | `ApplyBestEffortByParagraph` (partial-apply recovery) | `:1472`, inside the gate's `catch` |

Verified sole-caller by grep: `ReanchorStaleSaveAsync` is called only at `:1400`;
`ApplyBestEffortByParagraph` only at `:1472`.

**Why this branch is load-bearing rather than redundant.** When `request.ContentModel is null`,
`ResolveSaveBaselineAsync` (`:1984`) returns the retained baseline **completely unmodified** — paths
(a) `:2043` and (b) `:2051` do zero rendering. The renderer is invoked **only** inside the
`request.ContentModel is not null` block. Therefore, on this branch, `_patchEngine.Apply` is the **only**
code that turns "the baseline" into "the document containing the user's edits."

Delete the engine without replacing it and the save does not fail — it **succeeds and silently discards
the entire editing session**, persisting the untouched baseline while reporting `Saved`. That is exactly
the dishonest-outcome class R8 task 013 (FR-S06) was built to eliminate.

---

## 3. Proof 2 — empirical (not inference)

### 3a. Server, through the wire — the branch is live and covered

`ComposePatchEngineSaveSeamTests` drives the **real** `POST /api/compose/documents/{id}/save` route via
`WebApplicationFactory`, posting `content + operationLog + comments` and **no** `contentModel`:

```
Passed!  - Failed: 0, Passed: 151, Skipped: 0, Total: 151 - Sprk.Bff.Api.Tests.dll (net10.0)
```

### 3b. Mutation — the assertions are non-vacuous

`_patchEngine.Apply` at `:1450` was temporarily replaced with a baseline passthrough, simulating the
deletion. Same suite, same command:

```
Failed!  - Failed: 107, Passed: 44, Skipped: 0, Total: 151 - Sprk.Bff.Api.Tests.dll (net10.0)
```

The mutation was reverted; the file's MD5 was verified identical to its pre-mutation backup
(`c5a99690bb075e776aa949e6a1053dd6`).

**The most important failure is not one of the 107 — it is the shape of the 44 that still "passed."**
The endpoint keeps returning HTTP 200. Representative failure:

```
Expected response.StatusCode to be HttpStatusCode.UnprocessableEntity {value: 422} because an
unresolvable paraId must be refused as a typed 422 ProblemDetails (ComposePatchErrorKind.ParagraphNotFound),
never a 500 or a silent partial write, but found HttpStatusCode.OK {value: 200}.
```

Without the engine there is no refusal, no 422, and no error — just a 200 over discarded work. The
failure mode of deleting this engine is **silent data loss**, not a crash.

### 3c. Client — the op-log shape is an *enforced contract*, not legacy residue

`ComposeWorkspace.tsx:2033-2050` selects the save shape. `importedBuilt` is null — forcing **Shape 3**
(`contentModel` absent, `operationLog` + `comments` present) — under five independent live conditions:
`!editorIsDirty`, `!state.docxBytes`, `!state.loadedContentModel`, a missing
`buildImportedContentModel` handle, or that mapper returning null. The client's own comment names the
third: *"legacy session / older BFF / **failed canonical projection** → null → the transitional op-log
shape below runs completely unchanged."*

A failed canonical projection is a **current-client-against-current-server runtime condition**, not
version skew. `ComposeService.cs:358` / `:479` set `contentModel = null` whenever
`ComposeProjectionStatus.Failed`, and `ComposeDocxProjectionBuilder.BuildContentModel` returns `Failed`
from a bare `catch` → `"projection-error"` (`:1963`). Note that `Build` (which decides whether the
document mounts) and `BuildContentModel` (which produces the model) are **two different walks** over the
same bytes, each with its own catch-all — so a document can mount and edit perfectly while yielding no
canonical model. That document's save lands on Shape 3.

This is not theoretical. Two shipped regression tests **enforce** the behaviour, and both pass today:

```
$ npx jest src/widgets/ComposeWorkspace.renderOnSave.test.tsx -t "op-log shape"
Tests: 16 skipped, 2 passed, 18 total
```

- `falls back to the op-log shape when the mapper returns null (editor unavailable)` — asserts
  `body.contentModel` is `undefined` **and** `body.operationLog.operations` equals `[CAPTURED_OP]`.
- `keeps the op-log shape untouched when the Load carried NO contentModel (legacy session / older BFF)`
  — same assertions.

Full suite: `Tests: 18 passed, 18 total`.

---

## 4. Proof 3 — repo-wide consumer grep

510 occurrences across 168 files (ripgrep, whole repo). Filtered to **production code**:

| File | Nature |
|---|---|
| `Services/Compose/ComposeService.cs` | **REAL** — field `:175`, ctor `:240`/`:260`, three `Apply` calls |
| `Infrastructure/DI/ComposeModule.cs:52` | **REAL** — `services.AddSingleton<ComposeShadowPatchEngine>()` (unconditional, symmetric) |
| `Api/ComposeEndpoints.cs` (3) | comments / XML-doc only |
| `Services/Compose/IComposeService.cs` (2) | XML-doc `<see cref=...>` only |
| `Services/Compose/ComposeDocumentRenderer.cs` (3) | comments only |
| `Services/Compose/Operations/ComposeOperation.cs` (1) | comment only |
| `Spaarke.Compose.Components/**` (TS, ~20) | comments only |

Everything else is `projects/**` and `docs/**` prose. **No consumer outside the Compose save path** —
which on its own would have supported deletion. The blocker is not an external consumer; it is that the
in-path consumer is live.

Note: there is no `IComposeShadowPatchEngine` — the engine is a `public sealed class` with no interface,
so the POML's "delete its interface" step has no referent.

---

## 5. What deletion would actually cost

### 5a. A capability gap, not merely test churn

`PartialApplySummary` and `ReanchorSummary` are wired **exclusively** to `ContentModel is null`:

- `:1396` gates the entire re-anchor call on the op-log branch. The ContentModel stale-base case
  (`:1381`) is a **different** branch that does not re-anchor — it proceeds last-writer-wins with a
  warning.
- `ApplyBestEffortByParagraph` (`:1472`) sits inside the engine `Apply`'s `catch`, structurally
  unreachable from the render path — and it **re-enters the same engine** per unit (`:2837`).

So the render path today has **no** stale-base re-anchor and **no** partial-apply recovery. Deleting the
engine deletes both user-visible behaviours. This is precisely gate-decision §5's third precondition,
now confirmed as a genuine capability gap rather than a coverage gap.

### 5b. Scope

~**98 test methods** across ~17 files (~72 compile-breaking, ~26 behaviour-dependent). Also
`tests/integration/seam/Compose/ComposeWritePathTextSearchAuditTests.cs` reads
`ComposeShadowPatchEngine.cs` **by path** and throws `InvalidOperationException` if it is absent — a
deliberate tripwire.

---

## 6. Gate-decision §5's three preconditions — final status

| # | Precondition | Status |
|---|---|---|
| 1 | Clean-apply round trip for a **reopened Authored** doc **through the render path** | ❌ **Not met.** `ComposeCleanApplySeamTests.Save_AuthoredOrigin_...` posts `operationLog` with **no** `contentModel` (`:230-231`) — it proves clean-apply through the **engine**. The only Authored+ContentModel test is born-in-editor *origination*, not reopened *editing*. |
| 2 | The op-log shape is **unreachable** from every shipped client | ❌ **Disproved.** Two shipped client tests enforce that it IS emitted; five live null-conditions produce it. |
| 3 | Coverage for **partial-apply recovery** | ❌ **Not met**, and worse than assumed — there is no render-path equivalent to cover. |

Zero of three. The engine survives.

---

## 7. Criterion that moved: the god-class waiver

The POML requires *"Its waiver entry is DELETED from `GodClassGuardTests.cs`."* **That file no longer
exists** — the God-class LOC ratchet was retired 2026-08-20 (root CLAUDE.md §11.5,
`docs/standards/COMPONENT-COMPLEXITY.md`) because it gated on line count, the wrong instrument.
Confirmed: `tests/Spaarke.ArchTests/GodClassGuardTests.cs` is absent; ArchTests run **62/62** green
without it; `CredentialGuardTests.cs:27` documents the retirement.

**This criterion is satisfied by the retirement itself.** There is no waiver to delete, and — importantly
— **no surviving FR-D01 miss on the waiver axis**. The gate-decision §5 warning that "one waiver
survives" is now moot: the waiver *mechanism* is gone. FR-D01's remaining content is the LOC target,
which §11.5 explicitly de-fanged. No substitute gate was invented, and no "get under 2,000 lines" goal
influenced this determination.

---

## 8. Recommendation

**Do not schedule 074 as a deletion again until the engine's capabilities are ported, not merely
re-measured.** The blocking work is real engineering, not evidence-gathering:

1. Serve the `ContentModel is null` + op-log/comments request shape through the merge path — or
   eliminate the shape client-side across all five null-conditions and hold a full deprecation window.
2. Port **stale-base re-anchor** and **partial-apply recovery** to the render path (they have no
   equivalent there today).
3. Then re-run this file's mutation experiment; deletion is safe when it produces zero failures.

Until then the correct disposition is: **the engine stays, and this is documented rather than
resolved by deleting a live engine** — the same recommendation task 031 made, now on evidence
strong enough to close the question rather than defer it.
