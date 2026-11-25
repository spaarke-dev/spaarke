/**
 * How the Email code page was launched: compose mode (from `openEmailCompose`), one record, or the list.
 *
 * Moved out of `main.tsx` unchanged in behaviour so it can be tested (ISS-008, #2948): `main.tsx` passes the query
 * string and a reader for the embedded host form's Xrm.
 */
import {
  cleanGuid,
  parseComposeRegarding,
  type ComposeRegarding,
  type OpenEmailComposeMode,
} from '@spaarke/ui-components';

const COMMUNICATION_ENTITY = 'sprk_communication';

/** Compose-mode launch: which composer, its source email, and (New only) the "Related to" record. */
export interface ComposeLaunch {
  mode: OpenEmailComposeMode;
  sourceId?: string;
  regarding?: ComposeRegarding;
}

export interface EmailLaunch {
  initialSelectedId?: string;
  hideList: boolean;
  compose?: ComposeLaunch;
}

/**
 * What the embedded host form says about its record (Pattern A). The host (`main.tsx`) reads these from the
 * cross-frame Xrm and passes plain values, so the Xrm reads stay where the `getXrm` capability guard can see them.
 */
export interface HostFormRecord {
  /** `Xrm.Utility.getPageContext().input`. */
  pageInput?: { entityName?: string; entityId?: string };
  /** `Xrm.Page.data.entity.getEntityName()`. */
  formEntityName?: string;
  /** `Xrm.Page.data.entity.getId()`. */
  formEntityId?: string;
}

const COMPOSE_MODES: readonly OpenEmailComposeMode[] = ['new', 'reply', 'replyAll', 'forward'];

/**
 * Resolve the incoming `sprk_communication` record id + view mode from the launch context, in precedence order:
 *   (0) `compose=<mode>` — compose mode (Pattern B via `openEmailCompose`), as its own top-level param or folded into
 *       `data`. Short-circuits the reading surface. An unknown mode opens a New email. For a New email the "Related
 *       to" record is read from the same scope the `compose` key came from (#2908).
 *   (a) `data` query param — Pattern B (`Xrm.Navigation.navigateTo({ data })`, e.g. `openEmailRecord`). Carries the
 *       bare communication id, optionally with a folded `&single=1` (or `view=record`) flag.
 *   (b) `id` query param — direct-link / dev fallback.
 *   (c) the embedded host form's record — Pattern A (Email page embedded on a `sprk_communication` form); never
 *       the record of a form for another table.
 *
 * Single-record ("form") mode is derived from a `single=1` / `view=record` flag, as its own top-level param or folded
 * into `data`. `hideList` is only meaningful when an id resolves. When nothing resolves → list mode.
 */
export function resolveEmailLaunch(search: string, readHostForm: () => HostFormRecord | null | undefined): EmailLaunch {
  const normalizeId = (raw: string | null | undefined): string | undefined => {
    const v = cleanGuid(raw ?? '');
    return v.length > 0 ? v : undefined;
  };
  const isSingleFlag = (params: URLSearchParams): boolean =>
    params.get('single') === '1' || params.get('view') === 'record';

  // (0) COMPOSE mode. Parsing `data` as URLSearchParams is safe for the record case too: a bare-guid `data` yields
  //     no `compose` key.
  try {
    const top = new URLSearchParams(search);
    const rawData = top.get('data');
    const dataParams = rawData ? new URLSearchParams(rawData) : null;
    const rawCompose = top.get('compose') ?? dataParams?.get('compose') ?? '';
    if (rawCompose) {
      const mode = (COMPOSE_MODES as readonly string[]).includes(rawCompose)
        ? (rawCompose as OpenEmailComposeMode)
        : 'new';
      const sourceId = normalizeId(top.get('id') ?? dataParams?.get('id'));
      const regarding = mode === 'new' ? parseComposeRegarding(top.get('compose') ? top : (dataParams ?? top)) : undefined;
      return { hideList: false, compose: { mode, sourceId, regarding } };
    }
  } catch {
    /* URL parse failure → fall through to record/list resolution */
  }

  let id: string | undefined;
  let single = false;

  try {
    const top = new URLSearchParams(search);
    single = isSingleFlag(top);

    // (a) data param. The value is either `<guid>` or, when the flag was folded in by the launcher,
    //     `<guid>&single=1` — split the id from any trailing flags (Dataverse surfaces the whole `data` string).
    const rawData = top.get('data');
    if (rawData) {
      const [idPart, ...rest] = rawData.split('&');
      id = normalizeId(idPart);
      if (rest.length > 0 && isSingleFlag(new URLSearchParams(rest.join('&')))) single = true;
    }

    // (b) id fallback param.
    if (!id) id = normalizeId(top.get('id'));
  } catch {
    /* URL parse failure → fall through to host-form / list mode */
  }

  // (c) embedded host form (Pattern A) — only when no param supplied an id. The form-entity fallback is used only
  //     when neither source names another table: on, say, a matter form the matter's id is not a communication id,
  //     so the page shows the list rather than trying to open a record that does not exist.
  if (!id) {
    try {
      const host = readHostForm();
      const input = host?.pageInput;
      const isOtherTable = (name: string | undefined): boolean => !!name && name.toLowerCase() !== COMMUNICATION_ENTITY;
      if (input?.entityId && !isOtherTable(input.entityName)) {
        id = normalizeId(input.entityId);
      }
      if (!id && !isOtherTable(input?.entityName) && !isOtherTable(host?.formEntityName)) {
        id = normalizeId(host?.formEntityId);
      }
    } catch {
      /* not embedded on a form — list mode */
    }
  }

  return { initialSelectedId: id, hideList: Boolean(id) && single };
}
