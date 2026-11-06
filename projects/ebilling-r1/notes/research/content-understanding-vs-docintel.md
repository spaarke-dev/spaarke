# Content Understanding vs Document Intelligence for eBilling extraction, and the gpt-4o successor

> Researched 2026-10-14 by the `researcher` sub-agent for eBilling R1 (FR-03 Document Intelligence spike, FR-04 extraction confidence, FR-22 UTBMS coding, NFR-05).
> Every claim cites a source and the date that source was last updated. **GA** and **PREVIEW** are marked inline.
> No `knowledge/` folder covers Content Understanding (CU) or Document Intelligence (DI) yet, so every finding below comes from Microsoft Learn, Microsoft GitHub, NuGet or the Foundry blog.

---

## 0. Bottom line

1. **Extraction: use a CU custom analyzer on the GA API `2025-11-01`.** Base it on `prebuilt-document` and seed its schema from `prebuilt-invoice`. Add law-firm arrays (`TimeEntries`, `Expenses`, `TimekeeperSummary`) and set `estimateSourceAndConfidence` on every field. Each line subfield then returns its own confidence, page and polygon (`source`) and its markdown span. That covers the FR-04 "extraction confidence (JSON per field with source page)" requirement without any grounding code of our own. Microsoft's own decision guide places "invoices from multiple vendors with different formats" (high-variation, semi-structured) on a **CU custom analyzer**, not the DI prebuilt.
2. **Do not use DI v4.0 `prebuilt-invoice` for time-entry lines.** Its `Items` schema has no timekeeper, no hours or rate semantics and no narrative field. `queryFields` caps at 20 flat fields per request and can't define a line-item table.
3. **Keep UTBMS coding and AI observations as a separate Azure OpenAI structured-output step** over the extracted lines. CU `classify` fields do support `enum` and `enumDescriptions`, with up to 300 categories in total per analyzer. The separate step is still better here for four reasons:
   - The code set varies per matter and per version, and a CU enum is fixed when the analyzer is built.
   - Coding needs context that isn't in the PDF.
   - Labeled training doesn't apply to `generate` fields.
   - Microsoft has published no accuracy evidence for 60-way `classify`.

   Trial CU `classify` for the stable 28-value activity code in the FR-03 spike, as an experiment only.
4. **Model.** gpt-4o `2024-08-06` and `2024-11-20` retire on **2027-04-14**, with **gpt-5.1** listed as the replacement. gpt-4o-mini also retires on 2027-04-14 with no replacement listed. The **gpt-4.1 family is deprecated with the same 2027-04-14 date**, so don't move to it.
   - CU extraction: start on **gpt-5.2**, CU's recommended completion model (GA, retires 2027-06-08). Run a bake-off against **gpt-5.4-mini** for cost. CU doesn't support gpt-5.6 or GPT-6 yet.
   - Coding and classification: **gpt-5.4-mini** at low reasoning effort (GA, retires 2027-09-21). Bake it off against **gpt-5.6-luna** (GA, retires 2028-01-11).

---

## 1. Azure AI Content Understanding

### 1.1 Status, API versions, regions

