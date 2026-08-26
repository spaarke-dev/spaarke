/**
 * RecordHeader renderer CONTRACT suite — record-header-and-notepad-r2 FR-10.
 *
 * FR-10 defines "the renderer contract" as *the TextField contract, verbatim*:
 * every field renderer must obey the SAME semantics — props shape, self-applied
 * `gridColumn`, editable gate, draft/commit/cancel, revert-on-reject-stay-in-edit,
 * blur-commits, and the em-dash empty state — even though each one edits a
 * different value TYPE with a different EDITOR.
 *
 * This file is that acceptance. It is a single parameterized suite driven by
 * one small adapter per renderer: the adapter supplies the renderer's
 * *gestures* (how you enter edit, stage a draft, commit, cancel) and its
 * *payload*, while every ASSERTION below is shared and identical across all
 * six. Identical semantics, per-renderer gestures — deliberately not a
 * lowest-common-denominator suite.
 *
 * ── Coverage ────────────────────────────────────────────────────────────────
 * Six adapters, none skipped: TextField, TextareaField, OptionSetField,
 * DateField, NumberField, BooleanField.
 *
 * ── Deliberate exclusion ────────────────────────────────────────────────────
 * The RecordHeader `LookupField` (`fields/LookupField.tsx`, barrel-aliased
 * `RecordHeaderLookupField`) is OUT of this suite's scope by design — this
 * remains true after task 023 (FR-15/FR-15a) added its editable mode: the
 * "editor" is the OOB native `Xrm.Utility.lookupObjects` picker DIALOG, not
 * an inline Input/Dropdown/Switch, so there is no separable
 * draft/stage/blur/revert-on-reject gesture to drive through this suite's
 * shared assertions — commit is a single atomic pick-and-resolve with no
 * pending-draft state in between. Its value shape (`ILookupFieldValue`) also
 * remains non-scalar. Read-only behavior is covered by `fields.test.tsx`;
 * editable-mode behavior is covered by `LookupField.edit.test.tsx`.
 * (Not to be confused with the unrelated editable `components/LookupField/`.)
 *
 * ── Documented per-renderer allowances (suite PARAMETERS, never skips) ──────
 *  - `TextareaField` commits on **Ctrl/Cmd+Enter**; plain Enter inserts a
 *    newline. Shipped R1 behavior, deliberately preserved (multiline UX).
 *  - `OptionSetField` commits on **option-selection** in the Dropdown — its
 *    draft-change gesture and its commit gesture are the same act, so it is
 *    the suite's only `stageDraft: undefined` ("immediate-commit") adapter.
 *    The blur assertion branches accordingly (see the blur test's comment);
 *    it is asserted for all six, never skipped for any.
 *  - `DateField` is driven in `format="datetime"` mode, where its editor is a
 *    single Fluent `Input type="datetime-local"` carrying both halves of the
 *    value. Its draft is genuinely *pending* — typing stages, Enter/blur
 *    commits — so it needs no allowance beyond the wall-clock string shape of
 *    its draft text. (Before the NFR-02 rework it used
 *    `@fluentui/react-datepicker-compat` plus a companion time input, and
 *    calendar-day selection committed immediately; that special case retired
 *    with the picker.)
 *  - `BooleanField` stages its draft by toggling the Switch, then commits on
 *    Enter/blur — and its Switch is **permanently visible** while editable
 *    (`alwaysEditing`), so it has no click-to-edit reveal and never returns to
 *    a read-mode value slot. UAT drove that: a hidden editor rendered an unset
 *    flag as a grey cell containing an em-dash, which reads as broken rather
 *    than settable, and a Switch's position IS its value. The flag relaxes
 *    exactly three assertions (the reveal, and the two post-commit/cancel
 *    "left edit mode" checks); the other eighty-eight apply unchanged.
 *  - Per **D-10**, all six ACCEPT `required` but only `TextField` renders the
 *    `*` marker — asserted as a negative for the other five.
 *
 * Per ADR-038: this is a KEEP-category suite — it guards a cross-component
 * contract that outlives the project, tests only the public props surface, and
 * mocks no renderer internals. Per ADR-022 the file stays React 16/17-safe.
 *
 * @see FR-10, FR-11, D-10 record-header-and-notepad-r2 spec
 * @see fields/TextField.tsx — the contract's source of truth
 */

