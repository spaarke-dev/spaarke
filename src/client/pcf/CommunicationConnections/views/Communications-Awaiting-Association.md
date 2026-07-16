# "Communications Awaiting Association" — Dataverse view (FR-17 / task 042)

A saved public view on `sprk_communication` that triages every communication still
needing human association review. Deployed by **task 043** (packed into the
solution alongside the PCF); authored here so the FetchXML/layoutXML travel with
the PCF.

## Filter — REVIEW statuses only (task-002-verified integers)

`sprk_associationstatus` **in** the four review states — the same set the PCF's
`REVIEW_STATUSES` uses. Resolved (`100000000`) is excluded (already filed):

| Status | Integer |
|---|---|
| Pending Review | `100000001` |
| Unresolved (legacy → Pending Review) | `100000002` |
| Suggested | `100000003` |
| Ambiguous | `100000004` |

## Auth scoping (NFR-07 / ADR-003 / ADR-008)

The view carries **no** explicit auth filter. Matter-level scoping is enforced by
the Dataverse **record-level security model** — the view returns only rows the
running user has Read privilege on (owner/BU/matter-team sharing). Adding a
FetchXML filter would duplicate (and could contradict) that model. This is the
standard OOB pattern: security trims the result set, the view defines the slice.

## AI privilege = flag, never decided (ADR-015)

Per ADR-015 the AI privilege signal is **shown, never acted on**. R4's
deterministic rungs (0–3) do not emit a persisted privilege column, so the view
below stays on real R4 columns. When the AI-privilege flag column lands (AI rungs
4–5, future wave), add it as a **display-only** column here — the view MUST NOT
filter or sort on it (that would let AI "decide" visibility).

## FetchXML

```xml
<fetch version="1.0" mapping="logical" no-lock="true">
  <entity name="sprk_communication">
    <attribute name="sprk_communicationid" />
    <attribute name="sprk_subject" />
    <attribute name="sprk_communicationtype" />
    <attribute name="sprk_from" />
    <attribute name="sprk_to" />
    <attribute name="sprk_receiveddate" />
    <attribute name="sprk_associationstatus" />
    <order attribute="sprk_receiveddate" descending="true" />
    <filter type="and">
      <condition attribute="sprk_associationstatus" operator="in">
        <value>100000001</value>
        <value>100000002</value>
        <value>100000003</value>
        <value>100000004</value>
      </condition>
    </filter>
  </entity>
</fetch>
```

## layoutXml

```xml
<grid name="resultset" object="10008" jump="sprk_subject" select="1" icon="1" preview="1">
  <row name="result" id="sprk_communicationid">
    <cell name="sprk_subject" width="280" />
    <cell name="sprk_associationstatus" width="140" />
    <cell name="sprk_from" width="200" />
    <cell name="sprk_to" width="200" />
    <cell name="sprk_receiveddate" width="140" />
    <cell name="sprk_communicationtype" width="120" />
  </row>
</grid>
```

## savedquery metadata (for 043 solution pack)

- **name**: `Communications Awaiting Association`
- **returnedtypecode**: `sprk_communication`
- **querytype**: `0` (public view)
- **isdefault**: `false`
- **fetchxml / layoutxml**: as above

> Task 043 owns packing this into `customizations.xml` as a `<savedquery>` node and
> importing it. Verify the four integers against `docs/data-model/sprk_communication.md`
> at import time (task 002 is the source of truth).
