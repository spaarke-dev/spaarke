# Stages 1-3: Planning & Design

> **Audience**: Product Managers, Software Engineers  
> **Part of**: [Spaarke Software Development Procedures](INDEX.md)

---

## Stage 1: Feature Request

### Purpose

Capture and document the business need, user value, and high-level requirements for a new feature or enhancement.

### Inputs

- Discovery findings (Stage 0) or business need
- Validated user jobs-to-be-done
- Strategic roadmap context

### Process

1. **PM creates Feature Request** in Notion/Confluence (collaborative)
2. **Link discovery artifacts** (research summary, prototypes)
3. **Define use cases** with user scenarios
4. **Stakeholder review** for alignment and priority
5. **Approval gate** - PM confirms business value justifies development

### Output: Feature Request Document

**Location**: Notion/Confluence (collaborative)

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

### User Scenario Format

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

### Approval Gate

| Approver | Criteria |
|----------|----------|
| Product Manager | Business value confirmed, priority set |

**Exit Criteria**: Feature Request approved and prioritized for development

---

## Stage 2: Solution Assessment

### Purpose

Develop technical understanding, evaluate solution approaches, and create an RFC for significant changes.

### Inputs

- Approved Feature Request document
- Figma prototypes from discovery
- Existing architecture documentation
- ADR index

### Process

1. **Technical review** of Feature Request with engineering team
2. **Solution brainstorming** - identify 2-3 potential approaches
3. **Architecture impact analysis** - identify affected components
4. **ADR review** - identify applicable architecture decisions
5. **Create RFC** for significant changes (see below)
6. **Iteration** with PM to refine scope based on technical constraints
7. **Approval gate** - Technical feasibility confirmed

### Output: Solution Assessment Document

**Location**: Notion/Confluence (collaborative)

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

### RFC (Request for Comments) Process

**When to create RFC**: Changes affecting multiple modules, new integrations, architectural changes.

```markdown
# RFC-{NNN}: {Title}

## Status: Draft | In Review | Accepted | Rejected
## Authors: @developer, @pm
## Reviewers: @architect, @security
## Created: {date}

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

### Approval Gate

| Approver | Criteria |
|----------|----------|
| Lead Developer | Technical approach is sound |
| Product Manager | Approach aligns with requirements |

**Exit Criteria**: Solution approach approved, ready for detailed design

---

## Stage 3: Design Specification

### Purpose

Create a complete technical design with **executable specifications** (BDD) that can be implemented by AI agents with minimal ambiguity.

### Inputs

- Approved Solution Assessment / RFC
- Applicable ADRs
- Existing codebase context
- Figma designs from discovery

### Process

1. **Detailed technical design** by lead developer
2. **Component specification** - define new/modified components
3. **API contract definition** - endpoints, payloads, responses
4. **Data model definition** - entities, relationships, schemas
5. **Write BDD scenarios** in Gherkin format (see below)
6. **Finalize Figma designs** - high-fidelity, dev-ready
7. **Code snippets and recommendations** - example patterns to follow
8. **Technical review** - peer review of design
9. **Approval gate** - Design complete and approved

### Output: Design Specification Document

**Location**: Notion/Confluence + exported to `projects/{project-name}/SPEC.md`

| Section | Content |
|---------|---------|
| Executive Summary | 1-paragraph overview |
| Problem Statement | What problem this solves (from discovery) |
| Solution Design | Detailed technical approach |
| Architecture Diagram | Visual component relationships |
| Data Model | Entities, fields, relationships |
| API Contracts | Endpoints, methods, payloads |
| UI/UX Specifications | **Figma links** - high-fidelity designs |
| **Acceptance Criteria (BDD)** | **Gherkin scenarios** (see below) |
| Code Recommendations | Patterns, snippets, examples |
| Files to Create | New files with purposes |
| Files to Modify | Existing files with changes |
| ADR Compliance | How design follows ADRs |
| Testing Approach | Unit, integration, E2E strategy |

### Behavior-Driven Design (BDD) with Gherkin

**All acceptance criteria must be written as Gherkin scenarios.** This eliminates ambiguity and enables automated test generation.

#### Gherkin Format

```gherkin
Feature: {Feature Name}
  As a {role}
  I want {capability}
  So that {benefit}

  Scenario: {Scenario Name}
    Given {precondition}
    When {action}
    Then {expected outcome}

  Scenario Outline: {Parameterized Scenario}
    Given a document with MIME type "<mime_type>"
    When I click "Edit in Desktop"
    Then the browser should navigate to "<protocol>:ofe|u|<url>"

    Examples:
      | mime_type                  | protocol  |
      | application/vnd...word     | ms-word   |
      | application/vnd...excel    | ms-excel  |
