namespace Rushframe.Domain.Editing;

public sealed class UpdateCampaignDescriptionCommand : IAtomicEditCommand
{
    private readonly Project _project;
    private string? _oldDescription;

    public UpdateCampaignDescriptionCommand(Project project, string description)
    {
        _project = project ?? throw new ArgumentNullException(nameof(project));
        NewDescription = description?.Trim() ?? string.Empty;
    }

    public string NewDescription { get; }
    public string Description => "Update campaign description";

    public EditResult Execute(Sequence sequence)
    {
        _oldDescription ??= _project.CampaignDescription;
        _project.CampaignDescription = NewDescription;
        return EditResult.Ok();
    }

    public EditResult Undo(Sequence sequence)
    {
        if (_oldDescription == null) return EditResult.Fail("Campaign description was not updated");
        _project.CampaignDescription = _oldDescription;
        return EditResult.Ok();
    }
}

public sealed class UpdateEditingBriefCommand : IAtomicEditCommand
{
    private readonly Project _project;
    private EditingBrief? _oldBrief;

    public UpdateEditingBriefCommand(Project project, EditingBrief brief)
    {
        _project = project ?? throw new ArgumentNullException(nameof(project));
        NewBrief = brief ?? throw new ArgumentNullException(nameof(brief));
        NewBrief.Normalize();
    }

    public EditingBrief NewBrief { get; }
    public string Description => "Update editing brief";

    public EditResult Execute(Sequence sequence)
    {
        _oldBrief ??= Clone(_project.EditingBrief);
        _project.EditingBrief = Clone(NewBrief);
        return EditResult.Ok();
    }

    public EditResult Undo(Sequence sequence)
    {
        if (_oldBrief == null) return EditResult.Fail("Editing brief was not updated");
        _project.EditingBrief = Clone(_oldBrief);
        return EditResult.Ok();
    }

    private static EditingBrief Clone(EditingBrief source)
    {
        var clone = new EditingBrief
        {
            Purpose = source.Purpose,
            TargetAudience = source.TargetAudience,
            Platform = source.Platform,
            AspectRatio = source.AspectRatio,
            TargetDurationSeconds = source.TargetDurationSeconds,
            Tone = source.Tone,
            EditingStyle = source.EditingStyle,
            Pacing = source.Pacing,
            HookDeadlineSeconds = source.HookDeadlineSeconds,
            CaptionPolicy = source.CaptionPolicy,
            MusicPolicy = source.MusicPolicy,
            SoundEffectsPolicy = source.SoundEffectsPolicy,
            TransitionPolicy = source.TransitionPolicy,
            CallToAction = source.CallToAction,
            LogoPolicy = source.LogoPolicy,
            ReferenceNotes = source.ReferenceNotes,
        };
        clone.RequiredMessages.AddRange(source.RequiredMessages);
        clone.RequiredMediaAssetIds.AddRange(source.RequiredMediaAssetIds);
        clone.ForbiddenMediaAssetIds.AddRange(source.ForbiddenMediaAssetIds);
        clone.BrandColors.AddRange(source.BrandColors);
        clone.BrandFonts.AddRange(source.BrandFonts);
        return clone;
    }
}

public sealed class AddCampaignTaskCommand : IAtomicEditCommand
{
    private readonly Project _project;
    private int _index = -1;

    public AddCampaignTaskCommand(Project project, CampaignTask task)
    {
        _project = project ?? throw new ArgumentNullException(nameof(project));
        Task = task ?? throw new ArgumentNullException(nameof(task));
    }

    public CampaignTask Task { get; }
    public string Description => $"Add campaign task: {Task.Title}";

    public EditResult Execute(Sequence sequence)
    {
        if (string.IsNullOrWhiteSpace(Task.Title)) return EditResult.Fail("Campaign task title is required");
        if (_project.Tasks.Any(candidate => candidate.Id == Task.Id)) return EditResult.Fail("Campaign task already exists");
        _index = Math.Clamp(_index < 0 ? _project.Tasks.Count : _index, 0, _project.Tasks.Count);
        _project.Tasks.Insert(_index, Task);
        return EditResult.Ok();
    }

    public EditResult Undo(Sequence sequence)
    {
        var index = _project.Tasks.FindIndex(candidate => candidate.Id == Task.Id);
        if (index < 0) return EditResult.Fail("Campaign task no longer exists");
        _index = index;
        _project.Tasks.RemoveAt(index);
        return EditResult.Ok();
    }
}

public sealed class UpdateCampaignTaskCommand : IAtomicEditCommand
{
    private readonly Project _project;
    private string? _oldTitle;
    private bool _oldCompleted;
    private bool _captured;

    public UpdateCampaignTaskCommand(Project project, Guid taskId, string? title = null, bool? isCompleted = null)
    {
        _project = project ?? throw new ArgumentNullException(nameof(project));
        TaskId = taskId;
        NewTitle = title?.Trim();
        NewCompleted = isCompleted;
    }

    public Guid TaskId { get; }
    public string? NewTitle { get; }
    public bool? NewCompleted { get; }
    public string Description => "Update campaign task";

    public EditResult Execute(Sequence sequence)
    {
        var task = _project.Tasks.FirstOrDefault(candidate => candidate.Id == TaskId);
        if (task == null) return EditResult.Fail("Campaign task was not found");
        if (NewTitle != null && string.IsNullOrWhiteSpace(NewTitle)) return EditResult.Fail("Campaign task title is required");
        if (!_captured)
        {
            _oldTitle = task.Title;
            _oldCompleted = task.IsCompleted;
            _captured = true;
        }
        if (NewTitle != null) task.Title = NewTitle;
        if (NewCompleted.HasValue) task.IsCompleted = NewCompleted.Value;
        return EditResult.Ok();
    }

    public EditResult Undo(Sequence sequence)
    {
        if (!_captured || _oldTitle == null) return EditResult.Fail("Campaign task was not updated");
        var task = _project.Tasks.FirstOrDefault(candidate => candidate.Id == TaskId);
        if (task == null) return EditResult.Fail("Campaign task was not found");
        task.Title = _oldTitle;
        task.IsCompleted = _oldCompleted;
        return EditResult.Ok();
    }
}

public sealed class DeleteCampaignTaskCommand : IAtomicEditCommand
{
    private readonly Project _project;
    private CampaignTask? _removed;
    private int _index = -1;

    public DeleteCampaignTaskCommand(Project project, Guid taskId)
    {
        _project = project ?? throw new ArgumentNullException(nameof(project));
        TaskId = taskId;
    }

    public Guid TaskId { get; }
    public string Description => "Delete campaign task";

    public EditResult Execute(Sequence sequence)
    {
        var index = _project.Tasks.FindIndex(candidate => candidate.Id == TaskId);
        if (index < 0) return EditResult.Fail("Campaign task was not found");
        _index = index;
        _removed = _project.Tasks[index];
        _project.Tasks.RemoveAt(index);
        return EditResult.Ok();
    }

    public EditResult Undo(Sequence sequence)
    {
        if (_removed == null) return EditResult.Fail("Campaign task was not deleted");
        _project.Tasks.Insert(Math.Clamp(_index, 0, _project.Tasks.Count), _removed);
        _removed = null;
        return EditResult.Ok();
    }
}