import * as React from 'react';
import { act, fireEvent, screen, waitFor, type RenderResult } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
// Imported through the BARREL on purpose: this import is itself the check that
// task 015's `fields/index.ts` wiring type-checks for all six renderers.
import { BooleanField, DateField, NumberField, OptionSetField, TextField, TextareaField } from '../fields';

/** The em-dash every renderer must show for null / undefined / '' (FR-11). */
const EM_DASH = '—';

/** Option list backing the OptionSetField adapter. */
const STATUS_OPTIONS = [
  { value: 1, label: 'Open' },
  { value: 2, label: 'Closed' },
];

/** Fixed instant so formatted-output expectations are deterministic. */
const DATETIME_ISO = '2026-08-21T14:30:00.000Z';

/**
 * Mirrors DateField's `toInputValue` so we can predict the reverted draft.
 * LOCAL calendar fields on purpose — a `datetime-local` input has no zone, and
 * building this from `toISOString()` would shift the day (the failure mode
 * DateField's own suite guards directly).
 */
const toDateTimeInputValue = (d: Date): string =>
  `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}` +
  `T${String(d.getHours()).padStart(2, '0')}:${String(d.getMinutes()).padStart(2, '0')}`;

/** The committed value's wall-clock form, and the draft staged on top of it. */
const DATETIME_LOCAL = toDateTimeInputValue(new Date(DATETIME_ISO));
// Same local DAY, a different time-of-day. 14:30 UTC cannot be 09:15 in any
// real zone (that would need a -05:15 offset), so the staged draft is always
// genuinely different from the committed value — without which `commit` would
// short-circuit as a no-op and the commit assertions would see zero saves.
const DATETIME_LOCAL_STAGED = `${DATETIME_LOCAL.slice(0, 10)}T09:15`;

/** A resolved `onSave` double. */
const makeSave = (): jest.Mock => jest.fn().mockResolvedValue(undefined);

/** Knobs the shared assertions turn; each adapter maps them onto its own props. */
interface IContractRenderOptions {
  /** Always explicit — `undefined` is itself a value under test (FR-11). */
  value: unknown;
  span?: 1 | 2 | 3;
  required?: boolean;
  onSave?: jest.Mock;
  disabled?: boolean;
}

/**
 * Per-renderer gesture adapter. Keep these SMALL — if an adapter needs more
 * than props + gestures + payload, the renderer probably deviates from FR-10
 * and that is a finding, not an adapter feature.
 */
