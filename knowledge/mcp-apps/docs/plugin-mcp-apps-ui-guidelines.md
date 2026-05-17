---
source: https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/plugin-mcp-apps-ui-guidelines
fetched: 2026-05-14
---

# User experience guidelines for MCP apps in declarative agents for Microsoft 365 Copilot

> Microsoft Learn page metadata: `ms.date: 2026-03-06`, `updated_at: 2026-05-12`, `git_commit_id: 29ee91a4caf1dc5765bfc91e81935402f308fc58`.

This guide provides user experience (UX) guidance for developers building MCP-based UI experiences in Microsoft 365 Copilot. It covers patterns for creating native, coherent, and task-focused interactions that integrate smoothly into the conversational flow in Copilot.

## UX principles

Building a great Copilot agent with the MCP based rich UI means delivering a focused, conversational experience that feels native to Copilot. Copilot agents should feel like helpful extensions of the conversation surfacing the right action at the right time rather than full applications rebuilt inside Copilot.

### Deliver conversational value

- Agent should provide greater value inside Copilot than as a standalone UI.
- Leverage natural language, thread context, and multi-turn interaction to enable workflows that would be difficult or inefficient in a traditional navigation model.
- Design experiences that take advantage of conversation, not replicate existing flows.

### Extract capabilities, don't replicate interfaces

Avoid porting your full application experience into Copilot. Instead, identify high-value, atomic capabilities that can be exposed as tools. Each capability should:

- Require only the minimum necessary inputs
- Return structured, reliable outputs
- Enable model to confidently determine the next step

### Design to feel native to Copilot

- Leverage Copilot's design system, components, and interaction patterns to ensure a seamless, predictable experience.
- Consistency reduces cognitive load, increases predictability, and minimizes the need for users to learn new interaction models.

### Preserve human control

Trust is foundational to enterprise adoption. Users must remain the ultimate decision-makers, particularly when actions affect enterprise data. Provide:

- Clear visibility into agent actions
- Explicit confirmations for sensitive operations
- Transparent outcomes of what was created, modified, or updated

### Scale density with intent

Adapt the visual footprint of your UI to the user's immediate need.

- Use the inline widget for glanceable summaries and high-level actions.
- Use the expanded view for tasks where the user needs a larger real estate to work alongside the chat.

## Chat surfaces

Chat surfaces are the primary way users interact with agents built using the Copilot Apps SDK, defining how an app appears and behaves within the Copilot conversation.

When designing for Copilot, follow these core principles:

- **Conversation-first:** The chat remains the primary interaction model.
- **Progressive complexity:** Start lightweight. Expand only when needed.
- **Context preservation:** Users should not lose conversational context.
- **Clarity over duplication:** App UI and model text should complement each other - not repeat content.

Copilot currently supports two primary chat surfaces. Each surface serves a distinct purpose and should be chosen based on the complexity and depth of the interaction.

- All apps must support **inline mode**, where inline widgets appear before the generated model response.
- **Side-by-side mode** is an optional surface that can be used when richer interactions are needed.

### Inline mode

Inline mode is the default, in-conversation chat surface in Copilot. Inline is not a mini-application. It enhances conversation, it does not replace it.

#### When to use inline mode

Inline is recommended for:

- Previews (documents, images, drafts)
- Confirmations
- Simple actions
- Quick decision prompts

Inline experiences should remain concise and ideally fit within a single scroll of the response.

#### Inline mode layout

- **Agent header:** Identifies the responding agent and establishes context.
- **Inline widget:** Used to display structured content, previews, or action controls.
- **Model response:** A short, model-generated message shown after the widget to suggest edits, next steps and related actions.

#### Inline widget

Inline widgets appear directly within the chat flow, allowing users to view information and take action without leaving the conversation. They provide quick confirmations, simple actions, or visual aids.

- **Title:** Include a title if your card is document-based or contains items with a parent element.
- **Expand to side-by-side view:** Use to open a side-by-side mode if the card contains rich media or interactivity.
- **Actions:** Limit to two actions, placed at bottom of card. Actions should perform either a conversation turn or a tool call.

##### Interaction guidelines

- **Keep interaction focused:** Avoid multi-step flows, nested navigation, or deep configuration. If the task requires iteration, comparison, or extensive editing, move to Side-by-Side.
- **Show summaries, not systems:** Inline displays previews, not full applications. Avoid internal scrolling, pagination, tabs, filters, or multi-level grouping.
- **Make state explicit:** Inline interactions must provide clear system feedback like loading state, disabled state, success confirmation, error state with recovery option. Never rely on model text alone to communicate system status.
- **Preserve conversational flow:** A widget should fit comfortably within a single response scroll. It should avoid dominating the viewport. It should complement the model response, not compete with it.

### Side-by-side mode (optional)

