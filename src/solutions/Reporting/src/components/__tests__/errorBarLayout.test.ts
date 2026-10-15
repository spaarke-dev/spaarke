/**
 * The long error messages these components show (the frame plus the server's detail) must wrap.
 * `<MessageBar layout="singleline">` keeps `white-space: nowrap` and clips them, so every error bar
 * here has to be multiline. Source-level guard: rendering each component needs its whole provider tree.
 */
import * as fs from 'fs';
import * as path from 'path';

describe.each(['SaveControls.tsx', 'NewReportButton.tsx', 'ExportButton.tsx', 'DeleteReportButton.tsx'])(
  '%s error bar',
  file => {
    const src = fs.readFileSync(path.join(__dirname, '..', file), 'utf8');

    it('uses a multiline MessageBar for errors', () => {
      expect(src).toMatch(/<MessageBar intent="error" layout="multiline"/);
      expect(src).not.toMatch(/layout="singleline"/);
    });
  }
);
