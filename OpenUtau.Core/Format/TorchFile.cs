using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace OpenUtau.Core.Format {
    /// <summary>
    /// One float tensor in PyTorch's torch.save format: a stored zip with a protocol 2 pickle
    /// (data.pkl) that rebuilds the tensor from a raw little-endian storage (data/0). Reads
    /// what torch.save writes, including views with a storage offset and strides, and writes
    /// files torch.load (weights_only too) accepts.
    /// </summary>
    public static class TorchFile {
        const int Alignment = 64;

        /// <summary>Reads a float32, float16 or float64 tensor as float32, row-major, with its shape.</summary>
        public static (float[] data, int[] shape) ReadFloatTensor(string path) {
            using var zip = ZipFile.OpenRead(path);
            var pklEntry = zip.Entries.FirstOrDefault(e => e.FullName.EndsWith("/data.pkl"))
                ?? throw new InvalidDataException("No data.pkl in the torch file.");
            string prefix = pklEntry.FullName[..^"data.pkl".Length];
            var byteOrder = zip.GetEntry(prefix + "byteorder");
            if (byteOrder != null && ReadAll(byteOrder) is var bo && Encoding.ASCII.GetString(bo).Trim() != "little") {
                throw new InvalidDataException("Big-endian torch files are not supported.");
            }
            var tensor = new PickleReader(ReadAll(pklEntry)).Load() as TensorRef
                ?? throw new InvalidDataException("The torch file does not hold a single tensor.");
            var storageEntry = zip.GetEntry(prefix + "data/" + tensor.Storage.Key)
                ?? throw new InvalidDataException($"Missing storage {tensor.Storage.Key}.");
            var bytes = ReadAll(storageEntry);
            Func<long, float> element = tensor.Storage.Type switch {
                "FloatStorage" => i => BitConverter.ToSingle(bytes, checked((int)(i * 4))),
                "HalfStorage" => i => (float)BitConverter.ToHalf(bytes, checked((int)(i * 2))),
                "DoubleStorage" => i => (float)BitConverter.ToDouble(bytes, checked((int)(i * 8))),
                _ => throw new InvalidDataException($"Unsupported storage {tensor.Storage.Type}."),
            };
            var shape = tensor.Size.Select(s => checked((int)s)).ToArray();
            long count = shape.Aggregate(1L, (a, b) => a * b);
            var data = new float[count];
            var index = new long[shape.Length];
            for (long n = 0; n < count; n++) {
                long position = tensor.Offset;
                for (int d = 0; d < shape.Length; d++) {
                    position += index[d] * tensor.Stride[d];
                }
                data[n] = element(position);
                for (int d = shape.Length - 1; d >= 0; d--) {
                    if (++index[d] < shape[d]) {
                        break;
                    }
                    index[d] = 0;
                }
            }
            return (data, shape);
        }

        /// <summary>Writes a contiguous float32 tensor of the given shape, as torch.save does.</summary>
        public static void WriteFloatTensor(string path, float[] data, int[] shape) {
            if (shape.Aggregate(1L, (a, b) => a * b) != data.Length) {
                throw new ArgumentException("Shape does not match the data length.");
            }
            var stride = new long[shape.Length];
            long s = 1;
            for (int d = shape.Length - 1; d >= 0; d--) {
                stride[d] = s;
                s *= shape[d];
            }
            var storage = new byte[data.Length * 4];
            Buffer.BlockCopy(data, 0, storage, 0, storage.Length);
            if (!BitConverter.IsLittleEndian) {
                throw new PlatformNotSupportedException("Torch files are written little-endian.");
            }
            var serializationId = string.Concat(Enumerable.Range(0, 40).Select(_ => (char)('0' + Random.Shared.Next(10))));
            // torch.save's records, in its order, under its fallback archive name.
            var records = new List<(string, byte[])> {
                ("archive/data.pkl", TensorPickle(data.Length, shape, stride)),
                ("archive/.format_version", Encoding.ASCII.GetBytes("1")),
                ("archive/.storage_alignment", Encoding.ASCII.GetBytes(Alignment.ToString())),
                ("archive/byteorder", Encoding.ASCII.GetBytes("little")),
                ("archive/data/0", storage),
                ("archive/version", Encoding.ASCII.GetBytes("3\n")),
                ("archive/.data/serialization_id", Encoding.ASCII.GetBytes(serializationId)),
            };
            using var stream = File.Create(path);
            WriteStoredZip(stream, records);
        }

        static byte[] ReadAll(ZipArchiveEntry entry) {
            using var stream = entry.Open();
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            return memory.ToArray();
        }

        /// <summary>The pickle torch.save writes for a contiguous float tensor (protocol 2).</summary>
        static byte[] TensorPickle(long numel, int[] shape, long[] stride) {
            var p = new MemoryStream();
            int memo = 0;
            void Op(byte b) => p.WriteByte(b);
            void Put() {
                if (memo < 256) {
                    Op((byte)'q');
                    Op((byte)memo);
                } else {
                    Op((byte)'r');
                    p.Write(BitConverter.GetBytes(memo));
                }
                memo++;
            }
            void Global(string module, string name) {
                Op((byte)'c');
                p.Write(Encoding.ASCII.GetBytes(module + "\n" + name + "\n"));
                Put();
            }
            void Unicode(string value) {
                var bytes = Encoding.UTF8.GetBytes(value);
                Op((byte)'X');
                p.Write(BitConverter.GetBytes(bytes.Length));
                p.Write(bytes);
                Put();
            }
            void Int(long value) {
                if (value >= 0 && value < 256) {
                    Op((byte)'K');
                    Op((byte)value);
                } else if (value >= 0 && value < 65536) {
                    Op((byte)'M');
                    p.Write(BitConverter.GetBytes((ushort)value));
                } else if (value >= int.MinValue && value <= int.MaxValue) {
                    Op((byte)'J');
                    p.Write(BitConverter.GetBytes((int)value));
                } else {
                    Op(0x8a);  // LONG1
                    Op(8);
                    p.Write(BitConverter.GetBytes(value));
                }
            }
            void Tuple(IEnumerable<long> values) {
                Op((byte)'(');
                foreach (var v in values) {
                    Int(v);
                }
                Op((byte)'t');
                Put();
            }

            Op(0x80);
            Op(2);
            Global("torch._utils", "_rebuild_tensor_v2");
            Op((byte)'(');
            Op((byte)'(');
            Unicode("storage");
            Global("torch", "FloatStorage");
            Unicode("0");
            Unicode("cpu");
            Int(numel);
            Op((byte)'t');
            Put();
            Op((byte)'Q');  // BINPERSID
            Int(0);  // storage offset
            Tuple(shape.Select(v => (long)v));
            Tuple(stride);
            Op(0x89);  // requires_grad False
            Global("collections", "OrderedDict");
            Op((byte)')');
            Op((byte)'R');
            Put();
            Op((byte)'t');
            Put();
            Op((byte)'R');
            Put();
            Op((byte)'.');
            return p.ToArray();
        }

        /// <summary>A zip of stored (uncompressed) records, each record's data 64-byte aligned as torch does.</summary>
        static void WriteStoredZip(Stream stream, List<(string name, byte[] data)> records) {
            var w = new BinaryWriter(stream);
            var central = new List<(byte[] name, uint crc, int size, long offset)>();
            foreach (var (name, data) in records) {
                var nameBytes = Encoding.UTF8.GetBytes(name);
                long offset = stream.Position;
                long dataStart = offset + 30 + nameBytes.Length;
                int pad = (int)((Alignment - dataStart % Alignment) % Alignment);
                if (pad > 0 && pad < 4) {
                    pad += Alignment;
                }
                uint crc = Crc32(data);
                w.Write(0x04034b50u);
                w.Write((ushort)20);  // version needed
                w.Write((ushort)0);  // flags
                w.Write((ushort)0);  // stored
                w.Write((ushort)0);  // time
                w.Write((ushort)0);  // date
                w.Write(crc);
                w.Write(data.Length);
                w.Write(data.Length);
                w.Write((ushort)nameBytes.Length);
                w.Write((ushort)pad);
                w.Write(nameBytes);
                if (pad > 0) {
                    // An "FB" extra field of zeros, as torch pads.
                    w.Write((byte)'F');
                    w.Write((byte)'B');
                    w.Write((ushort)(pad - 4));
                    w.Write(new byte[pad - 4]);
                }
                w.Write(data);
                central.Add((nameBytes, crc, data.Length, offset));
            }
            long centralStart = stream.Position;
            foreach (var (name, crc, size, offset) in central) {
                w.Write(0x02014b50u);
                w.Write((ushort)20);  // version made by
                w.Write((ushort)20);  // version needed
                w.Write((ushort)0);
                w.Write((ushort)0);
                w.Write((ushort)0);
                w.Write((ushort)0);
                w.Write(crc);
                w.Write(size);
                w.Write(size);
                w.Write((ushort)name.Length);
                w.Write((ushort)0);  // extra
                w.Write((ushort)0);  // comment
                w.Write((ushort)0);  // disk
                w.Write((ushort)0);  // internal attributes
                w.Write(0u);  // external attributes
                w.Write(checked((uint)offset));
                w.Write(name);
            }
            long centralEnd = stream.Position;
            w.Write(0x06054b50u);
            w.Write((ushort)0);
            w.Write((ushort)0);
            w.Write((ushort)central.Count);
            w.Write((ushort)central.Count);
            w.Write(checked((uint)(centralEnd - centralStart)));
            w.Write(checked((uint)centralStart));
            w.Write((ushort)0);
            w.Flush();
        }

        static readonly uint[] crcTable = Enumerable.Range(0, 256).Select(n => {
            uint c = (uint)n;
            for (int k = 0; k < 8; k++) {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }
            return c;
        }).ToArray();

        static uint Crc32(byte[] data) {
            uint c = 0xFFFFFFFFu;
            foreach (byte b in data) {
                c = crcTable[(c ^ b) & 0xFF] ^ (c >> 8);
            }
            return c ^ 0xFFFFFFFFu;
        }

        sealed class StorageRef {
            public string Type = "";
            public string Key = "";
        }

        sealed class TensorRef {
            public StorageRef Storage = new StorageRef();
            public long Offset;
            public long[] Size = Array.Empty<long>();
            public long[] Stride = Array.Empty<long>();
        }

        sealed record GlobalRef(string Module, string Name);

        /// <summary>
        /// The subset of the pickle machine torch.save output uses: tensors rebuilt by
        /// _rebuild_tensor_v2 from persistent storages, with tuples, ints, strings and an
        /// empty OrderedDict of backward hooks. Anything else is rejected.
        /// </summary>
        sealed class PickleReader {
            static readonly object Mark = new object();
            readonly byte[] b;
            int pos;
            readonly List<object?> stack = new List<object?>();
            readonly Dictionary<long, object?> memo = new Dictionary<long, object?>();

            public PickleReader(byte[] bytes) {
                b = bytes;
            }

            byte Byte() => b[pos++];
            int Int32() { int v = BitConverter.ToInt32(b, pos); pos += 4; return v; }
            string Line() {
                int end = Array.IndexOf(b, (byte)'\n', pos);
                var s = Encoding.ASCII.GetString(b, pos, end - pos);
                pos = end + 1;
                return s;
            }
            object? Pop() { var v = stack[^1]; stack.RemoveAt(stack.Count - 1); return v; }
            List<object?> PopMark() {
                int i = stack.LastIndexOf(Mark);
                var items = stack.GetRange(i + 1, stack.Count - i - 1);
                stack.RemoveRange(i, stack.Count - i);
                return items;
            }

            public object? Load() {
                while (true) {
                    byte op = Byte();
                    switch (op) {
                        case 0x80: Byte(); break;  // PROTO
                        case 0x95: pos += 8; break;  // FRAME
                        case (byte)'c': {
                                string module = Line();
                                stack.Add(new GlobalRef(module, Line()));
                                break;
                            }
                        case (byte)'q': memo[Byte()] = stack[^1]; break;
                        case (byte)'r': memo[Int32()] = stack[^1]; break;
                        case 0x94: memo[memo.Count] = stack[^1]; break;  // MEMOIZE
                        case (byte)'h': stack.Add(memo[Byte()]); break;
                        case (byte)'j': stack.Add(memo[Int32()]); break;
                        case (byte)'(': stack.Add(Mark); break;
                        case (byte)'X': { int n = Int32(); stack.Add(Encoding.UTF8.GetString(b, pos, n)); pos += n; break; }
                        case 0x8c: { int n = Byte(); stack.Add(Encoding.UTF8.GetString(b, pos, n)); pos += n; break; }
                        case (byte)'K': stack.Add((long)Byte()); break;
                        case (byte)'M': stack.Add((long)BitConverter.ToUInt16(b, pos)); pos += 2; break;
                        case (byte)'J': stack.Add((long)Int32()); break;
                        case 0x8a: {
                                int n = Byte();
                                long v = 0;
                                for (int i = 0; i < n; i++) {
                                    v |= (long)b[pos + i] << (8 * i);
                                }
                                if (n > 0 && n < 8 && (b[pos + n - 1] & 0x80) != 0) {
                                    v -= 1L << (8 * n);
                                }
                                pos += n;
                                stack.Add(v);
                                break;
                            }
                        case (byte)')': stack.Add(Array.Empty<object?>()); break;
                        case (byte)'t': stack.Add(PopMark().ToArray()); break;
                        case 0x85: stack.Add(new[] { Pop() }); break;
                        case 0x86: { var y = Pop(); stack.Add(new[] { Pop(), y }); break; }
                        case 0x87: { var z = Pop(); var y = Pop(); stack.Add(new[] { Pop(), y, z }); break; }
                        case 0x88: stack.Add(true); break;
                        case 0x89: stack.Add(false); break;
                        case (byte)'N': stack.Add(null); break;
                        case (byte)'}': stack.Add(new Dictionary<object, object?>()); break;
                        case (byte)'Q': stack.Add(PersistentLoad(Pop())); break;
                        case (byte)'R': {
                                var args = Pop() as object?[] ?? throw new InvalidDataException("REDUCE without a tuple.");
                                stack.Add(Reduce(Pop(), args));
                                break;
                            }
                        case (byte)'.': return Pop();
                        default:
                            throw new InvalidDataException($"Unsupported pickle opcode 0x{op:x2}.");
                    }
                }
            }

            static StorageRef PersistentLoad(object? pid) {
                if (pid is object?[] t && t.Length >= 3 && t[0] is "storage" && t[1] is GlobalRef type && t[2] is string key) {
                    return new StorageRef { Type = type.Name, Key = key };
                }
                throw new InvalidDataException("Unsupported persistent id.");
            }

            static object Reduce(object? callable, object?[] args) {
                switch (callable) {
                    case GlobalRef { Module: "torch._utils", Name: "_rebuild_tensor_v2" }
                            when args.Length >= 4 && args[0] is StorageRef storage && args[1] is long offset
                            && args[2] is object?[] size && args[3] is object?[] stride:
                        return new TensorRef {
                            Storage = storage,
                            Offset = offset,
                            Size = size.Select(v => (long)v!).ToArray(),
                            Stride = stride.Select(v => (long)v!).ToArray(),
                        };
                    case GlobalRef { Module: "collections", Name: "OrderedDict" }:
                        return new Dictionary<object, object?>();
                    default:
                        throw new InvalidDataException($"Unsupported pickle call {callable}.");
                }
            }
        }
    }
}
