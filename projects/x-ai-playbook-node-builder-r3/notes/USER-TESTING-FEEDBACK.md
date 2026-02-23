# AI Playbook Builder - User Testing Feedback

> **Project**: ai-playbook-node-builder-r3
> **PR**: #143 (Draft)
> **Testing Date**: January 20, 2026
> **Status**: Collecting Feedback

---

## Overview

This document collects user testing feedback for the AI Playbook Builder feature. Each feedback item includes the observed behavior, expected behavior, and recommended solution approach.

---

## Feedback Items

### FB-001: AI Should Guide User Through Scope Selection After Node Creation

**Priority**: High
**Component**: `AiPlaybookBuilderService` / System Prompt

#### Observed Behavior
After creating nodes, the AI responds with a terse completion message:
> "Done! Created 4 nodes connected in sequence. Processing complete"

The conversation then ends without further guidance.

#### Expected Behavior
After creating the basic node structure, the AI should:
1. Acknowledge the nodes were created
2. Transition to scope selection phase
3. Suggest recommended scopes based on the document type
4. Ask user for confirmation or modifications

**Example response the user expects:**
> "I've created the basic node structure. The next step is to add scopes. Let's start with 'Document Analysis'. For a U.S. Patent Office Action Review I would recommend the following scopes:
> - **Actions**: Entity Extraction, Document Summary, Clause Analysis
> - **Skills**: Contract Law Basics (for patent claims analysis)
> - **Knowledge**: Standard Contract Terms (patent terminology reference)
>
> Do these look good or would you like to modify them?"

#### Context
- Test case: "let's create a new playbook for reviewing an U.S. patent office action"
- AI correctly identified 4 nodes: Document Intake, Document Analysis, Review & Approval, Final Output
- AI created all nodes with proper connections
- Issue is in post-creation workflow guidance

#### Recommended Solution
1. Update system prompt in `AiPlaybookBuilderService.GetBuilderScopePromptAsync()` to include post-creation guidance instructions
2. Modify the `create_node` tool response handling to trigger scope suggestion workflow
3. Add scope recommendation logic based on document type from user request
4. Consider adding a `suggest_scopes` tool that the AI can call after node creation

#### Files to Modify
- `src/server/api/Sprk.Bff.Api/Services/Ai/Builder/AiPlaybookBuilderService.cs` - System prompt
- Possibly add new tool for scope suggestions

---

### FB-002: AI Hangs When Asked About Available Scopes

**Priority**: High
**Component**: `AiPlaybookBuilderService` / OpenAI Function Calling / Timeout Handling

#### Observed Behavior
User asked: "for the document analysis, what scopes can we add"

The AI kept "thinking" for over 1 minute without any response. No timeout, no clarification request, just indefinite waiting.

#### Expected Behavior
The AI should respond within 5-10 seconds with ONE of:
1. **List available scopes** organized by type:
   > "For Document Analysis, you can add scopes from these categories:
   > - **Actions**: Entity Extraction, Document Summary, Clause Analysis, Risk Detection
   > - **Skills**: Real Estate Domain, Contract Law Basics, Financial Analysis
   > - **Knowledge**: Standard Contract Terms, Company Policies
   > - **Tools**: Entity Extractor Handler, Clause Analyzer Handler
   >
   > Which type would you like to explore first?"

2. **Go through each scope type** one at a time:
   > "Let's add scopes to Document Analysis. First, let's look at Actions.
   > Available actions: Entity Extraction, Document Summary, Clause Analysis...
   > Which actions would you like to add?"

3. **Ask for clarification** (if the question is ambiguous):
   > "I can help you add scopes to the Document Analysis node. Would you like me to:
   > 1. Show all available scopes by category
   > 2. Recommend scopes based on your document type
   > 3. Walk through each scope type one by one"

#### Root Cause Analysis
Possible causes:
1. **Tool call loop** - AI may be repeatedly calling `search_scopes` without returning results
2. **Missing tool response handling** - Tool returns data but AI doesn't know how to present it
3. **No timeout configuration** - OpenAI streaming has no max response time
4. **Scope catalog too large** - AI trying to process all 23 scopes at once

#### Recommended Solution
1. **Add response timeout** - If no tokens streamed for 15 seconds, return a "still thinking" message or error
2. **Update system prompt** - Add explicit instruction for handling scope queries:
   ```
   When user asks about available scopes:
   1. Use search_scopes tool to retrieve relevant scopes
   2. Present results organized by scope type (Actions, Skills, Knowledge, Tools)
   3. If no results, explain and ask for clarification
   4. Never "think" for more than 10 seconds without responding
   ```
3. **Add streaming heartbeat** - Send periodic "..." or status during long operations
4. **Limit scope results** - Return top 5-10 most relevant scopes per type, not all

#### Files to Investigate
- `src/server/api/Sprk.Bff.Api/Services/Ai/Builder/AiPlaybookBuilderService.cs` - Check tool handling
- `src/server/api/Sprk.Bff.Api/Api/Ai/AiPlaybookBuilderEndpoints.cs` - Check streaming timeout
- System prompt in `GetBuilderScopePromptAsync()` - Add scope query instructions

