/**
 * launchCreate — the SINGLE launch seam for the "create from this email" actions
 * (Create Event / Create To Do / Link Invoice) on the CommunicationActions bar.
 *
 * UAT R3 C11-3 (owner decision): these three actions previously navigated to a new
 * page / quick-create pane. They must now open as a MODAL. Per
 * `docs/standards/MODAL-DECISION-CRITERIA.md` (Two-Layout Standard, Layout 1) the
 * sanctioned default for opening a Dataverse record/create form as a modal from a
 * PCF is OOB `Xrm.Navigation.navigateTo({ pageType:'entityrecord' }, { target: 2 })`.
 *
 * A custom Fluent v9 create dialog does NOT drop into this PCF-on-OOB-form context:
 * the shared `CreateEventWizard` / `CreateTodoWizard` / `CreateInvoiceWizard`
 * components are WizardShell/Code-Page harnessed (need `bffBaseUrl`, deployed web
 * resources, and a regarding/service context designed for the LegalWorkspace /
 * SpaarkeAi surfaces). So we use the OOB in-app dialog now.
 *
 * The owner requirement is that OOB↔custom is swappable LATER WITHOUT touching the
 * button call sites. That is exactly what this module provides: the call sites only
 * ever call `launchCreate(kind, ctx)`. To swap to a custom dialog (or delegate to the
 * shared `navigateToEntityRecordSurfaceAsync` / a Code-Page wizard launcher), change
 * ONLY the body of `launchCreate` here — the App call sites do not change.
 */

// eslint-disable-next-line @typescript-eslint/no-explicit-any
type XrmNavigateTo = (pageInput: any, navigationOptions: any) => Promise<unknown>;

/** The three "create from this email" flows the action bar exposes. */
export type CreateKind = 'event' | 'todo' | 'invoice';

export interface CreateLaunchTarget {
  /** Dataverse entity logical name of the create form to open. */
  entityName: string;
  /** Dialog title shown in the OOB modal chrome. */
  title: string;
}

/**
 * kind → target-entity + dialog title. Mirrors the entity mapping the prior
 * navigation used (event → sprk_event, todo → sprk_todo, invoice → sprk_invoice),
 * so the record/context target is preserved through the modal launch.
 */
export const CREATE_LAUNCH_TARGETS: Record<CreateKind, CreateLaunchTarget> = {
  event: { entityName: 'sprk_event', title: 'Create Event' },
  todo: { entityName: 'sprk_todo', title: 'Create To Do' },
  invoice: { entityName: 'sprk_invoice', title: 'Link Invoice' },
};

export interface LaunchCreateOptions {
  /**
   * Optional default field values to pre-seed the OOB create form (attribute
   * logical-name keyed). The prior navigation seeded nothing; this seam carries the
   * seam for future context prefill without changing call sites.
   */
  defaultValues?: Record<string, unknown>;
  /**
   * Injectable navigateTo — used by tests. When omitted, resolves the host
   * `Xrm.Navigation.navigateTo` by frame-walking (PCF runs in an iframe).
   */
  navigateTo?: XrmNavigateTo;
  /** Invoked on a hard launch failure (no host) or a rejected navigation (e.g. cancel). */
  onError?: (err: unknown) => void;
}

/** Walk window/parent/top frames to locate a bound `Xrm.Navigation.navigateTo`. */
function resolveHostNavigateTo(): XrmNavigateTo | null {
  if (typeof window === 'undefined') return null;
  const frames: Window[] = [window];
  try {
    if (window.parent && window.parent !== window) frames.push(window.parent);
  } catch {
    /* cross-origin — skip */
  }
  try {
    if (window.top && window.top !== window) frames.push(window.top);
  } catch {
    /* cross-origin — skip */
  }
  for (const frame of frames) {
    try {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      const nav = (frame as any).Xrm?.Navigation;
      if (nav && typeof nav.navigateTo === 'function') {
        return nav.navigateTo.bind(nav) as XrmNavigateTo;
      }
    } catch {
      /* cross-origin — skip */
    }
  }
  return null;
}

/**
 * Open the create form for `kind` as an in-app centered modal dialog
 * (`Xrm.Navigation.navigateTo`, `target: 2`, `position: 1`). Fire-and-forget: the
 * caller does not await the outcome (parity with the prior launch behavior).
 */
export function launchCreate(kind: CreateKind, options: LaunchCreateOptions = {}): void {
  const target = CREATE_LAUNCH_TARGETS[kind];
  const onError = options.onError ?? (() => undefined);
  const navigateTo = options.navigateTo ?? resolveHostNavigateTo();
  if (!navigateTo) {
    onError(new Error('Create is unavailable in this host.'));
    return;
  }
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const pageInput: Record<string, any> = { pageType: 'entityrecord', entityName: target.entityName };
  if (options.defaultValues && Object.keys(options.defaultValues).length > 0) {
    pageInput.data = options.defaultValues;
  }
  const navigationOptions = {
    target: 2,
    position: 1,
    width: { value: 70, unit: '%' },
    height: { value: 80, unit: '%' },
    title: target.title,
  };
  try {
    Promise.resolve(navigateTo(pageInput, navigationOptions)).catch(onError);
  } catch (err) {
    onError(err);
  }
}
