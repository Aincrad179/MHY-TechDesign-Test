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

    // 阈值全部公开为 const：它们是只读的事实，视觉层要用来算大小，
    // 调试面板要用来画刻度线。公开常量不会让任何人改到状态
    public const float EmberFuel     = 10f;
    public const float EmberOxygen   = 6f;
    public const float DyingFuel     = 5f;
    public const float DyingOxygen   = 2f;
    public const float DyingDuration = 8f;

    // 吹火：按住多久算成功，以及按住期间每秒烧掉多少额外燃料
    public const float BlowDuration      = 1.5f;
    public const float BlowFuelPerSecond = 2f;    // 吹满一次共 3 点，够疼但不必死

    /// <summary>
    /// 状态迁移和区域进出要不要也打进 Console。
    /// 默认关——屏幕上的调试面板已经在显示这些，Console 只会刷屏。
    /// 需要看历史记录（比如复现一个偶发问题）时在面板上勾回来。
    /// </summary>
    public static bool ConsoleLog = false;

    public FlameState State { get; private set; } = FlameState.Extinguished;

    private Carrier currentCarrier;
    private float dyingTimer;

    // ── 吹火 ────────────────────────────────────────────────
    private bool  blowHeld;       // 这一帧输入端有没有按住
    private float blowProgress;

    /// <summary>正在吹火。阶段 3 的角色控制器读它决定能不能移动。</summary>
    public bool IsBlowing { get; private set; }

    /// <summary>吹火进度 0..1，给视觉层做提示用。</summary>
    public float BlowProgress01 => BlowDuration > 0f ? Mathf.Clamp01(blowProgress / BlowDuration) : 0f;

    /// <summary>
    /// 输入端每帧告诉火"吹火键按住了没"。
    ///
    /// Flame 不认识按键，输入端不认识状态机——阶段 1 是个临时脚本在调，
    /// 阶段 3 换成角色控制器调，这个方法不用改。
    /// </summary>
    public void SetBlowInput(bool held) => blowHeld = held;

    /// <summary>火熄灭那一刻的位置。熄灭后载体引用被放开，视觉层还要知道"尸体"在哪。</summary>
    private Vector3 lastKnownPosition;

    /// <summary>
    /// 火有没有寄居处。唯一为 false 的情况是已熄灭——
    /// 这个 null 窗口在 Flame 内部消化掉，镜头和视觉层都不该再判一次。
    /// </summary>
    public bool HasCarrier => currentCarrier != null;

    /// <summary>当前载体，只读。镜头、调试面板要知道火寄居在谁身上。</summary>
    public Carrier CurrentCarrier => currentCarrier;

    /// <summary>濒熄倒计时剩余秒数。不在 Dying 态时无意义。</summary>
    public float DyingTimer => dyingTimer;

    /// <summary>最近一次状态迁移的成因文字。调试面板显示用。</summary>
    public string LastReason { get; private set; } = "";

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
        blowHeld       = false;
        blowProgress   = 0f;
        IsBlowing      = false;
        State          = FlameState.Burning;

        ChangeCarrierTo(first);
        LastReason = "关卡开场点火";
        if (ConsoleLog) Debug.Log($"[Flame] 点火 → {first.name}　｜　fuel={Fuel:F1} oxygen={Oxygen:F1}");
    }

    public void Update(float deltaTime)
    {
        if (State == FlameState.Extinguished) return;

        // 顺序要紧：吹火要先扣掉这一帧的燃料，再让状态机看当前值。
        // 这样"吹到燃料归零"就落进 CheckIfChangeState 的硬规则里，
        // 不需要在吹火逻辑里再写一次熄灭判定
        UpdateBlow(deltaTime);

        // 燃料由载体自己在 Update 里扣，Flame 只判状态
        CheckIfChangeState(Fuel, Oxygen, deltaTime);
    }

    /// <summary>
    /// 吹火：按住键，烧额外的燃料，把火从濒熄拉回来。
    ///
    /// 【为什么按秒扣而不是成功时一次性扣】
    /// 1. 中途松手也已经烧掉了，符合"Fuel 不可逆"——放弃是要付钱的
    /// 2. "吹到燃料归零直接熄灭"这条自动成立，不用写特判
    /// 3. 玩家看得见：吹的时候木枝在肉眼可见地变短，代价即时可读
    ///
    /// dyingTimer 不暂停，所以这是场赛跑：得在倒计时烧完前吹满。
    /// 这不是新规则，是两个已有计时器相撞的结果。
    /// </summary>
    private void UpdateBlow(float deltaTime)
    {
        // 只有濒熄的火才谈得上抢救
        if (!blowHeld || State != FlameState.Dying)
        {
            IsBlowing    = false;
            blowProgress = 0f;
            return;
        }

        IsBlowing = true;

        // 走载体已有的入口。ConsumeFuel 收的是绝对量，当初就是为这类一次性消耗留的
        currentCarrier.ConsumeFuel(BlowFuelPerSecond * deltaTime);

        blowProgress += deltaTime;
        if (blowProgress < BlowDuration) return;

        blowProgress = 0f;
        IsBlowing    = false;

        // 初稿：吹火 → Burning。至于燃料还是不够，紧接着的 CheckIfChangeState
        // 会照常把它打回 Ember——那是对的，Ember 是稳定态，玩家已经脱离倒计时了。
        // 吹火买的是"不再倒计时"，不是"回到满血"
        SetState(FlameState.Burning, "吹火成功");
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
        LastReason = reason;

        if (ConsoleLog) Debug.Log($"[Flame] {prev} → {next}　｜　{reason}　｜　fuel={Fuel:F1} oxygen={Oxygen:F1}");
    }
}
