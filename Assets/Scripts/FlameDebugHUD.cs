using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 调试面板：把火种的全部内部状态摊在屏幕上，代替翻 Console。
///
/// 【为什么用 IMGUI 而不是 Canvas / UI Toolkit】
/// 它是调试工具，不是游戏 UI。IMGUI 不需要 Canvas、不需要字体资产、
/// 不需要预制体，一个脚本拖到任意物体上就能用，删的时候删一个文件。
/// 调试工具就该能整块拔掉——它慢，但慢在调试期完全无所谓。
/// 正式 UI（如果以后需要）另做，不要在这个基础上长。
///
/// 【它和 FlameVisual 一样是只读层】
/// 每帧只从 Flame / Carrier 读值，自己比对上一帧算出"发生了什么"，
/// 因此事件日志不需要 Flame 那边发任何事件、加任何回调。
/// 禁用本组件，游戏行为必须一模一样。
///
/// 用法：拖到场景里任意常驻物体上。F1 开关面板。
/// </summary>
public class FlameDebugHUD : MonoBehaviour
{
    [Header("显示")]
    [SerializeField] private bool  visible = true;
    [SerializeField] private float scale   = 1.5f;    // 高分屏调大
    [SerializeField] private int   maxEvents = 10;

    [Header("也打进 Console")]
    [SerializeField] private bool consoleLog = false;

    // ── 事件日志：自己 diff 出来的，不依赖 Flame 发事件 ──────
    private readonly List<string> events = new List<string>();

    private Flame.FlameState prevState;
    private Carrier          prevCarrier;
    private OxygenZone       prevZone;

    // 画长条用的 1x1 白图，GUI.color 负责染色
    private Texture2D pixel;

    private void Awake()
    {
        pixel = new Texture2D(1, 1);
        pixel.SetPixel(0, 0, Color.white);
        pixel.Apply();

        Flame.ConsoleLog = consoleLog;

        prevState = Flame.Instance.State;
    }

    private void OnDestroy()
    {
        if (pixel != null) Destroy(pixel);
    }

    private void Update()
    {
        var kb = Keyboard.current;
        if (kb != null && kb.f1Key.wasPressedThisFrame) visible = !visible;

        Flame.ConsoleLog = consoleLog;

        TrackEvents();
    }

    /// <summary>
    /// 比对上一帧，把变化记成一条事件。
    /// 全部靠读值推断，Flame 那边一行代码都不用为它改。
    /// </summary>
    private void TrackEvents()
    {
        Flame flame = Flame.Instance;

        if (flame.State != prevState)
        {
            Log($"{prevState} → {flame.State}　｜　{flame.LastReason}");
            prevState = flame.State;
        }

        Carrier carrier = flame.CurrentCarrier;
        if (carrier != prevCarrier)
        {
            if (carrier == null)      Log($"火离开了 {NameOf(prevCarrier)}");
            else if (prevCarrier == null) Log($"火寄居到 {carrier.name}");
            else                      Log($"交接：{NameOf(prevCarrier)} → {carrier.name}");
            prevCarrier = carrier;
        }

        OxygenZone zone = carrier != null ? carrier.CurrentZone : null;
        if (zone != prevZone)
        {
            string from = prevZone != null ? prevZone.name : "露天(兜底)";
            string to   = zone     != null ? zone.name     : "露天(兜底)";
            Log($"区域：{from} → {to}　｜　oxygen {OxygenOf(carrier):F1}");
            prevZone = zone;
        }
    }

    private void Log(string line)
    {
        events.Add($"[{Time.timeSinceLevelLoad,6:F2}s] {line}");
        if (events.Count > maxEvents) events.RemoveAt(0);
    }

    private static string NameOf(Carrier c) => c != null ? c.name : "(无)";
    private static float  OxygenOf(Carrier c) => c != null ? c.Oxygen : 0f;

    // ────────────────────────────────────────────────────────

    private void OnGUI()
    {
        if (!visible) return;

        Matrix4x4 saved = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

        const float w = 300f;
        float h = 250f + maxEvents * 14f;

        GUI.color = new Color(0f, 0f, 0f, 0.78f);
        GUI.DrawTexture(new Rect(8, 8, w, h), pixel);
        GUI.color = Color.white;

        GUILayout.BeginArea(new Rect(16, 14, w - 16, h - 12));
        DrawBody();
        GUILayout.EndArea();

        GUI.matrix = saved;
    }

