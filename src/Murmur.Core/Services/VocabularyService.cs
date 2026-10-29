using Murmur.Core.Models;

namespace Murmur.Core.Services;

public sealed class VocabularyService(MurmurPaths paths) : IVocabularyService
{
    private readonly JsonFileStore<Vocabulary> _store = new(paths.VocabularyFile, Vocabulary.Empty);

    public Vocabulary Load()
    {
        var vocabulary = _store.Load();

        // A hand-edited file may omit either key, which deserializes to null rather than an empty list.
        return vocabulary with
        {
            HintWords = vocabulary.HintWords ?? [],
            Replacements = vocabulary.Replacements ?? [],
        };
    }

    public void Save(Vocabulary vocabulary) => _store.Save(vocabulary);
}
