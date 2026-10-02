# Reuse verification — 2026-10-02

> ## ✅ STATUS: CURRENT — the reuse evidence base
>
> Produced by three parallel read-only audits (live Dataverse MCP + code) commissioned by the owner on
> 2026-10-02 to answer: *"are we very clear on the existing components, and certain we are not recreating
> components or creating conflicts, duplicates, inefficiencies, technical debt?"*
>
> **`design.md` holds the resulting decisions; this file holds the evidence.** Schema row counts live in
> [`mvp-technical-spec.md`](mvp-technical-spec.md) §10.7. Every claim below carries file:line.
> **Audience**: owner · `/design-to-spec` · `task-create` · `code-review`.

---

## 1. The headline: four of our own claims were wrong or overstated

| # | What we claimed | What the code shows | Where it is now corrected |
|---|---|---|---|
| 1 | *"`ILiveFactResolver` is **already generic** — so no per-entity fact tables"* | **Generic dispatch, hardcoded predicates.** Subject-scheme → resolver is config-driven (`SubjectParser.cs:80-98` + `SubjectSchemeCatalogOptions`), but each resolver's predicate set is a **closed C# `switch`** (`MatterLiveFactResolver.cs:166-174`, `InvoiceLiveFactResolver.cs:139-147`, `ProjectLiveFactResolver.cs:149-157`). **Neither target predicate is reachable by adding a `case`** | `design.md` D-8, restated |
| 2 | *"The `Existence` rule type is implemented as a FetchXML `link-entity` filter, so CM-3 holds"* — stated as if proven | **The EXISTS half has prior art; the NOT EXISTS half has none.** EXISTS reference: `DataversePrecedentBoard.cs:182-190` (`AddLink` + `LinkCriteria.AddCondition`). For NOT EXISTS, every `ConditionOperator.Null` in the repo filters a **column on the primary entity**, never an aliased link-entity column; every `LeftOuter` join enriches rather than anti-joins. **No in-repo template** | `design.md` §5 `Existence` row + §9 risk |
| 3 | *"reuse `ISignalRule`'s strategy shape"* | **`ISignalRule` is a private nested interface** (`SignalEvaluationService.cs:268`) whose three rules are instantiated directly in the constructor (lines 116-120). **Not an extension point** — a non-spend rule cannot be added without editing that file. Its XML doc claims a strategy pattern the code does not deliver | `design.md` §3.2 |
| 4 | *"the Insights Engine already exists"* | **True in part, and the part R1 needs is the real part.** Facts / Observations / Precedents are implemented and DI-registered; **`IInsightGraph` is a pure stub** — every method throws `NotImplementedException` ("deferred to Phase 1.5", `StubInsightGraph.cs:25-62`). R1 touches Facts + the `InsightArtifact`/`EvidenceRef` envelope and **never the Graph** | `design.md` §1.1 |

## 2. 🔴 Three duplication risks — the core answer to the owner's question

Each is a component that **already exists** and would be silently rebuilt. CLAUDE.md §11 is the rule; these are
the three places it would have been broken.

| Need | ❌ Do not build / extend | ✅ Extend this | Evidence |
|---|---|---|---|
| **Count-filter cards** (the retired stat tiles) | A new card — **or `StatTiles`**, which has **no `onClick` at all** (`StatTiles.tsx:95-111`) and whose "independent overlapping lenses" semantics is exactly what we are retiring | **`MetricCard` / `MetricCardRow`** (`Spaarke.UI.Components/src/components/WorkspaceShell/MetricCard.tsx:24-222`) — **already interactive** (`role="button"`, clickable), square, badge-capable | A third card type here would be a §11 violation with no cost-of-doing-nothing |
| **The row's ⋮ action menu** | A 4th bespoke `<Menu>` | **`DocumentRowMenu`** (`Spaarke.UI.Components/src/components/DocumentRowMenu.tsx:150-208`) — action-descriptor table + `disabledActions` filtering + grouped dividers. Its own docstring calls it *"the canonical Spaarke three-dot pattern"*, a claim `NarrativeBullet.tsx:73-74` itself cites | **Three hand-rolled ⋮ menus already exist**: `DocumentRowMenu`, `NarrativeBullet.tsx:600-645`, `HighPrioritySection.tsx:298-325`. This is CLAUDE.md §11's own named anti-pattern — *"one excellent handler beats five that partially overlap"* |
| **Post-action outcome surface** (complete / reschedule / reassign / dismiss) | A parallel "worklist outcome card" | **`OutcomeCard`** (`Spaarke.UI.Components/src/components/SprkChat/OutcomeCard.tsx:93-367`) — already status badge + user-facing summary + server-composed link + next-step chips | Extend it with the Signal statuses (`Acted` / `Dismissed` / `ConditionCleared` / `Superseded` / `PolicyRetired`) |

