using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Sharp.Xmpp.Core
{
    /// <summary>
    /// Manages thousands of WebSocket client connections efficiently using channels and optimized threading
    /// </summary>
    public class WebSocketClientManager
    {
        private static WebSocketClientManager _instance = null;

        // Thread-safe dictionary to store active client connections
        private readonly ConcurrentDictionary<string, WebSocketClientManaged> _clients;
        
        // Channel-based message queues for efficient async processing
        private readonly Channel<OutgoingMessage> _sendChannel;
        private readonly Channel<IncomingMessage> _receiveChannel;
        
        // Worker tasks for processing messages
        private readonly List<Task> _sendWorkers;
        private readonly List<Task> _receiveWorkers;
        
        // Cancellation token for graceful shutdown
        private CancellationTokenSource _shutdownCts;
        
        // Buffer size for receiving messages
        private const int BufferSize = 8192;
        
        // Number of worker threads for send/receive operations
        private readonly int _sendWorkerCount;
        private readonly int _receiveWorkerCount;

        // Event handlers (using ThreadPool for non-blocking execution)
        public event Action<string, string> OnMessageReceived;
        public event Action<string> OnClientConnected;
        public event Action<string, string, Exception> OnClientDisconnected;
        public event Action<string, Exception> OnClientError;

        public static WebSocketClientManager Instance
        {
            get => _instance ??= new WebSocketClientManager();
        }

        private WebSocketClientManager(
            int sendWorkerCount = 10,
            int receiveWorkerCount = 10)
        {
            _clients = new ConcurrentDictionary<string, WebSocketClientManaged>();
            
            // Unbounded channels for maximum throughput
            _sendChannel = Channel.CreateUnbounded<OutgoingMessage>(new UnboundedChannelOptions
            {
                SingleWriter = false,
                SingleReader = false
            });
            
            _receiveChannel = Channel.CreateUnbounded<IncomingMessage>(new UnboundedChannelOptions
            {
                SingleWriter = false,
                SingleReader = false
            });
            
            _sendWorkers = new List<Task>();
            _receiveWorkers = new List<Task>();
            _shutdownCts = new CancellationTokenSource();
           
            _sendWorkerCount = sendWorkerCount;
            _receiveWorkerCount = receiveWorkerCount;
            
            // Start worker threads
            StartWorkers();
        }

        /// <summary>
        /// Starts background worker threads for processing messages
        /// </summary>
        private void StartWorkers()
        {
            // Send workers - process outgoing messages from channel
            for (int i = 0; i < _sendWorkerCount; i++)
            {
                _sendWorkers.Add(Task.Run(() => SendWorkerAsync(_shutdownCts.Token)));
            }
            
            // Receive workers - process incoming messages from channel
            for (int i = 0; i < _receiveWorkerCount; i++)
            {
                _receiveWorkers.Add(Task.Run(() => ReceiveWorkerAsync(_shutdownCts.Token)));
            }
        }

        /// <summary>
        /// Worker thread that processes outgoing messages from the channel
        /// </summary>
        private async Task SendWorkerAsync(CancellationToken ct)
        {
            await foreach (var msg in _sendChannel.Reader.ReadAllAsync(ct))
            {
                try
                {
                    if (!_clients.TryGetValue(msg.ClientId, out var client))
                        continue;

                    if (client.WebSocket.State != WebSocketState.Open)
                        continue;

                    try
                    {
                        var buffer = Encoding.UTF8.GetBytes(msg.Message);
                        await client.WebSocket.SendAsync(
                            new ArraySegment<byte>(buffer),
                            WebSocketMessageType.Text,
                            true,
                            ct);

                        msg.CompletionSource?.TrySetResult(true);
                    }
                    catch (Exception ex)
                    {
                        msg.CompletionSource?.TrySetResult(false);
                        ThreadPool.QueueUserWorkItem(_ => OnClientError?.Invoke(msg.ClientId, ex));
                    }
                }
                catch { }
            }
        }

        /// <summary>
        /// Worker thread that processes incoming messages from the channel
        /// </summary>
        private async Task ReceiveWorkerAsync(CancellationToken ct)
        {
            await foreach (var msg in _receiveChannel.Reader.ReadAllAsync(ct))
            {
                try
                {
                    if (!_clients.TryGetValue(msg.ClientId, out var client))
                        continue;

                    // Update last activity timestamp
                    client.UpdateLastActivity();

                    // Fire event on ThreadPool to avoid blocking worker
                    ThreadPool.QueueUserWorkItem(_ => OnMessageReceived?.Invoke(msg.ClientId, msg.Message));
                }
                catch { }
            }
        }

        /// <summary>
        /// Creates and connects a new WebSocket client to a server
        /// </summary>
        public async Task ConnectAsync(string clientId, string serverUrl, WebProxy webProxy, CancellationToken ct = default)
        {
            //string clientId = Guid.NewGuid().ToString();
            var client = new WebSocketClientManaged(clientId, serverUrl);

            if (_clients.TryAdd(clientId, client))
            {
                try
                {
                    // Configure WebSocket for better performance
                    client.WebSocket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
                    client.WebSocket.Options.Proxy = webProxy;

                    await client.WebSocket.ConnectAsync(new Uri(serverUrl), ct);
                    
                    ThreadPool.QueueUserWorkItem(_ => OnClientConnected?.Invoke(clientId));

                    // Start receiving messages in background (one task per connection for minimal latency)
                    _ = Task.Run(() => ReceiveMessagesAsync(client, _shutdownCts.Token), ct);

                    return;
                }
                catch (Exception ex)
                {
                    _clients.TryRemove(clientId, out _);
                    ThreadPool.QueueUserWorkItem(_ => OnClientDisconnected?.Invoke(clientId, "", ex));
                    throw;
                }
            }

            throw new InvalidOperationException("Failed to add client to manager");
        }

        /// <summary>
        /// Continuously receives messages from a WebSocket client and pushes to channel
        /// </summary>
        private async Task ReceiveMessagesAsync(WebSocketClientManaged client, CancellationToken ct)
        {
            // Use ArrayPool for better memory efficiency with many connections
            var buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(BufferSize);
            var messageBuilder = new StringBuilder();

            try
            {
                while (client.WebSocket.State == WebSocketState.Open && !ct.IsCancellationRequested)
                {
                    WebSocketReceiveResult result;
                    
                    do
                    {
                        result = await client.WebSocket.ReceiveAsync(
                            new ArraySegment<byte>(buffer, 0, BufferSize), ct);

                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            await DisconnectAsync(client.Id, "Server closed connection");
                            return;
                        }

                        if (result.MessageType == WebSocketMessageType.Text)
                        {
                            messageBuilder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                        }
                    }
                    while (!result.EndOfMessage);

                    if (messageBuilder.Length > 0)
                    {
                        string message = messageBuilder.ToString();
                        messageBuilder.Clear();

                        // Push to channel for worker processing
                        await _receiveChannel.Writer.WriteAsync(
                            new IncomingMessage(client.Id, message), ct);
                    }
                }
            }
            catch (WebSocketException ex)
            {
                ThreadPool.QueueUserWorkItem(_ => OnClientError?.Invoke(client.Id, ex));
            }
            catch (OperationCanceledException)
            {
                // Normal cancellation
            }
            finally
            {
                System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
                await DisconnectAsync(client.Id, "Connection lost");
            }
        }

        /// <summary>
        /// Sends a text message through a specific client connection (via channel)
        /// </summary>
        public async Task<bool> SendAsync(string clientId, string message)
        {
            try
            {
                if (!_clients.TryGetValue(clientId, out var client))
                    return false;

                if (client.WebSocket.State != WebSocketState.Open)
                    return false;

                var tcs = new TaskCompletionSource<bool>();
                var msg = new OutgoingMessage(clientId, message, tcs);

                await _sendChannel.Writer.WriteAsync(msg);

                // Wait for the send to complete (with timeout)
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(Timeout.Infinite, cts.Token));

                return completedTask == tcs.Task && await tcs.Task;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Sends a message through all connected clients
        /// </summary>
        public async Task BroadcastAsync(string message)
        {
            var tasks = new List<Task>();

            foreach (var kvp in _clients)
            {
                if (kvp.Value.WebSocket.State == WebSocketState.Open)
                {
                    // Queue directly to channel without waiting
                    var msg = new OutgoingMessage(kvp.Key, message, null);
                    await _sendChannel.Writer.WriteAsync(msg);
                }
            }
        }

        /// <summary>
        /// Sends a message through specific clients
        /// </summary>
        public async Task SendToMultipleAsync(IEnumerable<string> clientIds, string message)
        {
            foreach (var clientId in clientIds)
            {
                try
                {
                    if (_clients.TryGetValue(clientId, out var client) &&
                        client.WebSocket.State == WebSocketState.Open)
                    {
                        var msg = new OutgoingMessage(clientId, message, null);
                        await _sendChannel.Writer.WriteAsync(msg);
                    }
                }
                catch { }
            }
        }

        /// <summary>
        /// Disconnects a specific client
        /// </summary>
        public async Task DisconnectAsync(string clientId, string reason = "Client disconnect")
        {
            if (_clients.TryRemove(clientId, out var client))
            {
                try
                {
                    if (client.WebSocket.State == WebSocketState.Open)
                    {
                        await client.WebSocket.CloseAsync(
                            WebSocketCloseStatus.NormalClosure,
                            reason,
                            CancellationToken.None);
                    }
                }
                catch (Exception)
                {
                    // Ignore errors during disconnect
                }
                finally
                {
                    client.WebSocket.Dispose();
                    ThreadPool.QueueUserWorkItem(_ => OnClientDisconnected?.Invoke(clientId, reason, null));
                }
            }
        }

        /// <summary>
        /// Checks if a client is connected
        /// </summary>
        public bool IsConnected(string clientId)
        {
            try
            {
                return _clients.TryGetValue(clientId, out var client) &&
                       client.WebSocket.State == WebSocketState.Open;
            }
            catch { return false; }
        }

        /// <summary>
        /// Gets the current number of active connections
        /// </summary>
        public int GetConnectionCount() => _clients.Count;

        /// <summary>
        /// Gets all connected client IDs
        /// </summary>
        public IEnumerable<string> GetConnectedClientIds()
        {
            return _clients.Where(kvp => kvp.Value.WebSocket.State == WebSocketState.Open)
                          .Select(kvp => kvp.Key);
        }

        /// <summary>
        /// Disconnects all clients and cleans up resources
        /// </summary>
        public async Task DisconnectAllAsync()
        {
            // Signal shutdown to all workers
            _shutdownCts.Cancel();
            
            // Close all send/receive channels
            _sendChannel.Writer.Complete();
            _receiveChannel.Writer.Complete();
            
            // Wait for workers to finish
            await Task.WhenAll(_sendWorkers.Concat(_receiveWorkers));
            
            // Disconnect all clients
            var tasks = _clients.Keys.Select(id => DisconnectAsync(id, "Manager shutdown")).ToList();
            await Task.WhenAll(tasks);
        }
    }

    /// <summary>
    /// Represents a single managed WebSocket client connection
    /// </summary>
    public class WebSocketClientManaged
    {
        public string Id { get; }
        public string ServerUrl { get; }
        public ClientWebSocket WebSocket { get; }
        public DateTime ConnectedAt { get; }
        private DateTime _lastActivity;
        public DateTime LastActivity => _lastActivity;

        public WebSocketClientManaged(string id, string serverUrl)
        {
            Id = id;
            ServerUrl = serverUrl;
            WebSocket = new ClientWebSocket();
            ConnectedAt = DateTime.UtcNow;
            _lastActivity = DateTime.UtcNow;
        }

        /// <summary>
        /// Updates the last activity timestamp (thread-safe)
        /// </summary>
        public void UpdateLastActivity()
        {
            _lastActivity = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Represents an outgoing message to be sent
    /// </summary>
    internal class OutgoingMessage
    {
        public string ClientId { get; }
        public string Message { get; }
        public TaskCompletionSource<bool> CompletionSource { get; }

        public OutgoingMessage(string clientId, string message, TaskCompletionSource<bool> completionSource)
        {
            ClientId = clientId;
            Message = message;
            CompletionSource = completionSource;
        }
    }

    /// <summary>
    /// Represents an incoming message received
    /// </summary>
    internal class IncomingMessage
    {
        public string ClientId { get; }
        public string Message { get; }

        public IncomingMessage(string clientId, string message)
        {
            ClientId = clientId;
            Message = message;
        }
    }
}