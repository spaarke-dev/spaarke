# email-communication-solution-r4 — UAT Round 2 Feedback (owner, 2026-07-20)

> Captured after the W9 remediation shipped + deployed. Two W9 fixes **verified working in prod**:
> - **#7** — body-reference matching → Ambiguous (screenshot: PAT-942665 matched "by reference number in the email body" @97% alongside the subject matter). ✅
> - **#2** — CommunicationAttachments PCF imports (XSD fixed), lists attachments, launches the preview modal. ✅
>
> The items below are a new wave (**W10**) of refinements + enhancements.

---

## Group A — Attachment PCF (#2) polish

| ID | Item | Type | Notes |
|----|------|------|-------|
| **A1** | Preview modal is missing the **prev/next browse** ("1 of N") across the attachment list (owner saw "2 of 25" on another modal and expects it here) | refinement | `RichFilePreviewDialog` supports it — pass `navigationTotal` / `currentIndex` / `onNavigate`; PCF maintains the index over the filtered attachment list. |
| **A2** | Header + row **styling** to match system: title = **14px Segoe, weight 600, color #242424**; **row height 20**; **top pad 4 / bottom pad 4** | refinement + docs | Maps to Fluent v9 tokens (ADR-021): `fontSizeBase300` (14px) / `fontWeightSemibold` (600) / `colorNeutralForeground1` (#242424 light). **Codify in a design-standards doc** so all Spaarke section headers/rows are consistent. |

## Group B — Association Engine enhancement

| ID | Item | Type | Notes |
|----|------|------|-------|
| **B1** | Email body named two contacts already in the system ("Working on this file will be **Eyal Iffergan and Sara Chen**") — they were **NOT matched**. Only sender/recipient participants match as contacts today (rung 2). | enhancement | Add **contact-name matching** in subject/body (analogous to RecordNameMatch for records, but for `contact`). **Decision needed**: Suggest-only (recommended — same "surface for review, never auto-file" posture as record-name matches) vs auto-file. Precision guards needed (a common first/last name is a weak signal). |

**Working-as-intended in the same screenshot (no change):** subject `CMRCL-848992 Smith v Smith` matched a matter (by number) AND a project (by name "Smith v Smith") → both surfaced; body `PAT-942665` matched a second matter → Ambiguous; contact Ralph Schroeder (sender) filed. The ladder is behaving correctly.

## Group C — Connections PCF (#042) UX

| ID | Item | Type | Notes |
|----|------|------|-------|
| **C1** | "Improve the Connections modal UI/UX — I'm still not clear on how I'm supposed to **resolve**, or what the statuses **mean**." | UX redesign | Clarify the resolve workflow (Ambiguous "choose one" → pick primary; what Primary / Filed / Suggested / Ambiguous each mean and what action each needs). Needs a small design pass, not just code. |

## Group D — Email Compose (#020/021)

| ID | Item | Type | Notes |
|----|------|------|-------|
| **D0** | "Is this modal the standard Email compose form?" | question | **Answer**: it's Spaarke's own `EmailComposer` (proprietary Fluent v9, built W2 tasks 020/021) — the canonical Spaarke compose surface across all send-paths, NOT the OOB Dynamics email activity form. |
| **D1** | **Reply / Reply All** need an option to include the attachment **documents as links and/or attach the files** | enhancement | |
| **D2** | **Forward** needs the attachment **document links and/or attached files** | enhancement | |
| **D3** | Address fields (To/CC/BCC) **do not look up the contact table** (no autocomplete against contacts) | enhancement/bug | The composer has an `onSearchRecipients` seam (added task 060) — likely not wired to a contact search in this host. |

---

## Triage summary

- **Quick refinements**: A1 (nav wiring), A2 (styling + design-standards doc).
- **Engine enhancement (needs decision)**: B1 contact-name matching (Suggest-only recommended).
- **UX redesign (needs design pass)**: C1 Connections modal.
- **Composer enhancements**: D1/D2 (attachment include on reply/forward), D3 (contact lookup on address fields). D0 answered.

Proposed as **Wave 10**. Sequencing recommendation: A1+A2 first (fast, visible), then D3 + D1/D2 (composer), then B1 (engine, needs decision), then C1 (UX, needs design). Each at FULL rigor via task-execute, same as W9.
