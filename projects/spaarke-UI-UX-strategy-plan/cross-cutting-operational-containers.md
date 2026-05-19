# Cross-Cutting Concept: Operational Containers

> **Status**: Draft v0.1
> **Slug**: cross-cutting-operational-containers
> **What this is**: A structural property of the application, not a peer pattern. Operational Containers describe how matters (and similar scoped contexts like investigations, projects, deals) stay coherent across sessions, across the seven UI patterns, and across users on the same team.

---

## 1. Why This Is Cross-Cutting, Not a Pattern

The seven UI patterns describe *what the user does* in a given moment — triage in Queue, inspect in Entity, work a document in Canvas. None of them say *how the work stays glued together over time*.

A matter is not a screen. It is the context that all the screens share. When a user opens Spaarke on Monday morning and is working on the Acme NDA matter, every pattern they touch — the Queue of pending NDAs, the Canvas with the document, the Conversation pane asking about playbook departures, the Entity view of the counterparty — should be scoped to that matter. When they pivot to a different matter, the panes should follow. When they close the laptop and reopen Tuesday morning, the relevant matter should still be in play, with their work in progress recoverable.

This persistent, scoped, recoverable context is the *operational container*. It's not a pattern because it has no specific UI of its own; it's a structural property the patterns operate within.

---

## 2. What Operational Containers Cover

Five behaviors fall under this concept:

1. **Session restore** — when a user returns to the application, their last working state is recoverable.
2. **Matter-as-context propagation** — when a user is "in" a matter, every pattern in every pane is scoped to that matter.
3. **Cross-matter pivot** — when a user changes matters mid-session, the panes follow, with privilege-aware cleanup.
4. **Matter memory** — the matter accumulates state over time (decisions made, documents reviewed, AI conversations had) that the user can return to.
5. **"Pick up where you left off"** — the application's first-screen-after-login surfaces the in-progress work the user most likely wants to resume.

Each is discussed below.

---

## 3. Session Restore

### 3.1 What good looks like

The user closes the application Friday evening with three Workspace tabs open (the Acme matter Canvas, the open-NDA queue, and a counterparty Entity view). Monday morning they open the application and find those three tabs restored, the Conversation pane history scoped to the matter they were last in, the Context pane showing the same matter context. Their unsaved work-in-progress (a redline they started, a form they was filling) is intact.

This is the baseline expectation set by every modern productivity app (browser sessions, IDE workspaces, Notion, Linear). Falling short of it makes Spaarke feel less serious than its peers.

### 3.2 What it requires

- **State serialization** at the level of each Workspace tab's widget, the Context pane's current state, the Conversation pane's scope, and any work-in-progress not yet committed.
- **A restore latency budget**, probably under 500ms after login. Longer makes the application feel slow at the moment users have least patience for it.
- **Graceful degradation** when restore can't fully succeed — if a widget's state can't be restored (data changed, permissions changed), the application shows what it can and clearly indicates what it couldn't.
- **An explicit "start fresh" option** for users who don't want yesterday's state restored. Not the default, but available.

### 3.3 Current design positions

- spec.md FR-502 names session-restore latency (<500ms) and behavior (per design.md §5.3).
- design.md §5 covers Work History as a related concept.
- Widget serialize / restore is supported (design.md §6.2, spec.md FR-201).

The architecture is in place. The user-facing UX conventions — what the user sees during restore, how partial restore is handled, where the "start fresh" option lives — are not fully specified.

---

## 4. Matter-as-Context Propagation

### 4.1 What good looks like

The user selects (or arrives at) a matter. Every pane in the application updates to be scoped to that matter:

- Conversation pane's chat is grounded to that matter's documents, records, and prior conversations.
- Workspace tabs show widgets relevant to that matter; new tabs created in this scope inherit it.
- Context pane shows that matter's information (parties, deadlines, status, recent activity).
- Search defaults to within-matter; the user can broaden explicitly.
- New records created (a new document, a new related party) attach to this matter by default.

This is *not* an explicit click on each pane. It is a property of the application's state: "the user is in matter X" propagates everywhere unless explicitly overridden.

### 4.2 What it requires

- **A clear visual indicator** of which matter the user is currently in. Not a tiny breadcrumb — a prominent, persistent, glanceable signal. Possibly in the header. Possibly in the Context pane. The user should never wonder which matter their actions will affect.
- **Defaults that follow the matter scope** — search, create, AI grounding, retrieval.
- **Explicit broadening when needed** — the user can search across matters, but the action is explicit and the result clearly tagged.
- **Visible indication when a pane is *not* matter-scoped** — e.g., the Conversation pane when the user is asking a general question vs. a matter-grounded one.

### 4.3 Current design positions

The design assumes matter scoping but doesn't fully specify the propagation rules and the visual signaling. This is the largest under-specified area in the Operational Containers concept.

---

## 5. Cross-Matter Pivot

### 5.1 What good looks like

The user is in matter A, mid-conversation in the Conversation pane, with two Workspace tabs open. They click into matter B (from a Queue, from search, from a notification). The application:

- Updates the visual indicator to show matter B.
- Strips the prior matter's privileged content from the conversation context (design.md §9.2.4, spec.md FR-405).
- Shows a brief notification — "Conversation context updated for [matter B]. Prior matter chat archived." — that is informational, not interruptive.
- Updates the Context pane to matter B.
- Preserves the Workspace tabs from matter A but visually indicates they belong to A, not B. (Alternative: prompts the user about whether to close them or keep them.)
- Restores any prior work-in-progress for matter B if available.

