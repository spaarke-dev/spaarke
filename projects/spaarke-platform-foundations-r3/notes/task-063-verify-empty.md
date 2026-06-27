# Task 063 — Verify-Empty Result + Maker-Side Escalation

> **Task**: R3-063 (P7.1 — UI tile consumer migration)
> **Authored**: 2026-06-21
> **Status**: Verify-empty closure; in-repo scope of FR-3H3.4 satisfied
> **Spec reference**: FR-3H3.4 (consumer migration) — UI tile cluster
> **Predecessor inventory**: [`sprk-searchindexed-consumer-inventory.md`](sprk-searchindexed-consumer-inventory.md) (task 060 — FR-3H3.3)

---

## 1. Result

**Zero in-repo UI tile readers of `sprk_searchindexed` exist.** No PCF control, code-page,
shared React component, or wizard reads the bool field for filtering or display logic. The
field is **write-only** in the current codebase (writes documented in tasks 061 + 062).

No code migration was performed. This task closes as verify-empty.

---

## 2. Re-Verification Method

Re-executed task 060's discovery against UI surfaces on 2026-06-21 (after task 061 schema
deploy + task 062 dual-write landed) to confirm no new readers were introduced.

### Grep commands run

| Surface | Pattern | Result |
|---|---|---|
| `src/client/pcf/` (PCF controls) | `sprk_searchindexed` (case-insensitive) | **0 matches** |
| `src/client/code-pages/` (React code-pages) | `sprk_searchindexed` (case-insensitive) | **0 matches** |
| `src/client/shared/` (shared TS/React libs) | `sprk_searchindexed` (case-insensitive) | 1 match (JSDoc only — see §3) |
| `src/client/shared/` | `searchindexed` (broader sibling search) | Same 1 JSDoc match |
| `src/solutions/` (Vite SPAs incl. wizards) | `sprk_searchindexed` (case-insensitive) | 1 match (JSDoc only — see §3) |

### Match disposition (both are documentation-only)

| File:Line | Type | Migration action |
|---|---|---|
| `src/client/shared/Spaarke.UI.Components/src/services/EntityCreationService.ts:187` | JSDoc `@param createdDocumentIds` description that says "the BFF updates `sprk_searchindexed` + tracking fields on each document" | **None for task 063.** No runtime read. Doc refresh deferred to P10 documentation sweep (per inventory §4B). |
| `src/solutions/DocumentUploadWizard/src/services/uploadOrchestrator.ts:480` | JSDoc on `triggerRagIndexing` that references "the sprk_searchindexed tracking field on the document record" | **None for task 063.** No runtime read. Doc refresh deferred to P10 documentation sweep (per inventory §4B). |

Both matches were already cataloged in task 060's inventory §4B as "Code-comment XML/inline
docs (no behavior — refresh during 062)". They remained as comments through tasks 061 + 062
because those tasks added the new fields without removing the old bool (dual-write
strategy). They will be refreshed during the P10 documentation sweep alongside the eventual
drop-old-field step (out of R3 scope).

---

## 3. Cross-Reference to Inventory Finding

Task 060's inventory (§3A) found **zero in-repo UI tile readers** and recommended:

> "Task 063 scope clarification needed: the migration must verify form/view XML in the
> Dataverse solution (NOT tracked in repo) to confirm whether any view filter or form column
> references the field. **Recommend escalating to maker-side solution export audit OR
> deferring 063 until 061 deploys + observation in dev.**"

Task 061's completion notes flagged this:

> "063+064 likely re-scope to 'verify-empty + escalate maker-side audit'"

This re-verification confirms the inventory's prediction. The in-repo migration scope of
FR-3H3.4 (UI tile cluster) is **empty**.

---

## 4. Maker-Side Audit Escalation (Operator Follow-Up)

The following maker-side artifacts live **outside this repository** and were not in scope
for task 063 (which audits in-repo code only). They MUST be operator-audited before the
eventual `sprk_searchindexed` drop-old-field step (out of R3 scope; planned post-prod-bake
per spec FR-3H3.4 "remove after consumer migration confirmed in dev/test").

### Operator audit checklist (maker-side)

For each of the following Dataverse artifact types, the platform operator MUST grep for
`sprk_searchindexed` and either migrate to `sprk_searchindexcompletedon` non-null check or
confirm zero references:

1. **Forms** — Document main + quick-create + quick-view forms; any field reference, business
   rule, or formula column citing `sprk_searchindexed`.
2. **Views** — Public + personal views, FetchXML view filters citing `sprk_searchindexed` as
   a filter clause or visible column.
3. **Business rules** — Server-side and client-side rules on the Document entity.
4. **Power Automate flows** — Flows in the `Spaarke` solution (and unmanaged customizations)
   that trigger on or read `sprk_searchindexed`.
5. **Power Apps canvas/model-driven apps** — Any app citing the field in a Power Fx formula
   or column reference.
6. **Plugin steps** — Registered SDK message processing steps that filter on or read the
   field. (Note: no Spaarke plugins in `src/dataverse/plugins/` currently reference the
   field — verified by task 060 inventory.)
7. **Solution-exported playbook JSON** — Any AI playbook with a JPS template referencing
   `sprk_searchindexed`. (Note: scope catalog inventory unchanged from R2 baseline; verify
   via `jps-scope-refresh` after task 062's playbook-side changes land.)

### Recommended escalation mechanism

File the maker-side audit as a separate operator follow-up ticket, NOT as an in-repo task.
Pattern matches `operator-followup-task071.md` (already filed for the membership-discovery
follow-up). The audit ticket should:

- **Title**: "Maker-side audit: confirm zero `sprk_searchindexed` references before
  drop-old-field"
- **Owner**: Platform operator / maker
- **Trigger condition**: Dual-write (task 062) is live in dev + tested in prod-bake; ready
  to drop old bool.
- **Acceptance**: Solution export + grep confirms zero references in forms/views/flows/
  apps/plugins, OR each reference has been migrated to `sprk_searchindexcompletedon`
  non-null check.
- **Deliverable**: Sign-off note attached to the drop-old-field PR (deferred beyond R3).

---

## 5. Acceptance — FR-3H3.4 In-Repo Scope

FR-3H3.4 ("Migrate all consumers identified in FR-3H3.3 to read `sprk_searchindexqueuedon`
/ `sprk_searchindexcompletedon`") is satisfied **for the UI tile cluster in-repo scope**:

- ✅ All UI tile clusters identified in FR-3H3.3 inventory (§3A): **0 readers**.
- ✅ Re-verification on 2026-06-21 confirms inventory finding remains accurate after tasks
  061 + 062 landed.
- ✅ Status display behavior unchanged from user perspective (no UI code touched, no UX
  regression risk).
- ✅ Maker-side residual scope (forms/views/flows/apps/plugins) escalated to operator
  follow-up — appropriate boundary per CLAUDE.md sub-agent write boundary (`.claude/` and
  Dataverse-native artifacts are not in-repo migration scope).

**Sibling task coordination**: Task 064 covers the API/FetchXML/OData reader cluster
(server-side); task 065 covers a separate canvas-server mapping integration test. No
overlap with task 063 scope.

**Disposition**: Task 063 closed as verify-empty. Acceptance criteria from POML:

- ✅ "Each UI tile consumer migrated" — vacuously true (zero consumers).
- ✅ "Status display unchanged from user perspective" — no UI code modified.
