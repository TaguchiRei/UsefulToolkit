# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this repo is

This is a Unity project (Editor version pinned in `ProjectSettings/ProjectVersion.txt`, currently `6000.3.10f1` / Unity 6) that doubles as the source repo for **"Useful Toolkit"**, a multi-package UPM (Unity Package Manager) library of game-dev productivity tools authored by Rei. The `Assets/` folder is a throwaway sandbox scene for manually exercising the packages; the actual product is everything under `Packages/com.rei.usefultoolkit.*`. The public source lives at `https://github.com/TaguchiRei/UsefulToolkit.git` (referenced by the in-editor installer — see below), and packages are consumed either via that git URL (`?path=Packages/<name>`) or as local `file:` references (see `Packages/manifest.json`).

There is no npm/CMake/etc. build system — everything is driven through the Unity Editor.

## Commands

There is no CLI build or test pipeline in this repo. All workflows go through the Unity Editor:

- **Open the project**: open this folder with Unity Hub / Unity Editor `6000.3.10f1` (or compatible 6000.x).
- **Compile**: happens automatically when the Editor is open and focused, or via `Assets > Reimport All` / just opening the project. There's no headless build script checked in.
- **Tests**: `com.unity.test-framework` is a project dependency, but no test assembly (`*.Tests.asmdef`) currently exists in any package. If you add tests, run them from `Window > General > Test Runner` in the Editor; there is no CLI test runner configured in this repo.
- **Custom in-editor tools** (menu items, all under the `UsefulToolkit` top menu):
  - `UsefulToolkit/Installer` — opens `UsefulToolkitInstaller` (`Packages/com.rei.usefultoolkit.framework/Editor/Setting/UsefulToolkitInstaller.cs`), a UPM installer window that lets a consumer pick which sub-packages to add via `Client.Add(gitUrl)` against the packages listed above. If you add a new sub-package, add it to `_packages` in that file too. Each `PackageInfo` entry can also declare `RequiredDependencies` — full external UPM identifiers (e.g. git URLs) that get queued automatically whenever that entry (or, for the required `Framework` entry, whenever any package at all) is installed, since UPM does not auto-resolve git-based transitive dependencies from `package.json`. `Framework` currently declares UniTask (`com.cysharp.unitask`) this way, because `UsefulToolkit.Framework.Runtime.asmdef` references it directly but it's otherwise undeclared anywhere consumers would see it.
  - `UsefulToolkit/Settings` — opens `UsefulToolkitSettings`, a tabbed settings window whose tabs are auto-discovered via reflection (see Architecture below).
  - `UsefulToolkit/AI/*` — `aitool`-package windows: `AiChat` (chat UI), `Generate Default Agents`, and `Unity CLI Loop Installer` (`Packages/com.rei.usefultoolkit.aitool/Editor/CliLoop/UnityCliLoopInstaller.cs`), which automates adding [hatayama/unity-cli-loop](https://github.com/hatayama/unity-cli-loop) (formerly uLoopMCP — a third-party MCP bridge that lets AI agents drive the Unity Editor) via `Client.Add`, running its `uloop-cli` npm CLI and Skills install (`npm install -g uloop-cli`, `uloop skills install --claude|--codex`) through a redirected `cmd.exe` child process, and jumping to that package's own `Window/Unity CLI Loop/Settings` for MCP client config (which this repo intentionally doesn't try to replicate/automate, since it's undocumented and package-version-specific).

## Architecture

### Package layout

Every distributable feature lives under `Packages/com.rei.usefultoolkit.<name>/`, each with its own `package.json`, and `Runtime/` and/or `Editor/` folders. Packages present today:

- `framework` — the core/common package; almost everything else depends on it. No dependencies of its own.
- `architecture` — composition-root / ordered-initialization base classes (`Initialize/`), depends on `framework`.
- `aitool` — Editor-only in-editor AI assistant (chat window, agent/tool commands for Assets/Scene/Inspector/File/Debug), depends on `framework`. **Note:** unlike the other packages, `aitool` is not listed in this project's own `Packages/manifest.json` — it's developed here but not currently consumed by the sandbox project itself.
- `debugging`, `gitsupport`, `networking`, `programtools`, `qualitycontroltools`, `soundarttools`, `staticdatatools`, `visualarttools` — each a thin, mostly-scaffolded shell (asmdefs + `package.json` only, little/no code yet) that depends on `framework`.

`Packages/manifest.json` is the authoritative dependency list for the sandbox project (uses `file:` refs for local packages); `package.json` inside each package is what a real consumer's UPM would resolve when installing via the git-URL path.

### Assembly definition boundaries

Unity draws assembly boundaries at whichever folder has an `.asmdef`. The convention in this repo is one asmdef per `Runtime` and per `Editor` folder per package (e.g. `UsefulToolkit.Framework.Runtime`, `UsefulToolkit.Framework.Editor`), with `rootNamespace` matching the C# namespace used inside. A subsystem that needs to compile as its own assembly (e.g. a State-Centrism layer, see below) gets its own nested asmdef instead of relying on the package's top-level one — check for a closer asmdef before assuming which assembly a new file lands in.

Naming convention for every asmdef in this repo: assembly name is `UsefulToolkit.<X>.Runtime` (or `.Editor`), and `rootNamespace` is the same string with the trailing `.Runtime`/`.Editor` dropped (e.g. `UsefulToolkit.Framework.Runtime` → rootNamespace `UsefulToolkit.Framework`). A nested asmdef for a State-Centrism layer instead **appends** the layer name after `.Runtime` — `UsefulToolkit.<Package>.Runtime.<Layer>`, rootNamespace `UsefulToolkit.<Package>.<Layer>` — with a trailing "Layer" stripped from `<Layer>` (`BlackBoardLayer` → `BlackBoard`, `EngineServiceLayer` → `EngineService`, `ExternalLayer` → `External`; `Application` and `Initialization` have no suffix to strip). This keeps every layer asmdef's name rooted at its owning package instead of standing alone.

`framework`'s `Runtime/` now has one nested asmdef per State-Centrism layer folder, each wired to reference only what the layer table (below) allows it to reference:

| Asmdef | Folder | References |
|---|---|---|
| `UsefulToolkit.Framework.Runtime` | `Runtime/` (now just `Attributes/`, since every layer folder below carves itself out via its own nested asmdef) | *(none)* |
| `UsefulToolkit.Framework.Runtime.BlackBoard` | `Runtime/BlackBoardLayer/` | UniTask (`GUID:f51ebe6a0ceec4240a699833d6309b23`) — needed for `Func<T[], IProgress<float>, UniTask>` in `Scene/ISceneChangeEvent.cs`/`SceneChangeBoard.cs` |
| `UsefulToolkit.Framework.Runtime.Application` | `Runtime/Application/` | `...Runtime.BlackBoard`, `...Runtime.External`, UniTask (same GUID — used by `SceneFlowController<T>`) |
| `UsefulToolkit.Framework.Runtime.External` | `Runtime/ExternalLayer/` | *(none)* |
| `UsefulToolkit.Framework.Runtime.EngineService` | `Runtime/EngineServiceLayer/` | `...Runtime.BlackBoard`, UniTask (`GUID:f51ebe6a0ceec4240a699833d6309b23`) |
| `UsefulToolkit.Framework.Runtime.Initialization` | `Runtime/Initialization/` (empty stub) | all four of the above (Initialization is the only layer allowed to see every other layer) |

Note: `UsefulToolkit.Framework.Runtime.EngineService` deliberately does **not** reference `...Runtime.Application` — `architecture` (which owns `InitializableMonoBehaviour`/`InitializerBase`) already depends on `framework`, so `framework` referencing anything back that would imply an architecture-style base class would be circular. EngineServiceLayer classes that don't need Inspector-serialized fields or an `Update` loop (e.g. `SceneLoadService<T>`) are plain C# classes for this reason, not `MonoBehaviour`s — mirroring the (now-superseded) plain-class `SceneService`.

The `architecture` package (`UsefulToolkit.Architecture.Runtime`) is the toolkit's actual cross-package Initialization-layer implementation (see below) and, for the same "Initialization refers to all layers" reason, references `UsefulToolkit.Framework.Runtime` (for `Attributes`) plus all four framework layer asmdefs above — update that reference list too if a new framework layer asmdef is added. `input` references `UsefulToolkit.Framework.Runtime.BlackBoard` directly (it needs `BlackBoardLayer` only) but otherwise keeps its own `BlackBoardLayer`/`EngineServiceLayer` folders inside a single package-wide asmdef rather than splitting further — only split a package's own Runtime asmdef along layer lines once it actually needs the compile-time boundary.

### State-Centrism Architecture (in-progress migration)

The `framework` and `architecture` packages are being restructured to follow a "State-Centrism" design: runtime `State` is the single source of truth, organized into layers — Initialization / Application / BlackBoardLayer / ExternalLayer / EngineServiceLayer. This is a live migration, not a finished design.

**Folder convention**: layer folders live directly under each package's `Runtime/`/`Editor/` (never nested inside a feature folder), and each layer folder contains one subfolder per feature it hosts — i.e. `UsefulToolkit.<package>/Runtime/<Layer>/<Feature>/...`, layer name first, feature name second. The `input` package follows this end-to-end (`Runtime/BlackBoardLayer/`, `Runtime/EngineServiceLayer/`) and is the cleanest reference example.

- `framework`'s top-level `Runtime/` layer folders are `Initialization`, `Application`, `BlackBoardLayer`, `ExternalLayer`, `EngineServiceLayer`. `Initialization` currently exists as an empty stub.
- `BlackBoardLayer` (`Runtime/BlackBoardLayer/`) has two feature folders:
  - `BlackBoardLayer/BlackBoard/` — the generic mediator primitives: `Board/BlackBoard.cs`, `Board/ChildStateBoardBase.cs`, `Board/ChildEventBoardBase.cs`, `Interface/IBlackBoard.cs`, and the `State/` types (`StateBase`, `StateContext<T>`, `StateLifeScope`, `SceneStateBase`, `GameStateBase`, `UnRegistableStateBase`) under namespace `UsefulToolkit.Application.StateManagement`. `ChildStateBoardBase`/`ChildEventBoardBase` are separate base classes with separate storage in `BlackBoard` (`TryRegister/GetStateChildBoard` vs `TryRegister/GetEventChildBoard`) — a ChildBoard is either State-backed or Event-backed, never both, matching the "State and Event are separate channels" rule.
  - `BlackBoardLayer/Scene/` — `SceneBoard` (the `ChildStateBoard` that `SceneState` registers into) plus `ISceneChangeEvent<T>`/`SceneChangeBoard<T>` (the `ChildEventBoard` side: `SceneLoadService<T>` registers its load method via `RegisterSceneLoader`, and `SceneFlowController<T>` triggers it via `RequestTransitionAsync` — mirrors the `IEventChannel`/`EventChannel` Register-vs-Publish split, just at the Board level). Namespace `UsefulToolkit.Framework`.
  - The whole `BlackBoardLayer/` folder — both feature subfolders — compiles as one nested asmdef (`UsefulToolkit.Framework.Runtime.BlackBoard`), separate from the package's top-level `UsefulToolkit.Framework.Runtime` asmdef (which references it); see Assembly definition boundaries below.
- `Application` (`Runtime/Application/`) holds the Entity/Usecase side of scene control: `SceneState<T>`/`ISceneStateGetter<T>` (current/next scene node, per-node Loaded/Unloaded action registration; `SetCurrentNodeId` is `internal` since its only caller, `SceneFlowController<T>`, lives in the same assembly — Single Writer enforced by the compiler, not just convention) and `SceneFlowController<T>` (the Usecase that game code actually calls: creates+registers `SceneState<T>` into `SceneBoard` and a `SceneChangeBoard<T>` into `IBlackBoard`, exposes `TransitionTo(nodeId, groupId)`/`GetCurrentNode()`/`GetNextNodeIds()`, and implements `IProgress<float>` to receive load progress directly).
- `ExternalLayer` (`Runtime/ExternalLayer/`) holds the scene-flow-graph data types under `ExternalLayer/Scene/`: `SceneFlowBase<T>` (ScriptableObject holding `SceneNode<T>[]`), `SceneNode<T>` (holds multiple `SceneGroupBase<T>` — same node, alternate scene combinations — plus `NextScenes` adjacency), and `SceneGroupBase<T>`/`SceneGroup<T>`/`FlexibleSceneGroup<T>` (Lighting/Content/Logic scene triple, either fixed or with an additional variable list; both expose the combined set via the `Scenes` property).
- `EngineServiceLayer` (`Runtime/EngineServiceLayer/`) has one feature folder, `EngineServiceLayer/Scene/`: `SceneLoadService<T>`, the sole class touching `UnityEngine.SceneManagement.SceneManager`. It never uses `LoadSceneMode.Single` (that would unload scenes it doesn't manage, like a persistent System/Boot scene) — instead it diffs the currently-loaded scene set against the requested one and only loads/unloads the difference. It's a plain class (not a `MonoBehaviour`) since it needs neither Inspector fields nor an `Update` loop; see the asmdef table's note on why it can't depend on `architecture`'s `InitializableMonoBehaviour` anyway.
- `StateBase.LifeScope` (a `StateLifeScope` enum: `OnGameEnd` / `OnSceneEnd` / `Other`) governs how/when a state gets disposed — see `BlackBoardLayer/BlackBoard/Board/StateDispose.cs`.
- The `architecture` package's `Initialize/` folder (`CompositionBase`, `InitializerBase`, `InitializableMonoBehaviour`) implements the ordered-initialization convention: `MonoBehaviour`s that shouldn't run until explicitly initialized start disabled (`enabled = false` in `Awake`), get re-enabled from `Initialize()`, and sort via `IComparable<InitializableMonoBehaviour>.InitializationOrder`. Both currently run at `[DefaultExecutionOrder(100)]`.

Before writing or reviewing code in this area, treat this as a genuinely unfinished, actively-changing design — don't assume a folder's current (often empty) contents reflect the intended final shape.

### Editor tooling internals

- Settings pages are discovered via reflection, not hard-wired: `SettingPageProvider` (`Framework/Editor/Setting/`) uses `InstanceCollector`/`TypeCollector` (`Framework/Editor/Reflection/`) to find all `SettingPageBase` subclasses and surfaces them as tabs in `UsefulToolkitSettings`. Adding a new settings tab means subclassing `SettingPageBase` — no registration step needed.
- Custom attributes (`ShowOnly`, `PullDownArray`, `SubclassSelector`, `MethodExecutor`) live in `Framework/Runtime/Attributes/` with matching `PropertyDrawer`/hook implementations in `Framework/Editor/Attributes/`.
- Project-wide code generation (enum/file generation for scenes etc.) is configured through `UsefulToolkitProjectSettings` / `UsefulToolkitSettingsScriptable`, persisted to `ProjectSettings/UsefulToolkitSettings.asset`.

## Conventions

- In-code comments and doc-comments are written in Japanese; match this when editing existing files.
- `UsefulToolkitInstaller`'s package list (`_packages` in `Packages/com.rei.usefultoolkit.framework/Editor/Setting/UsefulToolkitInstaller.cs`) uses `com.rei.usefultoolkit.*` sub-paths that now match the real package folder names 1:1, including `architecture` and `gitsupport`. If you add, rename, or remove a package folder, update this list too — nothing keeps them in sync automatically.
