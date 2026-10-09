/**
 * InAppWizardHost — the ONE in-app host for the Create wizards (spaarke-ontology-platform-r1
 * task 112; D-26; ADR-050 as amended 2026-10-07, launch rule (a)).
 *
 * A Spaarke React surface (the Console / SpaarkeAi, and LegalWorkspace running inside it) mounts
 * this component once. While it is mounted, the shared launchers in `wizardLaunchers.ts`
 * (`launchCreate*Wizard`, `launchAssignWorkWizard`, `navigateToWebResourceSurfaceAsync` and so
 * `launchSurface`) open these five wizards HERE, in `SprkModal`, instead of in an
 * `Xrm.Navigation.navigateTo(webresource, { target: 2 })` dialog — whose white title bar is
 * platform chrome that cannot be themed:
 *
 *   sprk_creatematterwizard · sprk_createprojectwizard · sprk_createeventwizard ·
 *   sprk_createtodowizard · sprk_createworkassignmentwizard · sprk_summarizefileswizard
 *
 * Task 113 (D-26) adds three more that live in code-page solutions this library cannot import
 * (`DocumentUploadWizard`, `FindSimilarCodePage`, `WorkspaceLayoutWizard`):
 *
 *   sprk_documentuploadwizard · sprk_findsimilar · sprk_workspacelayoutwizard
 *
 * The mounting app passes their `renderers`; the host registers only the names it can actually open,
 * so a launch it has no renderer for keeps using `navigateTo`.
 *
 * Without a mounted host the launchers keep calling `navigateTo` exactly as before, and the
 * ribbon scripts (`sprk_wizard_commands.js`) never reach this code: they stay on `navigateTo` and
 * the wizard code pages (embedded, hideTitle) stay deployable for them (launch rule (b)).
 *
 * The wizard renders NON-embedded: `WizardShell` inside `SprkModal`, named size `wizard`, dismiss
 * `explicit` (both the shell's defaults — Escape and the backdrop do not close it), `uiScale`
 * forwarded. Each wizard gets the same Xrm adapters and props its code page `main.tsx` builds; the
 * one difference is closing: the host unmounts the wizard (`onClose`), it never calls
 * `navigationService.closeDialog()`, which clicks the platform close button of the dialog the
 * Console itself may be running in.
 *
 * Hand-off: when the launch `data` carries a `handoffId` (an Assistant `launchSurface` create), the
 * host reads the envelope, pre-seeds the wizard exactly as the code page does, and on a successful
 * create writes the committed result before closing, so `launchSurface` reads the same outcome it
 * reads after a `navigateTo` close.
 *
 * Theme: no FluentProvider of its own — the modal inherits the host's (ADR-021, light and dark).
 *
 * @see wizardLaunchers.ts — `registerInAppWizardHost`, the routing seam
 * @see CreateAnalysisWizardWidget — the in-app modal-mode reference (WorkspacePane hosts it)
 */

import * as React from 'react';

import { CreateMatterWizard } from '../CreateMatterWizard/CreateMatterWizard';
import { mapMatterHandoffSeed } from '../CreateMatterWizard/handoffSeedMapping';
import { CreateProjectWizard } from '../CreateProjectWizard/CreateProjectWizard';
import { mapProjectHandoffSeed } from '../CreateProjectWizard/handoffSeedMapping';
import { CreateEventWizard } from '../CreateEventWizard/CreateEventWizard';
import { mapEventHandoffSeed } from '../CreateEventWizard/handoffSeedMapping';
import TodoWizardDialog from '../CreateTodoWizard/TodoWizardDialog';
import { resolveCurrentUserContact, withTodoCreatedBroadcast } from '../CreateTodoWizard/todoWizardHostSupport';
import WorkAssignmentWizardDialog from '../CreateWorkAssignmentWizard/WorkAssignmentWizardDialog';
import { SummarizeFilesDialog } from '../SummarizeFilesWizard/SummarizeFilesDialog';
import {
  DEFAULT_IN_APP_WIZARD_NAMES,
  registerInAppWizardHost,
  type InAppWizardName,
  type InAppWizardRequest,
} from '../WorkspaceShell/wizardLaunchers';
import { createXrmDataService } from '../../utils/adapters/xrmDataServiceAdapter';
import { createXrmNavigationService } from '../../utils/adapters/xrmNavigationServiceAdapter';
import { withBffChildWrites } from '../../utils/adapters/bffChildWriteAdapter';
import { getXrm } from '../../utils/xrmContext';
import { cleanGuid } from '../../utils/guid';
import { EntityCreationService, type IUserBuCascadeDefaults } from '../../services/EntityCreationService';
import type { AuthenticatedFetchFn } from '../../services/EntityCreationService';
import type { IDataService, INavigationService } from '../../types/serviceInterfaces';
import { completeHandoff, handoffSeed, readHandoffFromUrl } from '../../services/surfaceHandoff/readHandoff';

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

