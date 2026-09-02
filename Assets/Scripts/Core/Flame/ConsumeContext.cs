namespace ProjectEmber.Core
{
    public readonly struct ConsumeContext
    {
        public bool IsPlayerSprinting { get; }
        public float Oxygen { get; }

        public ConsumeContext(bool isPlayerSprinting, float oxygen)
        {
            IsPlayerSprinting = isPlayerSprinting;
            Oxygen = oxygen;
        }
    }
}