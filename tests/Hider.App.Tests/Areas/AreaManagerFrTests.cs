using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Hider.Areas;
using Hider.Core.Model;
using Hider.Core.Persistence;

namespace Hider.App.Tests.Areas
{
    /// <summary>
    /// FR-10 (any number of areas), FR-11 (areas stay topmost) and FR-12 (the area worked on last is in front) on real area
    /// windows.
    /// </summary>
    public class AreaManagerFrTests
    {
        [AvaloniaFact]
        public void GT131_ManyAreas_NoLimit_AllSavedAndRestored()
        {
            // FR-10: areas are added without a limit; each has its own id and window, all are saved and come back.
            const int count = 64;
            var ids = new List<string>();
            using (var f = new AreaHarness())
            {
                f.Manager.Restore();
                for (int i = 0; i < count; i++)
                {
                    var area = f.Manager.AddNew();
                    area.ExitEdit();
                    ids.Add(area.Model.Id);
                }
                Assert.Equal(count, f.Manager.Areas.Count);
                Assert.Equal(count, ids.Distinct().Count());
                Assert.Equal(count, f.Hosts.Count);
                f.Manager.SaveNow();
                var saved = AreasFile.Load(f.AreasPath, out var status);
                Assert.Equal(LoadStatus.Loaded, status);
                Assert.Equal(ids, saved.Areas.Select(_a => _a.Id));

                // A second app on the same file restores every one of them.
                var services = f.Services;
                var second = new Hider.Areas.AreaManager(services);
                try
                {
                    Assert.Equal(LoadStatus.Loaded, second.Restore());
                    Assert.Equal(ids, second.Areas.Select(_a => _a.Model.Id));
                }
                finally
                {
                    second.Dispose();
                }
            }
        }

        [AvaloniaFact]
        public void GT132_TheAreaWorkedOnLast_IsTheActiveWindow()
        {
            // FR-12: the area the user works on is activated (its window comes to the front of the other topmost windows); the
            // order follows the last one worked on. Headless has no z-order, so the activation is what is checked.
            using var f = new AreaHarness();
            var a = f.Add(new PhysicalRect(100, 100, 300, 200));
            var b = f.Add(new PhysicalRect(200, 150, 300, 200));
            a.ExitEdit();
            b.ExitEdit();
            // The activations of the two f.Add calls are delivered before anything is recorded.
            Dispatcher.UIThread.RunJobs();

            var order = new List<string>();
            a.Window.Activated += (_, _) => order.Add("a");
            b.Window.Activated += (_, _) => order.Add("b");

            // Headless windows do not deactivate each other: both are deactivated the way the platform would before each
            // step, so IsActive says which window the step activated.
            Deactivate(a, b);
            a.EnterEdit();
            Dispatcher.UIThread.RunJobs();
            Assert.True(a.Window.IsActive);
            Assert.False(b.Window.IsActive);
            Deactivate(a, b);
            b.EnterEdit();
            Dispatcher.UIThread.RunJobs();
            Assert.True(b.Window.IsActive);
            Assert.False(a.Window.IsActive);
            a.ExitEdit();
            Deactivate(a, b);
            a.EnterEdit();
            Dispatcher.UIThread.RunJobs();
            Assert.True(a.Window.IsActive);
            Assert.False(b.Window.IsActive);
            Assert.Equal("a,b,a", string.Join(',', order));
        }

        /// <summary>Runs the platform's deactivation for each active area window (UiJobs.Deactivate).</summary>
        private static void Deactivate(params AreaController[] _areas)
        {
            foreach (var area in _areas)
            {
                if (area.Window.IsActive)
                {
                    UiJobs.Deactivate(area.Window);
                }
            }
            Assert.All(_areas, _a => Assert.False(_a.Window.IsActive));
        }

        [AvaloniaFact]
        public void GT14_EveryAreaWindowIsTopmost_InEveryMode()
        {
            // FR-11: area windows stay above other applications' windows whatever the mode: editing, hidden, after a round
            // trip through editing, and restored (hidden) at start.
            string saved;
            using (var first = new AreaHarness())
            {
                var a = first.Add(new PhysicalRect(100, 100, 300, 200));
                var b = first.Add(new PhysicalRect(200, 150, 300, 200));
                Assert.True(a.Window.Topmost);
                Assert.True(b.Window.Topmost);
                a.ExitEdit();
                b.ExitEdit();
                Assert.Equal(AreaMode.Hidden, a.Mode);
                Assert.True(a.Window.Topmost);
                Assert.True(b.Window.Topmost);
                a.EnterEdit();
                Assert.True(a.Window.Topmost);
                a.ExitEdit();
                Assert.True(a.Window.Topmost);
                first.Manager.SaveNow();
                saved = File.ReadAllText(first.AreasPath);
            }

            using var f = new AreaHarness();
            File.WriteAllText(f.AreasPath, saved);
            Assert.Equal(LoadStatus.Loaded, f.Manager.Restore());
            Assert.Equal(2, f.Manager.Areas.Count);
            Assert.All(f.Manager.Areas, _a =>
            {
                Assert.Equal(AreaMode.Hidden, _a.Mode);
                Assert.True(_a.Window.Topmost);
            });
        }
    }
}
