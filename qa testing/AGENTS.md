# Rushframe QA Agent Instructions

These instructions apply to all work under `qa testing/`.

Rushframe is a local-first Windows video editor. QA must verify the real WPF editor, its canonical project state, preview/export behavior, persistence, controlled agent workflows, and final edited output. Do not reduce QA to unit tests, screenshots, or direct FFmpeg calls alone.

## 1. Start every QA task here

Before inspecting, implementing, fixing, or executing QA:

1. Run `git status --short`.
2. Read the repository-root `AGENTS.md`.
3. Read `AGENT_CONTEXT.md`.
4. Read `LOCAL_AGENT_INSTRUCTIONS.md`.
5. Read `qa testing/DETAILED_QA_TESTING_PLAN.md`.
6. Read `qa testing/QA_TESTING_PLAN.md`.
7. Read `qa testing/manual review/showcase-edit/defect_log.md`.
8. Inspect the current source, tests, scripts, fixtures, and evidence related to the workflow being tested.

The current source code and tests are the source of truth. Documentation describes required behavior but may lag behind implementation.

The working tree is intentionally dirty. Preserve every unrelated modified, deleted, and untracked file. Never use `git reset --hard`, `git clean`, destructive checkout, mass formatting, or broad rewrites.

## 2. QA mission

A complete Rushframe QA pass must independently evaluate:

1. build, unit, integration, architecture, and dependency gates;
2. visual regression against approved references;
3. functional WPF UI automation;
4. realtime preview, exact preview, and final export parity;
5. render probing, full decode, audio, black/freeze, and evidence-frame checks;
6. save, close, reopen, migration, autosave, and explicit recovery;
7. undo/redo, locked-state, revision, and no-partial-mutation behavior;
8. local-agent authentication, approval, atomicity, audit, path, budget, and stale-revision guardrails;
9. CPU-only performance and responsiveness;
10. UX, accessibility, audio listening, and human editing feel;
11. the polished showcase-edit acceptance plan.

Do not combine these into one score. Each phase requires its own result and evidence.

## 3. Product boundaries that QA must preserve

- Manual local import of video, audio, music, images, logos, fonts, subtitles, and supported assets is required.
- Do not add or test social-media downloading, scraping, URL importing, or arbitrary website acquisition.
- Agents may operate only on media already registered in the open project.
- Never overwrite or silently modify original source files.
- Timeline mutations must use the same undoable command paths as the editor.
- Manual edits win over stale agent requests.
- Startup must open an untitled project and must not silently restore the newest autosave.
- Exact preview and export must use the canonical FFmpeg timeline renderer.
- Extension manifests are metadata only; never execute arbitrary extension code.
- Asset packs must remain local, path-contained, offline, and license aware.
- Core editing and core QA must remain local and CPU-only.

## 4. Current prerequisites and blockers

At the time these instructions were created:

- Rushframe targets .NET 10, WPF, and Windows x64.
- `qa testing/harness` directly loads a project and invokes the renderer. It is useful for renderer smoke testing but does not test the WPF export workflow.
- existing PowerShell scripts provide transitional UI Automation and real `PrintWindow` capture.
- broad stable UI automation still requires explicit `AutomationProperties.AutomationId` values and custom automation peers where needed.
- approved visual references are expected under `qa testing/design-reference/`.

When approved references are missing or incomplete:

- mark only the affected visual states or visual phase `BLOCKED`;
- continue build, functional, renderer, persistence, security, performance, and manual QA when possible;
- never invent the design or modify generated screenshots to become the reference;
- do not call the missing reference a product defect.

## 5. Required implementation order for new QA automation

Implement QA infrastructure in this order:

