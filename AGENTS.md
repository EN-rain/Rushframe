# Rushframe Repository Instructions

These instructions apply to the entire repository. They are the first navigation and implementation guide for any coding agent working in Rushframe.

## 1. Start every task here

Before reading or changing code:

1. Run `git status --short` and preserve every existing user change.
2. Read the files directly related to the requested behavior before editing.
3. For a defect, also read `qa testing/manual review/showcase-edit/defect_log.md` and the relevant tests.
4. Prefer the smallest correct change. Do not reorganize unrelated code while solving a focused task.
5. Verify the affected layer first, then run the broader required gates listed below.
6. Report exactly what changed, which files changed, what was verified, and any blocker.

The working tree is often intentionally dirty. Never reset, checkout, clean, delete, reformat, or overwrite unrelated work. Never assume an untracked file is disposable.

## 2. Product identity and non-negotiable boundaries

Rushframe is a local-first Windows video editor and agent-editing runtime. It is built with .NET 10, WPF, FFmpeg/FFprobe, optional native C++, and an optional Python media-intelligence service.

Keep these product boundaries intact:

- Rushframe is an editor, preview surface, renderer, and controlled automation host.
- Manual local media upload/import and manual timeline editing are first-class workflows.
- The desktop application is not a social-media downloader, scraper, URL importer, or autonomous web acquisition tool.
- Agents may operate only on media already registered in the open Rushframe project.
- Do not add a raw download escape hatch, arbitrary URL fetching, or source-site extraction logic.
- Do not mutate or overwrite original source media. Generate proxies, thumbnails, waveforms, analysis files, previews, compositions, and exports separately.
- The user remains the source of approval for consequential agent edits, paid-provider use, workflow approvals, and renders.
- Manual edits win. Revision conflicts must stop stale agent changes rather than overwrite newer user work.
- The desktop editor is not the place for a general chat/prompt composer unless explicitly requested. External agents communicate through the local bridge/MCP surface.
- Extension manifests are metadata only. Rushframe currently does not execute arbitrary extension code.
- Creative-asset packs must remain local, path-contained, and license/attribution aware.
- External composition engines may render local generated assets, but Rushframe remains the project and timeline source of truth.
- Clipster is a separate monetization/platform concept, not the Rushframe editor.

## 3. Source-of-truth order

When documentation and code disagree, use this order:

1. Current source code and tests.
2. This `AGENTS.md` file.
3. Current QA defect/execution records under `qa testing/`.
4. `AGENT_CONTEXT.md` and `LOCAL_AGENT_INSTRUCTIONS.md` for extended background.
5. `README.md` and older planning documents.

Do not preserve stale behavior merely because an older document describes it.

## 4. High-level architecture

The solution uses a layered architecture:

```text
Rushframe.Desktop
  -> Rushframe.Application
  -> Rushframe.Infrastructure
  -> Rushframe.Media.Native

Rushframe.Application
  -> Rushframe.Domain
  -> Rushframe.Media.Abstractions
  -> Rushframe.LegacyImport

Rushframe.Infrastructure
  -> Rushframe.Domain

Rushframe.Media.Native
  -> Rushframe.Domain
  -> Rushframe.Media.Abstractions

Rushframe.LegacyImport
  -> Rushframe.Domain

Rushframe.Native.Interop
  -> optional standalone native boundary
```

Do not introduce a dependency from `Rushframe.Domain` to WPF, FFmpeg, file-system infrastructure, Python, or desktop services. Keep domain editing deterministic and testable without the UI.

### Main directories

