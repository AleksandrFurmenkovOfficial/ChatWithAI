using ChatWithAI.Core.AiFunctions;
using Newtonsoft.Json;

namespace ChatWithAI.Providers.Google
{
    internal sealed class GoogleGeminiFunctionsManager(IMemoryStorage memoryStorage) : AiFunctionsManager(memoryStorage)
    {
        private static readonly JsonSerializerSettings m_jsonSettings = new()
        {
            ContractResolver = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver(),
            NullValueHandling = NullValueHandling.Ignore
        };

        public override string Representation()
        {
            var functions = Functions.Values.Select(func => new
            {
                name = func.GetName(),
                description = func.GetDescription(),
                parameters = func.GetParameters().Count > 0 ? new
                {
                    type = "object",
                    properties = func.GetParameters().ToDictionary(
                        p => p.Name,
                        p => new
                        {
                            type = MapParamType(p.Type),
                            description = p.Description
                        }
                    ),
                    required = func.GetParameters()
                        .Where(p => p.IsRequired)
                        .Select(p => p.Name)
                        .ToList()
                } : null
            });

            return JsonConvert.SerializeObject(functions, m_jsonSettings);
        }

        public override Dictionary<string, string> ConvertParameters(string parameters)
        {
            try
            {
                return JsonConvert.DeserializeObject<Dictionary<string, string>>(parameters, m_jsonSettings) ?? [];
            }
            catch (JsonException)
            {
                throw new ArgumentException("Invalid parameters format. Must be a JSON object.");
            }
        }

        private static string MapParamType(ParamType type)
        {
            return type switch
            {
                ParamType.eString => "string",
                _ => "unknown"
            };
        }
    }
}
