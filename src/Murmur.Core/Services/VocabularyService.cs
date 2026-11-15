using Murmur.Core.Models;

namespace Murmur.Core.Services;

/// <summary>
/// The vocabulary file, read once and then kept in memory: every dictation reads the vocabulary, and the file only
/// changes through this service (one Murmur runs at a time). A failed read is not kept, so the next call tries the file
/// again. An edit made to the file by hand while Murmur runs is not seen until Murmur restarts, and the next save
/// overwrites it.
/// </summary>
public sealed class VocabularyService(MurmurPaths paths) : IVocabularyService
{
    private readonly JsonFileStore<Vocabulary> _store = new(paths.VocabularyFile, Vocabulary.Empty);
    private readonly Lock _lock = new();

    // The vocabulary, or null until the file has been read.
    private Vocabulary? _vocabulary;

    public Vocabulary Load()
    {
        lock (_lock)
            return _vocabulary ??= Normalize(_store.Load());
    }

    public void Save(Vocabulary vocabulary)
    {
        lock (_lock)
        {
            _store.Save(vocabulary);
            _vocabulary = Normalize(vocabulary);
        }
    }

    /// <summary>
    /// A hand-edited file may omit either list, which deserializes to null rather than an empty list. A saved vocabulary
    /// is normalized too, so the kept copy matches what reading the file back would give.
    /// </summary>
    private static Vocabulary Normalize(Vocabulary vocabulary) => vocabulary with
    {
        HintWords = vocabulary.HintWords ?? [],
        Replacements = vocabulary.Replacements ?? [],
    };
}
