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
///    ├─ Stick      ← 只有 MeshRenderer，会被本脚本缩放
///    └─ FlamePosition   ← 空物体，位置由本脚本每帧算出来写进去
///
/// 【Collider 和 Rigidbody 必须留在根节点，不能挪到 Stick 上】
/// 1. Stick 的 localScale 每帧被改，而碰撞体形状是按缩放烘出来的——
///    挂上去等于每帧重建一次物理形状，白给开销，还可能抖动、漏 trigger 事件。
/// 2. trigger 会跟着燃料一起缩小，于是凭空长出一条"燃料越少越难传火/越难被
///    区域检测到"的隐性规则。没人设计过它，而且方向和公理 I 相反。
/// 根节点承载"火把作为一个整体"的属性，Stick 只承载可视化——
/// 正因为 Stick 上没有任何逻辑，才能放心地每帧缩放它。
///
/// 【FlamePosition 为什么不挂在 Stick 底下】
/// 挂进去能靠继承缩放自动跟着降，代价是"降多少"取决于它的 localPosition.y
/// 与杆体网格顶面的比例——手填错一倍，火苗就以两倍速度往下掉，而且不报错。
/// 这个魔数还必须和网格类型手工同步（Cube 顶面在 0.5，Cylinder 在 1.0）。
/// 现在改成代码从网格包围盒算出杆顶、直接写世界坐标，挂点不再吃缩放，
/// 顺带避免了阶段 5 把粒子特效挂上去以后被杆体的缩放压扁。
/// </summary>
public class Torch : Carrier
{
    [Header("视觉")]
    [SerializeField] private Transform stick;            // 会随燃料变短的杆体
    [SerializeField] private Transform flamePosition;    // 挂点，位置由本脚本写入；留空也能跑

    // 烧到最后也留一小截，免得缩成零厚度的一条线
    [SerializeField, Range(0f, 1f)] private float minLengthRatio = 0.25f;

    // 火苗核心比杆顶再高一点会更像在"烧"。世界单位，不随杆体缩放
    [SerializeField] private float flameLift = 0.05f;

    [Header("传火")]
    [SerializeField] private bool igniteOnContact = true;   // 碰到别的载体就把火交出去

    private Vector3 stickBaseScale;
    private Vector3 stickBaseLocalPos;

    // 杆体网格自身的顶面/底面（局部坐标，未乘缩放），Awake 时从 mesh 的包围盒读。
    // 用 max/min 而不是 extents：包围盒中心不在原点的美术模型也能对
    private float stickTopLocalY    =  0.5f;
    private float stickBottomLocalY = -0.5f;

    // ApplyLength 每帧算好存在这里。Position 只是读，不重复算
    private Vector3 flameWorldPos;

    /// <summary>火在顶端烧，不在握把上。</summary>
    public override Vector3 Position => flameWorldPos;

