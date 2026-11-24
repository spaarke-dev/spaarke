/**
 * Host-form context helpers shared by the Communication* PCFs (#2967; copied between ConversationPanel and
 * MessageActions). `getXrm` and `cleanGuid` come from `@spaarke/ui-components` (ADR-044: one GUID normaliser) and are
 * passed in, so this module stays free of that package's barrel.
 */
export interface IHostContextDeps {
  /** `getXrm` from `@spaarke/ui-components`. */
  getXrm: (capability: 'page') => unknown;
  cleanGuid: (id: string | null | undefined) => string;
}

interface IXrmPageView {
  Page?: { data?: { entity?: { getId?: () => unknown; getEntityName?: () => unknown } } };
}

export function createHostContext(deps: IHostContextDeps): {
  getHostRecordId: () => string | undefined;
  getHostEntityName: () => string | undefined;
} {
  const entity = () => (deps.getXrm('page') as IXrmPageView | undefined)?.Page?.data?.entity;
  return {
    getHostRecordId: () => {
      try {
        const id = entity()?.getId?.();
        if (typeof id === 'string' && id.length > 0) return deps.cleanGuid(id);
      } catch {
        /* ignore */
      }
      return undefined;
    },
    getHostEntityName: () => {
      try {
        const name = entity()?.getEntityName?.();
        if (typeof name === 'string' && name.length > 0) return name;
      } catch {
        /* ignore */
      }
      return undefined;
    },
  };
}
