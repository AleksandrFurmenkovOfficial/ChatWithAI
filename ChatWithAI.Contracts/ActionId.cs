namespace ChatWithAI.Contracts
{
    public readonly struct ActionId(string name)
    {
        public readonly string Name { get; } = name;
    }
}