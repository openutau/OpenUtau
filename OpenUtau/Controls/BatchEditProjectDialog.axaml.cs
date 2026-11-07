using System.Collections.Generic;
using Avalonia.Controls;
using OpenUtau.Core.Editing;

namespace OpenUtau.App.Controls {
    public partial class BatchEditProjectDialog : Window {
        static BatchEditProjectDialog? open;

        public BatchEditProjectDialog() {
            InitializeComponent();
        }

        public static BatchEditProjectDialog Open(Window owner) {
            if (open != null) {
                open.Activate();
                return open;
            }
            open?.Close();
            var dialog = new BatchEditProjectDialog();
            open = dialog;
            dialog.Show(owner);
            if (dialog.Position.Y < 0) {
                dialog.Position = dialog.Position.WithY(0);
            }
            return dialog;
        }
    }
}
