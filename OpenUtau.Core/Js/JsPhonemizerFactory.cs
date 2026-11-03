using System;
using System.IO;
using System.Linq;
using System.Text;
using Jint;
using Jint.Native;
using OpenUtau.Api;
using Serilog;

namespace OpenUtau.Core.Js {
    /// <summary>
    /// Creates <see cref="JsPhonemizer"/>s for an oudep entry point with loader <see cref="LoaderName"/>.
    /// </summary>
    public class JsPhonemizerFactory : PhonemizerFactory {
        public const string LoaderName = "JsPhonemizer";

        public string packageId = string.Empty;
        public string packagePath = string.Empty;
        public string entrypointPath = string.Empty;

        public override string Id => $"js:{packageId}/{entrypointPath}";

        public override Phonemizer Create() {
            return new JsPhonemizer(this) {
                Name = name,
                Tag = tag,
                Language = language,
            };
        }

        /// <summary>
        /// Registers the JavaScript phonemizers of all installed packages, replacing the ones registered before.
        /// Call <see cref="PhonemizerFactory.BuildList"/> afterwards.
        /// </summary>
        public static void RegisterInstalled() {
            foreach (var factory in PhonemizerFactory.GetAll().OfType<JsPhonemizerFactory>()) {
                Unregister(factory.Id);
            }
            foreach (var ep in PackageManager.Inst.GetInstalledEntrypoints()) {
                if (ep.Entrypoint.loader != LoaderName) {
                    continue;
                }
                try {
                    var factory = Load(ep);
                    Register(factory);
                    Log.Information("Loaded JavaScript phonemizer {Factory} from {Id}", factory.ToString(), factory.Id);
                } catch (JsSandboxException e) {
                    // The script's own error, e.g. a syntax error. A .NET stack trace says nothing to its author.
                    Log.Error("Failed to load JavaScript phonemizer: {Message}", e.Message);
                } catch (Exception e) {
                    Log.Error(e, "Failed to load JavaScript phonemizer {Path} in package {Id}", ep.Entrypoint.path, ep.Package.id);
                }
            }
        }

        /// <summary>
        /// Evaluates the entry point once to read its exported info.
        /// </summary>
        public static JsPhonemizerFactory Load(PackageManager.InstalledEntrypoint ep) {
            var factory = new JsPhonemizerFactory {
                type = typeof(JsPhonemizer),
                packageId = ep.Package.id,
                packagePath = ep.PackagePath,
                entrypointPath = ep.Entrypoint.path.Replace('\\', '/'),
            };
            var phonemizer = new JsPhonemizer(factory);
            try {
                var info = phonemizer.LoadModule().Get("info");
                if (!info.IsObject()) {
                    throw new JsSandboxException($"{factory.Id}: does not export info.");
                }
                factory.name = GetString(info, "name", factory.Id, required: true)!;
                factory.tag = GetString(info, "tag", factory.Id, required: true)!;
                factory.author = GetString(info, "author", factory.Id, required: false)!;
                factory.language = GetString(info, "language", factory.Id, required: false)!;
            } finally {
                phonemizer.Unload();
            }
            return factory;
        }

        static readonly string[] TemplateFiles = { "oudep.yaml", "main.js", "openutau.d.ts", "jsconfig.json" };

        /// <summary>
        /// Creates a package with a starter JavaScript phonemizer in the Dependencies folder, from Js/Template.
        /// Returns the package folder.
        /// </summary>
        public static string CreatePackage(string id) {
            if (string.IsNullOrWhiteSpace(id) || !PackageManager.IsValidPackageName(id)) {
                throw new ArgumentException($"Invalid package id: \"{id}\"");
            }
            var dir = Path.Combine(PathManager.Inst.DependencyPath, id);
            if (Directory.Exists(dir)) {
                throw new IOException($"Package {id} already exists: {dir}");
            }
            Directory.CreateDirectory(dir);
            // E.g. "MY JA VCV" for "my_ja_vcv".
            var tag = id.ToUpperInvariant().Replace('_', ' ');
            var assembly = typeof(JsPhonemizerFactory).Assembly;
            foreach (var file in TemplateFiles) {
                using var stream = assembly.GetManifestResourceStream($"Js.Template.{file}")!;
                using var reader = new StreamReader(stream, Encoding.UTF8);
                var text = reader.ReadToEnd().Replace("{{id}}", id).Replace("{{tag}}", tag);
                File.WriteAllText(Path.Combine(dir, file), text, new UTF8Encoding(false));
            }
            return dir;
        }

        static string? GetString(JsValue info, string key, string id, bool required) {
            var value = info.AsObject().Get(key);
            if (value.IsString() && (!required || value.AsString().Length > 0)) {
                return value.AsString();
            }
            if (!required && value.IsUndefined()) {
                return null;
            }
            throw new JsSandboxException($"{id}: info.{key} must be a {(required ? "non-empty " : "")}string.");
        }
    }
}
