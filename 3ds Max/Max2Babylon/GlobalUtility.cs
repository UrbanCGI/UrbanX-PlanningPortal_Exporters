using Autodesk.Max;
using Autodesk.Max.Plugins;

#if MAX2025_OR_NEWER
using UiViewModels.Actions;
#else
using Autodesk.Max.IQuadMenuContext;
#endif

using System;
using System.Collections.Generic;

namespace Max2Babylon
{
    static class CuiTitles
    {
        // UrbanCGI fork: display titles say "Planner"; what Max and saved setups key on stays as it was.
        public const string MenuTitle = "Planner...";
        public const string GlobalTitle = "Planner";
        public const string PropertiesTitle = "Planner Properties";
        public const string AnimationGroupsTitle = "Planner Animation Groups";
        public const string ActionsBuilderTitle = "Planner Actions Builder";
        public const string SkipFlattenTitle = "Planner Toggle Skip Flatten Status";
        public const string LoadAnimationTitle = "Planner Load Animation From Containers";
        public const string SaveAnimationTitle = "Planner Save Animation To Containers";
        public const string FileExporterTitle = "File Exporter";

        /// <summary>The category the actions are listed under in the Customize UI and the Hotkey Editor.</summary>
        public const string ActionCategory = "Planner";

        /// <summary>
        /// The internal (non-localised) action category, as upstream. 3ds Max 2025+ may build the action table id
        /// (which the menu script addresses as 47368) from it, so it must never change.
        /// </summary>
        public const string InternalCategory = "Babylon";

        /// <summary>The titles earlier builds gave their menus; removed on install so an upgrade leaves no stale "Babylon" menu behind.</summary>
        public const string LegacyMenuTitle = "Babylon...";
        public const string LegacyGlobalTitle = "Babylon";
    }

#if MAX2025_OR_NEWER
    public class DummyCommandAdapter : CuiActionCommandAdapter
    {
        public const string DummyActionTitle = "BabylonDummyAction";
        public override string InternalActionText => DummyActionTitle;
        public override string InternalCategory => CuiTitles.InternalCategory;
        public override string ActionText => InternalActionText;
        // As upstream: which of the two categories 3ds Max builds the action table id from is not known, so both stay
        // "Babylon". The dummy action is deleted at start-up; the real actions show "Planner" through CategoryText.
        public override string Category => InternalCategory;
        public override void Execute(object parameter)
        {
            Loader.Global.COREInterface.DisplayTempPrompt("Babylon Dummy Action Adapter", 10);
        }

        // Clear and return ActionTable registered by the DummyAdapter
        public static IActionTable GetDummyActionTable()
        {
            var actionManager = Loader.Core.ActionManager;
            //actionManager.FindTable();
            for(int actionTableIndex = 0; actionTableIndex < actionManager.NumActionTables; ++actionTableIndex)
            {
                var theTable = actionManager.GetTable(actionTableIndex);

                for(int i = 0; i < theTable.Count; ++i)
                {
                    // if we found our known dummy action, remove it and return the table
                    var action = theTable[i];
                    if(action?.DescriptionText == DummyActionTitle)
                    {
                        theTable.DeleteOperation(action);
                        return theTable;
                    }
                }
            }

            return null;
        }
    }

#endif

    class GlobalUtility : GUP
    {
        public const string ActionTableName = "Babylon Actions";
        public const string GUIDPropertyName = "babylonjs_GUID";


#if MAX2025_OR_NEWER
        // Placeholder
        public static readonly string CreateMenuScript= System.Text.Encoding.UTF8.GetString(Resources.Resources.CreateBabylonMenus);

        /// <summary>The action table id CreateBabylonMenus.ms is written for.</summary>
        private const uint ExpectedActionTableId = 47368;

        private static bool registerMenusCallback = false;

        // Not Used
        // private GlobalDelegates.Delegate5 m_registerMenuDelegate;
        // private GlobalDelegates.Delegate5 m_registerQuadMenuDelegate;
#else
        IIMenu menu;
        IIMenuItem menuItem;
        IIMenuItem menuItemBabylon;
#endif
        uint idActionTable;
        IActionTable actionTable;
        IActionCallback actionCallback;

        /// <summary>
        /// Store reference of exporter form to close it manually when exiting 3ds max
        /// </summary>
        BabylonExportActionItem babylonExportActionItem;

#if MAX2018 || MAX2019
        GlobalDelegates.Delegate5 m_SystemStartupDelegate;
#endif
        private static bool filePreOpenCallback = false;
        private GlobalDelegates.Delegate5 m_FilePreOpenDelegate;

