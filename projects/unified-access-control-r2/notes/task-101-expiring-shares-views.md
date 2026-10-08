# Task 101 — "External shares by expiration" views (FR-33)

> **Status (2026-10-07)**: definitions done and checked live, read-only. **Not applied.** Per the binding directive of
> 2026-09-04 (FAILURE-MODES AP-13), the main session runs the script after merge (§5). The step-2 live verification
> (POML criterion 4) waits for that run.
> **Script**: [`scripts/Deploy-ExternalShareExpiryViews.ps1`](../../../scripts/Deploy-ExternalShareExpiryViews.ps1).
> This file shows the FetchXML the script builds. **If the two ever differ, the script is right.**
> **Design**: [`decisions/external-grant-expiry-mandatory.md`](decisions/external-grant-expiry-mandatory.md) §10.3.

## 1. What the views answer

The task 100 reminders answer whether one particular grant is about to lapse. These two views answer what is about to
lapse across all grants:

| # | View | Rows | Sort |
|---|---|---|---|
| 1 | **Active External Shares by Expiration** | `statecode = 0` | `sprk_expiresdate` ascending |
| 2 | **External Shares Expiring in 30 Days** | `statecode = 0` and expiry **on or before today + 30**, using a relative filter | `sprk_expiresdate` ascending |

Both are public (`querytype 0`) system views on `sprk_externalrecordaccess`, created in **SpaarkeCore**, the unmanaged
solution that holds the table. Neither is the default view: the existing default, "Active External Record Accesses",
is unchanged. No app needs a change, because neither app that includes the table (`sprk_MatterManagement`,
`sprk_SpaarkePlatform`) limits the table's views (zero `savedquery` app components). The table is already on the Matter
Management site map (`subarea_bb0cb719`), so the views show up in its view selector.

## 2. Logical names, checked against live metadata (spaarkedev1, 2026-10-07)

Source: `EntityDefinitions(LogicalName='sprk_externalrecordaccess')/Attributes`, its `LookupAttributeMetadata`
targets, and `DateTimeAttributeMetadata`. **Every name below exists live.** The script checks them again on every run
and stops before writing anything if one is missing.

| Column (display order) | Logical name | Type | Target / note |
|---|---|---|---|
| Project | `sprk_project` | Lookup | `sprk_project` |
| Matter | `sprk_matter` | Lookup | `sprk_matter` |
| Work Assignment | `sprk_workassignment` | Lookup | `sprk_workassignment` |
| Contact | `sprk_contact` | Lookup | `contact`. Empty on an organization grant |
| Organization | `sprk_organization` | Lookup | `sprk_organization`, a custom table, **not** `account` |
| Access Level | `sprk_accesslevel` | Picklist | 100000000 View Only · 100000001 Collaborate · 100000002 Full Access |
| Expires | `sprk_expiresdate` | DateTime | **Format `DateOnly`, behaviour `TimeZoneIndependent`** |
| Granted By | `sprk_grantedby` | Lookup | `systemuser` |
| Created By | `createdby` | Lookup | `systemuser` |
| (filter) | `statecode` | State | 0 = Active |

Object type code: **10911**. The script reads it from metadata, so it is right in every environment. Primary name:
`sprk_name`. Primary id: `sprk_externalrecordaccessid`.

**How "the shared record" became three columns.** A grant row keeps its root in one of several typed lookups, and a
view column cannot combine them, so each root gets its own column. On every live row exactly one of them is filled.
Three decisions follow from the live checks:
- **`sprk_invoice` is left out.** The lookup exists, but the evaluator never reads invoice grants
  (`ExternalParticipationService.cs:1373`: "sprk_invoice grants are intentionally NOT read"), and no grant path writes
  one (`ExternalGrantRoot` binds project, matter and work assignment only). On dev, 0 of 92 rows use it. An invoice
  column would always be empty, and showing such a row as a "share" would be wrong.
- **`sprk_grantedby` is added next to `createdby`.** The owner asked for "created by". On dev, **52 of 58** active
  rows were created by the BFF's service identity (`# mi-bff-api-dev` / `SDAP-BFF-SPE-API`), because grant writes are
  app-only. So Created By alone almost never names a person. Granted By names the internal user who granted the share
  wherever the grant path recorded one. Task 100's reminder routing uses the same column first.
- **`sprk_name` is left out.** It is empty on **every** row (92 of 92), because no grant path writes it (§8 D-1). The
  existing views show it as a blank column.

## 3. The relative window, measured live (not assumed)

