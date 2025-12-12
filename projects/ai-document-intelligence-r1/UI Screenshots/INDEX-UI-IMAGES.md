# Document Intelligence UI Specification

The following screenshots are provided as design references for the Document Intelligence UI. These mockups illustrate functional intent; final implementation must conform to existing Spaarke design conventions and fully align with Microsoft Fluent UI v9 components, styles, and interaction patterns.

If any clarification is needed regarding behaviors, states, or component selection, request additional detail.

---

## 01 — Analysis Builder Modal
**Entry Point:** Document → Analysis tab → “+ Add Analysis”  
**Purpose:**  
Initiates a new AI-driven Analysis. User selects Action Type, Scope, Document/Matter context, and any required parameters.

**Key Elements:**  
- Fluent v9 Modal dialog  
- Form fields: Action Type, Scope, Description, Target Document  
- Primary Action: “Run Analysis”  
- Secondary: Cancel  

---

## 02 — Document Analysis Output Form
**Entry Point:** Opens from the Analysis subgrid list after execution.  
**Purpose:**  
Displays the AI-generated output in a structured format, with options to refine, edit, or continue analysis.

**Key Elements:**  
- Two-column form (66/34 layout)  
- Output viewer (left) using Fluent v9 rich text / viewer components  
- Metadata, status, timestamps (right)  
- Toolbar: Save to Document, Save to Project, Save to Matter  

---

## 03 — Save to Document Modal
**Entry Point:** Toolbar button on Analysis Output Form  
**Purpose:**  
Saves the AI-generated content into the related Document record.

**Key Elements:**  
- Fluent v9 Modal  
- Dropdown for Document selection (if multiple)  
- Fields: Title, Summary, Insert Location  
- Primary Action: “Save”  

---

## 04 — Save to Project Modal
**Entry Point:** Toolbar button on Analysis Output Form  
**Purpose:**  
Saves output into a Project entity as project notes, insights, or attachments.

**Key Elements:**  
- Project selector  
- Fields: Note Type, Title, Related Work Item  
- Primary Action: “Save to Project”  

---

## 05 — Save to Matter Modal
**Entry Point:** Toolbar button on Analysis Output Form  
**Purpose:**  
Attaches output to a Matter as a structured analysis artifact.

**Key Elements:**  
- Matter selector  
- Fields: Category, Summary, Optional File Association  
- Primary Action: “Save to Matter”  

---

## 06 — Document Quick View Modal
**Entry Point:** From the Document tab → “Quick View”  
**Purpose:**  
A modal file preview facilitating quick access to versions, navigation, and basic actions.

**Key Elements:**  
- Embedded File Viewer (PCF control)  
- Navigation: Back/Forward  
- Metadata summary panel  
- Version selection (optional)  

---

## Implementation Notes

- All UI must use Fluent v9 components, spacing, typography, and interaction standards.  
- Modal behaviors must follow Model-Driven App constraints while using Pro-Code (React/Fluent) controls where needed inside PCFs.  
- PCF controls should be designed for reuse across Document Intelligence, SDAP, and broader Spaarke UI modules.  
- Ensure accessibility standards (keyboard navigation, ARIA roles, contrast).  
