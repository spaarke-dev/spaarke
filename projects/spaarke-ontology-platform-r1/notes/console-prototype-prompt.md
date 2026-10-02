# Console worklist prototype — prompt for Claude Code

> ## ✅ STATUS: CONSUMED `[stamped 2026-10-01]`
>
> The prototype was built from this prompt and is at
> `c:\code_files\spaarke-prototype\projects\2026-10-spaarke-console\` (now at **v2.1**, round-2 review
> pending). **The prototype and its README — findings 1–17 — supersede this prompt.** Kept only as provenance
> for what was asked versus what the exercise returned; its README's *"Deliberate departures from the prompt"*
> section is the diff. Do not re-run this prompt.

> **Created** 2026-10-01 during design review of [`design.md`](../design.md) rev 3.
> **Audience**: a Claude Code session working in `c:\code_files\spaarke-prototype`.
> **Purpose**: paste §"The prompt" below into that session. Everything above it is provenance for us.

## Why this prototype exists (our framing, not part of the prompt)

Two things the design cannot settle on paper:

1. **D-3** — worklist surface (Console / MDA / both) and whether rows are **matters** or **communications**.
   This also gates the `sprk_signal` shape, because "does the worklist read signals or matters?" decides the
   subject column. Drawing the row is the cheapest way to find out.
2. **The one-archetype claim** — component model §6.1 asserts worklist, Daily Briefing and email triage are the
   *same widget* with a closed set of configurable dimensions (facts · policies · actions · grouping · narrative
   on/off). If three differently-shaped signals cannot render in one row component, that claim is false and we
   learn it now rather than after building `sprk_gridconfiguration` rows against it.

It also puts §0.3 under test in the place §0.3 was violated: the row's **prose**. The first cross-source
predicate's message claimed *"unreconciled against its budget"* over a predicate with no budget term. The row
sentence is where that lie is told, so the prototype must make the sentence derive visibly from the predicate.

**Not in the prototype**: policy authoring (an MDA form — synopsis §4.4), the Action Engine (out, decision 9),
any real Dataverse or BFF call.

---

## The prompt

```
Build a UX prototype of the **Spaarke Console worklist** — the surface that shows what the Legal Operations
Intelligence platform has flagged, and lets a user act on it.

## Where it goes

This repo (`c:\code_files\spaarke-prototype`), as a **standalone UX experiment**, not a production component
harness — the worklist row does not exist in production yet, so there is nothing to alias.

- New folder: `projects/2026-10-ontology-console-worklist/`
- Copy the setup of `projects/2026-07-communication-conversation-widget/` (self-contained Vite app: its own
  `package.json`, `vite.config.ts`, `tsconfig.json`, `index.html`, `src/`)
- Follow `claude.md` strictly: React + TS, **Fluent UI v9 only** (`@fluentui/react-components`,
  `@fluentui/react-icons`), Fluent tokens for every color, **light + dark both working**, no Tailwind / MUI /
  shadcn, no custom color or type system, CSS only for layout mechanics
- All data mocked in `src/mockData.ts`. No backend, no auth, no Dataverse
- `README.md` in the project folder following the house format: **Hypothesis** · **What this prototype
  demonstrates** · **Status** checklist

## Background you need

Source documents, if you want them (read-only; do not edit):
- `c:\code_files\spaarke-wt-spaarke-ontology-platform-r1\projects\spaarke-ontology-platform-r1\design.md`
- `.../notes/ontology-architecture-feedback.md` §4 — the row contract, quoted below
- `.../notes/ontology-component-model.md` §6.1 — the worklist archetype
- `.../notes/mvp-synopsis.md` §4.5 — Console hosting

The platform evaluates the customer's **own declared rules** against data it holds, writes a **flag** when one
is true, shows the flag in a worklist, lets a human act under a confirmation gate, and records what was
decided. Membership is computed by **rule evaluation**, not by a user's filter. That is the whole product.

The **Spaarke Console** is an existing three-pane app hosted as a web resource inside the Dataverse shell
(`navbar=off`, chrome-free, full width). Mock that context: a plausible three-pane shell with the worklist as a
widget in the main pane. Do not rebuild Spaarke's navigation in detail — the worklist is the subject.

## The row contract — binding, five requirements

1. **A row resolves to an object**, never to a text string.
2. **Membership and rank are deterministic.** The model writes the sentence; it never selects the queue or the
   order. There must be an answer to "why didn't this surface on Tuesday?"
3. **It carries evidence with provenance and freshness, separated by epistemic tier** (below).
4. **It offers at least one action that changes something.** No action, no signal.
5. **Acting writes a Decision Record and closes the flag. Dismissing writes one too, with a reason.**

### Epistemic rendering rules — these are the point, not polish

- A **Fact** is stated flatly. No hedging, no confidence number.
- An **Observation** is visibly an interpretation: it carries a confidence and a citation to the source passage,
  and is never phrased as established.
- A **stale or unbound source shows as a gap** — never as a zero, never silently omitted.
- The **policy code and version in force appear on the row**, not only in the record.

### 🔴 The sentence must not claim more than the rule tested

The row's one-line summary may only assert what the predicate actually read. A rule that reads a classified
communication and the absence of a budget revision may say *"a commitment was raised and the budget has not
been revised since."* It may **not** say "over budget", "unreconciled against its budget", or any variance
figure — even when a spend fact is displayed as evidence directly below it. Evidence shown ≠ comparison made.

