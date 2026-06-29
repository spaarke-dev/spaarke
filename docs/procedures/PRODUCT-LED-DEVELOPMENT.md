# Product-Led Development at Spaarke

> **Status**: Adopted 2026-06-26 — first applied in `spaarkeai-widget-summarize-document-r1`
> **Audience**: Project authors, AI agents executing `/design-to-spec` and `/project-pipeline`, sales engineers reviewing scope, anyone scoping a new Spaarke project
> **Scope**: How we frame, name, scope, and graduate projects under the product-led approach. Captures concept + principles; specific procedures (scope checklists, productization criteria, graduation gates) will be refined as we run more projects under this model.

---

## 1. The concept in one paragraph

A **product-led project** is scoped around a single coherent user-facing capability that a salesperson can demo end-to-end. Its deliverable is a feature in a sales deck, not a technical milestone. Productization — UAT, documentation, sales training, demo polish — is in-scope by definition. Technical work (BFF endpoints, widgets, playbooks, Dataverse rows, infrastructure) is delivered as part of feature shipment, not as the project's purpose. The contrast: **platform-led projects** (`Rn` rounds, infrastructure rounds, refactor rounds) scope by technical milestone; **product-led projects** (`{surface}-{verb}-r#`) scope by demoable user capability.

---

## 2. Why we adopted this framing

Five reasons recorded during the 2026-06-26 strategic discussion that produced this approach:

1. **Sales drives the conversation.** Sales doesn't ask for "chat-routing-redesign-r2"; they ask for "show me summarization." Project names that match sales requests align the org around what's being sold, not what's being refactored.

2. **Force-ranks scope by user value.** Platform projects accumulate "while we're here, let's also fix X." Product-led projects force discipline: "does this make the demo better? If no, defer."

3. **Productization becomes in-scope by definition.** UAT, docs, demo flow, sales training, polish — all in scope. Platform-led projects historically treat these as afterthoughts that slip after the technical milestone "completes."

4. **The multiplier pattern emerges naturally.** When you scope "Draft an NDA," you naturally see "Draft a {X}" is the actual pattern — infrastructure decisions get made with that in mind. Platform-led framing misses this because it builds infrastructure speculatively.

5. **Spaarke's R1 chat-routing-redesign project was empirical evidence.** It was scoped technically ("redesign chat routing"). It built sophisticated dispatch + intent classification + routing tables + a 6-tier memory substrate — and shipped exactly **one** new LLM tool handler. The technical scope absorbed nearly all the budget. A product-led framing would have forced "we ship summarize end-to-end including the 4 specialized playbooks" and prevented the surface-thin outcome.

The frame is not an indictment of platform work — platform work matters and continues. It's a redirection of where the budget defaults: **demoable user verbs first, infrastructure as needed to support them.**

---

## 3. Project naming convention

```
{surface}-{verb}-r{round}
```

| Part | Meaning | Examples |
|---|---|---|
| `{surface}` | The Spaarke product surface this ships on | `spaarkeai-widget`, `outlook-addin`, `word-addin`, `pcf-dataset`, `teams-app`, `code-page` |
| `{verb}` | The user-facing capability — what the user does | `summarize-document`, `draft-document`, `intake-email`, `review-contract`, `track-matter` |
| `r{round}` | Iteration round (R1 = first ship, R2 = revisit) | `r1`, `r2`, `r3` |

### Examples

- `spaarkeai-widget-summarize-document-r1` — first round, in the SpaarkeAi shell, summarize-a-document capability
- `spaarkeai-widget-draft-document-r1` — follow-on drafting project
- `outlook-addin-intake-email-r1` — Outlook surface, matter intake from email
- `word-addin-redline-clause-r1` — Word surface, AI redlining (the strategic-bet project)
- `spaarkeai-widget-summarize-document-r2` — revisit summarize after r1 ships (add multi-doc, polish, etc.)

### Why drop the `P#` convention

`P1, P2, P3, ...` was an early naming proposal. We dropped it for two reasons:

- **Less self-describing.** "P1" tells you nothing about the surface or verb. `spaarkeai-widget-summarize-document-r1` tells you both at a glance.
- **The `r#` suffix gives us revisits naturally.** `P1-summarize-document-v1` would have needed `P1-summarize-document-v2` for revisits, which conflates "first product feature" with "first version of the first product feature." Cleaner: `spaarkeai-widget-summarize-document-r2`.

### Sequencing without numbering

We don't enforce a global sequence number. Projects ship as the business priorities demand. The order — what ships first, what's a strategic bet, what's deferred — lives in the portfolio backlog, not in the project name.

---

## 4. Principles

### 4.1 The demo is the deliverable

If a salesperson can't demo it end-to-end in 15 minutes with a real user persona (e.g., "Sarah, in-house counsel at MidCorp"), the project hasn't shipped. The demo script is a graduation artifact, not a post-launch nice-to-have.

### 4.2 Productize what's already built before building more

Spaarke has substantial infrastructure already shipped (JPS playbook framework, three-pane SpaarkeAi shell, ADR-030 v2 PaneEventBus, consumer routing, R6's 9 pillars, R1's chat-routing redesign). **A product-led project's first job is to surface that existing infrastructure as a coherent user capability.** Adding new infrastructure mid-project should require explicit justification — the default is "we ship what we already have, polished."

### 4.3 Multipliers — design for reuse, not speculation

When scoping, identify the **shared build items** (the multipliers) that will serve multiple future projects. Build them in this project but design them for the foreseeable next project's needs. Document the design constraints explicitly so future projects can inherit, not rewrite.

