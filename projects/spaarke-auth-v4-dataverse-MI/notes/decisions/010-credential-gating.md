# Decision record — 010: MI-flag gating, and the decoupling of app-only from OBO

> **Task**: `tasks/010-fix-mi-flag-gating-defect.poml` · **Completed**: 2026-08-20 · FULL rigor
> **Owner decision**: **Option A — decouple** (approved 2026-08-20 after the escalation in `BLOCKED.md`)

---

## 1. The defect (FR-A1)

`DataverseAccessDataSource` and `DataverseWebApiClient` selected their credential from **secret
presence**, never reading `Graph:ManagedIdentity:Enabled`. On dev the secret **is** present — because
OBO needs it — so both classes ran on the **client secret** despite MI being enabled.

That is not a hypothetical: it means any prior observation of these two files on dev was
characterising the *secret* code path, not the MI path. It matters to this project and to
`dataverse-access-unification-r1`, whose parity testing would otherwise have compared the wrong thing.

## 2. What the escalation caught

`DataverseWebApiClient` was a clean ~20-line fix — it has **no OBO path**, so gating its app-only
credential is all there is to do. `DataverseAccessDataSource` was not:

```csharp
if (secret present) {
    _credential = new ClientSecretCredential(...);   // (1) app-only token
    _cca = ...WithClientSecret(...).Build();          // (2) OBO confidential client
} else {
    _credential = credential ?? new DefaultAzureCredential();
    _cca = null;                                      // (2) OBO DISABLED
}
```

**One `if`, two unrelated concerns.** Task 010 step 2 said to copy the gating shape from
`DataverseWebApiService`. That shape is a plain `if (useManagedIdentity) { MI } else { secret }`.
Applied verbatim here, `_cca` lands in the `else` branch — so with `Graph:ManagedIdentity:Enabled=true`
(the intended dev end-state, **already live**) `_cca` becomes `null`, and every delegated Dataverse
access check throws at `GetDataverseTokenViaOBOAsync`. A total fail-closed outage, introduced by the
task meant to be a safe prerequisite.

`DataverseWebApiService` is a safe template **only because it has no OBO path**.

## 3. The fix — decouple

| Concern | Now gated by |
|---|---|
| **(1) `_credential`** — app-only token | `Graph:ManagedIdentity:Enabled` ← this is FR-A1's actual target |
| **(2) `_cca`** — OBO confidential client | Presence of OBO configuration. **Independent of the flag** |

Rationale, beyond "it doesn't break": `DefaultAzureCredential` **cannot** perform an OBO exchange
(ADR-028 A4), and the MI flag says nothing about delegated access — so tying them together was
incorrect on its own terms. The decoupling makes the code state what is actually true: app-only and
delegated auth are different concerns with different credentials.

It is also **exactly the seam Phase 2 needs**. Task 020 replaces the credential *inside* (2) with the
MI-FIC assertion and never touches the app-only branch. Left entangled, task 022 would have had to
untangle this during the highest-blast-radius migration in the project.

Also added: fail-fast validation on the secret branch, naming the missing setting, so a
misconfiguration surfaces at construction instead of as an opaque failure at first token request.

## 4. Test shape — a second ADR-038 finding, and how it was resolved

The first version of `CredentialSelectionSeamTests` asserted the selected credential type and OBO
presence **by reflecting on private fields**. That is **ADR-038 ban B8** (`tests/CLAUDE.md` —
"internal/private method tests via `InternalsVisibleTo` or reflection"). Caught at the quality gate and
rewritten.

A behavioural alternative for those specific assertions does **not** exist:
`DataverseAccessDataSource` is deliberately fail-closed and swallows credential errors into
`AccessRights.None`, so credential selection is genuinely not observable through its public surface.

**Resolution — split by test shape rather than weaken the ban:**

- **Kept here (8 tests, behavioural, no reflection)**: which configuration each flag state *requires*,
  and that a missing setting fails fast naming itself. This is real behaviour and it does catch the
  original defect — under the fix, `flag=true` no longer requires a secret.
- **Deferred to `tests/Spaarke.ArchTests/` (task 060)**: the structural guard that `_cca` is never
  assigned inside the managed-identity branch. Source analysis is the shape ADR-038 sanctions for
  precisely this, it is the shape task 060 already builds, and it is *stronger* than a reflection test
  because it fails at the shape level rather than on one sampled configuration.

**Obligation booked onto task 060**: add that guard. Without it, a future "simplification" back to a
single `if` would reintroduce the outage silently. This is recorded rather than assumed.

## 5. Verification

| Check | Result |
|---|---|
| Seam tests | ✅ **8 passed**, 0 reflection |
| `Spaarke.Dataverse.csproj` unchanged (no ProjectReference, no package) | ✅ zero diff |
| `LayerDependencyTests` FR-14 | ✅ passes |
| Build | ✅ 0 errors |
| Publish size | **43.67 MB** compressed incl. PDBs — unchanged vs the pre-task build; baseline 44.96, ceiling 60 |
| CVE scan | ✅ no vulnerable packages |

### ⚠️ Pre-existing CI failure, NOT caused by this task

`GodClassGuardTests` FR-14 (the god-class ratchet) is **red**, on two files this task never touched:

| File | Lines | Frozen baseline |
|---|---|---|
| `Spaarke.Dataverse/DataverseServiceClientImpl.cs` | 2975 | 2864 |
| `Sprk.Bff.Api/Api/ComposeEndpoints.cs` | 2755 | 2651 |

`ComposeEndpoints.cs` is **byte-identical to `origin/master`**, which alone proves the gate is red
independently of this branch. Both files arrived via the master merge (`88784e7d4`) from `#3b` and
`compose-r7`. Task 010's working diff contains neither file.

Per [`god-class-ratchet.md`](../../../.claude/patterns/testing/god-class-ratchet.md) the remedy is
**decompose, or re-baseline the waiver with a documented PR reason — never silently**. Neither is this
project's to decide: the growth belongs to the projects that caused it. **Raised for the owner rather
than absorbed.**

## 6. Acceptance criteria

| # | Criterion | Result |
|---|---|---|
| 1 | Flag true + secret present → MI credential | ✅ (behavioural: construction succeeds without requiring the secret path) |
| 2 | Flag false + secret present → secret | ✅ |
| 3 | Flag true + no secret → MI | ✅ |
| 4 | Negative: flag false + no secret fails fast, actionable | ✅ names `API_CLIENT_SECRET` |
| 5 | Negative: OBO still fails CLOSED when the user lacks rights | ✅ unchanged — no authorization logic touched; `_cca` preserved under MI, which is what keeps delegated checks evaluating as the user |
| 6 | `LayerDependencyTests` FR-14 passes; csproj unchanged | ✅ both |
| 7 | Publish size reported; no new HIGH CVE | ✅ 43.67 MB; clean |

## 7. Metadata correction made during this task

**Group A was misclassified as parallel-safe.** Tasks 010 and 011 both declared
`parallel-safe: true`, and TASK-INDEX claimed *"different files"* — but **both modify
`DataverseAccessDataSource.cs`**. Concurrent sub-agents would have collided mid-edit. Corrected to
`parallel-safe: false` on both, group re-marked **`010 → 011` sequential**. The error was in the
authored metadata, so any future dispatcher would have hit it.
