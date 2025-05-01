using ChatWithAI.Contracts.Configs;
using ChatWithAI.Core.ChatCommands;
using ChatWithAI.Core.ChatMessageActions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using TelegramChatGPT.Implementation;

namespace ChatWithAI.DependencyInjection
{
    public static class DependencyInjectionExtensions
    {
        public static IServiceCollection AddApplicationDependencies(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            services.AddSingleton<ILogger, Logger>();

            services.AddOptions<AppConfig>()
                .Bind(configuration)
                .ValidateDataAnnotations()
                .ValidateOnStart();

            services.AddOptions<StorageConfig>()
                .Bind(configuration)
                .ValidateDataAnnotations()
                .ValidateOnStart();

            var storageConfig = configuration.Get<StorageConfig>() ?? throw new InvalidOperationException("StorageConfig is not configured.");
            services.AddSingleton<IAccessStorage, AccessStorage>(sp =>
            {
                return new AccessStorage(Path.Combine(AppContext.BaseDirectory, storageConfig.AccessFolder));
            });
            services.AddSingleton<IMemoryStorage, MemoryStorage>(sp =>
            {
                return new MemoryStorage(Path.Combine(AppContext.BaseDirectory, storageConfig.MemoryFolder));
            });
            services.AddSingleton<IModeStorage, ModeStorage>(sp =>
            {
                return new ModeStorage(Path.Combine(AppContext.BaseDirectory, storageConfig.ModesFolder));
            });

            ConfigureProvider(services, configuration);
            ConfigureCore(services, configuration);

            return services;
        }

        private static void ConfigureProvider(
            IServiceCollection services,
            IConfiguration configuration)

        {
            var appConfig = configuration.Get<AppConfig>() ?? throw new InvalidOperationException("AppConfig is not configured.");
            var provider = appConfig.Provider?.ToAiProvider() ?? AiProvider.Anthropic;

            switch (provider)
            {
                case AiProvider.Anthropic:
                    {
                        services.AddOptions<AnthropicConfig>()
                        .Bind(configuration)
                        .ValidateDataAnnotations()
                        .ValidateOnStart();

                        var googleGeminiConfig = configuration.Get<GoogleGeminiConfig>() ?? throw new InvalidOperationException("GoogleGeminiConfig is not configured.");
                        var anthropicConfig = configuration.Get<AnthropicConfig>() ?? throw new InvalidOperationException("AnthropicConfig is not configured.");
                        services.AddSingleton<IAiAgentFactory>(sp =>
                        {
                            var memoryStorage = sp.GetRequiredService<IMemoryStorage>();
                            return new AnthropicAgentFactory(anthropicConfig, new GoogleImagegen3AiImagePainter(googleGeminiConfig.ApiKey), memoryStorage);
                        });
                        break;
                    }

                case AiProvider.GoogleGemini:
                    {
                        services.AddOptions<GoogleGeminiConfig>()
                        .Bind(configuration)
                        .ValidateDataAnnotations()
                        .ValidateOnStart();

                        var googleGeminiConfig = configuration.Get<GoogleGeminiConfig>() ?? throw new InvalidOperationException("GoogleGeminiConfig is not configured.");
                        services.AddSingleton<IAiAgentFactory>(sp =>
                        {
                            var memoryStorage = sp.GetRequiredService<IMemoryStorage>();
                            return new GoogleGeminiAgentFactory(googleGeminiConfig, new GoogleImagegen3AiImagePainter(googleGeminiConfig.ApiKey), memoryStorage);
                        });
                        break;
                    }

                default:
                    throw new ArgumentException($"Unsupported AI provider: {provider}");
            }
        }

