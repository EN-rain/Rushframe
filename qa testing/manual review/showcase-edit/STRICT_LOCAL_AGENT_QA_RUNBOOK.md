# Strict Local Agent Runbook — Complete Rushframe Showcase QA

Work locally inside:

`C:\Users\LENOVO\Desktop\Projectsss\Rushframe`

This runbook is mandatory for the unfinished showcase-edit QA pass. Continue from the existing project and evidence. Do not restart blindly and do not mark incomplete GUI work as a valid blocker.

---

## 1. Read First

Read these files before taking action:

1. `AGENT_CONTEXT.md`
2. `qa testing\QA_TESTING_PLAN.md`
3. `qa testing\manual review\showcase-edit\README.md`
4. `qa testing\manual review\showcase-edit\edit_brief.md`
5. `qa testing\manual review\showcase-edit\execution_results.md`
6. `qa testing\manual review\showcase-edit\defect_log.md`
7. `qa testing\manual review\showcase-edit\review_checklist.md`
8. `qa testing\manual review\showcase-edit\media_manifest.md`

Inspect the current files:

- `qa testing\manual review\showcase-edit\rushframe_showcase_edit.rushframe`
- `qa testing\manual review\showcase-edit\rushframe_showcase_edit.mp4`

The existing MP4 is not enough to pass QA. Finish every remaining editor, evidence, comparison, persistence, and creative-review requirement.

---

## 2. Non-Negotiable Execution Rule

Operate Rushframe through its real Windows desktop UI and editing commands.

Use the existing automation utilities under:

`qa testing\scripts`

Allowed local techniques include:

- UI Automation by Automation ID or accessible name;
- keyboard shortcuts and focus traversal;
- direct mouse coordinates and drag operations;
- native Win32 dialog automation;
- screen capture;
- the existing QA harness;
- a minimal QA-only automation hook when the real control cannot otherwise be reached.

Do not stop after builds, tests, project inspection, metadata checks, or command-line rendering.

---

## 3. Forbidden Shortcuts

Do not:

- directly edit `.rushframe` JSON to fake editing operations;
- create preview evidence from the final export;
- rename export frames as preview frames;
- use standalone FFmpeg editing as a replacement for Rushframe;
- create a different video outside Rushframe and claim Rushframe produced it;
- mark an unexecuted action as passed;
- reuse old screenshots or legacy QA evidence;
- report PASS because an MP4 exists;
- silently skip difficult requirements;
- stop because a custom WPF control lacks an Automation ID;
- reset, clean, restore, or discard unrelated Git changes;
- modify the original source media;
- download remote media or add downloader behavior.

Direct project-file modification is allowed only as a documented recovery step after a reproducible product failure. It must never be used to simulate timeline operations or evidence.

---

## 4. Existing State

The current project is approximately:

- 25.5 seconds long;
- 1080 × 1920 portrait;
- 11 primary video items;
- one audio track;
- two text items;
- three transitions;
- at least one effect;
- already exportable to H.264/AAC MP4.

Preserve useful existing work. Inspect and correct it through Rushframe.

---

## 5. Remaining Required Work

The previous QA run did not prove these items:

1. Three real Rushframe preview screenshots at matching export timestamps.
2. `timeline_overview.png`.
3. Complete save-close-reopen-continue-save validation.
4. Group selection and group move.
5. Group resize.
6. Persisted timeline marker.
7. Locked-track rejection.
8. Multi-property keyframed animation.
9. Keyframe graph and Bezier easing persistence.
10. Audio fade.
11. Export-dialog preset validation.
12. Preview/export comparison.
13. Full creative scoring.
14. Full audio review.
15. Complete final evidence package.

Every item must be attempted and documented.

---

## 6. Strict Blocker Policy

Use `BLOCKED — required media or environment unavailable` only for a genuine external environment failure, such as:

