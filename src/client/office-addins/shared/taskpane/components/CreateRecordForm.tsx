import React, { useCallback, useEffect, useRef, useState } from 'react';
import {
  makeStyles,
  tokens,
  Button,
  Dropdown,
  Field,
  Input,
  Label,
  Option,
  Spinner,
  Text,
  Textarea,
} from '@fluentui/react-components';
// Owner decision C (ADR-012 amended 2026-10-05): the shared, host-agnostic LookupField by EXACT alias — never the
// `@spaarke/ui-components` barrel. Types from `shared/types/spaarke-lookup-field.d.ts`.
import { LookupField, type ILookupItem } from '@spaarke/ui-components/lookup-field';
import type { EntityType } from '../hooks/useEntitySearch';
import { REFERENCE_LIST_PLURAL, type ReferenceListState } from '../hooks/useCreateRecordFormData';
import type { ReferenceListName } from '../services/referenceListService';
import type { DefaultAssignee } from '../services/quickCreateDefaultsService';
import type { ContactOption } from './views/CreateTodoView';

/**
 * CreateRecordForm — the "+ New" form inside `RelatedToPicker` (task 100, owner UAT round 5 item 3, decisions B + C).
 *
 *   Matter : Name*, Description, Matter Type*, Practice Area*, Assigned To Internal (prefilled)
 *   Project: Name*, Project Type, Description, Assigned To Internal (prefilled)
 *   Invoice: Name*, Description, Assigned To (prefilled)
 *
 * - Built from shared field components: the contact picker is the shared `LookupField` (`onSearch` injected — the
 *   pane's one contact search, `App.handleSearchContacts`); the reference fields are Fluent `Dropdown`s fed by the
 *   BFF's load-once lists (`useCreateRecordFormData`). The shared Create*Wizard steps are NOT reused (they write
 *   Dataverse directly and carry `Xrm` dependencies); the record is created by the BFF quick-create (WP-3).
 * - Create stays disabled until every required field is set; pressing Enter in the name box with one missing shows a
 *   labelled, announced (`role="alert"`) message per field instead.
 * - Assigned To is prefilled with the user's own linked contact (the server's default) and is optional. Clearing it
 *   on a Matter or Project means "the server's default" — which IS the user — so the field says so; an Invoice has no
 *   default, so a cleared Invoice is unassigned.
 * - A list that failed to load shows its message with Retry (never a required field that blocks silently); a list
 *   that loaded empty says so.
 * - One column, full width: fits the 320 px pane. Fluent tokens only (ADR-021).
 */

/** What the form submits. Only the fields the record type has are set; GUIDs are canonicalized by the caller. */
export interface CreateRecordInput {
  name: string;
  description?: string;
  matterTypeId?: string;
  practiceAreaId?: string;
  projectTypeId?: string;
  assignedToContactId?: string;
}

export interface CreateRecordFormProps {
  /** The record type being created (the picker's selected pill). */
  type: EntityType;
  matterTypes: ReferenceListState;
  practiceAreas: ReferenceListState;
  projectTypes: ReferenceListState;
  /** The Assigned To prefill — the user's own linked contact — or null. */
  defaultAssignee: DefaultAssignee | null;
  /** Contact search for Assigned To (the pane's one contact search). */
  onSearchContacts: (query: string) => Promise<ContactOption[]>;
  /** Creates the record. Throws with a readable message on failure; the form shows it. */
  onSubmit: (input: CreateRecordInput) => Promise<void>;
  onCancel: () => void;
  disabled?: boolean;
  /** Receives the name input (the host focuses it when the form opens). */
  nameInputRef?: (el: HTMLInputElement | null) => void;
}

const useStyles = makeStyles({
  form: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalM, minWidth: 0 },
  field: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalXXS, minWidth: 0 },
  errorRow: { display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalS, flexWrap: 'wrap' },
  fieldError: { color: tokens.colorPaletteRedForeground1, fontSize: tokens.fontSizeBase200 },
  note: { color: tokens.colorNeutralForeground3 },
  actions: {
    display: 'flex',
    justifyContent: 'space-between',
    gap: tokens.spacingHorizontalS,
    flexWrap: 'wrap',
  },
});

/** A contact as the shared LookupField shows it: "Name (email)" disambiguates two people with one name (task 091). */
function toLookupItem(contact: { id: string; name: string; email?: string }): ILookupItem {
  return {
    id: contact.id,
    name: contact.email ? `${contact.name} (${contact.email})` : contact.name,
    ...(contact.email ? { email: contact.email } : {}),
    entityType: 'contact',
  };
}

