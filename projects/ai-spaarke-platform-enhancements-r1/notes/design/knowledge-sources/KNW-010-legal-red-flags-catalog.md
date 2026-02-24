# KNW-010 — Legal Document Red Flags Catalog

> **External ID**: KNW-010
> **Content Type**: Reference
> **Tenant**: system
> **Created**: 2026-02-23
> **Task**: AIPL-032

---

## Overview

This catalog documents 30+ red flags that frequently appear in commercial legal documents and warrant escalation to legal counsel, further negotiation, or executive attention before signing. Red flags are organized by category and severity. Each entry explains why the provision is problematic and what a more favorable alternative looks like.

**Severity classification**:
- **[CRITICAL]**: Significant legal or financial exposure; should not be accepted without legal review and executive sign-off
- **[HIGH]**: Materially unfavorable; should be negotiated before execution
- **[MEDIUM]**: Below-market or one-sided; should be flagged for review

---

## Category 1: Liability and Indemnification Red Flags

### RF-001: Uncapped Indemnification Obligation [CRITICAL]

**Description**: The indemnification obligation has no dollar cap or other ceiling.

**Why it is a red flag**: An uncapped indemnification can result in liability far exceeding the value of the contract. In a worst case scenario (e.g., a data breach affecting millions of third-party individuals), uncapped indemnification could be financially catastrophic.

**Language to watch for**:
> "Vendor shall indemnify, defend, and hold harmless Client from and against any and all losses, claims, damages, liabilities, costs, and expenses arising out of Vendor's performance of the Services."

**Better alternative**: Indemnification should be capped at a multiple of fees paid (e.g., 2x or 3x annual fees), except for specific carve-outs such as fraud, gross negligence, willful misconduct, or death/personal injury.

### RF-002: Asymmetric Indemnification [HIGH]

**Description**: One party bears all indemnification obligations; the other party has none, or has significantly narrower obligations.

**Why it is a red flag**: In a balanced commercial relationship, both parties should bear responsibility for their own negligent acts. One-sided indemnification unfairly shifts all risk to one party.

**Language to watch for**: Review each party's indemnification section independently and compare the triggering events, scope of covered claims, and procedural requirements.

### RF-003: Unlimited Liability for IP Infringement [CRITICAL]

**Description**: One party provides a broad IP infringement indemnification with no cap and no carve-outs.

**Why it is a red flag**: Third-party IP infringement claims — particularly patent claims — can result in enormous damages. An uncapped, broad IP indemnification is particularly dangerous for service providers in software and technology.

**Better alternative**: IP indemnification caps should be consistent with the overall liability cap (or expressed as a separate, reasonable cap). Carve-outs should exclude infringement caused by the indemnified party's modification of the service or use in combination with third-party products.

### RF-004: Consequential Damages Waiver Without Carve-Outs [HIGH]

**Description**: The mutual exclusion of consequential damages is coupled with a liability cap, effectively limiting recovery to direct damages only up to a low cap amount.

**Why it is a red flag**: Where the primary harm from a breach would be consequential (e.g., lost profits, reputational damage, business interruption), a waiver without carve-outs means the non-defaulting party has no meaningful remedy for catastrophic breaches.

**Better alternative**: Carve out from the consequential damages waiver: (1) breach of confidentiality, (2) IP indemnification, (3) fraud/willful misconduct, (4) gross negligence.

---

## Category 2: Payment and Financial Red Flags

### RF-005: Automatic Price Escalation Without Cap [HIGH]

**Description**: Annual price increases are tied to CPI or another index without a cap on the increase.

**Why it is a red flag**: Unlimited CPI-based escalation can result in significant year-over-year price increases during high-inflation periods, with no contractual protection for the buyer.

**Better alternative**: CPI escalation with a cap (e.g., CPI increase but not more than 3% per year) and a floor of 0% (no decreases).

### RF-006: Unilateral Price Change Right [CRITICAL]

**Description**: The service provider reserves the right to change pricing at any time with minimal notice.

