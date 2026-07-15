namespace Rushframe.Domain.Editing;

public sealed class AddProjectMediaAssetCommand : IAtomicEditCommand
{
    private readonly Project _project;
    private int _index = -1;

    public AddProjectMediaAssetCommand(Project project, MediaAsset asset)
    {
        _project = project ?? throw new ArgumentNullException(nameof(project));
        Asset = asset ?? throw new ArgumentNullException(nameof(asset));
    }

    public MediaAsset Asset { get; }
    public string Description => $"Register media asset {Asset.Id}";

    public EditResult Execute(Sequence sequence)
    {
        if (_project.MediaLibrary.Any(candidate => candidate.Id == Asset.Id))
            return EditResult.Fail($"Media asset {Asset.Id} is already registered");
        _index = Math.Clamp(_index < 0 ? _project.MediaLibrary.Count : _index, 0, _project.MediaLibrary.Count);
        _project.MediaLibrary.Insert(_index, Asset);
        return EditResult.Ok();
    }

    public EditResult Undo(Sequence sequence)
    {
        var index = _project.MediaLibrary.FindIndex(candidate => candidate.Id == Asset.Id);
        if (index < 0) return EditResult.Fail($"Media asset {Asset.Id} is not registered");
        _index = index;
        _project.MediaLibrary.RemoveAt(index);
        return EditResult.Ok();
    }
}

public sealed class UpdateProjectMediaLicenseCommand : IAtomicEditCommand
{
    private readonly Project _project;
    private bool _captured;
    private string _oldLicenseName = string.Empty;
    private string _oldAttribution = string.Empty;
    private bool _oldRequiresAttribution;

    public UpdateProjectMediaLicenseCommand(
        Project project,
        MediaAssetId assetId,
        string licenseName,
        string attribution,
        bool requiresAttribution)
    {
        _project = project ?? throw new ArgumentNullException(nameof(project));
        AssetId = assetId;
        LicenseName = licenseName?.Trim() ?? string.Empty;
        Attribution = attribution?.Trim() ?? string.Empty;
        RequiresAttribution = requiresAttribution;
    }

    public MediaAssetId AssetId { get; }
    public string LicenseName { get; }
    public string Attribution { get; }
    public bool RequiresAttribution { get; }
    public string Description => $"Update sound license {AssetId}";

    public EditResult Execute(Sequence sequence)
    {
        var asset = _project.MediaLibrary.FirstOrDefault(candidate => candidate.Id == AssetId);
        if (asset == null) return EditResult.Fail($"Media asset {AssetId} is not registered");
        if (!_captured)
        {
            _oldLicenseName = asset.LicenseName;
            _oldAttribution = asset.Attribution;
            _oldRequiresAttribution = asset.RequiresAttribution;
            _captured = true;
        }
        asset.LicenseName = LicenseName;
        asset.Attribution = Attribution;
        asset.RequiresAttribution = RequiresAttribution;
        return EditResult.Ok();
    }

    public EditResult Undo(Sequence sequence)
    {
        if (!_captured) return EditResult.Fail("Sound license metadata was not updated");
        var asset = _project.MediaLibrary.FirstOrDefault(candidate => candidate.Id == AssetId);
        if (asset == null) return EditResult.Fail($"Media asset {AssetId} is not registered");
        asset.LicenseName = _oldLicenseName;
        asset.Attribution = _oldAttribution;
        asset.RequiresAttribution = _oldRequiresAttribution;
        return EditResult.Ok();
    }
}
