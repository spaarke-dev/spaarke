# Current Task State — sdap-SPE-admin-app-r2

> **Last Updated**: 2026-08-27 (by `context-handoff`)
> **Recovery**: read Quick Recovery, then §1 (the open escalation). §6 is preserved history.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | 042 **complete** (was partial; finished this session). No task active. |
| **Phase** | Workstream D done. Wave W15+ (E-tasks) next. |
| **Status** | Branch clean, **committed but NOT pushed**. In sync with `origin/master` otherwise. |
| **Head** | `1b1d03b23` |
| **Next Action** | `git push`, then **task 050** (container archival) — see §2 |
| **Blocking?** | No. One **escalation awaits your decision** (§1) but it does not block 050. |

### Files modified this session
| File | Purpose |
|---|---|
| 15 SpeAdmin test files | 9 deleted whole, 6 pruned — see §3 |
| `tests/unit/domain/SpeAdmin/SpeAdminDtoMappingTests.cs` | **new** — 4 relocated mapper tests |
| `tests/Spaarke.ArchTests/ADR007_NestedDomainRecordTests.cs` | **new** — 2 rules + controls, replaces 6 ad-hoc reflection tests |
| `tests/Spaarke.ArchTests/CosmosProvisioningSecretGuardTests.cs` | **repaired a dead guard** — see §1 |
| `tests/Spaarke.ArchTests/ServiceBusClientGuardTests.cs` | scoped off `.Tests` projects |
| `projects/…/notes/manual-test-plan.md` | **new** — relocated plan, annotated |
| `projects/…/notes/test-retirement-inventory.md` | **new** — full classification record |

### Critical context
Merged `origin/master` (was 9 behind; CI files only). **SpeAdmin tests 722 → 207**, build 0/0, 207/207
pass, 0 skipped. Task 042 is done. The one thing needing you is §1.

---

## 1. 🔔 OPEN ESCALATION — a CATASTROPHIC-severity secret guard was dead, and now reports real findings

**This is the most important thing in this file.**

`CosmosProvisioningSecretGuardTests` (FR-27) was **not failing — it was not running.** Its loader pointed
at `src/server/services/Sprk.Provisioning.ControlPlane/`, a directory that **does not exist**; L2 was
split into `.Api` / `.Core` / `.Sidecar` / `.Worker`. Both Facts threw `FileNotFoundException` every run.

**Why this mattered more than a broken test**: it did not report *"I cannot check this."* It reported a
failure under the DisplayName **"types have no string-typed secret-shape properties"** — so a reader
would reasonably conclude the secret rule had been evaluated and had something to say. It had never run.
The invariant is documented **CATASTROPHIC** (Cosmos is a queryable audit log; a cleartext secret there
leaks to any Reader), and it has been dark since the split.

**Repaired** (loads every `Sprk.Provisioning.ControlPlane*` assembly — the scan comment always said
`*`, only the loader was single). It now reports **8 secret-shaped properties**:

| Property | My read |
|---|---|
| `SolutionVerificationRequest.ClientSecret` | 🔴 **real value** — KV-resolved, used to build a `ClientSecretCredential` |
| `ExchangePolicySidecarClient+SharedSecretResolution.Secret` | 🔴 likely real value |
| `ExchangePolicySidecarReadClient+SharedSecretResolution.Secret` | 🔴 likely real value |
| `PendingKvSecretWrite.SecretName` | probably a NAME (allowed — root CLAUDE.md §9) |
| `TrapVerificationRequest.KeyVaultName` | probably a NAME |
| `SlotKeyVaultRefSnapshot.KeyVaultReferenceIdentity` | probably an identity ref, not a value |
| `PerEnvYamlEntry.Key` / `PerEnvSettingEntry.Key` | probably a settings key name |

**I did NOT refine the regex to silence the last five.** Loosening a CATASTROPHIC security detector in
another team's code, based on my inference about which properties hold names vs values, risks silently
removing protection if I'm wrong once. That is the exact failure mode this project exists to remove.

### All 5 remaining ArchTest failures are real findings, all in `customer-provisioning-orchestration-r1`

| Failure | Finding |
|---|---|
| **FR-27** | the 8 above |
| **FR-F1 / FR-F2** | 4 unlisted `ClientSecretCredential` sites: `DataverseWebApiSolutionVerifier.cs:55`, `DataverseWebApiSolutionImporter.cs:185`, `DataverseWebApiEnvVarValuesWriter.cs:84`, `DataverseRegistryConcurrencyStore.cs:298` |
| **ServiceBus** | `ServiceBusModule.cs:144` — a real second production construction site |
| **ADR-010** | 1:1 interface ceiling drifted 153 → 155 (2 new interfaces, unidentified) |

**Deliberately not forced green.** FR-F2's own message says *"A failure here is NOT a prompt to update
the number."* Adding FR-27 exclusions would re-dark the guard just repaired.

⚠️ **None of the five block CI.** Tier-1's blocking subset is 7 named tests; none are these. They live in
Tier 2 / `adr-audit.yml`.

**Recommendation**: file against `customer-provisioning-orchestration-r1`; `ClientSecret` first.

---

## 2. Next Action — push, then task 050

```bash
git push
```
Then **050 — container archival**. Unblocked (deps 020 + 040 complete).

⚠️ **050, 051, 052 all modify `SpeAdminGraphService.cs`** → all `∥-safe: false` → **one at a time, main
session.** They also all write into `tests/unit/Sprk.Bff.Api.Tests/Api/SpeAdmin/`.

⚠️ **052 is destructive** (item recycle bin). The 041 fixture (`LiveIntegrationFixture`) provisions and
tears down its own container — use it; do not hand-run against a pre-existing container (NFR-07).

