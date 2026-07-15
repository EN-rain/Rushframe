using Rushframe.Infrastructure;

namespace Rushframe.Desktop.Services;

public static class LocalOutputPathGuard
{
    public static string Resolve(string allowedDirectory, string requestedPath)
    {
        try
        {
            return LocalPhysicalPathGuard.ResolveContainedOutput(allowedDirectory, requestedPath);
        }
        catch (InvalidOperationException ex)
        {
            if (ex.Message.Contains("network", StringComparison.OrdinalIgnoreCase)
                || ex.Message.Contains("local drive", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Agent render output must use a local drive.", ex);
            if (ex.Message.Contains("filesystem link", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Agent render output escapes the project directory through a filesystem link.", ex);
            throw new InvalidOperationException("Agent render output must stay inside the saved Rushframe project directory.", ex);
        }
    }
}
