---
name: content-understanding-vs-docintel-invoices-2026-10-14
description: eBilling extraction choice — Azure Content Understanding GA 2025-11-01 vs DI v4 prebuilt-invoice for law-firm time-entry invoices; CU field schema/enum/grounding limits; gpt-4o retirement (2027-04-14 → gpt-5.1) and successor model picks incl. GPT-6 appearance
metadata:
  type: reference
---

## 2026-10-14: Content Understanding vs Document Intelligence for law-firm invoices + gpt-4o successor
**Question**: Which Azure service should extract header + time-entry lines from law-firm PDF invoices with per-field confidence and grounding, should UTBMS coding happen inside CU, and what replaces gpt-4o?
**Findings**:
- **CU** is GA on API `2025-11-01` (Nov 2025). `2026-06-01-preview` ("CU 2.0") is PREVIEW; the playground defaults to it.
  - Custom analyzers derive from `prebuilt-document` and support arrays of objects (nesting depth 7).
  - Field methods are extract, generate and classify, plus `enum` and `enumDescriptions`. The classify limit is **300 categories in total per analyzer**.
  - Confidence and grounding (`source` D(page,poly), `spans`) are returned **per array-item subfield**, for ALL methods.
  - Limits: 300 pages per request, no published latency numbers.
  - Not available in westus2.
  - .NET SDK `Azure.AI.ContentUnderstanding` 1.1.0 is GA; MI auth via the Content Understanding Reader role.
- **DI v4 `prebuilt-invoice` Items** = Amount, Date, Description, Quantity, ProductCode, Tax, TaxRate, Unit, UnitPrice. There's no timekeeper field, and `queryFields` allows only 20 flat fields. MS guidance routes "invoices from multiple vendors" (high variation) to a **CU custom analyzer**.
- **Recommendation given:** extract with a CU custom analyzer (GA), and keep UTBMS coding as a separate AOAI enum-restricted step. Reasons: the code set varies per matter and version, there's no MS evidence on 60-way classify, and labeled training doesn't support `generate` fields.
- **gpt-4o retirement:** 2024-08-06 and 2024-11-20 retire **2027-04-14 → gpt-5.1**. gpt-4o-mini also retires 2027-04-14 with no replacement listed. **gpt-4.1 family is deprecated (same date).**
  - CU supports models up to gpt-5.5 (not 5.6 or 6); gpt-5.2 is its recommended model.
  - **GPT-6 (sol/luna/astra) and GPT-6.1-sol appeared Sept 2026.** They aren't in the retirement table yet and Azure pricing for them isn't published.
**Sources**: learn.microsoft.com content-understanding/{overview, whats-new, service-limits, concepts/analyzer-reference, concepts/models-deployments, pricing-explainer, choosing-right-ai-tool, document/analyzer-improvement}; REST create-or-replace 2025-11-01; github Azure/content-understanding-toolkit prebuilt-schema invoice.md; DI invoice + add-on pages; foundry/openai/concepts/model-retirement-schedule (2026-09-23); foundry-models models-sold-directly-by-azure (2026-10-08); nuget Azure.AI.ContentUnderstanding. Full write-up: `projects/ebilling-r1/notes/research/content-understanding-vs-docintel.md`.
**Open questions**:
- How does CU behave on very long time-detail arrays near the output limit?
- How accurate is 60-way classify?
- Which region is `spaarke-docintel-invoices-dev` in?
- Live CU and AOAI prices (both pricing pages showed placeholders on 2026-10-14).

Related: [[azure-openai-reasoning-models-2026-07]] (pre-GPT-6 catalog; gpt-4.1 there is now deprecated), [[spaarke-customer-stamp-pricing-2026-08-12]], [[file-aware-playbook-routing-2026-06]].
