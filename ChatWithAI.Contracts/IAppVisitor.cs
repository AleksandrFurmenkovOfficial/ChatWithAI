using System;

namespace ChatWithAI.Contracts
{
    public interface IAppVisitor
    {
        string Name { get; }
        bool Access { get; set; }
        public DateTime LatestAccess { get; set; }
    }
}