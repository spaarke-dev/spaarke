# Foundation Decision — Workspace-Shell (2026-08-06)

## DECISION: adopt the SpaarkeAi-style **workspace shell** as R2's foundation (replaces the card-launcher)

Owner-approved after prototyping both. Rationale: best **balance of AI chat-directed interaction +
deterministic widget data access**; assistant-central (where the product is heading); **unifies the
platform model** (internal SpaarkeAi + external portal both = workspace + assistant + widgets, so every
new capability = register a widget); better for live-in personas (outside counsel). Feasibility
confirmed (`notes/review-additions-analysis.md` + workspace-shell investigation): the shell **chassis**
(widget registry + tab host + pane layout + assistant dock) is **Xrm-free and reusable**; the ~70%
Xrm-coupled *existing* widgets are NOT re-hosted — R2 builds **new external widgets** on the existing
injectable **`BffDataverseClient`** (broker-only impl of `IDataverseClient`) seam; assistant per-role
scoping ("no legal advice") is **config-only** (ADR-039 tool catalog + persona system-prompt +
knowledge scope, all Dataverse rows).

## Spec/design implication (to amend)
- **FR-01/FR-02** (module-host card launcher) → **reframe to the workspace shell**: branded portal
  header + **Quick Start action cards** + tabbed **role-defaulted widget** workspace + entitlement-gated
  **widget library** + **assistant dock** (dockable). "Modules" → "widgets". Card-launcher pattern
  survives as the widget-library + Quick Start cards.
- The 5 additions (FR-23–27) become **widgets/quick-starts** on the shell (NDA=quick-start+widget,
  Policy Library=widget with embedded Submit-Policy-Question, Messages=widget, Ask Legal=assistant
  dock, cross-boundary chat=Messages/Requests thread).

## UX model
- **Quick Start cards** (top section) = "**do something**" (wizards/actions): **NDA Assessment · Submit
  Policy Question · Invention Submission · Trademark Search Request · More Services** (→ modal of all
  wizards). Mirrors the corporate-legal-home "Get Started" action-card pattern.
- **Widgets** (tabbed workspace) = "**see/manage**" (data/info surfaces), role-defaulted.
- **Assistant dock** = AI chat, **dockable left or right** (owner theory: as users mature with AI, the
  assistant migrates from a far-right accessory to a first-class **left** primary pane).

## Role-defaulted widgets
- **Internal business users (workforce SSO)**: **My Requests · Inventions · Messages · Policy Library**.
  (NDA is accessed via the Quick Start card, not a default widget.)
- **Outside counsel (CIAM)**: **Projects · Matters · Work Assignments · Documents · Invoices** (= the R1
  Outside-Counsel workspace surfaces, re-hosted as widgets).
- **Core-user admin (MDA)**: Access Admin + Messages.

## UX refinements requested (2026-08-06, this iteration)
1. **Header**: taller / more "intranet/website" feel — a little, not too much (less system-of-record).
2. **Quick Start cards** top section (the 5 above + More Services modal).
3. **Close tab**: a **circle-in-upper-corner** control on the selected tab (SpaarkeAi style), not a shared "close tab" button.
4. **Submit Policy Question** available **inside the Policy Library** page/widget.
5. **Ask Legal dockable on the LEFT** (option) — not only far-right.
6. **Messages widget** includes a **threads pane** (thread list + conversation).
7. **Requests carry messages** — each request has a conversation thread (interaction with the law department happens via messages on the request).
8. Widget sets per persona as above.

## Layout cleanup (2026-08-06, decluttering pass — owner: "UI too messy")
Match the SpaarkeAi clean pattern:
- **Quick Start = the first PINNED tab** (non-closable, always first), NOT a separate stacked card row
  above the tabs. Its body shows the role-relevant Quick Start cards + More Services. Removes the
  over-stacked top chrome. Role widget tabs follow (closable, corner-× SpaarkeAi style).
- **Assistant (Ask Legal) pane**: exactly ONE header; conversation **input box at the bottom**; no
  duplicated Quick Start cards inside it (they live in the pinned tab). Dockable left/right (default
  left per the AI-maturity theory). Chat-focused + clean.
- Vertical stack = header → tab strip (Quick Start pinned + widgets) → active widget body, assistant
  docked to the side. Tighter spacing → reads like a web portal, not a system-of-record.

## Still to do (after prototype iteration + owner sign-off)
- Amend `spec.md`/`design.md`: reframe FR-01/02 → workspace shell; re-home FR-23–27 as widgets/quick-starts.
- Reshape P1 tasks (foundation) accordingly; add widget-set + Quick-Start + assistant-dock tasks.
- The "requests carry messages" ties FR-24 (feedback) + FR-27 (messaging) into one thread-on-request model — note for the FR-24/27 production tasks.
