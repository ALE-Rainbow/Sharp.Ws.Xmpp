using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

using Microsoft.Extensions.Logging;


namespace Sharp.Xmpp.Core
{
    internal class WebSocket
    {
        private readonly ILogger log;
        private readonly ILogger logWebRTC;

        public event EventHandler WebSocketOpened;
        public event EventHandler WebSocketClosed;

        private bool webSocketOpened = false;

        private bool rootElement;
        private string uri;

        private readonly object writeLock = new object();

        private BlockingCollection<string> actionsToPerform;
        private BlockingCollection<string> messagesToSend;
        private BlockingCollection<string> messagesReceived;
        private BlockingCollection<Iq> iqMessagesReceived;
        private HashSet<String> iqIdList;

        private Tuple<String, String, String> webProxyInfo = null;

        private ClientWebSocket clientWebSocket = null;

        public CultureInfo Language
        {
            get;
            private set;
        }

        public WebSocket(String uri, Tuple<String, String, String> webProxyInfo, string loggerPrefix = null)
        {
            log = LogFactory.CreateLogger<WebSocket>(loggerPrefix);
            logWebRTC = LogFactory.CreateWebRTCLogger(loggerPrefix);

            log.LogDebug("Create Web socket");
            this.uri = uri;
            rootElement = false;

            this.webProxyInfo = webProxyInfo;

            actionsToPerform = new BlockingCollection<string>(new ConcurrentQueue<string>());
            messagesToSend = new BlockingCollection<string>(new ConcurrentQueue<string>());
            messagesReceived = new BlockingCollection<string>(new ConcurrentQueue<string>());
            iqMessagesReceived = new BlockingCollection<Iq>(new ConcurrentQueue<Iq>());
            iqIdList = new HashSet<string>();
        }

        public void Open()
        {
            Task.Factory.StartNew(CreateAndManageWebSocket, TaskCreationOptions.LongRunning | TaskCreationOptions.DenyChildAttach);
        }

        public async void Close(bool normalClosure = true)
        {
            webSocketOpened = false;

            if (clientWebSocket != null)
            {
                if (clientWebSocket.State != System.Net.WebSockets.WebSocketState.Closed)
                {
                    try
                    {
                        if(normalClosure)
                            await clientWebSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                        else
                            await clientWebSocket.CloseAsync(WebSocketCloseStatus.EndpointUnavailable, "", CancellationToken.None);
                    }
                    catch
                    {
                        // Nothing to do more
                    }
                }
            }

            if (clientWebSocket != null)
            { 
                try
                {
                    clientWebSocket.Dispose();
                    clientWebSocket = null;
                }
                catch
                {
                    // Nothing to do more
                }
            }
        }

        private async void CreateAndManageWebSocket()
        {
            // First CLose / Dispose previous object
            Close();

            // Create Client
            clientWebSocket = new ClientWebSocket();

            clientWebSocket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);

#if NETCOREAPP
            clientWebSocket.Options.RemoteCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) => {
                return true; // If the server certificate is valid.
            };
#endif

            // Manage proxy configuration
            if (webProxyInfo == null)
                clientWebSocket.Options.Proxy = null;
            else
            {
                    
                log.LogDebug("[CreateAndManageWebSocket] Web Proxy Info:[{0}]", webProxyInfo?.Item1);
                WebProxy proxy = new WebProxy(webProxyInfo.Item1);
                if(!String.IsNullOrEmpty(webProxyInfo.Item2))
                    proxy.Credentials = new NetworkCredential(webProxyInfo.Item2, webProxyInfo.Item3);
                clientWebSocket.Options.Proxy = proxy;
            }

            await clientWebSocket.ConnectAsync(new Uri(uri), CancellationToken.None);


            // Need to raise WebSocketOpened or WebSocketClosed
            if (clientWebSocket.State == System.Net.WebSockets.WebSocketState.Open)
            {
                webSocketOpened = true;
                RaiseWebSocketOpened();
            }
            else
            {
                RaiseWebSocketClosed();
                return;
            }