        private static bool postSceneResetCallback = false;
        private GlobalDelegates.Delegate5 m_PostSceneResetCallback;

        private static bool nodeAddedCallback = false;
        private GlobalDelegates.Delegate5 m_NodeAddedDelegate;

        private static bool nodeDeleteCallback = false;
        private GlobalDelegates.Delegate5 m_NodeDeleteDelegate;

        private static bool nodesClonedCallback = false;
        private GlobalDelegates.Delegate5 m_NodesClonedDelegate;


        private void MenuSystemStartupHandler(IntPtr objPtr, INotifyInfo infoPtr)
        {
            InstallMenus();
            AddCallbacks();
        }

        private void InitializeBabylonGuids(IntPtr param0, IntPtr param1)
        {
            Tools.guids = new Dictionary<Guid, IAnimatable>();
        }

        private void InitializeBabylonGuids(IntPtr objPtr, INotifyInfo infoPtr)
        {
            Tools.guids = new Dictionary<Guid, IAnimatable>();
        }

#if MAX2015
        private void OnNodeAdded(IntPtr param0, IntPtr param1)
        {
            try
            {
                INotifyInfo obj = Loader.Global.NotifyInfo.Marshal(param1);

                IINode n = (IINode) obj.CallParam;
                //todo replace this with something like isXREFNODE
                //to have a distinction between added xref node and max node
                string guid = n.GetStringProperty( GUIDPropertyName, string.Empty);
                if (string.IsNullOrEmpty(guid))
                {
                    n.GetGuid(); // force to assigne a new guid if not exist yet for this node
                }

                IIContainerObject contaner = Loader.Global.ContainerManagerInterface.IsContainerNode(n);
                if (contaner != null)
                {
                    // a generic operation on a container is done (open/inherit)
                    contaner.ResolveContainer();
                }
            }
            catch
            {
                // Fails silently
            }
        }

        private void OnNodeDeleted(IntPtr objPtr, IntPtr param1)
        {
            try
            {
                INotifyInfo obj = Loader.Global.NotifyInfo.Marshal(param1);

                IINode n = (IINode) obj.CallParam;
                Tools.guids.Remove(n.GetGuid());
            }
            catch
            {
                // Fails silently
            }
        }
#endif

#if MAX2025_OR_NEWER

        /// <summary>
        /// Force 3ds Max 2025.0 menusystem refresh, required for pre 2025.3 versions
        /// </summary>
        public static void ForceICuiMenuRefresh()
        {
            string script = "(\n" +
                                "local menuMgr = maxops.GetICuiMenuMgr()\n" +
                                "menuMgr.LoadConfiguration(menuMgr.GetCurrentConfiguration())\n" +

                                "local quadMenuMgr = maxOps.GetICuiQuadMenuMgr()\n" +
                                "quadMenuMgr.LoadConfiguration(quadMenuMgr.GetCurrentConfiguration())\n" +
                            ")\n";

            ScriptsUtilities.ExecuteMaxScriptCommand(script);
        }
#endif

        private void OnNodeAdded(IntPtr objPtr, INotifyInfo infoPtr)
        {
            try
            {
                IINode n = (IINode)infoPtr.CallParam;
                //todo replace this with something like isXREFNODE
                //to have a distinction between added xref node and max node
                string guid = n.GetStringProperty( GUIDPropertyName, string.Empty);
                if (string.IsNullOrEmpty(guid))
                {
                    n.GetGuid(); // force to assigne a new guid if not exist yet for this node
                }

                IIContainerObject container = Loader.Global.ContainerManagerInterface.IsContainerNode(n);
                if (container != null)
                {
                    // a generic operation on a container is done (open/inherit)
                    container.ResolveContainer();
                }
            }
            catch
            {
                // Fails silently
            }
        }

        /// <summary>
        /// UrbanCGI fork: a clone (Shift-drag, Edit &gt; Clone, Mirror, Array) copies its original's user properties,
        /// babylonjs_GUID included, and the Planner re-links activities by that id. Each clone gets a new id here, so
        /// the original keeps its own. Best effort: should 3ds Max hand the clones over in a form not read here, the
        /// export still keeps the id on the oldest node (<see cref="Tools.InitializeGuidNodesMap"/>).
        /// </summary>
        private void OnNodesCloned(IntPtr objPtr, INotifyInfo infoPtr)
        {
            try
            {
                var cloned = ClonedNodesOf(infoPtr);
                if (cloned == null)
                {
                    return;
                }
                foreach (var node in Tools.ITabToIEnumerable(cloned))
                {
                    if (node != null)
                    {
                        node.RenewGuid();
                    }
                }
            }
            catch
            {
                // Fails silently, like the other node callbacks
            }
        }

