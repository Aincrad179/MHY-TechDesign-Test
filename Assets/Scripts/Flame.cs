using UnityEngine;

public class Flame
{
    public enum FlameState
    {
        Burning = 0,
        Ember = 1,
        Dying = 2,
        Extinguished = 3
    }

    public static Flame Instance { get; } = new Flame();

    private Flame() { }   // 禁止外部 new，唯一性在编译期就成立

    public  const float EmberFuel = 10f;   // Carrier.OnValidate 要读它做容量校验
    private const float EmberOxygen = 6f;
    private const float DyingFuel = 5f;
    private const float DyingOxygen = 2f;
    private const float DyingDuration = 8f;

    public FlameState State { get; private set; } = FlameState.Burning;

    private Carrier currentCarrier;
    private float dyingTimer;

    public Vector3 VisualPosition => currentCarrier.Position;
    public float Fuel => currentCarrier.Fuel;

    // 阶段 4 接入 OxygenZone，现在固定为露天
    public float Oxygen => 10f;
    

    public void ChangeCarrierTo(Carrier nextCarrier)
    {
        currentCarrier?.OnDetach();
        currentCarrier = nextCarrier;
        currentCarrier.OnAttach();
    }

    public void Update(float deltaTime)
    {
        if (State == FlameState.Extinguished) return;

        // 燃料由载体自己在 Update 里扣，Flame 只判状态
        CheckIfChangeState(Fuel, Oxygen, deltaTime);
    }

    private void CheckIfChangeState(float fuel, float oxygen, float deltaTime)
    {
        // 硬规则：任何一个归 0，立即熄灭，不走倒计时
        if (fuel <= 0f || oxygen <= 0f)
        {
            SetState(FlameState.Extinguished);
            currentCarrier.OnDetach();
            currentCarrier = null;
            return;
        }

        switch (State)
        {
            case FlameState.Burning:
                if (fuel < EmberFuel || oxygen < EmberOxygen)
                {
                    SetState(FlameState.Ember);
                }
                break;

            case FlameState.Ember:
                // 降级判定必须排在恢复判定前面
                if (fuel < DyingFuel || oxygen < DyingOxygen)
                {
                    SetState(FlameState.Dying);
                }
                else if (fuel >= EmberFuel && oxygen >= EmberOxygen)
                {
                    SetState(FlameState.Burning);
                }
                break;

            case FlameState.Dying:
                dyingTimer -= deltaTime;
                if (dyingTimer <= 0f)
                {
                    SetState(FlameState.Extinguished);
                }
                break;
        }
    }

    private void SetState(FlameState next)
    {
        if (State == next) return;

        State = next;

        // 倒计时只在这一处重置，任何进入 Dying 的路径都覆盖得到
        if (next == FlameState.Dying)
        {
            dyingTimer = DyingDuration;
        }

        Debug.Log($"[Flame] → {next}");
    }
}
