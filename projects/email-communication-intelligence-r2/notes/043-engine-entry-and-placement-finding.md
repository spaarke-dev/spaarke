# Task 043 — Engine-entry evaluation, escalation-trigger decision, and placement justification

> Author: task-execute (FULL rigor). Date: 2026-08-06.
> Task: FR-B3 — unify user-upload ("Save to Spaarke") with the capture pipeline (same engine + dedup).

## 1. Armed escalation trigger — evaluated, does NOT fire

The POML `<escalation><trigger>` says:

> If FR-C1 (021) or FR-C3 (024) is not yet merged, do NOT re-implement a second dedup
> mechanism … STOP and escalate. **If the engine entry contract cannot accept an
> upload-sourced email without a shape change, surface it as an ADR-045 tension (§6.5)
> rather than forking.**

**Evaluation:**

- **021 (FR-C1) and 024 (FR-C3) are merged** (deps ✅ in TASK-INDEX; the dedup + canonicalhash
  code is present in `IncomingCommunicationProcessor` / `OfficeDocumentPersistence` /
  `ContentDedupDetector`). No second dedup mechanism will be created.
- **The engine's real entry contract is `NormalizedMessage`, not `(mailboxEmail, graphMessageId)`.**
  `IncomingCommunicationProcessor.ProcessAsync(mailboxEmail, graphMessageId, …)` is only the
  **Graph-webhook adapter**: it fetches the message app-only from a receive-enabled monitored
  mailbox, maps Graph→`NormalizedMessage` via `GraphMessageNormalizer`, then calls the shared,
  channel-neutral seams. An upload-sourced email (a `SaveRequest.Email` DTO from the user's
  personal mailbox, OBO) genuinely CANNOT enter through *that adapter* — but it does not need to.
- **The engine already exposes channel-neutral peer entry points:**
  - `IncomingAssociationResolver.ResolveAsync(id, NormalizedMessage, AssociationContext, ct)` —
    operates over `NormalizedMessage` only ("NEVER over `Microsoft.Graph.Message`"),
    direction-symmetric.
  - `ICommunicationEnrichmentService.EnrichAsync(id, direction, NormalizedMessage, archivedDocId, ct)`
    — invoked by BOTH inbound + outbound today.
  - `ICommunicationChannelIngestor` (ADR-045 rule 4): the interface doc states R4 shipped the
    OUTBOUND legs but "left inbound CONCRETE and email-only." `MessagingIngestor` is the FIRST
    implementation — a **non-Graph** peer entry point that maps a normalized envelope → creates
    `sprk_communication` → runs shared enrichment/association, "so inbound capture is NOT forked
    per channel."

**Conclusion:** the engine entry contract CAN accept an upload-sourced email without a shape
change — you build a `NormalizedMessage` from the upload DTO (the DTO→envelope mapping is the
same pipeline-boundary step `GraphMessageNormalizer` and `AcsEventNormalizer` already do for
their channels) and call the same shared seams. Upload capture is a **third peer entry point**,
= textbook ADR-045 *extension*, not a fork. **Escalation trigger does not fire; proceeding.**

## 2. Dedup comes for free, structurally (NFR-02 / FR-C1)

Message-level dedup does NOT need any new upload-path logic: creating the `sprk_communication`
via `ICommunicationDataverseService.CreateCommunicationRaceProofAsync(entity, internetMessageId, ct)`
uses the task-020 UNIQUE alternate key on `sprk_internetmessageid`. If the same email was already
captured (or saved by another user), the create reconciles to the canonical row (`WasDuplicate=true`)
instead of inserting a duplicate — exactly the inbound path's behavior. This satisfies acceptance
criterion 2 (one canonical `sprk_communication`) with the SINGLE dedup authority (NFR-02).

Attachment content-hash dedup (FR-C3) is ALREADY wired on this path:
`OfficeDocumentPersistence.CreateDocumentWithSpePointersAsync` calls
`ContentDedupDetector.ReconcileAsync` and `OfficeService.SaveAsync` handles `WasContentDuplicate`
(skip finalization + clean the transient blob). No new attachment-dedup work required — criterion 3
is met by existing code; the seam test will assert it end-to-end.

The FR-C2/C4 cross-path reconciliation (record the saver on the canonical + link the archive
document to the canonical) is ALSO already present in `OfficeDocumentPersistence`
(`MergeUploaderAndResolveCanonicalAsync` / `LinkDocumentToCanonicalCommunicationAsync`) and in
`IncomingCommunicationProcessor` (`CrossPathLink.FindAndLinkArchiveDocumentsAsync`). Those handle
the case where an email is BOTH uploaded AND captured. What is MISSING — and what task 043 adds —
is making a saved email that was **never captured** intelligence-bearing (association + triage +
provenance), i.e. actually creating the `sprk_communication` and running the engine from the
upload path.