- no usable Windows desktop session;
- Rushframe cannot launch after troubleshooting;
- required local media is missing and cannot be recovered;
- a mandatory runtime is unavailable and cannot be located or installed;
- required paths cannot be read or written;
- the editor crashes before any workflow can begin.

These are not valid blockers:

- UI automation is inconvenient;
- controls lack Automation IDs;
- a custom timeline is hard to inspect;
- coordinates must be used;
- one script fails;
- a dialog needs native automation;
- the process must be relaunched;
- the current project is incomplete;
- a product defect prevents a requirement.

When Rushframe runs but a requirement fails because of the product, record a defect and use `FAIL — correction required` unless it is fixed and retested.

When an element is difficult to reach, try in this order:

1. Inspect the UI tree.
2. Inspect bounding rectangles.
3. Use keyboard shortcuts.
4. Use focus traversal.
5. Use screen coordinates.
6. Use native Win32 APIs.
7. Add a minimal QA-only hook when necessary.
8. Record a product defect before changing production code.

Do not stop after one failed method.

---

## 7. Required Workflow

### A. Preserve the workspace

Run:

```powershell
git status --short
```

Record the result. Do not reset or discard unrelated files.

Record hashes and timestamps before and after QA for:

- `samplevid.mp4`
- `samplevid_audio.wav`, when present

### B. Automated baseline

Run the required commands:

```powershell
dotnet build Rushframe.slnx
dotnet test Rushframe.slnx
python -m pytest tests\test_media_intelligence_v2.py
```

### C. Open the existing project through Rushframe

Launch:

```powershell
dotnet run --project src\Rushframe.Desktop\Rushframe.Desktop.csproj
```

Open:

`qa testing\manual review\showcase-edit\rushframe_showcase_edit.rushframe`

Verify through the UI:

- project name;
- duration;
- 1080 × 1920 canvas;
- frame rate;
- media assets;
- tracks and items;
- text;
- effects;
- transitions;
- audio;
- markers;
- keyframes;
- canvas/background settings.

Do not rely only on JSON inspection.

### D. Correct the timeline through the editor

The final project must contain:

- at least eight intentional edit points;
- at least two simultaneous visual layers in one section;
- at least one meaningful text item;
- at least one audio or music track;
- at least one multi-property keyframed animation;
- at least one purposeful transition;
- at least one audio fade;
- at least one controlled color adjustment or useful effect;
- at least one timeline marker;
- at least one group-selection edit;
- at least one tested undo and redo action.

Keep duration at or below 30.00 seconds.

### E. Execute real editing tests

Perform and verify:

- source preview;
- seek;
- Mark In;
- Mark Out;
- insert;
- overwrite;
- split;
- trim;
- move;
- snap;
- ripple;
- multi-selection;
- group move;
- group resize;
- copy;
- paste;
- duplicate;
- delete;
- direct preview movement;
- direct preview resizing;
- rotation;
- text styling;
- effects;
- transitions;
- audio gain;
- waveform inspection;
- audio fade;
- marker add/edit;
- undo;
- redo.

Use undo to restore intentional content after destructive tests when needed.

### F. Locked-track rejection

Lock a suitable track and attempt:

- move;
- trim;
- split;
- delete;
- duplicate;
- paste.

Expected:

- every destructive operation is rejected;
- no partial mutation occurs;
- the item and track remain unchanged;
- rejected actions do not pollute undo history.

Capture evidence and unlock the track afterward.

Any destructive operation that succeeds on a locked track is a Critical defect.

### G. Keyframes and Bezier easing

Create or verify animation on at least two properties, such as:

- position plus scale;
- scale plus rotation;
- position plus opacity.

Open the animation graph/editor and modify easing or Bezier handles.

Verify:

- smooth acceleration and deceleration;
- no first-frame or final-frame jump;
- channels remain synchronized;
- save/reopen preserves curves;
- export matches preview.

Capture `animation_graph.png` when practical.

