# Pre-release Package Tracker (Task 012)

> **Source**: `src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj`
> **Captured**: 2026-05-24
> **Status**: 3 pre-release packages confirmed (matches pipeline pre-flight)

---

## Pinned pre-release packages

| Package | Version | Inline rationale | Reflection-load impact | Latest stable |
|---|---|---|---|---|
| `Azure.AI.Projects` | `1.0.0-beta.8` | csproj L23-25: "Agent Framework (Phase 1 - AIPL-002, AIPL-050) - PINNED: Do NOT upgrade without testing. Microsoft.Agents.AI 1.0.0-rc1 requires Microsoft.Extensions.AI >= 10.3.0" | HIGH — Agent Framework loads agent + tool types via reflection at runtime | `2.0.1` (outdated.txt) — major-version drift |
| `Microsoft.Agents.AI` | `1.0.0-rc1` | csproj L24-25 chain (above); Agent Framework hosting | HIGH — Agent registry uses reflection | `1.6.2` (outdated.txt) — minor drift but still pre-release? |
| `Azure.AI.OpenAI` | `2.8.0-beta.1` | csproj L38: "restores compat with OpenAI 2.8.0 (2.1.0 threw MissingMethodException on SerializedAdditionalRawData)" | MEDIUM — Streaming + tool-calling depends on this specific shim | n/a (no stable in outdated.txt) |

**Chain dependency** (per inline comment): `Microsoft.Agents.AI 1.0.0-rc1` → `Microsoft.Extensions.AI 10.3.0` → `OpenAI 2.8.0`. Bumping any link risks `MissingMethodException` at runtime.

---

## Rationale assessment

| Package | Rationale still valid? | Notes |
|---|---|---|
| `Azure.AI.Projects 1.0.0-beta.8` | YES (validated this session) | Stable 2.0.1 exists upstream but moving requires testing the agent + projects API surface; out of scope per spec §Out of Scope. Re-verify per FR-B3 only if a CVE forces movement. |
| `Microsoft.Agents.AI 1.0.0-rc1` | YES (chain-locked) | Latest 1.6.2 may be stable now but the chain pin is what's binding; moving requires coordinated bump of Extensions.AI + OpenAI. Out of scope per spec. |
| `Azure.AI.OpenAI 2.8.0-beta.1` | YES (MissingMethodException avoidance) | Specific runtime exception risk documented; rationale still applies until OpenAI library matures past 2.8.0. |

**FR-B3 verdict**: All 3 pre-release pins remain valid as of 2026-05-24. No Phase 4 action needed. Phase 4 must NOT bump these packages (out of scope per spec).

---

## Out of scope confirmation

Per spec.md §Out of Scope: "Pre-release package version changes (Azure.AI.Projects, Microsoft.Agents.AI, Azure.AI.OpenAI betas) — pinning is documented inline in csproj for chain-compat reasons."

This inventory ↔ Phase 4 boundary preserved.
