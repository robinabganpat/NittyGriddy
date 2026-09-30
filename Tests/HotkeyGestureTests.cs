using System.Windows.Input;
using App.Models;

namespace NittyGriddy.Tests;

public class HotkeyGestureTests
{
    [Theory]
    [InlineData("Ctrl+Alt+G")]
    [InlineData("Alt+1")]
    [InlineData("Alt+Shift+9")]
    [InlineData("Alt+PgDn")]
    [InlineData("Alt+PgUp")]
    [InlineData("F5")]
    [InlineData("Ctrl+Shift+A")]
    [InlineData("Win+NumPad3")]
    public void Parse_then_format_round_trips(string text)
    {
        Assert.True(HotkeyGesture.TryParse(text, out var gesture));
        Assert.Equal(text, gesture.ToString());
    }

    [Fact]
    public void Parse_is_case_insensitive_and_accepts_control_spelled_out()
    {
        Assert.True(HotkeyGesture.TryParse("control+SHIFT+a", out var gesture));

        Assert.Equal(HotkeyModifiers.Control | HotkeyModifiers.Shift, gesture.Modifiers);
        Assert.Equal(0x41, gesture.VirtualKey);
    }

    [Fact]
    public void Digit_parses_to_top_row_virtual_key()
    {
        Assert.True(HotkeyGesture.TryParse("Alt+5", out var gesture));

        Assert.Equal(0x35, gesture.VirtualKey);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Ctrl+Alt")]
    [InlineData("Ctrl+")]
    [InlineData("Ctrl+NotAKey")]
    [InlineData("Banana+G")]
    public void Invalid_text_is_rejected(string text)
    {
        Assert.False(HotkeyGesture.TryParse(text, out _));
    }

    [Theory]
    [InlineData(Key.LeftCtrl)]
    [InlineData(Key.RightAlt)]
    [InlineData(Key.LeftShift)]
    [InlineData(Key.LWin)]
    [InlineData(Key.System)]
    [InlineData(Key.None)]
    public void Modifier_keys_alone_are_not_a_gesture(Key key)
    {
        Assert.Null(HotkeyGesture.FromKey(key, ModifierKeys.Control));
    }

    [Fact]
    public void FromKey_maps_wpf_modifiers()
    {
        var gesture = HotkeyGesture.FromKey(Key.D1, ModifierKeys.Alt | ModifierKeys.Shift);

        Assert.Equal("Alt+Shift+1", gesture.ToString());
    }

    [Fact]
    public void Every_default_hotkey_parses()
    {
        foreach (var action in HotkeyActions.All.Where(a => a.DefaultGesture.Length > 0))
        {
            Assert.True(HotkeyGesture.TryParse(action.DefaultGesture, out _), action.Id);
        }
    }

    [Fact]
    public void Defaults_have_no_duplicate_gestures()
    {
        var gestures = HotkeyActions.All
            .Where(a => a.DefaultGesture.Length > 0)
            .Select(a => a.DefaultGesture)
            .ToList();

        Assert.Equal(gestures.Count, gestures.Distinct().Count());
    }

    [Fact]
    public void Defaults_do_not_shadow_AltGr_characters()
    {
        // Ctrl+Alt is AltGr on US-International and most European layouts.
        foreach (var action in HotkeyActions.All.Where(a => a.DefaultGesture.Length > 0))
        {
            HotkeyGesture.TryParse(action.DefaultGesture, out var gesture);
            var isAltGr = gesture.Modifiers.HasFlag(HotkeyModifiers.Control | HotkeyModifiers.Alt);

            Assert.True(!isAltGr || action.Id == HotkeyActions.ToggleGrid, action.Id);
        }
    }

    [Fact]
    public void Slot_action_ids_round_trip()
    {
        Assert.True(HotkeyActions.TryGetSlot(HotkeyActions.FocusSlot(3), out var kind, out var slot));
        Assert.Equal(SlotActionKind.Focus, kind);
        Assert.Equal(3, slot);

        Assert.True(HotkeyActions.TryGetSlot(HotkeyActions.MoveToSlot(9), out kind, out slot));
        Assert.Equal(SlotActionKind.Move, kind);
        Assert.Equal(9, slot);

        Assert.False(HotkeyActions.TryGetSlot(HotkeyActions.ToggleGrid, out _, out _));
    }

    [Fact]
    public void Bring_all_tables_forward_defaults_to_Alt_0_and_is_a_table_hotkey()
    {
        var action = HotkeyActions.Find(HotkeyActions.AllTablesToFront);

        Assert.NotNull(action);
        Assert.Equal("Alt+0", action!.DefaultGesture);
        Assert.False(action.AlwaysActive);
    }

    [Fact]
    public void Settings_saved_before_an_action_existed_get_its_default()
    {
        var settings = new AppSettings();
        settings.Hotkeys.Remove(HotkeyActions.AllTablesToFront);

        settings.Normalize();

        Assert.Equal("Alt+0", settings.Hotkeys[HotkeyActions.AllTablesToFront]);
    }

    [Fact]
    public void Catalogue_has_nine_focus_and_nine_move_slots()
    {
        Assert.Equal(9, HotkeyActions.All.Count(a => a.Id.StartsWith("FocusSlot")));
        Assert.Equal(9, HotkeyActions.All.Count(a => a.Id.StartsWith("MoveToSlot")));
    }
}
