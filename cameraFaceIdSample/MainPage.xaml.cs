#region Libraries
using cameraFaceIdSample.Classes;
using Microsoft.ProjectOxford.Face;
using Microsoft.ProjectOxford.Face.Contract;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Microsoft.WindowsAzure.MobileServices;
using System.Collections.Generic;
using Windows.UI.Xaml.Media;
using Windows.Devices.Geolocation;
using Windows.Services.Maps;
using Windows.UI.Core;
using Windows.Media.Capture;
using Windows.Networking.Sockets;
using System.Linq;
using Windows.Networking;
using Windows.Networking.Connectivity;
using Windows.Media.MediaProperties;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Media.SpeechRecognition;
using System.Text;
using Windows.Globalization;
using Windows.UI.Xaml.Documents;
using Windows.UI.Xaml.Navigation;
using Windows.ApplicationModel.Core;
using Windows.Foundation;
using Windows.UI.Xaml.Controls.Maps;
using Windows.UI.Xaml.Input;
using Windows.UI.ViewManagement;

#endregion Libraries    

namespace cameraFaceIdSample
{
    public sealed partial class MainPage : Page
    {
        #region Global Attributes
        IMobileServiceTable<UsersUPT> userTableObject = App.MobileService.GetTable<UsersUPT>();
        bool CheckNetwork = true;
        bool isExpanded=false;
        string currentVisualState;
        FaceDetectionFrameProcessor faceDetectionProcessor;
        CancellationTokenSource requestStopCancellationToken;
        CameraPreviewManager cameraPreviewManager;
        FacialDrawingHandler drawingHandler;
        Geolocator _geolocator = null;
        static readonly string OxfordApiKey = "12476023b4c349939778c49e5db321d6";
        //static readonly string OxfordApiKey = "2bddec152651472a8cb690e00db31a43";Diego
        volatile TaskCompletionSource<SoftwareBitmap> copiedVideoFrameComplete;
        int _port = 13337;
        MediaCapture _mediaCap;
        StreamSocketListener _listener;
        ManualResetEvent _signal = new ManualResetEvent(false);
        List<Connection> _connections = new List<Connection>();
        internal CurrentVideo CurrentVideo = new CurrentVideo();
        Synthesizer sinth;
        SpeechRecognizer speechRecognizer;
        bool isListening;
        StringBuilder dictatedTextBuilder;
        static uint HResultPrivacyStatementDeclined = 0x80045509;
        public double latitude = 0;
        public double longitude = 0;
        BasicGeoposition geoPosition = new BasicGeoposition();
        #endregion Global Attributes
        public MainPage()
        {
            #region Initialize Components
            InitializeComponent();
            ApplicationView.PreferredLaunchWindowingMode = ApplicationViewWindowingMode.FullScreen;
            sinth = new Synthesizer(listboxOcult);
            isListening = false;
            dictatedTextBuilder = new StringBuilder();
            stackpanelNames.Visibility = Visibility.Collapsed;
            InitCamera();
            imgConnectivity.Visibility = (Network.IsConnected ? Visibility.Collapsed : Visibility.Visible);
            CheckNetwork = (Network.IsConnected ? true : false);
            Debug.WriteLine("Actuamente tu internet esta: " + CheckNetwork);
            if (CheckNetwork == true)
            {
                sinth.StartSpeaking(media, "Iniciando el sistema,   ,   ,  ,  ,   ,   ,   ,   ,   ,   ,    ,    ,    ,    ,Preparando Componentes" +
                    ",   ,   ,  ,  ,   ,   ,   ,   ,   ,   ,    ,    ,    ,    ,Conexion a internet exitosa");
            }
            else
            {
                sinth.StartSpeaking(media, "Iniciando el sistema,   ,   ,  ,  ,   ,   ,   ,   ,   ,   ,    ,    ,    ,    ,Preparando Componentes" +
                    ",   ,   ,  ,  ,   ,   ,   ,   ,   ,   ,    ,    ,    ,    ,No hay conexion a internet");
            }
            Network.InternetConnectionChanged += async (s, e) =>
            {
                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    CheckNetwork = e.IsConnected;
                    Debug.WriteLine("Internet cambió a: " + e.IsConnected);
                    imgConnectivity.Visibility =
                        (e.IsConnected ? Visibility.Collapsed : Visibility.Visible);
                    stackpanelInternet.Visibility = (e.IsConnected ? Visibility.Collapsed : Visibility.Visible);
                    if (CheckNetwork == true)
                    {
                        sinth.StartSpeaking(media, "Conexion a internet exitosa");
                    }
                    else
                    {
                        sinth.StartSpeaking(media, "No hay conexion a internet");
                    }
                });
            };
        }
        string CurrentVisualState
        {
            get
            {
                return (this.currentVisualState);
            }
            set
            {
                if (this.currentVisualState != value)
                {
                    this.currentVisualState = value;
                }
            }
        }
        #endregion Initialize Components
        #region SpeechRecognizer
        private void PopulateLanguageDropdown()
        {
            Language defaultLanguage = SpeechRecognizer.SystemSpeechLanguage;
            IEnumerable<Language> supportedLanguages = SpeechRecognizer.SupportedTopicLanguages;
            foreach (Language lang in supportedLanguages)
            {
                ComboBoxItem item = new ComboBoxItem();
                item.Tag = lang;
                item.Content = lang.DisplayName;
                cbLanguageSelection.Items.Add(item);
                if (lang.LanguageTag == defaultLanguage.LanguageTag)
                {
                    item.IsSelected = true;
                    cbLanguageSelection.SelectedItem = item;
                }
            }
        }
        private async void cbLanguageSelection_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (speechRecognizer != null)
            {
                ComboBoxItem item = (ComboBoxItem)(cbLanguageSelection.SelectedItem);
                Language newLanguage = (Language)item.Tag;
                if (speechRecognizer.CurrentLanguage != newLanguage)
                {
                    // trigger cleanup and re-initialization of speech.
                    try
                    {
                        await InitializeRecognizer(newLanguage);
                    }
                    catch (Exception exception)
                    {
                        var messageDialog = new Windows.UI.Popups.MessageDialog(exception.Message, "Exception");
                        await messageDialog.ShowAsync();
                    }
                }
            }
        }
        private async Task InitializeRecognizer(Language recognizerLanguage)
        {
            if (speechRecognizer != null)
            {
                // cleanup prior to re-initializing this scenario.
                speechRecognizer.StateChanged -= SpeechRecognizer_StateChanged;
                speechRecognizer.ContinuousRecognitionSession.Completed -= ContinuousRecognitionSession_Completed;
                speechRecognizer.ContinuousRecognitionSession.ResultGenerated -= ContinuousRecognitionSession_ResultGenerated;
                speechRecognizer.HypothesisGenerated -= SpeechRecognizer_HypothesisGenerated;
                speechRecognizer.Dispose();
                speechRecognizer = null;
            }

            this.speechRecognizer = new SpeechRecognizer(recognizerLanguage);

            // Provide feedback to the user about the state of the recognizer. This can be used to provide visual feedback in the form
            // of an audio indicator to help the user understand whether they're being heard.
            speechRecognizer.StateChanged += SpeechRecognizer_StateChanged;

            // Apply the dictation topic constraint to optimize for dictated freeform speech.
            var dictationConstraint = new SpeechRecognitionTopicConstraint(SpeechRecognitionScenario.Dictation, "dictation");
            speechRecognizer.Constraints.Add(dictationConstraint);
            SpeechRecognitionCompilationResult result = await speechRecognizer.CompileConstraintsAsync();
            if (result.Status != SpeechRecognitionResultStatus.Success)
            {
                btnContinuousRecognize.IsEnabled = false;
            }
            speechRecognizer.ContinuousRecognitionSession.Completed += ContinuousRecognitionSession_Completed;
            speechRecognizer.ContinuousRecognitionSession.ResultGenerated += ContinuousRecognitionSession_ResultGenerated;
            speechRecognizer.HypothesisGenerated += SpeechRecognizer_HypothesisGenerated;
        }
        private async void ContinuousRecognitionSession_Completed(SpeechContinuousRecognitionSession sender, SpeechContinuousRecognitionCompletedEventArgs args)
        {
            if (args.Status != SpeechRecognitionResultStatus.Success)
            {
                // If TimeoutExceeded occurs, the user has been silent for too long. We can use this to 
                // cancel recognition if the user in dictation mode and walks away from their device, etc.
                // In a global-command type scenario, this timeout won't apply automatically.
                // With dictation (no grammar in place) modes, the default timeout is 20 seconds.
                if (args.Status == SpeechRecognitionResultStatus.TimeoutExceeded)
                {
                    await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                    {
                        cbLanguageSelection.IsEnabled = true;
                        dictationTextBox.Text = dictatedTextBuilder.ToString();
                        isListening = false;
                    });
                }
                else
                {
                    await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                    {
                        cbLanguageSelection.IsEnabled = true;
                        isListening = false;
                    });
                }
            }
        }
        private async void SpeechRecognizer_HypothesisGenerated(SpeechRecognizer sender, SpeechRecognitionHypothesisGeneratedEventArgs args)
        {
            string hypothesis = args.Hypothesis.Text;
            // Update the textbox with the currently confirmed text, and the hypothesis combined.
            string textboxContent = dictatedTextBuilder.ToString() + " " + hypothesis + " ...";
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                dictationTextBox.Text = textboxContent;
                btnClearText.IsEnabled = false;
            });
        }
        private async void ContinuousRecognitionSession_ResultGenerated(SpeechContinuousRecognitionSession sender, SpeechContinuousRecognitionResultGeneratedEventArgs args)
        {
            // We may choose to discard content that has low confidence, as that could indicate that we're picking up
            // noise via the microphone, or someone could be talking out of earshot.
            string command = "";
            if (args.Result.Confidence == SpeechRecognitionConfidence.Medium ||
                args.Result.Confidence == SpeechRecognitionConfidence.High)
            {
                dictatedTextBuilder.Append(args.Result.Text + " ");
                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    discardedTextBlock.Visibility = Visibility.Collapsed;
                    dictationTextBox.Text = dictatedTextBuilder.ToString();
                    btnClearText.IsEnabled = false;
                    command = dictationTextBox.Text;
                    Task.Delay(TimeSpan.FromSeconds(3));
                    Debug.WriteLine("Comando: " + command.Trim(' ').Trim('.'));
                    dictatedTextBuilder.Clear();
                    dictationTextBox.Text = "";
                });
                if (command.Trim(' ').Trim('.') == "12345")
                {
                    Debug.WriteLine("Dijiste el comando 1");
                    command = "";
                }
                else if (command.Trim(' ').Trim('.') == "Analizar")
                {
                    Debug.WriteLine("Dijiste el comando Analizar");
                    if (CheckNetwork == true)
                    {
                        ProcessAll();
                    }
                    command = "";
                }
                else if (command.Trim(' ').Trim('.') == "Salir")
                {
                    Debug.WriteLine("Dijiste el comando Salir");
                    CoreApplication.Exit();
                    command = "";
                }
                else if (command.Trim(' ').Trim('.') == "Cerrar")
                {
                    Debug.WriteLine("Dijiste el comando Cerrar");
                    CoreApplication.Exit();
                    command = "";
                }
            }
            else
            {
                // In some scenarios, a developer may choose to ignore giving the user feedback in this case, if speech
                // is not the primary input mechanism for the application.
                // Here, just remove any hypothesis text by resetting it to the last known good.
                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    dictationTextBox.Text = dictatedTextBuilder.ToString();
                    string discardedText = args.Result.Text;

                    if (!string.IsNullOrEmpty(discardedText))
                    {
                        discardedText = discardedText.Length <= 25 ? discardedText : (discardedText.Substring(0, 25) + "...");
                        discardedTextBlock.Text = "Discarded due to low/rejected Confidence: " + discardedText;
                    }
                });
            }
        }
        private async void SpeechRecognizer_StateChanged(SpeechRecognizer sender, SpeechRecognizerStateChangedEventArgs args)
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
            });
        }
        public async void ContinuousRecognize_Click(object sender, RoutedEventArgs e)
        {
            btnContinuousRecognize.Visibility = Visibility.Collapsed;
            btnContinuousRecognize.IsEnabled = false;
            if (isListening == false)
            {
                // The recognizer can only start listening in a continuous fashion if the recognizer is currently idle.
                // This prevents an exception from occurring.
                if (speechRecognizer.State == SpeechRecognizerState.Idle)
                {
                    cbLanguageSelection.IsEnabled = false;
                    hlOpenPrivacySettings.Visibility = Visibility.Collapsed;
                    discardedTextBlock.Visibility = Visibility.Collapsed;
                    try
                    {
                        isListening = true;
                        Debug.WriteLine("Escuchando");
                        await speechRecognizer.ContinuousRecognitionSession.StartAsync();
                    }
                    catch (Exception ex)
                    {
                        if ((uint)ex.HResult == HResultPrivacyStatementDeclined)
                        {
                            // Show a UI link to the privacy settings.
                            hlOpenPrivacySettings.Visibility = Visibility.Visible;
                        }
                        else
                        {
                            var messageDialog = new Windows.UI.Popups.MessageDialog(ex.Message, "Exception");
                            await messageDialog.ShowAsync();
                        }
                        isListening = false;
                        cbLanguageSelection.IsEnabled = true;
                    }
                }
            }
            else
            {
                isListening = false;
                cbLanguageSelection.IsEnabled = true;
                Debug.WriteLine("escuchando 2");
                if (speechRecognizer.State != SpeechRecognizerState.Idle)
                {
                    // Cancelling recognition prevents any currently recognized speech from
                    // generating a ResultGenerated event. StopAsync() will allow the final session to 
                    // complete.
                    try
                    {
                        await speechRecognizer.ContinuousRecognitionSession.StopAsync();
                        Debug.WriteLine("escuchando 3");
                        // Ensure we don't leave any hypothesis text behind
                        dictationTextBox.Text = dictatedTextBuilder.ToString();
                    }
                    catch (Exception exception)
                    {
                        var messageDialog = new Windows.UI.Popups.MessageDialog(exception.Message, "Exception");
                        await messageDialog.ShowAsync();
                    }
                }
            }
            btnContinuousRecognize.IsEnabled = true;
        }
        private void btnClearText_Click(object sender, RoutedEventArgs e)
        {
            btnClearText.IsEnabled = false;
            dictatedTextBuilder.Clear();
            dictationTextBox.Text = "";
            discardedTextBlock.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
            // Avoid setting focus on the text box, since it's a non-editable control.
            btnContinuousRecognize.Focus(FocusState.Programmatic);
        }
        private void dictationTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var grid = (Grid)VisualTreeHelper.GetChild(dictationTextBox, 0);
            for (var i = 0; i <= VisualTreeHelper.GetChildrenCount(grid) - 1; i++)
            {
                object obj = VisualTreeHelper.GetChild(grid, i);
                if (!(obj is ScrollViewer))
                {
                    continue;
                }
                ((ScrollViewer)obj).ChangeView(0.0f, ((ScrollViewer)obj).ExtentHeight, 1.0f);
                break;
            }
        }
        private async void openPrivacySettings_Click(Hyperlink sender, HyperlinkClickEventArgs args)
        {
            await Windows.System.Launcher.LaunchUriAsync(new Uri("ms-settings:privacy-speechtyping"));
        }
        protected async override void OnNavigatedTo(NavigationEventArgs e)
        {
            // Prompt the user for permission to access the microphone. This request will only happen
            // once, it will not re-prompt if the user rejects the permission.
            bool permissionGained = await AudioCapturePermissions.RequestMicrophonePermission();
            if (permissionGained)
            {
                btnContinuousRecognize.IsEnabled = true;
                PopulateLanguageDropdown();
                await InitializeRecognizer(SpeechRecognizer.SystemSpeechLanguage);
            }
            else
            {
                this.dictationTextBox.Text = "Permission to access capture resources was not given by the user, reset the application setting in Settings->Privacy->Microphone.";
                btnContinuousRecognize.IsEnabled = false;
                cbLanguageSelection.IsEnabled = false;
            }
        }
        #endregion SpeechRecognizer
        #region VideoStreaming
        private async Task BeginRecording()
        {
            while (true)
            {
                try
                {
                    Debug.WriteLine($"Recording started");
                    var memoryStream = new InMemoryRandomAccessStream();
                    await _mediaCap.StartRecordToStreamAsync(MediaEncodingProfile.CreateMp4(VideoEncodingQuality.Vga), memoryStream);
                    await Task.Delay(TimeSpan.FromSeconds(5));
                    await _mediaCap.StopRecordAsync();
                    Debug.WriteLine($"Recording finished, {memoryStream.Size} bytes");
                    memoryStream.Seek(0);
                    CurrentVideo.Id = Guid.NewGuid();
                    CurrentVideo.Data = new byte[memoryStream.Size];
                    await memoryStream.ReadAsync(CurrentVideo.Data.AsBuffer(), (uint)memoryStream.Size, InputStreamOptions.None);
                    Debug.WriteLine($"Bytes written to stream");
                    _signal.Set();
                    _signal.Reset();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"StartRecording -> {ex.Message}");
                    break;
                }
            }
        }
        private async Task StartListener()
        {
            Debug.WriteLine($"Starting listener");
            _listener = new StreamSocketListener();
            _listener.ConnectionReceived += (sender, args) =>
            {
                Debug.WriteLine($"Connection received from {args.Socket.Information.RemoteAddress}");
                _connections.Add(new Connection(args.Socket, this));
            };
            HostName host = NetworkInformation.GetHostNames().FirstOrDefault(x => x.IPInformation != null && x.Type == HostNameType.Ipv4);
            await _listener.BindEndpointAsync(host, $"{_port}");
            Debug.WriteLine($"Listener started on {host.DisplayName}:{_listener.Information.LocalPort}");
        }
        internal byte[] GetCurrentVideoDataAsync(Guid guid)
        {
            if (CurrentVideo.Id == Guid.Empty || CurrentVideo.Id == guid)
                _signal.WaitOne();
            return CurrentVideo.Id.ToByteArray().Concat(CurrentVideo.Data).ToArray();
        }
        #endregion VideoStreaming
        #region Database Search
        private async void Query(string idBuscar)
        {
            List<UsersUPT> lista = new List<UsersUPT>();
            Debug.WriteLine("El id a buscar es: " + idBuscar);
            try
            {
                lista = await userTableObject.Where(userTableObj => userTableObj.PID == idBuscar).ToListAsync();
                list_Name.ItemsSource = lista;
                list_Name.DisplayMemberPath = "nombre";
                var obj = lista.First();
                list_Age.ItemsSource = lista;
                list_Age.DisplayMemberPath = "edad";
                list_description.ItemsSource = lista;
                list_description.DisplayMemberPath = "descripcion";
                sinth.StartSpeaking(media, "Nombre: ,    ,    ,    ,    ,    ,    , " + obj.nombre + ",   ,   ,   " +
        ",   ,   ,Edad:   ,   ,   ,   ,   ," + obj.edad + " años,   ,   ,   ,   ,Descripcion:,   ,   ,   ,   ,   ," + obj.descripcion);
            }
            catch (Exception ex)
            {
                Debug.Write("Error: " + ex);
            }
        }
        #endregion Database Search
        #region Camera Live preview
        async void InitCamera()
        {
            CurrentVisualState = "Playing";
            requestStopCancellationToken = new CancellationTokenSource();
            cameraPreviewManager = new CameraPreviewManager(this.captureElement);
            var videoProperties =
              await cameraPreviewManager.StartPreviewToCaptureElementAsync(
                vcd => vcd.EnclosureLocation.Panel == Windows.Devices.Enumeration.Panel.Front);
            faceDetectionProcessor = new FaceDetectionFrameProcessor(
            cameraPreviewManager.MediaCapture,
            cameraPreviewManager.VideoProperties);
            drawingHandler = new FacialDrawingHandler(
            drawCanvas, videoProperties, Colors.Blue);
            faceDetectionProcessor.FrameProcessed += (s, e) =>
            {
                drawingHandler.SetLatestFrameReceived(e.Results);
                CurrentVisualState =
                  e.Results.Count > 0 ? "PlayingWithFace" : "Playing";
                CopyBitmapForOxfordIfRequestPending(e.Frame.SoftwareBitmap);
            };
            try
            {
                await faceDetectionProcessor.RunFrameProcessingLoopAsync(
                  requestStopCancellationToken.Token);
            }
            catch (OperationCanceledException ex)
            {
                Debug.Write("Error: " + ex);
            }
            await cameraPreviewManager.StopPreviewAsync();
            requestStopCancellationToken.Dispose();
            CurrentVisualState = "Stopped";
        }
        #endregion Camera Live preview
        #region Oxford Bitmap
        void CopyBitmapForOxfordIfRequestPending(SoftwareBitmap bitmap)
        {
            if ((copiedVideoFrameComplete != null) &&
             (!copiedVideoFrameComplete.Task.IsCompleted))
            {
                var convertedRgba16Bitmap = SoftwareBitmap.Convert(bitmap,
                  BitmapPixelFormat.Rgba16);
                copiedVideoFrameComplete.SetResult(convertedRgba16Bitmap);
            }
        }
        #endregion Oxford Bitmap
        #region Geolocation
        private async Task StartTracking()
        {
            var accessStatus = await Geolocator.RequestAccessAsync();
            switch (accessStatus)
            {
                case GeolocationAccessStatus.Allowed:
                    _geolocator = new Geolocator { ReportInterval = 1000 };
                    _geolocator.PositionChanged += OnPositionChanged;
                    break;
            }
        }
        async private void OnPositionChanged(Geolocator sender, PositionChangedEventArgs e)
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                UpdateLocationData(e.Position);
            });
        }
        private async Task UpdateLocationData(Geoposition position)
        {
            if (position != null)
            {
                tbLatitude.Text = position.Coordinate.Point.Position.Latitude.ToString();
                tbLongitude.Text = position.Coordinate.Point.Position.Longitude.ToString();
                this.latitude = position.Coordinate.Point.Position.Latitude;
                this.longitude = position.Coordinate.Point.Position.Longitude;
                await GetTown(position.Coordinate.Point.Position.Latitude, position.Coordinate.Point.Position.Longitude);
            }
        }
        private async Task GetTown(double latitude, double longitude)
        {
            Debug.WriteLine("Entraste al metodo GetTown {0},{1}\n", latitude, longitude);
            try
            {
                BasicGeoposition location = new BasicGeoposition();
                location.Latitude = latitude;
                location.Longitude = longitude;
                Geopoint pointToReverseGeocode = new Geopoint(location);
                MapLocationFinderResult result =
                await MapLocationFinder.FindLocationsAtAsync(pointToReverseGeocode);
                if (result.Status == MapLocationFinderStatus.Success)
                {
                    tbStreet.Text = result.Locations[0].Address.Street;
                    tbDistrict.Text = result.Locations[0].Address.District;
                    tbTown.Text = result.Locations[0].Address.Town;
                    tbCountry.Text = result.Locations[0].Address.Country;
                    Debug.WriteLine("Town: " + result.Locations[0].Address.Town);
                    Debug.WriteLine("district: " + result.Locations[0].Address.District);
                    Debug.WriteLine("Country: " + result.Locations[0].Address.Country);
                    Debug.WriteLine("Street: " + result.Locations[0].Address.Street);
                    Mapping(latitude, longitude);
                }
            }
            catch (Exception e)
            {
                Debug.WriteLine(e.Message.ToString());
            }

        }
        private async void MyMap_Loaded(object sender, RoutedEventArgs e)
        {
            if (MyMap.Is3DSupported)
            {
                // Estilo del mapa
                MyMap.Style = MapStyle.Aerial3DWithRoads;
                // llave de bing
                MyMap.MapServiceToken = "iXNUzZxjjglXbxTQI3u2~5bxFRFZESkZUVlrEuPtCxg~AmcMVqUdyZW960FDSNF28-MUt_Thri564P4V3oHEyVEATyV-dHL9DdkBBRuxsdmI";

                
                geoPosition.Latitude = this.latitude;
                //geoPosition.Latitude = 20.0791441598;
                geoPosition.Longitude = this.longitude;
                //geoPosition.Longitude = -98.3714238064418;
                // obtiene posición
                Geopoint myPoint = new Geopoint(geoPosition);
                // crea POI
                MapIcon myPOI = new MapIcon { Location = myPoint, Title = "Position", NormalizedAnchorPoint = new Point(0.5, 1.0), ZIndex = 0 };
                // Despliega una imagen de un MapIcon
                myPOI.Image = RandomAccessStreamReference.CreateFromUri(new Uri("ms-appx:///Assets/pin.png"));
                // Agrega el mapa y lo centra
                MyMap.MapElements.Add(myPOI);
                MyMap.Center = myPoint;
                MyMap.ZoomLevel = 10;
                Debug.WriteLine("Coordenadas: " + latitude + " y " + longitude);
                MapScene mapScene = MapScene.CreateFromLocationAndRadius(new Geopoint(geoPosition), 200, 150, 70);
                MyMap.Style = MapStyle.Aerial3DWithRoads;
                await MyMap.TrySetSceneAsync(mapScene);
            }
        }
        public async void Mapping(double lat, double lon)
        {
            if (MyMap.Is3DSupported)
            {
                // Estilo del mapa
                MyMap.Style = MapStyle.Road;
                // llave de bing
                MyMap.MapServiceToken = "iXNUzZxjjglXbxTQI3u2~5bxFRFZESkZUVlrEuPtCxg~AmcMVqUdyZW960FDSNF28-MUt_Thri564P4V3oHEyVEATyV-dHL9DdkBBRuxsdmI";

                BasicGeoposition geoPosition = new BasicGeoposition();
                geoPosition.Latitude = lat;
                //geoPosition.Latitude = 20.0791441598;
                geoPosition.Longitude = lon;
                //geoPosition.Longitude = -98.3714238064418;
                // obtiene posición
                Geopoint myPoint = new Geopoint(geoPosition);
                // crea POI
                MapIcon myPOI = new MapIcon { Location = myPoint, Title = "Position", NormalizedAnchorPoint = new Point(0.5, 1.0), ZIndex = 0 };
                // Despliega una imagen de un MapIcon
                myPOI.Image = RandomAccessStreamReference.CreateFromUri(new Uri("ms-appx:///Assets/pin.png"));
                // Agrega el mapa y lo centra
                MyMap.MapElements.Add(myPOI);
                MyMap.Center = myPoint;
                MyMap.ZoomLevel = 10;
                Debug.WriteLine("Coordenadas: " + latitude + " y " + longitude);
                MapScene mapScene = MapScene.CreateFromLocationAndRadius(new Geopoint(geoPosition), 200, 150, 70);
                await MyMap.TrySetSceneAsync(mapScene);
            }
        }
        #endregion Geolocation
        #region Complete Process
        async void ProcessAll()
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                stackpanelAlert.Visibility = Visibility.Collapsed;
                stackpanel.Visibility = Visibility.Collapsed;
                imgCaution.Visibility = Visibility.Collapsed;
                imgGlasses.Visibility = Visibility.Collapsed;
                imgClean.Visibility = Visibility.Collapsed;
                imgNoFaces.Visibility = Visibility.Collapsed;
                stackpanelNames.Visibility = Visibility.Collapsed;
                stackpanelInternet.Visibility = Visibility.Collapsed;
            });
            if (CheckNetwork == true)
            {
                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>

                {
                    ScanModeTxt.Text = "SCAN MODE: ENABLED";
                    MyMap.Width = 100;
                    MyMap.Height = 100;
                    MyMap.Margin = new Thickness(5, -300, 0, 0);
                });
                try
                {
                    copiedVideoFrameComplete = new TaskCompletionSource<SoftwareBitmap>();
                    var bgra16CopiedFrame = await copiedVideoFrameComplete.Task;
                    copiedVideoFrameComplete = null;
                    InMemoryRandomAccessStream destStream = new InMemoryRandomAccessStream();
                    BitmapEncoder encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, destStream);
                    encoder.SetSoftwareBitmap(bgra16CopiedFrame);
                    await encoder.FlushAsync();
                    FaceServiceClient faceService = new FaceServiceClient(OxfordApiKey);
                    FaceServiceClient faceService1 = new FaceServiceClient(OxfordApiKey);
                    var requiredFaceAttributes = new FaceAttributeType[]
                    {FaceAttributeType.Age, FaceAttributeType.Gender, FaceAttributeType.Glasses};
                    Face[] faces = await faceService.DetectAsync(destStream.AsStream(), returnFaceLandmarks: true, returnFaceAttributes: requiredFaceAttributes);
                    try
                    {
                        if (faces.Length >= 1)
                        {
                            Debug.WriteLine("ID de rostro: " + faces[0].FaceId);
                            Guid idGuid = Guid.Parse(faces[0].FaceId.ToString());
                            SimilarPersistedFace[] facescomp = await faceService1.FindSimilarAsync(idGuid, "21122012", 1);
                            double confidence = Double.Parse(facescomp[0].Confidence.ToString());
                            string persistentID = facescomp[0].PersistedFaceId.ToString();
                            Debug.WriteLine("PID: " + facescomp[0].PersistedFaceId);
                            Debug.WriteLine("conf: " + facescomp[0].Confidence);
                            string lentes = faces[0].FaceAttributes.Glasses.ToString();
                            try
                            {
                                if (lentes == "NoGlasses")
                                {
                                    try
                                    {
                                        if (confidence >= .67)
                                        {
                                            await StartTracking();
                                            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                                            {
                                                stackpanel.Visibility = Visibility.Visible;
                                                stackpanelNames.Visibility = Visibility.Visible;
                                                stackpanelAlert.Width = 496;
                                                stackpanelAlert.Visibility = Visibility.Visible;
                                                stackpanelAlert.Background = new SolidColorBrush(Colors.Red);
                                                imgCaution.Visibility = Visibility.Visible;
                                                imgGlasses.Visibility = Visibility.Collapsed;
                                                imgClean.Visibility = Visibility.Collapsed;
                                                imgNoFaces.Visibility = Visibility.Collapsed;
                                                Debug.WriteLine("Usuario encontrado");
                                                Query(facescomp[0].PersistedFaceId.ToString());
                                            });
                                            sinth.StartSpeaking(media, "Nombre:,   ,   ,   ,   ," + (list_Name.SelectedItems[0]) + "Edad:,   ,   ,   ,   ," + (list_Name.SelectedItems[0]) +
                "Descripcion:,   ,   ,   ,   ," + (list_Name.SelectedItems[0]));
                                            Debug.WriteLine((list_Name.SelectedItems[0]) + "\n" + (list_Name.SelectedItems[0]) +
                                                 "\n" + (list_Name.SelectedItems[0]));
                                            //Video Stream
                                            //await StartListener();
                                            //await BeginRecording();
                                            //Mapping();
                                        }
                                        else
                                        {
                                            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                                            {
                                                stackpanelNames.Visibility = Visibility.Collapsed;
                                                stackpanelAlert.Width = 550;
                                                stackpanelAlert.Visibility = Visibility.Visible;
                                                stackpanelAlert.Background = new SolidColorBrush(Colors.Green);
                                                imgCaution.Visibility = Visibility.Collapsed;
                                                imgClean.Visibility = Visibility.Visible;
                                                imgGlasses.Visibility = Visibility.Collapsed;
                                                Debug.WriteLine("Usuario no identificado");
                                                sinth.StartSpeaking(media, "Usuario no identificado");
                                                stackpanel.Visibility = Visibility.Collapsed;
                                                imgNoFaces.Visibility = Visibility.Collapsed;
                                            });
                                        }
                                    }
                                    catch (Exception)
                                    {
                                    }
                                }
                                else
                                {
                                    await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                                    {
                                        sinth.StartSpeaking(media, "No se puede realizar el proceso con lentes");
                                        stackpanelNames.Visibility = Visibility.Collapsed;
                                        stackpanelAlert.Width = 616;
                                        stackpanelAlert.Visibility = Visibility.Visible;
                                        imgCaution.Visibility = Visibility.Collapsed;
                                        imgClean.Visibility = Visibility.Collapsed;
                                        imgGlasses.Visibility = Visibility.Visible;
                                        stackpanelAlert.Background = new SolidColorBrush(Colors.LightYellow);
                                        stackpanel.Visibility = Visibility.Collapsed;
                                        imgNoFaces.Visibility = Visibility.Collapsed;
                                    });
                                }
                            }
                            catch (Exception e)
                            {
                            }
                        }
                    }
                    catch (Exception eex)
                    {
                        await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                        {
                            stackpanelNames.Visibility = Visibility.Collapsed;
                            stackpanelAlert.Width = 550;
                            stackpanelAlert.Visibility = Visibility.Visible;
                            stackpanelAlert.Background = new SolidColorBrush(Colors.Green);
                            imgCaution.Visibility = Visibility.Collapsed;
                            imgClean.Visibility = Visibility.Visible;
                            imgGlasses.Visibility = Visibility.Collapsed;
                            Debug.WriteLine("Usuario no identificado");
                            sinth.StartSpeaking(media, "Usuario no identificado");
                            stackpanel.Visibility = Visibility.Collapsed;
                            imgNoFaces.Visibility = Visibility.Collapsed;
                        });
                    }
                }
                catch (Exception ex)
                {
                    await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                    {
                        stackpanelNames.Visibility = Visibility.Collapsed;
                        stackpanelAlert.Width = 550;
                        stackpanelAlert.Visibility = Visibility.Visible;
                        stackpanelAlert.Background = new SolidColorBrush(Colors.Green);
                        imgCaution.Visibility = Visibility.Collapsed;
                        imgClean.Visibility = Visibility.Visible;
                        imgGlasses.Visibility = Visibility.Collapsed;
                        Debug.WriteLine("Usuario no identificado");
                        sinth.StartSpeaking(media, "Usuario no identificado");
                        stackpanel.Visibility = Visibility.Collapsed;
                        imgNoFaces.Visibility = Visibility.Collapsed;
                    });
                }
            }
            else
            {
                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    stackpanel.Visibility = Visibility.Collapsed;
                    imgCaution.Visibility = Visibility.Collapsed;
                    imgGlasses.Visibility = Visibility.Collapsed;
                    imgClean.Visibility = Visibility.Collapsed;
                    imgNoFaces.Visibility = Visibility.Collapsed;
                    stackpanelNames.Visibility = Visibility.Collapsed;
                    stackpanelInternet.Visibility = Visibility.Visible;
                    imgConnectivity.Visibility = Visibility.Visible;
                    Debug.WriteLine("No hay internet");
                });
            }
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                ScanModeTxt.Text = "SCAN MODE: DISABLED";
            });
        }
        #endregion Complete Process
        #region EventHandlers
        private void Tap(object sender, Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            ProcessAll();
        }
        private void Page_RightTapped(object sender, Windows.UI.Xaml.Input.RightTappedRoutedEventArgs e)
        {
            ContinuousRecognize_Click(sender, e);
        }
        private void MyMap_MapDoubleTapped(MapControl sender, MapInputEventArgs args)
        {
            Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                if (!isExpanded)
                {
                    Debug.WriteLine("Hiciste DoubleTap en el mapa");
                    MyMap.Width = 300;
                    MyMap.Height = 300;
                    MyMap.Margin = new Thickness(105, -146, 0, 0);
                    isExpanded = true;
                }
                else
                {
                    MyMap.Width = 100;
                    MyMap.Height = 100;
                    MyMap.Margin = new Thickness(5, -300, 0, 0);
                    isExpanded = false;
                }
               
            });
        }
        #endregion
    }
}