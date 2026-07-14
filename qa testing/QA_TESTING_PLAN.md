# Rushframe Showcase Edit QA Plan

**Application:** Rushframe Desktop Video Editor  
**Repository:** `C:\Users\LENOVO\Desktop\Projectsss\Rushframe`  
**Primary source:** `samplevid.mp4`  
**Required final output:** one polished, non-generic edited video  
**Maximum duration:** 30.00 seconds  
**Target duration:** 20–30 seconds  
**Primary format:** 1080 × 1920, 9:16 portrait, centered composition  
**Status:** Showcase-edit acceptance plan  
**Companion mechanical QA plan:** `qa testing/DETAILED_QA_TESTING_PLAN.md`  

---

## 1. Purpose

This QA pass must prove that Rushframe can be used to create a genuinely good short-form edit, not only a basic feature demonstration.

The required output must feel intentionally edited. It must have a clear creative direction, controlled pacing, meaningful visual hierarchy, coherent typography, purposeful motion, useful sound, and a beginning–build–payoff structure.

A video that only contains simple cuts, a default title, a filter, and a transition does not pass this plan even when it is technically valid.

The final deliverable is one showcase edit no longer than 30 seconds, together with the Rushframe project and evidence that the editor features used in the edit survived preview, save/reopen, undo/redo, and final export.

---

## 2. Old QA Material

This document replaces all previous Rushframe QA plans, execution summaries, defect lists, generic test edits, and previous manual-review checklists.

The following are not valid evidence for this QA pass:

- previous basic-edit exports;
- previous styled-campaign exports;
- previous inspector-only exports;
- previous effect-stack exports;
- old baseline TRX reports;
- old screenshots and frame captures;
- old pass/fail conclusions;
- old defect numbering;
- any output created before this replacement plan.

Only evidence generated for the current showcase-edit QA pass may be used for release evaluation.

---

## 3. Required Output Structure

All new QA evidence must use this folder:

```text
qa testing/manual review/showcase-edit/
```

Required files:

```text
showcase-edit/
├── rushframe_showcase_edit.mp4
├── rushframe_showcase_edit.rushframe
├── edit_brief.md
├── execution_results.md
├── defect_log.md
├── media_manifest.md
├── export_metadata.txt
├── timeline_overview.png
├── preview_frame_01.png
├── preview_frame_02.png
├── preview_frame_03.png
├── export_frame_01.png
├── export_frame_02.png
├── export_frame_03.png
└── review_checklist.md
```

Optional files:

```text
showcase-edit/
├── waveform.png
├── animation_graph.png
├── mask_preview.png
├── transition_preview.png
├── agent_audit_excerpt.jsonl
└── migration_backup_manifest.txt
```

The output filename must be exactly:

```text
rushframe_showcase_edit.mp4
```

The project filename must be exactly:

```text
rushframe_showcase_edit.rushframe
```

---

## 4. Mandatory Creative Brief

Before editing, create `edit_brief.md` with the following fields:

```markdown
# Rushframe Showcase Edit Brief

## Concept
A one-sentence description of the edit.

## Mood
Three to five mood words.

## Story progression
- Opening:
- Build:
- Payoff:
- Ending:

## Visual language
Describe the framing, color, typography, motion, and transition style.

## Audio language
Describe the music, sound effects, dialogue, rhythm, and audio emphasis.

## Restraints
List effects or techniques that must not be overused.

## Intended platform
Short-form vertical social video.

## Duration target
20–30 seconds, never above 30.00 seconds.
```

The concept must be specific. Do not use descriptions such as:

- “cool montage”;
- “cinematic edit”;
- “modern social edit”;
- “fast-paced video”;
- “stylish promo.”

The brief must explain what the viewer should feel and how the edit progresses.

---

## 5. Quality Bar

The final output passes only when it meets all technical and creative requirements.

### 5.1 Required creative qualities

The edit must include:

1. A clear opening hook within the first 1.5 seconds.
2. A visible progression instead of identical pacing throughout.
3. At least one calm or controlled moment before a stronger payoff.
4. A distinct payoff, impact moment, reveal, or rhythmic peak.
5. A deliberate final frame or ending rather than an accidental cut.
6. Consistent visual identity across the full edit.
7. Typography that looks designed for the footage.
8. Motion that supports the subject rather than moving randomly.
9. Effects that are timed to meaning, movement, or audio.
10. Audio that is balanced and synchronized.

