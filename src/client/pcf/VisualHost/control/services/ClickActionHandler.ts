/**
 * Click Action Handler Service
 * Executes configured click actions from chart definition
 * Supports: openrecordform, opensidepane, navigatetopage, opendatasetgrid
 */

import { IChartDefinition, OnClickAction } from '../types';
import { logger } from '../utils/logger';
// React-free module from source (same precedent as VisualHostRoot / oobModalSizes).
import {
  getXrm as getSharedXrm,
  type XrmCapability,
} from '../../../../shared/Spaarke.UI.Components/src/utils/xrmContext';

export interface IClickActionContext {
  chartDefinition: IChartDefinition;
  recordId: string;
  entityName?: string;
  recordData?: Record<string, unknown>;
}

/**
 * Xrm comes from the one shared lookup (task 081 round 4, review F2): the
 * nearest frame whose Xrm has the capability each action needs. This file used
 * to read `window.Xrm` only, so a VisualHost rendered inside an iframe whose
 * own window has no Xrm (a custom page / dashboard host) silently did nothing
 * on click. Each call below asks for exactly what it uses.
 */
// eslint-disable-next-line @typescript-eslint/no-explicit-any
const getXrm = (capability: XrmCapability): any => getSharedXrm(capability);

/**
 * Resolve the record ID to use for the click action.
 * If onClickRecordField is configured, extract the ID from recordData.
 * Otherwise, use the provided recordId directly.
 */
function resolveRecordId(ctx: IClickActionContext): string {
  const { chartDefinition, recordId, recordData } = ctx;

  if (chartDefinition.sprk_onclickrecordfield && recordData) {
    const fieldValue = recordData[chartDefinition.sprk_onclickrecordfield];
    if (typeof fieldValue === 'string' && fieldValue.trim() !== '') {
      return fieldValue.replace(/[{}]/g, '');
    }
    logger.warn('ClickActionHandler', 'Configured record field not found or empty, using default recordId', {
      field: chartDefinition.sprk_onclickrecordfield,
    });
  }

  return recordId;
}

/**
 * Open a record's modal form
 */
async function openRecordForm(ctx: IClickActionContext): Promise<void> {
  const xrm = getXrm('openForm');
  if (!xrm?.Navigation?.openForm) {
    logger.warn('ClickActionHandler', 'Xrm.Navigation.openForm not available');
    return;
  }

  const resolvedId = resolveRecordId(ctx);
  const targetEntity = ctx.chartDefinition.sprk_onclicktarget || ctx.entityName;

  logger.info('ClickActionHandler', 'Opening record form', {
    entityName: targetEntity,
    recordId: resolvedId,
  });

  await xrm.Navigation.openForm({
    entityName: targetEntity,
    entityId: resolvedId,
  });
}

/**
 * Open a Custom Page in a side pane
 */
async function openSidePane(ctx: IClickActionContext): Promise<void> {
  const xrm = getXrm('sidePanes');
  if (!xrm?.App?.sidePanes?.createPane) {
    logger.warn('ClickActionHandler', 'Xrm.App.sidePanes not available');
    return;
  }

  const resolvedId = resolveRecordId(ctx);
  const pageName = ctx.chartDefinition.sprk_onclicktarget;

  if (!pageName) {
    logger.warn('ClickActionHandler', 'No target page configured for opensidepane action');
    return;
  }

  logger.info('ClickActionHandler', 'Opening side pane', {
    pageName,
    recordId: resolvedId,
  });

  const pane = await xrm.App.sidePanes.createPane({
    title: ctx.chartDefinition.sprk_name || 'Details',
    paneId: `visualhost_${resolvedId}`,
    canClose: true,
  });

  pane.navigate({
    pageType: 'custom',
    name: pageName,
    recordId: resolvedId,
  });
}

/**
 * Navigate to a Custom Page or URL
 */
async function navigateToPage(ctx: IClickActionContext): Promise<void> {
  const xrm = getXrm('navigation');
  if (!xrm?.Navigation?.navigateTo) {
    logger.warn('ClickActionHandler', 'Xrm.Navigation.navigateTo not available');
    return;
  }

  const resolvedId = resolveRecordId(ctx);
  const target = ctx.chartDefinition.sprk_onclicktarget;

  if (!target) {
    logger.warn('ClickActionHandler', 'No target configured for navigatetopage action');
    return;
  }

  logger.info('ClickActionHandler', 'Navigating to page', {
    target,
    recordId: resolvedId,
  });

  await xrm.Navigation.navigateTo({
    pageType: 'custom',
    name: target,
    recordId: resolvedId,
  });
}

/**
 * Execute the configured click action
 * Returns true if an action was executed, false if no action or action type is None
 */
export async function executeClickAction(ctx: IClickActionContext, onExpandClick?: () => void): Promise<boolean> {
  const action = ctx.chartDefinition.sprk_onclickaction;

  if (action === undefined || action === null || action === OnClickAction.None) {
    return false;
  }

  logger.info('ClickActionHandler', 'Executing click action', {
    action,
    chartName: ctx.chartDefinition.sprk_name,
    recordId: ctx.recordId,
  });

  try {
    switch (action) {
      case OnClickAction.OpenRecordForm:
        await openRecordForm(ctx);
        return true;

      case OnClickAction.OpenSidePane:
        await openSidePane(ctx);
        return true;

      case OnClickAction.NavigateToPage:
        await navigateToPage(ctx);
        return true;

      case OnClickAction.OpenDatasetGrid:
        if (onExpandClick) {
          onExpandClick();
        }
        return true;

      default:
        logger.warn('ClickActionHandler', 'Unknown click action type', {
          action,
        });
        return false;
    }
  } catch (err) {
    logger.error('ClickActionHandler', 'Failed to execute click action', err);
    return false;
  }
}

/**
 * Check if a chart definition has a configured click action
 */
export function hasClickAction(chartDefinition: IChartDefinition): boolean {
  return (
    chartDefinition.sprk_onclickaction !== undefined &&
    chartDefinition.sprk_onclickaction !== null &&
    chartDefinition.sprk_onclickaction !== OnClickAction.None
  );
}
