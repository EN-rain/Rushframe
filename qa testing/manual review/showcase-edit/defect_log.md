# Rushframe Showcase Edit Defect Log

Record only defects reproduced during the current showcase-edit QA pass.

## QA-NEW-001 - Desktop test suite stale after Campaign panel removal

**Severity:** Major  
**Status:** Retest Passed  
**Build:** local Debug build on 2026-07-12  
**Project revision:** working tree, uncommitted

### Preconditions

- Current workspace includes removal of the Campaign panel from the desktop UI and panel registry.

### Steps
1. Run `dotnet build Rushframe.slnx`.
2. Run `dotnet test Rushframe.slnx`.

### Expected

Desktop tests compile and reflect the current panel registry and workspace layout behavior.

### Actual

`WorkspaceLayoutTests` and `PanelRegistryTests` still referenced `PanelId.Tasks` and the old panel count, causing baseline verification to fail.

### Impact on showcase edit

The mandatory QA baseline could not pass before manual editor validation.

### Evidence
- Screenshot: none
- Video timestamp: n/a
- Project file: n/a
- Log: `dotnet build` and `dotnet test` failures before the test updates

### Suspected component

`tests\Rushframe.Desktop.Tests`

### Retest

Updated stale tests, reran `dotnet build Rushframe.slnx` and `dotnet test Rushframe.slnx`, and both passed.

## QA-NEW-002 - QA Python command path does not match repo layout

**Severity:** Major  
**Status:** Retest Passed  
**Build:** local Debug build on 2026-07-12  
**Project revision:** working tree, uncommitted

### Preconditions

- Follow the showcase-edit brief exactly from repo root.

### Steps
1. Open the current QA instruction set.
2. Run `python -m pytest test_media_intelligence_v2.py` from repo root.

### Expected

The required Python verification command should locate and run the intended media-intelligence test file.

### Actual

Pytest reports `file or directory not found: test_media_intelligence_v2.py`. The actual test file lives at `tests\test_media_intelligence_v2.py`.

### Impact on showcase edit

The mandatory baseline verification fails when the instruction is followed literally, which blocks a clean QA run.

### Evidence
- Screenshot: none
- Video timestamp: n/a
- Project file: n/a
- Log: literal pytest command fails; `python -m pytest tests\test_media_intelligence_v2.py` passes with 5 tests

### Suspected component

QA documentation / test command path

### Retest

Updated the main QA plan and strict showcase runbook to use `python -m pytest tests\test_media_intelligence_v2.py`. The corrected command passed with 5 tests on 2026-07-13.

## QA-NEW-003 - Delete command mutates a locked video track

**Severity:** Critical  
**Status:** Retest Passed  
**Build:** local Debug build on 2026-07-12  
**Project revision:** 64 live editor session

### Preconditions

- `rushframe_showcase_edit.rushframe` is open in Rushframe.
- V1 is locked through the track header context menu.
- A clip on V1 is selected.

### Steps
1. Right-click V1 track header.
2. Invoke `Lock Track`.
3. Select the first V1 clip.
4. Press Delete.
5. Save and inspect the project.

### Expected

The Delete operation is rejected because the selected clip is on a locked track. The V1 item count remains 11 and undo history is not polluted by a rejected mutation.

### Actual

The selected clip was deleted from V1 while V1 was locked. V1 item count changed from 11 to 10.

### Impact on showcase edit

Critical locked-track rejection requirement failed and the showcase timeline could be damaged by keyboard commands despite disabled toolbar buttons.

### Evidence
- Screenshot: `timeline_overview.png`
- Video timestamp: n/a
- Project file: `rushframe_showcase_edit.rushframe`
- Log: locked-track command output in QA run

### Suspected component

Domain edit commands and routed editor command path.

### Retest

Added domain locked-track guards for delete, ripple delete, duplicate, and split commands, plus regression tests. Rebuilt the desktop app, reopened the showcase project through the UI, locked V1, selected the first V1 clip, pressed Delete, saved, and verified V1 remained at 11 items while locked. Track was unlocked afterward.

## QA-NEW-004 - QA requires FFprobe but only FFmpeg is available locally

**Severity:** Major  
**Status:** Retest Passed  
**Build:** local Debug build on 2026-07-12  
**Project revision:** working tree, uncommitted

### Preconditions

- Follow the showcase-edit runbook final technical validation.

### Steps
1. Locate `ffprobe.exe` under `.tools\bin`.
2. Locate `ffprobe` in PATH.
3. Regenerate `export_metadata.txt`.

### Expected

The required FFprobe binary is available, or the runbook names the actual local metadata tool.

### Actual

`.tools\bin` contains `ffmpeg.exe` but not `ffprobe.exe`, and no PATH `ffprobe` command is available. Export metadata was regenerated with FFmpeg stream output instead.

### Impact on showcase edit

The runbook's exact FFprobe requirement cannot be completed from the current local tooling, although FFmpeg verified streams, decode, black intervals, and freeze intervals.

### Evidence
- Screenshot: n/a
- Video timestamp: n/a
- Project file: n/a
- Log: `export_metadata.txt`

### Suspected component

QA tooling / local media tool installation.

### Retest

Updated the QA plan, strict runbook, and artifact manifest to accept bundled FFmpeg stream metadata plus decode, black-frame, freeze, and audio-peak checks when FFprobe is unavailable. The 25.50-second showcase export passed those fallback checks on 2026-07-13.

## QA-NEW-005 - Locked tracks still allow move, trim, paste, and add commands

**Severity:** Critical  
**Status:** Fixed  
**Build:** local Release build on 2026-07-13  
**Project revision:** working tree, uncommitted

### Preconditions

- A sequence contains an item on a locked track, or a locked destination track is selected.
- Editing is invoked through domain/application commands rather than only toolbar enablement.

### Steps
1. Lock the source track and execute `MoveClipCommand` or `TrimClipCommand`.
2. Lock a destination track and execute `MoveClipCommand`, `PasteClipCommand`, or `AddClipCommand`.
3. Inspect track contents and item timing.

### Expected

Every command is rejected with no source or destination mutation. A failed command must not enter undo history.

### Actual

The commands did not check `Track.Locked`. They could move or trim an item on a locked track and add or paste content into a locked track. Moving into a locked destination was also allowed.

### Impact on showcase edit

Locked timelines can still be damaged through drag, trim, insert, overwrite, paste, or agent/application command paths even after the earlier Delete/Split/Duplicate fix.

### Evidence
- Screenshot: n/a
- Video timestamp: n/a
- Project file: `rushframe_showcase_edit.rushframe`
- Log: source inspection and regression tests

### Suspected component

`Rushframe.Domain.Editing` and `Rushframe.Application.PasteClipCommand`

### Retest

Added track/item lock guards across add, move, trim, paste, delete, ripple delete, duplicate, split, transform, inspector property, animation, text, effect, transition, and media-intelligence command paths. Locked selections now disable the inspector and hide preview transform handles. Focused locked-edit regressions passed, including verification that rejected commands do not enter undo history. Full UI interaction evidence remains part of the outstanding manual release pass.

## QA-NEW-006 - Undoing effect removal changes effect-stack order

**Severity:** Major  
**Status:** Retest Passed  
**Build:** local Release build on 2026-07-13  
**Project revision:** working tree, uncommitted

### Preconditions

- A timeline item has at least three effects in a defined order.

### Steps
1. Remove the middle effect with `RemoveEffectCommand`.
2. Undo the command.
3. Inspect the restored effect order.

### Expected

Undo restores the removed effect at its original index so rendering is identical to the pre-remove state.

### Actual

`RemoveEffectCommand.Undo` appended the restored effect to the end of the list, changing the effect processing order.

### Impact on showcase edit

Undo can silently change the visual result even though the effect appears to have been restored.

### Evidence
- Screenshot: n/a
- Video timestamp: n/a
- Project file: `rushframe_showcase_edit.rushframe`
- Log: source inspection and regression test

### Suspected component

`Rushframe.Domain.Editing.RemoveEffectCommand`

### Retest

Stored the removed effect index and restored the effect at that index during undo. The three-effect order regression passed, and the complete .NET suite passed on 2026-07-13.

## QA-NEW-007 - Transition command bypasses locks and accepts invalid duration

**Severity:** Major  
**Status:** Retest Passed  
**Build:** local Release build on 2026-07-13  
**Project revision:** working tree, uncommitted

### Preconditions

- Two adjacent items exist on the same track.
- The track or either item may be locked, or a non-positive transition duration is supplied programmatically.

### Steps
1. Execute `ApplyTransitionCommand` on a locked track or locked item.
2. Execute it with a zero or negative duration.
3. Inspect `Sequence.Transitions`.

### Expected

Locked content is not mutated, and transition duration must be greater than zero.

### Actual

The command did not enforce track/item locks and could construct a transition with a non-positive duration when called outside the UI dialog.

### Impact on showcase edit

Agent or programmatic edit paths can modify protected cuts or create invalid render timing that the dialog itself would not allow.

### Evidence
- Screenshot: n/a
- Video timestamp: n/a
- Project file: `rushframe_showcase_edit.rushframe`
- Log: source inspection and regression tests

### Suspected component

`Rushframe.Domain.Editing.ApplyTransitionCommand`

### Retest

Added track/item lock checks and rejected zero or negative transition durations before mutation. Locked-track and invalid-duration regressions passed, and the complete .NET suite passed on 2026-07-13.

## QA-NEW-008 - Unrelated keyframe mutations invalidate every animation cache

**Severity:** Major  
**Status:** Retest Passed  
**Build:** local Release build on 2026-07-13  
**Project revision:** working tree, uncommitted

### Preconditions

- An animation channel has a warmed lookup cache with 100 keyframes.
- Any other keyframe anywhere in the process is mutated.

### Steps
1. Warm `AnimationChannel.GetValueAt` for one channel.
2. Mutate an unrelated keyframe owned by another channel or test.
3. Query the warmed channel and measure current-thread allocations.

### Expected

An unrelated keyframe mutation does not rebuild this channel's sorted-keyframe cache or allocate memory.

### Actual

All keyframes shared one global mutation version. Any keyframe mutation made every channel call `Keyframes.ToArray()` and sort again on its next lookup. A 100-keyframe channel allocated 824 bytes, causing intermittent Release-suite failures under parallel tests.

### Impact on showcase edit

Editing one animated property can cause unrelated animation channels across the project to rebuild and allocate on preview playback, creating avoidable frame-time spikes in animation-heavy timelines.

### Evidence
- Screenshot: n/a
- Video timestamp: n/a
- Project file: n/a
- Log: Release suite intermittently failed `AnimationChannel_WarmedSteadyState_HasNegligibleAllocations` with exactly 824 bytes; isolated runs and BenchmarkDotNet steady state remained allocation-free

### Suspected component

`Rushframe.Domain.Keyframe` and `AnimationChannel.EnsureLookupCache`

### Retest

Added per-keyframe mutation versions and cached identity/version snapshots. Unrelated global mutations now refresh only the observed global version without rebuilding the sorted array; a channel rebuild occurs only when its own keyframe set or keyframe state changes. The deterministic unrelated-mutation allocation regression passed, the performance test group passed, and three consecutive full Release suites passed on 2026-07-13.

## QA-NEW-009 - Exact preview seek stays on the first frame and mismatches export frames

**Severity:** Critical  
**Status:** Retest Passed  
**Build:** local Debug build on 2026-07-13  
**Project revision:** working tree, uncommitted

### Preconditions

- `rushframe_showcase_edit.rushframe` is open in Rushframe.
- Exact timeline preview has been prepared after selecting a timeline clip.
- Export frames exist at 1.0s, 12.5s, and 24.0s.

### Steps
1. Use the preview seek control to request 1.0s, 12.5s, and 24.0s.
2. Capture the Rushframe preview monitor at each requested timestamp.
3. Compare those captures with the exported frames at the same timestamps.
4. Repeat a real mouse seek to 12.5s on the preview slider.

### Expected

