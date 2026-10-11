using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Hider.Core;
using Hider.Core.Model;

namespace Hider.Areas
{
    /// <summary>Edit mode toolbar (FR-34, design canvas 3): pattern, settings, refresh (mosaic only), delete, done, size.</summary>
    public partial class EditToolbar : UserControl
    {
        public const double NormalWidth = 436;
        public const double WidthWithWarning = 540;
        public const double HeightDip = 36;

        public EditToolbar()
        {
            InitializeComponent();
            Width = NormalWidth;
            Height = HeightDip;
            FillItem.Header = Strings.PatternFill;
            MosaicItem.Header = Strings.PatternMosaic;
            GeneratedItem.Header = Strings.PatternGenerated;
            DoneLabel.Text = Strings.ToolbarDone;
            FailedText.Text = Strings.ToolbarCaptureFailed;
            ToolTip.SetTip(SettingsButton, Strings.ToolbarSettings);
            ToolTip.SetTip(RefreshButton, Strings.ToolbarRefresh);
            ToolTip.SetTip(DeleteButton, Strings.ToolbarDelete);
            AutomationSet(SettingsButton, Strings.ToolbarSettings);
            AutomationSet(RefreshButton, Strings.ToolbarRefresh);
            AutomationSet(DeleteButton, Strings.ToolbarDelete);

            FillItem.Click += (_, _) => PatternSelected?.Invoke(PatternKind.Fill);
            MosaicItem.Click += (_, _) => PatternSelected?.Invoke(PatternKind.Mosaic);
            GeneratedItem.Click += (_, _) => PatternSelected?.Invoke(PatternKind.Generated);
            SettingsButton.Click += (_, _) => SettingsClicked?.Invoke();
            RefreshButton.Click += (_, _) => RefreshClicked?.Invoke();
            DeleteButton.Click += (_, _) => DeleteClicked?.Invoke();
            DoneButton.Click += (_, _) => DoneClicked?.Invoke();
        }

        public event Action<PatternKind>? PatternSelected;
        public event Action? SettingsClicked;
        public event Action? RefreshClicked;
        public event Action? DeleteClicked;
        public event Action? DoneClicked;

        /// <summary>Updates the labels for an area: pattern name, ↻ only for mosaic, size in physical pixels (FR-34), warning (FR-52).</summary>
        public void Update(PatternKind _pattern, PhysicalRect _rect, bool _captureFailed)
        {
            PatternLabel.Text = _pattern switch
            {
                PatternKind.Fill => Strings.PatternFill,
                PatternKind.Mosaic => Strings.PatternMosaic,
                _ => Strings.PatternGenerated,
            };
            RefreshButton.IsVisible = _pattern == PatternKind.Mosaic;
            SizeText.Text = Strings.ToolbarSize(_rect.Width, _rect.Height);
            FailedText.IsVisible = _captureFailed;
            Width = _captureFailed ? WidthWithWarning : NormalWidth;
        }

        private static void AutomationSet(Control _control, string _name)
        {
            Avalonia.Automation.AutomationProperties.SetName(_control, _name);
        }
    }
}
