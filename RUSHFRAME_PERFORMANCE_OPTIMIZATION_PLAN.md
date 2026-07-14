# Rushframe Performance Optimization Plan

**Audit date:** 2026-07-12  
**Repository:** `C:\Users\LENOVO\Desktop\Projectsss\Rushframe`  
**Primary goal:** Remove editor lag without weakening preview correctness, undo/redo, project revision safety, local-only media rules, or FFmpeg export fidelity.

---

## Implementation status — completed 2026-07-12

The code implementation described by this plan is complete in the current working tree.

Completed areas:

- Static timeline rendering is separated from the playhead transport overlay.
- Timeline duration, visible-item queries, hit testing, snapping, markers, and transition slots use revision-scoped indexes.
- Playback uses one throttled frame scheduler instead of overlapping render and timer loops.
- Real-time preview uses a revision-scoped interval render plan, incremental layer lifetime, cached transforms/effects, lookahead warming, and a media-player safety threshold.
- Animation-channel lookup is cached, ordered once per mutation, cursor-assisted, and binary searched with zero steady-state managed allocations in the benchmark.
- Normal editing no longer performs project file I/O. Project snapshots are serialized off-thread, protected by a mutation epoch, revision checked, debounced, coalesced, and written through one async writer.
- Media filtering is debounced and incremental; thumbnails use a bounded asynchronous LRU cache.
- Exact FFmpeg preview is range rendered into deterministic six-second chunks with in-flight deduplication, corruption checks, atomic replacement, and bounded disk cleanup.
- FFmpeg execution uses `ArgumentList`, bounded diagnostics, process-tree cancellation, concurrency control, metadata probe caching, streaming waveform extraction, and filter-script fallback for large graphs.
- Startup work for fonts, optional capabilities, and recovery is deferred. Audit-log reads and rotation are bounded.
- Python cache validation uses a sampled fast fingerprint before full SHA-256, Whisper and embedding models are cached, vectors use compact float BLOBs, semantic candidate scans are bounded, and backend request concurrency is capped.
- Performance telemetry, deterministic workload generation, trace scripts, memory-dump scripts, startup smoke automation, allocation tests, and BenchmarkDotNet projects are included.

Verification completed after implementation:

- Release build: **passed**, 0 warnings, 0 errors.
- .NET tests: **170 passed**, 0 failed.
- Python tests: **5 passed**, 0 failed.
- Isolated WPF startup/shutdown smoke: **passed**, exit code 0, performance snapshot written.
- `git diff --check`: **passed**; only repository line-ending conversion notices were emitted.
- BenchmarkDotNet animation lookup:
  - sequential lookup: approximately **170 ns/op**, **0 B/op**;
  - reverse lookup: approximately **194 ns/op**, **0 B/op**.
- BenchmarkDotNet duration hot path reported **0 B/op** for both cached and invalidated cases.
- Six deterministic workload projects were generated successfully: 50, 500, and 1,200-clip timelines; animation; audio; and exact-preview workloads.

The remaining acceptance work is hardware- and perception-dependent rather than unfinished code: capture ETW/GPU traces on the fixed baseline machine, run the ten-minute retained-memory scenario, and complete real preview-versus-export visual/audio review. The repository now contains scripts, workloads, telemetry, and gates for those runs under `qa testing/performance`.

---

## 1. Audit scope and confidence

This plan is based on a repository-wide static audit of the current working tree and targeted deep inspection of the performance-sensitive runtime paths.

Reviewed areas:

- WPF application startup, layout, event wiring, and editor shell
- Custom timeline rendering, hit testing, snapping, selection, waveform rendering, and transitions
- Source preview, real-time layered preview, exact FFmpeg preview fallback, and preview interaction
- Domain animation evaluation, sequence duration, edit commands, cloning, and serialization
- Project save, autosave, recovery, settings, workspace layout, and audit logs
- Media import, thumbnails, waveform generation, probing, proxy generation, FFmpeg execution, and timeline export
- Python media-intelligence pipeline, source cache, transcription, SQLite context search, and local backend
- Existing tests, QA artifacts, project structure, source size, and build state

Repository snapshot observed during the audit:

- Approximately 280 non-generated project files
- 144 C# files
- 2 large XAML files
- 25 Python files
- 4 native C/C++ files
- Approximately 28,562 lines across C#, XAML, Python, and native source
- The working tree already contained extensive uncommitted changes. This document does not reset, rewrite, or reinterpret those changes; findings reflect the current filesystem.

Verification completed:

- `dotnet build Rushframe.slnx -c Release --no-restore`
  - Passed
  - 0 warnings
  - 0 errors
  - Approximately 21 seconds
- `dotnet test Rushframe.slnx -c Release --no-build --no-restore`
  - 155 tests passed
  - 0 failed
  - Approximately 23 seconds
- Python intelligence tests were not executed because `.tools/intelligence-venv` does not currently contain `pytest`.

Important limitation:

- No ETW, PerfView, WPA, GPUView, `dotnet-trace`, or live frame-time capture was available during this static audit. The code contains several high-confidence lag sources, but baseline and after-change numbers must be captured before claiming measured improvements.

---

## 2. Executive diagnosis

Rushframe is laggy primarily because too much work is performed synchronously on the WPF UI thread and too much data is recomputed or reallocated every preview frame.

The highest-impact problems are:

1. **Playback redraws the entire custom timeline at render-frame frequency.**
   - Updating only the playhead triggers `TimelineControl.InvalidateVisual()`.
   - `OnRender` then scans tracks and clips, recomputes duration, formats text, creates brushes/pens/geometries, draws waveforms, and rebuilds transition visuals.

2. **The real-time preview compositor performs repeated whole-sequence queries and allocations every frame.**
   - It uses repeated LINQ, arrays, hash sets, transition searches, media-library searches, transform creation, effect creation, and duplicated audio updates.
   - A second 100 ms timer performs overlapping preview updates while `CompositionTarget.Rendering` is active.

3. **Animation evaluation sorts and allocates on every property lookup.**
   - Every animated property call scans channels, sorts keyframes with `OrderBy(...).ToList()`, then linearly searches for a segment.
   - Preview invokes this repeatedly for every active visual and audio layer.

4. **Every successful edit synchronously serializes and writes the project twice.**
   - One write updates the current project file.
   - A second write creates the autosave.
   - Both happen from the UI thread after each command, including drag/trim/edit bursts.
   - A separate background autosave can concurrently serialize the same mutable project.

5. **Media-list filtering and refresh synchronously recreates all view models and reloads thumbnails.**
   - Search refresh runs on every keystroke.
   - The list is cleared and rebuilt.
   - Thumbnail files are synchronously opened and decoded again.

6. **Unsupported real-time content causes a full-timeline FFmpeg preview render.**
   - Any relevant edit invalidates the complete preview.
   - Preview files use random names and are not visibly bounded or cleaned.
   - There is no range/chunk cache or dependency-aware invalidation.

