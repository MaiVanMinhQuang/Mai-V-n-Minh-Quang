using ToeicExam.Debugging;

namespace ToeicExam;

public class ToeicExamConsts
{
    public const string LocalizationSourceName = "ToeicExam";

    public const string ConnectionStringName = "Default";

    public const bool MultiTenancyEnabled = true;


    /// <summary>
    /// Default pass phrase for SimpleStringCipher decrypt/encrypt operations
    /// </summary>
    public static readonly string DefaultPassPhrase =
        DebugHelper.IsDebug ? "gsKxGZ012HLL3MI5" : "e31166a806b04b5ab8544384290a9649";
}
