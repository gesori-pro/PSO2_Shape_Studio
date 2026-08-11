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

// Shape sliders, undo/redo history, and pose rebuilding.
public partial class MainWindow : Window
{
    private async void LoadCharacter(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = L(AppText.CharacterPickerTitle),
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType(L(AppText.CharacterPickerType))
                {
                    Patterns = CharacterFile.SupportedExtensions
                        .Select(extension => $"*{extension}")
                        .ToArray(),
                },
            ],
        });
        await LoadPathsAsync(files.Select(file => file.Path.LocalPath));
    }

    private async void LoadAqm(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = L(AppText.AqmLoadTitle),
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(L(AppText.AqmFileType)) { Patterns = ["*.aqm"] }],
        });
        await LoadPathsAsync(files.Select(file => file.Path.LocalPath));
    }

    private async void SaveAqm(object? sender, RoutedEventArgs e)
    {
        if (_skeleton is null)
        {
            return;
        }

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = L(AppText.AqmSaveTitle),
            SuggestedFileName = "shape_sa.aqm",
            DefaultExtension = "aqm",
            FileTypeChoices = [new FilePickerFileType(L(AppText.AqmFileType)) { Patterns = ["*.aqm"] }],
        });
        if (file is null)
        {
            return;
        }

        try
        {
            var output = ShapeAdjustFile.Build(
                _skeleton,
                _profile,
                _shapeAdjust?.Adjustments,
                ActiveShapeGroups());
            await Task.Run(() => output.Save(file.Path.LocalPath));
            StatusText.Text = L(
                AppText.AqmSaved, Path.GetFileName(file.Path.LocalPath), output.Adjustments.Count);
        }
        catch (Exception exception)
        {
            StatusText.Text = L(AppText.AqmSaveFailed, exception.Message);
        }
    }

    private void ResetShape(object? sender, RoutedEventArgs e)
    {
        CommitPendingShapeEdit();
        var before = CaptureShapeState();
        _shapeAdjust = null;
        _profile = new ShapeProfile();
        RecordShapeEdit(before);
        SetEditorsFromProfile();
        RebuildPose();
        StatusText.Text = L(AppText.ShapeReset);
    }

    private void SliderValueChanged(object? sender, EventArgs e)
    {
        if (_updatingEditors || sender is not ShapeGroupViewModel editor)
        {
            return;
        }

        _pendingShapeEdit ??= CaptureShapeState();
        _profile[editor.Key] = editor.ToValue();
        _shapeEditTimer.Stop();
        _shapeEditTimer.Start();
        RebuildPose();
    }

    private ShapeHistoryState CaptureShapeState() => new(_profile.Clone(), _shapeAdjust);

    private void CommitPendingShapeEdit()
    {
        _shapeEditTimer.Stop();
        if (_pendingShapeEdit is null)
        {
            return;
        }

        var before = _pendingShapeEdit;
        _pendingShapeEdit = null;
        RecordShapeEdit(before);
    }

    private void RecordShapeEdit(ShapeHistoryState before)
    {
        if (before.Profile.ValueEquals(_profile) && ReferenceEquals(before.ShapeAdjust, _shapeAdjust))
        {
            return;
        }

        PushShapeState(_shapeUndo, before);
        _shapeRedo.Clear();
    }

    private static void PushShapeState(List<ShapeHistoryState> history, ShapeHistoryState state)
    {
        history.Add(state);
        if (history.Count > ShapeHistoryLimit)
        {
            history.RemoveAt(0);
        }
    }

    private void UndoShapeEdit()
    {
        CommitPendingShapeEdit();
        if (_shapeUndo.Count == 0)
        {
            return;
        }

        PushShapeState(_shapeRedo, CaptureShapeState());
        RestoreShapeState(PopShapeState(_shapeUndo));
        StatusText.Text = L(AppText.ShapeUndone);
    }

    private void RedoShapeEdit()
    {
        CommitPendingShapeEdit();
        if (_shapeRedo.Count == 0)
        {
            return;
        }

        PushShapeState(_shapeUndo, CaptureShapeState());
        RestoreShapeState(PopShapeState(_shapeRedo));
        StatusText.Text = L(AppText.ShapeRedone);
    }

    private static ShapeHistoryState PopShapeState(List<ShapeHistoryState> history)
    {
        var index = history.Count - 1;
        var state = history[index];
        history.RemoveAt(index);
        return state;
    }

    private void RestoreShapeState(ShapeHistoryState state)
    {
        _shapeAdjust = state.ShapeAdjust;
        _profile = state.Profile.Clone();
        SetEditorsFromProfile();
        RebuildPose();
    }

    private void SetEditorsFromProfile()
    {
        _updatingEditors = true;
        try
        {
            foreach (var editor in SliderGroups)
            {
                editor.SetValue(_profile[editor.Key]);
            }
        }
        finally
        {
            _updatingEditors = false;
        }
    }

    private void SetUniformDiagnosticScale(string key, float value)
    {
        var editor = SliderGroups.FirstOrDefault(group => group.Key == key);
        if (editor is null)
        {
            return;
        }
        editor.SetValue(new ShapeValue(new Vector3(value), Vector3.Zero, Vector3.Zero));
        _profile[key] = editor.ToValue();
        RebuildPose();
    }

    private void RebuildPose()
    {
        if (_skeleton is null)
        {
            return;
        }

        var composer = new BodyPoseComposer(_skeleton);
        if (_proportions is not null)
        {
            foreach (var (name, value) in _proportions.Bones)
            {
                composer.SetProportion(name, new BoneDelta(
                    ToVector3(value.Scale), ToVector3(value.Pos),
                    Quaternion.Normalize(new Quaternion(
                        (float)value.RotQuat[0], (float)value.RotQuat[1],
                        (float)value.RotQuat[2], (float)value.RotQuat[3]))));
            }
        }

        var activeGroups = ActiveShapeGroups();
        var sliderBones = activeGroups
            .SelectMany(group => group.RightBone is null
                ? new[] { group.LeftBone }
                : new[] { group.LeftBone, group.RightBone })
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (_shapeAdjust is not null)
        {
            foreach (var entry in _shapeAdjust.Adjustments.Values.Where(entry => !sliderBones.Contains(entry.Name)))
            {
                composer.SetShape(entry.Name, new BoneDelta(
                    entry.Scale ?? Vector3.One,
                    entry.Position ?? Vector3.Zero,
                    entry.Rotation ?? Quaternion.Identity));
            }
        }

        foreach (var group in activeGroups)
        {
            var value = _profile[group.Key];
            if (value.IsIdentity)
            {
                continue;
            }

            var rotation = ShapeSliders.EulerDegreesToQuaternion(value.EulerDegrees);
            composer.SetShape(group.LeftBone, new BoneDelta(value.Scale, value.Position, rotation));
            if (group.RightBone is not null)
            {
                composer.SetShape(group.RightBone, new BoneDelta(
                    value.Scale,
                    ShapeSliders.MirrorPosition(value.Position),
                    ShapeSliders.MirrorQuaternion(rotation)));
            }
        }

        var pose = composer.Build();
        Viewport.SetSkinMatrices(pose.SkinMatrices);
    }
}
