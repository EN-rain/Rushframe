# Rushframe Detailed QA Setup and Test Loop

**Application:** Rushframe Desktop Video Editor  
**Repository:** `C:\Users\LENOVO\Desktop\Projectsss\Rushframe`  
**Platform:** Windows x64, .NET 10, WPF  
**Rendering environment:** CPU-only; no GPU-dependent QA tooling  
**Companion acceptance plan:** `qa testing/QA_TESTING_PLAN.md`  
**Active defect log:** `qa testing/manual review/showcase-edit/defect_log.md`  
**Status:** Implementation plan and mandatory release-test procedure

---

## 1. Purpose and Scope

This document defines the QA infrastructure, test isolation, capture process, visual-regression loop, functional UI automation, renderer verification, persistence checks, agent guardrail checks, performance checks, UX review, and reporting rules for Rushframe.

It does not replace the showcase-edit quality bar in `qa testing/QA_TESTING_PLAN.md`. The two plans have different responsibilities:

- this document proves that the editor behaves correctly and reproducibly;
- `QA_TESTING_PLAN.md` proves that Rushframe can produce a polished real edit through the actual editor workflow.

A release candidate must satisfy both plans. A technically correct build with an unreviewed export does not pass. A visually polished showcase with broken editing, persistence, security, or export behavior also does not pass.

Keep the following signals separate:

1. build, unit, integration, and architecture tests;
2. visual screenshot comparison;
3. functional UI automation;
4. exact preview and export verification;
5. persistence, migration, autosave, and recovery;
6. local-agent security, revision, approval, and audit behavior;
7. performance and responsiveness;
8. UX, accessibility, and human feel review.

Do not combine these into one score. Each phase must have its own result and evidence.

---

## 2. Rushframe-Specific Facts the QA System Must Respect

The current source tree is the source of truth. Before changing QA infrastructure, re-check the relevant source and tests rather than relying on this snapshot.

At the time this plan was written:

- `src/Rushframe.Desktop/Rushframe.Desktop.csproj` targets `net10.0-windows`, enables WPF, and declares `win-x64` runtime support.
- `Rushframe.slnx` contains the production projects and deterministic non-UI test projects.
- `qa testing/harness` is a direct project-load and FFmpeg timeline-export harness. It bypasses the WPF UI and therefore cannot serve as the functional UI harness.
- `qa testing/scripts/Capture-RushframeWindow.ps1` already performs real Win32 `PrintWindow` capture.
- `qa testing/scripts/Control-RushframeUi.ps1` and related scripts already provide transitional UI Automation support.
- the main WPF shell has many named controls, but an audit found no explicit `AutomationProperties.AutomationId` assignments in the inspected desktop source.
- no approved `design-reference` directory currently exists.
- the active showcase defect log already contains `QA-NEW-###` records. Continue the existing sequence; never restart numbering.

These findings imply that the visual gate is currently **BLOCKED** until approved design references exist, and stable UI automation requires an AutomationId/accessibility pass before broad FlaUI coverage can be reliable.

---

## 3. Non-Negotiable Product and QA Boundaries

All QA work must preserve Rushframe’s product rules:

- manual import of local video, audio, music, images, logos, fonts, subtitles, and other supported assets remains first-class;
- do not add social-media downloaders, URL importers, scraping, or arbitrary website acquisition;
- never overwrite or modify original source media;
- normal timeline mutations must use the same undoable command paths as the editor;
- manual edits win over stale agent requests;
- agent mutations require authorization, revision validation, approval where configured, audit records, and registered local project media;
- startup must open an untitled project and must not silently restore the newest autosave;
- exact preview and final export must stay aligned with the canonical FFmpeg timeline renderer;
- extension manifests remain metadata only and must not execute arbitrary code;
- local asset packs must remain path-contained, offline, and license aware;
- core QA must remain local and CPU-only.

QA-only hooks may isolate storage, force deterministic software composition, disable nonessential animation, or expose diagnostic state, but they must be opt-in, clearly named, unavailable to agent payloads, and must not create a second editing or rendering implementation.

---

## 4. Required Repository Layout

Use the following layout for new QA infrastructure:

```text
qa testing/
├── DETAILED_QA_TESTING_PLAN.md
├── QA_TESTING_PLAN.md
├── design-reference/
│   ├── manifest.json
│   ├── shell.untitled.png
│   ├── shell.project-open.png
│   └── ...
├── fixtures/
│   ├── projects/
│   ├── media/
│   ├── asset-packs/
│   ├── extensions/
│   └── expected/
├── ui-automation/
│   ├── Rushframe.UiAutomation.Tests.csproj
│   ├── Infrastructure/
│   ├── States/
│   ├── Visual/
│   ├── Functional/
│   └── README.md
└── results/
    └── ui/
        ├── current/
        ├── diff/
        ├── reports/
        ├── logs/
        └── failed/
```

Rules:

- `qa testing/design-reference/` is approved ground truth and must never be rewritten by an ordinary test run.
- `qa testing/fixtures/` contains synthetic or explicitly licensed local QA media only.
- `qa testing/results/ui/` is generated evidence. Do not treat it as source code.
- keep the existing `qa testing/harness` as a direct renderer smoke/fallback tool; do not silently turn it into UI automation.
- keep headed UI tests out of the default `Rushframe.slnx` test gate unless a stable dedicated Windows runner is configured. Run them explicitly.

---

## 5. Phase 0 — Baseline, Prerequisites, and Stop Conditions

### 5.1 Preserve the current working tree

Before any implementation or QA run:

```powershell
git status --short
```

Read:

