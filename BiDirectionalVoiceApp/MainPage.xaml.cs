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

        //AMQP
        Amqp.Connection amqpConnection;
        string amqpEventhubHostFormat = "amqps://{0}:{1}@{2}.servicebus.windows.net";
        Address ampsAddress;
        Session amqpSession;

        private const string SRGS_FILE = "Grammar\\grammar.xml";
        public class Message
        {
            public string Event { get; set; }
        }

        static bool _isAskinForName = false;
        // Speech Recognizer
        private SpeechRecognizer recognizer;
        // Speech Recognizer
        static ObservableCollection<Message> items = new ObservableCollection<Message>();
      //  private SpeechRecognizer talkrecognizer;

        const string HUB_URL = "https://ewchub.azurewebsites.net/";
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

        #region amqp


        private async void ConnectToServiceBus()
        {
            try
            { 
                ampsAddress = new Address(string.Format(amqpEventhubHostFormat, "RootManageSharedAccessKey", Uri.EscapeDataString("pcKzLqYdC56xC4uOlia3odFilEoUgOeWbwTFJxAFFCY="), "pitalk"));
                
                amqpConnection = await Amqp.Connection.Factory.CreateAsync(ampsAddress);

                var receiverSubscriptionId = "me.amqp.recieiver." + GetHostName();

                // Name of the topic you will be sending messages
                var topic = "chats";

                // Name of the subscription you will receive messages from
                var subscription = "conversations";

                amqpSession = new Session(amqpConnection);
                var consumer = new ReceiverLink(amqpSession, receiverSubscriptionId, $"{topic}/Subscriptions/{subscription}");

                // Start listening
                consumer.Start(5, OnMessageCallback);

            }
            catch (Exception ex)
            {
                SendToView("Error " + ex.Message);
            }

        }
        
        private async void SendMessage(string recipient, string message)
        {
            // Create the topic if it does not exist already.
            SenderLink sender = null;

            try
            {
                // Amqp Code
                // amqpSession = new Session(amqpConnection);
                var senderSubscriptionId = "conversations." + GetHostName();
                var topic = "chats";

                sender = new SenderLink(amqpSession, senderSubscriptionId, topic);

                // Create message
                var chatMessage = new Amqp.Message(message);

                // Add a meesage id
                chatMessage.Properties = new Properties() { MessageId = Guid.NewGuid().ToString() };

                // Add some message properties
                chatMessage.ApplicationProperties = new ApplicationProperties();
                chatMessage.ApplicationProperties["Recipient"] = recipient;
                chatMessage.ApplicationProperties["Sender"] = _name;

                // Send message
                await sender.SendAsync(chatMessage);
                SendToView("Message Sent : " +  recipient + " " + message);

            }
            catch (Exception ex)
            {
                SendToView("Error " + ex.Message);
            }

            await sender.CloseAsync();
        }

        async void OnMessageCallback(ReceiverLink receiver, Amqp.Message message)
        {
            try
            {
                if (message.ApplicationProperties != null)
                {
                    // You can read the custom property
                    var recipient = message.ApplicationProperties["Recipient"];
                    var sender = message.ApplicationProperties["Sender"];

                    SendToView("Message Received from  " + sender + ": "  + recipient + " Message " + message.Body.ToString());


                    if (recipient.Equals(_name))
                    {
                        // Accept the messsage.
                        receiver.Accept(message);

                        await this.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                        {
                            ReadText(sender + "says. " + recipient + " " + message.Body.ToString());
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                receiver.Reject(message);
                SendToView(ex.Message);
            }

        }

        #endregion


        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            lvDataBinding.ItemsSource = items;
            SendToView("Loading...");
            initializeSpeechRecognizer();
            ConnectToServiceBus();

        }
        private async void InitializeTalk()
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
                _isAskinForName = false;

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

                            SendMessage(recipient, msg);
                        }
                    }

                    //Send Message o Servcie Bus
                    // await _proxy.Invoke<Task>("SendMessage",l.ToArray());
                    //Send to person
                }
            }
            catch (Exception ex)
            {
                SendToView(ex.Message);
            }
            
            await recognizer.ContinuousRecognitionSession.StartAsync();
        }

        // Release resources, stop recognizer, release pins, etc...
        private async void MainPage_Unloaded(object sender, object args)
        {
            // Stop recognizing
            await recognizer.ContinuousRecognitionSession.StopAsync();
           // await talkrecognizer.StopRecognitionAsync();
        }

        // Initialize Speech Recognizer and start async recognition
        private async void initializeSpeechRecognizer()
        {

            try
            {
                // Initialize recognizer
                recognizer = new SpeechRecognizer();

                // Set event handlers
                recognizer.StateChanged += RecognizerStateChanged;
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
                    SendToView("You can now say...'Connect [Name] to the hub'");
                }
                else
                {
                    SendToView("Status: continuous speech recognizer failed to initialize");
                }
            }catch(Exception ex)
            {
                SendToView(ex.Message);
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
           // throw new NotImplementedException();
        }

        // Recognizer generated results
        private async void RecognizerResultGenerated(SpeechContinuousRecognitionSession session, SpeechContinuousRecognitionResultGeneratedEventArgs args)
        {
            _isAskinForName = false;

            // Check for different tags and initialize the variables
            String target = args.Result.SemanticInterpretation.Properties.ContainsKey("cmd") ?
                            args.Result.SemanticInterpretation.Properties["cmd"][0].ToString() :
                            "";
            String name = args.Result.SemanticInterpretation.Properties.ContainsKey("name") ?
                args.Result.SemanticInterpretation.Properties["name"][0].ToString() :
                "";

            var c = args.Result.Confidence;
 
            if (c == SpeechRecognitionConfidence.High)
            {
                SendToView("Command: " + target);

                if (target == "connect")
                {
                   await recognizer.ContinuousRecognitionSession.StopAsync();

                    _name = name;

                    //Signalr Code
                    //await ConnectToHub(HUB_URL, HUB_NAME);
                    //await _proxy.Invoke<Task>("Join", _name.ToLower());

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

                //    //_hubConnection.Stop();
                //    //_proxy = null;
                //    //_hubConnection.Dispose();

                //    await recognizer.ContinuousRecognitionSession.StartAsync();

                //    return;

                //}
                if (target == "Send Message")
                {
                    await recognizer.ContinuousRecognitionSession.StopAsync();
                    InitializeTalk();
                    return;
                }

            }
        }

        private async void _hubConnection_StateChanged(StateChange obj)
        {
            try
            {
                if (obj.NewState == ConnectionState.Disconnected)
                {
                }

                if (obj.NewState == ConnectionState.Reconnecting)
                {
                }

                if (obj.NewState == ConnectionState.Connecting)
                {
                }

                if (obj.NewState == ConnectionState.Connected)
                {

                }
            }
            catch (Exception ex)
            {
                //You can log any errors here but its really on here for 503 errors to pass through
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

                _proxy.On<string>("OnMessageSent", (x) =>
                {
                     ReadText(x);
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
            MediaElement media = new MediaElement();
            SpeechSynthesisStream stream = null;

            var voices = SpeechSynthesizer.AllVoices;
            using (var speech = new SpeechSynthesizer()) {
                speech.Voice = voices.First(gender => gender.Gender == VoiceGender.Female);
                stream = await speech.SynthesizeTextToStreamAsync(text);
            }

                media.SetSource(stream, stream.ContentType);
                media.Play();
                media.Stop();

        }
    }
}

