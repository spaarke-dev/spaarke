# Task 213 notes - Arrival notifications read only the columns the thread has (#2775, DI-151)

Branch `work/ontology-213-arrival-thread-columns` from origin/master `b005279c4d`. Rigor FULL (BFF `.cs`), sonnet @ high, directional steps.

## Findings (step 1)

- `CommunicationArrivedProducer.ThreadColumns` built its thread projection from `RegardingFieldMap.AllRegardingFields`, which
  includes `sprk_regardingreportcard`. The SpaarkeMaster sources agree with the issue: `sprk_Communication/Entity.xml` declares that
  lookup, `sprk_CommunicationThread/Entity.xml` declares the other eleven and not it. Dataverse rejects the whole read; the producer's
  catch (step 2b) sets `thread = null`, and fan-out treats a null thread as private (deny-all), so no outbox row, no ping, no bell
  notification. A real failure of that read must still fail closed: kept, and tested.
- The exclusion already existed TWICE, hand-copied: `CommunicationThreadReadService.ThreadRegardingFields` and
  `ThreadResolver.ThreadRegardingColumns` (the latter used by seven readers: membership derivation, filing sweep, direct-message
  recipient, participant shares, send path). The producer was the one thread read still on the full family. Class grep result below.
- The existing producer seam test hid the defect: its fake `RetrieveAsync` for the thread ignored the column list.
- No schema change and no change to who is notified for a private thread, so the POML escalation trigger did not fire.

## Change

- `Engine/RegardingFieldMap.cs`: ONE definition of the thread's regarding lookups: `ThreadRegardingFields` (tuples) and
  `ThreadRegardingColumns` (names) = `All` minus `sprk_regardingreportcard`. No new service, interface or DI registration.
- `CommunicationThreadReadService.ThreadRegardingFields` and `ThreadResolver.ThreadRegardingColumns` now point at it (behaviour
  unchanged: the same eleven lookups, same order).
- `CommunicationArrivedProducer.ThreadColumns` uses `RegardingFieldMap.ThreadRegardingColumns`.

## Tests

- `tests/integration/seam/Communication/CommunicationArrivedProducerSeamTests.cs`: the fake thread read now rejects what Dataverse
  rejects (a column the thread lacks) and returns only the columns asked for. New tests: the thread read does not ask for the report
  card and is exactly privacy + name + the shared list; a thread holding the regarding (message not yet stamped) fans out and deep
  links to the regarding record; must-not-fire controls: a genuinely private thread still notifies nobody, a real read failure still
  fails closed.
- `tests/integration/data-mutation/Communication/CommunicationThreadRegardingColumnsSchemaAgreementTests.cs` (new): the shared list
  must equal the regarding family members that `sprk_CommunicationThread/Entity.xml` declares (a field added to the communication but not
  the thread fails; a field the thread later gains fails), every column in it exists on the thread entity, the other shared
  definitions agree, and a control proves the XML reader sees the report card on the communication and not the thread.

Evidence on origin/master production code (new seam tests, test files unchanged): 7 of 10 fail (the new thread-column test, the new
thread-regarding fan-out test, and the five pre-existing fan-out/deep-link tests, which now fault exactly as Dataverse does); the
three must-not-fire controls pass. With the fix: all pass. Schema guard mutation check (shared list reverted to the full family): 2 of 4
fail.

## Class grep (Step 9.5 rule 6)

`RegardingFieldMap.AllRegardingFields` / `.All` consumers that read a THREAD: none remain. The others read `sprk_communication`
(`CommunicationService` reply copy/archive, `DirectMessageRecipient`, `PrivateMessageWriteGuard`, `CommunicationThreadFilingSweepJob`
email reads, `RegardingParentEntityMapper`, `CommunicationThreadReadService` message value columns), which carries all twelve.
The thread-side guard is the new schema agreement test. No other defect found.

## Verification

- `dotnet test tests/unit/Sprk.Bff.Api.Tests --filter "FullyQualifiedName~Communication"`: 3495 passed, 2 skipped, 0 failed (includes the 4 seam tests added
  and the 4 schema agreement tests; the producer seam class is 10 tests, the schema class 4).
- `dotnet test tests/Spaarke.ArchTests` (built explicitly first): 2793 passed, 0 failed.
- BFF build: Release publish of both sides succeeded; no warnings introduced in the changed files.
- Publish size (BFF hygiene section 10), `dotnet publish -c Release -r linux-x64 --self-contained false src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/`,
  fresh worktrees at short paths, `Compress-Archive -CompressionLevel Optimal` over `deploy/api-publish/*`, PDBs included: master
  43,578,706 bytes, branch 43,578,789 bytes, delta +83 bytes, 194 files each (171 top level entries each). No package added or changed, so no new CVE exposure.

## Section 11 answers (no new service, interface, endpoint, registration, package or column)

1. Existing: the exclusion already existed twice (`CommunicationThreadReadService.ThreadRegardingFields`, `ThreadResolver.ThreadRegardingColumns`).
2. Extension: the two copies and the producer now use one definition added beside `RegardingFieldMap`; nothing new to register.
3. Cost of doing nothing: every arrival notification on a record thread is dropped (no fan-out, no bell, no deep link).

rework: none task 213
