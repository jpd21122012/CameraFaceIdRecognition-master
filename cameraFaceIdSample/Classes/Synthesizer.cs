using System;
using System.Linq;
using Windows.ApplicationModel.Resources.Core;
using Windows.Media.SpeechSynthesis;
using Windows.UI.Xaml.Controls;
using System.Diagnostics;
using System.IO;

namespace cameraFaceIdSample.Classes
{
    public class Synthesizer
    {
        private SpeechSynthesizer synthesizer;
        private ResourceContext speechContext;
        private ResourceMap speechResourceMap;

        public Synthesizer(ListBox listbox)
        {
            synthesizer = new SpeechSynthesizer();
            speechContext = ResourceContext.GetForCurrentView();
            speechContext.Languages = new string[] { SpeechSynthesizer.DefaultVoice.Language };
            speechResourceMap = ResourceManager.Current.MainResourceMap.GetSubtree("LocalizationTTSResources");
            InitializeListboxVoiceChooser(listbox);
        }
        private void InitializeListboxVoiceChooser(ListBox listbox)
        {
            // Get all of the installed voices.
            var voices = SpeechSynthesizer.AllVoices;

            // Get the currently selected voice.
            VoiceInformation currentVoice = synthesizer.Voice;
            foreach (VoiceInformation voice in voices.OrderByDescending(p => p.Language))
            {
                ComboBoxItem item = new ComboBoxItem();
                item.Name = voice.DisplayName;
                item.Tag = voice;
                item.Content = voice.DisplayName + " (Language: " + voice.Language + ")";
                listbox.Items.Add(item);
                Debug.WriteLine(voice.Language);
                // Check to see if we're looking at the current voice and set it as selected in the listbox.
                if (voice.DisplayName == "Microsoft Pablo Mobile")
                {
                    item.IsSelected = true;
                    listbox.SelectedItem = item;
                }
            }
            VoiceChange(listbox);
        }
        void VoiceChange(ListBox listbox)
        {
            try
            {
                ComboBoxItem item = (ComboBoxItem)(listbox.Items[2]);
                VoiceInformation voice = (VoiceInformation)(item.Tag);
                synthesizer.Voice = voice;

                // update UI text to be an appropriate default translation.
                speechContext.Languages = new string[] { voice.Language };
                // textToSynthesize.Text = speechResourceMap.GetValue("SynthesizeTextDefaultText", speechContext).ValueAsString;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Error: " + ex);
            }
        }
        public async void StartSpeaking(MediaElement mediaElement, string ToText)
        {
            string text = ToText;
            if (!String.IsNullOrEmpty(text))
            {
                // Change the button label. You could also just disable the button if you don't want any user control.
                try
                {
                    // Create a stream from the text. This will be played using a media element.
                    SpeechSynthesisStream synthesisStream = await synthesizer.SynthesizeTextToStreamAsync(text);

                    // Set the source and start playing the synthesized audio stream.
                    mediaElement.AutoPlay = true;
                    mediaElement.SetSource(synthesisStream, synthesisStream.ContentType);
                    mediaElement.Play();
                }
                catch (FileNotFoundException)
                {
                    // If media player components are unavailable, (eg, using a N SKU of windows), we won't
                    // be able to start media playback. Handle this gracefully
                    var messageDialog = new Windows.UI.Popups.MessageDialog("Media player components unavailable");
                    await messageDialog.ShowAsync();
                }
                catch (Exception)
                {
                    // If the text is unable to be synthesized, throw an error message to the user.
                    var messageDialog = new Windows.UI.Popups.MessageDialog("Unable to synthesize text");
                    await messageDialog.ShowAsync();
                }
            }
        }
    }
}
