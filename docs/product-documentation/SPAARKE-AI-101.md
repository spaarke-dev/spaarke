# Spaarke AI 101 — A Product Overview

> A user-focused tour of what Spaarke AI is, how it works, and what to expect — with just enough technical insight to make the pieces click. This is an orientation, not a training manual.

---

## 1. What Spaarke AI is

Spaarke AI is an **AI assistant for legal operations** that lives inside your Spaarke workspace. You talk to it in plain language — *"create a follow-up task, assign it to me,"* *"draft a cover letter,"* *"summarize what's in this matter"* — and it does real work against your actual records, documents, and knowledge base.

Two things make it different from a generic chatbot:

- **It acts, it doesn't just talk.** It creates matters, tasks, and notes; drafts emails and documents; searches your firm's material; and updates records — and it tells you honestly what it did and didn't do.
- **It's grounded and governed.** Every capability it has is defined, cataloged, and permission-aware. It works from *your* documents and *your* data, and it asks for confirmation exactly when a human should be in the loop — no more, no less.

Think of it as a capable colleague who knows your firm's playbooks, has read the file in front of you, and never pretends a task is done when it isn't.

---

## 2. How it works (the 30-second mental model)

When you type a request, Spaarke AI runs a short, disciplined loop:

1. **Understands the request** in the context of where you are (which matter or document, who you are, what was said earlier in the conversation).
2. **Picks the right capability** from a catalog of things it's allowed to do — this is the routing step.
3. **Decides whether it needs to check with you** — a reversible, low-risk action just happens; a risky or irreversible one asks first.
4. **Does the work**, records what happened, and **shows you a result card** with links, next steps, and an undo where it applies.

The important idea: the assistant isn't improvising with unlimited power. It's selecting from a **defined set of capabilities**, each with its own instructions, inputs, and safety profile. That's what makes it both useful and trustworthy.

---

## 3. The core building blocks

These are the concepts worth knowing. You don't configure most of them day-to-day, but understanding them explains *why the assistant behaves the way it does*.

### Actions — *the things the assistant can do*
An **Action** is a single, well-defined capability: "create a matter," "draft correspondence," "summarize a document." Each Action carries its own **instructions** (how to do the job well), an **input schema** (what information it needs), and an **output shape** (what it produces). Most Actions are *prompted* (the AI reasons through them using a carefully authored instruction); some are *coded* (deterministic workflows for steps that must run exactly the same way every time).

> *Technical insight:* Actions are authored as **JPS — JSON Prompt Schema** — a structured definition of the instruction, the fields, and the guardrails. This keeps the AI's behavior consistent and reviewable instead of ad-hoc.

### Playbooks — *multi-step analysis, composed*
A **Playbook** chains several Actions together into a richer piece of work — for example, reading a document, extracting key terms, checking them against a standard, and producing a narrative summary. Playbooks are how Spaarke does sophisticated, multi-stage analysis while keeping each step defined and inspectable.

### Bindings — *how a request finds the right Action*
A **Binding** is the connection between *what you ask for in a given place* and *which Action runs*. It's the routing table of the platform: it says "when a user on the assistant surface asks to create a matter, use the CREATE-MATTER action, with this risk profile and this behavior." Bindings are the single source of truth for routing — the assistant never invents a capability that isn't bound.

> *Technical insight:* Bindings live in one governed table (there is exactly **one routing surface**). This is deliberate — it means every capability the assistant can reach is explicitly registered, catalog-controlled, and health-checked. No hidden or forked routes.

### Scopes & catalogs — *what knowledge and tools are in play*
**Scope settings** determine what the assistant can see and use for a given task: which **knowledge sources** (document collections) it searches, which **entity** it's working within (this matter, this project, this invoice), and which **tools** from the catalog are available. Scoping is what keeps a search focused on the right material instead of the whole tenant, and what keeps the assistant's toolset appropriate to the task.

### The judgment layer — *confirmation exactly when it matters*
Before any action that changes something, the assistant runs a **judgment check**. Every capability declares a **risk tier** and whether it's **reversible**. From that, the assistant decides:
- **Low-risk, reversible, clearly asked** → it just does it (with an undo).
- **Ambiguous or incomplete** → it asks a clarifying question rather than guessing.
- **Irreversible or sensitive** → it confirms once, cleanly, then acts.
- **Not allowed / would fail** → it stops honestly and explains, offering a way forward.

> *Technical insight:* Risk is **catalog data**, not a hard-coded rule — so the same engine governs every capability consistently, and the confirmation you see is a decision, not an accident. Emails are always *drafted* with a review-and-send handoff, never auto-sent.