        private static ITab<IINode> ClonedNodesOf(INotifyInfo info)
        {
            var param = info != null ? info.CallParam : null;
            var clonedParams = param as IPostNodesClonedParams;
            if (clonedParams != null)
            {
                return clonedParams.ClonedNodes;
            }
#if MAX2025_OR_NEWER
            var notify = param as INotifyPostNodesCloned;
            if (notify != null)
            {
                return notify.ClonedNodes;
            }
            if (param is IntPtr && (IntPtr)param != IntPtr.Zero)
            {
                return Loader.Global.NotifyPostNodesCloned.Marshal((IntPtr)param).ClonedNodes;
            }
#endif
            return null;
        }

        public void RegisterNodesClonedCallback()
        {
            if (!nodesClonedCallback)
            {
                m_NodesClonedDelegate = new GlobalDelegates.Delegate5(this.OnNodesCloned);
                GlobalInterface.Instance.RegisterNotification(this.m_NodesClonedDelegate, null, SystemNotificationCode.PostNodesCloned);
                nodesClonedCallback = true;
            }
        }

        private void OnNodeDeleted(IntPtr objPtr, INotifyInfo infoPtr)
        {
            try
            {
                IINode n = (IINode)infoPtr.CallParam;
                Tools.guids.Remove(n.GetGuid());
            }
            catch
            {
                // Fails silently
            }
        }

        public override void Stop()
        {
            try
            {
                // Close exporter form manually
                if (babylonExportActionItem != null)
                {
                    babylonExportActionItem.Close();
                }

                if (actionTable != null)
                {
                    Loader.Global.COREInterface.ActionManager.DeactivateActionTable(actionCallback, idActionTable);
                }
#if MAX2025_OR_NEWER
                // Placeholder
                // no cleanup necessary for the new menu system
#else
                // Clean up menu
                if (menu != null)
                {
                    Loader.Global.COREInterface.MenuManager.UnRegisterMenu(menu);
                    Loader.Global.ReleaseIMenu(menu);
                    Loader.Global.ReleaseIMenuItem(menuItemBabylon);
                    Loader.Global.ReleaseIMenuItem(menuItem);

                    menu = null;
                    menuItem = null;
                }
#endif
            }
            catch
            {
                // Fails silently
            }
        }

        public override uint Start
        {
            get
            {
                IIActionManager actionManager = Loader.Core.ActionManager;

                // Set up global actions
                idActionTable = (uint)actionManager.NumActionTables;

#if MAX2025_OR_NEWER
                actionTable = DummyCommandAdapter.GetDummyActionTable();

                if(actionTable != null)
                    idActionTable = actionTable.Id_;

#elif MAX2022_OR_NEWER
                actionTable = Loader.Global.ActionTable.Create(idActionTable, 0 , ActionTableName);
#else
                string actionTableName = ActionTableName;
                actionTable = Loader.Global.ActionTable.Create(idActionTable, 0, ref actionTableName);
#endif
                // prevent null exceptions 
                if(actionTable == null)
                    return 0;

                babylonExportActionItem = new BabylonExportActionItem();
                actionTable.AppendOperation(babylonExportActionItem);
                actionTable.AppendOperation(new BabylonPropertiesActionItem()); // Babylon Properties forms are modals => no need to store reference
                actionTable.AppendOperation(new BabylonAnimationActionItem());
                actionTable.AppendOperation(new BabylonSaveAnimations());
                actionTable.AppendOperation(new BabylonLoadAnimations());
                actionTable.AppendOperation(new BabylonSkipFlattenToggle());
                
                actionCallback = new BabylonActionCallback();

                actionManager.RegisterActionTable(actionTable);
                actionManager.ActivateActionTable(actionCallback as ActionCallback, idActionTable);

                // Set up menus
#if MAX2018 || MAX2019
                var global = GlobalInterface.Instance;
                m_SystemStartupDelegate = new GlobalDelegates.Delegate5(MenuSystemStartupHandler);
                global.RegisterNotification(m_SystemStartupDelegate, null, SystemNotificationCode.SystemStartup);
#else
                InstallMenus();
                AddCallbacks();
#endif

                RegisterFilePreOpen();
                RegisterPostSceneReset();
                RegisterNodeAddedCallback();
                RegisterNodeDeletedCallback();
                RegisterNodesClonedCallback();
                return 0;
            }
        }