The preview monitor updates to the requested timeline timestamp, and each captured preview frame materially matches the exported frame at the same timestamp.

### Actual

The preview slider value changed through UI Automation, but the preview monitor stayed on the first/hook frame for 1.0s, 12.5s, and 24.0s. A real mouse seek to 12.5s also left the monitor on the first frame and reset the slider state. The 12.5s and 24.0s preview captures do not match their exported frames.

### Confirmed root cause

`PreviewSeekSlider` exposed `RangeValuePattern`, but the editor only invoked `SeekPreview` on `PreviewMouseLeftButtonUp`. UI Automation `SetRangeValue` changed the slider thumb without firing that mouse-up path, so the control value diverged from the canonical playhead and displayed frame. During investigation, frame-step navigation after an exact-preview seek also used the media element's local position, which could be stale, instead of the canonical timeline playhead.

### Impact on showcase edit

Release-critical preview/export confidence fails. The editor can present stale preview imagery while export output changes, so manual timing, typography, transition, and creative review cannot be trusted from the preview monitor.

### Evidence
- Screenshots: `preview_01_1.0s.png`, `preview_02_12.5s.png`, `preview_03_24.0s.png`, `preview_mouse_seek_12.5s.png`
- Export comparison frames: `export_01_1.0s.png`, `export_02_12.5s.png`, `export_03_24.0s.png`
- Project file: `rushframe_showcase_edit.rushframe`
- Log: `preview_capture_times.txt`

### Suspected component

Exact preview seek/render refresh path, including the preview slider update and realtime/exact preview frame presenter.

### Retest

Added `PreviewSeekRequestGate` so internal slider updates are suppressed, pointer drags still seek on release, and UIA/keyboard value changes invoke `SeekPreview`. Updated exact-preview frame stepping to derive from the canonical timeline playhead. Added deterministic Desktop tests for automation seek gating and exact-preview frame-step target calculation.

Retest evidence:
- Path A real pointer slider seek: PASS. 12.5s updated immediately; 24.0s displayed pending state while rendering and then updated to the correct frame after the chunk completed.
- Path B frame-step/navigation: PASS. Next-frame after a UIA seek to 15.000s advanced the slider to 15.033s instead of jumping back to the chunk start.
- Path C UI Automation slider value: PASS. `SetRangeValue` to 1.0s, 12.5s, and 24.0s updated both the preview frame and displayed time.
- Path D playback: PASS. Playback from 12.5s advanced the preview and playhead.
- Rapid seeks: PASS for enabled same-chunk sequence 13s -> 14s -> 15s; final frame settled at 15s. While exact chunk rendering is pending, controls are disabled and the status shows rendering, so stale old frames are not presented as ready.

Evidence files: `qa_new_009_final_uia_01_1.0s.png`, `qa_new_009_final_uia_02_12.5s.png`, `qa_new_009_final_uia_03_24.0s.png`, `qa_new_009_path_d_playback_after_fix.png`, `qa_new_009_final_frame_step_after_uia_seek.png`, `qa_new_009_after_fix_rapid_uia_same_chunk_final_15s.png`.

Automated verification on 2026-07-13: `dotnet build Rushframe.slnx` PASS, `dotnet test Rushframe.slnx` PASS with 191 tests, `python -m pytest tests\test_media_intelligence_v2.py -q` PASS with 5 tests, `dotnet build Rushframe.slnx -c Release` PASS, `dotnet test Rushframe.slnx -c Release --no-build` PASS with 191 tests. Focused preview/timeline/performance subset passed 15 tests in three consecutive runs.

## Manual blocker update - Keyframe/Bezier persistence

**Status:** Passed  
**Build:** local Debug/Release baseline from 2026-07-13  
**Project revision:** working tree, uncommitted

### Retest

Opened the showcase project through the WPF UI, selected the first visible V1 video clip, added a `positionX` animation channel with Bezier keyframes at `0s / 0` and `1.2s / 120`, applied the animation, saved, closed, reopened the project, and reopened the Animation Graph Editor. The reopened graph still showed both Bezier keyframes. A post-reopen edit changed the second value to `140`; undo restored `120`; redo restored `140`; the redone state was saved.

Evidence files: `keyframe_bezier_before_save.png`, `keyframe_bezier_after_reopen.png`, `keyframe_bezier_after_edit.png`, `keyframe_bezier_after_undo.png`, `keyframe_bezier_after_redo.png`.

No new defect was opened for keyframe persistence.

## QA-NEW-010 - Export title text is cropped off-frame

**Severity:** Major  
**Status:** Retest Passed  
**Build:** local Debug/Release validation on 2026-07-13  
**Project revision:** 91

### Preconditions

- Review `rushframe_showcase_edit_current_run.mp4` visually without audio.
- Inspect first, middle, payoff, and final export frames.

### Steps
1. Extract visual sweep frames from the latest verified export at 0.0s, 1.0s, 6.0s, 12.5s, 18.0s, 24.0s, and 25.4s.
2. Inspect title placement and safe-area readability.

### Expected

Title text remains inside the 1080x1920 export frame and is readable in the hook and ending.

### Actual

`DON'T BLINK.` is cropped at the left edge in `visual_sweep_01_0_0s.png`. `STILL HERE?` is cropped at the top/left in `visual_sweep_06_24_0s.png` and `visual_sweep_07_25_4s.png`.

### Impact on showcase edit

The non-audio creative assessment fails because Hook and Typography score below 3, and Technical quality cannot score 5 while visible title crop remains.

### Evidence
- Screenshots: `visual_sweep_01_0_0s.png`, `visual_sweep_06_24_0s.png`, `visual_sweep_07_25_4s.png`
- Project file: `rushframe_showcase_edit.rushframe`
- Export: `rushframe_showcase_edit_current_run.mp4`

### Suspected component

Showcase edit typography placement / safe-area composition.

### Retest

Project-data fix applied on 2026-07-13. The showcase project contained duplicated title-layer sequences; both copies were corrected so `DON'T BLINK.` no longer starts from an off-canvas keyframe and `STILL HERE?` no longer starts at the top-left canvas edge. No renderer/source-code defect was confirmed.

Corrected values:
- `DON'T BLINK.`: `positionX` default/keyframes `120`, `positionY` `260`, `fontFamily` `Arial`, `fontSize` `82`.
- `STILL HERE?`: `positionX` `120`, `positionY` `220`, `fontFamily` `Arial`, `fontSize` `78`.

Retest evidence:
- Before: `qa_new_010_first_frame_before.png`, `qa_new_010_final_frame_before.png`
- Preview: `qa_new_010_first_frame_after_preview.png`, `qa_new_010_final_frame_after_preview.png`
- Export: `qa_new_010_first_frame_after_export.png`, `qa_new_010_final_frame_after_export.png`
- Fixed MP4: `rushframe_showcase_edit_qa_new_010_fixed.mp4`
- FFmpeg logs: `qa_new_010_fixed_metadata.txt`, `qa_new_010_fixed_decode.txt`, `qa_new_010_fixed_blackdetect.txt`, `qa_new_010_fixed_freezedetect.txt`

The project was reopened through Rushframe, exact preview was rendered at `00:00` and `00:25`, and the Rushframe window remained foreground for both captures. The opening and final preview/export pairs match in complete text, punctuation, position, scale, Arial rendering, black background, video layer, and safe-area placement. Neither title is clipped, and no material preview/export mismatch is visible.

Two additional exports through the real Export Settings and Save As dialogs completed successfully and produced byte-identical 25.50-second 1080x1920 H.264/AAC files. Both decoded completely with no configured black/freeze events.

**Status:** Retest Passed.

## QA-NEW-011 - Apparent real-dialog export stall

**Severity:** Major (suspected)  
**Status:** Not Reproduced - Closed  
**Build:** local Debug build on 2026-07-13  
**Project revision:** 91

### Preconditions

- Open the corrected `rushframe_showcase_edit.rushframe` project through Rushframe.
- Open Export Settings and select Portrait, 1080p, MP4, High quality, and Include mixed audio.
- Choose a unique writable output path through the native Save As dialog.

### Steps
1. Start export through the real Export Settings and Save As dialogs.
2. Observe Rushframe, FFmpeg, progress, output-file growth, and completion UI.
3. Repeat using a second unique output path.
4. Start a third disposable export and invoke Cancel during rendering.

### Expected

The UI remains responsive, FFmpeg writes the output, completion is surfaced, controls restore after the completion prompt is answered, and cancellation terminates the active render and restores controls.

### Actual

No render stall reproduced. Both full attempts completed in about 30.5 seconds. During each attempt Rushframe remained responsive, one FFmpeg child process consumed CPU, and the output file grew continuously. FFmpeg exited normally, progress changed to 100%, and Rushframe displayed the modal `Export Complete` prompt asking whether to open the containing folder.

The initially suspected permanent busy state was the expected modal completion prompt awaiting a Yes/No response. While that prompt was open, the operation bar and disabled Export button remained visible because `ExportController.ExportAsync` had not yet returned. Selecting No dismissed the prompt; the finally path then hid operation controls, set `Export operation finished`, and re-enabled Export. A second export was started in the same process without restarting Rushframe.

Progress was bounded but coarse: it stayed at 5% during most rendering and jumped to 100% on completion. This did not block completion or responsiveness.

The cancellation probe was invoked with FFmpeg running and a 1,572,912-byte partial output. FFmpeg exited within roughly two seconds, the operation UI cleared, and Export was re-enabled. A partial 1,835,056-byte output remained on disk and was not treated as a completed export.

### Evidence
- Attempt 1: `rushframe_showcase_edit_real_dialog_attempt1_20260713_2330.mp4`
- Attempt 2: `rushframe_showcase_edit_real_dialog_attempt2_20260713_2340.mp4`
- Each completed output: 13,278,102 bytes, SHA256 `18EF5BB252DC9E7481D4CDC97808E12F5E17ECEC660B2DC9B84ECB70963D66E8`; identical to the direct-service fixed export
- Attempt 1 observation: 23:32:28 to 23:32:57; FFmpeg PID 15956
- Attempt 2 observation: 23:37:55 to 23:38:25; FFmpeg PID 28880
- Last in-progress percentage: 5%; completion percentage: 100%
- Screenshot: `real_dialog_export_attempt1_complete.png`
- Cancellation probe: `rushframe_showcase_edit_real_dialog_cancel_probe_20260713_2340.mp4`; FFmpeg PID 4824 terminated after Cancel
- UIA completion evidence: modal `Export Complete` window with Yes/No controls

### Suspected component

No code defect confirmed. The earlier report is consistent with an automation timeout or an unacknowledged completion prompt hidden by foreground-window conditions.

### Retest

Not reproduced in two unique-path full exports. Both outputs are byte-identical, decode successfully, contain H.264/AAC at 1080x1920 for 25.50 seconds, and produce no configured black/freeze events. No code change was made.

## QA-NEW-012 - Startup silently restores the newest autosave

**Severity:** Major  
**Status:** Retest Passed  
**Build:** local Release build on 2026-07-14

### Steps
1. Leave an autosaved project in the Rushframe autosave directory.
2. Close and reopen Rushframe.

### Expected
Rushframe opens to an empty untitled project. Previously saved work is opened explicitly through File > Open or File > Open Recent.

### Actual
`MainWindow.Loaded` calls `RestoreLatestAutosaveIfAvailableAsync`, replacing the empty startup project without user action.

### Suspected component
`src/Rushframe.Desktop/MainWindow.xaml.cs`

### Retest
Removed automatic autosave restoration from the startup path and added explicit File > Recover Latest Autosave. With 10 existing autosaves in `%LOCALAPPDATA%\Rushframe\autosave`, the Release app opened as `Untitled Project` with `No project assets`. UI Automation also confirmed File > Open Recent and File > Recover Latest Autosave are available.

## QA-NEW-013 - Inspector exposes invalid controls for selected media type

**Severity:** Major  
**Status:** Retest Passed  
**Build:** local Release build on 2026-07-14

