using System;
using System.IO;
using System.Linq;
using Xunit;

namespace OpenUtau.Core.Format {
    public class TorchFileTest : IDisposable {
        // torch.save(torch.arange(20.) * 0.25 - 1 sliced [3:13] as [1, 1, 10]) to a file named
        // "_あ_hnsep": a view with a storage offset, under torch's "archive" name, with data
        // descriptors and zip64 records, as hifisampler's hnsep cache of a Japanese sample.
        const string TorchSavedView =
            "UEsDBAAACAgAAAAAAAAAAAAAAAAAAAAAAAAQABIAYXJjaGl2ZS9kYXRhLnBrbEZCDgBaWlpaWlpaWlpaWlpaWoACY3RvcmNoLl91dGlscwpfcmVidWlsZF90ZW5zb3JfdjIKcQAoKFgHAAAAc3RvcmFnZXEBY3RvcmNoCkZsb2F0U3RvcmFnZQpxAlgBAAAAMHEDWAMAAABjcHVxBEsUdHEFUUsDSwFLAUsKh3EGSwpLCksBh3EHiWNjb2xsZWN0aW9ucwpPcmRlcmVkRGljdApxCClScQl0cQpScQsuUEsHCGdglkWeAAAAngAAAFBLAwQAAAgIAAAAAAAAAAAAAAAAAAAAAAAAFwAdAGFyY2hpdmUvLmZvcm1hdF92ZXJzaW9uRkIZAFpaWlpaWlpaWlpaWlpaWlpaWlpaWlpaWloxUEsHCLfv3IMBAAAAAQAAAFBLAwQAAAgIAAAAAAAAAAAAAAAAAAAAAAAAGgA3AGFyY2hpdmUvLnN0b3JhZ2VfYWxpZ25tZW50RkIzAFpaWlpaWlpaWlpaWlpaWlpaWlpaWlpaWlpaWlpaWlpaWlpaWlpaWlpaWlpaWlpaWlpaWjY0UEsHCD93cekCAAAAAgAAAFBLAwQAAAgIAAAAAAAAAAAAAAAAAAAAAAAAEQA/AGFyY2hpdmUvYnl0ZW9yZGVyRkI7AFpaWlpaWlpaWlpaWlpaWlpaWlpaWlpaWlpaWlpaWlpaWlpaWlpaWlpaWlpaWlpaWlpaWlpaWlpaWlpabGl0dGxlUEsHCIU94xkGAAAABgAAAFBLAwQAAAgIAAAAAAAAAAAAAAAAAAAAAAAADgA+AGFyY2hpdmUvZGF0YS8wRkI6AFpaWlpaWlpaWlpaWlpaWlpaWlpaWlpaWlpaWlpaWlpaWlpaWlpaWlpaWlpaWlpaWlpaWlpaWlpaWloAAIC/AABAvwAAAL8AAIC+AAAAAAAAgD4AAAA/AABAPwAAgD8AAKA/AADAPwAA4D8AAABAAAAQQAAAIEAAADBAAABAQAAAUEAAAGBAAABwQFBLBwjQPFgQUAAAAFAAAABQSwMEAAAICAAAAAAAAAAAAAAAAAAAAAAAAA8AMwBhcmNoaXZlL3ZlcnNpb25GQi8AWlpaWlpaWlpaWlpaWlpaWlpaWlpaWlpaWlpaWlpaWlpaWlpaWlpaWlpaWlpaWlozClBLBwjRnmdVAgAAAAIAAABQSwMEAAAICAAAAAAAAAAAAAAAAAAAAAAAAB4AMgBhcmNoaXZlLy5kYXRhL3NlcmlhbGl6YXRpb25faWRGQi4AWlpaWlpaWlpaWlpaWlpaWlpaWlpaWlpaWlpaWlpaWlpaWlpaWlpaWlpaWlpaWjEwNTM0NjgwMjUwNDk4NDk3MjYzMDQzODQ2ODMyMjIwMzgyOTM5MzZQSwcIOJ9uUygAAAAoAAAAUEsBAgAAAAAICAAAAAAAAGdglkWeAAAAngAAABAAAAAAAAAAAAAAAAAAAAAAAGFyY2hpdmUvZGF0YS5wa2xQSwECAAAAAAgIAAAAAAAAt+/cgwEAAAABAAAAFwAAAAAAAAAAAAAAAADuAAAAYXJjaGl2ZS8uZm9ybWF0X3ZlcnNpb25QSwECAAAAAAgIAAAAAAAAP3dx6QIAAAACAAAAGgAAAAAAAAAAAAAAAABRAQAAYXJjaGl2ZS8uc3RvcmFnZV9hbGlnbm1lbnRQSwECAAAAAAgIAAAAAAAAhT3jGQYAAAAGAAAAEQAAAAAAAAAAAAAAAADSAQAAYXJjaGl2ZS9ieXRlb3JkZXJQSwECAAAAAAgIAAAAAAAA0DxYEFAAAABQAAAADgAAAAAAAAAAAAAAAABWAgAAYXJjaGl2ZS9kYXRhLzBQSwECAAAAAAgIAAAAAAAA0Z5nVQIAAAACAAAADwAAAAAAAAAAAAAAAAAgAwAAYXJjaGl2ZS92ZXJzaW9uUEsBAgAAAAAICAAAAAAAADifblMoAAAAKAAAAB4AAAAAAAAAAAAAAAAAkgMAAGFyY2hpdmUvLmRhdGEvc2VyaWFsaXphdGlvbl9pZFBLBgYsAAAAAAAAAB4DLQAAAAAAAAAAAAcAAAAAAAAABwAAAAAAAADPAQAAAAAAADgEAAAAAAAAUEsGBwAAAAAHBgAAAAAAAAEAAABQSwUGAAAAAAcABwDPAQAAOAQAAAAA";

