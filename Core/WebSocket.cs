using Microsoft.Extensions.Logging;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;


namespace Sharp.Xmpp.Core
{
    internal class WebSocket
    {
        const Int32 TIMEOUT_WS_DEFAULT_VALUE = 30000;

        private readonly ILogger log;
        private readonly ILogger logWebRTC;

        public event EventHandler WebSocketOpened;
        public event EventHandler WebSocketClosed;

        private bool webSocketOpened = false;

        private readonly string uri;

        private readonly object closedLock = new();

        private readonly SemaphoreSlim semaphoreSendSlim = new (1, 1);

        private readonly BlockingCollection<string> messagesReceived;

        private readonly WebProxy webProxy = null;

        private ClientWebSocket clientWebSocket = null;

        public String Language
        {
            get;
            private set;
        }

        public WebSocket(String uri, WebProxy webProxy, string loggerPrefix = null)
        {
            log = LogFactory.CreateLogger<WebSocket>(loggerPrefix);
            logWebRTC = LogFactory.CreateWebRTCLogger(loggerPrefix);

            log.LogDebug("Create Web socket");
            this.uri = uri;
            rootElement = false;

            this.webProxy = webProxy;

            messagesReceived = new BlockingCollection<string>(new ConcurrentQueue<string>());
        }

        public void Open()
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await CreateAndManageWebSocketAsync();
                }
                catch (Exception ex)
                {
                    log.LogError("[Open] Fatal error during background connection: {Exception}", ex);
                }
            });
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

        private async Task CreateAndManageWebSocketAsync()
        {
            // First CLose / Dispose previous object
            Close();

            // Create Client
            clientWebSocket = new ClientWebSocket();
            clientWebSocket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
            clientWebSocket.Options.Proxy = webProxy;

#if NETCOREAPP
            clientWebSocket.Options.RemoteCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) => true;
