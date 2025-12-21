namespace ChatWithAI.Contracts
{
    public sealed class AppVisitor(bool access, string name, System.DateTime latestAccess)
    {
        public string Name { get; } = name;
        public bool Access { get; set; } = access;
        public System.DateTime LatestAccess { get; set; } = latestAccess;
    }
}