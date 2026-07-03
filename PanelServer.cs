// ============================================================
//  SIM FFB - Servidor del panel web (http + websocket, 127.0.0.1:8770)
//  Sirve la interfaz (carpeta panel/) y envia datos en vivo /
//  recibe cambios de ajustes por WebSocket.
// ============================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace SimFfb
{
    class PanelServer
    {
        readonly HttpListener listener = new HttpListener();
        static string Root { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "panel"); } }
        static CultureInfo IC = CultureInfo.InvariantCulture;

        public void Start()
        {
            listener.Prefixes.Add("http://127.0.0.1:8770/");
            listener.Start();
            Task.Run(() => AcceptLoop());
        }

        async Task AcceptLoop()
        {
            while (true)
            {
                HttpListenerContext ctx;
                try { ctx = await listener.GetContextAsync(); } catch { break; }
                if (ctx.Request.IsWebSocketRequest) { var ig = HandleWs(ctx); }
                else ServeFile(ctx);
            }
        }

        void ServeFile(HttpListenerContext ctx)
        {
            try
            {
                string path = ctx.Request.Url.AbsolutePath;
                if (path == "/" || string.IsNullOrEmpty(path)) path = "/index.html";
                string file = Path.Combine(Root, path.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(file)) { ctx.Response.StatusCode = 404; ctx.Response.Close(); return; }
                string ext = Path.GetExtension(file).ToLower();
                ctx.Response.ContentType = ext == ".html" ? "text/html" : ext == ".css" ? "text/css" : ext == ".js" ? "application/javascript" : "text/plain";
                ctx.Response.Headers.Add("Cache-Control", "no-store, no-cache, must-revalidate");
                var bytes = File.ReadAllBytes(file);
                ctx.Response.ContentLength64 = bytes.Length;
                ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
                ctx.Response.Close();
            }
            catch { try { ctx.Response.Close(); } catch { } }
        }

        async Task HandleWs(HttpListenerContext ctx)
        {
            WebSocket ws;
            try { ws = (await ctx.AcceptWebSocketAsync(null)).WebSocket; } catch { return; }
            // recibir ajustes en un task aparte
            var recv = Task.Run(() => ReceiveLoop(ws));
            // enviar datos en vivo
            try
            {
                await SendText(ws, BuildInit());
                while (ws.State == WebSocketState.Open)
                {
                    await SendText(ws, BuildLive());
                    await Task.Delay(33);
                }
            }
            catch { }
        }

        async Task ReceiveLoop(WebSocket ws)
        {
            var buf = new byte[4096];
            try
            {
                while (ws.State == WebSocketState.Open)
                {
                    var r = await ws.ReceiveAsync(new ArraySegment<byte>(buf), CancellationToken.None);
                    if (r.MessageType == WebSocketMessageType.Close) break;
                    string msg = Encoding.UTF8.GetString(buf, 0, r.Count);
                    Apply(msg);
                }
            }
            catch { }
        }

        void Apply(string json)
        {
            Dictionary<string, object> d;
            try { d = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json); } catch { return; }
            object t; if (!d.TryGetValue("t", out t)) return;
            string type = t.ToString();
            try
            {
                if (type == "ffb")
                {
                    string k = S(d, "k"); float v = F(d, "v");
                    if (k == "center") Cfg.CenterGain = v; else if (k == "lateral") Cfg.LateralGain = v;
                    else if (k == "impact") Cfg.ImpactGain = v; else if (k == "rumble") Cfg.RumbleGain = v;
                    else if (k == "drift") Cfg.DriftLighten = v;
                    Cfg.SaveFfb();
                }
                else if (type == "invffb") { Cfg.InvertFfb = B(d, "v"); Cfg.SaveFfb(); }
                else if (type == "map")
                {
                    string tgt = S(d, "tgt"), ax = S(d, "ax").ToUpper(); bool inv = B(d, "inv");
                    if (tgt == "steer") { Cfg.AxSteer = ax; Cfg.InvSteer = inv; }
                    else if (tgt == "throttle") { Cfg.AxThrottle = ax; Cfg.InvThrottle = inv; }
                    else if (tgt == "brake") { Cfg.AxBrake = ax; Cfg.InvBrake = inv; }
                    else if (tgt == "clutch") { Cfg.AxClutch = ax; Cfg.InvClutch = inv; }
                    Cfg.SaveInput();
                }
                else if (type == "paddle") { Cfg.BtnPaddleUp = (int)F(d, "up"); Cfg.BtnPaddleDown = (int)F(d, "down"); Cfg.SaveInput(); }
                else if (type == "quit") { Cfg.SaveFfb(); Cfg.SaveInput(); Environment.Exit(0); }
            }
            catch { }
        }

        string BuildInit()
        {
            var e = Program.Eng;
            var sb = new StringBuilder();
            sb.Append("{\"t\":\"init\"");
            sb.Append(",\"center\":").Append(Fs(Cfg.CenterGain));
            sb.Append(",\"lateral\":").Append(Fs(Cfg.LateralGain));
            sb.Append(",\"impact\":").Append(Fs(Cfg.ImpactGain));
            sb.Append(",\"rumble\":").Append(Fs(Cfg.RumbleGain));
            sb.Append(",\"drift\":").Append(Fs(Cfg.DriftLighten));
            sb.Append(",\"invffb\":").Append(Cfg.InvertFfb ? "true" : "false");
            sb.Append(",\"steerAx\":\"").Append(Cfg.AxSteer).Append("\",\"steerInv\":").Append(Cfg.InvSteer ? "true" : "false");
            sb.Append(",\"thrAx\":\"").Append(Cfg.AxThrottle).Append("\",\"thrInv\":").Append(Cfg.InvThrottle ? "true" : "false");
            sb.Append(",\"brkAx\":\"").Append(Cfg.AxBrake).Append("\",\"brkInv\":").Append(Cfg.InvBrake ? "true" : "false");
            sb.Append(",\"cluAx\":\"").Append(Cfg.AxClutch).Append("\",\"cluInv\":").Append(Cfg.InvClutch ? "true" : "false");
            sb.Append(",\"pup\":").Append(Cfg.BtnPaddleUp).Append(",\"pdn\":").Append(Cfg.BtnPaddleDown);
            sb.Append("}");
            return sb.ToString();
        }

        string BuildLive()
        {
            var e = Program.Eng;
            var sb = new StringBuilder();
            sb.Append("{\"t\":\"live\"");
            sb.Append(",\"steer\":").Append(Fs(e.SteerVal));
            sb.Append(",\"thr\":").Append(Fs(e.ThrottleVal));
            sb.Append(",\"brk\":").Append(Fs(e.BrakeVal));
            sb.Append(",\"clu\":").Append(Fs(e.ClutchVal));
            sb.Append(",\"btn\":[");
            for (int i = 0; i < 24; i++) { sb.Append(i < e.Buttons.Length && e.Buttons[i] ? "1" : "0"); if (i < 23) sb.Append(","); }
            sb.Append("]");
            sb.Append(",\"pov\":").Append(e.Pov);
            sb.Append(",\"wheel\":").Append(e.WheelOk ? "true" : "false");
            sb.Append(",\"name\":\"").Append(Esc(e.WheelName)).Append("\"");
            sb.Append(",\"five\":").Append(e.FiveMConnected ? "true" : "false");
            sb.Append("}");
            return sb.ToString();
        }

        static async Task SendText(WebSocket ws, string s)
        {
            var b = Encoding.UTF8.GetBytes(s);
            await ws.SendAsync(new ArraySegment<byte>(b), WebSocketMessageType.Text, true, CancellationToken.None);
        }
        static string Fs(float v) { return v.ToString("0.###", IC); }
        static string Esc(string s) { return (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\'"); }
        static string S(Dictionary<string, object> d, string k) { object o; return d.TryGetValue(k, out o) && o != null ? o.ToString() : ""; }
        static float F(Dictionary<string, object> d, string k) { object o; if (d.TryGetValue(k, out o) && o != null) { try { return Convert.ToSingle(o, IC); } catch { } } return 0; }
        static bool B(Dictionary<string, object> d, string k) { object o; if (d.TryGetValue(k, out o) && o != null) { try { return Convert.ToBoolean(o); } catch { } } return false; }
    }
}