### 5.2 Automatic creative failure conditions

The edit fails creative review when any of the following is true:

- it is only a sequence of default cuts;
- every clip has the same duration;
- transitions are applied between every cut without reason;
- effects are stacked only to demonstrate that they exist;
- text uses default placement, default wording, or default styling;
- motion is repetitive or unrelated to the subject;
- the subject is cropped poorly in portrait format;
- the composition is top-aligned when it should be centered;
- the edit has no hook or no payoff;
- the ending cuts off audio, text, or movement;
- the music is louder than important dialogue;
- the edit looks like a software feature test instead of a finished piece.

### 5.3 Restraint requirements

Do not force every feature into the video.

A good edit is more important than maximum feature count. Features must be used only when they improve the result.

Examples:

- one strong mask reveal is better than five unrelated masks;
- one well-timed impact flash is better than repeated flashes;
- two or three transition styles are enough;
- one coherent font family is better than many fonts;
- a controlled grade is better than extreme saturation or contrast;
- subtle sound design is better than placing effects on every cut.

---

## 6. Output Specifications

### 6.1 Duration

- Target: 20–30 seconds.
- Absolute maximum: 30.00 seconds.
- Any output above 30.00 seconds fails.
- Do not pass by trimming only the final container metadata; the actual content must end within 30 seconds.

### 6.2 Canvas

Primary output:

```text
Width: 1080
Height: 1920
Aspect ratio: 9:16
Frame rate: 30 fps or 29.97 fps
Pixel format: yuv420p when supported
```

Portrait framing requirements:

- keep the primary subject centered unless an intentional composition requires otherwise;
- do not default to top alignment;
- preserve important faces, hands, text, and action;
- use keyframed position or scale when the source framing changes;
- check TikTok, Reels, and Shorts safe areas;
- keep critical text away from bottom and right-side interface overlays.

### 6.3 Export

Primary export:

```text
Container: MP4
Video codec: H.264
Audio codec: AAC
Quality: High
Resolution: 1080 × 1920
Audio: enabled
```

The export dialog must show the selected dimensions before rendering.

### 6.4 Audio

- Audio must be present unless the creative brief explicitly requires silence.
- No clipping above 0 dBFS.
- No obvious pumping caused by accidental gain automation.
- Dialogue must remain intelligible.
- Music must support pacing rather than overwhelm the edit.
- Sound effects must be synchronized within a visually acceptable frame tolerance.
- No abrupt audio cut at the end unless it is an intentional hard stop.

---

## 7. Required Edit Construction

The showcase edit must use Rushframe as a real editor.

### 7.1 Media

Use manually uploaded local media only.

Required:

- `samplevid.mp4`;
- at least one manually uploaded music or audio file when available;
- at least one image, logo, sticker, or built-in shape when it improves the concept;
- no social URL import;
- no downloader;
- no runtime web asset retrieval.

Record every source in `media_manifest.md`:

```markdown
| Asset | Type | Local path | Duration | License/ownership | Used in final output |
|---|---|---|---:|---|---|
```

### 7.2 Timeline complexity

The final edit must contain enough structure to prove real editing capability without becoming cluttered.

Minimum timeline requirements:

- at least 8 intentional edit points;
- at least 2 visual tracks used at the same time in one section;
- at least 1 text track;
- at least 1 audio or music track;
- at least 1 section using layered composition;
- at least 1 item with keyframed movement;
- at least 1 item with animated opacity, scale, or rotation;
- at least 1 purposeful transition;
- at least 1 deliberate audio fade;
- at least 1 color adjustment or effect used for visual consistency;
- at least 1 marker used during the editing process.

These are minimums, not a requirement to overuse features.

### 7.3 Pacing structure

Recommended progression:

```text
0.0–1.5s   Hook
1.5–7.0s   Establish subject and visual language
7.0–15.0s  Build pace and introduce layered motion
15.0–24.0s Payoff or strongest section
24.0–30.0s Controlled ending, title, or final visual statement
```

The exact timing may change, but the edit must have an intentional progression.

### 7.4 Hook

The opening must immediately provide one of the following:

