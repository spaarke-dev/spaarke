# Task 012 — inject signed tracking footer on the outbound send path (FR-A1) — COMPLETE

> 2026-08-06. FULL rigor · sonnet tier (run on Opus session). Committed to the branch.

## What shipped

- **MOD** `Services/Communication/CommunicationService.cs`:
  - New private `ApplyTrackingFooterAsync(request, correlationId, ct)` — returns the request with the
    transparent, HMAC-signed footer appended to `Body` when the footer is enabled for the tenant AND the
    request regards a record AND a signed token is produced; otherwise returns it unchanged. Fully
    try/catch → returns the ORIGINAL request on any failure (NFR-04 / ADR-045 — never fails the send).
  - Called at BOTH email send sites (shared-mailbox branch ~L1047 + OBO/user branch in `SendAsUserAsync`
    ~L1374) — the SAME helper, no forked footer logic. The returned request is used for the SEND only; the
    persisted Dataverse record keeps the original body.
  - Ctor gains two OPTIONAL trailing deps `ITrackingTokenSigner? trackingTokenSigner` + `TrackingFooterGate?
    trackingFooterGate` (matches the class's existing optional-trailing pattern; all 13 existing test ctor
    sites keep compiling; production DI supplies both — registered by 010/011). Null → no footer.
- **NEW** `tests/…/Services/Communication/CommunicationServiceFooterTests.cs` — 7 tests through the public
  `SendAsync` with a recording channel-sender double + stub signer (no transport mocks, ADR-038): enabled+
  regarding → footer+token in body; disabled → unchanged; no-regarding → unchanged; signer throws → send
  still succeeds, no footer; signer null (KV unavailable) → no footer; plain-text body → `---` text footer
  (not HTML); OBO branch → footer (proves both branches share the helper). **7/7 green.**

## Escalation trigger — evaluated, did NOT fire

"If the tenant key or regarding-record shape is not available at the send sites (needs threading a new param
through the dispatcher / mutating `ChannelSendRequest`), STOP + escalate." Both are available:
- **Regarding** = `SendCommunicationRequest.Associations` (first valid `CommunicationAssociation`).
- **Tenant key** = null (single-org, consistent with `AutoFileGate` / `AssociationContext.TenantKey`); the
  gate + signer resolve global config/secret.
- `ChannelSendRequest` is UNTOUCHED — the footer mutates a COPY of `SendCommunicationRequest` (a record:
  `request with { Body = … }`) before the `ChannelSendRequest` is built. No contract change.

## Interpretation note (DTO shape vs. POML "both bodies")

The POML said "inject into BOTH the HTML and plain-text bodies." `SendCommunicationRequest` carries a SINGLE
`Body` + `BodyFormat` (HTML or PlainText), not separate HTML/text fields — so the footer is rendered to MATCH
`BodyFormat` (a visible `<hr/><p>…</p>` for HTML, a `---` block for text). This keeps it transparent
(ADR-028: no hidden/invisible markup) and quoted back on reply for the `TrackingTokenRung` (013). Not a
behavior deviation — the dual-body instruction just doesn't map to this DTO.

## Verification

- BFF build 0 errors. Footer tests 7/7 green. Full `CommunicationService` suite **48/48** (no regression from
  the ctor + send-path edits). No vulnerable packages; zero new NuGet. Publish **48.33 MB compressed** (+0.00
  vs 010 baseline). `/conflict-check`: no open-PR overlap on `CommunicationService.cs`.
- Step 9.5: adr-check 0 violations (ADR-045/018/028/013/010/038); code-review clean.

## Placement Justification (for PR)

The footer injection EXTENDS the existing send path in place (a private helper on `CommunicationService`) — no
new service, no new endpoint, no new DI registration. It consumes the already-registered `ITrackingTokenSigner`
(010) + `TrackingFooterGate` (011). Per §10 / `.claude/constraints/bff-extensions.md`.

## Scope boundary

Add-in COMPOSE-time footer injection (`src/client/office-addins/**`) is a separate Phase 4 (Pillar B) task —
NOT built here. This task is the server send-path injection only.

## Remaining (feature activation)

Same as 010: the footer stays inert until the operator sets the Key Vault secret + `SigningKeySecretName` +
`Enabled = true` (runbook in `notes/010-tracking-token-signer-complete.md`). Next: **013** (`TrackingTokenRung`
— verify inbound footer tokens on capture, deterministic auto-file when valid).
