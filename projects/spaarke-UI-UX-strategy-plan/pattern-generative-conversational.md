# Pattern Spec: Generative / Conversational

> **Status**: Draft v0.1
> **Pattern slug**: generative-conversational
> **Closest analogous product:** Microsoft Copilot (with reference to Harvey, Hebbia, and Spellbook for legal-specific AI; Notion AI and Linear AI for "AI in an opinionated app" patterns)
> **Primary user intent:** *"Help me think, find, draft, or explore."*

---

## 1. Optimizes For

The Generative pattern optimizes for **open intent expression that the system routes into structured action**. The user doesn't know exactly what they want or doesn't want to navigate to find it; they describe their intent in natural language and the system produces an answer, a rendered artifact, an opened tool, or a populated form.

The pattern is *not* a chatbot in the consumer sense (free conversation as the endpoint). In Spaarke, the chat is the *front door* to the rest of the application. Its job is to understand intent and route to the right pattern, rendering its output in the appropriate pane and widget. Chat-as-destination — where the user asks, gets an answer in chat, and stays there — is a failure mode of this pattern, not its goal.

This is the most strongly opinionated pattern in the doctrine and the one most likely to face pressure to drift into chat-as-destination as customers see "ChatGPT-like" interactions in other products.

---

## 2. When To Use / When Not To Use

### 2.1 Use this pattern when

- The user's intent is open-ended and they don't know exactly which tool or pattern serves it ("help me understand this matter," "find the recent NDAs with this counterparty").
- The user wants to compose an action that would require multiple steps in a traditional UI ("set up a new matter for Acme contract review with these parameters" — one chat utterance vs. five form steps).
- The user wants to ask a question grounded in the application's data ("what's our average review time for vendor MSAs," "show me matters that look like the one I'm working on").
- The user is in exploration mode and wants the system to suggest patterns / tools / records rather than navigating to them.
- The user wants to draft something with AI assistance (a memo, an email, a clause) and is willing to have the draft appear in Canvas or a Form for refinement.

### 2.2 Do not use this pattern when

- The user knows exactly what they want and where it lives. "Show me the Acme matter" should be a search or a direct nav, not a chat utterance — though it is acceptable to support both.
- The work is deep engagement with a specific artifact (Canvas).
- The work is per-item triage of many items (Queue).
- The user has just submitted a chat request — *the system's response should not be more chat unless the answer is truly conversational*. It should render the result in the right pane and pattern. Chat-as-answer-only is the pattern's primary failure mode.

### 2.3 Pattern overlaps

- **Every other pattern**, in a specific way: Generative routes *into* other patterns. The user asks a question; the answer manifests as a Summary widget, a Canvas open in Workspace, a Form populated for review, a Queue filtered to a relevant view, an Entity opened. Generative is the universal entry; the destination is always one of the other patterns.
- **Canvas-Generative binding** is the most consequential overlap. Asking about a document on Canvas produces answers cited back to specific clauses. This is bidirectional: chat can drive Canvas, Canvas can drive chat.

---

## 3. Mechanics

### 3.1 Core mechanics

1. **Persistent chat surface in the Conversation pane.** The chat is always available, persistent across navigation, with conversation history scoped to the current matter context.

2. **Routing, not just answering.** The chat's most important behavior is to recognize when the right response is to render something elsewhere. "Show me last quarter's spend" doesn't get a chat answer; it renders a BudgetDashboard in Workspace and the chat says, briefly, "Here's last quarter's spend." The chat's role is *to point*.

3. **Streaming response.** Text answers stream as they generate. The user sees progress; long answers don't feel frozen. The standard convention from current AI products (ChatGPT, Claude, Copilot) is well-established.

4. **Tool-use indicators.** When the chat is doing something — searching, calling a tool, generating a document — the user sees a brief indicator ("Searching matters," "Generating draft"). This is the AI-augmentation cross-cut's "AI is doing something" convention applied here.

