# Wizard Side-Pane Lookup and Email Compose Pattern

> **Last Reviewed**: 2026-11-12 (owner directive after ontology UAT round 4, findings F-R4-2 / F-R4-3)
> **Status**: Current

## When
Use whenever a task adds or changes, inside a wizard step (`CreateRecordWizard`, `WizardShell`, a follow-on step):
- a field that picks a **person or organization** record (contact, systemuser, `sprk_organization`), or
- an **email** the wizard sends (a Send Email follow-on, a notify step, a document email).

For a lookup outside a wizard (grid filter, record header, widget search box) use [`inline-lookup-field.md`](inline-lookup-field.md) instead.

## Read These Files
1. `src/client/shared/Spaarke.UI.Components/src/components/LookupField/DataverseLookupField.tsx` — the one side-pane lookup field for wizards (Select button → `INavigationService.openLookup`; chip + clear once picked; inline `LookupField` only when no navigation service is injected)
2. `src/client/shared/Spaarke.UI.Components/src/components/CreateProjectWizard/CreateProjectStep.tsx` (~437-457) — reference wiring: `navigationService` + `onSearch` fallback
3. `src/client/shared/Spaarke.UI.Components/src/utils/adapters/xrmNavigationServiceAdapter.ts` — the host adapter (`Xrm.Utility.lookupObjects`); every host already builds it with `createXrmNavigationService()`
4. `src/client/shared/Spaarke.UI.Components/src/components/DecisionWizard/steps/EmailMessageFields.tsx` — reference `EmailComposer` mount in **compose-only** mode inside a wizard (state lifted to the host; sent at Finish)
5. `src/solutions/DocumentUploadWizard/src/components/DocumentEmailStep.tsx` — reference composer mount when the record already exists
6. `projects/spaarke-ontology-platform-r1/notes/create-wizard-lookup-composer-review.md` — inventory of the wizard fields and email steps still on the old patterns, the risks, and the migration tasks (ontology tasks 178-185)

## Constraints
- **ADR-012** — both components live in `@spaarke/ui-components`; Dataverse access arrives only through injected services (`INavigationService`, `IDataService`, `authenticatedFetch`); no `Xrm` import in a component
- **ADR-021** — Fluent v9, semantic tokens; light and dark fully themed
- **ADR-050** — wizards run in `WizardShell`; the Dataverse side pane must open above the wizard and leave its values intact

## Key Rules
- **Never build a new lookup component or email form.** A wizard person/organization field is `DataverseLookupField`; a wizard email is `EmailComposer`. A third rendering of either is a defect (owner directive 2026-11-12).
- **Pass the host's `navigationService` down** to every step that has a person/organization field. A step without one falls back to the inline `LookupField` — that fallback is for hosts with no Xrm, not a choice.
- **Reference tables** (Matter Type, Practice Area, Event Type, other `*type` tables) may stay inline type-ahead until the owner rules otherwise (review §6 Q1); record the ruling in the task notes.
- **Filters** (e.g. Law Firm Attorney limited to the chosen firm) go through `DataverseLookupField`'s `filters` prop → `LookupOptions.filters`, never a client-side post-filter.
- **A failed pane is a cancel**: value unchanged, logged, no unhandled rejection.
- **Compose-only inside a wizard**: `EmailComposer` with `composeOnly` — no Send button, no From row; state lifted with `onStateChange`; the wizard sends at Finish. Do not say "saved as a draft" when Finish sends.
- **Plain-text bodies are sent as `PlainText`**, not the `HTML` default of `EntityCreationService.sendEmail`.
- **Bcc is sent end to end or visibly warned** — never silently dropped.
- Do **not** import the old `components/EmailStep/` in new code; it is being retired (ontology task 184, with a source-scan guard).
