using System.Collections;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Def.JsonPatch
{

    public class Applyer
    {
        // compare current and new results, apply new results to current and return changes

        public static IEnumerable<PropertyInfo> ApplyChanges<T>(T view, Change[] incomingChanges) where T : class
        {
            var changedProperties = new List<PropertyInfo>();
            foreach (var change in incomingChanges)
            {
                try
                {
                    changedProperties.Add(ApplyChange(view, change));
                }
                catch (FormatException ex)
                {
                    throw new JsonPatchArgumentException(change, ex);
                }
                catch (OverflowException ex) 
                {
                    throw new JsonPatchArgumentException(change, ex);
                }
                catch (ArgumentException arg)
                {
                    throw new JsonPatchArgumentException(change, arg);
                }
                catch (Exception)
                {
                    // TODO: catch other exceptions whichcan be threat as invalid values in json patch 
                    throw;
                }
            }
            return changedProperties;
        }

        static void Move(IList list, int positionFrom, int positionTo)
        {
            var modelToMove = list[positionFrom];
            list.RemoveAt(positionFrom);
            list.Insert(positionTo, modelToMove);
        }

        static PropertyInfo ApplyChange(object view, Change change)
        {
            Guards.ArgumentNotNullOrEmpty(change.path, "path", "Path is empty and doesn't point to applicable entity");
            Guards.ArgumentPassCondition(change.op != Operations.copy, "Operation 'copy' is not supported");
            Guards.ArgumentPassCondition(change.op != Operations.test, "Operation 'test' is not supported");
            Guards.ArgumentPassCondition(change.op != Operations.invalid, "Operation 'invalid' is not supported");

            var (curObj, curPath, parentProp) = JsonPathEx.TravelInto(view, change.path);

            if (JsonPathEx.TryParseIndex(curPath, out var index)) // last element in path is index, try to change collection
            {
                Guards.ArgumentNotNull(parentProp, "path", $"Path {change.path} doesn't point to applicable collection");
                var coll = curObj as IList;
                Guards.ArgumentNotNull(coll, $"Index {curPath} in path {change.path} is used for non-collection object");

                if (index == -1)
                    index = coll.Count;

                Guards.ArgumentPassCondition(index >= 0, $"Negative position {curPath} is not supported");

                if (change.op == Operations.add)
                {
                    Guards.ArgumentPassCondition(index <= coll.Count, $"Position {curPath} out of range for operation 'replace'");

                    var itemType = ReflectionEx.GetItemType(coll);
                    var value = DeserializeValue(change.value, itemType);
                    Guards.ArgumentNotNull(value, "add operation should has added object in field 'value'");
                    coll.Insert(index, value);
                }
                else if (change.op == Operations.remove)
                {
                    Guards.ArgumentPassCondition(index < coll.Count, $"Position {curPath} out of range for operation 'remove'");
                    coll.RemoveAt(index);
                }
                else if (change.op == Operations.replace)
                {
                    Guards.ArgumentPassCondition(index < coll.Count, $"Position {curPath} out of range for operation 'replace'");
                    var itemType = ReflectionEx.GetItemType(coll);
                    var value = DeserializeValue(change.value, itemType);
                    coll[index] = value;
                }
                else if (change.op == Operations.move)
                {
                    Guards.ArgumentNotNullOrEmpty(change.from, "Missed 'from' path for operation 'move'");
                    Guards.ArgumentPassCondition(index <= coll.Count, $"Position {curPath} out of range for operation 'replace'");

                    var (fromObj, fromPath, _) = JsonPathEx.TravelInto(view, change.from);
                    Guards.ArgumentPassCondition(fromObj == curObj, $"Operation 'move' is implemented only for same collection. Path {change.path} points to other collection than from {change.from}");

                    if (!JsonPathEx.TryParseIndex(fromPath, out var indexFrom))
                        Guards.ArgumentPassCondition(false, $"Field 'from' ({change.from}) should points to element in collection");
                    Guards.ArgumentPassCondition(indexFrom < coll.Count, $"Index in from {fromPath} out of range");
                    Guards.ArgumentPassCondition(indexFrom >= 0, $"Index in from {fromPath} out of range");

                    Move(coll, indexFrom, index);
                }
                else
                {
                    throw new NotImplementedException($"Unexpected operation {change.op}");
                }
                return parentProp;
            }
            else
            {
                Guards.ArgumentPassCondition(change.op != Operations.move, $"Operation 'move' is not supported for path '{change.path}'");

                var prop = curObj.GetType().GetProperty(curPath);
                Guards.ArgumentNotNull(prop, curPath, $"Unknown field {curPath}");
                if (change.op == Operations.remove)
                {
                    prop.SetValue(curObj, null);
                }
                else if (change.op == Operations.replace || change.op == Operations.add)
                {
                    var value = DeserializeValue(change.value, prop.PropertyType);
                    ValidateValue(value, prop, curObj);
                    prop.SetValue(curObj, value);
                }
                else
                {
                    throw new NotImplementedException($"Unexpected operation {change.op}");
                }
                return prop;
            }
        }

        private static void ValidateValue(object? value, PropertyInfo prop, object instance)
        {
            foreach (var att in prop.GetCustomAttributesEx<ValidationAttribute>())
            {
                var res = att.GetValidationResult(value, new ValidationContext(instance) { MemberName = prop.Name });
                if (res?.ErrorMessage != null)
                    throw new ArgumentException(prop.Name, res.ErrorMessage);
            }

            foreach (var attr in prop.GetCustomAttributes<ReadOnlyAttribute>())
            {
                if (attr.IsReadOnly)
                    throw new ArgumentException($"Property {prop.Name} is readonly");
            }
        }

        private static object? DeserializeValue(object? value, Type type)
        {
            if (value is JsonElement jsonElement)
            {
                var res = jsonElement.Deserialize(type);
                return res;
            }
            if (value is JsonArray jsonArray)
            {
                var res = jsonArray.Deserialize(type);
                return res;
            }
            else
            {
                var res = ConvertEx.ChangeType(value, type);
                return res;
            }
        }
    }
}