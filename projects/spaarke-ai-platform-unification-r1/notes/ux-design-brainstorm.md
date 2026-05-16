# Spaarke AI — UX/UI Design Brainstorm

> **Date**: 2026-05-16
> **Context**: Evolving from CRUD-oriented enterprise UI to AI-directed experience
> **Target users**: 80% consumers (not data entry operators)

---

## The Core Insight

Traditional enterprise apps are **database outward** — the UI reflects the schema. Forms, grids, lookups. Users navigate to records, read fields, update values. AI chat was bolted on as a sidebar assistant.

The Spaarke AI vision flips this: **user intent inward**. The user expresses what they want to accomplish, and the system orchestrates the data, tools, and workflows to make it happen. The database still exists — but the user rarely touches it directly.

This is the difference between:
- **Old**: Navigate to Matter → Open Documents tab → Find contract → Click "Analyze" → Read results
- **New**: "Review the Smith contract for liability exposure" → AI loads the contract, runs analysis, presents findings in the action pane, highlights key clauses

---

## Your Three-Pane Concept — Analysis

### What You Proposed

| Pane | Role | Key Idea |
|------|------|----------|
| Left | **Chat** | Conversational AI, specialized per loaded agent/playbook |
| Center | **Action** | Working area — document review, reports, drafts, results |
| Right | **Playbooks** | AI workflow selector that configures the agent and chat |

### What Works Well

1. **Playbook-driven specialization** is powerful. Instead of one generic chatbot, the user chooses a "mode" that loads the right agent, tools, knowledge, and system prompt. This is what makes the AI actually useful vs. generic.

2. **Center as action pane** (not just "output") is a critical distinction. "Output" implies passive display. "Action" implies the user and AI **co-work** on the content — editing a draft, annotating a document, adjusting a chart, approving recommendations.

3. **Chat as orchestration** (not just Q&A) is the right model. The chat isn't just answering questions — it's driving the workflow, proposing next steps, and manipulating the action pane content.

### Where I'd Refine

**The right pane has a lifecycle problem.** Once the user selects a playbook, the right pane's job is done — it becomes dead space. My recommendation: the right pane should be **contextual**, adapting its role based on the active workflow:

| Workflow State | Right Pane Shows |
|---------------|------------------|
| No playbook selected | **Playbook gallery** — cards for available workflows |
| Playbook active, no document | **Context panel** — entity info, related records, recent activity |
| Document loaded | **Source/reference panel** — the document being reviewed, citations, related docs |
| Multi-step workflow | **Progress tracker** — playbook steps, completion status, next actions |
| Research mode | **Sources & citations** — web results, case law, knowledge base hits |

This way the right pane is always useful. It transforms from "pick a playbook" to "here's your context" seamlessly.

---

## Recommended Design: Adaptive Three-Pane

### The Panes (Refined)

```
┌─────────────────┬──────────────────────────┬──────────────────┐
│   CONVERSATION   │      WORKSPACE           │    CONTEXT       │
│                 │                          │                  │
│  Chat with AI   │  Active work surface     │  Adaptive panel: │
│  Specialized    │  - Document editor       │  - Playbooks     │
│  per playbook   │  - Report viewer         │  - Entity info   │
│  Orchestrates   │  - Data analysis         │  - Sources       │
│  the workflow   │  - Draft builder         │  - Progress      │
│                 │  - Search results        │  - Related items │
│  Action chips   │  - Comparison view       │                  │
│  Suggestions    │                          │  Collapses when  │
│  Status updates │  Tabs for multiple       │  not needed      │
│                 │  active items            │                  │
└─────────────────┴──────────────────────────┴──────────────────┘
```

### The Flow

**Stage 1: Landing (No Context)**
```
┌─────────────────┬──────────────────────────┬──────────────────┐
│                 │                          │                  │
│  Welcome to     │   "What would you like   │  PLAYBOOKS       │
│  Spaarke AI     │    to work on?"          │                  │
│                 │                          │  📄 Review Doc   │
│  [Prompt        │   Recent items:          │  🔍 Research     │
│   buttons]      │   • Smith contract       │  📊 Financials   │
│                 │   • Q2 budget review     │  ✏️ Draft         │
│  Or just type   │   • Jones deposition     │  📋 Case Mgmt    │
│  what you need  │                          │  ⚖️ Compliance    │
│                 │                          │                  │
└─────────────────┴──────────────────────────┴──────────────────┘
```

The center pane shows recent items and a prompt. The right pane shows available playbooks. The user can EITHER type in chat OR click a playbook OR click a recent item.

