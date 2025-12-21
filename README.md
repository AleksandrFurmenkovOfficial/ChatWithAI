# ChatWithAI Telegram Bot

[![.NET Build Status](https://github.com/AleksandrFurmenkovOfficial/ChatWithAI/actions/workflows/dotnet.yml/badge.svg)](https://github.com/AleksandrFurmenkovOfficial/ChatWithAI/actions/workflows/dotnet.yml)
[![CodeQL Security Scan](https://github.com/AleksandrFurmenkovOfficial/ChatWithAI/actions/workflows/github-code-scanning/codeql/badge.svg)](https://github.com/AleksandrFurmenkovOfficial/ChatWithAI/actions/workflows/github-code-scanning/codeql)

## Overview

This repository hosts the source code for a versatile Telegram bot powered by large language models (LLMs). It allows users to interact with AI for various tasks, including image analysis, image creation, web searches for quick answers, and maintaining notes across conversations.

## Key Features

*   **🤖 AI Interaction:** Chat directly with powerful AI models.
*   **🖼️ Image Recognition:** Analyze images sent to the bot to understand their content.
*   **🎨 Image Generation:** Create images based on user descriptions or prompts.
*   **🌐 Internet Search:** Fetch answers to simple questions using web search capabilities.
*   **📝 Note-Taking:** A personal diary feature to save notes, reminders, or text snippets between chat sessions.
*   **⚙️ Multiple Modes:** Switch between different interaction modes like 'general', 'teacher', etc., to tailor the AI's responses.
*   **✨ Google Gemini Powered:** Uses Google's Gemini models for AI responses.

## Getting Started

Follow these steps to get your own instance of the bot running.

### Prerequisites

*   [.NET 8 SDK and Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/8.0): Ensure you have the necessary .NET version installed on your system.
*   **Telegram Bot Token:** Obtain a token from BotFather on Telegram.
*   **Google AI API Key:** Get an API key for Google's Gemini models.

### Installation

You can either build the bot from the source code or use pre-compiled binaries.

**Option 1: Build from Source**

1.  **Clone the Repository:**
    ```bash
    git clone https://github.com/AleksandrFurmenkovOfficial/ChatWithAI.git
    cd ChatWithAI
    ```
2.  **Build the Application:**
    ```bash
    dotnet build --configuration Release
    ```
    The executable will typically be found in `bin/Release/net8.0/`.

**Option 2: Use Pre-compiled Binaries**

1.  Download the latest release binaries from the [Releases](https://github.com/AleksandrFurmenkovOfficial/ChatWithAI/releases) page.
2.  Extract the downloaded archive.

### Configuration

Configure the bot using environment variables before running it.

**Core Settings:**

*   `TELEGRAM_BOT_KEY`: **(Required)** Your Telegram bot token obtained from BotFather.
*   `AI_PROVIDER`: **(Required)** Specify the AI provider. Set this to `"google"`.
*   `TELEGRAM_ADMIN_USER_ID`: (Optional) Your Telegram User ID for administrative commands or privileges.
*   `CHAT_CACHE_ALIVE_IN_MINUTES`: (Optional) Duration in minutes before a chat session context is reset for non-premium users. Defaults to `"5"`.

**AI Provider Settings (Google Gemini):**

*   `GOOGLE_API_KEY`: **(Required)** Your Google AI API key.
*   `GOOGLE_MODEL`: **(Required)** The model to use. Choose from `"gemini-3.0-pro-preview"` or `"gemini-3.0-flash-preview"`.
*   `GOOGLE_TEMPERATURE`: (Optional) Controls randomness (e.g., `"0.4"`). Default value might apply if unset.
*   `GOOGLE_MAX_TOKENS`: (Optional) Maximum tokens for the response (e.g., `"65536"`). Default value might apply if unset.

*Note: Only the Google provider is supported; configure the variables above to enable it.*

**Storage Paths:**

The application uses several folders for storing data. Default paths are relative to the executable's location, but can be overridden using environment variables.

*   **AI Memory (`MEMORY_FOLDER`)**
    *   **Purpose:** Stores conversation history and context for each user session.
    *   **Default Path:** `../AiMemory`
    *   **Permissions:** The application needs **read, write, and create file** permissions within this directory to manage session data.
    *   **Environment Variable:** `MEMORY_FOLDER`

*   **Modes (`MODES_FOLDER`)**
    *   **Purpose:** Contains definitions for the available AI interaction modes (e.g., 'teacher', 'translator'). This folder is treated as **read-only** by the application during runtime.
    *   **Default Path:** `Modes` (within the executable's directory)
    *   **Content:** Place plain text files (`.txt`) here, where each file's name represents a mode, and its content defines the initial prompt or instructions for that mode.
    *   **Implementation:** Ensure you have corresponding command classes (similar to `SetBaseMode.cs`) implemented to allow users to switch to these defined modes.
    *   **Environment Variable:** `MODES_FOLDER`

*   **Access Control (`ACCESS_FOLDER`)**
    *   **Purpose:** Manages user access permissions. This folder is treated as **read-only** by the application during runtime.
    *   **Default Path:** `../Access`
    *   **Content:**
        *   `ids.txt` (Optional): If present, contains a list of Telegram User IDs (one ID per line) allowed to interact with the bot. If this file doesn't exist or is empty, access might be open (depending on implementation).
        *   `premium_ids.txt` (Optional): If present, contains a list of Telegram User IDs (one ID per line) designated as premium users. For these users, the chat session cache (`CHAT_CACHE_ALIVE_IN_MINUTES`) is ignored, and the full conversation context is preserved indefinitely within the `MEMORY_FOLDER`.
    *   **Environment Variable:** `ACCESS_FOLDER`

*Make sure these folders exist with the correct structure and content before running the application, and ensure the application has the necessary permissions, especially for the `MEMORY_FOLDER`.*

### Running the Bot

1.  Set the environment variables defined in the Configuration section (Core, AI Provider, and optionally Storage Paths). How you set them depends on your operating system (e.g., using `export` on Linux/macOS, `set` or System Properties on Windows, or a `.env` file if supported by your setup).
2.  Ensure the directories specified by `MODES_FOLDER` and `ACCESS_FOLDER` exist and contain the necessary files (mode definitions, `ids.txt`, `premium_ids.txt` if used).
3.  Ensure the directory specified by `MEMORY_FOLDER` exists and the application has write permissions to it.
4.  Navigate to the directory containing the executable (either from your build output or the extracted binary).
5.  Run the application:
    ```bash
    # Replace 'YourExecutableName' with the actual name, e.g., ChatWithAI
    ./YourExecutableName
    ```
    On Windows, it might be:
    ```cmd
    YourExecutableName.exe
    ```
6.  Open Telegram and start chatting with your bot!

PS ChatWithAI.Plugins.Windows.ScreenshotCapture is a bonus for Windows users (^_^). It will be ignored for other platforms, feel free to remove it.
