# Pattern Spec: Form / Wizard-centered

> **Status**: Draft v0.1
> **Pattern slug**: form-wizard
> **Closest analogous product:** Standard form wizards (Salesforce Lightning record creation, Dynamics quick-create forms, Stripe / Typeform multi-step forms)
> **Primary user intent:** *"Walk me through creating this correctly."*

---

## 1. Optimizes For

The Form / Wizard pattern optimizes for **guided structured input** — collecting a defined set of data with validation, defaults, and explanation. The user is creating something new (a matter, a counterparty, an intake request) or making a substantial defined update, and the system's job is to make sure the result is complete and correct.

Forms / Wizards are the easiest pattern to default to and the most dangerous to overuse. They are appropriate when guidance has real value: many fields, validation that matters, decisions that benefit from being walked through. They are inappropriate when they replace direct manipulation — a wizard to change one field is friction, not guidance.

---

## 2. When To Use / When Not To Use

### 2.1 Use this pattern when

- Creating a new structured record with multiple required fields and validation.
- The fields have meaningful interdependencies ("if this, then that field is required").
- The user benefits from being walked through the work rather than seeing all fields at once.
- The form's output drives downstream behavior that depends on its correctness (a matter intake form drives matter setup, conflicts checks, etc.).
- A long form benefits from being broken into stages with progress visible.

### 2.2 Do not use this pattern when

- The user is editing one field on an existing record (Entity inline edit).
- The data is captured better as free text or in a conversation (Generative).
- The form is so short (1–3 fields) that walking the user through it is more friction than benefit.
- The work is fundamentally not data entry — it's deciding, reviewing, or analyzing.

### 2.3 Pattern overlaps

- **Workflow / Process-centered**: Workflows often *contain* forms at their stages. A new matter intake workflow has a form at stage 1. The form is a Form pattern instance; the surrounding stage progression is Workflow.
- **Entity-centered**: Entity views may host inline forms for editing sections, or open a Form when adding a related record. The Entity is the persistent record; the Form is a transaction against it.
- **Generative / Conversational**: Increasingly, structured input can be captured conversationally. A user describes a matter in chat; AI extracts fields and presents them in a form for confirmation. The form is the verification surface, not the entry surface.

---

## 3. Mechanics

### 3.1 Core mechanics

1. **Stepped progression with visible structure.** The user sees the steps, where they are, and how many remain. The convention is well-established and undisputed (Salesforce Lightning, Stripe Checkout, Typeform, Dynamics quick-create).

2. **Per-step focused input.** Each step has a manageable number of fields (typically 3–8) with enough room to think. Cramming all fields into one screen defeats the wizard's purpose; spreading them across 20 micro-steps does too.

3. **Inline validation.** Errors appear next to the field, on blur or on submit, with clear language. The user knows what's wrong and how to fix it without consulting a separate panel.

4. **Required-field indication.** Consistent visual treatment for required fields, with required-and-missing surfaced clearly when the user tries to advance.

