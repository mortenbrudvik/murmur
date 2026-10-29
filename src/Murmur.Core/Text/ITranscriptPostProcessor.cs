namespace Murmur.Core.Text;

/// <summary>
/// One stage of transcript post-processing. The controller runs stages in ascending <see cref="Order"/>;
/// a future AI rewrite mode registers a stage with an order above the vocabulary replacer's.
/// </summary>
public interface ITranscriptPostProcessor
{
    int Order { get; }

    string Process(string text);
}
