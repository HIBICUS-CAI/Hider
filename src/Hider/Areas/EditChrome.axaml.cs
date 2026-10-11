using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Hider.Core.Model;

namespace Hider.Areas
{
    /// <summary>Everything shown only in edit mode: frame, the eight handles and the toolbar (FR-32, FR-34, D-8).</summary>
    public partial class EditChrome : Canvas
    {
        public EditChrome()
        {
            InitializeComponent();
            Handles = new (Border, ResizeHandle)[]
            {
                (HandleTopLeft, ResizeHandle.TopLeft), (HandleTop, ResizeHandle.Top), (HandleTopRight, ResizeHandle.TopRight),
                (HandleLeft, ResizeHandle.Left), (HandleRight, ResizeHandle.Right),
                (HandleBottomLeft, ResizeHandle.BottomLeft), (HandleBottom, ResizeHandle.Bottom), (HandleBottomRight, ResizeHandle.BottomRight),
            };
            foreach (var (handle, kind) in Handles)
            {
                handle.Tag = kind;
            }
        }

        public IReadOnlyList<(Border Control, ResizeHandle Kind)> Handles { get; }

        /// <summary>Positions the frame, handles and toolbar around the area rectangle (DIP, window coordinates).</summary>
        public void Arrange(Rect _area, bool _toolbarInside, double _toolbarGap)
        {
            Place(OuterOutline, _area.Inflate(3));
            Place(Frame, _area.Inflate(2));
            Place(InnerOutline, _area);
            double cx = _area.X + _area.Width / 2, cy = _area.Y + _area.Height / 2;
            var centers = new Dictionary<ResizeHandle, Point>
            {
                [ResizeHandle.TopLeft] = _area.TopLeft, [ResizeHandle.Top] = new(cx, _area.Top), [ResizeHandle.TopRight] = _area.TopRight,
                [ResizeHandle.Left] = new(_area.Left, cy), [ResizeHandle.Right] = new(_area.Right, cy),
                [ResizeHandle.BottomLeft] = _area.BottomLeft, [ResizeHandle.Bottom] = new(cx, _area.Bottom), [ResizeHandle.BottomRight] = _area.BottomRight,
            };
            foreach (var (handle, kind) in Handles)
            {
                var c = centers[kind];
                SetLeft(handle, c.X - handle.Width / 2);
                SetTop(handle, c.Y - handle.Height / 2);
            }
            SetLeft(Toolbar, _area.X);
            SetTop(Toolbar, _toolbarInside ? _area.Y + 4 : _area.Y - _toolbarGap - Toolbar.Height);
        }

        private static void Place(Control _control, Rect _rect)
        {
            SetLeft(_control, _rect.X);
            SetTop(_control, _rect.Y);
            _control.Width = Math.Max(0, _rect.Width);
            _control.Height = Math.Max(0, _rect.Height);
        }
    }
}