7. **Several startup operations are synchronous and eager.**
   - Asset/extension manifest scans, audit-log reads, system font enumeration, autosave recovery, and general UI composition all happen before or during initial window setup.

These are architectural hot paths, not cosmetic micro-optimizations. Addressing the first four should produce the largest perceived improvement.

---

## 3. Performance targets

Use these targets as release gates after measuring the current baseline on a fixed test machine.

### 3.1 UI responsiveness

| Scenario | Target |
|---|---:|
| Timeline drag/trim input-handler P95 | `< 8 ms` |
| Any normal editor input-handler P95 | `< 16 ms` |
| Long UI-thread stalls during normal editing | No stall `> 100 ms` |
| Inspector selection change P95 | `< 50 ms` |
| Media search response after debounce | `< 100 ms` for 5,000 assets |
| Explicit project save feedback | UI remains interactive; no synchronous full-file write |

### 3.2 Preview

| Scenario | Target |
|---|---:|
| Draft preview on baseline machine | Stable `30 fps` |
| Full-quality preview where supported | Stable at sequence FPS when hardware allows |
| Preview frame-time P95 at 30 fps | `< 33.3 ms` |
| Dropped frames in a 60-second representative playback | `< 2%` |
| Clip-boundary activation stall | `< 50 ms`, ideally no visible pause |
| Play/pause response | `< 100 ms` |
| Seek response for cached/proxy media | `< 150 ms` |

### 3.3 Timeline scale

Test at minimum:

- 20 tracks
- 1,000 clips
- 100 markers
- 100 transitions
- 25 visible audio clips with waveform data
- 8 animated visible clips

Targets:

- Pan and zoom remain visually smooth.
- Playhead movement does not redraw static clip content.
- Hit testing and snapping remain below 4 ms P95.
- Timeline allocations during steady playback approach zero per frame.

### 3.4 Persistence

| Scenario | Target |
|---|---:|
| Command execution excluding domain mutation | No project file I/O |
| Autosave scheduling overhead on UI thread | `< 2 ms` |
| Duplicate save of same revision | Never |
| Save queue | One active writer, coalesced pending revisions |
| Recovery consistency | Saved snapshot always represents one complete project revision |

### 3.5 Memory and startup

| Scenario | Target |
|---|---:|
| Warm startup to interactive window | `< 2 s` on baseline machine |
| Cold startup to interactive window | `< 4 s` on baseline machine |
| Ten-minute looping preview memory growth | `< 10%` after warm-up |
| Preview cache | Configurable byte cap with LRU cleanup |
| Thumbnail cache | Bounded by count/bytes and releases unused images |

Targets should be adjusted after the first baseline capture, but regressions must be prevented with fixed budgets.

---

## 4. Ranked findings

| ID | Priority | Finding | Main files | User-visible effect |
|---|---|---|---|---|
| PERF-001 | P0 | Full timeline redraw for every playhead update | `TimelineControl.cs`, `MainWindow.Preview.cs`, `MainWindow.RealtimePreview.cs` | Playback and scrubbing stutter |
| PERF-002 | P0 | Duplicate preview schedulers and repeated per-frame compositor work | `MainWindow.xaml.cs`, `MainWindow.Preview.cs`, `MainWindow.RealtimePreview.cs` | High CPU, uneven frame pacing |
| PERF-003 | P0 | Animation channels sort and allocate on every lookup | `Keyframe.cs`, `TimelineItem.cs` | Frame-time spikes with animated clips |
| PERF-004 | P0 | Full synchronous project serialization and two writes after every edit | `MainWindow.Project.cs`, `MainWindow.xaml.cs`, `AutosaveService.cs`, `ProjectRepository.cs` | Drag/trim/apply edits freeze briefly |
| PERF-005 | P0 | Repeated sequence, transition, item, and media-library scans per frame | `Sequence.cs`, `MainWindow.RealtimePreview.cs`, `TimelineControl.cs` | Performance degrades rapidly as projects grow |
| PERF-006 | P1 | Layer rebuild clears all WPF media elements at active-set changes | `MainWindow.RealtimePreview.cs` | Clip-boundary playback stalls |
| PERF-007 | P1 | New transforms, effects, brushes, pens, formatted text, and geometries created repeatedly | Timeline and preview files | GC pressure and frame-time jitter |
| PERF-008 | P1 | Whole-timeline exact preview render with unbounded random cache files | `MainWindow.Preview.cs`, FFmpeg renderer | Long preview waits and disk growth |
| PERF-009 | P1 | Media list clear/rebuild and synchronous thumbnail reload on each search change | `MainWindow.xaml.cs` | Media-panel typing and scrolling lag |
| PERF-010 | P1 | Full-resolution image decode for preview layers | Preview files | High memory usage and decode stalls |
| PERF-011 | P1 | Waveform extraction buffers full decoded PCM and duplicates it with `ToArray()` | `FfmpegMediaService.cs` | High memory use on long audio |
| PERF-012 | P1 | FFmpeg runner captures complete output and builds long command strings | FFmpeg service/renderer | Memory growth, quoting risk, long-command limits |
| PERF-013 | P2 | Eager startup scans and full audit-log read | `MainWindow.xaml.cs`, `AgentAuditLogService.cs` | Slow startup |
| PERF-014 | P2 | Python cache hashes entire source before cache validation | `rushframe_intelligence/cache.py` | Large-media analysis startup delay |
| PERF-015 | P2 | Python semantic search loads model per query and scans/deserializes every vector | `context_index.py` | Slow repeated context searches |
| PERF-016 | P2 | Whisper model is constructed for each transcription call | `transcriber.py` | Repeated model-loading delay and memory churn |
| PERF-017 | P2 | No first-class runtime performance telemetry or automated performance gates | Entire solution/QA | Regressions are difficult to detect |
| PERF-018 | P3 | Large composition-root and timeline classes make hot-path ownership unclear | `MainWindow*.cs`, `TimelineControl*.cs` | Optimizations are harder and riskier |

---

## 5. Detailed findings and required changes

## 5.1 Timeline rendering

### Current behavior

`TimelineControl.PlayheadTime` invalidates the whole control whenever the playhead changes. During playback, this can happen once per compositor frame. `TimelineControl.OnRender` then:

- synchronizes viewport state;
- recomputes sequence duration by scanning all items;
- draws the entire background and ruler;
- draws every track header;
- loops every track and every clip before culling;
- resolves clip labels through a linear media-library search;
- creates text, brushes, pens, gradients, geometries, and typefaces;
- draws up to 180 waveform line segments per visible audio clip;
- sorts items and scans transitions to generate transition slots;
- redraws markers, selection, drag ghost, snap guides, and playhead.

### Required architecture

Split the timeline into separate rendering layers:

1. **Static/content layer**
   - track backgrounds;
   - ruler grid and labels;
   - clip bodies and names;
   - waveforms;
   - transition handles;
   - markers.

