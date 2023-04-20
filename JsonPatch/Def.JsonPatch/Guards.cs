using System.Diagnostics.CodeAnalysis;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace Def.JsonPatch
{
    public static class Guards
    {
        [DebuggerHidden]
        public static void InternalErrorIfNullOrEmpty([NotNull] string? value, string? error = null,
            [CallerFilePath] string callerFilePath = "",
            [CallerLineNumber] long callerLineNumber = 0,
            [CallerMemberName] string callerMember = "")
        {
            error ??= $"Unexpected null or empty string in method {callerMember} in {callerFilePath} line {callerLineNumber}";
            InternalErrorIfFalse(!string.IsNullOrEmpty(value), error);
        }

        [DebuggerHidden]
        public static void InternalErrorIfNull<T>([NotNull] T? obj, string? error = null,
            [CallerFilePath] string callerFilePath = "",
            [CallerLineNumber] long callerLineNumber = 0,
            [CallerMemberName] string callerMember = "") where T : class
        {
            error ??= $"Unexpected null value in method {callerMember} in {callerFilePath} line {callerLineNumber}";
            InternalErrorIfTrue(obj == default(T), error, callerFilePath, callerLineNumber, callerMember);
        }

        [DebuggerHidden]
        public static void InternalErrorIfNotNull<T>(T? obj, string? error = null,
            [CallerFilePath] string callerFilePath = "",
            [CallerLineNumber] long callerLineNumber = 0,
            [CallerMemberName] string callerMember = "") where T : class
        {
            error ??= $"Unexpected not null value in method {callerMember} in {callerFilePath} line {callerLineNumber}";
            InternalErrorIfFalse(obj == null, error);
        }

        [DebuggerHidden]
        public static void InternalErrorIfFalse([DoesNotReturnIf(false)] bool condition, string? error = null,
            [CallerFilePath] string callerFilePath = "",
            [CallerLineNumber] long callerLineNumber = 0,
            [CallerMemberName] string callerMember = "")
        {
            error ??= $"Unexpected false condition in method {callerMember} in {callerFilePath} line {callerLineNumber}";
            if (!condition)
                throw new InvalidOperationException(error);
        }

        [DebuggerHidden]
        public static void InternalErrorIfTrue([DoesNotReturnIf(true)] bool condition, string? error = null,
            [CallerFilePath] string callerFilePath = "",
            [CallerLineNumber] long callerLineNumber = 0,
            [CallerMemberName] string callerMember = "")
        {
            error ??= $"Unexpected true condition in method {callerMember} in {callerFilePath} line {callerLineNumber}";
            if (condition)
                throw new InvalidOperationException(error);
        }

        [DebuggerHidden]
        [DoesNotReturn]
        public static void InternalError(string error,
           [CallerFilePath] string callerFilePath = "",
           [CallerLineNumber] long callerLineNumber = 0,
           [CallerMemberName] string callerMember = "")

        {
            error ??= $"Unexpected error in method {callerMember} in {callerFilePath} line {callerLineNumber}";
            throw new InvalidOperationException(error);
        }

        [DebuggerHidden]
        public static void ArgumentNotNull<T>([NotNull] T? obj, string? fieldName = null, string? error = null) where T : class
        {
            ArgumentPassCondition(obj != null, error ?? $"Object of type {typeof(T)} is null", fieldName);
        }

        [DebuggerHidden]
        public static void ArgumentPassCondition([DoesNotReturnIf(false)] bool condition, string? error, string? fieldName = null)
        {
            if (!condition)
            {
                throw new ArgumentException(error, fieldName);
            }
        }

        [DebuggerHidden]
        public static void ArgumentNotNullOrEmpty([NotNull] string? obj, string? fieldName = null, string? error = null)
        {
            ArgumentPassCondition(!string.IsNullOrEmpty(obj), error, fieldName);
        }

        [DebuggerHidden]
        public static void ArgumentLength(string text, int? minLength, int maxLength, string fieldName)
        {
            if (!minLength.HasValue && text == null)
                return;
            if (minLength.HasValue && (text == null || text.Length < minLength.Value))
                throw new ArgumentException($"{fieldName} must be at least {minLength} in length.");
            if (text != null && text.Length > maxLength)
                throw new ArgumentException($"{fieldName} can't be larger than {maxLength} in length.");
        }

        [DebuggerHidden]
        public static T ArgumentEnum<T>(string text, string fieldName) where T : struct
        {
            if (!string.IsNullOrEmpty(text))
            {
                if (Enum.TryParse(text, true, out T result))
                {
                    if (Enum.IsDefined(typeof(T), result))
                    {
                        return result;
                    }
                }
            }
            throw new ArgumentException($"{fieldName} has an invalid value.");
        }

        [DebuggerHidden]
        public static void ArgumentEnum<T>(T value, string fieldName) where T : struct
        {
            if (!Enum.IsDefined(typeof(T), value))
                throw new ArgumentException($"{fieldName} has an invalid value.");
        }

        [DebuggerHidden]
        public static void ArgumentEnumOrNull<T>(T? value, string fieldName) where T : struct
        {
            if (value == null)
                return;
            if (!Enum.IsDefined(typeof(T), value.Value))
                throw new ArgumentException($"{fieldName} has an invalid value.");
        }

        [DebuggerHidden]
        [DoesNotReturn]
        public static void InternalErrorIfNotImplemented(string error)
        {
            throw new InvalidOperationException($"Failed as not-implemented: {error}");
        }

    }
}
