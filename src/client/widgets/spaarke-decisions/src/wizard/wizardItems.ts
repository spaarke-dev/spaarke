/**
 * The wizard's item reader, over MCP (spaarkeai-mcp-bff-api-r1 task 032, FR-13).
 *
 * The Console's loader (`WorklistWidget`'s `loaderFor`) builds the wizard's item from rows the worklist already holds. The widget does the
 * same with one difference: its lane rows come from the paging tool, which is slim (no rule sentence, no evidence lines), so the item's
 * row is the FULL row the plan tool returns for the Signal (the same row the REST worklist route returns), and the lane supplies what only
 * it knows: the core record and the other open items on it ("Also resolve").
 *
 * The lane is read at launch and the loader reads that snapshot, as the Console's loader reads the sets it held when the wizard opened:
 * the lane may be read again while the wizard is open (a decision was recorded; the row leaves the lane), and an item still to be shown
 * must not vanish from under the wizard because of it.
 *
 * Fails closed: a row the tool could not read (`rowError`), or one that has left the caller's lanes, opens nothing, so the wizard shows
 * its "could not be loaded" state with Try again instead of deciding an item whose evidence it cannot show.
 */
import type { DecisionItemLoader } from '@spaarke/ui-components/components/DecisionWizard/decisionWizardTypes';
import {
  parseWorklistResponse,
  type WorklistData,
  type WorklistGroupData,
} from '@spaarke/ui-components/components/Worklist/worklistService';
import { buildWizardItem } from '@spaarke/ui-components/components/Worklist/worklistWizardItem';
import type { WizardBridge } from './wizardFetch';

/**
 * Said inside the wizard, which stays open until the user closes it. Closing reads the lane again in place, so the one thing it asks of
 * the user is to close.
 */
export const ITEM_GONE = 'This item is no longer on your list. Close this decision to see the list as it is now.';
/** Said beside the lane when the Work Item asked for is gone from it (nothing opened): the lane has already been read again. */
export const ITEM_GONE_NOTICE = 'This item is no longer on your list. The list has been read again.';
export const ITEM_UNREADABLE = 'The details of this item could not be read. Try again.';

/** The group of the snapshot that holds the Signal (its core record and its sibling items), or null when the snapshot has none. */
export function findDecideGroup(lane: WorklistData, signalId: string): WorklistGroupData | null {
  for (const group of lane.Decide) {
    if (group.items.some(i => i.signalId === signalId)) return group;
  }
  return null;
}

/** The Signal's row as the shared parser reads it, or null when it is not a readable row. */
function parseRow(raw: unknown) {
  try {
    const parsed = parseWorklistResponse(
      {
        lanes: [
          { lane: 'Decide', groups: [{ notFiled: true, items: [raw] }] },
          { lane: 'Do', groups: [] },
        ],
      },
      'lanes'
    );
    return parsed.Decide[0]?.items[0] ?? null;
  } catch {
    return null;
  }
}

/**
 * The `loadItem` of one wizard launch over `lane`, the lane as it was when the wizard opened. Each call makes the one plan-tool call of
 * that open (`bridge.readItemRow`), which the wizard's plan and suppression reads then share.
 */
export function createWizardItemLoader(
  bridge: WizardBridge,
  lane: WorklistData,
  today: () => Date = () => new Date()
): DecisionItemLoader {
  return async signalId => {
    const group = findDecideGroup(lane, signalId);
    if (!group) throw new Error(ITEM_GONE);

    const body = await bridge.readItemRow(signalId);
    if (body.row === null || body.row === undefined) {
      // The tool read the plan but the Signal is no longer in the caller's lanes (decided elsewhere, or access changed).
      if (body.rowError === undefined || body.rowError === null) throw new Error(ITEM_GONE);
      throw new Error(ITEM_UNREADABLE);
    }
    const row = parseRow(body.row);
    // A different Signal's row is never shown under this item (a GUID may differ only in case, and the wizard's id is the one it asked for).
    if (!row || row.signalId.toLowerCase() !== signalId.toLowerCase()) throw new Error(ITEM_UNREADABLE);
    return buildWizardItem({ item: { ...row, signalId }, group, set: 'lanes', today: today() });
  };
}
