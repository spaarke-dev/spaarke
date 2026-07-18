# messaging-communication-app-r2 — Communication Workspace (DRAFT design)

> **Status**: DRAFT for review · **Author**: investigation-grounded synthesis (5 parallel resource audits, 2026-07-18)
> **Builds on**: `messaging-communication-app-r1` (shipped: ACS thread model + per-thread Timeline PCF + send/respond + impersonation read model — deployed to `spaarke-bff-dev`).
> **Coordinates with**: `ai-spaarke-ai-workspace-UI-r2` (already shipped the Communications DataGrid config + a thin workspace widget — see §3).

---

## 1. Problem & goals

Users expect a **communication workspace**, not just a per-thread viewer. From UAT:

1. **Conversations related to a record** — a Matter (or any regarding-family record) shows its message conversations.
2. **Multiple threads per record, back/forth** — e.g. *Jun: assignment · Feb: progress · Jul: resolution* — each a distinct, named conversation.
3. **Emails + messages unified** — both associated to the record AND to a thread, shown together.
4. **A global "all communications" view** — filter/organize by thread, record, **person**, channel, date range.

## 2. Key insight — the R1 data model already supports reqs 1–3; R2 is mostly *read + UI*

R1 built the load-bearing structure, so **no schema migration** is required for the core experience:

- Every message is `sprk_communication` with the ADR-024 regarding family (incl. typed `sprk_regardingmatter` lookup) → **already record-associated** (req 1).
- A thread (`sprk_communicationthread`) is a first-class record anchored to the regarding record; a record can have **N threads** → *Jun/Feb/Jul* already works as data (req 2).
- `IThreadResolver` is **channel-agnostic** — email replies join via `sprk_inreplyto` ancestry, chat via ACS thread id; both are the same entity, same threads → **email+chat unified** (req 3).

The gaps are: (a) **read/query surface** across threads (by-regarding + filtered query), (b) **UI surfaces** to render it, (c) two **schema deltas** (thread regarding-resolver treatment + a person/participant index), and (d) an **auto-threading policy** so messages always land in a sensible thread.

## 3. Existing resources — REUSE, do not rebuild (investigation results)

**A prior project already built part of this.** R2 must extend, not duplicate (root CLAUDE.md §11).

| Capability | Status | Location |
|---|---|---|
| **Communications DataGrid config** (`Active Communications`) | ✅ **Shipped** | `sprk_gridconfiguration` GUID `e1826c4c-9575-f111-ab0e-7ced8ddc4a05` (from `ai-spaarke-ai-workspace-UI-r2`) |
| **Thin `communications-list` workspace widget** (grid-only) | ✅ **Shipped** | `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/register-workspace-widgets.ts` + `src/solutions/LegalWorkspace/src/sections/communications.registration.ts` |
| **Per-thread Timeline** (polling, compose, reply-nesting) | ✅ **Shipped (R1)** | `src/client/shared/Spaarke.UI.Components/src/components/CommunicationTimeline/` + PCF `src/client/pcf/CommunicationTimeline/` |
| **Impersonation read model + access filter** | ✅ **Shipped (R1)** | `Services/Communication/CommunicationThreadReadService.cs`, `IImpersonatedCommunicationQuery.cs` (entity-set-agnostic), `Access/CommunicationAccessFilter.cs` |
| **RegardingResolver PCF** (entity-agnostic, FR-22) | ✅ **Shipped** | `src/client/pcf/RegardingResolver/` — attaches to any form via `entity` prop |
| **VisualHost config-driven panel framework** | ✅ **Shipped** | `src/client/pcf/VisualHost/` + config entity `sprk_chartdefinition` (Matter Health/Budget/Tasks patterns) |
| **DataGrid framework** (`<DataGrid configId=>`, chips, views) | ✅ **Shipped** | `src/client/shared/Spaarke.UI.Components/src/components/DataGrid/` + `sprk_gridconfiguration` |
| **Membership derivation** (ADR-034 reverse; write-side) | ✅ **Shipped (R1)** | `Services/Communication/Membership/ThreadMembershipDerivationService.cs` |
| **Participant index (person→communications)** | ❌ **Missing** | sender/recipients are `;`-separated TEXT (`sprk_from/to/cc`); no junction table |
| **by-regarding / filtered read endpoints** | ❌ **Missing** | read surface is thread-id-scoped only |
| **Thread typed-regarding lookups + naming/tags** | ❌ **Missing** | thread has denormalized *text* regarding only; no category/tags |

