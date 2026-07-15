using System.Windows;
using System.Windows.Controls;

namespace Rushframe.Desktop.Dialogs;

internal sealed class ApiKeyListEditor : StackPanel
{
    private readonly StackPanel _rows = new();
    private readonly List<PasswordBox> _boxes = [];

    public ApiKeyListEditor(IEnumerable<string> initialValues, string addButtonText)
    {
        Children.Add(_rows);
        var addButton = new Button
        {
            Content = addButtonText,
            HorizontalAlignment = HorizontalAlignment.Left,
            MinWidth = 100,
            Margin = new Thickness(0, 6, 0, 0),
        };
        addButton.Click += (_, _) => AddRow(string.Empty);
        Children.Add(addButton);

        foreach (var value in initialValues.Where(value => !string.IsNullOrWhiteSpace(value)))
            AddRow(value);
        if (_boxes.Count == 0) AddRow(string.Empty);
    }

    public IReadOnlyList<string> GetValues() => _boxes
        .Select(box => box.Password.Trim())
        .Where(value => value.Length > 0)
        .Distinct(StringComparer.Ordinal)
        .ToArray();

    private void AddRow(string value)
    {
        var row = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var box = new PasswordBox
        {
            Password = value,
            MinHeight = 32,
            ToolTip = "API key stored encrypted for the current Windows account",
        };
        var removeButton = new Button
        {
            Content = "Remove",
            MinWidth = 68,
            Margin = new Thickness(6, 0, 0, 0),
        };
        removeButton.Click += (_, _) =>
        {
            _boxes.Remove(box);
            _rows.Children.Remove(row);
            if (_boxes.Count == 0) AddRow(string.Empty);
        };

        Grid.SetColumn(removeButton, 1);
        row.Children.Add(box);
        row.Children.Add(removeButton);
        _boxes.Add(box);
        _rows.Children.Add(row);
    }
}

internal sealed class CloudflareCredentialListEditor : StackPanel
{
    private readonly StackPanel _rows = new();
    private readonly List<CloudflareCredentialRow> _credentialRows = [];

    public CloudflareCredentialListEditor(IEnumerable<CloudflareCredentialInput> initialValues)
    {
        Children.Add(_rows);
        var addButton = new Button
        {
            Content = "Add Cloudflare credential",
            HorizontalAlignment = HorizontalAlignment.Left,
            MinWidth = 170,
            Margin = new Thickness(0, 6, 0, 0),
        };
        addButton.Click += (_, _) => AddRow(new CloudflareCredentialInput(string.Empty, string.Empty));
        Children.Add(addButton);

        foreach (var value in initialValues.Where(value =>
                     !string.IsNullOrWhiteSpace(value.AccountId) || !string.IsNullOrWhiteSpace(value.ApiToken)))
            AddRow(value);
        if (_credentialRows.Count == 0)
            AddRow(new CloudflareCredentialInput(string.Empty, string.Empty));
    }

    public IReadOnlyList<CloudflareCredentialInput> GetValues()
    {
        var values = new List<CloudflareCredentialInput>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in _credentialRows)
        {
            var accountId = row.AccountId.Text.Trim();
            var token = row.ApiToken.Password.Trim();
            if (accountId.Length == 0 && token.Length == 0) continue;
            if (accountId.Length == 0 || token.Length == 0)
                throw new InvalidOperationException("Every Cloudflare credential requires both an account ID and API token.");
            var identity = $"{accountId}\u001f{token}";
            if (seen.Add(identity)) values.Add(new CloudflareCredentialInput(accountId, token));
        }
        return values;
    }

    private void AddRow(CloudflareCredentialInput value)
    {
        var container = new Grid { Margin = new Thickness(0, 5, 0, 0) };
        container.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.9, GridUnitType.Star) });
        container.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });
        container.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var accountId = new TextBox
        {
            Text = value.AccountId,
            MinHeight = 32,
            ToolTip = "Cloudflare account ID",
        };
        var apiToken = new PasswordBox
        {
            Password = value.ApiToken,
            MinHeight = 32,
            Margin = new Thickness(6, 0, 0, 0),
            ToolTip = "Cloudflare Workers AI API token",
        };
        var removeButton = new Button
        {
            Content = "Remove",
            MinWidth = 68,
            Margin = new Thickness(6, 0, 0, 0),
        };
        var row = new CloudflareCredentialRow(accountId, apiToken, container);
        removeButton.Click += (_, _) =>
        {
            _credentialRows.Remove(row);
            _rows.Children.Remove(container);
            if (_credentialRows.Count == 0)
                AddRow(new CloudflareCredentialInput(string.Empty, string.Empty));
        };

        Grid.SetColumn(apiToken, 1);
        Grid.SetColumn(removeButton, 2);
        container.Children.Add(accountId);
        container.Children.Add(apiToken);
        container.Children.Add(removeButton);
        _credentialRows.Add(row);
        _rows.Children.Add(container);
    }

    private sealed record CloudflareCredentialRow(TextBox AccountId, PasswordBox ApiToken, Grid Container);
}

internal sealed record CloudflareCredentialInput(string AccountId, string ApiToken);