### H. Audio validation

Verify:

- active audio track;
- real waveform;
- controlled gain;
- fade-in or fade-out;
- no doubled audio;
- no clipping;
- no obvious drift;
- no accidental abrupt ending.

Listen to the complete export in a normal external media player. AAC stream presence alone is not an audio-quality pass.

### I. Marker validation

Add a meaningful marker at the build, pause, payoff, or ending.

Then:

1. Undo it.
2. Redo it.
3. Save.
4. Close Rushframe.
5. Reopen the project.
6. Confirm the marker remains.

Record a defect if it does not persist.

### J. Save and reopen

Mandatory sequence:

1. Save the corrected project.
2. Capture timeline state.
3. Close Rushframe fully.
4. Confirm the process exited.
5. Relaunch Rushframe.
6. Reopen the project.
7. Verify all important state.
8. Make one legitimate additional edit.
9. Undo it.
10. Redo it.
11. Save again.

Verify preservation of:

- clips and source ranges;
- track order;
- text and font styling;
- transforms;
- animation channels;
- Bezier curves;
- transitions;
- effects;
- markers;
- audio gain and fades;
- canvas size and frame rate;
- background;
- project duration.

Any meaningful state loss is a Critical defect.

### K. Evidence capture

Create these exact files:

- `timeline_overview.png`
- `preview_frame_01.png`
- `preview_frame_02.png`
- `preview_frame_03.png`
- `export_frame_01.png`
- `export_frame_02.png`
- `export_frame_03.png`

Use matching timestamps for:

1. Opening hook.
2. Mid-edit layered or animated section.
3. Payoff or ending.

Preview images must come from Rushframe’s preview. Export images must come from the final MP4. Do not substitute one for the other.

Record exact timestamps in `execution_results.md`.

The timeline overview must clearly show:

- video tracks;
- text track;
- audio track;
- multiple clips;
- layered section;
- overall timeline structure.

### L. Export through the real Rushframe dialog

Open the real export dialog and verify before rendering:

- width: 1080;
- height: 1920;
- format: MP4;
- video codec: H.264;
- audio codec: AAC;
- quality: High;
- audio enabled.

Export to:

`qa testing\manual review\showcase-edit\rushframe_showcase_edit.mp4`

Do not use only the command-line harness unless the real export dialog is proven broken and recorded as a defect.

### M. Final technical validation

Use FFprobe when available and FFmpeg for decode/content checks. When FFprobe is unavailable, the bundled FFmpeg stream metadata is an accepted fallback. Verify:

- duration no more than 30.00 seconds;
- 1080 × 1920 resolution;
- H.264 video;
- AAC audio;
- expected frame rate;
- non-zero file size;
- complete decode;
- valid container;
- no blank opening or ending;
- no unexpected black frames;
- no long freeze;
- no missing layer;
- no missing audio;
- no obvious clipping or drift;
- no accidental ending.

Write the complete metadata and checks to:

`qa testing\manual review\showcase-edit\export_metadata.txt`

### N. Preview/export comparison

Compare the three matching frame pairs for:

- crop;
- framing;
- position;
- scale;
- rotation;
- opacity;
- typography;
- font rendering;
- outline and shadow;
- color;
- transition state;
- effect intensity;
- masks;
- layer order;
- background;
- safe-area placement.

Record every material mismatch as a new defect.

### O. Creative review

Watch the final export at least three complete times:

1. Story and pacing.
2. Composition, motion, typography, and color.
3. Audio, synchronization, and ending.

Score each category from 0 to 5:

- Hook
- Story progression
- Pacing
- Composition
- Motion
- Typography
- Color
- Layering
- Transitions
- Audio
- Technical quality
- Originality

Pass requirements:

- total score at least 48/60;
- no category below 3;
- Originality at least 4;
- Technical quality exactly 5.

Do not invent scores. Each score needs a concrete note based on visible or audible evidence.