interface IContractAdapter {
  name: string;
  /** The label text this adapter always renders with. */
  label: string;
  /** Mounts the renderer with the contract knobs applied. */
  render(options: IContractRenderOptions): RenderResult;
  /** The cell root — the element that must self-apply `gridColumn`. */
  root(): HTMLElement;
  /** The read-mode value slot (em-dash / display text / editable affordance). */
  valueEl(): HTMLElement;
  /** A non-empty sample value and the text it must render. */
  sample: { value: unknown; text: string };
  /** Click-to-edit. A no-op for {@link IContractAdapter.alwaysEditing} renderers. */
  enterEdit(): Promise<void>;
  /**
   * This renderer's editor is PERMANENTLY VISIBLE while editable — there is no
   * reveal step, so it reports `data-editing="true"` for as long as it is
   * editable and never returns to a read-mode value slot.
   *
   * `BooleanField` only. A Switch's position IS its value, so "enter edit mode"
   * has no meaning for it, and UAT showed the hidden-editor treatment reading as
   * a broken field: an unset flag rendered as a grey cell containing an em-dash.
   *
   * This flag relaxes exactly THREE assertions — the click-to-edit reveal, and
   * the two "left edit mode" checks after commit and cancel. Everything else in
   * this contract still applies to `BooleanField` unchanged, including the
   * read-only gates, the em-dash empty triple, span self-application, the
   * commit payload, single-invocation, the save spinner, and Escape reverting
   * the draft. It is a narrower carve-out than `RecordHeaderLookupField`, which
   * is excluded from this suite entirely.
   */
  alwaysEditing?: boolean;
  /**
   * Stages a draft that differs from the committed value WITHOUT committing.
   * `undefined` for immediate-commit renderers (OptionSetField) whose
   * draft-change gesture and commit gesture are inseparable by design.
   */
  stageDraft?(): Promise<void>;
  /** The renderer's documented commit gesture. */
  commitGesture(): Promise<void>;
  /** Blur the open editor. */
  blurEditor(): Promise<void>;
  /** Escape / cancel the open editor. */
  escapeEditor(): Promise<void>;
  /** Asserts the payload `onSave` received for the staged draft. */
  assertPayload(payload: unknown): void;
  /** Normalized string view of the OPEN editor's current draft. */
  draftText(): string;
  /** The `draftText()` expected once a rejected save reverts the draft. */
  revertedDraftText: string;
  /** Spinner testid shown while a save is in flight. */
  spinnerTestId: string;
  /** D-10: only TextField renders the `*` marker. */
  rendersRequiredMarker: boolean;
}

const byId = (id: string): HTMLElement => screen.getByTestId(id);

// ═══════════════════════════════════════════════════════════════════════════
// Adapters
// ═══════════════════════════════════════════════════════════════════════════

const textFieldAdapter: IContractAdapter = {
  name: 'TextField',
  label: 'Matter Number',
  render: ({ value, span = 1, required, onSave, disabled }) =>
    renderWithProviders(
      <TextField
        label="Matter Number"
        value={value as string | null | undefined}
        span={span}
        required={required}
        onSave={onSave as ((v: string) => Promise<void>) | undefined}
        disabled={disabled}
      />
    ),
  root: () => byId('record-header-text-field'),
  valueEl: () => byId('record-header-text-field-value'),
  sample: { value: 'M-001', text: 'M-001' },
  enterEdit: async () => {
    await act(async () => {
      await userEvent.click(byId('record-header-text-field-value'));
    });
  },
  stageDraft: async () => {
    fireEvent.change(byId('record-header-text-field-input'), { target: { value: 'M-002' } });
  },
  commitGesture: async () => {
    fireEvent.keyDown(byId('record-header-text-field-input'), { key: 'Enter' });
  },
  blurEditor: async () => {
    fireEvent.blur(byId('record-header-text-field-input'));
  },
  escapeEditor: async () => {
    fireEvent.keyDown(byId('record-header-text-field-input'), { key: 'Escape' });
  },
  assertPayload: payload => expect(payload).toBe('M-002'),
  draftText: () => (byId('record-header-text-field-input') as HTMLInputElement).value,
  revertedDraftText: 'M-001',
  spinnerTestId: 'record-header-text-field-spinner',
  rendersRequiredMarker: true,
};

