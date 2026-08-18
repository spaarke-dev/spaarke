# task-064 deviations note — 5 tenant-isolation ArchTests (I1–I5)

> **Task**: `projects/customer-provisioning-orchestration-r1/tasks/064-author-tenant-isolation-archtests.poml`
> **Wave**: Wave 4 Batch 4B
> **Author**: main-session (Sonnet 5, effort xhigh per POML `<model-tier>`)
> **Date**: 2026-08-17
> **Rigor**: FULL (test-modifying override per root CLAUDE.md §8)

---

## 1. Deliverables (as authored)

**New attribute definition** (Spaarke.Core — least-blast-radius shared home; both BFF and eventually L2 reconciler consume it):

- `src/server/shared/Spaarke.Core/Attributes/AllowCrossPartitionScanAttribute.cs` — waiver marker for I3, requires non-empty `Reason` constructor arg; matched BY NAME (regex) by the I3 ArchTest so consumers may reference the canonical definition or declare a same-named local attribute without coupling to the shared project.

**5 new ArchTest files** under `tests/Spaarke.ArchTests/TenantIsolation/`:

| # | File | Predicate shape | Neg-controls |
|---|---|---|---|
| I1 | `I1_NoHardcodedTenantTests.cs` | File+regex scan of `scripts/**/*.ps1` for `[string]$*Tenant* = 'GUID'` defaults inside `Param()` blocks (excludes function-body variable initializers per POML constraint) | 2 (flags known-bad shape; permits Mandatory / empty / non-tenant name) |
| I2 | `I2_AiSearchTenantIdFilterTests.cs` | File-level scan of `src/server/**/*.cs`: every file that invokes `SearchClient.SearchAsync<T>(...)` must also contain the substring `tenantId eq ` somewhere in the same file (or be in a documented exclusion list) | 2 (pattern flags generic call, skips facade calls; substring matches compliant shape) |
| I3 | `I3_CosmosPartitionKeyTests.cs` | File-level scan for `Container.{Read/Create/Upsert/Replace/Delete/Patch/ReadMany}ItemAsync`, `GetItemQueryIterator`, `GetItemLinqQueryable`; each call site must have `new PartitionKey(...)` / `partitionKey:` / an identifier `*PartitionKey` in its args OR a hoisted `PartitionKey` construction / `QueryRequestOptions { PartitionKey = ... }` initializer above the call in the same method OR the enclosing method / class must be annotated `[AllowCrossPartitionScan("reason")]`. Receiver-shape filter (`\w*[Cc]ontainer\w*` or `GetContainer()`) rejects facade calls (`store.DeleteItemAsync` etc.) | 6 (predicate flags missing, passes hoisted local, passes QueryRequestOptions initializer, rejects `PartitionKey.None`, recognizes waiver by name, receiver-shape filter distinguishes Container from facade) |
| I4 | `I4_SpeContainerIdLiteralTests.cs` | Regex scan for the string literal shape `"b![A-Za-z0-9_-]{20,}"` in `src/server/api/Sprk.Bff.Api/Services/**/*.cs`. Truncates any offender in the failure message so a real container-ID never appears fully in CI logs | 2 (flags a realistic sample; ignores short prefixes, comments, English mentions) |
| I5 | `I5_GraphPerTenantTokenTests.cs` | File scan of `src/server/api/Sprk.Bff.Api/Infrastructure/Graph/**/*.cs` for credential construction: `new ClientSecretCredential(...)` first positional arg must be non-empty / non-null / not `"common"`/`"organizations"`/`"consumers"`; every `new DefaultAzureCredential(...)` / `new ManagedIdentityCredential(...)` in a file must be paired with a `.TenantId = ...` assignment somewhere in that file; every `.WithAuthority(...)` argument must NOT bind to `/common`/`/organizations`/`/consumers` | 4 (ClientSecretCredential first-arg predicate, WithAuthority multi-tenant predicate) |

**Suite runtime**: <1 s for the 5 TenantIsolation tests; full ArchTests suite (60 pre-existing + 4 pre-existing negative-controls-of-I3-related + 21 new) runs in 12 s (well under the POML `<10s` guidance for the tenant-isolation subset).