| Path | Responsibility |
|---|---|
| `src/Rushframe.Domain` | Canonical project/timeline model, time, effects, automation records, serialization, migrations, undoable commands |
| `src/Rushframe.Application` | Application-level commands and services that coordinate domain objects |
| `src/Rushframe.Infrastructure` | Persistence, autosave, cache paths, audit logs, asset packs, extension manifests |
| `src/Rushframe.Media.Abstractions` | Media service contracts and export option records |
| `src/Rushframe.Media.Native` | FFmpeg/FFprobe probing, derivatives, exact timeline render, verification |
| `src/Rushframe.Desktop` | WPF shell, timeline control, preview, inspector, agent bridge, automation UI |
| `src/Rushframe.LegacyImport` | Legacy project migration/import |
| `src/Rushframe.Native.Interop` | SafeHandle/PInvoke wrappers around optional native code |
| `native/Rushframe.Native` | Small C ABI for frame buffers and BGRA scaling |
| `rushframe_intelligence` | Python analysis pipeline, search index, local HTTP/MCP backend |
| `tests` | .NET and Python regression tests |
| `benchmarks` | BenchmarkDotNet microbenchmarks |
| `qa testing` | Release-style QA plans, defect log, UI automation scripts, manual-review evidence |
| `spikes` | Experiments; never treat a spike as production architecture without explicit promotion |

## 5. Canonical project model

The canonical persisted model lives in `src/Rushframe.Domain`.

### Project root

`Project.cs` currently uses schema version `3` and owns:

- project ID, revision, modified time, and name;
- sequences and local media library;
- imported media-intelligence analyses;
- campaign description and tasks;
- creative-asset providers and extension manifests;
- project overview;
- production workflow, decisions, provider costs, and budget policy;
- export variants and their overrides;
- external compositions;
- agent edit-plan history;
- render jobs and render receipts;
- transcript-edit policy.

Any persistent model change must consider serialization, migration, defaults, tests, old project compatibility, and agent state output.

### Timeline hierarchy

```text
Project
  -> Sequence
      -> Track
          -> TimelineItem
      -> Marker
      -> Transition
```

Important files:

- `Project.cs`: project root and revision.
- `Sequence.cs`: canvas, rational frame rate, tracks, markers, transitions, cached duration.
- `Track.cs`: track kind, order, mute/solo/lock/hidden state, items.
- `TimelineItem.cs`: source/timeline timing, transforms, audio, text, crop, effects, masks, color, speed, stabilization, animation, chroma key.
- `ProductionAutomation.cs`: workflow, providers, costs, variants, compositions, plans, jobs, receipts.
- `MediaIntelligenceModels.cs`: C# representation of imported analysis.

### Time and frame-rate rules

- `MediaTime` is the canonical time type.
- It stores integer ticks at `120,000` ticks per second.
- `FrameRate` is rational and supports NTSC-derived rates such as 24000/1001 and 30000/1001.
- Do not make `double` seconds the persisted or mutation source of truth.
- Convert to/from seconds only at UI, protocol, or FFmpeg boundaries.
- Snap frame-sensitive edits through `FrameRate`/`MediaTime`, not ad-hoc floating-point rounding.
- `TimelineItem.TimelineStart` and `Duration` update the global timing mutation version used by sequence-duration caching.

### IDs

Use the typed IDs (`ProjectId`, `SequenceId`, `TrackId`, `TimelineItemId`, `MediaAssetId`, and others). Do not replace them with unstructured strings inside domain code.

## 6. Editing and undo/redo contract

All timeline mutations should flow through `IEditCommand` implementations in `Rushframe.Domain.Editing` or compatible application commands.

`IEditCommand` requires:

- a meaningful `Description`;
- `Execute(Sequence)`;
- `Undo(Sequence)`;
- an `EditResult` that fails without leaving partial mutation.

For every mutating command:

1. Resolve and validate all targets first.
2. Reject missing tracks/items with typed domain errors where available.
3. Reject locked source tracks, locked destination tracks, and locked items before mutation.
4. Validate timing, duration, indexes, transitions, effect parameters, and source bounds.
5. Capture the exact prior state and original collection index before mutation.
6. Apply the smallest deterministic mutation.
7. Make `Undo` restore exact values, ordering, ownership, and references.
8. Ensure a failed command does not enter undo history.
9. Add focused regression tests for execute, failure/no-mutation, undo, and redo.

Use `TimelineItemCloner` instead of manually copying an item. Preserve all supported properties, effects, masks, animation channels, and source metadata.

Use `CompositeEditCommand` for coordinated operations. It executes commands in order, rolls back already-executed commands if a later command fails, and undoes in reverse order. One user/agent operation should normally create one undo entry and one project revision.