        public void RegisterFilePreOpen()
        {
            if (!filePreOpenCallback)
            {
                m_FilePreOpenDelegate = new GlobalDelegates.Delegate5(this.InitializeBabylonGuids);
                GlobalInterface.Instance.RegisterNotification(this.m_FilePreOpenDelegate, null, SystemNotificationCode.FilePreOpen);

                filePreOpenCallback = true;
            }
        }

        public void RegisterPostSceneReset()
        {
            if (!postSceneResetCallback)
            {
                m_PostSceneResetCallback = new GlobalDelegates.Delegate5(this.InitializeBabylonGuids);
                GlobalInterface.Instance.RegisterNotification(this.m_PostSceneResetCallback, null, SystemNotificationCode.PostSceneReset);

                postSceneResetCallback = true;
            }
        }

        public void RegisterNodeAddedCallback()
        {
            if (!nodeAddedCallback)
            {
                m_NodeAddedDelegate = new GlobalDelegates.Delegate5(this.OnNodeAdded);
#if MAX2015
                //bug on Autodesk API  SystemNotificationCode.SceneAddedNode doesn't work for max 2015-2016
                GlobalInterface.Instance.RegisterNotification(this.m_NodeAddedDelegate, null, SystemNotificationCode.NodeLinked );
#else
                GlobalInterface.Instance.RegisterNotification(this.m_NodeAddedDelegate, null, SystemNotificationCode.SceneAddedNode);
#endif
                nodeAddedCallback = true;
            }
        }

        public void RegisterNodeDeletedCallback()
        {
            if (!nodeDeleteCallback)
            {
                m_NodeDeleteDelegate = new GlobalDelegates.Delegate5(this.OnNodeDeleted);
                GlobalInterface.Instance.RegisterNotification(this.m_NodeDeleteDelegate, null, SystemNotificationCode.ScenePreDeletedNode);
                nodeDeleteCallback = true;
            }
        }

