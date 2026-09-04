using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// ⚠️ 临时脚手架，阶段 3 会被角色控制器取代，届时整个文件删掉。
///
/// 阶段 1 还没有玩家角色，但吹火需要一个"按住"的输入源才能验收。
/// 这个组件就是那个输入源，除了读键盘什么都不做。
///
/// 它故意做成独立文件而不是塞进 FlameRunner：
/// 临时脚手架要能被干净地删掉，混进正式代码里就删不干净了。
///
/// 项目的 Active Input Handling 是 Input System Package (New)，
/// 老的 Input.GetKey 调用会直接抛异常，所以这里用 UnityEngine.InputSystem。
/// </summary>
public class TempBlowInput : MonoBehaviour
{
    private void Update()
    {
        // 没有键盘（比如手柄-only 的设备）时 Keyboard.current 是 null
        Keyboard keyboard = Keyboard.current;
        bool held = keyboard != null && keyboard.spaceKey.isPressed;

        Flame.Instance.SetBlowInput(held);
    }

    private void OnDisable()
    {
        // 组件被关掉时别把"按住"的状态留在火那边
        Flame.Instance.SetBlowInput(false);
    }
}