5. **Citations to grounded sources.** Answers that draw on the application's data cite back to specific records, documents, or clauses. Citations are clickable and drive navigation (to Entity, to Canvas with highlight, to Summary).

6. **Cross-pane rendering.** When the chat's answer is a structured artifact, it renders in the appropriate pane (usually Workspace) and the chat acknowledges. This is what makes Generative an orchestrator.

7. **Conversation history scoped to matter / context.** When the user pivots from matter A to matter B, the conversation context changes appropriately. Cross-matter pivots strip privileged content (design.md §9.2.4 — already specified).

8. **Edit and regenerate.** The user can edit their prompt and regenerate the response. Standard convention.

### 3.2 Supporting mechanics

1. **Suggested follow-up actions.** After an answer, the chat surfaces likely next moves — "open the matter," "see the source documents," "draft a response." These are not nag-ware; they reduce navigation friction.

2. **Conversational form completion.** Already discussed in the Form pattern spec — the user describes structured input in chat, the AI produces a populated form in Workspace for confirmation.

3. **Conversational summary requests.** "Summarize this matter for the board update" produces a Summary widget; "summarize this document" produces a Canvas view with summary atop.

4. **Question-about-canvas binding.** While the user is on a document in Canvas, the chat is implicitly grounded to that document; questions about "this clause" or "this paragraph" don't need explicit reference.

### 3.3 AI augmentation mechanics

Generative *is* an AI pattern at its core, so the augmentation discussion is integrated into the mechanics above. The cross-cutting AI Augmentation spec governs the conventions that apply across patterns; here they apply most densely.

Three points specific to the Generative pattern's AI design:

1. **Single LLM call per turn, with router pre-selection.** The architecture commits to one LLM call per user turn; the router pre-selects tools / sources / scope before the model generates. This is design.md §8.6 and it's the right architectural choice. The user-facing implication is that the model produces a coherent response in one turn, not a multi-step agentic dance. This shapes what the chat *can* do per turn and the spec must work within it.

2. **Streaming with retroactive groundedness annotation.** Output streams; groundedness annotations (which parts are cited to which sources) appear after streaming completes. This is design.md §9.2.2. The UX implication is real: the user may begin reading a streamed answer before the citations are attached. The Canvas pattern flagged the open question of what happens on Canvas during this gap; the Generative spec inherits the same question.

3. **Privilege-aware retrieval.** Cross-matter pivots strip privileged content from chat history (design.md §9.2.4, spec.md FR-405, FR-408). This is correct and load-bearing. The user-facing implication is that pivoting matters shows a brief notification ("conversation context updated for [new matter]").

---

## 4. Expectations to Honor (Closest Analogous Product: Microsoft Copilot)

This is the trickiest "analogous product" call because Copilot itself is still finding its conventions. Spaarke users will arrive with experience from Copilot in Word/Excel/Outlook (most), ChatGPT (most), and possibly Claude / Gemini.

### 4.1 What Copilot does that Spaarke must match

- **Chat-pane-on-the-side layout** in the host application — the chat is alongside the work, not the main view.
- **Streaming response with stop-generation affordance.**
- **Citations to source documents** within the response.
- **Suggested-prompt chips** at the start of a conversation or after an answer.
- **Conversation history visible and scrollable.**
- **Clear distinction between user message and AI response** in styling.
- **"AI is working" indicator** during generation.

### 4.2 Where Spaarke legitimately diverges — and where it must defend a stance

The Generative pattern is where Spaarke makes its most consequential design commitment: **chat is orchestrator, not destination**.

