namespace GridSpace.Controls;

/// <summary>Office-density choice input with explicit focus and navigation ownership inside dialogs and nested flyouts.</summary>
public sealed class OfficeChoiceBox : ComboBox
{
    private bool _pressed;
    private bool _wasOpen;

    public OfficeChoiceBox()
    {
        DefaultStyleKey = typeof(ComboBox);
        IsTabStop = true;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        DropDownOpened += (_, _) => Focus(FocusState.Programmatic);
    }

    protected override void OnPointerPressed(PointerRoutedEventArgs e)
    {
        _wasOpen = IsDropDownOpen;
        _pressed = e.GetCurrentPoint(this).Properties.IsLeftButtonPressed;
        base.OnPointerPressed(e);
        if (_pressed) Focus(FocusState.Pointer);
    }

    protected override void OnPointerReleased(PointerRoutedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_pressed) return;
        _pressed = false;
        // Some Skia hosts leave focus on a ContentDialog's default button when opening a nested ComboBox.
        // Keep keyboard ownership on this input so Enter cannot accidentally submit the surrounding dialog.
        if (!_wasOpen) IsDropDownOpen = true;
        Focus(FocusState.Programmatic);
    }

    protected override void OnPointerCanceled(PointerRoutedEventArgs e)
    {
        _pressed = false;
        base.OnPointerCanceled(e);
    }

    protected override void OnKeyDown(KeyRoutedEventArgs e)
    {
        if (Items.Count == 0) { base.OnKeyDown(e); return; }
        var alt = (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Menu) & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;
        switch (e.Key)
        {
            case VirtualKey.Home: SelectedIndex = 0; break;
            case VirtualKey.End: SelectedIndex = Items.Count - 1; break;
            case VirtualKey.Down when alt: IsDropDownOpen = true; break;
            case VirtualKey.Up when alt: IsDropDownOpen = false; break;
            case VirtualKey.Down: SelectedIndex = Math.Min(Items.Count - 1, SelectedIndex + 1); break;
            case VirtualKey.Up: SelectedIndex = Math.Max(0, SelectedIndex - 1); break;
            case VirtualKey.Enter when IsDropDownOpen:
            case VirtualKey.Escape when IsDropDownOpen:
                IsDropDownOpen = false;
                break;
            case VirtualKey.Space: IsDropDownOpen = !IsDropDownOpen; break;
            default: base.OnKeyDown(e); return;
        }
        e.Handled = true;
    }
}
