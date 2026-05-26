# From Patterns to Build — Four-Surface Design Decisions

> **Date**: 2026-05-18
> **Purpose**: Convert the seven UI pattern specs and two cross-cutting concepts into actionable design decisions for the Spaarke application. Specifies the four surfaces of the application, how they relate, what fills them, and what UI components must be built.
> **Companion documents**: Eight pattern / cross-cutting specs (`pattern-*.md`, `cross-cutting-*.md`).

---

## 1. The Four Surfaces

Each surface is a **screen region with a defined functional role**. The location is fixed; the functional role is fixed; what specifically fills the surface varies with what the user is doing.

### 1.1 Operational surface

**Definition.** A dedicated, full-screen destination launched from the Power Apps left navigation. Optimized for information-intensive review, management, and organization at volume.

**Screen role.** Takes over the screen (alongside the Power Apps left rail) when launched.

**Functional role.** Bulk information work — reviewing many items, managing collections, organizing visually, acting in batch. Users typically click through from a list or view into individual record details, where CRUD operations on the record are available.

**Examples:**
- **To Do List app** — managing tasks across matters
- **Semantic Search app** — searching across the knowledge base and Dataverse records
- **Email** — reviewing and processing email at volume
- **Work Queue** — a portfolio-level queue of items needing attention
- **Power BI dashboards** — bulk analytical review
- **Matters list view** — full-list view of matters for portfolio-level work (when this is what the user needs)

**What does NOT go here:** AI-orchestrated conversational work; focused work on a single document or single record done in conversation with AI (that's Workspace); ambient AI-derived context (that's Context).

**Pattern affinity:** High volume or dense presentation of lists / views — record lists, queue lists, special-purpose apps, links to external systems, bulk update of records.

### 1.2 Assistant surface

**Definition.** The conversational AI surface where the user expresses intent in natural language and the system responds. **Assistant is where the user orchestrates AI-directed resources and interactions** — instructing the AI, requesting information, driving action, and interacting with AI about content currently in the Workspace surface.

**Screen role.** A pane on the left side of the three-pane Spaarke AI experience.

**Functional role.** Intent expression, AI dialogue, and orchestration. The user instructs, asks, and converses; the AI replies, streams responses, calls tools, and routes results to the Workspace surface when the right output is a structured artifact rather than chat text.

**What lives here:**
- Active conversation prompt and recent exchanges
- Conversation history scoped to the current matter / context
- Suggested-prompt chips
- AI tool-use indicators ("Searching matters…")
- Streaming AI responses
- Brief acknowledgments when Assistant routes output to Workspace

**What does NOT go here:** Terminal answers to questions that warrant structured artifacts; persistent decisions (those get committed elsewhere); long-form drafting (drafts go to Workspace as Canvas).

**Pattern affinity:** Generative / Conversational.

### 1.3 Workspace surface

**Definition.** The focused-work surface where the user receives, views, and works on whatever they're currently engaged with. Workspace content is presented as **widgets** — informational or functional mini-apps such as a visual card, a list, a report, a wizard, a document review/drafting surface, or any other structured experience that supports the active work.

**Screen role.** The multi-tab middle pane of the three-pane Spaarke AI experience. Supports up to 3 concurrent tabs.

**Functional role.** Active focused work. What fills the Workspace surface varies with what the user is doing, but the surface itself is consistent: it's where the work that needs attention right now lives.

**How the Workspace surface gets filled.** Workspace content arrives from several sources:
- **On arrival**, by default: the Daily Briefing widget (see below).
- **From the Assistant**, when AI responses warrant structured artifacts (asking "what's our outside counsel spend this quarter" causes a BudgetDashboard widget to render in Workspace).
- **From the Context surface**, when the user selects an actionable item there (for example, selecting the "Create a Matter" playbook in Context opens the Create-a-Matter wizard in Workspace).
- **From a Daily Briefing or other widget**, when the user clicks through (clicking an entity card opens that entity in Workspace).

