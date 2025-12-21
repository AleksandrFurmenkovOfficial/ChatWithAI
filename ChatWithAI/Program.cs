using ChatWithAI.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NLog;
using NLog.Extensions.Hosting;
using System.Text;

namespace ChatWithAI
{
    internal sealed class Program
    {
        private static async Task Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            LogManager.Setup().LoadConfigurationFromFile("Settings/nlog.config");

            await Host.CreateDefaultBuilder(args)
                .UseNLog()
                .ConfigureAppConfiguration((context, config) =>
                {
                    config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
                    config.AddEnvironmentVariables();
                })
                .ConfigureServices((context, services) =>
                {
                    services.AddApplicationDependencies(context.Configuration);
                    services.AddHostedService<ChatHostedService>();
                })
                .UseConsoleLifetime()
                .Build()
                .RunAsync();
        }
    }
}