namespace ChatWithAI.Core.StateMachine
{
    
    public enum ChatState
    {
        WaitingForFirstMessage, // Initial state, waiting for user input
        WaitingForNewMessages,  // Chat has user messages, but waiting for new user input
        InitiateAIResponse,     // Available from WaitingForNewMessages
        Streaming,              // Available from WaitingForAIResponse, sucessful finished led to WaitingForNewMessages
        Error                   // Available from WaitingForAIResponse and Streaming 
    }
}
