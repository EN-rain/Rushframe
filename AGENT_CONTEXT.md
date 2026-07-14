# Rushframe Agent Context

Read this file before exploring the repository. It is the fast-start map for coding agents working on Rushframe.

Last reviewed: 2026-07-12 after the OpenCut parity and guardrail upgrade

## 1. What Rushframe Is

Rushframe is a local Windows video editor built with .NET 10, WPF, FFmpeg/FFprobe, optional native C++, and an optional Python media-intelligence pipeline. Timeline preview now uses a WPF hardware-composited layered path for supported edits and automatically falls back to an exact FFmpeg composition for unsupported effects or masks. Final export always uses the FFmpeg render graph.

The product has two connected systems:

1. The Windows desktop editor, which owns project state, manual editing, preview, timeline operations, undo/redo, project persistence, export, review, and local agent approvals.
2. The Python media-intelligence backend, which analyzes registered local media and produces structured scenes, transcripts, audio measurements, editing moments, duplicate-take groups, hooks, and searchable context.

Rushframe is a preview and manual-editing application. The prompt/agent brain is external and interacts through controlled local tools. Clipster is a separate monetization platform, not the editor.

## 2. Non-Negotiable Product Rules

- Manual video, audio, music, and asset upload is supported and required.
- Do not add downloader, scraper, social URL import, or arbitrary website-download behavior.
- Agents may only work with media already registered as local project sources.
- The agent must edit structured project state through controlled commands; it must not directly and silently mutate private source videos.
- Manual user edits win by default.
- Agent changes must not silently overwrite manual or approved work.
- Undo/redo must remain available for editing operations.
- Human review and approval are required before final export/package workflows that depend on approval.
- Agent bridge and MCP requests require the per-editor-session token. Edit and render calls also require the current project revision.
- Third-party asset packs must be local-only, path-contained, and include license/attribution metadata when required.
- Extension manifests are permission metadata only. Rushframe does not execute arbitrary extension entry points; high-risk permissions remain disabled.
- Blocked tasks should report the exact missing input instead of guessing or bypassing the requirement.
- External repositories may be studied or absorbed, but Rushframe must not depend on pulling upstream code at runtime. Imported code should become locally owned and modifiable.

## 3. Repository Map

### `src/Rushframe.Desktop`

WPF desktop editor and composition root.

Responsibilities:

- Main application window and lifecycle
- Project open/new/save/recovery
- Media import, relink, cache generation, and extraction
- Timeline hosting, multi-selection, box selection, group move/resize, waveform gain lines, snapping, and user interactions
- Real-time layered preview, exact FFmpeg preview fallback, direct move/scale/rotate handles, marks, stepping, composite snapshots, and fullscreen
- Inspector, effects, color correction, stabilization, and text editing
- Campaign description and task UI
- Export dialogs and orchestration
- Workspace panels, layout, and settings
- Python intelligence backend startup
- Local agent bridge, approval flow, and audit log

Important files:

