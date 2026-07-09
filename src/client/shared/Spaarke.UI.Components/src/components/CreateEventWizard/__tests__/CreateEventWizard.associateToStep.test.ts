/**
 * CreateEventWizard — associateToStep wiring + onFinish regarding-passthrough (task 015)
 *
 * Scope: the two PURE helpers extracted from CreateEventWizard.tsx so they can be unit
 * tested without rendering the full multi-step wizard shell (ADR-038 — deterministic
 * logic, no framework internals mocked):
 *
 *   1. `resolveEventAssociateToStepConfig` — gates whether the AssociateToStep is
 *      configured at all. Gated MORE PRECISELY than TodoWizardDialog's equivalent
 *      (navigationService-only) to avoid a UX regression on the pre-existing
 *      standalone Code Page entry point (src/solutions/CreateEventWizard/src/main.tsx),
 *      which passes `navigationService` but neither `initialAssociation` nor
 *      `lockAssociation` — see CreateEventWizard.tsx doc comment for full rationale.
 *
 *   2. `mergeEventRegardingFromAssociation` — copies the wizard's selected/seeded
 *      association onto the form fields `EventService.createEvent` reads
 *      (`regardingRecordId`/`regardingRecordName`), per its JSDoc contract.
 *
 * @see ../CreateEventWizard.tsx
 * @see ../../CreateTodoWizard/TodoWizardDialog.tsx (reference associateToStep pattern)
 * @see .claude/adr/ADR-024-polymorphic-resolver-pattern.md
 */

import { resolveEventAssociateToStepConfig, mergeEventRegardingFromAssociation } from '../CreateEventWizard';
import { EVENT_REGARDING_TARGETS } from '../../AssociateToStep/types';
import type { AssociationResult } from '../../AssociateToStep/types';
import type { INavigationService } from '../../../types/serviceInterfaces';
import type { ICreateEventFormState } from '../formTypes';

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

const NAV_SERVICE = {} as INavigationService;

const MATTER_ASSOCIATION: AssociationResult = {
  entityType: 'sprk_matter',
  recordId: '11111111-2222-3333-4444-555555555555',
  recordName: 'Smith v. Jones',
};

function makeForm(overrides?: Partial<ICreateEventFormState>): ICreateEventFormState {
  return {
    eventName: 'Kickoff meeting',
    eventTypeId: '',
    eventTypeName: '',
    dueDate: '',
    priority: 100000001,
    description: '',
    regardingRecordId: '',
    regardingRecordName: '',
    ...overrides,
  };
}

// ---------------------------------------------------------------------------
// EVENT_REGARDING_TARGETS — live-schema fidelity (task 015 background)
// ---------------------------------------------------------------------------

describe('EVENT_REGARDING_TARGETS', () => {
  it('has exactly 9 entries, matching the live sprk_event schema (describe(tables/sprk_event))', () => {
    expect(EVENT_REGARDING_TARGETS).toHaveLength(9);
  });

  it('preserves the "sprk_regardingorganziation" typo exactly (live column name — NOT "organization")', () => {
    const org = EVENT_REGARDING_TARGETS.find(t => t.label === 'Organization');
    expect(org).toBeDefined();
    expect(org?.lookupAttribute).toBe('sprk_regardingorganziation');
    expect(org?.entityType).toBe('sprk_organization');
  });

  it('uses the OOB "account" and "contact" logical names (not sprk_account/sprk_contact)', () => {
    const account = EVENT_REGARDING_TARGETS.find(t => t.label === 'Account');
    const contact = EVENT_REGARDING_TARGETS.find(t => t.label === 'Contact');
    expect(account?.entityType).toBe('account');
    expect(contact?.entityType).toBe('contact');
  });

  it('has a unique lookupAttribute for every entry (ADR-024 mutual exclusion precondition)', () => {
    const attrs = EVENT_REGARDING_TARGETS.map(t => t.lookupAttribute);
    expect(new Set(attrs).size).toBe(attrs.length);
  });
});

// ---------------------------------------------------------------------------
// resolveEventAssociateToStepConfig
// ---------------------------------------------------------------------------

