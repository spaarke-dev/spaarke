# Spaarke Claude Code Focused Audit — Three Directives

> **Source**: claude.ai consultation, 2026-05-14
> **Status**: Input to spec.md
>
> This document is preserved as-is from the claude.ai consultation. It captures three discrete improvements that were recommended for the Spaarke project's Claude Code setup. The spec.md in this project folder consolidates these with additional improvements identified during the 2026-05-14 working session (where multiple AI-procedure quality issues were exposed in practice).

---

**Audience**: Claude Code agent
**Scope**: Three discrete improvements to the team's Claude Code setup
**Estimated effort**: 4-6 hours of agent execution plus 2-3 hours of human review

Each directive below is self-contained. Execute them in order — Directive 1 first (researcher subagent), then Directive 2 (skills review), then Directive 3 (CLAUDE.md review). The order matters because the skills review may surface things that should move into CLAUDE.md (or vice versa), and you want the inventory work done first.

Commit after each directive completes successfully.

---

# Directive 1: Build the `researcher` subagent

## Purpose

Create a named subagent that handles deep technical investigations of Microsoft AI platform pieces and other external research. The subagent accumulates findings across sessions in persistent memory, so the team's "what we've learned about Microsoft platforms" knowledge compounds rather than starting fresh each time.

This subagent is bounded in scope:
- **In scope**: external platform research (Microsoft Foundry, Agent Framework, MCP Apps, declarative agents, SPE, Dataverse MCP, etc.); deep API investigations; comparing platform options; investigating unfamiliar errors
- **Out of scope**: project task execution (handled by `project-pipeline`); code review (handled by `project-pipeline` and GitHub checks); spec writing (handled by `design-to-spec`); routine documentation lookups (handled inline in the main session)

The researcher is for when the main session needs technical knowledge that doesn't exist in the project yet and shouldn't pollute the main context to acquire.

## Tasks

1. Verify `.claude/agents/` directory exists at the project root; create if it doesn't
2. Create `.claude/agents/researcher.md` with the structure below
3. Verify the project has the `knowledge/` directory referenced in the subagent's system prompt; if not, note this as a dependency that the directory needs to be created (per the separate knowledge base setup work)
4. Verify subagent persistent memory is available in the current Claude Code version (`claude --version` should show v2.1.33 or later)
5. Test the subagent by invoking it with a simple research query and verifying it operates correctly in its isolated context

## The file to create

Create `.claude/agents/researcher.md` with this content:

