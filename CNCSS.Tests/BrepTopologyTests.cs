using CNCSS.Geometry.BRep;

namespace CNCSS.Tests;

public class BrepTopologyTests
{
    [Fact]
    public void BoxPrimitive_ShouldHaveValidClosedShell()
    {
        var box = BrepPrimitives.CreateBox(new System.Windows.Media.Media3D.Rect3D(0, 0, 0, 10, 12, 3));
        bool ok = BrepTopologyValidator.ValidateClosedShell(box, out string err);
        Assert.True(ok, err);
    }
}
