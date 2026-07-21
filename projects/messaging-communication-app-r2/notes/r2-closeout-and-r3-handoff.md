# R2 Close-out + R3 Handoff — continuity ledger

> **Purpose**: guarantee nothing is lost between closing `messaging-communication-app-r2` and the new
> `messaging-communication-app-r3` project. Every R2 open item, deferred decision, and prerequisite is
> accounted for here with an explicit disposition.
> **Date**: 2026-07-20 · **R2 status**: ✅ COMPLETE (code-complete, merged to master, BFF deployed).

---

## 1. R2 final state

- **Code-complete**: all 21 tasks ✅ (20 work + 090 wrap). Tests **8654 pass / 0 fail**. Publish **~46.24 MB** (<60). 0 new CVE. 0 ADR violations.
- **Merged to master** (2026-07-20). BFF **deployed + verified live** on `spaarke-bff-dev` — `by-regarding` / `query` / `participant=` endpoints registered (401 behind auth; return data once schema is live).
- **ADR-048** (participant index) landed. Access model: impersonation + access-filter, **no membership-union** (retired 2026-07-16).

## 2. What R3 builds ON (the R2 foundation) — all captured in R3 `spec.md`

| R2 deliverable | R3 usage | In R3 spec |
|---|---|---|
| BFF reads (`by-regarding`, `query`, `participant=`) | Extended by backend wave | FR-16/18/19 |
| Thread schema (task 002: 11 typed lookups + discriminator + markers) | Regarding + naming | Prereq §Dependencies |
| Participant junction (task 003) | Person data + participant naming | Prereq §Dependencies, FR-17/21 |
| `ThreadResolver` auto-threading | Extended (participant naming + rename) | FR-17 |
| Shared `CommunicationTimeline` core | Reused (conversation core) | FR-01/02, §5 reuse ledger |
| Participant indexer | Rolled up for naming | FR-17 |
| Grid config `e1826c4c-…` + `sprk_communicationspage` | Email/list lens | FR-15 |

## 3. Open findings → dispositions (the "nothing lost" core)

| Finding (R2) | R2 status | Disposition — where it went |
|---|---|---|
| **Re-derive trigger (071)** — `ReDeriveThreadNameAsync` built but unwired | Method built, gated, no trigger | → **R3 FR-17** — a BFF rename endpoint sets name + flips the marker (no Dataverse plugin). Carried. |
| **Unread backing field (023)** — VisualHost count-only, no read-state | No backing field | → **R3 FR-25** — read/unread (per-user last-seen), best-effort. Carried. |
| **RegardingResolver catalog gap (Q3)** — shared `TODO_REGARDING_CATALOG` doesn't match the thread's 11-target regarding family | Control shipped but **NOT placed on the thread form** | **DEFERRED (path b)** — inert because R3 owns thread-regarding via the NewThreadModal, not RegardingResolver-on-thread. Fix recipe preserved in §6 so it is recoverable if ever needed. |
| **Compose-dep prod-bundle gap (030)** — mammoth/tiptap block full prod bundle on LegalWorkspace + SpaarkeAi | Pre-existing, **not caused by R2** | Portfolio-level follow-up (affects any full-bundle build). Not an R2 or R3 deliverable. |

## 4. R2 surfaces intentionally NOT deployed (superseded by R3 — do not finish)

Finishing these would be throwaway work; R3 replaces the surface:

- **`CommunicationTimelineRegarding` PCF on the 11 forms** → replaced by R3's record right-pane conversation PCF + the "Email & Messages" grid tab (R3 FR-13/15).
- **RegardingResolver on the thread form** (+ its catalog fix) → R3's thread UX (NewThreadModal, BFF rename) owns thread-regarding.
- **VisualHost "unread" card (023)** → R3 conversation has native unread + FR-25.
- **R2 Success Criterion 4** ("thread regarding resolves via RegardingResolver on the thread form; name re-derives") is therefore **superseded by R3**, not met by R2. Recorded honestly here; the *capability* moves to R3.

## 5. Owner prerequisites R3 depends on (confirm before R3 P1)

1. **Task 002 schema live** — `sprk_communicationthread` 11 typed `sprk_regarding*` lookups + `sprk_regardingrecordtype_ref` + `sprk_nameisautoderived` + `sprk_isdefaultthread`. (R2 PCF reads real data → likely applied; confirm.)
2. **Task 003 schema live** — `sprk_communicationparticipant` junction. R3's person filters **and** participant naming depend on it. **Confirm explicitly** (by-regarding read does NOT exercise it).
3. **Notification spine** available for R3 (owner-committed) — R3 FR-22 consumes `communication-arrived`.

## 6. RegardingResolver catalog fix recipe (preserved so it is not lost)

If thread-regarding-via-RegardingResolver is ever wanted (R3 currently does NOT need it):

- **Gap**: `TODO_REGARDING_CATALOG` (`src/client/shared/Spaarke.UI.Components/src/services/TodoRegardingUpdateBuilder.ts`) is a 12-entry catalog **missing `sprk_servicerequest` and `account`**, and maps **contact → `sprk_regardingcontact`** whereas the thread's field is **`sprk_regardingperson`** (task 002 / `RegardingFieldMap.All`). `resolveAllowedCatalog` only *filters* the catalog (blank = full), so the `regardingTargets` input cannot add the missing targets.
- **Fix**: (1) add `sprk_servicerequest` + `account` catalog entries (additive for other consumers); (2) make the resolver's nav-prop matching robust to the `contact → sprk_regardingperson` naming (match by referenced-entity within the regarding family, not a brittle `columnName.includes('contact')` substring) — without breaking `sprk_todo`'s `sprk_regardingcontact`; add a unit test. (3) **Data**: `sprk_recordtype_ref` rows must exist for `sprk_servicerequest`, `account`, `contact` (the resolver looks up the discriminator by entity logical name via `PolymorphicResolverService.resolveRecordType`).
- **Files**: `TodoRegardingUpdateBuilder.ts` (catalog) + `src/client/pcf/RegardingResolver/RegardingResolver/handlers/ResolverWriteHandler.ts` (nav-prop matching).

## 7. Pointers

- R3 spec: [`../../messaging-communication-app-r3/spec.md`](../../messaging-communication-app-r3/spec.md)
- R3 design: [`../../messaging-communication-app-r3/design.md`](../../messaging-communication-app-r3/design.md)
- R3 UX prototype (signed off): `spaarke-prototype/projects/2026-07-communication-conversation-widget/`
- R3 worktree: `C:\code_files\spaarke-wt-messaging-communication-app-r3` (branch `work/messaging-communication-app-r3`, from master).
- R2 README (graduation criteria + owner gates): [`../README.md`](../README.md)
