# SprkChat Extensibility — Analysis Workspace Command Center

> **Project**: ai-sprk-chat-extensibility-r1
> **Status**: Design
> **Priority**: 2 (Parallel with #3, depends on #1 for context-aware commands)
> **Branch**: work/ai-sprk-chat-extensibility-r1
> **Last Updated**: March 25, 2026
>
> **Scope Change (March 25, 2026)**: Narrowed from "all Spaarke contexts" to **Analysis Workspace only**. General Spaarke AI queries (Corporate Workspace, matter-level Q&A, navigation) are now served by M365 Copilot integration — see `projects/ai-m365-copilot-integration/design.md`. SprkChat's SidePaneManager injection on the Corporate Workspace will be removed; SprkChat is exclusively the Analysis Workspace AI companion.

---

## Executive Summary

Transform SprkChat from a text-in/text-out chat interface into a **contextual command center for the Analysis Workspace** — the interactive AI surface that bridges conversational AI, playbook execution, and workflow actions within analysis contexts. Playbooks remain the core AI orchestrator, running either explicitly (user-invoked) or implicitly (triggered in the background). The chat must be "smart" — understanding natural language intent and mapping it to system capabilities, just as Claude Code understands "run a report of all PCF controls" without explicit commands.

> **Note**: UC1 (Corporate Workspace — Proactive Dashboard Assistant) has been moved to the M365 Copilot integration project. SprkChat no longer launches on the Corporate Workspace. UC2-UC4 remain in scope as they operate within analysis/document contexts accessible from the Analysis Workspace.

---

## Problem Statement

Users interact with SprkChat exclusively through free-text chat. There is no structured way to invoke specific AI capabilities, switch playbooks, perform system operations, or access contextual quick actions. The AI-generated suggestion chips (post-response) are helpful but not user-invocable — the user cannot proactively say "I want to summarize" without typing it out.

More fundamentally, Spaarke has a rich **playbook library** of packaged AI (and non-AI) actions that users cannot access from chat. Playbooks can be triggered programmatically (e.g., New Document triggers Document Profile), but there is no conversational interface for users to discover, invoke, or interact with playbook capabilities. The chat needs to become the bridge between users and the playbook engine.

---

## Vision

SprkChat becomes the **unified interaction surface** where:

- **Natural language is primary** — users describe what they want; the AI routes to the right playbook, tool, or data query
- **Quick-action chips** provide one-tap access to contextual actions for discoverability
- **`/` command menu** serves as a power-user keyboard shortcut to the same capabilities
- **Playbooks are invisible infrastructure** — selected by context, invocable by name, but users never need to understand "playbooks" to use the system
- **Compound actions** chain multiple steps (summarize → draft → email) with plan preview and user approval gates
- **Responses are rich** — structured cards, email previews, clickable entity links, and interactive data — not just markdown text

---

## Market Context & Research

### Industry Shift: Chat → Delegation

The dominant 2025-2026 trend in enterprise AI is the move from freeform chat to **structured delegation**. Thomson Reuters explicitly frames CoCounsel's positioning as "from prompting to delegation." Nielsen Norman Group categorizes enterprise AI chat controls into four groups: feature discoverability, education/inspiration, conversation constraints, and follow-up facilitation.

### Leading Legal AI Platforms

#### Harvey AI (Most Relevant Comparison)

Harvey is a standalone app (`app.harvey.ai`) with five main tools accessed from a left sidebar: **Assistant, Vault, Workflows, History, Library**.

**Assistant (main chat interface):**
- Central prompt box with personalized suggested prompts below it
- Three key affordances built into the prompt bar:
  - **Sources/Files button** — opens a panel to select knowledge sources (LexisNexis, web, uploaded docs, DMS)
  - **Workflows button** — applies a pre-built or custom workflow agent to the thread
  - **Model selector** — pick AI model or "Auto"
- Previously had separate "chat" and "drafting" interfaces — **unified them** so users draft, ask follow-ups, and revise all in a single conversation thread
- In-app draft editor (no need to export to Word), with automatic redlining against originals
- All responses include inline citations linked to source documents

**Workflows (separate from chat, invocable from chat):**
- Pre-built Workflow Agents walk users through steps — the agent asks for inputs rather than requiring users to construct detailed prompts
- Workflow Builder: no-code visual canvas with drag-and-drop blocks (User Input, AI Action, Logic, Output) for teams to build custom workflows
- "Thinking states" show the AI's reasoning as it progresses through steps
- Workflows are stored in a Library that teams can browse and share

**Matter OS (emerging):**
- Matter-centric workspace consolidating documents, precedents, emails, and workflows per matter
- iManage integration for iterative search within a matter's document set
- Memory feature carries context across conversations (governed by firm admins)

**Key takeaway for SprkChat**: Harvey keeps workflows separate but accessible from the chat prompt bar. The unified experience (chat + drafting + revision in one thread) is the pattern to follow. Workflows are browsable in a Library.

#### Thomson Reuters CoCounsel Legal

CoCounsel moved from chat-first to **task delegation**:
1. User describes or selects a task
2. System shows a **multi-step research/execution plan** before executing
3. User reviews and approves the plan
4. System executes autonomously across repositories (Westlaw, Practical Law, DMS)
5. Output is a **structured deliverable** — full analytical report with sections, citations, arguments for/against

Guided Workflows include: drafting privacy policies, employee policies, complaints, discovery requests/responses, deposition transcript reviews, SEC Form 8-K reports. Deep Research generates a visible research plan, traces its logic with transparent reasoning, and delivers citation-backed reports.

**Key takeaway for SprkChat**: Plan preview with user approval before executing complex actions is becoming table-stakes for legal AI trust. The "structured deliverable" output (not just chat text) is expected.

#### LexisNexis Lexis+ Protege

Protege is a **left sidebar panel** integrated into Lexis+ with four modes: **Ask, Draft, Summarize, Documents**. Each mode changes what the sidebar does — it's a mode-switching panel, not a single chat. Draft mode has an inline editor with track changes. Backend coordinates four specialist agents (Orchestrator, Legal Research, Web Search, Customer Document Research).

**Key takeaway for SprkChat**: The sidebar panel with mode-aware behavior is relevant to SprkChat's side pane context. Different contexts = different capabilities, similar to how playbook context mapping works.

### General Enterprise AI Patterns

#### Microsoft 365 Copilot

- **Suggested actions** appear as pill-shaped buttons above the compose box in Teams
- **Adaptive Cards** — structured JSON-based UI components returned inline in chat (forms, buttons, data tables, action links)
- **Copilot Actions** — governed, auditable workflows triggered from chat
- **Copilot Studio** — agents built with custom instructions, grounding knowledge, tools via REST connectors, and multi-agent orchestration

#### GitHub Copilot Chat (Most Mature Command System)

Three-axis model for structured interaction:
- **`/` for commands**: `/explain`, `/fix`, `/tests`, `/doc`, `/help` — context-aware of current file/selection
- **`@` for context scoping**: `@workspace` (entire codebase), `@vscode` (IDE features), `@terminal`
- **`#` for variable injection**: `#file`, `#selection`, `#codebase`

#### Salesforce Agentforce

- Side panel in every Salesforce app, context-aware of current record
- **Next-Best Action Plans**: multi-step action plans the user can accept, modify, or decline
- **Prompt Builder**: no-code tool for creating governed, reusable prompt templates scoped to roles

### Emerging Patterns

| Pattern | Description | Leading Examples |
|---------|-------------|------------------|
| **Guided Workflows / Task Delegation** | Pre-built multi-step procedures triggered by selecting a task | CoCounsel, Harvey Workflow Agents, Copilot Actions |
| **Contextual Quick Actions** | Pill-shaped buttons above compose box suggesting next actions | Microsoft Teams, Gemini, Salesforce Agentforce |
| **Slash Commands + Context Scoping** | `/command` for actions, `@scope` for targeting | GitHub Copilot Chat, Notion AI, Slack AI |
| **Prompt Libraries / Playbooks** | Searchable, role-specific, governed prompt collections | OpenAI Prompt Packs, Salesforce Prompt Builder |
| **Transparent Reasoning / Plan Preview** | AI shows its plan before executing, with user approval gate | CoCounsel Deep Research, Harvey Thinking States |
| **Generative UI / Adaptive Cards** | AI returns structured cards, forms, or dynamic interfaces | Microsoft Adaptive Cards, Google A2UI, AG-UI protocol |
| **Madlibs / Fill-in-the-Blank** | Structured templates with blanks instead of freeform input | ShapeOf.AI pattern, PromptFluent |

---

## Design Principles

1. **Playbooks are the core AI orchestrator** — every AI action routes through a playbook, either explicitly (user picks from menu) or implicitly (context mapping selects the right one)
2. **Natural language first** — the chat must be "smart" enough to understand intent and route to the right capability without requiring slash commands
3. **Three interaction tiers** — natural language (primary), quick-action chips (discoverable), slash menu (power users)
4. **Context drives everything** — workspace vs. matter vs. document vs. analysis determines available actions, chips, and playbook selection
5. **Plan preview for compound actions** — any multi-step action shows the AI's plan before executing
6. **Rich structured responses** — cards, email previews, clickable links, interactive data — not just text
7. **Invisible complexity** — users talk naturally; playbooks, tools, and routing happen behind the scenes

---

## What Exists Today

### SprkChat Components
- `SprkChatInput` — Textarea with Ctrl+Enter send, character counter (0/2000)
- `SprkChatSuggestions` — Follow-up suggestion chips (AI-generated, 1-3 items, shown post-response)
- `SprkChatPredefinedPrompts` — Static prompt suggestions shown before first message
- `SprkChatContextSelector` — Document + playbook dropdown (hidden when no options)
- `SprkChatHighlightRefine` — Floating toolbar for text selection refinement

### BFF API
- `GET /api/ai/chat/playbooks` — Lists available playbooks (name, description, isPublic)
- `PATCH /api/ai/chat/sessions/{id}/context` — Switch playbook/document mid-session
- `DELETE /api/ai/chat/sessions/{id}` — Delete/clear session

### From Project #1 (Context Awareness)
- `GET /api/ai/chat/context-mappings` — Returns available playbooks for current context (default + alternatives)

### Playbook Library
- `sprk_analysisplaybook` Dataverse entity with capabilities multi-select (`search`, `analyze`, `write_back`, `reanalyze`, `selection_revise`, `web_search`, `summarize`)
- Playbooks compose Tier 1 scopes: Actions (ACT-*), Skills (SKL-*), Knowledge (KNW-*), Tools (TL-*)
- Triggered programmatically (e.g., New Document → Document Profile playbook)
- `PlaybookChatContextProvider` assembles system prompt + registers tools based on active playbook capabilities
- JPS (JSON Prompt Schema) externalizes prompt structure into maintainable, composable JSON

---

## Use Cases

### ~~UC1: Corporate Workspace — Proactive Dashboard Assistant~~ (MOVED)

> **Moved to**: `projects/ai-m365-copilot-integration/design.md`
>
> **Reason (March 25, 2026)**: Corporate Workspace general queries (due dates, assignments, matter activity) are better served by M365 Copilot, which goes GA in model-driven apps on April 13, 2026. SprkChat's SidePaneManager injection on the Corporate Workspace will be removed. These data-query use cases (navigable results, entity queries) align with Copilot's native Dataverse integration capabilities and do not require SprkChat's analysis-specific features (streaming, editor integration, write-back).

### UC2: Matter Context — Email with Summary to Outside Counsel

**Context**: User is on a matter form. SprkChat is available in the side pane. User wants to send an email to outside counsel with a matter summary and a request for an update.

**User experience**:
```
User types: "send outside counsel a summary of where we
are and ask for an update"

SprkChat:
┌──────────────────────────────────────────────┐
│ 📋 Here's my plan:                           │
│                                              │
│ 1. Summarize matter status from recent       │
│    documents and activity                    │
│ 2. Draft email to Jane Smith (Outside        │
│    Counsel — Kirkland & Ellis)               │
│ 3. Include: status summary + request for     │
│    update                                    │
│                                              │
│ [✏️ Edit plan] [▶ Proceed]  [✕ Cancel]       │
└──────────────────────────────────────────────┘

After user clicks Proceed:
┌──────────────────────────────────────────────┐
│ ✅ Searched 12 matter documents              │
│ ✅ Summarized 3 most recent updates          │
│                                              │
│ 📧 Draft Email                               │
│ To: j.smith@kirkland.com                     │
│ Subject: Status Update — Acme v. BigCorp     │
│ ──────────────────────────────────────────── │
│ Dear Jane,                                   │
│                                              │
│ [AI-generated summary + request]             │
│                                              │
│ [✏️ Edit] [📎 Attach docs] [📤 Send]         │
│ [💬 "make it more formal"]                   │
└──────────────────────────────────────────────┘
```

**Behavior**:
- SprkChat recognizes compound intent: summarize + draft email + resolve recipient + send
- Shows plan preview before executing (transparency pattern from CoCounsel/Harvey)
- "Outside counsel" resolved from matter party/role relationships in Dataverse
- Draft is conversationally refinable ("make it shorter", "add the contract deadline")
- Email send goes through BFF API (server-side) or triggers Dataverse email activity
- Matter-scoped playbook provides the tools: `DocumentSearch`, `SummaryGenerator`, `EmailDraft`, `PartyResolver`

**Key requirement**: Compound multi-step actions with plan preview and approval gates. The AI chains tools autonomously but the user approves the plan first.

### UC3: Document Context — Draft Response and Email

**Context**: User is viewing a document (e.g., a demand letter from opposing counsel). User wants to prepare a response and send it via email to outside counsel.

**User experience**:
```
Quick actions (document-type-aware):
[📝 Draft Response] [📋 Summarize] [⚠️ Flag Issues]

User clicks "Draft Response":
┌──────────────────────────────────────────────┐
│ I've analyzed this demand letter from        │
│ Smith & Associates regarding patent          │
│ infringement.                                │
│                                              │
│ What type of response?                       │
│ [Acknowledge & Request Extension]            │
│ [Formal Denial]                              │
│ [Request for Clarification]                  │
│ [Custom...]                                  │
└──────────────────────────────────────────────┘

User selects "Formal Denial":
┌──────────────────────────────────────────────┐
│ 📋 Plan:                                     │
│ 1. Draft formal denial response              │
│ 2. Reference key points from demand letter   │
│ 3. Prepare email to outside counsel for      │
│    review before sending to opposing counsel  │
│                                              │
│ [▶ Proceed] [✏️ Edit plan]                   │
└──────────────────────────────────────────────┘
```

**Behavior**:
- Quick-action chips are **document-type-aware** — a demand letter gets "Draft Response" while a contract gets "Review Terms"
- The Document Profile playbook (already triggered on upload) classified the document type, so SprkChat knows what actions are contextually relevant
- Response type selection narrows the AI's drafting approach (guided workflow pattern)
- Draft is conversationally refinable before sending
- Email integration allows sending to outside counsel or internal review

**Key requirement**: Document classification feeds into available quick actions. The playbook's prior analysis output informs what SprkChat can offer.

### UC4: NDA Analysis — Refine Output and Email Summary

**Context**: User is viewing NDA Analysis results (output of the NDA Analysis playbook). User wants to correct a finding, update the analysis record, and email a summary to outside counsel.

**User experience**:
```
Quick actions (analysis-context-aware):
[🔄 Refine Analysis] [📋 Summary Email] [📊 Compare to Standard]

User types: "The indemnification section is wrong — the
cap is $5M not $2M. Fix it and send a summary to
outside counsel."

SprkChat:
┌──────────────────────────────────────────────┐
│ 📋 Plan:                                     │
│                                              │
│ 1. Update NDA Analysis: Indemnification Cap  │
│    $2,000,000 → $5,000,000                  │
│ 2. Prepare summary of analysis (3 key        │
│    findings, 2 risk flags, 1 correction)     │
│ 3. Draft email to Jane Smith with summary    │
│                                              │
│ [▶ Proceed] [✏️ Edit plan]                   │
└──────────────────────────────────────────────┘

After approval:
┌──────────────────────────────────────────────┐
│ ✅ Updated: Indemnification Cap              │
│    $2,000,000 → $5,000,000                  │
│ ✅ Analysis record saved                     │
│                                              │
│ 📧 Draft Email                               │
│ To: j.smith@kirkland.com                     │
│ Subject: NDA Analysis Summary — Acme Corp    │
│ ──────────────────────────────────────────── │
│ Dear Jane,                                   │
│                                              │
│ [Summary with corrected findings]            │
│                                              │
│ [📤 Send] [✏️ Edit] [✕ Cancel]               │
└──────────────────────────────────────────────┘
```

**Behavior**:
- SprkChat reads existing analysis results (structured JSON from prior playbook execution)
- Uses `write_back` capability to update the Dataverse record
- Uses `reanalyze` capability to regenerate affected sections if needed
- Composes summary from the updated analysis output
- Email includes the corrected findings
- All within a single conversational thread — no context switching

**Key requirement**: SprkChat interacts with existing playbook output — reading, modifying, and acting on structured analysis data stored in Dataverse. Requires the `write_back` and `reanalyze` playbook capabilities.

---

## Design

### Architecture: Three Interaction Tiers

```
┌─────────────────────────────────────────────────────┐
│                  USER INTERACTION                    │
├─────────────────────────────────────────────────────┤
│                                                     │
│  Tier 1: Natural Language (Primary)                 │
│  "send outside counsel a summary"                   │
│  → Intent recognition → Smart routing → Execution   │
│                                                     │
│  Tier 2: Quick-Action Chips (Discoverable)          │
│  [📅 My Due Dates] [📝 Draft Response] [📋 Summary] │
│  → Pre-loaded, context-driven, one-tap access       │
│                                                     │
│  Tier 3: / Command Menu (Power Users)               │
│  /search, /summarize, /draft, /switch               │
│  → Keyboard-navigable catalog of all capabilities   │
│                                                     │
├─────────────────────────────────────────────────────┤
│                  SMART ROUTING LAYER                 │
│  AI model with registered tools decides how to      │
│  fulfill the request — routes to playbook actions,  │
│  data queries, system commands, or direct response   │
├─────────────────────────────────────────────────────┤
│                  PLAYBOOK ENGINE                     │
│  Context mapping → Active playbook → Registered     │
│  tools + capabilities → System prompt + scopes      │
└─────────────────────────────────────────────────────┘
```

### Smart Routing Layer

The most critical new component. When the user sends any message (typed, chip, or slash command), the AI model itself performs intent recognition and routing — the same pattern Claude Code uses:

1. **System prompt** includes the active playbook's capabilities, registered tools, and current context (entity type, page type, entity metadata)
2. **Registered tools** correspond to playbook capabilities (`search` → DocumentSearch tool, `summarize` → SummaryGenerator tool, etc.) plus system-level tools (data queries, email, navigation)
3. **The AI model decides** which tools to invoke based on the user's natural language — no separate intent-classification step
4. **Compound actions** are natural — the model chains multiple tool calls in sequence, just as Claude Code chains Read → Edit → Test

This means:
- "Put together a list of my upcoming tasks" → model invokes a data-query tool, returns structured results
- "Summarize this document" → model invokes SummaryGenerator via the active playbook
- "Send outside counsel a summary and ask for update" → model chains SummaryGenerator → PartyResolver → EmailDraft
- "Clear the chat" → model invokes system clear-session tool

### Dynamic Command Registry

Commands are **not static** — they are populated from three sources:

```
Command Registry = System Commands (static)
                 + Active Playbook Capabilities (dynamic)
                 + Context-Specific Actions (dynamic)
```

**System commands** (always available):

| Command | Description | Behavior |
|---------|-------------|----------|
| `/clear` | Clear conversation | Delete session, start fresh |
| `/new` | New session | Create new session with current context |
| `/export` | Export chat | Download conversation as text/markdown |
| `/history` | View history | Show previous sessions list |
| `/help` | Show commands | Display available commands |

**Playbook-derived commands** (from active playbook's `sprk_playbookcapabilities`):

| Capability | Slash Command | Behavior |
|------------|---------------|----------|
| `search` | `/search [query]` | Semantic search across entity's documents |
| `summarize` | `/summarize` | Summarize current document or context |
| `analyze` | `/analyze` | Execute analysis on current context |
| `write_back` | `/update [field]` | Modify a record field via chat |
| `reanalyze` | `/reanalyze` | Re-run analysis with corrections |
| `web_search` | `/web [query]` | Search the web for information |
| `selection_revise` | `/revise` | Refine selected text |

**Context-specific actions** (from context mapping + document classification):

| Context | Available Actions |
|---------|-------------------|
| Corporate Workspace | My Due Dates, My Assignments, Matter Activity |
| Matter Form | Email Counsel, Search Docs, Draft Document, View Timeline |
| Document (demand letter) | Draft Response, Summarize, Flag Issues |
| NDA Analysis | Refine Analysis, Summary Email, Compare to Standard |
| Document (contract) | Review Terms, Extract Dates, Identify Parties |

When the playbook changes (via context switch or explicit `/switch`), the available commands update automatically.

### Playbook Switching

**Implicit (automatic)**: Context mapping selects the default playbook when SprkChat loads on a page. User never sees "playbook" terminology.

**Explicit (user-initiated)**: Available through the `/` menu under a "Switch Assistant" category:

```
┌─────────────────────────────────────────┐
│  Actions (Document Assistant)           │
│    /summarize   Summarize document      │
│    /search      Search matter docs      │
│                                         │
│  Switch Assistant                       │
│    Legal Research                       │
│    General Assistant                    │
│    Contract Analyst                     │
│                                         │
│  System                                 │
│    /clear    Clear conversation          │
│    /new      New session                 │
│    /help     Show commands               │
└─────────────────────────────────────────┘
```

Switching updates the system prompt, registered tools, available commands, and quick-action chips — the entire chat personality and capability set changes.

### Quick-Action Chips

Pre-loaded contextual chips displayed above the input area. Source priority:

1. **Context-specific actions** (highest priority) — from context mapping configuration
2. **Playbook capabilities** — top 2-3 capabilities from the active playbook
3. **Predefined prompts** — existing `SprkChatPredefinedPrompts` data (pre-first-message only)

```
Before first message (workspace context):
┌──────────────────────────────────────────────┐
│  [📅 My Due Dates Today] [📬 Assignments]    │
│  [📊 Matter Activity This Week]              │
├──────────────────────────────────────────────┤
│  Type a message...               [📎] [/] ▶ │
└──────────────────────────────────────────────┘

During conversation (matter context):
┌──────────────────────────────────────────────┐
│  [📋 Summarize] [🔍 Search docs] [📧 Email]  │
├──────────────────────────────────────────────┤
│  Type a message...               [📎] [/] ▶ │
└──────────────────────────────────────────────┘
```

- Chips update when context changes (page navigation, playbook switch)
- Tapping a chip either sends a structured message or opens a parameter collection step
- Hidden when SprkChat is in a narrow pane (<350px) — only slash menu and natural language available
- Maximum 4 chips to avoid visual clutter

### Plan Preview Pattern

For compound actions (2+ steps), SprkChat shows a plan before executing:

```
┌──────────────────────────────────────────────┐
│ 📋 Here's what I'll do:                      │
│                                              │
│ 1. [Step description]                        │
│ 2. [Step description]                        │
│ 3. [Step description]                        │
│                                              │
│ [▶ Proceed] [✏️ Edit plan] [✕ Cancel]        │
└──────────────────────────────────────────────┘
```

- Single-step actions execute immediately (no plan preview needed)
- "Edit plan" allows the user to modify steps conversationally ("skip step 2", "also include the contract deadline")
- Progress indicators show execution status for each step
- Any step that modifies data (write-back, email send) requires the plan preview — no silent mutations

### Rich Response Rendering

SprkChat responses must support structured content beyond markdown:

| Response Type | Rendering | Example |
|---------------|-----------|---------|
| **Entity list** | Clickable card list with key fields | Due dates, assignments, search results |
| **Email draft** | Email preview card with To/Subject/Body + action buttons | UC2, UC3, UC4 email flows |
| **Data update** | Before/after diff card | UC4 indemnification cap correction |
| **Analysis summary** | Structured sections with findings, risk flags, key terms | NDA analysis summary |
| **Document reference** | Inline document card with name, type, date, open link | Search results, cited sources |
| **Action confirmation** | Success/failure card with details | "Email sent", "Record updated" |
| **Plan preview** | Numbered step list with approve/edit/cancel buttons | All compound actions |

### SlashCommandMenu Component

```
User types "/" in input
  ↓
SlashCommandMenu opens as Fluent Popover above input
  ↓
┌─────────────────────────────────────┐
│  / Filter commands...               │
├─────────────────────────────────────┤
│  Document Assistant                 │  ← Active playbook actions
│    /summarize   Summarize document  │
│    /search      Search docs         │
│    /analyze     Run analysis        │
│                                     │
│  Switch Assistant                   │  ← Available playbooks
│    Legal Research                   │
│    General Assistant                │
│                                     │
│  System                             │
│    /clear    Clear conversation      │
│    /new      New session             │
│    /export   Export chat             │
│    /help     Show commands           │
└─────────────────────────────────────┘
  ↑↓ keyboard navigation
  Enter = execute
  Esc = dismiss
  Typing filters list
```

**Behavior**:
- Opens when `/` is typed as the first character in an empty input (or at position 0)
- Closes on Escape, click-away, or Backspace past the `/`
- Keyboard navigation: Arrow Up/Down, Enter to select, Tab for category jump
- Type-ahead filtering: `/se` shows only `/search`
- Categories grouped: active playbook actions first, then switch options, then system
- Width matches input width; max height ~300px with scroll
- Action category label shows active playbook name (dynamic)

### Input Bar Design

```
┌──────────────────────────────────────────────────┐
│  [📋 Summarize] [🔍 Search] [📧 Email Counsel]   │  ← Quick-action chips
├──────────────────────────────────────────────────┤
│  Type a message...                [📎] [/]  ▶   │  ← Input with file attach + slash + send
└──────────────────────────────────────────────────┘
```

- `[📎]` — File upload (attach documents to context)
- `[/]` — Opens slash command menu (same as typing `/`)
- `▶` — Send button (also Ctrl+Enter)
- Chips row scrolls horizontally if more than fit; hidden at <350px width

---

## Phases

### Phase 0: Scope Enforcement + Side Pane Lifecycle

**Goal**: SprkChat only appears in Analysis Workspace; clean lifecycle management.

- **Remove SidePaneManager injection** from Corporate Workspace (`src/solutions/LegalWorkspace/index.html` — remove `sprk_SidePaneManager` script injection)
- **Remove global ribbon button** for SprkChat (or scope to analysis-context-only via enable rule)
- **Close side pane on navigation away**: When the user navigates away from an Analysis record (to Corporate Workspace, matter form, etc.), the `sprkchat-analysis` side pane MUST be explicitly closed. Two mechanisms:
  - **AnalysisWorkspace cleanup**: `useEffect` cleanup function in `App.tsx` calls `Xrm.App.sidePanes.getPane('sprkchat-analysis')?.close()` on unmount
  - **Context poll fallback**: Existing `contextService.ts` poll (2-second interval) detects entityType change away from `sprk_analysisoutput` → triggers pane close via `Xrm.App.sidePanes` API
- **Verify**: SprkChat is ONLY launchable from AnalysisWorkspace (`App.tsx` lines 446-493) after these changes

### Phase 1: Smart Chat Foundation (MVP)

**Goal**: Natural language intent routing + dynamic command registry + system commands.

- Smart routing layer: register playbook tools as AI-callable functions in the chat system prompt
- Dynamic command registry populated from active playbook capabilities
- `SlashCommandMenu` Fluent v9 component (popover, filter, keyboard nav)
- Input interception in `SprkChatInput` for `/` trigger and `[/]` button
- Built-in system commands: `/clear`, `/new`, `/help`, `/export`
- Playbook switching from command menu (using existing `useChatPlaybooks` + context mappings)
- Natural language routing for single-step actions ("summarize this document" → SummaryGenerator tool)

### Phase 2: Quick-Action Chips + Context Actions

**Goal**: Pre-loaded contextual chips and context-specific quick actions.

- Quick-action chip bar above input area
- Chips populated from context mapping + playbook capabilities
- Context-specific chip sets (workspace chips vs. matter chips vs. document chips)
- Document-type-aware actions (demand letter → "Draft Response"; contract → "Review Terms")
- Chips update dynamically on context change
- Responsive behavior: hidden at <350px

### Phase 3: Compound Actions + Plan Preview

**Goal**: Multi-step actions with transparency and approval gates.

- Plan preview pattern for compound actions (2+ tool calls)
- Progress indicators during multi-step execution
- Conversational plan editing ("skip step 2", "also attach the contract")
- Email drafting and sending as a first-class compound action
- Write-back capability (update Dataverse records through chat with confirmation)
- Structured response rendering (email previews, entity cards, before/after diffs)

### Phase 4: Rich Responses + Advanced Features

**Goal**: Structured output rendering and admin extensibility.

- Rich response cards (entity lists, email previews, data updates, analysis summaries)
- Clickable entity navigation (click a matter name → opens in main content area)
- Admin-defined context actions via Dataverse configuration (`sprk_aichatcontextaction`)
- Parameterized prompt templates: "Draft a {type} to {recipient} about {topic}" (Madlibs pattern)
- Conversational refinement of any generated output ("make it shorter", "more formal")

### Phase 5: Playbook Library Browser (Future)

**Goal**: Discoverability of the full playbook library from within chat.

- Browsable playbook catalog accessible from chat (similar to Harvey's Library)
- Playbook descriptions, capabilities, and recommended use cases
- "Try this playbook" one-click activation
- Admin-curated featured playbooks per role/team
- Usage analytics: which playbooks are most used per context

---

## Success Criteria

1. **Natural language works**: User types "what are my tasks due this week" on the workspace → receives structured, navigable results without needing slash commands
2. **Context-aware actions**: Quick-action chips change appropriately across workspace, matter, document, and analysis contexts
3. **Compound actions execute**: User can say "summarize and email outside counsel" → plan preview → execute → email sent — all within chat
4. **Slash menu is dynamic**: Available commands reflect the active playbook's capabilities; switching playbook changes the command set
5. **Plan preview builds trust**: Any multi-step or data-modifying action shows the plan before executing
6. **Playbooks are invisible**: Users never need to understand "playbooks" to use SprkChat effectively — the right capabilities appear automatically based on context
7. **Accessible**: Full keyboard navigation, screen reader labels, focus management throughout

---

## Dependencies

- **Project #1 (Context Awareness)** — context mappings, page type detection, entity metadata enrichment
- **Existing infrastructure** — `useChatPlaybooks` hook, `switchContext`, `PlaybookChatContextProvider`, tool registration
- **Playbook capabilities** — `sprk_playbookcapabilities` multi-select drives available commands
- **Document classification** — Document Profile playbook output feeds document-type-aware quick actions
- **Fluent UI v9** — Popover, MenuList, Card components
- **Email capability** — BFF API endpoint for email composition and sending (may need new infrastructure)

---

## Risks & Mitigations

| Risk | Mitigation |
|------|------------|
| Slash menu in narrow side pane (300px) feels cramped | Full-width popover, compact layout; chips hidden at <350px |
| Too many commands overwhelm users | Context filtering reduces list to relevant items; max 4 chips |
| Natural language routing accuracy | Playbook-scoped tools constrain the AI's action space; fallback to conversational response if no tool matches |
| Compound actions fail mid-execution | Plan preview gives user control; each step has rollback/retry; partial results shown |
| Email sends without user confirmation | Mandatory plan preview for all data-modifying and external-facing actions |
| Performance of dynamic command registry | Cache playbook capabilities client-side; update only on context change |
| Conflict with AI interpretation of "/" messages | Intercept before send; never send raw "/command" to API |

---

## Open Questions

1. **Email infrastructure**: Is email a first-class BFF API capability today, or does it need new endpoints? Does it go through Dataverse email activity or server-side Exchange/Graph?
2. **Party resolution**: How does SprkChat resolve "outside counsel" to a specific contact? From matter roles/party relationships in Dataverse?
3. **Structured response rendering**: Adaptive Cards (JSON schema) vs. custom React components for rich responses — which approach for V1?
4. **Context-specific chip configuration**: Hardcoded per page type in code, or admin-configurable via a new Dataverse table (e.g., `sprk_aichatcontextaction`)?
5. **Playbook library browser**: Should this be Phase 5 (future), or is discoverability important enough to pull into an earlier phase?

---

## Appendix: Market Research Sources

- [Harvey AI — A More Unified Experience](https://www.harvey.ai/blog/a-more-unified-harvey-experience)
- [Harvey AI — Introducing Agents](https://www.harvey.ai/blog/introducing-harvey-agents)
- [Harvey AI — Workflow Builder](https://www.harvey.ai/blog/introducing-workflow-builder)
- [Harvey AI — Memory](https://www.harvey.ai/blog/memory-in-harvey)
- [Harvey AI — Homepage Refresh](https://help.harvey.ai/release-notes/homepage-refresh)
- [LawNext — Thomson Reuters CoCounsel Legal Launch](https://www.lawnext.com/2025/08/thomson-reuters-launches-cocounsel-legal-with-agentic-ai-and-deep-research-capabilities-along-with-a-new-and-final-version-of-westlaw.html)
- [LawNext — CoCounsel 1 Million Users](https://www.lawnext.com/2026/02/three-years-after-launching-as-first-ai-legal-assistant-cocounsel-reaches-1-million-users-and-thomson-reuters-teases-whats-ahead.html)
- [LawNext — LexisNexis Next-Gen Protege](https://www.lawnext.com/2025/12/lexisnexis-unveils-the-next-generation-of-its-protege-general-ai-callling-it-the-most-integrated-legal-ai-workflow-solution.html)
- [LegalAIWorld — Westlaw vs Lexis AI September 2025](https://legalaiworld.com/westlaw-precision-ai-vs-lexis-ai-september-2025-best-ai-legal-research-assistant-for-u-s-lawyers/)
- [Microsoft Learn — Copilot Extensibility](https://learn.microsoft.com/en-us/microsoft-365-copilot/extensibility/overview-business-applications)
- [Microsoft Learn — Teams Conversational AI UX](https://learn.microsoft.com/en-us/microsoftteams/platform/bots/how-to/teams-conversational-ai/ai-ux)
- [GitHub Copilot Chat Cheat Sheet](https://docs.github.com/en/copilot/reference/chat-cheat-sheet)
- [Smashing Magazine — Design Patterns for AI Interfaces (July 2025)](https://www.smashingmagazine.com/2025/07/design-patterns-ai-interfaces/)
- [Nielsen Norman Group — Prompt Controls in GenAI Chatbots](https://www.nngroup.com/articles/prompt-controls-genai/)
- [ShapeOf.AI — UX Patterns for AI Design](https://www.shapeof.ai/)
- [Google A2UI — Agent-Driven Interfaces](https://developers.googleblog.com/introducing-a2ui-an-open-project-for-agent-driven-interfaces/)
- [AG-UI Protocol](https://docs.ag-ui.com/)
- [OpenAI Prompt Packs (September 2025)](https://mlq.ai/news/openai-releases-enterprise-ready-prompt-packs-across-key-roles/)
