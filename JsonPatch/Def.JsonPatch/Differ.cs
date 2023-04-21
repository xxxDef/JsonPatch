using System.Collections;
using System.Reflection;

namespace Def.JsonPatch
{
    public class Differ
    {
        private readonly DifferStrategies strategies;

        public Differ(DifferStrategies strategies)
        {
            this.strategies = strategies;
        }

        // compare current and new results, apply new results to current and return changes
        public IEnumerable<Change> DiffAndPatch<TCurrent, TNew>(TCurrent? current, TNew changed)
            where TCurrent : class
            where TNew : class
        {
            if (current == null)
            {
                // in this case we fully replace all content of object - use https://json8.github.io/patch/demos/apply/ to test
                return new Change[]
                {
                    new Change
                    {
                        op = Operations.replace,
                        path = "",
                        value = changed
                    }
                };
            }
            return DiffAndPatch(current, changed, "");
        }

        private IEnumerable<Change> DiffAndPatch(object originalContainer, object changedContainer, string parentPath)
        {
            var props = strategies.GetProperties(changedContainer);

            foreach (var changedProperty in props)
            {
                if (strategies.Skip != null && strategies.Skip(changedProperty))
                    continue;

                var originalProperty = strategies.GetSameProperty(originalContainer, changedProperty);
                var originalValue = strategies.GetValue(originalProperty, originalContainer);
                var changedValue = strategies.GetValue(changedProperty, changedContainer);

                var path = $"{parentPath}/{originalProperty.Name}";

                var changes = DiffAndPatchProperty(
                    originalValue, originalContainer, originalProperty,
                    changedValue, changedContainer, changedProperty,
                    path);
                foreach (var c in changes)
                    yield return c;
            }
        }

        IEnumerable<Change> DiffAndPatchProperty(object? originalValue, object originalContainer, PropertyInfo originalProperty, object? changedValue, object changedContainer, PropertyInfo changedProperty, string path)
        {
            if (originalProperty.IsSimpleTypeOrString())
            {
                return DiffAndPatchValue(originalValue, changedValue, originalContainer, originalProperty, path);
            }
            else if (originalProperty.IsAssociativeDictionary())
            {
                var originalDict = originalValue as IDictionary<string, object?>;
                var changedDict = changedValue as IDictionary<string, object?>;

                // TODO:
                Guards.InternalErrorIfNull(originalDict,
                   $"Dictionary {originalProperty.Name} should be initialized in current object");

                return DiffAndPatchDictionary(originalDict, changedDict, path);
            }
            else if (originalProperty.IsEnumerable())
            {
                var originalColl = originalValue as IList;
                Guards.InternalErrorIfNull(originalColl,
                    $"Collection {originalProperty} should implement IList interface");

                var changedColl = changedValue as IEnumerable;

                return DiffAndPatchCollection(originalColl, changedColl, path);
            }
            else
            {
                return DiffAndPatchContainer(originalValue, originalProperty, originalContainer, changedValue, path);
            }
        }

        private IEnumerable<Change> DiffAndPatchValue(object? originalValue,
            object? changedValue,
            object container,
            PropertyInfo property,
            string path)
        {
            if (strategies.AreEquals(originalValue, changedValue))
                yield break;

            if (!strategies.SetValue(property, container, changedValue))
                yield break;

            yield return new Change
            {
                path = path,
                op = Operations.replace,
                value = changedValue
            };
        }