            // Manage next incoming message
            ManageIncomingMessage();

            // Manage outgoing message
            ManageOutgoingMessage();
        }

        private async void ManageOutgoingMessage()
        {
            string message;

            // Loop used to send message when they are avaialble
            while (true)
            {
                if (clientWebSocket != null)
                {
                    if (clientWebSocket.State != System.Net.WebSockets.WebSocketState.Open)
                    {
                        ClientWebSocketClosed();
                        return;
                    }
                    else
                    {
                        message = DequeueMessageToSend();
                        if (message != null)
                        {
                            // Log webRTC stuff
                            if ((logWebRTC != null)
                                && (
                                    message.Contains("<jingle")
                                    || message.Contains("urn:xmpp:jingle"))
                                    )
                                logWebRTC.LogDebug("[ManageOutgoingMessage]: {0}", message);
                            else
                                log.LogDebug("[ManageOutgoingMessage]: {0}", message);


                            var sendBuffer = new ArraySegment<byte>(Encoding.UTF8.GetBytes(message));
                            try
                            {
                                await clientWebSocket.SendAsync(sendBuffer, WebSocketMessageType.Text, true, CancellationToken.None);
                            }
                            catch
                            {
                                // Nothing to do - if a pb occur here, it means that the socket has been closed
                                // The loop will take this into account
                            }
                        }
                    }
                }
                else
                    return;
            }
        }

        private void ManageIncomingMessage()
        {
            Task.Factory.StartNew( async() =>
                {
                    ArraySegment<Byte> buffer = new ArraySegment<byte>(new Byte[8192]);

                    WebSocketReceiveResult result = null;
                    Boolean readingCorrectly = true;

                    using (var ms = new MemoryStream())
                    {
                        do
                        {
                            try
                            {
                                result = await clientWebSocket.ReceiveAsync(buffer, CancellationToken.None);
                                ms.Write(buffer.Array, buffer.Offset, result.Count);
                            }
                            catch
                            {
                                readingCorrectly = false;
                            }
                        }
                        while (readingCorrectly && (!result.EndOfMessage));

                        // Do we read on the web socket correctly ?
                        if (!readingCorrectly)
                        {
                            ClientWebSocketClosed();
                            return;
                        }

                        // Queue the message but only if we received text
                        if (result.MessageType == WebSocketMessageType.Text)
                        {
                            ms.Seek(0, SeekOrigin.Begin);
                            using (var reader = new StreamReader(ms, Encoding.UTF8))
                            {
                                String message = reader.ReadToEnd();

                                // Log webRTC stuff
                                if ((logWebRTC != null)
                                        && (
                                            message.Contains("<jingle")
                                            || message.Contains("urn:xmpp:jingle"))
                                            )
                                    logWebRTC.LogDebug("[ManageIncomingMessage]: {0}", message);
                                else
                                    log.LogDebug("[ManageIncomingMessage]: {0}", message);


                                QueueMessageReceived(message);
                            }
                        }
                        else if (result.MessageType == WebSocketMessageType.Binary)
                        {
                            log.LogWarning("[ManageIncomingMessage] We have received data using unmanaged type - MessageType:[{0}]", result.MessageType.ToString());
                        }
                        //else
                        //{
                        //    // Nothing special to do here
                        //}

                    }

                    // Manage next incoming message
                    ManageIncomingMessage();
                }
            );
        }

#region Iq stuff
        public void AddExpectedIqId(string id)
        {
            //log.LogDebug("AddExpectedIqId:{0}", id);
            if (!iqIdList.Contains(id))
                iqIdList.Add(id);
        }

        public bool IsExpectedIqId(string id)
        {
            //log.LogDebug("IsExpectedIqId:{0}", id);
            return iqIdList.Contains(id);
        }

        public void QueueExpectedIqMessage(Iq iq)
        {
            //log.LogDebug("QueueExpectedIqMessage :{0}", iq.ToString());
            iqMessagesReceived.Add(iq);
        }

        public Iq DequeueExpectedIqMessage()
        {
            Iq iq = null;
            //log.LogDebug("DequeueExpectedIqMessage - START");
            iq = iqMessagesReceived.Take();
            //log.LogDebug("DequeueExpectedIqMessage - END");
            return iq;

        }