`UndoRedoStack` keeps at most 100 successful commands. Successful execution clears redo history.

### Desktop mutation sequence

The standard desktop path is `MainWindow.Project.cs -> Execute(IEditCommand)`:

```text
BeginMutation()
  -> UndoRedoStack.Execute(...)
  -> Project.IncrementRevision() exactly once
  -> update TimelineControl.ProjectRevision
  -> refresh selection/inspector when needed
  -> MarkProjectDirty(...)
  -> autosave coalescing and command-state refresh
```

Do not bypass this path for ordinary manual edits. If a special workflow mutates project-level state directly, it must still use `ProjectSaveCoordinator.BeginMutation()`, increment the revision exactly once for the committed mutation, mark the project dirty, and update any dependent UI/cache state.

## 7. Project revision, persistence, and schema rules

### Revision semantics

- `Project.Revision` is the conflict boundary for manual and agent edits.
- Increment it only after a successful committed mutation.
- Do not increment for previews, validation, rejected changes, or failed commands.
- Agent mutations and renders require the caller's `base_revision` to match the current project revision.
- On mismatch, return a conflict and require fresh state. Never silently rebase a stale agent edit.

### Save coordination

`ProjectSaveCoordinator` is the normal persistence coordinator.

- Wrap mutable project operations with `BeginMutation()`.
- It serializes stable snapshots off the UI thread.
- Its mutation epoch prevents partially-mutated snapshots.
- It coalesces repeated dirty revisions, debounces autosaves, and serializes file writes through a writer gate.
- Do not perform project file I/O inside an edit command.
- Do not add a second autosave loop or competing save coordinator.

`ProjectRepository` and `AutosaveService` use temporary-file-then-move writes. Preserve atomic replacement and temporary-file cleanup behavior.

Autosaves are explicit recovery data. Startup must not silently replace the empty project with the newest autosave. Recovery is exposed through `File > Recover Latest Autosave`.

### Serialization and migrations

`ProjectSerializer` uses:

- camelCase JSON;
- indented output;
- string enums;
- the custom `MediaTimeConverter`;
- workflow/provider/variant defaults;
- a regenerated project overview before serialization.

When changing persisted structure:

1. Bump `Project.CurrentSchemaVersion` only when necessary.
2. Add one deterministic migration step in `ProjectMigrationPipeline`.
3. Preserve old field compatibility where intentionally supported.
4. Reject projects newer than the current schema.
5. Add serializer and migration tests for old and current documents.
6. Consider Python/import/agent payload compatibility if the field crosses boundaries.

Do not hand-edit a saved project format in UI code when the serializer or migration layer should own the behavior.

## 8. Desktop/WPF navigation map

`MainWindow` is intentionally split across partial files. Route changes to the narrowest file instead of expanding `MainWindow.xaml.cs` further.

| File | Primary responsibility |
|---|---|
| `MainWindow.xaml` | Main shell layout and named controls |
| `App.xaml` | Shared styles, brushes, control templates, theme resources |
| `MainWindow.xaml.cs` | Composition root, service construction, command wiring, settings, bridge protocol, general shell behavior |
| `MainWindow.Project.cs` | dirty state, execute path, save/open/new/recent/recovery |
| `MainWindow.Media.cs` | import/relink/cache, media intelligence, audio extraction |
| `MainWindow.Preview.cs` | transport, source preview, exact preview chunks, seek/frame-step/marks/fullscreen |
| `MainWindow.RealtimePreview.cs` | WPF layered timeline compositor and media-player synchronization |
| `MainWindow.PreviewInteraction.cs` | preview transform handles, drag/scale/rotate/snapping |
| `MainWindow.Inspector.cs` | media-aware inspector profiles and item/effect editing |
| `MainWindow.Automation.cs` | workflow, transcript actions, variants, receipts, compositions |
| `MainWindow.Assets.cs` | creative-asset discovery and insertion |
| `MainWindow.Canvas.cs` | canvas settings and guide overlay |
| `MainWindow.CommandSearch.cs` | global function search |
| `Timeline/TimelineControl*.cs` | timeline drawing, hit testing, selection, drag/trim/group interactions |
| `Controllers/ExportController.cs` | user-facing export orchestration |
| `Controllers/AgentEditCommandFactory.cs` | agent action payload -> domain command |
| `Controllers/AgentEditPlanCompiler.cs` | validated multi-operation plan -> one composite command |
| `Services/*` | focused reusable desktop services |

