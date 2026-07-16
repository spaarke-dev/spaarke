# ADR-046: ACS Messaging Channel — Transport, Threads, Ingestor Seam (Concise) — PLACEHOLDER

> **Status**: 🔒 **RESERVED / PLACEHOLDER** — number claimed 2026-07-16 by `messaging-communication-app-r1` to prevent collision. Full ADR authored during that project. **Do not reuse ADR-046.**
> **Domain**: Communication (messaging channel over ADR-045 seams) — ACS transport + first-class threads
> **Source project**: `messaging-communication-app-r1` (`projects/messaging-communication-app-r1/design.md`)
> **Cross-references (intended)**: extends **ADR-045** (communication architecture / channel seams); **ADR-034** (user-record membership → open-thread membership); **ADR-028** (Auth v2 — server-side ACS token minting); **ADR-004/036** (job contract — Event Grid capture); **ADR-007** (SpeFileStore — transcript/attachment archive); **ADR-024** (regarding family — thread anchor); **ADR-027** (per-customer ACS resource provisioning); **ADR-018/032** (kill-switch / Null-Object). Sibling: **ADR-047** to be claimed by `notification-spine-r1`.

---

## Context (to be expanded)

Messaging is the **second channel** on the ADR-045 communication process. **Azure Communication Services (ACS) Chat is the transport; Dataverse `sprk_communication` is the system of record; the BFF is the sole policy-enforcement + token-minting point.** No native Teams chat, no Dataverse Activities, no portal comments. This ADR will capture the decisions that make ACS additive to the existing engine + the first-class thread model that serves all channels.

## Decision (placeholder — to be authored)

The full ADR will formalize, at minimum:

1. **ACS-as-transport / Dataverse-as-record.** Each chat message is a JSON transport artifact the BFF processes into a `sprk_communication` (type=`Message`=100000004) record + a transcript archive in SPE (via `ICommunicationArchiver`) — analogous to email's `.eml`.
2. **Uniform server-side token minting.** The BFF mints ACS chat tokens server-side for all participants (Entra→ACS exchange is VoIP-only); no ACS admin capability on clients; `communicationUserId` ↔ Dataverse identity mapping.
3. **Inbound ingestor seam.** Add `ICommunicationChannelIngestor` alongside the shipped `ICommunicationChannelSender` / `ICommunicationArchiver` seams — completes ADR-045's stated intent; inbound = Event Grid → webhook → job → normalizer → ingestor → persist → enrichment; idempotent dedupe on ACS message id (also covers the outbound echo).
4. **Direction-symmetric persistence.** Outbound persist-on-send (per ADR-045); inbound persist-on-event; both invoke enrichment.
5. **First-class thread model.** `sprk_communicationthread` entity + `sprk_thread` lookup + thread↔channel child table — the queryable grouping key for email reply-chains *and* chat; assigned by a direction-symmetric `IThreadResolver`. Email conversations become grouped threads too (point-forward).
6. **Access = Dataverse record security.** Open-thread membership derives from `MembershipResolverService` (ADR-034); private threads use an explicit per-record sharing grant; ACS thread membership is a reconciled projection.
7. **Retention-minimized + per-customer resource.** 30-day auto-delete / delete-post-persist so ACS is not a shadow store; per-residency-boundary ACS resource (immutable data location) provisioned via the orchestrator (ADR-027).

## Constraints (to be authored)

MUST / MUST NOT to be written with the project's spec. Anchors already fixed by the design: MUST NOT use native Teams chat / Activities / portal comments; MUST route send/archive/ingest through the ADR-045 seams; MUST mint ACS tokens server-side only; MUST derive membership from Dataverse security, never a parallel ACL.

## References

- Design: `projects/messaging-communication-app-r1/design.md`
- Full ADR (to author): `docs/adr/ADR-046-acs-messaging-channel.md`
- Depends on: ADR-045 (`.claude/adr/ADR-045-communication-architecture.md`)
