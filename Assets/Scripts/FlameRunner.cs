using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 唯一驱动 Flame 的地方。
///
/// Flame 是逻辑单例、不是 MonoBehaviour，自己没有 Update——
/// 必须有人每帧推它一下，就是这个类。场景里放一个，只放一个。
///
/// 它同时是"火灭了之后做什么"的挂点：Flame 只负责判定状态，
/// 失败重开 / 回检查点是关卡的事，不该写进 Flame。
/// </summary>
public class FlameRunner : MonoBehaviour
{
    [SerializeField] private Carrier initialCarrier;      // 开场火寄居在哪个载体上
    [SerializeField] private UnityEvent onExtinguished;   // 火灭了 → 关卡失败，在这里接重开

    private bool extinguishReported;   // 熄灭事件只发一次

    private void Start()
    {
        // 在 Start 点火而不是 Awake：等所有载体的 Awake 跑完，Fuel 才是满的
        if (initialCarrier == null)
        {
            Debug.LogError($"[{name}] 没指定 initialCarrier，这一关不会有火。", this);
            enabled = false;
            return;
        }

        Flame.Instance.Ignite(initialCarrier);
        extinguishReported = false;
    }

    /// <summary>
    /// 放在 LateUpdate 而不是 Update：载体在自己的 Update 里扣燃料，
    /// Unity 不保证脚本之间的 Update 顺序。等这一帧所有载体都扣完再判状态，
    /// Flame 读到的就永远是当前帧的值，不会慢一帧。
    /// </summary>
    private void LateUpdate()
    {
        Flame.Instance.Update(Time.deltaTime);

        if (!extinguishReported && Flame.Instance.State == Flame.FlameState.Extinguished)
        {
            extinguishReported = true;
            onExtinguished?.Invoke();
        }
    }
}
