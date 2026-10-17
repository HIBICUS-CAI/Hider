using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Hider.Areas;
using Hider.Core.Model;

namespace Hider.App.Tests.Areas
{
    /// <summary>EditChrome (frame, eight handles, toolbar placement) and EditToolbar (FR-32, FR-34, FR-52, design canvas 3).</summary>
    public class EditChromeToolbarTests
    {
        private static readonly string[] everyEvent = { "pattern:Fill", "pattern:Mosaic", "pattern:Generated", "settings", "refresh", "delete", "done" };

        private static void Click(Control _control)
        {
            _control.RaiseEvent(new RoutedEventArgs(_control is MenuItem ? MenuItem.ClickEvent : Button.ClickEvent));
        }

        [AvaloniaFact]
        public void GT05_Chrome_HasEightHandles_EachTaggedWithItsKind()
        {
            var chrome = new EditChrome();
            var expected = new (Border, ResizeHandle)[]
            {
                (chrome.HandleTopLeft, ResizeHandle.TopLeft), (chrome.HandleTop, ResizeHandle.Top), (chrome.HandleTopRight, ResizeHandle.TopRight),
                (chrome.HandleLeft, ResizeHandle.Left), (chrome.HandleRight, ResizeHandle.Right),
                (chrome.HandleBottomLeft, ResizeHandle.BottomLeft), (chrome.HandleBottom, ResizeHandle.Bottom), (chrome.HandleBottomRight, ResizeHandle.BottomRight),
            };
            Assert.Equal(expected.Length, chrome.Handles.Count);
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.Same(expected[i].Item1, chrome.Handles[i].Control);
                Assert.Equal(expected[i].Item2, chrome.Handles[i].Kind);
                Assert.Equal(expected[i].Item2, chrome.Handles[i].Control.Tag);
            }
            Assert.Equal(8, chrome.Handles.Select(_h => _h.Kind).Distinct().Count());
        }

        [AvaloniaFact]
        public void GT05_Chrome_HandlesHaveTheirResizeCursors()
        {
            var chrome = new EditChrome();
            var expected = new Dictionary<ResizeHandle, Avalonia.Input.StandardCursorType>
            {
                [ResizeHandle.TopLeft] = Avalonia.Input.StandardCursorType.TopLeftCorner,
                [ResizeHandle.Top] = Avalonia.Input.StandardCursorType.TopSide,
                [ResizeHandle.TopRight] = Avalonia.Input.StandardCursorType.TopRightCorner,
                [ResizeHandle.Left] = Avalonia.Input.StandardCursorType.LeftSide,
                [ResizeHandle.Right] = Avalonia.Input.StandardCursorType.RightSide,
                [ResizeHandle.BottomLeft] = Avalonia.Input.StandardCursorType.BottomLeftCorner,
                [ResizeHandle.Bottom] = Avalonia.Input.StandardCursorType.BottomSide,
                [ResizeHandle.BottomRight] = Avalonia.Input.StandardCursorType.BottomRightCorner,
            };
            Assert.Equal(expected.Count, chrome.Handles.Count);
            foreach (var (control, kind) in chrome.Handles)
            {
                // A standard cursor's name is its StandardCursorType (Avalonia 12.1.4 Cursor.ToString).
                Assert.Equal(expected[kind].ToString(), Assert.IsType<Avalonia.Input.Cursor>(control.Cursor).ToString());
                Assert.Contains("handle", control.Classes);
            }
        }

        [AvaloniaFact]
        public void GT05_Chrome_OutlinesAndFrame_SurroundTheArea_WithoutTakingClicks()
        {
            var chrome = new EditChrome();
            var area = new Rect(20, 60, 200, 100);
            chrome.Arrange(area, false, 8);
            void AssertPlaced(Control _c, Rect _r)
            {
                Assert.Equal(_r.X, Canvas.GetLeft(_c));
                Assert.Equal(_r.Y, Canvas.GetTop(_c));
                Assert.Equal(_r.Width, _c.Width);
                Assert.Equal(_r.Height, _c.Height);
                Assert.False(_c.IsHitTestVisible);
            }
            AssertPlaced(chrome.OuterOutline, new Rect(17, 57, 206, 106));
            AssertPlaced(chrome.Frame, new Rect(18, 58, 204, 104));
            AssertPlaced(chrome.InnerOutline, area);
        }

