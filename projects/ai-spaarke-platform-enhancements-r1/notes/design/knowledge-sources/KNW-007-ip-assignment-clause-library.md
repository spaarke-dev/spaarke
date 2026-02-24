# KNW-007 — IP Assignment Clause Library

> **External ID**: KNW-007
> **Content Type**: Reference
> **Tenant**: system
> **Created**: 2026-02-23
> **Task**: AIPL-032

---

## Overview

This clause library provides standard, annotated intellectual property (IP) assignment provisions for use in employment agreements, independent contractor agreements, statements of work, and consulting arrangements. Each clause variation is presented with drafting notes explaining its scope, intended use, and key negotiation considerations.

---

## Part 1: Core IP Assignment Provisions

### 1.1 Broad Work Product Assignment (Employer-Favorable)

**Use case**: Technology companies, software development, R&D-intensive organizations.

**Clause text**:

> **Assignment of Work Product.** Employee hereby irrevocably assigns and transfers to Company, and agrees to assign and transfer to Company, all right, title, and interest in and to any and all Inventions. "Inventions" means all inventions, discoveries, improvements, developments, innovations, works of authorship, software, databases, algorithms, data, processes, designs, know-how, trade secrets, mask works, and other intellectual property, whether or not patentable or copyrightable, that Employee makes, conceives, reduces to practice, or creates, either alone or jointly with others, during the period of Employee's employment with Company that: (a) relate to Company's business or actual or reasonably anticipated research or development; (b) result from work performed by Employee for Company; or (c) are made using Company's equipment, supplies, facilities, trade secrets, or confidential information.

**Drafting notes**:
- The three-part scope (relate to business / made at work / using company resources) is the broadest common formulation.
- "Reasonably anticipated research or development" captures future business areas and should not be unlimited in scope.
- Subject to statutory limitations in California (Labor Code § 2870), Delaware, Illinois, Minnesota, and Washington.
- The assignment language should be in present tense ("hereby assigns") for effectiveness without a further act; "agrees to assign" language may require a further assignment to be legally effective for certain types of IP.

### 1.2 Work-Performed-For Assignment (Narrower/Balanced)

**Use case**: Non-technology employers; balanced negotiations.

**Clause text**:

> **Work Product.** All Work Product shall be the sole and exclusive property of Company. "Work Product" means all works of authorship, inventions, discoveries, software, developments, and other intellectual property conceived, created, developed, or reduced to practice by Employee, solely or jointly, in the performance of Employee's duties for Company or using Company's Confidential Information, equipment, or resources. Employee hereby irrevocably assigns to Company all right, title, and interest in and to all Work Product, including all intellectual property rights therein.

**Drafting notes**:
- Narrower than Clause 1.1 because it is limited to work performed for Company or using Company resources; it does not reach work merely "related to" the company's business.
- Better balanced for employees with independent consulting or creative work outside their scope of employment.
- Still subject to moonlighting protection statutes in covered states.

### 1.3 Independent Contractor Work-for-Hire and Assignment

**Use case**: Independent contractor agreements, consulting agreements, statements of work.

**Clause text**:

> **Ownership of Deliverables.** All deliverables, work product, and materials created by Contractor in connection with the Services (collectively, "Deliverables") shall, to the maximum extent permitted by law, be considered works made for hire for Client within the meaning of Section 101 of the Copyright Act. To the extent any Deliverable does not qualify as a work made for hire, Contractor hereby irrevocably assigns to Client all right, title, and interest in and to such Deliverables, including all copyrights, patents, trade secrets, moral rights, and other intellectual property rights therein, throughout the universe in perpetuity. Contractor waives any and all moral rights with respect to the Deliverables to the fullest extent permitted by law. Contractor shall, at Client's request and expense, execute any documents and take any actions necessary to perfect or record such assignment.

**Drafting notes**:
- The dual structure (work for hire + fallback assignment) is essential because not all works qualify as works for hire under the Copyright Act.
- Works made for hire by independent contractors are limited to nine statutory categories: contributions to collective works, part of a motion picture or other audiovisual work, a translation, a supplementary work, a compilation, an instructional text, a test, answer material for a test, and an atlas.
- The assignment must be in writing and signed to be effective for patent rights.
- "Throughout the universe in perpetuity" is standard IP assignment language addressing future exploitation technologies and geographies.
- Moral rights waiver is important in jurisdictions that recognize moral rights (EU, UK); less relevant in the US where moral rights are limited.

---

## Part 2: Background IP and License-Back Provisions

### 2.1 Background IP Definition and License

**Use case**: Agreements where contractor brings pre-existing IP to the engagement.

**Clause text**:

> **Background IP.** "Background IP" means all intellectual property owned or controlled by Contractor prior to the commencement of the Services or developed by Contractor independently of the Services without use of Client's Confidential Information or resources, that is incorporated into the Deliverables. Contractor retains all right, title, and interest in and to Background IP. Contractor grants to Client a perpetual, irrevocable, worldwide, royalty-free, non-exclusive license to use, copy, modify, and distribute the Background IP solely as incorporated in the Deliverables and as necessary to receive the full benefit of the Deliverables.

**Drafting notes**:
- This provision protects the contractor's pre-existing tools, libraries, and frameworks while ensuring the client can use the final deliverable.
- The license-back is typically limited to "as incorporated in the Deliverables" to prevent the client from using the Background IP in other products.
- Clients may seek a broader license; contractors should resist expansion beyond what is necessary to use the Deliverables.
- A schedule listing specific Background IP at the time of contracting is advisable to avoid disputes about what constitutes Background IP.

