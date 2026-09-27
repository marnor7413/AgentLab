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

        long totalInputTokenCount = 0;
        long totalCchedInputTokenCount = 0;
        long totalOutputTokenCount = 0;
        long totalReasoningTokenCount = 0;
        ConsoleColor defaultColor = Console.ForegroundColor;

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
                var response = update.AsResponse();
                if(response is not null && response.Usage is UsageDetails usage)
                {
                    Console.WriteLine();
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("Usage:");
                    Console.WriteLine($"- Input Tokens: {usage.InputTokenCount}");
                    Console.WriteLine($"- Cached Tokens: {usage.CachedInputTokenCount ?? 0}");
                    Console.WriteLine($"- Output Tokens: {usage.OutputTokenCount} " + $"({usage.ReasoningTokenCount ?? 0} being reasoning Tokens)");
                    Console.ForegroundColor = defaultColor;

                    totalInputTokenCount += usage.InputTokenCount ?? 0;
                    totalCchedInputTokenCount += usage.CachedInputTokenCount ?? 0;
                    totalOutputTokenCount += usage.OutputTokenCount ?? 0;
                    totalReasoningTokenCount += usage.ReasoningTokenCount ?? 0;
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
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("Usage:");
        Console.WriteLine($"- TOTAL input Tokens: {totalInputTokenCount}");
        Console.WriteLine($"- TOTAL cached Tokens: {totalCchedInputTokenCount}");
        Console.WriteLine($"- TOTAL output Tokens: {totalOutputTokenCount} " + $"({totalReasoningTokenCount} being reasoning Tokens)");
        Console.ForegroundColor = defaultColor;

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