1. `AGENTS.md`;
2. `AGENT_CONTEXT.md`;
3. `LOCAL_AGENT_INSTRUCTIONS.md`;
4. this plan;
5. `qa testing/QA_TESTING_PLAN.md`;
6. the active defect log;
7. files and tests related to the workflow under test.

Never reset, clean, discard, overwrite, or reformat unrelated modified, deleted, or untracked files.

### 5.2 Verify the toolchain

Confirm:

- the installed .NET SDK supports the project target framework;
- the desktop project still targets the expected Windows framework and runtime;
- FFmpeg is available for export and exact preview;
- FFprobe is available for metadata verification, or the documented FFmpeg fallback is usable;
- PowerShell can execute the existing QA scripts;
- the machine is using a real interactive Windows desktop session, not Session 0 or a locked desktop;
- no GPU-only, CUDA-only, DirectML-only, or local vision-model dependency has been introduced.

Record exact versions in the run report:

```powershell
dotnet --info
& $ffmpegPath -version
& $ffprobePath -version
```

### 5.3 Establish a clean code baseline

Run the required repository gates before adding QA tooling:

```powershell
dotnet build Rushframe.slnx
dotnet test Rushframe.slnx
python -m pytest tests\test_media_intelligence_v2.py -q

dotnet build Rushframe.slnx -c Release
dotnet test Rushframe.slnx -c Release --no-build

dotnet list Rushframe.slnx package --vulnerable --include-transitive
```

Do not hardcode an expected test count in the plan. Record the actual count from the current run. A pre-existing failure must be separated from a newly introduced failure and documented honestly.

### 5.4 Launch the actual Release editor

Build and launch the Release desktop application. Verify at minimum:

- the process starts;
- the main window becomes responsive;
- startup shows an untitled project;
- no autosave is silently restored;
- the main media, preview, timeline, inspector, task, and automation surfaces are reachable;
- the app can close cleanly.

A successful build without a successful real launch is not a valid UI baseline.

### 5.5 Approved design references are mandatory for visual PASS

The visual reference folder must contain one approved PNG per captured state and a manifest describing the environment and state.

The manifest must include:

```json
{
  "schemaVersion": 1,
  "windowClientWidth": 1600,
  "windowClientHeight": 900,
  "displayScalePercent": 100,
  "theme": "dark",
  "culture": "en-US",
  "captureMode": "client",
  "states": [
    {
      "id": "shell.untitled",
      "reference": "shell.untitled.png",
      "fixtureProject": null,
      "notes": "Fresh startup with no autosave recovery"
    }
  ]
}
```

Do not generate a reference automatically from the current build and immediately call it approved. A human must review and explicitly approve a new or changed reference.

If the folder or any required state is missing:

- mark that visual state `BLOCKED — missing approved reference`;
- continue implementing test infrastructure and running non-visual phases when useful;
- do not mark the full QA loop or release candidate PASS;
- do not guess what the intended design should look like.

### 5.6 Stop conditions

Stop the affected phase and report a blocker when:

- the app does not build or launch;
- the required fixture media is absent or unlicensed;
- a design state has no approved reference;
- an interaction is not specified and multiple behaviors are plausible;
- the UI cannot expose a stable selector or automation pattern without a product code change;
- the desktop is locked or unavailable;
- FFmpeg/FFprobe required by the test is unavailable;
- a remote semantic service is requested without explicit approval or with non-synthetic/private screenshots.

Do not invent behavior to make a test pass.

---

## 6. Phase 1 — UI Automation and Diff Tooling

### 6.1 Create a separate Windows UI test project

Create:

```text
qa testing/ui-automation/Rushframe.UiAutomation.Tests.csproj
```

Use:

- target framework `net10.0-windows`;
- x64 execution;
- `Microsoft.NET.Test.Sdk`;
- xUnit and the repository’s current xUnit runner pattern;
- `FlaUI.Core`;
- `FlaUI.UIA3`;
- `SixLabors.ImageSharp` for local PNG loading, normalization, diffing, overlays, and heatmaps.

Do not add FlaUI or ImageSharp to `Rushframe.Desktop` or another shipped production assembly.

Prefer ImageSharp over a separately installed `odiff` binary so the harness remains self-contained and CPU-only. If an external diff binary is later used, pin and checksum it under `.tools`; never download it during a release test.

### 6.2 Test-process requirements

The UI test runner must:

- run in an interactive desktop session;
- use a single UI test collection unless isolation has been proven safe;
- disable test parallelization for tests sharing the desktop, app-data profile, native dialogs, or Rushframe process;
- kill only the Rushframe process instance it launched;
- capture logs and screenshots on failure;
- close modal dialogs before shutdown;
- clean temporary test output but never clean user project folders or repository evidence;
- preserve cancellation and fail on timeout with the current UI tree attached.

Retries may be used only for known environmental startup races and must not turn a product assertion failure into a pass. Every retry must be reported.

### 6.3 Stable AutomationId contract

Add explicit `AutomationProperties.AutomationId` values to every control used by automation. Do not rely only on visible text, localized names, control order, or raw screen coordinates.

Naming convention:

```text
Rushframe.<Area>.<Control>
```

Examples:

```text
Rushframe.Shell.Export
Rushframe.Media.Import
Rushframe.Media.List
Rushframe.Preview.Play
Rushframe.Preview.Seek
Rushframe.Timeline.Surface
Rushframe.Inspector.Apply
Rushframe.Export.Format
Rushframe.Export.Start
Rushframe.Animation.Graph
Rushframe.Workflow.Approve
```

Requirements:

- IDs are stable API-like contracts; do not rename them during visual cleanup without updating automation and contract tests.
- every icon-only control must also expose an accessible name and tooltip.
- hidden controls must not be treated as interactable.
- dialogs must have unique root IDs and stable IDs for accept, cancel, and primary fields.
- native file dialogs may be located through the desktop UIA root, but selection must be based on control type/AutomationId rather than fixed coordinates.