const textareaFieldAdapter: IContractAdapter = {
  name: 'TextareaField',
  label: 'Description',
  render: ({ value, span = 1, required, onSave, disabled }) =>
    renderWithProviders(
      <TextareaField
        label="Description"
        value={value as string | null | undefined}
        span={span}
        required={required}
        onSave={onSave as ((v: string) => Promise<void>) | undefined}
        disabled={disabled}
      />
    ),
  root: () => document.body.querySelector('[data-field-type="textarea"]') as HTMLElement,
  // TextareaField renders three different value nodes (clamped div / em-dash
  // placeholder / edit row) with no single shared testid, but always as the
  // wrapper's second child (the Label is first) — so index the wrapper rather
  // than branch on which state we are in.
  valueEl: () => textareaFieldAdapter.root().children[1] as HTMLElement,
  sample: { value: 'Line one.', text: 'Line one.' },
  enterEdit: async () => {
    await act(async () => {
      await userEvent.click(textareaFieldAdapter.valueEl());
    });
  },
  stageDraft: async () => {
    fireEvent.change(byId('sprk-textarea-input'), { target: { value: 'Line two.' } });
  },
  // Documented allowance: Ctrl+Enter commits; plain Enter inserts a newline.
  commitGesture: async () => {
    fireEvent.keyDown(byId('sprk-textarea-input'), { key: 'Enter', ctrlKey: true });
  },
  blurEditor: async () => {
    fireEvent.blur(byId('sprk-textarea-input'));
  },
  escapeEditor: async () => {
    fireEvent.keyDown(byId('sprk-textarea-input'), { key: 'Escape' });
  },
  assertPayload: payload => expect(payload).toBe('Line two.'),
  draftText: () => (byId('sprk-textarea-input') as HTMLTextAreaElement).value,
  revertedDraftText: 'Line one.',
  spinnerTestId: 'sprk-textarea-spinner',
  rendersRequiredMarker: false,
};

const optionSetFieldAdapter: IContractAdapter = {
  name: 'OptionSetField',
  label: 'Status',
  render: ({ value, span = 1, required, onSave, disabled }) =>
    renderWithProviders(
      <OptionSetField
        label="Status"
        value={value as string | null | undefined}
        span={span}
        required={required}
        options={STATUS_OPTIONS}
        onSave={onSave as ((v: number | null) => Promise<void>) | undefined}
        disabled={disabled}
      />
    ),
  root: () => byId('record-header-optionset-field'),
  valueEl: () => byId('record-header-optionset-field-value'),
  sample: { value: 'Open', text: 'Open' },
  enterEdit: async () => {
    await act(async () => {
      await userEvent.click(byId('record-header-optionset-field-value'));
    });
  },
  // stageDraft intentionally omitted — see the file header. Selecting an option
  // IS the commit; there is no separable pending draft by design.
  commitGesture: async () => {
    await act(async () => {
      await userEvent.click(screen.getByRole('option', { name: 'Closed' }));
    });
  },
  blurEditor: async () => {
    fireEvent.blur(byId('record-header-optionset-field-dropdown'));
  },
  escapeEditor: async () => {
    fireEvent.keyDown(byId('record-header-optionset-field-dropdown'), { key: 'Escape' });
  },
  assertPayload: payload => expect(payload).toBe(2),
  draftText: () => byId('record-header-optionset-field-dropdown').textContent?.trim() ?? '',
  revertedDraftText: 'Open',
  spinnerTestId: 'record-header-optionset-field-spinner',
  rendersRequiredMarker: false,
};

const dateFieldAdapter: IContractAdapter = {
  name: 'DateField',
  label: 'Planned Start',
  render: ({ value, span = 1, required, onSave, disabled }) =>
    renderWithProviders(
      <DateField
        label="Planned Start"
        value={value as string | Date | null | undefined}
        span={span}
        format="datetime"
        required={required}
        onSave={onSave as ((v: Date | null) => Promise<void>) | undefined}
        disabled={disabled}
      />
    ),
  root: () => byId('record-header-date-field'),
  valueEl: () => byId('record-header-date-field-value'),
  sample: {
    value: DATETIME_ISO,
    text: new Intl.DateTimeFormat(undefined, { dateStyle: 'short', timeStyle: 'short' }).format(new Date(DATETIME_ISO)),
  },
  enterEdit: async () => {
    await act(async () => {
      await userEvent.click(byId('record-header-date-field-value'));
    });
  },
  stageDraft: async () => {
    fireEvent.change(byId('record-header-date-field-input'), { target: { value: DATETIME_LOCAL_STAGED } });
  },
  commitGesture: async () => {
    fireEvent.keyDown(byId('record-header-date-field-input'), { key: 'Enter' });
  },
  blurEditor: async () => {
    fireEvent.blur(byId('record-header-date-field-input'));
  },
  escapeEditor: async () => {
    fireEvent.keyDown(byId('record-header-date-field-input'), { key: 'Escape' });
  },
  assertPayload: payload => {
    expect(payload).toBeInstanceOf(Date);
    expect((payload as Date).getHours()).toBe(9);
    expect((payload as Date).getMinutes()).toBe(15);
  },
  draftText: () => (byId('record-header-date-field-input') as HTMLInputElement).value,
  revertedDraftText: DATETIME_LOCAL,
  spinnerTestId: 'record-header-date-field-spinner',
  rendersRequiredMarker: false,
};

