using System.Windows.Input;
using System.Windows.Interop;
using App.Controls;

namespace NittyGriddy.Tests;

/// <summary>
/// Drives the hotkey capture box with key events raised in-process; nothing is typed on the real keyboard.
/// Modifier state comes from the real keyboard, so these cover unmodified keys only; modifier mapping is
/// covered by HotkeyGestureTests.
/// </summary>
[Collection(WpfCollection.Name)]
public class HotkeyBoxTests
{
    private static void RunOnStaThread(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure != null)
            throw new Xunit.Sdk.XunitException(failure.ToString());
    }

    /// <summary>
    /// Raise a key press on the box and report whether the box consumed it
    /// </summary>
    private static bool Press(HotkeyBox box, HwndSource source, Key key)
    {
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent
        };
        box.RaiseEvent(args);
        return args.Handled;
    }

    private static void WithBox(string initialGesture, Action<HotkeyBox, HwndSource> test)
    {
        RunOnStaThread(() =>
        {
            var box = new HotkeyBox { Gesture = initialGesture };
            using var source = new HwndSource(new HwndSourceParameters("HotkeyBoxTest") { Width = 1, Height = 1, WindowStyle = 0 });
            source.RootVisual = box;

            test(box, source);
        });
    }

    [Fact]
    public void Shows_the_gesture_or_a_placeholder()
    {
        WithBox("Alt+1", (box, _) =>
        {
            Assert.Equal("Alt+1", box.Text);

            box.Gesture = "";
            Assert.Equal("Not set", box.Text);
        });
    }

    [Fact]
    public void Pressing_a_key_records_it()
    {
        WithBox("Alt+1", (box, source) =>
        {
            Assert.True(Press(box, source, Key.F8));

            Assert.Equal("F8", box.Gesture);
            Assert.Equal("F8", box.Text);
        });
    }

    [Theory]
    [InlineData(Key.Back)]
    [InlineData(Key.Delete)]
    public void Backspace_or_delete_clears_the_binding(Key key)
    {
        WithBox("Alt+1", (box, source) =>
        {
            Assert.True(Press(box, source, key));

            Assert.Equal("", box.Gesture);
        });
    }

    [Fact]
    public void Escape_leaves_the_binding_unchanged()
    {
        WithBox("Alt+1", (box, source) =>
        {
            Assert.True(Press(box, source, Key.Escape));

            Assert.Equal("Alt+1", box.Gesture);
        });
    }

    [Theory]
    [InlineData(Key.LeftCtrl)]
    [InlineData(Key.LeftAlt)]
    [InlineData(Key.RightShift)]
    public void A_modifier_on_its_own_does_not_change_the_binding(Key key)
    {
        WithBox("Alt+1", (box, source) =>
        {
            Press(box, source, key);

            Assert.Equal("Alt+1", box.Gesture);
        });
    }

    [Fact]
    public void Tab_is_left_for_keyboard_navigation()
    {
        WithBox("Alt+1", (box, source) =>
        {
            Assert.False(Press(box, source, Key.Tab));

            Assert.Equal("Alt+1", box.Gesture);
        });
    }
}
