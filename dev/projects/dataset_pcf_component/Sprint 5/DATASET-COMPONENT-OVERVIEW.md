# SDAP Universal Dataset Component Overview
## Mission Statement
Build ONE flexible Dataset PCF component that adapts to ANY Dataverse entity through configuration, not code changes. This component replaces all subgrids across the Spaarke platform.

## Core Architecture Principles
### Single Component, Infinite Configurations
- **Metadata-driven**: Automatically discovers entity schemas
- **Configuration-driven**: Behavior changes through declarative inputs
- **Context-aware**: Understands placement and adjusts accordingly

### Two Operating Modes
#### Dataset-Bound Mode (Preferred)
- Used on Model-Driven forms and views
- Platform provides security-trimmed data
- Automatic paging, sorting, filtering
- Zero configuration needed for basic functionality

#### Headless Mode (Custom Pages)
- Component fetches data via Web API
- Manual cache management
- Client-side operations
- Required for SPAs and custom pages

## Key Design Decisions
1. **One Binary**: Single compiled component for all entities
2. **No Entity-Specific Code**: All variations through configuration
3. **Fluent v9 Only**: Strict adherence to Microsoft's latest design system
4. **Performance First**: Virtual scrolling, lazy loading, caching
5. **Security by Default**: Respects platform security automatically

## Component Capabilities
### Supported View Modes
- Grid (table with headers)
- Card (visual tiles)
- List (compact single column)
- Kanban (board view by status)
- Gallery (image-focused)

### Built-in Commands
- open, openInNewTab
- create, delete
- refresh, export
- Custom commands via configuration

### Column Types Handled
- Text (single/multi-line)
- Numeric (whole, decimal, currency)
- Date/Time (relative/absolute)
- Lookups (with navigation)
- Choices (with badges)
- Files/Images (with previews)

## Success Criteria
- Works on any entity without code changes
- Maintains <500ms initial render
- Supports 10,000+ records with virtual scrolling
- Zero custom CSS required
- Passes all accessibility tests

## AI Coding Prompt
You are implementing a universal Dataset PCF that must run in model-driven apps and custom pages. Read this overview and:
- Scaffold a new dataset PCF named `Spaarke.UniversalDataset` that renders a Fluent UI v9 DataGrid by default and supports Card/List views via a `viewMode` input.
- Ensure a single, app-level `FluentProvider` is used. Consume the host theme if present; fall back to Spaarke themes.
- Enforce metadata/config-driven design: no entity-specific code; expose inputs for `columnBehavior`, `enabledCommands`, and `configKey`.
- Implement dataset-bound mode first; add a switchable headless mode that reads data via Web API.
- Add accessibility guarantees (labels for icon buttons, keyboard focus order, role/status for async updates) and performance targets (<500ms first render, 10k+ rows with virtualization).
Deliverables: a short README that restates goals, a minimal PCF scaffold (class + React mount), and TODOs aligned to these goals.
