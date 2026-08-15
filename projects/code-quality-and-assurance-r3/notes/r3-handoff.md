# Handoff to `code-quality-and-assurance-r3`

> **From**: `dotnet-10-upgrade-r1` (FR-17 / task 090) · **Date**: 2026-08-14 · **Load this FIRST when planning r3.**
> **One line**: the backend is now **.NET 10 LTS**, master + dev are net10, the suite is green, the graph is zero-CVE. r3 re-plans on THIS baseline (design §11). Migrate the r3 worktree first: `/worktree-net10-migrate` (or `pwsh -File scripts/Update-WorktreeToNet10.ps1`).

---

## The three baseline assumptions r3 MUST honor (design §11)

### 1. Do NOT re-pin the superseded CVE packages
The net10 retarget REMOVED these direct pins because the **net10 shared framework now supplies them** (non-vulnerable). Re-adding them is a regression:
- `System.Text.Json`, `System.Formats.Asn1`, `System.Text.RegularExpressions` — framework-provided on net10 (NU1510). Gone from Core/Dataverse/BFF.
- `System.Security.Cryptography.Xml` — framework-provided on the net10 **web** app (BFF); the 3 **shared libs** (non-web) pin it + `Pkcs` at **`10.0.11`** deliberately (they don't get the web shared framework). Keep those two pins.
- `NoWarn=NU1903` was **deleted** from `Directory.Build.props` (it was masking the S.S.C.Xml HIGH, now genuinely fixed). Do not re-add it.
- Evidence: `notes/pin-removals.md`, `notes/cve-audit.md`. `dotnet list --vulnerable --include-transitive` = **zero** across the graph — keep it that way.

### 2. H1 / H2 / H3 hit-sites are already fixed — build ON them, don't redo
- **H1** — `BackgroundService.ExecuteAsync` now runs fully on a background thread (net10). ~10 workers audited + fixed; `TodoGenerationService` startup ordering handled. (`notes/h1-*`, adversarially verified.)
- **H2** — dev-boot DI validation: 45 captive-dependency errors → 10 root singletons → all fixed (scope-per-unit-of-work / demote-to-scoped). `ValidateOnBuild`/`ValidateScopes` are **on** (not disabled). A permanent guard test **`DiGraphValidationTests`** asserts a clean DI graph — **do NOT delete or weaken it**, and do not disable the validations. (`notes/h2-di-validation.md` + `h2-verification.md`.)
- **H3** — `X509Certificate2` ctor → `X509CertificateLoader.LoadPkcs12` (SYSLIB0057). Keep.

### 3. Publish-size + governance baselines MOVED
- Publish baseline: **44.96 MB incl. PDBs** (44.05 excl.), down from 49.63 (net8). Root `CLAUDE.md` §10 + `.claude/constraints/azure-deployment.md` already reflect this. Don't restore the old number.
- `global.json` = `10.0.100` (SDK 10.0.101 resolves); CI `setup-dotnet@v6` 10.x across 7 workflows; App Service `DOTNETCORE|10.0`; Functions `dotnet-isolated 10.0`.

---

## Graph v6 / Kiota 2.0 is DONE here — do NOT re-defer or assume Graph 5.x
- Task 033 moved `Microsoft.Graph` 5.105 → **6.5.0** (pulls Graph.Core 4.0.1 + transitive **Kiota 2.0.0**); the **7 direct Kiota pins deleted**.
- The **Kiota HIGH CVE (GHSA-7j59-v9qr-6fq9) is CLOSED** — do NOT re-accept it as risk; it was **dropped from the CI accepted-risk allow-list** (now empty). Graph v6 came **off** the deferred-majors list.
- **API impact r3 will hit**: Kiota now throws **`ODataError`** (not `ServiceException` — though Graph.Core 4.x retains `ServiceException`). `ODataError` exposes `ResponseStatusCode` (int) + dict-shaped `ResponseHeaders` (no typed `RetryAfter.Delta`). The `DriveItemOperations` catch blocks were migrated `ServiceException`→`ODataError`. Reference: **`notes/graph6-kiota2-break-assessment.md`** (the exact break patterns) + `notes/kiota-cve-finding.md`.

---

## What r3 OWNS from net10 (its backlog)

- **Deferred package majors** → **GitHub issue #772** + `notes/deferred-package-upgrades.md`: `Azure.Search.Documents` v12, `PowerBI.Api` v5, `Azure.AI.Projects` v2 GA, `Microsoft.Agents.AI` GA, `Http.Polly`→`Http.Resilience`, `JsonSchema.Net` v9, and the **coupled** `Microsoft.Extensions.AI`/`OpenAI` 10.3→10.9 (drags OpenAI ≥2.12 — coordinate). Each needs its own spec + test; do NOT batch-apply. (AppInsights 3.x is N/A — FR-06 removed the classic SDK.)
- **HELD — licensing, do NOT bump without sign-off**: `FluentAssertions` v8 (paid Xceed license — hold at 6.x), `QuestPDF` 2026.x (revenue-gated).
- **Already done — do NOT redo**: the Tier-1 same-major patch pass (Extensions→10.0.11, Azure SDK ServiceBus/Blobs/KeyVault, OTel Instrumentation 1.17.0, Mvc.Testing 10.0.11). Bicep CLI is on 0.46.1.

## Deferred production/demo cutover (tracked follow-on)
Only `spaarke-dev` is live; demo/prod were decommissioned for budget. The net10 **production/demo slot-swap cutover** (tasks 060/061, FR-16/NFR-04/NFR-06) is **deferred** until those environments are re-provisioned on net10. Procedure preserved in `notes/slot-swap-runbook.md` §B. Filed as a follow-on issue (see #772 sibling). Not r3's job unless it re-provisions those envs.

## Test-suite state r3 inherits
- Green on net10: **BFF 10,415/0/101**, ArchTests 28, Core 45, Scheduling 47, RecordSync 12.
- **ArchTests ADR-010 1:1 interface ceiling = 153** (re-armed from 76; legit seams). ADR-010 `IsRecordType` now detects record **struct** (via `PrintMembers`).
- `ExternalParticipationService` cache tests reference the **single-source-of-truth** const (`CacheVersion = ExternalParticipationService.CacheVersion`) — don't re-hardcode.
- `MessageAttachmentMaterializer` test asserts `sprk_relatedcommunication` (production renamed the sprk_document lookup) — not `sprk_communication`.
- **/test-diet**: see `notes/test-diet-report.md` — this project mostly MODIFIED tests (retarget/realign); the one net-new test (`DiGraphValidationTests`) is MAINTAIN-class (KEEP).

## Migration tooling (new — for r3's worktree + any other)
`/worktree-net10-migrate` skill + `scripts/Update-WorktreeToNet10.ps1` (non-destructive: SDK check → merge master → **net8-clobber guard** → build). **Close VS/Rider before merging** — an open solution can revert csproj to net8 → 503 on deploy (observed live 2026-08-14).

---

## Where to read the full reasoning
`projects/dotnet-10-upgrade-r1/`: `spec.md` (17 FR / 8 NFR), `design.md` (§5 hit-sites, §6 packages, §7 deploy, §11 r3 relationship), and `notes/` (pin-removals, graph6-kiota2-break-assessment, cve-audit, publish-size-rebaseline, test-green, h1/h2 verification, cutover-and-worktree-migration, deferred-package-upgrades, 050/051 env+smoke).
