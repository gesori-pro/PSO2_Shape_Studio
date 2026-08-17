using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Pso2ShapeStudio.App;

/// <summary>
/// Puts a spinner's decrease button on the left and its increase button on the
/// right. Fluent stacks them increase-first, so the arrow that lowers a value
/// sits to the right of the one that raises it - backwards from how the value
/// itself moves, and an easy way to nudge a slider the wrong way.
///
/// The two buttons are reordered rather than restyled: reproducing the whole
/// ButtonSpinner theme just to swap two children would pull in the theme's
/// internal resources, while PART_SpinnerPanel and PART_DecreaseButton are the
/// documented template contract.
/// </summary>
public static class SpinnerButtonOrder
{
    private const string SpinnerPanelName = "PART_SpinnerPanel";
    private const string DecreaseButtonName = "PART_DecreaseButton";

    public static readonly AttachedProperty<bool> DecreaseFirstProperty =
        AvaloniaProperty.RegisterAttached<ButtonSpinner, bool>(
            "DecreaseFirst", typeof(SpinnerButtonOrder));

    static SpinnerButtonOrder() =>
        DecreaseFirstProperty.Changed.AddClassHandler<ButtonSpinner>(DecreaseFirstChanged);

    public static void SetDecreaseFirst(ButtonSpinner spinner, bool value) =>
        spinner.SetValue(DecreaseFirstProperty, value);

    public static bool GetDecreaseFirst(ButtonSpinner spinner) =>
        spinner.GetValue(DecreaseFirstProperty);

    private static void DecreaseFirstChanged(
        ButtonSpinner spinner,
        AvaloniaPropertyChangedEventArgs args)
    {
        spinner.TemplateApplied -= TemplateApplied;
        spinner.Loaded -= Loaded;
        if (!args.GetNewValue<bool>())
        {
            return;
        }

        // Styling and templating do not run in a fixed order here: the spinner
        // is a template child of NumericUpDown, so it can already be templated
        // by the time this setter lands, in which case TemplateApplied never
        // fires again. Loaded always does. Reorder is idempotent, so whichever
        // arrives first settles it and the rest are no-ops.
        spinner.TemplateApplied += TemplateApplied;
        spinner.Loaded += Loaded;
        Reorder(spinner);
    }

    private static void TemplateApplied(object? sender, TemplateAppliedEventArgs args) =>
        Reorder(sender as ButtonSpinner);

    private static void Loaded(object? sender, RoutedEventArgs args) =>
        Reorder(sender as ButtonSpinner);

    private static void Reorder(ButtonSpinner? spinner)
    {
        if (spinner is null)
        {
            return;
        }

        var panel = spinner.GetVisualDescendants()
            .OfType<Panel>()
            .FirstOrDefault(candidate => candidate.Name == SpinnerPanelName);
        var decrease = panel?.Children
            .FirstOrDefault(child => child.Name == DecreaseButtonName);
        if (panel is null || decrease is null)
        {
            return;
        }

        var index = panel.Children.IndexOf(decrease);
        if (index <= 0)
        {
            return;
        }

        panel.Children.RemoveAt(index);
        panel.Children.Insert(0, decrease);
    }
}
