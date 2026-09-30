using System.Windows;
using App.Services;

namespace NittyGriddy.Tests;

public class FrameMathTests
{
    [Fact]
    public void Window_without_invisible_border_is_placed_exactly_on_the_target()
    {
        var window = new Rect(100, 100, 800, 600);

        var result = FrameMath.WindowRectForVisibleTarget(new Rect(0, 0, 400, 300), window, visibleFrame: window);

        Assert.Equal(new Rect(0, 0, 400, 300), result);
    }

    [Fact]
    public void Invisible_resize_border_is_added_around_the_target()
    {
        // Typical Windows 10/11 frame: 7px invisible border left, right and bottom, none on top
        var window = new Rect(100, 100, 814, 607);
        var frame = new Rect(107, 100, 800, 600);

        var result = FrameMath.WindowRectForVisibleTarget(new Rect(0, 0, 400, 300), window, frame);

        Assert.Equal(new Rect(-7, 0, 414, 307), result);
    }

    [Fact]
    public void Implausible_frame_is_ignored()
    {
        var window = new Rect(100, 100, 800, 600);
        var target = new Rect(0, 0, 400, 300);

        // Frame larger than the window, far smaller than it, or empty: fall back to the target as-is
        Assert.Equal(target, FrameMath.WindowRectForVisibleTarget(target, window, new Rect(50, 50, 900, 700)));
        Assert.Equal(target, FrameMath.WindowRectForVisibleTarget(target, window, new Rect(300, 300, 100, 100)));
        Assert.Equal(target, FrameMath.WindowRectForVisibleTarget(target, window, Rect.Empty));
    }

    [Fact]
    public void Fit_keeps_aspect_ratio_and_centres()
    {
        // 4:3 content in a wide area is limited by height and centred horizontally
        var result = FrameMath.FitPreservingAspect(new Size(800, 600), new Rect(0, 0, 1000, 300));

        Assert.Equal(new Rect(300, 0, 400, 300), result);
    }

    [Fact]
    public void Fit_limits_by_width_when_content_is_wider_than_the_area()
    {
        var result = FrameMath.FitPreservingAspect(new Size(1600, 400), new Rect(10, 20, 400, 300));

        Assert.Equal(new Rect(10, 120, 400, 100), result);
    }

    [Fact]
    public void Fit_of_degenerate_content_fills_the_area()
    {
        var area = new Rect(0, 0, 400, 300);

        Assert.Equal(area, FrameMath.FitPreservingAspect(new Size(0, 600), area));
        Assert.Equal(area, FrameMath.FitPreservingAspect(Size.Empty, area));
    }
}
