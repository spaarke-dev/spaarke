# AI Scope Configuration Guide

> **Audience**: AI Administrators, Business Analysts, Power Platform Administrators
> **Last Updated**: January 2026
> **Version**: 1.0

---

## Table of Contents

1. [Introduction](#introduction)
2. [Understanding the Scope System](#understanding-the-scope-system)
3. [Getting Started](#getting-started)
4. [Configuring Actions](#configuring-actions)
5. [Configuring Skills](#configuring-skills)
6. [Configuring Tools](#configuring-tools)
7. [Configuring Knowledge](#configuring-knowledge)
8. [Building Playbooks](#building-playbooks)
9. [Writing Effective System Prompts](#writing-effective-system-prompts)
10. [Testing and Iteration](#testing-and-iteration)
11. [Best Practices](#best-practices)
12. [Troubleshooting](#troubleshooting)

---

## Introduction

The Spaarke AI Document Intelligence system uses a **scope-based architecture** that allows administrators to customize how AI analyzes documents. This guide explains how to configure and refine these scopes to improve analysis quality for your organization.

### What You Can Configure

| Component | Purpose | Example |
|-----------|---------|---------|
| **Actions** | What the AI does | "Extract Entities", "Detect Risks" |
| **Skills** | How the AI approaches tasks | "Contract Analysis", "Executive Summary" |
| **Tools** | Capabilities the AI can use | "Entity Extractor", "Risk Detector" |
| **Knowledge** | Reference information | "Standard Contract Terms", "Risk Categories" |
| **Playbooks** | Pre-configured analysis workflows | "Quick Document Review", "Full Contract Analysis" |

### Benefits of Customization

- **Improved accuracy**: Tailor prompts to your document types and terminology
- **Consistency**: Ensure all analyses follow your organization's standards
- **Efficiency**: Create focused playbooks for common document types
- **Domain expertise**: Embed your organization's knowledge into the AI

---

## Understanding the Scope System

### How Scopes Work Together

```
┌─────────────────────────────────────────────────────────────┐
│                        PLAYBOOK                              │
│  "Full Contract Analysis"                                    │
│                                                              │
│  ┌──────────┐  ┌──────────┐  ┌──────────┐  ┌──────────┐    │
│  │  Skills  │  │ Actions  │  │  Tools   │  │Knowledge │    │
│  ├──────────┤  ├──────────┤  ├──────────┤  ├──────────┤    │
│  │Contract  │  │Extract   │  │Entity    │  │Standard  │    │
│  │Analysis  │  │Entities  │  │Extractor │  │Contract  │    │
│  │          │  │          │  │          │  │Terms     │    │
│  │Risk      │  │Detect    │  │Risk      │  │          │    │
│  │Assessment│  │Risks     │  │Detector  │  │Risk      │    │
│  │          │  │          │  │          │  │Categories│    │
│  └──────────┘  └──────────┘  └──────────┘  └──────────┘    │
└─────────────────────────────────────────────────────────────┘
```

When a user selects a playbook, the system:
1. Loads all linked scopes (skills, actions, tools, knowledge)
2. Assembles context from knowledge items
3. Executes actions using the defined tools
4. Applies skills to guide the analysis approach

### Scope Types Explained

| Type | Contains | Used For |
|------|----------|----------|
| **Action** | System prompt + instructions | Defining specific AI tasks |
| **Skill** | Behavioral guidelines | Shaping how AI approaches work |
| **Tool** | Function definitions | Giving AI specific capabilities |
| **Knowledge** | Reference content | Providing domain expertise |
| **Playbook** | Links to all scope types | Packaging scopes for users |

---

## Getting Started

### Accessing Scope Configuration

1. **Open Power Apps** at [make.powerapps.com](https://make.powerapps.com)
2. **Select your environment** (e.g., Spaarke Dev)
3. **Navigate to Tables** in the left sidebar
4. **Find the scope tables**:
   - `Analysis Action` (sprk_analysisaction)
   - `Analysis Skill` (sprk_analysisskill)
   - `Analysis Tool` (sprk_analysistool)
   - `Analysis Knowledge` (sprk_analysisknowledge)
   - `Analysis Playbook` (sprk_analysisplaybook)

### Understanding the Data Model

Each scope entity has these common fields:

| Field | Purpose |
|-------|---------|
| `sprk_name` | Display name shown to users |
| `sprk_description` | Longer explanation of purpose |
| `sprk_sortorder` | Controls display ordering |

Actions have an additional critical field:

| Field | Purpose |
|-------|---------|
| `sprk_systemprompt` | The AI instructions (most important!) |
| `sprk_actiontype` | Category lookup (Extraction, Analysis, etc.) |

---

## Configuring Actions

Actions define **what the AI does**. Each action contains a system prompt that instructs the AI how to perform a specific task.

### Viewing Existing Actions

1. Open the **Analysis Action** table
2. Review the existing actions:
   - ACT-001: Extract Entities
   - ACT-002: Analyze Clauses
   - ACT-003: Classify Document
   - ACT-004: Summarize Content
   - ACT-005: Detect Risks
   - ACT-006: Compare Clauses
   - ACT-007: Extract Dates
   - ACT-008: Calculate Values

### Editing an Action

1. Click on an action record to open it
2. Locate the **System Prompt** field
3. Edit the prompt text
4. Click **Save**

Changes take effect immediately for new analyses.

### Creating a New Action

1. Click **+ New row** in the Analysis Action table
2. Fill in required fields:
   - **Name**: Short, descriptive name (e.g., "Extract Signatures")
   - **Description**: Explain what this action does
   - **Action Type**: Select appropriate category
   - **Sort Order**: Number for display ordering
   - **System Prompt**: Full instructions for the AI

3. Save the record
4. Link the action to a playbook (see [Building Playbooks](#building-playbooks))

### Example: Creating a Custom Action

**Scenario**: Your organization needs to extract signature information from contracts.

```
Name: Extract Signatures
Description: Identify all signature blocks, signatories, and signing dates from the document.
Action Type: 01 - Extraction
Sort Order: 9

System Prompt:
You are a document signature extraction specialist. Analyze the provided document to identify all signature-related information.

## Extraction Targets

1. **Signature Blocks**
   - Location in document (page, section)
   - Signatory name
   - Title/Role
   - Organization represented

2. **Signing Information**
   - Date signed (or "undated" if missing)
   - Witness information (if applicable)
   - Notarization details (if applicable)

3. **Signature Status**
   - Signed / Unsigned / Partially signed
   - Electronic vs. wet signature indicators

## Output Format

Return a structured JSON object with:
- signatureBlocks: Array of identified signature blocks
- Each block has: signatory, title, organization, dateSigned, status, witnessInfo
- missingSignatures: List of expected but missing signatures

## Guidelines

- Note any signature blocks that appear incomplete
- Flag documents with missing expected signatures
- Identify corporate vs. individual signatories
- Note any power of attorney or authorized representative indicators
```

---

## Configuring Skills

Skills define **how the AI approaches tasks**. They provide behavioral guidance and methodology.

### Existing Skills

| ID | Name | Purpose |
|----|------|---------|
| SKL-001 | Contract Analysis | Comprehensive contract examination methodology |
| SKL-008 | Executive Summary | High-level overview generation |
| SKL-009 | Risk Assessment | Risk identification and categorization approach |
| SKL-010 | Clause Comparison | Methodology for comparing against standards |

### When to Create a New Skill

Create a new skill when you need the AI to:
- Follow a specific methodology consistently
- Apply domain expertise across multiple actions
- Maintain a particular analytical perspective

### Skill vs. Action

| Aspect | Skill | Action |
|--------|-------|--------|
| Focus | How to approach work | What specific task to perform |
| Scope | Broad methodology | Narrow, specific task |
| Reuse | Used across many playbooks | May be playbook-specific |
| Example | "Contract Analysis" approach | "Extract Payment Terms" task |

---

## Configuring Tools

Tools define **capabilities the AI can use**. They represent specific functions or utilities.

### Existing Tools

| ID | Name | Purpose |
|----|------|---------|
| TL-001 | Entity Extractor | Extract parties, dates, amounts |
| TL-002 | Clause Analyzer | Identify and analyze clauses |
| TL-003 | Document Classifier | Classify document type |
| TL-004 | Summary Generator | Generate concise summaries |
| TL-005 | Risk Detector | Identify potential risks |
| TL-006 | Clause Comparator | Compare to standard terms |
| TL-007 | Date Extractor | Extract and normalize dates |
| TL-008 | Financial Calculator | Calculate monetary values |

### Creating Custom Tools

Most organizations use the standard tools. Create custom tools when you need:
- Integration with external systems
- Custom calculation logic
- Specialized extraction patterns

---

## Configuring Knowledge

Knowledge items provide **reference information** the AI can use during analysis.

### Existing Knowledge

| ID | Name | Content |
|----|------|---------|
| KNW-001 | Standard Contract Terms | Industry-standard contract clauses |
| KNW-003 | Best Practices | Contract review best practices |
| KNW-004 | Risk Categories | Risk taxonomy for assessment |
| KNW-005 | Defined Terms | Standard legal definitions |

### Adding Organization-Specific Knowledge

**Scenario**: Your organization has specific contract standards.

1. Create a new Knowledge record
2. Populate with your organization's:
   - Standard clause language
   - Acceptable term ranges
   - Required provisions
   - Prohibited terms

**Example Knowledge Content**:

```
Name: ACME Corp Contract Standards
Description: Organization-specific contract requirements and standards

Content:
## Required Provisions

All contracts MUST include:
1. 30-day payment terms (NET 30)
2. Liability cap of 2x annual contract value
3. 90-day termination notice period
4. Governing law: State of Delaware

## Prohibited Terms

Contracts MUST NOT include:
1. Unlimited liability clauses
2. Auto-renewal periods exceeding 1 year
3. Non-mutual indemnification
4. Exclusive venue outside United States

## Acceptable Ranges

| Term | Minimum | Maximum | Preferred |
|------|---------|---------|-----------|
| Payment terms | NET 15 | NET 45 | NET 30 |
| Liability cap | 1x ACV | 3x ACV | 2x ACV |
| Warranty period | 30 days | 1 year | 90 days |
```

---

## Building Playbooks

Playbooks bundle scopes together into **ready-to-use analysis workflows**.

### Existing Playbooks

| ID | Name | Complexity | Use Case |
|----|------|------------|----------|
| PB-001 | Quick Document Review | Low | Initial document triage |
| PB-002 | Full Contract Analysis | High | Comprehensive contract review |
| PB-010 | Risk Scan | Low | Fast risk identification |

### Creating a Custom Playbook

1. **Plan your playbook**:
   - What document types will it analyze?
   - What information should it extract?
   - What risks should it identify?

2. **Create the playbook record**:
   - Navigate to Analysis Playbook table
   - Click **+ New row**
   - Fill in Name, Description
   - Set `sprk_ispublic` = Yes (to make it available to users)

3. **Link scopes to the playbook**:
   - Open the playbook record
   - Navigate to related records (N:N relationships)
   - Add: Skills, Actions, Tools, Knowledge items

### Example: NDA Review Playbook

**Purpose**: Specialized analysis for Non-Disclosure Agreements

**Configuration**:

| Scope Type | Items to Link |
|------------|---------------|
| Skills | Contract Analysis, Risk Assessment |
| Actions | Extract Entities, Classify Document, Detect Risks, Summarize Content |
| Tools | Entity Extractor, Document Classifier, Risk Detector, Summary Generator |
| Knowledge | Standard Contract Terms, Risk Categories, NDA Best Practices (custom) |

**Custom Knowledge for NDA**:

Create a new Knowledge record "NDA Best Practices" containing:
- Standard NDA provisions
- Acceptable confidentiality periods (1-5 years typical)
- Mutual vs. one-way NDA considerations
- Carve-outs that should be present
- Red flags specific to NDAs

---

## Writing Effective System Prompts

System prompts are the most important configuration element. They directly control AI behavior.

### Prompt Structure Template

```
You are a [role description]. [Primary task description].

## [Category 1]

1. **[Subcategory]**
   - [Specific instruction]
   - [Specific instruction]

2. **[Subcategory]**
   - [Specific instruction]

## Output Format

[Describe exactly what the output should look like]

For each [item type], provide:
- [field]: [description]
- [field]: [description]

## Guidelines

- [Behavioral instruction]
- [Quality standard]
- [Edge case handling]
```

### Prompt Writing Best Practices

| Practice | Example |
|----------|---------|
| **Be specific** | "Extract all dates in ISO 8601 format" not "Find dates" |
| **Define output format** | Specify JSON structure, field names, data types |
| **Set quality standards** | "Include confidence scores from 0-1" |
| **Handle edge cases** | "If no dates found, return empty array" |
| **Use categories** | Organize instructions with clear headers |
| **Provide examples** | Show sample inputs and expected outputs |

### Common Prompt Improvements

**Before** (vague):
```
Find risks in the document.
```

**After** (specific):
```
You are a document risk detection specialist. Analyze the provided document to identify potential risks.

## Risk Categories

1. **Legal Risks**
   - Unfavorable dispute resolution terms
   - Broad indemnification obligations
   - Unlimited liability exposure

2. **Financial Risks**
   - Unfavorable payment terms
   - Hidden fees or penalties
   - Auto-renewal traps

## Output Format

For each identified risk:
- category: Risk category (Legal/Financial/Operational/Compliance)
- description: Clear description of the risk
- severity: critical/high/medium/low
- clause: Reference to the relevant clause
- mitigation: Suggested mitigation or negotiation point

## Guidelines

- Prioritize risks by severity and likelihood
- Provide specific clause references
- Suggest practical mitigation strategies
```

---

## Testing and Iteration

### Testing Workflow

1. **Edit scope configuration** (action, prompt, etc.)
2. **Run analysis** on a test document
3. **Review results** for accuracy and completeness
4. **Refine configuration** based on results
5. **Repeat** until satisfied

### Test Document Selection

Choose test documents that:
- Represent your typical document types
- Include edge cases (unusual formatting, missing sections)
- Have known correct answers (for validation)
- Cover different complexity levels

### Tracking Improvements

Maintain a log of changes:

| Date | Scope | Change | Result |
|------|-------|--------|--------|
| 2026-01-05 | ACT-005 | Added financial risk subcategories | +15% risk detection |
| 2026-01-06 | KNW-001 | Added ACME-specific terms | Better deviation detection |

### A/B Testing Approach

1. **Create a variant** - Copy existing action with modified prompt
2. **Create test playbook** - Link variant action
3. **Run parallel analyses** - Same document, both playbooks
4. **Compare results** - Which produces better output?
5. **Promote winner** - Update main action with improved prompt

---

## Best Practices

### Organizational Standards

- **Document your customizations**: Keep a changelog of scope modifications
- **Version control prompts**: Store prompt text in your documentation system
- **Review periodically**: Schedule quarterly reviews of scope effectiveness
- **Gather feedback**: Collect user feedback on analysis quality

### Prompt Maintenance

- **Start simple**: Begin with basic prompts, add complexity as needed
- **Test incrementally**: Make one change at a time
- **Preserve history**: Keep previous prompt versions for rollback
- **Share learnings**: Document what works for different document types

### Security Considerations

- **No sensitive data in prompts**: Don't embed confidential information
- **Access control**: Limit who can edit scope configurations
- **Audit changes**: Track who modified what and when

---

## Troubleshooting

### Common Issues

| Issue | Possible Cause | Solution |
|-------|---------------|----------|
| Analysis returns empty results | Prompt too restrictive | Broaden extraction criteria |
| Low confidence scores | Vague instructions | Add specific examples to prompt |
| Missing expected fields | Output format not specified | Define exact JSON structure |
| Inconsistent results | Ambiguous instructions | Add "Guidelines" section with rules |
| Wrong document classification | Limited classifier training | Add more document type examples |

### Debugging Prompts

1. **Check prompt length**: Very long prompts may lose focus
2. **Verify field names**: Ensure output format matches what code expects
3. **Test in isolation**: Run single action before full playbook
4. **Review examples**: Ensure examples in prompt are accurate

### Getting Help

- **Technical issues**: Contact Spaarke support
- **Prompt optimization**: Review AI documentation and examples
- **Custom development**: Engage professional services for complex needs

---

## Appendix: Quick Reference

### Entity Table Names

| Display Name | Technical Name |
|--------------|----------------|
| Analysis Action | sprk_analysisaction |
| Analysis Skill | sprk_analysisskill |
| Analysis Tool | sprk_analysistool |
| Analysis Knowledge | sprk_analysisknowledge |
| Analysis Playbook | sprk_analysisplaybook |

### Key Fields

| Entity | Critical Field | Purpose |
|--------|---------------|---------|
| Action | sprk_systemprompt | AI instructions |
| Playbook | sprk_ispublic | User visibility |
| All | sprk_sortorder | Display ordering |

### Playbook-Scope Relationships (N:N)

| Relationship | Purpose |
|--------------|---------|
| sprk_playbook_action | Links actions to playbooks |
| sprk_playbook_skill | Links skills to playbooks |
| sprk_playbook_tool | Links tools to playbooks |
| sprk_playbook_knowledge | Links knowledge to playbooks |

---

*For technical implementation details, see the [AI Architecture Guide](../guides/SPAARKE-AI-ARCHITECTURE.md) and [Playbook Scope Design](../architecture/AI-ANALYSIS-PLAYBOOK-SCOPE-DESIGN.md).*
