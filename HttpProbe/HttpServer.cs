using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace HttpProbe
{
    internal delegate Task<HttpResponse> EndpointHandler(HttpRequest req, CancellationToken ct);

    internal class HttpRequest
    {
        public string Method;
        public string Path;
        public Dictionary<string, string> QueryString;
        public Dictionary<string, string> Headers;
        public byte[] RawBody;          // exactly what came on the wire
        public byte[] DecodedBody;      // base64-decoded if RequireBase64 is on, else == RawBody
        public string DecodedBodyText => DecodedBody != null && DecodedBody.Length > 0 ? Encoding.UTF8.GetString(DecodedBody) : string.Empty;
    }

    internal class HttpResponse
    {
        public int StatusCode = 200;
        public string ContentType = "application/json; charset=utf-8";
        public byte[] Body;

        public static HttpResponse Json(object payload, int status = 200)
        {
            var json = JsonConvert.SerializeObject(payload, Formatting.Indented);
            return new HttpResponse
            {
                StatusCode = status,
                ContentType = "application/json; charset=utf-8",
                Body = Encoding.UTF8.GetBytes(json),
            };
        }

        public static HttpResponse Text(string text, int status = 200)
        {
            return new HttpResponse
            {
                StatusCode = status,
                ContentType = "text/plain; charset=utf-8",
                Body = Encoding.UTF8.GetBytes(text ?? string.Empty),
            };
        }

        public static HttpResponse Bytes(byte[] data, int status = 200, string contentType = "application/octet-stream")
        {
            return new HttpResponse
            {
                StatusCode = status,
                ContentType = contentType,
                Body = data ?? Array.Empty<byte>(),
            };
        }

        public static HttpResponse Error(string message, int status = 400, Exception ex = null)
        {
            return Json(new
            {
                error = message,
                exception = ex?.ToString(),
            }, status);
        }
    }

    internal class HttpServer
    {
        // Method+path → handler. Path may end with "/{id}" — those become wildcard segment matches.
        private readonly ConcurrentDictionary<string, EndpointHandler> _routes = new ConcurrentDictionary<string, EndpointHandler>(StringComparer.OrdinalIgnoreCase);
        // Wildcard routes: stored with their template like "GET /jobs/{id}", checked on miss.
        private readonly List<RouteTemplate> _templateRoutes = new List<RouteTemplate>();
        private readonly object _routesLock = new object();

        private HttpListener _listener;
        private Thread _loopThread;
        private CancellationTokenSource _stopCts;

        public bool IsRunning => _listener != null && _listener.IsListening;

        public IReadOnlyDictionary<string, EndpointHandler> ExactRoutes => _routes;

        public List<string> ListAllRoutes()
        {
            var result = new List<string>();
            foreach (var k in _routes.Keys) result.Add(k);
            lock (_routesLock) foreach (var t in _templateRoutes) result.Add(t.Display);
            result.Sort(StringComparer.OrdinalIgnoreCase);
            return result;
        }

        public void Register(string method, string path, EndpointHandler handler)
        {
            method = (method ?? "GET").ToUpperInvariant();
            if (!path.StartsWith("/")) path = "/" + path;

            if (path.Contains("{") && path.Contains("}"))
            {
                lock (_routesLock)
                {
                    _templateRoutes.RemoveAll(t => t.Method == method && t.Template == path);
                    _templateRoutes.Add(new RouteTemplate(method, path, handler));
                }
            }
            else
            {
                _routes[$"{method} {path}"] = handler;
            }
            Plugin.Log.LogInfo($"[HttpProbe] route registered: {method} {path}");
        }

        public bool Unregister(string method, string path)
        {
            method = (method ?? "GET").ToUpperInvariant();
            if (!path.StartsWith("/")) path = "/" + path;
            bool any = _routes.TryRemove($"{method} {path}", out _);
            lock (_routesLock)
            {
                int removed = _templateRoutes.RemoveAll(t => t.Method == method && t.Template == path);
                if (removed > 0) any = true;
            }
            return any;
        }

        public void Start(string bindAddress, int port)
        {
            if (IsRunning) return;

            _listener = new HttpListener();
            string host = (bindAddress == "*" || bindAddress == "+") ? "+" : bindAddress;
            _listener.Prefixes.Add($"http://{host}:{port}/");
            _listener.Start();

            _stopCts = new CancellationTokenSource();
            _loopThread = new Thread(() => Loop(_stopCts.Token)) { IsBackground = true, Name = "HttpProbe-Listener" };
            _loopThread.Start();

            Plugin.Log.LogInfo($"[HttpProbe] listening on http://{host}:{port}/");
        }

        public void Stop()
        {
            try { _stopCts?.Cancel(); } catch { }
            try { _listener?.Stop(); } catch { }
            try { _listener?.Close(); } catch { }
            _listener = null;
            _stopCts = null;
        }

        private void Loop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && _listener != null && _listener.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = _listener.GetContext(); }
                catch (HttpListenerException) { return; }
                catch (ObjectDisposedException) { return; }
                catch (Exception e) { Plugin.Log.LogWarning($"[HttpProbe] GetContext: {e.Message}"); continue; }

                _ = Task.Run(() => Handle(ctx));
            }
        }

        private async Task Handle(HttpListenerContext ctx)
        {
            HttpResponse resp;
            string method = ctx.Request.HttpMethod ?? "GET";
            string path = ctx.Request.Url?.AbsolutePath ?? "/";

            try
            {
                if (!CheckAuth(ctx)) { resp = HttpResponse.Error("Unauthorized", 401); }
                else
                {
                    var req = await ReadRequest(ctx);
                    var handler = ResolveHandler(method, path, out var pathParams);
                    if (handler == null)
                    {
                        resp = HttpResponse.Error($"No route for {method} {path}", 404);
                    }
                    else
                    {
                        if (pathParams != null) foreach (var kv in pathParams) req.Headers["X-Path-" + kv.Key] = kv.Value;
                        if (Plugin.Cfg.Verbose.Value) Plugin.Log.LogInfo($"[HttpProbe] -> {method} {path} ({req.RawBody?.Length ?? 0}B)");
                        resp = await handler(req, CancellationToken.None) ?? HttpResponse.Json(new { ok = true });
                    }
                }
            }
            catch (Exception e)
            {
                resp = HttpResponse.Error("Unhandled server error", 500, e);
                Plugin.Log.LogError($"[HttpProbe] handler crashed for {method} {path}: {e}");
            }

            try { WriteResponse(ctx, resp); }
            catch (Exception e) { Plugin.Log.LogWarning($"[HttpProbe] WriteResponse: {e.Message}"); }
        }

        private bool CheckAuth(HttpListenerContext ctx)
        {
            string token = Plugin.Cfg.AuthToken.Value;
            if (string.IsNullOrEmpty(token)) return true;
            string given = ctx.Request.Headers["X-Auth-Token"];
            return string.Equals(given, token, StringComparison.Ordinal);
        }

        private async Task<HttpRequest> ReadRequest(HttpListenerContext ctx)
        {
            byte[] raw;
            using (var ms = new MemoryStream())
            {
                await ctx.Request.InputStream.CopyToAsync(ms);
                raw = ms.ToArray();
            }

            byte[] decoded;
            if (Plugin.Cfg.RequireBase64.Value && raw.Length > 0)
            {
                try
                {
                    string b64 = Encoding.UTF8.GetString(raw).Trim();
                    decoded = Convert.FromBase64String(b64);
                }
                catch (Exception e)
                {
                    throw new InvalidOperationException("Body is not valid base64. Set RequireBase64=false to bypass.", e);
                }
            }
            else
            {
                decoded = raw;
            }

            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string k in ctx.Request.Headers.AllKeys) headers[k] = ctx.Request.Headers[k];

            var query = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string k in ctx.Request.QueryString.AllKeys)
            {
                if (k != null) query[k] = ctx.Request.QueryString[k];
            }

            return new HttpRequest
            {
                Method = ctx.Request.HttpMethod ?? "GET",
                Path = ctx.Request.Url?.AbsolutePath ?? "/",
                Headers = headers,
                QueryString = query,
                RawBody = raw,
                DecodedBody = decoded,
            };
        }

        private void WriteResponse(HttpListenerContext ctx, HttpResponse resp)
        {
            byte[] body = resp.Body ?? Array.Empty<byte>();
            string ct = resp.ContentType;

            if (Plugin.Cfg.RequireBase64.Value && body.Length > 0)
            {
                string b64 = Convert.ToBase64String(body);
                body = Encoding.UTF8.GetBytes(b64);
                ct = "application/base64";
            }

            ctx.Response.StatusCode = resp.StatusCode;
            ctx.Response.ContentType = ct;
            ctx.Response.ContentLength64 = body.Length;
            ctx.Response.Headers["X-HttpProbe"] = "1";
            ctx.Response.OutputStream.Write(body, 0, body.Length);
            ctx.Response.OutputStream.Close();
            if (Plugin.Cfg.Verbose.Value)
            {
                Plugin.Log.LogInfo($"[HttpProbe] <- {resp.StatusCode} ({body.Length}B {ct})");
            }
        }

        private EndpointHandler ResolveHandler(string method, string path, out Dictionary<string, string> pathParams)
        {
            pathParams = null;
            string key = $"{method.ToUpperInvariant()} {path}";
            if (_routes.TryGetValue(key, out var h)) return h;

            lock (_routesLock)
            {
                foreach (var t in _templateRoutes)
                {
                    if (t.Method != method.ToUpperInvariant()) continue;
                    if (t.TryMatch(path, out var captured)) { pathParams = captured; return t.Handler; }
                }
            }
            return null;
        }

        private class RouteTemplate
        {
            public string Method;
            public string Template;
            public string Display;
            public EndpointHandler Handler;
            private readonly string[] _segments;

            public RouteTemplate(string method, string template, EndpointHandler handler)
            {
                Method = method;
                Template = template;
                Display = $"{method} {template}";
                Handler = handler;
                _segments = template.Trim('/').Split('/');
            }

            public bool TryMatch(string path, out Dictionary<string, string> captured)
            {
                captured = null;
                var parts = path.Trim('/').Split('/');
                if (parts.Length != _segments.Length) return false;

                for (int i = 0; i < _segments.Length; i++)
                {
                    var seg = _segments[i];
                    if (seg.StartsWith("{") && seg.EndsWith("}"))
                    {
                        captured ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        captured[seg.Substring(1, seg.Length - 2)] = Uri.UnescapeDataString(parts[i]);
                    }
                    else if (!string.Equals(seg, parts[i], StringComparison.OrdinalIgnoreCase))
                    {
                        captured = null;
                        return false;
                    }
                }
                return true;
            }
        }
    }
}