### 6.4 Custom control automation peers

Rushframe uses custom-drawn controls, especially the timeline and animation graph. Plain `x:Name` values are insufficient for semantic interaction.

Implement automation peers or another reviewed accessibility surface for:

- `TimelineControl`;
- timeline tracks and timeline items;
- trim handles;
- playhead;
- markers and transitions where interaction is exposed;
- `AnimationGraphControl` and keyframe points;
- preview transform handles when they are part of the test contract.

The automation surface should expose stable IDs and useful read-only properties such as:

- track ID and kind;
- item ID, media type, start, duration, selected state, locked state, and track ownership;
- playhead time;
- keyframe channel, time, value, and interpolation;
- whether an item supports invoke, selection, move, resize, or value operations.

Do not expose arbitrary domain mutation or raw FFmpeg execution through UI Automation. Automation must perform the same user-facing operations as a real user.

### 6.5 Keep transitional scripts

The existing PowerShell UIA and Win32 capture scripts remain useful for manual probing and defect reproduction. They are not the long-term source of truth for test assertions once the FlaUI project exists.

Do not delete them while the new harness is being introduced.

---

## 7. Phase 2 — Deterministic Test Environment and Fixtures

### 7.1 Isolate application data

UI automation must not read or overwrite the developer’s normal Rushframe settings, autosaves, recent projects, caches, audit logs, or exports.

Use an opt-in QA profile, for example:

```text
RUSHFRAME_QA_PROFILE_ROOT=<temporary absolute path>
```

All QA-profile paths must remain local and path-contained. The production default path behavior must not change when the variable is absent.

A QA profile may redirect:

- settings;
- recent-project history;
- autosaves;
- caches;
- audit logs;
- temporary exact-preview chunks;
- default test exports.

It must not bypass project serialization, save coordination, migration, recovery logic, authorization, revision checks, or export verification.

### 7.2 Deterministic software-composition mode

For CPU-only screenshot repeatability, support an opt-in launch mode such as:

```text
RUSHFRAME_QA_SOFTWARE_RENDER=1
```

This may set WPF process rendering to software-only before windows are created. It must not replace Rushframe’s preview scheduler, exact-preview fallback, or FFmpeg renderer, and it must not be enabled in normal production launches.

Record whether the run used software-only WPF composition.

### 7.3 Environment normalization

Visual runs must pin:

- Windows display scale, normally 100%;
- client capture dimensions;
- theme;
- culture and number formatting;
- system font availability;
- window state and position;
- preview zoom;
- timeline zoom and scroll position;
- panel visibility and widths;
- fixture project revision and content;
- current playhead time;
- deterministic thumbnail/proxy readiness;
- no unexpected native notification, tooltip, menu, or modal overlay.

Do not compare references created under a different DPI, theme, font set, capture mode, or window size unless the state manifest explicitly permits it.

### 7.4 Fixture rules

Create compact deterministic fixtures that exercise Rushframe without using personal or private media.

Include, as needed:

- a short video with known dimensions, frame rate, color bars, motion, and embedded tone;
- an audio-only WAV or AAC file with known duration and peaks;
- a music fixture with clear beats and fade test points;
- a still PNG with transparency;
- a logo/sticker image;
- a subtitle/transcript fixture;
- a saved project with three clips and multiple tracks;
- a project with text, effects, masks, transitions, animation, and audio;
- an old-schema project for migration;
- an offline-media project;
- a stale-revision agent request fixture;
- safe and unsafe asset-pack manifests;
- safe and unsafe extension manifests;
- a project near the requested performance workload size.

Every fixture must document ownership/license, expected duration, expected hashes where appropriate, and whether it may be sent to an approved remote semantic service. Default is local-only.

### 7.5 State navigation must use the UI

The capture and functional harness must reach product states through normal user paths:

- launch the editor;
- use menus, buttons, keyboard shortcuts, timeline interactions, inspector controls, and native dialogs;
- import/open local files through the same UI flows available to users;
- save/reopen/recover through the editor.

Do not construct or mutate `Project`, `Sequence`, `Track`, or `TimelineItem` objects inside the UI test process to fake a reached state.

It is acceptable to prepare a fixture project before the run and open it through the UI. It is also acceptable to inspect a saved project out of process after the UI has committed it, provided the test never uses that inspection path to mutate the running editor.

---

## 8. Phase 3 — Capture Harness

### 8.1 Launch and ownership

The harness must:

1. build or locate the intended Rushframe Release executable;
2. create a unique QA profile and output directory;
3. launch the executable with the approved QA environment;
4. attach through FlaUI UIA3;
5. identify the main Rushframe window by process ID and root AutomationId;
6. wait for the editor-ready condition;
7. record process ID, executable path, build configuration, window metrics, and environment metadata.

Never attach to an arbitrary pre-existing Rushframe process.

### 8.2 Wait for state, not time

Use bounded waits for observable conditions:

- element exists and is enabled;
- modal dialog is open/closed;
- project name or revision text changed;
- media list count changed;
- operation progress became visible/hidden;
- export completion prompt appeared;
- autosave status reached the expected state;
- preview frame or playhead reached the requested time;
- saved file exists and has stabilized.

A small post-condition settle interval may be used after the state is proven, but fixed sleeps alone are not sufficient.

### 8.3 Real window capture

Capture the composited Rushframe window through Win32, reusing or extracting the proven `PrintWindow` approach from `Capture-RushframeWindow.ps1`.

Requirements:

