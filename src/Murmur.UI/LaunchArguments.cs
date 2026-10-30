namespace Murmur.UI;

/// <summary>
/// What the command line asks of this launch. <c>--minimized</c> is what the Run key starts Murmur with at sign-in;
/// <see cref="RestartArguments"/> is what "Restart Murmur" starts the new instance with, so that it waits for the old
/// one and opens on Settings. Anything else is ignored: Murmur never refuses to start over its command line.
/// </summary>
/// <param name="Page">One of the web app's pages, spelled as it spells them (lower case), or null.</param>
internal sealed record LaunchArguments(bool Minimized, bool Restarted, string? Page)
{
    private static readonly string[] Pages = ["history", "vocabulary", "settings"];

    /// <summary>The arguments of the new instance that "Restart Murmur" starts.</summary>
    public static IReadOnlyList<string> RestartArguments { get; } = ["--restarted", "--page", "settings"];

    public static LaunchArguments Parse(IReadOnlyList<string> args)
    {
        var minimized = false;
        var restarted = false;
        string? page = null;

        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (Is(arg, "--minimized"))
            {
                minimized = true;
            }
            else if (Is(arg, "--restarted"))
            {
                restarted = true;
            }
            else if (Is(arg, "--page") && i + 1 < args.Count && FindPage(args[i + 1]) is { } known)
            {
                // Taken with its value only when that is a page, so a stray "--page" cannot swallow the flag after it.
                page = known;
                i++;
            }
        }

        return new LaunchArguments(minimized, restarted, page);
    }

    private static bool Is(string arg, string name) => string.Equals(arg, name, StringComparison.OrdinalIgnoreCase);

    private static string? FindPage(string value) =>
        Pages.FirstOrDefault(page => string.Equals(page, value, StringComparison.OrdinalIgnoreCase));
}
