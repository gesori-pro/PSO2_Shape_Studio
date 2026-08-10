using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using Pso2ShapeStudio.App.Localization;
using Pso2ShapeStudio.App.Rendering;
using Pso2ShapeStudio.Character;
using Pso2ShapeStudio.GameData;
using Pso2ShapeStudio.Formats;
using Pso2ShapeStudio.Rigging;

namespace Pso2ShapeStudio.App;

public partial class MainWindow : Window
{
    private const int DefaultSkinType1Id = 100000;
    private const int DefaultSkinType2Id = 200000;
    private const int ShapeHistoryLimit = 100;
    private static readonly string AppDataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Pso2ShapeStudio");
    private static readonly string CatalogPath = Path.Combine(AppDataDirectory, "objects.db");
    private static readonly string SettingsPath = Path.Combine(AppDataDirectory, "settings.json");
    private IBrush DataInfoBrush => Brush.Parse(
        ActualThemeVariant == ThemeVariant.Light ? "#475569" : "#8792A6");
    private IBrush DataSuccessBrush => Brush.Parse(
        ActualThemeVariant == ThemeVariant.Light ? "#047857" : "#74C69D");
    private IBrush DataErrorBrush => Brush.Parse(
        ActualThemeVariant == ThemeVariant.Light ? "#B91C1C" : "#F08A8A");

    private AqnSkeleton? _skeleton;
    private ProportionResult? _proportions;
    private ShapeAdjustFile? _shapeAdjust;
    private ShapeProfile _profile = new();
    private readonly List<ShapeHistoryState> _shapeUndo = [];
    private readonly List<ShapeHistoryState> _shapeRedo = [];
    private ShapeHistoryState? _pendingShapeEdit;
    private readonly DispatcherTimer _shapeEditTimer;
    private bool _updatingEditors;
    private DispatcherTimer? _stressTimer;
    private int _stressFrame;
    private Pso2DataLocator? _dataLocator;
    private ModelCatalog? _catalog;
    private AppLanguage _language = AppLanguage.English;
    private bool _applyingLanguage;
    private bool _applyingSkinSelections;
    private int _selectedSkinType1Id = DefaultSkinType1Id;
    private int _selectedSkinType2Id = DefaultSkinType2Id;
    private RenderTextureSet? _skinTextureType1;
    private RenderTextureSet? _skinTextureType2;
    private CharacterColorPalette _characterColors = CharacterColorPalette.Default;
    private readonly Dictionary<string, RenderTextureSet> _skinTextureCache =
        new(StringComparer.OrdinalIgnoreCase);
    private ViewportStatistics? _lastStatistics;

