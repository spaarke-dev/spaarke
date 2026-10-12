/**
 * policyTopics - the Policy Question topic list (r3 task 052; owner decision 2026-10-12, answer 4: the list is FIXED in
 * R3 and configurable per customer later).
 *
 * This is the ONE module that owns the list. The CODES are the stored value (`answers.topic`), so they are stable:
 * never rename or reuse one. A later per-customer/per-environment source replaces `getPolicyTopics()` without a data
 * migration, because the stored answers hold codes and the detail view maps codes to labels. `other` is special: it
 * asks for free text (`topicOther`) and is always offered last.
 *
 * Labels follow `notes/intake-form-design.md` section 2.2.
 */

export interface PolicyTopic {
  /** Stable code stored in the answers. */
  code: string;
  /** What the requester reads. */
  label: string;
}

/** The code that asks for free text. */
export const POLICY_TOPIC_OTHER = 'other';

const FIXED_POLICY_TOPICS: readonly PolicyTopic[] = [
  { code: 'ethics', label: 'Code of conduct and ethics' },
  { code: 'gifts', label: 'Gifts, entertainment and hospitality' },
  { code: 'antiBribery', label: 'Anti-bribery and corruption' },
  { code: 'conflicts', label: 'Conflicts of interest' },
  { code: 'privacy', label: 'Data privacy and personal information' },
  { code: 'infoSecurity', label: 'Information security and acceptable use' },
  { code: 'communications', label: 'Marketing, social media and public statements' },
  { code: 'signingAuthority', label: 'Contracts and signing authority' },
  { code: 'employment', label: 'Employment and HR policy' },
  { code: 'competition', label: 'Competition and antitrust' },
  { code: 'tradeCompliance', label: 'Trade, sanctions and export control' },
  { code: 'records', label: 'Records and retention' },
  { code: POLICY_TOPIC_OTHER, label: 'Other' },
];

/** The topics the wizard offers. The seam a per-customer source replaces (keep `other` last). */
export function getPolicyTopics(): readonly PolicyTopic[] {
  return FIXED_POLICY_TOPICS;
}

/** The label for a code, or `undefined` for a code that is not in the list. */
export function policyTopicLabel(code: string): string | undefined {
  return getPolicyTopics().find(t => t.code === code)?.label;
}