Example: in `spaarkeai-widget-summarize-document-r1`, the editable widget (Multiplier B) was scoped to handle rich text from day 1 — not because summarize needs rich text in r1, but because the follow-on drafting project (NDAs) does. Trivial to spec on day 1, expensive to retrofit.

### 4.4 Out-of-scope discipline

Each project lists what it explicitly does NOT ship and where each deferred item lives (follow-on project, strategic bet, indefinite defer). This protects the project from scope creep AND signals to sales which features are still TBD.

### 4.5 Sister-project dependencies are first-class

Infrastructure restoration (AI Search dev), platform fixes (Redis cache), and architectural cleanups (write-side unification) often run as their own focused projects. A product-led project lists them as named hard/soft dependencies with the sister-project owner identified.

### 4.6 Productization graduation has 4 tracks

A product-led project graduates when ALL four are true:

| Track | Concrete artifacts |
|---|---|
| **Functional** | All user verbs work end-to-end in dev; all FRs satisfied; all dependencies (e.g., new playbooks, Dataverse rows) deployed |
| **Quality** | Code review pass; ADR check pass; publish size within budget; test coverage; UAT regression script passes |
| **Productization** | 15-minute demo script written + recorded; user help doc shipped; sales team trained (1-hour walkthrough); observability dashboard live |
| **Dependency** | Sister projects required for graduation are complete OR waived with explicit acceptance |

A project that ships functional + quality but not productization has **not shipped** under this framing. The output is the product.

### 4.7 Decision criteria — combine vs split

When two related verbs are candidates for one project (e.g., summarize + draft):

- **Combine** when one shipped without the other looks incomplete to the buyer (e.g., "we summarize but you can't save the summary" — save is part of summarize, not a follow-on)
- **Split** when the buyer can see clear value in shipping one without the other AND the second can reuse 40%+ of the first's build items (multipliers) (e.g., summarize is demoable on its own; drafting follows naturally)

When in doubt: split. Ship the first, demo it, get sales reactions, then scope the second with empirical input.

---

## 5. How it works with existing skills

Product-led framing doesn't replace existing skills. It changes WHAT they're scoped against.

| Skill | Product-led behaviour |
|---|---|
| `design-to-spec` | Generates `spec.md` from a product-led `design.md`. Sections come out the same; the WHAT changes — FRs are user verbs, NFRs include productization criteria, "affected areas" includes UAT + docs + demo script |
| `project-pipeline` | Generates plan, CLAUDE.md, tasks. Wave decomposition reflects product-led ordering: surface completion → multipliers → polish → UAT → productization → wrap-up |
| `task-execute` | Unchanged — same rigor levels, same protocol. Tasks are smaller and more verb-shaped under product-led framing. |
| `code-review` / `adr-check` | Unchanged — quality gates at Step 9.5 still fire |
| `merge-to-master` | Unchanged — protected master + auto-merge PR pattern |
| `repo-cleanup` / wrap-up | Productization graduation criteria checked at wrap-up (not just functional + quality) |

A product-led project's `design.md` is the input to `/design-to-spec`. See the `spaarkeai-widget-summarize-document-r1` design as the working example.

---

## 6. What's intentionally left open for future refinement

This document captures the concept and principles. Specific procedures will be refined as we run more projects:

- **Productization graduation rubric** — currently 4 tracks at high level (§4.6). Concrete checklists per track (e.g., "what counts as 'demo script written'?") will be added after the first 1-2 product-led projects ship.
- **Multiplier identification pattern** — currently "look at the next 1-2 projects and what they'd reuse." A more rigorous pattern (perhaps a multiplier matrix added to design.md template) may emerge.
- **Portfolio-level sequencing doc** — a separate doc showing the active product-led pipeline (what's shipping, what's queued, what's deferred) may be useful. Out of scope for now; the GitHub portfolio board is the de facto pipeline view.
- **Scope-creep triggers** — when does scope expansion in-flight require respec vs accept? A heuristic will emerge with experience.
- **Sales-feedback loop** — how sales reactions to the first ship inform the second project's scope. Today informal; a formal pattern (e.g., post-ship sales survey, demo failure log) may be worth adopting.

Refinement happens by editing this document (single source of truth) when patterns crystallise — not by spawning more methodology docs.

---

## 7. What this is NOT

- **NOT a replacement for platform work.** Infrastructure projects (Redis, AI Search restoration, CI router) continue. They're sister projects; product-led projects depend on them.
- **NOT a quality gate substitute.** ADR-013, ADR-015, ADR-029, ADR-030 v2, ADR-032 all apply. CLAUDE.md §10 BFF hygiene applies. §11 Component justification applies.
- **NOT a license to ignore the architecture.** Product-led projects MUST respect the substrate (e.g., FR-45 invariant, ADR-030 v2 channel union, Playbook-as-Orchestration-Boundary per `ai-architecture-playbook-consumer-routing.md` §10.2).
- **NOT a sales pitch.** This is engineering scoping discipline. Sales benefits because the deliverable matches what they sell, but the rigor is the same.

---

## 8. Reference projects

| Project | Status | What it demonstrates |
|---|---|---|
| [`projects/spaarkeai-widget-summarize-document-r1/`](../../projects/spaarkeai-widget-summarize-document-r1/) | Design complete (2026-06-26) | First product-led project. Template for future projects. Multipliers (A, B, C, D), demo script, sister-project dependency mapping, R6/R1/R7 deferred-item folding, 4-track graduation criteria. |

Future product-led projects link here as they're created.

---

## Document changelog

| Date | Change |
|---|---|
| 2026-06-28 | Initial draft. Captures concept + principles from the 2026-06-26 strategic discussion that produced the `spaarkeai-widget-summarize-document-r1` project. |
