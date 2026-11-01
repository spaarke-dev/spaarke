# eBilling R1 research: tax, VAT and e-invoicing (buyer side)

Researched 2026-10-14 (web search and fetch on that date). Audience: Spaarke eBilling design.

## How to read the confidence tags

- **[P]** Primary source (EU Commission, IRS, Belgian FPS Finance, LEDES.org, EN 16931 page on the Commission site) fetched and read.
- **[S]** Secondary source (adviser, vendor or news site). Treat as a lead and verify before it drives a contractual or compliance commitment.
- **[U]** Unverified. The search did not confirm it. It is flagged so nobody builds on it.

Several official pages could not be fetched in this session: EUR-Lex returned empty, impots.gouv.fr was down for maintenance, vatcalc.com returned 403, and gov.pl was not the KSeF page. The Directive article texts (Art. 44, 196, 226) are therefore not quoted from EUR-Lex. They are cited from memory and flagged [U]. Re-verify them against EUR-Lex before use.

---

## 1. Executive summary

1. **R1 can stay USD-only and tax-light**, provided the data model carries tax, currency and identifiers from day one. The expensive restructuring risks are:
   - a single `tax_amount` on the invoice
   - one currency field
   - a single "invoice number" field
   - no place for an e-invoice document or a tax-authority reference
2. **In mandated countries the legally valid invoice is the structured e-invoice** (XML, or hybrid PDF with embedded XML). A PDF uploaded to a portal is not the tax invoice. Examples:
   - Belgium: Peppol structured e-invoice, mandatory from 1 January 2026 [P, S].
   - Germany: a PDF is not an e-invoice; XRechnung or ZUGFeRD is required [S].
   - France: the PDF does not qualify; Factur-X, UBL or CII is required [S].
   - Poland: the KSeF structured invoice (FA) is the invoice [S].
3. **Most mandates are domestic.** They bind a supplier established in that country invoicing a buyer established there. A US-headquartered corporate department that receives an invoice from a foreign law firm for work done for a foreign subsidiary is the classic exposure. The foreign subsidiary (the legal entity that is the buyer) is the one with the local receive obligation, not the US parent. The product must therefore model **buyer legal entity** and **supplier establishment country** on every invoice.
4. **ViDA** makes EN 16931 structured e-invoicing mandatory for intra-EU B2B transactions with transaction-level digital reporting from **1 July 2030** [P, with dates cross-checked in S]. Legal services to a business in another EU state are in scope, because they are B2B services under the reverse-charge place-of-supply rule.
5. **LEDES XML 2.x already has the tax scaffolding** (tax segments, tax-on-tax, tax point date, tax invoice flag, tax currency). Mitratech Collaborati already stores tax-authority reference fields. This is the interchange baseline Spaarke must ingest [P].
6. **Recommended integration posture:** in R1 Spaarke is not the legal tax invoice recipient. It is a **business-validation and approval layer** that holds the invoice data. It must carry references to the legal invoice (tax invoice number, e-invoice ID, clearance ID, Peppol message ID) and reconcile with whatever arrives in the buyer's ERP/AP or Peppol access point. Acting as the receiving platform (a Peppol access point or a national-platform connection) should be a later, partner-delivered option, not built in-house.

---

## 2. United States

### 2.1 Sales and use tax on legal services

