using ChatWithAI.Contracts;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace ChatWithAI.Plugins.Windows.ScreenshotCapture
{
    [SupportedOSPlatform("windows6.1")]
    public sealed class WindowsScreenshotService(ILogger logger) : IScreenshotProvider
    {
        [DllImport("user32.dll")]
#pragma warning disable SYSLIB1054 // Use 'LibraryImportAttribute' instead of 'DllImportAttribute' to generate P/Invoke marshalling code at compile time
        private static extern int GetSystemMetrics(int nIndex);
#pragma warning restore SYSLIB1054 // Use 'LibraryImportAttribute' instead of 'DllImportAttribute' to generate P/Invoke marshalling code at compile time

        private const int SM_XVIRTUALSCREEN = 76;
        private const int SM_YVIRTUALSCREEN = 77;
        private const int SM_CXVIRTUALSCREEN = 78;
        private const int SM_CYVIRTUALSCREEN = 79;

        public async Task<byte[]> CaptureScreenAsync(CancellationToken cancellationToken = default)
        {
            return await Task.Run(() =>
            {
                try
                {
                    if (!OperatingSystem.IsWindows())
                    {
                        throw new PlatformNotSupportedException("Screenshot capture is only supported on Windows.");
                    }

                    var screenBounds = GetVirtualScreenBounds();

                    using var bitmap = new Bitmap(screenBounds.Width, screenBounds.Height);
                    using var graphics = Graphics.FromImage(bitmap);

                    graphics.CopyFromScreen(screenBounds.Left, screenBounds.Top, 0, 0, screenBounds.Size, CopyPixelOperation.SourceCopy);

                    using var stream = new MemoryStream();
                    bitmap.Save(stream, ImageFormat.Png);
                    return stream.ToArray();
                }
                catch (Exception ex)
                {
                    logger?.LogException(ex);
                    throw;
                }
            }, cancellationToken);
        }

        private static Rectangle GetVirtualScreenBounds()
        {
            int left = GetSystemMetrics(SM_XVIRTUALSCREEN);
            int top = GetSystemMetrics(SM_YVIRTUALSCREEN);
            int width = GetSystemMetrics(SM_CXVIRTUALSCREEN);
            int height = GetSystemMetrics(SM_CYVIRTUALSCREEN);
            return new Rectangle(left, top, width, height);
        }
    }
}
