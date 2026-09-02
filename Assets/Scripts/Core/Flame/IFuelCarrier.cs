using UnityEngine;

namespace ProjectEmber.Core
{
    public interface IFuelCarrier
    {
        float GetFuelRatio();//返回剩余燃料比例，范围通常为 0–1

        Vector3 GetPosition();//告诉火焰自己目前在哪里

        void ConsumeFuel(float deltaTime, ConsumeContext context);//载体自行消耗自己的燃料

        void OnAttach();//火焰刚刚寄居到该载体

        void OnDetach();//火焰离开该载体
    }
}