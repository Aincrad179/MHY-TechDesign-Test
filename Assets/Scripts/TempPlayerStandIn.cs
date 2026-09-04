using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;   // KeyControl

/// <summary>
/// ⚠️ 临时脚手架，阶段 3 接入真正的角色控制器时删掉整个文件。
///
/// 【它为什么必须存在】
/// 阶段 2（镜头）排在阶段 3（角色）前面，可是"标准第三人称"没有一个会动的玩家
/// 就验收不了，"火脱手跑出画面"也演不出来。这个组件就是那个会动的东西——
/// 除了读键盘和挪 Transform，它什么都不做。
///
/// 它故意不做的事（阶段 3 会正经做，现在多写一行就是多删一行）：
///   - 不用 CharacterController、不做重力、不做碰撞
///   - 不做持火/空手两套状态，只做"把手上的火把丢下"这一半
///   - 不做拾取
///
/// 【按键都是临时绑定】
/// WASD 移动 ｜ G 放下火把 ｜ C 开关过场机位。
/// C 只是给阶段 2 验收过场镜头用的开关。过场在阶段 7 由关卡事件触发——
/// 而且**不需要任何代码**：UnityEvent 直接拖 `CM_LockOnFlame` 的
/// `GameObject.SetActive`，这个文件删掉之后过场照样能切。
///
/// 场景装配：挂在一个胶囊白模上，localScale 保持 1
/// （火把是作为子物体挂上来的，父节点缩放会把它一起缩）。
/// 胶囊的 Collider **留着没关系**：Cinemachine Deoccluder 是按 Layer 排除玩家的，
/// 把玩家放进 Player 层、Deoccluder 的 Collide Against 不勾 Player 即可。
/// 阶段 3 玩家要挂 CharacterController，那时 Collider 必然存在，靠 Layer 才是长久解法。
/// </summary>
public class TempPlayerStandIn : MonoBehaviour
{
    [Header("移动")]
    // 固定速度、无奔跑（取舍日志 2026-09-02：奔跑把移动速度和两套计时器隐式绑定，
    // 玩家读不出因果）。阶段 3 沿用这个数
    [SerializeField] private float moveSpeed = 3.5f;

    [Header("引用")]
    // 移动方向相对镜头算，所以这里要指 **Main Camera 的 Transform**（Brain 驱动的那台真相机），
    // 不是任何一台 vcam——vcam 只是"目标机位"，真正在动的是 Main Camera
    [SerializeField] private Transform cameraTransform;
    [SerializeField] private Transform carried;      // 开局挂在手上的火把，G 键放下

    /// <summary>
    /// 过场机位（`CM_LockOnFlame` 那个物体），**场景里默认是关着的**。
    ///
    /// 【为什么切镜头是"开关一个物体"，而不是调用什么 API】
    /// Cinemachine 里 priority 最高的那台 vcam 是 live 的。
    /// 常态机位 priority 低、一直开着，当兜底；过场机位 priority 高、默认关着，
    /// 需要时打开就自动接管，关掉就自动退回——中间的推镜由 Brain 的 Blend 负责。
    /// 于是"切镜头"这件事根本不需要一个切换器类：`GameObject.SetActive` 就是全部，
    /// 而它正好是 UnityEvent 在 Inspector 里能直接挂的方法。
    /// 将来工业关要加"拉远看全厂"的机位，也是同样的加法，一行代码都不用改。
    ///
    /// 这里存 GameObject 而不是 CinemachineCamera：这个脚手架因此完全不依赖
    /// Cinemachine，删掉它不会牵动任何东西。
    /// </summary>
    [SerializeField] private GameObject cutsceneShot;

    /// <summary>
    /// 锁光标。第三人称转镜头时鼠标不该跑出窗口。
    ///
    /// Cinemachine 的 Input Axis Controller 读的是鼠标位移，它不管光标锁不锁——
    /// 所以这件事没人做，得有人做。编辑器里按 Esc 会自动解锁，调试不受影响。
    /// 阶段 3 这行搬进正式的角色控制器。
    /// </summary>
    [SerializeField] private bool lockCursor = true;

    private void OnEnable()
    {
        if (!lockCursor) return;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible   = false;
    }