### Steps
1. Select an audio-only timeline item.
2. Open the Inspector tab.
3. Inspect the available controls and apply changes.

### Expected
Audio items expose audio and valid timing controls only. Text items expose typography and transform controls. Visual media exposes transform, color, effects, and stabilization where supported.

### Actual
Every item shares the same transform, color, stabilization, timing, and audio fields. Applying the inspector parses and writes unrelated properties, including color settings for audio-only items.

### Suspected component
`src/Rushframe.Desktop/MainWindow.xaml` and `MainWindow.Inspector.cs`

### Retest
Added media-aware inspector profiles for audio, music, voice, text, video, image, sticker, and adjustment items. Unsupported sections and tabs are hidden or disabled, and Apply now parses and writes only properties supported by the selected type. Seven focused desktop regressions covering profiles and recent projects passed; the complete 206-test C# suite passed in Debug and Release.

## QA-NEW-014 - Inspector inputs show the default focus adorner

**Severity:** Minor  
**Status:** Retest Passed  
**Build:** local Release build on 2026-07-14

### Steps
1. Select a timeline item.
2. Click or keyboard-focus a numeric inspector input.

### Expected
The field keeps the editor's compact neutral focus treatment without an extra blue focus rectangle.

### Actual
The inherited WPF focus visual appears around inspector inputs and clashes with the custom dark theme.

### Suspected component
`src/Rushframe.Desktop/App.xaml`

### Retest
Removed the inherited WPF focus visual from text, password, and combo inputs and replaced the accent-filled focus state with a neutral raised surface and control-outline border. Debug and Release XAML builds completed with zero warnings and zero errors; the updated shell started successfully in the live Release smoke test.

## QA-NEW-015 - Main editor theme and action controls are visually inconsistent

**Severity:** Minor  
**Status:** Retest Passed  
**Build:** local Debug and Release builds on 2026-07-14

### Steps
1. Open the main Rushframe editor.
2. Compare the shell, panels, timeline controls, and selection states.
3. Hover the main editor buttons.
4. Resize the application window toward its minimum dimensions.

### Expected
The editor uses a consistent violet-related palette, main editor actions are compact icon-only controls with their labels available as hover tooltips, hover states use background fill without a border, and the window cannot be resized below a usable workspace size.

### Actual
The shared palette still uses blue and steel tones, several main editor actions use text labels, the base button hover template adds an accent border, and the current minimum window size allows the editor workspace to become too compressed.

### Suspected component
`src/Rushframe.Desktop/App.xaml`, `src/Rushframe.Desktop/MainWindow.xaml`, `src/Rushframe.Desktop/MainWindow.Preview.cs`, and `src/Rushframe.Desktop/MainWindow.Inspector.cs`

### Retest
Replaced the shared blue/steel shell palette and custom editor interaction accents with violet-black, violet, and lavender values while retaining semantic success, warning, danger, and user-editable content colors. Main editor actions now render as icons with tooltip and automation labels; dynamic playback speed, zoom, animation count, and effect state details move to tooltips. Button and toggle hover templates use background fill only with no hover border. The custom `WM_GETMINMAXINFO` hook now supplies DPI-aware minimum tracking dimensions in addition to maximum dimensions. On a 150% DPI display, a UI Automation resize request of 500 x 300 was clamped to 1680 x 930 physical pixels, matching the 1120 x 620 logical minimum. UI Automation exposed 101 editor elements and confirmed accessible names for Export, playback speed, zoom, inspector Apply, timeline snapping, and window controls. Six focused theme contract tests passed; all 224 C# tests passed in Debug and Release, and both solution builds completed with zero warnings and zero errors.

## QA-NEW-016 - Header menu overflow and preview orientation control are missing

**Severity:** Minor  
**Status:** Retest Passed  
**Build:** local Debug and Release builds on 2026-07-14

### Steps
1. Reduce the Rushframe window width until the left side of the header has limited space.
2. Hover the main menu area and use the mouse wheel.
3. Use the portrait/landscape control in the preview panel header.

### Expected
The main menu can be horizontally scrolled with the mouse wheel while hovered. A compact button in the Preview window's top-right header switches the entire docked Preview window between landscape and portrait layouts. In portrait mode the Preview window becomes a tall central panel, Media and Timeline adapt into the left workspace, Inspector and activity panels remain usable on the right, and preview controls wrap at their normal size. The inner video/canvas, project sequence dimensions, export dimensions, and Rushframe application window remain unchanged.

### Actual
The first implementation changed the sequence canvas dimensions. The second implementation changed only the small monitor border inside the existing wide Preview window, producing an unusably narrow `57x101` monitor while the Preview window itself remained landscape.

### Suspected component
`src/Rushframe.Desktop/MainWindow.xaml`, `MainWindow.xaml.cs`, and `MainWindow.Canvas.cs`.

### Retest
The orientation button remains in the Preview window's top-right header, but it now toggles dock geometry rather than changing the sequence or the monitor border. Portrait mode gives `PreviewBorder` all three editor rows in a tall center column. Media occupies the left upper workspace, Timeline and its toolbar occupy the left lower workspace, and Inspector/activity remains full-height on the right. Returning to landscape restores the original three-column preview-above-timeline layout.

The preview transport changed from one horizontally scrolling row to a wrapping panel with a 76-pixel minimum height. In the live Release portrait retest, all 28 preview elements were on-screen; transport buttons remained approximately `43-59 x 40-41` physical pixels instead of shrinking. The first transport row appeared at `Y=870` and the wrapped final controls at `Y=910`. Timeline snapping remained available at `X=158, Y=499`, Timeline zoom at `X=571, Y=503`, Media remained available on the left, and the Inspector tab stayed at `X=1398, Y=89`.

The Rushframe application window stayed `1830x975` through both toggles. The toggle performs no `IEditCommand`, does not change project/sequence/export dimensions, creates no undo entry, and did not trigger autosave. The accessible name changes between `Switch preview window to portrait` and `Switch preview window to landscape`.

Verification: 11 focused UI/theme contracts passed; all 229 C# tests passed in Debug and Release; Debug and Release builds completed with zero warnings and zero errors; 5 Python media-intelligence tests passed.

## QA-NEW-017 - Portrait panel splitters do not move or lose their adjusted positions

**Severity:** Major  
**Status:** Retest Passed  
**Build:** local Debug build on 2026-07-14

### Steps
1. Switch the Preview window to portrait mode.
2. Drag the Media/Preview vertical boundary.
3. Drag the Preview/Inspector vertical boundary.
4. Drag the Media/Timeline horizontal boundary.
5. Switch to landscape and back to portrait.

### Expected
Each dock boundary moves smoothly within safe minimum sizes. Preview controls, Media, Timeline, and Inspector remain usable, and the adjusted portrait positions survive layout refreshes and orientation round trips.

### Actual
The portrait splitters appeared enabled, but the Media/Preview boundary did not move. Portrait layout recalculation also replaced splitter-adjusted dimensions with fixed calculated sizes.

### Retest
Added portrait-only controlled splitter dragging for the Media/Preview, Preview/Inspector, and upper-workspace/Timeline boundaries. Mouse movement is clamped so the left editing workspace, Preview window, Inspector, upper workspace, and Timeline cannot collapse. User-adjusted portrait Preview, Inspector, and Timeline sizes are retained and reapplied instead of being overwritten by `ApplyLayout`. Landscape continues to use the existing standard splitter behavior.

Live Debug validation moved the Media/Preview splitter from `X=774` to `X=648`, the Preview/Inspector splitter from `X=1387` to `X=1298`, and the horizontal Timeline boundary from `Y=488` to `Y=582`. Preview seek remained visible at `405x43`, Preview fullscreen remained visible at `43x41`, Timeline snapping and zoom remained available, and Inspector remained visible. Switching landscape and back to portrait restored all three adjusted splitter positions exactly.

Verification was limited to the directly related scope: 12 `MainEditorThemeContractTests` passed, including adjacent-panel conservation/clamping and Timeline clamping. No full solution test run was performed.

## QA-NEW-018 - Editor windows cannot be rearranged from their title bars

**Severity:** Major  
**Status:** Retest Passed  
**Build:** local Debug build on 2026-07-14

### Steps
1. Drag the Media, Preview, Timeline, or Inspector title bar.
2. Hover the dragged window over another editor window.
3. Release the mouse.
4. Repeat in portrait and landscape layouts.
5. Close and reopen Rushframe.

### Expected
Editor windows behave like tiled Blender areas: dragging a title bar displays a clear dock target, dropping swaps the complete windows, controls keep their usable sizes, portrait geometry follows the Preview window's new slot, and the arrangement persists across restart. Escape cancels an active drag.

### Actual
The workspace used fixed semantic grid positions. Splitters could resize boundaries, but complete Media, Preview, Timeline, and Inspector windows could not be rearranged by dragging their title bars.

### Retest
Added persistent Left, Center, Bottom, and Right dock slots to `WorkspaceLayout` schema version 3, with migration from version 2. Media, Preview, Timeline, and Inspector now expose draggable title bars with move grips. Dragging beyond the Windows drag threshold captures the pointer, displays a floating window label, and highlights the target title bar/window. Dropping swaps the two complete panel windows, reapplies the adaptive layout, saves `workspace-layout.json`, and reports the exchanged windows. Interactive controls inside the Preview title bar remain clickable and do not start a dock drag. Escape or capture loss cancels without changing the layout.

Portrait mode follows the Preview window when it is moved to the Left, Center, or Right slot. Preview-to-Bottom drops are rejected in portrait mode to prevent an unusable short portrait Preview. Column sizing and splitters use the physical dock slots rather than assuming fixed Media/Preview/Inspector identities.

Live isolated-workspace validation confirmed Media/Inspector, Timeline/Inspector, Preview/Media, and Preview/Timeline swaps. A Preview moved to the right remained portrait with all 29 Preview elements on-screen; after restart the saved arrangement reopened correctly. A later landscape swap moved Preview to the left, and switching back to portrait kept Preview on the left with seek, fullscreen, Media, Timeline, and Inspector controls visible.

Verification was limited to the related `WorkspaceLayoutTests` and `MainEditorThemeContractTests`. No full solution, Release, media, domain, or Python test suite was run.

## QA-NEW-019 - Preview controls show a scrollbar and landscape fails from side docks

**Severity:** Major  
**Status:** Retest Passed  
**Build:** local Debug build on 2026-07-14

### Steps
1. Narrow the Preview window until its lower control strip overflows.
2. Hover the control strip and use the mouse wheel.
3. Move Preview into the Left or Right dock while portrait mode is active.
4. Click the landscape orientation button.

### Expected
The lower Preview control strip has no visible horizontal scrollbar. Mouse-wheel input while hovered scrolls the button strip horizontally. Switching a side-docked Preview to landscape moves the complete Preview window into the wide Center dock and preserves the displaced panel through the normal dock-slot swap.

### Actual
Overflow relied on visible horizontal scrolling behavior. A Preview in Left or Right remained in the tall side dock after selecting landscape, so the orientation icon changed while the complete Preview window did not become landscape.

### Retest
Replaced the wrapping Preview transport panel with a single horizontal `StackPanel` inside `PreviewTransportScrollViewer`. Horizontal scrollbar visibility is `Hidden`, vertical scrolling is disabled, and the PreviewMouseWheel handler maps wheel delta to horizontal offset while the pointer is over the strip.

When portrait switches to landscape, `EnsurePreviewDockedForLandscape` checks the persisted Preview dock slot. Left or Right Preview windows swap with the Center panel, normalize and save the workspace layout, and then apply the landscape geometry. Bottom and Center are already landscape-capable and remain in place.

Live validation reported the Preview transport as horizontally scrollable with zero visible scrollbar descendants. One wheel step changed horizontal scroll percentage from `0` to `100`. For the Left case, the saved layout changed from `preview=left` to `preview=center`; for the Right case it changed from `preview=right` to `preview=center`. Both displayed `Preview window switched to landscape and moved to the center dock`, and the landscape button changed back to `Switch preview window to portrait`.

