using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Hider.Core.Model;

namespace Hider.Areas
{
    /// <summary>
    /// Draws an area's pattern. Step 3: fill only; mosaic and generated patterns are drawn by the presenters added in
    /// Step 4 and show their base color until then, so the area always covers the content (NFR-05).
    /// </summary>
    public sealed class PatternView : Control
    {
        public Area? Model { get; set; }

        public override void Render(DrawingContext _context)
        {
            if (Model is null)
            {
                return;
            }
            string color = Model.Pattern switch
            {
                PatternKind.Fill => Model.Fill.Color,
                PatternKind.Generated => Model.Generated.Common.BaseColor,
                _ => Model.Generated.Common.BaseColor,
            };
            var (r, g, b) = ColorText.Parse(color);
            _context.FillRectangle(new SolidColorBrush(Color.FromRgb(r, g, b)), new Rect(Bounds.Size));
        }
    }
}