2. **Interaction layer**
   - selection box;
   - drag ghosts;
   - trim handles;
   - snapping guides.

3. **Transport/playhead layer**
   - playhead line and time badge only.

The playhead layer must move using a lightweight transform or an adorner and must not invalidate the static content layer.

### Concrete tasks

- Add a `TimelineRenderCache` or `TimelineScene` keyed by:
  - project revision;
  - sequence ID;
  - viewport dimensions;
  - horizontal/vertical offset;
  - zoom bucket;
  - theme/resource version.
- Cache and freeze reusable WPF resources:
  - brushes;
  - pens;
  - typefaces;
  - static geometries;
  - gradient clip brushes.
- Replace per-frame `FormattedText` creation for unchanged labels with cached text drawings keyed by text, width bucket, font, and DPI.
- Cache asset display names in `Dictionary<MediaAssetId, string>` rather than `FirstOrDefault` for each clip render.
- Build per-track arrays sorted by timeline start once per revision.
- Find the first visible clip using binary search, then render until the viewport end.
- Cull vertically before drawing track headers and track content.
- Precompute transition adjacency and transition lookup dictionaries once per revision.
- Cache waveform drawings by asset ID, clip width/zoom bucket, source range, and gain-display mode.
- Coalesce multiple invalidation requests into one dispatcher/render pass.
- Do not call `InvalidateVisual()` when a property value has not materially changed.
- Move `UpdateSequenceDuration()` out of `OnRender`.
- Replace `HitTestMarker` LINQ allocation with a sorted marker array and nearest-neighbor search.
- Replace snapping scans with a sorted snap-point index that is rebuilt only after relevant edits.
- Add a fast path for steady playback where only the playhead overlay changes.

### Acceptance tests

- During steady playback, static timeline render count stays near zero.
- Timeline allocation rate remains approximately flat while only the playhead moves.
- 1,000-clip project pans and zooms without scanning all clips for each frame.
- Waveform display remains visually equivalent at supported zoom levels.

---

## 5.2 Preview scheduling and frame pacing

### Current behavior

Rushframe starts a 100 ms `DispatcherTimer` unconditionally. Playback also subscribes to `CompositionTarget.Rendering`.

For real-time preview, both paths call the real-time progress/frame update. This creates overlapping update loops and inconsistent frame pacing.

### Required change

Introduce one `PreviewFrameScheduler` that owns all playback ticks.

Responsibilities:

- subscribe to `CompositionTarget.Rendering` only while real-time playback is active;
- use `RenderingEventArgs.RenderingTime` or a monotonic clock;
- enforce the desired output cadence, such as sequence FPS or a draft 30 fps cap;
- skip duplicate callbacks in the same frame interval;
- update transport text at a lower rate than visual frames, such as 10 Hz;
- update the timeline playhead overlay independently from static timeline content;
- stop all callbacks while idle, paused, minimized, hidden, or app-deactivated when appropriate.

### Concrete tasks

- Remove the always-running timer or use it only for low-frequency idle transport labels.
- Never render the same real-time frame from both the timer and `CompositionTarget.Rendering`.
- Track:
  - requested frame number;
  - rendered frame number;
  - render duration;
  - dropped/skipped frames;
  - scheduler drift.
- Add adaptive draft mode when recent frame-time P95 exceeds the frame budget.
- Prevent re-entrant frame rendering with a single-frame gate.
- Skip UI text/slider assignments when the displayed value has not changed enough to be visible.

### Acceptance tests

- Exactly one preview-frame update source is active during playback.
- Timer count and event subscriptions return to zero after stop/window close.
- Frame pacing has no periodic 100 ms spikes caused by the secondary timer.

---

## 5.3 Real-time preview render plan

### Current behavior

`RenderRealtimeTimelineFrame` performs repeated whole-project work every frame:

- filters and sorts tracks;
- expands all track items;
- allocates active arrays and hash sets;
- searches transitions repeatedly;
- searches media assets repeatedly;
- clears and rebuilds all layers when the active ID set changes;
- updates players once in the main loop and again through `ApplyRealtimeAudioSettings`;
- creates transform/effect objects repeatedly.

### Required architecture

Create a revision-scoped `RealtimeRenderPlan`.

It should contain:

- ordered render tracks;
- item lookup by ID;
- media lookup by asset ID;
- incoming/outgoing transition lookup by item ID;
- active intervals for clips and transitions;
- cached animation-channel accessors;
- audio routing information;
- supported-feature decision and fallback reason;
- proxy/preview media paths;
- sequence duration and canvas information.

The plan is rebuilt only when a command changes a relevant part of the sequence.

### Concrete tasks

- Replace `FirstOrDefault` transition scans with dictionaries.
- Replace project media-library scans with a dictionary.
- Replace all-item active filtering with a time-indexed structure:
  - sorted interval starts/ends;
  - sweep cursor for forward playback;
  - binary-search reset for seeking.
- Diff active layers incrementally:
  - add newly active layer;
  - remove expired layer;
  - preserve unaffected media elements.
- Do not clear the entire preview canvas at every clip boundary.
- Create `TransformGroup`, `ScaleTransform`, `RotateTransform`, clip geometry, and supported effects once per layer; mutate their properties.
- Cache parsed colors and brushes.
- Remove the duplicated player/audio update pass.
- Update audio settings only when:
  - frame-local animation requires it;
  - global volume/mute changes;
  - an audio layer activates;
  - playback speed changes.
- Keep one `MediaElement` per needed active source, with an optional small warm pool for near-future clips.
- Pre-open the next clip shortly before its activation boundary.
- Add a maximum number of simultaneously active WPF media players; fall back to exact/proxy composition when exceeded.
- Render preview at a viewport-appropriate resolution rather than always using full sequence dimensions.

### Longer-term recommendation

WPF `MediaElement` is convenient but not an ideal deterministic multi-layer editing engine. After the P0/P1 optimizations, evaluate a dedicated decode/compositor path using the existing native/media bridge or another locally owned media engine. Do not start with this rewrite; first prove whether the optimized current architecture meets the target.

---

## 5.4 Animation evaluation

### Current behavior

`AnimationChannel.GetValueAt` sorts keyframes into a new list on every call and linearly searches for the active segment. `TimelineItem.GetAnimationChannel` linearly scans channels by property name.

### Required change

Make animation evaluation zero-allocation and index-based.

### Concrete tasks

- Keep keyframes sorted when they are created, pasted, edited, migrated, or deserialized.
- Validate duplicate or non-monotonic keyframe times at mutation boundaries.
- Use binary search to locate the active keyframe segment.
- Cache a property-name-to-channel lookup per timeline item.
- In the preview layer, cache channel references rather than resolving names every frame.
- For forward playback, cache the last segment index and advance it monotonically.
- Invalidate channel indexes only when keyframes change.
- Preserve Bezier behavior, but run Bezier solving only after locating the correct segment without allocation.
- Consider a small precomputed lookup table for Bezier easing only after profiling proves the solver itself remains significant.

