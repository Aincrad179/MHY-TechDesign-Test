using UnityEngine;

/// <summary>
/// 火的视觉层。**只读**：每帧问 Flame 拿状态和读数，翻译成看得见的东西。
///
/// 这条边界必须守住——一旦这里出现影响判定的代码，架构就漏了。
/// 自测方法：把整个组件禁用掉，游戏逻辑必须照常跑通，只是玩家看不见火。
///
/// 它不挂在任何载体上，自己是一个独立物体：火在设定上就不属于任何东西，
/// 每帧把自己的位置对齐到 Flame.VisualPosition 即可。
///
/// 期望的层级（在 Inspector 里拖引用）：
///   FlameVisual        ← 本脚本
///    ├─ Body           ← Sphere 之类的白模，**删掉它的 Collider**
///    └─ Glow           ← Point Light
/// </summary>
public class FlameVisual : MonoBehaviour
{
    [Header("被驱动的东西（都可以留空）")]
    [SerializeField] private Transform body;   // 火苗本体，只改 localScale
    [SerializeField] private Light glow;       // 火光。零 HUD 的项目里，光就是 UI

    [Header("尺寸")]
    [SerializeField] private float minScale = 0.12f;   // 快烧完时
    [SerializeField] private float maxScale = 0.45f;   // 余量充足时

    [Header("火光")]
    [SerializeField] private float minRange = 1.5f;
    [SerializeField] private float maxRange = 8f;
    [SerializeField] private float maxIntensity = 3f;

    private bool wasAlive;

    /// <summary>
    /// 放在 LateUpdate：FlameRunner 也在 LateUpdate 里推 Flame，两者顺序不保证，
    /// 所以 State 可能慢一帧。视觉慢一帧看不出来，逻辑慢一帧才要命——
    /// 位置则不受影响，VisualPosition 直接取载体的 Transform，永远是当前帧的。
    /// 真到了火苗跟不上快速移动的火把那天，去 Script Execution Order 里把
    /// FlameRunner 排在本脚本前面，不要在这里加补偿。
    /// </summary>
    private void LateUpdate()
    {
        Flame flame = Flame.Instance;
        bool alive = flame.State != Flame.FlameState.Extinguished;

        // 只在状态翻转的那一帧开关，不每帧调 SetActive
        if (alive != wasAlive)
        {
            SetVisible(alive);
            wasAlive = alive;
        }

        if (!alive) return;

        // 火不属于任何物体，所以是视觉去贴位置，而不是被谁挂着
        transform.position = flame.VisualPosition;

        // 【为什么用绝对 fuel 而不是比值】
        // 初稿的阈值是绝对值（fuel < 10 掉 Ember）。尺寸若跟比值走，
        // 一个容量 200 的篝火烧到剩 15 会显得奄奄一息，状态机却说它是 Burning——
        // 视觉和状态机打架。跟绝对值走，则"开始变小"和"开始警戒"是同一刻，
        // 而且换载体不用调参。10 以上看不出区别，那段信息由火把长度负责。
        float headroom = Mathf.Clamp01(flame.Fuel / Flame.EmberFuel);

        if (body != null)
        {
            body.localScale = Vector3.one * Mathf.Lerp(minScale, maxScale, headroom);
        }

        if (glow != null)
        {
            glow.range = Mathf.Lerp(minRange, maxRange, headroom);
            glow.intensity = Mathf.Lerp(0.4f, maxIntensity, headroom);
        }
    }

    private void SetVisible(bool visible)
    {
        if (body != null) body.gameObject.SetActive(visible);
        if (glow != null) glow.enabled = visible;
    }

    private void Awake()
    {
        // 开场 Flame 还是 Extinguished，先藏起来，等 FlameRunner 点火
        wasAlive = false;
        SetVisible(false);
    }

    private void OnValidate()
    {
        if (body == null && glow == null)
        {
            Debug.LogWarning($"[{name}] body 和 glow 都没指定，这个组件什么都不会做。", this);
        }
    }
}
