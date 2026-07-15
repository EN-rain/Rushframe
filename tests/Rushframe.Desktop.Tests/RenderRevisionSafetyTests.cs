using Rushframe.Desktop.Services;
using Rushframe.Domain;
using Rushframe.Domain.Serialization;

namespace Rushframe.Desktop.Tests;

public sealed class RenderRevisionSafetyTests
{
    [Fact]
    public void receipt_commit_keeps_newer_live_edits_and_records_snapshot_revision()
    {
        var project = new Project { Revision = 9, Name = "Live project" };
        var sequence = project.MainSequence!;
        var track = new Track { Kind = TrackKind.Text, Name = "T1" };
        var item = new TimelineItem
        {
            Kind = ItemKind.Text,
            TextContent = "revision nine",
            Duration = MediaTime.FromSeconds(2),
            SourceDuration = MediaTime.FromSeconds(2),
        };
        track.Items.Add(item);
        sequence.Tracks.Add(track);
        var variant = new ExportVariant
        {
            Name = "Primary",
            SequenceId = sequence.Id,
            Width = 720,
            Height = 1280,
        };
        project.ExportVariants.Clear();
        project.ExportVariants.Add(variant);

        var renderProject = ProjectSerializer.CreateSnapshot(project);
        var renderSequence = renderProject.Sequences.Single(candidate => candidate.Id == sequence.Id);
        var renderedItem = renderSequence.Tracks.Single().Items.Single();

        item.TextContent = "newer revision ten";
        project.IncrementRevision();
        var liveRevision = project.Revision;

        var receipt = new RenderReceiptDocument
        {
            ReceiptId = "receipt-r9",
            ReceiptPath = @"C:\renders\revision-nine.mp4.rushframe-receipt.json",
            ProjectId = renderProject.Id.ToString(),
            ProjectName = renderProject.Name,
            ProjectRevision = renderProject.Revision,
            ProjectGraphSha256 = "graph-r9",
            SequenceId = renderSequence.Id.ToString(),
            SequenceName = renderSequence.Name,
            VariantId = variant.Id,
            Status = RenderVerificationStatus.Passed,
            Output = new RenderOutputRecord
            {
                Path = @"C:\renders\revision-nine.mp4",
                Sha256 = "output-r9",
                SizeBytes = 1234,
                Width = 720,
                Height = 1280,
                DurationSeconds = 2,
                Format = "Mp4",
                Quality = "High",
            },
        };

        RenderReceiptService.ApplyToProject(project, receipt);

        Assert.Equal("revision nine", renderedItem.TextContent);
        Assert.Equal("newer revision ten", item.TextContent);
        Assert.Equal(liveRevision, project.Revision);
        var reference = Assert.Single(project.RenderReceipts);
        Assert.Equal(9, reference.ProjectRevision);
        Assert.Equal("output-r9", reference.OutputSha256);
        Assert.Equal(ExportVariantStatus.Completed, variant.Status);
        Assert.Equal(receipt.Output.Path, variant.LastOutputPath);
        Assert.Equal(receipt.ReceiptPath, variant.LastReceiptPath);
        var exportStage = project.Workflow.Stages.Single(stage => stage.Id == "export");
        Assert.Equal(ProductionStageStatus.Completed, exportStage.Status);
        Assert.Equal(receipt.CreatedUtc, exportStage.ApprovedUtc);
        Assert.Equal(receipt.ApprovalSource, exportStage.ApprovedBy);
    }
}
