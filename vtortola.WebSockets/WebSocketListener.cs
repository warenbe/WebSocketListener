﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using vtortola.WebSockets.Http;
using vtortola.WebSockets.Threading;
using vtortola.WebSockets.Tools;
using vtortola.WebSockets.Transports;

#pragma warning disable 420

namespace vtortola.WebSockets
{
    public sealed class WebSocketListener : IDisposable
    {
        private const int STATE_STOPPED = 0;
        private const int STATE_STARTING = 1;
        private const int STATE_STARTED = 2;
        private const int STATE_STOPPING = 3;
        private const int STATE_DISPOSED = 5;

        private static readonly Listener[] EmptyListeners = new Listener[0];
        private static readonly EndPoint[] EmptyEndPoints = new EndPoint[0];

        private readonly ILogger log;
        private readonly HttpNegotiationQueue negotiationQueue;
        private readonly WebSocketListenerOptions options;
        private readonly Uri[] listeningPrefixes;
        private volatile AsyncConditionSource stopConditionSource;
        private volatile Listener[] listeners;
        private volatile EndPoint[] localEndPoints;
        private volatile int state = STATE_STOPPED;

        public bool IsStarted => this.state == STATE_STARTED;

        public IReadOnlyCollection<EndPoint> LocalEndpoints => this.localEndPoints;

        public WebSocketListener(IPEndPoint endpoint)
            : this(endpoint, new WebSocketListenerOptions())
        {
        }
        public WebSocketListener(IPEndPoint endpoint, WebSocketListenerOptions options)
            : this(new[] { new Uri("tcp://" + endpoint) }, options)
        {

        }
        public WebSocketListener(Uri[] prefixes, WebSocketListenerOptions options)
        {
            if (prefixes == null) throw new ArgumentNullException(nameof(prefixes));
            if (prefixes.Length == 0) throw new ArgumentException("At least one prefix should be specified.", nameof(prefixes));
            if (prefixes.Any(p => p == null)) throw new ArgumentException("Null objects passed in array.", nameof(prefixes));
            if (options == null) throw new ArgumentNullException(nameof(options));

            options.CheckCoherence();
            this.options = options.Clone();
            if (this.options.BufferManager == null)
                this.options.BufferManager = BufferManager.CreateBufferManager(100, this.options.SendBufferSize); // create small buffer pool if not configured
            if (this.options.Logger == null)
                this.options.Logger = NullLogger.Instance;
            this.log = this.options.Logger;

            this.listeners = EmptyListeners;
            this.localEndPoints = EmptyEndPoints;
            this.listeningPrefixes = prefixes;

            this.negotiationQueue = new HttpNegotiationQueue(options.Standards, options.ConnectionExtensions, this.options);
        }

        public async Task StartAsync()
        {
            if (this.options.Standards.Count <= 0) throw new WebSocketException($"There are no WebSocket standards. Please, register standards using {nameof(WebSocketListenerOptions)}.{nameof(WebSocketListenerOptions.Standards)}.");
            if (this.options.Transports.Count <= 0) throw new WebSocketException($"There are no WebSocket transports. Please, register transports using {nameof(WebSocketListenerOptions)}.{nameof(WebSocketListenerOptions.Transports)}.");

            if (Interlocked.CompareExchange(ref state, STATE_STARTING, STATE_STOPPED) != STATE_STOPPED)
                throw new WebSocketException("Failed to start listener from current state. Maybe it is disposed or already started.");

            this.options.SetUsed(true);
            var listeners = default(Listener[]);
            try
            {
                if (this.log.IsDebugEnabled)
                    this.log.Debug($"{nameof(WebSocketListener)} is starting.");

                var endPoints = new Tuple<Uri, WebSocketTransport>[this.listeningPrefixes.Length];
                for (var i = 0; i < this.listeningPrefixes.Length; i++)
                {
                    var prefix = this.listeningPrefixes[i];
                    var transport = default(WebSocketTransport);
                    if (this.options.Transports.TryGetWebSocketTransport(prefix, out transport) == false)
                        throw new WebSocketException($"Unable to find transport for '{prefix}'. Available transports are: {string.Join(", ", this.options.Transports.SelectMany(t => t.Schemes).Distinct())}.");

                    endPoints[i] = Tuple.Create(prefix, transport);
                }

                listeners = new Listener[endPoints.Length];
                for (var i = 0; i < endPoints.Length; i++)
                    listeners[i] = await endPoints[i].Item2.ListenAsync(endPoints[i].Item1, this.options).ConfigureAwait(false);


                this.listeners = listeners;
                this.localEndPoints = this.listeners.SelectMany(l => l.LocalEndpoints).ToArray();
                this.stopConditionSource = new AsyncConditionSource(isSet: true) { ContinueOnCapturedContext = false };

                if (Interlocked.CompareExchange(ref state, STATE_STARTED, STATE_STARTING) != STATE_STARTING)
                    throw new WebSocketException("Failed to start listener from current state. Maybe it is disposed.");

                this.AcceptConnectionsAsync().LogFault(this.log);

                if (this.log.IsDebugEnabled)
                    this.log.Debug($"{nameof(WebSocketListener)} is started.");

                listeners = null;
            }
            catch
            {
                this.options.SetUsed(false);
                throw;
            }
            finally
            {
                // try to revert from starting state to stopped state
                Interlocked.CompareExchange(ref state, STATE_STOPPED, STATE_STARTING);

                if (listeners != null)
                {
                    foreach (var listener in listeners)
                        SafeEnd.Dispose(listener);

                    this.listeners = EmptyListeners;
                    this.localEndPoints = EmptyEndPoints;
                    this.stopConditionSource = null;
                }
            }
        }
        public async Task StopAsync()
        {
            if (Interlocked.CompareExchange(ref state, STATE_STOPPING, STATE_STARTED) != STATE_STARTED)
                throw new WebSocketException("Failed to stop listener from current state. Maybe it is disposed or not started.");

            this.options.SetUsed(false);
            var stopCondition = this.stopConditionSource;

            if (this.log.IsDebugEnabled)
                this.log.Debug($"{nameof(WebSocketListener)} is stopping.");

            // TODO: wait for all pending websockets and set stopCondition after it

            this.localEndPoints = EmptyEndPoints;
            var listeners = Interlocked.Exchange(ref this.listeners, EmptyListeners);
            foreach (var listener in listeners)
                SafeEnd.Dispose(listener, this.log);

            if (stopCondition != null)
                await stopCondition;

            if (Interlocked.CompareExchange(ref state, STATE_STOPPED, STATE_STOPPING) != STATE_STOPPING)
                throw new WebSocketException("Failed to stop listener from current state. Maybe it is disposed.");

            if (this.log.IsDebugEnabled)
                this.log.Debug($"{nameof(WebSocketListener)} is stopped.");
        }

