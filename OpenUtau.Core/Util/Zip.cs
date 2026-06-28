using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using SharpCompress.Archives;
using SharpCompress.Readers;

namespace OpenUtau.Core.Util {
    public static class Zip {
        public static string[] ExtractText(byte[] data, string key) {
            using var stream = new MemoryStream(data);
            using var archive = ArchiveFactory.OpenArchive(stream, new ReaderOptions());
            return ExtractText(archive, key);
        }

        public static string[] ExtractText(string path, string key) {
            using var stream = File.OpenRead(path);
            using var archive = ArchiveFactory.OpenArchive(stream, new ReaderOptions());
            return ExtractText(archive, key);
        }

        public static string[] ExtractText(IArchive archive, string key) {
            var entry = archive.Entries.FirstOrDefault(e => e.Key == key);
            if (entry == null) {
                return null;
            }
            using var stream = entry.OpenEntryStream();
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var lines = new List<string>();
            while (!reader.EndOfStream) {
                lines.Add(reader.ReadLine());
            }
            return lines.ToArray();
        }

        public static byte[] ExtractBytes(byte[] data, string key) {
            using var stream = new MemoryStream(data);
            using var archive = ArchiveFactory.OpenArchive(stream, new ReaderOptions());
            return ExtractBytes(archive, key);
        }

        public static byte[] ExtractBytes(string path, string key) {
            using var stream = File.OpenRead(path);
            using var archive = ArchiveFactory.OpenArchive(stream, new ReaderOptions());
            return ExtractBytes(archive, key);
        }

        public static byte[] ExtractBytes(IArchive archive, string key) {
            var entry = archive.Entries.FirstOrDefault(e => e.Key == key);
            if (entry == null) {
                return null;
            }
            using var entryStream = entry.OpenEntryStream();
            using var memStream = new MemoryStream();
            entryStream.CopyTo(memStream);
            return memStream.ToArray();
        }
    }
}
