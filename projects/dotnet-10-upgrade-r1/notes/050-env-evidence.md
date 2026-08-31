# Task 050 — `spaarke-bff-dev` net10 readiness evidence

> **Date**: 2026-08-13 · **Read-only `az` evidence** (no mutation). Resolves the two execution-time unresolved questions (region net10 availability + staging slot).

## Evidence (read-only `az`)

| Fact | Value |
|---|---|
| App Service | `spaarke-bff-dev` / `rg-spaarke-dev` |
| Kind / OS | `app,linux` |
| Region | **West US 2** |
| Plan / SKU | `spaarke-dev-plan` / **P1v3 (PremiumV3)**, capacity 1 |
| State | Running, `alwaysOn=true`, HTTP/2 on |
| **Current runtime** | **`DOTNETCORE|8.0`** (pre-cutover, as expected) |
| **Staging slot** | **NONE** (`az webapp deployment slot list` → `[]`) |
| net10 runtime availability | ✅ **`DOTNETCORE:10.0` IS offered** on Linux in this subscription/region (`az webapp list-runtimes --os-type linux`) |
| Slot capability | ✅ P1v3 supports deployment slots (up to 20) — a `staging` slot **can be created** for a zero-downtime, isolation-safe cutover |

## Resolved unresolved questions

1. **Region net10 support** → YES, `DOTNETCORE:10.0` available in West US 2. No region gate.
2. **Staging slot** → none exists today, but the P1v3 plan supports creating one. → **slot-based cutover (runbook Section B mechanics) is available for dev**, which is the safest path given the shared-App-Service coordination problem (see `notes/051-coordination.md`).

## Not yet confirmable (completes with 051)

- "Confirm spaarke-dev **runs** net10" — requires the actual net10 deploy (051). This note confirms the environment is *ready* to run net10; the runtime flip + smoke is 051 (operator go/no-go).