```markdown
---
name: researcher
description: Use for deep-dive technical investigation of Microsoft AI platform APIs, library behavior, unfamiliar errors, or comparing platform options. Accumulates findings across sessions via project memory. Do NOT use for project task execution, code review, or spec writing — those have dedicated patterns.
tools: Read, Bash, Glob, Grep, WebSearch, WebFetch
model: opus
effort: high
memory: project
---

You are a technical researcher for the Spaarke project. Your job is to investigate Microsoft AI platform pieces and adjacent technical topics in depth, then return synthesized findings to the caller.

## Operating principles

1. **Check known sources first.** Before searching externally, check in this order:
   - Your own `MEMORY.md` — you may have investigated this before
   - The project's `knowledge/` directory — curated Microsoft platform reference material
   - The project's `docs/architecture` and `docs/guides` — team-authoritative documentation
   - The project's existing ADRs (`ADR-*.md` at the project root)

   Only search externally if the answer isn't in these sources.

2. **External research order of preference**: Microsoft Learn (`learn.microsoft.com`), Microsoft official GitHub repos (`github.com/microsoft`, `github.com/Azure-Samples`, `github.com/OfficeDev`), the Microsoft 365 Developer Blog, trusted MVP repositories. Generic web search and Stack Overflow only when the above don't have answers.

3. **Return synthesized findings, not raw search results.** The caller wants conclusions, not a transcript of what you read. Structure findings as: what was asked, what you found, what's still uncertain, sources consulted.

4. **Don't modify project code.** You are read-only on the codebase. If the investigation reveals something that should change in code, surface it in the findings; the main session decides whether to act.

5. **Update MEMORY.md after every investigation.** Append a section with: date, question asked, key findings (3-5 sentences max), sources consulted (URLs or file paths), follow-up questions that emerged. Keep MEMORY.md concise — prioritize findings over process. Your accumulated knowledge is what makes you more useful over time.

## Memory file structure

Your `MEMORY.md` lives in your project-scoped memory directory. Maintain it as a chronological log with this structure per entry:

```
## YYYY-MM-DD: <Brief question title>
**Question**: <one sentence summarizing what was asked>
**Findings**: <3-5 sentence synthesis>
**Sources**: <bulleted list of URLs or file paths>
**Open questions**: <what's still uncertain, if anything>
```

When you start an investigation, search MEMORY.md for relevant prior entries. If you find one, lead your response with "I investigated something related on [date]: [brief summary]. Here's what's new or different about this question..."

## When to refuse a request

If the caller asks you to do work that's out of scope (executing a project task, writing code, doing code review), respond with a brief explanation of why this should go to the main session or a different pattern, and decline. Examples:

- "Execute this task" → "This is project execution work. The main session handles this through the project-pipeline skill, not a research subagent. I can investigate any specific technical questions the task surfaces."
- "Review this code" → "Code review happens in the main session as part of project-pipeline's task-completion flow, or via GitHub CI checks. I focus on external research. Happy to investigate any specific Microsoft platform questions the review raises."
- "Write a spec for X" → "Spec writing is handled by the design-to-spec skill in the main session. I focus on the research that informs specs. If there are external technical questions to answer before writing the spec, I can help with those."

## Format for findings

Your response to the caller should be structured for use in their main session:

**Summary** (2-3 sentences): the headline answer

**Details**: the substantive findings, organized by sub-question if the topic has multiple parts

**Sources**: URLs and file paths consulted, with brief notes on which was most authoritative

**Caveats**: what's preview-quality, what's likely to change, what you're less sure about

**Recommended follow-ups**: if there are obvious next investigations or things to verify in code, list them

Keep responses focused. The caller is trying to make a decision or understand something; long prose hurts that goal.
```

## Acceptance

