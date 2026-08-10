using System.ComponentModel;
using System.Runtime.CompilerServices;
using Pso2ShapeStudio.App.Localization;
using Pso2ShapeStudio.Formats;

namespace Pso2ShapeStudio.App;

public sealed class ModelEntry : INotifyPropertyChanged
{
    private bool _visible = true;
    private AppLanguage _language;

    public ModelEntry(RenderModel model, AppLanguage language)
    {
        Model = model;
        _language = language;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? VisibilityChanged;

    public RenderModel Model { get; }
    public string RemoveLabel => AppLocalizer.Text(_language, AppText.RemoveModel);
    public string DisplayName
    {
        get
        {
            var marker = Model.SourcePath.LastIndexOf("::", StringComparison.Ordinal);
            return marker >= 0
                ? Model.SourcePath[(marker + 2)..]
                : Path.GetFileName(Model.SourcePath);
        }
    }

    public bool Visible
    {
        get => _visible;
        set
        {
            if (_visible == value) return;
            _visible = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Visible)));
            VisibilityChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void SetLanguage(AppLanguage language)
    {
        if (_language == language)
        {
            return;
        }

        _language = language;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RemoveLabel)));
    }
}