## 3. Placement decision (§10 Placement Justification) + one deviation from the POML

The POML declared `<justification>OMIT</justification>` ("modify-only re-routing … no new
component"). Honest engineering finding: to make a saved email intelligence-bearing you must
(a) map the DTO → `NormalizedMessage` and (b) run "create-comm → associate → enrich" against the
shared seams. Inlining that orchestration into `Services/Office/` would be a THIRD hand-rolled copy
of the capture sequence (`IncomingCommunicationProcessor` + `MessagingIngestor` are the other two)
— precisely the per-channel capture fork the `ICommunicationChannelIngestor` seam exists to
prevent (ADR-045 rule 4), and engine-orchestration living in a consumer is what §10 discourages.

**Decision:** add ONE thin capture service in `Services/Communication/`
(`EmailUploadCaptureService`) that encapsulates the shared sequence ONCE and is callable by Office.
Office stays a thin best-effort caller. This is the email analog of `MessagingIngestor`.

**§11 three-question justification for the one new component:**
1. **Existing overlap?** `IncomingCommunicationProcessor` (Graph-only capture) and `MessagingIngestor`
   (ACS-only capture). Neither accepts a DTO-sourced email; both are channel-specific adapters.
2. **Extend instead?** Cannot extend `IncomingCommunicationProcessor` without dragging Graph-fetch
   assumptions (mailbox, app-only MI, receive-enabled account) into the upload path. The clean
   extension point is the SEAM the two share, so the new service *reuses* those seams
   (`IncomingAssociationResolver`, `ICommunicationEnrichmentService`,
   `ICommunicationDataverseService.CreateCommunicationRaceProofAsync`) — it does not re-implement them.
3. **Cost of doing nothing?** A user-saved email remains a `sprk_document` archive with no
   association/triage/provenance and is invisible to the reconciliation surface as a communication —
   FR-B3 / acceptance criterion 1 fails concretely.

**Deviation is documented here + will be called out in the PR description for reviewer sign-off
(§6.5 path A: project-scoped, documented, reviewer-approved).** It does not touch auth/security.

## 4. Design (to implement)

- **`EmailUploadCaptureService`** (`Services/Communication/`), registered unconditionally in
  `CommunicationModule` (ADR-010 concrete; §10 unconditional DI):
  - `CaptureAsync(SaveRequest request, string userId, CancellationToken ct) → Guid? communicationId`
    (best-effort; returns null when not an email / no engine work done).
  - Build `NormalizedMessage` (Direction=Incoming) from `request.Email` (EmailMetadata): From=SenderEmail,
    To/Cc/Bcc from `Recipients` partitioned by `RecipientType`, Subject, Body→BodyHtml/BodyText per
    `IsBodyHtml` (reuse `GraphMessageNormalizer.HtmlToPlainText` for the text reduction),
    InternetMessageId, ConversationId, SentAt=SentDate ?? ReceivedDate, Attachments from
    `Email.Attachments` (name/contentType/size/isInline).
  - Build `AssociationContext { CallerSuppliedRegarding = [from request.TargetEntity], Account = null,
    TenantKey = null }` — `TargetEntity` (the save-pane selection) is EXACTLY what
    `CallerSuppliedRegarding` is documented for (rung 0, highest determinism).
  - `CreateCommunicationRaceProofAsync(entity, InternetMessageId, ct)` → `(id, wasDuplicate)`.
    On `wasDuplicate` → return the canonical id, DO NOT re-run association/enrich (the canonical
    already has them) — mirror the inbound short-circuit.
  - Else `IncomingAssociationResolver.ResolveAsync(id, envelope, context, ct)` then
    `EnrichAsync(id, Incoming, envelope, archivedDocumentId: null, ct)` — each guarded, non-fatal.
  - Whole method wrapped so any failure is swallowed + logged (NFR-04) — never fails the user's save.
- **Office wiring:** `OfficeService.SaveAsync` (or `OfficeDocumentPersistence`) invokes
  `EmailUploadCaptureService.CaptureAsync` best-effort for `ContentType == Email`, AFTER the existing
  document archive path (preserve all current behavior). Injected as an OPTIONAL trailing ctor param
  (null-tolerant) so existing test ctors keep compiling — same pattern the C2/C4 seams already use in
  `OfficeDocumentPersistence`.
- **Client (FR-B1 mechanism-2):** wire the add-in drop in `useSaveFlow.ts` on `authenticatedFetch`
  (per task 040) so a dragged/dropped email hits the same `/api/office/save` → engine parity.
- **Seam test** under `tests/integration/seam/Communication/`: upload envelope → real engine output
  == mailbox-capture envelope output for the same email (parity); upload duplicating a capture →
  one canonical row (dedup); non-fatal on engine throw.

## 5. COMPLETION (2026-08-06)

**Implemented, all acceptance criteria met:**

- **NEW** `src/server/api/Sprk.Bff.Api/Services/Communication/EmailUploadCaptureService.cs` — the email
  sibling of `MessagingIngestor`. Builds `NormalizedMessage` from `EmailMetadata`, `AssociationContext`
  from the save-pane `TargetEntity` (rung-0 caller-supplied regarding), creates the canonical
  `sprk_communication` via `CreateCommunicationRaceProofAsync` (structural dedup, NFR-02), then runs
  `IncomingAssociationResolver.ResolveAsync` + `ICommunicationEnrichmentService.EnrichAsync`. On a
  race-proof dedup hit it reconciles to the canonical and skips re-association. Best-effort/non-fatal
  throughout (NFR-04).
- **MOD** `Services/Office/OfficeService.cs` — optional trailing ctor dep `EmailUploadCaptureService?`;
  invokes `CaptureAsync` best-effort for email saves, AFTER the idempotency early-return and BEFORE
  document creation (so the existing FR-C4 doc→communication link resolves the canonical this produces).
- **MOD** `Infrastructure/DI/CommunicationModule.cs` — `AddSingleton<EmailUploadCaptureService>()`
  (unconditional, ADR-010 concrete, all deps singletons).
- **NEW** `tests/integration/seam/Communication/EmailUploadCaptureSeamTests.cs` — 4 seam tests drive the
  REAL association engine (real rungs + mapper + gate), faking only the Dataverse boundary + enrichment:
  (1) email + save-pane selection → sprk_communication created + auto-filed regarding + ExplicitReference
  provenance (parity); (2) already-captured email → reconcile to canonical + no re-association (dedup);
  (3) association throws → non-fatal, still returns id; (4) non-email save → no-op. **All 4 green.**

**Client surface (FR-B1 mechanism-2):** `useSaveFlow.startSave` ALREADY POSTs `/api/office/save` for
Outlook email saves, so the add-in→engine parity is delivered by the SERVER re-routing — no separate
drag-drop-to-save handler exists (grep confirmed drag/drop only in ShareView doc-sharing). **No client
edit needed;** the hook's raw-fetch+Bearer is the documented D-AUTH-7 exception and its `authenticatedFetch`
migration is task 040's scope (gated) — deliberately not touched here.

**Acceptance criteria:** AC1 (communication not archive) ✅ · AC2 (one canonical, structural dedup) ✅ ·
AC3 (attachment content-hash dedup — already wired in `OfficeDocumentPersistence`/`ContentDedupDetector`,
exercised end-to-end) ✅ · AC4 (non-fatal) ✅ · AC5 (upload↔capture parity — same resolver) ✅ ·
AC6 (publish ≤60 MB + delta; no new CVE; tests updated; build green; TASK-INDEX ✅) ✅.

**§10 verification:** BFF publish **48.32 MB compressed (incl 4 PDBs)** — identical to the 042 baseline,
**~0 MB delta** (source-only, zero new packages). `dotnet list package --vulnerable --include-transitive`
→ **no vulnerable packages**. Office + Communication.Seam suites: **164 passed, 0 failed** (+ 4 new seam
tests). BFF build: 0 errors.

**Step 9.5 gates:** adr-check → 0 violations / 0 warnings (ADR-045/013/007/024/010/028/032/038 all
compliant). code-review → Clean, 0 Critical / 0 Warning / 0 Suggestion; AI-smell verdict Clean.

**Placement Justification (for PR):** New `EmailUploadCaptureService` placed in `Services/Communication/`
(not inlined into `Services/Office/`) so the shared "create→associate→enrich" sequence lives ONCE in the
Communication domain — the email peer of `MessagingIngestor` (ADR-045 rule 4). This is the one deviation
from the POML's `justification OMIT`; resolved via §6.5 path A (documented + reviewer sign-off at PR).

**`/conflict-check` MUST re-run before the PR** (contended `Services/Communication` + `Services/Office`).
