using System.Collections.ObjectModel;
using System.Text.Json;
using Microsoft.Maui.Controls;
using StandUpAlarmPOC;

namespace StandUpAlarmPOC
{
    public partial class MainPage : ContentPage
    {
        public ObservableCollection<AlarmModel> Alarms { get; } = new();

        public MainPage()
        {
            InitializeComponent();
            BindingContext = this;
            LoadAlarms();
        }

        private async void OnAlarmTapped(object sender, EventArgs e)
        {
            if (sender is Border border && border.BindingContext is AlarmModel alarm)
            {
                await Navigation.PushAsync(new AlarmDetailPage(alarm));
            }
        }

        private async void AddAlarm(object sender, EventArgs e)
        {
            var newAlarm = new AlarmModel
            {
                Name = "New Alarm",
                StartTime = new TimeSpan(8, 0, 0),  // 08:00
                EndTime = new TimeSpan(17, 0, 0),    // 17:00
                ExactMinuteToStart = 0,
                WorkWeek = true
            };

            Alarms.Add(newAlarm);
            await Navigation.PushAsync(new AlarmDetailPage(newAlarm));
            SaveAlarms();
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            SaveAlarms(); // Auto-save when returning to main page
        }

        private void LoadAlarms()
        {
            if (Preferences.ContainsKey("Alarms"))
            {
                var alarmsJson = Preferences.Get("Alarms", "");
                var alarms = JsonSerializer.Deserialize<List<AlarmModel>>(alarmsJson);
                Alarms.Clear();
                foreach (var alarm in alarms) 
                    Alarms.Add(alarm);
            }
        }

        public void SaveAlarms()
        {
            var alarmsJson = JsonSerializer.Serialize(Alarms.ToList());
            Preferences.Set("Alarms", alarmsJson);
        }

        private void OnAlarmTapped(object sender, TappedEventArgs e)
        {

        }
        private async void OnDeleteClicked(object sender, EventArgs e)
        {
            var button = sender as Button;
            var alarmId = (int)button.CommandParameter;
            bool confirm = await DisplayAlert("Confirm Delete", "Are you sure you want to delete this alarm?", "Yes", "No");

            if (confirm)
            {
#if ANDROID
                AlarmService.RemoveAlarm(alarmId);
#endif
                var alarmToRemove = Alarms.FirstOrDefault(a => a.Id == alarmId);
                if (alarmToRemove != null)
                {
                    Alarms.Remove(alarmToRemove);
                    SaveAlarms();
                    // Refresh the BindingContext
                    BindingContext = null;
                    BindingContext = this;
                }
            }
        }
    }

}