/** One reference dropdown: required marker, loading/empty/failed states, Retry. */
const ReferenceField: React.FC<{
  id: string;
  label: string;
  list: ReferenceListName;
  state: ReferenceListState;
  required: boolean;
  value: string;
  onChange: (id: string) => void;
  error: string | null;
  disabled: boolean;
}> = ({ id, label, list, state, required, value, onChange, error, disabled }) => {
  const styles = useStyles();
  const plural = REFERENCE_LIST_PLURAL[list];
  const singular = label.toLowerCase();
  const selectedName = state.options.find(o => o.id === value)?.name;
  return (
    <div className={styles.field}>
      <Label htmlFor={id} required={required}>
        {label}
      </Label>
      <Dropdown
        id={id}
        placeholder={state.loading ? `Loading ${plural}…` : `Select a ${singular}`}
        value={selectedName ?? ''}
        selectedOptions={value ? [value] : []}
        onOptionSelect={(_, data) => onChange(data.optionValue ?? '')}
        disabled={disabled || state.loading || !!state.error}
        aria-label={label}
        aria-required={required ? 'true' : 'false'}
        aria-invalid={!!error || !!state.error}
      >
        {state.options.map(o => (
          <Option key={o.id} value={o.id} text={o.name}>
            {o.name}
          </Option>
        ))}
      </Dropdown>
      {/* A failed load and "zero active rows, loaded fine" are different states (task 038 coordinator fix): only the
          genuine empty table gets the quiet note; a failure gets its own message + Retry; never both. */}
      {!state.loading && !state.error && state.options.length === 0 && (
        <Text size={200} className={styles.note}>
          No {plural} are available right now.
        </Text>
      )}
      {!state.loading && state.error && (
        <div className={styles.errorRow}>
          <Text size={200} className={styles.fieldError} role="alert">
            {state.error}
          </Text>
          <Button appearance="outline" size="small" onClick={() => state.retry()}>
            Retry
          </Button>
        </div>
      )}
      {error && (
        <Text size={200} className={styles.fieldError} role="alert">
          {error}
        </Text>
      )}
    </div>
  );
};

