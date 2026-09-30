using System.Windows;
using App.Services;

namespace NittyGriddy.Tests;

public class DpiMathTests
{
    [Fact]
    public void Scale_one_only_offsets_by_origin()
    {
        var result = DpiMath.PixelsToDips(new Rect(-2460, 50, 400, 300), new Point(-2560, 0), 1.0);

        Assert.Equal(new Rect(100, 50, 400, 300), result);
    }

    [Fact]
    public void Scale_divides_offset_and_size()
    {
        var result = DpiMath.PixelsToDips(new Rect(300, 150, 600, 450), new Point(0, 0), 1.5);

        Assert.Equal(new Rect(200, 100, 400, 300), result);
    }

    [Fact]
    public void Non_positive_scale_is_treated_as_one()
    {
        var result = DpiMath.PixelsToDips(new Rect(10, 20, 30, 40), new Point(0, 0), 0);

        Assert.Equal(new Rect(10, 20, 30, 40), result);
    }
}
