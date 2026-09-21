using System;
using TqkLibrary.WpfUi;

namespace AndroidSyncControl.UI.ViewModels
{
    class MainWVM : BaseViewModel
    {
        private string _status = "Ready";
        public string Status
        {
            get => _status;
            set { _status = value; NotifyPropertyChange(); }
        }
    }
}
