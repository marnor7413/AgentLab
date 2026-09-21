using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace AgentLab;

/// <summary>
/// Round-robin-manager som avslutar när godkännaren skriver exakt <see cref="ApprovalToken"/>
/// som sista icke-tomma rad i sitt svar. Ersätter ApprovalTerminationStrategy från Semantic Kernel.
/// </summary>
internal sealed class ApprovalGroupChatManager : RoundRobinGroupChatManager
{
    internal const string ApprovalToken = "[[APPROVED]]";

    private const string ThinkEndTag = "</think>";

    private readonly string approverName;

    public ApprovalGroupChatManager(IReadOnlyList<AIAgent> agents, string approverName)
        : base(agents)
    {
        this.approverName = approverName;
    }

    protected override async ValueTask<bool> ShouldTerminateAsync(
        IReadOnlyList<ChatMessage> history,
        CancellationToken cancellationToken = default)
    {
        // Behåll basklassens beteende (t.ex. iterationsgränsen) och lägg godkännandet ovanpå.
        if (await base.ShouldTerminateAsync(history, cancellationToken).ConfigureAwait(false))
        {
            return true;
        }

        return IsApproval(history.LastOrDefault(), approverName);
    }

    internal static bool IsApproval(ChatMessage? message, string approverName)
    {
        if (message is null ||
            message.Role != ChatRole.Assistant ||
            !string.Equals(message.AuthorName, approverName, StringComparison.Ordinal))
        {
            return false;
        }

        string text = message.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        // qwen3 kan, beroende på Ollama-version, lägga resonemanget i ett <think>-block i själva texten.
        int thinkEnd = text.LastIndexOf(ThinkEndTag, StringComparison.Ordinal);
        string answer = thinkEnd >= 0 ? text[(thinkEnd + ThinkEndTag.Length)..] : text;

        string? lastLine = answer
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .LastOrDefault();

        return string.Equals(lastLine, ApprovalToken, StringComparison.Ordinal);
    }
}