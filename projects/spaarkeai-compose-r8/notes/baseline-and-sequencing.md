# R8 Task 002 — Publish-size baseline + `/conflict-check` + PR #266 sequencing decision

> **Status**: Complete
> **Rigor**: MINIMAL (no source modified — measurement + coordination note only)
> **Measured on**: commit `b182f1687` (working tree confirmed clean at measurement time), branch `work/spaarkeai-compose-r8`
> **Date**: 2026-08-20

---

## 1. TFM confirmation (before measuring, per NFR-05)

```
grep -o "<TargetFramework>[^<]*</TargetFramework>" src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj
→ <TargetFramework>net10.0</TargetFramework>
```

Confirmed **net10** before publishing. SDK: `dotnet --version` → `10.0.101`. HEAD at measurement start: `b182f1687ad8e70179be7234f8b7f160de94c588`, `git status --porcelain` clean.

---

## 2. Publish-size measurement

**Exact command** (per root CLAUDE.md §10 bullet 4 / `.claude/constraints/azure-deployment.md`):

```
dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/
```

Build succeeded (warnings only — pre-existing `DemoProvisioningOptions` obsolete-API warnings, unrelated to this task). Output tree: 215 files (218 zip entries incl. 3 directory entries), including 4 `.pdb` files — matches the documented `~215` entry-count convention.

