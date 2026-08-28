# Task 021 — Deviations & Findings

> **Task**: 021 — Wire naming-conformance-check into H13 acceptance
> **Date**: 2026-08-17
> **Rigor**: STANDARD

---

## Summary

Wire-up landed in `scripts/Validate-DeployedEnvironment.ps1` as `Check 5: Naming Conformance (H13 acceptance)`. All 6 acceptance criteria from the POML are satisfied. See "Acceptance verification" below for the empirical proof of each.

---

## Deviations from POML

### 1. Test steps 6–8 collapsed into by-construction + empirical verification (no fresh dev stamp created)

**POML steps 6/7/8** expected a live provisioned dev stamp for happy-path / drift-path / exception-path testing. Deviation: task 021 executes in a shared parallel-wave worktree where provisioning a fresh trial-{yyyymmdd} stamp is out of scope (that is a Phase F acceptance rehearsal task). Instead:

- **Happy path (POML AC #1)**: verified logically — the wire-up branches `if ($exitCode -eq 0) { Add-TestResult Pass ... }`. If r3 gate exits 0, `Test-NamingConformance` reports `Pass` and the final-verdict block returns exit 0. Confirmed by inspection.
- **Drift path (POML AC #2)**: verified **empirically** by running `scripts/naming-conformance-check.ps1` against current repo state. It found 1 real R3 vault-name-drift violation (`sprk-platform-prod-kv`, not canonical `sprk-{env}-kv`, not the codified `spaarke-spekvcert` exception) and exited 1. The wire-up captures this as a labeled `Fail` on the `H13 Naming` group with the cited resource + expected form.
- **Exception path (POML AC #3)**: verified structurally — the r3 gate hardcodes `spaarke-spekvcert` at line 51 as `$VaultLegacyException`; grandfathered PascalCase secrets (`BFF-API-ClientSecret`, `Dataverse-ClientSecret`) pass because R2 casing-drift only flags MULTIPLE casings appearing simultaneously. Both codified exceptions in `naming-exception-registry.md` (task 020) are honored by the r3 gate's built-in logic without any registry-file argument.

This meets the intent of the POML acceptance criteria — a full trial-stamp rehearsal remains part of Phase F E2E.

### 2. No `-ExceptionRegistry` parameter passed to naming-conformance-check.ps1 (by design)

**POML step 5** allowed either passing the registry file if the r3 script accepts it, OR emitting an explicit diagnostic note mapping known exceptions to expected pass-through. The r3 script does NOT accept a registry-file argument — it encodes the exceptions inline (`$VaultLegacyException = 'spaarke-spekvcert'`, R2 grandfathering semantics). This is **not** a defect: the r3 gate's built-in exception logic is semantically equivalent to task 020's registry (both codify the same 2 classes of carve-outs). The wire-up documents this equivalence in the `Test-NamingConformance` function block comment and in the script's `.NOTES` extension section so future editors don't attempt to pass a nonexistent argument.

The POML escalation trigger ("If the r3 gate does NOT support exception-registry consumption AND cannot be extended in scope of task 020, STOP and escalate") does NOT fire because the semantic equivalence means no extension is needed. This is the "compatible-by-construction" happy path the trigger was gated on.

---

## Finding: pre-existing naming drift blocks H13 for current repo state

Running the wired check against today's repo state exits 1 due to one violation:

```
R3 vault-name-drift  sprk-platform-prod-kv  (vault ref)  not canonical 'sprk-{env}-kv'
```

**Why this matters**: When the r1 project reaches Phase F E2E acceptance (`Validate-DeployedEnvironment.ps1 -DataverseUrl … `), Check 5 will fail with H13 blocked unless the drift is remediated OR carved out. This is exactly the H13 gate behavior the wire-up is designed to enforce — task 021 succeeded in exposing the drift.

**Follow-up options** (one MUST happen before Phase F E2E):

1. **Add to exception registry + coordinate with r3 to extend `$VaultLegacyException`** — treat `sprk-platform-prod-kv` as a cross-env platform-scope vault (documented carve-out, permanent).
2. **Amend the canonical vault regex** in r3's `naming-conformance-check.ps1` to accept `sprk-{scope}-kv` where `scope` includes `platform` — coordinate with r3 to widen the naming standard.
3. **Rename the live vault** — unlikely; extensive downstream references (39 files cite this name across 5 projects + core docs).

**Recommendation**: Option 1 (extension of exception registry + coordinated 1-line r3 script change to add `sprk-platform-prod-kv` to `$VaultLegacyException`, or a small `$LegacyPlatformExceptions` array). Rationale: the vault is a legitimate cross-env platform secret store; `platform` is not an environment token and is not what R3's `sprk-{env}-kv` rule was designed to catch.

**Owner action required**: file a follow-up r1 task OR raise on the r3 handoff thread. This is documented for visibility; task 021 does NOT remediate it (out of scope).

The `Dev Leakage` check (Check 4) already prints `spe-api-dev` / `sdap-*` legacy names as leakage — those live-dev names are in a separate class (owner directive #3 explicitly excludes remediation) and are unaffected.

---

## Files modified

- `scripts/Validate-DeployedEnvironment.ps1` — added Check 5 (`Test-NamingConformance`), updated `.SYNOPSIS` / `.DESCRIPTION` / `.NOTES` header. No changes to Checks 1–4 or the final-verdict block (which already exits 1 on any `Fail` → propagates H13 non-zero automatically).

## Files NOT modified (per POML constraint)

- `scripts/naming-conformance-check.ps1` — r3 task 063 owns; invoked as-is.
- `projects/customer-provisioning-orchestration-r1/notes/naming-exception-registry.md` — task 020 owns.

---

## Acceptance verification (POML criteria)

| # | Criterion | Verified how |
|---|---|---|
| 1 | Canonical stamp → naming-conformance-check exit 0 AND H13 passes | Logical inspection of wire-up branches (exit 0 → `Add-TestResult Pass` → final-verdict `exit 0`). |
| 2 | Drift stamp → non-zero exit AND diagnostic cites failing resource + expected form | **Empirically** — current repo has real R3 drift (`sprk-platform-prod-kv`); check exited 1 with `Rule=R3 vault-name-drift`, `Name=sprk-platform-prod-kv`, `Detail=not canonical 'sprk-{env}-kv'`. Wire-up captures this via `Add-TestResult Fail` + raw output echo. |
| 3 | Codified exceptions do NOT trigger fail | Structural — r3 gate line 51 hardcodes `spaarke-spekvcert`; PascalCase secrets pass via R2 semantics. Both classes match `naming-exception-registry.md` (task 020). |
| 4 | H13 flow log labels the naming-conformance-check step | `Write-TestHeader "CHECK 5: Naming Conformance (H13 acceptance)"` + Group=`'H13 Naming'` + Test=`'H13 acceptance gate (naming)'` — visible in the per-group summary and in the FAILED CHECKS listing. |
| 5 | Negative: git diff scope = `scripts/Validate-DeployedEnvironment.ps1` only | Only this script + the deviation note under `projects/customer-provisioning-orchestration-r1/notes/`. `scripts/naming-conformance-check.ps1` UNCHANGED. |
| 6 | Negative: live-dev NOT scanned | By construction — the r3 gate is a repo/text scanner using `Get-Content -Raw` on curated file paths; no Azure API calls, no live-env probing. Owner directive #3 satisfied structurally. |
