using System.Collections.Concurrent;
using System.Collections;
using System.Linq.Expressions;
using System.Reflection;

namespace Def.JsonPatch
{
    public static class ReflectionEx
    {
        static readonly ConcurrentDictionary<Type, PropertyInfo[]> propertyInfoCache = new ConcurrentDictionary<Type, PropertyInfo[]>();
        static readonly ConcurrentDictionary<Type, FieldInfo[]> fieldInfoCache = new ConcurrentDictionary<Type, FieldInfo[]>();

        public static bool HasLessThanOrEqual(this Type t)
        {
            var op = t.GetUnderlineNonNullableType().GetMethod("op_LessThanOrEqual");
            return op != null && op.IsSpecialName;
        }
        public static bool HasGreaterThanOrEqual(this Type t)
        {
            var op = t.GetUnderlineNonNullableType().GetMethod("op_GreaterThanOrEqual");
            return op != null && op.IsSpecialName;
        }

        public static Type GetUnderlineNonNullableType(this Type t)
        {
            return Nullable.GetUnderlyingType(t) ?? t;
        }

        public static bool IsAssociativeDictionary(this PropertyInfo p)
        {
            return IsAssociativeDictionary(p.PropertyType);
        }
        public static bool IsCollection(this PropertyInfo p)
        {
            return IsCollection(p.PropertyType);
        }
        public static bool IsCollection(this Type t)
        {
            return t.IsGenericType && typeof(ICollection<>).IsAssignableFrom(t.GetGenericTypeDefinition());
        }
        public static bool IsEnumerable(this PropertyInfo p)
        {
            return IsEnumerable(p.PropertyType);
        }
        public static bool IsEnumerable(this Type t)
        {
            return typeof(IEnumerable).IsAssignableFrom(t);
        }
        public static bool IsAssociativeDictionary(this Type t)
        {
            return t == typeof(IDictionary<string, object>);
        }

        public static Type GetItemType(object collection)
        {
            var type = collection.GetType();
            return GetItemType(type);
        }

        public static Type GetItemType(this Type type)
        {
            if (type.IsArray)
            {
                var res = type.GetElementType();
                Guards.InternalErrorIfNull(res);
                return res;
            }
            Guards.InternalErrorIfFalse(type.IsGenericType && typeof(IEnumerable).IsAssignableFrom(type), $"{type} is not enumerable");
            return type.GetGenericArguments()[0];
        }

        public static bool IsSimpleTypeOrString(this PropertyInfo prop)
        {
            return prop.PropertyType.IsValueType || prop.IsString();
        }

        public static bool IsArrayOfSimpleTypeOrString(this PropertyInfo prop)
        {
            return prop.PropertyType.IsArrayOfSimpleTypeOrString();
        }

        public static bool IsArrayOfSimpleTypeOrString(this Type type)
        {
            if (!type.IsArray)
                return false;

            var elementType = type.GetElementType();
            Guards.InternalErrorIfNull(elementType);

            return elementType.IsSimpleTypeOrString();
        }

        public static bool IsEnumerableOfSimpleTypeOrString(this PropertyInfo prop)
        {
            if (!prop.IsEnumerable())
                return false;

            var elementType = prop.PropertyType;
            Guards.InternalErrorIfNull(elementType);

            return elementType.IsSimpleTypeOrString();
        }

        public static bool IsString(this PropertyInfo prop)
        {
            return prop.PropertyType == typeof(string);
        }

        public static bool IsSimpleTypeOrString(this Type type)
        {
            return type.IsValueType || type.IsString();
        }
        public static bool IsString(this Type type)
        {
            return type == typeof(string);
        }

        public static PropertyInfo[] GetPropertiesEx(this Type type)
        {
            Guards.InternalErrorIfNull(type);

            if (propertyInfoCache.TryGetValue(type, out var res))
                return res;

            res = type.GetProperties();
            propertyInfoCache.TryAdd(type, res);

            return res;
        }

        public static FieldInfo[] GetFieldsEx(this Type type)
        {
            Guards.InternalErrorIfNull(type);

            if (fieldInfoCache.TryGetValue(type, out var res))
                return res;

            res = type.GetFields();
            fieldInfoCache.TryAdd(type, res);

            return res;
        }

        public static PropertyInfo GetPropertyEx(this Type type, string name, bool ignoreCase)
        {
            var prop = type.GetPropertiesEx().FirstOrDefault(p => string.Compare(p.Name, name, ignoreCase) == 0);
            return prop;
        }

        static readonly ConcurrentDictionary<MemberInfo, Attribute[]> attributesInfoCache = new ConcurrentDictionary<MemberInfo, Attribute[]>();
        public static Attribute[] GetCustomAttributesEx(this MemberInfo element)
        {
            Guards.InternalErrorIfNull(element);

            if (attributesInfoCache.TryGetValue(element, out var res))
                return res;

            res = Attribute.GetCustomAttributes(element, true);
            attributesInfoCache.TryAdd(element, res);

            return res;
        }

        public static IEnumerable<TAttibute> GetCustomAttributesEx<TAttibute>(this MemberInfo element)
        {
            return element.GetCustomAttributesEx().OfType<TAttibute>().Cast<TAttibute>();
        }

        public static PropertyInfo GetPropertyInfo<T, P>(Expression<Func<T, P>> propertyExpr)
        {
            var expr = propertyExpr.Body is UnaryExpression uexpr ? (MemberExpression)uexpr.Operand : (MemberExpression)propertyExpr.Body;
            return (PropertyInfo)expr.Member;
        }
    }
}
