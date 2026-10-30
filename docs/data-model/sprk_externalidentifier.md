# sprk_externalidentifier (External Identifier)

> **Project**: ebilling-r1, task 022 (FR-12, FR-05, D-12, D-68)
> **Last Updated**: 2026-10-15
> **Script**: `scripts/ebilling/Deploy-EbillingExternalIdentifierSchema.ps1` (idempotent, additive; a second run changes nothing)
> **Solution**: `SpaarkeMaster` (unmanaged, in dev). Solution source, forms and views are synced by task 029.

A typed identifier quoted by an issuer for a record: the firm's invoice number, the firm's client-matter number, the law department's matter number, a PO as quoted. One invoice or matter can carry several identifiers from different issuers (firm, law department, AP, tax authority), which single columns on `sprk_invoice` cannot hold (D-12). The R1 consumers are the submission operation (task 044), which writes the rows and resolves a firm-quoted client-matter number to its core record **for that firm only**, and the Duplicate rule (task 071), which looks up (firm, type, value). Created additively in dev on 2026-10-15; no plugin, business rule, calculated or rollup column, or flow (ADR-002).

User-owned, entity set `sprk_externalidentifiers`, primary name `sprk_name` (text 200, application-required; normally type and value).

## Columns

| Logical name | Type | Notes |
|---|---|---|
| `sprk_name` | Text (200), primary name | Display name of the identifier. |
| `sprk_identifiertype` | Choice (local), **system-required** | The eight values below. |
| `sprk_identifiervalue` | Text (200) | The identifier exactly as quoted. |
| `sprk_normalizedvalue` | Text (200), **system-required** | The matching value; see "Normalization". Part of the alternate key. |
| `sprk_issuingsystem` | Text (200) | The system or ledger that issued the identifier, when known. |

### Identifier types (`sprk_identifiertype`)

A closed choice. The four R1 types are in use; the four reserved ones exist now so a later channel needs no schema change.

| Value | Label | Status |
|---|---|---|
| 100000000 | Firm invoice number | R1 |
| 100000001 | Firm client-matter number | R1 |
| 100000002 | Law-department matter number | R1 |
| 100000003 | PO as quoted | R1 |
| 100000004 | AP document number | reserved |
| 100000005 | VAT invoice number | reserved |
| 100000006 | E-invoice / clearance ID | reserved |
| 100000007 | Peppol message ID | reserved |

### Normalization

`sprk_normalizedvalue` is `sprk_identifiervalue` upper-cased with all whitespace removed, so matching does not depend on case or spaces (`abc 123` and `ABC123` match). The rule is applied by the writer, task 044; the schema does not compute it (no calculated column, ADR-002). Both `sprk_identifiertype` and `sprk_normalizedvalue` are raised to **SystemRequired**, because the platform enforces that level on API writes (checked live: a create without either is refused with `Attribute ... cannot be NULL`), whereas ApplicationRequired is enforced by forms only (DEF-009).

## Lookups

All are optional; every relationship has Delete = RemoveLink and no other cascade.

| Logical name | Target | Relationship | Meaning |
|---|---|---|---|
| `sprk_issuingorganization` | `sprk_organization` | `sprk_externalidentifier_issuingorg_organization` | The organization (the firm) that issued the identifier. Part of the alternate key. |
| `sprk_regardinginvoice` | `sprk_invoice` | `sprk_externalidentifier_reginvoice` | The invoice the identifier identifies (firm invoice number, PO). Not one of the ADR-024 core records; added because the firm invoice number identifies an invoice. |

## Core-record regarding (ADR-024, FR-05)

Three entity-specific lookups plus the five resolver fields, named as `CoreAncestorResolver.CoreAncestorLookups` expects (`sprk_regarding{core}`). At most one entity-specific lookup is populated per row and the resolver fields are filled together: both are enforced server-side by task 013, not by schema.

| Logical name | Type | Relationship / length |
|---|---|---|
| `sprk_regardingmatter` | Lookup -> `sprk_matter` | `sprk_externalidentifier_regmatter` |
| `sprk_regardingproject` | Lookup -> `sprk_project` | `sprk_externalidentifier_regproject` |
| `sprk_regardingworkassignment` | Lookup -> `sprk_workassignment` | `sprk_externalidentifier_regworkassignment` |
| `sprk_regardingrecordtype` | Lookup -> `sprk_recordtype_ref` | `sprk_externalidentifier_regrecordtype_recordtype_ref` |
| `sprk_regardingrecordid` | Text (100) | resolver field; part of the alternate key |
| `sprk_regardingrecordname` | Text (200) | resolver field |
| `sprk_regardingrecordnumber` | Text (100) | resolver field |
| `sprk_regardingrecordurl` | Text (500), format Url | resolver field |

