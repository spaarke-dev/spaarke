# Track B — Placement Justification + verification record (task 060, FR-B01)

> **Task**: `060-durable-byte-store.poml` — durable, tenant-partitioned byte copy of every chat session upload
> **Date**: 2026-08-25
> **Rigor**: FULL · opus @ xhigh
> **Worktree**: `C:\code_files\spaarke\.claude\worktrees\agent-a956acfa6ebc68125` (branched from **master @ `845b4cdc9`**, not from `work/spaarkeai-compose-r8`)

---

## 1. Placement Justification (root CLAUDE.md §10 bullet 2 — required even when the answer is "in BFF")

**Decision: in the BFF, at `src/server/api/Sprk.Bff.Api/Services/Ai/Sessions/SessionFileBlobStore.cs`.**

Against the [`bff-extensions.md` §A pre-merge checklist](../../../.claude/constraints/bff-extensions.md):

| Criterion | Answer |
|---|---|
| **A.1 — Could this live OUTSIDE the BFF?** | No. The write is **inline in the OBO request scope** of `POST /api/ai/chat/sessions/{id}/documents`: the bytes exist only in that request (`originalBinary`), and FR-B01 requires the copy to be durable *before* the endpoint answers. An Azure Function (ADR-001's out-of-band home) would require either shipping the bytes through a job payload — forbidden by **ADR-015** ("MUST NOT place document bytes in Service Bus job payloads", ADR-004) — or re-reading them from the 4h Redis cache that is itself the thing we do not trust. A separate deployable fails all four refined-ADR-013 exception criteria (no independent scaling need, no separate release cadence, no team boundary, no isolation requirement). |
| **A.2 — Which ADRs bind?** | ADR-014 + ADR-015 (tenant partitioning, retention, no content in logs), ADR-010 (DI minimalism — one registration, concrete type, no new interface), ADR-032 (symmetric/unconditional registration), ADR-013 (lives under `Services/Ai/`, no CRUD→AI dependency introduced), ADR-007 (this is explicitly **not** SPE), ADR-038 (KEEP-path tests). |
| **A.3 — Publish-size regression?** | **No package added.** `Azure.Storage.Blobs` 12.29.1 was already a direct reference with **zero** consumers in `src/` (verified by grep — the "existing consumer" at `UploadFinalizationWorker.cs:610` is a stub that returns `new MemoryStream()` and touches no blob type). Measurement in §7. |
| **A.4 — New CRUD→AI direct dependency?** | No. The store lives under `Services/Ai/Sessions/` and is consumed only by `Api/Ai/ChatDocumentEndpoints.cs`, which is already AI-side. No `PublicContracts/` facade is needed because no CRUD code crosses into it. |
| **A.5 — Feature-module DI convention?** | Yes — registered inside `AddAiPersistenceModule` (the existing AI-persistence composition root that already owns `CosmosClient`, `ISessionPersistenceService`, `IMemoryItemStore`). No new module, no `Program.cs` line. |
| **A.6 — New config field on the playbook surface?** | N/A — §G governs Action/Node/Playbook config homes. This adds two **app-configuration** keys (`SessionFileStore:BlobEndpoint`, `SessionFileStore:ContainerName`), not Dataverse columns. |

### Why blob and not the alternatives

| Candidate | Verdict |
|---|---|
| **Cosmos** | Rejected. Cosmos holds JSON documents, not bytes; the `sessions` container is Tier-3 work history with a document-size ceiling. (Spec §11 records the owner's 7-container check.) |
| **SPE (`SpeFileStore`, ADR-007)** | Rejected — explicitly. SPE is the matter/BU-scoped DMS. Per-user chat scratch would inherit its permission model (container/drive ACLs keyed to matters, not to a chat session), its retention model, and its writer-identity rule (`.claude/patterns/auth/spe-writer-identity-matching.md`). It would also pollute the DMS with ephemeral uploads users never filed. |
| **Keep it in Redis, longer TTL** | Rejected. Redis is the hot tier (ADR-009); a 90-day binary TTL turns a cache into a system of record and multiplies memory cost by the full upload corpus. |
| **Blob (chosen)** | Storage account, containers and role assignment already defined in `infrastructure/bicep/modules/storage-account.bicep`; managed-identity auth; per-blob cost ~0; natural home for opaque bytes. |

---

## 2. §11 three-question gate — the ONE new component

**Component**: `SessionFileBlobStore` (+ its internal `SessionFileBlobGateway` collaborator).

1. **Existing — what does this overlap with?**
   Nothing that is wired. Verified by grep, not assumed:
   - `grep -rn "BlobContainerClient|BlobServiceClient|Azure.Storage.Blobs" src/server/ --include=*.cs` → **one hit**, and it is the package reference at `src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj:100`. **There is no blob code in the BFF at all.**
   - `src/server/api/Sprk.Bff.Api/Workers/Office/UploadFinalizationWorker.cs:600-616` (`RetrieveTempFileAsync`) is the GH #231 stub. It is a **read** stub on the Office-add-in → temp-blob → SPE finalization path: a different direction (read, not write), a different lifecycle (transient hand-off, deleted after SPE upload), a different identity (MI background worker, no `tid` claim — it derives a tenant from `TENANT_ID` config, `:584`), and a different destination (SPE). Completing it would not produce a durable session-file copy, and forcing session uploads through it would drag chat scratch into the SPE finalization path — the placement this task explicitly rejects. **Escalation trigger 2 is therefore answered: the stub is NOT the intended seam.** (It remains open as GH #231.)
   - `SpeFileStore` — the DMS facade; see §1.
   - `ITenantCache` / Redis binary (`doc-upload-binary`, 4h TTL) — the hot tier this store is the durable counterpart to, **not** a replacement. Both still run.

2. **Extension — can I extend an existing component instead?**
   No, and the two candidates fail for concrete reasons rather than taste. `UploadFinalizationWorker` is a background worker, not a store, and its blob interaction is a transient read (above). `SpeFileStore` is `ADR-007`'s DMS facade whose every operation is drive/container-scoped — a session-scoped, tenant-partitioned byte path has no drive to resolve. The store is 1 file, 1 registration, ~150 lines of logic.

3. **Cost-of-doing-nothing — what concretely fails?**
   A conversation reopened after ~24h cannot recall from its own uploaded files. Concretely: `StoredSession.UploadedFiles[].SearchDocumentIdsCsv` (Cosmos, 90 days) keeps pointing at `spaarke-session-files` chunk ids that `SessionFilesCleanupJob` has already deleted (it sweeps on Redis-key absence; the session Redis key has a 24h sliding TTL), and the original bytes were only ever in Redis under `doc-upload-binary` with a **4h** TTL (`ChatDocumentEndpoints.UploadDocumentTtl`). Both recall and the R7 re-attach chip degrade to "no longer available" while the conversation is still in History. That is the R7 UAT point 1b defect verbatim, not a hypothetical.

---

## 3. What was verified rather than assumed

The task brief asked for verification of the POML's premises. Results:

| POML claim | Verified? | Evidence |
|---|---|---|
| Blob infra already provisioned in `storage-account.bicep`, wired at `customer.bicep:145` | ✅ **True** | Module + wiring read in full. |
| Provisioned containers fit | ⚠️ **Partly** — see §4 | Actual containers are `temp-files`, `document-processing`, `ai-chunks` (`customer.bicep:45`; `stacks/model1-shared.bicep:139-142` adds `customer-exports`; `stacks/model2-full.bicep:153`). There is **no** `session-files` container. |
| "with managed-identity RBAC" | ❌ **NOT true for two of the three stacks** — see §5 | `storage-account.bicep:150-160` creates the `Storage Blob Data Contributor` assignment only `if (!empty(appServicePrincipalId))`. **`customer.bicep:145-155` does not pass that param. `stacks/model1-shared.bicep:131-143` does not pass it either.** Only `stacks/model2-full.bicep:146-158` does. |
| `Azure.Storage.Blobs` 12.29.1 already referenced | ✅ **True** | `Sprk.Bff.Api.csproj:100`. |
| `UploadFinalizationWorker.cs:610` is its only consumer, and it is a stub | ⚠️ **Half true** | It is a stub (GH #231), but it is **not a consumer at all** — it references no blob type. The package had zero consumers. |
| ADR paths `.claude/adr/ADR-014-tenant-isolation.md` / `ADR-015-data-partitioning.md` | ❌ **Do not exist** | The real files are `ADR-014-ai-caching.md` and `ADR-015-ai-data-governance.md`. Both nevertheless carry the binding rules cited (ADR-014: "MUST scope keys by tenant"; ADR-015: "MUST scope all persisted AI artifacts by tenant", "MUST partition all Tier 2/3 data by `tenantId`"). Read and applied. The POML's `<knowledge><files>` paths are stale and should be corrected. |

---

## 4. Container decision (escalation trigger 1 — surfaced, not escalated-and-stopped)

**Chosen: `ai-chunks`, overridable via `SessionFileStore:ContainerName`. No bicep change made. The store never creates a container.**

Reasoning:
- `ai-chunks` is provisioned in **all three** deployment stacks, is in the **AI domain**, and has **zero** existing consumers in code or docs (`grep -rn "ai-chunks" src/ docs/ .claude/` → only bicep). No collision risk.
- Its only lifecycle rule (`storage-account.bicep:120-135`, tier-to-Cool after 30 days) is **non-destructive** and cost-favourable for content read rarely after 30 days. That rule is gated on `enableTestDocumentLifecycle`, which `customer.bicep:153` sets to **`false`**, so in customer deployments no lifecycle policy exists at all — nothing will delete these blobs.
- `temp-files` and `document-processing` were rejected on naming: both promise a transient lifecycle that this content must **not** have, and a future operator adding a cleanup rule to a container called `temp-files` would silently destroy 90-day session content.

**Owner decision left open (non-blocking):** a dedicated `session-files` container would read better than `ai-chunks`. That is one array entry in `customer.bicep:45` + the two stack files, plus setting `SessionFileStore:ContainerName`. It is deliberately **not** assumed here, per the task's "if a new container is genuinely required, that is a bicep change to justify, not to assume".

---

## 5. 🔔 Operator action required before this works in a deployed environment

Code-complete ≠ working. Two configuration/infra facts, both **owner decisions**, both outside this task's "no new Azure resource" boundary:

1. **App setting `SessionFileStore:BlobEndpoint` is not set anywhere.** No bicep file emits a storage app-setting today (`grep -i storage infrastructure/bicep/modules/app-service*.bicep` → nothing). Until it is set to the storage account's blob endpoint (bicep output `storagePrimaryEndpoint`, e.g. `https://sprk<customer><env>sa.blob.core.windows.net`), the store runs **disabled**: uploads still succeed, and the store logs a warning once per process. That is a deliberate fail-soft so nothing regresses — but it also means **the feature is inert until someone sets the key.**

2. **The Storage Blob Data Contributor role assignment is missing in `customer.bicep` and `stacks/model1-shared.bicep`** (see §3). Even with the endpoint set, writes will 403 in those stacks. Additionally, `model2-full.bicep` grants the role to `bffApi.outputs.appServicePrincipalId` (the **system-assigned** identity), while the BFF's `TokenCredential` is pinned to the **UAMI** in `Graph:ManagedIdentity:ClientId` when configured (`ManagedIdentityCredentialFactory.Create`). If a UAMI is pinned, the grant must be on the **UAMI's** principal id, not the system-assigned one.

Neither was changed here: both are bicep edits and role assignments, i.e. infrastructure decisions.

---

## 6. Verification record — every test was observed to FAIL before it passed

A green suite is not evidence. Each assertion below was watched failing against a deliberately broken implementation first.

### 6a. Tenant isolation — `tests/integration/tenant/Ai/SessionFileBlobStoreTenantIsolationTests.cs`

**Break applied**: the tenant segment was removed from `SessionFileBlobStore.BuildBlobName` (producing `session-files/{sessionId}/{fileId}`) and the `AssertTenantPartitioned` guard was disabled — i.e. exactly the careless-refactor shape this guard exists to catch.

```
Failed!  - Failed: 4, Passed: 15, Skipped: 0, Total: 19
  Write_PlacesTheBlobUnderTheCallingTenantsPrefix [FAIL]
  Read_FromAnotherTenant_WithTheSameSessionAndFileIds_MustNotReturnTheBytes [FAIL]
  TwoTenants_UsingIdenticalSessionAndFileIds_GetIndependentBytes [FAIL]
  Read_IsCaseSensitiveOnTheTenantSegment [FAIL]
```

with the decisive one reading:

```
Expected crossTenantRead to be <null> because a tenant must not be able to read another tenant's
session-file bytes by knowing the session id and file id (ADR-014 / ADR-015 ...), but found
SessionFileBytes { Content = PRIVILEGED — tenant A settlement figures. Must never reach tenant B.,
                   ContentType = "application/pdf" }
```

That is a real cross-tenant read, not a string mismatch. It is real because `InMemorySessionFileBlobGateway` resolves blob names the way Azure Blob does — opaque, ordinal, exact-match, **no path semantics** — so whether the read hits is decided entirely by the name the production code composed. Restoring the tenant segment turned all 19 green.

### 6b. Upload seam — `tests/integration/seam/Ai/SessionDurableFileStoreSeamTests.cs`

**Break applied**: step 9c's `durableFileStore.WriteAsync(...)` call was replaced with a stub returning `Written` — the pre-task-060 world, where the upload still 202s, still writes Redis, still indexes and still updates the manifest.

```
Failed!  - Failed: 4, Passed: 1, Skipped: 0, Total: 5
  Upload_WritesADurableByteCopyAtUploadTime [FAIL]
  Upload_StillWritesTheSessionCacheAndManifest_AlongsideTheDurableCopy [FAIL]
  TwoTenantsUploadingToTheSameSessionId_ProduceSeparateDurableCopies [FAIL]
  Upload_DurableCopyIsReadableByTheUploadingTenant [FAIL]
```

**Honest note**: `Upload_DurableCopy_IsNotReadableByAnotherTenant` **passed** under the break — vacuously, because nothing had been written. A cross-tenant-read assertion is not self-sufficient; it only means something alongside a positive control that proves the bytes were there to leak. That is why the positive controls (`..._IsReadableByTheUploadingTenant`, `TwoTenants...`) are in the same file, and why the tenant suite peeks the literal blob name to prove the write survived the missed read.

### 6c. A real bug found and fixed during review — the `$` anchor

The segment validator was first written as `^[A-Za-z0-9][A-Za-z0-9._-]*$`. **In .NET, `$` also matches immediately before a trailing `\n`**, so `"11111111-…-555555555555\n"` satisfied the pattern and a newline would have reached the blob name (and any header, URL or log line derived from it). Observed:

```
Write_RejectsSessionIdsThatCouldEscapeTheTenantPrefix(craftedSessionId:
  "11111111-2222-3333-4444-555555555555\n") [FAIL]
Failed! - Failed: 1, Passed: 10, Skipped: 0, Total: 11
```

Fixed by anchoring `\A…\z` (the true end-of-string anchor). The trailing-`\n` and `\r\n` cases are now permanent `[InlineData]` rows so re-introducing `^…$` breaks the build's tests, not production. Note `\r\n` was rejected even under `$` — only the bare-`\n` case was reachable, which is exactly why it needed an explicit case rather than reasoning.

---

## 7. Gate results

| Gate | Result |
|---|---|
| Baseline suite (before any change, this worktree) | **10,615 passed / 0 failed / 97 skipped** (`master @ 845b4cdc9`) |
| Suite after change | **10,665 passed / 0 failed / 97 skipped** — +50 tests, zero regressions |
| New DI registrations | **+1** (`SessionFileBlobStore`), unconditional. Repeatable metric `grep -rEn "services\.(AddSingleton\|AddScoped\|AddTransient\|AddHostedService)" src/server/api/Sprk.Bff.Api/ --include=*.cs \| wc -l`: **568 → 569**. (This counts every branch of every feature gate, so it is not the same instrument as ADR-010's "265 at Phase-3 baseline" — it is used here only for the before/after delta.) ADR-010's "≤15 non-framework lines" principle is a long-standing, documented, accepted violation (ADR-010 "Phase 5 baseline (2026-05-26)"); this task adds exactly the one registration the spec §11 table budgets for. |
| ArchTests (`tests/Spaarke.ArchTests/`, incl. `ADR010_DITests`, `ADR013_AiBoundaryTests`, `ADR007_GraphIsolationTests`) | **56 passed / 0 failed** |
| Full solution build | succeeded; zero warnings attributable to the new/changed files |
| New NuGet packages | **0** |
| New Azure resources | **0** |
| New secrets in configuration | **0** — the endpoint is a bare URI and a secret-bearing value is refused at construction |
| Publish size (compressed, incl. PDBs, `Compress-Archive -CompressionLevel Optimal` **under pwsh 7**) | **45.00 MB** — 215 files, 4 `.pdb`, raw dir sum ~137.4 MB, framework-dependent; **+0.04 MB vs the 44.96 MB net10 baseline**; 15.00 MB under the 60 MB ceiling; far below the +5 MB escalation threshold. Zero `.csproj` delta, so the change is code only. **This row is CORRECT as originally written** — see the note below. |

> **⚠️ Publish-size measurements in this project diverged by ~1.3 MB for months. The cause is the SHELL, not the tree, and not a dirty output directory.** Settled empirically 2026-08-25 during task 052 verification, by zipping the *same directory* twice in the same minute:
>
> | Shell | `Compress-Archive -CompressionLevel Optimal` |
> |---|---|
> | Windows PowerShell **5.1** (what `powershell` resolves to from Git Bash) | **43.73 MB** |
> | **pwsh 7.6.3** (what the `PowerShell` tool and CI use) | **45.03 MB** |
>
> Different `System.IO.Compression` implementations, identical input. Neither figure was an artifact — the two long-standing clusters in this project's notes (43.68–43.74 vs 45.00–45.04) are simply the two shells.
>
> **Canonical tool: `pwsh` 7.** It is what the repo's `PowerShell` tool and CI invoke, and it reconciles with the 44.96 MB net10 baseline at +0.07 MB — whereas PS 5.1 would imply an implausible −1.23 MB drop, which is itself the evidence that the baseline was taken under pwsh 7.
>
> **Method — pin the shell explicitly:**
> ```
> rm -rf <out>
> dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o <out>
> pwsh -Command "Compress-Archive -Path '<out>\*' -DestinationPath '<out>.zip' -CompressionLevel Optimal -Force"
> ```
> Report the **raw directory sum (~137 MB) and the file count (215 / 4 `.pdb`) alongside** the compressed figure. Those are shell-independent, so a mismatch there is a real content change while a mismatch in the zip alone is a tooling difference. An earlier "correction" of this row (which blamed a dirty output directory) was itself wrong and has been reverted.
| `dotnet list package --vulnerable --include-transitive` | **no vulnerable packages** |

> **Note on the suite baseline**: the task brief cited 11,179 passing. That is the count on `work/spaarkeai-compose-r8`. This worktree was created from **master @ `845b4cdc9`**, where the count is 10,615 — the difference is the R8 branch's own added tests, not a regression. The delta that matters here is 10,615 → 10,653 with 0 failures.

---

## 7a. Step 9.5 quality gates — findings and what was done

`code-review` and `adr-check` were run as independent Opus reviewers over the change set. Both maximised recall. Verdicts: tenant partitioning **held under every attack constructed** (Unicode/homoglyph, case-folding, traversal, separator injection, encoded forms, length, whitespace); ADR-001/007/008/009/010/013/014/032 + §F.1 (incl. transitive) + root §9/§11 all COMPLIANT.

### Fixed in this task (path C — pivot to comply)

| Finding | Fix |
|---|---|
| **C1 — deployment landmine.** `appsettings.template.json` shipped a POPULATED `BlobEndpoint`, so any rendered environment would have the store ENABLED while 2 of 3 bicep stacks create no role assignment → 403 → **500 on every chat upload**. Documenting the break is not mitigating it. | `BlobEndpoint` now ships **empty**. Disabled is the default; enabling is an explicit opt-in with both prerequisites stated inline. |
| **C2 — vacuous test.** `Upload_DurableCopy_IsNotReadableByAnotherTenant` asserted only `BeNull()`, so it passed when nothing was written — and the class docstring falsely claimed it among the tests observed failing. | Positive controls added (202 + `Count == 1` + owning-tenant read succeeds) BEFORE the negative. Docstring corrected to state which test passed vacuously and why. Re-observed under the step-9c break: **6 of 6 now fail**, no vacuous pass remains. |
| **ADR-019 violation.** Both new 500s omitted the mandated stable `errorCode` and `correlationId`. | Added `errorCode = "session.durable-store-failed"` + `correlationId = TraceIdentifier` at both sites, and a seam test that drives a failing write through the wire and asserts 500 + the code + **no manifest entry**. Observed failing under a fail-soft policy (`found HttpStatusCode.Accepted`). |
| **W1 — "fails fast at construction" was false.** The DI registration is a factory lambda, so a bad endpoint would first throw on a user request, and on every request after. | Validation extracted to `SessionFileBlobStore.ValidateConfiguration`, called from `AddAiPersistenceModule` at **composition** time as well as from the ctor. A misconfiguration now stops the host. Doc claims corrected. |
| **W2 — no 403/404 diagnostics**, i.e. the most likely production failure produced the least actionable log. | `Actionable()` translates 403 → "needs Storage Blob Data Contributor on the account behind `SessionFileStore:BlobEndpoint`; note storage-account.bicep only creates it when appServicePrincipalId is passed", and 404 → names `SessionFileStore:ContainerName` and states that this store never creates containers. Also narrowed the read-path 404 swallow to `BlobErrorCode.BlobNotFound`, so a **missing container** no longer masquerades as "no such file". |
| **W5 — the hoist left TWO identical content-type switches** (`startedContentType` ~140 lines earlier). | Replaced both with one `ResolveContentType(extension, file)`. Telemetry, the durable blob's `Content-Type`, and the manifest entry are now the same string by construction. |
| **S3 — container name was the one unvalidated name component**; a `/` would have reshaped the blob path and moved the tenant out of first position. | Azure container-name rule enforced in `ValidateConfiguration`, with theory cases incl. `has/slash`. |
| **S4 — trailing-dot segments accepted.** | Rejected; `[InlineData]` case added. |
| **S5 — `AssertTenantPartitioned` was an untested tripwire** that a future refactor could delete silently. | `BuildBlobName_PutsTheTenantFirst_SoTheAssertTripwireCannotBeSilentlyRemoved` pins the property directly. |
| **https not enforced** — `Uri.TryCreate(Absolute)` accepted `http://`, which would put the MI bearer token on the wire in cleartext. | Scheme check + theory cases. |
| **S2 / docstring accuracy** — `ai-chunks` DOES have a lifecycle rule (`tier-ai-chunks-to-cool-after-30d`), and the tenant suite's "never assert on a path shape" claim was contradicted by two of its own tests. | Both corrected in the XML docs. |
| **W3 (bounded part)** — the `X-Tenant-Id` header fallback is now the partition key of a *durable* store. | Cannot responsibly remove it under a store-scoped task (see §9), but both durable-write sites now log a **Warning** naming the tenant when it came from the header rather than a claim. Escalated below. |

### NOT fixed — escalated (see §10)

15-A (retention/deletion undefined), 15-B (ADR-015 governed-stores table row), 10-A (`design.md` Placement Justification + `<hot-path-declaration>`), W3-full (remove the header fallback), W4 (same-tenant cross-user overwrite), S1 (orphan blobs), 38-A (test-path placement).

## 8. Deliberate non-goals (owned by later Track B tasks)

- **No delete/erase API.** Task 061 must make `SessionFilesCleanupJob` evict the hot index only; the strongest form of that guarantee is that the job has **no reachable code path to durable bytes**. `SessionFileBlobStore` therefore exposes only `WriteAsync` and `ReadAsync`. Erasure is task 063 and will add the delete surface deliberately.
- **No lazy re-index.** Task 061. `ReadAsync` is the seam it will consume.
- **No retention/lifecycle policy, no `contentAvailable` signal.** Task 062 (90-day default for unfiled, indefinite for filed `StoredSession.Ttl == -1`).

## 8a. The binding deployment gate (this is the mechanism, not a promise)

**`SessionFileStore:BlobEndpoint` MUST remain empty in every deployed environment until tasks 062 (retention) and 063 (GDPR erasure) have merged.**

This is not a note-to-self. ADR-015 requires retention and deletion behaviour to be **defined** for any persisted AI artifact, and this task deliberately ships a store with no delete surface (so that task 061's cleanup sweep has no reachable path to durable bytes — FR-B03). The honest consequence is that between 060 and 063 there is a window in which enabling the store would accumulate user document bytes with no defined deletion path. The gate closes that window mechanically rather than by discipline:

- `appsettings.template.json` ships `"BlobEndpoint": ""` → `IsEnabled == false` → nothing is written anywhere.
- The requirement is stated in the template itself (`_DISABLED_BY_DEFAULT`), in the `SessionFileBlobStore` class remarks, and here.

This is the CLAUDE.md §6.5 **path A** exception for ADR-015 15-A: the ADR rule is correct as written, path C (implement delete now) would undo FR-B03's safety property, and "we'll fix it later" is explicitly forbidden — so the deviation is bounded by a gate that makes the non-compliant state unreachable.

## 9. Known weakness noted, not fixed here

`ChatDocumentEndpoints` resolves the tenant as `tid` claim → schema-URI claim → **`X-Tenant-Id` request header** (`:274-276`, `:744-746`, `:833-835`, `:1043-1045`). The header fallback is spoofable unless stripped at the edge, and it is the input to the durable store's partition key exactly as it already is to the Redis key and the AI-Search filter. This is **pre-existing** and affects three stores equally, so it was not changed under a task scoped to adding one. `SummarizeSessionEndpoint.cs:215-216` deliberately omits the header fallback and is the better shape. Worth a follow-up issue.

---

## 10. 🔔 Open items requiring an owner decision (escalated, not silently deferred)

Ordered by severity. Items 1 and 2 are ADR-015 MUST rules; the rest are recommendations.

### 1. ADR-015 15-A — retention and deletion are undefined for a store this task creates

- **Rules**: *"MUST define retention and deletion behavior for stored outputs"*, *"MUST support user-initiated deletion in Tier 3 (GDPR right to erasure)"*, *"MUST NOT exempt Tier 3 from GDPR deletion requirements"*.
- **State**: no delete API (deliberate, FR-B03); no lifecycle delete rule (`storage-account.bicep`'s only `ai-chunks` rule is tier-to-Cool, and the whole policy is gated `false` in `customer.bicep:153`).
- **Proposed path**: **A — project-scoped exception, bounded by the §8a gate.** Owner to confirm the gate and record it in the R8 `spec.md` / `design.md` **ADR Tensions** section, and in the PR description.

### 2. ADR-015 15-B — the Governed Data Stores table omits this store

The 2026-05-17 amendment enumerates governed stores by physical location. Session-file **bytes** are Tier-3-class content now living in a fourth physical store the ADR does not name. **Proposed path: B — amendment, one table row** (I cannot edit `.claude/`):

| Tier | Store | Content Allowed | Retention | Access | GDPR Erasure |
|---|---|---|---|---|---|
| **Tier 3: Work History** | Azure Blob — `{tenantId}/session-files/**` | Original uploaded bytes of chat-session files | 90 days default; indefinite when `StoredSession.Ttl == -1` (task 062) | Owning user + admin | Yes (Art. 17) — task 063 |

Merge alongside 062/063.

### 3. Root CLAUDE.md §10 — no `design.md` Placement Justification section, no `<hot-path-declaration>`

`projects/spaarkeai-compose-r8/design.md` exists on `work/spaarkeai-compose-r8` but not in this worktree (branched from master), and the main session owns it. This note is the content; it needs to be *referenced from* `design.md`, which also needs the §G block. Proposed text for the main session:

```xml
<hot-path-declaration>
  <bff-api>Y</bff-api>              <!-- Services/Ai/Sessions/SessionFileBlobStore.cs, AiPersistenceModule.cs, Api/Ai/ChatDocumentEndpoints.cs -->
  <spaarke-ai>N</spaarke-ai>
  <ci-workflows>N</ci-workflows>
  <skill-directives>N</skill-directives>
  <root-claude-md>N</root-claude-md>
</hot-path-declaration>
```

`AiPersistenceModule.cs` and `ChatDocumentEndpoints.cs` are high-collision files — 13 of 17 active worktrees touch the BFF — so `projects/INDEX.md` should carry the row.

### 4. The `X-Tenant-Id` header fallback (highest-severity thing this work surfaced)

`ChatDocumentEndpoints.cs:301, :816, :906, :1152` resolve tenant as `tid` claim → schema-URI claim → **request header**. Any authenticated principal without a `tid` claim can name its own tenant. That header is now the partition key of a **durable 90-day** store, not just a 4h cache key — the blast radius moved from "poisons a cache for an afternoon" to "places bytes permanently in another tenant's prefix".

- **Not changed here**: removing the fallback touches four handlers on an auth path and is security-sensitive (root §6 requires human sign-off), and it may break server-to-server callers that legitimately depend on it.
- **Done here**: both durable-write sites log a Warning naming the tenant when it came from the header, so the condition is alertable.
- **Recommended**: file a tracked issue and converge on `SummarizeSessionEndpoint.cs:215-216` (claims only, hard 401, stable `auth.tid-missing`). Confirm first that no registered auth scheme yields a `tid`-less principal.

### 5. Same-tenant, cross-user overwrite (W4)

`ChatSessionManager.GetSessionAsync` is tenant-scoped only — there is no per-user session-ownership check on the upload path — and the durable write intentionally overwrites (retry idempotency). So a user in tenant A who knows another user's `(sessionId, fileId)` can overwrite that user's durable copy. Exploitability is low (`fileId` is a 128-bit GUID) and the gap is inherited from the Redis/manifest layers, but durability changes the consequence from "poisons a 4h cache" to "poisons the 90-day record". Recorded so it is not mistaken for closed.

### 6. Orphan blobs have no reaper (S1)

The durable write precedes the manifest write. If the manifest write fails (it is non-fatal by design) the blob is unreferenced — and with no delete API and no lifecycle delete rule, permanently so. **Task 063 must enumerate for erasure BY TENANT PREFIX, not by walking the manifest**, or orphans survive a GDPR delete. Prefix enumeration reaches them; manifest enumeration does not.

### 7. Test-path placement (38-A)

`tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Sessions/SessionFileBlobStoreConfigurationTests.cs` is not one of ADR-038's KEEP paths (the only unit KEEP path is `tests/unit/domain/**`). The two behaviour-defining cases were moved to where they belong — the 500 contract is now a seam test (`Upload_WhenTheDurableWriteFails_Returns500_NotAFalse202`) and the isolation cases live in `tests/integration/tenant/**`. What remains is configuration-guard behaviour (secret rejection, non-https rejection, container-name rejection, enabled/disabled outcome). Flagging for `/test-diet` at project close: these are MAINTAIN-class in substance; if a KEEP home is wanted, the credential-guard theory is a natural fit for `tests/Spaarke.ArchTests/CredentialGuardTests.cs`. Note the same "non-KEEP path" observation applies to essentially the whole existing unit assembly, so this is a repo-wide reconciliation item rather than something peculiar to this task.


---

## Owner resolutions — 2026-08-25

**Container: a DEDICATED `session-files` container is the target; the default stays `ai-chunks` until
bicep declares it.** `ai-chunks` is provisioned in all three stacks with zero consumers, so it works
today — but it is an AI-chunk container by name and purpose, and task 062 gives session files a
retention rule that follows the SESSION TTL (including `-1` filed = indefinite). Mixing two different
lifecycles in one container makes 062 harder than it needs to be. The store never creates a container,
so the switch is: add `session-files` to `storage-account.bicep` (one line, three stacks), then set
`SessionFileStore:ContainerName`. Deliberately NOT done here — it pairs with the role-assignment fix
(§5), which is blocked on the owner confirming which stack the live environments use.

**ADR-015 records: DONE.** The governed-stores table now carries a Tier-3 row for this store, with
retention and erasure marked NOT YET IMPLEMENTED and the mechanical gate (empty `BlobEndpoint`) named
as the reason the non-compliant state is unreachable. Recorded as a §6.5 Path-B amendment.
