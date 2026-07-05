# spaarke-ai-code-audit-r1 — AI Code Inventory + Migration Map

> **Created**: 2026-07-05 (Fable 5 session, per operator direction 2026-07-05)
> **Parent epic**: #421 SPAARKE AI
> **Origin**: strategic pivot in `spaarke-ai-platform-unification-r7` — the canonical
> AI architecture doc (`docs/architecture/SPAARKE-AI-ARCHITECTURE-AND-COMPONENT-DESIGN.md`,
> v0.2.6) defines the target model; this project inventories ALL existing AI code
> against it so §4-7 can be designed against real constraints and deadwood identified.
> **Portfolio registration**: pending — `/devops-project-register --from-folder
> projects/spaarke-ai-code-audit-r1 --epic 421` (project type Cleanup / Data / Process
> is the operator's call).

---

## Why this project exists

Spaarke has accumulated ~30 AI-related projects (spaarke-ai-platform-unification
R1-R7 plus ~20+ others: document-intelligence, insight-engine, chat, daily-update,
playbook, email-to-document, chat-routing-redesign, ...). Many components the target
architecture requires have been built in one form or another; others are deadwood.
Operator (2026-07-04): *"what happens to all of this code — many required components
have been built in one way or another. BUT if not then we need to get rid of all
the deadwood."*

## The 3-step plan (operator-directed)

| Step | What | Deliverable | Status |
|---|---|---|---|
| **1** | Inventory ALL AI-touching code across all worktrees against the 5 target categories (Session / Consumer / Tool / Dispatcher / Manifest) + functional capabilities | [`SPAARKE-AI-CODE-INVENTORY.md`](SPAARKE-AI-CODE-INVENTORY.md) | ✅ v1.0 (2026-07-05) — operator review pending |
| **2** | Draft §4-7 of the canonical design doc, informed by Step 1 | `docs/architecture/SPAARKE-AI-ARCHITECTURE-AND-COMPONENT-DESIGN.md` v0.3 | ✅ v0.3 (2026-07-05) — D7-D12 pending operator ratification (incl. flagged deviation D10) |
| **2.5** | Greenfield conceptual design + convergence overlay (operator-added 2026-07-05) | [`GREENFIELD-CONCEPTUAL-DESIGN.md`](GREENFIELD-CONCEPTUAL-DESIGN.md) v0.2 + [`OVERLAY-MATRIX.md`](OVERLAY-MATRIX.md) v1.0 | ✅ drafted — overlay matrix under operator review (5 exceptions E-1..E-5; OQ-1/OQ-3 resolutions proposed) |
| **3** | Migration map: Track A (target alignment per overlay matrix) + Track B (deadwood sweep) + sequencing | [`SPAARKE-AI-MIGRATION-MAP.md`](SPAARKE-AI-MIGRATION-MAP.md) | 🔲 after overlay review |

## Scope

- **In**: every AI-touching code surface in the `spaarke` repo across master + all
  active worktrees — BFF (`Services/Ai`, `Api/Ai`, `Models/Ai`, jobs), client shared
  libs (SprkChat, AI widgets, DailyBriefing), SpaarkeAi code page, wizards, Dataverse
  AI schema + playbooks/JPS, plugins, Office add-ins AI surfaces, `.claude/catalogs`.
- **Out**: non-AI platform code (auth, grids, ribbons, deploy scripts) except where
  it hosts an AI entry point. No code changes — this is a read-only audit.

## Method (Step 1)

Delegated fan-out (operator-confirmed): parallel read-only Explore agents —
one set covering master by subsystem, one agent per active worktree scoped to its
merge-base diff vs master. Findings synthesized by the main session into the
inventory doc. Sub-agents cannot write to `.claude/` (CLAUDE.md §3) and write
nothing anywhere — they return structured text only.
