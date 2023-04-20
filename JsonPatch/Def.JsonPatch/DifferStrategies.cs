using System.Collections;
using System.Reflection;

namespace Def.JsonPatch
{
    public class DifferStrategies
    {
        public Func<PropertyInfo, bool>? Skip { get; set; }

        public Func<PropertyInfo, object, object?> GetValue { get; set; } = (prop, obj) =>
        {
            return prop.GetValue(obj);
        };
        public Func<object, IEnumerable<PropertyInfo>> GetProperties { get; set; } = (obj) =>
        {
            return obj.GetType().GetProperties();
        };

        public Func<object, object, bool> AreSame { get; set; } = (x, y) =>
        {
            throw new NotImplementedException($"AreSame Strategy not set");
        };

        public Func<Type, object> Create { get; set; } = (type) =>
        {
            var obj = Activator.CreateInstance(type);
            Guards.InternalErrorIfNull(obj);
            return obj;
        };

        public Func<PropertyInfo, object, object?, bool> SetValue { get; set; } = (pi, obj, newValue) =>
        {
            pi.SetValue(obj, newValue);
            return true;
        };

        public Func<object?, object?, bool> AreEquals { get; set; } = (x, y) =>
        {
            if (x == null && y == null)
                return true;
            if (x == null || y == null)
                return false;
            if (x.GetType() != y.GetType())
                return false;
            if (x is string xs && y is string ys) // or y is string
                return xs == ys;
            if (x is IStructuralEquatable ex)
                return ex.Equals(y, StructuralComparisons.StructuralEqualityComparer);
            return Equals(x, y);
        };

        public Func<object, PropertyInfo, PropertyInfo> GetSameProperty { get; set; } = (obj, prop) =>
        {
            var res = obj.GetType().GetProperty(prop.Name);
            Guards.InternalErrorIfNull(res, $"property {prop.Name} not found in object of type {obj.GetType()}");
            return res;
        };
    }
}