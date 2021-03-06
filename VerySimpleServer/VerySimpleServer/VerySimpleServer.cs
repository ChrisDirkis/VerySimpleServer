﻿using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace VerySimpleServer {
    public class VerySimpleServer : IDisposable {
        public const int DefaultMaxConcurrentRequests = 100;
        public const string TemporaryListenAddress = "http://+:80/Temporary_Listen_Addresses/";
        private const string GetMethod = "GET";
        
        private HttpListener listener;
        private CancellationTokenSource cancellationTokenSource;

        private List<string> prefixes;
        private Dictionary<string, Action<HttpListenerContext>> getDelegateRoutes;
        private Dictionary<string, (byte[], string)> getDataRoutes;

        private int maxConcurrentRequests;
        private CancellationToken cancellationToken;

        private List<Action<string>> logDelegates;

        private VerySimpleServer() { }

        public void Start() {
            if (cancellationToken == null) {
                cancellationTokenSource = new CancellationTokenSource();
                cancellationToken = cancellationTokenSource.Token;
            }

            listener = new HttpListener();
            foreach (var prefix in prefixes) {
                listener.Prefixes.Add(prefix);
            }
            listener.Start();

            var requests = new HashSet<Task>();
            for (int i = 0; i < maxConcurrentRequests; i++)
                requests.Add(listener.GetContextAsync());

            Task.Run(async () => {
                while (!cancellationToken.IsCancellationRequested) {
                    Task t = await Task.WhenAny(requests);
                    requests.Remove(t);

                    if (t is Task<HttpListenerContext>) {
                        var context = await (t as Task<HttpListenerContext>);
                        requests.Add(ProcessRequestAsync(context));
                        requests.Add(listener.GetContextAsync());
                    }
                }
            }, cancellationToken);
        }

        public void Stop() {
            if (cancellationTokenSource?.IsCancellationRequested ?? false) {
                cancellationTokenSource.Cancel();
            }
            if (listener.IsListening) {
                listener.Stop();
            }
        }

        private async Task ProcessRequestAsync(HttpListenerContext context) {
            var route = context.Request.Url.AbsolutePath;
            var method = context.Request.HttpMethod.ToUpper();
            if (method == GetMethod && getDelegateRoutes.TryGetValue(route, out var getDelegate)) {
                getDelegate(context);
            }
            else if (method == GetMethod && getDataRoutes.TryGetValue(route, out var dataRoute)) {
                var (response, mimeType) = dataRoute;
                context.Response.StatusCode = 200;
                context.Response.ContentType = mimeType;
                context.Response.ContentLength64 = response.Length;
                await context.Response.OutputStream.WriteAsync(response, 0, response.Length);
                context.Response.Close();
            }
            else {
                Log($"Failed ${context.Request.ContentType} request to {context.Request.Url}, no matching route");
                context.Response.StatusCode = 404;
                context.Response.Close();
            }
        }

        private void Log(string toLog) {
            foreach (var logDelegate in logDelegates) {
                logDelegate(toLog);
            }
        }

        // TODO CA1063
        public void Dispose() {
            Stop();
        }

        public class Builder {
            private List<string> prefixes = new List<string>();
            private Dictionary<string, Action<HttpListenerContext>> getDelegateRoutes = new Dictionary<string, Action<HttpListenerContext>>();
            private Dictionary<string, (byte[], string)> getDataRoutes = new Dictionary<string, (byte[], string)>();
            private List<Action<string>> logDelegates = new List<Action<string>>();
            private int maxConcurrentRequests = DefaultMaxConcurrentRequests;
            private CancellationToken cancellationToken;

            public Builder WithLocalhost(int port = 8080)
                => WithPrefix($"http://localhost:{port}/");

            /// <summary>
            /// This uses the preconfigured temporary listening address, which is always available as a non-admin on Windows
            /// Other applications may be trying to use this (apparently Skype), so it is not recommended.
            /// </summary>
            /// <returns></returns>
            public Builder WithTemporaryListenAddress()
                => WithPrefix(TemporaryListenAddress);

            public Builder WithPrefix(string prefix) {
                prefixes.Add(prefix);
                return this;
            }

            public Builder WithGetRoute(string route, Action<HttpListenerContext> action) {
                CheckGetRouteCollision(route);
                getDelegateRoutes.Add(route, action);
                return this;
            }

            public Builder WithGetRoute(string route, byte[] response, string mimeType = MimeTypes.DefaultBytes) {
                CheckGetRouteCollision(route);
                getDataRoutes.Add(route, (response, mimeType));
                return this;
            }

            public Builder WithGetRoute(string route, string response, string mimeType = MimeTypes.DefaultText)
                => WithGetRoute(route, Encoding.UTF8.GetBytes(response), mimeType);

            public Builder WithLogger(Action<string> logDelegate) {
                logDelegates.Add(logDelegate);
                return this;
            }

            public Builder WithMaxConcurrentRequests(int maxConcurrentRequests) {
                this.maxConcurrentRequests = maxConcurrentRequests;
                return this;
            }


            public Builder WithCancellationToken(CancellationToken token) {
                cancellationToken = token;
                return this;
            }


            public VerySimpleServer Start() {
                if (prefixes.Count == 0) {
                    // TODO more specific exception
                    throw new Exception("No prefixes provided");
                }
                var server = new VerySimpleServer() {
                    prefixes = prefixes,
                    getDelegateRoutes = getDelegateRoutes,
                    getDataRoutes = getDataRoutes,
                    logDelegates = logDelegates,
                    maxConcurrentRequests = maxConcurrentRequests,
                    cancellationToken = cancellationToken,
                };
                server.Start();
                return server;
            }

            private void CheckGetRouteCollision(string route) {
                if (getDelegateRoutes.ContainsKey(route) || getDataRoutes.ContainsKey(route)) {
                    // TODO more specific exception
                    throw new Exception($"Route '${route}' has already been added");
                }
            }
        }
    }
}