        readonly string dir = Path.Combine(Path.GetTempPath(), "TorchFileTest-" + Guid.NewGuid());

        public TorchFileTest() {
            Directory.CreateDirectory(dir);
        }

        public void Dispose() {
            Directory.Delete(dir, true);
        }

        [Fact]
        public void ReadsTorchSavedView() {
            var path = Path.Combine(dir, "_あ_hnsep");
            File.WriteAllBytes(path, Convert.FromBase64String(TorchSavedView));
            var (data, shape) = TorchFile.ReadFloatTensor(path);
            Assert.Equal(new[] { 1, 1, 10 }, shape);
            Assert.Equal(Enumerable.Range(3, 10).Select(i => i * 0.25f - 1), data);
        }

        [Fact]
        public void RoundTrip() {
            var path = Path.Combine(dir, "x_hnsep");
            var data = Enumerable.Range(0, 70000).Select(i => (float)Math.Sin(i * 0.01)).ToArray();
            TorchFile.WriteFloatTensor(path, data, new[] { 1, 1, data.Length });
            var (read, shape) = TorchFile.ReadFloatTensor(path);
            Assert.Equal(new[] { 1, 1, data.Length }, shape);
            Assert.Equal(data, read);
        }

        [Fact]
        public void AlignsStoragesAs64Bytes() {
            var path = Path.Combine(dir, "x_hnsep");
            TorchFile.WriteFloatTensor(path, new float[] { 1, 2, 3 }, new[] { 1, 1, 3 });
            var bytes = File.ReadAllBytes(path);
            // The storage's local header: name "archive/data/0", then the data.
            var name = System.Text.Encoding.ASCII.GetBytes("archive/data/0");
            int at = bytes.AsSpan().IndexOf(name);
            int extraLength = BitConverter.ToUInt16(bytes, at - 2);
            Assert.Equal(0, (at + name.Length + extraLength) % 64);
        }
    }
}
