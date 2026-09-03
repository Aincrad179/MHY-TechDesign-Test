using UnityEngine;

public class Flame
{
    public enum FlameState
    {
        Burning = 0,
        Ember = 1,
        Dying = 2,
        Extinguished = 3
    }

    public static Flame Instance { get; } = new Flame();

    private Flame() { }   // 禁止外部 new，唯一性在编译期就成立

    public  const float EmberFuel = 10f;   // Carrier.OnValidate 要读它做容量校验
    private const float EmberOxygen = 6f;
    private const float DyingFuel = 5f;
    private const float DyingOxygen = 2f;
    private const float DyingDuration = 8f;

    public FlameState State { get; private set; } = FlameState.Extinguished;

    private Carrier currentCarrier;
    private float dyingTimer;

    /// <summary>火熄灭那一刻的位置。熄灭后载体引用被放开，视觉层还要知道"尸体"在哪。</summary>
    private Vector3 lastKnownPosition;

    /// <summary>
    /// 火有没有寄居处。唯一为 false 的情况是已熄灭——
    /// 这个 null 窗口在 Flame 内部消化掉，镜头和视觉层都不该再判一次。
    /// </summary>
    public bool HasCarrier => currentCarrier != null;

    public Vector3 VisualPosition => currentCarrier != null ? currentCarrier.Position : lastKnownPosition;
    public float Fuel => currentCarrier != null ? currentCarrier.Fuel : 0f;

    /// <summary>
    /// 氧气不归火，也不归载体——它归空间。载体只是"我现在站在哪个区域"的转告人。
    /// 和 Fuel 一样，值不在 Flame 手里，Flame 只读。
    /// 兜底在 Carrier.Oxygen 里做掉了，这里永远取得到值。
    /// </summary>
    public float Oxygen => currentCarrier != null ? currentCarrier.Oxygen : 0f;



    public void ChangeCarrierTo(Carrier nextCarrier)
    {
        currentCarrier?.OnDetach();
        currentCarrier = nextCarrier;
        currentCarrier.OnAttach();
    }

    /// <summary>
    /// 关卡开场点火。FlameRunner 在 Start 里调一次，是火从"没有"到"有"的唯一入口。
    /// 顺手复位：关掉域重载时静态单例会跨 Play 残留，在这里洗干净。
    /// </summary>
    public void Ignite(Carrier first)
    {
        currentCarrier = null;   // 上一局的残留，不能拿去 OnDetach 一个已销毁的物体
        dyingTimer     = 0f;
        State          = FlameState.Burning;

        ChangeCarrierTo(first);
        Debug.Log($"[Flame] 点火 → {first.name}　｜　fuel={Fuel:F1} oxygen={Oxygen:F1}");
    }

    public void Update(float deltaTime)
    {
        if (State == FlameState.Extinguished) return;

        // 燃料由载体自己在 Update 里扣，Flame 只判状态
        CheckIfChangeState(Fuel, Oxygen, deltaTime);
    }

    private void CheckIfChangeState(float fuel, float oxygen, float deltaTime)
    {
        // 硬规则：任何一个归 0，立即熄灭，不走倒计时
        if (fuel <= 0f || oxygen <= 0f)
        {
            Extinguish(fuel <= 0f ? "燃料归零" : "氧气归零");
            return;
        }

        switch (State)
        {
            case FlameState.Burning:
                if (fuel < EmberFuel || oxygen < EmberOxygen)
                {
                    SetState(FlameState.Ember, Cause(fuel < EmberFuel, oxygen < EmberOxygen));
                }
                break;

            case FlameState.Ember:
                // 降级判定必须排在恢复判定前面
                if (fuel < DyingFuel || oxygen < DyingOxygen)
                {
                    SetState(FlameState.Dying, Cause(fuel < DyingFuel, oxygen < DyingOxygen));
                }
                else if (fuel >= EmberFuel && oxygen >= EmberOxygen)
                {
                    SetState(FlameState.Burning, "两者均回到阈值以上");
                }
                break;

            case FlameState.Dying:
                dyingTimer -= deltaTime;
                if (dyingTimer <= 0f)
                {
                    Extinguish($"濒熄倒计时走完 {DyingDuration}s");
                }
                break;
        }
    }

    /// <summary>
    /// 熄灭的**唯一**出口。
    ///
    /// 原先硬熄灭和倒计时熄灭各写各的，倒计时那条漏了 OnDetach——
    /// 结果火灭了载体还留着 isLit，继续在自己的 Update 里烧燃料。
    /// 同一件事有两条路径，就该合并成一个出口，而不是给漏掉的那条补一行。
    /// </summary>
    private void Extinguish(string reason)
    {
        // 先记位置再放手：放手之后就问不到了，而视觉层还要在原地收尾
        if (currentCarrier != null)
        {
            lastKnownPosition = currentCarrier.Position;
        }

        SetState(FlameState.Extinguished, reason);

        currentCarrier?.OnDetach();
        currentCarrier = null;
    }

    /// <summary>
    /// 两个变量都可能把火按下去，日志必须说清是哪一个——
    /// 否则调参时看着一行"→ Ember"根本不知道该改火把长度还是该改区域氧气。
    /// </summary>
    private static string Cause(bool byFuel, bool byOxygen)
    {
        if (byFuel && byOxygen) return "燃料与氧气同时不足";
        return byFuel ? "燃料不足" : "氧气不足";
    }

    private void SetState(FlameState next, string reason)
    {
        if (State == next) return;

        FlameState prev = State;
        State = next;

        // 倒计时只在这一处重置，任何进入 Dying 的路径都覆盖得到
        if (next == FlameState.Dying)
        {
            dyingTimer = DyingDuration;
        }

        // 读数一起打出来：出问题时不用回放，一行就能复现当时的两个输入
        Debug.Log($"[Flame] {prev} → {next}　｜　{reason}　｜　fuel={Fuel:F1} oxygen={Oxygen:F1}");
    }
}
