# Audit Findings — from r1 H-3 Solution Scoping (2026-08-20 / 08-21)

> Compiled during `customer-provisioning-orchestration-r1` H-3 pre-check + Apply.
> Two Fable-model audits + 6-layer live-dependency scan + real Apply attempt.

---

## Fable Audit Round 1 — 25 PCFs in Assemble delta classified

Initial classification of the 25 CustomControls that `Assemble-SpaarkeMasterSolution.ps1` planned to add to SpaarkeMaster:

| Verdict | Count | Records |
|---|---|---|
| USE | 16 | 7 Communication* + MatterHeader + TrackingFieldTrio + RegardingResolver + UpdateRelatedButton + SpaarkeGridCustomizer + EmailProcessingMonitor + ThemeEnforcer + ScopeConfigEditor + UniversalDatasetGrid |
| Presumed ORPHAN | 8 | UQC, AnalysisWorkspace, AssociationResolver, DueDatesWidget, EventCalendarFilter, FieldMappingAdmin, RegardingLink, LegalWorkspace |
| UNKNOWN | 1 | PlaybookBuilderHost |

Round 1 also flagged 3 "pre-existing orphans" already in SpaarkeMaster: SpeDocumentViewer, EventAutoAssociate, EventFormController.

## Fable Audit Round 2 — 6-layer live-dependency scan of 13 candidates

Full audit with 6 layers (Dependency table + FormXml + RibbonXml + SavedQuery layoutxml + SiteMap + Solution memberships). Verdict revision:

| Original Round-1 verdict | Round-2 corrections |
|---|---|
| 5 GREEN (safe to delete) | AnalysisWorkspace ✅, DueDatesWidget ✅, EventCalendarFilter ✅, FieldMappingAdmin ✅, LegalWorkspace ✅ |
| 3 YELLOW (delete canvas app first) | UQC, PlaybookBuilderHost, AnalysisBuilder |
| **5 RED — 🚨 REVERSED from "orphan" — actually LIVE consumers** | AssociationResolver (Matter form), RegardingLink (6 sprk_event views), SpeDocumentViewer (Document form + 72 MDA registrations), EventAutoAssociate (Event quick create), EventFormController (Event modal + Assign Work forms, **GUID hardcoded in EventDetailSidePane sidePaneService.ts:119**) |

**Key methodological finding**: Dataverse's `dependency` table indexes form-bound PCFs (`<controlDescription>`) but NOT view-cell PCFs (`<cell control=...>` in `layoutxml`). RegardingLink's 6-view binding was invisible to Layer 1; only Layer 4 (savedquery.layoutxml LIKE search) caught it. Any future PCF audit MUST use the multi-layer approach.

## Owner state changes during audit

- 2026-08-20: owner **removed AssociationResolver** from Matter main form + **removed Matter Insight Card Host HTML** from Matter form (re-added hidden)
- 2026-08-20: owner **overrode DEV-001 permanent-hold** on AnalysisWorkspace ("only launch is SpaarkeAI now")

## Assemble Apply outcome (2026-08-21)

Ran `Assemble-SpaarkeMasterSolution.ps1 -ExcludedPCFs @(...8 names...) -VersionBumpKind Minor`

**Successes**:
- 200 of 210 components added to SpaarkeMaster
- Version bumped `1.0.0.0` → `1.1.0.0`
- `-ExcludedPCFs` parameter wiring worked correctly (7 of 8 exclusions matched — AssociationResolver was the 8th, added after owner form-edit)

**Failures + workarounds**:
1. **10 N:N intersect Entity adds returned 400 Bad Request** — expected Dataverse behavior; intersects auto-generate transitively with parent M:N relationships. Cosmetic failure only.
2. **pac solution export blocked by UDG's dangling WebResource references** — its `bundle.js` (WR `aa669fce-…`) and `styles.css` (WR `cbc4b714-…`) both point to WebResources that don't exist in `spaarkedev1`
3. **`RemoveSolutionComponent` API rejected 4 different payload formats** — action signature exposes `SolutionComponent` as `mscrm.solutioncomponent` entity ref (not `ComponentId` GUID); OData `@odata.bind` and inline entity attempts all failed
4. **Direct DELETE on `solutioncomponent` unsupported** by Dataverse (400 error: "'Delete' method does not support entities of type 'solutioncomponent'")
5. **DELETE on `customcontrolresource` "succeeded" but rows auto-recreated** — likely from UDG's `manifestxml` field which is source-of-truth
6. **pac has no `remove-solution-component` subcommand** — only add

**Owner-approved resolution (Option A)**: **DELETE UDG's entire `customcontrol` record** (`88dbb4ef-6d31-4b15-b89b-68c9401b4f84`). Cascade removed:
- UDG solutioncomponent from SpaarkeMaster (and all other solutions)
- All 3 customcontrolresource rows (including the 2 dangling ones)

Post-cleanup pac export succeeded. SpaarkeMaster.zip 24 MB, 412 root components, 23 CCs, v1.1.0.0 managed.

## Environment-wide finding: 477 orphan customcontrolresources

Enumeration surfaced 477 `customcontrolresource` records in `spaarkedev1` that reference non-existent WebResources. Most are Microsoft-managed platform CustomControls (ViewManagementControl, AutoCompleteControl, QuickForm, PowerBIPersonalDashboard, PhoneNumberControl, ContextualEmail, RadialKnobControl, CanvasListControl, BarCodeScannerControl, ReceiptProcessorControl, etc.).

**Interpretation**: systematic Dataverse platform metadata debt from updates that renamed/consolidated resources but left stale metadata rows. Not Spaarke-caused. Not blocking for anything except SpaarkeMaster export (which is now unblocked by removing our own UDG orphans).

**Recommendation**: leave the Microsoft-managed ones alone. Track for reference. If a future solution export blocks on one, delete only what's blocking (targeted, not bulk).

## Backup safety net (still current)

The 10 baseline unmanaged solution ZIPs in `projects/pcf-orphan-cleanup-r1/backups-2026-06-22/` remain intact (5 MB total). Cover 10 of the 11 orphan PCFs (AnalysisBuilder is Default-solution-only, no dedicated backup). Recovery via `pac solution import`.

**Note**: UDG's dedicated solution `SpaarkeUniversalDatasetGrid` was NOT in the 2026-06-22 backup set. If UDG is ever needed again, restore path is: rebuild from source at `src/client/shared/Spaarke.UI.Components/` (the DataGrid framework's shared component library that supersedes it) or export from another env where UDG still exists.
