# R2-053: Dynamic Command Integration Findings

## Summary

Wired the BFF command catalog (R2-019) to the frontend slash menu pipeline (R2-035/036/037) end-to-end.

## Issues Found and Fixed

### 1. BFF Response Shape Mismatch (Critical)

**Problem**: The BFF `GetCommandsAsync` endpoint returned `Results.Ok(commands)` where `commands` is a flat `IReadOnlyList<CommandEntry>`. The frontend `useDynamicSlashCommands` hook expected a structured `ICommandsResponse` with `{ systemCommands, dynamicCommands }` arrays.

**Fix**: Updated `GetCommandsAsync` to partition the flat catalog into `CommandsResponse { SystemCommands, DynamicCommands }`. Created new `CommandsResponse` and `CommandResponseItem` DTOs in `Models/Ai/Chat/CommandsResponse.cs`.

### 2. Source Discriminator Mismatch (Critical)

**Problem**: The internal `CommandEntry.Source` field was the playbook/scope GUID (or null for system commands). The frontend expected `source` to be a type discriminator (`"system" | "playbook" | "scope"`) matching the `SlashCommandSource` union type.

Additionally, scope commands had their `Category` set to a scope-qualified label (e.g., "Legal Research -- Search") rather than a simple "scope" string, which broke the frontend's category-based routing.

**Fix**: The new `CommandResponseItem` DTO carries:
- `Source`: type discriminator ("system", "playbook", "scope") derived from `CommandEntry.Category` via `DeriveSourceType()`
- `Category`: normalized to "system", "playbook", or "scope" for frontend grouping
- `SourceName`: human-readable origin label (scope-qualified label for scope commands, playbook name for playbook commands, null for system)

### 3. No Frontend Code Changes Required

The frontend hooks and components were already correctly implemented to handle the structured response shape. The `toSlashCommand` mapping in `useDynamicSlashCommands` correctly maps `item.source` as `SlashCommandSource` and `item.sourceName` for display.

## Verified Integration Flow

```
BFF DynamicCommandResolver.ResolveCommandsAsync()
  -> Returns flat IReadOnlyList<CommandEntry>
  -> GetCommandsAsync partitions into CommandsResponse { systemCommands, dynamicCommands }
  -> Each item projected to CommandResponseItem with source discriminator

Frontend useDynamicSlashCommands hook
  -> GET /api/ai/chat/sessions/{id}/commands
  -> Parses ICommandsResponse { systemCommands, dynamicCommands }
  -> Deduplicates against DEFAULT_SLASH_COMMANDS
  -> Partitions into scopeCommands / playbookCommands (FR-11: independent persistence)
  -> Caches in-memory (R2-038)

useSlashCommands hook
  -> Merges DEFAULT_SLASH_COMMANDS + dynamicCommands
  -> Filters by typed text after '/'
  -> Passes to SlashCommandMenu

SlashCommandMenu
  -> Groups by source field: system / playbook / scope
  -> Category headers with distinct icons and accent colors
  -> Keyboard navigation across category boundaries
```

## Constraint Verification

| Constraint | Status | Evidence |
|-----------|--------|----------|
| FR-17: Auto-generated from metadata | PASS | Playbook commands from Dataverse `sprk_analysisplaybook`, scope commands from `sprk_capabilities` option set |
| Different entity types show different commands | PASS | `DynamicCommandResolver` filters by `sprk_recordtype = entityType` |
| Scope commands independent of playbook | PASS | `useDynamicSlashCommands` stores scope commands separately; persist across playbook switches |
| System commands always present | PASS | `DEFAULT_SLASH_COMMANDS` constant in frontend + `SystemCommands` in BFF always included |
| No static command relationship tables | PASS | Playbook-to-command mapping is Dataverse query, not a static table |

## R2-053 Integration Pass (Task 053)

### 4. SlashCommandCategory Type Missing 'scope' (Minor)

**Problem**: `SlashCommandCategory` type in `slashCommandMenu.types.ts` was defined as `'system' | 'playbook'`, missing the `'scope'` value. While the `SlashCommandMenu` groups by `source` (which correctly includes `'scope'`), the `category` field on `SlashCommand` couldn't represent scope commands accurately.

**Fix**: Updated `SlashCommandCategory` to `'system' | 'playbook' | 'scope'`.

### 5. toSlashCommand Category Mapping Incorrect for Scope Commands (Minor)

**Problem**: In `useDynamicSlashCommands.ts`, the `toSlashCommand` function mapped any non-"system" category to `'playbook'`, including scope commands that the BFF sends with `category: "scope"`. This meant scope commands had `category: 'playbook'` on the frontend even though `source: 'scope'` was correct.

**Fix**: Updated the category mapping to preserve the BFF's `"scope"` category: `item.category === 'system' ? 'system' : item.category === 'scope' ? 'scope' : 'playbook'`.

### 6. IAnalysisChatContextResponse Missing Fields (Non-breaking)

**Problem**: The frontend `IAnalysisChatContextResponse` interface in `useChatContextMapping.ts` did not include `commands`, `searchGuidance`, or `scopeMetadata` fields, even though the BFF `AnalysisChatContextResponse` record includes them. The extra fields were silently ignored during JSON deserialization.

**Fix**: Added `commands?: ICommandEntry[]`, `searchGuidance?: string`, and `scopeMetadata?: IAnalysisScopeMetadata` to the interface with corresponding new type definitions (`ICommandEntry`, `IAnalysisScopeMetadata`). These fields are optional since the dedicated `/commands` endpoint is the primary source for slash commands.

## Files Modified

### Initial integration (prior pass)
- `src/server/api/Sprk.Bff.Api/Api/Ai/ChatEndpoints.cs` -- Updated `GetCommandsAsync` to return structured `CommandsResponse`; added `DeriveSourceType` helper
- `src/server/api/Sprk.Bff.Api/Models/Ai/Chat/CommandsResponse.cs` -- New file: `CommandsResponse` and `CommandResponseItem` DTOs

### R2-053 integration pass
- `src/client/shared/Spaarke.UI.Components/src/components/SlashCommandMenu/slashCommandMenu.types.ts` -- Added `'scope'` to `SlashCommandCategory` type
- `src/client/shared/Spaarke.UI.Components/src/components/SprkChat/hooks/useDynamicSlashCommands.ts` -- Fixed `toSlashCommand` category mapping for scope commands
- `src/client/shared/Spaarke.UI.Components/src/components/SprkChat/hooks/useChatContextMapping.ts` -- Added `ICommandEntry`, `IAnalysisScopeMetadata` types; added `commands`, `searchGuidance`, `scopeMetadata` fields to `IAnalysisChatContextResponse`
