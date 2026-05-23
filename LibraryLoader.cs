using System;
using System.IO;
using System.Runtime.InteropServices;
using Serilog;

namespace OpenUtau {
    internal sealed class LibraryLoader : IDisposable {
        private const int RTLD_NOW = 2;
        private readonly IntPtr _handle;
        private bool _disposed;

        public LibraryLoader(string path, string library) {
            Log.Information($"Is64BitOperatingSystem {Environment.Is64BitOperatingSystem} Is64BitProcess {Environment.Is64BitProcess}");
            if (OS.IsWindows()) {
                string attempt1 = Environment.Is64BitProcess ? "win-x64" : "win-x86";
                string attempt2 = Environment.Is64BitProcess ? "win-x86" : "win-x64";
                _handle = LoadLibrary(Path.Combine(path, attempt1, $"{library}.dll"));
                if (_handle == IntPtr.Zero) {
                    Log.Error($"Error loading {attempt1}: {Marshal.GetLastWin32Error()}");
                    _handle = LoadLibrary(Path.Combine(path, attempt2, $"{library}.dll"));
                }
                if (_handle == IntPtr.Zero) {
                    Log.Error($"Error loading {attempt2}: {Marshal.GetLastWin32Error()}");
                }
            } else if (OS.IsLinux()) {
                string lib = Path.Combine(path, "linux-x64", $"lib{library}.so");
                Log.Information($"Loading {lib}");
                _handle = dlopenLinux(lib, RTLD_NOW);
            } else if (OS.IsMacOS()) {
                string lib = Path.Combine(path, "osx-x64", $"lib{library}.dylib");
                Log.Information($"Loading {lib}");
                _handle = dlopen(lib, RTLD_NOW);
            } else {
                throw new NotSupportedException("Platform not supported.");
            }
            if (_handle == IntPtr.Zero) {
                throw new Exception($"Could not load libary {library}.");
            }
        }

        public TDelegate LoadFunc<TDelegate>(string name) {
            IntPtr ptr = IntPtr.Zero;
            if (OS.IsWindows()) {
                ptr = GetProcAddress(_handle, name);
            } else if (OS.IsMacOS()) {
                ptr = dlsym(_handle, name);
            } else {
                ptr = dlsymLinux(_handle, name);
            }
            if (_handle == IntPtr.Zero) {
                throw new Exception($"Could not load function name: {name}.");
            }
            return Marshal.GetDelegateForFunctionPointer<TDelegate>(ptr);
        }

        public void Dispose() {
            if (_disposed) {
                return;
            }
            if (OS.IsWindows()) {
                FreeLibrary(_handle);
            } else if (OS.IsMacOS()) {
                dlclose(_handle);
            } else {
                dlcloseLinux(_handle);
            }
            _disposed = true;
        }

        [DllImport("kernel32", SetLastError = true)] static extern IntPtr LoadLibrary(string fileName);
        [DllImport("kernel32")] static extern IntPtr GetProcAddress(IntPtr module, string procName);
        [DllImport("kernel32")] static extern int FreeLibrary(IntPtr module);
        [DllImport("libdl")] static extern IntPtr dlopen(string fileName, int flags);
        [DllImport("libdl")] static extern IntPtr dlsym(IntPtr handle, string symbol);
        [DllImport("libdl")] static extern int dlclose(IntPtr handle);
        [DllImport("libdl.so.2", EntryPoint = "dlopen")] static extern IntPtr dlopen2(string fileName, int flags);
        [DllImport("libdl.so.2", EntryPoint = "dlsym")] static extern IntPtr dlsym2(IntPtr handle, string symbol);
        [DllImport("libdl.so.2", EntryPoint = "dlclose")] static extern int dlclose2(IntPtr handle);

        static IntPtr dlopenLinux(string fileName, int flags) {
            try {
                return dlopen2(fileName, flags);
            } catch (DllNotFoundException) {
                return dlopen(fileName, flags);
            }
        }
        static IntPtr dlsymLinux(IntPtr handle, string symbol) {
            try {
                return dlsym2(handle, symbol);
            } catch (DllNotFoundException) {
                return dlsym(handle, symbol);
            }
        }
        static int dlcloseLinux(IntPtr handle) {
            try {
                return dlclose2(handle);
            } catch (DllNotFoundException) {
                return dlclose(handle);
            }
        }

        // https://stackoverflow.com/a/15608028
        public static bool IsManagedAssembly(string fileName) {
            using var fileStream = new FileStream(fileName, FileMode.Open, FileAccess.Read);
            using var binaryReader = new BinaryReader(fileStream);
            if (fileStream.Length < 64) {
                return false;
            }

            //PE Header starts @ 0x3C (60). Its a 4 byte header.
            fileStream.Position = 0x3C;
            uint peHeaderPointer = binaryReader.ReadUInt32();
            if (peHeaderPointer == 0) {
                peHeaderPointer = 0x80;
            }

            // Ensure there is at least enough room for the following structures:
            //     24 byte PE Signature & Header
            //     28 byte Standard Fields         (24 bytes for PE32+)
            //     68 byte NT Fields               (88 bytes for PE32+)
            // >= 128 byte Data Dictionary Table
            if (peHeaderPointer > fileStream.Length - 256) {
                return false;
            }

            // Check the PE signature.  Should equal 'PE\0\0'.
            fileStream.Position = peHeaderPointer;
            uint peHeaderSignature = binaryReader.ReadUInt32();
            if (peHeaderSignature != 0x00004550) {
                return false;
            }

            // skip over the PEHeader fields
            fileStream.Position += 20;

            const ushort PE32 = 0x10b;
            const ushort PE32Plus = 0x20b;

            // Read PE magic number from Standard Fields to determine format.
            var peFormat = binaryReader.ReadUInt16();
            if (peFormat != PE32 && peFormat != PE32Plus) {
                return false;
            }

            // Read the 15th Data Dictionary RVA field which contains the CLI header RVA.
            // When this is non-zero then the file contains CLI data otherwise not.
            ushort dataDictionaryStart = (ushort)(peHeaderPointer + (peFormat == PE32 ? 232 : 248));
            fileStream.Position = dataDictionaryStart;

            uint cliHeaderRva = binaryReader.ReadUInt32();
            if (cliHeaderRva == 0) {
                return false;
            }

            return true;
        }
    }
}