Verification was limited to the directly related `MainEditorThemeContractTests`: 14 tests passed. No full solution, Release, media, domain, workspace, or Python test suite was run.

## QA-NEW-020 - Dock slots do not enforce the 3x2 adaptive grid rules

**Severity:** Major  
**Status:** Retest Passed  
**Build:** local Debug build on 2026-07-14

### Steps
1. Open the editor in landscape mode.
2. Drag Timeline to another row or horizontal position.
3. Switch Preview to portrait mode.
4. Drag Preview between the left and right sides.
5. Restart Rushframe and inspect the saved workspace layout.

### Expected
The editor uses a three-column by two-row grid. Timeline always occupies exactly two columns and one row. In portrait mode Preview always occupies one column and both rows and can only be placed on the left or right edge. Other windows repack into valid one- or two-cell rectangles without overlap or uncovered cells. Landscape and portrait arrangements persist independently.

### Actual
The previous Left, Center, Bottom, and Right slot model encoded semantic locations rather than grid rectangles. It could not express general two-cell panel areas, and orientation changes needed special-case relocation logic.

### Retest
Replaced semantic dock slots with persisted `PanelGridArea` rectangles in workspace schema version 4. The runtime now maps a logical 3x2 grid onto the WPF grid, validates complete non-overlapping coverage, and uses an adaptive layout planner when title-bar dragging targets another cell. Timeline candidates are restricted to `2x1`. Portrait Preview candidates are restricted to `1x2` at column 1 or column 3. Landscape and portrait layouts are stored separately. Version 2 and 3 workspace files migrate through the existing normalization path.

The WPF workspace now uses three equal star columns and two equal star rows separated by adaptive splitters. Splitters are shown only where adjacent cells belong to different visible windows; they do not cut through a panel that spans multiple cells. The drop overlay highlights the proposed grid rectangle before commit. Dropping inside the panel's existing rectangle is a no-op and does not repack unrelated windows.

Live validation confirmed the default landscape arrangement: Media upper-left, Preview upper-middle, Inspector full-height right, and Timeline lower-left across two columns. The default portrait arrangement placed Preview full-height right, Timeline lower-left across two columns, and Media/Inspector in the two upper cells. Dragging Timeline to the upper-right produced `timeline=1,0,2,1`; Preview adapted to `0,1,2,1`. Dragging portrait Preview to the left produced `preview=0,0,1,2`, while Timeline remained `1,1,2,1`. All saved placements used schema version 4.

Verification was limited to the workspace-grid tests and two docking/XAML contracts: 15 tests passed. No full solution, Release, media, domain, or Python test suite was run.

## QA-NEW-021 - Editor shell controls are inconsistent, clipped, and mis-grouped

**Severity:** Major  
**Status:** Retest Passed  
**Build:** local Debug build on 2026-07-14

### Steps
1. Inspect the main editor icons and resize the application toward its minimum size.
2. Attempt to drag Media, Preview, Timeline, and Inspector from the visible title text instead of the six-dot grip.
3. Use the Preview orientation, fullscreen, and free-layout controls.
4. Open View > Panels and enable Media Intelligence and Production Workflow.
5. Inspect the Project Files, Transform Inspector, dropdown, checkbox, and Intelligence layouts in portrait and landscape modes.

### Expected
Icons communicate their functions consistently. Every panel's empty title-bar surface can initiate docking. Project media is shown as one searchable and filterable file-directory window without a separate side rail. Preview exposes Free, Fullscreen, and orientation controls in its header and restores its adaptive grid position after fullscreen. Transform fields can be packed into one, two, or three columns. Utility panels open as additional Inspector tabs instead of replacing or splitting the Inspector. Dropdowns, editable dropdowns, checkboxes, and Intelligence options remain usable without clipped content. Landscape supports at most five windows and portrait supports at most four.

### Actual
The editor mixed text glyphs with path icons, title dragging depended on narrow grip regions, Media used a side rail plus thumbnail browser, fullscreen restored Preview to a stale fixed cell, Transform inputs consumed one row each, utility panels occupied a separate lower Inspector region, the Extra button duplicated Intelligence entry points, editable ComboBoxes lacked a usable text host, checked boxes had no visible check glyph, and dense Intelligence controls overflowed narrow Inspector cells.

### Retest
Replaced the major editor and utility actions with function-specific vector paths. Media is now a single `Project Files` directory-style `ListView` with Name, Type, Folder, and Duration columns, file/folder search, source-folder filtering, type filters, and local import actions. The side rail and `SideMediaButton` were removed. Media is titled `Project Files` in View > Panels.

Media, Preview, Timeline, and Inspector now use their full empty title-bar regions as drag surfaces; interactive buttons remain excluded. Live dragging directly on `Project Files` moved the complete window to column 3, and dragging directly on the Timeline label moved Timeline to column 2, row 1. No six-dot grip remains.

Preview now has header-level Free, Fullscreen, and orientation controls. Free mode uses the landscape adaptive planner without changing project/export dimensions. Fullscreen overlays the workspace and calls `ApplyLayout` on exit, restoring the exact current grid placement rather than a hardcoded cell. Live Free and Fullscreen entry/exit succeeded after panel rearrangement.

Utility tabs are reparented into `InspectorTabs`; View > Panels can show Intelligence, Workflow, Transcript, Variants, Compositions, and Queue alongside Properties, Effects, and Audio. Opening Media Intelligence and Production Workflow together left both tabs visible. The old activity split remains collapsed. Intelligence content uses grouped two-column checkboxes, labeled provider/model dropdowns, organized actions, and vertical scrolling in short grid cells. The clip-specific Inspector footer is hidden for utility tabs.

Transform fields now offer a one-, two-, or three-column layout selector. Shared ComboBox styling has a separately interactive editable text host, a reserved arrow target, full-width selection content, and a bordered popup. Checkboxes display an explicit vector checkmark when selected. Inspector tabs wrap instead of clipping.

The adaptive planner declares and tests a five-window landscape maximum and four-window portrait maximum. Live minimum-size checks kept Project Files, Preview header controls, Inspector tabs, Intelligence provider/model controls, and analysis actions accessible. One temporary menu crash during retest was traced to a separator cast in `TogglePanel`; menu synchronization now filters with `OfType<MenuItem>()` and the workflow was repeated successfully.

Verification was limited to `MainEditorThemeContractTests`, `PanelRegistryTests`, and `WorkspaceLayoutTests`: 34 tests passed. The Debug desktop project built with zero warnings and zero errors. No full solution, Release, media, domain, or Python test suite was run.

## QA-NEW-022 - Entire title bars move windows instead of dedicated six-dot handles

**Severity:** Minor  
**Status:** Retest Passed  
**Build:** local Debug build on 2026-07-14

### Steps
1. Drag the Project Files, Preview, Timeline, or Inspector title text.
2. Drag the six-dot icon at the top-left of the same window.

### Expected
Only the six-dot handle initiates adaptive-grid docking. The rest of the title bar remains a normal non-draggable surface.

### Actual
The previous shell revision registered each complete title bar as the drag source and removed the six-dot handles.

### Retest
Restored a dedicated `⠿` handle before each window title and registered only `MediaWindowDragHandle`, `PreviewWindowDragHandle`, `TimelineWindowDragHandle`, and `InspectorWindowDragHandle` with the docking controller. The containing title bars no longer expose the move cursor and are not registered as drag sources.

Live validation dragged the `Project Files` title text from `190,126` to `1500,220`; the workspace and status remained unchanged. Dragging its six-dot handle from `72,126` to the same destination moved Project Files to column 3, row 1. Verification was limited to `MainEditorThemeContractTests`: 11 tests passed. No full desktop or solution suite was run.

## QA-NEW-023 - Portrait utilities ignore empty grid cells and duplicate title icons

**Severity:** Major  
**Status:** Retest Passed  
**Build:** local Debug build on 2026-07-14

### Steps
1. Inspect the Project Files, Preview, Inspector, and Timeline title bars.
2. In portrait mode, open Media Intelligence while all four primary windows are visible.
3. Close Inspector while Media Intelligence remains open.
4. Reopen Inspector, switch to landscape, return to portrait, close Intelligence, and restart Rushframe with Inspector closed and Intelligence open.

### Expected
Each window title uses only the six-dot move handle and title text; decorative panel icons are absent. Utilities use a free portrait grid area as one tabbed utility window when a primary window is closed. If no area is free, utilities remain Inspector tabs. Reclaiming the cell, changing orientation, closing the last utility, or restarting must not create overlap, duplicate tabs, hidden hosts, or more than four portrait windows.

### Actual
Title labels included a second decorative icon after the six-dot handle. Utility panels were always reparented into Inspector, even when a closed primary panel left a valid portrait grid cell unused.

### Retest
Removed the decorative Media, Preview, Inspector, and Timeline title icons. Added a standalone tabbed utility host with the same dedicated six-dot drag handle and title-only label. `WorkspaceUtilityPlacementService` finds the largest rectangular empty area without overlapping visible primary windows, respects the portrait four-window cap, rejects damaged overlapping layouts, and falls back to Inspector tabs when no valid area exists. The utility host and splitters use the same physical grid mapping as the four primary windows.

Utility tabs now move atomically between `UtilityTabs` and `InspectorTabs`, preserving the selected tab. Opening paths are centralized in `TogglePanel`, which selects newly opened utilities and persists panel state regardless of whether View > Panels or command search initiated the action. Reopening a primary panel moves utilities back before the cell is reclaimed. Landscape always uses Inspector tabs; returning to portrait separates utilities again when a cell is free. Closing the last utility collapses the host.

Live validation confirmed: all-primary portrait kept Intelligence in Inspector; closing Inspector moved Intelligence into the freed top-middle cell as the fourth window; reopening Inspector restored it as a tab; landscape/portrait round trips did not duplicate it; closing Intelligence removed the utility host; and restart persistence restored the Inspector-closed + Intelligence-open standalone portrait window. Related `WorkspaceLayoutTests` and `MainEditorThemeContractTests` passed 32 tests. No full solution, Release, media, domain, or Python suite was run.

## QA-NEW-024 - Orientation and window closing leave empty cells; marker and close workflows crash

**Severity:** Critical  
**Status:** Retest Passed  
**Build:** local Debug build on 2026-07-14

### Steps
1. Switch repeatedly between portrait and landscape with one or more closeable windows hidden.
2. Close Project Files or Inspector and inspect adjacent grid cells.
3. Close and reopen Inspector tabs, then add a tab using the Inspector `+` control.
4. Click the project name beside MCP, rename it, and press Enter.
5. Add a timeline marker and save it.
6. Close a dirty project and choose Don't Save.

### Expected
Visible windows compact into a complete non-overlapping layout. In landscape, Preview absorbs adjacent free cells before the editor leaves a blank area. Timeline and Preview remain non-closeable; Project Files, Inspector, and the standalone utility window expose close controls. Every Inspector and utility tab has a working X, and `+` restores the next available tab. Project rename updates project metadata and revision. Marker and application-close workflows must not terminate the process.

### Actual
Hidden primary windows retained their stored grid rectangles, producing visual holes during orientation changes. Preview did not absorb nearby free landscape space. Project Files, Inspector, and utility windows lacked close controls. Core Inspector tabs were not closable, and the first `+` implementation relied on an unrealized WPF popup. Project name was displayed in Preview instead of the app header. Adding a marker crashed in the shared dialog wrapper because the same UI element was reparented while still attached to the Window. Recent crash records also showed reentrant `Close()` calls during `OnWindowClosing`, and the track-header context menu used brittle fixed-index casts across separators.

### Retest
Added `WorkspaceVisibleLayoutService`, which derives effective visible rectangles from stored areas while preserving Timeline `2x1` and portrait Preview edge-only `1x2` rules. It prioritizes complete six-cell coverage and rewards Preview for absorbing adjacent free landscape cells. Splitters and hit testing now use effective rectangles. Closing a primary window is rejected when the remaining windows and any reserved portrait utility cannot form complete, non-overlapping coverage.

