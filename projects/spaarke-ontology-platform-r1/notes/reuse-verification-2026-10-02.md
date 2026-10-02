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

---

## 8. Wider duplication + dead-code audit — 2026-10-02

Commissioned under the owner's instruction: *"this is very important/critical so let's ensure we have this fully
analyzed and well documented; also if there is other related clean up that is identified — even if not directly
part of or caused by this project we need to address it in this project and not defer or hand off."*

**Method note.** The top-level delegated audit **failed** — it fanned out and returned status updates rather than
findings, five times. Its **sub-agents did report**, and their findings are folded in here; the load-bearing ones
were **re-verified directly** before being written down, which is how §8.2 D1 was corrected *in our favour*.
§8.6 records a method finding worth more than any single row.

### 8.1 Correction to §3 — the dismiss hazard is real but smaller

§3 claimed the three dead briefing hooks are *"still barrel-exported."* **Verified false.**

| Claim | Verified |
|---|---|
| Exported from `Spaarke.DailyBriefing.Components/src/index.ts` | ❌ **Not in the barrel** |
| Importable by mistake | ⚠️ Deep-path only — a far weaker footgun |
| Consumed anywhere in `src/` | ❌ Only two hits, both **comments** |

**The real hazard is those two comments**, which mislead a reader rather than a compiler:
`LegalWorkspace/.../dailyBriefing.registration.ts:48` (*"fetches via `useBriefingNotifications`"*) and
`SpaarkeAi/src/main.tsx:252` (*"`appnotification` directly via `useBriefingNotifications(webApi)`"*). Both are
stale — the live path is `fetchBriefingLive` behind `USE_LIVE_RENDER=true` (`briefingService.ts:397`). Anyone
reading either to learn how the Briefing gets its data is told the wrong mechanism and pointed at
`appnotification` read-state as though it were the dismiss path. **Severity 🟡 (was 🔴).**

### 8.2 Verified duplication — components

| # | Finding | Evidence | Canonical | Size |
|---|---|---|---|---|
| **D1** | 🔴 **A dead, superseded fork that is also the *broken* copy** — *corrected from "two parallel implementations"* | `Spaarke.UI.Components/.../DataGrid/columnHeader/ColumnHeaderMenu.tsx:9` states **"Lifted from `@spaarke/events-components/.../ColumnHeaderMenu.tsx`"** with two material changes, one the **NFR-03/ADR-021 portal fix** (`applyStylesToPortals`, *"which fails in MDA dark mode"*). The Events original (573 lines) is imported **only by its own barrels** | **Delete the Events copy.** Not a merge — it is the older fork **missing the dark-mode fix**, so adopting it inherits a known bug | **XS** *(mis-sized M)* |
| **D2** | **Row ⋮ menus: four in shared libs + one in app code** | `DocumentRowMenu.tsx:150-208` · `NarrativeBullet.tsx:600-645` · `HighPrioritySection.tsx:298-325` · `EmailConnectionsReview.tsx` · **`SpaarkeAi/src/components/workspace/ManageWorkspacesPane.tsx:1091-1208`** | **`DocumentRowMenu`** — its docstring claims the canonical role and `NarrativeBullet.tsx:73-74` **cites that claim while not using it** | M |
| **D3** | Two components named `MetricCard` in two libraries | `UI.Components/.../WorkspaceShell/MetricCard.tsx` (221) vs `Visuals/src/components/MetricCard.tsx` (410) | ⚠️ **Name collision, not duplication** — Visuals serves the `VisualHost` PCF. Always cite the full path | XS |
| **D4** | Card/tile family is **seven wide** | `StatTiles` · `WorkspaceShell/{MetricCard,MetricCardRow,ActionCard,ActionCardRow}` · `RelationshipCountCard` · `Visuals/{MetricCard,MetricCardMatrix}` | For count filters: `WorkspaceShell/MetricCard` + `MetricCardRow`. No eighth card | XS |
| **D5** | **Pane-header "⋮ tools" menu built three times** | `AssistantToolMenu.tsx` · `WorkspacePaneMenu.tsx` · `ContextPaneMenu.tsx` | Extract one. ⓘ **Self-aware**: the three files' comments cross-reference *each other and CLAUDE.md §11* — and it still was never extracted. Naming the rule is not enough without a scheduled item | S |
| **D6** | **Three empty states, same shape** (icon + heading + description, centred, `role="status"`) | `DailyBriefing/EmptyState.tsx` · `SmartToDo.tsx:194` · `PlaybookGalleryWidget.tsx:308` | Hoist one into `Spaarke.UI.Components` | S |
| **D7** | **Four helpers copy-pasted verbatim** — *admitted in a code comment* | `derivePriorityGlyph` · `deriveEffortBadge` · `DUE_BADGE_STYLE` · `formatDueDate`, across `components/KanbanCard/KanbanCard.tsx` and `components/SmartToDo/KanbanCard.tsx` | Move to the **already-shared `todoScoring.ts`**. The two *components* stay separate (§8.5) | XS |
| **D8** | `ChannelHeading` redundant | `ChannelHeading.tsx:51-79` vs `SectionPanel.tsx`'s title bar (title + `badgeCount`) | `SectionPanel` | XS |
| **D9** | Two near-duplicate **"/" command palettes** | `SprkChatActionMenu.tsx` · `SlashCommandMenu.tsx` | One | S |
| **D10** | Three toolbar + overflow command bars | `Toolbar/CommandToolbar.tsx` · `PageChrome/CommandBar.tsx` · `DataGrid/commandBar/CommandBar.tsx` | ⚠️ **Partial only** — the grid version has genuine extra needs. Record as a deliberate non-merge | M, low |

