namespace ProjectEmber.Core
{
    public readonly struct ConsumeContext
    {
        public float Oxygen { get; }

        public ConsumeContext(float oxygen)
        {
            Oxygen = oxygen;
        }
    }
}
