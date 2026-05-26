---
source: https://fluent2.microsoft.design/get-started/develop
fetched: 2026-05-26
summary: Cross-platform install guides — React (Fluent v9 + Griffel), Web Components (FAST), iOS (Swift), Android (Kotlin), WinUI. React section duplicates quickstart.md.
loadWhen: bringing Fluent 2 to a non-React surface (iOS/Android/WinUI). Not needed for routine Spaarke React work.
---

# Fluent 2 Design System — Develop

Fluent 2 provides a seamless maker experience from design to development to delivery. Dive into the Fluent UI code libraries with these easy steps.

## Pick your platform

Fluent's cohesive design language is carried through each platform-specific Fluent UI library to ensure you can build the same great experiences across one platform or across them all.

- React
- Web Components
- iOS
- Android
- Windows (via WinUI)

---

## React

### Tooling and requirements

To build Fluent 2 experiences on React, you'll need **Fluent UI React v9**. Fluent UI React is built on React and TypeScript. The components are styled using CSS-in-JS — we use **Griffel** to render styles and insert CSS into the DOM when needed.

You'll need node.js and a package manager like yarn to build and run apps using v9.

### Migrating to Fluent UI React v9

Migrating from an older version of Fluent UI React? Check out the Migration topic in Storybook.

### Installing Fluent UI React v9

```sh
npm install @fluentui/react-components
# or
yarn add @fluentui/react-components
```

### Setting up your app

1. Import a `FluentProvider` and theme at the root.
2. Pass the theme as a prop of the `FluentProvider`. This defines your app-level settings.
3. Import any other v9 components you'll need to render within the `FluentProvider`.

```tsx
import {
  FluentProvider,
  webLightTheme,
  Button,
} from "@fluentui/react-components";

export default function App() {
  return (
    <FluentProvider theme={webLightTheme}>
      <Button appearance="primary">Hello Fluent UI React</Button>
    </FluentProvider>
  );
}
```

### Ready, set, make

You're all set. Be sure to read the component documentation to create fully accessible and delightful experiences with Fluent.

---

## Web Components

### Tooling and requirements

Fluent UI Web Components is built on **FAST Element** and TypeScript, and distributed as JavaScript modules. If you are not using the CDN distribution, you'll need node.js and a package manager like NPM, Yarn, or PNPM.

### Installing Fluent UI Web Components

```sh
yarn add @fluentui/web-components
# or
npm i @fluentui/web-components
# or
pnpm add @fluentui/web-components
```

### Setup

Fluent UI Web Components are styled using tokens in the form of CSS variables. You can use the `setTheme` utility to provide a theme for your website or application.

```ts
import { setTheme } from '@fluentui/web-components';
import { webLightTheme } from '@fluentui/tokens';

setTheme(webLightTheme);
```

### Defining the element yourself using named imports

```ts
import { ButtonDefinition, FluentDesignSystem } from '@fluentui/web-components';

ButtonDefinition.define(FluentDesignSystem.registry);
```

---

## iOS

### Tooling and requirements

- iOS 14 or later or MacOS 10.15 or later
- Xcode 14.1 or later
- Swift 5.7.1 or later

### Migration

With each release of Fluent UI Apple, more components align to the Fluent 2 design language. Once a component is tokenized to align to Fluent 2, the Fluent 1 version is no longer available. Check the release notes; to stick with the Fluent 1 version of an upgraded component, take the release before that component was tokenized.

### Installing (Swift Package Manager)

```swift
dependencies: [
  .package(url: "https://github.com/microsoft/fluentui-apple.git",
           .upToNextMinor(from: "X.X.X")),
]
```

### Installing (CocoaPods)

```ruby
pod 'MicrosoftFluentUI', '~> X.X.X'
```

### Importing

```swift
import FluentUI
```

```objc
#import <FluentUI/FluentUI-Swift.h>
```

---

## Android

### Tooling and requirements

- Android API 21 or later
- Android CompileSDK 33
- Kotlin 1.8.21
- Jetpack Compose BOM 2023.06.01
- Jetpack Compose Compiler 1.4.7
- Surface Duo SDK 1.0.0-alpha01 (optional)
- Android Studio Flamingo

### Installing via Gradle

The library is published on MavenCentral; add `mavenCentral()` to the project-level `build.gradle` for v0.0.17+.

```gradle
dependencies {
    implementation 'com.microsoft.fluentui:FluentUIAndroid:$version'
}
```

Individual modules can be consumed since v0.0.12, e.g. `fluentui_drawer`.

### Importing

```kotlin
import com.microsoft.fluentui.persona.AvatarView
```

```xml
<com.microsoft.fluentui.persona.AvatarView
    android:layout_width="wrap_content"
    android:layout_height="wrap_content"
    app:name="Mona Kane" />
```

---

## Fluent in WinUI

To get the building blocks for crafting Windows experiences, use **WinUI**. These components incorporate Fluent's design language. See [WinUI 3 documentation](https://learn.microsoft.com/en-us/windows/apps/winui/winui3/).
