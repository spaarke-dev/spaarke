# Invoice Review screen — prototype brief

> For: the Spaarke prototype project (`c:\code_files\spaarke-prototype\projects\`). Source project: eBilling R1 (`projects/ebilling-r1/spec.md` FR-31 – FR-37). Date: 2026-10-14.
> Start from the existing prototype `projects/financial-billing-analysis-module/src/InvoiceReviewDetail.tsx`; follow `docs/standards/UI-DESIGN-STANDARDS.md`, Fluent v9, and the shared components listed at the end.

## 1. Purpose

An in-house reviewer (attorney or legal-ops) opens one outside-counsel invoice that the system has already read, coded and checked. They decide each line (Accept, Reduce, Disallow), optionally adjust the invoice as a whole, and then Approve, Reject or Return it to the firm. The screen must make it fast to clear clean lines and easy to see *why* a line is flagged, with the source PDF one click away.

## 2. Users and entry points

| User | Arrives from | Can do |
|---|---|---|
| Eligible approver (resolved from the core record's approver roles) | "Invoices to review" view, in-app notification, the invoice record | Everything below |
| Other internal user with read access | Invoice record | Read-only view; no actions shown |
| Billing admin | Same | Read-only plus correcting misread values (no approval unless eligible) |

## 3. Layout (desktop first; the screen is a code page, not the SPA)

1. **Header bar** — firm, firm invoice number + tracking number (`INV-002.2`), core record (Matter / Project / Work Assignment, with link), billing period, invoice date, status badge, eligible approvers ("Awaiting: J. Smith, A. Lee").
2. **Totals strip** — Billed · Line adjustments · Invoice adjustments · **Approved total** (recalculates live) · count of lines by status (Pending, Flagged, Clean, Accepted, Reduced, Disallowed).
3. **Lines grid (main area)** — one row per Fee / Expense / Adjustment line.
4. **Right pane (tabbed, resizable)**:
   - **Document** — the invoice PDF, scrolled to and highlighting the selected line's source region.
   - **Findings** — every finding for the selected line (and invoice-level findings when nothing is selected).
   - **Context** — core-record status, budget vs spend, prior invoices from this firm, this firm's disallow history, timekeeper rate history.
   - **Related work** — on demand: documents on the core record near the line date by the same timekeeper, ranked by similarity to the narrative.
5. **Action bar (sticky bottom)** — Accept all clean lines · Add invoice adjustment · Return to firm · Reject · **Approve**.

## 4. Data on each line (grid columns)

| Column | Notes |
|---|---|
| Status | Pending, Clean, Flagged, Accepted, Reduced, Disallowed — colour badge |
| Date | Line date |
| Type | Fee, Expense, Adjustment |
| Timekeeper | Name as billed + matched roster entry and classification (e.g. Partner); unmatched shown as a warning |
| Hours · Rate · Amount | As billed; approved rate shown beside the billed rate when different |
| Narrative | Truncated, expandable; AI observation markers inline (block billing, vague) |
| UTBMS task / activity / expense | AI code + confidence; firm-supplied code shown when present; mismatch marked |
| Findings | Count + highest severity; hover shows rule names |
| Suggested adjustment | From the rules (e.g. "Reduce to approved rate: −$120") |
| Approved amount | Editable when Reduce is chosen |
| Low-confidence marker | Any extracted value below the reliability threshold shows a small "check value" icon |

## 5. Behaviour

**Line decisions**
- Accept, Reduce (approved amount + reason, required), Disallow (reason, required). A rule's suggested adjustment can be applied in one click (still a reviewer decision).
- Multi-select rows → bulk Accept / Disallow with one reason.
- "Accept all clean lines" accepts every Clean line in one action.
- Every decision updates the totals strip immediately; the server is the source of truth for totals.

**Selecting a line**
- Document tab scrolls to the page and draws a box around the line's source region (the extraction returns page + bounding box per field). Clicking a field value in the grid highlights that exact value on the PDF.
- Findings tab shows: rule name, policy version, the clause text it came from (quoted, with a link to the guideline document), the values read, and the suggested adjustment.

**Correcting a misread value**
- Any extracted value (hours, rate, amount, date, timekeeper, code) can be corrected inline. A correction is audited (who, when, old → new), re-runs the affected rules, and visibly changes the finding list. Corrections are also the training signal for future extraction and coding — the UI should make "correct" distinct from "reduce" (a correction says the reading was wrong; a reduction says the bill is wrong).

**Invoice-level adjustment**
- Dialog: Percent of fees (after line adjustments) or Fixed amount, reason (required), optional rule. Appears as an Adjustment line. Cannot make the approved total negative.

**Approve / Reject / Return**
- Approve is disabled while any line is Pending or Flagged (tooltip says how many remain).
- Reject (final) and Return to firm each require a reason; both show a preview of the firm notification (AI-drafted wording, editable before sending).
- After any of the three, the screen becomes read-only and shows who decided and when.

**States to prototype**
- Loading; normal; all-clean invoice (one-click approve path); heavily flagged invoice (30+ findings); line with a low-confidence value; unmatched timekeeper; read-only (decided); non-eligible viewer; context source unavailable (panel shows a message, not an error); 150-line invoice (grid performance, virtual scroll).

## 6. Out of scope for the prototype

Firm-facing (SPA) invoice detail; multi-approver routing; multi-matter invoices; LEDES file view; accruals / PO / GL coding; mobile layout for this screen.

## 7. Existing pieces to reuse

- Layout: `WorkspaceShell`, `ThreePaneLayout`, `PanelSplitter`, `SidePane` (`src/client/shared/Spaarke.UI.Components/src/components/`).
- Grid: `DataGrid` / `DatasetGrid` framework (`docs/architecture/SPAARKE-DATAGRID-FRAMEWORK-ARCHITECTURE.md`).
- AI markers: `AiFieldTag`, `AiSummaryPopover`, `StatusBadge`.
- Dialogs: `SprkModal` (`docs/standards/MODAL-DECISION-CRITERIA.md`, `MODAL-DESIGN-SYSTEM.md`).
- Document: `FilePreview/RichFilePreview.tsx`, `DocumentViewerWidget`. **Gap:** no component today draws bounding-box highlights on a PDF page — the prototype should show the intended interaction so the build task can size it.

## 8. Questions for the prototype to answer

1. Document pane on the right vs. a split view under the grid — which keeps both line and source visible on a 1440 px screen?
2. How findings are shown in the grid without crowding: badge + side pane, or an expandable row?
3. Whether "correct value" and "reduce" can share one edit affordance without confusing reviewers.
4. How the 30+ findings case stays scannable (grouping by rule? by severity?).