**Build**: `dotnet build tests/Spaarke.ArchTests/Spaarke.ArchTests.csproj` — 0 warnings / 0 errors.

**Analyzer note**: initial pass triggered xUnit's xUnit2008 (`Assert.True(regex.IsMatch(...))` → `Assert.Matches`); fixed in the negative-control tests before the final build.

---

## 2. Baseline violations (filed to task 065 audit sweep)

Per the POML step 7 acceptance criterion — "All 5 tests PASS against the current codebase (post-1834b77bc script fix). **Any pre-existing violation → filed against task 065 audit sweep.**" — the following baseline violations were found by the new ArchTests on the current codebase (HEAD `9e936e911`, master merged in). Per the POML's constraint (§9 "STOP and escalate per CLAUDE.md §6 rather than adding a broad exclusion list that would weaken the invariant"), NONE of the tests were weakened to make baseline pass. Every finding below is a genuine tenant-isolation invariant deviation that task 065 either fixes or documents as a Path-A exception (per root CLAUDE.md §6.5).

### I1 — hardcoded tenant defaults in PowerShell scripts (3 violations)

Commit `1834b77bc` fixed only `scripts/Register-EntraAppRegistrations.ps1:63`. Three sibling scripts have the same shape:

| File:line | Parameter |
|---|---|
| `scripts/Register-BffMiWithContainerType.ps1:25` | `[string]$TenantId = 'a221a95e-6abc-4434-aecc-e48338a1b2f2'` |
| `scripts/Setup-EntraInfrastructure.ps1:60` | `[string]$TenantId = 'a221a95e-6abc-4434-aecc-e48338a1b2f2'` |
| `scripts/Test-EntraAppRegistrations.ps1:50` | `[string]$TenantId = 'a221a95e-6abc-4434-aecc-e48338a1b2f2'` |

**Fix** (task 065 candidate): remove the default; annotate with `[Parameter(Mandatory=$true)]` (same pattern as the `1834b77bc` fix on the sibling script). One-line changes each.

### I2 — SearchClient callers with no `tenantId eq` filter in the file (4 violations)

Files that call `SearchClient.SearchAsync<T>(...)` directly but do not contain a `tenantId eq ` OData filter string anywhere in the file:

| File:line | Note |
|---|---|
| `src/server/api/Sprk.Bff.Api/Services/Ai/SemanticSearch/SemanticSearchService.cs:172` | Requires audit — file may build the filter via a helper method the file-level scanner doesn't see. |
| `src/server/api/Sprk.Bff.Api/Services/Finance/InvoiceSearchService.cs:124` | Requires audit — invoice-search filter shape TBD. |
| `src/server/api/Sprk.Bff.Api/Services/RecordMatching/DataverseIndexSyncService.cs:287` | Index-sync service — may be indexing writes rather than tenant queries; verify. |
| `src/server/api/Sprk.Bff.Api/Services/RecordMatching/RecordMatchService.cs:96` | Requires audit — record-match filter shape TBD. |

**Fix** (task 065): audit each. For genuinely compliant filter-via-helper cases, either inline a `tenantId eq ` mention in a comment for scanner visibility OR add to the ExcludedFileRelPaths list with a documented reason. For real violations, add the filter to the SearchOptions.Filter construction.

**Fixed during task 064 authoring** (not a task 065 candidate): `src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/RecallSessionFileHandler.cs:30` was flagged initially due to a false positive — the regex `\.SearchAsync\s*<` matched `.SearchAsync</c>` inside an XML doc-comment `<c>RagService.SearchAsync</c>` element. Predicate tightened to `\.SearchAsync\s*<[A-Za-z_]` (requires a letter/underscore after the angle bracket, distinguishing generic type-parameter open-brace from an XML tag close). Negative-control test added for the corrected shape.

### I3 — Cross-partition Cosmos queries without waiver (3 violations)

Real cross-partition query call sites — annotated in source as intentional cross-partition:

