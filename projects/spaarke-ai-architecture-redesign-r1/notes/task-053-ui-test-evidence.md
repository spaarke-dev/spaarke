# Task 053 — PlaybookBuilder de-scope → BA Action/Binding editor: UI-test evidence

> FR-P4-04 · executed 2026-07-07 (dispatched early by operator direction; gate 048 still in UAT).
> Browser verification is deliberately deferred to the **G-M maker gate (task 090)** per dispatch —
> the evidence here is jest-DOM (jsdom) per the operator's overlap plan. NFR-11 note: this file does
> NOT claim a gate pass; G-M is a human browser walk on spaarkedev1.

## What the acceptance flow proves (jest-DOM, mocked at the Dataverse wire only)

`src/client/code-pages/PlaybookBuilder/src/components/catalog/__tests__/CatalogEditorShell.test.tsx`
mocks ONLY `dataverseClient` (fetch wrapper). All validation, mapping, and save-gating logic runs for
real, so the asserted payloads are the exact Web API columns spaarkedev1 would receive.

| # | Assertion | Result |
|---|---|---|
| 1 | BA authors an Action end-to-end: name + action code + prompt + input schema + output schema → Save → `POST sprk_analysisactions` with `sprk_name/sprk_actioncode/sprk_inputschema/sprk_outputschemajson` | ✅ |
| 2 | **The exact G-P3 round-1 outage schema (property-level `"required": true`) shows an INLINE error while typing AND the save gate refuses it — zero Dataverse calls** | ✅ |
| 3 | NFR-06 eval-suite reminder renders on every successful catalog save | ✅ |
| 4 | Saved Action appears in the list (reload round-trip) and is selectable as the Binding's target | ✅ |
| 5 | BA authors the Binding end-to-end: consumer type, ucid, tool description, target Action (`sprk_action@odata.bind`), surfaces CSV, chip transition (pinned JSON shape), on-event binding `[{"event":"document_uploaded","order":1}]` → `POST sprk_playbookconsumers` | ✅ |
| 6 | Binding without target Action / tool description is refused (validation bar; zero writes) | ✅ |
| 7 | No canvas/graph authoring surface reachable anywhere in the rendered page (NFR-08 DOM assertion) | ✅ |
| 8 | ADR-021 static gate: all 6 catalog-editor components use ONLY Fluent v9 semantic tokens (no hex/rgb/named colors) — theme-agnostic by construction, verified in both themes at G-M | ✅ |

## Test totals (2026-07-07)

- PlaybookBuilder: **6 suites, 102/102 green** (`npm test`)
  - `schemaValidation.test.ts` — server-twin rule matrix incl. the exact UAT outage payload pinned invalid forever
  - `catalogService.test.ts` — validation-gated saves + column mapping
  - `CatalogEditorShell.test.tsx` — the FR-P4-04 end-to-end authoring flow
  - `ActionEditorForm.test.tsx` / `BindingEditorForm.test.tsx` — field-level authoring UX
  - `adr021-dark-mode-compliance.test.ts` — retargeted at the 6 new components (+ scanner self-tests)
- ScopeConfigEditor PCF: **4 suites, 53/53 green** (`npm test`)
  - `BindingConfigEditor.test.tsx` — Binding variant per-column validation + SchemaJsonEditor outage-payload pin + validator-twin matrix
  - `ScopeConfigEditorApp.test.tsx` — routing incl. `sprk_playbookconsumer` and the Action schema columns
- Builds: PlaybookBuilder `npm run build:prod` green (bundle 2.63 MiB; canvas libs removed);
  ScopeConfigEditor `npm run build:prod` (pcf-scripts, ESLint included) green, **v1.3.0**.

## G-M UAT script (maker gate walk-through, task 090 — operator on spaarkedev1)

Pre-req: deploy the PlaybookBuilder web resource (`build-webresource.ps1` / code-page-deploy) and the
ScopeConfigEditor solution v1.3.0 (pcf-deploy); add ScopeConfigEditor to the `sprk_playbookconsumer`
form bound to `sprk_chiptransitions` (and optionally `sprk_oneventbindings` / `sprk_tooldescription`),
and to the `sprk_analysisaction` form bound to `sprk_inputschema`.