        private IEnumerable<Change> DiffAndPatchContainer(
            object? originalValue,
            PropertyInfo originalProperty,
            object originalContainer,
            object? changedValue,
            string path)
        {
            if (originalValue == null && changedValue == null) // do nothing if both original and changed are null
                yield break;

            if (originalValue != null && changedValue == null)
            {
                // remove old 
                strategies.SetValue(originalProperty, originalContainer, null);
                yield return Change.Remove(path);
            }
            else if (originalValue == null && changedValue != null)
            {
                //add new 
                var newCurrentChild = strategies.Create(originalProperty.PropertyType);
                Guards.InternalErrorIfNull(newCurrentChild, $"failed to create new item of type {originalProperty.PropertyType}");
                strategies.SetValue(originalProperty, originalContainer, newCurrentChild);

                var skipped = DiffAndPatch(newCurrentChild, changedValue, path).ToArray();

                yield return Change.Add(path, originalValue);
            }
            else if (originalValue != null && changedValue != null)
            {
                // update/replace
                foreach (var c in DiffAndPatch(originalValue, changedValue, path))
                    yield return c;
            }
        }

        private IEnumerable<Change> DiffAndPatchDictionary(IDictionary<string, object?> originalDict,
            IDictionary<string, object?>? changedDict,
            string path)
        {
            if (changedDict == null && originalDict == null)
                yield break;

            if (changedDict == null && originalDict != null)
            {
                // clear original dict
                foreach (var item in originalDict)
                {
                    var curpatch = $"{path}/{item.Key}";
                    yield return Change.Remove(curpatch);
                }
                originalDict.Clear();
                yield break;
            }

            Guards.InternalErrorIfFalse(changedDict != null && originalDict != null, $"Unexpected case, both dictionaries should be not null in this place");

            foreach (var curKey in originalDict.Keys)
            {
                var curpatch = $"{path}/{curKey}";
                if (!changedDict.TryGetValue(curKey, out var changedItem) || changedItem == null)
                {
                    // no this item in new dict, remove it
                    originalDict.Remove(curKey);
                    yield return Change.Remove(curpatch);
                }
            }

            // add new and update exsising
            foreach (var changedPair in changedDict)
            {
                var curpatch = $"{path}/{changedPair.Key}";

                if (!originalDict.TryGetValue(changedPair.Key, out var origItem))
                {
                    if (changedPair.Value != null)
                    {
                        // new item, add it
                        var newItem = CreateFrom(changedPair.Value);

                        originalDict.Add(changedPair.Key, newItem);
                        yield return Change.Add(curpatch, newItem);
                    }
                }
                else
                {
                    if (changedPair.Value == null)
                    {
                        // new item is null, remove old item
                        if (origItem != null)
                        {
                            originalDict.Remove(changedPair.Key);
                            yield return Change.Remove(curpatch);
                        }
                    }
                    else if (changedPair.GetType().IsSimpleTypeOrString())
                    {
                        // both items exsist and they are simple
                        if (!strategies.AreEquals(origItem, changedPair.Value))
                        {
                            originalDict[changedPair.Key] = changedPair.Value;
                            yield return Change.Replace(curpatch, changedPair.Value);
                        };
                    }
                    else if (origItem == null)
                    {
                        // original item exsist but null, replace them
                        var newItem = CreateFrom(changedPair.Value);
                        originalDict[changedPair.Key] = newItem;
                        yield return Change.Replace(curpatch, newItem);
                    }
                    else
                    {
                        // both items exsist, value is complex
                        foreach (var c in DiffAndPatch(origItem, changedPair.Value, curpatch))
                            yield return c;
                    }
                }
            }
        }

        private object CreateFrom(object value)
        {
            var newItem = strategies.CreateFrom(value);
            Guards.InternalErrorIfNull(newItem);
            var skipped = DiffAndPatch(newItem, value, "").ToArray();
            return newItem;
        }



        record OldNewPair(object Changed)
        {
            internal object? Original { get; set; }
        }

