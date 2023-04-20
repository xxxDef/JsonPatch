using System.Collections;
using System.Reflection;
using System.Text;

namespace Def.JsonPatch
{
    public static class JsonPathEx
    {
        public static string[] Split(string path)
        {
            var strings = new List<string>();
            var sb = new StringBuilder(path.Length);

            for (var i = 0; i < path.Length; i++)
            {
                if (path[i] == '/')
                {
                    if (sb.Length > 0)
                    {
                        strings.Add(sb.ToString());
                        sb.Length = 0;
                    }
                }
                else if (path[i] == '~')
                {
                    ++i;
                    Guards.ArgumentPassCondition(i < path.Length, $"invalid patch {path}");

                    if (path[i] == '0')
                    {
                        sb.Append('~');
                    }
                    else if (path[i] == '1')
                    {
                        sb.Append('/');
                    }
                    else
                    {
                        Guards.ArgumentPassCondition(false, $"invalid patch {path}");
                    }
                }
                else
                {
                    sb.Append(path[i]);
                }
            }

            if (sb.Length > 0)
            {
                strings.Add(sb.ToString());
            }

            return strings.ToArray();
        }

        public static bool TryParseIndex(string p, out int index)
        {
            if (p == "-")
            {
                index = -1;
                return true;
            }
            if (int.TryParse(p, out index))
                return true;

            return false;
        }

        public static (object curObj, string curPath, PropertyInfo? parentProp) TravelInto(object curObj, string path)
        {
            var pathItems = Split(path);
            Guards.ArgumentPassCondition(pathItems.Length > 0, $"Invalid path {path}");

            PropertyInfo? parentProp = null;

            for (var i = 0; i < pathItems.Length - 1; ++i)
            {
                var curPath = pathItems[i];
                Guards.ArgumentNotNull(curObj, $"Part of path {curPath} in path {path} is used for undefined object");

                Guards.ArgumentPassCondition(curPath != "-", $"Index '-' (pointer to non-exsisintg item) in path {path} is used in the middle of path");

                if (int.TryParse(curPath, out var index))
                {
                    var coll = curObj as IList;
                    Guards.ArgumentNotNull(coll, $"Index {index} in path {path} is used for non-collection object");
                    var next = coll[index];
                    Guards.ArgumentNotNull(next, $"path {path} points to null object");
                    curObj = next;
                }
                else
                {
                    parentProp = curObj.GetType().GetProperty(curPath);
                    Guards.ArgumentNotNull(parentProp, $"Part of path {curPath} in path {path} points to undefined property");
                    var next = parentProp.GetValue(curObj);
                    Guards.ArgumentNotNull(next, $"path {path} points to null object");
                    curObj = next;
                }
            }
            var currentPath = pathItems[pathItems.Length - 1];
            return (curObj, currentPath, parentProp);
        }
    }
}