- Most US states do not tax legal services. Secondary sources name **Hawaii, New Mexico and South Dakota** as the states that do [S: https://www.claimsjournal.com/news/national/2016/10/20/274366.htm, https://www.cosmolex.com/blog/does-my-law-firm-have-to-pay-sales-tax/]. Those sources are old (2016 for the Claims Journal piece), so confirm each state's current position with the state Department of Revenue [U for 2026 currency].
  - South Dakota: 4% state rate on the sale of services including legal, under S.D.C.L. section 10-45-4 [S].
  - New Mexico: Gross Receipts Tax. Whether it applies depends on where the work is performed, not just where the client is [S].
  - Hawaii: general excise tax. Secondary sources say it applies to legal services; detail not verified here [U].
- **Washington** did not make legal services taxable. ESSB 5814 (effective 1 October 2025) extended retail sales tax to advertising, IT services, custom software, security services, temporary staffing and similar. Traditional legal services remain outside the retail sales tax base [S: https://www.withum.com/?p=130622, https://www.gtlaw.com/en/insights/2025/5/washington-state-expands-sales-tax-increases-b-and-o-capital-gains-taxes]. Washington B&O tax is a gross-receipts tax on the supplier (the law firm), not a buyer-facing line item, though a supplier may pass it through informally. Design consequence: some law-firm invoices will bundle taxable and non-taxable services, so tax must be per line, not per invoice [S, Withum].
- Rule of thumb for the design: a US law firm may bill sales tax to the buyer only in a few states. Tax shows up as a rare line or invoice-level amount, but the model must hold it. The jurisdiction (state, locality) matters because the buyer's AP team codes and reports it.

### 2.2 Use tax self-assessment by the buyer

- A buyer self-assesses use tax when a taxable service was bought without tax being charged and the service is taxable in the buyer's state (the buyer's use of the service). This applies mainly where the buyer's state taxes the service category and the supplier did not collect. For legal services this is a narrow case (the three states above) [S: https://www.avalara.com/us/en/blog/when-should-i-collect-sales-tax-on-my-services.html]. I did not find a primary state-DOR source in this session, so treat the general rule as [U] until verified against a specific DOR.
- Design consequence: the invoice-check engine needs a hook for "tax expected but not charged" and a "use tax accrual" field and flag on the approved invoice, which the ERP export carries. R1 does not need to compute it. R1 needs to have a place to hold the flag.

### 2.3 Form 1099 reporting for attorney payments

- **Attorneys' fees are reported on Form 1099-NEC box 1a at $2,000 or more** (a threshold change; the IRS instructions say the minimum threshold for certain information returns rose to $2,000 for tax years beginning after 2025) [P: https://www.irs.gov/instructions/i1099mec]. For years before 2026 the threshold was $600.
- **Gross proceeds paid to an attorney** (for example settlements) of $600 or more go on **Form 1099-MISC box 10** when not reportable in 1099-NEC box 1a [P, same URL]. The instructions page did not state whether the $2,000 change applies to box 10, so check this [U].
- **The corporate exemption does not apply to attorneys.** Fees paid to a law firm incorporated as a corporation (or a PC) are still reportable. This is the key point for the buyer: do not exempt a vendor from 1099 reporting because it is a corporation [P, same URL].
- **TIN:** the payer obtains the attorney's TIN, usually through **Form W-9**. The attorney must give the TIN promptly; if it does not, the payer must backup withhold [P, same URL].
- **Backup withholding is a flat 24%.** It applies when the payee does not give a TIN in the required manner, when the IRS notifies the payer that the TIN is incorrect, and in the underreporting case [P: https://www.irs.gov/taxtopics/tc307].
- **Data the buyer must capture per vendor (law firm):** legal name, business name, federal tax classification (W-9 line 3), TIN (EIN or SSN) and TIN type, address, W-9 certification date, backup-withholding status, TIN-match result and date (Pub. 1281 process), and an indicator for payments to non-US persons (Forms W-8 and 1042-S instead of 1099). That last item is outside R1 but affects the vendor record shape [U for the W-8 and 1042-S detail, which I did not verify].
- **Data per payment:** payment date (1099 is on a paid basis, not an invoiced basis), amount, and whether a portion is a settlement (gross proceeds, box 10) rather than fees (box 1a). So the model should allow payment-level data from the AP system, and the invoice should carry a **payment-category** flag (fees vs gross proceeds vs reimbursed expenses). Treatment of reimbursed expenses on a law firm invoice should be confirmed with the buyer's tax team [U].

---

## 3. VAT, GST and withholding on cross-border legal services

### 3.1 EU VAT Directive 2006/112/EC (cited from knowledge; EUR-Lex was not retrievable) [U]

- **Place of supply, B2B (Art. 44):** services to a taxable person acting as such are supplied where the customer is established. So a UK or US law firm advising an Irish company is outside Irish VAT at the supplier's end, and the Irish customer accounts for VAT.
- **Reverse charge (Art. 196):** the customer accounts for VAT when the supplier is not established in the customer's member state. The supplier invoices without VAT and states the reverse charge. The buyer reports both output and input VAT (so net nil if fully recoverable).
- **Invoice content (Art. 226):** date; sequential number; supplier and customer VAT identification numbers (the customer's where the customer is liable under reverse charge); full names and addresses; description of the services; the taxable amount, rate and VAT amount, or the reference to exemption or reverse charge; and, per Art. 226(11a), the mention "Reverse charge" where it applies. Verify the numbering against the consolidated text.
- The Commission's ViDA page [P] confirms the direction of travel but does not itself quote these articles.
- **Practical effect for Spaarke:** a cross-border legal invoice usually has **no VAT amount** but must carry a reverse-charge indicator and both VAT numbers. The check engine should treat "reverse charge, VAT = 0" as valid when the supplier and buyer are in different states. A domestic invoice with 0% VAT should raise a warning.
- **VAT ID validation:** the EU VIES service validates EU VAT numbers. I did not research VIES in this session [U].

### 3.2 United Kingdom

- **B2B services from a non-UK supplier to a UK business customer:** place of supply is the UK, so the customer accounts under reverse charge (VAT Notice 741A) [S: https://www.accaglobal.com/uk/en/technical-activities/uk-tech/in-practice/2022/september/vat-on-supply-of-services-to-customers-outside-uk.html]. The secondary source I found quotes gov.uk guidance but the gov.uk page itself was not fetched [S].
- **Making Tax Digital (MTD) for VAT** governs how VAT-registered businesses keep digital records and file returns through compatible software. It is a filing regime, not an invoice-delivery regime. I did not retrieve a primary MTD source in this session [U]. A secondary source makes the same distinction: MTD governs what the business does with VAT data; the e-invoicing mandate governs how invoices reach the business [S: https://www.spendesk.com/blog/e-invoicing-uk/].
- **UK e-invoicing mandate:** the government's November 2025 consultation response confirmed mandatory e-invoicing for UK VAT invoices (B2B and B2G) from **April 2029**, using a decentralised four-corner model, not central clearance, with Peppol described as the core interoperability network [S: https://www.fiscal-requirements.com/news/4902-uk-mandatory-e-invoicing-from-2029-main-points-from-consultation-response, https://www.sharedserviceslink.com/article/uk-b2b-e-invoicing-mandate-hmrc-consultation-response; official page: https://www.gov.uk/government/consultations/promoting-electronic-invoicing-across-uk-businesses-and-the-public-sector/outcome/promoting-electronic-invoicing-across-uk-businesses-and-the-public-sector-consultation-response (not fetched)]. Real-time reporting is deferred, not dropped [S]. Roadmap, thresholds and exemptions were not confirmed [U].

### 3.3 Canada (GST/HST/QST)

- I could not fetch the CRA page (404). Not verified [U].
- General position from knowledge, to verify: a Canadian business that buys services from a non-resident supplier who does not charge GST/HST must self-assess GST/HST on imported taxable services unless the purchase is for use exclusively in commercial activities. Quebec has a separate QST self-assessment. LEDES documentation lists Canadian tax types "GST", "HST", "PST" and "TVQ" as examples [S: https://success.mitratech.com/Collaborati/Frequently_Asked_Questions/LEDES_Files/faq_ledes_xml_21].
- Design consequence: a Canadian invoice may carry two or three taxes (GST + PST, or HST, or GST + QST). That is the tiered or multiple-tax case the tax-line model must handle.

### 3.4 Australia (GST)

- ATO guidance on reverse charge for offshore purchases applies in limited cases: the buyer is registered or required to be, the acquisition is partly private or relates to input-taxed supplies [S: https://www.ato.gov.au/businesses-and-organisations/international-tax-for-business/gst-for-non-resident-businesses/gst-cross-border-transactions-between-businesses; Allens commentary https://www.allens.com.au/pubs/tax/fotax14may15.htm]. For a fully taxable business a non-resident legal services supply is generally not connected with Australia and no GST arises [S, inferred; not stated for legal services specifically].
- Australia's invoice network uses Peppol PINT A-NZ [S: appears in search results, not studied].

### 3.5 Withholding taxes on legal fees (illustrative, not exhaustive)

- **India:**
  - GST: services by an individual advocate or a firm of advocates to a business entity in the taxable territory are under reverse charge (Notification 13/2017-Central Tax (Rate), Serial No. 2) [S: https://cbic-gst.gov.in/pdf/press-release/legal-services-15072017.pdf (press release; search result, not fetched), https://www.scconline.com/blog/post/2017/07/16/legal-services-of-advocates-firm-of-advocates-liable-to-gst-under-reverse-charge/].
  - TDS: a forum thread says 10% TDS under section 194J on advocate fees [S, weak: https://www.taxtmi.com/forum/issue?id=106019]. Section 195 (non-resident payees) was not verified [U].
- **Latin America:** PwC tax summaries show non-resident withholding on service fees: Brazil generally 15%, up to 25% in some cases and for tax havens; Colombia, Argentina, Chile and Mexico vary, treaties often reduce them [S: https://taxsummaries.pwc.com/brazil/corporate/withholding-taxes, https://taxsummaries.pwc.com/colombia/corporate/withholding-taxes, https://taxsummaries.pwc.com/argentina/corporate/withholding-taxes, https://taxsummaries.pwc.com/chile/corporate/withholding-taxes]. None of these pages addressed legal fees specifically, so the classification of legal fees is [U]. Do not hard-code rates.
- **Japan:** the LEDES Oversight Committee names Japanese withholding tax as the driver for tiered tax support in LEDES XML 2.2 [P: https://ledes.org/ledes-xml-ebilling-ver-2-2/].
- **Design consequence:** withholding is a deduction from payment, not a charge on the invoice. It changes **net payable** and generates a certificate or remittance obligation for the buyer. The model needs a withholding line type that reduces payable and records the tax authority beneficiary, separate from VAT.

---

## 4. EU e-invoicing

### 4.1 ViDA [P: https://taxation-customs.ec.europa.eu/taxation/vat/vat-digital-age-vida_en]

- Adopted 11 March 2025, published in the Official Journal 25 March 2025, in force 14 April 2025. Legal acts: Directive (EU) 2025/516, Regulation (EU) 2025/517, Implementing Regulation (EU) 2025/518.
- On entry into force (14 April 2025) member states may introduce mandatory domestic e-invoicing under certain conditions.
- **1 July 2028:** platform deemed-supplier rules start (can be delayed to 1 January 2030) and Single VAT Registration elements begin.
- **1 July 2030:** Digital Reporting Requirements (DRR) begin, mainly for cross-border B2B supplies.
- **1 January 2035:** deadline for member states with domestic real-time reporting to align with the EU system.
- Secondary sources add: intra-EU B2B invoices must be structured e-invoices complying with EN 16931; issuance within 10 days of the chargeable event; recapitulative statements (EC Sales Lists) replaced [S: https://www.pwc.com/mt/en/publications/vat/digital-reporting-and-e-invoicing-in-the-eu.html, https://digitalpolicyalert.org/change/4990]. The Commission page does not state EN 16931, so confirm in the Directive text [U].
- **Relevance to legal services:** a legal service B2B across EU states is a reverse-charge supply and in scope of the intra-EU DRR from 2030.

### 4.2 EN 16931, syntaxes and Peppol

- EN 16931-1 (semantic model) and -2 (syntax bindings) are free under an agreement between the Commission and CEN. **A 2026 version of EN 16931-1 was published in May 2026; the 2017 version is formally withdrawn but remains compliant during a migration period whose plans are still being developed** [P: https://ec.europa.eu/digital-building-blocks/sites/spaces/DIGITAL/pages/467108971/Country+overview].
- Permitted syntaxes: UBL 2.1 and UN/CEFACT CII [S: https://accp.econnect.eu/en/docs/learn/document-formats/formats/bis-billing-v3].
- **Peppol BIS Billing 3.0** is a CIUS (Core Invoice Usage Specification) of EN 16931 in UBL (and CII), carrying Peppol validation rules. One vendor lists version 3.0.20 (November 2025, hotfix January 2026), mandatory 23 February 2026 [S, same URL; check OpenPeppol].
- Peppol is a four-corner network: sender, sender's access point (C2), SMP lookup, receiver's access point (C3), receiver. Participants are identified by scheme-prefixed IDs. Example given: `9930:DE123456789`, where 9930 is the scheme code for German VAT [S: https://www.cleartax.com/de/en/peppol-bis-3-0-format-germany]. A document type ID identifies UBL Invoice, UBL Credit Note or CII [S, econnect].
- Peppol message IDs and the SBDH identifiers were not researched here [U].
- Belgium's FAQ confirms an Invoice Response with status "RE" lets a recipient reject an invoice over Peppol [P: https://einvoice.belgium.be/en/FAQ/specific-questions-about-e-invoicing].

### 4.3 National mandates, status at 2026-10-14

| Country | Model | Receive | Issue | Confidence |
|---|---|---|---|---|
| Germany | Interoperability, post-audit, no clearance | All domestic B2B businesses since 1 Jan 2025 | Turnover over EUR 800k in prior year: 1 Jan 2027. All: 1 Jan 2028 | S (IHK, vatcalc, fiscal-requirements) |
| France | Decentralised via accredited platforms (PA, formerly PDP) plus public portal (PPF); e-reporting | All businesses established in France: 1 Sep 2026 | Large and mid-size (ETI): 1 Sep 2026. SMEs and micro: 1 Sep 2027 | S, no official page fetched |
| Belgium | Peppol-based, post-audit | 1 Jan 2026 (fines for lacking capability) | 1 Jan 2026 | P |
| Poland | Central clearance (KSeF) | Phased with issuing | Over PLN 200m: 1 Feb 2026. Others: 1 Apr 2026. Micro: 1 Jan 2027 | S (EY) |
| Italy | Central clearance (SDI), since 2019 | Via SDI | Via SDI | S, thin |
| Spain | Verifactu (billing software integrity) plus B2B law | See below | See below | S, weak |
| Croatia | Fiscalization 2.0, Peppol delivery plus tax reporting | 1 Jan 2026 | 1 Jan 2026 | S |
| UK | Four-corner, Peppol | April 2029 | April 2029 | S |

Details and caveats per country:

- **Germany [S].**
  - The BMF letter of 15 October 2025 clarified format and validation: an invoice that does not follow an approved syntax or lacks readable mandatory VAT fields is treated as an ordinary file, not a valid e-invoice [S: https://www.fiscal-requirements.com/news/4566-germany-updated-bmf-guidance-on-2025-2028-b2b-e-invoicing-rollout].
  - Scope is domestic B2B. Cross-border invoices and B2C are outside the mandate [S: https://vatit.com/e-invoicing-guide/germany/].
  - Transition: EDI invoices in non-e-invoice formats are allowed until 31 December 2027 [S: PwC newsletter, https://newsletter.pwc.de/umsatzsteuer/nl0029-2024-10-29/bmf-e-rechnung].
  - A German company receiving a foreign law firm's PDF is likely outside the mandate because the supplier is not domestic, but the sources are unclear on a foreign supplier with a German permanent establishment [U]. Check the BMF letter text.
  - Accepted formats: XRechnung and ZUGFeRD [S: https://www.e-invoice.app/countries/DE].
- **France [S].**
  - Reception from 1 September 2026 for all businesses established in France. Issuing for large enterprises and ETI from 1 September 2026; SMEs and micro-businesses from 1 September 2027. A late-August 2026 adviser article states no further postponement [S: https://dext.com/fr/ressources/blog-actualite-comptable/single/demarrage-facturation-electronique-recommandations-dg-fi-p]. A separate source mentions a possible delay to 1 December 2026 [S: https://dev.to/zerolooplabs/frances-e-invoicing-mandate-lands-1-september-2026-the-developers-playbook-36eb]; the later adviser source says the date held. The date is therefore **probably in force as of today but not confirmed from an official page** [U].
  - Formats: Factur-X, UBL, CII. Plain PDF does not qualify [S: https://thepaypers.com/regulations/news/frances-e-invoicing-mandate-takes-effect-on-1-september].
  - Penalties reported: EUR 50 per invoice, up to EUR 15,000 per year [S: https://www.selexium.com/actualites/facturation-electronique-votre-entreprise-sera-t-elle-prete-au-1er-septembre-2026/].
  - Foreign suppliers' B2B invoices to French buyers fall under **e-reporting**, not e-invoicing. I did not verify this and it matters for cross-border legal services [U].
- **Belgium [P, S].**
  - Structured electronic invoices are mandatory from 1 January 2026 for B2B between Belgian VAT-registered businesses. PDFs are not a valid SEI, but a PDF credit note is still allowed if the customer accepts it (and the original was not an SEI) [P: https://einvoice.belgium.be/en/FAQ/specific-questions-about-e-invoicing].
  - Fines under the Royal Decree of 8 July 2025: EUR 1,500, EUR 3,000, EUR 5,000 for lacking the technical means to issue and receive [P, same URL]. Advisers report a grace period for Q1 2026 and enforcement from 1 April 2026 [S: https://www.fiscal-requirements.com/news/4211-belgium-e-invoicing-new-penalties-for-non-compliance-effective-2026]. An adviser claim that fines scale to 60-100% of VAT is unverified [U].
  - Foreign-established suppliers are excluded; the FAQ says a Belgian representative is bound under its own VAT number [P].
  - Without a valid SEI, input VAT deduction can be at risk unless a substance-over-form exception applies [P].
  - Belgian law firms raised professional secrecy concerns about Peppol [S: https://financialservices.norriq.com/en-be/blogs/professional-secrecy-and-peppol-how-law-firms-can-prepare-for-2026]. Relevant to what legal metadata an invoice may expose.
- **Poland KSeF [S].**
  - The VAT Act amendment was signed 27 August 2025 [S: https://www.ey.com/en_gl/technical/tax-alerts/poland-signs-into-law-mandatory-national-e-invoicing-system].
  - A grace period without penalties runs to 31 December 2026 [S: https://avask.com/blog/poland-e-invoicing-update-vat-compliance/].
  - I found no post-launch reporting [U]. Clearance model: a structured invoice is assigned a KSeF number on acceptance; the KSeF ID is also required in payment references from 2027 per one source [S: https://www.mddp.pl/poland-sets-out-plans-for-phased-rollout-of-mandatory-e-invoicing-from-2026/; that page has an outdated headline, so confirm the date].
  - Foreign suppliers without a Polish establishment are not required to issue through KSeF [U]. Verify.
- **Italy SDI [S, thin].**
  - Clearance model through SDI, FatturaPA XML; no PDFs; 10-year retention [S: https://www.b2brouter.net/fr/international/italy/, https://peppol.agid.gov.it/en/insights/electronic-invoicing/].
  - For services bought from non-residents the Italian buyer issues a self-billed document (TD17 for services) through SDI [S: https://ecosire.com/blog/odoo-italy-localization-guide-2026, a vendor blog]. Whether the esterometro ended in 2022 or 2025 conflicts between sources [U]. Deadline and TD codes need Agenzia delle Entrate confirmation.
  - Consequence: for an Italian buyer, a foreign law firm's PDF triggers a buyer-side obligation to report via SDI, so the buyer's ERP must receive the data.
- **Spain [S, weak].**
  - Verifactu is delayed to 1 January 2027 for corporate taxpayers and July 2027 for the self-employed; SII reporters are exempt [S: https://www.vatcalc.com/spain/spain-verifactu-delay-till-jan-2027-for-certified-e-invoicing/ (search result)].
  - The B2B e-invoicing mandate: a LinkedIn-sourced claim says Royal Decree 238/2026 published 31 March 2026, with turnover over EUR 8m by 1 October 2027 and all others by 1 October 2028 [U, low-authority]. Verify with BOE and AEAT.
- **Croatia [S].**
  - Fiscalization 2.0 from 1 January 2026 for VAT-registered businesses; non-VAT-registered and public bodies from 1 January 2027; Peppol delivery plus real-time reporting to the tax authority [S: https://www.cleartax.com/hr/en/e-invoicing-croatia, https://www.avalara.com/us/en/vatlive/country-guides/europe/croatia/croatian-e-invoicing.html]. Reporting deadlines for received invoices were not confirmed [U].
- **Other countries not researched:** Greece, Hungary, Romania, Bulgaria, Slovakia, Latvia, Lithuania, Portugal and others. Do not assume they are covered.

### 4.4 Clearance versus post-audit

- **Clearance (continuous transaction control):** the tax authority validates and issues an ID before or as the invoice is delivered. Poland (KSeF) and Italy (SDI) are the examples. The invoice is not valid until accepted. Croatia has a hybrid with validation and reporting [S].
- **Post-audit / interoperability:** Germany, Belgium and UK (planned). Invoices flow between parties; the authority audits afterwards. Belgium and the UK use Peppol.
- **Decentralised with reporting:** France (PA platforms plus e-reporting to the tax authority via the PPF concentrator).
- **Product consequence:** in clearance countries there is a status lifecycle (submitted, accepted, rejected, with ID). A buyer should not pay or post an invoice that is not cleared. The model needs a "tax-validation status" independent of the business-approval status.

---

## 5. What this means for a legal eBilling product in front of AP

### 5.1 Is the portal PDF the legal invoice?

- **In mandated jurisdictions, no.** The legal tax invoice is the structured document delivered through the national platform or Peppol to the buyer (in practice to the buyer's ERP, AP automation, or a certified provider) [P: Belgium FAQ; S: Germany BMF, France, Italy sources above].
- **When the mandate does not apply** (cross-border supplier to buyer, US buyer, R1), the legal invoice is whatever the supplier issued (PDF) plus the buyer's own records.
- The legal e-billing portal workflow (firm uploads LEDES or PDF; client reviews against billing guidelines; approver approves; export to AP) is a **commercial** invoice process. In mandated countries it runs **alongside** the tax e-invoice process. The two documents must correspond, and a mismatch between them is a first-class exception.

### 5.2 How the legal e-billing vendors handle it (evidence is thin)

Evidence below is from vendor documentation reachable in this session. I found no public detail on Brightflag, LawVu, Onit or CounselLink. **I cannot claim anything about them** [U]. I also found no public statement that any legal e-billing vendor operates as a Peppol access point or an SDI/KSeF connector.

- **Mitratech Collaborati / TeamConnect CSM [S, vendor docs]:**
  - Accepts LEDES 1998B, 1998BI, 2000 and XML 2.0/2.1, with line-item non-US taxes only in 1998BI V2 and XML 2.0/2.1 [https://success.mitratech.com/Collaborati/Frequently_Asked_Questions/LEDES_Files/faq_ledesbiv2_specs].
  - Collaborati 5.2 added a **Tax Authority section**: Tax Jurisdiction, Tax Locality, **Tax Authority Ref ID, Alternative Invoice ID, Tax Authority Invoice Date**, shown when the invoice or office currency is not USD [https://success.mitratech.com/Collaborati/collaborati_release_notes/Collaborati_Release_Notes/Collaborati_5.2_Release_Notes]. This is the closest public example of the identifier set Spaarke needs.
  - Manual invoices have a "VAT & Taxes" tab; users pick country and rate from a reference list and assign a tax category.
  - CSM maps Client Tax ID, Invoice Tax Total, Invoice Reported Tax Total and Invoice Net Total [https://success.mitratech.com/TeamConnect/TeamConnect_Module_Guides/Collaborati_Spend_Management_-_CSM/CSM_Settings/14_Handling_Non-U.S._Tax_and_International_Invoicing_Fields].
- **Wolters Kluwer ELM Solutions (Passport):** a "Global E-Billing Tax Compliance Module" extends Passport's built-in VAT support for European jurisdictions [S: https://assets.contenthub.wolterskluwer.com/api/public/content/9a59703ea8a945e18a8827f492fba266; the PDF was binary and I could not read it, so the claim comes from the search summary only, treat as [U]]. No evidence of Peppol or clearance support was found.
- **Thomson Reuters Legal Tracker:** the Amazon case study describes routing international tax and entity information on each invoice to local business units, holding invoices until the vendor exists in both US and international AP systems, and viewing financials in USD and native currency [S: https://legal.thomsonreuters.com/en/insights/case-studies/amazon-case-study]. That implies a vendor-master gate and dual-currency display. It does not show VAT validation or e-invoice handling.
- **Brightflag, LawVu, Onit, CounselLink:** no public product information retrieved [U]. Ask vendors directly if competitive positioning matters.

Observed pattern: legal e-billing vendors ingest tax data as LEDES fields and review it against client guidelines, and they pass totals to AP. None of the evidence shows them acting as the tax e-invoice recipient. This is consistent with the recommended posture in section 7.

### 5.3 LEDES and tax

All from LEDES.org and Mitratech documentation [P/S].

- **LEDES XML 2.2 (ratified 2020)** adds tiered taxes (Japanese withholding tax is the cited driver), 18 segments, 205 data elements [P: https://ledes.org/ledes-xml-ebilling-ver-2-2/]. A 2019 comment notice said the change adds 2 segments and 13 data elements and significantly changes the invoice math [S: Wikipedia summary, https://en.wikipedia.org/wiki/Legal_Electronic_Data_Exchange_Standard].
- **2.2.1 (3-2023)** added a rate schema so timekeeper rates can be given in the timekeeper's home currency while the invoice is in another currency; data elements go from 205 to 216 [P, same URL].
- **2.2.2** was authorised by the LEDES board in April 2026 to correct an error where client funds were applied twice in the invoice total [P, same URL]. Track this: Spaarke's invoice math validation must follow the version.
- **LEDES XML 2.1 tax model [S: https://success.mitratech.com/Collaborati/User_Guides/Invoices/Upload_a_LEDES_File/LEDES_XML_2.1_Format]:**
  - `@TAX` defines each tax: `tx_id`, `tax_type`, `tax_rate_percent` (decimal, 7.25% is "0.0725"), `taxing_jurisdiction_country`, `taxing_jurisdiction_locality`, `tax_on_tax`, `tax_on_tax_id`, `increase_decrease`, and `tax_id_number` (firm or office tax ID).
  - `@TAX_SUMMARY` totals taxed fees and expenses per `tx_id` per matter.
  - `@TAX_ITEM_FEE` and `@TAX_ITEM_EXPENSE` link a tax to a line item by `file_item_nbr`. `@TAX_MATTER_DISC_CRED` links a tax to a matter-level discount or credit.
  - `@INVOICE` has `inv_total_tax`, `inv_total_tax_other_iso` (a different currency where law requires), `tax_point_date`, `tax_invoice` flag, `credit_note`, `tax_invoice_cl_name`, `tax_invoice_cl_id`, `inv_currency`, `inv_other_iso`.
  - Client tax ID is `cl_tax_id`. The firm office identifier `lf_office_id` can be the office's VAT number.
- The `tax_on_tax` fields express compound taxes such as Quebec; "tiered taxes" in 2.2 extend this to withholding-style taxes.
- LEDES 1998BI is the older pipe-delimited international format with one tax per line item [S: Wikipedia].
- **LEDES has no field for the national e-invoice ID, clearance ID or Peppol message ID.** The best public analogue is Collaborati's Tax Authority Ref ID and Alternative Invoice ID. This gap is the reason Spaarke needs its own identifier model.

### 5.4 Identifiers to track

Per invoice:

| Identifier | Why | Source |
|---|---|---|
| Supplier invoice number (commercial) | Duplicate check, firm's own reference | Everywhere |
| Tax invoice number (VAT invoice number) | Sequential number required by Art. 226; may differ from commercial number | EU VAT Directive [U], LEDES alt-ID analogue |
| Alternative invoice ID | Collaborati precedent | Mitratech [S] |
| Supplier VAT/tax ID and scheme | Art. 226; Peppol participant ID built on it | EU VAT Directive [U], Peppol example [S] |
| Buyer VAT/tax ID (legal entity) | Reverse charge needs the buyer's number; LEDES `cl_tax_id` | LEDES [S] |
| Supplier TIN and W-9 status (US) | 1099, backup withholding | IRS [P] |
| E-invoice document ID and format/syntax (UBL, CII, Factur-X, FatturaPA, KSeF FA) | Distinguishes the legal document from the portal copy | Country sources [S] |
| Clearance or platform ID (KSeF number, SDI identifier, PA/PPF identifier) | Clearance-model validity and payment reference | Poland, Italy, France [S] |
| Peppol message ID and document type ID, sender and receiver participant IDs, access point ID | Audit trail, dispute, Invoice Response correlation | Peppol [S]; message ID details [U] |
| Tax authority reference and tax authority invoice date | Mitratech precedent | Mitratech [S] |
| Tax point date | Determines the exchange rate date and period | LEDES `tax_point_date` [S] |
| Credit-note reference to original invoice | Belgium: credit note format follows the original | Belgium FAQ [P] |
| Self-billing flag and agreement reference | Belgium requires prior agreement; Italy TD17 | Belgium FAQ [P], Italy [S] |
| Invoice Response status (Peppol) | Accept/reject loop | Belgium FAQ [P] |
| PO/matter number | Legal-specific, links to the matter | Mitratech [S] |

Per vendor (law firm): legal name, country of establishment, tax ID(s) by country with scheme, W-9 or W-8 status and date, TIN match status, Peppol participant ID where known, bank account and currency, withholding profile (treaty rate, certificate).

---

## 6. Design implications

### 6.1 Tax data model

Do not store a single `tax_amount`. Minimum shape:

- **Invoice header:** `net_total`, `tax_total`, `gross_total`, `withholding_total`, `payable_total` (all in document currency), `tax_point_date`, `is_tax_invoice`, `is_credit_note`, `reverse_charge` (boolean), `reverse_charge_basis` (text or code), `tax_treatment` (domestic, reverse charge, exempt, out of scope, use-tax-self-assess), `supplier_establishment_country`, `buyer_entity` (lookup).
- **Tax line (child of invoice), 0..n:**
  - `tax_line_id` (maps to LEDES `tx_id`)
  - `tax_category`: VAT, GST, HST, PST, QST, sales tax, use tax, withholding
  - `tax_code` (customer-maintained code table; maps to the ERP tax code and to the EN 16931 VAT category codes S, Z, E, AE, K, G, O, L, M)
  - `rate`, `taxable_base`, `tax_amount`
  - `jurisdiction_country`, `jurisdiction_region`, `jurisdiction_locality`
  - `tax_on_tax_of` (self-reference, for compound taxes and LEDES `tax_on_tax_id`)
  - `direction`: charged by supplier, self-assessed by buyer, withheld from payee
  - `exemption_or_reverse_charge_reason`
- **Tax line allocation:** a link table from tax line to invoice line item (fee line or expense line), mirroring `@TAX_ITEM_FEE` and `@TAX_ITEM_EXPENSE`, plus an optional link to a matter-level discount or credit. Without it, mixed taxable and non-taxable lines (the Washington-type bundle) cannot be represented.
- **Invoice line item:** `net_amount`, `tax_code`, `line_tax_amount` (derived), and `payment_category` (fees, expenses, gross proceeds) for 1099.
- **Withholding** is a tax line with `direction = withheld`. It reduces `payable_total`. Hold `certificate_required` and beneficiary authority.
- R1 populates only: one or zero tax lines, USD, `tax_treatment` default. Everything else is nullable. No R1 code needs restructuring when VAT arrives.

### 6.2 Currency model

- **Document currency** (invoice currency, LEDES `inv_currency`), the legal currency of the invoice.
- **Tax currency** where law requires a different one (LEDES `inv_total_tax_other_iso`).
- **Base (reporting) currency** of the buyer entity.
- **Exchange rate record:** `rate`, `rate_source`, `rate_date`, `rate_type` (tax point date, invoice date or payment date), `rate_locked_by`. Store both the document-currency and base-currency amounts for every total and line. Store the rate; do not recompute from a live table.
- Timekeeper rate currency: LEDES 2.2.1 allows timekeeper rates in home currency while the invoice is in another [P]. Rate-card validation therefore needs a currency conversion step before comparing invoiced rates with approved rates.
- Rounding: keep per-line rounding rules configurable. EN 16931 validation rules check totals arithmetic, and a rounding mismatch is a common rejection cause [U, from general knowledge].
- R1 sets `document_currency = base_currency = USD` and rate = 1, but the columns exist.

### 6.3 Identifier model

- A separate **invoice document** entity (0..n per business invoice) with: `document_type` (portal PDF, LEDES file, UBL, CII, Factur-X, FatturaPA, KSeF FA), `syntax_version`, `received_via` (portal upload, email, Peppol, SDI, KSeF, PA/PPF, ERP), `source_message_id`, `hash`, `is_legal_original`, `received_at`, and the raw file stored in document storage.
- A separate **tax-platform event** child (0..n): `platform` (Peppol, SDI, KSeF, PPF/PA, HMRC, other), `platform_id` (KSeF number, SDI identifier, Peppol message ID), `status` (submitted, accepted, rejected, delivered, paid), `status_at`, `reason`, `raw_response`. This models clearance holds as a status, not as a column.
- **Party identifiers** as a child table: `party`, `scheme` (VAT, EIN, Peppol participant, GLN, national company ID), `value`, `country`, `validated_at`, `validation_source` (VIES, IRS TIN Match). A single `vat_number` column cannot hold a supplier with several country registrations.
- **Reconciliation key** between the portal submission and the legal e-invoice: the supplier's tax invoice number plus supplier tax ID plus gross total plus tax point date. Do not rely on a single field.

### 6.4 Check engine hooks

New rules that need no R1 implementation but must have a place:

- Reverse charge declared but both parties in the same country: error.
- Domestic invoice with 0 tax: warning.
- Supplier or buyer VAT ID missing or failing format or VIES validation.
- Tax lines do not sum to header, or net + tax is not gross, within a configured tolerance.
- E-invoice (structured) totals differ from the portal submission totals.
- Currency differs from the PO or matter currency.
- Duplicate: same supplier tax ID + tax invoice number.
- W-9 absent or TIN not matched for a US vendor: hold payment export, backup withholding at 24% flag.

---

## 7. Integration pattern

Three patterns, in order of effort:

1. **Portal-first, reconcile-later (recommended for R1 and early fast followers).**
   - The firm submits LEDES or PDF to Spaarke. Spaarke extracts and checks, and the in-house approver approves. Spaarke exports the approved commercial invoice, its tax lines and identifiers to AP/ERP.
   - Where the country mandates a structured e-invoice, the legal invoice arrives separately in the buyer's ERP or e-invoicing provider. Spaarke ingests a **reference** back (tax invoice number, platform ID, status) and reconciles. A mismatch becomes a review exception.
   - Cost: low. The risk is the buyer paying on the approval before the legal e-invoice is cleared, so the export carries a "payment blocked until tax-validated" flag where the customer enables it.
2. **Ingest the structured e-invoice as the source of truth.**
   - Spaarke accepts UBL, CII or Factur-X files (for example forwarded from the customer's access point or ERP), parses EN 16931 fields into the tax and identifier model, and runs legal-specific review (rates, timekeepers, guidelines) on the PDF/LEDES companion data.
   - This depends on law firms and platforms delivering LEDES-compatible detail inside or beside a structured invoice. Standard EN 16931 invoices lack timekeeper-level detail, so LEDES remains the carrier for legal line detail, and the e-invoice carries the tax-legal summary.
3. **Spaarke acts as the receiving platform** (own Peppol access point, SDI channel, KSeF and PA connections).
   - Heavy: accreditation, uptime, certification, per-country change tracking, and professional-secrecy concerns on invoice content [S, Norriq]. Prefer a **certified access-point partner** that delivers to a Spaarke endpoint rather than certifying Spaarke itself.

**Recommendation:** build the data model in section 6 now, ship pattern 1 in R1 with only reference fields, add pattern 2 as the first fast follower when a customer has an EU entity (Belgium, Germany, France are the most pressing because receive obligations are already or imminently in force), and treat pattern 3 as partner-delivered. Open the fast follower by country priority: Belgium and Germany (receive in force), France (receive 1 September 2026, current), then Poland, Italy, Croatia, Spain.

---

## 8. Open questions and verification list

1. Quote and verify EU VAT Directive Art. 44, 196, 226 from EUR-Lex (the fetch returned empty). Also verify the Art. 226 reverse-charge mention requirement and the ViDA text on 2030 and EN 16931.
2. Confirm the French date and scope of reception, e-reporting for foreign suppliers, and the PPF/annuaire identifiers from impots.gouv.fr when it is back up.
3. Confirm the German position on foreign suppliers and permanent establishments from the BMF letters (13 October 2024 and 15 October 2025).
4. Confirm Italy: SDI obligations of a buyer on non-resident services (TD17 deadline, esterometro status) from Agenzia delle Entrate.
5. Confirm Spain (BOE and AEAT) and Poland (Ministry of Finance, KSeF portal) after launch, including treatment of non-established suppliers.
6. Confirm Hawaii and New Mexico current treatment of legal services, the use-tax rule in the states the customers operate in, and whether the 2026 $2,000 information-return threshold applies to 1099-MISC box 10.
7. UK: fetch the gov.uk MTD for VAT guidance and the Budget 2026 e-invoicing roadmap. Canada: CRA self-assessment rules. Australia: ATO "connected with Australia" for legal services.
8. Legal e-billing vendors: ask Brightflag, LawVu, Onit/CounselLink and Wolters Kluwer ELM what they actually support (VAT validation, Peppol, clearance holds). No public evidence was found.
9. OpenPeppol: message ID and SBDH identifier usage, current BIS Billing version and the transition to the 2026 EN 16931 version.
10. LEDES: obtain the XML 2.2.x specification itself (not only vendor summaries) for the exact tiered-tax elements and invoice math before designing the importer.
11. Is the legal specialisation (professional secrecy of invoice descriptions) a constraint on what may appear in a structured e-invoice sent over Peppol? Belgian law firms raised it [S]; ask counsel.

## 9. Source index

Primary [P]:
- https://taxation-customs.ec.europa.eu/taxation/vat/vat-digital-age-vida_en
- https://ec.europa.eu/digital-building-blocks/sites/spaces/DIGITAL/pages/467108971/Country+overview (EN 16931 version information)
- https://einvoice.belgium.be/en/FAQ/specific-questions-about-e-invoicing
- https://www.irs.gov/instructions/i1099mec
- https://www.irs.gov/taxtopics/tc307
- https://ledes.org/ledes-xml-ebilling-ver-2-2/

Secondary [S], by topic:
- US state tax: https://www.claimsjournal.com/news/national/2016/10/20/274366.htm, https://www.cosmolex.com/blog/does-my-law-firm-have-to-pay-sales-tax/, https://www.avalara.com/us/en/blog/when-should-i-collect-sales-tax-on-my-services.html, https://www.withum.com/?p=130622, https://www.gtlaw.com/en/insights/2025/5/washington-state-expands-sales-tax-increases-b-and-o-capital-gains-taxes
- ViDA: https://www.pwc.com/mt/en/publications/vat/digital-reporting-and-e-invoicing-in-the-eu.html, https://digitalpolicyalert.org/change/4990
- France: https://dext.com/fr/ressources/blog-actualite-comptable/single/demarrage-facturation-electronique-recommandations-dg-fi-p, https://thepaypers.com/regulations/news/frances-e-invoicing-mandate-takes-effect-on-1-september, https://www.selexium.com/actualites/facturation-electronique-votre-entreprise-sera-t-elle-prete-au-1er-septembre-2026/, https://dev.to/zerolooplabs/frances-e-invoicing-mandate-lands-1-september-2026-the-developers-playbook-36eb
- Germany: https://www.fiscal-requirements.com/news/4566-germany-updated-bmf-guidance-on-2025-2028-b2b-e-invoicing-rollout, https://vatit.com/e-invoicing-guide/germany/, https://www.ihk.de/stuttgart/fuer-unternehmen/recht-und-steuern/steuerrecht/steuermeldungen/e-rechnungen-5864496, https://newsletter.pwc.de/umsatzsteuer/nl0029-2024-10-29/bmf-e-rechnung
- Belgium: https://www.fiscal-requirements.com/news/4211-belgium-e-invoicing-new-penalties-for-non-compliance-effective-2026, https://financialservices.norriq.com/en-be/blogs/professional-secrecy-and-peppol-how-law-firms-can-prepare-for-2026
- Poland: https://www.ey.com/en_gl/technical/tax-alerts/poland-signs-into-law-mandatory-national-e-invoicing-system, https://avask.com/blog/poland-e-invoicing-update-vat-compliance/, https://www.mddp.pl/poland-sets-out-plans-for-phased-rollout-of-mandatory-e-invoicing-from-2026/
- Italy: https://peppol.agid.gov.it/en/insights/electronic-invoicing/, https://www.b2brouter.net/fr/international/italy/, https://ecosire.com/blog/odoo-italy-localization-guide-2026
- Spain: https://www.vatcalc.com/spain/spain-verifactu-delay-till-jan-2027-for-certified-e-invoicing/, https://aplikujonline.comarch.pl/trade-and-services/data-management/legal-regulation-changes/delays-in-spains-crea-y-crece-e-invoicing-mandate-push-implementation-to-2027-or-later
- Croatia: https://www.cleartax.com/hr/en/e-invoicing-croatia, https://www.avalara.com/us/en/vatlive/country-guides/europe/croatia/croatian-e-invoicing.html
- UK: https://www.spendesk.com/blog/e-invoicing-uk/, https://www.fiscal-requirements.com/news/4902-uk-mandatory-e-invoicing-from-2029-main-points-from-consultation-response, https://www.sharedserviceslink.com/article/uk-b2b-e-invoicing-mandate-hmrc-consultation-response, https://www.accaglobal.com/uk/en/technical-activities/uk-tech/in-practice/2022/september/vat-on-supply-of-services-to-customers-outside-uk.html
- Australia: https://www.ato.gov.au/businesses-and-organisations/international-tax-for-business/gst-for-non-resident-businesses/gst-cross-border-transactions-between-businesses, https://www.allens.com.au/pubs/tax/fotax14may15.htm
- India: https://www.scconline.com/blog/post/2017/07/16/legal-services-of-advocates-firm-of-advocates-liable-to-gst-under-reverse-charge/, https://www.taxtmi.com/forum/issue?id=106019
- LatAm withholding: https://taxsummaries.pwc.com/brazil/corporate/withholding-taxes, https://taxsummaries.pwc.com/colombia/corporate/withholding-taxes, https://taxsummaries.pwc.com/argentina/corporate/withholding-taxes, https://taxsummaries.pwc.com/chile/corporate/withholding-taxes
- Peppol: https://accp.econnect.eu/en/docs/learn/document-formats/formats/bis-billing-v3, https://www.cleartax.com/de/en/peppol-bis-3-0-format-germany
- LEDES / vendors: https://en.wikipedia.org/wiki/Legal_Electronic_Data_Exchange_Standard, https://success.mitratech.com/Collaborati/User_Guides/Invoices/Upload_a_LEDES_File/LEDES_XML_2.1_Format, https://success.mitratech.com/Collaborati/Frequently_Asked_Questions/LEDES_Files/faq_ledesbiv2_specs, https://success.mitratech.com/Collaborati/collaborati_release_notes/Collaborati_Release_Notes/Collaborati_5.2_Release_Notes, https://success.mitratech.com/TeamConnect/TeamConnect_Module_Guides/Collaborati_Spend_Management_-_CSM/CSM_Settings/14_Handling_Non-U.S._Tax_and_International_Invoicing_Fields, https://legal.thomsonreuters.com/en/insights/case-studies/amazon-case-study, https://assets.contenthub.wolterskluwer.com/api/public/content/9a59703ea8a945e18a8827f492fba266
