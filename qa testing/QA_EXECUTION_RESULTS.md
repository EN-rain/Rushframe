# Rushframe Showcase Edit QA Results

Canonical detailed results:

```text
qa testing/manual review/showcase-edit/execution_results.md
```

## Automated verification — 2026-07-13 (full re-run)

| Gate | Result | Run |
|------|--------|-----|
| Debug build (`dotnet build Rushframe.slnx`) | **0 warnings, 0 errors** | Current |
| Debug tests (`dotnet test Rushframe.slnx`) | **191 passed** (35+138+7+11) | Current |
| Python tests (`python -m pytest tests/test_media_intelligence_v2.py -q`) | **5 passed** | Current |
| Release build (`dotnet build -c Release`) | **0 warnings, 0 errors** | Current |
| Release tests (`dotnet test -c Release --no-build`) | **191 passed** | Current |
| NuGet vuln scan (`dotnet list package --vulnerable --include-transitive`) | **No vulnerable packages** | Current |
| Release startup smoke (`Smoke-Startup.ps1`) | **PASS** — ~7MB managed, clean exit | Current |
| Benchmarks (4/4) | **Zero allocation**: SequentialLookup 28.3ns, ReverseLookup 48.6ns, CachedDuration 51.6ns, DurationAfterMutation 2.19μs | Current |
| Audio technical (FFmpeg volumedetect+astats) | **PASS** — mean -16.3dB, max -2.2dB, no clipping, no silence | Current |
| Export frame extraction (1.0s, 12.5s, 24.0s) | **PASS** — 3 frames extracted | Current |
| Export Settings dialog (UI Automation + screenshot) | **PASS** - screenshot shows 1080x1920 Portrait, MP4, High quality; include-audio confirmed by UIA; H.264/AAC confirmed by export metadata | Current |
| Current-run export through real dialog | **PASS** - `rushframe_showcase_edit_current_run.mp4` produced from Export Settings and Save As dialogs | Current |
| Corrected real-dialog export attempts | **PASS** - two unique-path exports completed in ~30.5s each; both are byte-identical to each other and the direct-service fixed export, decode cleanly, and restore controls after the modal completion prompt is answered | Current |
| Real-dialog cancellation probe | **PASS** - Cancel terminated FFmpeg within ~2s and restored controls; the incomplete partial file was not treated as complete | Current |
| QA-NEW-009 UIA preview seek retest | **PASS** - captured correct preview frames at 1.0s, 12.5s, and 24.0s after fix | Current |
| QA-NEW-009 playback/frame-step retest | **PASS** - playback advanced from 12.5s; next-frame after UIA seek advanced from 15.000s to 15.033s | Current |
| QA-NEW-010 project-data fix | **RETEST PASSED** - first and final Rushframe preview/export pairs match; both titles are complete, correctly placed, and unclipped | Current |
| QA-NEW-010 fixed export verification | **PASS** - `rushframe_showcase_edit_qa_new_010_fixed.mp4` decodes cleanly at 25.50s, 1080x1920, H.264/AAC; black/freeze checks reported no events | Current |
| Cross-channel animation-cache invalidation regression | PASS (inherited prior session) | Prior |
| Showcase harness export | PASS (inherited prior session) | Prior |
| Complete FFmpeg decode | PASS (inherited prior session) | Prior |
| Black/freeze threshold checks | PASS (inherited prior session) | Prior |

## Export summary

- Project: `qa testing/manual review/showcase-edit/rushframe_showcase_edit.rushframe`
- Latest verified export: `qa testing/manual review/showcase-edit/rushframe_showcase_edit_current_run.mp4`
- Duration: 25.50 seconds
- Resolution: 1080 × 1920
- Frame rate: 30 fps
- Video: H.264, yuv420p
- Audio: AAC-LC, 48 kHz stereo

## Audio listening review

- Playback device used: none available to this agent; no working speaker/headphone audio perception is exposed through the Codex/PowerShell session.
- Full playback completed with listening: No.
- Result: BLOCKED.
- Timestamped issues: none recorded because listening could not be performed.
- Checklist status: clipping/distortion, crackling, clicks/pops, unexpected silence, balance, sync, drift, abrupt cuts, fade quality, ending truncation, post-final-frame audio, and visuals-after-audio are all BLOCKED rather than PASS/FAIL.

## Defects