    private void DrawBody()
    {
        Flame   flame   = Flame.Instance;
        Carrier carrier = flame.CurrentCarrier;

        // ── 状态 ──
        GUI.color = StateColor(flame.State);
        GUILayout.Label($"● {flame.State}", Bold());
        GUI.color = Color.white;

        if (!string.IsNullOrEmpty(flame.LastReason))
        {
            Dim($"最近成因：{flame.LastReason}");
        }

        Space();

        // ── 载体与燃料 ──
        if (carrier == null)
        {
            Dim("载体：(无 —— 火已熄灭)");
        }
        else
        {
            GUILayout.Label($"载体　{carrier.name}　({carrier.GetType().Name})");
            GUILayout.Label(
                $"燃料　{carrier.Fuel,6:F2} / {carrier.FuelCapacity:F0}" +
                $"　　-{carrier.BurnRate:F1}/s{(carrier.IsLit ? "" : "  [未点燃]")}");

            // 长条上画出两条阈值线，这样"离降级还有多远"是看出来的不是算出来的
            Bar(carrier.Fuel / Mathf.Max(carrier.FuelCapacity, 0.0001f),
                FuelColor(carrier.Fuel),
                new[]
                {
                    Flame.EmberFuel / Mathf.Max(carrier.FuelCapacity, 0.0001f),
                    Flame.DyingFuel / Mathf.Max(carrier.FuelCapacity, 0.0001f)
                });

            Space();

            // ── 氧气与区域 ──
            OxygenZone zone = carrier.CurrentZone;
            GUILayout.Label(
                $"氧气　{carrier.Oxygen,6:F2}　　" +
                (zone != null ? zone.name : $"露天(兜底 {OxygenZone.DefaultOxygen:F0})"));

            // 用一个固定量程画，方便和阈值比较
            float oxyMax = Mathf.Max(OxygenZone.DefaultOxygen, carrier.Oxygen);
            Bar(carrier.Oxygen / oxyMax,
                OxygenColor(carrier.Oxygen),
                new[] { Flame.EmberOxygen / oxyMax, Flame.DyingOxygen / oxyMax });

            // 整摞区域。只看最终 oxygen 值看不出"退出小区域有没有正确回到大区域"
            var stack = carrier.OverlappingZones;
            if (stack.Count == 0)
            {
                Dim("区域栈　(空 → 用兜底值)");
            }
            else
            {
                var sb = new StringBuilder("区域栈　");
                for (int i = 0; i < stack.Count; i++)
                {
                    if (i > 0) sb.Append(" › ");
                    sb.Append(stack[i] != null ? stack[i].name : "(已销毁)");
                }
                Dim(sb.ToString());
            }
        }

        Space();

        // ── 计时器 ──
        if (flame.State == Flame.FlameState.Dying)
        {
            GUI.color = new Color(1f, 0.45f, 0.3f);
            GUILayout.Label($"濒熄倒计时　{flame.DyingTimer,5:F2}s / {Flame.DyingDuration:F0}s", Bold());
            GUI.color = Color.white;
            Bar(flame.DyingTimer / Flame.DyingDuration, new Color(1f, 0.35f, 0.2f), null);
        }

        if (flame.IsBlowing)
        {
            GUI.color = new Color(1f, 0.85f, 0.3f);
            GUILayout.Label($"吹火中　{flame.BlowProgress01 * 100f:F0}%" +
                            $"　　-{Flame.BlowFuelPerSecond:F1}/s", Bold());
            GUI.color = Color.white;
            Bar(flame.BlowProgress01, new Color(1f, 0.85f, 0.3f), null);
        }

        Space();

        // ── 事件日志 ──
        Dim($"事件（最近 {maxEvents} 条）");
        for (int i = events.Count - 1; i >= 0; i--)
        {
            Dim(events[i]);
        }

        Space();
        consoleLog = GUILayout.Toggle(consoleLog, " 同时打进 Console");
        Dim("F1 开关面板");
    }

    // ── 画图小工具 ──────────────────────────────────────────

    /// <summary>一条进度长条。ticks 是要画在上面的阈值位置（0..1）。</summary>
    private void Bar(float fill01, Color color, float[] ticks)
    {
        Rect r = GUILayoutUtility.GetRect(1, 10, GUILayout.ExpandWidth(true));
        r.width -= 4;

        GUI.color = new Color(1f, 1f, 1f, 0.13f);
        GUI.DrawTexture(r, pixel);

        GUI.color = color;
        GUI.DrawTexture(new Rect(r.x, r.y, r.width * Mathf.Clamp01(fill01), r.height), pixel);

        if (ticks != null)
        {
            GUI.color = new Color(1f, 1f, 1f, 0.65f);
            foreach (float t in ticks)
            {
                if (t <= 0f || t >= 1f) continue;
                GUI.DrawTexture(new Rect(r.x + r.width * t, r.y - 1, 1, r.height + 2), pixel);
            }
        }

        GUI.color = Color.white;
    }

    private static void Space() => GUILayout.Space(6);

    private static void Dim(string s)
    {
        GUI.color = new Color(1f, 1f, 1f, 0.62f);
        GUILayout.Label(s);
        GUI.color = Color.white;
    }

    private static GUIStyle bold;
    private static GUIStyle Bold()
    {
        if (bold == null)
        {
            bold = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
        }
        return bold;
    }

    private static Color StateColor(Flame.FlameState s) => s switch
    {
        Flame.FlameState.Burning      => new Color(1f, 0.62f, 0.15f),
        Flame.FlameState.Ember        => new Color(1f, 0.40f, 0.15f),
        Flame.FlameState.Dying        => new Color(1f, 0.25f, 0.20f),
        _                             => new Color(0.55f, 0.55f, 0.58f),
    };

    private static Color FuelColor(float fuel)
    {
        if (fuel < Flame.DyingFuel) return new Color(1f, 0.25f, 0.20f);
        if (fuel < Flame.EmberFuel) return new Color(1f, 0.55f, 0.15f);
        return new Color(0.55f, 0.75f, 0.35f);
    }

    private static Color OxygenColor(float oxygen)
    {
        if (oxygen < Flame.DyingOxygen) return new Color(1f, 0.25f, 0.20f);
        if (oxygen < Flame.EmberOxygen) return new Color(1f, 0.55f, 0.15f);
        return new Color(0.35f, 0.65f, 0.95f);
    }
}
