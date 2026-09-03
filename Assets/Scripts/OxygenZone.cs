using UnityEngine;

/// <summary>
/// 氧气区域：一个空间常量 + 一个范围。
///
/// 公理 II 说"氧气即空间"，那么在代码里它就是区域上的一个常量——
/// 不插值、不衰减、进出即切换。玩家走进密闭空间火就闷、走出来就恢复，
/// 全自动，不需要写任何"进出房间"的玩法事件。
///
/// 【为什么是区域检测载体，而不是载体检测区域】
/// 1. Torch 已经有自己的 OnTriggerEnter（传火用）。若在 Carrier 基类里
///    再写一个，Unity 只会调子类那个，基类的检测会静默失效。
/// 2. 火交接到新载体时，新载体早就待在房间里了，不会再触发一次 Enter。
///    让每个载体各自记着自己在哪，交接时值天然就是对的。
///
/// 场景装配：本物体挂 Collider 并勾 Is Trigger；载体侧需要 Collider + Rigidbody
/// （Torch 已经有了，因为传火也靠 trigger）。
/// </summary>
[RequireComponent(typeof(Collider))]
public class OxygenZone : MonoBehaviour
{
    /// <summary>
    /// 没有任何区域覆盖时的兜底值 = 露天。
    ///
    /// 兜底放在代码里而不是"场景最外层放一个大 zone"：场景物体会被误删、
    /// 会忘了放，代码常量不会。哪一关的"外面"不是露天（太空关外面是真空），
    /// 那一关自己套一个大 zone 盖掉即可。
    /// </summary>
    public const float DefaultOxygen = 10f;

    [SerializeField] private float oxygen = DefaultOxygen;

    /// <summary>只读。初稿：oxygen 值保存在区域中，只能被读取。</summary>
    public float Oxygen => oxygen;

    private void OnTriggerEnter(Collider other)
    {
        var carrier = other.GetComponentInParent<Carrier>();
        if (carrier != null)
        {
            carrier.EnterZone(this);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        var carrier = other.GetComponentInParent<Carrier>();
        if (carrier != null)
        {
            carrier.ExitZone(this);
        }
    }

    private void OnValidate()
    {
        var col = GetComponent<Collider>();
        if (col != null && !col.isTrigger)
        {
            Debug.LogWarning($"[{name}] Collider 没勾 Is Trigger，这个区域检测不到任何载体。", this);
        }

        if (oxygen < 0f)
        {
            oxygen = 0f;
        }
    }

    /// <summary>在 Scene 视图里把区域范围画出来，方便摆位。颜色越暗＝氧气越少。</summary>
    private void OnDrawGizmos()
    {
        var col = GetComponent<Collider>();
        if (col == null) return;

        float t = Mathf.Clamp01(oxygen / DefaultOxygen);
        Gizmos.color = new Color(0.3f, 0.6f, 1f, Mathf.Lerp(0.05f, 0.25f, t));
        Gizmos.DrawCube(col.bounds.center, col.bounds.size);
    }
}
