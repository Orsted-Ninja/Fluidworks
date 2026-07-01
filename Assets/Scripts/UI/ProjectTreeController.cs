using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace AeroFlow.UI
{
    // Define a simple struct to hold both the display name and the data key
    public struct TreeViewItem
    {
        public string DisplayName;
        public string Key;
    }

    public class ProjectTreeController
    {
        public event Action<string> OnSelectionChanged;
        private readonly TreeView _treeView;

        public ProjectTreeController(VisualElement root)
        {
            _treeView = root.Q<TreeView>("project-tree");
            if (_treeView == null)
            {
                Debug.LogError("ProjectTreeController: Could not find 'project-tree' element in the UI hierarchy.");
                return;
            }
            BuildTree();
            RegisterCallbacks();
        }

        private void BuildTree()
        {
            // The TreeView is now strongly typed with our custom struct
            var setupChildren = new List<TreeViewItemData<TreeViewItem>>
            {
                new TreeViewItemData<TreeViewItem>(2, new TreeViewItem { DisplayName = "Simulation Context", Key = "geometry" }),
                new TreeViewItemData<TreeViewItem>(3, new TreeViewItem { DisplayName = "Fluid Properties", Key = "materials" }),
                new TreeViewItemData<TreeViewItem>(4, new TreeViewItem { DisplayName = "Boundary Conditions", Key = "boundary" })
            };

            var solutionChildren = new List<TreeViewItemData<TreeViewItem>>
            {
                new TreeViewItemData<TreeViewItem>(6, new TreeViewItem { DisplayName = "Live Monitors", Key = "monitors" })
            };

            var studyChildren = new List<TreeViewItemData<TreeViewItem>>
            {
                new TreeViewItemData<TreeViewItem>(1, new TreeViewItem { DisplayName = "Setup", Key = null }, setupChildren),
                new TreeViewItemData<TreeViewItem>(5, new TreeViewItem { DisplayName = "Solution", Key = null }, solutionChildren),
                new TreeViewItemData<TreeViewItem>(7, new TreeViewItem { DisplayName = "Results & Export", Key = "results" })
            };

            var rootItems = new List<TreeViewItemData<TreeViewItem>>
            {
                new TreeViewItemData<TreeViewItem>(0, new TreeViewItem { DisplayName = "Study 1", Key = null }, studyChildren)
            };
            
            // Tell the TreeView how to make and bind the visual elements
            _treeView.makeItem = () => new Label();
            _treeView.bindItem = (element, index) => 
            {
                var label = element as Label;
                // Get our custom struct data for the given index
                var item = _treeView.GetItemDataForIndex<TreeViewItem>(index);
                label.text = item.DisplayName;
            };

            _treeView.SetRootItems(rootItems);
            _treeView.Rebuild();
            _treeView.ExpandAll();
        }

        private void RegisterCallbacks()
        {
            _treeView.selectionChanged += OnTreeSelectionChanged;
        }

        private void OnTreeSelectionChanged(IEnumerable<object> selectedItems)
        {
            foreach (var item in selectedItems)
            {
                // The selected item is our custom struct
                if (item is TreeViewItem viewItem)
                {
                    if (!string.IsNullOrEmpty(viewItem.Key))
                    {
                        OnSelectionChanged?.Invoke(viewItem.Key);
                    }
                }
            }
        }
    }
}
