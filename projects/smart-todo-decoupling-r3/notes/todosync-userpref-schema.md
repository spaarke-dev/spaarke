# `MicrosoftToDoSync` User-Preference Schema

> **Task**: 017 (Phase 6: Graph Foundation)
> **Status**: Schema registered in dev (`spaarkedev1`); JSON shape documented
> **Date**: 2026-06-07
> **Source authority**: design.md §6.6 (User preference), FR-19, FR-20

---

## 1. Host entity

`sprk_userpreferences` (existing entity, owned by `SpaarkeCore` unmanaged solution). One-row-per-user-per-preference-type pattern, keyed by `sprk_userid` (lookup to `systemuser`) plus `sprk_preferencetype`.

Relevant columns:

| Column | Type | Role |
|---|---|---|
| `sprk_userpreferencesid` | GUID (PK) | Row id |
| `sprk_userid` | LOOKUP → `systemuser` | Owner of the preference; **per-user key** |
| `sprk_preferencetype` | CHOICE (local optionset) | Discriminator — selects the JSON schema applied to `sprk_preferencevalue` |
| `sprk_preferencevalue` | MULTILINE TEXT | JSON payload (UTF-8, no BOM) — shape determined by `sprk_preferencetype` |
| `sprk_name` | NVARCHAR(850), NOT NULL | Primary-name field; convention: `"{userPrincipalName}:{PreferenceTypeLabel}"` |
| `statecode` / `statuscode` | STATE / STATUS | Active / Inactive (use `Inactive` to soft-delete an opt-out without losing the row) |

---

## 2. Preference-type registration

`sprk_preferencetype` is a **local** optionset on `sprk_userpreferences.sprk_preferencetype`. Existing options + the new one:

| Value | Label | Notes |
|---|---|---|
| `100000000` | Theme Mode | pre-existing |
| `100000001` | To Do Thresholds | pre-existing (NOT "MicrosoftToDoSync" — design.md §6.6 hint at `100000001` was stale) |
| `100000002` | Entities | pre-existing |
| `100000003` | Theme Preference | pre-existing |
| **`100000004`** | **Microsoft To Do Sync** | **NEW (task 017, this commit). Logical marker: `MicrosoftToDoSync`.** |

Registered via `InsertOptionValue` Web-API action against the local optionset, scoped to solution `SpaarkeCore`. Published. Verified via `EntityDefinitions(...).../OptionSet` reflection and via `mcp__dataverse__describe tables/sprk_userpreferences`.

**Portability**: because the option was inserted with `SolutionUniqueName = SpaarkeCore`, it travels in solution export to every downstream tenant. No hardcoded env URLs.

---

## 3. `sprk_preferencevalue` JSON shape (for `MicrosoftToDoSync`)

```json
{
  "enabled": true,
  "listId": "AAMkAGI2…",
  "subscriptionId": "00000000-0000-0000-0000-000000000000",
  "expiresUtc": "2026-06-08T10:00:00Z",
  "initialBackfillCompletedUtc": "2026-06-07T15:30:00Z"
}
```

