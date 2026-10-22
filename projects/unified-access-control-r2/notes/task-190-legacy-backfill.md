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

## 4. Fix round 1 (head 6c034fe88d) and the container question

- **Review pass 1 fixes, all in:**
  - **`hasParent`:** an archive with a parent is never linked, so linking can only hide, never show, in every case.
  - **Dry-run proof:** `VerifyNoOtherCalls`; the reviewer's `BulkUpdateAsync` mutant is now killed.
  - **Child-count column:** pinned, with a projecting test store.
  - **Batch:** clamped to the 200-row list cap, so every run's lists are complete.
  - **I-19:** names the writer.
- **Container derivation (F4 → K2).**
  - Linking changes what `DeriveCoreAsync` derives (`RecordContainerResolver.DocumentPointer.cs:615-692`).
  - Under the interim rule, both halves only ADD admissions (`DocumentHangsOffAsync` :1155-1208; `IsArchivePathItem` :952-961). But the round-37 branch for BFF-placed files (:352-361) defers to the strict test, which can newly refuse.
  - The result is a refused download (fail closed), never exposure.
  - **Dev facts (read-only, 2026-10-21):** of the 29 matched archives, none touches a secure root on either the document or the communication side; 28 are unfiled and 1 is filed to a non-secure record. All 29 sit in the same container as 10 already-linked archives.
- **Gate procedure (binding for the Apply run, every environment):**
  1. Trigger `document-container-migration` report-only and save its lists.
  2. Trigger this job's Apply.
  3. Trigger `document-container-migration` report-only again.
  4. Any linked id that newly appears among the interim-refused documents is relocated by that job's own write mode (an owner go), or reported.

  Also: run this job's Apply BEFORE task 166's gate-24 migration pass and its `-Verify`. If it runs after, re-run them before the strict flip (gate 25).

## 5. Fix round 2 (head 2af6e97702); merged as c2e172f43f on 2026-10-22

- **Review pass 2 fixes, all in:**
  - **Duplicate archives per communication:** `communicationAlreadyArchived` is an attribute of a matched row, not a new reason. `CountAlreadyArchivedAsync` runs one `In` query before any write. A fault or `MoreRecords` sets `AlreadyArchivedCheckFailed`, and the run reports as partial.
  - **Remarks:** the false claims about durable run history and a standing `/enable` are removed (the scheduler store is in memory).
  - **Configuration:** tests pin the Apply key and the batch clamp (`batch.configured`, `effective`, `clamped` in the report).
- **Dev facts (read-only, 2026-10-21):**
  - none of the 29 matched communications already has an archive;
  - 6 communications have more than one archive, which is the #2225 condition.
- **Restrict:** `sprk_document_RelatedCommunication_sprk_communication` is Restrict on delete (#2186). A linked communication therefore cannot be deleted while the archive exists. That is intended: deleting it would silently drop the D7 leg.
- **Filed from this task:** #2222, #2225, #2226, #2227. The integration run also surfaced #2229 (pre-existing on master).
- **Checks at 2af6e97702:**
  - job suite 37/37; targeted 78/78; ArchTests 1337/1337; BFF build 0 errors, 0 warnings;
  - publish +9,940 B;
  - `Sprk.Bff.Api.IntegrationTests` 158 passed, 8 skipped, 0 failed;
  - `Spe.Integration.Tests`: 9 failed (the same 9 fail on master, #2229), 344 passed, 23 skipped.
- **CI:** Tier 1 `Arch Tests (full suite, blocking)` was cancelled by its 5-minute job timeout, not by a test failure. The same timeout cancelled master's runs at 18:59Z and 00:07Z. Evidence is on #2224; the job is re-run.
- **Apply run (owner go):** follow the §4 gate procedure. Save the run's `ResultJson` to this note straight after Apply, because the in-memory store loses it on the next restart.

## 6. Deploy and server gate (2026-10-22): PASS

- **Deploy:** batched with #2223 and fm-016, as master `a7b956fb55` (owner go in R3's session). Live `1.0.0+a7b956fb55` from 02:18:52Z; the previous live build f1b3dadcf5 is its ancestor. No app setting changed. Rollback snapshot: R3 `deploy/rollback/wwwroot-20261022T021635Z`.
- **Registration:** `email-archive-communication-link-sweep` is enabled=false, cron `45 3 * * *`, 0 runs (status before the trigger).
- **Step 1, `document-container-migration` report-only** (`DocumentContainerMigration:WritesEnabled` not set): triggered 02:20:52Z, Succeeded 02:23:56Z.
  - examined 100, all `InPlace`; `wouldNewlyRefuse` 0, `servedOnlyAfterFlip` 5, `relocatedButRefused` 0.
  - `passComplete: false` (one batch). The full pass is still owed before Apply.
- **Sweep dry run:** triggered 02:21:03Z, Succeeded 02:21:07Z. `mode: dry-run`, `apply: false`.
  - examined 120: `wouldLink` 29, `linked` **0** (no write), `noKey` 68, `exchangeItemId` 23, `hasParent` 0, `noCommunication` 0, `ambiguous` 0, `changedSinceRead` 0, `failed` 0;
  - `childrenCovered` 39, `childrenOfUnmatched` 20;
  - `communicationAlreadyArchived` total 0, `alreadyArchivedCheckFailed` false; `passComplete` true; batch 200/200, not clamped.
  - **This equals the §1 census exactly.**
- **Saved ResultJson** (contains document ids, so it is not in the repo): `C:/Users/RALPHS~1/AppData/Local/Temp/uac190gate/*.status.json`.
- **Exception reading (R3, 02:17:30Z–02:48:36Z): clean.** No new exception class; the only group was the known Graph "Invalid hostname" ×6 (ISS-006). 0 stager refusals, 0 narrowing exceptions, 0 409s. secure-child-reconciliation: 10 of 10 runs Succeeded, accessPermission failed 0.
- **Closed on merge + deploy + server gate (round 90).**
- **Apply stays an owner item:** setting `EmailArchiveLinkSweep:Apply=true` restarts the BFF.
  - Before it: the migration report-only to `passComplete`, with its lists saved.
  - After Apply: migration report-only again (§4 steps 3–4), and save the run's ResultJson here.
- **Unmatched for the owner's Restricted call:** 68 with no key + 23 with an Exchange id (+20 children under them). Their ids are in the saved ResultJson.
- **Follow-up from round 122:** a linked `.eml` whose own Restricted was set because the email could not be identified at save follows its communication. Design: `notes/task-190-round122-linked-eml-access.md`.