- `App.xaml`, `App.xaml.cs` — application startup
- `MainWindow.xaml` — main editor UI
- `MainWindow.xaml.cs` — composition root and remaining event wiring
- `MainWindow.Project.cs` — project lifecycle and persistence behavior
- `MainWindow.Media.cs` — media-library operations
- `MainWindow.Preview.cs` — preview behavior
- `MainWindow.Inspector.cs` — inspector/effect behavior
- `MainWindow.CommandSearch.cs` — global command search
- `Timeline/TimelineControl.cs` and `Timeline/TimelineControl.MultiSelection.cs` — custom timeline UI, group editing, cached waveform drawing, snapping, keyframe lanes, and interaction layer
- `Timeline/TimelineSceneIndex.cs` — revision-scoped timeline duration, visibility, hit-test, transition, marker, and snap-point indexes
- `Timeline/TimelinePlayheadOverlay.cs` — lightweight transport overlay that prevents full static-timeline redraws during playback
- `Controls/AnimationGraphControl.cs` and `Dialogs/AnimationEditorDialog.cs` — multi-property keyframe and Bezier graph editing
- `MainWindow.RealtimePreview.cs` — WPF layered timeline compositor and FFmpeg fallback selection
- `MainWindow.PreviewInteraction.cs` — direct preview transform handles and snapping
- `MainWindow.Canvas.cs` — canvas backgrounds, rational FPS, and social safe-area guides
- `MainWindow.Assets.cs` — local licensed asset packs and reviewed extension manifests
- `Controllers/ExportController.cs` — export workflow
- `Controllers/AgentEditCommandFactory.cs` — translates approved agent requests into edit commands
- `Services/LocalAgentBridgeService.cs` — local bridge for external agents
- `Services/IntelligenceBackendService.cs` — Python backend process integration
- `Services/ProjectSaveCoordinator.cs` — mutation-aware, revision-coalesced, off-thread project snapshot and save pipeline
- `Services/PreviewFrameScheduler.cs` — single throttled compositor scheduler for preview frames and transport updates
- `Services/RealtimeRenderPlan.cs` — revision-scoped interval/index plan for allocation-free active-layer queries
- `Services/ExactPreviewCache.cs` — deterministic range-preview chunk cache with corruption checks and bounded cleanup
- `Services/ThumbnailCache.cs` — bounded asynchronous frozen-bitmap LRU cache
- `Services/EditorPerformanceTelemetry.cs` — opt-in rolling samples plus `System.Diagnostics.Metrics` instruments
- `Workspace/*` and `Panels/*` — editor layout system

`MainWindow` was historically a very large god class. New work should continue moving cohesive behavior into partial files, controllers, or services rather than adding more unrelated code to the main file.

### `src/Rushframe.Domain`

Core editor model and editing rules. Keep this layer independent from WPF, FFmpeg, filesystem UI, and infrastructure concerns.

Important models:

- `Project` — project root, schema version, monotonic revision, media library, sequences, intelligence data, campaign description, tasks, asset providers, and extension manifests
- `Sequence` — canvas size, FPS, tracks, markers, transitions, computed duration
- `Track` — ordered timeline items and track metadata
- `TimelineItem` — clips/text/audio/image timeline state
- `MediaAsset` — registered local source media
- `MediaTime` — fixed 120,000-ticks-per-second timeline time value type
- `FrameRate` — rational frame-rate value type for integer and NTSC-derived rates
- `Transition`, `EffectDefinition`, multi-channel `AnimationChannel`/`Keyframe`, rich `Mask`, `SpeedCurve`, `ColorCorrection`, `Stabilization`
- `CanvasBackground`, `LayoutGuide`, `CreativeAssetProviderManifest`, and `ExtensionManifest`
- `CampaignTask` — user/agent-visible project tasks
- `MediaIntelligence` types — imported analysis state

Editing system:

- `Editing/IEditCommand.cs` — command contract
- `Editing/UndoRedoStack.cs` — edit history
- `Editing/AddClipCommand.cs`
- `Editing/MoveClipCommand.cs`
- `Editing/TrimClipCommand.cs`
- `Editing/SplitClipCommand.cs`
- `Editing/DeleteClipCommand.cs`
- `Editing/RippleDeleteClipCommand.cs`
- `Editing/DuplicateClipCommand.cs`
- `Editing/CompositeEditCommand.cs`
- effect, marker, text, track, and transition commands
- `TimelineItemCloner.cs` — safe cloning of timeline items

Prefer new timeline mutations as undoable domain commands. Avoid direct UI-side mutation when a command can represent the operation.

### `src/Rushframe.Application`

Application-level use cases that coordinate domain operations without owning WPF UI.

Important files:

- `Commands.cs` — application commands/helpers
- `PasteClipCommand.cs`, `SplitClipCommand.cs`, `SetPropertyCommand.cs`
- `MigrationService.cs` — project migration and backup orchestration
- `MediaIntelligenceImportService.cs` — imports Python analysis into domain state
- `MediaIntelligenceSearchService.cs` — searches imported/context-indexed analysis
- `MediaAgentContextBuilder.cs` — builds agent-readable media context

