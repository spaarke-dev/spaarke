# email-communication-solution-r4 — UAT Defects & Gaps

> **Opened**: 2026-07-19/20 during owner UAT (post-ship, dev/spaarkedev1).
> **Scope**: findings surfaced while exercising the live BFF + PCFs against real inbound email.
> Numbering follows the owner's UAT report (#2, #4, #5, #7). Severity is a first-pass estimate.

---

## #7 — Body-embedded reference numbers are not matched by the Association Engine  ·  **P1 (correctness)**  ·  root cause CONFIRMED

**Symptom (owner)**: an inbound email whose **subject** carried one matter number (`PAT-942665`) and whose **body** carried a second (`Smith & Smith REAL-2026-123456.02`) associated only the subject matter. A second email with **both numbers in the subject** correctly went Ambiguous (both matters surfaced). The body reference was silently dropped.

**Evidence (persisted `sprk_associationprovenance`)**:
- Email 1 `7ed6f45b-e483-f111-8076-7ced8ddc4cc6` (REAL# in **body**) → status **Suggested**; `RecordNameMatch` emitted **only** the Patent matter (`where=subject number=PAT-942665` @0.97). Smith v Smith **absent** from candidates. AI signal DID see the body: *"references a patent application matter and a real estate matter … types=[sprk_matter, sprk_organization]"* → **body was captured**.
- Email 2 `47ddc191-e883-f111-8076-70a8a590c51c` (both #s in **subject**) → status **Ambiguous**; `RecordNameMatch` emitted **both** matters (`REAL-2026-123456.02` @0.97, `where=subject`). Same Smith record (`b68299c6-bafb-f011-8407-7c1e520aa4df`) that email 1 missed.

**Confirmed root cause — retrieve-then-verify recall gap**:
[`RecordNameMatchRung.EvaluateAsync`](../../../src/server/api/Sprk.Bff.Api/Services/Communication/Engine/Rungs/RecordNameMatchRung.cs) is (1) **retrieve** top-`Limit` (=25) candidates from `spaarke-records-index` by keyword ranking, then (2) **verify** name/number containment in subject→body→attachment. Verification checks the body and scores a reference number a flat `NumberConfidence`=0.97 regardless of location — **but only for records that survived step 1.** A number that appears only in the body (amid prose) can fail to rank into the top-25 keyword window, so its record is never verified, even though the number is literally present in the text. Subject placement ranks the record high enough to be retrieved (email 2), body placement does not (email 1).

**Contributing factor**: rung 0 (`ExplicitReference`) did **not** fire on either email — it does not recognize free-text client reference formats (`PAT-…`, `REAL-…`). So *all* number matching rides on rung 3.5's keyword retrieval → single point of failure for body-only references.

**Fix direction (follow-up task — NOT a UAT hotfix)**:
- Add a **direct reference-number extraction** pass over the full normalized text (subject+body+attachment) that resolves extracted tokens against the record store by **exact number lookup** (deterministic, not keyword-relevance ranking). A number already in hand must not depend on keyword retrieval to surface its record.
- Either extend `ExplicitReference` (rung 0) to extract client reference patterns, or add a deterministic number-lookup step inside rung 3.5 that bypasses the `Limit` funnel for exact-number hits.
- Add a regression eval-case: subject-# + different body-# ⇒ Ambiguous (both matters), asserting body-placed numbers reach verification.

**Design note**: the ladder itself is correct — two matters each ≥0.85 on `sprk_regardingmatter` ⇒ Ambiguous, never guess, user picks primary ([`AssociationStatusMapper`](../../../src/server/api/Sprk.Bff.Api/Services/Communication/Engine/AssociationStatusMapper.cs)). The defect is upstream in candidate *recall*, not in the tie decision.

---

## #2 — Attachment subgrid should open the Document Preview modal  ·  **P2 (UX gap / new component)**

**Symptom (owner)**: the OOB Communication Attachment subgrid on the `sprk_communication` form lists the attachment → Document record, but the user expects clicking an attachment to open the **Document Preview modal** with easy access to the file, not navigate to the raw CA/Document record.

**Direction**: replace the OOB Communication Attachment subgrid with a PCF that renders the attachments and opens the existing Document Preview modal on click. Reuse the established preview modal ([`FilePreviewDialog`](../../../src/solutions/LegalWorkspace/src/components/FilePreview/FilePreviewDialog.tsx) / the RecordNavigationModalShell browse pattern) rather than authoring a new preview surface. Subject to the §11 Component Justification + MODAL-DECISION-CRITERIA before building.

---

## #4 — Archived `.eml` not saved in `.eml` format / no extension in SPE  ·  **P1 (data integrity) — CONFIRM**

**Symptom (owner)**: the archived email in SPE has no `.eml` extension and won't open as an email file.

**Where to look**: [`CommunicationService.ArchiveToSpeAsync`](../../../src/server/api/Sprk.Bff.Api/Services/Communication/CommunicationService.cs) — spePath is `/communications/{communicationId:N}/{emlResult.FileName}`; verify (a) `emlResult.FileName` carries the `.eml` extension, (b) the upload sets an `message/rfc822` content-type, (c) the `.eml` bytes are RFC-822 MIME (openable), not a stripped/HTML body. The archive **upload path itself was just fixed** (F-1/F-2 — MI granted on SPE container type), so this is a *format/naming* check on top of the now-working write.

---

## #5 — Archived email not run through the Document Profile pipeline  ·  **P2 (regression) — CONFIRM**

**Symptom (owner)**: the email/attachments are not being processed through the Document Profile like other files; *"this was being done previously"* → suspected regression.

**Where to look**: the archive path creates the `.eml` Document + a Document per attachment, but does not appear to enqueue Document Profile processing. Confirm against the prior R2/R3 behavior what "Document Profile" processing entailed (classification / profiling job) and whether the archive path should enqueue it. Determine whether this regressed during the R4 `Services/Email` retirement (task 007) or was never wired on the new archive path.

---

## Status

| # | Title | Severity | State |
|---|-------|:---:|---|
| 7 | Body-embedded reference numbers not matched | P1 | **Root cause CONFIRMED** — fix is a follow-up task |
| 2 | Attachment subgrid → Document Preview modal | P2 | Logged (new PCF) |
| 4 | `.eml` format/extension in SPE | P1 | Logged — needs confirm on now-working archive path |
| 5 | Document Profile processing of archived email | P2 | Logged — suspected regression, needs confirm |

None of these block the Association Engine tie/ladder logic (verified correct). #7 and #4 are the two that warrant a follow-up work item before this is considered production-clean.
