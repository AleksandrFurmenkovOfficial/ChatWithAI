using ChatWithAI.Contracts.Configs;
using ChatWithAI.Core.ChatCommands;
using ChatWithAI.Core.ChatMessageActions;
using ChatWithAI.Plugins.Windows.ScreenshotCapture;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Net;
using TelegramChatGPT.Implementation;

namespace ChatWithAI.DependencyInjection
{
    public static class DependencyInjectionExtensions
    {
        public static IServiceCollection AddApplicationDependencies(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            services.AddHttpClient("telegram_bot_client")
                .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
                {
                    PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                    PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
                    MaxConnectionsPerServer = 150,
                    KeepAlivePingPolicy = HttpKeepAlivePingPolicy.WithActiveRequests,
                    KeepAlivePingDelay = TimeSpan.FromSeconds(30),
                    KeepAlivePingTimeout = TimeSpan.FromSeconds(30),
                    AutomaticDecompression = DecompressionMethods.All,
                    ConnectTimeout = TimeSpan.FromSeconds(30)
                });

            services.AddHttpClient("google_gemini_client")
                .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
                {
                    PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                    PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
                    MaxConnectionsPerServer = 50,
                    KeepAlivePingPolicy = HttpKeepAlivePingPolicy.WithActiveRequests,
                    KeepAlivePingDelay = TimeSpan.FromSeconds(30),
                    KeepAlivePingTimeout = TimeSpan.FromSeconds(30),
                    AutomaticDecompression = DecompressionMethods.All,
                    ConnectTimeout = TimeSpan.FromSeconds(30),
                    ResponseDrainTimeout = TimeSpan.FromMinutes(5)
                });

            services.AddHttpClient();

