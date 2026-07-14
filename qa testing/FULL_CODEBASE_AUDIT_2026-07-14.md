# Rushframe Full Codebase Audit — 2026-07-14

## Verdict

**FAIL — not release-ready.**

The current checkout builds and all existing managed tests pass, but the audit confirmed release-blocking defects in save consistency, edit atomicity, lock enforcement, agent approvals, render reproducibility, preview/export parity, and required product workflows.

The current dirty working tree was audited as-is. Existing modified, deleted, and untracked files were preserved.

## Scope

Reviewed the architecture and execution paths across Domain, Application, Infrastructure, Media, Desktop/WPF, native interop/C++, Python intelligence/MCP, tests, and QA evidence.

Approximate inspected inventory: 182 C# files, 25 Python files, 2 XAML files, 2 C++ files, 1 native header, and about 34,668 source lines.

## P0 — Release blockers

### AUD-P0-001 — Explicit save can mark unsaved newer edits clean

`MainWindow.Project.cs:149-172` clears `_projectDirty` after awaiting a revision-scoped snapshot save. Because edits remain possible during the await, revision R can be saved while R+1 is marked clean. Closing can then lose R+1 without warning.

**Fix:** clear dirty state only when the current revision equals the coordinator's last explicitly saved revision; otherwise preserve dirty state and coalesce another save.

### AUD-P0-002 — Serialization mutates the live project on a worker thread

`ProjectSerializer.cs:19-26` changes schema/defaults/providers/variants/overview. `ProjectSaveCoordinator.cs:218-251` invokes it through `Task.Run` against the live project.

This bypasses `BeginMutation`, revision increments, undo, and WPF thread ownership. Serialization must operate on an immutable clone/snapshot and never modify the live model.

### AUD-P0-003 — Manual overwrite edit is not atomic

`MainWindow.Preview.cs:840-912` directly creates a track, deletes overlaps through separate `Execute` calls, then adds the replacement through another command.

A later locked-item or add failure can leave earlier deletions committed. One overwrite creates multiple undo entries and revisions. Prevalidate all targets and execute one composite command.

### AUD-P0-004 — Locked tracks are mutable through track commands

`TrackCommands.cs:33-210` does not reject locked tracks for delete, rename, reorder, duplicate, mute, or solo. `AgentEditPlanCompiler.cs:122-128` explicitly permits rename/reorder on locked tracks.

All mutating track commands except lock-toggle must enforce the lock before changing state.

### AUD-P0-005 — Ripple operations move individually locked clips

`RippleDeleteClipCommand.cs:17-43`, `TrimClipCommand.cs:19-59`, and `MoveClipCommand.cs:19-64` validate the selected item but shift downstream items without checking their lock state.

Commands must resolve and validate every affected item before mutation.

### AUD-P0-006 — Failed undo/redo destroys history

`UndoRedoStack.cs:25-52` removes the command from the source stack before calling Undo/Execute. A failure or exception leaves it on neither stack.

Keep history entries in place until successful completion and define safe exception/partial-failure handling.

### AUD-P0-007 — Composite edits are not reliably atomic

`CompositeEditCommand.cs:15-44` does not catch subcommand exceptions, ignores rollback failures, and can partially undo before returning a failure.

Add robust prevalidation, exception handling, verified rollback, and exact snapshots for recovery.

### AUD-P0-008 — Agent approval is controlled by the caller

`MainWindow.xaml.cs:3015-3021`, `3182-3215`, and `3273-3278` skip approval when the payload supplies `require_approval:false`. The Python MCP schema exposes that field.

Approval policy must be editor-owned and tied to a user-authorized session or signed one-time plan grant, not a caller boolean.

### AUD-P0-009 — Render and receipt state are not revision-frozen

`ExportController.cs:67-97`, agent render paths, and `RenderReceiptService.cs:100-131` use live mutable Project/Sequence objects during asynchronous render and verification. Normal editing and undo/redo are not globally blocked by `_isMediaOperationRunning`.

Output, project graph, receipt revision, and evidence can describe different states. Render from an immutable revision snapshot and commit receipt/job metadata afterward in one coordinated mutation.

### AUD-P0-010 — Undoing an agent plan leaves it recorded as applied

`MainWindow.xaml.cs:3023-3053` puts only the timeline command in undo history, then adds `AgentEditPlanRecord` and workflow changes outside the command (`3069-3082`). Undo can restore the timeline while plan/workflow status remains Applied/AwaitingApproval.

Plan and workflow state need an undo-aware state transition or compensating Undone record.

## P1 — High-priority defects and missing workflows