| Spaarke divergence | Why | Risk of getting this wrong |
|---|---|---|
| Chat answers that warrant a structured artifact render that artifact in Workspace, not in chat | The orchestrator stance — chat points to the right pattern, doesn't try to replace it | If we drift to chat-as-destination, we become a slightly different ChatGPT, not a structured legal-operations product. Customer demos may pressure this drift. |
| Citations from chat are bidirectional bindings, not just hyperlinks | Spaarke's three-pane shell makes click-citation-and-highlight-on-Canvas feasible and superior; Copilot's panel layout limits it | If we don't deliver this, the citations feel like consumer-product hyperlinks rather than legal-grade source attribution |
| Conversation history is scoped to matter context | Legal accountability — cross-matter chat history is a privilege risk | If we don't, we have a compliance problem, not just a UX problem |
| AI-suggested follow-up actions render as patterns (open Queue, show Summary), not as more chat | Reinforces the orchestrator stance at the moment users are most tempted to continue chatting | Continuous chat is comfortable; breaking out into structured patterns requires the system to model it consistently |

The orchestrator stance is the doctrine's strongest claim about the Generative pattern. The risk is not technical — it's that customers see Harvey or ChatGPT and ask "why doesn't ours just chat back at me?" The answer needs to be defensible: because legal work is structured, and a product that lets the user lose track of what was decided where is worse than a product that puts the answer in the right structured place.

---

## 5. Common Failure Modes

| Failure mode | Symptom | Fix |
|---|---|---|
| **Chat-as-destination drift.** Users ask questions, get text answers, never leave the chat. The other patterns atrophy. | Spaarke becomes a chatbot with a sidebar; the structured value of the product fades. | The orchestrator stance is enforced at the design level: chat outputs that warrant artifacts MUST render them in Workspace. The model is trained / prompted to route, not just answer. |
| **Citations that don't bind.** Chat says "as noted in the matter records" with no link, or links that open a generic list. | Trust failure: legal users cannot rely on AI assertions without specific grounding. | Citations must point to specific records / documents / clauses. If the chat can't produce specific citations, it should say so rather than gesturing vaguely. |
| **Cross-matter context leakage.** A user pivots from matter A to B and the chat continues with A's context. | Privilege violation; potential legal and ethical breach. | Privilege-aware retrieval (design.md §9.2.4) with explicit user-facing notification on matter pivot. The notification is not a UX nicety — it's a compliance signal. |
| **Streaming-without-citations anxiety.** The answer streams; the user reads and starts to act; citations appear retroactively and reveal the answer was less grounded than it looked. | False confidence; possible action on weak grounding. | Visual distinction during streaming: pre-annotation text looks different from post-annotation text. The user should never not know whether they're reading something grounded vs. still in progress. |
| **Tool-use indicator without consequence.** "Searching matters…" appears and disappears; the user has no idea what was searched or with what scope. | Trust degradation over time. | Tool-use indicators should be specific ("Searched: open litigation matters in your team's scope") and the underlying tool call should be inspectable on demand. |
| **Conversational form completion that doesn't show the form.** User says "set up a new matter," AI says "OK, I'll set up the matter," and silently creates it. | The user lost the verification step; errors become hard to catch. | Conversational form completion MUST produce a populated form for review; never silently commit to a structured action. |
| **Suggested-prompts becoming nag-ware.** Constant follow-up suggestions; users feel pestered. | Distraction; suggestions get ignored. | Suggestions are sparse, contextual, and dismissable. Two or three after an answer, not after every turn. |

---

## 6. Spaarke Component Mapping

### 6.1 Current design positions

| Pattern element | Current Spaarke component |
|---|---|
| Conversation surface | **SprkChat** (preserved from R1). Lives in the Conversation pane. |
| Router and tool pre-selection | design.md §8.6 architectural commitment; engineering layer. |
| Streaming + retroactive groundedness | design.md §9.2.2. |
| Cross-pane rendering from chat | spec.md FR-801 SSE events (`workspace_widget`, `workspace_action`) and FR-207 cross-pane interaction. |
| Citation surfaces | Mentioned across design.md and spec.md but the visual / interaction specifics aren't fully detailed. |
| Privilege-aware retrieval | design.md §9.2.4; spec.md FR-405, FR-408. |

### 6.2 Design-challenge findings