### `src/Rushframe.Infrastructure`

Filesystem and local-service implementations.

Important files:

- `ProjectRepository.cs` — project persistence
- `AgentAuditLogService.cs` — durable JSONL agent-action audit history
- `CreativeAssetPackService.cs` — local licensed asset-pack discovery and validation
- `ExtensionManifestService.cs` — non-executing permission-manifest discovery and validation
- `AutosaveService.cs` — periodic autosave and recovery
- `CacheService.cs` — cache operations
- `EffectRegistry.cs` — available effect definitions
- `StabilizationAnalysisService.cs` — stabilization analysis storage/execution
- `MediaIntelligenceImportService.cs` — infrastructure-side intelligence integration

### `src/Rushframe.Media.Abstractions`

Media service contracts shared by the application and FFmpeg implementation.

### `src/Rushframe.Media.Native`

FFmpeg/FFprobe-backed media implementation.

Primary file:

- `FfmpegMediaService.cs`

Responsibilities include media probing, media preparation, frame/thumbnail generation, real waveform-peak extraction, audio extraction, caching/transcoding, and export/render operations. `FfmpegTimelineRenderer.cs` is the exact composition/export implementation for transitions, animations, masks, styled text, stickers, adjustment layers, audio mixing, effects, backgrounds, and MP4/WebM/MOV/MKV output profiles.

FFmpeg and FFprobe are external runtime requirements. Preserve cancellation, path quoting, error reporting, and deterministic output handling. Process execution is centralized through `FfmpegProcessRunner`, which uses `ArgumentList`, bounded diagnostics, process-tree cancellation, and a concurrency gate. Exact preview should use range rendering and deterministic chunks rather than full-timeline temporary renders.

### `src/Rushframe.Native.Interop` and `native/Rushframe.Native`

Optional native pixel/frame operations.

- C# P/Invoke wrappers live in `Rushframe.Native.Interop`.
- Native C++ implementation lives under `native/Rushframe.Native`.
- `NativeFrameBufferHandle` and related types own native-resource lifetime.

Be careful with ownership, disposal, stride, dimensions, pixel format, and error-code translation.

### `src/Rushframe.LegacyImport`

Converts old Rushframe project files into the current domain model.

Primary file:

- `LegacyImporter.cs`

Legacy import is one-way compatibility logic. Do not leak old runtime architecture into the new editor.

### `rushframe_intelligence`

Python local media-analysis system.

Entry points:

- `__main__.py` — CLI entry
- `worker.py` — worker process behavior
- `backend.py` — local backend/API behavior
- `pipeline.py` — full analysis orchestration

Pipeline stages/components:

- media probing
- scene detection
- scene-frame extraction
- speech transcription
- optional forced alignment
- audio-level/event analysis
- music analysis
- optional OCR
- optional speaker diarization
- optional semantic audio-event recognition
- optional Gemini or local Qwen visual understanding
- visual quality scoring
- editing-moment construction
- duplicate-take detection
- JSON serialization
- cache manifests
- SQLite media-context indexing and search

Important constraints:

- Analysis runs against local registered media.
- Core features should degrade gracefully when optional AI dependencies are unavailable.
- Failures should become warnings where partial analysis remains useful.
- Outputs are versioned and cached.
- The pipeline limits analysis duration and can create a clipped analysis input.

See `rushframe_intelligence/README.md` for CLI and dependency details.

### `tests`

Automated tests:

- `Rushframe.Domain.Tests` — domain model, edit commands, undo/redo, serialization, architecture, recovery, intelligence search
- `Rushframe.Desktop.Tests` — panel registry, viewport/layout, desktop intelligence import
- `Rushframe.Media.Tests` — FFmpeg service
- `Rushframe.LegacyImport.Tests` — legacy conversion
- `test_media_intelligence_v2.py` — Python intelligence tests

### `qa testing`