Project Files, Inspector, and the standalone utility window now have close buttons; Timeline and Preview intentionally do not. Inspector Properties, Effects, Audio, and all utility tabs use the same closable header. The Inspector `+` button deterministically restores the next available closed tab, avoiding popup realization failures. Live validation closed Effects, removed it from the tab strip, and restored it with `+` with status `Added Effects Inspector tab`. Closing Intelligence removed its utility tab and panel state.

Moved project-name editing next to MCP. Clicking the name opens an inline editor; Enter validates and commits the name through `ProjectSaveCoordinator.BeginMutation`, increments project revision once, updates timeline revision, and marks the project dirty. Preview now displays the title `Preview` rather than the project name.

Fixed `DialogTheme.WrapContent` by detaching `dialog.Content` before reparenting it into the custom frame. This fixes the reproduced marker crash and protects Marker, Animation, Canvas Settings, Creative Assets, and Keyboard Shortcuts dialogs that share the wrapper. Fixed the track menu to resolve items by header instead of casting fixed indexes. Fixed dirty-window closing by queuing the permitted `Close()` at dispatcher idle after the canceled Closing event completes.

Live validation created a marker successfully, renamed the project, opened the unsaved-changes dialog, chose Don't Save, and confirmed Rushframe exited normally. The validation session produced `NEW_CRASH_EVENTS=0`. Related `WorkspaceLayoutTests` and `MainEditorThemeContractTests` passed 36 tests. No full solution, Release, media, domain, or Python suite was run.

## QA-NEW-025 - Inspector Apply destroys hidden transform, speed, and color state

**Severity:** Critical  
**Status:** Retest Passed  
**Build:** local Debug source audit on 2026-07-14

### Steps
1. Create an item with different `ScaleX` and `ScaleY`, a segmented speed curve, `PreservePitch=false`, and non-default exposure or tint.
2. Change an unrelated visible Inspector field such as opacity or volume.
3. Click Apply, then Undo.

### Expected
Only changed fields are committed. Hidden state remains exact, and Undo restores every prior value.

### Actual
Inspector Apply writes both scale axes from one field, replaces the full speed curve with a constant curve, replaces the full color-correction object with four visible fields, and creates no-op commands for unchanged values. Undo cannot restore the lost vertical scale because the Inspector snapshot stores only one scale value.

### Impact
Manual Inspector use can silently alter exact preview/export output, erase agent-created ramps and grading, pollute undo history, and increment project revision without a meaningful change.

### Suspected component
`MainWindow.Inspector.cs`, Inspector value snapshots, and edit-command construction.

### Resolution and validation
Inspector Apply now uses independent X/Y scale fields, changed-only command planning, full transform snapshots, speed-curve cloning, and color-correction merging that preserves hidden grading fields. No-op Apply no longer enters undo history or increments revision. Regression coverage passed in `InspectorValueLogicTests`, `InspectorContractTests`, and `AdvancedEditingCommandTests`. The full Debug and Release C# suites passed, including 272 Release tests.

## QA-NEW-026 - Inspector accepts non-finite values and silently normalizes invalid colors

**Severity:** Critical  
**Status:** Retest Passed  
**Build:** local Debug source audit on 2026-07-14

### Steps
1. Enter or paste `NaN` or `Infinity` into a numeric Inspector field.
2. Apply a fade or transition value, or apply an effect parameter.
3. Enter an invalid text color and apply it.

### Expected
Only finite numeric values and valid colors are accepted. Invalid input remains visible with field-level validation and must not mutate state.

### Actual
`double.TryParse` accepts non-finite values. Fade/transition conversion can throw before the guarded command path, while other fields can persist invalid floating-point state. Invalid colors silently become fallback white or black.

### Impact
The editor can crash, fail persistence/rendering, or commit a result different from what the user entered.

### Suspected component
Inspector numeric/color parsing and effect-parameter validation.

### Resolution and validation
Inspector, effect, and agent numeric parsing now requires finite values. Invalid colors remain rejected with field-level validation instead of silently becoming fallback colors. Focused finite-number and color tests passed, the full C# suites passed, and project/render validation completed without non-finite state.

## QA-NEW-027 - Inspector support profiles do not match exact renderer behavior

**Severity:** Critical  
**Status:** Retest Passed  
**Build:** local Debug source audit on 2026-07-14

### Steps
1. Add a text item and set scale, rotation, alignment, bold, font family, shadow blur, or an effect.
2. Compare realtime preview with exact preview/export.
3. Select text, image, sticker, or adjustment-layer items and inspect Timing/Audio controls.

### Expected
Visible Inspector controls describe behavior supported by exact preview/export, with realtime preview using the same coordinate and typography semantics.

### Actual
The FFmpeg text path omits or differently interprets several Inspector-visible properties. Static/generated items expose speed/reverse controls that have no meaningful exact-render effect. Visual fades are hidden inside the Audio tab for items without audio, and adjustment layers expose transform/timing properties ignored by the renderer.

### Impact
Users cannot trust the Inspector or realtime preview to predict final output.

### Suspected component
`InspectorProfile`, realtime text composition, and `FfmpegTimelineRenderer` text/adjustment paths.

### Resolution and validation
Profiles now expose only renderer-supported timing, transform, and audio capabilities, with stream-neutral fades in Properties. Realtime and exact text rendering share layout metrics and support center-relative position, independent scale, rotation, bold/font selection, alignment, outline, shadow blur, effects, masks, opacity, fades, transitions, and blend mode. A real FFmpeg advanced-composition export with those properties passed and produced valid video and audio.

## QA-NEW-028 - Pending Inspector edits and locked/unsupported tab actions are unsafe

**Severity:** Major  
**Status:** Retest Passed  
**Build:** local Debug source audit on 2026-07-14

### Steps
1. Edit an Inspector field without applying.
2. Select another item or transition.
3. Select a locked item, an image, text, adjustment layer, or transition and open Effects/Audio.
4. Use effect or extract-audio actions where the selected profile does not support them.

### Expected
Pending edits are explicitly resolved before selection changes. Unsupported and locked editors are disabled while tab headers remain usable.

### Actual
Selection refresh silently discards pending values. Effects and Audio remain actionable outside the disabled Properties panel, and extract audio is enabled for any media-backed item rather than only supported video sources.

### Impact
User input is lost without warning, and the Inspector presents actions that fail late or target unsupported media.

### Suspected component
Selection routing, dirty-state handling, Inspector action state, and command `CanExecute` logic.

### Resolution and validation
Selection changes, mutations, undo/redo, save, close, and project replacement now resolve pending Inspector edits through an Apply/Discard/Cancel decision. Locked and unsupported Effects/Audio editors are disabled while tab headers remain usable. Extract Audio now requires a valid online video asset. Source contracts, focused Inspector tests, and the full Release suite passed.

## QA-NEW-029 - Effect duplicate/redo and preview manipulation are not exact mutations

**Severity:** Major  
**Status:** Retest Passed  
**Build:** local Debug source audit on 2026-07-14

### Steps
1. Duplicate a disabled effect.
2. Undo and redo an added effect while retaining its ID as a reference.
3. Drag a preview transform while autosave or another reader can observe project state.

### Expected
Duplicate preserves enabled state. Redo restores the same effect identity. Preview manipulation commits only through one undoable command and does not expose unrevisioned canonical mutations.

### Actual
Duplicate creates an enabled effect. `AddEffectCommand` creates a new instance and ID on redo. Preview drag directly mutates the project item until drag completion, then restores and replays the final transform through a command.

### Impact
Rendering state and references can change across redo, and autosave/agent readers can observe state not represented by revision or undo history.

### Suspected component
Effect commands and `MainWindow.PreviewInteraction.cs`.

### Resolution and validation
Effect duplicate preserves enabled state, and `AddEffectCommand` reuses the same effect identity and insertion index across undo/redo. Preview manipulation now edits a temporary working transform and commits only once through `UpdateTransformCommand`. Exact identity/order and preview-working-copy regression tests passed.

## QA-NEW-030 - Imported fonts and Inspector tab lifecycle are disconnected

**Severity:** Major  
**Status:** Retest Passed  
**Build:** local Debug source audit on 2026-07-14

### Steps
1. Import a local `.ttf` or `.otf` asset.
2. Edit a text item and attempt to choose the imported font.
3. Type a font name directly, close an Inspector core tab, and restore it with the `+` control.

### Expected
Registered local font assets are available through a contained, project-aware Inspector choice. Editable font text marks the Inspector dirty. Core tabs expose working close controls and the `+` control restores hidden tabs.

### Actual
The Inspector enumerates installed system fonts only, imported font assets are unused, editable font text is not reliably tracked, and the current core-tab header implementation has no wired close button while the Add Tab control/menu does not reliably restore hidden core tabs.

### Impact
Manual font import is not usable for text styling, arbitrary font paths can bypass the registered-media workflow, and advertised Inspector tab controls are incomplete.

### Suspected component
Font asset integration, Inspector typography wiring, and `MainWindow.UiShell.cs`.

### Resolution and validation
The font selector now includes registered project font assets and installed system fonts, tracks editable text, rejects arbitrary unregistered font paths, and resolves Windows system families to local font files for exact FFmpeg rendering. Core and utility Inspector tabs have wired close controls and deterministic restoration through the `+` control and title menu. Agent font-path guardrail tests, tab source contracts, the real FFmpeg export, and the full C# suites passed.

## QA-NEW-031 - Adaptive windows can compress below usable size and utility overflow is orientation-specific

**Severity:** Major  
**Status:** Retest Passed  
**Build:** local Debug source audit on 2026-07-14

### Steps
1. Resize Rushframe toward its minimum width or height, including a UI scale above 100%.
2. Keep Preview and Timeline visible with three primary windows, then open an AI or other utility panel.
3. Repeat in portrait and landscape, then reopen the missing primary window.

### Expected
The shell applies viewport-aware guardrails before a panel becomes unusably narrow or short. Utility overflow follows the same rule in portrait and landscape: use a free grid cell only when it is large enough, otherwise keep the utility as a tab. Reopening a primary window retracts the utility into tabs without moving, shrinking, or hiding Preview or Timeline.

### Actual
All grid columns and rows are always equal `1*` tracks, independent of panel minimum content needs. Utility panels can become standalone windows only in portrait, while landscape always forces them into Inspector tabs. The placement decision considers cell occupancy but not the actual pixel width or height, so narrow or short cells can clip dense content.

### Impact
Workspace controls can become inaccessible, and equivalent window arrangements behave differently by orientation. Preview and Timeline can lose usable space when secondary windows are added.

### Suspected component
`AdaptiveWindowService`, workspace placement services, `MainWindow.Layout.cs`, and `MainWindow.UiShell.cs`.

### Resolution and validation
Added UI-scale-aware native minimum-size guardrails and pixel-aware panel minimums. Utility placement now uses the same free-cell policy in portrait and landscape and falls back to Inspector tabs whenever any primary or utility cell would be too small. Reopening a primary window retracts the utility host into tabs, and stored Preview/Timeline rectangles are preserved when a utility occupies the exact free cell. Preview and Timeline are non-closeable, and older layouts that hid them normalize back to open. Adaptive layout and shell contracts passed within the 127-test Desktop suite; the full Debug and Release suites passed with 295 tests each. A real Release WPF startup completed with exit code 0.

## QA-NEW-032 - AI tab passes the wrong speech-model value and allows unsafe or inconsistent actions

**Severity:** Major  
**Status:** Retest Passed  
**Build:** local Debug source audit on 2026-07-14

### Steps
1. Select Small or Medium in the AI tab and start analysis.
2. Select a timeline clip without selecting its media-library row and click Open analysis results.
3. Start analysis and inspect the Apply/Search/Open action states.
4. Apply imported analysis to a locked target or locked generated-content track.