### 2.2 Background IP Exclusion Schedule

**Clause text**:

> **Prior Inventions.** Attached as Exhibit A is a complete list of all Inventions that Employee has, alone or jointly with others, conceived, developed, or reduced to practice prior to the commencement of Employee's employment ("Prior Inventions"). Employee's obligations under this Agreement do not apply to any Prior Inventions. If no Prior Inventions are listed on Exhibit A, Employee represents that there are no Prior Inventions. If, in the course of Employee's employment with Company, Employee incorporates any Prior Invention into any Company product, process, or service, Company is hereby granted a non-exclusive, royalty-free, irrevocable, perpetual, worldwide license (with the right to sublicense) to make, have made, modify, use, and sell such Prior Invention as part of or in connection with such product, process, or service.

**Drafting notes**:
- The automatic license-back provision is important: without it, if an employee incorporates Background IP, the company could face an infringement claim.
- The "if no Prior Inventions are listed, there are none" representation is a standard protective device.
- Employers should not refuse to accept Prior Inventions listings — doing so may increase litigation risk.

---

## Part 3: Specialized IP Assignment Variations

### 3.1 AI and Machine Learning Model Assignment

**Use case**: Contracts involving AI development, training data, model weights.

**Clause text**:

> **AI Work Product.** Without limiting the foregoing, Work Product includes all AI models, model weights, training datasets, fine-tuned models, prompt templates, evaluation datasets, and intermediate model artifacts developed in connection with the Services. All training data compiled or curated in connection with the Services, all labeled datasets, and all model evaluation benchmarks shall constitute Work Product regardless of whether they contain pre-existing data, and Company shall own all rights in the new selection, arrangement, and labeling therein. Contractor represents that no third-party training data used in connection with the Services is subject to license terms that would restrict Client's ownership or use of the resulting model.

**Drafting notes**:
- AI-specific IP assignment clauses are increasingly important and frequently absent from standard form agreements.
- Model weights are not clearly categorized under traditional IP law; assignment language should be explicitly inclusive.
- Training data ownership is legally complex — the clause should at minimum cover the compilation and labeling, not assert ownership of underlying public domain data.
- Third-party data license representation is critical: open-source datasets have varying license terms (e.g., CC BY-SA has share-alike requirements that may "infect" derived works).

### 3.2 Software Assignment with Open Source Exception

**Use case**: Software development agreements where contractor may use open source components.

**Clause text**:

> **Open Source Compliance.** Contractor shall not incorporate any open source software, libraries, or materials ("Open Source Components") into the Deliverables without Company's prior written approval. For each approved Open Source Component, Contractor shall: (i) disclose the name, version, and applicable license; (ii) identify which portions of the Deliverables incorporate the component; and (iii) ensure the incorporation complies with the applicable open source license. Contractor shall not use Open Source Components subject to "copyleft" or "share-alike" licenses (including GPL v2/v3, LGPL, AGPL, and similar licenses) without Company's specific written consent after disclosure of the license terms.

**Drafting notes**:
- Copyleft licenses (GPL) can require that the entire combined work be released under the same license — this can inadvertently "open source" proprietary code.
- Permissive licenses (MIT, BSD, Apache 2.0) generally do not have this risk.
- A software bill of materials (SBOM) requirement can support ongoing compliance.

---

## Part 4: Moral Rights and Waiver

### 4.1 Moral Rights Waiver

**Use case**: International contracts; contracts involving creative works (art, literature, software as applicable).

**Clause text**:

> **Moral Rights.** To the extent that any Work Product is subject to moral rights, rights of integrity, rights of attribution, or similar rights under any applicable law ("Moral Rights"), Creator hereby irrevocably waives and agrees not to assert any such Moral Rights, and consents to all acts that would otherwise infringe such rights, including the right of Company to modify, alter, adapt, or make derivative works of the Work Product without attribution. Creator agrees to execute any written waivers or consents as Company may reasonably request to give effect to this waiver.

**Drafting notes**:
- Moral rights are not waivable under US law for fine art (Visual Artists Rights Act), but are generally not applicable to software or most business works.
- Moral rights waivers are important in contracts with EU/UK nationals or for works governed by European law.

---

## Part 5: Assignment of Patent Rights

### 5.1 Future Patents Assignment

**Clause text**:

> **Patent Assignment.** Employee agrees to assist Company in every proper way to obtain patents on Inventions in any and all countries, and to that end agrees to execute all documents that Company may reasonably request for use in obtaining, maintaining, and enforcing such patents. Employee's obligation to execute documents continues after the termination of Employee's employment for Inventions made during employment. If Company is unable, after reasonable effort, to obtain Employee's signature on any document needed in connection with the actions specified in this section, Employee irrevocably designates and appoints Company and its duly authorized officers as Employee's agent and attorney-in-fact to act for and in Employee's behalf to execute such documents.

**Drafting notes**:
- The post-employment obligation is critical: patent applications are often filed after employment ends.
- The power of attorney (attorney-in-fact) provision is a fallback mechanism widely used in employment and IP agreements.
- Consideration for post-employment obligations must be adequate (the original employment consideration typically suffices for assignments made during employment).

---

*This clause library is a reference document for AI-assisted IP provision analysis and drafting guidance. It does not constitute legal advice. All clauses should be reviewed by qualified IP and employment counsel before use.*