#endregion

#region Action to perform
        public void QueueActionToPerform(String action)
        {
            //log.LogDebug("QueueActionToPerform");
            actionsToPerform.Add(action);
        }

        public string DequeueActionToPerform()
        {
            //log.LogDebug("DequeueActionToPerform - START");
            string action = actionsToPerform.Take();
            //log.LogDebug("DequeueActionToPerform - END");
            return action;
        }
#endregion

#region Messages to send
        private void QueueMessageToSend(String message)
        {
            messagesToSend.Add(message);
        }

        private string DequeueMessageToSend()
        {
            String message;
            if (messagesToSend.TryTake(out message, 50))
                return message;
            return null;
        }
#endregion

#region Messages received
        public void QueueMessageReceived(String message)
        {
            lock (writeLock)
            {
                XmlDocument xmlDocument = new XmlDocument();
                try
                {
                    // Check if we have a valid XML message - if not an exception is raised
                    xmlDocument.LoadXml(message);

                    if (rootElement)
                    {
                        // Add message in the queue
                        messagesReceived.Add(message);
                    }
                    else
                    {
                        ReadRootElement(xmlDocument);
                    }
                }
                catch
                {
                    log.LogError("QueueMessageReceived - ERROR");
                }
            }
        }

        public string DequeueMessageReceived()
        {
            //log.LogDebug("Dequeue XML Message Received - START");
            string message = messagesReceived.Take();
            //log.LogDebug("Dequeue XML Message Received - END");
            return message;
        }
#endregion

        public void Send(string xml)
        {
            QueueMessageToSend(xml);
        }

        private void ClientWebSocketClosed()
        {
            if (webSocketOpened)
            {
                webSocketOpened = false;
                if (clientWebSocket != null)
                    log.LogDebug($"[ClientWebSocketClosed] CloseStatus:[{clientWebSocket.CloseStatus}] -  CloseStatusDescription:[{clientWebSocket.CloseStatusDescription}]");
                else
                    log.LogDebug("[ClientWebSocketClosed]");

                RaiseWebSocketClosed();
            }
        }

        private void RaiseWebSocketOpened()
        {
            log.LogDebug("Web socket opened");

            // Raise event WebSocketOpened
            EventHandler h = this.WebSocketOpened;
            if (h != null)
            {
                try
                {
                    h(this, null);
                }
                catch (Exception ex)
                {
                    log.LogError($"[RaiseWebSocketOpened]  - Exception raising WebSocketOpened:[{ex}]");
                }
            }
        }

        private void RaiseWebSocketClosed()
        {
            log.LogDebug("Web socket closed");

            // Raise event WebSocketClosed
            EventHandler h = this.WebSocketClosed;
            if (h != null)
            {
                try
                {
                    h(this, null);
                }
                catch (Exception ex)
                {
                    log.LogError($"[RaiseWebSocketClosed]  - Exception raising WebSocketClosed:[{ex}]");
                }
            }
        }

        private void ReadRootElement(XmlDocument xmlDocument)
        {
            XmlElement Open;
            Open = xmlDocument.DocumentElement;

            if (Open == null)
            {
                log.LogError("ReadRootElement - Unexpected XML message received");
            }

            if (Open.Name == "open")
            {
                rootElement = true;
                string lang = Open.GetAttribute("xml:lang");
                if (!String.IsNullOrEmpty(lang))
                    Language = new CultureInfo(lang);
            }
            else
            {
                log.LogError("ReadRootElement - ERROR");
            }
        }
    }
}
