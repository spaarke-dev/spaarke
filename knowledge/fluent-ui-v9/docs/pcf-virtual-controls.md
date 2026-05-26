---
source: https://www.microsoft.com/en-us/power-platform/blog/power-apps/virtual-code-components-for-power-apps-using-react-and-fluent-ui-react-platform-libraries/
fetched: 2026-05-26
summary: Public-preview announcement of virtual PCFs (`pac pcf init -fw react`) with platform-provided React + Fluent libraries. 98% smaller bundles. Background for the modern theming story.
loadWhen: orientation only — background context for why platform-library PCFs exist. Operational setup covered by patterns/pcf/fluent-v9-modern-theming.md.
notes: |
  Original blog post is JS-rendered; WebFetch produced a semi-summarized capture. Verify before quoting verbatim.
  Author: Hemant Gaur. Published April 7, 2022 (announcement of Public Preview).
  Original URL redirected from powerapps.microsoft.com → microsoft.com/en-us/power-platform.
---

# React (virtual) and Fluent UI code components using platform libraries

**Author**: Hemant Gaur · **Published**: April 7, 2022 (Public Preview announcement)

## What this introduces

Microsoft announced public preview availability of **React-based virtual code components** for Power Apps. This feature allows developers to build PCF components using **platform-provided React and Fluent UI libraries** instead of bundling them individually inside each component.

> "React-based virtual code components are finally here." The update eliminates the need for developers to package React or Fluent libraries within individual component bundles, ensuring consistent control styles across applications and preventing isolated component React trees.

## Why it matters

Traditional Power Apps components (standard controls) receive an `HtmlDivElement` during initialization and mount UI directly to the DOM. **React virtual controls attach to the platform's React tree instead**, leveraging React's virtual DOM for improved performance. The post claims performance gains "at par with some of the 1st-party controls."

## Creating React (virtual) components

Developers create components using a new optional `--framework` (`-fw`) parameter in the Power Apps Component CLI (PAC CLI):

```sh
pac pcf init --framework react
```

This generates a Hello World virtual control with:

- Control type specified as `virtual` in the manifest
- React and Fluent platform library resource declarations
- Local testing capability via `npm start` without requiring a Dataverse solution

## Fluent Design System

Fluent Design System is "an open-source, cross-platform design system that gives designers and developers the frameworks they need to create engaging product experiences." Originally released for Power Apps Teams and Custom pages, Fluent provides design alignment across Microsoft products and infrastructure for theming.

## Creating virtual Fluent components

Creating virtual Fluent components follows the same process as standard virtual controls. The `--framework` parameter automatically adds shared library resource definitions for both React and Fluent to the component manifest. Developers can remove the Fluent node if unnecessary for their project.

## Sample components updated for this model

- **Choice Picker Component** — updated React virtual control using platform libraries
- **React FacePile Component** — demonstrates functionality with platform library integration

## Performance metrics cited

Virtual controls demonstrate significant improvements over standard versions of the same components:

- **98% smaller bundle size**
- **Faster load times** across web and mobile
- **Enhanced performance on slower networks**

The FacePile control demonstrated substantial gains when converted to virtual component architecture.

## Forward path

At general availability (GA), React and Fluent UI will become "the recommended and default way to create all code components." During preview, developers should evaluate React + Fluent controls while maintaining standard control library packaging for production use.

> "Post GA, migrating to virtual controls should be simple as the core client development stack is the same."

## Resources

- [Preview Documentation — React controls & platform libraries](https://docs.microsoft.com/power-apps/developer/component-framework/react-controls-platform-libraries)
- [Power Apps Pro Dev Forum](https://aka.ms/PCFForum)
- Companion: [pcf-modern-theming.md](./pcf-modern-theming.md) (how to consume modern theme tokens once virtual)
- Companion sample: [`samples/PowerApps-Samples_FluentThemingAPIControl/`](../samples/PowerApps-Samples_FluentThemingAPIControl/)
