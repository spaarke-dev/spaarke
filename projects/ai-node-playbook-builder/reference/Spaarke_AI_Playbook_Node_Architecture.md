# Spaarke AI Playbook Builder  
## Node-Based Architecture for Legal Document Intelligence

---

## 1. Purpose and Scope

This document defines a **node-based visual architecture** for assembling AI Playbooks in the Spaarke AI Document Intelligence module.  
It formalizes how **Playbooks**, **Action-Nodes**, and **Scope Components** (Skills, Tools, Knowledge) work together to produce accurate, auditable legal analysis.

The objectives are to:

- Make AI analysis workflows understandable and governable
- Provide a clear mental model for advanced AI configuration
- Support repeatable, explainable, enterprise-grade outcomes
- Separate AI orchestration from end-user legal workflows

This document is intended for **AI / Legal Operations Analysts**, not attorneys or general end users.

---

## 2. Core Architecture Overview

### 2.1 Playbook

A **Playbook** represents the *overall analytical intent*.

Examples:
- Analyze Contract
- Review Software License Agreement
- Regulatory Inquiry Assessment

Characteristics:
- Defines the desired outcome
- Orchestrates a sequence of Actions
- Does not execute logic itself

The Playbook is a **workflow container**, not a computational step.

---

### 2.2 Action-Nodes

Each node in the visual graph represents an **Action**.

An Action is:
- A single, atomic unit of AI work
- Executed in sequence or dependency order
- Responsible for producing a structured output

Examples:
- Extract Entities
- Analyze Clauses
- Detect Risks
- Compare to Standards
- Generate Summary
- Create Tasks

Some Actions are intermediate; others are terminal. Technically, all are Actions.

---

### 2.3 Data Flow

Edges between nodes represent **data flow**.

- Output from one Action becomes input to the next
- Downstream Actions operate on increasingly structured data

This design ensures:
- Determinism
- Explainability
- Debuggability

---

## 3. Scope Components

Each Action-Node is configured by attaching three scope components:

| Scope Component | Purpose | Role |
|----------------|--------|------|
| Skills | How the AI reasons | Methodology |
| Tools | How the Action executes | Capability |
| Knowledge | What the AI may reference | Evidence |

These components are **properties of an Action-Node**, not standalone nodes.

---

## 4. Skills

### 4.1 Definition

A **Skill** defines *how the AI should think* while executing an Action.

Skills provide:
- Domain heuristics
- Prioritization logic
- Risk and severity rubrics
- Evidence and non-inference rules

Examples:
- Software License Risk Heuristics
- Contract Interpretation Rules
- Risk Severity Calibration

### 4.2 Constraints

Skills:
- Do not produce outputs
- Do not execute independently
- Do not define schemas

They influence reasoning, not execution.

### 4.3 Node Attachment

Skills attach directly to Action-Nodes to allow:
- Fine-grained control
- Reuse across Playbooks
- Consistent reasoning across runs

---

## 5. Tools

### 5.1 Definition

A **Tool** is the execution mechanism for an Action.

Tool types include:
- Deterministic functions (OCR, parsing)
- Retrieval functions (vector search, metadata lookup)
- LLM-backed functions (classification, comparison, summarization)
- Platform functions (task creation, persistence)

Each Action-Node:
- Uses exactly one primary Tool
- Configures that Tool with parameters

### 5.2 Design Rule

Tools are not nodes.  
They are implementation details bound to Actions.

---

## 6. Knowledge

### 6.1 Definition

**Knowledge** represents authoritative reference material the AI may rely on.

Examples:
- Standard Software License Positions
- Risk Taxonomy
- Security and Privacy Requirements
- Internal Policies

Knowledge may be implemented as:
- RAG indexes
- Document references
- Structured standards tables

### 6.2 Usage

Knowledge:
- Is retrieved at runtime
- Is injected as context
- Does not execute
- Does not define behavior

---

## 7. Example Playbook: Analyze Software License Agreement

### Logical Flow

```
Playbook: Analyze Software License Agreement
-------------------------------------------

[Extract Entities]
  - Tool: EntityExtractor
  - Skills: Legal Entity Precision
  - Output: Parties, Dates, Fees, Caps

        ↓

[Analyze Clauses]
  - Tool: ClauseAnalyzer
  - Skills: Contract Structure Recognition
  - Output: Clause Map

        ↓

[Detect Risks]
  - Tool: RiskDetector
  - Skills: Software License Risk Heuristics
  - Knowledge: Risk Taxonomy
  - Output: Risk Register

        ↓

[Compare to Standards]
  - Tool: ClauseComparator
  - Knowledge: Standard Software License Positions
  - Output: Deviations

        ↓

[Generate Executive Summary]
  - Tool: SummaryGenerator
  - Output: Structured Narrative Summary
```

This representation makes explicit:
- Order of operations
- Where judgment is applied
- Where standards are referenced
- How the final output is assembled

---

## 8. Intended User

### 8.1 Primary User

The intended user is a **Business AI Analyst / Legal Operations Analyst**.

Responsibilities include:
- Designing AI analysis workflows
- Encoding organizational standards
- Tuning accuracy and consistency
- Governing AI behavior and risk
- Explaining AI outputs internally

This role sits at the intersection of:
- Legal operations
- Knowledge management
- AI configuration
- Process design

---

### 8.2 Non-Target Users

This tool is not intended for:
- Attorneys performing legal review
- End users consuming AI outputs
- Casual system administrators

Attorneys interact with **outputs**, not AI orchestration.

---

## 9. Design Principles

To preserve coherence and scalability:

1. Playbooks orchestrate; Actions execute
2. Nodes represent Actions only
3. Skills, Tools, and Knowledge attach to nodes
4. Data flows only between Actions
5. Favor visual clarity over technical exposure
6. Enforce strong typing and validation

---

## 10. Next Steps

Logical follow-on work includes:
- Canonical Action-Node schema
- Playbook Graph data model
- Visual editor UX design
- Runtime graph serialization format
- Debug and explainability views

---

**Summary**

This node-based Playbook architecture aligns Spaarke with how advanced AI systems are actually built, while making them understandable, governable, and defensible for enterprise legal use.