The pivot is a meaningful action with audit consequence (the system records the pivot, the cross-matter exposure boundary is enforced) but it is also smooth — it doesn't feel like restarting the application.

### 5.2 What it requires

- **Privilege-aware retrieval** as already specified.
- **A clear UX for the workspace-tabs question** — what happens to existing tabs when matter changes. This needs a designer's call and prototype testing.
- **An informational notification** for the cross-matter content stripping — not a permission dialog, not silent, but informational.
- **An undo / "I didn't mean to pivot" affordance** within a short window — for users who clicked into the wrong matter.

### 5.3 Design-challenge findings

The Queue pattern raised the question of whether a queue is matter-scoped or cross-matter. Different in-house counsel workflows answer this differently. The architecture probably needs to support both (single-matter queues for matter-internal triage; cross-matter queues for portfolio-wide triage like "all NDAs awaiting my review"). The pivot UX is different in each case and needs working through.

---

## 6. Matter Memory

### 6.1 What good looks like

A matter accumulates state over time:

- Documents attached, with version history.
- Decisions made (and the conversations that led to them, where the user retained them).
- AI conversations scoped to the matter, retained for later reference.
- Tasks completed and outstanding.
- Activity timeline of who did what when.

When the user returns to a matter weeks later, this accumulated memory is *available without effort*. They don't have to remember where they put things. The matter's state is the application's memory.

This is what makes Spaarke usable for legal work that spans months. Without matter memory, every return to a matter is a re-orientation cost.

### 6.2 What it requires

- **Durable matter state** — committed work persists. (Architecturally trivial; user-facing UX must make it visible.)
- **Discoverable memory** — when the user returns to a matter, the relevant history is surfaced (recent activity, most-recently-edited documents, last conversation, outstanding tasks).
- **AI conversations scoped to matter and retained** — the chat history for matter X is recoverable when the user returns to matter X, separately from the chat history for matter Y.

### 6.3 Current design positions

- Work History (design.md §5) covers some of this at the architecture level.
- AI conversation scoping is implicit in the privilege-aware retrieval design but the user-facing "show me the prior conversations on this matter" affordance isn't fully specified.

---

## 7. "Pick Up Where You Left Off"

### 7.1 What good looks like

The user opens Spaarke. The first screen they see is not a generic dashboard or a chat prompt — it's a curated view of what they were doing last and what's most likely relevant now:

- "Resume: Acme NDA review (Canvas, 4 sections reviewed of 12)"
- "In progress: 3 NDAs in your triage queue"
- "Recent: Counterparty record for [name] (edited 2 hours ago)"
- "Suggested: New high-priority intake request (Matter [number])"

The user clicks one and lands in the right pattern with the right state.

This is the "first-30-seconds" experience that determines whether users feel the application is *theirs* or *generic*. Get it right and users feel oriented; get it wrong and every session begins with re-navigation.

### 7.2 What it requires

- **Intelligent surfacing** of in-progress work, recent work, and likely-relevant new work.
- **Use of matter context** to scope what gets surfaced.
- **AI-suggested relevance** where appropriate (the AI flags items the user is likely to want, learning from prior behavior).
- **Skip / dismiss option** for users who'd rather go directly to a specific destination.

### 7.3 Current design positions

Not fully specified. This is where the Operational Containers concept produces a new design requirement — a "home screen" or "session start" view that synthesizes session restore, matter memory, and AI-surfaced relevance.

---

## 8. Cross-Cutting Design-Challenge Findings

These flow to `/challenges/design-challenges.md`.

| Finding | Current design | Required addition |
|---|---|---|
| **Matter-as-context propagation rules aren't fully specified.** | Implicit in the design but not stated in detail. | A clear specification of which panes follow the matter scope by default, which are broader, and how the user knows. |
| **Cross-matter pivot UX isn't end-to-end specified.** | Privilege-aware retrieval is architected; the UX of the pivot (notification, tab handling, undo) isn't. | A concrete pivot UX spec. Likely needs prototype testing. |
| **The "pick up where you left off" home screen isn't defined.** | Not in the current design. | A new design component — call it MatterHomeView or SessionStartView — that synthesizes session restore, matter memory, and relevance. |
| **Matter visual indicator placement isn't specified.** | Implicit. | A clear decision about where the "you are in matter X" signal lives and how it stays visible across all interactions. |
| **AI conversation history per matter isn't user-facing.** | Architecturally retained; user-facing recovery affordance not specified. | A "prior conversations on this matter" affordance, probably in the Conversation pane's history view. |

---

## 9. Why This Concept Matters

Most enterprise applications fail at Operational Containers in subtle, cumulatively expensive ways. Users open the app, navigate to find what they were doing, search for the document, re-establish context manually. Each of these is small in isolation; collectively they are a productivity tax that destroys the apparent value of the tool.

Getting Operational Containers right is what makes Spaarke feel like a *system that knows the user's work*, as opposed to *a set of features they have to operate*. This is the difference between an application users tolerate and one they rely on.

It is also, not coincidentally, a primary differentiator against the "everything in chat" competitors. Harvey and Hebbia have chat conversations; they don't have matter-scoped, persistent, operationally coherent containers. The chat is brittle as a primary surface; the operational container is durable.

---

*Draft v0.1 — 2026-05-18.*