| ID | Severity | Code Fix | Automated Regression | UI Retest |
|---|---|---|---|---|
| QA-NEW-001 | Major | Updated stale Desktop tests | PASS (inherited) | N/A — test-only |
| QA-NEW-002 | Major | Corrected QA doc path | PASS (inherited) | N/A — doc-only |
| QA-NEW-003 | Critical | Locked-track guards Delete/Split/Duplicate | PASS (inherited) | PASS via UI (inherited) |
| QA-NEW-004 | Major | FFprobe fallback to FFmpeg | PASS (inherited) | N/A — tooling |
| QA-NEW-005 | Critical | Lock guards across ALL command paths | PASS (inherited) | PASS - UI retested for move, trim, paste, transform, effect, animation, and transition |
| QA-NEW-006 | Major | Fix effect removal undo order | PASS (inherited) | PASS - UI stack-order retest restored removed middle effect to original index |
| QA-NEW-007 | Major | Lock/invalid-duration guards transitions | PASS (inherited) | PASS - UI retested locked transition apply plus zero/negative duration handling |
| QA-NEW-008 | Major | Per-channel animation cache versioning | PASS (inherited) | N/A - no meaningful user-facing UI reproduction exists |
| QA-NEW-009 | Critical | UIA/keyboard slider changes now invoke seek; exact frame-step uses canonical playhead | PASS | PASS - vision-captured frames match 1.0s, 12.5s, 24.0s |
| QA-NEW-010 | Major | Project-data fix applied to duplicated title layers | N/A | Retest Passed - first and final preview/export pairs match with no clipping |
| QA-NEW-011 | Major (suspected) | No code change | N/A | Not Reproduced - two real-dialog exports completed; apparent stall was the modal completion prompt awaiting Yes/No |

## Manual evidence status

Completed:
- Export settings dialog screenshot: `export_dialog_settings.png`
- Current-run export through the real dialog: `rushframe_showcase_edit_current_run.mp4`
- Preview captures at 1.0s, 12.5s, and 24.0s
- Keyframe/Bezier persistence captures before save, after reopen, after post-reopen edit, after undo, and after redo
- QA-NEW-005 UI evidence: `locked_move_*`, `locked_trim_*`, `locked_paste_*`, `locked_transform_*`, `locked_effect_*`, `locked_animation_*`, `locked_transition_*`
- QA-NEW-006 UI evidence: `qa_new_006_effect_stack_before.png`, `qa_new_006_effect_stack_removed.png`, `qa_new_006_effect_stack_undo.png`, `qa_new_006_effect_stack_redo.png`
- QA-NEW-007 UI evidence: `qa_new_007_transition_baseline.png`, `qa_new_007_zero_duration_attempt.png`, `qa_new_007_negative_duration_attempt.png`, `qa_new_007_locked_transition_before.png`, `qa_new_007_locked_transition_apply_after.png`
- Visual sweep frames: `visual_sweep_01_0_0s.png` through `visual_sweep_07_25_4s.png`
- QA-NEW-010 fixed export: `rushframe_showcase_edit_qa_new_010_fixed.mp4`
- QA-NEW-010 fixed export frames: `qa_new_010_first_frame_after_export.png`, `qa_new_010_final_frame_after_export.png`
- QA-NEW-010 preview captures: `qa_new_010_first_frame_after_preview.png`, `qa_new_010_final_frame_after_preview.png`
- QA-NEW-010 matching export frames: `qa_new_010_first_frame_after_export.png`, `qa_new_010_final_frame_after_export.png`
- Corrected real-dialog outputs: `rushframe_showcase_edit_real_dialog_attempt1_20260713_2330.mp4`, `rushframe_showcase_edit_real_dialog_attempt2_20260713_2340.mp4`
- Real-dialog cancellation evidence: `rushframe_showcase_edit_real_dialog_cancel_probe_20260713_2340.mp4`

Final manual status:
1. **Preview/export frame comparison** - PASS for QA-NEW-009 retest. After the fix, UIA seek captures at 1.0s, 12.5s, and 24.0s visually update to the requested frames.
2. **Keyframe/Bezier persistence proof** - PASS. `positionX` persisted as `0s / 0 / Bezier` and `1.2s / 120 / Bezier`; post-reopen edit changed the second value to `140`, undo restored `120`, redo restored `140`, and the redone state was saved.
3. **Audio listening review** - BLOCKED. FFmpeg technical analysis passed, but subjective listening cannot be completed honestly in this environment because this agent cannot hear system audio.
4. **Creative scoring** - BLOCKED. Scoring is withheld because the required audio listening review is blocked.
5. **QA-NEW-010 visual retest** - PASS. Rushframe exact-preview captures at `00:00` and `00:25` match the corresponding corrected export frames. `DON'T BLINK.` and `STILL HERE?` are complete, correctly punctuated, positioned consistently, rendered with the expected Arial treatment, inside the safe area, and not clipped.
6. **Corrected real-dialog export workflow** - PASS. Two unique-path exports completed with live FFmpeg/file-growth evidence. The earlier apparent stall was not reproduced; a modal `Export Complete` prompt was awaiting Yes/No. Dismissing it restored operation controls and allowed the second export in the same process.

