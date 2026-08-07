---
name: dataverse-knowledgearticle-vs-spe-pnp-library
description: Where to home a Policy & Procedures knowledge library on Spaarke (Dataverse knowledgearticle vs SPE vs custom sprk_ table); restricted-table licensing landmine for broker-served external users
metadata:
  type: reference
---

# P&P knowledge library content home — Dataverse KM vs SPE vs custom entity (2026-08-06)

## knowledgearticle = RESTRICTED table (the core landmine)
- `knowledgearticle` (and `routingrule`, `sla`, `entitlement`, `incident`/Case) is on the **Restricted tables requiring Dynamics 365 licenses** list. Microsoft Learn `data-platform-restricted-entities`, page **ms.date 2026-04-27**.
- CRUD (create/update/delete) requires **Dynamics 365 for Customer Service, Enterprise edition** (or legacy CE/D365 plan — no longer sold). So **legal authors editing articles need a D365 Customer Service Enterprise license each.**
- **READ-only** does NOT require a D365 license: *"If an app or flow only reads information from a table, a Dynamics 365 app license isn't required and an appropriate Power Apps or Power Automate license is all that's needed."* — but note that still implies a **Power Apps/Power Automate/Power Pages license for the reading user**, which unlicensed external CIAM contacts do NOT have.
- Entity IS feature-rich: rich-text `content`, major/minor versioning (`knowledgearticleversion`), `statecode` Draft/Approved/Published/Expired/Archived, `expirationdate`, multi-language via translations (`languagelocaleid`), categories (`knowledgearticlescategories`), relevance/knowledge search, approval workflow. All real. Entity reference page ms.date 2025-10-31.

## Multiplexing landmine (why broker-only doesn't dodge licensing)
- S2S/app-only application user needs **no paid Power Apps license** and is free (Learn: build-web-applications-server-server-s2s-authentication). BUT Microsoft multiplexing rule: *"using hardware or software to pool connections, reroute information, or indirectly access the service does not reduce the number of licenses required; every user who inputs or views data, whether directly or indirectly, must have an appropriate license."* (Power Automate licensing FAQ / Power Platform licensing FAQ).
- => Serving Dataverse-stored data to end users through a BFF app-only broker is a licensing gray area **for interactive Power Platform data**. External customer (non-employee) scenarios are usually covered via **Power Pages** capacity or genuinely-custom-app carve-outs, but a **restricted D365 table amplifies the risk** — safest to keep restricted-table data out of the broker path entirely.

## Recommendation for Spaarke (already has SPE + RAG + BFF)
- **Do NOT use `knowledgearticle`.** Restricted-table licensing (author D365 CS Enterprise + reader license + multiplexing for external CIAM users) is disproportionate to a P&P library.
- Home = **SPE (content bytes) + a small custom `sprk_policy` Dataverse entity (metadata/lifecycle)**. `sprk_policy` = standard/unrestricted table → no D365 license, broker-friendly, external-servable. Holds: title, category, owner, version, effectivedate, expirationdate, status (Draft/InReview/Published/Retired), languagecode, SPE driveItem pointer. Reuse SPE for the doc bytes + existing RAG layer for grounded Q&A. One entity covers browse/read AND feeds RAG. Reuses existing infra; no new licensing surface.
- Rich text can live either as a Dataverse rich-text/multiline column (small policies) or as the SPE .docx/.pdf (large, versioned, Word-authored) — Spaarke already serves SPE bytes app-only to external users (see [[spe-ciam-crosstenant-apponly-brokering-2026-07-18]]).

## 2026 MS knowledge direction (context, not a fit)
- Copilot Studio **knowledge center uses Dataverse as central store** for data/knowledge sources; unstructured data as a knowledge source is a thing. Not a browsable P&P library — it's agent grounding config.
- **Business skills in Dataverse (public preview, 2026)** = capture "processes, policies, domain expertise as natural-language instructions" — adjacent but it's agent-instruction authoring, not a document library.
- Foundry IQ / Azure AI Search knowledge bases = the RAG grounding surface (already in Spaarke knowledge/ under foundry-iq, azure-ai-search).

## Sources
- https://learn.microsoft.com/en-us/power-apps/maker/data-platform/data-platform-restricted-entities (2026-04-27) — AUTHORITATIVE restricted-table list
- https://learn.microsoft.com/en-us/power-apps/developer/data-platform/reference/entities/knowledgearticle (2025-10-31)
- https://learn.microsoft.com/en-us/power-platform/admin/powerapps-flow-licensing-faq — multiplexing
- https://learn.microsoft.com/en-us/power-apps/developer/data-platform/build-web-applications-server-server-s2s-authentication — S2S app user no license
- https://www.microsoft.com/en-us/power-platform/blog/2025/03/27/knowledge-in-microsoft-copilot-studio/
- https://www.microsoft.com/en-us/power-platform/blog/2026/05/05/dataverse-agent-data-platform/ — business skills preview

## Open questions
- Exact Microsoft stance on external non-employee users reading a CUSTOM (unrestricted) Dataverse table through a fully-custom .NET app vs requiring Power Pages capacity — needs licensing-desk confirmation; general external-facing custom-app carve-out likely applies but is scenario-specific.
