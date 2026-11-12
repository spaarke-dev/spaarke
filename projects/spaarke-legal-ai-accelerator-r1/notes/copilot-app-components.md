# Copilot app: which component does what (decision 2026-11-12)

**Owner decision, 2026-11-12: skills first.** In the Microsoft 365 Copilot app, reviews and redlines run through our
Cowork skills. Agent Builder agents are optional: an @-name for questions about a playbook and a quick rated review
in chat, Teams or Word agent mode. They don't build redlines; they point the user to the skill.

Basis: the Cowork Acts 3–6 (the NDA skill built a checked redline set in one pass after plan approval) and Test 3,
2026-11-11 (the NDA Review agent, v0.2 instructions, in the Copilot app). Test 3 made a review table but waited to
be asked before it edited the file. It added 6 comments, all housekeeping, not one per change, and it deleted §8
and §10 without renumbering. Instructions alone did not hold the redline rules; the skill's scripts do.

## Plugin vs agent, for our reviews

| | Cowork skills (folder now, plugin for teams) | Agent Builder agent |
|---|---|---|
| What it is | Written procedures plus reference files plus Python scripts | One instruction text (8,000 characters at most) plus knowledge files, called by @-name |
| Where it runs | Cowork, in the Copilot app. Word lists the plugin but didn't run its skills (tested in Word for the web only, user-scoped upload, 2026-11-01, T-12b; Word desktop asked once). Not tested: Word desktop, admin deployment, an instruction-only skill, a plugin with an MCP tool | Copilot app chat, Teams, Word agent mode, Cowork |
| Runs our Python | Yes (tested) | No |
| Redline | Built and checked by script: one comment per change with its Topic ID, numbering kept, summary page | Whatever the model writes in that run |
| Table and redline in one pass | Yes: plan table, one approval card, then all files | Only as far as the model follows its instructions |
| Cost | Cowork credits per step | Within the Copilot licence |

## Where each piece goes

| Piece | Goes in | Used by | Status |
|---|---|---|---|
| Review skills: `nda-review`, `nda-library-review`, `legal-hold-review` | `Documents/Cowork/Skills/<skill>/` | Cowork | NDA tested (Acts 3–6); legal hold untested in Cowork (C-3) |
| Python engines (redline, summary page, checks, dashboard, audit pack) | Inside each skill's `scripts/` | The skills only | Tested with the NDA skill |
| Playbook masters (`.md`) | `Documents/Cowork/Playbooks/<type>/Playbook master/` | The skills | Reading from this folder untested (C-2) |
| Readable playbook (`(agent knowledge).docx`) | `Documents/Cowork/Playbooks/<type>/` | The agents, as knowledge picked from OneDrive | Untested (A-1) |
| Agent instructions v0.3 (`NDAR-1112-A`, `LHR-1112-A`) | Agent Builder, pasted from the recipe | The agents: rated review in chat, then a line pointing to `/nda-review` or `/legal-hold-review` | Built 2026-11-12; untested (A-2) |
| Word playbook (four columns), review method | `Documents/Cowork/Playbooks/...` | Word, attached by the user | Tested (Word round 1) |
| Templates and precedents | `Documents/Cowork/Playbooks/<type>/Templates` (proposal; owner to confirm) | Word drafting (attached). No drafting skill yet | Word R-2/R-3 in round 2 |
| More review guides, as skills | `Documents/Cowork/Skills/` when built | Cowork | Candidates only. They can't go in an agent: an agent can't carry skills or scripts |

**Hand-off to Word.** The skill saves its files next to the original in OneDrive. To keep working with the Legal
plugin, open the redline in Word for Windows: from the browser, **Editing › Open in Desktop App**. Word for the web
has no Legal plugin.

**The build enforces the agent rule.** `package_docs.recipe_problems()` fails the package build when agent
instructions don't point to the skill or tell the agent to build a redline. Tests:
`AgentRecipeTests.test_agents_review_in_chat_and_send_redlines_to_the_skill` (must pass) and
`test_v02_style_instructions_that_build_a_redline_are_refused` (must fire).

## Still open
- Whether an agent can start a skill (an @-agent in Cowork calling `/nda-review`). Not documented, not tested; we don't
  design around it.
- Test 3: did it start from a fresh copy of the Brightwater NDA? Its 15 substantive edits are stamped 2:12 PM
  under the owner's name, before the agent's own edits (5:51 PM). That may have been an earlier Word review.
- The four-tier draft (`accelerator-components-by-surface.md`) keeps agents in the advanced tiers. This note
  narrows their role; it doesn't settle the four-tier decision.
