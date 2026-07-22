# spaarke-legal-front-door-r1

Corporate **legal intake / "legal front door"** module initiative — business users submit legal service requests; the legal team receives, triages, prioritizes, assigns (internal or outside counsel), works, and manages them; requesters get visibility into their own requests.

## Contents
| File | Purpose |
|---|---|
| [`market-survey.md`](./market-survey.md) | Market survey of 16 vendors across 5 archetypes, 17-feature taxonomy, and full Spaarke capability mapping (9 Have · 5 Partial · 2–3 Gap) |
| [`phasing-roadmap.md`](./phasing-roadmap.md) | 6-phase delivery roadmap (Phase 0 spine → internal MVP → requester portal → SLA → AI → enterprise) with sequencing rationale |

Rendered survey (shareable): published to claude.ai as an Artifact — see conversation.

## Headline finding
The category maps unusually cleanly onto existing Spaarke primitives. It is a **composition over the platform**, not a standalone build. Genuine gaps: external requester portal (Power Pages), SLA engine, optional conversational AI intake / e-billing. The **CreateWorkAssignment wizard already delivers internal-vs-outside-counsel assignment** — the customer's headline ask.

## Status
Pre-spec. Next: confirm the `sprk_legalrequest` spine decision → `/design-to-spec` → `/project-pipeline`.
