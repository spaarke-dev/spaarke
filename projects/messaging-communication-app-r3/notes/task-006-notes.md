# Task 006 notes — doc-drift fix: `docs/data-model/sprk_communication.md`

**Rigor**: MINIMAL (docs-only). **Scope**: `docs/data-model/sprk_communication.md` only (plus this notes file). No `src/`, `tests/`, or `.claude/` files touched.

## What was verified and how

1. **`sprk_communicationtype` = `100000004: Message`** — confirmed against `src/server/api/Sprk.Bff.Api/Services/Communication/Models/CommunicationType.cs`:
   `Email=100000000, TeamsMessage=100000001, SMS=100000002, Notification=100000003, Message=100000004`.
   Doc's `sprk_communicationtype` row was missing the `100000004: Message` option — added.

2. **R1 messaging columns on `sprk_communication`** — cross-checked `projects/messaging-communication-app-r1/notes/messaging-schema-spec.md` (§5.1, marked `[x]` **VERIFIED live in spaarkedev1 via Dataverse MCP `describe`, 2026-07-16**) against actual code usage in `Services/Communication/**`:

   | Column | Code verification | Included |
   |---|---|---|
   | `sprk_acsmessageid` | Read/written in `CommunicationService.cs` (`communication["sprk_acsmessageid"] = sendResult.ProviderMessageId`) and `Channels/MessagingIngestor.cs` | ✅ added |
   | `sprk_acsthreadid` | Same two files + referenced in `Threads/MessagingThreadKeyStrategy.cs` | ✅ added |
   | `sprk_communicationthread` (lookup → `sprk_communicationthread`) | Read as `_sprk_communicationthread_value` in `CommunicationThreadReadService.cs`; written as `EntityReference` in `CommunicationService.cs` | ✅ added |
   | `sprk_isinternalonly` | Read/enforced in `Access/CommunicationAccessFilter.cs` (D-05 internal-only rule), consumed by every read path in `CommunicationThreadReadService.cs` | ✅ added |
   | `sprk_privilegeclassification` | Read/enforced in `Access/CommunicationAccessFilter.cs` and `CommunicationThreadReadService.cs` | ✅ added |
   | `sprk_isprivate` | **NOT found** anywhere under `src/server/api/Sprk.Bff.Api/` (grepped whole BFF tree, case-insensitive) | ⚠️ added, but flagged (see below) |

## Flagged item (escalation per task 006 `<escalation>` trigger)

`sprk_isprivate` is listed in the R1 as-built schema notes (`messaging-schema-spec.md`) as **verified live in Dataverse** (MCP `describe`, 2026-07-16), so it is a real column and not invented. However, a repo-wide grep of `src/server/api/Sprk.Bff.Api/` found **no code path that reads or writes it** — only `sprk_isinternalonly` and `sprk_privilegeclassification` are wired into `CommunicationAccessFilter.cs`. I added the row to the doc (source: as-built schema, which the task explicitly allows as a verification source alongside code) but annotated its description with "NOT currently read/written by any BFF code path found in `Services/Communication/**` as of this correction — flagged for confirmation," rather than presenting it as an active, consumed field. No further action taken — this is an observation for the project owner to confirm intent (deferred field vs. dead schema), not a doc error to silently fix by omission or invention.

## Schema-name casing caveat

The as-built schema spec (`messaging-schema-spec.md`) only records **logical names** for the new columns (verified via MCP `describe`), not `SchemaName` casing. I inferred `SchemaName` for the six new rows using the same `sprk_` + PascalCase-suffix convention every other row in this doc already follows (100% consistent across ~140 existing rows, e.g. `sprk_communicationtype` → `sprk_CommunicationType`). This is a documentation-formatting inference, not a functional claim — Logical Name (the field actually used in OData `$select`/`$filter` and C# dictionary keys) is the verified, load-bearing value in every case.

## Diff scope

Only `docs/data-model/sprk_communication.md` was modified, plus this notes file (`projects/messaging-communication-app-r3/notes/task-006-notes.md`). No `src/`, `tests/`, or `.claude/` changes. `TASK-INDEX.md` intentionally left untouched per the calling instruction (main session finalizes status).
