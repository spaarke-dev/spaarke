# Design — Spaarke External Access Platform R3 (DRAFT for review)

> **Status**: 🟡 DRAFT — scope collection for owner review. Not yet a spec; do not start execution.
> **Created**: 2026-08-12 · **Author**: (external-access-r2 follow-on)
> **Predecessors**: `spaarke-SPA-external-access-platform-r1` (shipped) · `-r2` (shipped — module-host SPA
> platform, dual-plane auth, access-write/read model, Tier-1 entitlement, task-073 UAT)

---

## 1. Context

R2 delivered the **module-host external SPA** (card launcher + dual identity-plane auth + Teams-embed),
the **external access model** (polymorphic grant write/read across Project/Matter/Work Assignment,
organization grants, standing grants, Tier-1 module entitlement), and the **read path** (granted records
+ document/invoice rollup) — all verified in task-073 live UAT.

R2 UAT confirmed the access model works end-to-end, but surfaced **interactive gaps** that were, by
design, deferred read-only "graceful degrades" or P3 (Legal Front Door intake) scope. R3 closes those so
the portal is a working destination, not just a read surface. **This is critical functionality** (owner,
2026-08-12).

## 2. Problem statement (from R2 live UAT, 2026-08-12)

| # | Observed | Root cause |
|---|---|---|
| 3E | Clicking a record in the SPA grids does nothing | SPA is intentionally **Xrm-free/read-only**; grid row-open defaults to `Xrm.Navigation.navigateTo`, which no-ops without `Xrm`. No SPA record-detail surface exists. |
| 4B | Quick-start cards (NDA Assessment, Invention Submission, Submit Policy Question, Trademark Search) don't open wizards | The **Legal Front Door typed-intake capability is not built** (P3). The cards are previews; the launch handler only opens a widget when one is wired. |
| 6A | An already-onboarded external contact added to a **subsequent** record gets **no** notification | `invite-and-grant` emails only on **first** CIAM onboarding; a subsequent `/grant` sends nothing. |
| Grids | Column sets in the CIAM data grids need refinement | Grid `sprk_gridconfiguration` column config is placeholder-level for several tabs. |
| Teams | Same quick-start + grid behavior in the Teams-embedded app | Teams embeds the same external-spa; fixes apply to both surfaces. |

**Working as designed (NOT R3 scope):** "Ask Legal" assistant (FR-26 preview — future release); e-signature
for NDA (deferred). 5A first-assignment CIAM invite email works (R2). 5B Partner-primary login shipped (R2).

## 3. Goals / Non-goals

**Goals**
- G1. Partners can **open a record** from any SPA/Teams grid into a read-only detail surface (fields +
  related documents/invoices), respecting the same Tier-2 per-record authorization as the list.
- G2. The **Legal Front Door** typed-intake wizards create `sprk_servicerequest` records and are launchable
  from the quick-start cards + "More Services" (NDA, Invention, Policy Question, Trademark — final set TBD).
- G3. An external contact added to a **new** record receives a **notification** (email, portal deep-link)
  — parity with first-assignment onboarding.
- G4. Refined, owner-approved **grid columns** across the CIAM data tabs.
- G5. Full **Teams-embedded parity** for G1–G4.

**Non-goals (this release)**
- Ask Legal assistant (FR-26 preview). E-signature. In-portal record **editing** (partners stay read-only;
  intake creates service requests, it does not edit core records). New identity planes.

## 4. In-scope capabilities + reuse map (§11 — extend donors, don't fork)

### C1 — Record-open detail surface (G1 / 3E)
- **Reuse**: `SprkModal` + `RecordNavigationModalShell` (the "1 of N" browse shell) from
  `@spaarke/ui-components`; `RecordHeader`/field components for read-only field display; the **existing R2
  read path (task 028)** that already powers the tab lists + doc/invoice rollup (no new server read).
- **New**: a read-only "record detail" view (fields + related docs/invoices) wired to the grid framework's
  `rowOpen` for the Xrm-free SPA host (grid config already supports a `rowOpen` type — see the Service
  Request config's `"rowOpen":{"type":"formDialog"}`).

### C2 — Legal Front Door typed-intake wizards (G2 / 4B)
- **Reuse**: the `Wizard` / `WizardModal` preset / `WizardRegistry` framework; `CreateRecordWizard` as the
  structural pattern; `FieldMappingService`; the `sprk_servicerequest` entity (stub exists); the
  QuickStartPane `launch → onOpenWidget` wiring + the surface-launch registry pattern.
- **New**: one typed-intake wizard per request type (schema-driven where possible) that writes a
  `sprk_servicerequest`; registry entries; wire each quick-start action's target. **NOT code pages** —
  these are shared React `WizardModal` components mounted in the SPA (same as the Create* wizards).

### C3 — Subsequent-assignment notification (G3 / 6A)
- **Reuse**: the R2 external-access grant write path + the existing CIAM invite/notify plumbing
  (`invite-and-grant`), the notification/email sender the BFF already uses.
- **New**: on a `/grant` to an already-onboarded contact, send a "you've been given access to {record}"
  notification with a portal deep-link. §10 BFF hygiene: Placement Justification + publish-size + tests.

### C4 — Grid column refinement (G4)
- **Reuse**: DataGrid framework `sprk_gridconfiguration` config records (data-only; no code).
- **New**: owner-approved column sets per CIAM tab (needs the column spec from the owner).

### C5 — Teams parity (G5) — verify C1–C4 render + launch correctly in the Teams-embedded host.

## 5. ADR touchpoints (anticipated)
- ADR-028 (auth planes — unchanged; reuse), ADR-024 (polymorphic regarding — service requests),
  ADR-050 / MODAL-DECISION-CRITERIA (record-detail modal), ADR-009 (cache/invalidate for notify),
  ADR-007 (SPE facade for any doc access in detail view), §10 BFF hygiene (C3 notification).

## 6. Open questions for the owner (review these)
1. **Front Door request types** — confirm the final set (NDA Assessment, Invention Submission, Policy
   Question, Trademark Search, other?) and which are R3 vs later.
2. **Record-detail depth** — read-only fields + related docs/invoices only, or also actions
   (e.g., "email members", "upload a document")? Any per-request-type detail?
3. **Notification channel (6A)** — email only, or also in-portal? Content/deep-link target?
4. **Grid columns (C4)** — provide the desired column set per tab (Projects/Matters/Work Assignments/
   Documents/Invoices/Service Requests).
5. **Scope boundary** — is 6A (notification) R3, or a small R2 follow-on? Is C4 (columns) R3 or immediate?

## 7. Draft success criteria
- A partner opens a granted Matter/Project/WA/Document/Invoice into a read-only detail surface (Tier-2
  enforced) in both SPA and Teams.
- Each quick-start card opens its intake wizard; submitting creates a `sprk_servicerequest` visible in the
  Service Requests tab.
- Adding an onboarded contact to a new record sends them a portal-linked notification.
- Grid columns match the owner-approved spec.

---

*Next step after review: convert this to `spec.md` via `/design-to-spec`, then `/project-pipeline`
(INITIALIZE-ONLY) to generate plan + tasks. Reuse donors above are binding per §11.*