const numberFieldAdapter: IContractAdapter = {
  name: 'NumberField',
  label: 'Total Amount',
  render: ({ value, span = 1, required, onSave, disabled }) =>
    renderWithProviders(
      <NumberField
        label="Total Amount"
        value={value as number | string | null | undefined}
        span={span}
        kind="integer"
        required={required}
        onSave={onSave as ((v: number | null) => Promise<void>) | undefined}
        disabled={disabled}
      />
    ),
  root: () => byId('record-header-number-field'),
  valueEl: () => byId('record-header-number-field-value'),
  sample: {
    value: 1200,
    text: new Intl.NumberFormat(undefined, { minimumFractionDigits: 0, maximumFractionDigits: 0 }).format(1200),
  },
  enterEdit: async () => {
    await act(async () => {
      await userEvent.click(byId('record-header-number-field-value'));
    });
  },
  stageDraft: async () => {
    fireEvent.change(byId('record-header-number-field-input'), { target: { value: '2500' } });
  },
  commitGesture: async () => {
    fireEvent.keyDown(byId('record-header-number-field-input'), { key: 'Enter' });
  },
  blurEditor: async () => {
    fireEvent.blur(byId('record-header-number-field-input'));
  },
  escapeEditor: async () => {
    fireEvent.keyDown(byId('record-header-number-field-input'), { key: 'Escape' });
  },
  assertPayload: payload => expect(payload).toBe(2500),
  draftText: () => (byId('record-header-number-field-input') as HTMLInputElement).value,
  revertedDraftText: '1200',
  spinnerTestId: 'record-header-number-field-spinner',
  rendersRequiredMarker: false,
};

const booleanFieldAdapter: IContractAdapter = {
  name: 'BooleanField',
  label: 'High Priority',
  render: ({ value, span = 1, required, onSave, disabled }) =>
    renderWithProviders(
      <BooleanField
        label="High Priority"
        value={value as boolean | '' | null | undefined}
        span={span}
        required={required}
        onSave={onSave as ((v: boolean) => Promise<void>) | undefined}
        disabled={disabled}
      />
    ),
  root: () => byId('record-header-boolean-field'),
  valueEl: () => byId('record-header-boolean-field-value'),
  sample: { value: true, text: 'Yes' },
  alwaysEditing: true,
  // No-op: the Switch is already live whenever the cell is editable. Kept as a
  // satisfied method (rather than made optional) so every other shared
  // assertion can call it unconditionally.
  enterEdit: async () => {
    /* nothing to reveal */
  },
  stageDraft: async () => {
    await act(async () => {
      await userEvent.click(byId('record-header-boolean-field-switch'));
    });
  },
  commitGesture: async () => {
    fireEvent.keyDown(byId('record-header-boolean-field-switch'), { key: 'Enter' });
  },
  blurEditor: async () => {
    fireEvent.blur(byId('record-header-boolean-field-switch'));
  },
  escapeEditor: async () => {
    fireEvent.keyDown(byId('record-header-boolean-field-switch'), { key: 'Escape' });
  },
  assertPayload: payload => expect(payload).toBe(false),
  draftText: () => String((byId('record-header-boolean-field-switch') as HTMLInputElement).checked),
  revertedDraftText: 'true',
  spinnerTestId: 'record-header-boolean-field-spinner',
  rendersRequiredMarker: false,
};