**Why it is a red flag**: This provision eliminates price certainty and gives the provider leverage to increase prices during the contract term without the customer's consent.

**Language to watch for**:
> "Provider reserves the right to change the fees for the Services at any time upon [30/60] days' notice."

**Better alternative**: Pricing should be fixed for the contract term. Any change in pricing should require a contract amendment with mutual written consent, or at minimum a right to terminate without penalty if the customer does not agree to the new pricing.

### RF-007: Payment Due Before Delivery or Acceptance [HIGH]

**Description**: Payment is required before services are performed or deliverables are accepted.

**Why it is a red flag**: Pre-payment eliminates the customer's primary leverage for ensuring performance. If the vendor fails to perform, recovery of pre-paid amounts may require litigation.

**Better alternative**: Milestone-based payment tied to acceptance of deliverables, or payment in arrears. Pre-payments (e.g., setup fees, deposits) should be expressly refundable if the vendor fails to perform.

### RF-008: No Late Payment Cure Period [MEDIUM]

**Description**: The vendor may suspend services or terminate the contract for any non-payment without a grace period.

**Why it is a red flag**: Payment delays can occur due to processing delays, disputes, or administrative errors. Without a grace period, a routine payment processing delay could trigger suspension or termination.

**Better alternative**: Grace period of at least 5–10 business days after written notice before suspension or termination rights arise.

---

## Category 3: Termination Red Flags

### RF-009: No Termination for Convenience Right [HIGH]

**Description**: The customer has no right to terminate the contract without cause before the end of the term.

**Why it is a red flag**: Business needs change. A contract without a termination for convenience right locks the customer into an arrangement regardless of changed circumstances, service quality, or strategy.

**Better alternative**: Negotiate a termination for convenience right (typically with 30–90 days notice). Accept a reasonable termination fee to compensate the vendor for sunk costs.

### RF-010: Termination Triggers Immediate Acceleration of All Payments [CRITICAL]

**Description**: Upon termination for any reason, all remaining payments under the contract become immediately due and payable.

**Why it is a red flag**: Acceleration of all future payments eliminates the benefit of a termination right. The customer is effectively obligated to pay for the full term regardless of when the contract ends.

**Language to watch for**:
> "In the event of early termination for any reason, all remaining fees for the balance of the then-current term shall become immediately due and payable."

**Better alternative**: Upon customer termination for cause (vendor default), no further payments due. Upon vendor termination for customer default, payment for services rendered to the termination date only (not for future periods).

### RF-011: Vendor Can Terminate for Convenience on Short Notice [HIGH]

**Description**: The vendor has a right to terminate for convenience on very short notice (fewer than 30 days).

**Why it is a red flag**: If the vendor terminates services with minimal notice, the customer may have insufficient time to procure alternative services, resulting in business disruption.

**Better alternative**: If the vendor has a termination for convenience right, the notice period should be at least 90 days and include transition assistance obligations.

---

## Category 4: Data and Privacy Red Flags

### RF-012: Vendor Data Ownership Claim [CRITICAL]

**Description**: The contract states that the vendor owns customer data or any data generated from the customer's use of the service.

**Why it is a red flag**: This is a fundamental ownership issue. Customer data belongs to the customer. A claim of ownership over customer data, usage data, or derived analytics may conflict with the customer's regulatory obligations and business interests.

**Language to watch for**:
> "All data processed through the Service, including Customer Data, shall be owned by Provider."
> "Provider may use aggregated, anonymized data derived from Customer's use of the Service for any purpose."

**Better alternative**: Customer owns all Customer Data. Vendor may use Customer Data only to provide the services and as required by law. Usage data and analytics derived from Customer Data are subject to negotiation, but vendor's use should be limited to service improvement.

### RF-013: Broad Data Use Rights for Third-Party or Marketing Purposes [HIGH]

**Description**: The vendor reserves the right to use customer data for purposes beyond service delivery — including marketing, product development, or sale to third parties.

