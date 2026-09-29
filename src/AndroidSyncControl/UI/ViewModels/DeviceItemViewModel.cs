using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AndroidSyncControl.UI.ViewModels
{
    public class DeviceItemViewModel : INotifyPropertyChanged
    {
        private bool _isSelected = true;
        private string _serial = string.Empty;
        private string _model = string.Empty;
        private string _networkType = "Unknown";
        private string _ipAddress = "---";
        private string _status = "Sẵn sàng";
        private string _statusColor = "#10B981";
        private int _progress = 0;
        private bool _isBusy = false;
        private bool _isCurrentMirror = false;

        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }

        public string Serial
        {
            get => _serial;
            set { _serial = value; OnPropertyChanged(); }
        }

        public string Model
        {
            get => _model;
            set { _model = value; OnPropertyChanged(); }
        }

        public string DisplayName => string.IsNullOrEmpty(Model) ? Serial : $"{Model} ({Serial})";

        public string NetworkType
        {
            get => _networkType;
            set { _networkType = value; OnPropertyChanged(); }
        }

        public string IpAddress
        {
            get => _ipAddress;
            set { _ipAddress = value; OnPropertyChanged(); }
        }

        public string Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged(); }
        }

        public string StatusColor
        {
            get => _statusColor;
            set { _statusColor = value; OnPropertyChanged(); }
        }

        public int Progress
        {
            get => _progress;
            set { _progress = value; OnPropertyChanged(); }
        }

        public bool IsBusy
        {
            get => _isBusy;
            set { _isBusy = value; OnPropertyChanged(); }
        }

        public bool IsCurrentMirror
        {
            get => _isCurrentMirror;
            set { _isCurrentMirror = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
    }
}