- a strong visual reveal;
- an unusual crop or movement;
- a short impactful line of text;
- a rhythmic cut pattern;
- a motion match;
- a sound-led impact;
- a before/after contrast;
- a brief mystery followed by clarification.

A slow default fade from black with no purpose does not count as a hook.

### 7.5 Payoff

The payoff must be the strongest coordinated moment in the edit.

It should combine at least two of the following:

- stronger pacing;
- keyframed movement;
- layered image or shape treatment;
- a controlled impact effect;
- typography emphasis;
- transition synchronized to audio;
- color or contrast shift;
- speed change;
- sound-design accent.

The payoff must still remain readable and visually controlled.

---

## 8. Editing Feature Validation Through the Showcase

Each feature below must be evaluated in the context of the final edit, not as a disconnected demonstration.

### SE-001 — Import and metadata

**Procedure**

1. Launch Rushframe.
2. Create a new project.
3. Import `samplevid.mp4`.
4. Import the chosen music/audio and any visual assets.
5. Confirm thumbnails, durations, dimensions, and media types.

**Pass criteria**

- duration is not zero;
- resolution is correct;
- audio stream is recognized;
- files remain local;
- duplicate imports are handled predictably;
- no asset is silently replaced.

### SE-002 — Source preview and range selection

**Procedure**

1. Preview each source.
2. Seek through the source.
3. Set Mark In and Mark Out around useful moments.
4. Insert selected ranges into the timeline.
5. Repeat with overwrite where appropriate.

**Pass criteria**

- preview is frame-accurate enough for editing;
- range duration matches inserted item duration;
- source start is preserved;
- no unexpected ten-second fallback duration;
- embedded source audio is retained where intended.

### SE-003 — Timeline assembly

**Procedure**

1. Build the rough cut.
2. Reorder selected shots.
3. Trim starts and ends.
4. Split longer clips into useful moments.
5. Use snap and ripple deliberately.

**Pass criteria**

- no invalid negative time;
- no unexpected overlap;
- ripple does not move unrelated locked material;
- split items preserve effects, transforms, masks, audio, and source timing;
- the rough cut already has a clear story progression.

### SE-004 — Multi-selection and group editing

**Procedure**

1. Select at least three timeline items.
2. Move them together.
3. Resize a selected group when appropriate.
4. Copy and paste the group.
5. Undo and redo each operation.

**Pass criteria**

- relative timing remains intact;
- track compatibility is enforced;
- group movement is one undoable action;
- copied items retain all styling and animation;
- no item is left partially moved.

### SE-005 — Portrait composition

**Procedure**

1. Set the sequence to 1080 × 1920.
2. Enable safe-area guides.
3. Position each important shot for portrait framing.
4. Keyframe framing changes when necessary.
5. Check the full timeline for accidental top alignment.

**Pass criteria**

- primary subject remains visually centered or intentionally offset;
- important content is not cropped;
- text remains inside safe areas;
- framing does not jump without purpose;
- preview and export use the same composition.

### SE-006 — Direct preview manipulation

**Procedure**

1. Select an image, video, text, or sticker layer.
2. Move it directly in the preview.
3. Resize it using handles.
4. Rotate it when the concept requires rotation.
5. Use snapping guides.
6. Undo and redo.

**Pass criteria**

- preview handles correspond to the selected item;
- movement is committed through undoable commands;
- snapping is visible and predictable;
- the exported position matches preview;
- resize does not distort unintentionally.

### SE-007 — Keyframe animation

**Procedure**

1. Animate at least one item across multiple properties.
2. Use position plus scale, opacity, or rotation.
3. Open the animation graph.
4. Adjust easing or Bezier handles.
5. Copy or paste a keyframe only when useful.
6. Preview the movement.

**Pass criteria**

- the motion has intentional acceleration and deceleration;
- no unintended jump occurs at the first or last keyframe;
- multiple channels remain synchronized;
- project save/reopen preserves the curves;
- FFmpeg export matches the intended animation.

### SE-008 — Typography

**Procedure**

1. Add a hook title or meaningful caption.
2. Select an appropriate system or local font.
3. Adjust size, alignment, fill, outline, and shadow.
4. Animate text only when it improves readability or impact.
5. Confirm safe-area placement.

**Pass criteria**