**Stage 2: Playbook Selected (e.g., "Review Document")**
```
┌─────────────────┬──────────────────────────┬──────────────────┐
│                 │                          │                  │
│  📄 Review Doc  │   Select a document:     │  DOCUMENT INFO   │
│  ─────────────  │                          │                  │
│  AI: "Which     │   [Upload]  [Browse]     │  (empty until    │
│  document would │                          │   doc selected)  │
│  you like to    │   Recent documents:      │                  │
│  review?"       │   • Smith Agreement.pdf  │                  │
│                 │   • Lease Draft v3.docx  │                  │
│  [Upload File]  │   • NDA Template.pdf     │                  │
│  [Browse Docs]  │                          │                  │
│                 │                          │                  │
└─────────────────┴──────────────────────────┴──────────────────┘
```

Playbook loads the Review Document agent. Chat specializes. Center shows document selection. Right pane waits for context.

**Stage 3: Active Work (Document Loaded)**
```
┌─────────────────┬──────────────────────────┬──────────────────┐
│                 │                          │                  │
│  📄 Review Doc  │   DOCUMENT VIEWER        │  REVIEW PANEL    │
│  ─────────────  │   ┌──────────────────┐   │                  │
│  AI: "I've      │   │ Smith Agreement  │   │  Key Findings:   │
│  identified 3   │   │                  │   │  ⚠ Liability §7  │
│  risk areas.    │   │ Section 7.2:     │   │  ⚠ Indemnity §12 │
│  §7.2 has an    │   │ ████████████     │   │  ✓ Term OK       │
│  unusual        │   │ ████ highlighted │   │                  │
│  liability      │   │ ████████████     │   │  Related Docs:   │
│  clause..."     │   │                  │   │  • Prior version │
│                 │   └──────────────────┘   │  • Similar NDA   │
│  You: "Compare  │                          │                  │
│  with standard" │   [Edit] [Annotate]      │  Progress: 2/5   │
│                 │   [Export] [Approve]      │  steps complete  │
└─────────────────┴──────────────────────────┴──────────────────┘
```

All three panes are coordinated. Chat drives the analysis. Center shows the document with AI highlights. Right shows findings, related context, and progress.

**Stage 4: Multi-Task (User Opens Another Item)**
```
┌─────────────────┬──────────────────────────┬──────────────────┐
│                 │  [Smith Contract] [Budget]│                  │
│  📊 Financials  │  ┌──────────────────────┐│  FINANCIAL DATA  │
│  ─────────────  │  │   Q2 Budget Report   ││                  │
│  AI: "The Q2    │  │                      ││  Matter: Smith   │
│  budget shows   │  │  ┌────┐ ┌────┐      ││  Budget: $250K   │
│  15% over on    │  │  │ ▌▌ │ │ ▌▌ │      ││  Spent: $287K    │
│  outside        │  │  │ ▌▌ │ │ ▌▌ │      ││  Variance: +15%  │
│  counsel..."    │  │  └────┘ └────┘      ││                  │
│                 │  │  Jan  Feb  Mar  Apr  ││  Invoices:       │
│  You: "Show me  │  │                      ││  • Feb: $45K     │
│  the invoices"  │  │  [Download] [Share]  ││  • Mar: $62K     │
│                 │  └──────────────────────┘│  • Apr: $38K     │
└─────────────────┴──────────────────────────┴──────────────────┘
```

User switched to a financial analysis. The center pane has **tabs** — they can flip back to the Smith Contract review. The chat context switches to the financial agent. The right pane shows financial data.

---

## Key Design Principles

### 1. Playbooks Are "Modes", Not Just Prompts

A playbook doesn't just set a system prompt — it reconfigures the entire experience:

| Playbook Configures | Example: "Review Document" |
|--------------------|-----------------------------|
| Chat agent | Document review specialist with clause analysis tools |
| Center widget | Document viewer with annotation, redlining |
| Right panel | Review checklist, key findings, related docs |
| Available actions | Highlight, annotate, compare, approve, reject |
| Suggestions | "Check indemnity clause", "Compare with template" |

### 2. The Center Pane Is a Workspace, Not Just Output

Key distinction:
- **Output pane** (current): AI shows results. User reads them. Passive.
- **Workspace** (proposed): AI and user co-edit. User can interact, modify, annotate, approve. Active.

The workspace should support:
- **Tabs** for multiple active items (like browser tabs)
- **Widget persistence** — switching playbooks doesn't destroy the workspace
- **Direct manipulation** — click a chart bar to drill down, select text to ask AI about it
- **Actions** — each workspace widget has contextual actions (Export, Share, Approve, Edit)

### 3. Chat Is the Orchestrator, Not Just Q&A

The chat should:
- **Propose next steps** after completing an action ("I've finished the review. Would you like me to draft a summary memo?")
- **Show progress** for multi-step workflows ("Step 2 of 5: Checking indemnity clauses...")
- **Accept commands** inline ("compare with the v2 draft" → loads comparison in center pane)
- **Confirm actions** before making changes ("I'll update the budget allocation. Confirm?")
- **Surface proactively** — if the AI notices something while the user is working, it should gently surface it in chat ("I noticed §12 references an outdated statute. Want me to check?")

