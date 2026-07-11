namespace Rushframe.Domain;

public sealed class Sequence
{
    public SequenceId Id { get; init; } = SequenceId.New();
    public string Name { get; set; } = "Main";
    public int Width { get; set; } = 1080;
    public int Height { get; set; } = 1920;
    public double Fps { get; set; } = 30.0;
    public List<Track> Tracks { get; init; } = [];
    public List<Marker> Markers { get; init; } = [];
    public List<Transition> Transitions { get; init; } = [];

    public MediaTime Duration
    {
        get
        {
            var max = MediaTime.Zero;
            foreach (var track in Tracks)
            foreach (var item in track.Items)
            {
                var end = item.TimelineStart.Add(item.Duration);
                if (end > max) max = end;
            }
            return max;
        }
    }
}
