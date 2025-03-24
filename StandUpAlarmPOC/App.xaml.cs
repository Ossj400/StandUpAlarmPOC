namespace StandUpAlarmPOC
{
    public partial class App : Application
    {
        public App()
        {
            InitializeComponent();
            MainPage = new NavigationPage(new MainPage());
        }

        protected override Window CreateWindow(IActivationState activationState)
        {
            var window = base.CreateWindow(activationState);

            if (window.Page is NavigationPage navPage &&
                navPage.CurrentPage is MainPage mainPage)
            {
#if ANDROID
                // if window is for platform android then start the alarm service
                AlarmService.Start(mainPage.Dispatcher);
#endif
            }

            return window;
        }
    }
}