| File:line | Method / doc-comment note |
|---|---|
| `src/server/api/Sprk.Bff.Api/Services/Ai/Feedback/FeedbackService.cs:263` | `container.GetItemQueryIterator<int>(query)` inside `QueryAggregateAsync` — COUNT query over feedback partitions; SQL WHERE clause DOES filter by `tenantId` but the SDK still does cross-partition fan-out since no `QueryRequestOptions.PartitionKey` is set. |
| `src/server/api/Sprk.Bff.Api/Services/Ai/Feedback/FeedbackService.cs:314` | Same as above — negative-comments TOP-N query. |
| `src/server/api/Sprk.Bff.Api/Services/Ai/PromptLibrary/PromptLibraryService.cs:325` | `container.GetItemQueryIterator<CosmosPromptDocument>(...)` inside `FindByIdAsync` — doc-comment on line 309 explicitly calls this out: "Cross-partition query to locate a template by id + tenantId. Used when the ownerId (partition key) is unknown." |

**Fix** (task 065): three options per site:
1. Refactor to scope the query per-tenant using `QueryRequestOptions { PartitionKey = new PartitionKey(tenantId) }` — preferred where the caller does have the tenant identity in hand (all 3 sites here do).
2. If the tenant-partition strategy needs revisiting (e.g., feedback partitioned by tenantId + entityKey rather than tenantId alone), spec + adr amendment.
3. Annotate the method with `[AllowCrossPartitionScan("<reason citing design section>")]` — only if a legitimate fleet-wide scan is required (not the case for any of these three sites).

### I5 — Graph credential without explicit tenant option (1 violation)

`src/server/api/Sprk.Bff.Api/Infrastructure/Graph/GraphClientFactory.cs:132` — `new DefaultAzureCredential(credentialOptions)` where `credentialOptions.TenantId` is NOT set (only `ManagedIdentityClientId` is set, when configured for a UAMI). Currently the credential resolves to the App Service's MI-host tenant (= Spaarke tenant), which is intended today (BFF is single-tenant). But per §4D I5's forcing-function intent, the credential MUST bind explicitly so a future multi-tenant switch is safe.

**Fix** (task 065): set `credentialOptions.TenantId = _tenantId` before constructing the credential (the field already reads from configuration on line 53 — `_tenantId = configuration["AZURE_TENANT_ID"] ?? configuration["TENANT_ID"]`). One-line change; no behavior change today (the MI credential resolves to the same tenant either way), but future-proofs the surface.

### I4 — PASS on baseline ✓

No SPE container-ID literal (`b!...`) found anywhere under `src/server/api/Sprk.Bff.Api/Services/**`.

---

## 3. Coordination notes

- **Attribute placement**: `AllowCrossPartitionScanAttribute` in `Spaarke.Core.Attributes` — no reference cycle, no coordination collision with tasks 057 / 052 / 077 (they don't touch Spaarke.Core; L2 will add a ProjectReference on Spaarke.Core when task 060 (reconciler crash-recovery scan) needs to use the attribute).
- **No touch on** `.claude/**` (sub-agent write-boundary respected — the ArchTests themselves live in `tests/**`, not `.claude/`).
- **No touch on** L2 (Sprk.Provisioning.ControlPlane) source — task 057 is safe.
- **No touch on** BFF service source — tasks 052, 077 are safe.
- **No touch on** CI workflow files — task 088 (CI-wiring coord PR) owns that surface per r3 handoff §7.

---

## 4. Test-diet classification (per ADR-038 §7)

All 5 new tests are **MAINTAIN class** under a KEEP path:

- Category: KEEP path variant — the ArchTests directory is a canonical KEEP path per ADR-038 §7 (forcing-function structural invariants; the whole point is regression protection at PR time).
- What breaks if deleted: cross-tenant data bleed regressions ship silently (CATASTROPHIC per §4D — legal-privilege leak, PII disclosure).
- Cannot be re-implemented as unit test — they inherently scan production source shape.

None are scaffolding class; none should be deleted at project-close `/test-diet`.

---

## 5. Follow-ons

- **Task 065 (Wave 4C)** — audit sweep + fixes for the 12 baseline violations listed in §2 above.
- **Task 067 (Wave 4C)** — nightly Graph app-role parity ArchTest (behind CI-wiring coord PR).
- **Task 088 (Wave H)** — coordinated PR with `ci-cd-unit-test-remediation-r1` to wire the 5 new ArchTests into the PR gate.
