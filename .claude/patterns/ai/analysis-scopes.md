# Analysis Scopes Pattern

## When
Use when building prompts for AI analysis — combining an action (required), skills (optional behavioral instructions), and knowledge (optional grounding context).

## Read These Files
1. `src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisContextBuilder.cs` — prompt assembly: action → skills → knowledge → document text
2. `src/server/api/Sprk.Bff.Api/Services/Ai/ScopeResolverService.cs` — loading actions, skills, and knowledge from Dataverse

## Constraints
- **ADR-013**: AI features extend BFF; scope models live in BFF, not a separate service

## Key Rules
- Action is required — provides the base system prompt defining analysis objective
- Skills (optional, multiple) append `PromptFragment` to system prompt, sorted by `Category`
- Knowledge (optional, multiple) of type `Inline` is prepended to user prompt before document text
- Knowledge types: `Inline` (text in prompt), `Document` (reference ID), `RagIndex` (search index)
- JSON structured output is enabled when the action requests it — check action config before assuming free-text