### Required benchmarks

Benchmark `GetValueAt` for:

- no keyframes;
- 2 keyframes;
- 10 keyframes;
- 100 keyframes;
- linear interpolation;
- Bezier interpolation;
- sequential playback queries;
- random seeks.

Target: no allocations in the steady-state lookup path.

---

## 5.5 Cached sequence state and indexes

### Current behavior

`Sequence.Duration` scans every item each time it is read. Timeline rendering independently scans the sequence to calculate duration. Preview methods read duration repeatedly during every frame.

### Required change

Introduce revision-aware derived state.

### Derived state to cache

- sequence duration;
- ordered tracks;
- item-by-ID lookup;
- item-to-track lookup;
- media-by-ID lookup at project level;
- incoming/outgoing transitions;
- sorted markers;
- snap points;
- per-track sorted clips;
- visible interval indexes;
- active audio/visual intervals.

### Invalidation

Prefer edit-impact metadata instead of blindly invalidating everything.

Each command should indicate categories such as:

- timing changed;
- visual property changed;
- audio property changed;
- media membership changed;
- transition changed;
- marker changed;
- metadata-only changed;
- preview exactness changed.

This enables targeted rebuilding and exact-preview range invalidation.

---

## 5.6 Project save and autosave

### Current behavior

After every successful edit, `Execute` calls `SaveAutosaveSnapshot` synchronously. That method can serialize and write the current project file, then serialize and write an autosave file. A separate periodic autosave serializes the same mutable project object from a background task.

Consequences:

- UI stalls grow with project size.
- Drag or repeated commands create bursts of full-file writes.
- The same revision is serialized more than once.
- The background task can observe mutable collections during editing.
- Explicit project save semantics are mixed with autosave semantics.

### Required architecture: `ProjectSaveCoordinator`

Responsibilities:

- receive `MarkDirty(revision)` calls without writing synchronously;
- debounce autosave after edit bursts;
- maintain one active save operation;
- coalesce pending revisions to the newest revision;
- capture a consistent project snapshot;
- serialize once per revision;
- write asynchronously and atomically;
- separately support explicit user save and recovery autosave;
- expose save state and last completed revision;
- cancel and flush safely during shutdown.

### Snapshot strategy

Choose one safe strategy and document it:

1. Create an immutable persistence DTO on the UI thread, then serialize it off-thread; or
2. Serialize on the UI thread into a pooled buffer only if measurement proves it stays below the UI budget; then write off-thread; or
3. Use a reader/writer state model that guarantees no mutation during snapshot creation.

Do not serialize the live mutable domain object concurrently with edit commands.

### Save policy

- On command success: mark dirty only.
- Debounced autosave: 750–1,500 ms after the latest change.
- Periodic safety autosave: only if revision differs from last autosaved revision.
- Explicit Save: enqueue at high priority and update the chosen project path after success.
- Shutdown: await a bounded final save or ask the existing close-confirmation flow.
- Never overwrite the explicit project file automatically after every command unless that behavior is deliberately specified.

### File I/O

- Use async file streams.
- Write to a temporary file in the destination directory.
- Flush as required.
- Atomically replace/move.
- Reuse the same serialized payload when explicit and autosave targets need the same revision.
- Rotate autosaves by project, revision, count, and byte cap.
- Dispose/await cancellation tokens and background loops.

### Acceptance tests

- Rapid 100-command edit burst produces at most one or a small coalesced number of autosaves.
- No file write occurs in the command handler.
- Concurrent edit/save cannot produce a partially mixed revision.
- Closing during a save is deterministic.
- Recovery loads the newest complete snapshot.

---

## 5.7 Media library and thumbnails

### Current behavior

`RefreshMediaList` clears the WPF list and recreates every `MediaListItem`. Each item synchronously loads and decodes its thumbnail. Search triggers this on every keystroke.

### Required change

- Introduce `MediaLibraryViewModel` with persistent item view models.
- Use `ICollectionView` or a filtered observable collection.
- Debounce text search by 150–250 ms.
- Update the list incrementally for imports, relinks, and generated caches.
- Cache thumbnails by:
  - media asset ID;
  - source/cache path;
  - file size and modified timestamp;
  - requested decode size.
- Load thumbnails asynchronously with cancellation.
- Show a placeholder while loading.
- Bound the thumbnail cache by memory or item count.
- Decode only to the displayed size multiplied by current DPI.
- Avoid setting `ItemsSource`/clearing items when the effective result set has not changed.
- Preserve the existing virtualizing panel, then validate it with 5,000 and 20,000-item synthetic libraries.

### Additional task

Review `VirtualizingWrapPanel` with UI virtualization diagnostics to ensure it does not realize more items than the visible viewport plus a small cache region.

---

## 5.8 Image decode and preview memory

### Current behavior

Selected images and real-time image layers are loaded with `BitmapCacheOption.OnLoad`, but without target decode dimensions. Large originals can therefore be decoded at full resolution even when displayed in a much smaller preview viewport.

### Required change

- Determine target preview dimensions from viewport, DPI, item scale, and quality mode.
- Set `DecodePixelWidth` or `DecodePixelHeight` before `EndInit`.
- Use cached proxy images for exceptionally large sources.
- Share frozen `BitmapSource` instances between media-library and preview where dimensions match.
- Add a bounded LRU image cache.
- Track decoded bytes, cache hits, misses, and evictions.
- Release image references when project/media is removed.

---

## 5.9 Exact FFmpeg preview fallback

### Current behavior

When real-time preview cannot represent a sequence exactly, the editor renders the entire timeline to a randomly named MP4. Any project edit marks the complete preview dirty. No cleanup path for successful historical preview files was found in the inspected code.

### Required architecture: chunked exact preview cache

- Render only a window around the requested position, such as 3–8 seconds.
- Divide the sequence into stable preview chunks.
- Key each chunk by:
  - project/sequence ID;
  - relevant revision/render signature;
  - time range;
  - preview dimensions;
  - FPS;
  - quality profile;
  - FFmpeg/render-engine version.
- Record which clips/effects/transitions contribute to each chunk.
- Invalidate only intersecting chunks after an edit.
- Pre-render the next and previous chunk at low priority.
- Cancel stale renders as soon as a newer request supersedes them.
- Maintain a configurable byte cap and LRU deletion.
- Delete orphaned temporary files after failures and on startup cleanup.
- Store cache metadata separately from canonical project state.

### UX requirements

- Distinguish `Real-time`, `Draft exact`, and `Full exact` preview modes.
- Display a non-blocking rendering state.
- Allow interaction to continue while an exact chunk renders.
- Never silently show an inaccurate approximation for unsupported features.

---

## 5.10 FFmpeg process execution

