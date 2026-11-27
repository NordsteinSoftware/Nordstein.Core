namespace Nordstein.Core.AI.Serialization;

/// <summary>
/// Reads the answer out of a model's raw text reply. Models wrap an answer in a Markdown code fence,
/// and reasoning models put a <c>&lt;think&gt;</c> block in front of it; these helpers remove both so
/// a parser sees only the answer.
/// </summary>
/// <remarks>
/// Pure and thread-safe. Both helpers are ordinal and culture-independent, and neither parses the
/// answer: they only cut the wrapper off.
/// </remarks>
public static class ModelReplyExtensions
{
    private const string Fence = "```";
    private const string ThinkOpen = "<think>";
    private const string ThinkClose = "</think>";

    /// <summary>
    /// Removes one surrounding Markdown code fence, with its optional language tag.
    /// </summary>
    /// <remarks>
    /// The fence is recognised when the reply, ignoring surrounding whitespace, starts with three
    /// backticks. The rest of that first line (the language tag) goes with it; when the reply has no
    /// line break, only the three backticks go. A closing fence at the end (ignoring trailing
    /// whitespace) is removed too, an unclosed fence is tolerated, and a fence inside the body stays.
    /// The result is not trimmed otherwise: the line break before a closing fence remains, so callers
    /// that need a trimmed answer call <see cref="string.Trim()"/> on it. A reply that does not start
    /// with a fence is returned exactly as given, whitespace included.
    /// </remarks>
    /// <param name="reply">The raw model reply.</param>
    /// <returns>The reply without its surrounding fence, or <paramref name="reply"/> itself when it is not fenced.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="reply"/> is <see langword="null"/>.</exception>
    public static string StripCodeFence(this string reply)
    {
        ArgumentNullException.ThrowIfNull(reply);

        string trimmed = reply.Trim();
        if (!trimmed.StartsWith(Fence, StringComparison.Ordinal))
        {
            return reply;
        }

        int lineBreak = trimmed.IndexOf('\n', StringComparison.Ordinal);
        string body = lineBreak < 0 ? trimmed[Fence.Length..] : trimmed[(lineBreak + 1)..];
        body = body.TrimEnd();
        return body.EndsWith(Fence, StringComparison.Ordinal) ? body[..^Fence.Length] : body;
    }

    /// <summary>
    /// Removes a leading <c>&lt;think&gt;…&lt;/think&gt;</c> block and the whitespace around it, as
    /// vLLM and Ollama return the reasoning of a reasoning model inside the reply content.
    /// </summary>
    /// <remarks>
    /// A reply with no <c>&lt;think&gt;</c> at all but a <c>&lt;/think&gt;</c> (the chat template put
    /// the opening tag in the prompt) loses everything up to and including the first
    /// <c>&lt;/think&gt;</c>. Any other reply is returned unchanged, including one whose block is not
    /// leading. Only the first block goes. The reasoning may quote text the answer is checked
    /// against, so callers must never read it as the answer; an unclosed block is therefore reported,
    /// not guessed at.
    /// </remarks>
    /// <param name="reply">The raw model reply.</param>
    /// <param name="answer">
    /// The reply without its reasoning. When the method returns <see langword="false"/> it is
    /// <see cref="string.Empty"/>, so the reasoning cannot be mistaken for the answer.
    /// </param>
    /// <returns>
    /// <see langword="false"/> when the reply opens a <c>&lt;think&gt;</c> block it never closes;
    /// otherwise <see langword="true"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="reply"/> is <see langword="null"/>.</exception>
    public static bool TryStripReasoning(this string reply, out string answer)
    {
        ArgumentNullException.ThrowIfNull(reply);

        string trimmed = reply.TrimStart();
        if (!trimmed.StartsWith(ThinkOpen, StringComparison.Ordinal))
        {
            int orphanClose = reply.Contains(ThinkOpen, StringComparison.Ordinal)
                ? -1
                : reply.IndexOf(ThinkClose, StringComparison.Ordinal);
            answer = orphanClose < 0 ? reply : reply[(orphanClose + ThinkClose.Length)..].TrimStart();
            return true;
        }

        int close = trimmed.IndexOf(ThinkClose, ThinkOpen.Length, StringComparison.Ordinal);
        if (close < 0)
        {
            answer = string.Empty;
            return false;
        }

        answer = trimmed[(close + ThinkClose.Length)..].TrimStart();
        return true;
    }
}
