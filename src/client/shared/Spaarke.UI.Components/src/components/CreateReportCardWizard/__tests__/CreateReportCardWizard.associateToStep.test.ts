/**
 * CreateReportCardWizard — associateToStep wiring (task 040)
 *
 * Scope: the pure helper extracted from CreateReportCardWizard.tsx so it can
 * be unit tested without rendering the full multi-step wizard shell
 * (ADR-038 — deterministic logic, no framework internals mocked).
 *
 * @see ../CreateReportCardWizard.tsx
 * @see CreateInvoiceWizard/__tests__/CreateInvoiceWizard.associateToStep.test.ts (reference shape)
 */

import { resolveReportCardAssociateToStepConfig } from '../CreateReportCardWizard';
import { REPORTCARD_REGARDING_TARGETS } from '../../AssociateToStep/types';
import type { AssociationResult } from '../../AssociateToStep/types';
import type { INavigationService } from '../../../types/serviceInterfaces';

const NAV_SERVICE = {} as INavigationService;

const MATTER_ASSOCIATION: AssociationResult = {
  entityType: 'sprk_matter',
  recordId: '11111111-2222-3333-4444-555555555555',
  recordName: 'Smith v. Jones',
};

describe('REPORTCARD_REGARDING_TARGETS', () => {
  it('has exactly 2 entries — Matter + Project only (reportcard.md manifest)', () => {
    expect(REPORTCARD_REGARDING_TARGETS).toHaveLength(2);
    expect(REPORTCARD_REGARDING_TARGETS.map(t => t.entityType)).toEqual(['sprk_matter', 'sprk_project']);
  });

  it('uses the "sprk_regarding{entity}" lookup-attribute convention (unlike Invoice\'s plain sprk_matter/sprk_project)', () => {
    expect(REPORTCARD_REGARDING_TARGETS.map(t => t.lookupAttribute)).toEqual([
      'sprk_regardingmatter',
      'sprk_regardingproject',
    ]);
  });

  it('has a unique lookupAttribute for every entry', () => {
    const attrs = REPORTCARD_REGARDING_TARGETS.map(t => t.lookupAttribute);
    expect(new Set(attrs).size).toBe(attrs.length);
  });
});

describe('resolveReportCardAssociateToStepConfig', () => {
  it('returns undefined when navigationService is absent (regardless of other props)', () => {
    expect(resolveReportCardAssociateToStepConfig(undefined, MATTER_ASSOCIATION, true)).toBeUndefined();
    expect(resolveReportCardAssociateToStepConfig(undefined, undefined, false)).toBeUndefined();
  });

  it('returns undefined when navigationService is present but neither initialAssociation nor lockAssociation is set', () => {
    expect(resolveReportCardAssociateToStepConfig(NAV_SERVICE, undefined, undefined)).toBeUndefined();
    expect(resolveReportCardAssociateToStepConfig(NAV_SERVICE, undefined, false)).toBeUndefined();
  });

  it('configures the step when lockAssociation is true (Visual Host locked-launch path)', () => {
    const config = resolveReportCardAssociateToStepConfig(NAV_SERVICE, MATTER_ASSOCIATION, true);
    expect(config).toBeDefined();
    expect(config?.navigationService).toBe(NAV_SERVICE);
    expect(config?.initialAssociation).toBe(MATTER_ASSOCIATION);
    expect(config?.lockAssociation).toBe(true);
  });

  it('configures the step when initialAssociation is supplied even without lockAssociation', () => {
    const config = resolveReportCardAssociateToStepConfig(NAV_SERVICE, MATTER_ASSOCIATION, undefined);
    expect(config).toBeDefined();
    expect(config?.initialAssociation).toBe(MATTER_ASSOCIATION);
    expect(config?.lockAssociation).toBeUndefined();
  });

  it('passes the full 2-entry REPORTCARD_REGARDING_TARGETS list as entityTypes (defensive copy)', () => {
    const config = resolveReportCardAssociateToStepConfig(NAV_SERVICE, MATTER_ASSOCIATION, true);
    expect(config?.entityTypes).toHaveLength(2);
    expect(config?.entityTypes).toEqual(REPORTCARD_REGARDING_TARGETS);
    expect(config?.entityTypes).not.toBe(REPORTCARD_REGARDING_TARGETS);
  });
});
