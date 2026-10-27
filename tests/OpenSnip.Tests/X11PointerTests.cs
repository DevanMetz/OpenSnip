namespace OpenSnip.Tests;

public class X11PointerTests
{
    [Fact]
    public void Query_returns_a_point_when_the_display_opens()
    {
        using var pointer = X11Pointer.TryOpen();
        if (pointer == null) return;
        Assert.NotNull(pointer.Position());
    }
}
