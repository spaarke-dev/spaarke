# AI Playbook Builder - End-to-End Test Plan

> **Project**: ai-playbook-node-builder-r3
> **Created**: 2026-01-19
> **Status**: Test Plan Document (CI Environment - Manual Test Procedures)
> **Purpose**: Comprehensive E2E testing of all 8 success criteria from spec.md

---

## Table of Contents

1. [Overview](#overview)
2. [Prerequisites](#prerequisites)
3. [Test Environment Setup](#test-environment-setup)
4. [Success Criteria Test Cases](#success-criteria-test-cases)
   - [SC-1: Complex Playbook Building via AI](#sc-1-complex-playbook-building-via-ai)
   - [SC-2: AI Creates Scopes in Dataverse](#sc-2-ai-creates-scopes-in-dataverse)
   - [SC-3: AI Suggests Existing Scopes](#sc-3-ai-suggests-existing-scopes)
   - [SC-4: Test Modes (Mock, Quick, Production)](#sc-4-test-modes-mock-quick-production)
   - [SC-5: Rule-Based Workflow Support](#sc-5-rule-based-workflow-support)
   - [SC-6: Hybrid Workflow Support](#sc-6-hybrid-workflow-support)
   - [SC-7: SYS- Scope Immutability](#sc-7-sys--scope-immutability)
   - [SC-8: Save As with Lineage](#sc-8-save-as-with-lineage)
5. [Test Results Summary Template](#test-results-summary-template)
6. [Known Issues and Limitations](#known-issues-and-limitations)

---

## Overview

This document provides comprehensive end-to-end test procedures for the AI Playbook Builder Assistant feature. Since this is a CI environment without live Dataverse connectivity, this document serves as a **test plan** that can be executed manually in a connected environment.

### Test Scope

| Area | Description |
|------|-------------|
| **Backend Services** | `AiPlaybookBuilderService`, `ScopeResolverService` |
| **Frontend PCF** | `PlaybookBuilderHost` with AI Assistant panel |
| **Dataverse** | Scope entities (Actions, Skills, Knowledge, Tools) |
| **Azure** | OpenAI for intent classification, Blob storage for test documents |

### Success Criteria Reference (from spec.md)

| # | Criterion | Verification Method |
|---|-----------|---------------------|
| 1 | User can describe complex playbook and AI builds it | End-to-end test: describe lease analysis playbook, verify canvas populated |
| 2 | AI creates required scopes in Dataverse | Query Dataverse after build, verify records with correct ownership |
| 3 | AI suggests existing scopes when appropriate | Test reuse rate > 50% across 10 sample requests |
| 4 | Test modes execute correctly | Run mock, quick, production modes; verify appropriate behavior |
| 5 | Rule-based workflows supported | Build approval routing playbook using condition nodes only |
| 6 | Hybrid workflows supported | Build AI classification -> conditional routing -> task creation playbook |
| 7 | SYS- scopes are immutable | Attempt edit of SYS- scope, verify rejection with appropriate error |
| 8 | Save As creates copy with lineage | Save As on scope, verify new record with `basedon` reference |

---

## Prerequisites

### Environment Requirements

| Requirement | Details |
|-------------|---------|
| **Dataverse Environment** | Dev environment with `Spaarke` solution imported |
| **Azure OpenAI** | Deployed GPT-4o and GPT-4o-mini models |
| **Azure Blob Storage** | `playbook-test-documents` container created |
| **BFF API** | Running with valid Azure AD authentication |
| **Browser** | Chrome/Edge with developer tools |

### Dataverse Prerequisites

```
1. Builder scopes deployed (21 solution import):
   - 5 ACT-BUILDER-* records
   - 5 SKL-BUILDER-* records
   - 9 TL-BUILDER-* records
   - 4 KNW-BUILDER-* records

2. Ownership fields added to scope entities:
   - sprk_ownertype (OptionSet: System=1, Customer=2)
   - sprk_isimmutable (Boolean)
   - sprk_parentscope (Lookup)
   - sprk_basedon (Lookup)

3. At least one SYS- prefixed scope for immutability testing
```

### Test Data Requirements

| Data Type | Description |
|-----------|-------------|
| **Sample Lease Document** | PDF or DOCX with standard lease terms for AI analysis |
| **Existing Scopes** | At least 5 pre-existing scopes for suggestion testing |
| **Test User Account** | User with Create/Read/Write permissions on scope entities |

---

## Test Environment Setup

### Step 1: Verify Dataverse Connection

```powershell
# Authenticate to Dataverse
pac auth create --url https://spaarkedev1.crm.dynamics.com

# Verify connection
pac org who
```

### Step 2: Verify BFF API Health

```bash
# Health check
curl https://spe-api-dev-67e2xz.azurewebsites.net/healthz

# Expected: HTTP 200 OK
```

### Step 3: Verify Azure OpenAI

```bash
# Check OpenAI endpoint (requires auth token)
curl -X POST https://spaarke-openai-dev.openai.azure.com/openai/deployments/gpt-4o/chat/completions?api-version=2024-02-15-preview \
  -H "api-key: <key>" \
  -H "Content-Type: application/json" \
  -d '{"messages":[{"role":"user","content":"test"}],"max_tokens":5}'
```

### Step 4: Open PlaybookBuilder

1. Navigate to Dataverse model-driven app
2. Open a record with PlaybookBuilder control
3. Verify AI Assistant panel is visible on right side
4. Verify model selector shows GPT-4o / GPT-4o-mini options

---

## Success Criteria Test Cases

---

### SC-1: Complex Playbook Building via AI

**Criterion**: User can describe complex playbook and AI builds it

**Goal**: Verify the AI can understand a natural language description and construct a complete playbook on the canvas.

#### Test Procedure

**Step 1: Open Fresh Canvas**
1. Navigate to PlaybookBuilder in Dataverse
2. Ensure canvas is empty (or create new playbook)
3. Open AI Assistant panel

**Step 2: Describe Lease Analysis Playbook**
1. Enter the following prompt in AI chat:
   ```
   Build me a playbook that analyzes real estate lease agreements. It should:
   - Extract the document text
   - Generate a TL;DR summary
   - Analyze compliance against our company standards
   - Extract all parties involved
   - Identify financial terms
   - Check jurisdiction-specific requirements
   - Flag liability and indemnification risks
   - Find similar documents in our repository
   - Compile everything into a structured report
   - Deliver as Word document and email
   ```

2. Observe AI response and intent classification

**Step 3: Verify Canvas Population**
1. Check canvas for nodes created:

| Expected Node | Type | Purpose |
|---------------|------|---------|
| Document Text Extraction | start | Extract text from source document |
| TL;DR Summary | ai_analysis | Generate executive summary |
| Compliance Analysis | ai_analysis | Compare vs. standards |
| Party Extraction | ai_analysis | Identify parties |
| Financial Terms | ai_analysis | Extract financial data |
| Jurisdiction Analysis | ai_analysis | State-specific terms |
| Liability Analysis | ai_analysis | Risk assessment |
| Similar Documents | rag_search | Find related docs |
| Assemble Output | output_assembly | Compile report |
| Deliver Output | delivery | Word + email |

2. Verify edges connect nodes in logical flow
3. Verify parallel execution configured for independent analysis nodes

**Step 4: Verify Scope Assignments**
1. Click on each AI analysis node
2. Verify scope is assigned (Action + Skill + optionally Knowledge)
3. Check scope IDs are valid Dataverse references

#### Expected Results

| Checkpoint | Expected | Pass/Fail |
|------------|----------|-----------|
| AI understands request | Intent classified as "build_playbook" | |
| Nodes created | 10+ nodes on canvas | |
| Node types correct | Mix of start, ai_analysis, rag_search, output_assembly, delivery | |
| Edges created | All nodes connected with valid flow | |
| Scopes assigned | Each AI node has valid scope reference | |
| Canvas validates | No validation errors | |

#### Test Result

```
Date: _______________
Tester: _______________
Environment: _______________

Result: [ ] PASS  [ ] FAIL  [ ] BLOCKED

Notes:
_______________________________________________________________________
_______________________________________________________________________
```

---

### SC-2: AI Creates Scopes in Dataverse

**Criterion**: AI creates required scopes in Dataverse with correct ownership

**Goal**: Verify that when the AI creates new scopes (not reusing existing), they are properly persisted to Dataverse with correct ownership fields.

#### Test Procedure

**Step 1: Request Novel Scope**
1. Enter a prompt requesting a unique/unusual playbook:
   ```
   Build a playbook for analyzing cryptocurrency tax reports.
   Create a new action scope called "Crypto Tax Analysis" that extracts
   transaction data, calculates gains/losses, and flags wash sales.
   ```

**Step 2: Confirm Scope Creation**
1. If AI asks for clarification, provide additional details
2. Observe AI creating the new scope
3. Note the scope ID returned

**Step 3: Query Dataverse**
1. Open Dataverse Advanced Find or use Web API:
   ```
   GET /api/data/v9.2/sprk_actions?$filter=sprk_name eq 'Crypto Tax Analysis'
   ```

2. Verify record exists with fields:

| Field | Expected Value |
|-------|----------------|
| `sprk_name` | "Crypto Tax Analysis" |
| `sprk_ownertype` | 2 (Customer) |
| `sprk_isimmutable` | false |
| `sprk_scopeid` | CUST- prefix |

**Step 4: Verify No Duplicate Creation**
1. Request the same scope again:
   ```
   I need a scope for crypto tax analysis
   ```

2. Verify AI suggests the existing scope rather than creating duplicate

#### Expected Results

| Checkpoint | Expected | Pass/Fail |
|------------|----------|-----------|
| AI creates scope | New Dataverse record created | |
| Ownership correct | ownertype=Customer, isimmutable=false | |
| Prefix correct | scopeid starts with CUST- | |
| No duplicates | Second request reuses existing | |

#### Dataverse Verification Query

```sql
-- Run in XrmToolBox SQL Query
SELECT sprk_scopeid, sprk_name, sprk_ownertype, sprk_isimmutable, createdon
FROM sprk_action
WHERE sprk_name LIKE '%Crypto Tax%'
ORDER BY createdon DESC
```

#### Test Result

```
Date: _______________
Tester: _______________
Environment: _______________
Scope ID Created: _______________

Result: [ ] PASS  [ ] FAIL  [ ] BLOCKED

Notes:
_______________________________________________________________________
_______________________________________________________________________
```

---

### SC-3: AI Suggests Existing Scopes

**Criterion**: AI suggests existing scopes when appropriate (reuse rate > 50%)

**Goal**: Verify the AI prefers to suggest existing scopes rather than always creating new ones.

#### Test Procedure

**Step 1: Ensure Existing Scopes**
Verify these scopes exist in Dataverse:
- ACT-SYS-001: Document Analysis
- ACT-SYS-002: Text Summarization
- SKL-SYS-001: Legal Document Patterns
- SKL-SYS-002: Contract Review Patterns

**Step 2: Run 10 Sample Requests**
Execute these prompts and record whether AI suggests existing or creates new:

| # | Request | Expected Scope | Reused? |
|---|---------|----------------|---------|
| 1 | "Analyze this document for key terms" | ACT-SYS-001 | |
| 2 | "Summarize the uploaded PDF" | ACT-SYS-002 | |
| 3 | "Extract parties from legal contract" | SKL-SYS-001 | |
| 4 | "Review this vendor agreement" | SKL-SYS-002 | |
| 5 | "Find similar leases" | Existing RAG scope | |
| 6 | "Analyze compliance risks" | Existing risk scope | |
| 7 | "Generate executive summary" | ACT-SYS-002 | |
| 8 | "Check jurisdiction requirements" | Existing jurisdiction scope | |
| 9 | "Extract financial terms" | Existing finance scope | |
| 10 | "Flag non-standard clauses" | Existing clause scope | |

**Step 3: Calculate Reuse Rate**
```
Reuse Rate = (Scopes Reused / Total Requests) x 100

Target: > 50%
```

#### Expected Results

| Checkpoint | Expected | Pass/Fail |
|------------|----------|-----------|
| Search invoked | AI calls searchScopes before createScope | |
| Suggestions shown | User sees list of matching existing scopes | |
| Reuse rate | > 50% of requests use existing scopes | |
| Match quality | Suggested scopes are semantically relevant | |

#### Test Result

```
Date: _______________
Tester: _______________
Environment: _______________

Reuse Count: _____ / 10
Reuse Rate: _____%

Result: [ ] PASS (>50%)  [ ] FAIL (<50%)  [ ] BLOCKED

Notes:
_______________________________________________________________________
_______________________________________________________________________
```

---

### SC-4: Test Modes (Mock, Quick, Production)

**Criterion**: Test modes execute correctly with appropriate behavior

**Goal**: Verify all three test execution modes work as specified.

#### Test Procedure - Mock Mode

**Step 1: Configure Mock Test**
1. Build a simple 3-node playbook:
   - Start -> AI Analysis -> End
2. Click "Test" button
3. Select "Mock" mode

**Step 2: Execute Mock Test**
1. Click "Run Test"
2. Observe execution without external calls

**Step 3: Verify Mock Behavior**

| Checkpoint | Expected |
|------------|----------|
| No Azure OpenAI calls | API logs show no outbound requests |
| Simulated responses | Nodes return mock data |
| Fast execution | < 2 seconds total |
| No document storage | No blob created |

#### Test Procedure - Quick Mode

**Step 1: Configure Quick Test**
1. Use same playbook
2. Upload a test document
3. Select "Quick" mode

**Step 2: Execute Quick Test**
1. Click "Run Test"
2. Observe execution with temp storage

**Step 3: Verify Quick Behavior**

| Checkpoint | Expected |
|------------|----------|
| Azure OpenAI calls | Real AI processing occurs |
| Temp blob storage | Document stored in `playbook-test-documents` container |
| Output generated | Real analysis results returned |
| Cleanup scheduled | Blob marked for 24-hour expiration |

**Blob Verification**:
```powershell
# Check blob container for test document
az storage blob list --container-name playbook-test-documents --account-name spaarkedev --output table
```

#### Test Procedure - Production Mode

**Step 1: Configure Production Test**
1. Use same playbook
2. Select existing production document
3. Select "Production" mode
4. Note the warning message displayed

**Step 2: Execute Production Test**
1. Acknowledge warning
2. Click "Run Test"
3. Observe full execution

**Step 3: Verify Production Behavior**

| Checkpoint | Expected |
|------------|----------|
| Real document used | Production SPE document accessed |
| Full AI processing | Complete analysis with all scopes |
| Output persisted | Results saved to production storage |
| Audit logged | Operation logged with user context |

#### Expected Results

| Mode | External Calls | Storage | Cleanup | Pass/Fail |
|------|----------------|---------|---------|-----------|
| Mock | None | None | N/A | |
| Quick | Yes | Temp blob | 24 hours | |
| Production | Yes | Production | No | |

#### Test Result

```
Date: _______________
Tester: _______________
Environment: _______________

Mock Mode: [ ] PASS  [ ] FAIL
Quick Mode: [ ] PASS  [ ] FAIL
Production Mode: [ ] PASS  [ ] FAIL

Overall Result: [ ] PASS  [ ] FAIL  [ ] BLOCKED

Notes:
_______________________________________________________________________
_______________________________________________________________________
```

---

### SC-5: Rule-Based Workflow Support

**Criterion**: Build approval routing playbook using condition nodes only

**Goal**: Verify the AI can build playbooks that use only rule-based logic (no AI nodes).

#### Test Procedure

**Step 1: Request Rule-Based Workflow**
1. Enter prompt:
   ```
   Build an approval routing workflow.
   If the document value is over $10,000, route to VP approval.
   If between $5,000 and $10,000, route to Manager approval.
   Under $5,000, auto-approve.
   No AI analysis needed - just routing rules.
   ```

**Step 2: Verify Canvas Structure**
1. Check nodes created:

| Expected Node | Type | Purpose |
|---------------|------|---------|
| Start | start | Input document value |
| Check > $10K | condition | Branch for VP |
| Check > $5K | condition | Branch for Manager |
| VP Approval Task | task | Create approval task |
| Manager Approval Task | task | Create approval task |
| Auto-Approve | task | Mark approved |
| End | end | Complete workflow |

2. Verify NO ai_analysis nodes present
3. Verify condition nodes have proper expressions

**Step 3: Verify Condition Configuration**
1. Click first condition node
2. Verify expression: `input.documentValue > 10000`
3. Click second condition node
4. Verify expression: `input.documentValue > 5000`

**Step 4: Validate Flow**
1. Click "Validate" on canvas
2. Verify no errors
3. Verify all paths lead to valid endpoints

#### Expected Results

| Checkpoint | Expected | Pass/Fail |
|------------|----------|-----------|
| AI understands rule-only request | No AI nodes suggested | |
| Condition nodes created | 2 condition nodes for routing | |
| Task nodes created | 3 task nodes for outcomes | |
| Expressions correct | Proper comparison logic | |
| Flow valid | All paths complete | |

#### Test Result

```
Date: _______________
Tester: _______________
Environment: _______________

Result: [ ] PASS  [ ] FAIL  [ ] BLOCKED

Nodes Created:
- Condition nodes: _____
- Task nodes: _____
- AI nodes: _____ (should be 0)

Notes:
_______________________________________________________________________
_______________________________________________________________________
```

---

### SC-6: Hybrid Workflow Support

**Criterion**: Build playbook mixing AI nodes and rule-based nodes

**Goal**: Verify the AI can construct workflows that combine AI classification with conditional routing and task creation.

#### Test Procedure

**Step 1: Request Hybrid Workflow**
1. Enter prompt:
   ```
   Build a document processing workflow that:
   1. Uses AI to classify incoming documents (contract, invoice, correspondence)
   2. Based on classification:
      - Contracts go to legal review queue
      - Invoices over $1000 go to AP manager
      - Invoices under $1000 auto-process
      - Correspondence goes to general inbox
   3. Create appropriate tasks in each queue
   ```

**Step 2: Verify Hybrid Structure**
1. Check nodes created:

| Expected Node | Type | Purpose |
|---------------|------|---------|
| Start | start | Input document |
| AI Classification | ai_analysis | Classify document type |
| Document Type Check | condition | Branch by classification |
| Invoice Value Check | condition | Branch by amount |
| Legal Queue Task | task | Route to legal |
| AP Manager Task | task | Route to AP manager |
| Auto-Process Task | task | Auto-process invoice |
| General Inbox Task | task | Route to inbox |
| End | end | Complete |

2. Verify MIX of ai_analysis AND condition nodes
3. Verify proper edge routing

**Step 3: Verify AI Node Configuration**
1. Click AI Classification node
2. Verify:
   - Action scope assigned (classification action)
   - Output variable defined for classification result
   - Model selection appropriate (GPT-4o-mini for classification)

**Step 4: Verify Condition Node Configuration**
1. Click Document Type Check condition
2. Verify expression references AI output: `aiClassification.documentType`
3. Click Invoice Value Check condition
4. Verify expression: `aiClassification.documentType == 'invoice' && input.amount > 1000`

**Step 5: Validate Complete Flow**
1. Click "Validate" on canvas
2. Verify all branches have valid endpoints
3. Verify no orphan nodes

#### Expected Results

| Checkpoint | Expected | Pass/Fail |
|------------|----------|-----------|
| AI node present | 1 ai_analysis node for classification | |
| Condition nodes present | 2 condition nodes for routing | |
| Task nodes present | 4 task nodes for outcomes | |
| Data flow correct | AI output feeds condition expressions | |
| All paths valid | Every branch reaches end node | |

#### Test Result

```
Date: _______________
Tester: _______________
Environment: _______________

Result: [ ] PASS  [ ] FAIL  [ ] BLOCKED

Node Inventory:
- AI Analysis nodes: _____
- Condition nodes: _____
- Task nodes: _____

Data Flow Verified: [ ] Yes  [ ] No

Notes:
_______________________________________________________________________
_______________________________________________________________________
```

---

### SC-7: SYS- Scope Immutability

**Criterion**: Attempt edit of SYS- scope, verify rejection with appropriate error

**Goal**: Verify that system scopes (SYS- prefix) cannot be modified or deleted.

#### Test Procedure

**Step 1: Identify System Scope**
1. Query Dataverse for SYS- prefixed scope:
   ```
   GET /api/data/v9.2/sprk_actions?$filter=startswith(sprk_scopeid,'SYS-')&$top=1
   ```
2. Note the scope ID (e.g., `SYS-ACT-001`)

**Step 2: Attempt Edit via AI Assistant**
1. Enter prompt:
   ```
   Edit the system scope SYS-ACT-001 and change its name to "Modified System Scope"
   ```

2. Observe AI response

**Step 3: Verify Rejection**
1. AI should return error message indicating immutability
2. Expected message pattern: "Cannot modify system scope. SYS- prefixed scopes are immutable."

**Step 4: Attempt Edit via Direct API**
1. Try PATCH request:
   ```
   PATCH /api/data/v9.2/sprk_actions(<scope-guid>)
   {
     "sprk_name": "Modified System Scope"
   }
   ```

2. Verify response is 400/403 with immutability error

**Step 5: Attempt Delete**
1. Enter prompt:
   ```
   Delete the system scope SYS-ACT-001
   ```

2. Verify rejection message

**Step 6: Verify via Direct API**
1. Try DELETE request:
   ```
   DELETE /api/data/v9.2/sprk_actions(<scope-guid>)
   ```

2. Verify rejection

**Step 7: Verify Customer Scope IS Editable**
1. Find a CUST- prefixed scope
2. Edit it successfully to confirm the immutability is only for SYS-

#### Expected Results

| Checkpoint | Expected | Pass/Fail |
|------------|----------|-----------|
| AI edit rejected | Error message returned | |
| API edit rejected | 400/403 response | |
| AI delete rejected | Error message returned | |
| API delete rejected | 400/403 response | |
| CUST- scope editable | Edit succeeds | |
| Error message clear | Mentions "immutable" or "system scope" | |

#### Test Result

```
Date: _______________
Tester: _______________
Environment: _______________
System Scope Tested: _______________

AI Edit Attempt: [ ] Rejected  [ ] Allowed (FAIL)
API Edit Attempt: [ ] Rejected  [ ] Allowed (FAIL)
AI Delete Attempt: [ ] Rejected  [ ] Allowed (FAIL)
API Delete Attempt: [ ] Rejected  [ ] Allowed (FAIL)

Result: [ ] PASS  [ ] FAIL  [ ] BLOCKED

Error Message Received:
_______________________________________________________________________
```

---

### SC-8: Save As with Lineage

**Criterion**: Save As on scope, verify new record with `basedon` reference

**Goal**: Verify the Save As functionality creates a new scope with proper lineage tracking.

#### Test Procedure

**Step 1: Select Existing Scope**
1. Open Scope Browser in PlaybookBuilder
2. Select an existing scope (e.g., `ACT-SYS-001: Document Analysis`)
3. Note original scope GUID

**Step 2: Invoke Save As**
1. Click "Save As" button (or via AI: "Save a copy of ACT-SYS-001 as Custom Document Analysis")
2. Enter new name: "Custom Document Analysis"
3. Optionally modify configuration
4. Click Save

**Step 3: Verify New Record Created**
1. Query Dataverse for new scope:
   ```
   GET /api/data/v9.2/sprk_actions?$filter=sprk_name eq 'Custom Document Analysis'
   ```

2. Verify fields:

| Field | Expected Value |
|-------|----------------|
| `sprk_name` | "Custom Document Analysis" |
| `sprk_scopeid` | CUST-* prefix (new ID) |
| `sprk_ownertype` | 2 (Customer) |
| `sprk_isimmutable` | false |
| `sprk_basedon` | GUID of original scope |

**Step 4: Verify basedon Lookup**
1. Expand the basedon lookup:
   ```
   GET /api/data/v9.2/sprk_actions(<new-guid>)?$expand=sprk_basedon
   ```

2. Verify basedon references the original scope

**Step 5: Verify Original Unchanged**
1. Query original scope
2. Verify no modifications to original record
3. Verify original remains SYS- if it was system scope

**Step 6: Test Extend (Parent Scope)**
1. Select a scope
2. Use "Extend" instead of "Save As":
   ```
   Extend ACT-SYS-002 with additional instructions for handling encrypted documents
   ```
3. Verify `sprk_parentscope` field is set (not `sprk_basedon`)

#### Expected Results

| Checkpoint | Expected | Pass/Fail |
|------------|----------|-----------|
| New scope created | New Dataverse record | |
| New ID generated | CUST-* prefix | |
| basedon populated | References original GUID | |
| Ownership correct | ownertype=Customer | |
| Original unchanged | No modifications | |
| Extend uses parentscope | Different field than Save As | |

#### Dataverse Verification Query

```sql
-- Verify lineage
SELECT
  child.sprk_scopeid AS ChildScope,
  child.sprk_name AS ChildName,
  parent.sprk_scopeid AS ParentScope,
  parent.sprk_name AS ParentName
FROM sprk_action child
LEFT JOIN sprk_action parent ON child.sprk_basedon = parent.sprk_actionid
WHERE child.sprk_name = 'Custom Document Analysis'
```

#### Test Result

```
Date: _______________
Tester: _______________
Environment: _______________

Original Scope: _______________
New Scope ID: _______________
basedon GUID: _______________

Record Created: [ ] Yes  [ ] No
basedon Populated: [ ] Yes  [ ] No
Original Unchanged: [ ] Yes  [ ] No

Result: [ ] PASS  [ ] FAIL  [ ] BLOCKED

Notes:
_______________________________________________________________________
_______________________________________________________________________
```

---

## Test Results Summary Template

Execute all tests and record results here:

| SC # | Success Criterion | Result | Date | Tester |
|------|-------------------|--------|------|--------|
| SC-1 | Complex playbook building via AI | | | |
| SC-2 | AI creates scopes in Dataverse | | | |
| SC-3 | AI suggests existing scopes (>50% reuse) | | | |
| SC-4 | Test modes (Mock/Quick/Production) | | | |
| SC-5 | Rule-based workflow support | | | |
| SC-6 | Hybrid workflow support | | | |
| SC-7 | SYS- scope immutability | | | |
| SC-8 | Save As with lineage | | | |

### Overall Status

```
Tests Passed: _____ / 8
Tests Failed: _____
Tests Blocked: _____

Overall Result: [ ] PASS  [ ] FAIL

Sign-off: ___________________________
Date: _______________
```

---

## Known Issues and Limitations

### CI Environment Limitations

| Limitation | Impact | Workaround |
|------------|--------|------------|
| No live Dataverse | Cannot verify actual record creation | Use manual testing with connected environment |
| No Azure OpenAI in CI | Cannot test real AI responses | Use mock responses for unit tests |
| No blob storage access | Cannot verify test document storage | Manual verification in Azure Portal |

### Implementation Notes

| Task | Status | Notes |
|------|--------|-------|
| Task 031 (Test Modes) | Pending | Test mode implementation not yet complete |
| Mock mode | Code complete | Ready for testing |
| Quick mode | Code complete | Requires blob container verification |
| Production mode | Code complete | Requires production data access |

### Issues to Monitor

1. **Intent Classification Accuracy**: Monitor confidence thresholds during testing. If clarification is triggered too often, adjust threshold from 0.8 to 0.7.

2. **Scope Search Performance**: If search latency > 1 second, consider adding caching for scope catalog.

3. **Parallel Node Execution**: Verify Nodes 3-9 in lease analysis execute in parallel, not sequentially.

---

## References

| Document | Purpose |
|----------|---------|
| [spec.md](../spec.md) | Success criteria definitions |
| [PLAYBOOK-REAL-ESTATE-LEASE-ANALYSIS.md](../../../docs/guides/PLAYBOOK-REAL-ESTATE-LEASE-ANALYSIS.md) | Reference playbook for SC-1 testing |
| [SPAARKE-AI-ARCHITECTURE.md](../../../docs/guides/SPAARKE-AI-ARCHITECTURE.md) | AI architecture patterns |
| [PLAYBOOK-BUILDER-FULLSCREEN-SETUP.md](../../../docs/guides/PLAYBOOK-BUILDER-FULLSCREEN-SETUP.md) | PlaybookBuilder setup and usage |

---

*Test Plan Created: 2026-01-19*
*Last Updated: 2026-01-19*
