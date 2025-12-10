# Spaarke Software Development Procedures

> **Version**: 2.0  
> **Last Updated**: December 4, 2025  
> **Status**: Active  
> **Audience**: Product Managers, Software Engineers, AI Agents

---

## Table of Contents

1. [Introduction](#1-introduction)
2. [Role Definitions](#2-role-definitions)
3. [Development Lifecycle Overview](#3-development-lifecycle-overview)
4. [Stage 0: Discovery & Research](#4-stage-0-discovery--research)
5. [Stage 1: Feature Request](#5-stage-1-feature-request)
6. [Stage 2: Solution Assessment](#6-stage-2-solution-assessment)
7. [Stage 3: Design Specification](#7-stage-3-design-specification)
8. [Stage 4: Project Initialization](#8-stage-4-project-initialization)
9. [Stage 5: Task Decomposition](#9-stage-5-task-decomposition)
10. [Stage 6: Task Execution (AI-Directed)](#10-stage-6-task-execution-ai-directed)
11. [Stage 7: Testing & Validation](#11-stage-7-testing--validation)
12. [Stage 8: Documentation & Completion](#12-stage-8-documentation--completion)
13. [Supporting Infrastructure](#13-supporting-infrastructure)
- [Appendix A: POML Reference](#appendix-a-poml-reference)
- [Appendix B: Template Inventory](#appendix-b-template-inventory)
- [Appendix C: Context Engineering Quick Reference](#appendix-c-context-engineering-quick-reference)
- [Appendix D: Stage Checklists](#appendix-d-stage-checklists)
- [Appendix E: Quick Start - Design Spec to Completion](#appendix-e-quick-start---design-spec-to-completion)

---

## 1. Introduction

### 1.1 Purpose

This document defines Spaarke's end-to-end software development procedures, integrating **AI-directed development** with **human engineering oversight**. It establishes a disciplined workflow where AI accelerates coding while humans ensure architecture, quality, and compliance.

### 1.2 Audience

| Audience | How to Use This Document |
|----------|-------------------------|
| **Product Managers** | Understand the full lifecycle, know what artifacts to produce, identify decision gates requiring your input |
| **Software Engineers** | Follow procedures for each stage, understand AI collaboration patterns, know when human judgment is required |
| **AI Agents** | Reference for context, constraints, and execution protocols during task execution |

### 1.3 Guiding Principles

1. **Structured Autonomy** - AI operates within defined boundaries; humans set direction and validate outcomes
2. **Artifact-Driven Workflow** - Each stage produces specific, templated artifacts enabling handoffs
3. **Human-in-the-Loop by Design** - Explicit checkpoints where human review is required before proceeding
4. **Context Engineering** - Optimized information flow to AI within token constraints
5. **ADR Compliance** - Architecture Decision Records govern all technical decisions
6. **Repeatability** - Consistent processes enable quality at scale

### 1.4 Core Technology Stack

| Layer | Technology | Purpose |
|-------|------------|---------|
| **Discovery & Design** | | |
| | Figma | UI/UX design, prototypes, wireframes |
| | Miro / FigJam | Collaborative workshops, journey maps |
| | Notion / Confluence | Collaborative documentation, RFCs |
| **Development** | | |
| | VS Code | Primary development interface |
| | Claude Code (VS Code Extension) | AI coding agent - code generation, task execution |
| | GitHub Copilot Chat | AI documentation - design specs, planning |
| **Linting & Static Analysis** | | |
| | ESLint | TypeScript/JavaScript static analysis (src/client/pcf/) |
| | Roslyn Analyzers | C# static analysis, null checks, async patterns |
| | TreatWarningsAsErrors | Compiler warnings as build errors (Directory.Build.props) |
| **Quality & Testing** | | |
| | SpecFlow / Cucumber | BDD - executable specifications |
| | Storybook | Component documentation and testing |
| **Version Control & CI/CD** | | |
| | Git / GitHub | Repository management, CI/CD |
| **Context Management** | | |
| | CLAUDE.md files, Skills | Persistent AI memory and workflows |

---

## 2. Role Definitions

### 2.1 Product Manager (PM)

**Primary Responsibility**: Define *what* to build and *why*

| Activity | Deliverable | Stage |
|----------|-------------|-------|
| Lead discovery research | Research findings, validated assumptions | Stage 0 |
| Capture business requirements | Feature Request document | Stage 1 |
| Define user value and use cases | Feature Request document | Stage 1 |
| Participate in solution assessment | Solution Assessment document | Stage 2 |
| Review and approve design specifications | Approved Design Spec | Stage 3 |
| Accept feature completion | Sign-off | Stage 8 |

**Decision Authority**:
- Feature prioritization
- Scope decisions (in/out)
- User experience requirements
- Acceptance criteria approval
- Research direction and user validation

### 2.2 Software Engineer (Developer)

**Primary Responsibility**: Define *how* to build and ensure quality

| Activity | Deliverable | Stage |
|----------|-------------|-------|
| Technical feasibility assessment | Solution Assessment document | Stage 2 |
| Architecture and detailed design | Design Specification | Stage 3 |
| Review AI-generated project artifacts | Approved plan.md, tasks | Stage 4-5 |
| Oversee AI task execution | Working code | Stage 6 |
| Code review and testing | Validated code | Stage 7 |
| Technical documentation | Updated docs | Stage 8 |

**Decision Authority**:
- Technical approach selection
- ADR compliance interpretation
- Code quality standards
- Architecture decisions

### 2.3 AI Agent (Claude Code)

**Primary Responsibility**: Execute defined tasks within constraints

| Activity | Human Oversight Required | Stage |
|----------|-------------------------|-------|
| Generate project structure | Review before proceeding | Stage 4 |
| Decompose plan into tasks | Review task list | Stage 5 |
| Execute coding tasks | Spot-check during execution | Stage 6 |
| Run tests and validate | Review test results | Stage 7 |
| Generate documentation | Review before publishing | Stage 8 |

**Operating Boundaries**:
- Must follow ADR constraints
- Must request human input for ambiguous decisions
- Must stop at defined checkpoints
- Must report context usage and trigger handoffs at thresholds

### 2.4 Human-in-the-Loop Checkpoints

```
┌─────────────────────────────────────────────────────────────────────────┐
│                     HUMAN-IN-THE-LOOP CHECKPOINTS                       │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                         │
│  Stage 0 ──► Stage 1      PM validation of research findings           │
│                           ✋ GATE: Problem validated, proceed approved  │
│                                                                         │
│  Stage 1 ──► Stage 2      PM approval of Feature Request               │
│                           ✋ GATE: Business value validated             │
│                                                                         │
│  Stage 2 ──► Stage 3      Technical team approval of approach          │
│                           ✋ GATE: Technical feasibility confirmed      │
│                                                                         │
│  Stage 3 ──► Stage 4      PM + Dev approval of Design Spec             │
│                           ✋ GATE: Design complete and approved         │
│                                                                         │
│  Stage 4 ──► Stage 5      Dev review of project artifacts              │
│                           ⚡ CHECKPOINT: README and plan.md reviewed    │
│                                                                         │
│  Stage 5 ──► Stage 6      Dev review of task decomposition             │
│                           ⚡ CHECKPOINT: Task list validated            │
│                                                                         │
│  During Stage 6           Dev spot-checks AI execution                 │
│                           ⚡ CHECKPOINT: Periodic code review           │
│                                                                         │
│  Stage 6 ──► Stage 7      All tasks complete                           │
│                           ⚡ CHECKPOINT: Code review before testing     │
│                                                                         │
│  Stage 7 ──► Stage 8      Tests pass, ADR compliance verified          │
│                           ✋ GATE: Quality gates passed                 │
│                                                                         │
│  Stage 8 ──► Complete     PM acceptance of feature                     │
│                           ✋ GATE: Feature accepted                     │
│                                                                         │
│  Legend: ✋ GATE = Formal approval required                             │
│          ⚡ CHECKPOINT = Review required, proceed if OK                 │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## 3. Development Lifecycle Overview

### 3.1 Lifecycle Diagram

```
┌─────────────────────────────────────────────────────────────────────────┐
│                    SPAARKE DEVELOPMENT LIFECYCLE                        │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                         │
│   ════════════════════════════════════════════════════════════════     │
│   ║            DISCOVERY & DESIGN PHASES                         ║     │
│   ════════════════════════════════════════════════════════════════     │
│                                                                         │
│   ┌──────────────┐    ┌──────────────┐    ┌──────────────┐             │
│   │   STAGE 0    │    │   STAGE 1    │    │   STAGE 2    │             │
│   │  Discovery   │───►│   Feature    │───►│   Solution   │             │
│   │  & Research  │    │   Request    │    │  Assessment  │             │
│   └──────────────┘    └──────────────┘    └──────────────┘             │
│         │                   │                   │                       │
│         │ PM + UX           │ PM                │ PM + Dev              │
│         ▼                   ▼                   ▼                       │
│   [User Research]      [Notion/Confluence] [RFC + Figma]               │
│   [Prototypes]         [Use Cases]         [Options]                   │
│   [Validation]                                  │                       │
│                                                 ▼                       │
│                                          ┌──────────────┐              │
│                                          │   STAGE 3    │              │
│                                          │   Design     │              │
│                                          │    Spec      │              │
│                                          └──────────────┘              │
│                                                 │                       │
│                                                 │ Dev + PM              │
│                                                 ▼                       │
│                                          [Design Doc]                  │
│                                          [BDD Scenarios]               │
│                                          [Figma Designs]               │
│                                                 │                       │
│   ════════════════════════════════════════════════════════════════     │
│   ║            AI-DIRECTED DEVELOPMENT BEGINS                    ║     │
│   ════════════════════════════════════════════════════════════════     │
│                                                 │                       │
│                                                 ▼                       │
│   ┌──────────────┐    ┌──────────────┐    ┌──────────────┐             │
│   │   STAGE 4    │    │   STAGE 5    │    │   STAGE 6    │             │
│   │   Project    │───►│    Task      │───►│    Task      │             │
│   │    Init      │    │ Decomposition│    │  Execution   │             │
│   └──────────────┘    └──────────────┘    └──────────────┘             │
│         │                   │                   │                       │
│         │ AI + Dev review   │ AI + Dev review   │ AI + Dev spot-check   │
│         ▼                   ▼                   ▼                       │
│    [README.md]         [.poml files]       [Code + Tests]              │
│    [plan.md]           [TASK-INDEX.md]                                 │
│    [CLAUDE.md]                                                         │
│                                                 │                       │
│   ════════════════════════════════════════════════════════════════     │
│   ║            QUALITY GATES                                     ║     │
│   ════════════════════════════════════════════════════════════════     │
│                                                 │                       │
│                                                 ▼                       │
│   ┌──────────────┐    ┌──────────────┐                                 │
│   │   STAGE 7    │    │   STAGE 8    │                                 │
│   │   Testing &  │───►│  Docs &      │───► COMPLETE                    │
│   │  Validation  │    │ Completion   │                                 │
│   └──────────────┘    └──────────────┘                                 │
│         │                   │                                          │
│         │ Dev + ADR check   │ PM acceptance                            │
│         ▼                   ▼                                          │
│    [Test Results]      [Feature Docs]                                  │
│    [ADR Report]        [PR Merged]                                     │
│                                                                         │
└─────────────────────────────────────────────────────────────────────────┘
```

### 3.2 Stage Summary

| Stage | Name | Owner | Input | Output | Tools |
|-------|------|-------|-------|--------|-------|
| 0 | Discovery & Research | PM + UX | Business need | Validated problem, prototypes | Miro, Figma, User interviews |
| 1 | Feature Request | PM | Discovery findings | Feature Request doc | Notion/Confluence |
| 2 | Solution Assessment | PM + Dev | Feature Request | RFC, Solution Assessment | Notion, Figma |
| 3 | Design Specification | Dev (lead) | Solution Assessment | Design Spec + BDD scenarios | Figma, Gherkin |
| 4 | Project Initialization | AI + Dev | Design Spec | README.md, plan.md, CLAUDE.md | Claude Code |
| 5 | Task Decomposition | AI + Dev | plan.md | Task files (.poml) | Claude Code |
| 6 | Task Execution | AI + Dev | Task files | Code, tests | Claude Code |
| 7 | Testing & Validation | Dev | Code | Test results, ADR report | SpecFlow, dotnet test |
| 8 | Documentation & Completion | Dev + PM | All | Feature docs, merged PR | GitHub |

### 3.3 Document Format Conventions

| Stage | Format | Tools | Rationale |
|-------|--------|-------|-----------|
| 0 | Miro boards, Figma prototypes | Miro, Figma | Visual, collaborative, stakeholder-friendly |
| 1-2 | Collaborative docs | Notion/Confluence | Comments, @mentions, versioning |
| 3 | Design doc + BDD | Notion + Gherkin | Executable specs, unambiguous |
| 4-8 | Markdown (.md) / POML (.poml) | VS Code, Claude | AI-optimized, version control friendly |

**Conversion Point**: Design Spec is converted to `spec.md` and placed in `projects/{project-name}/` to begin AI-directed development.

---

## 4. Stage 0: Discovery & Research

### 4.1 Purpose

Validate that we're solving the right problem before investing in solution design. Discovery reduces the risk of building features users don't need or won't use.

### 4.2 When to Use Stage 0

| Situation | Stage 0 Required? |
|-----------|-------------------|
| New feature or module | ✅ Yes - full discovery |
| Enhancement to existing feature | ⚡ Light discovery - user feedback review |
| Bug fix | ❌ No - skip to Stage 1 |
| Technical debt / refactoring | ❌ No - skip to Stage 2 |
| Regulatory / compliance requirement | ⚡ Light discovery - stakeholder validation |

### 4.3 Inputs

- Business hypothesis or customer feedback
- Market research or competitive analysis
- Support tickets or user complaints
- Strategic roadmap

### 4.4 Process

#### 4.4.1 Problem Framing (1-2 days)

1. **Define the hypothesis**: "We believe {user segment} has {problem} because {evidence}"
2. **Identify assumptions**: What must be true for this to be worth building?
3. **Define success metrics**: How will we know if this feature succeeds?

#### 4.4.2 User Research (3-5 days)

| Method | When to Use | Output |
|--------|-------------|--------|
| **User interviews** | Understanding motivations, pain points | Interview notes, quotes |
| **Contextual inquiry** | Observing actual workflows | Journey maps, pain points |
| **Survey** | Validating at scale | Quantitative data |
| **Support ticket analysis** | Understanding common issues | Frequency analysis |

**Minimum**: 5 user interviews for new features

#### 4.4.3 Jobs-to-be-Done Analysis

Frame the problem as user jobs:

```
When [situation], I want to [motivation], so I can [expected outcome].
```

**Example**:
```
When I receive a document for review, 
I want to open it in my desktop Word application, 
so I can use track changes and familiar editing tools.
```

#### 4.4.4 Concept Validation (2-3 days)

1. **Create low-fidelity prototype** in Figma (wireframes, not polished)
2. **Test with 3-5 users**: "Would this solve your problem?"
3. **Iterate based on feedback**
4. **Document validated/invalidated assumptions**

### 4.5 Outputs

| Artifact | Purpose | Tool |
|----------|---------|------|
| **Research Summary** | Key findings, user quotes, patterns | Notion/Confluence |
| **Journey Map** | User's current experience with pain points | Miro/FigJam |
| **Jobs-to-be-Done** | Framed user needs | Notion |
| **Validated Prototype** | Concept that resonates with users | Figma |
| **Assumption Log** | What was validated/invalidated | Notion |

### 4.6 Approval Gate

| Approver | Criteria |
|----------|----------|
| Product Manager | Problem is validated, solution direction is clear |
| (Optional) Stakeholder | Strategic alignment confirmed |

**Exit Criteria**: 
- Problem validated with user evidence
- At least one solution concept tested with users
- Clear job-to-be-done documented
- Decision to proceed, pivot, or kill

---

## 5. Stage 1: Feature Request

### 5.1 Purpose

Capture and document the business need, user value, and high-level requirements for a new feature or enhancement.

### 5.2 Inputs

- Discovery findings (Stage 0) or business need
- Validated user jobs-to-be-done
- Strategic roadmap context
- Related existing features

### 5.3 Process

1. **PM creates Feature Request** in Notion/Confluence (collaborative)
2. **Link discovery artifacts** (research summary, prototypes)
3. **Define use cases** with user scenarios
4. **Stakeholder review** for alignment and priority
5. **Approval gate** - PM confirms business value justifies development

### 5.4 Output: Feature Request Document

**Location**: Notion/Confluence (collaborative), exported to `docs/specs/features/{feature-name}/`

**Template sections**:

| Section | Content |
|---------|---------|
| Overview | Brief description of the feature |
| Business Need | Why this feature is needed (link to discovery) |
| User Value | How users benefit (link to JTBD) |
| User Scenarios | Specific scenarios written as user stories |
| User Roles | Who uses this feature |
| Solution Context | Where it fits in the overall product |
| UX Expectations | Link to Figma prototypes from discovery |
| Success Metrics | How we measure success (quantitative) |
| Discovery Reference | Links to research, journey maps, prototypes |

### 5.5 User Scenario Format

Write scenarios as user stories with acceptance criteria preview:

```markdown
### Scenario: Open Document in Desktop Application

**As a** case manager  
**I want to** open documents in my desktop Office application  
**So that** I can edit with full functionality and track changes

**Initial Acceptance Criteria** (refined in Stage 3):
- Button visible for supported file types (Word, Excel, PowerPoint)
- Clicking button opens correct desktop application
- Document opens with current version from SharePoint
```

### 5.6 Approval Gate

| Approver | Criteria |
|----------|----------|
| Product Manager | Business value confirmed, priority set |
| (Optional) Stakeholder | Strategic alignment confirmed |

**Exit Criteria**: Feature Request approved and prioritized for development

---

## 6. Stage 2: Solution Assessment

### 6.1 Purpose

Develop technical understanding of the request, evaluate solution approaches, and create a Request for Comments (RFC) for significant changes.

### 6.2 Inputs

- Approved Feature Request document
- Figma prototypes from discovery
- Existing architecture documentation
- ADR index

### 6.3 Process

1. **Technical review** of Feature Request with engineering team
2. **Solution brainstorming** - identify 2-3 potential approaches
3. **Architecture impact analysis** - identify affected components
4. **ADR review** - identify applicable architecture decisions
5. **Create RFC** for significant changes (see 6.5)
6. **Iteration** with PM to refine scope based on technical constraints
7. **Approval gate** - Technical feasibility confirmed

### 6.4 Output: Solution Assessment Document

**Location**: Notion/Confluence (collaborative)

**Template sections**:

| Section | Content |
|---------|---------|
| Technical Summary | High-level technical approach |
| Solution Options | 2-3 approaches with pros/cons |
| Recommended Approach | Selected option with rationale |
| Architecture Impact | Components affected, dependencies |
| ADRs Applicable | List of relevant Architecture Decision Records |
| Related Components | Existing code files and modules |
| Technical Risks | Identified risks and mitigations |
| Estimated Effort | High-level estimate (T-shirt size) |
| UI/UX Review | Link to Figma designs, any technical constraints |

### 6.5 RFC (Request for Comments) Process

**When to create RFC**: Changes affecting multiple modules, new integrations, architectural changes.

```markdown
# RFC-{NNN}: {Title}

## Status: Draft | In Review | Accepted | Rejected
## Authors: @developer, @pm
## Reviewers: @architect, @security
## Created: {date}
## Decided: {date}

## Summary
One paragraph explaining the proposal.

## Motivation
Why are we doing this? What problems does it solve?

## Detailed Design
Technical approach with diagrams.

## Alternatives Considered
What else did we consider? Why not those?

## Security Considerations
Any security implications?

## Open Questions
What's still uncertain?

## Decision
What was decided and why?
```

**RFC Location**: `docs/rfcs/RFC-{NNN}-{title}.md`

### 6.6 Approval Gate

| Approver | Criteria |
|----------|----------|
| Lead Developer | Technical approach is sound |
| Product Manager | Approach aligns with requirements |
| (If RFC) | RFC approved by reviewers |

**Exit Criteria**: Solution approach approved, ready for detailed design

---

## 7. Stage 3: Design Specification

### 7.1 Purpose

Create a complete technical design with **executable specifications** (BDD) that can be implemented by AI agents with minimal ambiguity.

### 7.2 Inputs

- Approved Solution Assessment / RFC
- Applicable ADRs
- Existing codebase context
- Figma designs from discovery

### 7.3 Process

1. **Detailed technical design** by lead developer
2. **Component specification** - define new/modified components
3. **API contract definition** - endpoints, payloads, responses
4. **Data model definition** - entities, relationships, schemas
5. **Write BDD scenarios** in Gherkin format (see 7.5)
6. **Finalize Figma designs** - high-fidelity, dev-ready
7. **Code snippets and recommendations** - example patterns to follow
8. **Technical review** - peer review of design
9. **Approval gate** - Design complete and approved

### 7.4 Output: Design Specification Document

**Location**: Notion/Confluence + exported to `projects/{project-name}/spec.md`

**Template sections**:

| Section | Content |
|---------|---------|
| Executive Summary | 1-paragraph overview |
| Problem Statement | What problem this solves (from discovery) |
| Solution Design | Detailed technical approach |
| Architecture Diagram | Visual component relationships |
| Data Model | Entities, fields, relationships |
| API Contracts | Endpoints, methods, payloads (OpenAPI if applicable) |
| UI/UX Specifications | **Figma links** - high-fidelity designs |
| **Acceptance Criteria (BDD)** | **Gherkin scenarios** (see 7.5) |
| Code Recommendations | Patterns, snippets, examples |
| Files to Create | New files with purposes |
| Files to Modify | Existing files with changes |
| ADR Compliance | How design follows ADRs |
| Testing Approach | Unit, integration, E2E strategy |
| Knowledge References | Relevant KM articles |
| Risks and Mitigations | Potential issues and solutions |

### 7.5 Behavior-Driven Design (BDD) with Gherkin

**All acceptance criteria must be written as Gherkin scenarios.** This eliminates ambiguity and enables automated test generation.

#### Gherkin Format

```gherkin
Feature: {Feature Name}
  As a {role}
  I want {capability}
  So that {benefit}

  Background:
    Given {common preconditions for all scenarios}

  Scenario: {Scenario Name}
    Given {precondition}
    And {additional precondition}
    When {action}
    And {additional action}
    Then {expected outcome}
    And {additional expectation}

  Scenario Outline: {Parameterized Scenario}
    Given a document with MIME type "<mime_type>"
    When I click "Edit in Desktop"
    Then the browser should navigate to "<protocol>:ofe|u|<encoded_url>"

    Examples:
      | mime_type                                                              | protocol      |
      | application/vnd.openxmlformats-officedocument.wordprocessingml.document | ms-word       |
      | application/vnd.openxmlformats-officedocument.spreadsheetml.sheet       | ms-excel      |
      | application/vnd.openxmlformats-officedocument.presentationml.presentation | ms-powerpoint |
```

#### Example: Desktop Document Editing Feature

```gherkin
Feature: Open Document in Desktop Application
  As a case manager
  I want to open documents in my desktop Office application
  So that I can edit with full functionality and track changes

  Background:
    Given I am authenticated as a user with document access
    And I am viewing the FileViewer PCF control

  Scenario: Word document shows Edit in Desktop button
    Given I am viewing a document with MIME type "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
    When the FileViewer renders
    Then I should see an "Edit in Desktop" button in the toolbar

  Scenario: Clicking Edit in Desktop opens Word
    Given I am viewing a Word document at URL "https://contoso.sharepoint.com/sites/legal/documents/contract.docx"
    When I click "Edit in Desktop"
    Then the browser should navigate to "ms-word:ofe|u|https%3A%2F%2Fcontoso.sharepoint.com%2Fsites%2Flegal%2Fdocuments%2Fcontract.docx"

  Scenario: Unsupported file type hides button
    Given I am viewing a document with MIME type "application/pdf"
    When the FileViewer renders
    Then I should not see an "Edit in Desktop" button

  Scenario: API returns desktop URL
    Given a valid driveItemId "abc123"
    And the document has MIME type "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
    When I call GET /api/spe/files/{driveItemId}/open-links
    Then the response status should be 200
    And the response should contain "desktopUrl" starting with "ms-word:ofe|u|"

  Scenario: Unauthorized user cannot access API
    Given I am not authenticated
    When I call GET /api/spe/files/abc123/open-links
    Then the response status should be 401
```

#### Why BDD/Gherkin?

| Benefit | Description |
|---------|-------------|
| **Unambiguous** | PM, Dev, and AI interpret scenarios identically |
| **Testable** | Generate automated tests directly from specs |
| **Living documentation** | Specs stay current with code |
| **AI-friendly** | Claude can generate code and tests from Gherkin |
| **Stakeholder-readable** | Non-technical stakeholders can review |

### 7.6 Figma Design Requirements

At Stage 3, Figma designs must be **high-fidelity and dev-ready**:

| Requirement | Description |
|-------------|-------------|
| **Component specs** | Exact dimensions, spacing, colors |
| **States documented** | Loading, error, empty, populated |
| **Interactions defined** | Hover, click, focus states |
| **Responsive behavior** | How component adapts to sizes |
| **Design tokens used** | Colors, typography from design system |
| **Dev mode annotations** | CSS/styling information for developers |

**Link Figma designs** in spec.md for AI reference.

### 7.7 Conversion to spec.md

Before Stage 4, convert the Design Spec to markdown:

1. Create project folder: `projects/{project-name}/`
2. Export/convert to: `projects/{project-name}/spec.md`
3. Include Gherkin scenarios in the `## Acceptance Criteria` section
4. Embed or link Figma designs
5. This becomes the permanent design reference

### 7.8 Approval Gate

| Approver | Criteria |
|----------|----------|
| Lead Developer | Design is complete and implementable |
| Product Manager | Design meets requirements, BDD scenarios approved |
| (Optional) Architect | Architecture is sound |

**Exit Criteria**: Design Spec approved, spec.md created with BDD scenarios, ready for AI-directed development

---

## 8. Stage 4: Project Initialization

### 8.1 Purpose

Create the project folder structure and generate initial artifacts (README, plan, CLAUDE.md) from the design specification.

### 8.2 Inputs

- `projects/{project-name}/spec.md` (converted design spec)

### 8.3 Process

**Skill**: `project-init`  
**Trigger**: `/project-init projects/{project-name}` or "initialize project"

1. **AI reads spec.md** and extracts key information
2. **AI generates folder structure**:
   ```
   projects/{project-name}/
   ├── spec.md             # Design specification (input)
   ├── README.md           # Project overview (generated)
   ├── plan.md             # Implementation plan (generated)
   ├── CLAUDE.md           # AI context file (generated)
   ├── tasks/              # Task files (created empty)
   │   └── TASK-INDEX.md   # Task registry (generated)
   └── notes/              # Ephemeral working files
       ├── debug/
       ├── spikes/
       ├── drafts/
       └── handoffs/
   ```
3. **Developer reviews** generated artifacts
4. **Checkpoint** - Confirm README and plan.md are accurate

### 8.4 Outputs

| File | Purpose | Content Source |
|------|---------|----------------|
| `README.md` | Project overview, goals, graduation criteria | Extracted from spec.md |
| `plan.md` | Implementation plan with WBS phases | Derived from spec.md |
| `CLAUDE.md` | AI context for this project | Generated with project metadata |
| `tasks/TASK-INDEX.md` | Task registry (initially empty) | Generated scaffold |

### 8.5 Developer Checkpoint

Before proceeding to Stage 5, developer reviews:

- [ ] README.md accurately reflects project goals
- [ ] plan.md phases align with design spec
- [ ] Graduation criteria are measurable
- [ ] CLAUDE.md has correct constraints

**Exit Criteria**: Project artifacts reviewed and approved

---

## 9. Stage 5: Task Decomposition

### 9.1 Purpose

Break down the implementation plan into discrete, executable task files that AI can process independently.

### 9.2 Inputs

- `projects/{project-name}/plan.md`
- `projects/{project-name}/spec.md` (for reference)

### 9.3 Process

**Skill**: `task-create`  
**Trigger**: `/task-create {project-name}` or "create tasks"

1. **AI reads plan.md** and extracts WBS phases
2. **AI decomposes phases** into discrete tasks (2-4 hours each)
3. **AI generates task files** in POML format:
   ```
   tasks/
   ├── TASK-INDEX.md
   ├── 001-first-task.poml
   ├── 002-second-task.poml
   ├── 010-phase-2-first.poml
   └── ...
   ```
4. **AI updates TASK-INDEX.md** with all tasks
5. **Developer reviews** task decomposition
6. **Checkpoint** - Confirm tasks are correctly scoped

### 9.4 Task Numbering Convention

| Phase | Task Numbers | Example |
|-------|--------------|---------|
| Phase 1 | 001, 002, 003... | 001-setup-environment.poml |
| Phase 2 | 010, 011, 012... | 010-create-api-endpoint.poml |
| Phase 3 | 020, 021, 022... | 020-build-ui-component.poml |
| Phase 4 | 030, 031, 032... | 030-integration-tests.poml |
| Phase 5 | 040, 041, 042... | 040-deploy-staging.poml |

**Gap rationale**: 10-number gaps allow inserting tasks later without renumbering.

### 9.5 Task File Format (POML)

Each task is a valid XML document with `.poml` extension:

```xml
<?xml version="1.0" encoding="UTF-8"?>
<task id="001" project="{project-name}">
  <metadata>
    <title>{Task Title}</title>
    <phase>{Phase Number}: {Phase Name}</phase>
    <status>not-started</status>
    <estimated-hours>{2-4}</estimated-hours>
    <dependencies>{task IDs or "none"}</dependencies>
    <blocks>{task IDs or "none"}</blocks>
  </metadata>

  <prompt>{Natural language instruction for AI}</prompt>
  <role>{Persona AI should adopt}</role>
  <goal>{Measurable definition of done}</goal>
  
  <context>...</context>
  <constraints>...</constraints>
  <knowledge>...</knowledge>
  <steps>...</steps>
  <tools>...</tools>
  <outputs>...</outputs>
  <acceptance-criteria>...</acceptance-criteria>
  <notes>...</notes>
</task>
```

See [Appendix A: POML Reference](#appendix-a-poml-reference) for complete tag definitions.

### 9.6 Developer Checkpoint

Before proceeding to Stage 6, developer reviews:

- [ ] All plan.md phases have corresponding tasks
- [ ] Tasks are appropriately sized (2-4 hours)
- [ ] Dependencies form a valid sequence (no circular refs)
- [ ] First tasks have no unmet dependencies
- [ ] Acceptance criteria are testable

**Exit Criteria**: Task decomposition reviewed and approved

---

## 10. Stage 6: Task Execution (AI-Directed)

### 10.1 Purpose

AI agent executes tasks sequentially, generating code, tests, and documentation while maintaining context discipline.

### 10.2 Inputs

- Task file (`.poml`) from `projects/{project-name}/tasks/`
- Referenced knowledge files, ADRs, patterns

### 10.3 Execution Environment

**Primary Tool**: Claude Code (VS Code Extension)

**Session Strategy**:
- Execute tasks within a single phase in one session when possible
- Start new session between phases or when context > 70%
- Use handoff summaries to maintain continuity

### 10.4 Task Execution Protocol

For each task, AI follows this protocol:

```
┌─────────────────────────────────────────────────────────────────────────┐
│                     TASK EXECUTION PROTOCOL                             │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                         │
│  ┌──────────────────────────────────────────────────────────────────┐  │
│  │ STEP 0: CONTEXT CHECK                                            │  │
│  │ • Check context usage (aim for < 70%)                            │  │
│  │ • If > 70%: Create handoff summary → Request new session         │  │
│  │ • If < 70%: Proceed                                              │  │
│  └──────────────────────────────────────────────────────────────────┘  │
│                              │                                          │
│                              ▼                                          │
│  ┌──────────────────────────────────────────────────────────────────┐  │
│  │ STEP 1: REVIEW PROGRESS                                          │  │
│  │ • Read project README.md and TASK-INDEX.md                       │  │
│  │ • Verify dependencies are complete                               │  │
│  │ • Check for previous partial work                                │  │
│  └──────────────────────────────────────────────────────────────────┘  │
│                              │                                          │
│                              ▼                                          │
│  ┌──────────────────────────────────────────────────────────────────┐  │
│  │ STEP 2: GATHER RESOURCES                                         │  │
│  │ • Read all files in <inputs> and <knowledge>                     │  │
│  │ • Load applicable ADRs                                           │  │
│  │ • Find existing patterns to follow                               │  │
│  └──────────────────────────────────────────────────────────────────┘  │
│                              │                                          │
│                              ▼                                          │
│  ┌──────────────────────────────────────────────────────────────────┐  │
│  │ STEP 3: PLAN IMPLEMENTATION                                      │  │
│  │ • Break into subtasks                                            │  │
│  │ • Identify code patterns to follow                               │  │
│  │ • List files to create/modify                                    │  │
│  └──────────────────────────────────────────────────────────────────┘  │
│                              │                                          │
│                              ▼                                          │
│  ┌──────────────────────────────────────────────────────────────────┐  │
│  │ STEP 4: IMPLEMENT                                                │  │
│  │ • Execute subtasks in order                                      │  │
│  │ • Write tests alongside code                                     │  │
│  │ • Context check after each subtask                               │  │
│  └──────────────────────────────────────────────────────────────────┘  │
│                              │                                          │
│                              ▼                                          │
│  ┌──────────────────────────────────────────────────────────────────┐  │
│  │ STEP 5: VERIFY                                                   │  │
│  │ • Run linting (npm run lint / dotnet build --warnaserror)        │  │
│  │ • Run tests (dotnet test / npm test)                             │  │
│  │ • Verify build succeeds                                          │  │
│  │ • Check acceptance criteria                                      │  │
│  └──────────────────────────────────────────────────────────────────┘  │
│                              │                                          │
│                              ▼                                          │
│  ┌──────────────────────────────────────────────────────────────────┐  │
│  │ STEP 6: DOCUMENT                                                 │  │
│  │ • Update TASK-INDEX.md status (🔲 → ✅)                          │  │
│  │ • Document any deviations in notes/                              │  │
│  │ • Generate completion report                                     │  │
│  └──────────────────────────────────────────────────────────────────┘  │
│                                                                         │
└─────────────────────────────────────────────────────────────────────────┘
```

### 10.5 Context Management

Claude Code has a finite context window (~200K tokens). Context management is critical.

#### Context Thresholds

| Usage | Level | Action |
|-------|-------|--------|
| < 50% | Normal | ✅ Proceed normally |
| 50-70% | Warning | ⚠️ Monitor, consider completing current subtask |
| > 70% | Critical | 🛑 STOP - Create handoff summary, request new session |
| > 85% | Emergency | 🚨 Immediately create handoff, context may truncate |

#### Context Reset Protocol

When context exceeds 70%:

1. **Create handoff summary** in `notes/handoffs/handoff-{NNN}.md`:
   - Task ID and title
   - Completed subtasks
   - Remaining subtasks
   - Files modified
   - Decisions made
   - Next steps
   - Resources needed for continuation

2. **Instruct user**: "Context at {X}%. Please start new session with this handoff."

3. **New session** reads handoff and continues

### 10.6 Human Oversight During Execution

**Developer spot-checks**:
- Review AI-generated code periodically (every 2-3 tasks)
- Validate architectural decisions align with ADRs
- Check for security issues or anti-patterns
- Intervene if AI appears stuck or going off-track

**When to intervene**:
- AI requests human input on ambiguous decisions
- Code review reveals significant issues
- Tests are failing and AI can't resolve
- Context management failures

### 10.7 Starting a Task Execution Session

**User prompt to AI**:
```
Execute task defined in: projects/{project-name}/tasks/001-task-name.poml

Follow the task execution protocol. Check context at start and after each subtask.
```

**Exit Criteria**: All tasks complete, code compiles, tests pass

---

## 11. Stage 7: Testing & Validation

### 11.1 Purpose

Validate that all code meets quality standards, follows ADRs, and satisfies acceptance criteria.

### 11.2 Process

#### 11.2.1 Linting

Run automated static code analysis before tests:

| Language | Tool | Command | Config |
|----------|------|---------|--------|
| TypeScript/PCF | ESLint | `cd src/client/pcf && npm run lint` | `eslint.config.mjs` |
| C# | Roslyn Analyzers | `dotnet build --warnaserror` | `Directory.Build.props` |

**Linting catches**:
- Syntax errors before runtime
- Unused variables and imports
- Type issues and null reference risks
- Security patterns (via analyzers)
- React hooks rule violations (TypeScript)

**Auto-fix commands**:
- TypeScript: `npx eslint --fix {files}`
- C#: `dotnet format`

> ⚠️ **Lint errors should be fixed before running tests.** CI/CD will fail on lint errors.

#### 11.2.2 Automated Testing

| Test Type | Command | Requirement |
|-----------|---------|-------------|
| Unit Tests | `dotnet test` / `npm test` | All tests pass |
| Integration Tests | `dotnet test --filter Integration` | All tests pass |
| E2E Tests | `npx playwright test` | All tests pass |
| Build | `dotnet build` / `npm run build` | No errors |

#### 11.2.3 ADR Validation

**Automated validation** via CI/CD:
- Runs on every pull request
- Posts violations as PR comment
- Non-blocking but tracked

**Manual validation**:
- Run `/adr-check` skill locally
- Run `dotnet test tests/Spaarke.ArchTests/`
- Review and address violations

**Violation Priority**:

| Priority | Action | Examples |
|----------|--------|----------|
| High | Fix before merge | ADR-001 (runtime), ADR-007 (security), ADR-009 (caching) |
| Medium | Fix in sprint | ADR-002, ADR-008, ADR-010 (maintainability) |
| Low | Track as tech debt | Pattern deviations |

#### 11.2.4 Code Review

Human developer reviews all AI-generated code:

- [ ] Linting passes (no ESLint errors, no C# warnings)
- [ ] Code follows project conventions
- [ ] No security vulnerabilities
- [ ] Error handling is appropriate
- [ ] Logging is adequate but not excessive
- [ ] No hardcoded secrets or URLs
- [ ] Tests cover critical paths

### 11.3 Approval Gate

| Approver | Criteria |
|----------|----------|
| Developer | Linting passes, all tests pass, code review complete |
| CI/CD | Build succeeds, lint check passes, ADR check complete |

**Exit Criteria**: All quality gates passed, ready for documentation and merge

---

## 12. Stage 8: Documentation & Completion

### 12.1 Purpose

Finalize documentation, merge code, and close out the project.

### 12.2 Process

#### 12.2.1 Feature Documentation

Create or update user-facing documentation:

- Feature description and purpose
- How to use the feature
- Configuration options
- Known limitations

**Location**: `docs/user/` or appropriate section

#### 12.2.2 Technical Documentation

Update technical documentation:

- Architecture diagrams (if changed)
- API documentation (if new endpoints)
- Component documentation
- Updated solution architecture

#### 12.2.3 Code Documentation

Verify in-code documentation:

- Public methods have XML doc comments (C#) or JSDoc (TypeScript)
- Complex logic has inline comments explaining "why"
- README files in affected modules are current

#### 12.2.4 Project Cleanup

1. **Review project artifacts**:
   - All tasks marked ✅ in TASK-INDEX.md
   - notes/ directory reviewed (delete or archive as appropriate)
   - No temporary files left behind

2. **Commit and PR**:
   - Create pull request with meaningful description
   - Reference original Feature Request
   - Link to Design Spec

3. **Merge**:
   - PR approved by reviewer
   - CI/CD passes
   - Merge to main branch

#### 12.2.5 Project Graduation

**Graduation checklist**:
- [ ] All graduation criteria from README.md met
- [ ] Feature documentation complete
- [ ] Technical documentation updated
- [ ] PR merged to main
- [ ] PM accepts feature delivery

### 12.3 Approval Gate

| Approver | Criteria |
|----------|----------|
| Product Manager | Feature meets requirements, documentation acceptable |
| Developer | Technical documentation complete, code merged |

**Exit Criteria**: Project complete, artifacts archived or cleaned up

---

## 13. Supporting Infrastructure

### 13.1 Repository Structure

```
spaarke/
├── .claude/                    # Claude Code configuration
│   ├── skills/                 # AI workflow definitions
│   │   ├── INDEX.md
│   │   ├── adr-check/
│   │   ├── code-review/
│   │   ├── design-to-project/
│   │   ├── project-init/
│   │   ├── spaarke-conventions/
│   │   └── task-create/
│   └── commands/               # Custom Claude commands
├── .github/                    # GitHub workflows, PR templates
├── .vscode/                    # VS Code settings
├── docs/                       # Documentation
│   ├── adr/                    # Architecture Decision Records
│   ├── ai-knowledge/           # AI context and templates
│   │   └── templates/          # Task, project templates
│   ├── reference/              # Reference documentation
│   ├── specs/                  # Feature specifications
│   └── user/                   # User documentation
├── projects/                   # Active development projects
│   └── {project-name}/
│       ├── spec.md
│       ├── README.md
│       ├── plan.md
│       ├── CLAUDE.md
│       ├── tasks/
│       └── notes/
├── src/                        # Application source code
│   ├── client/                 # Frontend code
│   │   ├── pcf/                # PCF controls
│   │   └── shared/             # Shared UI components
│   ├── server/                 # Backend code
│   │   ├── api/                # API projects
│   │   └── shared/             # Shared .NET code
│   └── dataverse/              # Dataverse artifacts
│       ├── plugins/
│       └── solutions/
├── tests/                      # Test suites
│   ├── unit/
│   ├── integration/
│   └── e2e/
└── CLAUDE.md                   # Root AI context
```

### 13.2 Skills System

Skills are reusable AI workflows defined in `.claude/skills/`.

| Skill | Purpose | Trigger |
|-------|---------|---------|
| `adr-check` | Validate code against ADRs | `/adr-check` |
| `code-review` | Comprehensive code review | `/code-review` |
| `design-to-project` | Full design-to-implementation pipeline | `/design-to-project` |
| `project-init` | Initialize project folder structure | `/project-init` |
| `spaarke-conventions` | Coding standards (always applied) | Auto |
| `task-create` | Decompose plan into task files | `/task-create` |

**Skill invocation**: Type trigger phrase in Claude Code chat or use slash command.

### 13.3 Architecture Decision Records (ADRs)

ADRs capture significant architectural decisions. All code must comply.

**Location**: `docs/adr/`  
**Index**: `docs/adr/README-ADRs.md`

**Key ADRs**:

| ADR | Title | Key Constraint |
|-----|-------|----------------|
| ADR-001 | Minimal API and Workers | No Azure Functions, use Minimal APIs |
| ADR-002 | No Heavy Plugins | Plugin execution < 50ms |
| ADR-006 | PCF over Webresources | Build PCF controls, not JS webresources |
| ADR-007 | SPE Storage Seam | Use SpeFileStore facade |
| ADR-008 | Authorization Endpoint Filters | Use endpoint filters, not middleware |
| ADR-009 | Caching Redis First | Use Redis for caching |
| ADR-010 | DI Minimalism | Static methods for pure functions |

### 13.4 Knowledge Articles

Knowledge articles provide technical guidance and best practices.

**Location**: `docs/ai-knowledge/` or `docs/KM-*.md`

**Topics**: PCF development, Dataverse patterns, Graph API, OAuth/MSAL, etc.

### 13.5 Embedded @ai-meta Comment Blocks

Best practice for key code files - include a header comment for AI context:

```csharp
// @ai-meta:
// module: Sdap.Documents
// role: Minimal API endpoints for document operations in SDAP.
// related-docs:
//   - docs/architecture/SDAP-ARCHITECTURE-GUIDE.md
//   - src/Modules/Sdap/docs/MAP.md
// invariants:
//   - Use Platform.Documents for data access.
//   - No direct Dataverse or SPE calls.
```

### 13.6 CLAUDE.md Files

CLAUDE.md files provide persistent AI context, loaded automatically by Claude Code.

**Hierarchy**:
- Root `CLAUDE.md` - Repository-wide conventions
- Module `CLAUDE.md` - Module-specific context (e.g., `src/server/api/CLAUDE.md`)
- Project `CLAUDE.md` - Project-specific context (e.g., `projects/{name}/CLAUDE.md`)

**Content guidelines**:
- Keep under 5,000 tokens total
- Include actionable information (commands, conventions, patterns)
- Exclude verbose explanations (link to docs instead)
- Use emphasis for critical rules ("IMPORTANT", "MUST")

---

## Appendix A: POML Reference

### A.1 Overview

**POML** (Prompt Orchestration Markup Language) provides structured prompt components for AI agent execution.

**File extension**: `.poml`  
**Format**: Valid XML document  
**Reference**: https://microsoft.github.io/poml/stable/

### A.2 Tag Definitions

#### `<task>`
Root element containing all task information.

**Attributes**:
- `id` - Task number (e.g., "001")
- `project` - Project name

#### `<metadata>`
Task metadata for tracking and dependencies.

**Child elements**:
- `<title>` - Human-readable task name
- `<phase>` - Phase number and name
- `<status>` - not-started | in-progress | complete | blocked
- `<estimated-hours>` - Effort estimate
- `<dependencies>` - Prerequisite task IDs
- `<blocks>` - Tasks blocked by this one
- `<tags>` - Context focus tags for Claude Code (see Standard Tag Vocabulary)

#### `<tags>`
Semantic tags that help Claude Code focus context when executing the task.

**Purpose**: Enable targeted context loading - Claude Code can load relevant CLAUDE.md files, skills, and knowledge docs based on task tags rather than exploring the entire codebase.

**Standard vocabulary**:
| Category | Tags |
|----------|------|
| API/Backend | `bff-api`, `api`, `backend`, `minimal-api`, `endpoints` |
| Frontend/PCF | `pcf`, `react`, `typescript`, `frontend`, `fluent-ui` |
| Dataverse | `dataverse`, `solution`, `fields`, `plugin`, `ribbon` |
| Azure | `azure`, `app-service`, `azure-ai`, `azure-search`, `bicep` |
| AI/ML | `azure-openai`, `ai`, `embeddings`, `document-intelligence` |
| Operations | `deploy`, `ci-cd`, `devops`, `infrastructure` |
| Quality | `testing`, `unit-test`, `integration-test`, `e2e-test` |

**Example**:
```xml
<metadata>
  <tags>bff-api, azure-openai, services</tags>
</metadata>
```

#### `<prompt>`
Natural-language description of the task's intent. The "task narrative" explaining purpose, scope, and expected outcome.

**Purpose**: Provides high-level guidance, establishes context, describes why the task exists.

**AI treatment**: Intent anchor (NOT step-by-step instructions).

#### `<role>`
Persona, responsibilities, and behavior the AI should adopt.

**Common roles**:
- Spaarke AI Developer Agent
- Spaarke Technical Architect
- Senior .NET developer with Minimal API expertise
- PCF control developer with Fluent UI experience

#### `<goal>`
Precise, outcome-oriented statement of what must be achieved. The success contract.

**Characteristics**:
- Must be measurable
- Cannot be ambiguous
- Describes a final state

#### `<context>`
Additional qualitative background information.

**Child elements**:
- `<background>` - Why this task exists, business context
- `<relevant-files>` - Files related to this task
- `<dependencies>` - Task dependencies with status

#### `<constraints>`
Hard rules that must be adhered to during execution.

**Attribute**: `source` - ADR reference or "project"

**Examples**:
```xml
<constraint source="ADR-001">Use Minimal API patterns</constraint>
<constraint source="ADR-010">Use static methods for pure functions</constraint>
<constraint source="project">Must not break existing tests</constraint>
```

#### `<knowledge>`
Reference technical approaches and best practices.

**Child elements**:
- `<files>` - Knowledge articles and ADRs to read
- `<patterns>` - Code patterns to follow with locations

#### `<steps>`
Deterministic sequence of actions.

**Child element**: `<step order="N">` - Individual action with sequence number

**Characteristics**:
- Sequential execution
- No ambiguity
- Describes *what* to do, not *how to think*

#### `<tools>`
External tools the AI may use.

**Examples**:
```xml
<tool name="dotnet">Build and test .NET projects</tool>
<tool name="npm">Build TypeScript/PCF projects</tool>
<tool name="terminal">Run shell commands</tool>
```

#### `<outputs>`
Artifacts to be produced or modified.

**Attribute**: `type` - code | test | docs

**Examples**:
```xml
<output type="code">src/api/Services/MyService.cs</output>
<output type="test">tests/unit/MyServiceTests.cs</output>
```

#### `<acceptance-criteria>`
Testable success criteria.

**Child element**: `<criterion testable="true">` - Individual criterion

**Format**: Given {precondition}, when {action}, then {expected result}.

#### `<examples>`
Reference samples or patterns to follow.

**Child element**: `<example name="..." location="...">` - Example with description

#### `<notes>`
Implementation hints, gotchas, references to spec sections.

---

## Appendix B: Template Inventory

| Template | Location | Purpose |
|----------|----------|---------|
| Project README | `docs/ai-knowledge/templates/project-README.template.md` | Project overview |
| Project Plan | `docs/ai-knowledge/templates/project-plan.template.md` | Implementation plan |
| Task Execution | `docs/ai-knowledge/templates/task-execution.template.md` | AI execution protocol |
| AI Agent Playbook | `docs/ai-knowledge/templates/AI-AGENT-PLAYBOOK.md` | Full lifecycle guide |

---

## Appendix C: Context Engineering Quick Reference

### C.1 Context Window

Claude Code has ~200,000 token context window encompassing:
- Conversation history
- File reads
- Tool interactions
- CLAUDE.md content
- AI outputs

### C.2 Best Practices

| Practice | Description |
|----------|-------------|
| **Monitor usage** | Check context before memory-intensive operations |
| **Document and clear** | Write progress to file, clear context, continue in new session |
| **Working directory** | Launch from specific module, not repo root |
| **Be specific** | Clear instructions reduce iterations and wasted tokens |
| **Offload to files** | Write design docs, checklists to files rather than keeping in memory |

### C.3 Context Commands

| Command | Purpose |
|---------|---------|
| `/context` | Display current context usage |
| `/clear` | Wipe conversation context |
| `/compact` | Compress conversation to reclaim space |
| `/resume` | Revisit previous session |

### C.4 Thresholds

| Usage | Level | Action |
|-------|-------|--------|
| < 50% | Normal | Proceed |
| 50-70% | Warning | Monitor, wrap up current subtask |
| > 70% | Critical | STOP, create handoff, new session |
| > 85% | Emergency | Immediately create handoff |

---

## Appendix D: Stage Checklists

### D.0 Stage 0: Discovery & Research Checklist

- [ ] Problem hypothesis defined
- [ ] Key assumptions identified
- [ ] User interviews conducted (minimum 5 for new features)
- [ ] Jobs-to-be-done documented
- [ ] Journey map created (for UX-heavy features)
- [ ] Low-fidelity prototype created in Figma
- [ ] Prototype validated with 3-5 users
- [ ] Assumptions validated/invalidated documented
- [ ] Research summary written
- [ ] Decision: proceed / pivot / kill

### D.1 Stage 1: Feature Request Checklist

- [ ] Discovery artifacts linked (if Stage 0 was done)
- [ ] Business need documented
- [ ] User value articulated (linked to JTBD)
- [ ] User scenarios defined (3-5 primary)
- [ ] User roles identified
- [ ] UX expectations described (Figma links)
- [ ] Success metrics defined
- [ ] PM approval obtained

### D.2 Stage 2: Solution Assessment Checklist

- [ ] Technical team reviewed Feature Request
- [ ] 2-3 solution options evaluated
- [ ] Recommended approach selected with rationale
- [ ] Architecture impact analyzed
- [ ] Applicable ADRs identified
- [ ] Related components documented
- [ ] RFC created (for significant changes)
- [ ] RFC reviewed and approved (if applicable)
- [ ] Technical risks identified
- [ ] Effort estimated
- [ ] PM + Dev approval obtained

### D.3 Stage 3: Design Specification Checklist

- [ ] Detailed technical design complete
- [ ] Architecture diagram created (if applicable)
- [ ] Data model defined
- [ ] API contracts specified
- [ ] UI/UX specifications documented (Figma high-fidelity)
- [ ] **BDD scenarios written in Gherkin format**
- [ ] Code recommendations provided
- [ ] Files to create/modify listed
- [ ] ADR compliance documented
- [ ] Acceptance criteria testable
- [ ] Testing approach defined
- [ ] Peer review complete
- [ ] PM + Dev approval obtained
- [ ] spec.md created in projects folder

### D.4 Stage 4: Project Initialization Checklist

- [ ] Project folder created at `projects/{project-name}/`
- [ ] spec.md in place
- [ ] README.md generated and reviewed
- [ ] plan.md generated and reviewed
- [ ] CLAUDE.md generated
- [ ] tasks/ directory created
- [ ] notes/ directory created with subdirectories
- [ ] Developer approval obtained

### D.5 Stage 5: Task Decomposition Checklist

- [ ] All plan.md phases have tasks
- [ ] Task numbering follows convention (001, 010, 020...)
- [ ] Tasks sized appropriately (2-4 hours)
- [ ] Dependencies form valid sequence
- [ ] First tasks have no unmet dependencies
- [ ] Each task has all required POML sections
- [ ] Acceptance criteria are testable
- [ ] TASK-INDEX.md updated
- [ ] Developer approval obtained

### D.6 Stage 6: Task Execution Checklist

Per task:
- [ ] Context check performed (< 70%)
- [ ] Progress reviewed
- [ ] Resources gathered
- [ ] Implementation planned
- [ ] Code implemented
- [ ] Tests written
- [ ] Tests pass
- [ ] Build succeeds
- [ ] TASK-INDEX.md updated (✅)
- [ ] Deviations documented

### D.7 Stage 7: Testing & Validation Checklist

- [ ] All unit tests pass
- [ ] All integration tests pass
- [ ] All E2E tests pass
- [ ] Build succeeds
- [ ] ADR validation run
- [ ] High-priority violations fixed
- [ ] Code review complete
- [ ] Security review complete
- [ ] Developer approval obtained

### D.8 Stage 8: Documentation & Completion Checklist

- [ ] Feature documentation created/updated
- [ ] Technical documentation updated
- [ ] In-code documentation verified
- [ ] All tasks marked complete in TASK-INDEX.md
- [ ] notes/ directory cleaned
- [ ] Pull request created
- [ ] PR approved
- [ ] CI/CD passes
- [ ] PR merged
- [ ] Graduation criteria met
- [ ] PM acceptance obtained

---

## Appendix E: Quick Start - Design Spec to Completion

> **Purpose**: Step-by-step guide for running a project from design spec through completion.
> **Audience**: Developers starting AI-directed development with an approved design specification.

### Prerequisites

Before starting, ensure you have:
- [ ] Approved Design Specification (Stage 3 complete)
- [ ] Design Spec converted to `spec.md`
- [ ] BDD scenarios (Gherkin) included in spec.md
- [ ] VS Code with Claude Code extension installed
- [ ] Access to the spaarke repository

### Quick Start Steps

```
┌─────────────────────────────────────────────────────────────────────────┐
│              QUICK START: DESIGN SPEC TO COMPLETION                     │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                         │
│  STEP 1: CREATE PROJECT FOLDER                                         │
│  ─────────────────────────────────────────────────────────────────────  │
│  mkdir projects/{project-name}                                         │
│  # Copy spec.md into the folder                                        │
│                                                                         │
│  STEP 2: INITIALIZE PROJECT (Claude Code)                              │
│  ─────────────────────────────────────────────────────────────────────  │
│  Prompt: "/project-init projects/{project-name}"                       │
│  Review: README.md, plan.md, CLAUDE.md                                 │
│  ⚡ CHECKPOINT: Developer reviews artifacts                            │
│                                                                         │
│  STEP 3: CREATE TASKS (Claude Code)                                    │
│  ─────────────────────────────────────────────────────────────────────  │
│  Prompt: "/task-create {project-name}"                                 │
│  Review: tasks/*.poml, TASK-INDEX.md                                   │
│  ⚡ CHECKPOINT: Developer reviews task decomposition                   │
│                                                                         │
│  STEP 4: EXECUTE TASKS (Claude Code - repeat for each task)            │
│  ─────────────────────────────────────────────────────────────────────  │
│  Prompt: "Execute task defined in:                                     │
│           projects/{project-name}/tasks/001-task-name.poml"            │
│  Monitor: Context usage, code quality                                  │
│  ⚡ CHECKPOINT: Spot-check code every 2-3 tasks                        │
│                                                                         │
│  STEP 5: VALIDATE (Developer)                                          │
│  ─────────────────────────────────────────────────────────────────────  │
│  Run: dotnet test / npm test                                           │
│  Run: /adr-check                                                       │
│  Review: All tests pass, no ADR violations                             │
│  ✋ GATE: Quality gates passed                                          │
│                                                                         │
│  STEP 6: COMPLETE (Developer + PM)                                     │
│  ─────────────────────────────────────────────────────────────────────  │
│  Update: Feature documentation                                         │
│  Create: Pull request                                                  │
│  Merge: After approval                                                 │
│  ✋ GATE: PM accepts feature                                            │
│                                                                         │
└─────────────────────────────────────────────────────────────────────────┘
```

### Detailed Step-by-Step

#### Step 1: Set Up Project Folder

```powershell
# Create project folder
mkdir projects/{project-name}

# Copy your spec.md into the folder
# Ensure spec.md includes BDD scenarios in Gherkin format
```

**Verify spec.md contains**:
- Problem statement
- Solution design
- API contracts (if applicable)
- UI/UX specifications (with Figma links)
- **BDD scenarios in Gherkin format** (critical for AI)
- Files to create/modify
- ADR compliance notes

#### Step 2: Initialize Project

**In Claude Code (VS Code Extension)**:

```
/project-init projects/{project-name}
```

Or use natural language:
```
Initialize the project at projects/{project-name}. 
Read the spec.md and create README.md, plan.md, and CLAUDE.md.
```

**AI generates**:
- `README.md` - Project overview and graduation criteria
- `plan.md` - Implementation plan with WBS phases
- `CLAUDE.md` - AI context for this project
- `tasks/` directory
- `notes/` directory with subdirectories

**Developer reviews**:
- [ ] README.md reflects project goals
- [ ] plan.md phases match design spec
- [ ] Graduation criteria are measurable

#### Step 3: Create Tasks

**In Claude Code**:

```
/task-create {project-name}
```

Or:
```
Create tasks for {project-name} from the plan.md.
Decompose each phase into 2-4 hour tasks.
```

**AI generates**:
- `tasks/TASK-INDEX.md` - Task registry
- `tasks/001-{name}.poml` - Individual task files
- `tasks/010-{name}.poml` - Phase 2 tasks
- etc.

**Developer reviews**:
- [ ] All phases have tasks
- [ ] Tasks are 2-4 hours each
- [ ] Dependencies are valid
- [ ] Acceptance criteria are testable

#### Step 4: Execute Tasks

**For each task, in Claude Code**:

```
Execute task defined in: projects/{project-name}/tasks/001-task-name.poml

Follow the task execution protocol. Check context at start and after each subtask.
```

**AI execution protocol**:
1. Context check (< 70%)
2. Review progress and dependencies
3. Gather resources (ADRs, patterns, knowledge)
4. Plan implementation
5. Implement with tests
6. Verify (build, test)
7. Update TASK-INDEX.md status

**Context management**:
- If context > 70%: AI creates handoff summary → Start new session
- Reset sessions between phases
- Keep handoffs in `notes/handoffs/`

**Developer oversight**:
- Spot-check code every 2-3 tasks
- Intervene if AI is stuck or off-track
- Validate architectural decisions

#### Step 5: Validate

**Run tests**:
```powershell
# .NET projects
dotnet test

# TypeScript/PCF projects
npm test
```

**Check ADR compliance**:
```
/adr-check
```

Or run architecture tests:
```powershell
dotnet test tests/Spaarke.ArchTests/
```

**Verify**:
- [ ] All unit tests pass
- [ ] All integration tests pass
- [ ] Build succeeds
- [ ] No high-priority ADR violations
- [ ] Code review complete

#### Step 6: Complete

**Update documentation**:
- Feature documentation in `docs/user/`
- Technical documentation updates
- Verify in-code documentation

**Create PR**:
```powershell
git checkout -b feature/{project-name}
git add .
git commit -m "feat: {description}"
git push origin feature/{project-name}
```

**PR description should include**:
- Link to Design Spec
- Summary of changes
- Test results
- ADR compliance status

**After merge**:
- [ ] Graduation criteria met
- [ ] PM accepts feature
- [ ] Clean up notes/ directory
- [ ] Archive or delete project folder (optional)

### Common Commands Reference

| Action | Command / Prompt |
|--------|------------------|
| Initialize project | `/project-init projects/{name}` |
| Create tasks | `/task-create {name}` |
| Execute task | `Execute task defined in: projects/{name}/tasks/001-task.poml` |
| Check ADR compliance | `/adr-check` |
| Code review | `/code-review` |
| Check context | `/context` |
| Clear context | `/clear` |

### Session Management Tips

| Situation | Action |
|-----------|--------|
| Starting new phase | Start fresh Claude Code session |
| Context > 70% | Create handoff → `/clear` → New session with handoff |
| AI stuck | Provide clarification or break task into smaller pieces |
| Need to pause | Have AI create progress summary in `notes/` |
| Resuming work | Start session, point AI to `notes/handoffs/` |

### Troubleshooting

| Issue | Solution |
|-------|----------|
| AI generates wrong code pattern | Reference specific ADR: "Follow ADR-008 for auth" |
| Tests failing | Have AI read test output and fix, or break into smaller steps |
| Context running out | Create handoff summary, start new session |
| AI missing context | Explicitly tell AI to read specific files |
| Task too large | Split into subtasks manually or ask AI to decompose |

---

*Document version: 2.0 | December 4, 2025*