### WPF rules

- UI-bound objects and controls must be touched on the dispatcher thread.
- Keep blocking I/O, FFmpeg work, hashing, serialization, and model inference off the UI thread.
- Use cancellation tokens for long operations and terminate child processes on cancellation.
- Reuse shared resources/styles from `App.xaml` before adding local duplicates.
- When adding a named control, update only the related partial/controller and tests.
- Keep media-aware Inspector visibility and write behavior aligned; hidden unsupported fields must not be parsed or persisted.
- Preserve keyboard accessibility, UI Automation behavior, and explicit focus styling.

## 9. Preview architecture

Rushframe has two complementary timeline-preview paths.

### Real-time WPF preview

Key files:

- `MainWindow.RealtimePreview.cs`
- `Services/RealtimeRenderPlan.cs`
- `Services/PreviewFrameScheduler.cs`
- `Timeline/TimelineSceneIndex.cs`

Rules:

- `RealtimeRenderPlan` is revision-scoped and pre-indexes visual/audio intervals.
- Rebuild it when the project revision or relevant sequence state changes.
- Use its active/warm interval queries instead of rescanning the full timeline every frame.
- `PreviewFrameScheduler` owns the single `CompositionTarget.Rendering` callback during playback.
- Do not add another independent preview timer or compositor callback.
- Avoid per-frame LINQ, reflection, repeated media lookups, object allocation, or control recreation in hot paths.
- Keep low-frequency transport text updates separate from visual frame scheduling.
- Respect the configured preview FPS and maximum width.

### Exact FFmpeg preview

Key files:

- `MainWindow.Preview.cs`
- `Services/ExactPreviewCache.cs`
- `Rushframe.Media.Native/FfmpegTimelineRenderer.cs`

Rules:

- Exact chunks are deterministic and keyed by project, sequence, project revision, chunk index, and output dimensions.
- Any committed timeline mutation marks exact preview dirty through the normal dirty path.
- Keep chunk renders bounded and cancellable.
- Validate generated MP4 chunks before publishing them into the cache.
- Preserve in-flight request deduplication and bounded cache cleanup.
- Canonical timeline playhead time, not stale `MediaElement.Position`, drives exact seek and frame-step behavior.
- Internal slider synchronization must not recursively issue seeks. Pointer, keyboard, and UI Automation seeks must all reach the canonical seek path.
- Never present an old frame as if a pending exact chunk were ready.

### Preview/export parity

Realtime preview may approximate unsupported expensive details; exact preview and final export must use the canonical FFmpeg graph. When adding an effect, mask, transition, animation property, blend mode, or timing behavior:

1. Update the domain model/command.
2. Update realtime preview if supported there.
3. Update exact FFmpeg rendering.
4. Update render capability validation.
5. Add render/preview parity tests or QA evidence.
6. Verify representative start, boundary, transition, middle, and end frames.

## 10. Media and FFmpeg navigation

### Contracts

`Rushframe.Media.Abstractions` defines probing, derivatives, export requests, and `TimelineExportOptions`.

### Implementation

`FfmpegMediaService` is partial across several files:

- `FfmpegMediaService.cs`: probing, proxy/thumbnail/waveform generation, simple export, audio extraction, shared helpers.
- `FfmpegTimelineRenderer.cs`: canonical timeline render graph.
- `FfmpegMediaService.Quality.cs`: runtime versions and post-export verification.
- `FfmpegProcessRunner.cs`: bounded concurrent process execution, stdout/stderr limits, metrics, cancellation, process-tree termination.

Do not construct shell command strings. Pass executable arguments through `ProcessStartInfo.ArgumentList`/the shared runner.

Preserve these process constraints:

