using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 载体基类：持有燃料，被点燃时自己烧自己。
/// 火把、油灯等具体载体继承它，只覆写有差异的部分。
/// </summary>
public abstract class Carrier : MonoBehaviour
{
    // ── 三个量 ──────────────────────────────────────────────
    [SerializeField] protected float fuelCapacity = 30f;   // 上限：满燃料是多少
    [SerializeField] protected float burnRate     = 1f;    // 速度：每秒烧掉多少

    /// <summary>
    /// 现状：现在还剩多少。
    ///
    /// 写成属性而不是公开字段，为的是两件事：
    ///   1. 它是**运行时值**，不该被序列化进场景。字段版本会被存盘，
    ///      Inspector 里显示成上次存的数（通常是 0），看着像 bug。
    ///   2. `protected set` 把"只有载体自己能改自己的 fuel"这条规则
    ///      从注释升级成编译器约束——Flame 想写也写不进来。
    ///      留 protected 而非 private，是给后面的 PipeNetwork 这类
    ///      需要覆写 ConsumeFuel 的子载体留门。
    /// </summary>
    public float Fuel { get; protected set; }

    /// <summary>火是否正寄居在自己身上。没点燃的载体不该自燃。</summary>
    protected bool isLit;

    /// <summary>火应该待的位置。</summary>
    public virtual Vector3 Position => transform.position;

    // ── 我在哪个区域 ────────────────────────────────────────
    // 由 OxygenZone 在 trigger 进出时写进来（见 OxygenZone 里"为什么是区域检测载体"）。
    //
    // 注意这里不判 isLit：区域归属是载体的客观属性，跟点没点燃无关。
    // 好处是火交接过来的那一帧，新载体的区域已经是现成的正确值，
    // 不需要写任何交接时的同步代码——和 Fuel 一样，数据放对地方就不用搬。
    //
    // 【这个列表故意允许重复】
    // 一个载体可能挂着多个碰撞体（根节点的 trigger + 杆体的 collider），
    // 进同一个区域会收到多次 Enter。若在 Enter 时去重，
    // 那么其中一个碰撞体先离开就会把整条记录删掉，而另一个还在区域里——
    // 火会莫名其妙地"恢复"。
    // 允许重复则列表天然成了个计数器：进几次记几笔，出几次删几笔，
    // 全部出完才真正离开。而"后进入者优先"照旧成立，不需要额外代码。
    private readonly List<OxygenZone> overlappingZones = new List<OxygenZone>();

    /// <summary>
    /// 当前所处区域的氧气值。**后进入的区域优先**——
    /// 走进大房间里的小密室，取小密室；退出来自动回到大房间。
    /// 一个区域都没覆盖到时用 <see cref="OxygenZone.DefaultOxygen"/> 兜底，
    /// 保证任何位置都取得到值，Flame 那边不需要判空。
    /// </summary>
    public float Oxygen
    {
        get
        {
            // 倒着找第一个还活着的：区域可能被销毁，Unity 的伪 null 在这里会被跳过
            for (int i = overlappingZones.Count - 1; i >= 0; i--)
            {
                if (overlappingZones[i] != null)
                {
                    return overlappingZones[i].Oxygen;
                }
            }

            return OxygenZone.DefaultOxygen;
        }
    }

    /// <summary>满燃料是多少。调试面板和视觉层要拿它算比例。</summary>
    public float FuelCapacity => fuelCapacity;

    /// <summary>每秒烧多少。</summary>
    public float BurnRate => burnRate;

    /// <summary>火正寄居在自己身上。</summary>
    public bool IsLit => isLit;

    /// <summary>
    /// 说了算的那个区域——也就是最后进入的、还活着的那个。没有则为 null（用兜底值）。
    /// </summary>
    public OxygenZone CurrentZone
    {
        get
        {
            for (int i = overlappingZones.Count - 1; i >= 0; i--)
            {
                if (overlappingZones[i] != null) return overlappingZones[i];
            }
            return null;
        }
    }

    /// <summary>
    /// 当前重叠着的所有区域，后进入的排在后面。只读——外面改不了这个栈。
    /// 调试面板要显示整摞，因为"退出小区域该回到大区域"这类问题
    /// 只看最终 oxygen 值是看不出来的。
    /// </summary>
    public IReadOnlyList<OxygenZone> OverlappingZones => overlappingZones;

    public void EnterZone(OxygenZone zone)
    {
        bool isNew = !overlappingZones.Contains(zone);
        overlappingZones.Add(zone);   // 不去重，见上面的注释

        // 只在第一次真正进入时打日志，第二个碰撞体带来的那笔不刷屏
        if (isNew)
        {
            if (Flame.ConsoleLog) Debug.Log($"[Zone] {name} 进入 {zone.name}　｜　oxygen {Oxygen:F1}");
        }
    }

    public void ExitZone(OxygenZone zone)
    {
        if (overlappingZones.Remove(zone) && !overlappingZones.Contains(zone))
        {
            if (Flame.ConsoleLog) Debug.Log($"[Zone] {name} 离开 {zone.name}　｜　oxygen 回到 {Oxygen:F1}");
        }
    }

    protected virtual void Awake()
    {
        Fuel = fuelCapacity;
    }

    protected virtual void Update()
    {
        // 没被点燃的载体不烧燃料——场上其他火把不会自己烧光消失
        if (!isLit) return;

        // 每秒烧 burnRate，这一帧过了 deltaTime 秒 → 这一帧烧 burnRate * deltaTime
        ConsumeFuel(burnRate * Time.deltaTime);
    }

    /// <summary>
    /// 扣掉指定量的燃料。参数是绝对量，不是 deltaTime——
    /// 所以「每帧燃烧」和「吹火额外消耗」能共用这一个入口。
    /// </summary>
    public virtual void ConsumeFuel(float amount)
    {
        // 只负责钳到 0，不在这里销毁自己：
        // 此刻 Flame 还持有 currentCarrier 引用，销毁会让它下一帧读到悬空引用。
        // 销毁统一放在 OnDetach——那时 Flame 已经放手了。
        Fuel = Mathf.Max(0f, Fuel - amount);
    }

    /// <summary>火刚寄居上来。</summary>
    public virtual void OnAttach()
    {
        isLit = true;
    }

    /// <summary>火离开了。可能是烧尽，也可能是交接给下一个载体。</summary>
    public virtual void OnDetach()
    {
        isLit = false;

        // 烧尽了才消失。只是交接给下一个载体时 Fuel 还有剩，载体留在场上
        if (Fuel <= 0f)
        {
            Destroy(gameObject);
        }
    }

    protected virtual void OnValidate()
    {
        // 阈值是绝对值，所以容量必须高过余烬线，否则这个载体一出生就是余烬
        if (fuelCapacity <= Flame.EmberFuel)
        {
            Debug.LogWarning(
                $"[{name}] fuelCapacity={fuelCapacity} 不高于余烬阈值 {Flame.EmberFuel}，" +
                "点燃后立刻就是 Ember 状态。", this);
        }
    }
}
