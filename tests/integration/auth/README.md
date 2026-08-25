# `tests/integration/auth/**` — security-auth KEEP category

> **Category authority**: [ADR-038](../../../docs/adr/ADR-038-testing-strategy.md) — Testing Strategy
> **Constraint loader**: [`.claude/constraints/testing.md`](../../../.claude/constraints/testing.md)
> **Standard**: [`docs/standards/TEST-ARCHITECTURE.md`](../../../docs/standards/TEST-ARCHITECTURE.md)

## What lives here

Integration tests covering **authentication, authorization, OBO exchange, claims handling, token validation**. This is one of the 6 KEEP-protected path categories.

## Deletion-safety rule

Removing a file under this path requires a **same-PR replacement** covering the same scenario. Enforced at code-review (`task-execute` Step 9.5) by path inspection — see ADR-038 §2.

## Authoring template

See [`tests/CLAUDE.md`](../../../tests/CLAUDE.md) integration-first AAA template.

## Inventory status (2026-06-26)

Per `notes/test-inventory-summary.md`: **25 KEEP-security-auth files** identified in the pre-reorg inventory. Bulk move pending (see `notes/path-reorganization-design.md` for csproj strategy decision).

## ⚠️ This directory is EMPTY and NOT COMPILED — read before adding a file here (2026-08-20)

The bulk move above never happened. This directory contains only this README, and — more importantly —
**it is not included in any test project**. `tests/unit/Sprk.Bff.Api.Tests/Sprk.Bff.Api.Tests.csproj`
globs `contract/`, `regression/`, `seam/` and `tenant/`, but **not `auth/`**. A test authored here today
would compile nowhere and run never, silently.

**Where auth/OBO tests actually live**: [`tests/integration/seam/Auth/`](../seam/Auth/) — currently
`CredentialSelectionSeamTests.cs` and `ConfidentialClientSharingSeamTests.cs`
(`spaarke-auth-v4-dataverse-MI` tasks 010 / 011). `seam/**` is the only KEEP path that is both
deletion-protected *and* compiled, so it is the correct home until this directory is wired up.

**To fix properly**, either add `auth/**` to the csproj `<Compile Include>` set and move the files, or
retire this directory and fold security-auth into `seam/`. Recorded rather than fixed here because it is
a test-architecture decision owned by ADR-038, not by an auth project. Surfaced by `adr-check` finding
**W5** at task 011's quality gate.
