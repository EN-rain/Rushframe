using Rushframe.Infrastructure;

namespace Rushframe.Domain.Tests;

public sealed class RecoveryPersistenceTests
{
    [Fact]
    public void ProjectRepository_SaveLoad_RoundTripsProject()
    {
        var root = Path.Combine(Path.GetTempPath(), $"rushframe-repo-{Guid.NewGuid():N}");
        try
        {
            var project = new Project
            {
                Name = "QA Project",
                CampaignDescription = "Create a launch edit for the summer campaign.",
            };
            project.Tasks.Add(new CampaignTask { Title = "Select hero clips", IsCompleted = true });
            var path = Path.Combine(root, "qa.rushframe");
            var repo = new ProjectRepository();

            repo.Save(project, path);
            var loaded = repo.Load(path);

            Assert.NotNull(loaded);
            Assert.Equal(project.Id, loaded.Id);
            Assert.Equal("QA Project", loaded.Name);
            Assert.Equal(project.CampaignDescription, loaded.CampaignDescription);
            Assert.Single(loaded.Tasks);
            Assert.Equal("Select hero clips", loaded.Tasks[0].Title);
            Assert.True(loaded.Tasks[0].IsCompleted);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void AutosaveService_LoadMostRecent_ReturnsNewestAutosave()
    {
        var root = Path.Combine(Path.GetTempPath(), $"rushframe-autosave-{Guid.NewGuid():N}");
        try
        {
            var service = new AutosaveService(root);
            service.Save(new Project { Name = "Older" });
            Thread.Sleep(20);
            service.Save(new Project { Name = "Newer" });

            var loaded = service.LoadMostRecent();

            Assert.NotNull(loaded);
            Assert.Equal("Newer", loaded.Name);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
