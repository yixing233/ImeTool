namespace ImeTool.Ime;

public enum ImeOpenStatus
{
    Unknown = 0,
    Closed = 1,
    Open = 2
}

public static class ImeOpenStatusExtensions
{
    public static bool? ToNullableBool(this ImeOpenStatus status) => status switch
    {
        ImeOpenStatus.Open => true,
        ImeOpenStatus.Closed => false,
        _ => null
    };

    public static ImeOpenStatus FromBool(bool isOpen) => isOpen ? ImeOpenStatus.Open : ImeOpenStatus.Closed;
}
