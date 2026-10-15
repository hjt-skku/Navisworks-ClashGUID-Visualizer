using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Clash;
using Autodesk.Navisworks.Api.Interop;
using Autodesk.Navisworks.Api.Plugins;
using Autodesk.Navisworks.Clash;
using Syncfusion.Windows.Controls.Grid;

namespace ClashGuidVisualizer
{
    // Adds a Clash GUID column and a GUID search box to the Clash Detective Results tab at runtime.
    // Loaded at startup; a timer attaches to the Results grid whenever it exists (and re-attaches after
    // Reset Columns or when the Clash Detective window is recreated).
    // Depends on Navisworks Manage 2025 internals (Navisworks.Clash.Plugin): ResultsTabVM.m_all_columns,
    // RebuildCurrentDisplayColumns and the ResultsTab "RightCommandButtons" panel.
    [Plugin("ClashGuidVisualizer", "HJTS", DisplayName = "ClashGUID Visualizer")]
    public class ClashGuidVisualizerPlugin : EventWatcherPlugin
    {
        const string Title = "ClashGUID Visualizer";
        const string GridName = "DataGrid_Results";
        const string Mapping = "ClashGuid"; // not a real property: values come from QueryUnknownProperty
        const string SearchBoxName = "ClashGuidSearchPanel";
        const BindingFlags Priv = BindingFlags.Instance | BindingFlags.NonPublic;

        DispatcherTimer m_timer;
        GridTreeControl m_grid;
        GridTreeControlImpl m_hookedGrid;
        bool m_searchBoxFailed;