Lengths follow `projects/spaarke-ontology-platform-r1/notes/140-schema-delta.md` section 2. Naming: new tables use the `sprk_regarding*` trio (the mixed scheme recorded in task 013; `sprk_invoice` itself keeps the typed `sprk_matter` / `sprk_project`).

## Alternate key (D-68)

`sprk_externalidentifier_firm_type_value_record` on (`sprk_issuingorganization`, `sprk_identifiertype`, `sprk_normalizedvalue`, `sprk_regardingrecordid`), index status **Active** (Dataverse lists the attributes alphabetically: `sprk_identifiertype`, `sprk_issuingorganization`, `sprk_normalizedvalue`, `sprk_regardingrecordid`). The owner decided the composition on 2026-10-15 (decisions-log D-68): uniqueness includes the core record, so the same firm-quoted number can legitimately sit on two core records; lookups by (firm, type, value) are therefore **filtered queries**, and an ambiguous match (one value on two core records) is refused by the resolver in task 044.

Checked live in dev (raw Web API, test rows deleted afterwards):

| Case | Result |
|---|---|
| Firm 1 / Firm client-matter number / `ABC123` / record `R1` | created |
| the same four values again | **refused**, HTTP 412, `0x80060892` "Entity Key Firm, Type, Value and Record violated" |
| firm 2, same type / value / record | accepted (firm-scoped) |
| firm 1, same type / value, record `R2` | accepted (core-record-scoped, D-68) |
| firm 1, other type, same value / record | accepted |
| firm 1, same type, other value | accepted |
| filtered query on (firm 1, type, `ABC123`) | returns both the `R1` and the `R2` row |

A row is **covered by the key only when all four key columns hold a value**: two rows with the same firm, type and value and no `sprk_regardingrecordid` were both accepted, and so were two rows with no issuing organization (checked live). The writer (task 044) must therefore always fill the issuing organization and the resolver record id for a firm-quoted identifier, or the duplicate check silently does not apply. Type and normalized value cannot be empty (system-required).

Addressing a row by the key in the Web API uses the lookup **value** name for the lookup and the numeric value for the choice: `sprk_externalidentifiers(_sprk_issuingorganization_value=<firm id>,sprk_identifiertype=100000001,sprk_normalizedvalue='ABC123',sprk_regardingrecordid='<record id>')`. The attribute names in the other forms tried (`sprk_issuingorganization=...`, reordered properties) were rejected with HTTP 400. One attempt shortly after the key turned Active was also rejected (`0x80060888` "key in the request URI is not valid") and the same form worked minutes later; the cause was not established (likely the metadata cache), so a first by-key call after a key change may need a retry.

## Notes for the writers

- The Web API navigation names follow the lookup schema names: `sprk_IssuingOrganization@odata.bind`, `sprk_RegardingInvoice@odata.bind`, `sprk_RegardingMatter@odata.bind`, and so on; value properties are `_sprk_issuingorganization_value` etc.
- Secure-record family (task 013, for unified-access-control-r2 review): a child table in `config/secure-record-owner-role.json`, `SecureChildLineage` (every core-record lookup plus its invoice / line / document parents) and `RecordOwnerAssignmentCensusTests.ChildTables`; the Secure Record Owner role holds Read on it (dev, 2026-10-15). It carries `sprk_createdbyperson` (lookup to systemuser, field-secured so only the BFF writes it; created by this table's schema script, secured by `scripts/Set-ChildRecordCreatorPersonSchema.ps1`, which lists the table with SpaarkeMaster as its solution home).
- Invoice-only rows (task 013, D-68): a row whose only regarding lookup is `sprk_regardinginvoice` regards the invoice. `sprk_regardingrecordid` holds the invoice id and the other four resolver fields describe the invoice (record type invoice, `sprk_invoicename`, `sprk_invoicenumber`, its URL), so the alternate key never has an empty column. The invoice is never walked to its matter or project. `InvoiceFamilyCoreRecordGuard` derives these values; a row naming a core record AND an invoice regards the core record.