        [AvaloniaTheory]
        [InlineData(ResizeHandle.TopLeft, 20, 60)]
        [InlineData(ResizeHandle.Top, 120, 60)]
        [InlineData(ResizeHandle.TopRight, 220, 60)]
        [InlineData(ResizeHandle.Left, 20, 110)]
        [InlineData(ResizeHandle.Right, 220, 110)]
        [InlineData(ResizeHandle.BottomLeft, 20, 160)]
        [InlineData(ResizeHandle.Bottom, 120, 160)]
        [InlineData(ResizeHandle.BottomRight, 220, 160)]
        public void GT05_Chrome_HandlesAreCentredOnCornersAndEdgeMiddles(ResizeHandle _kind, double _cx, double _cy)
        {
            // The handle sizes come from the area window's styles.
            using var f = new AreaHarness();
            var chrome = f.Add(new PhysicalRect(400, 300, 400, 240)).Window.Chrome;
            chrome.Arrange(new Rect(20, 60, 200, 100), false, 8);
            var handle = chrome.Handles.Single(_h => _h.Kind == _kind).Control;
            Assert.Equal(12, handle.Width); // the hit area; the visible square is 8 DIP
            Assert.Equal(12, handle.Height);
            Assert.Equal(_cx - 6, Canvas.GetLeft(handle));
            Assert.Equal(_cy - 6, Canvas.GetTop(handle));
        }

        [AvaloniaTheory]
        [InlineData(false, 8, 60 - 8 - 36)]
        [InlineData(false, 12, 60 - 12 - 36)]
        [InlineData(true, 8, 60 + 4)]
        public void GT12_Chrome_ToolbarAboveTheArea_OrInsideItsTop(bool _inside, double _gap, double _top)
        {
            var chrome = new EditChrome();
            chrome.Arrange(new Rect(20, 60, 200, 100), _inside, _gap);
            Assert.Equal(20, Canvas.GetLeft(chrome.Toolbar));
            Assert.Equal(_top, Canvas.GetTop(chrome.Toolbar));
        }

        [AvaloniaFact]
        public void GT05_Chrome_EmptyArea_GivesEmptyInnerOutline()
        {
            var chrome = new EditChrome();
            chrome.Arrange(new Rect(5, 5, 0, 0), false, 8);
            Assert.Equal(0, chrome.InnerOutline.Width);
            Assert.Equal(0, chrome.InnerOutline.Height);
            Assert.Equal(6, chrome.OuterOutline.Width);
        }

        [Fact]
        public void GT57_Toolbar_Sizes()
        {
            Assert.Equal(436, EditToolbar.NormalWidth);
            Assert.Equal(540, EditToolbar.WidthWithWarning);
            Assert.Equal(36, EditToolbar.HeightDip);
        }

        [AvaloniaFact]
        public void GT57_Toolbar_StartsAtTheNormalSize_WithItsLabels()
        {
            var toolbar = new EditToolbar();
            Assert.Equal(EditToolbar.NormalWidth, toolbar.Width);
            Assert.Equal(EditToolbar.HeightDip, toolbar.Height);
            Assert.Equal("塗りつぶし", toolbar.FillItem.Header);
            Assert.Equal("モザイク", toolbar.MosaicItem.Header);
            Assert.Equal("パターン", toolbar.GeneratedItem.Header);
            Assert.Equal("完了", toolbar.DoneLabel.Text);
            Assert.Equal("取得できません", toolbar.FailedText.Text);
            Assert.False(toolbar.FailedText.IsVisible);
        }

        [AvaloniaFact]
        public void GT79_Toolbar_IconButtons_HaveTooltipsAndAccessibleNames()
        {
            var toolbar = new EditToolbar();
            foreach (var (button, name) in new (Button, string)[]
            {
                (toolbar.SettingsButton, "設定"),
                (toolbar.RefreshButton, "今すぐ更新（下の内容を撮り直す）"),
                (toolbar.DeleteButton, "このエリアを削除"),
            })
            {
                Assert.Equal(name, ToolTip.GetTip(button));
                Assert.Equal(name, AutomationProperties.GetName(button));
            }
        }

        [AvaloniaFact]
        public void GT77_Toolbar_PatternMenu_IsTheButtonsFlyoutWithTheThreePatterns()
        {
            var toolbar = new EditToolbar();
            Assert.Same(toolbar.PatternButton.Flyout, toolbar.PatternMenu);
            Assert.Equal(new object[] { toolbar.FillItem, toolbar.MosaicItem, toolbar.GeneratedItem }, toolbar.PatternMenu.Items.Cast<object>());
        }

