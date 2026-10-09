using System;
using System.Reflection;
using System.Runtime.InteropServices;

namespace BabylonJS_Installer
{
    /// <summary>
    /// Starts a program through the Windows desktop (Explorer) instead of from this installer (UrbanCGI fork).
    /// The installer runs with administrator rights; a 3ds Max started straight from it would run with those rights
    /// too, which hides mapped network drives and blocks dragging files in from Explorer. Asking the desktop to start
    /// it gives the signed-in user's normal rights, as when 3ds Max is opened from the Start menu.
    /// This is the documented route: Raymond Chen, "How can I launch an unelevated process from my elevated process
    /// and vice versa?" (The Old New Thing, 2013): ShellWindows, the desktop's shell view, IShellDispatch2.ShellExecute.
    /// </summary>
    static class DesktopShell
    {
        private const int CSIDL_DESKTOP = 0;
        private const int SWC_DESKTOP = 8;
        private const int SWFO_NEEDDISPATCH = 1;
        private const uint SVGIO_BACKGROUND = 0;
        private const int SW_SHOWNORMAL = 1;

        private static readonly Guid CLSID_ShellWindows = new Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39");
        private static readonly Guid SID_STopLevelBrowser = new Guid("4C96BE40-915C-11CF-99D3-00AA004AE837");
        private static readonly Guid IID_IShellBrowser = new Guid("000214E2-0000-0000-C000-000000000046");
        private static readonly Guid IID_IDispatch = new Guid("00020400-0000-0000-C000-000000000046");

        /// <summary>IShellWindows (exdisp.idl); only FindWindowSW is called, the earlier slots keep the order.</summary>
        [ComImport, Guid("85CB6900-4D95-11CF-960C-0080C7F4EE85"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
        private interface IShellWindows
        {
            void Count();
            void Item();
            void NewEnum();
            void Register();
            void RegisterPending();
            void Revoke();
            void OnNavigate();
            void OnActivated();

            [return: MarshalAs(UnmanagedType.IDispatch)]
            object FindWindowSW(
                [In, MarshalAs(UnmanagedType.Struct)] ref object location,
                [In, MarshalAs(UnmanagedType.Struct)] ref object locationRoot,
                int windowClass,
                out int windowHandle,
                int options);
        }

        /// <summary>IServiceProvider (servprov.h).</summary>
        [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IComServiceProvider
        {
            [return: MarshalAs(UnmanagedType.IUnknown)]
            object QueryService(ref Guid service, ref Guid riid);
        }

        /// <summary>IShellBrowser (shobjidl.h): IOleWindow's 2 methods and 10 of its own come before QueryActiveShellView.</summary>
        [ComImport, Guid("000214E2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellBrowser
        {
            void GetWindow();
            void ContextSensitiveHelp();
            void InsertMenusSB();
            void SetMenuSB();
            void RemoveMenusSB();
            void SetStatusTextSB();
            void EnableModelessSB();
            void TranslateAcceleratorSB();
            void BrowseObject();
            void GetViewStateStream();
            void GetControlWindow();
            void SendControlMsg();

            IShellView QueryActiveShellView();
        }

        /// <summary>IShellView (shobjidl.h): IOleWindow's 2 methods and 10 of its own come before GetItemObject.</summary>
        [ComImport, Guid("000214E3-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellView
        {
            void GetWindow();
            void ContextSensitiveHelp();
            void TranslateAccelerator();
            void EnableModeless();
            void UIActivate();
            void Refresh();
            void CreateViewWindow();
            void DestroyViewWindow();
            void GetCurrentInfo();
            void AddPropertySheetPages();
            void SaveViewState();
            void SelectItem();

            [return: MarshalAs(UnmanagedType.IUnknown)]
            object GetItemObject(uint item, ref Guid riid);
        }

        /// <summary>
        /// Asks the desktop to start <paramref name="file"/> with <paramref name="arguments"/> in <paramref name="folder"/>.
        /// Returns null when the request was handed over, else why not (nothing was started then).
        /// </summary>
        public static string TryStart(string file, string arguments, string folder)
        {
            try
            {
                var shellWindows = (IShellWindows)Activator.CreateInstance(Type.GetTypeFromCLSID(CLSID_ShellWindows, true));
                object location = CSIDL_DESKTOP;
                object locationRoot = null;
                int windowHandle;
                var desktop = shellWindows.FindWindowSW(ref location, ref locationRoot, SWC_DESKTOP, out windowHandle, SWFO_NEEDDISPATCH);
                if (desktop == null) return "the Windows desktop was not found";

                var service = SID_STopLevelBrowser;
                var browserIid = IID_IShellBrowser;
                var browser = (IShellBrowser)((IComServiceProvider)desktop).QueryService(ref service, ref browserIid);
                var view = browser.QueryActiveShellView();
                var dispatchIid = IID_IDispatch;
                var folderView = view.GetItemObject(SVGIO_BACKGROUND, ref dispatchIid);

                // IShellFolderViewDual.Application is the desktop's IShellDispatch2; both are reached by name.
                var application = folderView.GetType().InvokeMember("Application", BindingFlags.GetProperty, null, folderView, null);
                application.GetType().InvokeMember("ShellExecute", BindingFlags.InvokeMethod, null, application,
                    new object[] { file, arguments ?? "", folder ?? "", "open", SW_SHOWNORMAL });
                return null;
            }
            catch (Exception ex)
            {
                var inner = ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex;
                return inner.GetType().Name + ": " + inner.Message;
            }
        }
    }
}
