/**
 * xrm-globals.d.ts — Minimal ambient declarations for Xrm globals.
 *
 * The ribbon scripts (WorkspaceLaunch.ts, EntityFormLaunch.ts) run as Dataverse
 * web resources where the Xrm SDK is injected as a global by the platform.
 * Rather than depend on @types/xrm (which brings in the full Xrm SDK surface),
 * we declare only the subset of types required by the launch scripts.
 *
 * Add declarations here as needed when additional Xrm APIs are used.
 */

declare namespace Xrm {
  namespace Navigation {
    interface NavigationOptions {
      target: 1 | 2;
      width?: { value: number; unit: "px" | "%" };
      height?: { value: number; unit: "px" | "%" };
      position?: 1 | 2;
      title?: string;
    }

    interface PageInputWebResource {
      pageType: "webresource";
      webresourceName: string;
      data?: string;
    }

    function navigateTo(
      pageInput: PageInputWebResource,
      navigationOptions?: NavigationOptions
    ): Promise<void>;
  }

  interface FormContext {
    data: {
      entity: {
        getId(): string;
        getEntityName(): string;
      };
    };
  }

  // spaarkeai-compose-r1 task 046: DocumentComposeLaunch reads the SPE drive-item
  // id (and optional drive id + file name) from the open `sprk_document` record
  // via WebApi.retrieveRecord — only the subset of WebApi declared here.
  interface WebApi {
    retrieveRecord(
      entityLogicalName: string,
      id: string,
      options?: string,
    ): Promise<{ [key: string]: unknown }>;
  }

  const WebApi: WebApi;
}
