using System.Collections.ObjectModel;
using System.Text.Json;
using Microsoft.Maui.Controls;
using StandUpAlarmPOC;

namespace StandUpAlarmPOC
{
    using Interfaces;

    public partial class MainPage : ContentPage
    {
        public ObservableCollection<AlarmModel> Alarms { get; } = new();
        private readonly ICamera _cameraService;
        private IDisposable _frameCaptureDisposable;
        private IImageProcessing _imageProcessor;

        public MainPage(ICamera cameraService, IImageProcessing imageProcessing)
        {
            InitializeComponent();
            BindingContext = this;
            LoadAlarms();
            _cameraService = cameraService;
            _imageProcessor = imageProcessing;
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
        private CancellationTokenSource _frameCaptureCancellationTokenSource;

        private async void StartTakingFrames(object sender, EventArgs e)
        {
#if ANDROID

            try
            {
                await _cameraService.InitializeCameraAsync();

                _frameCaptureCancellationTokenSource = new CancellationTokenSource();
                await StartFrameCaptureLoop(_frameCaptureCancellationTokenSource.Token);
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"An error occurred while starting frame capture: {ex.Message}", "OK");
            }
#endif

        }

        private async Task StartFrameCaptureLoop(CancellationToken cancellationToken)
        {
            try
            {
                 Task.Run(async () =>
                {
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        await ProcessFrame();
                        await Task.Delay(6500); // 0.2 seconds
                    }
                }, cancellationToken);
            }
            catch (Exception ex)
            {
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    await DisplayAlert("Error", $"Frame capture loop error: {ex.Message}", "OK");
                });
            }
        }

        private void StopTakingFrames(object sender, EventArgs e)
        {
            _frameCaptureCancellationTokenSource?.Cancel();
            _frameCaptureCancellationTokenSource = null;
        }
        private async Task ProcessFrame()
        {
            try
            {
                await _cameraService.OpenCameraAsync();
                await _cameraService.EnsureCaptureSessionAsync();
                using var stream = await _cameraService.CaptureFrameAsync();

                var fileName = $"frame_{DateTime.Now:yyyyMMdd_HHmmssfff}.jpg";
                var filePath = Path.Combine(FileSystem.CacheDirectory, fileName);
                ImageSource img;
                string txt = string.Empty;
                using var memoryStream = new MemoryStream();
                await stream.CopyToAsync(memoryStream);
                memoryStream.Seek(0, SeekOrigin.Begin); // rewind to start
                                                        //   (img, txt) = await _imageProcessor.ProcessUploadedImage(memoryStream);
                (img, txt)  = await _imageProcessor.ProcessUploadedImage(memoryStream, 
                    img  =>ResultImage.Source = img,
                    txt => DetectedText.Text = txt );
                using (var fileStream = File.Create(filePath))
                {
                    await stream.CopyToAsync(fileStream);
                }


                // Optional: Display path in UI
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    ResultImage.Source = img;
                    DetectedText.Text = txt;
                    CapturedFrameImage.Source = ImageSource.FromFile(filePath);
                });

            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Frame error: {ex.Message}", "OK");
            }
            finally
            {
                await _cameraService.CloseCameraAsync();

            }
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
