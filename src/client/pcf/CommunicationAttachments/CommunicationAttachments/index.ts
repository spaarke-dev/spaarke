/**
 * CommunicationAttachments PCF Control — Virtual Pattern (ADR-022)
 *
 * Attachment list for `sprk_communication` that replaces the OOB attachment
 * subgrid (UAT #2). Lists the communication's `sprk_communicationattachment`
 * rows (name + type; inline-image types filtered out) and, on row click, opens
 * the shared `RichFilePreviewDialog` for that attachment's `sprk_document` —
 * resolving the preview URL via the BFF and routing `.eml`/`message-rfc822`
 * documents to download/open instead of Graph inline preview.
 *
 * Per ADR-022:
 *   - virtual control (`control-type="virtual"` in manifest)
 *   - React 16 + Fluent v9 supplied as platform libraries (NOT bundled)
 *   - updateView() returns a React element instead of rendering imperatively
 *
 * Per ADR-028: auth is bootstrapped inside the React host (useEffect), never in
 * this class's init(); no `PublicClientApplication` is instantiated here (INV-7).
 * This control does not write any bound value — `getOutputs()` echoes the last
 * bound value so the framework never clobbers it with `undefined`.
 */

import * as React from 'react';
import { IInputs, IOutputs } from './generated/ManifestTypes';
import { CommunicationAttachmentsHost } from './CommunicationAttachmentsHost';

const CONTROL_VERSION = '1.1.0';

export class CommunicationAttachments implements ComponentFramework.ReactControl<IInputs, IOutputs> {
  private _boundField: string | undefined;

  public init(
    _context: ComponentFramework.Context<IInputs>,
    _notifyOutputChanged: () => void,
    _state: ComponentFramework.Dictionary
  ): void {
    /* no-op — data fetch + auth happen inside the React host (ADR-028) */
  }

  public updateView(context: ComponentFramework.Context<IInputs>): React.ReactElement {
    // Echo the bound anchor value so getOutputs() never clobbers it.
    const raw = context.parameters.boundField?.raw;
    this._boundField = typeof raw === 'string' ? raw : undefined;

    return React.createElement(CommunicationAttachmentsHost, {
      context,
      version: CONTROL_VERSION,
    });
  }

  public getOutputs(): IOutputs {
    return {
      boundField: this._boundField,
    };
  }

  public destroy(): void {
    /* no-op for virtual controls */
  }
}