1. **Open PlaybookBuilder** (sprk_playbookbuilder web resource). Verify: "AI Capability Catalog" with
   Actions | Bindings tabs; NO canvas, node palette, or run button anywhere. Toggle dark mode —
   every surface adapts (ADR-021).
2. **Author a new Action** (kind: Prompted): name + action code (e.g. `DEMO-CAP@v1`) + JPS prompt via
   "Insert JPS starter template" (edit role/task) + input schema. First paste the outage shape —
   `{"type":"object","properties":{"x":{"type":"string","required":true}}}` — verify the inline
   error names the property-level required ban and Save is refused. Fix to an object-level
   `required` array; add an output schema; pick a model tier; Save. Verify the NFR-06 eval reminder
   appears and the row exists in Dataverse (`sprk_analysisactions`).
3. **Author its Binding**: Bindings tab → New Binding → name, consumer type (e.g. `demo-capability`),
   UCID, tool description (plain-language intent), target Action = the row from step 2, disposition /
   risk / capture mode, surfaces (e.g. `assistant`), one chip transition (target = an existing Binding
   id; label), one on-event membership (`document_uploaded`, order N). Save. Verify the row in
   `sprk_playbookconsumers` incl. `sprk_action` lookup, `sprk_chiptransitions`, `sprk_oneventbindings`.
4. **Catalog projection**: open the Assistant on spaarkedev1 → the new capability projects as a tool
   (tool description drives intent matching); `GET /healthz` stays Healthy (the H1 health-check scan
   accepts the authored schema).
5. **Record-form variant**: open the Binding row's form → the ScopeConfigEditor Binding variant renders
   for the bound JSON column with a Valid/Invalid badge; paste a chip entry without
   `target_binding_id` → inline error. Open an Action row's form bound to `sprk_inputschema` → paste
   the outage shape → inline error; corrected shape → Valid badge. Verify both themes.
6. **Negative check (NFR-08)**: confirm no route/param/flag resurrects the canvas (`?canvas=`,
   old `playbookId` deep links land on the catalog editor).

## Save-path decision (DATA-ACCESS-DECISION-CRITERIA)

Direct Dataverse Web API via the page's existing `dataverseClient` (cookie-auth same-origin; the
code-page equivalent of `Xrm.WebApi`) — NOT the BFF. Criteria: #1 BA's own Dataverse privileges gate
catalog authoring; #2 simple single-table CRUD; #3 no AI in the save; #4 Dataverse auditing suffices;
#6 single-record writes. Also honors ADR-013 (no AI-internal types in authoring CRUD) and the
operator's src/server freeze — no new BFF endpoint was needed.

## Deferred / blocked (operator boundary 2026-07-07)

- **POML Step 5 — `AiPlaybookBuilderService` retarget + dead graph-endpoint deletion (src/server)**:
  BLOCKED by the dispatch hard boundary ("src/server … UAT fix territory — do NOT touch"). The ONLY
  client caller of `/api/ai/playbook-builder/process` (`aiPlaybookService.ts`) was deleted this task
  and grep-zero-verified, so the server surface is now dead code awaiting removal. Needs a follow-up
  task (or fold into the 048 round-2 wave): retarget/delete per OVERLAY-MATRIX S3 verdict, update
  `tests/unit/Sprk.Bff.Api.Tests/`, and report the ADR-029 publish-size delta (expected reduction).
- **ADR-029 publish-size**: N/A this task — zero `src/server` changes by construction.
- **Validator triple-twin consolidation**: `OpenAiFunctionSchemaValidator.cs` (server, source of
  truth) + `PlaybookBuilder/src/services/schemaValidation.ts` + `ScopeConfigEditor/.../utils/
  openAiSchemaValidator.ts`. Hoisting the two TS copies into `@spaarke/ui-components` was deferred
  (shared-lib churn during live G-P3 UAT waves); each copy carries a lock-step header and its own
  rule-matrix test pin. Candidate for `/defer` at task 090.
