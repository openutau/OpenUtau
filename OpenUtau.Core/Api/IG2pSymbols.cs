using static OpenUtau.Api.Phonemizer;
using static OpenUtau.Api.Phonemizer.Note;

namespace OpenUtau.Api {
    public interface IG2pSymbols {
         string[] GetSymbols(Note note);
    }

}
