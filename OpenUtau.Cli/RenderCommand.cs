using System;
using System.Collections.Generic;
using System.Linq;
using OpenUtau.Core.Headless;
using Serilog;

namespace OpenUtau.Cli {
    internal static class RenderCommand {
        public static int Run(string[] args) {
            if (args.Length == 0 || IsHelp(args[0])) {
                PrintUsage(args.Length > 0 && IsHelp(args[0]) ? Console.Out : Console.Error);
                return args.Length > 0 && IsHelp(args[0]) ? 0 : 2;
            }
            if (!string.Equals(args[0], "render", StringComparison.OrdinalIgnoreCase)) {
                Console.Error.WriteLine($"Unknown command: {args[0]}");
                PrintUsage(Console.Error);
                return 2;
            }
            if (args.Skip(1).Any(IsHelp)) {
                PrintUsage(Console.Out);
                return 0;
            }

            try {
                var (job, options) = ParseRenderArgs(args.Skip(1).ToArray());
                using var host = new HeadlessOpenUtauHost(options, Console.Out);
                var exitCode = host.Run(async () => {
                    await HeadlessRenderer.RenderOneAsync(job, host);
                    return 0;
                });
                Console.WriteLine($"Rendered to {job.OutputPath}");
                return exitCode;
            } catch (CommandLineException e) {
                Console.Error.WriteLine(e.Message);
                PrintUsage(Console.Error);
                return 2;
            } catch (HeadlessRenderException e) {
                Console.Error.WriteLine(e.Message);
                Log.Error(e, "Render command failed.");
                return 1;
            } catch (Exception e) {
                Console.Error.WriteLine(e.Message);
                Log.Error(e, "Render command failed unexpectedly.");
                return 1;
            }
        }

        private static (RenderJob job, HeadlessOpenUtauOptions options) ParseRenderArgs(string[] args) {
            var job = new RenderJob();
            var options = new HeadlessOpenUtauOptions();
            for (int i = 0; i < args.Length; i++) {
                var arg = args[i];
                var (name, value, consumedNext) = ReadOption(args, i);
                if (consumedNext) {
                    i++;
                }
                switch (name) {
                    case "--input":
                    case "-i":
                        job.InputPath = value;
                        break;
                    case "--output":
                    case "-o":
                        job.OutputPath = value;
                        break;
                    case "--singer":
                        job.Singer = value;
                        break;
                    case "--renderer":
                        job.Renderer = value;
                        break;
                    case "--phonemizer":
                        job.Phonemizer = value;
                        break;
                    case "--resampler":
                        job.Resampler = value;
                        break;
                    case "--wavtool":
                        job.Wavtool = value;
                        break;
                    case "--singers-path":
                        options.SingersPath = value;
                        break;
                    default:
                        throw new CommandLineException($"Unknown option: {name}");
                }
            }
            if (string.IsNullOrWhiteSpace(job.InputPath)) {
                throw new CommandLineException("Missing required option: --input");
            }
            if (string.IsNullOrWhiteSpace(job.OutputPath)) {
                throw new CommandLineException("Missing required option: --output");
            }
            return (job, options);
        }

        private static (string name, string value, bool consumedNext) ReadOption(string[] args, int index) {
            var arg = args[index];
            if (!arg.StartsWith("-")) {
                throw new CommandLineException($"Unexpected argument: {arg}");
            }
            var equals = arg.IndexOf('=');
            if (equals > 0) {
                var name = arg.Substring(0, equals);
                var value = arg.Substring(equals + 1);
                if (string.IsNullOrEmpty(value)) {
                    throw new CommandLineException($"Missing value for option: {name}");
                }
                return (name, value, false);
            }
            if (index + 1 >= args.Length || args[index + 1].StartsWith("-")) {
                throw new CommandLineException($"Missing value for option: {arg}");
            }
            return (arg, args[index + 1], true);
        }

        private static bool IsHelp(string arg) {
            return string.Equals(arg, "help", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(arg, "--help", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(arg, "-h", StringComparison.OrdinalIgnoreCase);
        }

        private static void PrintUsage(System.IO.TextWriter writer) {
            writer.WriteLine("Usage:");
            writer.WriteLine("  OpenUtau.Cli render --input <project.ust|project.ustx> --output <output.wav> [options]");
            writer.WriteLine();
            writer.WriteLine("Options:");
            writer.WriteLine("  --singer <id-or-name>");
            writer.WriteLine("  --renderer <CLASSIC|WORLDLINE-R|WORLDLINE-R2|ENUNU|VOGEN|DIFFSINGER|VOICEVOX>");
            writer.WriteLine("  --phonemizer <name-or-type>");
            writer.WriteLine("  --resampler <name>");
            writer.WriteLine("  --wavtool <name>");
            writer.WriteLine("  --singers-path <path>");
        }

        private sealed class CommandLineException : Exception {
            public CommandLineException(string message) : base(message) {
            }
        }
    }
}
