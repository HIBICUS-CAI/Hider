using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Hider.Core.Model;
using Hider.Core.Persistence;

namespace Hider.App.Tests.Areas
{
    /// <summary>FR-10 (any number of areas) and FR-12 (the area worked on last is in front) on real area windows.</summary>
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

            // Headless windows do not deactivate each other, so the order of activations is what is recorded.
            var order = new List<string>();
            a.Window.Activated += (_, _) => order.Add("a");
            b.Window.Activated += (_, _) => order.Add("b");

            a.EnterEdit();
            Dispatcher.UIThread.RunJobs();
            Assert.True(a.Window.IsActive);
            b.EnterEdit();
            Dispatcher.UIThread.RunJobs();
            Assert.True(b.Window.IsActive);
            a.ExitEdit();
            a.EnterEdit();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("a,b,a", string.Join(',', order.TakeLast(3)));
        }

        [AvaloniaFact]
        public void GT132_EveryAreaWindowIsTopmost()
        {
            // FR-12 is among Hider's own windows: all of them stay above other applications' windows.
            using var f = new AreaHarness();
            var a = f.Add(new PhysicalRect(100, 100, 300, 200));
            var b = f.Add(new PhysicalRect(200, 150, 300, 200));
            Assert.True(a.Window.Topmost);
            Assert.True(b.Window.Topmost);
        }
    }
}
