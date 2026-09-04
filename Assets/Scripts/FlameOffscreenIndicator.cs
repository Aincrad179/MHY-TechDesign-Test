using UnityEngine;

/// <summary>
/// 火种跑出画面时，在屏幕边缘画一个指向它的箭头。
///
/// 【它为什么存在：这是降级方案的另一半】
/// 原方案里镜头始终把火框在画面内（加权中点 + FOV 自适应），所以根本不需要指示器。
/// 降级成标准第三人称之后，火脱手就会真的跑出画面——"火在哪"这条信息突然没人负责了。
/// 这个箭头是来补那个洞的，它是降级的代价，不是新功能。
/// 详见取舍日志 2026-09-04 条里被否决的方案。
///
/// 【和"零 HUD"的冲突怎么处理】
/// 项目原则是零 HUD：所有信息由火焰本身承担（简报 3.5）。这个箭头是唯一的例外，
/// 所以把例外压到最小：
///   - 火在画面里时它**完全不存在**——不是变淡，是一个像素都不画
///   - 只给方向。不给距离、不给数字、不给边框、不给文字
///   - 颜色和闪烁全部从 Flame.State / DyingTimer 派生，**不引入任何新状态**。
///     "还剩多久"由闪烁频率说，不由一个数字说
///
/// 【它是只读层，和 FlameVisual / FlameDebugHUD 同级】
/// 每帧只读 Flame 和 Camera，不写回、不注册回调、不让 Flame 为它加事件。
/// 自测方法：禁用本组件，游戏行为必须一模一样，只是火跑出画面后你不知道它在哪。
///
/// 【为什么用 IMGUI 而不是 Canvas】
/// 它要画的东西是"一个会转的三角形"。IMGUI 版本不需要 Canvas、不需要图片资产、
/// 不需要预制体，纹理是代码生成的——整个功能就是这一个文件，
/// 交付时想撤掉这个例外，删一个文件即可。正式 UI（如果以后真的需要）另做，
/// 不要在这个基础上长。
///
/// 场景装配：挂在 Main Camera 上（或任意常驻物体，留空则找 Camera.main）。
///
/// 【它对镜头方案完全不敏感——这是只读层边界对的证据】
/// 它只要一台 <see cref="Camera"/>。镜头从手写的 GameCamera 换成 Cinemachine，
/// 整个换法里这个文件**一个字都没改**：Brain 驱动的还是那台 Main Camera，
/// `Camera.main` 照样找得到。换镜头方案时不用碰的代码，才算真的解耦了。
/// </summary>
public class FlameOffscreenIndicator : MonoBehaviour
{
    [Header("引用（留空则用 Camera.main）")]
    [SerializeField] private Camera cam;

    [Header("外观")]
    [SerializeField] private float margin = 46f;   // 箭头中心离屏幕边多少像素
    [SerializeField] private float size   = 34f;   // 箭头边长（像素）

    // 代码生成的三角形，不吃任何资产
    private Texture2D arrow;

    private void Awake()
    {
        arrow = BuildArrow(64);
    }

    private void OnDestroy()
    {
        if (arrow != null) Destroy(arrow);
    }