/** The wizards a mounting app renders itself (they live in code-page solutions, not this library). */
export type InAppHostedWizardName = 'sprk_documentuploadwizard' | 'sprk_findsimilar' | 'sprk_workspacelayoutwizard';

/** Everything a supplied renderer needs to mount its wizard non-embedded inside the host's modal. */
export interface IInAppWizardRenderContext {
  /** The launch `data` string exactly as the wizard's `navigateTo` call would carry it. */
  readonly data: string;
  /** Close the wizard (resolves the launcher's promise). The ONLY way a hosted wizard may close. */
  readonly onClose: () => void;
  readonly authenticatedFetch: AuthenticatedFetchFn;
  readonly bffBaseUrl: string;
  readonly tenantId?: string;
  readonly uiScale?: number;
  readonly dataService: IDataService;
  readonly navigationService: INavigationService;
  /** The Assistant hand-off file refs when `data` carries a `handoffId` (else `undefined`). */
  readonly initialFileRefs?: { sessionId: string; fileIds: readonly string[]; fileNames?: readonly string[] };
}

/** Renders one hosted wizard. Called on every render of the open wizard; keep it pure. */
export type InAppWizardRenderer = (context: IInAppWizardRenderContext) => React.ReactNode;

export type InAppWizardRenderers = Partial<Record<InAppHostedWizardName, InAppWizardRenderer>>;

export interface IInAppWizardHostProps {
  /** `@spaarke/auth` authenticated fetch (ADR-028: a function, never a token). */
  authenticatedFetch: AuthenticatedFetchFn;
  /** Spaarke BFF base URL from the host's runtime config (a base URL, not a token). */
  bffBaseUrl: string;
  /** AAD tenant id, forwarded to the wizards' file-indexing step. Omitted → indexing skipped. */
  tenantId?: string;
  /** The app-shell `--sprk-ui-scale` (`useUiScale()`), forwarded to the wizard's SprkModal. */
  uiScale?: number;
  /**
   * Renderers for the wizards that live in code-page solutions (Upload Documents, Find Similar,
   * Workspace layout). The host opens a name only if it has a renderer for it; the rest keep
   * using `navigateTo`.
   */
  renderers?: InAppWizardRenderers;
}

// ---------------------------------------------------------------------------
// Xrm-host resolvers — the same user → business-unit chain the Create code pages use
// ---------------------------------------------------------------------------

async function resolveUserBuDefaults(): Promise<IUserBuCascadeDefaults> {
  // Shared cross-frame walker (task 081 / C-8).
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const xrm: any = getXrm(['webApi', 'utility']);
  if (!xrm?.WebApi?.retrieveRecord) throw new Error('Xrm.WebApi not available');
  const userId = cleanGuid(xrm.Utility.getGlobalContext().userSettings.userId);
  return EntityCreationService.resolveUserBuDefaults(xrm.WebApi, userId);
}

async function resolveSpeContainerId(): Promise<string> {
  return (await resolveUserBuDefaults()).containerId ?? '';
}

// ---------------------------------------------------------------------------
// Host
// ---------------------------------------------------------------------------

interface IActiveWizard {
  readonly key: number;
  readonly name: InAppWizardName;
  readonly data: string;
  /** Resolves the launcher's promise (the `navigateTo`-promise equivalent: "the modal closed"). */
  readonly settle: () => void;
}

