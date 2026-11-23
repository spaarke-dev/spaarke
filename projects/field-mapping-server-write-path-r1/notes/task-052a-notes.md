# Task 052a — Repair reads column metadata app-only

The first slice of task 052: it wires an app-only column-metadata reader into Repair mode (033's piece, moved here
2026-11-07). The scheduled job, the admin trigger, the report and the family cascade are later slices. Rigor: FULL.

## What changed

- **Reader.** `IGenericEntityService.GetColumnMetadataAsync(table)` returns every column's `AttributeType`,
  `IsValidForUpdate`, `IsSecured` and text/memo `MaxLength` as a `ColumnMetadata` record (`Spaarke.Dataverse/Models.cs`).
  - It is one `RetrieveEntityRequest` (attributes, published metadata) in `DataverseServiceClientImpl`, projected by
    `ColumnMetadataOf`, and uncached.
  - The default interface body throws, like `ExecuteTransactionAsync`, so a substitute that does not model metadata
    fails closed.
- **Repair (`RecordCreationService.Repair.cs`, `RepairMappingAsync`).** Before reading anything from the source, Repair
  reads the metadata of both tables.
  - **Source:**
    - a field-secured mapped column is neither read for mapping nor handed to the engine;
    - a column the table does not have is left out;
    - a fault, an empty answer, or a mapped column with no `IsSecured` maps nothing:
      `record_repair.mapping_source_metadata_unavailable`.
  - **Engine input:** only the copyable columns. A gate column (`ownerid`, `sprk_issecure`) is read for the gate, never
    copied unless a rule names it and it is not secured.
  - **Target:**
    - a fault or an empty answer maps nothing: `record_repair.mapping_target_metadata_unavailable`;
    - a mapped column that is missing, or not valid for update (unknown counts as not), is skipped:
      `record_repair.mapped_column_not_updatable`;
    - a value its column's type does not take (the create path's `Fits`) is skipped:
      `record_repair.mapped_value_type_mismatch` (coordinator ruling, 2026-11-07);
    - a number is converted to the exact CLR type its column takes (review F1), since the SDK refuses any other:
      - Integer → `int` (whole and in range only);
      - BigInt → `long`;
      - Decimal → `decimal` (a `Money` gives its amount);
      - Double → `double`;
      - Money → `Money`.

      A number that does not convert, including a double no decimal can hold (review K4: the overflow is caught), is a
      type mismatch;
    - text longer than `MaxLength` is skipped: `record_repair.mapped_value_too_long`;
    - a number outside its column's `MinValue`/`MaxValue`, or with no range stated: `record_repair.mapped_value_out_of_range`;
    - a lookup to a table not in the column's `Targets`, or with no targets stated:
      `record_repair.mapped_lookup_table_not_accepted`;
    - an option (any value of a multi-select) not in the column's `OptionValues`, or with no choices stated:
      `record_repair.mapped_option_not_accepted`;
    - Money columns go to `ApplyFillOnly` as `moneyAttributes`, which closes #2124 on the Repair path.
- **Metadata projected (review F1).** `ColumnMetadata` also carries a number or Money column's range, a lookup's targets
  and a choice column's option values. All three come from the same `RetrieveEntityRequest` answer.
- **Test world (review F3).** `RepairWorld`'s update refuses a value whose CLR type is not the one its column's metadata
  type takes, as the SDK does. No existing test passed only because the world accepted anything: every Repair test
  passed against the stricter world before the review fixes, and the 16 new value tests failed against it.
- **#2188 guard.** Only the reason text of the existing per-member entry changed. See "Guard allow-list" below.

## CLAUDE.md §11

- **Existing.**
  - `MetadataService` (DataGrid DTO, 6 h Redis cache) has no `IsValidForUpdate`, `IsSecured` or `MaxLength`.
  - The create path's `TargetAttributesAsync` and `CallerMetadataReadMemo` read AS THE CALLER through
    `IDataverseUserClient`, and Repair has no caller.
  - `SecureRootInheritance.ColumnFieldSecurityAsync` reads one column's `IsSecured` through `DataverseWebApiClient`.
  - `SecurableEntityRegistry.FetchEntityMetadataAsync` is a `RetrieveMetadataChangesRequest` filtered to the
    `sprk_issecure` attribute, projecting logical names only (which tables can be secure, and which exist). It does not
    read column types or flags.
  - `UpdateRecordActionCore.ResolveEntityMetadataAsync` resolves `MetadataService` from a scope and returns its cached
    `EntityMetadataDto`. It has the same gaps as `MetadataService`, plus the 6 h cache.
- **Extension.** This extends the app-only reader Repair already holds (`_entities`) with one method and one projected
  record, beside `GetLookupNavigationAsync` / `LookupNavigationMetadata`. That means:
  - no new service, registration or constructor parameter;
  - `MetadataService` was not extended, because its DTO is the DataGrid API contract and its 6 h shared cache is not
    the brief caching UAC-r2 condition 1 allows for `IsSecured`.
- **Cost of doing nothing.** Repair copies field-secured source values into rows other people can read (#2188 on the
  system path). It also writes a decimal into a Money column (#2124), and one rule aimed at a missing or read-only
  column fails the row's whole update on every pass (016 K-1). 052's scheduled pass is hard-blocked on all three.

## Guard allow-list (UAC-r2 condition 3)

The brief says the #2188 guard exempts whole files only. That is out of date. Task 016 already added
`UnmaskedCallExemptions`, keyed `path|Type.Member` with a `Uses` count. It has must-fire, must-not-fire and stale
controls, plus mutants E1–E5 (task-016-notes "Condition 3: done").

The entry for `RecordCreationService.RepairMappingAsync` keeps `Uses = 1`, so a second unmasked call in that member, or
any unmasked call in another member, still fails. The smallest change that meets the condition was to keep the
mechanism and correct the reason, which no longer says "TODO task 033".

## Open points

- **052b must not ship without a per-pass metadata cache.** Repair reads both tables' metadata, a whole-table
  `RetrieveEntityRequest` each, for every row. The pass's cache keeps successful reads only (a fault is never kept), for
  one pass at most, which fits UAC-r2 condition 1's "read per pass, or cached briefly". The coordinator assigned it to
  052b.
- **What is still not checked.** The target filter covers what the review named:
  - existence and update validity;
  - type and exact CLR type;
  - text length;
  - number range;
  - lookup targets;
  - option values.

  Other refusals the SDK can give for a single value still fail the row's update and are reported per row as
  `record_repair.write_failed`. Examples: a date outside the platform's range, a lookup to a row that does not exist or
  is inactive, and a status that does not fit the row's state. These are not claimed to be handled.
- **#2124:** close it when this reaches master.