- bounded active FFmpeg jobs;
- bounded stdout/stderr capture;
- complete process-tree termination on cancellation;
- temporary-file cleanup;
- invariant-culture numeric formatting;
- even output dimensions where codecs require them;
- no arbitrary unvalidated FFmpeg arguments from an agent payload.

`FfmpegTimelineRenderer.ValidateRenderCapabilities` must reject enabled effects or masks the exact renderer cannot reproduce. Do not silently omit an unsupported render feature.

### Render verification and receipts

Every controlled timeline/variant render should flow through render-job state and `RenderReceiptService` when that workflow expects verification.

Verification can include:

- file existence and non-empty size;
- probe metadata, dimensions, duration, codecs, and streams;
- full decode;
- black and freeze intervals;
- loudness, peak, and silence checks;
- evidence frames around clip/transition boundaries;
- source hashes and output hash;
- timeline/safe-area warnings;
- runtime version information.

A verification failure is not a successful render. Keep render-job, variant, workflow, and receipt status synchronized.

## 11. Agent bridge and automation contract

### Local editor bridge

`LocalAgentBridgeService` exposes a loopback-only HTTP service, normally at `127.0.0.1:7320`.

Security invariants:

- loopback clients only;
- random per-session token;
- `X-Rushframe-Session` or Bearer authorization for non-health routes;
- fixed-time token comparison;
- request-size limit of 1 MiB;
- `Cache-Control: no-store`;
- UI work marshaled through the WPF dispatcher;
- output paths restricted to the saved project directory or the local app-data export directory;
- no UNC/network output paths for agent renders.

Current bridge protocol version is `2`. Main routes include timeline/transcript/workflow/provider state, cost handling, variants, compositions, render jobs, receipts, audit, edit-plan preview/apply, single edits, and rendering.

### Agent edit actions

`AgentEditCommandFactory.SupportedActions` is the canonical action list. It currently covers:

- adding text/captions/clips/music;
- move/trim/split/delete/ripple-delete/duplicate;
- transforms and item/text properties;
- transitions, effects, masks, chroma key, animation channels;
- markers;
- track add/delete/duplicate/rename/reorder/toggles;
- sequence settings;
- transcript captions/clips, best moments/takes, and silence removal.

When adding or changing an action:

1. Use the same domain command as the manual editor.
2. Validate typed IDs, bounds, source registration, offline state, locks, and payload values.
3. Update `SupportedActions` and capability output.
4. Update MCP/tool schemas if the action is exposed there.
5. Add factory/plan/bridge tests.
6. Keep preview-only behavior non-mutating.
7. Keep approval defaulted to true.
8. Audit success, rejection, conflict, and failure.

Agents never receive raw domain object mutation or raw FFmpeg execution.

### Multi-operation plans

`AgentEditPlanCompiler`:

- accepts at most 100 operations;
- validates every operation before execution;
- checks protected tracks/items;
- compiles to one `CompositeEditCommand`;
- records operation summaries, affected ranges, and warnings;
- does not execute during compilation;
- applies as one atomic undo entry after revision validation and approval.

Do not partially apply an agent plan.

### Provider and cost policy

Automation is local-first. Provider rules live in `ProductionAutomation.cs` and bridge handlers.

- Local provider endpoints must be file URIs or loopback HTTP endpoints.
- Remote endpoints must use HTTPS.
- Paid providers require the project paid-provider policy, budget checks, and user approval where configured.
- Record estimated/reserved/actual cost events and reconcile them.
- Surface a local alternative when one exists.
- Never hide or silently incur paid usage.

## 12. External compositions and creative assets

### External compositions

`ExternalCompositionService` supports validated local Remotion, HyperFrames, or custom composition projects.

- Keep project directories and outputs local and path-contained.
- Validate entry points and executable availability.
- Do not allow network paths.
- Render to a generated file, verify it, optionally import it as a new local media asset, and preserve Rushframe timeline authority.
- Do not absorb an external composition engine as an uncontrolled runtime dependency.

### Creative asset packs

`CreativeAssetPackService` loads built-in shapes and `*.rushframe-assets.json` local packs.

