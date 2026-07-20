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

## #5 — Archived email not run through the Document Profile pipeline  ·  **P2 — root cause CONFIRMED (NOT the suspected regression)**

**Symptom (owner)**: the email/attachments are not being processed through the Document Profile like other files; *"this was being done previously"* → suspected regression.

**Investigation (CONFIRMED — contradicts the regression suspicion)**:
- "Document Profile" = the `AppOnlyDocumentAnalysis` Service Bus job running the "Document Profile" playbook ([`AppOnlyAnalysisService.cs`](../../../src/server/api/Sprk.Bff.Api/Services/Ai/AppOnlyAnalysisService.cs), [`AppOnlyDocumentAnalysisJobHandler.cs`](../../../src/server/api/Sprk.Bff.Api/Services/Ai/Jobs/AppOnlyDocumentAnalysisJobHandler.cs)).
- It **is** wired on **auto-inbound** ([`IncomingCommunicationProcessor.cs:775,889`](../../../src/server/api/Sprk.Bff.Api/Services/Communication/IncomingCommunicationProcessor.cs#L775)) and **outbound-send** ([`CommunicationService.cs:1100,1396`](../../../src/server/api/Sprk.Bff.Api/Services/Communication/CommunicationService.cs#L1100)).
- **Confirmed gap**: the **on-demand "Save to SharePoint"** path (`ArchiveExistingAsync`) creates the `.eml` Document at [`CommunicationService.cs:195`](../../../src/server/api/Sprk.Bff.Api/Services/Communication/CommunicationService.cs#L195) but does **not** enqueue profiling for it (attachments on that path DO). → fix: add the enqueue.
- **NOT a task-007 regression**: task 007 is `not-started`; the retired OOB `email` subsystem never owned Document Profile (inbound was already 100% Graph). No code change removed the enqueue.
- **Why received emails showed no profile in UAT**: the archive itself was **403-broken until 2026-07-19** (F-1/F-2). The `.eml` never reached SPE, so the best-effort (failure-swallowing) profiler had no file to read. Now that archive works, the auto path must be **re-verified** with a fresh inbound email (UAT D-1) — not assumed broken.

**Fix**: task **092** — add the on-demand `EnqueueDocumentAnalysisAsync` for the .eml + re-verify the auto path via D-1.

---

## Owner decisions (2026-07-20)

- **#7** → **Suggest-only**: body/attachment references surface as Suggested review candidates and never auto-file (email 1 → Ambiguous, both matters surfaced, user picks). Aligns with owner spec 2026-07-17; requires ZERO mapper/ladder change (RecordNameMatch already non-auto-file).
- **#4** → **Embed attachments**: the archived `.eml` becomes a faithful original (body + embedded attachments, openable in Outlook), while each attachment is STILL archived as a separate `sprk_document`.
- **Delivery**: all four fixed/planned **in this project** (owner: no deferral). Wrap-up (090) HELD until W9 closes.

## Status → tracked tasks (Wave 9)

| # | Title | Sev | Task | State |
|---|-------|:---:|:---:|---|
| 7 | Body-embedded reference numbers not matched | P1 | **091** | Root cause CONFIRMED · decision locked (Suggest-only) · POML authored |
| 4 | `.eml` faithful original + extension/content-type | P1 | **092** | Scoped · decision locked (embed attachments) · POML authored |
| 5 | Document Profile on-demand archive gap | P2 | **092** | Root cause CONFIRMED (on-demand enqueue gap; not a regression) · POML authored |
| 2 | Attachment subgrid → Document Preview PCF | P2 | **093** | Buildable plan (reuse `RichFilePreviewDialog`) · POML authored |

None of these affect the Association Engine tie/ladder logic (verified correct — the ladder refuses to guess on 2+ ≥0.85; #7 is upstream candidate *recall*).
