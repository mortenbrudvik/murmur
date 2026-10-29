namespace Murmur.Core.Text;

/// <summary>Builds the Whisper initial prompt from the owner's hint words.</summary>
public static class PromptBuilder
{
    /// <summary>Upper bound on the prompt length in characters, including the closing period.</summary>
    public const int MaxLength = 800;

    /// <summary>
    /// Joins the distinct, trimmed hint words as "A, B, C." in the given order. Words that would push the prompt
    /// past <see cref="MaxLength"/> are left out whole; a word is never cut in half.
    /// </summary>
    public static string Build(IReadOnlyList<string> hintWords)
    {
        const string separator = ", ";
        const string terminator = ".";

        var included = new List<string>();
        var length = terminator.Length;

        foreach (var word in hintWords
                     .Select(w => w.Trim())
                     .Where(w => w.Length > 0)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var added = word.Length + (included.Count > 0 ? separator.Length : 0);
            if (length + added > MaxLength)
                break;

            included.Add(word);
            length += added;
        }

        return included.Count == 0 ? "" : string.Join(separator, included) + terminator;
    }
}
