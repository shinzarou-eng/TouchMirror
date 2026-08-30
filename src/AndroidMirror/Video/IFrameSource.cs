namespace TouchMirror.Video;

/// <summary>Source de frames BGRA poolées consommée par <see cref="Views.MirrorView"/>.</summary>
public interface IFrameSource
{
    bool TryTakeLatest(out byte[]? buffer, out int width, out int height);
    void Release(byte[] buffer);
}
