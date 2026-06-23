# Playbook Author Guide

> **Status**: Updated for R3 (2026-06-22) — new `LookupUserMembership` node + Handlebars helpers + Builder UI safety affordances.
> **Audience**: Spaarke makers + operators authoring playbooks in PlaybookBuilder. Plain-language guide — assumes you know your way around Power Apps but NOT around .NET or React internals.
> **Companion technical reference**: [`docs/architecture/playbook-architecture.md`](../architecture/playbook-architecture.md).

---

## Table of Contents

- [What This Guide Covers](#what-this-guide-covers)
- [What a Playbook Is (Plain Language)](#what-a-playbook-is-plain-language)
- [What's New in R3 (At a Glance)](#whats-new-in-r3-at-a-glance)
- [Quick-Start Recipe: Notify Me About New Documents on My Matters](#quick-start-recipe-notify-me-about-new-documents-on-my-matters)
- [Node Catalog (R3 Update)](#node-catalog-r3-update)
- [Handlebars Template Helpers (R3 Update)](#handlebars-template-helpers-r3-update)
- [Builder UI Safety Affordances (R3)](#builder-ui-safety-affordances-r3)
- [Anti-Patterns to Avoid](#anti-patterns-to-avoid)
- [Migration: Replacing Broken FetchXML with LookupUserMembership](#migration-replacing-broken-fetchxml-with-lookupusermembership)
- [Smoke-Test a Playbook Before Deploying](#smoke-test-a-playbook-before-deploying)
- [Related Documentation](#related-documentation)

---

## What This Guide Covers

This guide walks you through authoring a playbook end-to-end in PlaybookBuilder with the R3-era building blocks. It focuses on the **per-user notification** use case (the most common one) — "for each user on a schedule, look at what they care about, and notify them if something changed." Other shapes (synchronous document analysis, RAG-fed Q&A) are covered in the [Playbook vs. RAG decision tree](./INSIGHTS-PLAYBOOK-VS-RAG-DECISION-TREE.md).

After R3, the canvas has two new safety nets — a **rename guard** and a **branch picker** — and one new advisory — the **edge perf hint** — that catch the most common authoring mistakes BEFORE you save. You'll see them in action below.

---

## What a Playbook Is (Plain Language)

A playbook is a **visual workflow**. You drag nodes from a palette onto a canvas, connect them with lines (edges), and configure each node by clicking it and filling in a form. PlaybookBuilder takes care of two things behind the scenes:

1. **Saving** — every change is written to Dataverse (auto-save every 30 seconds, or Ctrl+S to save immediately). The canvas itself lives in a JSON blob on the playbook record; each node also gets its own `sprk_playbooknode` row so the server can execute them.
2. **Executing** — when the playbook runs (either on a schedule, when a user clicks something, or when an upstream system kicks it off), the server walks the nodes in dependency order. Each node produces a piece of output that downstream nodes can reference with the `{{nodeName.output.field}}` template syntax.

You don't write code. You drag, connect, and fill in forms. PlaybookBuilder validates your work as you go and warns you when something is likely to break.

---

## What's New in R3 (At a Glance)

| What | Where you'll see it | Why it matters |
|---|---|---|
| **`LookupUserMembership` node** | New drag-target in the node palette | One node replaces every hand-rolled "is this user on this matter?" FetchXML query. Talks to the same `MembershipResolverService` your tenant configures once for everyone. |
| **`{{joinIds X.ids}}` helper** | Use it inside a downstream FetchXML node | Renders an array of GUIDs as `"guid1,guid2,guid3"` — exactly the shape FetchXML `operator='in'` wants. No hand-written `{{#each}}` loops. |
| **`{{default X 'Y'}}` helper** | Use it inside any template field | Returns `X` if it resolves to a value, else `Y`. Replaces the broken `{{X ?? 'Y'}}` pattern (which used to render the raw `??` text in production — see Pitfall G1). |
| **OutputVariable rename guard** | Pops up when you rename a node's Output Variable | Auto-renames downstream `{{X.output.field}}` references in one click. Never breaks references silently. |
| **Branch wiring picker** | Pops up when you draw an edge from a Condition node | Asks "True / False / Both?" so you don't end up with a Condition node whose branches both fire. |
| **Edge perf hint** | Yellow badge on the source node of an edge | Tells you "this edge forces sequential execution but moves no data — confirm or remove." Advisory only; you can still save. |
| **Canvas↔server drift CI test** | Runs in every CI build (not visible in the UI) | Stops a developer from adding a new node type to the canvas without wiring up the server executor. Prevents an entire class of silent failures. |

---

## Quick-Start Recipe: Notify Me About New Documents on My Matters

This is the canonical R3 recipe. It uses every new building block: `LookupUserMembership` → `QueryDataverse` with `joinIds` → `Condition` → `CreateNotification`. Total time end-to-end: about 10 minutes for a maker who's seen PlaybookBuilder once.

### Before you start

You need:

- **PlaybookBuilder access** in your environment.
- A few existing matters you've been assigned to (so you have something to look at in smoke-test).
- **System Administrator** OR a role with permission to create `sprk_analysisplaybook` records.
- (Optional but recommended) the `MembershipResolverService` is set up for your tenant — [`MEMBERSHIP-RESOLUTION-GUIDE.md`](MEMBERSHIP-RESOLUTION-GUIDE.md) walks operators through the one-time setup. If it's not set up, your `LookupUserMembership` node will return empty results.

### Step 1 — Create a new playbook

1. Open PlaybookBuilder (it's a Code Page in your model-driven app — usually a tile labelled "Playbook Builder").
2. Click **New playbook**. Give it a name like "New Documents on My Matters" and a description.
3. In the Playbook Properties pane on the right, set:
   - **Playbook Type**: `Notification` (this tells the scheduler to run it on a per-user cadence)
   - **Schedule** (in `sprk_configjson` → `schedule`): `{ "frequency": "daily", "time": "06:00" }`
   - **Category**: `new-documents` (used by notification dedup logic — see Pitfall G4)

### Step 2 — Drop a Start node

1. From the node palette on the left, drag **Start** to the canvas (it usually appears auto-placed in the top-left).
2. Click it to open Properties. Default `outputVariable` is `start`. Leave it.

### Step 3 — Drop a LookupUserMembership node

This is the new R3 building block. It answers: "what matters is the executing user a member of?"

1. From the palette, drag **Lookup User Membership** to the right of the Start node.
2. Click the node to open Properties on the right.
3. Fill in:
   - **Entity Type**: `sprk_matter` (the Dataverse logical name of the entity to resolve memberships on)
   - **Roles** (comma-separated): `owner, assignedAttorney, assignedParalegal` (case-insensitive; matches the role names the `MembershipResolverService` discovered for your tenant — leave empty to get every role)
   - **Output Variable**: `myMatters` (this is the canvas variable name downstream nodes will reference — see [Anti-Patterns](#anti-patterns-to-avoid) about picking a good name)
   - **Include related (1-hop)**: leave **off**. (1-hop transitive memberships are out of R3 scope; the toggle is there for forward compatibility.)
4. Connect Start → Lookup User Membership by dragging from the bottom handle of Start to the top handle of the Lookup node.

> **What the node will produce at runtime**: `myMatters.ids` (a deduped list of matter GUIDs), `myMatters.byRole` (the same IDs grouped by role: `owner`, `assignedAttorney`, `assignedParalegal`), `myMatters.count`. The server resolver does the heavy lifting in one call — you don't need separate FetchXML for each role.

### Step 4 — Drop a QueryDataverse node and use `{{joinIds myMatters.ids}}`

1. From the palette, drag **Update Record** to the right of the Lookup node. (Yes — confusingly, `QueryDataverse` is exposed as the `updateRecord` canvas type with `queryMode: true`. This is a pre-R3 legacy quirk and will be cleaned up in a future release.)
2. Click the node and open Properties. Set:
   - **Query Mode**: on
   - **Entity Logical Name**: `sprk_document`
   - **Output Variable**: `newDocsQuery`
   - **FetchXML**:

     ```xml
     <fetch top="50">
       <entity name="sprk_document">
         <attribute name="sprk_name" />
         <attribute name="sprk_filename" />
         <attribute name="createdon" />
         <attribute name="sprk_documentid" />
         <attribute name="sprk_matter" />
         <filter type="and">
           <condition attribute="sprk_matter" operator="in" value="{{joinIds myMatters.ids}}" />
           <condition attribute="createdon" operator="last-x-hours" value="{{timeWindowHours}}" />
           <condition attribute="createdby" operator="ne-userid" />
         </filter>
         <order attribute="createdon" descending="true" />
       </entity>
     </fetch>
     ```

   - **Template Parameters**:
     ```jsonc
     { "timeWindowHours": "{{default userPreferences.timeWindowHours '24'}}" }
     ```
3. Connect Lookup User Membership → Query New Documents.

> **What the `{{joinIds myMatters.ids}}` does at runtime**: rewrites to `"guid1,guid2,guid3"` (a single CSV string) so FetchXML's `operator='in'` accepts it. If `myMatters.ids` is empty (user has zero matters), it renders as `""` → the IN clause matches zero rows → no notifications. That's the **fail-closed** behavior you want.

> **What the `{{default userPreferences.timeWindowHours '24'}}` does**: returns `userPreferences.timeWindowHours` if it resolves to a value, else `'24'`. Replaces the broken `{{userPreferences.timeWindowHours ?? '24'}}` pattern that used to emit raw text.

### Step 5 — Drop a Condition node to short-circuit when there's nothing to notify about

1. From the palette, drag **Condition** to the right of the Query node.
2. Open Properties. Set:
   - **Output Variable**: `hasNewDocs`
   - **Condition expression** (in `conditionJson`): `{{newDocsQuery.output.count}} > 0`
   - **True branch label**: leave as `True` (or rename to something like `Has new docs`)
   - **False branch label**: leave as `False`
3. Connect Query New Documents → Check Results.

### Step 6 — Drop a CreateNotification node on the True branch

1. From the palette, drag **Create Notification** to the right of the Condition node, slightly above (to leave room for a potential False branch later).
2. **Draw the edge from the Condition node's body** to the Create Notification node. At this point, the **Branch Picker dialog** pops up:
   - Choose **True**.
   - Click **Wire branch**.
3. The edge now shows as a green **True** edge. (If you had chosen Both, you'd get TWO edges — one green True + one red False. The picker never invents a "both" edge type.)
4. Open Create Notification properties. Set:
   - **Output Variable**: `notification`
   - **Title**: `{{newDocsQuery.output.count}} new document(s) on your matters`
   - **Body**: `{{#each newDocsQuery.output.items}}{{sprk_filename}} added to {{matterName}} ({{createdon}}).\n{{/each}}`
   - **Category**: `new-documents`
   - **Priority**: `200000000` (Important)
   - **Recipient ID**: `{{run.userId}}`
   - **Iterate Items**: on (creates one notification per item rather than one bulky summary)
   - **Item Notification**: configure the per-item template (see the notification-new-documents.json migrated playbook for the full shape)

### Step 7 — Save and deploy

1. Press **Ctrl+S** (or wait 30 seconds for auto-save).
2. Watch for validation warnings in the bottom-right Notification badge on each node. If the **edge perf hint** fires on any edge ("this edge forces sequential execution but moves no data") — verify the downstream node actually references the upstream node's Output Variable. In our case, every edge moves data (Lookup → joinIds usage → count check → notification creation), so this advisory should NOT fire.
3. Save complete? Now **schedule it**:
   - The notification scheduler picks up `sprk_playbooktype = Notification` playbooks automatically once they're saved. No separate deploy step.
   - The scheduler runs hourly by default; your `schedule: { frequency: "daily", time: "06:00" }` configures the actual cadence.

### What the recipient sees

The next morning (or the next time the scheduler runs after the `time` you configured), every user the playbook applies to will see in-app notifications in their Power Apps notification panel:

> **"3 new document(s) on your matters"** — clicking expands to one notification per document, each clickable to open the document record.

If the user has zero matters they're a member of, OR zero new documents on those matters in the past 24 hours, NOTHING is created — no empty notification, no error. This is the fail-closed behavior built into both `LookupUserMembership` (empty `ids` array) and `joinIds` (empty CSV).

---

## Node Catalog (R3 Update)

The full node catalog lives in [`playbook-architecture.md`](../architecture/playbook-architecture.md#node-executor-framework). This subsection covers the **R3 addition**.

### `LookupUserMembership` (NEW — ActionType 52)

**What it does**: Resolves the executing user's record memberships for a given Dataverse entity type by calling `IMembershipResolverService` in-process (same backing service as `GET /api/users/me/memberships/{entityType}`).

**Canvas type**: `lookupUserMembership` (drag the **Lookup User Membership** palette item).

**Config (fill in via Properties panel)**:

| Field | Required | Example | Notes |
|---|---|---|---|
| Entity Type | yes | `sprk_matter` | Dataverse logical name; free text, validity is determined by the discovery service at runtime |
| Roles (comma-separated) | no | `owner, assignedAttorney, assignedParalegal` | Case-insensitive; matches roles discovered by the membership service for your tenant; empty = all roles |
| Output Variable | yes | `myMatters` | Canvas variable name; downstream nodes reference as `{{myMatters.ids}}` or `{{joinIds myMatters.ids}}` |
| Include related (1-hop) | no | off | Phase 1D feature, currently accepted-but-ignored — leave off |

**Output shape**:

```jsonc
{
  "entityType": "sprk_matter",
  "count": 47,
  "ids": ["guid1", "guid2", "..."],              // deduped list — use with {{joinIds}}
  "byRole": {                                     // same IDs grouped by role
    "owner": ["guid1"],
    "assignedAttorney": ["guid1", "guid2"],
    "assignedParalegal": ["guid3"]
  },
  "continuationToken": null,
  "cacheExpiresAt": "2026-06-22T15:34:00Z"
}
```

**Where it gets the user identity from**: `NodeExecutionContext.UserId`, set by the scheduler when the playbook runs in per-user mode. If you need to run a playbook in a non-scheduler context, the executor falls back to scanning previous node outputs for a `userId` property — but the scheduler path is what 99% of authors use.

**Requires**: the operator has configured the `MembershipResolverService` for your tenant. If discovery returns no fields for your entity type, the node returns `count: 0` and an empty `ids` array (no error). See [`MEMBERSHIP-RESOLUTION-GUIDE.md`](MEMBERSHIP-RESOLUTION-GUIDE.md) for the one-time operator setup.

---

## Handlebars Template Helpers (R3 Update)

Existing helpers (`safe`, simple variable interpolation, `{{#each}}`, nested property access, etc.) are unchanged — the [architecture doc](../architecture/playbook-architecture.md#templateengine) covers them. R3 added two new helpers, both registered unconditionally — no feature flag needed.

### `joinIds` (NEW)

```handlebars
{{joinIds varName.ids}}
```

**What it does**: Converts an array of GUIDs (or any list of stringifiable values) into a comma-separated string. The output is the exact shape FetchXML's `operator='in'` clause expects.

**Example**:

```xml
<condition attribute="sprk_matter" operator="in" value="{{joinIds myMatters.ids}}" />
```

If `myMatters.ids` is `["a", "b", "c"]`, the rendered XML is:

```xml
<condition attribute="sprk_matter" operator="in" value="a,b,c" />
```

**Behavior with edge cases**:

| Input | Renders as |
|---|---|
| `["a", "b", "c"]` | `"a,b,c"` |
| `[]` (empty list) | `""` — IN clause matches zero rows (fail-closed) |
| `null` or unresolved binding | `""` |
| A scalar (string, number, bool) | `""` (defensive — caller likely passed the wrong shape) |

> **Do NOT** hand-roll a `{{#each ids}}{{this}},{{/each}}` substitute. That pattern leaves a trailing comma, doesn't handle empty lists correctly, and bypasses the unresolved-binding defense. Use `joinIds`.

### `default` (NEW)

```handlebars
{{default varName 'fallback'}}
```

**What it does**: Returns `varName` if it resolves to a non-empty value; otherwise renders `'fallback'`.

**Example**:

```jsonc
"templateParameters": {
  "timeWindowHours": "{{default userPreferences.timeWindowHours '24'}}"
}
```

If `userPreferences.timeWindowHours` is set to `"48"`, the rendered value is `"48"`. If it's missing/null/empty, the rendered value is `"24"`.

**Why this exists**: Handlebars.NET does not support the JavaScript-style `{{X ?? 'Y'}}` null-coalescing operator. Before R3, authors who reflexively used `??` got the literal text `?? 'Y'` rendered in production output — a silent breakage mode (Pitfall G1) that affected 2 of 7 active notification playbooks in R2 UAT. Use `default` instead.

### Runtime unrendered-template warning

If a template variable doesn't resolve at runtime (typo, broken reference, upstream node failed), the engine renders the literal `{{variable}}` string into the output. After R3, the orchestrator detects this case AND emits a structured `unrendered-template-detected` event to the SSE stream + a structured log warning. You'll see it in:

- The PlaybookBuilder run-history view (per-run event log)
- App Insights traces for the run (search for `UnrenderedTemplateDetected`)

This means you find broken references during the FIRST run, not days later when a downstream consumer misbehaves.

---

## Builder UI Safety Affordances (R3)

PlaybookBuilder gained three safety affordances in R3. All of them extend existing PlaybookBuilder components — none introduce new modal frameworks (Q5 owner directive).

### a. OutputVariable rename guard

**When it fires**: You change a node's **Output Variable** field AND at least one other node references `{{<oldName>.output.something>}}` in its config.

**What you see**: A Fluent UI dialog titled "Variable referenced by N downstream nodes" with three buttons:

| Button | Effect |
|---|---|
| **Auto-rename references** (default) | Updates the renamed node's Output Variable AND find/replaces every downstream `{{<oldName>.output.*}}` reference in one transaction. |
| **Keep old name** | Reverts the field to its previous value. Downstream references remain valid. |
| **Cancel rename** | Same as Keep old name. Provided as an escape hatch for users who reflexively look for "Cancel." |

**What to do**: 99% of the time, click **Auto-rename references**. The only reason to choose Keep is if you realized mid-typing that you don't actually want to rename.

Closing the dialog with Esc or clicking outside has the same effect as Cancel — closing the dialog never silently breaks a reference.

A complementary rule called `outputvar-collision` fires (severity: error) if two nodes ever share the same Output Variable. That's a save-blocking error — you must rename one of them.

### b. Branch wiring picker

**When it fires**: You draw an edge **from the body of a Condition node** to a downstream node (instead of dragging from one of the True/False handles on the side).

**What you see**: A Fluent UI dialog titled "Wire branch" with three options:

| Option | Effect |
|---|---|
| **True** | Single edge, drawn in green, labelled "True" (or your custom True label if you renamed it in the Condition editor). |
| **False** | Single edge, drawn in red, labelled "False". |
| **Both** | TWO edges — one True (green) + one False (red). The downstream node fires regardless of the Condition's result. |
| **Cancel** | No edge created. |

**What to do**: Pick the branch you want. The dialog reads the True/False labels from the Condition node's `conditionJson.trueBranch` / `falseBranch` fields, so if you renamed them to "Approved" / "Rejected" you'll see those names in the picker.

**Gotcha**: If you already dragged from one of the side handles (the explicit True or False output), the picker is skipped — the edge is wired directly to whichever handle you used. This is the FAST path; the picker exists for users who didn't notice the side handles.

### c. Edge perf hint advisory

**When it fires**: An edge connects two nodes whose configs don't reference each other through Output Variables. Specifically: the target node's serialized config does NOT contain `{{<source.outputVariable>.output.*}}` anywhere.

**What you see**: A yellow warning badge on the **source node's** Properties panel (in the NodeValidationBadge popover):

> "Edge from \"X\" to \"Y\" does not reference {{X.output.*}} in the target's configuration. This edge forces sequential execution. Confirm or remove?"

**What to do**:

- **Most common case**: you wired an edge by accident (intended to enforce ordering when none was needed). Delete the edge. Performance improves immediately — the orchestrator runs nodes in parallel within a batch as long as no edges force serialization.
- **Legitimate case**: you genuinely need side-effect-only sequencing (rare — e.g., one node writes a file that the next node reads via a side channel). Ignore the advisory. It's non-blocking; save succeeds.

The advisory is intentionally NOT a save-blocking error because legitimate cases exist. Use your judgment.

---

## Anti-Patterns to Avoid

These are the recurring mistakes that have broken playbooks in production. Each one has a real R2 incident behind it.

### 1. Hand-rolling FetchXML against a junction table that doesn't exist

**Anti-pattern**: Writing `<link-entity name="sprk_matterteammember" .../>` because "that's the obvious name for a matter-team-member junction."

**Why it broke**: `sprk_matterteammember` doesn't actually exist in production Dataverse. The query parsed fine, returned zero rows silently, and produced empty notifications for weeks.

**Use instead**: A `LookupUserMembership` node. Let the discovery service tell you which Lookup columns count as membership on `sprk_matter`.

### 2. Using `{{X ?? 'Y'}}` for fallbacks

**Anti-pattern**: `{{userPreferences.timeWindow ?? '24h'}}` (carried over from JavaScript / PCF muscle memory).

**Why it broke**: Handlebars.NET doesn't support `??`. The engine emits the literal text `?? '24h'` as part of the rendered output.

**Use instead**: `{{default userPreferences.timeWindow '24h'}}`.

### 3. Adding edges "to enforce ordering" when no data flows

**Anti-pattern**: Connecting A → B → C → D in a long chain when only D references B's output.

**Why it hurts**: Every edge forces sequential execution. The orchestrator can't run A, B, C in parallel even though A and C produce nothing B consumes.

**Use instead**: Connect only the edges that move data. The orchestrator schedules independent nodes in parallel within a batch (default 3-wide).

### 4. Reusing the same Output Variable name on two nodes

**Anti-pattern**: Two `QueryDataverse` nodes both named with `outputVariable: query`.

**Why it broke**: Downstream `{{query.output.field}}` references are ambiguous; the engine picks whichever happened to execute last.

**R3 protection**: The `outputvar-collision` validation rule fires as an ERROR (save-blocking) when two nodes share an Output Variable.

### 5. Renaming an Output Variable and forgetting downstream consumers

**Anti-pattern**: Rename `result` → `newDocsQuery` without updating the four downstream nodes that reference `{{result.output.*}}`.

**Why it broke**: Downstream nodes render the raw `{{result.output.count}}` literal at runtime.

**R3 protection**: The rename guard dialog auto-renames every downstream reference in one click. Don't bypass it.

### 6. Wiring a Condition node's body edge without specifying True/False

**Anti-pattern**: Drag from the Condition node body (not from a side handle) and accept the default edge type.

**Why it broke (pre-R3)**: The edge was created as a regular `smoothstep` edge with no branch metadata. The downstream node fired regardless of the Condition result — defeating the conditional logic.

**R3 protection**: The Branch Picker dialog forces you to choose True / False / Both before the edge is created.

### 7. Not setting an Output Variable on a node downstream consumers reference

**Anti-pattern**: Leaving Output Variable blank on a `QueryDataverse` node, then writing `{{output.count}}` in the next node hoping it'll work.

**Why it broke**: Without an Output Variable, the engine has no name to bind the result to. The template renders `{{output.count}}` literally.

**Use instead**: Always set an Output Variable. Pick a name that describes what the node produces (e.g., `myMatters`, `newDocsQuery`, `hasNewDocs`) — not `data` or `result`.

### 8. Assuming `iterateItems: true` means "one notification, summarizing all items"

**Anti-pattern**: Treating the `iterateItems` flag as a summary toggle.

**Why it broke**: `iterateItems: true` produces ONE notification per item (using the `itemNotification` template). If you want a single rolled-up notification, set `iterateItems: false` and use a `{{#each}}` loop in the body template.

**Use the right one for your case**: per-item notifications surface higher in the user's notification panel (each is individually clickable to the source record); rolled-up notifications keep the panel less cluttered when item counts are high.

### 9. Forgetting that idempotency dedupes on UNREAD only

**Anti-pattern**: Assuming "once per notification key, ever" semantics for `CreateNotification`.

**Why it surprises authors**: Once a user reads / dismisses the notification, the next scheduler tick will create a fresh notification with the same key (because the prior one is no longer unread). This is intentional — desirable for daily-update playbooks — but surprising if you wanted "send only once."

**Use instead**: If you genuinely need "once ever" semantics, implement it at the data layer (e.g., set a `notified=true` field on the source record after sending). Pitfall G4 in the architecture doc has the full discussion.

### 10. Mixing free-text display names with identity-typed Lookups

**Anti-pattern**: Trying to filter `sprk_matter` by `sprk_assignedattorney_displayname = 'Jane Doe'`.

**Why it doesn't work**: The membership service intentionally does NOT support matching against free-text display-name fields (explicitly out of scope). Display names aren't unique, aren't normalized, and aren't authoritative.

**Use instead**: The `LookupUserMembership` node resolves by `systemuserid` (the authoritative identifier). Let the resolver do its job.

---

## Migration: Replacing Broken FetchXML with LookupUserMembership

If you authored playbooks before R3 and they need this update, here's the worked diff. The three migrated R3 reference playbooks live at `projects/spaarke-daily-update-service/notes/playbooks/`.

### The A1 defect — what we're fixing

Three pre-R3 playbooks shared a common defect class: their "user's matters" filter either joined through a non-existent `sprk_matterteammember` table (silently returning zero rows) OR had no user-membership filter at all (returning every matter in the tenant). Both modes produced misleading results in production.

### Before / After diff — `notification-new-documents.json`

**Before** (R2): a single `QueryDataverse` node trying to join through a non-existent junction table.

```jsonc
// Old node — DOES NOT WORK
{
  "name": "Query New Documents",
  "canvasType": "updateRecord",
  "configJson": {
    "queryMode": true,
    "entityLogicalName": "sprk_document",
    "fetchXml": "<fetch>...<link-entity name='sprk_matterteammember' ...><filter><condition attribute='systemuserid' operator='eq-userid' /></filter></link-entity>...</fetch>"
  }
}
```

**After** (R3): a `LookupUserMembership` node feeding a downstream FetchXML via `{{joinIds}}`.

```jsonc
// New node 1 — resolve memberships
{
  "name": "Lookup My Matters",
  "canvasType": "lookupUserMembership",
  "actionType": 52,
  "outputVariable": "myMatters",
  "configJson": {
    "__actionType": 52,
    "entityType": "sprk_matter",
    "roles": ["owner", "assignedAttorney", "assignedParalegal"],
    "includeRelated": false
  }
},

// New node 2 — query using the resolved IDs
{
  "name": "Query New Documents",
  "canvasType": "updateRecord",
  "configJson": {
    "queryMode": true,
    "entityLogicalName": "sprk_document",
    "fetchXml": "<fetch top='50'><entity name='sprk_document'>...<filter><condition attribute='sprk_matter' operator='in' value='{{joinIds myMatters.ids}}' /><condition attribute='createdon' operator='last-x-hours' value='{{timeWindowHours}}' /></filter>...</entity></fetch>",
    "templateParameters": {
      "timeWindowHours": "{{default userPreferences.timeWindowHours '24'}}"
    }
  }
}
```

The other two migrated playbooks (`notification-new-emails.json`, `notification-new-events.json`) follow the same shape: `Start → LookupUserMembership → QueryDataverse with joinIds → Condition → CreateNotification`.

### How to audit your existing playbooks for similar issues

1. **Find candidate playbooks**: search your environment for `sprk_analysisplaybook` records where `sprk_canvaslayoutjson` contains `sprk_matterteammember` OR any other junction-table name you're not 100% certain exists.
2. **Verify the junction exists**: open the Power Apps maker portal → Tables → search. If the table isn't there, your FetchXML is silently failing.
3. **Even if junctions exist**, prefer `LookupUserMembership` over hand-rolled joins. The membership service auto-discovers every Lookup column on the parent entity that points to an identity table — so adding a new "assigned" column in Dataverse appears in your playbook results automatically (within an hour, or immediately after `POST /api/admin/membership/refresh-metadata`).
4. **Audit playbooks with NO user filter at all**: these silently iterate every row in the tenant. If a `Notification`-type playbook fans out a notification per row, it'll spam every user. Confirm the filter is there.

---

## Smoke-Test a Playbook Before Deploying

Before declaring a new playbook done, run it manually and inspect the result. This catches the 5% of issues the canvas validation can't predict (e.g., the Membership service isn't configured for the entity type you picked).

### Step 1 — Save the playbook

Press **Ctrl+S** in PlaybookBuilder. Confirm no save-blocking errors fire.

### Step 2 — Trigger the scheduler manually

The notification scheduler job is registered as `notification-playbook-scheduler`. Trigger it out-of-band via:

```http
POST /api/admin/jobs/notification-playbook-scheduler/trigger
Authorization: Bearer <SystemAdmin token>
```

This dispatches the scheduler immediately (independent of its hourly cron). Returns `202 Accepted` with a `runId`.

### Step 3 — Check the run status

```http
GET /api/admin/jobs/notification-playbook-scheduler/status
Authorization: Bearer <SystemAdmin token>
```

Look at the most recent run. Expected:

- `success`: `true`
- `errors`: `0`
- `processedItems > 0` if any user has memberships matching your playbook's criteria

If `processedItems = 0`:

- Either no user has memberships matching the playbook's filter (genuine zero state — your filter is too narrow for the test environment), OR
- The membership service isn't configured for the entity type — call `GET /api/admin/membership/discovered/{entityType}` and confirm `discoveredFields[]` includes the columns you expect

### Step 4 — Check the run history detail

```http
GET /api/admin/jobs/notification-playbook-scheduler/history?limit=5
Authorization: Bearer <SystemAdmin token>
```

Look for `UnrenderedTemplateDetected` events in the per-run log. If you see them, you have a `{{...}}` reference that didn't resolve — fix the reference and re-run.

### Step 5 — Verify the membership endpoint returns expected IDs

For one of the users the scheduler processed, impersonate (or grab their token) and call:

```http
GET /api/users/me/memberships/sprk_matter
Authorization: Bearer <user token>
```

Confirm the `ids[]` matches what you can verify by hand in the Dataverse model-driven app. If the endpoint returns IDs but your playbook's `LookupUserMembership` node returned empty — you have a config mismatch (different entity type, different role filter). Cross-check.

### Step 6 — Verify a notification was actually created

Open the user's notification panel in Power Apps (the bell icon top-right). Newly-created notifications appear within seconds of the scheduler run.

If notifications were created but the user can't see them: check the `recipientId` template in your `CreateNotification` node — usually `{{run.userId}}`. If the value is wrong, notifications get created against the wrong recipient.

---

## Related Documentation

- **Architecture** (technical reference for everything above): [`docs/architecture/playbook-architecture.md`](../architecture/playbook-architecture.md)
- **Membership Resolution operator guide** (one-time tenant setup for `LookupUserMembership`): [`docs/guides/MEMBERSHIP-RESOLUTION-GUIDE.md`](MEMBERSHIP-RESOLUTION-GUIDE.md)
- **Background Jobs operator guide** (scheduler admin endpoints, trigger / status / history): see [`docs/guides/`](.) for the background-jobs admin guide once published; admin endpoints under `/api/admin/jobs/*` are documented inline in [`JobsEndpoints.cs`](../../src/server/api/Sprk.Bff.Api/Api/Admin/JobsEndpoints.cs)
- **Pattern doc for developers adding NEW node types**: [`.claude/patterns/ai/node-executor-authoring.md`](../../.claude/patterns/ai/node-executor-authoring.md)
- **Playbook vs RAG decision tree** (when to author a playbook vs use generic RAG): [`docs/guides/INSIGHTS-PLAYBOOK-VS-RAG-DECISION-TREE.md`](INSIGHTS-PLAYBOOK-VS-RAG-DECISION-TREE.md)
- **R3 reference playbooks** (worked examples): `projects/spaarke-daily-update-service/notes/playbooks/` — `notification-new-documents.json`, `notification-new-emails.json`, `notification-new-events.json`
- **R3 spec** (origin of every R3 requirement cited here): `projects/spaarke-platform-foundations-r3/spec.md` — particularly §FR-1B (membership resolution + node), §FR-3H1 (Handlebars helpers), §FR-3H2 (Builder UI affordances), §FR-3H3 (canvas-server drift CI test)
- **ADR-034** (binding rules for user-record membership resolution): [`.claude/adr/ADR-034-user-record-membership.md`](../../.claude/adr/ADR-034-user-record-membership.md)

---
