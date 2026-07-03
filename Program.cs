// ============================================================
//  SIM FFB + EMULADOR  (panel web profesional)
//  Motor: lee G923 -> mando Xbox virtual (ViGEm) + FFB.
//  Interfaz: pagina web local (http://127.0.0.1:8770) estilo G HUB.
// ============================================================
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace SimFfb
{
    static class Cfg
    {
        public static float CenterGain = 0.85f, LateralGain = 0.35f, ImpactGain = 1.0f, RumbleGain = 0.7f, DriftLighten = 0.75f;
        public static bool InvertFfb = false; public static int DeviceIndex = -1;
        public static string AxSteer = "X", AxThrottle = "Y", AxBrake = "RZ", AxClutch = "S0";
        public static bool InvSteer = false, InvThrottle = true, InvBrake = true, InvClutch = true;
        public static int BtnPaddleUp = 4, BtnPaddleDown = 5, BtnHandbrake = 0;
        static string Dir { get { return AppDomain.CurrentDomain.BaseDirectory; } }

        public static void Load()
        {
            Read(Path.Combine(Dir, "ffb.cfg"), delegate (string k, string v) { try {
                if (k == "centergain") CenterGain = float.Parse(v); else if (k == "lateralgain") LateralGain = float.Parse(v);
                else if (k == "impactgain") ImpactGain = float.Parse(v); else if (k == "rumblegain") RumbleGain = float.Parse(v);
                else if (k == "driftlighten") DriftLighten = float.Parse(v); else if (k == "deviceindex") DeviceIndex = int.Parse(v);
                else if (k == "invert") InvertFfb = v == "1"; } catch { } });
            string ip = Path.Combine(Dir, "input.cfg");
            if (!File.Exists(ip)) SaveInput();
            Read(ip, delegate (string k, string v) { try {
                if (k == "steering") AxSteer = v.ToUpper(); else if (k == "throttle") AxThrottle = v.ToUpper();
                else if (k == "brake") AxBrake = v.ToUpper(); else if (k == "clutch") AxClutch = v.ToUpper();
                else if (k == "steeringinvert") InvSteer = v == "1"; else if (k == "throttleinvert") InvThrottle = v == "1";
                else if (k == "brakeinvert") InvBrake = v == "1"; else if (k == "clutchinvert") InvClutch = v == "1";
                else if (k == "paddleup") BtnPaddleUp = int.Parse(v); else if (k == "paddledown") BtnPaddleDown = int.Parse(v); } catch { } });
        }
        public static void SaveFfb()
        {
            File.WriteAllText(Path.Combine(Dir, "ffb.cfg"),
                "CenterGain=" + CenterGain + "\nLateralGain=" + LateralGain + "\nImpactGain=" + ImpactGain +
                "\nRumbleGain=" + RumbleGain + "\nDriftLighten=" + DriftLighten + "\nDeviceIndex=" + DeviceIndex +
                "\nInvert=" + (InvertFfb ? "1" : "0") + "\n");
        }
        public static void SaveInput()
        {
            File.WriteAllText(Path.Combine(Dir, "input.cfg"),
                "Steering=" + AxSteer + "\nSteeringInvert=" + (InvSteer ? "1" : "0") + "\nThrottle=" + AxThrottle +
                "\nThrottleInvert=" + (InvThrottle ? "1" : "0") + "\nBrake=" + AxBrake + "\nBrakeInvert=" + (InvBrake ? "1" : "0") +
                "\nClutch=" + AxClutch + "\nClutchInvert=" + (InvClutch ? "1" : "0") + "\nPaddleUp=" + BtnPaddleUp +
                "\nPaddleDown=" + BtnPaddleDown + "\nHandbrake=" + BtnHandbrake + "\n");
        }
        static void Read(string path, Action<string, string> cb)
        {
            if (!File.Exists(path)) return;
            foreach (var line in File.ReadAllLines(path)) { var s = line.Trim(); if (s.Length == 0 || s.StartsWith("#")) continue;
                int i = s.IndexOf('='); if (i < 1) continue; cb(s.Substring(0, i).Trim().ToLower(), s.Substring(i + 1).Trim()); }
        }
    }

    static class Program
    {
        public static Engine Eng;

        [STAThread]
        static void Main()
        {
            Cfg.Load();
            Eng = new Engine();
            new PanelServer().Start();   // sirve la UI en 127.0.0.1:8770
            System.Windows.Forms.Application.EnableVisualStyles();
            System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
            System.Windows.Forms.Application.Run(new MainWindow());
        }
    }
}
