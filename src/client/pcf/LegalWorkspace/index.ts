import { IInputs, IOutputs } from "./generated/ManifestTypes";
import { createElement } from "react";
import { createRoot, Root } from "react-dom/client";
import { LegalWorkspaceApp } from "./LegalWorkspaceApp";

const CONTROL_VERSION = "1.0.1";

export class LegalWorkspace
  implements ComponentFramework.StandardControl<IInputs, IOutputs>
{
  private _container!: HTMLDivElement;
  private _context!: ComponentFramework.Context<IInputs>;
  private _root!: Root;
  private _notifyOutputChanged!: () => void;

  constructor() {
    // No-op — PCF instantiates once, then calls init()
  }

  public init(
    context: ComponentFramework.Context<IInputs>,
    notifyOutputChanged: () => void,
    state: ComponentFramework.Dictionary,
    container: HTMLDivElement
  ): void {
    this._context = context;
    this._notifyOutputChanged = notifyOutputChanged;
    this._container = container;

    // Track container resize so updateView fires on layout changes
    context.mode.trackContainerResize(true);

    // React 18: createRoot (Custom Page exception — React 18 is allowed here)
    this._root = createRoot(container);
    this._render();
  }

  public updateView(context: ComponentFramework.Context<IInputs>): void {
    this._context = context;
    this._render();
  }

  public getOutputs(): IOutputs {
    return {};
  }

  public destroy(): void {
    if (this._root) {
      this._root.unmount();
    }
  }

  private _render(): void {
    const allocatedWidth = this._context.mode.allocatedWidth;
    const allocatedHeight = this._context.mode.allocatedHeight;

    this._root.render(
      createElement(LegalWorkspaceApp, {
        version: CONTROL_VERSION,
        allocatedWidth,
        allocatedHeight,
        webApi: this._context.webAPI,
        userId: this._context.userSettings.userId,
      })
    );
  }
}