- `AllowsNetwork` must remain false.
- Asset IDs must be unique.
- Resolve local and preview paths inside the pack directory.
- Reject path traversal and missing files.
- Enforce required attribution metadata.
- Never copy or expose font files as user-downloadable artifacts.

### Extensions

`ExtensionManifestService` loads `*.rushframe-extension.json` metadata.

- Automatically allowed permissions are read-project, read-media-metadata, and propose-edits.
- Higher-risk permissions disable the manifest.
- Entry points must be local and path-contained.
- Remote HTTP entry points are forbidden.
- Do not execute extension code until a separate reviewed sandbox/host exists.

## 13. Python media-intelligence subsystem

The Python package is optional and local-first. It analyzes registered source files into a versioned editing-context bundle.

### Entry points

- `python -m rushframe_intelligence analyze ...`
- `python -m rushframe_intelligence doctor ...`
- `python -m rushframe_intelligence search ...`
- `python -m rushframe_intelligence context ...`
- `python -m rushframe_intelligence serve ...`

`worker.py` is the CLI. `backend.py` hosts local HTTP and MCP, normally on `127.0.0.1:7319`.

### Pipeline order

`MediaIntelligencePipeline` generally performs:

1. source validation and output-directory setup;
2. fast fingerprint/full checksum cache validation;
3. technical probe;
4. optional input-duration clipping for analysis only;
5. scene detection and fallback full-source scene;
6. transcription;
7. audio/loudness/silence/music analysis;
8. frame extraction and visual-quality scoring;
9. optional alignment, OCR, diarization, semantic audio events, and visual understanding;
10. editing-moment construction;
11. duplicate-take grouping;
12. versioned JSON outputs and SQLite context index.

Main outputs include `media-analysis.json`, `summary.json`, `manifest.json`, `scenes.json`, `transcript.json`, `audio-events.json`, `moments.json`, `duplicate-takes.json`, `frames/`, and `context.sqlite`.

### Python invariants

- Analysis is keyed to the source file, checksum/fingerprint, analysis version, and enabled feature set.
- Timeline edits do not invalidate source analysis.
- Optional dependencies must fail as warnings/skips where designed, not crash the baseline pipeline.
- Preserve backward compatibility when loading v1 analysis documents.
- Keep dataclasses serializable and schema/version fields explicit.
- Bound context/search result counts.
- Use absolute local paths for analysis/index operations.
- The backend is loopback-only, request-limited, thread-bounded, and optionally session-token protected.
- Do not broaden MCP bridge URLs beyond localhost/127.0.0.1.

When changing Python output fields, also inspect the C# media-intelligence importer and its tests.

## 14. Optional native boundary

`native/Rushframe.Native` exposes a deliberately small C ABI:

- create/destroy frame buffer;
- retrieve buffer data/stride/size;
- scale BGRA pixels;
- retrieve the last native error.

`src/Rushframe.Native.Interop` wraps that ABI with source-generated `LibraryImport`, safe handles, and managed exceptions.

Rules:

- Preserve the C ABI and calling convention unless every native and managed caller is updated together.
- Keep ownership explicit: every created native buffer must be destroyed exactly once.
- Validate dimensions, stride, size, null pointers, and overflow before native memory access.
- Keep exceptions from crossing the C boundary; return `rf_result` and store a retrievable error message.
- Add managed and native tests before moving more editor logic into C++.

## 15. Task-routing guide

Use this table before searching the whole repository.

