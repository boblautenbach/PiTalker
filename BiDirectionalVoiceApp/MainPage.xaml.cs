using Microsoft.AspNet.SignalR.Client;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.Media.SpeechRecognition;
using Windows.Media.SpeechSynthesis;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using Amqp;
using Amqp.Framing;
using System.Xml;
using System.IO;
using Windows.Data.Xml.Dom;
using Windows.Networking.Connectivity;
using Windows.Networking;

// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace BiDirectionalVoiceApp
{
    // Grammer File

    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        private const string SRGS_FILE = "Grammar\\grammar.xml";
        public class Message
        {
            public string Event { get; set; }
        }

        public class HubMessage
        {
            public string Msg { get; set; }
            public string Group { get; set; }
            public string Sender { get; set; }

        }

        // Speech Recognizer
        private SpeechRecognizer recognizer;
        // Speech Recognizer
        static ObservableCollection<Message> items = new ObservableCollection<Message>();
      //  private SpeechRecognizer talkrecognizer;

        const string HUB_URL = "https://pitalkersignal.azurewebsites.net";
        const string HUB_NAME = "MessageHub";
        string _name = string.Empty;

        static HubConnection _hubConnection;
        static IHubProxy _proxy;
        // GPIO 
        public MainPage()
        {
            this.InitializeComponent();
        }

        private string GetHostName()
        {
            var hostNames = NetworkInformation.GetHostNames();
           return hostNames.FirstOrDefault(name => name.Type == HostNameType.DomainName)?.DisplayName;
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            lvDataBinding.ItemsSource = items;
            SendToView("Loading...");
            initializeSpeechRecognizer();

        }
        private async Task InitializeTalk()
        {

            string resultText = string.Empty;

            try
            {
                // Initialize recognizer
                using (var _talkrecognizer = new SpeechRecognizer())
                {
                   var compilationResult = await _talkrecognizer.CompileConstraintsAsync();

                    // If successful, display the recognition result.
                    if (compilationResult.Status == SpeechRecognitionResultStatus.Success)
                    {
                        SpeechRecognitionResult result = await _talkrecognizer.RecognizeAsync();

                        if (result.Status == SpeechRecognitionResultStatus.Success)
                        {
                            resultText = result.Text;
                        }

                    }
                }

                if (resultText != null && !string.IsNullOrEmpty(resultText))
                {
                    var i = resultText.IndexOf(' ');

                    if (i > 1)
                    {
                        var recipient = resultText.Substring(0, i).Trim().ToLower();

                        if (resultText.Length > i)
                        {
                            var msg = resultText.Substring(i).Trim();

                            List<string> l = new List<string>();
                            l.Add(recipient.ToLower());
                            l.Add(recipient + " " + msg);

                            //  SendMessage(recipient, msg);

                           await _proxy.Invoke<HubMessage>("SendMessage", new HubMessage() { Msg = msg, Sender = _name, Group = recipient });
                            SendToView("Message Sent : " + recipient + " " + msg);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                SendToView("InitializeTalk "  + ex.Message);
            }

            try
            {
                await recognizer.ContinuousRecognitionSession.StartAsync();
            }
            catch (Exception ex)
            {
                SendToView(ex.Message);
            }

        }

        // Release resources, stop recognizer, release pins, etc...
        private async void MainPage_Unloaded(object sender, object args)
        {
            // Stop recognizing
            await recognizer.ContinuousRecognitionSession.StopAsync();
           // await talkrecognizer.StopRecognitionAsync();
        }

        // Initialize Speech Recognizer and start async recognition
        private async Task initializeSpeechRecognizer()
        {

            try
            {
                // Initialize recognizer
                recognizer = new SpeechRecognizer();

                // Set event handlers
            //    recognizer.StateChanged += RecognizerStateChanged;
                recognizer.ContinuousRecognitionSession.ResultGenerated += RecognizerResultGenerated;
                // Load Grammer file constraint
                string fileName = String.Format(SRGS_FILE);
                StorageFile grammarContentFile = await Package.Current.InstalledLocation.GetFileAsync(fileName);

                SpeechRecognitionGrammarFileConstraint grammarConstraint = new SpeechRecognitionGrammarFileConstraint(grammarContentFile);

                // Add to grammer constraint
                recognizer.Constraints.Add(grammarConstraint);

                // Compile grammer
                SpeechRecognitionCompilationResult compilationResult = await recognizer.CompileConstraintsAsync();

                // Compile grammer

             //   SendToView("Status: " + compilationResult.Status.ToString());

                // If successful, display the recognition result.
                if (compilationResult.Status == SpeechRecognitionResultStatus.Success)
                {
                    await recognizer.ContinuousRecognitionSession.StartAsync();
                    SendToView("Status: continuous speech recognizer Initialized");
                    SendToView("You can now say...'Alfred, Connect [Name] to the hub'");
                }
                else
                {
                    SendToView("Status: continuous speech recognizer failed to initialize");
                }
            }catch(Exception ex)
            {
                SendToView("initializeSpeechRecognizer " + ex.Message);
            }
        }

        private async Task SendToView(string message)
        {
            await this.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            { 
              items.Insert(0,new Message() { Event = message });
            });
        }

        private void RecognizerStateChanged(SpeechRecognizer sender, SpeechRecognizerStateChangedEventArgs args)
        {
            //SendToView("Continuous Listener state ...: " + sender.State.ToString());
        }

        private async Task ProcessContinousVoiceResult(string target, string name)
        {
            try
            { 
                if (target == "connect")
                {
                    await recognizer.ContinuousRecognitionSession.StopAsync();

                    _name = name;

                    //Signalr Code
                    await ConnectToHub(HUB_URL, HUB_NAME);
                    await _proxy.Invoke("Join", _name.ToLower());

                    SendToView("Connecting as " + _name);

                    await this.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                    {
                        ReadText("OK " + _name + " You are now available.");
                    });

                    SendToView("You are now available for messaging");
                    SendToView("You can say 'send message [pause 1 second] [name of person to message] + message");

                    await recognizer.ContinuousRecognitionSession.StartAsync();
                    return;
                }

                //if (target == "disconnect")
                //{

                //    await this.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                //    {
                //        ReadText("OK You are no longer available on the hub.");
                //    });
                //    await recognizer.ContinuousRecognitionSession.StopAsync();

                //    _hubConnection.Stop();
                //    _proxy = null;
                //    _hubConnection.Dispose();

                //    await recognizer.ContinuousRecognitionSession.StartAsync();

                //    return;
                //}

                if (target == "Send Message")
                {
                    await recognizer.ContinuousRecognitionSession.StopAsync();
                    await InitializeTalk();
                    return;
                }


                if (target == "get time")
                {

                    await this.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                    {
                        ReadText(_name + " the time is <say-as interpret-as='time' format='hms12'> " + DateTime.Now.ToString("hh:mm tt") + " </say-as>");
                    });
                    return;
                }


                if (target == "thanks")
                {

                    await this.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                    {
                        ReadText("my pleasure " + _name);
                    });
                    return;
                }
            }
            catch (Exception ex)
            {
                //You can log any errors here but its really on here for 503 errors to pass through
                SendToView("ProcessContinousVoiceResult " + ex.Message);
            }
        }

        // Recognizer generated results
        private void RecognizerResultGenerated(SpeechContinuousRecognitionSession session, SpeechContinuousRecognitionResultGeneratedEventArgs args)
        {
            try
            {
                // Check for different tags and initialize the variables
                String target = args.Result.SemanticInterpretation.Properties.ContainsKey("cmd") ?
                            args.Result.SemanticInterpretation.Properties["cmd"][0].ToString() :
                            "";
                String name = args.Result.SemanticInterpretation.Properties.ContainsKey("name") ?
                            args.Result.SemanticInterpretation.Properties["name"][0].ToString() :
                            "";

                if (args.Result.Confidence == SpeechRecognitionConfidence.High)
                {
                    SendToView("Command: " + target);
                    ProcessContinousVoiceResult(target,name);
                }
            }
            catch (Exception ex)
            {
                SendToView("RecognizerResultGenerated " + ex.Message);
            }
        }

        private void _hubConnection_StateChanged(StateChange obj)
        {
            try
            {
                if (obj.NewState == ConnectionState.Disconnected)
                {
                    SendToView("hub disconnected");
                }

                if (obj.NewState == ConnectionState.Reconnecting)
                {
                    SendToView("hub reconnecting");
                }

                if (obj.NewState == ConnectionState.Connecting)
                {
                    SendToView("hub connecting");
                }

                if (obj.NewState == ConnectionState.Connected)
                {
                    SendToView("hub connected");
                }
            }
            catch (Exception ex)
            {
                //You can log any errors here but its really on here for 503 errors to pass through
                SendToView("hub state error " + ex.Message);
            }
        }

        private async Task WireHubEvent()
        {
            await this.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                _hubConnection.StateChanged += _hubConnection_StateChanged;
            });
        }

        private async Task<bool> ConnectToHub(string hubUrl, string hubName)
        {
            try
            {
                _hubConnection = new HubConnection(hubUrl);

                _proxy = _hubConnection.CreateHubProxy(hubName);

                await WireHubEvent();

                _proxy.On<HubMessage>("OnMessageSent", async (x) =>
                {

                    try { 
                        SendToView("Message Received from  " + x.Sender + " : " + x.Group + " "  + x.Msg);

                        await this.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                        {
                            ReadText(x.Sender + "says. " + x.Group + " " +x.Msg);
                        });
                    }
                    catch (Exception ex)
                    {
                        //You can log any errors here but its really on here for 503 errors to pass through
                        SendToView("OnMessageSent Error " + ex.Message);
                    }
                });

                await _hubConnection.Start();

            }
            catch (Exception ex)
            {
                //You can log any errors here but its really on here for 503 errors to pass through
                SendToView("Failed to connect to hub");
                return false;
            }
            return true;
        }

        private async Task ReadText(string text)
        {
            try
            {

                MediaElement media = new MediaElement();
                SpeechSynthesisStream stream = null;

                string Ssml =
                    @"<speak version='1.0' " +
                    "xmlns='http://www.w3.org/2001/10/synthesis' xml:lang='en-US'>" +
                    text +
                    "</speak>";

                //using (SpeechSynthesizer synthesizer = new SpeechSynthesizer())
                //{
                //    // show installed voices
                //    foreach (var v in SpeechSynthesizer.AllVoices)
                //    {
                //        SendToView(string.Format("Name:{0}, Gender:{1}, DisplayName:{2}",
                //          v.Description, v.Gender, v.DisplayName));
                //    }

                //}

                var voices = SpeechSynthesizer.AllVoices;
                using (var speech = new SpeechSynthesizer())
                {
                    //  speech.Voice = voices.First(gender => gender.Gender == VoiceGender.Female);
                    speech.Voice = voices.First(gender => gender.Gender == VoiceGender.Male && gender.Description.Contains("David"));
                    stream = await speech.SynthesizeSsmlToStreamAsync(Ssml);
                    //stream = await speech.SynthesizeTextToStreamAsync(text);
                }

                media.SetSource(stream, stream.ContentType);
                media.Play();

                media.Stop();
            }
            catch (Exception ex)
            {
                //You can log any errors here but its really on here for 503 errors to pass through
                SendToView("Play Text " + ex.Message);
            }
        }
    }
}