### 4. The Right Pane Is Contextual Intelligence

It shouldn't be static. It should show what's most useful RIGHT NOW:

| Context | Right Pane Shows |
|---------|-----------------|
| No workflow active | Playbook gallery + recent items |
| Document loaded | Document metadata, related docs, review checklist |
| Financial analysis | KPIs, comparisons, drill-down data |
| Research session | Sources, citations, knowledge base results |
| Multi-step workflow | Progress tracker with step status |
| CRUD action needed | Quick-entry form (inline, not modal?) |

### 5. CRUD Actions: Inline, Not Modal (When Possible)

You mentioned CRUD actions launching modals. Consider: modals break the flow. The user leaves the AI context, fills a form, comes back.

Alternative: **inline CRUD in the action pane**. When the user says "create a new matter for Smith", the center pane could show a streamlined matter creation form right there — pre-filled with what the AI already knows. The chat guides: "I've pre-filled the matter details from our conversation. Review and confirm."

For complex CRUD (many fields, lookups, validation): modal is fine. But for simple creates/updates, keep the user in the flow.

---

## Additional Recommendations

### Command Palette (Ctrl+K)

For power users, add a command palette overlay:
```
┌────────────────────────────────────┐
│ 🔍 Type a command or search...     │
│                                    │
│  Recent:                           │
│    📄 Smith Agreement review       │
│    📊 Q2 Budget analysis           │
│                                    │
│  Commands:                         │
│    /review — Start document review │
│    /research — Legal research      │
│    /compare — Compare documents    │
│    /draft — Draft a document       │
│                                    │
│  Matters:                          │
│    Smith v. Jones                   │
│    Project: Due Diligence Q2       │
└────────────────────────────────────┘
```

This gives keyboard-first users a fast path without using the playbook panel.

### Session Continuity

The system should remember where the user left off:
- "Welcome back. You were reviewing the Smith contract — §7.2 liability analysis was 60% complete. Resume?"
- Recent sessions should show not just chat history but workflow state

### Notification Integration

When AI background processes complete (long-running analysis, scheduled reports):
- Badge on the Spaarke AI nav item
- Notification in the welcome screen
- "Your document analysis is ready — view results?"

### Adaptive Complexity

New users see guided prompts and playbook cards. Power users see a command palette and keyboard shortcuts. The UI should adapt:
- First 5 sessions: prominent playbook cards, guided suggestions
- After 5 sessions: smaller playbook panel, command palette shortcut shown
- User preference: toggle between "guided" and "expert" mode

---

## What This Means Architecturally

### Changes from Current Implementation

| Current | Proposed | Effort |
|---------|----------|--------|
| Right pane = "Sources" (static) | Right pane = contextual (playbooks → entity info → sources → progress) | Medium — new right pane state machine |
| Output widgets are display-only | Workspace widgets support interaction (edit, annotate, approve) | High — widget API needs action callbacks |
| Chat and panes loosely connected | Chat orchestrates pane state changes | Medium — structured SSE events for pane commands |
| Single workspace item | Tabbed workspace with multiple items | Medium — tab management in center pane |
| Playbooks are backend-only | Playbooks drive the full UI configuration | Medium — playbook metadata includes UI config |
| Welcome screen has 4 buttons | Welcome + playbook gallery + recent items + command palette | Low-Medium |

### What We Can Do Now (R1)

1. **Rename right pane** from "Sources" to contextual — show playbooks when idle, sources when active
2. **Add workspace tabs** to center pane (multiple widgets)
3. **Enhance welcome screen** with playbook gallery in the right pane
4. **Chat-driven pane coordination** via the SSE events we already built

### What's R2

1. Interactive workspace widgets (edit, annotate, approve actions)
2. Inline CRUD forms in the action pane
3. Command palette (Ctrl+K)
4. Session continuity and workflow state persistence
5. Adaptive complexity (guided vs. expert mode)
6. Notification integration for background AI processes

---

## Open Questions

1. **Should playbooks be visible at all times** (right pane) or **hidden after selection** (only in a menu/command palette)?
2. **How deep should chat orchestration go?** Should the AI proactively suggest switching playbooks mid-session?
3. **Tab limits** — how many workspace items can be open simultaneously? (Memory/performance concern)
4. **Mobile** — three panes don't work on mobile. What's the mobile experience? Chat-only with swipe to workspace?
5. **Collaboration** — can two users be in the same Spaarke AI session? (Future — real-time collab is out of scope for R1)
6. **Offline** — what works offline in the PWA? (Cached playbook configs, recent session list, but no AI calls)

---

*This document captures the brainstorm as of 2026-05-16. To be refined into a formal design spec for R2.*
