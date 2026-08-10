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
  - `UsefulToolkit/Installer` — opens `UsefulToolkitInstaller` (`Packages/com.rei.usefultoolkit.framework/Editor/Setting/UsefulToolkitInstaller.cs`), a UPM installer window that lets a consumer pick which sub-packages to add via `Client.Add(gitUrl)` against the packages listed above. If you add a new sub-package, add it to `_packages` in that file too. Each `PackageInfo` entry can also declare `RequiredDependencies` — full external UPM identifiers (e.g. git URLs) that get queued automatically whenever that entry (or, for the required `Framework` entry, whenever any package at all) is installed, since UPM does not auto-resolve git-based transitive dependencies from `package.json`. `Framework` currently declares UniTask (`com.cysharp.unitask`) this way, because several of `framework`'s layer asmdefs (`...Runtime.BlackBoard`, `...Runtime.Application`, `...Runtime.EngineService`) reference it directly but it's otherwise undeclared anywhere consumers would see it.
  - `UsefulToolkit/Settings` — opens `UsefulToolkitSettings`, a tabbed settings window whose tabs are auto-discovered via reflection (see Architecture below).
  - `UsefulToolkit/AI/*` — `aitool`-package windows: `AiChat` (chat UI), `Generate Default Agents`, and `Unity CLI Loop Installer` (`Packages/com.rei.usefultoolkit.aitool/Editor/CliLoop/UnityCliLoopInstaller.cs`), which automates adding [hatayama/unity-cli-loop](https://github.com/hatayama/unity-cli-loop) (formerly uLoopMCP — a third-party MCP bridge that lets AI agents drive the Unity Editor) via `Client.Add`, running its `uloop-cli` npm CLI and Skills install (`npm install -g uloop-cli`, `uloop skills install --claude|--codex`) through a redirected `cmd.exe` child process, and jumping to that package's own `Window/Unity CLI Loop/Settings` for MCP client config (which this repo intentionally doesn't try to replicate/automate, since it's undocumented and package-version-specific).

## Architecture

### Package layout

Every distributable feature lives under `Packages/com.rei.usefultoolkit.<name>/`, each with its own `package.json`, and `Runtime/` and/or `Editor/` folders. Packages present today:

- `framework` — the core/common package; almost everything else depends on it. No dependencies of its own.
- `architecture` — composition-root / ordered-initialization base classes (`Initialize/`), depends on `framework`.
- `aitool` — Editor-only in-editor AI assistant (chat window, agent/tool commands for Assets/Scene/Inspector/File/Debug), depends on `framework`. **Note:** unlike the other packages, `aitool` is not listed in this project's own `Packages/manifest.json` — it's developed here but not currently consumed by the sandbox project itself.
- `input` — InputSystem integration and the cleanest end-to-end State-Centrism example (`Runtime/BlackBoardLayer/`, `Runtime/EngineServiceLayer/`); depends on `framework` (BlackBoard layer only) and `architecture`.
- `worktrack` — Editor-only work-time recorder (session recording, stats, export, its own settings tab). No `Runtime/` folder at all.
- `debugging` (runtime debug GUI, SceneView info board), `gitsupport` (gitignore/branch helpers, Editor-only), `programtools` (`PauseBoard`, `IPausable`, a multi-file search window) — partially implemented, each depends on `framework`.
- `networking`, `qualitycontroltools`, `soundarttools`, `staticdatatools`, `visualarttools` — each a thin, scaffolded shell (asmdefs + `package.json` only, no `.cs` yet) that depends on `framework`.

`Packages/manifest.json` is the authoritative dependency list for the sandbox project (uses `file:` refs for local packages); `package.json` inside each package is what a real consumer's UPM would resolve when installing via the git-URL path.

### Assembly definition boundaries

Unity draws assembly boundaries at whichever folder has an `.asmdef`. The convention in this repo is one asmdef per `Runtime` and per `Editor` folder per package (e.g. `UsefulToolkit.Framework.Runtime`, `UsefulToolkit.Framework.Editor`), with `rootNamespace` matching the C# namespace used inside. A subsystem that needs to compile as its own assembly (e.g. a State-Centrism layer, see below) gets its own nested asmdef instead of relying on the package's top-level one — check for a closer asmdef before assuming which assembly a new file lands in.

Naming convention for every asmdef in this repo: assembly name is `UsefulToolkit.<X>.Runtime` (or `.Editor`), and `rootNamespace` is the same string with the trailing `.Runtime`/`.Editor` dropped (e.g. `UsefulToolkit.Framework.Runtime` → rootNamespace `UsefulToolkit.Framework`). A nested asmdef for a State-Centrism layer instead **appends** the layer name after `.Runtime` — `UsefulToolkit.<Package>.Runtime.<Layer>`, rootNamespace `UsefulToolkit.<Package>.<Layer>` — with a trailing "Layer" stripped from `<Layer>` (`BlackBoardLayer` → `BlackBoard`, `EngineServiceLayer` → `EngineService`, `ExternalLayer` → `External`; `Application` and `Initialization` have no suffix to strip). This keeps every layer asmdef's name rooted at its owning package instead of standing alone.

`framework`'s `Runtime/` now has one nested asmdef per State-Centrism layer folder, each wired to reference only what the layer table (below) allows it to reference:

| Asmdef | Folder | References |
|---|---|---|
| `UsefulToolkit.Framework.Runtime` | `Runtime/` (now just `Attributes/`, since every layer folder below carves itself out via its own nested asmdef) | *(none)* |
| `UsefulToolkit.Framework.Runtime.BlackBoard` | `Runtime/BlackBoardLayer/` | UniTask (`GUID:f51ebe6a0ceec4240a699833d6309b23`) |
| `UsefulToolkit.Framework.Runtime.Application` | `Runtime/Application/` (currently empty — asmdef only) | `...Runtime.BlackBoard`, `...Runtime.External`, UniTask (same GUID) |
| `UsefulToolkit.Framework.Runtime.External` | `Runtime/ExternalLayer/` (currently empty — asmdef only) | *(none)* |
| `UsefulToolkit.Framework.Runtime.EngineService` | `Runtime/EngineServiceLayer/` (currently empty — asmdef only) | `...Runtime.BlackBoard`, UniTask (same GUID) |
| `UsefulToolkit.Framework.Runtime.Initialization` | `Runtime/Initialization/` (empty stub) | all four of the above (Initialization is the only layer allowed to see every other layer) |
| `UsefulToolkit.Utility` | `Runtime/Utility/` (empty, reserved for future use) | *(none)* |

Four of those folders currently hold no `.cs` at all: the scene-management system that used to live in `Application`/`ExternalLayer`/`EngineServiceLayer` was deleted wholesale (see the State-Centrism section below) and is waiting to be rebuilt. **Keep the asmdefs** — they carry the layer boundaries and their UniTask references, which the rebuilt scene system will need again. The UniTask references in `...BlackBoard`/`...Application`/`...EngineService` are unused right now for the same reason.

Two naming exceptions to be aware of: `UsefulToolkit.Utility` does not follow the `UsefulToolkit.<X>.Runtime` pattern (it predates the layer split and is a reserved slot, not a layer), and `Runtime/BlackBoardLayer/Logger/UsefulLogger.cs` is a plain logging utility that happens to live under `BlackBoardLayer/` rather than in `Utility/` — it is not a BlackBoard feature.

Note: `UsefulToolkit.Framework.Runtime.EngineService` deliberately does **not** reference `...Runtime.Application` — `architecture` (which owns `InitializableMonoBehaviour`/`InitializerBase`) already depends on `framework`, so `framework` referencing anything back that would imply an architecture-style base class would be circular. EngineServiceLayer classes that need neither Inspector-serialized fields nor an `Update` loop are therefore written as plain C# classes, not `MonoBehaviour`s.

The `architecture` package (`UsefulToolkit.Architecture.Runtime`) is the toolkit's actual cross-package Initialization-layer implementation (see below) and, for the same "Initialization refers to all layers" reason, references `UsefulToolkit.Framework.Runtime` (for `Attributes`) plus all four framework layer asmdefs above — update that reference list too if a new framework layer asmdef is added. `input` references `UsefulToolkit.Framework.Runtime.BlackBoard` (it needs `BlackBoardLayer` only) plus `UsefulToolkit.Architecture.Runtime`, but otherwise keeps its own `BlackBoardLayer`/`EngineServiceLayer` folders inside a single package-wide asmdef rather than splitting further — only split a package's own Runtime asmdef along layer lines once it actually needs the compile-time boundary.

### State-Centrism Architecture (in-progress migration)

The `framework` and `architecture` packages are being restructured to follow a "State-Centrism" design: runtime `State` is the single source of truth, organized into layers — Initialization / Application / BlackBoardLayer / ExternalLayer / EngineServiceLayer. This is a live migration, not a finished design.

**Folder convention**: layer folders live directly under each package's `Runtime/`/`Editor/` (never nested inside a feature folder), and each layer folder contains one subfolder per feature it hosts — i.e. `UsefulToolkit.<package>/Runtime/<Layer>/<Feature>/...`, layer name first, feature name second. The `input` package follows this end-to-end (`Runtime/BlackBoardLayer/`, `Runtime/EngineServiceLayer/`) and is the cleanest reference example.

- `framework`'s top-level `Runtime/` layer folders are `Initialization`, `Application`, `BlackBoardLayer`, `ExternalLayer`, `EngineServiceLayer`. **Only `BlackBoardLayer` currently holds any code**; the other four are asmdef-only stubs (see below).
- `BlackBoardLayer` (`Runtime/BlackBoardLayer/`) compiles as one nested asmdef (`UsefulToolkit.Framework.Runtime.BlackBoard`). Everything in it is namespace `UsefulToolkit.Framework.BlackBoard`, **except** the `State/` types, which are namespace `UsefulToolkit.Application.StateManagement`. It contains:
  - `BlackBoard/Board/` — `BlackBoard.cs` (registers ChildBoards by type; `TryRegister/TryGetStateBoard` and `TryRegister/TryGetEventBoard` use separate dictionaries), `ChildStateBoardBase.cs`, `ChildEventBoardBase.cs`, `BoardDispose.cs` (an Action-backed `IDisposable` used by every Register/Subscribe path in this layer).
  - `BlackBoard/State/` — `StateBase` (abstract `GetLog()`), `StateContext<T>` (change-notification payload, not wired up yet), and the three lifetime base classes `GameStateBase` / `SceneStateBase` / `UnRegistableStateBase`, which are **empty marker classes**: lifetime is expressed purely by which base class a State derives from and which `Register*` overload accepts it. `IStateGetter` (also in `StateBase.cs`) is an empty marker interface used as the dictionary key.
  - `BlackBoard/Event/` — `IEvent` (empty marker interface, the Event-side counterpart of `IStateGetter`) plus three channel implementations and their subscribe-only interfaces: `ActionChannel<T>`/`IActionChannel<T>` (fire-and-forget), `FuncCollectChannel<TArg,TRet>`/`IFuncCollectChannel<,>` (call every handler with the same argument, collect the results into an array), `FuncChainChannel<T>`/`IFuncChainChannel<T>` (pipeline: each handler's return value feeds the next).
  - `BlackBoard/Interface/IBlackBoard.cs`, `BlackBoard/Attributes/RegisterBoardAttribute.cs` (declares on a State which board it gets registered into; validates against `ChildStateBoardBase`), and `Logger/UsefulLogger.cs`.
- **`ChildStateBoardBase` and `ChildEventBoardBase` are deliberate 1:1 mirrors of each other.** Both key everything on an interface type (`IStateGetter`-derived / `IEvent`-derived), both expose the same three lifetime scopes, and both share the same exception types and messages:

  | | State side | Event side |
  |---|---|---|
  | ゲーム終了まで | `RegisterGameState<T>(GameStateBase)` | `RegisterGameEvent<T>(IEvent)` |
  | シーンアンロードまで | `RegisterSceneState<T>(SceneStateBase, sceneName)` | `RegisterSceneEvent<T>(IEvent, sceneName)` |
  | 手動解除 | `RegisterUnRegistableState<T>(...)` → `IDisposable` | `RegisterUnRegistableEvent<T>(IEvent)` → `IDisposable` |
  | 取得 | `TryGetGameState` / `TryGetSceneState` / `TryGetUnRegistableState` | `TryGetGameEvent` / `TryGetSceneEvent` / `TryGetUnRegistableEvent` |
  | 登録確認 | `CheckRegisterGameState` ほか | `CheckRegisterGameEvent` ほか |
  | 登録の待受 | `SubscribeStateRegister<T>(Action, bool invokeIfRegistered = false)` | `SubscribeEventRegister<T>(Action, bool invokeIfRegistered = false)` |

  One asymmetry is unavoidable: State's `Register*` take a base *class* so the lifetime scope is enforced at compile time, while Event's take `IEvent` (an interface), so the scope is only expressed by the method name. When editing one of these two classes, make the matching change in the other.
- **Publish permission is separated by type, on both channels.** A State's concrete class is held privately by its owner and published only as an `IStateGetter`-derived read interface; a channel's concrete class (`ActionChannel<T>` etc., which owns `Invoke`) is held privately by its publisher and handed out only as the matching `I*Channel<T>` (which exposes `Register` only). Registering a handler always returns an `IDisposable`; `event Action` is deliberately not used, because lambdas registered that way cannot be unsubscribed. All three channels reject a duplicate handler at `Register` time (delegate equality cannot tell two identical registrations apart at unregister time) and snapshot their handler list before invoking, so a handler may Register/Unregister during an invocation. `FuncChainChannel<T>.Register` additionally takes an `int priority` — **ascending, i.e. smaller runs first**, matching `InitializableMonoBehaviour.InitializationOrder` — and inserts in sorted position at registration time so that equal priorities keep their registration order.
- `IBlackBoard.OnSceneChanged(sceneName)` fans out to **both** ChildBoard kinds. Each ChildBoard drops only the entries registered under that scene name. Events need this as much as States do: the channel instance itself is held by the board, so a scene-owned publisher would otherwise stay reachable after its scene unloaded. Nothing calls `OnSceneChanged` yet — the scene system that was supposed to is deleted (below).
- **The scene-management system was deleted in full** (`SceneFlowController<T>`, `SceneState<T>`/`ISceneStateGetter<T>`, `SceneBoard`, `SceneChangeBoard<T>`/`ISceneChangeEvent<T>`, `SceneLoadService<T>`, and the `SceneFlowBase<T>`/`SceneNode<T>`/`SceneGroup*` graph types), because rebuilding the Event side would have broken it. That is why `Application/`, `ExternalLayer/`, and `EngineServiceLayer/` are empty. It is intended to be rebuilt on the new EventBoard — don't treat those empty folders as "this layer isn't used".
- The `architecture` package's `Initialize/` folder (`CompositionBase`, `InitializerBase`, `InitializableMonoBehaviour`) implements the ordered-initialization convention: `MonoBehaviour`s that shouldn't run until explicitly initialized start disabled (`enabled = false` in `Awake`), get re-enabled from `Initialize()`, and sort via `IComparable<InitializableMonoBehaviour>.InitializationOrder` (ascending). Both currently run at `[DefaultExecutionOrder(100)]`.

Before writing or reviewing code in this area, treat this as a genuinely unfinished, actively-changing design — don't assume a folder's current (often empty) contents reflect the intended final shape.

### Editor tooling internals

- Settings pages are discovered via reflection, not hard-wired: `SettingPageProvider` (`Framework/Editor/Setting/`) uses `InstanceCollector`/`TypeCollector` (`Framework/Editor/Reflection/`) to find all `SettingPageBase` subclasses and surfaces them as tabs in `UsefulToolkitSettings`. Adding a new settings tab means subclassing `SettingPageBase` — no registration step needed.
- Custom attributes (`ShowOnly`, `PullDownArray`, `SubclassSelector`, `MethodExecutor`) live in `Framework/Runtime/Attributes/` with matching `PropertyDrawer`/hook implementations in `Framework/Editor/Attributes/`.
- Project-wide code generation (enum/file generation for scenes etc.) is configured through `UsefulToolkitProjectSettings` / `UsefulToolkitSettingsScriptable`, persisted to `ProjectSettings/UsefulToolkitSettings.asset`.

## Conventions

- In-code comments and doc-comments are written in Japanese; match this when editing existing files.
- `UsefulToolkitInstaller`'s package list (`_packages` in `Packages/com.rei.usefultoolkit.framework/Editor/Setting/UsefulToolkitInstaller.cs`) uses `com.rei.usefultoolkit.*` sub-paths that now match the real package folder names 1:1, including `architecture` and `gitsupport`. If you add, rename, or remove a package folder, update this list too — nothing keeps them in sync automatically.
