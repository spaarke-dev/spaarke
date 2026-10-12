# Legal Front Door intake wizards: market research and form design

> Project `spaarke-SPA-external-access-platform-r3`, tasks 052/053, spec FR-05.
> Researched and written 2026-10-11 by the researcher subagent. This is a proposal for owner approval (task 052 step 1, task 053 equivalent), not a decision.
> Storage contract: one `sprk_servicerequest` per submission. `sprk_requesttype` is the type, `sprk_name` is the title (max 850), and `sprk_intakedata` holds the answers as one JSON object (max 100 KB, field-secured). Documents are added after submission from the request's Documents section (task 054); they are never part of the JSON.

---

## 0. How to read this

- **Sources** are numbered `[S#]` and listed in section 7 with URL and retrieval date. All were retrieved on 2026-10-11. Sources marked *(index)* could not be fetched directly (HTTP 403, or PDF/JS-only), so the claim comes from the search engine's summary of that page. Treat those as weaker.
- **Control types** use these names: `text`, `multiline`, `choice` (single select: radio or dropdown), `multi-choice`, `date`, `yes/no` (or `yes/no/unsure`), `number`, `person` (free-text name, optional email), `list` (repeatable rows).
- **Required** means the Next button stays disabled until the field is valid. "Core optional" fields are shown in v1 but not required. "Nice to have" fields are left out of v1 unless the owner asks for them.
- **JSON** keys are camelCase. Choice values are stored as stable codes such as `"mutual"`, not as labels (see section 5.3).

### 0.1 What the market does (cross-type summary)

- **Request type first, then a short type-specific form.** Onit Unity LSR ships 16 request templates, including NDA, Policy/Compliance Question, Intellectual Property Request and Third-party NDA [S6]. SpotDraft has the requester pick an "intake workflow" that matches the request [S10]. Brightflag lets admins define request types with required, date, numeric, dropdown, conditional-dropdown and file-upload fields [S9]. Spaarke's four quick-start cards already follow this pattern.
- **Every form shares a small core.** Onit's default fields on every request are Title and Description [S8], and a requester "can assign it a priority and business impact category, enter a requested due date" [S7]. Checkbox's per-type templates repeat requester and department, a description, and either a deadline or an urgency level [S3].
- **Keep forms short and use branching.** Streamline AI says to "only ask for the questions that you need", notes that requesters are more likely to finish a 10-field form than a 30-field one, and recommends if/then logic over separate forms [S11]. Tonkean shows "the few relevant fields" and adds more depending on the answers [S12]. Checkbox recommends asking only questions that change the legal outcome [S2].
- **Attachments matter, but in the right place.** Streamline recommends letting requesters attach third-party paper and supporting material [S11]. In Spaarke this happens after submission (task 054), so the wizard's job is to tell the requester to do it.
- **CLOC:** I found no published CLOC field standard. CLOC's public material is roundtable listings on intake channels, request sources and routing [S14]. Vendor guidance is the de facto standard.

---

## 1. NDA Assessment

### 1.1 What the market asks

| Product | Fields asked | Source |
|---|---|---|
| ServiceNow Legal (Simple Contracts) | Third-party category (Vendor / Customer / Partner), purpose of the agreement, company legal name (picker, or "not listed" plus a new name), address, country, start date, signature type, external signatories | [S1] |
| Checkbox (self-service NDA guide) | Requester, counterparty, business purpose, NDA type (mutual / one-way), disclosure direction, timing, **whether the counterparty supplied its own paper**. Routes to Legal instead of auto-issuing when there is regulated information, an unusual jurisdiction, counterparty paper or unusual terms | [S2] |
| Checkbox (form templates) | Requester and department, counterparty name and contact, purpose, term length, who has signing authority, deadline if any | [S3] |
| Ironclad (Basic NDA recipe) | Counterparty name, counterparty signer name and email. Pre-approved choices for duration and governing law; a non-standard choice routes to Legal. Supporting-document upload | [S4] *(index)* |
| Ontra (NDA automation) | Document-first: the incoming counterparty NDA is forwarded to an inbox, key terms are extracted, and the NDA is marked up against the customer's playbook | [S5] *(index)* |
| Onit Unity LSR | Separate "NDA" (generate ours or review a third-party NDA) and "Third-party NDA" templates | [S6] |

**Takeaways**

1. Every product asks for the **counterparty**, the **purpose**, and **mutual vs one-way**.
2. **Whose paper** is the biggest routing question. Our template means a fast path; their paper means a lawyer reviews it, and the draft has to be attached.
3. "Mutual vs one-way" alone does not say enough. A one-way NDA can protect us (we disclose) or bind us (they disclose), and the legal risk is opposite. Checkbox asks for "disclosure direction" as well as the type [S2]. Spaarke should ask the direction, from which mutual or one-way follows.
4. Signer details, address, term and governing law are needed to **generate** an NDA. They are not needed to **triage** an assessment, and the draft shows them anyway. They are nice to have.
5. Counterparty category (customer / vendor / partner) is cheap to ask and helps routing and reporting [S1].

### 1.2 Recommended field set (3 steps)

**Step 1: Counterparty**

