# Task 064 notes - external document list: sprk_documenttype is a choice column

Owner check 3, item 1. Rigor FULL, tier sonnet/high. Branch base a07ddb890.

## Root cause (from the task's live evidence)

`DocumentRow.SprkDocumenttype` was `string?`. `sprk_documenttype` is a picklist (`sprk_document_sprk_documenttype`,
`Entities/sprk_Document/Entity.xml`), so Dataverse sends a number. One typed document threw `JsonException` inside
`GetCollectionPageAsync`, which was caught and turned into "page 1 failed" -> empty list flagged truncated.

## Change

`src/server/api/Sprk.Bff.Api/Infrastructure/ExternalAccess/ExternalDataService.cs`

- `DocumentRow.SprkDocumenttype` is now `int?` (raw choice value); new `SprkDocumenttypeLabel` reads
  `sprk_documenttype@OData.Community.Display.V1.FormattedValue`.
- `MapDocument` returns the label only when the value is set AND a non-blank label came back; otherwise `null`
  (never the number). The wire DTO `ExternalDocumentDto.sprk_documenttype` stays a string - the SPA is unchanged.
- `GetCollectionPageAsync` sends `CollectionPrefer` =
  `odata.maxpagesize=200,odata.include-annotations="OData.Community.Display.V1.FormattedValue"`. Every page, including
  a followed `nextLink`, carries it. Extra annotation keys are ignored by the typed rows and by the
  `Dictionary<string, JsonElement>` readers (they look keys up by name).
- A genuine Dataverse failure is unchanged (empty + `FirstPageFailed` -> truncated + logged).

Not changed: the three `Prefer: return=representation` creates (they set no document type, and `TodoRow`/`EventRow`
have no choice mapped as a string).

## Tests

New, `tests/unit/Sprk.Bff.Api.Tests/Infrastructure/ExternalAccess/ExternalDataServiceTests.cs` (5):

| Test | Proves |
|---|---|
| `DocumentList_ARowWithANumericType_IsReturnedWithTheFormattedLabel_AndTheListIsNotTruncated` | number + annotation -> "Email"; unset -> null; not truncated (failed before) |
| `DocumentList_ANumberWithNoAnnotation_IsNull_NeverTheNumber` | number, no annotation -> null, row kept, not truncated (failed before) |
| `EveryCollectionPage_AsksForServerPaging_AndTheFormattedValueAnnotations` | Prefer carries both preferences (failed before) |
| `DocumentList_ARowWithNoTypeProperty_ReturnsANullType` | control (passed before) |
| `DocumentList_ANon2xxPage_StillReturnsEmptyFlaggedTruncated` | control for the unchanged failure path (passed before) |

Before the change: 3 failed, 2 passed. After: 5 passed.

Updated (contract of an existing test, not a new behaviour): `tests/integration/regression/Issue963_ExternalDataPagingTests.cs`
asserted `Prefer == "odata.maxpagesize=200"` and its fake parsed the whole header as the page size. It now asserts
`Prefer == ExternalDataService.CollectionPrefer` (which starts with the page size) and the fake reads the page size from
the first preference. This file is compiled into the same `Sprk.Bff.Api.Tests` project.

## Audit of every private `*Row` class in ExternalDataService.cs

Source: `src/dataverse/solutions/SpaarkeMaster/Entities/<Entity>/Entity.xml` `<Type>`. Lookups arrive as
`_x_value` GUID strings. `contact` and `account` are platform tables: `Entities/Contact` and `Entities/Account` carry
only the customised columns, so those columns are checked against the standard Dataverse schema, not the solution.

