namespace ChatWithAI.Core
{
    public class ChatEvent(string Id, Func<CancellationToken, Task> ExecutableEvent)
    {
        public string Id { get; } = Id;
        public Func<CancellationToken, Task> ExecutableAction { get; } = ExecutableEvent;
    }
}