| JSON key | Label | Control | Req? | Validation | Help text | Conditional |
|---|---|---|---|---|---|---|
| `counterpartyName` | Who is the other party? | text (organization) | Yes | 2–200 chars, trimmed | Full legal name if you know it, e.g. "Contoso Ltd". For a confidential deal you can use the project codename. | — |
| `counterpartyType` | What is your relationship with them? | choice | Yes | one of the codes | — | Options: `customer` Customer or prospect · `vendor` Vendor or supplier · `partner` Partner or collaborator · `investment` Investment, acquisition or financing · `individual` Individual (consultant, candidate, advisor) · `other` Other |
| `counterpartyTypeOther` | Describe the relationship | text | Yes, when shown | 2–200 | — | Show when `counterpartyType = other` |
| `counterpartyCountry` | Where is the other party based? | text | No (core optional) | ≤ 100 | Country or state. Helps Legal choose the governing law. | — |

**Step 2: Purpose and terms**

| JSON key | Label | Control | Req? | Validation | Help text | Conditional |
|---|---|---|---|---|---|---|
| `purpose` | What is the NDA for? | multiline | Yes | 10–1,000 | What will be discussed or shared, and why, e.g. "Evaluate their analytics platform for the Q3 pilot". | — |
| `disclosureDirection` | Who will share confidential information? | choice | Yes | code | Sets whether the NDA is mutual or one-way. | `mutual` Both sides · `weDisclose` Only us · `theyDisclose` Only them · `unsure` Not sure |
| `paperSource` | Whose NDA will be used? | choice | Yes | code | — | `ours` Our standard NDA · `theirs` They sent their own draft · `unsure` Not sure yet |
| *(no field)* | Information banner | — | — | — | "After you submit, open the request and add their draft under **Documents**. Legal can't review it without the file." | Show when `paperSource = theirs` |
| `sensitiveInfo` | Will any of the following be shared? | multi-choice | No (core optional) | codes; `none` excludes the others | Tick everything that applies. | `personalData` Personal or customer data · `sourceCode` Source code or technical designs · `financial` Non-public financial information · `regulated` Regulated or export-controlled information · `none` None of these |

**Step 3: Timing and review**

| JSON key | Label | Control | Req? | Validation | Help text | Conditional |
|---|---|---|---|---|---|---|
| `neededBy` | When do you need it signed? | date | No | today ≤ date ≤ today + 365 days | Leave blank if there is no fixed date. | — |
| `neededByReason` | What's driving the date? | text | No | ≤ 300 | e.g. "Kick-off meeting on the 14th". | Show when `neededBy` is within 3 business days |
| `notes` | Anything else Legal should know? | multiline | No | ≤ 2,000 | e.g. terms they have already asked for. | — |
| *(review)* | Summary of the answers, read-only | — | — | — | — | — |

**Nice to have (not in v1):** `counterpartySignerName` / `counterpartySignerEmail` (Ironclad [S4]); `existingNda` (yes/no/unsure: "Do we already have one with them?"); `proposedTerm` (choice: 1 / 2 / 3 / 5 years); `governingLawPreference`; `businessOwner` (section 5). Add the signer fields and term only if Spaarke later **issues** our-paper NDAs rather than only assessing them (open question 5).

**Draft upload:** expected whenever `paperSource = theirs`, and allowed for `ours` (e.g. an earlier signed NDA).

### 1.3 Title (`sprk_name`)

`NDA – {counterpartyName}`, e.g. `NDA – Contoso Ltd`. Truncate the whole title to 850 characters. The direction stays in the JSON, not the title, so the title is stable if Legal changes it.

### 1.4 Success text

> **NDA request sent to Legal**
> Your request "NDA – {counterpartyName}" has been created. You can follow its progress in **Service Requests**.
> {if paperSource = theirs} **Next step:** open the request and add the other party's draft under **Documents**. Legal will start the review once the draft is there.
> Please don't share confidential information with {counterpartyName} until the NDA is signed.

Buttons: **Open request** (primary; goes to the request detail with the Documents section) · **Done**.

### 1.5 Privacy and sensitivity

- Medium sensitivity. The counterparty name and purpose can reveal an unannounced deal, partnership or acquisition. The codename help text on `counterpartyName` covers this.
- `sprk_name` is **not** field-secured, and the counterparty name appears in it. That is acceptable because Legal needs it in lists, but the title must never include `purpose` or `notes`.
- `sensitiveInfo` answers are routing signals only. Requesters should not paste the information itself.

---

## 2. Policy Question

### 2.1 What the market asks

| Product | Fields asked | Source |
|---|---|---|
| Checkbox (form templates) | Requester name and role, **policy area** (e.g. data privacy, gifts and entertainment, anti-bribery), description of the question or issue, supporting documents, **urgency Low / Medium / High** | [S3] |
| Onit Unity LSR | "Policy/Compliance Question" template (guidance on internal policies or regulatory interpretation). Every request has Title and Description, plus priority, business-impact category and requested due date | [S6] [S7] [S8] |
| Josef Q | Policy Q&A self-service, so employees get instant answers from policy content before (or instead of) asking a lawyer | [S13] *(index)* |
| Streamline AI | Triage on urgency, importance and complexity. Can surface FAQ or self-help content based on the answers | [S11] |

**Takeaways**

1. The market form is **topic + question + urgency**, which matches the owner's starting point. The useful refinements are:
   - split "what do you need to know" from "background" so the question is crisp;
   - add a timing choice ("planning something" vs "already happened"). A question about an act already done can be an incident (a bribe already paid, data already sent) and needs different handling and possibly privilege;
   - express urgency as a plain-language answer time, not Low/Medium/High.
