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
    }
}