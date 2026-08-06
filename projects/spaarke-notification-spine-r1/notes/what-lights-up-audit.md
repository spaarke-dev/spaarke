# "What Lights Up" Audit — Notification-Disposition Flip (FR-14 pre-req)

> **Task**: 032 (Phase 3, Wave 10) · **Rigor**: STANDARD · **Model**: opus/high
> **Status**: ✅ Complete — **reviewed gate for task 033** (do NOT flip `DispositionRoutability.Notification` until this doc is signed off; see [§7 Sign-off](#7-sign-off)).
> **Date**: 2026-07-21
> **Bottom line**: **Nothing lights up.** Zero shipped Bindings resolve to `disposition=notification` in the live catalog **or** in code/seed data. The FR-14 flip is a **no-op for every existing capability**. Recommendation: **immediate flip is SAFE** — proceed with task 033 (registry flip + `OutputRouter` leg together, per ADR-043 Path C), **no Binding remediation required**.

---

## 1. Why this audit exists (the all-or-nothing checkpoint)

`DispositionRoutability` ([`DispositionRoutability.cs`](../../../src/server/api/Sprk.Bff.Api/Services/Ai/DispositionRoutability.cs)) is ADR-043 §3's **ONE** disposition source of truth. Its `Notification` entry is `Routable=false` today ([lines 98-102](../../../src/server/api/Sprk.Bff.Api/Services/Ai/DispositionRoutability.cs#L98-L102)):

```csharp
new Entry {
    Disposition = BindingDisposition.Notification, LedgerValue = "notification", Routable = false,
    NotRoutableReason = "the notification-delivery side-effect leg is not yet built — lands in a later wave",
}
```

The registry row is **per-disposition-TYPE, not per-Binding**. The instant task 033 flips `Routable=true`, `IsAdmissible(Notification)` and `IsRoutable(Notification)` return `true` for **every** Binding that declares that disposition **simultaneously** — there is no way to admit "some" Notification Bindings and reject others (that granularity does not exist in the registry, by design — ADR-043 §3). Today all such Bindings are rejected identically with a loud 422 before they reach `OutputRouter` (never a silent drop).

**This audit is therefore the only deliberate checkpoint before the flip.** It must enumerate every Binding that would begin actually emitting an `appnotification` the moment the registry flips, and judge whether that is safe.

---

## 2. Scope precision — two DIFFERENT notification mechanisms (do not conflate)

There are two independent ways an `appnotification` gets written in this codebase. **Only one is affected by the FR-14 flip.**

| # | Mechanism | Trigger | Gated by `DispositionRoutability`? | In scope for this audit? |
|---|---|---|---|---|
| **Path 1** | **Node executor** — `CreateNotificationNodeExecutor` (`ActionType 50` / `canvasType: createNotification`), invoked as a **playbook-graph node** via `NodeExecutorRegistry`. Writes `appnotification` directly through the Layer-A seam / entity service. | Scheduled/proactive **playbook run** (e.g. Daily Update Service `BackgroundService`). | **No.** It never touches `DispositionRoutability` — it is the linear playbook-engine path. It writes notifications **today**, unchanged by the flip. | **No** — not affected by FR-14. |
| **Path 2** | **Disposition routing** — a Binding (`sprk_playbookconsumer`) declares `sprk_disposition=notification`; on the chat/dispatch surface `OutputRouter` would route it. | Chat chip click / proactive dispatch / dispatch-session. | **Yes.** Admission = routability from the registry (ADR-043 §3). Currently `Routable=false` → rejected 422. | **YES** — this is the flip target. |

**Evidence Path 1 ≠ Path 2**: the Daily Update Service notification playbooks (e.g. [`notification-tasks-due-soon.json`](../../../projects/spaarke-daily-update-service/notes/playbooks/notification-tasks-due-soon.json)) declare `"__actionType": 50, "canvasType": "createNotification"` on a playbook **node** — they are Path 1. They already emit `appnotification` records through the node executor and are **not** `sprk_playbookconsumer` Binding rows, so they carry no `sprk_disposition` and are untouched by the registry flip. (Project CLAUDE.md for daily-update-service confirms: "MUST register `CreateNotificationNodeExecutor` via `NodeExecutorRegistry[ActionType]`" — the node path.)

> **NFR-02/03 note on Path 1**: the daily-update node-executor notifications *do* render task/event names + due dates into notification title/body (see the `title`/`body`/`itemNotification` fields in that JSON). That is an **existing, shipped** behavior on the node path, out of scope for FR-14, and already governed by that project's own recipient-scoping (`recipientId: {{run.userId}}`, membership-scoped queries). This audit does not re-litigate Path 1; it is flagged here only so the reviewer knows it was examined and consciously excluded.

---

## 3. Method — how "every Notification-disposition capability" was found

The disposition on a Binding is a **static, data-driven column** (`sprk_disposition`), mapped to `Binding.Disposition` ([`Binding.cs:73-74`](../../../src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/Binding.cs#L73-L74), default `Informational` when null/unknown). There is **no** runtime/model-judged disposition (ADR-043 §"MUST keep disposition … as catalog-declared DATA; no runtime model-judged disposition"). So a Binding resolves to Notification **iff** its `sprk_disposition = 100000005`. Two independent searches:

### 3a. Code + seed-data scan (source of truth for shipped/deployable definitions)
- `Grep` for `100000005` / `disposition…notification` across `*.{ps1,json,cs,sql,csv,xml}` and for `BindingDisposition.Notification`.
- `Grep` for any code assigning `Disposition = …Notification` on a Binding under `src/**/*.cs`.

**Result**: the ONLY occurrences of the Notification disposition are:
| Occurrence | File | What it is |
|---|---|---|
| Enum member `Notification = 100000005` | [`Binding.cs:152`](../../../src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/Binding.cs#L152) | Vocabulary definition |
| Registry entry `Routable = false` | [`DispositionRoutability.cs:100`](../../../src/server/api/Sprk.Bff.Api/Services/Ai/DispositionRoutability.cs#L100) | The flip target itself |
| Option-set value `{ Value = 100000005; Label = "Notification" }` | [`Deploy-AiCatalogSchemaExtensions.ps1:224`](../../../scripts/Deploy-AiCatalogSchemaExtensions.ps1#L224) | Defines the *column option*, not a Binding row |
| `[InlineData(BindingDisposition.Notification)]` | `DispositionRoutabilitySeamTests.cs:415`, `OutputRouterTests.cs:240` | Test fixtures |

**No seed data, fixture, or production code assigns `disposition=notification` to any Binding.** (The only `Disposition = BindingDisposition.Notification` match in `src/**/*.cs` is the registry entry on line 100 — not a Binding.)

### 3b. Live Dataverse catalog query (source of truth for what is actually shipped in `spaarkedev1`)
Queried the live `sprk_playbookconsumer` table (all states, all environments) via the Dataverse MCP.

**Targeted query** — `WHERE sprk_disposition = 100000005`:
```
[]   ← ZERO rows
```

**Full disposition census** (`GROUP BY sprk_disposition`, all 28 Binding rows, active + inactive):

| Disposition | Value | Binding count | Routable today? |
|---|---|---|---|
| Informational | 100000000 | 12 | ✅ yes |
| _(null → defaults to Informational)_ | — | 7 | ✅ yes (as Informational) |
| Surface Launch | 100000007 | 4 | ✅ yes |
| Compose | 100000006 | 3 | ✅ yes |
| Email | 100000003 | 1 | ✅ yes |
| Work Product | 100000001 | 1 | ✅ yes |
| **Notification** | **100000005** | **0** | ❌ no (flip target) |
| Overlay | 100000002 | 0 | ❌ no |
| Record | 100000004 | 0 | ❌ no |

Both the code/seed scan and the live catalog agree: **zero Notification-disposition Bindings exist.**

---

## 4. Enumeration + risk assessment

| # | Binding (id / name) | Owning playbook/capability | Entry surface | Notification content it would produce | Spam risk | Content/PII risk (NFR-02/03) | Verdict |
|---|---|---|---|---|---|---|---|
| — | **(none)** | — | — | — | — | — | **N/A — no Bindings resolve to Notification** |

Per acceptance-criterion #3, a "nothing lights up yet" result is itself a valid, reviewable outcome — not an incomplete audit. Every claim above is backed by concrete evidence (live query returning `[]`, the full census, and the code/seed scan citations in §3), satisfying the CLAUDE.md §11 evidence standard (no unverified/"for future flexibility" entries).

**Neither escalation trigger fires:**
- **Trigger 1** (a Binding whose notification content carries privileged/PII content inappropriate for the appnotification surface) — does **not** fire: there is no Binding to carry content.
- **Trigger 2** (a safe rollout requires per-Binding, not per-disposition-type, admission control that `DispositionRoutability` cannot express) — does **not** fire: with zero Bindings there is nothing to granularly gate. No ADR-043 §6.5 tension is raised by this audit.

---

## 5. Sequencing recommendation for task 033

**RECOMMENDATION: Immediate flip is SAFE. Proceed with task 033 — no Binding remediation is required (there are none to remediate).**

The flip is a **no-op for every currently-shipped capability**: nothing changes behavior at flip time because no Binding resolves to Notification. Task 033 may flip `DispositionRoutability.Notification` to `Routable=true` **provided it lands the `OutputRouter` leg in the same change** (ADR-043 Path C, registry-first).

Two **binding conditions** task 033 must honor (these are correctness requirements, not Binding remediations):

1. **The `OutputRouter` notification leg MUST land in the same change as the registry flip.** `OutputRouter.RouteAsync`'s switch has **no `case BindingDisposition.Notification`** today ([switch at lines 247-313](../../../src/server/api/Sprk.Bff.Api/Services/Ai/OutputRouter.cs#L247-L313); cases are Informational/Compose/SurfaceLaunch/Email/WorkProduct/`default`). Flipping the registry alone would send any future Notification dispatch to the `default:` drift-guard, which **throws `ArgumentOutOfRangeException`** ("registry marks routable, router has no leg"). This is not a Binding risk — it is the router's own designed self-check. 033's POML already scopes both edits together; this audit confirms that pairing is mandatory.

2. **Add the `admit⇔route⇔store` seam test for `Notification`** (`tests/integration/seam/**`, per ADR-038 + `DispositionRoutability`'s own doc-comment contract). Because the registry is all-or-nothing, this seam test is the **standing guard** that any future Notification Binding is admitted, routed to a real leg, and stored — with no silent drift.

---

## 6. Forward-looking guard (the real risk is post-flip, not at flip)

Because the flip is a no-op *now* but the registry is all-or-nothing, the meaningful risk is **deferred to the future**: the **first** `sprk_playbookconsumer` row anyone authors with `sprk_disposition=notification` — after 033 lands — will **light up immediately, with no further gate**, and actually emit an `appnotification` from the chat/dispatch surface (a genuinely new capability per 033's POML background).

**Guardrails already in place / recommended so this stays safe:**
- The **`side_effect_class` / ADR-041 confirmation gate** (`Binding.Risk`, [`Binding.cs:79-80`](../../../src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/Binding.cs#L79-L80)) remains the ONE side-effect gate — a future notification-emitting Binding is a side effect and is gated there, not by this registry.
- The **`admit⇔route⇔store` seam test** (§5 condition 2) mechanically proves the leg behaves for `Notification`.
- **Recommended process note for makers** (carry into `docs/guides/ai-guide-consumer-wiring.md` when the first notification Binding is authored): **authoring the first `disposition=notification` Binding is the moment to re-run the NFR-02/03 content/privacy assessment** — check that the Binding's rendered notification `title`/`body`/`actionUrl` cannot carry privileged content or PII onto the appnotification surface. This audit clears the *flip*; it cannot clear a Binding that does not yet exist.

This forward-looking note is **informational for 033** (033's own escalation Trigger 1 already says "if the flip would route a capability the 032 audit did not anticipate, STOP and escalate" — which, given this audit found none, means *any* Notification Binding appearing before 033 lands must halt 033 and return here).

---

## 7. Sign-off

| Field | Value |
|---|---|
| **Audit outcome** | Nothing lights up — 0 Notification-disposition Bindings (live catalog + code/seed). Flip is a no-op for existing capabilities. |
| **Escalation triggers fired** | None (both cleared — §4). |
| **Recommendation** | **Immediate flip SAFE** — proceed with task 033 (registry flip + `OutputRouter` leg together; add the `Notification` admit⇔route⇔store seam test). No Binding remediation. |
| **Reviewed by** | **Project owner (ralph.schroeder) — SIGNED OFF** ✅ Accepted "immediate flip, no remediation." |
| **Review date** | 2026-07-21 |

> **Gate status for task 033**: 033's escalation Trigger 3 requires this doc be "reviewed/signed off" before the flip. This audit's outcome is the **lowest-risk possible** (a verified no-op with zero risky Bindings and no escalation), but the human sign-off line above should be completed before 033's gate opens, per the task's acceptance-criterion #5 and CLAUDE.md §6. The reviewer's decision is simply: accept "immediate flip, no remediation" (recommended) — or direct otherwise.

---

## Appendix — evidence commands (reproducible)

```sql
-- Live catalog: Notification-disposition Bindings (returned [] — zero rows)
SELECT sprk_playbookconsumerid, sprk_name, sprk_consumertype, sprk_disposition, sprk_enabled, statecode
FROM sprk_playbookconsumer WHERE sprk_disposition = 100000005;

-- Full disposition census (28 rows total; Notification = 0)
SELECT sprk_disposition, COUNT(sprk_playbookconsumerid) AS binding_count
FROM sprk_playbookconsumer GROUP BY sprk_disposition;
```
```
# Code/seed scan (no Binding assigns disposition=notification):
Grep "100000005|disposition…notification"  --glob *.{ps1,json,cs,sql,csv,xml}
Grep "Disposition = …Notification"          --glob src/**/*.cs   # only hit: DispositionRoutability.cs:100 (the registry entry)
```
