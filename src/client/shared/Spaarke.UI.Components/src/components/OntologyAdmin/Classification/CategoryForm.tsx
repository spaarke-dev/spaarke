/**
 * CategoryForm - the Classification area's create / edit form (task 103; D-22, FR-59).
 *
 * Name and guidance only. The server is the authority on every rule: a rename is refused (409, naming the rules) while any policy
 * version reads the category, a name already in use is refused, a stale form is refused. This form shows what the server said and
 * keeps what the administrator typed. A new category is saved Off; the form says so.
 *
 * Fluent v9 semantic tokens only (ADR-021); no hex, rgb or named colour literals.
 *
 * Task: spaarke-ontology-platform-r1, task 103.
 */

import * as React from 'react';
import {
  Button,
  Field,
  Input,
  MessageBar,
  MessageBarBody,
  MessageBarTitle,
  Text,
  Textarea,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import { ClassificationRequestError } from './classificationService';
import type { ClassificationCategory } from './classificationService';
import { groupRuleReads } from './classificationView';

/** The classifier prompt guidance column holds at most this many characters (BFF `MaxGuidanceLength`). */
export const MAX_GUIDANCE_LENGTH = 2000;
/** The 409s that refuse a rename without a list of rules: the caller's read cannot show every rule, or the routing configuration names the category. */
const RENAME_REFUSALS: ReadonlySet<string> = new Set([
  'ontology.admin.classification.rename_unverifiable',
  'ontology.admin.classification.rename_routed',
]);
/** The longest category name the BFF accepts. */
export const MAX_NAME_LENGTH = 100;

export interface CategoryFormValues {
  name: string;
  guidance: string;
}

export interface CategoryFormProps {
  /** The category being edited; omit to create a new one (saved Off). */
  category?: ClassificationCategory;
  /** Saves the values. Throws a `ClassificationRequestError` to show its message on the form. */
  onSubmit: (values: CategoryFormValues) => Promise<void>;
  onCancel: () => void;
}

const useStyles = makeStyles({
  form: {
    display: 'flex',
    flexDirection: 'column',
    rowGap: tokens.spacingVerticalM,
    maxWidth: '640px',
    paddingTop: tokens.spacingVerticalM,
    paddingBottom: tokens.spacingVerticalM,
  },
  actions: { display: 'flex', columnGap: tokens.spacingHorizontalS },
  hint: { color: tokens.colorNeutralForeground3 },
  blockers: { margin: 0, paddingLeft: tokens.spacingHorizontalXL },
});

export const CategoryForm: React.FC<CategoryFormProps> = ({ category, onSubmit, onCancel }) => {
  const styles = useStyles();
  const [name, setName] = React.useState(category?.name ?? '');
  const [guidance, setGuidance] = React.useState(category?.guidance ?? '');
  const [saving, setSaving] = React.useState(false);
  const [error, setError] = React.useState<ClassificationRequestError | null>(null);
  const alive = React.useRef(true);
  React.useEffect(() => {
    alive.current = true;
    return () => {
      alive.current = false;
    };
  }, []);

  const trimmed = name.trim();
  const changed = category === undefined || trimmed !== category.name || guidance.trim() !== (category.guidance ?? '');
  const canSave = !saving && trimmed.length > 0 && changed;
  const readers = category ? groupRuleReads(category.readBy) : [];

  const submit = React.useCallback(
    async (event: React.FormEvent) => {
      event.preventDefault();
      if (!canSave) return;
      setSaving(true);
      setError(null);
      try {
        await onSubmit({ name: trimmed, guidance: guidance.trim() });
      } catch (err) {
        if (alive.current) {
          setError(
            err instanceof ClassificationRequestError ? err : new ClassificationRequestError('The category could not be saved.', null)
          );
        }
      } finally {
        if (alive.current) setSaving(false);
      }
    },
    [canSave, onSubmit, trimmed, guidance]
  );

  const nameErrors = error?.fieldErrors.name;
  const guidanceErrors = error?.fieldErrors.guidance;
  const renameBlocked = error?.reasonCode === 'ontology.admin.classification.rename_blocked';
  const renameRefused = renameBlocked || RENAME_REFUSALS.has(error?.reasonCode ?? '');

  return (
    <form className={styles.form} onSubmit={submit} data-testid="classification-form" aria-label={category ? `Edit ${category.name}` : 'New category'}>
      {category === undefined && (
        <Text className={styles.hint} data-testid="classification-form-new-note">
          A new category is saved Off. Turn it On from the list once its guidance reads right; it changes how the next email is classified.
        </Text>
      )}
      {readers.length > 0 && (
        <Text className={styles.hint} data-testid="classification-form-readers">
          Read by {readers.map(r => r.policyCode).join(', ')}. The name cannot be changed while a rule reads this category.
        </Text>
      )}

      {error && !nameErrors && !guidanceErrors && (
        <MessageBar intent={renameRefused || error.httpStatus === 409 ? 'warning' : 'error'} data-testid="classification-form-error">
          <MessageBarBody>
            <MessageBarTitle>{renameRefused ? 'Rename refused' : 'Not saved'}</MessageBarTitle>
            {error.message}
            {renameBlocked && error.blockedBy.length > 0 && (
              <ul className={styles.blockers} data-testid="classification-form-blockers">
                {groupRuleReads(error.blockedBy).map(r => (
                  <li key={r.policyId}>
                    {r.policyCode}
                    {r.policyName ? ` (${r.policyName})` : ''}: {r.versions}
                  </li>
                ))}
              </ul>
            )}
          </MessageBarBody>
        </MessageBar>
      )}

      <Field
        label="Name"
        required
        validationState={nameErrors ? 'error' : 'none'}
        validationMessage={nameErrors?.join(' ')}
        hint="This is also the value the classifier chooses from."
      >
        <Input
          value={name}
          maxLength={MAX_NAME_LENGTH}
          onChange={(_e, data) => setName(data.value)}
          data-testid="classification-form-name"
        />
      </Field>

      <Field
        label="Guidance"
        validationState={guidanceErrors ? 'error' : 'none'}
        validationMessage={guidanceErrors?.join(' ')}
        hint={`What the classifier is told about this category. ${guidance.length} of ${MAX_GUIDANCE_LENGTH} characters.`}
      >
        <Textarea
          value={guidance}
          rows={6}
          maxLength={MAX_GUIDANCE_LENGTH}
          onChange={(_e, data) => setGuidance(data.value)}
          data-testid="classification-form-guidance"
        />
      </Field>

      <div className={styles.actions}>
        <Button type="submit" appearance="primary" disabled={!canSave} data-testid="classification-form-save">
          {saving ? 'Saving...' : category ? 'Save' : 'Create (Off)'}
        </Button>
        <Button type="button" onClick={onCancel} disabled={saving} data-testid="classification-form-cancel">
          Cancel
        </Button>
      </div>
    </form>
  );
};

CategoryForm.displayName = 'CategoryForm';