### Expected
The lowercase model identifier from the ComboBox tag is passed to Python. Every AI action resolves the same selected local asset. Conflicting actions are disabled while analysis runs. A rejected timeline apply leaves project analysis and timeline state unchanged.

### Actual
The speech-model helper reads display text (`Base`, `Small`, `Medium`) instead of the lowercase tag expected by the Python/Whisper pipeline. Open analysis results only accepts a media-list selection while other AI actions accept a selected timeline clip. Click-only AI actions remain independently enabled during analysis. Analysis is stored in the project before the edit command is known to succeed, so a rejected apply can leave partial project state.

### Impact
Model loading can fail or select an unintended model, AI actions disagree about the current target, concurrent clicks can race, and failed application can violate the no-partial-mutation contract.

### Suspected component
`MainWindow.Media.cs`, AI tab control state, and media-intelligence application coordination.

### Resolution and validation
The AI tab now passes normalized lowercase speech-model tags, uses one asset resolver for media-list and timeline selections, adapts dense options to one or two columns, and disables conflicting controls throughout analysis/import/apply operations. Transcript/audio/visual dependent controls and CLI flags now follow their parent analysis modes. Analysis and import store project metadata without automatically mutating the timeline; Apply is explicit, guarded by the shared edit-command path, and restores the exact previous project-analysis entries when a locked or invalid target rejects the edit. Python analysis supports cancellation and kills the worker process tree. UI policy, rollback, responsive, and source-contract tests passed; the Python media-intelligence suite passed 5 tests.

## QA-NEW-033 - Release executable crashes while loading the AI tab XAML

**Severity:** Critical  
**Status:** Retest Passed  
**Build:** local Release build on 2026-07-14

### Steps
1. Build `Rushframe.slnx` in Release.
2. Launch `Rushframe.Desktop.exe` with startup diagnostics enabled.

### Expected
Rushframe creates and displays the main editor window, then exits normally through the QA auto-close hook.

### Actual
`InitializeComponent` throws `XamlParseException` because `MainWindow.xaml` references the undefined resource `AccentMutedBrush`. The process exits with code `-1` before the editor window is created.

### Impact
The compiled Windows application cannot start despite all build and unit tests passing.

### Evidence
Release startup diagnostic: `app.startup_failed|System.Windows.Markup.XamlParseException: Cannot find resource named 'AccentMutedBrush'`.

### Suspected component
AI tab toggle styling in `MainWindow.xaml` and application color resources in `App.xaml`.

### Resolution and validation
Replaced the undefined `AccentMutedBrush` reference with the existing `SelectionBrush`. Rebuilt Release with zero warnings and zero errors, reran all 295 Release tests, and launched the real `Rushframe.Desktop.exe` with startup diagnostics. The editor reached `window.loaded`, auto-closed normally, produced no dispatcher-unhandled event, and exited with code 0.

## QA-NEW-034 - AI analysis importer crashes on nullable numeric fields produced by the local pipeline

**Severity:** Critical
**Status:** Retest Passed
**Build:** local Release build on 2026-07-15

### Steps
1. Run the AI tab-equivalent local analysis profile against `batman entry.mov` with scene, transcript, and audio analysis enabled.
2. Import the generated `media-analysis.json` through `MediaIntelligenceImportService`.
3. Observe an audio event whose optional `clarity` field is JSON `null`.

### Expected
Optional numeric fields represented as JSON `null` import as nullable values. The complete analysis is stored in the project and remains available for Apply, save/reopen, and agent context.

### Actual
`ReadNullableDouble` calls `JsonElement.TryGetDouble` without checking `ValueKind`. `TryGetDouble` throws `InvalidOperationException` for JSON `null`, so Rushframe cannot import a valid analysis file produced by its own local pipeline.

### Impact
The AI tab can complete analysis successfully but fail when storing the result, blocking captions, markers, search, edit planning, persistence, and downstream agent-assisted edits.

### Evidence
Reproduced with `qa testing/manual review/batman-ai-five-edits/current-run/analysis-full-local/media-analysis.json`; stack trace terminates in `MediaIntelligenceImportService.ReadNullableDouble` while reading `audio.events[].clarity`.

### Suspected component
`src/Rushframe.Infrastructure/MediaIntelligenceImportService.cs` numeric JSON helpers.

### Resolution and validation
Numeric readers now require `JsonValueKind.Number` before calling `TryGetDouble`, `TryGetInt32`, or `TryGetInt64`; numeric arrays ignore `null` and non-number entries. Added a regression using the pipeline's nullable metadata, scene-quality, audio-event, music, and moment fields. The focused importer suite passed 4 tests. The corrected full local Batman analysis imported into all five generated Rushframe projects, survived save/reopen, and preserved 14 transcript segments, one scene, one editing moment, and zero warnings. Both Debug and Release repository suites passed 296 C# tests, and the Python suite passed 6 tests on 2026-07-15.

## QA-NEW-035 - Local scene detector emits negative starts that the project importer discards

**Severity:** High
**Status:** Retest Passed
**Build:** local Release build on 2026-07-15

### Steps
1. Run the built-in Video parser or full local AI analysis against `batman entry.mov`.
2. Inspect the generated first scene and moment range.
3. Import the generated analysis into a Rushframe project.

### Expected
The local pipeline produces non-negative media times. Importing the pipeline output preserves the detected scene and derived editing moment.

### Actual
PySceneDetect reports the first scene at `-0.033333` seconds. The pipeline serializes that value unchanged, while `MediaIntelligenceImportService` rejects all ranges whose start is below zero. The import therefore drops the only scene and moment and adds two warnings.

### Impact
A successful AI analysis can appear empty or incomplete inside the editor. Scene markers, hook search, agent context, Apply, and persisted analysis lose valid beginning-of-file data.

### Evidence
The first completed Batman edit manifest reported `ai_scene_count=0` and `ai_warning_count=2`, while the source analysis JSON contained one usable scene and one moment beginning at `-0.033333`.

### Suspected component
`rushframe_intelligence/scene_detector.py` and defensive range normalization in `MediaIntelligenceImportService`.

### Resolution and validation
The Python detector now clamps scene starts to zero and guarantees each end is no earlier than its normalized start. The C# importer also defensively normalizes negative scene and moment starts to zero before validating the range. Added Python and C# regressions. Regenerated `analysis-full-local-fixed/media-analysis.json` contains one scene, 14 transcript segments, one editing moment, and zero warnings; all five final edit manifests preserve those counts after project save/reopen. The Python suite passed 6 tests and both Debug and Release C# suites passed 296 tests on 2026-07-15.

## QA-NEW-036 - Explicit save and serialization can lose or silently clean newer edits

**Severity:** Critical
**Status:** Retest Passed
**Build:** local Release build on 2026-07-15

### Steps
1. Begin an explicit project save at revision R.
2. Apply another edit while the asynchronous write is in progress, producing revision R+1.
3. Observe `SaveCurrentProjectAsync` after the revision-R write completes.
4. Serialize a project with missing defaults or stale schema metadata through `ProjectSaveCoordinator`.

### Expected
Only the exact saved revision may be marked clean. Newer edits remain dirty and close protection remains active. Serialization operates on an isolated snapshot and never mutates the live project from a worker thread.

### Actual
`SaveCurrentProjectAsync` unconditionally sets `_projectDirty = false` after the awaited save. `ProjectSerializer.Serialize` updates schema, workflow defaults, providers, variants, and overview on the passed live project while snapshot serialization runs through `Task.Run`.

### Impact
A newer manual edit can be lost without a close warning, and background persistence can mutate project state outside edit commands, revision tracking, and WPF thread ownership.

### Suspected component
`MainWindow.Project.cs`, `ProjectSaveCoordinator`, and `ProjectSerializer`.

### Resolution and validation
Explicit save now returns the exact written revision and clears `_projectDirty` only when the live project still matches it; newer revisions remain dirty and are requeued for autosave. `ProjectSerializer.Serialize` creates and normalizes an isolated project snapshot instead of mutating the live object, including schema/default/overview updates. Regression coverage verifies pure serialization and exact saved-revision behavior. The focused persistence suite passed, and the complete Debug and Release suites each passed 336 C# tests.

## QA-NEW-037 - Preview overwrite is a multi-revision partial mutation

**Severity:** Critical
**Status:** Retest Passed
**Build:** local Release build on 2026-07-15

### Steps
1. Mark a source range and choose Overwrite at a playhead position overlapping multiple timeline items.
2. Include a locked overlapping item or force the final add to reject.
3. Inspect timeline state, undo history, and project revision.

### Expected
Track creation, overlap removal, and replacement insertion are one prevalidated atomic command, one undo entry, and one revision increment. Any rejection leaves the timeline unchanged.

### Actual
The UI directly creates a track, executes each delete separately, then executes the add separately. A late failure can leave earlier deletions committed and one overwrite can create several undo entries and revisions.

### Impact
Manual overwrite can destroy timeline content and cannot be undone as one logical edit.

### Suspected component
`MainWindow.Preview.cs` overwrite path.

### Resolution and validation
Preview overwrite now resolves and validates the destination first, creates a prepared track through an edit command when required, and executes all overlap deletions plus the replacement insertion as one `CompositeEditCommand`. A rejection restores the complete sequence snapshot, creates no undo entry, and increments no revision. Release safety contracts and adversarial command tests passed.

## QA-NEW-038 - Locked tracks and locked downstream clips remain mutable

**Severity:** Critical
**Status:** Retest Passed
**Build:** local Release build on 2026-07-15

### Steps
1. Lock a track, then invoke delete-track, rename, reorder, duplicate, mute, or solo commands.
2. Place an individually locked clip after another clip.
3. Ripple-move, ripple-trim, or ripple-delete the earlier clip.

### Expected
Every affected track and item is validated before mutation. Locked targets reject the complete operation with no state change or undo entry.

### Actual
Track commands other than lock-toggle do not reject locked tracks. Ripple operations validate only the selected item and then move locked downstream clips.

### Impact
Core data-integrity locks can be bypassed by manual and agent command paths.

### Suspected component
`TrackCommands.cs`, `MoveClipCommand.cs`, `TrimClipCommand.cs`, and `RippleDeleteClipCommand.cs`.

### Resolution and validation
Track mutations now reject locked tracks except the explicit lock toggle. Ripple move, trim, and delete precompute every downstream target and reject before mutation when any affected item is locked. Focused lock and editing-integrity regressions verify unchanged state and clean undo history on rejection; all Domain tests pass in Debug and Release.

## QA-NEW-039 - Failed commands can destroy undo history or leave composite partial state

**Severity:** Critical
**Status:** Retest Passed
**Build:** local Release build on 2026-07-15

### Steps
1. Put a command in undo or redo history whose Undo/Execute returns failure or throws.
2. Execute a composite in which a later child fails or throws and a rollback child also fails.
3. Inspect history and sequence state.

### Expected
History entries move stacks only after successful completion. Failed or exceptional execute/undo/redo restores the exact pre-operation sequence. Composite execution and undo are atomic even when child rollback fails.

### Actual
`UndoRedoStack` removes history entries before executing them. `CompositeEditCommand` does not catch child exceptions, ignores rollback failures, and can partially undo before returning failure.

### Impact
A rejected undo/redo can become unrecoverable, and a logical edit can corrupt timeline state despite reporting failure.

### Suspected component
`UndoRedoStack.cs` and `CompositeEditCommand.cs`.

### Resolution and validation
`UndoRedoStack` now moves entries only after successful execute/undo/redo and restores the exact pre-operation sequence when a command fails or throws. `CompositeEditCommand` uses the same sequence snapshot boundary, so child failures, rollback failures, and exceptions cannot leave partial state. Snapshot restoration preserves matching track/item object identity by stable ID. Adversarial execute, undo, redo, exception, and rollback regressions passed.

## QA-NEW-040 - External agents can disable editor approval policy

**Severity:** Critical
**Status:** Retest Passed
**Build:** local Release build on 2026-07-15