- use a consistent full-window or client-only mode defined by the reference manifest;
- use `PW_RENDERFULLCONTENT` where supported;
- verify width and height before saving;
- reject an all-black, all-transparent, zero-byte, or wrong-sized capture;
- include modal dialogs when the state requires them;
- do not use `RenderTargetBitmap` as the sole authoritative screenshot;
- do not use screen capture that accidentally includes unrelated windows.

`RenderTargetBitmap` may be used only as a diagnostic comparison, not as the release visual source.

### 8.4 State definition

Represent states as typed test definitions, not loose strings scattered across tests.

Each state must specify:

- stable state ID;
- fixture project/media;
- preconditions;
- UI navigation steps;
- readiness assertion;
- capture mode and dimensions;
- reference image;
- optional approved volatile masks;
- functional assertions related to reaching the state;
- cleanup behavior.

### 8.5 Initial Rushframe state inventory

The final inventory must be derived from approved references and current code. Start with these candidate states and remove or add states only through review:

| State ID | Intended UI state |
|---|---|
| `shell.untitled` | Fresh startup, empty untitled project |
| `shell.project-open` | Showcase/fixture project loaded |
| `media.library-all` | Media panel with mixed local assets |
| `media.source-preview` | Selected source loaded in preview |
| `media.marked-range` | Source Mark In/Out visible |
| `timeline.three-clips` | Three clips across representative tracks |
| `timeline.multi-selection` | Multiple selected items and group handles |
| `timeline.locked-track` | Locked track and selected item state |
| `timeline.transition-selected` | Transition selected with Inspector controls |
| `preview.timeline-frame` | Timeline preview at a fixed timestamp |
| `preview.transform-handles` | Selected visual item with direct-manipulation handles |
| `preview.safe-area-guides` | Portrait sequence with safe-area overlays |
| `inspector.video` | Video-specific Inspector profile |
| `inspector.audio` | Audio/music Inspector profile |
| `inspector.text` | Text and typography Inspector profile |
| `inspector.effects` | Effect stack and parameters |
| `animation.editor` | Animation editor with channels and keyframes |
| `animation.bezier-edit` | Bezier handles after an intentional edit |
| `canvas.settings` | Canvas settings dialog |
| `export.settings-default` | Export dialog with expected dimensions/audio |
| `export.in-progress` | Export progress and cancel state |
| `workflow.production` | Production workflow tab |
| `workflow.transcript` | Transcript editor state |
| `workflow.variants` | Output variants tab |
| `workflow.render-queue` | Render queue state |
| `workflow.receipts` | Render receipts state |
| `workflow.compositions` | External compositions state |
| `assets.browser` | Creative assets dialog |
| `agent.plan-preview` | Approved agent edit-plan preview dialog |
| `error.offline-media` | Offline/relink error state |
| `recovery.available` | Explicit autosave-recovery workflow |

A state not present in an approved mockup may still have a functional test, but it must not be assigned an invented visual reference.

### 8.6 Output paths

Write captures to:

```text
qa testing/results/ui/current/<run-id>/<state-id>.png
```

Write state metadata next to each capture:

```text
<state-id>.json
```

Never overwrite a previous failed run without preserving the run ID.

---

## 9. Phase 4 — Visual Regression Loop

### 9.1 Comparison algorithm

For each state:

1. load the approved reference and current capture;
2. fail immediately on dimension mismatch;
3. convert to a consistent pixel format;
4. compare pixels using a documented per-channel tolerance to avoid counting harmless encoder noise as a full mismatch;
5. apply only pre-approved masks from the reference manifest;
6. calculate mismatched pixels as a percentage of unmasked pixels;
7. produce a heatmap, alpha overlay, and summary JSON;
8. apply the state’s pass threshold.

Default release threshold:

```text
Mismatch <= 2.00%
```

The 2% threshold is per state. Do not average multiple states together.

For high-risk regions such as export dimensions, selected state, lock indicators, dialog actions, progress/cancel controls, safe-area guides, or error messages, define region assertions that may require exact presence even if the whole image remains under 2%.

### 9.2 Anti-aliasing and tolerance policy

Document the exact tolerance in code and reports. A recommended starting rule is that a pixel differs only when at least one channel exceeds a small fixed delta after normalization.

Do not increase tolerance merely to make a failing build pass. A tolerance change requires:

- a rationale;
- before/after evidence;
- review of false positives and false negatives;
- approval in the reference manifest or QA code review.

### 9.3 Volatile regions

Volatile masks are allowed only for unavoidable, non-product data such as a generated temporary path or timestamp that cannot be made deterministic.

Do not mask:

- missing controls;
- changed labels;
- wrong dimensions;
- incorrect selected/locked states;
- status/progress indicators;
- preview content;
- timeline item positions;
- error messages;
- layout overflows;
- visual defects under active investigation.

### 9.4 Artifacts

For every state, write:

```text
qa testing/results/ui/diff/<run-id>/<state-id>_diff.png
qa testing/results/ui/diff/<run-id>/<state-id>_overlay.png
qa testing/results/ui/reports/<run-id>/<state-id>.json
```

The JSON must include:

- state ID;
- reference and current paths;
- image dimensions;
- compared and masked pixel counts;
- mismatch count and percentage;
- threshold;
- pass/fail/block reason;
- environment metadata;
- reference manifest version.

### 9.5 Failure loop

For each failing state:

1. inspect the heatmap and overlay;
2. classify the difference: environment, fixture, capture bug, intended design change, or product defect;
3. record a defect before fixing a confirmed product defect;
4. fix the smallest root cause;
5. run focused tests;
6. rebuild Release;
7. recapture only the affected state first;
8. rerun the full visual state set before final PASS.

Do not update the approved reference to hide a regression. A reference update requires an explicit reviewed design change and a reason in version control.

### 9.6 Visual gate result

Each state result is exactly one of:

