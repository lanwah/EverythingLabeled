namespace EverythingLabeled.Cli;

public static class ExitCodes
{
    public const int Ok = 0;
    public const int Usage = 1;
    public const int ConfigOrConnection = 2;
    public const int Http4xx = 3;
    public const int Http5xx = 4;
}
