# Local Agent Instructions

You are working on the Rushframe repository:

`C:\Users\LENOVO\Desktop\Projectsss\Rushframe`

Before doing any work:

1. Read `AGENT_CONTEXT.md`.
2. Read `LOCAL_AGENT_INSTRUCTIONS.md`.
3. Run `git status --short`.
4. Treat the current filesystem as the source of truth.
5. Preserve all unrelated modified, deleted, and untracked files.
6. Never run `git reset --hard`, `git clean`, mass formatting, or commands that discard existing work.

Rushframe is a local Windows video editor using .NET 10, WPF, FFmpeg, optional native C++, and an optional local Python media-intelligence pipeline.

Preserve these product rules:

* Manual video, audio, music, image, logo, and asset upload is required.
* Do not add social URL imports, downloaders, scraping, or arbitrary website-download behavior.
* Work only with locally registered project media.
* Never silently modify original source files.
* Manual user edits win by default.
* Agent edits must be revision-safe, authorized, auditable, undoable, and reviewable.
* Clipster is a separate monetization platform, not the Rushframe editor.
* Do not move the prompt or agent brain into the editor UI.
* Do not execute arbitrary extension code.
* Do not introduce cloud requirements for core editing.

Editing rules:

* Use `IEditCommand` implementations for timeline mutations.
* Use `CompositeEditCommand` for one logical multi-property operation.
* Reuse `TimelineItemCloner` rather than manually copying clips.
* Keep WPF out of the domain layer.
* Keep filesystem and process concerns out of domain models.
* Do not write or serialize the full project on the WPF UI thread.
* Use `ProjectSaveCoordinator.BeginMutation()` for overlapping mutations.
* Preserve project revisions, undo/redo, autosave, migration, and recovery behavior.

Locked tracks and locked items are strict data-integrity boundaries. Every mutating command must reject locked content before making any change. This includes add, insert, overwrite, move, trim, split, delete, ripple delete, duplicate, paste, transforms, inspector properties, effects, text, keyframes, transitions, generated captions, and agent-driven changes.

A rejected operation must:

* return a failed result;
* leave all project state unchanged;
* avoid partial mutations;
* not enter undo history.

For every defect:

1. Reproduce it.
2. Record it in the current defect log before editing.
3. Use the next `QA-NEW-###` identifier.
4. Apply the smallest correct fix.
5. Add a deterministic regression test.
6. Run targeted tests.
7. Run the full affected suite.
8. Retest through the original reproduction path.
9. Update the defect status honestly.

Required automated gates:

```powershell
dotnet build Rushframe.slnx
dotnet test Rushframe.slnx
python -m pytest tests\test_media_intelligence_v2.py -q

dotnet build Rushframe.slnx -c Release
dotnet test Rushframe.slnx -c Release --no-build

dotnet list Rushframe.slnx package --vulnerable --include-transitive
```

Current expected baseline:

* zero build errors;
* zero new warnings;
* 185 C# tests passing;
* 5 Python tests passing.

For performance-sensitive changes, also run:

```powershell
powershell -ExecutionPolicy Bypass -File ".\qa testing\performance\Smoke-Startup.ps1"

dotnet run --project .\benchmarks\Rushframe.Benchmarks\Rushframe.Benchmarks.csproj -c Release -- --filter "*"
```

Do not claim a performance improvement without comparable before-and-after measurements.

The automated code audit is already complete. The next QA priority is the missing real-editor evidence:

* open the showcase project through Rushframe;
* test all locked operations through actual UI paths;
* verify keyframes and Bezier animation;
* save, close, reopen, and verify persistence;
* perform a post-reopen edit with undo and redo;
* export through the real Rushframe export dialog;
* capture matching preview and export frames;
* listen to the complete export externally;
* complete evidence-based creative scoring.

Store evidence under:

`qa testing\manual review\showcase-edit\`

Do not mark the release PASS until every mandatory visual, audio, persistence, preview/export, and export-dialog requirement has actual evidence.

End every task with:

* files inspected;
* files changed;
* defects fixed;
* exact test commands and counts;
* build configuration;
* startup, benchmark, and export results when relevant;
* remaining manual checks;
* known risks;
* final decision: PASS, FAIL, or BLOCKED.
