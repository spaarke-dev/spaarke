---
name: acs-chat-integration-2026-07-16
description: Azure Communication Services (ACS) Chat integration for governed record-centric legal messaging — transport vs system-of-record split, BYOI token minting, Event Grid capture, membership sync, UI Library React-19 incompatibility, pricing, .NET SDK footprint.
metadata:
  type: project
---

# ACS Chat for governed Dataverse-of-record messaging (2026-07-16)

**Context**: Spaarke r1 feature — ACS = message TRANSPORT, Dataverse = system of record (every message → `sprk_communication`). BFF is sole policy enforcement + mints scoped ACS tokens. Model-driven-app surface, chat only, no SMS/Teams/portal yet.

## Key findings by question

1. **Architecture** — Two-part model: "trusted service" (holds connection string / mints identities+tokens, adds/removes participants) + client app (holds only short-lived user token). Maps cleanly to BFF-as-trusted-service. Thread = conversation; participants = users; **0–250 participants/thread**; message ≤ **28 KB**. Events at user-level (per-recipient, excl. sender) + thread-level. D365 Contact Center precedent CONFIRMED: ACS is PSTN/voice/SMS transport, Dataverse `Conversation` table (`msdyn_ocliveworkitemid`) is the record — exactly the transport-vs-record split Spaarke wants.

2. **Identity/BYOI** — Standard model: server calls Identity API to `createUser` (returns `communicationUserId`) + `getToken(scopes)`. Works for ANYONE — no Entra/Teams license needed; the ACS identity is disjoint from Entra. App owns the ACS-id ↔ Spaarke-user/contact mapping (store `communicationUserId` on Dataverse contact/user). Scopes: `chat`, `chatJoin`, `chatJoinLimited`, `voip`, `voipJoin`. Token lifetime **1–24h (default 24h)**; refresh via credential callback. **Entra-ID→ACS token exchange still VoIP-only (chat NOT supported via Entra exchange)** — so even internal Entra users get ACS chat tokens minted server-side by the BFF, not via OBO exchange. Identity throttle: createUser/issueToken/createUserAndToken = 1000 per 30s; exchangeTokens 500/30s.

3. **Event capture** — Event Grid is still THE path. ACS is an Event Grid source; chat event types incl. `ChatMessageReceivedInThread`, `ChatMessageEditedInThread`, `ChatMessageDeletedInThread`, `ChatThreadCreatedByUser`, `ParticipantAddedToThread...`, etc. Webhook handshake = validation event (respond with validationCode / or validationURL manual). Delivery: at-least-once, unordered, possibly duplicate → handler MUST be idempotent. Retry w/ exponential backoff + configurable dead-letter to Storage.

4. **Membership sync** — Server-side via `ChatThreadClient.AddParticipants` / `RemoveParticipant` (trusted service). Rate: add/remove 10 per 10s + 30/min per thread; 3000/min add per resource. Only participants can send/receive. Caveat: >20 participants disables read receipts + typing indicators.

5. **UI** — `@azure/communication-react` **v1.32.0** (2026-07-07). Production-ready, Fluent-based (mixes Fluent v8+v9), themeable via FluentThemeProvider, ChatComposite drop-in. **CRITICAL BLOCKER: peerDeps require React `>=16.8 <19.0` — does NOT support React 19.** Spaarke Code Pages run React 19 (see [[feedback_shared-lib-react-version-tension]]). Also the package bundles calling deps (`@azure/communication-calling ^1.40`) even for chat-only → heavy client bundle. Options: (a) host chat composite in a React 17/18 island, (b) skip composites, use headless `@azure/communication-chat` (>=1.6.0) + build UI on Spaarke's own Fluent v9 lib.

6. **Deploy/provision** — Need: 1 ACS resource + Event Grid system topic (+ subscriptions to BFF webhook). **Data location chosen at resource-create time and is immutable** → per-customer/region isolation = separate ACS resource per data-residency boundary. Retention: set on Create Chat Thread API — indefinite, or auto-delete 30–90 days, or none. Since Dataverse is record, set SHORTEST retention (or explicit DeleteChatThread) to minimize ACS-held history.

7. **Pricing (pulled 2026-07-16, page ms.date 2026-03-25)** — Chat: **$0.0008 per message sent**. No per-identity, no per-token, no MAU fee. (Calling $0.004/participant/min; SMS separate; not needed for r1.)

