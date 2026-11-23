# Alignment brief: specialist-legal-agents-r1 ↔ spaarke-legal-ai-accelerator-r1 (2026-11-23)

> **For:** the accelerator r1 session (`acclerator-r1`, worktree `C:\code_files\spaarke-wt-spaarke-legal-ai-accelerator-r1`).
> **From:** the specialist-legal-agents-r1 setup session.
> **Read with:** `projects/specialist-legal-agents-r1/design.md` §3.9, §3.10 and §10.1 on branch `work/specialist-legal-agents-r1`.
> **Status:** awaiting the accelerator's review. This program's Phase 0 is blocked on it (gate A0, D-A14).

## What this program is

This is a new umbrella program created from the owner's synopsis "Specialist Legal Agents" (2026-11-22). It covers:
- one governed runtime that compiles a layered, attorney-owned Role Profile (platform → archetype → firm → owner → matter, with a one-way ratchet on authority);
- declarative agents in M365 Copilot, backed by the Spaarke BFF MCP connector;
- tuning by tested profile diff against a per-agent golden set.

The first archetype is the NDA reviewer (owner, 2026-11-23).

## Where it sits relative to the accelerator (proposed)

| Accelerator tier | Runtime | Who owns it |
|---|---|---|
| T0 individual, T1 team (incl. the four-tier package's base and advanced tiers) | Files, Cowork skills, Word playbooks, Agent Builder recipes | **Accelerator** |
| T2 firm or department (IT) | Admin-deployed package + Spaarke connector | Accelerator content + **this program's** runtime and tools |
| T3 Spaarke platform | Policy versions, matters, Decision Record | **This program** (with ontology) |

This program is the accelerator's T2/T3 continuation, not a parallel effort. It adopts, rather than re-designs, the accelerator's:
- Microsoft-legal research and the "Microsoft first" non-overlap rules;
- the skill design rules and the name-collision lint;
- the one content model (topic IDs, legal playbook format, review-result schema, version stamp, one customer master);
- the attorney profile (`legal-profile.md`) as the seed of the L3 owner profile;
- the `accelerator-setup` onboarding as the tailoring interview;
- the §5.8a server-side specialist pattern as the T2 runtime;
- the NDA practice content and evals as the Phase 1 seed.

## Proposals the accelerator should check

1. **Framing (D-A12, design §3.9).**
   - Functions (skill + Action; golden set and position schema live here) are the machinery.
   - Archetypes (personas: a set of functions, default positions and an authority ceiling) are the configuration and the Chat front door.
   - Instances are per attorney.
   - There is **one agent per practice area**, and role (partner, associate, paralegal) is a profile dimension. This matches `spaarke-{area}-{capability}`.
   - It also matches the researcher finding that one declarative agent with skills (up to 8) is a better front door than many agents (`declarative-agents-in-word-2026-11-09`).
2. **Microsoft first, program-wide (D-A13, design §3.10).**
   - Where one of Microsoft's 20 Legal for Cowork skills (or the Word legal plugin) owns a function, Spaarke contributes content (the attorney's positions exported as the four-column playbook), data (the connector) and governance. It does not contribute a competing skill.
   - Spaarke-owned functions exist only where Microsoft has none: NDA packaged output, legal hold, intake, invoice review, and the IP practice.
   - The collision gate and the Microsoft catalogue snapshot are shared.
3. **Phase 0 test G6.** This tests coexistence with Legal for Cowork on, and whether a Spaarke declarative agent can invoke or hand off to a Microsoft skill. It extends your T-P2 / T-14 and should reuse its prompt set.

## Questions (Q1–Q8, also in design §10.1)

- **Q1.** Is the team-with-IT tier's admin-deployed packaged agent the same thing as this program's area agent, or something separate? Who owns the NDA declarative agent?
- **Q2.** Is the T3 positions master the ontology Policy versions, exporting to the legal playbook and `.docx` files? Is the L3 owner profile a superset of `legal-profile.md`?
- **Q3.** Is there a written schema for `legal-profile.md` and `topics.yaml` to version against?
- **Q4.** Can the NDA eval corpus and rubric seed the per-agent golden set? What must stay out of the repo or the BFF (`answer-key/`, the excluded real NDAs)?
- **Q5.** What is the status of T-P2 / T-14? Should G6 extend it, or wait for it?
- **Q6.** Does skills-first (agents never build redlines) extend to T2/T3, where the BFF engine can build the redline?
- **Q7.** Who keeps the Microsoft legal catalogue snapshot (K3) current? Should the collision lint move to a shared place?
- **Q8.** Do the four-tier package and this program's phases need one roadmap?

## How to respond

Answer in the accelerator's own notes, or reply to the session that sent this brief. This program records the answers in its design.md §10.1 and decision log. **Do not edit this worktree from the accelerator session**, and this program will not edit the accelerator's.
