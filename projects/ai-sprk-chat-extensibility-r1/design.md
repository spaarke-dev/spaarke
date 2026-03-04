# SprkChat Extensibility — Slash Commands & Quick Actions

> **Project**: ai-sprk-chat-extensibility-r1
> **Status**: Design
> **Priority**: 2 (Parallel with #3, depends on #1 for context-aware commands)
> **Branch**: work/ai-sprk-chat-extensibility-r1

---

## Problem Statement

Users interact with SprkChat exclusively through free-text chat. There is no structured way to invoke specific AI capabilities, switch playbooks, perform system operations, or access contextual quick actions. The AI-generated suggestion chips (post-response) are helpful but not user-invocable — the user cannot proactively say "I want to summarize" without typing it out.

Modern AI chat interfaces (Claude Code, GitHub Copilot Chat, Slack) provide a `/command` menu that gives users structured access to capabilities. SprkChat needs this pattern to bridge the gap between "chat with AI" and "use AI tools."

## Goals

1. **Slash command menu** — `/` keystroke opens a filterable, keyboard-navigable command palette
2. **Built-in system commands** — `/clear`, `/new`, `/export`, `/history` for session management
3. **Playbook commands** — Switch active playbook mid-session from the command menu
4. **Action commands** — Invoke specific AI actions (summarize, search, analyze) as structured commands
5. **Context-aware filtering** — Available commands vary based on current entity type and page type
6. **Quick-action bar** — Optional persistent row of contextual action chips above the input area

## What Exists Today

### SprkChat Component
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

## Design

### Command Registry

```typescript
interface ISlashCommand {
  id: string;                          // Unique identifier (e.g., "clear", "search")
  label: string;                       // Display label (e.g., "Clear conversation")
  description: string;                 // Short description shown in menu
  icon?: React.ReactElement;           // Fluent icon (optional)
  category: "system" | "playbook" | "action" | "navigation";
  shortcut?: string;                   // Keyboard shortcut hint (e.g., "Ctrl+L")
  isAvailable?: (context: CommandContext) => boolean;  // Dynamic availability
  execute: (context: CommandContext, args?: string) => void | Promise<void>;
}

interface CommandContext {
  entityType: string;
  entityId: string;
  pageType: string;
  sessionId: string | null;
  currentPlaybookId: string;
  switchPlaybook: (playbookId: string) => void;
  clearSession: () => void;
  sendMessage: (message: string) => void;
  // ... other actions
}
```

### Built-in Commands

| Command | Category | Description | Behavior |
|---------|----------|-------------|----------|
| `/clear` | system | Clear conversation | Delete session, start fresh |
| `/new` | system | New session | Create new session with current context |
| `/export` | system | Export chat | Download conversation as text/markdown |
| `/history` | system | View history | Show previous sessions list |
| `/switch [name]` | playbook | Switch playbook | Change active AI personality |
| `/search [query]` | action | Search documents | Semantic search across entity's documents |
| `/summarize` | action | Summarize | Summarize current document or selection |
| `/analyze` | action | Run analysis | Execute analysis playbook on current context |
| `/draft [type]` | action | Draft document | Generate a document (memo, email, summary) |
| `/help` | system | Show commands | Display available commands |

### SlashCommandMenu Component

```
User types "/" in input
  ↓
SlashCommandMenu opens as Fluent Popover above input
  ↓
┌─────────────────────────────────┐
│  / Filter commands...           │
├─────────────────────────────────┤
│  System                         │
│    /clear    Clear conversation  │
│    /new      New session         │
│    /export   Export chat         │
│                                 │
│  Playbooks                      │
│    /switch Matter Assistant      │
│    /switch Legal Research        │
│                                 │
│  Actions                        │
│    /search   Search documents    │
│    /summarize Summarize          │
│    /analyze  Run analysis        │
└─────────────────────────────────┘
  ↑↓ keyboard navigation
  Enter = execute
  Esc = dismiss
  Typing filters list
```

**Behavior**:
- Opens when `/` is typed as the first character in an empty input (or at position 0)
- Closes on Escape, click-away, or Backspace past the `/`
- Keyboard navigation: Arrow Up/Down, Enter to select, Tab for category jump
- Type-ahead filtering: `/se` shows only `/search` and `/settings`
- Categories collapsed/expanded based on match count
- Width matches input width; max height ~300px with scroll

### Quick-Action Bar (Hybrid Pattern)

Persistent row of 3-4 contextual chips above the input area, populated from context mappings:

```
┌─────────────────────────────────────────┐
│  [Summarize] [Search docs] [Draft memo] │  ← Quick actions (from context mapping)
├─────────────────────────────────────────┤
│  Type a message...              / ▶     │  ← Input with / hint
└─────────────────────────────────────────┘
```

- Actions come from the current context's available playbooks + built-in actions
- Tapping a chip = sends a structured message or switches playbook + sends prompt
- Chips update when context changes (form → list = different actions)
- Hidden when SprkChat is in a narrow pane (<350px) — only slash menu available

### Input Integration

```typescript
// In SprkChatInput.tsx
const handleInputChange = (value: string) => {
  if (value === "/" && inputRef.current?.selectionStart === 1) {
    setShowCommandMenu(true);
    setCommandFilter("");
  } else if (showCommandMenu) {
    // Extract filter text after "/"
    const filterText = value.startsWith("/") ? value.slice(1) : "";
    setCommandFilter(filterText);
  }
};

const handleCommandSelect = (command: ISlashCommand) => {
  setShowCommandMenu(false);
  setInputValue(""); // Clear the /command text

  if (command.category === "action") {
    // Action commands: send as structured message
    command.execute(commandContext);
  } else {
    // System/playbook commands: execute directly
    command.execute(commandContext);
  }
};
```

## Phases

### Phase 1: Core Slash Menu (MVP)
- `ISlashCommand` type definitions and command registry
- `SlashCommandMenu` Fluent v9 component (popover, filter, keyboard nav)
- Input interception in `SprkChatInput`
- Built-in system commands: `/clear`, `/new`, `/help`
- Playbook commands from existing `useChatPlaybooks` data

### Phase 2: Action Commands
- `/search`, `/summarize`, `/analyze` as structured messages
- Action commands populated from context mapping playbooks
- Parameterized commands (e.g., `/search contract renewal`)
- Command execution feedback (loading state, success/error)

### Phase 3: Quick-Action Bar
- Contextual chip bar above input
- Populated from context mapping default actions
- Responsive: hidden in narrow pane, shown in wider layouts
- Syncs with slash command registry

### Phase 4: Custom Commands (Future)
- Admin-defined commands via Dataverse table (`sprk_aichatcommand`)
- Parameterized prompts: `/draft memo to {recipient} about {topic}`
- Command aliases and shortcuts

## Success Criteria

1. User types `/` → command menu appears with filtered, keyboard-navigable list
2. `/clear` creates a new session; `/switch [name]` changes the active playbook
3. Commands are filtered by current context (matter form shows matter-specific actions)
4. Quick-action chips provide one-tap access to top 3-4 contextual actions
5. Accessible: full keyboard navigation, screen reader labels, focus management

## Dependencies

- Project #1 (Context Awareness) for context-driven command filtering
- Existing `useChatPlaybooks` hook for playbook data
- Existing `switchContext` for playbook switching
- Fluent UI v9 Popover, MenuList components

## Risks

- Slash menu in narrow side pane (300px) may feel cramped (mitigation: full-width popover, compact layout)
- Too many commands overwhelm users (mitigation: context filtering reduces list to relevant items)
- Conflict with AI interpretation of "/" messages (mitigation: intercept before send, never send raw "/command" to API)
