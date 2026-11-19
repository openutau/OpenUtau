using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Jint;
using Jint.Native;
using Jint.Native.Object;
using Jint.Runtime;
using Jint.Runtime.Interop;
using Serilog;

namespace OpenUtau.Core.Js {
    /// <summary>
    /// A JavaScript VM that runs one entry point module of an oudep package.
    ///
    /// The script has no access to the CLR or the network. It can import modules from its own
    /// package folder, plus the host modules added with <see cref="AddHostModule"/>, and the host
    /// can let it read package files with <see cref="ReadPackageText"/>. Each call into the script
    /// is limited in time, allocation and recursion depth.
    ///
    /// The VM is single-threaded: callers must not use it from two threads at once.
    /// </summary>
    public sealed class JsSandbox : IDisposable {
        public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
        public const long MemoryLimit = 256 * 1024 * 1024;
        public const int RecursionLimit = 256;
        public const long MaxReadSize = 64 * 1024 * 1024;

        private readonly Engine engine;
        private readonly string packagePath;
        private readonly string entrypointPath;

        /// <summary>A name for logs and errors, e.g. "package-id/entry.js".</summary>
        public string Name { get; }

        /// <param name="packagePath">The package folder. Scripts can only import modules from it.</param>
        /// <param name="entrypointPath">The entry point module, relative to the package folder.</param>
        /// <param name="name">A name for logs and errors.</param>
        public JsSandbox(string packagePath, string entrypointPath, string name) {
            this.packagePath = packagePath = Path.GetFullPath(packagePath);
            var fullPath = Path.GetFullPath(Path.Combine(packagePath, entrypointPath));
            if (!IsInPackage(fullPath)) {
                throw new ArgumentException($"Entry point {entrypointPath} is outside of package {packagePath}.");
            }
            if (!File.Exists(fullPath)) {
                throw new FileNotFoundException($"Entry point {entrypointPath} not found in package {packagePath}.", fullPath);
            }
            this.entrypointPath = "./" + Path.GetRelativePath(packagePath, fullPath).Replace('\\', '/');
            Name = name;
            engine = new Engine(options => {
                options.EnableModules(packagePath, restrictToBasePath: true);
                options.DisableStringCompilation();
                options.TimeoutInterval(Timeout);
                options.LimitMemory(MemoryLimit);
                options.LimitRecursion(RecursionLimit);
            });
            var console = NewObject();
            console.Set("log", Function("log", args => Log.Information("[{Name}] {Message}", Name, Format(args))));
            console.Set("info", Function("info", args => Log.Information("[{Name}] {Message}", Name, Format(args))));
            console.Set("warn", Function("warn", args => Log.Warning("[{Name}] {Message}", Name, Format(args))));
            console.Set("error", Function("error", args => Log.Error("[{Name}] {Message}", Name, Format(args))));
            engine.SetValue("console", console);
        }

        /// <summary>
        /// Adds a module the script can import by <paramref name="specifier"/>, e.g. <c>import { foo } from "openutau"</c>.
        /// Must be called before <see cref="Import"/>.
        /// </summary>
        public void AddHostModule(string specifier, IReadOnlyDictionary<string, JsValue> exports) {
            engine.Modules.Add(specifier, builder => {
                foreach (var (name, value) in exports) {
                    builder.ExportValue(name, value);
                }
            });
        }

        /// <summary>
        /// Loads and evaluates the entry point module. Returns its namespace object.
        /// </summary>
        public ObjectInstance Import() {
            return Run("load", () => engine.Modules.Import(entrypointPath));
        }

        /// <summary>
        /// Calls the function exported as <paramref name="exportName"/>.
        /// </summary>
        public JsValue Call(JsValue function, string exportName, params JsValue[] args) {
            return Run(exportName, () => engine.Call(function, JsValue.Undefined, args));
        }

        T Run<T>(string what, Func<T> func) {
            try {
                return func();
            } catch (JavaScriptException e) {
                throw new JsSandboxException(RelativeToPackage($"{Name}: {what}: {e.Message}\n{e.JavaScriptStackTrace}"), e);
            } catch (Exception e) when (e is TimeoutException || e is JintException) {
                throw new JsSandboxException(RelativeToPackage($"{Name}: {what}: {e.Message}"), e);
            }
        }

        /// <summary>
        /// Names files in a message relative to the package folder, as the script does, e.g. "main.js:35:36".
        /// </summary>
        string RelativeToPackage(string message) {
            return message.Replace(packagePath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Reads a text file of the package, for a host function. <paramref name="path"/> is relative
        /// to the package folder. The file is decoded as UTF-8, or by its byte order mark.
        /// Throws a JavaScript error the script can catch if the file can't be read.
        /// </summary>
        public string ReadPackageText(string path) {
            if (string.IsNullOrEmpty(path) || Path.IsPathRooted(path)) {
                throw new JavaScriptException(engine.Intrinsics.TypeError, $"Path must be relative to the package: \"{path}\"");
            }
            var fullPath = Path.GetFullPath(Path.Combine(packagePath, path));
            if (!IsInPackage(fullPath)) {
                throw new JavaScriptException(engine.Intrinsics.Error, $"Path is outside of the package: \"{path}\"");
            }
            if (!File.Exists(fullPath)) {
                throw new JavaScriptException(engine.Intrinsics.Error, $"File not found: \"{path}\"");
            }
            // A link inside the package could point anywhere.
            for (var p = fullPath; p.Length > packagePath.Length; p = Path.GetDirectoryName(p)!) {
                if (File.GetAttributes(p).HasFlag(FileAttributes.ReparsePoint)) {
                    throw new JavaScriptException(engine.Intrinsics.Error, $"Path is a link: \"{path}\"");
                }
            }
            if (new FileInfo(fullPath).Length > MaxReadSize) {
                throw new JavaScriptException(engine.Intrinsics.Error, $"File is larger than {MaxReadSize / 1024 / 1024} MB: \"{path}\"");
            }
            try {
                return File.ReadAllText(fullPath, Encoding.UTF8);
            } catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) {
                throw new JavaScriptException(engine.Intrinsics.Error, $"Failed to read \"{path}\": {e.Message}");
            }
        }

        bool IsInPackage(string fullPath) {
            return fullPath.StartsWith(packagePath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        public ObjectInstance NewObject() => new JsObject(engine);

        public JsArray NewArray(IEnumerable<JsValue> items) => new JsArray(engine, items.ToArray());

        public JsValue Function(string name, Func<JsValue[], JsValue> func) {
            return new ClrFunction(engine, name, (_, args) => func(args));
        }

        public JsValue Function(string name, Action<JsValue[]> action) {
            return new ClrFunction(engine, name, (_, args) => {
                action(args);
                return JsValue.Undefined;
            });
        }

        static string Format(JsValue[] args) => string.Join(" ", args.Select(arg => arg.IsString() ? arg.AsString() : arg.ToString()));

        public void Dispose() {
            engine.Dispose();
        }
    }

    public class JsSandboxException : Exception {
        public JsSandboxException(string message, Exception? inner = null) : base(message, inner) { }
    }
}