    public MainWindow()
    {
        Models = [];
        SearchResults = [];
        SkinType1Options = [];
        SkinType2Options = [];
        SliderGroups = new ObservableCollection<ShapeGroupViewModel>(
            ShapeSliders.Groups.Select(group => new ShapeGroupViewModel(group, _language)));
        foreach (var editor in SliderGroups)
        {
            editor.ValueChanged += SliderValueChanged;
        }

        DataContext = this;
        InitializeComponent();
        _shapeEditTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _shapeEditTimer.Tick += (_, _) => CommitPendingShapeEdit();
        AddHandler(
            InputElement.KeyDownEvent,
            WindowKeyDown,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        ApplyLanguage(initializeDataState: true);
        LanguageComboBox.SelectionChanged += LanguageChanged;
        SkinType1ComboBox.SelectionChanged += SkinType1Changed;
        SkinType2ComboBox.SelectionChanged += SkinType2Changed;
        Viewport.SetLanguage(_language);

        OpenFilesButton.Click += OpenFiles;
        LoadCharacterButton.Click += LoadCharacter;
        LoadAqmButton.Click += LoadAqm;
        SaveAqmButton.Click += SaveAqm;
        ResetShapeButton.Click += ResetShape;
        SelectDataFolderButton.Click += SelectDataFolder;
        BuildCatalogButton.Click += RebuildCatalog;
        SearchModelsButton.Click += SearchModels;
        LoadSearchResultButton.Click += LoadSearchResult;
        ModelSearchResultsList.SelectionChanged += (_, _) =>
            LoadSearchResultButton.IsEnabled = ModelSearchResultsList.SelectedItem is ModelSearchResultViewModel
            { ResolvedPath: not null };
        ModelSearchResultsList.DoubleTapped += LoadSearchResult;
        ModelSearchBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                SearchModels(null, new RoutedEventArgs());
                e.Handled = true;
            }
        };
        Viewport.StatisticsChanged += ViewportStatisticsChanged;
        ViewportInputSurface.AddHandler(
            InputElement.PointerPressedEvent,
            ViewportPointerPressed,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        ViewportInputSurface.AddHandler(
            InputElement.PointerMovedEvent,
            ViewportPointerMoved,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        ViewportInputSurface.AddHandler(
            InputElement.PointerReleasedEvent,
            ViewportPointerReleased,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        ViewportInputSurface.AddHandler(
            InputElement.PointerCaptureLostEvent,
            ViewportPointerCaptureLost,
            RoutingStrategies.Direct,
            handledEventsToo: true);
        ViewportInputSurface.AddHandler(
            InputElement.PointerWheelChangedEvent,
            ViewportPointerWheelChanged,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        if (Environment.GetCommandLineArgs().Contains(
                "--camera-diagnostics",
                StringComparer.OrdinalIgnoreCase))
        {
            Viewport.CameraChanged += (_, camera) =>
                StatusText.Text =
                    $"CAMERA mode={camera.Mode} yaw={camera.Yaw:F3} pitch={camera.Pitch:F3} " +
                    $"focusY={camera.FocusY:F3} distance={camera.Distance:F3}";
        }
        Viewport.RendererStatusChanged += (_, message) =>
        {
            if (StatusText.Text == L(AppText.Ready) ||
                message.Contains(L(AppText.FailedWord), StringComparison.OrdinalIgnoreCase))
            {
                StatusText.Text = message;
            }
        };
        var rendererSmoke = Environment.GetCommandLineArgs().FirstOrDefault(argument =>
            argument.StartsWith("--renderer-smoke=", StringComparison.OrdinalIgnoreCase));
        if (rendererSmoke is not null)
        {
            var reportPath = rendererSmoke["--renderer-smoke=".Length..];
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Position = new PixelPoint(-32000, -32000);
            Viewport.RendererStatusChanged += (_, message) =>
            {
                try
                {
                    File.WriteAllText(reportPath, message);
                }
                finally
                {
                    DispatcherTimer.RunOnce(Close, TimeSpan.FromMilliseconds(100));
                }
            };
        }
        var sceneSmoke = Environment.GetCommandLineArgs().FirstOrDefault(argument =>
            argument.StartsWith("--scene-smoke=", StringComparison.OrdinalIgnoreCase));
        if (sceneSmoke is not null)
        {
            var reportPath = sceneSmoke["--scene-smoke=".Length..];
            var completed = false;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Position = new PixelPoint(-32000, -32000);

            void CompleteSceneSmoke(string message)
            {
                if (completed)
                {
                    return;
                }

                completed = true;
                File.WriteAllText(reportPath, message);
                DispatcherTimer.RunOnce(Close, TimeSpan.FromMilliseconds(100));
            }

            Viewport.RendererStatusChanged += (_, message) =>
            {
                if (message.Contains(L(AppText.FailedWord), StringComparison.OrdinalIgnoreCase))
                {
                    CompleteSceneSmoke(message);
                }
            };
            Viewport.StatisticsChanged += (_, statistics) =>
            {
                if (statistics.VertexCount > 0 && statistics.TextureCount > 0)
                {
                    CompleteSceneSmoke(
                        $"scene ready: models={statistics.ModelCount} vertices={statistics.VertexCount} " +
                        $"triangles={statistics.TriangleCount} textures={statistics.TextureCount}");
                }
            };
        }
        DragDrop.SetAllowDrop(this, true);
        DragDrop.AddDropHandler(this, FilesDropped);
        Opened += LoadCommandLineFiles;
    }

    public ObservableCollection<ModelEntry> Models { get; }

    public ObservableCollection<ModelSearchResultViewModel> SearchResults { get; }

    public ObservableCollection<SkinOptionViewModel> SkinType1Options { get; }

    public ObservableCollection<SkinOptionViewModel> SkinType2Options { get; }

    public ObservableCollection<ShapeGroupViewModel> SliderGroups { get; }

    private void ViewportPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(ViewportInputSurface);
        if (!Viewport.BeginCameraInteraction(
                e.GetPosition(ViewportInputSurface),
                point.Properties,
                e.KeyModifiers))
        {
            return;
        }

        ViewportInputSurface.Focus();

        if (point.Properties.PointerUpdateKind != PointerUpdateKind.MiddleButtonPressed &&
            !point.Properties.IsMiddleButtonPressed)
        {
            e.Pointer.Capture(ViewportInputSurface);
        }

        e.Handled = true;
    }

    private void ViewportPointerMoved(object? sender, PointerEventArgs e)
    {
        if (Viewport.UpdateCameraInteraction(e.GetPosition(ViewportInputSurface)))
        {
            e.Handled = true;
        }
    }

    private void ViewportPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (Viewport.EndCameraInteraction())
        {
            e.Pointer.Capture(null);
            e.Handled = true;
        }
    }

    private void ViewportPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e) =>
        Viewport.EndCameraInteraction();

