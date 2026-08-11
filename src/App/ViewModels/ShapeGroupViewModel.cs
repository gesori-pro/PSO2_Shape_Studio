using System.ComponentModel;
using System.Runtime.CompilerServices;
using Pso2ShapeStudio.App.Localization;
using System.Numerics;
using Pso2ShapeStudio.Rigging;

namespace Pso2ShapeStudio.App;

public sealed class ShapeGroupViewModel : INotifyPropertyChanged
{
    private readonly string _defaultLabel;
    private AppLanguage _language;
    private double _scaleX = 1;
    private double _scaleY = 1;
    private double _scaleZ = 1;
    private double _positionX;
    private double _positionY;
    private double _positionZ;
    private double _rotationX;
    private double _rotationY;
    private double _rotationZ;

    public ShapeGroupViewModel(ShapeGroupDefinition definition, AppLanguage language)
    {
        Key = definition.Key;
        _defaultLabel = definition.Label;
        _language = language;
        SupportsRotation = definition.SupportsRotation;
        ShowsRotation = definition.ShowsRotation;
        SupportsPosition = definition.SupportsPosition;
        SupportsScaleX = definition.SupportsScaleX;
        SupportsScaleY = definition.SupportsScaleY;
        SupportsScaleZ = definition.SupportsScaleZ;
        ScaleMinimum = definition.ScaleMinimum;
        ScaleMaximum = definition.ScaleMaximum;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? ValueChanged;

    public string Key { get; }
    public string Label => AppLocalizer.ShapeName(_language, Key, _defaultLabel);
    public string ScaleLabel => AppLocalizer.Text(_language, AppText.Scale);
    public string PositionLabel => AppLocalizer.Text(_language, AppText.Position);
    public string RotationLabel => AppLocalizer.Text(_language, AppText.Rotation);
    public bool SupportsRotation { get; }
    public bool ShowsRotation { get; }
    public bool SupportsPosition { get; }
    public bool SupportsScaleX { get; }
    public bool SupportsScaleY { get; }
    public bool SupportsScaleZ { get; }
    public double ScaleMinimum { get; }
    public double ScaleMaximum { get; }

    public double ScaleX { get => _scaleX; set => Set(ref _scaleX, value); }
    public double ScaleY { get => _scaleY; set => Set(ref _scaleY, value); }
    public double ScaleZ { get => _scaleZ; set => Set(ref _scaleZ, value); }
    public double PositionX { get => _positionX; set => Set(ref _positionX, value); }
    public double PositionY { get => _positionY; set => Set(ref _positionY, value); }
    public double PositionZ { get => _positionZ; set => Set(ref _positionZ, value); }
    public double RotationX { get => _rotationX; set => Set(ref _rotationX, value); }
    public double RotationY { get => _rotationY; set => Set(ref _rotationY, value); }
    public double RotationZ { get => _rotationZ; set => Set(ref _rotationZ, value); }

    public ShapeValue ToValue() => new(
        new Vector3((float)ScaleX, (float)ScaleY, (float)ScaleZ),
        new Vector3((float)PositionX, (float)PositionY, (float)PositionZ),
        new Vector3((float)RotationX, (float)RotationY, (float)RotationZ));

    public void SetValue(ShapeValue value)
    {
        ScaleX = value.Scale.X;
        ScaleY = value.Scale.Y;
        ScaleZ = value.Scale.Z;
        PositionX = value.Position.X;
        PositionY = value.Position.Y;
        PositionZ = value.Position.Z;
        RotationX = value.EulerDegrees.X;
        RotationY = value.EulerDegrees.Y;
        RotationZ = value.EulerDegrees.Z;
    }

    public void SetLanguage(AppLanguage language)
    {
        if (_language == language)
        {
            return;
        }

        _language = language;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Label)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ScaleLabel)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PositionLabel)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RotationLabel)));
    }

    private void Set(ref double field, double value, [CallerMemberName] string? propertyName = null)
    {
        if (field == value)
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        ValueChanged?.Invoke(this, EventArgs.Empty);
    }
}
