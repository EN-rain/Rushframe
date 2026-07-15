using Rushframe.Desktop.Services;
using Rushframe.Domain;

namespace Rushframe.Desktop.Tests;

public sealed class VariantRenderContextServiceTests
{
    [Fact]
    public void portrait_variant_recenters_primary_video_without_mutating_live_project()
    {
        var project = new Project();
        var sequence = project.MainSequence!;
        sequence.Width = 1920;
        sequence.Height = 1080;
        var item = new TimelineItem
        {
            Kind = ItemKind.Clip,
            Duration = MediaTime.FromSeconds(3),
            SourceDuration = MediaTime.FromSeconds(3),
        };
        item.Transform.PositionX = 125;
        item.Transform.PositionY = 420;
        sequence.Tracks.Add(new Track
        {
            Kind = TrackKind.Video,
            Name = "V1",
            Items = { item },
        });
        var variant = new ExportVariant
        {
            Name = "Portrait",
            SequenceId = sequence.Id,
            Width = 1080,
            Height = 1920,
        };

        var (_, renderedSequence) = VariantRenderContextService.Create(project, variant);
        var renderedItem = Assert.Single(Assert.Single(renderedSequence.Tracks).Items);

        Assert.Equal(0, renderedItem.Transform.PositionX);
        Assert.Equal(0, renderedItem.Transform.PositionY);
        Assert.Equal(125, item.Transform.PositionX);
        Assert.Equal(420, item.Transform.PositionY);
    }

    [Fact]
    public void portrait_variant_respects_explicit_position_override()
    {
        var project = new Project();
        var sequence = project.MainSequence!;
        sequence.Width = 1920;
        sequence.Height = 1080;
        var item = new TimelineItem
        {
            Kind = ItemKind.Clip,
            Duration = MediaTime.FromSeconds(3),
            SourceDuration = MediaTime.FromSeconds(3),
        };
        item.Transform.PositionY = 420;
        sequence.Tracks.Add(new Track
        {
            Kind = TrackKind.Video,
            Name = "V1",
            Items = { item },
        });
        var variant = new ExportVariant
        {
            SequenceId = sequence.Id,
            Width = 1080,
            Height = 1920,
            ItemOverrides =
            {
                new VariantItemOverride
                {
                    ItemId = item.Id,
                    PositionX = 25,
                    PositionY = -120,
                },
            },
        };

        var (_, renderedSequence) = VariantRenderContextService.Create(project, variant);
        var renderedItem = Assert.Single(Assert.Single(renderedSequence.Tracks).Items);

        Assert.Equal(25, renderedItem.Transform.PositionX);
        Assert.Equal(-120, renderedItem.Transform.PositionY);
    }

    [Fact]
    public void manual_and_agent_timeline_exports_apply_portrait_centering_to_snapshots()
    {
        var root = FindRepositoryRoot();
        var controller = File.ReadAllText(Path.Combine(root, "src", "Rushframe.Desktop", "Controllers", "ExportController.cs"));
        var window = File.ReadAllText(Path.Combine(root, "src", "Rushframe.Desktop", "MainWindow.xaml.cs"));

        Assert.Contains("CenterPrimaryVideoForPortrait", controller, StringComparison.Ordinal);
        Assert.Contains("CenterPrimaryVideoForPortrait", window, StringComparison.Ordinal);
        Assert.Contains("renderSequence", controller, StringComparison.Ordinal);
        Assert.Contains("renderSequence", window, StringComparison.Ordinal);
    }

    [Fact]
    public void portrait_auto_center_can_be_disabled_for_authored_layouts()
    {
        var project = new Project();
        var sequence = project.MainSequence!;
        sequence.Width = 1920;
        sequence.Height = 1080;
        var item = new TimelineItem
        {
            Kind = ItemKind.Clip,
            Duration = MediaTime.FromSeconds(3),
            SourceDuration = MediaTime.FromSeconds(3),
        };
        item.Transform.PositionY = 300;
        sequence.Tracks.Add(new Track
        {
            Kind = TrackKind.Video,
            Name = "V1",
            Items = { item },
        });
        var variant = new ExportVariant
        {
            SequenceId = sequence.Id,
            Width = 1080,
            Height = 1920,
            Overrides = { ["autoCenterPortrait"] = "false" },
        };

        var (_, renderedSequence) = VariantRenderContextService.Create(project, variant);

        Assert.Equal(300, Assert.Single(Assert.Single(renderedSequence.Tracks).Items).Transform.PositionY);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Rushframe.slnx"))) return directory.FullName;
        }
        throw new DirectoryNotFoundException("Could not locate the Rushframe repository root.");
    }
}
