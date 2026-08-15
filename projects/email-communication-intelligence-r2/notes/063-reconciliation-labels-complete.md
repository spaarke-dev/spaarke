# Task 063 — UAT round-2 label reconciliation (complete)

> **Completed**: 2026-08-11 · FULL rigor (TEST-MODIFYING override) · sonnet·high · Step 9.5 clean.

## What changed (2 display-string edits, shared lib)

| UX §E | File | Before | After |
|---|---|---|---|
| **E1a** | `EmailAssociationsAndTracking/EmailConnectionsReview.tsx:269` | tile label "Create new record" | **"New record"** (keeps the existing `DocumentAdd20Regular` add icon → "+ New task"-consistent) |
| **E2a** | `ReconcileTabs/FieldUpdateReconcileTab.tsx:440` | — | **"Accept"** — already shipped; **verified, no change** |
| **E3a** | `ReconcileTabs/TaskReconcileTab.tsx:606` | proposal primary "Accept" | **"Create"** |

## Key decisions

1. **E3a divergence from shipped state (owner-honored).** The UAT round-2 feedback referenced the *prototype* label ("Confirm & create" → "Create"), but the shipped 056 had already unified the Tasks proposal on **"Accept"** (matching Fields). The owner's explicit ask was "Create", so the Tasks proposal now reads **"Create"** while Fields stays **"Accept"** — intentional per-tab verb divergence (accept a field value vs. create a task). If consistency is later preferred over the explicit wording, this is the one line to revisit.
2. **testid + handler kept stable.** The Tasks button retains `data-testid="task-reconcile-accept"` and the `acceptProposal` handler even though the label is now "Create" — the action IS accept-and-create; the label emphasizes the create outcome. Keeping the testid/handler avoids test churn and preserves the 056 Accept-routing wiring (unchanged→034 apply / edited-identity→056b ad-hoc+dismiss). No test file needed changing (all assertions are testid-based).
3. **E1a stayed a label change, not a card redesign.** "+ New record" consistency = the add-icon + "New record" text (mirrors how "+ New task" renders — an `AddRegular` icon + "New task" text, no literal "+"). The tile already carried an add icon, so only the text changed; the card's icon-row layout was left intact (restructuring it into an inline-icon button would be a visual redesign, out of scope).

## Build / verify

- **tsc: 0 errors.** NOTE: before the `@spaarke/ui-components` `dist/` was rebuilt, Communication.Components tsc failed with 1 PRE-EXISTING error (`initialQuotedThread` not on `ISendEmailDialogProps`) — a **build-order artifact** from the master merge (master's quoted-thread feature updated `@spaarke/ui-components` *source* but this repo's local `dist/` was stale). `dist/` is gitignored; CI rebuilds in dependency order, so nothing broken ships. Confirmed the error pre-dated the 063 edits (stash-test at post-merge HEAD 13e2ba74e).
- **jest: 24/24** across TaskReconcileTab, EmailConnectionsReview, FieldUpdateReconcileTab.
- **Step 9.5**: code-review CLEAN, adr-check CLEAN (ADR-021/050/012 untouched; display strings only).

## Not in this task (gated follow-on)

E1b/E1c (Quick Start + `.eml` auto-load), E2b/E2c (typed controls + OOB advanced-lookup + Update-other-fields record modal), E3b (Assigned-to OOB advanced-lookup) = tasks **064/065/066**, GATED on prototype sign-off. Reuse-only per `pillar-e-mount-build-plan.md` §7.5.
