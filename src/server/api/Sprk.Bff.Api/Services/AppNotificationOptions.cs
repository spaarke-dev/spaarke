namespace Sprk.Bff.Api.Services;

/// <summary>
/// The ONE set of <c>appnotification</c> option-set values every writer in the BFF may send. Dataverse rejects any other
/// value, so a writer that sends one fails the first time it fires. Verified against the live table metadata; the checked-in
/// snapshot <c>tests/unit/Sprk.Bff.Api.Tests/Services/Fixtures/appnotification-options.snapshot.json</c> is compared with
/// these constants by a unit test, so a drift in either direction fails the build.
/// </summary>
/// <remarks>
/// <c>priority</c>: Normal 200000000, High 200000001 (there is no Critical, and 100000000 / 300000000 do not exist).
/// <c>toasttype</c>: Timed 200000000, Hidden 200000001. <c>icontype</c>: 100000000..100000005.
/// </remarks>
public static class AppNotificationOptions
{
    /// <summary>Values of the <c>priority</c> column.</summary>
    public static class Priority
    {
        public const int Normal = 200_000_000;
        public const int High = 200_000_001;
    }

    /// <summary>Values of the <c>toasttype</c> column.</summary>
    public static class ToastType
    {
        public const int Timed = 200_000_000;
        public const int Hidden = 200_000_001;
    }

    /// <summary>Values of the <c>icontype</c> column.</summary>
    public static class IconType
    {
        public const int Info = 100_000_000;
        public const int Success = 100_000_001;
        public const int Failure = 100_000_002;
        public const int Warning = 100_000_003;
        public const int Mention = 100_000_004;
        public const int Custom = 100_000_005;
    }

    public static bool IsValidPriority(int value) => value is Priority.Normal or Priority.High;

    public static bool IsValidToastType(int value) => value is ToastType.Timed or ToastType.Hidden;

    /// <summary>Returns null when both values are accepted by Dataverse, otherwise a message naming the bad value and the accepted set.</summary>
    public static string? Validate(int priority, int toastType)
    {
        if (!IsValidPriority(priority))
            return $"priority {priority} is not an appnotification option (accepted: {Priority.Normal} Normal, {Priority.High} High)";
        if (!IsValidToastType(toastType))
            return $"toastType {toastType} is not an appnotification option (accepted: {ToastType.Timed} Timed, {ToastType.Hidden} Hidden)";
        return null;
    }
}