    private void ViewportPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        Viewport.ZoomCamera(e.Delta.Y);
        e.Handled = true;
    }

    private string L(AppText key, params object?[] arguments) =>
        AppLocalizer.Text(_language, key, arguments);

    private async void LanguageChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_applyingLanguage ||
            LanguageComboBox.SelectedItem is not ComboBoxItem { Tag: string code })
        {
            return;
        }

        SetLanguage(AppLocalizer.ParseLanguage(code));
        try
        {
            await SaveSettingsAsync();
        }
        catch (Exception exception)
        {
            StatusText.Text = L(AppText.DataPrepareFailedStatus, exception.Message);
        }
    }



    private void SetLanguage(AppLanguage language)
    {
        _language = language;
        _applyingLanguage = true;
        LanguageComboBox.SelectedIndex = language switch
        {
            AppLanguage.Japanese => 1,
            AppLanguage.Korean => 2,
            _ => 0,
        };
        _applyingLanguage = false;
        ApplyLanguage(initializeDataState: false);
    }

    private void ApplyLanguage(bool initializeDataState)
    {
        SubtitleText.Text = L(AppText.Subtitle);
        OpenFilesButton.Content = L(AppText.OpenModels);
        GameDataExpander.Header = L(AppText.GameDataHeader);
        SelectDataFolderButton.Content = L(AppText.SelectGameFolder);
        BuildCatalogButton.Content = L(AppText.RefreshCache);
        ModelSearchBox.PlaceholderText = L(AppText.SearchPlaceholder);
        SearchModelsButton.Content = L(AppText.Search);
        WearBasewearCheck.Content = L(AppText.WearBasewear);
        WearOuterwearCheck.Content = L(AppText.WearOuterwear);
        WearSetwearCheck.Content = L(AppText.WearSetwear);
        WearCostumeCheck.Content = L(AppText.WearCostume);
        LoadSearchResultButton.Content = L(AppText.LoadSelectedModel);
        SkinTexturesHeaderText.Text = L(AppText.SkinTextures);
        SkinType1Label.Text = L(AppText.SkinType1);
        SkinType2Label.Text = L(AppText.SkinType2);
        ShapeSlidersHeaderText.Text = L(AppText.ShapeSliders);
        LoadCharacterButton.Content = L(AppText.LoadCharacter);
        LoadAqmButton.Content = L(AppText.LoadAqm);
        ResetShapeButton.Content = L(AppText.ResetShape);
        SaveAqmButton.Content = L(AppText.SaveAqm);
        CameraHelpText.Text = L(AppText.CameraHelp);
        ToolTip.SetTip(LanguageComboBox, L(AppText.LanguageTip));

        foreach (var group in SliderGroups)
        {
            group.SetLanguage(_language);
        }

        foreach (var result in SearchResults)
        {
            result.SetLanguage(_language);
        }

        foreach (var model in Models)
        {
            model.SetLanguage(_language);
        }

        foreach (var skin in SkinType1Options.Concat(SkinType2Options))
        {
            skin.SetLanguage(_language);
        }

        Viewport.SetLanguage(_language);
        if (_lastStatistics is not null)
        {
            UpdateStatisticsText(_lastStatistics);
        }

        if (_dataLocator is not null && _catalog?.Exists == true)
        {
            DataFolderValidationText.Text = L(AppText.CacheReadyInline, _catalog.RecordCount);
            StatusText.Text = L(AppText.CacheReadyStatus, _catalog.RecordCount);
        }
        else if (initializeDataState)
        {
            DataFolderText.Text = L(AppText.DataFolderUnspecified);
            DataFolderValidationText.Text = L(AppText.DataFolderHelp);
            StatusText.Text = L(AppText.Ready);
        }
    }

    private async Task SaveSettingsAsync(string? dataPath = null)
    {
        Directory.CreateDirectory(AppDataDirectory);
        var settings = new AppSettings(
            dataPath ?? _dataLocator?.DataPath ?? "",
            AppLocalizer.LanguageCode(_language),
            _selectedSkinType1Id,
            _selectedSkinType2Id);
        await File.WriteAllTextAsync(SettingsPath, JsonSerializer.Serialize(settings));
    }



























    private void WindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Source is TextBox || !e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            return;
        }

        if (e.Key == Key.Z && e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            RedoShapeEdit();
            e.Handled = true;
        }
        else if (e.Key == Key.Z)
        {
            UndoShapeEdit();
            e.Handled = true;
        }
        else if (e.Key == Key.Y)
        {
            RedoShapeEdit();
            e.Handled = true;
        }
    }















    private void ViewportStatisticsChanged(object? sender, ViewportStatistics statistics)
    {
        _lastStatistics = statistics;
        UpdateStatisticsText(statistics);
    }

    private void UpdateStatisticsText(ViewportStatistics statistics) =>
        StatisticsText.Text = L(
            AppText.Statistics,
            statistics.ModelCount,
            statistics.VertexCount,
            statistics.TriangleCount,
            statistics.TextureCount,
            statistics.FramesPerSecond);

    private void StartStressTest()
    {
        _stressTimer?.Stop();
        _stressTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) =>
        {
            var scale = 1.175f + 0.175f * MathF.Sin(_stressFrame++ * 0.08f);
            _profile["waist"] = new ShapeValue(new Vector3(scale), Vector3.Zero, Vector3.Zero);
            RebuildPose();
        });
        _stressTimer.Start();
    }

    private void UpdateCommandAvailability()
    {
        var enabled = _skeleton is not null;
        LoadCharacterButton.IsEnabled = enabled;
        LoadAqmButton.IsEnabled = enabled;
        SaveAqmButton.IsEnabled = enabled;
        ResetShapeButton.IsEnabled = enabled;
    }






    private sealed record LoadResult(
        AqnSkeleton? Skeleton,
        IReadOnlyList<RenderModel> Models,
        ProportionResult? Proportions,
        CharacterColorPalette? Colors,
        ShapeAdjustFile? ShapeAdjust,
        int ArchiveEntryCount,
        int ArchiveDdsCount);

    private sealed record AppSettings(
        string DataPath = "",
        string Language = "en",
        int SkinType1 = DefaultSkinType1Id,
        int SkinType2 = DefaultSkinType2Id);

    private sealed record ShapeHistoryState(ShapeProfile Profile, ShapeAdjustFile? ShapeAdjust);
}
