# Spaarke AI Accelerator for Microsoft Copilot: components by tier and surface (DRAFT v2, 2026-11-10)

Status: proposal for owner decision. Agents move to the *advanced* tiers, so the base package is files only. That
keeps the standing directive "two editions (Word vs Cowork)". Adding agents at the advanced tiers still needs the
directive "no declarative agents in the package" revoked (spike DA-1). Mechanics first; quality and scoring later.
Basis: `notes/poc-e2e/synopsis-word-and-cowork.md`, `results.md`, design §2.4, §5.2–5.4 and §8.1a, and researcher
note `declarative-agents-in-word-2026-11-09`.

## The four tiers

| | **Base** | **Advanced** |
|---|---|---|
| **Individual** | Files only. Extract one zip into OneDrive `Documents/`; paste one custom instruction. Runs on Microsoft's Copilot runtime, the Legal plugin and our Cowork skills | Adds **Agent Builder** agents the user builds from our recipe; their knowledge is the base files in the user's OneDrive |
| **Team** | **No IT.** The library owner keeps the files in a SharePoint library and shares the Cowork plugin and, optionally, an Agent Builder agent with named people | **IT.** The admin deploys the plugin and packaged agents to groups, with central versions |

**Prerequisites at every tier** (admin settings, not install steps): M365 Copilot licence, Microsoft's Legal plugin
on, Cowork on with credits. Base needs organization-built plugins allowed only if the plugin route is used (skill
folders don't need it). Advanced tiers also need Agent Builder, and sharing, not blocked. The setup checks these and
gives the user an IT one-pager when one fails.

## Fixed locations (individual)

The zip is laid out like `Documents/`, so "extract into Documents" puts everything in place:

| Folder | Holds | Why this folder |
|---|---|---|
| `Documents/Cowork/Skills/<skill>/` | Our Cowork skills, each with its scripts | **Cowork loads skills from here automatically** (`[Tested]` T-01/T-02) |
| `Documents/Legal Knowledge/<document type>/` | Playbook master (`.md`), Word playbook (four columns, contracts), readable playbook (table-free `.docx`), samples | One tree for both surfaces, the same layout as the team library (design §5.9) |
| `Documents/Legal Knowledge/Review method/` | `Spaarke Review Method.docx` (the method for non-contract reviews in Word) | The custom instruction names it |
| `Documents/Legal Knowledge/Start here/` | Quick cards, the custom-instruction text, the agent recipes (advanced) | — |

**Microsoft's own folders (checked 2026-11-10; detail in `poc-e2e/onedrive-layout.md`).** `Documents/Cowork/` is
Cowork's workspace. Microsoft documents only one thing it reads automatically: `Cowork/skills/<name>/SKILL.md`, at
the start of every conversation. Cowork writes `Tasks/`, `Projects/` and an `AGENTS.md` (it calls the folder its
durable user folder) itself. `Documents/Copilot/` is Copilot's own area: `Created/` holds files Copilot makes, and
`Memory/v2/` is an undocumented memory store. Neither is documented as a place Copilot reads instructions from, so
we put nothing there. Our files go in `Cowork/Skills/` (the one auto-loaded location) and `Legal Knowledge/`.

**Word doesn't load files from a folder.** Copilot in Word uses:
- the open document;
- files the user attaches or references with `/`;
- what its search finds across the user's OneDrive and SharePoint (and other content the user can access);
- custom instructions (agent mode only).

Nothing scopes Word Copilot to a folder. An instruction *inside* a file can't help it find that file. The custom
instruction is the only text Word always loads, and in Act 7 it found the named file **by its name, not by the path
given**. Stray copies elsewhere were also cited (gap #34). So Word steps say **attach or `/`-reference** the files,
and our file names must be unique. Whether a full file link in the custom instruction makes Word open that exact
file is test G-2.

## Components

### Individual, base (files only)

| Surface | Component | How the user uses it | Status |
|---|---|---|---|
| Word | **Word playbook** (four columns), one per contract type | "Review this NDA against the attached playbook" (attach), then *Apply edits with comments*. **In-place redline**; contracts only | Tested (Acts 2, 7) |
| Word | **Review method + readable playbook** for non-contract types | Agent mode; attach the playbook; the custom instruction brings in the method. Rated review in chat | Review tested (Act 7, 13/13 counts); in-place redline after it untested (H-1) |
| Word | **Custom instruction** (one block, pasted once) | Names the method file, says to attach the playbook, marks results as drafts | Tested in Word agent mode; whether one setting also covers Cowork is P-1 |
| Cowork | **Skill folders**: `setup`, `help`, a review skill per document type, `library-review`, plus the engines | Plain request ("review this NDA") or `/skill-name`; plan card; redlines (internal and external), clean copy, dashboard, audit pack | NDA tested (Acts 3–6). Legal hold skill not built. Reading from `Documents/Legal Knowledge/` untested (C-2) |
| Cowork | **Attorney profile** | Written by `setup` after one approval | Tested (T-05) |
| Both | **Quick cards** | One per surface and task | v1.1; needs the tiers |

### Individual, advanced (adds Agent Builder)

| Component | How | What it adds | Status |
|---|---|---|---|
| **Agent recipe** per document type (name, instructions text, suggested prompts, which knowledge files to pick) | The user builds the agent in Agent Builder. Knowledge = the readable playbook in their own `Documents/Legal Knowledge/` (OneDrive files, not uploads) | Same `@`-name and method in Word (agent mode), Cowork and the Copilot app; no custom instruction needed; in Cowork, review **plus** a redlined copy in one step (instructions v0.2) | Owner built three (Copilot Studio); OneDrive-file knowledge and v0.2 untested (A-1, A-2) |

The base stays in use: in Word the agent can't edit the open document and blocks the Legal skills in its chat, so
contracts still go through *Review contract* for an in-place redline.

### Team, base (no IT)

| Who | Component | How | Status |
|---|---|---|---|
| Library owner | **SharePoint `Legal Knowledge/` library** | Seeds it with the same tree; read-only for attorneys (site-owner permission, no admin) | Library tested; read-only for a non-owner untested (T-1) |
| Library owner | **Cowork plugin**, shared | Uploads the plugin and publishes it to named people | Upload tested ("Only you"); sharing with a second user untested (T-2) |
| Members | Word: attach from the library. Cowork: the shared plugin (or skill folders), with `setup` in team-member mode pointing at the library | — | Word attach tested by the owner; team-member mode untested (T-4) |
| Members | Custom instruction (team version naming the library files) | Paste once | — |
| Library owner, optional | **Shared Agent Builder agent** per document type | Built once with knowledge from the library; shared with the team group (Can chat) | Untested (T-3) |

### Team, advanced (IT)

| Component | How | Status |
|---|---|---|
| **Admin-deployed plugin** | Admin center › Agents, assigned to users or groups | Untested (T-14 / I-1) |
| **Packaged agents** (manifest v1.9; playbook as an agent skill, or library knowledge) | Per-customer build from a template; admin uploads and assigns; shown under *Built by your org* | Built (`Build-SpikePackages.ps1`); not installed (I-2, I-3) |
| Library governance | Permission groups, versions, retention | Design §5.3 |
| Later | Spaarke connection (MCP to the BFF) | Next project, not R1 |

## Build work before the tests (not tests)
- A legal hold Cowork skill (only NDA exists).
- `Spaarke Review Method.docx` from the owner's protocol, renamed uniquely.
- The readable (table-free) playbook rendered by the build, not the scratchpad converter.
- The zip laid out like `Documents/`.
- Agent recipes from the v0.2 instruction files.

Built 2026-11-10 by `src/accelerators/build/Build-Package.ps1` (see the commit). Owner script for the individual
tiers: `poc-e2e/tier-test-script.md`.

## Tests still needed (mechanics only)

**Word grounding and modes**
- **G-1** Custom instruction names the method file by name. Put a decoy copy elsewhere in OneDrive: which one does Word use, across 3 new chats?
- **G-2** Same, with the file's full OneDrive link in the custom instruction: does Word open that exact file?
- **G-3** `/`-reference the method and playbook in the prompt instead of attaching: reliable, no search?
- **G-4** `.md` vs `.docx` for the method file when attached or referenced.
- **H-1** Non-contract, base route: after the rated review, "Apply the recommended changes as tracked changes with comments". In the open document?
- **P-1** Edit the custom instruction once: does Cowork pick up the change too?

**Cowork base**
- **C-1** Fresh install by extracting the zip: skill folders show in `/` without a plugin upload, and the scripts run.
- **C-2** Skills read playbooks from `Documents/Legal Knowledge/` (the new tree; the old one was `Documents/Cowork/Playbooks/`).
- **C-3** Legal hold through the Cowork skill (after it's built): review plus redlined copy.

**Individual advanced**
- **A-1** Agent Builder agent with knowledge picked from OneDrive files (not uploaded): does it read the whole playbook?
- **A-2** Instructions v0.2: in Cowork, automatic redline with the coloured summary; in Word, what happens.
- **A-3** Change the playbook file in OneDrive: does the agent use the new version without being rebuilt?

**Team base (needs a second, non-owner test user)**
- **T-1** Read-only library: the member can attach in Word and Cowork can read, but neither can edit.
- **T-2** Plugin shared to the member: it appears and runs, and reads the library.
- **T-3** Agent Builder agent shared to a group: it appears in the member's Word `@` list and in Cowork, and its library knowledge works under the member's permissions.
- **T-4** `setup` in team-member mode.

**Team advanced (needs admin)**
- **I-1** Admin-deployed plugin (T-14): where it appears, and for whom.
- **I-2** Packaged agents (spike S1, B-1 to B-3, R-1): install via admin upload; do the skills run in Word and Cowork?
- **I-3** Packaged agent with library knowledge instead of an embedded skill: does a playbook update reach users?

**Preflight (optional, needs admin toggles):** what a user sees when the Legal plugin, Cowork, Agent Builder or
organization-built plugins are off.

Out of scope here: the Spaarke AI agent (BFF actions; the Spaarke connection project) and an Office add-in.
