using AgentLab.Constants;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace AgentLab;

internal sealed class AgentFlow
{
    private const int MaximumIterations = 12;
    private readonly IChatClient chatClient;
    private readonly ChatOptions baseChatOptions;

    public AgentFlow(IChatClient chatClient, ChatOptions options)
    {
        this.chatClient = chatClient;
        baseChatOptions = options;
    }

    internal async Task Run()
    {
        const string requirementsName = nameof(Personas.RequirementsEngineer);
        const string frontendName = nameof(Personas.FrontendDev);
        const string qualityAssuranceName = nameof(Personas.QualityAssurance);

        AIAgent requirementsAgent = CreateAgent(Personas.RequirementsEngineer, requirementsName);
        AIAgent frontEndAgent = CreateAgent(Personas.FrontendDev, frontendName);
        AIAgent qualityAssuranceAgent = CreateAgent(Personas.QualityAssurance, qualityAssuranceName);

        var workflow = AgentWorkflowBuilder
            .CreateGroupChatBuilderWith(agents =>
                new ApprovalGroupChatManager(agents, qualityAssuranceName)
                {
                    MaximumIterationCount = MaximumIterations
                })
            .AddParticipants(requirementsAgent, frontEndAgent, qualityAssuranceAgent)
            .Build();

        Console.WriteLine();
        Console.Write("## Me: ");
        string? userRequest = Console.ReadLine();
        Console.WriteLine();

        if (string.IsNullOrWhiteSpace(userRequest))
        {
            Console.WriteLine("## No request given, exiting.");
            return;
        }

        List<ChatMessage> messages = [new(ChatRole.User, userRequest)];

        await using StreamingRun run = await InProcessExecution.RunStreamingAsync(workflow, messages);
        await run.TrySendMessageAsync(new TurnToken(emitEvents: true));

        string? currentExecutor = null;
        List<ChatMessage>? finalHistory = null;

        await foreach (WorkflowEvent evt in run.WatchStreamAsync())
        {
            if (evt is AgentResponseUpdateEvent update)
            {
                if (IsNewSpeaker(currentExecutor, update))
                {
                    currentExecutor = update.ExecutorId;
                    Console.WriteLine();
                    Console.WriteLine();
                    Console.Write($"# {currentExecutor}: ");
                }

                foreach (ChatMessage message in update.AsResponse().Messages)
                {
                    Console.Write(message.Text);
                }
            }
            else if (evt is WorkflowOutputEvent output)
            {
                finalHistory = output.As<List<ChatMessage>>();
                break;
            }
        }

        Console.WriteLine();
        Console.WriteLine();
        bool approved = IsApprovedByQA(qualityAssuranceName, finalHistory);

        Console.WriteLine(approved
            ? "## END OF CHAT (approved by QA)"
            : $"## END OF CHAT (NOT approved, stopped after max {MaximumIterations} iterations or no output)");
    }

    private static bool IsNewSpeaker(string? currentExecutor, AgentResponseUpdateEvent update)
    {
        return update.ExecutorId != currentExecutor;
    }

    private static bool IsApprovedByQA(string qualityAssuranceName, List<ChatMessage>? finalHistory)
    {
        return finalHistory is not null
                    && ApprovalGroupChatManager.IsApproval(finalHistory.LastOrDefault(), qualityAssuranceName);
    }

    private AIAgent CreateAgent(string instructions, string name)
    {
        ChatOptions options = baseChatOptions.Clone();
        options.Instructions = instructions;

        return chatClient.AsAIAgent(new ChatClientAgentOptions
        {
            Name = name,
            ChatOptions = options
        });
    }
}