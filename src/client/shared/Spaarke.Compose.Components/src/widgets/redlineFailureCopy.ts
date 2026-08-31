/**
 * What we tell the user when a suggested edit could not be placed, or could only be placed if they
 * confirm where. Two functions: one for the error banner, one for the confirmation dialog.
 *
 * <b>Component justification (CLAUDE.md §11).</b>
 *  1. <i>Existing</i> — nothing owned this. The banner sentence lived inline in `ComposeBannerStack`
 *     as a five-deep ternary; the dialog sentence lived inline in `ComposeWorkspace`. No module
 *     covered "how we explain a placement failure".
 *  2. <i>Extension</i> — the obvious host would be `ComposeBannerStack`, but the dialog copy is not
 *     banner copy, and importing a banner component into `ComposeWorkspace` to reach a string
 *     function is worse coupling than a shared leaf module. Both callers are god-class files that
 *     Track D (070–072) is decomposing; this moves in that direction rather than against it.
 *  3. <i>Cost of doing nothing</i> — concrete, and already paid. Both sentences were WRONG for the
 *     live-anchorless population (issue #853) and no test could say so, because reaching either
 *     string required rendering a heavy component. Pure functions of the error make the copy itself
 *     assertable, which is what the #853 regression tests do.
 *
 * <b>The distinction these functions exist to make.</b> Three causes reach the same two surfaces, and
 * each implies a DIFFERENT remedy — which is why picking the wrong one is not a cosmetic bug:
 *
 * | source | what happened | remedy we offer |
 * |---|---|---|
 * | `'anchored'` | named a paragraph this document lacks | select the passage, try again |
 * | `'legacy-replay'` | predates paragraph anchoring | re-run it — re-running produces an anchor |
 * | `'live-anchorless'` | the assistant answered with no anchor | select the passage and ask again |
 *
 * Telling a `'live-anchorless'` user their suggestion "came from an earlier session" is false about
 * their own action, and "re-run it to get suggestions that point at real paragraphs" is not a remedy
 * — they just did, and it didn't. See {@link AnchorlessSource}.
 */

import type { AnchorlessSource, PendingRedlineError } from './hooks/usePendingRedline';

/**
 * The redline-failure sentence for the error banner (nothing was placed, and there is nothing to
 * confirm — the target could not be located at all).
 */
export function describeRedlineError(e: PendingRedlineError): string {
  const batch = (e.failedCount ?? 0) > 1;

  if (e.kind === 'target_deleted') {
    return batch
      ? `${e.failedCount} of ${e.totalCount} suggested edits referred to text that no longer exists in this document. Nothing was changed for them.`
      : `The text this suggestion referred to no longer exists.`;
  }

  if (e.kind === 'ambiguous') {
    switch (e.source) {
      case 'legacy-replay':
        return `This suggestion came from an earlier session and quoted wording that appears in ${e.matchCount} places, so we won't guess which one it meant. Re-run it on the passage you want.`;
      case 'live-anchorless':
        return `The assistant didn't say which paragraph to change, and the wording it quoted appears in ${e.matchCount} places — so we won't guess which one it meant. Select the exact passage and try again.`;
      default:
        return `This suggested edit matches ${e.matchCount} places in the document. Select the exact passage and try again.`;
    }
  }

  switch (e.source) {
    case 'legacy-replay':
      return batch
        ? `${e.failedCount} of ${e.totalCount} suggestions came from an earlier session, before suggestions carried a paragraph reference, and the text they quoted is no longer in this document. Nothing was changed — re-run them to get suggestions that point at real paragraphs.`
        : `This suggestion came from an earlier session, before suggestions carried a paragraph reference, and the text it quoted is no longer in this document. Nothing was changed — re-run it to get a suggestion that points at a real paragraph.`;
    case 'live-anchorless':
      return batch
        ? `${e.failedCount} of ${e.totalCount} suggestions came back without saying which paragraph to change, and the text they quoted isn't in this document. Nothing was changed — select the passage you want and ask again.`
        : `This suggestion came back without saying which paragraph to change, and the text it quoted isn't in this document. Nothing was changed — select the passage you want and ask again.`;
    default:
      return batch
        ? `${e.failedCount} of ${e.totalCount} suggested edits named a paragraph or section this document doesn't have. Nothing was changed for them — you can still review, edit, and save.`
        : `This suggested edit named a paragraph or section this document doesn't have (${e.targetText || 'no target given'}). Nothing was changed — select the passage you want and try again.`;
  }
}

/**
 * The lead sentence of the confirmation dialog for an anchorless suggestion whose quoted prose WAS
 * located. The question being asked is identical for both sources — "we found this wording; is it
 * the clause you meant?" — so only the explanation of why we are asking differs.
 */
export function describeAnchorlessProposal(args: {
  source: AnchorlessSource;
  proposedCount: number;
  totalCount: number;
}): string {
  const { source, proposedCount, totalCount } = args;
  const batch = proposedCount > 1;

  if (source === 'live-anchorless') {
    return batch
      ? `${proposedCount} of ${totalCount} suggestions in this set came back without saying which paragraph to change. We found the wording they quoted — check the first one below before placing them.`
      : 'This suggestion came back without saying which paragraph to change. We found the wording it quoted, but we cannot confirm it is the clause that was meant.';
  }

  return batch
    ? `${proposedCount} of ${totalCount} suggestions in this set came from an earlier session, before suggestions carried a paragraph reference. We found the wording they quoted — check the first one below before placing them.`
    : 'This suggestion came from an earlier session, before suggestions carried a paragraph reference. We found the wording it quoted, but we cannot confirm it is the clause that was meant.';
}
