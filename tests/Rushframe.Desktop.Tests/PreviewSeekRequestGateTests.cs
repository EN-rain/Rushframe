using Rushframe.Desktop.Services;

namespace Rushframe.Desktop.Tests;

public sealed class PreviewSeekRequestGateTests
{
    [Fact]
    public void ShouldSeekFromSliderValueChanged_AllowsAutomationAndKeyboardChanges()
    {
        var gate = new PreviewSeekRequestGate();

        Assert.True(gate.ShouldSeekFromSliderValueChanged());
    }

    [Fact]
    public void ShouldSeekFromSliderValueChanged_SuppressesInternalProgrammaticUpdates()
    {
        var gate = new PreviewSeekRequestGate();
        var observedDuringUpdate = true;

        gate.ApplyProgrammaticValue(() =>
        {
            observedDuringUpdate = gate.ShouldSeekFromSliderValueChanged();
        });

        Assert.False(observedDuringUpdate);
        Assert.True(gate.ShouldSeekFromSliderValueChanged());
    }

    [Fact]
    public void ShouldSeekFromSliderValueChanged_SuppressesPointerDragUntilRelease()
    {
        var gate = new PreviewSeekRequestGate();

        gate.BeginPointerSeek();

        Assert.True(gate.IsPointerSeeking);
        Assert.False(gate.ShouldSeekFromSliderValueChanged());
        Assert.True(gate.CompletePointerSeek());
        Assert.False(gate.IsPointerSeeking);
        Assert.True(gate.ShouldSeekFromSliderValueChanged());
    }

    [Fact]
    public void CompletePointerSeek_IsIdempotentWhenNoPointerSeekIsActive()
    {
        var gate = new PreviewSeekRequestGate();

        Assert.False(gate.CompletePointerSeek());
        Assert.True(gate.ShouldSeekFromSliderValueChanged());
    }
}