Side-by-side mode provides an expanded, immersive workspace that appears alongside the conversation. It is designed for richer workflows that cannot be effectively delivered within the inline surface. Unlike inline mode, which is optimized for lightweight interactions, side-by-side mode creates a dedicated workspace for deeper engagement, while preserving conversational context.

Side-by-side mode is optional and should be used intentionally.

#### When to use side-by-side mode

Use side-by-side mode when the experience requires:

- Multi-step editing or configuration
- Iterative workflows with persistent state
- Complex visual layouts (tables, canvases, dashboards)
- Extended review or comparison tasks
- Rich authoring (document drafting, design editing, structured inputs)
- Workspace-level interaction beyond a single scroll
- If the task can be completed in a concise, single-turn interaction, use inline mode instead.

#### Side-by-side layout

- **Conversation pane:** The Copilot chat that remains the primary source of intent and control.
- **Chiclet card:** When side-by-side mode is active, the original inline widget collapses into a compact card in the conversation, preserving context with the expanded workspace.
- **Side-by-side panel header:** Displays the agent identity (icon and name) and includes a handoff option to the full application.
- **App workspace:** Larger MCP-rendered surface for editing, reviewing, or managing structured content. This is a contextual workspace within Copilot, not a standalone application shell.
- **Contextual controls:** Task-specific controls within the workspace (for example: edit tools, formatting, zoom, export).

##### Interaction guidelines

- **Keep workspace contextual:** Side-by-side mode provides a focused, task-specific workspace - not a full application shell. Avoid global navigation, multi-tab systems, settings panels, or unrelated features. If the experience resembles your entire SaaS product, it exceeds scope.
- **Preserve chat as primary:** The conversation remains the source of intent and control. Users must be able to continue chatting while Side-by-side mode is open, ask clarifying questions mid-task and see Copilot reasoning alongside their workspace.
- **Scope to the active task:** Side-by-side mode should support a single coherent workflow. Avoid switching between unrelated entities and launching nested experiences. If multiple workflows are needed, split into separate surfaces or actions.
- **Make state explicit:** Side-by-side interactions must provide clear system feedback like loading state, disabled state, success confirmation, error state with recovery option. Never rely on model text alone to communicate system status.
- **Maintain progressive escalation:** Side-by-side mode should be entered intentionally. Do not default to side-by-side for simple previews or quick confirmations.

## Best practices

### ✅ Preserve conversational flow

Keep inline widgets lightweight and action-oriented. Support up to two primary actions (e.g., Approve, Edit, Download). If the task requires deep navigation, multi-step workflows, or heavy configuration, hand off to side-by-side mode.

### ✅ Use Fluent components for native fit

Inline experiences should feel like a natural extension of Copilot. Use Copilot-aligned [Fluent 2](https://fluent2.microsoft.design/) components, spacing, typography, and tokens to ensure visual and interaction consistency.

### ✅ Provide widget state handling

Widgets must provide clear system feedback like loading state, disabled state, success confirmation, and error state with recovery option.

### ❌ Don't use a widget to resemble a full application

Inline mode should feel like a natural extension of chat, not an entire application embedded inside it.

### ❌ Don't duplicate Copilot features in the widget

Avoid recreating chat capabilities (prompt input, suggestions, reasoning summaries, retry controls) inside the widget. Duplication creates confusion, visual noise, and fragmented interaction models.

### ❌ Avoid deep navigation in widgets

Widgets should not contain multiple tabs, or deeper navigation. Consider splitting these into separate cards or tool actions.

### ❌ Avoid large, scroll-heavy layouts

Inline widgets should be concise and glanceable. Avoid vertical scroll within the widget. Height should feel widget-sized, not application-sized. If content requires scrolling, complex tables, or detailed editing, transition to side-by-side mode.

### ❌ Don't duplicate content in model text and widget

Do not repeat the same information in both the widget and the model message.

## Visual design guidelines

Visual and interaction consistency is critical to Copilot's user experience. Apps are expected to align with the Fluent design system so that users experience predictable behavior, familiar controls, and consistent experiences across apps. This consistency helps users build trust, move confidently between workflows, and safely take action across multiple apps within Copilot.

### Fluent Copilot theme guidelines

Create beautiful, cohesive Microsoft experiences using the Fluent 2 UI kits. Built in Figma, the Fluent 2 UI kits contain design assets that map to the code libraries.

- **Color:** [Fluent 2 > Color](https://fluent2.microsoft.design/color)
- **Button:** [Fluent 2 > Button](https://fluent2.microsoft.design/components/web/react/core/button/usage)
- **Typography:** [Fluent 2 > Typography](https://fluent2.microsoft.design/typography)
- **Radius:** [Fluent 2 > Shapes](https://fluent2.microsoft.design/shapes#corner-radius)
- **Spacing:** Global padding of an app card should be 24 pixels.
- **Iconography:** [Fluent 2 > Iconography](https://fluent2.microsoft.design/iconography)