#endif

            using var timeoutCts = new CancellationTokenSource(TIMEOUT_WS_DEFAULT_VALUE);
            try
            {
                log.LogDebug("[CreateAndManageWebSocket] Attempting to connect to {Uri}...", uri);
                await clientWebSocket.ConnectAsync(new Uri(uri), timeoutCts.Token);
                if (clientWebSocket.State == System.Net.WebSockets.WebSocketState.Open)
                {
                    webSocketOpened = true;
                    RaiseWebSocketOpened();

                    _ = ManageIncomingMessageAsync();
                }
                else
                {
                    log.LogWarning("[CreateAndManageWebSocket] Socket not opened. State: {State}", clientWebSocket.State);
                    RaiseWebSocketClosed();
                }
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
            {
                log.LogWarning("[CreateAndManageWebSocket] Connection failed due to timeout ({Timeout}ms)", TIMEOUT_WS_DEFAULT_VALUE);
                RaiseWebSocketClosed();
            }
            catch (Exception exc)
            {
                log.LogError("[CreateAndManageWebSocket] Exception during connection: {Message}", exc.Message);
                RaiseWebSocketClosed();
            }
        }
        
        private async Task ManageIncomingMessageAsync()
        {
            int SIZE = 8192;
            var buffer = ArrayPool<byte>.Shared.Rent(SIZE);
            var segment = new ArraySegment<byte>(buffer);
            using var ms = new MemoryStream(SIZE);
            try
            {
                // Loop until the web socket is no more opened
                while (clientWebSocket != null && clientWebSocket.State == WebSocketState.Open)
                {
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await clientWebSocket.ReceiveAsync(segment, CancellationToken.None);

                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            log.LogDebug("[ManageIncomingMessage] Close received");
                            RaiseWebSocketClosed();
                            return;
                        }
                        else if (result.MessageType == WebSocketMessageType.Text)
                            ms.Write(buffer, 0, result.Count);
                    }
                    while (!result.EndOfMessage);

                    // Read message - only Text format is managed
                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        string message = Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
                        QueueMessageReceived(message);
                    }
                    ms.SetLength(0);
                }
            }
            catch (Exception exc)
            {
                log.LogWarning("[ManageIncomingMessage] Exception: {Exception}", exc);
                RaiseWebSocketClosed();
            }
            finally
            {
                // /!\ Need to return buffer to the shared pool
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

    #region Messages received
        public void QueueMessageReceived(String message)
        {
            messagesReceived.Add(message);
        }

        public string DequeueMessageReceived()
        {
            string message = messagesReceived.Take();

            // Log webRTC stuff
            if ((logWebRTC != null)
                    && (
                        message.Contains("<jingle")
                        || message.Contains("urn:xmpp:jingle"))
                        )
                logWebRTC.LogDebug("[ManageIncomingMessage]: {Message}", message);
            else
                log.LogDebug("[ManageIncomingMessage]: {Message}", message);

            return message;
        }
    #endregion

        public async Task<Boolean> SendAsync(string message)
        {
            if (String.IsNullOrEmpty(message))
                return false;

            if (clientWebSocket == null)
                return false;

            // To ensure send message one by one
            await semaphoreSendSlim.WaitAsync();

            if (clientWebSocket.State != System.Net.WebSockets.WebSocketState.Open)
            {
                try
                {
                    if (semaphoreSendSlim.CurrentCount == 0)
                        semaphoreSendSlim.Release();
                }
                catch { }

                log.LogWarning("[SendAsync] clientWebSocket.State: [{State}]", clientWebSocket.State);
                ClientWebSocketClosed();
                return false;
            }

            Boolean noError;
            var sendBuffer = new ArraySegment<byte>(Encoding.UTF8.GetBytes(message));
            try
            {
                await clientWebSocket.SendAsync(sendBuffer, WebSocketMessageType.Text, true, CancellationToken.None);
                noError = true;
            }
            catch
            {
                log.LogDebug("[ManageOutgoingMessage]: Message not sent");
                noError = false;
            }

            ILogger logger;
            // Log webRTC stuff
            if ((logWebRTC != null)
                && (
                    message.Contains("<jingle")
                    || message.Contains("urn:xmpp:jingle"))
                    )
                logger = logWebRTC;
            else
                logger = log;

            if (noError)
                logger.LogDebug("[ManageOutgoingMessage]: {Message}", message);
            else
                logger.LogWarning("[ManageOutgoingMessage] NOT SENT: {Message}", message);

            semaphoreSendSlim.Release();
            return noError;
        }

        private void ClientWebSocketClosed()
        {
            lock (closedLock)
            {
                try
                {
                    if (semaphoreSendSlim.CurrentCount == 0)
                        semaphoreSendSlim.Release();
                }
                catch { }

                if (webSocketOpened)
                {
                    webSocketOpened = false;
                    if (clientWebSocket != null)
                        log.LogDebug("[ClientWebSocketClosed] CloseStatus:[{CloseStatus}] -  CloseStatusDescription:[{CloseStatusDescription}]", clientWebSocket.CloseStatus, clientWebSocket.CloseStatusDescription);
                    else
                        log.LogDebug("[ClientWebSocketClosed]");

                    RaiseWebSocketClosed();
                }
            }
        }

        private void RaiseWebSocketOpened()
        {
            log.LogDebug("Web socket opened");
            try
            {
                WebSocketOpened?.Invoke(this, null);
            }
            catch (Exception ex)
            {
                log.LogError("[RaiseWebSocketOpened]  - Exception raising WebSocketOpened:[{Exception}]", ex);
            }
        }

        private void RaiseWebSocketClosed()
        {
            log.LogDebug("Web socket closed");
            try
            {
                WebSocketClosed?.Invoke(this, null);
            }
            catch (Exception ex)
            {
                log.LogError("[RaiseWebSocketClosed]  - Exception raising WebSocketClosed:[{Exception}]", ex);
            }
        }
    }
}
