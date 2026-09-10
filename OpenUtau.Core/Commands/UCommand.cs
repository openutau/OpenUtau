using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenUtau.Core {
    public abstract class UCommand {
        public virtual bool Silent => false;
        public virtual ValidateOptions ValidateOptions => default;
        /// <summary>
        /// The blast radius of this command, driving fine-grained snapshot
        /// invalidation. The default is conservative: project-wide.
        /// </summary>
        public virtual Pipeline.ImpactSet Impact => Pipeline.ImpactSet.All;
        public abstract void Execute();
        public abstract void Unexecute();
        public virtual bool CanMerge(IList<UCommand> commands) => false;
        public virtual UCommand Merge(IList<UCommand> commands) => throw new NotImplementedException();
        public abstract override string ToString();
    }

    public class UCommandGroup {
        public string? NameKey;
        public bool DeferValidate;
        public List<UCommand> Commands;
        public UCommandGroup(string? nameKey, bool deferValidate) {
            NameKey = nameKey;
            DeferValidate = deferValidate;
            Commands = new List<UCommand>();
        }
        public void Merge() {
            if (Commands.Count > 0 && Commands.Last().CanMerge(Commands)) {
                var merged = Commands.Last().Merge(Commands);
                Commands.Clear();
                Commands.Add(merged);
            }
        }
        public override string ToString() { return Commands.Count == 0 ? "No op" : Commands.First().ToString(); }
    }

    public interface ICmdSubscriber {
        void OnNext(UCommand cmd, bool isUndo);
    }
}