---

## 8. Defect Handling

For every newly reproduced defect:

1. Add it to `defect_log.md` before changing code.
2. Use the next `QA-NEW-###` ID.
3. Record severity and status.
4. Record build and project revision.
5. Record prerequisites and exact steps.
6. Record expected and actual behavior.
7. Record impact on the showcase edit.
8. Attach screenshots, timestamps, and logs.
9. Identify the suspected component.
10. Apply the smallest correct fix.
11. Add or update a regression test.
12. Rebuild and run targeted tests.
13. Retest through the real UI.
14. Update the defect status and retest result.

Do not silently fix defects. Do not modify product code just to make evidence appear successful.

---

## 9. Required Final Documentation

Complete and remove stale statements from:

- `edit_brief.md`
- `execution_results.md`
- `defect_log.md`
- `media_manifest.md`
- `export_metadata.txt`
- `review_checklist.md`

The media manifest must include every used local source, including:

- `samplevid.mp4`;
- `samplevid_audio.wav`, when used;
- any image, font, shape, sticker, logo, or music asset;
- duration and resolution where applicable;
- ownership/license;
- attribution requirement;
- whether used in the final output.

Remove stale claims such as:

- `No final export generated`;
- `Not executed`;
- `No finished edit`;
- `Blocked — no interactive session`.

Replace them with the actual current result.

---

## 10. Final Decision

Use exactly one status:

- `PASS — release candidate accepted`
- `FAIL — correction required`
- `BLOCKED — required media or environment unavailable`

Use PASS only when every release gate is proven.

Use FAIL when Rushframe runs but the final output, editor behavior, project persistence, creative score, preview consistency, or evidence package fails.

Do not use BLOCKED to hide incomplete automation.

---

# Completion Checklist

## Context and safety

- [ ] Read `AGENT_CONTEXT.md`.
- [ ] Read the QA plan and all showcase-edit documentation.
- [ ] Ran `git status --short`.
- [ ] Preserved unrelated working-tree changes.
- [ ] Recorded source media hashes and timestamps before QA.
- [ ] Confirmed no downloader, social URL import, or remote assets were used.
- [ ] Confirmed original source files were not modified.

## Automated verification — before editing

- [ ] `dotnet build Rushframe.slnx` completed.
- [ ] `dotnet test Rushframe.slnx` completed.
- [ ] Literal Python command result recorded.
- [ ] `python -m pytest tests\test_media_intelligence_v2.py` passed.
- [ ] Any baseline failure was recorded as a new defect before fixing.

## Project opening and inspection

- [ ] Rushframe launched successfully.
- [ ] Existing showcase project opened through the UI.
- [ ] Project name verified.
- [ ] Duration verified at no more than 30.00 seconds.
- [ ] Canvas verified as 1080 × 1920.
- [ ] Frame rate verified.
- [ ] All media assets verified.
- [ ] All tracks and timeline items verified.
- [ ] Existing text, transitions, effects, and audio verified.

## Timeline requirements

- [ ] At least eight intentional edit points exist.
- [ ] At least one layered visual section exists.
- [ ] At least one meaningful text item exists.
- [ ] At least one audio/music track exists.
- [ ] At least one multi-property keyframed animation exists.
- [ ] At least one purposeful transition exists.
- [ ] At least one audio fade exists.
- [ ] At least one controlled color adjustment or useful effect exists.
- [ ] At least one timeline marker exists.
- [ ] Duration remains no more than 30.00 seconds.

## Real editing operations

