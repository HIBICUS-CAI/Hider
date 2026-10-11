using Avalonia;
using Avalonia.Markup.Xaml;

namespace Hider
{
    public partial class HiderApp : Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            // No main window (FR-01): the tray icon and the restored areas are set up here from Step 3.
            base.OnFrameworkInitializationCompleted();
        }
    }
}
