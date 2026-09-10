using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using OpenUtau.Colors;
using OpenUtau.Core;
using Serilog;

namespace OpenUtau.App;

public interface IOudepLoader {
    void Load(string packagePath, OudepMetadata metadata, OudepEntrypoint entrypoint);
}

public static class OudepLoaderRegistry {
    private static readonly Dictionary<string, Func<IOudepLoader>> registry = new();

    public static void Register(string loaderName, Func<IOudepLoader> factory) {
        registry[loaderName] = factory;
    }

    public static IOudepLoader? Create(string loaderName) {
        return registry.TryGetValue(loaderName, out var f) ? f() : null;
    }

    public static void EnsureRegistered() {
        Register("ThemeLoader", () => new ThemeLoader());
    }

    public static async Task LoadAllAsync() {
        OudepLoaderRegistry.EnsureRegistered();
        CustomTheme.ClearPackageThemes();
        var entrypoints = await PackageManager.Inst.GetInstalledEntrypointsAsync();
        foreach (var ep in entrypoints) {
            var loader = OudepLoaderRegistry.Create(ep.Entrypoint.loader);
            if (loader == null) {
                Log.Warning("No loader registered for '{loader}' in package {id}", ep.Entrypoint.loader, ep.Package.id);
                continue;
            }
            try {
                loader.Load(ep.PackagePath, ep.Package, ep.Entrypoint);
            } catch (Exception e) {
                Log.Error(e, "Loader '{loader}' failed for entrypoint '{path}' in package {id}", ep.Entrypoint.loader, ep.Entrypoint.path, ep.Package.id);
            }
        }
    }
}

public class ThemeLoader : IOudepLoader {
    public void Load(string packagePath, OudepMetadata metadata, OudepEntrypoint entrypoint) {
        var yamlPath = Path.Combine(packagePath, entrypoint.path);
        if (!File.Exists(yamlPath)) {
            Log.Warning("Theme entrypoint not found: {path}", yamlPath);
            return;
        }

        try {
            var yaml = Yaml.DefaultDeserializer.Deserialize<CustomTheme.ThemeYaml>(
                File.ReadAllText(yamlPath, Encoding.UTF8));
            var displayName = ResolveUniqueName(yaml.Name, metadata.id);
            CustomTheme.Themes[displayName] = yamlPath;
            CustomTheme.MarkPackageTheme(displayName);
            yaml.Name = displayName;
            CustomTheme.Default = yaml;
        } catch (Exception e) {
            Log.Error(e, "Failed to load theme from {path}", yamlPath);
        }
    }

    private string ResolveUniqueName(string name, string packageId) {
        if (!CustomTheme.Themes.ContainsKey(name)) return name;
        var candidate = $"{name} ({packageId})";
        int i = 1;
        while (CustomTheme.Themes.ContainsKey(candidate)) {
            candidate = $"{name} ({packageId}) ({i++})";
        }
        return candidate;
    }
}