export const InAppWizardHost: React.FC<IInAppWizardHostProps> = ({
  authenticatedFetch,
  bffBaseUrl,
  tenantId,
  uiScale,
  renderers,
}) => {
  const [active, setActive] = React.useState<IActiveWizard | null>(null);
  const activeRef = React.useRef<IActiveWizard | null>(null);
  const sequenceRef = React.useRef(0);

  // Renderers are read at render time (a ref), but the names the host can open are part of the
  // registration — so the registration is keyed by the renderer NAMES, not the object identity.
  const renderersRef = React.useRef<InAppWizardRenderers | undefined>(renderers);
  renderersRef.current = renderers;
  const rendererNamesKey = Object.keys(renderers ?? {})
    .filter(name => typeof renderers?.[name as InAppHostedWizardName] === 'function')
    .sort()
    .join(',');

  React.useEffect(() => {
    const supported: InAppWizardName[] = [
      ...DEFAULT_IN_APP_WIZARD_NAMES,
      ...(rendererNamesKey ? (rendererNamesKey.split(',') as InAppHostedWizardName[]) : []),
    ];
    const unregister = registerInAppWizardHost((request: InAppWizardRequest) => {
      // One wizard at a time: a second launch while one is open is ignored rather than replacing -
      // and losing - the open one. It resolves at once, flagged busy, so the launcher reports
      // "opened nothing" instead of "opened and closed".
      if (activeRef.current) return Promise.resolve({ busy: true as const });
      return new Promise<void>(resolve => {
        sequenceRef.current += 1;
        const next: IActiveWizard = {
          key: sequenceRef.current,
          name: request.webresourceName,
          data: request.data,
          settle: resolve,
        };
        activeRef.current = next;
        setActive(next);
      });
    }, supported);
    return () => {
      unregister();
      // Never leave a launcher awaiting a wizard that can no longer close.
      activeRef.current?.settle();
      activeRef.current = null;
      setActive(null);
    };
  }, [rendererNamesKey]);

  const close = React.useCallback(() => {
    const current = activeRef.current;
    activeRef.current = null;
    setActive(null);
    current?.settle();
  }, []);

  // Services, built once per host — the adapters the code pages build (createXrm*), resolved
  // lazily against the cross-frame Xrm at call time.
  const dataService = React.useMemo(() => createXrmDataService(), []);
  const navigationService = React.useMemo(() => createXrmNavigationService(), []);
  // The To Do code page's data service: child writes through the BFF (UAC-r2 G5), and a successful
  // sprk_todo create broadcasts `sprk_todo:created` so the To Do widget refetches (todo.registration).
  const todoDataService = React.useMemo(
    () =>
      withTodoCreatedBroadcast(withBffChildWrites(createXrmDataService(), authenticatedFetch, bffBaseUrl || undefined)),
    [authenticatedFetch, bffBaseUrl]
  );

  if (!active) return null;

  return (
    <InAppWizard
      key={active.key}
      active={active}
      onClose={close}
      authenticatedFetch={authenticatedFetch}
      bffBaseUrl={bffBaseUrl}
      tenantId={tenantId}
      uiScale={uiScale}
      renderers={renderersRef.current}
      dataService={dataService}
      todoDataService={todoDataService}
      navigationService={navigationService}
    />
  );
};

// ---------------------------------------------------------------------------
// One open wizard (keyed per launch, so every open starts from a fresh state)
// ---------------------------------------------------------------------------

interface IInAppWizardProps extends IInAppWizardHostProps {
  active: IActiveWizard;
  onClose: () => void;
  dataService: ReturnType<typeof createXrmDataService>;
  todoDataService: ReturnType<typeof createXrmDataService>;
  navigationService: ReturnType<typeof createXrmNavigationService>;
}

