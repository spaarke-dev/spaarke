# task-065 deviations note — tenant-isolation audit sweep + remediation

> **Task**: `projects/customer-provisioning-orchestration-r1/tasks/065-tenant-isolation-audit-sweep.poml`
> **Wave**: Wave 4 Batch 4C
> **Author**: main-session (Sonnet 5, effort xhigh per POML `<model-tier>`)
> **Date**: 2026-08-17
> **Rigor**: FULL (TEST-MODIFYING override per root CLAUDE.md §8; tags `bff-api`, `testing`, `integration-test`)

---

## 1. Summary of work delivered

**Deliverable 1 — Audit report**: `projects/customer-provisioning-orchestration-r1/notes/tenant-isolation-audit-2026-08-17.md`. Enumerates all 47 call sites across I1/I2/I3/I4/I5 with per-site verdict.

**Deliverable 2 — 10 code fixes + 1 waiver** across 9 files (see audit report §8 for the table).

**Deliverable 3 — All 5 §4D ArchTests PASS** post-remediation (22 tests incl. neg-controls; §10.2 of the report).

**Deliverable 4 — §10 BFF hygiene report** (§10 of the audit report):
- Build: 0 warnings / 0 errors
- Full test suite: 10,477 BFF unit tests + 524 ControlPlane tests PASS (0 failed)
- ArchTests: 65/65 PASS
- Publish size: 44.96 MB compressed (Δ 0.00 vs task 077 baseline)
- CVE audit: 0 HIGH-severity CVEs

---

## 2. Deviations from POML

### 2.1 No Path-A exceptions issued

Per CLAUDE.md §6.5, if any violation required a documented Path-A exception (rather than fix), it would need to be surfaced explicitly. **NO Path-A exceptions were required.** Every violation had a standard fix (add filter / add PK / add tenantId option). The one intentional cross-partition case (`PromptLibraryService.FindByIdAsync`) was handled by the invariant's own in-attribute waiver mechanism (`[AllowCrossPartitionScan(...)]`) — that is NOT a Path-A exception; it is a documented use of the sanctioned waiver contract defined by task 064.

### 2.2 One violation surfaced OUTSIDE the I5 ArchTest scan scope — not fixed by this task

`ManagedIdentityCredentialFactory.cs:34–40` (in `Infrastructure/Auth/**`, not `Infrastructure/Graph/**`) has the same "no `TenantId` on options bag" gap as GraphClientFactory:132. The I5 ArchTest scope is explicitly `Infrastructure/Graph/**` (per task 064), so this is NOT flagged today.

**Decision**: NOT fixed here. Rationale — the POML task 065 acceptance criteria specifically anchor to "all 5 task-064 ArchTests PASS". Fixing something the ArchTest does not scan would be scope creep (§11 Component Justification cost-of-doing-nothing test). Filed in the audit report as an informational §7.2 follow-on for the owner to decide: (a) broaden the I5 ArchTest scope, or (b) apply the same fix in a targeted PR.

This is NOT a violation of task 065 acceptance — all 5 ArchTests PASS on the current codebase per §10.2.

### 2.3 InvoiceSearchService + RecordMatchService ctor signature changed (added `IConfiguration`)

Both services now take an additional `IConfiguration` ctor param to read `AzureAd:TenantId`. This is a NEW ctor param, not a REMOVED one — DI resolves `IConfiguration` unconditionally as an ambient service. No consumer code or test fixture needs updating. Verified: no test failures across 10,477 BFF unit tests + 524 ControlPlane tests.

Alternative considered: pass tenantId via each existing public method signature (breaking `IInvoiceSearchService.SearchAsync` and `IRecordMatchService.MatchAsync` public contracts, requiring endpoint changes). Rejected — the config-based read is (a) the same pattern already used by `DataverseIndexSyncService.cs:364` on the write side of the same index and (b) preserves single-tenant BFF behavior today (BFF is bound to one Azure AD tenant per §9.1 of spec v3.3). Once the BFF becomes multi-tenant, both services will need to migrate to per-request tenantId derivation via `IHttpContextAccessor` (the pattern established in `RecordSearchService.cs:190`); that is a design change out of scope for a fix-only audit-remediation task.

### 2.4 SemanticSearchService fix is a "scanner-visibility" comment, not a filter change

The file was flagged as a violation but the actual production behavior WAS compliant — `SearchFilterBuilder.BuildFilter(tenantId, ...)` (in a different file) authors `tenantId eq '{tenantId}'` unconditionally (line 71 of that file). The file-level I2 scanner cannot see through the helper.

**Fix chosen**: option (b) from the ArchTest failure message — add an inline comment mentioning `tenantId eq` (referencing the helper). This is preferred over option (c) (add to `ExcludedFileRelPaths` in the test) because the comment is INSIDE the offending file where the developer will see it (reviewer visibility), while the exclusion list is in the test file (only visible when the test fires).

### 2.5 FeedbackService I3 fix preserves SQL-level tenant WHERE clause as defense in depth

The fix adds `QueryRequestOptions { PartitionKey = new PartitionKey(tenantId) }` (the primary tenant scoping — turns cross-partition scan into single-partition query on `/tenantId` PK). The pre-existing `WHERE c.tenantId = @tenantId` SQL clause is DELIBERATELY KEPT (not removed) — this is belt-and-suspenders per the AI Search filter pattern (§4D I2 shape). Removing the SQL clause would have been "cleanup", but keeping it maintains defense-in-depth if the container's partition key strategy is ever revisited.

---

## 3. Test-diet classification (per ADR-038 §7)

**No new tests were authored by task 065.** All 5 §4D ArchTests were authored by task 064 (MAINTAIN class under `tests/Spaarke.ArchTests/TenantIsolation/`); this task fixes source under `src/server/**` so the pre-existing tests pass.

The `AllowCrossPartitionScanAttribute` in Spaarke.Core (also authored by task 064) received its first production consumer via this task (PromptLibraryService.FindByIdAsync annotation) — the attribute's Reason arg validation (throws if empty) is exercised by the annotation itself; no separate test needed.

---

## 4. Coordination notes

- `.claude/**` untouched (sub-agent write boundary respected).
- L2 (`Sprk.Provisioning.ControlPlane`) untouched (task 058 is safe).
- `tests/Spaarke.ArchTests/TenantIsolation/I1_NoHardcodedTenantTests.cs` untouched (task 066 owns).
- Existing `[Obsolete]` markers in `DemoProvisioningOptions` untouched (task 082 owns).
- No CI workflow files modified (task 088 coord PR territory).
- Publish delta 0.00 MB — no NFR-01 escalation.

---

## 5. Follow-ons

- **Broaden I5 ArchTest scope** to include `Infrastructure/Auth/**` — captures the `ManagedIdentityCredentialFactory` gap surfaced in §7.2 of the audit report. Owner decision: broaden ArchTest OR fix the factory in a targeted PR.
- **BFF multi-tenant migration** — when the BFF transitions from single-tenant to multi-tenant, `InvoiceSearchService` and `RecordMatchService` need to migrate from `AzureAd:TenantId` config-read to per-request `IHttpContextAccessor` tenantId (pattern established in `RecordSearchService.cs:190`). Filed in audit report §11.
