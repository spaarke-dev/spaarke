/**
 * ClickActionHandler resolves Xrm through the shared walker (task 081 round 4,
 * review F2). It used to read `window.Xrm` only, so a VisualHost hosted in a
 * frame whose own window carries no Xrm (custom page / dashboard iframe) did
 * nothing on click. Each action now asks for the capability it uses.
 */
import { executeClickAction } from '../ClickActionHandler';
import { OnClickAction, type IChartDefinition } from '../../types';

/* eslint-disable @typescript-eslint/no-explicit-any */

const originalParent = window.parent;

function setParentXrm(xrm: unknown): void {
  Object.defineProperty(window, 'parent', { value: { Xrm: xrm }, writable: true, configurable: true });
}

function chart(action: OnClickAction, target?: string): IChartDefinition {
  return {
    sprk_name: 'Chart',
    sprk_onclickaction: action,
    sprk_onclicktarget: target,
  } as unknown as IChartDefinition;
}

afterEach(() => {
  delete (window as any).Xrm;
  Object.defineProperty(window, 'parent', { value: originalParent, writable: true, configurable: true });
});

describe('ClickActionHandler — Xrm from the parent frame', () => {
  it('open record form uses the parent frame Xrm when window has none', async () => {
    const openForm = jest.fn().mockResolvedValue(undefined);
    setParentXrm({ Navigation: { openForm } });
    await executeClickAction({ chartDefinition: chart(OnClickAction.OpenRecordForm, 'sprk_event'), recordId: 'r1' });
    expect(openForm).toHaveBeenCalledWith({ entityName: 'sprk_event', entityId: 'r1' });
  });

  it('navigate to page skips a child frame Xrm without navigateTo', async () => {
    const navigateTo = jest.fn().mockResolvedValue(undefined);
    (window as any).Xrm = { WebApi: {}, Navigation: {} };
    setParentXrm({ Navigation: { navigateTo } });
    await executeClickAction({ chartDefinition: chart(OnClickAction.NavigateToPage, 'sprk_page'), recordId: 'r2' });
    expect(navigateTo).toHaveBeenCalledWith({ pageType: 'custom', name: 'sprk_page', recordId: 'r2' });
  });

  it('open side pane finds App.sidePanes on the parent frame', async () => {
    const navigate = jest.fn();
    const createPane = jest.fn().mockResolvedValue({ navigate });
    setParentXrm({ App: { sidePanes: { createPane } } });
    await executeClickAction({ chartDefinition: chart(OnClickAction.OpenSidePane, 'sprk_pane'), recordId: 'r3' });
    expect(createPane).toHaveBeenCalled();
    expect(navigate).toHaveBeenCalledWith({ pageType: 'custom', name: 'sprk_pane', recordId: 'r3' });
  });
});
