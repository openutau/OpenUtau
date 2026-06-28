using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using Serilog;

namespace OpenUtau.Classic {
    public class PluginRunner {
        private readonly Action<ReplaceNoteEventArgs> OnReplaceNote;
        private readonly Action<PluginErrorEventArgs> OnError;
        private readonly PathManager PathManager;

        public static PluginRunner from(PathManager pathManager, DocManager docManager) {
            return new PluginRunner(pathManager, ReplaceNoteMethod(docManager), ShowErrorMessageMethod(docManager));
        }

        private static Action<ReplaceNoteEventArgs> ReplaceNoteMethod(DocManager docManager) {
            return new Action<ReplaceNoteEventArgs>((args) => {
                docManager.StartUndoGroup("command.batch.plugin");
                docManager.ExecuteCmd(new RemoveNoteCommand(args.Part, args.ToRemove));
                docManager.ExecuteCmd(new AddNoteCommand(args.Part, args.ToAdd));
                docManager.EndUndoGroup();
            });
        }

        private static Action<PluginErrorEventArgs> ShowErrorMessageMethod(DocManager docManager) {
            return new Action<PluginErrorEventArgs>((args) => {
                docManager.ExecuteCmd(new ErrorMessageNotification(args.Message, args.Exception));
            });
        }

        /// <summary>
        /// for test
        /// </summary>
        /// <param name="pathManager"></param>
        /// <param name="onReplaceNote"></param>
        /// <param name="onError"></param>
        public PluginRunner(PathManager pathManager, Action<ReplaceNoteEventArgs> onReplaceNote, Action<PluginErrorEventArgs> onError) {
            PathManager = pathManager;
            OnReplaceNote = onReplaceNote;
            OnError = onError;
        }

        public async Task Execute(UProject project, UVoicePart part, UNote? first, UNote? last, IPlugin plugin) {
            if (first == null || last == null) {
                return;
            }
            try {
                var tempFile = Path.Combine(PathManager.CachePath, "temp.tmp");
                var sequence = Ust.WritePlugin(project, part, first, last, tempFile, encoding: plugin.Encoding);
                byte[]? beforeHash = HashFile(tempFile);
                await plugin.Run(tempFile);
                byte[]? afterHash = HashFile(tempFile);
                if (beforeHash == null || afterHash == null || Enumerable.SequenceEqual(beforeHash, afterHash)) {
                    Log.Information("Legacy plugin temp file has not changed.");
                    return;
                }
                Log.Information("Legacy plugin temp file has changed.");
                
                var (toRemove, toAdd) = await Task.Run(() => 
                    Ust.ParsePlugin(project, part, first, last, sequence, tempFile, encoding: plugin.Encoding)
                );
                
                OnReplaceNote(new ReplaceNoteEventArgs(part, toRemove, toAdd));
            } catch (Exception e) {
                OnError(new PluginErrorEventArgs("Failed to execute plugin", e));
            }
        }


        private byte[]? HashFile(string filePath) {
            using (var md5 = MD5.Create()) {
                using (var stream = File.OpenRead(filePath)) {
                    return md5.ComputeHash(stream);
                }
            }
        }

        public class ReplaceNoteEventArgs : EventArgs {
            public readonly UVoicePart Part;
            public readonly List<UNote> ToRemove;
            public readonly List<UNote> ToAdd;

            public ReplaceNoteEventArgs(UVoicePart part, List<UNote> toRemove, List<UNote> toAdd) {
                Part = part;
                ToRemove = toRemove;
                ToAdd = toAdd;
            }
        }

        public class PluginErrorEventArgs : EventArgs {
            public readonly string Message;
            public readonly Exception Exception;

            public PluginErrorEventArgs(string message, Exception exception) {
                Exception = exception;
                Message = message;
            }
        }
    }
}