### Memory — *the assistant remembers what matters*
Spaarke AI keeps **structured memory** in two scopes:
- **Record memory** — durable facts tied to a matter, project, or other record ("refer to the counterparty as 'Acme Corp'"). Anyone working that record benefits from it.
- **User memory** — your personal working preferences.

Memory is **captured automatically and silently** as you work — you don't run a "save this" step — and it's **provenance-tagged** (the assistant knows where a memory came from). You stay in control: you can review and delete your memory items at any time. Memory is structured knowledge, not a transcript, and newer facts supersede older ones.

### Context — *what the assistant knows on every turn*
On each turn, the assistant assembles a **context envelope**: who you are, which record you're in, relevant record and user memory, the recent conversation, and the business rules for the task. This is why it "already knows" the matter you're in and the preferences you've set, without you re-explaining. The envelope is budgeted and deterministic, so the assistant stays fast and consistent.

---

## 4. What you'll see as a user

The building blocks above surface as a handful of concrete behaviors:

| You'll notice… | What it means |
|---|---|
| **Outcome cards** on completed actions | A clear result with a **clickable link** to what was created/changed, **next-step chips**, and an **Undo** where the action is reversible. |
| **Confirmation only when it matters** | No nagging "are you sure?" on routine, reversible work — but a clean one-time confirm before something irreversible. |
| **The assistant asks instead of guessing** | Ambiguous or under-specified requests get a natural clarifying question, not a wrong-guess action. |
| **Honest completion** | It never claims something happened that didn't. A UI action is reported done only after it actually renders; a failure is reported as a real failure with the real reason. |
| **"How did you decide?"** | A **traceability view** shows the real chain — what it read, which tools it used, the approval path, the outcome — narrated from actual events, and it survives a page refresh. |
| **Progressive results** | Long output appears section-by-section as it's ready, rather than hanging then dumping. |
| **Memory that persists** | State a durable fact once; the assistant recalls it in later sessions on the same record — and you can review/delete it. |
| **Drafts, never surprises** | Emails and documents are drafted for your review; you send/save. |
| **Grounded answers** | Retrieval works from your firm's indexed documents, respecting the scope you're working in. |

---

## 5. What to expect — and how to think about it

- **It's a capable assistant, not an autopilot.** It's excellent at drafting, creating, summarizing, and finding — and it deliberately keeps you in the loop for anything consequential.
- **It's honest by design.** The system is built so the assistant would rather say "I couldn't do that, here's why, here's a way forward" than fabricate a success. If you ever see it claim something that didn't happen, that's a bug, not the intended behavior.
- **It gets more useful in context.** Open it *on a matter* (from the record) and it works with that matter's context and memory. Open it standalone and you'll name your own context — both are supported.
- **It improves as your catalog grows.** New capabilities are added by authoring new Actions/Bindings and playbooks — the assistant's power expands through governed catalog entries, not by loosening its guardrails.
- **Your data stays scoped.** Retrieval and memory are organized by subject (the record, the user), and the assistant works within the scope of what you're doing.

---

## 6. A worked example (end to end)

You're on the **Acme v. Widgeco** matter and you type:

> *"Create a follow-up task to review the NDA by Friday, and remember that we always call the other side 'Widgeco Ltd.'"*

Here's what happens under the hood, mapped to the building blocks:

1. **Context** — the assistant already knows you're on the Acme matter and who you are (no re-explaining).
2. **Routing (Binding)** — "create a task" resolves to the create-task Action; "remember…" resolves to a memory write.
3. **Judgment** — creating a task is low-risk and reversible → it just does it. The memory write is automatic and silent.
4. **Action** — the task is created on the matter, due Friday, assigned appropriately; the fact "counterparty = Widgeco Ltd." is captured as **record memory** with provenance.
5. **Result** — you get an **outcome card**: a link to the new task, an **Undo**, and **next-step chips**. No intrusive confirmation, because none was warranted.
6. **Later** — next week, in a brand-new session on the same matter, you ask it to draft a letter to the other side. It writes "Widgeco Ltd." without being told — because the memory persisted.

That's Spaarke AI: plain-language requests, grounded in your context, executed through a governed catalog of capabilities, with judgment about when to involve you and honesty about what it did.

---

*For the technical architecture behind this overview, see `docs/architecture/` (AI architecture, dispatch/routing, memory) and the ADRs governing judgment/confirmation (ADR-041) and memory (ADR-042).*
