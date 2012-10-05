using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Timers;
using System.Xml.Linq;
using SIPLib.SIP;
using SIPLib.Utils;
using log4net;

namespace ContextServer
{
    class Program
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(SIPApp));
        private static readonly ILog SessionLog = LogManager.GetLogger("SessionLogger");
        private static SIPApp _app;
        private static Address _localparty;
        private static Dictionary<string, string> _statusdict = new Dictionary<string, string>();
        public static SIPStack CreateStack(SIPApp app, string proxyIp = null, int proxyPort = -1)
        {
            SIPStack myStack = new SIPStack(app);
            if (proxyIp != null)
            {
                myStack.ProxyHost = proxyIp;
                myStack.ProxyPort = (proxyPort == -1) ? 5060 : proxyPort;
            }
            return myStack;
        }

        public static TransportInfo CreateTransport(string listenIp, int listenPort)
        {
            return new TransportInfo(IPAddress.Parse(listenIp), listenPort, System.Net.Sockets.ProtocolType.Udp);
        }

        static void AppResponseRecvEvent(object sender, SipMessageEventArgs e)
        {
            //Log.Info("Response Received:" + e.Message);
            Message response = e.Message;
            string requestType = response.First("CSeq").ToString().Trim().Split()[1].ToUpper();
            switch (requestType)
            {
                case "SUBSCRIBE":
                    {
                        if (response.ResponseCode == 200)
                        {
                            Log.Info("Successfully subscribed to " + response.First("To") + "'s status");
                        }
                        break;
                    }
                case "MESSAGE":
                    {
                        if (response.ResponseCode == 200)
                        {
                            Log.Info("Successfully sent SCIM status information");
                        }
                        else
                        {
                            Log.Info("Problem sending SCIM status information");

                        }
                        break;
                    }
                case "INVITE":
                case "REGISTER":
                default:
                    Log.Info("Response for Request Type " + requestType + " is unhandled ");
                    break;
            }
        }

        static void AppRequestRecvEvent(object sender, SipMessageEventArgs e)
        {
            //Log.Info("Request Received:" + e.Message);
            Message request = e.Message;
            switch (request.Method.ToUpper())
            {
                case "INVITE":
                case "REGISTER":
                case "BYE":
                case "ACK":
                case "MESSAGE":
                case "OPTIONS":
                case "REFER":
                case "SUBSCRIBE":
                case "NOTIFY":
                    {
                        Message m = e.UA.CreateResponse(200, "OK");
                        e.UA.SendResponse(m);
                        ProcessRequest(request);
                        break;
                    }
                case "PUBLISH":
                case "INFO":
                default:
                    {
                        Log.Info("Request with method " + request.Method.ToUpper() + " is unhandled");
                        break;
                    }
            }
        }

        private static void ProcessRequest(Message request)
        {
            if (request.Method.ToUpper().Contains("NOTIFY"))
            {
                if (request.Headers.ContainsKey("Content-Length"))
                {
                    if (request.Body.Length > 0)
                    {
                        try
                        {
                            XDocument xDoc = XDocument.Parse(request.Body.Trim());
                            string basic = "";
                            string note = "";
                            foreach (XElement xElement in xDoc.Descendants())
                            {
                                switch (xElement.Name.ToString())
                                {
                                    case "{urn:ietf:params:xml:ns:pidf}basic":
                                        basic = xElement.Value;
                                        break;

                                    case "{urn:ietf:params:xml:ns:pidf}note":
                                        note = xElement.Value;
                                        break;
                                }
                            }
                            string contact = ((Address)(request.First("From").Value)).Uri.ToString();
                            contact = contact.Replace("<", "");
                            contact = contact.Replace(">", "");
                            contact = contact.Replace("sip:", "");
                            Log.Info("Received status update: " + contact + " " + basic + " " + note);
                            _statusdict.Add(contact,basic);
                        }
                        catch (Exception exception)
                        {
                            Log.Warn("Error in handling presence xml: ", exception);
                        }
                    }
                }
            }
        }

        static void Subscribe(string sipUri)
        {
            UserAgent pua = new UserAgent(_app.Stack) { RemoteParty = new Address(sipUri), LocalParty = _localparty };
            Message request = pua.CreateRequest("SUBSCRIBE");
            request.InsertHeader(new Header("presence", "Event"));
            pua.SendRequest(request);
        }

        private static void StartTimer()
        {
            System.Timers.Timer aTimer = new System.Timers.Timer();
            aTimer.Elapsed += new ElapsedEventHandler(SendPresenceToSCIM);
            aTimer.Interval = 30000;
            aTimer.Enabled = true;
        }

        static void Main(string[] args)
        {
            TransportInfo localTransport = CreateTransport(Helpers.GetLocalIP(), 7777);
            _app = new SIPApp(localTransport);
            _app.RequestRecvEvent += new EventHandler<SipMessageEventArgs>(AppRequestRecvEvent);
            _app.ResponseRecvEvent += new EventHandler<SipMessageEventArgs>(AppResponseRecvEvent);
            const string scscfIP = "scscf.open-ims.test";
            const int scscfPort = 6060;
            SIPStack stack = CreateStack(_app, scscfIP, scscfPort);
            stack.Uri = new SIPURI("context_server@open-ims.test");
            _localparty = new Address("<sip:context_server@open-ims.test>");
            StartTimer();
            Subscribe("<sip:alice@open-ims.test>");
            Console.WriteLine("Press \'q\' to quit");
            while (Console.Read() != 'q') ;
        }

        private static void SendPresenceToSCIM(object source, ElapsedEventArgs e)
        {
            if(_statusdict.Count > 0)
            {
            StringBuilder sb = new StringBuilder();
            foreach (KeyValuePair<string, string> keyValuePair in _statusdict)
            {
                sb.Append(keyValuePair.Key + ":" + keyValuePair.Value);
            }
            _statusdict.Clear();
            _app.SendMessage("<sip:scim@open-ims.test>", sb.ToString());
            }
        }
    }
}
