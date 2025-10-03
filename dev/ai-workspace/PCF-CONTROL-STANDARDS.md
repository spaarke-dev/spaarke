# Expert Guide: Building a Power Platform PCF Dataset Control with React and TypeScript

## Overview

This knowledge article instructs an AI-driven developer agent in the correct, modern, and maintainable approach to building a Power Platform PCF (Power Apps Component Framework) control for datasets, using React, TypeScript, and following platform, performance, and UX best practices.

***

## Prerequisites

- Power Platform CLI (`pac`) and Node.js LTS installed.
- Familiarity with React, TypeScript, and Power Platform pro-code concepts.
- Access to a Power Apps environment for deployment and testing.
- Understanding of PCF project structure and the manifest (`ControlManifest.Input.xml`) file.

***

## High-Level Steps

1. **Project Setup**
2. **Manifest Design and Configuration**
3. **Implement React with PCF Hooks**
4. **Dataset API Integration**
5. **State, Loading, and Events Handling**
6. **Responsiveness and Accessibility**
7. **Lifecycle Management**
8. **Localization and Resource Files**
9. **Testing and Deployment**

***

## 1. Project Setup

- Initialize the project:

```
pac pcf init --namespace VibeAgent --name DataSetControl --template dataset
npm install
```

- Add React and TypeScript types:

```
npm install react react-dom @types/react @types/react-dom
```


***

## 2. Manifest Design and Configuration

- Define all properties, datasets, and resources in `ControlManifest.Input.xml`.
- Use descriptive `display-name-key` and `description-key` attributes for each property and control.[^5]
- Specify React and stylesheet resource files.

***

## 3. Implement React with PCF Hooks

- Create a root React component (`DataSetControlComponent.tsx`).
- Use context and hooks to manage state and props.
- Render dataset records, support custom cell renderers, and leverage Fluent UI for coherent UX.[^11][^13]

***

## 4. Dataset API Integration

- Use the dataset object provided on context to:
    - Retrieve, paginate, sort, and filter records natively.[^12][^14]
    - Implement shimmer/loader controls for asynchronous data states.
- Do not directly manipulate form data—respect the decoupling of your component from data context.

***

## 5. State, Loading, and Events Handling

- Show fluent shimmer/loading indicators during async fetches.
- Provide clear and instructive zero-data/empty states.
- Implement event handlers for record selection, pagination, and row/cell actions.
- Avoid storing unnecessary state in the component; derive rendering from props and dataset only.

***

## 6. Responsiveness and Accessibility

- Use CSS-in-JS or module CSS for modular, encapsulated styles.
- Ensure the control renders at 100% of allocated container size.
- Support both touch and keyboard navigation.
- Include `aria-*` attributes, roles, and announce state changes for screen reader users.[^8][^12]

***

## 7. Lifecycle Management

- Initialize subscriptions/resource-heavy logic in the `init` method.
- Clean up event listeners, timers, or custom resources in `destroy`.
- Use the component’s update lifecycle (`updateView`) to efficiently propagate changes to the React component.

***

## 8. Localization and Resource Files

- Store textual strings in resource files.
- Dynamically select translations based on the environment locale.
- Never hard-code UI text or accessibility labels.

***

## 9. Testing and Deployment

- Write unit tests for core logic and React components.
- Test with the Power Platform test harness:

```
npm start
```

- Build and package only from the source directory; keep `output` and `generated` folders build artifacts only.
- Import final solutions via Dataverse for deployment; always version controls and solutions.

***

## Best Practices Summary

- Use React + TypeScript for modular, maintainable, and scalable components.
- Employ Fluent UI for consistent interface design.
- Isolate data logic from view rendering.
- Always respect accessibility, localization, and responsive design.
- Clean up all resources in destroy for memory safety.
- Validate all incoming dataset props for robustness.
- Never make direct writes to form context in dataset PCFs.
- Thoroughly document your code for human and AI maintainers.

***

## References

- [Power Apps Code Components Best Practices]
- [PCF Dataset Control Tutorials
- [Best Practices for Developing PCF Controls]
- [Step by Step Guide to Developing PCF Controls]

***