export const CreateRecordForm: React.FC<CreateRecordFormProps> = ({
  type,
  matterTypes,
  practiceAreas,
  projectTypes,
  defaultAssignee,
  onSearchContacts,
  onSubmit,
  onCancel,
  disabled = false,
  nameInputRef,
}) => {
  const styles = useStyles();
  const isMatter = type === 'Matter';
  const isProject = type === 'Project';
  const assigneeLabel = type === 'Invoice' ? 'Assigned To' : 'Assigned To Internal';

  const [name, setName] = useState('');
  const [description, setDescription] = useState('');
  const [matterTypeId, setMatterTypeId] = useState('');
  const [practiceAreaId, setPracticeAreaId] = useState('');
  const [projectTypeId, setProjectTypeId] = useState('');
  const [assignee, setAssignee] = useState<ILookupItem | null>(defaultAssignee ? toLookupItem(defaultAssignee) : null);
  const [assigneeTouched, setAssigneeTouched] = useState(false);
  const [matterTypeError, setMatterTypeError] = useState<string | null>(null);
  const [practiceAreaError, setPracticeAreaError] = useState<string | null>(null);
  const [creating, setCreating] = useState(false);
  const [createError, setCreateError] = useState<string | null>(null);

  // Task 101 (owner UAT round 6 item 1): the picker's type pill can change while the form is open. Name, Description
  // and Assigned To exist on every type and carry over; the type-only values (Matter Type, Practice Area, Project Type)
  // and their messages do not. The required fields and reference lists follow `type` on their own.
  const previousType = useRef(type);
  useEffect(() => {
    if (previousType.current === type) return;
    previousType.current = type;
    setMatterTypeId('');
    setPracticeAreaId('');
    setProjectTypeId('');
    setMatterTypeError(null);
    setPracticeAreaError(null);
    setCreateError(null);
  }, [type]);

  // The prefill can arrive after the form opened (it is loaded when "+ New" is first pressed). It fills the field only
  // until the user has touched it — a choice the user made (including clearing it) is never overwritten.
  useEffect(() => {
    if (!assigneeTouched && defaultAssignee) setAssignee(toLookupItem(defaultAssignee));
  }, [defaultAssignee, assigneeTouched]);

  const handleAssigneeChange = useCallback((item: ILookupItem | null) => {
    setAssigneeTouched(true);
    setAssignee(item);
  }, []);

  // The contact search needs 2+ characters (the BFF answers 400 below that). LookupField's browse icon searches with
  // the CURRENT term, possibly empty — answer that locally instead of sending a request that can only fail.
  const searchContacts = useCallback(
    async (query: string): Promise<ILookupItem[]> =>
      query.trim().length < 2 ? [] : (await onSearchContacts(query)).map(toLookupItem),
    [onSearchContacts]
  );

  const requiredMissing = isMatter && (!matterTypeId || !practiceAreaId);
  const canCreate = !disabled && !creating && name.trim().length > 0 && !requiredMissing;

  const handleSubmit = async () => {
    const trimmedName = name.trim();
    if (trimmedName.length === 0 || creating) return;

    // Required on a Matter (owner decisions 2026-09-11 and B). The server never rejects a missing one; this form
    // always sends both. Each message is role="alert", so it is announced (NFR-11) the moment it renders.
    if (isMatter && (!matterTypeId || !practiceAreaId)) {
      setMatterTypeError(matterTypeId ? null : 'Choose a Matter Type before creating a Matter.');
      setPracticeAreaError(practiceAreaId ? null : 'Choose a Practice Area before creating a Matter.');
      return;
    }

    const trimmedDescription = description.trim();
    const input: CreateRecordInput = {
      name: trimmedName,
      ...(trimmedDescription ? { description: trimmedDescription } : {}),
      ...(isMatter ? { matterTypeId, practiceAreaId } : {}),
      ...(isProject && projectTypeId ? { projectTypeId } : {}),
      ...(assignee ? { assignedToContactId: assignee.id } : {}),
    };

    setCreating(true);
    setCreateError(null);
    try {
      await onSubmit(input);
    } catch (err) {
      // Task 053: the SERVER's own message (e.g. owner_unresolved, assignee_inaccessible), not a generic fallback.
      setCreateError(err instanceof Error ? err.message : `Couldn't create the ${type}.`);
    } finally {
      setCreating(false);
    }
  };

  const fieldsDisabled = disabled || creating;

  const nameField = (
    <div className={styles.field}>
      <Label htmlFor="new-record-name" required>
        Name
      </Label>
      <Input
        id="new-record-name"
        ref={el => nameInputRef?.(el)}
        value={name}
        onChange={(_, d) => setName(d.value)}
        onKeyDown={e => {
          if (e.key === 'Enter') void handleSubmit();
        }}
        placeholder={`New ${type} name`}
        disabled={fieldsDisabled}
        aria-label={`New ${type} name`}
        aria-required="true"
      />
    </div>
  );

  const descriptionField = (
    <Field label="Description">
      <Textarea
        value={description}
        onChange={(_, d) => setDescription(d.value)}
        resize="vertical"
        rows={2}
        disabled={fieldsDisabled}
      />
    </Field>
  );

  const assigneeField = (
    <div className={styles.field}>
      <LookupField
        label={assigneeLabel}
        value={assignee}
        onChange={handleAssigneeChange}
        onSearch={searchContacts}
        minSearchLength={2}
        placeholder="Search contacts"
      />
      {/* Clearing it on a Matter/Project means "the server's default" — the user themself — so say so, honestly. */}
      {!isMatter && !isProject ? null : defaultAssignee && !assignee ? (
        <Text size={200} className={styles.note}>
          Left empty, it is assigned to you ({defaultAssignee.name}).
        </Text>
      ) : null}
    </div>
  );

  return (
    <div className={styles.form} role="group" aria-label={`New ${type}`}>
      {nameField}
      {isMatter && (
        <>
          {descriptionField}
          <ReferenceField
            id="new-matter-type"
            label="Matter Type"
            list="matter-types"
            state={matterTypes}
            required
            value={matterTypeId}
            onChange={id => {
              setMatterTypeId(id);
              setMatterTypeError(null);
            }}
            error={matterTypeError}
            disabled={fieldsDisabled}
          />
          <ReferenceField
            id="new-practice-area"
            label="Practice Area"
            list="practice-areas"
            state={practiceAreas}
            required
            value={practiceAreaId}
            onChange={id => {
              setPracticeAreaId(id);
              setPracticeAreaError(null);
            }}
            error={practiceAreaError}
            disabled={fieldsDisabled}
          />
        </>
      )}
      {isProject && (
        <>
          <ReferenceField
            id="new-project-type"
            label="Project Type"
            list="project-types"
            state={projectTypes}
            required={false}
            value={projectTypeId}
            onChange={setProjectTypeId}
            error={null}
            disabled={fieldsDisabled}
          />
          {descriptionField}
        </>
      )}
      {!isMatter && !isProject && descriptionField}
      {assigneeField}

      {createError && (
        <Text size={200} className={styles.fieldError} role="alert">
          {createError}
        </Text>
      )}

      {/* Task 101 (owner item 3): Cancel left (secondary), Create right (primary) — the Save tab's footer pattern. */}
      <div className={styles.actions}>
        <Button appearance="secondary" onClick={onCancel} disabled={creating}>
          Cancel
        </Button>
        <Button appearance="primary" onClick={() => void handleSubmit()} disabled={!canCreate}>
          {creating ? <Spinner size="tiny" /> : 'Create'}
        </Button>
      </div>
    </div>
  );
};

export default CreateRecordForm;
