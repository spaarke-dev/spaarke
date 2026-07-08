/**
 * sprk_memo entity type — schema verified via Dataverse MCP (task 001, 2026-07-02).
 * See notes/sprk-memo-schema.md for the schema table.
 *
 * ADR-024 dual-field pattern:
 *   - 6 entity-specific regarding lookups (Matter, Project, Event, Invoice, Budget, WorkAssignment)
 *   - 5 resolver fields (id, name, number, url, recordtype ref)
 */
export interface Memo {
  sprk_memoid: string;                              // PK GUID
  sprk_name: string;                                // NVARCHAR(850) NOT NULL — title (required at create)
  sprk_memobody: string | null;                     // MULTILINE TEXT — body
  sprk_regardingrecordid: string | null;            // resolver: parent GUID (denormalized)
  sprk_regardingrecordname?: string | null;         // resolver: parent display name
  sprk_regardingrecordnumber?: string | null;       // resolver: parent number (5th field)
  sprk_regardingrecordurl?: string | null;          // resolver: parent URL
  sprk_regardingrecordtype?: {                      // resolver: lookup to sprk_recordtype_ref
    id: string;
    name: string;
  } | null;

  // Entity-specific regarding lookups (populated via PolymorphicResolverService)
  _sprk_regardingmatter_value?: string | null;
  _sprk_regardingproject_value?: string | null;
  _sprk_regardingevent_value?: string | null;
  _sprk_regardinginvoice_value?: string | null;
  _sprk_regardingbudget_value?: string | null;
  _sprk_regardingworkassignment_value?: string | null;

  // System fields
  createdby: {
    id: string;
    name: string;
  };
  createdon: string;                                // ISO datetime
  modifiedon: string;                               // ISO datetime
}

/**
 * Shape returned by Xrm.WebApi.retrieveMultipleRecords — with OData formatted-value annotations.
 * Consumers unwrap this into `Memo` shape.
 */
export interface MemoRaw {
  sprk_memoid: string;
  sprk_name: string;
  sprk_memobody: string | null;
  sprk_regardingrecordid: string | null;
  _createdby_value: string;
  "_createdby_value@OData.Community.Display.V1.FormattedValue"?: string;
  createdon: string;
  modifiedon: string;
  [key: string]: unknown;                           // Xrm returns lots of side-channel props
}