1. establish deterministic local fixtures and the design-reference manifest;
2. create the separate headed Windows UI automation test project under `qa testing/ui-automation/`;
3. add stable AutomationIds and useful accessibility names to production WPF controls;
4. add automation peers for custom timeline, preview, and animation interactions that normal UI Automation cannot address reliably;
5. isolate QA app data, autosaves, recent-project state, caches, exports, and temporary files from the user profile;
6. add an explicit QA launch mode for deterministic software composition only when needed;
7. implement state navigation through the real UI;
8. implement state-based waits and real composited-window capture;
9. implement local visual diff and independent per-state reporting;
10. automate functional workflows separately;
11. add renderer, persistence, agent/security, performance, accessibility, and UX evidence;
12. run the showcase edit and final human review.

Do not write dozens of coordinate-based tests before stable selectors, isolation, and fixtures exist.

## 6. UI automation rules

Use a separate Windows test project, preferably:

```text
qa testing/ui-automation/Rushframe.UiAutomation.Tests.csproj
```

Use FlaUI for headed WPF automation and SixLabors.ImageSharp for local image comparison unless the current repository establishes a better reviewed equivalent.

The UI test process must:

- target Windows and the current .NET version;
- run in STA where required;
- disable test parallelization;
- launch one isolated editor instance per test or controlled collection;
- own and terminate the process it starts;
- capture stdout, stderr, application logs, screenshots, and failure diagnostics;
- clean only its own generated profile and output directories;
- never share an app-data profile between parallel editor instances.

Selector priority:

1. `AutomationId`;
2. supported UI Automation pattern and stable accessibility name;
3. custom automation peer for custom-drawn controls;
4. deterministic keyboard shortcut;
5. coordinates only as a temporary documented fallback.

Do not use private reflection or direct model mutation to place the app into a test state. The test must exercise the same UI and command paths used by a person.

Use state-based waits, not arbitrary sleeps. Wait for observable conditions such as:

- window ready and responsive;
- project title or revision changed;
- selected item changed;
- operation state completed;
- modal appeared or closed;
- output file stabilized;
- render job reached a terminal state.

A timeout must report the unmet condition and capture evidence. Retries must not hide deterministic product failures.

## 7. Visual regression rules

Approved references live under:

```text
qa testing/design-reference/
```

Generated captures and reports live under:

```text
qa testing/results/ui/
```

Visual tests must:

- normalize client size, display scale, culture, theme, font availability, WPF render mode, and fixture data;
- drive the editor to a named state through the UI;
- capture the actual composited window or client area with `PrintWindow` or a verified equivalent;
- compare equal-sized images locally;
- produce actual, reference, diff heatmap, mismatch percentage, threshold, and pass/fail result;
- evaluate every mandatory state independently;
- fail a state when it exceeds its threshold;
- never average multiple failing states into a passing score.

Do not modify approved reference images during an ordinary test run. A reference update requires explicit human approval and a recorded reason.

Masks are allowed only for genuinely volatile external data such as timestamps or generated unique identifiers. Do not mask product UI, spacing, text, controls, or failures merely to reduce mismatch.

Visual similarity proves appearance only. It does not prove behavior.

## 8. Functional UI coverage

Functional tests must assert behavior separately from screenshots. Cover at minimum:

### Startup and project lifecycle

- fresh startup opens an untitled project;
- autosave is not silently restored;
- new, open, save, save as, close, recent project, and explicit recovery;
- unsaved-change prompts and clean process exit.

### Manual local import

- video, audio, music, image, logo, subtitle, and supported local assets;
- metadata, thumbnail, waveform, duration, dimensions, media kind, duplicate behavior, offline state, and relink;
- source files remain byte-identical before and after testing.

### Timeline editing

- insert, overwrite, move, trim, split, delete, ripple delete, duplicate, copy/paste;
- multi-selection and group operations;
- snapping, zoom, scroll, markers, transitions, effects, masks, transforms, crop, opacity, text, speed, audio, track operations, and sequence settings;
- successful logical edits increment revision exactly once and create one correct undo entry;
- rejected edits do not mutate state, increment revision, or enter undo history.

### Locked-state matrix

Test locked source tracks, locked destination tracks, and locked items for every relevant mutation, including inspector changes, effects, animation, transitions, paste, generated captions, and agent edits.