**The one reuse claim that checked out exactly**: `<DataGrid configId=… />` + `sprk_gridconfiguration` covers
**membership and column config** but not the row — `DataGridOverrides.columnRenderers` is a *per-column cell*
override inside a tabular row (`DataGrid.tsx:1220,1224`), not a row-level renderer. `needs-review.gridconfiguration.json`
proves membership can live entirely in FetchXML. So §7 criterion 7's amendment is correct in both halves.

## 3. 🔴 The dismiss-path trap — a live hazard, not a style note

`Spaarke.DailyBriefing.Components` still **barrel-exports three hooks that are dead in the current data path**:
`useBriefingNotifications`, `useBriefingNarration`, `useBriefingActions`, backed by `notificationService.ts`'s
`appnotification` writes (`markBriefingChecked` / `markBriefingRemoved` / `extendBriefingTtl`).

Dead because `DailyBriefingApp.tsx:64` imports only `useBriefingRender`, `useInlineTodoCreate` and
`useBriefingPreferences`; the live path is `fetchBriefingLive` behind `USE_LIVE_RENDER=true`
(`briefingService.ts:397`).

**Why it matters**: they are still importable and they still look like the dismiss mechanism. An implementer
wiring the worklist's **Dismiss** to `markBriefingRemoved` would be writing **bell-panel read-state** instead of
a **Decision Record** — silently violating row-contract requirement 5, and losing the suppression input that
criterion 4 depends on. Dismissal would appear to work and record nothing.

**Mitigation**: remove them from the barrel (or mark `@deprecated` with the reason) **before** the worklist row
is built, not after.

## 4. Component-by-component verdicts — `Spaarke.DailyBriefing.Components`

| Component | Verdict | Note |
|---|---|---|
| `NarrativeCitedText` (`NarrativeCitedText.tsx:92-303`) | ✅ **REUSE AS-IS** | Pure, test-covered deterministic text→entity-link segmenter. Carry forward **independently of the row that currently hosts it** |
| `useInlineTodoCreate` (`useInlineTodoCreate.ts:196-375`) | ✅ **REUSE AS-IS** | **The only existing "action that changes something"** in the package — creates `sprk_todo` with ADR-024 regarding resolution. Becomes the worklist's *create follow-up task* action |
| `EmptyState`, `channelIcons`, `preferencesService`, `useBriefingPreferences` | ✅ **REUSE AS-IS** | — |
| `DigestHeader` | 🟡 **EXTEND** | Becomes the worklist header; add lane tabs / *Start review*. Drop the already-`@deprecated` `totalUnreadCount` prop |
| `TldrSection` | 🟡 **EXTEND** | Narrative wrapper carries forward; **delete the `topAction` branch** (`TldrSection.tsx:444-459`) per BR-3 / decision 15 |
| `useBriefingRender` | 🟡 **EXTEND** | Its status machine (idle/loading/success/empty/unavailable/error) is exactly the shape a worklist hook needs |
| `PreferencesDropdown` | 🟡 **EXTEND** | Channel opt-out + windows map onto lane filters; keep the explicit-save UX |
| `briefingService` | 🟡 **PARTIAL RETIRE** | Keep `fetchBriefingLive`, `emailBriefingToColleague`, `getDocumentPreviewUrl`; retire the legacy `fetchAiBriefing` / `fetchBriefingNarration` |
| `HighPrioritySection` | 🔻 **RETIRE as a list, KEEP the template** | BR-3 retires *Critical Today*. Its **row layout** (kind chip · name link · description · action badge · reason chip · ⋮) is the visual template for the new row |
| `StatTiles` · `ChannelHeading` · `NarrativeBullet` · `ActivityNotesSection` · `DailyBriefingApp` | 🔻 **SUPERSEDED** | Replaced by the row + lanes. **Carry forward first**: the overdue+upcoming task-channel merge (`ActivityNotesSection.tsx:220-252`), the Documents-channel preview gating (L267-299), and `DailyBriefingApp`'s Xrm frame-walking, email-share and `handleOpenRecord` 403-fallback patterns — none has another home yet |
| `SubRow*` family · `CaughtUpFooter` | ❌ **RETIRE — already unreachable** | `CaughtUpFooter` is always called with `channelLabels={[]}` (`DailyBriefingApp.tsx:824`); the `SubRow` family is never reached in the `/render` path |
| `dailyBriefing.registration.ts` | ⏸ **SUPERSEDED, scheduled** | Per BR-4, keep until both lanes exist |

**Good news on coupling**: **no** SpaarkeAi or LegalWorkspace coupling anywhere in the package — every import is
`@spaarke/ui-components`, `@spaarke/auth`, `@fluentui/*` or intra-package. `SPAARKEAI-COMPONENTIZATION-AUDIT.md`
§2 cites the Daily Briefing hoist as the **success precedent**, not as debt.

