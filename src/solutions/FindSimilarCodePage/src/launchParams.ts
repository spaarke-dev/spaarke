/**
 * launchParams.ts — Find Similar's launch `data` contract (task 113; closes #1479).
 *
 * `launchFindSimilarWizard`, WorkspaceGrid and FindSimilarWizardWidget send `documentId` and
 * `containerId`, but nothing read them: a preselected document was silently dropped. The code page
 * (`main.tsx`) and the in-app renderer now read them with this ONE function.
 *
 * `containerId` is parsed and carried but deliberately NOT used to pick anything: like the Upload
 * wizard (task 076), the server derives containers; a client-supplied one must not select a destination.
 */
import { parseDataParams } from "@spaarke/ui-components/utils/parseDataParams";
import { cleanGuid } from "@spaarke/ui-components/utils/guid";

export interface FindSimilarLaunch {
  /** Bare lower-case document id to preselect; `""` when none was supplied. */
  documentId: string;
  containerId: string;
}

/**
 * @param search a `?key=value&…` query string or an Xrm `?data=…` envelope; omitted reads the page URL.
 */
export function parseFindSimilarLaunch(search?: string): FindSimilarLaunch {
  const params = parseDataParams(search);
  const documentId = typeof params.documentId === "string" ? cleanGuid(params.documentId) : "";
  const containerId = typeof params.containerId === "string" ? params.containerId : "";
  return { documentId, containerId };
}
