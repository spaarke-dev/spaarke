# Per-record No Access read: frozen contract (task 064)

> Frozen 2026-10-08 by task 064 for its two consumers, task 153 (form banner) and task 067 (Manage Access, read-only No
> Access section). Scope as narrowed by owner round 59 item 3: ONE route, project / matter / work assignment only.
> The provenance report and the deny-list add/remove routes in this note's original plan were cut by round 59.
> Code: `src/server/api/Sprk.Bff.Api/Api/ExternalAccess/RecordNoAccessEndpoint.cs`,
> `Api/ExternalAccess/Dtos/RecordNoAccessDtos.cs`.

## Route

```
GET /api/v1/records/sprk_project/{recordId}/no-access
GET /api/v1/records/sprk_matter/{recordId}/no-access
GET /api/v1/records/sprk_workassignment/{recordId}/no-access
```

Workforce (Azure AD) bearer token, the BFF's default policy. Any other record type has no route (404).

## Who gets what

| Caller's rights on the record (Dataverse, asked AS THE CALLER over OBO) | Answer |
|---|---|
| No Read; no bearer token; record does not exist; the rights check faulted | **404**, the uniform body (`reasonCode` `sdap.access.deny.record_unavailable`, same detail for all). Nothing else is read. |
| Read | **200**: `secure`, `noAccess`; `entriesState` = `notShown`, `entries` = `null` |
| Read + Write | **200**: as above, plus `entries` (the covering entries) |
| Not signed in | **401** |

The Write decision reuses the rights the gate's probe returned (`RecordRouteAccessAuthorizationFilter` publishes them for
the record it checked); no second probe and no app-only read stands in for the caller's gate.

## Response (200)

```jsonc
{
  "recordType": "sprk_project",           // echo of the route's type
  "recordId": "…",                        // echo; a client compares it with the record it asked about
  "secure": "applies" | "doesNotApply" | "unknown",
  "noAccess": "applies" | "doesNotApply" | "unknown",
  "entriesState": "notShown" | "complete" | "truncated" | "unavailable",
  "entries": null | [ /* RecordNoAccessEntry, Write callers only */ ]
}
```

### `secure`

`sprk_issecure` from the batched flag read every veto uses (`ExternalParticipationService.GetRootRecordFlagsAsync`):
true → `applies`, false → `doesNotApply`. **`unknown`** when the read faulted, did not return the row, or the value came
back EMPTY (`RootRecordFlags.IsUnreadable`; task 150: an empty value means field-level Read was lost and a true value may
be masked). Never "not secure" for any of these.

### `noAccess`

`applies` when at least one ACTIVE entry IN FORCE on the record covers it (`inForce: true`, see below). "Covers" is the enforcer's own lookup
(`NoAccessShareEnforcer.ReadCoverageAsync`, shared with "Update Access"):

1. an entry whose object record id is this record (id equality, as every deny reader matches);
2. an entry whose object organization is ANY organization the record references in an org-typed lookup (B-10);
3. the same two for every SECURE record this one is filed under, up the chain (round 61 item 1).

An entry is **in force** on the record when:

- it is well-formed (`NoAccessShareEnforcer.TryClassify`: schema Business Rule 1 plus task 154's canonical id; exactly one
  subject, and either an object organization alone or an object record type plus a canonical record id). Otherwise
  `inForce: false`, `notInForceReason: "malformed"`.
- and, for a USER wall (`subjectKind: "systemuser"`), the record is Secure (owner Q4: a user wall binds only Secure
  records; on a non-secure record the read-time veto and the enforcer remove nothing for it). On a record whose `secure`
  is `doesNotApply` it is `inForce: false`, `notInForceReason: "userWallOnNonSecureRecord"`; when `secure` is `unknown`
  it is `inForce: null`, `notInForceReason: "secureStateUnknown"`. A user wall reached through a secure PARENT
  (`viaSecureParent: true`, or `alsoViaSecureParent: true` when it reaches the record directly as well) is in force
  whatever this record's own flag reads: the parent is secure (round 61).
- A contact or organization wall is in force on any record (the contact plane; owner N3 on non-secure records).

`doesNotApply` only when every covering entry was read and none is in force or undecided. **`unknown`** when the
referenced organizations, the filing walk, the covering query or any entry read faulted; when an entry is undecided
(`inForce: null`) and none is in force; or when more entries cover the record than one read lists (100,
`NoAccessShareEnforcer.MaxEntriesPerRecord`) and none of the listed ones is in force.

### `entriesState` / `entries`

| `entriesState` | `entries` | Meaning |
|---|---|---|
| `notShown` | `null` | The caller holds Read but not Write (owner O2). |
| `complete` | array (maybe empty) | Every active covering entry is listed. |
| `truncated` | array of 100 | More cover the record; the list is a prefix and says so. |
| `unavailable` | `null` | The entries could not be read (`noAccess` is then `unknown`). Show an error, never "no entries". |

### `RecordNoAccessEntry`

| Field | Type | Notes |
|---|---|---|
| `entryId` | guid | `sprk_noaccessentryid` |
| `name` | string? | `sprk_name` |
| `subjectKind` | `contact` \| `organization` \| `systemuser` \| null | null when malformed |
| `subjectId` | guid? | who is walled off |
| `subjectName` | string? | Dataverse's formatted lookup value |
| `objectKind` | `record` \| `organization` \| null | null when malformed |
| `objectOrganizationId` / `objectOrganizationName` | guid? / string? | for an organization wall |
| `coveredRecordType` / `coveredRecordId` | string / guid | the record the entry covers this one THROUGH: this record, or a secure parent |
| `viaSecureParent` | bool | true when it covers this record through a secure record it is filed under |
| `alsoViaSecureParent` | bool | true when it reaches this record on its listed path AND (again) through a secure parent (e.g. this record and the parent both reference the walled organization); listed once, on its first path (this record's own when it has one) |
| `malformed` | bool | true: walls nobody off; listed so it can be fixed |
| `inForce` | bool? | true: walls someone off this record; false: listed but inert here; null: undecided (Secure flag unread) |
| `notInForceReason` | string? | `malformed` / `userWallOnNonSecureRecord` / `secureStateUnknown`; null when `inForce` is true |
| `modifiedById` / `modifiedByName` / `modifiedOn` | guid? / string? / datetime? | the last modifier is the author owner N5 checks |

**Never returned:** the entry's Reason (task 143: a refusal never reveals it; O2 did not extend it to Write holders), and
nothing about inactive entries.

## Consumer rules

- **153 (banner):** render `applies` per signal; render ANY `unknown`, any non-200 (including 404), an unparseable body or
  a `recordId` that does not match the form's record as "Access status unavailable". Never render a count or an identity.
- **067 (Manage Access):** show the list when `entriesState` is `complete` or `truncated` (with a "more entries exist"
  line for `truncated`); `unavailable` is an error state; `notShown` means the section is not shown. Show every row,
  and mark a row whose `inForce` is not `true` as not in force with its `notInForceReason` (for
  `userWallOnNonSecureRecord`: "user walls apply only to secure records"); label `viaSecureParent` rows with the parent.
  Only an `inForce: true` row drives a veto marker. A veto marker on a Current Access row matches `subjectId` (contact
  or systemuser) or, for an organization subject, the contact's organization.

## Not in this contract (cut by owner round 59)

The provenance report endpoint, deny-list add/remove routes (authoring is 154's form; enforcement is 143's
`/no-access/enforce`), the per-entry enforcement state, a per-record method on `NoAccessListReader`, and organization /
contact records.