| Task | Start here | Also inspect |
|---|---|---|
| Add/change timeline operation | `src/Rushframe.Domain/Editing` | `MainWindow.Project.cs`, agent factory, domain tests |
| Lock behavior | target command | `LockedTrackCommandTests.cs`, Inspector/preview selection behavior |
| Project field/schema | `Project.cs`, serializer, migration pipeline | serializer tests, bridge state, importers |
| Timeline drawing/interaction | `Timeline/TimelineControl*.cs` | viewport/scene index tests, commands |
| Preview transport/seek | `MainWindow.Preview.cs` | seek gate/math tests, exact cache |
| Realtime preview visual | `MainWindow.RealtimePreview.cs` | `RealtimeRenderPlan`, exact renderer parity |
| Preview transform handles | `MainWindow.PreviewInteraction.cs` | transform command, inspector |
| Inspector field/profile | `MainWindow.Inspector.cs` | `InspectorProfile.cs`, XAML, desktop tests |
| Media import/relink/cache | `MainWindow.Media.cs` | media service, media model, application services |
| FFmpeg export/render | `FfmpegTimelineRenderer.cs` | process runner, export controller, media tests |
| Export verification | `FfmpegMediaService.Quality.cs` | receipt service, QA runbook |
| Agent single edit | `AgentEditCommandFactory.cs` | command tests, bridge handler |
| Agent multi-edit plan | `AgentEditPlanCompiler.cs` | preview dialog, plan tests, composite command |
| Bridge/security | `LocalAgentBridgeService.cs` | bridge handler, Python backend, agent tests |
| Workflow/providers/budget | `ProductionAutomation.cs` | automation UI, bridge handlers, automation tests |
| Variants | `ProductionAutomation.cs` | automation UI, variant render context, receipts |
| External composition | `ExternalCompositionService.cs` | composition dialog/UI, render jobs, import path |
| Creative assets | `CreativeAssetPackService.cs` | asset dialog/insertion, guardrail tests |
| Extension manifest | `ExtensionManifestService.cs` | extension domain model and guardrail tests |
| Save/autosave/recovery | `ProjectSaveCoordinator.cs` | repository, autosave service, project partial, persistence tests |
| Media intelligence pipeline | `rushframe_intelligence/pipeline.py` | models, serialization, C# importer, Python tests |
| Intelligence search/MCP | `context_index.py`, `backend.py` | worker, agent context, tests |
| Performance regression | relevant hot path | telemetry, benchmarks, performance tests/QA scripts |
| Theme/control styling | `App.xaml` | `MainWindow.xaml`, live WPF smoke test |

## 16. Testing strategy

Add deterministic regression tests near the affected layer.

### Test project map

- `tests/Rushframe.Domain.Tests`: timeline commands, locks, time, serialization, migrations, automation, performance invariants.
- `tests/Rushframe.Desktop.Tests`: non-visual desktop services, panel/layout, inspector profiles, preview math/gates, agent automation.
- `tests/Rushframe.Media.Tests`: FFmpeg probing, derivatives, render graph, export behavior.
- `tests/Rushframe.LegacyImport.Tests`: old project import.
- `tests/test_media_intelligence_v2.py`: Python moments, duplicates, index, bounded context, v1 compatibility.

### Focused-first rule

Run the smallest relevant test project/filter first. Examples:

```powershell
dotnet test tests/Rushframe.Domain.Tests/Rushframe.Domain.Tests.csproj --filter FullyQualifiedName~LockedTrack
dotnet test tests/Rushframe.Desktop.Tests/Rushframe.Desktop.Tests.csproj --filter FullyQualifiedName~Preview
dotnet test tests/Rushframe.Media.Tests/Rushframe.Media.Tests.csproj
python -m pytest tests/test_media_intelligence_v2.py -q
```

### Required repository gates

From the repository root:

```powershell
dotnet build Rushframe.slnx
dotnet test Rushframe.slnx
python -m pytest tests/test_media_intelligence_v2.py -q
dotnet build Rushframe.slnx -c Release
dotnet test Rushframe.slnx -c Release --no-build
dotnet list Rushframe.slnx package --vulnerable --include-transitive
```

Use `--no-restore` or `--no-build` only after the prerequisite restore/build succeeded in the same verification session.

Tests that require FFmpeg/FFprobe may need `.tools/bin` or PATH. If FFprobe is unavailable, do not pretend its checks ran; use the documented FFmpeg fallback only where the QA plan allows it.

### WPF/manual verification

Automated unit tests are not sufficient for changes to:

- startup and project recovery;
- dialogs and native file pickers;
- timeline drag/trim/multiselect;
- preview seek/playback/fullscreen;
- Inspector visibility and focus;
- exact-preview/export parity;
- export progress, cancellation, completion prompts;
- visual typography/safe areas.

Use the real editor and existing scripts/runbooks under `qa testing/`. Capture evidence only when the task requires it; do not commit transient screenshots/logs unless the repository's QA artifact workflow calls for them.