```

#### Example: Desktop Document Editing

```gherkin
Feature: Open Document in Desktop Application
  As a case manager
  I want to open documents in my desktop Office application
  So that I can edit with full functionality

  Scenario: Word document shows Edit in Desktop button
    Given I am viewing a Word document
    When the FileViewer renders
    Then I should see an "Edit in Desktop" button

  Scenario: Clicking Edit in Desktop opens Word
    Given I am viewing a Word document at URL "https://..."
    When I click "Edit in Desktop"
    Then the browser should navigate to "ms-word:ofe|u|..."

  Scenario: Unsupported file type hides button
    Given I am viewing a PDF document
    When the FileViewer renders
    Then I should not see an "Edit in Desktop" button
```

#### Why BDD/Gherkin?

| Benefit | Description |
|---------|-------------|
| **Unambiguous** | PM, Dev, and AI interpret scenarios identically |
| **Testable** | Generate automated tests directly from specs |
| **AI-friendly** | Claude can generate code and tests from Gherkin |
| **Stakeholder-readable** | Non-technical reviewers can understand |

### Figma Design Requirements

At Stage 3, Figma designs must be **high-fidelity and dev-ready**:

| Requirement | Description |
|-------------|-------------|
| Component specs | Exact dimensions, spacing, colors |
| States documented | Loading, error, empty, populated |
| Interactions defined | Hover, click, focus states |
| Responsive behavior | How component adapts to sizes |
| Dev mode annotations | CSS/styling information |

### Conversion to SPEC.md

Before Stage 4:

1. Create project folder: `projects/{project-name}/`
2. Export/convert to: `projects/{project-name}/SPEC.md`
3. Include Gherkin scenarios in `## Acceptance Criteria` section
4. Embed or link Figma designs
5. This becomes the permanent design reference

### Approval Gate

| Approver | Criteria |
|----------|----------|
| Lead Developer | Design is complete and implementable |
| Product Manager | Design meets requirements, BDD scenarios approved |

**Exit Criteria**: Design Spec approved, SPEC.md created with BDD scenarios, ready for AI-directed development

---

## Stage Checklists

### Stage 1 Checklist
- [ ] Discovery artifacts linked (if Stage 0 was done)
- [ ] Business need documented
- [ ] User value articulated (linked to JTBD)
- [ ] User scenarios defined (3-5 primary)
- [ ] User roles identified
- [ ] UX expectations described (Figma links)
- [ ] Success metrics defined
- [ ] PM approval obtained

### Stage 2 Checklist
- [ ] Technical team reviewed Feature Request
- [ ] 2-3 solution options evaluated
- [ ] Recommended approach selected with rationale
- [ ] Architecture impact analyzed
- [ ] Applicable ADRs identified
- [ ] RFC created (for significant changes)
- [ ] Technical risks identified
- [ ] Effort estimated
- [ ] PM + Dev approval obtained

### Stage 3 Checklist
- [ ] Detailed technical design complete
- [ ] Architecture diagram created (if applicable)
- [ ] Data model defined
- [ ] API contracts specified
- [ ] UI/UX specifications documented (Figma high-fidelity)
- [ ] **BDD scenarios written in Gherkin format**
- [ ] Code recommendations provided
- [ ] Files to create/modify listed
- [ ] ADR compliance documented
- [ ] Peer review complete
- [ ] PM + Dev approval obtained
- [ ] SPEC.md created in projects folder

---

## Next Step

Proceed to [07-quick-start.md](07-quick-start.md) for AI-directed development

---

*Part of [Spaarke Software Development Procedures](INDEX.md) | v2.0 | December 2025*
