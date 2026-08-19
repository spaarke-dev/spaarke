/**
 * ComposeSaveNameDialog.tsx — "Name this document" dialog for Compose (FR-02,
 * spaarkeai-compose-r7 task 030 / UC-3).
 *
 * Purpose:
 *   On the FIRST create-on-save of a new (born-in-editor / blank / template) document AND on
 *   Save As, prompt for the document name before the server persists it — removing the silent
 *   `Untitled document.docx` fallback (spec FR-02). The host (`ComposeWorkspace`) owns the save
 *   trigger; this component is presentation only: it collects a trimmed name and hands it back
 *   via `onSubmit`, and the host threads it into the create-on-save `displayName`.
 *
 *   ONE editable "Document name" field drives BOTH the SPE file name and the sprk_document
 *   record name: the server's `ResolveFileName(displayName)` derives the `.docx` file name and
 *   `sprk_documentname` takes the same value (ComposeService.cs). A live, read-only "Saved as"
 *   preview shows the derived `<name>.docx` so the user sees the file that will be created. Per
 *   CLAUDE.md §11 there is no separate editable file-name field — no acceptance criterion
 *   requires a file name distinct from the document title, and the single `displayName` contract
 *   (SaveComposeDocumentBody, task 100) already carries the name end-to-end.
 *
 * Component justification (CLAUDE.md §11):
 *   - Existing: `FormModal` (`@spaarke/ui-components` SprkModal preset, ADR-050) is the canonical
 *     light-form modal — REUSED as the envelope (Cancel left / primary right, `explicit` dismiss).
 *     `ComposeApplyTemplateDialog` is the sibling composed-FormModal precedent in this lib.
 *   - Extension: the preset carries no field content by contract — this file IS that consumer.
 *     Inlining into ComposeWorkspace (~4000 LOC) would mix naming UI with the document state machine.
 *   - Cost-of-doing-nothing: FR-02's client affordance does not exist — Success Criterion 2 (the SPE
 *     record uses the entered name; no `Untitled document.docx`) is unreachable from the product.
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
  preview: {
    color: tokens.colorNeutralForeground2,
    fontSize: tokens.fontSizeBase200,
    lineHeight: tokens.lineHeightBase200,
    marginTop: tokens.spacingVerticalXS,
  },
});

// ---------------------------------------------------------------------------
// Name normalization (shared with the host's threading — keep in sync with
// ComposeService.ResolveFileName which appends `.docx`)
// ---------------------------------------------------------------------------

/**
 * Characters illegal in an SPE / Windows file name — stripped so the PUT-by-path cannot fail.
 * Spaces, hyphens, and ordinary punctuation are LEGAL and preserved (e.g. "Master Services
 * Agreement" survives intact); only `< > : " / \ | ? *` are removed.
 */
const ILLEGAL_FILENAME_CHARS = /[<>:"/\\|?*]/g;

/** Trim + strip illegal file-name characters (spaces/hyphens kept). Empty when nothing usable remains. */
export function sanitizeComposeName(raw: string): string {
  return raw.replace(ILLEGAL_FILENAME_CHARS, '').trim();
}

/** Mirror of `ComposeService.ResolveFileName` — the `.docx` file the server will create from a name. */
export function deriveComposeFileName(name: string): string {
  const clean = sanitizeComposeName(name);
  if (clean.length === 0) return '';
  return /\.docx$/i.test(clean) ? clean : `${clean}.docx`;
}

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface ComposeSaveNameDialogProps {
  /** Whether the dialog is visible. Host owns the open state. */
  open: boolean;

  /**
   * Which save this modal is gating. `'first-save'` = first create-on-save of a new document;
   * `'save-as'` = an explicit Save As fork. Drives the title + submit label only.
   */
  mode: 'first-save' | 'save-as';

  /** Pre-fill for the name field (e.g. the source doc name on Save As). Empty for a blank first save. */
  defaultName?: string;

  /** Submit handler — receives the trimmed, sanitized document name. Host runs the save. */
  onSubmit: (documentName: string) => void;

  /** Close/cancel handler. Hosts should no-op while `busy`. */
  onClose: () => void;

  /** True while the gated save is in flight — disables inputs + buttons. */
  busy?: boolean;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

/**
 * `ComposeSaveNameDialog` — a single "Document name" input + a derived `<name>.docx` preview, on
 * the canonical `FormModal` envelope (Cancel left, primary right, explicit dismiss). The input
 * seeds from `defaultName` each time the dialog opens so Save As starts from the source name and a
 * first save starts blank.
 */
export function ComposeSaveNameDialog(props: ComposeSaveNameDialogProps): React.JSX.Element {
  const { open, mode, defaultName = '', onSubmit, onClose, busy = false } = props;
  const styles = useStyles();

  const [name, setName] = React.useState(defaultName);

  // Seed the field from `defaultName` on each open (Save As → source name; first save → blank).
  // Keyed on `open` so a reopen re-seeds; edits during an open session are preserved.
  React.useEffect(() => {
    if (open) setName(defaultName);
  }, [open, defaultName]);

  const cleaned = sanitizeComposeName(name);
  const derivedFileName = deriveComposeFileName(name);

  const handleSubmit = React.useCallback((): void => {
    if (cleaned.length === 0 || busy) return;
    onSubmit(cleaned);
  }, [cleaned, busy, onSubmit]);

  // Render nothing while closed (hooks above still run — the seed effect fires on open). Matches
  // the ComposeApplyTemplateDialog idiom so per-file `jest.mock('@spaarke/ui-components')` factories
  // that stub only exercised members stay valid.
  if (!open) return <></>;

  const title = mode === 'save-as' ? 'Save a copy as' : 'Name this document';
  const submitLabel = mode === 'save-as' ? 'Save copy' : 'Save';

  return (
    <FormModal
      open={open}
      onClose={onClose}
      onSubmit={handleSubmit}
      title={title}
      size="sm"
      submitLabel={busy ? 'Saving…' : submitLabel}
      submitDisabled={cleaned.length === 0}
      busy={busy}
    >
      <div data-testid="compose-save-name-dialog">
        <Field label="Document name" required>
          <Input
            value={name}
            onChange={(_, data) => setName(data.value)}
            onKeyDown={e => {
              if (e.key === 'Enter') {
                e.preventDefault();
                handleSubmit();
              }
            }}
            disabled={busy}
            placeholder="e.g. Master Services Agreement"
            data-testid="compose-save-name-input"
            // Autofocus so the user can type immediately on open.
            autoFocus
          />
        </Field>
      </div>
      <Text className={styles.hint}>
        This name is used for the saved document and its file. You can rename it later.
      </Text>
      {derivedFileName ? (
        <Text className={styles.preview} data-testid="compose-save-name-preview">
          Saved as: {derivedFileName}
        </Text>
      ) : null}
    </FormModal>
  );
}

export default ComposeSaveNameDialog;
