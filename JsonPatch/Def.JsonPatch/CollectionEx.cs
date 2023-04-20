using System.Collections;

namespace Def.JsonPatch
{
    public static class CollectionsEx
    {
        public static bool AddUnique<TValue>(this HashSet<TValue> coll, TValue value)
        {
            if (coll.Contains(value))
                return false;

            coll.Add(value);
            return true;
        }

        public static void AddRange<T>(this ICollection<T> collection, IEnumerable<T> items)
        {
            foreach (var item in items)
            {
                collection.Add(item);
            }
        }

        public static object AddNew(this IList list)
        {
            var itemType = ReflectionEx.GetItemType(list);
            var newItem = Activator.CreateInstance(itemType);
            Guards.InternalErrorIfNull(newItem);
            list.Add(newItem);
            return newItem;
        }

        public static void Move(this IList list, int positionFrom, int positionTo)
        {
            var modelToMove = list[positionFrom];
            list.RemoveAt(positionFrom);
            list.Insert(positionTo, modelToMove);
        }
    }
}
