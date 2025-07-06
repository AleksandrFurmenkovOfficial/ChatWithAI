using ChatWithAI.Contracts;

namespace ChatWithAI.Plugins.Windows.ScreenshotCapture
{   
    public sealed class WindowsScreenshotServiceStub() : IScreenshotProvider
    {
        public Task<byte[]> CaptureScreenAsync(CancellationToken cancellationToken = default)
        {
            throw new PlatformNotSupportedException("Screenshot capture is only supported on Windows.");
        }
    }
}
