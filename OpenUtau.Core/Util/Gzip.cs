using System;
using System.Collections.Generic;
using System.Text;

namespace OpenUtau.Core.Util {
    public static class Gzip
    {
        public static byte[] Compress(byte[] data) {
            using (var compressedStream = new System.IO.MemoryStream()) {
                using (var zipStream = new System.IO.Compression.GZipStream(compressedStream, System.IO.Compression.CompressionMode.Compress)) {
                    zipStream.Write(data, 0, data.Length);
                }
                return compressedStream.ToArray();
            }
        }

        public static byte[] Decompress(byte[] data) {
            using (var compressedStream = new System.IO.MemoryStream(data)) {
                using (var zipStream = new System.IO.Compression.GZipStream(compressedStream, System.IO.Compression.CompressionMode.Decompress)) {
                    using (var resultStream = new System.IO.MemoryStream()) {
                        zipStream.CopyTo(resultStream);
                        return resultStream.ToArray();
                    }
                }
            }
        }
    }
}