QA plans, harness, automation scripts, execution results, defects, manual-review artifacts, and deterministic performance workloads. `qa testing/performance` contains trace/memory scripts and the project generator; generated captures belong under `qa testing/results/performance`.

Important rules:

- Real edited exports are required for manual review.
- Manual-review outputs belong in `qa testing/manual review`.
- Do not claim visual/export correctness based only on unit tests.

### `spikes`

Experimental proof-of-concept code. Do not treat spike code as production architecture unless it was deliberately promoted.

## 4. Main Runtime Flow

1. `App.xaml` launches the WPF desktop application.
2. `MainWindow` creates and wires services:
   - workspace/settings
   - project repository
   - autosave/recovery
   - migration
   - FFmpeg media service
   - stabilization service
   - media-intelligence import/search
   - local Python backend
   - local agent bridge
   - export controller
3. A `Project` owns local media assets, sequences, campaign context, tasks, and imported intelligence.
4. The active `Sequence` owns tracks, timeline items, markers, and transitions.
5. User or approved-agent edits should become `IEditCommand` instances, run inside `ProjectSaveCoordinator.BeginMutation()`, and increment `Project.Revision`.
6. Commands execute through `UndoRedoStack`; group edits use `CompositeEditCommand`. `MarkProjectDirty` schedules a coalesced autosave—normal edit handlers must not write project files directly.
7. Supported timeline compositions preview through a revision-scoped `RealtimeRenderPlan` and one `PreviewFrameScheduler`. Unsupported exact features use deterministic cached FFmpeg range chunks. FFmpeg always provides final rendering.
8. The Python pipeline writes structured analysis and a context index.
9. Desktop services import/search that analysis and expose it to the editor or external agent.
10. Projects are stored locally with autosave and recovery support.

## 5. Project State and File Format

The current C# `Project` object is the canonical editor state.

Important fields include:

- project identity and name
- sequences
- media library
- imported media intelligence
- campaign description
- campaign tasks

Persistence is handled through domain serialization plus `ProjectRepository` and migration services.

Project JSON is migrated through `ProjectMigrationPipeline` to `Project.CurrentSchemaVersion` before deserialization. Media time is stored in fixed integer ticks and frame rate is rational.

For agent-driven edits, preserve these principles:

- Work from the exact `base_revision` returned by the current timeline state; missing or stale revisions are rejected.
- Reject, merge, or request resolution when the base version is stale.
- Never silently replace manual edits.
- Preserve auditability and undoability.

## 6. Coding Rules for Future Agents

- Read this file first.
- Inspect only the files relevant to the requested task.
- Trust the current filesystem over old documentation or stale Git history.
- Check `git status` before editing because this repository may contain active uncommitted work.
- Never discard, reset, overwrite, or reformat unrelated user changes.
- Keep changes minimal and consistent with the existing architecture.
- Keep domain logic out of WPF code-behind when practical.
- Keep WPF types out of the domain layer.
- Use domain edit commands for timeline mutations.
- Preserve undo/redo behavior.
- Reuse existing clone helpers rather than hand-copying timeline-item state.
- Do not add a dependency unless it is clearly necessary.
- Do not introduce cloud requirements for core local editing.
- Do not add URL downloaders or social-media scraping.
- Do not confuse Rushframe with Clipster.
- Do not move the prompt/agent brain into the editor UI.
- When adding agent capabilities, keep session-token authentication, permission checks, approval, revision safety, and persistent audit logging.
- Keep real-time preview and FFmpeg export behavior aligned. Add an exact FFmpeg fallback instead of silently approximating unsupported features.
- Extension manifests must remain non-executing until a real sandboxed host exists.
- When touching FFmpeg execution, verify quoting, cancellation, cleanup, exit codes, and generated-file existence.
- When touching Python intelligence, preserve partial-result behavior and optional-feature fallbacks.
- Do not put full project serialization or file writes on the WPF UI thread. Mutations that can overlap saving must use `ProjectSaveCoordinator.BeginMutation()`.
- Do not reintroduce a second preview timer or invalidate the full static timeline merely to move the playhead.
- Preserve the FFmpeg process concurrency gate, bounded output capture, deterministic exact-preview chunks, and cache limits.
- Run the performance workload generator and targeted benchmarks when changing timeline, animation, preview, persistence, thumbnail, or FFmpeg hot paths.
- Add or update targeted tests for behavior changes.
- For visual/editor/export changes, also produce or verify real manual-review artifacts when appropriate.