### 8.3 Verified duplication — utility logic (the largest finding by volume)

| # | Finding | Scale | Canonical home |
|---|---|---|---|
| **U1** | 🔴 **`cleanGuid` — the brace-stripping one-liner is reimplemented ~70 times** | `PolymorphicResolverService.ts:347-366` carries a comment calling itself *"the ONE place braces get stripped."* **That claim is false**: `id.replace(/[{}]/g,'')...` is independently rewritten at **~70 sites**, including **~25 inside its own owning package** (`EventTypeService.ts:695`, `userLookup.ts:256`, `DataGrid.tsx:563`, `CreateMatterWizard.tsx:110-111`, `PolymorphicPicker.tsx:262`, the whole `EmailComposer/**` set …), **~14 in `Spaarke.Communication.Components`** (which already imports `@spaarke/ui-components`), 7 in `AI.Widgets`, 4 in `Events.Components`, 2 in `DailyBriefing`. A 4th *variant* (`replace(/^\{|\}$/g,'')`) appears at `SummarizeAnalysisStep.tsx:91` | Relocate to **`Spaarke.UI.Components/src/utils/guid.ts`** and barrel-export it — it is currently buried in a resolver service, which is why nobody finds it |
| **U2** | 🔴 **The Xrm 3-frame walk — 7 implementations** | Canonical `utils/xrmContext.ts:306-344` (`getXrm()`, 12+ consumers) **plus six duplicates, three in the same package**: `services/xrmGlobal.ts:19-39` (**same exported name**, different module, used by 3 `EmailComposer` files) · `PolymorphicPicker.tsx:201-205` · `SummarizeAnalysisStep.tsx:73-93` · `WorkspaceLayoutWidget.tsx:181-207` (its comment admits it *"mirrors LegalWorkspace's xrmProvider"* and never considered the available `@spaarke/ui-components` one) · `launchCreate.ts:65-89` · `DailyBriefingApp.tsx:290-312`. ⚠️ **This corrects §4 of this document**, which said `DailyBriefingApp`'s frame-walking *"has no other home yet"* — **a home exists; it simply is not used.** Only its `setInterval` retry is novel | **`utils/xrmContext.ts:306`** `getXrm()` |
| **U3** | **To-Do urgency/score computed three times across two packages** | `todoScoring.ts:62-95` · `useKanbanColumns.ts:84-113` (same package, duplication **documented** at `:74-78` as a deliberate local copy) · `UI.Components/.../TodoDetail/TodoDetail.tsx:87-121`, whose own doc at `:80-85` says it *"mirrors LegalWorkspace `computeTodoScore()` exactly"* — identical weights (0.5/0.2/0.3) and tier ladder | Hoist `todoScoring.ts` into `Spaarke.UI.Components/src/utils/`; all three consume it |
| **U4** | **Relative-time formatter, two independent versions, same name** | `ChatSessionCard.tsx:37-61` uses `Intl.RelativeTimeFormat` with second→month buckets and handles future dates; `TldrSection.tsx:243-255` hand-rolls past-tense only, no week/month cap | Hoist the **`Intl`** version to `Spaarke.UI.Components/src/utils/` |
| **U5** | The local-midnight day-diff idiom written **five** times | `formatDueDate.ts:19-42` · `useKanbanColumns.ts:139-152` · `todoScoring.ts:106-139` · `ConversationView.tsx:598-610` · the above | One `daysBetweenLocalMidnight(a,b)` collapses all five |
| **U6** | ⚠️ **Not duplication — inconsistency.** Urgency tier schemes disagree | SmartTodo uses **3/7/10-day** cutoffs; `EventDueDateCard.tsx:131-158` uses **3/5-day** | Worth a deliberate decision when the Do lane lands, since both will be on screen together |

