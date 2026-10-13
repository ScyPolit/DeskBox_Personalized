using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;

namespace DeskBox.Helpers;

/// <summary>
/// Assigns a <see cref="UIElement"/>'s ProtectedCursor. The property is
/// protected in the WinUI 3 projection, so it can only be reached through
/// reflection — and Native AOT trims a member that nothing references
/// directly, which silently killed every resize/move cursor on retail
/// builds while JIT Debug kept working (same class as the PasswordBox
/// incident). The <see cref="DynamicDependencyAttribute"/> roots the setter
/// so the lookup resolves in trimmed retail builds too. Cursors are cached
/// per shape: the resize borders re-assign on every hover, and each
/// uncached assignment would create another native cursor object.
/// </summary>
internal static class UiElementCursorHelper
{
    private static readonly object Gate = new();
    private static readonly Dictionary<InputSystemCursorShape, InputSystemCursor> Cursors = [];

    [DynamicDependency("set_ProtectedCursor", typeof(UIElement))]
    internal static void SetCursor(UIElement element, InputSystemCursorShape shape)
    {
        InputSystemCursor cursor;
        lock (Gate)
        {
            if (!Cursors.TryGetValue(shape, out cursor!))
            {
                cursor = InputSystemCursor.Create(shape);
                Cursors[shape] = cursor;
            }
        }

        var property = typeof(UIElement).GetProperty(
            "ProtectedCursor",
            BindingFlags.Instance | BindingFlags.NonPublic);
        property?.SetValue(element, cursor);
    }
}
