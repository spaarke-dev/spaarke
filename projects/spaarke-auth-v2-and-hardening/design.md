# Design — Spaarke Auth v2 + Hardening

The full design document for this project is the comprehensive audit:

**[.claude/AUDIT-FINDINGS-AUTH-SYSTEM.md](../../.claude/AUDIT-FINDINGS-AUTH-SYSTEM.md)**

That file is canonical for this project until `ADR-027` is written (task 090) and approved. It captures:

| Section | Content |
|---|---|
| §1 | Executive summary |
| §2 | Current state (8 server flows, 6-strategy client cascade, 17 consumer surfaces) |
| §3 | Findings by severity (Critical / High / Medium / Low) |
| §4 | Target architecture (function-based contract, pluggable strategies, MSAL invariants, env-independence) |
| §4.4 | Regression invariants INV-1..INV-8 (must not break) |
| §4.5 | Future auth scenarios (SPE, B2B, B2C, mobile, etc.) |
| §5 | Final scope — workstreams A through F |
| §6 | Explicitly out of scope (and why) |
| §7 | SOC 2 / enterprise review readiness map |
| §8 | Pre-flight conflict map + three-layer enforcement strategy |

## How to use this design

- **For project context**: read the audit doc top to bottom once.
- **For task execution**: each task POML lists the relevant audit doc sections in its `<knowledge>` block. Read those sections before executing the task.
- **For architecture questions**: §4 (target architecture) + §4.4 (invariants) are the primary references.
- **For "is this in scope?"**: §5 (scope) + §6 (out of scope).
- **For "what about SOC 2?"**: §7.
- **For "why are we doing X?"**: §3 (findings) traces every change back to a specific finding.

## When ADR-027 ships

After task 090 (`Draft ADR-027`) completes:
- `ADR-027` becomes the canonical reference
- The audit doc is preserved as the historical investigation that triggered v2
- Updated this design.md to point at both
