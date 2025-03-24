namespace StandUpAlarmPOC
{
    public partial class AlarmDetailPage : ContentPage
    {
        private readonly AlarmModel _alarm;
        private readonly MainPage _mainPage;


        public AlarmDetailPage(AlarmModel alarm)
        {
            InitializeComponent();
            _alarm = alarm;
            BindingContext = _alarm;
        }

        protected override void OnDisappearing()
        {
            // Validate minute entry
            if (BindingContext is AlarmModel alarm)
            {
                alarm.ExactMinuteToStart = Math.Clamp(alarm.ExactMinuteToStart, 0, 59);
            }
            base.OnDisappearing();
        }
        private async void OnSaveClicked(object sender, EventArgs e)
        {
            // Validate minute input
            if (_alarm.ExactMinuteToStart < 0 || _alarm.ExactMinuteToStart > 59)
            {
                await DisplayAlert("Error", "Minute must be between 0-59", "OK");
                return;
            }

            // Validate time range
            if (_alarm.StartTime >= _alarm.EndTime)
            {
                await DisplayAlert("Error", "End time must be after start time", "OK");
                return;
            }

            await Navigation.PopAsync();
        }
    }
}