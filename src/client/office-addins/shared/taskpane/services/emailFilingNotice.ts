/**
 * emailFilingNotice.ts
 *
 * Task 131 (#2171): the one place the add-in turns the server's `SaveResponse.emailFiling` into words. An Outlook save
 * that reconciles to an email already in Spaarke tries to FILE that email to the chosen record; when the caller may not
 * change it (a secure record's thread) or the write fails, the save still succeeds — and this is how the user hears the
 * email was not filed. Shared by the task pane (`SaveFlow`) and Quick Save (`outlook/commands`).
 *
 * The server serializes the enum by name (`"Filed" | "AlreadyFiled" | "NotPermitted" | "Failed"`, like `artifact.type`);
 * the property is OMITTED when there is nothing to report. A value this build does not know (a newer server) is ignored:
 * no notice, no crash. The words reveal nothing about which record the email is filed under.
 */

/** What the server did about filing the reconciled email (`SaveResponse.emailFiling`). Absent = nothing to report. */
export type EmailFilingOutcome = 'Filed' | 'AlreadyFiled' | 'NotPermitted' | 'Failed';

/** The outcomes the user is told about. Filed / AlreadyFiled / absent / unknown say nothing extra. */
export type EmailFilingProblem = 'NotPermitted' | 'Failed';

// The EXPLANATION only: each surface supplies its own "saved" context (the pane's "Saved to Spaarke" bar, Quick Save's
// "Saved to Spaarke and filed to <record>." sentence). "Try again" is deliberately absent: a repeat of the same save reuses
// its idempotency key, so it replays the original response (within 24 h) or is a duplicate (after) — it never re-files.
const NOT_PERMITTED_NOTICE =
  "The email wasn't added to this record's communications because you don't have permission to change it.";
const FAILED_NOTICE = "The email couldn't be added to this record's communications. File it from Spaarke.";

/**
 * The outcome to tell the user about, or `null` for filed / already filed / absent / any value this build does not know.
 * Accepts `unknown` because it reads a server body; compares case-insensitively so a change of the server's enum casing
 * cannot silently hide the notice.
 */
export function readEmailFilingProblem(value: unknown): EmailFilingProblem | null {
  if (typeof value !== 'string') return null;
  switch (value.toLowerCase()) {
    case 'notpermitted':
      return 'NotPermitted';
    case 'failed':
      return 'Failed';
    default:
      return null;
  }
}

/** The explanation sentence for a problem outcome (no "Saved." prefix; see the note above). */
export function describeEmailFilingProblem(problem: EmailFilingProblem): string {
  return problem === 'NotPermitted' ? NOT_PERMITTED_NOTICE : FAILED_NOTICE;
}