### AUD-P1-001 — Manual automatic-track creation bypasses commands

Direct track additions exist in `MainWindow.Preview.cs:860-870`, `MainWindow.Assets.cs:199-211`, `MainWindow.xaml.cs:3696-3703`, and `4350-4361`. Creative-asset insertion can also register an asset and increment revision separately before adding the item.

Use prepared-track/project commands and one revision per logical action.

### AUD-P1-002 — Track Solo is ignored by realtime preview and export

`RealtimeRenderPlan.cs:56-94` and `FfmpegTimelineRenderer.cs:318-395` filter hidden/muted tracks but never apply Solo. The UI can show a soloed track while all tracks still render/play.

### AUD-P1-003 — Track reorder does not change renderer order

`ReorderTrackCommand` changes list position but render paths sort by `Track.Order`. Duplicate track can also create duplicate order values.

Use one canonical ordering source and normalize it after every track mutation.

### AUD-P1-004 — AddClipCommand does not validate track compatibility

`AddClipCommand.cs:10-19` checks existence/lock only. Agent add paths can target any requested unlocked track.

Centralize item/track/media compatibility validation in the command.

### AUD-P1-005 — Delete/split do not maintain transition integrity

`DeleteClipCommand.cs`, `RippleDeleteClipCommand.cs`, and `SplitClipCommand.cs` do not remove, reattach, or restore transitions referencing affected items. Projects can retain dangling or semantically wrong transition references.

### AUD-P1-006 — Media-intelligence undo changes ordering

`ApplyMediaIntelligenceCommand.cs:125-175` restores removed markers, tracks, and items by appending rather than restoring original indices.

### AUD-P1-007 — Group paste silently omits items

`MainWindow.xaml.cs:3949-3973` skips clipboard items with no compatible unlocked target and pastes the rest without warning.

Validate all destinations first and reject or explicitly approve partial paste.

### AUD-P1-008 — Local bridge request bounds and concurrency are incomplete

`LocalAgentBridgeService.cs:56-113` checks declared Content-Length, then reads the entire body; unknown/chunked lengths are not bounded by the declared-size check. It also starts one unbounded task per accepted request and does not await in-flight shutdown.

Use a bounded stream, bounded queue/semaphore, and graceful shutdown.

### AUD-P1-009 — Output containment is only lexical

`MainWindow.xaml.cs:2775-2789` uses normalized string prefix checks and direct UNC checks. It does not validate mapped network drives or filesystem reparse-point/junction escapes.

Resolve and validate the real parent path/drive before creating output.

### AUD-P1-010 — Python backend can expose unauthenticated read routes

`backend.py:177-204` serves `/search`, `/context`, and `/capabilities` without the session check used by POST `/mcp`. `serve`/CLI accepts arbitrary host binding (`backend.py:393-399`, `worker.py:62`).

Enforce loopback-only operation or authenticate every non-health endpoint.

### AUD-P1-011 — Intelligence tools accept arbitrary local analysis paths

`backend.py:39-67` and `304-332` accept absolute paths to any analysis JSON or context SQLite database rather than project-authorized asset/analysis identifiers.

Restrict access through an open-project registry owned by the desktop bridge.

### AUD-P1-012 — MCP schema and desktop actions have drifted

`backend.py:32-137` exposes only a small action subset while `AgentEditCommandFactory.cs:20-33` supports many more actions. Direct edit schema also allows additional properties.

Generate schemas from shared typed contracts and test public/private action coverage.

### AUD-P1-013 — Campaign description and task workflow are absent

`Project.cs:15-16` contains persisted fields, but no desktop UI, undoable project commands, agent state, or task completion workflow references them. Only a persistence test uses them.

This required brief/task/agent-context workflow is not implemented.

### AUD-P1-014 — Subtitle and font support is nominal only

`GetMediaKind` recognizes `.srt`, `.vtt`, `.ttf`, and `.otf` (`MainWindow.xaml.cs:4382-4389`), but the import dialog excludes them (`MainWindow.Media.cs:22-28`) and no parse/apply workflow exists. The main import filter also excludes formats accepted by relink.

### AUD-P1-015 — Intelligence output/cache publication is non-atomic

`pipeline.py:297-354` writes analysis, summary, sidecars, manifest, and SQLite index sequentially in-place. A failure can leave a manifest-valid but incomplete bundle.

Publish a complete temporary bundle atomically and write completion metadata last.

### AUD-P1-016 — Variant/composition render status is outside coordinated persistence

`MainWindow.Automation.cs:442-475` uses `CancellationToken.None`, mutates variant status directly, and does not persist failure through a revision/dirty mutation. External composition service similarly mutates project-owned specs throughout asynchronous execution.

