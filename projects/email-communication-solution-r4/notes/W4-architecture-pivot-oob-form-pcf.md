# W4 Architecture Pivot — OOB Form + PCFs (replaces the Code-Page-as-form approach)

> **Status**: PROPOSED (owner-driven, 2026-07-15) — pending confirmation to formalize into task POMLs.
> **Supersedes**: the W4 "channel-aware Communication **Code Page**" framing (tasks 040/041 as the record form).
> **Prototype**: `spaarke-prototype/projects/email-comm-r4-uat` (ConnectionsEditor at OOB 66/34 proportions).

---

## Decision

Do **not** replace the `sprk_communication` model-driven form with a full Code Page. Instead **keep the OOB form** and **enhance it with PCFs** — the pattern already deployed on this form (the `RELATED RECORD` PCF v1.4.6 in the right column). The design-novel value (multi-connection association review) ships as a **Connections PCF** in the OOB form's 34% "accessories" column; compose/send ships as a **Communication Actions PCF** (+ the existing W2 EmailComposer, PCF-hosted or dialog).

### Why

1. **The OOB 66/34 already answers the layout question.** The right "accessories" column *is* the "right rail" we prototyped — natively, for free.
2. **It's the proven base.** R1 (`x-email-communication-solution-r1`) shipped the OOB form + native attachment subgrid + ribbon send and it worked. R4's Code Page was a re-architecture *away* from that; this returns to it.
3. **Far less to build/own.** Field layout, rich-text body editor, tabs, header (Status/Direction), Save/Save&Close, Attachments subgrid, audit, ribbon — all platform-native.
4. **Lower risk + reversible.** No Form-Component-Control swap (041's risk), no Code Page auth bootstrap, no parallel React SPA.
5. **Most of the plumbing already exists** (see build-vs-reuse below).

---

## Capability audit (grounded in code, 2026-07-15)

| Capability | Status | Evidence |
|---|---|---|
| **Send** (server + endpoint) | ✅ Built | `POST /api/communications/send` (+ `/send-bulk`) — `CommunicationEndpoints.cs:50`. Current UI = ribbon `sprk_communication_send.js` (~1,150 LOC; R4 task 062 slated to retire it). |
| **.eml → `sprk_document`** | ✅ Built (auto) | `EmlGenerationService` + `EmailArchiver` (016); inbound archival `IncomingCommunicationProcessor.cs:643-701`; outbound archive (R1 task 032). |
| **Per-attachment → `sprk_document`** | ✅ Built (auto) | One `sprk_document` per attachment (documenttype=Email) + `sprk_communicationattachment.sprk_document` link — `IncomingCommunicationProcessor.cs:570-616`. |
| **Attachment entity + subgrid** | ✅ Built | `sprk_communicationattachment` intersection + subgrid (in the live form). |
| **"Add Existing" document picker** | ⚙️ Native subgrid config | R1 task 036 designed it as a native subgrid "Add Existing" lookup (custom PCF explicitly rejected), container-filtered, creating attachment records on select — `attachment-picker-config.md`. Config + light form logic, not new dev. |
| **Compose/Reply/Forward UI** | ✅ Built (W2) | `<EmailComposer/>` / `SendEmailPage` / `SendEmailDialog` (tasks 020/021). Host in a PCF or dialog. |
| **Multi-connection association review** | 🎨 Prototyping | The **new value** → Connections PCF (this project's core). |

**Small optional add**: an on-demand **"Save to SharePoint"** button needs a thin `POST /api/communications/{id}/archive` endpoint over the existing archival logic (today archival runs automatically on send/receive).

---

## Re-scope (proposed task changes)

| Task | Was | Becomes |
|---|---|---|
| **040** Code Page shell | Channel-aware Code Page (record form) | **Superseded as the form host.** Retain shell/build tooling as the host for a PCF-hosted composer if needed; not deleted (reversibility). |
| **041** Mount composer + **FCC swap** | Replace OOB form via Form Component Control | **Drop the FCC form swap.** Composer-mount work → reused as the PCF/dialog host for `SendEmailPage`. |
| **042** Embed RegardingResolver review | Review surface on the Code Page | **REFOCUS → "Connections PCF"** — the multi-connection ConnectionsEditor (prototype) as a PCF in the OOB form's right column. Reuses `PolymorphicResolverService.applyResolverFields` + the ADR-024 regarding family. *(This is closer to 042's original PCF intent.)* |
| **NEW — Communication Actions PCF** | — | Reply / Forward / Send / Save Draft / (Save to SharePoint) buttons calling the **existing** `/api/communications/send` (+ optional `/archive`). **Replaces the 1,150-LOC ribbon `sprk_communication_send.js`** (folds in task 062's retirement). Your idea. |
| **NEW (small) — `/archive` endpoint** | — | Thin `POST /{id}/archive` over existing `EmailArchiver` for on-demand "Save to SharePoint". Optional. |
| **Attachment "Add Existing"** | (R1 designed) | Enable/verify the native filtered document lookup on the subgrid. Config task. |
| **043** Code Page deploy + UI tests | Deploy the Code Page | **Re-target** → PCF deploy (`pcf-deploy`) + OOB form config + UI tests. |
| **060/061** (W6 wizard migrations) | Reuse composer | **Unaffected** — the W2 composer wrappers are still used (as PCF/dialog host). |

**Component reuse (no rework lost):** the prototype's **ConnectionsEditor** ports directly into the Connections PCF (RegardingResolver already proves Fluent-v9-in-PCF on this form); the **EmailComposer** becomes PCF-hosted.

---

## ADR Tension (per root CLAUDE.md §6.5)

| ADR | Rule challenged | Conflict | Proposed path | Rationale |
|---|---|---|---|---|
| **ADR-026** (Full-page Custom Page standard) | Communication record surface should be a standalone Code Page | For a **record-review** surface, replacing the OOB form discards native field layout, rich-text editor, subgrids, ribbon, save, and audit — and departs from R1's shipped OOB-form base — for no proportional benefit. The 66/34 accessories column already provides the "right rail" the design wanted. | **Path A — project-scoped exception** (candidate Path B if we conclude Code-Page-replaces-form is wrong for *all* record-review surfaces) | Enhance-OOB-form-with-PCFs is lower-build, native, reversible, and already the deployed pattern here (RELATED RECORD PCF). Reserve Code Pages for genuinely bespoke full-page surfaces, not record forms. |

Alternative considered + rejected: keep the Code Page (ADR-026 compliant) — rejected because it re-creates platform-native form capability at higher build + maintenance cost and higher risk (FCC swap), with no UX gain over OOB + PCF.

---

## Net effect

- **Less to build**: send, archival, attachment fan-out, entity + subgrid, composer all exist. Net-new = **Connections PCF** + a small **Actions PCF** (+ optional `/archive` endpoint + subgrid "Add Existing" config).
- **Lower risk**: no form replacement; platform owns auth/nav/save/audit.
- **Retires tech debt**: the 1,150-LOC ribbon send script.
- **Preserves the design work**: ConnectionsEditor + EmailComposer both reused.

**Next step after confirmation**: formalize these into task POMLs (refocus 042; author the Actions PCF task; adjust 040/041/043 status; log the ADR-026 Path-A exception in `spec.md` ADR Tensions).
