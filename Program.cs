using System.ClientModel;
using AgentLab;
using Microsoft.Extensions.AI;
using OpenAI;

var chatClient = new OpenAIClient(
        new ApiKeyCredential("anyKey"),
        new OpenAIClientOptions { Endpoint = new Uri("http://192.168.50.3:11434/v1") })
    .GetChatClient("qwen3:32b")
    .AsIChatClient();

var options = new ChatOptions
{
    Temperature = 0.3f,
    MaxOutputTokens = 4096
};

var agentFlow = new AgentFlow(chatClient, options);
await agentFlow.Run();