**What can fill the Workspace surface:**
- **On arrival**: The **Daily Briefing** widget — a defined AI query that surfaces the user's open tasks, recently created records, documents, and other notifications. This is a focused AI-curated view, not a generic dashboard.
- **In response to an Assistant query**: a Summary widget, a TriageQueue widget, a Form/Wizard, a Canvas, an Entity view — whatever pattern best serves the request.
- **In response to a Context-pane action**: typically a wizard or focused work surface launched from a playbook.
- **In response to a widget action**: an entity record view, a document Canvas, a queue.
- **At user request**: any widget the user chooses to open as their starting point (see §3 on personalization).

**What does NOT go here:** Bulk information work spanning many items (that's Operational); AI conversation (that's Assistant); ambient context about the active work (that's Context).

**Pattern affinity:** Entity-centered, Canvas / Document-centered, Form / Wizard-centered, Workflow / Process-centered. Also Summary when scoped to the active context. Also Queue when triaging items in the current scope.

### 1.4 Context surface

**Definition.** The ambient information surface that surfaces what the AI knows, what's related, and what's referenceable for the work currently in play. Context is the "utility player" of the three-pane shell — it presents a wide and varied set of information based on what the user is doing and what's happening in Workspace and Assistant.

**Screen role.** A pane on the right side of the three-pane Spaarke AI experience.

**Functional role.** Provides AI-derived metadata, related information, reference materials, and ambient awareness for whatever is happening in Workspace and Assistant. Context updates as Workspace focus shifts and as Assistant interactions establish new scope.

**The AI intelligence layer concept.** An important distinction: Context does *not* duplicate Workspace by showing the same Dataverse record fields in another place. If a matter is open in Workspace as an Entity view, the matter's structured properties (parties, value, status, dates) live in Workspace. What Context shows is a different *layer* of information — what could be thought of as the AI intelligence layer or knowledge layer about the work. This includes:

- **AI-derived insights** about the active work ("this matter has been unusually quiet for 14 days"; "this counterparty's contract activity is up 4× this month")
- **Reference materials the AI surfaces as relevant** — playbook standards, prior similar matters, internal precedent, regulatory guidance
- **Cross-system context** the Dataverse record doesn't carry — recent email threads, Teams conversations, calendar items relevant to the matter
- **AI reasoning surfaces** — *why* the AI flagged something, *why* a priority was assigned, *why* a suggestion was made
- **Related artifacts** the AI has identified — documents, parties, matters
- **Workflow-stage progress** when the active work is part of a staged process
- **Actionable references** the user can launch into Workspace — playbook cards, common wizards, suggested next actions

This framing — Context as the AI intelligence layer — gives the surface a clearer identity than "miscellaneous information." Workspace shows the *work*; Context shows what the system *knows about the work*.

**Context's relationship to Assistant and Workspace.** Context is driven by *both* Workspace state *and* Assistant interactions:

- When the user is working on a matter in Workspace, Context shows that matter's AI intelligence layer.
- When the user asks Assistant "tell me about Counterparty X," Assistant may respond in chat *and* populate Context with the counterparty's intelligence panel.
- When the user invokes a playbook in Context (e.g., "Create a Matter"), Workspace opens the corresponding wizard — Context drives Workspace.

So Context, Assistant, and Workspace form a triangle where any pane can drive the others. The convention is: Assistant orchestrates AI interactions, Workspace holds the active work, Context holds what's known and referenceable about the active work. Each pane may cause the others to update.

**What lives here:**
- AI-derived insights for the active work
- AI reasoning surfaces
- Reference materials (playbooks, standards, precedent)
- Related items the AI has identified
- Cross-system context (email, Teams, calendar)
- Recent activity beyond what's in the structured record
- Actionable playbook cards and common wizards
- Filter / breakdown state when Workspace is showing a list or queue

**What does NOT go here:** Primary work the user must operate (that belongs in Workspace); navigation between surfaces (that's the Power Apps left rail); AI conversation (that's Assistant); the structured properties of the record currently in Workspace (those are in Workspace).

**Pattern affinity:** Cross-cutting — Context supports whatever pattern is currently in Workspace by surfacing the ambient information that pattern would want shown.

---

## 2. How the Surfaces Relate

### 2.1 Surface combinations

Not all four surfaces are always present. They combine differently depending on what the user is doing:

| What the user is doing | Surfaces in play |
|---|---|
| **In Spaarke AI** (any moment) | Power Apps left rail + Assistant + Workspace + Context |
| **In an Operational app** (To Do, Email, Search, dashboards, list views) | Power Apps left rail + the Operational surface |

Two patterns emerge:

1. **The three Spaarke AI surfaces move together.** When the user is in Spaarke AI, Assistant + Workspace + Context are all present and work as a system.
2. **Operational surfaces are entered and exited as units.** The user moves between Operational apps and Spaarke AI via the Power Apps left navigation.

### 2.2 How a user moves between surfaces

- **From Spaarke AI to an Operational app:** Click an entry on the Power Apps left rail (e.g., "To Do"). The full screen becomes that Operational app.
- **From an Operational app to Spaarke AI:** Click the Spaarke AI entry on the Power Apps left rail. The three-pane Spaarke AI experience appears.
- **Within Spaarke AI:** Movement happens within the three surfaces. Asking Assistant a question may produce content in Workspace. Selecting a Daily Briefing item replaces Workspace content. Selecting a playbook in Context may open a wizard in Workspace. Clicking a row in a Workspace queue may update Context. The three surfaces work as a system, with any pane potentially driving the others.

### 2.3 What the user understands

The user does not need to know the word "surface." They experience these as natural places — "where I check my tasks" (Operational), "where I ask the AI things" (Assistant), "what I'm working on right now" (Workspace), "info about what I'm working on" (Context). The model is invisible when it works.

---

## 3. Personalization

How the system gives the user what they want or need is the product of two complementary forces: **system intelligence** (sensible defaults based on persona, role, and context) and **user preferences** (explicit choices the user makes).

### 3.1 System intelligence — defaults that fit

The system applies sensible defaults based on what it knows about the user and the situation:

- **Persona-aware defaults.** An in-house counsel default home experience differs from a legal operations manager default — the AI surfaces different items, the suggested Daily Briefing emphasizes different things, the Operational apps presented in the left nav may differ.
- **Context-aware AI responses.** What the AI surfaces in Context, what it suggests as next actions, and what it interprets as the user's intent all draw on the user's persona, the active matter, and recent activity.
- **Adaptive prioritization.** The AI's priority signals (in queues, in Daily Briefing items, in Context insights) adapt to what the user has historically acted on.

System defaults can be overridden by user preferences but should be opinionated enough to be useful out of the box.

### 3.2 User preferences — explicit choices

Two preference points matter most:

**Home page setting (Power Apps standard).** Power Apps lets users choose which app they land in on login. The default Spaarke home is the Spaarke AI app (the three-pane experience). A user can elect to land in any Operational app instead — To Do, Email, a specific dashboard, etc. This is a single setting with high impact: it determines the user's first 30 seconds in the product every day.

**Workspace initial content within Spaarke AI.** When a user lands in Spaarke AI, the Workspace surface is filled by default with the Daily Briefing widget. A user can change this preference to a different widget — a specific dashboard, a personalized queue, a saved search result, or any other widget that serves their work mode better. This is similar to how Outlook lets users choose which folder loads on launch.

**Additional preferences** that will likely emerge over time: AI suggestion aggressiveness, density of information presented, default views within widgets, keyboard shortcut customization, notification preferences. These can be added incrementally; the home page and Workspace initial content are the two that should be available from launch.

### 3.3 Why personalization matters here

Personalization is not a polish feature; it shapes the user's primary experience of the product. A user who works in dashboards primarily and dips into Spaarke AI for occasional questions will configure differently than a user whose work is entirely AI-orchestrated matter handling. The system must accommodate both without either feeling like a second-class citizen.

---

## 4. User Moments and the Components They Require

These five moments are illustrative examples — not an exhaustive specification of every user interaction. They are chosen because they stress different surface combinations and surface different design decisions. Other moments (cross-matter pivots, administrative work, error recovery) inherit the conventions established here.

For each moment: the situation, what the user can do, and the components required for the moment to work. Component tables are directional; specific entry points may vary and components may be triggered from multiple surfaces.

### 4.1 Moment 1: Arrival

**Situation.** User opens Spaarke. They haven't been in the app since Friday. They want to know what they're walking into.

**What happens.** The Power Apps left rail is visible. The three Spaarke AI surfaces appear (assuming Spaarke AI is the user's home page). The Workspace surface is filled with the user's chosen initial content — by default, the Daily Briefing widget. Assistant shows a welcome state with suggested prompts. Context surfaces ambient information appropriate to the user's recent activity, possibly including a Playbook gallery.

**The user can:**
- Scan the Daily Briefing — open tasks, recently created records, documents, notifications
- Click into any item to take action on it
- Type a question into Assistant
- Select a playbook in Context to launch a focused work surface (e.g., "Create a Matter") into Workspace
- Navigate to an Operational app via the left rail

**Components required:**

| Component | Surface | Notes |
|---|---|---|
| **DailyBriefingWidget** | Workspace (default initial content) | The AI-curated arrival view. A defined query surfacing open tasks, recent records, documents, notifications. |
| **AssistantWelcomeState** | Assistant | Welcome message and suggested prompt chips. |
| **PlaybookGallery** | Context | Surfacing relevant playbook cards for one-click launch into Workspace. |
| **SessionRestore** | Cross-cutting | Durable session state so the user's prior work is recoverable. |

### 4.2 Moment 2: Triage

**Situation.** User wants to see and triage what needs their attention. The path varies: they might click an item in Daily Briefing, type a request into Assistant ("show me matters needing attention"), or select a playbook in Context. The directional point is that something causes Workspace to render a queue.

**What happens.** Workspace transitions to a TriageQueueWidget showing the relevant items. Assistant contextualizes the queue and remains available for follow-up. Context surfaces filter state, breakdown, and the AI's reasoning for any priority signals.

**The user can:**
- Scan rows with AI priority signals visible
- Use keyboard shortcuts to disposition rows quickly
- Select a row to drive Context to that item's details
- Click into a specific item to open its Entity view in another Workspace tab
- Use bulk operations with visible confirmation
- Ask Assistant cross-cutting questions about the queue

**Components required:**

| Component | Surface | Notes |
|---|---|---|
| **TriageQueueWidget** | Workspace | New load-bearing widget. Rows with triage signal density, disposition shortcuts, bulk operations, AI priority overlays. |
| **AIPrioritySignal** | Inside TriageQueueWidget | The priority indicator on each row. |
| **AIReasoningSurface** | Context | "Why is this high priority" — rationale that appears for the focused row. |
| **BulkOperationConfirmation** | Inside TriageQueueWidget | Required for legal accountability on bulk actions. |
| **PortfolioBreakdownPanel** | Context | Filter state, counts by status, breakdown of the active queue. |
| **CrossPaneRowFocus** | Cross-cutting protocol | The mechanism by which selecting a row drives Context. |

### 4.3 Moment 3: Deep work on a specific entity

**Situation.** User has opened a specific entity (a matter, a counterparty, a project) — typically by clicking through from a queue, a Daily Briefing item, or an Assistant response.

**What happens.** Workspace shows the entity view (EntityWorkspaceWidget). Assistant is implicitly scoped to this entity. Context shows the entity's AI intelligence layer — insights, related items, reference materials, recent cross-system activity.

**The user can:**
- View entity properties and edit fields in place
- See related parties, documents, recent activity within the entity view
- Open an attached document — Workspace adds a Canvas tab
- Ask Assistant about the entity — answers include citations
- Request Assistant to draft something — output renders in a new Workspace tab
- Launch a playbook from Context — wizard opens in Workspace

**Components required:**

| Component | Surface | Notes |
|---|---|---|
| **EntityWorkspaceWidget** | Workspace | Full entity view inside Workspace. Distinct from the more compact entity info that may appear in Context. |
| **InlineFieldEditor** | Inside EntityWorkspaceWidget | Click-to-edit on individual fields. |
| **RelatedRecordsSection** | Inside EntityWorkspaceWidget | Documents, parties, related entities surfaced inside the view. |
| **ActivityTimeline** | Inside EntityWorkspaceWidget | Audit-grade history. |
| **AIEntitySummary** | Inside EntityWorkspaceWidget | 1–3 sentence narrative at the top of the entity. |
| **AIFieldSuggestion** | Inside EntityWorkspaceWidget fields | AI-suggested values with AI-vs-user visual distinction; one-click accept. |
| **EntityIntelligencePanel** | Context | AI insights, cross-system context, AI reasoning for any flags. |
| **CrossPaneScopePropagation** | Cross-cutting | Assistant scope follows Workspace's active entity. |

### 4.4 Moment 4: Document review

**Situation.** User is reviewing a document — inbound NDA, counterparty redline, draft memo. They need to read, mark up, possibly compare and respond.

**What happens.** Workspace shows the Document Canvas. Assistant is implicitly grounded to the document. Context shows the document's intelligence layer — playbook reference for the section in focus, prior versions, AI-surfaced concerns on specific clauses.

**The user can:**
- Read at full document fidelity
- Select text to drive Context to the relevant playbook standard
- Annotate, highlight, redline
- Ask Assistant about a selection with citations back to specific clauses
- Invoke compare-to-playbook for inline diff against the standard
- Invoke AI redline proposals (visually distinct from user redlines, accept/reject per change)
- Export with appropriate options

**Components required:**

| Component | Surface | Notes |
|---|---|---|
| **DocumentCanvas** | Workspace | The load-bearing Canvas widget. Builds on DocumentViewer and RedlineViewerWidget. |
| **SelectionDrivenCrossPane** | Inside DocumentCanvas | Selecting text drives Context and grounds Assistant. |
| **InlineCitationMark** | Inside DocumentCanvas | Where AI citations land — clickable, navigable. |
| **CitationJumpAndHighlight** | Cross-cutting | Click a citation in Assistant → Canvas scrolls to and highlights. |
| **AIRedlineProposal** | Inside DocumentCanvas | Visually distinct from user redlines; per-change accept/reject. |
| **CompareMode** | Inside DocumentCanvas | Side-by-side or inline diff against prior version or playbook. |
| **PlaybookReferencePanel** | Context | Shows the playbook standard for the focused section. |
| **AIConcernFlag** | Inside DocumentCanvas | Inline flags where AI surfaces concern. |
| **AIvsUserVisualDistinction** | Cross-cutting | Applied here with highest stakes. |

### 4.5 Moment 5: Asking a question or expressing open intent

**Situation.** User opens Spaarke AI without a specific entity in mind. They want to ask something open-ended — "what's our outside counsel spend this quarter," "find recent NDAs with Acme," "set up a new matter."

**What happens.** Workspace begins in its initial state (Daily Briefing or the user's chosen default). The user types into Assistant or clicks a suggested-prompt chip. Assistant streams a brief acknowledgment, calls tools as needed, and routes the structured answer to Workspace.

**The user can:**
- Type natural language into Assistant
- See AI tool-use indicators
- Receive an answer routed to Workspace as a Summary widget, a Queue, a Form for review, an Entity view, or a Canvas
- Follow up with related questions — Workspace updates or adds tabs
- Click through citations to specific records or documents

**Components required:**

| Component | Surface | Notes |
|---|---|---|
| **AssistantInput** | Assistant | Natural language input with streaming response, tool indicators, suggested chips. |
| **AssistantRoutingMechanism** | Cross-cutting | The mechanism by which AI responses warranting structured artifacts render in Workspace rather than as chat text. |
| **NextActionChips** | Assistant | After an answer, suggested follow-up actions render as pattern-routing chips, not chat-continuation chips. |
| **DynamicWorkspaceRender** | Workspace | The capability for Workspace to render different widget types in response to Assistant output. |
| **AIWorkingIndicator** | Assistant | Streaming text, specific tool labels, stop affordance. |
| **CitationSurface** | Assistant | Citations in chat output that link to and highlight Workspace content. |
| **PreAnnotationStreamingVisual** | Cross-cutting | Visual distinction during streaming, before groundedness annotations attach. |

---

## 5. Consolidated Component Inventory

Aggregating across the five moments, components fall into four bands. Items used in multiple moments appear once.

### 5.1 Cross-cutting conventions (foundational — build first)

These are conventions applied across widgets. They must be defined before the widgets that depend on them.

| # | Component | Why foundational |
|---|---|---|
| 1 | **AI-vs-User visual distinction convention** | Required by every AI-augmented widget. Without it, accountability is broken across the system. |
| 2 | **Citation surface convention** (link, hover, click-jump-and-highlight) | Required in Assistant, Canvas, Summary, Entity. |
| 3 | **AI working indicator convention** (streaming + tool labels + stop) | Required everywhere AI works. |
| 4 | **Pre-annotation streaming visual** | Required by the retroactive groundedness annotation architecture. |
| 5 | **AI reasoning surface convention** | "Why this priority / suggestion / flag." |
| 6 | **Cross-pane interaction protocol** | How selection in one surface drives another. |
| 7 | **Matter scope propagation** | Active matter visible and propagated across surfaces. |
| 8 | **Session restore** | Resume on arrival depends on durable session state. |

### 5.2 Surface containers

| # | Component | Notes |
|---|---|---|
| 9 | **Power Apps left rail** | Standard Power Apps navigation. |
| 10 | **Three-pane Spaarke AI shell** | Layout container hosting Assistant + Workspace + Context. |
| 11 | **Workspace tab management** | Up to 3 tabs with focus, close, switch, persist behaviors. |
| 12 | **Personalization settings** | Home page choice, Workspace initial content choice. |

### 5.3 Workspace widgets (primary — build alongside foundations)

| # | Component | Moments using |
|---|---|---|
| 13 | **DailyBriefingWidget** | Moment 1 |
| 14 | **TriageQueueWidget** | Moment 2 |
| 15 | **EntityWorkspaceWidget** with InlineFieldEditor, RelatedRecordsSection, ActivityTimeline, AIEntitySummary, AIFieldSuggestion | Moment 3 |
| 16 | **DocumentCanvas** with SelectionDrivenCrossPane, InlineCitationMark, AIRedlineProposal, CompareMode, AIConcernFlag | Moment 4 |
| 17 | **DynamicWorkspaceRender** | Moment 5 — capability to render any widget type in response to Assistant routing |

### 5.4 Assistant and Context surface components

| # | Component | Notes |
|---|---|---|
| 18 | **AssistantInput** with streaming, tool indicators, suggested chips | Assistant |
| 19 | **AssistantRoutingMechanism** | The orchestrator-stance commitment in code. |
| 20 | **NextActionChips** | Pattern-routing after AI answers, not chat-continuation. |
| 21 | **EntityIntelligencePanel** | Context — AI insights, cross-system context, reasoning. |
| 22 | **PlaybookReferencePanel** | Context — playbook standards for active work. |
| 23 | **AIReasoningSurface** | Context — "why" rationale for AI signals. |
| 24 | **PortfolioBreakdownPanel** | Context during triage — filter state, counts. |
| 25 | **PlaybookGallery** | Context — actionable playbook cards on arrival and elsewhere. |

---

## 6. Build Sequencing

A rough four-phase sequence. Phase A is the foundational gate; Phases B, C, and D can overlap.

**Phase A — Foundations (weeks 1–2).** Items 1–8 (cross-cutting conventions) plus design specs for surface containers (9–12). These are mostly design specifications and standard component definitions. They don't all require code, but they require decisions.

**Phase B — Primary Workspace widgets (weeks 3–6).** Items 13–17. The four primary new Workspace widgets (DailyBriefingWidget, TriageQueueWidget, EntityWorkspaceWidget, DocumentCanvas) plus the DynamicWorkspaceRender capability that lets Workspace receive output from Assistant. These can run in parallel with separate teams. Each depends on Phase A conventions.

**Phase C — Assistant and Context surface (weeks 5–8).** Items 18–25. Overlaps with Phase B. AssistantRoutingMechanism (#19) is the load-bearing piece — it requires coordinated work on the engineering side (router and model) and the design side (visual conventions).

**Phase D — Personalization and refinements (weeks 7–10).** Item 12 (personalization settings) becomes implementable once the surfaces are stable. Additional widget refinements and Operational app integrations refine over time.

**The dependency that matters most:** Phase A conventions must land before Phase B widgets begin in earnest. The AI-vs-user visual distinction, citation surface, and AI reasoning surface conventions must be defined before the widgets that use them, or the widgets will be rebuilt.

---

## 7. Open Decisions Needing Owner Input

Five decisions worth making before Phase A starts:

1. **Power Apps left navigation entries.** What appears in the Operational layer's left navigation? Spaarke AI is the primary entry. The To Do List app, Semantic Search app, and other Operational apps already built are confirmed. The open question is whether entity collections — Matters, Projects, Documents, Invoices, Counterparties — should also appear as left-nav entries (giving users a direct route to full list views), or whether users will reach them through the Spaarke AI Workspace and Context surfaces (entity work happens in Spaarke AI; full-list management is rarely needed).

    The tradeoff: fewer left-nav entries reduces clutter and reinforces Spaarke AI as the primary work mode, but users will expect to find "Matters" or "Documents" somewhere obvious and may be confused if those entries don't exist. The goal is the fewest entries that satisfy user expectations — perhaps a small set of entity-list Operational apps (Matters, Documents at minimum) alongside the AI-focused apps. Recommend resolving this with a quick test against the primary persona's expected mental model.

2. **The orchestrator-stance commitment.** Item #19 (AssistantRoutingMechanism) is the design's strongest claim — Assistant is the front door, not the destination. Structured answers route to Workspace; chat does not become the place answers live. Confirming this commitment now prevents drift toward chat-as-destination under customer-demo pressure later.

3. **Personalization scope at launch.** Items confirmed: home page setting (Power Apps standard) and Workspace initial content choice. The open question is whether additional preferences (AI suggestion aggressiveness, density, default views, notifications) should launch with v1 or be added incrementally.

4. **Surface names.** Operational | Assistant | Workspace | Context are the working names. If any need refinement for user-facing language, decide before they appear in product strings.

5. **AI intelligence layer scope.** The Context surface concept relies on an "AI intelligence layer" — AI-derived metadata, cross-system signals, reference materials — distinct from the structured Dataverse record. This is an architectural concept that affects what data Context needs to fetch and how it integrates with the rest of the system. Worth a brief engineering / architecture discussion to confirm the concept is supported and to identify what work the intelligence layer requires beyond what already exists.

---

*Final version 2026-05-18.*