## 17. Defect workflow

For a reproduced defect:

1. Reproduce before editing when practical.
2. Add the next `QA-NEW-###` entry to `qa testing/manual review/showcase-edit/defect_log.md` before the code fix when following the active release QA process.
3. Include severity, status, build, preconditions, steps, expected, actual, impact, evidence, and suspected component.
4. Fix the smallest root cause.
5. Add a deterministic regression test that fails before the fix and passes after it.
6. Run focused tests, then required broader gates.
7. Retest through the real UI for UI/render/export defects.
8. Update the same defect entry with the exact retest result and evidence.

Do not create a code fix for an apparent defect that is actually project data, hidden modal UI, stale automation, or an unsupported local tool until the root cause is confirmed.

## 18. Performance and concurrency rules

Performance-sensitive areas include timeline layout/hit testing, animation lookup, preview frame rendering, project snapshotting, thumbnail/waveform work, FFmpeg jobs, and Python model loading.

- Measure before and after when changing a hot path.
- Prefer revision-keyed caches over repeated graph traversal.
- Invalidate narrowly; do not use a global invalidation when per-object versions are available.
- Warmed steady-state preview and animation reads should avoid allocations.
- Bound caches by count and/or bytes and clean stale temporary files.
- Bound process/thread concurrency.
- Never wait synchronously on long async work from the WPF UI thread.
- Dispose timers, cancellation sources, media players, file streams, native handles, and process resources.
- Preserve telemetry around startup, save snapshots/writes, preview frames, and FFmpeg processes.

Performance tooling is under `benchmarks/` and `qa testing/performance/`.

## 19. Generated and transient paths

Do not edit or use these as source unless a task explicitly targets generated output:

- `.git/`
- `**/bin/`
- `**/obj/`
- `**/.verify/`
- `**/.obj-check/`
- `**/.objcheck/`
- `**/.build*/`
- `.pytest_cache/`
- `**/__pycache__/`
- `.tools/` binaries
- `BenchmarkDotNet.Artifacts/`
- temporary Rushframe QA/app-data directories accidentally created inside the tree
- rendered QA evidence/output files unless the QA task explicitly requires them

Search source with generated directories excluded. Do not infer architecture from copied generated source trees.

## 20. Code-change discipline

- Match existing language/version features: C# for .NET 10, nullable enabled, implicit usings where configured; Python type hints and dataclasses in the intelligence package.
- Preserve public contracts unless the task requires a deliberate breaking change.
- Prefer clear named types over loosely shaped dictionaries inside the domain/application layers.
- Validate all file paths crossing external boundaries.
- Use invariant culture for serialized/FFmpeg numeric values.
- Do not swallow failures that affect correctness. Limited startup discovery may ignore invalid optional packs/manifests by design; explicit import must surface validation errors.
- Avoid broad catch blocks unless they preserve cancellation and intentionally convert optional-feature failures into warnings.
- Do not duplicate constants/protocol action names across layers without updating all contract tests.
- Keep comments focused on why a constraint exists, especially around security, persistence, timing, and performance.
- Do not add dependencies merely to avoid a small amount of straightforward code.
- Do not vendor or absorb an upstream repository without an explicit user request and license review.

## 21. Documentation maintenance

Update this file when a change materially alters:

- project architecture or dependency direction;
- canonical mutation/persistence flow;
- schema/protocol versions;
- bridge endpoints/security;
- preview/render architecture;
- required test/QA commands;
- product boundaries.

Use `AGENT_CONTEXT.md` for broader narrative/context and this file for enforceable navigation and implementation rules.

## 22. Required completion report

End a coding task with:

```text
Changes
- What behavior was added/fixed.

Files
- Exact files created or modified.

Verification
- Exact commands run and their results.
- Manual/UI verification performed, when applicable.

Notes / blockers
- Remaining risk, unavailable tools, unrelated pre-existing failures, or none.

Decision
- PASS, FAIL, or BLOCKED.
```

Be precise. Never claim a test, build, UI action, render check, or full-code review that was not actually performed.
