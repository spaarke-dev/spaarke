# Solution packs, components, Python and visuals (v0.1 draft, 2026-11-05)

Answers five owner questions from 2026-11-05. Design v0.5 is the baseline; this note proposes what goes into v0.6 (§6 lists the edits).

1. A plain component list by surface (Word, Cowork).
2. Solution packs with mini-specs that avoid "race" problems in Copilot's skill selection.
3. Componentizing primitives so they can be stacked and reused.
4. What Python actually does, with examples and limits.
5. Visuals in Cowork.

Tags: `[Tested]` seen in our tenant · `[Cited]` Microsoft documentation · `[Proposed]` not built or decided.

---

## 1. Components by surface (R1)

### 1.1 Word: Copilot in Word with Microsoft's Legal plugin

**In Word, Spaarke ships one thing: the Word playbook.** Everything else in Word is Microsoft's.

| # | Primitive | Owner | What it does in our solution | Status |
|---|---|---|---|---|
| W1 | Copilot pane in **Word for Windows desktop** | Microsoft | The host | `[Tested]` S5 |
| W2 | Legal plugin in Word (skills: Legal reasoning, Edit contract, Draft new contract) | Microsoft | Imports a playbook, runs the review, writes native tracked changes in the open document | `[Tested]` |
| W3 | Playbook import → topics/rules editor → **Run Review** | Microsoft | Turns our `.docx` into topics the attorney can edit and run | `[Tested]` L24 |
| W4 | **Saved playbook** object | Microsoft (owned by the user) | Keeps the imported topics for reuse. The import is lossy (drops Topic ID and fallback) and the saved copy can drift | `[Tested]` L24; reuse is T-P4 |
| W5 | Cloud Policy "Legal Skills in M365 Copilot in Word" | Customer admin | On by default | `[Cited]` |
| **W6** | **Spaarke Word playbook `.docx`** (one per agreement type and side) | **Spaarke** (rendered from the master) | Our positions, in the shape W3 imports: Heading 1/2, `Topic ID:`, `Priority:`, `Review question:`, and General guidelines with MUST/MUST NOT | Built (`render_word_playbook.py`); A/B import retest pending |
| W7 | Playbook location: SharePoint `Legal Playbooks/<practice>/` (governed, read-only) or OneDrive (personal) | Customer | One place to "Attach from Cloud" | T-P10 open |
| W8 | Onboarding card: "type `/` and the playbook name" plus the version line | Spaarke (guide text) | Gets the right playbook attached; helps spot a stale saved copy | `[Tested]` L19 |

**Not used in Word, and why:**

| Not used | Why |
|---|---|
| Our plugin and skills | Listed in Word's menu but never run (S17, L21, L25). A hazard to manage. |
| `references/`, `scripts/`, `assets/` | Word doesn't load them; "Python execution isn't available here" (L21) |
| Custom instructions, Projects | Word has none |
| Declarative agent | Out of R1 (owner, 2026-11-02) |
| MCP connector | v2 (§10); the route Harvey uses (L26) |
| Office add-in | Out of scope |

### 1.2 Cowork

| # | Primitive | Owner | What it does in our solution | Status |
|---|---|---|---|---|
| C1 | Skill router (matches the request against skill descriptions) and `/skill-name` | Microsoft | Picks our entry skill, or someone else's | `[Tested]` S9, T-09 |
| C2 | Python sandbox | Microsoft | Runs our scripts (§4) | `[Tested]` T-02, T-03, T-05 |
| C3 | Question cards | Microsoft | Intake: one card with only the missing defaults | `[Tested]` |
| C4 | Built-in skills we hand off to: Email, Adaptive Cards, Word, Excel, PDF | Microsoft | Cover email, the plan-preview card, reading PDFs | Email `[Tested]`; card `[Proposed]` |
| C5 | Approvals (send, write outside the task folder) | Microsoft | The attorney approves email and profile writes | `[Tested]` L10 |
| C6 | Cowork folders in OneDrive: `Tasks/<task>/output/`, `Profile/`, `Playbooks/`, `Skills/` | Microsoft (location) | Where outputs, the profile and personal playbooks live | `[Tested]` S3, T-07 |
| C7 | Custom instructions (Preferences) and Projects | Microsoft | Complement to the profile: always loaded, but no skill can write it | `[Cited]` |
| C8 | Automations | Microsoft | Unattended runs (no question card; profile defaults) | Designed, not tested |
| C9 | Legal for Cowork (20 skills) | Microsoft | A neighbour, and our main routing competitor | `[Tested]` T-09 |
| **C10** | **Plugin `.zip`** (unified manifest 1.30) | **Spaarke** | Distribution only | `[Tested]` |
| **C11** | **Entry skills:** `nda-review`, `accelerator-setup` (later `accelerator-help`) | **Spaarke** | The only things the router sees. Each holds one procedure. | `[Tested]` |
| **C12** | **`references/`:** guardrails, standards protocol, playbook format, edit-plan schema, topics, review checklist, baseline positions, comment library | **Spaarke** | Read by the skill when its body says so | `[Tested]` |
| **C13** | **`scripts/`:** probe, playbook_parse, segment_clauses, safe_docx, ooxml_redline, summary_table, check_outputs | **Spaarke** | The deterministic work (§4) | `[Tested]`, 41 tests |
| **C14** | **`assets/`:** summary intro, email cover, sample NDA | **Spaarke** | Templates filled at run time | `[Tested]` |
| **C15** | **Legal playbook (Markdown):** governed, personal or baseline | Customer or Spaarke | The positions | `[Tested]` T-07 (personal) |
| **C16** | **Attorney profile** `Profile/legal-profile.md` | Attorney (written by setup) | Defaults: side, posture, playbook folder, name, email behaviour | `[Tested]` S3 |
| **C17** | **Custom-instructions block** | Spaarke text, pasted by the attorney | Always-on reminders | Designed |
| **C18** | **Visuals:** plan-preview card, results card, Excel audit pack | **Spaarke** | §5 | `[Proposed]` |