### Steps
1. Send an agent timeline edit, edit plan, workflow decision, variant/composition mutation, render, or retry request.
2. Include `require_approval:false` in the caller-controlled payload.
3. Observe whether the editor displays its approval UI.

### Expected
Approval policy is owned by the editor. External payloads cannot disable required human review. Preview-only requests remain non-mutating, and internally approved retries prompt exactly once.

### Actual
Multiple mutating handlers read `require_approval` directly from the request and skip confirmation when false. The Python MCP schema advertises that bypass field.

### Impact
A caller with the session token can silently apply edits, alter production state, register compositions, or render outputs without the user approval required by the product boundary.

### Suspected component
Agent handlers in `MainWindow.xaml.cs` and public MCP schemas in `rushframe_intelligence/backend.py`.

### Resolution and validation
Caller-controlled approval flags were removed from the MCP schemas and all consequential editor handlers. Edits, plans, workflow decisions, variants, compositions, renders, and retries now follow editor-owned approval policy. Internally retried renders use a private `approvalAlreadyGranted` path only after the retry dialog succeeds, preventing both bypass and duplicate prompts. Desktop source-contract and Python schema tests passed.

## QA-NEW-041 - Render output and receipts use mutable live project state

**Severity:** Critical
**Status:** Retest Passed
**Build:** local Release build on 2026-07-15

### Steps
1. Start a manual or agent timeline/variant export at revision R.
2. Apply a manual edit while the asynchronous render or verification is running.
3. Compare rendered frames, receipt project revision/graph hash, source records, warnings, and the live project reference committed afterward.

### Expected
Rendering and verification use one immutable revision-R project snapshot. The receipt describes that same snapshot. Completion metadata is then committed to the current live project in one coordinated mutation without overwriting newer edits.

### Actual
Manual and agent timeline exports pass live `Project`/`Sequence` objects into asynchronous renderer and receipt creation. Receipt creation also mutates the passed project directly, so output, source revision, graph hash, warnings, and stored reference can describe different states.

### Impact
Exports are not reproducible or auditable, and a receipt can certify a graph different from the frames actually rendered.

### Suspected component
`ExportController`, agent render handlers, `MainWindow.Automation.cs`, and `RenderReceiptService`.

### Resolution and validation
Manual timeline, manual variant, agent timeline, and agent variant renders now use a revision-frozen project/sequence snapshot. Receipt generation reads that same snapshot and no longer mutates it. `RenderReceiptService.ApplyToProject` commits only receipt/workflow/variant metadata to the current live project inside one coordinated mutation, preserving newer edits. Behavioral tests verify the receipt revision and graph remain tied to the rendered snapshot while live revisions advance independently.

## QA-NEW-042 - Undoing an applied agent plan leaves project automation state applied

**Severity:** Critical
**Status:** Retest Passed
**Build:** local Release build on 2026-07-15

### Steps
1. Preview and approve an agent edit plan.
2. Apply it, then use the normal editor Undo command.
3. Inspect the timeline, `Project.AgentEditPlans`, and workflow stage state.

### Expected
The timeline mutation, applied-plan record, and workflow transition are one logical undoable action. Undo restores all three; redo reapplies all three.

### Actual
Only the compiled timeline command enters `UndoRedoStack`. The applied record and workflow changes are added afterward outside the command, so Undo restores timeline items while the plan remains recorded as Applied/AwaitingApproval.

### Impact
Agent audit and workflow state can contradict the canonical timeline, making later approvals, retries, and conflict decisions unreliable.

### Suspected component
`ApplyAgentEditPlanAsync`, undo/redo integration, and agent workflow state transitions.

### Resolution and validation
`AgentPlanApplicationCommand` now wraps the compiled timeline edit, applied-plan record, and workflow transition as one undoable command. Undo restores the exact prior timeline, plan list, workflow stages, decisions, and costs; redo reapplies them and refreshes the applied revision/timestamp. Focused behavioral tests cover apply, undo, and redo consistency.

## QA-NEW-043 - Solo and track-order state do not control realtime preview and export consistently

**Severity:** Major
**Status:** Retest Passed
**Build:** local Release build on 2026-07-15

### Steps
1. Create multiple visual/audio tracks and enable Solo on one track.
2. Reorder or duplicate tracks so list order and stored `Track.Order` differ.
3. Compare realtime preview and final export layer/audio selection.

### Expected
When any visible track is soloed, only soloed tracks contribute visual or audio content. Canonical sequence list order controls compositing consistently after reorder/duplicate and survives undo/redo.

### Actual
Realtime and FFmpeg render paths ignore Solo and sort by mutable `Track.Order`. Reorder changes list position without normalizing `Order`, and duplicate can create duplicate order values.

### Impact
Preview/export can include tracks the user explicitly isolated and can composite in an order different from the timeline UI.

### Suspected component
`RealtimeRenderPlan`, `FfmpegTimelineRenderer`, and track-order commands.

### Resolution and validation
Realtime preview and the canonical FFmpeg renderer now apply the same visible Solo rule to both visual and audio tracks. `TrackOrdering.Normalize` makes sequence list order canonical after add, delete, reorder, duplicate, prepared-track insertion, and undo. Focused preview/export state tests verify isolated tracks and consistent compositing order.

## QA-NEW-044 - Clip insertion and deletion can create incompatible tracks or dangling transitions

**Severity:** Major
**Status:** Retest Passed
**Build:** local Release build on 2026-07-15

### Steps
1. Execute `AddClipCommand` or paste with an item kind incompatible with the destination track.
2. Delete or ripple-delete an item referenced by an incoming/outgoing transition.
3. Split the left item of an outgoing transition and inspect transition endpoints; undo each action.

### Expected
Incompatible insertion is rejected before mutation. Delete removes referencing transitions and exact undo restores their original positions. Split reattaches an outgoing transition to the new right-hand item and undo restores the original endpoint.

### Actual
`AddClipCommand` and `PasteClipCommand` validate existence/lock only. Delete/ripple-delete leave dangling transition IDs. Split leaves the outgoing transition attached to the shortened left half.

### Impact
Invalid projects can be created through manual and agent paths; preview/export transition behavior can be wrong or fail after ordinary editing.

### Suspected component
Add/paste/delete/ripple-delete/split edit commands.

### Resolution and validation
Add and paste commands now enforce `TrackCompatibility` before mutation. Delete and ripple-delete remove all incoming/outgoing transitions and store their exact original indices for undo. Split transfers an outgoing transition endpoint to the new right-hand item and restores it on undo. Editing-integrity regressions cover incompatible insertion, transition cleanup, split endpoint behavior, and exact undo.

## QA-NEW-045 - Intelligence undo and group paste can silently change ordering or partially apply

**Severity:** Major
**Status:** Retest Passed
**Build:** local Release build on 2026-07-15

### Steps
1. Reapply media intelligence over existing generated markers/captions, then undo.
2. Copy a multi-track selection where one clipboard item has no compatible unlocked destination.
3. Paste the group and inspect ordering and item count.

### Expected
Undo restores every removed marker, track, and item at its exact original index. Group paste resolves all destinations first and rejects the whole operation when any item cannot be placed.

### Actual
Media-intelligence undo appends removed content. Group paste skips unplaceable items and applies the rest without warning.

### Impact
Undo is not exact and a single paste gesture can silently lose part of the user’s selection.

### Suspected component
`ApplyMediaIntelligenceCommand` and `MainWindow.Paste_Executed`.

### Resolution and validation
Media-intelligence apply records the exact original marker, track, and item indices and restores them in place on undo. Group paste resolves a compatible unlocked destination for every copied item before constructing the composite command; one unplaceable item cancels the complete paste. Single-item paste also enforces destination compatibility. Focused Domain and Desktop source-contract tests passed.

## QA-NEW-046 - Local agent servers expose unbounded or unauthorized request and file access paths

**Severity:** Critical
**Status:** Retest Passed
**Build:** local Release build on 2026-07-15

### Steps
1. Send a chunked bridge body larger than 1 MiB without a declared `Content-Length`.
2. Open many simultaneous bridge requests and stop the editor while they are active.
3. Start the Python backend with a non-loopback host or call `/capabilities`, `/search`, or `/context` without the session token.
4. Supply an arbitrary local `context.sqlite` or `media-analysis.json` path to MCP search/context tools.

### Expected
Actual streamed bytes are bounded, concurrent requests are capped and drained on shutdown, every non-health backend route is session-authenticated, backend binding is loopback-only, and intelligence tools can inspect only analyzed media registered in the open project.

### Actual
The bridge trusts declared length then reads the entire stream and starts an unbounded task per request. Python GET routes are unauthenticated, host binding is caller-controlled, and search/context accept arbitrary local filesystem paths.

### Impact
A local process or exposed backend configuration can exhaust memory/tasks or read analysis databases outside the open Rushframe project.

### Suspected component
`LocalAgentBridgeService`, `rushframe_intelligence/backend.py`, and live editor context endpoints.

### Resolution and validation
`LocalAgentBridgeService` now enforces the 1 MiB limit while streaming chunked bodies, caps concurrent work at eight requests, tracks in-flight requests, and cancels/drains them during shutdown. The Python backend now rejects non-loopback binds, requires the editor session token for every non-health route, and routes search/context through the authenticated editor bridge using registered `media_asset_id` values instead of arbitrary local paths. Focused Release validation passed 3 bridge concurrency/size/shutdown tests, 5 release-safety contract tests, and 3 Python backend security tests.

## QA-NEW-047 - Agent output containment does not resolve network drives or junction escapes

**Severity:** Critical
**Status:** Retest Passed
**Build:** local Release build on 2026-07-15

### Steps
1. Request an output under the saved project directory through an existing junction/reparse point that targets another directory.
2. Request an output on a mapped network drive.
3. Compare lexical prefix validation with the physical target path.

### Expected
Agent outputs remain on a local drive and physically inside the project/export root after resolving existing reparse points. Directory escapes and mapped network drives reject before output creation.

### Actual
Validation checks a normalized string prefix and direct UNC prefixes only. Mapped network drives and junctions can point outside the allowed root while retaining an allowed-looking path.

### Impact
An approved render could write outside the project boundary or onto a network location.

### Suspected component
`ValidateAgentOutputPath`.

### Resolution and validation
Agent render paths now pass through `LocalOutputPathGuard`, which checks lexical containment, resolves existing reparse points to their physical targets, rejects physical escapes, and rejects UNC or mapped network drives. Focused Release validation passed the inside-root, lexical traversal, junction escape, and source-contract tests.

## QA-NEW-048 - Incomplete manual-asset and subtitle refactors break the WPF build

**Severity:** Critical
**Status:** Retest Passed
**Build:** local Debug and Release builds on 2026-07-15

### Steps
1. Build `Rushframe.slnx` after the manual asset/subtitle/campaign command refactors.
2. Observe WPF temporary-project compilation.

### Expected
Every manual insertion path calls the current async/command-based implementation, built-in shapes use the same atomic track-plus-item mutation path, and subtitle parsing resolves its file/data exception types.

### Actual
The built-in shape path still called removed `EnsureAssetTrack`, command search still called removed `AddSelectedMediaToTimeline`, and `SubtitleParser.cs` omitted `System.IO` while using `File`, `FileNotFoundException`, and `InvalidDataException`.

### Impact
The Windows desktop application cannot compile, blocking all runtime QA and release packaging.

### Suspected component
`MainWindow.Assets.cs`, `MainWindow.CommandSearch.cs`, and `Services/SubtitleParser.cs`.

### Resolution and validation
Built-in shapes now create a prepared overlay track and sticker in one `CompositeEditCommand`; command search calls `AddSelectedMediaToTimelineAsync`; subtitle parsing imports `System.IO`. The complete Debug and Release builds pass with zero warnings and zero errors, followed by 336 passing C# tests in each configuration.

## QA-NEW-049 - Campaign action controls violate the editor icon-button contract

**Severity:** Major
**Status:** Retest Passed
**Build:** local Debug and Release builds on 2026-07-15