8. **.NET SDK** — `Azure.Communication.Chat` **1.4.0**, `Azure.Communication.Identity` **1.3.1** (SMS note only: `Azure.Communication.Sms`). Small managed SDKs (Azure.Core dep already in BFF) → negligible vs 60 MB publish ceiling. Footprint risk is CLIENT bundle, not BFF.

## Sources
- learn.microsoft.com/azure/communication-services/concepts/chat/concepts
- .../concepts/identity-model, .../quickstarts/identity/access-tokens
- learn.microsoft.com/azure/event-grid/communication-services-chat-events + /delivery-and-retry
- .../concepts/ui-library/ui-library-overview ; npm @azure/communication-react 1.32.0
- .../concepts/pricing (ms.date 2026-03-25) ; .../concepts/service-limits (updated 2026-03-05)
- learn.microsoft.com/dynamics365/guidance/reference-architectures/contact-center-* (D365 precedent)

## Follow-up addendum (2026-07-16)

**A. React 19 blocker — how hard?** Genuinely unsupported, not just a peer warning. Open issues Azure/communication-ui-library #6056 + #6042 (reopened 2026-05-18 after Fluent-UI blocker cleared; #5736 closed earlier). NO Microsoft ship-date commitment as of this pull. Root cause was Fluent UI v8 (bundled in composites) not supporting React 19; that's since eased but ACS hasn't shipped support. `--legacy-peer-deps` bypasses the *install* gate but does NOT fix runtime — composites bundle Fluent v8 + calling deps and are not validated on React 19. Verdict: force-install is a real-runtime-risk unsupported path, not a clean workaround. For React-19 code pages use headless `@azure/communication-chat` (>=1.6.0) + Spaarke's own Fluent v9 UI. (Composites work fine if hosted in a React 17/18 island.)

**B. Official sample repos to leverage** —
- `Azure-Samples/communication-services-authentication-hero-csharp` — THE reference for BFF trusted-service: token minting + identity mapping (maps ACS id↔Entra via Graph open extensions; Spaarke maps to Dataverse instead, same pattern) + token-exchange design guides. Most relevant.
- `Azure-Samples/communication-services-dotnet-quickstarts` — .NET Chat + Identity SDK quickstart snippets (createUserAndToken, ChatThreadClient thread/participant mgmt).
- `Azure-Samples/communication-services-web-chat-hero` — full chat composite web app (React) reference; also shows a server token endpoint.
- Event Grid capture: no dedicated hero repo; use Learn quickstart `quickstarts/events/subscribe-to-events` + `quickstarts/sms/handle-sms-events` patterns.
- Headless chat SDK: samples live inside `Azure/communication-ui-library` repo (`samples/`) + the dotnet-quickstarts; no standalone headless-only hero.

**C. SMS added scope for r1** — Same ACS resource as chat (SMS is just another channel on the resource) BUT big added surface. Send needs a number: toll-free (US/CA/PR only) requires MANDATORY toll-free verification since 2024-01-31 (unverified = fully BLOCKED, not throttled); verification takes ~5-6 wks (up to 8). 10DLC/long-code needs brand reg (2-3 biz days) + campaign reg. Short code = 8-12 wks. All are hard regulatory gates. Opt-out/STOP: auto-enforced+relayed for toll-free/short-code (carrier-mandated, can't override); you must honor. Inbound receive = Event Grid `SMSReceived` event (`quickstarts/sms/handle-sms-events`). SMS char limit 140 bytes/segment (GSM-7 160 chars, UCS-2 70). Pricing: per-segment send/receive + per-segment carrier surcharge + monthly number lease (verify on sms-pricing page) + brand/campaign reg fees. .NET SDK `Azure.Communication.Sms` (thin, negligible BFF footprint). **Recommendation: KEEP SMS IN r2** — the 5-6 wk toll-free verification gate + TCPA/opt-out compliance obligations are a hard external dependency that would bottleneck an otherwise-shippable chat r1.

## Open questions
- Whether ACS Entra-ID→ACS chat-token exchange has left preview (still VoIP-only as of this pull) — recheck; affects whether internal users could ever get OBO chat tokens.
- Exact client-bundle size of ChatComposite when tree-shaken chat-only.
- Exact monthly toll-free number lease cost + current per-segment SMS surcharge (on sms-pricing page; not pulled precisely).