    protected override void Awake()
    {
        base.Awake();   // Fuel = fuelCapacity

        if (stick != null)
        {
            stickBaseScale    = stick.localScale;
            stickBaseLocalPos = stick.localPosition;

            // 从网格包围盒读顶面和底面，这样杆体换成 Cube / Cylinder / 美术模型都不用改代码。
            // Cube 顶面在 0.5，Cylinder 在 1.0——正是手填挂点会错的那一倍
            var filter = stick.GetComponent<MeshFilter>();
            if (filter != null && filter.sharedMesh != null)
            {
                Bounds b = filter.sharedMesh.bounds;
                stickTopLocalY    = b.max.y;
                stickBottomLocalY = b.min.y;
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
    /// 把剩余燃料翻译成杆体长度，并把火焰挂点摆到杆顶。
    /// 只动 Transform，不碰任何逻辑值——删掉这个方法游戏照常跑，
    /// 只是玩家看不出还剩多少、火苗回到握把上。
    /// （假设杆体相对父节点没有旋转，局部 Y 就是长度方向。）
    /// </summary>
    private void ApplyLength()
    {
        if (stick == null)
        {
            // 没杆体就退回物体原点，别让 Position 返回一个没初始化的零向量
            flameWorldPos = base.Position;
            return;
        }

        float ratio  = fuelCapacity > 0f ? Mathf.Clamp01(Fuel / fuelCapacity) : 0f;
        float scaleY = Mathf.Lerp(minLengthRatio, 1f, ratio);

        stick.localScale = new Vector3(
            stickBaseScale.x,
            stickBaseScale.y * scaleY,
            stickBaseScale.z);

        // 缩放绕自身中心，不补偿的话握把会往上飘。
        // 整体往下挪，让底端钉在原地、只有顶端往下退。
        float shrink = -stickBottomLocalY * stickBaseScale.y * (1f - scaleY);
        stick.localPosition = stickBaseLocalPos - Vector3.up * shrink;

        // 杆顶：网格顶面那个点，交给 TransformPoint 换算成世界坐标——
        // 缩放和旋转都被它带上了，不需要自己乘。
        // 必须排在上面两行**之后**：它读的是这一帧刚写好的 scale 和 position
        flameWorldPos = stick.TransformPoint(new Vector3(0f, stickTopLocalY, 0f))
                        + stick.up * flameLift;

        // 挂点物体只是这个坐标的可视化 + 阶段 5 挂粒子用的锚。
        // 它不参与计算，删掉照样跑
        if (flamePosition != null)
        {
            flamePosition.position = flameWorldPos;
        }
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
            Debug.LogWarning($"[{name}] 没指定 stick，火把不会随燃料变短，火焰挂点也算不出来。", this);
        }

        // flamePosition 留空是允许的：位置由代码算，挂点只是可视化和粒子锚点

        if (flamePosition != null && flamePosition.parent == stick)
        {
            Debug.LogWarning(
                $"[{name}] flamePosition 挂在 stick 底下，会跟着杆体一起被缩放。" +
                "位置现在由代码写，挂到 Torch 根节点下即可。", this);
        }
    }

    /// <summary>在编辑器里挂上组件时给一组火把该有的默认值。</summary>
    private void Reset()
    {
        fuelCapacity = 30f;   // 约 30 秒，够走完段落一
        burnRate     = 1f;
    }

    /// <summary>
    /// 把代码算出来的三个点画在 Scene 视图里：杆体底端、杆顶、火焰挂点。
    ///
    /// 存在的理由：杆顶是从网格包围盒算出来的，肉眼看不见这个中间结果，
    /// 一旦和视觉上的杆顶不重合，就只能靠手算去查（已经查过一次了）。
    /// 画出来之后"代码认为的顶端"和"你看到的顶端"是否重合，一眼就知道。
    ///
    /// 编辑器专用，不参与任何判定。Gizmo 自己重新读网格，
    /// 所以没进 Play 模式（Awake 没跑过）也画得对。
    /// </summary>
    private void OnDrawGizmos()
    {
        if (stick == null) return;

        float topLocalY    =  0.5f;
        float bottomLocalY = -0.5f;

        var filter = stick.GetComponent<MeshFilter>();
        if (filter != null && filter.sharedMesh != null)
        {
            Bounds b = filter.sharedMesh.bounds;
            topLocalY    = b.max.y;
            bottomLocalY = b.min.y;
        }

        Vector3 top    = stick.TransformPoint(new Vector3(0f, topLocalY, 0f));
        Vector3 bottom = stick.TransformPoint(new Vector3(0f, bottomLocalY, 0f));
        Vector3 flame  = top + stick.up * flameLift;

        // 杆体轴线：底端到顶端
        Gizmos.color = new Color(0.55f, 0.42f, 0.29f);   // 木色
        Gizmos.DrawLine(bottom, top);
        Gizmos.DrawSphere(bottom, 0.015f);

        // 代码认为的杆顶。和视觉杆顶不重合就是包围盒读错了
        Gizmos.color = Color.white;
        Gizmos.DrawSphere(top, 0.02f);

        // 火焰挂点。它和杆顶之间那一小段就是 flameLift
        Gizmos.color = new Color(1f, 0.48f, 0.1f);
        Gizmos.DrawSphere(flame, 0.03f);
        if (flameLift != 0f)
        {
            Gizmos.DrawLine(top, flame);
        }
    }
}
