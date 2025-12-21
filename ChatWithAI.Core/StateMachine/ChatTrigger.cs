namespace ChatWithAI.Core.StateMachine
{
    
    public enum ChatTrigger
    {
        // Global triggers (can be fired in any state)
        UserReset,           // From any state to WaitingForFirstMessage state;

        UserSetMode,         // From WaitingForFirstMessage to WaitingForFirstMessage;
                             // From WaitingForNewMessages to WaitingForNewMessages;
                             // From WaitingForAIResponse to WaitingForAIResponse;
                             // From Streaming to WaitingForAIResponse;
                             // From Error to Error

        UserAddMessages,     // From WaitingForFirstMessage to WaitingForNewMessages;
                             // From WaitingForNewMessages to WaitingForNewMessages;
                             // From WaitingForAIResponse to WaitingForAIResponse;
                             // From Streaming to WaitingForAIResponse;
                             // From Error to Error

        // Specific triggers
        UserRequestResponse, // Available from WaitingForNewMessages
        UserCancel,          // Available from WaitingForAIResponse
        UserStop,            // Available from Streaming
        UserRegenerate,      // Available from WaitingForNewMessages
        UserContinue,        // Available from WaitingForNewMessages
        AIProducedContent,   // Available from WaitingForAIResponse
        AIResponseFinished,  // Available from WaitingForAIResponse
        AIResponseError      // Available from WaitingForAIResponse and Streaming
    }
}