- text is readable on every background;
- spelling and timing are correct;
- no default placeholder text remains;
- font choice supports the concept;
- outline and shadow are restrained;
- text does not collide with interface-safe zones;
- preview and export typography match.

### SE-009 — Effects and color

**Procedure**

1. Establish a consistent base grade.
2. Apply one or more effects only where creatively justified.
3. Compare before and after.
4. Confirm that adjustment layers affect the intended range.
5. Avoid excessive saturation, contrast, blur, or sharpening.

**Pass criteria**

- shots feel visually related;
- skin, highlights, and shadows remain usable;
- effects do not hide compression artifacts by making them worse;
- effect order is correct;
- unsupported real-time effects trigger exact FFmpeg preview;
- export matches the approved preview.

### SE-010 — Transitions

**Procedure**

1. Use hard cuts for most normal edit points.
2. Add transitions only for motivated changes.
3. Include at least one purposeful transition.
4. Test duration and alignment.
5. Review transitions frame by frame.

**Pass criteria**

- transition does not create blank or duplicated frames;
- motion direction supports adjacent shots;
- transition duration fits the pacing;
- audio remains continuous unless intentionally cut;
- no transition is added only because the feature exists.

### SE-011 — Layered composition

**Procedure**

1. Create at least one section with two or more simultaneous visual layers.
2. Use an image, sticker, shape, text, or duplicate video layer.
3. Apply opacity, scale, blend, or mask treatment.
4. Check the section in real-time preview and FFmpeg preview.

**Pass criteria**

- all intended layers are visible;
- layer order is correct;
- no lower layer disappears unexpectedly;
- alpha edges are clean;
- real-time preview does not silently misrepresent unsupported composition;
- final export contains the complete layer stack.

### SE-012 — Masks

**Procedure**

1. Add one mask only if it supports the concept.
2. Adjust mask geometry and feathering.
3. Animate the masked item if useful.
4. Confirm whether the editor uses real-time or FFmpeg preview.

**Pass criteria**

- mask reveals only the intended region;
- no inverted or offset mask occurs;
- feathering does not create a harsh unintended border;
- rotated or complex masks fall back to exact preview when required;
- export matches review output.

### SE-013 — Audio editing

**Procedure**

1. Add music or use source audio.
2. Inspect the real waveform.
3. Set clip gain.
4. Add fade-in and fade-out.
5. Adjust pan only when creatively useful.
6. Synchronize visual moments to audio cues.
7. Review through headphones and speakers where available.

**Pass criteria**

- waveform reflects the actual source;
- gain line changes the correct clip;
- audio remains synchronized after speed changes and cuts;
- no clipping or severe imbalance;
- fades are smooth;
- music does not overpower dialogue;
- final export contains the expected mixed audio.

### SE-014 — Save, reopen, and migration

**Procedure**

1. Save the project after the rough cut.
2. Close Rushframe.
3. Reopen the project.
4. Compare the timeline with screenshots and written notes.
5. Continue editing.
6. Save again.

**Pass criteria**

- every track and item remains present;
- transforms, effects, text, masks, transitions, markers, audio settings, and keyframes remain unchanged;
- project revision increments appropriately;
- no migration changes current-format data;
- old-format fixtures migrate without data loss when included in regression testing.

### SE-015 — Undo and redo

During normal construction, verify undo/redo for:

- add clip;
- move clip;
- trim clip;
- split clip;
- group move;
- group resize;
- transform manipulation;
- volume change;
- effect add/remove/reorder;
- keyframe edit;
- text edit;
- transition edit;
- marker edit;
- delete;
- paste and duplicate.

**Pass criteria**

- each logical action is one undo step;
- undo restores the full previous state;
- redo restores the full changed state;
- no stale inspector or preview state remains;
- save/reopen does not corrupt the undoable result.

### SE-016 — Real-time preview versus exact preview

**Procedure**

1. Review supported sections in real-time preview.
2. Add at least one feature that requires FFmpeg fallback only when it improves the edit.
3. Confirm the fallback is automatic.
4. Capture equivalent frames from preview and export.

**Pass criteria**

- supported layers update without full timeline re-render;
- unsupported effects do not display a misleading approximation;
- FFmpeg fallback renders all layers;
- timing and composition differences are not materially visible;
- audio stays aligned with the playhead.

