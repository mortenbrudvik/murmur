using System.Text.RegularExpressions;
using Murmur.Core.Models;
using Murmur.Core.Services;

namespace Murmur.Core.Text;

/// <summary>Applies the owner's "spoken -> written" vocabulary rules to a transcript.</summary>
public sealed class VocabularyReplacer(IVocabularyService vocabulary) : ITranscriptPostProcessor
{
    public int Order => 100;

    public string Process(string text) => ApplyRules(text, vocabulary.Load().Replacements);

    /// <summary>
    /// Replaces whole words or phrases, case-insensitively. Spoken text is matched literally (never as a regex),
    /// the written text is inserted literally (no <c>$1</c> expansion), and longer phrases are applied first so
    /// "web view two" wins over "web view".
    /// </summary>
    public static string ApplyRules(string text, IReadOnlyList<ReplacementRule> rules)
    {
        foreach (var rule in rules
                     .Where(r => !string.IsNullOrWhiteSpace(r.Spoken))
                     .OrderByDescending(r => r.Spoken.Trim().Length))
        {
            var pattern = $@"(?<!\w){Regex.Escape(rule.Spoken.Trim())}(?!\w)";
            text = Regex.Replace(text, pattern, _ => rule.Written, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        return text;
    }
}