### Current behavior

Most FFmpeg operations build one quoted argument string, capture complete standard output/error, and sometimes pass a large filter graph directly on the command line. Waveform peak generation buffers the complete decoded PCM stream into memory and then duplicates it with `ToArray()`.

### Required change

Create one shared `FfmpegProcessRunner`.

Responsibilities:

- use `ProcessStartInfo.ArgumentList` instead of manually quoted argument strings;
- add consistent `-hide_banner`, log-level, and statistics settings;
- parse structured progress via `-progress pipe:1` or a dedicated pipe;
- cap or stream diagnostic output instead of retaining unlimited logs;
- preserve cancellation and kill the process tree;
- expose exit code, final error summary, duration, and command category;
- redact sensitive/local paths from user-facing telemetry when appropriate;
- limit concurrent FFmpeg jobs through a media job scheduler.

### Filter graphs

- Use `-filter_complex_script` for large timeline graphs.
- Write the graph to a temporary UTF-8 file.
- delete it in `finally`;
- unit-test paths containing spaces, quotes, apostrophes, Unicode, commas, semicolons, and colons.

### Waveform peaks

Replace full-buffer decoding with streaming aggregation:

- determine duration/sample count from probe data when possible;
- read PCM chunks from stdout;
- aggregate directly into fixed peak buckets;
- retain only current bucket statistics;
- avoid `MemoryStream` plus `ToArray()` duplication.

### Probe cache

Cache probe results by canonical path, file size, and modified timestamp. Reuse them for:

- import;
- waveform generation;
- proxy decisions;
- preview setup;
- export graph creation;
- audio-stream detection.

### Optional hardware acceleration

After software-path optimization:

- probe available decoders/encoders once;
- offer hardware decode/encode as a setting;
- keep deterministic software fallback;
- validate output quality and driver failure handling;
- do not assume hardware acceleration always improves small or effect-heavy jobs.

---

## 5.11 Startup and idle work

### Current behavior

Startup eagerly scans asset/extension manifests, reads the full audit log before taking the last records, enumerates and sorts all system fonts, creates the full WPF tree, and restores autosave synchronously. The preview timer runs while idle.

### Required change

Use staged startup:

1. Construct minimal services and show the shell.
2. Make the main window interactive.
3. Load optional capabilities and non-critical data in the background.
4. Populate panels only when opened or when data becomes available.

### Concrete tasks

- Add startup marks for:
  - process entry;
  - `InitializeComponent` start/end;
  - window loaded;
  - first rendered frame;
  - first interactive input;
  - project restoration complete;
  - intelligence backend ready.
- Load system fonts lazily when the font selector is first opened.
- Scan asset/extension manifests asynchronously and cache validated manifests.
- Tail-read the last audit records rather than `File.ReadAllLines` on the entire file.
- Rotate or cap the audit log.
- Load autosave metadata first; deserialize the project after the shell is visible.
- Avoid starting timers or polling loops until needed.
- Defer intelligence backend startup unless enabled and required; display readiness asynchronously.

---

## 5.12 Python media intelligence

### 5.12.1 Source cache fingerprint

`source_checksum` reads the full media file before cache validation. For large videos, this adds substantial startup I/O even when size and modification time already show that the source is unchanged.

Change to a tiered fingerprint:

1. canonical path;
2. file size;
3. high-resolution modified timestamp;
4. partial hash of beginning/end/sample blocks;
5. full SHA-256 only when collision resistance is required or metadata changed unexpectedly.

Persist the fingerprint algorithm/version in the manifest.

### 5.12.2 Model lifetime

`transcribe` constructs `WhisperModel` for every call. Semantic search can construct `SentenceTransformer` for every query.

Required change:

- introduce process-level model caches keyed by model name/device/compute type;
- serialize access if the underlying model is not thread-safe;
- support explicit unload under memory pressure;
- report model-load time separately from inference time;
- warm the chosen model only when a job requires it.

### 5.12.3 Context-index semantic search

Current semantic search:

- reads every matching embedding row;
- deserializes JSON vectors;
- calculates cosine similarity in Python;
- sorts the entire result set.

This is acceptable only for small indexes.

Optimization path:

- Keep FTS5 as the fast primary retrieval stage.
- Restrict semantic reranking to top lexical candidates where possible.
- Store vectors as compact binary float arrays instead of JSON.
- Cache the embedding model and query vector path.
- Add indexes for score/duration filters.
- For large libraries, evaluate a local vector extension or an owned ANN index.
- Batch search requests and reuse open connections where safe.
- Measure before adding a new dependency.

### 5.12.4 Job scheduling

- Use a bounded intelligence job queue.
- Limit CPU/GPU-heavy tasks to a configured concurrency.
- Keep backend request threads from doing long model work directly when possible; enqueue and return job state/progress.
- Preserve cancellation and partial-result behavior.
- Record per-stage duration, cache hit/miss, warning count, and peak memory.

### 5.12.5 Serialization

- Avoid repeated pretty-printed large JSON in hot paths.
- Keep human-readable final artifacts where useful, but use compact internal cache payloads.
- Stream very large result files where practical.
- Version every cache/output format.

---

## 5.13 Local backend

The backend already uses `ThreadingHTTPServer`, so requests are not globally serialized. However, heavy search/model work still runs inside request threads.

Required improvements:

- Add bounded request/job concurrency.
- Add timeouts for editor bridge render calls rather than `timeout=None`, with a progress/job protocol for long operations.
- Cache `MediaContextIndex` and model resources by canonical path/configuration.
- Add request duration and error telemetry.
- Return `503 busy` or queued-job state instead of allowing unbounded heavy work.
- Keep loopback-only and session-token protections intact.

---

## 5.14 Inspector and command refresh

`Execute` refreshes the inspector and invalidates command routing after every command. Inspector refresh clears and recreates effect entries and can rebuild dynamic effect parameter controls.

Tasks:

- Update only inspector fields affected by the command.
- Avoid rebuilding effect controls when the selected item/effect stack did not change.
- Replace broad `CommandManager.InvalidateRequerySuggested()` calls with targeted command-state notifications where feasible.
- Debounce slider-driven commands into one undoable commit at gesture end while keeping lightweight live preview during the gesture.
- Keep the current command/undo model; do not directly mutate project state from controls.

---

## 5.15 Memory, resources, and cache ownership

Add explicit ownership for:

- preview images;
- thumbnails;
- waveform drawings;
- WPF media elements;
- FFmpeg temporary graphs/files;
- exact-preview chunks;
- model instances;
- cancellation token sources;
- background save loops;
- backend/search connections.

Every cache must define:

- key;
- owner;
- invalidation condition;
- count/byte limit;
- eviction policy;
- shutdown cleanup;
- telemetry.

Do not solve memory pressure with `GC.Collect()`. Remove allocations and release references instead.

---

## 5.16 Architecture boundaries

