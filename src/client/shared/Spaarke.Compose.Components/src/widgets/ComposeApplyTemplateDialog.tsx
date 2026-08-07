/**
 * ComposeApplyTemplateDialog.tsx — "Apply firm template" dialog for Compose (FR-05,
 * spaarkeai-compose-r6 task 032).
 *
 * Purpose:
 *   Minimal v1 template-select surface: a template NAME or ID text input + Apply. The host
 *   (`ComposeWorkspace`) owns the `POST /api/compose/documents/{id}/apply-template` call (the
 *   030 part-merge engine fed by the 031-resolved firm/matter `.dotx`), the post-apply remount,
 *   and the degradation-banner surfacing — this component is presentation only.
 *
 *   A full template PICKER (list/browse of available templates) is deliberately OUT of v1 scope
 *   (052+ per the orchestrator's task-032 design decision) — the input accepts a template record
 *   id (GUID) or the exact template `title`, matching `IComposeTemplateSource.ResolveAsync`.
 *
 * Component justification (CLAUDE.md §11):
 *   - Existing: `FormModal` (`@spaarke/ui-components` SprkModal preset) is the canonical
 *     light-form modal — REUSED as the envelope (Cancel left / primary right, `explicit`
 *     dismiss, busy spinner). `ComposeConflictDialog` is the sibling composed-SprkModal
 *     precedent in this lib.
 *   - Extension: the preset carries no field content by contract ("the consumer supplies only
 *     the fields") — this file IS that consumer. Inlining into ComposeWorkspace (~3800 LOC)
 *     would mix template-apply UI with the document state machine.
 *   - Cost-of-doing-nothing: FR-05's client affordance does not exist — Success Criterion 3
 *     (a document merged through a firm template carries its chrome) is unreachable from the
 *     product.
 *
 * Constraints honored (BINDING):
 *   - ADR-021 — Fluent v9 semantic tokens ONLY (zero hardcoded colors); renders correctly in
 *     light AND dark themes (the SprkModal shell + `tokens.*` inherit the host FluentProvider).
 *   - ADR-028 — no auth work here; the host owns `authenticatedFetch`.
 *   - ADR-050 — canonical SprkModal shell via the FormModal preset (no hand-rolled Dialog).
 */

import * as React from 'react';
import { Field, Input, Text, makeStyles, tokens } from '@fluentui/react-components';
import { FormModal } from '@spaarke/ui-components';

// ---------------------------------------------------------------------------
// Styles — Fluent v9 semantic tokens only (ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  hint: {
    color: tokens.colorNeutralForeground2,
    fontSize: tokens.fontSizeBase200,
    lineHeight: tokens.lineHeightBase200,
  },
  error: {
    color: tokens.colorPaletteRedForeground1,
    fontSize: tokens.fontSizeBase200,
    lineHeight: tokens.lineHeightBase200,
  },
});

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface ComposeApplyTemplateDialogProps {
  /** Whether the dialog is visible. Host owns the open state. */
  open: boolean;

  /** Apply handler — receives the trimmed template name/id. Host runs the POST. */
  onApply: (templateIdOrName: string) => void;

  /** Close/cancel handler. Hosts should no-op while `isApplying` (FormModal contract). */
  onClose: () => void;

  /** True while the apply-template POST is in flight — disables both buttons + spinner. */
  isApplying?: boolean;

  /** Failure surface (template not found / merge failure detail). Cleared by the host on
   *  the next attempt. Rendered inside the dialog so the user can correct the name + retry. */
  errorMessage?: string | null;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

/**
 * `ComposeApplyTemplateDialog` — template name/id input + Apply, on the canonical
 * `FormModal` envelope (Cancel left, primary right, explicit dismiss, busy spinner).
 * The input clears when the dialog closes so a reopen starts fresh.
 */
export function ComposeApplyTemplateDialog(props: ComposeApplyTemplateDialogProps): React.JSX.Element {
  const { open, onApply, onClose, isApplying = false, errorMessage } = props;
  const styles = useStyles();

  const [templateIdOrName, setTemplateIdOrName] = React.useState('');

  // Reset the input on close → reopen (fresh start; never reapplies a stale value silently).
  React.useEffect(() => {
    if (!open) setTemplateIdOrName('');
  }, [open]);

  const trimmed = templateIdOrName.trim();

  const handleSubmit = React.useCallback((): void => {
    if (trimmed.length === 0 || isApplying) return;
    onApply(trimmed);
  }, [trimmed, isApplying, onApply]);

  // Render nothing while closed (hooks above still run — the reset effect fires on close).
  // Deliberate divergence from the mount-always ComposeConflictDialog idiom: the closed dialog
  // has no exit animation to preserve, and not mounting the FormModal shell keeps the host's
  // existing per-file `jest.mock('@spaarke/ui-components')` factories (which stub only the
  // members each suite exercises) valid without churn.
  if (!open) return <></>;

  return (
    <FormModal
      open={open}
      onClose={onClose}
      onSubmit={handleSubmit}
      title="Apply firm template"
      size="sm"
      submitLabel={isApplying ? 'Applying…' : 'Apply template'}
      submitDisabled={trimmed.length === 0}
      busy={isApplying}
    >
      <div data-testid="compose-apply-template-dialog">
        <Field label="Template name or ID" hint="The firm/matter template's exact title, or its record ID." required>
          <Input
            value={templateIdOrName}
            onChange={(_, data) => setTemplateIdOrName(data.value)}
            onKeyDown={e => {
              if (e.key === 'Enter') {
                e.preventDefault();
                handleSubmit();
              }
            }}
            disabled={isApplying}
            placeholder="e.g. Firm Standard Pleading"
            data-testid="compose-apply-template-input"
          />
        </Field>
      </div>
      <Text className={styles.hint}>
        The template&apos;s house style (styles, numbering, headers and footers) is applied to the saved document as a
        new version — your document&apos;s text is kept. The previous version stays available in version history.
      </Text>
      {errorMessage ? (
        <Text className={styles.error} role="alert" data-testid="compose-apply-template-error">
          {errorMessage}
        </Text>
      ) : null}
    </FormModal>
  );
}

export default ComposeApplyTemplateDialog;
