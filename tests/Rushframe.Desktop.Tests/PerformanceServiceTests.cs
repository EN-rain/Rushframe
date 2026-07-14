using Rushframe.Desktop.Services;
using Rushframe.Domain;
using Rushframe.Domain.Serialization;
using Rushframe.Infrastructure;

namespace Rushframe.Desktop.Tests;

public sealed class PerformanceServiceTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"rushframe-performance-tests-{Guid.NewGuid():N}");

    public Task InitializeAsync()
    {
        Directory.CreateDirectory(_root);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
        return Task.CompletedTask;
    }

    [Fact]
    public async Task ProjectSaveCoordinator_CoalescesToLatestRevisionAndWritesConsistentSnapshot()
    {
        var autosave = new AutosaveService(Path.Combine(_root, "autosave"));
        await using var coordinator = new ProjectSaveCoordinator(
            new ProjectRepository(),
            autosave,
            EditorPerformanceTelemetry.Shared);
        coordinator.ConfigureAutosave(enabled: true, TimeSpan.FromHours(1));

        var project = new Project { Name = "Revision zero" };
        coordinator.ResetForProject(project);
        project.Name = "Revision one";
        project.IncrementRevision();
        coordinator.MarkDirty(project);
        project.Name = "Latest revision";
        project.IncrementRevision();
        coordinator.MarkDirty(project);

        await coordinator.FlushAutosaveAsync();

        var autosavePath = Assert.Single(Directory.GetFiles(Path.Combine(_root, "autosave"), "*.autosave"));
        var restored = ProjectSerializer.Deserialize(await File.ReadAllTextAsync(autosavePath));
        Assert.Equal(project.Revision, restored.Revision);
        Assert.Equal("Latest revision", restored.Name);
        Assert.Equal(project.Revision, coordinator.LastAutosavedRevision);
    }

    [Fact]
    public async Task ProjectSaveCoordinator_WaitsForActiveMutationBeforeCapturingSnapshot()
    {
        var autosave = new AutosaveService(Path.Combine(_root, "autosave-mutation"));
        await using var coordinator = new ProjectSaveCoordinator(
            new ProjectRepository(),
            autosave,
            EditorPerformanceTelemetry.Shared);
        coordinator.ConfigureAutosave(enabled: true, TimeSpan.FromHours(1));
        var project = new Project();
        coordinator.ResetForProject(project);

        var mutation = coordinator.BeginMutation();
        project.Name = "Stable after mutation";
        project.IncrementRevision();
        coordinator.MarkDirty(project);
        var flush = coordinator.FlushAutosaveAsync();
        await Task.Delay(40);
        Assert.False(flush.IsCompleted);

        mutation.Dispose();
        await flush;

        var restored = await autosave.LoadMostRecentAsync();
        Assert.NotNull(restored);
        Assert.Equal("Stable after mutation", restored!.Name);
        Assert.Equal(project.Revision, restored.Revision);
    }

    [Fact]
    public async Task ExactPreviewCache_ReusesDeterministicChunkAndRendersOnlyOnce()
    {
        var cache = new ExactPreviewCache(Path.Combine(_root, "preview"));
        var project = new Project();
        var sequence = project.MainSequence!;
        sequence.Tracks.Add(new Track
        {
            Kind = TrackKind.Video,
            Items =
            {
                new TimelineItem
                {
                    Kind = ItemKind.Clip,
                    TimelineStart = MediaTime.Zero,
                    Duration = MediaTime.FromSeconds(20),
                },
            },
        });
        project.IncrementRevision();

        var firstDescription = cache.Describe(project, sequence, 7.2, 960, 540);
        var secondDescription = cache.Describe(project, sequence, 7.9, 960, 540);
        Assert.Equal(firstDescription.Path, secondDescription.Path);
        Assert.Equal(6, firstDescription.StartSeconds, precision: 6);

        var renders = 0;
        async Task Render(string path, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref renders);
            await File.WriteAllBytesAsync(
                path,
                [0, 0, 0, 24, (byte)'f', (byte)'t', (byte)'y', (byte)'p',
                 (byte)'i', (byte)'s', (byte)'o', (byte)'m', 0, 0, 0, 0,
                 (byte)'i', (byte)'s', (byte)'o', (byte)'m', 0, 0, 0, 0],
                cancellationToken);
        }

        var first = await cache.GetOrCreateAsync(firstDescription, Render, CancellationToken.None);
        var second = await cache.GetOrCreateAsync(secondDescription, Render, CancellationToken.None);

        Assert.Equal(first, second);
        Assert.True(File.Exists(first));
        Assert.Equal(1, renders);
    }

    [Fact]
    public async Task ExactPreviewCache_CorruptExistingChunk_IsReplaced()
    {
        var cache = new ExactPreviewCache(Path.Combine(_root, "preview-corrupt"));
        var project = new Project();
        var sequence = project.MainSequence!;
        sequence.Tracks.Add(new Track
        {
            Kind = TrackKind.Video,
            Items =
            {
                new TimelineItem { Kind = ItemKind.Clip, Duration = MediaTime.FromSeconds(8) },
            },
        });
        project.IncrementRevision();
        var chunk = cache.Describe(project, sequence, 1, 960, 540);
        Directory.CreateDirectory(Path.GetDirectoryName(chunk.Path)!);
        await File.WriteAllTextAsync(chunk.Path, "not-an-mp4");
        var renders = 0;

        var result = await cache.GetOrCreateAsync(
            chunk,
            async (path, token) =>
            {
                renders++;
                await File.WriteAllBytesAsync(
                    path,
                    [0, 0, 0, 24, (byte)'f', (byte)'t', (byte)'y', (byte)'p',
                     (byte)'i', (byte)'s', (byte)'o', (byte)'m', 0, 0, 0, 0,
                     (byte)'i', (byte)'s', (byte)'o', (byte)'m', 0, 0, 0, 0],
                    token);
            },
            CancellationToken.None);

        Assert.Equal(1, renders);
        Assert.Equal(24, new FileInfo(result).Length);
    }

    [Fact]
    public void RealtimeRenderPlan_UsesPlayerSafetyThresholdDataAndIntervalQueries()
    {
        var project = new Project();
        var sequence = project.MainSequence!;
        var track = new Track { Kind = TrackKind.Video, Order = 0 };
        for (var index = 0; index < 12; index++)
        {
            var asset = new MediaAsset
            {
                Kind = MediaKind.Video,
                OriginalPath = Path.Combine(_root, $"video-{index}.mp4"),
                Duration = MediaTime.FromSeconds(10),
            };
            project.MediaLibrary.Add(asset);
            track.Items.Add(new TimelineItem
            {
                Kind = ItemKind.Clip,
                MediaAssetId = asset.Id,
                TimelineStart = MediaTime.Zero,
                Duration = MediaTime.FromSeconds(10),
            });
        }
        sequence.Tracks.Add(track);
        project.IncrementRevision();

        var plan = RealtimeRenderPlan.Build(project, sequence);
        var active = new List<RealtimeRenderPlan.VisualEntry>();
        plan.CollectActiveVisuals(5, active);

        Assert.Equal(12, active.Count);
        Assert.Equal(12, plan.MaxConcurrentMediaPlayers);
        Assert.Equal(project.Revision, plan.Revision);
    }
}