### SE-017 — Export

**Procedure**

1. Open the export dialog.
2. Select 1080 × 1920 portrait output.
3. Confirm displayed dimensions.
4. Select MP4, H.264, AAC, High quality.
5. Export to the showcase-edit folder.
6. Inspect metadata using FFprobe when available; otherwise use the bundled FFmpeg stream metadata and decode checks.
7. Watch the full video at least three times.

**Pass criteria**

- duration is at most 30.00 seconds;
- resolution is exactly 1080 × 1920;
- video and audio streams exist;
- no blank first or final frame;
- no unexpected black frame between edits;
- no missing visual layer;
- no missing source audio;
- no clipping or desynchronization;
- no unresolved placeholder or offline-media frame;
- output plays in a normal external media player.

---

## 9. Good-Edit Review Rubric

Score each category from 0 to 5.

| Category | 0 | 3 | 5 |
|---|---|---|---|
| Hook | No hook | Understandable opening | Immediate, memorable, and relevant |
| Story progression | Random shots | Basic order | Clear beginning, build, payoff, ending |
| Pacing | Flat or chaotic | Mostly controlled | Rhythm changes intentionally and supports content |
| Composition | Poor crop | Mostly usable | Strong portrait framing and visual hierarchy |
| Motion | Random/default | Some useful motion | Purposeful, smooth, and synchronized |
| Typography | Placeholder/default | Readable | Distinct, coherent, timed, and well composed |
| Color | Inconsistent | Acceptable | Unified, controlled, and supports mood |
| Layering | Missing/broken | Functional | Clean depth and meaningful visual treatment |
| Transitions | Distracting | Mostly acceptable | Motivated and integrated with movement/audio |
| Audio | Missing/unbalanced | Usable | Clear, dynamic, synchronized, and polished |
| Technical quality | Broken | Minor issues | Clean export with no visible defects |
| Originality | Generic template | Some personality | Specific creative identity, not a feature demo |

Maximum score: 60.

Release requirement:

```text
Minimum total score: 48 / 60
No category may score below 3
Originality must score at least 4
Technical quality must score 5
```

A technically perfect but generic edit fails.

---

## 10. Frame Review

Capture frames from three intentional moments:

1. Opening hook.
2. Mid-edit layered or animated section.
3. Payoff or final composition.

For each moment, capture:

- a frame from Rushframe preview;
- the matching frame from the final exported video.

Compare:

- crop;
- position;
- scale;
- rotation;
- opacity;
- text placement;
- font rendering;
- color;
- effect intensity;
- mask edges;
- transition state;
- background;
- safe-area compliance.

A material mismatch is a defect even when the export itself looks acceptable.

---

## 11. Audio Review

Review the final video with:

- normal speakers;
- headphones when available;
- volume set low enough to reveal poor balance;
- volume set high enough to reveal clipping or harshness.

Check:

- dialogue intelligibility;
- music balance;
- left/right balance;
- fade quality;
- transient clipping;
- sync at strong visual impacts;
- source-audio continuity across cuts;
- silence caused by missing streams;
- abrupt ending;
- repeated or doubled audio caused by stacked clips.

Record the findings in `review_checklist.md`.

---

## 12. Negative Tests

These tests are separate from the creative output but must use the same project where practical.

### NEG-001 — Duration overflow

Attempt an export with a timeline above 30 seconds.

Expected:

- QA output is rejected until the sequence is shortened;
- the final accepted output remains at or below 30 seconds.

### NEG-002 — Offline media

Temporarily make one source unavailable.

Expected:

- Rushframe marks it offline;
- final export is not silently completed with missing content;
- relink restores the item.

### NEG-003 — Locked track

Lock a track and attempt move, trim, delete, split, duplicate, and paste operations.

Expected:

- destructive changes are blocked;
- track and item remain unchanged;
- undo history is not polluted by rejected actions.

### NEG-004 — Invalid export path

Attempt export to an unavailable or unwritable path.

Expected:

- clear error;
- no false success message;
- no corrupt zero-byte final output treated as valid.

### NEG-005 — Unsupported real-time feature

Apply an advanced feature unsupported by the real-time compositor.

Expected:

- Rushframe uses exact FFmpeg preview;
- no inaccurate approximation is shown as final truth.

### NEG-006 — Stale agent revision

Submit an agent edit with an outdated `base_revision`.

Expected:

- request is rejected;
- manual changes remain unchanged;
- audit entry records the conflict.

### NEG-007 — Unauthorized agent request

Send a local bridge request without the current session token.

Expected:

- request is rejected;
- no project mutation;
- no source-media modification.

### NEG-008 — Unsafe asset pack

Load an asset pack with path traversal or network permission.

Expected:

- pack is rejected or disabled;
- no external download;
- no arbitrary code execution.

---

## 13. Automated and Mechanical Verification

The complete setup, state-capture, visual-regression, functional UI automation, renderer, persistence, agent-guardrail, performance, semantic, UX, and reporting procedure is defined in:

```text
qa testing/DETAILED_QA_TESTING_PLAN.md
```

Before manual editing, run the repository baseline:

```powershell
dotnet build Rushframe.slnx
dotnet test Rushframe.slnx
python -m pytest tests\test_media_intelligence_v2.py -q

dotnet build Rushframe.slnx -c Release
dotnet test Rushframe.slnx -c Release --no-build

dotnet list Rushframe.slnx package --vulnerable --include-transitive
```

When the separate headed UI project exists, run its visual and functional categories explicitly. Do not merge them into one result:

```powershell
dotnet test ".\qa testing\ui-automation\Rushframe.UiAutomation.Tests.csproj" -c Release --filter "Category=Visual"
dotnet test ".\qa testing\ui-automation\Rushframe.UiAutomation.Tests.csproj" -c Release --filter "Category=Functional"
```

Required baseline:

- Debug and Release builds succeed;
- zero build errors and no new warnings;
- all current .NET and Python tests pass;
- every mandatory approved visual state independently passes its threshold;
- every mandatory functional workflow independently passes;
- exact preview/export, save/reopen, undo/redo, locked-state, revision, agent-security, export-dialog, decode, and audio checks pass;
- no mandatory state is treated as passed when its design reference, fixture, environment, or interaction specification is missing.

After the showcase edit is complete, repeat the affected focused tests and all required repository gates. A newly failing deterministic test, visual state, functional workflow, export verification, or persistence check blocks release.

The current visual-regression gate remains `BLOCKED` until approved files exist under `qa testing/design-reference/`. Automated tests do not replace real-editor construction, final export review, full audio listening, or the human creative-quality decision.

---

## 14. Export Metadata Validation

Record FFprobe output in `export_metadata.txt` when FFprobe is available. Otherwise record the bundled FFmpeg stream metadata plus complete decode, black-frame, freeze, and audio-peak checks.

Required checks:

```text
Duration <= 30.00 seconds
Width = 1080
Height = 1920
Video stream present
Audio stream present
Frame rate = expected project frame rate
No invalid duration
No zero-byte stream
No unreadable container error
```

Also record:

- file size;
- video codec;
- audio codec;
- sample rate;
- channel count;
- pixel format;
- average video bitrate;
- creation timestamp.

---

## 15. Defect Severity

### Blocker

- editor cannot launch;
- project cannot be saved or reopened;
- final export cannot be produced;
- export is corrupt or unplayable;
- timeline loses major content;
- unauthorized agent request changes the project;
- source file is modified or destroyed.

### Critical

- final output exceeds 30 seconds despite configured limit;
- missing audio or visual layer in final export;
- preview materially differs from final export without warning/fallback;
- undo corrupts project state;
- save/reopen loses keyframes, text, effects, masks, or timing;
- locked tracks allow destructive edits;
- stale agent revision overwrites manual edits.

### Major

- transition artifact;
- incorrect portrait framing;
- noticeable audio drift;
- mask offset;
- direct manipulation commits wrong transform;
- font/style mismatch;
- repeated preview stall that interrupts normal editing;
- export preset shows incorrect dimensions.

### Minor

- cosmetic spacing issue;
- non-blocking label mismatch;
- slight inspector refresh delay;
- small visual issue with no export impact.

---

## 16. Defect Record Format

Use `defect_log.md`.