**Net: surfaces 2 & 3 are ~80% scaffolded.** R2's genuinely-new work is surface 1 (record-level threads view), the two schema deltas, the read endpoints, the person filter, and auto-threading.

## 4. Scope — three surfaces + four cross-cutting capabilities

### Surface 1 — Record-level "Communications" (on the Matter/regarding record) — **NEW**
Two complementary pieces:

- **1a. Regarding-mode Timeline (primary).** Extend the shipped `CommunicationTimeline` component with a **regarding mode**: given a regarding record (`entityType`,`id`), it renders the record's **threads as collapsible groups** (*Jun: assignment · Feb: progress · Jul: resolution*), each expanding to its message timeline (email+chat interleaved). It calls the **new `by-regarding` BFF endpoint** (§4 cross-cutting) so it inherits the **impersonation access filter** — full privacy parity with R1. This directly answers "how does the timeline show on the Matter." Delivered as a form-bound PCF (mirror the R1 `CommunicationTimeline` PCF, add a regarding-resolution path). *Reqs 1–3.*
- **1b. VisualHost summary card (optional, lightweight).** A `sprk_chartdefinition` config for an "at-a-glance" MetricCard ("12 messages · 3 unread") on the Matter form, drill-through to the threads view. **Config-only** if a count MetricCard suffices. ⚠️ **Design tension** (see §6): VisualHost containers fetch **client-side via `Xrm.WebApi`**, which honors Dataverse row-level security natively (private threads hidden) but does **not** apply R1's `internal-only` rule — acceptable for a count card, not for message bodies. Prefer 1a (BFF-backed) for anything showing content.