```text
PASS
FAIL
BLOCKED
NOT APPLICABLE
```

Every mandatory state must independently PASS. `BLOCKED` is not equivalent to PASS.

---

## 10. Phase 5 — Functional UI Automation

Run functional UI automation as a separate test command and report from visual comparison. Functional tests must drive the actual UI and assert persisted or observable behavior.

### 10.1 Assertion strategy

Use one or more of the following, in order of preference:

1. UI Automation state exposed by the product;
2. visible editor status, selection, timing, and control values;
3. saved project inspection after the UI commits a save;
4. exported media inspection with FFprobe/FFmpeg;
5. audit log or render receipt produced by the real workflow.

Do not call private event handlers, domain commands, project mutation methods, export services, or serializer methods inside the UI test to simulate a user action.

### 10.2 Startup and project lifecycle

Automate and assert:

- fresh startup opens an untitled project;
- existing autosaves do not silently replace the startup project;
- New Project handles dirty-state prompts correctly;
- Open Project loads the selected `.rushframe` file;
- Open Recent lists valid entries and handles missing entries predictably;
- Save and Save As create a valid project file;
- dirty-state indicator changes after a committed mutation;
- close prompts appear only when appropriate;
- canceling close preserves the running project;
- explicit autosave recovery loads the selected recovery state;
- save, close, reopen preserves project revision and all supported content;
- old-schema projects migrate deterministically and preserve a backup when required;
- projects newer than the supported schema are rejected safely.

### 10.3 Local media import and relink

Cover:

- video import;
- audio import;
- music import;
- image/logo/sticker import;
- supported subtitle/transcript import where exposed;
- duplicate import behavior;
- metadata, duration, dimensions, media type, and audio-stream recognition;
- thumbnail/waveform/proxy readiness;
- offline media indication;
- relink to an approved local file;
- rejection of unsafe, missing, network, or path-escape inputs where applicable;
- original source file hash remains unchanged after editing, preview, and export.

No test may use a web URL as a media source.

### 10.4 Source preview and range editing

Cover:

- select media and open source preview;
- seek by pointer, keyboard, and UI Automation;
- play, pause, stop, mute, volume, loop, speed, previous frame, and next frame;
- set and clear Mark In/Mark Out;
- insert and overwrite marked ranges;
- inserted duration and source start match the marked range;
- source audio is retained or omitted according to the UI choice;
- invalid or reversed ranges are rejected without mutation.

### 10.5 Timeline editing

Cover at minimum:

- add clip/text/music through user-facing controls;
- move within a track;
- move across compatible tracks;
- reject move to incompatible or locked destination tracks;
- trim start and end;
- reject zero/negative duration and out-of-source trim;
- split;
- delete;
- ripple delete;
- duplicate;
- copy and paste;
- multi-select and box select;
- group move and resize;
- snap on/off;
- ripple on/off;
- track add, rename, reorder, duplicate, delete, mute, solo, hide, and lock;
- markers;
- transitions;
- zoom and horizontal/vertical navigation;
- selection and playhead remain coherent after edits.

For every mutating workflow, assert:

- the successful logical mutation increments project revision exactly once;
- rejected operations do not increment revision;
- rejected operations do not enter undo history;
- no partial state changes occur;
- undo restores exact prior timing, track, ordering, properties, effects, masks, animation, transitions, and references;
- redo reapplies the same result.

### 10.6 Locked-state matrix

Locked tracks and items are strict integrity boundaries. Exercise every reachable mutation path against locked content, including:

- add/insert/overwrite to locked destination;
- move;
- trim;
- split;
- delete and ripple delete;
- duplicate and paste;
- direct preview transform;
- Inspector properties;
- effects;
- masks;
- text;
- keyframes and Bezier edits;
- transitions;
- captions and transcript-generated clips;
- agent plans.

Every rejection must leave state, revision, dirty flag, and undo history unchanged.

### 10.7 Inspector and properties

Cover media-aware Inspector profiles:

- video;
- image/logo/sticker;
- audio/music/voice;
- text;
- adjustment items where supported.

Assert that unsupported sections are hidden or disabled and that Apply reads/writes only supported fields.

Exercise:

- position, scale, rotation, opacity, crop, pan, speed, reverse;
- volume, mute, fades, and pan;
- text content, font, size, alignment, fill, outline, and shadow;
- color controls;
- effect stack add/remove/toggle/reorder/duplicate/reset/parameters;
- stabilization;
- transition kind, duration, and alignment;
- validation errors and reset behavior.

### 10.8 Direct preview manipulation

Cover:

- selected item handles appear for supported visual items;
- unsupported items do not expose invalid handles;
- drag, resize, and rotate use the normal command/undo path;
- snapping guides appear when expected;
- the resulting saved values match the UI operation;
- undo/redo restores exact values;
- locked items reject the operation.

### 10.9 Animation and keyframes

Cover:

- open animation editor;
- add/remove channels;
- add/remove/move keyframes;
- edit values and interpolation;
- edit Bezier handles;
- copy/paste when supported;
- save/reopen preservation;
- undo/redo preservation;
- lock rejection;
- exact preview/export parity at start, keyframe, middle, and end boundaries.

### 10.10 Preview behavior

Cover both preview paths:

- responsive realtime WPF preview for supported states;
- exact FFmpeg chunk fallback for unsupported effects or masks;
- playhead is canonical during seek and frame-step;
- pointer, keyboard, and UI Automation seek reach the same canonical path;
- rapid same-chunk and cross-chunk seeks show the final requested frame;
- stale exact chunks are never presented as current;
- play/pause/stop/frame-step/loop/fullscreen remain responsive;
- no competing preview timer or duplicate playback behavior appears;
- preview mutation invalidates exact-preview cache correctly.

