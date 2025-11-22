using System;

namespace ChatWithAI.Contracts.Model
{
    /// <summary>
    /// Unique identifier for a message in the model layer.
    /// This is independent of any messenger-specific IDs.
    /// </summary>
    public readonly struct ModelMessageId : IEquatable<ModelMessageId>
    {
        public Guid Value { get; }

        public ModelMessageId(Guid value)
        {
            Value = value;
        }

        public static ModelMessageId New() => new(Guid.NewGuid());
        public static ModelMessageId Empty => new(Guid.Empty);

        public bool IsEmpty => Value == Guid.Empty;

        public bool Equals(ModelMessageId other) => Value.Equals(other.Value);
        public override bool Equals(object? obj) => obj is ModelMessageId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public override string ToString() => Value.ToString();

        public static bool operator ==(ModelMessageId left, ModelMessageId right) => left.Equals(right);
        public static bool operator !=(ModelMessageId left, ModelMessageId right) => !left.Equals(right);
    }
}
