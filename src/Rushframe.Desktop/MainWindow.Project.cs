using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using Rushframe.Domain;
using Rushframe.Domain.Editing;

namespace Rushframe.Desktop;

public partial class MainWindow
{
    private readonly record struct ProjectOperationContext(Project Project, ProjectId ProjectId, long Generation);

    private ProjectOperationContext CaptureProjectOperationContext() =>
        new(_project, _project.Id, _projectGeneration);

    private bool IsCurrentProjectOperation(ProjectOperationContext context) =>
        context.Generation == _projectGeneration
        && context.ProjectId == _project.Id
        && ReferenceEquals(context.Project, _project);

    private bool HasActiveProjectOperation() =>
        _isMediaOperationRunning
        || _project.RenderJobs.Any(job => job.Status is RenderJobStatus.Rendering or RenderJobStatus.Verifying);

    private void ProjectReplacement_CanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = !HasActiveProjectOperation();
        e.Handled = true;
    }

    private async Task<bool> ConfirmCanReplaceCurrentProjectAsync()
    {
        if (HasActiveProjectOperation())
        {
            MessageBox.Show(
                this,
                "Wait for the active render to finish or cancel it before opening another project.",
                "Render In Progress",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }

        if (!TryResolvePendingInspectorChanges()) return false;
        if (!_projectDirty) return true;

        var answer = ShowUnsavedChangesDialog(
            "Save changes to the current project before opening another project?",
            "Open Project");

        if (answer == MessageBoxResult.Cancel) return false;
        if (answer == MessageBoxResult.Yes && !await SaveCurrentProjectAsync()) return false;
        if (HasActiveProjectOperation())
        {
            MessageBox.Show(
                this,
                "A render or media operation started while the project was being saved. Cancel or finish it before replacing the project.",
                "Operation In Progress",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }
        return true;
    }

    private async void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose) return;
        if (_inspectorDirty)
        {
            e.Cancel = true;
            if (!TryResolvePendingInspectorChanges()) return;
            if (!_projectDirty)
            {
                QueueAllowedClose();
                return;
            }
        }
        if (!_projectDirty) return;
        e.Cancel = true;
        if (_isClosingSaveInProgress) return;

        var answer = ShowUnsavedChangesDialog(
            "Save changes to the current project before closing?",
            "Close Rushframe");

        if (answer == MessageBoxResult.Cancel) return;
        if (answer == MessageBoxResult.No)
        {
            QueueAllowedClose();
            return;
        }

        _isClosingSaveInProgress = true;
        try
        {
            if (!await SaveCurrentProjectAsync()) return;
            QueueAllowedClose();
        }
        finally
        {
            _isClosingSaveInProgress = false;
        }
    }

    private void QueueAllowedClose()
    {
        if (_allowClose) return;
        _allowClose = true;
        Dispatcher.BeginInvoke(() =>
        {
            if (IsLoaded)
                Close();
        }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    private MessageBoxResult ShowUnsavedChangesDialog(string message, string primaryAction)
    {
        var dialog = CreateOwnedDialog(
            "Unsaved Changes",
            width: 520,
            height: 205,
            minimumWidth: 460,
            minimumHeight: 200,
            resizeMode: ResizeMode.NoResize);

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var body = new Grid { Margin = new Thickness(0, 0, 0, 22) };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var iconShell = new Border
        {
            Width = 42,
            Height = 42,
            CornerRadius = new CornerRadius(9),
            Background = (Brush)FindResource("AccentSurfaceBrush"),
            BorderBrush = (Brush)FindResource("AccentBrush"),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 1, 16, 0),
            Child = new TextBlock
            {
                Text = "!",
                Foreground = (Brush)FindResource("AccentHoverBrush"),
                FontSize = 24,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        body.Children.Add(iconShell);

        var textStack = new StackPanel();
        textStack.Children.Add(new TextBlock
        {
            Text = message,
            Foreground = (Brush)FindResource("TextBrush"),
            FontSize = 13.5,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        });
        textStack.Children.Add(new TextBlock
        {
            Text = "Your latest edits are not saved to a project file yet.",
            Foreground = (Brush)FindResource("TextMutedBrush"),
            FontSize = 12,
            Margin = new Thickness(0, 7, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        });
        Grid.SetColumn(textStack, 1);
        body.Children.Add(textStack);
        root.Children.Add(body);

        var result = MessageBoxResult.Cancel;
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        Grid.SetRow(actions, 1);
        var save = new Button { Content = "Save", MinWidth = 86, Height = 34, Style = (Style)FindResource("PrimaryButtonStyle"), Margin = new Thickness(0, 0, 8, 0) };
        var discard = new Button { Content = "Don't Save", MinWidth = 104, Height = 34, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", MinWidth = 86, Height = 34, IsCancel = true };
        save.Click += (_, _) => { result = MessageBoxResult.Yes; dialog.DialogResult = true; };
        discard.Click += (_, _) => { result = MessageBoxResult.No; dialog.DialogResult = true; };
        cancel.Click += (_, _) => { result = MessageBoxResult.Cancel; dialog.DialogResult = false; };
        actions.Children.Add(save);
        actions.Children.Add(discard);
        actions.Children.Add(cancel);
        root.Children.Add(actions);

        dialog.Content = CreateDialogFrame(dialog, primaryAction, root, new Thickness(18));
        return dialog.ShowDialog() == true ? result : MessageBoxResult.Cancel;
    }

    private void MarkProjectDirty(string status)
    {
        _projectDirty = true;
        _timelinePreviewDirty = true;
        if (_settings.AutosaveEnabled) _saveCoordinator.MarkDirty(_project);
        StatusText.Text = status;
    }

    private async Task<bool> SaveCurrentProjectAsync()
    {
        if (!TryResolvePendingInspectorChanges()) return false;
        var path = _currentProjectPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            var dialog = new SaveFileDialog
            {
                Filter = "Rushframe Project (*.rushframe)|*.rushframe",
                FileName = _project.Name + ".rushframe",
            };
            if (dialog.ShowDialog() != true) return false;
            path = dialog.FileName;
        }

        try
        {
            StatusText.Text = "Saving project…";
            var savedRevision = await _saveCoordinator.SaveExplicitAsync(_project, path);
            _currentProjectPath = path;
            _recentProjectsService.Add(path);
            ProjectNameText.Text = string.IsNullOrWhiteSpace(_project.Name) ? "Untitled" : _project.Name;
            ProjectNameEditBox.Text = ProjectNameText.Text;
            if (_project.Revision == savedRevision)
            {
                _projectDirty = false;
                StatusText.Text = "Project saved";
            }
            else
            {
                _projectDirty = true;
                _saveCoordinator.MarkDirty(_project);
                StatusText.Text = $"Revision {savedRevision} saved; newer edits remain unsaved";
            }
            return true;
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Project save failed: {ex.Message}";
            return false;
        }
    }

    private bool Execute(IEditCommand cmd, bool resolvePendingInspectorChanges = true)
    {
        if (resolvePendingInspectorChanges && !TryResolvePendingInspectorChanges()) return false;
        var sequence = _project.MainSequence;
        if (sequence == null) return false;

        try
        {
            using var mutation = _saveCoordinator.BeginMutation();
            var inspectorFingerprint = ComputeInspectorFingerprint(_selectedInspectorItem);
            var result = _undoRedo.Execute(sequence, cmd);
            if (!result.Success)
            {
                StatusText.Text = result.ErrorMessage ?? "The edit could not be applied.";
                return false;
            }

            if (_selectedInspectorItem != null
                && !sequence.Tracks.SelectMany(track => track.Items).Any(item => item.Id == _selectedInspectorItem.Id))
            {
                _selectedInspectorItem = null;
                _selectedTransitionSelection = null;
                _timeline?.ClearSelection();
            }

            _project.IncrementRevision();
            if (_timeline != null) _timeline.ProjectRevision = _project.Revision;
            if (_selectedTransitionSelection != null)
                RefreshTransitionInspectorAfterEdit(sequence);
            else if (inspectorFingerprint != ComputeInspectorFingerprint(_selectedInspectorItem))
                UpdateInspector(_selectedInspectorItem);
            MarkProjectDirty("Project modified");
            CommandManager.InvalidateRequerySuggested();
            return true;
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Edit failed: {ex.Message}";
            _timeline?.InvalidateVisual();
            return false;
        }
    }

    private void LoadProjectIntoEditor(Project project, string? projectPath, string displayName)
    {
        _projectGeneration = checked(_projectGeneration + 1);
        _project = project;
        _currentProjectPath = projectPath;
        _undoRedo.Clear();
        _clipboard = null;
        _groupClipboard = null;
        _selectedInspectorItem = null;
        _selectedTransitionSelection = null;
        _contextTrackIndex = -1;
        _lastSelectedTrackIndex = 0;
        _projectDirty = false;
        _timelinePreviewDirty = true;
        _isTimelineCompositePreview = false;
        _saveCoordinator.ResetForProject(_project);
        RebuildMediaIndexes();
        if (_timeline != null)
        {
            _timeline.Sequence = _project.MainSequence;
            _timeline.ProjectRevision = _project.Revision;
        }
        ProjectNameText.Text = !string.IsNullOrWhiteSpace(_project.Name)
            ? _project.Name
            : string.IsNullOrWhiteSpace(displayName) ? "Untitled" : displayName;
        ProjectNameEditBox.Text = ProjectNameText.Text;
        ProjectNameEditBox.Visibility = Visibility.Collapsed;
        ProjectNameText.Visibility = Visibility.Visible;
        ClearPreviewSurface("Nothing selected");
        RefreshMediaList();
        RefreshAutomationPanels();
        UpdateInspector(null);
        RestartAutosave();
        CommandManager.InvalidateRequerySuggested();
    }

    private async void NewProject_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (!await ConfirmCanReplaceCurrentProjectAsync()) return;

        LoadProjectIntoEditor(new Project(), null, "Untitled");
        StatusText.Text = "New project created";
    }

    private async void OpenProject_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Rushframe Project (*.rushframe)|*.rushframe|Legacy Project (*/project.json)|project.json" };
        if (dialog.ShowDialog() != true) return;
        await OpenProjectPathAsync(dialog.FileName);
    }

    private void PopulateOpenRecentMenu()
    {
        OpenRecentMenu.Items.Clear();
        var paths = _recentProjectsService.Load();
        if (paths.Count == 0)
        {
            OpenRecentMenu.Items.Add(new MenuItem { Header = "No recent projects", IsEnabled = false });
            return;
        }

        foreach (var path in paths)
        {
            var exists = File.Exists(path);
            var item = new MenuItem
            {
                Header = Path.GetFileNameWithoutExtension(path),
                ToolTip = exists ? path : $"Missing: {path}",
                IsEnabled = exists,
            };
            item.Click += async (_, _) => await OpenProjectPathAsync(path);
            OpenRecentMenu.Items.Add(item);
        }

        OpenRecentMenu.Items.Add(new Separator());
        var clear = new MenuItem { Header = "Clear Recent Projects" };
        clear.Click += (_, _) =>
        {
            _recentProjectsService.Clear();
            PopulateOpenRecentMenu();
        };
        OpenRecentMenu.Items.Add(clear);
    }

    private async Task OpenProjectPathAsync(string path)
    {
        if (!File.Exists(path))
        {
            _recentProjectsService.Remove(path);
            PopulateOpenRecentMenu();
            StatusText.Text = "Recent project was not found and was removed from the list";
            return;
        }
        if (!await ConfirmCanReplaceCurrentProjectAsync()) return;

        try
        {
            if (path.EndsWith("project.json", StringComparison.OrdinalIgnoreCase))
            {
                var legacyDir = Path.GetDirectoryName(path)!;
                var result = _migrationService.MigrateLegacyProject(legacyDir);
                if (result.Success && result.Project != null)
                {
                    LoadProjectIntoEditor(result.Project, null, result.Project.Name);
                    StatusText.Text = "Legacy project migrated";
                }
                else
                {
                    MessageBox.Show($"Migration failed:\n{string.Join("\n", result.Errors)}", "Migration Error");
                }
                return;
            }

            var loaded = _projectRepo.Load(path);
            if (loaded == null)
            {
                StatusText.Text = "Project could not be loaded";
                return;
            }

            LoadProjectIntoEditor(loaded, path, Path.GetFileNameWithoutExtension(path));
            _recentProjectsService.Add(path);
            StatusText.Text = "Project opened";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Project open failed: {ex.Message}";
        }
    }

    private async void SaveProject_Executed(object sender, ExecutedRoutedEventArgs e) =>
        await SaveCurrentProjectAsync();
}
