# Design — sdap-SPE-admin-app-r3: decompose `SpeAdminGraphService`

> **Created**: 2026-08-31
> **Predecessor**: [`sdap-SPE-admin-app-r2`](../sdap-SPE-admin-app-r2/) (merged — PRs #859, #907)
> **Status**: seeded, awaiting `/design-to-spec`
> **Sponsor's original objective**: this is the objective r2 was chartered for and did not deliver.

```xml
<hot-path-declaration>
  <bff>Y</bff>                <!-- Infrastructure/Graph/SpeAdminGraphService.cs + Api/SpeAdmin/** -->
  <spaarke-ai>N</spaarke-ai>
  <ci-workflows>N</ci-workflows>
  <skill-directives>N</skill-directives>
  <root-claude-md>N</root-claude-md>
</hot-path-declaration>
```

---

## 1. Why this project exists — and why r2 didn't do it

**r1/r2 were sponsored to break up a ~4,500-line god file.** r2 redirected to making the SPE Admin app
actually work, because 4 of 9 screens failed and 1 failed silently. That redirect was correct, and
§4 argues it made *this* project safer. But two things went wrong with how the deferral was handled,
and this document exists partly to make sure they don't recur:

1. **The file grew 52% during the project chartered to shrink it.**

   | | Lines | Public methods |
   |---|---|---|
   | At r2 start (2026-08-20) | **4,320** | — |
   | On master now (2026-08-31) | **6,545** | **168** |
   | Delta | **+2,225 (+52%)** | |

2. **The deferral pointed at a project that was never created.** r2's design deferred Workstream F to
   `speadmingraphservice-decomposition-r1`. That folder does not exist and never did. A name in a
   design document is not a deferral — it is a disappearance. **This folder is the correction.**

Compounding both: the `GodClassGuardTests` LOC ratchet was **retired on 2026-08-20** — the same day r2
began. Its retirement was defensible on the merits ([CLAUDE.md §11.5](../../CLAUDE.md): line count is
the wrong instrument and it was blocking normal feature work). But its replacement — human judgment
plus the **non-blocking, never-wired-in** `scripts/report-large-server-files.ps1` — meant nothing was
watching. Three individually reasonable decisions produced an unremarked 52% growth.

> ⚠️ **This is NOT an argument to re-instate an LOC gate.** See §7 — the operator has explicitly
> ruled that this file must not become a CI issue, and §11.5's reasoning stands.

---

## 2. The problem, stated in the terms the standard actually uses

[CLAUDE.md §11.5](../../CLAUDE.md) and [`docs/standards/COMPONENT-COMPLEXITY.md`](../../docs/standards/COMPONENT-COMPLEXITY.md)
are explicit: **evaluate complexity and cohesion, never line count.** A large *cohesive* file — a state
machine, an exhaustive mapping — is legitimate and should be left alone.

So the case here is not "6,545 is a big number". It is that **the responsibilities have diverged**.
111 public async methods across **nine** distinct domains, each with its own reason to change:

| Domain | Public async methods |
|---|---|
| Container types | 21 |
| Drive / items | 20 |
| Permissions & owners | 16 |
| Containers (CRUD/lifecycle) | 13 |
| Recycle bin / deleted | 12 |
| Columns | 8 |
| Custom properties | 4 |
| Security (alerts, secure score) | 4 |
| Search / audit | 4 |

Nine reasons to change in one type. Container-type settings and drive-item enumeration have nothing
to do with one another and change for unrelated reasons; today an edit to either risks the same file.

---

## 3. Scope

### In scope

- **Extract the nine domains** into cohesive services behind the existing facade boundary.
- **Preserve every public contract.** This is a pure refactor: no behaviour change, no endpoint change,
  no payload change. The contract tests are the proof.
- **Keep the `…ForConfigAsync` / `…ForUserAsync` split** — it encodes the app-only vs delegated
  distinction, which is load-bearing (container-type ops are delegated-only; see r2 findings).
- **Retire the `-r1` naming placeholder** by superseding it: this project *is* the decomposition.

### Out of scope

- Behaviour changes, new features, new endpoints.
- Re-instating any LOC gate (§7).
- The UI/client layer.
- Anything inherited from r2 that r2's own 090 resolves (see §6).

---

## 4. Why now is safer than it was at r2 planning time

**This is the strongest argument for the redirect having been right**, and it should survive into the spec.

Decomposing at r2 planning time would have meant refactoring code that *fabricated settings values,
silently discarded custom properties, mis-stated the cause of security failures, and could not create
a container type at all*. That is moving broken behaviour into tidier boxes — the refactor would have
preserved the defects and made them harder to find.

Today, **each of the nine domains has contract tests pinning its actual Graph wire shape**, written
after the defect they guard:

| Domain | Guarding tests |
|---|---|
| Recycle bin | 15 contract tests (restore/delete have opposite failure semantics) |
| Security | 13 (incl. absence-vs-failure, and the corrected access-denied wording) |
| Container types | 8 (`owningAppId` present, `trial` sent as trial, unmappable classification throws) |
| Custom properties | 3 (sub-resource URL, unwrapped body root) |
| Columns, quota, archival, URL, mapping, search | existing contract suites |

A refactor with wire-shape tests underneath it is a mechanical, verifiable change. Without them it is
a rewrite with extra steps. **The safety net now exists; it did not before.**

---

## 5. ADR tensions to resolve at spec time

Per [CLAUDE.md §6.5](../../CLAUDE.md), these must be surfaced now, not discovered mid-task.

| # | Tension | Initial reading |
|---|---|---|
| **T1** | **ADR-010 (DI minimalism)** — nine new services must NOT become nine new `IFooService` interfaces. ADR-010 permits only two seams | Path **C (comply)**: register **concretes**. Extraction is not a licence for interface-per-class — that is AI smell #1 in `code-review` |
| **T2** | **ADR-007 (Graph types isolated)** — the facade exists so Graph SDK types never reach endpoints. Nine services must not each leak them | Path **C**: keep the facade boundary; extracted services return domain records exactly as today |
| **T3** | **CLAUDE.md §11 (component justification)** — nine new classes would each need `<justification>` | Likely **not applicable**: this is *extraction from existing surface*, not new surface. Spec must state this explicitly so `code-review` Step 6.6 doesn't flag it |
| **T4** | **BFF §10 placement** | Trivially satisfied — nothing moves out of the BFF. State it anyway; §10 requires the sentence even when the answer is obvious |

---

## 6. Inherited from r2 — only if r2's 090 doesn't resolve them

r2 is **not** closed. These belong to r2 unless its wrap-up leaves them:

- **UAT §1A** — including the container-type create fix, which is **reasoned, not proven** (create is
  delegated-only; an app-only probe gets 403, so it could not be verified from this side).
- **`Add Permission`** — *skipped, not passed*. No evidence either way.
- **`SearchItemsTests`** — 7 HTTP contract tests at the non-KEEP path `tests/unit/Sprk.Bff.Api.Tests/**`,
  one of which makes a **real outbound Dataverse call**. A plain `git mv` would relocate a network
  dependency *into* a KEEP path; it needs an offline double or deletion of that one method.
  Full analysis: [`../sdap-SPE-admin-app-r2/notes/test-diet-report.md`](../sdap-SPE-admin-app-r2/notes/test-diet-report.md).
- **Task 050** — the SPE archival opt-in probe (24 h replication retry).

---

## 7. 🔴 Operator constraint — CI must not gate on this file

**Binding, stated by the operator 2026-08-31.** This file must not become a CI issue.

Verified current state — **nothing gates on it, and nothing should be added**:

- No CI job gates on file size (apparent grep hits were false positives on "lo**c**ations"/"b**loc**ker").
- `GodClassGuardTests.cs` is **absent from master**; the ratchet was retired 2026-08-20.
- The three ArchTests referencing this file check **content**, not size (ADR-007 Graph-type isolation,
  credential census) — and they pass. Full ArchTest run 2026-08-30: 5 failures, all
  provisioning/DI/ServiceBus, **none SpeAdmin**.

**Therefore**: this project MUST NOT add an LOC gate, re-instate `GodClassGuardTests`, or wire
`scripts/report-large-server-files.ps1` into CI. Progress is measured by **cohesion** — services
extracted, contracts preserved, tests still green — not by a line-count threshold. Any proposal to add
a size gate is a §6.5 escalation, not a task.

---

## 8. Success criteria (to be sharpened by `/design-to-spec`)

1. Nine domains extracted into cohesive services; each has one reason to change.
2. **Zero behaviour change** — every existing contract test passes **unmodified**. A test that needed
   editing to accommodate the refactor is evidence the refactor changed behaviour.
3. No new interfaces (T1); no Graph SDK types escaping the facade (T2).
4. `dotnet build -c Release` 0 errors / 0 warnings; publish size within the 60 MB ceiling.
5. No CI gate added (§7).

---

## 9. Open questions for `/design-to-spec`

| # | Question | Why it matters |
|---|---|---|
| 1 | One service per domain (9), or coarser grouping (e.g. containers+columns+properties as one)? | Nine thin services can be worse than one cohesive file. Decompose where reasons-to-change diverge, not to hit a count |
| 2 | Does `SpeAdminGraphService` remain as a thin facade delegating to the nine, or do endpoints inject the services directly? | Facade preserves ADR-007 and makes the change invisible to callers; direct injection touches every endpoint. Facade is the safer default |
| 3 | Shared helpers (`ExecuteWithRetryAsync`, `SendGraphJsonAsync`, `ResolveGraphBaseUrl`, `GetClientForConfigAsync`) — where do they live? | These are used by every domain. Wrong answer here recreates a smaller god file |
| 4 | One PR or one per domain? | Nine PRs are individually reviewable but invite long-lived divergence on a hot-path file |
