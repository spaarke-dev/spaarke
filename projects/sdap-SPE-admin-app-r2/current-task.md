# Current Task State — sdap-SPE-admin-app-r2

> **Last Updated**: 2026-08-27 (by `task-execute`, mid-task 050)
> **Recovery**: read Quick Recovery, then §0 (task 050) and §1 (the older open escalation).

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | **050 — container archival** (FR-E01), implementation complete, gates running |
| **Phase** | Wave W15 — Workstream E |
| **Status** | 042 pushed (`b2aff6e5a`). 050 code written, BFF build 0/0, 16/16 archival contract tests pass |
| **Next Action** | Finish gates: full test run, vite build, publish size. Then commit + escalate the opt-in (§0.4) |
| **Blocking?** | 050's `<escalation><trigger>` **HAS FIRED** — see §0.4. Everything not gated on it is done. |

### Files modified — task 050
| File | Purpose |
|---|---|
| `Infrastructure/Graph/SpeAdminGraphService.cs` | `ArchiveContainerAsync` / `UnarchiveContainerAsync` + `ForConfig` wrappers, `ArchivalNotEnabledException`, `ReadContainerStatus` + `ReadArchiveStatus`, `Status` → nullable |
| `Api/SpeAdmin/ContainerEndpoints.cs` | `POST …/archive` + `…/unarchive`, 409 remediation payload, `ContainerDto.ArchiveStatus` |
| `tests/integration/contract/SpeAdmin/SpeAdminContainerArchivalContractTests.cs` | **new** — 16 tests incl. 2 negative controls |
| `SpeAdminApp/src/types/spe.ts` | `ContainerArchiveStatus`, `ArchivalActionAccepted`, `status` nullable |
| `SpeAdminApp/src/services/speApiClient.ts` | `containers.archive` / `.unarchive` |
| `SpeAdminApp/src/components/containers/ContainersPage.tsx` | Archive/Restore toolbar + ConfirmModal + Archive column + Status honesty fix |
| `SpeAdminApp/src/components/containers/ContainerDetail.tsx` | Status absent-state + Archive row |
| `projects/…/notes/task-050-findings.md` | **new** — all measurements |

### Critical context
Archival is **beta-only** in Graph — and that is fine, the container surface is *already* pinned to
beta by task 020's measured decision, so **no ADR conflict and no §6.5 gate**. Two defects were found
and fixed on the way (§0.2, §0.3). The feature cannot be live-verified: the container type has not
opted in and that is an operator action (§0.4).

---

## 0. Task 050 — container archival (FR-E01)

### 0.1 🔴 The documented PowerShell remediation does not exist

POML AC-4 requires the not-opted-in error to "name the PowerShell remediation". Every source in the
repo — POML, spec FR-E01, `design.md` §4.3, `knowledge/sharepoint-embedded/` — says
`Set-SPOContainerType -IsArchiveEnabled`. **That parameter does not exist on that cmdlet in any
module version** (verified by reflecting the cmdlet types out of the module assembly).

The real one is **`Set-SPOContainerTypeConfiguration -ContainerTypeId <guid> -IsArchiveEnabled $true`**,
and it needs SPO module **≥ 16.0.27515.12000** (the commonly-installed 16.0.26413.0 has no archive
parameter on any cmdlet). Following the POML literally would have shipped an error message telling an
admin to run a command that does not exist — the project's signature defect, inside the feature meant
to remove it. Full detail: [`notes/task-050-findings.md`](notes/task-050-findings.md) §1.

**The four source docs still carry the wrong cmdlet.** Correcting them is not done yet — see §0.5.

### 0.2 🔴 `status` was fabricated as "active" for 100% of responses, everywhere

