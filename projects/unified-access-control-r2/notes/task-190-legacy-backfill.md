# Task 190: link legacy email documents to their communication (D7 T7b, #2126)

Worktree `C:/wt190`, branch `uac/190-legacy-backfill`, from master `f1b3dadcf5`.

## 1. Dev census, read-only (2026-10-21, before any code)

The census was run with the operator's own token and printed counts only. The script lived in a private temp folder (`uac190census.py`) and is not in the repo.

| Set | Count |
|---|---|
| `.eml` archive documents (`sprk_isemailarchive = true`) | 173 |
| — already linked (`sprk_relatedcommunication` set) | 53 |
| — unlinked | 120 |
| Unlinked, key 1 = **exactly one** communication with the same `sprk_internetmessageid` | **29** |
| Unlinked, no `sprk_emailmessageid` at all | 68 |
| Unlinked, holds an Exchange item id (`AAMk…`/`AQMk…`, saved before task 121): unmatchable | 23 |
| Unlinked, RFC key with no communication, or with more than one | 0 |
| Unlinked archives' own `sprk_accesspermission` | 119 blank, 1 Standard |
| Child documents (`sprk_parentdocument` set) | 78 |
| — unlinked children whose parent `.eml` is already linked | 18 |
| — unlinked children whose parent `.eml` matches key 1 | 39 |
| — unlinked children whose parent is unmatched | 20 |
| `sprk_processingjob` rows with `Attachment.ParentEmailId` (key 3) | 3, **0 in RFC form** |

## 2. Decisions (main session, 2026-10-21)

- **Key 2 (attachment children) is not built.** Task 188's predicate (`ExternalDocumentNarrowing`, `MaxHops = 3`) walks `sprk_parentdocument`, and the first communication link it finds decides. Linking the parent `.eml` therefore covers its children:
  - the 18 children whose parent is already linked are covered today;
  - the 39 whose parent matches key 1 become covered when the parent is linked.

  Writing the same link on each child adds writes and no visibility change. The POML criterion "an attachment child links only through a parent `.eml` that matched by key 1" holds as "a child is covered only through its key-1 parent".
- **Key 3 is not built.** Of the 3 processing jobs with a parent email id, none is in RFC form, and a mailbox item id cannot be joined to a communication (POML: "if zero, record that and do not build a matcher").
- **Shape.** One scheduled sweep job, following `CommunicationThreadFilingSweepJob` (word-add-in-r1 task 129) and `DocumentContainerMigrationJob` (task 166):
  - registered DISABLED;
  - triggered with `POST /api/admin/jobs/{jobId}/trigger` (SystemAdmin), so no new endpoint;
  - dry run unless an Apply setting is true (an owner decision, because it restarts the BFF);
  - batched, with a cursor;
  - the report is the run's `ResultJson`.

  It is also a standing safety net: it heals any archive whose best-effort link failed later.
- **Unmatched rows** (68 with no key, 23 with an Exchange id, 20 children under them): listed by id and count for the owner, who decides whether they become Restricted. The job never sets Restricted.

## 3. Implementation (PR #2221, draft, head d811f84168)

- **Job:** `EmailArchiveCommunicationLinkSweepJob`.
  - Registered disabled in `CommunicationModule`; run through `POST /api/admin/jobs/{jobId}/trigger`.
  - Dry run unless `EmailArchiveLinkSweep:Apply=true`.
  - Writes only `sprk_relatedcommunication`, null → the one exact match, through `UpdateIfRowVersionMatchesAsync`.
  - Reasons: `noKey`, `exchangeItemId`, `matched`, `ambiguous`, `noCommunication`, `changedSinceRead`, `failed`.
- **Checks:**
  - 27 new unit tests; with the reference job's suite, 51/51.
  - ArchTests 1337/1337.
  - BFF and `Spaarke.sln` builds: 0 errors, 0 warnings.
  - Publish +9,940 B against a fresh master build; no vulnerable packages.
- **Mutants**, each killed and restored:

  | Mutant | Tests that failed |
  |---|---|
  | Ambiguous branch removed | 4 |
  | Dry run writes | 5 |
  | Case-insensitive accept | 2 |
  | Stale row version passed to the write | 4 |
- **Guard.** On master, `EveryGenericUpdateWriter_AsksTheAppOnlyRefusal` does not scan `UpdateIfRowVersionMatchesAsync`, so an entry would be reported stale. A comment records the member entry `EmailArchiveCommunicationLinkSweepJob::LinkAsync` and its reason. Field-mapping's task/fm-016 widens the scan; whoever merges second turns the comment into the entry.
- **Read paths opened by the link:** none through Dataverse row security; the relationship is NoCascade on share, assign and reparent.
  - Two app-only lookups by communication reveal only that an archive exists: chat ingest (404 vs 403) and the archive route's `ArchiveDocumentId`. This was already true before 190, and it is filed as **#2222**.
  - D7 can only hide a newly linked archive, never show one.
- **Known limit:** the children counts in the report are direct children only. The D7 walk goes up to 3 hops.
