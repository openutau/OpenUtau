using System;
using System.Collections.Generic;
using System.Linq;
using OpenUtau.App.ViewModels;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace OpenUtau.ViewModels {
    public class CurveCopyEvent { }
    public class CurveSelectionEvent {
        public readonly CurveSelection selection;
        public CurveSelectionEvent(CurveSelection selection) {
            this.selection = selection;
        }
    }
    public enum CurveTools {
        CurveSelectTool,
        CurvePenTool,
        //CurveLineTool,
        CurveEraserTool
    }

    public class CurveViewModel : ViewModelBase, ICmdSubscriber {
        [Reactive] public int CurveToolIndex { get; set; } = 1;

        public CurveTools CurveTool => (CurveTools)CurveToolIndex;
        public bool IsSelected(string abbr) => selection.HasValue(abbr);
        private CurveSelection selection = new CurveSelection();

        public CurveViewModel() {
            DocManager.Inst.AddSubscriber(this);
            MessageBus.Current.Listen<NotesSelectionEvent>()
                .Subscribe(e => {
                    if (e.selectedNotes.Length > 0) {
                        ClearSelect();
                    }
                });
        }

        public void Select(UExpressionDescriptor descriptor, int startTick, int endTick, UCurve? curve) {
            selection.Clear();
            (int x, int y) startPoint = (startTick, curve?.Sample(startTick) ?? (int)descriptor.CustomDefaultValue);
            (int x, int y) endPoint = (endTick, curve?.Sample(endTick) ?? (int)descriptor.CustomDefaultValue);
            var xs = new List<int>();
            var ys = new List<int>();
            if (curve != null) {
                for (int i = 0; i < curve.xs.Count; i++) {
                    var x = curve.xs[i];
                    if (endTick < x) {
                        break;
                    }
                    if (startTick <= x) {
                        xs.Add(x);
                        ys.Add(curve.ys[i]);
                    }
                }
            }
            selection.Add(descriptor.abbr, startPoint, endPoint, xs, ys);
            MessageBus.Current.SendMessage(new CurveSelectionEvent(selection));
        }

        public void ClearSelect() {
            if (selection.HasValue()) {
                selection.Clear();
                MessageBus.Current.SendMessage(new CurveSelectionEvent(selection));
            }
        }

        public void Copy(UVoicePart part) {
            if (part != null && selection.HasValue()) {
                DocManager.Inst.CurvesClipboard = selection.Clone();
                MessageBus.Current.SendMessage(new CurveCopyEvent());
            }
        }

        public void Cut(UVoicePart part) {
            if (part != null && selection.HasValue()) {
                DocManager.Inst.CurvesClipboard = selection.Clone();
                MessageBus.Current.SendMessage(new CurveCopyEvent());

                DocManager.Inst.StartUndoGroup("command.exp.reset");
                DocManager.Inst.ExecuteCmd(new PasteCurveCommand(DocManager.Inst.Project, part, selection.Abbr!,
                    selection.StartPoint.x, selection.StartPoint.y,
                    selection.EndPoint.x, selection.EndPoint.y));
                DocManager.Inst.EndUndoGroup();
            }
        }

        public void Paste(UVoicePart part, UExpressionDescriptor descriptor) {
            var paste = DocManager.Inst.CurvesClipboard;
            if (part == null || paste == null || !paste.HasValue(descriptor.abbr)) return;
            ClearSelect();

            var playPosi = Math.Max(0, DocManager.Inst.playPosTick - part.position);
            var curve = part.curves.FirstOrDefault(c => c.abbr == descriptor.abbr);
            paste.GetSelectedRange(descriptor.abbr, out var xs, out var ys);
            int diffTick = playPosi - paste.StartPoint.x;
            xs = xs.Select(x => x + diffTick).ToList();

            DocManager.Inst.StartUndoGroup("command.exp.paste");
            DocManager.Inst.ExecuteCmd(new PasteCurveCommand(DocManager.Inst.Project, part, paste.Abbr!, xs, ys));
            DocManager.Inst.EndUndoGroup();
        }

        public void OnNext(UCommand cmd, bool isUndo) {
            if (cmd is UNotification notif) {
                if (cmd is LoadPartNotification || cmd is LoadProjectNotification || cmd is SelectExpressionNotification) {
                    ClearSelect();
                }
            }
        }
    }
}
