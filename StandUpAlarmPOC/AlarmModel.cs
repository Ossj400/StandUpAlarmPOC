using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace StandUpAlarmPOC
{
    public class AlarmModel : INotifyPropertyChanged
    {

        public int Id { get; }

        private string _name;
        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }

        private TimeSpan? _startTime;
        public TimeSpan? StartTime
        {
            get => _startTime;
            set { _startTime = value; OnPropertyChanged(); }
        }

        private TimeSpan? _endTime;
        public TimeSpan? EndTime
        {
            get => _endTime;
            set { _endTime = value; OnPropertyChanged(); }
        }

        // Add input validation for ExactMinuteToStart
        private int _exactMinuteToStart;
        public int ExactMinuteToStart
        {
            get => _exactMinuteToStart;
            set
            {
                _exactMinuteToStart = Math.Clamp(value, 0, 59);
                OnPropertyChanged();
            }
        }
        private bool _workWeek;
        public bool WorkWeek
        {
            get => _workWeek;
            set { _workWeek = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
