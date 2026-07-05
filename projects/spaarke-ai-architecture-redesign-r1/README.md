# spaarke-ai-architecture-redesign-r1

> **Created**: 2026-07-05 · **Parent epic**: #421 SPAARKE AI
> **Status**: design.md v1.0 drafted — operator review → `/design-to-spec` → `/project-pipeline`
> **Portfolio registration**: pending (`/devops-project-register` after pipeline init)

## What this project delivers (the one-liner)

**A working Spaarke Assistant**: drop in a document or type a plain-language
request and reliably get analysis, cited answers over documents AND Dataverse,
records created with confirmation, and drafts — composing across steps in one
conversation. And afterward, new legal capabilities are catalog rows a business
analyst authors, not engineering projects.

## Where this came from

The 2026-07-04/05 strategic pivot + `spaarke-ai-code-audit-r1` (3 steps:
full-estate inventory → converged target design → migration map). All
architecture decisions are **ratified** — this project implements, it does not
re-open design.

| Input | Location |
|---|---|
| **design.md** (this project's charter — review this) | [`design.md`](design.md) |
| Target architecture (living, canonical) | [`docs/architecture/SPAARKE-AI-ARCHITECTURE-AND-COMPONENT-DESIGN.md`](../../docs/architecture/SPAARKE-AI-ARCHITECTURE-AND-COMPONENT-DESIGN.md) **v0.4** |
| Frozen audit inputs (inventory, greenfield+Q&A, overlay matrix, ADR review, migration map, MCP research, auditor evidence) | [`notes/audit-inputs/`](notes/audit-inputs/README.md) |
| New governance | ADR-039 (Grounded Execution & Closed Catalogs) + ADR-040 (Session Ledger) — Proposed; promoted to Accepted at phases P1/P0 |

## Shape of the work

Five phases with **user-verifiable UAT gates** (G-P1..G-P4, design.md §2) and
hard cutovers per surface; a continuous Track-B deadwood sweep from day one.
Hot paths: BFF=Y, SpaarkeAi=Y, skill-directives=Y.