        private async Task AcceptConnectionsAsync()
        {
            await Task.Yield();

            var listeners = this.listeners;
            var acceptTasks = new Task<Connection>[listeners.Length];
            var listenerIndex = 0; // this is prevent starvation of array-end listeners when using 'choose from any' algorithm
            try
            {
                while (this.IsStarted)
                {
                    for (var i = 0; i < acceptTasks.Length; i++)
                    {
                        if (acceptTasks[i] != null) continue;

                        try
                        {
                            acceptTasks[i] = this.listeners[i].AcceptConnectionAsync();
                        }
                        catch (Exception acceptError) when (acceptError is ThreadAbortException == false)
                        {
                            acceptTasks[i] = TaskHelper.FailedTask<Connection>(acceptError);
                        }
                    }

                    await Task.WhenAny(acceptTasks).ConfigureAwait(false);

                    if (listenerIndex == ushort.MaxValue)
                        listenerIndex = 0;
                    listenerIndex++;

                    for (var i = 0; i < acceptTasks.Length; i++)
                    {
                        var taskIndex = (listenerIndex + i) % acceptTasks.Length;
                        var acceptTask = acceptTasks[taskIndex];
                        if (acceptTask == null || acceptTask.IsCompleted == false)
                            continue;

                        acceptTasks[taskIndex] = null;
                        var error = acceptTask.Exception.Unwrap();
                        if (acceptTask.Status != TaskStatus.RanToCompletion)
                        {
                            if (this.log.IsDebugEnabled && error != null && error is OperationCanceledException == false)
                                this.log.Debug($"Accept from '{listeners[taskIndex]}' has failed.", error);
                            continue;
                        }

                        var connection = acceptTask.Result;
                        if (this.log.IsDebugEnabled)
                            this.log.Debug($"New client from '{connection}' is connected.");
                        this.negotiationQueue.Queue(connection);
                    }
                }
            }
            finally
            {
                // dispose pending accepts
                foreach (var acceptTask in acceptTasks)
                {
                    acceptTask?.ContinueWith
                    (
                        t => SafeEnd.Dispose(t.Result, this.log),
                        CancellationToken.None,
                        TaskContinuationOptions.OnlyOnRanToCompletion | TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Current
                    ).LogFault(this.log);
                }
                Array.Clear(acceptTasks, 0, acceptTasks.Length);
            }
        }

        public async Task<WebSocket> AcceptWebSocketAsync(CancellationToken token)
        {
            try
            {
                var result = await this.negotiationQueue.DequeueAsync(token).ConfigureAwait(false);

                if (result.Error != null)
                {
                    if (this.log.IsDebugEnabled && result.Error.SourceException.Unwrap() is OperationCanceledException == false)
                        this.log.Debug($"{nameof(this.AcceptWebSocketAsync)} is complete with error.", result.Error.SourceException);

                    result.Error.Throw();
                    return null;
                }
                else
                    return result.Result;
            }
            catch (OperationCanceledException)
            {
                return null;
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref this.state, STATE_DISPOSED) == STATE_DISPOSED)
                return;

            this.stopConditionSource?.Set();

            this.localEndPoints = EmptyEndPoints;
            var listeners = Interlocked.Exchange(ref this.listeners, EmptyListeners);
            foreach (var listener in listeners)
                SafeEnd.Dispose(listener, this.log);

            SafeEnd.Dispose(this.negotiationQueue, this.log);
        }
    }
}
