using ChatWithAI.Providers.Google;
using System.Reflection;
using System.Text;
using Xunit.Abstractions;

namespace ChatWithAI.Tests;

/// <summary>
/// Tests for GeminiResponseStream-like behavior to detect race conditions.
/// </summary>
public class GeminiResponseStreamTests
{
    private readonly ITestOutputHelper _output;

    public GeminiResponseStreamTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static (IAiStreamingResponse Response, Func<string, Task> WriteTextAsync, Action Complete) CreateStream()
    {
        var providerAssembly = typeof(GeminiConfig).Assembly;
        var streamType = providerAssembly.GetType("ChatWithAI.Providers.Google.GeminiStreamingResponse", throwOnError: true);
        Assert.NotNull(streamType);

        var instance = Activator.CreateInstance(streamType!, nonPublic: true)
            ?? throw new InvalidOperationException("Failed to create GeminiResponseStream instance");

        var writeTextAsync = streamType!.GetMethod("WriteTextAsync", BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(writeTextAsync);

        var complete = streamType.GetMethod("Complete", BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(complete);

        return (
            (IAiStreamingResponse)instance,
            (string text) =>
                (Task)(writeTextAsync!.Invoke(instance, [text])
                    ?? throw new InvalidOperationException("WriteTextAsync returned null")),
            () => complete!.Invoke(instance, null));
    }

    [Fact]
    public async Task GeminiResponseStream_Dispose_UnblocksReader()
    {
        var (response, writeTextAsync, _) = CreateStream();

        await writeTextAsync("Hello");

        var readerTask = Task.Run(async () =>
        {
            var sb = new StringBuilder();
            await foreach (var delta in response.GetTextDeltasAsync())
            {
                sb.Append(delta);
            }
            return sb.ToString();
        });

        await response.DisposeAsync();

        var actual = await readerTask;
        Assert.Equal("Hello", actual);
    }

    [Fact]
    public async Task GeminiResponseStream_CumulativeWrites_NoDuplication()
    {
        var (response, writeTextAsync, complete) = CreateStream();

        await using (response)
        {
            var cumulativeChunks = new[]
            {
                "Hello",
                "Hello, World!"
            };

            foreach (var chunk in cumulativeChunks)
            {
                await writeTextAsync(chunk);
            }

            complete();

            var sb = new StringBuilder();
            await foreach (var delta in response.GetTextDeltasAsync())
            {
                sb.Append(delta);
            }

            Assert.Equal(cumulativeChunks[^1], sb.ToString());
        }
    }

    [Fact]
    public async Task GeminiResponseStream_ConcurrentWriteAndRead_NoDataLoss()
    {
        _output.WriteLine("TEST START: GeminiResponseStream_ConcurrentWriteAndRead_NoDataLoss");

        var (response, writeTextAsync, complete) = CreateStream();

        await using (response)
        {
            var expectedText = "Hello, World! This is a test message.";

            var readerTask = Task.Run(async () =>
            {
                var sb = new StringBuilder();
                await foreach (var delta in response.GetTextDeltasAsync())
                {
                    sb.Append(delta);
                }
                return sb.ToString();
            });

            var writerTask = Task.Run(async () =>
            {
                foreach (var chunk in expectedText.Chunk(5))
                {
                    await writeTextAsync(new string(chunk));
                    await Task.Delay(10);
                }
                complete();
            });

            await writerTask;
            var actualText = await readerTask;

            _output.WriteLine($"Expected: '{expectedText}'");
            _output.WriteLine($"Actual:   '{actualText}'");

            Assert.Equal(expectedText, actualText);
            _output.WriteLine("TEST PASSED");
        }
    }
}
