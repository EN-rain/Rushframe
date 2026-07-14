# Showcase Edit Review Checklist

## Technical

- [x] Final MP4 exists.
- [x] Duration is no more than 30.00 seconds (25.50s).
- [x] Resolution is 1080 x 1920.
- [x] Video and audio streams exist.
- [x] FFmpeg full decode passed.
- [x] No black or frozen interval detected at configured thresholds.
- [x] Audio technical analysis: mean -16.3dB, max -2.2dB, no clipping, no silence detected.
- [ ] Full audible clipping, drift, doubled-audio, and ending review was not completed (agent cannot hear audio).
- [x] Current-run export completed through the real Export Settings and Save As dialogs.
- [x] QA-NEW-010 fixed MP4 exists: `rushframe_showcase_edit_qa_new_010_fixed.mp4`.
- [x] QA-NEW-010 fixed MP4 decodes cleanly at 25.50s, 1080 x 1920, H.264/AAC.
- [x] QA-NEW-010 corrected export frames show opening and final titles fully visible.
- [x] QA-NEW-010 first and final in-app preview captures are valid and match their export frames.
- [x] Two corrected exports completed through the actual Export Settings and Save As dialogs using unique output paths.
- [x] Both corrected dialog exports decode completely and have no configured black/freeze events.
- [x] Real-dialog cancellation stops FFmpeg and restores controls.

Playback device used: none available to this agent; no working speaker/headphone audio perception is exposed through the Codex/PowerShell session. Full playback with listening completed: No.

Audio listening items:
- [ ] Clipping or distortion: BLOCKED, not assessed by listening.
- [ ] Crackling: BLOCKED, not assessed by listening.
- [ ] Clicks or pops: BLOCKED, not assessed by listening.
- [ ] Unexpected silence: BLOCKED, not assessed by listening.
- [ ] Music/dialogue balance: BLOCKED, not assessed by listening.
- [ ] Audio/video synchronization: BLOCKED, not assessed by listening.
- [ ] Gradual sync drift: BLOCKED, not assessed by listening.
- [ ] Abrupt audio cuts: BLOCKED, not assessed by listening.
- [ ] Fade-in and fade-out quality: BLOCKED, not assessed by listening.
- [ ] Ending truncation: BLOCKED, not assessed by listening.
- [ ] Audio continuing after the final visual frame: BLOCKED, not assessed by listening.
- [ ] Visuals continuing after the audio ends: BLOCKED, not assessed by listening.
- Timestamped issues: none recorded because listening could not be performed.

## Editor reliability

- [x] Project opened through Rushframe UI (inherited — prior session).
- [x] Marker add, undo, redo, save, close, and reopen were tested (inherited).
- [x] Project reopened with markers preserved (inherited).
- [x] Post-reopen edit/undo/redo tested (inherited).
- [x] Locked-track Delete retested through UI (inherited).
- [x] Locked-track move, trim, paste, transform, effect, animation, and transition UI retested on QA copy; saved hash stayed unchanged for each attempt.
- [ ] Group move/group resize evidence incomplete.
- [x] Keyframe graph and Bezier persistence passed: `positionX` persisted after save/reopen, post-reopen edit changed `1.2s` value from `120` to `140`, undo restored `120`, redo restored `140`, and the redone state was saved.
- [x] Export Settings dialog captured and confirmed: 1080x1920, Portrait, MP4, High quality; include-audio confirmed via UIA; H.264/AAC confirmed by export metadata.
- [x] Export-dialog screenshot captured: `export_dialog_settings.png`.
- [x] Three matching preview/export frame pairs retested for QA-NEW-009: UIA seek now updates preview at 1.0s, 12.5s, and 24.0s.

## Creative

- [x] Hook rescored 4/5 for QA-NEW-010 corrected export; opening title is fully visible.
- [x] Story progression scored 3/5 in non-audio assessment.
- [x] Pacing scored 3/5 in non-audio assessment, with audio rhythm excluded.
- [x] Composition scored 3/5 in non-audio assessment.
- [x] Motion scored 3/5 in non-audio assessment.
- [x] Typography rescored 4/5 for QA-NEW-010 corrected export; both titles are readable.
- [x] Color scored 3/5 in non-audio assessment.
- [x] Layering scored 3/5 in non-audio assessment.
- [x] Transitions scored 3/5 in non-audio assessment, with audio integration excluded.
- [ ] Audio reviewed and scored (requires listening review).
- [x] Technical quality rescored 5/5; first/final preview-export pairs match, both real-dialog exports complete and decode, and no unresolved non-audio visual/export defect remains.
- [x] Originality scored 4/5 in non-audio assessment.

## Score

Full creative score remains blocked because audio listening is excluded. QA-NEW-010 corrected non-audio reassessment: Hook 4, Story progression 3, Pacing 3, Composition 3, Motion 3, Typography 4, Color 3, Layering 3, Transitions 3, Audio N/A excluded, Technical quality 5, Originality 4.

Scored subtotal: 38/55 (69.1%). Minimum category result: PASS, lowest scored category is 3. Technical-quality requirement: PASS at 5/5. Result: NON-AUDIO PASS.

## Decision

- [x] NON-AUDIO PASS
- [ ] NON-AUDIO FAIL
- [x] FULL RELEASE BLOCKED — audible review only

Reviewer: opencode (agent-assisted)  
Date: 2026-07-13  
Build: local Debug/Release build — 0 warnings, 0 errors, 191 C# + 5 Python tests passing

## Agent capability note

The following were confirmed programmatically via PowerShell UI Automation (UIAutomationClient):
- Rushframe launches and renders its main window
- File menu contains New/Open/Save/Import/Export items
- Export Settings dialog contains: Current Size/Portrait/Landscape/Custom size presets, 480p/720p/1080p resolution, W/H edit fields, Container combo, Quality combo, "Include mixed audio" checkbox, samplevid.mp4 source display, "1080 x 1920 - Portrait" indicator, Cancel/Continue buttons
- Export rendering progress bar and cancel button appear after invoking export

The following remains incomplete:
- Listening to audio for subjective quality review

QA-NEW-010 update: Retest Passed. First and final Rushframe preview/export pairs match with complete, unclipped titles. Two real-dialog corrected exports completed; the earlier apparent stall was the modal completion prompt awaiting Yes/No.
