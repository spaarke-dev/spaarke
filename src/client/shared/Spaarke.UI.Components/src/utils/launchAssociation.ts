/**
 * launchAssociation.ts — the Associate-To pre-fill a wizard host takes from its launch envelope
 * (unified-access-control-r2 task 195, #2352).
 *
 * The Create Project and Create Matter code pages are opened from the partner record — its form command or the "New"
 * button on the Project <-> Matter subgrid (`sprk_wizard_commands`) — with `entityType`, `entityId` and `recordName` in
 * the data envelope. When the launch record is the partner table, the wizard pre-selects it and locks the Associate-To
 * step; any other launch record, or none, leaves the step as it was (nothing pre-selected, nothing locked).
 */
import type { AssociationResult } from '../components/AssociateToStep/types';
import { cleanGuid } from './guid';

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/;

export interface ILaunchAssociation {
  /** The launch record, when it is the partner table; `undefined` otherwise. */
  initialAssociation: AssociationResult | undefined;
  /** True exactly when `initialAssociation` is set. */
  lockAssociation: boolean;
}

/**
 * Reads the launch record from the parsed data envelope.
 *
 * @param params - the host's parsed data params (`parseDataParams`).
 * @param partnerEntityType - the only table taken as a launch association (e.g. `sprk_matter` for Create Project).
 */
export function launchAssociationFromParams(
  params: Readonly<Record<string, string | undefined>>,
  partnerEntityType: string
): ILaunchAssociation {
  const recordId = cleanGuid(params.entityId);
  if (params.entityType !== partnerEntityType || !GUID.test(recordId)) {
    return { initialAssociation: undefined, lockAssociation: false };
  }
  return {
    initialAssociation: { entityType: partnerEntityType, recordId, recordName: params.recordName ?? '' },
    lockAssociation: true,
  };
}
