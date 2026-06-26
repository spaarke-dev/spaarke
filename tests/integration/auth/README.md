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