### Steps
1. Open the production workflow panel XAML or run `MainEditorThemeContractTests`.
2. Inspect the campaign description/task action buttons.

### Expected
Editor action buttons are icon-only, expose a descriptive tooltip, and retain an accessible automation name.

### Actual
Save description, add task, toggle task, and delete task used text content; some lacked tooltips. The shared UI contract rejected the first text button.

### Impact
The new panel was visually inconsistent and less compact than the rest of Rushframe, and hover-label accessibility was incomplete.

### Suspected component
Campaign/task controls in `MainWindow.xaml`.

### Resolution and validation
All four controls now use toolbar paths with the existing icon button styles, descriptive tooltips, and `AutomationProperties.Name`. All 12 `MainEditorThemeContractTests` pass, and both complete C# suites remain green.

## QA-NEW-050 - Export can silently omit offline sources and overwrite an existing or original file

**Severity:** Critical
**Status:** Retest Passed
**Build:** local Debug and Release builds on 2026-07-15

### Steps
1. Export a sequence containing an active missing/offline source.
2. Export over an existing output or choose a registered original source as the destination.
3. Cancel or fail FFmpeg after output creation.

### Expected
Export preflight rejects missing active sources and any source-path collision. Rendering uses a unique temporary file and atomically publishes only a verified result.

### Actual
Missing visual/audio sources are skipped, FFmpeg writes directly to the destination with `-y`, and source-file collisions are not centrally blocked.

### Impact
A visibly incomplete render can pass verification, and a failed export can destroy a valid output or original source.

### Resolution and validation
The canonical renderer now preflights every active visual and audible item, rejects missing/offline or kind-incompatible registered sources, validates timeline and source bounds, and refuses an output path that matches any registered original. FFmpeg renders to a unique sibling temporary file; only a non-empty successful result is atomically moved over the destination, while cancellation or failure removes the temporary file and preserves the previous output. Regressions verify missing muted visual media is rejected, failed rendering preserves an existing output byte-for-byte, and original-source collisions are blocked. All 18 media tests pass in Debug and Release.

## QA-NEW-051 - Portrait export positions fitted video at the bottom instead of canvas center

**Severity:** Critical
**Status:** Retest Passed
**Build:** local Debug and Release builds on 2026-07-15

### Steps
1. Put a landscape source on a landscape sequence.
2. render it as a portrait variant such as 1080x1920.
3. Inspect the fitted video region.

### Expected
Aspect-fitted sequence output is centered horizontally and vertically within the portrait canvas unless an explicit variant/item override moves it.

### Actual
The fitted video appears in the lower portion of the portrait export.

### Impact
Portrait outputs are compositionally incorrect and do not match the editor’s centered-canvas expectation.

### Resolution and validation
Manual timeline export, agent timeline export, and manual/agent variant export now center primary video-track clips and images when a landscape sequence is rendered to a portrait canvas. The adjustment is made only on the revision-frozen render snapshot, does not mutate the live timeline, preserves explicit variant position overrides and position animation channels, and can be disabled with the authored `autoCenterPortrait=false` override. A real FFmpeg integration test rendered a red 320x180 landscape source to 180x320, sampled the output pixels, confirmed red at the portrait vertical center, and confirmed black letterbox below it.

## QA-NEW-052 - Agent render completion can mutate a newly opened project

**Severity:** Critical
**Status:** Retest Passed
**Build:** local Debug and Release builds on 2026-07-15

### Steps
1. Start a long agent timeline, variant, or composition render in project A.
2. Open or create project B before the render completes.
3. Let the original continuation finish.

### Expected
Project replacement is blocked or cancels the operation, and every continuation verifies the original project identity before committing jobs, receipts, variants, workflow, or imported output.

### Actual
New/Open remain available and render completion mutates the current `_project` reference.

### Impact
Render state from project A can corrupt project B.

### Resolution and validation
New/Open Project commands are disabled while media or render work is active, replacement is rechecked after an awaited save, and every manual or agent timeline/variant/composition operation captures the originating project reference, ID, and generation. Continuations verify that context after each long await and before committing render jobs, receipts, workflow state, variants, compositions, or generated media. Stale completions return without changing the newly opened project. Release-safety contracts cover command gating and project-scoped commits, and the Release desktop startup smoke exits cleanly with telemetry.

## QA-NEW-053 - Project-level commands inside sequence composites are not fully atomic and redo identities drift

**Severity:** Critical
**Status:** Retest Passed
**Build:** local Debug and Release builds on 2026-07-15

### Steps
1. Execute a creative-asset composite that registers project media before a later sequence command fails.
2. Undo and redo add/duplicate/paste commands and compare created IDs.

### Expected
Failure restores every project and sequence mutation. Redo restores the same objects and identifiers created by the first execution.

### Actual
Sequence-only snapshots cannot roll back media-library changes, while several commands create new objects/IDs on redo.

### Impact
Failed edits leave partial state and redo can invalidate references.

### Resolution and validation
Composite execution now undoes every completed child when a later child fails and rolls already-undone children forward when composite undo fails; a fallback sequence snapshot remains for non-atomic legacy commands. Project media registration therefore rolls back with the timeline. Production commands opt into `IAtomicEditCommand`, eliminating full-sequence JSON snapshots on ordinary edits while retaining the safety boundary for untrusted commands. Add/duplicate/paste/track/effect/transition/split commands preserve their created objects and IDs across redo. A regression forces failure after project media registration and confirms an unchanged media library and empty undo history; identity and adversarial rollback tests pass.

## QA-NEW-054 - Relink/import accept unprobed or incompatible media metadata

**Severity:** Major
**Status:** Retest Passed
**Build:** local Debug and Release builds on 2026-07-15

### Steps
1. Relink a video asset to a shorter, audio-only, corrupt, or incompatible file.
2. Import a file whose probe fails.

### Expected
Rushframe probes before committing, preserves the asset ID only after compatibility validation, updates metadata, and rejects unreadable files with a visible error.

### Actual
Relink retains stale duration/dimensions and import silently registers zero-duration assets after probe failure.

### Impact
Timeline bounds and export behavior become invalid or misleading.

### Resolution and validation
Video, audio, and image imports are probed and stream-kind validated before registration. Unreadable, duplicate, corrupt, or incompatible files are skipped with a visible per-file diagnostic rather than becoming zero-duration assets. Relink now probes first, preserves the stable media ID only after confirming the same media kind and enough duration for every used source range, and updates duration/dimensions from the replacement. Tests cover kind changes, short replacements, and files without the required stream.

## QA-NEW-055 - Track replacement and segmented speed operations do not preserve exact render semantics

**Severity:** Critical
**Status:** Retest Passed
**Build:** local Debug and Release builds on 2026-07-15

### Steps
1. Replace all items on a track that participates in transitions.
2. Apply a segmented speed curve and split/export the clip.

### Expected
Transitions remain valid or are removed/restored atomically. Segmented speed is either rendered and split correctly or rejected before committing unsupported state.

### Actual
Track replacement leaves dangling transitions; exact export ignores `SpeedCurve.Segments`; split uses only constant `item.Speed`.

### Impact
Saved project state and final output diverge.

### Resolution and validation
Track replacement now validates all replacement items before mutation, rejects duplicate/cross-track IDs, removes transitions whose endpoints would become invalid, records their exact indices, and restores items and transitions on undo. Split now handles reverse clips and constant speed correctly, partitions local animation/keyframes at the split, preserves right-side identity on redo, and rejects segmented ramps. Because segmented speed curves are not yet implemented in the exact renderer, export also rejects them instead of silently producing a constant-speed result. Focused Domain and Media regressions cover transitions, reverse ranges, animation partitioning, stable redo, and renderer rejection.

## QA-NEW-056 - Intelligence cache advertises failed optional features as complete

**Severity:** Major
**Status:** Retest Passed
**Build:** local Python validation on 2026-07-15

### Steps
1. Request transcription/embeddings or another optional feature and force that stage to fail.
2. Run the same analysis again without `--force`.

### Expected
The manifest records only successfully completed features, so a later run retries missing work.

### Actual
The requested feature list is written even when execution falls back or fails.

### Impact
A degraded analysis bundle can be reused indefinitely as a valid cache.

### Resolution and validation
The pipeline now separates requested features from successfully completed features and writes only completed work into the manifest. Embedding fallback explicitly removes `embeddings` while retaining the successfully rebuilt non-embedding context index, so a later requested embedding run is retried. A per-destination lock prevents concurrent runs from deleting or publishing over one another and supports stale-lock recovery. Python regressions verify truthful embedding manifests, lock rejection/recovery, bridge URL hardening, and the existing atomic bundle publication; all 16 tests pass.

## QA-NEW-057 - Local asset and composition containment is lexical and audio library discovery is missing

**Severity:** Major
**Status:** Retest Passed
**Build:** local Debug and Release builds on 2026-07-15

### Steps
1. Place an asset, preview, entry point, or local renderer beneath a junction that resolves outside its declared root.
2. Open Creative Assets and look for safe music/SFX acquisition guidance.

### Expected
Physical paths remain inside local roots after resolving links. Rushframe offers curated external library suggestions that open in the browser for manual download and local import, without downloading or scraping inside the editor.

### Actual
Several validators use lexical prefixes only, and the asset UI has no guided audio/SFX library workflow.

### Impact
Local-only boundaries can be bypassed and users lack a safe, license-aware discovery path.

### Resolution and validation
A shared physical-path guard now follows existing reparse points, rejects lexical and physical root escapes, UNC paths, and mapped network drives. Asset files, previews, extension entry points, composition project directories, Remotion entry points, renderer launchers, and output paths use contained local validation; Windows batch launchers run deliberately through `ComSpec`. Creative Assets includes an Audio Libraries tab, and the dedicated Sound Library window exposes the same curated music/SFX sources through browser-only Browse Libraries actions. Both surfaces explain that Rushframe never downloads or scrapes these sites: the user downloads manually, reviews the exact license/attribution, and imports the validated local file. Junction escape, output containment, external-composition, sound drag/drop, catalog, and UI contract tests pass.

## QA-NEW-058 - Sound Library filtering, indexing, license synchronization, and placement integrity defects

**Severity:** Major
**Status:** Retest Passed
**Build:** local Debug working tree on 2026-07-15

### Steps
1. Register project audio, then apply Favorites, collection, metadata, recent-use, or text filters in Sound Library.
2. Generate repeated filesystem changes while a watched-folder reindex is running.
3. Cause a large worker response or diagnostic stream.
4. Edit catalog license metadata for a registered sound while the project mutation is rejected.
5. Add a frame-snapped sound and compare the status timestamp with the actual item start.
6. Attempt to place a registered audio asset whose duration is unknown.

### Expected
Project fallback rows obey active filters; watched roots coalesce to one active scan plus at most one pending scan; worker output is bounded while reading; catalog/project license metadata remains synchronized; status reports snapped placement time; unknown-duration media is rejected without mutation.

### Actual
Fallback rows bypass filters, watcher scans can queue repeatedly, output limits are checked only after full buffering, license updates can diverge, status reports the unsnapped input time, and unknown duration becomes an invented ten-second clip.

### Impact
Search results are misleading, large libraries can cause avoidable resource pressure, licensing/export state can disagree, and timeline placement can report or render incorrect timing.

### Resolution and validation
Project fallback rows now pass an explicit fallback-query predicate, so active catalog filters cannot be bypassed. Watched roots allow only one active reindex and coalesce filesystem events into at most one pending pass. Python stdout and stderr are bounded while streaming and the process tree is terminated on overflow or cancellation. Catalog license changes update every matching project registration through one composite command and compensate the catalog when the project mutation is rejected. Placement status uses the frame-snapped start, and unknown-duration audio is rejected before creating a track or clip. Focused verification passed: 19 Sound Library C# tests and 12 Python tests. The full Desktop suite completed 198/200 with two unrelated existing UI-contract failures; the full Debug solution build succeeded with 0 warnings and 0 errors. Real WPF import/filter/drag/export listening remains a separate manual QA check.