/** All six renderers under contract. None is skipped. */
const ADAPTERS: IContractAdapter[] = [
  textFieldAdapter,
  textareaFieldAdapter,
  optionSetFieldAdapter,
  dateFieldAdapter,
  numberFieldAdapter,
  booleanFieldAdapter,
];

// ═══════════════════════════════════════════════════════════════════════════
// The shared contract — identical assertions, per-renderer gestures
// ═══════════════════════════════════════════════════════════════════════════

describe('FR-10 renderer contract', () => {
  it('covers every editable RecordHeader renderer (six adapters, none skipped)', () => {
    expect(ADAPTERS.map(a => a.name)).toEqual([
      'TextField',
      'TextareaField',
      'OptionSetField',
      'DateField',
      'NumberField',
      'BooleanField',
    ]);
  });

  describe.each(ADAPTERS.map(a => [a.name, a] as const))('%s', (_name, adapter) => {
    /** Renders the adapter's non-empty sample plus any contract knobs. */
    const renderSample = (extra: Omit<IContractRenderOptions, 'value'> = {}): RenderResult =>
      adapter.render({ value: adapter.sample.value, ...extra });

    const valueText = (): string => adapter.valueEl().textContent?.trim() ?? '';

    /**
     * Assert the renderer is no longer in edit mode.
     *
     * An `alwaysEditing` renderer never leaves it — its editor is the whole
     * cell — so the contract's real requirement ("the commit/cancel gesture
     * resolved and the renderer settled") is expressed as its steady state
     * rather than by weakening the assertion to a no-op.
     */
    const expectSettledOutOfEditMode = (): void => {
      expect(adapter.root().getAttribute('data-editing')).toBe(adapter.alwaysEditing ? 'true' : 'false');
    };

    // ── (a) label + value ────────────────────────────────────────────────
    it('renders its label and its value', () => {
      renderSample();
      expect(screen.getByText(adapter.label)).toBeInTheDocument();
      expect(valueText()).toBe(adapter.sample.text);
    });

    // ── (b) self-applied gridColumn (FR-03: the CELL owns its span) ──────
    it.each([1, 2, 3] as const)('self-applies gridColumn: span %i on its root', span => {
      renderSample({ span });
      expect(adapter.root().style.gridColumn).toBe(`span ${span}`);
      expect(adapter.root().getAttribute('data-span')).toBe(String(span));
    });

    // ── (c) FR-11 empty triple ───────────────────────────────────────────
    it.each([
      ['null', null],
      ['undefined', undefined],
      ["'' (empty string)", ''],
    ])('renders the em-dash when value is %s', (_label, emptyValue) => {
      adapter.render({ value: emptyValue });
      expect(valueText()).toBe(EM_DASH);
    });

    // ── (d) editable gate: no onSave → read-only ─────────────────────────
    it('is read-only when no onSave is supplied', () => {
      renderSample();
      expect(adapter.root().getAttribute('data-editable')).toBe('false');
      expect(adapter.valueEl()).not.toHaveAttribute('role', 'button');
    });

    // ── (e) editable gate: onSave + disabled → read-only ─────────────────
    it('is read-only when onSave is supplied but disabled=true', () => {
      renderSample({ onSave: makeSave(), disabled: true });
      expect(adapter.root().getAttribute('data-editable')).toBe('false');
      expect(adapter.valueEl()).not.toHaveAttribute('role', 'button');
    });

    it('exposes a live editor when onSave is supplied alone', async () => {
      renderSample({ onSave: makeSave() });
      expect(adapter.root().getAttribute('data-editable')).toBe('true');

      if (adapter.alwaysEditing) {
        // No reveal step and no read-mode value slot — the editor is already
        // mounted, which is the entire point of the carve-out.
        expect(adapter.root().getAttribute('data-editing')).toBe('true');
      } else {
        expect(adapter.valueEl()).toHaveAttribute('role', 'button');
      }

      await adapter.enterEdit();
      expect(adapter.root().getAttribute('data-editing')).toBe('true');
    });

    // ── (f) commit ───────────────────────────────────────────────────────
    it('commits exactly once with the expected payload and exits edit mode on resolve', async () => {
      const onSave = makeSave();
      renderSample({ onSave });

      await adapter.enterEdit();
      if (adapter.stageDraft) await adapter.stageDraft();
      await adapter.commitGesture();

      await waitFor(() => expect(onSave).toHaveBeenCalledTimes(1));
      adapter.assertPayload(onSave.mock.calls[0][0]);
      await waitFor(() => expectSettledOutOfEditMode());
    });

    // ── (g) cancel ───────────────────────────────────────────────────────
    it('Escape cancels: exits edit mode, zero onSave calls, prior value restored', async () => {
      const onSave = makeSave();
      renderSample({ onSave });

      await adapter.enterEdit();
      if (adapter.stageDraft) await adapter.stageDraft();
      await adapter.escapeEditor();

      expect(onSave).not.toHaveBeenCalled();
      expectSettledOutOfEditMode();
      if (adapter.alwaysEditing) {
        // No read-mode slot to fall back to — "prior value restored" is
        // observable as the live editor's own state reverting. Same guarantee,
        // different surface.
        expect(adapter.draftText()).toBe(adapter.revertedDraftText);
      } else {
        expect(valueText()).toBe(adapter.sample.text);
      }
    });

    // ── (h) blur ─────────────────────────────────────────────────────────
    it('blur commits a changed draft (immediate-commit renderers: blur exits with no extra save)', async () => {
      const onSave = makeSave();
      renderSample({ onSave });
      await adapter.enterEdit();

      if (adapter.stageDraft) {
        await adapter.stageDraft();
        await adapter.blurEditor();

        await waitFor(() => expect(onSave).toHaveBeenCalledTimes(1));
        adapter.assertPayload(onSave.mock.calls[0][0]);
      } else {
        // OptionSetField: selection already committed, so blur can never carry
        // a pending draft. The contract it must still honor is that blur is
        // WIRED to commit and that a no-change blur exits cleanly without
        // firing a spurious save.
        await adapter.blurEditor();

        expect(onSave).not.toHaveBeenCalled();
        await waitFor(() => expectSettledOutOfEditMode());
      }
      await waitFor(() => expectSettledOutOfEditMode());
    });

    // ── (i) NEGATIVE: rejected save reverts the draft AND stays in edit ──
    it('a rejected onSave reverts the draft, keeps the field IN edit mode, and shows a spinner while pending', async () => {
      let rejectSave: (err: Error) => void = () => undefined;
      const onSave = jest.fn(
        () =>
          new Promise<void>((_resolve, reject) => {
            rejectSave = reject;
          })
      );
      renderSample({ onSave });

      await adapter.enterEdit();
      if (adapter.stageDraft) await adapter.stageDraft();
      await adapter.commitGesture();

      await waitFor(() => expect(onSave).toHaveBeenCalledTimes(1));

      // Spinner is shown while the save is in flight.
      expect(screen.getByTestId(adapter.spinnerTestId)).toBeInTheDocument();

      await act(async () => {
        rejectSave(new Error('save failed'));
      });
      // The renderer's catch/finally settle across two microtasks; waitFor is
      // act-wrapped, so this also keeps the state updates inside act().
      await waitFor(() => expect(screen.queryByTestId(adapter.spinnerTestId)).not.toBeInTheDocument());

      // Stays in edit mode with the draft reverted to the prior value.
      expect(adapter.root().getAttribute('data-editing')).toBe('true');
      expect(adapter.draftText()).toBe(adapter.revertedDraftText);
    });

    // ── (j) NEGATIVE (D-10): the `*` marker is TextField-only ────────────
    it('renders the "*" required marker only for TextField (D-10)', () => {
      renderSample({ required: true });

      if (adapter.rendersRequiredMarker) {
        expect(screen.getByTestId('record-header-text-field-required-marker')).toHaveTextContent('*');
      } else {
        expect(screen.queryByText('*')).not.toBeInTheDocument();
      }
    });
  });
});