| Item | Finding | Source (updated) |
|---|---|---|
| GA | **GA since Nov 2025 with API `2025-11-01`.** "Use API version `2025-11-01` (GA) for production workloads." | [Overview](https://learn.microsoft.com/en-us/azure/ai-services/content-understanding/overview) (2026-09-24); [What's new](https://learn.microsoft.com/en-us/azure/ai-services/content-understanding/whats-new) (2026-09-09) |
| Preview | **`2026-06-01-preview` ("CU 2.0", Build 2026 wave): PREVIEW, no SLA.** Adds agentic mode, synchronous Read/Layout, advanced contextualization, distilled labeled-sample training, signature detection, document metadata and in-page segmentation. | Same; [Foundry blog 2026-08-12](https://devblogs.microsoft.com/foundry/azure-content-understanding-updates-august-2026) |
| Aug 2026 GA refresh | The GA `2025-11-01` API got GPT-5 series support and "improved grounding efficiency": up to 28% fewer inference tokens for GPT-4.1 and GPT-5.2, up to +3% accuracy, and confidence-model AUROC up to +14% (Microsoft internal evaluation). | [Foundry blog 2026-08-12](https://devblogs.microsoft.com/foundry/azure-content-understanding-updates-august-2026) |
| The owner's playground | The Studio/Foundry playground showed "2026-06-01 (Preview)", which is the preview API. **Production code should pin `2025-11-01`.** | Overview (2026-09-24) |
| Regions (GA) | australiaeast, canadacentral, eastus, eastus2, japaneast, southcentralus, southeastasia, swedencentral, uksouth, westeurope, westus, **westus3**. **westus2 is NOT listed.** | [Region support](https://learn.microsoft.com/en-us/azure/ai-services/content-understanding/language-region-support) (2026-09-19) |
| Cross-region models | Since Mar 2026, "Bring Your Own Capacity" lets a CU resource use Azure OpenAI deployments in any resource or region. | What's new, March 2026 (2026-09-09) |

### 1.2 How analyzers work

- An **analyzer** combines content extraction (OCR and layout), an optional field schema, config and model mappings. Custom analyzers must derive from one of four bases, `prebuilt-document|image|audio|video`. [Prebuilt analyzers](https://learn.microsoft.com/en-us/azure/ai-services/content-understanding/concepts/prebuilt-analyzers) (2026-09-09), [Analyzer reference](https://learn.microsoft.com/en-us/azure/ai-services/content-understanding/concepts/analyzer-reference) (2026-09-24)
- **To customise a prebuilt**, `GET /analyzers/prebuilt-invoice`, edit the definition and `PUT` it as your own analyzer. Microsoft: "Prebuilt analyzer definitions can change across API versions … make a copy of the prebuilt analyzer instead of relying on the prebuilt version directly in production." (Prebuilt analyzers, 2026-09-09)
- **Models.** CU always uses *your* Foundry deployments, because managed capacity was retired at GA. The resource needs a chat-completion deployment and an embedding deployment. Prebuilts reference aliases:
  - `prebuilt-analyzer-completion` (most prebuilts)
  - `prebuilt-analyzer-completion-mini` (e.g. `prebuilt-*Search`; this is the "mini model" the playground asked for)
  - `prebuilt-analyzer-embedding` (used for labeled samples and in-context learning)

  Aliases map to deployments via `PATCH /contentunderstanding/defaults` or per request via `modelDeployments`. "GPT-5.2 is a recommended model." [Model deployments](https://learn.microsoft.com/en-us/azure/ai-services/content-understanding/concepts/models-deployments) (2026-09-24). The playground's "gpt-5-reasoning" label isn't a documented model name. It's most likely Studio's display of whatever deployment the `prebuilt-analyzer-completion` alias points to (unverified).
- **Supported completion models** (GA list): gpt-5.5, gpt-5.4, gpt-5.4-mini, gpt-5.2, gpt-5.1, gpt-5, gpt-5-mini, gpt-5-nano, gpt-4.1/-mini/-nano, gpt-4o (2024-11-20, 2024-08-06), gpt-4o-mini. Supported embeddings: text-embedding-3-small/-large, ada-002. **Not listed: gpt-5.6-\*, gpt-6-\*.** The page lists every GPT-5 version as `2025-12-11`, which looks like a doc error. [Service limits](https://learn.microsoft.com/en-us/azure/ai-services/content-understanding/service-limits) (2026-09-16)

### 1.3 Prebuilt invoice analyzer (`prebuilt-invoice`, GA)

Schema from the official toolkit, [invoice.md (2025-11-01)](https://github.com/Azure/content-understanding-toolkit/blob/main/prebuilt-schema/2025-11-01/procurement/invoice.md) (read 2026-10-14):

- **Header:** VendorName, VendorAddress(+Recipient), VendorTaxId, CustomerName, CustomerId, CustomerAddress(+Recipient), CustomerTaxId, BillingAddress, ShippingAddress, ServiceAddress, RemittanceAddress (each +Recipient), InvoiceId, InvoiceDate, DueDate, PONumber, PaymentTerm, CountryRegion, plus the amount objects SubtotalAmount, TotalTaxAmount, TotalDiscountAmount, TotalAmount, AmountDue and BalanceForward. Each amount object holds `{Amount, CurrencyCode}`. Also `TaxDetails[]`.
- **`LineItems[]`** (array of objects, method `generate`). Each subfield is `extract`, except CurrencyCode, which is `generate`.
  - Date, Description, ProductCode, Quantity
  - QuantityUnit (described as "e.g., kg, lb, hours")
  - UnitPrice{Amount,CurrencyCode}, TotalAmount{…}, TaxAmount{…}, TaxRate
- **There are no timekeeper, title or class, hours, rate, narrative, billing-period, matter-reference or UTBMS fields, and no enum fields.** A time entry would land as Quantity = hours, QuantityUnit = "hours", UnitPrice = rate and Description = narrative. The timekeeper's name would be lost or merged into Description.
- Billing meters: Document **Standard** (layout) extraction plus **Standard** contextualization. [Pricing explainer](https://learn.microsoft.com/en-us/azure/ai-services/content-understanding/pricing-explainer) (2026-10-07)
- `prebuilt-procurement` is a composed classifier/router over invoice, receipt, PO, utility bill and credit memo.

### 1.4 Custom field schemas: can we model law-firm invoices?

**Yes. Each item below is GA on `2025-11-01` unless marked otherwise.**

| Question | Answer | Source |
|---|---|---|
| Field types | string, date, time, number, integer, boolean, array, object, json. "Table field" = array of objects. "Fixed table" = object of objects. | [REST `ContentFieldDefinition` 2025-11-01](https://learn.microsoft.com/en-us/rest/api/contentunderstanding/content-analyzers/create-or-replace?view=rest-contentunderstanding-2025-11-01) (2026-02-26); Service limits (2026-09-16) |
| Line-item array with custom subfields (timekeeper, hours, rate, narrative) | **Yes.** `{"type":"array","items":{"type":"object","properties":{…}}}`. Microsoft recommends arrays of objects for repeated data, not JSON-in-string. Nesting depth was raised from 5 to 7 in Mar 2026. Best practice is to avoid going deeper than 2–3 levels. | Analyzer reference (2026-09-24); [Best practices](https://learn.microsoft.com/en-us/azure/ai-services/content-understanding/concepts/best-practices) (2026-08-10); What's new, Mar 2026 |
| Field methods | `extract` (documents only; value as it appears; **requires `estimateSourceAndConfidence: true`**), `generate` (free inference), `classify` (pick from `enum`). The method is optional and the service picks one if you omit it. | Analyzer reference (2026-09-24) |
| Field-definition extras | `description` (acts as a mini-prompt; descriptions ≤ 1,024 chars), `examples[]`, `enum[]`, **`enumDescriptions{}`** (a description per enum value), `$ref` + `definitions` (reusable sub-schemas) | REST 2025-11-01 (2026-02-26); Service limits |
| Field-count limits | Max 1,000 fields per analyzer. A group with 3 subfields counts as 3. | Service limits (2026-09-16) |
| Enum / classify limits | **"Max classify field categories: 300", the total across ALL classify fields in the analyzer.** Example: task (≤ 60) + activity (28) + expense (~40) ≈ 130, which fits. This is separate from document classification (`contentCategories`), which allows 200 categories with name + description ≤ 120 chars. | Service limits (2026-09-16) |
| Is `classify` reliable for ~60 values? | **No Microsoft benchmark or guidance exists** for many-valued field classification. Best practices only cover document-category design. There's no evidence either way, so the FR-03 spike has to measure it. | Best practices (2026-08-10) |
| Multi-page tables | Returned as one logical table by default (since Nov 2025). | What's new, Nov 2025 |
| Normalisation | Dates and numbers are auto-normalised (ISO dates, doubles). You can't configure it. **The raw text isn't returned separately**, so the markdown `spans` are the way to recover it. | Analyzer reference (2026-09-24); What's new, Jul 2026 |

### 1.5 Confidence and grounding

- **Confidence (0–1) and grounding are available for ALL document field types**, including `generate` and `classify`, not just `extract`. They're enabled analyzer-wide with `config.estimateFieldSourceAndConfidence` or per field with `estimateSourceAndConfidence`. The two GA sources disagree on when this arrived: Nov 2025 per What's new, and "Jul 2026, preview and GA" per the analyzer-improvement page. Both now state it as GA behaviour. [Document overview](https://learn.microsoft.com/en-us/azure/ai-services/content-understanding/document/overview) (2026-08-18); [Analyzer improvement](https://learn.microsoft.com/en-us/azure/ai-services/content-understanding/document/analyzer-improvement) (2026-08-10)
- **Shape:** each field carries `confidence`, `spans[{offset,length}]` into the result markdown, and `source` as a string like `D(page,x1,y1,…,x4,y4)`, with several regions separated by `;`. **Inside `LineItems.valueArray[i].valueObject`, every subfield has its own confidence and source.** Some generated subfields, such as CurrencyCode, carry neither. [REST quickstart sample output](https://learn.microsoft.com/en-us/azure/ai-services/content-understanding/quickstart/use-rest-api) (read 2026-10-14). The `D(...)` grammar isn't formally specified in the docs. The Java SDK parses it into `pageNumber`/`polygon`/`boundingBox`, and the .NET SDK reports the coordinate unit.
- Microsoft's illustrative thresholds: ≥ 0.90 for critical fields, ≥ 0.80 for important ones, ≥ 0.70 for others. They say to "determine thresholds experimentally". Confidence is supported for documents only. (Best practices, 2026-08-10)
- Turning on grounding and confidence roughly **doubles LLM token usage**: "with source estimation + confidence enabled, the token usage is ~2x more per page". (Pricing explainer, 2026-10-07)

### 1.6 In-context learning, labeled examples, knowledge sources

- `knowledgeSources: [{kind:"labeledData", containerUrl, prefix, fileListPath}]`. Labeling happens in CU Studio ("Auto label", then correct the values). The limit is 1 GB and 50,000 pages. The embedding model is used whenever labeled data exists. (REST 2025-11-01; Service limits)
- **GA `2025-11-01`:** the built analyzer *retains* the labeled docs and retrieves examples into the prompt at analysis time, which adds LLM input tokens. **PREVIEW `2026-06-01`:** training "distils" the samples into the analyzer, which neither retains nor needs them at runtime, and the analyzer bills at the **advanced** contextualization rate. [Data privacy](https://learn.microsoft.com/en-us/azure/ai-foundry/responsible-ai/content-understanding/data-privacy) (2026-08-10); Pricing explainer (2026-10-07)
- **Training "doesn't currently support fields that use the `generate` method".** Labeled samples also don't fix OCR errors. (Analyzer improvement, 2026-08-10). Consequence: if UTBMS codes were `generate` fields, labeled examples wouldn't help them. `classify` and `extract` fields can be trained.

### 1.7 Limits and latency

| Limit | Value |
|---|---|
| Async PDF/image | ≤ 200 MB, **≤ 300 pages** per request. A page range can be specified. |
| Sync (PREVIEW, read/layout only) | ≤ 10 MB, ≤ 5 pages |
| Throughput (S0) | 1,000 pages per minute, 3,000 operations per minute, 100,000 analyzers |
| Model output | Bounded by the deployment. GPT-5.x max output is 128K tokens ([model catalog](https://learn.microsoft.com/en-us/azure/foundry/foundry-models/concepts/models-sold-directly-by-azure), 2026-10-08). **The docs don't say whether CU chunks very long line-item arrays internally.** |
| Latency | **No published figures.** Analyze is async: POST returns 202, then you poll every 1–2 s. Add `x-ms-diagnostics: true` to get model-call and latency diagnostics. (REST quickstart; Service limits 2026-09-16) |

Agentic mode (PREVIEW) needs about 400K TPM per job, can't use `extract` fields and can't use labeled samples. It isn't needed for R1. [Agentic mode](https://learn.microsoft.com/en-us/azure/ai-services/content-understanding/concepts/agentic-mode) (2026-08-10)

### 1.8 Pricing model

There are three parts. [Pricing explainer](https://learn.microsoft.com/en-us/azure/ai-services/content-understanding/pricing-explainer) (2026-10-07):

1. **Content extraction per 1,000 pages.** Meters are Minimal (digital), Basic (OCR) and **Standard (layout, the one that applies to scanned invoices)**. The explainer's illustration uses $5 per 1,000 pages. The live pricing page showed only placeholders on 2026-10-14, so **verify with the Azure calculator**.
2. **Contextualization:** 1,000 tokens per page. Standard is $1 per 1M (≈ **$1 per 1,000 pages**). Advanced (PREVIEW; labeled data on the preview API, or agentic mode) is $3 per 1M.
3. **LLM and embedding tokens are billed on our own Foundry deployment**, not by CU, and CU doesn't charge twice. The invoice example works out to about 5,200 input and 180 output tokens per page with grounding on. Line-dense time-detail pages will produce far more output tokens than that example.

There's no charge for a failed request, but Foundry tokens that were already consumed are still billed. For comparison, the DI prebuilt was about $10 per 1,000 pages per our 2026-08-12 pricing memo, which needs re-verifying.

### 1.9 Auth, SDK, data handling

- **Auth (GA):** Entra ID tokens with scope `https://cognitiveservices.azure.com/.default`, including **managed identity**, or a key. Built-in roles (May 2026) are **Cognitive Services Content Understanding Reader** (enough to run analyze), Contributor and Owner. VNet, private endpoints and CMK are supported. A CU resource is a Foundry resource with a custom subdomain, so the Entra path works. That isn't true of `spaarke-docintel-dev`, which ADR-028 records as key-only. [Secure communications](https://learn.microsoft.com/en-us/azure/ai-services/content-understanding/concepts/secure-communications) (2026-06-05); REST security section; `.claude/adr/ADR-028-spaarke-auth-architecture.md`
- **.NET SDK:** `Azure.AI.ContentUnderstanding` **1.1.0 GA (2026-04-22)** targets net8.0, net10.0 and netstandard2.0 and depends on Azure.Core ≥ 1.53.0. **1.2.0-beta.3 (2026-08-11)** is PREVIEW and defaults to `2026-06-01-preview`. The SDKs went GA in Mar 2026 for the `2025-11-01` API. Samples: [Azure-Samples/azure-ai-content-understanding-dotnet](https://github.com/Azure-Samples/azure-ai-content-understanding-dotnet). [NuGet](https://www.nuget.org/packages/Azure.AI.ContentUnderstanding) (read 2026-10-14); What's new, Mar/Aug 2026
- **Data:**
  - Data at rest stays in the resource region.
  - Inputs are deleted when processing completes. Results are retained ≤ 24 h, and results can be explicitly deleted.
  - LLM processing location follows the deployment type (Global, DataZone or regional). The `processingLocation` analyzer property can be geography, dataZone or **global (default)**.

  (CU data privacy, 2026-08-10; REST 2025-11-01)
- **No training on our data.** The Azure OpenAI data page says prompts and completions "are NOT used to train any generative AI foundation models without your permission" and "are not used to train, retrain, or improve the base models". [Foundry data privacy](https://learn.microsoft.com/en-us/azure/foundry/responsible-ai/openai/data-privacy) (2026-06-05)
- Guardrails (content filters) on the model deployment can suppress generated field values and return warnings, so check warnings before automating on them. (Overview, 2026-09-24)

---

## 2. Document Intelligence v4.0 `prebuilt-invoice`

| Item | Finding | Source (updated) |
|---|---|---|
| Status | v4.0 `2024-11-30` is **GA, current, no end date**. v3.0 retires 2029-03-30. v2.1 retires 2027-09-15. | [DI overview](https://learn.microsoft.com/en-us/azure/ai-services/document-intelligence/overview?view=doc-intel-4.0.0) (2026-09-08) |
| `Items` subfields | **Amount, Date, Description, Quantity, ProductCode, Tax, TaxRate, Unit, UnitPrice.** That's all of them. | [Schema 2024-11-30-ga/invoice.md](https://github.com/Azure-Samples/document-intelligence-code-samples/blob/main/schema/2024-11-30-ga/invoice.md) (read 2026-10-14); [invoice page](https://learn.microsoft.com/en-us/azure/ai-services/document-intelligence/prebuilt/invoice?view=doc-intel-4.0.0) (2026-08-14) |
| Header fields | CustomerName/Id, PurchaseOrder, InvoiceId, InvoiceDate, DueDate, Vendor\*, Customer\*, Billing/Shipping/Remittance/Service address (+Recipient), SubTotal, TotalDiscount, TotalTax, InvoiceTotal, AmountDue, PreviousUnpaidBalance, **ServiceStartDate/ServiceEndDate** (usable for the billing period), VendorTaxId, CustomerTaxId, PaymentTerm, KVKNumber | Same |
| Time-entry tables | **No documented support.** Hours would land in Quantity, rate in UnitPrice and narrative in Description. There's no timekeeper field, and the docs say nothing about timesheets. Supported types are "Invoices, utility bills, sales orders, purchase orders". | Invoice page (2026-08-14) |
| Add-ons | `queryFields` (premium; **max 20 fields per request**; flat fields, so it can't add a line-item column), `keyValuePairs`, `ocrHighResolution`, `languages`, `barcodes`, `styleFont`, `formulas` | [Add-on capabilities](https://learn.microsoft.com/en-us/azure/ai-services/document-intelligence/concept/add-on-capabilities?view=doc-intel-4.0.0) (2026-08-14) |
| Limits | ≤ 2,000 pages, ≤ 500 MB (S0) | Invoice page |
| Custom models | Custom **template** and **neural** models need ≥ 5 labeled samples. Neural supports tables and table/row/cell confidence in v4.0. The old **custom generative** extraction model (2024-07-31-preview) is gone from v4.0 docs: its URL now redirects to the template/neural custom-model page. LLM-based custom extraction effectively lives in CU now (inference from the redirect). | [Custom models](https://learn.microsoft.com/en-us/azure/ai-services/document-intelligence/train/custom-model?view=doc-intel-4.0.0) (2026-07-10) |
| DI ↔ CU relationship | "As part of Azure Content Understanding capabilities, Azure Document Intelligence provides high-accuracy and reliable deterministic extraction from structured documents." CU "brings together" DI capabilities with LLM analyzers. **DI isn't deprecated:** "If you're already running Document Intelligence in production … No migration is required." | DI overview (2026-09-08); [Choose the right tool](https://learn.microsoft.com/en-us/azure/ai-services/content-understanding/choosing-right-ai-tool) (2026-06-05) |
| Microsoft's guidance for new projects | **OCR or layout only → CU `prebuilt-read`/`prebuilt-layout`** ("Lower cost and richer layout extractions"). **Standard structured forms with a prebuilt → DI prebuilt.** **High-variation semi-structured ("Invoices from multiple vendors with different formats") → CU custom analyzer (Recommended)**: "Zero-shot to start … Add labeled samples to knowledge source to improve accuracy over time." Custom labeled extraction on highly structured docs → DI custom neural. On-prem → DI containers. | Choose the right tool (2026-06-05) |

**Reading for Spaarke.** Microsoft's own guidance makes CU the default for new document work. Even the existing BFF's DI `prebuilt-layout` use (Compose, `DocumentIntelligenceService`) has a CU equivalent, though nothing forces a migration.

---

## 3. Fit assessment for law-firm invoices

### 3.1 Options compared

| Need | A: today (DI read/layout + gpt-4o structured output) | B: DI `prebuilt-invoice` (+ queryFields) | **C: CU custom analyzer (GA)** |
|---|---|---|---|
| Header (firm, invoice no., period, client-matter ref, totals) | Yes (LLM) | Partial. Matter ref needs queryFields. | **Yes**, with descriptions and aliases per field |
| Time entries (date, timekeeper, title, hours, rate, amount, long narrative) | Yes (LLM) | **No timekeeper.** Hours and rate only by convention. | **Yes**, custom array subfields |
| Expense lines, timekeeper summary | Yes | Mixed into Items | **Yes**, separate arrays |
| Per-field confidence | LLM self-report only (uncalibrated) | Yes (model-native) | **Yes, per array-item subfield.** Confidence model refreshed Aug 2026. |
| Grounding (page + polygon) | **No.** We would need to fuzzy-match values back to layout spans ourselves. | Yes | **Yes, per subfield** (`source`, `spans`) |
| Scanned PDFs, multi-page tables | Yes (DI OCR) | Yes | Yes (same OCR lineage; multi-page tables merged) |
| Improve over time | Prompt edits | Retrain a custom neural model (≥ 5 labels per variant) | Field descriptions, then labeled samples in Studio |
| Maintenance | Our prompt, schema and parser | Low, but the schema doesn't fit | Analyzer JSON under source control; CU owns the model lifecycle |

**Recommendation: Option C.** Its advantage over today's pipeline is grounding and confidence per line subfield without writing a grounding layer, which FR-04 needs and the review screen will use. Accuracy still has to be shown in the FR-03 spike against Option A on ≥ 30 invoices.

### 3.2 Schema sketch for the spike (illustrative)

Base the analyzer on `prebuilt-document` with `config.estimateFieldSourceAndConfidence=true`, `enableOcr=true`, `enableLayout=true` and `tableFormat="markdown"` (or html).

- **Header:** FirmName, FirmTaxId, InvoiceNumber, InvoiceDate, BillingPeriodStart, BillingPeriodEnd, ClientName, ClientMatterReference, MatterName, Currency, FeesTotal, ExpensesTotal, DiscountTotal, TaxTotal, InvoiceTotal, PriorBalance, AmountDue. Use `extract` for all of them.
- **`TimeEntries[]`:** Date, TimekeeperName, TimekeeperTitle (as billed), TimekeeperInitials, Hours, Rate, Amount, Narrative, TaskCodeAsBilled, ActivityCodeAsBilled. Use `extract`. Firm-supplied codes stay "as billed" (FR-04).
- **`Expenses[]`:** Date, Description, Quantity, UnitCost, Amount, ExpenseCodeAsBilled
- **`TimekeeperSummary[]`:** Name, Title, Hours, Rate, Amount. This is for the cross-check rule.
- **Optional experiment:** `ActivityCode` (`classify`, 28-value enum with `enumDescriptions`) inside `TimeEntries`, to compare against the separate coding step.

Field descriptions should list label aliases ("Professional Services", "Fees", "Time Detail", "Disbursements", "Costs"). Microsoft recommends this explicitly (Best practices, 2026-08-10).

### 3.3 Should CU also do UTBMS coding in the same call?

**Recommendation: no, keep coding as a separate Azure OpenAI structured-output step**, which is the current D-05/FR-22 design.

1. **The code set is per matter and per version** (Litigation vs Bankruptcy vs IP; 1997 vs 2013 activity/expense sets). A CU enum is fixed at analyzer creation, so we'd need one analyzer per code set and version, built and maintained as deployment artefacts. A per-request schema in our own call handles this naturally.
2. **Coding needs context the PDF doesn't contain:** core-record type, code-set version, firm-supplied codes, and potentially OCG task definitions. CU's analyzer description and field descriptions are static. The ≤ 1,024-char description limit also constrains rich per-code guidance.
3. **No Microsoft evidence on 60-way `classify` accuracy or calibration.** The confidence-model claims (AUROC +14%) are generic. NFR-05 asks for ≥ 80% top-1 with calibrated flags, and we'd rather own that evaluation loop.
4. **Training limits:** labeled samples don't apply to `generate` fields, and labeled-sample "distillation" is PREVIEW-only.
5. **Observations** (block billing, vague description) need quoted text and reasons. The extraction already gives us each narrative's span and polygon, so the separate step can cite the extracted line without re-grounding.
6. **Cost and model flexibility:** short per-line classification can run on a cheaper model, batched (e.g. 25–50 lines per call), and isn't tied to CU's supported-model list.

What CU *can* do, and what's worth measuring in FR-03: add `ActivityCode` (28 stable values) and `IsBlockBilled` (boolean, `generate`) as per-line fields, and compare accuracy and confidence against the separate step. The constraint is 300 classify categories in total per analyzer.

### 3.4 Evidence on legal and professional-services invoices

**None found.** Searches across Microsoft Learn, the Foundry blog (incl. 2026-06-02 Build and 2026-08-12 updates) and general web turned up no Microsoft sample, blog or customer story that applies CU or DI to law-firm, LEDES/UTBMS or time-entry invoices. Microsoft's invoice material is AP/procurement-centric (vendors, POs, tax). The only "legal" prebuilt is `prebuilt-contract`. Treat law-firm invoices as unproven on both services. That's what the FR-03 spike is for.

---

## 4. Model successor to gpt-4o (as of 2026-10-14)

### 4.1 Retirement facts

Source: [Model retirement schedule](https://learn.microsoft.com/en-us/azure/foundry/openai/concepts/model-retirement-schedule) (updated 2026-09-23). Third-party posts quoting earlier dates (2026-03-31, 2026-10-01) are **superseded** by this table.

| Model | Version | Lifecycle | Retirement | Replacement |
|---|---|---|---|---|
| gpt-4o | 2024-05-13 | Deprecated | 2026-12-09 | gpt-5.6-sol |
| gpt-4o | 2024-08-06 | Deprecated | **2027-04-14** | **gpt-5.1** |
| gpt-4o | 2024-11-20 | Legacy | **2027-04-14** | **gpt-5.1** |
| gpt-4o-mini | 2024-07-18 | Deprecated | **2027-04-14** | — (none listed) |
| gpt-4.1 / -mini / -nano | 2025-04-14 | **Deprecated** | 2027-04-14 | — |
| gpt-5 / -mini / -nano | 2025-08-07 | GA | 2027-02-09 | — |
| gpt-5.1 | 2025-11-13 | GA | 2027-05-15 | — |
| gpt-5.2 | 2025-12-11 | GA | 2027-06-08 | — |
| gpt-5.4 / -mini / -nano | 2026-03-05 / 03-17 | GA | 2027-09-02 / 09-21 | — |
| gpt-5.5 | 2026-04-24 | GA | 2027-10-26 | — |
| gpt-5.6-sol / -terra / -luna | 2026-07-09 | GA | 2028-01-11 | — |
| text-embedding-3-large | 1 | GA | 2028-02-09 | — |

The "Replacement" column is also "the target for automatic upgrades at retirement" where supported. Check `versionUpgradeOption` on each deployment.

**New since our 2026-07 memo:** the catalogue (updated 2026-10-08) now lists **GPT-6** (`gpt-6-sol`, `gpt-6-luna`, `gpt-6-astra`; 2026-09) and **GPT-6.1** (`gpt-6.1-sol`, 2026-09-29), all with structured outputs and 1.05M context. They aren't yet in the retirement table, Azure prices for them were "in processing" on 2026-10-14, and **CU doesn't support them**. Watch them, but don't adopt them for R1. [Model catalog](https://learn.microsoft.com/en-us/azure/foundry/foundry-models/concepts/models-sold-directly-by-azure) (2026-10-08); [Azure OpenAI pricing](https://azure.microsoft.com/en-us/pricing/details/azure-openai/) (read 2026-10-14)

### 4.2 Capabilities relevant to us

- **Structured outputs are supported on every GPT-5.x, GPT-5.6 and GPT-6 model** listed in the catalogue (2026-10-08).
- **Context:** gpt-5.4/5.5/5.6 have 1.05M context (922K input, 128K output). gpt-5.4-mini/nano have 400K (272K input, 128K output).
- **gpt-5.6 and gpt-6 have separate short-context and long-context price categories** on Standard pay-as-you-go.
- Reasoning models trade latency for depth. Microsoft names "legal or financial document analysis" as a GPT-5 use case and recommends gpt-4.1 for low-latency, high-throughput work. **But gpt-4.1 is now deprecated**, so for fast work use a GPT-5.x mini or nano at low or no reasoning effort instead. [Model choice guide](https://learn.microsoft.com/en-us/azure/foundry/foundry-models/how-to/model-choice-guide) (2026-06-05)
- Sol, Terra and Luna positioning comes **from third-party posts only (unverified)**: Sol targets hard reasoning and agents, Terra everyday work, and **Luna "classification, extraction, and batch jobs"**. Reported OpenAI list prices are Sol $5/$30, Terra $2.50/$15 and Luna $1/$6 per 1M tokens in/out. [knightli.com 2026-07-10](https://knightli.com/en/2026/07/10/gpt-5-6-sol-terra-luna-comparison/). Azure prices weren't readable on 2026-10-14.

### 4.3 Recommendation by workload

| Workload | Primary | Bake-off alternative | Why |
|---|---|---|---|
| (a) Long-document extraction **inside CU** | **gpt-5.2** (GA, retires 2027-06-08; CU's recommended model) | gpt-5.4-mini (cheaper) or gpt-5.5 (longest CU-supported runway, 2027-10-26) | Must be on CU's supported list, which excludes 5.6 and 6. |
| (a′) Long-document extraction **if we stay on Option A** (own LLM call) | gpt-5.4 or gpt-5.5 at low/medium effort | gpt-5.6-terra | 1.05M context and 128K output, which matters for 50+ page time detail. gpt-4o is limited to 16K output. |
| (b) High-volume short classification (UTBMS coding, observations) | **gpt-5.4-mini**, `reasoning_effort` low, structured output with enum-restricted schema | **gpt-5.6-luna** (longest runway, 2028-01-11) | Cheap, fast, GA. Replaces gpt-4o-mini, which has no listed successor. |
| Embeddings (CU labeled samples) | text-embedding-3-large | — | Retires 2028-02-09 |

Deployment type: **DataZone Standard (US)** where data-zone residency matters. Global otherwise.

---

## 5. Caveats and open questions

1. **No law-firm evidence** for CU or DI. Accuracy on dense time-detail tables, especially long narratives that wrap across cells and pages, is unproven. Test it in FR-03.
2. **Very long invoices.** CU caps a request at 300 pages, and the docs don't say how CU handles arrays with hundreds of items whose output nears the model's output limit. Test the largest sample (a big fee application) early. The page-range parameter is the fallback for splitting.
3. **Region.** CU isn't offered in westus2. Our 2026-07 memo recorded the dev Azure OpenAI resource in westus2. Check which region `spaarke-docintel-invoices-dev` is in, and where its model deployments are (cross-region BYOC is supported). Per-customer stamps (ADR-013 Model 1/2) must sit in CU regions or use cross-region model deployments. **Resolved by task 003 (2026-10-15):** `spaarke-openai-dev` is in **eastus** (resource group `spe-infrastructure-westus2`, hence the earlier misreading); the CU resource `spaarke-docintel-invoices-dev` is in **westus** (a CU GA region); only `spaarke-docintel-dev` (FormRecognizer, unused here) is in westus2. See `../003-model-deployments.md`.
4. **Playground versus production.** The playground uses `2026-06-01-preview`. A custom analyzer created on the preview API with labeled data bills at the **advanced** rate. Create production analyzers on `2025-11-01`.
5. **Prices.** Both pricing pages showed placeholders on 2026-10-14, so every $ figure above is illustrative (from the explainer) or from older memos. Verify with the Azure calculator.
6. **Doc inconsistencies:**
   - The CU service-limits page lists every GPT-5 version as `2025-12-11`.
   - The explainer's invoice example says "GPT-5.2" but uses $0.40/$1.60 token rates.
   - The `D(...)` source grammar isn't formally specified.
7. The CU confidence "AUROC +14%" claim is Microsoft-internal and not field-specific. Calibrate thresholds on our own evaluation set (NFR-05).

## 6. Follow-ups for the main session (not done here; read-only)

- **FR-03 spike design.** Run three arms on ≥ 30 invoices: (A) current DI layout + LLM, (B) CU `prebuilt-invoice` as-is, (C) CU custom analyzer (§3.2). Measure line-field accuracy, line recall, grounding hit rate, confidence calibration, latency (`x-ms-diagnostics`) and `usage` tokens. Record the decision in `notes/`.
- **BFF placement (CLAUDE.md §10/§11).** `Azure.AI.ContentUnderstanding` 1.1.0 is a new package, so it needs the publish-size delta and a CVE check. The alternative is a thin REST client over the existing `HttpClient` stack. Placement must go through the AI `PublicContracts` facade (ADR-013), behind `IInvoiceAi`.
- **Auth.** Use MI with the Content Understanding Reader role on the CU resource. The CU resource's own access to its model deployments needs checking in Studio "connected resource" setup. Run adr-check against ADR-028 (Entra-first; the DI key is out of A4 scope today).
- **Analyzer-as-code.** Keep the analyzer JSON in source control, provision it with `PUT ...?allowReplace=true` per customer stamp, and version the analyzer ID by schema version (e.g. `spaarke.ebilling.invoice.v1`).
- **Model migration.** Plan gpt-4o and gpt-4o-mini retirements (2027-04-14) across *all* playbooks in `.claude/catalogs/scope-model-index.json`, not just Finance.

---

## 9. Preview API (2026-06-01-preview) evaluation — 2026-10-14

> Researched 2026-10-14 by the `researcher` sub-agent. This section extends §1.1, §1.6 and §1.7 and doesn't repeat them. Owner decisions taken as given: CU custom analyzer (seeded from `prebuilt-invoice`, plus `TimeEntries`, `Expenses` and `TimekeeperSummary`), confidence and grounding on, UTBMS coding in a separate AOAI step, `Azure.AI.ContentUnderstanding`, gpt-5.2 for CU and gpt-5.4-mini for coding, go-live around January 2027.
> **UNVERIFIED** marks anything not stated by a Microsoft source.

### 9.0 Verdict

**Option (a). Build on GA `2025-11-01` with the GA SDK 1.1.0. Add one preview arm to the FR-03 spike. Adopt preview features only once they reach GA.**

For our workload, no preview feature solves a problem that GA leaves unsolved:
- Splitting bundled PDFs is already GA (`contentCategories`, `enableSegment`, `analyzerId` routing).
- Labeled-sample training is already GA.
- Per-subfield confidence and grounding are already GA.

What the preview adds is incremental: segment confidence, distilled training (fewer tokens, no retained samples), and a claimed accuracy gain of up to 3.5%. The risks it brings are concrete, as detailed in the subsections below:
- It has no SLA, and Microsoft says it isn't for production.
- The preview SDK is beta only.
- There's no published GA date.
- The last preview's headline feature (pro mode) never shipped as GA.

### 9.1 Exact diff: `2026-06-01-preview` vs `2025-11-01` (REST reference)

Both REST references were last updated 2026-02-26 (GA) and 2026-08-10 (preview), and were read on 2026-10-14. Sources: [operation group GA](https://learn.microsoft.com/en-us/rest/api/contentunderstanding/content-analyzers?view=rest-contentunderstanding-2025-11-01) / [preview](https://learn.microsoft.com/en-us/rest/api/contentunderstanding/content-analyzers?view=rest-contentunderstanding-2026-06-01-preview); Create Or Replace [GA](https://learn.microsoft.com/en-us/rest/api/contentunderstanding/content-analyzers/create-or-replace?view=rest-contentunderstanding-2025-11-01) / [preview](https://learn.microsoft.com/en-us/rest/api/contentunderstanding/content-analyzers/create-or-replace?view=rest-contentunderstanding-2026-06-01-preview); Analyze [GA](https://learn.microsoft.com/en-us/rest/api/contentunderstanding/content-analyzers/analyze?view=rest-contentunderstanding-2025-11-01) / [preview](https://learn.microsoft.com/en-us/rest/api/contentunderstanding/content-analyzers/analyze?view=rest-contentunderstanding-2026-06-01-preview).

| Area | New in preview only |
|---|---|
| Operations | `Analyze Inline` and `Analyze Binary Inline`, which return HTTP 200 with the result and need no LRO polling. All other operations are identical. |
| `ContentAnalyzerConfig` | `workflow` (`default` or `agentic`)<br>`allowInPageSegments`<br>`chunkingStrategy` (`semantic`, `maxTokens`)<br>`allowInputTruncation`, also available as a per-request query parameter |
| `ContentFieldDefinition`, `ContentFieldSchema`, `LabeledDataKnowledgeSource`, `ContentCategoryDefinition` | **No change.** The field-schema language is identical. |
| Result `DocumentContent` | `signatures[]`, `metadata{}`, `chunks[]` |
| Result `DocumentContentSegment` | `confidence` and `source`. **GA segments have neither**; they carry only id, category, page range and span. |
| Result `AnalysisResult` | `infos[]` (diagnostics) |
| `UsageDetails` | `documentPages{Minimal,Basic,Standard}Inline`, `advancedContextualizationTokens` |
| Field result types (`StringField` and the rest) | **No change.** `confidence`, `source` and `spans` are the same in both versions. |

Not visible in the schema:
- **Layout-based classification features** apply automatically on preview calls.
- **Distilled training** is how preview builds an analyzer that has labeled data.
- **Prebuilt tax analyzers** (K-1 family, MN M1) are new on preview.

([Classifier](https://learn.microsoft.com/en-us/azure/ai-services/content-understanding/concepts/classifier), updated 2026-08-10; [What's new](https://learn.microsoft.com/en-us/azure/ai-services/content-understanding/whats-new), updated 2026-09-09)

### 9.2 Feature-by-feature value for law-firm invoices

| Preview feature | What it does | Value for us | Why |
|---|---|---|---|
| **Classification: segment confidence and source** | Each split segment gets a confidence score in [0–1] and a `D(page,…)` polygon. | **Medium** | Bundled PDFs (cover letter + invoice, or several invoices) can already be split on GA, but a bad split can't be scored there. Preview lets a wrong split go to review on the score. On GA we can compensate by cross-checking each segment's header against its totals (sum of lines = FeesTotal, InvoiceNumber present) and by showing segment page ranges in the review UI. |
| **Classification: layout-based features** (automatic) | Uses section markers, table headers and figure descriptions as classification signals. | **Low–Medium** | It helps when the classification signal is sparse or sits mid-page. Cover letters (prose) and invoices (tables, totals) are already very different on text alone. Worth measuring in the spike; there's no evidence it's needed. |
| **In-page segmentation** (`allowInPageSegments`, set at analyzer creation only) | Splits a single page into segments when it holds content from more than one document type. | **Low** | It only matters when two documents share a page: a cover letter ending above an invoice header, or a remittance stub. Law-firm invoices almost always start on a new page (an assumption, which the spike corpus should confirm). |
| **Distilled labeled-sample training ("advanced contextualization")** | Preview training distils the labeled samples into the analyzer. The samples aren't retained and aren't retrieved at runtime. Microsoft claims, from internal evaluation, up to +3.5% accuracy and up to 22% fewer LLM tokens. | **Medium** | Labeled-sample training itself is **GA** with the same Studio workflow. Preview changes the mechanism, not whether we get it. Two benefits matter: (1) **privacy** — on GA "the built analyzer retains the labeled training data and uses it at analysis time", which matters if we label one firm's or client's invoices in an analyzer shared across customers; (2) fewer runtime tokens. The accuracy gain is unproven for our documents. **Doesn't apply to `generate` fields.** |
| **Agentic mode** (`workflow: "agentic"`) | Multi-step reasoning, calculation and validation over the document. | **None (negative for extraction)** | It **can't use `extract` fields or labeled samples**, needs about 400K TPM per job and bills at the advanced rate. Docs don't say whether field-level confidence and grounding work in agentic mode (UNVERIFIED). Our reconciliation rules (line sums vs totals, timekeeper summary cross-check) are deterministic and belong in code. |
| **Synchronous Read/Layout** | Inline analyze for `prebuilt-read` and `prebuilt-layout` only, up to 5 pages and 10 MB, with no fields. | **None** | Our extraction is async background work with field schemas, and inline doesn't apply to custom field analyzers. |
| **Signature detection** | `signatures[]` with polygon and role. Detection only, no verification. | **None–Low** | Invoices don't need it. It might flag signed fee-application certifications later. |
| **Document metadata** | PDF author, createdAt, title, language, pageCount. Email adds from, to and subject. There's **no producer/creator-application key**. | **Low** | Marginal provenance and duplicate hints only. We already have file hashes and the SPE metadata. |
| **Semantic chunking** (`chunkingStrategy`) | Structure-aware chunks for RAG. | **None for eBilling** | We don't index invoice text for retrieval. |
| **`allowInputTruncation`** | Truncates over-limit input and returns a partial result with a warning instead of failing. | **None (keep off)** | It would hide missing lines. Hard failure plus our own `range`-based splitting is safer. The default is false. |
| **`infos[]` diagnostics, inline usage counters** | Extra diagnostics. | **Low** | GA already gives `x-ms-diagnostics` and `usage`. |
| **New prebuilt tax analyzers** | Tax forms. | **None** | — |
| **Model support** | Same list as GA. | **None** | See §9.5. |

#### Bundled PDFs: what GA already does

GA `2025-11-01` already supports "classify, segment, and analyze" in one call. You set `contentCategories` (up to 200 categories, name + description ≤ 120 chars) with `enableSegment: true` and an `other` category, and set `analyzerId` on the `invoice` category so it routes to our custom analyzer. `omitContent: true` returns only the routed results. Microsoft's own GA example is "a collection of scanned invoices in a single file". **The GA limit is that "the minimum unit for classification of documents is a single page."** ([Classifier](https://learn.microsoft.com/en-us/azure/ai-services/content-understanding/concepts/classifier), 2026-08-10; [Analyzer reference](https://learn.microsoft.com/en-us/azure/ai-services/content-understanding/concepts/analyzer-reference), 2026-09-24)

So cover letter + invoice and multiple invoices per file are **GA-covered** whenever each document starts on a new page.

Two alternatives the spike should compare:
- **(i) Routed.** One call with `analyzerId` routing.
- **(ii) Two-step.** A split-only call (categories with no `analyzerId`), then one extraction call per segment using GA `inputs[].range` (for example `"3-7"`). This gives us explicit control and one result per invoice.

**UNVERIFIED for both:** whether field `source` page numbers in routed or ranged results refer to the original file's page numbers. The review UI's page jump depends on it, so test it in the spike.

#### Does labeled-sample training on preview improve the GA API too?

- **Documented:** an analyzer created on GA and fetched with the preview API reports `config.workflow = "standard.2025-11-01"` and keeps the standard rate.
- **Documented:** a preview analyzer with labeled data resolves to `advanced.2026-06-01-preview`.
- **Not documented:** whether a preview-built (distilled) analyzer can be *called* with `api-version=2025-11-01`. The docs say preview features "require API version `2026-06-01-preview`". Treat cross-version use of a distilled analyzer as **UNVERIFIED, likely unsupported**.

In practice, labeling effort carries over because the labeled blobs and the Studio project are version-agnostic. A built analyzer doesn't carry over. ([Analyzer reference §workflow](https://learn.microsoft.com/en-us/azure/ai-services/content-understanding/concepts/analyzer-reference), 2026-09-24; [Analyzer improvement](https://learn.microsoft.com/en-us/azure/ai-services/content-understanding/document/analyzer-improvement), 2026-08-10)

#### Billing

The advanced contextualization rate is $3 per 1M tokens (≈ $3 per 1,000 pages), against $1 for standard (explainer illustration; rates are placeholders). It applies **only** to analyzers created on the preview *with labeled data*, to agentic analyzers, and to some prebuilt tax analyzers.

At our volume of 100–200 invoices a month, roughly 1–6K pages, the difference is about **$2–12 per month per environment**, which is immaterial. That's partly offset by GA's in-context retrieval, which adds LLM input tokens on every call. That offset isn't quantified. ([Pricing explainer](https://learn.microsoft.com/en-us/azure/ai-services/content-understanding/pricing-explainer), 2026-10-07)

#### Does any preview feature weaken `estimateSourceAndConfidence` or grounding?

The docs say no for standard and advanced workflows: "In the `2025-11-01` GA and `2026-06-01-preview` APIs, confidence scores and grounding are available for all document field types." (Analyzer improvement, 2026-08-10).

Three exceptions:
- **Agentic mode forbids `extract` fields**, which are the only fields that *require* grounding, and its confidence and grounding behaviour isn't documented.
- **`allowInputTruncation`** can return partial grounding without failing.
- Preview *adds* grounding (`source`) and confidence to segments.

Microsoft's Aug 2026 grounding and confidence-model refresh was described for the **GA** path ([Foundry blog 2026-08-12](https://devblogs.microsoft.com/foundry/azure-content-understanding-updates-august-2026)), so GA isn't the weaker grounding path.

### 9.3 Preview lifecycle, GA likelihood, SLA, data

| Question | Evidence |
|---|---|
| SLA | **None.** Every CU page carries the banner "Previews are provided without a service-level agreement and aren't recommended for production workloads". Overview (2026-09-24): "Use API version `2025-11-01` (GA) for production workloads, and use API version `2026-06-01-preview` to evaluate new capabilities." The CU CLI and the migration guide also say to pin `2025-11-01` for production. |
| Stated retirement policy | **No CU-specific "N days after GA" policy is published.** |
| Precedent: retirement | `2024-12-01-preview` and `2025-05-01-preview` were announced to retire "by July 15, 2026", about 8 months after GA `2025-11-01`. The [agentic mode page](https://learn.microsoft.com/en-us/azure/ai-services/content-understanding/concepts/agentic-mode) (2026-08-10) confirms "the `2025-05-01-preview` API, which is now retired". The July 15 date is from localized Learn copies found by search; I didn't see it on an en-us page today. |
| Precedent: feature survival | **Pro mode** (headline feature of `2025-05-01-preview`) **never went GA**. It was dropped at GA and later replaced by agentic mode in a *new* preview, which "isn't a drop-in replacement". Analyzers also needed manual migration (`baseAnalyzerId`, `models`, re-PUT). |
| Announced GA date or roadmap for the `2026-06-01` features | **None found.** Not in the Aug 2026 Foundry blog, What's new (2026-09-09), the overview or the Baeke digest (2026-08-22). Ignite 2026 is Nov 17–20; nothing ties CU 2.0 to it. |
| Likelihood that features we'd want are GA before Jan 2027 | **Inference only (labeled speculation).** The last cycle went preview at Build (May 2025) and GA at Ignite (Nov 2025, 6 months). A GA at Ignite 2026 fits that pattern but is unannounced. Even if it happens, the GA release may not keep every preview feature unchanged; pro mode is the counterexample. Treat P(GA of segment confidence + distilled training before 2027-01) as **uncertain, < 50%** on current evidence. |
| Data and compliance differences | The only documented one is labeled data: GA retains labeled docs in the analyzer, while preview distils them. Otherwise processing, retention (results ≤ 24 h) and region rules are the same. Preview carries the Azure Preview Supplemental Terms (updated June 2026); generative-AI previews may "process and store Customer Data submitted to the service". **Check the current terms text before sending client invoice data to any preview** (law-firm confidentiality). ([CU data privacy](https://learn.microsoft.com/en-us/azure/foundry/responsible-ai/content-understanding/data-privacy), 2026-08-10; [Preview terms](https://azure.microsoft.com/support/legal/preview-supplemental-terms/)) |

### 9.4 SDK (`Azure.AI.ContentUnderstanding`)

| Version | Date (NuGet / CHANGELOG) | Service versions supported | Notes |
|---|---|---|---|
| 1.0.0 | 2026-02-26/27 | `2025-11-01` | GA |
| **1.1.0** | 2026-04-22 (NuGet), 2026-04-21 (CHANGELOG) | **`2025-11-01` only.** The `ServiceVersion` enum = `V2025_11_01`; any other value throws `NotSupportedException`. | Latest stable. Adds `IConfiguration` client settings and `GetUsage()`. |
| 1.2.0-beta.1 | 2026-04-30 | `2025-11-01` | `ToLlmInput` (preview helper) |
| 1.2.0-beta.2 | 2026-06-11/12 | `2025-11-01` | Bug fixes |
| **1.2.0-beta.3** | 2026-08-11 | **`V2025_11_01` and `V2026_06_01_Preview` (default)** | Adds inline analyze, `AnalyzeOptions` bags, chunks, workflow/agentic, signatures, `AllowInPageSegments`, metadata, `Infos`, `GetUsageDetails()`. `GetUsage()` is obsolete. |
| 1.2.0-beta.4 | unreleased | — | Empty changelog |

Sources: [NuGet](https://www.nuget.org/packages/Azure.AI.ContentUnderstanding) (read 2026-10-14); [CHANGELOG](https://github.com/Azure/azure-sdk-for-net/blob/main/sdk/contentunderstanding/Azure.AI.ContentUnderstanding/CHANGELOG.md); `ContentUnderstandingClientOptions.cs` at tag `Azure.AI.ContentUnderstanding_1.1.0` and at `main` (read 2026-10-14).

**Can GA 1.1.0 call the preview?** Not supported. The version is `internal` and the enum has only GA. A custom `HttpPipelinePolicy` that rewrites `api-version` would technically send the request, but 1.1.0's models don't deserialize preview-only properties such as segment confidence, signatures and metadata. That's a hack, so don't use it.

Calling the preview from .NET means taking a **beta** package into the BFF. That raises a BFF-hygiene and CVE-review question (CLAUDE.md §10) and brings API churn: beta.3 already changed option bags and made `GetUsage` obsolete.

### 9.5 Model support on preview

There's **no difference from GA.** Service limits (2026-09-16) has one table for both versions: gpt-5.5, gpt-5.4, gpt-5.4-mini, gpt-5.2, gpt-5.1, gpt-5, gpt-5-mini, gpt-5-nano, gpt-4.1 family, gpt-4o, gpt-4o-mini.

**No gpt-5.6-\* or gpt-6-\*** appears on either version. The overview says preview "expands and iterates model support aligned with Build 2026 updates", but names no extra model. The Aug 2026 blog also lists "GPT-5.3", which is absent from the service-limits table (doc inconsistency, UNVERIFIED). Our picks (gpt-5.2 for CU, gpt-5.4-mini for coding outside CU) are unaffected.

### 9.6 Analyzer portability and migration cost

- **GA → preview:** documented. A GA analyzer is readable from the preview API (`workflow = standard.2025-11-01`) and keeps the standard rate. Analyzing a GA-created analyzer with the preview api-version is implied but not explicitly stated. **Task 005 (2026-10-15): not true on our resource — a GA-built analyzer is 404 under the preview api-version; see §9.8.**
- **Preview → GA:** **not documented.** A preview analyzer that uses only GA properties is *probably* callable on GA, but that's UNVERIFIED. One using distilled training, `workflow: agentic` or `allowInPageSegments` should be assumed GA-incompatible.
- **The field schema is byte-identical across versions.** The analyzer JSON (fields, descriptions, enums, `knowledgeSources`, `contentCategories`) ports by re-PUT under a new analyzer id. Labeled blobs are reusable.

If we built on preview and the features didn't GA, the migration would cost:
- **Code:** swap the beta SDK for 1.1.x, and remove inline, segment-confidence, signature and metadata handling. Small, about 1–3 days.
- **Analyzers:** re-PUT on GA and rebuild with labeled data, which switches to in-context retrieval. Trivial.
- **Quality:** re-run the full FR-03 accuracy and calibration evaluation, because distilled and retrieved training, and preview and GA classifiers, behave differently and confidence thresholds would need re-calibration. Medium, about 1 week.
- **Schedule:** a forced migration before preview retirement, which has been about 8 months after a successor GA.

### 9.7 Recommendation (decisive reasons and risk if wrong)

**Choose (a), with one hybrid element in tooling only:**
1. **Production path: GA `2025-11-01` plus SDK 1.1.0.** Production analyzers are created by our analyzer-as-code `PUT` on `2025-11-01`, never from the Studio playground, which defaults to the preview.
2. **FR-03 spike: add arm C′.** Use the same custom analyzer schema, created on `2026-06-01-preview` with the same labeled samples (via REST or CU CLI, not the BFF), and run the bundled-PDF corpus through `contentCategories` + `enableSegment`. Measure:
   - Line accuracy and calibration, C′ vs C. Is the up-to-3.5% claim real for time entries?
   - Split errors on bundled files, and whether segment confidence would have caught them.
   - Tokens per page.
   - A 15-minute portability probe: call a preview-built analyzer with `api-version=2025-11-01`.
3. **Labeling (the hybrid element):** label in Studio. The labeled blob container is version-agnostic, so the work carries over whichever version we end on.
4. **Adoption trigger:** re-evaluate when Microsoft GAs a version that contains segment confidence and distilled training (watch Ignite 2026, Nov 17–20, and What's new). Adopt it then via re-PUT and the evaluation re-run.

**Decisive reasons:**
- Every capability the requirements need is GA: per-subfield confidence and grounding (FR-04), splitting bundled PDFs, labeled-sample improvement, and our models.
- Microsoft explicitly says the preview isn't for production and has no SLA, with go-live about 11 weeks away.
- The preview .NET path is beta-only.
- The last preview's headline feature never shipped as GA.

**Risk if wrong:** if arm C′ shows the preview materially beats GA on line accuracy (for example, GA under the ≥ 90% line target and preview over it) and no GA follows by January:
- We'd go live below target, or take an **ADR-style project exception** (CLAUDE.md §6.5 path A) to run that one analyzer on preview with GA fallback, owner sign-off and a retirement-watch item.
- The exposure is bounded by the low migration cost in §9.6.

The opposite error, building on preview and then having it changed or retired, would cost the same migration plus a production incident risk with no SLA. That makes it the worse failure mode.

### 9.8 Open items (UNVERIFIED, test in FR-03)

1. Can a preview-built analyzer (plain, or with distilled labeled data) be analyzed with `api-version=2025-11-01`?
2. Do field `source` page numbers in routed (`analyzerId`) and ranged (`inputs[].range`) results use original-file page numbers?
3. What is confidence and grounding behaviour under agentic mode? Academic for us; we won't use it.
4. Is the July 15, 2026 retirement date for `2024-12-01-preview`/`2025-05-01-preview` correct? Confirm on an en-us page.
5. Does the current Azure Preview Supplemental Terms text for generative-AI previews permit client invoice data under our customer contracts?

> **2026-10-15 — resolved by task 005** (`notes/extraction-spike.md` §7–§8):
> - Item 1: **No.** A preview-built analyzer returns 404 `ModelNotFound` under `2025-11-01` (GET and analyze).
>   - The reverse also fails: a GA-built analyzer is 404 under `2026-06-01-preview`. This corrects the GA → preview statement in §9.6.
>   - Analyzers are scoped to the api-version that created them, so moving versions means re-PUT under the new version plus re-evaluation.
> - Item 2: routed (`contentCategories` + `enableSegment`) results use **original-file** page numbers in field `source`, on both GA and preview. Ranged input was not tested.
