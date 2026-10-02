using System;
using System.Collections.Generic;
using System.Windows.Forms;
using Autodesk.Max;
using ActionItem = Autodesk.Max.Plugins.ActionItem;

namespace Max2Babylon
{
    class BabylonLoadAnimations:ActionItem
    {
        public override bool ExecuteAction()
        {
            var selectedContainers = Tools.GetContainerInSelection();

            if (selectedContainers?.Count <= 0)
            {
                AnimationGroupList.LoadDataFromAnimationHelpers();
                return true;
            }

            foreach (IIContainerObject containerObject in selectedContainers)
            {
                AnimationGroupList.LoadDataFromContainerHelper(containerObject);
            }

            return true;
        }

        public void Close()
        {
            return;
        }

        public override int Id_ => 5;
        public override string ButtonText
        {
            get { return "Planner Load AnimationGroups"; }
        }

        public override string MenuText
        {
            get
            {
                var selectedContainers = Tools.GetContainerInSelection();
                if (selectedContainers?.Count > 0)
                {
                    return "&Planner Load AnimationGroups from selected containers";
                }
                else
                {
                    return "&(Xref/Merge) Planner Load AnimationGroups";
                }
            }
        }

        public override string DescriptionText
        {
            get { return "Planner - Load AnimationGroups from Scene or selected Containers"; }
        }

        public override string CategoryText
        {
            get { return CuiTitles.ActionCategory; }
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