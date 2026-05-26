# Cross-Cutting Concept: AI Augmentation

> **Status**: Draft v0.1
> **Slug**: cross-cutting-ai-augmentation
> **What this is**: The consistent set of conventions that govern how AI behaviors manifest across the seven UI patterns. Per the Overview: "The pattern is the load-bearing wall; AI is the wiring." This document specifies the wiring conventions.

---

## 1. Why This Is Cross-Cutting, Not a Pattern

AI is not a pattern in Spaarke. Each of the seven UI patterns has AI overlays (suggested fields in Entity, priority labels in Queue, narrative captions in Summary, cite-back in Canvas, pre-fill in Form, handoff summaries in Workflow, the entire surface of Generative). What makes these *feel like one product* rather than seven AI features bolted onto seven patterns is the consistency of the conventions they share.

Inconsistency is the failure mode. If AI-suggested fields look different in Entity than in Form, users have to re-learn each context. If citations work one way in Canvas and another way in Generative, users lose trust. If "AI is working" indicators are inconsistent in their meaning, the application feels unreliable.

This document defines the conventions. The seven pattern specs reference back here for the specific applications.

---

## 2. The Seven Conventions

| # | Convention | What it covers |
|---|---|---|
| 1 | Streaming and "AI is working" indication | How users see in-progress AI work |
| 2 | AI-suggested vs user-entered visual distinction | The most important convention; how users know what came from where |
| 3 | Citations and groundedness | How AI assertions are linked to their sources |
| 4 | Confidence indicators | How users see when the AI is more or less sure |
| 5 | Regeneration and editing of AI output | How users correct or refine what the AI produced |
| 6 | Error recovery | How the AI fails gracefully |
| 7 | The pre-annotation streaming gap | Specific to streaming with retroactive groundedness annotation |

Each is discussed below with the specific application pattern-by-pattern.

---

## 3. Convention 1: Streaming and "AI Is Working"

### 3.1 Principle

The user always knows when the AI is doing something, what it's doing at a high level, and approximately how long it's been working. This is non-negotiable: silent AI work produces user anxiety and the assumption the application is broken.

### 3.2 Mechanics

- **Streaming text output** when the AI is producing language (chat responses, summaries, drafted content). The standard convention from ChatGPT / Claude / Copilot applies.
- **Tool-use indicators** when the AI is doing something other than producing text — searching, calling a function, generating a document. A short, specific label: "Searching matters," "Generating draft," "Comparing to playbook."
- **A stop-generation affordance** for long-running work, where appropriate.
- **Duration indication** for work that exceeds a reasonable threshold (e.g., 5 seconds). Either a progress signal or a "still working…" reassurance.

### 3.3 Application per pattern

| Pattern | Streaming / "working" application |
|---|---|
| Generative | Primary application — text streams; tool-use shown; stop available |
| Canvas | When AI is proposing redlines or summarizing, the document area shows the work in progress |
| Summary | When AI is producing a narrative caption or surfacing anomalies, the dashboard shows a brief "analyzing" state |
| Entity | When AI is suggesting field values, the affected fields show "thinking" briefly |
| Form | When AI is pre-filling fields, the affected fields show "filling in" briefly |
| Queue | When AI is prioritizing items, a brief "prioritizing" state on the queue header |
| Workflow | When AI is producing a handoff summary or anomaly flag, brief state shown |

### 3.4 What to avoid

- Spinning loaders without context. "Loading…" is not enough.
- Long silent gaps where the AI is clearly working but the user has no signal.
- Tool-use indicators that don't reflect what's actually happening ("Thinking…" for everything).

---

## 4. Convention 2: AI-Suggested vs User-Entered Visual Distinction

### 4.1 Principle

The most important convention in this document. Users must always be able to tell which content came from them and which came from AI. In legal contexts this is not a usability preference; it is an accountability requirement. A user must be able to say, looking at a record or a document, what they personally entered vs. what they accepted from AI.

### 4.2 Mechanics

- **Pre-confirmation state**: AI-suggested content (a field value, a redline, a drafted paragraph) renders with distinct visual treatment until the user confirms it.
- **Confirmation is explicit**: a one-click or one-keystroke action that promotes the content from "AI-suggested" to "user-confirmed."
- **Post-confirmation state**: the content visually merges with user-entered content. The system retains the metadata (it was AI-suggested and user-confirmed at this time by this user) for audit; the UI no longer visually distinguishes.
- **Override is one action**: the user can edit an AI suggestion before confirming, which counts as a confirmation of the edited value.
- **Visible distinction must work for color-blind users** and other accessibility considerations — not color alone; also a textural / structural cue (italic, dashed underline, etc.).

### 4.3 Application per pattern

