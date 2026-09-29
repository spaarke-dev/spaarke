# Configurable Record Header (R2) — Implementation Plan

> **Generated**: 2026-08-25 by `/project-pipeline` Step 2
> **Source**: [`spec.md`](spec.md) (27 FRs · 11 NFRs · 24 success criteria) ← [`design.md`](design.md)
> **Estimate**: ~9.25–13.5 dev-days · **Tasks**: 28 across 7 phases
> **Branch**: `work/record-header-and-notepad-r2` (already exists; merged to `origin/master` 2026-08-25)

---

## 1. Architecture Context

### Discovered Resources

**ADRs** (loaded — full content consulted during design verification):

| ADR | Relevance to R2 |
|---|---|
| [ADR-006](../../.claude/adr/ADR-006-pcf-over-webresources.md) | PCF is the correct surface for form-bound UI |
| [ADR-012](../../.claude/adr/ADR-012-shared-components.md) | Renderers + resolver + hoisted machinery belong in `@spaarke/ui-components` |
| [ADR-021](../../.claude/adr/ADR-021-fluent-design-system.md) | Fluent v9 semantic tokens only; dark + high-contrast |
| [ADR-022](../../.claude/adr/ADR-022-pcf-platform-libraries.md) | Shared components stay React 16/17-safe |
| [ADR-024](../../.claude/adr/ADR-024-polymorphic-resolver-pattern.md) | `sprk_memo` Path C; regarding-field wiring |
| [ADR-038](../../docs/adr/ADR-038-testing-strategy.md) | `resolveHeaderConfig` is a pure function → unit tests are a KEEP category |
| [ADR-020](../../.claude/adr/ADR-020-versioning.md) | PCF version sync across **5** locations |
| ADR-028 | **N/A** — host-context `Xrm` only, no BFF, no `@spaarke/auth` |
| [ADR-011](../../.claude/adr/ADR-011-dataset-pcf.md) | Cited only to correct a misreading — it contains no "no runtime schemas" rule |

**Skills**: `fluent-v9-component` (CRITICAL) · `pcf-deploy` (CRITICAL) · `ui-test` · `code-review` + `adr-check` (Step 9.5 gates) · `adr-aware`, `spaarke-conventions`, `context-handoff`

**Patterns**: [`pcf/pcf-build-scaffold.md`](../../.claude/patterns/pcf/pcf-build-scaffold.md) (10 gotchas from R1 UAT — read before any build work) · [`pcf/xrm-webapi-related-count.md`](../../.claude/patterns/pcf/xrm-webapi-related-count.md) · [`pcf/dataverse-queries.md`](../../.claude/patterns/pcf/dataverse-queries.md) · [`pcf/fluent-v9-modern-theming.md`](../../.claude/patterns/pcf/fluent-v9-modern-theming.md) · [`ui/record-header-composition.md`](../../.claude/patterns/ui/record-header-composition.md) · [`ui/fluent-v9-component-authoring.md`](../../.claude/patterns/ui/fluent-v9-component-authoring.md) · [`ui/fluent-v9-react-version-boundaries.md`](../../.claude/patterns/ui/fluent-v9-react-version-boundaries.md)

**Canonical implementations to copy** (do not invent — these are the reference impls):

| Need | Copy from |
|---|---|
| Renderer contract | `RecordHeader/fields/TextField.tsx` |
| Tiered config resolver | `components/DataGrid/configResolution.ts` |
| Non-throwing config guard | `types/DataGridConfiguration.ts:479` `isValidDataGridConfiguration` |
| Metadata access | `services/XrmDataverseClient.ts` — **extend, never replace** |
| OOB lookup picker | `CommunicationActions/CommunicationActionsApp.tsx:405-413` |
| Form-buffer staging | `MatterHeaderView.tsx:175-235` (preserve semantics exactly) |
| Metadata cache shape | `services/PolymorphicResolverService.ts:451` `_navPropCache` |

**Scripts**: `pcf-deploy` skill drives build/pack/import; `scripts/ensure-dist-fresh.js` is a mandatory prebuild guard.

**Schema validation** (live against `spaarkedev1`, 2026-08-25): all six entities verified — field lists, attribute types, option-set values, lookup targets, main-form GUIDs, `sprk_recordsummary` presence. Recorded in design §9 and [`notes/discovery-checklist.md`](notes/discovery-checklist.md). **No schema gaps remain** — the owner created `sprk_recordsummary` on all six.

### Hot-Path Declaration

```xml
<hot-path-declaration>
  <bff>N</bff> <spaarkeai>N</spaarkeai> <ci-workflows>N</ci-workflows>
  <skill-directives>N</skill-directives> <root-claude-md>N</root-claude-md>
</hot-path-declaration>
```

**Overlap check**: R2 is all-N, so no hot-path collision with the 32 active worktrees is possible. The 10 open PRs touch Compose, provisioning, devops and dependabot surfaces — none touches `RecordHeader/**`, `HeaderToolbar/**`, `MatterHeader/**` or `configResolution.ts`. ✅ No coordination required.

