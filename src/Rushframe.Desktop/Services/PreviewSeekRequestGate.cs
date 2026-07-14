namespace Rushframe.Desktop.Services;

public sealed class PreviewSeekRequestGate
{
    private bool _isPointerSeeking;
    private bool _isApplyingProgrammaticValue;

    public bool IsPointerSeeking => _isPointerSeeking;

    public void BeginPointerSeek() => _isPointerSeeking = true;

    public bool CompletePointerSeek()
    {
        if (!_isPointerSeeking) return false;
        _isPointerSeeking = false;
        return true;
    }

    public bool ShouldSeekFromSliderValueChanged() =>
        !_isPointerSeeking && !_isApplyingProgrammaticValue;

    public void ApplyProgrammaticValue(Action update)
    {
        ArgumentNullException.ThrowIfNull(update);

        _isApplyingProgrammaticValue = true;
        try
        {
            update();
        }
        finally
        {
            _isApplyingProgrammaticValue = false;
        }
    }
}
