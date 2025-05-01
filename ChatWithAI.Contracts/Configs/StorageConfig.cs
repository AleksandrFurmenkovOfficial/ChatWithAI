using Microsoft.Extensions.Configuration;


namespace ChatWithAI.Contracts.Configs
{
    public class StorageConfig
    {
        [ConfigurationKeyName("MEMORY_FOLDER")]
        public string MemoryFolder { get; set; } = "../AiMemory";

        [ConfigurationKeyName("MODES_FOLDER")]
        public string ModesFolder { get; set; } = "Modes";

        [ConfigurationKeyName("ACCESS_FOLDER")]
        public string AccessFolder { get; set; } = "../Access";
    }
}