`SpeContainerSummary.Status` defaulted to `"active"` and all four mapping sites ended `: "active"`.
`status` is in the **v1.0 schema**, so the Graph SDK models it as a **typed property** and Kiota never
puts it in `AdditionalData` — which is where all four readers looked. The lookup could not match on any
path, so the literal fired every time: GET-single (Graph really returns `active`), CREATE (Graph really
returns `inactive`), and both LIST paths. A brand-new, not-yet-activated container was reported active.
The client had a second `?? "active"` on top. Both fixed; `Status` is now `string?` and renders
"Not reported". Regression-guarded by two contract tests.

### 0.3 Kiota shape guess — caught by a test, not by review

`archivalDetails` arrives as **`Microsoft.Kiota.Abstractions.Serialization.UntypedObject`**, not
`JsonElement` and not `IDictionary<string,object>`. My first `ReadArchiveStatus` handled the latter two
and returned null for every real response. Caught because the contract test asserted the *mapped value*
rather than that the code ran. (Scalars differ — `storageUsedInBytes` arrives as `decimal`, so task
024's storage fix is genuinely working; checked while I was in there.)

### 0.4 🔔 ESCALATION FIRED — archive/restore cannot be live-verified from here

Live probe on a **throwaway container** (NFR-07: provisioned, activated, probed, torn down 204/204):

```
POST /beta/storage/fileStorage/containers/{id}/archive
  → 403 notAllowed: "Archival operation cannot proceed because this
                     application does not currently support archiving."
```

The 403 is **semantic, not routing** — the beta action exists and is reachable; the container type has
not opted in. So AC-1 and AC-2 (archive succeeds / restore returns to active) are **not verified**.

Enabling it is an operator action and I did not do it: it is a tenant-level change to a **shared**
container type (`Spaarke PAYGO 1`) other projects use, and it needs the module upgrade in §0.1.
Recipe to finish the verification is in `notes/task-050-findings.md` §7.

⚠️ **Watch item**: `archivalDetails` has never been seen on the wire — omitted from LIST and from
GET-single even with an explicit `$select` that `@odata.context` echoes back. If it is still absent
after a successful archive, the property is unserved and the grid must source archive state from the
action outcome + `Get-SPOContainer -ArchiveStatus` instead. The code isolates this in one mapper.

### 0.5 Gates — all green

| Gate | Result |
|---|---|
| BFF build | **0 errors / 0 warnings** |
| BFF tests | **10,661 passed / 0 failed** / 95 skipped |
| SpeAdmin contract tests | **92/92**, incl. 16 new archival |
| ArchTests | **106/111** — the same 5 pre-existing `customer-provisioning-orchestration-r1` failures as before this task; none mine, none in Tier-1's blocking set |
| Client typecheck | **124 errors = baseline exactly** (`git stash` diff), 0 new |
| Vite build | ✅ 19.2 s; strings verified present in `dist/speadmin.html` |
| Publish size | **45.08 MB** incl PDBs (44.16 excl). Baseline 44.96 → **+0.12 MB**. Ceiling 60 |
| CVE | none |
| ADR-check | 0 violations, 1 warning (test path — resolved path C, see below) |

**Docs corrected** (all 5 that carried the non-existent cmdlet): `spec.md` FR-E01, `design.md` §4.3,
`knowledge/sharepoint-embedded/docs/learn-containers.md`, `notes/spe-platform-research-2026-08-20.md`
(annotated, left as historical record), and the 050 POML.

**Deviation — test location.** The POML nominated `tests/unit/Sprk.Bff.Api.Tests/Api/SpeAdmin/`;
task 042 established that is **not** a KEEP path, so tests written there would be deletion candidates
at the `/test-diet` gate (task 090). Written to `tests/integration/contract/SpeAdmin/` instead — same
assembly, no csproj change. §6.5 path C (comply); the POML predates 042's finding.

### 0.6 Remaining on 050
Nothing in code. **The one open item is the operator opt-in in §0.4** — until then AC-1/AC-2 stay
unverified and 050 is 🔄, not ✅, in `TASK-INDEX.md`.

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