### Preview

- source preview, timeline preview, seek, play, pause, stop, loop, mute, volume, frame stepping, marks, snapshots, fullscreen, and direct manipulation;
- pointer, keyboard, and UI Automation seek paths reach the same canonical seek behavior;
- unsupported realtime features use exact-preview fallback without showing stale frames as ready.

### Animation and inspector

- media-aware inspector visibility;
- apply/reset behavior;
- keyframe creation, channel editing, Bezier handles, undo/redo, save/reopen, and export parity.

### Export

- use the real Rushframe export dialog;
- verify preset, dimensions, codec/container, audio state, output path validation, progress, completion prompt, cancellation, cleanup, second export in the same process, and overwrite behavior;
- a direct renderer harness result is supporting evidence, never a replacement for this workflow.

### Tasks and agent workflows

- campaign description and task persistence;
- bridge loopback restriction, session authentication, request-size limit, current revision requirement, approval, atomic plan application, audit records, registered-media restriction, safe output paths, provider policy, budgets, and cost reconciliation;
- stale requests must never overwrite newer manual edits.

### Assets and extensions

- path traversal, UNC/network paths, missing files, missing attribution, network-enabled asset packs, unsafe extension permissions, and external-composition directory escapes are rejected.

## 9. Preview, renderer, and export verification

For representative edits, compare realtime preview, exact preview, and final export at:

- clip start;
- trim boundary;
- transition midpoint;
- animation midpoint;
- effect or mask boundary;
- audio fade region;
- final frame.

Verify transforms, crop, opacity, blend mode, text, effects, masks, chroma key, animation, speed, transitions, track state, volume, pan, mute, and fades where used.

Every controlled export must be checked for:

- file existence and non-zero size;
- probe metadata, dimensions, duration, codecs, streams, frame rate, sample rate, and channels;
- complete decode;
- configured black-frame, freeze, silence, loudness, and peak checks;
- representative evidence frames;
- output hash and source records where required;
- render job and render receipt consistency.

A verification failure is not a successful render.

## 10. Persistence and recovery

Test through the real editor:

- save, close, reopen, and exact state restoration;
- a post-reopen edit followed by undo and redo;
- project revision preservation;
- timeline ordering, timing, effects, masks, keyframes, transitions, references, campaign data, tasks, variants, workflows, and receipts;
- old supported schemas and deterministic migrations;
- rejection of newer unsupported schemas;
- autosave creation and retention;
- explicit recovery without silent startup restoration;
- temporary-file cleanup and atomic save replacement;
- concurrent mutation/save coordination without partial snapshots.

## 11. CPU-only performance and human feel

Use existing tools under `qa testing/performance/` and `benchmarks/` when relevant.

Measure comparable before/after workloads for startup, project open, import, timeline interaction, preview scheduling, rapid seek, exact chunk preparation, save/autosave, sustained memory, export, cancellation, and process cleanup.

Do not claim a performance improvement without comparable measurements using the same fixture, environment, build, and command.

A human must still assess:

- scrub responsiveness;
- drag and trim smoothness;
- preview usability;
- audio quality and synchronization;
- export cancellation feel;
- selection, loading, rendering, and error-state visibility;
- keyboard navigation, focus order, accessible names, patterns, contrast, and minimum window usability.

## 12. Remote semantic comparison

Remote image analysis is optional, not a release substitute.

Do not upload screenshots, media, project names, file paths, or user content by default. A remote semantic check requires:

- explicit enablement;
- approved synthetic or non-sensitive fixtures;
- secure endpoint configuration;
- recorded provider and model;
- cost estimate and budget approval when applicable;
- actual-cost reconciliation.

When disabled or unavailable, record `Disabled` or `BLOCKED`. Do not fail deterministic local QA solely because an optional remote model is unavailable.

## 13. Defect workflow

Use:

```text
qa testing/manual review/showcase-edit/defect_log.md
```