### Surface 2 — Communications DataGrid (global list) — **MOSTLY DONE**
The config + framework exist. R2 work is thin:
- Optionally a **standalone full-page app** (`src/solutions/sprk_communicationspage/`, ~50-line shell copying `sprk_invoicespage/src/main.tsx`) for an "All Communications" navigation entry.
- Curate the **saved query / columns** so the framework auto-derives the right filter chips: **channel** (`sprk_communicationtype`), **date** (`sprk_sentat`/`createdon`), **regarding** (`sprk_regardingmatter`,…), and **person** lookups already present (`sprk_sentby`, `sprk_regardingperson`).
- Reuse the existing config GUID `e1826c4c-…` (don't create a colliding second default). *Req 4 — the grid half.*

### Surface 3 — SpaarkeAi "Communications" widget (rich) — **UPGRADE-IN-PLACE**
Upgrade the existing thin `communications-list` widget to the **rich Calendar-style Pattern D** widget (visual cards + filter-chip toolbar + embedded `<DataGrid>` + row-click modal). Copy `CalendarWorkspaceWidget.tsx`. **Keep the type string `communications-list`** and section id `communications` (renaming breaks dispatch). New shared lib **`@spaarke/communication-components`** (§11 reasoning: Event lib is entity-coupled; ai-widgets is the thin-generic layer). *Req 4 — the workspace half.*

### Cross-cutting capabilities
- **CC-1. Thread regarding-resolver treatment + naming + category/tags** (schema + form).
- **CC-2. Person/participant index** (new junction) → the person filter.
- **CC-3. Read endpoints**: `by-regarding` + filtered `query`.
- **CC-4. Auto-threading policy**: subject → record-default → master.
- **CC-5. Compose-form enrichment** — capture Subject/topic, structured recipients, Cc/Bcc (feeds CC-1 naming + CC-2 person index at the point of capture).

## 5. Cross-cutting design detail

### CC-1 — Thread gets the same regarding treatment as `sprk_communication`
**Gotcha (verified):** the thread's `sprk_regardingrecordtype` is **Text**, but `sprk_communication`'s is a **Lookup** (`sprk_recordtype_ref`), and RegardingResolver requires a Lookup binding. So:
- **Add** the typed `sprk_regarding{matter,project,…}` lookups to `sprk_communicationthread` (subset or all 11, mirroring `RegardingFieldMap.All`).
- **Add a new Lookup discriminator field** for RegardingResolver's `regardingRecordType` binding (keep the existing Text field as the denormalized copy the timeline/membership already read — avoids a breaking retype of `ThreadResolver.cs` + membership derivation + timeline filters).
- **Place the existing RegardingResolver PCF** on the thread form with `entity="sprk_communicationthread"` — **zero PCF code change** (FR-22 lever).
- **Naming**: `sprk_name` is already user-editable; today `ThreadResolver.BuildTopic()` sets a one-shot default at create. Add "**re-derive unless user-edited**" logic (new — needs an auto-vs-edited flag).
- **Category/tags/description**: genuinely new schema (no ADR-024 precedent). **First check for an existing tag/category entity** platform-wide before inventing one (§11).
- ❌ **Do NOT** use `CommunicationConnections` PCF (AI-provenance-specific) or the Field Mapping Framework (client-wizard-only) for thread regarding.

### CC-2 — Person filter needs a participant index (the one real schema-heavy piece)
**Finding:** sender/recipients are stored as `;`-separated **text** (`sprk_from/to/cc`); there is **no queryable participant table**. The Membership service answers *record→people* (write-side), not *person→communications*.
- **Add** `sprk_communicationparticipant` junction: `(communication|thread) ↔ resolved systemuser|contact + role{From,To,Cc})`, populated at **capture/send** time by reusing `ParticipantCorrelationRung`'s email→contact resolution (`QueryContactByEmailAsync`).
- Align with ADR-034's `(personId, personIdType)` tuple precedent (which explicitly rejected text-index + polymorphic-lookup approaches). **Requires a schema ADR + §10/§11 justification.**
- **Interim:** ship person filtering via the existing **lookups** (`sprk_sentby`, `sprk_regardingperson`) on the grid now; gate the full sender/recipient/participant filter on the junction.

### CC-3 — Read endpoints (extend the blessed read path, don't fork it)
- `GET /api/communications/by-regarding/{entityType}/{id}` → all threads+messages for a record. **Reuse** `IImpersonatedCommunicationQuery` (entity-set-agnostic) + `ICommunicationAccessFilter` + DTOs; add a `ReadByRegardingAsync` on `CommunicationThreadReadService` (impersonated-query threads by regarding → messages by thread-id `or`-filter → same access filter). Small add, no new access logic.
- `GET /api/communications?thread=&regarding=&channel=&from=&to=&participant=` → filtered query. thread/regarding/channel/date reuse the read path; **`participant=` is blocked on CC-2's junction**.
- ❌ **Do NOT reintroduce the membership-union on reads** — it was deliberately retired 2026-07-16 (`notes/access-model-decision.md`); reads stay impersonation + the 2-rule filter.

### CC-4 — Auto-threading policy (from UAT feedback)
3-tier `IThreadResolver` policy so messages never orphan: **subject/conversation thread → record-default thread (one per record) → master thread (per-user catch-all)**. Small resolver-policy change + a `default-thread` marker.

### CC-5 — Compose-form enrichment (verified gap in the R1 send form)
The R1 compose surface (`TimelineComposeBox`) captures only **To / Body / BodyFormat / Attachments** — it has **no Subject and no Cc/Bcc**, so every message persists `sprk_subject = "(No Subject)"` → `sprk_name = "Message: (No Subject)"` (why the timeline shows generic "Message" labels), and `To` is stored as **`;`-joined text** in `sprk_to` (not resolved to records). The full send path (`SendCommunicationRequest`) already supports Subject/Cc/Bcc — only the form omits them. R2 enrichment:
- **Subject/topic field** on compose → populates `sprk_subject` + a meaningful `sprk_name`; also the natural default source for **thread naming** (CC-1) and the *Jun/Feb/Jul* thread labels.
- **Structured recipient picker** (To/Cc/Bcc) that resolves entries to `contact`/`systemuser` records → the write path populates the **`sprk_communicationparticipant` index** (CC-2) at capture time, making the **person filter** exact instead of text-`LIKE`. Keep free-text entry as a fallback for external addresses.
- **Expose Cc/Bcc** on the form (`sprk_cc`/`sprk_bcc` already in the DTO, unused by the form today).
- Reuse the existing **`RecipientField`** component (already used by `TimelineComposeBox`/`EmailComposer`) — extend its selection to emit resolved record ids, don't build a new picker.

This is the single point where CC-1 (naming), CC-2 (person index), and the capture UX converge — worth doing together.

## 6. ADR tensions / decisions for owner

| # | Tension | Options |
|---|---|---|
| T-1 | **VisualHost client-fetch vs BFF access filter** | Surface 1b (VisualHost) fetches client-side (native Dataverse RLS, but no `internal-only` rule). **Decision:** use VisualHost only for a count/summary card; render message *content* via the BFF-backed regarding-mode Timeline (1a). |
| T-2 | **Participant index = net-new schema (ADR)** | Person filter can't be done well on text fields. Add `sprk_communicationparticipant` junction (§11 + schema ADR) vs. accept lookup-only person filtering (`sprk_sentby`/`sprk_regardingperson`) for R2. |
| T-3 | **Thread regarding discriminator field** | Add a new Lookup field (non-breaking) vs. retype the existing Text `sprk_regardingrecordtype` (breaking — touches ThreadResolver + membership + timeline filters). Recommend: add new Lookup, keep Text as denormalized copy. |
| T-4 | **Coordinate with `ai-spaarke-ai-workspace-UI-r2`** | The grid config + thin widget are theirs. R2 upgrades the widget in place — confirm ownership/coordination so we don't fork the config record. |

## 7. Hot-path declaration (root CLAUDE.md §10)

```xml
<hot-path-declaration>
  <bff>YES</bff>                <!-- new read endpoints + participant-index write + auto-thread policy -->
  <spaarke-ai>YES</spaarke-ai>  <!-- upgrade the communications-list workspace widget -->
  <ci-workflows>NO</ci-workflows>
  <skill-directives>NO</skill-directives>
  <root-claude-md>NO</root-claude-md>
</hot-path-declaration>
```

**Placement Justification (§10):** new read endpoints extend the existing `CommunicationEndpoints` group + `CommunicationThreadReadService` (the blessed impersonation read path); the participant index is a Dataverse schema add + a capture/send-time write reusing existing resolution logic — no new AI dependency, no new external SDK. Publish-size impact expected ≈0 (no new packages).

## 8. Reuse ledger (§11 — what we extend vs. build)

| New surface | Extend (don't build) | Genuinely new |
|---|---|---|
| Record threads view | `CommunicationTimeline` component + PCF pattern; BFF read path | `by-regarding` endpoint; regarding-mode rendering |
| Global grid | `sprk_gridconfiguration` `e1826c4c-…`; DataGrid framework | (optional) `sprk_communicationspage` shell |
| Workspace widget | existing `communications-list` registration; `CalendarWorkspaceWidget` shape | `@spaarke/communication-components` lib content |
| Thread regarding | RegardingResolver PCF (0 code); `RegardingFieldMap` | typed lookups + Lookup discriminator; category/tags |
| Person filter | `ParticipantReference`; `ParticipantCorrelationRung` resolution; ADR-034 tuple pattern | `sprk_communicationparticipant` junction |

## 9. Proposed waves (draft)

- **W0 — Schema + coordination**: thread typed-regarding lookups + Lookup discriminator + category/tags; participant-index ADR decision; confirm widget/config coordination with `ai-spaarke-ai-workspace-UI-r2`.
- **W1 — BFF reads**: `by-regarding` + filtered `query` endpoints (reuse read path); seam tests.
- **W2 — Record threads view (Surface 1)**: regarding-mode Timeline component + PCF; optional VisualHost summary card.
- **W3 — Workspace widget upgrade (Surface 3)**: `@spaarke/communication-components` + rich widget replacing the thin one.
- **W4 — Grid polish (Surface 2)**: standalone page shell + curated views/columns/person chips.
- **W5 — Participant index + person filter (CC-2)**: junction + capture/send population + `participant=` facet.
- **W6 — Compose-form enrichment (CC-5)**: Subject/topic + Cc/Bcc + structured recipient picker (feeds CC-1 naming + CC-2 index).
- **W7 — Auto-threading policy (CC-4)** + thread naming re-derive (CC-1).
- **W8 — Tests + docs + wrap**.

## 10. Open questions for the owner
1. **Participant index (T-2)** — build the junction in R2, or defer full person-filter and ship lookup-only for now?
2. **Category/tags** — is there an existing platform tag/taxonomy entity to reuse, or is a new choice set acceptable?
3. **Coordination (T-4)** — is `ai-spaarke-ai-workspace-UI-r2` active; who owns the shared config record + widget?
4. **Standalone page (Surface 2)** — needed, or is the workspace widget + record panel sufficient?
5. **Scope of "records"** — Matter-first, or all 11 regarding-family entities from day one?

---

*Draft — grounded in the 2026-07-18 five-part resource investigation (findings archived at `notes/r2-resource-investigation.md`). Next step after review: `/design-to-spec` → `/project-pipeline`.*
