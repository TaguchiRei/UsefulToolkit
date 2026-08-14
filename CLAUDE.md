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
  - `UsefulToolkit/Project Structure` — opens `ProjectStructureWindow` (`Packages/com.rei.usefultoolkit.framework/Editor/ProjectStructure/`), which reorganizes a project's `Assets/` into the toolkit's standard layout: it creates the declared folders, moves/deletes whatever a JSON template's rules match, and can snapshot the current `Assets/` back out as a template. See "Project structure tool" below.
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
| `UsefulToolkit.Framework.Runtime.Application` | `Runtime/Application/` | `...Runtime.BlackBoard`, `...Runtime.External`, UniTask (same GUID) |
| `UsefulToolkit.Framework.Runtime.External` | `Runtime/ExternalLayer/` | *(none)* |
| `UsefulToolkit.Framework.Runtime.EngineService` | `Runtime/EngineServiceLayer/` | `...Runtime.BlackBoard`, UniTask (same GUID) |
| `UsefulToolkit.Framework.Runtime.Initialization` | `Runtime/Initialization/` (empty stub) | all four of the above (Initialization is the only layer allowed to see every other layer) |
| `UsefulToolkit.Utility` | `Runtime/Utility/` (empty, reserved for future use) | *(none)* |

`Initialization/` and `Utility/` hold no `.cs` at all — **keep their asmdefs anyway**, they carry the layer boundary and the reserved slot respectively. Every other layer folder is now occupied by the scene-management system (see the State-Centrism section below), which is also what the UniTask references in `...BlackBoard`/`...Application`/`...EngineService` are for.

Two naming exceptions to be aware of: `UsefulToolkit.Utility` does not follow the `UsefulToolkit.<X>.Runtime` pattern (it predates the layer split and is a reserved slot, not a layer), and `Runtime/BlackBoardLayer/Logger/UsefulLogger.cs` is a plain logging utility that happens to live under `BlackBoardLayer/` rather than in `Utility/` — it is not a BlackBoard feature.

Note: `UsefulToolkit.Framework.Runtime.EngineService` deliberately does **not** reference `...Runtime.Application` — `architecture` (which owns `InitializableMonoBehaviour`/`InitializerBase`) already depends on `framework`, so `framework` referencing anything back that would imply an architecture-style base class would be circular. EngineServiceLayer classes that need neither Inspector-serialized fields nor an `Update` loop are therefore written as plain C# classes, not `MonoBehaviour`s.

The `architecture` package (`UsefulToolkit.Architecture.Runtime`) is the toolkit's actual cross-package Initialization-layer implementation (see below) and, for the same "Initialization refers to all layers" reason, references `UsefulToolkit.Framework.Runtime` (for `Attributes`) plus all four framework layer asmdefs above — update that reference list too if a new framework layer asmdef is added. `input` references `UsefulToolkit.Framework.Runtime.BlackBoard` (it needs `BlackBoardLayer` only) plus `UsefulToolkit.Architecture.Runtime`, but otherwise keeps its own `BlackBoardLayer`/`EngineServiceLayer` folders inside a single package-wide asmdef rather than splitting further — only split a package's own Runtime asmdef along layer lines once it actually needs the compile-time boundary.

### State-Centrism Architecture (in-progress migration)

The `framework` and `architecture` packages are being restructured to follow a "State-Centrism" design: runtime `State` is the single source of truth, organized into layers — Initialization / Application / BlackBoardLayer / ExternalLayer / EngineServiceLayer. This is a live migration, not a finished design.

**Folder convention**: layer folders live directly under each package's `Runtime/`/`Editor/` (never nested inside a feature folder), and each layer folder contains one subfolder per feature it hosts — i.e. `UsefulToolkit.<package>/Runtime/<Layer>/<Feature>/...`, layer name first, feature name second. The `input` package follows this end-to-end (`Runtime/BlackBoardLayer/`, `Runtime/EngineServiceLayer/`) and is the cleanest reference example.