**Not used in Cowork in R1:** a declarative agent; an MCP connector and MCP Apps (v2, §5.3); SharePoint site skills (they don't run in Cowork).

### 1.3 Shared by both surfaces

Only the **content** is shared, never the runtime:
- the topic catalogue (`topics.yaml`);
- the legal playbook master, rendered to Markdown (Cowork) and `.docx` (Word);
- the eval corpus.

---

## 2. Solution packs: one capability, one mini-spec

### 2.1 What we can't control, and what we can

| We can't control | We can control |
|---|---|
| Which skill Copilot picks (description matching, C1) | How many entry skills we ship, and their descriptions |
| Whether it picks ours or Microsoft's or a third party's | The words that route, and the neighbours each description names |
| Ordering *between* skills (there is no call contract, §5.2) | Ordering *inside* one skill: the procedure steps |
| Whether the model reads a reference or runs a script | Making a skipped step fail loudly (artifact chaining, blocking checks) |
| Which built-in it hands off to | Keeping hand-offs optional, with a fallback |
| The user's plugin toggles | A recommended toggle set per role, and `/` as the explicit route |

### 2.2 Design rules that remove the race

1. **One job, one entry skill.** No two Spaarke skills may match the same request. A pack has one entry skill per user job (for NDA: review; setup lives in Core).
2. **Sequencing lives inside a skill, never between skills.** We never design a flow that needs Copilot to run skill A and then skill B. Reuse happens at build time (§3), not by one skill calling another.
3. **Artifact chaining.** Each step writes a file the next step needs: `clauses.json` → `edit-plan.json` → dry run → `redline.docx` → `check_outputs`. A skipped step makes the next one fail with a named error, instead of silently producing something worse.
4. **A blocking verifier at the end.** `check_outputs` passes or the run doesn't deliver. Whatever path the model took, the deliverable meets the same 10 checks.
5. **Hand-offs to built-ins are cosmetic or approved.** Email (needs approval) and cards (fallback: a chat table) only. Never a load-bearing step.
6. **A user-facing skill exists in exactly one plugin.** Shared *user-facing* skills (setup, help) move to a Core plugin when a second pack ships. Shared *internal* parts (engines, references) are copied into each pack's skills, which the router never sees. Today the NDA plugin bundles `accelerator-setup`; with two packs installed that would put two `accelerator-setup` skills in front of the router (behaviour undocumented).
7. **Disjoint by object and verb.** Packs split by agreement type (NDA vs MSA vs lease) and by verb (review vs draft vs triage vs summarize). Microsoft owns the verbs and objects we don't ship (protocol rule 1).
8. **Named neighbours in every description**, gated: "Do NOT use for MSAs (use review-contract)".
9. **Visible provenance.** Every Spaarke output starts with the provenance line. If it's missing, the user knows another skill ran.
10. **Measured, not assumed.** Each pack has a routing eval set (prompts → expected skill), run in the tenant with the recommended toggles and with Legal for Cowork ON (T-P2).

### 2.3 What a solution pack is

A **solution pack** is a customer-facing capability (e.g., "NDA Review"), delivered across surfaces, defined by a mini-spec and built from catalogue components (§3). A pack ships as one plugin plus its Word playbooks. Packs depend on the **Core pack**.

| Pack | Release | Entry skills (Cowork) | Word deliverable | Status |
|---|---|---|---|---|
| **Core** | R1 | `accelerator-setup`, `accelerator-help` | — (guides only) | setup built; help not built |
| **NDA Review** | R1 | `nda-review` | NDA Word playbooks (mutual, disclosing, receiving) | MVP built |
| Legal Intake | R2 (OD-21) | `legal-intake` | — | Designed |
| Invoice Review | R2 (OD-21) | `invoice-review` | — | Designed |
| Others (§8.3) | R2+ | one each | Word playbook where review applies | Roadmap |

### 2.4 Mini-spec template (one per pack, `practices/<pack>/PACK.md`)

| Section | Content |
|---|---|
| 1. Capability | 2–3 lines: what the user gets |
| 2. Jobs × surfaces | Each user job → surface → the primitive that handles it (Microsoft or Spaarke), from §1 |
| 3. Routing contract | Entry skill per job; trigger phrases; NOT-for list with each neighbour's owner; explicit `/` command; assumed toggles |
| 4. Components | Catalogue IDs and versions (§3); the build writes the bill of materials |
| 5. Inputs | Documents, playbook (which kinds, which locations), profile fields |
| 6. Flow | The steps inside the entry skill, each marked **model judgment**, **script** or **hand-off (fallback)**, with the artifact each step writes |
| 7. Outputs | Files, names, visuals, chat summary; the checks that must pass |
| 8. Guardrails and approvals | What needs approval; what is never done |
| 9. Tests | Engine tests, gates, eval cases, routing evals, tenant tests |
| 10. Not in this pack | Explicit negative scope, with who owns it instead |
| 11. Open items | Spikes and owner decisions |
| 12. Sources | Per-pack source sweep at pack start: GitHub, Hugging Face, standards bodies and government sources, each with its licence checked at source. It builds on the shared baseline (design §8.5; the 2026-10-31 and 2026-11-05 researcher notes). Excluded sources are listed with the reason, so nobody re-adds them. |

### 2.5 NDA Review pack: mini-spec (first fill)

1. **Capability.** Review an NDA against the customer's playbook, or the Spaarke baseline, and deliver an internal redline with a linked page-1 summary and one comment per change, an external redline and a clean copy, all checked by script. In Word: the same positions, as a Word playbook for Microsoft's Legal plugin.
2. **Jobs × surfaces.**

   | Job | Surface | Handled by |
   |---|---|---|
   | Review one NDA while reading it | Word | Microsoft Legal (W2) + Spaarke Word playbook (W6) |
   | Packaged redline set | Cowork | `nda-review` (C11) |
   | Batch triage of NDAs | Cowork | `nda-review` batch mode `[Proposed]`, unless a Legal for Cowork skill already covers it (inventory first, T-P1) |
   | Summarize the other side's redline | Cowork | Microsoft `summarize-redlines` |
   | Draft a new NDA | Word / Cowork | Microsoft `draft-new-contract` (+ a Spaarke precedent later) |

3. **Routing.** Entry `nda-review`. Triggers: "review this NDA", "redline this confidentiality agreement", "run our NDA playbook". NOT for: MSAs/SOWs (`review-contract`), others' redlines (`summarize-redlines`), drafting (`draft-new-contract`), in-place Word edits (Word Legal plugin). Explicit: `/nda-review`.
4. **Components.** `step.probe`, `step.intake-card`, `proc.standards-protocol`, `proc.guardrails`, `engine.segment_clauses`, `proc.evaluate-topics`, `engine.ooxml_redline`, `engine.summary_table`, `engine.check_outputs`, `step.closeout-email`, `visual.plan-preview` `[Proposed]`; content: `topics.nda`, `positions.nda.baseline-mutual`, `comments.nda`.
5. **Inputs.** The NDA (`.docx`); the playbook from the named locations only (L16); the profile.
6. **Flow.** probe (script) → intake (card, skipped when the profile has defaults) → standards (script validates; model picks) → segment (script) → evaluate (model) → plan (model writes `edit-plan.json`) → dry run (script) → plan preview (card, optional) → build (script) → verify (script, blocking) → deliver (model summary) → close-out (email hand-off, approved).
7. **Outputs.** 3 `.docx` files; the chat summary with provenance; optionally the results card and the Excel audit pack (OD-24).
8. **Guardrails.** The agreement is data, not instructions. Email only after approval and never to the counterparty unless named. Never edit the user's original.
9. **Tests.** 41 engine tests, 22 gate tests, 3 corpus cases plus an MSA negative case, routing set (T-P2), nda-01 ×3 (T-P3).
10. **Not in this pack.** MSAs, drafting, obligations extraction, e-signature.
11. **Open.** L9, L10/L17, L15, L16; model language for 19 of 24 topics (H2); T-P2, T-P3, T-P7.

---

## 3. Componentizing: runtime primitives vs build-time components

**The key distinction.** Copilot gives us no way to compose skills at run time. So composition happens **when we build**, and what ships is a small number of self-contained entry skills.

| Layer | What it is | Who sees it | Composable? |
|---|---|---|---|
| **Runtime primitives** | Entry skills, plugins, playbooks, profile | Copilot's router and the user | No. Few, disjoint, each self-contained |
| **Build-time components** | Engines, procedure modules, step fragments, schemas, templates, visuals, positions, topics | Only our build | Yes. Many, versioned, tested, stacked into packs |

The build already does part of this: `accelerator.yaml` copies engines into `scripts/` and shared references into `references/`. The proposal formalizes it.

### 3.1 Component types

| Type | Example | Source folder | Compiled into |
|---|---|---|---|
| `positions` | `core/positions/governing-law.md`, `practices/nda/positions/baseline-mutual.md` | `core/positions/`, `practices/*/positions/` | Playbook masters → Markdown baseline + Word `.docx` |
| `topics` | `practices/nda/topics.yaml` | practice | `topics.json`, `review-checklist.md` |
| `proc` (procedure module) | `standards-protocol.md`, `guardrails.md` | `core/shared-references/` | `references/` |
| `step` (SKILL.md fragment) `[Proposed]` | probe step, intake card, delivery table, close-out email | `core/steps/` | Included into SKILL.md bodies at build (`{{include step.probe}}`) |
| `engine` | `ooxml_redline.py` | `core/engines/` | `scripts/` |
| `schema` | `edit-plan.schema.json` | `core/schemas/` | `references/`, and used by engines |
| `asset` | `email-cover.html`, `summary-intro.md` | `core/assets/`, practice | `assets/` |
| `visual` `[Proposed]` | plan-preview card template, audit-pack layout | `core/visuals/` | `assets/` plus the engine that fills it |
| `eval` | corpus `.docx`, expected plan | `practices/*/evals/` | Tests (never shipped) |
| `gate` | `description-plain`, `cli-drift` | `build/gates.py` | The build |

### 3.2 Component manifest `[Proposed]`

Each component gets a small header, in `component.yaml` or front matter:

```yaml
id: engine.ooxml_redline
version: 1.3.0
provides: [redline.ooxml]
requires: [engine.safe_docx, python:lxml]
surfaces: [cowork]          # cowork | word | both
owner: spaarke-engineering
tests: core/engines/tests/test_engines.py
```

The pack manifest (`accelerator.yaml` → `pack.yaml`) lists components by `id@version`. The build then:
1. **resolves dependencies** (today `safe_docx` is listed by hand);
2. **checks surfaces**: nothing with `surfaces: [cowork]` goes into a Word deliverable;
3. **includes step fragments** into SKILL.md bodies;
4. **writes a bill of materials** (`references/bom.json`): what went into this build, for provenance and support;
5. **runs the overlap gates** (§3.4).

### 3.3 Stacking example

A **contract-review stack** is the reusable middle of every review pack:

```
step.probe → step.intake-card → proc.standards-protocol → engine.playbook_parse
→ engine.segment_clauses → proc.evaluate-topics → (edit plan) → engine.ooxml_redline --dry-run
→ visual.plan-preview → engine.ooxml_redline → engine.summary_table
→ engine.check_outputs → step.deliver → step.closeout-email
```

- **NDA Review** = the stack + `topics.nda` + `positions.nda.*` + `comments.nda`.
- A future **lease** or **MSA review pack** (if Microsoft's `review-contract` leaves a gap, protocol rule 1) = the same stack + its own topics, positions and comment library. No engine changes.
- **Invoice Review** (R2) reuses `step.probe`, `step.intake-card`, the check pattern, `visual.*` and a future `engine.xlsx_writer`, with its own `engine.ledes_parse`.

### 3.4 Rules and gates

| Rule | Enforced by |
|---|---|
| Components never route: only entry skills have front matter | Gate: SKILL.md only in `skills/<entry>/` |
| A user-facing skill exists in one plugin across all packs | Gate across every `pack.yaml` (new) |
| No trigger phrase appears in two entry skills | Gate across packs (new); routing evals in the tenant |
| Every description names its neighbours | Existing gate |
| Reuse by copy, pinned by version; a skill never reads another skill's folder | Gate: script paths resolve inside the skill |
| A position appears only in `positions` components | Existing T-P5 rule, gated |
| Every engine has tests; every command in SKILL.md matches the engine's CLI | Existing tests and `cli-drift` |

---

## 4. What Python actually does

### 4.1 The division of labour

- **The model** reads the contract, applies judgment, decides what to change, and writes it down as a plan.
- **Python** does the exact, mechanical work the model can't be trusted to do: Word's XML, counting, comparing and checking.

Think of the model as the associate deciding the markup, and the scripts as the document-production software plus the proofreading checklist.

How it runs:
1. The skill says, for example, `python scripts/segment_clauses.py original.docx > clauses.json`.
2. Cowork runs it in a Linux sandbox, against the files in the task's working folder.
3. The script reads and writes files and prints a short JSON result.
4. The model reads only that result (the script's code is never loaded into its context, which saves credits), then carries on.

### 4.2 What our NDA scripts do, step by step

| Script | What it does | Example output |
|---|---|---|
| `probe.py` | Checks the sandbox: Python version, `lxml`, `soffice`, a writable folder. Stops the run early if something changed. | `{"ok": true, "python": "3.12.15", "lxml": "6.1.3", ...}` |
| `playbook_parse.py` | Reads the playbook Markdown, checks every topic against the catalogue, reports missing or unknown topics and the side | "24 topics; 2 silent → baseline; side: disclosing" |
| `segment_clauses.py` | Opens the `.docx` (a zip of XML) and lists every paragraph with a stable key, number and heading. Flags existing tracked changes and **hidden text** (prompt-injection defence, corpus case nda-03). | `clauses.json` with `para_key`, `has_tracked_changes`, `hidden_text` |
| `ooxml_redline.py` | Takes the model's `edit-plan.json` and writes **real Word tracked changes** (author, date), comments anchored to exact words, and bookmarks. Produces internal, external and clean copies. `--dry-run` tests every edit first; exit 3 names the edit that doesn't fit. | Three `.docx` files |
| `summary_table.py` | Puts the page-1 summary table in the internal redline, each row linked to its bookmark | "Summary: 9 rows, all linked" |
| `check_outputs.py` | 10 blocking checks: plan matches the contract; all files open; every change tracked; existing revisions kept; revision text right; one comment per change; summary present and linked; clean copy = redline with everything accepted; external has no comments; no leftover placeholders | `{"ok": true, "checks": [...]}` |
| `soffice` + `pdftoppm` | Render page 1 to an image so the model can look at it | `page1.png` |

### 4.3 What Python can do for legal work (beyond NDA)

| Capability | Legal example | Needs |
|---|---|---|
| Edit Office files at XML level | Tracked changes, comments, bookmarks, styles, numbering in `.docx` | `lxml` `[Tested]` |
| Build spreadsheets | Excel audit pack (change log, risk summary, review checklist, coverage); obligations register; invoice exception report with formulas | `openpyxl` (unverified) or raw XML with stdlib (always works) |
| Convert and render | `.docx` → PDF → page images, for a visual check or a card thumbnail | `soffice`, `pdftoppm` `[Tested]` |
| Extract and check text | Dates, amounts, parties, defined terms; "defined but never used"; broken cross-references; numbering gaps | stdlib (`re`) |
| Compare versions | What the counterparty changed against our last draft, including untracked changes they "forgot" to mark | `lxml` |
| Calculate | Notice periods and renewal deadlines; LEDES invoice rate and cap checks | stdlib |
| Validate the model's output | JSON-schema checks on the plan; counts by risk; topic coverage | stdlib |
| Make charts | Risk-by-topic bar chart as SVG (stdlib) or PNG (`matplotlib`, unverified), embedded in Word, Excel or PowerPoint | stdlib for SVG |
| Compute visual data | Exact numbers for a card (counts, High items), so the card never shows a number the model guessed | stdlib |

### 4.4 What Python can't do

| Limit | Consequence for design |
|---|---|
| **No network** (our rule; Microsoft doesn't document egress) | Scripts can't call Graph, SharePoint, Dataverse, the Spaarke BFF or the web. The model fetches; scripts only process files. |
| **No LLM calls, no secrets** (our rule) | All judgment stays with the model |
| **Only sees files in the working folder** | The model brings files in (attachments, copies) and Cowork saves outputs to the task folder |
| **Can't talk to the user or draw UI** | It prints JSON; the model presents it, or asks a built-in to draw a card (§5) |
| **No memory between runs**; the workspace can even reset mid-session | Every step is restartable from files; nothing relies on an earlier session |
| **Doesn't run in Word** (L21) or Copilot Chat | Word gets content only (§1.1) |
| **Package set is undocumented**; installing packages unobserved | `probe.py` first; stdlib fallbacks; re-check after Ignite (T-P12) |
| **Time, memory and size limits undocumented** | Keep scripts to seconds and inputs small |
| **No legal judgment** | Python decides *how* to write a change exactly, never *what* to change |
| **Not guaranteed to be called** | Artifact chaining and the blocking `check_outputs` (§2.2) make a skipped script visible |

---

## 5. Visuals in Cowork

Source: researcher note `cowork-visual-outputs-2026-11-05.md`, which checked Learn on 2026-11-05 (no Cowork visual page changed after 2026-11-03), plus the expert demo.

### 5.1 What Microsoft documents

| Route | What is documented | What isn't | Status |
|---|---|---|---|
| **File preview pane** | Renders PDF inline; Word, Excel and PowerPoint as online previews; CSV as a table; Markdown "with full formatting"; images inline, **including SVG**; and **"HTML: Renders in the preview pane."** Output-folder files get Preview and Download buttons. Microsoft says "Cowork can already create interactive HTML files as part of a task", with an interactive HTML dashboard as its example. | Whether the HTML preview runs JavaScript, and its CSP. Whether a PNG appears inline in the chat itself. | `[Cited]` |
| **Adaptive Cards built-in** | "Interactive card-based responses with structured layouts, buttons, and data displays in the conversation" | How a skill supplies a card (template in `assets/`? `${}` binding?), the schema version, which elements render (charts, Table, Badge), whether a button click comes back as a user turn, size limits. No Microsoft sample shows a skill producing one. | Exists `[Cited]`. Renders from a skill in the April demo (Frontier). Contract unknown. |
| **Markdown in chat** | Microsoft's own sample skill asks for a Markdown table of clause risks | Mermaid and LaTeX: not mentioned anywhere | Partly `[Cited]` |
| **App built-in** | Frontier preview, "might not be released for GA". Builds a Microsoft-hosted app (Copilot Managed Runtime, Entra auth, connectors, a Git repo per app), shareable inside the org. Bills credits to build and to run (one blog: about 1,600 credits for a Gantt app). | Its tech stack; whether a skill can drive it from a template | `[Cited]` hosting; rest unknown |
| **MCP App** | Self-contained HTML in a sandboxed frame. React and Fluent work. Can call our tools and post back to the chat (design §9.1a). | — | `[Cited]`; needs our server (T2) |

### 5.2 The visual ladder for our packs (ranked by evidence)

| Level | What | Example (NDA pack) | Hosting | Interactive? | Persists and shares? | Status |
|---|---|---|---|---|---|---|
| **V1. Visuals inside the deliverables** | Charts and colour in the files we already produce: native Excel charts and conditional formatting, a PowerPoint brief, SVG/PNG charts built by script and embedded | Page-1 summary in the internal redline (have). **Excel audit pack** with a risk chart and coverage heat map. Optional one-slide brief. | None | No | **Yes**: they're files | Documented preview; build `[Proposed]` |
| **V2. Self-contained HTML dashboard** | One offline `.html` file in the output folder, opened in the preview pane | Results dashboard: risk donut, topic heat map, clause list, and links back to the redline | None | **Probably not in the preview.** Microsoft's own `CoworkDashboard` sample says previews may block scripts. Microsoft CAT saw Teams and Outlook previews skip inline scripts over about 2,000 characters. Interactive once downloaded and opened in a browser. | Yes: a file that also opens in any browser | `[Cited]` that HTML renders; scripts likely blocked (observed, 2026-11-05 sweep); T-P13 confirms |
| **V3. Adaptive Card in chat** | A card drawn by the built-in skill | **Plan preview** before files are built: counts, High items, escalations | None | Buttons unproven (S7) | No: chat only | Demo only; contract unknown |
| **V4. MCP App** | A React/Fluent widget served by our connector | Interactive review: accept or reject plan items, matter look-ups, saving to Spaarke | BFF (T2) | Yes | Through Spaarke | v2 (§9.1a) |
| **V5. App built-in** | A Microsoft-hosted app | — | Microsoft | Yes | Yes, inside the org | **Not for R1**: Frontier preview, metered, no template hook |

**Recommendation.** Make V1 and V2 the primary visual channel, because they are documented, persistent, shareable and need no hosting. Use V3 as the "wow" moment before files are built, but always with a fallback. Keep V4 as the interactive route in the connector project.

### 5.3 Can we use a React component library?

| Where | React? | How |
|---|---|---|
| Adaptive Card (V3) | **No** | Cards are JSON drawn by the host in its own style. We supply the data and layout, not components. |
| HTML dashboard (V2) | **Not in the preview pane, probably** (2026-11-05 sweep: Microsoft's `CoworkDashboard` and CAT's `rich-html-presentation` both report previews blocking scripts) | **Default:** `render_dashboard.py` writes **static HTML + CSS + SVG** with no script, which renders in the preview. **Optional:** the same file can carry a React + Fluent layer that runs only when the user downloads it and opens it in a browser. That layer is worth building only if T-P13 shows a real benefit. Offline only: no CDN, no `fetch`. |
| MCP App (V4) | **Yes** | Single-file Fluent v9 build served by the BFF. We already have the `widget-design` skill and `knowledge/mcp-apps/`. |
| App built-in (V5) | Unknown | Not for R1 |

So **interactive React belongs to the MCP App (V4)**, in the `spaarkeai-mcp-bff-api-r1` project. At T0 the visual is a static HTML/SVG file, plus visuals inside the Office files we deliver.

### 5.4 Rules for visuals

1. **Numbers come from scripts, never from the model.** A script computes every count, percentage and list from `edit-plan.json` and the check results. The model never types a figure into a card or chart. *Enforced by: the visual engines take only files, and `check_outputs` compares the visual's data with the plan.* `[Proposed]`
2. **Every visual has a text fallback**: a Markdown table in chat and the same facts on page 1 of the internal redline.
3. **Offline and self-contained.** No external scripts, fonts or calls. Client data never leaves the output folder.
4. **One Spaarke visual language.** A `visual.tokens` component (risk colours, icons, labels) used by the card, HTML, Excel and Word outputs alike.
5. **Visuals are declared in the pack's mini-spec** (§2.4 section 7): what, when (before or after the build), data source and fallback.
6. **Accessible.** Colour is never the only signal; every chart has a table.

### 5.5 NDA pack visuals `[Proposed]`

| When | Visual | Level | Built by |
|---|---|---|---|
| After the dry run, before files | Plan preview card: changes by priority, High items, escalations, Approve / Change | V3, fallback chat table | `engine.card_data` (new) + `visual.plan-preview` template |
| After `check_outputs` | Results dashboard (`<name> - Review Dashboard.html`), static HTML + SVG | V2 | `engine.render_dashboard` (new, stdlib) + `visual.dashboard` template |
| After `check_outputs` | Excel audit pack: change log, risk summary with chart, review checklist, topic coverage | V1 | `engine.audit_pack` (new; stdlib XML, or `openpyxl` if probe finds it) |
| Always | Page-1 linked summary in the internal redline | V1 | `summary_table` (have) |

### 5.6 Tenant tests to close (new T-P13, one session)

1. Probe `matplotlib`, `openpyxl`, `pandas`, `PIL`, `plotly` and `node`.
2. A script writes `risk.png` and `risk.svg`: do they show inline in chat, or only in the preview pane?
3. A script writes a self-contained `dashboard.html` with an inline script, a button, an SVG chart, a `fetch()` and a CDN `<script>`: does the preview run the JS? Are the network call and CDN blocked?
4. A small single-file React + Fluent bundle in `assets/`, with injected data: does it render and respond?
5. Card JSON in `assets/` (1.5 with Table/FactSet, then 1.6 with Chart.Donut and Badge), and the skill asks the Adaptive Cards skill to use it: does it render, and are the `${}` placeholders filled?
6. Action.Submit / Execute / OpenUrl / ShowCard: which render, and does a click arrive as a user turn? (S7)
7. A script-built `.xlsx` and `.pptx` with native charts, previewed: check fidelity.
8. In chat: a Markdown table, a Mermaid block, LaTeX.

9. Binary companions after plugin install. One community packager reports that `.docx` and `.xlsx` arrive empty. Our T-05 counters that for our route: the `sample-nda.docx` asset in the unified plugin worked and the trial review ran on it. Re-check on admin deploy (T-14) and the folder route.
10. Runtime packages. A community probe (one tenant, 2026-10-05) saw `openpyxl`, `pdfplumber`, `PIL` and `tesseract`, and no Chromium or Playwright. Confirm with our `probe.py` before relying on any of them.

T-P14 (optional, Frontier tenant only): `/app` with clause-risk JSON. Record the stack, where it renders, sharing and `/cost`.

---

## 7. External resources (GitHub and Hugging Face sweep, 2026-11-05)

Source: researcher note `github-hf-delta-sweep-cowork-mcp-2026-11-05.md` (licences checked at source). It adds to the 2026-10-31 legal-content sweep (design §8.5, `references.md`).

**`kesslernity/awesome-copilot-cowork-skills`: Skip.**
- CC BY-SA 4.0, so none of its wording can go into a proprietary product.
- It's a marketing funnel for paid kits.
- It's stale: wrong count of built-ins and old models.
- Its "tested" claims are self-declared; its legal skill says "Tested in Cowork: not yet".

**Adopt (accelerator):**

| Item | Licence | Use |
|---|---|---|
| `microsoft/wiqd` v0.24.0 (2026-11-05) | MIT | CI gate `plugin validate --mode deep`. **0.24.0 changes validation, so re-baseline our gates.** |
| `microsoft/CoworkDashboard` | MIT | HTML dashboard and verify-gate pattern; the preview caveats behind OD-30 |
| `microsoft/cat-agent-skills` (`rich-html-presentation`, `chart-builder`) | MIT | Preview constraints; chart script pattern |
| `NVIDIA/SkillEvaluator` Tier 1 only | Apache-2.0 | Offline skill-quality score as a lint gate. Tiers 2 and 3 send skill text to a hosted model: not used. |
| `Dmitry-Kov/ooxml-integrity` | MIT (young, 0 stars) | A second, independent CI check for lost comment anchors and tracked changes. Trial beside `check_outputs`. |
| Hugging Face `crosbylegal/RedlineBench` | CC BY 4.0 data, MIT code | Eval method: real `.docx` files with tracked changes, playbooks, attorney rubrics, "surgicalness" and verbosity metrics. For NDA: the rubric and metrics. For the **MSA pack** (OD-32): the corpus as well, with attribution. |

**Reference:**
- `houfu/lq-plugin-cowork`: a 27-probe Cowork harness. It has no licence, so run it but don't copy it.
- `cowork-scenario-plugin-packager`: platform notes.
- Skill eval tools: `skillprobe`, `skillgrade`, `agent-skills-eval`, `skilllint`.
- Charts-in-Adaptive-Cards doc and card evals.
- `alea-institute/alea-redline` and `alea-clause-type` on Hugging Face (CC BY 4.0).

**Watch:** `microsoft/apm` (pack versioning), `docx-redline`, `python-redlines`, adeu.

**Skip:** anything AGPL, non-commercial or unlicensed. That includes `jubarte-redlines`, the `kleister_nda_*` datasets (no licence) and the obaydata benchmark (non-commercial).

**Also found:**
- Learn pages disagree on upload limits: `cowork-customize` says 100 files / 10 MB compressed / 50 MB uncompressed; `use-cowork` says 20 companions / 10 MB.
- No NDA-specific redline or negotiation dataset exists with a clean licence.

**For the MCP project:** the same note has a section (B) to hand to the `spaarkeai-mcp-bff-api-r1` window. It covers:
- the PnP .NET 10 Entra-protected MCP sample;
- Fluent v9 single-file widget samples (copilot-camp lab11, Power CAT, power-platform-skills `mcp-apps`);
- the MCP conformance suite (`--spec-version 2026-07-28`);
- `microsoft/DataFactory.MCP` (.NET MCP Apps).

No public sample yet combines .NET, Entra, a widget and a declarative agent.

---

## 6. Proposed edits to design.md (v0.6)

| Design § | Edit |
|---|---|
| New §5.4a | "Components by surface": the Word and Cowork tables from §1 |
| §5.6 | Add §4.1–4.4 in short: what Python does, an example per engine, and the limits table |
| §5.10 | Add the race-avoidance rules (§2.2: one entry skill per job, sequencing inside the skill, artifact chaining, blocking verifier, one plugin per user-facing skill); runtime primitives vs build-time components; the component types and manifest; the new gates |
| New §5.11 | Solution packs: definition, the pack catalogue, the mini-spec template; NDA mini-spec moves to `practices/nda/PACK.md` |
| §9 | Retitle "Visuals, forms and interactive UI"; add §5.1–5.5 (the ladder, React answer, rules, NDA visuals). The existing forms content stays. |
| §12 | Add T-P13 (visual probe) and T-P14 (App built-in, optional) |
| §16 | Phase 1: component manifest, step includes, BOM, cross-pack gates. Phase 2: NDA visuals after T-P13. |
| §17 | New SC: every pack has a `PACK.md`; the build fails on a duplicate user-facing skill or a shared trigger phrase; every visual's numbers match the plan and it has a text fallback |
| §18 | New owner decisions (below) |

**Owner decisions to add:**

| ID | Decision | Recommendation |
|---|---|---|
| OD-26 | Adopt solution packs with a `PACK.md` mini-spec each | Yes |
| OD-27 | Move shared user-facing skills (setup, help) into a **Core plugin** | Yes, before a second pack ships; R1 can stay one plugin if it ships only NDA |
| OD-28 | Component manifest, step includes and bill of materials in the build | Yes, Phase 1 |
| OD-29 | Visual priority: deliverable visuals (V1) and the HTML dashboard (V2) first; the card (V3) optional with fallback | Yes |
| OD-30 | T0 dashboard format | **Static HTML + CSS + SVG from Python** (previews likely block scripts, per the 2026-11-05 sweep). Interactive React only in the MCP App (V4). |
| OD-31 | Adopt external tools found in the 2026-11-05 sweep (§7) | **Decided (owner, 2026-11-05): adopt** all §7 "Adopt" items |
| OD-32 | **MSA pack** in the library (owner, 2026-11-05). This reverses design §8.3 "Rejected: MSA review … duplicates Legal for Cowork". | Add it, without competing with Microsoft on routing. (a) Always ship **content**: an MSA Word playbook (Word) and an MSA legal playbook that Microsoft's `review-contract` can use in Cowork (protocol rule 1). (b) Add a Spaarke `msa-review` entry skill only for the **packaged deliverable** (internal and external redlines plus a clean copy, checked by script), as with NDA. When it ships, `nda-review`'s "use review-contract for MSAs" line changes, and routing coexistence is tested (T-P2). Eval corpus: RedlineBench (CC BY 4.0, attributed). Wave: R2 or later. |