Full creative threshold result: BLOCKED because the mandatory Audio category cannot be scored without listening. The completed non-audio assessment passes at 38/55 (69.1%), with no scored category below 3, Originality 4, and Technical quality 5.

## Non-audio creative assessment

Audio is excluded from this run. This is not the final full creative score.

| Category | Score | Evidence | Weakness |
|---|---:|---|---|
| Hook | 2 | `visual_sweep_01_0_0s.png` shows an immediate title/face hook. | The hook title is cropped at the left edge, reducing readability. |
| Story progression | 3 | 0.0s close-up, 6.0s kitchen, 12.5s aisle, 18.0s table, 24.0s outdoor ending. | Progression is understandable but mainly montage-driven. |
| Pacing | 3 | Timeline/export frames show varied scene lengths and four transitions. | Audio-rhythm pacing is excluded, so only visual pacing was scored. |
| Composition | 3 | Subject remains mostly centered in `visual_sweep_03_6_0s.png`, `visual_sweep_04_12_5s.png`, and `visual_sweep_05_18_0s.png`. | Horizontal source framing leaves large black vertical-format bands. |
| Motion | 3 | Keyframed title/clip evidence and preview/export frame changes show motion support. | Smoothness was judged visually only; audio sync excluded. |
| Typography | 2 | `visual_sweep_01_0_0s.png`, `visual_sweep_06_24_0s.png`, and `visual_sweep_07_25_4s.png`. | Title text is cropped off-frame, so readability fails the non-audio standard. |
| Color | 3 | Visual sweep frames keep a consistent purple/soft-light treatment. | Some source overlays/watermarks remain visible. |
| Layering | 3 | Text layer, video layer, transitions, and keyframed overlay evidence are present. | Layering is functional but not clean enough to offset cropped text. |
| Transitions | 3 | Four cross-dissolve transitions in the project and visual frame progression. | Audio integration excluded; transitions are basic. |
| Audio | N/A | Excluded from this run. | Audio listening remains outstanding. |
| Technical quality | 3 | Export decodes and preview/export consistency passed. | Visible title crop prevents a clean non-audio technical score of 5. |
| Originality | 4 | Character-focused joke/title concept with repeated themed footage and payoff title. | Still depends heavily on source footage/watermarks. |

Scored subtotal: 32. Maximum possible subtotal: 55. Percentage: 58.2%. Minimum category result: FAIL, lowest scored category is 2. Result: NON-AUDIO FAIL.

### QA-NEW-010 reassessment - 2026-07-13

Audio remains excluded. This reassessment covers only the affected visual categories after the project-data fix and corrected export.

| Category | Score | Evidence | Weakness |
|---|---:|---|---|
| Hook | 4 | `qa_new_010_first_frame_after_export.png` | Opening title is now readable and inside frame; the source watermark/black-band layout still reduces polish. |
| Composition | 3 | `qa_new_010_first_frame_after_export.png`, `qa_new_010_final_frame_after_export.png` | Titles are inside safe area, but horizontal source footage still leaves large black bands. |
| Typography | 4 | `qa_new_010_first_frame_after_export.png`, `qa_new_010_final_frame_after_export.png` | Both titles are readable and no longer cropped; typography remains simple. |
| Technical quality | 5 | `qa_new_010_first_frame_after_preview.png`, `qa_new_010_final_frame_after_preview.png`, matching export frames, and both corrected real-dialog outputs | First/final preview-export pairs match; both real-dialog exports decode completely with no configured black/freeze events, missing media, font substitution, clipping, or unresolved visual/export defect. |

Updated non-audio subtotal with the affected retest scores: 38/55 (69.1%). Minimum category result: PASS, lowest scored category is 3. Technical-quality requirement: PASS at 5/5. Result: NON-AUDIO PASS.

## Final decision

`BLOCKED` - Non-audio QA passes. QA-NEW-010 is retest-passed and the corrected real-dialog workflow completed twice. Project release remains blocked only by the required audible review, which this environment cannot perform.

## Release recommendation

NON-AUDIO PASS — project release remains blocked only by the audible clipping/sync/drift/balance/fade/ending review.
