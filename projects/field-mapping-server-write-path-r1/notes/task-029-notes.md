# Task 029 notes — Document Upload and Summarize Files on the server write path

## Scope (owner decision, 2026-11-25: option B)

The POML covered the Document Upload wizard. The Summarize Files wizard was a separate create path (`POST /api/v1/documents {name, containerId}`, with the container read client-side from the user's business unit). The escalation trigger fired, and the owner chose to move Summarize onto the BFF child-records route as well.

## What changed

- **Document Upload**:
  - The `sprk_document` create sends no `sprk_searchindexname` and no `sprk_AI_Search_Index` bind.
  - The `rag/index-file` body has no `searchIndexName`; the server routes by content type (FR-40, task 041).
  - The client search-index resolver is deleted.
- **Summarize Files**: creates `POST /api/v1/child-records/sprk_document {sprk_documentname}`. The client no longer reads a business-unit container.

## Deviation: Upload always names its mapping source

POML step 3 said to pass `mappingSource` only when the filing parent differs from the intended source. The wizard passes the filing parent every time, because the server infers a source only for the tables in `ChildRecordEndpoints.InferMappingSourceTables` (`sprk_matter`, `sprk_project`). `sprk_document` is not in that set, so without the query a document would map nothing.

- The server comment above that set now says that Document Upload and the Event wizard name their source.
- A later task that adds `sprk_document` to the set, or removes the query parameter, must keep one or the other.
- **Server effect** of the source the wizard names (`RecordCreationService.Mapping.cs`):
  - a table with no profile maps nothing;
  - an unreadable source gives a warning, never a refusal;
  - only a stripped secure-source value that is a filing lookup is refused (existing behaviour).
- The server comment edit is comment-only. It adds no endpoint, service, registration or package, so no publish-size measurement is needed (`.claude/rules/bff-hygiene.md` item 4 applies to additions).

## Review (Step 9.5): one adversarial pass, Opus

| # | Class | Finding | Outcome |
|---|---|---|---|
| 1 | F1, introduced | The upload decorator returned only the id, so the route's mapping warnings were dropped | Fixed: warnings are carried through to the per-file result and shown |
| 2 | F1, pre-existing (since UAC-r2 task 166) | `withBffChildCreates` had no `deleteRecord`, so a failed file attach left a fileless row | Fixed: the decorator deletes through `DELETE /api/v1/documents/{id}` |
| 3 | F1, pre-existing | Summarize creates documents with no file, and the analysis then reads the "No file content" placeholder | Filed as DI-042 / #3156 (the upload pipeline is outside FR-16's scope) |
| 4 | F2, introduced | The always-name-the-source decision was not recorded, and the server comment misled | Fixed: this note plus the comment |
| K1 | Known limit | NFR-05 timing of ≤10 parallel creates on the child-records route | Observe in the live check |
| K2 | Known limit | The old route set `statuscode=1`; the new route takes the table default | Check in the live check |
| K3 | Known limit | A legacy matter with an empty secure flag counts as flagged, so its mapped values are stripped with a warning | Existing server behaviour |

## Fix round (5520e1b7d1, merged 88ff6ea39d)

- **F1, warnings:**
  - `withBffChildCreates` calls `createRecordViaBffWithWarnings` and returns `warnings` only when there are some.
  - `DocumentRecordService` copies them into its result, and the orchestrator carries them on the per-file result.
  - The success screen shows each one as a non-blocking `MessageBar` warning ("<file>: <warning>"), reusing `WizardSuccessScreen`'s existing warnings list.
- **F2, cleanup:**
  - The decorator's `deleteRecord` removes a `sprk_document` row through `DELETE /api/v1/documents/{id}`. That route deletes the row only, never a file, and refuses a row other documents name as their parent.
  - Other tables go to the inner client.
  - A non-2xx response throws, and `createAndAttach` logs it.
- **Tests:**
  - The real decorator: a refused attach sends exactly one DELETE, and an attached file sends none.
  - Warnings, with negative cases (no key, an empty list, a failed create), through the decorator, the service, the orchestrator and the success screen.
  - With the old source restored, 7 tests fail in the shared library and 3 in the wizard.
  - Totals: shared library 65/65 (document-upload + SummarizeFilesWizard), DocumentUploadWizard 27/27.
  - `tsc` is clean, both builds pass, and client-lint `ui-components` passes with no suppression change.
- Summarize sends no mapping source (its documents are parentless), so its create returns no mapping warnings. It needs no warnings UI.

## Gates

- **Publish delta:** master 9ce92b3104 vs branch 987387b264. Branch 43,607,063 bytes vs master 43,617,912 bytes = −10,849 (the branch is slightly behind master). 194 = 194 files, `Compress-Archive`, 41.59 MB total.
  - The only BFF change is a comment.
  - `dotnet list package --vulnerable --include-transitive`: none.
- **code-review / adr-check:** recorded inline for the client diff.
  - ADR-028: every BFF call uses `authenticatedFetch`; no token props.
  - ADR-012: changes stay inside the existing shared document-upload service; no new package.
  - ADR-021: no new UI; the existing Fluent v9 `MessageBar` list is reused.
  - ADR-002: no plugin.
  - ADR-008: no server authorization change.
- **Deploys needed:**
  - `sprk_documentuploadwizard`;
  - `sprk_summarizefileswizard`;
  - `sprk_spaarkeai` — its `vite.config.ts` bundles `DocumentUploadWizard/src`, and the Summarize dialog comes from the shared library through `inAppWizardRenderers.tsx`.

## Deploy and live check (owner go 2026-11-25: "Yes, deploy after merge")

- **Rollback copies** of dev before the deploy, in `C:\tmp\fm029-rollback`:
  - `sprk_documentuploadwizard`: 2026-11-23 21:35Z, 1,542,762 bytes, SHA BF9A8613…BC68FA03.
  - `sprk_summarizefileswizard`: 2026-11-23 21:35Z, 1,512,677 bytes, SHA 7D6598EA…6EEEB977.
  - `sprk_spaarkeai`: 2026-11-25 17:35Z, 6,466,220 bytes, SHA 90503EEE…745D6A90.
- **Planned temporary profile** "FMSWP-029 live check: Matter to Document (temporary)":
  - source type Matter (e8547bb4…) → target type Document (b83dbfc1…);
  - rule `sprk_matterdescription` → `sprk_documentdescription` (Memo, 6/100000006);
  - rule `sprk_assignedtointernal` → `sprk_assignedtointernal` (contact lookup, 1/100000001);
  - deactivate it after the check.
- **Test matter:** MAT-000113 `68190578-c655-458f-8518-34eff189c39c`. It is Standard, has a description, and its Internal assignee is set.

### Results (2026-11-25, dev)

- **Merged:** PR #3158, 19:27Z, as 520ca7917d.
  - Every required check passed.
  - The advisory `api-office` leg was cancelled at its 40-minute limit during pass 1, not failed by a test (known).
- **Built and deployed** from master 520ca7917d in a temporary worktree (`C:\tmp\w029`). Master had moved on since the PR head, so the pages were rebuilt there. `sprk_wizard_commands` is unchanged on master since its last deploy.

| Page | Deployed (UTC) | Bytes | SHA256 (read-back = build) | Rollback |
|---|---|---|---|---|
| `sprk_documentuploadwizard` | 21:09:49 | 1,545,188 | CC515070…7576AEAF | BF9A8613…BC68FA03 |
| `sprk_summarizefileswizard` | 21:16:25 | 1,515,721 | 229F1B4B…C2A45F2A | 7D6598EA…6EEEB977 |
| `sprk_spaarkeai` | 19:54:59 (Deploy SpaarkeAi workflow from 520ca7917d) | 6,485,985 | contains the 029 delete message and `mappingSourceEntity`; no business-unit container read | 90503EEE…745D6A90 |

- **Temporary profile** "FMSWP-029 live check: Matter to Document (temporary)" `d349f3a0-14e9-f111-84ea-00224829aef9`:
  - rules `6a8107a0…` (`sprk_matterdescription` → `sprk_documentdescription`) and `8bf2cba1…` (`sprk_assignedtointernal` → `sprk_assignedtointernal`);
  - created 21:10Z, deactivated 21:20Z.

**Document Upload: PASS.** Run on MAT-000113 from the matter form's Documents "+" button. Document `1400088a-a633-46d3-beb4-10523499a65b`:
- `sprk_documentdescription` = the matter's description, and `sprk_assignedtointernal` = the matter's Internal assignee. Both are mapped; the wizard sends neither key. `sprk_document` is not in the inferred-source set, so mapping proves the wizard named its source.
- Owner = Spaarke Business Unit 1 (the matter's team, record-first).
- File attached (`sprk_graphitemid` set), status Draft.
- `sprk_searchindexname` = `spaarke-files-index` (the server default, task 041); no index bind.
- Success screen: "1 document created successfully", with no warnings. That is correct: the source was readable and nothing was dropped.

**Summarize Files: PASS.** From MAT-000113, Work on Analysis → Document Summary. Document `09806312-0efb-422e-a9bf-4561945d3557`:
- name only; no search index, no container, not filed under anything;
- owner = the caller's business-unit team ("Spaarke": the signed-in user's business unit);
- the Playbook Library opened. It was closed without running, because of DI-042: the row has no file.

**Negative criterion.** The payload holds only keys the user entered (closed-set tests, plus the bundle checks above). "A user value wins" could not be exercised in this wizard: it has no field for the mapped columns. Fill-only behaviour is covered by the server tests and the 021–028 live checks.

**Known limits:**
- K1: not measured. One file took about 1 s to create.
- K2: `statuscode` Draft on both rows, the same as the old route.

**Found:** DI-043 / #3176. The wizard silently drops TXT/PPTX/MSG/EML files it says it accepts. Pre-existing.
- The first live attempt used a TXT file, which hit this; the check was rerun with a PDF.
- The matter form showed an "Access status unavailable…" banner on MAT-000113 throughout. It was not investigated here, and the uploads were unaffected.
