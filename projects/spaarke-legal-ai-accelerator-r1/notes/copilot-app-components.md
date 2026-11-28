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
| Where it runs | Cowork, in the Copilot app. Word for the web listed our plugin but didn't run its skills (T-12b, 2026-11-01). **Word desktop follows instructions-only plugin skills** (our probe, WP-2, 2026-11-25) **and calls Spaarke MCP tools through one** (`spaarke-decisions`, admin-deployed, 2026-11-26). **Word desktop does not load a skill that ships Python scripts** (WP-1, 2 tries, 2026-11-26: answered from the instructions skill instead), **but it runs fixed Office.js that an instructions-only skill carries, as written** (WP-5 run 3, 2026-11-27: one tracked replacement and one comment, hash-proven; runner form `async function main(context) { ... }`, no `Word.run`, return value not passed back, refuses `toString` and top-level `await`). **WP-6, 2026-11-27: Copilot fills a data line and our fixed code applies the edits** (3 tracked changes + 3 comments, hash-exact); 17 KB of code carried intact. Word has **two runners**: Office.js via a sub-agent (Word API, no return value) and an HTML-document runner `(doc) => {}` (return value, errors and console come back; Copilot shims our calls onto HTML). The error channel is proven on the HTML runner only | Copilot app chat, Teams, Word agent mode, Cowork |
| Runs our Python | Yes (tested) | No |
| Redline | Built and checked by script: one comment per change with its Topic ID, numbering kept, summary page | Whatever the model writes in that run |
| Table and redline in one pass | Yes: plan table, one approval card, then all files | Only as far as the model follows its instructions |
| Cost | Cowork credits per step | Within the Copilot licence |

**From specialist-legal-agents-r1 (2026-11-28, their dev-tenant runs; not re-tested here):**
- Each declarative agent's MCP action needs its own per-user sign-in (a "Sign in to Spaarke MCP …" card), even when
  the tenant connector is already connected.
- In an agent, a user's first "list every tool" answer can name no tools until the agent has called one Spaarke tool
  in that chat.
- A skills-only Cowork plugin reaches no Spaarke tool unless the **Spaarke MCP** source is switched on. Our NDA tests
  run with it off, so any test that needs the MCP must check the switch first.

**Installing a plugin: one app id per route (2026-11-27, WP-6 setup).** The admin center (Tools › Upload) refuses a
plugin whose app id was already uploaded through Cowork › Customize › Plugins, even after it's removed there. The
only message is "We couldn't validate this manifest file". A new app id fixes it. Our accelerator builds give each
package a fixed id, so a package tested through Cowork needs a different id for admin deployment. Still untested:
whether the admin center accepts skills that ship Python scripts. Detail: `poc-e2e/results.md`, WP-6 setup.

## Where each piece goes

| Piece | Goes in | Used by | Status |
|---|---|---|---|
| Review skills: `nda-review`, `nda-library-review`, `legal-hold-review` | `Documents/Cowork/Skills/<skill>/` | Cowork | NDA tested (Acts 3–6); legal hold untested in Cowork (C-3) |
| Python engines (redline, summary page, checks, dashboard, audit pack) | Inside each skill's `scripts/` | The skills only | Tested with the NDA skill |
| Playbook masters (`.md`) | `Documents/Cowork/Playbooks/<type>/Playbook master/` | The skills | Reading from this folder untested (C-2) |
| Readable playbook (`(agent knowledge).docx`) | `Documents/Cowork/Playbooks/<type>/` | The agents, as knowledge picked from OneDrive | Untested (A-1) |
| Agent instructions v0.3 (`NDAR-1112-A`, `LHR-1112-A`) | Agent Builder, pasted from the recipe | The agents: rated review in chat, then a line pointing to `/nda-review` or `/legal-hold-review` | Built 2026-11-12; untested (A-2) |
| Word playbook (four columns), review method | `Documents/Cowork/Playbooks/...` | Word, attached by the user | Tested (Word round 1) |
| Templates and precedents | `Documents/Cowork/Playbooks/<type>/Templates` (owner decision, 2026-11-19) | Word drafting (attached). No drafting skill yet | Word R-2/R-3 in round 2 |
| More review guides, as skills | `Documents/Cowork/Skills/` when built | Cowork | Candidates only. They can't go in an agent: an agent can't carry skills or scripts |

**Hand-off to Word.** The skill saves its files either next to the original in OneDrive or in the Cowork task's
output folder (under `Documents/Cowork/`), and names the place in its reply. Both happen: on 2026-11-25 the NDA run
saved to the task folder and the legal hold run saved next to the original (`results.md`, Tier tests round 2; the
skills allow both, design decision 13). To keep working with the Legal
plugin, open the redline in Word for Windows: from the browser, **Editing › Open in Desktop App**. Word for the web
has no Legal plugin.

**The build enforces the agent rule.** `package_docs.recipe_problems()` fails the package build when agent
instructions don't point to the skill or tell the agent to build a redline. Tests:
`AgentRecipeTests.test_agents_review_in_chat_and_send_redlines_to_the_skill` (must pass) and
`test_v02_style_instructions_that_build_a_redline_are_refused` (must fire).

## Still open
- **Pairing the review skills with the Spaarke MCP** (from spaarkeai-mcp-bff-api-r2, 2026-11-26; issue #3257; their note
  `projects/spaarkeai-mcp-bff-api-r2/notes/cross-project-cowork-review-skill.md` on `work/spaarkeai-mcp-bff-api-r2`).
  The skill would find the matter and document with `spaarke_search_documents` / `spaarke_find_records` /
  `spaarke_get_document` and could start `spaarke_run_playbook`. Two owner decisions, both ours:
  1. **Document into the sandbox.** `spaarke_get_document` returns metadata and open links only, not bytes. Whether
     Cowork's SharePoint connector can read SharePoint Embedded files is unverified (needs a `researcher` check).
     Until then a Spaarke document reaches the skill only if the user downloads it and hands it over. A
     content-returning tool would be new MCP surface (CLAUDE.md §11), not in mcp-r2's scope.
     **Researched 2026-11-26** (`notes/research/cowork-access-to-spe-files-2026-11.md`): Cowork can't read SPE
     bytes natively (file picker = OneDrive, SharePoint, Teams; 403 observed 2026-10-30; our container type has
     discoverability off), and a pasted link won't change that. SPE retrieval surfaces (Retrieval API, Work IQ
     `retrieve`) return text, not files. Routes: (1) user downloads and attaches: works today; (2) the BFF stages
     a copy in the user's OneDrive, user attaches it: inferred, needs an owner decision on copying client content
     to OneDrive and an ADR-028 check; (3) a tool result saved as a sandbox file: undocumented, one tenant test
     settles it; (4) a signed URL fetched by the script: undocumented, egress likely blocked.
  2. **Matter email.** For matter work the close-out would call `spaarke_compose_email`: pre-filled draft, the user
     presses Send, the email is archived to the matter. That replaces the close-out's Outlook send card. It fits
     our "never send email on my behalf" rule, since the user sends.
- Whether an agent can start a skill (an @-agent in Cowork calling `/nda-review`). Not documented, not tested; we don't
  design around it.
- Test 3: did it start from a fresh copy of the Brightwater NDA? Its 15 substantive edits are stamped 2:12 PM
  under the owner's name, before the agent's own edits (5:51 PM). That may have been an earlier Word review.
- The four-tier package (`accelerator-components-by-surface.md`, adopted 2026-11-19) keeps agents in the advanced
  tiers; this note narrows their role there.