The current `MainWindow.xaml.cs` remains a very large composition root, and `TimelineControl.cs` combines rendering, hit testing, interactions, selection, snapping, and transition logic.

Refactor incrementally after performance baselines exist.

Recommended components:

- `PreviewFrameScheduler`
- `RealtimeRenderPlanBuilder`
- `RealtimePreviewEngine`
- `TimelineSceneIndex`
- `TimelineStaticRenderer`
- `TimelineInteractionOverlay`
- `ProjectSaveCoordinator`
- `MediaLibraryViewModel`
- `ThumbnailCache`
- `MediaProbeCache`
- `FfmpegProcessRunner`
- `MediaJobScheduler`
- `EditorPerformanceTelemetry`

Rules:

- Domain remains independent of WPF and FFmpeg.
- Timeline mutations remain undoable commands.
- Project revision remains monotonic and enforced for agent edits.
- Preview/export fidelity rules remain unchanged.
- No cloud dependency is added to core editing.
- No downloader or scraper behavior is introduced.
- No big-bang rewrite of the whole editor.

---

## 6. Phased implementation roadmap

## Phase 0 — Establish measurement and representative workloads

**Priority:** P0  
**Purpose:** Prevent guesswork and create before/after evidence.

### Tasks

- Add `EditorPerformanceTelemetry` behind a development/performance flag.
- Record:
  - UI input-handler duration;
  - timeline static render duration/count;
  - playhead overlay updates;
  - real-time frame duration;
  - active layer count;
  - dropped frames;
  - allocations/GC counts where available;
  - save snapshot/serialize/write duration;
  - FFmpeg process duration;
  - thumbnail cache behavior;
  - startup milestones.
- Create deterministic projects:
  - `perf-small`: 50 clips, 4 tracks;
  - `perf-medium`: 500 clips, 12 tracks;
  - `perf-large`: 1,000+ clips, 20 tracks;
  - `perf-animation`: multiple channels and 100 keyframes/channel;
  - `perf-audio`: long files and many visible waveforms;
  - `perf-exact-preview`: masks/effects that force FFmpeg fallback.
- Add scripts/runbooks for:
  - PerfView or WPA trace;
  - `dotnet-trace` collection;
  - memory dump after ten-minute loop;
  - startup capture;
  - frame-time CSV export.
- Store results under `qa testing/results/performance/`, not in source directories.

### Exit gate

- Baseline report contains reproducible machine specifications, scenarios, P50/P95/P99 timings, CPU, memory, and trace links.

---

## Phase 1 — Remove UI-thread stalls and duplicate frame work

**Priority:** P0  
**Expected effect:** Largest immediate improvement in editing responsiveness.

### Tasks

1. Implement `ProjectSaveCoordinator`.
2. Remove synchronous save calls from command execution.
3. Debounce and coalesce autosaves by revision.
4. Create one preview frame scheduler.
5. Stop the 100 ms timer from duplicating real-time frame work.
6. Move playhead rendering into a lightweight overlay.
7. Avoid assigning unchanged transport text/slider values.
8. Add cancellation-safe shutdown for save and preview schedulers.

### Exit gate

- Rapid trim/move operations do not write files synchronously.
- Playback uses one scheduler.
- Moving the playhead does not call the full timeline `OnRender` path.

---

## Phase 2 — Optimize domain and timeline data access

**Priority:** P0/P1

### Tasks

1. Sort/index animation keyframes at mutation time.
2. Use binary search and cached channel lookup.
3. Add revision-aware sequence derived-state cache.
4. Add item/media/transition dictionaries.
5. Build sorted per-track clip arrays and snap-point indexes.
6. Replace transition-slot sorting/scanning in render and hit-test paths.
7. Add viewport-aware clip and track culling.
8. Cache/freeze timeline drawing resources.
9. Cache waveform drawings by zoom bucket.

### Exit gate

- Animation lookup steady state allocates zero bytes.
- Timeline rendering visits only visible tracks/clips plus a small boundary set.
- 1,000-clip timeline meets interaction targets.

---

## Phase 3 — Rebuild the real-time preview hot path

**Priority:** P1

### Tasks

1. Build `RealtimeRenderPlan` per relevant revision.
2. Add interval-based active-set tracking.
3. Incrementally add/remove layers.
4. Reuse transform and effect objects.
5. Remove duplicate audio/player update pass.
6. Cache decoded images at preview resolution.
7. Add next-layer warm-up around clip boundaries.
8. Add draft preview resolution/FPS settings.
9. Introduce safe fallback thresholds for excessive simultaneous WPF players.

### Exit gate

- No whole-sequence LINQ allocations per steady-state frame.
- Unaffected layers survive clip-boundary changes.
- Preview meets target FPS on the baseline machine.

---

## Phase 4 — Optimize exact preview and FFmpeg work

**Priority:** P1

### Tasks

1. Implement shared `FfmpegProcessRunner` with `ArgumentList`.
2. Add bounded media job scheduler.
3. Add structured progress and bounded logs.
4. Move large filter graphs to script files.
5. Implement chunked exact preview cache.
6. Add dependency-aware chunk invalidation.
7. Add preview cache byte cap/LRU cleanup.
8. Stream waveform peak aggregation.
9. Add probe-result cache.
10. Evaluate hardware acceleration after software measurements.

### Exit gate

- Exact preview begins from a local chunk rather than requiring full-timeline render.
- Preview cache remains within configured limits.
- Long waveform generation has bounded memory.

---

## Phase 5 — Media library, startup, and Python intelligence

**Priority:** P2

### Tasks

1. Persistent media item view models and debounced filtering.
2. Async bounded thumbnail cache.
3. Lazy font enumeration.
4. Async asset/extension scans.
5. Tail-based audit-log loading and rotation.
6. Deferred autosave recovery UI.
7. Tiered media fingerprinting in Python.
8. Persistent Whisper/SentenceTransformer model caches.
9. Binary vector storage and candidate-limited semantic reranking.
10. Bounded intelligence job queue and per-stage telemetry.

### Exit gate

- Media search and startup targets pass.
- Repeated intelligence tasks reuse models and cached fingerprints.
- Context search scales without deserializing every vector for ordinary queries.

---

## Phase 6 — Hardening and regression gates

**Priority:** P1/P2

### Tasks

- Add BenchmarkDotNet project for pure C# hot paths.
- Add performance integration tests for save coalescing, index rebuilds, and cache eviction.
- Add UI automation performance scenarios.
- Add memory-leak tests for repeated project open/close, preview play/stop, and thumbnail scrolling.
- Add failure tests:
  - FFmpeg cancellation;
  - disk full;
  - cache corruption;
  - project change during autosave;
  - model load failure;
  - unsupported preview feature;
  - process shutdown during background work.
- Add CI smoke budgets where stable and keep hardware-sensitive full tests in a dedicated performance environment.
- Restore Python test execution by adding `pytest` to the intended development/test environment definition.

