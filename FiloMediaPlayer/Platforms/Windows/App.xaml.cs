using Microsoft.UI.Xaml;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace FiloMediaPlayer.WinUI
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : MauiWinUIApplication
    {
        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            this.InitializeComponent();

            UnhandledException += OnUnhandledException;
        }

        // Safety net: a connection failure that escaped a page's own error
        // handling is reported instead of closing the app. Other exceptions
        // are left alone so real bugs still surface.
        private void OnUnhandledException(
            object sender,
            Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
        {
            var exception = e.Exception is AggregateException aggregate
                ? aggregate.GetBaseException()
                : e.Exception;

            if (exception == null || !FiloMediaPlayer.Services.ApiErrors.IsConnectionFailure(exception))
                return;

            e.Handled = true;

            System.Diagnostics.Debug.WriteLine($"Unhandled connection error: {exception}");

            MainThread.BeginInvokeOnMainThread(async () =>
            {
                try
                {
                    if (Shell.Current?.CurrentPage is Page page)
                        await FiloMediaPlayer.Services.ErrorPresenter.ShowAsync(page, exception, "complete that action");
                }
                catch (Exception)
                {
                }
            });
        }

        protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
    }

}