| Pattern | AI-suggested distinction application |
|---|---|
| Entity | AI-suggested field values (industry inferred from name, classification from description) appear distinctly until confirmed |
| Form | AI pre-filled fields appear distinctly until confirmed (per-field or per-form, depending on form type) |
| Canvas | AI-proposed redlines appear distinctly from user-entered redlines, and from accepted-into-text content |
| Summary | AI-generated narrative caption is visually distinct from user-entered annotation |
| Workflow | AI-generated handoff summary is distinct from user-entered notes |
| Queue | AI-assigned priority labels are visually distinct from user-set priorities |
| Generative | Doesn't apply directly — the whole pane is AI conversation — but cited content from AI assertions inherits this distinction in the destination pattern |

### 4.4 What to avoid

- AI content that looks identical to user content from the moment it appears.
- Confirmation that's invisible (silent accept on submit).
- Visual distinction that's too subtle to notice (a 5% lighter background).
- Visual distinction that's too aggressive (red borders) — it should signal "review me" not "warning."

---

## 5. Convention 3: Citations and Groundedness

### 5.1 Principle

When AI makes assertions grounded in data the application holds, those assertions cite specific sources. Citations are clickable and navigate to the source. Ungrounded assertions are either marked as such, qualified, or omitted.

This is what makes Spaarke's AI defensible in legal contexts. Lawyers cannot rely on assertions they cannot verify; the application must make verification a single click away.

### 5.2 Mechanics

- **Inline citation marks** at the point of an assertion — typically a small indicator (number, icon) immediately after the cited content.
- **Citations link to specific sources** — not "the matter records" but a specific record; not "the contract" but a specific clause.
- **Click-citation behavior** depends on the destination:
  - Citation to an Entity record → opens the Entity in the appropriate pane.
  - Citation to a document → opens Canvas with the cited clause highlighted.
  - Citation to a Summary data point → highlights the data point on the relevant Summary widget.
  - Citation to a prior conversation → shows the prior message inline or in conversation history.
- **Multiple citations in one assertion** are individually clickable.
- **Ungrounded assertions** are either omitted (preferred), qualified ("general knowledge: ..."), or marked as ungrounded.

### 5.3 Application per pattern

Citations are most dense in Generative (chat answers cite their grounding) and Canvas (cited content highlights on the document). Summary uses citations to underlying records when a chart segment is questioned. Entity uses them lightly (an AI-suggested field value may cite where the suggestion came from). Form uses them for pre-fill ("this value comes from the originating email"). Queue uses them for priority reasoning ("flagged high because [X cited Y]").

### 5.4 What to avoid

- Citations that don't link — "as noted elsewhere" with no destination.
- Citations to vague destinations (a folder, a list) rather than specific items.
- Citations rendered as visual clutter that interrupts reading.
- Citations that don't survive page state changes (citation links to a specific document position that breaks when the document is edited).

---

## 6. Convention 4: Confidence Indicators

### 6.1 Principle

When the AI's confidence in an assertion varies, the user sees it. The user should not have to assume that all AI output is equally reliable.

This is the most contested convention because the AI's own confidence is often miscalibrated. The convention applies *where confidence indication actually helps the user act*; it is not applied universally.

### 6.2 Mechanics

- **Apply confidence indication when:**
  - The action the user might take is consequential (sign a contract, advance a workflow stage).
  - The AI has a reasonable confidence signal (not all models do for all tasks).
  - The user can do something with the confidence information (verify, override, ask for re-analysis).
- **Do not apply when:**
  - The output is decorative or supporting (a narrative caption on a chart).
  - The confidence signal is unreliable (worse than no indicator).
  - The user has no action to take based on confidence.

- **When applied**, confidence is a small visual cue (a confidence bar, an icon, a text label). Three levels is typically sufficient: high / medium / low. More granularity reads as false precision.

### 6.3 Application per pattern

Most relevant in:
- **Canvas** — AI-flagged concerns on specific clauses ("high concern" vs "minor issue").
- **Queue** — AI priority labels with confidence in the priority assignment.
- **Form** — AI pre-fill where the source signal varies in reliability.
- **Generative** — answers that depend on retrieval where retrieval quality varies.

