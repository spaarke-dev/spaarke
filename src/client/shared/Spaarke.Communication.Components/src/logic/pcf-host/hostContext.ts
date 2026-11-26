/**
 * Host-form context helpers shared by the Communication* PCFs (#2967; copied between ConversationPanel and
 * MessageActions). `cleanGuid` comes from `@spaarke/ui-components` (ADR-044: one GUID normaliser) and is passed in, so
 * this module stays free of that package's barrel. The readers are the PCF's own one-liners over the host form
 * (`getXrm('page')` ...): `getXrm` itself is never passed around, because the Xrm capability guard follows every
 * `getXrm` value to its uses.
 */
export interface IHostContextDeps {
  /** The host form's record id (raw, e.g. `{ABC-...}`), or undefined. */
  readEntityId: () => unknown;
  /** The host form's entity logical name, or undefined. */
  readEntityName: () => unknown;
  cleanGuid: (id: string | null | undefined) => string;
}

export function createHostContext(deps: IHostContextDeps): {
  getHostRecordId: () => string | undefined;
  getHostEntityName: () => string | undefined;
} {
  return {
    getHostRecordId: () => {
      try {
        const id = deps.readEntityId();
        if (typeof id === 'string' && id.length > 0) return deps.cleanGuid(id);
        // eslint-disable-next-line no-restricted-syntax -- a throwing host probe means no value; callers treat undefined as absent
      } catch {
        /* ignore */
      }
      return undefined;
    },
    getHostEntityName: () => {
      try {
        const name = deps.readEntityName();
        if (typeof name === 'string' && name.length > 0) return name;
        // eslint-disable-next-line no-restricted-syntax -- a throwing host probe means no value; callers treat undefined as absent
      } catch {
        /* ignore */
      }
      return undefined;
    },
  };
}