| Row class | Property -> column | Dataverse type | Result |
|---|---|---|---|
| ProjectRow (sprk_project) | SprkProjectid -> sprk_projectid | primarykey (GUID string) | OK |
| | SprkName -> sprk_projectname | nvarchar | OK |
| | SprkReferencenumber -> sprk_projectnumber | nvarchar | OK |
| | SprkDescription -> sprk_projectdescription | ntext | OK |
| | SprkIssecure -> sprk_issecure | bit (`bool?`) | OK |
| | SprkStatus -> statecode | state (`int?`) | OK |
| | Createdon -> createdon | datetime (ISO string) | OK |
| | Modifiedon -> modifiedon | datetime (ISO string) | OK |
| DocumentRow (sprk_document) | SprkDocumentid -> sprk_documentid | primarykey | OK |
| | SprkName -> sprk_documentname | nvarchar | OK |
| | SprkDocumenttype -> sprk_documenttype | **picklist** | **fixed** (`string?` -> `int?` + label) |
| | SprkSummary -> sprk_filesummary | ntext | OK |
| | SprkProjectidValue -> _sprk_project_value | lookup | OK |
| | SprkRelatedprojectValue -> _sprk_relatedproject_value | lookup | OK |
| | SprkMatterValue -> _sprk_matter_value | lookup | OK |
| | SprkRelatedmatterValue -> _sprk_relatedmatter_value | lookup | OK |
| | SprkWorkassignmentValue -> _sprk_workassignment_value | lookup | OK |
| | SprkRelatedworkassignmentValue -> _sprk_relatedworkassignment_value | lookup | OK |
| | Createdon -> createdon | datetime | OK |
| | CreatedByPersonValue -> _sprk_createdbyperson_value | lookup | OK |
| | CreatedByContactValue -> _sprk_createdbycontact_value | lookup | OK |
| TodoRow (sprk_todo) | SprkTodoid -> sprk_todoid | primarykey | OK |
| | SprkName -> sprk_name | nvarchar | OK |
| | SprkNotes -> sprk_notes | ntext | OK |
| | SprkDuedate -> sprk_duedate | datetime, date-only (string) | OK |
| | SprkPriorityscore -> sprk_priorityscore | int (`int?`) | OK |
| | SprkEffortscore -> sprk_effortscore | int (`int?`) | OK |
| | SprkTodocolumn -> sprk_todocolumn | picklist, already `int?` | OK |
| | SprkTodopinned -> sprk_todopinned | bit (`bool?`) | OK |
| | Statecode -> statecode | state (`int?`) | OK |
| | Statuscode -> statuscode | status (`int?`) | OK |
| | Createdon -> createdon | datetime | OK |
| | SprkRegardingprojectValue / SprkRegardingmatterValue / SprkRegardingworkassignmentValue | lookups | OK |
| | SprkRegardingrecordid -> sprk_regardingrecordid | nvarchar | OK |
| | SprkRegardingrecordname -> sprk_regardingrecordname | nvarchar | OK |
| | SprkRegardingrecordurl -> sprk_regardingrecordurl | nvarchar (url) | OK |
| | CreatedByPersonValue / CreatedByContactValue | lookups | OK |
| EventRow (sprk_event) | SprkEventid -> sprk_eventid | primarykey | OK |
| | SprkName -> sprk_eventname | nvarchar | OK |
| | SprkDuedate -> sprk_duedate | datetime, date-only (string) | OK |
| | SprkStatus -> statuscode | status (`int?`) | OK |
| | Createdon -> createdon | datetime | OK |
| | SprkRegardingprojectValue / SprkRegardingmatterValue / SprkRegardingworkassignmentValue | lookups | OK |
| | CreatedByPersonValue / CreatedByContactValue | lookups | OK |
| ContactRow (contact) | Contactid -> contactid | primarykey | OK |
| | Fullname, Firstname, Lastname -> fullname, firstname, lastname | string | OK |
| | Emailaddress1 -> emailaddress1 | string | OK |
| | Telephone1 -> telephone1 | string | OK |
| | Jobtitle -> jobtitle | string | OK |
| | ParentcustomeridValue -> _parentcustomerid_value | customer lookup | OK |
| AccountRow (account) | Accountid -> accountid | primarykey | OK |
| | Name -> name | string | OK |
| | Websiteurl -> websiteurl | string | OK |
| | Telephone1 -> telephone1 | string | OK |
| | Address1City -> address1_city | string | OK |
| | Address1Country -> address1_country | string | OK |
| AccessLinkRow (sprk_externalrecordaccess) | ContactId -> _sprk_contact_value | lookup | OK |
| RecordTypeRefRow (sprk_recordtype_ref) | Id -> sprk_recordtype_refid | primarykey | OK |
| | DisplayName -> sprk_recorddisplayname | nvarchar | OK |

Result: one mismatch (`sprk_documenttype`), fixed. No other `string?` property maps a choice, boolean or number column.
Date-only and datetime columns arrive as JSON strings, so `string?` is correct for them.

The `Dictionary<string, JsonElement>` reads (grants, document scope, service-request state) take values by key and
check `ValueKind`, so they cannot hit this class of failure. The other `*Row` classes under
`Infrastructure/ExternalAccess` (`ExternalGrantRow`, `ContactStateRow`, `RootFlagRow`, ...) are in other files and not in
this task's audit scope; they are not touched.

## Gates

- BFF hygiene (CLAUDE.md section 10): no new package, endpoint, service or DI registration. One new `internal static`
  string (`CollectionPrefer`). Publish-size delta: no code or package surface of size added, not measured with a fresh
  master build (none run in this worktree).
- Contract: the wire DTO is unchanged; the SPA needs no change.
- Reaches dev with the next BFF deploy (owner-approved). No live commands run.
