# UniverseLib (Unity Game Translator Fork)

> **This is a fork of [UniverseLib](https://github.com/sinai-dev/UniverseLib) with specific adaptations for [Unity Game Translator](https://github.com/djethino/UnityGameTranslator).**

UniverseLib is a library for making plugins which target IL2CPP and Mono Unity games, with a focus on UI-driven plugins.

## Fork Changes

This fork includes the following modifications for Unity Game Translator:

### UI Improvements
- **Modern color theme**: Slate blue tints with purple accents matching the UGT website
- **Cleaner panels**: Removed thick black borders for a more modern look
- **Improved title bar**: Slimmer design (28px) with proper padding
- **Modern close button**: Uses "✕" with red hover effect
- **Updated component styling**: Toggles, dropdowns, inputs, scrollviews with cohesive colors

### New Widgets
- **HoverEffect**: IL2CPP-compatible hover color effect using `IPointerEnterHandler`/`IPointerExitHandler`
- **DynamicScrollbar**: Auto-hide scrollbar when content fits in viewport

### New API
- `UIFactory.Colors`: Centralized theme colors (backgrounds, accents, status colors)
- `UIFactory.AddHoverEffect()`: Add hover effects to UI elements
- `UIFactory.ConfigureAutoHideScrollbar()`: Configure auto-hiding scrollbars

## Original Documentation

Documentation and usage guides can be found on the [original Wiki](https://github.com/sinai-dev/UniverseLib/wiki).

## Original NuGet Packages

For the original UniverseLib packages, see:

[![](https://img.shields.io/nuget/v/UniverseLib.Mono?label=UniverseLib.Mono)](https://www.nuget.org/packages/UniverseLib.Mono)
[![](https://img.shields.io/nuget/v/UniverseLib.IL2CPP.Interop?label=UniverseLib.IL2CPP.Interop)](https://www.nuget.org/packages/UniverseLib.IL2CPP.Interop)

## Acknowledgements

* [sinai-dev](https://github.com/sinai-dev) for the original [UniverseLib](https://github.com/sinai-dev/UniverseLib)
* [yukieiji](https://github.com/yukieiji) for their [fork](https://github.com/yukieiji/UniverseLib) with additional improvements
* [Geoffrey Horsington](https://github.com/ghorsington) and [BepInEx](https://github.com/BepInEx) for [ManagedIl2CppEnumerator](https://github.com/BepInEx/BepInEx/blob/master/BepInEx.IL2CPP/Utils/Collections/Il2CppManagedEnumerator.cs) \[[license](https://github.com/BepInEx/BepInEx/blob/master/LICENSE)\], included for IL2CPP coroutine support.
