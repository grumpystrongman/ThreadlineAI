namespace Threadline.Windows
{
    public sealed partial class MainWindow
    {
        // Older partials still refer to the Threadline local API client by this name.
        // Keep the alias here so feature partials can converge without breaking the build.
        private global::Threadline.Windows.Services.ThreadlineLocalClient _threadlineClient => _client;

        // A few partial files use unqualified Visibility members. Keeping this bridge avoids
        // compile failures when those files do not import Microsoft.UI.Xaml directly.
        private static class Visibility
        {
            public static global::Microsoft.UI.Xaml.Visibility Visible => global::Microsoft.UI.Xaml.Visibility.Visible;
            public static global::Microsoft.UI.Xaml.Visibility Collapsed => global::Microsoft.UI.Xaml.Visibility.Collapsed;
        }
    }
}

namespace Threadline.Windows.UI.Core
{
    internal static class CoreVirtualKeyStates
    {
        public static global::Windows.UI.Core.CoreVirtualKeyStates Down => global::Windows.UI.Core.CoreVirtualKeyStates.Down;
    }
}
