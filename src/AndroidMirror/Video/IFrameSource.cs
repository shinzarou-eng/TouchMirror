namespace TouchMirror.Video;

public interface IFrameSource
{
    bool TryTakeLatest(out byte[]? buffer, out int width, out int height);
    void Release(byte[] buffer);
}