        private void InstallMenus()
        {
#if MAX2025_OR_NEWER
            var maxVer = Tools.GetMaxVersion();

            // with 2025.3 and up we could also use ICUIMenuManager ( Loader.Core.ICuiMenuManager / ICuiQuadMEnuManager )
            // UrbanCGI fork: the menu script names the action table as 47368. Should 3ds Max ever give the table
            // another id, the menus use the real one, and the listener says so.
            var menuScript = CreateMenuScript;
            if (idActionTable != ExpectedActionTableId)
            {
                menuScript = menuScript.Replace("actionTableId=" + ExpectedActionTableId, "actionTableId=" + idActionTable);
                ScriptsUtilities.ExecuteMaxScriptCommand($"format \"Planner Exporters: the action table id is {idActionTable}, not {ExpectedActionTableId}; the menus use {idActionTable}.\n\"");
            }
            ScriptsUtilities.ExecuteMaxScriptCommand(menuScript);
            
            // force menu refresh, as this is broken in 3dsMax versions 2025.0, 2025.1 and 2025.2
            if( maxVer.Major==27 && maxVer.Minor < 3 )
            {
                ForceICuiMenuRefresh();
                ScriptsUtilities.ExecuteMaxScriptCommand($"format \"Planner Exporters: forced menu refresh...\n\"");
            }
#else
            IIMenuManager menuManager = Loader.Core.MenuManager;

            // Set up menu. UrbanCGI fork: earlier sessions' menus go first, under the current title and the old
            // "Babylon" one, so an upgraded install shows a single "Planner" menu.
            RemoveMenus(menuManager, menuManager.MainMenuBar, CuiTitles.GlobalTitle, CuiTitles.LegacyGlobalTitle);
            menu = null;

            // Main menu
            menu = Loader.Global.IMenu;
            menu.Title = CuiTitles.GlobalTitle; // "Planner"
            menuManager.RegisterMenu(menu, 0);

            // Launch option
            menuItemBabylon = Loader.Global.IMenuItem;
            menuItemBabylon.Title = CuiTitles.FileExporterTitle; // "&File Exporter";
            menuItemBabylon.ActionItem = actionTable[0];
            menu.AddItem(menuItemBabylon, -1);

            menuItem = Loader.Global.IMenuItem;
            menuItem.SubMenu = menu;

            menuManager.MainMenuBar.AddItem(menuItem, -1);

            // Quad
            var rootQuadMenu = menuManager.GetViewportRightClickMenu(RightClickContext.NonePressed);
            var quadMenu = rootQuadMenu.GetMenu(0);

            RemoveMenus(menuManager, quadMenu, CuiTitles.MenuTitle, CuiTitles.LegacyMenuTitle);
            menu = null;

            menu = Loader.Global.IMenu;
            menu.Title = CuiTitles.MenuTitle; // "Planner...";
            menuManager.RegisterMenu(menu, 0);

            menuItemBabylon = Loader.Global.IMenuItem;
            menuItemBabylon.Title = CuiTitles.PropertiesTitle; // "Babylon Properties";
            menuItemBabylon.ActionItem = actionTable[1];
            menu.AddItem(menuItemBabylon, -1);

            menuItemBabylon = Loader.Global.IMenuItem;
            menuItemBabylon.Title = CuiTitles.AnimationGroupsTitle; // "Babylon Animation Groups";
            menuItemBabylon.ActionItem = actionTable[2];
            menu.AddItem(menuItemBabylon, -1);

            menuItemBabylon = Loader.Global.IMenuItem;
            menuItemBabylon.Title = CuiTitles.SaveAnimationTitle; // "Babylon Save Animation To Containers";
            menuItemBabylon.ActionItem = actionTable[3];
            menu.AddItem(menuItemBabylon, -1);

            menuItemBabylon = Loader.Global.IMenuItem;
            menuItemBabylon.Title = CuiTitles.LoadAnimationTitle; // "Babylon Load Animation From Containers";
            menuItemBabylon.ActionItem = actionTable[4];
            menu.AddItem(menuItemBabylon, -1);

            menuItemBabylon = Loader.Global.IMenuItem;
            menuItemBabylon.Title = CuiTitles.SkipFlattenTitle; // "Babylon Toggle Skip Flatten Status";
            menuItemBabylon.ActionItem = actionTable[5];
            menu.AddItem(menuItemBabylon, -1);

            menuItemBabylon = Loader.Global.IMenuItem;
            menuItemBabylon.Title = CuiTitles.ActionsBuilderTitle; // "Babylon Actions Builder";
            menuItemBabylon.ActionItem = actionTable[6];
            menu.AddItem(menuItemBabylon, -1);

            menuItem = Loader.Global.IMenuItem;
            menuItem.SubMenu = menu;

            quadMenu.AddItem(menuItem, -1);

            Loader.Global.COREInterface.MenuManager.UpdateMenuBar();
#endif
            }

#if !MAX2025_OR_NEWER
        /// <summary>
        /// UrbanCGI fork: takes every item whose submenu is titled one of <paramref name="titles"/> off
        /// <paramref name="host"/> (the main menu bar or the quad), then unregisters the menus with those titles.
        /// </summary>
        private static void RemoveMenus(IIMenuManager menuManager, IIMenu host, params string[] titles)
        {
            try
            {
                if (host != null)
                {
                    for (int i = host.NumItems - 1; i >= 0; i--)
                    {
                        var item = host.GetItem(i);
                        var subMenu = item != null ? item.SubMenu : null;
                        if (subMenu != null && Array.IndexOf(titles, subMenu.Title) >= 0)
                        {
                            host.RemoveItem(i);
                        }
                    }
                }
            }
            catch
            {
                // A menu that cannot be inspected is left as it is; unregistering below still runs.
            }

            foreach (var title in titles)
            {
                // A crash or an older build can leave more than one menu with the same title.
                IIMenu previous = null;
                for (int guard = 0; guard < 8; guard++)
                {
                    var existing = menuManager.FindMenu(title);
                    if (existing == null || object.Equals(existing, previous))
                    {
                        break;
                    }
                    // A menu 3ds Max still holds is never released, and none is released twice.
                    if (!menuManager.UnRegisterMenu(existing))
                    {
                        break;
                    }
                    Loader.Global.ReleaseIMenu(existing);
                    previous = existing;
                }
            }
        }
#endif

        private void AddCallbacks() 
        {
            foreach (var s in MaterialScripts.AddCallbacks())
#if MAX2022_OR_NEWER
                ManagedServices.MaxscriptSDK.ExecuteMaxscriptCommand(s,ManagedServices.MaxscriptSDK.ScriptSource.NotSpecified);
#else
                ManagedServices.MaxscriptSDK.ExecuteMaxscriptCommand(s);
#endif
       }
    }
}
