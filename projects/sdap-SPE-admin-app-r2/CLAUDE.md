# CLAUDE.md — sdap-SPE-admin-app-r2

> **Project context file.** Loads when working in `projects/sdap-SPE-admin-app-r2/`.
> **Root rules still apply** — see repo-root [`CLAUDE.md`](../../CLAUDE.md).

---

## 🚨 MANDATORY: Task Execution Protocol

**ABSOLUTE RULE**: execute tasks via the `task-execute` skill. Do **not** read a `.poml` and implement
manually. Bypassing loses ADR loading, checkpointing, and the Step 9.5 quality gates.

| User says | Action |
|---|---|
| "work on task X" | Invoke `task-execute` with that task's POML |
| "continue" / "next task" | Read `tasks/TASK-INDEX.md`, find first 🔲, invoke `task-execute` |
| "pick up where we left off" | Load `current-task.md`, invoke `task-execute` |

Parallel waves: ONE message with MULTIPLE Skill invocations — never sequential.

---

## What this project is

The SPE Admin app gives admins a UI for SharePoint Embedded, which Microsoft otherwise exposes only via
Postman, PowerShell, and CLI. **It has never been fully functional** — 4 of 9 screens fail outright, 1
fails silently, and the app reports success throughout.

R2 makes it work on the current platform. It is **NOT** a decomposition of `SpeAdminGraphService.cs` —
that framing was withdrawn 2026-08-20 and moved to `speadmingraphservice-decomposition-r1`.

**The systemic defect**: the app reports success when it is not succeeding. Storage silently zero; Sync
Status "OK"; a Settings field that controls nothing; error messages naming the wrong cause. For an admin
tool that is worse than being large — an operator cannot trust what it tells them.

---

## 🔔 Binding gates — read before touching auth

### ADR-028 / §6.5 — RESOLVED as path C, with a reopen condition

The spec's **ADR Tensions** section is completed. Workstream B performs the delegated exchange as the
**per-customer owning app**, which ADR-028 exception **E-1** already sanctions — so this is *compliance*
(path C), not an exception.

**Do not "fix" this by routing through the BFF app registration.** That lands in ADR-028 **A4** territory
(MUST use MI-FIC or KV certificate, never a client secret), trips E-3's *"does not license expansion"*
clause, and contradicts `spaarke-auth-v4-dataverse-MI`, which explicitly scopes `SpeAdminTokenProvider` /
`SpeAdminGraphService` **out** of its migration (its `design.md:149`).

**Reopen condition — task 010.** Two verified defects mean the owning-app path cannot currently succeed:

1. `SpeAdminTokenProvider.cs:142` requests scope `api://{OwningAppId}/.default` — an owning-app audience —
   while the token is handed to a Graph client (`SpeAdminGraphService.cs:4212`).
2. `SpeAdminTokenProvider.cs:306` builds the client as `Create(config.OwningAppId)`, but MSAL OBO requires
   the incoming assertion to be audienced to that same client; the code page authenticates against the BFF.

If task 010 shows the shape is unworkable, **STOP and re-run the §6.5 gate** — do not fall back silently.

### BFF hygiene (root §10) — every task

1. Load [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md) before designing any addition.
2. State the Placement Justification in the PR.
3. **Verify publish size**: `dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/`.
   Ceiling **≤60 MB**; baseline **~44.96 MB incl. PDBs**. ≥+5 MB single-task delta needs justification.
4. No new NuGet (NFR-02). **WireMock.Net 1.5.45 already exists** — do not add it.
5. No new HIGH CVE: `dotnet list package --vulnerable --include-transitive`.
6. Update tests in `tests/unit/Sprk.Bff.Api.Tests/`.

---

## Key facts, verified against code (2026-08-21)

