---
source: |
  https://github.com/microsoft/fluentui/blob/master/apps/public-docsite-v9/src/Concepts/Accessibility/AccessibleComponents.mdx
  https://github.com/microsoft/fluentui/blob/master/apps/public-docsite-v9/src/Concepts/Accessibility/AccessibleExperiences.mdx
upstream_commit: 0aa62de59fe5845eeba40c9028d527fd93d88f27
fetched: 2026-05-26
summary: §1 component-level accessibility (WCAG 2.1, ARIA, tabster, axe-core testing); §2 application-level checklist (landmarks, focus moves, focus traps, high contrast).
loadWhen: any UI feature with user interaction OR accessibility audit. §2 checklist is the operational lift; §1 is contextual.
notes: |
  Concatenates two related concept pages: "Components Overview" and "Experiences".
  Storybook URLs (JS-rendered):
    https://react.fluentui.dev/?path=/docs/concepts-developer-accessibility-components-overview--docs
    https://react.fluentui.dev/?path=/docs/concepts-developer-accessibility-experiences--docs
---

# Accessibility

## §1 — Components accessibility (overview)

Fluent UI components are designed to support various aspects of accessibility by default, so that they can be used with different input methods (mouse, touch, keyboard, screen readers) as well as fulfill different rendering and layout requirements (theming, zoom, contrast).

The [Web Content Accessibility Guidelines (WCAG)](https://www.w3.org/TR/WCAG21/) and [WAI-ARIA Authoring Practices](https://www.w3.org/TR/wai-aria-practices-1.2/) are community-driven standards for accessible web pages and applications. Fluent UI components aim to fully respect both WCAG and ARIA practices.

> Using components themselves does not guarantee that an application or a page will be accessible. See **§2** for further points on achieving good usability.

### Scope of what Fluent UI components provide

- **DOM structure** that provides **semantic value** either by using correct element types or roles
- **ARIA attributes** that are valid for elements/roles used and provide correct state information about the component or element
- **Keyboard navigation** (tabbing, arrow keys, pagination or letter keys, click and right click (Enter/Space, Shift+F10), and close (Escape)) applied based on the component
- **Screen reader navigation** (Virtual Cursor / Browse mode / Scan mode / VoiceOver keys)
- **Touch interaction**
- **Focus handling** when the component can move focus in a predictable way — mostly when opening menus, popups, or dialogs (autofocus) or dismissing them with Esc. **Focus trap** for `Dialog` and popups.
- **Sufficient color contrast**
- Light, Dark, and High Contrast **themes**
- Displaying a **focus indicator** when keyboard is used to interact with components
- **Tested** against visual inconsistencies/bugs on zoom up to 400%

Fluent UI components use [tabster](https://github.com/microsoft/tabster) for focus handling, so they can be easily integrated with application-level tabster functionality (deloser, cross-iframe focusing).

### Out of scope

- **Internationalization, globalization, keyboard shortcuts, and language detection** are deliberately not part of Fluent UI and should be handled by the hosting application.
- **App-level focus handling** (beyond what's listed in Scope) needs to be handled by the application, preferably using `tabster`.

### Ensuring accessibility — testing

- [`axe-core`](https://github.com/dequelabs/axe-core) is used to validate individual components during development and build time.
- **Manual tests** are executed on small isolated pages which show different accessibility scenarios. For each component, suitable scenarios are defined and implemented. They are tested by experienced trusted accessibility testers and real users. Axe-core tests are executed on each scenario.

---

## §2 — Creating an accessible web application or page

The easiest way to make an application or page accessible is to maintain a consistent and compliant document structure.

- Decompose UI to parts and **identify components, variants, and behaviors to use**
- Define the usage of **[headings and landmarks](https://www.w3.org/TR/wai-aria-practices/examples/landmarks/index.html)**
- Verify the usage of **[color and contrast](https://webaim.org/resources/contrastchecker/)** to convey information
- Ensure **[tab order](https://www.w3.org/TR/UNDERSTANDING-WCAG20/navigation-mechanisms-focus-order.html)** and **[arrow key navigation](https://dequeuniversity.com/tips/aria-keyboard-patterns)** is consistent with DOM structure and ARIA roles
- Specify **labels**, especially for components without textual information (e.g. icon-only buttons) and for containers (lists, toolbars, …)
- Specify texts for **[state change announcements](https://www.w3.org/WAI/WCAG21/Understanding/status-messages)** ([error messages](https://www.w3.org/WAI/WCAG21/Techniques/aria/ARIA19), confirmations, dynamic UI changes, …)
- Identify UI parts that **[appear on hover or focus](https://www.w3.org/WAI/WCAG21/Understanding/content-on-hover-or-focus.html)** then specify keyboard and screen reader interaction with them. **Note**: screen readers and (in most cases) touch devices do not support hover state.
- List cases when **focus** needs to be **moved programmatically** (parts of the UI appearing/disappearing)
- List cases when **focus** needs to be **trapped** in sections of the UI (for dialogs, popups, hierarchical navigation)
- When extending existing functionality, think about how it fits into the current experience with regards to **discoverability, interaction, keyboard navigation, and screen reader navigation**
- In your designs, cover **[High contrast](https://blogs.windows.com/msedgedev/2020/09/17/styling-for-windows-high-contrast-with-new-standards-for-forced-colors/)** and **[Zoom and reflow](https://www.w3.org/WAI/WCAG21/Understanding/reflow.html)** scenarios
