using System;

namespace ChatWithAI.Contracts
{
    public readonly struct MessageId(string value) : IEquatable<MessageId>
    {
        public readonly string Value { get; } = value;

        public bool Equals(MessageId other)
        {
            return Value == other.Value;
        }

        public static bool operator ==(MessageId left, MessageId right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(MessageId left, MessageId right)
        {
            return !(left == right);
        }

        public override int GetHashCode()
        {
            return Value.GetHashCode();
        }

        public override bool Equals(object? obj) => obj is MessageId id && Equals(id);
    }
}