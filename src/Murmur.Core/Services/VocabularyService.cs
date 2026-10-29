using Murmur.Core.Models;

namespace Murmur.Core.Services;

public sealed class VocabularyService(MurmurPaths paths) : IVocabularyService
{
    private readonly JsonFileStore<Vocabulary> _store = new(paths.VocabularyFile, Vocabulary.Empty);

    public Vocabulary Load() => _store.Load();

    public void Save(Vocabulary vocabulary) => _store.Save(vocabulary);
}