**Why it is a red flag**: Use of customer data for non-service purposes may violate the customer's privacy policies, regulatory obligations (GDPR, CCPA), and contractual commitments to its own customers.

### RF-014: No Data Return or Destruction Obligation [HIGH]

**Description**: Upon termination, the vendor has no obligation to return or delete customer data.

**Why it is a red flag**: Retained data poses ongoing privacy, security, and regulatory risks. Without a deletion obligation, customer data may be held indefinitely — potentially subject to future breach or unauthorized use.

---

## Category 5: Intellectual Property Red Flags

### RF-015: Broad Work Product Ownership by Service Provider [HIGH]

**Description**: The vendor claims ownership of all work product, deliverables, or improvements created under the contract.

**Why it is a red flag**: If the customer is paying for custom development, the customer should own the resulting work product. Vendor ownership of custom deliverables provides significant leverage over the customer's future product development.

### RF-016: No IP Indemnification [HIGH]

**Description**: The vendor provides no warranty or indemnification for infringement of third-party IP by the vendor's service or deliverables.

**Why it is a red flag**: Use of a service that infringes third-party IP could expose the customer to infringement claims. Without an IP indemnification, the customer bears that risk.

### RF-017: License to Use Customer's IP for Vendor's Benefit [MEDIUM]

**Description**: The vendor's license to use customer's IP (trademarks, logos, content) is broader than necessary to provide the service.

**Why it is a red flag**: Broad IP licenses to the vendor can result in the vendor using the customer's brand or content in ways the customer did not intend (e.g., marketing materials, case studies, press releases).

---

## Category 6: Contract Structure Red Flags

### RF-018: Automatic Renewal Without Notification Window [HIGH]

**Description**: The contract automatically renews for a full term (not a shorter period) unless notice is given within a very narrow window.

**Why it is a red flag**: If the customer misses the narrow notice window, it is locked in for another full term. For multi-year contracts, this can result in years of unintended commitment.

**Language to watch for**:
> "This Agreement will automatically renew for successive one-year terms unless either party provides written notice of non-renewal at least [90] days prior to the end of the then-current term."

**Better alternative**: Shorter renewal periods (month-to-month or 1 year), longer notice windows (at least 180 days for multi-year contracts), and active renewal consent.

### RF-019: Unilateral Right to Modify Agreement [CRITICAL]

**Description**: The vendor reserves the right to modify the terms of the agreement unilaterally, with or without notice.

**Why it is a red flag**: This provision effectively converts a binding contract into a set of terms that can be changed at will by one party. The customer's agreed-upon rights and protections can be eliminated unilaterally.

**Language to watch for**:
> "Provider reserves the right to modify these Terms at any time. Your continued use of the Service after the effective date of any modification constitutes your acceptance of the modified Terms."

**Better alternative**: Any modification requires mutual written consent. SaaS providers may reserve the right to update terms for legal compliance purposes only, with a reasonable notice period and a termination right if the customer does not agree.

### RF-020: Incorporation of Vendor's Standard Terms by Reference [HIGH]

**Description**: The agreement incorporates the vendor's standard terms and conditions, acceptable use policy, or privacy policy "as amended from time to time" or by URL reference.

**Why it is a red flag**: Incorporating external documents "as amended" allows the vendor to modify the incorporated terms unilaterally and without the customer's consent.

**Better alternative**: Attach the referenced documents as exhibits to the contract. Any amendment to an exhibit requires a written amendment to the master agreement.

---

## Category 7: Dispute Resolution Red Flags

### RF-021: One-Sided Forum Selection [HIGH]

**Description**: The forum selection clause requires the customer to litigate in the vendor's home jurisdiction regardless of where the dispute arises.

**Why it is a red flag**: Litigating in an inconvenient forum significantly increases the cost of enforcing rights and may effectively deter legitimate claims.

### RF-022: Arbitration Class Action Waiver [MEDIUM]

**Description**: The arbitration provision includes a class action or collective arbitration waiver, preventing the customer from participating in a class action or bringing claims on behalf of others.