FetchXML has no relative "on or before". Each relative operator's boundaries were measured on spaarkedev1 with
read-only aggregate queries on 2026-10-07 (the viewing user's today was 10-07, while UTC was already 10-08):

| Operator | Measured meaning on a Date Only column | Evidence |
|---|---|---|
| `next-x-days N` | today **<** date ≤ today + N. **Today is excluded** | Expiry 2026-12-10 (today + 64): N=63 → 0, N=64 → 20. Granted date today: `next-x-days 1` → 0, while `today` → 1 |
| `last-x-days N` | today − N ≤ date ≤ today | 2026-09-22 (today − 15): N=14 → 0, N=15 → 3. Today's row is included |
| `olderthan-x-days N` | date < today − N | 2026-09-22: N=14 → 3, N=15 → 0. Yesterday (10-06) is excluded at N=1. N=0 is refused ("should be greater than zero") |

So **expiry ≤ today + 30** = `olderthan-x-days 1` ∪ `last-x-days 1` ∪ `next-x-days 30`, with no gap. Checked against
an independent absolute `on-or-before` count at N = 30 / 63 / 64 / 84 / 85 / 89: **0/0, 0/0, 20/20, 23/23, 26/26,
56/56**. The past side (olderthan 1 ∪ last 1) matched all 92 dated rows across 2026-03-17 … today.

A filter using only `next-x-days 30` would have **dropped every share expiring today**, the most urgent ones, and
every share that is past its date but still Active. Those are exactly the rows this view is for.

Undated rows are matched by none of the three operators, so they never appear in View 2. In View 1 they sort **first**
(Dataverse sorts NULL first ascending; verified on `sprk_granteddate`). After task 107, an Active row with no date or
a past date **confers no access** (`ExternalParticipationService.ConfersAccessOn`: `expiresDate is not null &&
expiresDate >= today`). It stays visible at the top of View 1, where an admin can renew it (task 098) or end it. Live
on dev today: **0 undated Active rows** (task 097's backfill holds) and **0 past-dated Active rows**.

## 4. The definitions

View 1 — **Active External Shares by Expiration**
```xml
<fetch version="1.0" output-format="xml-platform" mapping="logical" distinct="false">
  <entity name="sprk_externalrecordaccess">
    <attribute name="sprk_externalrecordaccessid" />
    <attribute name="sprk_project" />
    <attribute name="sprk_matter" />
    <attribute name="sprk_workassignment" />
    <attribute name="sprk_contact" />
    <attribute name="sprk_organization" />
    <attribute name="sprk_accesslevel" />
    <attribute name="sprk_expiresdate" />
    <attribute name="sprk_grantedby" />
    <attribute name="createdby" />
    <order attribute="sprk_expiresdate" descending="false" />
    <filter type="and">
      <condition attribute="statecode" operator="eq" value="0" />
    </filter>
  </entity>
</fetch>
```

View 2 — **External Shares Expiring in 30 Days**: the same, except for the filter:
```xml
    <filter type="and">
      <condition attribute="statecode" operator="eq" value="0" />
      <filter type="or">
        <condition attribute="sprk_expiresdate" operator="olderthan-x-days" value="1" />
        <condition attribute="sprk_expiresdate" operator="last-x-days" value="1" />
        <condition attribute="sprk_expiresdate" operator="next-x-days" value="30" />
      </filter>
    </filter>
```

Layout (both views):
```xml
<grid name="resultset" object="10911" jump="sprk_name" select="1" icon="1" preview="1">
  <row name="result" id="sprk_externalrecordaccessid">
    <cell name="sprk_project" width="160" />
    <cell name="sprk_matter" width="160" />
    <cell name="sprk_workassignment" width="160" />
    <cell name="sprk_contact" width="170" />
    <cell name="sprk_organization" width="170" />
    <cell name="sprk_accesslevel" width="110" />
    <cell name="sprk_expiresdate" width="110" />
    <cell name="sprk_grantedby" width="150" />
    <cell name="createdby" width="150" />
  </row>
</grid>
```

## 5. Applying them (operator, after merge) — exact run order

```powershell
pwsh -File scripts/Deploy-ExternalShareExpiryViews.ps1            # 1. dry run: checks names, runs both queries live, prints the plan
pwsh -File scripts/Deploy-ExternalShareExpiryViews.ps1 -Apply     # 2. creates both views in SpaarkeCore, publishes, then runs -Verify
pwsh -File scripts/Deploy-ExternalShareExpiryViews.ps1 -Verify    # 3. read-only; exit 0 = both views exist and return the right rows
```

The script is idempotent:
- A view that already matches is left alone.
- A same-named view whose query or columns differ is rewritten, and its old definition is saved to `scripts/logs/`
  first.
- Two views with the same name make it refuse.
- It **never** touches the default view, any other view, a role or the schema.

Its checks are semantic: filter conditions and their grouping, sort, attributes and cells. A view built by hand
(§6) is therefore verified the same way.

What `-Verify` checks, for each view:
1. It exists exactly once.
2. Its saved definition means the same thing as the definition above.
3. Its **saved query**, run through the Web API `?savedQuery=`, returns:
   - only Active rows;
   - rows in expiry order, undated first;
   - for View 1, exactly the Active row count;
   - for View 2, nothing undated or later than today + 30, and every Active row dated on or before today + 29.

   Both window checks allow one day either side, because the relative operators use the viewing user's time zone.

### Run so far (read-only, 2026-10-07, spaarkedev1)
```
Metadata    : 9 columns + statecode present; sprk_expiresdate is DateOnly; object type code 10911
Active rows : 57 (undated: 0)
[Active External Shares by Expiration]  PLAN create (query runs: 57 rows, all checks pass)
[External Shares Expiring in 30 Days]   PLAN create (query runs: 0 rows, all checks pass)
DRY RUN: 2 view(s) to write.                                     -> exit 0
-Verify: GAP view does not exist (x2), VERIFY FAIL               -> exit 1 (expected before -Apply)
```
View 2 returns 0 rows on dev today because the soonest expiry is 2026-12-10, which is today + 64. §3's wider-window
runs show the same filter returning the right rows.

## 6. Maker-portal fallback (only if the script cannot be used)

1. make.powerapps.com → the environment → **Solutions → SpaarkeCore → Tables → External Record Access → Views →
   + New view**. Name: `Active External Shares by Expiration`.
2. Columns, in this order: Project, Matter, Work Assignment, Contact, Organization, Access Level, Expires Date, Granted
   By, Created By. Widths as in §4. Remove the Name column; it is empty on every row (§2).
3. Sort by **Expires Date, ascending**. Filter: **Status equals Active**. Save and publish.
4. **Save as** `External Shares Expiring in 30 Days`, then add a **group of type OR** on Expires Date containing three
   rows: *Older than X days* = 1, *Last X days* = 1, *Next X days* = 30. Do **not** use "Next X days" alone; it
   excludes today (§3). Save and publish.
5. Run `-Verify` (step 3 of §5). It checks a hand-built view the same way.

## 7. Known limits (K-class, one line each)

- **K2 — time zone at the day boundary.** The relative operators use the *viewing user's* "today"; the access
  evaluator uses UTC. For a few hours a day the two dates differ by one. A row expiring on that boundary day can
  appear one day early or late in View 2. These views are informational only: no access decision reads them.
- **Scope of "estate-wide" = the reader's privilege.** Live: Spaarke Core User and Spaarke Basic User hold
  `prvReadsprk_externalrecordaccess` at **Parent:Child BU** depth in the root BU "Spaarke". System Administrator,
  System Customizer, Service Reader and Service Writer hold it at **Organization** depth. So an internal user in the
  root BU sees every share, and a user in a child BU sees only that BU's subtree. Nothing in this task changes that.

## 8. Defects found

| # | What | Where | Disposition |
|---|---|---|---|
| D-1 | **`sprk_name` is never written** on a grant row: 92 of 92 rows on dev are blank. Every existing view of the table ("Active External Record Accesses", the default; "All External Record Access") shows an empty Name column, and that column is the grid's open-record link. Failure: a user opening the default grid sees a blank first column, with no record name to click. | `src/server/api/Sprk.Bff.Api/Api/ExternalAccess/GrantExternalAccessEndpoint.cs:1106-1122` (`BuildGrantPayload` has no `sprk_name`). The materializer creates through the same `CreateGrantAsync` (`Services/ExternalAccess/AssignedAccessMaterializer.cs:1489`) | **Filed as [#1394](https://github.com/spaarke-dev/spaarke/issues/1394) and reported, not fixed**: out of scope (BFF code under `Api/ExternalAccess`, which this task must not touch while task 154 runs). The new views leave the column out. |
| D-2 | **`views-schema.md` documented four views that do not exist live.** "Active Participants" (shown as the default), "By Project", "By Contact" and "Expiring Access" are absent. The live views are "Active External Record Accesses" (default), "All External Record Access", "Inactive External Record Accesses" and the system lookup / quick find / associated / advanced find views. This is the same class as the nine docs-versus-metadata mismatches recorded so far, and the tenth found. | `src/solutions/SpaarkeCore/entities/sprk_externalrecordaccess/views-schema.md` | **Fixed in this task**: the file now records the live views, marks the four as never applied, and points to these two. |

Data observation, **not triaged as a defect**: **46 of 58** Active rows have no Granted By. Most were written on
2026-10-06 by the BFF identity. The grant core leaves `sprk_grantedby` out when it cannot resolve a grantor
(`GrantExternalAccessEndpoint.cs:1135-1141`, "Omitted when unresolved"), and the materializer passes the run's
`GrantorOid` (`AssignedAccessMaterializer.cs:1489-1491`). A background run, such as reconciliation, has no grantor by
design. This task did not establish which path wrote those rows. If a user-initiated path is meant to record a granter
and does not, that belongs to the grant-core owner. It is noted here so it is not lost.

## 9. Open owner questions

None blocking. Two choices were made inside the owner's spec. Say if either is wrong:
1. **View 2 includes Active shares that are already past their date** ("on or before today + 30", taken literally).
   They confer nothing, but they can be renewed, so they are the most actionable rows.
2. **Granted By is shown next to Created By** (§2). Created By names the service identity on 52 of 58 Active rows.
