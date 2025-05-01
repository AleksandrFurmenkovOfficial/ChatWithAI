using ChatWithAI.Contracts;
using Microsoft.Extensions.Hosting;

namespace ChatWithAI
{
    internal sealed class ChatHostedService(
        IChatProcessor chatProcessor,
        IHostApplicationLifetime applicationLifetime) : IHostedService
    {
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            try
            {
                await chatProcessor.RunEventLoop(cancellationToken);
            }
            catch
            {
                applicationLifetime.StopApplication();
            }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}