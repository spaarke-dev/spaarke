# `tests/integration/contract/**` — endpoint-contract KEEP category

> **Category authority**: [ADR-038](../../../docs/adr/ADR-038-testing-strategy.md)
> **Authoring rule**: "Every new endpoint = ≥1 integration test" (per `tests/CLAUDE.md` MUST rules)

## What lives here

Integration tests covering **endpoint HTTP contract**: route + status + ProblemDetails shape + payload shape. Anchors the stability of public API surface.

## Deletion-safety rule

KEEP-protected. Deletion requires same-PR replacement covering the same endpoint contract.

## Inventory status (2026-06-26)

117 KEEP-endpoint-contract files identified — the second-largest KEEP category after domain-logic. Bulk move pending.
