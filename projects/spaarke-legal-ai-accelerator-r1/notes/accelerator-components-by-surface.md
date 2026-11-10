# Spaarke AI Accelerator for Microsoft Copilot: components by surface and deploy path (DRAFT 2026-11-10)

Status: proposal for owner decision. It would revoke the standing directive "no declarative agents in the package"
(spike DA-1). Mechanics first; quality and scoring come later. Basis: `notes/poc-e2e/synopsis-word-and-cowork.md`,
`results.md`, design §2.4, §5.2, §5.4 and §8.1a, and researcher note `declarative-agents-in-word-2026-11-09`.

## Two deploy paths

| | **Individual** (self-install, no IT expected) | **Team** (shared library, IT involved) |
|---|---|---|
| Who sets it up | The attorney | Library owner (SharePoint site owner) plus a tenant admin |
| Where the content lives | OneDrive `Documents/Legal Knowledge/` | SharePoint `<site>/Shared Documents/Legal Knowledge/`, read-only for attorneys |
| Agents | Built by the attorney in **Agent Builder** from our recipe; private | **Packaged agents** uploaded by the admin and assigned to groups; or built once by the library owner and shared |
| Cowork skills | Plugin uploaded and published to **Only you** (or skill folders in OneDrive) | The same plugin, deployed by the admin to users or groups |
| Updates | Extract the new zip over the old one; re-paste agent instructions when they change | Library owner replaces the playbook; the admin uploads a new agent or plugin version |

**What still needs a tenant admin, even on the individual path** (prerequisites, not our install steps):
- M365 Copilot licence (the Legal plugin, agent knowledge files and Cowork all need it);
- Microsoft's Legal plugin switched on;
- Cowork enabled, with credits;
- Agent Builder not blocked;
- organization-built plugins not blocked in Cowork.

The package's preflight (design §8.1a step 0) checks these and hands the user an IT one-pager when one fails.

**What an individual can't do without IT:** upload a packaged agent (custom-app upload is normally admin-only), share
an agent org-wide, or deploy the plugin to others.

## Components by surface

### A. Content (used by every surface)

| Component | Purpose | Individual | Team | Status |
|---|---|---|---|---|
| Playbook master (`.md`) per document type | The source of truth; Cowork reads it | OneDrive tree | Library | Built (NDA, legal hold) |
| **Word playbook** (four columns, `.docx`) | Attach to *Review contract*; **contracts only** | OneDrive tree | Library | Built |
| **Agent knowledge copy** (table-free `.docx`) | Agents drop tables, so they need this copy | OneDrive tree | Library | Built for the spike (`(text).docx`); needs to move into the build |
| Samples with expected results | Trial run after setup | In the zip | Library `Grounding/Samples/` | Built (NDA, legal hold) |
| Quick cards | Which surface and steps for each task | In the zip | Library | v1.1; needs an agent update |
| START HERE and the IT one-pager | Install guide; what to ask IT for | In the zip | Admin pack | Draft (X12) |

### B. Word desktop (Copilot pane)

| Component | Does | Individual | Team | Status |
|---|---|---|---|---|
| Microsoft *Review contract* + our Word playbook | **The only in-place redline**: rated review, then *Apply edits with comments* as tracked changes and comments in the open document. Contracts only | Attach from OneDrive (nothing to install) | Attach from the library | Tested (Acts 2, 7) |
| **Specialist agents** (`@Spaarke NDA Review`, `@Spaarke Legal Hold Review`, one per document type) | Rated review chart in chat, any document type, agent mode only. **Can't edit the open document**, and takes over the chat, so the Legal skills aren't available alongside it | Agent Builder (§D) | Packaged or shared (§D) | Review tested; whether the "redlined copy" works in Word is untested |
| Custom-instruction line + review protocol | Fallback for non-contracts with no agent (agent mode reads custom instructions) | Paste into Personalization | Same; each user pastes it | Tested (Act 7, 13/13 counts) |

