using System.Text.RegularExpressions;
using GhostLetters.Domain.Roles;

namespace GhostLetters.Infrastructure.Bots;

/// <summary>Only explicit first-person public statements, never verified against hidden roles.</summary>
public static class PublicRoleClaims
{
    private static readonly Regex Quoted = new(
        """«[^»]*(?:»|$)|"[^"]*(?:"|$)|“[^”]*(?:”|$)""",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly Regex Statement = new(
        @"^я\s+(?:[—–-]\s*)?(?<denial>не\s+)?(?<role>свидетель|эксперт)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    // Narrow grammar intentionally rejects questions, quotations, conditions and third-person reports.
    public static IReadOnlyDictionary<Guid, Role> Read(IEnumerable<(Guid Author, string Text)> messages)
    {
        var claims = new Dictionary<Guid, Role>();
        foreach (var (author, text) in messages)
        // Mask before sentence splitting: punctuation/newlines inside quotes must
        // not turn a quotation into the author's own declaration or retraction.
        foreach (var sentence in Quoted.Replace(text, "[цитата]").Split(['.', '!', '\n']))
        {
            var match = Statement.Match(sentence.Trim());
            if (!match.Success) continue;
            var role = match.Groups["role"].Value.Equals("свидетель", StringComparison.OrdinalIgnoreCase)
                ? Role.Witness : Role.Expert;
            if (!match.Groups["denial"].Success) claims[author] = role;
            else if (claims.GetValueOrDefault(author) == role) claims.Remove(author);
        }
        return claims;
    }
}
