// ============================================================
//  SIM FFB - Motor (volante + mando virtual + FFB + WebSocket)
//  Expone valores en vivo para que el panel los dibuje.
// ============================================================
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using SharpDX.DirectInput;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;

namespace SimFfb
{
    class Engine
    {
        IntPtr hwnd = IntPtr.Zero;
        Joystick wheel;
        Effect constEffect, rumbleEffect;
        EffectParameters constParams, rumbleParams;
        ConstantForce constForce; PeriodicForce periodic;
        ViGEmClient vigem; IXbox360Controller pad;
        readonly object gate = new object();
        volatile bool running = false;

        // ---- valores en vivo (para el panel) ----
        public volatile bool WheelOk = false;
        public volatile bool FiveMConnected = false;
        public string WheelName = "(sin volante)";
        public float SteerVal = 0f;     // -1..1
        public float ThrottleVal = 0f;  // 0..1
        public float BrakeVal = 0f;     // 0..1
        public float ClutchVal = 0f;    // 0..1
        public bool[] Buttons = new bool[32];
        public int Pov = -1;              // cruceta (POV hat): -1 centrado, 0/9000/18000/27000...
        public string LastError = "";

        public bool Start(IntPtr windowHandle)
        {
            hwnd = windowHandle;
            try
            {
                if (!InitWheel()) { LastError = "No se encontro volante con Force Feedback."; return false; }
                InitViGem();
                running = true;
                new Thread(InputLoop) { IsBackground = true }.Start();
                var srv = new WsServer("http://127.0.0.1:8767/", OnTelemetry, delegate { FiveMConnected = false; ReleaseForces(); });
                srv.Start();
                return true;
            }
            catch (Exception ex) { LastError = ex.Message; return false; }
        }

        public void Stop()
        {
            running = false;
            ReleaseForces();
            try { if (pad != null) pad.Disconnect(); } catch { }
            try { if (wheel != null) wheel.Unacquire(); } catch { }
        }

        bool InitWheel()
        {
            var di = new DirectInput();
            var devices = new List<DeviceInstance>();
            foreach (var d in di.GetDevices(DeviceClass.GameControl, DeviceEnumerationFlags.ForceFeedback)) devices.Add(d);
            if (devices.Count == 0)
                foreach (var d in di.GetDevices(DeviceClass.GameControl, DeviceEnumerationFlags.AttachedOnly)) devices.Add(d);
            if (devices.Count == 0) return false;

            int idx = Cfg.DeviceIndex;
            if (idx < 0 || idx >= devices.Count) { idx = 0;
                for (int i = 0; i < devices.Count; i++) { var n = (devices[i].InstanceName ?? "").ToLower();
                    if (n.Contains("g923") || n.Contains("g920") || n.Contains("logitech") || n.Contains("wheel")) { idx = i; break; } } }
            WheelName = devices[idx].InstanceName;

            wheel = new Joystick(di, devices[idx].InstanceGuid);
            wheel.Properties.BufferSize = 128;
            wheel.SetCooperativeLevel(hwnd, CooperativeLevel.Background | CooperativeLevel.Exclusive);
            wheel.Properties.AutoCenter = false;
            wheel.Acquire();

            var actuators = new List<int>();
            foreach (var obj in wheel.GetObjects(DeviceObjectTypeFlags.ForceFeedbackActuator)) actuators.Add(obj.Offset);
            if (actuators.Count == 0) actuators.Add(0);
            int[] ax = actuators.ToArray(); int[] dir = new int[ax.Length]; dir[0] = 1;

            constForce = new ConstantForce { Magnitude = 0 };
            constParams = new EffectParameters { Flags = EffectFlags.Cartesian | EffectFlags.ObjectOffsets,
                Duration = int.MaxValue, SamplePeriod = 0, Gain = 10000, TriggerButton = -1, TriggerRepeatInterval = 0,
                Axes = ax, Directions = dir, Parameters = constForce };
            constEffect = new Effect(wheel, EffectGuid.ConstantForce, constParams); constEffect.Start();

            periodic = new PeriodicForce { Magnitude = 0, Offset = 0, Phase = 0, Period = 40000 };
            rumbleParams = new EffectParameters { Flags = EffectFlags.Cartesian | EffectFlags.ObjectOffsets,
                Duration = int.MaxValue, SamplePeriod = 0, Gain = 10000, TriggerButton = -1, TriggerRepeatInterval = 0,
                Axes = ax, Directions = dir, Parameters = periodic };
            rumbleEffect = new Effect(wheel, EffectGuid.Sine, rumbleParams); rumbleEffect.Start();

            WheelOk = true; return true;
        }

        void InitViGem()
        {
            vigem = new ViGEmClient();
            pad = vigem.CreateXbox360Controller();
            pad.Connect();
        }