        private static void ConfigureCore(IServiceCollection services, IConfiguration configuration)
        {
            services.AddOptions<TelegramConfig>()
                .Bind(configuration)
                .ValidateDataAnnotations()
                .ValidateOnStart();

            services.AddSingleton(new ConcurrentDictionary<string, IAppVisitor>());
            services.AddSingleton(new ConcurrentDictionary<string, ConcurrentDictionary<string, ActionId>>());
            services.AddSingleton<IAdminChecker>(sp =>
            {
                var config = sp.GetRequiredService<IOptions<TelegramConfig>>().Value;
                return new AdminChecker(config.AdminUserId ?? string.Empty);
            });

            services.AddSingleton<IMessengerBotSource>(sp =>
            {
                var config = sp.GetRequiredService<IOptions<TelegramConfig>>().Value;
                return new TelegramBotSource(config.BotToken!);
            });
            services.AddSingleton<IMessenger, TelegramMessenger>(sp =>
            {
                var config = sp.GetRequiredService<IOptions<TelegramConfig>>().Value;
                var actionsMapping = sp.GetRequiredService<ConcurrentDictionary<string, ConcurrentDictionary<string, ActionId>>>();
                var telegramBotSource = sp.GetRequiredService<IMessengerBotSource>();
                return new TelegramMessenger(config, actionsMapping, telegramBotSource);
            });

            services.AddSingleton<IChatModeLoader, ChatModeLoader>();
            services.AddSingleton<CacheWithExpirationCallback>(sp => 
            {
                var logger = sp.GetRequiredService<ILogger>();
                return new CacheWithExpirationCallback(TimeSpan.FromMinutes(0.1), logger); 
            });

            services.AddSingleton<IChatFactory, ChatFactory>(sp =>
            {
                var appConfig = configuration.Get<AppConfig>() ?? throw new InvalidOperationException("AppConfig is not configured.");

                var modeLoader = sp.GetRequiredService<IChatModeLoader>();
                var aIAgentFactory = sp.GetRequiredService<IAiAgentFactory>();
                var messenger = sp.GetRequiredService<IMessenger>();
                var logger = sp.GetRequiredService<ILogger>();
                var cache = sp.GetRequiredService<CacheWithExpirationCallback>();

                return new ChatFactory(appConfig, modeLoader, aIAgentFactory, messenger, logger, cache);
            });

            services.AddSingleton<IChatCommandProcessor>(sp =>
            {
                var visitors = sp.GetRequiredService<ConcurrentDictionary<string, IAppVisitor>>();
                var chatModeLoader = sp.GetRequiredService<IChatModeLoader>();
                var memoryStorage = sp.GetRequiredService<IMemoryStorage>();
                var commands = new List<IChatCommand>
                {
                    new ReStart(),
                    new ShowVisitors(visitors),
                    new AddAccess(visitors),
                    new DelAccess(visitors),
                    new SetCommonMode(chatModeLoader),
                    new SetBaseMode(chatModeLoader),
                    new SetTrollMode(chatModeLoader),
                    new ClearDiary(memoryStorage)
                };
                var adminChecker = sp.GetRequiredService<IAdminChecker>();
                return new ChatCommandProcessor(commands, adminChecker);
            });

            services.AddSingleton(sp =>
            {
                var visitors = sp.GetRequiredService<ConcurrentDictionary<string, IAppVisitor>>();
                var adminChecker = sp.GetRequiredService<IAdminChecker>();
                var accessStorage = sp.GetRequiredService<IAccessStorage>();
                return new AccessChecker(adminChecker, visitors, accessStorage);
            });

            services.AddSingleton<IChatMessageProcessor, ChatMessageProcessor>();
            services.AddSingleton<IChatMessageActionProcessor>(sp =>
            {
                var actions = new List<IChatMessageAction>
                {
                    new CancelAction(),
                    new StopAction(),
                    new RegenerateAction(),
                    new ContinueAction(),
                    new RetryAction()
                };
                return new ChatMessageActionProcessor(actions);
            });

            services.AddSingleton<IChatMessageConverter>(sp =>
            {
                var settings = sp.GetRequiredService<IOptions<TelegramConfig>>().Value;
                var telegramBotSource = sp.GetRequiredService<IMessengerBotSource>();
                return new ChatMessageConverter(settings.BotToken!, telegramBotSource);
            });

            services.AddSingleton<IChatProcessor, ChatProcessor>();
        }
    }
}