    private void OnDisable()
    {
        // 退出 Play 时别把锁着的光标留给编辑器
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible   = true;
    }

    private void Update()
    {
        Keyboard kb = Keyboard.current;
        if (kb == null) return;   // 手柄 only 的设备上 Keyboard.current 是 null

        if (kb.gKey.wasPressedThisFrame) Drop();
        if (kb.cKey.wasPressedThisFrame) ToggleCutsceneShot();

        // 吹火时无法移动（初稿）。阶段 1.4 就把 IsBlowing 暴露出来了，这里第一次用上；
        // 阶段 3 的角色控制器接的是同一个开关，不会有第二套锁移动的逻辑
        if (Flame.Instance.IsBlowing) return;

        Move(kb);
    }

    private void Move(Keyboard kb)
    {
        Vector2 input = new Vector2(Axis(kb.dKey, kb.aKey), Axis(kb.wKey, kb.sKey));
        if (input.sqrMagnitude < 1e-4f) return;

        // 方向相对镜头算，但要先把镜头的俯角压掉——
        // 否则低头看地时"往前走"会变成往地里钻
        Vector3 forward = Vector3.forward;
        Vector3 right   = Vector3.right;

        if (cameraTransform != null)
        {
            forward = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up).normalized;
            right   = Vector3.ProjectOnPlane(cameraTransform.right,   Vector3.up).normalized;
        }

        Vector3 dir = forward * input.y + right * input.x;

        // 镜头几乎垂直向下时 forward 压平后会退化成零向量，此时 LookRotation 会报错。
        // Orbital Follow 的 Vertical Axis range 本来挡住了这种情况，
        // 但那是 Inspector 里随手就能改的一个数，代码不该赌它没被人调过
        if (dir.sqrMagnitude < 1e-4f) return;

        dir.Normalize();
        transform.position += dir * (moveSpeed * Time.deltaTime);
        transform.rotation  = Quaternion.LookRotation(dir, Vector3.up);
    }

    private static float Axis(KeyControl positive, KeyControl negative)
    {
        return (positive.isPressed ? 1f : 0f) - (negative.isPressed ? 1f : 0f);
    }

    /// <summary>
    /// 把手上的火把丢在原地。只放不捡——拾取是阶段 3 的正经功能，脚手架不实现。
    /// 保持世界位置地解除父子关系即可：火把本来就是个独立载体，
    /// 挂在玩家身上只是"拿着"的临时表达。
    /// </summary>
    private void Drop()
    {
        if (carried == null) return;

        carried.SetParent(null, true);
        carried = null;
    }

    /// <summary>
    /// 开／关过场机位。读的是物体自己的当前状态，所以不会和别处不同步——
    /// 脚手架不需要自己记着"现在切到哪台了"。
    /// </summary>
    private void ToggleCutsceneShot()
    {
        if (cutsceneShot == null) return;

        cutsceneShot.SetActive(!cutsceneShot.activeSelf);
    }

    private void OnValidate()
    {
        if (cameraTransform == null)
        {
            Debug.LogWarning($"[{name}] 没指定 cameraTransform，WASD 会按世界坐标轴走，转镜头也不会改变前进方向。", this);
        }

        // 指到 vcam 上是很容易犯的错：vcam 只是"目标机位"，它自己不一定朝着玩家看的方向，
        // 而且过场切走之后它还停在原地。要指真正在动的那台 Main Camera
        if (cameraTransform != null && cameraTransform.GetComponent<Camera>() == null)
        {
            Debug.LogWarning(
                $"[{name}] cameraTransform 指的不是一台真相机（可能指到 vcam 上了）。" +
                "这里要的是 Brain 驱动的 Main Camera 的 Transform。", this);
        }

        // 过场机位在场景里应该默认关着，否则一开局就是过场
        if (cutsceneShot != null && cutsceneShot.activeSelf)
        {
            Debug.LogWarning(
                $"[{name}] cutsceneShot（{cutsceneShot.name}）在场景里是开着的，" +
                "它 priority 更高，会一进 Play 就抢走镜头。请在 Inspector 里把它关掉。", cutsceneShot);
        }
    }
}