        public override void OnLoaded()
        {
            m_timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1.5) };
            m_timer.Tick += Tick;
            m_timer.Start();
        }

        public override void OnUnloading()
        {
            m_timer?.Stop();
        }

        void Tick(object sender, EventArgs e)
        {
            try
            {
                if (m_grid == null || !m_grid.IsLoaded)
                {
                    m_grid = FindGrid();
                    if (m_grid == null) return;
                }
                if (m_grid.DataContext == null) return;
                if (!(m_grid.DataContext is ResultsTabVM vm))
                {
                    Fail($"Unexpected internal structure. DataContext: {m_grid.DataContext.GetType().FullName}");
                    return;
                }

                if (!HookGrid(m_grid)) return; // internal grid not created yet
                AddColumn(vm);
                if (!m_searchBoxFailed) AddSearchBox(m_grid, vm);
            }
            catch (Exception ex)
            {
                Fail(ex.Message);
            }
        }

        // Stops the add-in for this session and reports once, instead of retrying every tick.
        void Fail(string message)
        {
            m_timer.Stop();
            MessageBox.Show($"{message}\n\nThe add-in has been disabled for this session. This Navisworks version may not be supported.", Title);
        }

        static void AddColumn(ResultsTabVM vm)
        {
            var columns = (List<ResultColumn>)typeof(ResultsTabVM).GetField("m_all_columns", Priv).GetValue(vm);
            if (columns.Any(c => c.MappingName == Mapping)) return;

            var col = new ResultColumn
            {
                MappingName = Mapping,
                HeaderText = "Clash GUID",
                Width = 260,
                Position = columns.Max(c => c.Position) + 1,
                Visible = true,
                ReadOnly = true,
            };

            columns.Add(col);
            typeof(ResultsTabVM).GetMethod("RebuildCurrentDisplayColumns", Priv).Invoke(vm, null);
        }

        void AddSearchBox(GridTreeControl grid, ResultsTabVM vm)
        {
            var tab = FindAncestor<ResultsTab>(grid);
            if (tab == null) return; // not attached yet
            if (!(tab.FindName("RightCommandButtons") is StackPanel host))
            {
                m_searchBoxFailed = true;
                MessageBox.Show("Could not find where to place the search box (RightCommandButtons). Only the GUID column was added.", Title);
                return;
            }
            if (host.Children.OfType<FrameworkElement>().Any(el => el.Name == SearchBoxName)) return;

            var box = new TextBox { Width = 280, Margin = new Thickness(1), VerticalContentAlignment = VerticalAlignment.Center, ToolTip = "Enter a Clash GUID and press Enter to go to the result. Esc clears the filter." };
            box.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter) { Search(vm, box.Text); e.Handled = true; }
                else if (e.Key == Key.Escape) { vm.CurrentFilter = null; box.Clear(); e.Handled = true; }
            };

            var panel = new StackPanel { Name = SearchBoxName, Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 5, 0) };
            panel.Children.Add(new TextBlock { Text = "GUID", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
            panel.Children.Add(box);
            host.Children.Insert(0, panel);
        }

        // The column is a plain text cell like the built-in ones. Its MappingName is not a property of the row
        // view model, so the grid asks QueryUnknownProperty for the value: the result GUID, blank for groups.
        // Rows revealed by expanding a group are redrawn right after the expand so they never stay blank.
        bool HookGrid(GridTreeControl grid)
        {
            var impl = grid.InternalGrid;
            if (impl == null) return false;
            if (m_hookedGrid == impl) return true;
            m_hookedGrid = impl;
            impl.QueryUnknownProperty += (s, e) =>
            {
                if (e.PropertyName != Mapping) return;
                var row = (e.Node as GridTreeNode)?.Item as ClashIssueVM;
                e.Value = row?.ApiItem is ClashResult result ? result.Guid.ToString() : "";
            };
            grid.ExpandStateChanged += (s, e) =>
                grid.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => grid.InternalGrid?.InvalidateCells()));
            return true;
        }

        // Exact match. Switches to the owning test, expands the parent group, selects the result and applies a GUID filter.
        static void Search(ResultsTabVM vm, string text)
        {
            if (!Guid.TryParse(text.Trim(), out var guid))
            {
                MessageBox.Show("Not a valid GUID.", Title);
                return;
            }
            var result = Autodesk.Navisworks.Api.Application.ActiveDocument.GetClash().TestsData.ResolveGuid(guid) as ClashResult;
            if (result == null)
            {
                MessageBox.Show("No clash result with this GUID.", Title);
                return;
            }

            SavedItem node = result;
            while (node != null && !(node is ClashTest)) node = node.Parent;
            var test = (ClashTest)node;

            vm.CurrentFilter = null; // an active filter hiding the new selection would trigger a confirmation dialog
            var tests = vm.Service.Tests;
            vm.UIService.CurrentTest = tests.FindTestVmFromTest(test);
            if (result.Parent is ClashResultGroup group && tests.FindIssueVmFromIssue(group) is ClashIssueVM groupVm)
                groupVm.IsExpanded = true;
            ClashCurrentIssue.CurrentIssue = result;
            vm.CurrentFilter = new GuidQuickFilter(guid);
        }

        static GridTreeControl FindGrid()
        {
            foreach (PresentationSource src in PresentationSource.CurrentSources)
            {
                if (src.RootVisual is DependencyObject root && Find(root) is GridTreeControl g)
                    return g;
            }
            return null;
        }

        static DependencyObject Find(DependencyObject node)
        {
            if (node is FrameworkElement fe && fe.Name == GridName) return node;
            int n = VisualTreeHelper.GetChildrenCount(node);
            for (int i = 0; i < n; i++)
            {
                var hit = Find(VisualTreeHelper.GetChild(node, i));
                if (hit != null) return hit;
            }
            return null;
        }

        static T FindAncestor<T>(DependencyObject node) where T : DependencyObject
        {
            while (node != null && !(node is T)) node = VisualTreeHelper.GetParent(node);
            return node as T;
        }
    }

    public class GuidQuickFilter : QuickFilter
    {
        readonly Guid m_guid;
        public GuidQuickFilter(Guid guid) { m_guid = guid; }
        protected override bool IsFilteredOutImpl(IClashResult result) => !(result is SavedItem si) || si.Guid != m_guid;
        protected override string GetFilterDisplayString() => "GUID = " + m_guid;
    }
}