        [AvaloniaFact]
        public void GT77_Toolbar_ButtonsAndItems_RaiseTheirEvents()
        {
            var toolbar = new EditToolbar();
            var log = new List<string>();
            toolbar.PatternSelected += _p => log.Add($"pattern:{_p}");
            toolbar.SettingsClicked += () => log.Add("settings");
            toolbar.RefreshClicked += () => log.Add("refresh");
            toolbar.DeleteClicked += () => log.Add("delete");
            toolbar.DoneClicked += () => log.Add("done");
            Click(toolbar.FillItem);
            Click(toolbar.MosaicItem);
            Click(toolbar.GeneratedItem);
            Click(toolbar.SettingsButton);
            Click(toolbar.RefreshButton);
            Click(toolbar.DeleteButton);
            Click(toolbar.DoneButton);
            Assert.Equal(everyEvent, log);
        }

        [AvaloniaFact]
        public void GT77_Toolbar_ButtonsAndItems_WithoutListeners_DoNothing()
        {
            var toolbar = new EditToolbar();
            foreach (var control in new Control[] { toolbar.FillItem, toolbar.MosaicItem, toolbar.GeneratedItem, toolbar.SettingsButton, toolbar.RefreshButton, toolbar.DeleteButton, toolbar.DoneButton })
            {
                Click(control);
            }
            Assert.Equal(EditToolbar.NormalWidth, toolbar.Width);
        }

        [AvaloniaTheory]
        [InlineData(PatternKind.Fill, "塗りつぶし", false)]
        [InlineData(PatternKind.Mosaic, "モザイク", true)]
        [InlineData(PatternKind.Generated, "パターン", false)]
        [InlineData((PatternKind)99, "パターン", false)] // anything else reads as a generated pattern
        public void GT35_Toolbar_Update_NamesThePattern_AndShowsRefreshOnlyForMosaic(PatternKind _pattern, string _label, bool _refresh)
        {
            var toolbar = new EditToolbar();
            toolbar.Update(_pattern, new PhysicalRect(1, 2, 640, 360), false);
            Assert.Equal(_label, toolbar.PatternLabel.Text);
            Assert.Equal(_refresh, toolbar.RefreshButton.IsVisible);
            Assert.Equal("640 × 360 px", toolbar.SizeText.Text);
            Assert.False(toolbar.FailedText.IsVisible);
            Assert.Equal(EditToolbar.NormalWidth, toolbar.Width);
        }

        [AvaloniaFact]
        public void GT24_Toolbar_Update_CaptureFailed_ShowsTheWarningAndWidens_ThenNarrowsAgain()
        {
            var toolbar = new EditToolbar();
            toolbar.Update(PatternKind.Generated, new PhysicalRect(0, 0, 24, 24), true);
            Assert.True(toolbar.FailedText.IsVisible);
            Assert.Equal(EditToolbar.WidthWithWarning, toolbar.Width);
            Assert.Equal("24 × 24 px", toolbar.SizeText.Text);
            toolbar.Update(PatternKind.Generated, new PhysicalRect(0, 0, 24, 24), false);
            Assert.False(toolbar.FailedText.IsVisible);
            Assert.Equal(EditToolbar.NormalWidth, toolbar.Width);
        }

        [AvaloniaFact]
        public void GT79_Toolbar_Tips_AnyOpenAndCloseAll()
        {
            var window = new Window { Content = new EditToolbar() };
            var toolbar = (EditToolbar)window.Content!;
            window.Show();
            Assert.False(toolbar.AnyTipOpen);
            foreach (var button in new[] { toolbar.SettingsButton, toolbar.RefreshButton, toolbar.DeleteButton })
            {
                ToolTip.SetIsOpen(button, true);
                Assert.True(toolbar.AnyTipOpen);
                toolbar.CloseTips();
                Assert.False(toolbar.AnyTipOpen);
                Assert.False(ToolTip.GetIsOpen(button));
            }
            ToolTip.SetIsOpen(toolbar.SettingsButton, true);
            ToolTip.SetIsOpen(toolbar.DeleteButton, true);
            toolbar.CloseTips();
            Assert.False(toolbar.AnyTipOpen);
            toolbar.CloseTips(); // nothing open: still fine
            window.Close();
        }
    }
}
