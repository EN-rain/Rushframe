using System.Collections.ObjectModel;

namespace Rushframe.Desktop.Services;

public sealed class VisualProviderCredentialRotationService
{
    private readonly Func<EditorSettings> _settingsAccessor;
    private readonly Action<EditorSettings> _persistSettings;
    private readonly Dictionary<string, SessionSelection> _sessionSelections = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public VisualProviderCredentialRotationService(
        Func<EditorSettings> settingsAccessor,
        Action<EditorSettings> persistSettings)
    {
        _settingsAccessor = settingsAccessor ?? throw new ArgumentNullException(nameof(settingsAccessor));
        _persistSettings = persistSettings ?? throw new ArgumentNullException(nameof(persistSettings));
    }

    public bool TryGetEnvironment(
        string provider,
        out IReadOnlyDictionary<string, string> environment,
        out string error)
    {
        if (!MediaIntelligenceUiPolicy.IsSupportedVisualProvider(provider))
        {
            environment = ReadOnlyDictionary<string, string>.Empty;
            error = $"Unsupported visual provider: {provider}";
            return false;
        }

        var normalized = MediaIntelligenceUiPolicy.NormalizeVisualProvider(provider);

        lock (_gate)
        {
            var settings = _settingsAccessor();
            var candidates = BuildCandidates(settings, normalized);
            if (candidates.Count == 0)
            {
                environment = ReadOnlyDictionary<string, string>.Empty;
                error = normalized switch
                {
                    "cloudflare" => "Cloudflare visual analysis requires at least one account ID and API token pair in Settings.",
                    _ => "Groq visual analysis requires at least one API key in Settings.",
                };
                return false;
            }

            var signature = string.Join("\u001f", candidates.Select(candidate => candidate.Signature));
            if (_sessionSelections.TryGetValue(normalized, out var selected) && selected.Signature == signature)
            {
                environment = selected.Environment;
                error = string.Empty;
                return true;
            }

            settings.AiProviderRotationCursors ??= [];
            settings.AiProviderRotationCursors.TryGetValue(normalized, out var cursor);
            var index = PositiveModulo(cursor, candidates.Count);
            var candidate = candidates[index];
            settings.AiProviderRotationCursors[normalized] = (index + 1) % candidates.Count;
            _persistSettings(settings);

            var readOnlyEnvironment = new ReadOnlyDictionary<string, string>(
                new Dictionary<string, string>(candidate.Environment, StringComparer.Ordinal));
            _sessionSelections[normalized] = new SessionSelection(signature, readOnlyEnvironment);
            environment = readOnlyEnvironment;
            error = string.Empty;
            return true;
        }
    }

    private static List<CredentialCandidate> BuildCandidates(EditorSettings settings, string provider)
    {
        return provider switch
        {
            "groq" => BuildSingleKeyCandidates(
                "GROQ_API_KEY",
                UnprotectDistinct(settings.ProtectedGroqApiKeys),
                settings.ProtectedGroqApiKeys ?? []),
            "cloudflare" => BuildCloudflareCandidates(settings.ProtectedCloudflareCredentials),
            _ => [],
        };
    }

    private static List<CredentialCandidate> BuildSingleKeyCandidates(
        string environmentName,
        IReadOnlyList<string> unprotectedValues,
        IReadOnlyList<string> protectedValues)
    {
        var candidates = new List<CredentialCandidate>();
        for (var index = 0; index < unprotectedValues.Count; index++)
        {
            var value = unprotectedValues[index];
            var signature = index < protectedValues.Count && !string.IsNullOrWhiteSpace(protectedValues[index])
                ? protectedValues[index]
                : value;
            candidates.Add(new CredentialCandidate(
                signature,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [environmentName] = value,
                }));
        }
        return candidates;
    }

    private static List<CredentialCandidate> BuildCloudflareCandidates(
        IReadOnlyList<ProtectedCloudflareCredential>? protectedCredentials)
    {
        var candidates = new List<CredentialCandidate>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var protectedCredential in protectedCredentials ?? [])
        {
            var accountId = SecretProtectionService.Unprotect(protectedCredential.ProtectedAccountId).Trim();
            var token = SecretProtectionService.Unprotect(protectedCredential.ProtectedApiToken).Trim();
            if (accountId.Length == 0 || token.Length == 0) continue;
            var identity = $"{accountId}\u001f{token}";
            if (!seen.Add(identity)) continue;
            candidates.Add(new CredentialCandidate(
                $"{protectedCredential.ProtectedAccountId}\u001f{protectedCredential.ProtectedApiToken}",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["CLOUDFLARE_ACCOUNT_ID"] = accountId,
                    ["CLOUDFLARE_API_TOKEN"] = token,
                    ["CLOUDFLARE_AUTH_TOKEN"] = token,
                }));
        }
        return candidates;
    }

    private static IReadOnlyList<string> UnprotectDistinct(IReadOnlyList<string>? protectedValues)
    {
        var values = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var protectedValue in protectedValues ?? [])
        {
            var value = SecretProtectionService.Unprotect(protectedValue).Trim();
            if (value.Length > 0 && seen.Add(value)) values.Add(value);
        }
        return values;
    }

    private static int PositiveModulo(int value, int divisor)
    {
        var remainder = value % divisor;
        return remainder < 0 ? remainder + divisor : remainder;
    }

    private sealed record CredentialCandidate(string Signature, Dictionary<string, string> Environment);
    private sealed record SessionSelection(string Signature, IReadOnlyDictionary<string, string> Environment);
}