        private IEnumerable<Change> DiffAndPatchCollection(
            IList originalColl,
            IEnumerable? changedColl,
            string path)
        {
            if (changedColl == null)
            {
                // just remove all items from original collection
                for (int i = 0; i < originalColl.Count;)
                {
                    originalColl.RemoveAt(i);
                    var curpatch = $"{path}/{i}";
                    yield return Change.Remove(curpatch);
                }
                yield break;
            }

            var oldNewPairs = new List<OldNewPair>();
            foreach (var m in changedColl)
            {
                Guards.InternalErrorIfNull(m, $"unexpected null item in collection of type {changedColl.GetType()}");
                oldNewPairs.Add(new OldNewPair(m));
            }

            // First iteration: bind current items to new items and remove missed from current items
            for (int i = 0; i < originalColl.Count;)
            {
                var view = originalColl[i];
                Guards.InternalErrorIfNull(view, $"Unexpected null item in position {i} in collection {originalColl.GetType()}");

                var old = oldNewPairs.FirstOrDefault(mv => mv.Changed != null && strategies.AreSame(mv.Changed, view));
                if (old != null)
                {
                    Guards.InternalErrorIfFalse(old.Original == null, $"Object in old collection for type {view.GetType()} exist more than once.");
                    old.Original = view;
                    ++i;
                }
                else
                {
                    // current item is not exsist in changed collection - remove it
                    originalColl.RemoveAt(i);
                    var curpatch = $"{path}/{i}";
                    yield return Change.Remove(curpatch);
                }
            }

            Guards.InternalErrorIfFalse(originalColl.Count <= oldNewPairs.Count, "currentColl have to contains only items already existing in changedColl");

            // add new items - items which are present only in newCollection
            for (int pos = 0; pos < oldNewPairs.Count; pos++)
            {
                var pair = oldNewPairs[pos];
                if (pair.Original != null)
                    continue;

                // TODO: optimize 
                Guards.InternalErrorIfFalse(originalColl.Count >= pos,
                    $"on this step of DiffAndPatchCollection we expect original colection has more elements than current element in new collection to allow correctly insert new element");

                pair.Original = strategies.CreateFrom(pair.Changed);
                originalColl.Insert(pos, pair.Original);

                var curpatch = $"{path}/{pos}";

                // update current from changed without registry changes in result 
                // this initialized object will be set into value
                if (!pair.Original.GetType().IsSimpleTypeOrString())
                {
                    var skipped = DiffAndPatch(pair.Original, pair.Changed, curpatch).ToArray();
                }

                yield return Change.Add(curpatch, pair.Original);
            }

            Guards.InternalErrorIfFalse(originalColl.Count == oldNewPairs.Count, "viewColl have to contains same items as in modelColl");

            // optimization: detect what way (from top to bottom or bottom to top) will have less movings
            int weight = 0;
            for (int pos = 0; pos < oldNewPairs.Count; pos++)
            {
                var pair = oldNewPairs[pos];
                var viewPos = originalColl.IndexOf(pair.Original);

                if (viewPos < pos)
                    --weight;
                else if (viewPos > pos)
                    ++weight;
            }

            if (weight <= 0)
            {
                for (int p = 0; p < oldNewPairs.Count; p++)
                    foreach (var c in MoveAndUpdate(p))
                        yield return c;
            }
            else
            {
                for (int p = oldNewPairs.Count; p > 0; p--)
                    foreach (var c in MoveAndUpdate(p - 1))
                        yield return c;
            }
            // move and update

            IEnumerable<Change> MoveAndUpdate(int pos)
            {
                var pair = oldNewPairs[pos];
                Guards.InternalErrorIfNull(pair.Changed, $"All views should already created and bound with model");
                Guards.InternalErrorIfNull(pair.Original, "All views should already created and bound with model");

                var oldPos = originalColl.IndexOf(pair.Original);

                var curpatch = $"{path}/{pos}";
                if (oldPos != pos)
                {
                    var fromPath = $"{path}/{oldPos}";
                    originalColl.RemoveAt(oldPos);
                    originalColl.Insert(pos, pair.Original);

                    yield return Change.Move(curpatch, fromPath);
                }

                if (!pair.Original.GetType().IsSimpleTypeOrString())
                {
                    foreach (var c in DiffAndPatch(pair.Original, pair.Changed, curpatch))
                        yield return c;
                }
            }
        }


    }
}