Make this visible rather than merely obeyed: give each row a small disclosure ("Why this fired") that shows the
predicate's clauses and which evidence item satisfied each. A reader should be able to see that the sentence and
the clauses agree.

## Target row — render approximately this

```
⚠  Matter 4471 · Acme v. Northwind                          Budget · POL-BUDGET v3

   A commitment with financial consequence was raised on 12 Mar and the
   budget has not been revised since.

   ── Evidence ──────────────────────────────────────────────────────────
   Fact         $236,400 invoiced against a $200,000 budget (118%).
                6 invoices, INV-8830..8832 latest. Source: legaltracker,
                as of 4 hours ago.
   Observation  Email from R. Salazar (Northwind counsel), 12 Mar, classified
                Scope / budget change — "…three additional custodians…"
                confidence 0.94 · View thread ›
   Fact         No budget revision recorded since 12 Mar.

   ── Actions ───────────────────────────────────────────────────────────
   [Send budget inquiry]  [Revise budget]  [Approve variance]
   [Dismiss — reason required]

   Flagged by Budget Variance Policy v3 · in force since 1 Feb 2026
```

Use Fluent idiom for this — do not reproduce ASCII rules. Collapsed and expanded states are both needed: a
dense scannable list, expanding to the full evidence block.

## Three mock signals of DIFFERENT shapes — this is the real test

One row component must render all three. If it cannot without branching into three components, say so
explicitly in the README — that finding is more valuable than a pretty screen.

1. **Cross-source** (the one above): a communication classified *Scope / budget change* in the window **and**
   no budget revision in the window. Two sources. Evidence mixes Fact and Observation.
2. **Single-source threshold**: budget variance alone. Facts only, no Observation. Label it in the README as the
   *plumbing proof, not the pitch* — incumbents already do this.
3. **SLA / awaiting reply**: an Inquiry sent 9 days ago with no response, SLA breached. Its "evidence" is the
   state of an outstanding action, and its actions are escalate / extend / close.

Then: **one row with a gap** — a bound source that is stale or unreachable, rendering as a gap per the rules
above, not as a zero.

## Flows to build

- **Send budget inquiry** → a confirmation gate (this is a real architectural step: every action is
  human-confirmed, the human *is* the authority) → on confirm, the flag closes as `Acted` and a Decision Record
  row appears.
- **Dismiss** → reason **required**, from a short list plus free text → Decision Record written → flag closes
  as `Dismissed` → show that a suppression window has opened.
- **Suppression made visible**: a matter dismissed three times stops being raised. Show that state somewhere
  rather than leaving it implied.
- **Decision Record view** — reachable per matter. Append-only (no edit, no delete affordances anywhere). Each
  entry shows: what was proposed · the fact that triggered it · the policy code + version · who confirmed · the
  outcome. Both authorize **and** deny paths appear; a dismissal is a record, not an absence.

## States a flag must be able to show

`Open` · `Acted` · `Dismissed` · **`ConditionCleared`** (the predicate stopped holding on its own — the budget
was revised after all) · `Superseded` · `PolicyRetired`.

`ConditionCleared` matters disproportionately: nothing today closes a flag when the condition clears by itself,
which is how internal alerting surfaces rot. It must also be visually distinguishable from `Dismissed`, because
counting an auto-clear as a human dismissal makes the three-dismissal suppression rule wrong in both directions.

## Also show

- **Narrative on/off toggle.** With narrative on, the same rows render as a Daily-Briefing-style prose wrapper;
  off, as the bare work queue. Same data, same rows, one toggle. This is testing whether briefing and worklist
  really are one archetype.
- **Action rate per policy** — acted ÷ surfaced, somewhere unobtrusive (a policy chip, a header stat). Below
  roughly half, a policy is generating noise. It is a standing product metric, not a dashboard.

## Two open product questions the prototype should help answer — address in the README

1. **Are rows matters or communications?** Build whichever you find more defensible, then state the case. If
   one row = one matter, several flags on one matter must group somehow. If one row = one communication, the
   matter-level picture fragments. Show the consequence rather than asserting a preference.
2. **Console, model-driven app, or both?** The Console is the three-pane app; the MDA is where admins live. If
   the row needs something the MDA cannot give (or vice versa), name it.

## Explicitly not in scope

- Policy authoring UI — that is a model-driven form; Dataverse gives the form, versioning and audit for free
- Any real Dataverse / BFF / network call, or any production component import
- New widget archetypes beyond the worklist + its narrative variant. The configurable dimensions are a **closed
  set**: facts shown · policies evaluated · actions offered · grouping/sort · narrative on/off. Over-generalizing
  the UI is a known trap here
- Visual redesign of the Console shell itself

## Done when

- `npm run dev` serves it; `npm run build` is clean
- Light and dark both correct, no hardcoded colors
- All three signal shapes render through one row component (or the README says plainly why they cannot)
- Both flows complete end to end: act → gate → record → flag closed; dismiss → reason → record → suppression
- README carries the hypothesis, what it demonstrates, the D-3 recommendation with its reasoning, and anything
  you found that contradicts the design documents

Report what you built, and separately, what the exercise revealed about the design. The second part is the
deliverable we care most about.
```
