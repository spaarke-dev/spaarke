# CLAUDE.md — events-record-direct-embed

## Project Context

This project adds **embedded mode** to the existing `EventsPage.html` web resource so it can serve as a context-aware Events tab inside any Dataverse entity form (Matter, Project, Invoice, etc.).

## Key Decisions

- **Not a fork**: The existing EventsPage gains an embedded mode — one codebase, three modes (system/dialog/embedded)
- **Entity-agnostic**: Adding support for a new entity requires only form tab configuration + optional views
- **View discovery by naming convention**: `{EntityPrefix}-{ViewName}` pattern, queried from `savedquery` at runtime
- **Side pane cleanup via reusable hook**: `useSidePaneLifecycle` handles tab-switch detection for any web resource
- **`sprk_regardingrecordid`**: Polymorphic lookup field already exists on `sprk_event` — filters to any parent entity

## Existing Code to Reuse

| Code | Location | How It's Reused |
|------|----------|-----------------|
| `parseDrillThroughParams()` | `EventsPage/src/App.tsx:113-156` | Extended with `entityName`, `recordId` params |
| `ContextFilter` interface | `GridSection.tsx:113-118` | Passed from App.tsx with parent record ID |
| FetchXML context injection | `GridSection.tsx:790-801` | No changes — already injects `<condition>` |
| OData context filter | `GridSection.tsx:840-843` | No changes — already appends filter |
| `IEventViewConfig` interface | `eventConfig.ts:137-141` | Discovery returns same shape |
| `EVENT_VIEWS` static config | `eventConfig.ts:147-165` | Used as fallback when no entity views found |
| Navigation cleanup | `App.tsx:1239-1307` | Extracted into `useSidePaneLifecycle` hook |
| BroadcastChannel | `broadcastChannel.ts` | Unchanged — same-origin messaging works |
| Session persistence | `sessionPersistence.ts` | Unchanged — survives tab switches |

## ADRs to Follow

- **ADR-021**: Fluent UI v9 exclusively, semantic tokens, dark mode support
- **ADR-022**: React 16 APIs only in PCF (but EventsPage is a web resource — React 18 OK)
- **ADR-006**: No new legacy webresources (we're enhancing an existing React web resource)

## Testing Checklist

- [ ] System Events page — no regression
- [ ] Dialog drill-through — no regression
- [ ] Embedded in Matter — grid filters, views discovered, +New pre-fills
- [ ] Tab switch cleanup — panes close when leaving Events tab
- [ ] Calendar side pane — additive filtering in embedded mode
- [ ] Event detail side pane — edit/save in embedded mode
- [ ] Dark mode — renders correctly in embedded context
- [ ] No entity views — falls back to system views
