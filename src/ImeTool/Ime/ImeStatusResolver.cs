namespace ImeTool.Ime;

public static class ImeStatusResolver
{
    public static ImeOpenStatus FirstKnown(params Func<ImeOpenStatus>[] readers)
    {
        foreach (Func<ImeOpenStatus> reader in readers)
        {
            ImeOpenStatus status = reader();
            if (status != ImeOpenStatus.Unknown)
            {
                return status;
            }
        }

        return ImeOpenStatus.Unknown;
    }

    public static bool FirstSuccessful(params Func<bool>[] writers)
    {
        foreach (Func<bool> writer in writers)
        {
            if (writer())
            {
                return true;
            }
        }

        return false;
    }
}