| Field | Type | Required | Set / updated by | Meaning |
|---|---|---|---|---|
| `enabled` | `boolean` | yes | User opt-in/opt-out UI (writes via BFF) | `true` after the user opts in; `false` after opt-out. Drives whether the sync engine processes this user's todos. Initial backfill is triggered on the `false → true` transition (FR-19, design §6.6). |
| `listId` | `string` (Graph `todoTaskList.id`) | yes (once provisioned) | BFF `TodoListProvisioner` on first opt-in (FR-19) | Id of the "Spaarke" `todoTaskList` under `/me/todo/lists/`. Set once on first opt-in; **reused on subsequent opt-ins** — no duplicate list created (FR-19). If the user deletes the list in MS To Do, the provisioner detects 404 on next sync and re-provisions, replacing `listId`. |
| `subscriptionId` | `string` (GUID, Graph `subscription.id`) | yes (once active) | BFF `GraphSubscriptionManager` on subscription create / renew | Id of the Graph change-notification subscription for resource `/me/todo/lists/{listId}/tasks`. Used by the webhook handler to validate that the inbound notification's `subscriptionId` matches an active subscription (binding rule from spec.md, Technical Constraints §"MUST validate Graph change notifications"). |
| `expiresUtc` | `string` (ISO-8601 UTC, `Z`-suffixed) | yes (once active) | BFF `GraphSubscriptionManager` on subscription create / renew | Subscription expiry timestamp. The renewal job reads this to decide which subscriptions to renew (renew when within renewal-window). |
| `initialBackfillCompletedUtc` | `string` (ISO-8601 UTC) **or** `null` | yes (field always present; value nullable) | BFF backfill job on completion of first-opt-in backfill (FR-20) | `null` until the first-opt-in backfill completes; set to the completion timestamp once it does. **Resumability marker** (FR-20): if BFF restarts mid-backfill, the resume job knows backfill is incomplete because this field is still `null`. After backfill completes, this field is never re-cleared (re-opt-in after opt-out does not re-trigger backfill — the list already contains the user's todos). |

### 3.1 Field-presence invariants

- On a freshly-provisioned row (just after first opt-in, before list provisioning completes): `enabled = true`, all four other fields `null` or absent.
- On a fully-active row: all five fields populated.
- On an opt-out row: `enabled = false`; other fields retained so re-opt-in re-uses the same `listId` + subscription state.

### 3.2 Logical type-marker string

Code that needs to refer to this preference type symbolically should use the magic string **`"MicrosoftToDoSync"`** in TypeScript / C# constants. Map to the optionset value at the DB boundary:

```ts
export const PreferenceType = {
  ThemeMode: 100000000,
  ToDoThresholds: 100000001,
  Entities: 100000002,
  ThemePreference: 100000003,
  MicrosoftToDoSync: 100000004,
} as const;
```

---

## 4. One-row-per-user pattern

- **Key**: `(sprk_userid, sprk_preferencetype)` — application-enforced (no alternate-key constraint on the table).
- **Cardinality**: exactly one Active row per `(systemuser, MicrosoftToDoSync)` tuple.
- **Lifecycle**:
  - On first opt-in: BFF queries `sprk_userpreferences` for `sprk_userid eq {userid} and sprk_preferencetype eq 100000004 and statecode eq 0`. If no row, insert one with `enabled = true` and the JSON skeleton. If a row exists (re-opt-in case), update `enabled = true` and reuse existing `listId` + `subscriptionId` (FR-19).
  - On opt-out: update `enabled = false`; do NOT delete the row, do NOT clear `listId` / `subscriptionId` / `initialBackfillCompletedUtc` (re-opt-in safety).
  - On subscription renewal: BFF updates `subscriptionId` + `expiresUtc` (rare — subscription id changes only when recreated).

Duplicate-row guard: the opt-in code path MUST query first and upsert; do NOT blind-insert. (No DB-level uniqueness constraint, so the safeguard is in application logic.)

---

## 5. NO rows are provisioned in task 017

Per the task contract, **no `sprk_userpreferences` rows are created** in this task. Row provisioning happens at first opt-in (task 063, Phase 7) via the BFF.

---

## 6. Verification commands

```pwsh
# Confirm the option exists in dev
pwsh -Command @"
  `$token = (az account get-access-token --resource 'https://spaarkedev1.crm.dynamics.com' --query accessToken -o tsv)
  `$h = @{Authorization=\"Bearer `$token\"; Accept='application/json'}
  `$u = 'https://spaarkedev1.crm.dynamics.com/api/data/v9.2/EntityDefinitions(LogicalName=''sprk_userpreferences'')/Attributes(LogicalName=''sprk_preferencetype'')/Microsoft.Dynamics.CRM.PicklistAttributeMetadata?`$expand=OptionSet'
  (Invoke-RestMethod -Uri `$u -Headers `$h).OptionSet.Options | ForEach-Object { '{0}={1}' -f `$_.Value, `$_.Label.UserLocalizedLabel.Label }
"@
# Expect: 100000004=Microsoft To Do Sync (among others)
```

Or via Claude Code MCP: `mcp__dataverse__describe tables/sprk_userpreferences` — the `sprk_preferencetype` choice line MUST contain `Microsoft To Do Sync (100000004)`.

---

## 7. Forward references

- **Task 063** (Phase 7) — `TodoListProvisioner` service: reads/writes this row on first opt-in.
- **Task 065** (Phase 7) — initial backfill job: sets `initialBackfillCompletedUtc` on completion (FR-20).
- **Task 068** (Phase 7) — webhook handler: validates inbound `subscriptionId` against active row's `subscriptionId`.

These three tasks are unblocked by task 017.