## 5. `DailyBriefingCollector` — BR-2 is a predicate migration, not a rewrite

| Method | Entity | Membership source |
|---|---|---|
| `ResolveMembershipsSafelyAsync` (`:630`) | — | ✅ **already declarative** — `IMembershipResolverService`, metadata-driven |
| `QueryOverdueTasksAsync` / `QueryUpcomingTasksAsync` (`:661`,`:689`,`:722`) | `sprk_event` | ⚠️ hardcoded `sprk_eventtype_ref = <Task GUID>` (`:101`,`:756`), `statuscode = Open` (`:104`,`:757`), **`TaskOverdueDaysPast = 5` as a C# constant** (`:116`). Date windows ARE user-configurable |
| `QueryDocumentsAsync` (`:802`) · `QueryMattersAsync` (`:871`) · `QueryProjectsAsync` (`:927`) | `sprk_document` / `sprk_matter` / `sprk_project` | Resolver-derived set **+** hardcoded `modifiedon >= cutoff` / `statecode = Active` |
| `QueryTodosAsync` (`:999`) | `sprk_todo` | ⚠️ **hardcoded `owninguser = systemUserId`, no resolver call** — `sprk_todo` carries no membership-bearing fields |
| `CollectHighPriorityAsync` (`:337`,`:403-441`,`:473`) | 7 entities via a static `HighPriorityEntitySpecs[]` | ⚠️ **fully hardcoded** `sprk_highpriority = true OR sprk_monitor = true` (`:500-502`) |

**The shape of the change**: the query *shape* — entities, columns, joins — is reused **verbatim**. Only the
*predicates* move out of C# into `sprk_policy` / `sprk_policyversion` rows: the task-type GUID, `statuscode`,
the 5-day overdue constant, and the `highpriority OR monitor` condition. Stating this as a **predicate
migration** rather than a collector rewrite is what keeps BR-2 small.

## 6. Reuse targets on the server side

| Need | Reuse | Evidence |
|---|---|---|
| **Scope matching for the new evaluator** | **`CommunicationRuleGate`, copied verbatim** (`Services/Communication/CommunicationRuleGate.cs:69-209`) — blank rule tenant = all tenants (`:175-181`); empty rule matter = all matters (`:183-188`); `OrderBy(sprk_priority ?? 500).ThenBy(r => r.Id)` (`:129-133`); **fail-closed** on a rule-store read failure, returning `Deny(…, "rule-store-read-failed")` rather than defaulting to allow (`:108-126`) | This — **not** `ISignalRule` — is the real reference implementation |
| **Idempotent signal upsert** | ⚠️ **Cannot be reused as-is.** `GenerateDeterministicId(matterId, signalType)` (`SignalEvaluationService.cs:241-258`) XORs the matter GUID with the signal-type int. The key is **matter + signalType only**, with no room for a polymorphic subject — exactly the CM-9 dedupe problem, now with a file:line | |
| **EXISTS in FetchXML** | `DataversePrecedentBoard.cs:182-190` | INNER `AddLink` + `LinkCriteria.AddCondition` |
| **NOT EXISTS in FetchXML** | 🔴 **No prior art** | New code, no template |
| **Gate + dispatch** | `ConfirmationPolicyEngine.Evaluate` (`:98-125`) → `GateDecisionV2` | ⚠️ **Two enums, do not conflate**: `GateOutcome` has **5** values (`Execute`, `ExecuteWithUndo`, `ConfirmDialog`, `Elicit`, `HonestBlock` — `GateDecisionV2.cs:139-155`); the **8 dispositions** are `BindingDisposition` (`Binding.cs:148-186`): Informational · WorkProduct · Overlay · Email · Record · Notification · Compose · SurfaceLaunch |

## 7. `InsightArtifact` is a DTO — D-7 is settled by this

`InsightArtifact` (`Models/Insights/InsightArtifact.cs:25-154`) is an abstract `[JsonPolymorphic]` **record** with
four subtypes (`Fact` / `Observation` / `Precedent` / `Inference`), a `JsonElement` value and provenance fields —
**no lifecycle, no state**. Persistence: only `ObservationArtifact`, and only as a **side-effect mirror** into the
*generic* `sprk_analysis` table, discriminated by a borrowed column (`sprk_searchprofile = "insights-observation@v1"`)
because `sprk_analysis` has no discriminator of its own (`DataverseObservationMirror.cs:100-206`). `FactArtifact`
and `InferenceArtifact` are **never persisted**.

So a Signal — a durable row with `Open → Acted / Dismissed / ConditionCleared / Superseded / PolicyRetired`,
dedupe and suppression — is **not** an `InsightArtifact` subtype. **D-7 resolves to sibling**, and nothing needs
migrating for it to.