## 7. Fast Task Routing

Use this routing before searching broadly:

- Timeline behavior or undo/redo: `Rushframe.Domain/Editing`, then `TimelineControl`
- Project schema or serialization: `Rushframe.Domain/Project.cs`, related models, `Serialization/ProjectSerializer.cs`, `ProjectRepository.cs`
- Media import/relink/library: `MainWindow.Media.cs`, `MediaAsset.cs`, media service
- Preview behavior: `MainWindow.Preview.cs`, `MainWindow.RealtimePreview.cs`, and `MainWindow.PreviewInteraction.cs`
- Inspector/effects/color/stabilization: `MainWindow.Inspector.cs`, domain effect types, `EffectRegistry`
- Export/render: `ExportController.cs`, `FfmpegMediaService.cs`, export dialog
- Agent editing: `LocalAgentBridgeService.cs`, `AgentEditCommandFactory.cs`, `AgentAuditLogService.cs`, domain edit commands
- Animation/keyframes: `Keyframe.cs`, `UpdateAnimationChannelsCommand.cs`, `AnimationGraphControl.cs`, `AnimationEditorDialog.cs`
- Canvas/guides: `CanvasPresentation.cs`, `UpdateSequenceSettingsCommand.cs`, `CanvasSettingsDialog.cs`, `MainWindow.Canvas.cs`
- Creative assets/extensions: `CreativeAssetPackService.cs`, `ExtensionManifestService.cs`, `CreativeAssetsDialog.cs`, `MainWindow.Assets.cs`
- Campaign description/tasks: `Project.cs`, `CampaignTask.cs`, task UI in desktop files
- Python analysis: `rushframe_intelligence/pipeline.py`, then the specific analyzer
- Intelligence import/search: application services plus desktop backend service
- Autosave/recovery: `AutosaveService.cs`, `ProjectRepository.cs`, project partial
- Legacy files: `LegacyImporter.cs`
- Workspace/panels: `Workspace/*`, `Panels/*`
- Native pixel operations: `Rushframe.Native.Interop` and `native/Rushframe.Native`
- QA expectations: `qa testing/QA_TESTING_PLAN.md`, execution results, and manual-review README files

## 8. Build and Verification

Basic commands:

```powershell
dotnet build Rushframe.slnx
dotnet test Rushframe.slnx
dotnet run --project src/Rushframe.Desktop
```

Python intelligence setup:

```powershell
.\scripts\setup-intelligence.ps1
```

Advanced optional intelligence dependencies:

```powershell
.\scripts\setup-intelligence.ps1 -Advanced
```

Prefer the smallest relevant verification first. Run the full suite when a change crosses layers or affects shared behavior.

## 9. Current Working-Tree Warning

At the time this context was updated, the repository still had active uncommitted work, including the OpenCut parity upgrade:

- modified application, desktop, domain, and media files
- newly added desktop partial files, controllers, dialogs, and `TimelineItemCloner.cs`
- an untracked `qa testing` directory
- tracked `migration_plan` files deleted from the working tree
- generated `.obj-check` / `.objcheck` directories present

Do not assume these changes are mistakes. Re-check `git status` and preserve them unless the user explicitly asks otherwise.

## 10. Context Maintenance

Update this file when any of the following changes materially:

- project/module architecture
- canonical project model
- agent permission/version workflow
- export/review workflow
- major entry points
- folder ownership
- build commands
- non-negotiable product constraints

Keep this document architectural and durable. Do not fill it with temporary logs, one-off bugs, or full code dumps.