| Fact | Location |
|---|---|
| `SpeAdminGraphService.cs` = **4,911 LOC** | `Infrastructure/Graph/` |
| `/beta` hardcoded — **3 sites** | `:925`, `:4195`, `:4212` |
| `StorageUsedInBytes: null` — **4 sites** | `:645`, `:976`, `:1060`, `:1110` |
| Wrong PATCH properties | `:3940` (`majorVersionLimit`), `:3945` (`storageUsedInBytes`) |
| `deletedDateTime` in `$select`, contradicted 11 lines below | `:4351` vs `:4362` |
| `catch (ODataError)` sites | **exactly 70** |
| Dead 3-line stub | `Services/SpeAdmin/SpeAdminGraphService.cs` |
| Misfiled endpoints | `Api/ContainerItemEndpoints.cs` → belongs in `Api/SpeAdmin/` |
| Tests make **no** HTTP call, stand up **no** host | 14 files, 359 tests |

**Correct v1.0 property names**: `itemMajorVersionLimit` (not `majorVersionLimit`) and
`maxStoragePerContainerInBytes` (not `storageUsedInBytes` — and it is a quota **ceiling**, not consumption).

---

## ⚠️ Scheduling constraint — the god-file

Nearly every task modifies `SpeAdminGraphService.cs`. **At most ONE task per wave may modify it.** Waves
pair that task with tasks owning different files (endpoints, client, tests, Azure config, notes).
Realistic concurrency is 2–3 agents, not 6. See [`plan.md`](plan.md) §3.

---

## Live-tenant safety (NFR-07)

✅ `spaarkedev1` ("Spaarke Dev", config "Spaarke PAYGO 1") is available for live testing.

> ⚠️ **Destructive tests MUST use a dedicated throwaway container.** The existing containers hold **real
> working documents** — signed NDAs, Compose drafts, matter files. Delete / permanent-delete /
> recycle-bin-purge / restore paths provision and tear down their own container. Read-only and additive
> operations may use the existing ones.

---

## Out of scope — do not build these

| Excluded | Why / where it lives |
|---|---|
| **Information barriers / ethical walls** | Owner decision 2026-08-21: not needed. Removed entirely, not deferred |
| **`SpeAdminGraphService.cs` decomposition** | `speadmingraphservice-decomposition-r1` (gated on A–E + harness green) |
| **Billing-profile attach** | `customer-provisioning-orchestration-r1` — PowerShell + Azure sub owner. We **read** `billingStatus` only |
| **Legal hold / retention / eDiscovery** | Microsoft Purview. We expose the container URL + deep-link instead |
| **SPE knowledge source (Foundry)** | Separate AI-architecture evaluation |
| **A `SpeAdminDriveService` interface** | Would be the third drive abstraction — fails §11 extension test |

---

## Testing (ADR-038)

- Repo convention is **`[Trait("Category", "LiveIntegration")]`** — *not* `[Category(...)]`.
- WireMock at the HTTP boundary is sanctioned; **`Mock<HttpMessageHandler>` is banned**.
- Coverage is observation, never a gate.
- `/test-diet` runs at project close (task 090) — mandatory.

---

## Conventions

- **Build**: `dotnet build src/server/api/Sprk.Bff.Api/`
- **Client**: `src/solutions/SpeAdminApp/` is a Vite code page — use
  `npm install --legacy-peer-deps --no-audit --no-fund`, **not** `npm ci`
- **Errors**: ProblemDetails per ADR-019 via `Infrastructure/Errors/ProblemDetailsHelper.cs`
- **Checkpoint**: every 3 steps / 5+ files / after any deployment — `context-handoff`

---

## Files

| File | Purpose |
|---|---|
| [`spec.md`](spec.md) | 31 FRs, ADR Tensions, owner clarifications — **the contract** |
| [`plan.md`](plan.md) | Phases, waves, effort, risks |
| [`design.md`](design.md) | Verified current state + root causes |
| [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) | Task registry + wave sequencing |
| [`current-task.md`](current-task.md) | Active task state (context recovery) |
| [`notes/spe-platform-research-2026-08-20.md`](notes/spe-platform-research-2026-08-20.md) | Platform research |
