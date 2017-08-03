using System;
using Windows.Networking.Connectivity;

namespace cameraFaceIdSample.Classes
{
    public class InternetConnectionChangedEventArgs : EventArgs
    {
        public InternetConnectionChangedEventArgs(bool isConnected)
        {
            this.isConnected = isConnected;
        }
        private bool isConnected;
        public bool IsConnected
        {
            get { return isConnected; }
        }
    }
    public static class Network
    {
        public static event EventHandler<InternetConnectionChangedEventArgs>
            InternetConnectionChanged;

        static Network()
        {
            NetworkInformation.NetworkStatusChanged += (s) =>
            {
                if (InternetConnectionChanged != null)
                {
                    var arg = new InternetConnectionChangedEventArgs(IsConnected);
                    InternetConnectionChanged(null, arg);
                }
            };
        }
        public static bool IsConnected
        {
            get
            {
                var profile = NetworkInformation.GetInternetConnectionProfile();
                var isConnected = (profile != null
                    && profile.GetNetworkConnectivityLevel() ==
                    NetworkConnectivityLevel.InternetAccess);
                return isConnected;
            }
        }
    }
}