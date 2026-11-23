# Specialist Legal Agents — Program Design (`specialist-legal-agents-r1`)

> **Status:** Draft v0.5, umbrella program design. v0.5 (2026-11-23) adds §0 (the model in one page: seven components, how they stack, tiers, the pattern for every persona) and §3.11 (Word, Chat, Cowork and Home after Ignite). **Gate A0 (alignment with spaarke-legal-ai-accelerator-r1, §10.1): accelerator review received and folded in on 2026-11-23; owner decisions D-A11, D-A16 and the Q7 catalogue owner still pending.** v0.2 (2026-11-23) added §3.9 (function vs persona framing, D-A12) and Phase 0 test G5. v0.3 (2026-11-23) adds §3.10 (Microsoft's legal skills, D-A13), §10.1 (accelerator alignment, D-A14) and Phase 0 test G6. The next step is to split off the Phase 0 child and run `/design-to-spec` on it (§4). `/design-to-spec` has not been run.
> **Date:** 2026-11-23
> **Author:** Ralph Schroeder + Claude Code
> **Source:** [notes/source/design-synopsis-2026-11-22.md](notes/source/design-synopsis-2026-11-22.md), the owner's synopsis of 22 Nov 2026 (verbatim copy). This document keeps the synopsis's design. It changes four things: the primitives table is corrected against code (§6), live Copilot evidence is folded in (§7), the Phase 0 scope is narrowed to what that evidence leaves open (§8), and coordination points and ADR tensions are added (§10, §11).
> **Epic:** #421 SPAARKE AI AND AUTOMATION
>
> **Tags:** `[Synopsis]` taken from the source synopsis · `[Code]` checked against master on 2026-11-23 · `[Tested]` live result from `spaarkeai-mcp-bff-api-r1` Phase 0 (dev tenant, 2026-11-12 to 14) · `[Open]` owner decision · `[Verify]` Phase 0 must prove it

---

## 0. The model in one page (v0.5, 2026-11-23)

**The idea in one sentence.** Legal expertise is written once, as **practice content**, and turned into **functions** (one legal task each). Functions are grouped into **personas** (one per practice area) and tuned by each **attorney's profile**. All of this is delivered through **wrappers** for each Microsoft surface. From T2, it runs on a **Spaarke runtime** that adds Spaarke data, server-side engines and governance.

The accelerator (spaarke-legal-ai-accelerator-r1) already does this for NDA at the individual and team tiers, with no IT and no connector. This program generalises that pattern to every practice and adds the runtime for T2/T3. The accelerator is **not finished**; it completes in parallel, and this program builds on it in two ways: on its NDA capabilities, and on its structure as the **pattern** for every other persona.

### 0.1 Seven components

| # | Component | What it is | NDA example | At T0/T1 (accelerator, files only) | At T2/T3 (this program) |
|---|---|---|---|---|---|
| 1 | **Microsoft platform** | The surfaces (Word Copilot; the Copilot app with Chat, Cowork and Home), Microsoft's orchestrator and models, the **Legal for Cowork** skills and the **Word legal plugin**, and the built-in skills | Microsoft's `review-contract`, `summarize-redlines`; Word "Review contract" | Ridden as is | Ridden as is. Spaarke never competes with a Microsoft function (§3.10) |
| 2 | **Practice content** | The expertise for one practice area: topic catalogue, baseline positions (the legal playbook), references, eval cases, output schemas | `topics.yaml` (`nda.*`), `baseline-mutual.md`, the Bluefin Harbor evals, `review-result.schema.json` | `src/accelerators/practices/nda/`, built into files | **The same source**, also rendered as JPS Actions and Knowledge. Customer positions are mastered in Dataverse and exported back to files |
| 3 | **Function** | One legal task: procedure + **engine** (the deterministic mechanics) + output contract + **golden set** | `nda-review`, `nda-library-review` | A Cowork skill with Python engines | The same procedure, with a **C# engine in the BFF** behind a connector tool, tested on the **same fixtures** |
| 4 | **Persona** (archetype) | One per **practice area**: which functions (Spaarke **and** Microsoft), default positions, authority and autonomy ceiling. Role (partner, associate, paralegal) is a setting, not a separate persona | "Commercial contracts counsel" = {`nda-review`, `nda-library-review`, Microsoft `review-contract` + the attorney's playbook, Microsoft `summarize-redlines`, …} | Light: the setup's recommended skill set per role, quick cards, and an optional agent recipe | An archetype record compiled at run time. The **area agent** is its front door in Chat |
| 5 | **Attorney profile** (instance) | One attorney's positions (playbook overrides), defaults (side, posture, jurisdiction), voice and thresholds | "Jane: usually the disclosing party, balanced posture, never accept uncapped indemnity" | `Documents/Cowork/Profile/legal-profile.md` + personal playbook files, written by `accelerator-setup` | The Dataverse master with layers L0–L4 and the ratchet (§3.2), **exported** back to the same files so T0/T1 skills keep working (round-trip: D-A15) |
| 6 | **Delivery wrappers** | How each surface receives components 2–5 | Word playbook `.docx`; `nda-review` skill; the NDA Review agent recipe | Word playbook, Cowork skills and plugin, Agent Builder agent (advanced tiers), packaged agent (team with IT) | Plus the **area agent + Spaarke connector**, which replaces the packaged agent. All wrappers are generated by the build from one source |
| 7 | **Spaarke runtime** | T2/T3 only: connector tools, BFF engines, profile compiler with ratchet, gates, run record, golden-set gate and tuning loop, Spaarke data (matters, SPE documents) | `spaarke_review_nda`-style tool; the deployed NDA-REVIEW Action | — | This program's main build |

### 0.2 How they stack

```
 Surfaces (Microsoft)    Word Copilot            Copilot app:  Chat  ·  Cowork   (Home = the entry, §3.11)
                              │                                  │          │
 Wrappers (6)           Word playbook .docx           agent (front door)   skills / plugin
                              └──────────────────┬───────────────┴──────────┘
 Persona (4)            practice area: which functions · default positions · authority ceiling · role
                                                 │ tuned by
 Attorney profile (5)   positions · defaults · voice · thresholds        (L0 platform … L4 matter, ratchet)
                                                 │ parameterises
 Functions (3)          Spaarke functions (NDA, legal hold, IP …)   +   Microsoft Legal functions (1)
                                                 │ built from
 Practice content (2)   topics · baseline positions · references · eval cases · schemas
 ───────────────────────────────────────────────────────────────────────────────────────────────
 Spaarke runtime (7)    T2/T3 only, under the functions: connector · BFF engines · profile compiler
                        and ratchet · gates · run record · tuning loop · matters and SPE documents
```

### 0.3 Tiers: same content, more runtime

| Tier | Owner | Adds | Profile lives in | Engines run in | Governance |
|---|---|---|---|---|---|
| **T0 individual / T1 team** (the accelerator's four packages: individual base and advanced, team without IT and with IT) | Accelerator | Files, Cowork skills, Word playbook; Agent Builder or packaged agents in the advanced tiers | `legal-profile.md` (a file) | Python in the Cowork sandbox | Microsoft's only (approval cards; the user's own rights) |
| **T2 firm or department** | This program + accelerator content | **The Spaarke connector**: Spaarke data, BFF engines, server-side profile, Spaarke's own gates and run record; the area agent replaces the packaged agent | Dataverse, exported to the file | BFF (C#), with Python still available in Cowork | Spaarke gates, run record, admin deployment |
| **T3 Spaarke platform** | This program (+ ontology) | The positions master (Policy versions), layers L0–L4 with the ratchet, tuning loop with the golden-set gate, gated autonomy, matters | Dataverse (master) | BFF | Full: ratchet, tested diffs, audit, autonomy ladder |

### 0.4 The pattern for every other persona

A new persona (patents, trademarks, employment, …) is built the way the accelerator built NDA (accelerator design §5.1: customer layer, practice layer, core):
1. **Practice content.** A topic catalogue, baseline positions, references, eval cases and schemas, reviewed by an attorney. Use the researched sources, and do not inherit untested Spaarke content (accelerator §7.3).
2. **Microsoft-first inventory.** For each task: does Microsoft own it? If yes, reuse it and feed it the attorney's exported positions. If no, it becomes a Spaarke function (§3.10).
3. **Spaarke functions.** A skill + engines + golden set. At T2 the engine also exists in the BFF, tested on the same fixtures.
4. **The persona definition.** Its function list (Spaarke and Microsoft), default positions, authority ceiling and role presets.
5. **Onboarding.** The setup interview (`accelerator-setup` pattern) writes the attorney profile.
6. **Wrappers.** Generated by the build for each surface and tier from the one source, with collision, recipe and forbidden-content gates.

**NDA is the reference implementation** (accelerator `practices/nda/`). Every later persona copies its shape.

## 1. Owner decisions (2026-11-23)

| # | Decision | Choice |
|---|---|---|
| OD-1 | Project name | `specialist-legal-agents-r1` |
| OD-2 | First archetype (Phase 1 vertical slice) | **NDA reviewer.** This replaces the synopsis's biotech patent claim-drafting associate. Reasons: the accelerator projects already ship an NDA agent, playbook, positions, eval corpus and Cowork skills; the BFF already runs a deployed NDA-REVIEW Action (§9); and the synopsis's autonomous-run example is the NDA. The patent associate moves to Phase 4 as the second archetype, together with the supervision protocol it exercises. |
| OD-3 | Shape | **Umbrella program with phased child projects** (§4). This folder holds the program design, decisions and coordination; each phase runs as its own child project. |

Still open from the parent session's recommendations: whether deterministic gates and profile application live server-side (D-A7, §12) and the NDA-first choice's knock-on for the supervision protocol (D-A8). OD-2 settles the archetype only.

## 2. Summary `[Synopsis]`

**Build one governed runtime and many attorney-owned Role Profiles, not many bespoke agents.** A specialist ("NDA reviewer", "CRE partner", "biotech patent claim-drafting associate") is a versioned configuration document: charter, positions, knowledge scopes, skills, tools, authority limits and output contracts. A shared runtime compiles it at invocation. The attorney owns and edits the profile; the platform owns the guardrails the profile cannot loosen. Attorneys meet the agent in Microsoft 365 Copilot through plugin-packaged declarative agents backed by the Spaarke BFF MCP connector, with a custom engine agent as the pro-code fallback.

Three decisions carry the design:

1. **Profile layering with a one-way ratchet.** Platform → archetype → firm policy → owner → matter. Lower layers may narrow authority and tighten gates; they can never widen them.
2. **Tuning is propose-and-approve, never silent learning.** Conversation, redline corrections and example documents all produce a reviewable profile diff. The diff is tested against the agent's golden set before it goes live.
3. **Attributed to the owner, enforced in tools.** Chat runs execute under the attorney's delegated identity. Every run is ledgered to the owner and the profile version. Background agents get Entra Agent IDs (but see ADR tension T-2, §11).

**What the evidence changes (§7).** Copilot did not honour MCP server `instructions` in Chat or Cowork (T7), and it calls our tools for most domain prompts even when the source is switched off (T6/T6b). Both point the same way: the attorney profile and every gate should be **applied by Spaarke on the server**, with Copilot as router and renderer, unless Phase 0 shows a declarative agent reliably follows a profile it is handed. That is Phase 0's go/no-go (§8, G1).

## 3. Core design `[Synopsis]`

### 3.1 A specialist is a governed configuration

An attorney gets an **Agent Instance**: an Archetype plus their own overrides, bound to the shared runtime. At invocation the runtime compiles the layers into one effective, versioned instruction-and-permission set. Every run records which profile version produced it.

Why configuration rather than code:
- the attorney tunes a document they can read, diff and roll back;
- a role changes parameters, positions and gates, not machinery (template-matching doctrine);
- one runtime to secure, evaluate and meter (ADR-013, ADR-016);
- archetypes become distributable templates, and a firm's tuned instance is a private fork.

Two role shapes: a **partner** profile, weighted toward judgment, risk positions and escalation, and an **associate** profile, weighted toward production under a named supervising profile or human.

### 3.2 Profile layers and the ratchet

| Layer | Owner | Typical content | Locked against lower layers |
|---|---|---|---|
| L0 Platform | Spaarke | Privilege handling, D-F0 fact rules, epistemic-class grounding, write gates, audit | Everything |
| L1 Archetype | Spaarke or firm knowledge team | Role charter, default skills, output contracts, baseline positions | Tools and scopes it does not list |
| L2 Firm or department policy | GC, managing partner, practice head | Approved positions, conflicts and ethical-wall rules, model tier, autonomy ceiling | Autonomy ceiling, data scopes |
| L3 Owner profile | Owning attorney | Personal positions and fallbacks, voice, review habits, preferred precedents, escalation thresholds | Nothing above it |
| L4 Matter overlay | Matter lead | Client instructions, deal specifics, jurisdiction, deadlines | Nothing above it |

Authority, gates and data access ratchet one way. Style and emphasis merge freely. The existing `PromptSchemaOverrideMerger` (§6) merges by "arrays concatenate, scalars replace, `__replace` directive". That is a free merge with no ratchet, so the layer compiler is new code, not a configuration of the merger.

### 3.3 Tuning

There are four inputs and one output: a **proposed profile diff** in plain language.
1. Direct edit in a profile editor (Fluent v9 Code Page).
2. Conversational ("from now on, never accept uncapped indemnity without flagging it"): the Builder Agent elicits variables and emits the diff.
3. Correction harvesting: repeated edits and rejections on agent output become a suggested position change, with examples attached.
4. Exemplar-based: "this document is how I do it" produces style and structure rules, with the source excerpts cited.

**Activation is gated.** The diff runs against the golden set, the attorney sees the behaviour diff and approves, and every run records its version. Rollback is one action. The attorney can never tune the platform gates, the privilege rules, the fact layer (dates, arithmetic and deadlines are computed) or the human-review requirement for external effect.

### 3.4 Evaluation loop

Seed (archetype golden set) → Extend (promote accepted, rejected or corrected outputs with one action) → Gate (every diff runs against the set) → Monitor (override, escalation and cost rates per agent) → Retire (stale positions flagged). Deterministic checks run as tools and as eval assertions.

### 3.5 Self-tuning (Coach)

A Service Bus job (ADR-004) turns signals into suggestions; nothing changes silently. The signals are: corrections in the run ledger and SPE versions; buttons on outputs (Add to my knowledge, Make this my position, Use as my example, Never do this again); and, opt-in, Work IQ context. Suggestions are delivered under a nudge budget. Store derived rules, not client content. Harvesting is matter-scoped (D-A4).

### 3.6 Autonomous runs

An event (for example an incoming email classified as an NDA) starts the same specialist headlessly through the backend:
1. Graph change notification.
2. Service Bus job with an idempotency key and an authority envelope.
3. Compile L0–L4.
4. D-F0 deterministic checks, then GREEN, YELLOW or RED triage.
5. OOXML redline and draft reply.
6. The send is a write gate: the attorney gets an Outlook draft and a Teams approval card.

The autonomy ladder is triage only → prepare (default) → auto-send (GREEN, standard paper, only where L2 and L3 both allow) → never automatic (RED, non-standard, privileged, first runs after retune). **The synopsis's "run as the delegated attorney first" conflicts with ADR-052 and ADR-028 A5 (T-1, §11).**

### 3.7 Governance

- **Accountable human per instance**, plus a named supervising reviewer for associate roles.
- **Reads permissive, writes gated.**
- **Ethical walls in Spaarke's scope tools.** Purview Information Barriers are not supported for plugin management.
- **Agent work product is attorney work product.** It is tenant-resident and never used for shared training.
- **Honesty contract:** each output carries its epistemic class and citations.
- **Exportable audit.**
- **Cross-client learning off by default.**

### 3.8 Microsoft platform mapping and delivery `[Synopsis]`, evidence in §7

| Concern | Microsoft service | Recommendation |
|---|---|---|
| Attorney chat surface | M365 Copilot (Chat, Cowork), Teams, Outlook, Word | Chat and Cowork are proven surfaces for our connector; Word is partial and Outlook is absent today (T1) |
| Packaging | Plugin (M365 app package): agents, skills (≤ 20), connectors (≤ 10) | Core plugin (connector + shared skills) plus one plugin per practice area |
| Front door (Path B, default) | Declarative agent with skills and MCP actions, one per archetype; instructions ≤ 8,000 chars | Needed for `@`-routing (E5) |
| Pro-code fallback (Path A) | Custom engine agent (Agents SDK / Foundry; Copilot harness SDK 2.0 is a new middle option) | Only on a failed requirement or eval |
| Backend orchestration | In the BFF, behind tools | Identical under both paths. Note: `Microsoft.Agents.AI` was removed from the BFF on 2026-09-29 (#1027), so see §10 |
| Knowledge | Azure AI Search, SPE, Foundry IQ | Knowledge Scopes as filtered, security-trimmed views |
| Identity | Delegated attorney for chat (proven, T5); Entra Agent ID for background agents | The Agent ID part needs an ADR-028 decision (T-2) |
| Memory | Foundry or Copilot memory | Non-authoritative; Dataverse profile is the source of truth |
| Tuning UI and storage | Code Page + Dataverse | Profile editor and diff review |
| Evaluation | Foundry evals, Copilot Agent Evaluations (preview), our eval harness | Whether Copilot's evals cover declarative agents is `[Verify]`, not Phase 0 |

**Decision rule `[Synopsis]`.** Conversational and tuned: agent. Multi-step, long-running, approval-gated or scheduled: Cowork skill. Data or actions: the single connector. Each procedure is written once in the skill library and packaged both ways.

### 3.9 Framing: is an agent a function or a persona? (D-A12, proposed 2026-11-23)

**The question (owner).** The "legal & IP professional team" could be framed by **function** (an agreement reviewer, a patent application drafter, a trademark searcher), by **persona** (the set of functions a given legal professional needs), or by a mix of both.

**What exists today is function-shaped.** The tenant's Plugins page shows the following:
- Chat tab: the agents *Spaarke NDA Review* and *Spaarke Legal Hold Review* (Agent Builder, built from accelerator recipes), plus *Spaarke AI* and the Copilot Studio agent *Spaarke Dataverse MCP*.
- Also on the Chat tab, *Spaarke Legal Review*. **Corrected 2026-11-23 by accelerator-r1 (replaces C3):**
  - It **is** from the accelerator's work: the owner built it in Copilot Studio on 2026-11-09 for the accelerator's declarative-agent spike (route A; accelerator `notes/poc-e2e/results.md` "Spike DA-1" and `da-spike-script.md`).
  - It is the single front door over **both** the NDA and legal hold playbooks, so it is a **two-function agent**, not a function.
  - The accelerator's package build does not generate it, and its instruction version is unconfirmed.
  - It is an early, never-measured instance of the persona-agent shape that G5 tests.
- Cowork tab: the skills `nda-review`, `accelerator-setup` and `legal-hold-review`, plus the *Spaarke MCP* and *Spaarke MCP spike* connectors.

Each of those agents is one task. That was the right shape for a proof of value, but it does not scale: one agent per function becomes dozens of near-identical entries in the `@` picker, each needing its own profile.

**Proposed answer: both, at different layers. Functions are the machinery; personas are the configuration and the front door.**

| Layer | Unit | Examples | What it owns | Owned by |
|---|---|---|---|---|
| **Function** (capability) | One legal task, built once: a `SKILL.md` + a server-side Action/tool (§6 "Skill", "Tool") | `spaarke-nda-review`, `spaarke-contracts-agreement-review`, `spaarke-patents-claim-drafting`, `spaarke-trademarks-clearance-search` | The procedure, deterministic checks, output contract, accuracy claims, its **golden set** and its **position schema** (which positions it reads) | Spaarke (L0/L1) |
| **Archetype** (persona) | A role template: a set of functions + default positions per function + authority and autonomy ceiling + escalation and voice | "Commercial contracts counsel" = {NDA review, agreement review, …}; "Patent prosecution associate" = {claim drafting, office-action response, prior-art search} under a supervising partner | Which functions it may run, the **authority ratchet**, defaults for each function's positions | Spaarke or firm knowledge team (L1/L2) |
| **Agent Instance** | One attorney's tuned archetype | "Jane's commercial contracts counsel" | The owner's positions per function, thresholds, voice, golden-set additions (L3), plus matter overlays (L4) | Attorney |

Why each concern sits where it does:
- **Golden sets and accuracy are per function.** The NDA corpus and expected outputs (§9) test a task, not a person. A persona's eval is the union of its functions' sets, run with that instance's positions.
- **Positions belong to the function's domain, but ownership follows the layers.** The profile stores positions keyed by function (`positions["spaarke-nda-review"].term = …`). The NDA positions an attorney tunes are used wherever NDA review runs for them.
- **Authority belongs to the role, not the task.** The same trademark search is fine as a paralegal's draft and needs review before it reaches a client. Autonomy ceilings, send and file rights, and supervision are archetype and instance properties. So the ratchet lives in the persona layer.
- **Copilot mechanics force function-level skills anyway.**
  - Cowork cannot call agents (Ignite: Frontier preview), so every function must exist as a standalone skill.
  - A declarative agent's instructions are capped at 8,000 characters, so a persona agent cannot hold its functions' procedures. It routes to skills and tools.
  - Skills are reachable under `/` (E1/E2); agents under `@` (E5).
- **Distribution follows practice areas.** Admins assign plugins to groups (Agent 365), and groups map to practice teams. The agreed naming rule `spaarke-{area}-{capability}` already says it: the **area** is the persona and package grouping, and the **capability** is the function.

**Area and role are two axes; do not build one agent per area × role.** The synopsis's archetypes combine them ("CRE *partner*", "biotech patent *associate*"). Proposed:
- **one front-door declarative agent per practice area** (`spaarke-contracts`, `spaarke-patents`, `spaarke-trademarks`, …), whose functions are that area's skills;
- **role (partner / associate / paralegal) as a profile dimension** that sets authority, autonomy, output weighting and the supervision link.

A firm can still publish a role-named agent if it wants one; it is the same archetype with a role preset. This avoids a combinatorial catalog and keeps routing in Copilot's picker manageable.

**Consequences:**
- **OD-2 (NDA first) stands.** In this model NDA review is the first *function* of the first *area archetype* (commercial contracts). Phase 1 should deliver it as the contracts archetype with NDA review as its first function. If a second existing function (for example legal hold review, which the accelerator already packages) can be added cheaply, Phase 1 also proves routing between functions within one persona.
- **The existing agents** (the single-function *Spaarke NDA Review* and *Legal Hold Review*, and the two-function *Legal Review*) become either presets of their area agent or are retired in favour of it. That is a coordination item with accelerator r1 (D-A11).
- **New Phase 0 test G5 (§8):** can a persona-level declarative agent route reliably among its functions?

**Open for the owner:** confirm the three-layer model, and whether the persona grouping is the **practice area** (proposed) or the **professional role**.

### 3.10 Microsoft's legal skills: coverage, coexistence and riding their upgrades (D-A13, proposed 2026-11-23)

**Source.** Accelerator r1 researched this in depth: its design §2.1, §2.4, §5.2, §5.2a, §5.7 and §5.8a; `notes/legal-for-cowork-skills/` (the 20 skill texts, extracted 2026-11-05); and `notes/copilot-app-components.md`. This section adopts its findings. It does not re-derive them.

**What Microsoft ships.** "Copilot for legal" (shipped 2026-10-15) is two plugins, included with the M365 Copilot seat:
- **Legal for Cowork.** A built-in plugin of **20 skills**. The tenant showed v1.0.10. The user adds it from Customize › Roles & Industry › Legal. It is read-only to us.
- **The legal plugin in Word** (Word for Windows desktop only). Three skills share Legal for Cowork's names: *Legal reasoning*, *Edit contract* and *Draft new contract*. It runs "Review contract" against a four-column Word playbook, producing native tracked changes.

Microsoft ships **no** practice content, no default playbooks, no playbook schema and no per-attorney personalization that carries across surfaces. Those gaps are where Spaarke adds value.

**Microsoft's 20 Cowork skills, grouped by function** (each has a "Do NOT use for … use X instead" line pointing to its neighbours):

| Group | Microsoft skills | Spaarke stance |
|---|---|---|
| Contract review and markup | `review-contract` (against a supplied playbook), `edit-contract` (tracked changes), `summarize-redlines` | **Microsoft-first.** Spaarke supplies the attorney's positions as the playbook. Exception: **NDA in Cowork routes to Spaarke `nda-review`** (packaged, script-checked output; owner-approved 2026-11-02, accelerator §5.7). Routing evidence so far is accelerator S9 only (2026-11-01: 2 prompts, user-scoped plugin, Legal for Cowork on); the full routing test is G6 |
| Drafting from precedent | `draft-new-contract`, `create-playbook` | Microsoft-first. `create-playbook` is the onboarding route that turns precedents into a first-draft playbook |
| Portfolio and extraction | `compare-key-terms`, `analyze-related-contracts`, `extract-obligations-and-deadlines` | Microsoft-first. Spaarke adds only where its data or engines differ (for example the accelerator's `nda-library-review` with playbook-rated cells) |
| Document checks | `check-defined-terms`, `check-cross-references`, `check-execution-readiness` | Microsoft-first as **skills**. The synopsis's "deterministic checks (defined-term consistency, cross-references)" stay **inside** Spaarke functions as engines or eval assertions, never as competing skills |
| Reasoning and writing | `legal-reasoning` (the neutral fallback), `draft-legal-memo`, `explain-legal-concepts`, `improve-legal-prompt` | Microsoft-first |
| Litigation | `build-case-chronology` | Microsoft-first. The accelerator rejected a chronology accelerator as a duplicate |
| Regulatory | `identify-regulatory-gaps`, `compare-across-jurisdictions` | Microsoft-first |
| Governance and matters | `draft-minutes-and-resolutions`, `summarize-legal-matters` | Microsoft-first. Spaarke matter data could ground them later, through the connector |
| **Not covered by Microsoft** | — | **Spaarke-owned functions:** NDA packaged review, legal hold review, legal intake, invoice review (accelerator wave 1), and all **IP practice** functions (patent claim drafting, office-action response, trademark clearance search, IP docketing). IP has the lowest collision risk |

**Why Microsoft-first.** Microsoft's skills are first-party, come with the seat, and will keep improving. A Spaarke skill that competes with one of them risks three things:
1. losing routing to it;
2. confusing the attorney;
3. being overtaken by Microsoft's next release.

What we know about routing:
- Copilot picks a skill by matching the request against enabled skills' descriptions. Accelerator test S9: "Review this NDA" went to `nda-review`, and "Review this MSA" went to Microsoft's `review-contract`.
- Plugin skills **cannot override** a built-in of the same name.
- Users cannot switch individual built-ins off.
- Precedence when both plugins are on is not documented. Accelerator T-P2 / T-14 is still open.

**Rules adopted program-wide** (from accelerator §5.7 and its non-overlap rules):
1. **Microsoft first.** Before building any function, inventory what Microsoft Legal (Word + Cowork) and installed partners already do. Where Microsoft owns a task, Spaarke supplies **content** (the attorney's positions as a playbook), **data** (the connector) and **governance** (profile, gates, ledger), not a competing skill.
2. **No name or primary-trigger collision** with a Microsoft legal or built-in skill, gated in the build. **The existing gate is incomplete** (accelerator, 2026-11-23, C4):
   - `src/accelerators/build/gates.py` `RESERVED_NAMES` holds only 7 of the 20 Microsoft names, so 13 are not gated;
   - it checks names only, not triggers.

   The fix (agreed direction, Q7):
   - move the snapshot to a shared, machine-readable list (name, description, version, date) in `knowledge/copilot-for-legal/`;
   - have the one lint in `gates.py` read that list, so both projects run one check;
   - add a trigger check.

   Do not rely on the gate as complete until that lands.
3. **Every Spaarke function description names its Microsoft neighbours** ("Do NOT use for MSAs — use review-contract"), the way Microsoft's own descriptions do.
4. **Positions render to Microsoft's format.** The four-column contract-review playbook (Topic | Instructions | Example Text | Compromise Positions) is what Word's Legal plugin and `review-contract` read. An attorney's tuned positions (L3) are **exported** to that format. Tuning in Spaarke then improves Microsoft's skills for that attorney too, so every Microsoft upgrade to `review-contract` becomes an upgrade to the attorney's specialist.
5. **Track Microsoft's catalogue.** Keep a dated snapshot of the Microsoft legal skills (name, description, version), currently the accelerator's `notes/legal-for-cowork-skills/` and its planned `knowledge/copilot-for-legal/` (K3). The collision gate runs against it. When Microsoft's version changes, refresh the snapshot. When a new or changed Microsoft skill overlaps a Spaarke function, record a decision: retire ours and keep our content feeding theirs, or keep ours with the differentiator written down. That differentiator must be packaged output, Spaarke data, deterministic verification, the per-attorney profile, or governance.

**How this fits the persona layer (§3.9).** An archetype's function list may name **Microsoft functions** as well as Spaarke functions, for example a "commercial contracts counsel" = {Spaarke `nda-review`, Microsoft `review-contract` + the attorney's exported playbook, Microsoft `summarize-redlines`, …}. The profile then supplies positions to both kinds.

What is not known is whether a Spaarke declarative agent can **invoke** Microsoft's skills:
- A declarative agent carries only the skills in its own package (up to 8).
- No source documents a custom agent or skill triggering a Legal for Cowork skill by name. Skill-to-skill hand-off is prose only, and the model's choice.

So the persona agent may only be able to *recommend* the Microsoft skill. Phase 0 test **G6** (§8) settles this.

**Differentiation that survives Microsoft's roadmap.** Per-attorney, governed positions with a ratchet and tested diffs; Spaarke matter and document data through the connector; verified deterministic output (engines checked by script); golden sets and audit; gated autonomy; and the IP practice. Generic legal reasoning, drafting from precedent and document checks are Microsoft's to own.

### 3.11 Surfaces after Ignite: Word, Chat, Cowork and Home (2026-11-23)

**Source:** [notes/research/copilot-surfaces-post-ignite-2026-11-23.md](notes/research/copilot-surfaces-post-ignite-2026-11-23.md). It has the full 7-column matrix and a source and tag for every cell. It is built on the accelerator's Word/Cowork research and the mcp-r1 tenant tests.

**Names.** The "Microsoft 365 Copilot app" is now **Microsoft Copilot**. **Home** is a tab in that app, not a separate product: one composer that switches between **Chat** and **Cowork**, plus **Office in Copilot** (a Word, Excel or PowerPoint canvas beside the conversation). Microsoft's Ignite blog (17 Nov) calls Home GA. The rollout is still going, and some Learn pages still say Frontier. Whether our dev tenant has it is unknown.

**Verdict: Chat and Cowork are partially collapsing.**
- **Converged:**
  - Home, one entry point.
  - Chat recommends Cowork and hands over its context (GA). Automatic routing across modes is "coming soon".
  - **Skills are GA in Chat and shared with Cowork** (MC1493223; Chat skills need Copilot Premium).
  - **One plugin registry**, with admin-deployed plugins available in both.
  - Plugin and skill governance is GA.
- **Still separate:**
  - **Declarative agents:** GA in Chat; Cowork can call them only in a Frontier private preview.
  - **Billing:** Chat is within the licence; Cowork bills **credits per step**.
  - **Memory and instructions:** separate.
  - **Runtime:** Cowork runs our Python with `lxml`. Chat's documented script sandbox has a fixed package list **without `lxml`**.
  - **Connectors:** connector-only plugins are not in Chat's store yet, and our package-delivered connector failed in Chat on 23 Nov (T2/T13/T14).

**Key differences by surface** (✅ yes · ⚠️ partly · ❌ no · ? unknown; detail and sources in the note):

| Capability | Word (desktop) | Word (web) | Chat | Cowork | Outlook / Teams |
|---|---|---|---|---|---|
| Microsoft legal skills | ✅ legal plugin: 3 skills + Review contract | ❌ | ? (skills GA in Chat; Legal for Cowork in Chat untested) | ✅ all 20 | ? |
| Runs **our** `SKILL.md` | ❌ listed, never loads | ❌ | ✅ documented GA, **untested with ours** | ✅ tested | ? |
| Runs our skill **scripts** (Python engines) | ❌ | ❌ | ⚠️ sandbox without `lxml` | ✅ tested (Python 3.12, lxml) | ❌ |
| Hosts a **declarative agent** (our persona front door) | ✅ reads and advises; cannot edit | ✅ | ✅ | ❌ (Frontier preview) | ✅ (classic Outlook, Teams) |
| **Spaarke connector** tools | ? | ⚠️ after a plugin pick + approval card, no widget | ⚠️ Dev Portal connector ✅; package connector ❌ today | ✅ | ❌ Outlook not offered (our test) |
| MCP Apps **widgets** | ? | ❌ | ✅ | ✅ (later widget hides earlier) | ? |
| **Tracked changes** | ✅ legal plugin Run Review | ⚠️ edits are "trackable" | ❌ | ? (not mentioned in any source) | ❌ |
| Billing | Licence | Licence | Licence | **Credits per step** | Licence |

**What this means for the model (§0):**
1. **A persona needs two wrappers, not one.**
   - In **Chat** (and Word, Teams, Outlook) its front door is the **area declarative agent**.
   - In **Cowork**, where agents cannot run, the persona is its **set of skills + the attorney profile + custom instructions**, as the accelerator does today.
   - Home puts both behind one composer, so the attorney sees one app. The build generates both wrappers from one persona definition.
2. **Function procedures now reach Chat too**, because skills are shared. The Python engines are another matter: Chat's sandbox lacks `lxml`, so the accelerator's redline engine may not run there. This is one more reason the T2+ engine lives in the **BFF behind the connector**, which works from any surface that reaches the connector.
3. **Route work by cost and depth.** Use **Chat** (within the licence) for conversational review, questions about a playbook and quick rated reviews. Use **Cowork** (credits) for packaged deliverables, batches and multi-file work. At T2+, the BFF engine can produce packaged output from Chat too, with no Cowork credits (Spaarke's Azure cost instead).
4. **Word is content plus advice, not Spaarke execution.**
   - Spaarke contributes the attorney's **exported positions as the Word playbook** for Microsoft's legal plugin. That plugin is desktop only and writes the tracked changes.
   - A declarative agent can read and advise, but cannot edit.
   - Connector tools work in Word web only after a plugin pick and an approval card.

   This matches the accelerator's rule "in Word, review = Microsoft Legal + the Spaarke Word playbook".
5. **Packaging risk is now concrete.** The package connector fails in Chat today, and duplicate registrations double-fire (T2/T14). G2 must prove Chat delivery before Phase 1 commits to the area agent in Chat.

**New Phase 0 test G7 (folded into G2):**
- Does our plugin skill run in **Chat**, with and without a Python script?
- Do Legal for Cowork skills run in Chat and in the Office-in-Copilot Word canvas?
- Do Cowork or canvas edits produce **tracked changes**?

These decide where each function's procedure and engine can run.

## 4. Program structure (OD-3)

This folder is the umbrella. It holds this design, the decision log, the coordination register and the Phase 0 go/no-go record. It carries no implementation tasks of its own. Each phase is a child project registered under the same Epic. A child starts only after the previous gate passes.

| Phase | Child project (proposed slug) | Scope | Gate to next |
|---|---|---|---|
| 0 | `specialist-legal-agents-phase0-r1` | Spikes and inventory follow-ups in §8 only | **G1 go/no-go** decides the runtime shape (D-A7) |
| 1 | `specialist-legal-agents-nda-r1` | Role Profile schema, layer compiler with ratchet, Agent Instance, NDA archetype end to end (Chat + Cowork), run ledger binding | NDA golden set passes for two owner profiles; ratchet tests pass |
| 2 | `specialist-legal-agents-tuning-r1` | Profile diff review, golden-set gate, versioning and rollback, then correction harvesting and the Coach | Diff-gate eval; no silent change path |
| 3 | `specialist-legal-agents-governance-r1` | Agent 365 plugin governance, background identity (after T-2), per-archetype budgets (ADR-016), dashboards, autonomous NDA intake (after T-1) | Owner and counsel sign-off on autonomy |
| 4 | `specialist-legal-agents-archetypes-r1` | Second archetype (biotech patent associate + supervision protocol), CRE partner, L2 firm-policy editor, template distribution | — |

Slugs are proposals, to be confirmed when each child is created. Phase 0's design is §7–§8 of this document.

**Gate A0, before any child starts:** alignment with spaarke-legal-ai-accelerator-r1 (§10.1, D-A14). Then create the Phase 0 child folder with a `design.md` drawn from §7–§8 and run `/design-to-spec` on it.

## 5. Scope boundaries

**In:** the runtime, profile, ratchet, tuning loop, golden set per agent, run attribution and the NDA archetype. The rest arrives through child projects.

**Out (owned elsewhere):**
- the MCP host, connector auth and plugin packaging mechanics (`spaarkeai-mcp-bff-api-r1`);
- the Cowork and file-based NDA skills and the accelerator tiers (`spaarke-legal-ai-accelerator-r1`, `-claude-r2`);
- the Template/Action engine itself (`ai-spaarke-action-engine-r1`);
- the Fact/Observation/Inference taxonomy (insights engine);
- the access model and Entra Agent ID policy (`unified-access-control-r2` and ADR-028 owners).

**Not adopted:** Copilot Studio as the agent environment; Copilot memory as a profile store; dependence on Cowork invoking declarative agents (Frontier private preview); auto-send before counsel's view on supervision and privilege.

## 6. Primitives — corrected against code `[Code]`

The inventory ran on 2026-11-23 against master `1aedc9ea74` (read-only `code-mapper` pass). Paths are relative to `src/server/api/Sprk.Bff.Api/` unless stated otherwise. The verdicts are Exists, Partial, Absent and Design-only.

| Primitive | Synopsis said | Real status | Evidence | Gap the program must close |
|---|---|---|---|---|
| **Archetype** | `[PROPOSED]`, extends the Action Engine Template | **Design-only.** No `ActionTemplate` or Action Engine code in `src/` | `projects/ai-spaarke-action-engine-r1` (ON HOLD; its resumption trigger, redesign-r1 G-P3, has passed, so it waits only for an owner go) | "Extends Template" depends on an engine that does not exist. Either Phase 1 defines Archetype on its own and the Action Engine adopts it later, or the Action Engine resumes first (D-A9) |
| **Agent Instance** | `[PROPOSED]` | **Absent.** No per-user agent configuration entity or class | — | New Dataverse entity. Needs a §11 justification |
| **Role Profile** | Dataverse JPS, extends the Scope System | **Partial.** JPS exists: `Services/Ai/PromptSchemaRenderer.cs:23`, `Services/Ai/Schemas/PromptSchema`. Node-level override merge: `Services/Ai/PromptSchemaOverrideMerger.cs:23`. Personas are looked up per playbook: `Services/Ai/AnalysisPersonaService.cs:22`, `PlaybookPersonaLookup` (`:369`) | No per-user or per-attorney overlay (`UserPreference` and `PromptOverlay` have 0 hits). The merger is free-merge with no ratchet | Layer compiler with a one-way ratchet; owner (L3) and matter (L4) layers; version pinning per run |
| **Skill** | Exists (Skills), with worked examples | **Partial.** `sprk_analysisskill` with `PromptFragment` (`Services/Ai/AnalysisSkillService.cs:403`); siblings `AnalysisKnowledgeService` and `AnalysisToolService`. Separately, the accelerator `SKILL.md` skills (Cowork/Copilot) live on the accelerator branch, not master | No examples field on the skill model (Dataverse columns not checked). Two different things are both called "skill": the Scope-System `sprk_analysisskill` (a BFF prompt fragment) and the Agent Skills `SKILL.md` (a Copilot package component) | Decide which one the synopsis's Skill is. Probably both: `SKILL.md` in the package and `sprk_analysisskill` server-side (D-A10) |
| **Tool** | Exists: BFF `IAiToolHandler`, exposed as MCP | **Partial: three separate surfaces, not one.** (1) `Services/Ai/IAiToolHandler.cs:13` has **one** implementation (`Services/Finance/Tools/FinancialCalculationToolHandler.cs:15`). (2) About 37 `IAnalysisToolHandler` handlers via `ToolHandlerRegistry`, wrapped for chat by `AgentToolCatalogProjector` / `ToolHandlerToAIFunctionAdapter`. (3) MCP: `Api/Mcp/McpEndpoints.cs:41,58` (`MapMcp`), `McpToolCatalog.cs`, `McpToolAuthorization.cs`, `Infrastructure/DI/McpModule.cs`, with 5 decision tools + 1 widget in `Api/Mcp/LegalOperations/Decisions/` | No file under `Api/Mcp` references `IAiToolHandler`, `IAnalysisToolHandler` or `ToolHandlerRegistry` | A profile "whitelists tools" across which surface? The MCP catalog is what Copilot sees; the analysis handlers are what playbooks run. Phase 0 inventory item I-1 |
| **Knowledge Scope** | Exists (index v2) | **Partial.** RAG index `spaarke-discovery-index` (`Configuration/AiSearchOptions.cs:31`), records index `spaarke-records-index`, references index `spaarke-rag-references`. Filters in `Services/Ai/RagService.cs`: tenant (`:205`, `:1075`, `:1172`), `speFileId` (`:939`), `ParentEntityId` (`:1192`). Named scopes are `sprk_analysisknowledge` rows | "Index v2" matches nothing in code. Trimming is by tenant and optionally parent entity or document; **no per-user ACL trim in `RagService`** | Security trimming to the caller and ethical-wall combinations (L2) are not there today. That is a dependency on UAC (§10) |
| **Position Library** | Extends Playbooks; `PlaybookExecutionEngine` is the execution path | **Playbooks exist; positions are absent in the BFF.** There is **no `PlaybookExecutionEngine`** (0 hits). The real path is `Services/Ai/PlaybookOrchestrationService.cs` (+ `IPlaybookOrchestrationService`, `NullPlaybookOrchestrationService`), with nodes via `Services/Ai/Nodes/AiAnalysisNodeExecutor.cs`; data `sprk_aiplaybook` / `sprk_playbooknode`; chat playbooks `Services/Ai/Chat/Playbooks/*.playbook.json` | No clause-position or per-attorney construct in `src/`. NDA positions exist as Markdown on the accelerator branch (`src/accelerators/practices/nda/positions/`, §9) | **Do not invent a new schema.** Adopt the accelerator's content model (§10.1): positions are keyed by `topics.yaml` topic IDs and written in its legal playbook format; outputs use its review-result schema. At T3 the master is the ontology project's **Policy versions**, exported to the Markdown and four-column `.docx` files that Cowork and Word read (accelerator §6.2). Binding into playbook nodes follows from that |
| **Authority and Gate Policy** | Exists; needs per-profile binding | **Exists, but hard-coded rather than policy-driven.** "The ONE Confirmation Gate": `Services/Ai/Chat/SideEffectGateAIFunction.cs:13`; `PendingPlanManager.cs:172` `RequiresConfirmation(ToolSideEffectClass?, BindingRisk, bool)`; `Services/Ai/Chat/Gate/GateRiskProfile.cs`; `PublicContracts/DecisionGateTier.cs`, `GateDecisionV2.cs`; `Services/Signals/GateDecisionRecordJob.cs` | The gate is a static function of the tool's `sprk_sideeffectclass` + `BindingRisk`. It fails closed, but takes no per-user, per-profile or per-tenant policy input. Whether the chat gate also covers MCP-invoked tools is not shown. MCP writes are gated by the widget's own Confirm (T3) | A policy input (L0–L4 ratchet) to the gate, the same gate on the MCP path, and an autonomy ceiling |
| **Supervision Protocol** | `[PROPOSED]`, Agent Framework workflow | **Absent.** `Microsoft.Agents.AI` (Agent Framework) was **removed** from the BFF on 2026-09-29 (#1027, zero call sites; `Sprk.Bff.Api.csproj:66–76`). The M365 Agents SDK (`Microsoft.Agents.Hosting.AspNetCore` 1.0.1) and `Microsoft.Extensions.AI` 10.11.0 remain | No delegation or supervisor workflow code | Moves to Phase 4 with the patent associate (OD-2). Re-adding Agent Framework goes back through `agent-framework-fit-assessment-r1` (§10) |
| **Golden Set** | Exists; needs per-agent scoping | **Exists, global and per-capability.** CI "Eval Gate (Golden Utterances)" in `.github/workflows/ci-tier1-blocking.yml:674–702` (`Category=GoldenUtteranceEval`); 15 suites in `tests/integration/contract/Eval/*`, including **`NdaReviewDispatchEvalTests`**; `tests/unit/Sprk.Bff.Api.Tests/Eval/PredictMatterCostEvalHarnessTests.cs`. Accelerator NDA eval corpus (4 documents + expected outputs) on the accelerator branch | No per-agent or per-owner golden-set concept, and no data-driven registry: suites are code | Per-agent golden set as data (owner-promotable cases), runnable on a profile diff outside CI. Seed from the accelerator's per-practice eval sets (`practices/<p>/evals`, rubric in its §13) and keep its **cross-surface gate** (§6.5 there: the same corpus through Cowork and the Spaarke Action must agree), rather than a second fixture set |
| **Run Ledger** | Exists (session ledger), Dataverse + Cosmos | **Exists, but session-scoped and erasable.** `Models/Ai/Chat/SessionLedgerEntries.cs:32` (ADR-040): `SessionOutput`, `SessionToolChain`, `SessionToolCall`, `SessionWidgetEvent`, `SessionGate`, `SessionContextFingerprint`. Redis hot, Cosmos warm (`Services/Ai/Sessions/StoredLedgerEntries.cs`), Dataverse cold | No profile-version or reviewer-action fields found. ADR-040 makes the ledger **Tier 3, GDPR-erasable, per session**. That is not the synopsis's "immutable, exportable for a regulator" record (T-3) | Profile version + owner + reviewer action on every run; a durable record (the Decision Record?) that does not depend on session erasure |

**Declarative agents in the repo.** On master there is one: `src/solutions/CopilotAgent/declarativeAgent.json` ("Spaarke AI", general legal ops, no NDA content). The NDA Review / Legal Review / Legal Hold Review agents and the `nda-review` / `nda-library-review` / `legal-hold-review` skills seen in the tenant's Plugins page were built on the accelerator r1 branch (`src/accelerators/`, not merged), §9.

## 7. Evidence already collected — mapped to the synopsis's `[VALIDATION NEEDED]` items `[Tested]`

Source: `projects/spaarkeai-mcp-bff-api-r1/notes/phase0-results.md` on branch `work/spaarkeai-mcp-bff-api-r1` (PR #2587). The tests ran against a **federated MCP connector** ("Spaarke MCP"), not a declarative agent. Where the synopsis's question is about a declarative agent, the result is a strong indication, not an answer. The test user was a licensed non-admin.

| Synopsis item | Evidence | What it means for this program |
|---|---|---|
| Profile "loaded by the first tool call"; "whether Cowork reliably follows a profile returned by a tool" | **T7 ❌**: MCP server `instructions` were **not honoured** in Chat or in Cowork (the tool got `instructions check` with no required prefix). | A **warning sign** for the central mechanism. Copilot ignored server-level guidance it is supposed to read. A profile returned as a tool *result* is a different channel and is untested, but the prior is poor. This is the main reason G1 (§8) is a go/no-go. mcp-r1's rule: put guidance in tool descriptions, not `instructions`. |
| "Widget-to-tool callbacks `[VALIDATION NEEDED]`" (tuning buttons) | **T1 ✅** Chat: the widget's button made an app-initiated `callServerTool` round trip. **Cowork ✅**: renders after the user switches the plugin on once under Customize (it is off by default in Cowork, on in Chat). A later widget in the same Cowork task collapses the earlier frame to 0×0. | Tuning buttons as MCP Apps widgets work in both surfaces. "Buttons on every output" in Cowork will hide earlier outputs' buttons, so design one live card per task there. Size budget: aim < 1 MB, 3 MB fail (paint delay grows with size). |
| "Tuning buttons … Cowork support `[VALIDATION NEEDED]`"; "whether Cowork renders widgets" | **T1 Cowork ✅**, as above. | Answered. Remove from Phase 0. |
| Agents also work in Teams, Outlook, Word | **T1 Word ⚠️**: the tool runs only after an explicit **+ › Choose plugins** pick, with Word's own approval card **even for read-only tools**; no widget. **T1 Outlook ❌**: the connector is not offered at all. Teams not tested. | The synopsis's "also Teams, Outlook, Word" does not hold for the connector today. Whether a *declarative agent* with an MCP action reaches Word or Outlook is untested (G4). Tuning widgets are Chat- and Cowork-only for now. |
| "Read permissive, write gated … deterministic confirmation gate" | **T3**: a widget calling an app-only write tool ran with **no host confirmation card**, in Chat and in Cowork. **T4**: a model-initiated write gets a per-call host card with raw parameters, and the user can pick **Always allow** (per tool, Cowork). The model also once *claimed* a write it never made. **T12**: tools without annotations are treated as writes; set `readOnlyHint` explicitly. | **The host's card is not a governance gate.** It is skipped for widget writes and can be permanently waived by the user. Every gate the ratchet promises must be **Spaarke's own, enforced server-side**, and the ledger, not the model's reply, is the record of what happened. |
| `@`-mention routing among legal agents; "decide how our specialists are named and routed when several legal agents share one Copilot" | **E5 ❌**: a connector **cannot be `@`-mentioned**. Declarative and Copilot Studio agents can (the "Spaarke Dataverse MCP" agent appeared in the `@` picker). **E1/E2 ✅**: skills appear under `/` in Chat and Cowork. Harvey and Legora sit in the same pickers. | Each archetype needs a **declarative agent** as its addressable front door (supports Path B for routing). The connector alone is reached only by naming the task (T6), by Sources (E3) or by a skill under `/`. |
| Tool scoping: "the profile whitelists them" | **T6**: Copilot called our tool for **13/15 (87%)** of domain prompts that never mention Spaarke, and 0/5 controls. **T6b**: switching the Spaarke MCP source **off does not stop calls** (5/5 still called); the switch resets on every new chat. | Our tools can be called **outside any specialist agent** and against the user's apparent choice. The server cannot assume a call came through an archetype's agent. Authority and profile must be resolved per call from identity plus an explicit agent or route signal (G3). Tool descriptions must keep unrelated prompts away (0/5 controls is the bar). |
| "Each user needs a Microsoft 365 Copilot licence" | **T11 ⚠️** (informal): with only Copilot Chat (Basic) the connector is not offered; with the licence and a new chat it is. | Confirmed as a precondition. It goes in licensing (D-A5) and user setup. |
| "Chat runs execute under the attorney's own delegated identity" | **T5 ✅**: OAuth via the federated connector, then OBO as the caller (`oid` = the user; WhoAmI returned the user's own `systemuser`). **T0 ✅**: the server is stateless (no SSE; GET 405). | Delegated identity is proven, so profile resolution from `oid` is feasible. But the server is **stateless**: there is no built-in "run" or "conversation" boundary on which to compile and pin a profile "once per run" (G3). |
| Work IQ context alongside our tools (tuning signals, cold start) | **T9 ✅**: Work IQ mail search and our tool ran in the same turn. | Composition works in Chat. Work IQ as a *scheduled* signal without the attorney present stays `[Verify]` (Phase 2, not Phase 0). |
| "Whether one package can hold several agents"; core plus practice plugins | **T2/T14, run 2026-11-23** (package with two connectors, one OAuth registration, deployed through admin center Agents > Tools > Registry). **Cowork ✅**: both connectors listed and run, the widget renders, and one sign-in connects both. But with the Dev Portal connector still on, **both** registrations answered one prompt (two widgets). **Chat ❌**: calls through the package connector failed before reaching the BFF (T13 live). Cowork shows the **plugin** name, not the connector name. | **Packaging works in Cowork and fails in Chat today.** Chat is the area agent's home, so this is a live risk for G2. Retire the duplicate Dev Portal registration before any user test. Still untested: a declarative agent in the same package, several agents, plugin-to-plugin dependency (G2). |
| Route per area (`/mcp/{area}`) and display name on cards | **T13 🔲**: local pass only (each route lists only its own tools); live test pending. | A per-archetype or per-area route is the cleanest "which agent called" signal for G3. It depends on T13 going live. |
| Skill naming a connector tool | **T10 🔲**: not run. | Needed for "write each procedure once, package as agent component and Cowork skill" (G2). |
| Cowork credit cost per task | Accelerator r1 POC (2026-11-06): `nda-library-review` used **185 credits** (375/384 topic cells), against Microsoft `compare-key-terms` at 259 and `review-contract` at 449. | First real data point for D-A5. It is for a file-based skill, not the connector path. |
| Cowork invoking a declarative agent | Ignite: Frontier private preview (synopsis). Not tested. | Do not depend on it (unchanged). |
| Events invoking a declarative agent; Copilot harness agents publishable into Chat; Copilot evals covering declarative agents; admin pin or pre-enable for a group; Work IQ listen status | Not tested. Only partial signal: the Cowork plugin was **off by default** for the user and the Chat source **on by default** (T1, T6b). | Stay `[Verify]`. None is a Phase 0 go/no-go. They belong to Phases 2–3. |

## 8. What Phase 0 still has to prove

Phase 0 covers only what §7 leaves open. Each item names its pass condition. G1 is the go/no-go for the program's runtime shape.

**G1 — Profile adherence (GO/NO-GO).** Does a declarative agent reliably follow an attorney profile, or must the profile drive server-side execution while Copilot only routes?
- *Setup:* an NDA declarative agent; two owner profiles that differ on at least five NDA positions (for example term, residuals, non-solicit, governing law, uncapped indemnity flag); the accelerator NDA corpus plus about ten more NDAs.
- *Variants:*
  - (a) profile returned in the first tool result and the model applies it;
  - (b) archetype-level guidance in the agent's instructions (≤ 8,000 chars, static) plus the per-owner profile from a tool;
  - (c) **server-side**: a single `spaarke_review_nda`-style tool runs the review in the BFF with the compiled profile (reusing NDA-REVIEW, §9), and Copilot routes the request and renders the result.
- *Measure,* in Chat and Cowork: the share of position decisions that match the active profile; cross-profile bleed (owner A's position applied for owner B); a ratchet probe (a user prompt asks the agent to skip a gate or widen authority).
- *Pass for (a)/(b):* at least 95% position adherence on both surfaces and zero ratchet escapes; otherwise (c) is the runtime. Thresholds are to be confirmed in the Phase 0 spec.
- *Expected, given T7:* (c) wins. In that case the declarative agent is a thin router and renderer, and "follows the profile" becomes "routes to the profile-driven tool".

**G2 — One package: declarative agent + connector + skills.** Extends T2/T14 (connectors + skills) and T10 (a skill naming a connector tool).
- *Pass:* one app package installs a declarative agent whose action is our connector, plus at least one `SKILL.md`, and both work in Chat.
- *Depends on G8:*
  - If an agent can use the tenant connector, the package carries the agent and skills only, with no connector of its own (copilot-platform-coordination R1/R2).
  - If it cannot, every persona agent needs its own registration of the MCP URL. That breaks R1 and must go to the owner before Phase 1. The Cowork side is a skill + connector (Cowork cannot call agents).
- *Also record:* several agents in one package, and whether a practice plugin can depend on the core plugin. Both are informational and decide packaging, not go/no-go.

**G3 — Profile resolution from the caller's identity on the first tool call.**
- *Pass:* with a stateless server (T0), a tool call resolves `oid` → `systemuser` → Agent Instance → compiled profile version within the tool-call latency budget, and the same version is used for every call that belongs to one run.
- *Must establish:*
  - what Copilot sends that can key a "run" or conversation (a conversation id header, or nothing);
  - whether the server can tell **which agent** invoked the tool (per-archetype route via T13, or a header), because T6/T6b show calls also arrive from outside the agent;
  - fail-closed behaviour when no Agent Instance exists (platform defaults only, never another owner's profile).

**G4 — Declarative agent reach in Word and Outlook.** T1 tested only the bare connector there. mcp-r1's T1-Word run is the same run as the accelerator's WP-3. The accelerator's WP-0 to WP-2 (does Word load a third-party skill's instructions, and does it run its scripts) have not run yet. Reuse their results when they land rather than re-testing. *Record:* whether a declarative agent with our MCP action is addressable and can call tools in Word and Outlook, and whether widgets render. This is informational and sets the surface list for Phase 1.

**G5 — Function routing inside a persona agent (§3.9).** Can one area-level declarative agent route each request to the right function skill or tool?
- *Setup:* one agent with at least three functions (for example NDA review, agreement review, legal hold review).
- *Measure:* mixed prompts plus out-of-scope controls, in Chat.
- *Pass:* at least 95% correct routing, and no function invoked for the controls (threshold to confirm in the Phase 0 spec).
- *If it fails:* the front door falls back to one agent per function, with the persona kept server-side as profile only.
- *Prior art:* *Spaarke Legal Review*, the accelerator's two-function agent (NDA and legal hold; §3.9), already has this shape but was never measured. Use it as G5's baseline: run the G5 prompt set against it first. Confirm its instruction version first. Any re-upload or copy needs an inventory row (§10, copilot-platform-coordination R4).
- This decides D-A12's delivery shape, not the go/no-go.

**G6 — Coexistence with Microsoft's legal plugins (§3.10).** With Legal for Cowork on, run the persona agent and Spaarke skills together. **G6 subsumes the accelerator's T-P2** (agreed 2026-11-23; T-P2 has not been run). Start from its prompt set (accelerator `notes/component-protocol.md`, row T-P2: NDA → Spaarke, MSA → Microsoft, redline summary → Microsoft; 5 prompts × 2 configurations), extend it, and send the results to the accelerator. T-14 (admin deployment) stays with the accelerator as its I-1.
- *Measure:* the share of prompts that reach the intended owner (Spaarke or Microsoft) in Chat and Cowork, and whether a Spaarke declarative agent can invoke or hand off to a Microsoft skill (`review-contract`, `summarize-redlines`).
- *Also check* that an exported attorney playbook is honoured by Microsoft's `review-contract` and by Word's Run Review.
- *Pass:* no Spaarke function captures a Microsoft-owned task, and no Microsoft skill captures a Spaarke-owned one, across the shared prompt set.
- This decides whether personas compose Microsoft functions directly or only recommend them (D-A13).

**G8 — Can a declarative agent use the tenant connector, or only its own actions?** This is copilot-platform-coordination **S-3** (its `notes/copilot-structure.md` §4), adopted 2026-11-23. Learn says an agent's MCP actions are private to that agent. It does not say whether an agent can use a federated tenant connector ("Spaarke MCP").
- *Setup:*
  - Build one test agent in Agent Builder, and the same agent packaged.
  - Add "Spaarke MCP" as a source, with **no MCP action of its own**.
  - Ask it to call `spike_echo`, then one read tool.
  - Run it in Chat, plus Word and Teams where it is offered.
- *Record:*
  - whether the tool runs, and under which identity (App Insights `tools/list` and `tools/call` against the one registration);
  - whether the widget renders.
- *Decides:*
  - G2's package shape;
  - whether persona agents (and "Spaarke AI") can obey R1 (one registration per MCP URL);
  - together with S-2 (= mcp-r1 T10), whether rule R2 (packages carry skills and agents only) holds.
- *Run early:* it is cheap, and G2 waits on it.
- *Before the test agent is created:* send its inventory row (owner project, removal plan, removal date) to copilot-platform-coordination (R4, §10).

**Code inventory follow-ups (no spike; Phase 0 desk work):**
- **I-1 Tool surface:** which surface the profile governs (MCP catalog vs `IAnalysisToolHandler` vs `IAiToolHandler`), and whether the chat Confirmation Gate (`SideEffectGateAIFunction`) also applies on the MCP path.
- **I-2 Gate policy input:** can `RequiresConfirmation` take a policy argument (the ratchet) without forking the one gate, given ADR-039 "one dispatch protocol".
- **I-3 Durable run record:** the Decision Record vs the ADR-040 session ledger for profile-version, owner and reviewer attribution (T-3).
- **I-4 Per-user knowledge trimming:** what UAC provides today, given that `RagService` trims by tenant and parent only.

**Already answered, so out of Phase 0:** widget callbacks; widgets in Cowork; host confirmation for widget writes; the licence requirement; `@`-mention of a connector; tool-call rate without "Spaarke"; delegated OBO identity.

## 9. NDA archetype — what already exists to reuse

| Asset | Where | Use in Phase 1 |
|---|---|---|
| NDA positions (`baseline-mutual.md`, "Spaarke NDA – Mutual v1.2") | `src/accelerators/practices/nda/positions/` on `work/spaarke-legal-ai-accelerator-r1` (PR #2528, not merged) | Seed for the L1 archetype positions. They are test positions, **not attorney-approved** (`accelerator.yaml` `status: mvp-test`) |
| NDA eval corpus (4 documents incl. a counterparty markup and an **injection** case) + expected outputs and golden plan | `src/accelerators/practices/nda/evals/` (same branch) | Seed for the NDA golden set and the G1 test bed; the injection case seeds the ratchet probe. The corpus is synthetic (Bluefin Harbor), and expected output exists for nda-01 only. Never put `answer-key/` or the excluded real NDAs anywhere an agent can read (§10.1 Q4) |
| `nda-review`, `nda-library-review`, `accelerator-setup` skills; `nda-review.instructions.md` agent instructions | `src/accelerators/practices/nda/`, `src/accelerators/core/` (same branch) | `accelerator-setup` "records the attorney's defaults and runs a trial review". That is an early **L3 owner profile and tailoring interview**, so align rather than duplicate |
| NDA-REVIEW Action (`nda-review.action.json`, Reasoning tier, advisory output, closed schema `{overallRisk, flaggedSections[]}`), KNW-011 grounding standard, Compose single-surface UX, model-tier picker | `ai-advanced-capabilities-nda-r1` (COMPLETE, deployed to dev 2026-07-28, archived) | Server-side execution path for G1 variant (c). The profile would parameterise this Action rather than a new pipeline |
| `NdaReviewDispatchEvalTests` | `tests/integration/contract/Eval/` (master) | Existing NDA dispatch eval to extend per profile |
| Accelerator four-tier decision (2026-11-19): advanced tiers include declarative agents (Agent Builder, shared, admin-deployed) | accelerator r1 `design.md` owner decisions | **Ownership overlap**: whose NDA declarative agent is it? Coordinate before Phase 1 (§10) |

## 10. Coordination points

| Project | Overlap | What we need, or must not break |
|---|---|---|
| **spaarke-legal-ai-accelerator-r1** (worktree `spaarke-wt-spaarke-legal-ai-accelerator-r1`, PR #2528, Desktop session `acclerator-r1`) | **Source of every existing NDA capability** and of the Microsoft-legal research; see **§10.1**, which is a gate for this program | See §10.1 |
| **spaarke-legal-ai-accelerator-claude-r2** | Claude for Legal research (practice plugins, tailoring interviews, watcher templates) | Synopsis's "borrow and benchmark" item. Check licence terms and quality there, not here |
| **spaarkeai-mcp-bff-api-r1** (PR #2587) | The single `/mcp/{area}` connector host; **ADR-028 A10** connector auth (OBO as caller, `systemuser` only); **task 040** plugin package + skill (held on T2/T14/T10); naming O-25; T13 route-per-area; widget budget | All specialist tools go on that host. No second MCP server (the synopsis also wants the two existing entries consolidated). Phase 0 G2/G3 build on 040 and T13 rather than duplicating them. Record T2/T14 from mcp-r1's results |
| **ai-spaarke-action-engine-r1** (ON HOLD, Issue #435) | Archetype = Template, Agent Instance = Action (synopsis) | Its resumption trigger (redesign-r1 G-P3) has passed and it waits on an owner go. D-A9: resume it, or let Phase 1 define Archetype independently and have the Action Engine adopt it |
| **ai-spaarke-insights-engine-r1 / r2 / r3** (r1 design complete, r2 complete, r3 paused) | Fact / Observation / Inference taxonomy = the honesty contract (epistemic class per output) | Consume the taxonomy; do not re-derive facts. D-F0 deterministic facts first |
| **ai-advanced-capabilities-nda-r1** (COMPLETE) | NDA-REVIEW Action, ADR-039 advisory mode, NDA eval | Reuse as the server-side NDA executor (§9) |
| **agent-framework-fit-assessment-r1** (COMPLETE, `docs/assessments/agent-framework-fit-assessment-2026-06-03.md`) | Supervision protocol "on Microsoft Agent Framework"; the BFF removed `Microsoft.Agents.AI` (#1027) | Any re-adoption (Phase 4) cites that assessment and passes BFF hygiene §10 (publish-size delta, CVE) |
| **unified-access-control-r2** (Issue #808, in progress) | **Entra Agent ID** for background agents and any ADR-028 change; ethical walls and scope trimming (L2); per-user knowledge trimming (I-4) | Auth ADR amendments go through its owners. Ethical walls build on its access model, not a parallel one |
| spaarke-ai-architecture-redesign-r1 / r2 (r1 COMPLETE) | One confirmation gate, Binding capability model, ADR-039 | The profile feeds the existing gate and catalogs. No second gate or dispatch path |
| **copilot-platform-coordination** (Issue #3006, Epic #421, branch `work/copilot-platform-coordination`, session "copilot-platform-coordination"; set up 2026-11-23) | **The single register of Copilot tenant artifacts:** `notes/tenant-inventory.md`, `notes/copilot-registration-rules.md` (DRAFT), `notes/coordination-map.md`. Its inventory lists the existing NDA agents D2–D4 as **REPLACE, pending D-A11** | We follow its rules (peer coordination, not owner approval):<br>**R1.** One tenant registration per MCP URL, so our persona-agent packages carry **no second connector**. This matches the duplicate-registration double-fire in §7.<br>**R2.** Packages carry skills and agents only. This is **open** until S-2 (= mcp-r1 T10) and S-3 (= our **G8**) are run.<br>**R3.** Names follow `spaarke-{area}-{capability}`.<br>**R4.** Every new tenant artifact, **Phase 0 G1–G7 test agents included**, gets an inventory row (owner project, removal plan) **before upload**. We send the row to that session first.<br>**R5.** Throwaway artifacts get a removal date.<br>This program has no tenant artifacts yet. |

### 10.1 Alignment with spaarke-legal-ai-accelerator-r1 (gate A0, before Phase 0)

**Status: reviewed by the accelerator session on 2026-11-23** (at `c255c9458b`). It confirmed the tier split and the adopt list, sent corrections C1–C5 (folded in) and answered Q1–Q8 (below). **Gate A0 now waits only on the owner decisions listed under "Still open".** The accelerator is where the NDA capability, the Microsoft-legal research and the content model already live. This program is its **T2/T3 continuation**, not a parallel effort. The detailed brief is [notes/accelerator-alignment-2026-11-23.md](notes/accelerator-alignment-2026-11-23.md).

**How the two fit (proposed).** The accelerator's strategy defines four tiers:

| Tier | Who sets it up | Runtime | Owner |
|---|---|---|---|
| T0 individual, T1 team: **all four package tiers** (individual base/advanced, team without IT / **with IT**) | Attorney, practice team, or IT deploying the plugin and packaged agents | Files, skills and (advanced tiers) declarative agents on Microsoft's runtime; **no Spaarke connector** | Accelerator |
| T2 firm or department: **starts where the Spaarke connector enters** | Customer IT + Spaarke | Admin-deployed package + the **Spaarke connector** | Shared: accelerator content, this program's runtime and connector tools |
| T3 Spaarke platform | Spaarke engagement | The platform: Policy versions, matters, Decision Record | **This program** (with ontology) |

This program is the governed runtime behind T2 and T3. The attorney profile, positions, functions and evals are **the same content at every tier**, rendered for each surface (accelerator §6 "one content model, three surfaces").

**What this program adopts from the accelerator, instead of designing anew:**

| Accelerator asset | Where it lives | Use here |
|---|---|---|
| Microsoft-legal boundary research, the 20-skill inventory, "Microsoft first" non-overlap rules, skill design rules, the name-collision lint | design §2.4, §5.2a, §5.7; `notes/legal-for-cowork-skills/` | §3.10 adopts them program-wide |
| One content model: `topics.yaml` topic IDs, legal playbook format, **review-result schema**, version stamp, **one customer master** | design §6, §5.3 | Positions (§6 row) and run outputs use them. The synopsis's "Position Library" = topic-keyed positions in this model |
| **Attorney profile** `Documents/Cowork/Profile/legal-profile.md` (practice, usual side, posture, defaults), written by `accelerator-setup` | design §5.3, §8.1a | The **seed of the L3 owner profile**. At T3 Spaarke holds the master and exports this file, so T0/T1 skills read the same profile |
| Onboarding `accelerator-setup` (role, practice, side, toggle set per role) | design §8.1a | The tailoring interview for an Agent Instance. Note the accelerator already gives a **recommended toggle set per role**: a persona idea at T0/T1 |
| **Specialist reviewers** (§5.8a): specialist panel inside a skill (T0/T1); **specialist as a server-side agent** via the connector (T2) | design §5.8a | The T2 pattern *is* this program's runtime. The specialist roster files become archetype content (§3.9) |
| NDA practice: positions, eval corpus (incl. an injection case) + rubric, `nda-review` / `nda-library-review` / `accelerator-setup` skills, engines (`ooxml_redline` etc.) | `src/accelerators/practices/nda/`, `core/` | Phase 1 NDA content and golden-set seed (§9) |
| **Skills first** (owner, 2026-11-12): agents review in chat only, never build redlines, point to the skill; enforced by the package build (`package_docs.recipe_problems()`) | `notes/copilot-app-components.md` | **The principle underneath applies at every tier** (Q6): the model decides, a **tested engine** writes the redline, never a model-written redline. At T2/T3 the BFF engine is that engine (G1 variant c), tested on the same golden fixtures and review-result schema. **This program needs its own equivalent of the recipe gate**: no agent instruction tells the agent to write tracked changes itself |
| Four-tier package (2026-11-19): agents only in the advanced tiers; the team-with-IT tier has admin-deployed packaged agents (manifest v1.9, playbook as agent skill or knowledge, no connector; I-2/I-3 untested) | `notes/accelerator-components-by-surface.md`, `Build-SpikePackages.ps1` | **The area agent is the successor** (Q1): once a customer has the connector, the area agent replaces the packaged agent. Both read the same positions and topic IDs |
| Naming: plugin `spaarke-{area}`, skills `spaarke-{area}-{capability}`, tools `spaarke_{verb}_{object}`, build gate `^spaarke(-[a-z0-9]+)+$` | OD-20/OD-42, shared with mcp-r1 | Adopted |
| T2 connector tool list (`spaarke_find_documents`, `spaarke_get_document`, `spaarke_log_analysis` taking the review-result schema, `spaarke_matter_card`, …) | design §10 | `spaarke_log_analysis` is the natural input to this program's run record (§6 Run Ledger, T-3) |

**Answers from the accelerator session (2026-11-23).** These are the accelerator's positions. Owner decisions are marked as such.

| # | Question | Answer | Status |
|---|---|---|---|
| Q1 | Is the team-with-IT packaged agent the same as the area agent? Who owns the NDA agent? | **Different things, same lineage.** The packaged agent is a T0/T1 declarative agent with no connector. The area agent is a declarative agent + MCP action at T2. The area agent is the successor. **Proposed ownership:** the accelerator owns the T0/T1 NDA agent content (instructions v0.3 `practices/nda/agent/nda-review.instructions.md`, recipes, recipe gate); this program owns the area agent | **Owner decides** (D-A11) |
| Q2 | Is the T3 positions master Policy versions? Is L3 ⊇ `legal-profile.md`? | **Agree in principle** that T3 holds the master and exports. "Policy versions" is ontology §3 terminology, so **confirm with ontology-r1**. Today the master is the Markdown playbook (`practices/*/positions/*.md`), validated against `core/schemas/legal-playbook.schema.json` + `topics.yaml`, and rendered to the four-column Word playbook, the readable `.docx` and the Cowork master. **L3 ⊇ `legal-profile.md`: yes** | Open: ontology confirmation; profile round-trip (D-A15) |
| Q3 | Written schemas? | **Partly.** Written: `core/schemas/legal-playbook.schema.json`, `review-result.schema.json`, `edit-plan.schema.json`. `topics.yaml` has no standalone schema (it is checked by build code and the topic-ID pattern / `PRACTICE_PREFIXES` in `gates.py`). `legal-profile.md` has no schema, only the template `core/skills/accelerator-setup/references/profile-template.md` (prose fields). Version against the template's field list now; **add a schema before any T3 export** | Phase 1 prerequisite |
| Q4 | Can the NDA evals seed the golden set, and what must stay out? | **Yes, with limits.** `practices/nda/evals/` is synthetic (Bluefin Harbor: 3 NDAs incl. counterparty markup and injection, plus 1 MSA routing control). Expected output exists for **nda-01 only** (`expected/nda-01-*.md` + golden plan). The positions are **test positions, not attorney-approved**. **Off-limits:** `notes/poc-e2e/answer-key/` must never go anywhere Copilot or an agent can read (OneDrive, SharePoint, any BFF retrieval index); it is the blind key for owner tests. A test-only fixture outside retrieval is acceptable. The Corteva NDA and the real RPX/Helios NDA never go in the repo or the BFF. **Reuse the gate regex `FORBIDDEN_IN_PACKAGE`** (`answer-key\|corteva\|rpx\|helios`) from `gates.py` | Constraint for Phase 0/1 |
| Q5 | T-P2 / T-14 status? | Neither has run; only S9. **G6 subsumes T-P2** (it starts from the T-P2 prompt set and sends results to the accelerator). T-14 (admin deployment) stays with the accelerator as I-1 | Agreed (§8 G6) |
| Q6 | Does skills-first extend to T2/T3? | **Yes, as the principle underneath:** a tested engine writes the redline, never the model. An agent calling a BFF redline tool complies. This program needs its own recipe-gate equivalent | Agreed (adopt row above) |
| Q7 | Who keeps the Microsoft catalogue snapshot? Shared lint? | K3 `knowledge/copilot-for-legal/` **does not exist yet.** The current snapshot is accelerator `notes/legal-for-cowork-skills/` (20 skills, v1.0.10, 2026-11-05) on the unmerged branch. **Proposal:** move it to a shared `knowledge/copilot-for-legal/` with a machine-readable list, and have the one lint in `gates.py` read it. The accelerator refreshes it until its R1 closes, then this program does | **Owner decides** (who refreshes, location) |
| Q8 | One roadmap? | **Yes, the owner's call.** One table: the accelerator's four package tiers + T-P2/T-14 + this program's Phase 0–N, with the hand-off point "connector available → area agent replaces packaged agent". Do not source it from the accelerator design body, which is not yet revised to the four tiers | **Owner decides** (D-A16) |

**Still open before Phase 0 (gate A0):**
- D-A11: NDA agent ownership split (as proposed in Q1).
- Q7: catalogue owner and location.
- D-A16: one roadmap.
- Q2: ontology-r1 confirms the "Policy versions" term.

The profile round-trip (D-A15) and the `topics.yaml` and profile schemas (Q3) are Phase 1 prerequisites, not A0 blockers.

## 11. ADR tensions (root CLAUDE.md §6.5)

The paths are proposals. The human chooses the path for each tension before the affected phase's spec.

| # | ADR and rule | Conflict | Proposed path | Rationale | Alternatives rejected |
|---|---|---|---|---|---|
| **T-1** | **ADR-052** "MUST NOT let the caller's token (OBO, user tokens) leave the BFF request that holds it"; impersonation per **ADR-028 A5** only "for work that user started through an authenticated BFF request — never timer, webhook or system-triggered work" | Synopsis §Autonomous runs: "Start by running as the delegated attorney" for a Graph-notification-triggered NDA run. That is webhook-triggered work acting as a user | **C (comply)** for Phase 3: the background run is app-only (stamp MI), produces **draft-only** output, is attributed to the owning attorney in the ledger, and the attorney's own send is the write gate. Revisit with T-2 | Keeps ADR-052 intact; "prepare" (the default autonomy level) never needs the user's identity | Path A exception for impersonating on webhooks: weakens a fail-closed auth rule for convenience |
| **T-2** | **ADR-028** (A1–A10) has no provision for **Entra Agent ID** identities | Synopsis D-A2: background agents (Coach, autonomous NDA) get Entra Agent IDs | **B (amendment)**, owned with unified-access-control-r2, **before Phase 3**; not needed for Phases 0–2 | A new principal type in the auth model is exactly what ADR-028 governs; §6.5 bars bypassing auth ADRs without sign-off | Use the stamp MI forever: loses per-agent attribution and revocation, which is the synopsis's point |
| **T-3** | **ADR-040**: the session ledger is "Tier 3 (user-owned, GDPR-erasable, tenant-partitioned)" | Synopsis Run Ledger: "immutable record … exportable for a client or regulator inquiry" | **C**: keep the session ledger as is; write the durable attribution (profile version, owner, reviewer action, gate decisions) to the append-only **Decision Record**, if I-3 confirms it fits. Otherwise **B** | The erasure guarantee is a privacy commitment; durability belongs in a store designed for it | Making session entries non-erasable: breaks ADR-040/ADR-015 |
| **T-4** | **ADR-039**: one dispatch protocol, closed catalogs, "the LLM never invokes an unlisted tool"; the Text path is Spaarke's one bounded agent turn | Under Path B, Copilot's orchestrator is the probabilistic decider over our MCP catalog, and a profile "whitelists tools" | **C**: profiles may only **narrow** the closed catalog (never add); MCP tools stay cataloged; reuse mcp-r1's treatment of Copilot-as-caller | The ratchet is a narrowing operation by design, so it fits the closed catalog | Per-profile tool definitions: an open catalog |
| T-5 | **ADR-013** (extend the BFF), **ADR-004/036/052** (background work), **ADR-002** (no plugins; one owner per invariant) | None expected: the Coach is a Service Bus job; the ratchet invariant is owned by the BFF profile write path | **C** | Recorded so the specs cite it | — |
| T-6 | Not an ADR: the **#1027** removal of `Microsoft.Agents.AI` | Synopsis: backend orchestration and supervision on Microsoft Agent Framework | Decide in Phase 4 with the fit assessment; Phases 0–3 do not need it | — | — |

## 12. Open decisions

| ID | Decision | Leaning | Decided by |
|---|---|---|---|
| D-A1 | Declarative agent or custom engine agent as front door | Declarative agent (needed for `@`, E5); custom engine only on a failed requirement or eval | Synopsis; confirm after G1 |
| D-A2 | Identity for chat vs background | Delegated attorney for chat (T5 ✅); background per T-1/T-2 | ADR-028 owners |
| D-A3 | Profile storage | One versioned JSON document with indexed header fields | Phase 1 spec |
| D-A4 | Consent and privilege for correction harvesting | Owner-scoped, matter-restricted, opt-in per matter | Counsel + owner, Phase 2 |
| D-A5 | Per-agent pricing and Agent 365 licensing | `[Verify]` with Microsoft contacts; Copilot licence required (T11); Cowork 185 credits per library review (accelerator POC) | Owner |
| D-A6 | Firm-authored Copilot Studio agents as A2A peers | Defer to Phase 4 | — |
| **D-A7** | **Where profile application and gates run** | **Server-side** (G1 variant c) unless G1 shows ≥ 95% adherence for (a)/(b). Recommended by the mcp-r1 session from T7/T3/T6 | **Owner, at the G1 gate.** Not yet decided |
| D-A8 | Supervision protocol timing | Moves to Phase 4 with the patent associate (follows from OD-2) | Owner |
| D-A9 | Archetype vs Action Engine Template | Phase 1 defines Archetype minimally; the Action Engine adopts it if and when it resumes | Owner, with action-engine-r1 |
| D-A10 | What "Skill" means here | Both: the `SKILL.md` package component (shared with the accelerator) and server-side `sprk_analysisskill` prompt fragments; name them differently in the spec | Phase 0 spec |
| D-A11 | Ownership of the NDA declarative agent | This program owns the profile-driven NDA specialist; the accelerator keeps file-based tiers. To agree with accelerator r1 | Owner + accelerator r1 |
| **D-A13** | **Microsoft's legal skills (§3.10)** | Microsoft-first program-wide: Spaarke adds content (exported positions), data (connector) and governance where Microsoft owns a function; Spaarke-owned functions only where Microsoft has none (NDA packaged output, legal hold, intake, invoice review, IP); collision gate + catalogue snapshot; G6 decides whether personas compose or only recommend Microsoft skills | **Owner**, with accelerator r1 |
| **D-A14** | **Alignment gate A0 (§10.1)** | Accelerator review received 2026-11-23 (C1–C5 folded in; Q1–Q8 answered). Phase 0 waits on D-A11, D-A16, the Q7 catalogue owner and the ontology term check | **Owner** |
| D-A15 | Attorney profile round-trip: at T0/T1 the attorney edits `legal-profile.md` freely in prose (`[DEFAULT]` / `[PENDING]` markers). If T3 exports it, those edits need a way back into the Dataverse master, or the exported file must become read-only | Open. Raised by the accelerator (Q2). Decide in Phase 1 with the profile schema (Q3) | Owner, Phase 1 |
| D-A16 | One roadmap across the accelerator's package tiers and this program's phases (Q8) | One table, with the hand-off "connector available → area agent replaces packaged agent" | **Owner** |
| **D-A12** | **Framing: function, persona, or both (§3.9)** | Both, in layers: functions (skill + Action, with golden set and position schema) are the machinery; archetypes (persona) are configuration and the Chat front door; instances are per attorney. One agent per **practice area**, with role as a profile dimension; validate routing in G5 | **Owner.** Proposed 2026-11-23, not yet decided |

## 13. Market position `[Synopsis]`, summary

Harvey (Agent Builder, 500+ agents, in Copilot and Cowork since June 2026) uses the same delivery pattern. Legora (aOS, Word/Outlook add-ins, Copilot connection), CoCounsel (agentic, Westlaw-grounded) and Claude for Legal (Apache 2.0 plugins and tailoring interviews, not on Copilot) are the other reference points. AI-native firms sell outcomes, not software.

- **Our claimed difference:** per-attorney ownership with a one-way ratchet, tuning by tested diff, gates and walls enforced in tools, and delivery inside the customer's tenant.
- **Where we are behind:** licensed content, liability or insurance, and chained autonomy.

The full text, with sources, is in the source synopsis.
