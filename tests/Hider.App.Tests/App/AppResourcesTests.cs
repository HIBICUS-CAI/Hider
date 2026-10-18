using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.VisualTree;

namespace Hider.App.Tests.App
{
    /// <summary>AppInfo, AppIcons, UiFonts and the application's XAML (themes).</summary>
    public class AppResourcesTests
    {
        private static readonly string[] monoFamilies = { "Cascadia Mono", "Consolas", "DejaVu Sans Mono", "monospace" };

        [Fact]
        public void GT41_AppInfo_Version_IsTheInformationalVersionWithoutTheRevision()
        {
            string informational = typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;

            Assert.Equal(informational.Split('+')[0], AppInfo.Version);
            Assert.DoesNotContain("+", AppInfo.Version);
        }

        [Fact]
        public void GT41_AppInfo_Version_MatchesTheAssemblyVersion()
        {
            // Both come from the one <Version> (Directory.Build.props, or build.py --version in CI).
            var version = typeof(AppInfo).Assembly.GetName().Version!;

            Assert.Equal($"{version.Major}.{version.Minor}.{version.Build}", AppInfo.Version);
        }

        [Fact]
        public void GT41_AppInfo_Version_IsReadOnce()
        {
            Assert.Same(AppInfo.Version, AppInfo.Version);
        }

        [AvaloniaFact]
        public void GT42_AppIcons_Image_Is32PxAndShared()
        {
            var image = AppIcons.Image;

            Assert.Equal(new PixelSize(32, 32), image.PixelSize);
            Assert.Same(image, AppIcons.Image);
        }

        [AvaloniaFact]
        public void GT42_AppIcons_WindowIconSources_AreTheMultiSizeIcoAndThe48PxPng()
        {
            // CreateWindowIcon takes the .ico on Windows and the PNG elsewhere (the headless icon keeps no pixels to check, so
            // the two sources are checked here).
            using (var ico = AssetLoader.Open(new Uri("avares://Hider/Assets/hider.ico")))
            {
                var header = new byte[6];
                ico.ReadExactly(header);
                Assert.Equal(new byte[] { 0, 0, 1, 0 }, header[..4]); // reserved 0, type 1 (icon)
                Assert.True(BitConverter.ToUInt16(header, 4) >= 4); // 16 / 20 / 24 / 32 at least
            }
            using var png = AssetLoader.Open(new Uri("avares://Hider/Assets/hider-48.png"));
            using var bitmap = new Bitmap(png);
            Assert.Equal(new PixelSize(48, 48), bitmap.PixelSize);
            Assert.NotNull(AppIcons.CreateWindowIcon());
        }

        [AvaloniaFact]
        public void GT42_AppIcons_WindowIcon_IsNewEachTime()
        {
            // Each tray icon / window owns its icon.
            Assert.NotSame(AppIcons.CreateWindowIcon(), AppIcons.CreateWindowIcon());
        }

        [AvaloniaTheory]
        [InlineData("hider.ico")]
        [InlineData("hider-32.png")]
        [InlineData("hider-48.png")]
        public void GT42_AppIcons_AssetsAreEmbedded(string _name)
        {
            Assert.True(AssetLoader.Exists(new Uri($"avares://Hider/Assets/{_name}")));
        }

        [Fact]
        public void GT43_UiFonts_Mono_IsMonospacedWithFallbacks_WindowsFirst()
        {
            Assert.Equal(monoFamilies, UiFonts.Mono.FamilyNames);
            Assert.Same(UiFonts.Mono, UiFonts.Mono);
        }

        [AvaloniaFact]
        public void GT43_HiderApp_Initialize_LoadsFluentAndTheColorPickerTheme_Light()
        {
            var app = new HiderApp();

            app.Initialize();

            Assert.Equal(ThemeVariant.Light, app.RequestedThemeVariant);
            Assert.Equal(2, app.Styles.Count);
            Assert.IsType<FluentTheme>(app.Styles[0]);
            // The StyleInclude is resolved at compile time (trim safe, spike S-7): its styles are loaded in place.
            var colorPicker = Assert.IsType<Styles>(app.Styles[1]);
            Assert.True(colorPicker.TryGetResource(typeof(ColorView), ThemeVariant.Light, out object? theme));
            Assert.IsType<ControlTheme>(theme);
        }

        [AvaloniaFact]
        public void GT43_ColorPickerTheme_IsApplied()
        {
            // Without the ColorPicker styles a ColorView has no template and the settings' colour flyout would be empty.
            var view = new ColorView();
            var window = new Window { Content = view };
            window.Show();

            Assert.NotEmpty(view.GetVisualChildren());
            window.Close();
        }
    }
}
