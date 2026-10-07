/**
 * openPlaybookLibrary — the one "Browse Playbooks" opener for both Daily
 * Briefing hosts (task 081 round 4, review F6).
 *
 * The standalone Code Page (`src/solutions/DailyBriefing/src/main.tsx`) used
 * to detach `navigateTo` into a local and call it unbound; the real
 * `Xrm.Navigation.navigateTo` depends on its receiver (the platform rejects an
 * unbound call). The stub below RECORDS its receiver rather than rejecting, so
 * a regression fails on the `mock.contexts` assertion, not as an unhandled
 * rejection (round 6, review R5-7).
 */
import * as fs from 'fs';
import * as path from 'path';
import * as React from 'react';
import * as ts from 'typescript';
import { openPlaybookLibrary } from '../src/utils/openPlaybookLibrary';
import { browsePlaybooks } from '../../../../solutions/DailyBriefing/src/browsePlaybooks';

jest.mock('../src/components/DailyBriefingApp', () => ({ DailyBriefingApp: () => null }));
// eslint-disable-next-line import/first
import { createDailyBriefingRegistration } from '../src/widgets/dailyBriefing.registration';

function registrationBrowseHandler(): () => void {
  const config = createDailyBriefingRegistration().factory({} as never);
  const element = (
    config as unknown as { renderContent: () => React.ReactElement<{ onBrowsePlaybooks: () => void }> }
  ).renderContent();
  return element.props.onBrowsePlaybooks;
}

function makeXrm() {
  const Navigation = {
    navigateTo: jest.fn((..._args: unknown[]) => Promise.resolve(undefined)),
  };
  return { WebApi: {}, Navigation };
}

describe('openPlaybookLibrary', () => {
  const originalParent = window.parent;
  afterEach(() => {
    delete (window as unknown as { Xrm?: unknown }).Xrm;
    Object.defineProperty(window, 'parent', { value: originalParent, writable: true, configurable: true });
  });

  it('calls Xrm.Navigation.navigateTo as a method with the Playbook Library page', () => {
    const xrm = makeXrm();
    (window as unknown as { Xrm: unknown }).Xrm = xrm;
    openPlaybookLibrary('[test]');
    expect(xrm.Navigation.navigateTo).toHaveBeenCalledTimes(1);
    expect(xrm.Navigation.navigateTo.mock.contexts[0]).toBe(xrm.Navigation);
    expect(xrm.Navigation.navigateTo.mock.calls[0][0]).toMatchObject({
      pageType: 'webresource',
      webresourceName: 'sprk_playbooklibrary',
    });
  });

  it('uses the parent frame when the window has no Xrm that can navigate', () => {
    const xrm = makeXrm();
    (window as unknown as { Xrm: unknown }).Xrm = { WebApi: {} };
    Object.defineProperty(window, 'parent', { value: { Xrm: xrm }, writable: true, configurable: true });
    openPlaybookLibrary('[test]');
    expect(xrm.Navigation.navigateTo).toHaveBeenCalledTimes(1);
  });

  it('warns and does nothing when no frame can navigate', () => {
    const warn = jest.spyOn(console, 'warn').mockImplementation(() => undefined);
    expect(() => openPlaybookLibrary('[test]')).not.toThrow();
    expect(warn).toHaveBeenCalledWith(expect.stringContaining('[test] Xrm.Navigation unavailable'));
    warn.mockRestore();
  });

  // Behavioural (round 5, R4-8): each host's handler, invoked as the UI would
  // invoke it, must call navigateTo bound to Navigation. The Code Page's handler
  // is the module `main.tsx` passes as `onBrowsePlaybooks` (main.tsx itself
  // cannot be imported under jest: import.meta.env).
  it.each([
    ['standalone Code Page (browsePlaybooks)', () => browsePlaybooks()],
    ['embedded registration (onBrowsePlaybooks)', () => registrationBrowseHandler()()],
  ])('%s calls Xrm.Navigation.navigateTo as a method', (_host, invoke) => {
    const xrm = makeXrm();
    (window as unknown as { Xrm: unknown }).Xrm = xrm;
    invoke();
    expect(xrm.Navigation.navigateTo).toHaveBeenCalledTimes(1);
    expect(xrm.Navigation.navigateTo.mock.contexts[0]).toBe(xrm.Navigation);
    expect(xrm.Navigation.navigateTo.mock.calls[0][0]).toMatchObject({ webresourceName: 'sprk_playbooklibrary' });
  });

  // The Code Page wires that handler (round 6, review R5-7). `main.tsx` cannot be
  // imported under jest, so its JSX is read with the TypeScript parser: the
  // `DailyBriefingApp` element's `onBrowsePlaybooks` must be the identifier
  // imported as `browsePlaybooks` from './browsePlaybooks'.
  it('the Daily Briefing Code Page passes browsePlaybooks as onBrowsePlaybooks', () => {
    const file = path.resolve(__dirname, '../../../../solutions/DailyBriefing/src/main.tsx');
    const sf = ts.createSourceFile(
      file,
      fs.readFileSync(file, 'utf8'),
      ts.ScriptTarget.Latest,
      true,
      ts.ScriptKind.TSX
    );

    const imported = new Set<string>();
    const handlers: string[] = [];
    const visit = (n: ts.Node): void => {
      if (
        ts.isImportDeclaration(n) &&
        (n.moduleSpecifier as ts.StringLiteral).text === './browsePlaybooks' &&
        n.importClause?.namedBindings &&
        ts.isNamedImports(n.importClause.namedBindings)
      ) {
        for (const el of n.importClause.namedBindings.elements)
          if ((el.propertyName ?? el.name).text === 'browsePlaybooks') imported.add(el.name.text);
      }
      if (
        (ts.isJsxSelfClosingElement(n) || ts.isJsxOpeningElement(n)) &&
        n.tagName.getText(sf) === 'DailyBriefingApp'
      ) {
        for (const attr of n.attributes.properties) {
          if (ts.isJsxAttribute(attr) && attr.name.getText(sf) === 'onBrowsePlaybooks') {
            const e =
              attr.initializer && ts.isJsxExpression(attr.initializer) ? attr.initializer.expression : undefined;
            handlers.push(e && ts.isIdentifier(e) ? e.text : `(not an identifier: ${e?.getText(sf)})`);
          }
        }
      }
      ts.forEachChild(n, visit);
    };
    visit(sf);

    expect(handlers).toHaveLength(1);
    expect(imported.has(handlers[0])).toBe(true);
  });
});