---

### FB-003: "Suggestions?" Menu Action Does Nothing + Misleading "Process Complete"

**Priority**: High
**Component**: PCF Chat Menu / System Prompt / Tool Definitions

#### Observed Behavior
User clicked "Suggestions?" from the chat menu.

AI Response: "I understand you want to get help. Process complete."

**Two issues:**
1. Nothing actually happened - no suggestions were provided
2. "Process complete" is misleading when no action was taken

#### Expected Behavior
When user clicks "Suggestions?", the AI should:
1. Analyze the current canvas state (nodes, connections, scopes)
2. Provide contextual suggestions based on what's missing or could be improved

**Example expected response:**
> "Looking at your playbook, here are my suggestions:
>
> **Missing Scopes:**
> - 'Document Analysis' node has no scopes attached - consider adding Entity Extraction or Document Summary
> - 'Review & Approval' node could benefit from Risk Detection action
>
> **Optimization Ideas:**
> - Consider adding a 'Financial Analysis' skill if processing contracts with monetary terms
> - The 'Final Output' node could use a Summary tool for generating reports
>
> Would you like me to help with any of these?"

#### Root Cause Analysis
1. **"Suggestions?" may not be mapped to a tool** - The AI doesn't know what action to take
2. **System prompt lacks instructions** for handling menu commands like "Suggestions?"
3. **"Process complete" is a fallback** - AI says this when it doesn't know what else to do

#### Recommended Solution
1. **Map menu actions to explicit tools or instructions** in system prompt:
   ```
   When user selects "Suggestions?" from the menu:
   1. Use get_canvas_state tool to retrieve current playbook structure
   2. Analyze each node for missing scopes
   3. Compare against recommended scopes for the document type
   4. Provide actionable suggestions organized by node
   5. Ask which suggestion they'd like to implement
   ```

2. **Remove generic "Process complete" responses** - Only say "complete" when something was actually done

3. **Add validation** - If AI can't provide suggestions, explain why:
   > "I'd like to help with suggestions, but I need more context. Could you tell me:
   > - What type of documents will this playbook process?
   > - What's the primary goal (analysis, extraction, review)?"

#### Files to Modify
- `src/server/api/Sprk.Bff.Api/Services/Ai/Builder/AiPlaybookBuilderService.cs` - System prompt
- PCF control - Verify menu action sends correct message format
- Consider adding `analyze_playbook` or `get_suggestions` tool

---

### FB-004: Remove Generic "Process Complete" Responses

**Priority**: Medium
**Component**: System Prompt / Response Handling

#### Observed Behavior
AI frequently ends responses with "Process complete" or "Processing complete" even when:
- Nothing was actually done
- The user asked a question (not a command)
- An error occurred
- The AI doesn't understand the request

#### Expected Behavior
- **After successful action**: "Done! I've added the Entity Extraction action to Document Analysis."
- **After answering a question**: Just answer - no "complete" needed
- **When confused**: "I'm not sure what you'd like me to do. Could you clarify..."
- **When nothing to do**: "Your playbook looks good! Is there anything specific you'd like to change?"

#### Recommended Solution
Update system prompt with explicit guidance:
```
Response Guidelines:
- Only say "complete" or "done" after successfully executing a canvas operation
- For questions: Answer directly without status messages
- For unclear requests: Ask for clarification
- Never say "Process complete" if you didn't do anything
```

#### Files to Modify
- `src/server/api/Sprk.Bff.Api/Services/Ai/Builder/AiPlaybookBuilderService.cs` - System prompt

---

### FB-005: [Reserved for next feedback item]

**Priority**: TBD
**Component**: TBD

#### Observed Behavior
_To be filled during testing_

#### Expected Behavior
_To be filled during testing_

#### Recommended Solution
_To be filled during testing_

---

## Feedback Summary

| ID | Title | Priority | Component | Status |
|----|-------|----------|-----------|--------|
| FB-001 | Guide user through scope selection after node creation | High | System Prompt / Service | Open |
| FB-002 | AI hangs when asked about available scopes | High | Timeout / Tool Handling | Open |
| FB-003 | "Suggestions?" menu does nothing + misleading "Process complete" | High | Menu / System Prompt | Open |
| FB-004 | Remove generic "Process complete" responses | Medium | System Prompt | Open |
| FB-005 | _Reserved_ | - | - | - |

---

## Testing Checklist

- [x] Create new playbook (basic flow)
- [ ] Add scopes to nodes
- [ ] Modify existing nodes
- [ ] Delete nodes
- [ ] Connect/disconnect nodes
- [ ] Save playbook
- [ ] Load existing playbook
- [ ] Error handling (invalid requests)

---

## Next Steps

1. Complete user testing and add additional feedback items
2. Prioritize feedback items
3. Create implementation tasks for each item
4. Address feedback comprehensively in Phase 5 or new project phase

---

*Document created: January 20, 2026*