For a newly confirmed product defect:

1. reproduce it through the original path when practical;
2. use the next unused `QA-NEW-###` identifier from the existing log;
3. record severity, status, build, project revision, preconditions, exact steps, expected, actual, impact, evidence, and suspected component before fixing;
4. fix the smallest root cause;
5. add a deterministic regression test;
6. run focused tests;
7. run the broader affected suite;
8. retest through the original UI, preview, persistence, or export path;
9. update the same defect entry with the exact retest result and evidence.

Separate product defects from harness defects, missing references, missing optional providers, and environmental blockers.

Do not silently fix ambiguous interaction behavior based on personal preference. Record the competing reasonable behaviors and mark the affected UX requirement `BLOCKED` pending a product/design decision.

## 14. Required commands

Run focused tests first, then the broader gates appropriate to the change.

Repository gates:

```powershell
dotnet build Rushframe.slnx
dotnet test Rushframe.slnx
python -m pytest tests\test_media_intelligence_v2.py -q

dotnet build Rushframe.slnx -c Release
dotnet test Rushframe.slnx -c Release --no-build

dotnet list Rushframe.slnx package --vulnerable --include-transitive
```

Headed UI categories, after the project exists:

```powershell
dotnet test ".\qa testing\ui-automation\Rushframe.UiAutomation.Tests.csproj" -c Release --filter "Category=Visual"
dotnet test ".\qa testing\ui-automation\Rushframe.UiAutomation.Tests.csproj" -c Release --filter "Category=Functional"
```

Performance, when relevant:

```powershell
powershell -ExecutionPolicy Bypass -File ".\qa testing\performance\Smoke-Startup.ps1"
powershell -ExecutionPolicy Bypass -File ".\qa testing\performance\Run-PerformanceBaseline.ps1"
dotnet run --project .\benchmarks\Rushframe.Benchmarks\Rushframe.Benchmarks.csproj -c Release -- --filter "*"
```

Never claim a command or manual action ran when it did not.

## 15. Evidence and reporting

Store generated UI evidence under:

```text
qa testing/results/ui/
```

Store showcase-edit evidence under:

```text
qa testing/manual review/showcase-edit/
```

Each full run must produce:

```text
qa testing/results/ui/reports/<run-id>/summary.md
```

Report separately:

- environment and working-tree state;
- exact commands and results;
- visual states and mismatch percentages;
- functional workflows;
- renderer and media verification;
- persistence and recovery;
- agent and security guardrails;
- performance metrics;
- semantic-check status;
- UX and accessibility findings;
- open, fixed, and harness defects;
- blockers;
- manual validation.

Never report a build, test, screenshot, preview match, export, audio review, benchmark, or UI action that was not actually performed.

## 16. Final decision

Use exactly one result:

```text
PASS — all mandatory deterministic and manual gates completed
FAIL — a mandatory gate failed or a release-blocking defect remains
BLOCKED — a required reference, fixture, environment, dependency, media file, or product decision is unavailable
```

Rushframe cannot receive an overall QA `PASS` unless both `DETAILED_QA_TESTING_PLAN.md` and `QA_TESTING_PLAN.md` pass.

A successful build alone is not PASS. A pixel-perfect screenshot alone is not PASS. A direct renderer export alone is not PASS. A polished video with broken persistence, undo, locking, security, or export behavior is not PASS.

## 17. Completion report for every QA task

End substantial QA work with:

```text
Changes
- QA infrastructure, tests, product fixes, or evidence added.

Files
- Exact files created or modified.

Defects
- IDs opened, fixed, retested, or none.

Verification
- Exact commands, build configuration, test counts, UI paths, exports, probes, and manual checks actually performed.

Results
- Separate visual, functional, renderer, persistence, security, performance, semantic, UX, and showcase status.

Notes / blockers
- Missing references, fixtures, dependencies, product decisions, remaining risks, or none.

Decision
- PASS, FAIL, or BLOCKED.
```