### 10.11 Export dialog and export lifecycle

Drive the real export dialog and assert:

- format, codec/profile, resolution, frame rate, quality, audio, and output path controls show the intended values;
- portrait export displays 1080 × 1920 when the project requires it;
- unsafe, invalid, or disallowed output paths are rejected;
- overwrite behavior follows the product prompt;
- Export starts one render operation;
- progress and cancel controls become visible and the UI remains responsive;
- cancellation terminates the FFmpeg process tree and does not report success;
- partial output is not treated as completed output;
- completion prompt is handled and operation controls reset;
- a second export can run in the same application process;
- output is non-zero, probeable, fully decodable, and matches requested streams/dimensions/duration;
- render job, workflow, variant, and receipt state remain synchronized when those workflows are used.

### 10.12 Preview/export parity

For representative timestamps, capture the preview and decode the corresponding exported frame.

Required samples include:

- opening frame;
- clip boundary;
- transition boundary;
- animation/keyframe boundary;
- middle frame;
- final visible frame.

Compare:

- layer order;
- crop and transform;
- opacity and blend behavior;
- text;
- effects and color;
- masks/chroma key;
- transition state;
- safe-area-independent composition;
- audio sync around the same event.

A realtime-preview approximation may differ only where the product explicitly indicates exact fallback. Exact preview and export must materially agree.

### 10.13 Undo/redo stress

Run a deterministic mixed sequence of edits, then undo all and redo all.

Assert:

- no exception;
- one undo entry per logical action;
- redo clears after a new successful edit;
- failed edits never enter history;
- final project state after redo matches the saved expected state;
- revision increments only for committed new mutations, not for undo-history inspection or rejected actions.

### 10.14 Automation, tasks, campaign, and agent bridge

Cover user-facing campaign/task behavior and controlled agent behavior separately.

Campaign/tasks:

- campaign description saves and reopens;
- tasks are visible to the user and agent context;
- task status changes persist through the intended command/coordinator path.

Agent bridge/security:

- loopback-only binding;
- health route behavior;
- session token required for protected routes;
- fixed request-size limit;
- unauthorized request causes no mutation;
- stale `base_revision` is rejected;
- edit-plan preview is non-mutating;
- approval is required where configured;
- approved multi-operation plan applies atomically as one undo entry and one revision;
- a failing operation rolls back the whole plan;
- only registered, online local project media may be referenced;
- raw FFmpeg or arbitrary shell execution is unavailable;
- output paths are local, path-contained, and not UNC/network paths;
- success, rejection, conflict, and failure are audited;
- manual edits made after plan creation prevent stale apply.

These checks may combine existing deterministic desktop/domain tests with a small number of real UI/bridge integration tests. Keep their report separate from ordinary timeline UI tests.

### 10.15 Asset packs, extensions, and external compositions

Cover:

- valid local asset pack discovery;
- path traversal rejection;
- missing file rejection;
- required attribution enforcement;
- network-enabled pack rejection;
- valid extension metadata discovery;
- high-risk extension permission disablement;
- remote entry-point rejection;
- no extension code execution;
- local Remotion/HyperFrames/custom composition path validation when explicitly enabled;
- external output is verified and imported as a new local asset without replacing originals;
- directory escape and network path rejection.

### 10.16 Functional test command and artifacts

Run the UI project explicitly, for example:

```powershell
dotnet test ".\qa testing\ui-automation\Rushframe.UiAutomation.Tests.csproj" -c Release --filter "Category=Functional"
```

Write TRX, logs, UI trees, project snapshots, and failure screenshots under the current run directory.

---

## 11. Phase 6 — Renderer and Media Verification

Functional UI success is not enough for a video editor. Verify the media itself.

### 11.1 Probe and decode

For every mandatory export:

- file exists and is non-zero;
- expected container opens;
- video stream exists;
- audio stream exists when enabled;
- dimensions match;
- frame rate matches expected rational rate;
- duration is within tolerance;
- codec and pixel format are acceptable;
- a complete decode succeeds;
- output hash is recorded;
- runtime versions are recorded.

### 11.2 Quality checks

Use the existing render verification/receipt path where applicable. Record:

- black intervals;
- freeze intervals;
- silence intervals;
- loudness and peak measurements;
- evidence frames around boundaries;
- source and output hashes;
- duration, dimensions, codecs, and streams;
- render warnings and capability validation results.

Do not silently omit an enabled unsupported effect or mask. Capability validation must reject a render it cannot reproduce.

### 11.3 Direct harness role

`qa testing/harness` may remain as:

- a direct canonical renderer smoke test;
- a diagnostic comparison when the UI path fails;
- a fallback for generating controlled media evidence.

It cannot replace real export-dialog testing because it bypasses WPF orchestration, progress, cancellation, completion prompts, dirty state, and user-selected settings.

---

## 12. Phase 7 — Persistence, Autosave, Migration, and Recovery

Run these as a dedicated phase because visual and editing checks may pass while persistence is corrupt.

Cover:

- atomic temporary-file-then-move save behavior;
- no full project file I/O on the WPF UI thread;
- overlapping mutation/save coordination;
- revision-coalesced autosave;
- autosave retention;
- explicit recovery;
- startup does not silently recover;
- save while an operation is pending does not capture partial mutation;
- close/reopen restores all supported project state;
- old schema migration is deterministic;
- newer schema rejection;
- invalid/truncated project handling;
- temporary-file cleanup after failure;
- recent-project behavior;
- original source paths and files remain unchanged.

For a save/reopen parity test, compare at least:

