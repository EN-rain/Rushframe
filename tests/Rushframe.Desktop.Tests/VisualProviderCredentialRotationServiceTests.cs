using Rushframe.Desktop.Services;

namespace Rushframe.Desktop.Tests;

public sealed class VisualProviderCredentialRotationServiceTests
{
    [Fact]
    public void provider_uses_one_key_for_the_entire_service_session()
    {
        var settings = new EditorSettings
        {
            ProtectedGroqApiKeys = ProtectAll("groq-one", "groq-two"),
        };
        var saves = 0;
        var service = CreateService(settings, () => saves++);

        Assert.True(service.TryGetEnvironment("groq", out var first, out var firstError), firstError);
        Assert.True(service.TryGetEnvironment("groq", out var second, out var secondError), secondError);

        Assert.Equal("groq-one", first["GROQ_API_KEY"]);
        Assert.Equal("groq-one", second["GROQ_API_KEY"]);
        Assert.Equal(1, saves);
        Assert.Equal(1, settings.AiProviderRotationCursors["groq"]);
    }

    [Fact]
    public void new_app_session_rotates_and_wraps_provider_keys()
    {
        var settings = new EditorSettings
        {
            ProtectedGroqApiKeys = ProtectAll("groq-one", "groq-two"),
        };

        var firstSession = CreateService(settings);
        Assert.True(firstSession.TryGetEnvironment("groq", out var first, out var firstError), firstError);

        var secondSession = CreateService(settings);
        Assert.True(secondSession.TryGetEnvironment("groq", out var second, out var secondError), secondError);

        var thirdSession = CreateService(settings);
        Assert.True(thirdSession.TryGetEnvironment("groq", out var third, out var thirdError), thirdError);

        Assert.Equal("groq-one", first["GROQ_API_KEY"]);
        Assert.Equal("groq-two", second["GROQ_API_KEY"]);
        Assert.Equal("groq-one", third["GROQ_API_KEY"]);
    }

    [Fact]
    public void cloudflare_rotates_account_and_token_as_one_credential()
    {
        var settings = new EditorSettings
        {
            ProtectedCloudflareCredentials =
            [
                Cloudflare("account-one", "token-one"),
                Cloudflare("account-two", "token-two"),
            ],
        };

        var firstSession = CreateService(settings);
        Assert.True(firstSession.TryGetEnvironment("cloudflare", out var first, out var firstError), firstError);

        var secondSession = CreateService(settings);
        Assert.True(secondSession.TryGetEnvironment("cloudflare", out var second, out var secondError), secondError);

        Assert.Equal("account-one", first["CLOUDFLARE_ACCOUNT_ID"]);
        Assert.Equal("token-one", first["CLOUDFLARE_API_TOKEN"]);
        Assert.Equal("account-two", second["CLOUDFLARE_ACCOUNT_ID"]);
        Assert.Equal("token-two", second["CLOUDFLARE_AUTH_TOKEN"]);
    }

    [Fact]
    public void removed_gemini_provider_is_rejected()
    {
        var service = CreateService(new EditorSettings());

        Assert.False(service.TryGetEnvironment("gemini", out var environment, out var error));
        Assert.Empty(environment);
        Assert.Contains("Unsupported", error, StringComparison.Ordinal);
    }

    [Fact]
    public void remote_provider_without_credentials_is_rejected_without_advancing_cursor()
    {
        var settings = new EditorSettings();
        var service = CreateService(settings);

        Assert.False(service.TryGetEnvironment("groq", out var environment, out var error));

        Assert.Empty(environment);
        Assert.Contains("Groq", error, StringComparison.Ordinal);
        Assert.Empty(settings.AiProviderRotationCursors);
    }

    [Fact]
    public void removed_local_provider_is_rejected()
    {
        var service = CreateService(new EditorSettings());

        Assert.False(service.TryGetEnvironment("qwen", out var environment, out var error));
        Assert.Empty(environment);
        Assert.Contains("Unsupported", error, StringComparison.Ordinal);
    }

    private static VisualProviderCredentialRotationService CreateService(
        EditorSettings settings,
        Action? onSave = null) =>
        new(() => settings, _ => onSave?.Invoke());

    private static List<string> ProtectAll(params string[] values) =>
        values.Select(SecretProtectionService.Protect).ToList();

    private static ProtectedCloudflareCredential Cloudflare(string accountId, string token) => new()
    {
        ProtectedAccountId = SecretProtectionService.Protect(accountId),
        ProtectedApiToken = SecretProtectionService.Protect(token),
    };
}