- `framework`'s top-level `Runtime/` layer folders are `Initialization`, `Application`, `BlackBoardLayer`, `ExternalLayer`, `EngineServiceLayer`. `Initialization` is still an asmdef-only stub; every other layer holds code (`BlackBoardLayer` has the mediator primitives, the other three hold the scene-management system).
- `BlackBoardLayer` (`Runtime/BlackBoardLayer/`) compiles as one nested asmdef (`UsefulToolkit.Framework.Runtime.BlackBoard`). Everything in it is namespace `UsefulToolkit.Framework.BlackBoard`, **except** the `State/` types, which are namespace `UsefulToolkit.Application.StateManagement`. It contains:
  - `BlackBoard/Board/` — `BlackBoard.cs` (registers ChildBoards by type; `TryRegister/TryGetStateBoard` and `TryRegister/TryGetEventBoard` use separate dictionaries), `ChildStateBoardBase.cs`, `ChildEventBoardBase.cs`, `BoardDispose.cs` (an Action-backed `IDisposable` used by every Register/Subscribe path in this layer).
  - `BlackBoard/State/` — `StateBase` (abstract `GetLog()`), `StateContext<T>` (change-notification payload, not wired up yet), and the three lifetime base classes `GameStateBase` / `SceneStateBase` / `UnRegistableStateBase`, which are **empty marker classes**: lifetime is expressed purely by which base class a State derives from and which `Register*` overload accepts it. `IStateGetter` (also in `StateBase.cs`) is an empty marker interface used as the dictionary key.
  - `BlackBoard/Event/` — `IEvent` (empty marker interface, the Event-side counterpart of `IStateGetter`) plus three channel implementations and their subscribe-only interfaces: `ActionChannel<T>`/`IActionChannel<T>` (fire-and-forget), `FuncCollectChannel<TArg,TRet>`/`IFuncCollectChannel<,>` (call every handler with the same argument, collect the results into an array), `FuncChainChannel<T>`/`IFuncChainChannel<T>` (pipeline: each handler's return value feeds the next).
  - `BlackBoard/Interface/IBlackBoard.cs`, `BlackBoard/Attributes/RegisterBoardAttribute.cs` (declares on a State which board it gets registered into; validates against `ChildStateBoardBase`), and `Logger/UsefulLogger.cs`.
  - `Scene/` — `SceneBoard.cs` and `ISceneStateGetter.cs`, the BlackBoardLayer half of the scene-management system (see below).
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
- `IBlackBoard.OnSceneChanged(sceneName)` fans out to `SceneBoard` plus **both** ChildBoard dictionaries. Each ChildBoard drops only the entries registered under that scene name. Events need this as much as States do: the channel instance itself is held by the board, so a scene-owned publisher would otherwise stay reachable after its scene unloaded. Its one caller is `SceneLoadService`, which calls it right after each `UnloadSceneAsync` completes.
- **`SceneBoard` is the one ChildBoard that `BlackBoard` special-cases**: it is passed to `BlackBoard`'s constructor and exposed as `IBlackBoard.SceneBoard`, instead of being registered later via `TryRegisterStateBoard`. It is deliberately *not* also put into `_stateChildBoards` (that would be double bookkeeping), which is why `OnSceneChanged` has to call it separately. The point is that every consumer can assume it exists — the previous design made `TryGetStateBoard<SceneBoard>` fail at runtime because nothing ever registered it.
- **The scene-management system** spans four layers and is the reference example for how a feature crosses them. It was rebuilt from scratch (the previous generic-everywhere version was deleted first):
  - `ExternalLayer/Scene/` — `SceneFlowAsset<T> : ScriptableObject where T : Enum` is the only place an `enum` appears. Consumers write a one-line non-generic subclass (`class GameSceneFlow : SceneFlowAsset<BuildScenes> {}`) so the asset can actually be created, fill in `SceneNodeData<T>`/`SceneGroupData<T>` in the Inspector, and call `Build()` to get the immutable runtime graph: `SceneFlow` (NodeId→node dictionary) / `SceneNode` (`Groups`, **`NextNodeIds` as `int[]`, never node references** — Unity cannot serialize a mutually-referencing object graph, which is what made the previous version impossible to author) / `SceneGroup` (a flat `IReadOnlyList<string>` of scene names, Lighting+Content+Logic+Additional concatenated and de-duplicated at build time).
  - `BlackBoardLayer/Scene/` — `SceneBoard` merges what used to be two boards: it is a `ChildStateBoardBase` (holding the `SceneState`) *and* carries the loader hand-off, `RegisterSceneLoader` (EngineService side) / `RequestTransitionAsync` (Application side). No ChildEventBoard is involved. `ISceneStateGetter` exposes only `int` node ids plus `Entered`/`Exited` channels — **it must not expose `SceneNode`**, since BlackBoardLayer is not allowed to reference ExternalLayer; whoever needs the node itself holds the `SceneFlow` and looks it up.
  - `Application/Scene/` — `SceneState` (`GameStateBase, ISceneStateGetter`; its `SetCurrentNode` is `internal`, so the compiler enforces Single Writer) and `SceneFlowControllerBase`, an **abstract** class whose `TransitionTo(nodeId, groupIndex)` is **`protected`**. Consumers derive one scene-manager class and expose their own game-vocabulary methods; that is what limits "who may start a transition" to a single type. Re-entrant `TransitionTo` throws `InvalidOperationException`, and progress is an `IActionChannel<float>`, not an `event`.
  - `EngineServiceLayer/Scene/` — `SceneLoadService`, the sole class touching `UnityEngine.SceneManagement.SceneManager`. It diffs the scenes it has loaded against the requested set and only unloads/loads the difference; it never uses `LoadSceneMode.Single`, which would tear down persistent System/Boot scenes it does not manage.
  - The enum itself comes from the Editor-side `SceneEnumGenerator` (`Framework/Editor/SceneSupport/`), which regenerates `AutoGenerated.BuildScenes` whenever the build scene list changes.
  - Still missing: nothing wires this up. `Initialization/` is empty, so the construction order (`SceneBoard` → `BlackBoard` → `SceneLoadService` → `flowAsset.Build()` → the derived controller) is currently the consumer's job.
- The `architecture` package's `Initialize/` folder (`CompositionBase`, `InitializerBase`, `InitializableMonoBehaviour`) implements the ordered-initialization convention: `MonoBehaviour`s that shouldn't run until explicitly initialized start disabled (`enabled = false` in `Awake`), get re-enabled from `Initialize()`, and sort via `IComparable<InitializableMonoBehaviour>.InitializationOrder` (ascending). Both currently run at `[DefaultExecutionOrder(100)]`.

Before writing or reviewing code in this area, treat this as a genuinely unfinished, actively-changing design — don't assume a folder's current (often empty) contents reflect the intended final shape.

### Editor tooling internals

- Settings pages are discovered via reflection, not hard-wired: `SettingPageProvider` (`Framework/Editor/Setting/`) uses `InstanceCollector`/`TypeCollector` (`Framework/Editor/Reflection/`) to find all `SettingPageBase` subclasses and surfaces them as tabs in `UsefulToolkitSettings`. Adding a new settings tab means subclassing `SettingPageBase` — no registration step needed.
- Custom attributes (`ShowOnly`, `PullDownArray`, `SubclassSelector`, `MethodExecutor`) live in `Framework/Runtime/Attributes/` with matching `PropertyDrawer`/hook implementations in `Framework/Editor/Attributes/`.
- Project-wide code generation (enum/file generation for scenes etc.) is configured through `UsefulToolkitProjectSettings` / `UsefulToolkitSettingsScriptable`, persisted to `ProjectSettings/UsefulToolkitSettings.asset`.

### Project structure tool

`Framework/Editor/ProjectStructure/` reorganizes a consumer project's `Assets/` (typically a freshly-created Unity project, whose default layout differs per template — URP/2D/HDRP each ship a different set of files) into the toolkit's standard layout. Split across `ProjectStructureTemplate` (data) / `ProjectStructurePath` (path helpers) / `ProjectStructureTemplateIO` (JSON load/save) / `ProjectStructurePlanner` (builds a side-effect-free plan, i.e. the dry run) / `ProjectStructureApplier` (executes it) / `ProjectStructureSnapshot` (current `Assets/` → template) / `ProjectStructureWindow` (IMGUI).

- The template is **JSON, not a ScriptableObject**, so it can be shipped inside the package and carried between projects. `DefaultTemplate.json` sits next to the code and is overridden per-project by `ProjectSettings/UsefulToolkitProjectStructure.json` (kept outside `Assets/` so the tool never processes its own config). Parsed with `JsonUtility`, so **no comments are allowed** — the `description` field is where notes go.
- Rules are `{match, pattern, action, destination}` and are evaluated in the fixed order **ExactPath → Folder → Glob → Name**; a target claimed by an earlier rule (including everything under a claimed folder) is never re-evaluated. That ordering is load-bearing: it is what lets `Assets/Readme.asset`'s Delete rule win over the broad `Assets/*.asset` Glob.
- `excludes` blocks a path from being a move *source* and from snapshot scanning, but still allows it as a *destination* — that's how `Assets/Plugins` receives `TextMesh Pro` while its own contents stay untouched. Unity's special folders (`Resources`, `StreamingAssets`, `Editor`, …) are excluded by default because their location carries meaning.
- Everything goes through `AssetDatabase` (`MoveAsset`/`CreateFolder`/`MoveAssetToTrash`) so `.meta` files and GUID references survive; Delete sends to the OS trash rather than deleting outright. All moves are pre-checked with `ValidateMoveAsset` and **the whole run aborts before any move or delete if a single one fails**.
- Two ordering constraints in the planner are easy to reintroduce as bugs: folders that a folder-move will itself create must *not* be pre-created (`BuildFolderCreations` filters anything same-or-under a move destination, which is why `Assets/Data/Settings` appears in `folders` yet is skipped when `Assets/Settings` is being moved into `Assets/Data`), and moves are topologically ordered by `OrderMoves` so a move whose destination is still occupied by another move's source runs second.
- Snapshot can only emit `Name` rules (the current state doesn't record where anything came from), and it **skips any name that occurs at more than one path**, reporting it as a warning instead of guessing. Re-running it regenerates the `Name`+`Move` rules while preserving hand-written ExactPath/Folder/Glob rules.

## Conventions

- In-code comments and doc-comments are written in Japanese; match this when editing existing files.
- `UsefulToolkitInstaller`'s package list (`_packages` in `Packages/com.rei.usefultoolkit.framework/Editor/Setting/UsefulToolkitInstaller.cs`) uses `com.rei.usefultoolkit.*` sub-paths that now match the real package folder names 1:1, including `architecture` and `gitsupport`. If you add, rename, or remove a package folder, update this list too — nothing keeps them in sync automatically.
