/**
 * EmailStep barrel export.
 *
 * Provides a generic email composition step with user lookup,
 * suitable for any wizard or multi-step form.
 */
export { SendEmailStep } from "./SendEmailStep";
export type { ISendEmailStepProps } from "./SendEmailStep";

export { LookupField } from "./LookupField";
export type { ILookupFieldProps, ILookupItem } from "./LookupField";

export { extractEmailFromUserName } from "./emailHelpers";