describe('resolveEventAssociateToStepConfig', () => {
  it('returns undefined when navigationService is absent (regardless of other props)', () => {
    expect(resolveEventAssociateToStepConfig(undefined, MATTER_ASSOCIATION, true)).toBeUndefined();
    expect(resolveEventAssociateToStepConfig(undefined, undefined, false)).toBeUndefined();
  });

  it('returns undefined when navigationService is present but neither initialAssociation nor lockAssociation is set (Code Page regression guard)', () => {
    // This is EXACTLY the shape of src/solutions/CreateEventWizard/src/main.tsx's call:
    // navigationService is passed, but initialAssociation/lockAssociation are not.
    expect(resolveEventAssociateToStepConfig(NAV_SERVICE, undefined, undefined)).toBeUndefined();
    expect(resolveEventAssociateToStepConfig(NAV_SERVICE, undefined, false)).toBeUndefined();
  });

  it('configures the step when lockAssociation is true (Visual Host locked-launch path)', () => {
    const config = resolveEventAssociateToStepConfig(NAV_SERVICE, MATTER_ASSOCIATION, true);
    expect(config).toBeDefined();
    expect(config?.navigationService).toBe(NAV_SERVICE);
    expect(config?.initialAssociation).toBe(MATTER_ASSOCIATION);
    expect(config?.lockAssociation).toBe(true);
  });

  it('configures the step when initialAssociation is supplied even without lockAssociation', () => {
    const config = resolveEventAssociateToStepConfig(NAV_SERVICE, MATTER_ASSOCIATION, undefined);
    expect(config).toBeDefined();
    expect(config?.initialAssociation).toBe(MATTER_ASSOCIATION);
    expect(config?.lockAssociation).toBeUndefined();
  });

  it('configures the step when lockAssociation is true even without an initialAssociation seed (empty-host edge case)', () => {
    const config = resolveEventAssociateToStepConfig(NAV_SERVICE, undefined, true);
    expect(config).toBeDefined();
    expect(config?.initialAssociation).toBeUndefined();
    expect(config?.lockAssociation).toBe(true);
  });

  it('passes the full 9-entry EVENT_REGARDING_TARGETS list as entityTypes', () => {
    const config = resolveEventAssociateToStepConfig(NAV_SERVICE, MATTER_ASSOCIATION, true);
    expect(config?.entityTypes).toHaveLength(9);
    expect(config?.entityTypes).toEqual(EVENT_REGARDING_TARGETS);
    // Defensive copy — not the same array reference as the shared const.
    expect(config?.entityTypes).not.toBe(EVENT_REGARDING_TARGETS);
  });
});

// ---------------------------------------------------------------------------
// mergeEventRegardingFromAssociation
// ---------------------------------------------------------------------------

describe('mergeEventRegardingFromAssociation', () => {
  it('passes formValues through unchanged when association is null (hidden/skipped step)', () => {
    const form = makeForm();
    const result = mergeEventRegardingFromAssociation(form, null);
    expect(result).toBe(form); // same reference — true no-op, not just equal shape
    expect(result.regardingRecordId).toBe('');
    expect(result.regardingRecordName).toBe('');
  });

  it('copies association.recordId/.recordName onto regardingRecordId/regardingRecordName', () => {
    const form = makeForm();
    const result = mergeEventRegardingFromAssociation(form, MATTER_ASSOCIATION);
    expect(result.regardingRecordId).toBe(MATTER_ASSOCIATION.recordId);
    expect(result.regardingRecordName).toBe(MATTER_ASSOCIATION.recordName);
    // Original form values (entered by the user) are preserved.
    expect(result.eventName).toBe(form.eventName);
  });

  it('overwrites any pre-existing regardingRecordId/regardingRecordName with the association values', () => {
    const form = makeForm({ regardingRecordId: 'stale-id', regardingRecordName: 'Stale Name' });
    const result = mergeEventRegardingFromAssociation(form, MATTER_ASSOCIATION);
    expect(result.regardingRecordId).toBe(MATTER_ASSOCIATION.recordId);
    expect(result.regardingRecordName).toBe(MATTER_ASSOCIATION.recordName);
  });

  it('does not mutate the original formValues object', () => {
    const form = makeForm();
    const frozenCopy = { ...form };
    mergeEventRegardingFromAssociation(form, MATTER_ASSOCIATION);
    expect(form).toEqual(frozenCopy);
  });
});
