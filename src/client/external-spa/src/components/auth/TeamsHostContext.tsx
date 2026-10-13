/**
 * TeamsHostContext — "is this app running inside the Teams host?" for any component below `App`, without prop drilling
 * (spaarke-SPA-external-access-platform-r3 task 051 review; NFR-04: a redirect inside the Teams iframe is never allowed).
 *
 * Widget bodies are built by the widget registry (`createGridWidgetBody(configId)`), which passes them no host signal, so
 * a `BffAccessState` rendered from a record-detail modal could not learn it was in the Teams tab and would call
 * `loginRedirect` inside the iframe. `App` provides its `teamsHost` here; `BffAccessState` reads it when its explicit
 * `teamsHost` prop is absent (an explicit prop still wins).
 *
 * Outside a provider the value is `undefined` ("not told"), which `BffAccessState` resolves to the browser behaviour.
 */
import * as React from 'react';

const TeamsHostContext = React.createContext<boolean | undefined>(undefined);

export const TeamsHostProvider: React.FC<{ teamsHost: boolean; children?: React.ReactNode }> = ({
  teamsHost,
  children,
}) => <TeamsHostContext.Provider value={teamsHost}>{children}</TeamsHostContext.Provider>;

/** The host signal `App` provided, or `undefined` when there is no provider above. */
export function useTeamsHost(): boolean | undefined {
  return React.useContext(TeamsHostContext);
}