- [ ] Source preview tested.
- [ ] Seek tested.
- [ ] Mark In tested.
- [ ] Mark Out tested.
- [ ] Insert tested.
- [ ] Overwrite tested.
- [ ] Split tested.
- [ ] Trim tested.
- [ ] Move tested.
- [ ] Snap tested.
- [ ] Ripple tested.
- [ ] Multi-selection tested.
- [ ] Group move tested.
- [ ] Group resize tested.
- [ ] Copy tested.
- [ ] Paste tested.
- [ ] Duplicate tested.
- [ ] Delete tested.
- [ ] Direct preview movement tested.
- [ ] Direct preview resizing tested.
- [ ] Rotation tested.
- [ ] Text styling tested.
- [ ] Effects tested.
- [ ] Transitions tested.
- [ ] Audio gain tested.
- [ ] Waveform inspected.
- [ ] Audio fade tested.
- [ ] Marker add/edit tested.
- [ ] Undo tested.
- [ ] Redo tested.

## Locked-track rejection

- [ ] Locked track rejected move.
- [ ] Locked track rejected trim.
- [ ] Locked track rejected split.
- [ ] Locked track rejected delete.
- [ ] Locked track rejected duplicate.
- [ ] Locked track rejected paste.
- [ ] No partial mutation occurred.
- [ ] Rejected actions did not pollute undo history.
- [ ] Track was unlocked after testing.

## Animation and graph

- [ ] At least two animation properties were used together.
- [ ] Animation graph/editor opened.
- [ ] Bezier or easing handles were changed.
- [ ] Motion has intentional acceleration/deceleration.
- [ ] No first-frame jump exists.
- [ ] No final-frame jump exists.
- [ ] Channels remain synchronized.
- [ ] `animation_graph.png` captured when practical.

## Audio

- [ ] Active audio track verified.
- [ ] Waveform reflects real source audio.
- [ ] Gain is controlled.
- [ ] Fade-in or fade-out is present.
- [ ] No doubled audio is audible.
- [ ] No clipping is audible or detected.
- [ ] No obvious drift is audible.
- [ ] Ending audio is deliberate.
- [ ] Full export listened to in an external player.

## Marker

- [ ] Meaningful marker added.
- [ ] Marker addition undone.
- [ ] Marker addition redone.
- [ ] Marker saved.
- [ ] Marker survived close and reopen.

## Save and reopen

- [ ] Project saved before closing.
- [ ] Timeline state captured before closing.
- [ ] Rushframe closed completely.
- [ ] Process exit confirmed.
- [ ] Rushframe relaunched.
- [ ] Project reopened through the UI.
- [ ] Clips and source ranges survived.
- [ ] Track order survived.
- [ ] Text and font styling survived.
- [ ] Transforms survived.
- [ ] Keyframes survived.
- [ ] Bezier curves survived.
- [ ] Effects survived.
- [ ] Transitions survived.
- [ ] Marker survived.
- [ ] Audio gain and fades survived.
- [ ] Canvas and background settings survived.
- [ ] One legitimate edit was made after reopen.
- [ ] That edit was undone and redone.
- [ ] Project was saved again.

## Evidence

- [ ] `timeline_overview.png` created.
- [ ] `preview_frame_01.png` created from Rushframe preview.
- [ ] `preview_frame_02.png` created from Rushframe preview.
- [ ] `preview_frame_03.png` created from Rushframe preview.
- [ ] `export_frame_01.png` created at matching timestamp.
- [ ] `export_frame_02.png` created at matching timestamp.
- [ ] `export_frame_03.png` created at matching timestamp.
- [ ] Exact frame timestamps recorded in `execution_results.md`.
- [ ] Timeline screenshot clearly shows the finished track structure.

## Export dialog

- [ ] Width displayed as 1080.
- [ ] Height displayed as 1920.
- [ ] Format displayed as MP4.
- [ ] Video codec displayed as H.264.
- [ ] Audio codec displayed as AAC.
- [ ] Quality displayed as High.
- [ ] Audio enabled.
- [ ] Export was run through the actual Rushframe dialog.
- [ ] Final MP4 was written to the required path.

## Final technical validation

