using UnityEngine;

/// <summary>
/// 火把 / 干木枝：石器关的主载体。
///
/// 相对基类只多做两件事，其余全部继承：
///   1. 火焰挂点在杆体顶端，不在物体原点
///   2. 杆体随剩余燃料变短——"木枝的长度就是时间"，这是第一关唯一的燃料 UI
///
/// 期望的层级（在 Inspector 里拖引用）：
///   Torch          ← 本脚本 + IsTrigger 的 Collider + Rigidbody(可勾 Is Kinematic)
///    └─ Stick      ← MeshRenderer，会被本脚本缩放
///        └─ FlameAnchor   ← 空物体，摆在杆体顶端
///   FlameAnchor 挂在 Stick 底下，杆体一缩短它自动跟着降，不用单独算位置。
/// </summary>
public class Torch : Carrier
{
    [Header("视觉")]
    [SerializeField] private Transform stick;          // 会随燃料变短的杆体
    [SerializeField] private Transform flameAnchor;    // 火该待的位置（杆体顶端）

    // 烧到最后也留一小截，免得缩成零厚度的一条线
    [SerializeField, Range(0f, 1f)] private float minLengthRatio = 0.25f;

    [Header("传火")]
    [SerializeField] private bool igniteOnContact = true;   // 碰到别的载体就把火交出去

    private Vector3 stickBaseScale;
    private Vector3 stickBaseLocalPos;
    private float   stickHalfHeight = 0.5f;   // 杆体网格自身的半高，Awake 时从 mesh 读

    /// <summary>火在顶端烧，不在握把上。</summary>
    public override Vector3 Position =>
        flameAnchor != null ? flameAnchor.position : base.Position;

    protected override void Awake()
    {
        base.Awake();   // Fuel = fuelCapacity

        if (stick != null)
        {
            stickBaseScale    = stick.localScale;
            stickBaseLocalPos = stick.localPosition;

            // 从网格包围盒读半高，这样杆体换成 Cube / Cylinder / 美术模型都不用改代码
            var filter = stick.GetComponent<MeshFilter>();
            if (filter != null && filter.sharedMesh != null)
            {
                stickHalfHeight = filter.sharedMesh.bounds.extents.y;
            }
        }

        ApplyLength();   // 出生就按当前燃料摆好，不等第一帧
    }

    protected override void Update()
    {
        base.Update();   // 点燃状态下扣燃料
        ApplyLength();
    }

    /// <summary>
    /// 把剩余燃料翻译成杆体长度。只动 Transform，不碰任何逻辑值——
    /// 删掉这个方法游戏照常跑，只是玩家看不出还剩多少。
    /// （假设杆体相对父节点没有旋转，局部 Y 就是长度方向。）
    /// </summary>
    private void ApplyLength()
    {
        if (stick == null || fuelCapacity <= 0f) return;

        float ratio  = Mathf.Clamp01(Fuel / fuelCapacity);
        float scaleY = Mathf.Lerp(minLengthRatio, 1f, ratio);

        stick.localScale = new Vector3(
            stickBaseScale.x,
            stickBaseScale.y * scaleY,
            stickBaseScale.z);

        // 缩放绕自身中心，不补偿的话握把会往上飘。
        // 整体往下挪，让底端钉在原地、只有顶端往下退。
        float shrink = stickHalfHeight * stickBaseScale.y * (1f - scaleY);
        stick.localPosition = stickBaseLocalPos - Vector3.up * shrink;
    }

    /// <summary>碰到另一个载体 → 把火交出去。这是场景里唯一的传火入口。</summary>
    private void OnTriggerEnter(Collider other)
    {
        if (!igniteOnContact || !isLit) return;

        var next = other.GetComponentInParent<Carrier>();
        if (next == null || next == this) return;

        // 交接完自己就熄了：全场只有一团火。
        // 木枝没烧完就不会被销毁（基类 OnDetach 判 Fuel），掉在地上还能再点。
        Flame.Instance.ChangeCarrierTo(next);
    }

    protected override void OnValidate()
    {
        base.OnValidate();

        if (stick == null)
        {
            Debug.LogWarning($"[{name}] 没指定 stick，火把不会随燃料变短。", this);
        }

        if (flameAnchor == null)
        {
            Debug.LogWarning($"[{name}] 没指定 flameAnchor，火会长在握把上而不是顶端。", this);
        }
    }

    /// <summary>在编辑器里挂上组件时给一组火把该有的默认值。</summary>
    private void Reset()
    {
        fuelCapacity = 30f;   // 约 30 秒，够走完段落一
        burnRate     = 1f;
    }
}
