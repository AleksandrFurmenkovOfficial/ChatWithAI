namespace ChatWithAI.Core.ChatCommands
{
    public sealed class AppVisitor(bool access, string name, DateTime latestAccess) : IAppVisitor
    {
        public string Name { get; } = name;
        public bool Access { get; set; } = access;
        public DateTime LatestAccess { get; set; } = latestAccess;
    }
}