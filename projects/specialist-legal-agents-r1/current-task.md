# Current Task State — specialist-legal-agents-r1 (umbrella program)

> **Last Updated**: 2026-11-23 (setup session "Set up the specialist legal agents project")
> **Worktree**: `C:\code_files\spaarke-wt-specialist-legal-agents-r1` · **Branch**: `work/specialist-legal-agents-r1` · **Board**: Issue #3002 under Epic #421
> **Phase**: pre-`/design-to-spec`. No POML tasks. Do NOT run `/design-to-spec` until the owner says so.

## Quick Recovery

| Field | Value |
|---|---|
| **Active work** | The owner asked (2026-11-23): (1) "I'm still not clear on the structure and core components of the model" — this program must build on accelerator-r1 both for NDA capabilities and **as the pattern for other persona/skill-based agents**; (2) explain the capability differences between Word Copilot and the M365 Copilot app, and within it between Chat and Cowork, and check whether Ignite 2026 collapsed Chat/Cowork, and what "Copilot Home" is |
| **Step** | DONE: design.md v0.5 has §0 (model) and §3.11 (surfaces, from notes/research/copilot-surfaces-post-ignite-2026-11-23.md). Next: owner reviews the model; D-A11 still "discuss first" |
| **Design version** | design.md v0.5 (commit `b5ebb37b3c`) |
| **Uncommitted** | none |

## Owner context to keep (2026-11-23)
- accelerator-r1's purpose: a simple **individual install package, no IT**, a more capable, purpose-built NDA review on top of Microsoft's Copilot for legal (Legal for Cowork + Word legal plugin), as shown in the Cowork redlining video. **It is not done; it completes in parallel with this project.**
- This program builds on the accelerator in two ways: (a) NDA-specific capabilities; (b) the accelerator's practice-accelerator anatomy (customer layer / practice layer / core, accelerator design §5.1) is the **pattern** for every other persona or skill.
- Microsoft's 20 Legal for Cowork skills: Microsoft-first, no collisions, ride upgrades (design §3.10, D-A13).

## Draft core model (to write into design.md)
- **Components:** Microsoft platform (surfaces + Legal skills + built-ins) · Practice content (accelerator `practices/<p>/`: topics, baseline positions, references, evals, schemas) · Function (skill + engine + output contract + golden set; Python engine in Cowork, C# engine in BFF) · Persona/archetype (set of Spaarke + Microsoft functions, default positions, authority ceiling, role; one per practice area) · Attorney profile/instance (T0/T1 `legal-profile.md` + personal playbooks; T2/T3 Dataverse master, exported) · Delivery wrappers per surface (Word playbook .docx, Cowork skill/plugin, Agent Builder agent, packaged DA, area DA + connector) · Spaarke runtime (T2/T3 only: connector tools, BFF engines, profile compiler + ratchet, gates, ledger, tuning loop).
- **Tiers:** T0/T1 = accelerator (all four package tiers, no connector) → T2 = connector enters → T3 = platform. Same content at every tier.

## Open owner decisions
- D-A11: NDA ownership. accelerator-r1 proposed a split by tier (it owns the T0/T1 NDA agent content; this program owns the area agent). Claude proposed a **split by layer**: the NDA function content has one owner (the accelerator until its R1 closes, then this program), each project owns its own surface wrappers, and one Spaarke NDA agent per tenant. **The owner chose "Discuss first"; not decided.**
- D-A12 framing (function vs persona; one agent per practice area); D-A13 Microsoft-first; D-A16 one roadmap; Q7 owner of the Microsoft catalogue snapshot; D-A7 server-side profile and gates (decided at G1); "Spaarke Legal Review" agent: what it is (C3).
- Ontology-r1 must confirm the "Policy versions" term (Q2).

## Next action
When the researcher returns: read its note → write design.md §0 "The model in one page" (component table + layer diagram + tier table + "what we reuse from the accelerator as the pattern") and §3.11 "Surfaces: Word vs Chat vs Cowork vs Home" (matrix + convergence verdict + design consequences) → bump to v0.5 → commit and push → explain to the owner with a visual.

## Standing directives
- Never `git stash`; never `python3` in Bash; every Agent launch names a model; sub-agents can't write `.claude/`.
- Don't edit the accelerator or mcp-r1 worktrees; coordinate by message (acclerator-r1 session) only.
- Accelerator constraints: never put `answer-key/` or the real NDAs (Corteva, RPX/Helios) anywhere an agent can read; reuse `FORBIDDEN_IN_PACKAGE`.