- [ ] Final file exists and is not zero bytes.
- [ ] Duration is no more than 30.00 seconds.
- [ ] Resolution is exactly 1080 × 1920.
- [ ] H.264 video stream exists.
- [ ] AAC audio stream exists.
- [ ] Frame rate matches the project.
- [ ] Full decode succeeds.
- [ ] File opens in an external media player.
- [ ] No unexpected black frames.
- [ ] No long frozen section.
- [ ] No missing layer.
- [ ] No missing audio.
- [ ] No obvious clipping.
- [ ] No obvious audio drift.
- [ ] Ending is deliberate.
- [ ] `export_metadata.txt` completed.

## Preview/export comparison

- [ ] Opening preview/export pair compared.
- [ ] Mid-edit preview/export pair compared.
- [ ] Payoff/ending preview/export pair compared.
- [ ] Crop matches.
- [ ] Position and scale match.
- [ ] Rotation and opacity match.
- [ ] Typography and font rendering match.
- [ ] Color and effect intensity match.
- [ ] Transition state matches.
- [ ] Masks and layer order match.
- [ ] Background and safe-area placement match.
- [ ] Every material mismatch was logged as a defect.

## Creative review

- [ ] Watched once for story and pacing.
- [ ] Watched once for composition, motion, typography, and color.
- [ ] Watched once for audio, sync, and ending.
- [ ] Hook scored.
- [ ] Story progression scored.
- [ ] Pacing scored.
- [ ] Composition scored.
- [ ] Motion scored.
- [ ] Typography scored.
- [ ] Color scored.
- [ ] Layering scored.
- [ ] Transitions scored.
- [ ] Audio scored.
- [ ] Technical quality scored exactly 5 for PASS.
- [ ] Originality scored at least 4 for PASS.
- [ ] Total score is at least 48/60 for PASS.
- [ ] No category is below 3 for PASS.
- [ ] Score notes cite visible or audible evidence.

## Documentation and defects

- [ ] `edit_brief.md` completed.
- [ ] `execution_results.md` completed.
- [ ] `defect_log.md` completed.
- [ ] `media_manifest.md` completed.
- [ ] `export_metadata.txt` completed.
- [ ] `review_checklist.md` completed.
- [ ] Stale blocked/not-executed statements removed.
- [ ] Every new defect was recorded before fixing.
- [ ] Every fixed defect has a regression test when applicable.
- [ ] Every fixed defect was retested through the real UI.

## Automated verification — after editing

- [ ] `dotnet build Rushframe.slnx` passed after completion.
- [ ] `dotnet test Rushframe.slnx` passed after completion.
- [ ] `python -m pytest tests\test_media_intelligence_v2.py` passed after completion.
- [ ] No newly failing test remains.

## Release gate

- [ ] No open Blocker defect.
- [ ] No open Critical defect.
- [ ] Final export is polished and non-generic.
- [ ] Project state survived save/reopen.
- [ ] Preview and export are materially consistent.
- [ ] Required evidence package is complete.
- [ ] Final status uses exactly one approved status.

Final status:

- [ ] `PASS — release candidate accepted`
- [ ] `FAIL — correction required`
- [ ] `BLOCKED — required media or environment unavailable`

Only one final-status checkbox may be selected.

---

## Required Final Agent Response

Use this structure only:

### Changes
- QA work completed.
- Product fixes made, if any.
- Evidence generated.

### Files
- Exact QA files changed or created.
- Exact source-code and test files changed, if any.

### Verification
- Build result.
- .NET test count.
- Python test count.
- Export duration, resolution, codecs, and file size.
- Creative score.
- Preview/export comparison result.
- Save/reopen result.
- Locked-track result.
- Undo/redo result.

### Defects
- Open defects.
- Fixed defects.
- Retest status.

### Final status

Use exactly one:

- `PASS — release candidate accepted`
- `FAIL — correction required`
- `BLOCKED — required media or environment unavailable`

Do not claim PASS without complete evidence.
