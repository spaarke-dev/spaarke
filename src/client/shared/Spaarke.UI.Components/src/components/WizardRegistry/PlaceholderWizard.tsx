/**
 * PlaceholderWizard.tsx
 * Temporary stand-in for `wizardRegistry` keys that don't have a real
 * wizard implementation yet.
 *
 * TEMPORARY PLACEHOLDER (task 011, visual-host-create-button-r1):
 *   - `invoice`         -> replaced by `CreateInvoiceWizard` in task 030.
 *   - `kpi-assessment`  -> replaced by `CreateKPIAssessmentWizard` in task 040.
 *
 * Until those wizards ship, this component gives `wizardRegistry.ts` a real
 * module to `lazy()`-import (pointing at a not-yet-created path would break
 * every build touching this module) and lets an unexpected mount of one of
 * these two keys degrade gracefully with a message instead of crashing.
 *
 * Not intended to be imported directly outside `wizardRegistry.ts`.
 */
import * as React from 'react';
import {
  Dialog,
  DialogSurface,
  DialogTitle,
  DialogBody,
  DialogActions,
  DialogContent,
  Button,
  Text,
} from '@fluentui/react-components';

import type { WizardHostProps } from './wizardRegistry';

const PlaceholderWizard: React.FC<WizardHostProps> = ({ open, onClose }) => {
  return (
    <Dialog open={open} onOpenChange={(_, data) => !data.open && onClose()}>
      <DialogSurface>
        <DialogBody>
          <DialogTitle>Wizard not yet available</DialogTitle>
          <DialogContent>
            <Text>This create wizard has not shipped yet. Please check back later.</Text>
          </DialogContent>
          <DialogActions>
            <Button appearance="primary" onClick={onClose}>
              Close
            </Button>
          </DialogActions>
        </DialogBody>
      </DialogSurface>
    </Dialog>
  );
};

export default PlaceholderWizard;