| Finding | Current design | Pattern requires |
|---|---|---|
| **The chat-as-orchestrator stance is correct, but the mechanism for enforcing it isn't fully specified.** | design.md commits to "Conversation pane is orchestrator + entry point, not destination" (§8.2). | A concrete mechanism: when the model would produce a structured-data answer, it MUST route to a widget render rather than rendering in chat. The router's responsibility is partly to enforce this. The risk is that without an enforced mechanism, the model drifts to chat-as-destination over time. |
| **Streaming-with-retroactive-groundedness needs a UX convention for the pre-annotation window.** | design.md §9.2.2 commits to the architecture; the user-facing behavior during the streaming-but-not-yet-annotated gap isn't specified. | A visual convention: text during streaming looks different from text after annotation completes. This is critical for legal contexts where pre-annotation text could be misleading. The Canvas spec also flagged this; it belongs in AI Augmentation cross-cutting. |
| **The orchestrator stance is at risk of being eroded by customer pressure.** | Doctrine commits to it. | A defensible articulation of why — for buyer-evaluation conversations and for the design team's own use when pressured to "just chat back." This belongs in the buyer-evaluation answer-sheet (Task X.4) and in the doctrine more broadly. |
| **Citation binding from chat to Canvas is not fully specified at the interaction level.** | Cross-pane interaction is mentioned (design.md §2.2, spec.md FR-207). | Concrete spec of the click-citation-jump-and-highlight behavior. Already flagged in the Canvas spec; belongs in the Composition Guide (Generative → Canvas transition). |
| **AI-suggested follow-up actions as pattern routing, not chat continuation, isn't fully specified.** | Implicit in the orchestrator stance. | A defined component for "after this answer, here are likely next moves" that render as buttons routing into patterns (open Queue, show Summary, draft Form), not as chat suggestions to continue chatting. |

### 6.3 New components / events / widgets proposed

- **Orchestrator enforcement mechanism** — architecturally, how the router and model coordinate to ensure structured-data answers render as artifacts, not chat text. Engineering call.
- **Pre-annotation streaming convention** — visual distinction during the gap between streaming completion and groundedness annotation arrival. Cross-cutting.
- **Citation binding protocol** — already noted in Canvas spec; concrete spec belongs in Composition Guide.
- **AI-suggested next-action chips** — render as pattern-routing buttons, not chat suggestions.

---

## 7. Open Questions

| Question | Why it matters | Validation |
|---|---|---|
| When chat produces a structured artifact (a Summary, a populated Form), does the chat itself remain in the pane with a brief acknowledgment, or shrink / collapse to make room for the artifact? | UX flow and the user's sense of where they are. | Prototype testing |
| For follow-up questions about an artifact rendered in Workspace, does the chat implicitly know the artifact context, or must the user reference it? | Friction vs. ambiguity. Implicit is smoother but may be wrong; explicit is unambiguous but more typing. | Likely implicit with visible "context: matter X" indicator; prototype testing to confirm |
| How aggressive should AI-suggested next actions be? After every answer? Only when confidence is high? Only when the user is at a specific decision point? | Trust vs. distraction. | Prototype testing + pilot instrumentation on suggestion-acceptance rates |
| For the streaming gap before groundedness annotation, what's the right visual treatment — gray text? Italic? A "verifying sources…" indicator that resolves into citations? | Trust and the user's ability to know what they're reading. | Prototype testing |
| When a user pivots matters mid-conversation, does the prior matter's chat history archive (visible to that user later for that matter) or get destroyed? | Audit and resumption. Archiving is the default expectation for legal work. | Engineering call + product policy decision |
| The orchestrator stance commits us to NOT building features that other AI products have (free-form continuous chat). Is this defensible against pressure to add ChatGPT-like behavior? | The product's structural value depends on holding this line. | Designer's-call commitment, defended in the doctrine and the buyer-evaluation answer-sheet |

---

*Draft v0.1 — 2026-05-18.*
