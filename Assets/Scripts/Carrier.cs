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

    public float Fuel;                                     // 现状：现在还剩多少

    /// <summary>火是否正寄居在自己身上。没点燃的载体不该自燃。</summary>
    protected bool isLit;

    /// <summary>火应该待的位置。</summary>
    public virtual Vector3 Position => transform.position;

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
