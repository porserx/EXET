namespace EXET.Cef.Contracts;

public sealed class CefPaintFrame : EventArgs
{
    public int Width { get; init; }
    public int Height { get; init; }
    public byte[] Pixels { get; init; } = Array.Empty<byte>();
}

public interface ICefHost
{
    event EventHandler<CefPaintFrame>? Paint;
    IntPtr CreateBrowser(string url, int width, int height);
    void Resize(IntPtr browserId, int width, int height);
    void DisposeBrowser(IntPtr browserId);
    void Shutdown();
}
