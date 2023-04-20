using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Def.JsonPatch
{
    public static class ConvertEx
    {
        public static T? ChangeType<T>(object? value) where T : class
        {
            return (T?)ChangeType(value, typeof(T));
        }
        public static object? ChangeType(object? value, Type targetType)
        {
            Guards.InternalErrorIfNull(targetType);

            if (value == null)
                return null;

            if (value.GetType() == targetType)
                return value;

            var underlyingType = Nullable.GetUnderlyingType(targetType);
            if (underlyingType != null)
            {
                if (value is string s && string.IsNullOrEmpty(s))
                    return null;
                targetType = underlyingType;
            }

            if (targetType.IsEnum && value.GetType().IsString())
            {
                if (Enum.TryParse(targetType, (string)value, true, out var res))
                    return res;
                throw new ArgumentException($"Unknown enum value {value} for enum {targetType.Name}");
            }

            if (targetType == typeof(DateTime) && value is string sval)
            {
                if (DateTimeOffset.TryParse(sval, CultureInfo.InvariantCulture, DateTimeStyles.None, out var res))
                {
                    //return DateTime part ignoring offset
                    return res.DateTime;
                }

                throw new ArgumentException($"Unexpected date/time value {value}");
            }

            if (targetType == typeof(DateTimeOffset) && value is string sdt)
            {
                if (DateTimeOffset.TryParse(sdt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var res))
                {
                    return res;
                }

                throw new ArgumentException($"Unexpected date/time value {value}");
            }

            if (targetType == typeof(TimeSpan) && value is string str)
            {
                if (TimeSpan.TryParse(str, CultureInfo.InvariantCulture, out var timeSpan))
                    return timeSpan;

                throw new ArgumentException($"Unexpected timespan value {value}");
            }

            return Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
        }
    }
}