**Compression methodology**: `Compress-Archive -CompressionLevel Optimal` over `deploy/api-publish/*`, matching the `dotnet-10-upgrade-r1` task 031 reference-baseline convention (PowerShell `Compress-Archive`, not `az webapp deploy`'s zip). The excl.-PDB figure was produced by compressing the same file set minus the 4 `*.pdb` files (flattened archive — 211 entries, no filename collisions verified against the incl.-PDB 211 non-PDB files).

| Measurement | Size | Bytes | vs 44.96 MB reference baseline | vs 44.05 MB reference baseline |
|---|---|---|---|---|
| **Incl. PDBs** (4 files) | **44.97 MB** | 47,155,446 | **+0.01 MB** | — |
| **Excl. PDBs** | **44.07 MB** | 46,211,375 | — | **+0.02 MB** |

**PDB convention stated explicitly**: the 44.96 MB / 44.05 MB reference baseline (`dotnet-10-upgrade-r1` task 031, 2026-08-13) is incl./excl. PDBs respectively, framework-dependent linux-x64 publish. This measurement follows the same convention.

**Delta interpretation**: +0.01 MB / +0.02 MB is within normal compression noise (file timestamps, zip metadata ordering) for an **unchanged dependency tree** — no package references, no source files, and no `Sprk.Bff.Api.csproj` changes exist between this measurement and the 2026-08-13 reference. This is expected: task 002 modifies nothing under `src/`. **This number is the R8 baseline every later BFF-touching task in this project reports its own delta against** — not the 2026-08-13 figure directly (though they agree to the noise floor).

**Escalation check**: 44.97 MB is far below the 55 MB architecture-review threshold and the 60 MB hard ceiling. No escalation triggered.

---

## 3. `/conflict-check` — scoped to the Compose spine

Per the task constraint, this run was scoped to the **named Compose spine file list**, not "the BFF" generally:

- `src/server/api/Sprk.Bff.Api/Services/Compose/**`
- `src/server/api/Sprk.Bff.Api/Api/ComposeEndpoints.cs`
- `src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeWorkspace.tsx`
- `src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeEditor.tsx`
- `src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeAiToolbar.tsx`
- `src/client/shared/Spaarke.Compose.Components/src/widgets/hooks/usePendingRedline.ts`
- `src/client/shared/Spaarke.Compose.Components/src/utils/docxBridge.ts`

**Open PRs** (`gh pr list --state open --json number,title,headRefName,files`, 24 open PRs at measurement time): filtered every PR's file list against the spine glob set above. **Zero open PRs touch any spine file.** (PR #266 touches only `Sprk.Bff.Api.csproj` — not a spine file; see §4.)

**Other active worktrees** (`git worktree list`, 60+ worktrees; explicitly checked all prior/parallel Compose projects — `compose-r2` through `compose-r7`, `compose-fidelity-r4.5`, `compose-templates-r8` — plus a full sweep of every other worktree's `git status --porcelain` scoped to the spine paths): **clean**. No other worktree has uncommitted edits inside the named spine paths.

**Result: CLEAN** against the named spine file list, across both open PRs and all other worktrees.

### Live in-worktree activity observed during this task — includes a named spine file

The state below evolved twice while this task ran; both snapshots are reported for transparency.

**First observation** (mid-measurement): `src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeBannerStack.tsx` (+8 lines, diff annotated `FR-S02 (r8 task 011)`) appeared modified in **this same worktree**, though the tree was clean at `b182f1687` when this task started. `ComposeBannerStack.tsx` is not on the POML-named spine list.

**Second observation** (just before finalizing this note — `git status --porcelain` re-run): two further files are now uncommitted, and one **is** a named spine file:

```
 M src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeBannerStack.tsx
 M src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeWorkspace.saveErrorRouting.test.tsx
 M src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeWorkspace.tsx   <-- ON THE NAMED SPINE LIST
```

`ComposeWorkspace.tsx` diff (+26/-28 net, restructuring) and its paired test file (`ComposeWorkspace.saveErrorRouting.test.tsx`, +33/-6) are consistent with a concurrent same-session agent actively executing Track S task **010 (client save error routing / FR-S01)** and/or **011 (concurrency last-writer-wins / FR-S02)** in this same worktree, in parallel with this task.

**Why this does not trigger the POML's escalation trigger, stated precisely**: the trigger fires on "another active worktree with uncommitted edits inside `Services/Compose/**`" — i.e. an *inter-project* collision on the *server* tree, detected via `/conflict-check`'s worktree/PR sweep. This is instead an *intra-project* collision: other tasks of **this same R8 project**, dispatched by the main session into **this same worktree**, already mid-flight on the **client** spine (not `Services/Compose/**`). The `/conflict-check` sweep across all *other* worktrees and all open PRs (§3 above) is genuinely clean — no external project is touching these files. This finding is about task-level parallelism inside r8 itself, which is the main session's own orchestration to sequence, not a cross-project conflict this skill is built to catch.

**Why it is reported anyway**: the task brief states "the main session is holding off on BFF source edits until you report" — technically true (no `Services/Compose/**` server file was touched), but `ComposeWorkspace.tsx` is explicitly named on *this task's own* spine-file scoping constraint, and it was edited while task 002 — a P0 `startable`-gate task with `deps: none` that every later BFF-touching task is meant to report against — was still in flight. The main session should confirm task 010/011 were deliberately started in parallel with task 002 (both are legitimately `parallel-group: P0` / early-phase per the project's task numbering) rather than assume task 002's completion was a gate on Track S client work starting.

---

## 4. PR #266 sequencing decision

`gh pr view 266 --json number,title,state,headRefName,baseRefName,files,mergeable,createdAt,updatedAt`:

- **State**: OPEN, `dependabot/nuget/.../DocumentFormat.OpenXml-3.5.1` → `master`
- **Change**: `src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj`, 1 line (`DocumentFormat.OpenXml` 3.4.1 → 3.5.1) — no other files
- **Created**: 2026-03-23; **last updated**: 2026-05-16 (~5 months open); automatic rebases disabled (>30 days open policy)
- **Release notes** (3.5.1): purely additive API surface (new `ChartDrawing` classes/attributes/enum) — no documented breaking changes, no CVE/security driver named

### Decision: **HOLD** — defer PR #266 past the Phase-3 fidelity gate (merge only after task 031 `gate-decision-adr-amendment` closes)

**Reasoning (control-vs-gate measurement consistency):**

The deciding factor named in the POML is whether the Phase-2 **control measurement** (task 023) and the Phase-3 **merge-prototype/gate measurement** (task 030) can be taken on the **same** `DocumentFormat.OpenXml` version. Track A (tasks 040-045) is the OOXML merge logic that both measurements characterize, and it is built directly on this library between those two tasks. Two sequencing options both preserve consistency in principle — GO (land before Phase 2) or HOLD (defer past the gate) — but they are not equally safe operationally:

1. **GO requires an actively-timed merge action with no natural trigger in the task sequence.** Task 002 (this task) is explicitly forbidden from touching the package reference — GO would require a *separate*, precisely-sequenced action to land PR #266 before task 023 executes. Given Phase 0/1 tasks (this one, Track S 010-017) run first and Track S itself is scoped as its own standalone deploy phase, there is real risk that task 023 fires before anyone has explicitly merged #266, landing the version bump in the ambiguous middle of the exact window the POML is trying to protect.
2. **HOLD requires zero additional coordination.** Simply not merging #266 is a no-op — the control measurement (023) and the gate measurement (030) both run against the current pinned `3.4.1`, guaranteed, with no dependency on a timed external action.
3. **No urgency justifies accepting GO's timing risk.** PR #266 is a routine dependabot minor bump, not a security patch (no CVE cited in the PR body), and it has already sat open for 5 months with no operational pressure. The cost of holding it a few more weeks through Phase 2-3 is zero.
4. **The gate is R8's highest-consequence measurement.** Per `projects/spaarkeai-compose-r8/CLAUDE.md` §"Phase 3 is a real gate": Phase 4 does not start until the corpus hits 100% near-tier / ≥95% overall preservation with zero hard-fails, and a miss escalates to the owner rather than being improvised around. Introducing *any* uncontrolled variable (a different serializer version) into that measurement's numerator (Phase 3) without it also being present in the denominator (Phase 2 control) would make a fidelity regression or improvement structurally unattributable — exactly the failure mode the POML names.

**Action for the owner / main session**: do not merge PR #266 until task 031 (`gate-decision-adr-amendment`) has closed. At that point it is a normal, low-risk standalone dependency-bump PR — Track A's merge logic and the gate corpus will already be validated against `3.4.1`, and Track D (decomposition) has no OpenXml-version sensitivity. Re-flag this decision if the gate timeline stretches long enough that `3.4.1` accumulates its own maintenance risk (not observed as of this measurement).

---

## 5. Acceptance-criteria confirmation

- [x] Measured publish size stated with exact command, TFM confirmed net10, PDB convention stated explicitly.
- [x] Delta vs 44.96 MB baseline stated as signed number: **+0.01 MB** (incl. PDBs); **+0.02 MB** vs the 44.05 MB excl.-PDB reference.
- [x] `/conflict-check` results name every colliding worktree/file: **CLEAN across all other worktrees and all 24 open PRs** (the check this task is scoped to run); **one in-worktree, intra-project, same-session concurrent edit** on a named spine file (`ComposeWorkspace.tsx`, plus its test file and `ComposeBannerStack.tsx`) is named explicitly in §3 for transparency, with reasoning for why it is not an escalation-triggering collision.
- [x] GO/HOLD decision recorded with control-vs-gate measurement-consistency reasoning: **HOLD**.
- [x] NEGATIVE: no package reference was modified.
- [x] NEGATIVE: no source file under `src/` was modified (by this task).
