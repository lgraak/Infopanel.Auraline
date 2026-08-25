namespace Auraline.Contracts;

public readonly record struct ContractVersion(int Major, int Minor)
{
    public static ContractVersion Current { get; } = new(1, 0);

    public bool IsCompatibleWith(ContractVersion other) => Major == other.Major;

    public override string ToString() => $"{Major}.{Minor}";
}