Less relevant in:
- **Summary** narrative captions (confidence is moot — either the data supports the caption or it doesn't, in which case fix the caption).
- **Workflow** AI handoff summaries (these are summaries of completed work; confidence isn't the relevant axis).
- **Entity** AI field suggestions (the user accepts or doesn't; binary).

### 6.4 What to avoid

- Confidence indicators on every AI output (numbed by ubiquity).
- High-precision confidence numbers (84.3%) that the user can't interpret.
- Confidence indicators where the AI's confidence is known to be miscalibrated.

---

## 7. Convention 5: Regeneration and Editing of AI Output

### 7.1 Principle

AI output is a starting point, not a final state. Users must be able to refine, regenerate, or override what the AI produced. The mechanics of refinement should feel like editing a draft, not fighting the system.

### 7.2 Mechanics

- **Edit-in-place** for AI-produced text. The user clicks into the AI's output and edits it directly. Standard text-editing conventions apply.
- **Regenerate** affordance for substantial AI outputs (chat answers, drafts, narrative captions). One click, with optional prompt modification.
- **Partial regenerate** where possible — regenerate just this paragraph, not the whole answer.
- **Undo / restore** to bring back the original AI output if the user's edits proved wrong.
- **Versioning of significant AI outputs** (a drafted memo has 3 prior versions accessible) for high-stakes work.

### 7.3 Application per pattern

Most relevant in Generative (regenerate the chat answer), Canvas (modify AI-proposed redlines), and Form (override AI pre-fill). Less relevant in Queue and Summary where AI output is descriptive rather than draft-able.

### 7.4 What to avoid

- Regenerate that produces near-identical output (frustrating).
- Edit modes that lock the user out of related context.
- Regenerate that discards prior conversational context.

---

## 8. Convention 6: Error Recovery

### 8.1 Principle

When AI fails — model unavailable, content filter triggered, tool call errored, retrieval found nothing — the user sees a clear, useful error and a path forward.

### 8.2 Mechanics

- **Specific errors, not generic.** "Could not generate redlines because the document couldn't be parsed" is useful; "Something went wrong" is not.
- **Retry affordance** when retry is sensible (transient failures).
- **Alternative action suggestions** when retry won't help. "I couldn't find prior NDAs with this counterparty — would you like to search broadly, or check if the counterparty record exists?"
- **No silent failure.** A pre-fill that didn't fill must say so, not just leave fields blank.
- **Graceful degradation** — partial work is preserved when one element fails.

### 8.3 What to avoid

- Generic error messages that don't help.
- Errors that leave the user stuck with no recovery path.
- Silent fallback (the AI didn't work, so nothing happens, and the user doesn't know).

---

## 9. Convention 7: The Pre-Annotation Streaming Gap

### 9.1 The specific problem

Spaarke's architecture (design.md §9.2.2) streams output token-by-token, with groundedness annotations applied retroactively after streaming completes. This creates a window where the user can read streamed output before the citations and groundedness flags are attached.

In a chat interface this is uncomfortable but tolerable. In legal contexts where the user might act on the streamed content (accept an AI assertion, agree to a recommendation, copy a draft into a real document) before annotations arrive, this is consequential.

### 9.2 The convention

- **Pre-annotation streamed text is visually distinct.** Standard treatment: slightly lighter weight or color, or a subtle indicator that this is "streaming, not yet verified." The user reads but knows the verification step is pending.
- **Post-annotation transition is visible.** When annotations arrive, the text transitions to its final state with citation marks appearing inline. The transition is animated briefly so the user notices.
- **User actions on pre-annotation content are not blocked, but are not encouraged.** No modal "wait for annotations." But UI affordances that act on the content (accept a draft, advance a workflow) are slightly subdued or labeled "still verifying" until annotations land.
- **If annotation fails** (the verification step errors), the user sees an explicit signal — the streamed output is marked as ungrounded or pending verification, not silently treated as verified.

### 9.3 Why this is its own convention

This case is specific enough to Spaarke's architectural choice that it warrants its own convention rather than being subsumed under streaming. Most AI products don't have retroactive groundedness annotation; Spaarke does, and the UX around the gap is novel enough to need specification.

---

## 10. Cross-Cutting Design-Challenge Findings

| Finding | Current design | Required addition |
|---|---|---|
| **AI-vs-user visual distinction is not a defined convention.** | Implicit; varies across patterns. | This document defines the convention; pattern specs reference back. Engineering needs to apply it consistently. |
| **Citation binding from chat to Canvas (and other destinations) needs concrete spec.** | Cross-pane interaction is sketched (design.md §2.2, spec.md FR-207). | Concrete protocol for click-citation behavior per destination type. Belongs in Composition Guide. |
| **Pre-annotation streaming gap visual treatment is undefined.** | The architecture creates this window; the UX response isn't specified. | This document proposes a visual convention. Needs prototype testing for the specific styling. |
| **Confidence indicator policy isn't defined.** | Not in current design. | This document proposes a "apply where it helps, omit where it doesn't" policy. Engineering call on which AI outputs have reliable confidence signals to expose. |
| **Tool-use indicator specificity needs convention.** | Not specified. | Indicators must be specific to what's happening; generic "thinking" is insufficient. |
| **Error recovery affordances per AI failure mode need pattern-specific specification.** | Not specified. | Each pattern's AI mechanics section should specify the expected failure modes and recovery paths. Belongs partly here, partly in pattern specs. |

---

## 11. What This Document Is Not

Three things this document deliberately does not cover:

1. **The model itself** — which model is called, with what prompt, with what tools. That's the architecture's job (design.md §8.6).
2. **Privilege and safety** — these are addressed in design.md §9 and the Operational Containers cross-cutting concept. AI Augmentation conventions assume privilege-aware retrieval is in place.
3. **The conversational pattern's specific mechanics** — those are in the Generative pattern spec. This document is about the conventions that *cross* patterns.

---

*Draft v0.1 — 2026-05-18.*