2. Deflection to policy content (Josef Q, Streamline) is Phase 3 in Spaarke's own roadmap (`notes/legal-front-door-solution-overview.md`). It is out of scope for R3, but the `topic` codes should be stable so a later deflection step can key on them.

### 2.2 Recommended field set (2 steps)

**Step 1: Your question**

| JSON key | Label | Control | Req? | Validation | Help text | Conditional |
|---|---|---|---|---|---|---|
| `topic` | What is it about? | choice | Yes | code | Pick the closest. Legal will re-categorise if needed. | `ethics` Code of conduct and ethics · `gifts` Gifts, entertainment and hospitality · `antiBribery` Anti-bribery and corruption · `conflicts` Conflicts of interest · `privacy` Data privacy and personal information · `infoSecurity` Information security and acceptable use · `communications` Marketing, social media and public statements · `signingAuthority` Contracts and signing authority · `employment` Employment and HR policy · `competition` Competition and antitrust · `tradeCompliance` Trade, sanctions and export control · `records` Records and retention · `other` Other |
| `topicOther` | What topic? | text | Yes, when shown | 2–200 | — | Show when `topic = other` |
| `question` | What do you need to know? | multiline | Yes | 20–1,000 | One clear question, e.g. "Can I accept a conference ticket worth about $400 from a supplier?" | — |
| `background` | Background | multiline | No (core optional) | ≤ 4,000 | Who is involved, amounts, dates, and anything you've already been told. Leave out personal details Legal doesn't need. | — |
| `situation` | Which best describes this? | choice | Yes | code | — | `planning` I'm planning to do something · `happened` It has already happened · `general` General question |
| *(no field)* | Warning banner | — | — | — | "If you are reporting a possible data breach, fraud or misconduct, use {incident/speak-up channel} instead. You can still submit this question." | Show when `situation = happened` and `topic` ∈ {`privacy`, `infoSecurity`, `antiBribery`, `ethics`}. The channel text comes from configuration (open question 3) |
| `policyReference` | Which policy, if you know? | text | No | ≤ 200 | e.g. "Gifts and Hospitality Policy v3". | Nice to have; omit in v1 if the owner prefers |

**Step 2: Timing and review**

| JSON key | Label | Control | Req? | Validation | Help text | Conditional |
|---|---|---|---|---|---|---|
| `urgency` | When do you need an answer? | choice | Yes | code | Legal will confirm the timing. | `routine` No rush (within about a week) · `soon` In the next few days · `urgent` Urgent: it's blocking a decision now |
| `neededBy` | By when? | date | Yes, when shown | today ≤ date ≤ today + 90 days | — | Show when `urgency` ∈ {`soon`, `urgent`} |
| *(review)* | Summary, read-only | — | — | — | — | — |