5. **Sensible defaults.** Where the system can infer a value (a matter's currency from the customer's tenant, a counterparty's region from prior records), it provides a default that the user can override. Defaults that the user has to undo are worse than no defaults; defaults that fit are silent value.

6. **Back / forward navigation that preserves work.** Going back doesn't lose what's been entered; going forward doesn't lock prior steps.

7. **Review-before-submit step.** A final summary of all entered data before commit. For consequential forms (matter creation, contract drafts) this is mandatory; for trivial ones it can be skipped.

8. **Clear submission state.** "Submitting…" "Submitted successfully." "Submission failed — here's why." The user always knows.

### 3.2 Supporting mechanics

1. **Save and resume.** For long forms, the user can save partial progress and return later. Critical for legal work where intake forms may need lookups or approvals.

2. **Conditional logic.** Fields appear or disappear based on prior answers. "If matter type = litigation, show jurisdiction field." Implemented carefully — too much conditional logic makes the form feel unpredictable.

3. **Help text and field explanations.** For legal-specific fields where the meaning isn't self-evident, inline help (not just tooltips) is appropriate.

4. **Repeat groups / dynamic field sets.** For forms that capture variable-length data ("list all related parties"), the user can add or remove rows.

### 3.3 AI augmentation mechanics

This is where Form / Wizard has changed substantially in the last 18 months.

1. **AI pre-fill from context.** The user is creating a matter intake; the AI fills in fields it can infer from the originating email, the document attached, or prior similar matters. Pre-filled fields are *visually distinct* from user-entered — the user must confirm or override before the form submits.

2. **AI extraction from unstructured input.** A user pastes a counterparty's NDA cover email; the AI extracts counterparty name, type, jurisdiction, and proposed scope, populating a counterparty record form. The form is the verification surface, not the manual entry surface.

3. **Conversational form completion.** Instead of clicking through a form, the user describes the request in chat: "set up a new matter for Acme contract review, contract value about $500K, urgency normal, legal lead is [me]." The AI produces a populated form for review; the user confirms and submits. The conversation is the input mode; the form is the structured output.

4. **AI-suggested defaults and corrections.** Inline suggestions during form completion. "Did you mean Acme Corp or Acme Inc? They're different counterparties." "This matter value is unusual — typical for this customer is in the $50K-$200K range." Corrections are suggestions, not blocks.

5. **AI validation reasoning.** When a field fails validation, the AI can explain why and propose a fix rather than just flagging the error.

---

## 4. Expectations to Honor (Closest Analogous Product)

Form / Wizard has the densest, longest-standing UX research base of any pattern in this set. Form design conventions are well-established and disagreement is mostly at the margins.

### 4.1 Expectations across all form conventions

- **Labels above fields**, left-aligned, in sentence case. Not all-caps, not below, not floating-only.
- **Tab order matches visual order.** Keyboard users expect Tab to move down/right, not jump around.
- **Field width hints at expected input.** A short zip-code field signals 5–10 characters; a long description field signals more. Inconsistent widths break this signal.
- **Error messages adjacent to fields**, not in a single banner at the top of the form.
- **Submit button bottom-right (Western reading order)**, with primary visual weight.
- **Cancel / back affordance** clearly distinguishable from submit.
- **Form fields use standard controls** (text input, select, checkbox, radio, date picker) consistently — Fluent UI v9 standard controls.
- **Tab to a select field allows keyboard selection** without forcing a mouse click.

### 4.2 Where Spaarke legitimately diverges

| Spaarke divergence | Why |
|---|---|
| AI pre-fill and AI-suggested defaults appear by default | Legal intake forms have high cognitive cost; AI pre-fill from context (email, attached document) materially reduces it |
| Conversational form completion is a first-class alternative entry mode | This is the three-pane shell's value proposition for structured input; the chat is in the Conversation pane, the form is in Workspace |
| AI extraction from unstructured input (paste an email, get a populated form) is standard | Legal users frequently work from forwarded emails / requests; "fill in the form from this email" is genuinely high-leverage |
| Review-before-submit shows AI-suggested vs user-entered distinction | Legal accountability: the user must know what they confirmed vs what they entered fresh |

---

## 5. Common Failure Modes

| Failure mode | Symptom | Fix |
|---|---|---|
| **Form chosen when Form isn't needed.** A 1-field "update status" wizard. | Friction without guidance value. | Reserve Form / Wizard for genuinely multi-field, validation-rich work. Use inline edit (Entity pattern) for simple updates. |
| **Hidden required fields.** User clicks submit, gets "fix the errors" with errors on a prior step the user can no longer see. | Frustration and lost work. | Make required fields visible at each step; validate per step on advance, not just on submit; if validation must check across steps, name the cross-step dependency. |
| **AI pre-fill that looks identical to user input.** | User submits without verifying pre-filled fields; later discovers the AI was wrong. | Distinct visual treatment for AI-suggested-not-yet-confirmed values. Confirmation can be one click but must happen. |
| **Defaults that don't fit.** Pre-filled values the user always has to change. | Worse than no defaults; users start treating the form as adversarial. | Default only when the system has real signal; otherwise leave empty. |
| **Too many micro-steps.** A 4-field form spread across 4 single-field screens. | Click fatigue; users feel patronized. | Each step should have enough work to justify its existence. Three to eight fields is the typical band. |
| **No save-and-resume on long forms.** User gets pulled away mid-form; loses everything. | Users avoid the form, or always complete it in one sitting under time pressure. | Save partial progress automatically; let user return where they left. |
| **Conditional logic that surprises.** Fields appear and disappear unpredictably as the user enters values. | Disorientation; users feel the form is unstable. | Conditional logic should be predictable and minimal. When a field appears, it should be obvious why. |

---

## 6. Spaarke Component Mapping

### 6.1 Current design positions

| Pattern element | Current Spaarke component |
|---|---|
| Form / Wizard widget | **WizardDialog** (R1) — exists and serves the pattern. R2 includes embedded wizard support in Workspace (spec.md FR-206). |
| Pane assignment | Workspace pane when invoked as primary work; embedded within Workflow when part of a stage; possibly dialog-style for quick adds from elsewhere. |
| AI pre-fill | Not currently specified as a standard form behavior. |
| Conversational form completion | The Conversation → Form transition (chat input produces a populated form for review) isn't fully specified. |
| AI extraction from unstructured input | Not currently specified. |

### 6.2 Design-challenge findings

| Finding | Current design | Pattern requires |
|---|---|---|
| **AI pre-fill is not a default Form behavior.** | WizardDialog handles structured input; AI augmentation is not specified at the form level. | Define the AI pre-fill convention: where the AI gets context, how pre-filled values are visually distinguished, how confirmation works. Belongs in AI Augmentation cross-cutting and applied here. |
| **Conversational form completion (chat → form) isn't a defined transition.** | Forms are entered through their own UI; conversational entry isn't a defined path. | A Conversation → Form transition where chat input produces a populated form for review. Belongs in Composition Guide. Architecturally simpler than it sounds: chat output is a structured tool call to "populate form X with these fields"; Workspace renders the populated form. |
| **AI extraction from pasted unstructured input isn't a defined component.** | Users enter forms field-by-field. | A "paste and extract" affordance on forms where appropriate — paste an email, populate fields. Engineering call on whether this is a form-level affordance or a separate "paste-to-form" widget. |
| **Visual distinction for AI-suggested fields requires the cross-cutting convention.** | Not specified. | The same AI-vs-user visual distinction needed in Canvas applies here. Belongs in AI Augmentation cross-cutting. |

### 6.3 New components / events / widgets proposed

- **AI pre-fill convention** for forms — placement, visual treatment, confirmation mechanics. Cross-cutting.
- **Conversation → Form transition** — chat output populates a Workspace form for review and confirmation. Composition Guide.
- **Paste-and-extract affordance** — paste unstructured text, get structured form output. Engineering call on form factor.

---

## 7. Open Questions

| Question | Why it matters | Validation |
|---|---|---|
| When AI pre-fills a form, do the pre-filled values become user-confirmed automatically on submit, or do they require explicit per-field confirmation? | Trust and accountability. Auto-confirm is faster; explicit confirm is auditable. | Designer's call by form type; legal-consequential forms probably require explicit per-field confirm |
| How does conversational form completion handle ambiguity? If the user says "matter for Acme contract review" and there are two Acme entities, does the chat ask back or does the form surface the ambiguity? | UX flow and where the resolution happens. | Prototype testing |
| Should embedded wizards inside Workflow look identical to standalone wizards, or have visible workflow context? | Consistency vs. context. Identical is simpler; context-aware reduces orientation cost. | Designer's call + prototype testing |
| For very long intake forms (some legal-departmental intake forms have 30+ fields), is the right answer to break into more sub-forms, or to allow conversational entry as the primary mode? | This decision shapes how the longest, most consequential forms work. | Use case inventory will tell us how many forms in the system have this scale; designer's call afterward |

---

*Draft v0.1 — 2026-05-18.*