### Exit gate

- All functional tests pass.
- Performance budgets pass on the defined baseline machine.
- No preview/export correctness regression is found in manual-review artifacts.

---

## 7. File-level implementation checklist

### `src/Rushframe.Desktop/Timeline/TimelineControl.cs`

- [ ] Remove full redraw from playhead-only change.
- [ ] Split static, interaction, and playhead rendering.
- [ ] Cache/freeze brushes, pens, typefaces, and geometries.
- [ ] Replace all-track/all-item scans with indexed visible-range queries.
- [ ] Remove duration calculation from `OnRender`.
- [ ] Cache transition slots and snap points.
- [ ] Cache waveform drawings.
- [ ] Replace allocation-heavy marker/transition hit testing.

### `src/Rushframe.Desktop/Timeline/TimelineControl.MultiSelection.cs`

- [ ] Coalesce invalidations during multi-selection gestures.
- [ ] Use indexed item/track lookup.
- [ ] Emit one grouped command at gesture completion.

### `src/Rushframe.Desktop/MainWindow.Preview.cs`

- [ ] Replace dual timer/render scheduling with one scheduler.
- [ ] Throttle transport labels separately from visual frames.
- [ ] Integrate chunked exact-preview cache.
- [ ] Decode images to preview dimensions.
- [ ] Avoid whole-timeline fallback rendering for local seeks.

### `src/Rushframe.Desktop/MainWindow.RealtimePreview.cs`

- [ ] Build revision-scoped render plan.
- [ ] Incrementally diff active layers.
- [ ] Reuse transforms/effects/clips.
- [ ] Remove duplicate audio update loops.
- [ ] Cache transition and media lookups.
- [ ] Add interval cursor/binary seek.
- [ ] Add draft resolution/FPS controls.
- [ ] Warm next media layer before boundary.

### `src/Rushframe.Desktop/MainWindow.Project.cs`

- [ ] Command completion only marks dirty and enqueues persistence.
- [ ] Do not perform synchronous project or autosave writes.
- [ ] Use targeted UI refresh based on command impact.

### `src/Rushframe.Desktop/MainWindow.xaml.cs`

- [ ] Stop unconditional preview timer.
- [ ] Defer startup scans and font enumeration.
- [ ] Replace media-list clear/rebuild flow.
- [ ] Reduce broad command requery invalidations.
- [ ] Continue extracting cohesive services from the composition root.

### `src/Rushframe.Desktop/MainWindow.Inspector.cs`

- [ ] Avoid complete inspector/effect-list rebuild for unrelated commands.
- [ ] Separate live gesture preview from committed undo command.
- [ ] Cache reusable dynamic effect editors where practical.

### `src/Rushframe.Domain/Keyframe.cs`

- [ ] Sorted keyframe invariant.
- [ ] Binary segment search.
- [ ] Zero-allocation lookup.
- [ ] Optional sequential segment cursor.

### `src/Rushframe.Domain/TimelineItem.cs`

- [ ] Cached animation channel lookup.
- [ ] Explicit invalidation when channels change.

### `src/Rushframe.Domain/Sequence.cs`

- [ ] Cached duration/derived state or move derived state into a revision-scoped index.
- [ ] Eliminate repeated duration scans from frame paths.

### `src/Rushframe.Infrastructure/AutosaveService.cs`

- [ ] Replace unmanaged fire-and-forget loop with awaitable lifecycle.
- [ ] Save only changed revisions.
- [ ] Never serialize live mutable state concurrently.
- [ ] Add byte/count retention policy.

### `src/Rushframe.Infrastructure/ProjectRepository.cs`

- [ ] Async atomic write API.
- [ ] Accept pre-serialized snapshot/payload where appropriate.
- [ ] Preserve deterministic error reporting and recovery.

### `src/Rushframe.Infrastructure/AgentAuditLogService.cs`

- [ ] Read recent records from file tail.
- [ ] Rotate/cap log.
- [ ] Optional buffered async append with durable flush policy.

### `src/Rushframe.Media.Native/FfmpegMediaService.cs`

- [ ] Shared process runner.
- [ ] `ArgumentList` everywhere.
- [ ] Bounded logs and structured progress.
- [ ] Streaming waveform peak extraction.
- [ ] Probe cache.

### `src/Rushframe.Media.Native/FfmpegTimelineRenderer.cs`

- [ ] Filter graph script for large jobs.
- [ ] Reuse validated render-plan metadata.
- [ ] Support range/chunk rendering for exact preview.
- [ ] Emit dependency metadata for cache invalidation.

### `rushframe_intelligence/cache.py`

- [ ] Tiered fingerprint instead of unconditional full-file hash.
- [ ] Version fingerprint algorithm.

### `rushframe_intelligence/transcriber.py`

- [ ] Reuse `WhisperModel` instances.
- [ ] Add model load/inference telemetry.

### `rushframe_intelligence/context_index.py`

- [ ] Cache embedding models.
- [ ] Store vectors in binary form.
- [ ] Restrict semantic reranking candidate set.
- [ ] Add scale benchmarks.

### `rushframe_intelligence/backend.py`

- [ ] Bounded heavy-job concurrency.
- [ ] Long-operation job/progress API instead of infinite bridge timeout.
- [ ] Cache index/model resources safely.

---

## 8. Benchmark matrix

| Benchmark | Small | Medium | Large |
|---|---:|---:|---:|
| Timeline clips | 50 | 500 | 1,000+ |
| Tracks | 4 | 12 | 20 |
| Transitions | 5 | 50 | 100 |
| Markers | 10 | 50 | 100 |
| Animated clips visible | 2 | 5 | 8+ |
| Keyframes/channel | 2 | 10 | 100 |
| Media library assets | 100 | 5,000 | 20,000 |
| Audio duration | 1 min | 30 min | 3 hr |
| Exact preview duration | 10 s | 2 min | 30 min |
| Intelligence moments | 100 | 5,000 | 50,000 |

Measure:

- CPU by thread;
- UI-thread utilization;
- frame time P50/P95/P99;
- dropped frames;
- allocation rate;
- Gen 0/1/2 collections;
- working set and private bytes;
- disk reads/writes;
- FFmpeg process count and concurrency;
- cache hit/miss/eviction;
- startup milestones;
- command-to-visible-update latency.

---

## 9. Tests to add

### Unit tests

- Animation keyframes remain sorted after every mutation path.
- Binary animation lookup matches current interpolation output.
- Sequence index invalidation is correct for all command categories.
- Transition dictionaries remain consistent after add/delete/split/move.
- Save coordinator coalesces revisions and never saves stale state after newer state.
- Cache keys change only for relevant inputs.
- Preview chunk invalidation intersects correct ranges.
- Probe/thumbnail/image caches invalidate on source file changes.

### Integration tests