**Nice to have:** `policyReference` (above), `peopleInvolved` (person list; most questions don't need names, so leave it out, which also limits PII).

**Document upload:** optional, e.g. the invitation, email thread or offer the question is about. The success text mentions it, but it is not expected.

### 2.3 Title (`sprk_name`)

`Policy question – {topicLabel}`, e.g. `Policy question – Gifts, entertainment and hospitality`. When `topic = other`, use `Policy question – {topicOther}` truncated to 120 characters. **Do not** put `question` text in the title (section 2.5).

### 2.4 Success text

> **Question sent to Legal**
> We've logged your {topicLabel} question. You'll get the answer as a message on the request; you can follow it in **Service Requests**.
> {if urgency = urgent} If this can't wait, contact Legal directly as well.
> If you have related emails or documents, add them under **Documents** on the request.

### 2.5 Privacy and sensitivity

- Sensitivity varies, and can be high. Questions about conflicts, gifts, misconduct or HR can name colleagues or admit to conduct. The answers belong in the field-secured `sprk_intakedata`. The unsecured `sprk_name` carries only the topic label.
- Questions to in-house counsel for legal advice may be privileged. Don't copy question text into notifications, emails, audit messages or telemetry. The notification feed (C3) should say "New message on *Policy question – Gifts…*" and nothing more.
- The `background` help text tells requesters to leave out personal details Legal doesn't need.

---

## 3. Invention Submission

### 3.1 What the market asks

| Source | Fields asked | Ref |
|---|---|---|
| ServiceNow Legal Invention Disclosure (corporate) | Requester (pre-filled, can submit on behalf of someone), **inventors**, invention title, **problem solved**, **description**, **approximate conception date** (not in the future), **publicly disclosed?** (Yes opens disclosure method and date of first disclosure), **implemented or planned for a customer product?** (Yes opens release date and product name) | [S15] |
| USC Stevens (Wellspring Sophia) | Required: title and short description, **date of first public disclosure or anticipated disclosure date**, detailed description (manuscript, slides or written material can be attached), inventors with emails and % contribution, funding sources, related agreements (MTA, collaboration, consulting), third-party code for software. Steps: Details → Researchers → Funding → Survey/sign-off | [S16] |
| Marquette (Inteum portal) | Inventors, funding sources, description, conception and proof-of-concept dates, novelty and public disclosure, likely products, applications, markets and licensees | [S17] |
| CASRAI IDF guide | Title and inventors; conception and reduction-to-practice dates; description good enough for a patent attorney to draft from; prior and planned public disclosure; funding; commercial applications and known related art; certification. A US public disclosure starts a 1-year grace period; many countries have none. Disclosures are treated as confidential | [S18] |
| NHS Lothian and other IDFs | "Have you supplied any material relating to the invention to anyone outside your research group?" Known prior art and how the invention differs | [S19] *(index)* |

**Takeaways for a corporate legal team**

1. The **triage-critical** facts are inventors, what it is and what problem it solves, and above all **public disclosure** (past or planned) and **product release**. A past or imminent disclosure starts a filing clock. Every source asks this, and ServiceNow puts it at the top level with conditional follow-ups [S15] [S16] [S18].
2. University-specific items (funding sponsors for Bayh-Dole, royalty % splits, inventor sign-off) mostly don't apply to a corporate team. Funding becomes a broader question for corporates: was it developed with or funded by anyone outside the company (customer, university, grant, contractor)? That decides ownership.
3. Technical depth comes from **attachments** (manuscripts, slides, drawings) [S16]. The wizard collects a summary and asks for files afterwards.
4. Known prior art and differences are useful and raise the duty-of-disclosure point, but they are optional for the requester [S18] [S19].

### 3.2 Recommended field set (4 steps)

**Step 1: The invention**

| JSON key | Label | Control | Req? | Validation | Help text | Conditional |
|---|---|---|---|---|---|---|
| `shortTitle` | Short, non-confidential title | text | Yes | 5–150 | **This title is shown in request lists and notifications.** Describe the area, not the idea: "Improved battery cooling method", not how it works. | — |
| `problemSolved` | What problem does it solve? | multiline | Yes | 20–2,000 | The problem or limitation you set out to fix. | — |
| `description` | How does it work, and what is new about it? | multiline | Yes | 50–8,000 | Summarise the key idea and what makes it different. After you submit, add drawings, slides, lab notes or a draft paper under **Documents**. | — |
| `knownAlternatives` | Similar products, methods or patents you know of | multiline | No (core optional) | ≤ 3,000 | And how yours differs. "None I know of" is a fine answer. | — |

**Step 2: Inventors**

| JSON key | Label | Control | Req? | Validation | Help text | Conditional |
|---|---|---|---|---|---|---|
| `submitterIsInventor` | Are you one of the inventors? | yes/no | Yes | — | You're already identified from your sign-in. | — |
| `coInventors` | Other inventors | list of {`name` text 2–150, `email` text email format ≤ 254, `organization` text ≤ 200} | Yes, at least 1 row when `submitterIsInventor = no`; otherwise optional | max 20 rows | Everyone who contributed to the idea itself, not only to building or testing it. Add the organisation for anyone outside the company. | — |
| `conceptionDate` | Roughly when did you come up with it? | date (month precision acceptable) | No (core optional) | ≤ today | An estimate is fine. | — |

**Step 3: Disclosure, use and outside involvement** (time-critical)

| JSON key | Label | Control | Req? | Validation | Help text | Conditional |
|---|---|---|---|---|---|---|
| `publicDisclosure` | Has it been shared publicly, or is that planned? | choice | Yes | code | Includes papers, talks, posters, websites, demos, product sales or offers, and open-source releases. | `none` No · `done` Yes, already · `planned` Not yet, but planned |
| `disclosureDate` | Date (first or planned) | date | Yes, when shown | `done`: ≤ today; `planned`: ≥ today | — | Show when `publicDisclosure` ≠ `none` |
| `disclosureMethod` | How? | choice | Yes, when shown | code | — | `publication` · `presentation` · `onlinePost` Website, social or preprint · `productSale` Sale or offer for sale · `demo` Demo to a customer or third party · `openSource` · `other`. Show when `publicDisclosure` ≠ `none` |
| `disclosureVenue` | Where? | text | No | ≤ 300 | The venue or journal name only, not the content. | Show when `publicDisclosure` ≠ `none` |
| *(no field)* | Warning banner | — | — | — | "Public disclosure can limit or end patent protection, especially outside the US. Legal will contact you about timing." | Show when `publicDisclosure = planned` and the date is ≤ 60 days away, or when `publicDisclosure = done` |
| `sharedExternally` | Has it been shared with anyone outside the company, even under NDA? | yes/no/unsure | Yes | — | e.g. a supplier, customer or university partner. | — |
| `sharedWith` | With whom, and was there an NDA? | text | Yes, when shown | ≤ 500 | — | Show when `sharedExternally = yes` |
| `productUse` | Is it used in a product or service? | choice | Yes | code | — | `no` No · `planned` Planned · `released` Already released |
| `productName` | Product or service name | text | Yes, when shown | ≤ 200 | — | Show when `productUse` ≠ `no` |
| `productDate` | Release date (actual or planned) | date | No | — | — | Show when `productUse` ≠ `no` |
| `externalInvolvement` | Was it developed with, or funded by, anyone outside the company? | yes/no/unsure | Yes | — | e.g. a customer project, university collaboration, government grant or contractor. | — |
| `externalInvolvementDetails` | Who, and under what agreement if you know? | text | Yes, when shown | ≤ 1,000 | — | Show when `externalInvolvement = yes` |

**Step 4: Review and confirm**

| JSON key | Label | Control | Req? | Validation | Help text | Conditional |
|---|---|---|---|---|---|---|
| `notes` | Anything else? | multiline | No | ≤ 2,000 | — | — |
| `accuracyConfirmed` | I confirm this is accurate to the best of my knowledge, and I will keep the invention confidential until Legal advises | yes/no (checkbox) | Yes (must be true) | — | — | — |

**Nice to have (not in v1):** `inventorContributionPct` (university royalty practice [S16], rarely used by corporate teams); `commercialApplications` and `competitors` (Marquette [S17]; patent counsel can ask later); `keywords`/`technologyArea` (USC [S16]); `thirdPartyCode` for software inventions (USC [S16]); `labNotebookRef`.

**Document upload:** expected. Drawings, diagrams, slides, draft papers, lab notes and code excerpts all go under Documents after submission. The JSON holds no figures or code.

### 3.3 Title (`sprk_name`)

`Invention – {shortTitle}`, e.g. `Invention – Improved battery cooling method`. `shortTitle` is non-confidential **by design**: the requester is told it is visible, because `sprk_name` is not field-secured.

### 3.4 Success text

> **Invention disclosure submitted**
> Thank you. Legal has received "{shortTitle}" and will contact you, usually to set up a short call with the inventors.
> **Next step:** add drawings, slides, lab notes or a draft paper under **Documents** on the request.
> **Keep it confidential.** Please don't publish, present, demo or post about this invention until Legal has advised you. {if a disclosure date was given} Legal has your {disclosure/planned disclosure} date of {date} and will prioritise accordingly.

### 3.5 Privacy and sensitivity

- **Highest sensitivity of the four.** `problemSolved`, `description`, `knownAlternatives` and the attachments are potential trade secrets and patent subject matter. Treat them as confidential:
  - they live only in the field-secured `sprk_intakedata` and in the request's documents;
  - they never go into `sprk_name`, notification text, email bodies, telemetry, logs or AI prompts without Legal's say-so.
- **Recommend verifying in code (not done here):** that neither the intake endpoint (task 035) nor the client logs the request body (`intakeData`) to Application Insights or the console, and that error responses don't echo it.
- Don't ask for more disclosure detail than triage needs. `disclosureVenue` asks for the venue only, and the help text says so.
- **CIAM partners:** if `PartnerServiceRequestsEnabled` is on, an external partner could submit an invention, which raises ownership and confidentiality questions. Consider keeping this wizard workforce-only even when the switch is on (open question 2).
- Co-inventor emails are personal data about third parties. Keep the list to name, email and organisation.

---

## 4. Trademark Search

### 4.1 What the market asks

| Source | Fields asked | Ref |
|---|---|---|
| Carter Ledyard & Milburn clearance checklist (law firm) | Request date, contact, **proposed mark**; **goods/services**; international classes; **mark form** (word or composite, design, colours, slogan, use with a house mark); **meanings** (other languages, acronyms, surname or geographic significance, descriptiveness); internet and domain use; **countries** where offered or sold; **where already used and since when** | [S20] |
| Innovation Capital Law Group questionnaire (law firm) | The mark (word, phrase, logo, design); applicant entity; design mark yes/no (send a JPEG); colours claimed; **already in use in the US?** (yes: first-use date and specimens; no: intent-to-use); goods/services; foreign applications | [S21] |
| Checkbox (trademark review intake) | Proposed mark, related product/service, jurisdictions or markets, intended use, launch timing, business owner, urgency, prior use, supporting materials, business context. Paths differ for name review, geographic expansion, brand use and launch escalations | [S22] |
| Bloomberg Law (practice note) | Marketing often starts on names without Legal; naming agencies' "pre-cleared" names come with liability disclaimers. So: involve Legal early | [S23] *(index)* |

**Takeaways**

1. The core request is **the mark, what it will be used for, and where**, plus **whether it's already in use** [S20] [S21] [S22].
2. **Don't ask business users for Nice classes.** Law-firm forms ask for classes because their users are IP professionals. A business requester describes the product, and Legal assigns the classes. ICAP and Carter Ledyard both ask for goods/services in words first [S20] [S21].
3. **Mark form** (word vs logo) changes the search. A logo is provided as an upload afterwards; the law-firm form asks for a JPEG [S21].
4. **Launch date** is the real urgency driver [S22]. Search depth (knockout vs full) is Legal's call [S20], so don't ask the requester.

### 4.2 Recommended field set (3 steps)

**Step 1: The proposed mark**

| JSON key | Label | Control | Req? | Validation | Help text | Conditional |
|---|---|---|---|---|---|---|
| `proposedMark` | Proposed name or mark | text | Yes | 1–200 | Exact spelling, capitalisation and spacing. | — |
| `markForm` | What kind of mark? | choice | Yes | code | — | `word` Name or word only · `logo` Logo or design only · `wordAndLogo` Name with a logo · `slogan` Slogan or tagline |
| *(no field)* | Information banner | — | — | — | "After you submit, add the logo or design file under **Documents**." | Show when `markForm` ∈ {`logo`, `wordAndLogo`} |
| `markMeaning` | What does it mean, or how was it chosen? | text | No (core optional) | ≤ 500 | Include meanings in other languages, or say if it's an acronym or a made-up word. | — |
| `alternatives` | Back-up options | text | No | ≤ 500 | Other names in order of preference, so Legal can check them in the same pass. | Nice to have (cheap and saves a round trip; owner's call) |

**Step 2: How and where it will be used**

| JSON key | Label | Control | Req? | Validation | Help text | Conditional |
|---|---|---|---|---|---|---|
| `goodsServices` | What will it be used for? | multiline | Yes | 10–2,000 | Describe the product or service in plain words. Legal will work out the trademark classes. | — |
| `markRole` | What will it name? | choice | No (core optional) | code | — | `product` A product · `service` A service or programme · `company` A company, brand or business unit · `feature` A feature or campaign |
| `territories` | Where will it be used? | multi-choice | Yes | ≥ 1 code | Where it will be sold, offered or marketed, now and within about 2 years. | `us` United States · `ca` Canada · `eu` European Union · `uk` United Kingdom · `cn` China · `jp` Japan · `in` India · `au` Australia · `worldwide` Worldwide / online only · `other` Other |
| `territoriesOther` | Other countries | text | Yes, when shown | ≤ 300 | — | Show when `territories` contains `other` |

**Step 3: Status and timing**

| JSON key | Label | Control | Req? | Validation | Help text | Conditional |
|---|---|---|---|---|---|---|
| `currentUse` | Is the name already in use? | choice | Yes | code | — | `notYet` Not yet · `internal` Only internally · `public` Yes, publicly |
| `firstUseDate` | Since when? | date | No | ≤ today | — | Show when `currentUse = public` |
| `useLocations` | Where is it used? | text | No | ≤ 300 | Countries or channels. | Show when `currentUse = public` |
| `launchDate` | Planned launch or announcement date | date | No (core optional) | ≥ today | — | — |
| `neededBy` | When do you need the result? | date | No | today ≤ date ≤ launchDate (if set) | Full searches can take a few weeks, so ask early. | — |
| `notes` | Anything else? | multiline | No | ≤ 2,000 | e.g. an agency proposed the name. | — |

**Nice to have:** `businessOwner` (section 5); `domainNeeded` (yes/no, [S20]); `usedWithHouseMark` (yes/no, [S20]); `searchDepth`, which is not recommended because it is Legal's decision.

**Document upload:** expected for logos and designs; optional for mock-ups, packaging or agency naming decks.

### 4.3 Title (`sprk_name`)

`Trademark search – {proposedMark}`, e.g. `Trademark search – AURORA`.

### 4.4 Success text

> **Trademark search requested**
> Legal has received your request for "{proposedMark}" and will let you know what it finds.
> {if markForm includes a logo} **Next step:** add the logo file under **Documents** on the request.
> Until Legal confirms, please don't announce the name, file applications, register domains or order packaging or signage. A quick search that finds no conflicts is not the same as clearance.

### 4.5 Privacy and sensitivity

- Medium sensitivity. An unannounced product or company name is commercially confidential, and it is in `sprk_name` (unsecured). That is acceptable because Legal needs it in lists, and the record is visible only to the requester and Legal. It does mean the notification feed and emails should not broadcast titles beyond the record's members (already the C3 design).
- `goodsServices` can describe unreleased products. It stays in the secured JSON.

---

## 5. Cross-cutting fields

### 5.1 Recommendation

| Field | Market practice | Recommendation for Spaarke | Wizards |
|---|---|---|---|
| Requester (name, email, contact) | Every product pre-fills it from sign-in [S15]; ServiceNow lets you change it to submit on someone's behalf | **Automatic.** The server stamps `sprk_requestedby` from the token (task 035). **Not in the JSON**, so it isn't duplicated as PII; a body that names another requester is ignored (035 test) | None (system) |
| On behalf of / business owner | Checkbox (trademark) asks for the business owner [S22]; ServiceNow supports on-behalf-of [S15] | Optional `businessOwner` (person: name, optional email), only where it changes who Legal talks to | Nice to have for NDA and Trademark; not in v1 |
| Business unit / department | Checkbox templates list "requester's name and department" [S3]; Brightflag has Department as a matter field [S9] | **Don't ask.** Take it from the identity (Entra `department`) or the contact record if available; Spaarke's own design principle is that "identity, business unit, and region derive from Entra" (`legal-front-door-solution-overview.md`). If neither carries it, add one optional `department` text field to all four (open question 1) | None (pending Q1) |
| Cost centre | Not on business-facing intake forms in the sources reviewed; it belongs to matter/spend management (Brightflag, Onit) | **Don't ask.** Legal assigns it if a matter is opened | None |
| Needed-by date | Onit "requested due date" [S7]; Checkbox deadline/timeline [S3] | One optional key, `neededBy` (ISO date), with the same name everywhere so the queue can sort on it | NDA, Policy (conditional), Trademark. **Not** Invention: urgency there comes from the disclosure dates |
| Urgency / priority | Onit priority + business impact [S7]; Checkbox Low/Med/High for policy only [S3] | **Policy Question only** (`urgency`). Elsewhere urgency is implied by `neededBy`/`launchDate`/`disclosureDate`; adding a priority choice to every form invites "everything is High" | Policy |
| Free-text notes | Universal | Optional `notes` ≤ 2,000 on the last step | NDA, Invention, Trademark (Policy already has `background`) |
| Attachments | Universal [S9] [S11] | Not in the wizard. After submission, in Documents (task 054); each success screen says what to add | All (post-submit) |

### 5.2 Wizard shape

| Wizard | Steps | Required fields (base path) |
|---|---|---|
| NDA Assessment | Counterparty → Purpose and terms → Timing and review | 5 |
| Policy Question | Your question → Timing and review | 4 (+1 conditional) |
| Invention Submission | The invention → Inventors → Disclosure, use and outside involvement → Review and confirm | 10 (+ conditionals) |
| Trademark Search | The proposed mark → How and where → Status and timing | 5 |

Invention Submission is deliberately the heaviest. It matches the market (Sophia uses 4 pages [S16]), and each required item is triage-critical (ownership and filing clock).

### 5.3 JSON envelope conventions

- Flat object, camelCase keys, plus a system key `schemaVersion: 1` (the wizard sets it, the user never sees it) so the detail view and any later extraction can handle form changes.
- Choice values are **codes** (`"theyDisclose"`), multi-choice values are arrays of codes, dates are ISO `yyyy-mm-dd`, and yes/no/unsure is `"yes" | "no" | "unsure"` (a boolean for pure yes/no). The detail view (task 050) maps codes to labels, so a label can be reworded without migrating data.
- Leave out keys for hidden or empty optional fields. Don't store `null`s for branches the requester never saw.
- **Size:** the largest case (Invention: 8,000 + 3,000 + 2,000 + 2,000 + 1,000 + 20 inventor rows) is about 20,000 characters, so ≤ 60 KB even if every character takes 3 UTF-8 bytes. That is well under the 100 KB cap. Client caps should match the server's validation so the user never meets a 400.

Example (NDA):

```json
{ "schemaVersion": 1, "counterpartyName": "Contoso Ltd", "counterpartyType": "vendor",
  "purpose": "Evaluate their analytics platform for the Q3 pilot",
  "disclosureDirection": "mutual", "paperSource": "theirs",
  "sensitiveInfo": ["personalData"], "neededBy": "2026-10-30" }
```

### 5.4 Sensitivity summary

| Wizard | Sensitivity | What may appear in `sprk_name` (unsecured) |
|---|---|---|
| NDA Assessment | Medium (deal confidentiality) | Counterparty name (codename allowed) |
| Policy Question | Medium–high (personal conduct, possibly privileged) | Topic label only |
| Invention Submission | **High** (trade secret / patentability) | A non-confidential short title the requester has been warned about |
| Trademark Search | Medium (unannounced brand) | The proposed mark |

All answers sit only in the field-secured `sprk_intakedata`. No answer text goes into notifications, emails, logs, telemetry or AI prompts without a deliberate decision.

---

## 6. Open questions for the owner

1. **Department / business unit.** Does the signed-in identity (Entra `department` claim or Graph profile) or the `contact` record carry the requester's department? If not, should all four wizards ask an optional `department` question for demand reporting?
2. **Partners and inventions.** When `PartnerServiceRequestsEnabled` is on, should CIAM partners see all four wizards, or should Invention Submission (and perhaps Trademark Search) stay workforce-only for ownership and confidentiality reasons?
3. **Incident channel.** For Policy Questions about something that has already happened (possible breach, bribery or misconduct), should the form point to a speak-up or incident channel, and what is its name or link, per customer or fixed?
4. **Option lists.** Should the policy topics and trademark territories be fixed Spaarke defaults in R3 (recommended), or configurable per customer (which needs a configuration surface that isn't in scope)?
5. **NDA scope.** Is "NDA Assessment" review-only (Legal assesses a draft, usually the counterparty's), or should it also cover issuing our standard NDA? Issuing would add signer name and email, term and governing law (Ironclad-style [S4]) and probably make `neededBy` required.

### 6.1 Owner answers (2026-10-12)

| # | Answer | Build consequence |
|---|---|---|
| 1 | Use the contact record's department field. | No department question in any wizard. The server copies the requester contact's `department` into the stored intake data as the system key `requesterDepartment`, never from the client. |
| 2 | Invention Submission is optionally available to partners, default **off**; by default it is for workforce users (staff and workforce contacts) only. | New `ExternalAccess:PartnerInventionIntakeEnabled` (default false). The server refuses a CIAM invention submission while it is off; the SPA hides the tile. Trademark Search follows the existing partner switch. |
| 3 | Incidents: keep it simple for now. A notification or alerting process (for example an email to a designated mailbox) is wanted later. | R3: no incident channel link or routing in the Policy Question wizard. The alerting process is deferred (tracked in `defer-issues.md`). |
| 4 | Policy topics and trademark territories are fixed for R3; per-customer configuration comes later. | Fixed code lists in R3, kept as stable codes so a later per-environment configuration can replace the source without a data migration. |
| 5 | NDA covers both review and issuing our standard NDA ("needs extended functionality"). | The NDA wizard asks review vs issue. The issue path adds signer name, signer email, term and governing law, and makes `neededBy` required. Whether "issue" also means generating the NDA document automatically is an open question (§6.2). |

### 6.2 Open after the owner answers

- **NDA issue path.** Does "issuing our standard NDA" mean the request collects what Legal needs to issue it (R3 scope as built), or that Spaarke generates the standard NDA document automatically from the answers? Generation needs a template, a document-generation capability and a delivery step, and is not in the R3 spec.

---

## 7. Sources (all retrieved 2026-10-11)

*(index)* = could not be fetched directly; the claim relies on the search engine's summary of the page.

| # | Source | URL | Notes |
|---|---|---|---|
| S1 | ServiceNow docs: Submit a legal request for a NDA (Legal Simple Contracts) | https://www.servicenow.com/docs/r/H8yQl2SqBqnT6LbpTm28UQ/LUCwH5cFrqE_iunBRlNvfg | Vendor docs; full field list. **Most authoritative NDA source** |
| S2 | Checkbox blog: self-service NDA requests and automated delivery | https://blog.checkbox.ai/blog/decision-guide/which-platforms-allow-employees-to-initiate-nda-requests-and-get-a-completed-contract-back-automatic-130a1a | Vendor blog; NDA fields and routing triggers |
| S3 | Checkbox: Best legal intake forms, templates and apps | https://www.checkbox.ai/legal-intake/best-legal-intake-forms-templates-apps-to-streamline-requests | Vendor; per-type field lists (NDA, contract, policy, employment) |
| S4 | Ironclad support: Recipe – Build a Basic NDA Workflow | https://support.ironcladapp.com/hc/en-us/articles/33517529670679-Recipe-Build-a-Basic-NDA-Workflow | *(index)*, HTTP 403 on fetch |
| S5 | Ontra blog: How an NDA playbook speeds up negotiations | https://www.ontra.ai/blog/contract-playbook-speeds-up-nda-negotiations/ | *(index)*; document-first NDA intake |
| S6 | Onit docs: Overview of Unity LSR | https://onit.screenstepslive.com/a/2035159-overview-of-unity-lsr | Vendor docs; 16 request templates |
| S7 | Onit docs: Unity LSR Q1 2026 release notes | https://onit.screenstepslive.com/a/2035776-unity-lsr-q1-2026-release-notes | Priority, business-impact category, requested due date |
| S8 | Onit docs: Submitting and managing a legal service request | https://onit.screenstepslive.com/a/2036188-submitting-managing-a-legal-service-request | Default fields: Title, Description |
| S9 | Brightflag help: Legal Service Requests | https://help.brightflag.com/en/articles/753368-legal-service-requests | Configurable request types and field kinds |
| S10 | SpotDraft help: How to submit a legal request | https://help.spotdraft.com/articles/4059815172-how-to-submit-a-legal-request | Workflow-per-type selection |
| S11 | Streamline AI: Legal intake form guide | https://www.streamline.ai/blog/legal-intake-form-guide | Short forms, conditional logic, attachments |
| S12 | Tonkean template library: Legal request from form | https://library.tonkean.com/template/legal-request-from-form/ | *(index)*; progressive fields |
| S13 | LawNext: Josef Q turns policies into Q&A tools (2023) | https://www.lawnext.com/2023/03/new-product-josef-q-uses-openais-tech-to-transform-policies-and-regulations-into-digital-qa-tools.html | *(index)*; policy self-service |
| S14 | CLOC SF roundtable: Legal intake challenges and solutions (Sep 2024) | https://hq.cloc.org/events/cloc-san-francisco-monthly-roundtable-legal-intake-challenges-and-solutions---september-2024 | *(index)*; event listing only, no field standard |
| S15 | ServiceNow docs: Legal Invention Disclosure form | https://www.servicenow.com/docs/r/XbpkTdWa3HBo31srdD0Rdg/k79TeHGFDA7MPgNwCLTXOw | Vendor docs; corporate IDF with conditional follow-ups. **Most authoritative invention source** |
| S16 | USC Stevens: Guide to Completing an Invention Disclosure Form (Wellspring Sophia) | https://stevens.usc.edu/wp-content/uploads/2021/05/Guide-to-Completing-an-Invention-Disclosure-Form-rev-051421.pdf | Full 4-page portal walkthrough |
| S17 | Marquette University: How do I disclose my invention? (Inteum) | https://www.marquette.edu/innovation/technology-transfer/submit-invention.php | Disclosure question list and post-submit process |
| S18 | CASRAI: Invention disclosure form guide | https://www.casrai.org/guides/invention-disclosure-form | Section structure; grace period; confidentiality |
| S19 | NHS Lothian: Invention Disclosure Form | https://policyonline.nhslothian.scot/wp-content/uploads/2023/03/Intellectual%20Property%20Management%20-%20Invention%20Disclosure%20Form.pdf | *(index)*; "supplied material outside the group" question |
| S20 | Carter Ledyard & Milburn via Mondaq: Trademark clearance search checklist | https://www.mondaq.com/unitedstates/trademark/209872/trademark-clearance-search | Law-firm checklist. **Most authoritative trademark source** |
| S21 | Innovation Capital Law Group: Trademark Questionnaire | https://www.patenttrademarkblog.com/wp-content/uploads/2016/08/Trademark-Form-ICLG.pdf | Law-firm questionnaire (mark, design, use, goods/services) |
| S22 | Checkbox blog: trademark review and clearance intake | https://blog.checkbox.ai/blog/solution-recommendation/what-tools-help-in-house-legal-teams-manage-trademark-review-and-clearance-requests-through-a-struct-9f6d64 | Vendor; corporate trademark intake fields |
| S23 | Bloomberg Law: Is your business or product name legal? | https://news.bloomberglaw.com/ip-law/is-your-business-or-product-name-legal-heres-how-to-find-out | *(index)*; involve Legal early |

**Not found / gaps:**
- Field-level public documentation for Juro, LawVu, Axiom, Streamline's NDA template (its questions are behind a form), Anaqua or Inteum's own product.
- A corporate (non-university) published IDF template other than ServiceNow's.
- A corporate trademark request template; the law-firm forms are the nearest match.

Josef's intake-triage PDF could not be fetched (DNS failure).