    private void OnGUI()
    {
        // OnGUI 一帧会被调用多次（Layout / Repaint / 各种输入事件）。
        // 这里只画不布局，所以只在重绘那次动手，否则同一个箭头一帧要画好几遍
        if (Event.current.type != EventType.Repaint) return;

        Flame flame = Flame.Instance;

        // 火没了就没什么可指的。熄灭后 VisualPosition 仍然有值（最后已知位置），
        // 但那是给视觉层收尾用的，不该继续拿箭头指着一具尸体
        if (flame.State == Flame.FlameState.Extinguished) return;

        Camera c = ResolveCamera();
        if (c == null) return;

        Vector3 sp     = c.WorldToScreenPoint(flame.VisualPosition);
        Vector2 center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        Vector2 p      = new Vector2(sp.x, sp.y);

        // 【WorldToScreenPoint 在相机背后给出的是镜像结果】
        // z<0 时它照样返回一组看起来很正常的 x/y，但那是投影到镜头**后方**的虚像，
        // 直接拿来算方向，箭头会稳定地指反。绕屏幕中心镜像回来即可。
        // 这是整段里唯一一处不看注释就会写错、而且错了还不报错的地方
        bool behind = sp.z < 0f;
        if (behind) p = center - (p - center);

        Vector2 d     = p - center;
        float   halfW = Screen.width  * 0.5f - margin;
        float   halfH = Screen.height * 0.5f - margin;

        // 在框内**且**在镜头前方 → 玩家自己看得见火，指示器根本不该出现。
        // 这一行就是"零 HUD 的例外压到最小"的全部实现
        if (!behind && Mathf.Abs(d.x) <= halfW && Mathf.Abs(d.y) <= halfH) return;

        // 火正好在镜头的正后方正中：没有方向可指，画什么都是瞎指
        if (d.sqrMagnitude < 1e-4f) return;

        // 把方向射线推到内缩矩形的边界上：先分别算出撞左右边和撞上下边各要放大多少，
        // 取小的那个——那条边才是先撞上的
        float sx = Mathf.Abs(d.x) > 1e-4f ? halfW / Mathf.Abs(d.x) : float.MaxValue;
        float sy = Mathf.Abs(d.y) > 1e-4f ? halfH / Mathf.Abs(d.y) : float.MaxValue;
        Vector2 edge = center + d * Mathf.Min(sx, sy);

        // 屏幕坐标 y 向上，GUI 坐标 y 向下
        Vector2 gui = new Vector2(edge.x, Screen.height - edge.y);

        // 纹理里的箭头指向正上方。GUI 的正角度是顺时针，
        // 而"从正上方顺时针偏多少能对准 d"正好是 atan2(d.x, d.y)
        float angle = Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg;

        Matrix4x4 savedMatrix = GUI.matrix;
        Color     savedColor  = GUI.color;

        GUIUtility.RotateAroundPivot(angle, gui);
        GUI.color = ArrowColor(flame);
        GUI.DrawTexture(new Rect(gui.x - size * 0.5f, gui.y - size * 0.5f, size, size), arrow);

        GUI.color  = savedColor;
        GUI.matrix = savedMatrix;
    }

    /// <summary>
    /// 箭头颜色完全由火的状态派生，本组件自己不记任何状态。
    /// 颜色取的是规范表里保留给火焰的橙红（材质颜色.md：红橙黄白留给火，环境不得大量使用），
    /// 所以这个箭头在白模场景里天然不会和别的东西混淆。
    /// </summary>
    private static Color ArrowColor(Flame flame)
    {
        switch (flame.State)
        {
            case Flame.FlameState.Burning:
                return new Color(1f, 0.48f, 0.10f, 0.95f);

            case Flame.FlameState.Ember:
                return new Color(0.95f, 0.30f, 0.08f, 0.85f);

            case Flame.FlameState.Dying:
                // 闪烁频率跟着倒计时走：越接近归零闪得越急。
                // 用的是 Flame 早就暴露出来的 DyingTimer，没有为指示器新增任何状态——
                // 于是"还剩多久"这条信息不需要一个数字来显示，这才配叫零 HUD
                float urgency = 1f - Mathf.Clamp01(flame.DyingTimer / Flame.DyingDuration);
                float hz      = Mathf.Lerp(2f, 8f, urgency);
                float alpha   = Mathf.Lerp(0.25f, 0.9f,
                                    0.5f + 0.5f * Mathf.Sin(Time.time * hz * Mathf.PI * 2f));
                return new Color(0.85f, 0.18f, 0.05f, alpha);
        }

        return Color.clear;
    }

    /// <summary>
    /// Camera.main 每次调用都要按 tag 找一遍，所以找到就存下来。
    /// 留空能用是故意的：这个组件应该拖上去就工作，装配步骤越少越不会漏。
    /// </summary>
    private Camera ResolveCamera()
    {
        if (cam == null) cam = Camera.main;
        return cam;
    }

    /// <summary>
    /// 生成一个指向正上方的实心三角形。
    ///
    /// Texture2D 的 y=0 是**底**行，而 GUI.DrawTexture 会把纹理正着画出来，
    /// 所以顶点放在 y = n-1（纹理上边），底边放在 y = 0。
    /// 边界上按覆盖度给 alpha 当作一像素的抗锯齿，否则高分屏上是明显的锯齿。
    /// </summary>
    private static Texture2D BuildArrow(int n)
    {
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode   = TextureWrapMode.Clamp,
            hideFlags  = HideFlags.HideAndDontSave
        };

        var pixels = new Color32[n * n];
        float cx = (n - 1) * 0.5f;

        for (int y = 0; y < n; y++)
        {
            float halfWidth = (1f - y / (float)(n - 1)) * cx;

            for (int x = 0; x < n; x++)
            {
                float coverage = Mathf.Clamp01(halfWidth - Mathf.Abs(x - cx) + 0.5f);
                pixels[y * n + x] = new Color32(255, 255, 255, (byte)(coverage * 255f));
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply();
        return tex;
    }

    private void OnValidate()
    {
        margin = Mathf.Max(0f, margin);
        size   = Mathf.Max(4f, size);
    }
}
