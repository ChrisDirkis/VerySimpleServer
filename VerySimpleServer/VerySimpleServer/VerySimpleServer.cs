using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace VerySimpleServer {
    public class VerySimpleServer : IDisposable {
        public const int DefaultMaxConcurrentRequests = 100;
        private const string GetMethod = "GET";
        
        private HttpListener listener;
        private CancellationTokenSource cancellationTokenSource;

        private List<string> prefixes;
        private Dictionary<string, Action<HttpListenerContext>> getDelegateRoutes;
        private Dictionary<string, (byte[], string)> getDataRoutes;

        private List<Action<string>> logDelegates;

        private VerySimpleServer() { }

        public async Task Start() {
            cancellationTokenSource = new CancellationTokenSource();
            await Start(DefaultMaxConcurrentRequests, cancellationTokenSource.Token);
        }

        public async Task Start(int maxConcurrentRequests, CancellationToken token) {
            listener = new HttpListener();
            foreach (var prefix in prefixes) {
                listener.Prefixes.Add(prefix);
            }
            listener.Start();

            var requests = new HashSet<Task>();
            for (int i = 0; i < maxConcurrentRequests; i++)
                requests.Add(listener.GetContextAsync());

            while (!token.IsCancellationRequested) {
                Task t = await Task.WhenAny(requests);
                requests.Remove(t);

                if (t is Task<HttpListenerContext>) {
                    var context = (t as Task<HttpListenerContext>).Result;
                    requests.Add(ProcessRequestAsync(context));
                    requests.Add(listener.GetContextAsync());
                }
            }
        }

        public void Stop() {
            if (cancellationTokenSource?.IsCancellationRequested ?? false) {
                cancellationTokenSource.Cancel();
            }
            listener.Stop();
        }

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

            public Builder WithLocalhost(int port = 80)
                => WithPrefix($"http://localhost:${port}");

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

            public VerySimpleServer Build() {
                return new VerySimpleServer() {
                    prefixes = prefixes,
                    getDelegateRoutes = getDelegateRoutes,
                    getDataRoutes = getDataRoutes,
                    logDelegates =  logDelegates,
                };
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
