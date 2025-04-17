using System.Security.Claims;
using System.Text.Json;
using Android.App;
using Android.Content;
using Android.Media;
using Android.OS;
using Android.Widget;

namespace StandUpAlarmPOC
{
    [Service(ForegroundServiceType = Android.Content.PM.ForegroundService.TypeDataSync)]
    public class AlarmService : Service
    {
        private Vibrator vibrator;

        private static IDispatcherTimer _timer;
        private static IDispatcher _dispatcher;
        public override IBinder OnBind(Intent intent)
        {
            return null;
        }
        private PowerManager.WakeLock wakeLock;

        public override void OnCreate()
        {
            base.OnCreate();
            vibrator = (Vibrator)GetSystemService(Context.VibratorService);

            var powerManager = GetSystemService(PowerService) as PowerManager;
            wakeLock = powerManager.NewWakeLock(WakeLockFlags.Partial, "VibrationWakelock");
            wakeLock.Acquire();
        }
        public static void Start(IDispatcher dispatcher)
        {
            _dispatcher = dispatcher;
            _timer = dispatcher.CreateTimer();
            _timer.Interval = TimeSpan.FromMinutes(1);
            _timer.Tick += (s, e) => CheckAlarms();
            _timer.Start();
        }

        private static async void CheckAlarms()
        {
            var currentTime = DateTime.Now;
            var alarms = GetAlarms();

            foreach (var alarm in alarms)
            {
                if (ShouldTriggerAlarm(alarm, currentTime))
                {
                    await TriggerVibration();
                }
            }
        }
        private static List<AlarmModel> GetAlarms()
        {
            if (Preferences.ContainsKey("Alarms"))
            {
                var alarmsJson = Preferences.Get("Alarms", "");
                return JsonSerializer.Deserialize<List<AlarmModel>>(alarmsJson);
            }
            return new List<AlarmModel>();
        }

        private static bool ShouldTriggerAlarm(AlarmModel alarm, DateTime currentTime)
        {
            return currentTime.Minute == alarm.ExactMinuteToStart &&
                   currentTime.TimeOfDay >= alarm.StartTime &&
                   currentTime.TimeOfDay <= alarm.EndTime &&
                   (!alarm.WorkWeek || (currentTime.DayOfWeek >= DayOfWeek.Monday &&
                                       currentTime.DayOfWeek <= DayOfWeek.Friday));
        }

        private static async Task TriggerVibration()
        {
            try
            {
                Intent serviceIntent = new Intent(Android.App.Application.Context, typeof(AlarmService));
                serviceIntent.PutExtra("duration", 8000);
                Android.App.Application.Context.StartForegroundService(serviceIntent);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Vibration error: {ex}");
            }
        }

        public override StartCommandResult OnStartCommand(Intent intent, StartCommandFlags flags, int startId)
        {
            StartForeground(1000, CreateNotification());

            if (intent != null && intent.HasExtra("duration"))
            {
                long duration = intent.GetLongExtra("duration", 8000);
                Vibrate(duration);
            }

            return StartCommandResult.Sticky;
        }

        private Notification CreateNotification()
        {
            var builder = new Notification.Builder(this);
            if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
            {
                builder.SetChannelId("vibration_channel");
            }
            builder.SetContentTitle("Vibration Service");
            builder.SetContentText("Running vibration service");
            return builder.Build();
        }

        private void Vibrate(long durationMs)
        {
            if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
            {
                var audioAttributesBuilder = new AudioAttributes.Builder();

                // Set content type to SONIFICATION (for system sounds/vibrations)
                audioAttributesBuilder.SetContentType(AudioContentType.Sonification);

                // Set usage to ALARM (highest priority for notifications)
                audioAttributesBuilder.SetUsage(AudioUsageKind.Alarm);

                var audioAttributes = audioAttributesBuilder.Build();

                vibrator.Vibrate(VibrationEffect.CreateOneShot(durationMs, 255),
                audioAttributes);
            }
            else
            {
                vibrator.Vibrate(durationMs);
            }
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            StopForeground(true);
            wakeLock.Release();
            vibrator.Cancel();
        }

        public static void RemoveAlarm(int alarmId)
        {
            var alarms = GetAlarms();
            var context = Android.App.Application.Context;

            var alarmToRemove = alarms.FirstOrDefault(a => a.Id == alarmId);
            if (alarmToRemove != null)
            {
                alarms.Remove(alarmToRemove);
                SaveAlarms(alarms);
                Toast.MakeText(context, "Vibrator for name of "+alarmToRemove.Name + " is now gone xD" 
                    , ToastLength.Long).Show();

            }
        }

        private static void SaveAlarms(List<AlarmModel> alarms)
        {
            var alarmsJson = JsonSerializer.Serialize(alarms);
            Preferences.Set("Alarms", alarmsJson);
        }
    }
}

