namespace Rushframe.Domain;

public static class TrackOrdering
{
    public static void Normalize(Sequence sequence)
    {
        ArgumentNullException.ThrowIfNull(sequence);
        for (var index = 0; index < sequence.Tracks.Count; index++)
            sequence.Tracks[index].Order = index;
    }
}