- `.claude/agents/researcher.md` exists with the content above
- The agent successfully invokes when explicitly delegated to (test with: "Use the researcher subagent to find out the current preview status of Foundry IQ remote sources")
- The subagent operates in an isolated context (verified by the main session not showing the subagent's intermediate tool calls)
- The subagent creates and writes to its `MEMORY.md` (verify by checking the project memory directory after the test invocation)

---

# Directive 2: Audit and refine existing skills

## Purpose

Skills are how Claude Code gets relevant context loaded just-in-time. Skills that are too long, vague in their trigger, or contradict each other dilute the agent's effective behavior. This directive audits each skill against current best practices and refines or removes as appropriate.

## Best practices to apply (as of May 2026)

These are the standards each skill should meet after this review:

1. **The description field is the trigger.** Claude Code does fuzzy matching on the description to decide whether to load the skill. The description should lead with the trigger phrase a developer would actually say or do, then the action verb. Example: not "Helps with database schema work" but "When working on Dataverse table schemas, plugins, or Web API queries — guides schema design and migration patterns."

2. **SKILL.md body under 200 lines.** Anything longer should be split: keep trigger conditions, high-level guidance, and constraints in SKILL.md; move detailed examples, references, and patterns to subdirectories (`references/`, `examples/`, `scripts/`). The agent loads SKILL.md first and dereferences supporting files only when needed.

3. **Goal-oriented, not prescriptive.** Skills should tell the agent what to achieve and what to avoid, not script step-by-step instructions. Trust the agent's judgment within the constraints. "Achieve X. Avoid Y. Reference Z for context." beats "Step 1, step 2, step 3."

4. **Gotchas section.** Every skill should have a "Gotchas" or "Common mistakes" section near the bottom. This is the highest-signal content — failure modes the agent should know about. If the skill doesn't have one, add it (even if initially with stub content marking known issues to fill in over time).

5. **No overlap or contradiction with other skills.** Two skills with overlapping descriptions cause unpredictable triggering. If overlap exists, either merge or add explicit "use this for X, not Y" lines.

6. **No deterministic rules.** Things that must happen every time belong in `.claude/settings.json` or hooks, not in skill prose. The agent treats skill content as advisory; it treats settings as binding.

7. **References to current docs and knowledge.** If the skill points at outdated docs or paths, update. Cross-reference the `knowledge/` directory where applicable.

## Tasks

### Step 2.1: Inventory

1. List all files under `.claude/skills/` (every SKILL.md and supporting file)
2. For each skill, capture: name, description text, body line count, presence of Gotchas section, files in supporting subdirectories
3. Write inventory to `.claude/AUDIT-FINDINGS-SKILLS.md` with one section per skill

### Step 2.2: Per-skill audit

For each skill, evaluate against the seven best practices above. Write the audit to the same `AUDIT-FINDINGS-SKILLS.md`. For each skill, capture:

- **Description quality**: pass / needs revision / vague
- **Body length**: line count, whether splitting is needed
- **Goal-oriented vs prescriptive**: pass / mixed / heavily prescriptive
- **Gotchas section**: present / missing / stub
- **Overlap with other skills**: list any overlaps
- **Deterministic rules to extract**: any "always do X" or "never do Y" content that should move to settings/hooks
- **References to verify**: list any external links or file paths that should be checked for currency
- **Recommended action**: refine in place / split / merge with skill X / remove / leave alone

### Step 2.3: Execute the refinements

After the human reviewer approves the audit findings, execute the recommended actions:

- **Refine in place**: rewrite descriptions, restructure bodies, add Gotchas sections, convert prescriptive content to goal-oriented
- **Split**: move detailed content from SKILL.md to subdirectories; have SKILL.md point at them
- **Merge**: combine overlapping skills into one with clear scope; archive the others (move to `.claude/archive/<date>/` rather than deleting)
- **Remove**: archive (don't delete) skills that aren't earning their place

Important: do not execute remove or merge actions without explicit human confirmation in the audit findings document. Refinement-in-place is safe; destructive changes need a sign-off.

### Step 2.4: Verify

After refinements, start a fresh Claude Code session and run a representative task that should trigger one of the refined skills. Verify the skill loads when expected and the agent uses its guidance.

## Acceptance

- `AUDIT-FINDINGS-SKILLS.md` exists with complete per-skill audit
- All skills approved for refinement-in-place have been updated
- No skill exceeds 200 lines without justification (split into subdirectories where larger)
- Every remaining skill has a Gotchas section (stub is acceptable if the agent doesn't know specific gotchas yet)
- Skills marked for removal or merge have been archived to `.claude/archive/<date>/` with the human reviewer's confirmation

---

# Directive 3: Audit and refine CLAUDE.md

## Purpose

CLAUDE.md is loaded into every session before any task. It competes with task-specific context for the agent's attention. The current best practice is to treat CLAUDE.md as a *router* rather than a *manual* — short, focused, pointing at where detail lives rather than containing the detail itself.

## Best practices to apply (as of May 2026)

1. **Under 200 lines, ideally under 150.** Every line costs attention across every session. Less is more.

2. **Four things to cover, briefly:**
   - **What is this project?** One paragraph on the project's nature and current focus.
   - **What are the load-bearing principles?** The architectural rules every task should honor (ADRs, tenant-resident posture, etc.). Bullet list, not prose.
   - **Where does detail live?** Pointers to `.claude/skills/`, `knowledge/`, `docs/architecture`, `docs/guides`, ADR files, briefings. The router function.
   - **How should the agent work?** Disposition — interview-first for non-trivial work, plan mode default, end-of-project doc updates, when to invoke the researcher subagent.

3. **No workflow-specific instructions.** Anything that only applies to certain task types belongs in a skill, not CLAUDE.md. CLAUDE.md is always-on; skills are triggered.

4. **No deterministic rules.** Things that must happen every time belong in `.claude/settings.json` or hooks. CLAUDE.md content is advisory and the agent may not honor it under context pressure.

5. **No duplicated content.** If a skill, ADR, or knowledge file covers something, CLAUDE.md should point at it, not repeat it. Duplication signals to the model that the same point made in multiple places is somehow less important, not more.

6. **Cross-references that work.** Every file path mentioned in CLAUDE.md should exist. Every URL should resolve. Audit and update.

## Tasks

### Step 3.1: Read and inventory

1. Read current `CLAUDE.md`
2. Capture: total line count; major sections; any duplications with skills (verify by cross-referencing skill content from Directive 2)
3. Write inventory to `.claude/AUDIT-FINDINGS-CLAUDEMD.md`

### Step 3.2: Section-by-section audit

For each section of CLAUDE.md, classify it into one of:

- **Keep in CLAUDE.md**: project nature, load-bearing principles, location pointers, agent disposition. Should be under the four categories above.
- **Move to a skill**: workflow-specific guidance that only applies to certain tasks.
- **Move to settings.json or hooks**: deterministic rules.
- **Move to docs/architecture or docs/guides**: project documentation that doesn't need to be in every session's context.
- **Move to a knowledge/ file**: Microsoft platform reference material.
- **Delete**: stale, redundant, or unused.

Document the classification for each section in the audit findings.

### Step 3.3: Plan the new structure

Before rewriting, write a target outline in the audit findings showing:

- New CLAUDE.md outline (4 sections, brief)
- Each existing section's disposition (where the content is moving, if anywhere)
- Any new skills that need to be created (if workflow content is being extracted)
- Any settings.json changes needed (if deterministic rules are being extracted)

Get human sign-off on the plan before executing the rewrite.

### Step 3.4: Execute the rewrite

After sign-off:

1. Archive the current CLAUDE.md to `.claude/archive/<date>/CLAUDE.md` (preserving the original)
2. Write the new CLAUDE.md following the four-section structure
3. Move extracted content to its new homes (new skills, settings, docs, knowledge)
4. Verify all cross-references in the new CLAUDE.md resolve

### Step 3.5: Verify

Start a fresh Claude Code session. Confirm:

- CLAUDE.md loads cleanly without errors
- The agent demonstrates awareness of the project's load-bearing principles in early responses
- When asked a workflow question, the agent correctly references the skill that handles it (rather than trying to answer from CLAUDE.md content that's no longer there)
- The agent references docs/architecture or knowledge/ when relevant

## Acceptance

- `AUDIT-FINDINGS-CLAUDEMD.md` exists with complete section-by-section audit and target outline
- Human reviewer has approved the target outline
- New CLAUDE.md is under 200 lines (ideally under 150)
- Original CLAUDE.md is archived (not deleted)
- All extracted content has a new home; nothing is orphaned
- All cross-references in the new CLAUDE.md resolve
- The agent's behavior in a verification session demonstrates the new structure is working

---

## Constraints across all three directives

1. **Commit after each directive.** Directive 1 gets its own commit (researcher subagent created and tested). Directive 2 gets one or more commits (audit findings, then refinements). Directive 3 gets one or more commits (audit findings, then rewrite). The human reviewer can examine each directive's outcome independently.

2. **Don't delete; archive.** Anything being removed (skills, CLAUDE.md content) goes to `.claude/archive/<date>/` first. Reversibility matters.

3. **Get human sign-off before destructive changes.** Phase 1 (researcher subagent) is purely additive and doesn't need sign-off beyond verifying it works. Phases 2 and 3 have audit findings documents that should be reviewed by the human before refinements are executed.

4. **Be honest about what doesn't apply.** If a skill is already in good shape, the audit should say so and no refinement happens. If CLAUDE.md is already well-structured, the audit should reflect that. Don't fabricate problems to justify making changes.

5. **What's out of scope for this audit**:
   - Hooks (only add if specific recurring issues warrant them)
   - settings.json (cover incidentally when extracting deterministic rules from CLAUDE.md or skills; otherwise leave alone)
   - GitHub CLI setup
   - MCP server inventory
   - Plugin marketplace
   - Knowledge base setup (separate work; this audit assumes whatever state knowledge/ is in)

---

## What success looks like

After all three directives complete:

- The team has a `researcher` subagent that accumulates Microsoft platform knowledge over time
- Every skill is precisely-triggered, focused, goal-oriented, and has a Gotchas section
- CLAUDE.md is under 200 lines, acts as a router rather than a manual, points at the right places for detail
- The agent's behavior is more predictable because instructions are less likely to compete for attention
- Future additions (new skills, new knowledge) have clear places to land

This is the minimum-viable improvement that produces measurable better outcomes. Other potential improvements (hooks, settings tuning, MCP audit) can be addressed later if specific issues warrant them.
