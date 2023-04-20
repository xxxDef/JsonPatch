namespace Def.JsonPatch
{
    public class Change
    {
        public Operations op { get; set; }
        public string? path { get; set; }
        public string? from { get; set; }
        public object? value { get; set; }

        public override string ToString()
        {
            return $"{op}:{path}={value}";
        }

        public static Change Remove(string path)
        {
            return new Change
            {
                op = Operations.remove,
                path = path
            };
        }

        public static Change Add(string path, object? value)
        {
            return new Change
            {
                op = Operations.add,
                path = path,
                value = value
            };
        }

        public static Change Replace(string patch, object value)
        {
            return new Change
            {
                op = Operations.replace,
                path = patch,
                value = value
            };
        }

        public static Change Move(string patch, string fromPath)
        {
            return new Change
            {
                op = Operations.move,
                path = patch,
                from = fromPath
            };
        }
    }

    
}