Keep runtime state separate and commit final persistent state once, with cancellation propagated.

### AUD-P1-017 — Domain layer performs filesystem I/O

`ProjectOverview.cs:322-323`, `388-413`, and `471-476` call `File.Exists` while building domain overview data, which serialization invokes.

This violates dependency direction, makes output machine-dependent, and can block save snapshots on unavailable paths. Move filesystem health checks to Infrastructure/Application.

## P2 — Medium-priority gaps

1. `DeleteMarkerCommand` undo appends instead of restoring original marker order (`MarkerCommands.cs:72-95`).
2. Migration output can use current time for missing timestamps, reducing determinism (`ProjectMigrationPipeline.cs`).
3. Deserialization lacks a complete invariant validation/repair pass for dimensions, time, speed, IDs, compatibility, ordering, and references.
4. FFmpeg embedded-audio probing uses a broad catch and can silently omit audio (`FfmpegTimelineRenderer.cs:394-409`).
5. Realtime preview setup catches all failures and silently falls back, hiding parity defects (`MainWindow.RealtimePreview.cs:29-72`).
6. Required `qa testing/design-reference/` and `qa testing/ui-automation/` directories are missing.
7. Native C++ is outside `Rushframe.slnx`, has no automated tests, and could not be built on this machine because no C++ toolchain/Visual Studio installation was available.
8. No repository-wide warnings-as-errors, explicit analyzer policy, architecture gate, or enforced coverage threshold was found.
9. Python requirements are mostly unpinned with no lock/hashes. The managed venv is dependency-consistent but lacks pytest; the launcher can fall back to the machine Python environment, whose dependency check currently fails.
10. QA test-count records are stale: QA results say 191, local instructions say 185, current Release suite has 230.
11. Autosave retention is global (10 files / 256 MiB), so activity across projects can evict another project's recovery file.
12. Desktop orchestration is highly concentrated in `MainWindow.xaml.cs` and partials, increasing boundary violations and regression risk.

## Agent state completeness

`BuildAgentTimelineState` omits important planning/protection data including track hidden state, item lock state, full transform/crop/blend/audio/speed/mask/chroma-key state, render warnings, campaign description, and tasks. Agents cannot fully inspect the state that controls correctness.

## Verification performed

### Passed

- Debug build: `dotnet build Rushframe.slnx --nologo` — 0 warnings, 0 errors.
- Debug tests: 230 passed.
- Release build: `dotnet build Rushframe.slnx -c Release --nologo` — 0 warnings, 0 errors.
- Release tests: Desktop 64, Domain 146, Legacy 7, Media 13 — total 230 passed.
- Python tests through machine Python: 5 passed.
- NuGet vulnerability query: no vulnerable packages reported by configured sources.
- Startup smoke: successful process launch/exit; managed memory 7,253,944 bytes; GC 3/3/2.
- Managed intelligence venv `pip check`: no broken requirements.

### Blocked/incomplete

- Managed-venv Python test run: blocked because pytest is not installed in `.tools/intelligence-venv`.
- Native C++ configure/build: blocked because no NMake/C++ compiler or Visual Studio 2022 installation was available.
- Full interactive editor workflow, physical audio output, and DPI/resize visual-reference validation were not executed.

## Missing regression coverage for release blockers

The current suite does not cover save/edit races, serializer purity, failed undo/redo history preservation, composite rollback exceptions, locked ripple targets, locked track commands, Solo rendering, rendered track reorder, transition cleanup, approval bypass, immutable render snapshots, receipt/source-revision matching, bounded bridge streaming/concurrency, contained real paths, campaign/task workflows, subtitle/font workflows, or atomic intelligence bundles.

## Remediation order

1. Save consistency, serializer purity, immutable render snapshots.
2. Undo/composite atomicity and complete lock enforcement.
3. Command-only manual mutations and one revision per logical edit.
4. Editor-owned agent approvals and bridge/path hardening.
5. Solo/order/transition/compatibility renderer correctness.
6. Campaign/tasks, subtitles/fonts, and complete agent state.
7. Native/visual automation, analyzers, coverage gates, pinned Python environments, and current QA evidence.

## Files changed by this audit

- Added `qa testing/FULL_CODEBASE_AUDIT_2026-07-14.md`.
- No application, test, build, or project source files were modified.

## Final result

**FAIL**

The managed build is green, but confirmed P0 defects can cause data loss, partial edits, lock bypass, unapproved agent actions, and non-reproducible renders. Native verification is separately blocked, but the current release decision is already FAIL.