### ADR Tensions

**None declared.** Spec states this affirmatively with two clarifications on record (ADR-011 does not bar config-driven controls; hoisting `Xrm.Page` into `hooks/`+`services/` follows the library's existing convention rather than breaking ADR-012).

---

## 2. Strategy

Four ordering constraints drive everything:

1. **Shared-lib first.** Renderers, resolver and metadata land before the control consumes them; the control lands before any form binding.
2. **Matter migrates last.** It is the parity regression test, and retiring `MatterHeaderPcf` is irreversible on delivery (D-4).
3. **The barrel serializes.** R1 lost time to parallel renderer tasks racing on `fields/index.ts`. Renderer tasks are `parallel-safe: true` for their own files but the barrel edit is a separate serial task.
4. **Remediation is independent.** RS-1/RS-2 (Phase 4) depend on nothing and unblock nothing — schedule them early because RS-1 is live production breakage.

---

## 3. Phase Breakdown

### Phase 0 — Spike + Baseline (2 tasks · ~0.5 d) — BLOCKS everything

| Task | Deliverable |
|---|---|
| **001** | `layoutJson` ergonomics spike — classic designer, `Multiple` vs `SingleLine.Text`, export/import round-trip. Decides one manifest attribute; cannot change the design. |
| **002** | Capture `MatterHeaderPcf` v1.0.20 parity baseline — screenshots light+dark, exact 5-field layout + spans, bound field, footer version. **Must happen before any code change.** |

> ⚠️ Task 002 is genuinely blocking: once the control changes there is no baseline to diff against.

### Phase 1 — Renderers (6 tasks · ~2–3 d)

| Task | Deliverable | Parallel |
|---|---|---|
| **010** | `DateField` — `DateOnly` + `DateAndTime` modes off metadata `Format` (FR-06) | ✅ group A |
| **011** | `NumberField` — Integer/Decimal/Double/Money, currency + precision, right-align (FR-07) | ✅ group A |
| **012** | `BooleanField` — Yes/No read, Fluent `Switch` edit (FR-08) | ✅ group A |
| **013** | `OptionSetField` — add edit mode + fix stale label typography (FR-09) | ✅ group A |
| **014** | `TextField` — em-dash `''` alignment (FR-11) | ✅ group A |
| **015** | **Barrel + contract tests** — `fields/index.ts` exports; shared renderer-contract test suite (FR-10) | ❌ serial, after A |

### Phase 2 — Metadata + Machinery (5 tasks · ~2–2.5 d)

| Task | Deliverable | Parallel |
|---|---|---|
| **020** | `IDataverseClient` + `XrmDataverseClient`: project lookup `targets`; page-session metadata cache (FR-21) | ✅ group B |
| **021** | Shared `getXrmPage()`; add `Page` to `xrmContext`; migrate both existing duplicates (FR-20) | ✅ group B |
| **022** | `useRecordHeaderFields` — hoist form-buffer staging, pending buffer, `projectLookup`; unify the throwing path (FR-13, FR-14, FR-19) | ❌ after 021 |
| **023** | OOB lookup cell — `Xrm.Utility.lookupObjects`, owns its `span`; retire the custom type-ahead from the header path (FR-15a) | ❌ after 020, 022 |
| **024** | Toolbar: slot auto-hide on null filter + add `sprk_agreement` to both parent maps (FR-16, FR-24) | ✅ group B |

### Phase 3 — Config Resolver + Control (5 tasks · ~2–2.5 d)

| Task | Deliverable | Parallel |
|---|---|---|
| **030** | `RecordHeaderConfiguration` types + `isValidRecordHeaderConfiguration` guard (FR-01 schema, FR-03) | ✅ group C |
| **031** | `resolveHeaderConfig` — tiers, span clamp, renderer derivation, derived defaults (FR-02, FR-04, FR-05) | ❌ after 030 |
| **032** | `RecordHeaderShell` optional `columns` prop for the skeleton (FR-18) | ✅ group C |
| **033** | **`RecordHeader` PCF** — new solution identity, manifest incl. `layoutJson` (no apostrophes), `ensure-dist-fresh` prebuild, entity self-detection, generic view (FR-01, FR-12, NFR-11) | ❌ after 031 |
| **034** | Sparkle wiring — `sprk_recordsummary` via `RECORDSUMMARY_FIELD`; existence-not-population visibility; "No summary yet" (FR-17, FR-22) |❌ after 033 |

### Phase 4 — Schema-drift remediation (2 tasks · ~0.25 d) — independent

| Task | Deliverable |
|---|---|
| **040** | **RS-1** — remove `sprk_mattersummary` from the Matter `$select`; the shipped header currently 400s. Decide v1.0.21 hotfix vs wait-for-R2 (FR-23) |
| **041** | **RS-2** — `sprk_aitopicregistry` "Matter Summary" row: `sprk_targetfield` → `sprk_recordsummary`. Dataverse data fix. Also fix the stale `sprk_aisummary` comment in `InvoiceExtractionJobHandler` (FR-23) |

### Phase 5 — Rollout (7 tasks · ~3 d)

Bind + QA per entity against its live-verified main form. Each is a form edit plus acceptance run.

| Task | Entity | Form GUID | Exercises |
|---|---|---|---|
| **050** | `sprk_project` | `5aa00242-…` | date · boolean · optionset |
| **051** | `sprk_workassignment` | `7e578eef-…` | date · boolean · optionset |
| **060** | `sprk_invoice` | `93aa1c69-…` | **currency** · date · boolean · optionset |
| **061** | `sprk_event` | `eaf22dcb-…` | **datetime** · lookup · optionset |
| **070** | `sprk_agreement` | `59d88274-…` | toolbar map change; **needs a seeded record** (0 exist) |
| **080** | `sprk_matter` | `4fa382f2-…` | **parity QA** vs the 002 baseline; lookups excepted |
| **081** | Retire `MatterHeaderPcf` — remove form refs + publish, **then** delete the CustomControl (two ordered steps) |

> Waves: 050+051 → soak → 060+061 → 070 → 080 → 081. Bundle + TTI measured **per wave** (NFR-01, NFR-02), not once at the end.

### Phase 6 — Documentation + wrap (3 tasks · ~1 d)

| Task | Deliverable |
|---|---|
| **085** | Rewrite `RECORD-HEADER-PCF-AUTHORING-GUIDE.md` **from shipped code** — ~170–190 of 354 lines replaced; preserve the bundle triad; "4 version locations" → 5 (FR-27) |
| **086** | Refresh `.claude/patterns/ui/record-header-composition.md` body sections (main-session-only — `.claude/` write boundary) |
| **090** | Wrap-up — README status, `lessons-learned.md`, `/test-diet`, archive |

---

## 4. Parallel Execution Groups

| Group | Tasks | Prerequisite | Notes |
|---|---|---|---|
| **A** | 010, 011, 012, 013, 014 | 002 | Distinct renderer files. **None may edit `fields/index.ts`** — that is task 015. |
| **B** | 020, 021, 024 | 002 | Distinct services/hooks files |
| **C** | 030, 032 | 015 | Types file + shell prop |
| — | 040, 041 | none | Independent; schedule early (RS-1 is live breakage) |
| — | 050+051, then 060+061 | 034 | Form binding is operator/maker work, not agent work |

**Serial by necessity**: 015 (barrel), 022 (needs 021), 023 (needs 020+022), 031 (needs 030), 033 (needs 031), 034 (needs 033), 080/081 (Matter last), 086 (`.claude/` main-session-only).

**Max concurrency**: 6 agents per wave. Build verification between waves is mandatory — `npm run build:prod` for PCF (**not** `npm run build`).

---

## 5. Risks

Carried from design §10, with the plan-level mitigation:

| Risk | Mitigation in this plan |
|---|---|
| Single control = shared blast radius | Staged binding (Phase 5 waves), Matter last, version footer as the swap check |
| Bundle growth breaches 250 KB | Measured per wave (Phase 5), not at the end. Optimization triad untouchable. |
| Metadata calls regress TTI | Task 020 delivers the cache; TTI measured per wave |
| Renderer tasks race on `fields/index.ts` | Barrel extracted to serial task 015 |
| `layoutJson` editor ergonomics | Task 001 spike; `SingleLine.Text` fallback proven — cannot change the design |
| Agreement has 0 records | Task 070 seeds one before QA |
| Matter parity vs the OOB lookup change | Baseline captured in 002; parity criterion explicitly excludes the lookup interaction |

---

## 6. Definition of Done

All 24 spec success criteria met, plus:

- Six entities render correctly from config on their main forms
- Matter parity holds against the task-002 baseline (lookup interaction excepted)
- `LOOKUP_META` deleted; exactly one `getXrmPage` in `src/`
- No source file references `sprk_mattersummary` or `sprk_aisummary`
- Bundle ≤250 KB; TTI ≤300 ms warm / ≤800 ms cold
- `MatterHeaderPcf` retired
- Authoring guide contains no trace of the retired per-entity recipe

---

## 7. References

- [`spec.md`](spec.md) — 27 FRs / 11 NFRs / 24 success criteria
- [`design.md`](design.md) — rationale, live-verified schema (§9), decisions D-1…D-10
- [`notes/discovery-checklist.md`](notes/discovery-checklist.md) — closed discovery, audit trail
- [`notes/issues/`](notes/issues/README.md) — 3 schema-drift issue docs (out of scope; separate evaluation)
- [`record-header-and-notepad-r1/notes/lessons-learned.md`](../record-header-and-notepad-r1/notes/lessons-learned.md) — **read before any PCF build work**
- [`record-header-and-notepad-r1/notes/matter-form-binding-instructions.md`](../record-header-and-notepad-r1/notes/matter-form-binding-instructions.md) — the maker recipe Phase 5 follows