        static int RawAxis(JoystickState s, string name)
        {
            switch (name.ToUpper())
            {
                case "X": return s.X; case "Y": return s.Y; case "Z": return s.Z;
                case "RX": return s.RotationX; case "RY": return s.RotationY; case "RZ": return s.RotationZ;
                case "S0": return s.Sliders != null && s.Sliders.Length > 0 ? s.Sliders[0] : 32767;
                case "S1": return s.Sliders != null && s.Sliders.Length > 1 ? s.Sliders[1] : 32767;
                default: return 32767;
            }
        }
        static float N01(int raw, bool inv) { float v = raw / 65535f; if (inv) v = 1f - v; return v < 0 ? 0 : (v > 1 ? 1 : v); }
        static short Thumb(int raw, bool inv) { float c = (raw - 32767f) / 32767f; if (inv) c = -c; c = c < -1 ? -1 : (c > 1 ? 1 : c); return (short)(c * 32767); }
        static byte Trig(int raw, bool inv) { return (byte)(N01(raw, inv) * 255); }

        // valores crudos de cada eje configurado (para el panel de mapeo)
        public int[] LastRaw = new int[8];
        static readonly string[] AXNAMES = { "X", "Y", "Z", "RX", "RY", "RZ", "S0", "S1" };

        void InputLoop()
        {
            while (running && pad != null)
            {
                try
                {
                    wheel.Poll();
                    var st = wheel.GetCurrentState();
                    for (int i = 0; i < AXNAMES.Length; i++) LastRaw[i] = RawAxis(st, AXNAMES[i]);

                    short steer = Thumb(RawAxis(st, Cfg.AxSteer), Cfg.InvSteer);
                    short clutch = (short)(N01(RawAxis(st, Cfg.AxClutch), Cfg.InvClutch) * 32767);
                    byte thr = Trig(RawAxis(st, Cfg.AxThrottle), Cfg.InvThrottle);
                    byte brk = Trig(RawAxis(st, Cfg.AxBrake), Cfg.InvBrake);

                    pad.SetAxisValue(Xbox360Axis.LeftThumbX, steer);
                    pad.SetAxisValue(Xbox360Axis.LeftThumbY, clutch);
                    pad.SetSliderValue(Xbox360Slider.RightTrigger, thr);
                    pad.SetSliderValue(Xbox360Slider.LeftTrigger, brk);

                    var b = st.Buttons;
                    // Cruceta (POV hat) = las flechas del volante
                    var pv = st.PointOfViewControllers;
                    Pov = (pv != null && pv.Length > 0) ? pv[0] : -1;
                    bool up = Pov==31500 || Pov==0 || Pov==4500;
                    bool rt = Pov==4500  || Pov==9000 || Pov==13500;
                    bool dn = Pov==13500 || Pov==18000 || Pov==22500;
                    bool lf = Pov==22500 || Pov==27000 || Pov==31500;
                    pad.SetButtonState(Xbox360Button.Up, up);
                    pad.SetButtonState(Xbox360Button.Right, rt);
                    pad.SetButtonState(Xbox360Button.Down, dn);
                    pad.SetButtonState(Xbox360Button.Left, lf);

                    // Levas de cambio (configurables)
                    pad.SetButtonState(Xbox360Button.RightShoulder, BtnOn(b, Cfg.BtnPaddleUp));
                    pad.SetButtonState(Xbox360Button.LeftShoulder, BtnOn(b, Cfg.BtnPaddleDown));
                    // Botones estandar del G923 en modo Xbox (passthrough)
                    pad.SetButtonState(Xbox360Button.A, BtnOn(b, 0));
                    pad.SetButtonState(Xbox360Button.B, BtnOn(b, 1));
                    pad.SetButtonState(Xbox360Button.X, BtnOn(b, 2));
                    pad.SetButtonState(Xbox360Button.Y, BtnOn(b, 3));
                    pad.SetButtonState(Xbox360Button.Back,  BtnOn(b, 6) || BtnOn(b, 10));  // View / (-)
                    pad.SetButtonState(Xbox360Button.Start, BtnOn(b, 7) || BtnOn(b, 9));   // Menu / (+)
                    pad.SetButtonState(Xbox360Button.LeftThumb,  BtnOn(b, 8));
                    pad.SetButtonState(Xbox360Button.RightThumb, BtnOn(b, 11));

                    // valores en vivo para el panel
                    SteerVal = steer / 32767f;
                    ThrottleVal = thr / 255f;
                    BrakeVal = brk / 255f;
                    ClutchVal = clutch / 32767f;
                    if (b != null) for (int i = 0; i < Buttons.Length && i < b.Length; i++) Buttons[i] = b[i];
                }
                catch { }
                Thread.Sleep(4);
            }
        }
        static bool BtnOn(bool[] b, int i) { return b != null && i >= 0 && i < b.Length && b[i]; }

