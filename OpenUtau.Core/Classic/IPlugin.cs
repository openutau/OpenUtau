using System.Threading.Tasks;

namespace OpenUtau.Classic {
    public interface IPlugin {
        string Encoding { get; }
        Task Run(string tempFile);
    }
}