- project ID and revision semantics;
- sequence canvas and frame rate;
- tracks and ordering;
- item IDs, timing, source timing, transforms, crop, audio, text, effects, masks, animation, chroma key, speed, transitions, and references;
- campaign description and tasks;
- workflow/provider/variant/composition/plan/job/receipt state where present;
- imported media-intelligence references.

---

## 13. Phase 8 — Optional Semantic/Layout Sanity Check

A hosted vision model is not a substitute for pixel diff, UI Automation, renderer verification, or human review. It is non-deterministic, remote, and may create privacy or availability risk.

Therefore the semantic check is **opt-in**, not a default core release dependency.

It may run only when all of the following are true:

- the user or release owner explicitly enables remote semantic QA;
- screenshots contain only synthetic or explicitly approved non-private fixtures;
- no source paths, tokens, project secrets, private media, or personal information are visible;
- the endpoint uses HTTPS;
- the provider/model is verified as currently available at run time;
- estimated cost is shown before execution, even when the selected tier is advertised as free;
- a strict request and budget limit is configured;
- actual usage/cost is recorded afterward;
- failures or provider unavailability do not erase deterministic test results.

A user-provided candidate model such as an OpenRouter free vision-capable model may be used only after those checks. Do not hardcode one third-party model as permanently available.

Prompt the service to compare only coarse semantics:

- same major elements;
- same order and hierarchy;
- no missing or swapped controls;
- same selected/locked/progress/dialog state;
- no obvious layout overflow.

Store only the provider name, model name, request ID, redacted prompt, structured response, and pass/fail rationale. Never store API keys.

When remote semantic QA is disabled, perform a local deterministic semantic check instead:

- compare the expected AutomationId set;
- compare control types, names, enabled/visible states, and hierarchy;
- verify key region occupancy and ordering;
- complete human screenshot review.

Remote semantic PASS can add confidence but cannot override a deterministic FAIL.

---

## 14. Phase 9 — UX, Accessibility, and Heuristic Review

Run this only after the deterministic functional and visual phases are understood. Record findings; do not silently redesign ambiguous behavior.

### 14.1 Feedback

Check that:

- click, drag, drop, trim, split, seek, apply, save, analyze, and export actions produce immediate visible feedback;
- selected, playing, paused, loading, analyzing, saving, autosaving, rendering, canceled, completed, and failed states are distinguishable;
- actions taking more than roughly 200 ms provide progress or a busy indication without freezing the editor;
- completion prompts and errors are not hidden behind the main window.

### 14.2 Consistency

Check that:

- similar delete, duplicate, lock, reset, apply, and cancel actions use consistent interaction and visual patterns;
- icon-only actions expose consistent tooltips and accessible names;
- similar Inspector fields validate and report errors consistently;
- keyboard shortcuts and command-search actions invoke the same behavior as visible controls.

### 14.3 Error prevention and recovery

Check that:

- destructive actions are undoable or confirmed as appropriate;
- overwrite export is explicit;
- locked and protected content cannot be changed accidentally;
- stale agent changes cannot overwrite manual work;
- invalid timing, path, media, and effect inputs fail without partial mutation;
- cancellation leaves the app usable;
- recovery is explicit and understandable.

### 14.4 State visibility

Check that it is always clear:

- which item, track, transition, effect, keyframe, media source, workflow stage, variant, or render job is selected;
- whether the preview is source or timeline preview;
- current playhead time and duration;
- whether exact fallback is being prepared or shown;
- whether the project is dirty/saving/saved;
- whether a track or item is locked, muted, soloed, hidden, or offline.

### 14.5 Control grouping and hierarchy

Check that:

- transport controls are grouped;
- source marks and insert/overwrite controls are grouped;
- timeline edit toggles are grouped;
- related Inspector properties are grouped and media-aware;
- export format, dimensions, quality, audio, output, progress, and cancel actions form a clear sequence;
- workflow, tasks, variants, jobs, receipts, and compositions do not obscure core manual editing.

### 14.6 Accessibility

Check:

- keyboard reachability and logical tab order;
- visible focus treatment consistent with the theme;
- accessible names for icon-only controls;
- AutomationId stability;
- controls expose correct UIA patterns;
- no critical action depends only on color;
- disabled/hidden state is represented correctly;
- custom timeline/graph controls expose meaningful automation peers;
- high-contrast and text scaling behavior is reviewed where supported.

### 14.7 Ambiguity handling

When the reference shows appearance but not behavior:

- record the ambiguity in the report;
- describe the competing reasonable behaviors;
- do not implement a personal preference as if it were specified;
- request a product/design decision before marking the affected UX test PASS.

---

## 15. Phase 10 — CPU-Only Performance and Responsiveness

Use the existing tools under `qa testing/performance` and `benchmarks`.

At minimum run, when relevant:

```powershell
powershell -ExecutionPolicy Bypass -File ".\qa testing\performance\Smoke-Startup.ps1"
powershell -ExecutionPolicy Bypass -File ".\qa testing\performance\Run-PerformanceBaseline.ps1"
dotnet run --project .\benchmarks\Rushframe.Benchmarks\Rushframe.Benchmarks.csproj -c Release -- --filter "*"
```

Measure representative workloads for:

- cold and warm startup;
- project open;
- media import and derivative generation;
- timeline layout, selection, hit testing, drag, trim, zoom, and scroll;
- preview frame scheduling and dropped/late frames;
- rapid seek and exact-chunk preparation;
- save snapshot and write duration;
- autosave coalescing;
- memory growth during sustained editing/playback;
- export progress, cancellation latency, and process cleanup;
- large project workloads generated by existing performance tooling.

Rules:

- compare before and after using the same build configuration, fixture, environment, and command;
- do not claim improvement without comparable measurements;
- do not hide UI-thread stalls behind a longer timeout;
- capture trace or memory evidence for unexplained regressions;
- report CPU model, RAM, Windows version, power mode, software-render mode, and FFmpeg version.