---

## 3. Task 042 result

| Metric | Before | After |
|---|---|---|
| SpeAdmin test **cases** | **722** (721 pass, 1 skip) | **207** (207 pass, **0 skip**) |
| Files | 15 in the non-KEEP location | 6 |
| `Spaarke.ArchTests` | 108 (102 pass / 6 fail) | 111 (106 pass / **5** fail) |

**Deleted whole (9)**: Phase2, Phase3, MultiAppSupport (the `Integration/SpeAdmin/` dir is gone),
MultiTenant, ContainerColumn, ContainerTypeEndpoints, ContainerTypePermission, RecycleBin,
**SecurityEndpoint** (your call — verified nothing external referenced it first).

**Pruned (6)**: Register −27, UpdateSettings −18, SearchItems −12, SearchContainers −18, Bulk −15,
CustomProperty −12. AMBIGUOUS tests **retained and marked** `// AMBIGUOUS (task 042):` for `/test-diet`
at task 090.

**Relocated, not deleted**: 4 mapper tests → `tests/unit/domain/SpeAdmin/`; 6 ad-hoc ADR-007 reflection
guards → one generalised ArchTest rule; both manual test plans → `notes/manual-test-plan.md`.

### Three findings from 042 (full detail in `notes/test-retirement-inventory.md`)

1. **34 tests were green against a feature that cannot execute.** `GetClientForOwningAppAsync`,
   `ValidateOwningAppSecretsAsync`, `FetchOwningAppSecretAsync` have **zero callers** in `src/`
   (grep-verified). Task 010's UNWORKABLE verdict at the test layer. The dead code is **still
   DI-registered and shipped** — a CLAUDE.md §11 removal candidate, out of 042's scope.
2. **2nd instance of tests defending a defect.** Two skip-token tests pinned a *numeric* offset scheme
   while claiming to mirror production, which forwards an *opaque* token. (1st was task 023's ten tests.)
3. **`ADR007_GraphIsolationTests` gap** — its allowlist exempts any namespace *containing*
   `Infrastructure.Graph`, so every nested domain record was unguarded. New rule closes it; return-type
   sibling narrowed to exempt `GraphServiceClient` (a factory returning a client is its contract), reason
   documented inline.

### Coverage gaps filed (real, pre-existing, none created by 042)
Security endpoints (no contract test at all) · Bulk operations (validation only ever mirror-tested; the
file's docstring claimed to cover `BulkOperationService`, which no test ever constructed) · container
columns · register error codes · CT-006 app-permissions · audit-logging on create · the
`NameIdentifier`/`sub` userId fallback.

---

## 4. Unrelated pre-existing flake — finally adjudicated

`SseStreamingIntegrationTests.Cancellation_NoLingeringBackgroundTask_AfterClientAbort` fails in the
full-project run and **passes in isolation** (167 ms) → order/timing-dependent, **flaky, not a
regression**. Two earlier CI runs never settled this because I cancelled both with my own pushes.
Recorded, not fixed. Relevant to master's `classify-and-retry.ps1` determinism work (PR #830).

---

## 5. Verification recipes worth reusing

- **Prove a failure is pre-existing**: `git stash -u` → run → `git stash pop`. Used twice this session;
  it is what turned "6 ArchTest failures" from an accusation into a fact.
- **Baseline before deleting tests**: `dotnet test … --filter "FullyQualifiedName~SpeAdmin"`. Never
  inherit a count from a POML — 042's said "14 files / 359 tests"; actual was **29 files / 722 cases**.
- `dotnet test tests/integration/seam/` fails **MSB1003** — that path is globbed into
  `Sprk.Bff.Api.Tests` via `<Compile Include="..\..\integration\seam\**\*.cs">`. Run the csproj.
- **All 8 KEEP categories compile into `Sprk.Bff.Api.Tests`** via csproj globs → relocating a test to a
  KEEP path is a file move, **no csproj change**.

---

## 6. Orchestration lessons (preserved)

1. `parallel-safe: true` describes the work, not the bookkeeping — both Wave A POMLs write `TASK-INDEX.md`.
2. An agent once silently skipped an instruction; caught only by `git status`. **Improved**: all 7 agents
   this session reported honestly, and one *correctly refused* a deletion I ordered — my ArchTest rule
   checked `Microsoft.Graph*` but not `Microsoft.SharePoint`, so it kept that guard. I then extended the
   rule. Give agents the standing to push back and they will.
3. A CI observation and a `git push` cannot be interleaved — two runs lost to my own pushes.
4. Stale POML `<status>`/`<deps>` have misled three times. `TASK-INDEX.md` is authoritative.
5. **Partition agent file-sets disjointly and forbid them running `dotnet build`** — concurrent builds on
   one project corrupt `bin`/`obj`. The orchestrator builds centrally, once.

---

## 7. Wave state

| Task | Status |
|---|---|
| **042** | ✅ complete |
| **050** | 🔲 **next** — `∥-safe: false` |
| **051**, **052** | 🔲 unblocked; 052 destructive |
| **090** | 🔲 `/test-diet` is a BINDING gate; it re-examines every AMBIGUOUS marker left by 042 |
| 025, 026, 029 | 🔄 **PARTIAL, not open** — do not restart |

**Not in the POML backlog**: the client typecheck+vitest gap · I2 cross-tenant search bleed (waived on
the deployment, not fixed — `JobContract` has no tenant field) · container-type DELETE does not exist ·
the `communications`/`emails`/`exports` folder origin (now answerable in one click via the File
Browser's **Modified By** column — worth doing **before** 052, which is destructive).
