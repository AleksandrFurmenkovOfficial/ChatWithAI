using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ChatWithAI.Contracts
{
    public enum ParamType
    {
        eUnknown,
        eString
    }

    public readonly struct Parameter(ParamType type, string name, string description, bool isRequired)
    {
        public readonly ParamType Type { get; } = type;
        public readonly string Name { get; } = name;
        public readonly string Description { get; } = description;
        public readonly bool IsRequired { get; } = isRequired;
    }

    public interface IAiFunction
    {
        string GetName();
        string GetDescription();
        List<Parameter> GetParameters();
        Task<AiFunctionResult> Execute(IAiAgent api, Dictionary<string, string> parameters, string userId, CancellationToken cancellationToken = default);
    }
}