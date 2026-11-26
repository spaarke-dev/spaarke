# Playbook: Spaarke Legal Hold Notice – Employee Custodians v1.0
Version: 1.0 | Owner: Spaarke Legal Engineering | Effective: 2026-11-07 | Status: POC test positions, not attorney-approved

Scope. Document type: legal hold (litigation hold) notice from in-house counsel to employee custodians, sent when litigation or a government investigation is reasonably anticipated. Use it to review a draft notice before it is sent. Basis: Fed. R. Civ. P. 37(e) (reasonable steps to preserve electronically stored information) and The Sedona Conference, Commentary on Legal Holds, Second Edition (2019). Do not use for: preservation demand letters to an opposing party, subpoenas, or records-retention schedules.

## topic: lh.matter
Preferred: Identifies the dispute or investigation (the other party and the general subject) clearly enough that recipients can recognise relevant information.
Fallback: A matter code name, if the notice also describes the subject matter it covers.
Prohibited / escalate: The notice does not say what matter it concerns.
Model language: "Acme has received a claim from [other party] concerning [subject]. Acme expects litigation and must preserve information relevant to it."
Priority: High

## topic: lh.time-period
Preferred: A start date early enough to cover the underlying events (for a contract dispute, at least from the negotiation of the agreement), and an ongoing hold that also covers information created or received after the notice.
Fallback: A start date at the signing of the agreement, if the hold is ongoing.
Prohibited / escalate: A period that ends at the notice date or another fixed date, or that starts only at the demand letter or another recent event.
Model language: "This hold covers information from [start date, no later than the start of the relationship or events in dispute] to the present, and applies to information created or received after the date of this notice."
Priority: High

## topic: lh.subject-scope
Preferred: Describes the categories of relevant information in plain language (the agreement, its performance, communications with the other side, internal discussions, financial records).
Fallback: n/a
Prohibited / escalate: Scope limited to what the recipient considers important or key, or described only in legal terms such as "all discoverable material".
Priority: High

## topic: lh.data-types
Preferred: Covers every form and location: email; chat and collaboration messages (for example Teams); text and instant messages, including on personal phones used for work; voicemail; documents on network drives, OneDrive and SharePoint; business systems; paper files and notebooks.
Fallback: n/a
Prohibited / escalate: Limited to email, paper or shared-drive documents; omits chat, text messages or personal devices used for work.
If missing: If the notice does not mention chat messages, text messages or personal devices used for work, it is not acceptable: add them from the Example Text.
Model language: "This includes email, Teams and other chat messages, text and instant messages (including on personal phones used for work), voicemail, documents on network drives, OneDrive and SharePoint, business systems, and paper files and notebooks."
Priority: High

## topic: lh.preserve-instruction
Preferred: Tells recipients plainly not to delete, discard, alter or overwrite relevant information, and to keep it where it is: no moving, forwarding to personal accounts or tidying up.
Fallback: n/a
Prohibited / escalate: Any invitation to delete, tidy, consolidate or clean up files, drafts or duplicates, or to use personal judgment about what to keep.
Model language: "Do not delete, discard, alter or overwrite any of this information, and do not move it or forward it to personal accounts. Keep it as it is, including drafts and duplicates."
Priority: High

## topic: lh.auto-deletion
Preferred: Tells recipients to turn off automatic deletion they control (mailbox clean-up rules, auto-delete of messages on phones) and says that Legal and IT are placing technical holds on company systems.
Fallback: Says that IT will suspend automatic deletion on company systems, without steps for recipients.
Prohibited / escalate: Silent on automatic deletion.
If missing: If the notice says nothing about automatic deletion, it is not acceptable: add the instruction from the Example Text.
Model language: "Turn off any automatic deletion you control, such as mailbox clean-up rules or auto-delete settings for messages on your phone. Legal and IT are placing technical holds on company systems; do not rely on them alone."
Priority: High

## topic: lh.overrides-retention
Preferred: States that the hold overrides the records-retention schedule and any routine destruction until Legal releases it.
Fallback: n/a
Prohibited / escalate: Says or implies that normal retention or destruction schedules continue to apply.
Model language: "This hold overrides Acme's records-retention schedule and any routine destruction practice."
Priority: High

## topic: lh.other-custodians
Preferred: Asks recipients to tell Legal about anyone else (including former employees, contractors and vendors) who may have relevant information, and to tell Legal before they leave the company or change role so that their devices and accounts are preserved.
Fallback: Asks recipients to identify other people with relevant information, without the departure instruction.
Prohibited / escalate: Neither: the notice treats the recipient list as complete.
Model language: "If you know of anyone else, including former employees, contractors or vendors, who may have relevant information, tell Legal. If you plan to leave Acme or change roles, tell Legal first so that your devices and accounts can be preserved."
Priority: High

## topic: lh.duration-release
Preferred: The hold stays in effect until Legal releases it in writing.
Fallback: n/a
Prohibited / escalate: A fixed expiry date, or an end on an event without a written release from Legal.
Model language: "This hold remains in effect until Legal tells you in writing that it has been released."
Priority: High

## topic: lh.acknowledgment
Preferred: Each recipient confirms in writing, by a stated date (normally within 5 business days), that they have read the notice and will comply.
Fallback: Written acknowledgment requested without a deadline.
Prohibited / escalate: No acknowledgment requested.
Model language: "Please confirm by [date, within 5 business days] that you have read this notice and will comply, by replying to this email."
Priority: High

## topic: lh.contact
Preferred: Names a person in Legal, with contact details, for questions.
Fallback: Names a Legal Department mailbox.
Prohibited / escalate: No contact for questions.
Priority: High

## topic: lh.privilege-content
Preferred: Marked privileged and confidential, and limited to what recipients need in order to preserve: no assessment of the merits, strategy or settlement, and no opinions about the other side, because a hold notice may be disclosed in the litigation.
Fallback: n/a
Prohibited / escalate: Comments on the strength of the claims, strategy or settlement, or statements that could be read as an admission.
Model language: "PRIVILEGED & CONFIDENTIAL — ATTORNEY-CLIENT COMMUNICATION"
Priority: High

## topic: lh.consequences
Preferred: Explains, in a neutral tone, that failing to preserve can lead to court sanctions against Acme and to disciplinary action.
Fallback: Explains why compliance matters without naming consequences.
Prohibited / escalate: n/a
Model language: "If relevant information is lost, a court can impose sanctions on Acme, and failure to comply with this notice may lead to disciplinary action."
Priority: High
