using System;
using System.Windows.Forms;
using ActionItem = Autodesk.Max.Plugins.ActionItem;

namespace Max2Babylon
{
    public class BabylonExportActionItem : ActionItem
    {
        NativeWindow parentWindow;
        private ExporterForm form;

        public override bool ExecuteAction()
        {
            if (form == null || form.IsDisposed)
                form = new ExporterForm(this);

            if (parentWindow == null)
                parentWindow = new NativeWindow();

            if (parentWindow.Handle == IntPtr.Zero)
                parentWindow.AssignHandle(Loader.Core.MAXHWnd);

            if (!form.Visible)
                form.Show(parentWindow);

            form.WindowState = FormWindowState.Normal;
            form.BringToFront();

            return true;
        }

        public void Close()
        {
            if (form == null)
            {
                return;
            }
            form.Dispose();
            form = null;
        }

        public override int Id_ => 1;

        public override string ButtonText
        {
            get { return "Planner"; }
        }

        public override string MenuText
        {
            get { return "&Planner..."; }
        }

        public override string DescriptionText
        {
            get { return "Planner - export the scene and build its phasing layers from the Planner's list"; }
        }

        public override string CategoryText
        {
            get { return "Babylon"; }
        }

        public override bool IsChecked_
        {
            get { return false; }
        }

        public override bool IsItemVisible
        {
            get { return true; }
        }

        public override bool IsEnabled_
        {
            get { return true; }
        }
    }
}