Human review must still assess scrub responsiveness, drag smoothness, preview usability, and cancellation feel on the CPU-only machine.

---

## 16. Defect Workflow

Use the active file:

```text
qa testing/manual review/showcase-edit/defect_log.md
```

Before fixing a newly confirmed product defect:

1. reproduce it through the original path when practical;
2. assign the next unused `QA-NEW-###` ID from the existing log;
3. record severity, status, build, project revision, preconditions, exact steps, expected, actual, impact, evidence, and suspected component;
4. fix the smallest root cause;
5. add a deterministic regression test;
6. run focused tests;
7. run the broader affected suite;
8. retest the original UI/export/persistence path;
9. update the same defect record with the exact result and evidence.

Do not create a new defect for:

- a missing approved design reference;
- unavailable optional remote semantic provider;
- a hidden modal prompt that is working as designed;
- a test harness selector bug;
- an unsupported local dependency that the plan already marks as a prerequisite.

Record those as blockers or harness defects separately unless they reveal a product problem.

---

## 17. Reporting

Each full run must produce one summary under:

```text
qa testing/results/ui/reports/<run-id>/summary.md
```

Use this structure:

```markdown
# Rushframe QA Run

## Environment
- Commit / working-tree state:
- Build configuration:
- .NET SDK:
- Windows:
- Display scale:
- Window client size:
- WPF render mode:
- FFmpeg / FFprobe:
- Fixture version:
- Design-reference manifest:

## Repository gates
| Command | Result | Tests / warnings | Evidence |
|---|---|---:|---|

## Visual regression
| State | Mismatch % | Threshold | Result | Evidence |
|---|---:|---:|---|---|

## Functional UI automation
| Workflow | Result | Evidence | Notes |
|---|---|---|---|

## Renderer and media verification
| Export | Probe | Decode | Audio | Frames | Result |
|---|---|---|---|---|---|

## Persistence and recovery
| Test | Result | Evidence |
|---|---|---|

## Agent and security guardrails
| Test | Result | Evidence |
|---|---|---|

## Performance
| Metric | Baseline | Current | Delta | Result |
|---|---:|---:|---:|---|

## Semantic check
- Disabled / local deterministic / approved remote:
- Provider/model when applicable:
- Result:

## UX and accessibility findings
- Finding:
- Severity:
- Evidence:
- Product/design decision needed:

## Defects
- Open:
- Fixed and retested:
- Harness defects:

## Blockers

## Manual validation

## Final decision
PASS / FAIL / BLOCKED
```

Never report a build, test, screenshot, preview match, export, audio review, benchmark, or UI action that was not actually performed.

---

## 18. Release Gate

The detailed QA result is `PASS` only when:

1. required Debug and Release repository gates pass;
2. every mandatory approved visual state passes independently;
3. every mandatory functional UI workflow passes;
4. locked-state, undo/redo, revision, and no-partial-mutation behavior pass;
5. save/reopen, migration, autosave, and explicit recovery pass;
6. preview/export parity is verified at representative boundaries;
7. real export-dialog output probes and fully decodes;
8. cancellation and second-export behavior pass;
9. local-agent authorization, revision, approval, atomicity, path, and audit guardrails pass;
10. source media remains unchanged;
11. no Blocker or Critical defect remains open;
12. performance has no unexplained release-blocking regression;
13. required manual feel, audio, and UX review is complete;
14. the showcase acceptance plan also passes.

Use exactly one final result:

```text
PASS — all mandatory deterministic and manual gates completed
FAIL — a mandatory gate failed or a release-blocking defect remains
BLOCKED — required reference, media, environment, dependency, or product decision is unavailable
```

Current visual status remains `BLOCKED` until `qa testing/design-reference/manifest.json` and approved state images are supplied.

---

## 19. Hard Constraints

- No GPU-dependent QA library, CUDA package, local vision model, or hidden GPU requirement.
- No modification of approved design references by normal test execution.
- No default upload of screenshots or media to a remote service.
- No remote semantic check without explicit enablement, approved synthetic content, budget, and recorded provider usage.
- No direct project mutation from the UI test harness.
- No direct renderer call as a substitute for real export-dialog testing.
- No parallel editor instances sharing the same QA profile.
- No fixed-coordinate selector when a stable AutomationId or automation peer can be provided.
- No screenshot-only assertion for functional correctness.
- No functional assertion inferred only from pixel similarity.
- No averaging failing visual states into a passing score.
- No retry policy that hides a deterministic product failure.
- No source-file overwrite or mutation.
- No social URL import, downloader, scraper, or arbitrary network media acquisition.
- No raw FFmpeg/shell capability exposed to agents.
- No release PASS while a mandatory state, reference, interaction specification, export, audio review, or real-editor workflow is blocked.

---

## 20. Implementation Order

Implement this plan in the following order:

1. add the approved reference/fixture directory structure and manifests;
2. create the separate FlaUI/ImageSharp test project;
3. add stable AutomationIds and accessibility names;
4. add automation peers for custom timeline/animation/preview interactions;
5. add isolated QA profile and optional software-render launch configuration;
6. implement state navigation and real-window capture;
7. implement local visual diff and reports;
8. automate startup, import, core timeline, locked-state, undo/redo, save/reopen, preview, and export workflows;
9. add renderer, parity, persistence, agent/security, asset, and composition coverage;
10. integrate performance and UX evidence;
11. run focused tests, then all required repository and release gates;
12. complete the showcase edit and final human review.

Do not postpone AutomationId, isolation, or deterministic fixture work until after writing dozens of brittle tests. Stable selectors and repeatable state are prerequisites, not cleanup tasks.
