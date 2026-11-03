---
name: legal-skill-content-sources-2026-10-31
description: Per-practice content sources for legal Agent Skills (NDA, employment, CRE, invoice, U.S. patents) - OpenAgreements Apache as best NDA/employment scaffold with 56-jx noncompete survey; ContractNLI is CC BY 4.0 (not NC); aggregator licence traps (lawve-ai CC BY-NC-ND, borghei Commons Clause); invoice/patent/CRE thin in skills so use LEDES/UTBMS/FDIC, MPEP/WIPO CC BY/USPTO PD data, GSA L100; datasets with licences
metadata:
  type: reference
---

# Sources for authoring legal Agent Skills content — five U.S. practice areas (as of 2026-10-31)

Tags: **Cited** = I fetched the primary page / API / file today. **Secondary** = seen only via search snippet, mirror, or third-party page. **Inferred** = training-data knowledge, not re-verified today.
Clones (data only, never executed) are under `C:\Users\RalphSchroeder\AppData\Local\Temp\claude\c--code-files-spaarke\ee376ef3-44a0-44eb-9490-149c2fde42f6\scratchpad\repos\`.

## Summary

1. **The biggest new find is `open-agreements/open-agreements`** (Apache-2.0, HEAD `50ae131` 2026-10-29, 930 commits): primary-source-cited U.S. practice guides, a **56-jurisdiction non-compete survey with 16 structured columns**, per-state restrictive-covenant and offer-letter templates (CC BY 4.0), review checklists (non-compete, hiring, invention assignment, offer letter), a non-compete *currency* eval harness, `skills/agreements/{nda,employment-contract,...}` SKILL.md files, and a per-template `metadata.yaml` licence model that already separates CC-BY / CC0 / "free but non-redistributable" (NVCA) / CC-BY-ND (YC). It is the best single scaffold for NDA + employment content and a model for how to record provenance.
2. **Correction to the brief:** ContractNLI is **CC BY 4.0**, not non-commercial (project page, GitHub LICENSE, LegalBench task page all say CC BY 4.0; only the `kiddothe2b/contract-nli` HF mirror mislabels it cc-by-nc-sa-4.0). 607 NDAs + 17 hypotheses are usable commercially with attribution.
3. **Two aggregator repos look rich but have licence traps:** `lawve-ai/awesome-legal-skills` is **CC BY-NC-ND 4.0 at repo level** (individual skills override only where they ship their own LICENSE: e.g., Carl Ditzler's outside-counsel billing reviewer is Apache-2.0, Jamie Tso's NDA reviewer is **AGPL-3.0**). `borghei/Claude-Skills` is **MIT + Commons Clause** (no selling). `ThomasMoreAI/legal-skills-open` is an Apache-2.0 *index* of 18k skills mirrored from other repos with per-skill `author:`/`license:` frontmatter — use it to discover, then take from the upstream.
4. **Invoice review and U.S. patent drafting remain thin in the open skill ecosystem.** The usable raw material is standards/government: LEDES 98B spec + sample (free download, © LOC, no stated licence), UTBMS code sets incl. **LOC IP Patent Prosecution Codes (Apr 2023, updated Jun 2024)**, FDIC Outside Counsel Deskbook (rev. Nov 2025, public domain), published public-sector OCGs (NJ AG, City of Chicago 2024, Penn State 2025), MPEP 9th ed. rev. 01.2024 + 2025 superseding memos, WIPO Patent Drafting Manual 2nd ed. (**CC BY 4.0**), and USPTO public-domain datasets (Office Action Research Dataset 2008–2017, PatEx 2001–2023 updated 2026-01-20, Patent Claims Research Dataset updated 2026-09-03).
5. **CRE leases have no open U.S. checklist of quality.** Best scaffolds: GSA Global Lease L100 (rev. May 2026, public domain) as a public lease form; `mariourquia/cre-skills-plugin` (Apache-2.0; lease-abstract-extractor 30+ fields, estoppel-certificate-generator, CAM reconciliation, lease-compliance-auditor); CaseMark's office/retail/industrial lease + SNDA + estoppel drafting scaffolds (Apache-2.0); SEC EDGAR exhibit leases via Full-Text Search. BOMA and AIR CRE forms are paywalled/proprietary.

---

## A. Agent-skill / prompt repositories (general)

| Repo | HEAD / pushed | Licence | Commercial OK? | What's there / quality | Take | Tag |
|---|---|---|---|---|---|---|
| **open-agreements/open-agreements** | `50ae131` 2026-10-29 | Apache-2.0 (code, guides, checklists, surveys); templates per `metadata.yaml`: Common Paper CC-BY-4.0, Bonterms cover pages labelled CC0-1.0, OpenAgreements CC-BY-4.0 / CC0, NVCA "free, non-redistributable" (field-selector only), YC CC-BY-ND-4.0 | Yes (Apache/CC-BY/CC0 parts); NVCA/YC pointer-only | 750 template files, 169 checklist files, 44 survey files, practice guides with footnoted primary-law citations, `skills/agreements/{nda,employment-contract,cloud-service-agreement,...}`, `evals/non-compete-currency` (CI re-derives numbers from pinned guide commit), `docs/employment-source-policy.md` (permissive / pointer-only / restricted classification). Lease guide is thin (761 words, public-housing focus). No invoice or patent content. | NDA + employment baselines, 50-state non-compete matrix, state offer-letter/restrictive-covenant templates, provenance model | Cited |
| **anthropics/claude-for-legal** | `4a6c651` 2026-07-23 (pushed 2026-10-29) | Apache-2.0 | Yes | Known. 151 SKILL.md, method-only, no default positions; `employment-legal` incl. worker-classification (3,356 words) | Process skeletons, cold-start interview pattern | Cited (prior memory) |
| **Open-Legal-Products/mike-workflows** / **mikegc-workflows** | `20bed20` 2026-10-20 / pushed 2026-10-27 | MIT / Apache-2.0 | Yes | Known. Lean table-output reviews; lease is UK-flavoured | NDA, employment, tabular column sets | Cited (prior memory) |
| **CaseMark/skills** | `51b5e32` 2026-10-28 | Apache-2.0 | Yes | 874 legal SKILL.md drafting scaffolds, 450–1,150 words each, article frameworks + prerequisites + checklists. Relevant: office-lease, retail-lease-agreement, industrial-warehouse-lease, triple-net-lease, snda, tenant-estoppel-certificate, lease-summary, commercial-lease-summary, non-compete-agreement (names CA/MN/ND/OK bans, red-pencil), separation-agreement (OWBPA 21/45/7-day table), employee-separation-release, independent-contractor-agreement, executive-employment-agreement, confidentiality-nda, nda-summary, patent-summary, billing-summary. README gives no authorship/review provenance; `skill-qa.yml` validates format only. | Drafting article maps and intake checklists for CRE lease family, separation/OWBPA, non-compete | Cited |
| **ThomasMoreAI/legal-skills-open** | `2896ce4` 2026-10-12 | Apache-2.0 (repo); per-skill `license:` frontmatter (MIT/Apache/CC-BY...) | Mostly yes; check each skill | 18k+ skills, 54 jurisdictions, aggregator (mirrors CaseMark, claude-for-legal, mike, lawve, LegalQuants, jeffreytse, RobThePCGuy, OneWave-AI...). `us/real-estate` 92, `us/employment` 81, `us/ip` 168 skills. Some filenames are German-language mirrors mis-filed under `us/`. No lawyer-review claim. | Discovery index only; take from upstream | Cited |
| **LegalQuants/lq-ai** (+ `lq-skills`) | `755abc6` 2026-10-28 | Apache-2.0 | Yes | 17 skills: nda-review (2,356 words, perspective-aware), nda-snapshot, msa-review-saas, playbook-easy-extract. **`skills/playbooks/{nda,nda-unilateral,msa-saas,msa-commercial-purchase,dpa-gdpr}/playbook.yaml`** — structured positions with standard language, fallback tiers, redline strategy; explicitly "starting point, not vetted". | NDA playbook YAML schema + 8 mutual-NDA positions as a seed | Cited |
| **lawve-ai/awesome-legal-skills** | `f9f28cd` 2026-10-19 (916 stars) | **CC BY-NC-ND 4.0 repo-level**; 206/276 skills declare own licence (AGPL-3.0 ×6, CC-BY-4.0 ×5, MIT ×3, Apache ×2, CC-BY-NC-SA ×1 ...) | **No** by default; only skills with permissive own LICENSE | Scott Margetts legal-ops set (invoice-review-compliance 7,382 words, engagement-terms-billing-guidelines 5,240 words — **no own LICENSE → NC-ND**); Carl Ditzler outside-counsel-billing-and-performance-reviewer (**Apache-2.0**; PLAYBOOK-SCHEMA.md OCG rule schema, METRICS-CATALOG, FAILURE-MODES, REFERENCE-SOURCES with safe-to-embed classification); nda-reviewer-jamie-tso (**AGPL-3.0**); nda-reviewer-anthropic (Apache mirror). No lease/patent skills. | Ditzler OCG rule schema + metrics catalog (Apache). Read Margetts for *structure* only; do not copy text. | Cited |
| **mariourquia/cre-skills-plugin** | `e4597f1` 2026-10-28 | Apache-2.0 (+NOTICE crediting ahacker-1/cre-acquisition-orchestrator, Apache) | Yes | 100+ institutional CRE skills; lease-abstract-extractor (1,623 words, 30+ fields, amendment cross-ref, critical-dates), estoppel-certificate-generator (1,345), lease-compliance-auditor (1,712), lease-negotiation-analyzer, cam-reconciliation-calculator, rent-roll tools. Investor/asset-manager perspective, not counsel. | Abstract field list, estoppel generator logic, CAM audit maths | Cited |
| **evolsb/claude-legal-skill** | `bca0d2e` 2026-07-23 (491 stars) | MIT | Yes | Single 2,280-word contract-review skill built on CUAD's 41 categories, with NDA/SaaS/M&A examples | CUAD-category risk taxonomy mapping | Cited |
| **judicialmind/legal-skills** | `916ec5a` 2026-03-20 | MIT | Yes | 30 practice folders; `billing-operations` 1,474 words (UTBMS, LEDES, time-entry quality) — generic | Time-entry quality rules | Cited |
| **microsoft/cat-agent-skills** | `3678d23` 2026-10-28 | MIT | Yes | `submissions/vendor-contract-risk-review` (595 words; first-pass clause flags: auto-renewal, termination, caps, indemnity, data/IP, SLA, pricing, governing law, assignment) and `submissions/redlining-content` (664 words; template-vs-draft Track Changes via `scripts/redline.py`, `assets/template.docx`; platforms Cowork + Copilot Studio; v1.0.0 2026-07-08). Cowork-native packaging examples. | Cowork/Copilot Studio packaging pattern; redline script | Cited |
| **sboghossian/mini-claude-for-legal** | `a3bdc4e` 2026-05-14 | MIT | Yes | 982 skills, **MENA-first** lens; draft-non-compete, draft-severance-agreement, review-lease-landlord-side, prompt-pack-legal-invoice-review-checklist (1,363 words), eval-dataset-nda-prompts-30 (JSONL eval format with expected_signals). Zero OWBPA/DTSA hits. | Eval-dataset shape only | Cited |
| **revfactory/harness-100** `en/68-patent-drafter` | `8e8d35c` 2026-03-22 | Apache-2.0 | Yes | claim-drafting-patterns (688 words), patent-drafter, prior-art-search-strategy + 5 agents. Claim templates are **KR/JP-style** ("...comprising [title]" closing, "said"), not U.S. practice. | Multi-agent orchestration shape only | Cited |
| **borghei/Claude-Skills** `legal/` | `2c12c7e` 2026-10-23 | **MIT + Commons Clause** | **No** (Commons Clause bars selling) | nda-review, nda-triage, tabular-document-review | Avoid | Cited |
| **TapnTell-AI/awesome-legal-aiagent-skills** | `19c01cd` 2026-06-10 | **None** | No | 1,251 "rubric-blind" skills from Parthenon/Harvey-LAB paper; real-estate/identify-issues-in-estoppel-certificate exists | Avoid (no licence) | Cited |
| **CSlawyer1985/legal-skillhub**, `lev-os/agents`, `lionelsimai/...` | 2026 | None / MIT | — | Re-hosts of CaseMark skills | Use CaseMark upstream | Cited (API) |
| **Klotzkette/claude-fuer-deutsches-recht** | 2026-10-23 | Apache-2.0 | Yes | German law; not relevant | — | Cited (API) |
| OpenAI cookbook / LangChain templates / LlamaIndex packs | — | — | — | **No official legal-contract examples found** (cookbook search: only unrelated "contract" hits; LlamaIndex: a SambaNova vendor notebook) | Nothing | Cited (negative) |
| **CommonAccord/Cmacc-Org** | pushed 2026-10-31 | MIT | Yes | Prose-object library incl. `Doc/G/NW-NDA`, `Doc/G/TechContracts/Assignment-NDA`, NVCA 2017/2018 SPA decompositions, ABA MSPA; clause-level decomposition model | Clause decomposition idea; NDA variants | Cited (API) |

## B. Open standard agreements / model positions

| Source | Licence | Commercial adaptation? | Covers | Tag |
|---|---|---|---|---|
| **Common Paper** standards (commonpaper.com/standards) | **CC BY 4.0** ("free to use and modify") | Yes, attribute + mark modified | Mutual NDA v1.0, One-way NDA, Independent Contractor Agreement, CSA, DPA, BAA, PSA, SLA, AI Addendum, Software License, Pilot, Partnership, ToS, LOI, SOW, Order Form. No real estate, no employment beyond contractor. | Cited |
| **Bonterms** (bonterms.com/legal-center §2.1) | Designated licence **CC BY 4.0** if none stated; OpenAgreements records the Mutual NDA *cover-page example* as **CC0 1.0** — verify per document | Yes | Mutual/One-way NDA v1.0, Cloud Terms, PSA, DPA/BAA, SLA, Software License Terms ("13 standards") | Cited |
| **oneNDA / oneSaaS / oneDPA** (Law Insider) | **CC BY-ND 4.0**; FAQ: alterations must strip oneNDA branding | **Derivatives prohibited** → reference/benchmark only | Mutual NDA, SaaS, DPA, oneNDA Playbook | Cited |
| **NVCA Model Legal Documents** | "FREE DOWNLOADS", © NVCA All Rights Reserved, no licence | Download/use yes; **redistribution not granted** (OpenAgreements treats as non-redistributable) | Financing docs (CoI, SPA, IRA, Voting 06/2026, ROFR 04/2026), Life-Science CDA (07/2025) | Cited |
| **Y Combinator SAFE** | © YC, "free to download and use", no licence text; OA labels CC-BY-ND-4.0 | Use yes; modify discouraged ("keep unmodified") | SAFEs, side letters | Cited |
| **Series Seed** (seriesseed/equity) | **CC0** | Yes | Seed financing docs | Cited (API) |
| **SALI LMSS** (github.com/sali-legal/LMSS) | **MIT** | Yes | 18k-tag legal matter taxonomy (areas of law, doc types); useful for skill metadata, not positions | Cited |
| **Contract Standards / Law Insider clause library** | Not verifiable today (TLS error / no terms found) | Unknown → treat as reference-only | Clause library | Inferred |
| **Balanced Employee IP Agreement** (GitHub), **Papertrail legal-docs** | CC0 1.0 (per OA source policy) | Yes | Employee IP/invention assignment; employment forms | Secondary |
| **University mutual NDAs** (Ohio AG-approved template; Boise State 11-2023 DOCX; UT Arlington) | No licence stated (public-institution docs) | Reference | Mutual NDA | Secondary |
| **Government model clauses** — GSA L100 Global Lease (rev. 5/26), GSA Form 3517B General Clauses (rev. Apr 2026), L201 Simplified | U.S. Government work → public domain | Yes | Office lease (landlord-side federal tenant) | Cited (PDF URLs) / Inferred (PD status) |
| **ABA model forms** (MAPA/MSPA etc.) | ABA copyright, sold | No | — | Inferred |

## C. Practice-specific references

### C1. NDAs
- **18 U.S.C. §1833(b)** DTSA whistleblower immunity + notice duty; cross-reference-to-policy compliance; loss of exemplary damages/fees if omitted; applies to agreements entered/updated after 2016-05-11. law.cornell.edu/uscode/text/18/1833; govinfo. Public domain. Cited (search snippets of statute) / statute text Inferred.
- Common Paper + Bonterms NDAs (CC BY 4.0 / CC0) and OpenAgreements `skills/agreements/nda` as baseline positions; LegalQuants `playbooks/nda/playbook.yaml` (Apache) for position schema.
- **ContractNLI** 607 NDAs, 17 hypotheses, **CC BY 4.0** (stanfordnlp.github.io/contract-nli; GitHub LICENSE CC-BY-4.0). Cited.
- CUAD (CC BY 4.0) has no NDA type but 41 clause categories incl. non-compete, no-solicit, confidentiality-adjacent; LegalBench `cuad_*` 38 tasks CC BY 4.0. Cited.
- SEC Rule 21F-17 whistleblower carve-out (relevant to NDA/separation confidentiality) — Inferred.

### C2. Employment
- **OWBPA 29 U.S.C. §626(f)**; **29 CFR 1625.22** (waivers; group-program disclosure; appendix sample) — ecfr.gov blocked the fetch; Inferred (public domain).
- **EEOC Q&A "Understanding Waivers of Discrimination Claims in Employee Severance Agreements"** (EEOC-NVTA-2009-2, 2009-07-15; App. B rev. Apr 2010): 7 OWBPA requirements, 45-day/decisional-unit/job-title-and-age disclosure, **Appendix A employee checklist, Appendix B sample group release + disclosure table**. eeoc.gov/node/130190. Public domain. Cited.
- **DOL worker classification:** NPRM 2026-02-27 (FR 2026-03962) to rescind the 2024 rule and restore modified 2021 "core factors" test; comments closed 2026-04-28; **no final rule in the Federal Register as of 2026-10-31** (FR API newest WHD docs: joint-employer NPRM 2026-04-23, IC NPRM 2026-02-27). 2024 rule nominally in effect; WHD stopped applying it in investigations (May 2025). Cited.
- **FTC non-compete rule:** appeals dropped Sept 2025; removed from CFR eff. 2026-02-12; case-by-case enforcement. Secondary (Foley 2026-07).
- **50-state non-compete surveys:** OpenAgreements `surveys/non-compete/us.md` (56 jurisdictions, 16 columns, Apache-2.0, linked per-state guides) — the only openly licensed one. Firm surveys are © and reference-only: Beck Reed Riden chart (updated 2026-07-01), Katz Banks Kumin statutory map (data as of 2026-03-01, data download offered), Epstein Becker Green 50-state survey (updated 2026-04-21, free PDF). Cited/Secondary.
- **cedarstoneventures/us-noncompete-enforceability** (HF, **CC BY 4.0**, snapshot 2026-07-05): 76 verified court holdings, 30 states, CourtListener-sourced, verbatim holding text + outcome labels. Cited.
- State developments 2026 to encode: WA expansive law (voids existing covenants mid-2027), TN $70k floor eff. 2026-07-01, UT healthcare ban, VA severance-or-void, FL CHOICE Act (2025). Secondary.
- NLRB *McLaren Macomb* (2023) still operative for severance confidentiality/non-disparagement; GC Memo 23-05 rescinded; Board signalling revisit (Prime Communications Apr 2026; case 10-CA-324512). Secondary.
- OpenAgreements per-state offer letters (CA, FL, IL, MA, NY, PA, TX), CIIA (general + Nevada), NY wage notice, `practice-guides/employment-offer-letter/us/classification-and-companion-documents.md`, `surveys/{wage-and-hour,stay-or-pay,invention-assignment}`. Cited.

### C3. Commercial Real Estate leases
- **GSA lease templates** (public domain): Global Lease L100 rev. 5/26 (gsa.gov/system/files/Global%20Lease%20Template%20L100%20%28May%202026%29.pdf), Form 3517B General Clauses rev. Apr 2026, L201 Simplified; 2026 revision cut ~20 pages. Cited (PDF URL) / Secondary (3517B date).
- **SEC EDGAR Full-Text Search** (filings since 2001) for Exhibit 10 office/retail/industrial leases, SNDAs, estoppels; sec.gov content "may be copied... without the SEC's permission" but exhibits may carry filer copyright (ISDA v. Socratek, S.D.N.Y. 2010). Cited (SEC policy) / Secondary (case).
- **BOMA** ANSI/BOMA Z65.1-2024 Office: paid ($85–$119), © all rights reserved — cite by name only. **AIR CRE** forms: proprietary software/membership. Secondary.
- Skill scaffolds: mariourquia (abstract 30+ fields; retail adds co-tenancy, kick-out, exclusive use, radius, percentage rent), CaseMark (office/retail/industrial/NNN/ground lease; SNDA; tenant estoppel; lease summary), ThomasMoreAI `us/real-estate` mirrors incl. `commercial-lease-analyzer-onewave-ai` (MIT), `lease-compliance-auditor-fdu-ins` (Apache), `negotiate-lease-jeffreytse` (MIT). Cited.
- Open lease-abstract templates: only vendor freebies (Occupier) with no licence — treat as reference. Secondary.
- Datasets: none U.S.-commercial-lease-specific. `HaseebDev/lease_agreement_disputes` is CC BY-NC-4.0 (synthetic disputes) — not usable. LEDGAR (EDGAR provisions) includes lease-type provisions. Cited.

### C4. Legal invoice / outside-counsel billing review
- **LEDES 98B** spec "Ledes 1998B Format Rev 7-2014" (.xls) + "Ledes98B Sample File Rev 7-2014" (.txt): direct downloads, no login; 24 pipe-delimited fields; format frozen. 98BI (51 fields), 2000, XML 2.0/2.1/2.2, Budgeting, Timekeeper Attributes, API v1, Error Codes. ledes.org © 2000–2025 LOC; Terms of Use: "You may download materials...", "only designed to serve as a guide"; **no explicit implementation/redistribution licence**; LEDES® mark may not appear in product names. Cited.
- **UTBMS code sets** (utbms.com, © LOC 1998–2025, ABA sets "reproduced with permission of the ABA"): ABA Litigation/Counseling/Project/Bankruptcy/Activity/Expense; LOC Revised Activity/Expense (2013), Project; LOC eDiscovery; DRI Litigation; **LOC IP Patent Prosecution Codes Apr 2023 ("110" codes; phase → deliverable hierarchy; white paper PDF + Excel; update 2024-06-10)**; LOC Patent/Trademark 2009; Real Estate, M&A, GRC, Criminal, Workers' Comp sets; Timekeeper Classification 2014. No stated licence → implement the codes as facts; avoid republishing ABA descriptive text verbatim. Cited / Secondary.
- **Published OCGs (public sector, public records):** FDIC Outside Counsel Deskbook rev. Nov 2025 + e-billing appendix (fdic.gov/doing-business-fdic/outside-counsel-deskbook.pdf); NJ Attorney General Division of Law OCG (eff. 2015-01-01); City of Chicago DOL OCG amended 2024-03-15; Penn State OCG eff. 2025-09-04; URI OGC Protocol 03 rev. 2024-04-24; Berks County PA; HESAA 2020. Corporate examples (Visa 2017, First Citizens 2023, Aptim) are © but public. ACC billing-guidelines template and Value Challenge toolkit (2009) are members-only. Secondary.
- Skills: Ditzler (Apache) OCG rule schema; Margetts (NC-ND, structure only); judicialmind (MIT); mini-claude invoice checklist (MIT, MENA lens). Cited.
- Datasets: **none public for LEDES invoices**. `ClarusC64/legal-billing-narrative-*` (MIT) = 10 synthetic narratives with coherence labels (format idea only). `karlrees/ledestool` (GPL-3.0, 2020) converts 98B↔98BI — GPL, do not vendor. Synthetic LEDES sets must be generated from the LOC sample file. Cited.

### C5. U.S. patent applications
- **MPEP** Ninth Edition, Rev. 01.2024 (pub. Nov 2024, current as of 2024-01-31); superseding memos 2025-01-14, 2025-01-17, 2025-10-24 (×2), 2025-12-05 (*Ex parte Desjardins* §101); consolidated rules current as of 2026-07-20. Chapter 2100 (§112 at 2161–2185 — Inferred). USPTO terms: government works "not subject to copyright restrictions", acknowledgement requested; patent text/drawings typically uncopyrighted. Cited.
- **WIPO Patent Drafting Manual, 2nd ed.** (Pub. 867, 2022/2023, DOI 10.34667/tind.44657): **CC BY 4.0** incl. commercial adaptation with credit + change notice. Best open claim-drafting/specification pedagogy. Secondary (front-matter quote via search).
- **USPTO datasets (public domain / CC Public Domain Mark):** Office Action Research Dataset 2008–2017 (4.4M OAs, 2.2M apps; rejection types §101/102/103/112) at data.uspto.gov/bulkdata/datasets/ptoffact; Office Action Rejection/Citation APIs; **PatEx** 2001–2023 (13M+ apps, transaction history; updated 2026-01-20); **Patent Claims Research Dataset** (grants 1976–2014, pubs 2001–2014; parsed claims + dependency; page updated 2026-09-03; "cite this documentation"). Cited (USPTO pages) / Secondary (licence via data.gov).
- **PTAB** decisions and PTAB API (developer.uspto.gov) — public domain. Inferred.
- **LOC IPMM Invention Disclosure Schema** (ledes.org; XML schema for invention disclosures, free access, no version shown) — a ready field model for invention-disclosure intake. Cited.
- **PatentsView** redirected to data.uspto.gov transition guide (empty fetch) — Inferred CC BY 4.0 historically. **Google Patents Public Data** (BigQuery) CC BY 4.0 per Google blog caption. Secondary.
- Skills: ThomasMoreAI `us/ip` mirrors: `patent-application-creator` (RobThePCGuy, MIT, 2,778 words), `patent-reviewer` (MIT), `patent-novelty-check` (wanshuiyin, MIT); CaseMark patent-summary / claim-construction-chart / patent-infringement-analysis (Apache); harness-100 (KR-style claims — orchestration only); claude-for-legal `ip-legal` (fto-triage, infringement-triage; Apache). No open **office-action-response** or **§112 QC** skill of quality found. Cited.

## D. Datasets (evaluation / examples)

| Dataset | Where | Licence | Commercial? | Relevance | Tag |
|---|---|---|---|---|---|
| CUAD (510 contracts, 41 categories) | HF `theatticusproject/cuad`, `cuad-qa` | CC BY 4.0 | Yes (no warranty on underlying EDGAR contracts) | Non-compete, no-solicit, IP, term/renewal clause spotting | Cited |
| MAUD | HF `theatticusproject/maud` | CC BY 4.0 | Yes | M&A only — low relevance | Cited |
| ACORD (126k query-clause pairs) | HF `theatticusproject/acord` (2025-01-15) | CC BY 4.0 | Yes | Clause retrieval evals (clause-library search) | Cited |
| ContractNLI (607 NDAs, 17 hypotheses) | stanfordnlp.github.io/contract-nli; GitHub | **CC BY 4.0** (HF mirror `kiddothe2b` mislabelled NC-SA) | Yes | **Primary NDA eval set** | Cited |
| LegalBench (162 tasks) | HF `nguha/legalbench` (card cc-by-4.0, updated 2026-03-30); GitHub task READMEs | **Per task**: `cuad_*` (38) CC BY 4.0; `maud_*` (34) CC BY 4.0; `contract_nli_*` (14) CC BY 4.0; `contract_qa` CC BY 4.0; `insurance_policy_interpretation` CC BY 4.0; **`consumer_contracts_qa` CC BY-NC 4.0**; **`learned_hands_*` (16, incl. `learned_hands_employment`) CC BY-NC-SA 4.0**; `proa`, `unfair_tos`, `opp115_*`, `ssla_*`, `privacy_policy_*` — licence not stated in README (check task page) | Mixed | Use cuad_/contract_nli_/contract_qa; exclude learned_hands/consumer_contracts_qa | Cited |
| LegalBench-RAG | `thethomasmore/legalbench-rag-bilingual_v1_public` Apache-2.0; `orgrctera/legalbenchrag_*`; original (ZeroEntropy) reported CC-BY-4.0 | Apache / CC BY | Yes | RAG retrieval evals over CUAD/ContractNLI/MAUD | Secondary |
| LEDGAR (EDGAR provisions, 12.5k labels) | via HF `coastalcph/lex_glue` (CC BY 4.0); standalone mirrors `namban/ledgar`, `MAdAiLab/lex_glue_ledgar` (no licence) | LexGLUE CC BY 4.0; LREC paper CC BY-NC (paper, not data); source = public SEC filings | Yes via LexGLUE | Provision classification incl. lease-type clauses | Cited/Secondary |
| EDGAR-CORPUS (10-K 1993–2020) | HF `eloukas/edgar-corpus` | Apache-2.0 | Yes | 10-K text only, no exhibits | Cited |
| HUPD (4.5M apps 2004–2018, 34 fields, decision labels) | HF `HUPD/hupd` (125 GB) | **Conflicting**: metadata `cc-by-sa-4.0`; card body "CC BY-NC-SA 4.0"; paper CC BY-SA | **Treat as NC until clarified**; underlying USPTO data is public domain | Patent drafting/outcome evals | Cited |
| BIGPATENT (1.3M, 1971–2018 summarisation) | HF `NortheasternUniversity/big_patent` (2025-09-25) | CC BY 4.0 | Yes | Summaries of descriptions | Cited (API) |
| PatentMatch (TU Berlin) | HF `BNNT/PatentMatch` mirror | apache-2.0 (mirror); original terms unverified | Probably | Claim ↔ prior-art passage novelty | Secondary |
| USPTO Office Action Research Dataset 2008–2017; PatEx 2001–2023; Patent Claims Research Dataset | data.uspto.gov/bulkdata | Public domain (CC PDM 1.0 on data.gov) | Yes | §101/102/103/112 rejection patterns; claim statistics | Cited/Secondary |
| `deter3/office-action-triplets-*` | HF | **No licence** | No | — | Cited |
| cedarstoneventures/us-noncompete-enforceability | HF (2026-07-05) | CC BY 4.0 | Yes | 76 verified holdings, 30 states | Cited |
| Mr-Dintov/Legal-Contract-Clause-Risk-Corpus | HF (sample: 1 row; full on request) | CC BY 4.0 | Yes | Synthetic PSA clauses — low value | Cited |
| ClarusC64/legal-billing-narrative-* (4 sets) | HF (2026-02) | MIT | Yes | 10 synthetic narratives each; format idea | Cited |
| HaseebDev/lease_agreement_disputes, employment_contract_disputes | HF (2026-10-08) | CC BY-NC 4.0 | **No** | — | Cited |
| Pile of Law; `lexlms/lex_files` | HF | CC BY-NC-SA 4.0 | **No** | — | Cited |
| NDA / lease / employment-agreement / LEDES invoice corpora | HF search | **None exist** beyond the above | — | Gap: build from EDGAR + Common Paper/Bonterms + synthetic LEDES | Cited (negative) |

## E. Best-5 per practice and gaps

### E1. NDAs
1. **OpenAgreements** `skills/agreements/nda` + Common Paper Mutual/One-way NDA (CC BY 4.0) + Bonterms NDA (CC0/CC BY) — baseline balanced positions and cover-page variable model.
2. **LegalQuants `playbooks/nda*.yaml`** (Apache) — position/fallback/redline schema with 8 seeded issues.
3. **ContractNLI** (CC BY 4.0) — 607 real NDAs + 17 hypotheses for review-accuracy evals; LegalBench `contract_nli_*`.
4. **mike-workflows nda-review / nda-tabular-review** (MIT) + claude-for-legal nda-review (Apache) — checklist matrix and process.
5. **18 U.S.C. §1833(b)** DTSA notice + oneNDA (CC BY-ND, benchmark-only) for market-standard comparison.
**Attorneys must author:** Spaarke default positions (discloser vs recipient), residuals-clause policy, U.S. state-law choice table, DTSA/21F-17 carve-out language, escalation thresholds.

### E2. Employment
1. **OpenAgreements** non-compete 56-jurisdiction survey + per-state restrictive-covenant/offer-letter templates + checklists + currency eval (Apache/CC BY 4.0).
2. **EEOC OWBPA Q&A** (2009; App. B sample group release + disclosure table) + 29 CFR 1625.22 + 29 U.S.C. §626(f) — public domain.
3. **CaseMark** separation-agreement / employee-separation-release / non-compete / independent-contractor / executive-employment (Apache) — drafting article maps with 21/45/7-day table.
4. **claude-for-legal employment-legal** (worker-classification, Apache) + DOL 2026 NPRM / 2024 rule / IRS factors for classification.
5. **cedarstoneventures/us-noncompete-enforceability** (CC BY 4.0) + firm surveys (BRR 2026-07-01, KBK 2026-03-01, EBG 2026-04-21) as read-only cross-checks.
**Attorneys must author:** restrictive-covenant positions by state tier, OWBPA group-termination disclosure QA rules, worker-classification decision tree reflecting the pending DOL final rule, McLaren Macomb-compliant confidentiality/non-disparagement language, state WARN overlays.

### E3. CRE leases
1. **GSA Global Lease L100 (5/26) + Form 3517B** — public-domain full office lease and general clauses (tenant-favourable federal baseline).
2. **mariourquia/cre-skills-plugin** (Apache) — lease abstract 30+ fields (+retail fields), estoppel generator, CAM reconciliation, compliance audit.
3. **CaseMark** office / retail / industrial / NNN / ground lease + SNDA + tenant-estoppel + lease-summary (Apache) — article frameworks and intake checklists.
4. **SEC EDGAR Full-Text Search** Exhibit 10 leases/SNDAs/estoppels — real U.S. examples for evals (filer copyright caveat).
5. **LEDGAR via LexGLUE** (CC BY 4.0) — provision classification incl. lease-type provisions; ACORD for clause retrieval.
**Attorneys must author:** everything substantive — landlord/tenant position playbooks for office/retail/industrial, abstract schema aligned to Spaarke ontology, estoppel/SNDA review checklists (what to refuse, what to confirm), BOMA/rentable-area sanity rules (cite BOMA by name only), market-standard escalations/caps.

### E4. Invoice / outside-counsel billing review
1. **LEDES 98B spec + sample file** (ledes.org; free) and 98BI/XML 2.2 — field-level validation rules.
2. **UTBMS code sets** incl. **LOC IP Patent Prosecution Codes 2023/2024** (utbms.com) — code validity, phase/task consistency.
3. **FDIC Outside Counsel Deskbook rev. Nov 2025** (public domain) + NJ AG / Chicago 2024 / Penn State 2025 OCGs — real billing-rule corpora to mine for rule categories and thresholds.
4. **Carl Ditzler outside-counsel-billing-and-performance-reviewer** (Apache-2.0 inside lawve-ai) — OCG rule schema, metrics catalog, failure modes, safe-to-embed source policy.
5. **judicialmind billing-operations** (MIT) + mini-claude invoice checklist (MIT) for time-entry quality heuristics; Margetts skills for *structure* only (NC-ND).
**Attorneys must author:** Spaarke OCG rule set (block billing, vague narratives, staffing ratios, travel, research, rounding, timekeeper-rate cap), severity/remediation matrix, appeal letter templates, synthetic LEDES eval invoices (none exist publicly), UTBMS-code-to-matter-type expectations.

### E5. U.S. patent applications
1. **MPEP 9th ed. rev. 01.2024** (+2025 superseding memos; ch. 2100 §§2161–2185 for §112) — public domain examiner standard for QC rules.
2. **WIPO Patent Drafting Manual 2nd ed.** (CC BY 4.0) — claim-drafting pedagogy adaptable commercially with attribution.
3. **USPTO Office Action Research Dataset + PatEx + Patent Claims Research Dataset** (public domain) — rejection-pattern evals, claim statistics, prosecution timelines.
4. **LOC IPMM Invention Disclosure Schema** + **LOC IP Patent Prosecution Codes 2023** — invention-disclosure field model and prosecution phase taxonomy.
5. **ThomasMoreAI `us/ip` mirrors** (`patent-application-creator`, `patent-reviewer`, MIT) + claude-for-legal `ip-legal` (Apache) + HUPD/BIGPATENT for examples (HUPD licence unclear → NC assumption).
**Attorneys must author:** §112(a)/(b)/(f) QC checklist (antecedent basis, written description/enablement, means-plus-function flags), claim-set architecture rules, office-action response strategy per rejection type, invention-disclosure interview questions, §101 *Desjardins*-era eligibility flags.

## Licence red flags

- **lawve-ai/awesome-legal-skills** repo licence is **CC BY-NC-ND 4.0**; many skills (incl. the 7k-word invoice-review-compliance) have no own LICENSE and inherit it. Jamie Tso NDA reviewer is **AGPL-3.0**. Only skills with their own permissive LICENSE (Ditzler Apache; anthropic mirror Apache) are usable.
- **borghei/Claude-Skills**: MIT + **Commons Clause** → no commercial sale. **TapnTell-AI**: no licence. **CSlawyer1985**: no licence (re-host).
- **oneNDA / oneSaaS / oneDPA / oneNDA Playbook**: **CC BY-ND** → no adapted versions; benchmark only. **YC SAFE**: effectively ND.
- **NVCA**: free download, "All Rights Reserved", not redistributable; use field-selectors/pointers only (OpenAgreements pattern).
- **HUPD**: metadata CC BY-SA vs card body CC BY-NC-SA → treat as non-commercial until the maintainers clarify.
- **LegalBench**: `learned_hands_*` CC BY-NC-SA; `consumer_contracts_qa` CC BY-NC; several tasks have no licence line in their README.
- **HF mirrors mislabel**: `kiddothe2b/contract-nli` says NC-SA but the dataset is CC BY 4.0; `HaseebDev/*` and Pile-of-Law are NC.
- **LEDES/UTBMS**: no explicit licence; LEDES® may not appear in product names; ABA code text reproduced "with permission" — implement codes as facts, don't republish ABA prose.
- **Firm 50-state surveys** (BRR, KBK, EBG): copyrighted; read-only cross-check, never redistribute.
- **BOMA / AIR CRE / ABA forms / ACC templates**: paid or members-only; cite by name only.
- **karlrees/ledestool**: GPL-3.0 — do not vendor.
- **Bonterms** licence varies by document (legal-center default CC BY 4.0; some cover-page examples CC0) — record per document.
- **SEC EDGAR exhibits**: SEC content free to reuse, but filer-authored exhibits may carry copyright (ISDA v. Socratek) — fine for evals/training examples, risky for verbatim redistribution as templates.

## Reference links (primary)

- https://github.com/open-agreements/open-agreements (Apache-2.0, `50ae131`)
- https://github.com/CaseMark/skills (Apache-2.0, `51b5e32`) · https://github.com/ThomasMoreAI/legal-skills-open (`2896ce4`) · https://github.com/LegalQuants/lq-ai (`755abc6`) · https://github.com/mariourquia/cre-skills-plugin (`e4597f1`) · https://github.com/lawve-ai/awesome-legal-skills (`f9f28cd`, CC BY-NC-ND) · https://github.com/evolsb/claude-legal-skill (`bca0d2e`) · https://github.com/microsoft/cat-agent-skills (`3678d23`) · https://github.com/judicialmind/legal-skills (`916ec5a`) · https://github.com/sboghossian/mini-claude-for-legal (`a3bdc4e`) · https://github.com/revfactory/harness-100 (`8e8d35c`) · https://github.com/CommonAccord/Cmacc-Org
- https://commonpaper.com/standards/ · https://bonterms.com/legal-center · https://onenda.org/faq · https://nvca.org/model-legal-documents/ · https://www.ycombinator.com/documents · https://github.com/seriesseed/equity · https://github.com/sali-legal/LMSS
- https://www.law.cornell.edu/uscode/text/18/1833 · https://www.eeoc.gov/node/130190 · https://www.federalregister.gov/documents/2026/02/27/2026-03962/... (IC NPRM) · https://www.ecfr.gov/current/title-29/.../section-1625.22 · https://beckreedriden.com/tag/50-state-chart/ · https://katzbanks.com/noncompete-map/ · https://www.ebglaw.com/trade-secrets-employee-mobility/50-state-noncompete-survey-updated-amid-growing-state-restrictions · https://huggingface.co/datasets/cedarstoneventures/us-noncompete-enforceability
- https://www.gsa.gov/system/files/Global%20Lease%20Template%20L100%20%28May%202026%29.pdf · https://www.sec.gov/edgar/search/ · https://www.sec.gov/about/privacy-information#dissemination · https://standards.boma.org/
- https://ledes.org/ledes-98b/ · https://ledes.org/terms-of-use/ · https://utbms.com/ · https://utbms.com/wp-content/uploads/2023/04/IP-Patent-Prosecution-Codes-Apr-2023.pdf · https://ledes.org/ipmm/ · https://www.fdic.gov/doing-business-fdic/outside-counsel-deskbook.pdf · https://www.nj.gov/oag/law/pdf/rfqs/oag-dol-Outside-Counsel-Guidelines.pdf · https://chicago.gov/content/dam/city/depts/dol/OutsideCounselOpenHouse/Outside%20Counsel%20Guidelines%20-%20%20Amended%203.15.24.pdf · https://ogc.psu.edu/assets/uploads/documents/2025-Penn-State-Outside-Counsel-Guidelines.pdf
- https://www.uspto.gov/web/offices/pac/mpep/index.html · https://www.uspto.gov/terms-use-uspto-websites · https://www.wipo.int/edocs/pubdocs/en/wipo-pub-867-23-en-wipo-patent-drafting-manual.pdf · https://catalog.data.gov/dataset/patent-application-office-actions-research-dataset-for-academia-and-researchers-2008-2017 · https://catalog.data.gov/dataset/patent-examination-research-dataset-patex-for-academia-and-researchers-2001-2021 · https://www.uspto.gov/ip-policy/economic-research/research-datasets/patent-claims-research-dataset
- https://huggingface.co/datasets/theatticusproject/{cuad,cuad-qa,maud,acord} · https://stanfordnlp.github.io/contract-nli/ · https://github.com/stanfordnlp/contract-nli · https://huggingface.co/datasets/nguha/legalbench · https://hazyresearch.stanford.edu/legalbench/tasks/ · https://huggingface.co/datasets/coastalcph/lex_glue · https://huggingface.co/datasets/HUPD/hupd · https://huggingface.co/datasets/NortheasternUniversity/big_patent · https://huggingface.co/datasets/BNNT/PatentMatch · https://huggingface.co/datasets/eloukas/edgar-corpus

## Still uncertain
- Bonterms per-document licence (CC BY 4.0 default vs CC0 cover-page examples).
- HUPD actual licence (SA vs NC-SA).
- LEDGAR standalone licence (only LexGLUE's CC BY 4.0 verified).
- Whether the DOL will finalize the IC rule before year-end; whether NLRB overrules McLaren Macomb.
- LegalBench tasks without a licence line (`proa`, `unfair_tos`, `opp115_*`, `ssla_*`, `privacy_policy_*`).
- PatentsView current terms after migration to data.uspto.gov.
- Quality of OpenAgreements' per-state content has not been attorney-checked by me; its CI eval only checks currency of citations.

## Proposed researcher memory entry (main session to apply — sub-agent cannot write `.claude/`)
File: `.claude/agent-memory/researcher/legal-skill-content-sources-2026-10-31.md`; index line: `- [Legal skill content sources (2026-10-31)](legal-skill-content-sources-2026-10-31.md) — OpenAgreements Apache = best NDA/employment scaffold + 56-jx noncompete survey; ContractNLI is CC BY 4.0 (not NC); lawve-ai is CC BY-NC-ND; invoice/patent/CRE skills thin → LEDES/UTBMS/FDIC, MPEP/WIPO-CC-BY/USPTO PD data, GSA L100`.
