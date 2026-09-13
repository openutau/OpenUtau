using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenUtau.Core {
    public static class Json {

        public static readonly JsonSerializerOptions DefaultOptions = new() {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            IncludeFields = true
        };

        public static readonly JsonSerializerOptions WriteIndentedOptions = new() {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            IncludeFields = true,
            WriteIndented = true
        };

        public static string Serialize(object? graph, JsonSerializerOptions? options = null) {
            return JsonSerializer.Serialize(graph, options ?? DefaultOptions);
        }

        public static T? Deserialize<T>(string input, JsonSerializerOptions? options = null) {
            return JsonSerializer.Deserialize<T>(input, options ?? DefaultOptions);
        }

        public static T? Deserialize<T>(Stream input, JsonSerializerOptions? options = null) {
            return JsonSerializer.Deserialize<T>(input, options ?? DefaultOptions);
        }

        public static T? Deserialize<T>(JsonNode? input, JsonSerializerOptions? options = null) {
            return input.Deserialize<T>(options ?? DefaultOptions);
        }

    }
}