- Execute 100 rapid edits and verify bounded save count.
- Mutate project while autosave is queued and verify complete revision consistency.
- Cancel FFmpeg during waveform, proxy, preview, and export jobs.
- Repeated play/pause/seek does not accumulate event handlers or media elements.
- Cross clip boundaries while unrelated layers remain alive.
- Open and close ten projects while monitoring retained references.
- Search 20,000 media items with debounced filtering.
- Search 50,000 intelligence moments with lexical and semantic modes.

### Manual performance QA

- Scrub densely edited timeline.
- Drag/trim groups while preview is paused and playing.
- Loop a transition-heavy section for ten minutes.
- Toggle between real-time and exact preview.
- Edit an unsupported effect and verify local chunk regeneration only.
- Import a large media library and rapidly filter it.
- Save and close while a background autosave is active.
- Compare real-time/exact preview frames with final export.

---

## 10. Instrumentation design

Use lightweight `System.Diagnostics.Metrics`, `ActivitySource`, `Stopwatch`, and optional EventSource/ETW events.

Suggested metrics:

- `rushframe.preview.frame.duration_ms`
- `rushframe.preview.frames.rendered`
- `rushframe.preview.frames.dropped`
- `rushframe.preview.layers.active`
- `rushframe.timeline.static_render.duration_ms`
- `rushframe.timeline.static_render.count`
- `rushframe.timeline.visible_items`
- `rushframe.timeline.items_scanned`
- `rushframe.animation.lookup.duration_ns`
- `rushframe.project.snapshot.duration_ms`
- `rushframe.project.serialize.duration_ms`
- `rushframe.project.write.duration_ms`
- `rushframe.project.save.coalesced_count`
- `rushframe.thumbnail.cache.hit_rate`
- `rushframe.preview_cache.bytes`
- `rushframe.ffmpeg.jobs.active`
- `rushframe.ffmpeg.job.duration_ms`
- `rushframe.intelligence.stage.duration_ms`
- `rushframe.startup.time_to_first_render_ms`
- `rushframe.startup.time_to_interactive_ms`

Development overlay:

- current FPS;
- P95 frame time;
- dropped frames;
- active layers/players;
- timeline items scanned/visible;
- last save duration/revision;
- cache sizes;
- process/job counts.

The overlay must be disabled by default in normal release mode.

---

## 11. Risk and rollback strategy

### Risk: preview output changes

Mitigation:

- preserve current exact FFmpeg fallback;
- compare representative preview snapshots against export;
- gate optimized real-time paths by supported-feature checks;
- provide a runtime switch to disable the new preview engine during rollout.

### Risk: save coordinator loses a recent edit

Mitigation:

- explicit revision tracking;
- atomic writes;
- bounded shutdown flush;
- fault-injection tests;
- retain the old save path behind a temporary fallback flag until recovery tests pass.

### Risk: cached indexes become stale

Mitigation:

- key indexes by project revision and sequence ID;
- centralize command-impact invalidation;
- assert index revision in development builds;
- add a slow correctness fallback and comparison mode during rollout.

### Risk: image/media caches increase memory

Mitigation:

- byte-bounded LRU caches;
- explicit eviction telemetry;
- low-memory response;
- proxy-size tiers;
- no unbounded static dictionaries.

### Risk: architectural refactor collides with active uncommitted work

Mitigation:

- implement one isolated component at a time;
- avoid formatting unrelated files;
- keep commits small and feature-flagged;
- capture current behavior with tests before moving code.

---

## 12. Anti-patterns to avoid

- Do not call `GC.Collect()` to hide allocation problems.
- Do not increase timer intervals as the only fix.
- Do not silently lower preview fidelity for unsupported effects.
- Do not remove undo/redo or bypass commands for speed.
- Do not mutate project state from background threads.
- Do not serialize the live mutable project concurrently.
- Do not introduce unbounded global caches.
- Do not rebuild the entire editor before measuring focused fixes.
- Do not add a cloud service to solve local preview or persistence performance.
- Do not add new dependencies until the current bottleneck is measured and the dependency has a clear ownership/lifecycle plan.
- Do not treat a successful build or unit tests as proof of smooth UI performance.
- Do not claim performance improvement without before/after traces on the same workload and machine.

---

## 13. Recommended implementation order

1. Capture baseline traces and add counters.
2. Remove synchronous per-command persistence.
3. Eliminate duplicate preview scheduler.
4. Separate playhead from static timeline redraw.
5. Make animation lookup zero-allocation.
6. Add sequence/media/transition indexes.
7. Optimize timeline culling and drawing-resource caches.
8. Build revision-scoped real-time render plan.
9. Incrementally manage preview layers and reuse transforms/effects.
10. Add image/thumbnail/probe caches.
11. Add FFmpeg process runner and waveform streaming.
12. Implement chunked exact-preview cache.
13. Defer startup work.
14. Optimize Python fingerprint/model/search paths.
15. Add automated performance gates and complete manual preview/export validation.

This order minimizes risk and gives early user-visible wins before deeper media-engine work.

---

## 14. Definition of done

The optimization project is complete only when all of the following are true:

- Release build succeeds with no new warnings.
- Existing functional tests pass.
- Python test environment is reproducible and Python tests pass.
- Performance workload projects and runbooks are committed.
- Normal edit commands perform no synchronous project file I/O.
- Autosave is revision-aware, coalesced, consistent, and awaitable.
- Steady playback uses one frame scheduler.
- Playhead movement does not redraw static timeline content.
- Animation evaluation is zero-allocation in steady state.
- Real-time preview no longer scans the whole sequence or recreates transforms/effects every frame.
- Clip-boundary activation does not clear/rebuild every preview layer.
- Exact preview is chunked, cancellable, dependency-aware, and bounded on disk.
- Media search does not synchronously reload all thumbnails.
- Large images are decoded to appropriate preview sizes.
- FFmpeg diagnostics and waveform extraction have bounded memory.
- Startup non-critical work is deferred.
- Representative baseline-machine targets pass.
- Ten-minute preview loop shows no meaningful retained-memory growth.
- Real-time/exact preview still matches final FFmpeg export for tested features.
- Manual edit priority, undo/redo, revision safety, local-only media behavior, approval flow, and auditability remain intact.

---

## 15. Immediate first implementation slice

The safest first slice should touch only focused areas and deliver measurable improvement:

1. Add frame/save timing counters.
2. Introduce `ProjectSaveCoordinator` and remove synchronous `SaveAutosaveSnapshot()` from `Execute`.
3. Disable the 100 ms preview timer while `CompositionTarget.Rendering` is active.
4. Add a separate playhead overlay so playback no longer invalidates `TimelineControl`.
5. Change animation lookup to sorted keyframes plus binary search.
6. Add benchmarks and regression tests for these changes.

Do not proceed to a media-engine rewrite until this slice has been profiled. These changes directly address the most expensive confirmed paths and are likely to improve perceived responsiveness substantially with limited architectural risk.