            services.AddSingleton<IContentHelper, ContentHelper>();
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
                case AiProvider.GoogleGemini:
                    {
                        services.AddOptions<GeminiConfig>()
                        .Bind(configuration)
                        .ValidateDataAnnotations()
                        .ValidateOnStart();

                        var googleGeminiConfig = configuration.Get<GeminiConfig>() ?? throw new InvalidOperationException("GoogleGeminiConfig is not configured.");
                        services.AddSingleton<IAiAgentFactory>(sp =>
                        {
                            var memoryStorage = sp.GetRequiredService<IMemoryStorage>();
                            var logger = sp.GetRequiredService<ILogger>();
                            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
                            return new GeminiAgentFactory(googleGeminiConfig, new Imagegen4AiImagePainter(googleGeminiConfig.ApiKey, httpClientFactory), memoryStorage, httpClientFactory, logger);
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

            services.AddSingleton(new ConcurrentDictionary<string, AppVisitor>());
            services.AddSingleton(new ConcurrentDictionary<string, ConcurrentDictionary<string, ActionId>>());
            services.AddSingleton<IAdminChecker>(sp =>
            {
                var config = sp.GetRequiredService<IOptions<TelegramConfig>>().Value;
                return new AdminChecker(config.AdminUserId ?? string.Empty);
            });

            services.AddSingleton<IMessengerBotSource>(sp =>
            {
                var config = sp.GetRequiredService<IOptions<TelegramConfig>>().Value;
                var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
                return new TelegramBotSource(config.BotToken!, httpClientFactory);
            });
            services.AddSingleton<IMessenger, TelegramMessenger>(sp =>
            {
                var config = sp.GetRequiredService<IOptions<TelegramConfig>>().Value;
                var actionsMappingByChat = sp.GetRequiredService<ConcurrentDictionary<string, ConcurrentDictionary<string, ActionId>>>();
                var telegramBotSource = sp.GetRequiredService<IMessengerBotSource>();
                var contentHelper = sp.GetRequiredService<IContentHelper>();
                return new TelegramMessenger(config, actionsMappingByChat, telegramBotSource, contentHelper);
            });

            services.AddSingleton<IChatModeLoader, ChatModeLoader>();
            services.AddSingleton<ChatCache>(sp =>
            {
                var logger = sp.GetRequiredService<ILogger>();
                return new ChatCache(TimeSpan.FromMinutes(0.2), logger);
            });

            services.AddSingleton<IChatFactory, ChatFactory>(sp =>
            {
                var appConfig = configuration.Get<AppConfig>() ?? throw new InvalidOperationException("AppConfig is not configured.");

                var aIAgentFactory = sp.GetRequiredService<IAiAgentFactory>();
                var messenger = sp.GetRequiredService<IMessenger>();
                var logger = sp.GetRequiredService<ILogger>();
                var cache = sp.GetRequiredService<ChatCache>();

                return new ChatFactory(appConfig, aIAgentFactory, messenger, logger, cache);
            });

            services.AddSingleton(sp =>
            {
                var visitors = sp.GetRequiredService<ConcurrentDictionary<string, AppVisitor>>();
                var adminChecker = sp.GetRequiredService<IAdminChecker>();
                var accessStorage = sp.GetRequiredService<IAccessStorage>();
                return new AccessChecker(adminChecker, visitors, accessStorage);
            });

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
                var contentHelper = sp.GetRequiredService<IContentHelper>();
                return new TelegramChatMessageConverter(settings.BotToken!, telegramBotSource, contentHelper);
            });

            services.AddSingleton<ChatEventSource>(sp =>
            {
                var actionsMappingByChat = sp.GetRequiredService<ConcurrentDictionary<string, ConcurrentDictionary<string, ActionId>>>();
                var telegramBotSource = sp.GetRequiredService<IMessengerBotSource>();
                var chatMessageConverter = sp.GetRequiredService<IChatMessageConverter>();
                var adminChecker = sp.GetRequiredService<IAdminChecker>();
                var cache = sp.GetRequiredService<ChatCache>();
                var logger = sp.GetRequiredService<ILogger>();

                var visitors = sp.GetRequiredService<ConcurrentDictionary<string, AppVisitor>>();
                var chatModeLoader = sp.GetRequiredService<IChatModeLoader>();
                var memoryStorage = sp.GetRequiredService<IMemoryStorage>();
                var messenger = sp.GetRequiredService<IMessenger>();
                var commands = new List<IChatCommand>
                {
                    new ReStart(chatModeLoader),
                    new AppShowVisitors(visitors, messenger),
                    new AppAddAccess(visitors, messenger),
                    new AppDelAccess(visitors, messenger),
                    new SetCommonMode(chatModeLoader, messenger),
                    new SetGrammarMode(chatModeLoader, messenger),
                    new SetBaseMode(chatModeLoader, messenger),
                    new SetTrollMode(chatModeLoader, messenger),
                    new SetPhotoEditorMode(chatModeLoader, messenger),
                    new ClearDiary(memoryStorage, messenger),
                    new Help(messenger),
                    new ShowDiary(memoryStorage, messenger),
                    new SetDocsMode(chatModeLoader, messenger),
                };

                return new ChatEventSource(
                    commands,
                    actionsMappingByChat,
                    telegramBotSource,
                    chatMessageConverter,
                    adminChecker,
                    cache,
                    logger);
            });

            services.AddSingleton<AccessChecker>();
            services.AddSingleton<IChatActionEventSource>(sp => sp.GetRequiredService<ChatEventSource>());
            services.AddSingleton<IChatCommandEventSource>(sp => sp.GetRequiredService<ChatEventSource>());
            services.AddSingleton<IChatMessageEventSource>(sp => sp.GetRequiredService<ChatEventSource>());
            services.AddSingleton<IChatExpireEventSource>(sp => sp.GetRequiredService<ChatEventSource>());

            AddWindowsScreenshotCapture(services);

            services.AddSingleton<IChatProcessor, ChatEventBatcher>();
        }

        public static IServiceCollection AddWindowsScreenshotCapture(this IServiceCollection services)
        {
            //            if (OperatingSystem.IsWindows())
            //            {
            //#pragma warning disable CA1416 // Validate platform compatibility
            //                services.AddSingleton<WindowsHotKeyService>(sp =>
            //                {
            //                    var config = sp.GetRequiredService<IOptions<TelegramConfig>>().Value;
            //                    var logger = sp.GetRequiredService<ILogger>();
            //                    return new WindowsHotKeyService(config.AdminUserId ?? string.Empty, logger);
            //                });
            //
            //                services.AddSingleton<IChatCtrlCEventSource>(sp => sp.GetRequiredService<WindowsHotKeyService>());
            //                services.AddSingleton<IChatCtrlVEventSource>(sp => sp.GetRequiredService<WindowsHotKeyService>());
            //                services.AddSingleton<IScreenshotProvider, WindowsScreenshotService>();
            //#pragma warning restore CA1416 // Validate platform compatibility
            //            }
            //            else
            {
                services.AddSingleton<WindowsHotKeyServiceStub>();
                services.AddSingleton<IChatCtrlCEventSource>(sp => sp.GetRequiredService<WindowsHotKeyServiceStub>());
                services.AddSingleton<IChatCtrlVEventSource>(sp => sp.GetRequiredService<WindowsHotKeyServiceStub>());
                services.AddSingleton<IScreenshotProvider, WindowsScreenshotServiceStub>();
            }

            return services;
        }
    }
}