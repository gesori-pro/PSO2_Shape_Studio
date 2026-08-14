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
    private static readonly string DisplayVersion = FormatDisplayVersion(
        typeof(MainWindow).Assembly.GetName().Version);
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
    private AppLanguage _language = AppLocalizer.DetectSystemLanguage();
    private bool _applyingLanguage;
    private bool _applyingSkinSelections;
    private int _selectedSkinType1Id = DefaultSkinType1Id;
    private int _selectedSkinType2Id = DefaultSkinType2Id;
    private RenderTextureSet? _skinTextureType1;
    private RenderTextureSet? _skinTextureType2;
    private string _defaultMainSkin = AppSettingDefaults.MainSkinColor;
    private string _defaultSubSkin = AppSettingDefaults.SubSkinColor;
    private HashSet<string> _hiddenShapeGroups = new(StringComparer.OrdinalIgnoreCase);
    private List<CustomBoneGroupSetting> _customShapeGroups = [];
    private CharacterColorPalette _characterColors = CharacterColorPalette.CreateWithSkinColors(
        AppSettingDefaults.MainSkinColor,
        AppSettingDefaults.SubSkinColor);
    private bool _characterColorsFromFile;
    private readonly Dictionary<string, RenderTextureSet> _skinTextureCache =
        new(StringComparer.OrdinalIgnoreCase);
    private ViewportStatistics? _lastStatistics;

    public MainWindow()
    {
        Models = [];
        SearchResults = [];
        SkinType1Options = [];
        SkinType2Options = [];
        SliderGroups = [];

        DataContext = this;
        InitializeComponent();
        Title = $"PSO2 Shape Studio {DisplayVersion}";
        VersionText.Text = $"v{DisplayVersion}";
        _shapeEditTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _shapeEditTimer.Tick += (_, _) => CommitPendingShapeEdit();
        RebuildSliderGroups();
        Viewport.SetCharacterColors(_characterColors);
        LanguageComboBox.ItemsSource = AppLocalizer.AvailableLanguages;
        AddHandler(
            InputElement.KeyDownEvent,
            WindowKeyDown,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        LanguageComboBox.SelectedIndex = LanguageIndex(_language);
        ApplyLanguage(initializeDataState: true);
        LanguageComboBox.SelectionChanged += LanguageChanged;
        BackgroundComboBox.SelectionChanged += async (_, _) =>
        {
            ApplyBackgroundSelection();
            try
            {
                await SaveSettingsAsync();
            }
            catch (IOException)
            {
                // A failed write only loses the preference for next launch.
            }
        };
        FloorGuideCheckBox.Click += async (_, _) =>
        {
            ApplyFloorGuideSelection();
            try
            {
                await SaveSettingsAsync();
            }
            catch (IOException)
            {
                // A failed write only loses the preference for next launch.
            }
        };
        MainSidebarSplitter.DragCompleted += async (_, _) =>
        {
            try
            {
                await SaveSettingsAsync();
            }
            catch (IOException)
            {
                // A failed write only loses the preference for next launch.
            }
        };
        OptionsButton.Click += OpenOptions;
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

    private static string FormatDisplayVersion(Version? version) =>
        version is null ? "development" : $"{version.Major}.{version.Minor}.{version.Build}";

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
            LanguageComboBox.SelectedItem is not AppLanguageOption option)
        {
            return;
        }

        SetLanguage(option.Language);
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
        LanguageComboBox.SelectedIndex = LanguageIndex(language);
        _applyingLanguage = false;
        ApplyLanguage(initializeDataState: false);
    }

    private static int LanguageIndex(AppLanguage language)
    {
        var code = AppLocalizer.LanguageCode(language);
        for (var index = 0; index < AppLocalizer.AvailableLanguages.Count; index++)
        {
            if (string.Equals(
                    AppLocalizer.LanguageCode(AppLocalizer.AvailableLanguages[index].Language),
                    code,
                    StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return 0;
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
        ToolTip.SetTip(BackgroundComboBox, L(AppText.BackgroundTip));
        FloorGuideCheckBox.Content = L(AppText.FloorGuide);
        ToolTip.SetTip(FloorGuideCheckBox, L(AppText.FloorGuideTip));
        OptionsButton.Content = L(AppText.Options);

        foreach (var item in BackgroundComboBox.Items.OfType<ComboBoxItem>())
        {
            if (item.Tag is string tag && BackgroundNames.TryGetValue(tag, out var name))
            {
                item.Content = L(name);
            }
        }

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

    // Keys are the hex tags declared on the MainWindow.axaml combo box items.
    private static readonly IReadOnlyDictionary<string, AppText> BackgroundNames =
        new Dictionary<string, AppText>(StringComparer.OrdinalIgnoreCase)
        {
            ["0E1114"] = AppText.BackgroundDark,
            ["2A2E36"] = AppText.BackgroundGraphite,
            ["6B7280"] = AppText.BackgroundGray,
            ["C9CED6"] = AppText.BackgroundSilver,
            ["F2F4F7"] = AppText.BackgroundWhite,
            ["17233D"] = AppText.BackgroundNavy,
            ["1D3327"] = AppText.BackgroundForest,
            ["8A1D6B"] = AppText.BackgroundMagenta,
        };

    private string SelectedBackgroundTag() =>
        (BackgroundComboBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "0E1114";

    private void ApplyBackgroundSelection()
    {
        var tag = SelectedBackgroundTag();
        if (tag.Length == 6 &&
            int.TryParse(tag, System.Globalization.NumberStyles.HexNumber, null, out var rgb))
        {
            Viewport.SetBackgroundColor(new Vector3(
                ((rgb >> 16) & 0xFF) / 255f,
                ((rgb >> 8) & 0xFF) / 255f,
                (rgb & 0xFF) / 255f));
        }
    }

    private void RestoreBackground(string? tag)
    {
        foreach (var item in BackgroundComboBox.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag as string, tag, StringComparison.OrdinalIgnoreCase))
            {
                BackgroundComboBox.SelectedItem = item;
                return;
            }
        }
    }

    private void ApplyFloorGuideSelection() =>
        Viewport.SetFloorGuideVisible(FloorGuideCheckBox.IsChecked != false);

    private void RestoreFloorGuide(bool? visible)
    {
        FloorGuideCheckBox.IsChecked = visible ?? true;
        ApplyFloorGuideSelection();
    }

    private async Task SaveSettingsAsync(string? dataPath = null)
    {
        Directory.CreateDirectory(AppDataDirectory);
        var settings = new AppSettings(
            dataPath ?? _dataLocator?.DataPath ?? "",
            AppLocalizer.LanguageCode(_language),
            _selectedSkinType1Id,
            _selectedSkinType2Id,
            SelectedBackgroundTag(),
            FloorGuideCheckBox.IsChecked != false,
            _defaultMainSkin,
            _defaultSubSkin,
            _hiddenShapeGroups.Order(StringComparer.OrdinalIgnoreCase).ToArray(),
            _customShapeGroups.ToArray(),
            CurrentSidebarWidth());
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

    private sealed record ShapeHistoryState(ShapeProfile Profile, ShapeAdjustFile? ShapeAdjust);
}