### 8.4 Dead-but-exported, ranked by whether mis-wiring causes a WRONG SIDE EFFECT

The dangerous class is not "unused" — it is **"looks canonical, is a stale fork."**

| # | Symbol | Why dangerous |
|---|---|---|
| **X1** | 🔴 `composeCommentThreadsToDocxAnnotations` (`ComposeCommentThread.types.ts:157` → `index.ts:144`) | **The worst find in this audit. This function already caused silent data loss once.** Its own docstring (`:217`) records that the replacement *"REPLACES"* it because its `DocxAnnotationInput`/`targetText` shape rode a retired save field *"which the server's `SaveComposeDocumentBody` never deserialized (every comment sent that way was silently dropped)."* It was abandoned — **and is still barrel-exported**, inviting re-wiring into the identical trap. Its only caller is its own unit test |
| **X2** | 🔴 `anchoredAnnotationsToDocxAnnotations` (`useComposeWordShuttle.ts:162` → `index.ts:208`) | **Zero real callers** — only its own JSDoc and seven `() => []` jest mocks. A dev adding "export AI suggestions to Word" would match it by name and shape and **silently bypass** the live mark-based mechanism (`redlineMarksToDocxAnnotations`) |
| **X3** | 🔴 `LegalWorkspaceRenderer` (`LegalWorkspace/src/index.ts:69` → `Spaarke.LegalWorkspace/src/index.ts:88`) | Its docstring **prescribes it as *the* registration route** — `setDefaultWorkspaceRenderer(LegalWorkspaceRenderer)` — including a paragraph on an *"operator architectural decision."* The real consumer, `SpaarkeAi/src/main.tsx`, does **not** use it; it wires `LegalWorkspaceApp` + `createLegalWorkspaceSectionRegistry` directly (Option D). Following the docstring **bypasses the composition SpaarkeAi depends on.** Zero call sites |
| **X4** | 🔴 `Spaarke.AI.Context` — `useChatSession`, `useChatContextMapping`, `useChatPlaybooks`, `ChatApiClient` (whole `hooks` + `services` barrels) | Doc-comments say *"extracted from `SprkChat` for reuse across … future AI surfaces"* — **the extraction was never adopted.** `SprkChat` still uses **its own local hooks of identical names**. A new surface author imports the "canonical extracted" version and gets a **stale fork** |
| **X5** | 🟠 `ChatHistoryPanel`, `ChatSessionCard`, `useChatHistoryFilter` (`AI.Outputs` `chat-history` → `index.ts:26`) | Built as *the* chat-history panel; SpaarkeAi's real UI (`HistoryOverlay`) was built from scratch and iterated **three times** without it |
| **X6** | 🟠 `TodoDetailPane` (`TodoDetailPane.tsx:230`, barrel-exported at three levels) | A **443-line ready-made** detail pane with `openRecord` navigation and action stubs, exported at the package root — **and never rendered**. A dev adding "click a card to expand" finds it ready-made, unaware it may be stale against the current `ITodo`/scoring schema since nothing exercises it |
| **X7** | 🟠 `@spaarke/visuals` package barrel (boundary drift) | Its own header says *"Consumers: `src/client/pcf/VisualHost/` (repoint in VHVU-060)"* — **the repoint never happened.** `VisualHost` imports everything by **deep relative path**; `from '@spaarke/visuals'` appears **zero** times in `src/`. Following the README and importing by name yields a **second module graph** — dual React/Fluent instance risk |
| **X8** | 🟠 `AiContextConfig` (`types/index.ts:221`) | **Dangling reference**: its doc says *"Passed to `AiContextProvider` at the application root"* and **`AiContextProvider` was deleted**. A reader could try to rebuild a bootstrap that no longer exists |
| **X9** | 🟠 Events `ColumnHeaderMenu` — see D1 | Dangerous for a second reason: it is the copy **without** the dark-mode portal fix |
| **X10** | 🟡 Merely dead, no runtime risk | `TrendCard` (`Visuals`, zero consumers even relatively) · `ThresholdSettings` (byte-identical alias of `ThresholdSettingsPopover`) · `ComposeStylesPane` + `useComposeDocumentStyles` + `deriveDocumentStyles` + `applyComposeDocumentStyle` (**intentionally** unmounted, `ComposeEditor.tsx:4090-4092`) · `ChatSessionContext`, `AiAuthContext`, `AnalysisAiContextShape`, `AiWidgetDescriptor` and ~20 `./chat` types |

