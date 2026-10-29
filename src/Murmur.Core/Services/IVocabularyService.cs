using Murmur.Core.Models;

namespace Murmur.Core.Services;

public interface IVocabularyService
{
    Vocabulary Load();
    void Save(Vocabulary vocabulary);
}
