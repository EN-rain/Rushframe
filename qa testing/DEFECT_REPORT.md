# Rushframe Showcase Edit Defect Log

Canonical defect details:

```text
qa testing/manual review/showcase-edit/defect_log.md
```

## Current status — 2026-07-13 (post-agent QA session)

| ID | Severity | Code Fix | Automated Regression | UI Retest | Notes |
|---|---|---|---|---|---|
| QA-NEW-001 | Major | Updated stale Desktop tests after panel removal | PASS (inherited) | N/A | Test-only defect |
| QA-NEW-002 | Major | Corrected Python test path in QA docs | PASS (inherited) | N/A | Doc-only defect |
| QA-NEW-003 | Critical | Added locked-track guards to Delete/Split/Duplicate | PASS (inherited) | PASS via UI (inherited) | Original lock bypass fixed and retested |
| QA-NEW-004 | Major | Updated runbook to accept FFmpeg metadata as FFprobe fallback | PASS (inherited) | N/A | Tooling limitation |
| QA-NEW-005 | Critical | Lock guards across ALL command paths (move, trim, paste, add, transform, effects, text, keyframes, transitions, media intelligence) | PASS (inherited) | PASS | UI retested on QA copy for move, trim, paste, transform, effect, animation, and transition; saved hash unchanged for each locked attempt |
| QA-NEW-006 | Major | Effect-removal undo preserves original stack index | PASS (inherited) | PASS | UI retest restored `brightness,contrast,noise_reduction` after undo and removed `contrast` again after redo |
| QA-NEW-007 | Major | Transition command enforces track/item locks and positive duration | PASS (inherited) | PASS | UI retest normalized zero duration safely, rejected negative duration without broken transition, and ignored locked transition apply |
| QA-NEW-008 | Major | Per-channel animation cache versioning prevents cross-channel invalidation | PASS (inherited) | N/A | Internal allocation/cache defect; no meaningful user-facing UI reproduction exists |
| QA-NEW-009 | Critical | Preview seek slider now routes UIA/keyboard value changes through the same seek path; exact-preview frame stepping uses canonical playhead time | PASS | PASS | Retest Passed with vision-captured frames at 1.0s, 12.5s, and 24.0s |
| QA-NEW-010 | Major | Project-data title placement corrected in both duplicated showcase sequences | N/A | Retest Passed | First and final Rushframe preview/export pairs match; both titles are fully visible and inside safe area |
| QA-NEW-011 | Major (suspected) | No code change | N/A | Not Reproduced - Closed | Two corrected real-dialog exports completed; apparent stall was the modal completion prompt awaiting Yes/No, after which controls restored |

QA-NEW-009 and QA-NEW-010 are retest-passed. QA-NEW-005 through QA-NEW-007 have direct UI retest evidence, and QA-NEW-008 remains UI N/A because the defect is internal allocation/cache behavior with no meaningful user-facing reproduction. QA-NEW-011 was investigated and closed as not reproduced.

Manual blocker update on 2026-07-13: keyframe/Bezier persistence passed with `positionX` evidence before save, after reopen, after post-reopen edit, after undo, and after redo. QA-NEW-010 now has valid first/final in-app preview captures and matching export frames. Two unique-path real-dialog exports completed in the same Rushframe process, decoded completely, and had no configured black/freeze events. Full subjective audio listening remains the only release blocker because this agent cannot hear system audio.