const InAppWizard: React.FC<IInAppWizardProps> = ({
  active,
  onClose,
  authenticatedFetch,
  bffBaseUrl,
  tenantId,
  uiScale,
  renderers,
  dataService,
  todoDataService,
  navigationService,
}) => {
  // The launch `data` is the same `key=value&…` string the code page would receive on its URL.
  const handoff = React.useMemo(() => readHandoffFromUrl(`?${active.data}`), [active.data]);
  const seed = React.useMemo(() => handoffSeed(handoff), [handoff]);
  const initialFileRefs = React.useMemo(
    () =>
      seed?.sessionId && seed.fileIds.length > 0
        ? { sessionId: seed.sessionId, fileIds: seed.fileIds, fileNames: seed.fileNames }
        : undefined,
    [seed]
  );
  const matterValues = React.useMemo(() => mapMatterHandoffSeed(seed), [seed]);
  const projectValues = React.useMemo(() => mapProjectHandoffSeed(seed), [seed]);
  const eventValues = React.useMemo(() => mapEventHandoffSeed(seed), [seed]);

  // A successful create writes the committed hand-off result (honest ack), then closes. Cancel
  // closes without writing — `launchSurface` infers cancellation from the absent result.
  const onComplete = React.useCallback(
    (recordId?: string) => {
      if (handoff?.handoffId) completeHandoff(handoff.handoffId, recordId);
      onClose();
    },
    [handoff, onClose]
  );

  // To Do: default "Assigned To" to the current user's contact, as the code page does.
  const [defaultAssignedTo, setDefaultAssignedTo] = React.useState<
    { contactId: string; contactName?: string } | undefined
  >(undefined);
  React.useEffect(() => {
    if (active.name !== 'sprk_createtodowizard') return undefined;
    let cancelled = false;
    void resolveCurrentUserContact(todoDataService).then(contact => {
      if (!cancelled && contact) setDefaultAssignedTo(contact);
    });
    return () => {
      cancelled = true;
    };
  }, [active.name, todoDataService]);

  const common = { open: true, embedded: false, uiScale, onClose, navigationService, bffBaseUrl };
  // CreateProjectWizard / CreateEventWizard / SummarizeFilesDialog type the prop as `typeof fetch`; the `@spaarke/auth`
  // function their code pages pass is the same `(url, init)` shape (string URLs only).
  const fetchForFetchTyped = authenticatedFetch as typeof fetch;

  switch (active.name) {
    case 'sprk_creatematterwizard':
      return (
        <CreateMatterWizard
          {...common}
          authenticatedFetch={authenticatedFetch}
          dataService={dataService}
          onComplete={onComplete}
          resolveSpeContainerId={resolveSpeContainerId}
          resolveUserBuDefaults={resolveUserBuDefaults}
          tenantId={tenantId}
          initialFormValues={matterValues}
          initialFileRefs={initialFileRefs}
        />
      );
    case 'sprk_createprojectwizard':
      return (
        <CreateProjectWizard
          {...common}
          authenticatedFetch={fetchForFetchTyped}
          dataService={dataService}
          onComplete={onComplete}
          resolveSpeContainerId={resolveSpeContainerId}
          resolveUserBuDefaults={resolveUserBuDefaults}
          tenantId={tenantId}
          initialFormValues={projectValues}
          initialFileRefs={initialFileRefs}
        />
      );
    case 'sprk_createeventwizard':
      return (
        <CreateEventWizard
          {...common}
          authenticatedFetch={fetchForFetchTyped}
          dataService={dataService}
          onComplete={onComplete}
          resolveSpeContainerId={resolveSpeContainerId}
          tenantId={tenantId}
          initialFormValues={eventValues}
          initialFileRefs={initialFileRefs}
        />
      );
    case 'sprk_createtodowizard':
      return (
        <TodoWizardDialog
          {...common}
          authenticatedFetch={authenticatedFetch}
          dataService={todoDataService}
          resolveSpeContainerId={resolveSpeContainerId}
          defaultAssignedTo={defaultAssignedTo}
        />
      );
    case 'sprk_createworkassignmentwizard':
      return (
        <WorkAssignmentWizardDialog
          {...common}
          authenticatedFetch={authenticatedFetch}
          dataService={dataService}
          // #1420: a successful create writes the committed hand-off result (the new record id) and
          // closes, so `launchSurface` resolves committed and Quick Start's `onRecordCreated` fires.
          onComplete={onComplete}
          resolveSpeContainerId={resolveSpeContainerId}
          tenantId={tenantId}
          initialFileRefs={initialFileRefs}
        />
      );
    case 'sprk_summarizefileswizard':
      // Parity with the code page (`SummarizeFilesWizard/main.tsx`): it writes no hand-off result
      // (the wizard commits no single record), so a hand-off launch reads back as cancelled there too.
      return (
        <SummarizeFilesDialog
          {...common}
          authenticatedFetch={fetchForFetchTyped}
          dataService={dataService}
          initialFileRefs={initialFileRefs}
        />
      );
    case 'sprk_documentuploadwizard':
    case 'sprk_findsimilar':
    case 'sprk_workspacelayoutwizard': {
      // Supplied by the mounting app (these wizards live in code-page solutions). The registration
      // only declares names that have a renderer, so a missing one is a defensive no-op.
      const render = renderers?.[active.name];
      if (!render) return null;
      return (
        <>
          {render({
            data: active.data,
            onClose,
            authenticatedFetch,
            bffBaseUrl,
            tenantId,
            uiScale,
            dataService,
            navigationService,
            initialFileRefs,
          })}
        </>
      );
    }
    default: {
      const exhaustive: never = active.name;
      void exhaustive;
      return null;
    }
  }
};

export default InAppWizardHost;
