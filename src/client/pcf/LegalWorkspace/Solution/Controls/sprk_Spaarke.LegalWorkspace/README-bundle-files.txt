Build artifacts required in this directory (copied from out/controls/control/ after `npm run build`):

  bundle.js          - Compiled + minified PCF bundle
  ControlManifest.xml - Already present (generated version, must match version 1.0.1)
  styles.css         - Compiled Griffel/Fluent CSS

To populate these files, run Package-LegalWorkspace.ps1 from the scripts/ directory:
  scripts/Package-LegalWorkspace.ps1

Or manually:
  cd src/client/pcf/LegalWorkspace
  npm run build
  copy out\controls\control\bundle.js     Solution\Controls\sprk_Spaarke.Controls.LegalWorkspace\
  copy out\controls\control\styles.css    Solution\Controls\sprk_Spaarke.Controls.LegalWorkspace\
  copy out\controls\control\ControlManifest.xml Solution\Controls\sprk_Spaarke.Controls.LegalWorkspace\

After copying, run Solution/pack.ps1 to create the importable ZIP.
