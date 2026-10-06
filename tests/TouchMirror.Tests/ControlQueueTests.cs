using TouchMirror.Engine;
using Xunit;

namespace TouchMirror.Tests;

public sealed class ControlQueueTests
{
    private static byte[] TouchMsg(byte action)
    {
        var m = new byte[32];
        m[0] = (byte)ControlMsgType.InjectTouchEvent;
        m[1] = action;
        return m;
    }

    [Fact]
    public void TouchMove_IsDroppable()
        => Assert.True(ControlChannel.IsDroppable(TouchMsg(AndroidMotionEvent.ActionMove)));

    [Fact]
    public void TouchUp_NeverDroppable()
        => Assert.False(ControlChannel.IsDroppable(TouchMsg(AndroidMotionEvent.ActionUp)));

    [Fact]
    public void PointerUp_NeverDroppable()
        => Assert.False(ControlChannel.IsDroppable(
            TouchMsg(AndroidMotionEvent.PointerAction(AndroidMotionEvent.ActionPointerUp, 1))));

    [Fact]
    public void Scroll_IsDroppable()
    {
        var m = new byte[21];
        m[0] = (byte)ControlMsgType.InjectScrollEvent;
        Assert.True(ControlChannel.IsDroppable(m));
    }

    [Fact]
    public void KeyUp_NeverDroppable()
    {
        var m = new byte[14];
        m[0] = (byte)ControlMsgType.InjectKeycode;
        m[1] = AndroidKeyEvent.ActionUp;
        Assert.False(ControlChannel.IsDroppable(m));
    }

    [Fact]
    public void OtherTypes_NeverDroppable()
    {
        var m = new byte[4];
        m[0] = (byte)ControlMsgType.InjectText;
        Assert.False(ControlChannel.IsDroppable(m));
        Assert.False(ControlChannel.IsDroppable(Array.Empty<byte>()));
    }
}
