namespace Def.JsonPatch
{
    public static class ChangeEx
    {
        public static int CalculateChangesHash(this Change[] changes)
        {
            var res = changes.Length;
            foreach (var c in changes)
            {
                res ^= c.op.GetHashCode();
                res ^= c.from?.GetHashCode() ?? 0;
                res ^= c.path?.GetHashCode() ?? 0;
                res ^= c.value?.ToString()?.GetHashCode() ?? 0;
            }
            return res;
        }

        public static void AddRemove(this ICollection<Change> changes, string path) 
        {
            changes.Add(new Change
            {
                path = path,
                op = Operations.remove,
            });
        } 
        public static void AddReplace(this ICollection<Change> changes, string path, object value) 
        {
            changes.Add(new Change
            {
                path = path,
                op = Operations.replace,
                value = value
            });
        }
        public static void AddAdd(this ICollection<Change> changes, string path, object value) 
        {
            changes.Add(new Change
            {
                path = path,
                op = Operations.add,
                value = value
            });
        }
    }
}