namespace Def.JsonPatch
{
    public class JsonPatchArgumentException : ArgumentException
    {
        public JsonPatchArgumentException(Change change, Exception arg) : base("Invalid JSON Patch operation", arg)
        {
            this.Change = change;
        }
        public Change Change { get; }
    }
}