        // ---------------- FFB ----------------
        void OnTelemetry(string json)
        {
            if (!WheelOk) return;
            FiveMConnected = true;
            Dictionary<string, object> d;
            try { d = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json); } catch { return; }
            if (!GB(d, "inCar", false)) { ReleaseForces(); return; }

            float kmh = GF(d, "kmh", 0), steer = GF(d, "steer", 0), drift = GF(d, "drift", 0),
                  latAcc = GF(d, "latAcc", 0), vertAcc = GF(d, "vertAcc", 0), impact = GF(d, "impact", 0), brake = GF(d, "brake", 0);
            bool onWheels = GB(d, "onWheels", true), handbrk = GB(d, "handbrake", false);

            float speedF = Cf(kmh / 120f, 0f, 1.2f), adrift = Math.Abs(drift);
            float gripF = 1f - Cfg.DriftLighten * Cf((adrift - 6f) / 30f, 0f, 1f);
            if (!onWheels) gripF = 0.15f;
            float center = -steer * Cfg.CenterGain * (0.25f + 0.75f * speedF);
            float lateral = Cf(latAcc / 25f, -1f, 1f) * Cfg.LateralGain * speedF;
            float hit = impact * Cfg.ImpactGain * (steer >= 0 ? 1f : -1f);
            int mag = Ci((int)(((center + lateral) * gripF + hit) * 10000f), -10000, 10000);
            if (Cfg.InvertFfb) mag = -mag;

            float bumps = Cf(Math.Abs(vertAcc) / 30f, 0f, 1f);
            float lockup = (brake > 0.7f && kmh > 8f && kmh < 60f) ? 0.5f : 0f;
            float rum = Cf(bumps + lockup + (handbrk ? 0.4f : 0f), 0f, 1f) * Cfg.RumbleGain;
            int rmag = Ci((int)(rum * 9000f), 0, 10000);

            lock (gate) { try {
                constForce.Magnitude = mag;
                constEffect.SetParameters(constParams, EffectParameterFlags.TypeSpecificParameters | EffectParameterFlags.Start);
                periodic.Magnitude = rmag;
                rumbleEffect.SetParameters(rumbleParams, EffectParameterFlags.TypeSpecificParameters | EffectParameterFlags.Start);
            } catch { } }
        }

        void ReleaseForces()
        {
            if (!WheelOk) return;
            lock (gate) { try {
                if (constForce != null) { constForce.Magnitude = 0; constEffect.SetParameters(constParams, EffectParameterFlags.TypeSpecificParameters | EffectParameterFlags.Start); }
                if (periodic != null) { periodic.Magnitude = 0; rumbleEffect.SetParameters(rumbleParams, EffectParameterFlags.TypeSpecificParameters | EffectParameterFlags.Start); }
            } catch { } }
        }

        static int Ci(int v, int lo, int hi) { return v < lo ? lo : (v > hi ? hi : v); }
        static float Cf(float v, float lo, float hi) { return v < lo ? lo : (v > hi ? hi : v); }
        static float GF(Dictionary<string, object> d, string k, float def) { object o; if (d != null && d.TryGetValue(k, out o) && o != null) { try { return Convert.ToSingle(o); } catch { } } return def; }
        static bool GB(Dictionary<string, object> d, string k, bool def) { object o; if (d != null && d.TryGetValue(k, out o) && o != null) { try { return Convert.ToBoolean(o); } catch { } } return def; }
    }

    // ---------------- Servidor WebSocket ----------------
    class WsServer
    {
        readonly HttpListener listener = new HttpListener();
        readonly Action<string> onMsg; readonly Action onClose;
        public WsServer(string prefix, Action<string> onMsg, Action onClose)
        { listener.Prefixes.Add(prefix); this.onMsg = onMsg; this.onClose = onClose; }
        public void Start() { listener.Start(); Task.Run(() => AcceptLoop()); }
        async Task AcceptLoop()
        {
            while (true) { HttpListenerContext ctx;
                try { ctx = await listener.GetContextAsync(); } catch { break; }
                if (ctx.Request.IsWebSocketRequest) { var ignore = HandleSocket(ctx); }
                else { ctx.Response.StatusCode = 400; ctx.Response.Close(); } }
        }
        async Task HandleSocket(HttpListenerContext ctx)
        {
            WebSocket sock;
            try { sock = (await ctx.AcceptWebSocketAsync(null)).WebSocket; } catch { return; }
            var buf = new byte[8192]; var sb = new StringBuilder();
            try { while (sock.State == WebSocketState.Open) { sb.Clear(); WebSocketReceiveResult r;
                do { r = await sock.ReceiveAsync(new ArraySegment<byte>(buf), CancellationToken.None);
                    if (r.MessageType == WebSocketMessageType.Close) { await sock.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None); onClose(); return; }
                    sb.Append(Encoding.UTF8.GetString(buf, 0, r.Count)); } while (!r.EndOfMessage);
                onMsg(sb.ToString()); } }
            catch { } finally { onClose(); }
        }
    }
}
