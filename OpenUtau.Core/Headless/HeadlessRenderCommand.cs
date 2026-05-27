using System;
using System.Globalization;
using System.Linq;
using Serilog;

namespace OpenUtau.Core.Headless {
    public static class HeadlessRenderCommand {
        public static bool IsCommand(string[] args) {
            return args.Length > 0 &&
                string.Equals(args[0], "render", StringComparison.OrdinalIgnoreCase);
        }

        public static int Run(string[] args, string executableName = "OpenUtau") {
            if (args.Length == 0 || IsHelp(args[0])) {
                PrintUsage(args.Length > 0 && IsHelp(args[0]) ? Console.Out : Console.Error, executableName);
                return args.Length > 0 && IsHelp(args[0]) ? 0 : 2;
            }
            if (!string.Equals(args[0], "render", StringComparison.OrdinalIgnoreCase)) {
                Console.Error.WriteLine($"Unknown command: {args[0]}");
                PrintUsage(Console.Error, executableName);
                return 2;
            }
            if (args.Skip(1).Any(IsHelp)) {
                PrintUsage(Console.Out, executableName);
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
                PrintUsage(Console.Error, executableName);
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
                    case "--onnx-runner":
                        options.OnnxRunner = ParseOnnxRunner(value);
                        break;
                    case "--onnx-gpu":
                        options.OnnxGpu = ParseNonNegativeInt(name, value);
                        break;
                    case "--diffsinger-depth":
                        options.DiffSingerDepth = ParseNonNegativeDouble(name, value);
                        break;
                    case "--diffsinger-steps":
                        options.DiffSingerSteps = ParsePositiveInt(name, value);
                        break;
                    case "--diffsinger-variance-steps":
                        options.DiffSingerVarianceSteps = ParsePositiveInt(name, value);
                        break;
                    case "--diffsinger-pitch-steps":
                        options.DiffSingerPitchSteps = ParsePositiveInt(name, value);
                        break;
                    case "--diffsinger-tensor-cache":
                        options.DiffSingerTensorCache = ParseBool(name, value);
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

        private static string ParseOnnxRunner(string value) {
            var runner = OpenUtau.Core.Onnx.getRunnerOptions()
                .FirstOrDefault(option => string.Equals(option, value, StringComparison.OrdinalIgnoreCase));
            if (runner == null) {
                throw new CommandLineException(
                    $"Invalid value for --onnx-runner: {value}. Expected one of: {string.Join(", ", OpenUtau.Core.Onnx.getRunnerOptions())}");
            }
            return runner;
        }

        private static int ParsePositiveInt(string name, string value) {
            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var result) ||
                result <= 0) {
                throw new CommandLineException($"Invalid value for {name}: {value}. Expected a positive integer.");
            }
            return result;
        }

        private static int ParseNonNegativeInt(string name, string value) {
            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var result) ||
                result < 0) {
                throw new CommandLineException($"Invalid value for {name}: {value}. Expected a non-negative integer.");
            }
            return result;
        }

        private static double ParseNonNegativeDouble(string name, string value) {
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) ||
                result < 0) {
                throw new CommandLineException($"Invalid value for {name}: {value}. Expected a non-negative number.");
            }
            return result;
        }

        private static bool ParseBool(string name, string value) {
            if (bool.TryParse(value, out var result)) {
                return result;
            }
            if (value == "1") {
                return true;
            }
            if (value == "0") {
                return false;
            }
            throw new CommandLineException($"Invalid value for {name}: {value}. Expected true or false.");
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

        private static void PrintUsage(System.IO.TextWriter writer, string executableName) {
            writer.WriteLine("Usage:");
            writer.WriteLine($"  {executableName} render --input <project.ust|project.ustx> --output <output.wav> [options]");
            writer.WriteLine();
            writer.WriteLine("Options:");
            writer.WriteLine("  --singer <id-or-name>");
            writer.WriteLine("  --renderer <CLASSIC|WORLDLINE-R|WORLDLINE-R2|ENUNU|VOGEN|DIFFSINGER|VOICEVOX>");
            writer.WriteLine("  --phonemizer <name-or-type>");
            writer.WriteLine("  --resampler <name>");
            writer.WriteLine("  --wavtool <name>");
            writer.WriteLine("  --singers-path <path>");
            writer.WriteLine("  --onnx-runner <CPU|DirectML|CoreML|NNAPI>");
            writer.WriteLine("  --onnx-gpu <device-index>");
            writer.WriteLine("  --diffsinger-depth <value>");
            writer.WriteLine("  --diffsinger-steps <count>");
            writer.WriteLine("  --diffsinger-variance-steps <count>");
            writer.WriteLine("  --diffsinger-pitch-steps <count>");
            writer.WriteLine("  --diffsinger-tensor-cache <true|false>");
        }

        private sealed class CommandLineException : Exception {
            public CommandLineException(string message) : base(message) {
            }
        }
    }
}
