# Task 067 — Service Request autonumber: record

Owner approvals: "yes service requests should get automatic numbers" (2026-10-13), and "067 - yes run it" (2026-10-13,
after the commands and rollback below were shown).

## Solution change

`src/dataverse/solutions/SpaarkeMaster/Entities/sprk_ServiceRequest/Entity.xml`: `sprk_servicerequestnumber`
`<AutoNumberFormat>RQST-{SEQNUM:4}</AutoNumberFormat>` (was empty). The XML is well-formed. The seed is NOT part of
the solution: each environment starts at the platform default unless `SetAutoNumberSeed` runs there.

## Dev (spaarkedev1), 2026-10-13

Script: session scratchpad `task067-autonumber.ps1` (`-Mode Read | Apply | Rollback`). The token stays in a
variable and is never printed. The steps that ran:

| # | Step | Result |
|---|---|---|
| 1 | Read: GET attribute metadata; GET `sprk_servicerequests?$select=sprk_servicerequestnumber&$filter=startswith(sprk_servicerequestnumber,'RQST-')` | Format empty; 1 RQST row; highest 1 |
| 2 | Apply, first run: PUT attribute | **400**, no write: the body lacked `@odata.type` (`Microsoft.Dynamics.CRM.StringAttributeMetadata`), so it parsed as base AttributeMetadata. Publish and seed did not run |
| 3 | Apply, second run (with `@odata.type`): PUT attribute (`MSCRM.MergeLabels: true`) + `PublishXml` for `sprk_servicerequest` | Succeeded. `SetAutoNumberSeed` right after answered "not an Auto Number attribute" (metadata cache) |
| 4 | Read back with `Consistency: Strong` | `AutoNumberFormat = RQST-{SEQNUM:4}` |
| 5 | `POST SetAutoNumberSeed {EntityName: sprk_servicerequest, AttributeName: sprk_servicerequestnumber, Value: 2}` | Succeeded |
| 6 | `POST GetNextAutoNumberValue` (action, not a function) | `3` |

The next value read 3 after the seed of 2. Either the action reports one ahead or a call advanced the sequence; in
both cases the next number (RQST-0002 or RQST-0003) cannot collide with the one existing value, RQST-0001 (hand-typed,
left as is per the owner).

## Rollback

`-Mode Rollback`: PUT the attribute with `AutoNumberFormat = ''` (with `@odata.type`) and `PublishXml`; read back with
`Consistency: Strong`. The seed has no effect without a format. Numbers already issued stay on their records.

## Open

- Owner test: create a service request (SPA or model-driven app) and confirm it gets an RQST number other than
  RQST-0001.
- Other environments: run `SetAutoNumberSeed` after the solution import if they need numbering to start low.