### C. Cowork desktop

| Component | Does | Individual | Team | Status |
|---|---|---|---|---|
| **Spaarke plugin** (skills `setup`, `help`, a review skill per document type, `library-review`, plus engines) | Automatic routing; plan card; engine-built redlines (internal and external), clean copy, dashboard, audit pack; library review across a folder | Upload the zip and publish to **Only you** (no admin), or copy skill folders into OneDrive | Admin deploys to groups (admin center › Agents), or the library owner shares with named users | NDA built and tested (Acts 3–6). Legal hold skill not built. Admin deploy (T-14) untested |
| **Specialist agents** (the same ones as in Word) | Review plus a redlined copy with comments (2026-11-10), in one chat | Agent Builder (§D) | Packaged or shared (§D) | Tested once (NDA, Copilot Studio agent) |
| Attorney profile + custom-instruction block | Defaults and output preferences | Written by setup | Setup in "team member" mode | Tested (T-05) |
| Microsoft Legal for Cowork skills | Memo, redline summary, key-terms grid, folder review; they use our playbooks and policy | Microsoft's | Microsoft's | Tested (Acts 4, 6) |

### D. Agents: one component that works in three places (Word agent mode, Cowork, the Copilot app)

| Route | Path | How | Updates | Limits | Status |
|---|---|---|---|---|---|
| **Agent Builder recipe** | Individual | The attorney creates the agent: name, instructions (paste), suggested prompts, knowledge = the agent copy in their OneDrive | Playbook changes flow through the knowledge file; instruction changes need a re-paste | Owner's builder shows no skills option (Microsoft says skills are GA); ≤ 20 uploaded or 50 OneDrive files; private unless shared | Works (owner built three) |
| **Shared Agent Builder agent** | Team, little IT | The library owner builds it once, with knowledge pointing at the library files, and shares it with the team group (Can chat) | As above, done once by the owner | Depends on the tenant's sharing policy; recipients need the licence | Untested |
| **Packaged agent** (manifest v1.9; the playbook travels as an agent skill) | Team, with IT | Admin uploads the zip (Teams admin center or M365 admin center › Agents) and assigns users or groups; per-customer build from a template | New package version per change | Playbook in the package is a static copy (repackage on change), unless it uses library knowledge instead | Built (`Build-SpikePackages.ps1`); not yet installed |

## Where agents are used (confirmed by tests so far)

| Place | Use the agent for | Don't use it for | Instead |
|---|---|---|---|
| **Word desktop** | The rated review of any document type, the same way every time | Redlining: it can't edit the open document and blocks Microsoft's Legal skills in that chat | **Contracts:** new chat, *Review contract* with the Word playbook. **Non-contracts:** the agent's redlined copy if it works in Word (to test), or Cowork |
| **Cowork** | Review plus a redlined copy, in one step (no plugin needed) | Library-wide review; engine-checked outputs | The Spaarke plugin skills (same edition, both paths) |
| **Copilot app** (chat) | Review of an attached document | — | — (untested) |

So the agent is the one reviewer that works on every surface. Plugin skills remain Cowork's power features, and
Microsoft's *Review contract* remains the only in-place redline in Word.

## Mechanics still to confirm before this list is final
1. Packaged agent: install by admin and by custom-app upload; does the skill-based agent run in Word and Cowork (spike S1, B-1 to B-3).
2. Agent v0.2 instructions (automatic redlined copy, coloured summary): what Word does with them, and Cowork again.
3. Agent Builder: sharing to a team group, and whether shared agents appear in Word's `@` list and in Cowork.
4. Agent Builder skills option: missing in the owner's builder although announced GA. Re-check; until then the
   individual path uses knowledge files.
5. Cowork plugin: admin deployment (T-14), and the individual route when organization-built plugins are blocked
   (fallback: skill folders).

Out of scope here: the Spaarke AI agent (BFF actions; the Spaarke connection project) and an Office add-in.