**Why it is a red flag**: For high-volume, low-value claims (e.g., billing errors affecting many customers), the class action waiver makes individual claims economically impractical. This is a standard vendor-protective provision but should be noted.

### RF-023: Mandatory Arbitration With Vendor's Preferred Rules [MEDIUM]

**Description**: The arbitration provision requires use of a specific arbitral body that is closely affiliated with the vendor or imposes procedurally unfavorable rules.

**Why it is a red flag**: Arbitral body selection can affect the neutrality, speed, cost, and outcome of the process.

---

## Category 8: Confidentiality Red Flags

### RF-024: Residuals Clause in NDA [HIGH]

**Description**: The NDA includes a residuals clause allowing the receiving party to use general information retained in employees' unaided memories after exposure to confidential information.

**Why it is a red flag**: Residuals clauses can be used to justify use of trade secrets and proprietary know-how after the relationship ends. They are particularly problematic in technology and software development contexts.

### RF-025: Short Confidentiality Survival Period for Trade Secrets [HIGH]

**Description**: Confidentiality obligations survive for only 1–2 years after termination, even for information that constitutes trade secrets.

**Why it is a red flag**: Trade secrets are protected indefinitely under applicable law. A contractual survival period shorter than indefinite may create ambiguity about whether trade secrets receive their full statutory protection.

---

## Category 9: Employment and Personnel Red Flags

### RF-026: Broad Solicitation Restriction on Customer's Personnel [MEDIUM]

**Description**: The vendor imposes a non-solicitation restriction preventing the customer from hiring the vendor's employees who have worked on the account.

**Why it is a red flag**: One-sided non-solicitation provisions that protect only the vendor's workforce are not commercially balanced and may limit the customer's ability to hire talented individuals.

### RF-027: Inadequate Data Security Requirements for Vendor Personnel [HIGH]

**Description**: The contract imposes no security training, background check, or confidentiality requirements on vendor personnel who will access the customer's data or systems.

**Why it is a red flag**: Vendor personnel are a primary attack vector for social engineering and insider threats. Absence of personnel security requirements is a significant gap.

---

## Category 10: Miscellaneous Red Flags

### RF-028: Broad Representations by Customer About Data Legality [HIGH]

**Description**: The customer broadly represents and warrants that all data provided to the vendor is lawfully obtained and does not infringe any third-party rights.

**Why it is a red flag**: This is an appropriate representation in principle, but overly broad scope (e.g., covering all data the vendor processes) can expose the customer to liability for data quality issues in complex data streams.

### RF-029: No Audit Right [MEDIUM]

**Description**: The customer has no right to audit the vendor's security practices, compliance with contract terms, or use of customer data.

**Why it is a red flag**: Without audit rights, the customer cannot verify the vendor's compliance representations. This is particularly problematic for regulated industries (financial services, healthcare, government).

### RF-030: Missing Force Majeure Provision [MEDIUM]

**Description**: The contract has no force majeure provision.

**Why it is a red flag**: Without a force majeure clause, a party may face breach of contract claims for non-performance caused by genuinely unforeseeable and uncontrollable events. Whether the common law doctrine of impossibility or frustration will apply is uncertain.

### RF-031: Change of Control Trigger — No Assignment Consent Right [HIGH]

**Description**: The contract provides that a change of control of the vendor does not constitute an assignment and does not require the customer's consent.

**Why it is a red flag**: If the vendor is acquired by a competitor, the customer may be forced to continue providing access and payments to a party with whom it is not willing to do business. The customer should have at minimum a termination right on a change of control.

### RF-032: Survival Clause Omitting Key Obligations [MEDIUM]

**Description**: The survival clause does not expressly include confidentiality, IP ownership, indemnification, or limitation of liability provisions.

**Why it is a red flag**: Without express survival language, a court might interpret termination of the contract as terminating all obligations — including those the parties intended to survive. Survival provisions should expressly identify each provision that must survive.

---

*This catalog supports AI-assisted legal document review. It does not constitute legal advice. Legal review of all commercial contracts is recommended before execution.*
