﻿namespace ChatWithAI.Contracts
{
    public class ChatMode
    {
        public ChatMode()
        {
            AiName = "";
            AiSettings = "";
        }

        public string AiName { get; set; }
        public string AiSettings { get; set; }
    }
}