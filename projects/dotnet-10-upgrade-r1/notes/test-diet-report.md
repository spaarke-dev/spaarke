# /test-diet Report — dotnet-10-upgrade-r1 (project close, FR-17)

> **Date**: 2026-08-14 · Reconciles tests added/modified during the migration against the ADR-038 build-vs-maintain classifier (17-ban). Read-only: this report emits the reviewer verdict; no MAINTAIN-class test is deleted and no SCAFFOLDING-class test is kept without the call below.

## Nature of this project's test changes

A support-lifecycle **retarget** — the vast majority of test changes are **modifications to existing MAINTAIN-class tests** (fix stale assertions, realign to renamed production, bump test-host packages), not new scaffolding. Build-vs-maintain classification:

| Test change | Type | Class | Verdict |
|---|---|---|---|
| `DiGraphValidationTests` (H2, task 020) | **NEW** | **MAINTAIN** — network-free guard asserting a clean DI graph (`ValidateOnBuild`/`ValidateScopes`); protects against captive-dependency regressions | **KEEP** (do NOT delete/weaken — noted in r3 handoff) |
| `DesktopUrlBuilderTests` ×10 (task 030) | modified | MAINTAIN | KEEP — realigned to production's abbreviated `ms-word:` format (test was stale) |
| `ExternalParticipation*` / `StandingGrant*` CacheVersion (030) | modified | MAINTAIN | KEEP — now reference the single-source-of-truth const (master SSOT refactor superseded the hardcode) |
| `MessageAttachmentMaterializerTests` line 106 (Part-A re-sync) | modified | MAINTAIN | KEEP — realigned to renamed `sprk_relatedcommunication`; was red on master too |
| `ADR010_DITests` (ceiling 76→153, record-struct detection) | modified | MAINTAIN | KEEP — arch ratchet re-armed + detection gap fixed |
| `AttachmentActionEvalTests` `_createTaskAi`→`createTaskAi` (030) | modified | MAINTAIN | KEEP — H2 field→scoped-local follow-through |
| `*.csproj` test-host bumps (Mvc.Testing 8→10.0.11; Extensions align) | modified | infra | KEEP — required for net10 test host (PipeWriter) |
| `tests/unit/Spaarke.Plugins.Tests/**` | **DELETED** (030) | dead orphan | already removed — referenced a deleted project, in no sln |

## Verdict

- **0 SCAFFOLDING-class tests were added** by this project → **nothing to `git rm`**.
- **1 net-new test** (`DiGraphValidationTests`) is **MAINTAIN-class** → KEEP (it lives with the BFF unit tests as a permanent DI guard).
- **1 deletion already done** (`Spaarke.Plugins.Tests` dead orphan) — correct, no replacement needed (it tested a deleted project).
- **No AMBIGUOUS tests** requiring escalation.

No reviewer action required — the diet is clean because the project realigned existing coverage rather than scaffolding new tests. Suite remains green on net10 (BFF 10,415 / 0).