### 8.5 Verified NOT a problem — this section prevents destructive cleanup

| Item | Verdict |
|---|---|
| 🔴 **The 7 `output-widgets` + 6 `source-widgets` in `Spaarke.AI.Outputs`** | **LIVE — a naive dead-export sweep would have deleted working widgets.** Their named barrel exports have no importers, but `register-workspace-widgets.ts:256-421` lazy-`import()`s each by **deep subpath**, and that registry is pulled in as a side effect by `Spaarke.AI.Widgets/src/index.ts:7-8` |
| `KanbanHeader`, `AddTodoBar`, `DismissedSection`, `ThresholdSettingsPopover`, `TodoAISummaryDialog`, `PriorityScoreCard`, `EffortScoreCard` | Reachable via `SmartToDo.tsx:46-64`, which **is** externally consumed (`LegalWorkspace/.../SmartToDo.tsx:18`) |
| `Spaarke.Notifications` internals (`negotiate`, `connectSignalR`, `KindRouter`, `startPollFallback`) | Used internally by `NotificationsClient.ts:2-4,89,158`; barrel-exported **intentionally for tests** |
| `useComposeCommentThreads` | **LIVE** (`ComposeEditor.tsx:2883`, rendered `:3820` via the toolbar's Add-Comment toggle). ⚠️ A **stale comment at `ComposeEditor.tsx:4085-4089`** claims it is *"unreachable from UI"* — only the standalone FAB was removed. Another instance of a comment lying about the live path |
| `HistoryOverlay`'s per-row `<Popover>` instead of `<Menu>` | **Legitimate and documented**: Fluent v9 treats a nested `<Menu>` in a `MenuList` as a submenu and auto-closes the parent |
| `ComposeEmptyState` | **Legitimately distinct — and self-justified in its own JSDoc, which runs the CLAUDE.md §11 three-question test.** The exemplar for how a new component should document itself |
| The two `KanbanCard` **components** | **Deliberately separate**, alias-exported to avoid collision, with in-code rationale. Only the four *helpers* (D7) duplicate |
| `PolymorphicResolverService` for `sprk_regarding*` resolution | **Genuinely canonical, zero reimplementations.** `ConnectionsWriteHandler.ts:4` wraps it; the reconciliation readers do something different (reading already-resolved lookups), not competing |
| `ConfidenceIndicator` | **Sole implementation.** `ThresholdSettings` (todo bucketing) and `CitationBadge` (verification tier) are different domains, ruled out |
| `useDocumentActions` (`DocumentOperations`) | 3 real importers, no equivalent elsewhere — §11 clean |
| **Status / severity badges** | **No duplication — and a genuine gap.** Only domain-specific badges exist (`CitationBadge`, `PinnedMemoryProvenanceBadge`, `ChannelBadge`), so the **Signal-status badge is legitimately new** and belongs in `Spaarke.UI.Components` |
| `MenuTrigger` across ~20 shared files | Mostly legitimate — toolbars, column headers, pickers, selectors. **Only D2 and D5 are duplicated clusters.** Do not run a "consolidate all menus" project |

### 8.6 Method finding — use the import specifier, never the symbol name

A plain name-grep **over-counts badly**, because packages define near-identically-named symbols independently
(`Spaarke.AI.Context`'s chat types vs `Spaarke.UI.Components/SprkChat`'s; two `getXrm()`; two `MetricCard`; two
`formatRelativeTime`). The reliable signal is the **literal import specifier** — `from '@spaarke/ai-context'`
surfaced just **two** real consumer files for that entire package.

And the inverse trap: **a symbol with no barrel importer can still be live** via lazy deep-subpath import
(§8.5, the 13 widgets). **Any future dead-export sweep must check both directions**, or it will simultaneously
miss stale forks and delete working code.

### 8.7 Schedule, do not defer — work items

In this project per §5.0. **Prereq** = must land before the worklist row component is built.

| ID | Work item | Size | Prereq? |
|---|---|---|---|
| **C-1** | Delete the three dead briefing hooks + the `notificationService` methods they back; **correct the two stale comments** misdescribing the live data path | XS | ✅ |
| **C-2** | **Delete the superseded Events `ColumnHeaderMenu` fork** — 573 lines carrying a known dark-mode bug | XS | — |
| **C-3** | Disambiguate `MetricCard` (full paths in §1.3; consider renaming the Visuals one) | XS | ✅ |
| **C-4** | Add a generic **status/severity badge** to `Spaarke.UI.Components`; use it for Signal status | S | ✅ |
| **C-5** | 🔴 **Delete `composeCommentThreadsToDocxAnnotations` and `anchoredAnnotationsToDocxAnnotations`** — the first already caused silent comment loss on save and is still exported | XS | — |
| **C-6** | 🔴 **Delete or adopt `LegalWorkspaceRenderer`** — its docstring prescribes a registration route that bypasses what SpaarkeAi actually depends on | XS | — |
| **C-7** | 🔴 Relocate **`cleanGuid`** to `Spaarke.UI.Components/src/utils/guid.ts` + barrel; **delete the false "ONE place" comment**; migrate call sites opportunistically (~70 — do **not** attempt in one pass) | S + ongoing | — |
| **C-8** | 🔴 Collapse the **six duplicate Xrm frame-walks** onto `xrmContext.ts:306`, starting with `DailyBriefingApp.tsx` and `WorkspaceLayoutWidget.tsx` (both already depend on `@spaarke/ui-components`) | S | ⚠️ partly — the row runs in the same host |
| **C-9** | Extract a shared **`RowActionMenu`** from `DocumentRowMenu`; migrate the four other row menus | M | ⚠️ partly — build the row **against `DocumentRowMenu`** |
| **C-10** | Hoist `todoScoring.ts` to `Spaarke.UI.Components/src/utils/`; delete the two mirrored copies; move D7's four Kanban helpers in | S | — |
| **C-11** | Hoist one `EmptyState`; migrate the three hand-rolls | S | — |
| **C-12** | Extract one pane-header "⋮ tools" menu; migrate the three | S | — |
| **C-13** | Hoist the `Intl.RelativeTimeFormat` relative-time formatter; add `daysBetweenLocalMidnight`; retire U5's five copies | S | — |
| **C-14** | Delete `TodoDetailPane`, `TrendCard`, `ThresholdSettings` alias, `AiContextConfig` + X10's dead types; **delete or adopt** the `AI.Context` hooks/`ChatApiClient` and the `chat-history` trio | S | — |
| **C-15** | Fix the **`@spaarke/visuals` boundary drift** — either repoint `VisualHost` to the package name or delete the misleading barrel + README claim | S | — |
| **C-16** | Correct the stale comment at `ComposeEditor.tsx:4085-4089` (claims `useComposeCommentThreads` is unreachable; it is live) | XS | — |
| **C-17** | Decide U6's tier scheme (3/7/10 vs 3/5 days) before Do-lane items and event cards share a screen | XS | ⚠️ |
| **C-18** | Remaining unaudited: Fluent-token/hardcoded-colour sweep in shared libs; `EmailCardList`/`MessageRow`/`ChatSessionCard` vs `RecordCardShell` (`UNVERIFIED`); `SECTION_REGISTRY` external liveness (`UNVERIFIED`) | S each | — |

**Shape**: ten XS/S items plus one M (C-9) and one ongoing migration (C-7). **Only C-1, C-3, C-4 gate the row**;
C-8 and C-9 are partial. Three items — **C-5, C-6, C-2** — are each XS and each remove a hazard that has already
either caused data loss or carries a known bug, which is the clearest argument that absorbing this work now is
cheaper than handing it off.

### 8.8 Three further packages — audited after §8.7 was drafted

Reports for `Spaarke.UI.Components`, `Spaarke.AI.Widgets` and `Spaarke.Communication`/`Events.Components`
landed last. They raise the hazard count materially, and one of them **contradicts root `CLAUDE.md`**.

#### `Spaarke.UI.Components`

| # | Finding | Why dangerous |
|---|---|---|
| **X11** | 🔴 **`CommandRegistry` / `EntityConfigurationService` / `CustomCommandFactory` / `Toolbar/CommandToolbar` / `useKeyboardShortcuts`** — leftover command-bar infra from the **deleted** UniversalDatasetGrid PCF (`src/client/pcf/CLAUDE.md`: *"⛔ DELETED. Not in the repo"*) | **The worst side-effect risk in the audit.** `CommandRegistry.deleteCommand()` (`services/CommandRegistry.ts:80-113`) loops `await context.webAPI.deleteRecord(...)` over **every selected record** — a real bulk delete. The cluster reads as the generic, privilege-aware command builder and sits **right beside the genuinely live `CommandExecutor`**, so a dev adding a grid toolbar could wire `getCommandsWithCustom(...)` and ship an **untested, unreviewed bulk-delete**. `CommandToolbar` takes host-supplied `commands` and never calls the registry; zero real consumers |
| **X12** | 🔴 **`wizardRegistry` / `resolveWizard`** (`components/WizardRegistry/wizardRegistry.ts:129,185`) | Its header instructs: *"Adding a new wizard = one new entry in `wizardRegistry` below. No other Visual Host code changes."* **The live consumer does not import it** — `VisualHostRoot.tsx:70-117` keeps a parallel `WIZARD_KEY_TO_PAGE` map with a comment explaining the deliberate divergence (bundle weight). **Following the module's own instruction produces a silent no-op** |
| **X13** | 🟠 `PcfDataverseClient` (`services/document-upload/PcfDataverseClient.ts:21`) | Presented as the PCF-side counterpart to the live `ODataDataverseClient`, implements the same `IDataverseClient` — but has **zero instantiation sites** and is the one sibling `pcf-safe.ts:52-55` **deliberately omits**. Untested Dataverse **write path** |
| **X14** | 🟡 Merely dead | `useDocumentMultiSelect` (zero hits, not even a test) · `DraftSummaryFollowOnStep` (built alongside four siblings that *are* wired; never mounted) · `cancelHandoff` — **self-documented as intentional** (`readHandoff.ts:125-127`: the cancel path deliberately writes nothing and the orchestrator infers cancellation), so leave it |

✅ **Verified live — do not re-flag**: all six `SprkModal` presets, the other four `WizardFollowOns` steps,
`AccessGrantModal`, the `EmailComposer` wrappers, and every other `services/document-upload` class.

#### `Spaarke.AI.Widgets`

| # | Finding | Why dangerous |
|---|---|---|
| **X15** | 🔴 **The Pillar-9 `getAgentVisibleState` machinery is dead** — `getWorkspaceWidgetVisibleStateFn` (`registry/WorkspaceWidgetRegistry.ts:446`), all of `widgets/workspace/pillar9-visibility.ts`, and the per-widget visibility wrappers on 8+ registrations | It is an **ADR-015-governed privacy contract** for what reaches the agent's system prompt — it could not look more load-bearing. It is **never called in production**: `SprkChatAgentFactory.cs:1506-1726` **re-derives the same shapes server-side** from raw `WorkspaceTabWidgetData` (*"server derives FR-57 shapes directly… not by trusting client serialization"*). **A developer fixing a privacy bug here changes nothing at runtime** |
| **X16** | 🔴 **`InsightSummaryCard` + `ConfidenceIndicator`, `FeedbackButtons`, `SafetyAnnotationOverlay`, `CitationBadge`, `GroundednessHighlight`** | `src/dataverse/forms/sprk_matter/insightCardMount.ts:476-482` mounts via `window.SpaarkeAiWidgets.mountInsightSummaryCard` — **that bundle does not exist in the repo.** Every link in the chain is present (FormXml patch, telemetry, mount glue, placeholder fallback) **except the one that makes it fire**, so production silently renders the *"Phase 4 placeholder"* forever. Anyone reading the form glue would reasonably conclude the card is live |
| **X17** | 🟠 `'get-started-cards'` context registration | Its own docblock admits the props shape is **not** the standard `ContextWidgetProps`, so `ContextPaneController` imports the component directly and never resolves it generically. If any future `context_update` SSE dispatches that literal, the cards render with **no `onCallback` wired** — inert buttons |
| **X18** | 🟡 Registered but never emitted | **6 of the 7 `output_pane` widgets** — only `SearchResults` is emitted server-side (`DocumentSearchHandler.cs:437,566`); plus `'redline-viewer'` (its comment says the compare-documents tool is *retired*) and `'matters-dashboard'` (no menu, no `surfaceLaunchRegistry` entry — contrast `my-tasks-list` at `surfaceLaunchRegistry.ts:147-151`) |

> ⚠️ **Reconciling X18 with §8.5.** Both are true at different layers, and conflating them would cause a bad
> delete. The 13 widget **components** are reachable — lazily imported by deep subpath from
> `register-workspace-widgets.ts`, so they are **not dead code**. What is dead is the **server ever asking for
> six of them**. So: *do not delete the components*; do record that six registrations have no producer. This is
> exactly the two-direction check §8.6 prescribes.

#### `Spaarke.Events.Components` — and a contradiction in root `CLAUDE.md`

| # | Finding | Why |
|---|---|---|
| **X19** | 🔴 **`CalendarFilterPane` — root `CLAUDE.md` describes a variant that is not wired** | Root `CLAUDE.md` lists *"two intentional Calendar variants"*: `CalendarSection` **and `CalendarFilterPane`**. But the one place it should be used — `src/solutions/CalendarSidePane/src/App.tsx:30-33` — imports **`CalendarSection` + `CalendarFilterOutput`**, the *other* variant. Worse, `parseParams.ts:20` and `postMessage.ts:15` (both live) `import type { CalendarFilterPaneOutput } from "@spaarke/events-components"` — **a type the package's barrels and `exports` map never surface.** An abandoned migration left looking live. **Root `CLAUDE.md` needs correcting** |
| **X20** | 🔴 `ColumnHeaderMenu` **and `ColumnFilterHeader`** (confirms and extends D1) | Both orphaned pre-DataGrid leftovers, both **name-colliding with live components of the same names** in `Spaarke.UI.Components/DataGrid`. `import { ColumnHeaderMenu } from '@spaarke/events-components'` by autocomplete yields a header that **renders but is disconnected** from live `<DataGrid hostFilters>` state — silent non-functional filter UI |
| **X21** | 🟡 A whole migration's worth of dead surface | `useEventsBulkActions` (doc claims two consumers; **both false**) · the **entire `FetchXmlService`** module (5 exports) · the `ViewSelectorDropdown` chain · five context selector hooks (`useCalendarFilter`, `useAssignedToFilter`, `useStatusFilter`, `useActiveEvent`, `useGridRefresh`) whose dispatch loop was removed — `CalendarWorkspaceWidget.tsx:462-464` admits the setters *"remain on context for any external integrations"* |

> **🔬 Root cause, and it is a pattern worth naming.** `src/solutions/EventsPage/` — this package's documented
> consumer — was **rewritten onto `@spaarke/ui-components`** (`DataGridPageShell`/`FetchXmlService`/`ColumnHeaderMenu`)
> and now has **zero source imports of `@spaarke/events-components`**, while still declaring the npm dependency.
> **One migration killed four feature areas at once and nobody pruned the barrel.** That is the mechanism behind
> most of this audit: not carelessness, but a migration that moved the consumer and left the producer exported.

#### ✅ `Spaarke.Communication.Components` — healthy

**No dangerous dead exports.** Both feature areas are live with independent consumers (`EmailPage`,
`CommunicationReconciliation`, three LegalWorkspace registrations, two AI.Widgets wrappers); every
`logic/connections` write-path function has a real caller. Only note: `buildDocumentLinkUrl` /
`projectSourceAttachment` are barrel-exported but called only internally — pure and harmless.

**This matters for §5.1**: the reconciliation surface we are mounting as a Console tab sits in the one package
the audit found clean.

### 8.9 Additional work items

| ID | Work item | Size | Prereq? |
|---|---|---|---|
| **C-19** | 🔴 **Delete the `CommandRegistry` cluster** (registry + `EntityConfigurationService` + `CustomCommandFactory` + `CommandToolbar` + `useKeyboardShortcuts`) — dead infra from a deleted PCF carrying a **loop-over-selection `deleteRecord`**. Highest side-effect risk found | S | — |
| **C-20** | 🔴 **Delete or wire `wizardRegistry`/`resolveWizard`** — its own header instructs an edit that is a silent no-op | XS | — |
| **C-21** | 🔴 **Resolve the Pillar-9 visibility shim (X15)** — delete it, or make the server call it. Leaving an ADR-015 privacy contract that looks enforcing and is bypassed is the worst shape of this class | S | — |
| **C-22** | 🔴 **Resolve `InsightSummaryCard` (X16)** — build the missing bundle or remove the mount glue + placeholder. Today production shows a placeholder forever and the code reads as live | S | — |
| **C-23** | 🔴 **Correct root `CLAUDE.md`'s Calendar entry (X19)**, and either promote `CalendarFilterPane` + `CalendarFilterPaneOutput` to the public surface or delete them and fix the two live type imports | XS | — |
| **C-24** | 🔴 **Delete the Events `ColumnFilterHeader`** alongside `ColumnHeaderMenu` (C-2) — same collision, same silent-no-op failure | XS | — |
| **C-25** | Prune the rest of the EventsPage-migration fallout — `useEventsBulkActions`, the whole `FetchXmlService` module, the `ViewSelectorDropdown` chain, the five dead context selectors; then **drop the unused `@spaarke/events-components` dependency from `EventsPage`** | S | — |
| **C-26** | Delete `PcfDataverseClient`, `useDocumentMultiSelect`, `DraftSummaryFollowOnStep`. **Keep `cancelHandoff`** — self-documented as intentional | XS | — |
| **C-27** | Record that **6 of 7 `output_pane` widgets have no server producer** (and `redline-viewer`, `matters-dashboard`). ⚠️ **Do not delete the components** — they are lazily reachable (§8.6) | XS | — |

**Revised shape**: ~20 XS/S items plus one M (C-9) and one ongoing migration (C-7). Still only **C-1, C-3, C-4**
gate the worklist row. But the hazard list now includes **an untested bulk-delete**, **a bypassed privacy
contract**, **a render path that silently shows a placeholder in production**, **a function that already caused
silent data loss**, and **a root-`CLAUDE.md` statement that is not true** — none of which this project caused, and
all of which it found. That is the case for absorbing the work rather than filing it.

### 8.10 One finding is not debt — it is a live bug

The To-Do urgency scorer's three copies (U3) have **already drifted**.

`todoScoring.ts:71-75` carries a **date-only local-midnight fix**. The duplicate at
`useKanbanColumns.ts:85-89` is a **stale fork that never received it**. The two therefore **disagree by one day
in negative-UTC-offset time zones** — which is every US user.

So U3 is not deferred cleanup: **it is producing wrong output today**, and a To-Do can sit in a different Kanban
column than its own detail view says it should. The third copy (`TodoDetail.tsx:87-121`) advertises itself as
mirroring the others *"exactly"*, which is how the drift stayed invisible.

**This changes C-10's priority**: it is a **bug fix**, not a refactor, and it is independent of the worklist. It
also sharpens the general lesson — *"mirrors X exactly"* in a doc comment is an assertion about code that drifts,
and the only durable fix is one implementation plus a forcing-function test, the shape
`xrmContext.ts:373-397` (`getXrmPage`) already demonstrates with its import-asserting test at
`hooks/__tests__/useRecordHeaderFields.test.ts:450-451`.

Two further specifics from the same report, worth recording so they are not re-derived:

- **Two divergent brace regexes are in circulation** — `/[{}]/g` (canonical) vs `/^\{|\}$/g`
  (`SummarizeAnalysisStep.tsx:91`). The second only strips *outer* braces, so it is a real behavioural
  difference, not a style variant.
- **`DUE_BADGE_STYLE` is copied four times inside one package** — `components/KanbanCard/KanbanCard.tsx:91-99`,
  `SmartToDo/KanbanCard.tsx:37-41`, `SmartToDo/TodoDetailPane.tsx:91-99`, `SmartToDo/DismissedSection.tsx:65-73`.
- ⓘ **`Spaarke.LegalWorkspace` does not exist as a package directory** in this worktree (consistent with the
  documented retirement), so `WorkspaceLayoutWidget.tsx:174-177`'s *"mirrors LegalWorkspace's xrmProvider"*
  comment points at nothing.