```markdown
## QA-NEW-001 — Concise title

**Severity:** Blocker / Critical / Major / Minor  
**Status:** Open / Fixed / Retest Passed / Retest Failed  
**Build:**  
**Project revision:**  

### Preconditions

### Steps
1.
2.
3.

### Expected

### Actual

### Impact on showcase edit

### Evidence
- Screenshot:
- Video timestamp:
- Project file:
- Log:

### Suspected component

### Retest
```

Do not reuse old QA defect numbers.

---

## 17. Execution Result Format

Use `execution_results.md`.

```markdown
# Rushframe Showcase Edit QA Results

## Environment

## Source media

## Creative brief summary

## Automated verification

## Timeline summary

## Features used intentionally

## Tests executed

| Test ID | Result | Evidence | Notes |
|---|---|---|---|

## Export metadata

## Creative review score

| Category | Score | Notes |
|---|---:|---|

## Defects

## Blocked tests

## Final decision
PASS / FAIL

## Release recommendation
```

---

## 18. Final Review Checklist

The showcase edit passes only when every required item below is checked.

### Output

- [ ] `rushframe_showcase_edit.mp4` exists.
- [ ] Duration is no more than 30.00 seconds.
- [ ] Resolution is exactly 1080 × 1920.
- [ ] Video stream exists.
- [ ] Audio stream exists.
- [ ] Output plays outside Rushframe.
- [ ] No blank or corrupt frames.

### Creative quality

- [ ] Hook works within 1.5 seconds.
- [ ] Edit has a clear beginning, build, payoff, and ending.
- [ ] Pacing changes intentionally.
- [ ] The edit does not look like a generic template.
- [ ] Originality score is at least 4/5.
- [ ] Typography is designed and readable.
- [ ] Motion supports the content.
- [ ] Effects are restrained and purposeful.
- [ ] Portrait framing is centered and intentional.
- [ ] Final frame feels deliberate.

### Timeline and editing

- [ ] At least 8 intentional edit points.
- [ ] At least one layered visual section.
- [ ] At least one useful keyframed movement.
- [ ] At least one purposeful transition.
- [ ] At least one controlled color/effect treatment.
- [ ] At least one meaningful audio fade.
- [ ] Group editing was tested.
- [ ] Undo and redo were tested during actual construction.
- [ ] Project reopened without state loss.

### Preview/export consistency

- [ ] Opening preview frame matches export.
- [ ] Mid-edit preview frame matches export.
- [ ] Payoff/final preview frame matches export.
- [ ] Unsupported features used exact FFmpeg fallback.
- [ ] Layer order matches.
- [ ] Text rendering matches.
- [ ] Audio sync matches.

### Safety and reliability

- [ ] Manual upload only.
- [ ] No downloader or social URL import.
- [ ] Original source media was not modified.
- [ ] Locked track behavior passed.
- [ ] Autosave/recovery did not corrupt the project.
- [ ] Agent authentication and revision conflict tests passed when agent features were included.
- [ ] Asset packs did not access network paths or external URLs.

### Evidence

- [ ] Project file saved.
- [ ] Edit brief saved.
- [ ] Execution results saved.
- [ ] Defect log saved.
- [ ] Media manifest saved.
- [ ] Export metadata saved.
- [ ] Timeline screenshot saved.
- [ ] Three preview frames saved.
- [ ] Three matching export frames saved.
- [ ] Review checklist signed.

---

## 19. Release Gate

Rushframe passes this QA only when:

1. All automated tests pass.
2. No Blocker or Critical defect remains open.
3. The final export is at most 30 seconds.
4. The final export is technically valid.
5. Preview and export are materially consistent.
6. The project survives save and reopen.
7. Undo/redo works through real editing operations.
8. The creative score is at least 48/60.
9. Originality scores at least 4/5.
10. Technical quality scores 5/5.
11. A human reviewer agrees that the result feels like a finished edit rather than a generic feature demonstration.

Final status must be exactly one of:

```text
PASS — release candidate accepted
FAIL — defects or quality issues require correction
BLOCKED — required media, environment, or dependency unavailable
```

---

## 20. Final Rule

Do not mark this QA as passed because the editor can technically export a video.

The pass condition is a polished, coherent, non-generic video created inside Rushframe, no longer than 30 seconds, with reliable project state, accurate preview, complete layered export, good audio, and evidence that the editor functions used in the piece worked correctly.
